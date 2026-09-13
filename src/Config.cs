#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace TabBouncer;

internal sealed class Config
{
    internal static readonly string[] BuiltinAdDomains =
    {
        "popads.net", "popcash.net", "propellerads.com", "adsterra.com",
        "exoclick.com", "exdynsrv.com", "hilltopads.net", "adcash.com",
        "clickadu.com", "trafficstars.com", "juicyads.com", "bodelen.com",
        "onclickalgo.com", "onclckpro.com", "onclasrv.com", "bidgear.com",
        "doubleclick.net", "adnxs.com", "adsrvr.org", "mgid.com",
        "revcontent.com", "taboola.com", "outbrain.com", "zeropark.com",
        "clickaine.com", "popunder.net", "richads.com", "monetag.com",
        "incompetencesorting.com"
    };

    internal static readonly string[] CloseChromeChoices = { "ask", "always", "never" };

    public bool Enabled { get; set; } = true;
    public bool DryRun { get; set; }
    public bool StrictMode { get; set; }
    public bool PreemptiveBlock { get; set; } = true;
    public bool RefocusOpener { get; set; } = true;
    public bool BlockAutomaticCrossSitePopups { get; set; } = true;
    public bool ProtectExplicitClicks { get; set; } = true;
    public bool BlockRedirectHijack { get; set; } = true;

    public int CloseThreshold { get; set; } = 80;
    public int DebounceMs { get; set; } = 1200;
    public int IntentWindowMs { get; set; } = 3500;
    public int DebugPort { get; set; }
    public bool AutoLaunchChrome { get; set; } = true;
    public string ChromePath { get; set; } = "";
    public string UserDataDir { get; set; } = "";
    public string StartUrl { get; set; } = "";
    public List<string> ChromeArguments { get; set; } = new();

    public bool StartMonitoringOnLaunch { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool NotifyOnBlock { get; set; } = true;
    public string CloseChromeOnExit { get; set; } = "ask";
    public string Language { get; set; } = "";

    public List<string> WatchedSites { get; set; } = new();
    public bool UseBuiltinAdDomains { get; set; } = true;
    public List<string> AdDomains { get; set; } = new();
    public List<string> RemovedAdDomains { get; set; } = new();
    public List<string> AllowedSites { get; set; } = new();
    public List<string> Whitelist { get; set; } = new();
    public List<string> SuspiciousTlds { get; set; } = new();
    public List<string> FavoriteSites { get; set; } = new();

    // 내장 광고 목록 + 사용자 추가분 - 제외 목록. 파일에는 저장하지 않는다.
    [JsonIgnore]
    public List<string> EffectiveAdDomains { get; private set; } = new();

    public static Config Defaults()
    {
        var config = new Config
        {
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
        config.Resolve();
        return config;
    }

    // 파생 값을 다시 계산한다. 목록을 바꾼 뒤에는 반드시 호출한다.
    public void Resolve()
    {
        ChromeArguments ??= new();
        WatchedSites ??= new();
        AdDomains ??= new();
        RemovedAdDomains ??= new();
        AllowedSites ??= new();
        Whitelist ??= new();
        SuspiciousTlds ??= new();
        FavoriteSites ??= new();
        ChromePath ??= "";
        UserDataDir ??= "";
        StartUrl ??= "";
        CloseChromeOnExit = CloseChromeChoices.Contains(CloseChromeOnExit?.Trim().ToLowerInvariant())
            ? CloseChromeOnExit!.Trim().ToLowerInvariant()
            : "ask";
        Language = L.NormalizeLanguage(Language);

        var removed = new HashSet<string>(
            RemovedAdDomains.Select(NormalizeDomain).Where(value => value.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        var effective = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> source = UseBuiltinAdDomains
            ? BuiltinAdDomains.Concat(AdDomains)
            : AdDomains;
        foreach (string item in source)
        {
            string domain = NormalizeDomain(item);
            if (domain.Length > 0 && !removed.Contains(domain) && seen.Add(domain))
                effective.Add(domain);
        }
        EffectiveAdDomains = effective;
    }

    // 예전 형식은 내장 광고 목록 전체가 adDomains에 복사돼 있다. 내장 항목을 걷어내 사용자 추가분만 남긴다.
    public bool MigrateBuiltinAdDomains()
    {
        int before = AdDomains.Count;
        AdDomains = AdDomains
            .Where(item => !BuiltinAdDomains.Contains(NormalizeDomain(item), StringComparer.OrdinalIgnoreCase))
            .ToList();
        return AdDomains.Count != before;
    }

    // 사용자에게 보여 줄 수 있는 문장으로 첫 번째 문제를 돌려준다. 문제가 없으면 null이다.
    public string? Validate()
    {
        if (CloseThreshold is < 1 or > 300)
            return L.Format("config.invalid.closeThreshold", CloseThreshold);
        if (DebounceMs is < 200 or > 10000)
            return L.Format("config.invalid.debounceMs", DebounceMs);
        if (IntentWindowMs is < 500 or > 20000)
            return L.Format("config.invalid.intentWindowMs", IntentWindowMs);
        if (DebugPort != 0 && DebugPort is < 1024 or > 65535)
            return L.Format("config.invalid.debugPort", DebugPort);
        if (!string.IsNullOrWhiteSpace(ChromePath) &&
            !File.Exists(Environment.ExpandEnvironmentVariables(ChromePath)))
            return L.Format("config.invalid.chromePath", ChromePath);
        return null;
    }

    // 사용자가 붙여 넣은 주소나 "*.example.com"에서 호스트만 남긴다.
    internal static string NormalizeDomain(string? value)
    {
        string text = (value ?? "").Trim();
        if (text.Length == 0)
            return "";
        if (text.Contains("://", StringComparison.Ordinal) &&
            Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) && uri.Host.Length > 0)
            text = uri.Host;
        else
        {
            int cut = text.IndexOfAny(new[] { '/', '?', '#' });
            if (cut >= 0)
                text = text[..cut];
        }
        return text.TrimStart('*', '.').TrimEnd('.').ToLowerInvariant();
    }

    internal static string NormalizeUrl(string? value)
    {
        string text = (value ?? "").Trim();
        if (text.Length == 0)
            return "";
        if (!text.Contains("://", StringComparison.Ordinal) &&
            !text.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            text = "https://" + text;
        return Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) ? uri.AbsoluteUri : "";
    }
}
