# Fix Grafana Database Permissions Script
# Run this script as Administrator to fix database permission issues

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Fixing Grafana Database Permissions" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if running as Administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "✗ This script must be run as Administrator!" -ForegroundColor Red
    Write-Host "Right-click PowerShell and select 'Run as Administrator', then run this script again." -ForegroundColor Yellow
    exit 1
}

$grafanaDataPath = "C:\Program Files\GrafanaLabs\grafana\data"
$dbPath = Join-Path $grafanaDataPath "grafana.db"

# Check if paths exist
if (-not (Test-Path $grafanaDataPath)) {
    Write-Host "✗ Grafana data directory not found at: $grafanaDataPath" -ForegroundColor Red
    exit 1
}

Write-Host "Fixing permissions for Grafana data directory..." -ForegroundColor Yellow
Write-Host "  Path: $grafanaDataPath" -ForegroundColor White
Write-Host ""

# Get current user
$currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name

# Fix permissions on data directory
Write-Host "Setting permissions on data directory..." -ForegroundColor Yellow
$result = icacls $grafanaDataPath /grant "${currentUser}:(F)" /T 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Data directory permissions fixed" -ForegroundColor Green
} else {
    Write-Host "✗ Failed to set permissions. Exit code: $LASTEXITCODE" -ForegroundColor Red
    Write-Host "Output: $result" -ForegroundColor Yellow
}

# Fix permissions on database file specifically
if (Test-Path $dbPath) {
    Write-Host ""
    Write-Host "Setting permissions on database file..." -ForegroundColor Yellow
    Write-Host "  Path: $dbPath" -ForegroundColor White
    $result = icacls $dbPath /grant "${currentUser}:(F)" 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✓ Database file permissions fixed" -ForegroundColor Green
    } else {
        Write-Host "✗ Failed to set database permissions. Exit code: $LASTEXITCODE" -ForegroundColor Red
        Write-Host "Output: $result" -ForegroundColor Yellow
    }
} else {
    Write-Host ""
    Write-Host "⚠ Database file not found (may be created on first run): $dbPath" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "You may need to restart Grafana for the changes to take full effect." -ForegroundColor Yellow
Write-Host ""
