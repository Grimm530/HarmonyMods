# Build script for SkillTree Harmony Mod
# Output: <server root>\HarmonyMods\SkillTree.dll
# Config: HarmonyConfig/SkillTree.json
# Data:   HarmonyData/SkillTree/ (default) or CustomSkillTreeDataDirectory from config

Write-Host "Building SkillTree Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "SkillTree.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    # Resolve server root (3 levels up from .cursor/HarmonyMods/SkillTree/).
    $root = $env:RUST_SERVER_ROOT
    if (-not $root) {
        $candidate = Join-Path $PSScriptRoot "..\..\..\"
        $root = [System.IO.Path]::GetFullPath($candidate)
    }

    $harmonyModsPath = Join-Path $root "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    # Find the built DLL (net48 or flat Release output).
    $dllPath = Join-Path $PSScriptRoot "bin\Release\SkillTree.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\SkillTree.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found under bin\Release\SkillTree.dll" -ForegroundColor Red
        exit 1
    }

    $destPath = Join-Path $harmonyModsPath "SkillTree.dll"
    # Copy ONLY the mod DLL — never referenced Rust/Unity assemblies.
    Copy-Item -Path $dllPath -Destination $destPath -Force

    Write-Host ""
    Write-Host "Build successful!  SkillTree.dll -> $destPath" -ForegroundColor Green
    Write-Host "Config:  HarmonyConfig/SkillTree.json"         -ForegroundColor Yellow
    Write-Host "Data:    HarmonyData/  (or CustomSkillTreeDataDirectory in config)" -ForegroundColor Yellow
    Write-Host "Load:    auto on startup (alphabetical). Binds Permissions + MovementSpeed via ready callbacks." -ForegroundColor Gray
} else {
    Write-Host ""
    Write-Host "Build FAILED! Check errors above." -ForegroundColor Red
    exit 1
}
