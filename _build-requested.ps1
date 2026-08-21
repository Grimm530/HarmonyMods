# Orchestrator: build requested Harmony mods and deploy DLLs to this workspace HarmonyMods
# Runs each mod's build.ps1 in an isolated child process (so 'exit' can't kill us),
# falls back to 'dotnet build' when no build.ps1 exists, then copies the fresh DLL
# from bin\Release into the runtime HarmonyMods folder.
$ErrorActionPreference = 'Continue'

$root   = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$src    = Join-Path $root '.cursor\HarmonyMods'
$deploy = Join-Path $root 'HarmonyMods'

$env:RUST_MANAGED = Join-Path $root 'RustDedicated_Data\Managed'

# Requested DLL name  ->  source folder under .cursor\HarmonyMods
$map = [ordered]@{
  'AdminAlias'              = 'AdminAlias'
  'AdminTime'               = 'AdminTime'
  'AdminTools'              = $null            # no source present
  'AlphaLoot'               = 'AlphaLoot'
  'AlwaysBonus'             = 'AlwaysBonus'
  'BagCooldowns'            = 'BagCooldowns'
  'BetterAirDrop'           = 'BetterAirDrop'
  'BetterBackpack'          = 'BetterBackpack'
  'ChatFilter'              = 'ChatFilter'
  'ChatIcons'               = 'ChatIcons'
  'ChatTranslator'          = 'ChatTranslator'
  'CommandHistory'          = 'CommandHistory'
  'CommunityTab'            = 'CommunityTab'
  'Convoy'                  = 'Convoy'
  'ConvoyKits'              = 'ConvoyKits'
  'CraftingSpeed'           = 'CraftingSpeed'
  'CustomMapGen'            = 'CustomMapGen'
  'DeveloperListOverride'   = 'DeveloperListOverride'
  'DiscordLinks'            = 'DiscordLinks'
  'FakePopulation'          = 'FakePopulation'
  'FullRangeAutoturrets'    = 'FullRangeTurrets'
  'FurnaceSplitter'         = 'FurnaceSplitter'
  'GatherManager'           = 'GatherManager'
  '0GrimmNPC'               = '0GrimmNPC'
  'HideAdminActions'        = 'HideAdminActions'
  'IndustrialTransferSpeed' = 'IndustrialTransferSpeed'
  'InstantBarrel'           = 'InstantBarrel'
  'Leaderboard'             = 'Leaderboard'
  'MapVoter'                = 'MapVoter'
  'MixingSpeed'             = 'MixingSpeed'
  'NoActiveItemDrop'        = 'NoActiveItemDrop'
  'NoGibs'                  = 'NoGibs'
  'Prodigy'                 = 'Prodigy'
  'Radar'                   = 'Radar'
  'RecyclerSpeed'           = 'RecyclerSpeed'
  'Rustcord'                = 'Rustcord'
  'SafeZonePVE'             = 'SafeZonePVE'
  'ShorterNights'           = 'ShorterNights'
  'SmeltingSpeed'           = 'SmeltingSpeed'
  'StackManager'            = 'StackManager'
  'TCUpgrade'               = 'TCUpgrade'
  'TeleportGUI'             = 'TeleportGUI'
  'Thorium'                 = 'Thorium.Rust-main'
  'TranslationAPI'          = 'TranslationAPI'
  'TruePVE'                 = 'TruePVE'
  'UnlockTier1'             = 'UnlockTier1'
  'Vanish'                  = 'Vanish'
}

$logDir = Join-Path $src '_buildlogs'
New-Item -ItemType Directory -Path $logDir -Force | Out-Null

$results = @()
$startAll = Get-Date

