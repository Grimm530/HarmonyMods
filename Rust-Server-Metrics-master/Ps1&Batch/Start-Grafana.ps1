#Requires -Version 5.1
<#
.SYNOPSIS
  Starts Grafana (console session) and opens the UI. Closing this window stops the server process we started.

  Optional environment variables:
    GRAFANA_SERVER_EXE  Full path to grafana-server.exe
    GRAFANA_URL         Default http://localhost:3000

  Desktop shortcut: right-click Start-Grafana.bat -> Send to -> Desktop (create shortcut)
#>
$ErrorActionPreference = "Continue"

$RepoRoot = Split-Path $PSScriptRoot -Parent
$GrafanaUrl = if ($env:GRAFANA_URL) { $env:GRAFANA_URL.Trim() } else { "http://localhost:3000" }

function Find-GrafanaServerExe {
    if ($env:GRAFANA_SERVER_EXE -and (Test-Path -LiteralPath $env:GRAFANA_SERVER_EXE)) {
        return (Resolve-Path -LiteralPath $env:GRAFANA_SERVER_EXE).Path
    }
    # Grafana 9–10+: unified "grafana.exe"; older MSI used "grafana-server.exe" in the same bin folder.
    $candidates = @(
        "C:\Program Files\GrafanaLabs\grafana\bin\grafana.exe"
        "C:\Program Files\GrafanaLabs\grafana\bin\grafana-server.exe"
        "C:\Program Files (x86)\GrafanaLabs\grafana\bin\grafana.exe"
        "C:\Program Files (x86)\GrafanaLabs\grafana\bin\grafana-server.exe"
    )
    foreach ($p in $candidates) {
        if (Test-Path -LiteralPath $p) { return (Resolve-Path -LiteralPath $p).Path }
    }
    # Per-user / alternate installs (e.g. winget: %LOCALAPPDATA%\Programs\GrafanaLabs\...)
    $searchRoots = @(
        (Join-Path $env:LOCALAPPDATA "Programs\GrafanaLabs")
        (Join-Path $env:ProgramFiles "GrafanaLabs")
        (Join-Path ${env:ProgramFiles(x86)} "GrafanaLabs")
    )
    foreach ($root in $searchRoots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        foreach ($binName in @("grafana.exe", "grafana-server.exe")) {
            try {
                $hit = Get-ChildItem -LiteralPath $root -Filter $binName -Recurse -Depth 12 -ErrorAction SilentlyContinue |
                    Select-Object -First 1
                if ($hit) { return $hit.FullName }
            } catch { }
        }
    }
    # PATH (if Grafana added itself)
    foreach ($name in @("grafana.exe", "grafana-server.exe")) {
        try {
            $cmd = Get-Command $name -ErrorAction SilentlyContinue
            if ($cmd -and $cmd.Source -and (Test-Path -LiteralPath $cmd.Source)) { return $cmd.Source }
        } catch { }
    }
    return $null
}

