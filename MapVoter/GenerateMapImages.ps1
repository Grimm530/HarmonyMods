<#
.SYNOPSIS
    Generates map preview images by starting a Rust server for each seed in seeds_to_generate.txt.
    Loops continuously: process seeds, wait, check for new seeds (like Faststart.bat).

.DESCRIPTION
    Run this AFTER MapVoter writes seeds_to_generate.txt (e.g. after mvtest).
    For each seed, starts RustDedicated, waits for map image, then stops.
    Images saved to {ServerRoot}\maps\images\{size}_{seed}.png

    By default, when done it waits 30s and checks seeds_to_generate.txt again - run mvtest
    to add more seeds, and the next loop will process them. Use -RunOnce to process once and exit.

    PREREQUISITES:
    - CustomMapGen: MapImage.Enabled=true, OutputFolder=maps/images, MapVoterFormat=true
    - Uses +server.identity my_server_identity1, my_server_identity2, etc. per seed so each run gets a fresh
      server folder (no cached map) without touching your main server identity (e.g. grimm).

.EXAMPLE
    .\GenerateMapImages.ps1
    .\GenerateMapImages.ps1 -ServerRoot "D:\!RustServer"
    .\GenerateMapImages.ps1 -RunOnce
#>
param(
    [string]$ServerRoot = "D:\!RustServer",
    [int]$GenTimeoutMinutes = 8,
    [string]$SeedsFile = "",
    [switch]$RunOnce  # Use -RunOnce to process once and exit (no loop)
)
$ErrorActionPreference = "Stop"
if ([string]::IsNullOrEmpty($SeedsFile)) {
    $SeedsFile = Join-Path $ServerRoot "HarmonyImages\MapVoter\seeds_to_generate.txt"
}

# Set title so window is recognizable (like Faststart.bat)
$Host.UI.RawUI.WindowTitle = "Map Image Generator"

while ($true) {
    if (-not (Test-Path $SeedsFile)) {
        Write-Host "Seeds file not found: $SeedsFile" -ForegroundColor Yellow
        Write-Host "Run 'mvtest 30' on your server - MapVoter writes seeds when a vote starts."
        Write-Host "Waiting 30 seconds, then checking again..."
        Start-Sleep -Seconds 30
        if (-not $RunOnce) { continue }
        exit 1
    }

    $lines = Get-Content $SeedsFile
    $mapSize = [int]$lines[0]
    $seeds = @()
    for ($i = 1; $i -lt $lines.Count; $i++) {
        $s = $lines[$i].Trim()
        if (-not [string]::IsNullOrEmpty($s)) { $seeds += [int]$s }
    }
    if ($seeds.Count -eq 0) {
        Write-Host "No seeds to generate." -ForegroundColor Yellow
        if ($RunOnce) { exit 0 }
        Write-Host "Waiting 30 seconds, then checking again..."
        Start-Sleep -Seconds 30
        continue
    }
    $exe = Join-Path $ServerRoot "RustDedicated.exe"
    if (-not (Test-Path $exe)) {
        Write-Host "RustDedicated.exe not found at: $exe" -ForegroundColor Red
        Write-Host "Set -ServerRoot to your server folder (e.g. D:\!RustServer)"
        exit 1
    }
    $outDir = Join-Path $ServerRoot "maps\images"
    if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

    Write-Host "================================" -ForegroundColor Cyan
    Write-Host "[$(Get-Date -Format 'yyyyMMdd-HHmmss')] MAP IMAGE GENERATION" -ForegroundColor Cyan
    Write-Host "================================" -ForegroundColor Cyan
    Write-Host "Map size: $mapSize | Seeds: $($seeds -join ', ')" -ForegroundColor Cyan
    Write-Host "Ensure CustomMapGen MapImage: Enabled=true, OutputFolder=maps/images, MapVoterFormat=true" -ForegroundColor Yellow

    $idx = 0
    foreach ($seed in $seeds) {
        $idx++
        $targetFile = Join-Path $outDir "${mapSize}_${seed}.png"
        if (Test-Path $targetFile) {
            Write-Host "[$idx/$($seeds.Count)] Skip seed $seed - image exists" -ForegroundColor Gray
            continue
        }
        Write-Host "[$idx/$($seeds.Count)] Generating map image for seed $seed..." -ForegroundColor Green
        $identity = "my_server_identity$idx"
        $proc = Start-Process -FilePath $exe -ArgumentList @(
            "-batchmode", "-nographics",
            "+server.identity", $identity,
            "+server.seed", $seed.ToString(),
            "+server.worldsize", $mapSize.ToString(),
            "+server.maxplayers", "0"
        ) -WorkingDirectory $ServerRoot -PassThru
        $deadline = (Get-Date).AddMinutes($GenTimeoutMinutes)
        while ((Get-Date) -lt $deadline) {
            if ($proc.HasExited) { break }
            if (Test-Path $targetFile) {
                Write-Host "  Image saved: $targetFile" -ForegroundColor Green
                break
            }
            $mapFile = Join-Path $ServerRoot "map_${mapSize}_${seed}.png"
            if (Test-Path $mapFile) {
                Move-Item -Path $mapFile -Destination $targetFile -Force
                Write-Host "  Moved map_*.png to $targetFile" -ForegroundColor Green
                break
            }
            Start-Sleep -Seconds 15
        }
        if (-not $proc.HasExited) {
            Write-Host "  Timeout - stopping server process" -ForegroundColor Yellow
            $proc.Kill()
        }
        if (-not (Test-Path $targetFile)) {
            Write-Host "  WARNING: No image for seed $seed after $GenTimeoutMinutes min" -ForegroundColor Red
        }
    }

    Write-Host "Done. Images in $outDir" -ForegroundColor Cyan
    if ($RunOnce) { break }

    Write-Host "Checking for new seeds in 30 seconds..."
    Start-Sleep -Seconds 30
}
