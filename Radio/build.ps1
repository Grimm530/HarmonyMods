# Build Radio Harmony Mod
# Output: <server root>\HarmonyMods\Radio.dll (DLL only)

Write-Host "Building Radio Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "Radio.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\Radio.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\Radio.dll"
    }
    Copy-Item -Path $dllPath -Destination (Join-Path $harmonyModsPath "Radio.dll") -Force
    Write-Host "`nBuild successful! Radio.dll copied to HarmonyMods\" -ForegroundColor Green
} else {
    Write-Host "`nBuild failed!" -ForegroundColor Red
    exit 1
}
