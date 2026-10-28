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

$IcoInputs = $IcoSizes | ForEach-Object { Join-Path $OutputDir "mycel_$_.png" }
$IcoOutput = Join-Path $OutputDir "mycel.ico"
& magick @IcoInputs $IcoOutput
Write-Host "  -> Created mycel.ico"
Write-Host "Done."
