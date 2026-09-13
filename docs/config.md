# 설정 레퍼런스

TabBouncer의 모든 설정은 `config.json` 한 파일에 있다. 보통은 TabBouncer 창의 **설정 열기**에서 바꾸고, 이 문서는 키 하나하나의 뜻과 기본값을 확인할 때 쓴다.

## 설정 파일 위치

TabBouncer는 아래 순서로 설정 폴더를 정한다.

1. `--data-dir=경로`를 지정하면 그 폴더를 쓴다. 로그와 전용 프로필도 같은 폴더에 둔다.
2. 실행 파일이 있는 폴더에 쓸 수 있으면 그 폴더의 `config.json`을 쓴다. 폴더째 들고 다니는 포터블 사용에 맞다.
3. 실행 파일 폴더에 쓸 수 없으면(`C:\Program Files` 등) `%LOCALAPPDATA%\TabBouncer\config.json`을 쓴다. 이때 실행 파일 옆에 `config.json`이 있으면 처음 한 번만 복사해 시작값으로 삼는다.

지금 쓰는 파일 경로는 설정 창 맨 위와 활동 로그의 "설정 파일:" 줄에 나온다.

파일이 없으면 기본값으로 새로 만든다. 설정 창에서 **기본값으로 복원**을 누르고 저장해도 기본값으로 돌아간다.

## 바뀐 설정이 적용되는 방식

- 설정 창에서 저장하면 값을 검사한 뒤 바로 적용한다. 검사에 실패하면 저장하지 않고 이유를 보여 준다.
- 파일을 메모장 등으로 직접 고쳐 저장해도 자동으로 다시 읽는다.
- `enabled`는 읽지 않는다. 감시는 실행할 때마다 꺼진 상태로 시작하며 GUI에서만 켜고 끈다. 바로 켜고 싶으면 `startMonitoringOnLaunch`를 쓴다.
- `dryRun`(관측 모드)은 GUI에서 잠시 바꾼 값이 우선한다. 파일의 `dryRun` 값을 실제로 바꾸거나 설정 창에서 저장했을 때만 파일 값을 따른다.
- 명령줄 인수 `--port`, `--url`, `--strict`, `--no-preempt`로 준 값은 파일을 다시 읽어도 유지된다.
- `language`는 TabBouncer를 다시 실행해야 적용된다.

## 전체 키

### 감시와 판정

| 키 | 형식 | 기본값 | 뜻 |
|---|---|---|---|
| `enabled` | bool | `true` | 파일 값은 쓰지 않는다. 호환을 위해 남아 있다. |
| `dryRun` | bool | `false` | 관측 모드. 탭을 닫거나 이동을 되돌리지 않고, 판정 결과만 목록과 로그에 남긴다. |
| `strictMode` | bool | `false` | 감시 사이트나 광고 도메인이 아니어도 다른 사이트에서 열린 모든 탭을 점수로 판정한다. 정상 팝업이 닫힐 가능성이 커진다. |
| `preemptiveBlock` | bool | `true` | 새 탭을 로드 전에 잠시 멈추고 먼저 판정한다. 광고 페이지가 보이는 시간을 줄인다. |
| `refocusOpener` | bool | `true` | 광고 탭을 닫은 뒤 그 탭을 연 탭을 앞으로 가져온다. |
| `blockAutomaticCrossSitePopups` | bool | `true` | 감시 사이트가 아니어도, 클릭 없이 또는 클릭과 다르게 열린 다른 사이트 탭을 판정 범위에 넣는다. |
| `protectExplicitClicks` | bool | `true` | 클릭 의도를 수집해 목적지가 같은 탭을 보호한다. 끄면 모든 탭을 클릭 정보 없이 판정한다. |
| `blockRedirectHijack` | bool | `true` | 보던 탭이 클릭 없이 다른 사이트로 넘어가면 원래 페이지로 되돌린다. |
| `closeThreshold` | int | `80` | 이 점수 이상이면 닫거나 되돌린다. 1~300. 이 값에서 30을 뺀 점수 이상이면 유지하더라도 이벤트와 목록에 남긴다. |
| `debounceMs` | int | `1200` | 새 탭이 생긴 뒤 최종 판정까지 기다리는 시간(밀리초). 200~10000. |
| `intentWindowMs` | int | `3500` | 클릭을 이 시간 안에 생긴 새 탭·이동과 연결한다(밀리초). 500~20000. |

