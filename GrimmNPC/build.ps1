# Build GrimmNPC (NpcSpawn Harmony port)
# Copies only GrimmNPC.dll into server HarmonyMods/

Write-Host "Building GrimmNPC..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "GrimmNPC.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\GrimmNPC.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\GrimmNPC.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "GrimmNPC.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! GrimmNPC.dll -> $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/GrimmNPC.json  Data: HarmonyConfig/NpcSpawn/" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed!" -ForegroundColor Red
    exit 1
}
