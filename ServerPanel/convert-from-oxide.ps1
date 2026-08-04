# Converts the Oxide ServerPanel / ServerPanelPopUps sources into the Harmony port sources.
# Source stays untouched in .cursor/Oxide.Plugins.Cant-Use; output lands next to this script.
$ErrorActionPreference = "Stop"

function Convert-Panel {
    $src = Join-Path $PSScriptRoot "..\..\Oxide.Plugins.Cant-Use\ServerPanel.cs"
    $dst = Join-Path $PSScriptRoot "ServerPanel.cs"
    if (-not (Test-Path $src)) { throw "Source not found: $src" }
    $text = [System.IO.File]::ReadAllText($src)

    # --- Usings / namespaces -------------------------------------------------
    foreach ($using in @("using Oxide.Core;", "using Oxide.Core.Libraries;", "using Oxide.Core.Libraries.Covalence;", "using Oxide.Core.Plugins;")) {
        $text = [regex]::Replace($text, "(?m)^" + [regex]::Escape($using) + "\r?\n", "")
    }
    # Longer namespace first.
    $text = $text.Replace("using Oxide.Plugins.ServerPanelExtensionMethods;", "using ServerPanelHarmony.ServerPanelExtensionMethods;")
    $text = $text.Replace("namespace Oxide.Plugins.ServerPanelExtensionMethods", "namespace ServerPanelHarmony.ServerPanelExtensionMethods")
    $text = $text.Replace("namespace Oxide.Plugins", "namespace ServerPanelHarmony")

    # --- Class declaration ---------------------------------------------------
    $newClass = @"
    /// <summary>
    /// ServerPanel 2.0.20 ported for Harmony (no Oxide). Logic matches the Oxide plugin; hosting differs.
    /// </summary>
    public class ServerPanel : ServerPanelPluginBase
"@
    $pattern = '(?m)^[ \t]*\[Info\("ServerPanel", "Mevent", "2\.0\.20"\)\][^\r\n]*\r?\n[ \t]*public class ServerPanel : RustPlugin'
    $newText = [regex]::Replace($text, $pattern, { param($m) $newClass.TrimEnd() }, 1)
    if ($newText -eq $text) { throw "Could not find ServerPanel class declaration to replace" }
    $text = $newText

    # --- Plugin references become live lookups -------------------------------
    $refBlock = @"
        // Harmony: plugin references resolve live through the AppDomain bridge instead of Oxide.
        private Plugin ImageLibrary => ServerPanelHost.Instance?.ImageLibrary;
        private Plugin NoEscape => null;
        private Plugin Notify => null;
        private Plugin UINotify => null;
        private Plugin KillRecords => plugins?.Find("KillRecords");
        private Plugin Statistics => plugins?.Find("Statistics");
        private Plugin UltimateLeaderboard => plugins?.Find("UltimateLeaderboard");
        private Plugin ServerPanelPopUps => plugins?.Find("ServerPanelPopUps");
        private Plugin ServerPanelMigrations => null;
"@
    $refPattern = '(?ms)^[ \t]*\[PluginReference\][ \t]*private Plugin\s*\r?\n.*?ServerPanelMigrations = null;'
    if (-not [regex]::IsMatch($text, $refPattern)) { throw "PluginReference block not found (ServerPanel)" }
    $text = [regex]::Replace($text, $refPattern, { param($m) $refBlock.TrimEnd() }, 1)

    # --- Attributes ----------------------------------------------------------
    $text = [regex]::Replace($text, '(?m)^[ \t]*\[ConsoleCommand\([^\r\n]*\)\]\r?\n', "")
    $text = [regex]::Replace($text, '(?m)^[ \t]*\[ChatCommand\([^\r\n]*\)\]\r?\n', "")

    # --- Visibility for methods called from the mod host / patches -----------
    foreach ($method in @("OnPlayerConnected", "OnPlayerDisconnected", "InitializePlugin", "Unload", "Init", "OnServerInitialized")) {
        $text = [regex]::Replace($text, "(?m)^(\s*)private (void|object|bool|string) " + $method + "\(", '$1internal $2 ' + $method + '(')
    }

    # --- Auto-open gate (Oxide toggles this by subscribing / unsubscribing) --
    $connectOld = @"
        internal void OnPlayerConnected(BasePlayer player)
        {
            if (player == null || !player.userID.IsSteamId()) return;
"@
    $connectNew = @"
        internal void OnPlayerConnected(BasePlayer player)
        {
            if (player == null || !player.userID.IsSteamId()) return;

            // Oxide gates this by (un)subscribing the hook; the Harmony patch always fires.
            if (_config?.AutoOpen == null || !_config.AutoOpen.ShowMenuEveryTime) return;
"@
    $connectOld = $connectOld -replace "`r`n", "`n"
    $text = $text -replace "`r`n", "`n"
    if ($text.IndexOf($connectOld) -lt 0) { throw "OnPlayerConnected block not found" }
    $text = $text.Replace($connectOld, ($connectNew -replace "`r`n", "`n"))

    # --- Name / Title / ctor -------------------------------------------------
    $ctorMarker = "        private static ServerPanel Instance;"
    $ctor = @"
        private static ServerPanel Instance;

        public override string Name => "ServerPanel";
        public override string Title => "ServerPanel";

        public ServerPanel()
        {
            Version = new VersionNumber(2, 0, 20);
        }
"@
    if (-not $text.Contains($ctorMarker)) { throw "ctor marker missing (private static ServerPanel Instance)" }
    $text = $text.Replace($ctorMarker, $ctor)

    # --- Plugin pages returning a foreign CuiElementContainer ----------------
    $pageOld = @"
                            var obj = categoryPage.Plugin?.Call(categoryPage.PluginHook, player);
                            if (obj is CuiElementContainer pluginElements && pluginElements.Count > 0)
                                allElements.Add(pluginElements.ToJson().RemoveArrayBrackets());
                            else if (obj is string serializedElements && !string.IsNullOrWhiteSpace(serializedElements))
                                allElements.Add(serializedElements);
"@
    $pageNew = @"
                            var obj = categoryPage.Plugin?.Call(categoryPage.PluginHook, player);
                            if (obj is CuiElementContainer pluginElements && pluginElements.Count > 0)
                                allElements.Add(pluginElements.ToJson().RemoveArrayBrackets());
                            else if (obj is string serializedElements && !string.IsNullOrWhiteSpace(serializedElements))
                                allElements.Add(serializedElements);
                            else
                            {
                                // Other Harmony mods build the container from their own copy of the CUI types.
                                var foreignElements = ForeignCui.ToElementsJson(obj);
                                if (!string.IsNullOrWhiteSpace(foreignElements))
                                    allElements.Add(foreignElements);
                            }
"@
    $pageOld = $pageOld -replace "`r`n", "`n"
    $normalized = $text -replace "`r`n", "`n"
    if ($normalized.IndexOf($pageOld) -lt 0) { throw "Plugin page block not found" }
    $text = $normalized.Replace($pageOld, ($pageNew -replace "`r`n", "`n"))

    # --- Harmony lifecycle ---------------------------------------------------
    $harmonyLifecycle = @"

        // ---- Harmony lifecycle (replaces Oxide Init / OnServerInitialized / Unload) ----
        public override void HarmonyInit()
        {
            LoadConfig();

            // Data on this server is already at 2.0.20; the Oxide Migrations plugin gate is skipped.
            _migrationRequired = false;
            _migrationInProgress = false;

            ServerPanelExtensionMethods.ExtensionMethods.perm = ServerPanelHost.Instance?.Permission;

            Init();
            LoadDefaultMessages();
        }

        public override void HarmonyServerInitialized()
        {
            InitializePlugin();
        }

        public override void HarmonyUnload()
        {
            Unload();
        }
"@
    $unloadEnd = '(?ms)(internal void Unload\(\)\s*\{.*?\n        \})'
    if (-not [regex]::IsMatch($text, $unloadEnd)) { throw "Unload method marker missing (ServerPanel)" }
    $text = [regex]::Replace($text, $unloadEnd, { param($m) $m.Groups[1].Value + $harmonyLifecycle }, 1)

    # --- Extension-method permission bridge ----------------------------------
    $text = $text.Replace("internal static Permission perm;", "internal static HarmonyPermissionHelper perm;")
    $text = $text.Replace("perm ??= Interface.Oxide.GetLibrary<Permission>();", "perm ??= ServerPanelHost.Instance?.Permission;")

    [System.IO.File]::WriteAllText($dst, $text)
    Write-Host "Wrote $dst"
    return $text
}

function Convert-PopUps {
    $src = Join-Path $PSScriptRoot "..\..\Oxide.Plugins.Cant-Use\ServerPanelPopUps.cs"
    $dst = Join-Path $PSScriptRoot "ServerPanelPopUps.cs"
    if (-not (Test-Path $src)) { throw "Source not found: $src" }
    $text = [System.IO.File]::ReadAllText($src)

    foreach ($using in @("using Oxide.Core;", "using Oxide.Core.Libraries;", "using Oxide.Core.Libraries.Covalence;", "using Oxide.Core.Plugins;")) {
        $text = [regex]::Replace($text, "(?m)^" + [regex]::Escape($using) + "\r?\n", "")
    }
    $text = $text.Replace("using Oxide.Plugins.ServerPanelPopUpsExtensionMethods;", "using ServerPanelHarmony.ServerPanelPopUpsExtensionMethods;")
    $text = $text.Replace("namespace Oxide.Plugins.ServerPanelPopUpsExtensionMethods", "namespace ServerPanelHarmony.ServerPanelPopUpsExtensionMethods")
    $text = $text.Replace("namespace Oxide.Plugins", "namespace ServerPanelHarmony")

    # ServerPanel PopUps ships its own copy of the shared CUI helper classes; the ServerPanel file
    # already declares them at namespace level, so drop the duplicate here.
    $newClass = @"
    /// <summary>
    /// ServerPanel Pop Ups 2.0.20 ported for Harmony (no Oxide), hosted by the ServerPanel mod.
    /// </summary>
    public class ServerPanelPopUps : ServerPanelPluginBase
"@
    $pattern = '(?m)^[ \t]*\[Info\("ServerPanel Pop Ups", "Mevent", "2\.0\.20"\)\][^\r\n]*\r?\n[ \t]*public class ServerPanelPopUps : RustPlugin'
    $newText = [regex]::Replace($text, $pattern, { param($m) $newClass.TrimEnd() }, 1)
    if ($newText -eq $text) { throw "Could not find ServerPanelPopUps class declaration to replace" }
    $text = $newText

    $refBlock = @"
        // Harmony: plugin references resolve live through the AppDomain bridge instead of Oxide.
        private Plugin ServerPanel => plugins?.Find("ServerPanel");
        private Plugin Notify => null;
        private Plugin UINotify => null;
        private Plugin ImageLibrary => ServerPanelHost.Instance?.ImageLibrary;
"@
    $refPattern = '(?ms)^[ \t]*\[PluginReference\][ \t]*private Plugin\s*\r?\n.*?ImageLibrary = null;'
    if (-not [regex]::IsMatch($text, $refPattern)) { throw "PluginReference block not found (PopUps)" }
    $text = [regex]::Replace($text, $refPattern, { param($m) $refBlock.TrimEnd() }, 1)

    $text = [regex]::Replace($text, '(?m)^[ \t]*\[ConsoleCommand\([^\r\n]*\)\]\r?\n', "")
    $text = [regex]::Replace($text, '(?m)^[ \t]*\[ChatCommand\([^\r\n]*\)\]\r?\n', "")

    foreach ($method in @("OnPlayerDisconnected", "Unload", "Init", "OnServerInitialized")) {
        $text = [regex]::Replace($text, "(?m)^(\s*)private (void|object|bool|string) " + $method + "\(", '$1internal $2 ' + $method + '(')
    }

    $ctorMarker = "        private static ServerPanelPopUps Instance;"
    $ctor = @"
        private static ServerPanelPopUps Instance;

        public override string Name => "ServerPanelPopUps";
        public override string Title => "ServerPanel Pop Ups";

        public ServerPanelPopUps()
        {
            Version = new VersionNumber(2, 0, 20);
        }
"@
    if (-not $text.Contains($ctorMarker)) { throw "ctor marker missing (private static ServerPanelPopUps Instance)" }
    $text = $text.Replace($ctorMarker, $ctor)

    $harmonyLifecycle = @"

        // ---- Harmony lifecycle (replaces Oxide Init / OnServerInitialized / Unload) ----
        public override void HarmonyInit()
        {
            LoadConfig();

            ServerPanelPopUpsExtensionMethods.ExtensionMethods.perm = ServerPanelHost.Instance?.Permission;

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
    if (-not [regex]::IsMatch($text, $unloadEnd)) { throw "Unload method marker missing (PopUps)" }
    $text = [regex]::Replace($text, $unloadEnd, { param($m) $m.Groups[1].Value + $harmonyLifecycle }, 1)

    $text = $text.Replace("internal static Permission perm;", "internal static HarmonyPermissionHelper perm;")
    $text = $text.Replace("perm ??= Interface.Oxide.GetLibrary<Permission>();", "perm ??= ServerPanelHost.Instance?.Permission;")

    [System.IO.File]::WriteAllText($dst, $text)
    Write-Host "Wrote $dst"
    return $text
}

$panel = Convert-Panel
$popups = Convert-PopUps

$checks = @(
    @{ Name = "using Oxide.Core"; Pattern = "using Oxide\.Core" },
    @{ Name = "RustPlugin"; Pattern = "RustPlugin" },
    @{ Name = "[ConsoleCommand]"; Pattern = "\[ConsoleCommand" },
    @{ Name = "[ChatCommand]"; Pattern = "\[ChatCommand" },
    @{ Name = "[PluginReference]"; Pattern = "\[PluginReference" },
    @{ Name = "namespace Oxide.Plugins"; Pattern = "namespace Oxide\.Plugins\b" },
    @{ Name = "GetLibrary<Permission>"; Pattern = "GetLibrary<Permission>" },
    @{ Name = "static Permission perm"; Pattern = "static Permission perm" },
    @{ Name = "HarmonyInit"; Pattern = "HarmonyInit" }
)
foreach ($check in $checks) {
    $a = ([regex]::Matches($panel, $check.Pattern)).Count
    $b = ([regex]::Matches($popups, $check.Pattern)).Count
    Write-Host ("{0,-24} ServerPanel={1} PopUps={2}" -f $check.Name, $a, $b)
}
