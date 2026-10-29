<#
.SYNOPSIS
    Builds, tests and packages Google Photos Auto Compressor. Everything it makes goes to dist/:
      GooglePhotosAutoCompressor-v<ver>-win-x64.exe                portable Windows app (one file)
      GooglePhotosAutoCompressor-v<ver>-x86_64.AppImage.tar.gz     any Linux: unpack, double-click
      google-photos-auto-compressor_v<ver>_amd64.deb               Ubuntu, Debian, Mint

    Prints one line per step. The full output of each step is in dist/logs/; when a step fails,
    the end of its log is shown. Live Google Photos tests are not run here (integration_tests.ps1).

    Linux packages: on Windows the app is built here and packed in WSL (Ubuntu, with dpkg-deb);
    on Linux they are packed directly. Run on Linux with PowerShell 7: pwsh ./build.ps1

.EXAMPLE
    ./build.ps1                    # unit tests, Windows exe, Linux packages
    ./build.ps1 test               # only build and run the unit tests (quick check)
    ./build.ps1 win                # unit tests, then only the Windows exe
    ./build.ps1 linux -SkipTests   # only the Linux packages
    ./build.ps1 -Version v1.2.0    # default: <Version> of the app project
#>
# Only the targets are positional: "./build.ps1 test" must not set -Version.
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Targets,
    [string]$Version,
    [switch]$SkipTests,
    # WSL distribution that packs the Linux packages on Windows.
    [string]$WslDistro = "Ubuntu-24.04"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$onWindows = $env:OS -eq "Windows_NT"
$onLinux = (-not $onWindows) -and $IsLinux
$dist = Join-Path $root "dist"
$logs = Join-Path $dist "logs"
$failed = $false
$built = @()
$interactive = -not [Console]::IsOutputRedirected

function Finish([bool]$ok) {
    Pop-Location
    Write-Host ""
    if ($ok) { Write-Host "All done." -ForegroundColor Green } else { Write-Host "Something failed - see above." -ForegroundColor Red }
    # Started by double-click / "Run with PowerShell": keep the window open.
    if ($onWindows) {
        try {
            $parentId = (Get-CimInstance Win32_Process -Filter "ProcessId=$PID" -ErrorAction Stop).ParentProcessId
            if ((Get-Process -Id $parentId -ErrorAction Stop).ProcessName -eq "explorer") { Read-Host "Press Enter to close" | Out-Null }
        }
        catch { }
    }
    if ($ok) { exit 0 } else { exit 1 }
}

function Elapsed([datetime]$start) {
    $t = (Get-Date) - $start
    return "{0}:{1:00}" -f [int][math]::Floor($t.TotalMinutes), $t.Seconds
}

# Runs a program with all its output in dist/logs/<log>.log. While it runs, one status line counts
# the time. Returns $true when it exits with 0; otherwise shows the end of the log.
function Invoke-Logged([string]$label, [string]$file, [string]$arguments, [string]$log) {
    $logPath = Join-Path $logs "$log.log"
    $errPath = Join-Path $logs "$log.err.log"
    $start = Get-Date
    $proc = Start-Process -FilePath $file -ArgumentList $arguments -NoNewWindow -PassThru `
        -RedirectStandardOutput $logPath -RedirectStandardError $errPath -WorkingDirectory $root
    $null = $proc.Handle   # keeps the exit code readable after the process ends
    while (-not $proc.WaitForExit(1000)) {
        if ($interactive) { Write-Host -NoNewline ("`r  {0,-16} running {1}   " -f $label, (Elapsed $start)) }
    }
    $proc.WaitForExit()
    # One log per step: stderr goes after stdout.
    if ((Test-Path $errPath) -and (Get-Item $errPath).Length -gt 0) { Get-Content $errPath | Add-Content $logPath }
    Remove-Item $errPath -ErrorAction SilentlyContinue
    $script:stepTime = Elapsed $start
    if ($interactive) { Write-Host -NoNewline ("`r{0}`r" -f (" " * 60)) }
    if ($proc.ExitCode -eq 0) { return $true }

    Write-Host ("  {0,-16} FAILED after {1}" -f $label, $script:stepTime) -ForegroundColor Red
    Get-Content $logPath -Tail 25 | ForEach-Object { Write-Host "  | $_" }
    Write-Host "  Full log: $logPath"
    return $false
}

