# Snipjar のセットアップ。これ 1 つ実行すれば使える状態になる。
#
#   1. 自分の Cloudflare アカウントに R2 バケットと Worker を作る
#   2. アップロード用トークンを発行して Worker のシークレットに登録する
#   3. 設定ファイルを書き、常駐アプリを配置して起動する
#
# 画像は「あなたの」Cloudflare に入る。作者のサーバーは一切経由しない。
[CmdletBinding()]
param(
    [switch]$Yes,        # 確認を省略する
    [switch]$ClientOnly, # Cloudflare 側は触らず、アプリの導入だけ行う (2 台目以降)
    [switch]$NewToken    # 既存のトークンを捨てて新しく発行し直す
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# Worker と R2 バケットの名前。各自のアカウントに立つので衝突しない
$Name = 'snipjar'

function Step($text) { Write-Host ''; Write-Host "== $text" -ForegroundColor Cyan }
function Fail($text) { Write-Host ''; Write-Host $text -ForegroundColor Red; exit 1 }

# wrangler は進捗もエラーも stderr に書く。PowerShell 5.1 はネイティブコマンドの
# stderr を異常終了として扱うので、素直にパイプで受けると「バケットは既にある」の
# ような想定内の応答でも落ちる。別プロセスにしてファイルで受ける。
function Invoke-Wrangler {
    param([string[]]$Arguments, [switch]$PassThruExit)
    $outFile = [System.IO.Path]::GetTempFileName()
    $errFile = [System.IO.Path]::GetTempFileName()
    try {
        # npx は .cmd なので直接は起動できない。cmd.exe 経由で呼ぶ
        $quoted = (@('--yes', 'wrangler@latest') + $Arguments) | ForEach-Object {
            if ($_ -match '[\s&|<>^]') { '"' + $_ + '"' } else { $_ }
        }
        $p = Start-Process -FilePath "$env:ComSpec" -Wait -NoNewWindow -PassThru `
            -RedirectStandardOutput $outFile -RedirectStandardError $errFile `
            -ArgumentList (@('/c', 'npx') + $quoted)
        $text = (Get-Content $outFile -Raw) + (Get-Content $errFile -Raw)
        if ($PassThruExit) { return [pscustomobject]@{ Text = $text; ExitCode = $p.ExitCode } }
        return $text
    } finally {
        Remove-Item $outFile, $errFile -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ''
Write-Host 'Snipjar のセットアップ' -ForegroundColor Cyan
Write-Host '画像の保存先は、あなた自身の Cloudflare アカウントです。'

$configDir = Join-Path $env:APPDATA 'snipjar'
$config = Join-Path $configDir 'config.ini'

function Read-Config([string]$key) {
    if (-not (Test-Path $config)) { return $null }
    $line = Get-Content $config | Where-Object { $_ -match "^\s*$key\s*=" } | Select-Object -First 1
    if (-not $line) { return $null }
    return ($line -split '=', 2)[1].Trim()
}

# --- 2 台目以降: Cloudflare には触らず、アプリだけ入れる ---------------------
if ($ClientOnly) {
    Step 'アプリだけ入れます (Cloudflare には触りません)'
    $endpoint = Read-Config 'endpoint'
    $token = Read-Config 'token'

    if (-not $endpoint -or -not $token) {
        Write-Host '  1 台目の %APPDATA%\snipjar\config.ini に書いてある値が要ります。'
        if (-not $endpoint) { $endpoint = Read-Host '  endpoint (https://snipjar.xxx.workers.dev/upload)' }
        if (-not $token) {
            $secure = Read-Host '  token' -AsSecureString
            $token = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
                [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
        }
    }
    if (-not $endpoint -or -not $token) { Fail 'endpoint と token の両方が要ります。' }

    New-Item -ItemType Directory -Force -Path $configDir | Out-Null
    Set-Content -Path $config -Encoding utf8 -Value @"
# Snipjar
endpoint=$endpoint
token=$token
upload=always
"@
    Write-Host "  $config"
    $installArgs = @{}
    if ($Yes) { $installArgs['Yes'] = $true }
    & (Join-Path $PSScriptRoot 'install.ps1') @installArgs
    $base = ([uri]$endpoint).GetLeftPart('Authority')
    Write-Host ''
    Write-Host '準備できました。' -ForegroundColor Green
    Write-Host "  一覧ページ : $base/gallery"
    Write-Host '  使い方     : スタートメニューの Snipjar を右クリック →「タスクバーにピン留めする」'
    return
}

# --- 1. 前提の確認 -----------------------------------------------------------
Step '必要なものを確認します'

if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    Fail @'
Node.js が見つかりません。https://nodejs.org/ から入れてから、もう一度実行してください。
(Cloudflare へのデプロイに使う wrangler が Node.js で動きます)
'@
}
Write-Host "  Node.js $(node --version)"

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    Fail '.NET Framework 4.x が見つかりません。Windows 10/11 なら標準で入っています。'
}
Write-Host '  .NET Framework 4.x  (アプリのビルドに使います)'

# --- 2. Cloudflare にログイン ------------------------------------------------
Step 'Cloudflare にログインします'
$who = Invoke-Wrangler @('whoami')
if ($who -match 'not authenticated|You are not logged in') {
    Write-Host '  ブラウザが開きます。Cloudflare にログインして許可してください。'
    Invoke-Wrangler @('login') | Out-Null
    $who = Invoke-Wrangler @('whoami')
}
$account = ([regex]::Match($who, '([\w .\-]+)\s*\|\s*[0-9a-f]{32}')).Groups[1].Value.Trim()
if ($account) { Write-Host "  アカウント: $account" } else { Write-Host '  ログイン済み' }

if (-not $Yes) {
    Write-Host ''
    Write-Host '  これから、このアカウントに次を作ります:'
    Write-Host "    R2 バケット  $Name      (スクショの保存先)"
    Write-Host "    Worker       $Name      (アップロード受け口と一覧ページ)"
    Write-Host '  R2 は無料枠 10GB。事前に Cloudflare ダッシュボードで R2 の有効化が必要です。'
    $answer = Read-Host '  進めますか? [y/N]'
    if ($answer -notmatch '^[yY]') { Fail '中止しました。' }
}

# --- 3. R2 バケット ----------------------------------------------------------
Step "R2 バケット $Name を用意します"
$created = Invoke-Wrangler @('r2', 'bucket', 'create', $Name)
if ($created -match 'already (exists|owned)') {
    Write-Host '  すでにあるので、そのまま使います。'
} elseif ($created -match 'Created bucket') {
    Write-Host '  作成しました。'
} else {
    Write-Host $created
    Fail @'
R2 バケットを作れませんでした。Cloudflare ダッシュボードの R2 ページで
「Purchase R2 / Enable R2」を一度押して有効化してから、もう一度実行してください。
(無料枠 10GB。有効化にはカード登録が必要です)
'@
}

# --- 4. デプロイ -------------------------------------------------------------
# シークレットは Worker が存在しないと置けないので、先にデプロイする
Step 'Worker をデプロイします'
$deploy = Invoke-Wrangler @('deploy')
$url = ([regex]::Match($deploy, 'https://[a-z0-9.\-]+\.workers\.dev')).Value
if (-not $url) {
    Write-Host $deploy
    Fail 'デプロイ先の URL を読み取れませんでした。上の出力を確認してください。'
}
Write-Host "  $url"

# --- 5. トークン -------------------------------------------------------------
$existing = Read-Config 'token'
if ($existing -and -not $NewToken) {
    Step 'すでにあるトークンをそのまま使います'
    Write-Host '  作り直すと、他の PC の設定とブラウザのログインが無効になるためです。'
    Write-Host '  作り直したいときは -NewToken を付けて実行してください。'
    $token = $existing
} else {

Step 'アップロード用トークンを発行します'
$bytes = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$token = [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')

# secret put はパイプ入力を素直に受け取ってくれないことがあるので bulk を使う。
# 一時ファイルは BOM も改行も付けずに書き、直後に消す。
$secretFile = Join-Path ([System.IO.Path]::GetTempPath()) ('snipjar-' + [guid]::NewGuid().ToString('N') + '.json')
try {
    [System.IO.File]::WriteAllText($secretFile, (@{ UPLOAD_TOKEN = $token } | ConvertTo-Json -Compress))
    $result = Invoke-Wrangler @('secret', 'bulk', $secretFile)
    if ($result -notmatch 'Success|uploaded') { Write-Host $result; Fail 'シークレットを登録できませんでした。' }
} finally {
    Remove-Item $secretFile -Force -ErrorAction SilentlyContinue
}
Write-Host '  Worker のシークレットに登録しました（画面には出しません）。'
}

# --- 6. 設定ファイル ---------------------------------------------------------
Step '設定ファイルを書きます'
New-Item -ItemType Directory -Force -Path $configDir | Out-Null
Set-Content -Path $config -Encoding utf8 -Value @"
# Snipjar
# endpoint   : Worker のアップロード先
# token      : Worker の UPLOAD_TOKEN と同じ値
# upload     : always = 一覧に残すため裏で R2 にも上げる / never = 一切上げない
# savedir    : 「保存」の保存先 (省略時は Pictures\snipjar)
# updatecheck: off にすると GitHub への更新確認をしない
endpoint=$url/upload
token=$token
upload=always
"@
Write-Host "  $config"

# --- 7. クライアント ---------------------------------------------------------
Step 'アプリを配置します'
$installArgs = @{}
if ($Yes) { $installArgs['Yes'] = $true }
& (Join-Path $PSScriptRoot 'install.ps1') @installArgs

Write-Host ''
Write-Host '準備できました。' -ForegroundColor Green
Write-Host ''
Write-Host "  一覧ページ : $url/gallery"
Write-Host '               初回だけ config.ini の token を貼ってください。'
Write-Host ''
Write-Host '  使い方     : スタートメニューの Snipjar を右クリック →「タスクバーにピン留めする」'
Write-Host '               アイコンをクリック → 範囲をドラッグ →「コピー」か「保存」'
Write-Host ''
