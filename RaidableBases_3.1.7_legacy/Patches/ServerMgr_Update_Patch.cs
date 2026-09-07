/*
 * Runs deferred RaidableBases server init when the mod loads before/after ItemManager.
 * Also retries if soft-start never finished (cold boot race with CopyPaste / scene load).
 */
using HarmonyLib;
using UnityEngine;

namespace RaidableBases
{
    [HarmonyPatch(typeof(ServerMgr), "Update")]
    internal static class ServerMgr_Update_Patch
    {
        private static float _nextWatchdog;

        [HarmonyPostfix]
        static void Postfix()
        {
            var entry = RaidableBasesHarmonyEntry.Instance;
            if (entry == null)
                return;

            if (RaidableBasesHarmonyEntry.DeferredServerInitPending)
            {
                // Wait until items exist — ItemManager.Initialize may not have run yet on cold boot.
                if (ItemManager.itemList == null || ItemManager.itemList.Count == 0)
                    return;
                if (Rust.Application.isLoading || Rust.Application.isLoadingSave)
                    return;

                RaidableBasesHarmonyEntry.DeferredServerInitPending = false;
                try
                {
                    entry.RunDeferredServerInit();
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("[RaidableBases] Deferred server init failed: " + ex);
                    RaidableBasesHarmonyEntry.DeferredServerInitPending = true;
                }
                return;
            }

            // Watchdog: soft-start aborted mid-boot → queues never start → no auto spawns until harmony.load.
            if (Time.realtimeSinceStartup < _nextWatchdog)
                return;
            _nextWatchdog = Time.realtimeSinceStartup + 30f;

            if (Rust.Application.isLoading || Rust.Application.isLoadingSave)
                return;
            if (ItemManager.itemList == null || ItemManager.itemList.Count == 0)
                return;

            if (entry.NeedsSoftInitRetry())
            {
                Debug.LogWarning("[RaidableBases] Soft-start did not finish - retrying server init.");
                try
                {
                    entry.RunDeferredServerInit();
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("[RaidableBases] Soft-start retry failed: " + ex);
                }
            }
        }
    }
}
