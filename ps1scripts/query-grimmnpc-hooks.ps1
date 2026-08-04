# Query GrimmNPC Potential Hooks
# Since GrimmNPC is a HarmonyMod, it patches game code directly
# We can identify potential hooks by looking for NPC-related behaviours/methods

param(
    [int]$LookbackMinutes = 60,
    [string]$ConfigPath = "",
    [string]$InfluxUrl = "",
    [string]$DatabaseName = "",
    [string]$DatabaseUser = "",
    [string]$DatabasePassword = "",
    [string]$ServerTag = ""
)

$ErrorActionPreference = "Stop"

# Import shared functions from generate-metrics-report.ps1
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$sharedScript = Join-Path $scriptPath "generate-metrics-report.ps1"
if (Test-Path $sharedScript) {
    . $sharedScript -ErrorAction SilentlyContinue
} else {
    Write-Error "Could not find generate-metrics-report.ps1 for shared functions"
    exit 1
}

# Load configuration (reuse logic from generate-metrics-report.ps1)
$configSearchPaths = @()
if ($ConfigPath) {
    $configSearchPaths += $ConfigPath
}

$potentialPaths = @(
    (Join-Path $PSScriptRoot "..\..\..\HarmonyData\ServerMetrics\Configuration.json"),
    (Join-Path $PSScriptRoot "..\..\HarmonyData\ServerMetrics\Configuration.json"),
    (Join-Path $PSScriptRoot "HarmonyData\ServerMetrics\Configuration.json"),
    "HarmonyData\ServerMetrics\Configuration.json"
)

foreach ($potentialPath in $potentialPaths) {
    try {
        $resolvedPath = Resolve-Path $potentialPath -ErrorAction Stop
        if ($resolvedPath -and (Test-Path $resolvedPath)) {
            $configSearchPaths += $resolvedPath.Path
        }
    } catch {
        continue
    }
}

foreach ($path in $configSearchPaths | Select-Object -Unique) {
    if (-not $path) { continue }
    Resolve-ConfigValues -ConfigPathCandidate $path -InfluxUrlRef ([ref]$InfluxUrl) -DatabaseNameRef ([ref]$DatabaseName) -DatabaseUserRef ([ref]$DatabaseUser) -DatabasePasswordRef ([ref]$DatabasePassword) -ServerTagRef ([ref]$ServerTag)
    if ($InfluxUrl -and $DatabaseName -and $DatabaseUser -and $DatabasePassword -and $ServerTag) {
        Write-Host "Loaded configuration from: $path" -ForegroundColor Green
        break
    }
}

if (-not ($InfluxUrl -and $DatabaseName -and $DatabaseUser -and $DatabasePassword -and $ServerTag)) {
    throw "Missing Influx connection details. Provide -ConfigPath or explicit parameters."
}

$influxBase = $InfluxUrl.TrimEnd('/')
if ($influxBase.ToLower().EndsWith("/query")) {
    $queryEndpoint = $influxBase
} else {
    $queryEndpoint = "$influxBase/query"
}

Write-Host ""
Write-Host "=== GrimmNPC Potential Hook Analysis ===" -ForegroundColor Cyan
Write-Host "Server: $ServerTag" -ForegroundColor Gray
Write-Host "Lookback: $LookbackMinutes minutes" -ForegroundColor Gray
Write-Host ""

# NPC-related behaviours that GrimmNPC likely patches
$npcBehaviours = @(
    "BaseNpc",
    "NPCPlayer", 
    "BaseAIBrain",
    "NPCShopKeeper",
    "NPCVendor",
    "GrimmNPC",  # In case GrimmNPC has its own behaviour class
    "HumanNPC",
    "ScientistNPC"
)

Write-Host "Querying invoke_execution for NPC-related behaviours..." -ForegroundColor Yellow
Write-Host ""

# Query all invoke_execution data grouped by behaviour and method
$allInvokeTotals = Get-GroupedTotals -Measurement "invoke_execution" -Field "duration" -GroupByTags @("behaviour","method") -LookbackMinutes $LookbackMinutes -ServerTag $ServerTag -Endpoint $queryEndpoint -Database $DatabaseName -User $DatabaseUser -Password $DatabasePassword

