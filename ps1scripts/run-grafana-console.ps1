# Run Grafana Server in Console Mode
# This runs Grafana directly (not as a service) so you can see the output

Write-Host "Starting Grafana Server in console mode..." -ForegroundColor Yellow
Write-Host ""

$grafanaPath = "C:\Program Files\GrafanaLabs\grafana\bin"
$grafanaExe = Join-Path $grafanaPath "grafana-server.exe"

# Check if Grafana executable exists
if (-not (Test-Path $grafanaExe)) {
    Write-Host "✗ Grafana executable not found at: $grafanaExe" -ForegroundColor Red
    Write-Host "Please verify your Grafana installation path." -ForegroundColor Yellow
    pause
    exit 1
}

# Change to Grafana bin directory
Set-Location $grafanaPath

Write-Host "Running Grafana from: $grafanaPath" -ForegroundColor Cyan
Write-Host "Press Ctrl+C to stop Grafana" -ForegroundColor Yellow
Write-Host ""

# Run Grafana server
try {
    & $grafanaExe
} catch {
    Write-Host ""
    Write-Host "✗ Error running Grafana: $_" -ForegroundColor Red
    pause
    exit 1
}

# Pause after Grafana exits (if it does)
Write-Host ""
Write-Host "Grafana has stopped." -ForegroundColor Yellow
pause























































