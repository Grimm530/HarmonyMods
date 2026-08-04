# Diagnostic Script for Grafana Dashboard Issues
# This script checks various aspects of your Grafana setup to identify why the dashboard isn't displaying data

param(
    [string]$GrafanaUrl = "http://localhost:3000",
    [string]$GrafanaUser = "admin",
    [string]$GrafanaPassword = "",
    [string]$InfluxUrl = "http://localhost:8086",
    [string]$InfluxUser = "admin",
    [string]$InfluxPassword = "adminadmin"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Grafana Dashboard Diagnostic Tool" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$errors = @()
$warnings = @()

# Prompt for password if not provided
if ([string]::IsNullOrEmpty($GrafanaPassword)) {
    $securePassword = Read-Host "Enter Grafana password for user '$GrafanaUser'" -AsSecureString
    $GrafanaPassword = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
    )
}

# Setup authentication headers
$base64Auth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${GrafanaUser}:${GrafanaPassword}"))
$headers = @{
    "Authorization" = "Basic $base64Auth"
    "Content-Type" = "application/json"
}

# 1. Check Grafana Connection
Write-Host "[1/8] Checking Grafana connection..." -ForegroundColor Yellow
try {
    $health = Invoke-RestMethod -Uri "$GrafanaUrl/api/health" -Method Get -ErrorAction Stop
    Write-Host "  ✓ Grafana is accessible" -ForegroundColor Green
} catch {
    Write-Host "  ✗ Cannot connect to Grafana at $GrafanaUrl" -ForegroundColor Red
    Write-Host "    Error: $_" -ForegroundColor Red
    $errors += "Grafana connection failed"
}

# 2. Check Authentication
Write-Host "[2/8] Checking authentication..." -ForegroundColor Yellow
try {
    $user = Invoke-RestMethod -Uri "$GrafanaUrl/api/user" -Method Get -Headers $headers -ErrorAction Stop
    Write-Host "  ✓ Authentication successful (User: $($user.login))" -ForegroundColor Green
} catch {
    Write-Host "  ✗ Authentication failed" -ForegroundColor Red
    Write-Host "    Error: $_" -ForegroundColor Red
    $errors += "Grafana authentication failed"
    exit 1
}

# 3. Check Data Sources
Write-Host "[3/8] Checking data sources..." -ForegroundColor Yellow
try {
    $dataSources = Invoke-RestMethod -Uri "$GrafanaUrl/api/datasources" -Method Get -Headers $headers -ErrorAction Stop
    $rustMetricsDS = $dataSources | Where-Object { $_.name -eq "Rust Server Metrics" }
    
    if ($rustMetricsDS) {
        Write-Host "  ✓ Found data source: 'Rust Server Metrics'" -ForegroundColor Green
        Write-Host "    Type: $($rustMetricsDS.type)" -ForegroundColor Cyan
        Write-Host "    URL: $($rustMetricsDS.url)" -ForegroundColor Cyan
        Write-Host "    Database: $($rustMetricsDS.database)" -ForegroundColor Cyan
        Write-Host "    User: $($rustMetricsDS.user)" -ForegroundColor Cyan
        Write-Host "    Is Default: $($rustMetricsDS.isDefault)" -ForegroundColor Cyan
        
        # Test data source connection
        Write-Host "    Testing connection..." -ForegroundColor Yellow
        try {
            $testResult = Invoke-RestMethod -Uri "$GrafanaUrl/api/datasources/$($rustMetricsDS.id)/health" -Method Get -Headers $headers -ErrorAction Stop
            if ($testResult.status -eq "OK") {
                Write-Host "    ✓ Data source connection is healthy" -ForegroundColor Green
            } else {
                Write-Host "    ⚠ Data source health check returned: $($testResult.status)" -ForegroundColor Yellow
                $warnings += "Data source health check: $($testResult.status)"
            }
        } catch {
            Write-Host "    ⚠ Could not test data source health (this endpoint may not be available)" -ForegroundColor Yellow
            $warnings += "Data source health check unavailable"
        }
    } else {
        Write-Host "  ✗ Data source 'Rust Server Metrics' not found!" -ForegroundColor Red
        Write-Host "    Available data sources:" -ForegroundColor Yellow
        foreach ($ds in $dataSources) {
            Write-Host "      - $($ds.name) ($($ds.type))" -ForegroundColor Yellow
        }
        $errors += "Data source 'Rust Server Metrics' not found"
    }
} catch {
    Write-Host "  ✗ Failed to retrieve data sources" -ForegroundColor Red
    Write-Host "    Error: $_" -ForegroundColor Red
    $errors += "Failed to retrieve data sources"
}

