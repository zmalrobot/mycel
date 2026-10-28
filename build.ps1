# Mycel Release Packaging Script for PowerShell
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

$version = "1.0.0"
if (Test-Path "Directory.Build.props") {
    [xml]$xml = Get-Content "Directory.Build.props"
    if ($xml.Project.PropertyGroup.Version) {
        $version = $xml.Project.PropertyGroup.Version
    }
}

Write-Host "========================================================================" -ForegroundColor Cyan
Write-Host " Building Mycel v$version Release Packages" -ForegroundColor Cyan
Write-Host "========================================================================" -ForegroundColor Cyan

New-Item -ItemType Directory -Force -Path "dist" | Out-Null
New-Item -ItemType Directory -Force -Path "artifacts/linux-x64" | Out-Null
New-Item -ItemType Directory -Force -Path "artifacts/win-x64" | Out-Null

Write-Host "`n--> Step 1/4: Running automated tests..." -ForegroundColor Yellow
dotnet test -c Release --filter "FullyQualifiedName!~PerformanceProfilingTests" --nologo
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }

Write-Host "`n--> Step 2/4: Publishing linux-x64 (self-contained, SingleFile, ReadyToRun)..." -ForegroundColor Yellow
dotnet publish src/Mycel.App/Mycel.App.csproj `
    -c Release `
    -r linux-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:EnableCompressionInSingleFile=true `
    -o artifacts/linux-x64 `
    --nologo

Copy-Item "LICENSE" "artifacts/linux-x64/"
Copy-Item "THIRD_PARTY_NOTICES.md" "artifacts/linux-x64/"

Write-Host "Creating dist/Mycel-$version-linux-x64.tar.gz..."
tar -czf "dist/Mycel-$version-linux-x64.tar.gz" -C artifacts/linux-x64 .

Write-Host "`n--> Step 3/4: Publishing win-x64 (self-contained, SingleFile, ReadyToRun)..." -ForegroundColor Yellow
dotnet publish src/Mycel.App/Mycel.App.csproj `
    -c Release `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:EnableCompressionInSingleFile=true `
    -o artifacts/win-x64 `
    --nologo

Copy-Item "LICENSE" "artifacts/win-x64/"
Copy-Item "THIRD_PARTY_NOTICES.md" "artifacts/win-x64/"

Write-Host "Creating dist/Mycel-$version-win-x64.zip..."
Compress-Archive -Path "artifacts/win-x64/*" -DestinationPath "dist/Mycel-$version-win-x64.zip" -Force

Write-Host "`n--> Step 4/4: Verification of release packages..." -ForegroundColor Yellow
Get-Item "dist/Mycel-$version-linux-x64.tar.gz" | Select-Object Name, Length, LastWriteTime
Get-Item "dist/Mycel-$version-win-x64.zip" | Select-Object Name, Length, LastWriteTime

Write-Host "`n========================================================================" -ForegroundColor Green
Write-Host " SUCCESS: Mycel v$version packages generated in dist/" -ForegroundColor Green
Write-Host "========================================================================" -ForegroundColor Green

