<#
.SYNOPSIS
    Runs the live Google Photos page tests. Each step the app relies on is tried on the real
    website, and your local Claude Code checks a screenshot of the result.

    Read-only: nothing is uploaded, deleted or changed in your account.
    Screenshots of your library are sent to Claude.

.EXAMPLE
    ./integration_tests.ps1              # run the tests
    ./integration_tests.ps1 -SignIn      # first time: sign in to Google in the window that opens (15 min)
    ./integration_tests.ps1 -BinDialog   # also open the "Move to bin" confirmation (then cancel it)
#>
param(
    # Wait up to 15 minutes for you to sign in when the profile is signed out.
    [switch]$SignIn,
    # Also test the "Move to bin" confirmation dialog. Worst case one item goes to the bin.
    [switch]$BinDialog,
    # Run Chrome without a window. Cannot be used with -SignIn.
    [switch]$Headless,
    # Claude model used to check screenshots.
    [string]$Model = "sonnet",
    # Browser profile to use. Default: the app's own profile from its last run.
    [string]$ProfileDir
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

function Fail([string]$message) {
    Write-Host $message -ForegroundColor Red
    exit 1
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Fail "The .NET 8 SDK is not installed. Get it from https://dotnet.microsoft.com/download/dotnet/8.0"
}
if (-not $env:GPAC_TEST_CLAUDE_PATH -and -not (Get-Command claude -ErrorAction SilentlyContinue)) {
    Fail ("Claude Code is not installed. Install it (https://claude.com/claude-code), run 'claude' once to sign in, " +
          "then try again. If it is installed outside PATH, set GPAC_TEST_CLAUDE_PATH to its full path.")
}
if ($SignIn -and $Headless) {
    Fail "-SignIn needs a visible window: leave out -Headless."
}

$settings = @{
    GPAC_TEST_LIVE            = "1"
    GPAC_TEST_SIGN_IN         = if ($SignIn) { "1" } else { $null }
    GPAC_TEST_LIVE_BIN_DIALOG = if ($BinDialog) { "1" } else { $null }
    GPAC_TEST_HEADLESS        = if ($Headless) { "1" } else { $null }
    GPAC_TEST_CLAUDE_MODEL    = $Model
    GPAC_TEST_PROFILE_DIR     = if ($ProfileDir) { (Resolve-Path $ProfileDir).Path } else { $env:GPAC_TEST_PROFILE_DIR }
}

# Set the variables for this run only, and put the old values back afterwards.
$saved = @{}
foreach ($name in $settings.Keys) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name)
    [Environment]::SetEnvironmentVariable($name, $settings[$name])
}

try {
    if ($SignIn) {
        Write-Host "A Chrome window will open. Sign in to Google there within 15 minutes; the tests then continue." -ForegroundColor Yellow
    }
    Write-Host "Running live Google Photos tests (about 1-2 minutes)..." -ForegroundColor Cyan

    dotnet test GooglePhotosAutoCompressor.Tests --filter "Category=Live"
    $code = $LASTEXITCODE
}
finally {
    foreach ($name in $saved.Keys) {
        [Environment]::SetEnvironmentVariable($name, $saved[$name])
    }
}

$screens = Join-Path ([IO.Path]::GetTempPath()) "gpac-live-screens"
Write-Host "Screenshots: $screens"

if ($code -ne 0) {
    Write-Host "Some live tests failed. Each failure says what Claude Code saw on the page instead." -ForegroundColor Red
    exit $code
}
Write-Host "Live tests OK. Skipped tests (if any) say above what they need." -ForegroundColor Green
