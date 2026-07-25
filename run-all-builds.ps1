# Run all build.ps1 in HarmonyMods subfolders. Output: D:\!RustServer\HarmonyMods
$ErrorActionPreference = "Continue"
$base = $PSScriptRoot
$dirs = @(
  'AdminTools','AlphaLoot','AlwaysBonus','BagCooldowns','BetterAirDrop','BetterBackpack','ChatIcons','ChatTranslator','CommandHistory',
  'CommunityTab','CraftingSpeed','CustomMapGen','DiscordLinks','FakePopulation','FullRangeTurrets','FurnaceSplitter','GatherManager','0GrimmNPC','GrimmNPCOrigional',
  'HarmonyCustomGenerator-0.2.2','HideAdminActions','IndustrialTransferSpeed','InstantBarrel','Leaderboard','MapVoter','MixingSpeed','NoGibs','Radar','RecyclerSpeed',
  'Rust-Server-Metrics-master','Rustcord','RustEditStandalone','ShorterNights','SmeltingSpeed','StackManager','TCUpgrade','TranslationAPI','UnlockTier1','Vanish'
)
$failed = @()
foreach ($dir in $dirs) {
  $script = Join-Path $base (Join-Path $dir 'build.ps1')
  if (-not (Test-Path $script)) { continue }
  Write-Host "`n========== $dir ==========" -ForegroundColor Cyan
  Push-Location (Join-Path $base $dir)
  try {
    if ($dir -eq 'Rust-Server-Metrics-master') {
      & .\build.ps1 -NonInteractive
    } else {
      & .\build.ps1
    }
    if ($LASTEXITCODE -ne 0) { $failed += $dir }
  } catch {
    $failed += $dir
    Write-Host $_.Exception.Message -ForegroundColor Red
  }
  Pop-Location
}
if ($failed.Count -gt 0) {
  Write-Host "`nFailed: $($failed -join ', ')" -ForegroundColor Red
  exit 1
}
Write-Host "`nAll builds completed." -ForegroundColor Green
exit 0