### 사이트 목록

| 키 | 형식 | 기본값 | 뜻 |
|---|---|---|---|
| `allowedSites` | string[] | `[]` | 정상 사이트. 등록한 도메인과 모든 하위 도메인의 탭·창·이동은 점수와 관계없이 유지한다. GUI의 "정상 사이트로 등록"이 여기에 추가한다. |
| `watchedSites` | string[] | `[]` | 광고가 자주 뜨는 사이트. 이 사이트가 연 탭은 +45점, 이 사이트에서 떠나는 자동 이동은 +15점이다. GUI의 "감시 사이트로 등록"이 여기에 추가한다. |
| `useBuiltinAdDomains` | bool | `true` | 프로그램에 들어 있는 광고 도메인 목록을 함께 쓴다. |
| `adDomains` | string[] | `[]` | 내장 목록에 더해 쓸 광고 도메인. 새 탭 +80점, 현재 탭 이동 +20점, 광고 페이지가 연 탭 +20점이다. GUI의 "광고 도메인으로 등록"이 여기에 추가한다. |
| `removedAdDomains` | string[] | `[]` | 내장 광고 목록에 있지만 광고로 보지 않을 도메인. |
| `whitelist` | string[] | 로그인·결제 9개 | 항상 유지할 로그인·결제 서비스. 효과는 `allowedSites`와 같다. 기본값은 `accounts.google.com`, `login.microsoftonline.com`, `appleid.apple.com`, `github.com`, `nid.naver.com`, `accounts.kakao.com`, `toss.im`, `kftc.or.kr`, `paypal.com`이다. |
| `suspiciousTlds` | string[] | 10개 | 이 최상위 도메인으로 끝나는 주소는 +15점이다. 기본값은 `top`, `xyz`, `buzz`, `click`, `link`, `cyou`, `icu`, `sbs`, `rest`, `lol`이다. |
| `favoriteSites` | string[] | `[]` | 안내 페이지에 바로가기로 보여 줄 주소. 설정 창에서 바탕화면 바로가기도 만들 수 있다. |

내장 광고 도메인은 `popads.net`, `popcash.net`, `propellerads.com`, `adsterra.com`, `exoclick.com`, `exdynsrv.com`, `hilltopads.net`, `adcash.com`, `clickadu.com`, `trafficstars.com`, `juicyads.com`, `bodelen.com`, `onclickalgo.com`, `onclckpro.com`, `onclasrv.com`, `bidgear.com`, `doubleclick.net`, `adnxs.com`, `adsrvr.org`, `mgid.com`, `revcontent.com`, `taboola.com`, `outbrain.com`, `zeropark.com`, `clickaine.com`, `popunder.net`, `richads.com`, `monetag.com`, `incompetencesorting.com`이다. 새 버전에서 이 목록이 늘어나면 기존 사용자에게도 바로 적용된다.

### 프로그램

| 키 | 형식 | 기본값 | 뜻 |
|---|---|---|---|
| `startMonitoringOnLaunch` | bool | `false` | 실행하면 바로 감시를 시작한다. 처음에는 관측 모드로 결과를 확인한 뒤 켜는 것을 권한다. |
| `minimizeToTray` | bool | `true` | 최소화하면 작업 표시줄에서 사라지고 알림 영역 아이콘으로만 남는다. |
| `closeToTray` | bool | `true` | 닫기(X)를 누르면 트레이로 숨긴다. 끄면 닫기가 프로그램을 종료한다. |
| `notifyOnBlock` | bool | `true` | 탭을 닫거나 이동을 되돌리면 Windows 알림을 띄운다. 3초 안의 여러 건은 한 번에 알린다. |
| `closeChromeOnExit` | `"ask"` / `"always"` / `"never"` | `"ask"` | 완전히 종료할 때 전용 Chrome을 닫을지. `ask`는 매번 묻는다. 대화상자에서 "다음부터 묻지 않기"를 고르면 선택이 여기에 저장된다. |
| `language` | `""` / `"ko"` / `"en"` | `""` | 화면 언어. 비우면 Windows 표시 언어가 한국어일 때 한국어, 아니면 영어다. 활동 로그 문구는 한국어로 남는다. |

