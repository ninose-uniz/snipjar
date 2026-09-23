# icon.svg から app.ico を作り直す保守用スクリプト。
#
# ビルドには不要 — app.ico はリポジトリに入れてあるので、`build.ps1` はそれを使う。
# デザイン（icon.svg）を変えたときだけ、これを実行して app.ico を作り直す。
# ラスタライズに Chrome を使う（SVG を原本のまま、きれいに縮小するため）。
#
# 小さいサイズは DIB で書く。PNG 圧縮エントリは Windows は読めるが
# System.Drawing.Icon が読めず、後から扱いづらいため 256px だけに留める。
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$svg = Join-Path $PSScriptRoot 'icon.svg'
$out = Join-Path $PSScriptRoot 'app.ico'
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)

$chrome = @(
    "$env:ProgramFiles\Google\Chrome\Application\chrome.exe"
    "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe"
    "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe"
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $chrome) {
    throw @'
Chrome か Edge が必要です (SVG のラスタライズに使います)。
app.ico はリポジトリに入っているので、アイコンを作り直さないなら実行不要です。
'@
}

$temp = Join-Path ([System.IO.Path]::GetTempPath()) ('snipjar-icon-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $temp | Out-Null

try {
    # Chrome にはウィンドウの最小幅があり、16px などを直接指定すると
    # SVG の左上だけを切り取った空の画像になる。大きく 1 枚描いて縮小する。
    $master = 512
    $scaled = Join-Path $temp 'icon-master.svg'
    (Get-Content $svg -Raw) -replace 'width="256" height="256"', "width=`"$master`" height=`"$master`"" |
        Set-Content -Path $scaled -Encoding utf8

    $masterPng = Join-Path $temp 'master.png'
    # Chrome は進捗を stderr に書くので、Start-Process で受け止める
    # (PowerShell は native の stderr を例外として扱ってしまうため)
    Start-Process -FilePath $chrome -Wait -NoNewWindow `
        -RedirectStandardError (Join-Path $temp 'chrome.log') `
        -ArgumentList @(
            '--headless=new', '--disable-gpu', '--hide-scrollbars',
            '--default-background-color=00000000',
            '--force-device-scale-factor=1',
            "--screenshot=$masterPng", "--window-size=$master,$master",
            ("file:///" + ($scaled -replace '\\', '/'))
        )
    if (-not (Test-Path $masterPng)) { throw 'SVG のラスタライズに失敗しました' }

    $source = New-Object System.Drawing.Bitmap($masterPng)
    if ($source.Width -lt $master) { throw "描画が小さすぎます ($($source.Width)px)" }
    Write-Host "  SVG を $($source.Width)px で描画しました"

    function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
        $w = $bmp.Width
        $h = $bmp.Height
        $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
        $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $pixels = New-Object byte[] ($data.Stride * $h)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
        $stride = $data.Stride
        $bmp.UnlockBits($data)

        $stream = New-Object System.IO.MemoryStream
        $w2 = New-Object System.IO.BinaryWriter($stream)
        $w2.Write([UInt32]40)          # biSize
        $w2.Write([Int32]$w)           # biWidth
        $w2.Write([Int32]($h * 2))     # biHeight — 色データと AND マスクの合計
        $w2.Write([UInt16]1)           # biPlanes
        $w2.Write([UInt16]32)          # biBitCount
        $w2.Write([UInt32]0)           # biCompression
        $w2.Write([UInt32]($w * $h * 4))
        $w2.Write([Int32]0); $w2.Write([Int32]0); $w2.Write([UInt32]0); $w2.Write([UInt32]0)

        for ($y = $h - 1; $y -ge 0; $y--) { $w2.Write($pixels, $y * $stride, $w * 4) }

        $maskStride = [int][Math]::Floor(($w + 31) / 32) * 4
        $w2.Write((New-Object byte[] ($maskStride * $h)))  # アルファがあるのでマスクは全 0

        $w2.Flush()
        $bytes = $stream.ToArray()
        $w2.Dispose(); $stream.Dispose()
        return , $bytes
    }

    $images = @()
    foreach ($size in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap -ArgumentList @([int]$size, [int]$size,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.DrawImage($source, (New-Object System.Drawing.Rectangle -ArgumentList @(0, 0, [int]$size, [int]$size)))
        $g.Dispose()

        if ($size -ge 256) {
            $stream = New-Object System.IO.MemoryStream
            $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $images += , $stream.ToArray()
            $stream.Dispose()
        } else {
            $images += , (Get-DibBytes $bmp)
        }
        $bmp.Dispose()
    }

    $ico = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($ico)
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$sizes.Count)

    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $size = $sizes[$i]
        $dimension = $size
        if ($size -ge 256) { $dimension = 0 }  # ICO では 256 を 0 で表す
        $writer.Write([Byte]$dimension)
        $writer.Write([Byte]$dimension)
        $writer.Write([Byte]0)   # パレットなし
        $writer.Write([Byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$images[$i].Length)
        $writer.Write([UInt32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($image in $images) { $writer.Write($image) }
    $writer.Flush()
    [System.IO.File]::WriteAllBytes($out, $ico.ToArray())
    $writer.Dispose(); $ico.Dispose()

    $source.Dispose()
    Write-Host "app.ico を書き出しました: $out ($([Math]::Round((Get-Item $out).Length / 1KB, 1)) KB, $($sizes.Count) サイズ)"
} finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
