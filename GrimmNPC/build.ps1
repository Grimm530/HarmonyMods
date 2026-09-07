# Build script for GrimmNPC Harmony Mod
# Output: D:\!RustServer\HarmonyMods\GrimmNPC.dll

Write-Host "Building GrimmNPC..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "GrimmNPC.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $harmonyModsPath = "D:\!RustServer\HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\GrimmNPC.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\GrimmNPC.dll"
    }
    $destPath = Join-Path $harmonyModsPath "GrimmNPC.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! GrimmNPC.dll copied to $destPath" -ForegroundColor Green
    Write-Host "The mod will load automatically on next server start (harmony.load GrimmNPC)." -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
