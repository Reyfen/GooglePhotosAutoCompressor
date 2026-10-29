<#
.SYNOPSIS
    Builds the portable Windows app: one .exe that runs without installing anything
    (.NET, ffmpeg and the Playwright browser driver are inside).

    Linux packages (.AppImage.tar.gz and .deb) are built on Linux with packaging/linux/build-packages.sh,
    or by the "Packages" GitHub workflow, which also tests them on clean machines.

.EXAMPLE
    ./publish.ps1                    # dist/GooglePhotosAutoCompressor-v1.0.0-win-x64.exe
    ./publish.ps1 -Version v1.2.0    # "1.2.0" works too; file names always get the "v"
#>
param(
    [string]$Version = "v1.0.0",
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

# Names use "v1.2.0"; the exe's own version number must be digits only ("1.2.0").
$Number = $Version.TrimStart("v", "V")
$Version = "v$Number"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "The .NET 8 SDK is not installed. Get it from https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Red
    exit 1
}

$ffmpeg = "GooglePhotosAutoCompressor/tools/ffmpeg.exe"
if (-not (Test-Path $ffmpeg) -or (Get-Item $ffmpeg).Length -lt 1MB) {
    Write-Host "Warning: $ffmpeg is missing or a Git LFS pointer (run 'git lfs pull'). The exe will offer to download ffmpeg instead." -ForegroundColor Yellow
}

$out = Join-Path $PSScriptRoot "dist/publish-$Runtime"
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Publishing $Runtime $Version..." -ForegroundColor Cyan
dotnet publish GooglePhotosAutoCompressor.App -c Release -r $Runtime --self-contained `
    -p:Version=$Number `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IncludeAllContentForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $out
if ($LASTEXITCODE -ne 0) { Write-Host "Publish failed." -ForegroundColor Red; exit $LASTEXITCODE }

$target = Join-Path $PSScriptRoot "dist/GooglePhotosAutoCompressor-$Version-$Runtime.exe"
Move-Item (Join-Path $out "GooglePhotosAutoCompressor.exe") $target -Force
Remove-Item $out -Recurse -Force

Write-Host "Checking the exe (no Google account needed)..." -ForegroundColor Cyan
$check = Start-Process -FilePath $target -ArgumentList "--check" -Wait -PassThru -NoNewWindow
if ($check.ExitCode -ne 0) { Write-Host "Setup check failed (see above)." -ForegroundColor Red; exit 1 }

$size = [math]::Round((Get-Item $target).Length / 1MB)
Write-Host "Done: $target ($size MB)" -ForegroundColor Green
