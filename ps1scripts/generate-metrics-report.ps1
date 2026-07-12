# Influx Metrics Report Generator for Rust Server
# Pulls high-cost plugin and server hook data from InfluxDB and formats a report

param(
    [int]$LookbackMinutes = 30,
    [int]$TopLimit = 10,
    [string[]]$IncludePlugins = @(),  # Force include specific plugins even if not in top N
    [string]$OutputPath = "",
    [string]$ConfigPath = "",
    [string]$InfluxUrl = "",
    [string]$DatabaseName = "",
    [string]$DatabaseUser = "",
    [string]$DatabasePassword = "",
    [string]$ServerTag = ""
)

$ErrorActionPreference = "Stop"

function Get-ConfigObject {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        return $null
    }
    try {
        return Get-Content $Path -Raw | ConvertFrom-Json
    } catch {
        Write-Warning "Failed to parse configuration at $Path : $($_.Exception.Message)"
        return $null
    }
}

function Resolve-ConfigValues {
    param(
        [string]$ConfigPathCandidate,
        [ref]$InfluxUrlRef,
        [ref]$DatabaseNameRef,
        [ref]$DatabaseUserRef,
        [ref]$DatabasePasswordRef,
        [ref]$ServerTagRef
    )

    if (-not $ConfigPathCandidate) {
        return
    }
    $configObject = Get-ConfigObject -Path $ConfigPathCandidate
    if ($null -eq $configObject) {
        return
    }

    if (-not $InfluxUrlRef.Value -and $configObject.'Influx Database Url') {
        $InfluxUrlRef.Value = $configObject.'Influx Database Url'
    }
    if (-not $DatabaseNameRef.Value -and $configObject.'Influx Database Name') {
        $DatabaseNameRef.Value = $configObject.'Influx Database Name'
    }
    if (-not $DatabaseUserRef.Value -and $configObject.'Influx Database User') {
        $DatabaseUserRef.Value = $configObject.'Influx Database User'
    }
    if (-not $DatabasePasswordRef.Value -and $configObject.'Influx Database Password') {
        $DatabasePasswordRef.Value = $configObject.'Influx Database Password'
    }
    if (-not $ServerTagRef.Value -and $configObject.'Server Tag') {
        $ServerTagRef.Value = $configObject.'Server Tag'
    }
}

function Invoke-InfluxQuery {
    param(
        [string]$Query,
        [string]$Endpoint,
        [string]$Database,
        [string]$User,
        [string]$Password
    )

    # Use Basic Auth headers for InfluxDB 1.8
    $base64Auth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${User}:${Password}"))
    $headers = @{
        "Authorization" = "Basic $base64Auth"
    }

    # Build form-encoded body string manually
    $bodyParams = @(
        "db=$([System.Uri]::EscapeDataString($Database))",
        "q=$([System.Uri]::EscapeDataString($Query))",
        "epoch=ms"
    )
    $bodyString = $bodyParams -join "&"

    try {
        $response = Invoke-RestMethod -Uri $Endpoint -Method Post -Headers $headers -Body $bodyString -ContentType "application/x-www-form-urlencoded"
        if ($response.results -and $response.results[0].error) {
            $errorMsg = $response.results[0].error
            throw "InfluxDB error: $errorMsg"
        }
        return $response.results
    } catch {
        $errorDetail = $_.Exception.Message
        if ($_.ErrorDetails) {
            $errorDetail += " - $($_.ErrorDetails.Message)"
        }
        if ($_.Exception.Response) {
            try {
                $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
                $responseBody = $reader.ReadToEnd()
                $errorDetail += " - Response: $responseBody"
            } catch {
                # Ignore if we can't read the response
            }
        }
        throw "Influx query failed: $errorDetail"
    }
}

