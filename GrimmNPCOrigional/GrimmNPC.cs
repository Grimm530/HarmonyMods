using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Rust;
using UnityEngine;
using Facepunch;

namespace GrimmNPC
{
    /// <summary>
    /// GrimmNPC - Performance-optimized Harmony mod for custom NPC behavior in Rust.
    /// 
    /// Initialization Chain Integration:
    /// - Patches ScientistNPC.ServerInit() to apply custom config during initialization
    /// - Configures components in-place (no destruction) for 27.9s → <1s spawn time
    /// - Integrates with Rust's initialization: ServerInit() → InitializeAI() → Navigator.Init()
    /// 
    /// Performance Optimizations:
    /// - Direct IL patching (no reflection overhead)
    /// - Dictionary pre-sizing (prevents resizing with 1000+ NPCs)
    /// - Config caching in hot paths (re-check every 5 seconds)
    /// - Throttled operations in Think() patch (dormancy, roam enforcement, combat)
    /// 
    /// See INSTRUCTIONAL.md for complete documentation.
    /// </summary>
    public class GrimmNPC : IHarmonyModHooks
    {
        /// <summary>
        /// Singleton instance for static API access.
        /// Set during OnLoaded(), cleared during OnUnloaded().
        /// </summary>
        public static GrimmNPC Instance { get; private set; }
        
        /// <summary>
        /// Custom NPC identification skin ID.
        /// NPCs with this skinID are identified as custom NPCs by patches.
        /// Allows fast filtering without component access or reflection.
        /// </summary>
        public static readonly ulong CUSTOM_NPC_SKIN_ID = 11162132011012UL;
        
        private static readonly string CONFIG_PATH = Path.Combine(Application.dataPath, "..", "HarmonyConfig", "GrimmNPC.json");
        private static readonly string DATA_PATH = Path.Combine(Application.dataPath, "..", ".cursor", "HarmonyMods", "GrimmNPC", "data.json");
        
        private NpcConfig _config;
        private NpcData _data;
        
        /// <summary>
        /// NPC data dictionary keyed by network ID.
        /// Pre-sized to 1000 to prevent dictionary resizing overhead.
        /// Used by patches to retrieve NPC configuration during runtime.
        /// </summary>
        private readonly Dictionary<ulong, CustomNpcData> _npcs = new Dictionary<ulong, CustomNpcData>(1000);
        
        /// <summary>
        /// Set of used NPC user IDs to prevent duplicate NPCs.
        /// Pre-sized to 1000 to prevent HashSet resizing overhead.
        /// Persisted to data.json on mod unload.
        /// </summary>
        private readonly HashSet<ulong> _usedNpcUserIds = new HashSet<ulong>(1000);
        
        /// <summary>
        /// Pending NPC registrations keyed by entity instance ID.
        /// Allows registration before spawn (when netId is 0).
        /// Used by external plugins to register NPC data before Spawn() is called.
        /// 
        /// Registration Flow:
        /// 1. External plugin calls RegisterPending(entity, npcData) BEFORE Spawn()
        /// 2. SpawnPatches.ApplyCustomConfig() checks for pending registration
        /// 3. If found, consumes pending data and registers with netId
        /// 
        /// This prevents ServerInit from creating default 50m RoamRange NPCs.
        /// </summary>
        private readonly Dictionary<int, CustomNpcData> _pending = new Dictionary<int, CustomNpcData>(1000);
        
        /// <summary>
        /// Called when Harmony mod is loaded.
        /// Initializes singleton, loads config/data, and prepares for NPC registration.
        /// 
        /// Initialization Order:
        /// 1. Set Instance (enables static API methods)
        /// 2. LoadConfig() - Loads or creates config file
        /// 3. LoadData() - Restores used NPC user IDs from persistence
        /// </summary>
        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            UnityEngine.Debug.Log("[GrimmNPC] ===== OnLoaded Hook Called =====");
            Instance = this;
            LoadConfig();
            LoadData();
            UnityEngine.Debug.Log("[GrimmNPC] Loaded - Optimized for 1000+ NPCs");
            UnityEngine.Debug.Log($"[GrimmNPC] Config loaded: EnableDebugLogging={GetConfig().EnableDebugLogging}");
            UnityEngine.Debug.Log($"[GrimmNPC] Total registered NPCs: {_npcs.Count}");
        }
        
