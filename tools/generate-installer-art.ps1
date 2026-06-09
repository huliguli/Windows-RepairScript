# Erzeugt die gebrandeten Setup-Bilder (dunkel, Teal-Logo) fuer Inno Setup:
#   assets\wizard.bmp        (gross, Welcome/Finish)
#   assets\wizard-small.bmp  (klein, Kopfzeile)
# Hintergrund FLACH in App-Dunkel (#0d0f14), damit es nahtlos mit dem Wizard-Panel verschmilzt.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root   = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$assets = Join-Path $root 'assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null

$bgCol = [System.Drawing.Color]::FromArgb(13, 15, 20)   # = clBg #0d0f14
$reg = [System.Drawing.FontStyle]::Regular
$px  = [System.Drawing.GraphicsUnit]::Pixel
$sf  = New-Object System.Drawing.StringFormat
$sf.Alignment = [System.Drawing.StringAlignment]::Center
$sf.LineAlignment = [System.Drawing.StringAlignment]::Center
$dark = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(8, 28, 26))
$glyph = [string][char]0xE90F

function Glow($g, $cx, $cy, $r) {
    $gp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $gp.AddEllipse($cx - $r, $cy - $r, $r * 2, $r * 2)
    $pgb = New-Object System.Drawing.Drawing2D.PathGradientBrush($gp)
    $pgb.CenterColor = [System.Drawing.Color]::FromArgb(60, 45, 212, 191)
    $pgb.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 45, 212, 191))
    $g.FillPath($pgb, $gp)
}
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
$g.Clear($bgCol)
Glow $g 82 134 80
Logo $g 52 104 60 30
$tf = New-Object System.Drawing.Font ('Segoe UI Semibold', 13, [System.Drawing.FontStyle]::Bold)
$tb = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(233,235,240))
$g.DrawString('Windows-Wartung', $tf, $tb, (New-Object System.Drawing.RectangleF 0,178,$bw,24), $sf)
$stf = New-Object System.Drawing.Font ('Segoe UI', 8.5)
$sb = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(154,161,176))
$g.DrawString('Wartungs-Toolbox', $stf, $sb, (New-Object System.Drawing.RectangleF 0,201,$bw,18), $sf)
$g.Dispose()
$bmp.Save((Join-Path $assets 'wizard.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$bmp.Dispose()

# ---- kleines Bild 55x55 ----
$s = 55
$bmp2 = New-Object System.Drawing.Bitmap $s, $s
$g2 = [System.Drawing.Graphics]::FromImage($bmp2)
$g2.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g2.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g2.Clear($bgCol)
Logo $g2 9 9 37 18
$g2.Dispose()
$bmp2.Save((Join-Path $assets 'wizard-small.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$bmp2.Dispose()

"OK: wizard.bmp + wizard-small.bmp (flacher dunkler Hintergrund)"
