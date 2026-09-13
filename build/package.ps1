<#
.SYNOPSIS
    TabBouncer 릴리스 파일을 만든다.

.DESCRIPTION
    두 가지 배포본을 만든다.
      TabBouncer-v<버전>-win-x64.zip               .NET 10 Desktop Runtime이 필요한 단일 실행 파일
      TabBouncer-v<버전>-win-x64-selfcontained.zip 런타임을 포함한 단일 실행 파일
    그리고 SHA256SUMS.txt와 CHANGELOG.md에서 잘라 낸 release-notes.md를 만든다.

    코드 서명 인증서(PFX)를 Base64로 넣은 환경 변수 SIGNING_CERTIFICATE_BASE64와
    암호 SIGNING_CERTIFICATE_PASSWORD가 있으면 실행 파일에 서명한다. 없으면 서명을 건너뛴다.

.EXAMPLE
    pwsh ./build/package.ps1
    pwsh ./build/package.ps1 -Version 1.1.0 -OutputDirectory artifacts
#>
param(
    [string]$Version = "",
    [string]$OutputDirectory = "artifacts"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/TabBouncer.csproj'

[xml]$projectXml = Get-Content $project
$projectVersion = @($projectXml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
if (-not $Version) { $Version = $projectVersion }
if ($Version -ne $projectVersion) {
    throw "요청한 버전($Version)과 TabBouncer.csproj의 <Version>($projectVersion)이 다르다."
}

$output = Join-Path $root $OutputDirectory
$work = Join-Path $output 'work'
if (Test-Path $output) { Remove-Item $output -Recurse -Force }
New-Item -ItemType Directory -Force -Path $work | Out-Null

$variants = @(
    @{ Name = "TabBouncer-v$Version-win-x64"; SelfContained = 'false'; Extra = @() },
    @{ Name = "TabBouncer-v$Version-win-x64-selfcontained"; SelfContained = 'true'; Extra = @('-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true') }
)

foreach ($variant in $variants) {
    $publish = Join-Path $work $variant.Name
    Write-Host "== dotnet publish $($variant.Name)"
    $arguments = @('publish', $project, '-c', 'Release', '-r', 'win-x64',
        "--self-contained", $variant.SelfContained,
        '-p:PublishSingleFile=true', '-p:DebugType=None', '-p:DebugSymbols=false',
        '-o', $publish) + $variant.Extra
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 실패: $($variant.Name)" }
    $variant.Publish = $publish
}

if ($env:SIGNING_CERTIFICATE_BASE64) {
    Write-Host "== 코드 서명"
    $certificate = Join-Path $work 'signing.pfx'
    [IO.File]::WriteAllBytes($certificate, [Convert]::FromBase64String($env:SIGNING_CERTIFICATE_BASE64))
    try {
        $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName | Select-Object -Last 1
        if (-not $signtool) { throw "signtool.exe를 찾지 못했다." }
        foreach ($variant in $variants) {
            $exe = Join-Path $variant.Publish 'tabbouncer.exe'
            & $signtool.FullName sign /f $certificate /p $env:SIGNING_CERTIFICATE_PASSWORD /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $exe
            if ($LASTEXITCODE -ne 0) { throw "서명 실패: $exe" }
        }
    }
    finally {
        Remove-Item $certificate -Force -ErrorAction SilentlyContinue
    }
}
else {
    Write-Host "== SIGNING_CERTIFICATE_BASE64가 없어 코드 서명을 건너뛴다."
}

foreach ($variant in $variants) {
    foreach ($document in 'README.md', 'README.en.md', 'LICENSE', 'CHANGELOG.md') {
        Copy-Item (Join-Path $root $document) $variant.Publish
    }
    # 기존 폴더에 덮어써도 사용자 설정이 유지되도록 config.json은 넣지 않는다.
    Remove-Item (Join-Path $variant.Publish 'config.json') -ErrorAction SilentlyContinue
    $zip = Join-Path $output "$($variant.Name).zip"
    Write-Host "== $zip"
    Compress-Archive -Path (Join-Path $variant.Publish '*') -DestinationPath $zip -CompressionLevel Optimal
}

$sums = Get-ChildItem $output -Filter '*.zip' | Sort-Object Name | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
[IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'), $sums)

# CHANGELOG.md에서 "## [버전]" 절만 잘라 릴리스 노트로 쓴다.
$changelog = Get-Content (Join-Path $root 'CHANGELOG.md') -Encoding UTF8
$start = -1
for ($i = 0; $i -lt $changelog.Count; $i++) {
    if ($changelog[$i] -match "^## \[$([regex]::Escape($Version))\]") { $start = $i + 1; break }
}
if ($start -lt 0) { throw "CHANGELOG.md에 [$Version] 절이 없다." }
$end = $changelog.Count
for ($i = $start; $i -lt $changelog.Count; $i++) {
    if ($changelog[$i] -match '^## \[') { $end = $i; break }
}
$notes = ($changelog[$start..($end - 1)] -join "`n").Trim()
[IO.File]::WriteAllText((Join-Path $output 'release-notes.md'), $notes + "`n", (New-Object Text.UTF8Encoding($false)))

Remove-Item $work -Recurse -Force
Write-Host ""
Get-ChildItem $output | ForEach-Object { "{0,10:N0} KB  {1}" -f ($_.Length / 1KB), $_.Name }
