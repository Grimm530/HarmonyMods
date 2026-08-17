#Requires -Version 5.1
<#
.SYNOPSIS
  Starts Grafana from C:\Grafana (minimized) and opens http://localhost:3000
#>
$ErrorActionPreference = "Stop"
$homePath = "C:\Grafana"
$exe = Join-Path $homePath "bin\grafana.exe"
if (-not (Test-Path $exe)) { $exe = Join-Path $homePath "bin\grafana-server.exe" }
if (-not (Test-Path $exe)) { throw "Grafana not found under C:\Grafana\bin" }

$url = "http://127.0.0.1:3000"
$existing = Get-Process -Name grafana,grafana-server -ErrorAction SilentlyContinue | Select-Object -First 1
if ($existing) {
    Write-Host "Grafana already running (PID $($existing.Id))" -ForegroundColor Yellow
} else {
    $leaf = Split-Path $exe -Leaf
    $args = if ($leaf -ieq "grafana.exe") { "server -homepath `"$homePath`"" } else { "-homepath `"$homePath`"" }
    Start-Process -FilePath $exe -ArgumentList $args -WorkingDirectory $homePath -WindowStyle Minimized
    Start-Sleep -Seconds 5
}

$max = 15
for ($i = 1; $i -le $max; $i++) {
    try {
        $null = Invoke-WebRequest -Uri "$url/api/health" -UseBasicParsing -TimeoutSec 2
        Write-Host "Grafana OK  $url  (default login admin / admin)" -ForegroundColor Green
        Start-Process $url
        exit 0
    } catch {
        Start-Sleep -Seconds 2
    }
}
Write-Host "Grafana process started but UI not ready yet. Open $url manually." -ForegroundColor Yellow
Start-Process $url
