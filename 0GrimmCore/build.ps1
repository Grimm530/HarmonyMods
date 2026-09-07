# Build 0GrimmCore — load before most mods (0G < 0P). Unified Hurt + AI cull.
Write-Host "Building 0GrimmCore..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "0GrimmCore.csproj"
$serverRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$harmonyModsPath = Join-Path $serverRoot "HarmonyMods"

dotnet build $projectPath -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

$dllPath = Join-Path $PSScriptRoot "bin\Release\net48\0GrimmCore.dll"
if (-not (Test-Path $dllPath)) {
    $dllPath = Join-Path $PSScriptRoot "bin\Release\0GrimmCore.dll"
}

if (-not (Test-Path $harmonyModsPath)) {
    New-Item -ItemType Directory -Path $harmonyModsPath | Out-Null
}

$destPath = Join-Path $harmonyModsPath "0GrimmCore.dll"
Copy-Item -LiteralPath $dllPath -Destination $destPath -Force
Write-Host "Build successful! 0GrimmCore.dll -> $destPath" -ForegroundColor Green
Write-Host "Reload: harmony.reload 0GrimmCore" -ForegroundColor Yellow
