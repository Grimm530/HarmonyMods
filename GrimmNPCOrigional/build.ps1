# Build script for GrimmNPC Harmony Mod
Write-Host "Building GrimmNPC..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "GrimmNPC.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nBuild successful! Copying DLL to D:\!RustServer\HarmonyMods..." -ForegroundColor Green
    
    $harmonyModsPath = "D:\!RustServer\HarmonyMods"
    
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath | Out-Null
    }
    
    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\GrimmNPC.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\GrimmNPC.dll"
    }
    $destPath = Join-Path $harmonyModsPath "GrimmNPC.dll"
    
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nGrimmNPC.dll copied to $destPath" -ForegroundColor Green
    Write-Host "The mod will load automatically on next server start." -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
