# Adapt RaidableBases3.1.5.cs (Oxide) into Harmony partials.
# Mechanical Oxide→Harmony transforms + required soft-start/config/CopyPaste bridges only.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$oxideSrc = Join-Path (Split-Path (Split-Path $root)) "Oxide.Plugins.Cant-Use\RaidableBases3.1.5.cs"
$outDir = $root
$backupDir = Join-Path $root "_backup_3.1.3_$(Get-Date -Format 'yyyyMMdd_HHmmss')"

if (-not (Test-Path $oxideSrc)) { throw "Oxide source not found: $oxideSrc" }

Write-Host "Source: $oxideSrc"
Write-Host "Out:    $outDir"
Write-Host "Backup: $backupDir"

# Backup existing partials
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
Get-ChildItem $outDir -Filter "RaidableBases.*.cs" | Where-Object { $_.Name -notmatch 'Harmony|Stubs|ExtensionMethods|Net48|GamePolyfill|csproj' } | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $backupDir $_.Name)
}
if (Test-Path (Join-Path $outDir "RaidableBasesExtensionMethods.cs")) {
    Copy-Item (Join-Path $outDir "RaidableBasesExtensionMethods.cs") (Join-Path $backupDir "RaidableBasesExtensionMethods.cs")
}

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
$text = $text -replace '\[Info\("Raidable Bases", "nivex", "3\.1\.5"\)\]\r?\n\s*', ''
$text = $text -replace '\[Description\("Create fully automated raidable bases with npcs\."\)\]\r?\n\s*', ''
$text = $text -replace 'public class RaidableBases : RustPlugin', "public partial class RaidableBases : RaidableBasesBase`r`n    {`r`n        public const string Version = `"3.1.5`";"

# Fix accidental double brace from class replace — we'll clean after PluginReference rewrite
# Remove Oxide Name const (base provides Name)
$text = $text -replace '\s*private new const string Name = "RaidableBases";\r?\n', "`r`n"

# PluginReference → object stubs
$pluginBlockPattern = '(?s)\[PluginReference\]\s*Plugin\s*\r?\n\s*AbandonedBases.*?XLevels;'
$pluginBlockReplacement = @"
#pragma warning disable CS0649, CS0169
        private object AbandonedBases, DangerousTreasures, ZoneManager, BankSystem, IQEconomic, Economics, ServerRewards, GUIAnnouncements, AdvancedAlerts, Archery, Space, PocketDimensions, FauxAdmin, PreventLooting;
        private object IQDronePatrol, Friends, Clans, Kits, TruePVE, SimplePVE, NightLantern, Wizardry, NextGenPVE, Imperium, Backpacks, BaseRepair, Notify, SkillTree, ShoppyStock, BuyableBases, XPerience, XLevels;
#pragma warning restore CS0649, CS0169
"@
if ($text -notmatch $pluginBlockPattern) { throw "PluginReference block not found" }
$text = [regex]::Replace($text, $pluginBlockPattern, $pluginBlockReplacement)

# Console player
$text = $text.Replace('new Game.Rust.Libraries.Covalence.RustConsolePlayer()', 'new RustConsolePlayer()')

# Data layer
$text = $text.Replace('Interface.Oxide.DataFileSystem.', 'HarmonyDataLayer.')

# CopyPaste API
$text = $text.Replace('CopyPaste.Call(', 'CopyPasteAPI.Call(')
$text = $text.Replace('CopyPaste.Version', 'CopyPasteAPI.Version')
$text = $text.Replace('as HashSet<Dictionary<string, object>>', 'as ICollection<Dictionary<string, object>>')
$text = $text.Replace('Did CopyPaste plugin throw an error above?', 'Is CopyPaste Harmony mod loaded?')

# Random / Utility
$text = $text.Replace('Oxide.Core.Random.', 'Core.Random.')
$text = $text.Replace('Oxide.Core.Utility.GetFileNameWithoutExtension', 'System.IO.Path.GetFileNameWithoutExtension')

# Logging
$text = $text.Replace('Interface.Oxide.LogInfo("{0}", _sb.ToString());', 'Puts("{0}", _sb.ToString());')
$text = $text.Replace('Interface.Oxide.LogInfo("[{0}] {1}", Name, ex);', 'UnityEngine.Debug.Log($"[{Name}] {ex}");')
$text = $text.Replace('Interface.Oxide.LogInfo("[{0}] {1}", Name, (args.Length != 0) ? string.Format(format, args) : format);', 'UnityEngine.Debug.Log($"[{Name}] {((args.Length != 0) ? string.Format(format, args) : format)}");')

# ReloadPlugin no-op
$text = $text.Replace('Interface.Oxide.NextTick(() => Interface.Oxide.ReloadPlugin(Name));', 'Instance.NextTick(() => { });')

# UnloadPlugin (lang mismatch) — no-op
$text = $text.Replace('NextTick(() => Interface.Oxide.UnloadPlugin(Name));', 'NextTick(() => { });')

# Remove Oxide CopyPaste property; replace IsCopyPasteLoaded with base forwarder
$cpPropPattern = '(?s)\s*private Plugin CopyPaste => plugins\.Find\("CopyPaste"\);\s*public bool IsCopyPasteLoaded\(out string error\)\s*\{[^}]+try \{ return CopyPaste\.Version >= new VersionNumber\(4, 2, 7\); \} catch \{ return false; \}\s*\}'
$cpPropReplacement = @"

        public new bool IsCopyPasteLoaded(out string error) => base.IsCopyPasteLoaded(out error);
"@
# After CopyPaste.Call replacement, the property still references plugins.Find — match original Oxide shape before Call replace... 
# Actually we already replaced CopyPaste.Version inside IsCopyPasteLoaded body. Re-match current text:
$cpPropPattern2 = '(?s)\s*private Plugin CopyPaste => plugins\.Find\("CopyPaste"\);\s*public bool IsCopyPasteLoaded\(out string error\)\s*\{.*?try \{ return CopyPasteAPI\.Version >= new VersionNumber\(4, 2, 7\); \} catch \{ return false; \}\s*\}'
if ($text -notmatch $cpPropPattern2) {
    Write-Warning "CopyPaste property/IsCopyPasteLoaded block not found with expected pattern; trying looser match"
    $cpPropPattern2 = '(?s)\s*private Plugin CopyPaste => plugins\.Find\("CopyPaste"\);\s*public bool IsCopyPasteLoaded\(out string error\)\s*\{.*?\}'
}
if ($text -notmatch $cpPropPattern2) { throw "CopyPaste/IsCopyPasteLoaded block not found" }
$text = [regex]::Replace($text, $cpPropPattern2, $cpPropReplacement)

# Fix BMGOnly CopyPaste null check: Oxide used `CopyPaste == null || CopyPaste.Version` — after replace may be broken
# Search remaining bare CopyPaste references (should be none except comments)
$bareCp = [regex]::Matches($text, '(?<![A-Za-z])CopyPaste(?!API)(?![A-Za-z])')
Write-Host "Remaining bare CopyPaste refs: $($bareCp.Count)"
foreach ($m in $bareCp) {
    $start = [Math]::Max(0, $m.Index - 40)
    $snippet = $text.Substring($start, [Math]::Min(100, $text.Length - $start)).Replace("`r","").Replace("`n"," ")
    Write-Host "  ...$snippet..."
}

# Fix elevator BMG check that used CopyPaste == null
$text = $text.Replace('Options.Elevators.BMGOnly || CopyPasteAPI == null || CopyPasteAPI.Version', 'Options.Elevators.BMGOnly || !CopyPasteAPI.IsAvailable || CopyPasteAPI.Version')
# If still has CopyPaste == null from property removal path:
$text = $text -replace 'CopyPaste\s*==\s*null\s*\|\|', '!CopyPasteAPI.IsAvailable ||'

# --- Inject Harmony soft-start around Init/Unload/OnServerInitialized ---
$initOldPattern = '(?s)        private void Init\(\)\s*\{.*?        private void OnServerInitialized\(bool isStartup\)\s*\{.*?LoadOwnership\(\);\s*\}'
$initNew = @'
        internal void InitHarmony() => Init();
        internal void UnloadHarmony() => Unload();
        /// <summary>Load config only (used from OnLoaded so load returns immediately).</summary>
        internal void InitMinimal()
        {
            LoadConfig();
        }
        /// <summary>Rest of init after config (run from deferred soft-start coroutine to avoid load freeze).</summary>
        internal void InitRest()
        {
            if (InstallationError) return;
            HtmlTagRegex = new("<.*?>", RegexOptions.Compiled);
            Automated = new(this, config.Settings.Maintained.Enabled, config.Settings.Schedule.Enabled);
            UndoComparer.DeployableItems = DeployableItems;
            UndoComparer.IsBox = IsBox;
            SpawnsController.Instance = this;
            UI = new() { Instance = this };
            UI.LoadOffsetData();
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
            SaveData();
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

        /// <summary>Unload steps with yields so entry can run unload without freezing.</summary>
        internal IEnumerator RunUnloadStepsAsync()
        {
            if (InstallationError) yield break;
            SaveData();
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

        /// <summary>Start server init as a soft-start coroutine (yields between steps so server stays responsive on harmony.load).</summary>
        public void StartSoftInitCoroutine()
        {
            if (ServerMgr.Instance != null)
                ServerMgr.Instance.StartCoroutine(OnServerInitializedSoftStartCoroutine());
            else
                OnServerInitialized(true);
        }

        /// <summary>Soft start: run server init as coroutine with yields between heavy steps.</summary>
        public IEnumerator OnServerInitializedSoftStartCoroutine()
        {
            yield return null;
            if (InstallationError) yield break;
            InitRest();
            yield return null;
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
            yield return null;
            LoadPlayerData();
            yield return CoroutineEx.waitForSeconds(0.05f);
            yield return InitializeSkinsCoroutine();
            yield return CoroutineEx.waitForSeconds(0.05f);
            if (config.Settings.Buyable.Cooldowns == null)
            {
                config.Settings.Buyable.Cooldowns = new();
                data.BuyableCooldowns.Clear();
                SaveConfig();
            }
            if (config.Settings.TeleportMarker)
                Subscribe(nameof(OnMapMarkerAdded));
            else
                Unsubscribe(nameof(OnMapMarkerAdded));
            Subscribe(nameof(OnPlayerSleepEnded));
            GridController.LoadSpawns();
            yield return CoroutineEx.waitForSeconds(0.05f);
            if (ZoneManager != null)
                SpawnsController.SetupZones(true);
            Skins.Clear();
            CreateDefaultFiles();
            yield return CoroutineEx.waitForSeconds(0.05f);
            SetOnSun(true);
            GridController.SetupGrid();
            yield return CoroutineEx.waitForSeconds(0.05f);
            OceanLevel = WaterSystem.OceanLevel;
            Queues.RestartCoroutine();
            timer.Repeat(Mathf.Clamp(config.EventMessages.Interval, 1f, 60f), 0, CheckNotifications);
            timer.Repeat(30f, 0, UpdateAllMarkers);
            timer.Repeat(30f, 0, CheckOceanLevel);
            timer.Repeat(300f, 0, SaveData);
            setupCopyPasteObstructionRadius = ServerMgr.Instance.StartCoroutine(SetupCopyPasteObstructionRadius());
            SubscribeDamageHook();
            BuildPrefabIds();
            LoadOwnership();
        }

        private void OnServerInitialized(bool isStartup)
        {
            if (InstallationError)
            {
                return;
            }
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
            LoadPlayerData();
            InitializeSkins();
            Initialize();
            OceanLevel = WaterSystem.OceanLevel;
            Queues.RestartCoroutine();
            timer.Repeat(Mathf.Clamp(config.EventMessages.Interval, 1f, 60f), 0, CheckNotifications);
            timer.Repeat(30f, 0, UpdateAllMarkers);
            timer.Repeat(30f, 0, CheckOceanLevel);
            timer.Repeat(300f, 0, SaveData);
            setupCopyPasteObstructionRadius = ServerMgr.Instance.StartCoroutine(SetupCopyPasteObstructionRadius());
            SubscribeDamageHook();
            BuildPrefabIds();
            LoadOwnership();
        }
'@

# The pattern must include OnServerShutdown between Init and Unload — adjust
$initOldPattern = '(?s)        private void Init\(\)\s*\{.*?        private void OnServerShutdown\(\)\s*\{.*?        private void Unload\(\)\s*\{.*?        private void OnServerInitialized\(bool isStartup\)\s*\{.*?LoadOwnership\(\);\s*\}'
if ($text -notmatch $initOldPattern) { throw "Init/Unload/OnServerInitialized block not found" }
$text = [regex]::Replace($text, $initOldPattern, $initNew)

# Remove Oxide permission assignment line if still present in OnServerInitialized (already removed in our replacement)
$text = $text -replace '\s*RaidableBasesExtensionMethods\.ExtensionMethods\._permission \?\?= permission;\r?\n', "`r`n"

# --- Replace LoadConfig/SaveConfig/LoadDefaultConfig with Harmony versions ---
$loadConfigPattern = '(?s)        private bool BuoyantBox;\s*private bool isInitialized = true;\s*private Exception exConf;\s*private const bool en = true;\s*private bool InstallationError;\s*protected override void LoadConfig\(\)\s*\{.*?protected override void LoadDefaultConfig\(\)\s*\{.*?populate with your profiles\."\);\s*\}'
$loadConfigNew = @'
        private bool BuoyantBox;
        private string _configFilePath;
        private bool isInitialized = true;
        private Exception exConf;
        private const bool en = true;
#pragma warning disable CS0649
        private bool InstallationError;
#pragma warning restore CS0649

        protected void LoadConfig()
        {
            if (RaidableBasesHost.Instance != null)
            {
                LoadConfigHarmony();
                return;
            }
            LoadConfigLegacy();
        }

        private void LoadConfigHarmony()
        {
            isInitialized = false;
            var path = HarmonyDataLayer.GetPreferredConfigPath();
            _configFilePath = path;
            var configDir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir)) Directory.CreateDirectory(configDir);
            try
            {
                if (File.Exists(path))
                    config = JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(path));
                if (config == null) { config = new Configuration(); LoadDefaultConfig(); }
                else isInitialized = true;
            }
            catch (Exception ex)
            {
                exConf = ex;
                config = new Configuration();
                LoadDefaultConfig();
                Puts(ex.ToString());
            }
            ProcessConfigAfterLoad();
            if (isInitialized) SaveConfig();
        }

        private void LoadConfigLegacy()
        {
            isInitialized = false;
            try
            {
                config = new Configuration();
                LoadDefaultConfig();
                isInitialized = true;
            }
            catch (Exception ex)
            {
                exConf = ex;
                LoadDefaultConfig();
                Puts(ex.ToString());
            }
            ProcessConfigAfterLoad();
        }

        private void ProcessConfigAfterLoad()
        {
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
            if (config.UI.Status.OffsetMin == new Vector2(43.957f, 87.056f))
            {
                config.UI.Status.OffsetMin = new(191.957f, 17.056f);
                config.UI.Status.OffsetMax = new(327.626f, 79.024f);
            }
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
                    buffer[i] = choices[UnityEngine.Random.Range(0, choices.Length)];
                config.Settings.EditCommand = new string(buffer);
            }
            config.Settings.Management.Inherit.RemoveAll(string.IsNullOrWhiteSpace);
            UndoSettings = new(config.Settings.Management, config.LogToFile);
            config.Settings.Management._Players = null;
        }

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

        protected void LoadDefaultConfig()
        {
            config = new();
            Puts("Loaded default configuration file. Please allow a few moments for it to populate with your profiles.");
        }
'@
if ($text -notmatch $loadConfigPattern) { throw "LoadConfig block not found" }
$text = [regex]::Replace($text, $loadConfigPattern, $loadConfigNew)

# Fix double opening brace from class declaration replace if present
$text = $text -replace 'public partial class RaidableBases : RaidableBasesBase\r?\n    \{\r?\n        public const string Version = "3\.1\.5";\r?\n    \{\r?\n', "public partial class RaidableBases : RaidableBasesBase`r`n    {`r`n        public const string Version = `"3.1.5`";`r`n"

