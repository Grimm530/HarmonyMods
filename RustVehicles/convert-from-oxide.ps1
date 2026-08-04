$ErrorActionPreference = "Stop"
$src = Join-Path $PSScriptRoot "..\..\Oxide.Plugins.Cant-Use\RustVehicles.cs"
$dst = Join-Path $PSScriptRoot "RustVehicles.cs"
if (-not (Test-Path $src)) { throw "Source not found: $src" }
$text = [System.IO.File]::ReadAllText($src)

# Strip Oxide runtime usings (keep nothing Oxide).
foreach ($using in @(
        "using Oxide.Core;",
        "using Oxide.Core.Plugins;",
        "using Oxide.Game.Rust;",
        "using System.Web;"
    )) {
    $text = [regex]::Replace($text, "(?m)^" + [regex]::Escape($using) + "\r?\n", "")
}

$text = $text.Replace("using static Oxide.Plugins.RustVehicles;", "using static RustVehiclesHarmony.RustVehicles;")
$text = $text.Replace("namespace Oxide.Plugins", "namespace RustVehiclesHarmony")

# Avoid DescriptionAttribute clash with System.ComponentModel while keeping DefaultValue.
$text = [regex]::Replace($text, "(?m)^using System\.ComponentModel;\r?\n", "")
$text = $text.Replace("[DefaultValue(", "[System.ComponentModel.DefaultValue(")

$newClass = @"
    /// <summary>
    /// RustVehicles 2.0.5 ported for Harmony (no Oxide). Logic matches the Oxide plugin; hosting differs.
    /// </summary>
    [Info("Rust Vehicles", "Grimm530", "2.0.5")]
    [Description("Allows players to buy vehicles and then spawn or store it")]
    public class RustVehicles : RustVehiclesPluginBase
"@
$pattern = '(?ms)^[ \t]*\[Info\("Rust Vehicles", "Grimm530", "2\.0\.5"\)\]\r?\n[ \t]*\[Description\([^\r\n]*\)\]\r?\n[ \t]*public class RustVehicles : RustPlugin'
$newText = [regex]::Replace($text, $pattern, { param($m) $newClass.TrimEnd() }, 1)
if ($newText -eq $text) { throw "Could not find RustVehicles class declaration to replace" }
$text = $newText

# Drop PluginReference attributes; fields stay internal (non-readonly) so bridges can assign.
$text = $text.Replace("[PluginReference] private readonly Plugin", "internal Plugin")
$text = $text.Replace("[PluginReference] private Plugin", "internal Plugin")
$text = $text.Replace("private readonly Plugin", "internal Plugin")
# Multi-line PluginReference block: "[PluginReference] private readonly Plugin \n Economics, ..."
$text = [regex]::Replace($text, '(?m)^(\s*)\[PluginReference\]\s*\r?\n(\s*)private readonly Plugin', '${1}internal Plugin')
$text = [regex]::Replace($text, '(?m)^(\s*)\[PluginReference\]\s+private readonly Plugin', '${1}internal Plugin')

# KEEP [ConsoleCommand] / [ChatCommand] — HarmonyMod discovers them.

# Hook + command methods need patch/bridge access.
$methods = @(
    "Init", "OnServerInitialized", "Unload", "OnServerSave", "OnNewSave",
    "OnEntityDeath", "OnEntityKill", "OnPlayerDisconnected", "OnEntityReskin",
    "OnTurretTarget", "OnSwitchToggled", "OnServerCommand", "CanMountEntity",
    "OnEntityTakeDamage", "OnEntityEnter", "CanLootEntity", "OnEntitySpawned",
    "OnRidableAnimalClaimed", "OnEntityDismounted", "OnEngineStarted", "OnVehiclePush",
    "CmdPickup", "CmdDiscoverCustomVehicles", "CmdUniversal", "CmdCustomKill",
    "CmdLicenseHelp", "CmdBuyVehicle", "CmdSpawnVehicle", "CmdRecallVehicle", "CmdKillVehicle",
    "CCmdRemoveVehicle", "CCmdDumpCommands", "CCmdClearVehicle", "CCmdReloadCustom",
    "CCmdBuyVehicle", "CCmdSpawnVehicle", "CCmdRecallVehicle", "CCmdKillVehicle",
    "ManualWipeCMD"
)
foreach ($method in $methods) {
    $text = [regex]::Replace(
        $text,
        "(?m)^(\s*)(?:private |internal |protected |public )?(void|object|bool|string) " + [regex]::Escape($method) + "\(",
        '${1}internal $2 ' + $method + '(')
}

# Data path → HarmonyData/RustVehicles/RustVehicles.json
$text = $text.Replace(
    'Interface.Oxide.DataFileSystem.ReadObject<VehicleDatabase>(Name)',
    'Interface.Oxide.DataFileSystem.ReadObject<VehicleDatabase>("RustVehicles/RustVehicles")')
$text = $text.Replace(
    'Interface.Oxide.DataFileSystem.WriteObject(Name, vehicleDatabase)',
    'Interface.Oxide.DataFileSystem.WriteObject("RustVehicles/RustVehicles", vehicleDatabase)')

