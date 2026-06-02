# Laedt die Assembly in-proc, zeigt das Fenster kurz, macht einen Screenshot und schliesst es.
param([string]$Exe, [string]$Shot)
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[void][Reflection.Assembly]::LoadFrom($Exe)

$form = New-Object WartungsToolbox.MainForm
$form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
$form.Location = New-Object System.Drawing.Point 60, 60

$t = New-Object System.Windows.Forms.Timer
$t.Interval = 1400
$t.Add_Tick({
    try {
        $bmp = New-Object System.Drawing.Bitmap $form.Width, $form.Height
        $rect = New-Object System.Drawing.Rectangle 0, 0, $form.Width, $form.Height
        $form.DrawToBitmap($bmp, $rect)
        $bmp.Save($Shot, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
    } catch { Write-Host "Screenshot-Fehler: $_" }
    $t.Stop()
    $form.Close()
})
$t.Start()
[System.Windows.Forms.Application]::Run($form)
Write-Host "Smoke-Test fertig -> $Shot"
