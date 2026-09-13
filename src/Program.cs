#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TabBouncer;

// 진입점, 명령줄 인수, 중복 실행 방지, Chrome 연결 수명 주기를 담당한다.
// 판정은 Program.Judge.cs, CDP 이벤트는 Program.Cdp.cs, Chrome 실행은 Program.Chrome.cs,
// 설정·로그·통계는 Program.Storage.cs, GUI가 부르는 기능은 Program.Api.cs에 있다.
internal static partial class Program
{
    private const string AppName = "TabBouncer";
    private const int MaxRecentItems = 50;

    private static string _dataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);
    private static bool _dataDirectoryFromArgument;
    private static string _configDirectory = "";
    private static string ConfigPath => Path.Combine(
        _configDirectory.Length > 0 ? _configDirectory : AppContext.BaseDirectory, "config.json");
    private static string EventLogPath => Path.Combine(_dataDirectory, "events.jsonl");
    private static string LogFilePath => Path.Combine(_dataDirectory, "tabbouncer.log");
    private static string StatsPath => Path.Combine(_dataDirectory, "stats.json");
    private static string PendingUrlPath => Path.Combine(_dataDirectory, "pending-url.txt");
    internal static string UiStatePath => Path.Combine(_dataDirectory, "ui-state.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly ConcurrentDictionary<string, TargetRecord> Targets = new();
    private static readonly ConcurrentDictionary<string, string> TargetsBySession = new();
    private static readonly ConcurrentDictionary<string, PageContext> Pages = new();
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<UserIntent>> Intents = new();
    private static readonly ConcurrentQueue<UserIntent> GlobalIntents = new();
    private static readonly ConcurrentQueue<string> PendingOpenUrls = new();
    private static readonly List<ClosedItem> RecentClosed = new();
    private static readonly object LogLock = new();
    private static readonly CancellationTokenSource ApplicationCancellation = new();

    private static readonly SemaphoreSlim ChromeLaunchRequests = new(0, 1);

    private static Config _config = Config.Defaults();
    private static CdpClient? _cdp;
    private static volatile bool _waitingForChrome;
    private static FileSystemWatcher? _configWatcher;
    private static volatile bool _connected;
    private static string _connectionStatus = "";
    private static string _browserName = "";
    private static string _browserVersion = "";
    private static string _startUrlFromArgument = "";
    private static string[] _launchArguments = Array.Empty<string>();
    private static int _sessionClosedCount;
    private static Mutex? _instanceMutex;
    private static EventWaitHandle? _showRequest;
    private static long Now => Environment.TickCount64;

    internal static event Action<AppLogEntry>? LogEmitted;
    internal static event Action<ClosedItem>? ItemRecorded;
    internal static event Action? ShowRequested;

    internal static bool StartMinimized { get; private set; }

    internal static string Version =>
        typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "dev";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            Console.OutputEncoding = Encoding.UTF8;
            return RunSelfTests();
        }

        _launchArguments = args;
        ApplyDataDirectoryArgument(args);
        Directory.CreateDirectory(_dataDirectory);

        if (!AcquireSingleInstance(args))
            return 0;

        _configDirectory = _dataDirectoryFromArgument ? _dataDirectory : ResolveConfigDirectory();
        LoadOrCreateConfig();
        L.SetLanguage(_config.Language);
        _connectionStatus = L.T("status.preparing");

        // 실행할 때마다 감시는 꺼진 상태로 시작한다. 사용자가 설정에서 명시적으로 켠 경우와
        // 자동화용 --auto-start만 예외다. config.json의 enabled 값은 쓰지 않는다.
        _config.Enabled = _config.StartMonitoringOnLaunch;
        ApplyArguments(args);

        LoadStats();
        LoadRecentFromEvents();
        WriteStartupLog();
        if (!_dataDirectoryFromArgument)
            WindowsIntegration.RefreshAutoRunPath();

        ApplicationConfiguration.Initialize();
        using var window = new MainForm();
        StartShowRequestListener(ApplicationCancellation.Token);
        _ = Task.Run(() => SweepAsync(ApplicationCancellation.Token));
        StartConfigWatcher();
        Task engine = Task.Run(() => RunEngineAsync(ApplicationCancellation.Token));

        Application.Run(window);
        ApplicationCancellation.Cancel();
        try
        {
            engine.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
        }

        _configWatcher?.Dispose();
        GC.KeepAlive(_instanceMutex);
        return 0;
    }

    // 같은 데이터 폴더를 쓰는 인스턴스는 하나만 둔다. 두 번째 실행은 첫 인스턴스 창을 앞으로 가져오고
    // --url= 인수가 있으면 그 주소를 첫 인스턴스의 전용 Chrome에서 열도록 넘긴다.
    private static bool AcquireSingleInstance(string[] args)
    {
        string key = InstanceKey();
        _instanceMutex = new Mutex(true, @"Local\TabBouncer." + key, out bool first);
        _showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\TabBouncer.Show." + key);
        if (first)
            return true;

        string? url = UrlArgument(args);
        if (!string.IsNullOrWhiteSpace(url))
        {
            try
            {
                File.AppendAllText(PendingUrlPath, url + Environment.NewLine, new UTF8Encoding(false));
            }
            catch
            {
            }
        }
        _showRequest.Set();
        return false;
    }

    private static string InstanceKey()
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            Path.GetFullPath(_dataDirectory).TrimEnd('\\', '/').ToLowerInvariant()));
        return Convert.ToHexString(hash)[..16];
    }

    private static void StartShowRequestListener(CancellationToken cancellationToken)
    {
        EventWaitHandle? handle = _showRequest;
        if (handle is null)
            return;

        var thread = new Thread(() =>
        {
            var handles = new[] { handle, cancellationToken.WaitHandle };
            while (WaitHandle.WaitAny(handles) == 0)
            {
                ProcessPendingUrls();
                ShowRequested?.Invoke();
            }
        })
        {
            IsBackground = true,
            Name = "TabBouncer show request"
        };
        thread.Start();
    }

    private static void ProcessPendingUrls()
    {
        string processing = PendingUrlPath + ".processing";
        try
        {
            if (!File.Exists(PendingUrlPath))
                return;
            File.Move(PendingUrlPath, processing, true);
            foreach (string line in File.ReadAllLines(processing, Encoding.UTF8))
            {
                if (!string.IsNullOrWhiteSpace(line))
                    OpenUrl(line.Trim());
            }
            File.Delete(processing);
        }
        catch (Exception ex)
        {
            Warning(L.Format("log.pendingUrlFailed", ex.Message));
        }
    }

    private static async Task RunEngineAsync(CancellationToken cancellationToken)
    {
        bool allowLaunch = _config.AutoLaunchChrome;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                SetConnectionState(false, L.T("status.connecting"));
                await RunSessionAsync(allowLaunch, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Error(L.Format("log.sessionError", ex.Message));
            }

            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                allowLaunch = await WaitForChromeAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        SetConnectionState(false, L.T("status.stopped"));
        Info(L.T("log.exiting"));
    }

    // 사용자가 전용 Chrome을 닫았는데 다시 띄우면 창이 끝없이 되살아난다.
    // 다시 실행 중인 Chrome이 보이면 붙기만 하고, 새로 띄우는 것은 사용자가 요청할 때만 한다.
    private static async Task<bool> WaitForChromeAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        bool announced = false;
        try
        {
            while (true)
            {
                if (await ProbeOwnBrowserAsync(cancellationToken).ConfigureAwait(false) is not null)
                {
                    ChromeLaunchRequests.Wait(0);
                    return false;
                }

                if (!announced)
                {
                    announced = true;
                    _waitingForChrome = true;
                    SetConnectionState(false, L.T("status.chromeClosed"));
                    Info(L.T("log.chromeNotRunning"));
                    PushStateToGuide();
                }

                if (await ChromeLaunchRequests.WaitAsync(5000, cancellationToken).ConfigureAwait(false))
                    return true;
            }
        }
        finally
        {
            _waitingForChrome = false;
        }
    }

    private static string? UrlArgument(IEnumerable<string> args)
    {
        const string prefix = "--url=";
        string? argument = args.LastOrDefault(value =>
            value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return argument is null ? null : Config.NormalizeUrl(argument[prefix.Length..].Trim('"'));
    }

    // 실행할 때 한 번만 적용하는 인수다. 설정을 다시 읽을 때는 ApplyPersistentArguments만 다시 적용한다.
    private static void ApplyArguments(IEnumerable<string> args)
    {
        foreach (string argument in args)
        {
            if (argument.Equals("--live", StringComparison.OrdinalIgnoreCase))
                _config.DryRun = false;
            else if (argument.Equals("--dry-run", StringComparison.OrdinalIgnoreCase))
                _config.DryRun = true;
            else if (argument.Equals("--auto-start", StringComparison.OrdinalIgnoreCase))
                _config.Enabled = true;
            else if (argument.Equals("--minimized", StringComparison.OrdinalIgnoreCase))
                StartMinimized = true;
        }

        _startUrlFromArgument = UrlArgument(args) ?? "";
        ApplyPersistentArguments(args);
    }

    // 설정 파일을 다시 읽어도 명령줄로 지정한 포트·시작 주소·판정 옵션은 유지한다.
    private static void ApplyPersistentArguments(IEnumerable<string> args)
    {
        foreach (string argument in args)
        {
            if (argument.Equals("--strict", StringComparison.OrdinalIgnoreCase))
                _config.StrictMode = true;
            else if (argument.Equals("--no-preempt", StringComparison.OrdinalIgnoreCase))
                _config.PreemptiveBlock = false;
            else if (argument.StartsWith("--port=", StringComparison.OrdinalIgnoreCase) &&
                     int.TryParse(argument[7..], out int port))
                _config.DebugPort = port;
        }

        if (UrlArgument(args) is { Length: > 0 } url)
            _config.StartUrl = url;
    }

    private static void ApplyDataDirectoryArgument(IEnumerable<string> args)
    {
        const string prefix = "--data-dir=";
        string? argument = args.LastOrDefault(value =>
            value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (argument is null)
            return;

        string path = Environment.ExpandEnvironmentVariables(argument[prefix.Length..].Trim('"'));
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("--data-dir 경로가 비어 있다.");
        _dataDirectory = Path.GetFullPath(path);
        _dataDirectoryFromArgument = true;
    }

    private static async Task RunSessionAsync(bool allowLaunch, CancellationToken cancellationToken)
    {
        string? webSocketUrl = await ProbeOwnBrowserAsync(cancellationToken).ConfigureAwait(false);
        bool launched = false;

        if (webSocketUrl is null)
        {
            if (!allowLaunch)
                return;

            LaunchChrome();
            launched = true;
            for (int attempt = 0; attempt < 40 && webSocketUrl is null; attempt++)
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                webSocketUrl = await ProbeOwnBrowserAsync(cancellationToken).ConfigureAwait(false);
            }

            if (webSocketUrl is null)
                throw new TimeoutException(L.T("error.chromeConnectTimeout"));
        }
        else
        {
            Info(L.T("log.attachExisting"));
        }

        Targets.Clear();
        TargetsBySession.Clear();
        Pages.Clear();
        Intents.Clear();
        while (GlobalIntents.TryDequeue(out _))
        {
        }
        _isolatedWorldWarningShown = false;

        await using var client = new CdpClient();
        _cdp = client;
        var disconnected = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        client.Closed += exception =>
        {
            string detail = exception is null ? "" : " (" + exception.Message + ")";
            SetConnectionState(false, L.T("status.disconnected"));
            Warning(L.T("log.disconnected") + detail);
            disconnected.TrySetResult(true);
        };
        client.EventReceived += OnCdpEvent;

        await client.ConnectAsync(webSocketUrl, cancellationToken).ConfigureAwait(false);
        await ReadBrowserVersionAsync(client).ConfigureAwait(false);
        Success(L.T("log.connected") + (_browserVersion.Length > 0 ? " (" + _browserVersion + ")" : ""));
        SetConnectionState(true, L.T("status.connected"));

        await client.SendAsync(
            "Target.setDiscoverTargets",
            new JsonObject { ["discover"] = true }).ConfigureAwait(false);

        // 연결 전부터 열려 있던 탭은 새 팝업이 아니다. 판정 대상에서 빼고 현재 URL을 신뢰 기준으로 삼는다.
        JsonNode? existing = await client.SendAsync("Target.getTargets").ConfigureAwait(false);
        foreach (JsonNode? targetInfo in existing?["targetInfos"]?.AsArray() ?? new JsonArray())
            AdoptExistingPage(targetInfo);

        await client.SendAsync(
            "Target.setAutoAttach",
            new JsonObject
            {
                ["autoAttach"] = true,
                ["waitForDebuggerOnStart"] = _config.PreemptiveBlock,
                ["flatten"] = true
            }).ConfigureAwait(false);

        Info(L.T(_config.PreemptiveBlock ? "log.trackingWithPreempt" : "log.tracking"));

        if (!launched && _startUrlFromArgument.Length > 0)
            PendingOpenUrls.Enqueue(_startUrlFromArgument);
        _startUrlFromArgument = "";
        OpenPendingUrls();

        using (cancellationToken.Register(() => disconnected.TrySetResult(true)))
            await disconnected.Task.ConfigureAwait(false);

        _cdp = null;
    }

    private static void OpenPendingUrls()
    {
        CdpClient? client = _cdp;
        if (client is null)
            return;
        while (PendingOpenUrls.TryDequeue(out string? url))
        {
            client.Fire("Target.createTarget", new JsonObject { ["url"] = url, ["newWindow"] = false });
            Info(L.Format("log.urlOpened", Shorten(url)));
        }
    }

    private static async Task SweepAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                long now = Now;
                foreach (PageContext page in Pages.Values.ToArray())
                {
                    if (page.PausedSessionId is { } sessionId && now - page.PausedAt > 1600)
                    {
                        Resume(sessionId);
                        page.PausedSessionId = null;
                    }

                    if (page.Decided == 0 && now - page.CreatedAt >= _config.DebounceMs)
                        await EvaluateAsync(page, "debounce").ConfigureAwait(false);
                }

                foreach (ConcurrentQueue<UserIntent> queue in Intents.Values)
                    PruneIntentQueue(queue, now);
                PruneIntentQueue(GlobalIntents, now);
            }
            catch
            {
            }

            try
            {
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static void SetConnectionState(bool connected, string status)
    {
        _connected = connected;
        _connectionStatus = status;
    }
}
