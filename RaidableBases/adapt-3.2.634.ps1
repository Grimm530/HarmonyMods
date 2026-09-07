# Adapt RaidableBases3.2.634.cs (Oxide) into Harmony partials.
# Mechanical Oxide→Harmony transforms + soft-start/config bridges.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$oxideSrc = Join-Path (Split-Path (Split-Path $root)) "Origionals\RaidableBases3.2.634.cs"
$outDir = $root
$version = "3.2.634"

if (-not (Test-Path $oxideSrc)) { throw "Oxide source not found: $oxideSrc" }

Write-Host "Source: $oxideSrc"
Write-Host "Out:    $outDir"

$text = [System.IO.File]::ReadAllText($oxideSrc)

# --- Header / namespace / base class ---
$text = $text -replace 'using Oxide\.Core;\r?\n', ''
$text = $text -replace 'using Oxide\.Core\.Configuration;\r?\n', ''
$text = $text -replace 'using Oxide\.Core\.Libraries\.Covalence;\r?\n', ''
$text = $text -replace 'using Oxide\.Core\.Plugins;\r?\n', ''
$text = $text -replace 'using Oxide\.Game\.Rust;\r?\n', ''
$text = $text -replace 'using Oxide\.Game\.Rust\.Cui;\r?\n', ''
$text = $text -replace 'using static Oxide\.Plugins\.RaidableBasesExtensionMethods\.ExtensionMethods;', 'using static RaidableBases.RaidableBasesExtensionMethods.ExtensionMethods;'
$text = $text -replace 'namespace Oxide\.Plugins', 'namespace RaidableBases'
$text = $text -replace '\[Info\("Raidable Bases", "nivex", "3\.2\.634"\)\]\r?\n\s*', ''
$text = $text -replace '\[Description\("Create fully automated raidable bases with npcs\."\)\]\r?\n\s*', ''
$text = $text -replace 'public class RaidableBases : RustPlugin', "public partial class RaidableBases : RaidableBasesBase`r`n    {`r`n        public const string Version = `"$version`";"
$text = $text -replace '\s*private new const string Name = "RaidableBases";\r?\n', "`r`n"

# PluginReference → object stubs (3.2.634 plugin list)
$pluginBlockPattern = '(?s)\[PluginReference\]\s*Plugin\s*\r?\n\s*AbandonedBases.*?XLevels;'
$pluginBlockReplacement = @"
#pragma warning disable CS0649, CS0169
        private object AbandonedBases, DangerousTreasures, ZoneManager, BankSystem, IQEconomic, Economics, ServerRewards, GUIAnnouncements, AdvancedAlerts, Archery, Space, PocketDimensions, FauxAdmin, PreventLooting;
        private object IQDronePatrol, Friends, Clans, Kits, Mercatura, AegisPVE, TruePVE, RealPVE, SimplePVE, NightLantern, Wizardry, NextGenPVE, Imperium, Backpacks, BaseRepair, Notify, SkillTree, ShoppyStock, BuyableBases, XPerience, XLevels;
#pragma warning restore CS0649, CS0169
"@
if ($text -notmatch $pluginBlockPattern) { throw "PluginReference block not found" }
$text = [regex]::Replace($text, $pluginBlockPattern, $pluginBlockReplacement)

# Harmony Mod locals → object
$text = $text -replace '\bPlugin plugin\b', 'object plugin'
$text = $text -replace '\bPlugin CopyPaste\b', 'object CopyPaste'

# Console player
$text = $text.Replace('new Game.Rust.Libraries.Covalence.RustConsolePlayer()', 'new RustConsolePlayer()')

# Data layer
$text = $text.Replace('HarmonyModInterface.Mods.DataFileSystem.', 'HarmonyDataLayer.')
$text = $text.Replace('HarmonyModInterface.Mods.DataDirectory', 'HarmonyDataLayer.DataDirectory')

# Random / Utility / Logging / reload no-ops
$text = $text.Replace('Harmony.Core.Random.', 'Core.Random.')
$text = $text.Replace('Harmony.Core.Utility.GetFileNameWithoutExtension', 'System.IO.Path.GetFileNameWithoutExtension')
$text = $text.Replace('HarmonyModInterface.Mods.LogInfo("{0}", _sb.ToString());', 'Puts("{0}", _sb.ToString());')
$text = $text.Replace('HarmonyModInterface.Mods.LogInfo("[{0}] {1}", Name, ex);', 'UnityEngine.Debug.Log($"[{Name}] {ex}");')
$text = $text.Replace('HarmonyModInterface.Mods.LogInfo("[{0}] {1}", nameof(RaidableBases), (args.Length != 0) ? string.Format(format, args) : format);', 'UnityEngine.Debug.Log($"[{nameof(RaidableBases)}] {((args.Length != 0) ? string.Format(format, args) : format)}");')
$text = $text.Replace('Instance.Config.WriteObject(importedConfig);', 'Instance.config = importedConfig; Instance.SaveConfig();')
$text = $text.Replace('HarmonyModInterface.Mods.NextTick(() => HarmonyModInterface.Mods.ReloadPlugin(Name));', 'Instance.NextTick(() => { });')
$text = $text.Replace('NextTick(() => HarmonyModInterface.Mods.UnloadPlugin(Name));', 'NextTick(() => { });')

# HashSet paste preload → ICollection where needed
$text = $text.Replace('as HashSet<Dictionary<string, object>>', 'as ICollection<Dictionary<string, object>>')
$text = $text -replace 'Core\.Libraries\.Permission', 'HarmonyPermissionHelper'

# --- Inject Harmony lifecycle around Init/Unload/OnServerInitialized (3.2.634) ---
$initOldPattern = '(?s)        private void Init\(\)\s*\{.*?        private void OnServerShutdown\(\)\s*\{.*?        private void Unload\(\)\s*\{.*?        private void OnServerInitialized\(bool initial\)\s*\{.*?LoadOwnership\(\);\s*\}'
$initNew = @'
        internal void InitHarmony() => Init();
        internal void UnloadHarmony() => Unload();
        internal void InitMinimal()
        {
            LoadConfig();
            Kits = RaidableBasesHost.Instance?.Kits ?? new KitsPluginStub();
            KitsAPI.Init();
        }
        internal void InitRest()
        {
            if (InstallationError) return;
            _messages = new(this);
            HtmlTagRegex = new("<.*?>", RegexOptions.Compiled);
            _configPresetController = new(this);
            _pasteEngine = new(this);
            _targetInfo = new(this);
            _pasteEngine.Initialize();
            harmonyEngine ??= new HarmonyEngine(this);
            Automated = new(this, config.Settings.Maintained.Enabled, config.Settings.Schedule.Enabled);
            UndoComparer.DeployableItems = DeployableItems;
            UndoComparer.IsBox = IsBox;
            SpawnsController.Instance = this;
            UI = new() { Instance = this };
            UI.LoadOffsetData();
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                UI.DestroyAllUi(player);
            }
            IsUnloading = false;
            Buildings = new();
            GridController.Instance = this;
            IsSpawnerBusy = true;
            RegisterPermissions();
            buyableEnabled = config.Settings.Buyable.Max > 0;
            Unsubscribe(nameof(OnMapMarkerAdded));
            Unsubscribe(nameof(OnPlayerSleepEnded));
            Unsubscribe(nameof(CanBuild));
            Unsubscribe(nameof(CanLootEntity));
            Unsubscribe(nameof(OnEntitySpawned));
            UnsubscribeHooks();
            SpawnsController.Initialize();
            Queues = new(this);
        }
        private void Init()
        {
            LoadConfig();
            if (InstallationError) return;
            InitRest();
        }

        private void OnServerShutdown()
        {
            IsShuttingDown = true;
            IsUnloading = true;
        }

        private void Unload()
        {
            if (InstallationError) return;
            IsUnloading = true;
            IsSpawnerBusy = true;
            _configPresetController?.Dispose();
            _pasteEngine?.Dispose();
            _targetInfo?.Dispose();
            _messages?.Dispose();
            TryInvokeMethod(ClearPlayerDelayExclusions);
            SaveData();
            UI?.DestroyAll();
            TryInvokeMethod(StopLoadCoroutines);
            TryInvokeMethod(UnsubscribeSky);
            TryInvokeMethod(StartEntityCleanup);
            DestroyProtection();
        }

        internal void SetUnloadingState(bool unloading, bool spawnerBusy)
        {
            IsUnloading = unloading;
            IsSpawnerBusy = spawnerBusy;
        }

        internal IEnumerator RunUnloadStepsAsync()
        {
            if (InstallationError) yield break;
            _configPresetController?.Dispose();
            _pasteEngine?.Dispose();
            _targetInfo?.Dispose();
            _messages?.Dispose();
            SaveData();
            yield return null;
            UI?.DestroyAll();
            yield return null;
            UnsubscribeSky();
            yield return null;
            StartEntityCleanup();
            yield return null;
            DestroyProtection();
        }

        internal void RunUnloadStepsSync()
        {
            if (InstallationError) return;
            SaveData();
            TryInvokeMethod(UnsubscribeSky);
            TryInvokeMethod(StartEntityCleanup);
            DestroyProtection();
        }

        public void OnServerInitializedHarmony() => OnServerInitialized(true);

        public void StartSoftInitCoroutine(System.Action onComplete = null)
        {
            if (ServerMgr.Instance != null)
                ServerMgr.Instance.StartCoroutine(OnServerInitializedSoftStartCoroutine(onComplete));
            else
            {
                OnServerInitialized(true);
                onComplete?.Invoke();
            }
        }

        public IEnumerator OnServerInitializedSoftStartCoroutine(System.Action onComplete = null)
        {
            yield return null;
            if (InstallationError || IsUnloading || RaidableBasesHost.Instance == null) yield break;
            if (Queues != null) { onComplete?.Invoke(); yield break; }
            InitRest();
            yield return null;
            if (IsUnloading || RaidableBasesHost.Instance == null) yield break;
            SpawnsController.instruction0 = CoroutineEx.waitForSeconds(0.0025f);
            if (!string.IsNullOrWhiteSpace(config.Settings.EditCommand)) AddCovalenceCommand(config.Settings.EditCommand, nameof(CommandEdit));
            if (!string.IsNullOrWhiteSpace(config.Settings.BuyCommand)) AddCovalenceCommand(config.Settings.BuyCommand, nameof(CommandBuyRaid));
            if (!string.IsNullOrWhiteSpace(config.Settings.EventCommand)) AddCovalenceCommand(config.Settings.EventCommand, nameof(CommandRaidBase));
            if (!string.IsNullOrWhiteSpace(config.Settings.HunterCommand)) AddCovalenceCommand(config.Settings.HunterCommand, nameof(CommandRaidHunter));
            if (!string.IsNullOrWhiteSpace(config.Settings.ConsoleCommand)) AddCovalenceCommand(config.Settings.ConsoleCommand, nameof(CommandRaidBase));
            AddCovalenceCommand("rb.reloadconfig", nameof(CommandReloadConfig));
            AddCovalenceCommand("rb.reloadprofiles", nameof(CommandReloadConfig));
            AddCovalenceCommand("rb.reloadtables", nameof(CommandReloadConfig));
            AddCovalenceCommand("rb.config", nameof(CommandConfig), "raidablebases.config");
            AddCovalenceCommand("rb.populate", nameof(CommandPopulate), "raidablebases.config");
            AddCovalenceCommand("rb.toggle", nameof(CommandToggle), "raidablebases.config");
            AddCovalenceCommand("rb.difficulty", nameof(CommandDifficulty), "raidablebases.config");
            CommandRegistry.RegisterAttributedConsoleCommands(this);
            yield return null;
            if (IsUnloading || RaidableBasesHost.Instance == null) yield break;
            LoadPlayerData(true);
            yield return CoroutineEx.waitForSeconds(0.05f);
            if (IsUnloading || RaidableBasesHost.Instance == null) yield break;
            yield return InitializeSkinsCoroutine();
            yield return CoroutineEx.waitForSeconds(0.05f);
            if (IsUnloading || RaidableBasesHost.Instance == null) yield break;
            Initialize();
            OceanLevel = WaterSystem.OceanLevel;
            Queues.RestartCoroutine();
            timer.Repeat(Mathf.Clamp(config.EventMessages.Interval, 1f, 60f), 0, _messages.ProcessQueue);
            timer.Repeat(30f, 0, UpdateAllMarkers);
            timer.Repeat(30f, 0, CheckOceanLevel);
            timer.Repeat(300f, 0, SaveData);
            setupCopyPasteObstructionRadius = ServerMgr.Instance.StartCoroutine(SetupCopyPasteObstructionRadius());
            SubscribeDamageHook();
            BuildPrefabIds();
            LoadOwnership();
            onComplete?.Invoke();
            Puts("Soft-start complete.");
        }

        private void OnServerInitialized(bool initial)
        {
            if (InstallationError) return;
            SpawnsController.instruction0 = CoroutineEx.waitForSeconds(0.0025f);
            if (!string.IsNullOrWhiteSpace(config.Settings.EditCommand)) AddCovalenceCommand(config.Settings.EditCommand, nameof(CommandEdit));
            if (!string.IsNullOrWhiteSpace(config.Settings.BuyCommand)) AddCovalenceCommand(config.Settings.BuyCommand, nameof(CommandBuyRaid));
            if (!string.IsNullOrWhiteSpace(config.Settings.EventCommand)) AddCovalenceCommand(config.Settings.EventCommand, nameof(CommandRaidBase));
            if (!string.IsNullOrWhiteSpace(config.Settings.HunterCommand)) AddCovalenceCommand(config.Settings.HunterCommand, nameof(CommandRaidHunter));
            if (!string.IsNullOrWhiteSpace(config.Settings.ConsoleCommand)) AddCovalenceCommand(config.Settings.ConsoleCommand, nameof(CommandRaidBase));
            AddCovalenceCommand("rb.reloadconfig", nameof(CommandReloadConfig));
            AddCovalenceCommand("rb.reloadprofiles", nameof(CommandReloadConfig));
            AddCovalenceCommand("rb.reloadtables", nameof(CommandReloadConfig));
            AddCovalenceCommand("rb.config", nameof(CommandConfig), "raidablebases.config");
            AddCovalenceCommand("rb.populate", nameof(CommandPopulate), "raidablebases.config");
            AddCovalenceCommand("rb.toggle", nameof(CommandToggle), "raidablebases.config");
            AddCovalenceCommand("rb.difficulty", nameof(CommandDifficulty), "raidablebases.config");
            CommandRegistry.RegisterAttributedConsoleCommands(this);
            LoadPlayerData(initial);
            InitializeSkins();
            Initialize();
            OceanLevel = WaterSystem.OceanLevel;
            Queues.RestartCoroutine();
            timer.Repeat(Mathf.Clamp(config.EventMessages.Interval, 1f, 60f), 0, _messages.ProcessQueue);
            timer.Repeat(30f, 0, UpdateAllMarkers);
            timer.Repeat(30f, 0, CheckOceanLevel);
            timer.Repeat(300f, 0, SaveData);
            setupCopyPasteObstructionRadius = ServerMgr.Instance.StartCoroutine(SetupCopyPasteObstructionRadius());
            SubscribeDamageHook();
            BuildPrefabIds();
            LoadOwnership();
        }
'@
if ($text -notmatch $initOldPattern) { throw "Init/Unload/OnServerInitialized block not found" }
$text = [regex]::Replace($text, $initOldPattern, $initNew)
$text = $text -replace '\s*RaidableBasesExtensionMethods\.ExtensionMethods\._permission \?\?= permission;\r?\n', "`r`n"

# --- LoadConfig / SaveConfig / LoadDefaultConfig (3.2.634 Configuration region) ---
$loadConfigPattern = '(?s)        protected override void LoadConfig\(\)\s*\{\s*base\.LoadConfig\(\);\s*_extensions\.Clear\(\);.*?Interface\.Oxide\.CallHook\("OnRaidableConfigLoaded".*?\);\s*\}'
$loadConfigNew = @'
        private string _configFilePath;

        protected void LoadConfig()
        {
            _extensions.Clear();
            isInitialized = false;
            exConf = null;
            try
            {
                var path = HarmonyDataLayer.GetPreferredConfigPath();
                _configFilePath = path;
                var configDir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir)) Directory.CreateDirectory(configDir);
                if (File.Exists(path))
                    config = JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(path));
                if (config == null) { config = new Configuration(); LoadDefaultConfig(); }
                else isInitialized = true;
            }
            catch (Exception ex)
            {
                exConf = ex;
                LoadDefaultConfig();
                Puts(ex.ToString());
            }

            config.UI ??= new();
            config.UI.Theme ??= new();
            config.UI.TargetInfo ??= new();
            config.UI.AA ??= new();
            config.UI.Buyable ??= new();
            config.UI.BuyableCooldowns ??= new();
            config.UI.Delay ??= new();
            config.UI.Lockout ??= new();
            config.UI.PasteProgress ??= new();
            config.UI.Teleport ??= new();
            config.UI.Status ??= new();

            MigrateRewrittenUiConfiguration();

            if (config.DestroyDlcContainerOnceLooted == null)
            {
                config.DestroyDlcContainerOnceLooted = config.BlockPaidContent;
            }
            if (config.Settings.Management._AllowBuilding.HasValue)
            {
                allowBuilding = config.Settings.Management._AllowBuilding.Value;
                config.Settings.Management._AllowBuilding = null;
            }
            if (config.Settings.Management._AllowedBuildingBlocks != null)
            {
                allowBuildingBlockExceptions = config.Settings.Management._AllowedBuildingBlocks.ToList();
                config.Settings.Management._AllowedBuildingBlocks = null;
            }
            if (config.UI.Status.PanelColor == "#252121")
            {
                config.UI.Status.PanelColor = "#1B1B1B";
            }

            if (config.UI.Status.TitlePanelColor == "#000000")
            {
                config.UI.Status.TitlePanelColor = "#252525";
            }

            if (config.UI.Status.ColorPVP == "#FF0000")
            {
                config.UI.Status.ColorPVP = "#E5484D";
            }

            if (config.UI.Status.ColorPVE == "#008000")
            {
                config.UI.Status.ColorPVE = "#56C596";
            }

            if (config.UI.Status.NoneColor == "#FFFFFF")
            {
                config.UI.Status.NoneColor = "#E8E8E8";
            }

            if (config.UI.Status.NegativeColor == "#FF0000")
            {
                config.UI.Status.NegativeColor = "#E5484D";
            }

            if (config.UI.Status.PositiveColor == "#008000")
            {
                config.UI.Status.PositiveColor = "#56C596";
            }

            if (!string.Equals(config.UI.Status.ContentLayout, "Rows", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(config.UI.Status.ContentLayout, "Cards", StringComparison.OrdinalIgnoreCase))
            {
                config.UI.Status.ContentLayout = "Rows";
            }

            config.UI.Status.RaidDetailsFormat ??= "{type} • {difficulty} • {mode} • {state}";
            config.UI.Buyable.MoveModeSeconds = Mathf.Max(1f, config.UI.Buyable.MoveModeSeconds);
            config.UI.BuyableCooldowns.MoveModeSeconds = Mathf.Max(1f, config.UI.BuyableCooldowns.MoveModeSeconds);
            config.UI.Delay.MoveModeSeconds = Mathf.Max(1f, config.UI.Delay.MoveModeSeconds);
            config.UI.Lockout.MoveModeSeconds = Mathf.Max(1f, config.UI.Lockout.MoveModeSeconds);
            config.UI.PasteProgress.MoveModeSeconds = Mathf.Max(1f, config.UI.PasteProgress.MoveModeSeconds);
            config.UI.Status.MoveModeSeconds = Mathf.Max(1f, config.UI.Status.MoveModeSeconds);
            config.UI.Teleport.MoveModeSeconds = Mathf.Max(1f, config.UI.Teleport.MoveModeSeconds);

            if (config.Settings.Management._RequireCupboardLooted != null)
            {
                config.Settings.Management.RequireCupboardLooted = config.Settings.Management._RequireCupboardLooted.Value;
                config.Settings.Management._RequireCupboardLooted = null;
            }
            if (string.IsNullOrWhiteSpace(config.Settings.EditCommand))
            {
                const int len = 8;
                const string choices = "abcdefghijklmnopqrstuvwxyz";
                char[] buffer = new char[len];
                for (int i = 0; i < len; i++)
                    buffer[i] = choices[Core.Random.Range(0, choices.Length)];
                config.Settings.EditCommand = new string(buffer);
            }
            config.Settings.Management.BlockedMonumentMarkers.RemoveAll(string.IsNullOrWhiteSpace);
            config.Settings.Management.Inherit.RemoveAll(string.IsNullOrWhiteSpace);
            UndoSettings = new(config.Settings.Management, config.LogToFile);
            config.Settings.Management._Players = null;
            if (isInitialized) SaveConfig();
            HarmonyModInterface.CallHook("OnRaidableConfigLoaded", Version, IsPremium(), en, config.Settings.Buyable.RandomOnly, ArePurchasesConfigured(), exConf);
        }
'@
if ($text -notmatch $loadConfigPattern) { throw "LoadConfig block not found" }
$text = [regex]::Replace($text, $loadConfigPattern, $loadConfigNew)

$text = $text -replace '(?s)        protected override void SaveConfig\(\)\s*\{\s*if \(isInitialized\)\s*\{\s*Config\.WriteObject\(config\);\s*\}\s*\}', @'
        protected void SaveConfig()
        {
            if (!isInitialized) return;
            if (RaidableBasesHost.Instance != null && !string.IsNullOrEmpty(_configFilePath))
            {
                var dir = Path.GetDirectoryName(_configFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_configFilePath, JsonConvert.SerializeObject(config, Formatting.Indented));
            }
        }
'@

$text = $text -replace 'protected override void LoadDefaultConfig\(\)', 'protected void LoadDefaultConfig()'

# Fix double brace from class replace if present
$text = $text -replace "public partial class RaidableBases : RaidableBasesBase\r?\n    \{\r?\n        public const string Version = `"$version`";\r?\n    \{\r?\n", "public partial class RaidableBases : RaidableBasesBase`r`n    {`r`n        public const string Version = `"$version`";`r`n"

$adaptedPath = Join-Path $outDir "_adapted_3.2.634.cs"
[System.IO.File]::WriteAllText($adaptedPath, $text)
Write-Host "Wrote adapted monolith: $adaptedPath ($($text.Length) chars)"

# --- Split by regions ---
$lines = $text -split "`r?`n"
$total = $lines.Count
Write-Host "Total lines: $total"

$markers = @()
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^\s*#(region|endregion)') {
        $markers += [pscustomobject]@{ Line = $i + 1; Text = $lines[$i].Trim() }
    }
}

