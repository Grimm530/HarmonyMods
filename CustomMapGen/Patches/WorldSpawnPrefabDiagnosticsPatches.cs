using System;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace CustomMapGen.Patches
{
    /// <summary>
    /// When <see cref="MapGenConfig.DebugLogSkippedWorldPrefabs"/> is true, logs each time
    /// <see cref="World"/> would silently skip spawning because <see cref="Prefab.Load(uint)"/> wrapped a null
    /// GameObject (<c>FindPrefab</c> failed). Vanilla code: <c>if (prefab != null &amp;&amp; (bool)prefab.Object)</c>.
    /// Industrial / electrical IO prefabs often hit this on dedicated if asset scenes are wrong or IDs mismatch — no error otherwise.
    /// </summary>
    [HarmonyPatch(typeof(World), "SpawnPrefab", new Type[] { typeof(string), typeof(Prefab), typeof(Vector3), typeof(Quaternion), typeof(Vector3) })]
    internal static class World_SpawnPrefab_Diagnostics_Prefix
    {
        private static int _seenAnySpawnPrefabCall;

        static void Prefix(string category, Prefab prefab, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var cfg = CustomMapGen.Instance?.GetConfig();
            if (cfg == null || !cfg.Enabled)
                return;

            if (cfg.DebugLogging || cfg.DebugLogSkippedWorldPrefabs)
                SwapSpawnTracking.RecordSpawnAttempt(prefab);

            if (!cfg.DebugLogSkippedWorldPrefabs)
                return;

            if (Interlocked.Exchange(ref _seenAnySpawnPrefabCall, 1) == 0)
                UnityEngine.Debug.Log("[CustomMapGen] World.SpawnPrefab diagnostics hook is active.");

            if (prefab == null)
            {
                UnityEngine.Debug.LogWarning("[CustomMapGen] World.SpawnPrefab: Prefab wrapper is null (unexpected). category=" + category + " pos=" + position);
                return;
            }

            if (prefab.Object != null)
                return;

            string path = prefab.Name ?? "";
            UnityEngine.Debug.LogWarning(
                "[CustomMapGen] World.SpawnPrefab SKIP (silent vanilla behavior): GameManager.FindPrefab returned null — " +
                "nothing will spawn. prefabId=" + prefab.ID + " path=\"" + path + "\" category=\"" + category + "\" pos=" + position +
                " scale=" + scale +
                ". Fix: verify prefab ID in map matches this server build; ensure AssetBundleBackend loaded required scenes for this path.");
        }
    }

    [HarmonyPatch(typeof(ServerMgr), "Update")]
    internal static class ServerMgr_Update_SwapTrackingPump_Patch
    {
        static void Prefix()
        {
            SwapSpawnTracking.PumpMainThread();
        }
    }
}
