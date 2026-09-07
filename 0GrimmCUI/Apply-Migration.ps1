# Migrates Grimm Harmony mods to shared 0GrimmCUI foundation.
param(
    [switch]$WhatIf,
    [string[]]$OnlyMods
)

$ErrorActionPreference = "Stop"
$harmonyRoot = $PSScriptRoot | Split-Path -Parent
$grimmCuiDll = Join-Path (Split-Path $harmonyRoot -Parent | Split-Path -Parent) "HarmonyMods\0GrimmCUI.dll"
if (-not (Test-Path $grimmCuiDll)) {
    $grimmCuiDll = Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path "HarmonyMods\0GrimmCUI.dll"
}

function Get-RelativeHintPath([string]$modDir) {
    $d = Resolve-Path $modDir
    $target = Resolve-Path $grimmCuiDll
    # Build relative path for MSBuild HintPath
    $modParts = $d.Path.Split([char]'\\')
    $targetParts = $target.Path.Split([char]'\\')
    $common = 0
    while ($common -lt $modParts.Length -and $common -lt $targetParts.Length -and ($modParts[$common] -eq $targetParts[$common])) { $common++ }
    $up = @("..") * ($modParts.Length - $common)
    $down = $targetParts[$common..($targetParts.Length - 1)]
    return ($up + $down) -join '\'
}

