# Snipjar を %LOCALAPPDATA% に入れ、ログオン時に常駐させ、
# タスクバーへピン留めするためのショートカットを作る。
# 何をするか表示してから確認する。-Yes で確認を省略。
[CmdletBinding()]
param(
    [switch]$Yes,
    [switch]$Uninstall
)
$ErrorActionPreference = 'Stop'

$target = Join-Path $env:LOCALAPPDATA 'snipjar'
$exe = Join-Path $target 'snipjar.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$shortcut = Join-Path $startMenu 'Snipjar.lnk'

function Confirm-Step([string[]]$lines) {
    Write-Host ''
    foreach ($line in $lines) { Write-Host "  $line" }
    Write-Host ''
    if ($Yes) { return }
    $answer = Read-Host '実行しますか? [y/N]'
    if ($answer -notmatch '^[yY]') { throw '中止しました' }
}

# 常駐を止める。--quit で行儀よく終わらせたいが、プロセスの実行ファイルが
# こちらから見えないことがある (別のコンテナで動いている場合など)。
# そこで失敗しても止まらず、最後は必ず Stop-Process で片付ける。
function Stop-Snipjar {
    foreach ($p in @(Get-Process snipjar -ErrorAction SilentlyContinue)) {
        try {
            if ($p.Path -and (Test-Path -LiteralPath $p.Path)) { & $p.Path '--quit' }
        } catch { }
    }
    Start-Sleep -Milliseconds 600
    foreach ($p in @(Get-Process snipjar -ErrorAction SilentlyContinue)) {
        try { Stop-Process -Id $p.Id -Force -ErrorAction Stop } catch { }
    }
}


if ($Uninstall) {
    Confirm-Step @(
        '次を削除します:',
        "  自動起動の登録  $runKey\snipjar",
        "  ショートカット  $shortcut",
        "  インストール先  $target",
        '設定 (%APPDATA%\snipjar\config.ini) は残します。'
    )
    Stop-Snipjar
    Remove-ItemProperty -Path $runKey -Name 'snipjar' -ErrorAction SilentlyContinue
    Remove-Item $shortcut -ErrorAction SilentlyContinue
    Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host 'アンインストールしました。'
    return
}

& (Join-Path $PSScriptRoot 'build.ps1')
$built = Join-Path $PSScriptRoot 'snipjar.exe'

Confirm-Step @(
    '次を行います:',
    "  1. $built を $exe に配置",
    "  2. スタートメニューにショートカットを作成  $shortcut",
    "  3. ログオン時の自動起動を登録  $runKey\snipjar = `"$exe`" --background",
    '書き込むのは上の 3 か所だけです (すべて現在のユーザー用)。'
)

Stop-Snipjar

New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item $built $exe -Force

$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $exe
$link.WorkingDirectory = $target
$link.IconLocation = "$exe,0"
$link.Description = '範囲を選んでアップロードし、URL をコピーする'
$link.Save()

New-ItemProperty -Path $runKey -Name 'snipjar' -PropertyType String `
    -Value ('"{0}" --background' -f $exe) -Force | Out-Null

$config = Join-Path $env:APPDATA 'snipjar\config.ini'
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
Write-Host '  1. スタートメニューの Snipjar を右クリック →「タスクバーにピン留めする」'
Write-Host '  2. ピン留めしたアイコンをクリックすると範囲選択が始まります'
Write-Host '  3. 離すと出るバーで「コピー」か「保存」を選びます (Esc で破棄)'
Write-Host ''
Write-Host '常駐を止めるとき:  snipjar.exe --quit'

Start-Process -FilePath $exe -ArgumentList '--background'
Write-Host '常駐を開始しました。'
