using HarmonyLib;
using DHPlugin = Oxide.Plugins.DefendableHomes;

namespace DefendableHomes.Patches
{
    [HarmonyPatch(typeof(NPCPlayer), nameof(NPCPlayer.CreateCorpse))]
    public static class Patch_NPCPlayer_CreateCorpse
    {
        [HarmonyPostfix]
        public static void Postfix(NPCPlayer __instance, BaseCorpse __result)
        {
            if (__instance == null || __result == null) return;
            ScientistNPC scientist = __instance as ScientistNPC;
            NPCPlayerCorpse corpse = __result as NPCPlayerCorpse;
            if (scientist == null || corpse == null) return;
            DHPlugin.Dispatch_OnCorpsePopulate(scientist, corpse);
        }
    }
}
