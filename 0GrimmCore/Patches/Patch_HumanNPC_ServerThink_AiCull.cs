using HarmonyLib;
using UnityEngine;

namespace GrimmCoreHarmony.Patches
{
    /// <summary>
    /// nivex cull: skip vanilla map scientists. Tagged custom HumanNPCs still think.
    /// Does not patch animals (BaseNpc / BaseNPC2) — those keep stock brains.
    /// </summary>
    [HarmonyPatch(typeof(HumanNPC), nameof(HumanNPC.ServerThink))]
    internal static class Patch_HumanNPC_ServerThink_AiCull
    {
        [HarmonyPrefix]
        private static bool Prefix(HumanNPC __instance, float delta)
        {
            if (__instance.IsDestroyed)
                return false;

            // Keep custom/marked HumanNPCs running AI.
            if (GrimmCoreEntityMarkers.IsAiAllowed(__instance))
                return true;

            // Otherwise the old behavior just skipped ServerThink (brain-dead NPCs still on the map).
            // User-facing fix: remove those vanilla map scientists entirely.
            try
            {
                if (!__instance.IsDead())
                    __instance.Kill(BaseNetworkable.DestroyMode.Gib, callOnKilled: false);
            }
            catch { }

            // Skip vanilla ServerThink for these instances (they're being removed).
            return false;
        }
    }
}
