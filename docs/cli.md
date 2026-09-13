# 명령줄 인수

`tabbouncer.exe`는 인수 없이 실행하는 것이 기본이다. 아래 인수는 바로가기, 자동 실행, 테스트 자동화에서 쓴다.

| 인수 | 뜻 |
|---|---|
| `--self-test` | 판정 점수 자체 검사를 콘솔에 출력하고 끝낸다. 창을 띄우지 않는다. |
| `--data-dir=경로` | 설정·로그·통계·안내 페이지·전용 프로필을 모두 이 폴더에 둔다. 폴더마다 독립된 인스턴스로 동작한다. |
| `--auto-start` | 실행하자마자 감시를 켠다. 자동화용이다. 평소에는 설정의 `startMonitoringOnLaunch`를 쓴다. |
| `--dry-run` | 관측 모드로 시작한다. |
| `--live` | 관측 모드를 끄고 시작한다. |
| `--strict` | `strictMode`를 켠다. |
| `--no-preempt` | `preemptiveBlock`을 끈다. |
| `--port=N` | `debugPort`를 N으로 바꾼다. |
| `--url=주소` | 전용 브라우저의 시작 주소를 바꾼다. 같은 데이터 폴더의 TabBouncer가 이미 실행 중이면 새로 켜지 않고 그 인스턴스의 전용 브라우저에 새 탭으로 연다. 이미 떠 있는 전용 브라우저에 연결할 때도 이 주소를 새 탭으로 연다. 주소에 `https://`가 없으면 붙인다. |
| `--minimized` | 창을 띄우지 않고 알림 영역 아이콘으로 시작한다. Windows 자동 실행이 이 인수를 쓴다. |

## 적용 순서

1. `--data-dir`을 가장 먼저 처리해 설정 파일 위치를 정한다.
2. 같은 데이터 폴더로 이미 실행 중인 TabBouncer가 있으면, 그 인스턴스의 창을 앞으로 가져오고 `--url` 주소를 넘긴 뒤 곧바로 끝낸다.
3. `config.json`을 읽는다.
4. 나머지 인수를 적용한다. 인수는 파일 값보다 우선한다.
5. 실행 중에 설정 파일을 다시 읽어도 `--port`, `--url`, `--strict`, `--no-preempt`는 유지한다. `--dry-run`, `--live`, `--auto-start`는 시작할 때 한 번만 적용한다.

## 사용 예

자주 가는 사이트를 전용 브라우저에서 바로 여는 바로가기 대상:

```text
"C:\Users\me\AppData\Local\Programs\TabBouncer\tabbouncer.exe" --url=https://free-webtoon.example/
```

설정 창의 **사이트 목록 → 즐겨찾기 사이트 → 바탕화면 바로가기 만들기**가 같은 바로가기를 만든다.

다른 설정으로 두 번째 인스턴스를 띄운다:

```powershell
.\tabbouncer.exe --data-dir="$env:LOCALAPPDATA\TabBouncer-test" --dry-run
```

브라우저 스모크 테스트가 쓰는 조합:

```powershell
dotnet .\bin\Release\tabbouncer.dll --data-dir=C:\Temp\smoke --port=25101 --auto-start
```
