using HarmonyLib;

namespace ItemRetrieverHarmony
{
    /// <summary>
    /// Oxide OnEntitySaved(BasePlayer, SaveInfo) — inject supplier items into networked inventory save.
    /// </summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Save))]
    internal static class BasePlayer_Save_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance, BaseNetworkable.SaveInfo info)
        {
            var plugin = ItemRetrieverHost.Instance?.Plugin;
            // Match Oxide IOnEntitySaved: network snapshots only. Disk saves include invMain, and
            // injecting supplier items there writes ulong.MaxValue fake ItemIds into the world .sav
            // (RegisterUID on load then exhausts TakeUID: "ran out of available UIDs").
            if (plugin == null || info.forDisk || info.forConnection == null)
                return;
            if (info.msg?.basePlayer?.inventory?.invMain == null)
                return;

            try
            {
                plugin.OnEntitySaved(__instance, info);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning("[ItemRetriever] OnEntitySaved: " + ex.Message);
            }
        }
    }
}
