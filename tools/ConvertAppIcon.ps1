param(
    [Parameter(Mandatory=$true)][string]$SourcePng,
    [Parameter(Mandatory=$true)][string]$DestinationIco
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if (Test-Path -LiteralPath $DestinationIco) { throw 'Destination already exists; choose a new filename.' }
# Format packaging only: preserve artwork/alpha and provide native Windows icon sizes.
$iconSource = [Drawing.Image]::FromFile([IO.Path]::GetFullPath($SourcePng))
$iconSizes = @(16,24,32,48,64,128,256)
$iconFrames = New-Object 'Collections.Generic.List[byte[]]'
try {
    foreach ($iconSize in $iconSizes) {
        $iconBitmap = New-Object Drawing.Bitmap($iconSize,$iconSize,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $iconGraphics = [Drawing.Graphics]::FromImage($iconBitmap)
        $iconStream = New-Object IO.MemoryStream
        try {
            $iconGraphics.Clear([Drawing.Color]::Transparent)
            $iconGraphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $iconGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $iconGraphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $iconGraphics.DrawImage($iconSource,[Drawing.Rectangle]::new(0,0,$iconSize,$iconSize))
            $iconBitmap.Save($iconStream,[Drawing.Imaging.ImageFormat]::Png)
            $iconFrames.Add($iconStream.ToArray())
        } finally { $iconGraphics.Dispose(); $iconBitmap.Dispose(); $iconStream.Dispose() }
    }
} finally { $iconSource.Dispose() }
$iconFile = [IO.File]::Open([IO.Path]::GetFullPath($DestinationIco),[IO.FileMode]::CreateNew)
$iconWriter = New-Object IO.BinaryWriter($iconFile)
try {
    $iconWriter.Write([uint16]0); $iconWriter.Write([uint16]1); $iconWriter.Write([uint16]$iconSizes.Count)
    $iconOffset = 6 + 16 * $iconSizes.Count
    for ($iconIndex=0;$iconIndex -lt $iconSizes.Count;$iconIndex++) {
        $iconDimension = $iconSizes[$iconIndex] % 256
        $iconWriter.Write([byte]$iconDimension); $iconWriter.Write([byte]$iconDimension)
        $iconWriter.Write([byte]0); $iconWriter.Write([byte]0)
        $iconWriter.Write([uint16]1); $iconWriter.Write([uint16]32)
        $iconWriter.Write([uint32]$iconFrames[$iconIndex].Length); $iconWriter.Write([uint32]$iconOffset)
        $iconOffset += $iconFrames[$iconIndex].Length
    }
    foreach ($iconFrame in $iconFrames) { $iconWriter.Write([byte[]]$iconFrame) }
} finally { $iconWriter.Dispose(); $iconFile.Dispose() }
Write-Output "Created ICO sizes: $($iconSizes -join ', ')"
