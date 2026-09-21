# app.ico を生成する。ビルド時に無ければ build.ps1 から呼ばれる。
# 小さいサイズは DIB で書く。PNG 圧縮エントリは Windows は読めるが
# System.Drawing.Icon が読めず、後から扱いづらいため 256px だけに留める。
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot 'app.ico'
$sizes = @(16, 24, 32, 48, 64, 128, 256)

$back = [System.Drawing.Color]::FromArgb(255, 26, 28, 32)
$accent = [System.Drawing.Color]::FromArgb(255, 90, 170, 255)
$bracket = [System.Drawing.Color]::FromArgb(255, 244, 246, 250)

function New-Face([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    $pad = [Math]::Max(1, [int]($size * 0.06))
    $radius = [Math]::Max(2, [int]($size * 0.22))
    $rect = New-Object System.Drawing.Rectangle($pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad))

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc(($rect.Right - $d), $rect.Y, $d, $d, 270, 90)
    $path.AddArc(($rect.Right - $d), ($rect.Bottom - $d), $d, $d, 0, 90)
    $path.AddArc($rect.X, ($rect.Bottom - $d), $d, $d, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.SolidBrush($back)
    $g.FillPath($brush, $path)

    # 範囲選択を表すコーナーブラケット
    $pen = New-Object System.Drawing.Pen($bracket, [Math]::Max(1.0, $size * 0.075))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    $left = $size * 0.28
    $top = $size * 0.28
    $right = $size * 0.72
    $bottom = $size * 0.72
    $arm = $size * 0.15

    $g.DrawLines($pen, @(
        (New-Object System.Drawing.PointF($left, ($top + $arm))),
        (New-Object System.Drawing.PointF($left, $top)),
        (New-Object System.Drawing.PointF(($left + $arm), $top))))
    $g.DrawLines($pen, @(
        (New-Object System.Drawing.PointF(($right - $arm), $top)),
        (New-Object System.Drawing.PointF($right, $top)),
        (New-Object System.Drawing.PointF($right, ($top + $arm)))))
    $g.DrawLines($pen, @(
        (New-Object System.Drawing.PointF($left, ($bottom - $arm))),
        (New-Object System.Drawing.PointF($left, $bottom)),
        (New-Object System.Drawing.PointF(($left + $arm), $bottom))))
    $g.DrawLines($pen, @(
        (New-Object System.Drawing.PointF(($right - $arm), $bottom)),
        (New-Object System.Drawing.PointF($right, $bottom)),
        (New-Object System.Drawing.PointF($right, ($bottom - $arm)))))

    $dot = [Math]::Max(2.0, $size * 0.16)
    $dotBrush = New-Object System.Drawing.SolidBrush($accent)
    $g.FillEllipse($dotBrush, ($size / 2.0 - $dot / 2.0), ($size / 2.0 - $dot / 2.0), $dot, $dot)

    $dotBrush.Dispose(); $pen.Dispose(); $brush.Dispose(); $path.Dispose(); $g.Dispose()
    return $bmp
}

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

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $stream = New-Object System.IO.MemoryStream
    $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    return , $bytes
}

$images = @()
foreach ($size in $sizes) {
    $face = New-Face $size
    if ($size -ge 256) {
        $images += , (Get-PngBytes $face)
    } else {
        $images += , (Get-DibBytes $face)
    }
    $face.Dispose()
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

Write-Host "app.ico を書き出しました: $out"
