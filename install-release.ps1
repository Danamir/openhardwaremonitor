<#
.SYNOPSIS
  Builds the Release version and installs it into the install folder.

.DESCRIPTION
  Refuses to run while the monitor is running from the install folder.
  Writes to Program Files: when not elevated, it relaunches itself through a UAC
  prompt, in a window that waits for a key at the end.
  The settings and history next to the installed exe (OpenHardwareMonitor.config,
  .settings) are not part of the build output, so they are kept.

.PARAMETER InstallDir
  Install folder. Default: C:\Program Files\OpenHardwareMonitor

.PARAMETER CopyDebugConfig
  Replace the installed config with the one of the Debug build
  (OpenHardwareMonitor\bin\Debug). The current one is backed up first.

.PARAMETER Start
  Start the installed monitor at the end.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\install-release.ps1 -Start
  Or right click > Run with PowerShell.
#>
param(
    [string]$InstallDir = "C:\Program Files\OpenHardwareMonitor",
    [switch]$CopyDebugConfig,
    [switch]$Start,
    # internal: set on the elevated relaunch, to keep its window open at the end
    [switch]$Elevated
)

# Elevate first: without elevation, the path of the (elevated) running monitor
# can't be read, so the running check below wouldn't see it
$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = @("-NoProfile", "-ExecutionPolicy", "Bypass",
        "-File", "`"$PSCommandPath`"", "-Elevated", "-InstallDir", "`"$InstallDir`"")
    if ($CopyDebugConfig) { $arguments += "-CopyDebugConfig" }
    if ($Start) { $arguments += "-Start" }
    try {
        $process = Start-Process powershell -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    } catch {
        Write-Host "Elevation cancelled, nothing installed." -ForegroundColor Red
        exit 1
    }
    exit $process.ExitCode
}

$ErrorActionPreference = "Stop"
$repo = $PSScriptRoot
$project = Join-Path $repo "OpenHardwareMonitor\OpenHardwareMonitor.csproj"
$releaseDir = Join-Path $repo "OpenHardwareMonitor\bin\Release"
$debugConfig = Join-Path $repo "OpenHardwareMonitor\bin\Debug\OpenHardwareMonitor.config"
$installedExe = Join-Path $InstallDir "OpenHardwareMonitor.exe"
$installedConfig = Join-Path $InstallDir "OpenHardwareMonitor.config"

function Done([int]$exitCode) {
    if ($Elevated) {
        Write-Host "Press a key to close this window."
        [void][Console]::ReadKey($true)
    }
    exit $exitCode
}

function Fail([string]$message) {
    Write-Host $message -ForegroundColor Red
    Done 1
}

trap {
    Fail $_
}

# Gate: the installed monitor must not be running (its files are locked, and
# it would overwrite the config when closing)
$running = Get-Process OpenHardwareMonitor -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and ($_.Path -ieq $installedExe) }
if ($running) {
    Fail ("The monitor is running from the install folder (PID {0}). Close it (tray icon > Exit) and run again." -f
        (($running | ForEach-Object Id) -join ", "))
}

if ($CopyDebugConfig) {
    if (-not (Test-Path $debugConfig)) {
        Fail "No Debug config to copy: $debugConfig"
    }
    # the Debug build saves its config when closing: copy it only once closed
    $debugExe = Join-Path (Split-Path $debugConfig) "OpenHardwareMonitor.exe"
    $debugRunning = Get-Process OpenHardwareMonitor -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and ($_.Path -ieq $debugExe) }
    if ($debugRunning) {
        Fail ("The Debug build is running (PID {0}): close it first, so its config is up to date." -f
            (($debugRunning | ForEach-Object Id) -join ", "))
    }
}

Write-Host "Building Release..." -ForegroundColor Cyan
# no MSBuild nodes or compiler server left running: they would keep the
# console window open after the script ends
dotnet build $project -c Release --nologo -v quiet --disable-build-servers
if ($LASTEXITCODE -ne 0) {
    Fail "Build failed, nothing installed."
}

Write-Host "Installing into $InstallDir..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force $InstallDir | Out-Null
# copy only, no /MIR: the files of the install folder that aren't in the build
# output (config, settings, backups) stay untouched
robocopy $releaseDir $InstallDir /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) {
    Fail "Copy failed (robocopy exit code $LASTEXITCODE)."
}

if ($CopyDebugConfig) {
    if (Test-Path $installedConfig) {
        $backup = "$installedConfig.{0:yyyyMMdd-HHmmss}" -f (Get-Date)
        Copy-Item $installedConfig $backup
        Write-Host "Previous config backed up to $backup"
    }
    Copy-Item $debugConfig $installedConfig -Force
    Write-Host "Debug config copied."
}

$version = (Get-Item $installedExe).VersionInfo.ProductVersion
Write-Host "Installed version $version." -ForegroundColor Green

if ($Start) {
    Start-Process -FilePath $installedExe -WorkingDirectory $InstallDir
}
# robocopy's success codes (1-7) would otherwise be the script exit code
Done 0
