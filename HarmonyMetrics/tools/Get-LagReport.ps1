#Requires -Version 5.1
<#
.SYNOPSIS
  Query InfluxDB for a frametime / AI / NPC lag report around a time window.

.DESCRIPTION
  Prints a console summary and writes JSON under tools/lag-report-*.json.

  Quirks this script already handles:
  - server_update field is "duration" (InfluxQL reserved - must be quoted)
  - behaviour / method tag values are stored WITH literal quotes (e.g. "AIThinkManager")
  - players online field is "count" (not "online")
  - Influx 1.8 only supports ORDER BY time - ranking is done in PowerShell

.EXAMPLE
  # Last 2 hours ending now
  .\Get-LagReport.ps1

.EXAMPLE
  # Around a Discord report time (local machine timezone)
  .\Get-LagReport.ps1 -Around "2026-09-06 00:03" -BeforeMinutes 75 -AfterMinutes 90

.EXAMPLE
  # Staging tag on another box
  .\Get-LagReport.ps1 -Around "2026-09-06 00:03" -ServerTag stagingserver -OutDir .\out
#>
[CmdletBinding()]
param(
    [string]$Around = "",
    [int]$BeforeMinutes = 60,
    [int]$AfterMinutes = 30,
    [int]$Hours = 0,
    [string]$InfluxUrl = "http://127.0.0.1:8086",
    [string]$Database = "rust_server_metrics",
    [string]$ServerTag = "stagingserver",
    [string]$OutDir = "",
    [int]$HitchThresholdMs = 80,
    [switch]$SkipJson
)

$ErrorActionPreference = "Stop"

