# Query Entity Counts from InfluxDB
# Helps identify if entity count spikes correlate with performance issues

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

    $base64Auth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${User}:${Password}"))
    $headers = @{
        "Authorization" = "Basic $base64Auth"
    }

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
        throw "Influx query failed: $errorDetail"
    }
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

Write-Host "Querying entity counts for last $LookbackMinutes minutes..." -ForegroundColor Yellow
Write-Host ""

# Query entity counts over time
$entityQuery = "SELECT mean(`count`) AS avg_count, max(`count`) AS max_count, min(`count`) AS min_count FROM entities WHERE time > now() - ${LookbackMinutes}m AND server='$ServerTag' GROUP BY time(5m)"
$results = Invoke-InfluxQuery -Query $entityQuery -Endpoint $queryEndpoint -Database $DatabaseName -User $DatabaseUser -Password $DatabasePassword

Write-Host "== Entity Count Statistics (last $LookbackMinutes minutes) ==" -ForegroundColor Cyan
Write-Host ""

$allCounts = New-Object System.Collections.Generic.List[double]

foreach ($result in $results) {
    if (-not $result.series) {
        continue
    }
    foreach ($series in $result.series) {
        $columns = $series.columns
        $countIndex = -1
        for ($i = 0; $i -lt $columns.Count; $i++) {
            if ($columns[$i] -like "*count*" -or $columns[$i] -eq "mean" -or $columns[$i] -eq "avg_count") {
                $countIndex = $i
                break
            }
        }
        
        if ($countIndex -ge 0) {
            foreach ($row in $series.values) {
                if ($row -and $row.Count -gt $countIndex) {
                    $value = $row[$countIndex]
                    if ($null -ne $value -and $value -ne "") {
                        try {
                            $numValue = [double]$value
                            $allCounts.Add($numValue)
                        } catch {
                            # Skip non-numeric values
                        }
                    }
                }
            }
        }
    }
}

if ($allCounts.Count -gt 0) {
    $avgCount = ($allCounts | Measure-Object -Average).Average
    $maxCount = ($allCounts | Measure-Object -Maximum).Maximum
    $minCount = ($allCounts | Measure-Object -Minimum).Minimum
    
    Write-Host "Average Entity Count: $([math]::Round($avgCount, 0))" -ForegroundColor White
    Write-Host "Maximum Entity Count: $([math]::Round($maxCount, 0))" -ForegroundColor White
    Write-Host "Minimum Entity Count: $([math]::Round($minCount, 0))" -ForegroundColor White
    Write-Host ""
    Write-Host "NOTE: This shows total entities. For NPC-specific counts, run 'npccount' in server console." -ForegroundColor Yellow
} else {
    Write-Host "No entity count data found in InfluxDB for this time period." -ForegroundColor Yellow
    Write-Host "Entity counts may not be tracked, or data hasn't been written yet." -ForegroundColor Gray
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "To check NPC counts in-game, use:" -ForegroundColor Yellow
Write-Host "  npccount" -ForegroundColor White
Write-Host ""
Write-Host "Or in server console:" -ForegroundColor Yellow
Write-Host "  servermetrics.npccount" -ForegroundColor White
Write-Host "========================================" -ForegroundColor Cyan

