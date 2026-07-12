$ErrorActionPreference = "Stop"
$src = Join-Path $PSScriptRoot "..\..\Oxide.Plugins.Cant-Use\Kits.cs"
$dst = Join-Path $PSScriptRoot "Kits.cs"
if (-not (Test-Path $src)) { throw "Source not found: $src" }
$text = [System.IO.File]::ReadAllText($src)

# --- Usings ---
$text = $text.Replace("using Oxide.Core;`r`n", "")
$text = $text.Replace("using Oxide.Core;`n", "")
$text = $text.Replace("using Oxide.Core.Libraries;`r`n", "")
$text = $text.Replace("using Oxide.Core.Libraries;`n", "")
$text = $text.Replace("using Oxide.Core.Libraries.Covalence;`r`n", "")
$text = $text.Replace("using Oxide.Core.Libraries.Covalence;`n", "")
$text = $text.Replace("using Oxide.Core.Plugins;`r`n", "")
$text = $text.Replace("using Oxide.Core.Plugins;`n", "")
# Keep Oxide.Game.Rust.Cui
$text = $text.Replace("using Oxide.Plugins.KitsExtensionMethods;", "using KitsHarmony.KitsExtensionMethods;")

# --- Strip #if CARBON blocks: keep #else branch when present (line-based, not greedy) ---
function Strip-CarbonBlocks([string]$src) {
    $lines = [regex]::Split($src, '\r?\n')
    $out = New-Object System.Collections.Generic.List[string]
    $i = 0
    while ($i -lt $lines.Count) {
        $line = $lines[$i].TrimEnd("`r", "`t", " ")
        if ($line -match '^\s*#if\s+CARBON\s*$') {
            $i++
            $elseBody = $null
            $inElse = $false
            while ($i -lt $lines.Count) {
                $cur = $lines[$i].TrimEnd("`r", "`t", " ")
                if ($cur -match '^\s*#else\s*$' -and -not $inElse) {
                    $inElse = $true
                    $elseBody = New-Object System.Collections.Generic.List[string]
                    $i++
                    continue
                }
                if ($cur -match '^\s*#endif\s*$') {
                    $i++
                    break
                }
                if ($inElse) { $elseBody.Add($lines[$i]) }
                $i++
            }
            if ($null -ne $elseBody) {
                foreach ($l in $elseBody) { $out.Add($l) }
            }
            continue
        }
        $out.Add($lines[$i])
        $i++
    }
    return [string]::Join("`r`n", $out.ToArray())
}
$text = Strip-CarbonBlocks $text
$text = $text.Replace("using Carbon.Base;`r`n", "")
$text = $text.Replace("using Carbon.Base;`n", "")
$text = $text.Replace("using Carbon.Modules;`r`n", "")
$text = $text.Replace("using Carbon.Modules;`n", "")

# --- Namespaces ---
$text = $text.Replace("namespace Oxide.Plugins", "namespace KitsHarmony")
$text = $text.Replace("namespace Oxide.Plugins.KitsExtensionMethods", "namespace KitsHarmony.KitsExtensionMethods")
# Fix double-replace if KitsExtensionMethods was already under KitsHarmony from first replace
$text = $text.Replace("namespace KitsHarmony.KitsExtensionMethods", "namespace KitsHarmony.KitsExtensionMethods")
# The first replace also hits KitsExtensionMethods namespace incorrectly:
# "namespace Oxide.Plugins.KitsExtensionMethods" -> after replacing "namespace Oxide.Plugins" becomes
# "namespace KitsHarmony.KitsExtensionMethods" which is correct IF we do the longer one first.
# Re-read: we already replaced Oxide.Plugins.KitsExtensionMethods first... wait we didn't.
# Order was: Oxide.Plugins first, which would turn
#   namespace Oxide.Plugins.KitsExtensionMethods
# into
#   namespace KitsHarmony.KitsExtensionMethods
# Good.

# --- Class declaration ---
$newClass = @"
    /// <summary>
    /// Kits 2.3.8 ported for Harmony (no Oxide). Logic matches Oxide plugin; only I/O and hosting differ.
    /// </summary>
    public class Kits : KitsPluginBase
"@
$pattern = '(?m)^[ \t]*\[Info\("Kits", "Mevent", "2\.3\.8"\)\]\r?\n[ \t]*public class Kits : RustPlugin'
$newText = [regex]::Replace($text, $pattern, { param($m) $newClass.TrimEnd() }, 1)
if ($newText -eq $text) { throw "Could not find class declaration to replace" }
$text = $newText

# --- PluginReference ---
$text = $text.Replace(
    "[PluginReference] private Plugin",
    "private Plugin")

# --- Strip command attributes ---
$text = [regex]::Replace($text, '(?m)^[ \t]*\[ConsoleCommand\("[^"]*"\)\]\r?\n', "")
$text = [regex]::Replace($text, '(?m)^[ \t]*\[ChatCommand\("[^"]*"\)\]\r?\n', "")