function Get-Marker([string]$exact) {
    $m = $markers | Where-Object { $_.Text -eq $exact } | Select-Object -First 1
    if (-not $m) { throw "Marker not found: $exact" }
    return $m.Line
}

$rHooks = Get-Marker '#region Hooks'
$rHooksEnd = Get-Marker '#endregion Hooks'
$rSpawn = Get-Marker '#region Spawn'
$rSpawnEnd = ($markers | Where-Object { $_.Line -gt $rSpawn -and $_.Text -eq '#endregion' } | Select-Object -First 1).Line
$rPaste = Get-Marker '#region Paste'
$rPasteEnd = ($markers | Where-Object { $_.Line -gt $rPaste -and $_.Text -eq '#endregion' } | Select-Object -First 1).Line
$rCommands = Get-Marker '#region Commands'
$rCommandsEnd = Get-Marker '#endregion Commands'
$rGarbage = Get-Marker '#region Garbage'
$rGarbageEnd = Get-Marker '#endregion Garbage'
$rIQ = Get-Marker '#region IQDronePatrol'
$rIQEnd = ($markers | Where-Object { $_.Line -gt $rIQ -and $_.Text -eq '#endregion' } | Select-Object -First 1).Line
$rHelpers = Get-Marker '#region Helpers'
$rMiscStart = $rIQEnd + 1
$rMiscEnd = $rHelpers - 1
$rHelpersEnd = ($markers | Where-Object { $_.Line -gt $rHelpers -and $_.Text -eq '#endregion' } | Select-Object -First 1).Line
$rData = Get-Marker '#region Data files'
$rDataEnd = ($markers | Where-Object { $_.Line -gt $rData -and $_.Text -eq '#endregion' } | Select-Object -First 1).Line
$rConfig = Get-Marker '#region Configuration'
$rConfigEnd = ($markers | Where-Object { $_.Line -gt (Get-Marker '#endregion Facepunch TOS Compliance') -and $_.Text -eq '#endregion' } | Select-Object -First 1).Line
$rUI = Get-Marker '#region UI'
$rUIEnd = Get-Marker '#endregion UI'

