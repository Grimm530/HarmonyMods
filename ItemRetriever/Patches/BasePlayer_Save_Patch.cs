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
            if (plugin == null || info.msg?.basePlayer?.inventory?.invMain == null)
                return;

            // Oxide only fires for network snapshots with inventory; disk saves also have invMain when forDisk.
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
