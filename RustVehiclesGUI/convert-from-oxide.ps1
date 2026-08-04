$ErrorActionPreference = "Stop"
$src = Join-Path $PSScriptRoot "..\..\Oxide.Plugins.Cant-Use\RustVehiclesGUI.cs"
$dst = Join-Path $PSScriptRoot "RustVehiclesGUI.cs"
if (-not (Test-Path $src)) { throw "Source not found: $src" }
$text = [System.IO.File]::ReadAllText($src)

# Strip Oxide runtime usings. Oxide.Game.Rust.Cui stays: RustCui.cs re-implements that namespace.
foreach ($using in @(
        "using Oxide.Core;",
        "using Oxide.Core.Libraries.Covalence;",
        "using Oxide.Core.Plugins;"
    )) {
    $text = [regex]::Replace($text, "(?m)^" + [regex]::Escape($using) + "\r?\n", "")
}

$text = $text.Replace("namespace Oxide.Plugins", "namespace RustVehiclesGUIHarmony")

$newClass = @"
    /// <summary>
    /// RustVehiclesGUI 1.0.5 ported for Harmony (no Oxide). Logic matches the Oxide plugin; hosting differs.
    /// Vehicle purchases and spawns are delegated to the RustVehicles Harmony mod.
    /// </summary>
    [Info("Rust Vehicles GUI", "Grimm530", "1.0.5")]
    [Description("GUI interface for Rust Vehicles plugin with ServerPanel integration")]
    public class RustVehiclesGUI : RustVehiclesGUIPluginBase
"@
$pattern = '(?ms)^[ \t]*\[Info\("Rust Vehicles GUI", "Grimm530", "1\.0\.5"\)\]\r?\n[ \t]*\[Description\([^\r\n]*\)\]\r?\n[ \t]*public class RustVehiclesGUI : RustPlugin'
$newText = [regex]::Replace($text, $pattern, { param($m) $newClass.TrimEnd() }, 1)
if ($newText -eq $text) { throw "Could not find RustVehiclesGUI class declaration to replace" }
$text = $newText

# PluginReference fields -> live AppDomain bridges. Properties (not fields) so a mod that loads
# after this one still resolves, and PluginBridges caches per wrapper so `CorePlugin == RustVehicles` holds.
$refBlock = @'
        [PluginReference] private Plugin 
            RustVehicles,
            VehicleLicence,
            Economics,
            ServerRewards,
            ServerPanel;
'@
$refReplacement = @'
        private Plugin RustVehicles => PluginBridges.RustVehicles;
        private Plugin VehicleLicence => PluginBridges.VehicleLicence;
        private Plugin Economics => PluginBridges.Economics;
        private Plugin ServerRewards => PluginBridges.ServerRewards;
        private Plugin ServerPanel => PluginBridges.ServerPanel;
'@
$refBlockNormalized = $refBlock -replace "`r`n", "`n"
$textNormalized = $text -replace "`r`n", "`n"
if (-not $textNormalized.Contains($refBlockNormalized)) { throw "PluginReference block not found" }
$text = $textNormalized.Replace($refBlockNormalized, ($refReplacement -replace "`r`n", "`n"))

# KEEP [ConsoleCommand] — RustVehiclesGUIHarmonyMod discovers them by reflection.

# Hook methods need to be reachable from the Harmony lifecycle wrappers.
foreach ($method in @("Init", "OnServerInitialized", "OnServerShutdown")) {
    $text = [regex]::Replace(
        $text,
        "(?m)^(\s*)private (void) " + [regex]::Escape($method) + "\(",
        '${1}internal $2 ' + $method + '(')
}

# LoadDefaultConfig hides the base virtual otherwise.
$text = $text.Replace("private void LoadDefaultConfig()", "protected override void LoadDefaultConfig()")

# RustVehicles Harmony data lives in HarmonyData/RustVehicles/RustVehicles.json.
$text = $text.Replace('var rv = $"{dataDir}/RustVehicles.json";', 'var rv = $"{dataDir}/RustVehicles/RustVehicles.json";')
$text = $text.Replace('var vl = $"{dataDir}/VehicleLicence.json";', 'var vl = $"{dataDir}/VehicleLicence/VehicleLicence.json";')

# Oxide read of "config/RustVehicles" resolved under oxide/data and never matched the
# Dictionary<string, object> cast. Read the real core config instead.
$maxVehiclesOld = @'
                var config = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<string, object>>("config/RustVehicles");
                if (config?.ContainsKey("Global Settings") == true && config["Global Settings"] is Dictionary<string, object> globalSettings)
                {
                    if (globalSettings.ContainsKey("Limit Vehicles") && int.TryParse(globalSettings["Limit Vehicles"].ToString(), out int limit))
                    {
                        return limit;
                    }
                }
'@
$maxVehiclesNew = @'
                var config = GetVehicleConfig();
                if (config?.ContainsKey("Global Settings") == true &&
                    config["Global Settings"] is Newtonsoft.Json.Linq.JObject globalSettings)
                {
                    var limitToken = globalSettings["Limit Vehicles"];
                    if (limitToken != null && int.TryParse(limitToken.ToString(), out int limit))
                    {
                        return limit;
                    }
                }
