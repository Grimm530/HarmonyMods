# Grafana Console Launcher
# Launches Grafana server and opens the web interface
# Closes Grafana when this console window is closed
# Does NOT auto-start on Windows boot
# Automatically updates dashboard to use new plugin metrics (avgRunningTime, initTime, etc.)

$ErrorActionPreference = "Continue"

# Function to update Grafana dashboard JSON with new metrics
function Update-GrafanaDashboard {
    param(
        [string]$DashboardPath
    )
    
    if (-not (Test-Path $DashboardPath)) {
        Write-Host "Dashboard file not found: $DashboardPath" -ForegroundColor Yellow
        return $false
    }
    
    Write-Host "Updating Grafana dashboard with new metrics..." -ForegroundColor Cyan
    
    try {
        # Read the dashboard JSON
        $dashboardContent = Get-Content $DashboardPath -Raw -Encoding UTF8
        $dashboard = $dashboardContent | ConvertFrom-Json
        
        $updated = $false
        
        # Find and update all panels that use hookTime
        if ($dashboard.panels) {
            foreach ($panel in $dashboard.panels) {
                if ($panel.targets) {
                    foreach ($target in $panel.targets) {
                        if ($target.select) {
                            foreach ($selectGroup in $target.select) {
                                if ($selectGroup -is [System.Array]) {
                                    $fieldItem = $null
                                    $aliasItem = $null
                                    
                                    # Find field and alias items
                                    foreach ($selectItem in $selectGroup) {
                                        if ($selectItem.type -eq "field") {
                                            $fieldItem = $selectItem
                                        }
                                        if ($selectItem.type -eq "alias") {
                                            $aliasItem = $selectItem
                                        }
                                    }
                                    
                                    # Update hookTime to avgRunningTime
                                    if ($fieldItem -and $fieldItem.params -and $fieldItem.params.Count -gt 0) {
                                        if ($fieldItem.params[0] -eq "hookTime") {
                                            $fieldItem.params[0] = "avgRunningTime"
                                            $updated = $true
                                            
                                            # Update alias if it exists
                                            if ($aliasItem -and $aliasItem.params -and $aliasItem.params.Count -gt 0) {
                                                if ($panel.title -like "*1s Average*" -or $panel.title -like "*Derivative*") {
                                                    $aliasItem.params[0] = "Runtime Rate (excl. init)"
                                                } else {
                                                    $aliasItem.params[0] = "Average Runtime (excl. init)"
                                                }
                                            }
                                            
                                            Write-Host "  Updated panel '$($panel.title)' to use avgRunningTime" -ForegroundColor Green
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        
        if ($updated) {
            # Convert back to JSON with proper formatting
            $updatedJson = $dashboard | ConvertTo-Json -Depth 100
            
            # Create backup
            $backupPath = $DashboardPath + ".backup"
            Copy-Item $DashboardPath $backupPath -Force
            Write-Host "  Created backup: $backupPath" -ForegroundColor Gray
            
            # Write updated dashboard
            $updatedJson | Set-Content $DashboardPath -Encoding UTF8 -NoNewline
            Write-Host "Dashboard updated successfully!" -ForegroundColor Green
            return $true
        } else {
            Write-Host "Dashboard already uses new metrics or no updates needed." -ForegroundColor Gray
            return $false
        }
    } catch {
        Write-Host "Error updating dashboard: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Grafana Server Launcher" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Option to skip dashboard update (set to $false to disable auto-update)
$UpdateDashboard = $true

# Update dashboard with new metrics (only if enabled and file exists)
if ($UpdateDashboard) {
    $scriptDir = Split-Path $MyInvocation.MyCommand.Path -Parent
    # Navigate from Rust-Server-Metrics-master to HarmonyMods/ps1scripts
    $harmonyModsDir = Split-Path $scriptDir -Parent
    $dashboardPath = Join-Path $harmonyModsDir "ps1scripts\Grafana-Dashboard.json"
    if (Test-Path $dashboardPath) {
        Update-GrafanaDashboard -DashboardPath $dashboardPath
        Write-Host ""
    } else {
        Write-Host "Dashboard file not found at: $dashboardPath" -ForegroundColor Yellow
        Write-Host "Skipping dashboard update..." -ForegroundColor Gray
        Write-Host ""
    }
}

$grafanaPath = "C:\Program Files\GrafanaLabs\grafana\bin\grafana-server.exe"
$grafanaUrl = "http://localhost:3000"
$grafanaProcess = $null

# Cleanup function to stop Grafana
function Stop-Grafana {
    param($ProcessId)
    if ($ProcessId) {
        $proc = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
        if ($proc -and -not $proc.HasExited) {
            Write-Host "`nStopping Grafana server..." -ForegroundColor Yellow
            Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
            Write-Host "Grafana server stopped." -ForegroundColor Green
        }
    }
}

# Register cleanup on script exit
$null = Register-EngineEvent PowerShell.Exiting -Action {
    if ($global:GrafanaProcessId) {
        Stop-Grafana -ProcessId $global:GrafanaProcessId
    }
}

# Check if Grafana executable exists
if (-not (Test-Path $grafanaPath)) {
    Write-Host "ERROR: Grafana not found at: $grafanaPath" -ForegroundColor Red
    Write-Host "Please install Grafana or update the path in this script." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Press any key to exit..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 1
}

# Check if Grafana is already running
$existingProcess = Get-Process -Name "grafana-server" -ErrorAction SilentlyContinue
if ($existingProcess) {
    Write-Host "Grafana is already running (PID: $($existingProcess.Id))" -ForegroundColor Yellow
    Write-Host "Opening Grafana web interface..." -ForegroundColor Green
    Start-Process $grafanaUrl
    Write-Host ""
    Write-Host "Grafana is available at: $grafanaUrl" -ForegroundColor Green
    Write-Host ""
    Write-Host "Press any key to exit..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 0
}

Write-Host "Starting Grafana server..." -ForegroundColor Green
Write-Host "Grafana path: $grafanaPath" -ForegroundColor Gray
Write-Host ""

# Start Grafana server
try {
    # Set Grafana home directory (parent of bin directory)
    $grafanaBinDir = Split-Path $grafanaPath -Parent
    $grafanaHomeDir = Split-Path $grafanaBinDir -Parent
    
    Write-Host "Grafana home directory: $grafanaHomeDir" -ForegroundColor Gray
    
    # Start Grafana with homepath parameter and working directory set
    $processStartInfo = New-Object System.Diagnostics.ProcessStartInfo
    $processStartInfo.FileName = $grafanaPath
    $processStartInfo.Arguments = "-homepath `"$grafanaHomeDir`""
    $processStartInfo.WorkingDirectory = $grafanaHomeDir
    $processStartInfo.UseShellExecute = $false
    $processStartInfo.CreateNoWindow = $true
    
    $grafanaProcess = New-Object System.Diagnostics.Process
    $grafanaProcess.StartInfo = $processStartInfo
    $grafanaProcess.Start() | Out-Null
    $global:GrafanaProcessId = $grafanaProcess.Id
    
    Write-Host "Grafana server started (PID: $($grafanaProcess.Id))" -ForegroundColor Green
    Write-Host ""
    
    # Wait a few seconds for Grafana to start
    Write-Host "Waiting for Grafana to initialize..." -ForegroundColor Yellow
    Start-Sleep -Seconds 5
    
    # Check if Grafana is responding
    $maxAttempts = 12
    $attempt = 0
    $grafanaReady = $false
    
    while ($attempt -lt $maxAttempts -and -not $grafanaReady) {
        try {
            $response = Invoke-WebRequest -Uri $grafanaUrl -TimeoutSec 2 -UseBasicParsing -ErrorAction Stop
            $grafanaReady = $true
        } catch {
            $attempt++
            Write-Host "  Attempt $attempt/$maxAttempts - Waiting for Grafana..." -ForegroundColor Gray
            Start-Sleep -Seconds 2
        }
    }
    
    if ($grafanaReady) {
        Write-Host ""
        Write-Host "Grafana is ready!" -ForegroundColor Green
        Write-Host "Opening Grafana web interface..." -ForegroundColor Green
        Start-Process $grafanaUrl
        Write-Host ""
        Write-Host "Grafana is available at: $grafanaUrl" -ForegroundColor Green
        Write-Host "Default login: admin / admin" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Server Metrics Configuration:" -ForegroundColor Cyan
        Write-Host "  Database: db01" -ForegroundColor Gray
        Write-Host "  Server Tag: svr1_pve" -ForegroundColor Gray
        Write-Host ""
    } else {
        Write-Host ""
        Write-Host "WARNING: Grafana may still be starting up." -ForegroundColor Yellow
        Write-Host "You can access it at: $grafanaUrl" -ForegroundColor Green
        Write-Host ""
        Start-Process $grafanaUrl
    }
    
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Grafana server is running." -ForegroundColor Green
    Write-Host "Close this window to stop Grafana server." -ForegroundColor Yellow
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    
    # Keep console open and wait for Grafana process to exit or console to close
    try {
        # Wait for Grafana process, checking periodically
        while (-not $grafanaProcess.HasExited) {
            Start-Sleep -Seconds 1
            # Refresh process info
            try {
                $null = $grafanaProcess.Refresh()
            } catch {
                # Process may have been terminated
                break
            }
        }
    } catch {
        # Console was closed or process terminated
    } finally {
        # Cleanup: Stop Grafana if it's still running
        Stop-Grafana -ProcessId $global:GrafanaProcessId
    }
    
} catch {
    Write-Host ""
    Write-Host "ERROR: Failed to start Grafana server" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    Write-Host "Press any key to exit..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 1
}
