$ErrorActionPreference = "Stop"
$src = Join-Path $PSScriptRoot "..\..\Oxide.Plugins.Cant-Use\Shop.cs"
$dst = Join-Path $PSScriptRoot "Shop.cs"
if (-not (Test-Path $src)) { throw "Source not found: $src" }
$text = [System.IO.File]::ReadAllText($src)

# Strip Oxide runtime usings but retain Oxide.Game.Rust.Cui for the CUI types.
foreach ($using in @("using Oxide.Core;", "using Oxide.Core.Libraries;", "using Oxide.Core.Libraries.Covalence;", "using Oxide.Core.Plugins;")) {
    $text = [regex]::Replace($text, "(?m)^" + [regex]::Escape($using) + "\r?\n", "")
}

# The longer namespace must be rewritten before its parent namespace.
$text = $text.Replace("using Oxide.Plugins.ShopExtensionMethods;", "using ShopHarmony.ShopExtensionMethods;")
$text = $text.Replace("using Rust.Workshop;`r`n", "")
$text = $text.Replace("using Rust.Workshop;`n", "")
$text = $text.Replace("namespace Oxide.Plugins.ShopExtensionMethods", "namespace ShopHarmony.ShopExtensionMethods")
$text = $text.Replace("namespace Oxide.Plugins", "namespace ShopHarmony")

$newClass = @"
    /// <summary>
    /// Shop 2.4.201 ported for Harmony (no Oxide). Logic matches the Oxide plugin; hosting differs.
    /// </summary>
    public class Shop : ShopPluginBase
"@
$pattern = '(?m)^[ \t]*\[Info\("Shop", "Grimm530", "2\.4\.201"\)\][^\r\n]*\r?\n(?:[ \t]*//[^\r\n]*\r?\n)?[ \t]*public class Shop : RustPlugin'
$newText = [regex]::Replace($text, $pattern, { param($m) $newClass.TrimEnd() }, 1)
if ($newText -eq $text) { throw "Could not find Shop class declaration to replace" }
$text = $newText

$text = $text.Replace("[PluginReference] private Plugin", "private Plugin")
$text = [regex]::Replace($text, '(?m)^[ \t]*\[ConsoleCommand\([^\r\n]*\)\]\r?\n', "")
$text = [regex]::Replace($text, '(?m)^[ \t]*\[ChatCommand\([^\r\n]*\)\]\r?\n', "")

# Commands and hooks need patch/bridge access under Harmony.
$text = [regex]::Replace($text, '(?m)^(\s*)private (void|object|bool|string) (OpenShopUI|Cmd\w+)\(', '$1internal $2 $3(')
foreach ($method in @("Init", "OnServerInitialized", "Unload", "OnPlayerConnected", "OnPlayerDisconnected", "OnNewSave", "CanLootEntity", "OnUseNPC", "GetImage")) {
    $text = [regex]::Replace($text, "(?m)^(\s*)private (void|object|bool|string) " + $method + "\(", '$1internal $2 ' + $method + '(')
}

# Extension-method permission bridge.
$text = $text.Replace("internal static Permission perm;", "internal static HarmonyPermissionHelper perm;")
$text = $text.Replace("perm ??= Interface.Oxide.GetLibrary<Permission>();", "perm ??= ShopHost.Instance?.Permission;")

$ctorMarker = "        private static Shop _instance;"
$ctor = @"
        private static Shop _instance;

        public Shop()
        {
            Version = new VersionNumber(2, 4, 201);
        }
"@
if (-not $text.Contains($ctorMarker)) { throw "ctor marker missing (private static Shop _instance)" }
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

# The original source exposes ExtensionMethods.perm; bind it when Init establishes the host instance.
$text = [regex]::Replace($text, '(internal void Init\(\)\s*\{\s*_instance = this;)', { param($m) $m.Groups[1].Value + "`r`n            ShopExtensionMethods.ExtensionMethods.perm = ShopHost.Instance?.Permission;" }, 1)

[System.IO.File]::WriteAllText($dst, $text)
Write-Host "Wrote $dst ($((($text -split "`n").Count)) lines)"
$checks = @(
    @{ Name = "Oxide.Core using"; Pattern = "using Oxide\.Core" },
    @{ Name = "RustPlugin"; Pattern = "RustPlugin" },
    @{ Name = "[ConsoleCommand]"; Pattern = "\[ConsoleCommand" },
    @{ Name = "[ChatCommand]"; Pattern = "\[ChatCommand" },
    @{ Name = "namespace Oxide.Plugins"; Pattern = "namespace Oxide\.Plugins\b" },
    @{ Name = "HarmonyInit"; Pattern = "HarmonyInit" },
    @{ Name = "ShopPluginBase"; Pattern = "ShopPluginBase" },
    @{ Name = "GetLibrary<Permission>"; Pattern = "GetLibrary<Permission>" },
    @{ Name = "static Permission perm"; Pattern = "static Permission perm" }
)
foreach ($check in $checks) { Write-Host ("{0}: {1}" -f $check.Name, ([regex]::Matches($text, $check.Pattern)).Count) }
