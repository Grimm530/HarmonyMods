using HarmonyLib;
using Rust;
using Rust.Ai;
using System;
using System.Reflection;
using UnityEngine;
using ConVar;

namespace GrimmNPC.Patches
{
    /// <summary>
    /// CRITICAL: Eliminates component destruction (27.9s → <1s spawn time).
    /// 
    /// Patches ScientistNPC.ServerInit() to apply custom config WITHOUT destroying components.
    /// 
    /// Initialization Chain Integration:
    /// ScientistNPC.ServerInit()
    ///   → [GrimmNPC] ScientistNPC_ServerInit_Patch.Postfix()
    ///     → ApplyCustomConfig()
    ///       → Configure Health, Damage, Name
    ///       → Configure Brain (SenseRange, TargetLostRange)
    ///       → Configure Navigator (CanUseBaseNav, CanUseNavMesh, AreaMask, AgentTypeID)
    ///   → BaseAIBrain.InitializeAI()
    ///     → BaseNavigator.Init()
    ///       → BaseNavigator.PlaceOnNavMesh(2f)
    /// 
    /// Critical Timing: This patch runs AFTER components are initialized but BEFORE
    /// BaseAIBrain.InitializeAI(), allowing GrimmNPC to configure components before the brain starts.
    /// 
    /// Performance: Runs once per spawn (not in hot path). The in-place configuration
    /// eliminates the 27.9s component destruction overhead.
    /// 
    /// See INSTRUCTIONAL.md "Complete Initialization Chain" section for details.
    /// </summary>
    [HarmonyPatch(typeof(ScientistNPC), nameof(ScientistNPC.ServerInit))]
    public class ScientistNPC_ServerInit_Patch
    {
        // Cached reflection for BaseNavigator static fields (performance optimization)
        private static FieldInfo _navTypeHeightOffsetField;
        private static FieldInfo _navTypeDistanceField;
        private static bool _reflectionInitialized = false;
        private static readonly object _reflectionLock = new object();
        
        // Cached reflection for NavMeshAgent properties (performance optimization - spawn-time but called frequently)
        private static PropertyInfo _agentAreaMaskProp;
        private static PropertyInfo _agentAgentTypeIDProp;
        private static PropertyInfo _agentUpdateRotationProp;
        private static PropertyInfo _agentUpdatePositionProp;
        private static bool _agentReflectionInitialized = false;
        private static readonly object _agentReflectionLock = new object();
        
        // Default values matching BaseNavigator defaults (used if reflection fails)
        private const float DEFAULT_NAV_TYPE_HEIGHT_OFFSET = 0.5f;
        private const float DEFAULT_NAV_TYPE_DISTANCE = 1f;
        
        /// <summary>
        /// Initializes cached reflection for BaseNavigator static fields.
        /// Called once on first use to avoid repeated reflection lookups.
        /// </summary>
        private static void InitializeReflection()
        {
            if (_reflectionInitialized) return;
            
            lock (_reflectionLock)
            {
                if (_reflectionInitialized) return;
                
                try
                {
                    var baseNavigatorType = typeof(BaseNavigator);
                    _navTypeHeightOffsetField = baseNavigatorType.GetField("navTypeHeightOffset", 
                        BindingFlags.Public | BindingFlags.Static);
                    _navTypeDistanceField = baseNavigatorType.GetField("navTypeDistance", 
                        BindingFlags.Public | BindingFlags.Static);
                    
                    _reflectionInitialized = true;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[GrimmNPC] Failed to initialize BaseNavigator reflection, using defaults: {ex.Message}");
                    _reflectionInitialized = true; // Mark as initialized to prevent retry loops
                }
            }
        }
        
