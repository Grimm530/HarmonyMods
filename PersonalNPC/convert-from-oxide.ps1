# Converts the three Oxide sources (PersonalNPC, PersonalNPCHelper, PNPCAddonBuilder)
# into Oxide-free Harmony sources under this folder.
#
#   .cursor/Oxide.Plugins.Cant-Use/PersonalNPC.cs            -> PersonalNPC.cs
#   .cursor/Oxide.Plugins.Cant-Use/PersonalNPCHelper.cs      -> PersonalNPCHelper.cs
#   .cursor/Oxide.Plugins.Cant-Use/PersonalNPCAddonBuilder.cs-> PNPCAddonBuilder.cs
#
# Re-running this script overwrites the generated files, so never hand-edit them.

$ErrorActionPreference = "Stop"
$srcDir = Join-Path $PSScriptRoot "..\..\Oxide.Plugins.Cant-Use"

function Assert-Replaced([string]$before, [string]$after, [string]$what) {
    if ($before -eq $after) { throw "Conversion step did not match: $what" }
}

function Make-Internal([string]$text, [string[]]$names) {
    foreach ($n in $names) {
        $pattern = '(?m)^([ \t]*)private[ \t]+(void|object|bool|string)[ \t]+(' + $n + ')[ \t]*\('
        $text = [regex]::Replace($text, $pattern, '$1internal $2 $3(')
    }
    return $text
}

function Strip-CommandAttributes([string]$text) {
    $text = [regex]::Replace($text, '(?m)^[ \t]*\[ConsoleCommand\("[^"]*"\)\]\r?\n', "")
    $text = [regex]::Replace($text, '(?m)^[ \t]*\[ChatCommand\("[^"]*"\)\]\r?\n', "")
    return $text
}

function Strip-Usings([string]$text, [string[]]$usings) {
    foreach ($u in $usings) {
        $text = $text.Replace("using $u;`r`n", "")
        $text = $text.Replace("using $u;`n", "")
    }
    return $text
}

# Oxide's plugin compiler injects System.Linq for every plugin; a plain csproj does not.
function Add-Linq([string]$text) {
    if ($text -match '(?m)^using System\.Linq;') { return $text }
    return [regex]::Replace($text, '(?m)^using System;\r?\n', "using System;`r`nusing System.Linq;`r`n", 1)
}

# ---------------------------------------------------------------------------
# 1. PersonalNPC.cs (core)
# ---------------------------------------------------------------------------
Write-Host "Converting PersonalNPC.cs..." -ForegroundColor Cyan

$src = Join-Path $srcDir "PersonalNPC.cs"
if (-not (Test-Path $src)) { throw "Source not found: $src" }
$text = [System.IO.File]::ReadAllText($src)

$text = Strip-Usings $text @("Oxide.Core.Plugins", "Oxide.Core")
$text = Add-Linq $text
# Keep Oxide.Game.Rust.Cui - RustCui.cs provides that namespace locally.
$text = $text.Replace("using Oxide.Plugins.PersonalNPCex;", "using PersonalNPCHarmony.PersonalNPCex;")
$text = $text.Replace("namespace Oxide.Plugins.PersonalNPCex", "namespace PersonalNPCHarmony.PersonalNPCex")
$text = $text.Replace("namespace Oxide.Plugins", "namespace PersonalNPCHarmony")

# Class declaration
$before = $text
$newClass = @"
    /// <summary>
    /// PersonalNPC 2.0.7 ported for Harmony (no Oxide). Logic matches the Oxide plugin;
    /// only hosting, config/data I/O and cross-plugin calls differ.
    /// </summary>
    public class PersonalNPC : PersonalNPCPluginBase
"@
$pattern = '(?m)^[ \t]*\[Info\("PersonalNPC",[^\]]*\)\][ \t]*\r?\n[ \t]*public class PersonalNPC : RustPlugin[ \t]*'
$text = [regex]::Replace($text, $pattern, { param($m) $newClass.TrimEnd() }, 1)
Assert-Replaced $before $text "PersonalNPC class declaration"

# [PluginReference] fields must stay reachable for the host to wire the bridges.
$before = $text
$text = $text.Replace("[PluginReference] private Plugin ", "internal Plugin ")
Assert-Replaced $before $text "PersonalNPC [PluginReference]"

