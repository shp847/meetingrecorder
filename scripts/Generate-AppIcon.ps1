param(
    [string]$OutputDirectory = "src\MeetingRecorder.App\Assets"
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$assetDirectory = Join-Path $repoRoot $OutputDirectory
New-Item -ItemType Directory -Force -Path $assetDirectory | Out-Null

$pngPath = Join-Path $assetDirectory "MeetingRecorder.png"
$icoPath = Join-Path $assetDirectory "MeetingRecorder.ico"

function New-RoundedRectanglePath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

$bitmap = New-Object System.Drawing.Bitmap 256, 256
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)

$backgroundPath = New-RoundedRectanglePath -X 12 -Y 12 -Width 232 -Height 232 -Radius 42
$backgroundBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    ([System.Drawing.Point]::new(0, 0)),
    ([System.Drawing.Point]::new(256, 256)),
    ([System.Drawing.ColorTranslator]::FromHtml("#16302C")),
    ([System.Drawing.ColorTranslator]::FromHtml("#264F46")))
$graphics.FillPath($backgroundBrush, $backgroundPath)

$panelPath = New-RoundedRectanglePath -X 38 -Y 42 -Width 180 -Height 132 -Radius 22
$panelBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml("#F4F2EA"))
$graphics.FillPath($panelBrush, $panelPath)

$tailPoints = [System.Drawing.PointF[]]@(
    ([System.Drawing.PointF]::new(86, 156)),
    ([System.Drawing.PointF]::new(66, 194)),
    ([System.Drawing.PointF]::new(110, 173))
)
$tailBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml("#F4F2EA"))
$graphics.FillPolygon($tailBrush, $tailPoints)

$wavePen = New-Object System.Drawing.Pen ([System.Drawing.ColorTranslator]::FromHtml("#D56D3E"), 12)
$wavePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$wavePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$graphics.DrawLine($wavePen, 72, 120, 92, 96)
$graphics.DrawLine($wavePen, 92, 96, 112, 132)
$graphics.DrawLine($wavePen, 112, 132, 136, 86)
$graphics.DrawLine($wavePen, 136, 86, 160, 122)
$graphics.DrawLine($wavePen, 160, 122, 186, 102)

$ringPen = New-Object System.Drawing.Pen ([System.Drawing.ColorTranslator]::FromHtml("#7FB5A6"), 10)
$graphics.DrawEllipse($ringPen, 132, 132, 70, 70)

$dotBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml("#7FB5A6"))
$graphics.FillEllipse($dotBrush, 146, 146, 42, 42)

$bitmap.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)

$icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
$stream = [System.IO.File]::Create($icoPath)
try {
    $icon.Save($stream)
}
finally {
    $stream.Dispose()
    $icon.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

Write-Host "Generated icon assets at $assetDirectory"
