# Build script for SafeDeepSeaWipe Harmony Mod
# Output: <workspace>\HarmonyMods\SafeDeepSeaWipe.dll

Write-Host "Building SafeDeepSeaWipe..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "SafeDeepSeaWipe.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\SafeDeepSeaWipe.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\SafeDeepSeaWipe.dll"
    }
    $destPath = Join-Path $harmonyModsPath "SafeDeepSeaWipe.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! SafeDeepSeaWipe.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load SafeDeepSeaWipe" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
