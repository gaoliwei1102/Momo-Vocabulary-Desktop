[CmdletBinding()]
param([string]$PreviewDirectory = 'artifacts/icon-preview')

$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw 'Use powershell.exe -NoProfile -STA -File scripts/create-icon.ps1 to render the WPF vector icon.'
}
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$projectRoot = Split-Path -Parent $PSScriptRoot
$assetDirectory = Join-Path $projectRoot 'src\WordBubble\Assets'
$previewPath = [IO.Path]::GetFullPath((Join-Path $projectRoot $PreviewDirectory))
New-Item -ItemType Directory -Path $previewPath -Force | Out-Null

# One editable vector source, with real small ICO frames for Windows tray / DPI.
# No font, external image service, native HICON handle, or user profile is needed.
$visual = [Windows.Markup.XamlReader]::Parse([IO.File]::ReadAllText((Join-Path $assetDirectory 'mascot-icon.xaml')))
$visual.Measure([Windows.Size]::new(64, 64))
$visual.Arrange([Windows.Rect]::new(0, 0, 64, 64))
$visual.UpdateLayout()
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = @()
foreach ($size in $sizes) {
    $drawing = [Windows.Media.DrawingVisual]::new()
    $context = $drawing.RenderOpen()
    try {
        $brush = [Windows.Media.VisualBrush]::new($visual)
        $context.DrawRectangle($brush, $null, [Windows.Rect]::new(0, 0, $size, $size))
    } finally { $context.Close() }
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($drawing)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $memory = [IO.MemoryStream]::new()
    try { $encoder.Save($memory); $bytes = $memory.ToArray() } finally { $memory.Dispose() }
    [IO.File]::WriteAllBytes((Join-Path $previewPath "icon-$size.png"), $bytes)
    $frames += ,$bytes
}

$stream = [IO.File]::Create((Join-Path $assetDirectory 'app.ico'))
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose(); $stream.Dispose() }
Write-Output "Generated app.ico with frames: $($sizes -join ', ') pixels."
Write-Output "Previews: $previewPath"
