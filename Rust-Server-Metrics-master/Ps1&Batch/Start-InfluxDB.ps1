#Requires -Version 5.1
<#
.SYNOPSIS
  Starts InfluxDB 1.x (influxd) in the foreground for a desktop-style session.
  Closing this window stops the influxd process started by this script.

  Optional environment variables:
    INFLUXD_EXE     Full path to influxd.exe
    INFLUXD_CONFIG  Full path to influxdb.conf (passed as -config)

  Desktop shortcut: right-click Start-InfluxDB.bat -> Send to -> Desktop (create shortcut)

  NOTE: Rust Server Metrics requires InfluxDB 1.8, not 2.x.
#>
$ErrorActionPreference = "Continue"

function Find-InfluxdExe {
    if ($env:INFLUXD_EXE -and (Test-Path -LiteralPath $env:INFLUXD_EXE)) {
        return (Resolve-Path -LiteralPath $env:INFLUXD_EXE).Path
    }
    $candidates = @(
        "D:\!RustServer\InfluxDB-1.8\influxd.exe"
        "C:\Program Files\InfluxData\influxdb\influxd.exe"
        "C:\Program Files (x86)\InfluxData\influxdb\influxd.exe"
    )
    foreach ($p in $candidates) {
        if (Test-Path -LiteralPath $p) { return (Resolve-Path -LiteralPath $p).Path }
    }
    return $null
}

function Find-InfluxConfig {
    param([string]$InfluxdPath)
    if ($env:INFLUXD_CONFIG -and (Test-Path -LiteralPath $env:INFLUXD_CONFIG)) {
        return (Resolve-Path -LiteralPath $env:INFLUXD_CONFIG).Path
    }
    $installDir = Split-Path $InfluxdPath -Parent
    $sameDir = Join-Path $installDir "influxdb.conf"
    if (Test-Path -LiteralPath $sameDir) { return (Resolve-Path -LiteralPath $sameDir).Path }
    $programData = Join-Path $env:ProgramData "InfluxData\influxdb\influxdb.conf"
    if (Test-Path -LiteralPath $programData) { return (Resolve-Path -LiteralPath $programData).Path }
    return $null
}

function Stop-InfluxdProcess {
    param($ProcessId)
    if (-not $ProcessId) { return }
    $proc = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($proc -and -not $proc.HasExited) {
        Write-Host "`nStopping InfluxDB (PID $ProcessId)..." -ForegroundColor Yellow
        Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
        Write-Host "InfluxDB stopped." -ForegroundColor Green
    }
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  InfluxDB 1.x Launcher (influxd)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$influxdPath = Find-InfluxdExe
if (-not $influxdPath) {
    Write-Host "ERROR: influxd.exe not found." -ForegroundColor Red
    Write-Host "Install InfluxDB 1.8 for Windows or set INFLUXD_EXE to the full path." -ForegroundColor Yellow
    Write-Host ""
    if ($Host.UI.RawUI) {
        Write-Host "Press any key to exit..."
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    }
    exit 1
}

$configPath = Find-InfluxConfig -InfluxdPath $influxdPath
$installDir = Split-Path $influxdPath -Parent

$existing = Get-Process -Name "influxd" -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "influxd is already running (PID: $($existing.Id))." -ForegroundColor Yellow
    Write-Host "HTTP API is typically http://localhost:8086" -ForegroundColor Green
    Write-Host ""
    if ($Host.UI.RawUI) {
        Write-Host "Press any key to exit..."
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    }
    exit 0
}

$null = Register-EngineEvent PowerShell.Exiting -Action {
    if ($global:InfluxdProcessId) {
        Stop-InfluxdProcess -ProcessId $global:InfluxdProcessId
    }
}

Write-Host "Starting: $influxdPath" -ForegroundColor Green
if ($configPath) {
    Write-Host "Config:   $configPath" -ForegroundColor Gray
} else {
    Write-Host "Config:   (none found; influxd default paths will be used)" -ForegroundColor Yellow
}
Write-Host "Working directory: $installDir" -ForegroundColor Gray
Write-Host ""

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $influxdPath
if ($configPath) {
    $psi.Arguments = "-config `"$configPath`""
} else {
    $psi.Arguments = ""
}
$psi.WorkingDirectory = $installDir
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true

try {
    $p = New-Object System.Diagnostics.Process
    $p.StartInfo = $psi
    $p.Start() | Out-Null
    $global:InfluxdProcessId = $p.Id
    Write-Host "InfluxDB started (PID: $($p.Id))." -ForegroundColor Green
    Write-Host "Typical URL: http://localhost:8086" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Close this window to stop InfluxDB (this session only)." -ForegroundColor Yellow
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""

    while (-not $p.HasExited) {
        Start-Sleep -Seconds 1
        try { $null = $p.Refresh() } catch { break }
    }
} catch {
    Write-Host "ERROR: Failed to start influxd." -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    if ($Host.UI.RawUI) {
        Write-Host "Press any key to exit..."
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    }
    exit 1
} finally {
    Stop-InfluxdProcess -ProcessId $global:InfluxdProcessId
}