# TryApplyAutoHeight HashSet → ICollection if present
$text = $text.Replace('HashSet<Dictionary<string, object>> preloadData', 'ICollection<Dictionary<string, object>> preloadData')
$text = $text.Replace('TryApplyAutoHeight(RandomBase rb, HashSet<Dictionary<string, object>>', 'TryApplyAutoHeight(RandomBase rb, ICollection<Dictionary<string, object>>')

# Write adapted monolith for debugging
$adaptedPath = Join-Path $outDir "_adapted_3.1.5.cs"
[System.IO.File]::WriteAllText($adaptedPath, $text)
Write-Host "Wrote adapted monolith: $adaptedPath ($($text.Length) chars)"

# --- Split by regions ---
$lines = $text -split "`r?`n"
$total = $lines.Count
Write-Host "Total lines: $total"

# Find region markers (1-based)
function Find-Line([string]$pattern) {
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match $pattern) { return $i + 1 }
    }
    return -1
}

$hooksStart = Find-Line('^\s*#region Hooks\s*$')
$hooksEnd = Find-Line('^\s*#endregion Hooks\s*$')
$spawnStart = Find-Line('^\s*#region Spawn\s*$')
$spawnEnd = Find-Line('^\s*#endregion\s*$')  # first after spawn — need careful
# Better: find all region markers
$markers = @()
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^\s*#(region|endregion)') {
        $markers += [pscustomobject]@{ Line = $i + 1; Text = $lines[$i].Trim() }
    }
}
Write-Host "Region markers:"
$markers | ForEach-Object { Write-Host ("  {0}: {1}" -f $_.Line, $_.Text) }

