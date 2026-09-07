#Requires -Version 5.1
<#
.SYNOPSIS
  Pulls Oxide / Harmony plugin hook-time metrics from InfluxDB 1.x (Rust Server Metrics).

.DESCRIPTION
  Rust Server Metrics stores plugin load in measurement "oxide_plugins" with tags server and plugin.
  Fields include avgRunningTime, peakRunningTime, hookTime (see MetricsLogger.OnOxidePluginMetrics).
  This is a CPU *proxy* (time in hooks), not true CPU% or per-plugin RAM — server RAM is in the "memory" measurement (aggregate only).

.PARAMETER ConfigPath
  Path to HarmonyMods ServerMetrics Configuration.json (defaults: four levels above this script -> !RustServer/HarmonyMods_Data/ServerMetrics/Configuration.json).

.PARAMETER Hours
  Time window ending at now (default 1).

.PARAMETER ServerTag
  Overrides Configuration.json "Server Tag" when set.

.EXAMPLE
  .\Get-PluginResourceReport.ps1
  .\Get-PluginResourceReport.ps1 -Hours 6 -ServerTag "testserver"
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

function Normalize-PluginTag {
    param([string]$s)
    if ($null -eq $s) { return "" }
    $t = $s.Trim()
    # Influx/JSON sometimes returns plugin tag with extra quote characters; strip repeatedly.
    while ($t.Length -ge 2 -and $t.StartsWith('"') -and $t.EndsWith('"')) {
        $t = $t.Substring(1, $t.Length - 2).Trim()
    }
    $t = $t.Trim([char[]]@('"', "'"))
    return $t
}

function Format-InvariantNumber {
    param($v)
    if ($null -eq $v) { return "" }
    try {
        return ([double]$v).ToString([System.Globalization.CultureInfo]::InvariantCulture)
    } catch {
        return [string]$v
    }
}

function Escape-CsvValue {
    param([string]$f)
    if ($null -eq $f) { return "" }
    if ($f.IndexOfAny([char[]]@( ',', '"', "`r", "`n" )) -ge 0) {
        return '"' + ($f.Replace('"', '""')) + '"'
    }
    return $f
}

function Write-PluginReportFiles {
    param(
        [System.Collections.IEnumerable]$Rows,
        [string]$BasePathWithoutExtension
    )
    $utf8Bom = New-Object System.Text.UTF8Encoding $true
    $csvPath = "$BasePathWithoutExtension.csv"
    $tsvPath = "$BasePathWithoutExtension.tsv"
    $swCsv = New-Object System.IO.StreamWriter($csvPath, $false, $utf8Bom)
    $swTsv = New-Object System.IO.StreamWriter($tsvPath, $false, $utf8Bom)
    try {
        $swCsv.WriteLine("plugin,mean_avg_ms,peak_ms,last_hook")
        $swTsv.WriteLine("plugin`tmean_avg_ms`tpeak_ms`tlast_hook")
        foreach ($r in $Rows) {
            $pCsv = Escape-CsvValue([string]$r.plugin)
            $pTsv = [string]$r.plugin -replace "`t", " " -replace "`r", " " -replace "`n", " "
            $m = Format-InvariantNumber $r.mean_avg_ms
            $pk = Format-InvariantNumber $r.peak_ms
            $h = Format-InvariantNumber $r.last_hook
            $swCsv.WriteLine("$pCsv,$m,$pk,$h")
            $swTsv.WriteLine("$pTsv`t$m`t$pk`t$h")
        }
    } finally {
        $swCsv.Dispose()
        $swTsv.Dispose()
    }
    return @{ Csv = $csvPath; Tsv = $tsvPath }
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
    $resp = Invoke-RestMethod @params
    return $resp
}

function Convert-InfluxSeriesToRows {
    param($InfluxResponse)
    $rows = [System.Collections.Generic.List[object]]::new()
    if (-not $InfluxResponse.results) { return $rows }
    foreach ($res in $InfluxResponse.results) {
        if (-not $res.series) { continue }
        foreach ($ser in $res.series) {
            $plugin = $null
            if ($ser.tags -and $ser.tags.plugin) {
                $plugin = Normalize-PluginTag ([string]$ser.tags.plugin)
            }
            if (-not $ser.columns -or -not $ser.values) { continue }
            $colNames = @($ser.columns)
            foreach ($valArr in $ser.values) {
                $row = [ordered]@{ plugin = $plugin }
                for ($i = 0; $i -lt [Math]::Min($colNames.Count, $valArr.Count); $i++) {
                    $row[$colNames[$i]] = $valArr[$i]
                }
                $rows.Add([pscustomobject]$row)
            }
        }
    }
    return $rows
}

