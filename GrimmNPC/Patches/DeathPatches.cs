using HarmonyLib;
using Rust;
using UnityEngine;

namespace GrimmNPC.Patches
{
    /// <summary>
    /// Patches NPCPlayer.CreateCorpse to trigger bomber explosion on death.
    /// OnCorpsePopulate is an Oxide hook called from CreateCorpse, so we patch CreateCorpse
    /// and call the hook ourselves (like DefendableHomes does).
    /// </summary>
    [HarmonyPatch(typeof(NPCPlayer), nameof(NPCPlayer.CreateCorpse))]
    public class NPCPlayer_CreateCorpse_Patch
    {
        static void Prefix(NPCPlayer __instance)
        {
            if (__instance == null || !(__instance is ScientistNPC)) return;
            ScientistNPC npc = __instance as ScientistNPC;
            if (!GrimmNPC.IsCustomNpc(npc)) return;

            ulong netId = npc.net?.ID.Value ?? 0;
            var data = GrimmNPC.GetNpcData(netId);
            if (data == null || !data.IsBomber) return;

            Effect.server.Run("assets/prefabs/tools/c4/effects/c4_explosion.prefab",
                npc.transform.position + new Vector3(0f, 1f, 0f), Vector3.up, null, true);

            GrimmNPC.CallOxideHook("OnBomberExplosion", npc, null);
        }
    }
}
