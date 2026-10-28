[CmdletBinding()]
param(
    [switch]$NoMask
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$Source = Join-Path $RootDir "resources" "logo.jpeg"
$OutputDir = Join-Path $RootDir "src" "Mycel.App" "Assets" "icons"

if (-not (Test-Path $Source)) {
    Write-Error "Master logo not found at $Source"
    exit 1
}

$MagickCmd = Get-Command magick -ErrorAction SilentlyContinue
if (-not $MagickCmd) {
    $MagickCmd = Get-Command convert -ErrorAction SilentlyContinue
}
if (-not $MagickCmd) {
    Write-Error "ImageMagick ('magick' or 'convert') is required."
    exit 1
}

$Dim = & magick identify -format "%w %h" $Source
$Width = [int]($Dim.Split(" ")[0])
$Height = [int]($Dim.Split(" ")[1])

if ($Width -lt 512 -or $Height -lt 512) {
    Write-Warning "Master logo is smaller than 512px ($Width x $Height)."
}

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$Sizes = @(16, 24, 32, 48, 64, 128, 256, 512)
$IcoSizes = @(16, 24, 32, 48, 64, 128, 256)

Write-Host "Generating icon assets in $OutputDir..."

foreach ($s in $Sizes) {
    $OutPng = Join-Path $OutputDir "mycel_$s.png"
    if (-not $NoMask) {
        $r = [Math]::Max(1, [int]($s * 0.20))
        $sm1 = $s - 1
        & magick $Source -filter Lanczos -resize "${s}x${s}" `
            "(" +clone -alpha transparent -background none -fill white -draw "roundrectangle 0,0,$sm1,$sm1,$r,$r" ")" `
            -alpha off -compose CopyOpacity -composite $OutPng
    } else {
        & magick $Source -filter Lanczos -resize "${s}x${s}" $OutPng
    }
    Write-Host "  -> Created mycel_$s.png"
}

# Generate multi-resolution mycel.ico with PNG-compressed entries
$IcoOutput = Join-Path $OutputDir "mycel.ico"
$numImages = $IcoSizes.Count
$header = [byte[]]@(0, 0, 1, 0, ($numImages -band 0xFF), (($numImages -shr 8) -band 0xFF))

$pngBlobs = @()
foreach ($s in $IcoSizes) {
    $pngPath = Join-Path $OutputDir "mycel_$s.png"
    $pngBlobs += , [System.IO.File]::ReadAllBytes($pngPath)
}

$offset = 6 + 16 * $numImages
$entries = [System.Collections.Generic.List[byte]]::new()
for ($i = 0; $i -lt $numImages; $i++) {
    $s = $IcoSizes[$i]
    $blob = $pngBlobs[$i]
    $w = if ($s -eq 256) { [byte]0 } else { [byte]$s }
    $h = if ($s -eq 256) { [byte]0 } else { [byte]$s }
    $size = $blob.Length

    $entries.Add($w)
    $entries.Add($h)
    $entries.Add(0)
    $entries.Add(0)
    $entries.AddRange([BitConverter]::GetBytes([uint16]1))
    $entries.AddRange([BitConverter]::GetBytes([uint16]32))
    $entries.AddRange([BitConverter]::GetBytes([uint32]$size))
    $entries.AddRange([BitConverter]::GetBytes([uint32]$offset))
    $offset += $size
}

$allBytes = [System.Collections.Generic.List[byte]]::new()
$allBytes.AddRange($header)
$allBytes.AddRange($entries)
foreach ($blob in $pngBlobs) {
    $allBytes.AddRange($blob)
}
[System.IO.File]::WriteAllBytes($IcoOutput, $allBytes.ToArray())
Write-Host "  -> Created mycel.ico (PNG-compressed $IcoSizes)"
Write-Host "Done."