        /// <summary>
        /// Gets navTypeHeightOffset from BaseNavigator static field.
        /// Returns default (0.5f) if reflection fails.
        /// </summary>
        private static float GetNavTypeHeightOffset()
        {
            InitializeReflection();
            
            if (_navTypeHeightOffsetField != null)
            {
                try
                {
                    object value = _navTypeHeightOffsetField.GetValue(null);
                    if (value is float floatValue)
                    {
                        return floatValue;
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[GrimmNPC] Failed to get navTypeHeightOffset, using default: {ex.Message}");
                }
            }
            
            return DEFAULT_NAV_TYPE_HEIGHT_OFFSET;
        }
        
        /// <summary>
        /// Gets navTypeDistance from BaseNavigator static field.
        /// Returns default (1f) if reflection fails.
        /// </summary>
        private static float GetNavTypeDistance()
        {
            InitializeReflection();
            
            if (_navTypeDistanceField != null)
            {
                try
                {
                    object value = _navTypeDistanceField.GetValue(null);
                    if (value is float floatValue)
                    {
                        return floatValue;
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[GrimmNPC] Failed to get navTypeDistance, using default: {ex.Message}");
                }
            }
            
            return DEFAULT_NAV_TYPE_DISTANCE;
        }
        
        /// <summary>
        /// Initializes cached reflection for NavMeshAgent properties.
        /// Called once on first use to avoid repeated reflection lookups.
        /// </summary>
        private static void InitializeAgentReflection()
        {
            if (_agentReflectionInitialized) return;
            
            lock (_agentReflectionLock)
            {
                if (_agentReflectionInitialized) return;
                
                try
                {
                    // Get NavMeshAgent type via reflection (avoids requiring UnityEngine.AIModule reference)
                    var navMeshAgentType = Type.GetType("UnityEngine.AI.NavMeshAgent, UnityEngine.AIModule");
                    if (navMeshAgentType == null)
                    {
                        navMeshAgentType = Type.GetType("UnityEngine.AI.NavMeshAgent");
                    }
                    
                    if (navMeshAgentType != null)
                    {
                        _agentAreaMaskProp = navMeshAgentType.GetProperty("areaMask");
                        _agentAgentTypeIDProp = navMeshAgentType.GetProperty("agentTypeID");
                        _agentUpdateRotationProp = navMeshAgentType.GetProperty("updateRotation");
                        _agentUpdatePositionProp = navMeshAgentType.GetProperty("updatePosition");
                    }
                    
                    _agentReflectionInitialized = true;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[GrimmNPC] Failed to initialize NavMeshAgent reflection, using runtime lookup: {ex.Message}");
                    _agentReflectionInitialized = true; // Mark as initialized to prevent retry loops
                }
            }
        }
        
        /// <summary>
        /// Postfix executes after ScientistNPC.ServerInit() completes.
        /// Applies custom configuration to existing components without destroying them.
        /// </summary>
        static void Postfix(ScientistNPC __instance)
        {
            // Only process if this is a custom NPC (check skinID)
            if (__instance.skinID != GrimmNPC.CUSTOM_NPC_SKIN_ID)
                return;
            
            try
            {
                // CRITICAL: Check if navmesh is ready before applying config
                // This prevents spawn/placement thrash during navmesh building windows
                // Mirrors Rust's own spawner logic (NPCSpawner.WaitingForNavMesh)
                // Note: We still apply config even if navmesh isn't ready, as config doesn't require navmesh.
                // Rust's BaseNavigator.Init() will handle placement when navmesh is ready.
                if (IsWaitingForNavMesh(__instance.transform.position))
                {
                    var config = GrimmNPC.GetConfig();
                    if (config.EnableDebugLogging)
                    {
                        UnityEngine.Debug.LogWarning($"[GrimmNPC Spawn] Navmesh not ready for NPC at {__instance.transform.position} - " +
                            "config will be applied but placement may be delayed until navmesh is ready");
                    }
                    // Continue with config application - Rust will handle placement when navmesh is ready
                }
                
                // Apply custom config WITHOUT destroying components
                // This is the key optimization - modify in place instead of destroy/recreate
                ApplyCustomConfig(__instance);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[GrimmNPC] Error in ServerInit patch: {ex}");
            }
        }
        
        /// <summary>
        /// Checks if navmesh is ready for spawning at the given position.
        /// Mirrors Rust's NPCSpawner.WaitingForNavMesh() logic to prevent spawn/placement thrash.
        /// 
        /// Returns true if navmesh is NOT ready (should wait), false if ready.
        /// </summary>
        private static bool IsWaitingForNavMesh(Vector3 position)
        {
            // Check if AI.move is false (navmesh system not ready)
            if (!AI.move)
            {
                return true; // Wait for navmesh system
            }
            
            // Check if position is on a monument with building navmesh
            if (TerrainMeta.Path != null && TerrainMeta.Path.Monuments != null)
            {
                foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
                {
                    if (monument == null) continue;
                    
                    if (monument.IsInBounds(position) && monument.HasNavmesh)
                    {
                        // Check if monument navmesh is still building
                        MonumentNavMesh monumentNavMesh = monument.GetComponentInChildren<MonumentNavMesh>();
                        if (monumentNavMesh != null)
                        {
                            // Use reflection to check IsBuilding property
                            var navMeshType = typeof(MonumentNavMesh);
                            var isBuildingProp = navMeshType.GetProperty("IsBuilding", BindingFlags.Public | BindingFlags.Instance);
                            if (isBuildingProp != null)
                            {
                                try
                                {
                                    bool isBuilding = (bool)isBuildingProp.GetValue(monumentNavMesh);
                                    if (isBuilding)
                                    {
                                        return true; // Monument navmesh still building
                                    }
                                }
                                catch
                                {
                                    // If reflection fails, assume navmesh is ready (fail-safe)
                                }
                            }
                        }
                    }
                }
            }
            
            // Check dungeon navmesh (if applicable)
            // Note: DungeonNavmesh.NavReady() check would require additional reflection
            // For now, we skip this check as it's less common
            
            return false; // Navmesh is ready
        }
        
        private static void ApplyCustomConfig(ScientistNPC npc)
        {
            // Get or create custom NPC data
            ulong netId = npc.net?.ID.Value ?? 0;
            if (netId == 0) return;
            
            var config = GrimmNPC.GetConfig();
            bool debugLogging = config.EnableDebugLogging;
            
            Vector3 spawnPosition = npc.transform.position;
            
            // CRITICAL: Check for pending registration FIRST, even before checking GetNpcData
            // This allows external plugins to register NPC data before Spawn() is called
            // (when netId is 0). Prevents ServerInit from creating default 50m RoamRange NPCs.
            // NOTE: We check pending BEFORE GetNpcData because pending registrations should
            // take precedence over any existing registration (which might be stale/default).
            var npcData = GrimmNPC.ConsumePending(npc);
            if (npcData != null)
            {
                // Found pending registration - use it
                if (debugLogging)
                {
                    UnityEngine.Debug.Log($"[GrimmNPC Spawn] Found pending registration: InstanceID={npc.GetInstanceID()}, " +
                        $"Name={npcData.Name}, RoamRange={npcData.RoamRange}m");
                }
                
                // Ensure HomePosition is correct (may have been set before spawn position was known)
                if (npcData.HomePosition == Vector3.zero)
                {
                    npcData.HomePosition = spawnPosition;
                }
                
                // Auto-detect navmesh type if using defaults (AreaMask=1, AgentTypeID=-1372625422)
                // OR if position is on monument but has wrong settings
                // This allows pending registrations to use auto-detection and fixes wrong settings
                bool shouldDetectPending = (npcData.AreaMask == 1 && npcData.AgentTypeID == -1372625422);
                
                // Also detect if we're on a monument but have wrong settings
                if (!shouldDetectPending && npcData.AreaMask == 1)
                {
                    if (TerrainMeta.Path != null && TerrainMeta.Path.Monuments != null)
                    {
                        foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
                        {
                            if (monument != null && monument.IsInBounds(spawnPosition))
                            {
                                shouldDetectPending = true;
                                if (debugLogging)
                                {
                                    UnityEngine.Debug.LogWarning($"[GrimmNPC Spawn] Pending registration for {npcData.Name} is on monument '{monument.name}' " +
                                        $"but has wrong AreaMask={npcData.AreaMask} (should be 25). Re-detecting navmesh type.");
                                }
                                break;
                            }
                        }
                    }
                }
                
                if (shouldDetectPending)
                {
                    DetectAndConfigureNavMesh(npcData, spawnPosition, debugLogging);
                }
                
                // Register with netId now that we have it
                GrimmNPC.RegisterNpc(netId, npcData);
                
                if (debugLogging)
                {
                    UnityEngine.Debug.Log($"[GrimmNPC Spawn] Consumed pending registration: {npcData.Name} | " +
                        $"NetID: {netId} | " +
                        $"HomePosition: {spawnPosition} | " +
                        $"RoamRange: {npcData.RoamRange}m | " +
                        $"ChaseRange: {npcData.ChaseRange}m");
                }
            }
            else
            {
                // No pending registration - check if already registered
                npcData = GrimmNPC.GetNpcData(netId);
                if (npcData == null)
                {
                    // Not registered and no pending - create defaults
                    if (debugLogging)
                    {
                        UnityEngine.Debug.Log($"[GrimmNPC Spawn] No pending registration found: InstanceID={npc.GetInstanceID()}, " +
                            $"NetID={netId}, will create defaults");
                    }
                }
            }
            
            if (npcData == null)
            {
                // Create default data if not found and no pending registration
                // CRITICAL: Auto-detect navmesh type from position instead of using terrain defaults
                // This ensures NPCs get correct navmesh settings even if registration failed
                npcData = new CustomNpcData
                {
                    Name = npc.displayName ?? "Custom NPC",
                    Health = npc.startHealth,
                    HomePosition = spawnPosition
                };
                
                // Auto-detect navmesh type from position before registering
                // This prevents NPCs from getting wrong terrain navmesh settings
                // Follows detection pattern: monument -> building block -> terrain default
                DetectAndConfigureNavMesh(npcData, spawnPosition, debugLogging);
                
                GrimmNPC.RegisterNpc(netId, npcData);
                
                if (debugLogging)
                {
                    UnityEngine.Debug.Log($"[GrimmNPC Spawn] Registered NEW NPC (auto-detected navmesh): {npcData.Name} | " +
                        $"NetID: {netId} | " +
                        $"HomePosition: {spawnPosition} | " +
                        $"AreaMask: {npcData.AreaMask} | " +
                        $"AgentTypeID: {npcData.AgentTypeID} | " +
                        $"RoamRange: {npcData.RoamRange}m | " +
                        $"ChaseRange: {npcData.ChaseRange}m");
                }
            }
            else
            {
                // NPC already registered - log existing data
                if (debugLogging)
                {
                    UnityEngine.Debug.Log($"[GrimmNPC Spawn] Found EXISTING NPC: {npcData.Name} | " +
                        $"NetID: {netId} | " +
                        $"HomePosition: {npcData.HomePosition} | " +
                        $"CurrentPos: {spawnPosition} | " +
                        $"RoamRange: {npcData.RoamRange}m | " +
                        $"ChaseRange: {npcData.ChaseRange}m");
                }
                
                // CRITICAL: Validate HomePosition (prevents clustering issues)
                // This can happen if NPC was registered before spawn position was set
                if (GrimmNPC.ValidateHomePosition(npcData, spawnPosition))
                {
                    if (debugLogging)
                    {
                        UnityEngine.Debug.Log($"[GrimmNPC Spawn] Updated HomePosition from zero to: {spawnPosition}");
                    }
                }
            }
            
            // Apply config values directly to existing components
            if (npcData.Health > 0)
            {
                npc.startHealth = npcData.Health;
                npc._health = npcData.Health;
            }
            
            if (npcData.DamageScale > 0)
            {
                npc.damageScale = npcData.DamageScale;
            }
            
            // CRITICAL: Apply Name from npcData to displayName to preserve custom NPC names
            // This ensures map markers and vending machine markers show the correct name
            if (!string.IsNullOrEmpty(npcData.Name))
            {
                npc.displayName = npcData.Name;
            }
            
            // Apply to brain if it exists
            var brain = npc.GetComponent<ScientistBrain>();
            if (brain != null)
            {
                // Modify brain properties directly
                if (npcData.SenseRange > 0)
                {
                    brain.SenseRange = npcData.SenseRange;
                }
                
                // Set target lost range based on sense range
                brain.TargetLostRange = npcData.SenseRange * 2f;
                
                // CRITICAL: Re-initialize sense system after setting SenseRange
                // This ensures the sense system uses the correct range and can detect players
                // The sense system may have been initialized with default values before GrimmNPC set SenseRange
                if (brain.Senses != null)
                {
                    // Ensure basic player detection properties are set (will be overridden by BossMonster in NextTick)
                    // These defaults ensure NPCs can detect players even if BossMonster hasn't configured them yet
                    if (brain.SenseTypes == 0)
                    {
                        brain.SenseTypes = EntityType.Player;
                    }
                    // CRITICAL FIX: Set HostileTargetsOnly to FALSE so NPCs detect all players immediately
                    // When HostileTargetsOnly=true, NPCs only detect players who have already attacked (are hostile)
                    // Setting to false allows NPCs to detect and target players as soon as they're in range with LOS
                    brain.HostileTargetsOnly = false;
                    
                    // Use brain's current properties (may have defaults, but BossMonster will override in NextTick)
                    float senseRange = npcData.SenseRange > 0 ? npcData.SenseRange : 50f;
                    float memoryDuration = brain.MemoryDuration > 0 ? brain.MemoryDuration : 10f;
                    float visionCone = brain.VisionCone != 0 ? brain.VisionCone : Vector3.Dot(Vector3.forward, Quaternion.Euler(0f, 120f, 0f) * Vector3.forward);
                    bool checkVisionCone = brain.CheckVisionCone;
                    bool checkLOS = brain.CheckLOS;
                    bool ignoreNonVisionSneakers = brain.IgnoreNonVisionSneakers;
                    float listenRange = brain.ListenRange > 0 ? brain.ListenRange : senseRange / 2f;
                    bool hostileTargetsOnly = false; // Always false for immediate player detection
                    bool ignoreSafeZonePlayers = brain.IgnoreSafeZonePlayers;
                    EntityType senseTypes = brain.SenseTypes;
                    bool refreshKnownLOS = brain.RefreshKnownLOS;
                    
                    // Re-initialize sense system with correct SenseRange and player detection enabled
                    brain.Senses.Init(
                        npc,
                        brain,
                        memoryDuration,
                        senseRange,
                        brain.TargetLostRange,
                        visionCone,
                        checkVisionCone,
                        checkLOS,
                        ignoreNonVisionSneakers,
                        listenRange,
                        hostileTargetsOnly,
                        false, // senseFriendlies
                        ignoreSafeZonePlayers,
                        senseTypes,
                        refreshKnownLOS
                    );
                    
                    // Force immediate update to start detecting targets
                    brain.Senses.nextUpdateTime = 0f;
                    brain.Senses.Update();
                    
                    if (debugLogging)
                    {
                        UnityEngine.Debug.Log($"[GrimmNPC Spawn] Re-initialized sense system for {npcData.Name}: " +
                            $"SenseRange={senseRange}m, HostileTargetsOnly={hostileTargetsOnly}, SenseTypes={senseTypes}, CheckLOS={checkLOS}");
                    }
                }
            }
            
            // Final check: Ensure HomePosition is set correctly (fallback validation)
            if (GrimmNPC.ValidateHomePosition(npcData, spawnPosition))
            {
                if (debugLogging)
                {
                    UnityEngine.Debug.Log($"[GrimmNPC Spawn] Final fallback: Set HomePosition to spawn position: {spawnPosition}");
                }
            }

            // 💣 BOMBER: Spawn timed explosive if this is a bomber NPC
            if (npcData.IsBomber)
            {
                BomberHelper.SpawnBomberExplosive(npc, npcData, debugLogging);
            }
            
            // CRITICAL: Auto-detect navmesh type following detection pattern:
            // 1. Check if on monument first
            // 2. Check if on building block (player structure)
            // 3. Default based on TypeNavMesh setting
            // 
            // Always run detection if using default terrain values (AreaMask=1, AgentTypeID=-1372625422)
            // OR if current values are wrong for monument (AreaMask=1 but position is on monument)
            // This ensures NPCs on monuments get correct settings even if registered with wrong values
            bool shouldDetect = (npcData.AreaMask == 1 && npcData.AgentTypeID == -1372625422);
            
            // Also detect if we're on a monument but have wrong settings (AreaMask should be 25)
            if (!shouldDetect && npcData.AreaMask == 1)
            {
                // Quick check: if position is on monument, we need to fix the settings
                if (TerrainMeta.Path != null && TerrainMeta.Path.Monuments != null)
                {
                    foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
                    {
                        if (monument != null && monument.IsInBounds(spawnPosition))
                        {
                            shouldDetect = true;
                            if (debugLogging)
                            {
                                UnityEngine.Debug.LogWarning($"[GrimmNPC Spawn] NPC {npcData.Name} is on monument '{monument.name}' " +
                                    $"but has wrong AreaMask={npcData.AreaMask} (should be 25). Re-detecting navmesh type.");
                            }
                            break;
                        }
                    }
                }
            }
            
            if (shouldDetect)
            {
                DetectAndConfigureNavMesh(npcData, spawnPosition, debugLogging);
            }
            
            // Apply to navigator if it exists
            var navigator = npc.GetComponent<BaseNavigator>();
            if (navigator != null)
            {
                // CRITICAL: Initialization order according to Base_NavMesh_Complete_Guide.md:
                // 1. Detect building block at spawn position ✓ (done above via DetectAndConfigureNavMesh)
                // 2. Configure NavMeshAgent (areaMask, agentTypeID, updateRotation, updatePosition)
                //    - Done below (AFTER Init() may have been called by NPCPlayer.ServerInit())
                //    - The guide says Agent properties can be set "BEFORE calling Init() or AFTER initialization"
                // 3. Call BaseNavigator.Init() - done by Rust (NPCPlayer.ServerInit() or BaseAIBrain.InitializeAI())
                // 4. Enable Base Navigation flags AFTER Init() ✓ (done here)
                // 5. Verify initialization ✓ (navigator exists check)
                //
                // CRITICAL: Enable Base navigation for building blocks (foundations, floors, etc.)
                // This allows NPCs to navigate on player buildings WITHOUT requiring navmesh generation
                // 
                // Navigation Type Selection (handled by BaseNavigator.DetermineNavigationType()):
                // - CanUseBaseNav = true enables NavigationType.Base for building blocks
                // - CanUseNavMesh = true enables NavigationType.NavMesh when navmesh is available
                // - BaseNavigator automatically selects BaseNav if on building blocks, NavMesh otherwise
                // 
                // Reference: Base_NavMesh_Complete_Guide.md "Complete Initialization Flow" section (lines 172-329)
                navigator.CanUseBaseNav = true;
                navigator.CanUseNavMesh = true;
                
                // Configure navigator properties for better behavior (based on FrankensteinBrain/BaseNPC patterns)
                // These improve NPC navigation responsiveness and precision
                navigator.CanUseAStar = true; // Enable AStar pathfinding as fallback
                
                // CRITICAL: Set DefaultArea based on navmesh type to ensure proper sampling during placement
                // PlaceOnNavMesh() → GetNearestNavmeshPosition() uses defaultAreaMask derived from DefaultArea,
                // not NavMeshAgent.areaMask. This must match the navmesh type for successful placement.
                // 
                // Follows game's pattern (BradleyAPC.SpawnScientist):
                // - For monuments: Use "HumanNPC" as DefaultArea (matches game's approach for normal spawns)
                // - For terrain: Use "Walkable" as DefaultArea (matches game's approach for road spawns)
                // 
                // TIMING NOTE: According to AI_Navigation_Instructional.md, NPCPlayer.ServerInit() may call
                // BaseNavigator.Init() before this patch runs. If Init() was already called, defaultAreaMask
                // was calculated from the old DefaultArea. We update DefaultArea here, and if Init() was already
                // called, we need to update defaultAreaMask manually via reflection.
                string previousDefaultArea = navigator.DefaultArea;
                if (npcData.AreaMask == 25)
                {
                    // Monument navmesh - use "HumanNPC" (matches game's approach in BradleyAPC for normal spawns)
                    navigator.DefaultArea = "HumanNPC";
                }
                else
                {
                    // Terrain navmesh (AreaMask=1) - use "Walkable" (matches game's approach in BradleyAPC for road spawns)
                    navigator.DefaultArea = "Walkable";
                }
                
                // CRITICAL: If Init() was already called (by NPCPlayer.ServerInit()), update defaultAreaMask manually
                // Init() calculates defaultAreaMask from DefaultArea, but if Init() was called before we set DefaultArea,
                // we need to update defaultAreaMask to match the new DefaultArea.
                // Check if defaultAreaMask field exists and update it if DefaultArea changed
                if (previousDefaultArea != navigator.DefaultArea)
                {
                    try
                    {
                        var navigatorType = navigator.GetType();
                        var defaultAreaMaskField = navigatorType.GetField("defaultAreaMask", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (defaultAreaMaskField != null)
                        {
                            // Recalculate defaultAreaMask from new DefaultArea (matches Init() logic)
                            // Use reflection to avoid requiring UnityEngine.AIModule reference
                            var navMeshType = Type.GetType("UnityEngine.AI.NavMesh, UnityEngine.AIModule");
                            if (navMeshType == null)
                            {
                                navMeshType = Type.GetType("UnityEngine.AI.NavMesh");
                            }
                            var getAreaFromNameMethod = navMeshType?.GetMethod("GetAreaFromName", BindingFlags.Public | BindingFlags.Static);
                            int areaIndex = 0;
                            if (getAreaFromNameMethod != null)
                            {
                                areaIndex = (int)getAreaFromNameMethod.Invoke(null, new object[] { navigator.DefaultArea });
                            }
                            int newDefaultAreaMask = 1 << areaIndex;
                            defaultAreaMaskField.SetValue(navigator, newDefaultAreaMask);
                            
                            if (debugLogging)
                            {
                                UnityEngine.Debug.Log($"[GrimmNPC Spawn] Updated defaultAreaMask from old DefaultArea '{previousDefaultArea}' " +
                                    $"to new DefaultArea '{navigator.DefaultArea}' (defaultAreaMask={newDefaultAreaMask})");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (debugLogging)
                        {
                            UnityEngine.Debug.LogWarning($"[GrimmNPC Spawn] Failed to update defaultAreaMask after DefaultArea change: {ex.Message}");
                        }
                    }
                }
                
                navigator.MoveTowardsSpeed = BaseNavigator.NavigationSpeed.Normal; // Movement speed preference
                navigator.FaceMoveTowardsTarget = true; // Face target when moving towards it
                // StoppingDistance defaults to 0.5f (good for most cases, can be customized per NPC if needed)
                
                // CRITICAL: Apply area mask and agent type - MUST be set BEFORE navigation operations
                // According to AI_Navigation_Instructional.md line 627-628, these can be set "BEFORE calling Init() or AFTER initialization"
                // Since NPCPlayer.ServerInit() may call Init() before this patch, we set them here (AFTER initialization)
                // Note: NavMeshAgent is accessed through BaseNavigator.Agent property
                // We use reflection to avoid requiring UnityEngine.AIModule reference
                if (npcData.AreaMask > 0)
                {
                    // Use reflection to get Agent property (avoids NavMeshAgent type reference)
                    var navigatorType = navigator.GetType();
                    var agentProperty = navigatorType.GetProperty("Agent");
                    if (agentProperty != null)
                    {
                        object navAgent = agentProperty.GetValue(navigator);
                        if (navAgent != null)
                        {
                            // Initialize cached reflection for NavMeshAgent properties (performance optimization)
                            InitializeAgentReflection();
                            
                            // Use cached PropertyInfo if available, otherwise fall back to runtime lookup
                            // This avoids repeated GetProperty() calls (Harmony mod performance best practice)
                            var agentType = navAgent.GetType();
                            var areaMaskProp = _agentAreaMaskProp ?? agentType.GetProperty("areaMask");
                            var agentTypeIDProp = _agentAgentTypeIDProp ?? agentType.GetProperty("agentTypeID");
                            var updateRotationProp = _agentUpdateRotationProp ?? agentType.GetProperty("updateRotation");
                            var updatePositionProp = _agentUpdatePositionProp ?? agentType.GetProperty("updatePosition");
                            
                            // CRITICAL: Configure Agent properties according to Base Navigation guide
                            // Building blocks always use terrain NavMesh settings (areaMask=1, agentTypeID=-1372625422)
                            // Navigator handles position and rotation updates, not the Agent
                            if (areaMaskProp != null)
                            {
                                areaMaskProp.SetValue(navAgent, npcData.AreaMask);
                            }
                            if (agentTypeIDProp != null)
                            {
                                agentTypeIDProp.SetValue(navAgent, npcData.AgentTypeID);
                            }
                            
                            // CRITICAL: Navigator handles position and rotation, not the Agent
                            // This prevents Agent from interfering with Base Navigation movement
                            // Reference: Base_NavMesh_Complete_Guide.md lines 199-200, 621-623
                            if (updateRotationProp != null)
                            {
                                updateRotationProp.SetValue(navAgent, false);
                            }
                            if (updatePositionProp != null)
                            {
                                updatePositionProp.SetValue(navAgent, false);
                            }
                            
                            // CRITICAL: Also update navMeshQueryFilter if Init() was already called
                            // Init() sets navMeshQueryFilter.agentTypeID from Agent.agentTypeID, but if Init() was called
                            // before we set Agent.agentTypeID, we need to update navMeshQueryFilter manually
                            try
                            {
                                var navMeshQueryFilterField = navigatorType.GetField("navMeshQueryFilter", BindingFlags.NonPublic | BindingFlags.Instance);
                                if (navMeshQueryFilterField != null)
                                {
                                    object navMeshQueryFilter = navMeshQueryFilterField.GetValue(navigator);
                                    if (navMeshQueryFilter != null)
                                    {
                                        var filterType = navMeshQueryFilter.GetType();
                                        var filterAgentTypeIDProp = filterType.GetProperty("agentTypeID");
                                        var filterAreaMaskProp = filterType.GetProperty("areaMask");
                                        
                                        if (filterAgentTypeIDProp != null)
                                        {
                                            filterAgentTypeIDProp.SetValue(navMeshQueryFilter, npcData.AgentTypeID);
                                        }
                                        if (filterAreaMaskProp != null)
                                        {
                                            // Use the updated defaultAreaMask (calculated above)
                                            // Use reflection to avoid requiring UnityEngine.AIModule reference
                                            var navMeshType = Type.GetType("UnityEngine.AI.NavMesh, UnityEngine.AIModule");
                                            if (navMeshType == null)
                                            {
                                                navMeshType = Type.GetType("UnityEngine.AI.NavMesh");
                                            }
                                            var getAreaFromNameMethod = navMeshType?.GetMethod("GetAreaFromName", BindingFlags.Public | BindingFlags.Static);
                                            int areaIndex = 0;
                                            if (getAreaFromNameMethod != null)
                                            {
                                                areaIndex = (int)getAreaFromNameMethod.Invoke(null, new object[] { navigator.DefaultArea });
                                            }
                                            int currentDefaultAreaMask = 1 << areaIndex;
                                            filterAreaMaskProp.SetValue(navMeshQueryFilter, currentDefaultAreaMask);
                                            
                                            if (debugLogging)
                                            {
                                                UnityEngine.Debug.Log($"[GrimmNPC Spawn] Updated navMeshQueryFilter: " +
                                                    $"agentTypeID={npcData.AgentTypeID}, areaMask={currentDefaultAreaMask}");
                                            }
                                        }
                                        else if (debugLogging && filterAgentTypeIDProp != null)
                                        {
                                            UnityEngine.Debug.Log($"[GrimmNPC Spawn] Updated navMeshQueryFilter: " +
                                                $"agentTypeID={npcData.AgentTypeID} (areaMask property not found)");
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                if (debugLogging)
                                {
                                    UnityEngine.Debug.LogWarning($"[GrimmNPC Spawn] Failed to update navMeshQueryFilter: {ex.Message}");
                                }
                            }
                        }
                    }
                }
                
                if (debugLogging)
                {
                    UnityEngine.Debug.Log($"[GrimmNPC Spawn] Configured navigator for {npcData.Name}: " +
                        $"CanUseBaseNav={navigator.CanUseBaseNav}, CanUseNavMesh={navigator.CanUseNavMesh}, " +
                        $"DefaultArea={navigator.DefaultArea}, AreaMask={npcData.AreaMask}, AgentTypeID={npcData.AgentTypeID}");
                }
                
                // NOTE: Navmesh is NOT locked after spawn to allow dynamic switching
                // This enables NPCs to roam freely between monuments, terrain, and bases
                // Dynamic navmesh switching (UpdateNavmeshForCurrentPosition) handles position-based updates
                // This matches the behavior of other plugins (BotReSpawn, FrankensteinPet) that allow
                // seamless navigation across different navmesh types
                npcData.NavmeshLocked = false;
                
                if (debugLogging)
                {
                    UnityEngine.Debug.Log($"[GrimmNPC Spawn] Navmesh unlocked for {npcData.Name} - dynamic switching enabled");
                }
            }
        }
        
        /// <summary>
        /// CRITICAL: Auto-detects navmesh type following the detection pattern:
        /// 1. Check if on monument first
        /// 2. Check if on building block (player structure)
        /// 3. Default based on terrain (TypeNavMesh = 0)
        /// 
        /// Only runs if using default terrain values (AreaMask=1, AgentTypeID=-1372625422).
        /// 
        /// NOTE: This is GrimmNPC's auto-detection feature - the game does NOT auto-detect.
        /// - Game (BradleyAPC): Uses hardcoded `roadSpawned` parameter, does NOT auto-detect
        /// - Plugins (BetterNpc/BossMonster): Intentionally use defaults to trigger auto-detection
        /// - Plugins CAN override: Set AreaMask/AgentTypeID manually to skip auto-detection
        /// </summary>
        private static void DetectAndConfigureNavMesh(CustomNpcData npcData, Vector3 position, bool debugLogging)
        {
            // Step 1: Check if on monument first
            if (DetectAndConfigureMonumentNavMesh(npcData, position, debugLogging))
            {
                return; // Monument detected and configured
            }
            
            // Step 2: Check if on building block (player structure)
            if (IsPositionOnBuildingBlock(position))
            {
                // Building blocks use Base Navigation, but Agent still needs terrain settings
                npcData.AreaMask = 1; // Terrain navmesh (Base Navigation will handle building blocks)
                npcData.AgentTypeID = -1372625422; // Terrain agent type
                
                if (debugLogging)
                {
                    UnityEngine.Debug.Log($"[GrimmNPC Spawn] Auto-detected building block navmesh: " +
                        $"Position={position}, AreaMask=1, AgentTypeID=-1372625422 (Base Navigation enabled)");
                }
                return; // Building block detected and configured
            }
            
            // Step 3: Default to terrain (already set, but log for clarity)
            if (debugLogging)
            {
                UnityEngine.Debug.Log($"[GrimmNPC Spawn] Using default terrain navmesh: " +
                    $"Position={position}, AreaMask=1, AgentTypeID=-1372625422");
            }
        }
        
        /// <summary>
        /// Detects if spawn position is on a monument and automatically configures navmesh settings.
        /// Returns true if monument was detected and configured, false otherwise.
        /// 
        /// CRITICAL: This is GrimmNPC's auto-detection feature (game does NOT auto-detect).
        /// - Game (BradleyAPC): Uses hardcoded `roadSpawned` parameter, does NOT auto-detect navmesh type
        /// - GrimmNPC: Auto-detects navmesh type when defaults are used (AreaMask=1, AgentTypeID=-1372625422)
        /// - Plugins (BetterNpc/BossMonster): Intentionally use defaults to trigger auto-detection
        /// - Plugins CAN override: Set AreaMask/AgentTypeID manually to skip auto-detection
        /// 
        /// Follows the game's exact pattern (like BradleyAPC.SpawnScientist):
        /// 1. Check if position is within monument bounds
        /// 2. Use BaseNavigator.GetNavMeshAgentID("Humanoid") to get agent type (matches game's approach)
        /// 3. Always configure for monument navmesh (areaMask=25, DefaultArea="HumanNPC") if on monument
        /// 4. Does NOT require HasNavmesh==true (works even if navmesh is still building)
        /// </summary>
        private static bool DetectAndConfigureMonumentNavMesh(CustomNpcData npcData, Vector3 position, bool debugLogging)
        {
            try
            {
                if (TerrainMeta.Path == null || TerrainMeta.Path.Monuments == null)
                {
                    if (debugLogging)
                    {
                        UnityEngine.Debug.Log($"[GrimmNPC Spawn] Monument detection: TerrainMeta.Path or Monuments is null");
                    }
                    return false;
                }
                
                foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
                {
                    if (monument == null) continue;
                    
                    // Check if position is within monument bounds
                    if (monument.IsInBounds(position))
                    {
                        // CRITICAL: Follow game's pattern - use BaseNavigator.GetNavMeshAgentID() like BradleyAPC does
                        // For HumanNPCs, game uses "Humanoid" agent type
                        int agentTypeID = BaseNavigator.GetNavMeshAgentID("Humanoid");
                        if (agentTypeID == -1)
                        {
                            // Fallback: try to get from monument navmesh component
                            MonumentNavMesh monumentNavMesh = monument.GetComponentInChildren<MonumentNavMesh>();
                            if (monumentNavMesh != null)
                            {
                                try
                                {
                                    var navMeshType = typeof(MonumentNavMesh);
                                    var indexProperty = navMeshType.GetProperty("NavMeshAgentTypeIndex");
                                    if (indexProperty != null)
                                    {
                                        int agentTypeIndex = (int)indexProperty.GetValue(monumentNavMesh);
                                        var navMeshType2 = Type.GetType("UnityEngine.AI.NavMesh, UnityEngine.AIModule");
                                        if (navMeshType2 == null)
                                        {
                                            navMeshType2 = Type.GetType("UnityEngine.AI.NavMesh");
                                        }
                                        var getSettingsMethod = navMeshType2?.GetMethod("GetSettingsByIndex", 
                                            BindingFlags.Public | BindingFlags.Static, null,
                                            new Type[] { typeof(int) }, null);
                                        if (getSettingsMethod != null)
                                        {
                                            object settings = getSettingsMethod.Invoke(null, new object[] { agentTypeIndex });
                                            if (settings != null)
                                            {
                                                var settingsType = settings.GetType();
                                                var agentTypeIDProp = settingsType.GetProperty("agentTypeID");
                                                if (agentTypeIDProp != null)
                                                {
                                                    agentTypeID = (int)agentTypeIDProp.GetValue(settings);
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    if (debugLogging)
                                    {
                                        UnityEngine.Debug.LogWarning($"[GrimmNPC] Failed to get monument agent type: {ex.Message}");
                                    }
                                }
                            }
                            // Final fallback: default monument agent type
                            if (agentTypeID == -1)
                            {
                                agentTypeID = 0;
                            }
                        }
                        
                        // CRITICAL: For monuments, always use areaMask 25 (monument navmesh area)
                        // Even if navmesh isn't built yet, we configure correctly so it works when built
                        // This matches the game's approach - configure for the environment type, not current navmesh state
                        int areaMask = 25; // Monument navmesh area mask
                        string defaultArea = "HumanNPC"; // Monuments use HumanNPC area (matches game's approach in BradleyAPC)
                        
                        // Configure for monument navmesh
                        npcData.AreaMask = areaMask;
                        npcData.AgentTypeID = agentTypeID;
                        
                        if (debugLogging)
                        {
                            UnityEngine.Debug.Log($"[GrimmNPC Spawn] Auto-detected monument navmesh: " +
                                $"Monument={monument.name}, Position={position}, AreaMask={areaMask}, AgentTypeID={agentTypeID}, " +
                                $"DefaultArea={defaultArea}, HasNavmesh={monument.HasNavmesh}");
                        }
                        
                        return true; // Found monument, configured
                    }
                }
                
                if (debugLogging)
                {
                    UnityEngine.Debug.Log($"[GrimmNPC Spawn] Monument detection: Position {position} is not within any monument bounds");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[GrimmNPC] Error detecting monument navmesh: {ex}");
            }
            
            return false; // No monument detected
        }
        
        /// <summary>
        /// Checks if position is on a building block (player structure).
        /// 
        /// CRITICAL: Matches exact detection logic from BaseNavigator.DetermineNavigationType():
        /// - Uses same LayerMask: 2097152 (building blocks layer = 1 << 21)
        /// - Uses same raycast pattern: position + Vector3.up * navTypeHeightOffset, Vector3.down
        /// - Uses same distance: navTypeDistance (default 1f, configurable via server var)
        /// 
        /// This ensures our detection matches the game's internal detection exactly,
        /// preventing mismatches where we think a position is on building blocks but
        /// the game's DetermineNavigationType() doesn't detect it.
        /// 
        /// Reference: AI_Navigation_Instructional.md lines 54-73
        /// </summary>
        private static bool IsPositionOnBuildingBlock(Vector3 position)
        {
            const int BUILDING_BLOCKS_LAYER = 2097152; // Building blocks layer (1 << 21)
            
            // Get BaseNavigator's navTypeHeightOffset and navTypeDistance (matches game logic)
            float navTypeHeightOffset = GetNavTypeHeightOffset();
            float navTypeDistance = GetNavTypeDistance();
            
            // CRITICAL: Use exact same raycast pattern as BaseNavigator.DetermineNavigationType()
            // Raycast from position + height offset downward with navTypeDistance
            Vector3 raycastOrigin = position + Vector3.up * navTypeHeightOffset;
            
            // Perform raycast matching game's detection logic
            RaycastHit hit;
            if (UnityEngine.Physics.Raycast(raycastOrigin, Vector3.down, out hit, navTypeDistance, BUILDING_BLOCKS_LAYER))
            {
                // Additional validation: ensure hit entity is actually a building block
                // This matches the game's logic which checks for building blocks
                BaseEntity hitEntity = hit.collider.ToBaseEntity();
                if (hitEntity is BuildingBlock || hitEntity is SimpleBuildingBlock)
                {
                    return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Fallback method to detect agent type by sampling navmesh at position.
        /// Returns 0 (default monument agent type) if detection fails.
        /// </summary>
        private static int DetectAgentTypeBySampling(Vector3 position, bool debugLogging)
        {
            // Simplified fallback: return default monument agent type (0)
            // Full navmesh sampling via reflection is complex and error-prone
            // The main detection via MonumentNavMesh should work for most cases
            if (debugLogging)
            {
                UnityEngine.Debug.Log($"[GrimmNPC] Using default monument agent type (0) as fallback");
            }
            return 0; // Default monument agent type
        }
    }
    
    /// <summary>
    /// Patches BaseAIBrain.InitializeAI() to ensure navigator is unpaused after brain initialization.
    /// This fixes the issue where NPCs spawn with paused navigators (Is paused: True) and navigation type None,
    /// preventing them from moving even when destinations are set.
    /// 
    /// The navigator is paused by default and needs to be resumed after the brain initializes.
    /// This patch ensures the navigator is ready to use when the brain starts.
    /// </summary>
    [HarmonyPatch(typeof(BaseAIBrain), nameof(BaseAIBrain.InitializeAI))]
    public class BaseAIBrain_InitializeAI_Patch
    {
        static void Postfix(BaseAIBrain __instance)
        {
            // Only process if this is a custom NPC (check skinID)
            var baseEntity = __instance.GetBaseEntity();
            if (baseEntity == null || baseEntity.skinID != GrimmNPC.CUSTOM_NPC_SKIN_ID)
                return;
            
            try
            {
                var navigator = __instance.Navigator;
                if (navigator == null)
                    return;
                
                // Check if navigator is paused using reflection (paused is a private field)
                var navigatorType = navigator.GetType();
                var pausedField = navigatorType.GetField("paused", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (pausedField != null)
                {
                    bool isPaused = (bool)pausedField.GetValue(navigator);
                    if (isPaused)
                    {
                        // Unpause the navigator by setting paused to false directly
                        // Resume() requires a destination, but if there's no destination yet, we just unpause
                        pausedField.SetValue(navigator, false);
                        
                        var config = GrimmNPC.GetConfig();
                        if (config.EnableDebugLogging)
                        {
                            UnityEngine.Debug.Log($"[GrimmNPC Spawn] Unpaused navigator for custom NPC after brain initialization");
                        }
                    }
                }
                
                // CRITICAL: Ensure navigation type is determined if it's still None
                // The navigation type is determined when SetDestination() is called, but if the navigator
                // is paused, it won't be determined. We need to force it to determine the type.
                var currentNavTypeProperty = navigatorType.GetProperty("CurrentNavigationType");
                if (currentNavTypeProperty != null)
                {
                    var currentNavType = currentNavTypeProperty.GetValue(navigator);
                    // Check if CurrentNavigationType is None (0)
                    if (currentNavType != null && (int)currentNavType == 0)
                    {
                        // Force navigation type determination by calling DetermineNavigationType()
                        // This method determines the navigation type based on the current position
                        var determineNavTypeMethod = navigatorType.GetMethod("DetermineNavigationType", 
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (determineNavTypeMethod != null)
                        {
                            try
                            {
                                var navType = determineNavTypeMethod.Invoke(navigator, null);
                                
                                // Set the navigation type using SetCurrentNavigationType()
                                var setNavTypeMethod = navigatorType.GetMethod("SetCurrentNavigationType",
                                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                if (setNavTypeMethod != null && navType != null)
                                {
                                    setNavTypeMethod.Invoke(navigator, new object[] { navType });
                                }
                                
                                var config = GrimmNPC.GetConfig();
                                if (config.EnableDebugLogging)
                                {
                                    UnityEngine.Debug.Log($"[GrimmNPC Spawn] Determined and set navigation type: {navType} for custom NPC after brain initialization");
                                }
                            }
                            catch (Exception ex)
                            {
                                var config = GrimmNPC.GetConfig();
                                if (config.EnableDebugLogging)
                                {
                                    UnityEngine.Debug.LogWarning($"[GrimmNPC Spawn] Failed to determine navigation type: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[GrimmNPC] Error in InitializeAI patch: {ex}");
            }
        }
    }
    
    /// <summary>
    /// Patches GameManager.server.CreateEntity to mark custom NPCs
    /// This allows us to identify custom NPCs before ServerInit
    /// </summary>
    [HarmonyPatch(typeof(GameManager), "CreateEntity", new Type[] { typeof(string), typeof(Vector3), typeof(Quaternion), typeof(bool) })]
    public class GameManager_CreateEntity_Patch
    {
        static void Postfix(BaseNetworkable __result, string strPrefab)
        {
            // Check if this is a custom NPC spawn request
            // You can identify this by checking if strPrefab matches your custom prefab
            // or by checking some other marker
            
            // For now, we'll identify custom NPCs by skinID after ServerInit
            // This patch is here for future optimization if needed
        }
    }

    /// <summary>
    /// 💣 BOMBER: Spawns a timed explosive attached to the bomber NPC (like NpcSpawn line 1785).
    /// </summary>
    static class BomberHelper
    {
        public static void SpawnBomberExplosive(ScientistNPC npc, CustomNpcData npcData, bool debugLogging)
    {
        if (npc == null || npcData == null || npc.IsDestroyed) return;

        // Spawn timed explosive entity attached to NPC
        RFTimedExplosive explosive = GameManager.server.CreateEntity("assets/prefabs/tools/c4/explosive.timed.deployed.prefab", 
            npc.transform.position, Quaternion.identity) as RFTimedExplosive;
        if (explosive == null) return;

        explosive.enableSaving = false;
        explosive.timerAmountMin = float.PositiveInfinity;
        explosive.timerAmountMax = float.PositiveInfinity;
        explosive.transform.localPosition = new Vector3(0f, 1f, 0f);
        explosive.SetParent(npc);
        explosive.Spawn();
        npcData.BomberTimedExplosive = explosive;

        if (debugLogging)
        {
            UnityEngine.Debug.Log($"[GrimmNPC Bomber] Spawned timed explosive for bomber NPC: {npcData.Name ?? "Unknown"}");
        }
        }
    }
}
