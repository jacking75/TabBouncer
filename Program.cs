#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace TabBouncer;

internal sealed class Config
{
    public bool Enabled { get; set; } = true;
    public bool DryRun { get; set; }
    public bool StrictMode { get; set; }
    public bool PreemptiveBlock { get; set; } = true;
    public bool RefocusOpener { get; set; } = true;
    public bool BlockAutomaticCrossSitePopups { get; set; } = true;
    public bool ProtectExplicitClicks { get; set; } = true;

    public int CloseThreshold { get; set; } = 80;
    public int DebounceMs { get; set; } = 1200;
    public int IntentWindowMs { get; set; } = 3500;
    public int DebugPort { get; set; } = 9222;
    public bool AutoLaunchChrome { get; set; } = true;
    public string ChromePath { get; set; } = "";
    public string UserDataDir { get; set; } = "";
    public string StartUrl { get; set; } = "";
    public List<string> ChromeArguments { get; set; } = new();

    public List<string> WatchedSites { get; set; } = new();
    public List<string> AdDomains { get; set; } = new();
    public List<string> AllowedSites { get; set; } = new();
    public List<string> Whitelist { get; set; } = new();
    public List<string> SuspiciousTlds { get; set; } = new();

    public static Config Defaults() => new()
    {
        AdDomains = new()
        {
            "popads.net", "popcash.net", "propellerads.com", "adsterra.com",
            "exoclick.com", "exdynsrv.com", "hilltopads.net", "adcash.com",
            "clickadu.com", "trafficstars.com", "juicyads.com", "bodelen.com",
            "onclickalgo.com", "onclckpro.com", "onclasrv.com", "bidgear.com",
            "doubleclick.net", "adnxs.com", "adsrvr.org", "mgid.com",
            "revcontent.com", "taboola.com", "outbrain.com", "zeropark.com",
            "clickaine.com", "popunder.net", "richads.com", "monetag.com"
        },
        Whitelist = new()
        {
            "accounts.google.com", "login.microsoftonline.com", "appleid.apple.com",
            "github.com", "nid.naver.com", "accounts.kakao.com", "toss.im",
            "kftc.or.kr", "paypal.com"
        },
        SuspiciousTlds = new()
        {
            "top", "xyz", "buzz", "click", "link", "cyou", "icu", "sbs",
            "rest", "lol"
        }
    };
}

internal sealed class TargetRecord
{
    public string TargetId = "";
    public string Type = "";
    public string Url = "";
    public string? OpenerId;
    public string? SessionId;
}

internal sealed class PageContext
{
    public string TargetId = "";
    public string? OpenerId;
    public string InitialUrl = "";
    public long CreatedAt;
    public int Decided;
    public bool WindowChecked;
    public bool PopupLikely;
    public int Redirects;
    public string? PausedSessionId;
    public long PausedAt;
    public bool UserApproved;
    public string ApprovalReason = "";
}

internal sealed record UserIntent(
    string TargetId,
    string Kind,
    string Url,
    string PageUrl,
    string Label,
    bool OpensNewContext,
    long ReceivedAt);

internal readonly record struct IntentAssessment(
    bool Approved,
    bool Unexpected,
    bool Automatic,
    bool UserControl,
    string Reason)
{
    public static IntentAssessment Explicit(string reason) => new(true, false, false, false, reason);
    public static IntentAssessment Mismatch(string reason) => new(false, true, false, false, reason);
    public static IntentAssessment Auto(string reason) => new(false, false, true, false, reason);
    public static IntentAssessment Control(string reason) => new(false, false, false, true, reason);
    public static IntentAssessment Neutral(string reason) => new(false, false, false, false, reason);
}

internal sealed record ClosedItem(string Url, string OpenerUrl, int Score, string Reason, DateTime At);

internal sealed class CdpClient : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> _pending = new();
    private CancellationTokenSource? _receiveCancellation;
    private int _nextId;

    public event Action<string, JsonNode?, string?>? EventReceived;
    public event Action<Exception?>? Closed;

    public async Task ConnectAsync(string webSocketUrl, CancellationToken cancellationToken)
    {
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        await _socket.ConnectAsync(new Uri(webSocketUrl), cancellationToken).ConfigureAwait(false);
        _receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => ReceiveLoopAsync(_receiveCancellation.Token));
    }

    public async Task<JsonNode?> SendAsync(
        string method,
        JsonObject? parameters = null,
        string? sessionId = null,
        int timeoutMs = 5000)
    {
        int id = Interlocked.Increment(ref _nextId);
        var message = new JsonObject
        {
            ["id"] = id,
            ["method"] = method
        };
        if (parameters is not null)
            message["params"] = parameters;
        if (sessionId is not null)
            message["sessionId"] = sessionId;

        var completion = new TaskCompletionSource<JsonNode?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;

        byte[] bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_socket.State != WebSocketState.Open)
                throw new InvalidOperationException("CDP WebSocket이 닫혀 있다.");

            await _socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }
        finally
        {
            _sendLock.Release();
        }

        using var timeout = new CancellationTokenSource(timeoutMs);
        using var registration = timeout.Token.Register(() =>
        {
            if (_pending.TryRemove(id, out var timedOut))
                timedOut.TrySetException(new TimeoutException($"CDP 명령 시간 초과: {method}"));
        });
        return await completion.Task.ConfigureAwait(false);
    }

    public void Fire(string method, JsonObject? parameters = null, string? sessionId = null)
    {
        _ = SendAsync(method, parameters, sessionId).ContinueWith(
            _ => { },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        using var stream = new MemoryStream();
        Exception? failure = null;

        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   _socket.State == WebSocketState.Open)
            {
                stream.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                        throw new WebSocketException("Chrome이 CDP 연결을 종료했다.");
                    stream.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                JsonNode? node;
                try
                {
                    node = JsonNode.Parse(Encoding.UTF8.GetString(
                        stream.GetBuffer(), 0, checked((int)stream.Length)));
                }
                catch (JsonException)
                {
                    continue;
                }

                if (node is null)
                    continue;

                if (node["id"] is JsonNode idNode)
                {
                    int id = idNode.GetValue<int>();
                    if (!_pending.TryRemove(id, out var completion))
                        continue;

                    if (node["error"] is JsonNode error)
                        completion.TrySetException(new InvalidOperationException(error.ToJsonString()));
                    else
                        completion.TrySetResult(node["result"]);
                    continue;
                }

                string? method = node["method"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(method))
                {
                    EventReceived?.Invoke(
                        method,
                        node["params"],
                        node["sessionId"]?.GetValue<string>());
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            foreach (var item in _pending)
                item.Value.TrySetCanceled();
            _pending.Clear();
            Closed?.Invoke(failure);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _receiveCancellation?.Cancel();
        }
        catch
        {
        }

        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "TabBouncer 종료",
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
        }

        _receiveCancellation?.Dispose();
        _socket.Dispose();
        _sendLock.Dispose();
    }
}

