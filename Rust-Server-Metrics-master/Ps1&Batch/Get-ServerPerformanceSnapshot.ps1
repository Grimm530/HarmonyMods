#Requires -Version 5.1
<#
.SYNOPSIS
  Pulls server FPS, frametime, entity count, players, and memory from InfluxDB (Rust Server Metrics).

.DESCRIPTION
  Complements Get-PluginResourceReport.ps1: Harmony hook times do NOT include Unity InvokeRepeating / FixedUpdate
  on spawned entities (e.g. event turrets). Use this to correlate FPS dips with load (entities, players, frame spikes).

.PARAMETER Hours
  Time window ending at now (default 1).

.EXAMPLE
  .\Get-ServerPerformanceSnapshot.ps1
  .\Get-ServerPerformanceSnapshot.ps1 -Hours 0.25
#>
[CmdletBinding()]
param(
    [string]$ConfigPath = "",
    [ValidateRange(0.25, 168)]
    [double]$Hours = 1,
    [string]$InfluxUrl = "",
    [string]$Database = "",
    [string]$User = "",
    [string]$Password = "",
    [string]$ServerTag = ""
)

$ErrorActionPreference = "Stop"

function Get-ServerRootFromScript {
    $p = $PSScriptRoot
    for ($i = 0; $i -lt 4; $i++) {
        $p = Split-Path $p -Parent
    }
    return $p
}

function Read-RsmConfiguration {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Configuration not found: $Path"
    }
    $raw = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    return ($raw | ConvertFrom-Json)
}

function Escape-InfluxTagValue {
    param([string]$s)
    if ($null -eq $s) { return "" }
    return $s.Replace("\", "\\").Replace("'", "\'")
}

function Invoke-InfluxQuery {
    param(
        [string]$BaseUrl,
        [string]$Db,
        [string]$InfluxQl,
        [string]$U,
        [string]$P
    )
    $q = [uri]::EscapeDataString($InfluxQl)
    $uri = "$BaseUrl/query?db=$([uri]::EscapeDataString($Db))&q=$q"
    $params = @{
        Uri             = $uri
        Method          = "Get"
        UseBasicParsing = $true
        TimeoutSec      = 60
    }
    if ($U) {
        $pair = "${U}:${P}"
        $b64 = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($pair))
        $params["Headers"] = @{ Authorization = "Basic $b64" }
    }
    return (Invoke-RestMethod @params)
}

function Get-InfluxScalarValue {
    param($InfluxResponse)
    if (-not $InfluxResponse.results) { return $null }
    foreach ($res in $InfluxResponse.results) {
        if (-not $res.series) { continue }
        foreach ($ser in $res.series) {
            if (-not $ser.values -or $ser.values.Count -lt 1) { continue }
            $row = $ser.values[0]
            if ($row.Count -ge 2) { return $row[1] }
        }
    }
    return $null
}

if (-not $ConfigPath) {
    $rootGuess = Get-ServerRootFromScript
    $ConfigPath = Join-Path $rootGuess "HarmonyMods_Data\ServerMetrics\Configuration.json"
}

$cfg = $null
if (Test-Path -LiteralPath $ConfigPath) {
    $cfg = Read-RsmConfiguration -Path $ConfigPath
}

if (-not $InfluxUrl) {
    if (-not $cfg) { throw "No -InfluxUrl and no readable config at $ConfigPath" }
    $InfluxUrl = ($cfg."Influx Database Url").Trim().TrimEnd("/")
}
if (-not $Database) {
    if (-not $cfg) { throw "No -Database and no readable config at $ConfigPath" }
    $Database = $cfg."Influx Database Name"
}
if (-not $User -and $cfg) { $User = $cfg."Influx Database User" }
if (-not $Password -and $cfg) { $Password = $cfg."Influx Database Password" }
if (-not $ServerTag -and $cfg) { $ServerTag = $cfg."Server Tag" }

if ([string]::IsNullOrWhiteSpace($ServerTag)) {
    throw "Server Tag is empty. Set it in Configuration.json or pass -ServerTag."
}