# 4. Check Dashboards
Write-Host "[4/8] Checking dashboards..." -ForegroundColor Yellow
try {
    $dashboards = Invoke-RestMethod -Uri "$GrafanaUrl/api/search?query=&type=dash-db" -Method Get -Headers $headers -ErrorAction Stop
    $rustDashboard = $dashboards | Where-Object { $_.title -like "*Rust*" -or $_.title -like "*Server*" -or $_.title -like "*Metrics*" }
    
    if ($rustDashboard) {
        Write-Host "  ✓ Found dashboard(s):" -ForegroundColor Green
        foreach ($dash in $rustDashboard) {
            Write-Host "    - $($dash.title) (UID: $($dash.uid))" -ForegroundColor Cyan
            
            # Get dashboard details
            try {
                $dashboardDetails = Invoke-RestMethod -Uri "$GrafanaUrl/api/dashboards/uid/$($dash.uid)" -Method Get -Headers $headers -ErrorAction Stop
                $dashboard = $dashboardDetails.dashboard
                
                Write-Host "      Panels: $($dashboard.panels.Count)" -ForegroundColor Cyan
                
                # Check data source references
                $dsRefs = @()
                if ($dashboard.templating -and $dashboard.templating.list) {
                    foreach ($var in $dashboard.templating.list) {
                        if ($var.type -eq "datasource") {
                            $dsRefs += $var.current.value
                        }
                    }
                }
                
                # Check panel data sources
                function GetPanelDataSources($panels) {
                    $sources = @()
                    foreach ($panel in $panels) {
                        if ($panel.datasource -and $panel.datasource.uid) {
                            $sources += $panel.datasource.uid
                        }
                        if ($panel.datasource -and $panel.datasource.type -eq "datasource" -and $panel.datasource.uid -eq "`$datasource") {
                            $sources += "`$datasource (template variable)"
                        }
                        if ($panel.panels) {
                            $sources += GetPanelDataSources $panel.panels
                        }
                    }
                    return $sources
                }
                
                $panelDS = GetPanelDataSources $dashboard.panels
                $allDS = ($dsRefs + $panelDS) | Select-Object -Unique
                
                if ($allDS.Count -gt 0) {
                    Write-Host "      Data source references:" -ForegroundColor Cyan
                    foreach ($ds in $allDS) {
                        Write-Host "        - $ds" -ForegroundColor Cyan
                    }
                }
            } catch {
                Write-Host "      ⚠ Could not retrieve dashboard details" -ForegroundColor Yellow
            }
        }
    } else {
        Write-Host "  ⚠ No Rust/Server/Metrics dashboard found" -ForegroundColor Yellow
        Write-Host "    Available dashboards:" -ForegroundColor Yellow
        foreach ($dash in $dashboards | Select-Object -First 10) {
            Write-Host "      - $($dash.title)" -ForegroundColor Yellow
        }
        $warnings += "No Rust metrics dashboard found"
    }
} catch {
    Write-Host "  ✗ Failed to retrieve dashboards" -ForegroundColor Red
    Write-Host "    Error: $_" -ForegroundColor Red
    $errors += "Failed to retrieve dashboards"
}

# 5. Check InfluxDB Connection
Write-Host "[5/8] Checking InfluxDB connection..." -ForegroundColor Yellow
try {
    $influxAuth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${InfluxUser}:${InfluxPassword}"))
    $influxHeaders = @{
        "Authorization" = "Basic $influxAuth"
    }
    
    # Try to ping InfluxDB
    $pingResult = Invoke-WebRequest -Uri "$InfluxUrl/ping" -Method Get -Headers $influxHeaders -ErrorAction Stop
    if ($pingResult.StatusCode -eq 204) {
        Write-Host "  ✓ InfluxDB is accessible at $InfluxUrl" -ForegroundColor Green
    } else {
        Write-Host "  ⚠ InfluxDB responded with status: $($pingResult.StatusCode)" -ForegroundColor Yellow
        $warnings += "InfluxDB ping returned status $($pingResult.StatusCode)"
    }
} catch {
    Write-Host "  ✗ Cannot connect to InfluxDB at $InfluxUrl" -ForegroundColor Red
    Write-Host "    Error: $_" -ForegroundColor Red
    $warnings += "InfluxDB connection failed - check if InfluxDB is running"
}

