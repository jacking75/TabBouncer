# 로그 읽는 법

TabBouncer는 콘솔 창이 없는 프로그램이라, 동작 기록을 파일로 남긴다. 파일은 데이터 폴더(기본 `%LOCALAPPDATA%\TabBouncer`, `--data-dir`을 쓰면 그 폴더)에 있다. TabBouncer 창의 **로그 폴더 열기**로 바로 연다.

> 로그에는 방문한 주소가 들어 있다. 이슈에 붙이거나 다른 사람에게 보내기 전에 공개하고 싶지 않은 주소를 지운다.

## 데이터 폴더의 파일

| 파일 | 내용 |
|---|---|
| `tabbouncer.log` | 사람이 읽는 활동 로그. TabBouncer 창의 활동 영역과 같은 내용이다. |
| `events.jsonl` | 판정 이벤트. 한 줄에 JSON 하나다. 관측 모드 결과를 분석할 때 쓴다. |
| `stats.json` | 누적 차단 통계. 창의 "누적 N개"가 이 값이다. |
| `ui-state.json` | 창 위치·크기, "유지·관측 항목도 표시" 선택, 트레이 안내를 이미 보여 줬는지. |
| `start.html` | 전용 Chrome의 안내 페이지. 실행할 때마다 다시 만든다. |
| `ChromeProfile\` | 전용 Chrome 프로필. |
| `config.json` | 실행 파일 폴더에 쓸 수 없거나 `--data-dir`을 쓸 때만 여기에 생긴다. |

## 로그 회전

`tabbouncer.log`와 `events.jsonl`은 각각 5MB를 넘으면 `.1`로 이름을 바꾸고 새 파일을 시작한다. 이전 `.1`은 `.2`가 되고, 그보다 오래된 파일은 지운다. 파일마다 최대 약 15MB를 차지한다. TabBouncer는 시작할 때 `.2`, `.1`, 현재 파일 순서로 최근 기록을 읽어 목록을 복원한다.

## tabbouncer.log

```text
2026-09-14 00:09:16 [success] 자동 광고 탭 종료 110점 [passive-click-popup+cross-site+blank-redirect+fast-open] http://localhost:25080/?page=burst-11
```

`날짜 시간 [수준] 내용` 형식이다. 수준은 네 가지다.

| 수준 | 뜻 |
|---|---|
| `info` | 연결, 설정 다시 읽기, 유지 판정 같은 일반 기록 |
| `success` | 탭을 닫았거나 이동을 되돌렸거나 등록에 성공함 |
| `warning` | 관측 모드의 "닫을 대상", 연결 끊김, 초기화 실패 |
| `error` | 설정을 읽지 못함, 탭 종료 실패 같은 오류 |

시작할 때 버전, Windows와 .NET 버전, 설정 파일 경로, 데이터 폴더를 먼저 적는다. Chrome에 연결하면 브라우저 버전을 적는다.

활동 로그 문구는 화면 언어를 따른다. 위 예시는 한국어 화면의 로그다. 이슈에 붙일 로그는 어느 언어여도 된다.

## events.jsonl

각 줄은 다음 형식이다.

```json
{"ts":"2026-09-14T00:09:16.1234567+09:00","data":{"stage":"closed","score":110,"reason":"passive-click-popup+cross-site+blank-redirect+fast-open","url":"http://localhost:25080/?page=burst-11","openerUrl":"http://127.0.0.1:25080/","breakdown":[{"code":"passive-click-popup","points":60},{"code":"cross-site","points":25},{"code":"blank-redirect","points":15},{"code":"fast-open","points":10}]}}
```

`ts`는 기록 시각이고, `data.stage`가 이벤트 종류다.

| `stage` | 뜻 | 주요 필드 |
|---|---|---|
| `user-approved` | 클릭한 목적지와 같아 사용자 요청 탭으로 유지 | `url`, `openerUrl`, `reason`(클릭한 요소 라벨) |
| `preempt`, `documentRequest`, `targetInfoChanged`, `frameNavigated`, `debounce` | 새 탭을 판정한 단계. 점수가 기준 -30 이상일 때만 남긴다. | `score`, `reason`, `url`, `openerUrl`, `initialUrl`, `popupLikely`, `redirects`, `intent`, `ageMs`, `breakdown` |
| `kept` | 최종 판정에서 기준에 조금 못 미쳐 유지 | `score`, `reason`, `url`, `openerUrl`, `breakdown` |
| `dry-run` | 관측 모드라 닫지 않은 새 탭 | `score`, `reason`, `url`, `openerUrl`, `breakdown` |
| `closed` | 새 탭을 닫음 | `score`, `reason`, `url`, `openerUrl`, `breakdown` |
| `redirect-blocked` | 현재 탭의 이동을 원래 페이지로 되돌림 | `score`, `reason`, `url`(가려던 주소), `restoredUrl`(돌아간 주소), `breakdown` |
| `redirect-dry-run` | 관측 모드라 되돌리지 않은 이동 | `redirect-blocked`와 같음 |

- TabBouncer 창의 최근 목록은 같은 사이트의 반복 항목을 한 행으로 묶지만, `events.jsonl`에는 막을 때마다 한 줄씩 남는다.
- `reason`은 사유 코드를 `+`로 이은 문자열이다. 코드의 뜻은 [판정 방식 상세](how-it-works.md)의 점수표에 있다.
- `breakdown`은 항목별 점수다. 1.1.0 이전 기록에는 없다.
- `intent`는 판정에 쓴 클릭 의도 결과다. `clicked:라벨`이면 사용자가 그 라벨의 링크를 누른 것이다.

## 관측 모드 결과 보는 순서

1. TabBouncer 창에서 **유지·관측 항목도 표시**를 켜고 "관측(닫을 대상)" 항목을 훑는다.
2. 정상 사이트가 섞여 있으면 항목을 선택하고 **선택한 사이트 등록**을 누른다.
3. 더 오래된 기록은 `events.jsonl`에서 `dry-run`과 `redirect-dry-run`을 찾는다.
4. 오탐이 없으면 관측 모드를 끈다.

## PowerShell로 요약하기

닫은 탭을 도메인별로 센다.

```powershell
Get-Content "$env:LOCALAPPDATA\TabBouncer\events.jsonl" | ConvertFrom-Json |
  Where-Object { $_.data.stage -in 'closed', 'redirect-blocked' } |
  Group-Object { ([uri]$_.data.url).Host } |
  Sort-Object Count -Descending |
  Select-Object -First 20 Count, Name
```

관측 모드에서 닫았을 탭만 본다.

```powershell
Get-Content "$env:LOCALAPPDATA\TabBouncer\events.jsonl" | ConvertFrom-Json |
  Where-Object { $_.data.stage -in 'dry-run', 'redirect-dry-run' } |
  Select-Object ts, @{ n = 'score'; e = { $_.data.score } }, @{ n = 'url'; e = { $_.data.url } }, @{ n = 'reason'; e = { $_.data.reason } }
```
