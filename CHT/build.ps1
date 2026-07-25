Write-Host "Building CHT..." -ForegroundColor Cyan
$project=Join-Path $PSScriptRoot "CHT.csproj"
dotnet build $project -c Release
if($LASTEXITCODE -ne 0){exit 1}
$dest=Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")) "HarmonyMods\CHT.dll"
Copy-Item (Join-Path $PSScriptRoot "bin\Release\CHT.dll") $dest -Force
Write-Host "Built $dest" -ForegroundColor Green
