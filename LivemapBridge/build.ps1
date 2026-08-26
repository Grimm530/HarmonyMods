Write-Host "Building LivemapBridge..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "LivemapBridge.csproj"
dotnet build $projectPath -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

$serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
if (-not (Test-Path $harmonyModsPath)) {
    New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
}

$dllPath = Join-Path $PSScriptRoot "bin\Release\LivemapBridge.dll"
if (-not (Test-Path $dllPath)) {
    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\LivemapBridge.dll"
}
if (-not (Test-Path $dllPath)) {
    Write-Host "Build output not found" -ForegroundColor Red
    exit 1
}

$destPath = Join-Path $harmonyModsPath "LivemapBridge.dll"
Copy-Item -Path $dllPath -Destination $destPath -Force
Write-Host "LivemapBridge.dll -> $destPath" -ForegroundColor Green
Write-Host "Load with: harmony.load LivemapBridge" -ForegroundColor Yellow
