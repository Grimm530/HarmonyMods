$ErrorActionPreference = "Stop"

Write-Host "Building GrimmNPC2..."

$defaultManaged = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\..\RustDedicated_Data\Managed"))
$managedRoot = if ($env:RUST_MANAGED) { $env:RUST_MANAGED.Trim().TrimEnd('\', '/') } else { $defaultManaged }

$asm = Join-Path $managedRoot "Assembly-CSharp.dll"
if (-not (Test-Path $asm)) {
    Write-Host ""
    Write-Host "ERROR: Rust Managed assemblies not found."
    Write-Host "  Looked for: $asm"
    Write-Host ""
    Write-Host "Install the Rust dedicated server under this repo (RustDedicated_Data\Managed\), or set:"
    Write-Host '  $env:RUST_MANAGED = "D:\path\to\RustDedicated_Data\Managed"'
    Write-Host ""
    exit 1
}

$managedArg = $managedRoot + [System.IO.Path]::DirectorySeparatorChar
dotnet build "$PSScriptRoot\GrimmNPC2.csproj" -c Release "-p:RustManagedDir=$managedArg"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed! Check errors above."
    exit $LASTEXITCODE
}

$dll = Join-Path $PSScriptRoot "bin\Release\net48\GrimmNPC2.dll"
$dstRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\..\HarmonyMods"))
$dst = Join-Path $dstRoot "GrimmNPC2.dll"
if (-not (Test-Path $dstRoot)) {
    New-Item -ItemType Directory -Path $dstRoot -Force | Out-Null
}
Copy-Item $dll $dst -Force
Write-Host "Build successful! GrimmNPC2.dll copied to $dst"