internal static class Program
{
    private const string AppName = "TabBouncer";
    private const string IntentBindingName = "__tabBouncerIntent";

    private static string _dataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);
    private static string _configDirectory = AppContext.BaseDirectory;
    private static string ConfigPath => Path.Combine(_configDirectory, "config.json");
    private static string EventLogPath => Path.Combine(_dataDirectory, "events.jsonl");

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
    private static readonly List<ClosedItem> RecentClosed = new();
    private static readonly object LogLock = new();
    private static readonly CancellationTokenSource ApplicationCancellation = new();

    private static Config _config = Config.Defaults();
    private static CdpClient? _cdp;
    private static FileSystemWatcher? _configWatcher;
    private static long Now => Environment.TickCount64;

    private const string ClickTrackerScript = """
        (() => {
          const installed = '__tabBouncerIntentTrackerInstalledV1';
          if (document[installed]) return;
          Object.defineProperty(document, installed, { value: true });

          const binding = globalThis.__tabBouncerIntent;
          if (typeof binding !== 'function') return;

          const clean = value => String(value || '').replace(/\s+/g, ' ').trim().slice(0, 160);
          const send = value => {
            try {
              binding(JSON.stringify({
                ...value,
                pageUrl: location.href,
                at: Date.now()
              }));
            } catch (_) {}
          };

          const pathElement = (event, selector) => {
            for (const item of event.composedPath()) {
              if (item instanceof Element && item.matches(selector)) return item;
            }
            return null;
          };

          const labelOf = element => clean(
            element.innerText ||
            element.getAttribute('aria-label') ||
            element.getAttribute('title') ||
            element.querySelector('img')?.getAttribute('alt') || ''
          );

          const visiblyDescribed = element => {
            const style = getComputedStyle(element);
            const rect = element.getBoundingClientRect();
            return style.display !== 'none' &&
              style.visibility !== 'hidden' &&
              Number(style.opacity || 1) > 0.05 &&
              rect.width >= 4 && rect.height >= 4 &&
              labelOf(element).length > 0;
          };

          const recordPointer = event => {
            if (!event.isTrusted) return;

            const link = pathElement(event, 'a[href],area[href]');
            if (link) {
              const explicit = visiblyDescribed(link);
              send({
                kind: explicit ? 'link' : 'passive-link',
                url: link.href || '',
                label: labelOf(link),
                opensNewContext: link.target === '_blank' || event.button === 1 ||
                  event.ctrlKey || event.metaKey || event.shiftKey
              });
              return;
            }

            const control = pathElement(event,
              'button,input,select,textarea,[role="button"],[role="link"],[contenteditable="true"]');
            if (control) {
              send({
                kind: 'control',
                url: '',
                label: labelOf(control) || clean(control.value),
                opensNewContext: false
              });
              return;
            }

            send({
              kind: 'passive',
              url: '',
              label: '',
              opensNewContext: false
            });
          };

          addEventListener('pointerdown', recordPointer, true);
          addEventListener('auxclick', recordPointer, true);
          addEventListener('click', event => {
            if (event.detail === 0) recordPointer(event);
          }, true);

          addEventListener('submit', event => {
            if (!event.isTrusted || !(event.target instanceof HTMLFormElement)) return;
            const form = event.target;
            const submitter = event.submitter;
            send({
              kind: 'form',
              url: submitter?.formAction || form.action || location.href,
              label: submitter ? labelOf(submitter) : '',
              opensNewContext: (submitter?.formTarget || form.target) === '_blank'
            });
          }, true);
        })();
        """;

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
            return RunSelfTests();

        Console.Title = AppName;
        ApplyDataDirectoryArgument(args);
        Directory.CreateDirectory(_dataDirectory);
        LoadOrCreateConfig();
        ApplyArguments(args);

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            ApplicationCancellation.Cancel();
        };

        PrintBanner();
        _ = Task.Run(ReadKeys);
        _ = Task.Run(() => SweepAsync(ApplicationCancellation.Token));
        StartConfigWatcher();

        while (!ApplicationCancellation.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(ApplicationCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Error("세션 오류: " + ex.Message);
            }

            if (ApplicationCancellation.IsCancellationRequested)
                break;

            Info("5초 후 Chrome에 다시 연결한다.");
            try
            {
                await Task.Delay(5000, ApplicationCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _configWatcher?.Dispose();
        Info("종료한다.");
        return 0;
    }

    private static void ApplyArguments(IEnumerable<string> args)
    {
        foreach (string argument in args)
        {
            if (argument.Equals("--live", StringComparison.OrdinalIgnoreCase))
                _config.DryRun = false;
            else if (argument.Equals("--dry-run", StringComparison.OrdinalIgnoreCase))
                _config.DryRun = true;
            else if (argument.Equals("--strict", StringComparison.OrdinalIgnoreCase))
                _config.StrictMode = true;
            else if (argument.Equals("--no-preempt", StringComparison.OrdinalIgnoreCase))
                _config.PreemptiveBlock = false;
            else if (argument.StartsWith("--port=", StringComparison.OrdinalIgnoreCase) &&
                     int.TryParse(argument[7..], out int port))
                _config.DebugPort = port;
            else if (argument.StartsWith("--url=", StringComparison.OrdinalIgnoreCase))
                _config.StartUrl = argument[6..];
        }
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
        _configDirectory = _dataDirectory;
    }

    private static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("""

          ╔══════════════════════════════════════════════════╗
          ║   T A B   B O U N C E R                          ║
          ║   원해서 연 탭은 두고, 자동 광고 탭은 내보낸다   ║
          ╚══════════════════════════════════════════════════╝
        """);
        Console.ResetColor();
        Console.WriteLine($"  설정 : {ConfigPath}");
        Console.WriteLine($"  로그 : {EventLogPath}");
        Console.WriteLine($"  모드 : {(_config.DryRun ? "DRY-RUN (관측만)" : "LIVE (실제 종료)")}"
                          + $" | 임계값 {_config.CloseThreshold}"
                          + $" | 클릭 보호 {(_config.ProtectExplicitClicks ? "ON" : "OFF")}");
        Console.WriteLine($"  감시 : {(_config.WatchedSites.Count == 0 ? "자동 판정" : string.Join(", ", _config.WatchedSites))}");
        Console.WriteLine("  키   : [d]ry-run [p]ause [u]ndo [l]ist [w]정상 등록 [r]eload [q]uit");
        Console.WriteLine(new string('─', 68));
    }

    private static async Task RunSessionAsync(CancellationToken cancellationToken)
    {
        string? webSocketUrl = await ProbeBrowserWebSocketAsync(
            _config.DebugPort, cancellationToken).ConfigureAwait(false);

        if (webSocketUrl is null)
        {
            if (!_config.AutoLaunchChrome)
            {
                throw new InvalidOperationException(
                    $"127.0.0.1:{_config.DebugPort}에 디버깅 가능한 Chrome이 없다.");
            }

            LaunchChrome();
            for (int attempt = 0; attempt < 40 && webSocketUrl is null; attempt++)
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                webSocketUrl = await ProbeBrowserWebSocketAsync(
                    _config.DebugPort, cancellationToken).ConfigureAwait(false);
            }

            if (webSocketUrl is null)
                throw new TimeoutException("Chrome 디버깅 포트에 연결하지 못했다.");
        }
        else
        {
            Info("이미 실행 중인 디버깅 Chrome에 접속한다.");
        }

        Targets.Clear();
        TargetsBySession.Clear();
        Pages.Clear();
        Intents.Clear();
        while (GlobalIntents.TryDequeue(out _))
        {
        }

        await using var client = new CdpClient();
        _cdp = client;
        var disconnected = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        client.Closed += exception =>
        {
            string detail = exception is null ? "" : " (" + exception.Message + ")";
            Warning("Chrome 연결이 끊어졌다." + detail);
            disconnected.TrySetResult(true);
        };
        client.EventReceived += OnCdpEvent;

        await client.ConnectAsync(webSocketUrl, cancellationToken).ConfigureAwait(false);
        Success("Chrome에 연결했다.");

        await client.SendAsync(
            "Target.setDiscoverTargets",
            new JsonObject { ["discover"] = true }).ConfigureAwait(false);

        await client.SendAsync(
            "Target.setAutoAttach",
            new JsonObject
            {
                ["autoAttach"] = true,
                ["waitForDebuggerOnStart"] = _config.PreemptiveBlock,
                ["flatten"] = true
            }).ConfigureAwait(false);

        Info(_config.PreemptiveBlock
            ? "선차단과 사용자 클릭 추적을 시작했다."
            : "사용자 클릭 추적을 시작했다.");

        using (cancellationToken.Register(() => disconnected.TrySetResult(true)))
            await disconnected.Task.ConfigureAwait(false);

        _cdp = null;
    }

    private static async Task<string?> ProbeBrowserWebSocketAsync(
        int port,
        CancellationToken cancellationToken)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            string json = await http.GetStringAsync(
                $"http://127.0.0.1:{port}/json/version", cancellationToken).ConfigureAwait(false);
            return JsonNode.Parse(json)?["webSocketDebuggerUrl"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    private static void LaunchChrome()
    {
        string chrome = ResolveChromePath();
        string profile = string.IsNullOrWhiteSpace(_config.UserDataDir)
            ? Path.Combine(_dataDirectory, "ChromeProfile")
            : Environment.ExpandEnvironmentVariables(_config.UserDataDir);
        Directory.CreateDirectory(profile);

        var startInfo = new ProcessStartInfo(chrome)
        {
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add($"--remote-debugging-port={_config.DebugPort}");
        startInfo.ArgumentList.Add("--remote-debugging-address=127.0.0.1");
        startInfo.ArgumentList.Add($"--user-data-dir={profile}");
        startInfo.ArgumentList.Add("--no-first-run");
        startInfo.ArgumentList.Add("--no-default-browser-check");
        startInfo.ArgumentList.Add("--restore-last-session");
        foreach (string argument in _config.ChromeArguments.Where(value =>
                     !string.IsNullOrWhiteSpace(value)))
        {
            startInfo.ArgumentList.Add(argument);
        }
        if (!string.IsNullOrWhiteSpace(_config.StartUrl))
            startInfo.ArgumentList.Add(_config.StartUrl);

        Info("Chrome을 실행한다: " + chrome);
        Info("전용 프로필: " + profile);
        Process.Start(startInfo);
    }

    private static string ResolveChromePath()
    {
        string configured = Environment.ExpandEnvironmentVariables(_config.ChromePath);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        string[] candidates =
        {
            Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles") ?? "",
                @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? "",
                @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Google\Chrome\Application\chrome.exe")
        };

        foreach (string candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            "chrome.exe를 찾지 못했다. config.json의 chromePath에 경로를 지정해야 한다.");
    }

    private static void OnCdpEvent(string method, JsonNode? parameters, string? sessionId)
    {
        try
        {
            switch (method)
            {
                case "Target.targetCreated":
                case "Target.targetInfoChanged":
                    HandleTargetInfo(method, parameters?["targetInfo"]);
                    break;

                case "Target.attachedToTarget":
                    HandleAttachedTarget(parameters);
                    break;

                case "Runtime.bindingCalled":
                    HandleIntentBinding(parameters, sessionId);
                    break;

                case "Page.frameNavigated":
                    HandleFrameNavigated(parameters, sessionId);
                    break;

                case "Network.requestWillBeSent":
                    HandleDocumentRequest(parameters, sessionId);
                    break;

                case "Target.targetDestroyed":
                    HandleTargetDestroyed(parameters);
                    break;

                case "Target.detachedFromTarget":
                    HandleDetachedTarget(parameters);
                    break;
            }
        }
        catch (Exception ex)
        {
            Error($"CDP 이벤트 처리 오류({method}): {ex.Message}");
        }
    }

    private static void HandleTargetInfo(string method, JsonNode? targetInfo)
    {
        if (targetInfo is null || targetInfo["targetId"] is null)
            return;

        string targetId = targetInfo["targetId"]!.GetValue<string>();
        var target = Targets.GetOrAdd(
            targetId,
            static id => new TargetRecord { TargetId = id });
        target.Type = targetInfo["type"]?.GetValue<string>() ?? "";

        string url = targetInfo["url"]?.GetValue<string>() ?? "";
        if (!string.IsNullOrEmpty(url))
            target.Url = url;

        string? openerId = targetInfo["openerId"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(openerId))
            target.OpenerId = openerId;

        if (!target.Type.Equals("page", StringComparison.Ordinal))
            return;

        var page = Pages.GetOrAdd(
            targetId,
            _ => new PageContext
            {
                TargetId = targetId,
                OpenerId = target.OpenerId,
                InitialUrl = target.Url,
                CreatedAt = Now
            });
        page.OpenerId ??= target.OpenerId;

        if (method == "Target.targetInfoChanged" && !IsBlank(target.Url))
            _ = EvaluateAsync(page, "targetInfoChanged");
    }

    private static void HandleAttachedTarget(JsonNode? parameters)
    {
        JsonNode? targetInfo = parameters?["targetInfo"];
        string? sessionId = parameters?["sessionId"]?.GetValue<string>();
        if (targetInfo is null || string.IsNullOrEmpty(sessionId) || targetInfo["targetId"] is null)
            return;

        string targetId = targetInfo["targetId"]!.GetValue<string>();
        string type = targetInfo["type"]?.GetValue<string>() ?? "";
        string url = targetInfo["url"]?.GetValue<string>() ?? "";
        string? openerId = targetInfo["openerId"]?.GetValue<string>();
        bool waiting = parameters?["waitingForDebugger"]?.GetValue<bool>() ?? false;

        var target = Targets.GetOrAdd(
            targetId,
            static id => new TargetRecord { TargetId = id });
        target.Type = type;
        target.SessionId = sessionId;
        if (!string.IsNullOrEmpty(url))
            target.Url = url;
        if (!string.IsNullOrEmpty(openerId))
            target.OpenerId = openerId;
        TargetsBySession[sessionId] = targetId;

        if (!type.Equals("page", StringComparison.Ordinal))
        {
            if (waiting)
                Resume(sessionId);
            return;
        }

        var page = Pages.GetOrAdd(
            targetId,
            _ => new PageContext
            {
                TargetId = targetId,
                OpenerId = target.OpenerId,
                InitialUrl = target.Url,
                CreatedAt = Now
            });
        page.OpenerId ??= target.OpenerId;
        if (waiting)
        {
            page.PausedSessionId = sessionId;
            page.PausedAt = Now;
        }

        _ = InitializeAttachedPageAsync(page, sessionId, waiting);
    }

    private static async Task InitializeAttachedPageAsync(
        PageContext page,
        string sessionId,
        bool waiting)
    {
        try
        {
            CdpClient? client = _cdp;
            if (client is null)
                return;

            if (waiting)
            {
                // 클릭 이벤트와 새 Target 이벤트는 서로 다른 CDP 세션에서 올 수 있다.
                // 아주 짧은 유예를 둬 클릭 의도가 먼저 기록되도록 한다.
                await Task.Delay(150).ConfigureAwait(false);
                bool closed = await EvaluateAsync(page, "preempt").ConfigureAwait(false);
                Resume(sessionId);
                page.PausedSessionId = null;
                if (closed)
                    return;
            }

            await client.SendAsync("Runtime.enable", null, sessionId, 1500).ConfigureAwait(false);
            await client.SendAsync(
                "Runtime.addBinding",
                new JsonObject { ["name"] = IntentBindingName },
                sessionId,
                1500).ConfigureAwait(false);
            await client.SendAsync("Page.enable", null, sessionId, 1500).ConfigureAwait(false);
            await client.SendAsync("Network.enable", null, sessionId, 1500).ConfigureAwait(false);
            await client.SendAsync(
                "Page.addScriptToEvaluateOnNewDocument",
                new JsonObject { ["source"] = ClickTrackerScript },
                sessionId,
                1500).ConfigureAwait(false);

            client.Fire(
                "Runtime.evaluate",
                new JsonObject
                {
                    ["expression"] = ClickTrackerScript,
                    ["returnByValue"] = false
                },
                sessionId);

        }
        catch (Exception ex)
        {
            Warning("페이지 감시 초기화 실패: " + ex.Message);
        }
        finally
        {
            if (page.PausedSessionId is not null)
            {
                Resume(sessionId);
                page.PausedSessionId = null;
            }
        }
    }

    private static void HandleIntentBinding(JsonNode? parameters, string? sessionId)
    {
        if (!_config.ProtectExplicitClicks || string.IsNullOrEmpty(sessionId))
            return;
        if (!string.Equals(
                parameters?["name"]?.GetValue<string>(),
                IntentBindingName,
                StringComparison.Ordinal))
            return;
        if (!TargetsBySession.TryGetValue(sessionId, out string? targetId))
            return;

        string payload = parameters?["payload"]?.GetValue<string>() ?? "";
        JsonNode? value;
        try
        {
            value = JsonNode.Parse(payload);
        }
        catch (JsonException)
        {
            return;
        }

        if (value is null)
            return;

        var intent = new UserIntent(
            targetId,
            value["kind"]?.GetValue<string>() ?? "passive",
            value["url"]?.GetValue<string>() ?? "",
            value["pageUrl"]?.GetValue<string>() ?? "",
            value["label"]?.GetValue<string>() ?? "",
            value["opensNewContext"]?.GetValue<bool>() ?? false,
            Now);

        ConcurrentQueue<UserIntent> queue = Intents.GetOrAdd(
            targetId, static _ => new ConcurrentQueue<UserIntent>());
        queue.Enqueue(intent);
        GlobalIntents.Enqueue(intent);
        PruneIntentQueue(queue, Now);
        PruneIntentQueue(GlobalIntents, Now);
    }

    private static void HandleFrameNavigated(JsonNode? parameters, string? sessionId)
    {
        JsonNode? frame = parameters?["frame"];
        if (frame is null || frame["parentId"] is not null || string.IsNullOrEmpty(sessionId))
            return;
        if (!TargetsBySession.TryGetValue(sessionId, out string? targetId))
            return;

        string url = frame["url"]?.GetValue<string>() ?? "";
        if (IsBlank(url))
            return;

        if (Targets.TryGetValue(targetId, out var target))
            target.Url = url;
        if (Pages.TryGetValue(targetId, out var page))
        {
            page.Redirects++;
            _ = EvaluateAsync(page, "frameNavigated");
        }
    }

    private static void HandleDocumentRequest(JsonNode? parameters, string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId) ||
            !string.Equals(parameters?["type"]?.GetValue<string>(), "Document",
                StringComparison.Ordinal) ||
            !TargetsBySession.TryGetValue(sessionId, out string? targetId))
        {
            return;
        }

        string frameId = parameters?["frameId"]?.GetValue<string>() ?? "";
        string requestId = parameters?["requestId"]?.GetValue<string>() ?? "";
        string loaderId = parameters?["loaderId"]?.GetValue<string>() ?? "";
        bool looksLikeMainDocument = frameId.Equals(targetId, StringComparison.Ordinal) ||
                                     (requestId.Length > 0 &&
                                      requestId.Equals(loaderId, StringComparison.Ordinal));
        if (!looksLikeMainDocument)
            return;

        string url = parameters?["request"]?["url"]?.GetValue<string>() ?? "";
        if (IsBlank(url) || !Pages.TryGetValue(targetId, out var page))
            return;

        if (parameters?["redirectResponse"] is not null)
            page.Redirects++;

        IntentAssessment intent = AssessIntent(page, url);
        if (intent.Approved)
        {
            page.UserApproved = true;
            page.ApprovalReason = intent.Reason;
        }

        if (Targets.TryGetValue(targetId, out var target))
            target.Url = url;
        _ = EvaluateAsync(page, "documentRequest");
    }

    private static void HandleTargetDestroyed(JsonNode? parameters)
    {
        string? targetId = parameters?["targetId"]?.GetValue<string>();
        if (string.IsNullOrEmpty(targetId))
            return;

        if (Targets.TryRemove(targetId, out var target) && target.SessionId is not null)
            TargetsBySession.TryRemove(target.SessionId, out _);
        Pages.TryRemove(targetId, out _);
        Intents.TryRemove(targetId, out _);
    }

    private static void HandleDetachedTarget(JsonNode? parameters)
    {
        string? sessionId = parameters?["sessionId"]?.GetValue<string>();
        if (string.IsNullOrEmpty(sessionId))
            return;

        if (TargetsBySession.TryRemove(sessionId, out string? targetId) &&
            Targets.TryGetValue(targetId, out var target))
        {
            target.SessionId = null;
        }
    }

    private static void Resume(string sessionId)
    {
        _cdp?.Fire("Runtime.runIfWaitingForDebugger", null, sessionId);
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

                    if (now - page.CreatedAt > 15000)
                        Pages.TryRemove(page.TargetId, out _);
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

    private static void PruneIntentQueue(ConcurrentQueue<UserIntent> queue, long now)
    {
        long retention = Math.Max(_config.IntentWindowMs * 2L, 10000L);
        while (queue.TryPeek(out UserIntent? oldest) && now - oldest.ReceivedAt > retention)
            queue.TryDequeue(out _);
    }

    private static async Task<bool> EvaluateAsync(PageContext page, string stage)
    {
        CdpClient? client = _cdp;
        if (!_config.Enabled || client is null || page.Decided != 0)
            return false;

        string url = Targets.TryGetValue(page.TargetId, out var current)
            ? current.Url
            : page.InitialUrl;
        string openerUrl = page.OpenerId is not null &&
                           Targets.TryGetValue(page.OpenerId, out var opener)
            ? opener.Url
            : "";

        IntentAssessment intent = AssessIntent(page, url);
        if (intent.Approved)
        {
            page.UserApproved = true;
            page.ApprovalReason = intent.Reason;
        }

        if (stage == "debounce" && !page.WindowChecked)
        {
            page.WindowChecked = true;
            try
            {
                JsonNode? result = await client.SendAsync(
                    "Browser.getWindowForTarget",
                    new JsonObject { ["targetId"] = page.TargetId },
                    null,
                    1500).ConfigureAwait(false);
                JsonNode? bounds = result?["bounds"];
                int width = bounds?["width"]?.GetValue<int>() ?? 0;
                int height = bounds?["height"]?.GetValue<int>() ?? 0;
                page.PopupLikely = width > 0 && height > 0 && (width < 1000 || height < 780);
            }
            catch
            {
            }
        }

        (int score, string reason) = Score(url, openerUrl, page, intent, _config);

        if (score <= -900)
        {
            if (intent.Approved ||
                (stage == "debounce" && reason != "blank-pending"))
                Interlocked.Exchange(ref page.Decided, 1);

            if (intent.Approved)
            {
                Info($"사용자 요청 탭 유지 [{intent.Reason}] {Shorten(url)}");
                WriteEvent(new
                {
                    stage = "user-approved",
                    url,
                    openerUrl,
                    reason = intent.Reason
                });
            }
            return false;
        }

        if (score >= _config.CloseThreshold - 30)
        {
            WriteEvent(new
            {
                stage,
                score,
                reason,
                url,
                openerUrl,
                initialUrl = page.InitialUrl,
                page.PopupLikely,
                page.Redirects,
                intent = intent.Reason,
                ageMs = Now - page.CreatedAt
            });
        }

        if (score < _config.CloseThreshold)
        {
            if (stage == "debounce")
                Interlocked.Exchange(ref page.Decided, 1);
            if (score >= _config.CloseThreshold - 30)
                Info($"유지 {score}점 [{reason}] {Shorten(url)}");
            return false;
        }

        if (Interlocked.Exchange(ref page.Decided, 1) != 0)
            return false;

        if (_config.DryRun)
        {
            Warning($"[DRY-RUN] 종료 대상 {score}점 [{reason}] {Shorten(url)}");
            return false;
        }

        int normalPages = Targets.Values.Count(target =>
            target.Type == "page" && !IsInternal(target.Url));
        if (normalPages <= 1)
        {
            Warning("마지막 일반 탭이므로 종료하지 않는다: " + Shorten(url));
            return false;
        }

        try
        {
            await client.SendAsync(
                "Target.closeTarget",
                new JsonObject { ["targetId"] = page.TargetId },
                null,
                3000).ConfigureAwait(false);

            lock (RecentClosed)
            {
                RecentClosed.Insert(0, new ClosedItem(
                    url, openerUrl, score, reason, DateTime.Now));
                if (RecentClosed.Count > 20)
                    RecentClosed.RemoveRange(20, RecentClosed.Count - 20);
            }

            Success($"자동 광고 탭 종료 {score}점 [{reason}] {Shorten(url)}");
            WriteEvent(new { stage = "closed", score, reason, url, openerUrl });

            if (_config.RefocusOpener && page.OpenerId is not null)
            {
                client.Fire(
                    "Target.activateTarget",
                    new JsonObject { ["targetId"] = page.OpenerId });
            }

            return true;
        }
        catch (Exception ex)
        {
            Error("탭 종료 실패: " + ex.Message);
            return false;
        }
    }

    private static IntentAssessment AssessIntent(PageContext page, string destinationUrl)
    {
        if (!_config.ProtectExplicitClicks)
            return IntentAssessment.Neutral("click-protection-off");
        if (page.UserApproved)
            return IntentAssessment.Explicit(page.ApprovalReason);

        long now = Now;
        IEnumerable<UserIntent> candidates;
        if (page.OpenerId is not null && Intents.TryGetValue(page.OpenerId, out var openerIntents))
        {
            candidates = openerIntents.ToArray();
        }
        else
        {
            candidates = GlobalIntents.ToArray();
        }

        UserIntent[] recent = candidates
            .Where(intent => now - intent.ReceivedAt >= 0 &&
                             now - intent.ReceivedAt <= _config.IntentWindowMs)
            .OrderByDescending(intent => intent.ReceivedAt)
            .ToArray();

        return AssessRecentIntents(destinationUrl, recent);
    }

    private static IntentAssessment AssessRecentIntents(
        string destinationUrl,
        IReadOnlyList<UserIntent> recent)
    {
        if (recent.Count == 0)
            return IntentAssessment.Auto("no-user-gesture");

        // 이전 클릭이 다음 클릭을 오염시키지 않도록 가장 최근 제스처 하나만 본다.
        UserIntent newest = recent[0];
        if ((newest.Kind == "link" || newest.Kind == "form") &&
            !string.IsNullOrWhiteSpace(newest.Url))
        {
            if (UrlMatchesIntent(newest.Url, destinationUrl))
            {
                string label = string.IsNullOrWhiteSpace(newest.Label)
                    ? newest.Kind
                    : newest.Label;
                return IntentAssessment.Explicit("clicked:" + Shorten(label, 36));
            }
            return IntentAssessment.Mismatch("clicked-url-mismatch");
        }

        if (newest.Kind is "passive" or "passive-link")
            return IntentAssessment.Mismatch("passive-click-popup");
        if (newest.Kind == "control")
            return IntentAssessment.Control("clicked-control");
        return IntentAssessment.Neutral("recent-user-gesture");
    }

    private static (int Score, string Reason) Score(
        string url,
        string openerUrl,
        PageContext page,
        IntentAssessment intent,
        Config config)
    {
        if (page.UserApproved || intent.Approved)
            return (-999, "explicit-user-navigation");
        if (string.IsNullOrWhiteSpace(url) || IsBlank(url))
            return (-999, "blank-pending");
        if (IsInternal(url))
            return (-999, "internal");

        string host = HostOf(url);
        string openerHost = HostOf(openerUrl);
        if (host.Length == 0)
            return (-999, "no-host");
        if (Matches(host, config.AllowedSites))
            return (-999, "allowed-site");
        if (Matches(host, config.Whitelist))
            return (-999, "whitelist");
        if (LooksLikeSensitiveFlow(url))
            return (-999, "sensitive-flow");

        bool adDomain = Matches(host, config.AdDomains);
        bool watchedOpener = openerHost.Length > 0 && Matches(openerHost, config.WatchedSites);
        bool hasOpener = openerHost.Length > 0;
        bool crossSite = hasOpener &&
                         !Etld1(host).Equals(Etld1(openerHost), StringComparison.OrdinalIgnoreCase);

        if (!hasOpener && !adDomain)
            return (-999, "no-opener");

        bool automaticScope = config.BlockAutomaticCrossSitePopups &&
                              crossSite && (intent.Automatic || intent.Unexpected);
        if (!watchedOpener && !adDomain && !automaticScope && !config.StrictMode)
            return (-999, "not-in-scope");

        int score = 0;
        var reasons = new List<string>();

        Add(adDomain, 80, "ad-domain");
        Add(watchedOpener, 45, "watched-opener");
        Add(intent.Unexpected, 60, intent.Reason);
        Add(intent.Automatic, 50, intent.Reason);
        Add(crossSite, 25, "cross-site");

        if (hasOpener && !crossSite)
        {
            score -= 35;
            reasons.Add("same-site");
        }

        Add(page.PopupLikely, 15, "popup-window");
        Add(openerHost.Length > 0 && Matches(openerHost, config.AdDomains), 20, "ad-chain");
        Add(IsBlank(page.InitialUrl) && !IsBlank(url), 15, "blank-redirect");
        Add(config.SuspiciousTlds.Contains(TldOf(host), StringComparer.OrdinalIgnoreCase),
            15, "suspicious-tld");
        Add(Now - page.CreatedAt < 1500, 10, "fast-open");
        Add(page.Redirects >= 2, 10, "redirect-chain");

        if (intent.UserControl)
        {
            score -= 40;
            reasons.Add("clicked-control");
        }

        return (score, string.Join('+', reasons));

        void Add(bool condition, int points, string reason)
        {
            if (!condition)
                return;
            score += points;
            reasons.Add(reason);
        }
    }

    private static bool UrlMatchesIntent(string intended, string actual)
    {
        if (!Uri.TryCreate(intended, UriKind.Absolute, out Uri? left) ||
            !Uri.TryCreate(actual, UriKind.Absolute, out Uri? right))
            return string.Equals(intended.TrimEnd('/'), actual.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase);

        if (!left.Scheme.Equals(right.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !left.Host.Equals(right.Host, StringComparison.OrdinalIgnoreCase) ||
            left.Port != right.Port)
            return false;

        string leftPath = Uri.UnescapeDataString(left.AbsolutePath).TrimEnd('/');
        string rightPath = Uri.UnescapeDataString(right.AbsolutePath).TrimEnd('/');
        if (!leftPath.Equals(rightPath, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrEmpty(left.Query))
            return true;
        return left.Query.Equals(right.Query, StringComparison.Ordinal);
    }

    private static string HostOf(string raw)
    {
        return Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri)
            ? uri.IdnHost.ToLowerInvariant().TrimEnd('.')
            : "";
    }

    private static readonly HashSet<string> CommonSecondLevelDomains =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "co", "com", "ne", "net", "or", "org", "go", "ac", "gov", "edu",
            "pe", "re", "kg", "ms", "sc", "hs"
        };

    private static string Etld1(string host)
    {
        string[] parts = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3 && parts[^1].Length == 2 &&
            CommonSecondLevelDomains.Contains(parts[^2]))
        {
            return string.Join('.', parts[^3..]);
        }

        return parts.Length >= 2 ? string.Join('.', parts[^2..]) : host;
    }

    private static string TldOf(string host)
    {
        int separator = host.LastIndexOf('.');
        return separator < 0 ? "" : host[(separator + 1)..];
    }

    private static bool Matches(string host, IEnumerable<string> domains)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        foreach (string? item in domains)
        {
            string domain = (item ?? "").Trim().TrimStart('*', '.').ToLowerInvariant();
            if (domain.Length == 0)
                continue;
            if (host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsBlank(string url)
    {
        return string.IsNullOrWhiteSpace(url) ||
               url.StartsWith("about:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInternal(string url)
    {
        return IsBlank(url) ||
               url.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("chrome-untrusted://", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("devtools://", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("edge://", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("file://", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeSensitiveFlow(string url)
    {
        string value = url.ToLowerInvariant();
        return value.Contains("/oauth", StringComparison.Ordinal) ||
               value.Contains("/authorize", StringComparison.Ordinal) ||
               value.Contains("/signin", StringComparison.Ordinal) ||
               value.Contains("/login", StringComparison.Ordinal) ||
               value.Contains("/sso", StringComparison.Ordinal) ||
               value.Contains("saml", StringComparison.Ordinal) ||
               value.Contains("/checkout", StringComparison.Ordinal) ||
               value.Contains("/payment", StringComparison.Ordinal);
    }

    private static string Shorten(string value, int maximum = 100)
    {
        return value.Length <= maximum ? value : value[..maximum] + "…";
    }

    private static void LoadOrCreateConfig()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                _config = Config.Defaults();
                SaveConfig();
                return;
            }

            Config? loaded = JsonSerializer.Deserialize<Config>(
                File.ReadAllText(ConfigPath, Encoding.UTF8), JsonOptions);
            if (loaded is not null)
                _config = loaded;
        }
        catch (Exception ex)
        {
            Error("설정을 읽지 못해 기본값을 사용한다: " + ex.Message);
            _config = Config.Defaults();
        }
    }

    private static void SaveConfig()
    {
        try
        {
            Directory.CreateDirectory(_configDirectory);
            File.WriteAllText(
                ConfigPath,
                JsonSerializer.Serialize(_config, JsonOptions),
                new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Error("설정 저장 실패: " + ex.Message);
        }
    }

    private static void StartConfigWatcher()
    {
        try
        {
            _configWatcher = new FileSystemWatcher(_configDirectory, "config.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size |
                               NotifyFilters.FileName,
                EnableRaisingEvents = true
            };

            long lastReload = 0;
            FileSystemEventHandler reload = (_, _) =>
            {
                long now = Now;
                if (now - Interlocked.Read(ref lastReload) < 500)
                    return;
                Interlocked.Exchange(ref lastReload, now);
                _ = Task.Run(async () =>
                {
                    await Task.Delay(150).ConfigureAwait(false);
                    LoadOrCreateConfig();
                    Info("설정을 다시 읽었다.");
                });
            };
            _configWatcher.Changed += reload;
            _configWatcher.Created += reload;
            _configWatcher.Renamed += (sender, eventArgs) => reload(sender, eventArgs);
        }
        catch (Exception ex)
        {
            Warning("설정 자동 다시 읽기를 시작하지 못했다: " + ex.Message);
        }
    }

    private static void WriteEvent(object payload)
    {
        try
        {
            var record = new JsonObject
            {
                ["ts"] = DateTimeOffset.Now.ToString("O"),
                ["data"] = JsonNode.Parse(JsonSerializer.Serialize(payload, JsonOptions))
            };
            lock (LogLock)
            {
                File.AppendAllText(
                    EventLogPath,
                    record.ToJsonString() + Environment.NewLine,
                    new UTF8Encoding(false));
            }
        }
        catch
        {
        }
    }

    private static void ReadKeys()
    {
        while (!ApplicationCancellation.IsCancellationRequested)
        {
            ConsoleKeyInfo key;
            try
            {
                key = Console.ReadKey(true);
            }
            catch
            {
                return;
            }

            switch (char.ToLowerInvariant(key.KeyChar))
            {
                case 'q':
                    ApplicationCancellation.Cancel();
                    return;

                case 'd':
                    _config.DryRun = !_config.DryRun;
                    Info("DRY-RUN " + (_config.DryRun ? "ON (관측만)" : "OFF (실제 종료)"));
                    break;

                case 'p':
                    _config.Enabled = !_config.Enabled;
                    Info("감시 " + (_config.Enabled ? "재개" : "일시중지"));
                    break;

                case 'r':
                    LoadOrCreateConfig();
                    Info("설정을 다시 적용했다.");
                    break;

                case 'l':
                    PrintRecentClosed();
                    break;

                case 'u':
                    UndoLastClosed();
                    break;

                case 'w':
                    AllowLastClosedSite();
                    break;
            }
        }
    }

    private static void PrintRecentClosed()
    {
        lock (RecentClosed)
        {
            if (RecentClosed.Count == 0)
            {
                Info("최근 종료 내역이 없다.");
                return;
            }

            Console.WriteLine("  ── 최근 종료 ──");
            for (int index = 0; index < RecentClosed.Count; index++)
            {
                ClosedItem item = RecentClosed[index];
                Console.WriteLine(
                    $"   [{index}] {item.At:HH:mm:ss} {item.Score,3}점 " +
                    $"[{item.Reason}] {Shorten(item.Url, 80)}");
            }
        }
    }

    private static void UndoLastClosed()
    {
        ClosedItem? item = null;
        lock (RecentClosed)
        {
            if (RecentClosed.Count > 0)
            {
                item = RecentClosed[0];
                RecentClosed.RemoveAt(0);
            }
        }

        if (item is null)
        {
            Info("되돌릴 탭이 없다.");
            return;
        }

        _cdp?.Fire(
            "Target.createTarget",
            new JsonObject { ["url"] = item.Url, ["newWindow"] = false });
        Info("탭을 다시 열었다: " + Shorten(item.Url));
    }

    private static void AllowLastClosedSite()
    {
        string? host = null;
        lock (RecentClosed)
        {
            if (RecentClosed.Count > 0)
                host = HostOf(RecentClosed[0].Url);
        }

        if (string.IsNullOrEmpty(host))
        {
            Info("정상 사이트로 등록할 최근 탭이 없다.");
            return;
        }

        string domain = Etld1(host);
        if (_config.AllowedSites.Contains(domain, StringComparer.OrdinalIgnoreCase))
        {
            Info("이미 정상 사이트로 등록되어 있다: " + domain);
            return;
        }

        _config.AllowedSites.Add(domain);
        SaveConfig();
        Success("정상 사이트로 등록했다: " + domain);
    }

    private static int RunSelfTests()
    {
        int passed = 0;
        Config config = Config.Defaults();
        config.WatchedSites.Add("problem.example");
        long now = Now;

        Check("명시적으로 클릭한 광고 주소도 보호", () =>
        {
            var page = TestPage(now);
            var intent = IntentAssessment.Explicit("clicked:광고 링크");
            return Score("https://popads.net/a", "https://problem.example/", page, intent, config).Score <= -900;
        });

        Check("클릭한 링크와 다른 외부 탭은 종료", () =>
        {
            var page = TestPage(now);
            var intent = IntentAssessment.Mismatch("clicked-url-mismatch");
            return Score("https://unwanted.example/a", "https://problem.example/", page, intent, config).Score >= config.CloseThreshold;
        });

        Check("사용자 조작 버튼의 일반 외부 창은 보호", () =>
        {
            var page = TestPage(now);
            var intent = IntentAssessment.Control("clicked-control");
            return Score("https://tool.example/a", "https://problem.example/", page, intent, config).Score < config.CloseThreshold;
        });

        Check("사용자 동작 없는 외부 팝업은 종료", () =>
        {
            var page = TestPage(now);
            var intent = IntentAssessment.Auto("no-user-gesture");
            return Score("https://unknown.example/a", "https://ordinary.example/", page, intent, config).Score >= config.CloseThreshold;
        });

        Check("opener 없는 일반 탭은 보호", () =>
        {
            var page = TestPage(now);
            var intent = IntentAssessment.Auto("no-user-gesture");
            return Score("https://example.com/", "", page, intent, config).Score <= -900;
        });

        Check("로그인 흐름은 보호", () =>
        {
            var page = TestPage(now);
            var intent = IntentAssessment.Auto("no-user-gesture");
            return Score("https://identity.example/oauth/authorize", "https://problem.example/", page, intent, config).Score <= -900;
        });

        Check("등록한 정상 사이트의 하위 도메인 창도 보호", () =>
        {
            config.AllowedSites.Add("safe.example");
            var page = TestPage(now);
            var intent = IntentAssessment.Auto("no-user-gesture");
            return Score("https://sub.safe.example/popup", "https://problem.example/", page, intent, config).Score <= -900;
        });

        Check("기본 설정 경로는 실행 파일 폴더", () =>
            Path.GetFullPath(ConfigPath).Equals(
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "config.json")),
                StringComparison.OrdinalIgnoreCase));

        Check("클릭 URL 비교 시 추적 쿼리 추가를 허용", () =>
            UrlMatchesIntent("https://example.com/article", "https://example.com/article?utm_source=x"));

        Check("이전 링크 기록보다 최신 버튼 클릭을 우선", () =>
        {
            UserIntent[] intents =
            {
                new("opener", "control", "", "https://source.example/", "도구 열기", false, now),
                new("opener", "link", "https://old.example/", "https://source.example/", "이전 링크", true, now - 1000)
            };
            return AssessRecentIntents("https://tool.example/", intents).UserControl;
        });

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"자체 테스트 통과: {passed}/10");
        Console.ResetColor();
        return 0;

        static PageContext TestPage(long createdAt) => new()
        {
            TargetId = "test",
            InitialUrl = "https://example.invalid/",
            CreatedAt = createdAt
        };

        void Check(string name, Func<bool> assertion)
        {
            if (!assertion())
                throw new InvalidOperationException("자체 테스트 실패: " + name);
            passed++;
            Console.WriteLine("통과: " + name);
        }
    }

    private static void WriteLine(ConsoleColor color, string marker, string message)
    {
        lock (LogLock)
        {
            Console.ForegroundColor = color;
            Console.Write($"[{DateTime.Now:HH:mm:ss}] {marker} ");
            Console.ResetColor();
            Console.WriteLine(message);
        }
    }

    private static void Info(string message) => WriteLine(ConsoleColor.Gray, "·", message);
    private static void Success(string message) => WriteLine(ConsoleColor.Green, "✔", message);
    private static void Warning(string message) => WriteLine(ConsoleColor.Yellow, "!", message);
    private static void Error(string message) => WriteLine(ConsoleColor.Red, "✖", message);
}
