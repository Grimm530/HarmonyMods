# Build AnimalSpawn Harmony mod (GrimmBoss custom-animal helper).
# Copies only AnimalSpawn.dll into server HarmonyMods/.
# Horse ownership limits stay in Shop (shop.horse / animalspawn.horse).

Write-Host "Building AnimalSpawn..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "AnimalSpawn.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\AnimalSpawn.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\AnimalSpawn.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }

    $destPath = Join-Path $harmonyModsPath "AnimalSpawn.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "Build successful! AnimalSpawn.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/AnimalSpawn.json  Data: HarmonyData/AnimalSpawn/" -ForegroundColor Yellow
    Write-Host "Load: harmony.load AnimalSpawn" -ForegroundColor Yellow
    Write-Host "Unload oxide/plugins/AnimalSpawn.cs so Shop keeps animalspawn.horse" -ForegroundColor Gray
} else {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}
