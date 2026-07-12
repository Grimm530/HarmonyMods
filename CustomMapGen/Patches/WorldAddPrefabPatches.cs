using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace CustomMapGen.Patches
{
    /// <summary>
    /// 1) Prevents car wreck monuments from being added when RemoveCarWrecks is enabled.
    /// 2) When AllowBanditCamp is false, prevents bandit town monument from spawning (compound in center acts as combined outpost/bandit).
    /// 3) Redirects outpost (and bandit camp if allowed) to map center when TrySpawningOutpostInCenter is enabled
    ///    by modifying the position argument — no second outpost is created; the one the game places is moved.
    /// 4) Monument Swapping: replace vanilla monument with custom .map prefab from maps/prefabs (e.g. harbor_1.prefab.map) when enabled.
    /// </summary>
    [HarmonyPatch(typeof(World), nameof(World.AddPrefab), typeof(string), typeof(Prefab), typeof(Vector3), typeof(Quaternion), typeof(Vector3))]
    public static class World_AddPrefab_Patch
    {
        /// <summary>When we block compound/outpost not at center, we save its position so the next relocated small/large monument can use this slot.</summary>
        private static Vector3? _blockedOutpostPosition;
        private static bool _spawningSwapRows;
        private static bool _liveOutpostSwapApplied;
        private static bool _centerOutpostPlaced;

        internal static bool LiveOutpostSwapApplied => _liveOutpostSwapApplied;

        internal static void ResetLiveOutpostSwapState()
        {
            _spawningSwapRows = false;
            _liveOutpostSwapApplied = false;
            _centerOutpostPlaced = false;
        }

        private const float CenterOutpostThreshold = 80f;

        private static bool IsCompoundOrOutpost(string nameLower) =>
            nameLower.Contains("compound") || nameLower.Contains("outpost");

        private static bool IsOffCenterOutpostPosition(Vector3 position, out Vector3 mapCenter)
        {
            mapCenter = TerrainMeta.Position + TerrainMeta.Size * 0.5f;
            float dx = Math.Abs(position.x - mapCenter.x);
            float dz = Math.Abs(position.z - mapCenter.z);
            return dx > CenterOutpostThreshold || dz > CenterOutpostThreshold;
        }

        static bool Prefix(string category, ref Prefab prefab, ref Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (!CustomMapGen.IsCustomMapGenEnabled())
                return true;
            if (_spawningSwapRows)
                return true;
            var config = CustomMapGen.Instance.GetConfig();
            if (config.DisableWorldAddPrefabPatch)
                return true;
            if (prefab?.Name == null)
                return true;

            string nameLower = prefab.Name.ToLowerInvariant();

            // Block powerline poles when powerlines are disabled (PlacePowerlineObjects still runs; clear list + block here as safety)
            if (!config.Powerlines &&
                (nameLower.Contains("powerline_pole") || category != null && category.IndexOf("owerline", System.StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return false;
            }

            // Block zipline prefabs (launch/arrival points) when ziplines are disabled
            if (!config.Ziplines && nameLower.Contains("zipline"))
            {
                return false;
            }

            // Block any prefab matching BlockedPrefabs (e.g. powerline_pole, coastal_rocks) at AddPrefab time
            if (config.BlockedPrefabs != null && config.BlockedPrefabs.Count > 0)
            {
                foreach (var blocked in config.BlockedPrefabs)
                {
                    if (!string.IsNullOrEmpty(blocked) && nameLower.Contains(blocked.ToLowerInvariant()))
                    {
                        return false;
                    }
                }
            }

            if (category == "Monument")
            {
                // Capture original position for debug log (generated vs relocated)
                Vector3 originalPosition = position;

                // The center outpost swap is handled live below so fresh procgen spawns the custom rows immediately.

                // Center outpost: block off-center spawns, swap center monument into that slot, always place one at center.
                if (config.TrySpawningOutpostInCenter && IsCompoundOrOutpost(nameLower))
                {
                    if (TerrainMeta.Path != null && TerrainMeta.HeightMap != null)
                    {
                        if (IsOffCenterOutpostPosition(position, out Vector3 mapCenter))
                        {
                            if (config.UseBlockedOutpostSlotForRelocation)
                                _blockedOutpostPosition = position;
                            if (config.DebugLogging)
                            {
                                float dx = Math.Abs(position.x - mapCenter.x);
                                float dz = Math.Abs(position.z - mapCenter.z);
                                UnityEngine.Debug.Log($"[CustomMapGen] Blocking compound/outpost not at center: {prefab.Name} at {position} (center={mapCenter}, dx={dx}, dz={dz}) — moving center monument to this slot and placing outpost at center.");
                            }
                            EnsureCenterOutpostPlaced(config, position);
                            return false;
                        }

                        if (_centerOutpostPlaced)
                        {
                            if (config.DebugLogging)
                                UnityEngine.Debug.Log($"[CustomMapGen] Blocking duplicate compound/outpost AddPrefab (center outpost already placed): {prefab.Name} at {position}");
                            return false;
                        }

                        // Game is placing at center: clear anything already there, then allow this placement.
                        Vector3 centerPos = GetCenterOutpostPositionDry(config.DebugLogging);
                        if (World.Serialization != null)
                            PostSaveSwap.MoveMonumentsAtCenterToNewPosition(World.Serialization, centerPos, config.MinMonumentDistance, config.DebugLogging, swapTargetPosition: null);
                        RemoveMonumentsAtCenter(centerPos, config.DebugLogging);
                        _centerOutpostPlaced = true;
                    }
                }

                // Debug: log every outpost/bandit/compound monument AddPrefab
                if (config.DebugLogging && (nameLower.Contains("outpost") || nameLower.Contains("bandit") || nameLower.Contains("compound")))
                    UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] AddPrefab Monument (outpost/bandit/compound): {prefab.Name} at {position} (TrySpawningOutpostInCenter={config.TrySpawningOutpostInCenter})");

                // Block car wrecks if configured
                if (config.RemoveCarWrecks &&
                    (nameLower.Contains("wreck") || nameLower.Contains("vehicle_wreck") || nameLower.Contains("car_wreck")))
                {
                    UnityEngine.Debug.Log($"[CustomMapGen] Skipping car wreck monument: {prefab.Name}");
                    return false;
                }

                // Block bandit town when using compound as combined outpost/bandit (AllowBanditCamp = false).
                // If TrySpawningOutpostInCenter is true, spawn the center safe zone at map center. Game uses compound.prefab
                // at center (not outpost); so look for "outpost" first, then "compound" in monument/medium folder.
                if (!config.AllowBanditCamp && (nameLower.Contains("bandit") || prefab.Name.IndexOf("bandit_town", System.StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    UnityEngine.Debug.Log($"[CustomMapGen] Skipping bandit town monument (AllowBanditCamp = false): {prefab.Name}");
                    Vector3 banditSlotPos = position;
                    if (config.TrySpawningOutpostInCenter && TerrainMeta.Path != null && TerrainMeta.HeightMap != null)
                    {
                        bool slotFilled = EnsureCenterOutpostPlaced(config, banditSlotPos);
                        if (!slotFilled && config.FillBanditSlotWithMonument)
                            TrySpawnMonumentInBanditSlot(banditSlotPos, config.DebugLogging);
                    }
                    return false;
                }

                // Redirect outpost/bandit prefab names to map center (compound is handled in the block above).
                if (config.TrySpawningOutpostInCenter &&
                    !nameLower.Contains("compound") &&
                    (nameLower.Contains("outpost") || nameLower.Contains("bandit")))
                {
                    if (TerrainMeta.Path != null && TerrainMeta.HeightMap != null)
                    {
                        if (_centerOutpostPlaced)
                        {
                            if (config.DebugLogging)
                                UnityEngine.Debug.Log($"[CustomMapGen] Blocking duplicate outpost/bandit redirect (center outpost already placed): {prefab.Name}");
                            return false;
                        }
                        Vector3 centerPos = GetCenterOutpostPositionDry(config.DebugLogging);
                        if (World.Serialization != null)
                            PostSaveSwap.MoveMonumentsAtCenterToNewPosition(World.Serialization, centerPos, config.MinMonumentDistance, config.DebugLogging, swapTargetPosition: position);
                        RemoveMonumentsAtCenter(centerPos, config.DebugLogging);
                        position = centerPos;
                        _centerOutpostPlaced = true;
                        if (config.DebugLogging)
                            UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] Outpost/bandit moved to map center: {position}");
                    }
                    else if (config.DebugLogging)
                        UnityEngine.Debug.Log("[CustomMapGen] [DEBUG] Outpost redirect skipped: TerrainMeta.Path or HeightMap is null");
                }

                // Large monuments (water treatment, airfield, etc.) that spawn at/near center — relocate to blocked outpost slot
                // (Compound is placed at center when bandit is blocked; large monuments are placed AFTER, so they weren't caught by MoveMonumentsAtCenterToNewPosition.)
                if (config.TrySpawningOutpostInCenter && config.UseBlockedOutpostSlotForRelocation && _blockedOutpostPosition.HasValue &&
                    TerrainMeta.Path != null && TerrainMeta.HeightMap != null)
                {
                    if (PostSaveSwap.IsLargeMonument(nameLower) &&
                        !nameLower.Contains("compound") && !nameLower.Contains("outpost") && !nameLower.Contains("bandit"))
                    {
                        Vector3 centerPos = TerrainMeta.Position + TerrainMeta.Size * 0.5f;
                        float dx = Math.Abs(position.x - centerPos.x);
                        float dz = Math.Abs(position.z - centerPos.z);
                        const float centerThreshold = 150f; // Water treatment ~80m radius; use 150 to catch overlap
                        if (dx <= centerThreshold && dz <= centerThreshold)
                        {
                            Vector3 newPos = _blockedOutpostPosition.Value;
                            newPos.y = TerrainMeta.HeightMap.GetHeight(newPos);
                            if (config.DebugLogging)
                                UnityEngine.Debug.Log($"[CustomMapGen] Relocated large monument at center {prefab.Name} from ({position.x:F0},{position.z:F0}) to blocked outpost slot ({newPos.x:F0},{newPos.z:F0}).");
                            position = newPos;
                            _blockedOutpostPosition = null;
                        }
                    }
                }

                // Small monuments must not spawn within MinDistanceSmallToLargeMonument of a large monument (e.g. Large Barn not under Outpost corner)
                if (config.MinDistanceSmallToLargeMonument > 0 && !PostSaveSwap.IsLargeMonument(nameLower) &&
                    World.Serialization != null && TerrainMeta.Path != null && TerrainMeta.HeightMap != null)
                {
                    object worldObj = PostSaveSwap.GetWorldFromSerialization(World.Serialization);
                    var prefabsList = worldObj != null ? PostSaveSwap.GetPrefabsListFromWorld(worldObj) : null;
                    if (prefabsList != null && prefabsList.Count > 0)
                    {
                        float minFromLarge = (float)config.MinDistanceSmallToLargeMonument;
                        float minFromOthers = (float)Math.Max(1, config.MinMonumentDistance);
                        bool tooCloseToLarge = false;
                        foreach (var p in prefabsList)
                        {
                            if (p == null) continue;
                            if (!PostSaveSwap.TryGetPrefabId(p, out uint pid)) continue;
                            string pname = pid != 0 ? StringPool.Get(pid) : null;
                            if (string.IsNullOrEmpty(pname) || !PostSaveSwap.IsLargeMonument(pname.ToLowerInvariant())) continue;
                            float px = PostSaveSwap.GetPrefabPositionComponent(p, "x");
                            float pz = PostSaveSwap.GetPrefabPositionComponent(p, "z");
                            float dx = position.x - px;
                            float dz = position.z - pz;
                            float distSq = dx * dx + dz * dz;
                            // Large monuments have footprint radii (compound/outpost 120m, water_treatment/airfield 80m, sewer/radtown 50m, etc.)
                            float effectiveRadius = PostSaveSwap.GetEffectiveRadiusForLargeMonument(pname);
                            float minDistFromLarge = effectiveRadius + minFromLarge;
                            if (distSq < minDistFromLarge * minDistFromLarge)
                            {
                                tooCloseToLarge = true;
                                break;
                            }
                        }
                        if (tooCloseToLarge)
                        {
                            Vector3? newPos = null;
                            bool usedBlockedSlot = false;
                            if (config.UseBlockedOutpostSlotForRelocation && _blockedOutpostPosition.HasValue && IsValidPositionForMonument(_blockedOutpostPosition.Value, prefabsList, null, minFromLarge, minFromOthers))
                            {
                                Vector3 blockedPos = _blockedOutpostPosition.Value;
                                blockedPos.y = TerrainMeta.HeightMap.GetHeight(blockedPos);
                                newPos = blockedPos;
                                _blockedOutpostPosition = null;
                                usedBlockedSlot = true;
                                if (config.DebugLogging)
                                    UnityEngine.Debug.Log($"[CustomMapGen] Relocated small monument {prefab.Name} from ({position.x:F0},{position.z:F0}) to ({newPos.Value.x:F0},{newPos.Value.z:F0}) (blocked outpost slot).");
                            }
                            if (!newPos.HasValue)
                                newPos = PostSaveSwap.FindValidPositionForSmallMonument(position, prefabsList, null, minFromLarge, minFromOthers, config.DebugLogging);
                            if (newPos.HasValue)
                            {
                                if (!usedBlockedSlot && config.DebugLogging)
                                    UnityEngine.Debug.Log($"[CustomMapGen] Relocated small monument {prefab.Name} from ({position.x:F0},{position.z:F0}) to ({newPos.Value.x:F0},{newPos.Value.z:F0}) (MinDistanceSmallToLargeMonument={config.MinDistanceSmallToLargeMonument}m).");
                                position = newPos.Value;
                            }
                        }
                    }
                }

                // Debug: list every monument placed with location and whether it was moved
                if (config.DebugLogging)
                {
                    string shortName = GetShortMonumentName(prefab.Name);
                    string displayName = GetMonumentDisplayName(shortName, prefab.Name);
                    float movedDistSq = (position - originalPosition).sqrMagnitude;
                    bool moved = movedDistSq > 1f;
                    UnityEngine.Debug.Log($"[CustomMapGen] Monument placed: {displayName} at ({position.x:F0}, {position.y:F0}, {position.z:F0}) {(moved ? $"(relocated from ({originalPosition.x:F0}, {originalPosition.z:F0}))" : "(generated position)")}");
                }
            }

            return true;
        }

        /// <summary>
        /// With TrySpawningOutpostInCenter: move monument(s) at map center to <paramref name="relocateCenterMonumentsTo"/>,
        /// then place custom or vanilla compound at center. Idempotent per generation.
        /// </summary>
        /// <returns>true if a center monument was moved into the relocate slot.</returns>
        private static bool EnsureCenterOutpostPlaced(MapGenConfig config, Vector3 relocateCenterMonumentsTo)
        {
            if (_centerOutpostPlaced)
                return false;
            if (!config.TrySpawningOutpostInCenter || TerrainMeta.Path == null || TerrainMeta.HeightMap == null)
                return false;

            Vector3 centerPos = GetCenterOutpostPositionDry(config.DebugLogging);
            bool slotFilled = false;
            if (World.Serialization != null)
                slotFilled = PostSaveSwap.MoveMonumentsAtCenterToNewPosition(World.Serialization, centerPos, config.MinMonumentDistance, config.DebugLogging, swapTargetPosition: relocateCenterMonumentsTo);
            RemoveMonumentsAtCenter(centerPos, config.DebugLogging);

            bool spawnedCustom = TrySpawnLiveOutpostSwap(centerPos, Quaternion.identity, config);
            if (!spawnedCustom)
            {
                Prefab centerPrefab = ResolveCenterCompoundPrefab(config.DebugLogging);
                if (centerPrefab != null)
                {
                    Vector3 centerScale = centerPrefab.Object != null ? centerPrefab.Object.transform.localScale : Vector3.one;
                    World.AddPrefab("Monument", centerPrefab, centerPos, Quaternion.identity, centerScale);
                    if (config.DebugLogging)
                        UnityEngine.Debug.Log($"[CustomMapGen] Spawned center outpost at map center ({centerPrefab.Name}): {centerPos}");
                }
                else if (config.DebugLogging)
                    UnityEngine.Debug.Log("[CustomMapGen] Could not resolve compound prefab for center outpost placement.");
            }

            _centerOutpostPlaced = true;
            return slotFilled;
        }

        private static Prefab ResolveCenterCompoundPrefab(bool debugLogging)
        {
            Prefab[] mediumPrefabs = Prefab.Load("assets/bundled/prefabs/autospawn/monument/medium", null, null, useProbabilities: false, useWorldConfig: false);
            Prefab centerPrefab = null;
            if (mediumPrefabs != null)
            {
                if (debugLogging)
                {
                    var names = new System.Text.StringBuilder();
                    foreach (var p in mediumPrefabs)
                        names.Append(p?.Name ?? "null").Append(", ");
                    UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] monument/medium prefabs: {names}");
                }
                foreach (var p in mediumPrefabs)
                {
                    if (p?.Name == null || p.Object == null) continue;
                    if (p.Name.IndexOf("outpost", StringComparison.OrdinalIgnoreCase) >= 0)
                    { centerPrefab = p; break; }
                }
                if (centerPrefab == null)
                    foreach (var p in mediumPrefabs)
                    {
                        if (p?.Name == null || p.Object == null) continue;
                        if (p.Name.IndexOf("compound", StringComparison.OrdinalIgnoreCase) >= 0)
                        { centerPrefab = p; break; }
                    }
            }
            if (centerPrefab == null)
            {
                Prefab[] direct = Prefab.Load("assets/bundled/prefabs/autospawn/monument/medium/compound.prefab", null, null, useProbabilities: false, useWorldConfig: false);
                if (direct != null && direct.Length > 0 && direct[0]?.Object != null)
                    centerPrefab = direct[0];
            }
            return centerPrefab;
        }

        private static bool TrySpawnLiveOutpostSwap(Vector3 centerPos, Quaternion centerRotation, MapGenConfig config)
        {
            if (_liveOutpostSwapApplied || config?.SwapMonuments == null || !config.SwapMonuments.Enabled)
                return false;

            string folder = !string.IsNullOrEmpty(config.SwapMonuments.CustomPrefabsFolder)
                ? Path.Combine(Environment.CurrentDirectory, config.SwapMonuments.CustomPrefabsFolder)
                : Path.Combine(Environment.CurrentDirectory, "maps/prefabs");
            if (!Directory.Exists(folder))
                return false;

            string mapPath = null;
            string[] files = Directory.GetFiles(folder, "*.map");
            foreach (string file in files)
            {
                string shortName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file));
                if (shortName.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    shortName = shortName.Substring(0, shortName.Length - 7);
                if (string.Equals(shortName, "outpost", StringComparison.OrdinalIgnoreCase))
                {
                    mapPath = file;
                    break;
                }
            }
            if (string.IsNullOrEmpty(mapPath))
                return false;

            try
            {
                var swapMap = new WorldSerialization();
                swapMap.Load(mapPath);
                object swapWorld = PostSaveSwap.GetWorldFromSerialization(swapMap);
                IList swapPrefabs = PostSaveSwap.GetPrefabsListFromWorld(swapWorld);
                if (swapPrefabs == null || swapPrefabs.Count == 0)
                    return false;

                PostSaveSwap.LogSwapMapPrefabInventory(swapPrefabs, mapPath);

                float heightOffset = config.SwapMonuments.PlacementHeightOffset;
                object startPos = MapHandlerReflection.NewVector3(centerPos.x, centerPos.y + heightOffset, centerPos.z);
                Vector3 euler = centerRotation.eulerAngles;
                object startRot = MapHandlerReflection.NewVector3(euler.x, euler.y, euler.z);
                bool useOrigin = config.SwapMonuments.UseMapOriginAsPlacementReference;
                IList created = MapHandlerReflection.CreatePrefabFromMap(startPos, startRot, swapPrefabs, useOrigin);
                if (created == null || created.Count == 0)
                    return false;

                int serializedOnly = 0;
                int spawned = 0;
                _spawningSwapRows = true;
                SwapSpawnTracking.BeginTracking(Path.GetFileName(mapPath), created);
                try
                {
                    foreach (object row in created)
                    {
                        if (row == null || !PostSaveSwap.TryGetPrefabId(row, out uint id) || id == 0)
                            continue;

                        string rowCategory = Convert.ToString(PostSaveSwap.GetPrefabMember(row, "category"));
                        if (string.IsNullOrEmpty(rowCategory))
                            rowCategory = "Monument";
                        Vector3 rowPos = GetPrefabVector(row, "position", Vector3.zero);
                        Quaternion rowRot = Quaternion.Euler(GetPrefabVector(row, "rotation", Vector3.zero));
                        Vector3 rowScale = GetPrefabVector(row, "scale", Vector3.one);
                        Prefab rowPrefab = Prefab.Load(id);
                        if (rowPrefab?.Object == null)
                        {
                            World.Serialization?.AddPrefab(rowCategory, id, rowPos, rowRot, rowScale);
                            serializedOnly++;
                            continue;
                        }

                        World.AddPrefab(rowCategory, rowPrefab, rowPos, rowRot, rowScale);
                        spawned++;
                    }
                }
                finally
                {
                    _spawningSwapRows = false;
                    SwapSpawnTracking.EndTrackingAndLog("LiveProcgenSwap");
                }

                _liveOutpostSwapApplied = true;
                _centerOutpostPlaced = true;
                if (config.DebugLogging)
                    UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] Live outpost swap: spawned {spawned} prefab row(s), serializedOnly={serializedOnly}, source={mapPath}");
                return true;
            }
            catch (Exception ex)
            {
                _spawningSwapRows = false;
                UnityEngine.Debug.LogWarning("[CustomMapGen] Live outpost swap failed; falling back to vanilla compound. " + ex.Message);
                return false;
            }
        }

        private static Vector3 GetPrefabVector(object prefabRow, string memberName, Vector3 fallback)
        {
            object vector = PostSaveSwap.GetPrefabMember(prefabRow, memberName);
            if (vector == null)
                return fallback;

            return new Vector3(
                GetVectorComponent(vector, "x", fallback.x),
                GetVectorComponent(vector, "y", fallback.y),
                GetVectorComponent(vector, "z", fallback.z));
        }

        private static float GetVectorComponent(object vector, string axis, float fallback)
        {
            if (vector == null)
                return fallback;
            var type = vector.GetType();
            var field = type.GetField(axis);
            if (field != null)
            {
                object value = field.GetValue(vector);
                if (value != null) return Convert.ToSingle(value);
            }
            var prop = type.GetProperty(axis);
            if (prop != null)
            {
                object value = prop.GetValue(vector);
                if (value != null) return Convert.ToSingle(value);
            }
            return fallback;
        }

        /// <summary>Short name from prefab path (e.g. stables_b.prefab -> stables_b).</summary>
        private static string GetShortMonumentName(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath)) return "?";
            int lastSlash = prefabPath.LastIndexOf('/');
            string name = lastSlash >= 0 ? prefabPath.Substring(lastSlash + 1) : prefabPath;
            if (name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 7);
            return name;
        }

        /// <summary>In-game display name where prefab name differs (path distinguishes e.g. medium/radtown_small_3 = Sewer Branch, roadside/radtown_1 = Radtown).</summary>
        private static string GetMonumentDisplayName(string shortName, string fullPath)
        {
            if (string.IsNullOrEmpty(shortName)) return "?";
            string pathLower = (fullPath ?? "").ToLowerInvariant();
            string shortLower = shortName.ToLowerInvariant();
            // monument/medium/radtown_small_3.prefab = Sewer Branch (in-game); roadside/radtown_1 = Rad Town
            if (pathLower.IndexOf("monument/medium", StringComparison.OrdinalIgnoreCase) >= 0 && shortLower.Contains("radtown_small"))
                return "Sewer Branch";
            if (pathLower.IndexOf("roadside", StringComparison.OrdinalIgnoreCase) >= 0 && shortLower == "radtown_1")
                return "Rad Town";
            return shortName;
        }

        /// <summary>True if position is valid for a small monument: dry and at least minFromLarge from large monuments (plus their radius), minFromOthers from all others.</summary>
        private static bool IsValidPositionForMonument(Vector3 cand, IList prefabsList, object currentPrefab, float minFromLarge, float minFromOthers)
        {
            if (TerrainMeta.HeightMap == null || prefabsList == null) return false;
            cand.y = TerrainMeta.HeightMap.GetHeight(cand);
            if (TerrainMeta.WaterMap != null && cand.y < TerrainMeta.WaterMap.GetHeight(cand) - 0.1f)
                return false;
            foreach (var other in prefabsList)
            {
                if (other == currentPrefab) continue;
                float ox = PostSaveSwap.GetPrefabPositionComponent(other, "x");
                float oy = PostSaveSwap.GetPrefabPositionComponent(other, "y");
                float oz = PostSaveSwap.GetPrefabPositionComponent(other, "z");
                float d = (cand - new Vector3(ox, oy, oz)).magnitude;
                string oname = null;
                if (PostSaveSwap.TryGetPrefabId(other, out uint oid))
                    oname = oid != 0 ? StringPool.Get(oid) : null;
                bool otherIsLarge = !string.IsNullOrEmpty(oname) && PostSaveSwap.IsLargeMonument(oname.ToLowerInvariant());
                float effectiveRadius = !string.IsNullOrEmpty(oname) ? PostSaveSwap.GetEffectiveRadiusForLargeMonument(oname) : 0f;
                float requiredDist = effectiveRadius + minFromLarge;
                if (otherIsLarge && d < requiredDist) return false;
                if (d < minFromOthers) return false;
            }
            return true;
        }

        /// <summary>Find a dry position near map center for the center outpost. Newer Rust/staging can have lake at center for same seed; avoid placing outpost in water.</summary>
        private static Vector3 GetCenterOutpostPositionDry(bool debugLogging)
        {
            Vector3 center = TerrainMeta.Position + TerrainMeta.Size * 0.5f;
            center.y = TerrainMeta.HeightMap.GetHeight(center);
            if (TerrainMeta.WaterMap == null)
                return center;
            float waterY = TerrainMeta.WaterMap.GetHeight(center);
            const float dryTolerance = 0.1f;
            if (center.y >= waterY - dryTolerance)
                return center;
            if (debugLogging)
                UnityEngine.Debug.Log($"[CustomMapGen] Map center is in water (terrain={center.y:F1}, water={waterY:F1}); searching for dry position.");
            const float step = 80f;
            const float maxRadius = 800f;
            for (float r = step; r <= maxRadius; r += step)
            {
                int steps = Mathf.Max(1, Mathf.RoundToInt((2f * (float)Math.PI * r) / step));
                for (int i = 0; i < steps; i++)
                {
                    float angle = (float)i / steps * 2f * (float)Math.PI;
                    Vector3 p = center + new Vector3((float)Math.Cos(angle) * r, 0f, (float)Math.Sin(angle) * r);
                    p.y = TerrainMeta.HeightMap.GetHeight(p);
                    float wY = TerrainMeta.WaterMap.GetHeight(p);
                    if (p.y >= wY - dryTolerance)
                    {
                        if (debugLogging)
                            UnityEngine.Debug.Log($"[CustomMapGen] Using dry position at ({p.x:F0}, {p.z:F0}), terrain={p.y:F1}, water={wY:F1}, dist={r:F0}m from center.");
                        return p;
                    }
                }
            }
            if (debugLogging)
                UnityEngine.Debug.Log("[CustomMapGen] No dry position found within " + maxRadius + "m; using center anyway.");
            return center;
        }

        /// <summary>Remove any monument at map center (e.g. oasis) so the center outpost doesn't spawn inside it. Staging/newer Rust can place oases at center.</summary>
        private static void RemoveMonumentsAtCenter(Vector3 centerPos, bool debugLogging)
        {
            var monumentsAtCenter = TerrainPathAccess.GetMonuments(TerrainMeta.Path);
            if (monumentsAtCenter == null || monumentsAtCenter.Count == 0)
                return;
            const float centerRadius = 120f; // Oases can be large; clear enough radius for outpost
            var toRemove = new List<MonumentInfo>();
            foreach (var m in monumentsAtCenter)
            {
                if (m == null || m.gameObject == null) continue;
                float dx = m.transform.position.x - centerPos.x;
                float dz = m.transform.position.z - centerPos.z;
                if (dx * dx + dz * dz <= centerRadius * centerRadius)
                    toRemove.Add(m);
            }
            foreach (var m in toRemove)
            {
                monumentsAtCenter.Remove(m);
                if (m.gameObject != null)
                    UnityEngine.Object.Destroy(m.gameObject);
                if (debugLogging)
                    UnityEngine.Debug.Log($"[CustomMapGen] Removed monument at center to reserve for outpost: {m.name} (Type={m.Type})");
            }
        }

        /// <summary>Fill the bandit slot (when bandit is blocked) with another monument — e.g. gas station, supermarket — so we don't lose a monument slot.</summary>
        private static void TrySpawnMonumentInBanditSlot(Vector3 banditSlotPos, bool debugLogging)
        {
            Prefab[] roadside = Prefab.Load("assets/bundled/prefabs/autospawn/monument/roadside", null, null, useProbabilities: false, useWorldConfig: false);
            var allowed = new List<Prefab>();
            if (roadside != null)
            {
                foreach (var p in roadside)
                {
                    if (p?.Name == null || p.Object == null) continue;
                    string n = p.Name.ToLowerInvariant();
                    if (n.IndexOf("compound", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("outpost", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("bandit", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;
                    allowed.Add(p);
                }
            }
            if (allowed.Count == 0)
            {
                Prefab[] small = Prefab.Load("assets/bundled/prefabs/autospawn/monument/small", null, null, useProbabilities: false, useWorldConfig: false);
                if (small != null)
                {
                    foreach (var p in small)
                    {
                        if (p?.Name == null || p.Object == null) continue;
                        string n = p.Name.ToLowerInvariant();
                        if (n.IndexOf("compound", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("outpost", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("bandit", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;
                        allowed.Add(p);
                    }
                }
            }
            if (allowed.Count == 0)
            {
                if (debugLogging)
                    UnityEngine.Debug.Log("[CustomMapGen] FillBanditSlotWithMonument: no roadside/small monument prefabs available (excluding compound/outpost/bandit).");
                return;
            }
            // Deterministic pick from seed + position so same map seed gives same fill
            uint seed = (uint)((int)World.Seed + (int)banditSlotPos.x + (int)banditSlotPos.z);
            int idx = (int)(seed % (uint)allowed.Count);
            Prefab fillPrefab = allowed[idx];
            Vector3 pos = banditSlotPos;
            if (TerrainMeta.HeightMap != null)
                pos.y = TerrainMeta.HeightMap.GetHeight(pos);
            Vector3 scale = fillPrefab.Object != null ? fillPrefab.Object.transform.localScale : Vector3.one;
            World.AddPrefab("Monument", fillPrefab, pos, Quaternion.identity, scale);
            if (debugLogging)
                UnityEngine.Debug.Log($"[CustomMapGen] Filled bandit slot with {GetMonumentDisplayName(GetShortMonumentName(fillPrefab.Name), fillPrefab.Name)} at ({pos.x:F0},{pos.y:F0},{pos.z:F0}).");
        }
    }
}