$regions = @(
    @{ Start = 1; End = ($rHooks - 1); Name = 'Main' }
    @{ Start = $rHooks; End = $rHooksEnd; Name = 'Hooks' }
    @{ Start = $rSpawn; End = $rSpawnEnd; Name = 'Spawn' }
    @{ Start = $rPaste; End = $rPasteEnd; Name = 'Paste' }
    @{ Start = $rCommands; End = $rCommandsEnd; Name = 'Commands' }
    @{ Start = $rGarbage; End = $rGarbageEnd; Name = 'Garbage' }
    @{ Start = $rIQ; End = $rIQEnd; Name = 'IQDronePatrol' }
    @{ Start = $rMiscStart; End = $rMiscEnd; Name = 'Misc' }
    @{ Start = $rHelpers; End = $rHelpersEnd; Name = 'Helpers' }
    @{ Start = $rData; End = $rDataEnd; Name = 'DataFiles' }
    @{ Start = $rConfig; End = $rConfigEnd; Name = 'Configuration' }
    @{ Start = $rUI; End = $rUIEnd; Name = 'UI' }
)

$usingLines = @(
    'using Facepunch;',
    'using HarmonyLib;',
    'using Network;',
    'using Newtonsoft.Json;',
    'using Newtonsoft.Json.Linq;',
    'using Rust;',
    'using Rust.Ai.Gen2;',
    'using Rust.Ai.Gen2.Nav;',
    'using System;',
    'using System.Collections;',
    'using System.Collections.Generic;',
    'using System.ComponentModel;',
    'using System.Diagnostics;',
    'using System.Drawing;',
    'using System.Globalization;',
    'using System.IO;',
    'using System.Reflection;',
    'using System.Reflection.Emit;',
    'using System.Runtime.CompilerServices;',
    'using System.Runtime.Serialization;',
    'using System.Text;',
    'using System.Text.RegularExpressions;',
    'using UnityEngine;',
    'using UnityEngine.AI;',
    'using UnityEngine.SceneManagement;',
    'using Newtonsoft.Json.Serialization;',
    'using Color = UnityEngine.Color;',
    'using static RaidableBases.RaidableBasesExtensionMethods.ExtensionMethods;'
) -join "`r`n"

