# Renders assets/social-preview.png (1280x640) from the design of social-preview.svg.
#
# GitHub accepts the repository social preview image through the web UI only (Settings > Social
# preview), and it must be a PNG or JPG - the SVG source next to this script cannot be uploaded
# directly. Run this script after changing social-preview.svg:
#
#     powershell -ExecutionPolicy Bypass -File assets/render-social-preview.ps1
Add-Type -AssemblyName System.Drawing

$output = Join-Path $PSScriptRoot 'social-preview.png'
$background = [System.Drawing.ColorTranslator]::FromHtml('#0b1326')
$card = [System.Drawing.ColorTranslator]::FromHtml('#16253a')
$accent = [System.Drawing.ColorTranslator]::FromHtml('#3156d3')
$light = [System.Drawing.ColorTranslator]::FromHtml('#e9eef2')
$muted = [System.Drawing.ColorTranslator]::FromHtml('#8a9aa8')

function New-RoundedRectangle([int] $x, [int] $y, [int] $width, [int] $height, [int] $radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $radius * 2
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Fill-RoundedRectangle(
    [System.Drawing.Graphics] $graphics,
    [System.Drawing.Brush] $brush,
    [int] $x, [int] $y, [int] $width, [int] $height, [int] $radius) {
    $path = New-RoundedRectangle $x $y $width $height $radius
    try {
        $graphics.FillPath($brush, $path)
    }
    finally {
        $path.Dispose()
    }
}

$bitmap = New-Object System.Drawing.Bitmap 1280, 640
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

$boardBrush = New-Object System.Drawing.SolidBrush $card
$accentBrush = New-Object System.Drawing.SolidBrush $accent
$lightBrush = New-Object System.Drawing.SolidBrush $light
$mutedBrush = New-Object System.Drawing.SolidBrush $muted
$titleFont = New-Object System.Drawing.Font 'Segoe UI', 88, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
$subtitleFont = New-Object System.Drawing.Font 'Segoe UI', 32, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$centered = New-Object System.Drawing.StringFormat
$centered.Alignment = [System.Drawing.StringAlignment]::Center

try {
    $graphics.Clear($background)

    # The kanban board: three columns, six card slots, the accent marking WIP.
    Fill-RoundedRectangle $graphics $boardBrush 540 180 200 280 16
    $tiles = @(
        @{ X = 548; Y = 196; Accent = $true }, @{ X = 548; Y = 268; Accent = $false },
        @{ X = 612; Y = 196; Accent = $false }, @{ X = 612; Y = 268; Accent = $true },
        @{ X = 676; Y = 196; Accent = $false }, @{ X = 676; Y = 268; Accent = $false }
    )
    foreach ($tile in $tiles) {
        $brush = if ($tile.Accent) { $accentBrush } else { $lightBrush }
        Fill-RoundedRectangle $graphics $brush $tile.X $tile.Y 56 56 8
    }

    $graphics.DrawString('Aiko', $titleFont, $lightBrush, 640, 462, $centered)
    $graphics.DrawString('AI kanban orchestrator', $subtitleFont, $mutedBrush, 640, 540, $centered)

    $bitmap.Save($output, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $graphics.Dispose()
    $bitmap.Dispose()
    $boardBrush.Dispose()
    $accentBrush.Dispose()
    $lightBrush.Dispose()
    $mutedBrush.Dispose()
    $titleFont.Dispose()
    $subtitleFont.Dispose()
    $centered.Dispose()
}

Write-Host "Wrote $output"
