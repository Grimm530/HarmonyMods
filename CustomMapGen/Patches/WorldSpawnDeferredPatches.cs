using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace CustomMapGen.Patches
{
    /// <summary>
    /// After the world is loaded from file (World.Spawn IEnumerator), spawn deferred compound entities
    /// that were skipped during procgen because AssetScene-props was not loaded.
    /// Runs when World.SpawnPrefabData completes (e.g. when loading a saved map).
    /// </summary>
    [HarmonyPatch(typeof(World))]
    public static class World_SpawnPrefabData_Deferred_Patch
    {
        private static int _spawnedPrefabCount;

        [HarmonyPatch("SpawnPrefabData")]
        [HarmonyPostfix]
        static void Postfix()
        {
            if (CustomMapGen.IsLoadingExistingMap) return;
            if (!CustomMapGen.IsCustomMapGenEnabled())
                return;
            int total = GetWorldPrefabsCount();
            if (total <= 0) return;
            _spawnedPrefabCount++;
            if (_spawnedPrefabCount < total)
                return;
            _spawnedPrefabCount = 0;
            // Run deferred compound spawn after all prefabs are spawned (both when loading from cache and after fresh procgen).
            RunDeferredCompoundSpawn();
        }

        private static int GetWorldPrefabsCount()
        {
            var worldType = typeof(World);
            var serializationProp = worldType.GetProperty("Serialization", BindingFlags.Public | BindingFlags.Static);
            if (serializationProp == null) return 0;
            var serialization = serializationProp.GetValue(null);
            if (serialization == null) return 0;
            var worldProp = serialization.GetType().GetProperty("world", BindingFlags.Public | BindingFlags.Instance);
            if (worldProp == null) return 0;
            var world = worldProp.GetValue(serialization);
            if (world == null) return 0;
            var prefabsProp = world.GetType().GetProperty("prefabs", BindingFlags.Public | BindingFlags.Instance);
            if (prefabsProp == null) return 0;
            var prefabs = prefabsProp.GetValue(world) as System.Collections.ICollection;
            return prefabs?.Count ?? 0;
        }

        internal static void RunDeferredCompoundSpawn()
        {
            const string path = "HarmonyConfig/CustomMapGen_deferred.json";
            bool debug = CustomMapGen.IsCustomMapGenEnabled() && CustomMapGen.Instance?.GetConfig()?.DebugLogging == true;
            if (debug)
                UnityEngine.Debug.Log("[CustomMapGen] [DEBUG] RunDeferredCompoundSpawn: entry, path=" + path);
            if (!File.Exists(path))
            {
                if (debug)
                    UnityEngine.Debug.Log("[CustomMapGen] [DEBUG] RunDeferredCompoundSpawn: file does not exist, exit");
                return;
            }
            if (debug)
                UnityEngine.Debug.Log("[CustomMapGen] [DEBUG] RunDeferredCompoundSpawn: reading and deserializing...");
            string json = File.ReadAllText(path);
            var list = JsonConvert.DeserializeObject<List<PlaceMonumentsCompound_Patch.DeferredCompoundEntityData>>(json);
            if (list == null || list.Count == 0)
            {
                File.Delete(path);
                if (debug)
                    UnityEngine.Debug.Log("[CustomMapGen] [DEBUG] RunDeferredCompoundSpawn: list null or empty, deleted file, exit");
                return;
            }
            if (debug)
                UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] RunDeferredCompoundSpawn: list count={list.Count}, starting entity spawn loop...");
            int spawned = 0;
            foreach (var d in list)
            {
                var pos = new Vector3(d.WX, d.WY, d.WZ);
                var rot = new Quaternion(d.RX, d.RY, d.RZ, d.RW);
                var scale = new Vector3(d.SX, d.SY, d.SZ);
                var entity = GameManager.server.CreateEntity(d.PrefabName, pos, rot);
                if (entity != null)
                {
                    if (scale != Vector3.one)
                    {
                        entity.transform.localScale = scale;
                        entity.networkEntityScale = true;
                    }
                    entity.Spawn();
                    spawned++;
                }
            }
            File.Delete(path);
            if (debug)
                UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] RunDeferredCompoundSpawn: loop done, spawned={spawned}, exit");
            UnityEngine.Debug.Log($"[CustomMapGen] Spawned {spawned} deferred compound entities (AssetScene-props loaded)");
        }
    }

    /// <summary>
    /// Defer PostSaveSwap only when RunPostSaveSwap is true (legacy). When SwapMonuments.Enabled,
    /// the Save Prefix already does the swap before write, so we write one map file only and skip
    /// copying to maps/temp and running at DONE — no second file.
    /// </summary>
    [HarmonyPatch(typeof(WorldSerialization), nameof(WorldSerialization.Save), new Type[] { typeof(string) })]
    public static class WorldSerialization_Save_PostSaveSwap_Patch
    {
        static void Postfix(string fileName)
        {
            if (string.IsNullOrEmpty(fileName) || !CustomMapGen.IsCustomMapGenEnabled())
                return;
            if (!World.Procedural)
                return;
            var config = CustomMapGen.Instance?.GetConfig();
            if (config?.SwapMonuments == null || config.SwapMonuments.RunPostSaveSwap == false)
                return;
            // When swap is done in Save Prefix, we don't copy to temp or run at DONE — one file only.
            if (config.SwapMonuments.Enabled)
                return;
            string fullPath = Path.GetFullPath(fileName);
            if (!File.Exists(fullPath))
            {
                fullPath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, fileName));
                if (!File.Exists(fullPath))
                    return;
            }

            // Legacy: copy to maps/temp so PostSaveSwap runs at DONE; then copy back (creates second file).
            string tempDir = Path.Combine(Environment.CurrentDirectory, "maps", "temp");
            string tempPath = Path.Combine(tempDir, Path.GetFileName(fullPath));
            if (!Directory.Exists(tempDir))
                Directory.CreateDirectory(tempDir);
            File.Copy(fullPath, tempPath, true);
            PostSaveSwap.PendingMapPath = tempPath;
            PostSaveSwap.PendingOriginalPath = fullPath;
            if (config?.DebugLogging == true)
                UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] Post-save: stashing path for PostSaveSwap at DONE, path={tempPath}, original={fullPath}");
            PostSaveSwap.PendingRunPostSaveSwap = true;
        }
    }
}