function Done([string]$label, [string]$detail) {
    Write-Host ("  {0,-16} OK  {1} ({2})" -f $label, $detail, $script:stepTime) -ForegroundColor Green
}

Push-Location $root

# ---------- what to build ----------
$doTest = -not $SkipTests; $doWin = $false; $doLinux = $false
foreach ($t in $Targets) {
    switch ($t.ToLowerInvariant()) {
        "test" { $doTest = $true }
        "win" { $doWin = $true }
        "linux" { $doLinux = $true }
        default { Write-Host "Unknown target '$t' (use test, win, linux)." -ForegroundColor Red; Finish $false }
    }
}
$onlyTest = ($Targets -contains "test") -and -not ($doWin -or $doLinux)
if (-not $onlyTest -and -not ($doWin -or $doLinux)) { $doWin = $true; $doLinux = $true }

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "The .NET 8 SDK is not installed. Get it from https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Red
    Finish $false
}

# Names use "v1.2.0"; the number inside the exe and the .deb must be digits only ("1.2.0").
if (-not $Version) {
    $csproj = Get-Content (Join-Path $root "GooglePhotosAutoCompressor.App/GooglePhotosAutoCompressor.App.csproj") -Raw
    $Version = [regex]::Match($csproj, "<Version>([^<]+)</Version>").Groups[1].Value
}
$number = $Version.TrimStart("v", "V")
$Version = "v$number"

New-Item -ItemType Directory -Force $dist, $logs | Out-Null
Write-Host "Google Photos Auto Compressor $Version" -ForegroundColor Cyan

# ---------- unit and UI tests ----------
if ($doTest) {
    $ok = Invoke-Logged "Build" "dotnet" "build GooglePhotosAutoCompressor.sln -v quiet -nologo" "build"
    if ($ok) { Done "Build" "compiled" }
    else { Finish $false }

    # Counts come from the .trx result files (one per test project): exact, unlike the console log.
    $trxDir = Join-Path $logs "test-results"
    Remove-Item $trxDir -Recurse -Force -ErrorAction SilentlyContinue
    $ok = Invoke-Logged "Tests" "dotnet" "test GooglePhotosAutoCompressor.sln --no-build -v quiet --nologo --filter Category!=Live --logger trx --results-directory `"$trxDir`"" "tests"
    $passed = 0; $skipped = 0
    foreach ($trx in Get-ChildItem $trxDir -Filter *.trx -ErrorAction SilentlyContinue) {
        $c = ([xml](Get-Content $trx.FullName -Raw)).TestRun.ResultSummary.Counters
        $passed += [int]$c.passed; $skipped += [int]$c.total - [int]$c.executed
    }
    if ($ok) { Done "Tests" "$passed passed$(if ($skipped) { ", $skipped skipped" })" }
    else { Finish $false }
}

# ---------- Windows: one portable exe ----------
if ($doWin) {
    $target = Join-Path $dist "GooglePhotosAutoCompressor-$Version-win-x64.exe"
    $ffmpeg = Join-Path $root "GooglePhotosAutoCompressor/tools/ffmpeg.exe"
    if (-not (Test-Path $ffmpeg) -or (Get-Item $ffmpeg).Length -lt 1MB) {
        Write-Host "  Note: tools/ffmpeg.exe is missing or a Git LFS pointer ('git lfs pull'); the exe will offer to download ffmpeg." -ForegroundColor Yellow
    }
    if ($onWindows -and (Get-Process | Where-Object { $_.Path -eq $target })) {
        Write-Host "  The exe in dist/ is running: close it, then run this again." -ForegroundColor Red
        Finish $false
    }

    $out = Join-Path $dist "publish-win-x64"
    Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
    $winStart = Get-Date
    $ok = Invoke-Logged "Windows exe" "dotnet" ("publish GooglePhotosAutoCompressor.App -c Release -r win-x64 --self-contained " +
        "-p:Version=$number -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true " +
        "-p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none " +
        "-v quiet -nologo -o `"$out`"") "windows"
    if ($ok) {
        Move-Item (Join-Path $out "GooglePhotosAutoCompressor.exe") $target -Force
        Remove-Item $out -Recurse -Force
        if ($onWindows) {
            # The setup check starts the exe itself: ffmpeg, photos, folders, browser (no Google account).
            $ok = Invoke-Logged "Windows exe" $target "--check" "windows-check"
        }
    }
    $script:stepTime = Elapsed $winStart   # publish and setup check together
    if ($ok) { Done "Windows exe" $(if ($onWindows) { "setup check passed" } else { "not started (not on Windows)" }); $built += $target }
    else { $failed = $true }
}

