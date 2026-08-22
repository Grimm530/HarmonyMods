using HarmonyLib;
using DHPlugin = Oxide.Plugins.DefendableHomes;

namespace DefendableHomes.Patches
{
    /// <summary>
    /// Oxide OnExplosiveThrown / OnExplosiveDropped. Both throw and drop call SetUpThrownWeapon after Spawn.
    /// </summary>
    [HarmonyPatch(typeof(ThrownWeapon), "SetUpThrownWeapon")]
    public static class Patch_ThrownWeapon_SetUpThrownWeapon
    {
        [HarmonyPostfix]
        public static void Postfix(ThrownWeapon __instance, BaseEntity ent, Item ownerItem)
        {
            if (__instance == null || ent == null) return;
            BasePlayer player = __instance.GetOwnerPlayer();
            if (player == null) player = ent.creatorEntity as BasePlayer;
            if (player == null) return;
            DHPlugin.Dispatch_OnExplosiveThrown(player, ent, __instance);
        }
    }
}
