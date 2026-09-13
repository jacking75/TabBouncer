#nullable enable

using System;
using System.Linq;

namespace TabBouncer;

// 판정 사유 코드를 화면 문구로 바꾼다. 문구는 Strings.cs의 "reason.<코드>" 키에 있다.
// 코드를 추가하면 Strings.cs와 docs/how-it-works.md의 점수표도 함께 고친다.
internal static class Reasons
{
    internal static string Describe(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "";
        return string.Join(", ", code
            .Split('+', StringSplitOptions.RemoveEmptyEntries)
            .Select(One));
    }

    internal static string One(string code)
    {
        if (code.StartsWith("clicked:", StringComparison.Ordinal))
            return L.Format("reason.clicked-label", code["clicked:".Length..]);
        string key = "reason." + code;
        return L.Has(key) ? L.T(key) : code;
    }

    internal static string KindLabel(ClosedKind kind) => kind switch
    {
        ClosedKind.ClosedTab => L.T("kind.closed"),
        ClosedKind.RevertedRedirect => L.T("kind.reverted"),
        ClosedKind.Observed => L.T("kind.observed"),
        _ => L.T("kind.kept")
    };
}