function Get-GroupedTotals {
    param(
        [string]$Measurement,
        [string]$Field,
        [string[]]$GroupByTags,
        [int]$LookbackMinutes,
        [string]$ServerTag,
        [string]$Endpoint,
        [string]$Database,
        [string]$User,
        [string]$Password
    )

    # Build the GROUP BY clause if needed
    $groupByClause = ""
    if ($GroupByTags -and $GroupByTags.Count -gt 0) {
        # InfluxDB GROUP BY uses tag names directly (no quotes for tags)
        $groupByClause = " GROUP BY " + ($GroupByTags -join ",")
    }

    # Build query using explicit string concatenation to avoid variable expansion issues
    # In InfluxDB, field names in aggregate functions should be quoted to avoid conflicts with keywords
    # "duration" is a common field name that might conflict, so we'll quote all field names
    $quotedField = '"' + $Field + '"'
    $queryParts = @()
    $queryParts += "SELECT sum(" + $quotedField + ")"
    $queryParts += "FROM " + $Measurement
    $queryParts += "WHERE time > now() - " + $LookbackMinutes + "m"
    $queryParts += "AND server='" + $ServerTag + "'"
    if ($groupByClause) {
        $queryParts += $groupByClause
    }
    $query = $queryParts -join " "
    
    Write-Host "    Query: $query" -ForegroundColor DarkGray
    $results = Invoke-InfluxQuery -Query $query -Endpoint $Endpoint -Database $Database -User $User -Password $Password

    $items = New-Object System.Collections.Generic.List[object]
    $debugDumped = $false
    foreach ($result in $results) {
        if (-not $result.series) {
            continue
        }

        foreach ($series in $result.series) {
            $columns = $series.columns
            
            # Debug: dump first series structure once
            if (-not $debugDumped -and $series.values.Count -gt 0) {
                Write-Host "      Debug - Columns: $($columns -join ', ')" -ForegroundColor DarkGray
                Write-Host "      Debug - First row: $($series.values[0] -join ', ')" -ForegroundColor DarkGray
                Write-Host "      Debug - Tags: $($series.tags | ConvertTo-Json -Compress)" -ForegroundColor DarkGray
                $debugDumped = $true
            }
            # InfluxDB returns column names - find the sum column
            # Column name could be "sum", "sum_hookTime", "sum_duration", etc.
            $sumIndex = -1
            for ($i = 0; $i -lt $columns.Count; $i++) {
                $colName = $columns[$i]
                # Check for sum column (could be exact "sum" or prefixed like "sum_<field>")
                if ($colName -eq "sum" -or $colName -like "sum_*" -or $colName -like "*sum*") {
                    $sumIndex = $i
                    break
                }
            }
            
            # If not found, try to find any numeric column that's not "time"
            if ($sumIndex -lt 0) {
                for ($i = 0; $i -lt $columns.Count; $i++) {
                    if ($columns[$i] -ne "time") {
                        $sumIndex = $i
                        break
                    }
                }
            }
            
            if ($sumIndex -lt 0) {
                Write-Host "      Warning: No sum column found. Columns: $($columns -join ', ')" -ForegroundColor Yellow
                continue
            }

            # With GROUP BY, each series represents one group
            # The sum value should be in the first row (usually only one row per group)
            $total = 0
            if ($series.values -and $series.values.Count -gt 0) {
                foreach ($row in $series.values) {
                    if ($row -and $row.Count -gt $sumIndex) {
                        $value = $row[$sumIndex]
                        if ($null -ne $value -and $value -ne "") {
                            try {
                                $numValue = [double]$value
                                $total += $numValue
                            } catch {
                                # Try parsing as string first
                                $strValue = [string]$value
                                if ([double]::TryParse($strValue, [ref]$numValue)) {
                                    $total += $numValue
                                } else {
                                    Write-Host "      Warning: Could not parse value '$value' (type: $($value.GetType().Name))" -ForegroundColor Yellow
                                }
                            }
                        }
                    }
                }
            } else {
                # Debug: show what we got
                if ($series.values.Count -eq 0) {
                    Write-Host "      Warning: Empty values array for series with tags: $($series.tags | ConvertTo-Json -Compress)" -ForegroundColor Yellow
                }
            }

            # Include items even if total is 0 (might be valid data)
            # Only skip if we couldn't extract any value at all
            if ($total -eq 0 -and $series.values.Count -eq 0) {
                continue
            }

            $items.Add([PSCustomObject]@{
                    TotalMs = $total
                    Tags    = $series.tags
                })
        }
    }
    
    Write-Host "      Found $($items.Count) items (with values > 0: $(($items | Where-Object { $_.TotalMs -gt 0 }).Count))" -ForegroundColor DarkGray

    return $items
}

function Get-GroupedStats {
    param(
        [string]$Measurement,
        [string]$Field,
        [string[]]$GroupByTags,
        [int]$LookbackMinutes,
        [string]$ServerTag,
        [string]$Endpoint,
        [string]$Database,
        [string]$User,
        [string]$Password
    )

    # Build the GROUP BY clause if needed
    $groupByClause = ""
    if ($GroupByTags -and $GroupByTags.Count -gt 0) {
        $groupByClause = " GROUP BY " + ($GroupByTags -join ",")
    }

    $quotedField = '"' + $Field + '"'
    $queryParts = @()
    $queryParts += "SELECT sum(" + $quotedField + ") AS total, mean(" + $quotedField + ") AS avg, max(" + $quotedField + ") AS peak, count(" + $quotedField + ") AS count"
    $queryParts += "FROM " + $Measurement
    $queryParts += "WHERE time > now() - " + $LookbackMinutes + "m"
    $queryParts += "AND server='" + $ServerTag + "'"
    if ($groupByClause) {
        $queryParts += $groupByClause
    }
    $query = $queryParts -join " "
    
    $results = Invoke-InfluxQuery -Query $query -Endpoint $Endpoint -Database $Database -User $User -Password $Password

    $items = New-Object System.Collections.Generic.List[object]
    foreach ($result in $results) {
        if (-not $result.series) {
            continue
        }

        foreach ($series in $result.series) {
            $columns = $series.columns
            $total = 0
            $avg = 0
            $peak = 0
            $count = 0
            
            if ($series.values -and $series.values.Count -gt 0) {
                foreach ($row in $series.values) {
                    for ($i = 0; $i -lt $columns.Count; $i++) {
                        $colName = $columns[$i]
                        $value = $row[$i]
                        if ($null -eq $value -or $value -eq "") { continue }
                        
                        try {
                            $numValue = [double]$value
                            if ($colName -eq "total" -or $colName -like "*sum*") {
                                $total = $numValue
                            } elseif ($colName -eq "avg" -or $colName -like "*mean*") {
                                $avg = $numValue
                            } elseif ($colName -eq "peak" -or $colName -like "*max*") {
                                $peak = $numValue
                            } elseif ($colName -eq "count") {
                                $count = $numValue
                            }
                        } catch {
                            $strValue = [string]$value
                            if ([double]::TryParse($strValue, [ref]$numValue)) {
                                if ($colName -eq "total" -or $colName -like "*sum*") {
                                    $total = $numValue
                                } elseif ($colName -eq "avg" -or $colName -like "*mean*") {
                                    $avg = $numValue
                                } elseif ($colName -eq "peak" -or $colName -like "*max*") {
                                    $peak = $numValue
                                } elseif ($colName -eq "count") {
                                    $count = $numValue
                                }
                            }
                        }
                    }
                }
            }

            if ($total -eq 0 -and $series.values.Count -eq 0) {
                continue
            }

            $items.Add([PSCustomObject]@{
                    TotalMs = $total
                    AvgMs = $avg
                    PeakMs = $peak
                    CallCount = [int]$count
                    Tags    = $series.tags
                })
        }
    }
    
    return $items
}

function Get-StatResult {
    param(
        [string]$Query,
        [string]$Endpoint,
        [string]$Database,
        [string]$User,
        [string]$Password
    )

    $results = Invoke-InfluxQuery -Query $Query -Endpoint $Endpoint -Database $Database -User $User -Password $Password
    foreach ($result in $results) {
        if (-not $result.series) {
            continue
        }
        foreach ($series in $result.series) {
            $obj = @{}
            $columns = $series.columns
            foreach ($row in $series.values) {
                for ($i = 0; $i -lt $columns.Count; $i++) {
                    $name = $columns[$i]
                    if ($name -eq "time") {
                        continue
                    }
                    $obj[$name] = $row[$i]
                }
            }
            return $obj
        }
    }
    return @{}
}

