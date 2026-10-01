# Build script for AlphaLoot Harmony Mod
# Output: <server root>\HarmonyMods\AlphaLoot.dll

Write-Host "Building AlphaLoot Harmony mod..." -ForegroundColor Cyan

# Stale Assembly-CSharp copies in bin/ break ItemManager.Create overload resolution.
Get-ChildItem $PSScriptRoot -Directory -Recurse -Filter "bin" -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
Get-ChildItem $PSScriptRoot -Directory -Recurse -Filter "obj" -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }

$projectPath = Join-Path $PSScriptRoot "AlphaLoot.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\AlphaLoot.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\AlphaLoot.dll"
    }
    $destPath = Join-Path $harmonyModsPath "AlphaLoot.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! AlphaLoot.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/AlphaLoot.json | Data: HarmonyData/AlphaLoot/LootProfiles/" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
