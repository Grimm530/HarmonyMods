$ErrorActionPreference = "Stop"
$src = Join-Path $PSScriptRoot "..\..\Oxide.Plugins.Cant-Use\RestoreItems.cs"
$dst = Join-Path $PSScriptRoot "RestoreItems.cs"
if (-not (Test-Path $src)) { throw "Source not found: $src" }
$text = [System.IO.File]::ReadAllText($src)

foreach ($using in @(
    "using Oxide.Core;",
    "using Oxide.Core.Plugins;"
)) {
    $text = [regex]::Replace($text, "(?m)^" + [regex]::Escape($using) + "\r?\n", "")
}

$newClass = @"
    /// <summary>
    /// RestoreItems 2.1.6 ported for Harmony (no Oxide). Logic matches Oxide plugin; only I/O and hosting differ.
    /// </summary>
    public partial class RestoreItems : RustPlugin
"@
$pattern = '(?m)^[ \t]*\[Info\("RestoreItems", "Grimm530", "2\.1\.6"\)\]\r?\n[ \t]*\[Description\("[^"]*"\)\]\r?\n\r?\n[ \t]*public class RestoreItems : RustPlugin'
$newText = [regex]::Replace($text, $pattern, { param($m) $newClass.TrimEnd() }, 1)
if ($newText -eq $text) { throw "Could not find class declaration to replace" }
$text = $newText

$text = $text.Replace(
    "[PluginReference] Plugin Economics;",
    "/// <summary>Bound at runtime from Economics Harmony mod.</summary>`r`n        internal Plugin Economics;")
$text = $text.Replace(
    "[PluginReference] Plugin RaidableBases;",
    "/// <summary>Bound at runtime from RaidableBases Harmony mod.</summary>`r`n        internal Plugin RaidableBases;")

$text = [regex]::Replace($text, '(?m)^[ \t]*\[ChatCommand\([^\r\n]*\)\]\r?\n', "")

if ($text -notmatch "using RestoreItemsHarmony;")
{
    $text = $text.Replace(
        "using UnityEngine;",
        "using UnityEngine;`r`nusing Oxide.Core;`r`nusing Oxide.Core.Plugins;`r`nusing RestoreItemsHarmony;`r`nusing System.IO;")
}

$hooks = @(
    "OnServerInitialized",
    "OnPlayerDeath",
    "OnDied",
    "OnItemAddedToContainer",
    "OnItemStacked",
    "OnEntitySpawned",
    "OnRaidableBaseBackpackEject",
    "OnEntityKill",
    "ChatCmdGetItems",
    "ChatCmdDebug",
    "CmdRestoreTest"
)
foreach ($h in $hooks) {
    $text = [regex]::Replace($text, "(?m)^(\s*)private (void|object|bool) $h\(", "`$1internal `$2 $h(")
}

$harmonyLifecycle = @"

        // ---- Harmony lifecycle ----
        public void HarmonyInit()
        {
            LoadConfig();
            LoadDefaultMessages();
        }

        public void HarmonyServerInitialized()
        {
            OnServerInitialized();
        }

        public void HarmonyUnload()
        {
            timer?.DestroyAll();
        }
"@
$lifecyclePattern = '(?ms)(\s*#endregion\s*\r?\n\s*\}\s*\r?\n\})\s*$'
if ($text -match $lifecyclePattern) {
    $text = [regex]::Replace($text, $lifecyclePattern, $harmonyLifecycle + "`r`n        #endregion`r`n    }`r`n}", 1)
} else {
    throw "Could not find class end marker for Harmony lifecycle injection"
}

$text = $text.Replace([string][char]0x2014, "-")
$text = $text.Replace([string][char]0x2013, "-")

[System.IO.File]::WriteAllText($dst, $text)
Write-Host "Wrote $dst ($((($text -split "`n").Count)) lines)"