foreach ($name in $map.Keys) {
  $folder = $map[$name]
  $rec = [ordered]@{ Name = $name; Folder = $folder; Compiled = $false; Deployed = $false; Note = '' }

  if (-not $folder) {
    $rec.Note = 'NO SOURCE (kept existing runtime DLL)'
    $results += [pscustomobject]$rec
    Write-Host ("[SKIP] {0}: {1}" -f $name, $rec.Note) -ForegroundColor Yellow
    continue
  }

  $modDir = Join-Path $src $folder
  if (-not (Test-Path $modDir)) {
    $rec.Note = 'folder missing'
    $results += [pscustomobject]$rec
    Write-Host ("[FAIL] {0}: folder missing" -f $name) -ForegroundColor Red
    continue
  }

  Write-Host ("`n========== {0} ({1}) ==========" -f $name, $folder) -ForegroundColor Cyan
  $log = Join-Path $logDir ("$name.log")

  # Find build.ps1 (prefer shallowest)
  $buildScript = Get-ChildItem $modDir -Recurse -Filter build.ps1 -ErrorAction SilentlyContinue |
                 Sort-Object { $_.FullName.Length } | Select-Object -First 1

  if ($buildScript) {
    $rec.Note = 'build.ps1'
    & powershell -NoProfile -ExecutionPolicy Bypass -File $buildScript.FullName *>&1 |
        Tee-Object -FilePath $log | Out-Null
  } else {
    # Fall back to dotnet build on the csproj
    $csproj = Get-ChildItem $modDir -Recurse -Filter *.csproj -ErrorAction SilentlyContinue |
              Sort-Object { $_.FullName.Length } | Select-Object -First 1
    if (-not $csproj) {
      $rec.Note = 'no build.ps1 and no csproj'
      $results += [pscustomobject]$rec
      Write-Host ("[FAIL] {0}: no build.ps1 and no csproj" -f $name) -ForegroundColor Red
      continue
    }
    $rec.Note = "dotnet build ($($csproj.Name))"
    if ($name -eq 'Thorium') {
      # Thorium uses custom Linux;Windows configs
      & dotnet build $csproj.FullName -c Windows *>&1 | Tee-Object -FilePath $log | Out-Null
    } else {
      & dotnet build $csproj.FullName -c Release *>&1 | Tee-Object -FilePath $log | Out-Null
    }
  }

  # Locate freshly built DLL (prefer exact requested name), built within this run
  $dll = Get-ChildItem $modDir -Recurse -Filter "$name.dll" -ErrorAction SilentlyContinue |
         Where-Object { $_.FullName -match '\\bin\\' -and $_.LastWriteTime -ge $startAll } |
         Sort-Object LastWriteTime -Descending | Select-Object -First 1

  if (-not $dll) {
    # try any dll built this run under bin (assembly name may differ from folder)
    $dll = Get-ChildItem $modDir -Recurse -Filter '*.dll' -ErrorAction SilentlyContinue |
           Where-Object { $_.FullName -match '\\bin\\Release\\' -and $_.LastWriteTime -ge $startAll -and
                          $_.Name -notmatch '^(0Harmony|Newtonsoft|Oxide|System\.|Microsoft\.|UnityEngine)' } |
           Sort-Object LastWriteTime -Descending | Select-Object -First 1
  }

  if ($dll) {
    $rec.Compiled = $true
    try {
      Copy-Item -Path $dll.FullName -Destination (Join-Path $deploy "$name.dll") -Force
      $rec.Deployed = $true
      Write-Host ("[OK]   {0}: compiled -> {1}.dll ({2} bytes)" -f $name, $name, $dll.Length) -ForegroundColor Green
    } catch {
      $rec.Note += " | deploy copy failed: $($_.Exception.Message)"
      Write-Host ("[WARN] {0}: compiled but copy failed" -f $name) -ForegroundColor Yellow
    }
  } else {
    Write-Host ("[FAIL] {0}: no fresh DLL produced (see _buildlogs\{0}.log)" -f $name) -ForegroundColor Red
  }

  $results += [pscustomobject]$rec
}

Write-Host "`n`n================= SUMMARY =================" -ForegroundColor Cyan
$results | Format-Table Name, Compiled, Deployed, Note -AutoSize

$fail = $results | Where-Object { -not $_.Compiled -and $_.Folder }
$noSrc = $results | Where-Object { -not $_.Folder }
Write-Host ("`nCompiled+Deployed: {0}/{1} buildable mods" -f (@($results | Where-Object Deployed).Count), (@($results | Where-Object Folder).Count)) -ForegroundColor Green
if ($fail)  { Write-Host ("Failed to compile: {0}" -f (($fail.Name) -join ', ')) -ForegroundColor Red }
if ($noSrc) { Write-Host ("No source (not built): {0}" -f (($noSrc.Name) -join ', ')) -ForegroundColor Yellow }
Write-Host ("Elapsed: {0:n1} min" -f ((Get-Date) - $startAll).TotalMinutes)
