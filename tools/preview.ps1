param([string]$Exe, [string]$Dir)
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[void][Reflection.Assembly]::LoadFrom($Exe)

function Capture($form, $path) {
    $bmp = New-Object System.Drawing.Bitmap $form.Width, $form.Height
    $r = New-Object System.Drawing.Rectangle 0, 0, $form.Width, $form.Height
    $form.DrawToBitmap($bmp, $r)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$form = New-Object WartungsToolbox.MainForm
$form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
$form.Location = New-Object System.Drawing.Point 0, 0
$form.Show()
for ($i = 0; $i -lt 14; $i++) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 40 }
Capture $form (Join-Path $Dir 'preview_default.png')

# Konsole einklappen (private Methode per Reflection)
$m = $form.GetType().GetMethod('SetConsoleVisible', [Reflection.BindingFlags]'NonPublic,Instance')
$m.Invoke($form, @($false)) | Out-Null
for ($i = 0; $i -lt 8; $i++) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 40 }
Capture $form (Join-Path $Dir 'preview_collapsed.png')
$form.Close(); $form.Dispose()

# Toast-Vorschau auf dunkler Flaeche
$cv = New-Object System.Drawing.Bitmap 392, 300
$g = [System.Drawing.Graphics]::FromImage($cv)
$g.Clear([System.Drawing.Color]::FromArgb(22, 24, 28))
$specs = @(
    @('SFC scannow', 'Erfolgreich in 3.2s', [System.Drawing.Color]::FromArgb(152, 195, 121), 'E73E'),
    @('Netzwerk-Reset', 'Mit Hinweisen abgeschlossen', [System.Drawing.Color]::FromArgb(229, 192, 123), 'E7BA'),
    @('CHKDSK planen', 'Abgebrochen', [System.Drawing.Color]::FromArgb(224, 108, 117), 'E711')
)
$y = 12
foreach ($s in $specs) {
    $t = New-Object WartungsToolbox.ToastForm $s[0], $s[1], $s[2], $s[3], $null
    [void]$t.Handle
    $tb = New-Object System.Drawing.Bitmap $t.Width, $t.Height
    $tr = New-Object System.Drawing.Rectangle 0, 0, $t.Width, $t.Height
    $t.DrawToBitmap($tb, $tr)
    $g.DrawImage($tb, 12, $y)
    $y += $t.Height + 10
    $tb.Dispose(); $t.Dispose()
}
$g.Dispose()
$cv.Save((Join-Path $Dir 'preview_toast.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$cv.Dispose()
"OK"
