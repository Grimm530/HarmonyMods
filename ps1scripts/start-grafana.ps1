# Start Grafana Service Script
# Use this to manually start Grafana when needed

Write-Host "Starting Grafana service..." -ForegroundColor Yellow

try {
    Start-Service -Name Grafana
    Write-Host "✓ Grafana service started successfully" -ForegroundColor Green
    Write-Host ""
    Write-Host "Grafana should be accessible at: http://localhost:3000" -ForegroundColor Cyan
    Write-Host "Waiting a few seconds for Grafana to fully initialize..." -ForegroundColor Yellow
    Start-Sleep -Seconds 5
    
    # Check if Grafana is responding
    try {
        $health = Invoke-RestMethod -Uri "http://localhost:3000/api/health" -Method Get -TimeoutSec 5
        Write-Host "✓ Grafana is ready!" -ForegroundColor Green
    } catch {
        Write-Host "⚠ Grafana is starting but may need a few more seconds to be ready" -ForegroundColor Yellow
    }
} catch {
    Write-Host "✗ Failed to start Grafana: $_" -ForegroundColor Red
    Write-Host ""
    Write-Host "You may need to run this as Administrator if the service requires elevated privileges." -ForegroundColor Yellow
    exit 1
}























































