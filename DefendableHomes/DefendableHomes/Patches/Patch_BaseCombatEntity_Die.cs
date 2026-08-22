using HarmonyLib;
using DHPlugin = Oxide.Plugins.DefendableHomes;

namespace DefendableHomes.Patches
{
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Die), new[] { typeof(HitInfo) })]
    public static class Patch_BaseCombatEntity_Die
    {
        [HarmonyPostfix]
        public static void Postfix(BaseCombatEntity __instance, HitInfo info)
        {
            DHPlugin.Dispatch_Die(__instance, info);
        }
    }
}
