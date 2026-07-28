# Build script for StackManager Harmony Mod
# Output: <serverRoot>/HarmonyMods/StackManager.dll

Write-Host "Building StackManager..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "StackManager.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    # Prefer bin\Release (AppendTargetFrameworkToOutputPath=false), then net48 fallback.
    $dllPath = Join-Path $PSScriptRoot "bin\Release\StackManager.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\StackManager.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }

    $destPath = Join-Path $harmonyModsPath "StackManager.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! StackManager.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load StackManager (after harmony.load 0Permissions)." -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
