#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TabBouncer;

// 새 탭·창 점수 판정과 종료, 현재 탭 리다이렉트 복귀를 담당한다.
internal static partial class Program
{
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
                JsonNode? window = await client.SendAsync(
                    "Browser.getWindowForTarget",
                    new JsonObject { ["targetId"] = page.TargetId },
                    null,
                    1500).ConfigureAwait(false);
                JsonNode? bounds = window?["bounds"];
                int width = bounds?["width"]?.GetValue<int>() ?? 0;
                int height = bounds?["height"]?.GetValue<int>() ?? 0;
                page.PopupLikely = width > 0 && height > 0 && (width < 1000 || height < 780);
            }
            catch
            {
            }
        }

        ScoreResult result = Score(url, openerUrl, page, intent, _config);
        int score = result.Score;
        string reason = result.Reason;

        if (score <= -900)
        {
            if (intent.Approved ||
                (stage == "debounce" && reason != "blank-pending"))
                Interlocked.Exchange(ref page.Decided, 1);

            if (intent.Approved)
            {
                Info(L.Format("log.userApproved", intent.Reason, Shorten(url)));
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
                ageMs = Now - page.CreatedAt,
                breakdown = BreakdownForEvent(result.Breakdown)
            });
        }

        if (score < _config.CloseThreshold)
        {
            if (stage == "debounce")
            {
                Interlocked.Exchange(ref page.Decided, 1);
                if (score >= _config.CloseThreshold - 30)
                {
                    // 기준에 조금 못 미쳐 유지한 탭이다. 광고였다면 GUI에서 광고 도메인으로 등록할 수 있게 목록에 남긴다.
                    RecordItem(new ClosedItem(url, openerUrl, score, reason, DateTime.Now,
                        ClosedKind.Kept, result.Breakdown, page.TargetId));
                    WriteEvent(new
                    {
                        stage = "kept",
                        score,
                        reason,
                        url,
                        openerUrl,
                        breakdown = BreakdownForEvent(result.Breakdown)
                    });
                }
            }
            if (score >= _config.CloseThreshold - 30)
                Info(L.Format("log.kept", score, reason, Shorten(url)));
            return false;
        }

        if (Interlocked.Exchange(ref page.Decided, 1) != 0)
            return false;

        if (_config.DryRun)
        {
            Warning(L.Format("log.dryRunClose", score, reason, Shorten(url)));
            RecordItem(new ClosedItem(url, openerUrl, score, reason, DateTime.Now,
                ClosedKind.Observed, result.Breakdown, page.TargetId));
            WriteEvent(new
            {
                stage = "dry-run",
                score,
                reason,
                url,
                openerUrl,
                breakdown = BreakdownForEvent(result.Breakdown)
            });
            return false;
        }

        int normalPages = Targets.Values.Count(target =>
            target.Type == "page" && !IsInternal(target.Url));
        if (normalPages <= 1)
        {
            Warning(L.Format("log.lastTab", Shorten(url)));
            return false;
        }

        try
        {
            await client.SendAsync(
                "Target.closeTarget",
                new JsonObject { ["targetId"] = page.TargetId },
                null,
                3000).ConfigureAwait(false);

            Success(L.Format("log.closed", score, reason, Shorten(url)));
            WriteEvent(new
            {
                stage = "closed",
                score,
                reason,
                url,
                openerUrl,
                breakdown = BreakdownForEvent(result.Breakdown)
            });
            RecordItem(new ClosedItem(url, openerUrl, score, reason, DateTime.Now,
                ClosedKind.ClosedTab, result.Breakdown, page.TargetId));

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
            Error(L.Format("log.closeFailed", ex.Message));
            return false;
        }
    }

    private static async Task EvaluateRedirectHijackAsync(
        PageContext page,
        string sessionId,
        string newUrl,
        bool pageInitiated)
    {
        if (IsInternal(newUrl))
            return;

        string trustedUrl = page.CommittedUrl;
        CdpClient? client = _cdp;
        // 감시가 꺼져 있을 때도 기준 URL은 따라가야 다시 켰을 때 엉뚱한 페이지로 되돌리지 않는다.
        if (!pageInitiated || IsBlank(trustedUrl) ||
            trustedUrl.Equals(newUrl, StringComparison.OrdinalIgnoreCase) ||
            !_config.Enabled || !_config.BlockRedirectHijack || client is null)
        {
            page.CommittedUrl = newUrl;
            return;
        }

        IntentAssessment intent = AssessSameTabIntent(page, newUrl);
        ScoreResult result = ScoreRedirectHijack(trustedUrl, newUrl, intent, _config);
        int score = result.Score;
        string reason = result.Reason;

        if (score < _config.CloseThreshold)
        {
            page.CommittedUrl = newUrl;
            return;
        }

        if (_config.DryRun)
        {
            Warning(L.Format("log.dryRunRedirect", score, reason, Shorten(newUrl)));
            WriteEvent(new
            {
                stage = "redirect-dry-run",
                score,
                reason,
                url = newUrl,
                restoredUrl = trustedUrl,
                breakdown = BreakdownForEvent(result.Breakdown)
            });
            RecordItem(new ClosedItem(newUrl, trustedUrl, score, reason, DateTime.Now,
                ClosedKind.Observed, result.Breakdown, page.TargetId));
            page.CommittedUrl = newUrl;
            return;
        }

        bool repeating = page.RevertedFromUrl.Equals(trustedUrl, StringComparison.OrdinalIgnoreCase) &&
                         Now - page.RevertedAt < 30000;
        int attempts = repeating ? page.RevertCount : 0;
        if (attempts >= 2)
        {
            // 되돌린 페이지가 스스로 다시 이동하면 무한 반복된다. 두 번 막은 뒤에는 이동을 허용한다.
            Warning(L.Format("log.redirectRepeating", score, reason, Shorten(newUrl)));
            page.RevertedFromUrl = "";
            page.RevertCount = 0;
            page.CommittedUrl = newUrl;
            return;
        }

        page.RevertedFromUrl = trustedUrl;
        page.RevertedAt = Now;
        page.RevertCount = attempts + 1;

        try
        {
            await client.SendAsync(
                "Page.navigate",
                new JsonObject { ["url"] = trustedUrl },
                sessionId,
                3000).ConfigureAwait(false);

            Success(L.Format("log.redirectBlocked", score, reason, Shorten(newUrl)));
            WriteEvent(new
            {
                stage = "redirect-blocked",
                score,
                reason,
                url = newUrl,
                restoredUrl = trustedUrl,
                breakdown = BreakdownForEvent(result.Breakdown)
            });
            RecordItem(new ClosedItem(newUrl, trustedUrl, score, reason, DateTime.Now,
                ClosedKind.RevertedRedirect, result.Breakdown, page.TargetId));
        }
        catch (Exception ex)
        {
            page.CommittedUrl = newUrl;
            Error(L.Format("log.redirectRestoreFailed", ex.Message));
        }
    }

    private static object[] BreakdownForEvent(IReadOnlyList<ScorePart> breakdown) =>
        breakdown.Select(part => (object)new { code = part.Code, points = part.Points }).ToArray();

    private static void RecordItem(ClosedItem item)
    {
        lock (RecentClosed)
        {
            AddOrMerge(RecentClosed, item);
            if (RecentClosed.Count > MaxRecentItems)
                RecentClosed.RemoveRange(MaxRecentItems, RecentClosed.Count - MaxRecentItems);
        }

        if (item.Kind is ClosedKind.ClosedTab or ClosedKind.RevertedRedirect)
        {
            Interlocked.Increment(ref _sessionClosedCount);
            IncrementStats(item.Kind);
            PushStateToGuide();
        }

        try
        {
            ItemRecorded?.Invoke(item);
        }
        catch
        {
        }
    }

    // 최신 항목을 맨 앞에 둔다. 같은 종류·같은 호스트 항목이 이미 있으면 새 행을 만들지 않고
    // 횟수를 올린 뒤 가장 최근 주소·점수·사유로 바꿔 맨 앞으로 옮긴다. 목록 잠금 안에서만 호출한다.
    private static void AddOrMerge(List<ClosedItem> list, ClosedItem item)
    {
        int index = list.FindIndex(existing => existing.GroupKey == item.GroupKey);
        if (index < 0)
        {
            list.Insert(0, item);
            return;
        }

        ClosedItem existing = list[index];
        list.RemoveAt(index);
        list.Insert(0, item with
        {
            Count = existing.Count + item.Count,
            FirstAt = existing.FirstSeen < item.FirstSeen ? existing.FirstSeen : item.FirstSeen
        });
    }

    private static IntentAssessment AssessSameTabIntent(PageContext page, string destinationUrl)
    {
        if (!_config.ProtectExplicitClicks)
            return IntentAssessment.Neutral("click-protection-off");

        long now = Now;
        IEnumerable<UserIntent> candidates = Intents.TryGetValue(page.TargetId, out var own)
            ? own.ToArray()
            : Array.Empty<UserIntent>();

        UserIntent[] recent = candidates
            .Where(intent => now - intent.ReceivedAt >= 0 && now - intent.ReceivedAt <= _config.IntentWindowMs)
            .OrderByDescending(intent => intent.ReceivedAt)
            .ToArray();

        return AssessRecentIntents(destinationUrl, recent);
    }

    private static ScoreResult ScoreRedirectHijack(
        string fromUrl, string toUrl, IntentAssessment intent, Config config)
    {
        if (intent.Approved)
            return ScoreResult.Keep("explicit-user-navigation");
        if (IsBlank(fromUrl))
            return ScoreResult.Keep("initial-load");

        string fromHost = HostOf(fromUrl);
        string toHost = HostOf(toUrl);
        if (toHost.Length == 0)
            return ScoreResult.Keep("no-host");
        if (Matches(toHost, config.AllowedSites))
            return ScoreResult.Keep("allowed-site");
        if (Matches(toHost, config.Whitelist))
            return ScoreResult.Keep("whitelist");
        bool adDomain = Matches(toHost, config.EffectiveAdDomains);
        if (!adDomain && LooksLikeSensitiveFlow(toUrl))
            return ScoreResult.Keep("sensitive-flow");
        if (fromHost.Length > 0 &&
            Etld1(toHost).Equals(Etld1(fromHost), StringComparison.OrdinalIgnoreCase))
            return ScoreResult.Keep("same-site-navigation");

        int score = 0;
        var reasons = new List<string>();
        var breakdown = new List<ScorePart>();

        Add(intent.Automatic, 80, intent.Reason);
        Add(intent.Unexpected, 80, intent.Reason);
        Add(adDomain, 20, "ad-domain");
        Add(fromHost.Length > 0 && Matches(fromHost, config.WatchedSites), 15, "watched-site-origin");
        Add(config.SuspiciousTlds.Contains(TldOf(toHost), StringComparer.OrdinalIgnoreCase),
            15, "suspicious-tld");
        Add(intent.UserControl, -60, "clicked-control");

        return new ScoreResult(score, string.Join('+', reasons), breakdown);

        void Add(bool condition, int points, string reason)
        {
            if (!condition)
                return;
            score += points;
            reasons.Add(reason);
            breakdown.Add(new ScorePart(reason, points));
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

    private static ScoreResult Score(
        string url,
        string openerUrl,
        PageContext page,
        IntentAssessment intent,
        Config config)
    {
        if (page.UserApproved || intent.Approved)
            return ScoreResult.Keep("explicit-user-navigation");
        if (string.IsNullOrWhiteSpace(url) || IsBlank(url))
            return ScoreResult.Keep("blank-pending");
        if (IsInternal(url))
            return ScoreResult.Keep("internal");

        string host = HostOf(url);
        string openerHost = HostOf(openerUrl);
        if (host.Length == 0)
            return ScoreResult.Keep("no-host");
        if (Matches(host, config.AllowedSites))
            return ScoreResult.Keep("allowed-site");
        if (Matches(host, config.Whitelist))
            return ScoreResult.Keep("whitelist");

        bool adDomain = Matches(host, config.EffectiveAdDomains);
        // 로그인·결제 흐름처럼 보여도 알려진 광고 도메인이면 예외를 주지 않는다.
        if (!adDomain && LooksLikeSensitiveFlow(url))
            return ScoreResult.Keep("sensitive-flow");

        bool watchedOpener = openerHost.Length > 0 && Matches(openerHost, config.WatchedSites);
        bool hasOpener = openerHost.Length > 0;
        bool crossSite = hasOpener &&
                         !Etld1(host).Equals(Etld1(openerHost), StringComparison.OrdinalIgnoreCase);

        if (!hasOpener && !adDomain)
            return ScoreResult.Keep("no-opener");

        bool automaticScope = config.BlockAutomaticCrossSitePopups &&
                              crossSite && (intent.Automatic || intent.Unexpected);
        if (!watchedOpener && !adDomain && !automaticScope && !config.StrictMode)
            return ScoreResult.Keep("not-in-scope");

        int score = 0;
        var reasons = new List<string>();
        var breakdown = new List<ScorePart>();

        Add(adDomain, 80, "ad-domain");
        Add(watchedOpener, 45, "watched-opener");
        Add(intent.Unexpected, 60, intent.Reason);
        Add(intent.Automatic, 50, intent.Reason);
        Add(crossSite, 25, "cross-site");
        Add(hasOpener && !crossSite, -35, "same-site");
        Add(page.PopupLikely, 15, "popup-window");
        Add(openerHost.Length > 0 && Matches(openerHost, config.EffectiveAdDomains), 20, "ad-chain");
        Add(IsBlank(page.InitialUrl) && !IsBlank(url), 15, "blank-redirect");
        Add(config.SuspiciousTlds.Contains(TldOf(host), StringComparer.OrdinalIgnoreCase),
            15, "suspicious-tld");
        Add(Now - page.CreatedAt < 1500, 10, "fast-open");
        Add(page.Redirects >= 2, 10, "redirect-chain");
        // 버튼 감점은 알 수 없는 사이트의 정상 창을 지키려는 것이다. 광고 도메인을 여는 버튼은 위장한 광고로 본다.
        Add(intent.UserControl && !adDomain, -40, "clicked-control");

        return new ScoreResult(score, string.Join('+', reasons), breakdown);

        void Add(bool condition, int points, string reason)
        {
            if (!condition)
                return;
            score += points;
            reasons.Add(reason);
            breakdown.Add(new ScorePart(reason, points));
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
               url.StartsWith("brave://", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("file://", StringComparison.OrdinalIgnoreCase);
    }

    // 경로 세그먼트 단위로만 본다. "?r=/login"처럼 쿼리에 끼워 넣은 문자열로는 예외를 받을 수 없다.
    // "/login.php", "/signin-oidc", "/oauth2/authorize"처럼 확장자나 접미사가 붙은 세그먼트는 허용한다.
    private static readonly Regex SensitivePathPattern = new(
        @"(^|/)(oauth\d*|authorize|signin|login|sso|checkout|payment)((\.|-|_)[a-z0-9]+)*(/|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static bool LooksLikeSensitiveFlow(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            return false;
        string path = Uri.UnescapeDataString(uri.AbsolutePath);
        // SAML 응답·요청은 경로와 쿼리 형식이 서비스마다 달라 어디에 있어도 인정한다.
        return SensitivePathPattern.IsMatch(path) ||
               uri.PathAndQuery.Contains("saml", StringComparison.OrdinalIgnoreCase);
    }

    private static string Shorten(string value, int maximum = 100)
    {
        return value.Length <= maximum ? value : value[..maximum] + "…";
    }
}