**Windows에 로그인하면 자동 실행**은 설정 창에만 있고 `config.json`에는 저장하지 않는다. 켜면 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`에 `TabBouncer` 값을 `"실행 파일 경로" --minimized`로 쓴다. 프로그램 폴더를 옮긴 뒤 한 번 실행하면 경로를 새 위치로 고친다. `--data-dir`로 실행한 인스턴스에서는 바꿀 수 없다.

### 브라우저

| 키 | 형식 | 기본값 | 뜻 |
|---|---|---|---|
| `debugPort` | int | `0` | CDP 디버깅 포트. `0`이면 브라우저가 빈 포트를 고르고, TabBouncer는 전용 프로필의 `DevToolsActivePort` 파일로 포트를 찾아 **전용 프로필 브라우저에만** 연결한다. 1024~65535로 지정하면 그 포트의 브라우저에 연결한다. |
| `autoLaunchChrome` | bool | `true` | 실행할 때 전용 브라우저를 연다. 끄면 이미 실행 중인 전용 브라우저가 있을 때만 연결한다. |
| `chromePath` | string | `""` | 브라우저 실행 파일. 비우면 Chrome, Edge, Brave, Chromium 순서로 표준 설치 경로에서 찾는다. 환경 변수(`%LOCALAPPDATA%` 등)를 쓸 수 있다. |
| `userDataDir` | string | `""` | 전용 프로필 폴더. 비우면 데이터 폴더의 `ChromeProfile`이다. 평소 Chrome 프로필은 Chrome 136부터 원격 디버깅이 금지돼 쓸 수 없다. |
| `startUrl` | string | `""` | 전용 브라우저를 열 때 보여 줄 주소. 비우면 안내 페이지를 연다. |
| `chromeArguments` | string[] | `[]` | 브라우저 실행 인수 추가분. 예: `["--lang=en"]` |

## 도메인 목록 표기 규칙

- `example.com`은 `example.com`과 `www.example.com`, `a.b.example.com` 같은 모든 하위 도메인에 맞는다.
- 앞에 붙인 `*.`나 `.`은 무시한다. `*.example.com`은 `example.com`과 같다.
- 대소문자를 구분하지 않는다.
- 설정 창에 `https://www.example.com/path`처럼 주소를 붙여 넣으면 호스트(`www.example.com`)만 남긴다. GUI의 등록 버튼은 `www.example.com`이 아니라 `example.com`처럼 사이트 단위로 등록한다.
- 쉼표나 공백으로 여러 개를 한 번에 추가할 수 있다.

## 설정 예시

### 처음 며칠은 관측만 한다

```json
{
  "dryRun": true
}
```

TabBouncer 창의 관측 모드 체크박스로도 같은 효과를 낸다. "관측(닫을 대상)" 항목을 보고 오탐이 없으면 끈다.

### 특정 사이트를 강하게 막는다

```json
{
  "watchedSites": ["free-webtoon.example", "stream.example"],
  "favoriteSites": ["https://free-webtoon.example/", "https://stream.example/"]
}
```

### Edge를 전용 브라우저로 쓴다

```json
{
  "chromePath": "%ProgramFiles(x86)%\\Microsoft\\Edge\\Application\\msedge.exe"
}
```

Chrome이 설치돼 있지 않으면 지정하지 않아도 Edge를 찾는다.

### USB에 넣어 들고 다닌다

프로그램 폴더를 USB에 복사하고 `--data-dir`로 데이터도 같은 USB에 둔다. 바로가기 대상에 인수를 넣는다.

```text
E:\TabBouncer\tabbouncer.exe --data-dir=E:\TabBouncer\data
```
