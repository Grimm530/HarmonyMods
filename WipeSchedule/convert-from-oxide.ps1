$ErrorActionPreference = "Stop"
$src = Join-Path $PSScriptRoot "..\..\Oxide.Plugins.Cant-Use\WipeSchedule.cs"
$dst = Join-Path $PSScriptRoot "WipeSchedule.cs"
if (-not (Test-Path $src)) { throw "Source not found: $src" }
$text = [System.IO.File]::ReadAllText($src)

# Strip Oxide runtime usings but retain Oxide.Game.Rust.Cui for the CUI types.
foreach ($using in @("using Oxide.Core;", "using Oxide.Core.Libraries;", "using Oxide.Core.Libraries.Covalence;", "using Oxide.Core.Plugins;")) {
    $text = [regex]::Replace($text, "(?m)^" + [regex]::Escape($using) + "\r?\n", "")
}

# Strip Carbon usings block (with or without space after #)
$text = [regex]::Replace($text, '(?ms)^#\s*if\s+CARBON\s*\r?\n(?:\s*using Carbon\.[^\r\n]*\r?\n)+#\s*endif\s*\r?\n', "")

# Longer nested namespace before parent.
$text = $text.Replace("using Oxide.Plugins.WipeScheduleEx;", "using WipeScheduleHarmony.WipeScheduleEx;")
$text = $text.Replace("namespace Oxide.Plugins.WipeScheduleEx", "namespace WipeScheduleHarmony.WipeScheduleEx")
$text = $text.Replace("namespace Oxide.Plugins", "namespace WipeScheduleHarmony")

$newClass = @"
    /// <summary>
    /// Wipe Schedule 2.0.21 ported for Harmony (no Oxide). Logic matches the Oxide plugin; hosting differs.
    /// </summary>
    public class WipeSchedule : WipeSchedulePluginBase
"@
$pattern = '(?m)^[ \t]*\[Info\("Wipe Schedule", "Mevent", "2\.0\.21"\)\][^\r\n]*\r?\n(?:[ \t]*//[^\r\n]*\r?\n)?[ \t]*public class WipeSchedule : RustPlugin'
$newText = [regex]::Replace($text, $pattern, { param($m) $newClass.TrimEnd() }, 1)
if ($newText -eq $text) { throw "Could not find WipeSchedule class declaration to replace" }
$text = $newText

$text = $text.Replace("[PluginReference] private Plugin", "private Plugin")
$text = [regex]::Replace($text, '(?m)^[ \t]*\[ConsoleCommand\([^\r\n]*\)\]\r?\n', "")
$text = [regex]::Replace($text, '(?m)^[ \t]*\[ChatCommand\([^\r\n]*\)\]\r?\n', "")

# Commands and hooks need patch/bridge access under Harmony.
foreach ($method in @(
        "Init", "OnServerInitialized", "Unload", "OnPlayerDisconnected", "OnServerPanelClosed",
        "CmdWipeSchedule", "CmdConsoleWipeSchedule", "CmdCheckTime", "API_OpenPlugin",
        "AddEventManagerSchedule", "AddImage", "GetImage", "HasImage", "GetPng", "LoadImages",
        "LoadDefaultMessages", "BroadcastILNotInstalled"
    )) {
    $text = [regex]::Replace($text, "(?m)^(\s*)private (void|object|bool|string|CuiElementContainer) " + $method + "\(", '$1internal $2 ' + $method + '(')
    $text = [regex]::Replace($text, "(?m)^(\s*)protected override (void) " + $method + "\(", '$1protected override $2 ' + $method + '(')
}

$ctorMarker = "        private static WipeSchedule Instance;"
$ctor = @"
        private static WipeSchedule Instance;

        public WipeSchedule()
        {
            Version = new VersionNumber(2, 0, 21);
        }
"@
if (-not $text.Contains($ctorMarker)) { throw "ctor marker missing (private static WipeSchedule Instance)" }
$text = $text.Replace($ctorMarker, $ctor)

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
if (-not [regex]::IsMatch($text, $unloadEnd)) { throw "Unload method marker missing" }
$text = [regex]::Replace($text, $unloadEnd, { param($m) $m.Groups[1].Value + $harmonyLifecycle }, 1)

[System.IO.File]::WriteAllText($dst, $text)
Write-Host "Wrote $dst ($((($text -split "`n").Count)) lines)"
$checks = @(
    @{ Name = "Oxide.Core using"; Pattern = "using Oxide\.Core" },
    @{ Name = "RustPlugin"; Pattern = "RustPlugin" },
    @{ Name = "[ConsoleCommand]"; Pattern = "\[ConsoleCommand" },
    @{ Name = "[ChatCommand]"; Pattern = "\[ChatCommand" },
    @{ Name = "namespace Oxide.Plugins"; Pattern = "namespace Oxide\.Plugins\b" },
    @{ Name = "HarmonyInit"; Pattern = "HarmonyInit" },
    @{ Name = "WipeSchedulePluginBase"; Pattern = "WipeSchedulePluginBase" },
    @{ Name = "Carbon using"; Pattern = "using Carbon\." }
)
foreach ($check in $checks) { Write-Host ("{0}: {1}" -f $check.Name, ([regex]::Matches($text, $check.Pattern)).Count) }