# Map by known names
function Get-Marker([string]$exact) {
    $m = $markers | Where-Object { $_.Text -eq $exact } | Select-Object -First 1
    if (-not $m) { throw "Marker not found: $exact" }
    return $m.Line
}

$rHooks = Get-Marker '#region Hooks'
$rHooksEnd = Get-Marker '#endregion Hooks'
$rSpawn = Get-Marker '#region Spawn'
# Spawn endregion is unnamed — find first #endregion after spawn that isn't nested
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
$rHelpersEnd = ($markers | Where-Object { $_.Line -gt $rHelpers -and $_.Text -eq '#endregion' } | Select-Object -First 1).Line
$rData = Get-Marker '#region Data files'
$rDataEnd = ($markers | Where-Object { $_.Line -gt $rData -and $_.Text -eq '#endregion' } | Select-Object -First 1).Line
$rConfig = Get-Marker '#region Configuration'
$rConfigEnd = ($markers | Where-Object { $_.Line -gt $rConfig -and $_.Text -eq '#endregion' -and $_.Line -gt (Get-Marker '#endregion Facepunch TOS Compliance') } | Select-Object -First 1).Line
# Configuration endregion is the #endregion after Facepunch TOS — the outer Configuration #endregion
$rConfigEnd = ($markers | Where-Object { $_.Line -gt (Get-Marker '#endregion Facepunch TOS Compliance') -and $_.Text -eq '#endregion' } | Select-Object -First 1).Line
$rUI = Get-Marker '#region UI'
$rUIEnd = Get-Marker '#endregion UI'