function Build-TableLines {
    param(
        [string]$Title,
        [System.Collections.IEnumerable]$Rows,
        [array]$Columns,
        [System.Text.StringBuilder]$Builder
    )

    $Builder.AppendLine() | Out-Null
    $Builder.AppendLine("== $Title ==") | Out-Null

    if (-not $Rows -or $Rows.Count -eq 0) {
        $Builder.AppendLine("No datapoints were returned.") | Out-Null
        return
    }

    $header = ($Columns | ForEach-Object { $_.Header }) -join " | "
    $Builder.AppendLine($header) | Out-Null
    $divider = ($Columns | ForEach-Object { "-" * [Math]::Max(3, $_.Header.Length) }) -join "-|-"
    $Builder.AppendLine($divider) | Out-Null

    $rowCount = 0
    foreach ($row in $Rows) {
        $lineValues = @()
        foreach ($column in $Columns) {
            $propName = $column.Property
            # Access property - try multiple methods
            $value = if ($row.PSObject.Properties[$propName]) {
                $row.PSObject.Properties[$propName].Value
            } elseif ($row.$propName) {
                $row.$propName
            } else {
                $null
            }
            
            # Debug first row
            if ($rowCount -eq 0 -and $propName -eq "TotalMs") {
                Write-Host "      Debug - Row has TotalMs property: $($row.PSObject.Properties['TotalMs'] -ne $null), Value: $value" -ForegroundColor DarkGray
            }
            
            if ($null -eq $value) {
                $value = ""
            }
            
            if ($column.Format) {
                try {
                    # Format the value - the scriptblock receives $_ from pipeline
                    $formattedValue = $value | ForEach-Object { & $column.Format $_ }
                    $lineValues += $formattedValue
                } catch {
                    # If formatting fails, convert to string
                    $lineValues += [string]$value
                }
            } else {
                $lineValues += [string]$value
            }
        }
        $Builder.AppendLine(($lineValues -join " | ")) | Out-Null
        $rowCount++
    }
}

if ($LookbackMinutes -lt 1) {
    throw "LookbackMinutes must be at least 1."
}

$configSearchPaths = @()
if ($ConfigPath) {
    $configSearchPaths += $ConfigPath
}

# Build list of potential config file paths
# Script is at: .cursor\HarmonyMods\ps1scripts\generate-metrics-report.ps1
# Config is at: HarmonyMods_Data\ServerMetrics\Configuration.json (workspace root)
$potentialPaths = @(
    # Go up 3 levels from ps1scripts to workspace root
    (Join-Path $PSScriptRoot "..\..\..\HarmonyMods_Data\ServerMetrics\Configuration.json"),
    # Go up 2 levels (in case structure is different)
    (Join-Path $PSScriptRoot "..\..\HarmonyMods_Data\ServerMetrics\Configuration.json"),
    # Relative to script directory
    (Join-Path $PSScriptRoot "HarmonyMods_Data\ServerMetrics\Configuration.json"),
    # Relative to current working directory
    "HarmonyMods_Data\ServerMetrics\Configuration.json",
    # Absolute path using Split-Path (chained to go up 3 levels)
    (Join-Path (Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent) "HarmonyMods_Data\ServerMetrics\Configuration.json")
)

