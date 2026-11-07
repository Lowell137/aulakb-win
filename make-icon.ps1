Add-Type -AssemblyName System.Drawing
$render = {
  param([int]$size)
  $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
  $g.Clear([System.Drawing.Color]::Transparent)
  # rounded background
  $pad = [math]::Max(1, [int]($size * 0.06))
  $r = $size - 2*$pad
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $rad = [int]($size * 0.18)
  $path.AddArc($pad, $pad, $rad*2, $rad*2, 180, 90)
  $path.AddArc($pad+$r-$rad*2, $pad, $rad*2, $rad*2, 270, 90)
  $path.AddArc($pad+$r-$rad*2, $pad+$r-$rad*2, $rad*2, $rad*2, 0, 90)
  $path.AddArc($pad, $pad+$r-$rad*2, $rad*2, $rad*2, 90, 90)
  $path.CloseFigure()
  $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 30, 41, 59))
  $g.FillPath($brush, $path)
  # glyph
  $fontFamilies = @('Segoe Fluent Icons','Segoe MDL2 Assets','Segoe UI Symbol')
  $chosen = $null
  foreach ($f in $fontFamilies) { try { $ff = New-Object System.Drawing.FontFamily($f); if ($ff) { $chosen = $ff; break } } catch {} }
  if (-not $chosen) { $chosen = [System.Drawing.FontFamily]::GenericSansSerif }
  $fontSize = [int]($size * 0.58)
  $font = New-Object System.Drawing.Font($chosen, $fontSize, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
  $sf = New-Object System.Drawing.StringFormat
  $sf.Alignment = [System.Drawing.StringAlignment]::Center
  $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
  $tb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
  $g.DrawString([char]0xE765, $font, $tb, (New-Object System.Drawing.RectangleF(0,0,$size,$size)), $sf)
  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  return $ms.ToArray()
}
$sizes = @(256, 64, 32, 16)
$frames = @()
foreach ($s in $sizes) { $frames += ,(& $render $s) }
# write ICO
$icoPath = "src\AulaKb.App\Assets\AppIcon.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
# ICONDIR
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$frames.Count)
# offsets: header 6 + 16*count
$offset = 6 + 16*$frames.Count
$entries = @()
foreach ($i in 0..($frames.Count-1)) {
  $sz = $sizes[$i]
  $data = $frames[$i]
  $bw.Write([byte]($sz -band 0xFF))   # width (0 means 256)
  $bw.Write([byte]($sz -band 0xFF))   # height
  $bw.Write([byte]0)                  # color count
  $bw.Write([byte]0)                  # reserved
  $bw.Write([UInt16]1)                # planes
  $bw.Write([UInt16]32)               # bitcount
  $bw.Write([UInt32]$data.Length)     # size in bytes
  $bw.Write([UInt32]$offset)          # offset
  $offset += $data.Length
  $entries += $data
}
foreach ($d in $entries) { $bw.Write($d) }
$bw.Flush(); $bw.Close()
Write-Host "written $icoPath ($($frames.Count) frames)"