# Main is from start through line before Hooks
$regions = @(
    @{ Start = 1; End = ($rHooks - 1); Name = 'Main' }
    @{ Start = $rHooks; End = $rHooksEnd; Name = 'Hooks' }
    @{ Start = $rSpawn; End = $rSpawnEnd; Name = 'Spawn' }
    @{ Start = $rPaste; End = $rPasteEnd; Name = 'Paste' }
    @{ Start = $rCommands; End = $rCommandsEnd; Name = 'Commands' }
    @{ Start = $rGarbage; End = $rGarbageEnd; Name = 'Garbage' }
    @{ Start = $rIQ; End = $rIQEnd; Name = 'IQDronePatrol' }
    @{ Start = $rHelpers; End = $rHelpersEnd; Name = 'Helpers' }
    @{ Start = $rData; End = $rDataEnd; Name = 'DataFiles' }
    @{ Start = $rConfig; End = $rConfigEnd; Name = 'Configuration' }
    @{ Start = $rUI; End = $rUIEnd; Name = 'UI' }
)

$usingLines = @(
    'using Facepunch;',
    'using Network;',
    'using Newtonsoft.Json;',
    'using Newtonsoft.Json.Linq;',
    'using Rust;',
    'using System;',
    'using System.Collections;',
    'using System.Collections.Generic;',
    'using System.ComponentModel;',
    'using System.Diagnostics;',
    'using System.Globalization;',
    'using System.IO;',
    'using System.Runtime.CompilerServices;',
    'using System.Runtime.Serialization;',
    'using System.Text;',
    'using System.Text.RegularExpressions;',
    'using UnityEngine;',
    'using UnityEngine.AI;',
    'using UnityEngine.SceneManagement;',
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
        # Main already has usings + namespace + class open; ensure it closes class+namespace
        # Strip trailing closing braces of original file that belong after UI/ExtensionMethods
        # Main ends just before Hooks, so it should still be inside the class — close class+ns
        # But Main content includes opening namespace/class — add closing braces
        # Remove any trailing blank and ensure we don't include ExtensionMethods
        $fullContent = $content + "`r`n    }`r`n}`r`n"
    } else {
        $fullContent = $partialHeader + $content + $partialFooter
    }

    $outPath = Join-Path $outDir "RaidableBases.$($r.Name).cs"
    [System.IO.File]::WriteAllText($outPath, $fullContent)
    Write-Host ("Wrote RaidableBases.{0}.cs (lines {1}-{2})" -f $r.Name, $r.Start, $r.End)
}