# --- resolve config ---
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

Write-Host ""
Write-Host "Influx: $InfluxUrl  |  db=$Database  |  server=$ServerTag  |  window=now-$dur" -ForegroundColor Cyan
Write-Host ""

# --- queries ---
$where = "time >= now() - $dur AND `"server`" = '$tagEsc'"

$qMean = "SELECT mean(`"avgRunningTime`") AS mean_avg_ms FROM `"oxide_plugins`" WHERE $where GROUP BY `"plugin`""
$qPeak = "SELECT max(`"peakRunningTime`") AS peak_ms FROM `"oxide_plugins`" WHERE $where GROUP BY `"plugin`""
$qHook = "SELECT last(`"hookTime`") AS last_hook FROM `"oxide_plugins`" WHERE $where GROUP BY `"plugin`""

try {
    $rMean = Invoke-InfluxQuery -BaseUrl $InfluxUrl -Db $Database -InfluxQl $qMean -U $User -P $Password
    $rPeak = Invoke-InfluxQuery -BaseUrl $InfluxUrl -Db $Database -InfluxQl $qPeak -U $User -P $Password
    $rHook = Invoke-InfluxQuery -BaseUrl $InfluxUrl -Db $Database -InfluxQl $qHook -U $User -P $Password
} catch {
    Write-Host "Influx query failed: $($_.Exception.Message)" -ForegroundColor Red
    throw
}

$meanRows = Convert-InfluxSeriesToRows $rMean
$peakRows = Convert-InfluxSeriesToRows $rPeak
$hookRows = Convert-InfluxSeriesToRows $rHook

if ($meanRows.Count -eq 0 -and $peakRows.Count -eq 0 -and $hookRows.Count -eq 0) {
    Write-Host "No series returned. Check server tag, database name, and that the Rust server has been writing metrics in this window." -ForegroundColor Yellow
    exit 1
}

$byPlugin = @{}
foreach ($r in $meanRows) {
    $pk = Normalize-PluginTag ([string]$r.plugin)
    if (-not $pk) { continue }
    $byPlugin[$pk] = [ordered]@{ plugin = $pk; mean_avg_ms = $r.mean_avg_ms }
}
foreach ($r in $peakRows) {
    $pk = Normalize-PluginTag ([string]$r.plugin)
    if (-not $pk) { continue }
    if (-not $byPlugin.ContainsKey($pk)) { $byPlugin[$pk] = [ordered]@{ plugin = $pk } }
    $byPlugin[$pk].peak_ms = $r.peak_ms
}
foreach ($r in $hookRows) {
    $pk = Normalize-PluginTag ([string]$r.plugin)
    if (-not $pk) { continue }
    if (-not $byPlugin.ContainsKey($pk)) { $byPlugin[$pk] = [ordered]@{ plugin = $pk } }
    $byPlugin[$pk].last_hook = $r.last_hook
}

$merged = @($byPlugin.Values | ForEach-Object { [pscustomobject]$_ })

# numeric sort: mean_avg_ms descending (nulls last)
$sorted = $merged | Sort-Object @{
    Expression = {
        $v = $_.mean_avg_ms
        if ($null -eq $v) { return [double]::NegativeInfinity }
        return [double]$v
    }
    Descending = $true
}

Write-Host "--- Plugin hook cost (higher = more time in Harmony hooks in this window) ---" -ForegroundColor Green
Write-Host "mean(avgRunningTime) = rolling avg of per-tick hook deltas after startup init (ms)." -ForegroundColor DarkGray
Write-Host "peak_ms = max peakRunningTime in window. last_hook = cumulative Harmony hook time (or 1 for Harmony-only 'loaded' ping)." -ForegroundColor DarkGray
Write-Host ""

$sorted | Format-Table -AutoSize plugin, mean_avg_ms, peak_ms, last_hook

$outDir = Join-Path $PSScriptRoot "reports"
if (-not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Path $outDir | Out-Null
}
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$base = Join-Path $outDir "plugin-metrics-$stamp"
$written = Write-PluginReportFiles -Rows $sorted -BasePathWithoutExtension $base
Write-Host ""
Write-Host "CSV (UTF-8 BOM, Excel-friendly): $($written.Csv)" -ForegroundColor Cyan
Write-Host "TSV (tab-separated, good for Notepad++): $($written.Tsv)" -ForegroundColor Cyan
