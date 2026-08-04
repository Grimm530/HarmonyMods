# InfluxDB Setup Script for Rust Server Metrics
# This script helps set up the InfluxDB database and user

param(
    [string]$InfluxDBPath = "C:\InfluxDB",
    [string]$DatabaseName = "db01",
    [string]$Username = "grimm530",
    [string]$Password = "!APsMb42sgXSnbt",
    [string]$RetentionPolicyName = "12weeks",
    [int]$RetentionDuration = 12,  # weeks
    [int]$ShardDuration = 24        # hours
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "InfluxDB Setup for Rust Server Metrics" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if InfluxDB executable exists
$influxExe = Join-Path $InfluxDBPath "influx.exe"
if (-not (Test-Path $influxExe)) {
    Write-Host "ERROR: influx.exe not found at: $influxExe" -ForegroundColor Red
    Write-Host "Please update the -InfluxDBPath parameter to point to your InfluxDB installation." -ForegroundColor Yellow
    exit 1
}

Write-Host "InfluxDB path: $InfluxDBPath" -ForegroundColor Green
Write-Host "Database name: $DatabaseName" -ForegroundColor Green
Write-Host "Username: $Username" -ForegroundColor Green
Write-Host ""

# Check if InfluxDB is running
Write-Host "Checking if InfluxDB is running..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "http://localhost:8086/debug/vars" -TimeoutSec 5 -ErrorAction Stop
    Write-Host "✓ InfluxDB is running" -ForegroundColor Green
} catch {
    Write-Host "✗ InfluxDB is not running or not accessible at http://localhost:8086" -ForegroundColor Red
    Write-Host "Please start InfluxDB service first:" -ForegroundColor Yellow
    Write-Host "  net start influxdb" -ForegroundColor White
    exit 1
}

Write-Host ""
Write-Host "Setting up InfluxDB..." -ForegroundColor Yellow
Write-Host ""

Write-Host "Executing InfluxDB setup commands..." -ForegroundColor Yellow
Write-Host ""

# Execute commands separately since InfluxDB CLI doesn't support USE in batch mode
# First, create user (this may fail if user exists, which is okay)
$tempFile = [System.IO.Path]::GetTempFileName()
$allOutput = @()
$allErrors = @()

Write-Host "  Creating user..." -ForegroundColor Gray
$process = Start-Process -FilePath $influxExe -ArgumentList "-execute", "`"CREATE USER $Username WITH PASSWORD '$Password' WITH ALL PRIVILEGES`"" -NoNewWindow -Wait -PassThru -RedirectStandardOutput "$tempFile.out" -RedirectStandardError "$tempFile.err"

if (Test-Path "$tempFile.out") {
    $output = Get-Content "$tempFile.out" -ErrorAction SilentlyContinue
    if ($output) { $allOutput += $output }
}
if (Test-Path "$tempFile.err") {
    $errors = Get-Content "$tempFile.err" -ErrorAction SilentlyContinue
    # Ignore "already exists" errors
    $errors | Where-Object { $_ -notmatch "already exists" } | ForEach-Object { $allErrors += $_ }
}

# Create database (using username/password for auth)
Write-Host "  Creating database..." -ForegroundColor Gray
$process = Start-Process -FilePath $influxExe -ArgumentList "-username", $Username, "-password", $Password, "-execute", "`"CREATE DATABASE $DatabaseName`"" -NoNewWindow -Wait -PassThru -RedirectStandardOutput "$tempFile.out" -RedirectStandardError "$tempFile.err"

if (Test-Path "$tempFile.out") {
    $output = Get-Content "$tempFile.out" -ErrorAction SilentlyContinue
    if ($output) { $allOutput += $output }
}
if (Test-Path "$tempFile.err") {
    $errors = Get-Content "$tempFile.err" -ErrorAction SilentlyContinue
    $errors | Where-Object { $_ -notmatch "already exists" } | ForEach-Object { $allErrors += $_ }
}