# --- ExtensionMethods ---
$extNs = Find-Line('^namespace Oxide\.Plugins\.RaidableBasesExtensionMethods')
if ($extNs -lt 0) { $extNs = Find-Line('^namespace RaidableBases\.RaidableBasesExtensionMethods') }
# After namespace replace, ExtensionMethods namespace was also changed if it was Oxide.Plugins.RaidableBasesExtensionMethods
# Original: namespace Oxide.Plugins.RaidableBasesExtensionMethods — the replace `namespace Oxide.Plugins` would have made it `namespace RaidableBases.RaidableBasesExtensionMethods` ONLY if we replaced exact `namespace Oxide.Plugins` — 
# Wait: 'namespace Oxide.Plugins' would match the start of 'namespace Oxide.Plugins.RaidableBasesExtensionMethods' and become 'namespace RaidableBases.RaidableBasesExtensionMethods' — GOOD if replace is not word-boundary limited.
# Actually -replace 'namespace Oxide\.Plugins' replaces the prefix, so Oxide.Plugins.RaidableBasesExtensionMethods → RaidableBases.RaidableBasesExtensionMethods. Good.

$extStart = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^namespace RaidableBases\.RaidableBasesExtensionMethods') { $extStart = $i; break }
}
if ($extStart -lt 0) { throw "ExtensionMethods namespace not found in adapted text" }

