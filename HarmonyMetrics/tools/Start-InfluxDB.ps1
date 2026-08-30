#Requires -Version 5.1
<#
.SYNOPSIS
  Starts InfluxDB 1.8 from C:\InfluxDB-1.8 in a visible console that stays open.
#>
$ErrorActionPreference = "Stop"

function Wait-ForEnter {
    Write-Host ""
    Write-Host "Press Enter to close this launcher (InfluxDB keeps running in its own window)..." -ForegroundColor DarkGray
    try { [void][Console]::ReadLine() } catch { Start-Sleep -Seconds 8 }
}

try {
    $influxd = "C:\InfluxDB-1.8\influxd.exe"
    $conf = "C:\InfluxDB-1.8\influxdb.conf"

    if (-not (Test-Path $influxd)) { throw "Missing $influxd" }
    if (-not (Test-Path $conf)) { throw "Missing $conf" }

    $existing = Get-Process -Name influxd -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "InfluxDB already running (PID $($existing.Id -join ', '))" -ForegroundColor Yellow
    } else {
        Write-Host "Starting InfluxDB in a new console window..." -ForegroundColor Cyan
        # cmd /k keeps the window open if influxd exits with an error so you can read it.
        $cmdArgs = "/k title InfluxDB 1.8 & `"$influxd`" -config `"$conf`""
        Start-Process -FilePath "$env:SystemRoot\System32\cmd.exe" -ArgumentList $cmdArgs -WorkingDirectory "C:\InfluxDB-1.8"
        Start-Sleep -Seconds 3
    }

    $r = Invoke-WebRequest -Uri "http://127.0.0.1:8086/ping" -UseBasicParsing -TimeoutSec 8
    Write-Host "InfluxDB OK  http://127.0.0.1:8086  version=$($r.Headers['X-Influxdb-Version'])" -ForegroundColor Green
} catch {
    Write-Host "InfluxDB start failed: $($_.Exception.Message)" -ForegroundColor Red
    Wait-ForEnter
    exit 1
}

Wait-ForEnter
exit 0
