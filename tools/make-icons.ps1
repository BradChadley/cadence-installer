<#
  make-icons.ps1: draws the installer's own icon (src\Assets\chooser.ico) and turns Cadence's icon into the PNG
  picture the card shows (src\Assets\cadence.png).
  Run again only when Cadence's icon changes: copy its new .ico over src\Assets\cadence.ico first.
  Run:  pwsh -File tools\make-icons.ps1
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $PSScriptRoot '..\src\Assets' | Resolve-Path | ForEach-Object Path

# Cadence's picture: the largest picture inside its .ico, at 128 pixels.
foreach ($pair in @(, @('cadence.ico', 'cadence.png'))) {
    $icon = [System.Drawing.Icon]::new((Join-Path $assets $pair[0]), 256, 256)
    $big = $icon.ToBitmap()
    $out = [System.Drawing.Bitmap]::new(128, 128)
    $g = [System.Drawing.Graphics]::FromImage($out)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.SmoothingMode = 'HighQuality'
    $g.DrawImage($big, 0, 0, 128, 128)
    $g.Dispose(); $out.Save((Join-Path $assets $pair[1]), [System.Drawing.Imaging.ImageFormat]::Png); $out.Dispose(); $big.Dispose(); $icon.Dispose()
    "$($pair[1]) written"
}

# The installer's own icon: a dark rounded tile, a rose arrow coming down onto a line (the shelf it lands on).
function Draw([int]$s) {
    $b = [System.Drawing.Bitmap]::new($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $r = [single]($s * 0.22); $d = 2 * $r; $w = [single]($s - 1)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($w - $d, 0, $d, $d, 270, 90)
    $path.AddArc($w - $d, $w - $d, $d, $d, 0, 90); $path.AddArc(0, $w - $d, $d, $d, 90, 90); $path.CloseFigure()
    $fill = [System.Drawing.Drawing2D.LinearGradientBrush]::new([System.Drawing.PointF]::new(0, 0), [System.Drawing.PointF]::new(0, $s), [System.Drawing.Color]::FromArgb(255, 0x2A, 0x20, 0x26), [System.Drawing.Color]::FromArgb(255, 0x14, 0x12, 0x15))
    $g.FillPath($fill, $path)
    # an arrow coming down onto a shelf: getting something onto this PC
    $t = [single][Math]::Max(1.6, $s * 0.085)
    $arrow = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 0xE8, 0xB8, 0xCC), $t)
    $arrow.StartCap = 'Round'; $arrow.EndCap = 'Round'; $arrow.LineJoin = 'Round'
    $g.DrawLine($arrow, [single]($s * 0.5), [single]($s * 0.2), [single]($s * 0.5), [single]($s * 0.58))
    $g.DrawLines($arrow, [System.Drawing.PointF[]]@([System.Drawing.PointF]::new($s * 0.32, $s * 0.42), [System.Drawing.PointF]::new($s * 0.5, $s * 0.6), [System.Drawing.PointF]::new($s * 0.68, $s * 0.42)))
    $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 0xB0, 0x76, 0x8B), $t)
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawLine($pen, [single]($s * 0.24), [single]($s * 0.77), [single]($s * 0.76), [single]($s * 0.77))
    $g.Dispose()
    $ms = [IO.MemoryStream]::new(); $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
    , $ms.ToArray()
}
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) { , (Draw $s) }
$ico = [IO.MemoryStream]::new(); $bw = [IO.BinaryWriter]::new($ico)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $n = $pngs[$i].Length
    $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$n); $bw.Write([uint32]$offset); $offset += $n
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush(); [IO.File]::WriteAllBytes((Join-Path $assets 'chooser.ico'), $ico.ToArray())
[IO.File]::WriteAllBytes((Join-Path $assets 'chooser.png'), $pngs[5])
'chooser.ico written'