# ---------- Linux: AppImage .tar.gz and .deb ----------
if ($doLinux) {
    $tar = Join-Path $dist "GooglePhotosAutoCompressor-$Version-x86_64.AppImage.tar.gz"
    $deb = Join-Path $dist "google-photos-auto-compressor_${Version}_amd64.deb"
    $out = "dist/publish-linux-x64"
    $canPack = $true
    if ($onWindows) {
        # wsl -l prints UTF-16; strip the NULs to compare names.
        $distros = @()
        if (Get-Command wsl.exe -ErrorAction SilentlyContinue) { $distros = @(wsl.exe -l -q | ForEach-Object { ($_ -replace "`0", "").Trim() } | Where-Object { $_ }) }
        if ($distros -notcontains $WslDistro) {
            Write-Host "  Linux packages   SKIPPED: WSL '$WslDistro' is not installed (wsl --install -d $WslDistro, or -WslDistro <name>)." -ForegroundColor Red
            Write-Host "                   The Packages workflow on GitHub also builds them." -ForegroundColor Red
            $canPack = $false; $failed = $true
        }
    }
    elseif (-not $onLinux) {
        Write-Host "  Linux packages   SKIPPED: build them on Linux or Windows (with WSL)." -ForegroundColor Red
        $canPack = $false; $failed = $true
    }

    if ($canPack) {
        Remove-Item (Join-Path $root $out) -Recurse -Force -ErrorAction SilentlyContinue
        $ok = Invoke-Logged "Linux app" "dotnet" ("publish GooglePhotosAutoCompressor.App -c Release -r linux-x64 --self-contained " +
            "-p:Version=$number -p:DebugType=none -v quiet -nologo -o `"$out`"") "linux-publish"
        if ($ok) {
            Done "Linux app" "built"
            $script = "packaging/linux/build-packages.sh $Version x64 $out"
            if ($onWindows) { $ok = Invoke-Logged "Linux packages" "wsl.exe" "-d $WslDistro --cd `"$root`" -- bash $script" "linux-packages-wsl" }
            else { $ok = Invoke-Logged "Linux packages" "bash" $script "linux-packages-run" }
        }
        Remove-Item (Join-Path $root $out) -Recurse -Force -ErrorAction SilentlyContinue
        if ($ok) { Done "Linux packages" "AppImage and .deb"; $built += $tar, $deb }
        else { $failed = $true }
    }
}

# ---------- what was made ----------
if ($built.Count -gt 0) {
    Write-Host ""
    Write-Host "Built now (dist/):"
    foreach ($path in $built) {
        $f = Get-Item -LiteralPath $path -ErrorAction SilentlyContinue
        if ($f) { Write-Host ("  {0,6:0} MB  {1}" -f ($f.Length / 1MB), $f.Name) }
        else { Write-Host "  missing: $(Split-Path $path -Leaf)" -ForegroundColor Red; $failed = $true }
    }
}

Finish (-not $failed)
