# Install InfluxDB as Windows Service
# This script must be run as Administrator

param(
    [string]$InfluxDBPath = "C:\influxdb-1.8.10-1"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "InfluxDB Service Installation" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check for admin privileges
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ERROR: This script must be run as Administrator!" -ForegroundColor Red
    Write-Host "Right-click PowerShell and select 'Run as Administrator'" -ForegroundColor Yellow
    exit 1
}

# Check if InfluxDB exists
if (-not (Test-Path "$InfluxDBPath\influxd.exe")) {
    Write-Host "ERROR: InfluxDB not found at: $InfluxDBPath" -ForegroundColor Red
    exit 1
}

# Check if service already exists
$existingService = Get-Service -Name "InfluxDB" -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "InfluxDB service already exists. Removing old service..." -ForegroundColor Yellow
    if ($existingService.Status -eq "Running") {
        Stop-Service -Name "InfluxDB" -Force
    }
    sc.exe delete InfluxDB | Out-Null
    Start-Sleep -Seconds 2
}

# Create the service
Write-Host "Creating InfluxDB Windows service..." -ForegroundColor Yellow
$binPath = "`"$InfluxDBPath\influxd.exe`" -config `"$InfluxDBPath\influxdb.conf`""
sc.exe create InfluxDB binPath= $binPath start= auto DisplayName= "InfluxDB" | Out-Null

if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Service created successfully" -ForegroundColor Green
    
    # Start the service
    Write-Host "Starting InfluxDB service..." -ForegroundColor Yellow
    Start-Service -Name "InfluxDB"
    Start-Sleep -Seconds 3
    
    $service = Get-Service -Name "InfluxDB"
    if ($service.Status -eq "Running") {
        Write-Host "✓ InfluxDB service is running" -ForegroundColor Green
    } else {
        Write-Host "⚠ Service created but not running. Status: $($service.Status)" -ForegroundColor Yellow
        Write-Host "You may need to start it manually: net start InfluxDB" -ForegroundColor Yellow
    }
} else {
    Write-Host "✗ Failed to create service" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Service Installation Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Service Management:" -ForegroundColor White
Write-Host "  Start:   net start InfluxDB" -ForegroundColor Gray
Write-Host "  Stop:    net stop InfluxDB" -ForegroundColor Gray
Write-Host "  Status:  Get-Service InfluxDB" -ForegroundColor Gray
Write-Host ""

