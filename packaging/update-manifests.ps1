<#
.SYNOPSIS
    winget·scoop 매니페스트의 버전, 다운로드 주소, SHA256을 릴리스 결과에 맞춘다.

.DESCRIPTION
    build/package.ps1이 만든 SHA256SUMS.txt(또는 GitHub 릴리스에서 받은 같은 파일)를 읽어
    packaging/winget/*.yaml과 packaging/scoop/tabbouncer.json을 고친다.
    두 매니페스트 모두 런타임 포함(selfcontained) zip을 쓴다.

.EXAMPLE
    ./packaging/update-manifests.ps1 -Version 1.1.0 -Sha256Sums artifacts/SHA256SUMS.txt
    ./packaging/update-manifests.ps1 -Version 1.1.0 -Sha256Sums https://github.com/jacking75/TabBouncer/releases/download/v1.1.0/SHA256SUMS.txt
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Sha256Sums,
    [string]$ReleaseDate = (Get-Date -Format 'yyyy-MM-dd')
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$zipName = "TabBouncer-v$Version-win-x64-selfcontained.zip"
$url = "https://github.com/jacking75/TabBouncer/releases/download/v$Version/$zipName"

if ($Sha256Sums -match '^https?://') {
    $sums = (Invoke-WebRequest -Uri $Sha256Sums -UseBasicParsing).Content -split "`r?`n"
}
else {
    $sums = Get-Content $Sha256Sums
}

$line = $sums | Where-Object { $_ -match "^\s*([0-9a-fA-F]{64})\s+\*?$([regex]::Escape($zipName))\s*$" } | Select-Object -First 1
if (-not $line) { throw "SHA256SUMS에서 $zipName 항목을 찾지 못했다." }
$hash = ([regex]::Match($line, '[0-9a-fA-F]{64}')).Value.ToLowerInvariant()

$utf8 = New-Object System.Text.UTF8Encoding($false)

foreach ($file in Get-ChildItem (Join-Path $here 'winget') -Filter '*.yaml') {
    $text = [IO.File]::ReadAllText($file.FullName)
    $text = [regex]::Replace($text, '(?m)^PackageVersion: .*$', "PackageVersion: $Version")
    $text = [regex]::Replace($text, '(?m)^ReleaseDate: .*$', "ReleaseDate: $ReleaseDate")
    $text = [regex]::Replace($text, '(?m)^(\s*InstallerUrl: ).*$', "`${1}$url")
    $text = [regex]::Replace($text, '(?m)^(\s*InstallerSha256: ).*$', "`${1}$($hash.ToUpperInvariant())")
    [IO.File]::WriteAllText($file.FullName, $text, $utf8)
    Write-Host "updated $($file.Name)"
}

$scoopPath = Join-Path $here 'scoop/tabbouncer.json'
$scoop = [IO.File]::ReadAllText($scoopPath)
$scoop = [regex]::Replace($scoop, '(?m)^(    "version": ")[^"]*(")', "`${1}$Version`${2}")
$scoop = [regex]::Replace($scoop, '(?m)^(            "url": ")https://github\.com/jacking75/TabBouncer/releases/download/v[^/]+/[^"]*(")', "`${1}$url`${2}")
$scoop = [regex]::Replace($scoop, '(?m)^(            "hash": ")[^"]*(")', "`${1}$hash`${2}")
[IO.File]::WriteAllText($scoopPath, $scoop, $utf8)
Write-Host "updated scoop/tabbouncer.json"
Write-Host "version=$Version sha256=$hash"
