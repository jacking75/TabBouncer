# winget 등록 후속 작업

- [x] [winget-pkgs PR #439742](https://github.com/microsoft/winget-pkgs/pull/439742)의 검토 요청에 대응하고 병합을 확인한다.
- [x] 병합 후 공개 winget 원본에 `jacking75.TabBouncer` 버전 `1.1.0`이 게시됐는지 확인한다.
- [ ] 별도의 Windows 11 x64 환경에서 공개 winget 원본으로 설치하고 실행 및 제거를 확인한다.
- [x] 공개 게시를 확인한 뒤 `README.md`와 `README.en.md`에 winget 설치 명령을 추가한다.
- [ ] 다음 버전 배포 때 winget 업그레이드 후 사용자 설정이 유지되는지 확인하고, 문제가 있으면 갱신 전에 수정한다.
- [ ] 다음 버전의 릴리스 파일과 SHA256을 반영한 매니페스트를 검증하고, 해당 버전만 포함하는 winget-pkgs PR을 제출한다.

## 현재 상태

2026-09-25 기준 PR #439742는 병합됐고 게시 파이프라인이 성공했다. 공개 winget 원본의 `winget show --id jacking75.TabBouncer --exact --source winget`에서 버전 `1.1.0`이 조회된다. 별도 Windows 11 x64 환경에서 공개 설치·실행·제거 시험은 아직 남아 있다.

## 병합 후 확인 방법

```powershell
winget source update --name winget
winget show --id jacking75.TabBouncer --exact --source winget
```

`winget show`에서 `1.1.0`과 공개 원본 `winget`이 표시되면 게시를 확인한 것이다. 이미 로컬 매니페스트로 설치한 PC에서는 설치 시험이 기존 설치와 겹칠 수 있으므로 별도 환경에서 다음 명령을 실행한다.

```powershell
winget install --id jacking75.TabBouncer --exact --source winget
tabbouncer
winget uninstall --id jacking75.TabBouncer --exact
```

설치 시험에서는 `tabbouncer` 명령 별칭, 실행 파일 옆의 `languages.json`, 앱 실행, 제거 결과를 확인한다. 공개 설치 확인 후 README에 같은 설치 명령을 안내한다.

## 다음 버전 배포 시 확인 방법

GitHub 릴리스에 올린 실제 ZIP과 `SHA256SUMS.txt`를 기준으로 `packaging/update-manifests.ps1`을 실행한다. 이 스크립트는 winget과 Scoop 매니페스트를 함께 갱신한다. `ReleaseDate`에는 실제 릴리스 날짜를 준다. `winget validate --manifest packaging/winget`와 설치·업그레이드 시험을 마친 뒤 새 버전의 매니페스트만 별도 PR로 제출한다.

현재 앱은 실행 파일 폴더에 쓸 수 있으면 그곳에 `config.json`을 저장한다. winget 업그레이드가 설치 폴더를 교체할 때 설정이 유지되는지는 아직 검증되지 않았다. 다음 버전에서는 설정을 만든 뒤 업그레이드하고 값이 그대로 남는지 확인한다.