$text = Strip-CommandAttributes $text

$text = Make-Internal $text @(
    "Init", "Loaded", "OnServerInitialized", "Unload",
    "OnPlayerConnected", "OnPlayerDisconnected", "OnPlayerRespawned", "OnPlayerDeath",
    "OnEntitySpawned", "OnEntityKill", "OnEntityMounted", "OnEntityBuilt", "OnEntityTakeDamage",
    "CanBeTargeted", "CanBradleyApcTarget", "CanUseGesture", "CanUseLockedEntity",
    "OnLoseCondition", "OnItemAction", "CanMoveItem", "CanAcceptItem",
    "OnDispenserGather", "OnDispenserBonus",
    "CanLootEntity", "OnLootEntity", "OnCorpsePopulate",
    "chatCommand", "cnslCommand", "cnslCommandInfo", "cnslCommandItem", "ConsoleDepositCommand",
    "HasBot", "GetBotController", "IsPersonalNPC", "GetMsg"
)

# Bot inventories already live in HarmonyData/PersonalNPC/Inventories/BotInventories.json
$before = $text
$text = $text.Replace(
    'private const string BotInventoryDataPath = "PersonalNPC/BotInventories";',
    'private const string BotInventoryDataPath = "PersonalNPC/Inventories/BotInventories";')
Assert-Replaced $before $text "BotInventoryDataPath"

# ImageLibrary is always provided by the host, so never unload over a missing plugin.
$before = $text
$pattern = '(?ms)[ \t]*if\(ImageLibrary == null\)\r?\n[ \t]*\{.*?UnloadPlugin\(Title\)\);\r?\n\r?\n[ \t]*return;\r?\n[ \t]*\}\r?\n'
$text = [regex]::Replace($text, $pattern, "", 1)
Assert-Replaced $before $text "ImageLibrary unload guard"

