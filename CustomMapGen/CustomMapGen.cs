using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace CustomMapGen
{
    public class CustomMapGen : IHarmonyModHooks
    {
        public static CustomMapGen Instance { get; private set; }

        /// <summary>True when the server is loading an existing map file (not a fresh wipe). When true, the mod is 100% dormant — no patches touch anything.</summary>
        public static bool IsLoadingExistingMap { get; private set; }

        /// <summary>Set from Bootstrap or InitCoroutine. When true, mod does nothing (no config, no path overrides, no procgen).</summary>
        public static void SetIsLoadingExistingMap(bool value)
        {
            IsLoadingExistingMap = value;
        }

        /// <summary>Set at start of WorldSetup.InitCoroutine: if the procedural map file already exists, we are loading (not generating).</summary>
        public static void UpdateIsLoadingExistingMap(string saveFolder, string mapFileName)
        {
            if (string.IsNullOrEmpty(mapFileName))
            {
                IsLoadingExistingMap = false;
                return;
            }
            string folder = string.IsNullOrEmpty(Path.GetPathRoot(saveFolder ?? "")) && !string.IsNullOrEmpty(saveFolder)
                ? Path.Combine(Environment.CurrentDirectory, saveFolder)
                : saveFolder ?? "";
            string path = Path.Combine(folder, mapFileName);
            IsLoadingExistingMap = File.Exists(path);
            if (IsLoadingExistingMap)
                UnityEngine.Debug.Log("[CustomMapGen] Existing map file detected — staying dormant (no procgen changes).");
        }

        private static readonly string CONFIG_PATH = Path.Combine(Application.dataPath, "..", "HarmonyConfig", "CustomMapGen.json");
        
        private MapGenConfig _config;
        
        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            UnityEngine.Debug.Log("[CustomMapGen] ===== OnLoaded Hook Called =====");
            Instance = this;
            UnityEngine.Debug.Log("[CustomMapGen] Instance set, about to LoadConfig...");
            LoadConfig();
            UnityEngine.Debug.Log("[CustomMapGen] LoadConfig completed, starting patch verification...");
            
            // Verify patches were registered
            UnityEngine.Debug.Log("[CustomMapGen] Starting patch verification...");
            var assembly = Assembly.GetExecutingAssembly();
            UnityEngine.Debug.Log($"[CustomMapGen] Assembly: {assembly.FullName}");
            var allTypes = assembly.GetTypes();
            UnityEngine.Debug.Log($"[CustomMapGen] Total types in assembly: {allTypes.Length}");
            var patchTypes = new List<Type>();
            for (int i = 0; i < allTypes.Length; i++)
            {
                var t = allTypes[i];
                if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0)
                    patchTypes.Add(t);
            }
            UnityEngine.Debug.Log($"[CustomMapGen] Found {patchTypes.Count} patch classes in assembly");
            if (patchTypes.Count == 0)
                UnityEngine.Debug.LogError("[CustomMapGen] WARNING: No patch classes found! Patches may not be working!");
            else
            {
                foreach (var patchType in patchTypes)
                    UnityEngine.Debug.Log($"[CustomMapGen] Patch class registered: {patchType.Name}");
            }

            UnityEngine.Debug.Log("[CustomMapGen] Loaded - Custom procedural map generation enabled");
            UnityEngine.Debug.Log($"[CustomMapGen] Config: Lakes={_config.LakeMinAmount}-{_config.LakeMaxAmount} ({_config.LakesGenerate}), Islands={_config.IslandsEnabled} (Intensity: {_config.IslandIntensity}), Cliffs={_config.EnableCliffs}, Powerlines={_config.Powerlines}, Ziplines={_config.Ziplines}, AboveGroundRails={_config.GenerateAboveGroundTrainTracks}, Rivers={!_config.RemoveRivers}, DebugLogging={_config.DebugLogging}, DebugLogSkippedWorldPrefabs={_config.DebugLogSkippedWorldPrefabs}, DebugLogSwapMapPrefabBreakdown={_config.DebugLogSwapMapPrefabBreakdown}");
            UnityEngine.Debug.Log("[CustomMapGen] NOTE: Patch execution messages will only appear when map generation starts!");
        }
        
        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            // Don't save config on unload to prevent overwriting user changes
            // Config is only saved when explicitly needed (e.g., creating default)
            Instance = null;
            UnityEngine.Debug.Log("[CustomMapGen] Unloaded");
        }
        
        private void LoadConfig()
        {
            if (File.Exists(CONFIG_PATH))
            {
                string json = File.ReadAllText(CONFIG_PATH);
                var settings = new JsonSerializerSettings
                {
                    DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate,
                    NullValueHandling = NullValueHandling.Ignore
                };
                _config = JsonConvert.DeserializeObject<MapGenConfig>(json, settings);
                var jo = Newtonsoft.Json.Linq.JObject.Parse(json);
                if (jo["Powerlines"] == null)
                    _config.Powerlines = !(_config.RemoveSmallPowerLines && _config.RemoveLargePowerLines);
                if (jo["DeferDoneWorkSeconds"] == null)
                    _config.DeferDoneWorkSeconds = 0.2f;
                if (_config.BlockedPrefabs == null)
                    _config.BlockedPrefabs = new List<string>();
                if (_config.MapSettings == null) _config.MapSettings = new MapSettingsConfig();
                if (_config.MapImage == null) _config.MapImage = new MapImageConfig();
                if (_config.SwapMonuments == null) _config.SwapMonuments = new SwapMonumentsConfig();
                // Backwards compat: merge top-level outpost settings into SwapMonuments if present
                if (jo["TrySpawningOutpostInCenter"] != null) _config.SwapMonuments.TrySpawningOutpostInCenter = jo["TrySpawningOutpostInCenter"].ToObject<bool>();
                if (jo["AllowBanditCamp"] != null) _config.SwapMonuments.AllowBanditCamp = jo["AllowBanditCamp"].ToObject<bool>();
                if (jo["FillBanditSlotWithMonument"] != null) _config.SwapMonuments.FillBanditSlotWithMonument = jo["FillBanditSlotWithMonument"].ToObject<bool>();
                if (jo["UseBlockedOutpostSlotForRelocation"] != null) _config.SwapMonuments.UseBlockedOutpostSlotForRelocation = jo["UseBlockedOutpostSlotForRelocation"].ToObject<bool>();
                if (string.IsNullOrEmpty(_config.Language)) _config.Language = "en";
                UnityEngine.Debug.Log("[CustomMapGen] Config loaded from HarmonyConfig/CustomMapGen.json");
            }
            else
            {
                _config = MapGenConfig.Default();
                SaveConfig();
                UnityEngine.Debug.Log("[CustomMapGen] Created default config at HarmonyConfig/CustomMapGen.json");
            }
        }
        
        private void SaveConfig()
        {
            string dir = Path.GetDirectoryName(CONFIG_PATH);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var settings = new JsonSerializerSettings
            {
                DefaultValueHandling = DefaultValueHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.Indented
            };
            string json = JsonConvert.SerializeObject(_config, settings);
            File.WriteAllText(CONFIG_PATH, json);
        }
        
        public MapGenConfig GetConfig()
        {
            return _config ?? MapGenConfig.Default();
        }
        
        public static bool IsCustomMapGenEnabled()
        {
            return Instance != null && Instance.GetConfig().Enabled;
        }
    }
    
    [Serializable]
    public class MapGenConfig
    {
        public bool Enabled = true;
        
        // Infrastructure settings (matching standard.json)
        public string GenerateRingRoad = "Wanted"; // "Wanted", "NotWanted", "NoPreference"
        [Newtonsoft.Json.JsonProperty("AboveGroundRails")]
        public string GenerateAboveGroundTrainTracks = "Wanted"; // "Wanted", "NotWanted", "NoPreference" - Controls above-ground railroad generation
        public bool RemoveSmallPowerLines = true;
        public bool RemoveLargePowerLines = true;
        /// <summary>Simple toggle: true = allow powerlines, false = remove all. Overrides RemoveSmallPowerLines/RemoveLargePowerLines when set in JSON.</summary>
        public bool Powerlines = false;
        /// <summary>Simple toggle: true = allow ziplines (launch/arrival points), false = block zipline prefabs during map gen.</summary>
        public bool Ziplines = true;
        public bool RemoveRivers = false;
        public bool RemoveCarWrecks = true;
        public bool AllowBuildingOnRoads = false;
        /// <summary>Minimum distance in meters between different monument types (e.g. oasis and outpost). When &gt; 0, applied to PlaceMonuments so they don't spawn on top of each other. Default 75. Set to 0 to use game default.</summary>
        public int MinMonumentDistance = 75;
        /// <summary>Minimum distance in meters from small monuments (e.g. Large Barn, Gas Station) to any large monument (e.g. Outpost, Water Treatment). When &gt; 0, small monuments that would spawn within this distance of a large monument are relocated. Default 50. Set to 0 to disable.</summary>
        public int MinDistanceSmallToLargeMonument = 50;
        /// <summary>Convenience getter: TrySpawningOutpostInCenter from SwapMonuments.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool TrySpawningOutpostInCenter => SwapMonuments?.TrySpawningOutpostInCenter ?? true;
        /// <summary>Convenience getter: AllowBanditCamp from SwapMonuments.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool AllowBanditCamp => SwapMonuments?.AllowBanditCamp ?? false;
        /// <summary>Convenience getter: FillBanditSlotWithMonument from SwapMonuments.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool FillBanditSlotWithMonument => SwapMonuments?.FillBanditSlotWithMonument ?? true;
        /// <summary>Convenience getter: UseBlockedOutpostSlotForRelocation from SwapMonuments.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool UseBlockedOutpostSlotForRelocation => SwapMonuments?.UseBlockedOutpostSlotForRelocation ?? true;
        public bool RemoveUndergroundTunnels = false;
        /// <summary>When true, add rail segments from the above-ground rail network to each train tunnel entrance so tracks connect visually. Runs after GenerateDungeonGrid.</summary>
        public bool ConnectRailsToTunnelEntrances = true;
        public bool EmbedCargoShipPath = true;
        
        // Cliff settings
        public bool EnableCliffs = true;
        
        // Lake settings (matching standard.json lakesConfiguration)
        public int LakeMinAmount = 2;
        public int LakeMaxAmount = 2;
        public bool LakesBlocked = false;
        public string LakesGenerate = "Wanted"; // "Wanted", "NotWanted", "NoPreference"
        
        // Island settings (matching standard.json islandConfig)
        public bool IslandsEnabled = true;
        public int IslandIntensity = 7; // 0-10
        
        // Oases settings (matching standard.json oasesConfiguration)
        public int OasesMinAmount = 2;
        public int OasesMaxAmount = 2;
        public bool OasesBlocked = false;
        public string OasesGenerate = "Wanted"; // "Wanted", "NotWanted", "NoPreference"
        
        // Canyons settings (matching standard.json canyonsConfiguration)
        public int CanyonsMinAmount = 2;
        public int CanyonsMaxAmount = 2;
        public bool CanyonsBlocked = false;
        public string CanyonsGenerate = "NotWanted"; // "Wanted", "NotWanted", "NoPreference"
        
        // Underwater labs configuration (matching standard.json underwaterLabsConfiguration)
        public int UnderwaterLabsMinAmount = 1;
        public int UnderwaterLabsMaxAmount = 1;
        public bool UnderwaterLabsBlocked = false;
        public string UnderwaterLabsGenerate = "NoPreference"; // "Wanted", "NotWanted", "NoPreference"
        
        // Terrain configuration (matching standard.json terrainConfiguration)
        public TerrainConfig TerrainConfiguration = new TerrainConfig();
        
        // Monument configurations (matching standard.json)
        public List<OilRigConfig> OilRigConfigurations = new List<OilRigConfig>();
        public List<SafezoneConfig> Safezones = new List<SafezoneConfig>();
        public List<MonumentConfig> LargeMonuments = new List<MonumentConfig>();
        public List<MonumentConfig> SmallMonuments = new List<MonumentConfig>();
        public List<MonumentConfig> Harbors = new List<MonumentConfig>();
        public List<MonumentConfig> WaterWells = new List<MonumentConfig>();
        public List<MonumentConfig> Caves = new List<MonumentConfig>();
        public List<MonumentConfig> Mountains = new List<MonumentConfig>();
        public List<MonumentConfig> Quarries = new List<MonumentConfig>();
        public List<MonumentConfig> Icebergs = new List<MonumentConfig>();
        public List<MonumentConfig> IceLakes = new List<MonumentConfig>();
        public List<MonumentConfig> Ruins = new List<MonumentConfig>();
        
        // Webhook configuration (matching standard.json webhook)
        public WebhookConfig Webhook = new WebhookConfig();
        
        // Blocked prefabs. Use "BlockedPrefabs" in CustomMapGen.json (PascalCase). Entries match prefab path substrings (e.g. coastal_rocks, rock_formation_small).
        public List<string> BlockedPrefabs = new List<string>();
        
        // --- QoL & Map Settings (HarmonyCustomGenerator parity) ---
        /// <summary>Skip asset warmup on server start to reduce startup time.</summary>
        public bool SkipAssetWarmup = false;
        /// <summary>Map settings: override size (3500-6000), save folder, save name, force new seed each time.</summary>
        public MapSettingsConfig MapSettings = new MapSettingsConfig();
        /// <summary>After map generation, render splat/height map image and optionally monument names/grid.</summary>
        public MapImageConfig MapImage = new MapImageConfig();
        /// <summary>Replace vanilla monuments with custom .map prefabs from maps/prefabs (e.g. harbor_1.prefab.map).</summary>
        public SwapMonumentsConfig SwapMonuments = new SwapMonumentsConfig();
        /// <summary>Config language for future RU/EN support (e.g. "en", "ru").</summary>
        public string Language = "en";
        
        /// <summary>When true, extra debug logs for outpost redirect, compound prefab skipping, and shore flattening. Set to false once issues are resolved.</summary>
        public bool DebugLogging = true;

        /// <summary>FREEZE DEBUG: When true, skip RunDeferredCompoundSpawn when LoadingScreen.Update("DONE") runs. If server no longer freezes, deferred spawn was the cause. Set back to false after testing.</summary>
        public bool SkipDeferredCompoundSpawnAtDone = false;

        /// <summary>FREEZE DEBUG: When true, skip PostSaveSwap.Run when LoadingScreen.Update("DONE") runs. If server no longer freezes, PostSaveSwap at DONE was the cause. Set back to false after testing.</summary>
        public bool SkipPostSaveSwapAtDone = false;

        /// <summary>When &gt; 0, DONE work (deferred spawn + PostSaveSwap) runs after this many seconds instead of inline. Lets LoadingScreen.Update("DONE") return immediately so the server can continue (avoids freeze after map creation). Default 0.2. Set to 0 to run inline (old behavior).</summary>
        public float DeferDoneWorkSeconds = 0.2f;

        /// <summary>
        /// When true, logs every skipped <see cref="World"/> prefab spawn where <c>Prefab.Load</c> produced a wrapper whose
        /// <c>Object</c> is null (FindPrefab failed). Vanilla skips silently — this is the usual reason industrial/electrical/IO
        /// entities &quot;exist in RustEdit&quot; but never appear at runtime.
        /// </summary>
        public bool DebugLogSkippedWorldPrefabs = false;

        /// <summary>
        /// When true (and <see cref="DebugLogging"/>), logs category counts and IO/industrial/electric-like prefab samples from the
        /// raw custom .map before pasted prefabs are transformed onto the monument anchor.
        /// </summary>
        public bool DebugLogSwapMapPrefabBreakdown = false;

        /// <summary>Isolate ocean-topology issues: when true, shore-flatten patch is skipped entirely. Set true to test; add back false one patch at a time.</summary>
        public bool DisableShoreFlattenPatch = false;
        /// <summary>Isolate issues: when true, powerline-layout patch is skipped (same as Powerlines=false).</summary>
        public bool DisablePowerlineLayoutPatch = false;
        /// <summary>Isolate issues: when true, rail patches do nothing (vanilla rail behavior).</summary>
        public bool DisableRailPatch = false;
        /// <summary>Isolate issues: when true, river-layout patch does nothing (vanilla river behavior).</summary>
        public bool DisableRiverLayoutPatch = false;
        /// <summary>Isolate topology/issues: when true, road-topology (MarkRoadside) patch is skipped.</summary>
        public bool DisableRoadTopologyPatch = false;
        /// <summary>Isolate issues: when true, lake-info (ProcessProceduralObjects lake cleanup) patch is skipped.</summary>
        public bool DisableLakeInfoPatch = false;
        /// <summary>Isolate issues: when true, mountain-reduction patch is skipped.</summary>
        public bool DisableMountainPatch = false;
        /// <summary>Isolate issues: when true, PlaceCliffs patch is skipped (vanilla cliff behavior).</summary>
        public bool DisablePlaceCliffsPatch = false;
        /// <summary>Isolate issues: when true, PlaceMonumentsOffshore (islands) patch is skipped.</summary>
        public bool DisablePlaceMonumentsOffshorePatch = false;
        /// <summary>Isolate issues: when true, PlaceMonuments (oases/canyons filter) patch is skipped.</summary>
        public bool DisablePlaceMonumentsPatch = false;
        /// <summary>Isolate issues: when true, PlaceMonumentsRoadside (car wrecks filter) patch is skipped.</summary>
        public bool DisablePlaceMonumentsRoadsidePatch = false;
        /// <summary>When true, after filtering invalid prefab IDs from the world list we save the map so the file no longer contains them (permanent fix; next load won't have errors even if mod is unloaded). Default true.</summary>
        [System.ComponentModel.DefaultValue(true)]
        public bool SaveMapAfterFilteringInvalidPrefabs = true;

        /// <summary>Isolate issues: when true, World.AddPrefab patch is skipped (vanilla prefab/add behavior). NEW-only vs CustomMapGenOld.</summary>
        public bool DisableWorldAddPrefabPatch = false;
        /// <summary>Isolate issues: when true, World map settings (Name, MapFolderName, etc.) patches are skipped. NEW-only vs CustomMapGenOld.</summary>
        public bool DisableWorldMapSettingsPatch = false;
        /// <summary>Isolate issues: when true, ProcgenConfigApply (World.Config apply at InitCoroutine/ProcessProceduralObjects/GenerateHeight) is skipped. NEW-only vs CustomMapGenOld.</summary>
        public bool DisableProcgenConfigApplyPatch = false;

        /// <summary>When true, both Tier and Biome percentages are written to World.Config. When false, use ApplyTierToWorldConfig / ApplyBiomeToWorldConfig to apply only one (avoids mainland ocean topology if only biomes are applied).</summary>
        public bool ApplyTierBiomeToWorldConfig = false;

        /// <summary>When true, Tier percentages (Tier0/1/2) are written to World.Config. Ignored if ApplyTierBiomeToWorldConfig is true.</summary>
        public bool ApplyTierToWorldConfig = false;

        /// <summary>When true, Biome percentages (Arid/Temperate/Tundra/Arctic/Jungle) are written to World.Config. Ignored if ApplyTierBiomeToWorldConfig is true.</summary>
        public bool ApplyBiomeToWorldConfig = false;

        // Legacy support (for backward compatibility)
        [Newtonsoft.Json.JsonIgnore]
        public int LakeCount
        {
            get { return LakesBlocked ? 0 : (LakeMinAmount == LakeMaxAmount ? LakeMinAmount : -1); }
            set
            {
                if (value < 0)
                {
                    LakesGenerate = "NoPreference";
                    LakesBlocked = false;
                }
                else if (value == 0)
                {
                    LakesBlocked = true;
                    LakesGenerate = "NotWanted";
                }
                else
                {
                    LakeMinAmount = value;
                    LakeMaxAmount = value;
                    LakesBlocked = false;
                    LakesGenerate = "Wanted";
                }
            }
        }
        
        [Newtonsoft.Json.JsonIgnore]
        public int IslandCount
        {
            get { return IslandsEnabled ? (IslandIntensity > 0 ? IslandIntensity : -1) : 0; }
            set
            {
                if (value < 0)
                {
                    IslandsEnabled = true;
                    IslandIntensity = 7;
                }
                else if (value == 0)
                {
                    IslandsEnabled = false;
                    IslandIntensity = 0;
                }
                else
                {
                    IslandsEnabled = true;
                    IslandIntensity = Math.Min(10, Math.Max(1, value));
                }
            }
        }
        
        [Newtonsoft.Json.JsonIgnore]
        public bool EnablePowerlines
        {
            get { return Powerlines; }
            set { Powerlines = value; RemoveSmallPowerLines = !value; RemoveLargePowerLines = !value; }
        }
        
        [Newtonsoft.Json.JsonIgnore]
        public bool EnableAboveGroundRails
        {
            get { return GenerateAboveGroundTrainTracks == "Wanted"; }
            set { GenerateAboveGroundTrainTracks = value ? "Wanted" : "NotWanted"; }
        }
        
        public static MapGenConfig Default()
        {
            return new MapGenConfig
            {
                Enabled = true,
                GenerateRingRoad = "Wanted",
                GenerateAboveGroundTrainTracks = "Wanted",
                RemoveSmallPowerLines = true,
                RemoveLargePowerLines = true,
                Powerlines = false,
                Ziplines = true,
                RemoveRivers = false,
                RemoveCarWrecks = true,
                EnableCliffs = true,
                LakeMinAmount = 2,
                LakeMaxAmount = 2,
                LakesBlocked = false,
                LakesGenerate = "Wanted",
                IslandsEnabled = true,
                IslandIntensity = 7,
                OasesMinAmount = 2,
                OasesMaxAmount = 2,
                OasesBlocked = false,
                OasesGenerate = "Wanted",
                CanyonsMinAmount = 2,
                CanyonsMaxAmount = 2,
                CanyonsBlocked = false,
                CanyonsGenerate = "NotWanted",
                UnderwaterLabsMinAmount = 1,
                UnderwaterLabsMaxAmount = 1,
                UnderwaterLabsBlocked = false,
                UnderwaterLabsGenerate = "NoPreference",
                AllowBuildingOnRoads = false,
                MinMonumentDistance = 75,
                MinDistanceSmallToLargeMonument = 50,
                RemoveUndergroundTunnels = false,
                ConnectRailsToTunnelEntrances = true,
                EmbedCargoShipPath = true,
                TerrainConfiguration = TerrainConfig.Default(),
                OilRigConfigurations = new List<OilRigConfig>(),
                Safezones = new List<SafezoneConfig>(),
                LargeMonuments = new List<MonumentConfig>(),
                SmallMonuments = new List<MonumentConfig>(),
                Harbors = new List<MonumentConfig>(),
                WaterWells = new List<MonumentConfig>(),
                Caves = new List<MonumentConfig>(),
                Mountains = new List<MonumentConfig>(),
                Quarries = new List<MonumentConfig>(),
                Icebergs = new List<MonumentConfig>(),
                IceLakes = new List<MonumentConfig>(),
                Ruins = new List<MonumentConfig>(),
                Webhook = new WebhookConfig { Enabled = false, Url = "" },
                BlockedPrefabs = new List<string>(),
                SkipAssetWarmup = false,
                MapSettings = new MapSettingsConfig(),
                MapImage = new MapImageConfig(),
                SwapMonuments = new SwapMonumentsConfig
                {
                    TrySpawningOutpostInCenter = true,
                    AllowBanditCamp = false,
                    FillBanditSlotWithMonument = true,
                    UseBlockedOutpostSlotForRelocation = true
                },
                Language = "en",
                DebugLogging = true,
                SkipDeferredCompoundSpawnAtDone = false,
                SkipPostSaveSwapAtDone = false,
                DeferDoneWorkSeconds = 0.2f,
                DebugLogSkippedWorldPrefabs = false,
                DebugLogSwapMapPrefabBreakdown = false,
                DisableShoreFlattenPatch = false,
                DisablePowerlineLayoutPatch = false,
                DisableRailPatch = false,
                DisableRiverLayoutPatch = false,
                DisableRoadTopologyPatch = false,
                DisableLakeInfoPatch = false,
                DisableMountainPatch = false,
                DisablePlaceCliffsPatch = false,
                DisablePlaceMonumentsOffshorePatch = false,
                DisablePlaceMonumentsPatch = false,
                DisablePlaceMonumentsRoadsidePatch = false,
                SaveMapAfterFilteringInvalidPrefabs = true,
                DisableWorldAddPrefabPatch = false,
                DisableWorldMapSettingsPatch = false,
                DisableProcgenConfigApplyPatch = false,
                ApplyTierBiomeToWorldConfig = false,
                ApplyTierToWorldConfig = false,
                ApplyBiomeToWorldConfig = false
            };
        }

        // Convert from standard.json format (camelCase) to our config format
        public static MapGenConfig FromStandardJson(Newtonsoft.Json.Linq.JObject standardJson)
        {
            var config = Default();
            // Map camelCase fields from standard.json to PascalCase config
                if (standardJson["generateRingRoad"] != null)
                    config.GenerateRingRoad = standardJson["generateRingRoad"].ToString();
                if (standardJson["generateAboveGroundTrainTracks"] != null)
                    config.GenerateAboveGroundTrainTracks = standardJson["generateAboveGroundTrainTracks"].ToString();
                if (standardJson["removeSmallPowerLines"] != null)
                    config.RemoveSmallPowerLines = standardJson["removeSmallPowerLines"].ToObject<bool>();
                if (standardJson["removeLargePowerLines"] != null)
                    config.RemoveLargePowerLines = standardJson["removeLargePowerLines"].ToObject<bool>();
                config.Powerlines = !config.RemoveSmallPowerLines && !config.RemoveLargePowerLines;
                if (standardJson["removeRivers"] != null)
                    config.RemoveRivers = standardJson["removeRivers"].ToObject<bool>();
                if (standardJson["removeCarWrecks"] != null)
                    config.RemoveCarWrecks = standardJson["removeCarWrecks"].ToObject<bool>();
                if (standardJson["allowBuildingOnRoads"] != null)
                    config.AllowBuildingOnRoads = standardJson["allowBuildingOnRoads"].ToObject<bool>();
                if (standardJson["trySpawningOutpostInCenter"] != null)
                    config.SwapMonuments.TrySpawningOutpostInCenter = standardJson["trySpawningOutpostInCenter"].ToObject<bool>();
                if (standardJson["allowBanditCamp"] != null)
                    config.SwapMonuments.AllowBanditCamp = standardJson["allowBanditCamp"].ToObject<bool>();
                if (standardJson["fillBanditSlotWithMonument"] != null)
                    config.SwapMonuments.FillBanditSlotWithMonument = standardJson["fillBanditSlotWithMonument"].ToObject<bool>();
                if (standardJson["useBlockedOutpostSlotForRelocation"] != null)
                    config.SwapMonuments.UseBlockedOutpostSlotForRelocation = standardJson["useBlockedOutpostSlotForRelocation"].ToObject<bool>();
                if (standardJson["removeUndergroundTunnels"] != null)
                    config.RemoveUndergroundTunnels = standardJson["removeUndergroundTunnels"].ToObject<bool>();
                if (standardJson["embedCargoShipPath"] != null)
                    config.EmbedCargoShipPath = standardJson["embedCargoShipPath"].ToObject<bool>();
                
                // Lakes configuration
                var lakesConfig = standardJson["lakesConfiguration"];
                if (lakesConfig != null)
                {
                    if (lakesConfig["minAmount"] != null) config.LakeMinAmount = lakesConfig["minAmount"].ToObject<int>();
                    if (lakesConfig["maxAmount"] != null) config.LakeMaxAmount = lakesConfig["maxAmount"].ToObject<int>();
                    if (lakesConfig["blocked"] != null) config.LakesBlocked = lakesConfig["blocked"].ToObject<bool>();
                    if (lakesConfig["generate"] != null) config.LakesGenerate = lakesConfig["generate"].ToString();
                }
                
                // Oases configuration
                var oasesConfig = standardJson["oasesConfiguration"];
                if (oasesConfig != null)
                {
                    if (oasesConfig["minAmount"] != null) config.OasesMinAmount = oasesConfig["minAmount"].ToObject<int>();
                    if (oasesConfig["maxAmount"] != null) config.OasesMaxAmount = oasesConfig["maxAmount"].ToObject<int>();
                    if (oasesConfig["blocked"] != null) config.OasesBlocked = oasesConfig["blocked"].ToObject<bool>();
                    if (oasesConfig["generate"] != null) config.OasesGenerate = oasesConfig["generate"].ToString();
                }
                
                // Canyons configuration
                var canyonsConfig = standardJson["canyonsConfiguration"];
                if (canyonsConfig != null)
                {
                    if (canyonsConfig["minAmount"] != null) config.CanyonsMinAmount = canyonsConfig["minAmount"].ToObject<int>();
                    if (canyonsConfig["maxAmount"] != null) config.CanyonsMaxAmount = canyonsConfig["maxAmount"].ToObject<int>();
                    if (canyonsConfig["blocked"] != null) config.CanyonsBlocked = canyonsConfig["blocked"].ToObject<bool>();
                    if (canyonsConfig["generate"] != null) config.CanyonsGenerate = canyonsConfig["generate"].ToString();
                }
                
                // Underwater labs configuration
                var underwaterLabsConfig = standardJson["underwaterLabsConfiguration"];
                if (underwaterLabsConfig != null)
                {
                    if (underwaterLabsConfig["minAmount"] != null) config.UnderwaterLabsMinAmount = underwaterLabsConfig["minAmount"].ToObject<int>();
                    if (underwaterLabsConfig["maxAmount"] != null) config.UnderwaterLabsMaxAmount = underwaterLabsConfig["maxAmount"].ToObject<int>();
                    if (underwaterLabsConfig["blocked"] != null) config.UnderwaterLabsBlocked = underwaterLabsConfig["blocked"].ToObject<bool>();
                    if (underwaterLabsConfig["generate"] != null) config.UnderwaterLabsGenerate = underwaterLabsConfig["generate"].ToString();
                }
                
                // Terrain configuration
                var terrainConfig = standardJson["terrainConfiguration"];
                if (terrainConfig != null)
                {
                    config.TerrainConfiguration = JsonConvert.DeserializeObject<TerrainConfig>(terrainConfig.ToString());
                }
                
                // Blocked prefabs
                if (standardJson["blockedPrefabs"] != null)
                {
                    config.BlockedPrefabs = standardJson["blockedPrefabs"].ToObject<List<string>>();
                }
                
                // Webhook
                if (standardJson["webhook"] != null)
                {
                    config.Webhook = JsonConvert.DeserializeObject<WebhookConfig>(standardJson["webhook"].ToString());
                }
                
                // Monument configurations - convert all arrays
                if (standardJson["oilRigConfigurations"] != null)
                {
                    config.OilRigConfigurations = JsonConvert.DeserializeObject<List<OilRigConfig>>(standardJson["oilRigConfigurations"].ToString());
                }
                
                if (standardJson["safezones"] != null)
                {
                    config.Safezones = JsonConvert.DeserializeObject<List<SafezoneConfig>>(standardJson["safezones"].ToString());
                }
                
                if (standardJson["largeMonuments"] != null)
                {
                    config.LargeMonuments = JsonConvert.DeserializeObject<List<MonumentConfig>>(standardJson["largeMonuments"].ToString());
                }
                
                if (standardJson["smallMonuments"] != null)
                {
                    config.SmallMonuments = JsonConvert.DeserializeObject<List<MonumentConfig>>(standardJson["smallMonuments"].ToString());
                }
                
                if (standardJson["harbors"] != null)
                {
                    config.Harbors = JsonConvert.DeserializeObject<List<MonumentConfig>>(standardJson["harbors"].ToString());
                }
                
                if (standardJson["waterWells"] != null)
                {
                    config.WaterWells = JsonConvert.DeserializeObject<List<MonumentConfig>>(standardJson["waterWells"].ToString());
                }
                
                if (standardJson["caves"] != null)
                {
                    config.Caves = JsonConvert.DeserializeObject<List<MonumentConfig>>(standardJson["caves"].ToString());
                }
                
                if (standardJson["mountains"] != null)
                {
                    config.Mountains = JsonConvert.DeserializeObject<List<MonumentConfig>>(standardJson["mountains"].ToString());
                }
                
                if (standardJson["quarries"] != null)
                {
                    config.Quarries = JsonConvert.DeserializeObject<List<MonumentConfig>>(standardJson["quarries"].ToString());
                }
                
                if (standardJson["icebergs"] != null)
                {
                    config.Icebergs = JsonConvert.DeserializeObject<List<MonumentConfig>>(standardJson["icebergs"].ToString());
                }
                
                if (standardJson["iceLakes"] != null)
                {
                    config.IceLakes = JsonConvert.DeserializeObject<List<MonumentConfig>>(standardJson["iceLakes"].ToString());
                }
                
                if (standardJson["ruins"] != null)
                {
                    config.Ruins = JsonConvert.DeserializeObject<List<MonumentConfig>>(standardJson["ruins"].ToString());
                }
            return config;
        }
    }
    
    [Serializable]
    public class OilRigConfig
    {
        public BiomePreferenceConfig BiomePreference = new BiomePreferenceConfig();
        public PositionConfig Position = new PositionConfig();
        public bool Desired = true;
        public string Type = ""; // "Large Oilrig", "Small Oilrig"
        public bool Blocked = false;
        public bool AllowedToSetBiomes = false;
        public List<BiomePreferenceItem> BiomePreferences = null;
    }
    
    [Serializable]
    public class SafezoneConfig
    {
        public CustomPrefabConfig CustomPrefab = new CustomPrefabConfig();
        public bool Desired = true;
        public string Type = ""; // "Outpost", "Bandit Town", etc.
        public bool Blocked = false;
        public bool AllowedToSetBiomes = true;
        public List<BiomePreferenceItem> BiomePreferences = new List<BiomePreferenceItem>();
    }
    
    [Serializable]
    public class MonumentConfig
    {
        public string Type = "";
        public bool Blocked = false;
        public bool AllowedToSetBiomes = true;
        public List<BiomePreferenceItem> BiomePreferences = new List<BiomePreferenceItem>();
        public bool Desired = true; // For large monuments
    }
    
    [Serializable]
    public class BiomePreferenceConfig
    {
        public bool Enabled = false;
        public string Biome = ""; // "Desert", "Snow", etc.
    }
    
    [Serializable]
    public class PositionConfig
    {
        public bool Enabled = true;
        public string Alignment = ""; // "Top", "Right", etc.
        public float Position = 0f;
    }
    
    [Serializable]
    public class CustomPrefabConfig
    {
        public bool Enabled = false;
        public string Id = "default";
    }
    
    [Serializable]
    public class BiomePreferenceItem
    {
        public string BiomeType = ""; // "Snow", "Forest", "Tundra", "Desert", "Jungle"
        public string Selection = "NoPreference"; // "Wanted", "NotWanted", "NoPreference"
    }
    
    [Serializable]
    public class WebhookConfig
    {
        public bool Enabled = false;
        public string Url = "";
    }
    
    [Serializable]
    public class MapSettingsConfig
    {
        public bool Enabled = false;
        /// <summary>Override map size (3500-6000). 0 = use server worldsize.</summary>
        public int MapSizeOverride = 0;
        /// <summary>Override save folder (e.g. "server/mymap"). Empty = use server rootFolder.</summary>
        public string SaveFolderOverride = "";
        /// <summary>Override map/save base name (e.g. "mymap"). Empty = use level name + size + seed.</summary>
        public string SaveNameOverride = "";
        /// <summary>Force new seed each startup (random) so map is regenerated.</summary>
        public bool ForceNewMapEachTime = false;
    }
    
    [Serializable]
    public class MapImageConfig
    {
        public bool Enabled = false;
        /// <summary>Folder for map images. Empty = server root (current directory).</summary>
        public string OutputFolder = "";
        /// <summary>When true, save as {size}_{seed}.png for MapVoter compatibility (vs default map_{size}_{seed}.png).</summary>
        public bool MapVoterFormat = false;
        public bool IncludeMonumentNames = true;
        /// <summary>When true, draw grid overlay. Default false (map + monument markers only).</summary>
        public bool IncludeGrid = false;
        public float Scale = 0.75f;
        /// <summary>Ocean margin in pixels (100-500). 150 matches HarmonyCustomGenerator, keeps texture under 4096.</summary>
        public int OceanMargin = 150;
        /// <summary>Font folder. Default maps/images/resources. Place dinprobold.otf, dinpro.otf, PermanentMarker.ttf there.</summary>
        public string FontResourcesPath = "maps/images/resources";
        /// <summary>Monument label font: dinprobold, dinpro, or PermanentMarker. PermanentMarker = HCG-style marker/handwritten; dinprobold = clean sans-serif.</summary>
        public string MonumentFont = "PermanentMarker";
    }
    
    [Serializable]
    public class SwapMonumentsConfig
    {
        public bool Enabled = false;
        /// <summary>When false (default), swap is done in Save Prefix only — one map file written. When true, copy to maps/temp and run swap at DONE (legacy, creates second file).</summary>
        public bool RunPostSaveSwap = false;
        /// <summary>Folder containing custom .map prefabs (e.g. harbor_1.prefab.map). Relative to server root.</summary>
        public string CustomPrefabsFolder = "maps/prefabs";
        /// <summary>If true, save a second map version without swaps (vanilla).</summary>
        public bool SaveBothVersions = false;
        /// <summary>Add this many units to the placement height (Y). Use if your custom monument sinks into terrain (e.g. 1.5 or 2).</summary>
        public float PlacementHeightOffset = 0f;
        /// <summary>When true (default), place using the map origin (0,0,0) so the monument's ground aligns with the compound. When false, the first prefab in the .map is the anchor (can sink if that prefab has Y&gt;0, e.g. pivot at Y=5).</summary>
        public bool UseMapOriginAsPlacementReference = true;
        /// <summary>Move the outpost (and bandit camp) to map center. Required when using custom outpost.prefab.map at center.</summary>
        public bool TrySpawningOutpostInCenter = true;
        /// <summary>When false, bandit town monument is not spawned (compound in center acts as combined outpost/bandit).</summary>
        public bool AllowBanditCamp = false;
        /// <summary>When true and AllowBanditCamp is false, the bandit slot is filled with another monument (e.g. Gas Station, Supermarket) if no monument was relocated there from center.</summary>
        public bool FillBanditSlotWithMonument = true;
        /// <summary>When true, the blocked outpost position (when compound spawns away from center) is used to relocate monuments at center (e.g. Water Treatment Plant) so they don't overlap the center outpost.</summary>
        public bool UseBlockedOutpostSlotForRelocation = true;
        /// <summary>RustEdit often tags monument contents as category &quot;Decor&quot;. When true (default), pasted swap-map rows with category Decor are written as &quot;Monument&quot; so world spawn matches vanilla compound children.</summary>
        public bool NormalizeDecorCategoryToMonumentWhenPastingSwapMap = true;
        /// <summary>When true, after spawn tracking finalizes, attempt a late CreateEntity replay for swapped outpost rows that look like runtime entities (deployables/NPC/casino/etc) so they persist in server.save.</summary>
        public bool EnableLateEntityRecovery = true;
        /// <summary>Seconds after spawn tracking finalize before late entity recovery executes.</summary>
        public float LateEntityRecoveryDelaySeconds = 20f;
    }

    [Serializable]
    public class TerrainConfig
    {
        public IslandConfig IslandConfig = new IslandConfig();
        public MountainConfig MountainConfig = new MountainConfig();
        public TierConfig TierConfig = new TierConfig();
        public BiomeConfig BiomeConfig = new BiomeConfig();
        public bool FlattenShoreAndBay = true;
        public string BiomeAxisAngle = "TopDesertBottomSnow"; // "TopDesertBottomSnow", etc.
        public string LootAxisAngle = "LeftTier0RightTier2"; // "LeftTier0RightTier2", etc.
        
        public static TerrainConfig Default()
        {
            return new TerrainConfig
            {
                IslandConfig = new IslandConfig { Enabled = true, Intensity = 7 },
                MountainConfig = new MountainConfig { ReduceMountains = true },
                TierConfig = new TierConfig
                {
                    Enabled = true,
                    Tier0Percentage = 0.33f,
                    Tier1Percentage = 0.33f,
                    Tier2Percentage = 0.34f
                },
                BiomeConfig = new BiomeConfig
                {
                    Enabled = true,
                    AridPercentage = 0.25f,
                    TemperatePercentage = 0.25f,
                    TundraPercentage = 0.25f,
                    ArcticPercentage = 0.25f,
                    JunglePercentage = 0.5f
                },
                FlattenShoreAndBay = true,
                BiomeAxisAngle = "TopDesertBottomSnow",
                LootAxisAngle = "LeftTier0RightTier2"
            };
        }
    }
    
    [Serializable]
    public class IslandConfig
    {
        public bool Enabled = true;
        public int Intensity = 7; // 0-10
    }
    
    [Serializable]
    public class MountainConfig
    {
        public bool ReduceMountains = true;
    }
    
    [Serializable]
    public class TierConfig
    {
        public bool Enabled = true;
        public float Tier0Percentage = 0.33f;
        public float Tier1Percentage = 0.33f;
        public float Tier2Percentage = 0.34f;
    }
    
    [Serializable]
    public class BiomeConfig
    {
        public bool Enabled = true;
        public float AridPercentage = 0.25f;
        public float TemperatePercentage = 0.25f;
        public float TundraPercentage = 0.25f;
        public float ArcticPercentage = 0.25f;
        public float JunglePercentage = 0.5f;
    }
}
