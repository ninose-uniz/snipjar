# 配布用の zip と SHA256SUMS.txt を作る。
# zip を展開したフォルダだけで setup.ps1 が完結するように、必要なものを全部入れる。
[CmdletBinding()]
param([string]$Version)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

& (Join-Path $PSScriptRoot 'build.ps1')

$exe = Join-Path $PSScriptRoot 'snipjar.exe'
if (-not $Version) {
    $Version = (Get-Item $exe).VersionInfo.FileVersion -replace '\.0$', ''
}
$tag = "v$Version"

$dist = Join-Path $PSScriptRoot 'dist'
$stage = Join-Path $dist "snipjar-$tag"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

# zip に入れるもの。client/ と worker/ はソースなので、受け取った側で作り直せる
$items = @(
    'snipjar.exe', 'install.bat', 'setup.ps1', 'install.ps1', 'build.ps1', 'release.ps1',
    'wrangler.toml', 'README.md', 'README.ja.md', 'LICENSE'
)
foreach ($item in $items) {
    Copy-Item (Join-Path $PSScriptRoot $item) -Destination $stage -Force
}
foreach ($dir in 'client', 'worker', 'docs') {
    Copy-Item (Join-Path $PSScriptRoot $dir) -Destination $stage -Recurse -Force
}
# ビルドで生成されるものは配らない（受け取った側で作られる）
Remove-Item (Join-Path $stage 'client\app.ico') -Force -ErrorAction SilentlyContinue

$zip = Join-Path $dist "snipjar-$tag.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal

$sums = Join-Path $dist 'SHA256SUMS.txt'
$lines = foreach ($file in @($zip, $exe)) {
    $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLower()
    "$hash  $(Split-Path $file -Leaf)"
}
Set-Content -Path $sums -Value $lines -Encoding ascii

Write-Host ''
Write-Host "できました: $zip  ($([Math]::Round((Get-Item $zip).Length / 1KB)) KB)"
Get-Content $sums | ForEach-Object { Write-Host "  $_" }
Write-Host ''
Write-Host '公開するには:'
Write-Host "  gh release create $tag `"$zip`" `"$sums`" --title `"Snipjar $tag`" --notes-file <リリースノート>"
