$ErrorActionPreference = "Stop"
$src = Join-Path $PSScriptRoot "..\..\Oxide.Plugins.Cant-Use\Backpacks.cs"
$dst = Join-Path $PSScriptRoot "Backpacks.cs"
if (-not (Test-Path $src)) { throw "Source not found: $src" }
$text = [System.IO.File]::ReadAllText($src)

# --- Usings ---
foreach ($using in @(
    "using Oxide.Core;",
    "using Oxide.Core.Libraries;",
    "using Oxide.Core.Libraries.Covalence;",
    "using Oxide.Core.Plugins;",
    "using Oxide.Core.Configuration;"
)) {
    $text = [regex]::Replace($text, "(?m)^" + [regex]::Escape($using) + "\r?\n", "")
}
# Keep Oxide.Game.Rust.Cui

# --- Namespaces ---
$text = $text.Replace("namespace Oxide.Plugins", "namespace BackpacksHarmony")

# --- Class declaration ---
$newClass = @"
    /// <summary>
    /// Backpacks 3.17.41 ported for Harmony (no Oxide). Logic matches Oxide plugin; only I/O and hosting differ.
    /// </summary>
    public class Backpacks : BackpacksPluginBase
"@
$pattern = '(?m)^[ \t]*\[Info\("Backpacks", "WhiteThunder Edited by Grimm530", "3\.17\.41"\)\]\r?\n[ \t]*\[Description\("[^"]*"\)\]\r?\n[ \t]*public class Backpacks : CovalencePlugin'
$newText = [regex]::Replace($text, $pattern, { param($m) $newClass.TrimEnd() }, 1)
if ($newText -eq $text) { throw "Could not find class declaration to replace" }
$text = $newText

# --- PluginReference ---
$text = $text.Replace(
    "[PluginReference]`r`n        private readonly Plugin",
    "private readonly Plugin")
$text = $text.Replace(
    "[PluginReference]`n        private readonly Plugin",
    "private readonly Plugin")
$text = $text.Replace("[PluginReference]`r`n        private Plugin", "private Plugin")
$text = $text.Replace("[PluginReference]`n        private Plugin", "private Plugin")

# ItemRetriever is bound at runtime from ItemRetriever Harmony mod
$text = $text.Replace(
    "private readonly Plugin Arena, BackpackButton, EventManager, ItemRetriever;",
    "private readonly Plugin Arena, BackpackButton, EventManager;`r`n        /// <summary>Assigned at runtime when ItemRetriever Harmony mod is discovered.</summary>`r`n        internal Plugin ItemRetriever;")

# Expose supplier registration for Harmony bind
$text = $text.Replace("private void RegisterAsItemSupplier()", "internal void RegisterAsItemSupplier()")

# Inject MaybeRegisterItemRetriever before RegisterAsItemSupplier if missing
if ($text -notmatch "MaybeRegisterItemRetriever") {
    $maybe = @"
        /// <summary>Bind ItemRetriever PluginReference and register supplier callbacks (safe to call repeatedly).</summary>
        internal void MaybeRegisterItemRetriever()
        {
            var bridge = ItemRetrieverBinder.TryResolveBridge();
            if (bridge == null)
                return;

            ItemRetriever = bridge;
            RegisterAsItemSupplier();
        }

"@
    $text = $text.Replace("internal void RegisterAsItemSupplier()", $maybe + "        internal void RegisterAsItemSupplier()")
}

# OnServerInitialized / OnPluginLoaded should use MaybeRegister
$text = $text.Replace(
    "CheckBackpackButtonPlugin();`r`n            RegisterAsItemSupplier();",
    "CheckBackpackButtonPlugin();`r`n            MaybeRegisterItemRetriever();")
$text = $text.Replace(
    "CheckBackpackButtonPlugin();`n            RegisterAsItemSupplier();",
    "CheckBackpackButtonPlugin();`n            MaybeRegisterItemRetriever();")
$text = $text.Replace(
    "case nameof(ItemRetriever):`r`n                    RegisterAsItemSupplier();",
    "case nameof(ItemRetriever):`r`n                    MaybeRegisterItemRetriever();")
$text = $text.Replace(
    "case nameof(ItemRetriever):`n                    RegisterAsItemSupplier();",
    "case nameof(ItemRetriever):`n                    MaybeRegisterItemRetriever();")

# --- Strip command / hook attributes ---
$text = [regex]::Replace($text, '(?m)^[ \t]*\[Command\([^\r\n]*\)\]\r?\n', "")
$text = [regex]::Replace($text, '(?m)^[ \t]*\[ConsoleCommand\([^\r\n]*\)\]\r?\n', "")
$text = [regex]::Replace($text, '(?m)^[ \t]*\[ChatCommand\([^\r\n]*\)\]\r?\n', "")
$text = [regex]::Replace($text, '(?m)^[ \t]*\[HookMethod\([^\r\n]*\)\]\r?\n', "")


# --- Visibility: commands ---
$cmdMethods = @(
    "BackpackOpenCommand",
    "BackpackNextCommand",
    "BackpackPreviousCommand",
    "BackpackFetchCommand",
    "EraseBackpackCommand",
    "ViewBackpackCommand",
    "ViewBackpack",
    "AddBackpackCapacityCommand",
    "SetBackpackCapacityCommand",
    "ToggleBackpackGUICommand",
    "ResetGuiCommand",
    "SetGatherCommand",
    "ToggleGatherUICommand",
    "ToggleRetrieveUICommand",
    "DebugSizeCommand",
    "DebugGatherCommand"
)
foreach ($m in $cmdMethods) {
    $text = [regex]::Replace($text, "(?m)^(\s*)private (void|object|bool|string|int) $m\(", "`$1internal `$2 $m(")
}

