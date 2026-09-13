#nullable enable

using System.Collections.Generic;
using System.Globalization;

namespace TabBouncer;

// 화면 문구 표. config.json의 language가 비어 있으면 Windows 표시 언어를 따른다(한국어가 아니면 영어).
// 활동 로그 파일 문구는 아직 한국어만 쓴다.
internal static class L
{
    private static readonly Dictionary<string, (string Ko, string En)> Table = new();

    internal static string Language { get; private set; } = Detect("");

    internal static void SetLanguage(string? configured) => Language = Detect(configured);

    private static string Detect(string? configured) =>
        configured is "ko" or "en"
            ? configured
            : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko" ? "ko" : "en";

    internal static bool Has(string key) => Table.ContainsKey(key);

    internal static string T(string key) =>
        Table.TryGetValue(key, out var value) ? (Language == "ko" ? value.Ko : value.En) : key;

    internal static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(key), args);

    private static void Add(string key, string ko, string en) => Table[key] = (ko, en);

    static L()
    {
        Add("common.cancel", "취소", "Cancel");

        // 연결 상태
        Add("status.preparing", "Chrome 연결 준비 중", "Preparing Chrome connection");
        Add("status.connecting", "Chrome 연결 중", "Connecting to Chrome");
        Add("status.connected", "Chrome 연결됨", "Chrome connected");
        Add("status.connectedWith", "{0} 연결됨", "{0} connected");
        Add("status.disconnected", "Chrome 연결 끊김", "Chrome disconnected");
        Add("status.chromeClosed", "Chrome 닫힘", "Chrome closed");
        Add("status.stopped", "종료됨", "Stopped");
        Add("scope.allSites", "모든 사이트 자동 판정", "All sites judged automatically");

        // 메인 창
        Add("main.subtitle", "원치 않는 광고 탭을 조용히 막는다", "Quietly blocks unwanted ad tabs");
        Add("main.banner.off", "감시가 꺼져 있다. \"감시 시작\"을 눌러야 광고 탭·창을 막는다.",
            "Monitoring is off. Press \"Start monitoring\" to block ad tabs and windows.");
        Add("main.banner.chromeClosed", "전용 Chrome이 닫혀 있어 감시할 대상이 없다. \"Chrome 열기\"를 누른다.",
            "The dedicated Chrome is closed, so there is nothing to monitor. Press \"Open Chrome\".");
        Add("main.banner.connecting", "Chrome에 연결하는 중이다.", "Connecting to Chrome.");
        Add("main.title.off", "감시 꺼짐", "Monitoring off");
        Add("main.title.chromeClosed", "Chrome 닫힘", "Chrome closed");
        Add("main.card.monitoring", "감시 상태", "Monitoring");
        Add("main.card.mode", "작동 모드", "Mode");
        Add("main.card.blocked", "차단", "Blocked");
        Add("main.state.on", "감시 중", "On");
        Add("main.state.off", "일시중지", "Paused");
        Add("main.mode.dryRun", "관측만", "Observe only");
        Add("main.mode.live", "자동 차단", "Auto block");
        Add("main.blocked.session", "이번 실행 {0}개", "{0} this session");
        Add("main.blocked.total", "누적 {0}개", "{0} in total");
        Add("main.button.start", "감시 시작", "Start monitoring");
        Add("main.button.pause", "감시 일시중지", "Pause monitoring");
        Add("main.button.openChrome", "Chrome 열기", "Open Chrome");
        Add("main.button.settings", "설정 열기", "Settings");
        Add("main.dryRun", "관측 모드 (탭을 닫지 않음)", "Observe mode (don't close tabs)");
        Add("main.url.placeholder", "전용 Chrome에서 열 주소 (예: example.com)  ·  Ctrl+L",
            "Address to open in the dedicated Chrome (e.g. example.com)  ·  Ctrl+L");
        Add("main.url.open", "전용 Chrome에서 열기", "Open in dedicated Chrome");
        Add("main.recent.title", "최근 차단한 탭", "Recently blocked");
        Add("main.recent.showKept", "유지·관측 항목도 표시", "Show kept and observed items");
        Add("main.recent.col.time", "시간", "Time");
        Add("main.recent.col.kind", "종류", "Type");
        Add("main.recent.col.count", "횟수", "Count");
        Add("main.recent.countValue", "{0}회", "×{0}");
        Add("main.detail.repeated", "{0}회 반복, 처음 {1}", "repeated {0} times, first at {1}");
        Add("main.recent.col.score", "점수", "Score");
        Add("main.recent.col.url", "주소", "Address");
        Add("main.recent.col.reason", "판정", "Reason");
        Add("main.recent.undo", "다시 열기", "Reopen");
        Add("main.recent.undoSelected", "선택한 탭 다시 열기", "Reopen selected");
        Add("main.recent.allow", "정상 사이트로 등록", "Allow site");
        Add("main.recent.allowSelected", "선택한 사이트 등록", "Allow selected site");
        Add("main.recent.adDomain", "광고 도메인으로 등록", "Mark as ad domain");
        Add("main.recent.watch", "감시 사이트로 등록", "Watch opener site");
        Add("main.detail.kind", "종류", "Type");
        Add("main.detail.url", "주소", "Address");
        Add("main.detail.opener", "열린 곳", "Opened from");
        Add("main.detail.restoredUrl", "돌아간 페이지", "Returned to");
        Add("main.detail.score", "점수 내역", "Score");
        Add("main.detail.none", "목록에서 항목을 선택하면 자세한 판정 내용을 보여 준다.",
            "Select an item to see how it was judged.");
        Add("main.detail.restored", "이전 실행 기록", "from a previous run");
        Add("main.activity.title", "활동", "Activity");
        Add("main.activity.openLogs", "로그 폴더 열기", "Open log folder");
        Add("main.activity.diagnostics", "진단 정보 복사", "Copy diagnostics");
        Add("main.activity.diagnosticsCopied",
            "진단 정보를 클립보드에 복사했다. 이슈에 붙여 넣기 전에 공개하고 싶지 않은 주소를 지운다.",
            "Diagnostics copied to the clipboard. Remove any addresses you don't want to share before posting.");
        Add("main.scope", "감시 범위: {0}  ·  차단 기준: {1}점", "Scope: {0}  ·  Threshold: {1}");

        Add("kind.closed", "탭 닫음", "Closed tab");
        Add("kind.reverted", "이동 되돌림", "Reverted redirect");
        Add("kind.kept", "유지", "Kept");
        Add("kind.observed", "관측(닫을 대상)", "Observed");

        Add("notify.one", "광고 탭을 막았다: {0}", "Blocked an ad tab: {0}");
        Add("notify.many", "광고 탭 {0}개를 막았다", "Blocked {0} ad tabs");

        // 트레이
        Add("tray.show", "창 열기", "Open window");
        Add("tray.monitoring", "감시", "Monitoring");
        Add("tray.dryRun", "관측 모드", "Observe mode");
        Add("tray.openChrome", "Chrome 열기", "Open Chrome");
        Add("tray.openLogs", "로그 폴더 열기", "Open log folder");
        Add("tray.exit", "종료", "Exit");
        Add("tray.hint", "TabBouncer는 트레이에서 계속 실행 중이다. 종료하려면 트레이 아이콘 메뉴에서 \"종료\"를 누른다.",
            "TabBouncer keeps running in the tray. To quit, choose \"Exit\" from the tray icon menu.");
        Add("tray.tooltip.on", "감시 중", "monitoring");
        Add("tray.tooltip.off", "감시 꺼짐", "monitoring off");
        Add("tray.tooltip.chromeClosed", "Chrome 닫힘", "Chrome closed");

        // 종료
        Add("exit.title", "TabBouncer 종료", "Exit TabBouncer");
        Add("exit.question", "전용 Chrome도 닫을까?", "Close the dedicated Chrome as well?");
        Add("exit.detail", "TabBouncer를 종료하면 전용 Chrome은 더 이상 보호되지 않는다.",
            "Once TabBouncer exits, the dedicated Chrome is no longer protected.");
        Add("exit.remember", "다음부터 묻지 않기", "Don't ask again");
        Add("exit.closeChrome", "Chrome도 닫기", "Close Chrome");
        Add("exit.keepChrome", "Chrome은 남기기", "Keep Chrome open");

        // 진단
        Add("diagnostics.privacy", "※ 붙여 넣기 전에 방문한 주소(URL) 등 공개하고 싶지 않은 정보를 지운다.",
            "* Before posting, remove visited addresses (URLs) or anything else you don't want to share.");

        // 안내 페이지
        Add("guide.title", "TabBouncer 보호 창", "TabBouncer protected window");
        Add("guide.titlePrefixOff", "[감시 꺼짐] ", "[Monitoring off] ");
        Add("guide.intro",
            "이 창은 TabBouncer가 연 <strong>전용 Chrome</strong>이다. 광고 탭·창 차단은 <strong>이 창과 이 창에서 연 탭·창에서만</strong> 동작한다.",
            "This is the <strong>dedicated Chrome</strong> opened by TabBouncer. Ad tab and window blocking works <strong>only in this window and the tabs and windows opened from it</strong>.");
        Add("guide.tipAddress", "보고 싶은 사이트는 이 창의 주소창에 입력해서 연다.",
            "Type the sites you want to visit into this window's address bar.");
        Add("guide.tipDefault", "평소 쓰는 Chrome 창은 보호되지 않는다. Chrome 보안 정책상 기본 프로필은 제어할 수 없다.",
            "Your everyday Chrome windows are not protected. Chrome's security policy does not allow controlling the default profile.");
        Add("guide.tipClosed", "이 창을 모두 닫으면 감시할 대상이 사라진다. 다시 열려면 TabBouncer의 <strong>Chrome 열기</strong>를 누른다.",
            "Closing every window of this Chrome leaves nothing to monitor. Press <strong>Open Chrome</strong> in TabBouncer to reopen it.");
        Add("guide.favoritesTitle", "즐겨찾기 사이트", "Favorite sites");
        Add("guide.noFavorites", "설정의 사이트 목록 탭에서 즐겨찾기 사이트를 등록하면 여기에 바로가기가 생긴다.",
            "Add favorite sites in Settings > Site lists to see shortcuts here.");
        Add("guide.stateOn", "감시 중이다. 이 창에서 생기는 광고 탭·창을 막는다.",
            "Monitoring is on. Ad tabs and windows in this window are blocked.");
        Add("guide.stateDry", "관측 모드다. 광고로 판정한 탭을 닫지 않고 기록만 한다.",
            "Observe mode. Tabs judged as ads are recorded but not closed.");
        Add("guide.stateOff", "감시가 꺼져 있다. 아래 \"감시 시작\"이나 TabBouncer 창의 \"감시 시작\"을 눌러야 광고 탭·창을 막는다.",
            "Monitoring is off. Press \"Start monitoring\" below or in the TabBouncer window to block ad tabs and windows.");
        Add("guide.start", "감시 시작", "Start monitoring");
        Add("guide.pause", "감시 일시중지", "Pause monitoring");
        Add("guide.dryOn", "관측 모드 켜기", "Turn on observe mode");
        Add("guide.dryOff", "관측 모드 끄기", "Turn off observe mode");
        Add("guide.count", "이번 실행에서 막은 광고 {0}개", "{0} ads blocked this session");

        // 판정 사유
        Add("reason.clicked-label", "클릭한 '{0}'", "clicked '{0}'");
        Add("reason.explicit-user-navigation", "사용자가 직접 연 주소", "opened by the user");
        Add("reason.blank-pending", "주소 대기 중", "waiting for address");
        Add("reason.internal", "브라우저 내부 페이지", "browser page");
        Add("reason.no-host", "호스트 없음", "no host");
        Add("reason.allowed-site", "정상 사이트로 등록됨", "allowed site");
        Add("reason.whitelist", "기본 보호 목록", "built-in protected list");
        Add("reason.sensitive-flow", "로그인·결제 흐름", "sign-in or payment flow");
        Add("reason.no-opener", "직접 연 탭", "opened directly");
        Add("reason.not-in-scope", "판정 범위 밖", "out of scope");
        Add("reason.initial-load", "첫 로드", "initial load");
        Add("reason.same-site-navigation", "같은 사이트 안 이동", "same-site navigation");
        Add("reason.ad-domain", "알려진 광고 도메인", "known ad domain");
        Add("reason.watched-opener", "감시 사이트에서 열림", "opened from a watched site");
        Add("reason.clicked-url-mismatch", "클릭한 링크와 다른 주소", "different from the clicked link");
        Add("reason.passive-click-popup", "빈 영역 클릭으로 열림", "opened by clicking a blank area");
        Add("reason.no-user-gesture", "클릭 없이 열림", "opened without a click");
        Add("reason.cross-site", "다른 사이트", "different site");
        Add("reason.same-site", "같은 사이트", "same site");
        Add("reason.popup-window", "작은 팝업 창", "small popup window");
        Add("reason.ad-chain", "광고 페이지가 연 탭", "opened by an ad page");
        Add("reason.blank-redirect", "빈 탭에서 이동", "redirected from a blank tab");
        Add("reason.suspicious-tld", "의심스러운 도메인 끝", "suspicious domain ending");
        Add("reason.fast-open", "생성 직후", "right after creation");
        Add("reason.redirect-chain", "연속 리다이렉트", "redirect chain");
        Add("reason.clicked-control", "버튼을 눌러 열림", "opened by a button");
        Add("reason.watched-site-origin", "감시 사이트에서 이동", "left a watched site");
        Add("reason.recent-user-gesture", "최근 사용자 동작 있음", "recent user action");
        Add("reason.click-protection-off", "클릭 보호 꺼짐", "click protection off");

        // 설정 창
        Add("config.title", "설정", "Settings");
        Add("config.tab.general", "일반", "General");
        Add("config.tab.sites", "사이트 목록", "Site lists");
        Add("config.tab.json", "고급 (JSON)", "Advanced (JSON)");
        Add("config.button.defaults", "기본값으로 복원", "Restore defaults");
        Add("config.button.save", "저장", "Save");
        Add("config.button.browse", "찾아보기", "Browse");
        Add("config.group.judge", "감시와 판정", "Monitoring and judgement");
        Add("config.group.app", "프로그램", "Application");
        Add("config.group.browser", "브라우저", "Browser");
        Add("config.json.help", "config.json 원문이다. 다른 탭으로 옮기거나 저장하면 검사한다.",
            "Raw config.json. It is checked when you switch tabs or save.");
        Add("config.closeChrome.ask", "매번 묻기", "Ask every time");
        Add("config.closeChrome.always", "항상 닫기", "Always close");
        Add("config.closeChrome.never", "닫지 않기", "Never close");
        Add("config.language.system", "Windows 설정 따르기", "Follow Windows");
        Add("config.language.restart", "언어 변경은 TabBouncer를 다시 실행하면 적용된다.",
            "The language change takes effect after restarting TabBouncer.");
        Add("config.browse.filter", "브라우저 실행 파일 (*.exe)|*.exe", "Browser executable (*.exe)|*.exe");
        Add("config.defaults.confirm", "모든 설정을 기본값으로 되돌린다. 등록한 사이트 목록도 비워진다. 계속할까?",
            "Reset every setting to its default? Your site lists will be cleared too.");
        Add("config.defaults.pending", "기본값을 불러왔다. \"저장\"을 눌러야 적용된다.",
            "Defaults loaded. Press \"Save\" to apply them.");
        Add("config.error.empty", "설정 내용이 비어 있다.", "The settings are empty.");
        Add("config.error.json", "JSON을 해석하지 못했다: {0}", "Could not parse the JSON: {0}");
        Add("config.error.save", "저장하지 못했다: {0}", "Could not save: {0}");
        Add("config.error.autoRun", "설정은 저장했지만 자동 실행을 바꾸지 못했다: {0}",
            "Settings were saved, but auto start could not be changed: {0}");
        Add("config.error.invalidItem", "올바른 값이 아니다: {0}", "Not a valid value: {0}");
        Add("config.error.shortcut", "바로가기를 만들지 못했다: {0}", "Could not create the shortcut: {0}");
        Add("config.shortcut.created", "바탕화면에 바로가기를 만들었다.\n{0}", "Created desktop shortcuts:\n{0}");
        Add("config.invalid.closeThreshold", "closeThreshold는 1~300이어야 한다. (현재 {0})",
            "closeThreshold must be between 1 and 300. (now {0})");
        Add("config.invalid.debounceMs", "debounceMs는 200~10000이어야 한다. (현재 {0})",
            "debounceMs must be between 200 and 10000. (now {0})");
        Add("config.invalid.intentWindowMs", "intentWindowMs는 500~20000이어야 한다. (현재 {0})",
            "intentWindowMs must be between 500 and 20000. (now {0})");
        Add("config.invalid.debugPort", "debugPort는 0(자동) 또는 1024~65535여야 한다. (현재 {0})",
            "debugPort must be 0 (automatic) or between 1024 and 65535. (now {0})");
        Add("config.invalid.chromePath", "chromePath 파일이 없다: {0}", "chromePath does not exist: {0}");

        Field("dryRun", "관측 모드", "탭을 닫거나 이동을 되돌리지 않고, 판정 결과만 목록과 로그에 남긴다.",
            "Observe mode", "Record verdicts in the list and logs without closing tabs or reverting redirects.");
        Field("protectExplicitClicks", "클릭한 링크 보호", "클릭한 링크·폼의 목적지와 새 탭 주소가 같으면 광고 점수와 관계없이 유지한다.",
            "Protect clicked links", "Keep a new tab whose address matches the clicked link or form, regardless of score.");
        Field("blockAutomaticCrossSitePopups", "클릭 없이 열린 외부 팝업 차단",
            "감시 사이트로 등록하지 않은 곳에서도, 클릭 없이 또는 클릭과 다르게 열린 다른 사이트 탭을 판정한다.",
            "Block automatic cross-site popups",
            "Judge other-site tabs opened without a click, or unlike the click, even on sites you haven't marked as watched.");
        Field("blockRedirectHijack", "현재 탭 강제 이동 되돌리기", "보던 탭이 클릭 없이 다른 사이트로 넘어가면 원래 페이지로 되돌린다.",
            "Revert redirect hijacks", "If the current tab jumps to another site without a click, return to the previous page.");
        Field("preemptiveBlock", "선차단", "새 탭이 로드되기 전에 잠시 멈추고 먼저 판정한다. 광고 페이지가 보이는 시간을 줄인다.",
            "Pre-emptive blocking", "Pause each new tab briefly and judge it before it loads, so ads show for less time.");
        Field("refocusOpener", "닫은 뒤 원래 탭으로 돌아가기", "광고 탭을 닫으면 그 탭을 연 탭을 다시 앞으로 가져온다.",
            "Return to the opener", "After closing an ad tab, bring the tab that opened it back to the front.");
        Field("useBuiltinAdDomains", "내장 광고 도메인 목록 사용", "TabBouncer에 들어 있는 광고 도메인 목록을 사용자 목록과 함께 적용한다.",
            "Use the built-in ad domain list", "Apply TabBouncer's built-in ad domain list together with your own list.");
        Field("strictMode", "엄격 모드", "다른 사이트에서 열린 모든 탭을 점수로 판정한다. 정상 팝업이 닫힐 가능성이 커진다.",
            "Strict mode", "Score every tab opened by another site. Legitimate popups are more likely to be closed.");
        Field("closeThreshold", "차단 기준 점수", "이 점수 이상이면 닫거나 되돌린다. 낮출수록 더 많이 막고 오탐도 늘어난다. (기본 80)",
            "Block threshold", "Tabs at or above this score are closed or reverted. Lower means more blocking and more false positives. (default 80)");
        Field("debounceMs", "판정 대기 (ms)", "새 탭이 생긴 뒤 최종 판정까지 기다리는 시간이다. (기본 1200)",
            "Decision delay (ms)", "How long to wait after a tab appears before the final verdict. (default 1200)");
        Field("intentWindowMs", "클릭 연결 시간 (ms)", "클릭을 이 시간 안에 생긴 새 탭·이동과 연결한다. (기본 3500)",
            "Click window (ms)", "A click is linked to new tabs or navigations within this time. (default 3500)");
        Field("startMonitoringOnLaunch", "실행하면 바로 감시 시작",
            "기본은 꺼짐이다. 처음에는 관측 모드로 결과를 확인한 뒤 켜는 것을 권한다.",
            "Start monitoring on launch", "Off by default. We recommend checking results in observe mode first.");
        Field("autoRun", "Windows에 로그인하면 자동 실행", "로그인할 때 트레이에 최소화된 상태로 시작한다. 이 값은 config.json이 아니라 Windows에 저장한다.",
            "Start with Windows", "Start minimized to the tray when you sign in. Stored in Windows, not in config.json.");
        Field("minimizeToTray", "최소화하면 트레이로 숨기기", "최소화하면 작업 표시줄에서 사라지고 알림 영역 아이콘으로만 남는다.",
            "Minimize to tray", "When minimized, hide from the taskbar and stay as a notification area icon.");
        Field("closeToTray", "닫기(X)를 누르면 트레이로 숨기기", "끄면 닫기 버튼이 프로그램을 종료한다.",
            "Close to tray", "When off, the close button exits the application.");
        Field("notifyOnBlock", "광고를 막으면 알림 표시", "탭을 닫거나 이동을 되돌리면 Windows 알림을 띄운다. 3초 안의 여러 건은 묶는다.",
            "Notify when blocking", "Show a Windows notification when a tab is closed or a redirect is reverted. Bursts within 3 seconds are grouped.");
        Field("closeChromeOnExit", "종료할 때 전용 Chrome", "TabBouncer를 완전히 종료할 때 전용 Chrome을 어떻게 할지 정한다.",
            "Dedicated Chrome on exit", "What to do with the dedicated Chrome when TabBouncer exits.");
        Field("language", "화면 언어", "다시 실행하면 적용된다. 활동 로그는 한국어로 남는다.",
            "Language", "Applied after restart. The activity log stays in Korean.");
        Field("chromePath", "브라우저 경로", "비우면 Chrome, Edge, Brave, Chromium 순서로 찾는다.",
            "Browser path", "Leave empty to look for Chrome, Edge, Brave, then Chromium.");
        Add("config.field.chromePath.placeholder", "자동으로 찾기", "Find automatically");
        Field("startUrl", "시작 페이지", "전용 Chrome을 열 때 보여 줄 주소다. 비우면 안내 페이지를 연다.",
            "Start page", "Address to show when the dedicated Chrome opens. Leave empty for the guide page.");
        Add("config.field.startUrl.placeholder", "안내 페이지", "Guide page");
        Field("userDataDir", "전용 프로필 폴더", "비우면 데이터 폴더의 ChromeProfile을 쓴다. 평소 쓰는 Chrome 프로필은 지정할 수 없다.",
            "Dedicated profile folder", "Leave empty to use ChromeProfile in the data folder. Your everyday Chrome profile cannot be used.");
        Field("debugPort", "디버깅 포트", "0이면 빈 포트를 자동으로 쓰고 전용 프로필의 Chrome에만 연결한다. 직접 지정하면 그 포트의 Chrome에 연결한다.",
            "Debugging port", "0 picks a free port and connects only to the dedicated profile's Chrome. A fixed port connects to whichever Chrome uses it.");
        Field("autoLaunchChrome", "실행할 때 전용 Chrome 열기", "끄면 이미 실행 중인 전용 Chrome이 있을 때만 연결한다.",
            "Open the dedicated Chrome on launch", "When off, TabBouncer only connects to a dedicated Chrome that is already running.");

        Add("config.list.add", "추가", "Add");
        Add("config.list.addTitle", "새 항목 추가: 아래 칸에 입력하고 Enter나 \"추가\"를 누른다",
            "Add a new entry: type it below and press Enter or \"Add\"");
        Add("config.list.readonlyTitle", "읽기 전용 목록이라 여기에는 추가할 수 없다",
            "This list is read-only, so entries cannot be added here");
        Add("config.list.count", "등록된 항목 {0}개", "{0} entries");
        Add("config.list.remove", "선택 항목 삭제", "Remove selected");
        Add("config.list.shortcut", "바탕화면 바로가기 만들기", "Create desktop shortcut");
        Add("config.list.placeholder.domain", "예: example.com   (주소를 붙여 넣어도 되고, 여러 개는 쉼표로 구분)",
            "e.g. example.com   (you can paste an address; separate several with commas)");
        Add("config.list.placeholder.url", "예: https://example.com",
            "e.g. https://example.com");
        AddList("allowedSites", "정상 사이트 (allowedSites)", "등록한 도메인과 모든 하위 도메인의 탭·창·이동은 점수와 관계없이 유지한다. GUI의 \"정상 사이트로 등록\"이 여기에 추가한다.",
            "Allowed sites (allowedSites)", "Tabs, windows and navigations on these domains and all their subdomains are always kept. \"Allow site\" in the main window adds here.");
        AddList("watchedSites", "감시 사이트 (watchedSites)", "광고가 자주 뜨는 사이트다. 이 사이트가 연 탭은 +45점, 이 사이트에서 떠나는 이동은 +15점을 받는다.",
            "Watched sites (watchedSites)", "Sites that often spawn ads. Tabs they open get +45 and navigations leaving them get +15.");
        AddList("adDomains", "광고 도메인, 직접 추가 (adDomains)", "내장 목록에 더해 적용할 광고 도메인이다. 새 탭 +80점, 현재 탭 이동 +20점. GUI의 \"광고 도메인으로 등록\"이 여기에 추가한다.",
            "Ad domains, your additions (adDomains)", "Ad domains applied on top of the built-in list: +80 for new tabs, +20 for navigations. \"Mark as ad domain\" adds here.");
        AddList("removedAdDomains", "내장 광고 목록에서 제외 (removedAdDomains)", "내장 광고 목록에 있지만 광고로 보지 않을 도메인이다.",
            "Excluded built-in ad domains (removedAdDomains)", "Domains in the built-in ad list that should not be treated as ads.");
        AddList("builtinAdDomains", "내장 광고 도메인 (읽기 전용)", "TabBouncer에 들어 있는 목록이다. 빼려면 이름을 \"내장 광고 목록에서 제외\"에 추가한다.",
            "Built-in ad domains (read only)", "The list shipped with TabBouncer. To exclude one, add it to \"Excluded built-in ad domains\".");
        AddList("whitelist", "기본 보호 목록 (whitelist)", "로그인·결제 서비스처럼 항상 유지할 도메인이다. 효과는 정상 사이트와 같다.",
            "Protected services (whitelist)", "Sign-in and payment services that are always kept. Works the same as allowed sites.");
        AddList("suspiciousTlds", "의심 도메인 끝 (suspiciousTlds)", "이 최상위 도메인(예: xyz)으로 끝나는 주소는 +15점을 받는다.",
            "Suspicious TLDs (suspiciousTlds)", "Addresses ending in these top-level domains (e.g. xyz) get +15.");
        AddList("favoriteSites", "즐겨찾기 사이트 (favoriteSites)", "안내 페이지에 바로가기로 보여 준다. 선택한 뒤 바탕화면 바로가기를 만들면 TabBouncer 전용 Chrome에서 바로 열린다.",
            "Favorite sites (favoriteSites)", "Shown as shortcuts on the guide page. Create a desktop shortcut to open a site directly in the dedicated Chrome.");
    }

    private static void Field(string key, string ko, string koHelp, string en, string enHelp)
    {
        Add("config.field." + key, ko, en);
        Add("config.field." + key + ".help", koHelp, enHelp);
    }

    private static void AddList(string key, string ko, string koHelp, string en, string enHelp)
    {
        Add("config.list." + key, ko, en);
        Add("config.list." + key + ".help", koHelp, enHelp);
    }
}
