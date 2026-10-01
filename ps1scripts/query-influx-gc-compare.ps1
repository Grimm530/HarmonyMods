# Reads Influx credentials from HarmonyConfig; writes JSON (no secrets) for GC compare.
$ErrorActionPreference = "Stop"
$configPath = "D:\!RustServer\HarmonyConfig\HarmonyMetrics.json"
$outPath = "D:\!RustServer\.cursor\HarmonyMods\HarmonyMetrics\tools\gc-compare-now.json"
$cfg = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$base = $cfg.'Influx Database Url'.TrimEnd('/')
$u = $cfg.'Influx Database User'
$p = $cfg.'Influx Database Password'
$pair = "${u}:${p}"
$b64 = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($pair))
$headers = @{ Authorization = "Basic $b64" }

function Invoke-Influx([string]$Database, [string]$Query) {
    $body = "db=$([uri]::EscapeDataString($Database))&q=$([uri]::EscapeDataString($Query))&epoch=ms"
    Invoke-RestMethod -Uri "$base/query" -Method Post -Headers $headers -Body $body -ContentType "application/x-www-form-urlencoded"
}

$bundle = [ordered]@{
    queriedAtUtc = [DateTime]::UtcNow.ToString("o")
    databases = [ordered]@{}
}

$qs = [ordered]@{
    measurements = 'SHOW MEASUREMENTS'
    servers_memory = 'SHOW TAG VALUES FROM memory WITH KEY = "server"'
    servers_oxide = 'SHOW TAG VALUES FROM oxide_plugins WITH KEY = "server"'
    servers_harmony = 'SHOW TAG VALUES FROM harmony_mods WITH KEY = "server"'
    memory_last = 'SELECT last("collections") AS collections, last("used") AS used, last("allocations") AS allocations FROM memory GROUP BY server'
    fps_last = 'SELECT last("instant") AS instant, last("average") AS average FROM framerate GROUP BY server'
    players_last = 'SELECT last("count") AS count FROM players GROUP BY server'
    entities_last = 'SELECT last("count") AS count FROM entities GROUP BY server'
    oxide_last = 'SELECT last("hookTime") AS hookTime FROM oxide_plugins GROUP BY server'
    harmony_count_last = 'SELECT last("count") AS count FROM harmony_mod_count GROUP BY server'
    memory_3h = 'SELECT last("collections") AS collections, last("used") AS used FROM memory WHERE time > now() - 3h GROUP BY server, time(1m) fill(none)'
    fps_3h = 'SELECT last("instant") AS instant FROM framerate WHERE time > now() - 3h GROUP BY server, time(1m) fill(none)'
    memory_7d = 'SELECT last("collections") AS collections FROM memory WHERE time > now() - 7d GROUP BY server, time(10m) fill(none)'
    fps_7d = 'SELECT last("instant") AS instant FROM framerate WHERE time > now() - 7d GROUP BY server, time(10m) fill(none)'
    ents_7d = 'SELECT last("count") AS count FROM entities WHERE time > now() - 7d GROUP BY server, time(10m) fill(none)'
    memory_7d_first = 'SELECT first("collections") AS collections FROM memory WHERE time > now() - 7d GROUP BY server'
    memory_7d_last = 'SELECT last("collections") AS collections FROM memory WHERE time > now() - 7d GROUP BY server'
    fps_7d_last = 'SELECT last("instant") AS instant FROM framerate WHERE time > now() - 7d GROUP BY server'
}

foreach ($dbName in @('rust_server_metrics', 'rust-server-metrics')) {
    $dbBundle = [ordered]@{}
    foreach ($name in $qs.Keys) {
        try {
            $dbBundle[$name] = Invoke-Influx $dbName $qs[$name]
        } catch {
            $dbBundle[$name] = @{ error = $_.Exception.Message }
        }
    }
    $bundle.databases[$dbName] = $dbBundle
}

$json = $bundle | ConvertTo-Json -Depth 20 -Compress:$false
[System.IO.File]::WriteAllText($outPath, $json)
Write-Host "Wrote $outPath"
Write-Host "queriedAtUtc=$($bundle.queriedAtUtc)"
