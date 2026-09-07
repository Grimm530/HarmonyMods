using HarmonyLib;
using UnityEngine;

namespace GrimmCoreHarmony.Patches
{
    /// <summary>
    /// Same cull as HumanNPC for map scarecrows (NPCPlayer, not HumanNPC). Custom tagged scarecrows still think.
    /// </summary>
    [HarmonyPatch(typeof(ScarecrowNPC), nameof(ScarecrowNPC.ServerThink))]
    internal static class Patch_ScarecrowNPC_ServerThink_AiCull
    {
        [HarmonyPrefix]
        private static bool Prefix(ScarecrowNPC __instance, float delta)
        {
            if (__instance.IsDestroyed)
                return false;

            // Keep custom/marked scarecrows running AI.
            if (GrimmCoreEntityMarkers.IsAiAllowed(__instance))
                return true;

            // Otherwise the old behavior just skipped ServerThink (brain-dead NPCs still on the map).
            // User-facing fix: remove those vanilla map scarecrows entirely.
            try
            {
                if (!__instance.IsDead())
                    __instance.Kill(BaseNetworkable.DestroyMode.Gib, callOnKilled: false);
            }
            catch { }

            return false;
        }
    }
}
