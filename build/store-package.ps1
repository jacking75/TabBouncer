<# Builds an unsigned Microsoft Store MSIX from the verified public release ZIP. #>
param(
    [string]$ReleaseZip = 'artifacts/TabBouncer-v1.1.1-win-x64-selfcontained.zip'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$store = Join-Path $root 'packaging/store'
$output = Join-Path $root 'artifacts/store'
$work = Join-Path $output ('work-' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $work 'payload'
New-Item -ItemType Directory -Force -Path $payload | Out-Null

$zip = if ([IO.Path]::IsPathRooted($ReleaseZip)) { $ReleaseZip } else { Join-Path $root $ReleaseZip }
$expected = '88915d28a961f0220d1e62c86aa9ca484285f227513f2546a03930e65f7d34c3'
if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
    throw 'Release ZIP does not match the published v1.1.1 SHA-256.'
}
Expand-Archive -LiteralPath $zip -DestinationPath $payload
if (Test-Path -LiteralPath (Join-Path $payload 'config.json')) { throw 'User config must not be packaged.' }
Copy-Item -LiteralPath (Join-Path $store 'AppxManifest.xml') -Destination $payload
Add-Type -AssemblyName System.Drawing
$source = [Drawing.Image]::FromFile((Join-Path $root 'docs/images/tabbouncer-icon-source.png'))
try {
    $assets = Join-Path $payload 'Assets'
    New-Item -ItemType Directory -Path $assets | Out-Null
    foreach ($asset in @(
        @{Name='StoreLogo.png';Size=50},
        @{Name='Square44x44Logo.png';Size=44},
        @{Name='Square150x150Logo.png';Size=150},
        @{Name='StoreTile.png';Size=300}
    )) {
        $bitmap = New-Object Drawing.Bitmap($asset.Size, $asset.Size)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.DrawImage($source, 0, 0, $asset.Size, $asset.Size)
            $bitmap.Save((Join-Path $assets $asset.Name), [Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
    Copy-Item -LiteralPath (Join-Path $assets 'StoreTile.png') -Destination (Join-Path $output 'StoreTile.png')
}
finally { $source.Dispose() }

# Keep the actual existing screenshots unchanged; add only neutral surrounding space.
foreach ($number in 1..2) {
    $image = [Drawing.Image]::FromFile((Join-Path $root ('docs/images/{0:000}.png' -f $number)))
    $bitmap = New-Object Drawing.Bitmap(1366, 1024)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([Drawing.Color]::FromArgb(244, 246, 249))
        $graphics.DrawImageUnscaled($image, [int]((1366 - $image.Width) / 2), [int]((1024 - $image.Height) / 2))
        $bitmap.Save((Join-Path $output ('Screenshot-{0}.png' -f $number)), [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $graphics.Dispose(); $bitmap.Dispose(); $image.Dispose() }
}

$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
$makeappx = Get-ChildItem -LiteralPath $sdkRoot -Filter makeappx.exe -Recurse |
    Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName | Select-Object -Last 1
if (-not $makeappx) { throw 'Windows SDK MakeAppx.exe is required.' }
$package = Join-Path $output 'TabBouncer-1.1.1.0-x64.msix'
& $makeappx.FullName pack /d $payload /p $package /o
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx validation or packaging failed.' }
[IO.File]::WriteAllText((Join-Path $output 'package-root.txt'), $payload, (New-Object Text.UTF8Encoding($false)))
$hash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), "$hash  TabBouncer-1.1.1.0-x64.msix`n", (New-Object Text.UTF8Encoding($false)))
Write-Host "Package: $package"
Write-Host "Payload: $payload"
Write-Host "SHA256: $hash"