$tagEsc = Escape-InfluxTagValue $ServerTag
$dur = if ($Hours -lt 1) { "$([int]($Hours * 60))m" } else { "$([int]$Hours)h" }
$where = "time >= now() - $dur AND `"server`" = '$tagEsc'"

$script:fpsSamples = $null
$script:fpsUnder60 = $null

Write-Host ""
Write-Host "Influx: $InfluxUrl  |  db=$Database  |  server=$ServerTag  |  window=now-$dur" -ForegroundColor Cyan
Write-Host ""
Write-Host "How to read this" -ForegroundColor Green
Write-Host "  RSM posts ~1 point/sec (Performance.FPSTimer). 'instant' FPS = frames counted that second." -ForegroundColor DarkGray
Write-Host "  mean FPS ~= healthy average. min/max often = one bad second (save/GC/stall), not 'constant bounce'." -ForegroundColor DarkGray
Write-Host "  stddev + 'seconds below 60 FPS' show variance. p95 frametime = typical worst second (ignores single spikes)." -ForegroundColor DarkGray
Write-Host "  memory.used = system RAM used (MB, OS-level via Rust native), not RustDedicated.exe private bytes." -ForegroundColor DarkGray
Write-Host "  GC collections = cumulative process count; 'delta in window' = new collections while profiling." -ForegroundColor DarkGray
Write-Host "  Plugin TSV = Harmony hook time only - not Unity InvokeRepeating on event entities (turrets, etc.)." -ForegroundColor DarkGray
Write-Host ""

$queries = @(
    @{ Label = "FPS instant (mean / min / max / stddev)"; Q = "SELECT mean(`"instant`") AS mean, min(`"instant`") AS min, max(`"instant`") AS max, stddev(`"instant`") AS stddev FROM `"framerate`" WHERE $where" }
    @{ Label = "FPS: seconds sampled (count of 1s buckets)"; Q = "SELECT COUNT(`"instant`") AS samples FROM `"framerate`" WHERE $where" }
    @{ Label = 'FPS: seconds under 60 FPS (hitchy seconds)'; Q = 'SELECT COUNT("instant") AS seconds_under_60 FROM "framerate" WHERE ' + $where + ' AND "instant" < 60' }
    @{ Label = 'FPS average field (mean of game 60s rolling avg)'; Q = "SELECT mean(`"average`") AS mean_avg FROM `"framerate`" WHERE $where" }
    @{ Label = "Frame time ms instant (mean / max / p95)"; Q = "SELECT mean(`"instant`") AS mean_ms, max(`"instant`") AS max_ms, percentile(`"instant`", 95) AS p95_ms FROM `"frametime`" WHERE $where" }
    @{ Label = "Frame time ms average field (mean)"; Q = "SELECT mean(`"average`") AS mean_avg_ms FROM `"frametime`" WHERE $where" }
    @{ Label = "Entity count (mean / max)"; Q = "SELECT mean(`"count`") AS mean, max(`"count`") AS max FROM `"entities`" WHERE $where" }
    @{ Label = "Players online (mean / max)"; Q = "SELECT mean(`"count`") AS mean, max(`"count`") AS max FROM `"players`" WHERE $where" }
    @{ Label = "System memory used (mean / max, MB)"; Q = "SELECT mean(`"used`") AS mean_mb, max(`"used`") AS max_mb FROM `"memory`" WHERE $where" }
    @{ Label = 'GC collections (cumulative; first vs last in window = delta while profiling)'; Q = "SELECT first(`"collections`") AS first, last(`"collections`") AS last FROM `"memory`" WHERE $where" }
)

foreach ($item in $queries) {
    Write-Host ("--- {0} ---" -f $item.Label) -ForegroundColor Yellow
    try {
        $resp = Invoke-InfluxQuery -BaseUrl $InfluxUrl -Db $Database -InfluxQl $item.Q -U $User -P $Password
        if ($resp.results[0].error) {
            Write-Host ("  Influx error: {0}" -f $resp.results[0].error) -ForegroundColor Red
            continue
        }
        if (-not $resp.results[0].series) {
            Write-Host "  (no data in this window)" -ForegroundColor DarkGray
            continue
        }
        $ser = $resp.results[0].series[0]
        $cols = @($ser.columns)
        $vals = $ser.values[0]
        $row = @{}
        for ($i = 0; $i -lt $cols.Count; $i++) {
            if ($i -eq 0) { continue }
            $name = $cols[$i]
            $v = $vals[$i]
            $row[$name] = $v
            Write-Host ("  {0,-22} {1}" -f $name, $v)
        }
        if ($item.Label -like "FPS: seconds sampled*") { $script:fpsSamples = $row["samples"] }
        if ($item.Label -like "FPS: seconds under 60*") { $script:fpsUnder60 = $row["seconds_under_60"] }
        if ($item.Label -like "GC collections*" -and $row.ContainsKey("first") -and $row.ContainsKey("last") -and $null -ne $row["first"] -and $null -ne $row["last"]) {
            try {
                $d = [double]$row["last"] - [double]$row["first"]
                Write-Host ("  {0,-22} {1}" -f "delta_collections", $d)
            } catch { }
        }
    } catch {
        Write-Host ("  Query failed: {0}" -f $_.Exception.Message) -ForegroundColor Red
    }
    Write-Host ""
}

if ($null -ne $script:fpsSamples -and $null -ne $script:fpsUnder60) {
    try {
        $s = [double]$script:fpsSamples
        if ($s -gt 0) {
            $pct = 100.0 * [double]$script:fpsUnder60 / $s
            Write-Host "--- Derived ---" -ForegroundColor Yellow
            Write-Host ("  pct_seconds_under_60_fps  {0:N2} % ({1} of {2} samples)" -f $pct, $script:fpsUnder60, $script:fpsSamples) -ForegroundColor White
            Write-Host ""
        }
    } catch { }
}

Write-Host "Correlate with plugin-metrics TSV:" -ForegroundColor DarkGray
Write-Host "  If event plugins show tiny mean_*_ms but FPS still dips -> look at native load (entities, physics, AI), not Harmony hooks." -ForegroundColor DarkGray
Write-Host "  If seconds_under_60 is high vs samples -> sustained low-FPS seconds; if it is 1-2 -> rare hitches (max frametime)." -ForegroundColor DarkGray
Write-Host ""
