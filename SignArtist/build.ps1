# Build SignArtist Harmony Mod
# Output: <server root>\HarmonyMods\SignArtist.dll (DLL only)

Write-Host "Building SignArtist Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "SignArtist.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\SignArtist.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\SignArtist.dll"
    }
    Copy-Item -Path $dllPath -Destination (Join-Path $harmonyModsPath "SignArtist.dll") -Force
    Write-Host "`nBuild successful! SignArtist.dll copied to HarmonyMods\" -ForegroundColor Green
} else {
    Write-Host "`nBuild failed!" -ForegroundColor Red
    exit 1
}
