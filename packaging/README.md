# 패키지 관리자 배포

winget과 scoop으로 TabBouncer를 설치할 수 있게 하는 매니페스트다. 두 매니페스트 모두 GitHub 릴리스의 **런타임 포함** zip(`TabBouncer-vX.Y.Z-win-x64-selfcontained.zip`)을 쓴다. .NET 런타임 의존성을 따로 걸 필요가 없기 때문이다.

> 계획상 등록은 릴리스가 2~3번 안정적으로 나온 뒤에 한다. 등록 전까지 이 폴더의 해시 값은 `REPLACE_WITH_SHA256_FROM_RELEASE`로 비워 둔다.

```text
packaging/
  update-manifests.ps1                       버전·주소·해시를 릴리스 결과로 채운다
  scoop/tabbouncer.json                      scoop 매니페스트 (checkver·autoupdate 포함)
  winget/jacking75.TabBouncer.yaml           winget 버전 매니페스트
  winget/jacking75.TabBouncer.installer.yaml winget 설치 매니페스트 (zip 안의 portable exe)
  winget/jacking75.TabBouncer.locale.*.yaml  winget 설명 (en-US 기본, ko-KR)
```

## 1. 매니페스트 갱신

릴리스 워크플로가 GitHub 릴리스를 게시한 뒤, 게시된 `SHA256SUMS.txt`로 매니페스트를 채운다.

```powershell
./packaging/update-manifests.ps1 -Version 1.1.0 `
  -Sha256Sums https://github.com/jacking75/TabBouncer/releases/download/v1.1.0/SHA256SUMS.txt
```

로컬에서 `build/package.ps1`을 돌린 결과로 시험할 때는 `-Sha256Sums artifacts/SHA256SUMS.txt`를 준다. 이 해시는 릴리스 워크플로가 만든 파일과 다르므로 커밋하지 않는다.

## 2. winget

winget 매니페스트는 [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs)에 PR로 올린다.

1. 로컬에서 검사한다.

   ```powershell
   winget validate --manifest packaging/winget
   winget install --manifest packaging/winget
   ```

   `winget install --manifest`를 쓰려면 관리자 PowerShell에서 `winget settings --enable LocalManifestFiles`를 한 번 실행해야 한다.

2. 제출한다. [wingetcreate](https://github.com/microsoft/winget-create)를 쓰면 포크와 PR을 대신 만든다.

   ```powershell
   wingetcreate submit --token <GitHub 토큰> packaging/winget
   ```

   다음 버전부터는 `wingetcreate update jacking75.TabBouncer --version X.Y.Z --urls <zip 주소> --submit`으로 갱신할 수 있다.

설치 매니페스트는 `NestedInstallerType: portable`이라 사용자 폴더에 압축을 풀고 `tabbouncer` 명령 별칭을 만든다. 배포 zip에는 `config.json`이 없어 업그레이드해도 설정이 유지된다. 화면 문구 파일 `languages.json`은 zip에 들어 있어 업그레이드하면 새 버전 문구로 바뀐다.

## 3. scoop

scoop은 두 가지 방법이 있다.

- **주소로 바로 설치**: 버킷 없이 매니페스트 주소로 설치한다. 가장 간단하다.

  ```powershell
  scoop install https://raw.githubusercontent.com/jacking75/TabBouncer/main/packaging/scoop/tabbouncer.json
  ```

- **버킷**: `scoop-bucket` 같은 별도 저장소를 만들고 `bucket/tabbouncer.json`으로 복사한다. 사용자는 `scoop bucket add jacking75 https://github.com/jacking75/scoop-bucket` 뒤 `scoop install tabbouncer`를 쓴다. 매니페스트에 `checkver`와 `autoupdate`가 있어, 버킷 저장소에서 [Excavator](https://github.com/ScoopInstaller/GithubActions) 워크플로를 돌리면 새 릴리스를 자동으로 반영한다.

scoop은 앱 폴더를 버전마다 새로 만들기 때문에 `persist`로 `config.json`을 보존한다. 처음 설치하면 빈 `config.json`이 생기고, TabBouncer는 빈 설정 파일을 기본값으로 채운다. `languages.json`은 보존하지 않으므로 업데이트하면 새 버전 문구를 쓴다.

## 4. 코드 서명

서명하지 않은 실행 파일은 SmartScreen 경고를 띄우고, 패키지 관리자 검사에서도 백신 오탐이 날 수 있다. 인증서를 구하면 저장소 비밀값 두 개를 넣는다. 릴리스 워크플로가 `build/package.ps1`에서 자동으로 서명한다.

| 비밀값 | 내용 |
|---|---|
| `SIGNING_CERTIFICATE_BASE64` | 코드 서명 인증서 PFX 파일을 Base64로 인코딩한 값 |
| `SIGNING_CERTIFICATE_PASSWORD` | PFX 암호 |

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("codesign.pfx")) | Set-Clipboard
```

오픈소스 프로젝트는 [SignPath Foundation](https://signpath.org/)의 무료 서명을 신청할 수 있다. [Azure Trusted Signing](https://learn.microsoft.com/azure/trusted-signing/)을 쓰면 PFX 대신 전용 GitHub 액션으로 서명 단계를 바꾼다.
