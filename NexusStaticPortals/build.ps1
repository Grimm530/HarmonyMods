$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
    $projectPath = Join-Path $PSScriptRoot 'NexusStaticPortals.csproj'

    $managed = $null
    if ($env:RUST_MANAGED -and (Test-Path -LiteralPath $env:RUST_MANAGED)) {
        $managed = $env:RUST_MANAGED
    }

    if (-not $managed) {
        $candidates = @(
            (Join-Path $serverRoot 'RustDedicated_Data\Managed')
        )

        foreach ($candidate in $candidates) {
            if (Test-Path -LiteralPath (Join-Path $candidate 'Rust.Harmony.dll')) {
                $managed = $candidate
                break
            }
        }
    }

    if (-not $managed) {
        Write-Host @'
Could not find Rust managed assemblies.

Set:
  $env:RUST_MANAGED = "C:\Path\To\RustDedicated_Data\Managed"

Then run:
  powershell -ExecutionPolicy Bypass -File build.ps1
'@ -ForegroundColor Yellow
        exit 1
    }

    Write-Host "Building NexusStaticPortals (Rust Managed: $managed)..." -ForegroundColor Cyan
    dotnet build $projectPath -c Release -p:RustManagedPath="$managed"
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'Build failed.' -ForegroundColor Red
        exit 1
    }

    $harmonyModsPath = Join-Path $serverRoot 'HarmonyMods'
    if (-not (Test-Path -LiteralPath $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot 'bin\Release\net48\NexusStaticPortals.dll'
    if (-not (Test-Path -LiteralPath $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot 'bin\Release\NexusStaticPortals.dll'
    }

    $destPath = Join-Path $harmonyModsPath 'NexusStaticPortals.dll'
    Copy-Item -LiteralPath $dllPath -Destination $destPath -Force
    Write-Host "NexusStaticPortals.dll copied to: $destPath" -ForegroundColor Green
}
finally {
    Pop-Location
}
