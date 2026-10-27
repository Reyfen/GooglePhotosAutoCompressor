<#
.SYNOPSIS
    Builds the app and runs the unit tests (no browser, no Google account needed).

.EXAMPLE
    ./build.ps1                      # Debug build + unit tests
    ./build.ps1 -Configuration Release
    ./build.ps1 -SkipTests
#>
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "The .NET 8 SDK is not installed. Get it from https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Red
    exit 1
}

Write-Host "Building ($Configuration)..." -ForegroundColor Cyan
dotnet build GooglePhotosAutoCompressor.sln -c $Configuration
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed." -ForegroundColor Red; exit $LASTEXITCODE }

if ($SkipTests) {
    Write-Host "Build OK (tests skipped)." -ForegroundColor Green
    exit 0
}

# The live Google Photos tests are slow and need a signed-in account: run them with ./integration_tests.ps1.
Write-Host "Running unit tests..." -ForegroundColor Cyan
dotnet test GooglePhotosAutoCompressor.sln -c $Configuration --no-build --filter "Category!=Live"
if ($LASTEXITCODE -ne 0) { Write-Host "Unit tests failed." -ForegroundColor Red; exit $LASTEXITCODE }

Write-Host "Build and unit tests OK." -ForegroundColor Green
