# shotlink を %LOCALAPPDATA% に入れ、ログオン時に常駐させ、
# タスクバーへピン留めするためのショートカットを作る。
# 何をするか表示してから確認する。-Yes で確認を省略。
[CmdletBinding()]
param(
    [switch]$Yes,
    [switch]$Uninstall
)
$ErrorActionPreference = 'Stop'

$target = Join-Path $env:LOCALAPPDATA 'shotlink'
$exe = Join-Path $target 'shotlink.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$shortcut = Join-Path $startMenu 'shotlink.lnk'

function Confirm-Step([string[]]$lines) {
    Write-Host ''
    foreach ($line in $lines) { Write-Host "  $line" }
    Write-Host ''
    if ($Yes) { return }
    $answer = Read-Host '実行しますか? [y/N]'
    if ($answer -notmatch '^[yY]') { throw '中止しました' }
}

if ($Uninstall) {
    Confirm-Step @(
        '次を削除します:',
        "  自動起動の登録  $runKey\shotlink",
        "  ショートカット  $shortcut",
        "  インストール先  $target",
        '設定 (%APPDATA%\shotlink\config.ini) は残します。'
    )
    Get-Process shotlink -ErrorAction SilentlyContinue | ForEach-Object {
        & $_.Path '--quit' 2>$null
    }
    Start-Sleep -Milliseconds 500
    Get-Process shotlink -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-ItemProperty -Path $runKey -Name 'shotlink' -ErrorAction SilentlyContinue
    Remove-Item $shortcut -ErrorAction SilentlyContinue
    Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host 'アンインストールしました。'
    return
}

& (Join-Path $PSScriptRoot 'build.ps1')
$built = Join-Path $PSScriptRoot 'shotlink.exe'

Confirm-Step @(
    '次を行います:',
    "  1. $built を $exe に配置",
    "  2. スタートメニューにショートカットを作成  $shortcut",
    "  3. ログオン時の自動起動を登録  $runKey\shotlink = `"$exe`" --background",
    '書き込むのは上の 3 か所だけです (すべて現在のユーザー用)。'
)

Get-Process shotlink -ErrorAction SilentlyContinue | ForEach-Object {
    & $_.Path '--quit' 2>$null
}
Start-Sleep -Milliseconds 600
Get-Process shotlink -ErrorAction SilentlyContinue | Stop-Process -Force

New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item $built $exe -Force

$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $exe
$link.WorkingDirectory = $target
$link.IconLocation = "$exe,0"
$link.Description = '範囲を選んでアップロードし、URL をコピーする'
$link.Save()

New-ItemProperty -Path $runKey -Name 'shotlink' -PropertyType String `
    -Value ('"{0}" --background' -f $exe) -Force | Out-Null

$config = Join-Path $env:APPDATA 'shotlink\config.ini'
$gallery = ''
if (Test-Path $config) {
    $endpoint = (Select-String -Path $config -Pattern '^\s*endpoint\s*=\s*(.+)$').Matches.Groups[1].Value
    if ($endpoint) { $gallery = ([uri]$endpoint).GetLeftPart('Authority') + '/gallery' }
}

Write-Host ''
Write-Host "配置しました: $exe"
Write-Host "設定ファイル: $config"
if ($gallery) { Write-Host "一覧ページ  : $gallery" }
Write-Host ''
Write-Host '次にやること:'
Write-Host '  1. スタートメニューの shotlink を右クリック →「タスクバーにピン留めする」'
Write-Host '  2. ピン留めしたアイコンをクリックすると範囲選択が始まります'
Write-Host '  3. 離すと出るバーで「コピー」か「保存」を選びます (Esc で破棄)'
Write-Host ''
Write-Host '常駐を止めるとき:  shotlink.exe --quit'

Start-Process -FilePath $exe -ArgumentList '--background'
Write-Host '常駐を開始しました。'
