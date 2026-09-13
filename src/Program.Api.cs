#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace TabBouncer;

// GUI(MainForm, ConfigForm, 트레이)가 부르는 기능이다.
internal static partial class Program
{
    internal static string DataDirectory => _dataDirectory;

    // 화면 표시용으로만 읽는다. 바꿀 때는 아래 메서드를 쓴다.
    internal static Config CurrentConfig => _config;

    internal static bool UsesDataDirectoryArgument => _dataDirectoryFromArgument;

    internal static AppSnapshot GetSnapshot()
    {
        string watched = _config.WatchedSites.Count == 0
            ? L.T("scope.allSites")
            : string.Join(", ", _config.WatchedSites);

        long total;
        lock (LogLock)
            total = _stats.TotalClosed + _stats.TotalReverted;

        return new AppSnapshot(
            _config.Enabled,
            _config.DryRun,
            _connected,
            _connectionStatus,
            _browserName,
            _config.CloseThreshold,
            _sessionClosedCount,
            total,
            watched,
            _waitingForChrome);
    }

    internal static void RequestChromeLaunch()
    {
        if (!_waitingForChrome)
            return;
        try
        {
            ChromeLaunchRequests.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    internal static IReadOnlyList<ClosedItem> GetRecentClosed()
    {
        lock (RecentClosed)
            return RecentClosed.ToArray();
    }

    // 감시를 켤 때 전용 Chrome이 닫혀 있으면 함께 연다. 사용자가 버튼을 누른 순간에만 열기 때문에
    // "닫은 Chrome을 멋대로 다시 띄우지 않는다"는 원칙은 그대로 지킨다.
    internal static void ToggleMonitoring()
    {
        _config.Enabled = !_config.Enabled;
        Info(L.T(_config.Enabled ? "log.monitoringResumed" : "log.monitoringPaused"));
        PushStateToGuide();
        if (_config.Enabled && _waitingForChrome && _config.AutoLaunchChrome)
        {
            Info(L.T("log.openChromeForMonitoring"));
            RequestChromeLaunch();
        }
    }

    internal static void SetDryRun(bool enabled)
    {
        if (_config.DryRun == enabled)
            return;

        _config.DryRun = enabled;
        Info(L.T(enabled ? "log.dryRunOn" : "log.dryRunOff"));
        PushStateToGuide();
    }

    internal static void ReloadConfig(bool adoptFileDryRun = false)
    {
        LoadOrCreateConfig(adoptFileDryRun);
        PushStateToGuide();
        Info(L.T("log.configApplied"));
    }

    internal static void Undo(ClosedItem item)
    {
        if (item.Kind is ClosedKind.Kept or ClosedKind.Observed)
        {
            Info(L.Format("log.undoNotNeeded", Shorten(item.Url)));
            return;
        }

        // 화면이 가진 항목은 그사이 횟수가 늘어 다른 인스턴스로 바뀌었을 수 있으므로 묶음 기준으로 지운다.
        lock (RecentClosed)
            RecentClosed.RemoveAll(existing => existing.GroupKey == item.GroupKey);
        OpenUrl(item.Url);
        Info(L.Format("log.reopened", Shorten(item.Url)));
    }

    internal static void AllowSite(ClosedItem item)
    {
        string host = HostOf(item.Url);
        if (host.Length == 0)
        {
            Info(L.T("log.allowNoAddress"));
            return;
        }

        string domain = Etld1(host);
        if (_config.AllowedSites.Contains(domain, StringComparer.OrdinalIgnoreCase))
        {
            Info(L.Format("log.allowExists", domain));
            return;
        }

        UpdateConfigFile(config =>
        {
            if (!config.AllowedSites.Contains(domain, StringComparer.OrdinalIgnoreCase))
                config.AllowedSites.Add(domain);
        });
        Success(L.Format("log.allowAdded", domain));
    }

    internal static void RegisterAdDomain(ClosedItem item)
    {
        string domain = Etld1(HostOf(item.Url));
        if (domain.Length == 0)
        {
            Info(L.T("log.adDomainNoAddress"));
            return;
        }

        if (_config.EffectiveAdDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
        {
            Info(L.Format("log.adDomainExists", domain));
        }
        else
        {
            UpdateConfigFile(config =>
            {
                config.RemovedAdDomains.RemoveAll(value =>
                    Config.NormalizeDomain(value).Equals(domain, StringComparison.OrdinalIgnoreCase));
                if (!config.EffectiveAdDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
                    config.AdDomains.Add(domain);
            });
            Success(L.Format("log.adDomainAdded", domain));
        }

        // 기준에 못 미쳐 유지했던 탭이 아직 열려 있으면 지금 닫는다.
        if (item.Kind is ClosedKind.Kept or ClosedKind.Observed &&
            item.TargetId.Length > 0 && !_config.DryRun &&
            Targets.TryGetValue(item.TargetId, out TargetRecord? target) &&
            Matches(HostOf(target.Url), new[] { domain }))
        {
            int normalPages = Targets.Values.Count(value => value.Type == "page" && !IsInternal(value.Url));
            if (normalPages > 1)
            {
                _cdp?.Fire("Target.closeTarget", new JsonObject { ["targetId"] = target.TargetId });
                Success(L.Format("log.keptAdClosed", Shorten(target.Url)));
            }
        }
    }

    internal static void RegisterWatchedSite(ClosedItem item)
    {
        string domain = Etld1(HostOf(item.OpenerUrl));
        if (domain.Length == 0)
        {
            Info(L.T("log.watchNoOpener"));
            return;
        }

        if (_config.WatchedSites.Contains(domain, StringComparer.OrdinalIgnoreCase))
        {
            Info(L.Format("log.watchExists", domain));
            return;
        }

        UpdateConfigFile(config =>
        {
            if (!config.WatchedSites.Contains(domain, StringComparer.OrdinalIgnoreCase))
                config.WatchedSites.Add(domain);
        });
        Success(L.Format("log.watchAdded", domain));
    }

    internal static void UndoLatest()
    {
        if (LatestOfKind(ClosedKind.ClosedTab, ClosedKind.RevertedRedirect) is { } item)
            Undo(item);
        else
            Info(L.T("log.undoNothing"));
    }

    internal static void AllowLatestSite()
    {
        if (LatestOfKind(ClosedKind.ClosedTab, ClosedKind.RevertedRedirect, ClosedKind.Observed) is { } item)
            AllowSite(item);
        else
            Info(L.T("log.allowNothing"));
    }

    private static ClosedItem? LatestOfKind(params ClosedKind[] kinds)
    {
        lock (RecentClosed)
            return RecentClosed.FirstOrDefault(item => kinds.Contains(item.Kind));
    }

    internal static string SiteOf(string url)
    {
        string host = HostOf(url);
        return host.Length == 0 ? "" : Etld1(host);
    }

    internal static string HostForDisplay(string url)
    {
        string host = HostOf(url);
        return host.Length == 0 ? Shorten(url, 60) : host;
    }

    // 사용자가 요청한 주소를 전용 Chrome의 새 탭으로 연다. Chrome이 닫혀 있으면 열고 연결되는 대로 연다.
    internal static bool OpenUrl(string raw)
    {
        string url = Config.NormalizeUrl(raw);
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            Warning(L.Format("log.invalidUrl", Shorten(raw)));
            return false;
        }

        PendingOpenUrls.Enqueue(uri.AbsoluteUri);
        if (_connected && _cdp is not null)
        {
            OpenPendingUrls();
        }
        else if (_waitingForChrome)
        {
            Info(L.Format("log.openAfterLaunch", Shorten(uri.AbsoluteUri)));
            RequestChromeLaunch();
        }
        else
        {
            Info(L.Format("log.openAfterConnect", Shorten(uri.AbsoluteUri)));
        }
        return true;
    }

    internal static void SetCloseChromeOnExit(string choice)
    {
        if (!Config.CloseChromeChoices.Contains(choice))
            return;
        UpdateConfigFile(config => config.CloseChromeOnExit = choice);
    }

    internal static string GetConfigPath() => ConfigPath;

    internal static string ReadConfigText()
    {
        try
        {
            return File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath, Encoding.UTF8) : "";
        }
        catch (Exception ex)
        {
            Error(L.Format("log.configReadFailed", ex.Message));
            return "";
        }
    }

    // 설정 화면이 편집할 복사본이다. 파일에 저장된 값을 기준으로 하되, 관측 모드는 지금 화면에 보이는 값을 쓴다.
    internal static Config GetEditableConfig()
    {
        Config file = ReadConfigFileOrDefaults();
        file.DryRun = _config.DryRun;
        return file;
    }

    internal static string SerializeConfig(Config config) => JsonSerializer.Serialize(config, JsonOptions);

    internal static Config? ParseConfig(string json, out string error)
    {
        try
        {
            Config? config = JsonSerializer.Deserialize<Config>(json, JsonOptions);
            if (config is null)
            {
                error = L.T("config.error.empty");
                return null;
            }
            config.Resolve();
            error = "";
            return config;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return null;
        }
    }

    internal static bool TrySaveConfigText(string json, out string error)
    {
        Config? parsed = ParseConfig(json, out error);
        if (parsed is null)
            return false;
        if (parsed.Validate() is { } problem)
        {
            error = problem;
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, json, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        error = "";
        ReloadConfig(adoptFileDryRun: true);
        return true;
    }

    internal static bool TrySaveConfig(Config model, out string error)
    {
        model.Resolve();
        if (model.Validate() is { } problem)
        {
            error = problem;
            return false;
        }
        return TrySaveConfigText(SerializeConfig(model), out error);
    }

    internal static string BuildDiagnostics()
    {
        AppSnapshot snapshot = GetSnapshot();
        Config config = _config;
        string browserPath = _launchedBrowserPath;
        if (browserPath.Length == 0)
        {
            try
            {
                browserPath = ResolveChromePath();
            }
            catch
            {
                browserPath = "(not found)";
            }
        }

        var text = new StringBuilder();
        text.AppendLine("TabBouncer diagnostics");
        text.AppendLine(L.T("diagnostics.privacy"));
        text.AppendLine();
        text.AppendLine($"Version: v{Version}");
        text.AppendLine($"OS: Windows {Environment.OSVersion.Version} ({RuntimeInformation.OSArchitecture})");
        text.AppendLine($".NET: {Environment.Version}");
        text.AppendLine($"UI language: {L.Language}");
        text.AppendLine($"Browser: {(_browserVersion.Length > 0 ? _browserVersion : "(not connected)")}");
        text.AppendLine($"Browser path: {browserPath}");
        text.AppendLine($"Connected: {snapshot.Connected} ({snapshot.ConnectionStatus})");
        text.AppendLine($"Monitoring: {snapshot.Enabled}, dryRun: {snapshot.DryRun}");
        text.AppendLine($"Config file: {ConfigPath}{(_configDirectoryFallback ? " (fallback)" : "")}");
        text.AppendLine($"Data folder: {_dataDirectory}");
        text.AppendLine($"Settings: closeThreshold={config.CloseThreshold}, strictMode={config.StrictMode}, " +
                        $"preemptiveBlock={config.PreemptiveBlock}, protectExplicitClicks={config.ProtectExplicitClicks}, " +
                        $"blockAutomaticCrossSitePopups={config.BlockAutomaticCrossSitePopups}, " +
                        $"blockRedirectHijack={config.BlockRedirectHijack}, debounceMs={config.DebounceMs}, " +
                        $"intentWindowMs={config.IntentWindowMs}, debugPort={config.DebugPort}, " +
                        $"autoLaunchChrome={config.AutoLaunchChrome}, useBuiltinAdDomains={config.UseBuiltinAdDomains}");
        text.AppendLine($"Lists: allowedSites={config.AllowedSites.Count}, watchedSites={config.WatchedSites.Count}, " +
                        $"adDomains(user)={config.AdDomains.Count}, removedAdDomains={config.RemovedAdDomains.Count}, " +
                        $"effectiveAdDomains={config.EffectiveAdDomains.Count}, whitelist={config.Whitelist.Count}");
        text.AppendLine($"Blocked: session={snapshot.SessionClosedCount}, total={snapshot.TotalClosedCount}");
        text.AppendLine();
        text.AppendLine("Recent log (30 lines):");
        foreach (string line in ReadLogTail(30))
            text.AppendLine(line);
        return text.ToString();
    }
}
