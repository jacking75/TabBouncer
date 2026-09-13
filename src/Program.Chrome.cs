#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace TabBouncer;

// 전용 브라우저 실행, 디버깅 포트 탐색, 안내 페이지를 담당한다.
internal static partial class Program
{
    private static readonly string GuideToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    private static string _launchedBrowserPath = "";
    private static bool _portWarningShown;

    private static string GuidePagePath => Path.Combine(_dataDirectory, "start.html");
    private static string GuidePageUrl => new Uri(GuidePagePath).AbsoluteUri;

    private static string ResolveProfileDirectory() =>
        string.IsNullOrWhiteSpace(_config.UserDataDir)
            ? Path.Combine(_dataDirectory, "ChromeProfile")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(_config.UserDataDir));

    private static string ActivePortFilePath => Path.Combine(ResolveProfileDirectory(), "DevToolsActivePort");

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

    // Chrome은 원격 디버깅을 켜면 프로필 폴더에 "포트\n/devtools/browser/<id>" 형식의 파일을 쓴다.
    private static (int Port, string Path)? ReadActivePortFile()
    {
        try
        {
            string[] lines = File.ReadAllLines(ActivePortFilePath);
            if (lines.Length >= 2 && int.TryParse(lines[0].Trim(), out int port) && port > 0)
                return (port, lines[1].Trim());
        }
        catch
        {
        }
        return null;
    }

    // TabBouncer 전용 프로필로 실행된 브라우저의 CDP 주소만 돌려준다.
    // debugPort가 0이면 프로필의 DevToolsActivePort로 포트를 찾고, 웹소켓 경로가 같아야 우리 브라우저로 본다.
    // debugPort를 직접 지정하면 예전처럼 그 포트를 쓰되, 전용 프로필이 아닌 것 같으면 한 번 경고한다.
    private static async Task<string?> ProbeOwnBrowserAsync(CancellationToken cancellationToken)
    {
        (int Port, string Path)? active = ReadActivePortFile();

        if (_config.DebugPort > 0)
        {
            string? fixedPort = await ProbeBrowserWebSocketAsync(_config.DebugPort, cancellationToken)
                .ConfigureAwait(false);
            if (fixedPort is not null && active is { } file && !_portWarningShown)
            {
                bool samePort = file.Port == _config.DebugPort;
                if ((samePort && !fixedPort.EndsWith(file.Path, StringComparison.Ordinal)) ||
                    (!samePort && await ProbeBrowserWebSocketAsync(file.Port, cancellationToken)
                        .ConfigureAwait(false) is { } own && own.EndsWith(file.Path, StringComparison.Ordinal)))
                {
                    _portWarningShown = true;
                    Warning($"포트 {_config.DebugPort}에는 TabBouncer 전용 프로필이 아닌 다른 Chrome이 있을 수 있다. " +
                            "config.json의 debugPort를 0(자동)으로 두는 것을 권한다.");
                }
            }
            return fixedPort;
        }

        if (active is not { } found)
            return null;
        string? candidate = await ProbeBrowserWebSocketAsync(found.Port, cancellationToken).ConfigureAwait(false);
        return candidate is not null && candidate.EndsWith(found.Path, StringComparison.Ordinal)
            ? candidate
            : null;
    }

    private static void LaunchChrome()
    {
        string chrome = ResolveChromePath();
        string profile = ResolveProfileDirectory();
        Directory.CreateDirectory(profile);
        try
        {
            File.Delete(ActivePortFilePath);
        }
        catch
        {
        }

        var startInfo = new ProcessStartInfo(chrome)
        {
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add($"--remote-debugging-port={Math.Max(0, _config.DebugPort)}");
        startInfo.ArgumentList.Add("--remote-debugging-address=127.0.0.1");
        startInfo.ArgumentList.Add($"--user-data-dir={profile}");
        startInfo.ArgumentList.Add("--no-first-run");
        startInfo.ArgumentList.Add("--no-default-browser-check");
        foreach (string argument in _config.ChromeArguments.Where(value =>
                     !string.IsNullOrWhiteSpace(value)))
        {
            startInfo.ArgumentList.Add(argument);
        }
        string startUrl = string.IsNullOrWhiteSpace(_config.StartUrl)
            ? WriteGuidePage()
            : _config.StartUrl;
        if (!string.IsNullOrWhiteSpace(startUrl))
            startInfo.ArgumentList.Add(startUrl);

        _launchedBrowserPath = chrome;
        Info("브라우저를 실행한다: " + chrome);
        Info("전용 프로필: " + profile);
        Process.Start(startInfo);
    }

    internal static string[] BrowserCandidates()
    {
        string programFiles = Environment.GetEnvironmentVariable("ProgramFiles") ?? "";
        string programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? "";
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] roots = { programFiles, programFilesX86, localAppData };
        string[] relatives =
        {
            @"Google\Chrome\Application\chrome.exe",
            @"Microsoft\Edge\Application\msedge.exe",
            @"BraveSoftware\Brave-Browser\Application\brave.exe",
            @"Chromium\Application\chrome.exe"
        };

        // Chrome을 먼저 찾고, 없을 때만 다른 Chromium 계열을 쓴다.
        return relatives
            .SelectMany(relative => roots
                .Where(root => !string.IsNullOrWhiteSpace(root))
                .Select(root => Path.Combine(root, relative)))
            .ToArray();
    }

    private static string ResolveChromePath()
    {
        string configured = Environment.ExpandEnvironmentVariables(_config.ChromePath);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        foreach (string candidate in BrowserCandidates())
        {
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            "Chrome 계열 브라우저를 찾지 못했다. config.json의 chromePath에 chrome.exe 경로를 지정해야 한다.");
    }

    private static async Task ReadBrowserVersionAsync(CdpClient client)
    {
        try
        {
            JsonNode? version = await client.SendAsync("Browser.getVersion", null, null, 2000).ConfigureAwait(false);
            string product = version?["product"]?.GetValue<string>() ?? "";
            string userAgent = version?["userAgent"]?.GetValue<string>() ?? "";
            _browserVersion = product;
            _browserName = DescribeBrowser(product, userAgent, _launchedBrowserPath);
        }
        catch
        {
            _browserVersion = "";
            _browserName = "Chrome";
        }
    }

    private static string DescribeBrowser(string product, string userAgent, string path)
    {
        string major(string value) => value.Split('.')[0];
        int edge = userAgent.IndexOf(" Edg/", StringComparison.Ordinal);
        if (edge >= 0)
            return "Edge " + major(userAgent[(edge + 5)..].Split(' ')[0]);

        string[] parts = product.Split('/', 2);
        string name = parts.Length > 0 && parts[0].Length > 0 ? parts[0] : "Chrome";
        if (name.StartsWith("Headless", StringComparison.Ordinal))
            name = "Chrome";
        if (path.Contains("brave", StringComparison.OrdinalIgnoreCase))
            name = "Brave";
        else if (path.Contains(@"\Chromium\", StringComparison.OrdinalIgnoreCase))
            name = "Chromium";
        return parts.Length > 1 ? $"{name} {major(parts[1])}" : name;
    }

    private static string WriteGuidePage()
    {
        try
        {
            var strings = new JsonObject
            {
                ["title"] = L.T("guide.title"),
                ["intro"] = L.T("guide.intro"),
                ["tipAddress"] = L.T("guide.tipAddress"),
                ["tipDefault"] = L.T("guide.tipDefault"),
                ["tipClosed"] = L.T("guide.tipClosed"),
                ["favoritesTitle"] = L.T("guide.favoritesTitle"),
                ["noFavorites"] = L.T("guide.noFavorites"),
                ["stateOn"] = L.T("guide.stateOn"),
                ["stateDry"] = L.T("guide.stateDry"),
                ["stateOff"] = L.T("guide.stateOff"),
                ["titlePrefixOff"] = L.T("guide.titlePrefixOff"),
                ["start"] = L.T("guide.start"),
                ["pause"] = L.T("guide.pause"),
                ["dryOn"] = L.T("guide.dryOn"),
                ["dryOff"] = L.T("guide.dryOff"),
                ["count"] = L.T("guide.count")
            };
            string html = Scripts.GuidePageHtml
                .Replace("{{LANG}}", L.Language)
                .Replace("{{STRINGS}}", strings.ToJsonString())
                .Replace("{{STATE}}", GuideState().ToJsonString())
                .Replace("{{TOKEN}}", GuideToken);
            File.WriteAllText(GuidePagePath, html, new UTF8Encoding(false));
            return GuidePageUrl;
        }
        catch (Exception ex)
        {
            Warning("안내 페이지를 만들지 못했다: " + ex.Message);
            return "";
        }
    }

    private static JsonObject GuideState()
    {
        var favorites = new JsonArray();
        foreach (string url in _config.FavoriteSites.Select(Config.NormalizeUrl).Where(url => url.Length > 0))
            favorites.Add(url);
        return new JsonObject
        {
            ["monitoring"] = _config.Enabled,
            ["dryRun"] = _config.DryRun,
            ["closedCount"] = _sessionClosedCount,
            ["favorites"] = favorites
        };
    }

    // 안내 페이지는 파일이라 감시 상태를 스스로 알 수 없다. 페이지 로드와 상태 변경 때마다 CDP로 알려준다.
    private static void PushStateToGuide(string? sessionId = null)
    {
        CdpClient? client = _cdp;
        if (client is null)
            return;

        string guideUrl = GuidePageUrl;
        // JSON은 HTML 태그로 해석될 문자를 이스케이프한 기본 인코더로 만든다.
        // 이전 실행이 연 안내 페이지에는 옛 토큰이 들어 있으므로 현재 토큰도 함께 보낸다.
        // 이 값은 주소가 안내 페이지 파일인 탭에만 보내므로 웹 페이지에는 드러나지 않는다.
        JsonObject pushed = GuideState();
        pushed["token"] = GuideToken;
        string state = pushed.ToJsonString();
        foreach (TargetRecord target in Targets.Values)
        {
            if (target.SessionId is not { } targetSession ||
                (sessionId is not null && targetSession != sessionId) ||
                !target.Url.StartsWith(guideUrl, StringComparison.OrdinalIgnoreCase))
                continue;

            client.Fire(
                "Runtime.evaluate",
                new JsonObject
                {
                    ["expression"] =
                        $"window.__tabBouncerPendingState = {state}; window.__tabBouncerSetState?.({state});"
                },
                targetSession);
        }
    }

    internal static async Task CloseChromeAsync()
    {
        CdpClient? client = _cdp;
        if (client is null)
            return;
        try
        {
            await client.SendAsync("Browser.close", null, null, 3000).ConfigureAwait(false);
            Info("전용 Chrome을 닫았다.");
        }
        catch (OperationCanceledException)
        {
            // Chrome이 응답을 보내기 전에 연결을 닫으면 대기 중인 명령이 취소된다. 닫힌 것으로 본다.
            Info("전용 Chrome을 닫았다.");
        }
        catch (Exception ex)
        {
            Warning("전용 Chrome을 닫지 못했다: " + ex.Message);
        }
    }
}