$partialHeader = $usingLines + "`r`n`r`nnamespace RaidableBases`r`n{`r`n    public partial class RaidableBases`r`n    {`r`n`r`n"
$partialFooter = "`r`n`r`n    }`r`n}`r`n"

foreach ($r in $regions) {
    $s = $r.Start - 1
    $e = $r.End - 1
    if ($e -ge $total) { $e = $total - 1 }
    $content = ($lines[$s..$e] -join "`r`n").TrimEnd()
    if ($r.Name -eq 'Main') {
        $fullContent = $content + "`r`n    }`r`n}`r`n"
    } else {
        $fullContent = $partialHeader + $content + $partialFooter
    }
    $outPath = Join-Path $outDir "RaidableBases.$($r.Name).cs"
    [System.IO.File]::WriteAllText($outPath, $fullContent)
    Write-Host ("Wrote RaidableBases.{0}.cs (lines {1}-{2})" -f $r.Name, $r.Start, $r.End)
}

# --- ExtensionMethods ---
$extStart = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^namespace RaidableBases\.RaidableBasesExtensionMethods') { $extStart = $i; break }
}
if ($extStart -lt 0) { throw "ExtensionMethods namespace not found" }
$extLines = ($lines[$extStart..($lines.Count - 1)] -join "`r`n")

