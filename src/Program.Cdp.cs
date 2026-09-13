#nullable enable

using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace TabBouncer;

// CDP 이벤트를 받아 탭 상태를 갱신하고, 페이지마다 클릭 의도 추적기를 설치한다.
internal static partial class Program
{
    private static bool _isolatedWorldWarningShown;

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
                    HandleBinding(parameters, sessionId);
                    break;

                case "Page.frameRequestedNavigation":
                    HandleFrameRequestedNavigation(parameters, sessionId);
                    break;

                case "Page.frameNavigated":
                    HandleFrameNavigated(parameters, sessionId);
                    break;

                case "Page.loadEventFired":
                    PushStateToGuide(sessionId);
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
            Error(L.Format("log.cdpEventError", method, ex.Message));
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

    private static void AdoptExistingPage(JsonNode? targetInfo)
    {
        if (targetInfo?["targetId"] is null ||
            !string.Equals(targetInfo["type"]?.GetValue<string>(), "page", StringComparison.Ordinal))
            return;

        HandleTargetInfo("Target.targetCreated", targetInfo);
        if (!Pages.TryGetValue(targetInfo["targetId"]!.GetValue<string>(), out var page))
            return;

        Interlocked.Exchange(ref page.Decided, 1);
        string url = targetInfo["url"]?.GetValue<string>() ?? "";
        if (!IsInternal(url))
            page.CommittedUrl = url;
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
            await client.SendAsync("Page.enable", null, sessionId, 1500).ConfigureAwait(false);
            await client.SendAsync("Network.enable", null, sessionId, 1500).ConfigureAwait(false);
            // 안내 페이지 버튼용 명령 바인딩이다. 명령은 안내 페이지 주소와 실행마다 바뀌는 토큰이 맞을 때만 처리한다.
            await client.SendAsync(
                "Runtime.addBinding",
                new JsonObject { ["name"] = Scripts.CommandBindingName },
                sessionId,
                1500).ConfigureAwait(false);
            await InstallIntentTrackerAsync(client, page, sessionId).ConfigureAwait(false);
            PushStateToGuide(sessionId);
        }
        catch (Exception ex)
        {
            // 광고 탭을 닫으면 초기화 중이던 명령이 실패한다. 탭이 이미 사라졌다면 정상 흐름이므로 경고하지 않는다.
            await Task.Delay(300).ConfigureAwait(false);
            if (Targets.ContainsKey(page.TargetId))
                Warning(L.Format("log.pageInitFailed", ex.Message));
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

    // 클릭 추적기와 바인딩을 격리 월드에 둔다. 페이지 스크립트는 격리 월드의 전역 객체를 볼 수 없으므로
    // 광고 스크립트가 바인딩을 직접 불러 "사용자가 링크를 클릭했다"는 기록을 꾸며 넣을 수 없다.
    private static async Task InstallIntentTrackerAsync(CdpClient client, PageContext page, string sessionId)
    {
        try
        {
            await client.SendAsync(
                "Runtime.addBinding",
                new JsonObject
                {
                    ["name"] = Scripts.IntentBindingName,
                    ["executionContextName"] = Scripts.IntentWorldName
                },
                sessionId,
                1500).ConfigureAwait(false);
            await client.SendAsync(
                "Page.addScriptToEvaluateOnNewDocument",
                new JsonObject
                {
                    ["source"] = Scripts.ClickTracker(hideBinding: false),
                    ["worldName"] = Scripts.IntentWorldName
                },
                sessionId,
                1500).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (!_isolatedWorldWarningShown)
            {
                _isolatedWorldWarningShown = true;
                Warning(L.Format("log.isolatedWorldUnavailable", ex.Message));
            }

            string fallback = Scripts.ClickTracker(hideBinding: true);
            await client.SendAsync(
                "Runtime.addBinding",
                new JsonObject { ["name"] = Scripts.IntentBindingName },
                sessionId,
                1500).ConfigureAwait(false);
            await client.SendAsync(
                "Page.addScriptToEvaluateOnNewDocument",
                new JsonObject { ["source"] = fallback },
                sessionId,
                1500).ConfigureAwait(false);
            client.Fire(
                "Runtime.evaluate",
                new JsonObject { ["expression"] = fallback, ["returnByValue"] = false },
                sessionId);
            return;
        }

        // 이미 로드된 문서에는 새 문서용 스크립트가 돌지 않으므로 격리 월드를 직접 만들어 설치한다.
        try
        {
            JsonNode? world = await client.SendAsync(
                "Page.createIsolatedWorld",
                new JsonObject
                {
                    ["frameId"] = page.TargetId,
                    ["worldName"] = Scripts.IntentWorldName
                },
                sessionId,
                1500).ConfigureAwait(false);
            int contextId = world?["executionContextId"]?.GetValue<int>() ?? 0;
            if (contextId > 0)
            {
                client.Fire(
                    "Runtime.evaluate",
                    new JsonObject
                    {
                        ["expression"] = Scripts.ClickTracker(hideBinding: false),
                        ["contextId"] = contextId,
                        ["returnByValue"] = false
                    },
                    sessionId);
            }
        }
        catch
        {
            // 현재 문서만 추적하지 못한다. 다음 이동부터는 새 문서용 스크립트가 설치한다.
        }
    }

    private static void HandleBinding(JsonNode? parameters, string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return;
        string name = parameters?["name"]?.GetValue<string>() ?? "";
        if (name.Equals(Scripts.CommandBindingName, StringComparison.Ordinal))
            HandleCommandBinding(parameters, sessionId);
        else if (name.Equals(Scripts.IntentBindingName, StringComparison.Ordinal))
            HandleIntentBinding(parameters, sessionId);
    }

    private static void HandleCommandBinding(JsonNode? parameters, string sessionId)
    {
        if (!TargetsBySession.TryGetValue(sessionId, out string? targetId) ||
            !Targets.TryGetValue(targetId, out TargetRecord? target) ||
            !target.Url.StartsWith(GuidePageUrl, StringComparison.OrdinalIgnoreCase))
            return;

        JsonNode? value;
        try
        {
            value = JsonNode.Parse(parameters?["payload"]?.GetValue<string>() ?? "");
        }
        catch (JsonException)
        {
            return;
        }

        if (!string.Equals(value?["token"]?.GetValue<string>(), GuideToken, StringComparison.Ordinal))
            return;

        switch (value?["command"]?.GetValue<string>())
        {
            case "toggleMonitoring":
                ToggleMonitoring();
                break;
            case "setDryRun":
                SetDryRun(value["value"]?.GetValue<bool>() ?? false);
                break;
            case "open":
                if (Config.NormalizeUrl(value["value"]?.GetValue<string>()) is { Length: > 0 } url)
                    OpenUrl(url);
                break;
        }
    }

    private static void HandleIntentBinding(JsonNode? parameters, string sessionId)
    {
        if (!_config.ProtectExplicitClicks)
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
            bool pageInitiated = page.PageNavigationRequestedAt != 0 &&
                                 Now - page.PageNavigationRequestedAt <= 10000;
            page.PageNavigationRequestedAt = 0;
            _ = EvaluateAsync(page, "frameNavigated");
            // 이미 유지하기로 결정된(Decided != 0) 탭도 자체 하이재킹 검사는 매번 새로 받아야 한다.
            // 그렇지 않으면 최초 로드 때 통과한 탭이 나중에 광고 사이트로 리다이렉트돼도 영원히 무시된다.
            _ = EvaluateRedirectHijackAsync(page, sessionId, url, pageInitiated);
        }
    }

    // Chrome은 페이지(스크립트·링크·meta refresh)가 시작한 이동에만 이 이벤트를 보낸다.
    // 주소창 입력·북마크·뒤로 가기 같은 브라우저 주도 이동에는 오지 않으므로 둘을 구분하는 근거로 쓴다.
    private static void HandleFrameRequestedNavigation(JsonNode? parameters, string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId) ||
            !TargetsBySession.TryGetValue(sessionId, out string? targetId))
            return;
        if (!string.Equals(parameters?["frameId"]?.GetValue<string>(), targetId, StringComparison.Ordinal) ||
            !string.Equals(parameters?["disposition"]?.GetValue<string>(), "currentTab", StringComparison.Ordinal))
            return;
        if (Pages.TryGetValue(targetId, out var page))
            page.PageNavigationRequestedAt = Now;
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

    private static void PruneIntentQueue(ConcurrentQueue<UserIntent> queue, long now)
    {
        long retention = Math.Max(_config.IntentWindowMs * 2L, 10000L);
        while (queue.TryPeek(out UserIntent? oldest) && now - oldest.ReceivedAt > retention)
            queue.TryDequeue(out _);
    }
}
