using HarmonyLib;
using BNPlugin = Harmony.Plugins.BetterNpc;

namespace BetterNpc.Patches
{
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Die), new[] { typeof(HitInfo) })]
    public static class Patch_BaseCombatEntity_Die
    {
        [HarmonyPostfix]
        public static void Postfix(BaseCombatEntity __instance, HitInfo info)
        {
            BNPlugin.Dispatch_Die(__instance, info);
        }
    }
}