$extLines = $extLines -replace 'internal static Core\.Libraries\.Permission _permission;\r?\n\s*', ''
$extLines = $extLines -replace 'public static bool HasPermission\(this string a, string b\) \{ _permission \?\?= Interface\.Oxide\.GetLibrary<Core\.Libraries\.Permission>\(null\); return !string\.IsNullOrEmpty\(a\) && _permission\.UserHasPermission\(a, b\); \}', 'public static bool HasPermission(this string a, string b) { var p = global::RaidableBases.RaidableBasesHost.Instance?.Permission; return p != null && !string.IsNullOrEmpty(a) && p.UserHasPermission(a, b); }'
$extLines = $extLines -replace 'public static bool BelongsToGroup\(this string a, string b\) \{ _permission \?\?= Interface\.Oxide\.GetLibrary<Core\.Libraries\.Permission>\(null\); return !string\.IsNullOrEmpty\(a\) && _permission\.UserHasGroup\(a, b\); \}', 'public static bool BelongsToGroup(this string a, string b) { var p = global::RaidableBases.RaidableBasesHost.Instance?.Permission; return p != null && !string.IsNullOrEmpty(a) && p.UserHasGroup(a, b); }'
$extLines = $extLines -replace 'public static bool CanCall\(this Plugin o\) => o != null && o\.IsLoaded;', 'public static bool CanCall(this object o) => o != null;'
$extLines = $extLines -replace 'public static bool IsHuman\(this BasePlayer a\) => a\.userID\.IsSteamId\(\);', 'public static bool IsHuman(this BasePlayer a) { if (a == null) return false; try { return ((ulong)a.userID).IsSteamId(); } catch { return a.UserIDString.IsSteamId(); } }'

