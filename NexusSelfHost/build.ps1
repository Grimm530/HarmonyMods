# Build script for NexusSelfHost Harmony Mod
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    # NexusSelfHost lives at <repo>\.cursor\NexusSelfHost → repo is two levels up (not three).
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

    $managed = $null
    if ($env:RUST_MANAGED -and (Test-Path -LiteralPath $env:RUST_MANAGED)) {
        $managed = $env:RUST_MANAGED
    }
    if (-not $managed) {
        $candidates = @(
            (Join-Path $repoRoot 'RustDedicated_Data\Managed')
            'D:\!RustServer\RustDedicated_Data\Managed'
        )
        foreach ($c in $candidates) {
            if (Test-Path -LiteralPath (Join-Path $c 'Rust.Harmony.dll')) {
                $managed = $c
                break
            }
        }
    }

    if (-not $managed) {
        Write-Host @'
Could not find Rust managed assemblies (Rust.Harmony.dll, etc.).

Set the folder that contains them, then run this script again:

  $env:RUST_MANAGED = 'C:\Grimmzone\RustDedicated_Data\Managed'   # your Rust server install

Or pass to MSBuild:

  dotnet build NexusSelfHost.csproj -c Release -p:RustManagedPath="C:\Grimmzone\RustDedicated_Data\Managed"

That path must be the game's Managed folder (same dir as Rust.Harmony.dll, 0Harmony.dll, Facepunch.Nexus.dll).
'@ -ForegroundColor Yellow
        exit 1
    }

    Write-Host "Building NexusSelfHost (Rust Managed: $managed)..." -ForegroundColor Cyan
    dotnet build NexusSelfHost.csproj -c Release -p:RustManagedPath="$managed"
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'Build failed!' -ForegroundColor Red
        exit 1
    }

    $harmonyModsPath = 'D:\!RustServer\HarmonyMods'
    if (-not (Test-Path -LiteralPath $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath | Out-Null
    }
    $dllPath = Join-Path $PSScriptRoot 'bin\Release\net48\NexusSelfHost.dll'
    Copy-Item -LiteralPath $dllPath -Destination (Join-Path $harmonyModsPath 'NexusSelfHost.dll') -Force
    Write-Host "NexusSelfHost.dll copied to: $(Join-Path $harmonyModsPath 'NexusSelfHost.dll')" -ForegroundColor Green
} finally { Pop-Location }
