using HarmonyLib;
using ATPlugin = Oxide.Plugins.ArmoredTrain;

namespace ArmoredTrain.Patches
{
    /// <summary>
    /// Port of ArmoredTrain OnLootEntityEnd(...): when a player stops looting an emptied event crate,
    /// the crate is destroyed. PlayerLoot.Clear() is the stop-looting entry point; entitySource still
    /// references the looted entity when Clear runs, so we read it in the prefix.
    /// </summary>
    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.Clear))]
    public static class Patch_PlayerLoot_Clear
    {
        [HarmonyPrefix]
        public static void Prefix(PlayerLoot __instance)
        {
            BasePlayer player = __instance?.baseEntity;
            BaseEntity source = __instance?.entitySource;
            if (player == null || source == null) return;
            ATPlugin.Dispatch_OnLootEntityEnd(player, source);
        }
    }
}