$harmonyHelpers = @'

        public static bool HasPermission(this IPlayer p, string perm) => p != null && !string.IsNullOrEmpty(p.Id) && p.Id.HasPermission(perm);
        public static bool IsSteamId(this string id) => !string.IsNullOrEmpty(id) && id.Length >= 17 && ulong.TryParse(id, out var v) && v.IsSteamId();
        public static bool IsSteamId(this object o)
        {
            if (o == null) return false;
            if (o is ulong u) return u.IsSteamId();
            if (o is string s) return s.IsSteamId();
            return false;
        }
        public static IPlayer GetIPlayer(this BasePlayer p) => p == null ? null : new global::RaidableBases.BasePlayerWrapper(p);
        public static TValue GetValueOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, TValue defaultValue = default) => dict != null && dict.TryGetValue(key, out var v) ? v : defaultValue;
        public static bool TryAdd<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, TValue value) { if (dict == null || dict.ContainsKey(key)) return false; dict[key] = value; return true; }
        public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value) { key = pair.Key; value = pair.Value; }
'@

if ($extLines -notmatch 'public static ulong userid\(this BasePlayer player\)') { throw "userid extension not found" }
$extLines = $extLines -replace '(public static ulong userid\(this BasePlayer player\) => \(ulong\)player\.userID;)', "`$1`r`n$harmonyHelpers"

$extHeader = @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Facepunch;
using Network;
using Newtonsoft.Json;
using Rust;
using UnityEngine;
using IPlayer = RaidableBases.IPlayer;

"@
$extFull = $extHeader + "`r`n" + $extLines.TrimEnd() + "`r`n"
if ($extFull -notmatch 'public static bool IsSteamId\(this ulong') {
    $extFull = $extFull -replace '(public static bool HasPermission\(this ulong a, string b\))', "public static bool IsSteamId(this ulong id) => id >= 76561197960265728UL;`r`n        `$1"
}
[System.IO.File]::WriteAllText((Join-Path $outDir "RaidableBasesExtensionMethods.cs"), $extFull)
Write-Host "Wrote RaidableBasesExtensionMethods.cs"
Write-Host "Done adapt $version"