if (-not $OutDir) {
    $OutDir = Join-Path $PSScriptRoot "lag-reports"
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Get-OffsetString {
    $offset = [DateTimeOffset]::Now.Offset
    $sign = if ($offset.TotalMinutes -ge 0) { "+" } else { "-" }
    return ("{0}{1:00}:{2:00}" -f $sign, [Math]::Abs([int]$offset.Hours), [Math]::Abs($offset.Minutes % 60))
}

function Format-InfluxTime([DateTimeOffset]$dto) {
    # Local wall clock with numeric offset - Influx 1.8 accepts this.
    return $dto.ToString("yyyy-MM-ddTHH:mm:ss") + (Get-OffsetString)
}

function Invoke-Influx([string]$query) {
    $uri = "{0}/query?db={1}&epoch=ms&q={2}" -f $InfluxUrl, [uri]::EscapeDataString($Database), [uri]::EscapeDataString($query)
    try {
        return Invoke-RestMethod -Uri $uri -TimeoutSec 180
    } catch {
        throw "Influx query failed: $($_.Exception.Message)`nQuery: $query"
    }
}

function Get-SeriesRows($response) {
    $out = New-Object System.Collections.Generic.List[object]
    if (-not $response.results -or -not $response.results[0].series) { return $out }
    if ($response.results[0].error) { throw "Influx error: $($response.results[0].error)" }
    foreach ($series in $response.results[0].series) {
        $tags = $series.tags
        foreach ($values in $series.values) {
            $row = [ordered]@{}
            for ($i = 0; $i -lt $series.columns.Count; $i++) {
                $row[$series.columns[$i]] = $values[$i]
            }
            if ($tags) {
                foreach ($prop in $tags.PSObject.Properties) {
                    # Strip literal quote wrappers HarmonyMetrics writes into tags.
                    $row[$prop.Name] = ([string]$prop.Value).Trim('"')
                }
            }
            [void]$out.Add([pscustomobject]$row)
        }
    }
    return $out
}

function Save-Json([string]$name, $obj) {
    if ($SkipJson) { return }
    $path = Join-Path $OutDir ("lag-report-{0}.json" -f $name)
    $obj | ConvertTo-Json -Depth 14 -Compress | Set-Content -Path $path -Encoding UTF8
    return $path
}

function Write-Section([string]$title) {
    Write-Host ""
    Write-Host ("==== {0} ====" -f $title) -ForegroundColor Cyan
}

# --- resolve window ---
$tz = [TimeZoneInfo]::Local
if ($Hours -gt 0) {
    $end = [DateTimeOffset]::Now
    $start = $end.AddHours(-$Hours)
    $coreStart = $start
    $coreEnd = $end
} elseif ($Around) {
    $parsed = [DateTime]::Parse($Around, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeLocal)
    $center = [DateTimeOffset]::new($parsed, $tz.GetUtcOffset($parsed))
    $start = $center.AddMinutes(-$BeforeMinutes)
    $end = $center.AddMinutes($AfterMinutes)
    # Narrow "core" = +/- 15 min around report for method aggregates
    $coreStart = $center.AddMinutes(-15)
    $coreEnd = $center.AddMinutes(15)
} else {
    $end = [DateTimeOffset]::Now
    $start = $end.AddHours(-2)
    $coreStart = $end.AddMinutes(-30)
    $coreEnd = $end
}

$t0 = Format-InfluxTime $start
$t1 = Format-InfluxTime $end
$c0 = Format-InfluxTime $coreStart
$c1 = Format-InfluxTime $coreEnd
$stamp = (Get-Date).ToString("yyyyMMdd-HHmmss")

Write-Host "HarmonyMetrics lag report" -ForegroundColor Green
Write-Host ("  Server tag : {0}" -f $ServerTag)
Write-Host ("  Window     : {0}  ->  {1}" -f $start.ToString("yyyy-MM-dd HH:mm"), $end.ToString("yyyy-MM-dd HH:mm"))
Write-Host ("  Core       : {0}  ->  {1}" -f $coreStart.ToString("yyyy-MM-dd HH:mm"), $coreEnd.ToString("yyyy-MM-dd HH:mm"))
Write-Host ("  OutDir     : {0}" -f $OutDir)

# ping
try {
    Invoke-WebRequest -Uri ($InfluxUrl.TrimEnd('/') + "/ping") -UseBasicParsing -TimeoutSec 5 | Out-Null
} catch {
    throw "Influx not reachable at $InfluxUrl - start it with tools\Start-InfluxDB.ps1 first."
}

# Tag values are stored WITH literal quote characters (value = "AIThinkManager").
# Prefer regex filters so we do not fight PowerShell/InfluxQL quote escaping.
$aiFilter = "behaviour =~ /AIThinkManager/ AND method =~ /ProcessQueue/"
$smFilter = "behaviour =~ /ServerMgr/ AND method =~ /Update/"

# --- queries ---
$raw = [ordered]@{}

$raw.ftStats = Invoke-Influx @"
SELECT mean(average) AS avg_ms, max(average) AS max_ms, min(average) AS min_ms,
       percentile(average,95) AS p95, percentile(average,99) AS p99
FROM frametime
WHERE server='$ServerTag' AND time >= '$t0' AND time <= '$t1'
"@

$raw.ftMinute = Invoke-Influx @"
SELECT mean(average) AS avg_ms, max(average) AS max_ms, percentile(average,95) AS p95
FROM frametime
WHERE server='$ServerTag' AND time >= '$t0' AND time <= '$t1'
GROUP BY time(1m) fill(none)
"@

$raw.ftSpikes = Invoke-Influx @"
SELECT average AS ms, instant
FROM frametime
WHERE server='$ServerTag' AND time >= '$t0' AND time <= '$t1' AND average > $HitchThresholdMs
"@

$raw.ftInstant = Invoke-Influx @"
SELECT average AS avg_ms, instant
FROM frametime
WHERE server='$ServerTag' AND time >= '$t0' AND time <= '$t1' AND instant > 200
"@

$raw.suCore = Invoke-Influx @"
SELECT mean("duration") AS mean_ms, max("duration") AS max_ms, sum("duration") AS sum_ms, count("duration") AS n
FROM server_update
WHERE server='$ServerTag' AND time >= '$c0' AND time <= '$c1'
GROUP BY behaviour, method
"@

$raw.aiMinute = Invoke-Influx @"
SELECT mean("duration") AS mean_ms, max("duration") AS max_ms
FROM server_update
WHERE server='$ServerTag' AND time >= '$t0' AND time <= '$t1'
  AND $aiFilter
GROUP BY time(1m) fill(none)
"@

$raw.smMinute = Invoke-Influx @"
SELECT mean("duration") AS mean_ms, max("duration") AS max_ms
FROM server_update
WHERE server='$ServerTag' AND time >= '$t0' AND time <= '$t1'
  AND $smFilter
GROUP BY time(1m) fill(none)
"@

$raw.invokeCore = Invoke-Influx @"
SELECT mean("duration") AS mean_ms, max("duration") AS max_ms, sum("duration") AS sum_ms, count("duration") AS n
FROM invoke_execution
WHERE server='$ServerTag' AND time >= '$c0' AND time <= '$c1'
GROUP BY method
"@

$raw.consoleCore = Invoke-Influx @"
SELECT mean("duration") AS mean_ms, max("duration") AS max_ms, sum("duration") AS sum_ms, count("duration") AS n
FROM console_commands
WHERE server='$ServerTag' AND time >= '$c0' AND time <= '$c1'
GROUP BY command
"@

$raw.playersMinute = Invoke-Influx @"
SELECT mean(count) AS online, mean(bots) AS bots, mean(bots_mod) AS bots_mod,
       mean(bots_grimm) AS grimm, mean(bots_zombie) AS zombie, mean(bots_vanilla) AS vanilla
FROM players
WHERE server='$ServerTag' AND time >= '$t0' AND time <= '$t1'
GROUP BY time(1m) fill(none)
"@

$raw.npcMinute = Invoke-Influx @"
SELECT mean(bots_grimm) AS grimm, mean(bots_zombie) AS zombie, mean(bots_vanilla) AS vanilla,
       mean(animals_total) AS animals
FROM npc_census
WHERE server='$ServerTag' AND time >= '$t0' AND time <= '$t1'
GROUP BY time(1m) fill(none)
"@

$raw.entStats = Invoke-Influx @"
SELECT mean(count) AS ent, max(count) AS max_ent
FROM entities
WHERE server='$ServerTag' AND time >= '$t0' AND time <= '$t1'
"@

$raw.cpuStats = Invoke-Influx @"
SELECT mean(total_cpu_ms) AS cpu, max(total_cpu_ms) AS max_cpu,
       mean(update_ms) AS upd, max(update_ms) AS max_upd
FROM cpu_sample
WHERE server='$ServerTag' AND time >= '$t0' AND time <= '$t1'
"@

$raw.modEvents = Invoke-Influx @"
SELECT * FROM harmony_mod_event
WHERE server='$ServerTag' AND time >= '$t0' AND time <= '$t1'
"@

if (-not $SkipJson) {
    $bundlePath = Join-Path $OutDir ("lag-report-raw-{0}.json" -f $stamp)
    $raw | ConvertTo-Json -Depth 16 -Compress | Set-Content -Path $bundlePath -Encoding UTF8
    Write-Host ("  Raw JSON   : {0}" -f $bundlePath) -ForegroundColor DarkGray
}

# --- analyze ---
function LocalTime([int64]$ms) {
    return [DateTimeOffset]::FromUnixTimeMilliseconds($ms).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
}
function LocalMinute([int64]$ms) {
    return [DateTimeOffset]::FromUnixTimeMilliseconds($ms).ToLocalTime().ToString("HH:mm")
}

$ftStats = Get-SeriesRows $raw.ftStats | Select-Object -First 1
$ftMinutes = @(Get-SeriesRows $raw.ftMinute | Sort-Object { [double]$_.max_ms } -Descending)
$instantHits = @(Get-SeriesRows $raw.ftInstant | Sort-Object { [double]$_.instant } -Descending)
$avgSpikes = @(Get-SeriesRows $raw.ftSpikes | Sort-Object { [double]$_.ms } -Descending)
$su = @(Get-SeriesRows $raw.suCore | Sort-Object { [double]$_.max_ms } -Descending)
$invoke = @(Get-SeriesRows $raw.invokeCore | Sort-Object { [double]$_.sum_ms } -Descending)
$console = @(Get-SeriesRows $raw.consoleCore | Sort-Object { [double]$_.max_ms } -Descending)
$aiMinutes = @(Get-SeriesRows $raw.aiMinute | Sort-Object { [double]$_.max_ms } -Descending)
$players = @(Get-SeriesRows $raw.playersMinute)
$npc = @(Get-SeriesRows $raw.npcMinute)
$ent = Get-SeriesRows $raw.entStats | Select-Object -First 1
$cpu = Get-SeriesRows $raw.cpuStats | Select-Object -First 1
$mods = @(Get-SeriesRows $raw.modEvents)

Write-Section "FRAMETIME SUMMARY"
if ($ftStats) {
    Write-Host ("  avg={0:n1} ms  max={1:n1} ms  p95={2:n1}  p99={3:n1}" -f `
        $ftStats.avg_ms, $ftStats.max_ms, $ftStats.p95, $ftStats.p99)
} else {
    Write-Host "  (no frametime data in window)" -ForegroundColor Yellow
}

Write-Section "WORST FRAMETIME MINUTES"
$ftMinutes | Select-Object -First 10 | ForEach-Object {
    Write-Host ("  {0}  avg={1,6:n1}  max={2,6:n1}  p95={3,6:n1}" -f `
        (LocalMinute $_.time), $_.avg_ms, $_.max_ms, $_.p95)
}

Write-Section "INSTANT HITCHES (>200 ms)"
if ($instantHits.Count -eq 0) {
    Write-Host "  none"
} else {
    $instantHits | Select-Object -First 8 | ForEach-Object {
        Write-Host ("  {0}  instant={1:n0} ms  avg={2:n1}" -f (LocalTime $_.time), $_.instant, $_.avg_ms)
    }
}

Write-Section "SERVER_UPDATE TOP BY MAX (core window)"
if ($su.Count -eq 0) {
    Write-Host "  none"
} else {
    $su | Select-Object -First 12 | ForEach-Object {
        Write-Host ("  {0,-28} {1,-22} mean={2,8:n1} max={3,8:n1} sum={4,10:n0}" -f `
            $_.behaviour, $_.method, $_.mean_ms, $_.max_ms, $_.sum_ms)
    }
}

Write-Section "AITHINK WORST MINUTES"
$aiMinutes | Select-Object -First 10 | ForEach-Object {
    Write-Host ("  {0}  mean={1,7:n1}  max={2,7:n1}" -f (LocalMinute $_.time), $_.mean_ms, $_.max_ms)
}

Write-Section "INVOKE TOP BY SUM (core)"
$invoke | Select-Object -First 12 | ForEach-Object {
    Write-Host ("  {0,-45} mean={1,7:n2} max={2,7:n1} sum={3,9:n0}" -f `
        $_.method, $_.mean_ms, $_.max_ms, $_.sum_ms)
}

Write-Section "CONSOLE TOP BY MAX (core)"
if ($console.Count -eq 0) {
    Write-Host "  none"
} else {
    $console | Select-Object -First 8 | ForEach-Object {
        Write-Host ("  {0,-35} mean={1,7:n2} max={2,7:n1} n={3}" -f `
            $_.command, $_.mean_ms, $_.max_ms, $_.n)
    }
}

Write-Section "PLAYERS / NPC (window ends)"
if ($players.Count -gt 0) {
    $p = $players[-1]
    Write-Host ("  last minute {0}: online={1:n0} bots={2:n0} mod={3:n0} grimm={4:n0} zombie={5:n0} van={6:n0}" -f `
        (LocalMinute $p.time), $p.online, $p.bots, $p.bots_mod, $p.grimm, $p.zombie, $p.vanilla)
}
if ($npc.Count -gt 0) {
    $n0 = $npc[0]; $n1 = $npc[-1]
    Write-Host ("  npc start {0}: grimm={1:n0} zombie={2:n0} animals={3:n0}" -f `
        (LocalMinute $n0.time), $n0.grimm, $n0.zombie, $n0.animals)
    Write-Host ("  npc end   {0}: grimm={1:n0} zombie={2:n0} animals={3:n0}" -f `
        (LocalMinute $n1.time), $n1.grimm, $n1.zombie, $n1.animals)
    $grimmDelta = [int]([double]$n1.grimm - [double]$n0.grimm)
    if ([Math]::Abs($grimmDelta) -ge 5) {
        Write-Host ("  Grimm delta over window: {0:+#;-#;0}" -f $grimmDelta) -ForegroundColor Yellow
    }
}
if ($ent) {
    Write-Host ("  entities mean={0:n0} max={1:n0}" -f $ent.ent, $ent.max_ent)
}
if ($cpu) {
    Write-Host ("  cpu mean={0:n0} ms  max={1:n0}  update mean={2:n0} max={3:n0}" -f `
        $cpu.cpu, $cpu.max_cpu, $cpu.upd, $cpu.max_upd)
}

Write-Section "HARMONY MOD EVENTS"
if ($mods.Count -eq 0) {
    Write-Host "  none in window"
} else {
    $mods | ForEach-Object {
        Write-Host ("  {0}  {1,-8} {2}" -f (LocalTime $_.time), $_.event, $_.mod)
    }
}

# --- verdict heuristic ---
Write-Section "VERDICT HINT"
$topSu = $su | Select-Object -First 1
$worstInstant = $instantHits | Select-Object -First 1
$worstAi = $aiMinutes | Select-Object -First 1
$aiHeavy = $topSu -and ($topSu.behaviour -eq "AIThinkManager" -or ($su | Where-Object { $_.behaviour -eq "AIThinkManager" -and [double]$_.max_ms -gt 500 } | Select-Object -First 1))

if ($worstInstant -and [double]$worstInstant.instant -ge 1000) {
    Write-Host ("  Hitch detected: {0} instant={1:n0} ms" -f (LocalTime $worstInstant.time), $worstInstant.instant) -ForegroundColor Red
}
if ($aiHeavy -or ($worstAi -and [double]$worstAi.max_ms -ge 1000)) {
    Write-Host "  Primary suspect: AIThinkManager.ProcessQueue (NPC / Grimm AI load)." -ForegroundColor Yellow
    if ($worstAi) {
        Write-Host ("    Worst AI minute {0}: max={1:n0} ms mean={2:n0}" -f `
            (LocalMinute $worstAi.time), $worstAi.max_ms, $worstAi.mean_ms)
    }
} elseif ($topSu) {
    Write-Host ("  Top server_update by max: {0}.{1} max={2:n0} ms" -f $topSu.behaviour, $topSu.method, $topSu.max_ms)
} else {
    Write-Host "  No strong server_update signal in core window." -ForegroundColor DarkYellow
}

if ($ftStats -and [double]$ftStats.avg_ms -lt 25 -and (-not $worstInstant -or [double]$worstInstant.instant -lt 500)) {
    Write-Host "  Overall window looks mild on average - check instant hitches / specific minutes above." -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
