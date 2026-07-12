/*
 * Marks that ItemManager is ready. Actual soft-start runs from ServerMgr_Update_Patch
 * once the world is finished loading (avoids cold-boot races).
 */
using HarmonyLib;

namespace RaidableBases
{
    [HarmonyPatch(typeof(ItemManager), nameof(ItemManager.Initialize))]
    internal static class ItemManager_Initialize_Patch
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            // Keep DeferredServerInitPending true so ServerMgr_Update starts soft-init
            // after Application.isLoading / isLoadingSave clear.
            if (RaidableBasesHarmonyEntry.Instance == null)
                return;
            if (!RaidableBasesHarmonyEntry.DeferredServerInitPending)
                RaidableBasesHarmonyEntry.DeferredServerInitPending = true;
        }
    }
}
