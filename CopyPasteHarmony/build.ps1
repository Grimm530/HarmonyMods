# Build script for CopyPaste Harmony Mod
# Output: <workspace>\HarmonyMods\CopyPasteHarmony.dll

Write-Host "Building CopyPasteHarmony..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "CopyPasteHarmony.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\CopyPasteHarmony.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\CopyPasteHarmony.dll"
    }

    $destPath = Join-Path $harmonyModsPath "CopyPasteHarmony.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force

    Write-Host "`nBuild successful! CopyPasteHarmony.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/CopyPaste.json (auto-created)" -ForegroundColor Yellow
    Write-Host "Commands: /copy, /paste, /copylist, /pasteback, /undo (chat); or F1 console 'copy ...'" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}

