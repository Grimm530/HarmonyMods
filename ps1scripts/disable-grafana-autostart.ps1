# Disable Grafana Auto-Start Script
# Run this script as Administrator to change Grafana from auto-start to manual

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Disabling Grafana Auto-Start" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if running as Administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "✗ This script must be run as Administrator!" -ForegroundColor Red
    Write-Host "Right-click PowerShell and select 'Run as Administrator', then run this script again." -ForegroundColor Yellow
    exit 1
}

# Check if Grafana service exists
$service = Get-Service -Name Grafana -ErrorAction SilentlyContinue
if (-not $service) {
    Write-Host "✗ Grafana service not found." -ForegroundColor Red
    exit 1
}

Write-Host "Current Grafana service status:" -ForegroundColor Yellow
Write-Host "  Status: $($service.Status)" -ForegroundColor White
Write-Host "  StartType: $($service.StartType)" -ForegroundColor White
Write-Host ""

# Stop the service if it's running
if ($service.Status -eq 'Running') {
    Write-Host "Stopping Grafana service..." -ForegroundColor Yellow
    try {
        Stop-Service -Name Grafana -Force
        Start-Sleep -Seconds 2
        Write-Host "✓ Grafana service stopped" -ForegroundColor Green
    } catch {
        Write-Host "✗ Failed to stop service: $_" -ForegroundColor Red
        Write-Host "You may need to stop it manually from Services (services.msc)" -ForegroundColor Yellow
    }
}

# Change startup type to Manual
Write-Host ""
Write-Host "Changing startup type to Manual..." -ForegroundColor Yellow
try {
    Set-Service -Name Grafana -StartupType Manual
    Write-Host "✓ Grafana startup type changed to Manual" -ForegroundColor Green
    Write-Host ""
    Write-Host "Grafana will no longer start automatically on boot." -ForegroundColor Cyan
    Write-Host "To start it manually, run: Start-Service -Name Grafana" -ForegroundColor Yellow
    Write-Host "Or use: net start Grafana" -ForegroundColor Yellow
} catch {
    Write-Host "✗ Failed to change startup type: $_" -ForegroundColor Red
    Write-Host "Trying alternative method..." -ForegroundColor Yellow
    try {
        $result = sc.exe config Grafana start= demand
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✓ Grafana startup type changed to Manual (using sc.exe)" -ForegroundColor Green
        } else {
            Write-Host "✗ Failed: $result" -ForegroundColor Red
            exit 1
        }
    } catch {
        Write-Host "✗ Failed to change startup type: $_" -ForegroundColor Red
        exit 1
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

