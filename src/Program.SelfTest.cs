#nullable enable

using System;
using System.IO;
using System.Linq;

namespace TabBouncer;

// --self-test: 판정 점수 계산을 확인하는 콘솔 자체 검사다.
internal static partial class Program
{
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

        Check("버튼으로 열려도 등록된 광고 도메인은 종료", () =>
        {
            var page = TestPage(now);
            var intent = IntentAssessment.Control("clicked-control");
            return Score("https://incompetencesorting.com/x?key=1", "https://problem.example/", page, intent, config).Score >= config.CloseThreshold;
        });

        Check("다른 가산점이 없어도 버튼으로 열린 광고 도메인은 종료", () =>
        {
            var page = TestPage(now - 5000);
            var intent = IntentAssessment.Control("clicked-control");
            return Score("https://incompetencesorting.com/x?key=1", "", page, intent, config).Score >= config.CloseThreshold;
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

        Check("클릭 없이 현재 탭이 광고 사이트로 리다이렉트되면 차단", () =>
        {
            var intent = IntentAssessment.Auto("no-user-gesture");
            return ScoreRedirectHijack(
                "https://reader.example/chapter-1", "https://popads.net/x", intent, config).Score
                >= config.CloseThreshold;
        });

        Check("전혀 다른 사이트로 위장한 리다이렉트도 차단", () =>
        {
            var intent = IntentAssessment.Mismatch("passive-click-popup");
            return ScoreRedirectHijack(
                "https://reader.example/chapter-1", "https://scam-casino.example/", intent, config).Score
                >= config.CloseThreshold;
        });

        Check("버튼을 눌러 이동하는 현재 탭 리다이렉트는 보호", () =>
        {
            var intent = IntentAssessment.Control("clicked-control");
            return ScoreRedirectHijack(
                "https://reader.example/chapter-1", "https://partner.example/", intent, config).Score
                < config.CloseThreshold;
        });

        Check("로그인 등 민감한 흐름으로의 리다이렉트는 보호", () =>
        {
            var intent = IntentAssessment.Auto("no-user-gesture");
            return ScoreRedirectHijack(
                "https://reader.example/chapter-1", "https://identity.example/oauth/authorize", intent, config).Score
                <= -900;
        });

        Check("같은 사이트 안에서의 이동은 보호", () =>
        {
            var intent = IntentAssessment.Auto("no-user-gesture");
            return ScoreRedirectHijack(
                "https://reader.example/chapter-1", "https://reader.example/chapter-2", intent, config).Score
                <= -900;
        });

        Check("쓰기 가능하면 설정 경로는 실행 파일 폴더", () =>
            Path.GetFullPath(Path.Combine(ResolveConfigDirectory(), "config.json")).Equals(
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

        Check("언어 표는 한국어가 기본이고 영어가 있으며 모든 언어의 키와 자리표시자가 같음", () =>
        {
            foreach (string problem in L.FindTableProblems())
                Console.WriteLine("  " + problem);
            return L.DefaultLanguage == "ko" &&
                   L.NormalizeLanguage("EN") == "en" &&
                   L.Languages.Count >= 2 &&
                   !L.FindTableProblems().Any();
        });

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"자체 테스트 통과: {passed}/18");
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
}
