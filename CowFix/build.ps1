# Build CowFix Harmony mod
# Output: <server root>\HarmonyMods\CowFix.dll

Write-Host "Building CowFix Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "CowFix.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\CowFix.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\CowFix.dll"
    }
    $destPath = Join-Path $harmonyModsPath "CowFix.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! CowFix.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load CowFix" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