'@
$maxVehiclesOld = $maxVehiclesOld -replace "`r`n", "`n"
$maxVehiclesNew = $maxVehiclesNew -replace "`r`n", "`n"
if (-not $text.Contains($maxVehiclesOld)) { throw "GetMaxVehicles config read not found" }
$text = $text.Replace($maxVehiclesOld, $maxVehiclesNew)

# BasePlayer.userID is an EncryptedValue<ulong>; boxing it into a reflection Call() breaks the
# ulong/string parameter match on the receiving mod. Unwrap it at every bridge call site.
$text = [regex]::Replace($text, '\.Call\((\"[A-Za-z_]+\"), player\.userID(?=[,)])', '.Call($1, player.userID.Get()')

# The client never forwards UI_ServerPanel, so redraw the panel server-side instead.
$text = $text.Replace(
    'player.SendConsoleCommand("UI_ServerPanel", "menu", "page", "0");',
    'RustVehiclesGUIHost.RunPlayerConsoleCommand(player, "UI_ServerPanel", "menu", "page", "0");')

# Harmony lifecycle + ServerPanel consumer hooks, inserted at the end of the Hooks region.
$lifecycle = @'

        // ---- Harmony lifecycle (replaces Oxide Init / OnServerInitialized / Unload) ----
        public override void HarmonyInit()
        {
            LoadConfig();
            Init();
        }

        public override void HarmonyServerInitialized()
        {
            OnServerInitialized();
        }

        public override void HarmonyUnload()
        {
            try { OnServerShutdown(); }
            catch (Exception ex) { PrintWarning("HarmonyUnload: " + ex.Message); }

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null) continue;
                try { DestroyAllUI(player); }
                catch { }
                ClearImageQueue(player.userID);
            }

            _playerServerPanelView.Clear();
            _playerSelectedCategory.Clear();
            _playerSelectedManageCategory.Clear();
            _playerShopPage.Clear();
            _playerManagePage.Clear();
            _cachedVehicleLists.Clear();
            _cachedVehicleListPlayer.Clear();
            _playerOwnedVehiclesCache.Clear();
            Instance = null;
        }

        // ---- ServerPanel consumer hooks (ServerPanel broadcasts these to registered mods) ----
        internal void OnServerPanelClosed(BasePlayer player)
        {
            if (player == null) return;
            var userId = player.userID;
            ClearImageQueue(userId);
            ClearVehicleListCache(userId);
            _playerServerPanelView.Remove(userId);
            _playerShopPage.Remove(userId);
            _playerManagePage.Remove(userId);
            _playerOwnedVehiclesCache.Remove(userId);
            DebugServerPanel($"[SERVERPANEL] OnServerPanelClosed: cleared state for {userId}");
        }

        /// <summary>
        /// ServerPanel passes the category as an int id (or whatever the caller had), so this takes object.
        /// It must stay void: ServerPanel treats any non-null hook result as "cancel the page switch".
        /// </summary>
        internal void OnServerPanelCategoryPage(BasePlayer player, object category, int page)
        {
            if (player == null) return;
            ClearImageQueue(player.userID);
            DebugServerPanel($"[SERVERPANEL] OnServerPanelCategoryPage: category='{category}' page={page} for {player.userID}");
        }

'@
$lifecycle = $lifecycle -replace "`r`n", "`n"
$hooksEnd = "        #endregion`n`n        #region Chat Commands"
if (-not $text.Contains($hooksEnd)) { throw "Hooks region end marker missing" }
$text = ([regex]::new([regex]::Escape($hooksEnd))).Replace($text, ($lifecycle + $hooksEnd), 1)

[System.IO.File]::WriteAllText($dst, $text)
Write-Host "Wrote $dst ($((($text -split "`n").Count)) lines)"

$checks = @(
    @{ Name = "Oxide.Core using"; Pattern = "using Oxide\.Core" },
    @{ Name = "RustPlugin"; Pattern = "\bRustPlugin\b" },
    @{ Name = "namespace Oxide.Plugins"; Pattern = "namespace Oxide\.Plugins\b" },
    @{ Name = "[PluginReference]"; Pattern = "\[PluginReference\]" },
    @{ Name = "[ConsoleCommand]"; Pattern = "\[ConsoleCommand" },
    @{ Name = "RustVehiclesGUIPluginBase"; Pattern = "RustVehiclesGUIPluginBase" },
    @{ Name = "HarmonyInit"; Pattern = "HarmonyInit" },
    @{ Name = "OnServerPanelClosed"; Pattern = "OnServerPanelClosed" },
    @{ Name = "API_OpenPlugin"; Pattern = "API_OpenPlugin" },
    @{ Name = "PluginBridges"; Pattern = "PluginBridges\." },
    @{ Name = "SendConsoleCommand UI_ServerPanel"; Pattern = 'SendConsoleCommand\("UI_ServerPanel' }
)
foreach ($check in $checks) {
    Write-Host ("{0}: {1}" -f $check.Name, ([regex]::Matches($text, $check.Pattern)).Count)
}
