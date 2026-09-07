#Requires -Version 5.1
# Launches Start-InfluxDB.ps1 in a new elevated PowerShell window.
# Kept at repo root (path has no &) so "Run as administrator" shortcuts work.
#
# Single -ArgumentList string — avoids RunAs dropping split argv (instant-close window).
$ErrorActionPreference = "Stop"
$target = Join-Path $PSScriptRoot "Ps1&Batch\Start-InfluxDB.ps1"
if (-not (Test-Path -LiteralPath $target)) {
    Write-Error "Not found: $target"
    exit 1
}

$targetFull = (Resolve-Path -LiteralPath $target).Path
$psExe = Join-Path $PSHOME "powershell.exe"
$argLine = "-NoExit -NoLogo -NoProfile -ExecutionPolicy Bypass -File `"$targetFull`""

try {
    Start-Process -FilePath $psExe -Verb RunAs -ArgumentList $argLine -ErrorAction Stop
} catch {
    Write-Host "Elevation failed or was cancelled: $($_.Exception.Message)" -ForegroundColor Red
    if ($Host.UI.RawUI) {
        Write-Host "Press any key to exit..."
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    }
    exit 1
}

exit 0
