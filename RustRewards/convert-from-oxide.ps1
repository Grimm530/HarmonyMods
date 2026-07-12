$ErrorActionPreference = "Stop"
$src = Join-Path $PSScriptRoot "..\..\Oxide.Plugins.Cant-Use\RustRewards.cs"
$dst = Join-Path $PSScriptRoot "RustRewards.cs"
if (-not (Test-Path $src)) { throw "Source not found: $src" }
$text = [System.IO.File]::ReadAllText($src)

# Strip Oxide runtime usings but retain Oxide.Game.Rust.Cui for the CUI types.
foreach ($using in @("using Oxide.Core;", "using Oxide.Core.Libraries;", "using Oxide.Core.Plugins;")) {
    $text = [regex]::Replace($text, "(?m)^" + [regex]::Escape($using) + "\r?\n", "")
}

$text = $text.Replace("namespace Oxide.Plugins", "namespace RustRewardsHarmony")

$newClass = @"
    /// <summary>
    /// RustRewards 3.2.5 ported for Harmony (no Oxide). Logic matches the Oxide plugin; hosting differs.
    /// </summary>
    public class RustRewards : RustRewardsPluginBase
"@
$pattern = '(?ms)^[ \t]*\[Info\("RustRewards"[^\]]*\]\s*\r?\n[ \t]*\[Description\([^\]]*\)\]\s*\r?\n[ \t]*public class RustRewards : RustPlugin'
$newText = [regex]::Replace($text, $pattern, { param($m) $newClass.TrimEnd() }, 1)
if ($newText -eq $text) {
    # Fallback: Info only
    $pattern2 = '(?m)^[ \t]*\[Info\("RustRewards"[^\]]*\]\r?\n(?:[ \t]*\[Description\([^\]]*\)\]\r?\n)?[ \t]*public class RustRewards : RustPlugin'
    $newText = [regex]::Replace($text, $pattern2, { param($m) $newClass.TrimEnd() }, 1)
}
if ($newText -eq $text) { throw "Could not find RustRewards class declaration to replace" }
$text = $newText

# Strip leftover Description if still present
$text = [regex]::Replace($text, '(?m)^[ \t]*\[Description\([^\]]*\)\]\r?\n', "")

$text = $text.Replace("[PluginReference] Plugin", "Plugin")
$text = [regex]::Replace($text, '(?m)^[ \t]*\[ConsoleCommand\([^\r\n]*\)\]\r?\n', "")
$text = [regex]::Replace($text, '(?m)^[ \t]*\[ChatCommand\([^\r\n]*\)\]\r?\n', "")

# Methods that patches / HarmonyMod need access to (private or default -> internal)
$methods = @(
    "Init", "Loaded", "OnServerInitialized", "Unload", "OnServerSave", "OnNewSave",
    "OnPlayerConnected", "OnPlayerDisconnected",
    "OnDispenserGather", "OnDispenserBonus", "OnGrowableGathered", "OnCollectiblePickup",
    "OnItemAction", "OnLootEntityEnd", "OnEntityDeath", "OnEntityTakeDamage", "OnPlayerDeath",
    "AnimalKill", "RustRewardsUI",
    "RRChangePref", "RRChangePos", "RRChangeType", "rrv", "CmdRrv", "RRUI",
    "RRChangeNum", "RRChangeAll", "RRChangeMult", "RRChangeAllMult",
    "RRZone", "RRChangeZoneMult", "RRChangeAllZoneMult", "CloseRR",
    "CmdRustRewardsWipeSummary", "CmdRustRewardsSetWipeBaseline", "CmdSendDiscordReport",
    "LoadConfigVariables", "ResolvePluginReferences"
)
foreach ($method in $methods) {
    # private/protected/default access -> internal (preserve return type)
    $text = [regex]::Replace($text,
        "(?m)^(\s*)(private |protected )?(void|object|bool|string|int|double) " + [regex]::Escape($method) + "\(",
        { param($m) $m.Groups[1].Value + "internal " + $m.Groups[3].Value + " " + $method + "(" })
}

