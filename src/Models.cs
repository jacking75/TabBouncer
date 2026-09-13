#nullable enable

using System;
using System.Collections.Generic;

namespace TabBouncer;

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
    public string CommittedUrl = "";
    public long PageNavigationRequestedAt;
    public string RevertedFromUrl = "";
    public long RevertedAt;
    public int RevertCount;
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

internal readonly record struct ScorePart(string Code, int Points);

internal readonly record struct ScoreResult(int Score, string Reason, IReadOnlyList<ScorePart> Breakdown)
{
    public static ScoreResult Keep(string reason) => new(-999, reason, Array.Empty<ScorePart>());
}

internal enum ClosedKind
{
    ClosedTab,
    RevertedRedirect,
    Kept,
    Observed
}

internal sealed record ClosedItem(
    string Url,
    string OpenerUrl,
    int Score,
    string Reason,
    DateTime At,
    ClosedKind Kind,
    IReadOnlyList<ScorePart> Breakdown,
    string TargetId = "",
    bool Restored = false,
    int Count = 1,
    DateTime FirstAt = default)
{
    // 같은 종류로 같은 호스트에서 반복된 항목은 최근 목록에서 한 행으로 묶는다.
    // 광고 주소는 쿼리나 경로가 매번 달라지므로 주소 전체가 아니라 호스트로 비교한다.
    public string GroupKey => $"{Kind}|{GroupHost}";

    private string GroupHost =>
        Uri.TryCreate(Url, UriKind.Absolute, out Uri? uri) && uri.Host.Length > 0
            ? uri.Host.ToLowerInvariant()
            : Url;

    public DateTime FirstSeen => FirstAt == default ? At : FirstAt;

    // 화면 갱신 여부를 판단할 때 쓴다. 횟수나 마지막 시각이 바뀌면 달라진다.
    public string Signature => $"{GroupKey}:{Count}:{At.Ticks}:{Restored}";
}

internal sealed record AppSnapshot(
    bool Enabled,
    bool DryRun,
    bool Connected,
    string ConnectionStatus,
    string BrowserName,
    int CloseThreshold,
    int SessionClosedCount,
    long TotalClosedCount,
    string WatchedSites,
    bool CanOpenChrome);

internal sealed record AppLogEntry(DateTime At, string Level, string Message);
