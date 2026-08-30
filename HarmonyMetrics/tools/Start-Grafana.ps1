#Requires -Version 5.1
<#
.SYNOPSIS
  Starts Grafana from C:\Grafana in a visible console that stays open, then opens the UI.
#>
$ErrorActionPreference = "Stop"

function Wait-ForEnter {
    Write-Host ""
    Write-Host "Press Enter to close this launcher (Grafana keeps running in its own window)..." -ForegroundColor DarkGray
    try { [void][Console]::ReadLine() } catch { Start-Sleep -Seconds 8 }
}

try {
    $homePath = "C:\Grafana"
    $exe = Join-Path $homePath "bin\grafana.exe"
    if (-not (Test-Path $exe)) { $exe = Join-Path $homePath "bin\grafana-server.exe" }
    if (-not (Test-Path $exe)) { throw "Grafana not found under C:\Grafana\bin" }

    $url = "http://127.0.0.1:3000"
    $existing = Get-Process -Name grafana,grafana-server -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($existing) {
        Write-Host "Grafana already running (PID $($existing.Id))" -ForegroundColor Yellow
    } else {
        Write-Host "Starting Grafana in a new console window..." -ForegroundColor Cyan
        $leaf = Split-Path $exe -Leaf
        $serverArgs = if ($leaf -ieq "grafana.exe") { "server -homepath `"$homePath`"" } else { "-homepath `"$homePath`"" }
        $cmdArgs = "/k title Grafana & `"$exe`" $serverArgs"
        Start-Process -FilePath "$env:SystemRoot\System32\cmd.exe" -ArgumentList $cmdArgs -WorkingDirectory $homePath
        Start-Sleep -Seconds 5
    }

    $ready = $false
    for ($i = 1; $i -le 20; $i++) {
        try {
            $null = Invoke-WebRequest -Uri "$url/api/health" -UseBasicParsing -TimeoutSec 2
            $ready = $true
            break
        } catch {
            Start-Sleep -Seconds 2
        }
    }

    if ($ready) {
        Write-Host "Grafana OK  $url  (default login admin / admin)" -ForegroundColor Green
    } else {
        Write-Host "Grafana process started but UI not ready yet. Open $url manually." -ForegroundColor Yellow
    }
    Start-Process $url
} catch {
    Write-Host "Grafana start failed: $($_.Exception.Message)" -ForegroundColor Red
    Wait-ForEnter
    exit 1
}

Wait-ForEnter
exit 0
