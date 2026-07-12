using HarmonyLib;
using ATPlugin = Oxide.Plugins.ArmoredTrain;

namespace ArmoredTrain.Patches
{
    /// <summary>
    /// Port of the ArmoredTrain OnEntityDeath(...) Oxide overloads (economy rewards + driver-killed
    /// handling). Postfix so the entity has already registered its death.
    /// </summary>
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Die), new[] { typeof(HitInfo) })]
    public static class Patch_BaseCombatEntity_Die
    {
        [HarmonyPostfix]
        public static void Postfix(BaseCombatEntity __instance, HitInfo info)
        {
            ATPlugin.Dispatch_Die(__instance, info);
        }
    }
}