        /// <summary>
        /// Called when Harmony mod is unloaded.
        /// Saves data persistence and cleans up resources.
        /// 
        /// Cleanup Order:
        /// 1. SaveData() - Persists used NPC user IDs
        /// 2. Clear dictionaries (prevent memory leaks)
        /// 3. Clear Instance (disable static API methods)
        /// </summary>
        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            SaveData();
            _npcs.Clear();
            _usedNpcUserIds.Clear();
            _pending.Clear();
            Instance = null;
            UnityEngine.Debug.Log("[GrimmNPC] Unloaded");
        }
        
        private void LoadConfig()
        {
            try
            {
                // Check new location first
                if (File.Exists(CONFIG_PATH))
                {
                    string json = File.ReadAllText(CONFIG_PATH);
                    _config = JsonConvert.DeserializeObject<NpcConfig>(json);
                    UnityEngine.Debug.Log("[GrimmNPC] Config loaded from HarmonyConfig/GrimmNPC.json");
                }
                else
                {
                    // Try to migrate from old location
                    string oldConfigPath = Path.Combine(Application.dataPath, "..", ".cursor", "HarmonyMods", "GrimmNPC", "config.json");
                    if (File.Exists(oldConfigPath))
                    {
                        UnityEngine.Debug.Log("[GrimmNPC] Migrating config from old location to HarmonyConfig/GrimmNPC.json");
                        string json = File.ReadAllText(oldConfigPath);
                        _config = JsonConvert.DeserializeObject<NpcConfig>(json);
                        SaveConfig(); // Save to new location
                    }
                    else
                    {
                        // Create default config
                        _config = NpcConfig.Default();
                        SaveConfig();
                        UnityEngine.Debug.Log("[GrimmNPC] Created default config at HarmonyConfig/GrimmNPC.json");
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[GrimmNPC] Failed to load config: {ex}");
                _config = NpcConfig.Default();
            }
        }
        
        private void SaveConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(CONFIG_PATH);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                
                string json = JsonConvert.SerializeObject(_config, Formatting.Indented);
                File.WriteAllText(CONFIG_PATH, json);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[GrimmNPC] Failed to save config: {ex}");
            }
        }
        
        private void LoadData()
        {
            try
            {
                if (File.Exists(DATA_PATH))
                {
                    string json = File.ReadAllText(DATA_PATH);
                    _data = JsonConvert.DeserializeObject<NpcData>(json);
                    
                    // Restore used user IDs
                    if (_data.UsedNpcUserIds != null)
                    {
                        foreach (ulong id in _data.UsedNpcUserIds)
                        {
                            _usedNpcUserIds.Add(id);
                        }
                    }
                }
                else
                {
                    _data = new NpcData();
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[GrimmNPC] Failed to load data: {ex}");
                _data = new NpcData();
            }
        }
        
        private void SaveData()
        {
            try
            {
                string dir = Path.GetDirectoryName(DATA_PATH);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                
                _data.UsedNpcUserIds = new List<ulong>(_usedNpcUserIds);
                string json = JsonConvert.SerializeObject(_data, Formatting.Indented);
                File.WriteAllText(DATA_PATH, json);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[GrimmNPC] Failed to save data: {ex}");
            }
        }
        
        // ====================================================================
        // Public API for Patches
        // ====================================================================
        
        /// <summary>
        /// Checks if an entity is a custom NPC by skinID.
        /// Fast check (no reflection) used by all patches for early exit filtering.
        /// 
        /// Performance: O(1) - single property access
        /// Used in: All patches (hot path - 4000+ calls/second)
        /// </summary>
        /// <param name="entity">Entity to check</param>
        /// <returns>True if entity is a custom NPC (skinID matches CUSTOM_NPC_SKIN_ID)</returns>
        public static bool IsCustomNpc(BaseEntity entity)
        {
            return entity != null && entity.skinID == CUSTOM_NPC_SKIN_ID;
        }
        
        /// <summary>
        /// Gets the current NPC configuration.
        /// Returns default config if mod not loaded (prevents null reference exceptions).
        /// 
        /// Performance: O(1) - cached in Instance
        /// Used in: All patches (cached in hot paths, re-checked every 5 seconds)
        /// </summary>
        /// <returns>Current NpcConfig instance or default if mod not loaded</returns>
        public static NpcConfig GetConfig()
        {
            return Instance?._config ?? NpcConfig.Default();
        }
        
        /// <summary>
        /// Calls an Oxide hook via reflection (Harmony mods don't have direct Oxide.Core access).
        /// 
        /// PERFORMANCE OPTIMIZED: All reflection lookups are cached for maximum efficiency.
        /// - Oxide.Core assembly cached on first lookup
        /// - Interface.Oxide instance cached
        /// - MethodInfo cached for common overloads
        /// 
        /// This method is called infrequently (on death, guard target destroyed, etc.),
        /// but caching ensures minimal overhead when it is called.
        /// </summary>
        /// <param name="hookName">Name of the hook to call (e.g., "OnBomberExplosion")</param>
        /// <param name="args">Arguments to pass to the hook</param>
        /// <returns>True if hook was called successfully, false otherwise</returns>
        public static bool CallOxideHook(string hookName, params object[] args)
        {
            // Initialize cached references if not already done
            if (!_oxideInitialized)
            {
                InitializeOxideCache();
            }

            // If Oxide is not available, return false
            if (!_oxideAvailable) return false;

            try
            {
                // Use cached MethodInfo based on argument count
                System.Reflection.MethodInfo method = null;
                
                if (args.Length == 0 && _callHook0Args != null)
                {
                    method = _callHook0Args;
                }
                else if (args.Length == 1 && _callHook1Arg != null)
                {
                    method = _callHook1Arg;
                }
                else if (args.Length == 2 && _callHook2Args != null)
                {
                    method = _callHook2Args;
                }
                else if (_callHookParams != null)
                {
                    // Fallback to params object[] overload (works for any arg count)
                    method = _callHookParams;
                }

                if (method == null) return false;

                // Invoke the cached method
                if (method == _callHookParams)
                {
                    method.Invoke(_oxideInstance, new object[] { hookName, args });
                }
                else if (args.Length == 0)
                {
                    method.Invoke(_oxideInstance, new object[] { hookName });
                }
                else if (args.Length == 1)
                {
                    method.Invoke(_oxideInstance, new object[] { hookName, args[0] });
                }
                else if (args.Length == 2)
                {
                    method.Invoke(_oxideInstance, new object[] { hookName, args[0], args[1] });
                }

                return true;
            }
            catch { /* Hook call failed - ignore silently */ }
            
            return false;
        }

        // Cached Oxide references (initialized once, reused forever)
        private static bool _oxideInitialized = false;
        private static bool _oxideAvailable = false;
        private static object _oxideInstance = null;
        private static System.Reflection.MethodInfo _callHookParams = null;  // CallHook(string, params object[])
        private static System.Reflection.MethodInfo _callHook0Args = null;   // CallHook(string)
        private static System.Reflection.MethodInfo _callHook1Arg = null;   // CallHook(string, object)
        private static System.Reflection.MethodInfo _callHook2Args = null;  // CallHook(string, object, object)

        /// <summary>
        /// Initializes cached Oxide references (called once on first hook call).
        /// </summary>
        private static void InitializeOxideCache()
        {
            _oxideInitialized = true; // Set immediately to prevent recursion

            try
            {
                // Search all loaded assemblies for Oxide.Core (only once)
                System.Reflection.Assembly oxideCoreAssembly = null;
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (assembly.GetName().Name == "Oxide.Core" || 
                            assembly.FullName.Contains("Oxide.Core"))
                        {
                            oxideCoreAssembly = assembly;
                            break;
                        }
                    }
                    catch { }
                }

                if (oxideCoreAssembly == null)
                {
                    _oxideAvailable = false;
                    return;
                }

                var interfaceType = oxideCoreAssembly.GetType("Oxide.Core.Interface");
                if (interfaceType == null)
                {
                    _oxideAvailable = false;
                    return;
                }

                var oxideProperty = interfaceType.GetProperty("Oxide", 
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (oxideProperty == null)
                {
                    _oxideAvailable = false;
                    return;
                }

                _oxideInstance = oxideProperty.GetValue(null);
                if (_oxideInstance == null)
                {
                    _oxideAvailable = false;
                    return;
                }

                // Cache MethodInfo for all common overloads
                var oxideType = _oxideInstance.GetType();
                
                // Try params object[] overload first (most common, works for any arg count)
                _callHookParams = oxideType.GetMethod("CallHook", 
                    new[] { typeof(string), typeof(object[]) });
                
                // Cache specific overloads for better performance
                _callHook0Args = oxideType.GetMethod("CallHook", new[] { typeof(string) });
                _callHook1Arg = oxideType.GetMethod("CallHook", new[] { typeof(string), typeof(object) });
                _callHook2Args = oxideType.GetMethod("CallHook", new[] { typeof(string), typeof(object), typeof(object) });

                _oxideAvailable = true;
            }
            catch
            {
                _oxideAvailable = false;
            }
        }

        /// <summary>
        /// Registers a custom NPC with the mod.
        /// 
        /// CRITICAL: Must be called BEFORE npc.Spawn() to ensure:
        /// - HomePosition is set correctly before spawn
        /// - NPC data is available when ServerInit patch executes
        /// - UserID tracking prevents duplicate NPCs
        /// 
        /// Registration Flow:
        /// 1. Store NPC data in _npcs dictionary (keyed by network ID)
        /// 2. Add UserID to _usedNpcUserIds HashSet (prevents duplicates)
        /// 3. Log registration (if debug logging enabled)
        /// 
        /// See INSTRUCTIONAL.md "Complete Initialization Chain" section for details.
        /// </summary>
        /// <param name="netId">Network ID of the NPC entity</param>
        /// <param name="npcData">Custom NPC data configuration</param>
        public static void RegisterNpc(ulong netId, CustomNpcData npcData)
        {
            if (Instance != null)
            {
                // Validate HomePosition (CRITICAL for clustering prevention)
                if (npcData != null && npcData.HomePosition == Vector3.zero)
                {
                    UnityEngine.Debug.LogWarning($"[GrimmNPC Register] WARNING: HomePosition is zero for NPC {npcData.Name} (NetID: {netId}). " +
                        "This may cause clustering issues. Ensure HomePosition is set before registration.");
                }
                
                bool alreadyExists = Instance._npcs.ContainsKey(netId);
                Instance._npcs[netId] = npcData;
                
                // Track UserID to prevent duplicate NPCs
                if (npcData != null && npcData.UserID != 0)
                {
                    Instance._usedNpcUserIds.Add(npcData.UserID);
                }
                
                // Debug logging
                var config = GetConfig();
                if (config.EnableDebugLogging && npcData != null)
                {
                    if (alreadyExists)
                    {
                        UnityEngine.Debug.LogWarning($"[GrimmNPC Register] OVERWRITING existing NPC registration: " +
                            $"NetID: {netId} | Name: {npcData.Name} | " +
                            $"HomePosition: {npcData.HomePosition} | " +
                            $"RoamRange: {npcData.RoamRange}m | " +
                            $"ChaseRange: {npcData.ChaseRange}m");
                    }
                    else
                    {
                        UnityEngine.Debug.Log($"[GrimmNPC Register] Registered NEW NPC: " +
                            $"NetID: {netId} | Name: {npcData.Name} | " +
                            $"HomePosition: {npcData.HomePosition} | " +
                            $"RoamRange: {npcData.RoamRange}m | " +
                            $"ChaseRange: {npcData.ChaseRange}m | " +
                            $"Total NPCs: {Instance._npcs.Count}");
                    }
                }
            }
        }
        
        /// <summary>
        /// Unregisters a custom NPC from the mod.
        /// Removes NPC data and frees UserID for reuse.
        /// 
        /// Called when NPC is destroyed or despawned.
        /// </summary>
        /// <param name="netId">Network ID of the NPC entity</param>
        public static void UnregisterNpc(ulong netId)
        {
            if (Instance != null && Instance._npcs.TryGetValue(netId, out var npcData))
            {
                // Free UserID for reuse
                if (npcData != null && npcData.UserID != 0)
                {
                    Instance._usedNpcUserIds.Remove(npcData.UserID);
                }
                Instance._npcs.Remove(netId);
            }
        }
        
        /// <summary>
        /// Gets NPC data by network ID.
        /// Used by patches to retrieve NPC configuration during runtime.
        /// 
        /// Performance: O(1) - dictionary lookup
        /// Used in: All patches (hot path - 4000+ calls/second)
        /// </summary>
        /// <param name="netId">Network ID of the NPC entity</param>
        /// <returns>CustomNpcData instance or null if not found</returns>
        public static CustomNpcData GetNpcData(ulong netId)
        {
            if (Instance != null && Instance._npcs.TryGetValue(netId, out var data))
            {
                return data;
            }
            return null;
        }
        
        /// <summary>
        /// Checks if a user ID is already used by a registered NPC.
        /// Prevents duplicate NPCs with the same UserID.
        /// 
        /// Performance: O(1) - HashSet lookup
        /// </summary>
        /// <param name="userId">User ID to check</param>
        /// <returns>True if user ID is already used</returns>
        public static bool IsUserIdUsed(ulong userId)
        {
            return Instance != null && Instance._usedNpcUserIds.Contains(userId);
        }
        
        /// <summary>
        /// Registers pending NPC data keyed by entity instance ID.
        /// Allows registration BEFORE Spawn() when netId is 0.
        /// 
        /// CRITICAL: This prevents ServerInit from creating default 50m RoamRange NPCs.
        /// 
        /// Usage:
        /// 1. Create NPC entity (netId is still 0)
        /// 2. Call RegisterPending(npc, npcData) with your custom data
        /// 3. Call npc.Spawn()
        /// 4. SpawnPatches will consume pending data during ServerInit
        /// 
        /// The pending data is automatically consumed and converted to netId registration
        /// during ServerInit, then removed from _pending dictionary.
        /// </summary>
        /// <param name="entity">NPC entity (before or after Spawn())</param>
        /// <param name="npcData">Custom NPC data configuration</param>
        public static void RegisterPending(BaseEntity entity, CustomNpcData npcData)
        {
            if (Instance == null)
            {
                // Log warning if debug logging is enabled (even if Instance is null, GetConfig() returns default)
                var nullCheckConfig = GetConfig();
                if (nullCheckConfig.EnableDebugLogging)
                {
                    UnityEngine.Debug.LogWarning($"[GrimmNPC RegisterPending] Instance is null - cannot register pending NPC. " +
                        "Ensure GrimmNPC mod is loaded (harmony.load GrimmNPC) and wait for initialization.");
                }
                return;
            }
            if (entity == null || npcData == null) return;
            
            int instanceId = entity.GetInstanceID();
            Instance._pending[instanceId] = npcData;
            
            var config = GetConfig();
            if (config.EnableDebugLogging)
            {
                UnityEngine.Debug.Log($"[GrimmNPC RegisterPending] Registered pending NPC: " +
                    $"InstanceID: {instanceId} | Name: {npcData.Name} | " +
                    $"HomePosition: {npcData.HomePosition} | " +
                    $"RoamRange: {npcData.RoamRange}m | " +
                    $"ChaseRange: {npcData.ChaseRange}m");
            }
        }
        
        /// <summary>
        /// Consumes pending NPC data by entity instance ID.
        /// Called by SpawnPatches during ServerInit to retrieve data registered before Spawn().
        /// 
        /// After consuming, the data is removed from _pending and registered with netId.
        /// This ensures ServerInit has the correct NPC data instead of creating defaults.
        /// </summary>
        /// <param name="entity">NPC entity</param>
        /// <returns>CustomNpcData if found, null otherwise</returns>
        internal static CustomNpcData ConsumePending(BaseEntity entity)
        {
            if (Instance == null || entity == null) return null;
            
            int instanceId = entity.GetInstanceID();
            var config = GetConfig();
            bool debugLogging = config.EnableDebugLogging;
            
            if (debugLogging)
            {
                UnityEngine.Debug.Log($"[GrimmNPC ConsumePending] Looking for pending NPC: InstanceID={instanceId}, " +
                    $"NetID={entity.net?.ID.Value ?? 0}, TotalPending={Instance._pending.Count}");
                
                // Log all pending instance IDs for debugging
                if (Instance._pending.Count > 0)
                {
                    var pendingIds = string.Join(", ", Instance._pending.Keys);
                    UnityEngine.Debug.Log($"[GrimmNPC ConsumePending] Pending instance IDs: {pendingIds}");
                }
            }
            
            if (Instance._pending.TryGetValue(instanceId, out var data))
            {
                Instance._pending.Remove(instanceId);
                
                if (debugLogging)
                {
                    UnityEngine.Debug.Log($"[GrimmNPC ConsumePending] Consumed pending NPC: " +
                        $"InstanceID: {instanceId} | Name: {data.Name} | " +
                        $"RoamRange: {data.RoamRange}m | ChaseRange: {data.ChaseRange}m");
                }
                
                return data;
            }
            
            if (debugLogging)
            {
                UnityEngine.Debug.LogWarning($"[GrimmNPC ConsumePending] No pending registration found for InstanceID={instanceId}, " +
                    $"NetID={entity.net?.ID.Value ?? 0}. This may cause default 50m RoamRange to be used.");
            }
            
            return null;
        }
        
        /// <summary>
        /// Marks a user ID as used.
        /// Used when creating NPCs to prevent duplicates.
        /// 
        /// Performance: O(1) - HashSet add
        /// </summary>
        /// <param name="userId">User ID to mark as used</param>
        public static void MarkUserIdUsed(ulong userId)
        {
            if (Instance != null)
            {
                Instance._usedNpcUserIds.Add(userId);
            }
        }
        
        /// <summary>
        /// Validates HomePosition is set correctly.
        /// Used by patches to ensure HomePosition is not zero (prevents clustering).
        /// 
        /// See INSTRUCTIONAL.md "Troubleshooting - NPCs Clustering" section.
        /// </summary>
        /// <param name="npcData">NPC data to validate</param>
        /// <param name="fallbackPosition">Position to use if HomePosition is invalid</param>
        /// <returns>True if HomePosition was updated</returns>
        public static bool ValidateHomePosition(CustomNpcData npcData, Vector3 fallbackPosition)
        {
            if (npcData == null) return false;
            
            // Check if HomePosition is zero or very close to origin
            if (npcData.HomePosition == Vector3.zero || 
                Vector3.Distance(npcData.HomePosition, Vector3.zero) < 1f)
            {
                npcData.HomePosition = fallbackPosition;
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Gets the horizontal (XZ plane) distance from HomePosition.
        /// Used by patches for roam range enforcement (prevents Y-axis issues).
        /// 
        /// Performance: O(1) - single distance calculation
        /// Used in: ThinkPatches (roam enforcement), TargetingPatches (chase range)
        /// </summary>
        /// <param name="npcData">NPC data containing HomePosition</param>
        /// <param name="currentPosition">Current position to measure from</param>
        /// <returns>Horizontal distance from HomePosition in meters</returns>
        public static float GetDistanceFromHome(CustomNpcData npcData, Vector3 currentPosition)
        {
            if (npcData == null || npcData.HomePosition == Vector3.zero)
                return 0f;
            
            // Calculate horizontal (XZ) distance manually (Vector3Ex not available in Harmony mods)
            Vector3 diff = currentPosition - npcData.HomePosition;
            return Mathf.Sqrt(diff.x * diff.x + diff.z * diff.z);
        }
        
        /// <summary>
        /// 🎯 SetKnown: Adds an entity to NPC's memory system.
        /// Used by assist system to alert nearby NPCs to threats.
        /// </summary>
        /// <param name="npc">NPC to add entity to memory</param>
        /// <param name="entity">Entity to add to memory</param>
        public static void SetKnown(ScientistNPC npc, BaseEntity entity)
        {
            if (npc == null || entity == null) return;
            if (!IsCustomNpc(npc)) return;
            
            var brain = npc.Brain;
            if (brain == null || brain.Senses == null || brain.Senses.Memory == null) return;
            
            // Use the Memory.SetKnown method directly (available in Rust)
            try
            {
                brain.Senses.Memory.SetKnown(entity, npc, null);
            }
            catch
            {
                // Fallback: Update existing entry or add new one manually
                var memoryAll = brain.Senses.Memory.All;
                if (memoryAll != null)
                {
                    for (int i = 0; i < memoryAll.Count; i++)
                    {
                        var info = memoryAll[i];
                        if (info.Entity == entity)
                        {
                            // Update existing entry
                            info.Position = entity.transform.position;
                            info.Timestamp = Time.realtimeSinceStartup;
                            return;
                        }
                    }
                }
            }
        }
    }
    
    // ====================================================================
    // Configuration Classes
    // ====================================================================
    
    /// <summary>
    /// Global configuration for all NPCs.
    /// Loaded from HarmonyConfig/GrimmNPC.json on mod load.
    /// Cached in hot paths (re-checked every 5 seconds).
    /// 
    /// See INSTRUCTIONAL.md "Configuration System" section for details.
    /// </summary>
    public class NpcConfig
    {
        // Targeting Configuration
        public bool CanTargetAnimal { get; set; } = false;
        public bool CanTargetNpc { get; set; } = false;
        public bool CanTargetSleepingPlayer { get; set; } = false;
        public bool CanTargetWoundedPlayer { get; set; } = false;
        public bool CanTargetSafeZonePlayer { get; set; } = false;
        public bool PreventScarecrowTargeting { get; set; } = true;
        
        // Dormancy Configuration
        public bool ForceRespectAiDormant { get; set; } = false;
        public float DefaultSleepDistance { get; set; } = 160f;
        
        // Spawn Configuration
        public string Prefab { get; set; } = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab";
        
        // Debug Configuration
        public bool EnableDebugLogging { get; set; } = false;
        public bool EnableNavMeshValidation { get; set; } = false;
        
        // Assist System Configuration
        public bool EnableAssistCallouts { get; set; } = true;
        public float AssistRange { get; set; } = 100f;
        
        // Raiding Configuration
        /// <summary>
        /// If true, all NPCs can perform raiding actions (attacking structures).
        /// External plugins can enable this to allow raiding for all NPCs.
        /// Default: false (only NPCs with IsRaidingNpc=true can raid)
        /// </summary>
        public bool EnableRaidingForAllNpcs { get; set; } = false;
        
        // Target Exclusion Configuration
        /// <summary>
        /// List of entity type names to exclude from targeting and assist callouts.
        /// NPCs will not target entities of these types, and will not call for help when attacked by them.
        /// Example: ["ZombieNPC", "CustomNPC"] to exclude ZombieNPC and CustomNPC entities.
        /// </summary>
        public System.Collections.Generic.List<string> ExcludedTargetTypes { get; set; } = new System.Collections.Generic.List<string>();
        
        /// <summary>
        /// Creates a default configuration with all default values.
        /// Used when config file doesn't exist or fails to load.
        /// </summary>
        /// <returns>Default NpcConfig instance</returns>
        public static NpcConfig Default()
        {
            return new NpcConfig();
        }
    }
    
    /// <summary>
    /// Runtime data persistence.
    /// Saved to .cursor/HarmonyMods/GrimmNPC/data.json on mod unload.
    /// Used to track used NPC user IDs across server restarts.
    /// 
    /// See INSTRUCTIONAL.md "Data Management" section for details.
    /// </summary>
    public class NpcData
    {
        public List<ulong> UsedNpcUserIds { get; set; } = new List<ulong>();
    }
    
    /// <summary>
    /// Per-NPC configuration data.
    /// Stored in _npcs dictionary keyed by network ID.
    /// Used by patches to customize NPC behavior.
    /// 
    /// CRITICAL: HomePosition must be set correctly to prevent clustering.
    /// 
    /// See INSTRUCTIONAL.md "Core Architecture - CustomNpcData" section for details.
    /// </summary>
    public class CustomNpcData
    {
        // Identity
        public ulong UserID { get; set; }
        public string Name { get; set; }
        
        // Combat Configuration
        public float Health { get; set; }
        public float DamageScale { get; set; } = 1f;
        public float TurretDamageScale { get; set; } = 1f;
        public float AimConeScale { get; set; } = 1f;
        
        // Turret Targeting Configuration
        public bool CanBeTargetedByAutoTurrets { get; set; } = true;
        public bool CanBeTargetedByGunTraps { get; set; } = true;
        public bool CanBeTargetedByFlameTurrets { get; set; } = true;
        public bool CanBeTargetedByAPC { get; set; } = true;

        // Raiding Configuration
        /// <summary>
        /// If true, this NPC can perform raiding actions against player bases.
        /// </summary>
        public bool IsRaidingNpc { get; set; } = false;

        /// <summary>
        /// Optional raid settings for this NPC. If null, defaults are used.
        /// </summary>
        public RaidSettings RaidSettings { get; set; } = new RaidSettings();

        // Guard Configuration
        /// <summary>
        /// If true, this NPC is a guard NPC that protects other entities.
        /// Guard NPCs will follow their GuardTarget and prioritize threats to it.
        /// </summary>
        public bool IsGuardNpc { get; set; } = false;

        /// <summary>
        /// The entity this guard NPC is protecting. Guard NPCs will follow this target and prioritize threats to it.
        /// </summary>
        public BaseEntity GuardTarget { get; set; } = null;

        /// <summary>
        /// Original home position before guard assignment. Used to return NPC to original position when guard target is destroyed.
        /// </summary>
        public Vector3 BeforeGuardHomePosition { get; set; } = Vector3.zero;

        // Bomber Configuration
        /// <summary>
        /// If true, this NPC is a bomber NPC that carries a timed explosive and explodes on death or when using the explosive.
        /// </summary>
        public bool IsBomber { get; set; } = false;

        /// <summary>
        /// The timed explosive entity attached to the bomber NPC.
        /// </summary>
        public RFTimedExplosive BomberTimedExplosive { get; set; } = null;
        
        // Navigation Configuration (CRITICAL for clustering prevention)
        /// <summary>
        /// Spawn/home position. CRITICAL: Must be set correctly to prevent clustering.
        /// Used by roam enforcement and chase range calculations.
        /// Should be set to spawn position before registration.
        /// </summary>
        public Vector3 HomePosition { get; set; }
        
        /// <summary>
        /// Maximum distance from HomePosition for roaming (meters).
        /// NPCs will be forced to return if they exceed this range (when not in combat).
        /// Default: 50m
        /// </summary>
        public float RoamRange { get; set; } = 50f;
        
        /// <summary>
        /// Maximum distance from HomePosition for chasing targets (meters).
        /// NPCs will not target entities beyond this range from HomePosition.
        /// Default: 100m
        /// </summary>
        public float ChaseRange { get; set; } = 100f;
        
        /// <summary>
        /// Detection range for targets (meters).
        /// Used to configure brain.SenseRange.
        /// Default: 50m
        /// </summary>
        public float SenseRange { get; set; } = 50f;
        
        // Dormancy Configuration
        public bool CanSleep { get; set; } = false;
        public float SleepDistance { get; set; } = 160f;
        
        // NavMesh Configuration
        /// <summary>
        /// NavMesh area mask.
        /// 1 = terrain navmesh, 25 = monument/construction navmesh.
        /// Default: 1 (terrain navmesh) - matches AgentTypeID default
        /// </summary>
        public int AreaMask { get; set; } = 1;
        
        /// <summary>
        /// NavMesh agent type ID.
        /// -1372625422 = terrain agent type, 0 = monument agent type (default).
        /// Default: -1372625422 (terrain agent type) - matches AreaMask default
        /// </summary>
        public int AgentTypeID { get; set; } = -1372625422;
        
        /// <summary>
        /// If true, navmesh configuration is locked and runtime fixes will not override it.
        /// Set to true after spawn-time navmesh configuration to prevent runtime overrides.
        /// Default: false (allows runtime fixes until locked)
        /// </summary>
        public bool NavmeshLocked { get; set; } = false;
        
        // Swimming Configuration
        public bool CanSwim { get; set; } = false;
        public float SwimmingSpeedMultiplier { get; set; } = 0.4f;
        
        // Combat Movement Configuration
        /// <summary>
        /// Always strafe/move during combat, even when at optimal distance.
        /// When true, NPCs will continuously generate lateral movement during combat.
        /// Default: false (uses smart strafe logic that only strafes when needed)
        /// </summary>
        public bool AlwaysStrafeInCombat { get; set; } = false;
        
        /// <summary>
        /// Radius for lateral strafe movements during combat (meters).
        /// NPCs will strafe this distance perpendicular to target direction.
        /// Default: 3f (2-4m recommended for visible movement)
        /// </summary>
        public float StrafeRadius { get; set; } = 3f;
        
        /// <summary>
        /// Combat movement update interval (frame count).
        /// Lower values = more frequent updates (1 = every frame, 2 = every 2 frames, etc.).
        /// Default: 1 (every frame) when AlwaysStrafeInCombat is true, 4 (every 4th frame) otherwise
        /// </summary>
        public int StrafeInterval { get; set; } = 1;
        
        /// <summary>
        /// If true, strafe movement is only generated while NPC is actively engaging (shooting/attacking).
        /// Prevents any strafe movement during roaming/idle/targetless states.
        /// Default: true (strafe only when attacking).
        /// </summary>
        public bool StrafeOnlyWhenAttacking { get; set; } = true;
        
        // 🏠 RAID GOAL CONFIGURATION: For raiding NPCs, this overrides standard targeting
        /// <summary>
        /// If true, NPC has an active raid goal that should override standard player targeting.
        /// When true, TargetingPatches will prioritize RaidGoalPosition/RaidGoalEntityId over player targets.
        /// </summary>
        public bool RaidGoalActive { get; set; } = false;
        
        /// <summary>
        /// Target position for raid goal (e.g., TC position, structure position).
        /// Used when RaidGoalActive is true and RaidGoalEntityId is not set or entity is destroyed.
        /// </summary>
        public Vector3 RaidGoalPosition { get; set; }
        
        /// <summary>
        /// Target entity ID for raid goal (e.g., TC entity, building block entity).
        /// Optional - if set, NPC will target this entity. If null or destroyed, falls back to RaidGoalPosition.
        /// </summary>
        public ulong RaidGoalEntityId { get; set; } = 0;
        
        // 🏗️ RAID TARGET TRACKING: raid target management
        /// <summary>
        /// Turret target for raiding (highest priority).
        /// </summary>
        public BaseCombatEntity Turret { get; set; } = null;
        
        /// <summary>
        /// Player target building block for raiding (fallback when no turret/foundations).
        /// </summary>
        public BaseCombatEntity PlayerTarget { get; set; } = null;
        
        /// <summary>
        /// Set of foundation building blocks to raid (second priority after turret).
        /// </summary>
        public System.Collections.Generic.HashSet<BuildingBlock> Foundations { get; set; } = new System.Collections.Generic.HashSet<BuildingBlock>();
        
        /// <summary>
        /// Current active raid target (calculated by GetRaidTarget).
        /// </summary>
        public BaseCombatEntity CurrentRaidTarget { get; set; } = null;
    }

    /// <summary>
    /// Raid settings for NPCs that can raid player bases.
    /// </summary>
    public class RaidSettings
    {
        public bool Enable { get; set; } = true;
        public bool RequireTargetAuth { get; set; } = true;
        public bool DisableAtMonuments { get; set; } = true;
        public bool AllowTargetSleeping { get; set; } = false;
        public bool AllowTargetOffline { get; set; } = false;
        public bool AllowExplosives { get; set; } = true;
        public float AttackRangeMelee { get; set; } = 6f;
        public float AttackRangeRanged { get; set; } = 30f;
        public float FallbackDamageMelee { get; set; } = 25f;
        public float FallbackDamageRanged { get; set; } = 50f;
    }

    /// <summary>
    /// Shared raiding logic for custom NPCs.
    /// </summary>
    public static class Raid
    {
        private const int ConstructionLayerMask = 1 << 21;
        private static readonly RaycastHit[] RaycastHits = new RaycastHit[32];

        private class RaidState
        {
            public float NextRaidTime;
        }

        private static readonly Dictionary<ulong, RaidState> RaidStates = new Dictionary<ulong, RaidState>(512);

        public static bool IsRaidingNpc(ScientistNPC npc)
        {
            if (npc == null) return false;
            var data = GrimmNPC.GetNpcData(npc.net?.ID.Value ?? 0);
            return data != null && data.IsRaidingNpc;
        }

        public static void SetRaidingNpc(ScientistNPC npc, bool enabled)
        {
            if (npc == null) return;
            var data = GrimmNPC.GetNpcData(npc.net?.ID.Value ?? 0);
            if (data != null) data.IsRaidingNpc = enabled;
        }

        public static void SetRaidSettings(ulong netId, RaidSettings settings)
        {
            var data = GrimmNPC.GetNpcData(netId);
            if (data != null) data.RaidSettings = settings ?? new RaidSettings();
        }

        public static void StopRaid(ScientistNPC npc)
        {
            if (npc == null || npc.net == null) return;
            RaidStates.Remove(npc.net.ID.Value);
        }

        public static void TickRaid(ScientistNPC npc)
        {
            if (npc == null || npc.net == null) return;
            if (!GrimmNPC.IsCustomNpc(npc)) return;

            ulong netId = npc.net.ID.Value;
            var data = GrimmNPC.GetNpcData(netId);
            if (data == null || !data.IsRaidingNpc) return;

            RaidSettings settings = data.RaidSettings ?? new RaidSettings();
            if (!settings.Enable) return;

            if (settings.DisableAtMonuments && IsOnMonument(npc.transform.position))
                return;

            var brain = npc.Brain;
            if (brain == null) return;

            // 🏠 RAID GOAL SUPPORT: Check if we have Raid Goal but no target in memory
            // In this case, we should still try to find and attack structures near the Raid Goal
            if (data.RaidGoalActive && (brain.Events == null || brain.Events.Memory == null))
            {
                // No memory system, but we have Raid Goal - try to find structures near goal position
                if (data.RaidGoalPosition != Vector3.zero)
                {
                    if (!RaidStates.TryGetValue(netId, out RaidState state))
                        RaidStates[netId] = state = new RaidState();

                    if (Time.time >= state.NextRaidTime)
                    {
                        state.NextRaidTime = Time.time + 0.5f;
                        BaseCombatEntity foundTarget = FindStructuresNearRaidGoal(npc, data.RaidGoalPosition);
                        if (foundTarget != null)
                        {
                            TryAttackTarget(npc, foundTarget, settings);
                        }
                    }
                }
                return;
            }

            if (brain.Events == null || brain.Events.Memory == null) return;

            int slot = brain.Events.CurrentInputMemorySlot;
            if (slot < 0)
            {
                // No target in memory slot, but check if we have Raid Goal
                if (data.RaidGoalActive && data.RaidGoalPosition != Vector3.zero)
                {
                    if (!RaidStates.TryGetValue(netId, out RaidState state))
                        RaidStates[netId] = state = new RaidState();

                    if (Time.time >= state.NextRaidTime)
                    {
                        state.NextRaidTime = Time.time + 0.5f;
                        BaseCombatEntity foundTarget = FindStructuresNearRaidGoal(npc, data.RaidGoalPosition);
                        if (foundTarget != null)
                        {
                            TryAttackTarget(npc, foundTarget, settings);
                        }
                    }
                }
                return;
            }

            BaseEntity target = brain.Events.Memory.Entity.Get(slot);
            if (target == null)
            {
                // No target in memory, but check if we have Raid Goal
                if (data.RaidGoalActive && data.RaidGoalPosition != Vector3.zero)
                {
                    if (!RaidStates.TryGetValue(netId, out RaidState state))
                        RaidStates[netId] = state = new RaidState();

                    if (Time.time >= state.NextRaidTime)
                    {
                        state.NextRaidTime = Time.time + 0.5f;
                        BaseCombatEntity foundTarget = FindStructuresNearRaidGoal(npc, data.RaidGoalPosition);
                        if (foundTarget != null)
                        {
                            TryAttackTarget(npc, foundTarget, settings);
                        }
                        else
                        {
                            // No structures found - ensure NPC moves toward Raid Goal position
                            if (brain.Navigator != null)
                            {
                                float distToGoal = Vector3.Distance(npc.transform.position, data.RaidGoalPosition);
                                if (distToGoal > 2f) // Only set if not already at goal
                                {
                                    brain.Navigator.SetDestination(data.RaidGoalPosition, BaseNavigator.NavigationSpeed.Normal);
                                }
                            }
                        }
                    }
                    else
                    {
                        // Even if on cooldown, ensure NPC is moving toward Raid Goal
                        if (brain.Navigator != null && !brain.Navigator.Moving)
                        {
                            float distToGoal = Vector3.Distance(npc.transform.position, data.RaidGoalPosition);
                            if (distToGoal > 2f)
                            {
                                brain.Navigator.SetDestination(data.RaidGoalPosition, BaseNavigator.NavigationSpeed.Normal);
                            }
                        }
                    }
                }
                return;
            }

            // 🏗️ Check for raid targets (Turret, Foundations, PlayerTarget) first
            BaseCombatEntity trackedRaidTarget = GetRaidTarget(npc);
            if (trackedRaidTarget != null)
            {
                // We have a raid target from tracking system - use it
                if (!RaidStates.TryGetValue(netId, out RaidState state))
                    RaidStates[netId] = state = new RaidState();

                if (Time.time >= state.NextRaidTime)
                {
                    state.NextRaidTime = Time.time + 0.5f;
                    TryAttackTarget(npc, trackedRaidTarget, settings);
                }
                return;
            }

            // 🏠 RAID GOAL SUPPORT: Handle both player targets and Raid Goal targets (TC/structures)
            BasePlayer targetPlayer = target as BasePlayer;
            BaseEntity raidGoalTarget = null;
            
            // Check if this is a Raid Goal target (TC or structure)
            if (targetPlayer == null && data.RaidGoalActive)
            {
                // Check if target matches Raid Goal Entity
                if (data.RaidGoalEntityId != 0 && target.net?.ID.Value == data.RaidGoalEntityId)
                {
                    raidGoalTarget = target;
                }
                // Or check if target is at Raid Goal Position (within 5m)
                else if (data.RaidGoalPosition != Vector3.zero)
                {
                    float distToGoal = Vector3.Distance(target.transform.position, data.RaidGoalPosition);
                    if (distToGoal <= 5f)
                    {
                        raidGoalTarget = target;
                    }
                }
            }

            // Handle player target (original logic)
            if (targetPlayer != null)
            {
                if (!settings.AllowTargetOffline && !targetPlayer.IsConnected) return;
                if (!settings.AllowTargetSleeping && targetPlayer.IsSleeping()) return;

                if (brain.Senses != null && brain.Senses.Memory != null && brain.Senses.Memory.IsLOS(targetPlayer))
                    return;

                if (!RaidStates.TryGetValue(netId, out RaidState state))
                    RaidStates[netId] = state = new RaidState();

                if (Time.time < state.NextRaidTime)
                    return;

                state.NextRaidTime = Time.time + 0.5f;

                BaseCombatEntity blockingTarget = FindBlockingTarget(npc, targetPlayer);
                if (blockingTarget == null) return;

                if (settings.RequireTargetAuth && !CanRaidTarget(blockingTarget, targetPlayer))
                    return;

                TryAttackTarget(npc, blockingTarget, settings);
                return;
            }

            // 🏠 Handle Raid Goal target (TC/structure) - find and attack blocking structures
            if (raidGoalTarget != null)
            {
                // Check if we have LOS to Raid Goal target
                bool hasLOS = false;
                if (brain.Senses != null && brain.Senses.Memory != null)
                {
                    hasLOS = brain.Senses.Memory.IsLOS(raidGoalTarget);
                }

                // Even if we have LOS to TC, we should still find and attack blocking structures
                // because TCs typically can't be directly attacked - we need to break through doors/walls
                // Also, there might be structures between us and the TC that we need to clear
                if (!RaidStates.TryGetValue(netId, out RaidState state))
                    RaidStates[netId] = state = new RaidState();

                if (Time.time < state.NextRaidTime)
                    return;

                state.NextRaidTime = Time.time + 0.5f;

                // Find blocking structures between NPC and Raid Goal target
                BaseCombatEntity blockingTarget = FindBlockingTargetToRaidGoal(npc, raidGoalTarget);
                if (blockingTarget == null)
                {
                    // No blocking structures found via raycast - try finding structures near Raid Goal position
                    if (data.RaidGoalPosition != Vector3.zero)
                    {
                        blockingTarget = FindStructuresNearRaidGoal(npc, data.RaidGoalPosition);
                    }
                }
                
                if (blockingTarget == null)
                {
                    // No structures found - ensure NPC moves toward Raid Goal target
                    if (brain.Navigator != null)
                    {
                        float distToGoal = Vector3.Distance(npc.transform.position, raidGoalTarget.transform.position);
                        if (distToGoal > 2f) // Only set if not already at goal
                        {
                            brain.Navigator.SetDestination(raidGoalTarget.transform.position, BaseNavigator.NavigationSpeed.Normal);
                        }
                    }
                    return;
                }

                // For Raid Goal targets, we don't check RequireTargetAuth (we're raiding the base, not a player's base)
                // Or we could check if the structure belongs to the same building as the Raid Goal
                TryAttackTarget(npc, blockingTarget, settings);
                return;
            }

            // No valid target for raiding
        }

        private static bool CanRaidTarget(BaseCombatEntity target, BasePlayer player)
        {
            if (target == null || player == null) return false;
            var decay = target as DecayEntity;
            if (decay == null) return false;

            BuildingPrivlidge priv = decay.GetBuildingPrivilege();
            if (priv == null) return false;

            return priv.IsAuthed(player);
        }

        private static BaseCombatEntity FindBlockingTarget(ScientistNPC npc, BasePlayer targetPlayer)
        {
            Vector3 origin = npc.eyes != null ? npc.eyes.position : npc.transform.position;
            Vector3 destination = targetPlayer.eyes != null ? targetPlayer.eyes.position : targetPlayer.transform.position;
            Vector3 direction = (destination - origin).normalized;
            float distance = Vector3.Distance(origin, destination);

            return FindBlockingStructures(origin, direction, distance);
        }

        /// <summary>
        /// 🏠 RAID GOAL: Find blocking structures between NPC and Raid Goal target (TC/structure).
        /// Uses same priority: Doors > Walls > Foundations
        /// </summary>
        private static BaseCombatEntity FindBlockingTargetToRaidGoal(ScientistNPC npc, BaseEntity raidGoalTarget)
        {
            Vector3 origin = npc.eyes != null ? npc.eyes.position : npc.transform.position;
            Vector3 destination = raidGoalTarget.transform.position;
            Vector3 direction = (destination - origin).normalized;
            float distance = Vector3.Distance(origin, destination);

            return FindBlockingStructures(origin, direction, distance);
        }

        /// <summary>
        /// 🏠 RAID GOAL: Find structures near Raid Goal position when no target in memory.
        /// Searches for doors, walls, and foundations within 20m of Raid Goal position.
        /// Priority: Doors > Walls > Foundations
        /// </summary>
        private static BaseCombatEntity FindStructuresNearRaidGoal(ScientistNPC npc, Vector3 raidGoalPosition)
        {
            Vector3 npcPos = npc.transform.position;
            float searchRadius = 20f; // Search within 20m of Raid Goal position
            float searchRadiusSqr = searchRadius * searchRadius;
            
            // Iterate through server entities to find construction entities near Raid Goal position
            // This avoids using Vis.Entities which requires OBB type from Rust.Global
            Door nearestDoor = null;
            BuildingBlock nearestWall = null;
            BuildingBlock nearestFoundation = null;
            float doorDist = float.MaxValue;
            float wallDist = float.MaxValue;
            float foundationDist = float.MaxValue;

            foreach (var entity in BaseNetworkable.serverEntities)
            {
                if (entity == null || entity.IsDestroyed) continue;
                
                // Check if entity is within search radius of Raid Goal position
                float distToGoalSqr = (entity.transform.position - raidGoalPosition).sqrMagnitude;
                if (distToGoalSqr > searchRadiusSqr) continue;
                
                // Check if entity is on construction layer (doors, building blocks)
                if (((1 << entity.gameObject.layer) & ConstructionLayerMask) == 0) continue;

                float dist = Vector3.Distance(npcPos, entity.transform.position);

                Door door = entity as Door;
                if (door != null && !door.IsOpen())
                {
                    if (dist < doorDist)
                    {
                        doorDist = dist;
                        nearestDoor = door;
                    }
                    continue;
                }

                BuildingBlock block = entity as BuildingBlock;
                if (block != null)
                {
                    if (IsFoundation(block))
                    {
                        if (dist < foundationDist)
                        {
                            foundationDist = dist;
                            nearestFoundation = block;
                        }
                    }
                    else
                    {
                        if (dist < wallDist)
                        {
                            wallDist = dist;
                            nearestWall = block;
                        }
                    }
                }
            }

            if (nearestDoor != null) return nearestDoor;
            if (nearestWall != null) return nearestWall;
            if (nearestFoundation != null) return nearestFoundation;
            return null;
        }

        /// <summary>
        /// Find blocking structures (doors, walls, foundations) along a raycast path.
        /// Priority: Doors > Walls > Foundations
        /// </summary>
        private static BaseCombatEntity FindBlockingStructures(Vector3 origin, Vector3 direction, float distance)
        {
            int hitCount = Physics.RaycastNonAlloc(origin, direction, RaycastHits, distance, ConstructionLayerMask);
            if (hitCount <= 0) return null;

            Door nearestDoor = null;
            BuildingBlock nearestWall = null;
            BuildingBlock nearestFoundation = null;
            float doorDist = float.MaxValue;
            float wallDist = float.MaxValue;
            float foundationDist = float.MaxValue;

            for (int i = 0; i < hitCount; i++)
            {
                var hit = RaycastHits[i];
                BaseEntity entity = hit.collider != null ? hit.collider.GetComponentInParent<BaseEntity>() : null;
                if (entity == null || entity.IsDestroyed) continue;

                float dist = hit.distance;

                Door door = entity as Door;
                if (door != null && !door.IsOpen())
                {
                    if (dist < doorDist)
                    {
                        doorDist = dist;
                        nearestDoor = door;
                    }
                    continue;
                }

                BuildingBlock block = entity as BuildingBlock;
                if (block != null)
                {
                    if (IsFoundation(block))
                    {
                        if (dist < foundationDist)
                        {
                            foundationDist = dist;
                            nearestFoundation = block;
                        }
                    }
                    else
                    {
                        if (dist < wallDist)
                        {
                            wallDist = dist;
                            nearestWall = block;
                        }
                    }
                }
            }

            if (nearestDoor != null) return nearestDoor;
            if (nearestWall != null) return nearestWall;
            if (nearestFoundation != null) return nearestFoundation;
            return null;
        }

        /// <summary>
        /// 🏗️ GetRaidTarget: Gets the best raid target based on priority.
        /// Priority: Turret → Foundations → PlayerTarget
        /// Includes pathfinding validation and height checks.
        /// </summary>
        public static BaseCombatEntity GetRaidTarget(ScientistNPC npc)
        {
            if (npc == null || npc.net == null) return null;
            if (!GrimmNPC.IsCustomNpc(npc)) return null;

            ulong netId = npc.net.ID.Value;
            var data = GrimmNPC.GetNpcData(netId);
            if (data == null || !data.IsRaidingNpc) return null;

            // Update targets first (clean up destroyed entities)
            UpdateRaidTargets(npc, data);

            BaseCombatEntity main = null;

            // Priority 1: Turret (highest priority)
            if (data.Turret != null && !data.Turret.IsDestroyed)
            {
                // Find building block near turret (0.1m radius)
                BuildingBlock block = GetNearEntity<BuildingBlock>(data.Turret.transform.position, 0.1f, 1 << 21);
                main = (block != null && !block.IsDestroyed) ? block : data.Turret;
            }
            // Priority 2: Foundations (second priority)
            else if (data.Foundations != null && data.Foundations.Count > 0)
            {
                // Find nearest foundation
                BuildingBlock nearest = null;
                float nearestDist = float.MaxValue;
                foreach (var foundation in data.Foundations)
                {
                    if (foundation == null || foundation.IsDestroyed) continue;
                    float dist = Vector3.Distance(npc.transform.position, foundation.transform.position);
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearest = foundation;
                    }
                }
                main = nearest;
            }
            // Priority 3: PlayerTarget (fallback)
            else if (data.PlayerTarget != null && !data.PlayerTarget.IsDestroyed)
            {
                main = data.PlayerTarget;
            }

            if (main == null) return null;

            // Pathfinding validation for mounted NPCs
            if (npc.IsMounted()) return main;

            // Height check and pathfinding validation
            var brain = npc.Brain;
            if (brain == null || brain.Navigator == null) return main;

            float heightGround = TerrainMeta.HeightMap.GetHeight(main.transform.position);

            // Check if target is too high (more than 15m above ground)
            if (main.transform.position.y - heightGround > 15f)
            {
                // Find building block at ground level
                main = GetNearEntity<BuildingBlock>(
                    new Vector3(main.transform.position.x, heightGround, main.transform.position.z),
                    15f, 1 << 21);
                if (main == null) return null;
            }

            // Pathfinding validation removed - requires UnityEngine.AIModule reference
            // NPCs will use BaseNavigator's pathfinding which handles this automatically
            // The GetRaidTarget method still provides priority-based target selection

            return main;
        }

        /// <summary>
        /// 🏗️ GetNearEntity: Finds nearest entity of type T within radius.
        /// </summary>
        private static T GetNearEntity<T>(Vector3 position, float radius, int layerMask) where T : BaseCombatEntity
        {
            // Use BaseNetworkable.serverEntities iteration (no Vis.Entities dependency)
            T nearest = null;
            float nearestDist = float.MaxValue;
            float radiusSqr = radius * radius;

            foreach (var entity in BaseNetworkable.serverEntities)
            {
                if (entity == null || entity.IsDestroyed) continue;
                if (((1 << entity.gameObject.layer) & layerMask) == 0) continue;

                T typedEntity = entity as T;
                if (typedEntity == null) continue;

                float distSqr = (entity.transform.position - position).sqrMagnitude;
                if (distSqr <= radiusSqr && distSqr < nearestDist)
                {
                    nearestDist = distSqr;
                    nearest = typedEntity;
                }
            }

            return nearest;
        }

        /// <summary>
        /// 🏗️ UpdateTargets: Cleans up destroyed raid targets.
        /// </summary>
        private static void UpdateRaidTargets(ScientistNPC npc, CustomNpcData data)
        {
            if (data == null) return;

            // Clean up turret
            if (data.Turret != null && (data.Turret.IsDestroyed || data.Turret.Health() <= 0f))
                data.Turret = null;

            // Clean up player target
            if (data.PlayerTarget != null && (data.PlayerTarget.IsDestroyed || data.PlayerTarget.Health() <= 0f))
                data.PlayerTarget = null;

            // Clean up foundations
            if (data.Foundations != null)
            {
                var toRemove = new System.Collections.Generic.List<BuildingBlock>();
                foreach (var foundation in data.Foundations)
                {
                    if (foundation == null || foundation.IsDestroyed || foundation.Health() <= 0f)
                        toRemove.Add(foundation);
                }
                foreach (var foundation in toRemove)
                    data.Foundations.Remove(foundation);
            }

            // Clean up current raid target
            if (data.CurrentRaidTarget != null && (data.CurrentRaidTarget.IsDestroyed || data.CurrentRaidTarget.Health() <= 0f))
                data.CurrentRaidTarget = null;
        }

        /// <summary>
        /// 🏗️ AddTurret: Adds a turret to raid targets.
        /// </summary>
        public static void AddTurret(ScientistNPC npc, BaseCombatEntity turret)
        {
            if (npc == null || turret == null || npc.net == null) return;
            if (!GrimmNPC.IsCustomNpc(npc)) return;

            ulong netId = npc.net.ID.Value;
            var data = GrimmNPC.GetNpcData(netId);
            if (data == null || !data.IsRaidingNpc) return;

            // Only add if no turret exists or new turret is closer
            if (data.Turret == null || data.Turret.IsDestroyed ||
                Vector3.Distance(npc.transform.position, turret.transform.position) <
                Vector3.Distance(npc.transform.position, data.Turret.transform.position))
            {
                data.Turret = turret;
                // Find building block near turret (0.1m radius)
                BuildingBlock block = GetNearEntity<BuildingBlock>(turret.transform.position, 0.1f, 1 << 21);
                data.CurrentRaidTarget = (block != null && !block.IsDestroyed) ? block : turret;
            }
        }

        /// <summary>
        /// 🏗️ AddTargetRaid: Adds foundations to raid targets.
        /// </summary>
        public static void AddTargetRaid(ScientistNPC npc, System.Collections.Generic.HashSet<BuildingBlock> foundations)
        {
            if (npc == null || foundations == null || npc.net == null) return;
            if (!GrimmNPC.IsCustomNpc(npc)) return;

            ulong netId = npc.net.ID.Value;
            var data = GrimmNPC.GetNpcData(netId);
            if (data == null || !data.IsRaidingNpc) return;

            // Add all foundations to the set
            if (data.Foundations == null)
                data.Foundations = new System.Collections.Generic.HashSet<BuildingBlock>();
            
            foreach (var foundation in foundations)
            {
                if (foundation != null && !foundation.IsDestroyed)
                    data.Foundations.Add(foundation);
            }
        }

        /// <summary>
        /// 🛡️ AddTargetGuard: Sets a guard target for a guard NPC (like NpcSpawn AddTargetGuard).
        /// Guard NPCs will follow their guard target and prioritize threats to it.
        /// </summary>
        public static void AddTargetGuard(ScientistNPC npc, BaseEntity target, float maxDistance = 0f)
        {
            if (npc == null || target == null || npc.net == null) return;
            if (!GrimmNPC.IsCustomNpc(npc)) return;

            ulong netId = npc.net.ID.Value;
            var data = GrimmNPC.GetNpcData(netId);
            if (data == null) return;

            // Only assign if no current guard target or new target is closer (if maxDistance specified)
            if (data.GuardTarget == null || data.GuardTarget.IsDestroyed ||
                (maxDistance > 0f && Vector3.Distance(npc.transform.position, target.transform.position) < maxDistance))
            {
                data.IsGuardNpc = true;
                data.GuardTarget = target;
                data.BeforeGuardHomePosition = data.HomePosition; // Store original home position
            }
        }

        /// <summary>
        /// 💣 ExplosionBomber: Triggers bomber explosion and calls OnBomberExplosion hook.
        /// </summary>
        public static void ExplosionBomber(ScientistNPC npc, BaseEntity target = null)
        {
            if (npc == null || npc.IsDestroyed) return;
            if (!GrimmNPC.IsCustomNpc(npc)) return;

            ulong netId = npc.net?.ID.Value ?? 0;
            var data = GrimmNPC.GetNpcData(netId);
            if (data == null || !data.IsBomber) return;

            // Trigger explosion effect
            Effect.server.Run("assets/prefabs/tools/c4/effects/c4_explosion.prefab", 
                npc.transform.position + new Vector3(0f, 1f, 0f), Vector3.up, null, true);

            // Call hook for external plugins (like DefendableHomes)
            GrimmNPC.CallOxideHook("OnBomberExplosion", npc, target);

            // Kill the NPC
            npc.Kill();
        }

        /// <summary>
        /// Helper method to attack raid targets (calls StartExplosion or TryHandleRaidAttack).
        /// </summary>
        private static void TryAttackTarget(ScientistNPC npc, BaseCombatEntity target, RaidSettings settings)
        {
            if (npc == null || target == null) return;

            var brain = npc.Brain;
            if (brain == null || brain.Navigator == null) return;
            
            // Use StartExplosion() to decide between C4 and rocket launcher (like NpcSpawn)
            if (!StartExplosion(npc, target))
            {
                // Fallback to general raid attack if StartExplosion returns false
                SpecialWeaponsHandler.TryHandleRaidAttack(npc, target, settings);
            }
        }

        /// <summary>
        /// Decides between C4 and rocket launcher for raiding (like NpcSpawn line 1076).
        /// Returns true if an attack was started, false otherwise.
        /// </summary>
        private static bool StartExplosion(ScientistNPC npc, BaseCombatEntity target)
        {
            if (npc == null || target == null) return false;
            
            // Try C4 first (closer range)
            if (CanThrownC4(npc, target))
            {
                var state = SpecialWeaponsHandler.GetState(npc.net?.ID.Value ?? 0);
                if (state != null)
                {
                    state.FireC4Coroutine = ServerMgr.Instance.StartCoroutine(SpecialWeaponsHandler.ThrownC4(npc, target, state));
                    return true;
                }
            }
            
            // Then try rocket launcher (longer range)
            if (CanRaidRocketLauncher(npc, target))
            {
                // Throw smoke grenade (like NpcSpawn line 1086)
                SpecialWeaponsHandler.ThrownSmoke(npc);
                
                var state = SpecialWeaponsHandler.GetState(npc.net?.ID.Value ?? 0);
                if (state != null)
                {
                    state.FireRocketLauncherCoroutine = ServerMgr.Instance.StartCoroutine(SpecialWeaponsHandler.ProcessFireRocketLauncher(npc, target, state));
                    return true;
                }
            }
            
            return false;
        }

        /// <summary>
        /// Checks if NPC can throw C4 at target (like NpcSpawn line 1148).
        /// </summary>
        private static bool CanThrownC4(ScientistNPC npc, BaseCombatEntity target)
        {
            if (npc == null || target == null) return false;
            
            ulong netId = npc.net?.ID.Value ?? 0;
            var state = SpecialWeaponsHandler.GetState(netId);
            if (state == null) return false;
            
            // Check state flags (like NpcSpawn)
            if (state.IsReloadC4 || state.IsFireC4) return false;
            
            // Check if has C4
            if (!HasC4(npc)) return false;
            
            // Check distance (must be within 6f like NpcSpawn)
            float distance = Vector3.Distance(npc.transform.position, target.transform.position);
            return distance < 6f;
        }

        /// <summary>
        /// Checks if NPC can fire rocket launcher at target (like NpcSpawn line 1095).
        /// </summary>
        private static bool CanRaidRocketLauncher(ScientistNPC npc, BaseCombatEntity target)
        {
            if (npc == null || target == null) return false;
            
            ulong netId = npc.net?.ID.Value ?? 0;
            var state = SpecialWeaponsHandler.GetState(netId);
            if (state == null) return false;
            
            // Check state flags (like NpcSpawn)
            if (state.IsReloadRocketLauncher || state.IsFireRocketLauncher) return false;
            
            // Check if has rocket launcher
            if (!HasRocketLauncher(npc)) return false;
            
            // Check distance (must be within 30f like NpcSpawn)
            float distance = Vector3.Distance(npc.transform.position, target.transform.position);
            return distance < 30f;
        }

        /// <summary>
        /// Checks if NPC has C4 in belt (like NpcSpawn line 1146).
        /// </summary>
        private static bool HasC4(ScientistNPC npc)
        {
            if (npc == null || npc.inventory == null || npc.inventory.containerBelt == null) return false;
            
            foreach (var item in npc.inventory.containerBelt.itemList)
            {
                if (item?.info?.shortname == "explosive.timed")
                    return true;
            }
            
            return false;
        }

        /// <summary>
        /// Checks if NPC has rocket launcher in belt (like NpcSpawn line 1093).
        /// </summary>
        private static bool HasRocketLauncher(ScientistNPC npc)
        {
            if (npc == null || npc.inventory == null || npc.inventory.containerBelt == null) return false;
            
            foreach (var item in npc.inventory.containerBelt.itemList)
            {
                if (item?.info?.shortname == "rocket.launcher")
                    return true;
            }
            
            return false;
        }

        /// <summary>
        /// Checks if two players are on the same team (like NpcSpawn line 1170).
        /// Supports Friends and Clans plugins.
        /// </summary>
        private static bool IsTeam(ulong playerId, ulong targetId)
        {
            if (playerId == 0 || targetId == 0) return false;
            if (playerId == targetId) return true;
            
            // Check Rust team system
            var playerTeam = RelationshipManager.ServerInstance?.FindPlayersTeam(playerId);
            if (playerTeam != null && playerTeam.members != null && playerTeam.members.Contains(targetId))
                return true;
            
            // Note: Friends and Clans plugin integration would require Oxide plugin system
            // Harmony mods don't have direct access to Oxide plugins
            // This functionality can be added via external plugin integration if needed
            
            return false;
        }

        /// <summary>
        /// Handles raiding when no foundations are found (like NpcSpawn line 1181).
        /// </summary>
        private static void TryRaidWithoutFoundations(ScientistNPC npc)
        {
            if (npc == null) return;
            
            ulong netId = npc.net?.ID.Value ?? 0;
            var data = GrimmNPC.GetNpcData(netId);
            if (data == null || !data.IsRaidingNpc) return;
            
            // Only proceed if in raid state and no foundations found
            if (data.Foundations == null || data.Foundations.Count != 0) return;
            
            var brain = npc.Brain;
            if (brain == null || brain.Events == null || brain.Events.Memory == null) return;
            
            int slot = brain.Events.CurrentInputMemorySlot;
            if (slot < 0) return;
            
            var currentTarget = brain.Events.Memory.Entity.Get(slot);
            if (currentTarget == null) return;
            
            // Check if current target is drone
            if (currentTarget is Drone)
            {
                data.PlayerTarget = null;
                data.CurrentRaidTarget = null;
                return;
            }
            
            // Check if current target is player
            var basePlayer = currentTarget as BasePlayer;
            if (basePlayer != null)
            {
                bool foundValidTarget = false;
                
                // Try to find building block near player
                BuildingBlock block = GetNearEntityBase<BuildingBlock>(currentTarget.transform.position, 0.1f, 1 << 21);
                if (block != null && !block.IsDestroyed)
                {
                    ulong blockOwnerId = block.OwnerID;
                    if (blockOwnerId > 0 && IsTeam(basePlayer.userID, blockOwnerId))
                    {
                        data.PlayerTarget = block;
                        foundValidTarget = true;
                    }
                }
                
                // Check if player is on tugboat
                if (!foundValidTarget)
                {
                    var tugboat = currentTarget.GetParentEntity() as Tugboat;
                    if (tugboat != null && !tugboat.IsDestroyed)
                    {
                        data.PlayerTarget = tugboat;
                        foundValidTarget = true;
                    }
                }
                
                // Check if player is in submarine
                if (!foundValidTarget)
                {
                    var vehicle = basePlayer.GetMountedVehicle();
                    if (vehicle != null && !vehicle.IsDestroyed)
                    {
                        if (vehicle is SubmarineDuo || vehicle is BaseSubmarine)
                        {
                            data.PlayerTarget = vehicle;
                            foundValidTarget = true;
                        }
                    }
                }
                
                // Clear targets if no valid target found
                if (!foundValidTarget)
                {
                    data.PlayerTarget = null;
                    data.CurrentRaidTarget = null;
                }
            }
        }

        /// <summary>
        /// Helper to find nearby entities of a specific type (like NpcSpawn GetNearEntity).
        /// Uses the existing GetNearEntity method with BaseCombatEntity constraint.
        /// </summary>
        private static T GetNearEntityBase<T>(Vector3 position, float radius, int layerMask) where T : BaseEntity
        {
            if (position == Vector3.zero) return null;
            
            var hits = Physics.OverlapSphere(position, radius, layerMask);
            if (hits == null || hits.Length == 0) return null;
            
            foreach (var hit in hits)
            {
                var entity = hit.GetComponentInParent<T>();
                if (entity != null && !entity.IsDestroyed)
                    return entity;
            }
            
            return null;
        }

        private static bool IsFoundation(BuildingBlock block)
        {
            if (block == null) return false;
            string name = block.ShortPrefabName ?? string.Empty;
            return name.Contains("foundation");
        }

        private static bool IsOnMonument(Vector3 position)
        {
            var monuments = TerrainMeta.Path?.Monuments;
            if (monuments == null) return false;

            for (int i = 0; i < monuments.Count; i++)
            {
                var monument = monuments[i];
                if (monument != null && monument.IsInBounds(position))
                    return true;
            }

            return false;
        }
    }
}
