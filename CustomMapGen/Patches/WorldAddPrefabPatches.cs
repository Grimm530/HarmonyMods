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
    /// 4) Monument Swapping: after vanilla places a monument (terrain + fog/bushes), overlay extras
    ///    from maps/prefabs/*.map without applying terrain stamps again.
    /// </summary>
    [HarmonyPatch(typeof(World), nameof(World.AddPrefab), typeof(string), typeof(Prefab), typeof(Vector3), typeof(Quaternion), typeof(Vector3))]
    public static class World_AddPrefab_Patch
    {
        /// <summary>When we block compound/outpost not at center, we save its position so the next relocated small/large monument can use this slot.</summary>
        private static Vector3? _blockedOutpostPosition;
        private static Vector3? _centerOutpostPosition;
        private static Vector3? _centerOutpostEntrance;
        private static bool _spawningSwapRows;
        private static bool _liveOutpostSwapApplied;
        private static readonly HashSet<string> _liveSwappedMonumentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Custom .map extras sit on vanilla monument terrain. World.SpawnPrefab otherwise
        /// applies TerrainPlacement/Modifiers again and recarves a hard step around the pad.
        /// </summary>
        internal static bool SkipTerrainStampsOnSpawn;

        /// <summary>Must match <see cref="GenerateDungeonGrid.CellSize"/> / <see cref="DungeonGridInfo.CellSize"/>.</summary>
        internal const int DungeonGridCellSize = 216;
        private const float CenterSlotThreshold = 80f;
        /// <summary>Train yard / other large monuments placed after the center outpost can overlap its dungeon cell. 150m was too small (trainyard sat 247m away).</summary>
        private const float LargeMonumentCenterOverlap = 280f;

        internal static bool LiveOutpostSwapApplied => _liveOutpostSwapApplied;

        internal static bool WasLiveMonumentSwapApplied(string swapKey)
        {
            return !string.IsNullOrEmpty(swapKey) && _liveSwappedMonumentKeys.Contains(PostSaveSwap.NormalizeSwapKey(swapKey));
        }

        /// <summary>Cached center-outpost origin after dungeon-grid snap (null until first placement query).</summary>
        internal static Vector3? CenterOutpostPositionIfCached => _centerOutpostPosition;

        /// <summary>Expected dungeon-door world XZ for the live-swapped center outpost.</summary>
        internal static Vector3? CenterOutpostEntranceIfCached => _centerOutpostEntrance;

        internal static void ResetLiveOutpostSwapState()
        {
            _spawningSwapRows = false;
            SkipTerrainStampsOnSpawn = false;
            _liveOutpostSwapApplied = false;
            _liveSwappedMonumentKeys.Clear();
            _centerOutpostPosition = null;
            _centerOutpostEntrance = null;
        }

        /// <summary>Dungeon-grid-snapped center outpost origin (cached after first query).</summary>
        internal static Vector3 GetCenterOutpostPositionForSystems(bool debugLogging)
        {
            return GetCenterOutpostPositionDry(debugLogging);
        }

        static bool Prefix(string category, ref Prefab prefab, ref Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (!CustomMapGen.IsCustomMapGenEnabled())
                return true;
            if (CustomMapGen.IsLoadingExistingMap)
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

                // Block compound/outpost when NOT at the intended center slot — we only want the one we place there (avoids two outposts).
                // Compare against the dungeon-grid-snapped slot, not geographic (0,0): a 4000 map's nearest tunnel cell is ~56m off origin.
                if (config.TrySpawningOutpostInCenter && (nameLower.Contains("compound") || nameLower.Contains("outpost")))
                {
                    if (TerrainMeta.Path != null && TerrainMeta.HeightMap != null)
                    {
                        Vector3 centerPos = GetCenterOutpostPositionDry(config.DebugLogging);
                        if (!IsAtCenterOutpostSlot(position, centerPos, CenterSlotThreshold))
                        {
                            if (config.UseBlockedOutpostSlotForRelocation)
                                _blockedOutpostPosition = position;
                            if (config.DebugLogging)
                            {
                                float dx = Math.Abs(position.x - centerPos.x);
                                float dz = Math.Abs(position.z - centerPos.z);
                                UnityEngine.Debug.Log($"[CustomMapGen] Blocking compound/outpost not at center: {prefab.Name} at {position} (center={centerPos}, dx={dx}, dz={dz}) — saved position for relocating monuments.)");
                            }
                            return false;
                        }
                        // Compound/outpost is being added AT center: clear any other monument already at/near center (e.g. Large Barn) so they don't sit under the outpost
                        if (World.Serialization != null)
                            PostSaveSwap.MoveMonumentsAtCenterToNewPosition(World.Serialization, centerPos, config.MinMonumentDistance, config.DebugLogging, swapTargetPosition: null);
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
                    Vector3 banditSlotPos = position; // bandit's position — use for swap target or fill with another monument
                    if (config.TrySpawningOutpostInCenter && TerrainMeta.Path != null && TerrainMeta.HeightMap != null)
                    {
                        // useWorldConfig: false so prefab is not filtered by blacklist during procgen
                        Prefab[] mediumPrefabs = Prefab.Load("assets/bundled/prefabs/autospawn/monument/medium", null, null, useProbabilities: false, useWorldConfig: false);
                        Prefab centerPrefab = null;
                        if (mediumPrefabs != null)
                        {
                            if (config.DebugLogging)
                            {
                                var names = new System.Text.StringBuilder();
                                foreach (var p in mediumPrefabs)
                                    names.Append(p?.Name ?? "null").Append(", ");
                                UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] monument/medium prefabs: {names}");
                            }
                            // Center safe zone is compound.prefab; look for outpost first, then compound
                            foreach (var p in mediumPrefabs)
                            {
                                if (p?.Name == null || p.Object == null) continue;
                                if (p.Name.IndexOf("outpost", System.StringComparison.OrdinalIgnoreCase) >= 0)
                                { centerPrefab = p; break; }
                            }
                            if (centerPrefab == null)
                                foreach (var p in mediumPrefabs)
                                {
                                    if (p?.Name == null || p.Object == null) continue;
                                    if (p.Name.IndexOf("compound", System.StringComparison.OrdinalIgnoreCase) >= 0)
                                    { centerPrefab = p; break; }
                                }
                        }
                        if (centerPrefab == null)
                        {
                            Prefab[] direct = Prefab.Load("assets/bundled/prefabs/autospawn/monument/medium/compound.prefab", null, null, useProbabilities: false, useWorldConfig: false);
                            if (direct != null && direct.Length > 0 && direct[0]?.Object != null)
                                centerPrefab = direct[0];
                        }
                        if (centerPrefab != null)
                        {
                            Vector3 centerPos = GetCenterOutpostPositionDry(config.DebugLogging);
                            // Swap: monument at center moves to bandit's position (so we don't lose a slot)
                            bool slotFilled = World.Serialization != null && PostSaveSwap.MoveMonumentsAtCenterToNewPosition(World.Serialization, centerPos, config.MinMonumentDistance, config.DebugLogging, swapTargetPosition: banditSlotPos);
                            RemoveMonumentsAtCenter(centerPos, config.DebugLogging);
                            Vector3 centerScale = centerPrefab.Object != null ? centerPrefab.Object.transform.localScale : Vector3.one;
                            World.AddPrefab("Monument", centerPrefab, centerPos, Quaternion.identity, centerScale);
                            if (config.DebugLogging)
                                UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] Spawned center safe zone at map center ({centerPrefab.Name}): {centerPos}");
                            // Fill bandit slot with another monument if no monument was swapped there (all maps have outpost + bandit; we block bandit so we have an open slot)
                            if (!slotFilled && config.FillBanditSlotWithMonument)
                                TrySpawnMonumentInBanditSlot(banditSlotPos, config.DebugLogging);
                        }
                        else if (config.DebugLogging)
                            UnityEngine.Debug.Log("[CustomMapGen] [DEBUG] Could not find outpost or compound prefab in monument/medium folder to place at center");
                    }
                    return false;
                }

                // Redirect outpost/bandit to map center (move only; do not create a second one)
                if (config.TrySpawningOutpostInCenter &&
                    (nameLower.Contains("outpost") || nameLower.Contains("bandit")))
                {
                    if (TerrainMeta.Path != null && TerrainMeta.HeightMap != null)
                    {
                        Vector3 centerPos = GetCenterOutpostPositionDry(config.DebugLogging);
                        // Swap: monument at center moves to outpost's original position (so we don't lose a monument slot)
                        if (World.Serialization != null)
                            PostSaveSwap.MoveMonumentsAtCenterToNewPosition(World.Serialization, centerPos, config.MinMonumentDistance, config.DebugLogging, swapTargetPosition: position);
                        RemoveMonumentsAtCenter(centerPos, config.DebugLogging);
                        position = centerPos;
                        if (config.DebugLogging)
                            UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] Outpost moved to map center: {position}");
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
                        Vector3 centerPos = GetCenterOutpostPositionDry(false);
                        float dx = Math.Abs(position.x - centerPos.x);
                        float dz = Math.Abs(position.z - centerPos.z);
                        if (dx <= LargeMonumentCenterOverlap && dz <= LargeMonumentCenterOverlap)
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

        static void Postfix(string category, Prefab prefab, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (_spawningSwapRows)
                return;
            if (!CustomMapGen.IsCustomMapGenEnabled() || CustomMapGen.IsLoadingExistingMap)
                return;
            if (category != "Monument" || prefab?.Name == null)
                return;

            var config = CustomMapGen.Instance?.GetConfig();
            if (config == null || config.DisableWorldAddPrefabPatch)
                return;

            string nameLower = prefab.Name.ToLowerInvariant();
            if (config.TrySpawningOutpostInCenter
                && (nameLower.Contains("compound") || nameLower.Contains("outpost"))
                && TerrainMeta.Path != null && TerrainMeta.HeightMap != null
                && IsAtCenterOutpostSlot(position, GetCenterOutpostPositionDry(false), CenterSlotThreshold))
            {
                TrySpawnLiveOutpostSwap(position, rotation, config);
                return;
            }

            TrySpawnLiveCustomMonumentSwap(prefab, position, rotation, scale, config);
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

                var knownRows = new List<object>();
                int skippedUnknown = 0;
                foreach (object row in swapPrefabs)
                {
                    if (row == null || !PostSaveSwap.TryGetPrefabId(row, out uint checkId) || checkId == 0)
                        continue;
                    string checkPath = StringPool.Get(checkId);
                    if (string.IsNullOrEmpty(checkPath))
                    {
                        skippedUnknown++;
                        if (config.DebugLogging)
                            UnityEngine.Debug.LogWarning($"[CustomMapGen] Live outpost swap: skipping unknown prefab id={checkId} (not in server StringPool). Remove it from outpost.map.");
                        continue;
                    }
                    knownRows.Add(row);
                }

                // Snap To Tunnel Grid already Ceil's Y onto the 1.5m link grid. PlacementHeightOffset
                // would pull the door back off that grid and undo the lift that makes the 18m station link work.
                bool snapToGrid = config.SwapMonuments.SnapCenterOutpostToDungeonGrid;
                float heightOffset = snapToGrid ? 0f : config.SwapMonuments.PlacementHeightOffset;
                if (snapToGrid && config.DebugLogging && Mathf.Abs(config.SwapMonuments.PlacementHeightOffset) > 0.001f)
                    UnityEngine.Debug.Log("[CustomMapGen] Skipping PlacementHeightOffset because Snap To Tunnel Grid is on.");
                object startPos = MapHandlerReflection.NewVector3(centerPos.x, centerPos.y + heightOffset, centerPos.z);
                Vector3 euler = centerRotation.eulerAngles;
                object startRot = MapHandlerReflection.NewVector3(euler.x, euler.y, euler.z);
                bool useOrigin = config.SwapMonuments.UseMapOriginAsPlacementReference;
                IList created = MapHandlerReflection.CreatePrefabFromMap(startPos, startRot, knownRows, useOrigin);
                if (created == null || created.Count == 0)
                    return false;

                int serializedOnly = 0;
                int spawned = 0;
                int skippedRoots = 0;
                _spawningSwapRows = true;
                SkipTerrainStampsOnSpawn = true;
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
                        string rowPath = StringPool.Get(id) ?? "";
                        if (IsOutpostMonumentRootRow(rowPath, rowCategory))
                        {
                            skippedRoots++;
                            continue;
                        }

                        Vector3 rowPos = GetPrefabVector(row, "position", Vector3.zero);
                        Quaternion rowRot = Quaternion.Euler(GetPrefabVector(row, "rotation", Vector3.zero));
                        Vector3 rowScale = GetPrefabVector(row, "scale", Vector3.one);
                        Prefab rowPrefab = Prefab.Load(id);
                        if (rowPrefab?.Object == null)
                        {
                            // Serialize into the .map now; spawn GameObject LAST at DONE (after AssetScene-props loads).
                            World.Serialization?.AddPrefab(rowCategory, id, rowPos, rowRot, rowScale);
                            DeferredOutpostSpawn.Enqueue(rowCategory, id, rowPath, rowPos, rowRot, rowScale);
                            serializedOnly++;
                            continue;
                        }

                        World.AddPrefab(rowCategory, rowPrefab, rowPos, rowRot, rowScale);
                        spawned++;
                    }
                }
                finally
                {
                    SkipTerrainStampsOnSpawn = false;
                    _spawningSwapRows = false;
                    SwapSpawnTracking.EndTrackingAndLog("LiveProcgenSwap");
                }

                bool applied = spawned > 0 || serializedOnly > 0;
                if (!applied)
                    return false;

                _liveOutpostSwapApplied = true;
                UnityEngine.Debug.Log($"[CustomMapGen] Overlaid outpost.map extras onto vanilla compound at ({centerPos.x:F0},{centerPos.y:F0},{centerPos.z:F0}) spawned={spawned} deferredUntilDone={serializedOnly} skippedRoots={skippedRoots} skippedUnknown={skippedUnknown}.");
                return true;
            }
            catch (Exception ex)
            {
                SkipTerrainStampsOnSpawn = false;
                _spawningSwapRows = false;
                UnityEngine.Debug.LogWarning("[CustomMapGen] Live outpost swap failed; keeping vanilla compound. " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Overlay extras from a matching custom .map onto an already-placed vanilla monument
        /// (e.g. "Swamp C.map" → swamp_c). Vanilla keeps terrain, fog, and bushes.
        /// Skips outpost/compound (handled by the center live swap) and RoadFix maps (bridge/bridgerail).
        /// </summary>
        private static bool TrySpawnLiveCustomMonumentSwap(Prefab vanillaPrefab, Vector3 position, Quaternion rotation, Vector3 scale, MapGenConfig config)
        {
            if (config?.SwapMonuments == null || !config.SwapMonuments.Enabled)
                return false;
            if (vanillaPrefab?.Name == null)
                return false;

            string nameLower = vanillaPrefab.Name.ToLowerInvariant();
            if (nameLower.IndexOf("outpost", StringComparison.OrdinalIgnoreCase) >= 0
                || nameLower.IndexOf("compound", StringComparison.OrdinalIgnoreCase) >= 0
                || nameLower.IndexOf("bandit", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            string folder = !string.IsNullOrEmpty(config.SwapMonuments.CustomPrefabsFolder)
                ? Path.Combine(Environment.CurrentDirectory, config.SwapMonuments.CustomPrefabsFolder)
                : Path.Combine(Environment.CurrentDirectory, "maps/prefabs");
            if (!PostSaveSwap.TryFindCustomMapForPrefab(folder, vanillaPrefab.Name, out string mapPath, out string swapKey))
                return false;
            if (_liveSwappedMonumentKeys.Contains(swapKey))
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

                var knownRows = new List<object>();
                int skippedUnknown = 0;
                object rootRow = null;
                foreach (object row in swapPrefabs)
                {
                    if (row == null || !PostSaveSwap.TryGetPrefabId(row, out uint checkId) || checkId == 0)
                        continue;
                    string checkPath = StringPool.Get(checkId);
                    if (string.IsNullOrEmpty(checkPath))
                    {
                        skippedUnknown++;
                        if (config.DebugLogging)
                            UnityEngine.Debug.LogWarning($"[CustomMapGen] Live {swapKey} swap: skipping unknown prefab id={checkId} (not in server StringPool).");
                        continue;
                    }
                    string cat = Convert.ToString(PostSaveSwap.GetPrefabMember(row, "category"));
                    if (rootRow == null && IsVanillaMonumentRootRow(checkPath, cat, swapKey, vanillaPrefab))
                        rootRow = row;
                    knownRows.Add(row);
                }

                // Anchor to the Custom/monument root in the .map (not map origin). Otherwise extras
                // keep the editor's absolute Y and float when we skip re-stamping terrain.
                if (rootRow != null)
                {
                    knownRows.Remove(rootRow);
                    knownRows.Insert(0, rootRow);
                }

                Vector3 euler = rotation.eulerAngles;
                object startPos = MapHandlerReflection.NewVector3(position.x, position.y, position.z);
                object startRot = MapHandlerReflection.NewVector3(euler.x, euler.y, euler.z);
                // false => first row (monument root) is the placement reference, matching vanilla origin.
                IList created = MapHandlerReflection.CreatePrefabFromMap(startPos, startRot, knownRows, useMapOrigin: false);
                if (created == null || created.Count == 0)
                    return false;

                float seatDelta = SeatOverlayExtrasToTerrain(created, swapKey, vanillaPrefab, config.DebugLogging);

                int serializedOnly = 0;
                int spawned = 0;
                int skippedRoots = 0;
                _spawningSwapRows = true;
                SkipTerrainStampsOnSpawn = true;
                SwapSpawnTracking.BeginTracking(Path.GetFileName(mapPath), created);
                try
                {
                    foreach (object row in created)
                    {
                        if (row == null || !PostSaveSwap.TryGetPrefabId(row, out uint id) || id == 0)
                            continue;

                        string rowCategory = Convert.ToString(PostSaveSwap.GetPrefabMember(row, "category"));
                        string originalCategory = rowCategory;
                        if (string.IsNullOrEmpty(rowCategory) || string.Equals(rowCategory, "Custom", StringComparison.OrdinalIgnoreCase))
                            rowCategory = "Monument";
                        string rowPath = StringPool.Get(id) ?? "";
                        if (IsVanillaMonumentRootRow(rowPath, originalCategory, swapKey, vanillaPrefab))
                        {
                            skippedRoots++;
                            continue;
                        }

                        Vector3 rowPos = GetPrefabVector(row, "position", Vector3.zero);
                        Quaternion rowRot = Quaternion.Euler(GetPrefabVector(row, "rotation", Vector3.zero));
                        Vector3 rowScale = GetPrefabVector(row, "scale", Vector3.one);
                        Prefab rowPrefab = Prefab.Load(id);
                        if (rowPrefab?.Object == null)
                        {
                            World.Serialization?.AddPrefab(rowCategory, id, rowPos, rowRot, rowScale);
                            DeferredOutpostSpawn.Enqueue(rowCategory, id, rowPath, rowPos, rowRot, rowScale);
                            serializedOnly++;
                            continue;
                        }

                        World.AddPrefab(rowCategory, rowPrefab, rowPos, rowRot, rowScale);
                        spawned++;
                    }
                }
                finally
                {
                    SkipTerrainStampsOnSpawn = false;
                    _spawningSwapRows = false;
                    SwapSpawnTracking.EndTrackingAndLog("LiveMonumentSwap");
                }

                if (spawned <= 0 && serializedOnly <= 0)
                    return false;

                _liveSwappedMonumentKeys.Add(swapKey);
                UnityEngine.Debug.Log($"[CustomMapGen] Overlaid {Path.GetFileName(mapPath)} extras onto vanilla {GetShortMonumentName(vanillaPrefab.Name)} at ({position.x:F0},{position.y:F0},{position.z:F0}) spawned={spawned} deferredUntilDone={serializedOnly} skippedRoots={skippedRoots} skippedUnknown={skippedUnknown} seatDelta={seatDelta:F2}.");
                return true;
            }
            catch (Exception ex)
            {
                SkipTerrainStampsOnSpawn = false;
                _spawningSwapRows = false;
                UnityEngine.Debug.LogWarning($"[CustomMapGen] Live swap for {Path.GetFileName(mapPath)} failed; keeping vanilla. " + ex.Message);
                return false;
            }
        }

        private static bool IsVanillaMonumentRootRow(string path, string category, string swapKey, Prefab vanillaPrefab)
        {
            if (!string.IsNullOrEmpty(swapKey) && PostSaveSwap.PrefabPathMatchesSwapKey(path, swapKey))
                return true;
            if (vanillaPrefab?.Name != null && PostSaveSwap.PrefabPathMatchesSwapKey(path, GetShortMonumentName(vanillaPrefab.Name)))
                return true;
            if (string.Equals(category, "Custom", StringComparison.OrdinalIgnoreCase)
                && vanillaPrefab?.Name != null
                && !string.IsNullOrEmpty(path)
                && path.IndexOf(GetShortMonumentName(vanillaPrefab.Name), StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        /// <summary>
        /// Overlay extras keep baked Y from the .map. Without the custom monument's height stamps
        /// they float above vanilla ground/water. Shift the whole overlay by one shared delta so
        /// stilts/foundations meet the heightmap (relative layout preserved, no terrain recarve).
        /// </summary>
        private static float SeatOverlayExtrasToTerrain(IList created, string swapKey, Prefab vanillaPrefab, bool debugLogging)
        {
            if (created == null || created.Count == 0 || TerrainMeta.HeightMap == null)
                return 0f;

            var clearances = new List<float>(64);
            foreach (object row in created)
            {
                if (row == null || !PostSaveSwap.TryGetPrefabId(row, out uint id) || id == 0)
                    continue;
                string path = StringPool.Get(id) ?? "";
                string cat = Convert.ToString(PostSaveSwap.GetPrefabMember(row, "category"));
                if (IsVanillaMonumentRootRow(path, cat, swapKey, vanillaPrefab))
                    continue;
                if (!IsGroundContactOverlayPrefab(path))
                    continue;

                Vector3 pos = GetPrefabVector(row, "position", Vector3.zero);
                float terrainY = TerrainMeta.HeightMap.GetHeight(pos);
                clearances.Add(pos.y - terrainY);
            }

            if (clearances.Count == 0)
            {
                // No stilts/foundations — use a low percentile of all extras so we don't chase rooftops.
                foreach (object row in created)
                {
                    if (row == null || !PostSaveSwap.TryGetPrefabId(row, out uint id) || id == 0)
                        continue;
                    string path = StringPool.Get(id) ?? "";
                    string cat = Convert.ToString(PostSaveSwap.GetPrefabMember(row, "category"));
                    if (IsVanillaMonumentRootRow(path, cat, swapKey, vanillaPrefab))
                        continue;
                    Vector3 pos = GetPrefabVector(row, "position", Vector3.zero);
                    clearances.Add(pos.y - TerrainMeta.HeightMap.GetHeight(pos));
                }
                if (clearances.Count == 0)
                    return 0f;
                clearances.Sort();
                float low = clearances[Math.Min(clearances.Count - 1, Math.Max(0, clearances.Count / 10))];
                if (low <= 0.75f)
                    return 0f;
                ApplyOverlaySeatDelta(created, swapKey, vanillaPrefab, low - 0.1f);
                if (debugLogging)
                    UnityEngine.Debug.Log($"[CustomMapGen] Seated {swapKey} overlay extras by {- (low - 0.1f):F2}m (low-percentile clearance, n={clearances.Count}).");
                return low - 0.1f;
            }

            clearances.Sort();
            float median = clearances[clearances.Count / 2];
            // Stilts may sit a little above the riverbed; only correct obvious float.
            if (median <= 0.75f)
                return 0f;

            float delta = median - 0.1f;
            ApplyOverlaySeatDelta(created, swapKey, vanillaPrefab, delta);
            if (debugLogging)
                UnityEngine.Debug.Log($"[CustomMapGen] Seated {swapKey} overlay extras by {-delta:F2}m (stilt/foundation median clearance={median:F2}m, n={clearances.Count}).");
            return delta;
        }

        private static void ApplyOverlaySeatDelta(IList created, string swapKey, Prefab vanillaPrefab, float delta)
        {
            if (delta <= 0.001f)
                return;
            foreach (object row in created)
            {
                if (row == null || !PostSaveSwap.TryGetPrefabId(row, out uint id) || id == 0)
                    continue;
                string path = StringPool.Get(id) ?? "";
                string cat = Convert.ToString(PostSaveSwap.GetPrefabMember(row, "category"));
                if (IsVanillaMonumentRootRow(path, cat, swapKey, vanillaPrefab))
                    continue;
                object posObj = PostSaveSwap.GetPrefabMember(row, "position");
                if (posObj == null)
                    continue;
                float y = PostSaveSwap.GetPrefabPositionComponent(row, "y");
                PostSaveSwap.SetVectorComponent(posObj, "y", y - delta);
            }
        }

        private static bool IsGroundContactOverlayPrefab(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            string n = path.Replace('\\', '/').ToLowerInvariant();
            return n.IndexOf("pillar.third", StringComparison.Ordinal) >= 0
                || n.IndexOf("cabin_foundation", StringComparison.Ordinal) >= 0
                || n.IndexOf("cabin_pillar", StringComparison.Ordinal) >= 0
                || n.IndexOf("foundation", StringComparison.Ordinal) >= 0
                || n.IndexOf("wooden_walkway", StringComparison.Ordinal) >= 0;
        }

        private static bool IsOutpostMonumentRootRow(string path, string category)
        {
            if (string.Equals(category, "Dungeon", StringComparison.OrdinalIgnoreCase)
                || string.Equals(category, "Custom", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.IsNullOrEmpty(path))
                return false;
            string n = path.Replace('\\', '/').ToLowerInvariant();
            if (n.IndexOf("/compound.prefab", StringComparison.Ordinal) >= 0)
                return true;
            if (n.IndexOf("/outpost.prefab", StringComparison.Ordinal) >= 0)
                return true;
            if (n.IndexOf("monument/medium/compound", StringComparison.Ordinal) >= 0)
                return true;
            return PostSaveSwap.PrefabPathMatchesSwapKey(path, "outpost")
                || PostSaveSwap.PrefabPathMatchesSwapKey(path, "compound");
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

        private static bool IsAtCenterOutpostSlot(Vector3 position, Vector3 centerPos, float threshold)
        {
            return Math.Abs(position.x - centerPos.x) <= threshold
                && Math.Abs(position.z - centerPos.z) <= threshold;
        }

        /// <summary>
        /// GenerateDungeonGrid uses <c>WorldSpaceGrid(size, 216, RoundingMode.Down)</c>.
        /// Instance coords: CellCount=floor(size/216), world = (cell - CellCount/2)*216 + 108 (station/cell pivot).
        /// Logged tunnel prefabs sit exactly on that formula (e.g. -1620 = (1-9)*216+108), not on the static ClosestGridCell(-size/2) grid.
        /// </summary>
        internal static int DungeonGridCellCount(float worldSize)
        {
            return Mathf.Max(1, Mathf.FloorToInt(worldSize / DungeonGridCellSize));
        }

        internal static void WorldToDungeonGrid(Vector3 worldPos, int cellCount, out int cellX, out int cellZ)
        {
            int half = cellCount / 2;
            cellX = Mathf.Clamp(Mathf.FloorToInt(worldPos.x / DungeonGridCellSize) + half, 0, cellCount - 1);
            cellZ = Mathf.Clamp(Mathf.FloorToInt(worldPos.z / DungeonGridCellSize) + half, 0, cellCount - 1);
        }

        internal static Vector3 DungeonGridStationWorld(int cellX, int cellZ, int cellCount)
        {
            int half = cellCount / 2;
            float halfCell = DungeonGridCellSize * 0.5f;
            return new Vector3(
                (cellX - half) * DungeonGridCellSize + halfCell,
                0f,
                (cellZ - half) * DungeonGridCellSize + halfCell);
        }

        internal static Vector3 ClosestDungeonGridStation(Vector3 worldPos, float worldSize)
        {
            int count = DungeonGridCellCount(worldSize);
            WorldToDungeonGrid(worldPos, count, out int cx, out int cz);
            return DungeonGridStationWorld(cx, cz, count);
        }

        private static bool IsDryOutpostPosition(Vector3 p, float dryTolerance)
        {
            if (TerrainMeta.HeightMap == null)
                return true;
            float terrainY = TerrainMeta.HeightMap.GetHeight(p);
            if (TerrainMeta.WaterMap == null)
                return true;
            return terrainY >= TerrainMeta.WaterMap.GetHeight(p) - dryTolerance;
        }

        private static float SamplePreferredGroundHeight(Vector3 geographic)
        {
            if (TerrainMeta.HeightMap == null)
                return geographic.y;
            float best = geographic.y;
            const float step = 60f;
            for (int ix = -2; ix <= 2; ix++)
            {
                for (int iz = -2; iz <= 2; iz++)
                {
                    Vector3 p = geographic + new Vector3(ix * step, 0f, iz * step);
                    p.y = TerrainMeta.HeightMap.GetHeight(p);
                    if (!IsDryOutpostPosition(p, 0.1f))
                        continue;
                    if (p.y < best)
                        best = p.y;
                }
            }
            return best;
        }

        /// <summary>
        /// Local offset of the tunnel connect relative to the placed compound origin.
        /// XZ comes from outpost.map / measured entrance; Y must match vanilla compound's
        /// entrance (~0.2), not the outpost.map dungeon-row Y (~5.2). Using the map Y puts
        /// the door off the 1.5m link grid and breaks PathLinks (door at 17.5 instead of 18/22.5).
        /// </summary>
        private static Vector3 GetDungeonConnectLocalOffset(bool debugLogging)
        {
            Vector3 fromMap = TryGetOutpostMapEntranceOffset(debugLogging);
            Vector3 fromCompound = TryGetCompoundPrefabEntranceOffset(debugLogging);

            // Vanilla compound entrance sits near monument origin height (this gen: local Y≈0.2).
            float localY = 0.2f;
            if (fromCompound.x * fromCompound.x + fromCompound.z * fromCompound.z > 1f
                || Mathf.Abs(fromCompound.y) > 0.01f)
                localY = fromCompound.y;

            if (fromMap.x * fromMap.x + fromMap.z * fromMap.z > 400f)
            {
                var combined = new Vector3(fromMap.x, localY, fromMap.z);
                if (debugLogging)
                    UnityEngine.Debug.Log($"[CustomMapGen] Dungeon connect offset XZ from outpost.map, Y from vanilla compound: ({combined.x:F1},{combined.y:F1},{combined.z:F1}).");
                return combined;
            }

            if (fromCompound.x * fromCompound.x + fromCompound.z * fromCompound.z > 400f)
                return new Vector3(fromCompound.x, localY, fromCompound.z);

            if (debugLogging)
                UnityEngine.Debug.Log($"[CustomMapGen] Dungeon connect offset fallback (-58.3,{localY:F1},84.2).");
            return new Vector3(-58.3f, localY, 84.2f);
        }

        private static Vector3 TryGetCompoundPrefabEntranceOffset(bool debugLogging)
        {
            try
            {
                Prefab[] direct = Prefab.Load("assets/bundled/prefabs/autospawn/monument/medium/compound.prefab", null, null, useProbabilities: false, useWorldConfig: false);
                if (direct == null || direct.Length == 0 || direct[0]?.Object == null)
                {
                    if (debugLogging)
                        UnityEngine.Debug.Log("[CustomMapGen] Compound prefab load returned no object for dungeon offset.");
                    return Vector3.zero;
                }

                GameObject root = direct[0].Object;
                DungeonGridInfo entrance = root.GetComponentInChildren<DungeonGridInfo>(true);
                if (entrance == null)
                {
                    if (debugLogging)
                        UnityEngine.Debug.Log("[CustomMapGen] Compound prefab has no DungeonGridInfo on the unloaded template.");
                    return Vector3.zero;
                }

                Vector3 connectPos = entrance.transform.position;
                TerrainPathConnect[] connects = entrance.GetComponentsInChildren<TerrainPathConnect>(true);
                if (connects != null)
                {
                    for (int i = 0; i < connects.Length; i++)
                    {
                        if (connects[i] != null && connects[i].Type == InfrastructureType.Tunnel)
                        {
                            connectPos = connects[i].transform.position;
                            break;
                        }
                    }
                }

                Vector3 local = connectPos - root.transform.position;
                if (debugLogging)
                    UnityEngine.Debug.Log($"[CustomMapGen] Compound dungeon connect local offset=({local.x:F1},{local.y:F1},{local.z:F1}) from '{entrance.name}'.");
                return local;
            }
            catch (Exception ex)
            {
                if (debugLogging)
                    UnityEngine.Debug.LogWarning("[CustomMapGen] Could not read compound dungeon connect offset: " + ex.Message);
                return Vector3.zero;
            }
        }

        private static Vector3 TryGetOutpostMapEntranceOffset(bool debugLogging)
        {
            try
            {
                var config = CustomMapGen.Instance?.GetConfig();
                string folder = config?.SwapMonuments?.CustomPrefabsFolder;
                if (string.IsNullOrEmpty(folder))
                    folder = "maps/prefabs";
                string mapPath = Path.Combine(Environment.CurrentDirectory, folder, "outpost.map");
                if (!File.Exists(mapPath))
                    mapPath = Path.Combine(Environment.CurrentDirectory, folder, "outpost.prefab.map");
                if (!File.Exists(mapPath))
                    return Vector3.zero;

                var swapMap = new WorldSerialization();
                swapMap.Load(mapPath);
                object swapWorld = PostSaveSwap.GetWorldFromSerialization(swapMap);
                IList swapPrefabs = PostSaveSwap.GetPrefabsListFromWorld(swapWorld);
                if (swapPrefabs == null || swapPrefabs.Count == 0)
                    return Vector3.zero;

                Vector3 best = Vector3.zero;
                float bestScore = float.MaxValue;
                foreach (object row in swapPrefabs)
                {
                    if (row == null || !PostSaveSwap.TryGetPrefabId(row, out uint id) || id == 0)
                        continue;
                    string path = StringPool.Get(id) ?? "";
                    string pathLower = path.ToLowerInvariant();
                    if (pathLower.IndexOf("compound.prefab", StringComparison.OrdinalIgnoreCase) >= 0
                        || pathLower.IndexOf("outpost.prefab", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;
                    if (pathLower.IndexOf("entrance_monuments", StringComparison.OrdinalIgnoreCase) < 0
                        && pathLower.IndexOf("/entrance", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    Vector3 pos = GetPrefabVector(row, "position", Vector3.zero);
                    if (pos.x * pos.x + pos.z * pos.z < 400f)
                        continue;
                    float score = pos.x * pos.x + pos.z * pos.z;
                    if (pathLower.IndexOf("entrance_monuments", StringComparison.OrdinalIgnoreCase) >= 0)
                        score -= 100000f;
                    if (score >= bestScore)
                        continue;
                    bestScore = score;
                    best = pos;
                    if (debugLogging)
                        UnityEngine.Debug.Log($"[CustomMapGen] outpost.map dungeon-connect candidate id={id} path=\"{path}\" pos=({pos.x:F1},{pos.y:F1},{pos.z:F1})");
                }

                if (best.x * best.x + best.z * best.z > 400f && debugLogging)
                    UnityEngine.Debug.Log($"[CustomMapGen] Using outpost.map entrance local offset=({best.x:F1},{best.y:F1},{best.z:F1}).");
                return best.x * best.x + best.z * best.z > 400f ? best : Vector3.zero;
            }
            catch (Exception ex)
            {
                if (debugLogging)
                    UnityEngine.Debug.LogWarning("[CustomMapGen] Could not read outpost.map dungeon offset: " + ex.Message);
                return Vector3.zero;
            }
        }

        /// <summary>
        /// Offsets from the station pivot. One axis must be ≥28m so the 28×28 entrance volume does not
        /// Intersects2D the station (that skip leaves no PathLinks). The other axis must be ≥6m
        /// (LinkRadius*2): a purely cardinal leftover (x=0,z=36) is rejected by GenerateDungeonGrid
        /// until too many attempts fail, which is the leftover hole between corridor pieces.
        /// Vanilla bunker/harbor both have X and Z components (17.5m / 31.9m).
        /// </summary>
        private static readonly Vector3[] DungeonEntranceOffsets = BuildDungeonEntranceOffsets();

        private static Vector3[] BuildDungeonEntranceOffsets()
        {
            var list = new List<Vector3>(32);
            float[] majors = { 36f, 48f };
            float[] minors = { 9f, 12f };
            foreach (float major in majors)
            {
                foreach (float minor in minors)
                {
                    list.Add(new Vector3(major, 0f, minor));
                    list.Add(new Vector3(major, 0f, -minor));
                    list.Add(new Vector3(-major, 0f, minor));
                    list.Add(new Vector3(-major, 0f, -minor));
                    list.Add(new Vector3(minor, 0f, major));
                    list.Add(new Vector3(minor, 0f, -major));
                    list.Add(new Vector3(-minor, 0f, major));
                    list.Add(new Vector3(-minor, 0f, -major));
                }
            }
            return list.ToArray();
        }

        /// <summary>
        /// Place the center outpost before GenerateDungeonGrid. Stay near geographic center, but shift
        /// a little so the dungeon door sits on a 3m leftover next to a 216m station cell — that is
        /// what lets vanilla tunnel linking close. Exact (0,0) leaves a leftover PathLink cannot finish.
        /// </summary>
        private static Vector3 GetCenterOutpostPositionDry(bool debugLogging)
        {
            if (_centerOutpostPosition.HasValue)
                return _centerOutpostPosition.Value;

            Vector3 geographic = TerrainMeta.Position + TerrainMeta.Size * 0.5f;
            if (TerrainMeta.HeightMap != null)
                geographic.y = TerrainMeta.HeightMap.GetHeight(geographic);

            var config = CustomMapGen.Instance?.GetConfig();
            bool snap = config?.SwapMonuments?.SnapCenterOutpostToDungeonGrid ?? true;
            if (snap && TerrainMeta.Size.x > 0f && TrySnapCenterOutpostToTunnelStation(geographic, debugLogging, out Vector3 snapped))
            {
                _centerOutpostPosition = snapped;
                return snapped;
            }

            Vector3 dry = FindDryNear(geographic, debugLogging);
            _centerOutpostPosition = dry;
            return dry;
        }

        /// <summary>
        /// Search dungeon cells around map center. Prefer lower, flatter land (so we don't sit on a
        /// peak 40m+ above a nearby airfield) while keeping the dungeon door on a station leftover.
        /// Door Y is snapped onto the 1.5m link grid without Ceil-lifting onto stilts.
        /// </summary>
        private static bool TrySnapCenterOutpostToTunnelStation(Vector3 geographic, bool debugLogging, out Vector3 origin)
        {
            origin = geographic;
            Vector3 connectLocal = GetDungeonConnectLocalOffset(debugLogging);
            int cellCount = DungeonGridCellCount(TerrainMeta.Size.x);
            WorldToDungeonGrid(geographic, cellCount, out int originCx, out int originCz);

            float nearbyMonumentY = GetNearbyMonumentGroundHeight(geographic, 400f);
            float contextMin = SampleContextMinHeight(geographic, 240f);

            Vector3 bestOrigin = Vector3.zero;
            Vector3 bestEntrance = Vector3.zero;
            Vector3 bestStation = Vector3.zero;
            float bestScore = float.MaxValue;
            bool found = false;
            const int searchRadius = 2;
            const float maxOriginMove = 400f;

            for (int cx = originCx - searchRadius; cx <= originCx + searchRadius; cx++)
            {
                if (cx < 0 || cx >= cellCount)
                    continue;
                for (int cz = originCz - searchRadius; cz <= originCz + searchRadius; cz++)
                {
                    if (cz < 0 || cz >= cellCount)
                        continue;

                    Vector3 station = DungeonGridStationWorld(cx, cz, cellCount);
                    foreach (Vector3 offset in DungeonEntranceOffsets)
                    {
                        Vector3 entrance = station + offset;
                        entrance.x = Mathf.Round(entrance.x / 3f) * 3f;
                        entrance.z = Mathf.Round(entrance.z / 3f) * 3f;

                        Vector3 cand = new Vector3(entrance.x - connectLocal.x, 0f, entrance.z - connectLocal.z);
                        float move = Mathf.Sqrt(
                            (cand.x - geographic.x) * (cand.x - geographic.x) +
                            (cand.z - geographic.z) * (cand.z - geographic.z));
                        if (move > maxOriginMove)
                            continue;

                        SampleOutpostPad(cand, 90f, out float padY, out float spread, out float slope);
                        if (!IsDryOutpostPosition(new Vector3(cand.x, padY, cand.z), 0.1f))
                            continue;
                        if (slope > 28f)
                            continue;
                        if (spread > 32f)
                            continue;

                        float doorNatural = padY + connectLocal.y;
                        float doorY = SnapDoorHeightToLinkGrid(doorNatural);
                        cand.y = doorY - connectLocal.y;

                        float score = padY + move * 0.06f + spread * 0.25f;
                        // Prefer shorter leftovers (36+9 over 48+12). PathLink only gets 8
                        // segments/side; long leftovers leave a cardinal stub hole at the station.
                        float leftoverLen = Mathf.Abs(offset.x) + Mathf.Abs(offset.z);
                        score += (leftoverLen - 45f) * 0.35f;
                        if (padY > contextMin + 12f)
                            score += (padY - contextMin - 12f) * 2.5f;
                        if (nearbyMonumentY < 800f && padY > nearbyMonumentY + 10f)
                            score += (padY - nearbyMonumentY - 10f) * 3f;

                        if (score >= bestScore)
                            continue;

                        bestScore = score;
                        bestOrigin = cand;
                        bestEntrance = new Vector3(entrance.x, doorY, entrance.z);
                        bestStation = station;
                        found = true;
                    }
                }
            }

            if (!found)
            {
                if (debugLogging)
                    UnityEngine.Debug.LogWarning("[CustomMapGen] No dry tunnel-aligned slot within 400m of map center; using geographic/dry fallback.");
                return false;
            }

            origin = bestOrigin;
            _centerOutpostEntrance = bestEntrance;
            if (debugLogging)
            {
                float dx = bestEntrance.x - bestStation.x;
                float dz = bestEntrance.z - bestStation.z;
                UnityEngine.Debug.Log(
                    $"[CustomMapGen] Center outpost origin snapped ({origin.x:F1},{origin.y:F1},{origin.z:F1}) " +
                    $"from geographic ({geographic.x:F0},{geographic.y:F0},{geographic.z:F0}) score={bestScore:F1}. " +
                    $"Dungeon door=({bestEntrance.x:F1},{bestEntrance.y:F1},{bestEntrance.z:F1}) station=({bestStation.x:F1},{bestStation.z:F1}) leftover=({dx:F1},{dz:F1}) " +
                    $"nearbyMonumentY={(nearbyMonumentY < 800f ? nearbyMonumentY.ToString("F0") : "none")} contextMin={contextMin:F0}.");
            }
            return true;
        }

        /// <summary>
        /// Put the dungeon door on the 1.5m link grid. Never Ceil above natural terrain — that is the
        /// stilt lift. Round when it stays at/below ground; Floor when Round would raise.
        /// 0.71m miss this gen: door 81.7 → 81.0.
        /// </summary>
        private static float SnapDoorHeightToLinkGrid(float naturalDoorY)
        {
            const float linkHeight = 1.5f;
            float rounded = Mathf.Round(naturalDoorY / linkHeight) * linkHeight;
            if (rounded > naturalDoorY + 0.05f)
                return Mathf.Floor(naturalDoorY / linkHeight) * linkHeight;
            return rounded;
        }

        private static void SampleOutpostPad(Vector3 xz, float radius, out float padY, out float spread, out float slope)
        {
            padY = xz.y;
            spread = 0f;
            slope = 0f;
            if (TerrainMeta.HeightMap == null)
                return;

            Vector3 center = xz;
            center.y = TerrainMeta.HeightMap.GetHeight(center);
            padY = center.y;
            slope = TerrainMeta.HeightMap.GetSlope(center);

            float min = center.y;
            float max = center.y;
            float sum = 0f;
            int n = 0;
            const float step = 20f;
            for (float x = -radius; x <= radius; x += step)
            {
                for (float z = -radius; z <= radius; z += step)
                {
                    if (x * x + z * z > radius * radius)
                        continue;
                    Vector3 p = new Vector3(center.x + x, 0f, center.z + z);
                    p.y = TerrainMeta.HeightMap.GetHeight(p);
                    if (!IsDryOutpostPosition(p, 0.1f))
                        continue;
                    if (p.y < min) min = p.y;
                    if (p.y > max) max = p.y;
                    sum += p.y;
                    n++;
                }
            }

            if (n > 0)
                padY = sum / n;
            spread = max - min;
        }

        private static float SampleContextMinHeight(Vector3 geographic, float radius)
        {
            if (TerrainMeta.HeightMap == null)
                return geographic.y;
            float min = TerrainMeta.HeightMap.GetHeight(geographic);
            const float step = 40f;
            for (float x = -radius; x <= radius; x += step)
            {
                for (float z = -radius; z <= radius; z += step)
                {
                    Vector3 p = new Vector3(geographic.x + x, 0f, geographic.z + z);
                    p.y = TerrainMeta.HeightMap.GetHeight(p);
                    if (!IsDryOutpostPosition(p, 0.1f))
                        continue;
                    if (p.y < min)
                        min = p.y;
                }
            }
            return min;
        }

        private static float GetNearbyMonumentGroundHeight(Vector3 geographic, float radius)
        {
            float best = float.MaxValue;
            float radiusSq = radius * radius;
            var monuments = TerrainPathAccess.GetMonuments(TerrainMeta.Path);
            if (monuments == null)
                return best;
            foreach (var m in monuments)
            {
                if (m == null || m.transform == null)
                    continue;
                Vector3 p = m.transform.position;
                float dx = p.x - geographic.x;
                float dz = p.z - geographic.z;
                if (dx * dx + dz * dz > radiusSq)
                    continue;
                if (p.y < best)
                    best = p.y;
            }
            return best;
        }

        /// <summary>Find a dry position near map center. Newer Rust/staging can have lake at center for same seed.</summary>
        private static Vector3 FindDryNear(Vector3 center, bool debugLogging)
        {
            if (TerrainMeta.HeightMap != null)
                center.y = TerrainMeta.HeightMap.GetHeight(center);
            if (TerrainMeta.WaterMap == null || IsDryOutpostPosition(center, 0.1f))
                return center;

            float waterY = TerrainMeta.WaterMap.GetHeight(center);
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
                    if (IsDryOutpostPosition(p, 0.1f))
                    {
                        if (debugLogging)
                            UnityEngine.Debug.Log($"[CustomMapGen] Using dry position at ({p.x:F0}, {p.z:F0}), terrain={p.y:F1}, dist={r:F0}m from center.");
                        return p;
                    }
                }
            }
            if (debugLogging)
                UnityEngine.Debug.Log("[CustomMapGen] No dry position found within " + maxRadius + "m; using center anyway.");
            return center;
        }

        /// <summary>
        /// RustEdit "Snap To Tunnel Grid": <see cref="DungeonGridInfo.SnapPosition"/> —
        /// round XZ to 3m, Ceil Y to 1.5m. That Y ceil is the lift that lets station sockets meet link pieces.
        /// </summary>
        internal static Vector3 SnapToTunnelGrid(Vector3 pos)
        {
            const float linkRadius = 3f;
            const float linkHeight = 1.5f;
            pos.x = (float)Mathf.RoundToInt(pos.x / linkRadius) * linkRadius;
            pos.y = (float)Mathf.CeilToInt(pos.y / linkHeight) * linkHeight;
            pos.z = (float)Mathf.RoundToInt(pos.z / linkRadius) * linkRadius;
            return pos;
        }

        /// <summary>Remove any monument at map center (e.g. oasis) so the center outpost doesn't spawn inside it. Staging/newer Rust can place oases at center.</summary>
        private static void RemoveMonumentsAtCenter(Vector3 centerPos, bool debugLogging)
        {
            var monumentsAtCenter = TerrainPathAccess.GetMonuments(TerrainMeta.Path);
            if (monumentsAtCenter == null || monumentsAtCenter.Count == 0)
                return;
            const float centerRadius = 180f; // Oases can be large; clear enough radius for outpost
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
