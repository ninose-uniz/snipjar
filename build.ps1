# snipjar.exe をビルドする。.NET SDK は不要で、Windows 同梱の csc.exe を使う。
$ErrorActionPreference = 'Stop'

$client = Join-Path $PSScriptRoot 'client'
$output = Join-Path $PSScriptRoot 'snipjar.exe'
$icon = Join-Path $client 'app.ico'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    throw "csc.exe が見つかりません: $csc (.NET Framework 4.x が必要)"
}

if (-not (Test-Path $icon)) {
    & (Join-Path $client 'make-icon.ps1')
}

$sources = Get-ChildItem -Path $client -Filter '*.cs' | ForEach-Object { $_.FullName }
if ($sources.Count -eq 0) { throw "ソースが見つかりません: $client" }

$arguments = @(
    '/nologo'
    '/target:winexe'
    '/platform:anycpu'
    '/optimize+'
    '/warn:4'
    '/warnaserror+'
    "/out:$output"
    "/win32icon:$icon"
    '/reference:System.dll'
    '/reference:System.Drawing.dll'
    '/reference:System.Windows.Forms.dll'
) + $sources

& $csc @arguments
if ($LASTEXITCODE -ne 0) { throw "ビルドに失敗しました" }

$size = [Math]::Round((Get-Item $output).Length / 1KB, 1)
Write-Host "ビルド完了: $output ($size KB)"
