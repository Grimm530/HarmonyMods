#Requires -Version 5.1
# Launches Start-Grafana.ps1 in a new elevated PowerShell window.
# Kept at repo root (path has no &) so "Run as administrator" shortcuts work.
#
# Uses a single -ArgumentList string so UAC/RunAs does not drop split argv (common cause of
# the elevated window closing instantly with no -NoExit / no -File applied).
$ErrorActionPreference = "Stop"
$target = Join-Path $PSScriptRoot "Ps1&Batch\Start-Grafana.ps1"
if (-not (Test-Path -LiteralPath $target)) {
    Write-Error "Not found: $target"
    exit 1
}

$targetFull = (Resolve-Path -LiteralPath $target).Path
$psExe = Join-Path $PSHOME "powershell.exe"
# Entire tail after powershell.exe — one token for CreateProcess argument parsing under RunAs
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

# Exit immediately so only the elevated Grafana window stays open (no second launcher window).
exit 0
