#Requires -Version 5.1
<#
.SYNOPSIS
  Starts InfluxDB 1.8 from C:\InfluxDB-1.8 (minimized window) and verifies /ping.
#>
$ErrorActionPreference = "Stop"
$influxd = "C:\InfluxDB-1.8\influxd.exe"
$conf = "C:\InfluxDB-1.8\influxdb.conf"

if (-not (Test-Path $influxd)) { throw "Missing $influxd" }
if (-not (Test-Path $conf)) { throw "Missing $conf" }

$existing = Get-Process -Name influxd -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "InfluxDB already running (PID $($existing.Id))" -ForegroundColor Yellow
} else {
    Start-Process -FilePath $influxd -ArgumentList "-config `"$conf`"" -WorkingDirectory "C:\InfluxDB-1.8" -WindowStyle Minimized
    Start-Sleep -Seconds 2
}

try {
    $r = Invoke-WebRequest -Uri "http://127.0.0.1:8086/ping" -UseBasicParsing -TimeoutSec 5
    Write-Host "InfluxDB OK  http://127.0.0.1:8086  version=$($r.Headers['X-Influxdb-Version'])" -ForegroundColor Green
} catch {
    Write-Host "InfluxDB started but /ping failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
