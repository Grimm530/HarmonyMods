$ErrorActionPreference = "Stop"
$src = Join-Path $PSScriptRoot "..\..\Oxide.Plugins.Cant-Use\ItemRetriever.cs"
$dst = Join-Path $PSScriptRoot "ItemRetriever.cs"
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

# --- Namespaces ---
$text = $text.Replace("namespace Oxide.Plugins", "namespace ItemRetrieverHarmony")

# --- Class declaration ---
$newClass = @"
    /// <summary>
    /// ItemRetriever 0.7.7 ported for Harmony (no Oxide). Library for external container item supply.
    /// </summary>
    public class ItemRetriever : ItemRetrieverPluginBase
"@
$pattern = '(?m)^[ \t]*\[Info\("Item Retriever", "WhiteThunder", "0\.7\.7"\)\]\r?\n[ \t]*\[Description\("[^"]*"\)\]\r?\n[ \t]*internal class ItemRetriever : CovalencePlugin'
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

# --- Strip hook attributes ---
$text = [regex]::Replace($text, '(?m)^[ \t]*\[HookMethod\([^\r\n]*\)\]\r?\n', "")

# --- Visibility: lifecycle / hooks (Harmony patches call these) ---
$hooks = @(
    "OnServerInitialized",
    "Unload",
    "OnPluginLoaded",
    "OnPluginUnloaded",
    "OnEntitySaved",
    "OnInventoryNetworkUpdate",
    "OnInventoryItemsCount",
    "OnInventoryItemsTake",
    "OnInventoryItemsFind",
    "OnInventoryItemFind",
    "OnInventoryAmmoFind",
    "OnInventoryAmmoItemFind",
    "OnIngredientsCollect",
    "CanCraft"
)
foreach ($h in $hooks) {
    $text = [regex]::Replace($text, "(?m)^(\s*)private (void|object|bool|string|Item) $h\(", "`$1internal `$2 $h(")
}

# API methods already public — keep

# --- ASCII-only log messages ---
$text = $text.Replace([string][char]0x2014, "-")  # em dash
$text = $text.Replace([string][char]0x2013, "-")  # en dash
$text = $text.Replace([string][char]0x2192, "->") # arrow

# --- Ctor: set Version ---
$ctorMarker = "        public ItemRetriever()`r`n        {"
$ctorNew = "        public ItemRetriever()`r`n        {`r`n            Version = new VersionNumber(0, 7, 7);"
if ($text.Contains($ctorMarker)) {
    $text = $text.Replace($ctorMarker, $ctorNew)
} else {
    $ctorMarker = "        public ItemRetriever()`n        {"
    $ctorNew = "        public ItemRetriever()`n        {`n            Version = new VersionNumber(0, 7, 7);"
    if (-not $text.Contains($ctorMarker)) { throw "ctor marker missing" }
    $text = $text.Replace($ctorMarker, $ctorNew)
}

# --- ItemContainer.onDirty Publicizer ambiguity ---
$text = $text.Replace(
    "Container.onDirty += _handleDirty;",
    "ItemContainerHooks.AddOnDirty(Container, _handleDirty);")
$text = $text.Replace(
    "Container.onDirty -= _handleDirty;",
    "ItemContainerHooks.RemoveOnDirty(Container, _handleDirty);")

# --- Harmony lifecycle after Unload ---
$harmonyLifecycle = @"

        // ---- Harmony lifecycle (replaces Oxide OnServerInitialized / Unload) ----
        public override void HarmonyInit()
        {
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

[System.IO.File]::WriteAllText($dst, $text)
Write-Host "Wrote $dst ($((($text -split "`n").Count)) lines)"

$checks = @(
    @{ Name = "Oxide.Core using"; Pattern = "using Oxide\.Core[^.]." },
    @{ Name = "CovalencePlugin"; Pattern = "CovalencePlugin" },
    @{ Name = "[PluginReference]"; Pattern = "\[PluginReference\]" },
    @{ Name = "[HookMethod]"; Pattern = "\[HookMethod" },
    @{ Name = "namespace Oxide.Plugins"; Pattern = "namespace Oxide\.Plugins\b" },
    @{ Name = "HarmonyInit"; Pattern = "HarmonyInit" },
    @{ Name = "ItemRetrieverPluginBase"; Pattern = "ItemRetrieverPluginBase" },
    @{ Name = "internal OnInventoryItemsCount"; Pattern = "internal object OnInventoryItemsCount" },
    @{ Name = "internal CanCraft"; Pattern = "internal object CanCraft" }
)
foreach ($c in $checks) {
    $count = ([regex]::Matches($text, $c.Pattern)).Count
    Write-Host ("{0}: {1}" -f $c.Name, $count)
}
