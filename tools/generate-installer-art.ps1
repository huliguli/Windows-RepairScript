# Erzeugt die gebrandeten Setup-Bilder (dunkel, Teal-Logo) fuer Inno Setup:
#   assets\wizard.bmp        (gross, Welcome/Finish)
#   assets\wizard-small.bmp  (klein, Kopfzeile)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root   = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$assets = Join-Path $root 'assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null

$reg = [System.Drawing.FontStyle]::Regular
$px  = [System.Drawing.GraphicsUnit]::Pixel
$sf  = New-Object System.Drawing.StringFormat
$sf.Alignment = [System.Drawing.StringAlignment]::Center
$sf.LineAlignment = [System.Drawing.StringAlignment]::Center
$dark = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(8, 28, 26))
$glyph = [string][char]0xE90F

function Logo($g, $x, $y, $size, $glyphPx) {
    $rect = New-Object System.Drawing.Rectangle $x, $y, $size, $size
    $teal = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(45,212,191), [System.Drawing.Color]::FromArgb(56,189,248), 45)
    $rad = [int]($size * 0.27); $d = $rad * 2
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $p.AddArc($rect.Right-$d, $rect.Y, $d, $d, 270, 90)
    $p.AddArc($rect.Right-$d, $rect.Bottom-$d, $d, $d, 0, 90)
    $p.AddArc($rect.X, $rect.Bottom-$d, $d, $d, 90, 90)
    $p.CloseFigure()
    $g.FillPath($teal, $p)
    $f = New-Object System.Drawing.Font ('Segoe MDL2 Assets', $glyphPx, $reg, $px)
    $g.DrawString($glyph, $f, $dark, (New-Object System.Drawing.RectangleF $x, $y, $size, $size), $sf)
}

# ---- grosses Bild 164x314 ----
$bw = 164; $bh = 314
$bmp = New-Object System.Drawing.Bitmap $bw, $bh
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.Rectangle 0,0,$bw,$bh), [System.Drawing.Color]::FromArgb(16,19,24), [System.Drawing.Color]::FromArgb(9,11,16), 90)
$g.FillRectangle($bg, 0, 0, $bw, $bh)
Logo $g 52 56 60 30
$tf = New-Object System.Drawing.Font ('Segoe UI Semibold', 13, [System.Drawing.FontStyle]::Bold)
$tb = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(233,235,240))
$g.DrawString('Windows-Wartung', $tf, $tb, (New-Object System.Drawing.RectangleF 0,128,$bw,24), $sf)
$stf = New-Object System.Drawing.Font ('Segoe UI', 8.5)
$sb = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(154,161,176))
$g.DrawString('Wartungs-Toolbox', $stf, $sb, (New-Object System.Drawing.RectangleF 0,151,$bw,18), $sf)
$g.Dispose()
$bmp.Save((Join-Path $assets 'wizard.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$bmp.Dispose()

# ---- kleines Bild 55x55 ----
$s = 55
$bmp2 = New-Object System.Drawing.Bitmap $s, $s
$g2 = [System.Drawing.Graphics]::FromImage($bmp2)
$g2.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g2.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g2.Clear([System.Drawing.Color]::FromArgb(16,19,24))
Logo $g2 9 9 37 18
$g2.Dispose()
$bmp2.Save((Join-Path $assets 'wizard-small.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$bmp2.Dispose()

"OK: wizard.bmp + wizard-small.bmp erstellt"
