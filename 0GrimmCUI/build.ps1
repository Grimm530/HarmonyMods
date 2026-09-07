# Build 0GrimmCUI Harmony Mod (loads first alphabetically after 0Permissions)
Write-Host "Building 0GrimmCUI..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "0GrimmCUI.csproj"
$serverRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$harmonyModsPath = Join-Path $serverRoot "HarmonyMods"

dotnet build $projectPath -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

$dllPath = Join-Path $PSScriptRoot "bin\Release\net48\0GrimmCUI.dll"
if (-not (Test-Path $dllPath)) {
    $dllPath = Join-Path $PSScriptRoot "bin\Release\0GrimmCUI.dll"
}

if (-not (Test-Path $harmonyModsPath)) {
    New-Item -ItemType Directory -Path $harmonyModsPath | Out-Null
}

$destPath = Join-Path $harmonyModsPath "0GrimmCUI.dll"
Copy-Item -LiteralPath $dllPath -Destination $destPath -Force
Write-Host "Build successful! 0GrimmCUI.dll -> $destPath" -ForegroundColor Green
Write-Host "Load order: 0Permissions -> 0GrimmCUI -> 0GrimmNPC -> feature mods" -ForegroundColor Yellow
Write-Host "Reload: harmony.reload 0GrimmCUI (then reload consumer mods)" -ForegroundColor Yellow
