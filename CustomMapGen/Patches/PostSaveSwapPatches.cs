using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CustomMapGen.Patches
{
    /// <summary>
    /// Post-save monument swap: after map is saved, load the .map file, find vanilla monument prefabs
    /// by shortname, replace them with custom .map prefabs (from CustomPrefabsFolder), transform
    /// position/rotation to match original, then save. Uses reflection for PrefabData/VectorData (game types).
    /// </summary>
    public static class PostSaveSwap
    {
        private static readonly WorldSerialization _mainMap = new WorldSerialization();
        private static readonly WorldSerialization _swapMap = new WorldSerialization();

        /// <summary>Stash map path (maps/temp copy) for PostSaveSwap at DONE.</summary>
        internal static string PendingMapPath;
        /// <summary>Original save path (e.g. server/grimm/...) so we can copy swapped result back.</summary>
        internal static string PendingOriginalPath;
        internal static bool PendingRunPostSaveSwap;

        internal static object GetWorldFromSerialization(WorldSerialization serialization)
        {
            if (serialization == null) return null;
            var t = serialization.GetType();
            foreach (var name in new[] { "world", "World" })
            {
                var prop = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null) return prop.GetValue(serialization);
                var field = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null) return field.GetValue(serialization);
            }
            return null;
        }

        /// <summary>Get prefabs list from world object; game may use "prefabs" or "Prefabs" (ProtoBuf).</summary>
        internal static IList GetPrefabsListFromWorld(object worldObj)
        {
            if (worldObj == null) return null;
            var t = worldObj.GetType();
            foreach (var name in new[] { "prefabs", "Prefabs" })
            {
                var prop = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                {
                    var list = prop.GetValue(worldObj) as IList;
                    if (list != null) return list;
                }
                var field = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    var list = field.GetValue(worldObj) as IList;
                    if (list != null) return list;
                }
            }
            return null;
        }

        /// <summary>Get world size (e.g. 3500) for center calculation.</summary>
        internal static float GetWorldSize()
        {
            var worldType = typeof(World).Assembly.GetType("World");
            if (worldType != null)
            {
                var sizeProp = worldType.GetProperty("Size", BindingFlags.Public | BindingFlags.Static);
                if (sizeProp != null)
                    return (float)Convert.ToDouble(sizeProp.GetValue(null));
            }
            return 3500f;
        }

        internal static float GetPrefabPositionComponent(object prefab, string axis)
        {
            if (prefab == null) return 0;
            object pos = GetPrefabMember(prefab, "position");
            if (pos == null) return 0;
            var f = pos.GetType().GetField(axis, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? (float)Convert.ToDouble(f.GetValue(pos)) : 0;
        }

        /// <summary>RustEdit often tags electrical/industrial prefabs with categories like Deployable / IO while meshes use &quot;world&quot;. Used only for diagnostic logging.</summary>
        internal static bool IsProbablyIoIndustrialOrElectric(string path, string category)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string p = path.ToLowerInvariant();
            string c = (category ?? "").ToLowerInvariant();
            if (c.Contains("io") || c.Contains("industrial") || c.Contains("deployable") || c.Contains("electric"))
                return true;
            if (p.Contains("/io/") || p.Contains("/iosockets") || p.Contains("ioentity"))
                return true;
            if (p.Contains("industrial") || p.Contains("industrialconveyor") || p.Contains("storageadaptor"))
                return true;
            if (p.Contains("electric") && (p.Contains("switch") || p.Contains("splitter") || p.Contains("branch") || p.Contains("combiner") || p.Contains("battery") || p.Contains("generator")))
                return true;
            if (p.Contains("/deployable/") && (p.Contains("vending") || p.Contains("sign") || p.Contains("speaker")))
                return true;
            return false;
        }

        /// <summary>When <see cref="MapGenConfig.DebugLogSwapMapPrefabBreakdown"/> is on, summarizes categories in the raw .map and samples IO-like rows.</summary>
        internal static void LogSwapMapPrefabInventory(IList swapPrefabs, string mapPath)
        {
            var config = CustomMapGen.Instance?.GetConfig();
            if (config?.DebugLogSwapMapPrefabBreakdown != true || config.DebugLogging != true)
                return;
            if (swapPrefabs == null || swapPrefabs.Count == 0)
                return;

            var byCategory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int ioish = 0;
            const int maxSamples = 40;
            var samples = new List<string>();

            foreach (object p in swapPrefabs)
            {
                if (p == null) continue;
                if (!TryGetPrefabId(p, out uint id)) continue;
                string path = id != 0 ? StringPool.Get(id) : "";
                object catObj = GetPrefabMember(p, "category");
                string cat = catObj != null ? Convert.ToString(catObj) : "";
                if (string.IsNullOrEmpty(cat)) cat = "(empty)";
                if (byCategory.TryGetValue(cat, out int n)) byCategory[cat] = n + 1;
                else byCategory[cat] = 1;

                if (IsProbablyIoIndustrialOrElectric(path, cat))
                {
                    ioish++;
                    if (samples.Count < maxSamples)
                        samples.Add($"  id={id} category=\"{cat}\" path=\"{path}\"");
                }
            }

            var sortedKeys = new List<string>(byCategory.Keys);
            sortedKeys.Sort(StringComparer.OrdinalIgnoreCase);
            var catParts = new List<string>(sortedKeys.Count);
            for (int i = 0; i < sortedKeys.Count; i++)
            {
                string key = sortedKeys[i];
                catParts.Add($"{key}={byCategory[key]}");
            }
            string catSummary = string.Join(", ", catParts.ToArray());
            UnityEngine.Debug.Log($"[CustomMapGen] Swap map prefab inventory ({Path.GetFileName(mapPath)}): total={swapPrefabs.Count} | categories: {catSummary}");
            UnityEngine.Debug.Log($"[CustomMapGen] Swap map rows matching IO/industrial/deployable/electric heuristics: {ioish} (showing up to {maxSamples})");
            if (samples.Count == 0)
                UnityEngine.Debug.Log("[CustomMapGen]   (no paths matched — if RustEdit lists many IO entities, categories/paths may differ from heuristics; enable DebugLogSkippedWorldPrefabs to catch null FindPrefab at spawn)");
            else
                foreach (string line in samples)
                    UnityEngine.Debug.Log("[CustomMapGen]" + line);
        }

        /// <summary>Get prefab id from PrefabData; game may use property or field (ProtoBuf often uses fields).</summary>
        internal static bool TryGetPrefabId(object prefab, out uint id)
        {
            id = 0;
            if (prefab == null) return false;
            var t = prefab.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var name in new[] { "id", "ID", "Id" })
            {
                var prop = t.GetProperty(name, flags);
                if (prop != null)
                {
                    var val = prop.GetValue(prefab);
                    if (val != null) { id = Convert.ToUInt32(val); return true; }
                }
                var field = t.GetField(name, flags);
                if (field != null)
                {
                    var val = field.GetValue(prefab);
                    if (val != null) { id = Convert.ToUInt32(val); return true; }
                }
            }
            return false;
        }

        /// <summary>Get a member (e.g. position, rotation) from PrefabData; game may use property or field (ProtoBuf).</summary>
        internal static object GetPrefabMember(object prefab, string memberName)
        {
            if (prefab == null || string.IsNullOrEmpty(memberName)) return null;
            var t = prefab.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var lower = memberName.ToLowerInvariant();
            foreach (var name in new[] { memberName, char.ToUpperInvariant(memberName[0]) + memberName.Substring(1), lower })
            {
                var prop = t.GetProperty(name, flags);
                if (prop != null)
                {
                    var val = prop.GetValue(prefab);
                    if (val != null) return val;
                }
                var field = t.GetField(name, flags);
                if (field != null)
                {
                    var val = field.GetValue(prefab);
                    if (val != null) return val;
                }
            }
            return null;
        }

        /// <summary>
        /// Apply monument swap to the prefabs list in place. Used by Save Prefix so the
        /// written file contains the swap. For outpost: find compound/outpost by prefab name (monument identity),
        /// not by position; swap outpost with outpost. Center safe zone is compound.prefab.
        /// </summary>
        internal static void ApplySwapToPrefabsList(IList prefabsList, string folder, out int swapped, out int injected)
        {
            swapped = 0;
            injected = 0;
            var config = CustomMapGen.Instance?.GetConfig();
            bool doSwap = config?.SwapMonuments != null && config.SwapMonuments.Enabled;
            if (!doSwap) return;
            if (prefabsList == null) return;

            float worldSize = GetWorldSize();
            float halfSize = worldSize * 0.5f;

            string[] customFiles = Directory.GetFiles(folder, "*.map");
            if (customFiles == null) customFiles = new string[0];
            if (config?.DebugLogging == true && customFiles.Length > 0)
                UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] ApplySwapToPrefabsList: folder={folder}, customFiles={customFiles.Length}, prefabCount={prefabsList?.Count ?? 0}");

            foreach (string file in customFiles)
            {
                if (string.IsNullOrEmpty(file) || !Path.GetFileName(file).EndsWith(".map", StringComparison.OrdinalIgnoreCase))
                    continue;
                string prefabShortname = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file));
                if (string.IsNullOrEmpty(prefabShortname)) continue;
                if (prefabShortname.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    prefabShortname = prefabShortname.Substring(0, prefabShortname.Length - 7);

                // For outpost.map: find compound/outpost by monument identity (prefab name), not by position. Swap outpost with outpost; center safe zone is compound.prefab.
                if (string.Equals(prefabShortname, "outpost", StringComparison.OrdinalIgnoreCase))
                {
                    if (World_AddPrefab_Patch.LiveOutpostSwapApplied)
                    {
                        if (config?.DebugLogging == true)
                            UnityEngine.Debug.Log("[CustomMapGen] [DEBUG] Outpost swap: live procgen swap already applied; skipping save-prefix outpost replacement to avoid duplicate rows.");
                        continue;
                    }

                    // Match full path or short name: compound.prefab, monument/medium/compound, outpost
                    var matchNames = new List<string> { "compound", "outpost", "monument/medium/compound" };
                    var matchPrefabs = new List<object>();
                    foreach (var p in prefabsList)
                    {
                        if (p == null) continue;
                        if (!TryGetPrefabId(p, out uint id)) continue;
                        string name = id != 0 ? StringPool.Get(id) : null;
                        if (string.IsNullOrEmpty(name)) continue;
                        foreach (var matchName in matchNames)
                        {
                            if (name.IndexOf(matchName, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                matchPrefabs.Add(p);
                                if (config?.DebugLogging == true)
                                {
                                    float px = GetPrefabPositionComponent(p, "x");
                                    float pz = GetPrefabPositionComponent(p, "z");
                                    UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] Outpost swap: found compound/outpost prefab id={id} name={name} at ({px:F1}, ?, {pz:F1})");
                                }
                                break;
                            }
                        }
                    }
                    if (matchPrefabs.Count == 0)
                    {
                        if (config?.DebugLogging == true)
                        {
                            UnityEngine.Debug.Log("[CustomMapGen] [DEBUG] Outpost swap: no compound/outpost prefab in list (match by monument name), skipping");
                            // Log sample of prefab id/names so we can see what is in the list (compound was placed earlier in log)
                            int sample = Math.Min(15, prefabsList.Count);
                            for (int i = 0; i < sample; i++)
                            {
                                var p = prefabsList[i];
                                if (p == null) continue;
                                if (!TryGetPrefabId(p, out uint id)) { UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG]   prefab[{i}] type={p.GetType().Name} (no id property/field)"); continue; }
                                string name = id != 0 ? StringPool.Get(id) : "(id=0)";
                                float px = GetPrefabPositionComponent(p, "x");
                                float pz = GetPrefabPositionComponent(p, "z");
                                UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG]   prefab[{i}] id={id} name={name ?? "(null)"} at ({px:F0},{pz:F0})");
                            }
                            if (prefabsList.Count > sample)
                                UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG]   ... and {prefabsList.Count - sample} more prefabs");
                        }
                        continue;
                    }
                    // If multiple (e.g. compound at center + one elsewhere), take the one closest to map center (0,0 or halfSize depending on coord system)
                    object firstPrefab = matchPrefabs[0];
                    if (matchPrefabs.Count > 1)
                    {
                        float bestDistance = MinSquaredDistanceToCenter(firstPrefab, halfSize);
                        for (int i = 1; i < matchPrefabs.Count; i++)
                        {
                            object candidate = matchPrefabs[i];
                            float candidateDistance = MinSquaredDistanceToCenter(candidate, halfSize);
                            if (candidateDistance < bestDistance)
                            {
                                bestDistance = candidateDistance;
                                firstPrefab = candidate;
                            }
                        }
                    }
                    object posObj = GetPrefabMember(firstPrefab, "position");
                    object rotObj = GetPrefabMember(firstPrefab, "rotation");
                    if (posObj == null || rotObj == null)
                    {
                        if (config?.DebugLogging == true)
                            UnityEngine.Debug.Log("[CustomMapGen] [DEBUG] Outpost swap: prefab has no position/rotation (position=" + (posObj != null) + ", rotation=" + (rotObj != null) + "), skipping");
                        continue;
                    }

                    if (config?.DebugLogging == true)
                    {
                        float fx = GetPrefabPositionComponent(firstPrefab, "x");
                        float fy = GetPrefabPositionComponent(firstPrefab, "y");
                        float fz = GetPrefabPositionComponent(firstPrefab, "z");
                        UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] Outpost swap: removing 1 compound/outpost at ({fx:F1}, {fy:F1}, {fz:F1}), placing custom from {file}");
                    }
                    prefabsList.Remove(firstPrefab);

                    _swapMap.Load(file);
                    object swapWorld = GetWorldFromSerialization(_swapMap);
                    var swapPrefabs = GetPrefabsListFromWorld(swapWorld);
                    if (swapPrefabs == null || swapPrefabs.Count == 0)
                    {
                        if (config?.DebugLogging == true)
                            UnityEngine.Debug.Log("[CustomMapGen] [DEBUG] Outpost swap: could not load " + file + " or no prefabs in it");
                        continue;
                    }
                    LogSwapMapPrefabInventory(swapPrefabs, file);
                    // Apply height offset so custom monument isn't sunk into terrain (e.g. procedural center can be higher than compound Y).
                    float heightOffset = config?.SwapMonuments?.PlacementHeightOffset ?? 0f;
                    if (heightOffset != 0f)
                    {
                        float px = GetVectorComponent(posObj, "x");
                        float py = GetVectorComponent(posObj, "y");
                        float pz = GetVectorComponent(posObj, "z");
                        posObj = MapHandlerReflection.NewVector3(px, py + heightOffset, pz);
                    }
                    bool useOrigin = config?.SwapMonuments?.UseMapOriginAsPlacementReference ?? true;
                    if (config?.DebugLogging == true)
                        UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] Outpost swap: calling CreatePrefabFromMap with swapPrefabs.Count={swapPrefabs.Count}, UseMapOriginAsPlacementReference={useOrigin}");
                    var created = MapHandlerReflection.CreatePrefabFromMap(posObj, rotObj, swapPrefabs, useOrigin);
                    if (config?.DebugLogging == true)
                        UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] Outpost swap: CreatePrefabFromMap returned {(created == null ? "null" : "count=" + created.Count)}");
                    if (created != null)
                    {
                        SwapSpawnTracking.BeginTracking(Path.GetFileName(file), created);
                        if (config?.DebugLogging == true)
                            UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] Outpost pasted: placing {created.Count} prefab(s) from {Path.GetFileName(file)}");
                        int idx = 0;
                        foreach (var p in created)
                        {
                            if (p != null)
                            {
                                prefabsList.Add(p);
                                if (config?.DebugLogging == true)
                                {
                                    uint pid = 0;
                                    TryGetPrefabId(p, out pid);
                                    float px = GetPrefabPositionComponent(p, "x");
                                    float py = GetPrefabPositionComponent(p, "y");
                                    float pz = GetPrefabPositionComponent(p, "z");
                                    UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] Monument created: #{idx + 1} id={pid} at ({px:F1}, {py:F1}, {pz:F1})");
                                }
                                idx++;
                            }
                        }
                        swapped++;
                        if (config?.DebugLogging == true)
                            UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] Outpost swap: removed 1 compound/outpost, placed {created.Count} from " + file);
                    }
                    continue;
                }

                // Other custom .map files: match by name, then replace
                var otherMatchNames = new List<string> { prefabShortname };
                var otherMatchPrefabs = new List<object>();
                foreach (var p in prefabsList)
                {
                    if (p == null) continue;
                    if (!TryGetPrefabId(p, out uint id)) continue;
                    string name = id != 0 ? StringPool.Get(id) : null;
                    if (string.IsNullOrEmpty(name)) continue;
                    foreach (var matchName in otherMatchNames)
                    {
                        if (name.IndexOf(matchName, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            otherMatchPrefabs.Add(p);
                            break;
                        }
                    }
                }
                if (otherMatchPrefabs.Count == 0) continue;

                foreach (var firstPrefab in otherMatchPrefabs)
                {
                    if (firstPrefab == null) continue;
                    _swapMap.Load(file);
                    object swapWorld = GetWorldFromSerialization(_swapMap);
                    var swapPrefabs = GetPrefabsListFromWorld(swapWorld);
                    if (swapPrefabs == null || swapPrefabs.Count == 0) continue;

                    object posObj = GetPrefabMember(firstPrefab, "position");
                    object rotObj = GetPrefabMember(firstPrefab, "rotation");
                    if (posObj == null || rotObj == null) continue;

                    prefabsList.Remove(firstPrefab);
                    var created = MapHandlerReflection.CreatePrefabFromMap(posObj, rotObj, swapPrefabs);
                    if (created != null)
                    {
                        foreach (var p in created)
                        {
                            if (p != null) prefabsList.Add(p);
                        }
                        swapped++;
                    }
                }
            }
        }

        /// <summary>
        /// Run after map file is saved and loading is done (LoadingScreen.Update("DONE")). Load map from path,
        /// swap matching monuments with custom .map prefabs, then save (overwrite or .swapped.map).
        /// </summary>
        public static void Run(string mapPath)
        {
            var config = CustomMapGen.Instance?.GetConfig();
            if (config?.DebugLogging == true)
                UnityEngine.Debug.Log($"[CustomMapGen] PostSaveSwap.Run called, mapPath={mapPath ?? "(null)"}, exists=" + (mapPath != null && File.Exists(mapPath)));
            if (string.IsNullOrEmpty(mapPath) || !File.Exists(mapPath))
            {
                if (config?.DebugLogging == true && !string.IsNullOrEmpty(mapPath))
                    UnityEngine.Debug.Log("[CustomMapGen] [DEBUG] PostSaveSwap early exit: map path empty or file not found");
                return;
            }
            string folder = config?.SwapMonuments != null && !string.IsNullOrEmpty(config.SwapMonuments.CustomPrefabsFolder)
                ? Path.Combine(Environment.CurrentDirectory, config.SwapMonuments.CustomPrefabsFolder)
                : Path.Combine(Environment.CurrentDirectory, "maps/prefabs");
            if (!Directory.Exists(folder))
            {
                if (config?.DebugLogging == true)
                    UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] PostSaveSwap early exit: CustomPrefabsFolder does not exist: {folder}");
                return;
            }
            bool doSwap = config?.SwapMonuments != null && config.SwapMonuments.Enabled;
            if (config?.DebugLogging == true)
                UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] PostSaveSwap doSwap={doSwap}, folder={folder}");
            if (!doSwap)
            {
                if (config?.DebugLogging == true)
                    UnityEngine.Debug.Log("[CustomMapGen] [DEBUG] PostSaveSwap early exit: swap disabled");
                return;
            }

            // Like HarmonyCustomGenerator: always load from file so we have the saved prefabs (dedicated server in-memory world may not match).
            _mainMap.Load(mapPath);
            object worldObj = GetWorldFromSerialization(_mainMap);
            if (worldObj == null)
            {
                if (config?.DebugLogging == true)
                    UnityEngine.Debug.Log("[CustomMapGen] [DEBUG] PostSaveSwap early exit: map load did not populate world");
                return;
            }
            var prefabsList = GetPrefabsListFromWorld(worldObj);
            if (prefabsList == null || prefabsList.Count == 0)
            {
                if (config?.DebugLogging == true)
                    UnityEngine.Debug.Log("[CustomMapGen] [DEBUG] PostSaveSwap early exit: prefabs list null or empty");
                return;
            }
            if (config?.DebugLogging == true)
                UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] PostSaveSwap: loaded from file, prefab count=" + prefabsList.Count);

                string[] customFiles = doSwap ? Directory.GetFiles(folder, "*.map") : null;
                if (doSwap && (customFiles == null || customFiles.Length == 0))
                    customFiles = new string[0];

                int swapped = 0;
                if (doSwap && customFiles != null)
                {
                    foreach (string file in customFiles)
                    {
                    if (string.IsNullOrEmpty(file) || !Path.GetFileName(file).EndsWith(".map", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string prefabShortname = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file));
                    if (string.IsNullOrEmpty(prefabShortname))
                        continue;
                    if (prefabShortname.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                        prefabShortname = prefabShortname.Substring(0, prefabShortname.Length - 7);

                    // Center safe zone is "compound.prefab" not "outpost" — when replacing outpost.prefab.map, also match compound
                    var matchNames = new List<string> { prefabShortname };
                    if (string.Equals(prefabShortname, "outpost", StringComparison.OrdinalIgnoreCase))
                        matchNames.Add("compound");

                    var matchPrefabs = new List<object>();
                    foreach (var p in prefabsList)
                    {
                        if (p == null) continue;
                        if (!TryGetPrefabId(p, out uint id)) continue;
                        string name = id != 0 ? StringPool.Get(id) : null;
                        if (string.IsNullOrEmpty(name)) continue;
                        foreach (var matchName in matchNames)
                        {
                            if (name.IndexOf(matchName, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                matchPrefabs.Add(p);
                                break;
                            }
                        }
                    }
                    if (matchPrefabs.Count == 0)
                        continue;

                    // For outpost: only replace the monument at map center (like HarmonyCustomGenerator).
                    if (string.Equals(prefabShortname, "outpost", StringComparison.OrdinalIgnoreCase) && matchPrefabs.Count > 1)
                    {
                        float worldSize = GetWorldSize();
                        float half = worldSize * 0.5f;
                        object nearest = matchPrefabs[0];
                        float nearestDistance = SquaredDistanceToCenter(nearest, half);
                        for (int i = 1; i < matchPrefabs.Count; i++)
                        {
                            object candidate = matchPrefabs[i];
                            float candidateDistance = SquaredDistanceToCenter(candidate, half);
                            if (candidateDistance < nearestDistance)
                            {
                                nearestDistance = candidateDistance;
                                nearest = candidate;
                            }
                        }
                        matchPrefabs = new List<object> { nearest };
                        if (config?.DebugLogging == true)
                            UnityEngine.Debug.Log("[CustomMapGen] [DEBUG] Outpost: targeting center only (1 prefab at map center)");
                    }

                    foreach (var firstPrefab in matchPrefabs)
                    {
                        if (firstPrefab == null) continue;
                        _swapMap.Load(file);
                        object swapWorld = GetWorldFromSerialization(_swapMap);
                        var swapPrefabs = GetPrefabsListFromWorld(swapWorld);
                        if (swapPrefabs == null || swapPrefabs.Count == 0)
                            continue;

                        object posObj = GetPrefabMember(firstPrefab, "position");
                        object rotObj = GetPrefabMember(firstPrefab, "rotation");
                        if (posObj == null || rotObj == null)
                            continue;

                        prefabsList.Remove(firstPrefab);
                        var created = MapHandlerReflection.CreatePrefabFromMap(posObj, rotObj, swapPrefabs);
                        if (created != null)
                        {
                            foreach (var p in created)
                            {
                                if (p != null)
                                    prefabsList.Add(p);
                            }
                            swapped++;
                            if (config?.DebugLogging == true)
                                UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] Replaced {prefabShortname} with {created.Count} prefab(s)");
                        }
                    }
                    }
                }

                if (swapped > 0)
                {
                    bool saveBoth = config.SwapMonuments != null && config.SwapMonuments.SaveBothVersions;
                    string savePath = saveBoth ? mapPath.Replace(".map", ".swapped.map") : mapPath;
                    _mainMap.Save(savePath);
                    if (config?.DebugLogging == true)
                        UnityEngine.Debug.Log($"[CustomMapGen] Post-save: replaced " + swapped + " monument(s), saved to " + savePath);
                }
        }

        private static float SquaredDistanceToCenter(object prefab, float centerXZ)
        {
            object pos = prefab != null ? GetPrefabMember(prefab, "position") : null;
            if (pos == null) return float.MaxValue;
            float x = GetVectorComponent(pos, "x"), z = GetVectorComponent(pos, "z");
            float dx = x - centerXZ, dz = z - centerXZ;
            return dx * dx + dz * dz;
        }

        /// <summary>Min of squared distance to (0,0) and (halfSize,halfSize) so we pick the prefab closest to map center regardless of coord system.</summary>
        private static float MinSquaredDistanceToCenter(object prefab, float halfSize)
        {
            object pos = prefab != null ? GetPrefabMember(prefab, "position") : null;
            if (pos == null) return float.MaxValue;
            float x = GetVectorComponent(pos, "x"), z = GetVectorComponent(pos, "z");
            float d0 = x * x + z * z;
            float dx = x - halfSize, dz = z - halfSize;
            float dHalf = dx * dx + dz * dz;
            return Math.Min(d0, dHalf);
        }

        private static float GetVectorComponent(object vector, string fieldName)
        {
            if (vector == null) return 0;
            var f = vector.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? (float)Convert.ToDouble(f.GetValue(vector)) : 0;
        }

        /// <summary>Set a component (x, y, z) on a VectorData object. Used to relocate a prefab in the serialization list.</summary>
        internal static void SetVectorComponent(object vector, string fieldName, float value)
        {
            if (vector == null || string.IsNullOrEmpty(fieldName)) return;
            var t = vector.GetType();
            var f = t.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null)
                f.SetValue(vector, value);
            else
            {
                var prop = t.GetProperty(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null && prop.CanWrite)
                    prop.SetValue(vector, value);
            }
        }

        /// <summary>
        /// When outpost is being moved to map center, any other monument already in the serialization at center
        /// is relocated. If swapTargetPosition is provided (outpost's original position), the first monument at center
        /// is moved there (1:1 swap). Any further monuments at center get a valid position at least minDistance from center and others.
        /// Returns true if at least one monument was moved to swapTargetPosition (slot filled).
        /// </summary>
        internal static bool MoveMonumentsAtCenterToNewPosition(WorldSerialization serialization, Vector3 centerPos, int minDistance, bool debugLogging, Vector3? swapTargetPosition = null)
        {
            if (serialization == null)
                return false;
            if (TerrainMeta.Path == null || TerrainMeta.HeightMap == null)
                return false;
            object worldObj = GetWorldFromSerialization(serialization);
            if (worldObj == null) return false;
            IList prefabsList = GetPrefabsListFromWorld(worldObj);
            if (prefabsList == null || prefabsList.Count == 0) return false;

            float minDist = (float)Math.Max(1, minDistance);
            const float centerRadius = 180f; // outpost footprint can be large; clear anything that could sit under it
            Vector3 mapCenter = TerrainMeta.Position + TerrainMeta.Size * 0.5f;
            float halfSize = TerrainMeta.Size.x * 0.5f;
            if (!swapTargetPosition.HasValue && halfSize < minDist + 100f) return false;

            bool usedSwapTarget = false;
            bool slotFilled = false;
            for (int idx = 0; idx < prefabsList.Count; idx++)
            {
                object prefab = prefabsList[idx];
                if (prefab == null) continue;
                object posObj = GetPrefabMember(prefab, "position");
                if (posObj == null) continue;
                float px = GetVectorComponent(posObj, "x");
                float pz = GetVectorComponent(posObj, "z");
                float dx = px - centerPos.x;
                float dz = pz - centerPos.z;
                if (dx * dx + dz * dz > centerRadius * centerRadius)
                    continue;
                if (!TryGetPrefabId(prefab, out uint id)) continue;
                string name = id != 0 ? StringPool.Get(id) : null;
                if (string.IsNullOrEmpty(name)) continue;
                if (name.IndexOf("compound", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("outpost", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                Vector3 newPos;
                bool usedSwapThis = false;
                if (swapTargetPosition.HasValue && !usedSwapTarget)
                {
                    usedSwapTarget = true;
                    usedSwapThis = true;
                    slotFilled = true;
                    newPos = swapTargetPosition.Value;
                    newPos.y = TerrainMeta.HeightMap.GetHeight(newPos);
                }
                else
                {
                    var found = FindValidMonumentPosition(centerPos, prefabsList, prefab, minDist, mapCenter, halfSize);
                    if (found == null)
                    {
                        if (debugLogging)
                            UnityEngine.Debug.Log($"[CustomMapGen] Could not find valid position to move monument at center (id={id}, name={name}); it will remain at center (may overlap outpost).");
                        continue;
                    }
                    newPos = found.Value;
                }
                float py = GetVectorComponent(posObj, "y");
                SetVectorComponent(posObj, "x", newPos.x);
                SetVectorComponent(posObj, "y", newPos.y);
                SetVectorComponent(posObj, "z", newPos.z);
                if (debugLogging)
                    UnityEngine.Debug.Log($"[CustomMapGen] Moved monument at center to new position: {name} from ({px:F0},{py:F0},{pz:F0}) to ({newPos.x:F0},{newPos.y:F0},{newPos.z:F0}) (swap to outpost slot={usedSwapThis}).");
            }
            return slotFilled;
        }

        private static Vector3? FindValidMonumentPosition(Vector3 centerPos, IList prefabsList, object excludePrefab, float minDist, Vector3 mapCenter, float halfSize)
        {
            const float stepRadius = 60f;
            const float angleStep = 80f;
            for (float radius = minDist; radius <= halfSize - 100f; radius += stepRadius)
            {
                int steps = Math.Max(8, (int)(2.0 * Math.PI * radius / angleStep));
                for (int i = 0; i < steps; i++)
                {
                    float angle = (float)i / steps * 2f * (float)Math.PI;
                    Vector3 cand = mapCenter + new Vector3((float)Math.Cos(angle) * radius, 0f, (float)Math.Sin(angle) * radius);
                    cand.y = TerrainMeta.HeightMap.GetHeight(cand);
                    if (TerrainMeta.WaterMap != null && cand.y < TerrainMeta.WaterMap.GetHeight(cand) - 0.1f)
                        continue;
                    if ((cand - centerPos).magnitude < minDist)
                        continue;
                    bool ok = true;
                    foreach (var other in prefabsList)
                    {
                        if (other == excludePrefab) continue;
                        object opos = GetPrefabMember(other, "position");
                        if (opos == null) continue;
                        float ox = GetVectorComponent(opos, "x");
                        float oy = GetVectorComponent(opos, "y");
                        float oz = GetVectorComponent(opos, "z");
                        if ((cand - new Vector3(ox, oy, oz)).magnitude < minDist)
                        {
                            ok = false;
                            break;
                        }
                    }
                    if (ok) return cand;
                }
            }
            return null;
        }

        /// <summary>True if the prefab name indicates a large monument (Outpost, Water Treatment, Airfield, Sewer Branch, etc.). Used to enforce MinDistanceSmallToLargeMonument.</summary>
        internal static bool IsLargeMonument(string prefabNameLower)
        {
            if (string.IsNullOrEmpty(prefabNameLower)) return false;
            string n = prefabNameLower;
            // Caves must not be treated as large monuments. Especially cave_large_sewers_hard —
            // a bare "sewer" match would falsely classify it as Sewer Branch and relocate it
            // onto the center-outpost clearance ring (tall plateau next to Outpost).
            if (n.IndexOf("cave", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            return n.IndexOf("compound", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("outpost", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("bandit_town", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("airfield", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("junkyard", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("launch_site", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("military_tunnels", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("powerplant", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("radtown", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("trainyard", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("water_treatment", StringComparison.OrdinalIgnoreCase) >= 0
                // Sewer Branch only (caves already excluded above so "sewer" cannot match cave_large_sewers_*).
                || n.IndexOf("sewer", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("sphere_tank", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("satellite_dish", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("silo", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("ziggurat", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("excavator", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("oilrig", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Effective radius (meters) from monument center for distance checks. Large monuments have big footprints; small monuments need to stay outside this radius + MinDistanceSmallToLargeMonument.</summary>
        internal static float GetEffectiveRadiusForLargeMonument(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return 0f;
            string n = prefabName;
            // Caves are not large monuments; do not apply Sewer Branch / large radii to cave_large_sewers_*.
            if (n.IndexOf("cave", StringComparison.OrdinalIgnoreCase) >= 0)
                return 0f;
            if (n.IndexOf("compound", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("outpost", StringComparison.OrdinalIgnoreCase) >= 0)
                return 120f;
            // Airfield footprint is much larger than water treatment (runway + hangars).
            if (n.IndexOf("airfield", StringComparison.OrdinalIgnoreCase) >= 0)
                return 150f;
            if (n.IndexOf("water_treatment", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("powerplant", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("launch_site", StringComparison.OrdinalIgnoreCase) >= 0)
                return 80f;
            if (n.IndexOf("junkyard", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("trainyard", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("sewer", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("radtown", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("silo", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("sphere_tank", StringComparison.OrdinalIgnoreCase) >= 0)
                return 50f;
            // Jungle ziggurat / ruins are compact; keep a modest blend radius so tiny monuments do not eject them from Jungle.
            if (n.IndexOf("ziggurat", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("jungle_ruins", StringComparison.OrdinalIgnoreCase) >= 0)
                return 40f;
            return 30f;
        }

        /// <summary>Find a valid position for a small monument that is at least minDistFromLarge from every large monument (plus their effective radius) and minDistFromOthers from every other monument. Prefers the position with the best clearance (max of min distance to any other monument) so the barn goes into open space.</summary>
        internal static Vector3? FindValidPositionForSmallMonument(Vector3 currentPos, IList prefabsList, object currentPrefab, float minDistFromLarge, float minDistFromOthers, bool debugLogging)
        {
            if (TerrainMeta.Path == null || TerrainMeta.HeightMap == null || prefabsList == null)
                return null;
            Vector3 mapCenter = TerrainMeta.Position + TerrainMeta.Size * 0.5f;
            float halfSize = TerrainMeta.Size.x * 0.5f;
            const float stepRadius = 50f;
            const float angleStep = 70f;
            Vector3? bestCand = null;
            float bestMinDist = 0f;
            for (float radius = minDistFromLarge; radius <= halfSize - 80f; radius += stepRadius)
            {
                int steps = Math.Max(8, (int)(2.0 * Math.PI * radius / angleStep));
                for (int i = 0; i < steps; i++)
                {
                    float angle = (float)i / steps * 2f * (float)Math.PI;
                    Vector3 cand = mapCenter + new Vector3((float)Math.Cos(angle) * radius, 0f, (float)Math.Sin(angle) * radius);
                    cand.y = TerrainMeta.HeightMap.GetHeight(cand);
                    if (TerrainMeta.WaterMap != null && cand.y < TerrainMeta.WaterMap.GetHeight(cand) - 0.1f)
                        continue;
                    float minDistToAny = float.MaxValue;
                    bool ok = true;
                    foreach (var other in prefabsList)
                    {
                        if (other == currentPrefab) continue;
                        object opos = GetPrefabMember(other, "position");
                        if (opos == null) continue;
                        float ox = GetVectorComponent(opos, "x");
                        float oy = GetVectorComponent(opos, "y");
                        float oz = GetVectorComponent(opos, "z");
                        float d = (cand - new Vector3(ox, oy, oz)).magnitude;
                        if (d < minDistToAny) minDistToAny = d;
                        string oname = null;
                        if (TryGetPrefabId(other, out uint oid))
                            oname = oid != 0 ? StringPool.Get(oid) : null;
                        bool otherIsLarge = !string.IsNullOrEmpty(oname) && IsLargeMonument(oname.ToLowerInvariant());
                        float effectiveRadius = !string.IsNullOrEmpty(oname) ? GetEffectiveRadiusForLargeMonument(oname) : 0f;
                        float requiredDist = effectiveRadius + minDistFromLarge;
                        if (otherIsLarge && d < requiredDist) { ok = false; break; }
                        if (d < minDistFromOthers) { ok = false; break; }
                    }
                    if (ok && minDistToAny > bestMinDist)
                    {
                        bestMinDist = minDistToAny;
                        bestCand = cand;
                    }
                }
            }
            return bestCand;
        }
    }

    /// <summary>
    /// Run monument swap in Save Prefix so the written file contains the swap.
    /// Does not depend on LoadingScreen.Update("DONE") — compound/outpost is replaced before the map is written.
    /// </summary>
    [HarmonyPatch(typeof(WorldSerialization), nameof(WorldSerialization.Save), new Type[] { typeof(string) })]
    public static class WorldSerialization_Save_SwapPrefix_Patch
    {
        static void Prefix(WorldSerialization __instance, string fileName)
        {
            if (string.IsNullOrEmpty(fileName) || !CustomMapGen.IsCustomMapGenEnabled())
                return;
            if (!World.Procedural)
                return;
            var config = CustomMapGen.Instance?.GetConfig();
            bool doSwap = config?.SwapMonuments != null && config.SwapMonuments.Enabled;
            if (!doSwap)
                return;

            object worldObj = PostSaveSwap.GetWorldFromSerialization(__instance);
            if (worldObj == null)
            {
                if (config?.DebugLogging == true)
                    UnityEngine.Debug.Log("[CustomMapGen] [DEBUG] Save Prefix: GetWorldFromSerialization returned null (cannot get world from serialization)");
                return;
            }
            var prefabsList = PostSaveSwap.GetPrefabsListFromWorld(worldObj);
            if (prefabsList == null || prefabsList.Count == 0)
            {
                if (config?.DebugLogging == true)
                    UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] Save Prefix: prefabs list null or empty (worldObj type={worldObj.GetType().Name}, list count={prefabsList?.Count ?? -1}) - compound was added earlier via World.AddPrefab so list should not be empty");
                return;
            }

            string folder = config?.SwapMonuments != null && !string.IsNullOrEmpty(config.SwapMonuments.CustomPrefabsFolder)
                ? Path.Combine(Environment.CurrentDirectory, config.SwapMonuments.CustomPrefabsFolder)
                : Path.Combine(Environment.CurrentDirectory, "maps/prefabs");
            if (!Directory.Exists(folder))
                return;

            if (config?.DebugLogging == true)
                UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] Save Prefix: applying monument swap (folder={folder}), then writing to {fileName}");
            PostSaveSwap.ApplySwapToPrefabsList(prefabsList, folder, out int swapped, out int injected);
            if (config?.DebugLogging == true)
            {
                if (swapped > 0)
                    UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] Save Prefix: replaced {swapped} monument(s); writing to {fileName}");
                else
                    UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] Save Prefix: no monuments swapped (swapped=0, injected={injected}); writing to {fileName}");
            }
        }
    }

    /// <summary>
    /// Transforms prefabs from a custom .map to world position/rotation using reflection (game types PrefabData/VectorData).
    /// </summary>
    internal static class MapHandlerReflection
    {
        internal static readonly System.Reflection.Assembly GameAssembly = typeof(World).Assembly;
        private static Type _vectorDataType;
        private static Type _prefabDataType;
        private static Type VectorDataType => _vectorDataType ??= ResolveType("VectorData");
        private static Type PrefabDataType => _prefabDataType ??= ResolveType("PrefabData");

        /// <summary>Resolve type by name from game assembly or any loaded assembly (ProtoBuf types may be in a different DLL).</summary>
        private static Type ResolveType(string typeName)
        {
            var gameTypes = GameAssembly.GetTypes();
            for (int i = 0; i < gameTypes.Length; i++)
            {
                var gameType = gameTypes[i];
                if (gameType.Name == typeName)
                    return gameType;
            }
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var asmTypes = asm.GetTypes();
                for (int i = 0; i < asmTypes.Length; i++)
                {
                    var t = asmTypes[i];
                    if (t.Name == typeName)
                        return t;
                }
            }
            return null;
        }

        public static IList CreatePrefabFromMap(object startPos, object startRot, IList prefabs)
        {
            return CreatePrefabFromMap(startPos, startRot, prefabs, useMapOrigin: false);
        }

        /// <summary>
        /// Place prefabs from a custom map at startPos/startRot.
        /// When useMapOrigin is true, the map origin (0,0,0) is placed at startPos so the monument's
        /// ground (if at Y=0 in the map) aligns with the target; when false, the first prefab's position is used (legacy).
        /// </summary>
        public static IList CreatePrefabFromMap(object startPos, object startRot, IList prefabs, bool useMapOrigin)
        {
            var config = CustomMapGen.Instance?.GetConfig();
            bool debug = config?.DebugLogging == true;
            if (VectorDataType == null || PrefabDataType == null)
            {
                if (debug)
                    UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] CreatePrefabFromMap: type resolve failed (VectorData={VectorDataType != null}, PrefabData={PrefabDataType != null})");
                return null;
            }
            if (prefabs == null || prefabs.Count == 0)
            {
                if (debug) UnityEngine.Debug.Log("[CustomMapGen] [DEBUG] CreatePrefabFromMap: prefabs null or empty");
                return null;
            }
            object referencePos = useMapOrigin ? NewVector(0f, 0f, 0f) : GetMember(prefabs[0], "position");
            var result = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(PrefabDataType));
            bool first = true;
            foreach (var prefab in prefabs)
            {
                object idObj = GetMember(prefab, "id");
                uint id = idObj != null ? Convert.ToUInt32(idObj) : 0;
                uint newId = (id == 2749405185u) ? 504351302u : id;
                object scale = (id == 2749405185u) ? NewVector(0, 0, 0) : GetMember(prefab, "scale");
                object position = Calculate(startPos, GetMember(prefab, "position"), GetMember(prefab, "scale"), prefabs, referencePos, startRot);
                object rotation = first ? startRot : CalculateRot(startRot, GetMember(prefab, "rotation"));
                object category = GetMember(prefab, "category");
                if (category == null) category = "Monument";
                else if (config?.SwapMonuments != null && config.SwapMonuments.NormalizeDecorCategoryToMonumentWhenPastingSwapMap
                    && string.Equals(category.ToString(), "Decor", StringComparison.OrdinalIgnoreCase))
                    category = "Monument";
                object newPrefab = NewPrefabData(newId, position, rotation, scale, category);
                if (newPrefab != null)
                    result.Add(newPrefab);
                first = false;
            }
            return result;
        }

        /// <summary>Get property or field (ProtoBuf uses fields).</summary>
        private static object GetMember(object obj, string name)
        {
            if (obj == null || string.IsNullOrEmpty(name)) return null;
            var t = obj.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var prop = t.GetProperty(name, flags);
            if (prop != null) return prop.GetValue(obj);
            var field = t.GetField(name, flags);
            return field?.GetValue(obj);
        }

        private static object NewVector(float x, float y, float z)
        {
            if (VectorDataType == null) return null;
            var v = Activator.CreateInstance(VectorDataType);
            var px = VectorDataType.GetField("x");
            var py = VectorDataType.GetField("y");
            var pz = VectorDataType.GetField("z");
            if (px != null) px.SetValue(v, x);
            if (py != null) py.SetValue(v, y);
            if (pz != null) pz.SetValue(v, z);
            return v;
        }

        /// <summary>Create VectorData for position/rotation.</summary>
        internal static object NewVector3(float x, float y, float z) => NewVector(x, y, z);

        private static object NewPrefabData(uint id, object position, object rotation, object scale, object category)
        {
            if (PrefabDataType == null) return null;
            var p = Activator.CreateInstance(PrefabDataType);
            SetMember(p, "id", id);
            SetMember(p, "position", position);
            SetMember(p, "rotation", rotation);
            SetMember(p, "scale", scale);
            SetMember(p, "category", category);
            return p;
        }

        /// <summary>Set property or field (ProtoBuf uses fields).</summary>
        private static void SetMember(object obj, string name, object value)
        {
            if (obj == null || string.IsNullOrEmpty(name)) return;
            var t = obj.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var prop = t.GetProperty(name, flags);
            if (prop != null) { prop.SetValue(obj, value); return; }
            var field = t.GetField(name, flags);
            if (field != null) field.SetValue(obj, value);
        }

        private static float GetV(object v, string f)
        {
            if (v == null) return 0;
            var field = v.GetType().GetField(f);
            return field != null ? (float)Convert.ChangeType(field.GetValue(v), typeof(float)) : 0;
        }

        private static object CalculateLocalPos(object placePos, object globalPos, object rotation)
        {
            float dx = GetV(globalPos, "x") - GetV(placePos, "x");
            float dy = GetV(globalPos, "y") - GetV(placePos, "y");
            float dz = GetV(globalPos, "z") - GetV(placePos, "z");
            return RotateVector(NewVector(dx, dy, dz), rotation);
        }

        private static object RotateVector(object vector, object rotation)
        {
            if (vector == null || rotation == null) return vector;
            float vx = GetV(vector, "x"), vy = GetV(vector, "y"), vz = GetV(vector, "z");
            float rx = GetV(rotation, "x") * (float)Math.PI / 180f;
            float ry = GetV(rotation, "y") * (float)Math.PI / 180f;
            float rz = GetV(rotation, "z") * (float)Math.PI / 180f;
            float cosX = (float)Math.Cos(rx), sinX = (float)Math.Sin(rx);
            float cosY = (float)Math.Cos(ry), sinY = (float)Math.Sin(ry);
            float cosZ = (float)Math.Cos(rz), sinZ = (float)Math.Sin(rz);
            float ny = vy * cosX - vz * sinX, nz = vy * sinX + vz * cosX;
            vy = ny; vz = nz;
            float nx = vx * cosY + vz * sinY; nz = vz * cosY - vx * sinY;
            vx = nx; vz = nz;
            nx = vx * cosZ - vy * sinZ; ny = vx * sinZ + vy * cosZ;
            vx = nx; vy = ny;
            return NewVector(vx, vy, vz);
        }

        private static object Calculate(object globalPos, object position, object scale, IList prefabs, object firstPrefabPos, object firstPrefabRotation)
        {
            object localPos = CalculateLocalPos(firstPrefabPos, position, firstPrefabRotation);
            float gx = GetV(globalPos, "x"), gy = GetV(globalPos, "y"), gz = GetV(globalPos, "z");
            float lx = GetV(localPos, "x"), ly = GetV(localPos, "y"), lz = GetV(localPos, "z");
            return NewVector(gx + lx, gy + ly, gz + lz);
        }

        private static object CalculateRot(object globalRot, object localRot)
        {
            float gx = GetV(globalRot, "x"), gy = GetV(globalRot, "y"), gz = GetV(globalRot, "z");
            float lx = GetV(localRot, "x"), ly = GetV(localRot, "y"), lz = GetV(localRot, "z");
            return NewVector(gx + lx, gy + ly, gz + lz);
        }
    }
}
