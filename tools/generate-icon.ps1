# Erzeugt assets\app.ico (Segoe-MDL2-Schraubenschluessel auf dunklem Quadrat)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root    = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$assets  = Join-Path $root 'assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null
$icoPath = Join-Path $assets 'app.ico'

$size = 256
$bmp  = New-Object System.Drawing.Bitmap $size, $size
$g    = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g.Clear([System.Drawing.Color]::Transparent)

# abgerundetes Quadrat als Hintergrund
$rad  = 52
$d    = $rad * 2
$x    = 10; $y = 10; $w = $size - 20; $h = $size - 20
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$path.AddArc($x, $y, $d, $d, 180, 90)
$path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
$path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
$path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
$path.CloseFigure()
$bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(30, 34, 41))
$g.FillPath($bg, $path)

# Glyph (E90F = Repair / Schraubenschluessel)
$font  = New-Object System.Drawing.Font ('Segoe MDL2 Assets', 122, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$teal  = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(78, 205, 196))
$sf    = New-Object System.Drawing.StringFormat
$sf.Alignment     = [System.Drawing.StringAlignment]::Center
$sf.LineAlignment = [System.Drawing.StringAlignment]::Center
$glyph = [string][char]0xE90F
$rectF = New-Object System.Drawing.RectangleF 0, 0, $size, $size
$g.DrawString($glyph, $font, $teal, $rectF, $sf)
$g.Dispose()

# als ICO mit PNG-Nutzlast (Vista+) schreiben
$ms = New-Object System.IO.MemoryStream
$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$png = $ms.ToArray()

$ico = New-Object System.IO.MemoryStream
$bw  = New-Object System.IO.BinaryWriter $ico
$bw.Write([uint16]0)            # reserviert
$bw.Write([uint16]1)            # Typ = Icon
$bw.Write([uint16]1)            # Anzahl
$bw.Write([byte]0)             # Breite 0 => 256
$bw.Write([byte]0)             # Hoehe 0 => 256
$bw.Write([byte]0)             # Farben
$bw.Write([byte]0)             # reserviert
$bw.Write([uint16]1)           # Ebenen
$bw.Write([uint16]32)          # Bit pro Pixel
$bw.Write([uint32]$png.Length) # Groesse
$bw.Write([uint32]22)          # Offset (6 + 16)
$bw.Write($png)
$bw.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $ico.ToArray())
$bw.Dispose(); $ico.Dispose(); $ms.Dispose(); $bmp.Dispose()

"Icon erstellt: $icoPath ($([math]::Round($png.Length/1KB,1)) KB PNG)"