# modFolder (relative to HarmonyMods) -> registration config
$manifest = @{
    "Kits" = @{ Marker = "KITS"; Type = "rebuilt"; Command = "UI_Kits"; Handler = "KitsHarmonyMod.Instance?.Plugin?.CmdKitsConsole"; Rewriter = "UI_Kits->KITS" }
    "Backpacks" = @{ Marker = "BP"; Type = "harmony"; Method = "HandleCuiEndtest"; Rewriter = "backpack" }
    "AdminMenu" = @{ Marker = "ADMINMENU"; Type = "harmony"; Method = "HandleCuiCallback" }
    "AutoCodeLock" = @{ Marker = "AUTOCODELOCK"; Type = "harmony"; Method = "HandleCuiCallback" }
    "CombatClasses" = @{ Marker = "CC"; Type = "harmony"; Method = "HandleCuiEndtest"; Rewriter = "dynamic_cc"; DragPrefix = "CC_UI_" }
    "SkillTree" = @{ Marker = "ST"; Type = "harmony"; Method = "HandleCuiEndtest"; Rewriter = "dynamic_st" }
    "Shop" = @{ Marker = "SHOP"; Type = "shop"; ExtraMarkers = @("SHOPINST") }
    "RustRewards" = @{ Marker = "RR"; Type = "harmony"; Method = "HandleCuiEndtest"; Rewriter = "rr" }
    "RaidableBases" = @{ Marker = "RBUI"; Type = "console"; DragPrefix = "RB_UI_"; NoRustCui = $true }
    "Quest" = @{ Marker = "QUEST"; Type = "harmony"; Method = "HandleCuiEndtest"; Rewriter = "dynamic" }
    "Hud" = @{ Marker = "HUD"; Type = "harmony"; Method = "HandleCuiEndtest"; Rewriter = "dynamic" }
    "HitMarkers" = @{ Marker = "HITMARKERS"; Type = "harmony"; Method = "HandleCuiEndtest"; Rewriter = "dynamic" }
    "KillFeed" = @{ Marker = "KILLFEED"; Type = "harmony"; Method = "HandleCuiEndtest"; Rewriter = "dynamic" }
    "JetPack" = @{ Marker = "JETPACK"; Type = "harmony"; Method = "HandleCuiEndtest"; Rewriter = "dynamic" }
    "IndustrialRecycler" = @{ Marker = "INDUSTRIALRECYCLER"; Type = "harmony"; Method = "HandleCuiEndtest"; Rewriter = "dynamic" }
    "RocketGuidanceSystem" = @{ Marker = "RGS"; Type = "harmony"; Method = "HandleCuiEndtest"; Rewriter = "dynamic" }
    "WaterBases" = @{ Marker = "WB"; Type = "harmony"; Method = "HandleCuiEndtest"; Rewriter = "dynamic_wb" }
    "VirtualItems" = @{ Marker = "VIRTUALITEMS"; Type = "harmony"; Method = "HandleCuiEndtest"; Rewriter = "dynamic" }
    "VirtualQuarries" = @{ Marker = "VIRTUALQUARRIES"; Type = "harmony"; Method = "HandleCuiEndtest"; Rewriter = "dynamic" }
    "UberTool" = @{ Marker = "UBERTOOL"; Type = "harmony"; Method = "HandleCuiEndtest"; Rewriter = "dynamic" }
    "UpLifted" = @{ Marker = "UPLIFTED"; Type = "harmony"; Method = "HandleCuiEndtest"; Rewriter = "dynamic" }
    "BradleyDrops" = @{ Marker = "BRADLEYDROPS"; Type = "harmony"; Method = "HandleCuiEndtest"; Rewriter = "dynamic" }
    "ZoneManager" = @{ Marker = "ZONEMANAGER"; Type = "harmony"; Method = "HandleCuiEndtest"; Rewriter = "zmui" }
    "WipeSchedule" = @{ Marker = "WIPESCHEDULE"; Type = "rebuilt"; Command = "command.wipe.schedule"; Handler = "WipeScheduleHarmonyMod.Instance?.Plugin?.CmdConsoleWipeSchedule"; Rewriter = "wipe" }
    "Cooking" = @{ Marker = "COOKING"; Type = "harmony"; Method = "HandleCuiEndtest"; Rewriter = "dynamic" }
    "LootQoL" = @{ Marker = "LOOTQOL"; Type = "harmony"; Method = "HandleCuiCallback"; Rewriter = "lootqol" }
    "RustLeague" = @{ Marker = "RUSTLEAGUE"; Type = "harmony"; Method = "HandleCuiCallback"; Rewriter = "rustleague" }
    "RustVehiclesGUI" = @{ Marker = "VGUI"; Type = "harmony"; Method = "InvokeConsoleCommand"; Rewriter = "vgui" }
    "ServerPanel" = @{ Marker = "SERVERPANEL"; Type = "serverpanel" }
    "PersonalNPC" = @{ Marker = "PNPC"; Type = "personalnpc"; ExtraMarkers = @("PNPCHELPER") }
    "PlayerSkins" = @{ Marker = "PLAYERSKINS"; Type = "harmony"; Method = "HandleCuiCallback" }
    "Minimap" = @{ Marker = "MINIMAP"; Type = "harmony"; Method = "HandleCuiCallback" }
    "StackManager" = @{ Marker = "STACKMANAGER"; Type = "harmony"; Method = "HandleCuiCallback" }
    "DynamicCupShare" = @{ Marker = "DYNAMICCUPSHARE"; Type = "harmony"; Method = "HandleCuiCallback" }
    "TeleportGUI\TeleportGUI" = @{ Marker = "TELEPORTGUI"; Type = "harmony"; Method = "HandleCuiEndtest" }
    "SortButton" = @{ Marker = "SORTBUTTON"; Type = "harmony"; Method = "HandleCui"; ModClass = "SortButtonMod" }
    "TCUpgrade\TCUpgrade" = @{ Marker = "SENDCMD"; Type = "harmony"; Method = "HandleSendCmdFromCui"; ModClass = "TCUpgradeMod" }
    "Radar" = @{ Marker = "RADAR"; Type = "harmony"; Method = "HandleCuiCommand"; ModClass = "RadarMod"; PrefixMatch = $true }
    "MapVoter" = @{ Marker = "MapVoter"; Type = "harmony"; Method = "HandleCuiCommand"; ModClass = "MapVoterMod"; PrefixMatch = $true }
    "AirbourneSpawn" = @{ Marker = "AIRBOURNESPAWN"; Type = "harmony"; Method = "HandleCuiCallback"; ModClass = "AirbourneSpawnMod" }
    "InventoryShortcuts" = @{ Marker = "INVSHORTCUTS"; Type = "custom_invshortcuts" }
    "RecyclerSpeed\RecyclerSpeed" = @{ Marker = "RECYCLER_SPEED"; Type = "harmony"; Method = "HandleCuiCommand"; ModClass = "RecyclerSpeedMod" }
    "Leaderboard" = @{ Marker = "LEADERBOARD"; Type = "leaderboard" }
    "Prodigy" = @{ Marker = "PRODIGY"; Type = "prodigy" }
    "IndustrialTransferSpeed\IndustrialTransferSpeed" = @{ Marker = "ITS_PLANTER_"; Type = "harmony"; Method = "HandlePlanterCuiCommand"; ModClass = "IndustrialTransferSpeedMod"; PrefixMatch = $true }
    "CHT\CHT" = @{ Marker = "CHT"; Type = "rebuilt"; Command = "cht.shopcontroller"; Handler = "CHTMod.Instance?.Plugin?.cmdShopController"; Rewriter = "cht" }
    "RaidableBasesUI\RaidableBasesUI" = @{ Marker = "RB_UI"; Type = "rbui_legacy" }
}