function Update-GrafanaDashboard {
    param([string]$DashboardPath)

    if (-not (Test-Path -LiteralPath $DashboardPath)) {
        Write-Host "Dashboard file not found: $DashboardPath" -ForegroundColor Yellow
        return $false
    }

    Write-Host "Updating Grafana dashboard with new metrics..." -ForegroundColor Cyan

    try {
        $dashboardContent = Get-Content -LiteralPath $DashboardPath -Raw -Encoding UTF8
        $dashboard = $dashboardContent | ConvertFrom-Json
        $updated = $false

        if ($dashboard.panels) {
            foreach ($panel in $dashboard.panels) {
                if (-not $panel.targets) { continue }
                foreach ($target in $panel.targets) {
                    if (-not $target.select) { continue }
                    foreach ($selectGroup in $target.select) {
                        if ($selectGroup -isnot [System.Array]) { continue }
                        $fieldItem = $null
                        $aliasItem = $null
                        foreach ($selectItem in $selectGroup) {
                            if ($selectItem.type -eq "field") { $fieldItem = $selectItem }
                            if ($selectItem.type -eq "alias") { $aliasItem = $selectItem }
                        }
                        if ($fieldItem -and $fieldItem.params -and $fieldItem.params.Count -gt 0) {
                            if ($fieldItem.params[0] -eq "hookTime") {
                                $fieldItem.params[0] = "avgRunningTime"
                                $updated = $true
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

        if ($updated) {
            $updatedJson = $dashboard | ConvertTo-Json -Depth 100
            $backupPath = $DashboardPath + ".backup"
            Copy-Item -LiteralPath $DashboardPath -Destination $backupPath -Force
            Write-Host "  Created backup: $backupPath" -ForegroundColor Gray
            $updatedJson | Set-Content -LiteralPath $DashboardPath -Encoding UTF8 -NoNewline
            Write-Host "Dashboard updated successfully!" -ForegroundColor Green
            return $true
        }
        Write-Host "Dashboard already uses new metrics or no updates needed." -ForegroundColor Gray
        return $false
    } catch {
        Write-Host "Error updating dashboard: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Grafana Server Launcher" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$UpdateDashboard = $true
if ($UpdateDashboard) {
    $dashboardPath = Join-Path $RepoRoot "res\Grafana-Dashboard.json"
    $legacyPath = Join-Path (Split-Path $RepoRoot -Parent) "ps1scripts\Grafana-Dashboard.json"
    if (Test-Path -LiteralPath $dashboardPath) {
        $null = Update-GrafanaDashboard -DashboardPath $dashboardPath
        Write-Host ""
    } elseif (Test-Path -LiteralPath $legacyPath) {
        $null = Update-GrafanaDashboard -DashboardPath $legacyPath
        Write-Host ""
    } else {
        Write-Host "No dashboard JSON found (tried res and HarmonyMods\ps1scripts). Skipping update." -ForegroundColor Yellow
        Write-Host ""
    }
}

$grafanaPath = Find-GrafanaServerExe
$grafanaProcess = $null

function Stop-GrafanaProcess {
    param($ProcessId)
    if (-not $ProcessId) { return }
    $proc = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($proc -and -not $proc.HasExited) {
        Write-Host "`nStopping Grafana server..." -ForegroundColor Yellow
        Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
        Write-Host "Grafana server stopped." -ForegroundColor Green
    }
}

$null = Register-EngineEvent PowerShell.Exiting -Action {
    if ($global:GrafanaProcessId) {
        Stop-GrafanaProcess -ProcessId $global:GrafanaProcessId
    }
}

if (-not $grafanaPath) {
    Write-Host "ERROR: Grafana server binary not found (grafana.exe or grafana-server.exe)." -ForegroundColor Red
    Write-Host "Install Grafana (https://grafana.com/grafana/download) or set the full path once:" -ForegroundColor Yellow
    Write-Host '  [Environment]::SetEnvironmentVariable("GRAFANA_SERVER_EXE", "C:\Path\to\grafana\bin\grafana.exe", "User")' -ForegroundColor Gray
    Write-Host "  (then open a new PowerShell window and run this script again)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Searched: GRAFANA_SERVER_EXE env, Program Files\GrafanaLabs\...\bin (grafana.exe / grafana-server.exe), %LOCALAPPDATA%\Programs, PATH." -ForegroundColor DarkGray
    Write-Host ""
    if ($Host.UI.RawUI) {
        Write-Host "Press any key to exit..."
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    }
    exit 1
}

$existingProcess = Get-Process -Name "grafana", "grafana-server" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($existingProcess) {
    Write-Host "Grafana is already running (PID: $($existingProcess.Id))" -ForegroundColor Yellow
    Write-Host "Opening Grafana web interface..." -ForegroundColor Green
    Start-Process $GrafanaUrl
    Write-Host "Grafana is available at: $GrafanaUrl" -ForegroundColor Green
    Write-Host ""
    if ($Host.UI.RawUI) {
        Write-Host "Press any key to exit..."
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    }
    exit 0
}

Write-Host "Starting Grafana server..." -ForegroundColor Green
Write-Host "Grafana path: $grafanaPath" -ForegroundColor Gray
Write-Host ""

try {
    $grafanaBinDir = Split-Path $grafanaPath -Parent
    $grafanaHomeDir = Split-Path $grafanaBinDir -Parent
    Write-Host "Grafana home directory: $grafanaHomeDir" -ForegroundColor Gray

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $grafanaPath
    $psi.Arguments = "-homepath `"$grafanaHomeDir`""
    $psi.WorkingDirectory = $grafanaHomeDir
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true

    $grafanaProcess = New-Object System.Diagnostics.Process
    $grafanaProcess.StartInfo = $psi
    $grafanaProcess.Start() | Out-Null
    $global:GrafanaProcessId = $grafanaProcess.Id

    Write-Host "Grafana server started (PID: $($grafanaProcess.Id))" -ForegroundColor Green
    Write-Host ""
    Write-Host "Waiting for Grafana to initialize..." -ForegroundColor Yellow
    Start-Sleep -Seconds 5

    $maxAttempts = 12
    $attempt = 0
    $grafanaReady = $false
    while ($attempt -lt $maxAttempts -and -not $grafanaReady) {
        try {
            $null = Invoke-WebRequest -Uri $GrafanaUrl -TimeoutSec 2 -UseBasicParsing -ErrorAction Stop
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
        Start-Process $GrafanaUrl
        Write-Host "Grafana is available at: $GrafanaUrl" -ForegroundColor Green
        Write-Host "Default login: admin / admin" -ForegroundColor Yellow
        Write-Host ""
    } else {
        Write-Host ""
        Write-Host "WARNING: Grafana may still be starting up." -ForegroundColor Yellow
        Write-Host "You can access it at: $GrafanaUrl" -ForegroundColor Green
        Write-Host ""
        Start-Process $GrafanaUrl
    }

    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Grafana server is running." -ForegroundColor Green
    Write-Host "Close this window to stop Grafana server." -ForegroundColor Yellow
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""

    try {
        while (-not $grafanaProcess.HasExited) {
            Start-Sleep -Seconds 1
            try { $null = $grafanaProcess.Refresh() } catch { break }
        }
    } finally {
        Stop-GrafanaProcess -ProcessId $global:GrafanaProcessId
    }
} catch {
    Write-Host ""
    Write-Host "ERROR: Failed to start Grafana server" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    if ($Host.UI.RawUI) {
        Write-Host "Press any key to exit..."
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    }
    exit 1
}
