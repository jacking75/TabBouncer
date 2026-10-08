# Microsoft Store 무료 배포

- [x] Partner Center에서 TabBouncer 이름을 예약하고 실제 패키지 식별자를 확인한다.
- [x] 공개 v1.1.1 배포본으로 Store 제출용 MSIX와 이미지를 만든다.
- [ ] 패키지 구조와 실행을 검증한다.
- [ ] 무료 가격, 속성, 연령 등급, 한국어·영어 목록, 개인정보 안내를 입력한다.
- [ ] 인증 제출 결과를 확인한다.
- [ ] Microsoft 인증과 공개 게시 결과를 확인한다.

## 목적과 배포 방식

기존 Windows 11 x64 앱을 MSIX로 포장해 무료로 배포한다. Microsoft Store가 인증 후 패키지를 서명하므로 별도 유료 코드 서명 인증서를 요구하지 않는다. WinGet과 ZIP 배포는 계속 별도로 유지한다.

Store ID는 `9PKGB7BHTBLB`, 패키지 이름은 `jacking75.TabBouncer`, 게시자는 `CN=E66A7C98-9426-41DB-9B55-AC993F05E1A3`, 게시자 표시 이름은 `jacking75`다. 이는 실제 Partner Center 제품 ID 화면에서 확인한 값이다. 계정 비밀번호나 제출 API 토큰은 저장하지 않는다.

## 패키지와 검증

`build/store-package.ps1`은 공개 릴리스 ZIP의 SHA-256을 확인한 뒤 기존 실행 파일과 화면 문구를 패키지에 넣는다. 앱 버전은 1.1.1, MSIX 버전은 1.1.1.0이다. 사용자 `config.json`과 개발용 서명 키는 포함하지 않는다. 일반 사용자 권한의 데스크톱 앱으로 실행하며 브라우저와 로컬 CDP 연결에 필요한 `runFullTrust`를 선언한다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build/store-package.ps1
```

결과는 `artifacts/store/`에 생긴다. 패키지 생성, 실제 패키지 실행, Store 업로드 검사, Microsoft 인증을 각각 별도 검증으로 취급한다. 업로드 성공만으로 Store 인증 또는 게시 완료를 주장하지 않는다.

목록 이미지는 기존 실제 앱 화면에 단색 여백을 추가해 Desktop 최소 크기를 충족한다. 화면의 내용·기록·버전 표시는 고치지 않는다. 기존 화면은 1.1.0에서 촬영했고 1.1.1의 같은 UI 기능을 설명하는 참고 자료다.

## 제출 자료

한국어·영어 목록은 `listing.json`, 개인정보 처리방침은 [개인정보 안내](../../docs/privacy.md)를 사용한다. 홈페이지와 지원은 공개 GitHub 저장소와 이슈 페이지로 연결한다. 계정 또는 앱 내 구매는 없으며, 사용자가 직접 선택한 전용 브라우저 창만 감시한다. 로그인·결제 같은 민감한 흐름을 보호하려고 시도하지만 모든 사이트에서 완벽한 판정을 보장하지 않는다.

설정, 이벤트 기록과 Chrome 프로필은 사용자 영역에 저장한다. 프로그램은 방문 URL과 클릭한 컨트롤의 라벨을 로컬에서 처리하므로 개인정보 접근 여부를 사실에 맞게 기재한다. 개발자 서버로 수집하는 원격 분석은 없다. 지원 요청에 로그를 올릴 때는 URL과 개인정보를 제거하도록 안내한다.

## 공식 기준

- [MSIX 제출과 서명](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements)
- [수동 데스크톱 앱 패키징](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-manual-conversion)
- [Store 스크린샷과 이미지](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images)

## 제출 상태

2026-10-09에 첫 제출 초안을 만들었다. 제출 ID는 `1152921505702076002`다. 인증 및 공개 게시 상태는 아직 확인하지 않았다.
