using HarmonyLib;
using BNPlugin = Harmony.Plugins.BetterNpc;

namespace BetterNpc.Patches
{
    [HarmonyPatch(typeof(NPCPlayer), nameof(NPCPlayer.CreateCorpse))]
    public static class Patch_NPCPlayer_CreateCorpse
    {
        [HarmonyPostfix]
        public static void Postfix(NPCPlayer __instance, BaseCorpse __result)
        {
            if (__result == null) return;
            ScientistNPC scientist = __instance as ScientistNPC;
            NPCPlayerCorpse corpse = __result as NPCPlayerCorpse;
            if (scientist == null || corpse == null) return;
            BNPlugin.Dispatch_OnCorpsePopulate(scientist, corpse);
        }
    }
}