# Data paths: HarmonyData/RustRewards/RustRewards.json
$text = $text.Replace('ReadObject<StoredData>("RustRewards")', 'ReadObject<StoredData>("RustRewards/RustRewards")')
$text = $text.Replace('WriteObject("RustRewards", storedData)', 'WriteObject("RustRewards/RustRewards", storedData)')

# Plugin.CallHook → Call (Compat Plugin has Call; CallHook alias also exists)
$text = $text.Replace("?.CallHook(", "?.Call(")

# Ctor after static rr field
$ctorMarker = "		public static RustRewards rr;"
$ctor = @"
		public static RustRewards rr;

		public RustRewards()
		{
			Version = new VersionNumber(3, 2, 5);
		}
"@
if (-not $text.Contains($ctorMarker)) { throw "ctor marker missing (public static RustRewards rr)" }
$text = $text.Replace($ctorMarker, $ctor)

# Harmony lifecycle after Unload
$harmonyLifecycle = @"

		// ---- Harmony lifecycle (replaces Oxide Init / OnServerInitialized / Unload) ----
		public override void HarmonyInit()
		{
			rr = this;
			LoadConfig();
			Init();
			Loaded();
			ResolvePluginReferences();
		}

		public override void HarmonyServerInitialized()
		{
			ResolvePluginReferences();
			OnServerInitialized();
		}

		public override void HarmonyUnload() => Unload();

		internal void ResolvePluginReferences()
		{
			Economics = plugins.Find("Economics");
			RaidableBases = plugins.Find("RaidableBases");
			Clans = plugins.Find("Clans");
			Friends = plugins.Find("Friends");
			GUIAnnouncements = plugins.Find("GUIAnnouncements");
			NoEscape = plugins.Find("NoEscape");
			ServerRewards = plugins.Find("ServerRewards");
			ZoneManager = plugins.Find("ZoneManager");
			PlaytimeTracker = plugins.Find("PlaytimeTracker");
		}

		protected override void LoadConfig()
		{
			// Host.Config already points at HarmonyConfig/RustRewards.json
			LoadConfigVariables();
		}
"@

$unloadEnd = '(?ms)(internal void Unload\(\)\s*\{.*?\n\t\t\})'
if (-not [regex]::IsMatch($text, $unloadEnd)) {
    # Try with tabs/spaces variance after internal rewrite
    $unloadEnd = '(?ms)(internal void Unload\(\)\s*\{(?:[^{}]|\{(?:[^{}]|\{[^{}]*\})*\})*\})'
}
if (-not [regex]::IsMatch($text, $unloadEnd)) { throw "Unload method marker missing" }
$text = [regex]::Replace($text, $unloadEnd, { param($m) $m.Groups[1].Value + $harmonyLifecycle }, 1)

# Ensure LoadConfigVariables is not duplicated as private after override path
# (already made internal above)

[System.IO.File]::WriteAllText($dst, $text)
Write-Host "Wrote $dst ($((($text -split "`n").Count)) lines)"
$checks = @(
    @{ Name = "Oxide.Core using"; Pattern = "using Oxide\.Core[^.]." },
    @{ Name = "Oxide.Game.Rust.Cui"; Pattern = "using Oxide\.Game\.Rust\.Cui" },
    @{ Name = "RustPlugin"; Pattern = "RustPlugin" },
    @{ Name = "[ConsoleCommand]"; Pattern = "\[ConsoleCommand" },
    @{ Name = "[ChatCommand]"; Pattern = "\[ChatCommand" },
    @{ Name = "[PluginReference]"; Pattern = "\[PluginReference\]" },
    @{ Name = "namespace Oxide.Plugins"; Pattern = "namespace Oxide\.Plugins\b" },
    @{ Name = "HarmonyInit"; Pattern = "HarmonyInit" },
    @{ Name = "ResolvePluginReferences"; Pattern = "ResolvePluginReferences" },
    @{ Name = "RustRewards/RustRewards"; Pattern = "RustRewards/RustRewards" },
    @{ Name = "RustRewardsPluginBase"; Pattern = "RustRewardsPluginBase" }
)
foreach ($check in $checks) { Write-Host ("{0}: {1}" -f $check.Name, ([regex]::Matches($text, $check.Pattern)).Count) }