$extLines = $lines[$extStart..($lines.Count - 1)] -join "`r`n"

# Adapt ExtensionMethods body
$extLines = $extLines -replace 'internal static Core\.Libraries\.Permission _permission;\r?\n\s*', ''
$extLines = $extLines -replace 'public static bool HasPermission\(this string a, string b\) \{ _permission \?\?= Interface\.Oxide\.GetLibrary<Core\.Libraries\.Permission>\(null\); return !string\.IsNullOrEmpty\(a\) && _permission\.UserHasPermission\(a, b\); \}', 'public static bool HasPermission(this string a, string b) { var p = global::RaidableBases.RaidableBasesHost.Instance?.Permission; return p != null && !string.IsNullOrEmpty(a) && p.UserHasPermission(a, b); }'
$extLines = $extLines -replace 'public static bool BelongsToGroup\(this string a, string b\) \{ _permission \?\?= Interface\.Oxide\.GetLibrary<Core\.Libraries\.Permission>\(null\); return !string\.IsNullOrEmpty\(a\) && _permission\.UserHasGroup\(a, b\); \}', 'public static bool BelongsToGroup(this string a, string b) { var p = global::RaidableBases.RaidableBasesHost.Instance?.Permission; return p != null && !string.IsNullOrEmpty(a) && p.UserHasGroup(a, b); }'
$extLines = $extLines -replace 'public static bool CanCall\(this Plugin o\) => o != null && o\.IsLoaded;', 'public static bool CanCall(this object o) => o != null;'
$extLines = $extLines -replace 'public static bool IsHuman\(this BasePlayer a\) => a\.userID\.IsSteamId\(\);', 'public static bool IsHuman(this BasePlayer a) => a != null && a.userID.IsSteamId();'

