# Influx Metrics Report Generator for Rust Server
# Pulls high-cost plugin and server hook data from InfluxDB and formats a report

param(
    [int]$LookbackMinutes = 30,
    [int]$TopLimit = 10,
    [string]$OutputPath = "",
    [string]$ConfigPath = "",
    [string]$InfluxUrl = "",
    [string]$DatabaseName = "",
    [string]$DatabaseUser = "",
    [string]$DatabasePassword = "",
    [string]$ServerTag = "",
    [switch]$ComparePlugins
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

function Get-PluginComparison {
    param(
        [string[]]$PluginNames,
        [int]$LookbackMinutes,
        [string]$ServerTag,
        [string]$Endpoint,
        [string]$Database,
        [string]$User,
        [string]$Password
    )

    $comparisonData = @{}
    
    # Query all plugins grouped by plugin, then filter for our target plugins
    # This avoids issues with how plugin names are stored in tags
    Write-Host "  Querying all plugin metrics..." -ForegroundColor Yellow
    $allPluginTotals = Get-GroupedTotals -Measurement "oxide_plugins" -Field "hookTime" -GroupByTags @("plugin") -LookbackMinutes $LookbackMinutes -ServerTag $ServerTag -Endpoint $Endpoint -Database $Database -User $User -Password $Password
    
    foreach ($pluginName in $PluginNames) {
        Write-Host "  Processing metrics for: $pluginName" -ForegroundColor Yellow
        
        # Find this plugin in the results
        $pluginData = $allPluginTotals | Where-Object { 
            $tagPluginName = $_.Tags.plugin -replace '^"|"$', '' -replace '\\"', '"'
            $tagPluginName -eq $pluginName
        } | Select-Object -First 1
        
        if (-not $pluginData) {
            Write-Host "    Warning: No data found for plugin '$pluginName'" -ForegroundColor Yellow
        }
        
        # Get plugin hook time totals from the grouped data
        $totalHookTime = if ($pluginData) { [double]$pluginData.TotalMs } else { 0 }
        $hookCount = 0
        $avgHookTime = 0
        $maxHookTime = 0
        $minHookTime = [double]::MaxValue
        
        # Get hook count and average - query with aggregates grouped by plugin, then filter
        # Query: SELECT COUNT, MIN, MAX, MEAN grouped by plugin, then filter in PowerShell
        $hookStatsQuery = "SELECT COUNT(`"hookTime`") AS count, MIN(`"hookTime`") AS min_time, MAX(`"hookTime`") AS max_time, MEAN(`"hookTime`") AS mean_time FROM oxide_plugins WHERE time > now() - ${LookbackMinutes}m AND server='$ServerTag' GROUP BY plugin"
        $hookStatsResult = Invoke-InfluxQuery -Query $hookStatsQuery -Endpoint $Endpoint -Database $Database -User $User -Password $Password
        
        $hookCount = 0
        $avgHookTime = 0
        $maxHookTime = 0
        $minHookTime = [double]::MaxValue
        
        if ($hookStatsResult -and $hookStatsResult[0].series -and $hookStatsResult[0].series.Count -gt 0) {
            foreach ($series in $hookStatsResult[0].series) {
                $tagPluginName = if ($series.tags.plugin) { $series.tags.plugin -replace '^"|"$', '' -replace '\\"', '"' } else { "" }
                if ($tagPluginName -eq $pluginName) {
                    $columns = $series.columns
                    if ($series.values -and $series.values.Count -gt 0) {
                        foreach ($row in $series.values) {
                            for ($i = 0; $i -lt $columns.Count; $i++) {
                                if ($columns[$i] -eq "time") { continue }
                                $colName = $columns[$i]
                                if ($row.Count -gt $i -and $null -ne $row[$i]) {
                                    try {
                                        $value = [double]$row[$i]
                                        if ($colName -like "*count*" -or $colName -eq "count") {
                                            $hookCount = [int]$value
                                        } elseif ($colName -like "*min*" -or $colName -eq "min_time") {
                                            $minHookTime = $value
                                        } elseif ($colName -like "*max*" -or $colName -eq "max_time") {
                                            $maxHookTime = $value
                                        } elseif ($colName -like "*mean*" -or $colName -eq "mean_time") {
                                            $avgHookTime = $value
                                        }
                                    } catch {
                                        # Ignore parse errors
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        
        if ($hookCount -gt 0 -and $avgHookTime -eq 0) {
            $avgHookTime = $totalHookTime / $hookCount
        }
        if ($minHookTime -eq [double]::MaxValue) {
            $minHookTime = 0
        }
        
        if ($hookStatsResult -and $hookStatsResult[0].series -and $hookStatsResult[0].series.Count -gt 0) {
            foreach ($series in $hookStatsResult[0].series) {
                $columns = $series.columns
                if ($series.values -and $series.values.Count -gt 0) {
                    foreach ($row in $series.values) {
                        for ($i = 0; $i -lt $columns.Count; $i++) {
                            if ($columns[$i] -eq "time") { continue }
                            $colName = $columns[$i]
                            if ($row.Count -gt $i -and $null -ne $row[$i]) {
                                try {
                                    $value = [double]$row[$i]
                                    if ($colName -like "*count*" -or $colName -eq "count") {
                                        $hookCount = [int]$value
                                    } elseif ($colName -like "*min*" -or $colName -eq "min") {
                                        if ($value -lt $minHookTime) { $minHookTime = $value }
                                    } elseif ($colName -like "*max*" -or $colName -eq "max") {
                                        if ($value -gt $maxHookTime) { $maxHookTime = $value }
                                    } elseif ($colName -like "*mean*" -or $colName -eq "mean") {
                                        $avgHookTime = $value
                                    }
                                } catch {
                                    # Ignore parse errors
                                }
                            }
                        }
                    }
                }
            }
        }
        
        if ($hookCount -gt 0 -and $avgHookTime -eq 0) {
            $avgHookTime = $totalHookTime / $hookCount
        }
        if ($minHookTime -eq [double]::MaxValue) {
            $minHookTime = 0
        }
        
        # Get invoke execution metrics for this plugin's hooks - query all and filter
        $allInvokeTotals = Get-GroupedTotals -Measurement "invoke_execution" -Field "duration" -GroupByTags @("plugin") -LookbackMinutes $LookbackMinutes -ServerTag $ServerTag -Endpoint $Endpoint -Database $Database -User $User -Password $Password
        $invokeData = $allInvokeTotals | Where-Object { 
            $tagPluginName = $_.Tags.plugin -replace '^"|"$', '' -replace '\\"', '"'
            $tagPluginName -eq $pluginName
        } | Select-Object -First 1
        $totalInvokeTime = if ($invokeData) { [double]$invokeData.TotalMs } else { 0 }
        
        # Get RPC call metrics - query all and filter
        $allRpcTotals = Get-GroupedTotals -Measurement "rpc_calls" -Field "duration" -GroupByTags @("plugin") -LookbackMinutes $LookbackMinutes -ServerTag $ServerTag -Endpoint $Endpoint -Database $Database -User $User -Password $Password
        $rpcData = $allRpcTotals | Where-Object { 
            $tagPluginName = $_.Tags.plugin -replace '^"|"$', '' -replace '\\"', '"'
            $tagPluginName -eq $pluginName
        } | Select-Object -First 1
        $totalRpcTime = if ($rpcData) { [double]$rpcData.TotalMs } else { 0 }
        
        # Get top hooks for this plugin - query all hooks grouped by hook and plugin, then filter
        $allHooksTotals = Get-GroupedTotals -Measurement "oxide_plugins" -Field "hookTime" -GroupByTags @("plugin", "hook") -LookbackMinutes $LookbackMinutes -ServerTag $ServerTag -Endpoint $Endpoint -Database $Database -User $User -Password $Password
        $topHooksResult = $allHooksTotals | Where-Object { 
            $tagPluginName = $_.Tags.plugin -replace '^"|"$', '' -replace '\\"', '"'
            $tagPluginName -eq $pluginName
        }
        
        $topHooks = @()
        if ($topHooksResult) {
            foreach ($hookData in $topHooksResult) {
                $hookName = if ($hookData.Tags.hook) { $hookData.Tags.hook -replace '^"|"$', '' -replace '\\"', '"' } else { "Unknown" }
                $hookTotal = [double]$hookData.TotalMs
                if ($hookTotal -gt 0) {
                    $topHooks += [PSCustomObject]@{
                        Hook = $hookName
                        TotalMs = [math]::Round($hookTotal, 2)
                    }
                }
            }
        }
        
        # Sort by TotalMs descending and take top 10
        $topHooks = $topHooks | Sort-Object -Property TotalMs -Descending | Select-Object -First 10
        
        $comparisonData[$pluginName] = [PSCustomObject]@{
            PluginName = $pluginName
            TotalHookTime = [math]::Round($totalHookTime, 2)
            TotalHookTimeSeconds = [math]::Round($totalHookTime / 1000, 2)
            HookCount = $hookCount
            AvgHookTime = [math]::Round($avgHookTime, 4)
            MaxHookTime = [math]::Round($maxHookTime, 4)
            MinHookTime = [math]::Round($minHookTime, 4)
            AvgMsPerMin = [math]::Round($totalHookTime / $LookbackMinutes, 2)
            TotalInvokeTime = [math]::Round($totalInvokeTime, 2)
            TotalRpcTime = [math]::Round($totalRpcTime, 2)
            TopHooks = $topHooks
        }
    }
    
    return $comparisonData
}

function Build-ComparisonReport {
    param(
        [hashtable]$ComparisonData,
        [int]$LookbackMinutes,
        [string]$ServerTag,
        [System.Text.StringBuilder]$Builder
    )
    
    $Builder.AppendLine() | Out-Null
    $Builder.AppendLine("========================================") | Out-Null
    $Builder.AppendLine("PLUGIN PERFORMANCE COMPARISON") | Out-Null
    $Builder.AppendLine("========================================") | Out-Null
    $Builder.AppendLine("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')") | Out-Null
    $Builder.AppendLine("Lookback Window: $LookbackMinutes minutes") | Out-Null
    $Builder.AppendLine("Server Tag: $ServerTag") | Out-Null
    $Builder.AppendLine() | Out-Null
    
    # Overall comparison table
    $Builder.AppendLine("== Overall Performance Comparison ==") | Out-Null
    $Builder.AppendLine("Plugin | Total ms | Total s | Hook Count | Avg ms/hook | Max ms | Min ms | Avg ms/min") | Out-Null
    $Builder.AppendLine("-------|----------|---------|------------|-------------|--------|--------|------------") | Out-Null
    
    $plugins = $ComparisonData.Keys | Sort-Object
    foreach ($pluginName in $plugins) {
        $data = $ComparisonData[$pluginName]
        $Builder.AppendLine("$($data.PluginName) | $($data.TotalHookTime) | $($data.TotalHookTimeSeconds) | $($data.HookCount) | $($data.AvgHookTime) | $($data.MaxHookTime) | $($data.MinHookTime) | $($data.AvgMsPerMin)") | Out-Null
    }
    
    # Find the most efficient plugin (lowest total time)
    $sortedByTotal = $ComparisonData.Values | Sort-Object -Property TotalHookTime
    if ($sortedByTotal.Count -gt 0) {
        $mostEfficient = $sortedByTotal[0]
        $leastEfficient = $sortedByTotal[-1]
        
        $Builder.AppendLine() | Out-Null
        $Builder.AppendLine("== Performance Summary ==") | Out-Null
        $Builder.AppendLine("Most Efficient (Lowest Total Time): $($mostEfficient.PluginName) - $($mostEfficient.TotalHookTime) ms ($($mostEfficient.TotalHookTimeSeconds) s)") | Out-Null
        $Builder.AppendLine("Least Efficient (Highest Total Time): $($leastEfficient.PluginName) - $($leastEfficient.TotalHookTime) ms ($($leastEfficient.TotalHookTimeSeconds) s)") | Out-Null
        
        if ($sortedByTotal.Count -gt 1) {
            $difference = $leastEfficient.TotalHookTime - $mostEfficient.TotalHookTime
            $percentDiff = if ($mostEfficient.TotalHookTime -gt 0) {
                [math]::Round(($difference / $mostEfficient.TotalHookTime) * 100, 2)
            } else { 0 }
            $Builder.AppendLine("Difference: $difference ms ($([math]::Round($difference / 1000, 2)) s) - $percentDiff% more time") | Out-Null
        }
    }
    
    # Detailed breakdown per plugin
    foreach ($pluginName in $plugins) {
        $data = $ComparisonData[$pluginName]
        $Builder.AppendLine() | Out-Null
        $Builder.AppendLine("== $pluginName - Detailed Metrics ==") | Out-Null
        $Builder.AppendLine("Total Hook Time: $($data.TotalHookTime) ms ($($data.TotalHookTimeSeconds) s)") | Out-Null
        $Builder.AppendLine("Total Hook Calls: $($data.HookCount)") | Out-Null
        $Builder.AppendLine("Average Hook Time: $($data.AvgHookTime) ms") | Out-Null
        $Builder.AppendLine("Max Hook Time: $($data.MaxHookTime) ms") | Out-Null
        $Builder.AppendLine("Min Hook Time: $($data.MinHookTime) ms") | Out-Null
        $Builder.AppendLine("Average ms per minute: $($data.AvgMsPerMin)") | Out-Null
        $Builder.AppendLine("Total Invoke Time: $($data.TotalInvokeTime) ms") | Out-Null
        $Builder.AppendLine("Total RPC Time: $($data.TotalRpcTime) ms") | Out-Null
        
        if ($data.TopHooks.Count -gt 0) {
            $Builder.AppendLine() | Out-Null
            $Builder.AppendLine("Top Hooks by Time:") | Out-Null
            foreach ($hook in $data.TopHooks) {
                $Builder.AppendLine("  - $($hook.Hook): $($hook.TotalMs) ms") | Out-Null
            }
        }
    }
    
    # Side-by-side comparison
    $Builder.AppendLine() | Out-Null
    $Builder.AppendLine("== Side-by-Side Comparison ==") | Out-Null
    
    # Create comparison metrics
    $metrics = @("TotalHookTime", "TotalHookTimeSeconds", "HookCount", "AvgHookTime", "AvgMsPerMin")
    foreach ($metric in $metrics) {
        $Builder.AppendLine() | Out-Null
        $Builder.AppendLine("$metric :") | Out-Null
        foreach ($pluginName in $plugins) {
            $data = $ComparisonData[$pluginName]
            $value = $data.$metric
            $Builder.AppendLine("  $pluginName : $value") | Out-Null
        }
    }
}

if ($LookbackMinutes -lt 1) {
    throw "LookbackMinutes must be at least 1."
}

$configSearchPaths = @()
if ($ConfigPath) {
    $configSearchPaths += $ConfigPath
}
$configSearchPaths += @(
    (Join-Path $PSScriptRoot "..\..\HarmonyMods_Data\ServerMetrics\Configuration.json"),
    (Join-Path $PSScriptRoot "HarmonyMods_Data\ServerMetrics\Configuration.json"),
    "HarmonyMods_Data\ServerMetrics\Configuration.json"
)

foreach ($path in $configSearchPaths | Select-Object -Unique) {
    Resolve-ConfigValues -ConfigPathCandidate $path -InfluxUrlRef ([ref]$InfluxUrl) -DatabaseNameRef ([ref]$DatabaseName) -DatabaseUserRef ([ref]$DatabaseUser) -DatabasePasswordRef ([ref]$DatabasePassword) -ServerTagRef ([ref]$ServerTag)
    if ($InfluxUrl -and $DatabaseName -and $DatabaseUser -and $DatabasePassword -and $ServerTag) {
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
    $OutputPath = Join-Path $PSScriptRoot "vehicle-comparison_$timestamp.txt"
}

Write-Host "Generating vehicle plugin comparison report for last $LookbackMinutes minutes..." -ForegroundColor Yellow
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

# Define the three vehicle plugins to compare
$vehiclePlugins = @("RustVehicles", "VehicleLicence", "VehicleLicenceFork")

Write-Host "Querying performance metrics for vehicle plugins..." -ForegroundColor Yellow
$comparisonData = Get-PluginComparison -PluginNames $vehiclePlugins -LookbackMinutes $LookbackMinutes -ServerTag $ServerTag -Endpoint $queryEndpoint -Database $DatabaseName -User $DatabaseUser -Password $DatabasePassword

$reportBuilder = New-Object System.Text.StringBuilder
Build-ComparisonReport -ComparisonData $comparisonData -LookbackMinutes $LookbackMinutes -ServerTag $ServerTag -Builder $reportBuilder

$reportContent = $reportBuilder.ToString()
$reportContent | Set-Content -Path $OutputPath -Encoding UTF8

# Copy to clipboard for easy sharing
try {
    $reportContent | Set-Clipboard
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Vehicle comparison report written to: $OutputPath" -ForegroundColor Green
    Write-Host "Report also copied to clipboard!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
} catch {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Vehicle comparison report written to: $OutputPath" -ForegroundColor Green
    Write-Host "(Could not copy to clipboard: $_)" -ForegroundColor Yellow
    Write-Host "========================================" -ForegroundColor Cyan
}

# Display a summary in console for quick reference
Write-Host ""
Write-Host "=== VEHICLE PLUGIN COMPARISON SUMMARY ===" -ForegroundColor Cyan
$sortedPlugins = $comparisonData.Values | Sort-Object -Property TotalHookTime
foreach ($plugin in $sortedPlugins) {
    Write-Host "$($plugin.PluginName): $($plugin.TotalHookTime) ms ($($plugin.TotalHookTimeSeconds) s) - $($plugin.HookCount) hooks" -ForegroundColor White
}
Write-Host ""
Write-Host "Full comparison report available in: $OutputPath" -ForegroundColor Gray
Write-Host "========================================" -ForegroundColor Cyan

