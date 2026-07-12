# Build script for AdminAlias Harmony Mod
# Output: HarmonyMods/AdminAlias.dll (server root)

Write-Host "Building AdminAlias..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "AdminAlias.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = if ($env:OXIDE_SERVER_ROOT) { $env:OXIDE_SERVER_ROOT } else { "D:\!RustServer" }
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\AdminAlias.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\AdminAlias.dll"
    }
    $destPath = Join-Path $harmonyModsPath "AdminAlias.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! AdminAlias.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Add your Steam64 ID and alias to HarmonyConfig/AdminAlias.json. Load with: harmony.load AdminAlias" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
