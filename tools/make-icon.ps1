# Build-time helper (not shipped): makes a multi-size .ico from the idle pose, trimmed to the unicorn and centred on a square.
# Usage: .\tools\make-icon.ps1   (writes src\UnicornPet.WorkSafe\Assets\unicorn.ico; commit the result)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$png = Join-Path $repo 'assets\poses-512\idle.png'
$out = Join-Path $repo 'src\UnicornPet.WorkSafe\Assets\unicorn.ico'
Add-Type -AssemblyName System.Drawing

$src = New-Object System.Drawing.Bitmap $png
$rect = New-Object System.Drawing.Rectangle 0, 0, $src.Width, $src.Height
$data = $src.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$bytes = New-Object byte[] ($data.Stride * $src.Height)
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
$stride = $data.Stride
$src.UnlockBits($data)
$minX = $src.Width; $maxX = -1; $minY = $src.Height; $maxY = -1
for ($y = 0; $y -lt $src.Height; $y++) {
    $row = $y * $stride
    for ($x = 0; $x -lt $src.Width; $x++) {
        if ($bytes[$row + $x * 4 + 3] -gt 24) {
            if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
        }
    }
}
$w = $maxX - $minX + 1; $h = $maxY - $minY + 1
$side = [int]([Math]::Ceiling([Math]::Max($w, $h) * 1.08))
$canvas = New-Object System.Drawing.Bitmap $side, $side, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($canvas)
$g.Clear([System.Drawing.Color]::Transparent)
$g.DrawImage($src, [int](($side - $w) / 2), [int](($side - $h) / 2), (New-Object System.Drawing.Rectangle $minX, $minY, $w, $h), [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose(); $src.Dispose()

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gg = [System.Drawing.Graphics]::FromImage($bmp)
    $gg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $gg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $gg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $gg.Clear([System.Drawing.Color]::Transparent)
    $gg.DrawImage($canvas, 0, 0, $s, $s)
    $gg.Dispose()
    if ($s -ge 256) {
        # the largest size is stored as a PNG, like ordinary icons
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $blob = $ms.ToArray()
        $ms.Dispose()
    } else {
        # smaller sizes are classic 32-bit bitmaps (header + bottom-up BGRA pixels + empty AND mask) so every Windows component renders them
        $r = New-Object System.Drawing.Rectangle 0, 0, $s, $s
        $d = $bmp.LockBits($r, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $px = New-Object byte[] ($d.Stride * $s)
        [System.Runtime.InteropServices.Marshal]::Copy($d.Scan0, $px, 0, $px.Length)
        $bmp.UnlockBits($d)
        $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
        $ms = New-Object System.IO.MemoryStream
        $w2 = New-Object System.IO.BinaryWriter $ms
        $w2.Write([uint32]40); $w2.Write([int32]$s); $w2.Write([int32]($s * 2)); $w2.Write([uint16]1); $w2.Write([uint16]32)
        $w2.Write([uint32]0); $w2.Write([uint32]($s * $s * 4 + $maskRow * $s)); $w2.Write([int32]0); $w2.Write([int32]0); $w2.Write([uint32]0); $w2.Write([uint32]0)
        for ($y = $s - 1; $y -ge 0; $y--) { $w2.Write($px, $y * $d.Stride, $s * 4) }
        $w2.Write((New-Object byte[] ($maskRow * $s)))
        $w2.Flush()
        $blob = $ms.ToArray()
        $ms.Dispose()
    }
    $images += , @{ Size = $s; Bytes = $blob }
    $bmp.Dispose()
}
$canvas.Dispose()

New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($i in $images) {
    $dim = if ($i.Size -ge 256) { 0 } else { $i.Size }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$i.Bytes.Length); $bw.Write([uint32]$offset)
    $offset += $i.Bytes.Length
}
foreach ($i in $images) { $bw.Write($i.Bytes) }
$bw.Flush(); $fs.Dispose()
Write-Host ("Wrote {0} ({1} bytes, sizes: {2})" -f $out, (Get-Item $out).Length, ($sizes -join ', '))
