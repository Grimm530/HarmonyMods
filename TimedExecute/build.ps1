# Build TimedExecute Harmony mod
# Copies only TimedExecute.dll into server HarmonyMods/

Write-Host "Building TimedExecute..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "TimedExecute.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\TimedExecute.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\TimedExecute.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "TimedExecute.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! TimedExecute.dll -> $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/TimedExecute.json" -ForegroundColor Yellow
    Write-Host "Load with: harmony.load TimedExecute" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed!" -ForegroundColor Red
    exit 1
}
