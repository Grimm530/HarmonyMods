# Split adapted RaidableBases monolith by regions (3.1.5)
# Prefer adapt-3.1.5.ps1 for full Oxideâ†’Harmony adaptation.
$ErrorActionPreference = "Stop"
$srcPath = Join-Path $PSScriptRoot "_adapted_3.1.5.cs"
$outDir = $PSScriptRoot
Write-Host "Use adapt-3.1.5.ps1 instead. Regions from last run:"
Write-Host "Main 1-13331; Hooks 13332-16137; Spawn 16139-16253; Paste 16255-17649"
Write-Host "Commands 17651-20302; Garbage 20304-20576; IQ 20578-20601"
Write-Host "Helpers 20603-22320; Data 22322-23318; Config 23320-28540; UI 28542-29591"