# Performance Log Generator for Rust Server
# This script monitors server performance and generates a detailed log for analysis

param(
    [int]$DurationMinutes = 10,           # How long to monitor (default 10 minutes)
    [int]$SampleIntervalSeconds = 5,      # How often to sample (default 5 seconds)
    [string]$OutputPath = "",             # Output file path (default: auto-generated)
    [string]$ServerProcessName = "RustDedicated",  # Process name to monitor
    [string]$OxideLogPath = "..\..\oxide\logs"    # Path to Oxide logs
)

$ErrorActionPreference = "Continue"

# Generate output filename if not provided
if ([string]::IsNullOrEmpty($OutputPath)) {
    $timestamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $OutputPath = Join-Path $scriptDir "performance-log_$timestamp.txt"
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Rust Server Performance Log Generator" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Duration: $DurationMinutes minutes" -ForegroundColor Green
Write-Host "Sample Interval: $SampleIntervalSeconds seconds" -ForegroundColor Green
Write-Host "Output File: $OutputPath" -ForegroundColor Green
Write-Host ""

# Find Rust server process
Write-Host "Searching for Rust server process..." -ForegroundColor Yellow
$serverProcess = Get-Process -Name $ServerProcessName -ErrorAction SilentlyContinue

if (-not $serverProcess) {
    Write-Host "✗ Rust server process '$ServerProcessName' not found!" -ForegroundColor Red
    Write-Host "Please ensure the Rust server is running." -ForegroundColor Yellow
    exit 1
}

Write-Host "✓ Found Rust server process (PID: $($serverProcess.Id))" -ForegroundColor Green
Write-Host ""

# Get system info
$totalRAM = (Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB
$cpuCores = (Get-CimInstance Win32_ComputerSystem).NumberOfLogicalProcessors

# Initialize performance counters
$cpuCounter = New-Object System.Diagnostics.PerformanceCounter("Process", "% Processor Time", $ServerProcessName)
$cpuCounter.NextValue() | Out-Null  # First call always returns 0
$ramCounter = New-Object System.Diagnostics.PerformanceCounter("Process", "Working Set - Private", $ServerProcessName)

# Initialize data collection
$samples = @()
$startTime = Get-Date
$endTime = $startTime.AddMinutes($DurationMinutes)
$sampleCount = 0

Write-Host "Starting performance monitoring..." -ForegroundColor Yellow
Write-Host "Press Ctrl+C to stop early" -ForegroundColor Gray
Write-Host ""

# Create log file header
$logHeader = @"
========================================
Rust Server Performance Log
Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Duration: $DurationMinutes minutes
Sample Interval: $SampleIntervalSeconds seconds
========================================

System Information:
- Total RAM: $([math]::Round($totalRAM, 2)) GB
- CPU Cores: $cpuCores
- Server Process: $ServerProcessName (PID: $($serverProcess.Id))

========================================
Performance Data
========================================
Timestamp,ElapsedSeconds,CPUPercent,RAMMB,RAMPercent,ThreadCount,HandleCount,PrivateMemoryMB,WorkingSetMB,PageFileMB,GCGen0,GCGen1,GCGen2
"@

Add-Content -Path $OutputPath -Value $logHeader

# Monitoring loop
try {
    while ((Get-Date) -lt $endTime) {
        $currentTime = Get-Date
        $elapsed = ($currentTime - $startTime).TotalSeconds
        
        # Refresh process info
        $serverProcess.Refresh()
        
        # Get CPU usage (average across all cores)
        $cpuPercent = [math]::Round($cpuCounter.NextValue() / $cpuCores, 2)
        
        # Get RAM usage
        $ramMB = [math]::Round($serverProcess.WorkingSet64 / 1MB, 2)
        $ramPercent = [math]::Round(($serverProcess.WorkingSet64 / ($totalRAM * 1GB)) * 100, 2)
        $privateMemoryMB = [math]::Round($serverProcess.PrivateMemorySize64 / 1MB, 2)
        $pageFileMB = [math]::Round($serverProcess.PagedMemorySize64 / 1MB, 2)
        
        # Get process details
        $threadCount = $serverProcess.Threads.Count
        $handleCount = $serverProcess.HandleCount
        
        # Try to get GC info (if available via .NET)
        $gcGen0 = [GC]::CollectionCount(0)
        $gcGen1 = [GC]::CollectionCount(1)
        $gcGen2 = [GC]::CollectionCount(2)
        
        # Create sample object
        $sample = [PSCustomObject]@{
            Timestamp = $currentTime.ToString("yyyy-MM-dd HH:mm:ss")
            ElapsedSeconds = [math]::Round($elapsed, 2)
            CPUPercent = $cpuPercent
            RAMMB = $ramMB
            RAMPercent = $ramPercent
            ThreadCount = $threadCount
            HandleCount = $handleCount
            PrivateMemoryMB = $privateMemoryMB
            WorkingSetMB = $ramMB
            PageFileMB = $pageFileMB
            GCGen0 = $gcGen0
            GCGen1 = $gcGen1
            GCGen2 = $gcGen2
        }
        
        $samples += $sample
        
        # Write to log file
        $logLine = "$($sample.Timestamp),$($sample.ElapsedSeconds),$($sample.CPUPercent),$($sample.RAMMB),$($sample.RAMPercent),$($sample.ThreadCount),$($sample.HandleCount),$($sample.PrivateMemoryMB),$($sample.WorkingSetMB),$($sample.PageFileMB),$($sample.GCGen0),$($sample.GCGen1),$($sample.GCGen2)"
        Add-Content -Path $OutputPath -Value $logLine
        
        # Display progress
        $sampleCount++
        $progress = [math]::Round(($elapsed / ($DurationMinutes * 60)) * 100, 1)
        Write-Host "[$sampleCount] $($sample.Timestamp) - CPU: $($sample.CPUPercent)% | RAM: $($sample.RAMMB) MB ($($sample.RAMPercent)%) | Progress: $progress%" -ForegroundColor Gray
        
        # Wait for next sample
        Start-Sleep -Seconds $SampleIntervalSeconds
    }
} catch {
    Write-Host ""
    Write-Host "Monitoring interrupted: $_" -ForegroundColor Yellow
}

# Generate summary statistics
Write-Host ""
Write-Host "Generating summary report..." -ForegroundColor Yellow

$summary = @"

========================================
Summary Statistics
========================================

Total Samples: $sampleCount
Monitoring Duration: $([math]::Round(($endTime - $startTime).TotalMinutes, 2)) minutes

CPU Usage:
- Average: $([math]::Round(($samples | Measure-Object -Property CPUPercent -Average).Average, 2))%
- Minimum: $([math]::Round(($samples | Measure-Object -Property CPUPercent -Minimum).Minimum, 2))%
- Maximum: $([math]::Round(($samples | Measure-Object -Property CPUPercent -Maximum).Maximum, 2))%
- Median: $([math]::Round(($samples | Sort-Object CPUPercent | Select-Object -Index ([math]::Floor($samples.Count / 2))).CPUPercent, 2))%

RAM Usage:
- Average: $([math]::Round(($samples | Measure-Object -Property RAMMB -Average).Average, 2)) MB ($([math]::Round(($samples | Measure-Object -Property RAMPercent -Average).Average, 2))%)
- Minimum: $([math]::Round(($samples | Measure-Object -Property RAMMB -Minimum).Minimum, 2)) MB
- Maximum: $([math]::Round(($samples | Measure-Object -Property RAMMB -Maximum).Maximum, 2)) MB ($([math]::Round(($samples | Measure-Object -Property RAMPercent -Maximum).Maximum, 2))%)
- Median: $([math]::Round(($samples | Sort-Object RAMMB | Select-Object -Index ([math]::Floor($samples.Count / 2))).RAMMB, 2)) MB

Thread Count:
- Average: $([math]::Round(($samples | Measure-Object -Property ThreadCount -Average).Average, 0))
- Minimum: $($samples | Measure-Object -Property ThreadCount -Minimum).Minimum
- Maximum: $($samples | Measure-Object -Property ThreadCount -Maximum).Maximum

Handle Count:
- Average: $([math]::Round(($samples | Measure-Object -Property HandleCount -Average).Average, 0))
- Minimum: $($samples | Measure-Object -Property HandleCount -Minimum).Minimum
- Maximum: $($samples | Measure-Object -Property HandleCount -Maximum).Maximum

========================================
Performance Warnings
========================================
"@

# Check for performance issues
$warnings = @()

# High CPU usage (>80% average or >95% peak)
$avgCPU = ($samples | Measure-Object -Property CPUPercent -Average).Average
$maxCPU = ($samples | Measure-Object -Property CPUPercent -Maximum).Maximum
if ($avgCPU -gt 80) {
    $warnings += "⚠ WARNING: High average CPU usage ($([math]::Round($avgCPU, 2))%). Server may be CPU-bound."
}
if ($maxCPU -gt 95) {
    $warnings += "⚠ WARNING: CPU usage peaked at $([math]::Round($maxCPU, 2))%. Consider investigating spikes."
}

# High RAM usage (>80% of system RAM)
$maxRAMPercent = ($samples | Measure-Object -Property RAMPercent -Maximum).Maximum
if ($maxRAMPercent -gt 80) {
    $warnings += "⚠ WARNING: High RAM usage (peak: $([math]::Round($maxRAMPercent, 2))%). Server may be memory-bound."
}

# RAM growth trend
$firstRAM = $samples[0].RAMMB
$lastRAM = $samples[-1].RAMMB
$ramGrowth = $lastRAM - $firstRAM
if ($ramGrowth -gt 500) {
    $warnings += "⚠ WARNING: RAM usage increased by $([math]::Round($ramGrowth, 2)) MB during monitoring. Possible memory leak."
}

# High thread count (>200)
$maxThreads = ($samples | Measure-Object -Property ThreadCount -Maximum).Maximum
if ($maxThreads -gt 200) {
    $warnings += "⚠ WARNING: High thread count (peak: $maxThreads). May indicate threading issues."
}

# High handle count (>10000)
$maxHandles = ($samples | Measure-Object -Property HandleCount -Maximum).Maximum
if ($maxHandles -gt 10000) {
    $warnings += "⚠ WARNING: High handle count (peak: $maxHandles). May indicate resource leak."
}

if ($warnings.Count -eq 0) {
    $warnings += "✓ No significant performance issues detected."
}

$summary += "`n" + ($warnings -join "`n")

# Check Oxide logs for errors
$summary += "`n`n========================================`nOxide Plugin Log Analysis`n========================================`n"

$oxideLogPath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "oxide\logs"
if (Test-Path $oxideLogPath) {
    $recentLogs = Get-ChildItem -Path $oxideLogPath -Filter "*.txt" | Sort-Object LastWriteTime -Descending | Select-Object -First 5
    if ($recentLogs) {
        $summary += "Recent log files found:`n"
        foreach ($log in $recentLogs) {
            $summary += "- $($log.Name) ($(Get-Date $log.LastWriteTime -Format 'yyyy-MM-dd HH:mm:ss'))`n"
            
            # Count errors and warnings in the log
            $logContent = Get-Content $log.FullName -ErrorAction SilentlyContinue
            $errorCount = ($logContent | Select-String -Pattern "\[Error\]|ERROR|Exception" -CaseSensitive:$false).Count
            $warningCount = ($logContent | Select-String -Pattern "\[Warning\]|WARNING" -CaseSensitive:$false).Count
            
            if ($errorCount -gt 0 -or $warningCount -gt 0) {
                $summary += "  ⚠ Errors: $errorCount | Warnings: $warningCount`n"
            }
        }
    } else {
        $summary += "No Oxide log files found in: $oxideLogPath`n"
    }
} else {
    $summary += "Oxide log path not found: $oxideLogPath`n"
}

# Append summary to log file
Add-Content -Path $OutputPath -Value $summary

# Display summary
Write-Host $summary

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Performance log saved to:" -ForegroundColor Green
Write-Host $OutputPath -ForegroundColor White
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Cleanup
$cpuCounter.Dispose()
$ramCounter.Dispose()

Write-Host "Analysis complete!" -ForegroundColor Green