# HttpUtility.UrlEncode → Uri.EscapeDataString
$text = $text.Replace("HttpUtility.UrlEncode", "Uri.EscapeDataString")

# Oxide webrequest enums → local stubs
$text = $text.Replace("Core.Libraries.RequestMethod", "RequestMethod")
$text = $text.Replace("Oxide.Core.Libraries.RequestMethod", "RequestMethod")
$text = $text.Replace("Core.Libraries.DecompressionMethods", "DecompressionMethods")
$text = $text.Replace("Oxide.Core.Libraries.DecompressionMethods", "DecompressionMethods")

# Constructor + Version
$ctorMarker = "        private readonly string PERMISSION_USE = `"RustVehicles.use`";"
$ctor = @"
        public RustVehicles()
        {
            Version = new VersionNumber(2, 0, 5);
        }

        private readonly string PERMISSION_USE = `"RustVehicles.use`";
"@
if (-not $text.Contains($ctorMarker)) { throw "ctor marker missing (PERMISSION_USE)" }
$text = $text.Replace($ctorMarker, $ctor)

# Wire optional plugin bridges after Instance assignment in Init.
$text = [regex]::Replace(
    $text,
    '(internal void Init\(\)\s*\{\s*LoadData\(\);\s*Instance = this;)',
    {
        param($m)
        $m.Groups[1].Value + @"

            Economics = PluginBridges.Economics;
            ServerRewards = PluginBridges.ServerRewards;
            Friends = PluginBridges.Friends;
            Clans = PluginBridges.Clans;
            NoEscape = PluginBridges.NoEscape;
            LandOnCargoShip = PluginBridges.LandOnCargoShip;
            RustTranslationAPI = PluginBridges.RustTranslationAPI;
            ZoneManager = PluginBridges.ZoneManager;
            CustomEntities = PluginBridges.CustomEntities;
            RustCar = PluginBridges.RustCar;
            RustPlane = PluginBridges.RustPlane;
            RustHelicopter = PluginBridges.RustHelicopter;
            KaruzaVehicleChatCommand = PluginBridges.KaruzaVehicleChatCommand;
"@
    },
    1)

$harmonyLifecycle = @"

        // ---- Harmony lifecycle (replaces Oxide Init / OnServerInitialized / Unload) ----
        public override void HarmonyInit()
        {
            LoadConfig();
            LoadDefaultMessages();
            Init();
        }

        public override void HarmonyServerInitialized(bool initial = true)
        {
            OnServerInitialized(initial);
        }

        public override void HarmonyUnload()
        {
            Unload();
        }
"@
# Prefer inserting after Unload(); base may only declare parameterless HarmonyServerInitialized.
$unloadEnd = '(?ms)(internal void Unload\(\)\s*\{.*?\n        \})'
if (-not [regex]::IsMatch($text, $unloadEnd)) { throw "Unload method marker missing" }
$text = [regex]::Replace($text, $unloadEnd, { param($m) $m.Groups[1].Value + $harmonyLifecycle }, 1)

# Fix HarmonyServerInitialized signature to match abstract base (no bool) — wrap internally.
$text = $text.Replace(
    "public override void HarmonyServerInitialized(bool initial = true)`r`n        {`r`n            OnServerInitialized(initial);`r`n        }",
    "public override void HarmonyServerInitialized()`r`n        {`r`n            OnServerInitialized(RustVehiclesHarmonyMod.IsFirstServerInit);`r`n        }")
$text = $text.Replace(
    "public override void HarmonyServerInitialized(bool initial = true)`n        {`n            OnServerInitialized(initial);`n        }",
    "public override void HarmonyServerInitialized()`n        {`n            OnServerInitialized(RustVehiclesHarmonyMod.IsFirstServerInit);`n        }")

[System.IO.File]::WriteAllText($dst, $text)
Write-Host "Wrote $dst ($((($text -split "`n").Count)) lines)"

$checks = @(
    @{ Name = "Oxide.Core using"; Pattern = "using Oxide\.Core" },
    @{ Name = "RustPlugin"; Pattern = "RustPlugin" },
    @{ Name = "[ConsoleCommand]"; Pattern = "\[ConsoleCommand" },
    @{ Name = "[ChatCommand]"; Pattern = "\[ChatCommand" },
    @{ Name = "namespace Oxide.Plugins"; Pattern = "namespace Oxide\.Plugins\b" },
    @{ Name = "HarmonyInit"; Pattern = "HarmonyInit" },
    @{ Name = "RustVehiclesPluginBase"; Pattern = "RustVehiclesPluginBase" },
    @{ Name = "HttpUtility"; Pattern = "HttpUtility" },
    @{ Name = "System.Web"; Pattern = "System\.Web" },
    @{ Name = "RustVehicles/RustVehicles data"; Pattern = 'RustVehicles/RustVehicles' },
    @{ Name = "PluginBridges.Economics"; Pattern = "PluginBridges\.Economics" }
)
foreach ($check in $checks) {
    Write-Host ("{0}: {1}" -f $check.Name, ([regex]::Matches($text, $check.Pattern)).Count)
}