# Insert Harmony helpers before closing of class (before last two closing braces of namespace)
$harmonyHelpers = @'

        public static bool HasPermission(this IPlayer p, string perm) => p != null && !string.IsNullOrEmpty(p.Id) && p.Id.HasPermission(perm);
        public static bool IsSteamId(this string id) => !string.IsNullOrEmpty(id) && id.Length >= 17 && ulong.TryParse(id, out var v) && v.IsSteamId();
        public static bool IsSteamId(this object o)
        {
            if (o == null) return false;
            if (o is ulong u) return u.IsSteamId();
            if (o is string s) return s.IsSteamId();
            try
            {
                var t = o.GetType();
                var prop = t.GetProperty("Value", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (prop != null) { var v = prop.GetValue(o); if (v is ulong u2) return u2.IsSteamId(); }
            }
            catch { }
            return false;
        }
        public static IPlayer GetIPlayer(this BasePlayer p) => p == null ? null : new global::RaidableBases.BasePlayerWrapper(p);
        public static TValue GetValueOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, TValue defaultValue = default) => dict != null && dict.TryGetValue(key, out var v) ? v : defaultValue;
        public static bool TryAdd<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, TValue value) { if (dict == null || dict.ContainsKey(key)) return false; dict[key] = value; return true; }
        public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value) { key = pair.Key; value = pair.Value; }
        public static string SentenceCase(this string s) => string.IsNullOrEmpty(s) => s : char.ToUpperInvariant(s[0]) + (s.Length > 1 ? s.Substring(1).ToLowerInvariant() : "");
        public static string TitleCase(this string s) => string.IsNullOrEmpty(s) ? s : string.Join(" ", s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(w => w.Length > 0 ? char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w.Substring(1).ToLowerInvariant() : "") : ""));
'@
# Fix typo in SentenceCase (I used => instead of ?)
$harmonyHelpers = $harmonyHelpers.Replace('string.IsNullOrEmpty(s) => s :', 'string.IsNullOrEmpty(s) ? s :')

# Insert before final closing braces of ExtensionMethods class
if ($extLines -notmatch 'public static ulong userid\(this BasePlayer player\)') { throw "userid extension not found" }
$extLines = $extLines -replace '(public static ulong userid\(this BasePlayer player\) => \(ulong\)player\.userID;)', "`$1`r`n$harmonyHelpers"

$extHeader = @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Facepunch;
using Network;
using Newtonsoft.Json;
using Rust;
using UnityEngine;
using IPlayer = RaidableBases.IPlayer;

"@
# Namespace already in extLines
$extFull = $extHeader + "`r`n" + $extLines.TrimEnd() + "`r`n"
# Ensure IsSteamId(ulong) exists — Oxide has HasPermission(ulong) which calls IsSteamId — need IsSteamId(ulong)
if ($extFull -notmatch 'public static bool IsSteamId\(this ulong') {
    $extFull = $extFull -replace '(public static bool HasPermission\(this ulong a, string b\))', "public static bool IsSteamId(this ulong id) => id >= 76561197960265728UL;`r`n        `$1"
}

$extPath = Join-Path $outDir "RaidableBasesExtensionMethods.cs"
[System.IO.File]::WriteAllText($extPath, $extFull)
Write-Host "Wrote RaidableBasesExtensionMethods.cs"

# Update split.ps1 region numbers for future use
$splitPs1 = Join-Path $outDir "split.ps1"
$splitContent = @"
# Split adapted RaidableBases monolith by regions (3.1.5)
# Prefer adapt-3.1.5.ps1 for full Oxide→Harmony adaptation.
`$ErrorActionPreference = "Stop"
`$srcPath = Join-Path `$PSScriptRoot "_adapted_3.1.5.cs"
`$outDir = `$PSScriptRoot
Write-Host "Use adapt-3.1.5.ps1 instead. Regions from last run:"
Write-Host "Main 1-$($rHooks-1); Hooks $rHooks-$rHooksEnd; Spawn $rSpawn-$rSpawnEnd; Paste $rPaste-$rPasteEnd"
Write-Host "Commands $rCommands-$rCommandsEnd; Garbage $rGarbage-$rGarbageEnd; IQ $rIQ-$rIQEnd"
Write-Host "Helpers $rHelpers-$rHelpersEnd; Data $rData-$rDataEnd; Config $rConfig-$rConfigEnd; UI $rUI-$rUIEnd"
"@
[System.IO.File]::WriteAllText($splitPs1, $splitContent)

Write-Host "Done. Backup at $backupDir"