# --- Visibility: lifecycle / hooks ---
$hooks = @(
    "Init",
    "OnServerInitialized",
    "Unload",
    "OnNewSave",
    "OnServerSave",
    "OnPlayerConnected",
    "OnPlayerDisconnected",
    "OnPlayerRespawned",
    "OnPlayerSleep",
    "OnPlayerSleepEnded",
    "OnEntityDeath",
    "OnEntityKill",
    "CanMoveItem",
    "OnItemAction",
    "OnNpcConversationStart",
    "OnNpcConversationEnded",
    "OnNetworkSubscriptionsUpdate",
    "OnPluginLoaded",
    "OnPluginUnloaded",
    "OnGroupPermissionGranted",
    "OnGroupPermissionRevoked",
    "OnUserPermissionGranted",
    "OnUserPermissionRevoked"
)
foreach ($h in $hooks) {
    $text = [regex]::Replace($text, "(?m)^(\s*)private (void|object|bool|string) $h\(", "`$1internal `$2 $h(")
}

# API methods already public — keep
# Make Subscribe/Unsubscribe accessible from DynamicHookSubscriber (same class — fine as protected on base)

# --- BasePlayer.IPlayer -> ToIPlayer() ---
$text = $text.Replace(".IPlayer", ".ToIPlayer()")

# --- Offline steam lookup: use FindPlayerById instead of All.FirstOrDefault ---
$text = $text.Replace(
    "var player = covalence.Players.All.FirstOrDefault(p => p.Id == nameOrID);",
    "var player = covalence.Players.FindPlayerById(nameOrID);")

# --- ASCII-only log messages: replace common unicode dashes ---
$text = $text.Replace([string][char]0x2014, "-")  # em dash
$text = $text.Replace([string][char]0x2013, "-")  # en dash
$text = $text.Replace([string][char]0x2192, "->") # arrow
$text = $text.Replace([string][char]0x2713, "[OK]")
$text = $text.Replace([string][char]0x2714, "[OK]")

# --- Ctor: Version already set in existing ctor; ensure VersionNumber ---
# Existing: public Backpacks() { ... } — inject Version after fields init
$ctorMarker = "        public Backpacks()`r`n        {"
$ctorNew = "        public Backpacks()`r`n        {`r`n            Version = new VersionNumber(3, 17, 41);"
if ($text.Contains($ctorMarker)) {
    $text = $text.Replace($ctorMarker, $ctorNew)
} else {
    $ctorMarker = "        public Backpacks()`n        {"
    $ctorNew = "        public Backpacks()`n        {`n            Version = new VersionNumber(3, 17, 41);"
    if (-not $text.Contains($ctorMarker)) { throw "ctor marker missing" }
    $text = $text.Replace($ctorMarker, $ctorNew)
}

# --- Harmony lifecycle after Unload ---
$harmonyLifecycle = @"

        // ---- Harmony lifecycle (replaces Oxide Init / OnServerInitialized / Unload) ----
        public override void HarmonyInit()
        {
            LoadConfig();
            Init();
            LoadDefaultMessages();
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
$unloadEnd = '(?ms)(internal void Unload\(\)\s*\{.*?\n        \})'
$m = [regex]::Match($text, $unloadEnd)
if (-not $m.Success) { throw "Unload method marker missing" }
$text = [regex]::Replace($text, $unloadEnd, { param($mm) $mm.Groups[1].Value + $harmonyLifecycle }, 1)

$text = $text.Replace("private Permission _permission;", "private HarmonyPermissionHelper _permission;")
$text = $text.Replace("private readonly Permission _permission;", "private readonly HarmonyPermissionHelper _permission;")

# ItemContainer.onDirty is ambiguous under Publicizer
$text = $text.Replace(
    "ItemContainer.onDirty += _onDirty;",
    "ItemContainerHooks.AddOnDirty(ItemContainer, _onDirty);")
$text = $text.Replace(
    "container.onDirty -= playerLoot.MarkDirty;",
    "ItemContainerHooks.RemoveOnDirty(container, playerLoot.MarkDirty);")

[System.IO.File]::WriteAllText($dst, $text)
Write-Host "Wrote $dst ($((($text -split "`n").Count)) lines)"

$checks = @(
    @{ Name = "Oxide.Core using"; Pattern = "using Oxide\.Core[^.]." },
    @{ Name = "CovalencePlugin"; Pattern = "CovalencePlugin" },
    @{ Name = "[Command]"; Pattern = "\[Command\(" },
    @{ Name = "[PluginReference]"; Pattern = "\[PluginReference\]" },
    @{ Name = "[HookMethod]"; Pattern = "\[HookMethod" },
    @{ Name = "namespace Oxide.Plugins"; Pattern = "namespace Oxide\.Plugins\b" },
    @{ Name = "HarmonyInit"; Pattern = "HarmonyInit" },
    @{ Name = "BackpacksPluginBase"; Pattern = "BackpacksPluginBase" },
    @{ Name = ".IPlayer (property)"; Pattern = "\.IPlayer\b" },
    @{ Name = "ToIPlayer"; Pattern = "ToIPlayer" }
)
foreach ($c in $checks) {
    $count = ([regex]::Matches($text, $c.Pattern)).Count
    Write-Host ("{0}: {1}" -f $c.Name, $count)
}
