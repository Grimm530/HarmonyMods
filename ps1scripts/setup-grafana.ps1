# Grafana Setup Script for Rust Server Metrics
# This script configures Grafana data source and imports the dashboard

param(
    [string]$GrafanaUrl = "http://localhost:3000",
    [string]$GrafanaUser = "admin",
    [string]$GrafanaPassword = "admin",
    [string]$InfluxUrl = "http://localhost:8086",
    [string]$InfluxDatabase = "db01",
    [string]$InfluxUser = "grimm530",
    [string]$InfluxPassword = "!APsMb42sgXSnbt",
    [string]$DashboardPath = "res\Grafana-Dashboard.json"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Grafana Setup for Rust Server Metrics" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if Grafana is accessible
Write-Host "Checking Grafana connection..." -ForegroundColor Yellow
try {
    $health = Invoke-RestMethod -Uri "$GrafanaUrl/api/health" -Method Get
    Write-Host "✓ Grafana is accessible" -ForegroundColor Green
} catch {
    Write-Host "✗ Cannot connect to Grafana at $GrafanaUrl" -ForegroundColor Red
    Write-Host "Please ensure Grafana is running and accessible." -ForegroundColor Yellow
    exit 1
}

# Authenticate with Grafana using Basic Auth
Write-Host ""
Write-Host "Authenticating with Grafana..." -ForegroundColor Yellow
$base64Auth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${GrafanaUser}:${GrafanaPassword}"))
$headers = @{
    "Authorization" = "Basic $base64Auth"
    "Content-Type" = "application/json"
}

try {
    $user = Invoke-RestMethod -Uri "$GrafanaUrl/api/user" -Method Get -Headers $headers
    Write-Host "✓ Authentication successful (User: $($user.login))" -ForegroundColor Green
} catch {
    Write-Host "✗ Authentication failed. Please check your Grafana credentials." -ForegroundColor Red
    Write-Host "Note: If this is the first time, default credentials are admin/admin" -ForegroundColor Yellow
    Write-Host "You may need to log in to Grafana web interface first and change the password." -ForegroundColor Yellow
    exit 1
}

# Create InfluxDB data source
Write-Host ""
Write-Host "Creating InfluxDB data source..." -ForegroundColor Yellow

$dataSourceBody = @{
    name = "Rust Server Metrics"
    type = "influxdb"
    url = $InfluxUrl
    access = "proxy"
    isDefault = $true
    database = $InfluxDatabase
    user = $InfluxUser
    secureJsonData = @{
        password = $InfluxPassword
    }
    jsonData = @{
        httpMode = "GET"
        version = "InfluxQL"
    }
} | ConvertTo-Json -Depth 10

try {
    $dataSource = Invoke-RestMethod -Uri "$GrafanaUrl/api/datasources" -Method Post -Headers $headers -Body $dataSourceBody
    Write-Host "✓ Data source created (ID: $($dataSource.datasource.id))" -ForegroundColor Green
    $dataSourceId = $dataSource.datasource.id
} catch {
    # Check if data source already exists
    try {
        $existing = Invoke-RestMethod -Uri "$GrafanaUrl/api/datasources/name/Rust Server Metrics" -Method Get -Headers $headers -ErrorAction Stop
        Write-Host "✓ Data source already exists (ID: $($existing.id))" -ForegroundColor Yellow
        $dataSourceId = $existing.id
        # Update it
        $dataSourceBody = @{
            id = $existing.id
            name = "Rust Server Metrics"
            type = "influxdb"
            url = $InfluxUrl
            access = "proxy"
            isDefault = $true
            database = $InfluxDatabase
            user = $InfluxUser
            secureJsonData = @{
                password = $InfluxPassword
            }
            jsonData = @{
                httpMode = "GET"
                version = "InfluxQL"
            }
        } | ConvertTo-Json -Depth 10
        Invoke-RestMethod -Uri "$GrafanaUrl/api/datasources/$($existing.id)" -Method Put -Headers $headers -Body $dataSourceBody | Out-Null
        Write-Host "✓ Data source updated" -ForegroundColor Green
    } catch {
        Write-Host "✗ Failed to create or find data source: $_" -ForegroundColor Red
        Write-Host "Error details: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
}

# Test data source connection
Write-Host ""
Write-Host "Testing data source connection..." -ForegroundColor Yellow
try {
    $testResult = Invoke-RestMethod -Uri "$GrafanaUrl/api/datasources/$dataSourceId/health" -Method Get -Headers $headers
    if ($testResult.status -eq "OK") {
        Write-Host "✓ Data source connection successful" -ForegroundColor Green
    } else {
        Write-Host "⚠ Data source test returned: $($testResult.message)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "⚠ Could not test data source (this is okay if InfluxDB auth was just enabled)" -ForegroundColor Yellow
}

# Import dashboard
Write-Host ""
Write-Host "Importing dashboard..." -ForegroundColor Yellow

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$dashboardFullPath = Join-Path $scriptPath $DashboardPath

if (-not (Test-Path $dashboardFullPath)) {
    Write-Host "✗ Dashboard file not found at: $dashboardFullPath" -ForegroundColor Red
    exit 1
}

$dashboardJson = Get-Content $dashboardFullPath -Raw | ConvertFrom-Json

# Update dashboard to use our data source
$dashboardJson.templating.list | Where-Object { $_.type -eq "datasource" } | ForEach-Object {
    $_.current = @{
        selected = $true
        text = "Rust Server Metrics"
        value = "Rust Server Metrics"
    }
    $_.options = @(
        @{
            selected = $true
            text = "Rust Server Metrics"
            value = "Rust Server Metrics"
        }
    )
}

$importBody = @{
    dashboard = $dashboardJson
    overwrite = $true
    inputs = @(
        @{
            name = "DS_RUST_SERVER_METRICS"
            type = "datasource"
            pluginId = "influxdb"
            value = "Rust Server Metrics"
        }
    )
} | ConvertTo-Json -Depth 20

try {
    $importResult = Invoke-RestMethod -Uri "$GrafanaUrl/api/dashboards/db" -Method Post -Headers $headers -Body $importBody
    Write-Host "✓ Dashboard imported successfully!" -ForegroundColor Green
    Write-Host "  Dashboard URL: $GrafanaUrl$($importResult.url)" -ForegroundColor Cyan
} catch {
    Write-Host "✗ Failed to import dashboard: $_" -ForegroundColor Red
    Write-Host "You may need to import it manually from the Grafana web interface." -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Setup Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Grafana URL: $GrafanaUrl" -ForegroundColor White
Write-Host "Default login: admin / admin (change this on first login!)" -ForegroundColor Yellow
Write-Host ""