# Resolve paths that exist
foreach ($potentialPath in $potentialPaths) {
    try {
        $resolvedPath = Resolve-Path $potentialPath -ErrorAction Stop
        if ($resolvedPath -and (Test-Path $resolvedPath)) {
            $configSearchPaths += $resolvedPath.Path
        }
    } catch {
        # Path doesn't exist, skip it
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

Write-Host "Connecting to InfluxDB..." -ForegroundColor Yellow
Write-Host "  URL: $queryEndpoint" -ForegroundColor Gray
Write-Host "  Database: $DatabaseName" -ForegroundColor Gray
Write-Host "  User: $DatabaseUser" -ForegroundColor Gray
Write-Host "  Server Tag: $ServerTag" -ForegroundColor Gray
Write-Host ""

# Test connection with a simple query
Write-Host "Testing connection..." -ForegroundColor Yellow
try {
    # Test with a simple query that doesn't need a database
    $base64Auth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${DatabaseUser}:${DatabasePassword}"))
    $headers = @{
        "Authorization" = "Basic $base64Auth"
    }
    $testBody = "q=" + [System.Uri]::EscapeDataString("SHOW DATABASES")
    $testResponse = Invoke-RestMethod -Uri $queryEndpoint -Method Post -Headers $headers -Body $testBody -ContentType "application/x-www-form-urlencoded"
    Write-Host "✓ Connection successful" -ForegroundColor Green
} catch {
    Write-Host "✗ Connection test failed: $_" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please verify:" -ForegroundColor Yellow
    Write-Host "  1. InfluxDB is running (check: http://localhost:8086/debug/vars)" -ForegroundColor Gray
    Write-Host "  2. Credentials in Configuration.json are correct" -ForegroundColor Gray
    Write-Host "  3. Database '$DatabaseName' exists" -ForegroundColor Gray
    Write-Host "  4. User '$DatabaseUser' has read permissions" -ForegroundColor Gray
    exit 1
}

if (-not $OutputPath) {
    $timestamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
    $OutputPath = Join-Path $PSScriptRoot "metrics-report_$timestamp.txt"
}

Write-Host "Generating report for last $LookbackMinutes minutes..." -ForegroundColor Yellow
Write-Host ""

# Test with a simple query first to verify data exists
Write-Host "Testing data availability..." -ForegroundColor Yellow
try {
    $testQuery = "SELECT COUNT(*) FROM oxide_plugins WHERE time > now() - ${LookbackMinutes}m AND server='$ServerTag' LIMIT 1"
    $testResult = Invoke-InfluxQuery -Query $testQuery -Endpoint $queryEndpoint -Database $DatabaseName -User $DatabaseUser -Password $DatabasePassword
    Write-Host "✓ Data found for server tag '$ServerTag'" -ForegroundColor Green
} catch {
    Write-Host "⚠ Warning: Could not verify data for server tag '$ServerTag': $_" -ForegroundColor Yellow
    Write-Host "Continuing anyway..." -ForegroundColor Gray
}
Write-Host ""

$reportBuilder = New-Object System.Text.StringBuilder
$reportBuilder.AppendLine("Rust Server Influx Metrics Report") | Out-Null
$reportBuilder.AppendLine("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')") | Out-Null
$reportBuilder.AppendLine("Lookback Window: $LookbackMinutes minutes") | Out-Null
$reportBuilder.AppendLine("Server Tag: $ServerTag") | Out-Null
$reportBuilder.AppendLine() | Out-Null

Write-Host "Querying plugin metrics..." -ForegroundColor Yellow
# Query hookTime (backward compatible, includes initialization)
$pluginTotals = Get-GroupedTotals -Measurement "oxide_plugins" -Field "hookTime" -GroupByTags @("plugin") -LookbackMinutes $LookbackMinutes -ServerTag $ServerTag -Endpoint $queryEndpoint -Database $DatabaseName -User $DatabaseUser -Password $DatabasePassword

# Query new runtime metrics (excluding initialization)
Write-Host "Querying runtime metrics (avgRunningTime)..." -ForegroundColor DarkGray
$pluginRuntimeAvgs = Get-GroupedStats -Measurement "oxide_plugins" -Field "avgRunningTime" -GroupByTags @("plugin") -LookbackMinutes $LookbackMinutes -ServerTag $ServerTag -Endpoint $queryEndpoint -Database $DatabaseName -User $DatabaseUser -Password $DatabasePassword

# Query peak runtime metrics
Write-Host "Querying peak runtime metrics..." -ForegroundColor DarkGray
$pluginPeakRuntimes = Get-GroupedStats -Measurement "oxide_plugins" -Field "peakRunningTime" -GroupByTags @("plugin") -LookbackMinutes $LookbackMinutes -ServerTag $ServerTag -Endpoint $queryEndpoint -Database $DatabaseName -User $DatabaseUser -Password $DatabasePassword

# Query initialization times (reported once per plugin)
# Note: InfluxDB 1.x doesn't support IS NOT NULL, so we query all and filter in PowerShell
Write-Host "Querying initialization times..." -ForegroundColor DarkGray
$initTimeQuery = "SELECT last(`"initTime`") AS last_init FROM `"oxide_plugins`" WHERE time > now() - ${LookbackMinutes}m AND server='$ServerTag' GROUP BY `"plugin`""
$initTimeResults = Invoke-InfluxQuery -Query $initTimeQuery -Endpoint $queryEndpoint -Database $DatabaseName -User $DatabaseUser -Password $DatabasePassword
$initTimes = @{}
foreach ($result in $initTimeResults) {
    if ($result.series) {
        foreach ($series in $result.series) {
            if (-not $series.tags -or -not $series.tags.plugin) {
                continue
            }
            $pluginName = $series.tags.plugin -replace '^"|"$', '' -replace '\\"', '"'
            if ($series.values -and $series.values.Count -gt 0) {
                $columns = $series.columns
                # Find the last_init column index
                $lastInitIndex = -1
                for ($i = 0; $i -lt $columns.Count; $i++) {
                    if ($columns[$i] -eq "last_init" -or $columns[$i] -like "*last_init*") {
                        $lastInitIndex = $i
                        break
                    }
                }
                # If not found, try index 1 (time is usually 0)
                if ($lastInitIndex -lt 0 -and $columns.Count -gt 1) {
                    $lastInitIndex = 1
                }
                if ($lastInitIndex -ge 0) {
                    $initValue = $series.values[0][$lastInitIndex]
                    # Filter out null/empty values (InfluxDB 1.x doesn't support IS NOT NULL in WHERE)
                    if ($null -ne $initValue -and $initValue -ne "" -and $initValue -ne 0) {
                        try {
                            $parsedValue = [double]$initValue
                            if ($parsedValue -gt 0) {
                                $initTimes[$pluginName] = $parsedValue
                            }
                        } catch {
                            # Skip if can't parse
                        }
                    }
                }
            }
        }
    }
}

# Build comprehensive plugin data with all metrics
$allPlugins = @($pluginTotals |
    Where-Object { $_.TotalMs -gt 0 } |
    ForEach-Object {
        $pluginName = $_.Tags.plugin -replace '^"|"$', '' -replace '\\"', '"'
        $totalMs = [double]$_.TotalMs
        
        # Find matching runtime stats
        $runtimeStats = $pluginRuntimeAvgs | Where-Object { ($_.Tags.plugin -replace '^"|"$', '' -replace '\\"', '"') -eq $pluginName } | Select-Object -First 1
        $peakStats = $pluginPeakRuntimes | Where-Object { ($_.Tags.plugin -replace '^"|"$', '' -replace '\\"', '"') -eq $pluginName } | Select-Object -First 1
        
        $avgRuntimeMs = if ($runtimeStats) { [double]$runtimeStats.AvgMs } else { 0 }
        $peakRuntimeMs = if ($peakStats) { [double]$peakStats.PeakMs } else { 0 }
        $initTimeMs = if ($initTimes.ContainsKey($pluginName)) { $initTimes[$pluginName] } else { 0 }
        
        [PSCustomObject]@{
            Plugin        = $pluginName
            TotalMs       = [math]::Round($totalMs, 2)
            TotalSeconds  = [math]::Round($totalMs / 1000, 2)
            AvgMsPerMin   = [math]::Round($totalMs / $LookbackMinutes, 2)
            InitTimeMs    = [math]::Round($initTimeMs, 2)
            InitTimeSec   = [math]::Round($initTimeMs / 1000, 2)
            AvgRuntimeMs  = [math]::Round($avgRuntimeMs, 2)
            PeakRuntimeMs = [math]::Round($peakRuntimeMs, 2)
        }
    } | Sort-Object -Property TotalMs -Descending)

Write-Host "      Found $($allPlugins.Count) total plugins with data" -ForegroundColor DarkGray

# Calculate total plugin time for percentages (exclude HarmonyMods which are just status indicators)
$knownHarmonyMods = @("GrimmNPC", "NoActiveItemDrop", "NoGibs", "RustServerMetrics", "SafeZonePVE", "UnlockTier1", "Vanish")
$oxidePluginsOnly = @($allPlugins | Where-Object { $knownHarmonyMods -notcontains $_.Plugin })
$totalPluginTime = ($oxidePluginsOnly | Measure-Object -Property TotalMs -Sum).Sum

# Add percentages to all plugins
$allPlugins = @($allPlugins | ForEach-Object {
    $pct = if ($totalPluginTime -gt 0) { [math]::Round(($_.TotalMs / $totalPluginTime) * 100, 2) } else { 0 }
    $_ | Add-Member -MemberType NoteProperty -Name "PercentOfTotal" -Value $pct -Force
    $_
})

# Check for known HarmonyMods
$harmonyModsFound = @($allPlugins | Where-Object { $knownHarmonyMods -contains $_.Plugin })
if ($harmonyModsFound.Count -gt 0) {
    Write-Host "      HarmonyMods found: $($harmonyModsFound.Count)" -ForegroundColor Cyan
    foreach ($hm in $harmonyModsFound) {
        Write-Host "        - $($hm.Plugin): $($hm.TotalMs) ms (rank: $(($allPlugins.IndexOf($hm) + 1)))" -ForegroundColor Cyan
        Write-Host "          Note: HarmonyMods show 'loaded status' (hookTime=1), not actual CPU usage" -ForegroundColor DarkGray
    }
} else {
    Write-Host "      ⚠ No known HarmonyMods found in data!" -ForegroundColor Yellow
    Write-Host "      This could mean:" -ForegroundColor Yellow
    Write-Host "        1. HarmonyMod tracking is not working" -ForegroundColor Yellow
    Write-Host "        2. HarmonyMods are not loaded" -ForegroundColor Yellow
    Write-Host "        3. Data hasn't been collected yet" -ForegroundColor Yellow
}

# Get top plugins for report (exclude HarmonyMods from main list, they have their own section)
$topPlugins = @($oxidePluginsOnly | Select-Object -First $TopLimit)

# Force include specific plugins if requested
$pluginRows = @($topPlugins)
if ($IncludePlugins -and $IncludePlugins.Count -gt 0) {
    Write-Host "      Force including plugins: $($IncludePlugins -join ', ')" -ForegroundColor Cyan
    foreach ($pluginToInclude in $IncludePlugins) {
        $pluginToInclude = $pluginToInclude.Trim()
        # Try exact match first
        $foundPlugin = $allPlugins | Where-Object { $_.Plugin -eq $pluginToInclude } | Select-Object -First 1
        # If not found, try case-insensitive match
        if (-not $foundPlugin) {
            $foundPlugin = $allPlugins | Where-Object { $_.Plugin -ieq $pluginToInclude } | Select-Object -First 1
        }
        # If still not found, try partial match (e.g., "VehicleLicence" matches "VehicleLicence.cs")
        if (-not $foundPlugin) {
            $foundPlugin = $allPlugins | Where-Object { $_.Plugin -like "*$pluginToInclude*" } | Select-Object -First 1
        }
        
            if ($foundPlugin) {
                # Only add if not already in the list
                if ($pluginRows | Where-Object { $_.Plugin -eq $foundPlugin.Plugin }) {
                    Write-Host "        - $($foundPlugin.Plugin)`: Already in top $TopLimit" -ForegroundColor DarkGray
                } else {
                    $pluginRows += $foundPlugin
                    Write-Host "        - $($foundPlugin.Plugin)`: Added to report" -ForegroundColor Green
                }
            } else {
                Write-Host "        - ${pluginToInclude}: Not found in metrics data" -ForegroundColor Yellow
            }
    }
    # Re-sort by TotalMs after adding forced plugins
    $pluginRows = @($pluginRows | Sort-Object -Property TotalMs -Descending)
}

Write-Host "      Showing $($pluginRows.Count) Oxide plugins in report (total: $([math]::Round($totalPluginTime, 2)) ms)" -ForegroundColor DarkGray

$invokeTotals = Get-GroupedTotals -Measurement "invoke_execution" -Field "duration" -GroupByTags @("behaviour","method") -LookbackMinutes $LookbackMinutes -ServerTag $ServerTag -Endpoint $queryEndpoint -Database $DatabaseName -User $DatabaseUser -Password $DatabasePassword
$invokeRows = @($invokeTotals |
    Where-Object { $_.TotalMs -gt 0 } |
    Sort-Object -Property TotalMs -Descending |
    Select-Object -First $TopLimit |
    ForEach-Object {
        $behaviour = ($_.Tags.behaviour -replace '^"|"$', '' -replace '\\"', '"')
        $method = ($_.Tags.method -replace '^"|"$', '' -replace '\\"', '"')
        $totalMs = [double]$_.TotalMs
        [PSCustomObject]@{
            Hook         = "$behaviour::$method"
            TotalMs      = [math]::Round($totalMs, 2)
            TotalSeconds = [math]::Round($totalMs / 1000, 2)
            AvgMsPerMin  = [math]::Round($totalMs / $LookbackMinutes, 2)
        }
    })

$behaviourTotals = Get-GroupedTotals -Measurement "invoke_execution" -Field "duration" -GroupByTags @("behaviour") -LookbackMinutes $LookbackMinutes -ServerTag $ServerTag -Endpoint $queryEndpoint -Database $DatabaseName -User $DatabaseUser -Password $DatabasePassword
$behaviourRows = @($behaviourTotals |
    Where-Object { $_.TotalMs -gt 0 } |
    Sort-Object -Property TotalMs -Descending |
    Select-Object -First $TopLimit |
    ForEach-Object {
        $behaviour = ($_.Tags.behaviour -replace '^"|"$', '' -replace '\\"', '"')
        $totalMs = [double]$_.TotalMs
        [PSCustomObject]@{
            Behaviour    = $behaviour
            TotalMs      = [math]::Round($totalMs, 2)
            TotalSeconds = [math]::Round($totalMs / 1000, 2)
        }
    })

$workQueueTotals = Get-GroupedTotals -Measurement "work_queue" -Field "duration" -GroupByTags @("behaviour","method") -LookbackMinutes $LookbackMinutes -ServerTag $ServerTag -Endpoint $queryEndpoint -Database $DatabaseName -User $DatabaseUser -Password $DatabasePassword
$workQueueRows = @($workQueueTotals |
    Where-Object { $_.TotalMs -gt 0 } |
    Sort-Object -Property TotalMs -Descending |
    Select-Object -First $TopLimit |
    ForEach-Object {
        $behaviour = ($_.Tags.behaviour -replace '^"|"$', '' -replace '\\"', '"')
        $method = ($_.Tags.method -replace '^"|"$', '' -replace '\\"', '"')
        $totalMs = [double]$_.TotalMs
        [PSCustomObject]@{
            WorkItem     = "$behaviour::$method"
            TotalMs      = [math]::Round($totalMs, 2)
            TotalSeconds = [math]::Round($totalMs / 1000, 2)
        }
    })

$rpcTotals = Get-GroupedTotals -Measurement "rpc_calls" -Field "duration" -GroupByTags @("behaviour","method") -LookbackMinutes $LookbackMinutes -ServerTag $ServerTag -Endpoint $queryEndpoint -Database $DatabaseName -User $DatabaseUser -Password $DatabasePassword
$rpcRows = @($rpcTotals |
    Where-Object { $_.TotalMs -gt 0 } |
    Sort-Object -Property TotalMs -Descending |
    Select-Object -First $TopLimit |
    ForEach-Object {
        $behaviour = ($_.Tags.behaviour -replace '^"|"$', '' -replace '\\"', '"')
        $method = ($_.Tags.method -replace '^"|"$', '' -replace '\\"', '"')
        $totalMs = [double]$_.TotalMs
        [PSCustomObject]@{
            Rpc          = "$behaviour::$method"
            TotalMs      = [math]::Round($totalMs, 2)
            TotalSeconds = [math]::Round($totalMs / 1000, 2)
        }
    })

$serverUpdateTotals = Get-GroupedTotals -Measurement "server_update" -Field "duration" -GroupByTags @("behaviour","method") -LookbackMinutes $LookbackMinutes -ServerTag $ServerTag -Endpoint $queryEndpoint -Database $DatabaseName -User $DatabaseUser -Password $DatabasePassword
$serverUpdateRows = @($serverUpdateTotals |
    Where-Object { $_.TotalMs -gt 0 } |
    Sort-Object -Property TotalMs -Descending |
    Select-Object -First $TopLimit |
    ForEach-Object {
        $behaviour = ($_.Tags.behaviour -replace '^"|"$', '' -replace '\\"', '"')
        $method = ($_.Tags.method -replace '^"|"$', '' -replace '\\"', '"')
        $totalMs = [double]$_.TotalMs
        [PSCustomObject]@{
            Update       = "$behaviour::$method"
            TotalMs      = [math]::Round($totalMs, 2)
            TotalSeconds = [math]::Round($totalMs / 1000, 2)
        }
    })

$cpuStatsQuery = "SELECT mean(instant) AS avg_fps, min(instant) AS min_fps, max(instant) AS max_fps, mean(average) AS avg_smoothed FROM framerate WHERE time > now() - ${LookbackMinutes}m AND server='$ServerTag'"
$cpuStats = Get-StatResult -Query $cpuStatsQuery -Endpoint $queryEndpoint -Database $DatabaseName -User $DatabaseUser -Password $DatabasePassword

$frameTimeQuery = "SELECT mean(instant) AS avg_frame_ms, max(instant) AS max_frame_ms FROM frametime WHERE time > now() - ${LookbackMinutes}m AND server='$ServerTag'"
$frameTimeStats = Get-StatResult -Query $frameTimeQuery -Endpoint $queryEndpoint -Database $DatabaseName -User $DatabaseUser -Password $DatabasePassword

$memoryQuery = "SELECT mean(used) AS avg_used, max(used) AS max_used, mean(allocations) AS avg_allocations, mean(collections) AS avg_collections FROM memory WHERE time > now() - ${LookbackMinutes}m AND server='$ServerTag'"
$memoryStats = Get-StatResult -Query $memoryQuery -Endpoint $queryEndpoint -Database $DatabaseName -User $DatabaseUser -Password $DatabasePassword

Build-TableLines -Title "Top CPU Consuming Plugins (Runtime Performance - Excludes Init)" -Rows $pluginRows -Columns @(
    @{ Header = "Plugin"; Property = "Plugin" },
    @{ Header = "Avg Runtime ms"; Property = "AvgRuntimeMs"; Format = { if ($_ -gt 0) { "{0:N2}" -f $_ } else { "N/A" } } },
    @{ Header = "Peak Runtime ms"; Property = "PeakRuntimeMs"; Format = { if ($_ -gt 0) { "{0:N2}" -f $_ } else { "N/A" } } },
    @{ Header = "Init Time s"; Property = "InitTimeSec"; Format = { if ($_ -gt 0) { "{0:N2}" -f $_ } else { "N/A" } } },
    @{ Header = "% of Total"; Property = "PercentOfTotal"; Format = { "{0:N2}%" -f $_ } }
) -Builder $reportBuilder

# Add a note about the metrics
$reportBuilder.AppendLine() | Out-Null
$reportBuilder.AppendLine("Note: Runtime metrics exclude initialization time for accurate performance measurement.") | Out-Null
$reportBuilder.AppendLine("      Init Time shows one-time initialization cost. Avg/Peak Runtime shows ongoing performance.") | Out-Null

# Add initialization times section
$pluginsWithInit = @($allPlugins | Where-Object { $_.InitTimeMs -gt 0 } | Sort-Object -Property InitTimeMs -Descending | Select-Object -First $TopLimit)
if ($pluginsWithInit.Count -gt 0) {
    Build-TableLines -Title "Plugin Initialization Times (One-Time Cost)" -Rows $pluginsWithInit -Columns @(
        @{ Header = "Plugin"; Property = "Plugin" },
        @{ Header = "Init Time ms"; Property = "InitTimeMs"; Format = { "{0:N2}" -f $_ } },
        @{ Header = "Init Time s"; Property = "InitTimeSec"; Format = { "{0:N2}" -f $_ } }
    ) -Builder $reportBuilder
    $reportBuilder.AppendLine() | Out-Null
    $reportBuilder.AppendLine("Note: Initialization time is a one-time cost when the plugin loads. It does not affect ongoing performance.") | Out-Null
} else {
    $reportBuilder.AppendLine() | Out-Null
    $reportBuilder.AppendLine("== Plugin Initialization Times ==") | Out-Null
    $reportBuilder.AppendLine("No initialization time data found. This may indicate plugins haven't completed initialization yet.") | Out-Null
}

# Add HarmonyMods section if any found
if ($harmonyModsFound.Count -gt 0) {
    Build-TableLines -Title "HarmonyMod Plugins (Loaded Status)" -Rows $harmonyModsFound -Columns @(
        @{ Header = "Plugin"; Property = "Plugin" },
        @{ Header = "Total ms"; Property = "TotalMs"; Format = { "{0:N2}" -f $_ } },
        @{ Header = "Total s"; Property = "TotalSeconds"; Format = { "{0:N2}" -f $_ } },
        @{ Header = "Avg ms/min"; Property = "AvgMsPerMin"; Format = { "{0:N2}" -f $_ } }
    ) -Builder $reportBuilder
} else {
    $reportBuilder.AppendLine() | Out-Null
    $reportBuilder.AppendLine("== HarmonyMod Plugins ==") | Out-Null
    $reportBuilder.AppendLine("No HarmonyMod plugins found in metrics data.") | Out-Null
    $reportBuilder.AppendLine("Note: HarmonyMods report hookTime=1 every 5 seconds to indicate loaded status.") | Out-Null
}

Build-TableLines -Title "Most Expensive Method Calls (Invoke Hooks)" -Rows $invokeRows -Columns @(
    @{ Header = "Hook"; Property = "Hook" },
    @{ Header = "Total ms"; Property = "TotalMs"; Format = { "{0:N2}" -f $_ } },
    @{ Header = "Total s"; Property = "TotalSeconds"; Format = { "{0:N2}" -f $_ } },
    @{ Header = "Avg ms/min"; Property = "AvgMsPerMin"; Format = { "{0:N2}" -f $_ } }
) -Builder $reportBuilder

Build-TableLines -Title "Invoke Time by Behaviour" -Rows $behaviourRows -Columns @(
    @{ Header = "Behaviour"; Property = "Behaviour" },
    @{ Header = "Total ms"; Property = "TotalMs"; Format = { "{0:N2}" -f $_ } },
    @{ Header = "Total s"; Property = "TotalSeconds"; Format = { "{0:N2}" -f $_ } }
) -Builder $reportBuilder

Build-TableLines -Title "Object Work Queue Hotspots" -Rows $workQueueRows -Columns @(
    @{ Header = "Work Item"; Property = "WorkItem" },
    @{ Header = "Total ms"; Property = "TotalMs"; Format = { "{0:N2}" -f $_ } },
    @{ Header = "Total s"; Property = "TotalSeconds"; Format = { "{0:N2}" -f $_ } }
) -Builder $reportBuilder

Build-TableLines -Title "RPC Call Times" -Rows $rpcRows -Columns @(
    @{ Header = "RPC"; Property = "Rpc" },
    @{ Header = "Total ms"; Property = "TotalMs"; Format = { "{0:N2}" -f $_ } },
    @{ Header = "Total s"; Property = "TotalSeconds"; Format = { "{0:N2}" -f $_ } }
) -Builder $reportBuilder

Build-TableLines -Title "Server Update Hotspots" -Rows $serverUpdateRows -Columns @(
    @{ Header = "Update"; Property = "Update" },
    @{ Header = "Total ms"; Property = "TotalMs"; Format = { "{0:N2}" -f $_ } },
    @{ Header = "Total s"; Property = "TotalSeconds"; Format = { "{0:N2}" -f $_ } }
) -Builder $reportBuilder

# Calculate totals for optimization summary
$totalInvokeTime = ($invokeRows | Measure-Object -Property TotalMs -Sum).Sum
$totalWorkQueueTime = ($workQueueRows | Measure-Object -Property TotalMs -Sum).Sum
$totalServerUpdateTime = ($serverUpdateRows | Measure-Object -Property TotalMs -Sum).Sum
$totalSystemTime = $totalInvokeTime + $totalWorkQueueTime + $totalServerUpdateTime

$reportBuilder.AppendLine() | Out-Null
$reportBuilder.AppendLine("== Optimization Opportunities ==") | Out-Null
$reportBuilder.AppendLine() | Out-Null

# Top plugin optimization targets
$top3Plugins = $pluginRows | Select-Object -First 3
$reportBuilder.AppendLine("Top 3 Plugin Optimization Targets (by Runtime Performance):") | Out-Null
foreach ($plugin in $top3Plugins) {
    $runtimeInfo = if ($plugin.AvgRuntimeMs -gt 0) { "Avg Runtime: $($plugin.AvgRuntimeMs) ms, Peak: $($plugin.PeakRuntimeMs) ms" } else { "Runtime data not available" }
    $initInfo = if ($plugin.InitTimeSec -gt 0) { "Init: $($plugin.InitTimeSec) s" } else { "" }
    $reportBuilder.AppendLine("  • $($plugin.Plugin): $runtimeInfo $initInfo ($($plugin.PercentOfTotal)% of total)") | Out-Null
}

$reportBuilder.AppendLine() | Out-Null
$reportBuilder.AppendLine("Most Expensive System Hooks (Core Game):") | Out-Null
$top3Hooks = $invokeRows | Select-Object -First 3
foreach ($hook in $top3Hooks) {
    $hookPct = if ($totalSystemTime -gt 0) { [math]::Round(($hook.TotalMs / $totalSystemTime) * 100, 2) } else { 0 }
    $reportBuilder.AppendLine("  • $($hook.Hook): $($hook.TotalSeconds) s ($hookPct% of system time)") | Out-Null
}

$reportBuilder.AppendLine() | Out-Null
$reportBuilder.AppendLine("Work Queue Hotspots:") | Out-Null
$top3WorkQueue = $workQueueRows | Select-Object -First 3
foreach ($wq in $top3WorkQueue) {
    $wqPct = if ($totalWorkQueueTime -gt 0) { [math]::Round(($wq.TotalMs / $totalWorkQueueTime) * 100, 2) } else { 0 }
    $reportBuilder.AppendLine("  • $($wq.WorkItem): $($wq.TotalSeconds) s ($wqPct% of work queue time)") | Out-Null
}

$reportBuilder.AppendLine() | Out-Null
$reportBuilder.AppendLine("Note: HarmonyMods (GrimmNPC, etc.) show 'loaded status' only, not actual CPU usage.") | Out-Null
$reportBuilder.AppendLine("      To measure HarmonyMod performance, check invoke_execution hooks they may trigger.") | Out-Null

$reportBuilder.AppendLine() | Out-Null
$reportBuilder.AppendLine("== CPU / Frame Stats ==") | Out-Null
if ($cpuStats.Count -gt 0 -or $frameTimeStats.Count -gt 0) {
    $reportBuilder.AppendLine(("Average FPS: {0:N2}" -f ([double]($cpuStats.avg_fps)))) | Out-Null
    $reportBuilder.AppendLine(("Min FPS: {0:N2} / Max FPS: {1:N2}" -f ([double]($cpuStats.min_fps)), ([double]($cpuStats.max_fps)))) | Out-Null
    $reportBuilder.AppendLine(("Smoothed FPS: {0:N2}" -f ([double]($cpuStats.avg_smoothed)))) | Out-Null
    $reportBuilder.AppendLine(("Average Frame Time (ms): {0:N2}" -f ([double]($frameTimeStats.avg_frame_ms)))) | Out-Null
    $reportBuilder.AppendLine(("Worst Frame Time (ms): {0:N2}" -f ([double]($frameTimeStats.max_frame_ms)))) | Out-Null
} else {
    $reportBuilder.AppendLine("No framerate data returned during the window.") | Out-Null
}

$reportBuilder.AppendLine() | Out-Null
$reportBuilder.AppendLine("== Memory Usage ==") | Out-Null
if ($memoryStats.Count -gt 0) {
    $avgUsedGb = [math]::Round(([double]($memoryStats.avg_used)) / 1GB, 3)
    $maxUsedGb = [math]::Round(([double]($memoryStats.max_used)) / 1GB, 3)
    $reportBuilder.AppendLine("Average Used: $avgUsedGb GB") | Out-Null
    $reportBuilder.AppendLine("Peak Used: $maxUsedGb GB") | Out-Null
    $reportBuilder.AppendLine(("Avg Allocations: {0:N0}" -f ([double]($memoryStats.avg_allocations)))) | Out-Null
    $reportBuilder.AppendLine(("Avg GC Collections: {0:N0}" -f ([double]($memoryStats.avg_collections)))) | Out-Null
} else {
    $reportBuilder.AppendLine("No memory datapoints returned.") | Out-Null
}

$reportContent = $reportBuilder.ToString()
$reportContent | Set-Content -Path $OutputPath -Encoding UTF8

# Copy to clipboard for easy sharing
try {
    $reportContent | Set-Clipboard
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Metrics report written to: $OutputPath" -ForegroundColor Green
    Write-Host "Report also copied to clipboard!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
} catch {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Metrics report written to: $OutputPath" -ForegroundColor Green
    Write-Host "(Could not copy to clipboard: $_)" -ForegroundColor Yellow
    Write-Host "========================================" -ForegroundColor Cyan
}

# Display a summary in console for quick reference
Write-Host ""
Write-Host "=== QUICK SUMMARY (for copy/paste) ===" -ForegroundColor Cyan
Write-Host "Top 5 Plugins by Runtime Performance:" -ForegroundColor Yellow
$pluginRows | Select-Object -First 5 | ForEach-Object {
    $runtimeInfo = if ($_.AvgRuntimeMs -gt 0) { "Avg: $($_.AvgRuntimeMs) ms, Peak: $($_.PeakRuntimeMs) ms" } else { "Runtime: N/A" }
    $initInfo = if ($_.InitTimeSec -gt 0) { "Init: $($_.InitTimeSec) s" } else { "" }
    Write-Host "  $($_.Plugin): $runtimeInfo $initInfo" -ForegroundColor White
}
if ($harmonyModsFound.Count -gt 0) {
    Write-Host ""
    Write-Host "HarmonyMod Plugins Found:" -ForegroundColor Yellow
    $harmonyModsFound | ForEach-Object {
        Write-Host "  $($_.Plugin): $($_.TotalMs) ms ($($_.TotalSeconds) s)" -ForegroundColor Cyan
    }
}
Write-Host ""
Write-Host "Top 5 Hooks by CPU:" -ForegroundColor Yellow
$invokeRows | Select-Object -First 5 | ForEach-Object {
    Write-Host "  $($_.Hook): $($_.TotalMs) ms ($($_.TotalSeconds) s)" -ForegroundColor White
}
Write-Host ""
Write-Host "Full report available in: $OutputPath" -ForegroundColor Gray
Write-Host "========================================" -ForegroundColor Cyan

