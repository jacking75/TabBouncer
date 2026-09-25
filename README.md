# TabBouncer

[![최신 릴리스](https://img.shields.io/github/v/release/jacking75/TabBouncer?label=%EB%A6%B4%EB%A6%AC%EC%8A%A4)](https://github.com/jacking75/TabBouncer/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

[한국어](README.md) | [English](README.en.md)

![TabBouncer가 사용자 요청 탭은 통과시키고 자동 광고 팝업을 차단하는 모습](docs/images/tabbouncer-hero.png)

TabBouncer는 Windows 11의 Chrome에서 자동으로 생기는 광고 탭과 팝업 창을 감지해 닫는 프로그램이다. 사용자가 눈에 보이는 링크나 폼을 직접 선택해 연 탭은 목적지 URL을 대조해 유지한다.

> **TabBouncer는 TabBouncer가 연 전용 Chrome 창만 보호한다.** 평소 쓰는 Chrome 창은 건드리지 않는다. 광고 팝업이 많은 사이트는 전용 창의 주소창이나 TabBouncer 창의 주소 입력란에서 연다.

![데모 페이지에서 링크를 누르면 목적지 탭은 유지되고 함께 열린 광고 탭은 닫히며 TabBouncer 창의 최근 목록에 기록되는 모습](docs/images/demo.gif)

## 어떤 앱인가

광고 탭을 무조건 닫는 도구가 아니다. 사용자가 원해서 연 탭은 지키고, 웹페이지가 몰래 추가로 여는 광고 탭·팝업·강제 리다이렉트만 걸러낸다.

| 특징 | 사용자에게 좋은 점 |
|---|---|
| 클릭한 링크의 목적지와 실제 새 탭 URL을 대조 | 보고 싶어서 연 링크와 로그인·결제 창을 최대한 유지한다. |
| 클릭 없이 생긴 외부 탭과 광고 도메인을 점수로 판정 | 한 번의 클릭에서 부수적으로 열리는 광고 탭을 자동으로 닫는다. |
| 현재 탭의 자동 외부 이동도 감시 | 읽던 페이지가 광고 사이트로 바뀌면 원래 페이지로 되돌린다. |
| 판정 사유와 점수 내역, 관측 모드, 한 번에 예외 등록 | 왜 닫혔는지 확인하고, 잘못 닫힌 사이트나 놓친 광고를 바로 등록한다. |
| 트레이 상주, 차단 알림, 로그인 시 자동 실행 | 창을 띄워 두지 않아도 조용히 보호하고, 막을 때만 알린다. |

### 이런 곳에서 사용한다

- 무료 다운로드·스트리밍·웹툰·커뮤니티처럼 클릭 뒤 광고 탭이 자주 열리는 사이트를 볼 때 사용한다.
- 링크를 눌렀는데 별도 창이 여러 개 뜨거나, 현재 탭이 다른 광고 사이트로 넘어가는 환경에서 사용한다.
- 가족이나 팀원이 반복 팝업 때문에 불편을 겪지만 브라우저 개발자 도구를 직접 다루기 어려울 때 사용한다.

## 다운로드와 설치

### 요구 환경

- Windows 11 64비트
- Google Chrome 136 이상. Chrome이 없으면 Microsoft Edge, Brave, Chromium을 차례로 찾는다. Edge는 스모크 테스트로 확인했고 Brave·Chromium은 실험적이다.
- 런타임 포함 버전이 아니면 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)

### winget으로 설치하기

Windows 패키지 관리자 winget에서 다음 명령으로 설치한다. 등록된 패키지 ID는 `jacking75.TabBouncer`이며, 런타임이 포함된 64비트 버전을 사용한다.

```powershell
winget install --id jacking75.TabBouncer --exact --source winget
```

설치 후 새 터미널에서 `tabbouncer`를 실행한다. 공개 원본에 게시된 버전과 패키지 정보는 `winget show --id jacking75.TabBouncer --exact --source winget`으로 확인할 수 있다.

### ZIP 파일로 설치하기

[Releases](https://github.com/jacking75/TabBouncer/releases/latest)에서 둘 중 하나를 받는다.

| 파일 | 고르는 기준 |
|---|---|
| `TabBouncer-vX.Y.Z-win-x64.zip` | 파일이 작다. .NET 10 Desktop Runtime이 설치돼 있거나 설치할 수 있으면 이것을 쓴다. |
| `TabBouncer-vX.Y.Z-win-x64-selfcontained.zip` | 런타임이 들어 있어 따로 설치할 것이 없다. 파일이 크다. |

압축은 아무 폴더에나 풀면 된다. `%LOCALAPPDATA%\Programs\TabBouncer`를 권한다. `C:\Program Files`처럼 쓰기 권한이 없는 폴더에 풀면 설정 파일은 자동으로 `%LOCALAPPDATA%\TabBouncer\config.json`에 저장된다. 배포 파일에는 `config.json`이 들어 있지 않아, 새 버전을 기존 폴더에 덮어써도 설정이 유지된다.

### 처음 실행할 때

TabBouncer 실행 파일은 아직 코드 서명이 없어 Windows SmartScreen이 "Windows의 PC 보호" 창을 띄울 수 있다. 받은 파일이 릴리스의 `SHA256SUMS.txt`와 같은지 확인한 뒤 **추가 정보 → 실행**을 누른다.

```powershell
Get-FileHash .\TabBouncer-v1.1.0-win-x64.zip -Algorithm SHA256
```

1. `tabbouncer.exe`를 실행한다.
2. **감시 시작**을 누른다. 전용 Chrome 창이 함께 열린다.
3. 전용 창의 주소창에 보고 싶은 사이트를 입력한다.

처음 며칠은 **관측 모드**를 켜 두면 탭을 닫지 않고 "닫았을 탭"만 목록에 기록한다. 결과가 괜찮으면 관측 모드를 끈다.

## 사용 방법

### 1. 감시를 시작한다

![TabBouncer 메인 창. 감시 상태, 작동 모드, 차단 개수 카드와 최근 차단 목록, 선택한 항목의 점수 내역이 보인다.](docs/images/001.png)

프로그램을 실행하면 감시는 안전을 위해 꺼진 상태로 시작한다. 노란 배너가 보이면 **감시 시작**을 누른다. 전용 Chrome이 닫혀 있으면 감시 시작과 함께 열린다. 매번 누르기 번거로우면 **설정 → 일반 → 실행하면 바로 감시 시작**을 켠다.

감시 중인데 전용 Chrome을 모두 닫으면 주황 배너와 창 제목이 "Chrome 닫힘"을 알린다. TabBouncer는 닫은 Chrome을 멋대로 다시 띄우지 않는다. **Chrome 열기**를 누르면 다시 연다.

### 2. 전용 Chrome 창에서 웹을 연다

![TabBouncer 전용 Chrome의 안내 페이지. 감시 상태, 감시 일시중지와 관측 모드 버튼, 이번 실행 차단 개수, 즐겨찾기 사이트가 보인다.](docs/images/002.png)

전용 Chrome은 평소 프로필과 분리된 창이다. 사이트는 다음 방법 중 편한 것으로 연다.

- 전용 창의 주소창에 입력한다.
- TabBouncer 창의 주소 입력란에 입력하고 Enter를 누른다(Ctrl+L로 바로 이동).
- 안내 페이지의 **즐겨찾기 사이트** 링크를 누른다. 목록은 설정의 사이트 목록 탭에서 관리한다.
- 설정에서 즐겨찾기 사이트를 선택하고 **바탕화면 바로가기 만들기**를 누른다. TabBouncer가 실행 중이면 바로가기가 그 전용 Chrome에 새 탭으로 연다. 실행 중이 아니면 TabBouncer를 켜고 그 주소로 전용 Chrome을 연다. 이때 감시는 평소처럼 꺼진 상태로 시작하므로 **감시 시작**을 누르거나 "실행하면 바로 감시 시작"을 켜 둔다.

안내 페이지에서도 감시 일시중지와 관측 모드를 바로 바꿀 수 있다.

### 3. 결과를 확인하고 예외를 등록한다

![최근 차단 목록과 상세 패널. 같은 사이트에서 반복된 광고 탭이 한 행으로 묶여 횟수가 보이고, 선택한 항목의 종류, 주소, 돌아간 페이지, 항목별 점수가 보인다.](docs/images/004.png)

**최근 차단한 탭** 목록은 닫은 탭, 되돌린 리다이렉트, 기준에 조금 못 미쳐 유지한 탭, 관측 모드에서 닫았을 탭을 보여 준다. 같은 사이트(호스트)에서 같은 종류가 반복되면 새 행을 만들지 않고 **횟수** 열의 숫자를 올린다. 이 행은 가장 최근 시각과 주소를 보여 주고, 상세 패널에 반복 횟수와 처음 시각이 나온다. 항목을 선택하면 아래 상세 패널에 판정 사유와 항목별 점수가 나온다. 이전 실행의 기록도 흐린 글자로 함께 보인다.

| 상황 | 할 일 |
|---|---|
| 정상 사이트가 닫혔다 | 항목을 선택하고 **선택한 탭 다시 열기**, **선택한 사이트 등록**을 누른다. 등록한 도메인과 하위 도메인은 다시 닫지 않는다. |
| 광고인데 닫히지 않았다 | "유지" 항목을 선택하고 **광고 도메인으로 등록**을 누른다. 그 탭이 아직 열려 있으면 바로 닫는다. |
| 특정 사이트에서 광고가 자주 뜬다 | 그 사이트에서 열린 항목을 선택하고 **감시 사이트로 등록**을 누른다. 이후 그 사이트가 여는 탭을 더 적극적으로 판정한다. |

항목을 선택하지 않으면 **다시 열기**와 **정상 사이트로 등록**은 가장 최근에 닫은 항목에 적용된다.

### 4. 트레이에서 조용히 쓴다

창을 최소화하거나 닫기(X)를 누르면 알림 영역 아이콘으로 숨는다. 광고를 막으면 Windows 알림이 뜬다(3초 안의 여러 건은 한 번에 알린다). 트레이 아이콘 메뉴에서 감시·관측 모드·Chrome 열기·로그 폴더 열기·종료를 할 수 있다. 트레이로 숨기지 않고 완전히 끝내려면 창 오른쪽 위의 **완전 종료** 버튼이나 트레이 메뉴의 **종료**를 누른다. 완전히 종료할 때는 전용 Chrome도 닫을지 묻는다.

**설정 → 일반 → Windows에 로그인하면 자동 실행**을 켜면 로그인할 때 트레이에 최소화된 상태로 시작한다.

### 키보드 단축키

| 키 | 동작 |
|---|---|
| Ctrl+M | 감시 켜기/일시중지 |
| Ctrl+D | 관측 모드 켜기/끄기 |
| Ctrl+O | 전용 Chrome 열기 |
| Ctrl+L | 주소 입력란으로 이동 |
| Ctrl+Z | 선택한(없으면 최근) 탭 다시 열기 |
| Ctrl+, | 설정 열기 |
| Ctrl+Q | 완전 종료(트레이로 숨기지 않음) |
| Ctrl+S / Esc | 설정 창에서 저장 / 취소 |

## 판정 방식

프로그램은 Chrome DevTools Protocol(CDP)로 전용 Chrome의 새 탭과 이동을 감시한다. 각 페이지에는 클릭 의도만 수집하는 짧은 스크립트를 페이지 스크립트가 볼 수 없는 격리 환경에 넣는다.

| 상황 | 처리 |
|---|---|
| 눈에 보이는 링크를 클릭했고 새 탭 URL이 링크 목적지와 일치함 | 유지 |
| 링크를 클릭했지만 별개의 외부 탭이 함께 열림 | 광고 탭으로 판정 |
| 빈 영역이나 투명 링크를 눌렀을 때 외부 탭이 열림 | 광고 탭으로 판정 |
| 버튼 등 명시적인 컨트롤을 눌러 일반 외부 창이 열림 | 사용자 동작으로 가중치를 낮춰 유지 |
| 사용자 동작 없이 외부 탭이 생성됨 | 광고 탭으로 판정 |
| 주소창 입력, 새 탭 버튼 등 opener 없는 일반 탭 | 유지 |
| 로그인, OAuth, 결제 흐름 또는 정상 사이트 | 유지 |
| 페이지 스크립트가 클릭 없이 현재 탭을 다른 사이트로 이동시킴 | 원래 페이지로 복귀 |
| 주소창 입력, 북마크, 뒤로 가기로 현재 탭을 이동함 | 유지 |
| TabBouncer가 연결되기 전부터 열려 있던 탭 | 유지 |

점수표, 시간 기준, 예외 규칙, 한계는 [판정 방식 상세](docs/how-it-works.md)에 있다.

## 설정

**설정 열기**를 누르면 일반, 사이트 목록, 고급(JSON) 탭으로 `config.json`을 편집한다. 저장하면 검사한 뒤 바로 적용한다. 파일을 직접 고쳐 저장해도 자동으로 다시 읽는다.

![설정 창의 일반 탭. 감시와 판정 옵션이 설명과 함께 보인다.](docs/images/003.png)

자주 쓰는 항목은 다음과 같다.

```json
{
  "dryRun": false,
  "allowedSites": ["my-safe-site.example"],
  "watchedSites": ["problem-site.example"],
  "favoriteSites": ["https://problem-site.example/"]
}
```

- `dryRun`은 관측 모드다. 탭을 닫지 않고 판정 결과만 기록한다.
- `allowedSites`에 넣은 도메인과 하위 도메인은 항상 유지한다.
- `watchedSites`는 광고가 자주 뜨는 사이트다. 이 사이트가 여는 탭을 더 적극적으로 판정한다.
- `favoriteSites`는 안내 페이지의 바로가기다.

화면 언어는 **설정 → 일반 → 화면 언어**에서 고르고, 다시 실행하면 적용된다. 한국어가 기본이고 영어를 지원한다. 화면 문구와 활동 로그 문구는 실행 파일 옆 `languages.json` 한 파일에 언어별로 들어 있어, 이 파일에 언어를 더하면 목록에 나타난다. 형식은 [설정 레퍼런스의 언어 파일](docs/config.md#언어-파일-languagesjson)에 있다.

모든 키와 기본값은 [설정 레퍼런스](docs/config.md), 명령줄 인수는 [명령줄 인수](docs/cli.md)에 있다.

## 문제 해결과 도움말

- [문제 해결](docs/troubleshooting.md): 광고가 안 닫힐 때, 정상 탭이 닫힐 때, Chrome에 연결되지 않을 때
- [자주 묻는 질문](docs/faq.md): 왜 확장이 아닌지, 평소 Chrome은 왜 보호하지 않는지, 광고 차단 확장과 같이 써도 되는지
- [로그 읽는 법](docs/logs.md): `tabbouncer.log`와 `events.jsonl` 형식
- 버그·오탐·미탐 신고는 [Issues](https://github.com/jacking75/TabBouncer/issues/new/choose)에 한다. TabBouncer 창의 **진단 정보 복사**로 버전과 설정 요약을 붙여 넣으면 빨리 확인할 수 있다.

## 개인정보와 보안

- TabBouncer는 네트워크로 아무것도 보내지 않는다. 업데이트 확인이나 사용 통계 수집도 없다.
- 활동 로그와 판정 이벤트는 이 PC의 `%LOCALAPPDATA%\TabBouncer`에만 남는다. 로그에는 방문한 주소가 들어 있으므로 공유하기 전에 지운다. 각 로그 파일은 5MB를 넘으면 이전 파일 두 개까지만 남긴다.
- 전용 Chrome의 디버깅 포트는 `127.0.0.1`에만 열린다. 다만 같은 PC에서 실행 중인 다른 프로그램은 이 포트로 전용 Chrome을 제어할 수 있다. 신뢰하지 않는 프로그램이 도는 PC에서는 전용 창에서 중요한 계정에 로그인하지 않는다.
- 전용 프로필은 평소 Chrome 프로필과 분리돼 있어 비밀번호·확장·북마크를 공유하지 않는다.

## 제거

winget으로 설치했다면 먼저 TabBouncer의 자동 실행 설정을 끄고 완전히 종료한 뒤 `winget uninstall --id jacking75.TabBouncer --exact`를 실행한다. 사용자 설정과 로그를 함께 지우려면 `%LOCALAPPDATA%\TabBouncer` 폴더도 지운다.

ZIP 파일로 설치했다면 다음 순서로 제거한다.

1. 창의 **완전 종료** 버튼이나 트레이 아이콘 메뉴의 **종료**를 누른다.
2. 자동 실행을 켰다면 먼저 **설정 → 일반 → Windows에 로그인하면 자동 실행**을 끄고 저장한다. 이미 폴더를 지웠다면 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`의 `TabBouncer` 값을 지운다.
3. 압축을 푼 프로그램 폴더를 지운다.
4. `%LOCALAPPDATA%\TabBouncer` 폴더를 지운다. 전용 Chrome 프로필, 로그, 통계가 함께 지워진다.
5. 바로가기를 만들었다면 바탕화면의 `TabBouncer - 사이트.lnk`를 지운다.

## 개발자용

```powershell
dotnet build src -c Release
dotnet run --project src -c Release -- --self-test
node .\tests\browser-smoke.mjs
```

빌드 결과는 `bin\Release`에 생긴다. 소스 구조, 코드 읽는 순서, 기여 규칙은 [CONTRIBUTING.md](CONTRIBUTING.md)에 있다. 변경 이력은 [CHANGELOG.md](CHANGELOG.md)에 있다.

## 라이선스

[MIT License](LICENSE)