# Create retention policy
Write-Host "  Creating retention policy..." -ForegroundColor Gray
$rpCmd = "CREATE RETENTION POLICY $RetentionPolicyName ON $DatabaseName DURATION ${RetentionDuration}w REPLICATION 1 SHARD DURATION ${ShardDuration}h DEFAULT"
$process = Start-Process -FilePath $influxExe -ArgumentList "-username", $Username, "-password", $Password, "-database", $DatabaseName, "-execute", $rpCmd -NoNewWindow -Wait -PassThru -RedirectStandardOutput "$tempFile.out" -RedirectStandardError "$tempFile.err"

if (Test-Path "$tempFile.out") {
    $output = Get-Content "$tempFile.out" -ErrorAction SilentlyContinue
    if ($output) { $allOutput += $output }
}
if (Test-Path "$tempFile.err") {
    $errors = Get-Content "$tempFile.err" -ErrorAction SilentlyContinue
    $errors | Where-Object { $_ -notmatch "already exists" } | ForEach-Object { $allErrors += $_ }
}

# Grant permissions
Write-Host "  Granting permissions..." -ForegroundColor Gray
$process = Start-Process -FilePath $influxExe -ArgumentList "-username", $Username, "-password", $Password, "-execute", "`"GRANT ALL ON $DatabaseName TO $Username`"" -NoNewWindow -Wait -PassThru -RedirectStandardOutput "$tempFile.out" -RedirectStandardError "$tempFile.err"

if (Test-Path "$tempFile.out") {
    $output = Get-Content "$tempFile.out" -ErrorAction SilentlyContinue
    if ($output) { $allOutput += $output }
}
if (Test-Path "$tempFile.err") {
    $errors = Get-Content "$tempFile.err" -ErrorAction SilentlyContinue
    if ($errors) { $allErrors += $errors }
}

# Show databases and users
Write-Host "  Checking databases..." -ForegroundColor Gray
$process = Start-Process -FilePath $influxExe -ArgumentList "-username", $Username, "-password", $Password, "-execute", "`"SHOW DATABASES`"" -NoNewWindow -Wait -PassThru -RedirectStandardOutput "$tempFile.out" -RedirectStandardError "$tempFile.err"

if (Test-Path "$tempFile.out") {
    $output = Get-Content "$tempFile.out" -ErrorAction SilentlyContinue
    if ($output) { $allOutput += $output }
}

Write-Host "  Checking users..." -ForegroundColor Gray
$process = Start-Process -FilePath $influxExe -ArgumentList "-username", $Username, "-password", $Password, "-execute", "`"SHOW USERS`"" -NoNewWindow -Wait -PassThru -RedirectStandardOutput "$tempFile.out" -RedirectStandardError "$tempFile.err"

if (Test-Path "$tempFile.out") {
    $output = Get-Content "$tempFile.out" -ErrorAction SilentlyContinue
    if ($output) { $allOutput += $output }
}

# Check for errors
if ($allErrors.Count -gt 0) {
    Write-Host "Errors occurred:" -ForegroundColor Red
    $allErrors | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
}

# Show output
if ($allOutput.Count -gt 0) {
    Write-Host "Output:" -ForegroundColor Green
    $allOutput | ForEach-Object { Write-Host "  $_" -ForegroundColor White }
}

# Cleanup
Remove-Item $tempFile -ErrorAction SilentlyContinue
Remove-Item "$tempFile.out" -ErrorAction SilentlyContinue
Remove-Item "$tempFile.err" -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Setup Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Verify the database was created:" -ForegroundColor White
Write-Host "   cd $InfluxDBPath" -ForegroundColor Gray
Write-Host "   .\influx.exe" -ForegroundColor Gray
Write-Host "   SHOW DATABASES;" -ForegroundColor Gray
Write-Host ""
Write-Host "2. Configure Grafana data source:" -ForegroundColor White
Write-Host "   URL: http://localhost:8086" -ForegroundColor Gray
Write-Host "   Database: $DatabaseName" -ForegroundColor Gray
Write-Host "   User: $Username" -ForegroundColor Gray
Write-Host "   Password: $Password" -ForegroundColor Gray
Write-Host ""
Write-Host "3. Update your Rust server config:" -ForegroundColor White
Write-Host "   HarmonyData/ServerMetrics/Configuration.json" -ForegroundColor Gray
Write-Host ""