function Update-Csproj([string]$csprojPath, [string]$modDir) {
    [xml]$xml = Get-Content $csprojPath
    $ns = $xml.Project.ItemGroup
    if (-not $ns) { return }

    $hint = Get-RelativeHintPath $modDir
    $projContent = Get-Content $csprojPath -Raw
    if ($projContent -notmatch "0GrimmCUI") {
        $refBlock = @"

  <ItemGroup>
    <Reference Include="0GrimmCUI">
      <HintPath>$hint</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
"@
        $projContent = $projContent -replace '</Project>', "$refBlock`n</Project>"
    }

    $removes = @(
        "RustCui.cs",
        "Patches\Cui_Endtest_Patch.cs",
        "Patches\Patch_CuiDrag.cs",
        "Patches\Cui_DragRPC_Patch.cs",
        "Patches\CuiEndtestPatch.cs",
        "Patches\CuiEndtestRebind.cs",
        "Patches\Chat_Cui_Patches.cs",
        "Patches\GameHooks_Patches.cs",
        "Patches\Game_Patches.cs",
        "Patches\Patch_Cui_Endtest_Leaderboard.cs",
        "Patches\Patch_Cui_Endtest_Prodigy.cs",
        "Patches\Cui_Endtest_PlanterProduction_Patch.cs"
    )
    foreach ($r in $removes) {
        if ($projContent -notmatch [regex]::Escape("<Compile Remove=`"$r`"")) {
            $projContent = $projContent -replace '(<ItemGroup>\s*\r?\n\s*<Compile Remove="obj)', "  <Compile Remove=`"$r`" />`n    `$1"
        }
    }
    if ($projContent -notmatch '<Compile Remove="RustCui.cs"') {
        $insert = "  <ItemGroup>`n    <Compile Remove=`"RustCui.cs`" />`n    <Compile Remove=`"Patches\Cui_Endtest_Patch.cs`" />`n    <Compile Remove=`"Patches\Patch_CuiDrag.cs`" />`n    <Compile Remove=`"Patches\Cui_DragRPC_Patch.cs`" />`n  </ItemGroup>`n"
        $projContent = $projContent -replace '</Project>', "$insert</Project>"
    }
    if (-not $WhatIf) { Set-Content -Path $csprojPath -Value $projContent -NoNewline }
}

Write-Host "0GrimmCUI migration script - run with -WhatIf first" -ForegroundColor Cyan
Write-Host "GrimmCUI DLL: $grimmCuiDll" -ForegroundColor Gray
Write-Host "Manifest entries: $($manifest.Count)" -ForegroundColor Gray
