# Build script for Convoy Harmony Mod
# Output: <server root>\HarmonyMods\Convoy.dll

Write-Host "Building Convoy..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "Convoy\Convoy.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $outDir = Join-Path $PSScriptRoot "Convoy\bin\Release"
    $dllPath = Join-Path $outDir "net48\Convoy.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $outDir "Convoy.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found under Convoy\bin\Release" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "Convoy.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! Convoy.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load Convoy" -ForegroundColor Yellow
    Write-Host "Commands: /convoystart, /convoystop (admin). Config: Convoy.json in oxide/config/, HarmonyConfig/, or root" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
