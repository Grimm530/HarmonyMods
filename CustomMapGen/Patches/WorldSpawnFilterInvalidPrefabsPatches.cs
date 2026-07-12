using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace CustomMapGen.Patches
{
    /// <summary>
    /// 1) Runs monument swap (SwapMonuments) before spawn so the in-memory prefab list has custom outpost
    ///    when the game spawns — otherwise the game spawns the vanilla compound, then we only swap in the Save Prefix
    ///    and the live world never gets the custom prefabs.
    /// 2) Removes prefab entries with IDs not in the game manifest (StringPool) before World.Spawn
    ///    builds prefabPaths and spawns. Optionally saves the map after filtering.
    /// </summary>
    [HarmonyPatch]
    public static class World_Spawn_FilterInvalidPrefabs_Patch
    {
        private static Dictionary<uint, string> _stringPool;

        private static void EnsureStringPoolInit()
        {
            if (_stringPool != null) return;
            typeof(StringPool).GetMethod("Init", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, null);
            _stringPool = typeof(StringPool).GetField("toString", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as Dictionary<uint, string>;
        }

        private static bool IsKnownPrefabId(uint id)
        {
            if (id == 0) return false;
            EnsureStringPoolInit();
            if (_stringPool == null) return true;
            return _stringPool.ContainsKey(id);
        }

        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods()
        {
            var worldType = typeof(World);
            var spawnProgress = AccessTools.Method(worldType, "Spawn", new[] { typeof(float), typeof(Action<string>), typeof(CancellationToken) });
            if (spawnProgress != null)
                yield return spawnProgress;

            var nullableBounds = typeof(Nullable<>).MakeGenericType(typeof(Bounds));
            var spawnBounds = AccessTools.Method(worldType, "Spawn", new[] { nullableBounds });
            if (spawnBounds != null)
                yield return spawnBounds;
        }

        [HarmonyPrefix]
        static void Prefix(MethodBase __originalMethod)
        {
            var config = CustomMapGen.Instance?.GetConfig();
            int initialCount = World.Serialization?.world?.prefabs?.Count ?? 0;
            if (CustomMapGen.IsCustomMapGenEnabled() && config?.DebugLogging == true)
                UnityEngine.Debug.Log($"[CustomMapGen] World.Spawn Prefix: {__originalMethod?.Name ?? "Spawn"} hook entered (serialized prefabs={initialCount}, swapEnabled={config?.SwapMonuments?.Enabled})");

            // Run monument swap BEFORE spawn so the list has custom outpost when the game spawns prefabs.
            // (Save Prefix still runs before Save() so the file on disk also has the swap.)
            if (CustomMapGen.IsCustomMapGenEnabled())
            {
                if (config?.SwapMonuments != null && config.SwapMonuments.Enabled)
                {
                    object worldObj = PostSaveSwap.GetWorldFromSerialization(World.Serialization);
                    var prefabsList = worldObj != null ? PostSaveSwap.GetPrefabsListFromWorld(worldObj) : null;
                    if (prefabsList != null && prefabsList.Count > 0)
                    {
                        string folder = config.SwapMonuments != null && !string.IsNullOrEmpty(config.SwapMonuments.CustomPrefabsFolder)
                            ? Path.Combine(Environment.CurrentDirectory, config.SwapMonuments.CustomPrefabsFolder)
                            : Path.Combine(Environment.CurrentDirectory, "maps/prefabs");
                        if (Directory.Exists(folder))
                        {
                            PostSaveSwap.ApplySwapToPrefabsList(prefabsList, folder, out int swapped, out int injected);
                            if (swapped > 0 && config.DebugLogging)
                                UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] World.Spawn Prefix: applied monument swap before spawn (swapped={swapped}); custom outpost will be spawned.");
                            else if (swapped == 0 && config.DebugLogging)
                                UnityEngine.Debug.Log("[CustomMapGen] [DEBUG] World.Spawn Prefix: monument swap ran but swapped=0 (no matching .map or compound not in list yet — check folder and procgen order).");
                        }
                        else if (config.DebugLogging)
                            UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] World.Spawn Prefix: swap folder missing: {folder}");
                    }
                    else if (config.DebugLogging)
                        UnityEngine.Debug.Log("[CustomMapGen] [DEBUG] World.Spawn Prefix: could not read prefabs list from serialization (null or empty before spawn).");
                }
            }

            var list = World.Serialization?.world?.prefabs;
            if (list == null || list.Count == 0)
                return;

            EnsureStringPoolInit();
            var removedIds = new List<uint>();
            int removed = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var prefab = list[i];
                if (!IsKnownPrefabId(prefab.id))
                {
                    removedIds.Add(prefab.id);
                    list.RemoveAt(i);
                    removed++;
                }
            }

            if (config?.DebugLogging == true)
                UnityEngine.Debug.Log($"[CustomMapGen] World.Spawn Prefix: manifest filter removed {removed} prefab row(s) with unknown IDs (remaining={list.Count}).");

            if (removed <= 0)
                return;

            string idList = string.Join(", ", removedIds);
            UnityEngine.Debug.Log($"[CustomMapGen] Unknown prefab IDs (not in server manifest): {idList}. These may be from a custom monument or different game version; remove them from your custom .map to prevent errors when this mod is unloaded. See README 'Invalid prefab IDs' for how to identify them.");

            if (config != null && config.SaveMapAfterFilteringInvalidPrefabs)
            {
                try
                {
                    string mapPath = World.MapFolderName + "/" + World.MapFileName;
                    World.Serialization.Save(mapPath);
                    UnityEngine.Debug.Log($"[CustomMapGen] Saved map to {mapPath} (invalid prefabs removed permanently; next load will not have these entries even if mod is unloaded).");
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[CustomMapGen] Failed to save map after filtering invalid prefabs: {ex.Message}");
                }
            }
        }

        [HarmonyPostfix]
        static void Postfix(MethodBase __originalMethod)
        {
            var config = CustomMapGen.Instance?.GetConfig();
            if (config?.DebugLogging != true)
                return;

            string source = __originalMethod?.Name ?? "Spawn";
            SwapSpawnTracking.EndTrackingAndLog(source);
        }
    }

    [HarmonyPatch(typeof(World), "SpawnPrefabData")]
    public static class World_SpawnPrefabData_Probe_Patch
    {
        private static int _prefixCalls;
        private static int _postfixCalls;
        private const int InitialDetailedLogs = 2;
        private const int PeriodicSummaryEvery = 5000;

        [HarmonyPrefix]
        static void Prefix()
        {
            if (!CustomMapGen.IsCustomMapGenEnabled())
                return;

            int call = Interlocked.Increment(ref _prefixCalls);
            if (call <= InitialDetailedLogs)
            {
                WorldSpawnProbeHelpers.LogWorldSnapshot("World.SpawnPrefabData Prefix", WorldSpawnProbeHelpers.GetWorldObject(World.Serialization));
                return;
            }

            if (call % PeriodicSummaryEvery == 0)
                UnityEngine.Debug.Log($"[CustomMapGen] [PROBE] World.SpawnPrefabData Prefix: call={call} (detailed snapshot logging suppressed after first {InitialDetailedLogs} calls).");
        }

        [HarmonyPostfix]
        static void Postfix()
        {
            if (!CustomMapGen.IsCustomMapGenEnabled())
                return;

            int call = Interlocked.Increment(ref _postfixCalls);
            if (call <= InitialDetailedLogs)
            {
                WorldSpawnProbeHelpers.LogWorldSnapshot("World.SpawnPrefabData Postfix", WorldSpawnProbeHelpers.GetWorldObject(World.Serialization));
                return;
            }

            if (call % PeriodicSummaryEvery == 0)
                UnityEngine.Debug.Log($"[CustomMapGen] [PROBE] World.SpawnPrefabData Postfix: call={call} (detailed snapshot logging suppressed after first {InitialDetailedLogs} calls).");
        }
    }

    internal static class WorldSpawnProbeHelpers
    {
        internal static object GetWorldObject(object serialization)
        {
            if (serialization == null)
                return null;
            var t = serialization.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var worldProp = t.GetProperty("world", flags) ?? t.GetProperty("World", flags);
            if (worldProp != null)
                return worldProp.GetValue(serialization);
            var worldField = t.GetField("world", flags) ?? t.GetField("World", flags);
            return worldField?.GetValue(serialization);
        }

        internal static void LogWorldSnapshot(string source, object worldObj)
        {
            if (worldObj == null)
            {
                UnityEngine.Debug.Log($"[CustomMapGen] [PROBE] {source}: world object is null.");
                return;
            }

            var worldType = worldObj.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var prefabsProp = worldType.GetProperty("prefabs", flags) ?? worldType.GetProperty("Prefabs", flags);
            object prefabsObj = prefabsProp != null ? prefabsProp.GetValue(worldObj) : null;
            if (prefabsObj == null)
            {
                var prefabsField = worldType.GetField("prefabs", flags) ?? worldType.GetField("Prefabs", flags);
                prefabsObj = prefabsField?.GetValue(worldObj);
            }

            if (prefabsObj is not System.Collections.IList prefabs)
            {
                UnityEngine.Debug.Log($"[CustomMapGen] [PROBE] {source}: prefabs list unavailable (type={prefabsObj?.GetType().Name ?? "null"}).");
                return;
            }

            var byCategory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int idZero = 0;
            int nullRows = 0;
            int sampleLimit = 3;
            var samples = new List<string>();
            for (int i = 0; i < prefabs.Count; i++)
            {
                var row = prefabs[i];
                if (row == null)
                {
                    nullRows++;
                    continue;
                }

                var rowType = row.GetType();
                object idObj = rowType.GetField("id", flags)?.GetValue(row) ?? rowType.GetProperty("id", flags)?.GetValue(row);
                uint id = idObj != null ? Convert.ToUInt32(idObj) : 0;
                if (id == 0)
                    idZero++;

                object categoryObj = rowType.GetField("category", flags)?.GetValue(row) ?? rowType.GetProperty("category", flags)?.GetValue(row);
                string category = categoryObj != null ? Convert.ToString(categoryObj) : "(null)";
                if (string.IsNullOrEmpty(category))
                    category = "(empty)";
                byCategory[category] = byCategory.TryGetValue(category, out int count) ? count + 1 : 1;

                if (samples.Count < sampleLimit)
                {
                    string path = id != 0 ? StringPool.Get(id) : "";
                    samples.Add($"id={id} cat={category} path=\"{path}\"");
                }
            }

            var sortedKeys = new List<string>(byCategory.Keys);
            sortedKeys.Sort(StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            for (int i = 0; i < sortedKeys.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                string key = sortedKeys[i];
                sb.Append(key).Append("=").Append(byCategory[key]);
            }

            UnityEngine.Debug.Log($"[CustomMapGen] [PROBE] {source}: prefabRows={prefabs.Count}, nullRows={nullRows}, idZero={idZero}, categories={sb}");
            for (int i = 0; i < samples.Count; i++)
                UnityEngine.Debug.Log($"[CustomMapGen] [PROBE] {source}: sample[{i + 1}] {samples[i]}");
        }
    }
}