# Filter for NPC-related behaviours
$grimmnpcHooks = @($allInvokeTotals |
    Where-Object { 
        $behaviour = ($_.Tags.behaviour -replace '^"|"$', '' -replace '\\"', '"')
        $npcBehaviours -contains $behaviour
    } |
    Where-Object { $_.TotalMs -gt 0 } |
    Sort-Object -Property TotalMs -Descending |
    ForEach-Object {
        $behaviour = ($_.Tags.behaviour -replace '^"|"$', '' -replace '\\"', '"')
        $method = ($_.Tags.method -replace '^"|"$', '' -replace '\\"', '"')
        $totalMs = [double]$_.TotalMs
        [PSCustomObject]@{
            Hook         = "$behaviour::$method"
            Behaviour    = $behaviour
            Method       = $method
            TotalMs      = [math]::Round($totalMs, 2)
            TotalSeconds = [math]::Round($totalMs / 1000, 2)
            AvgMsPerMin  = [math]::Round($totalMs / $LookbackMinutes, 2)
        }
    })

Write-Host "Found $($grimmnpcHooks.Count) NPC-related hooks that GrimmNPC may be patching:" -ForegroundColor Green
Write-Host ""

if ($grimmnpcHooks.Count -eq 0) {
    Write-Host "No NPC-related hooks found. This could mean:" -ForegroundColor Yellow
    Write-Host "  1. GrimmNPC doesn't patch these methods" -ForegroundColor Gray
    Write-Host "  2. No NPCs are active on the server" -ForegroundColor Gray
    Write-Host "  3. The hooks haven't been called during the lookback period" -ForegroundColor Gray
} else {
    # Group by behaviour for better organization
    $byBehaviour = $grimmnpcHooks | Group-Object -Property Behaviour | Sort-Object { ($_.Group | Measure-Object -Property TotalMs -Sum).Sum } -Descending
    
    foreach ($group in $byBehaviour) {
        $behaviourTotal = ($group.Group | Measure-Object -Property TotalMs -Sum).Sum
        Write-Host "=== $($group.Name) ===" -ForegroundColor Cyan
        Write-Host "Total Time: $([math]::Round($behaviourTotal, 2)) ms ($([math]::Round($behaviourTotal / 1000, 2)) s)" -ForegroundColor White
        Write-Host ""
        
        foreach ($hook in $group.Group | Sort-Object -Property TotalMs -Descending) {
            Write-Host "  $($hook.Method)" -ForegroundColor Yellow
            Write-Host "    Total: $($hook.TotalMs) ms ($($hook.TotalSeconds) s)" -ForegroundColor Gray
            Write-Host "    Avg/min: $($hook.AvgMsPerMin) ms" -ForegroundColor Gray
            Write-Host ""
        }
    }
    
    Write-Host "=== Summary ===" -ForegroundColor Cyan
    $totalTime = ($grimmnpcHooks | Measure-Object -Property TotalMs -Sum).Sum
    Write-Host "Total NPC-related hook time: $([math]::Round($totalTime, 2)) ms ($([math]::Round($totalTime / 1000, 2)) s)" -ForegroundColor White
    Write-Host ""
    Write-Host "Top 5 NPC hooks by total time:" -ForegroundColor Yellow
    $grimmnpcHooks | Select-Object -First 5 | ForEach-Object {
        Write-Host "  $($_.Hook): $($_.TotalSeconds) s" -ForegroundColor White
    }
}

Write-Host ""
Write-Host "=== Note ===" -ForegroundColor Cyan
Write-Host "These hooks represent ALL calls to NPC-related methods." -ForegroundColor Gray
Write-Host "GrimmNPC may be patching some of these, but we cannot definitively" -ForegroundColor Gray
Write-Host "identify which ones without inspecting GrimmNPC's source code." -ForegroundColor Gray
Write-Host ""
Write-Host "To identify GrimmNPC's actual impact:" -ForegroundColor Yellow
Write-Host "  1. Check GrimmNPC source code for Harmony patches" -ForegroundColor Gray
Write-Host "  2. Compare server performance with/without GrimmNPC loaded" -ForegroundColor Gray
Write-Host "  3. Use profiling tools to measure patch overhead" -ForegroundColor Gray