# 6. Check InfluxDB Databases
Write-Host "[6/8] Checking InfluxDB databases..." -ForegroundColor Yellow
try {
    $influxAuth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${InfluxUser}:${InfluxPassword}"))
    $influxHeaders = @{
        "Authorization" = "Basic $influxAuth"
    }
    
    $databases = Invoke-RestMethod -Uri "$InfluxUrl/query?q=SHOW+DATABASES" -Method Get -Headers $influxHeaders -ErrorAction Stop
    
    if ($databases.results -and $databases.results[0].series) {
        $dbNames = $databases.results[0].series[0].values | ForEach-Object { $_[0] }
        Write-Host "  ✓ Found databases:" -ForegroundColor Green
        foreach ($db in $dbNames) {
            Write-Host "    - $db" -ForegroundColor Cyan
        }
        
        # Check if the database from data source exists
        if ($rustMetricsDS -and $rustMetricsDS.database) {
            if ($dbNames -contains $rustMetricsDS.database) {
                Write-Host "  ✓ Database '$($rustMetricsDS.database)' exists" -ForegroundColor Green
            } else {
                Write-Host "  ✗ Database '$($rustMetricsDS.database)' not found in InfluxDB!" -ForegroundColor Red
                $errors += "Database '$($rustMetricsDS.database)' not found in InfluxDB"
            }
        }
    }
} catch {
    Write-Host "  ⚠ Could not query InfluxDB databases" -ForegroundColor Yellow
    Write-Host "    Error: $_" -ForegroundColor Yellow
    $warnings += "Could not query InfluxDB databases"
}

