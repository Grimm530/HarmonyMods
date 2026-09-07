# Full GrimmCore rollout: rebuild 0GrimmCore + all mods using unified Hurt dispatcher.
$ErrorActionPreference = "Continue"
$base = Split-Path $PSScriptRoot -Parent
$harmonyMods = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..\HarmonyMods")).Path

# 0GrimmCore MUST build first
$dirs = @(
    '0GrimmCore',
    'TruePVE',
    'PveMode',
    'RaidableBases',
    'ZoneManager',
    'SkillTree',
    'Cooking',
    'CombatClasses',
    'VirtualQuarries',
    'UpLifted',
    'ArmoredTrain',
    'GrimmBoss',
    'DefendableHomes',
    'BradleyDrops',
    'PersonalNPC',
    '0GrimmNPC',
    'ZombieHorde',
    'RustEditStandalone',
    'RustLeague',
    'RustVehicles',
    'CHT',
    'Convoy',
    'RustRewards',
    'HitMarkers',
    'Leaderboard'
)

$failed = @()
$succeeded = @()
$skipped = @()

foreach ($dir in $dirs) {
    $folder = Join-Path $base $dir
    $script = Join-Path $folder 'build.ps1'
    if (-not (Test-Path $script)) {
        $skipped += $dir
        Write-Host "SKIP: $dir - no build.ps1" -ForegroundColor Yellow
        continue
    }
    Write-Host "`n========== $dir ==========" -ForegroundColor Cyan
    Push-Location $folder
    try {
        & .\build.ps1
        if ($LASTEXITCODE -ne 0) {
            $failed += $dir
            Write-Host "FAIL: $dir (exit $LASTEXITCODE)" -ForegroundColor Red
        } else {
            $succeeded += $dir
            Write-Host "OK: $dir" -ForegroundColor Green
        }
    } catch {
        $failed += $dir
        Write-Host "FAIL: $dir - $($_.Exception.Message)" -ForegroundColor Red
    }
    Pop-Location
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Rollout complete: $($succeeded.Count) ok, $($failed.Count) failed, $($skipped.Count) skipped"
if ($failed.Count -gt 0) {
    Write-Host "Failed: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host "All GrimmCore rollout builds succeeded." -ForegroundColor Green
Write-Host "Deploy folder: $harmonyMods"
exit 0
