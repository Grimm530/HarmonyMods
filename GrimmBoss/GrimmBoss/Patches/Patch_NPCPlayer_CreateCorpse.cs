using HarmonyLib;
using GBPlugin = Oxide.Plugins.GrimmBoss;

namespace GrimmBoss.Patches
{
    /// <summary>Oxide OnCorpsePopulate — boss loot / respawn queue / economy on death.</summary>
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
            GBPlugin.Dispatch_OnCorpsePopulate(scientist, corpse);
        }
    }
}
