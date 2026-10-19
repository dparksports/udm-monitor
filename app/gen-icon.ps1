Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot "src\UdmMonitor.App\Assets\app.ico"
New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null

$bmp = New-Object System.Drawing.Bitmap 32, 32
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

$bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0, 120, 215))
$g.FillEllipse($bg, 2, 2, 28, 28)

$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 3.0
$pts = @(
    [System.Drawing.Point]::new(7, 19),
    [System.Drawing.Point]::new(12, 19),
    [System.Drawing.Point]::new(15, 10),
    [System.Drawing.Point]::new(19, 23),
    [System.Drawing.Point]::new(22, 19),
    [System.Drawing.Point]::new(25, 19)
)
$g.DrawLines($pen, $pts)
$g.Dispose()

$icon = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
$fs = [System.IO.File]::Create($out)
$icon.Save($fs)
$fs.Close()
$icon.Dispose()
$bmp.Dispose()

Write-Host "icon written: $out ($((Get-Item $out).Length) bytes)"
