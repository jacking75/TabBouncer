# TabBouncer

![TabBouncer가 사용자 요청 탭은 통과시키고 자동 광고 팝업을 차단하는 모습](docs/images/tabbouncer-hero.png)

TabBouncer는 Windows 11의 Chrome에서 자동으로 생기는 광고 탭과 팝업 창을 감지해 닫는 .NET 8 프로그램이다. 사용자가 눈에 보이는 링크나 폼을 직접 선택해 연 탭은 목적지 URL을 대조해 유지한다.

## 판정 방식

프로그램은 Chrome DevTools Protocol(CDP)로 새 탭을 감시한다. 각 페이지에는 클릭 의도만 수집하는 짧은 스크립트를 CDP로 주입한다.

| 상황 | 처리 |
|---|---|
| 눈에 보이는 링크를 클릭했고 새 탭 URL이 링크 목적지와 일치함 | 유지 |
| 링크를 클릭했지만 별개의 외부 탭이 함께 열림 | 광고 탭으로 판정 |
| 빈 영역이나 투명 링크를 눌렀을 때 외부 탭이 열림 | 광고 탭으로 판정 |
| 버튼 등 명시적인 컨트롤을 눌러 일반 외부 창이 열림 | 사용자 동작으로 가중치를 낮춰 유지 |
| 사용자 동작 없이 외부 탭이 생성됨 | 광고 탭으로 판정 |
| 주소창 입력, 새 탭 버튼 등 opener 없는 일반 탭 | 유지 |
| 로그인, OAuth, 결제 흐름 또는 화이트리스트 도메인 | 유지 |

클릭한 URL이 최초 요청과 일치하면 그 뒤 서버 리다이렉트를 거쳐도 같은 사용자 요청 탭으로 보호한다. 명시적으로 클릭한 링크라면 목적지가 알려진 광고 도메인이어도 닫지 않는다.

## 요구 환경

- Windows 11
- Google Chrome 136 이상
- .NET 8 Runtime 또는 SDK

Chrome 136 이상에서는 원격 디버깅에 기본 Chrome 프로필을 쓸 수 없다. TabBouncer는 `%LOCALAPPDATA%\TabBouncer\ChromeProfile` 전용 프로필로 Chrome을 실행한다.

## 빌드와 실행

```powershell
dotnet build -c Release
dotnet run -c Release
```

단일 실행 파일을 만들려면 다음 명령을 사용한다.

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

생성 파일은 `bin\Release\net8.0\win-x64\publish\tabbouncer.exe`에 있다. 실행하면 디버깅 포트 9222와 전용 프로필을 사용하는 Chrome을 자동으로 연다.

TabBouncer는 콘솔 프로그램이다. 실행 중 콘솔에서 현재 판정 결과를 확인하고 단축키로 감시 상태를 바꿀 수 있다.

테스트나 별도 인스턴스에서 설정과 프로필 위치를 분리하려면 `--data-dir=C:\원하는\경로`를 지정한다.
Chrome에 추가 실행 인수가 필요하면 생성된 설정의 `chromeArguments` 배열에 넣는다.

## 설정

첫 실행 때 **실행 파일과 같은 폴더**에 `config.json`을 만든다. 실행 중 파일을 저장하면 변경 사항을 자동으로 다시 읽는다. 로그와 Chrome 전용 프로필은 `%LOCALAPPDATA%\TabBouncer`에 저장한다.

광고가 자주 생기는 사이트를 `watchedSites`에 추가하면 알려지지 않은 광고망도 더 적극적으로 차단한다.
항상 정상으로 취급할 사이트는 `allowedSites`에 등록한다. 등록한 도메인과 모든 하위 도메인의 탭·새 창은 광고 점수와 관계없이 닫지 않는다.

```json
{
  "dryRun": false,
  "strictMode": false,
  "protectExplicitClicks": true,
  "blockAutomaticCrossSitePopups": true,
  "closeThreshold": 80,
  "allowedSites": [
    "my-safe-site.example"
  ],
  "watchedSites": [
    "problem-site.example"
  ]
}
```

`dryRun`은 탭을 실제로 닫지 않고 판정 결과만 콘솔과 `events.jsonl`에 기록하는 관찰 모드다. 처음 관찰만 하려면 `dryRun`을 `true`로 바꾸거나 `--dry-run` 인수를 사용한다. `false`이면 종료 조건을 만족한 탭을 실제로 닫는다. `strictMode`는 정상 팝업 오탐 가능성을 높이므로 기본값을 유지하는 편이 좋다.

## 실행 중 키

- `d`: 실제 종료와 DRY-RUN 전환
- `p`: 감시 일시중지 또는 재개
- `u`: 마지막으로 닫은 탭 복구
- `l`: 최근 종료 목록
- `w`: 마지막으로 닫은 도메인을 `allowedSites`에 정상 사이트로 등록
- `r`: 설정 다시 읽기
- `q`: TabBouncer 종료

종료 횟수 제한은 없다. 짧은 시간에 광고 탭이나 창이 10개 이상 생성돼도 광고로 판정되는 항목은 모두 닫는다. 브라우저의 마지막 일반 탭은 닫지 않는다.

## 자체 테스트

```powershell
dotnet run -c Release -- --self-test
```

클릭 URL 일치, 별도 광고 탭, 버튼 팝업, 자동 외부 팝업, opener 없는 탭, 로그인 흐름, 등록한 정상 사이트 보호를 검사한다.

실제 Chrome을 headless 모드로 실행하는 브라우저 스모크 테스트는 빌드 후 다음과 같이 실행한다. Node.js 22 이상이 필요하다.

```powershell
node .\tests\browser-smoke.mjs
```

## 판별 한계

웹페이지의 임의 JavaScript 버튼이 여는 창은 사람이 기대한 결과인지 코드만으로 완벽하게 알 수 없다. TabBouncer는 실제 링크 목적지가 있으면 정확히 대조하고, 일반 버튼은 사용자 의도로 우선 보호한다. 특정 정상 서비스가 잘못 닫히면 실행 중 `w` 키를 누르거나 `allowedSites`에 도메인을 추가한다.