# --- Visibility for commands / API ---
$text = $text.Replace("private void CmdOpenKits(", "internal void CmdOpenKits(")
$text = $text.Replace("private void CmdKitsConsole(", "internal void CmdKitsConsole(")
$text = $text.Replace("private void editKitCommand(", "internal void editKitCommand(")
$text = $text.Replace("private void CmdKitsReset(", "internal void CmdKitsReset(")
$text = $text.Replace("private void CmdKitsGive(", "internal void CmdKitsGive(")
$text = $text.Replace("private void CmdKitsGiveKit(", "internal void CmdKitsGiveKit(")
$text = $text.Replace("private void CmdKitsSetTemplate(", "internal void CmdKitsSetTemplate(")
$text = $text.Replace("private void OldKitsConvert(", "internal void OldKitsConvert(")
# GiveKit / isKit API (RaidableBases NPC kits)
$text = $text.Replace("private object GiveKit(BasePlayer player, string name)", "public object GiveKit(BasePlayer player, string name)")
$text = $text.Replace("private bool GiveKit(BasePlayer player, string name, bool usingUI)", "public bool GiveKit(BasePlayer player, string name, bool usingUI)")
$text = $text.Replace("private bool isKit(string name)", "public bool isKit(string name)")
$text = $text.Replace("private bool IsKit(string name)", "public bool IsKit(string name)")
# Lifecycle hooks called from Harmony patches
$text = $text.Replace("private void OnPlayerRespawned(", "internal void OnPlayerRespawned(")
$text = $text.Replace("private void OnPlayerDisconnected(", "internal void OnPlayerDisconnected(")
$text = $text.Replace("private void OnPlayerDeath(", "internal void OnPlayerDeath(")
$text = $text.Replace("private void OnNewSave(", "internal void OnNewSave(")
$text = $text.Replace("private void Init()", "internal void Init()")
$text = $text.Replace("private void OnServerInitialized()", "internal void OnServerInitialized()")
$text = $text.Replace("private void Unload()", "internal void Unload()")

# --- ExtensionMethods Permission type ---
$text = $text.Replace("internal static Permission perm;", "internal static HarmonyPermissionHelper perm;")
$text = $text.Replace(
    "perm ??= Interface.Oxide.GetLibrary<Permission>();",
    "perm ??= KitsHost.Instance?.Permission;")

# --- IsSteamId: ensure string extension available (PlayerExtensions) ---
# Kits ExtensionMethods may also define IsSteamId for ulong - check and leave; PlayerExtensions covers string/ulong.

# --- Ctor ---
$ctorMarker = "        private static Kits _instance;"
$ctor = @"
        private static Kits _instance;

        public Kits()
        {
            Version = new VersionNumber(2, 3, 8);
        }
"@
if (-not $text.Contains($ctorMarker)) { throw "ctor marker missing (private static Kits _instance)" }
$text = $text.Replace($ctorMarker, $ctor)

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

# Wire ExtensionMethods.perm on Init
$text = $text.Replace(
    "internal void Init()`r`n        {`r`n            _instance = this;",
    "internal void Init()`r`n        {`r`n            _instance = this;`r`n            KitsExtensionMethods.ExtensionMethods.perm = KitsHost.Instance?.Permission;")
$text = $text.Replace(
    "internal void Init()`n        {`n            _instance = this;",
    "internal void Init()`n        {`n            _instance = this;`n            KitsExtensionMethods.ExtensionMethods.perm = KitsHost.Instance?.Permission;")

# LoadConfig: base.LoadConfig() is empty virtual — keep call
# GetValueOrDefault: DictionaryExtensions in KitsCompat

[System.IO.File]::WriteAllText($dst, $text)
Write-Host "Wrote $dst ($((($text -split "`n").Count)) lines)"

$checks = @(
    @{ Name = "Oxide.Core using"; Pattern = "using Oxide\.Core" },
    @{ Name = "RustPlugin"; Pattern = "RustPlugin" },
    @{ Name = "[ConsoleCommand]"; Pattern = "\[ConsoleCommand" },
    @{ Name = "[ChatCommand]"; Pattern = "\[ChatCommand" },
    @{ Name = "[PluginReference]"; Pattern = "\[PluginReference\]" },
    @{ Name = "#if CARBON"; Pattern = "#if CARBON" },
    @{ Name = "namespace Oxide.Plugins"; Pattern = "namespace Oxide\.Plugins\b" },
    @{ Name = "HarmonyInit"; Pattern = "HarmonyInit" },
    @{ Name = "KitsPluginBase"; Pattern = "KitsPluginBase" },
    @{ Name = "GetLibrary<Permission>"; Pattern = "GetLibrary<Permission>" },
    @{ Name = "static Permission perm"; Pattern = "static Permission perm" }
)
foreach ($c in $checks) {
    $count = ([regex]::Matches($text, $c.Pattern)).Count
    Write-Host ("{0}: {1}" -f $c.Name, $count)
}