# Constructor
$before = $text
$text = $text.Replace(
    "        public static PersonalNPC Instance;",
    @"
        public static PersonalNPC Instance;

        public PersonalNPC()
        {
            Version = new VersionNumber(2, 0, 7);
        }
"@)
Assert-Replaced $before $text "PersonalNPC ctor"

# Harmony lifecycle, appended after Unload()
$lifecycle = @"

        // ---- Harmony lifecycle (replaces Oxide LoadConfig / Init / Loaded / OnServerInitialized / Unload) ----
        public override void HarmonyInit()
        {
            LoadConfig();
            LoadDefaultMessages();
            Init();
            Loaded();
        }

        public override void HarmonyServerInitialized()
        {
            OnServerInitialized();
        }

        public override void HarmonyUnload()
        {
            Unload();
        }
"@
$before = $text
$unloadEnd = '(?ms)(internal void Unload\(\)\s*\{.*?\n        \})'
$text = [regex]::Replace($text, $unloadEnd, { param($mm) $mm.Groups[1].Value + $lifecycle }, 1)
Assert-Replaced $before $text "PersonalNPC Harmony lifecycle"

# The plugin ships its own OfType<T> in PersonalNPCex, which now collides with System.Linq.
$text = $text.Replace(
    "new(BaseNetworkable.serverEntities.OfType<CollectibleEntity>())",
    "new(System.Linq.Enumerable.OfType<CollectibleEntity>(BaseNetworkable.serverEntities))")

[System.IO.File]::WriteAllText((Join-Path $PSScriptRoot "PersonalNPC.cs"), $text)
Write-Host ("  wrote PersonalNPC.cs ({0} lines)" -f ([regex]::Split($text, "`r?`n")).Count)

# ---------------------------------------------------------------------------
# 2. PersonalNPCHelper.cs (wheel + Frankenstein unlock)
# ---------------------------------------------------------------------------
Write-Host "Converting PersonalNPCHelper.cs..." -ForegroundColor Cyan

$src = Join-Path $srcDir "PersonalNPCHelper.cs"
if (-not (Test-Path $src)) { throw "Source not found: $src" }
$text = [System.IO.File]::ReadAllText($src)

$text = Strip-Usings $text @("Oxide.Core.Plugins", "Oxide.Core")
$text = Add-Linq $text
$text = $text.Replace("namespace Oxide.Plugins", "namespace PersonalNPCHarmony")

$before = $text
$newClass = @"
    /// <summary>
    /// PersonalNPCHelper 1.3.0 ported for Harmony. Co-hosted with PersonalNPC in one DLL, so the
    /// Oxide load/unload dance around the parent plugin is gone.
    /// </summary>
    public class PersonalNPCHelper : PersonalNPCPluginBase
"@
$pattern = '(?m)^[ \t]*\[Info\("PersonalNPCHelper",[^\]]*\)\][ \t]*\r?\n[ \t]*\[Description\("[^"]*"\)\][ \t]*\r?\n[ \t]*class PersonalNPCHelper : RustPlugin[ \t]*'
$text = [regex]::Replace($text, $pattern, { param($m) $newClass.TrimEnd() }, 1)
Assert-Replaced $before $text "PersonalNPCHelper class declaration"

$before = $text
$text = $text.Replace("[PluginReference] private Plugin ", "internal Plugin ")
Assert-Replaced $before $text "PersonalNPCHelper [PluginReference]"

$text = Strip-CommandAttributes $text

$text = Make-Internal $text @(
    "Init", "Unload", "OnServerSave", "OnServerInitialized", "OnNewSave", "OnPlayerDisconnected",
    "InputPNPC", "OnPlayerCommand", "OnPNPCBuilderBaseSelected", "OnPNPCBuilderBaseUiClosed",
    "CmdBuildUi", "CmdWheel", "CmdBotWheel", "CmdBotWheelAlias", "CmdReset", "CmdGrant",
    "SaveData", "ToggleWheel", "OpenWheel", "CloseWheel", "OpenBuildUi", "CloseBuildUi",
    "CloseAllWheels", "CloseAllBuildUis"
)

# Data file lives beside the rest of the PersonalNPC data.
$before = $text
$text = $text.Replace('DataFileSystem.WriteObject("PersonalNPCHelper", _data)', 'DataFileSystem.WriteObject("PersonalNPC/PersonalNPCHelper", _data)')
$text = $text.Replace('DataFileSystem.ReadObject<StoredData>("PersonalNPCHelper")', 'DataFileSystem.ReadObject<StoredData>("PersonalNPC/PersonalNPCHelper")')
Assert-Replaced $before $text "PersonalNPCHelper data path"

# The parent plugin can never be missing - both live in this DLL.
$before = $text
$pattern = '(?ms)[ \t]*if \(PersonalNPC == null \|\| !PersonalNPC\.IsLoaded\)\r?\n[ \t]*\{.*?\r?\n[ \t]*return;\r?\n[ \t]*\}\r?\n'
$text = [regex]::Replace($text, $pattern, "", 1)
Assert-Replaced $before $text "PersonalNPCHelper parent-plugin guard"

# ASCII-only log output
$text = $text.Replace([char]0x2014, '-')

$before = $text
$text = $text.Replace(
    "        private StoredData _data;",
    @"
        public override string Name => "PersonalNPCHelper";

        public PersonalNPCHelper()
        {
            Version = new VersionNumber(1, 3, 0);
        }

        private StoredData _data;
"@)
Assert-Replaced $before $text "PersonalNPCHelper ctor"

$lifecycle = @"

        // ---- Harmony lifecycle ----
        public override void HarmonyInit()
        {
            Init();
        }

        public override void HarmonyServerInitialized()
        {
            OnServerInitialized();
        }

        public override void HarmonyUnload()
        {
            Unload();
        }
"@
$before = $text
$unloadEnd = '(?ms)(internal void Unload\(\)\s*\{.*?\n        \})'
$text = [regex]::Replace($text, $unloadEnd, { param($mm) $mm.Groups[1].Value + $lifecycle }, 1)
Assert-Replaced $before $text "PersonalNPCHelper Harmony lifecycle"

[System.IO.File]::WriteAllText((Join-Path $PSScriptRoot "PersonalNPCHelper.cs"), $text)
Write-Host ("  wrote PersonalNPCHelper.cs ({0} lines)" -f ([regex]::Split($text, "`r?`n")).Count)

# ---------------------------------------------------------------------------
# 3. PersonalNPCAddonBuilder.cs -> PNPCAddonBuilder.cs
# ---------------------------------------------------------------------------
Write-Host "Converting PersonalNPCAddonBuilder.cs..." -ForegroundColor Cyan

$src = Join-Path $srcDir "PersonalNPCAddonBuilder.cs"
if (-not (Test-Path $src)) { throw "Source not found: $src" }
$text = [System.IO.File]::ReadAllText($src)

$text = Strip-Usings $text @("Oxide.Core.Plugins", "Oxide.Core")
$text = Add-Linq $text
$text = $text.Replace("namespace Oxide.Plugins", "namespace PersonalNPCHarmony")
$text = $text.Replace("Core.Configuration.DynamicConfigFile", "DynamicConfigFile")

$before = $text
$newClass = @"
    /// <summary>
    /// PNPC Builder AI Addon 1.0.0 ported for Harmony. Its config lives in the shared
    /// PersonalNPC.json under "Available buildings (by PNPC bot spawn name)".
    /// </summary>
    public class PNPCAddonBuilder : PersonalNPCPluginBase
"@
$pattern = '(?m)^[ \t]*\[Info\("PNPC Builder AI Addon",[^\]]*\)\][ \t]*\r?\n[ \t]*public class PNPCAddonBuilder : RustPlugin[ \t]*'
$text = [regex]::Replace($text, $pattern, { param($m) $newClass.TrimEnd() }, 1)
Assert-Replaced $before $text "PNPCAddonBuilder class declaration"

$text = Strip-CommandAttributes $text

$text = Make-Internal $text @(
    "Unload", "OnEntityTakeDamage", "OnStructureUpgrade",
    "Build", "GetBuildList", "TryStartBuildFromFile", "SelectBuildFromUi",
    "InputPNPC", "IsBuilderActive", "ResetBuilder"
)

$before = $text
$text = $text.Replace(
    "        private Configuration _config;",
    @"
        public override string Name => "PNPCAddonBuilder";

        public PNPCAddonBuilder()
        {
            Version = new VersionNumber(1, 0, 0);
        }

        private Configuration _config;
"@)
Assert-Replaced $before $text "PNPCAddonBuilder ctor"

$lifecycle = @"

        // ---- Harmony lifecycle ----
        public override void HarmonyInit()
        {
            LoadConfig();
            LoadDefaultMessages();
        }

        public override void HarmonyServerInitialized()
        {
        }

        public override void HarmonyUnload()
        {
            Unload();
        }
"@
$before = $text
$unloadEnd = '(?ms)(internal void Unload\(\)\s*\{.*?\n        \})'
$text = [regex]::Replace($text, $unloadEnd, { param($mm) $mm.Groups[1].Value + $lifecycle }, 1)
Assert-Replaced $before $text "PNPCAddonBuilder Harmony lifecycle"

[System.IO.File]::WriteAllText((Join-Path $PSScriptRoot "PNPCAddonBuilder.cs"), $text)
Write-Host ("  wrote PNPCAddonBuilder.cs ({0} lines)" -f ([regex]::Split($text, "`r?`n")).Count)

# ---------------------------------------------------------------------------
# Sanity checks
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "Residual Oxide references (all counts should be 0 except Oxide.Game.Rust.Cui):" -ForegroundColor Cyan
foreach ($f in @("PersonalNPC.cs", "PersonalNPCHelper.cs", "PNPCAddonBuilder.cs")) {
    $t = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot $f))
    $checks = @(
        @{ Name = "using Oxide.Core"; Pattern = "using Oxide\.Core" },
        @{ Name = "namespace Oxide"; Pattern = "namespace Oxide\." },
        @{ Name = "RustPlugin"; Pattern = ": RustPlugin" },
        @{ Name = "[ChatCommand]"; Pattern = "\[ChatCommand" },
        @{ Name = "[ConsoleCommand]"; Pattern = "\[ConsoleCommand" },
        @{ Name = "[PluginReference]"; Pattern = "\[PluginReference\]" },
        @{ Name = "[Info]"; Pattern = "\[Info\(" },
        @{ Name = "HarmonyInit"; Pattern = "HarmonyInit" }
    )
    $line = $f + " -> "
    foreach ($c in $checks) {
        $line += "{0}={1}  " -f $c.Name, ([regex]::Matches($t, $c.Pattern)).Count
    }
    Write-Host $line
}
