# Build script for the older GrimmNPC Harmony project (CustomNpcData / RegisterPending).
# Live server NPCs use 0GrimmNPC (NpcSpawn port). Do not copy over HarmonyMods/0GrimmNPC.dll.

Write-Host "Building GrimmNPC (legacy CustomNpcData tree)..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "GrimmNPC.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\GrimmNPC.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\GrimmNPC.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }
    Write-Host "`nBuild successful! $dllPath" -ForegroundColor Green
    Write-Host "This project is not the live NPC runtime. Deploy 0GrimmNPC instead (harmony.load 0GrimmNPC)." -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