# 7. Check for Data in InfluxDB
Write-Host "[7/8] Checking for data in InfluxDB..." -ForegroundColor Yellow
if ($rustMetricsDS -and $rustMetricsDS.database) {
    try {
        $influxAuth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${InfluxUser}:${InfluxPassword}"))
        $influxHeaders = @{
            "Authorization" = "Basic $influxAuth"
        }
        
        # Check measurements
        $measurementsQuery = "SHOW MEASUREMENTS"
        $measurementsUrl = "$InfluxUrl/query?db=$($rustMetricsDS.database)&q=$measurementsQuery"
        $measurements = Invoke-RestMethod -Uri $measurementsUrl -Method Get -Headers $influxHeaders -ErrorAction Stop
        
        if ($measurements.results -and $measurements.results[0].series) {
            $measurementNames = $measurements.results[0].series[0].values | ForEach-Object { $_[0] }
            Write-Host "  ✓ Found measurements:" -ForegroundColor Green
            foreach ($m in $measurementNames) {
                Write-Host "    - $m" -ForegroundColor Cyan
            }
            
            if ($measurementNames.Count -eq 0) {
                Write-Host "  ⚠ No measurements found in database!" -ForegroundColor Yellow
                $warnings += "No measurements found in InfluxDB - data may not be being written"
            } else {
                # Check for recent data in a few key measurements
                $keyMeasurements = @("framerate", "players", "memory", "network")
                foreach ($measurement in $keyMeasurements) {
                    if ($measurementNames -contains $measurement) {
                        $dataQuery = "SELECT * FROM `"$measurement`" ORDER BY time DESC LIMIT 1"
                        $dataUrl = "$InfluxUrl/query?db=$($rustMetricsDS.database)&q=$dataQuery"
                        try {
                            $data = Invoke-RestMethod -Uri $dataUrl -Method Get -Headers $influxHeaders -ErrorAction Stop
                            if ($data.results -and $data.results[0].series) {
                                $latestTime = $data.results[0].series[0].values[0][0]
                                Write-Host "    Latest $measurement data: $latestTime" -ForegroundColor Cyan
                            } else {
                                Write-Host "    ⚠ No data in $measurement" -ForegroundColor Yellow
                            }
                        } catch {
                            Write-Host "    ⚠ Could not query $measurement" -ForegroundColor Yellow
                        }
                    }
                }
            }
        } else {
            Write-Host "  ⚠ No measurements found in database" -ForegroundColor Yellow
            $warnings += "No measurements found in InfluxDB"
        }
    } catch {
        Write-Host "  ⚠ Could not query measurements" -ForegroundColor Yellow
        Write-Host "    Error: $_" -ForegroundColor Yellow
        $warnings += "Could not query InfluxDB measurements"
    }
} else {
    Write-Host "  ⚠ Skipping data check (data source not found)" -ForegroundColor Yellow
}

# 8. Check Configuration File
Write-Host "[8/8] Checking Rust Server Metrics configuration..." -ForegroundColor Yellow
$configPath = "HarmonyData\ServerMetrics\Configuration.json"
if (Test-Path $configPath) {
    try {
        $config = Get-Content $configPath -Raw | ConvertFrom-Json
        Write-Host "  ✓ Configuration file found" -ForegroundColor Green
        Write-Host "    Enabled: $($config.Enabled)" -ForegroundColor Cyan
        Write-Host "    Database URL: $($config.'Influx Database Url')" -ForegroundColor Cyan
        Write-Host "    Database Name: $($config.'Influx Database Name')" -ForegroundColor Cyan
        Write-Host "    Server Tag: $($config.'Server Tag')" -ForegroundColor Cyan
        
        if (-not $config.Enabled) {
            Write-Host "  ⚠ Metrics gathering is DISABLED in configuration!" -ForegroundColor Yellow
            $warnings += "Metrics gathering is disabled in configuration"
        }
        
        if ($config.'Influx Database Name' -like "*CHANGEME*" -or $config.'Influx Database Name' -like "*example*") {
            Write-Host "  ⚠ Database name appears to be using default/example value!" -ForegroundColor Yellow
            $warnings += "Database name may not be configured correctly"
        }
        
        if ($config.'Influx Database Url' -like "*example*") {
            Write-Host "  ⚠ Database URL appears to be using default/example value!" -ForegroundColor Yellow
            $warnings += "Database URL may not be configured correctly"
        }
    } catch {
        Write-Host "  ⚠ Could not read configuration file" -ForegroundColor Yellow
        $warnings += "Could not read configuration file"
    }
} else {
    Write-Host "  ⚠ Configuration file not found at: $configPath" -ForegroundColor Yellow
    $warnings += "Configuration file not found"
}

# Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Diagnostic Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

if ($errors.Count -eq 0 -and $warnings.Count -eq 0) {
    Write-Host "✓ All checks passed! Your setup looks good." -ForegroundColor Green
} else {
    if ($errors.Count -gt 0) {
        Write-Host "✗ ERRORS FOUND:" -ForegroundColor Red
        foreach ($error in $errors) {
            Write-Host "  - $error" -ForegroundColor Red
        }
        Write-Host ""
    }
    
    if ($warnings.Count -gt 0) {
        Write-Host "⚠ WARNINGS:" -ForegroundColor Yellow
        foreach ($warning in $warnings) {
            Write-Host "  - $warning" -ForegroundColor Yellow
        }
    }
}

Write-Host ""
Write-Host "Common Issues and Solutions:" -ForegroundColor Cyan
Write-Host "  1. No data in InfluxDB:" -ForegroundColor Yellow
Write-Host "     - Check if Rust server is running and metrics are enabled" -ForegroundColor White
Write-Host "     - Verify Configuration.json has Enabled=true" -ForegroundColor White
Write-Host "     - Check server logs for metrics errors" -ForegroundColor White
Write-Host ""
Write-Host "  2. Data source not found:" -ForegroundColor Yellow
Write-Host "     - Run setup-grafana.ps1 to create the data source" -ForegroundColor White
Write-Host "     - Or manually create it in Grafana UI" -ForegroundColor White
Write-Host ""
Write-Host "  3. Data source connection failed:" -ForegroundColor Yellow
Write-Host "     - Verify InfluxDB is running (check with: Get-Service InfluxDB)" -ForegroundColor White
Write-Host "     - Check InfluxDB credentials match in Grafana data source" -ForegroundColor White
Write-Host "     - Verify database name matches in both Grafana and Configuration.json" -ForegroundColor White
Write-Host ""
Write-Host "  4. Dashboard not displaying data:" -ForegroundColor Yellow
Write-Host "     - Check time range in dashboard (try 'Last 1 hour' or 'Last 6 hours')" -ForegroundColor White
Write-Host "     - Verify data source variable is set correctly in dashboard" -ForegroundColor White
Write-Host "     - Check if measurements exist and have recent data" -ForegroundColor White
Write-Host ""

