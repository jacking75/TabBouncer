# Microsoft Store 무료 배포

- [x] Partner Center에서 TabBouncer 이름을 예약하고 실제 패키지 식별자를 확인한다.
- [x] 공개 v1.1.1 배포본으로 Store 제출용 MSIX와 이미지를 만든다.
- [x] MakeAppx 패키지 구조 검사와 배포 실행 파일의 자체 테스트를 검증한다.
- [ ] 설치된 MSIX의 실행과 종료를 검증한다.
- [x] 무료 가격, 공개 배포, 앱 속성과 개인정보 안내를 저장한다.
- [x] 한국어·영어 소개 문구와 심사 담당자용 실행 안내를 저장한다.
- [x] 패키지를 업로드하고 Partner Center의 Validated 결과를 확인한다.
- [x] 목록 이미지를 업로드하고 검증 결과를 확인한다.
- [x] IARC 약관 동의 후 연령 등급을 저장한다.
- [x] 인증 제출 결과를 확인한다.
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

현재 MakeAppx의 기본 의미 검사가 통과했고, 패키지에 포함한 실행 파일의 자체 테스트는 18/18 통과했다. MSIX의 SHA-256은 `966c84ea30fa3756fdddf4b18f81bd5f7ee38d9f27f90a523b9704000d2aad47`이다. 이는 설치된 MSIX 실행 검증과 구분한다. 로컬 App Certification Kit 실행은 Windows에서 취소되어 검사 결과가 생성되지 않았다.

목록 이미지는 기존 실제 앱 화면에 단색 여백을 추가해 Desktop 최소 크기를 충족한다. 화면의 내용·기록·버전 표시는 고치지 않는다. 기존 화면은 1.1.0에서 촬영했고 1.1.1의 같은 UI 기능을 설명하는 참고 자료다.

## 제출 자료

한국어·영어 목록은 `listing.json`, 개인정보 처리방침은 [개인정보 안내](../../docs/privacy.md)를 사용한다. 홈페이지와 지원은 공개 GitHub 저장소와 이슈 페이지로 연결한다. 계정 또는 앱 내 구매는 없으며, 사용자가 직접 선택한 전용 브라우저 창만 감시한다. 로그인·결제 같은 민감한 흐름을 보호하려고 시도하지만 모든 사이트에서 완벽한 판정을 보장하지 않는다.

설정, 이벤트 기록과 Chrome 프로필은 사용자 영역에 저장한다. 프로그램은 방문 URL과 클릭한 컨트롤의 라벨을 로컬에서 처리하므로 개인정보 접근 여부를 사실에 맞게 기재한다. 개발자 서버로 수집하는 원격 분석은 없다. 지원 요청에 로그를 올릴 때는 URL과 개인정보를 제거하도록 안내한다.

## 공식 기준

- [MSIX 제출과 서명](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements)
- [수동 데스크톱 앱 패키징](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-manual-conversion)
- [Store 스크린샷과 이미지](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images)

## 제출 상태

2026-10-09에 첫 제출 초안을 만들었다. 제출 ID는 `1152921505702076002`다. 무료 가격과 공개 배포 설정을 저장했으며, 유틸리티 + 도구 범주 및 개인정보처리방침·홈페이지·지원 URL은 속성 완료 상태로 저장했다. 연령 등급 설문은 도구 앱의 실제 기능에 맞게 작성했고 Microsoft Store 3세 이상이다. 사용자가 IARC 동의와 저장을 직접 완료했으며, 동의 확인란 대신 확정된 등급과 계속 링크가 나타난 것을 확인했다. 연령 등급 저장 확인 당시 등급 ID는 보류 중이었다.

Edge 연결 후 동일한 초안에서 240개 시장의 USD 기준 가격 0, 공개 검색 가능, 가능한 한 빨리 릴리스 설정을 확인했다. 한국어(한국)·영어(미국) 설명, 기능 6개, 검색 키워드 4개, 짧은 설명, 개발자와 저작권 정보를 저장했다. 심사 담당자용 실행 절차와 `runFullTrust` 필요 사유를 추가 테스트 정보에 저장했고, 인증 통과 후 즉시 게시 옵션을 저장했다.

Chrome과 Edge의 자동 파일 업로드는 확장 프로그램의 파일 URL 접근 권한 오류로 실패했다. Edge를 재시작하고 사용자가 직접 인증과 패키지 업로드를 완료한 뒤, 제출 개요에서 `TabBouncer-1.1.1.0-x64.msix`의 `Validated` 및 패키지 `완료` 상태를 확인했다. 이전의 `null Bytes` 실패 항목은 정리했다. 최종 제출 시 서버가 패키지를 유효한 것으로 인정하고 제출을 접수했다.

패키지 분석 후 나타난 제한된 기능 입력란에 500자 이내의 `runFullTrust` 사용 사유를 저장했고, 제출 개요에서 제출 옵션 `완료`를 확인했다. 사용자가 한국어·영어 목록의 스크린샷 업로드와 저장을 직접 완료한 뒤, Store 목록 `완료` 및 인증 제출 버튼 활성화를 확인했다.

2026-10-09 01:27 KST에 인증 제출 접수를 확인했다. 제품 상태는 `인증 중`, 진행 상태는 `사전 처리 중`(2/4 단계)이며 제출 단계는 완료다. 인증을 통과하자마자 자동 게시하도록 설정되어 있다. 아직 Microsoft 인증 통과나 공개 게시 완료는 아니다. 증빙 화면은 로컬 `artifacts/store/submitted-certification.jpg`에 저장했다. `artifacts/`는 Git 추적에서 제외되므로 증빙 이미지는 저장소에 포함하지 않는다. 진행 상태는 [Partner Center](https://partner.microsoft.com/ko-kr/dashboard/products/9PKGB7BHTBLB/overview)에서 확인한다.
