using HarmonyLib;
using Rust.Ai;
using UnityEngine;

namespace GrimmNPC.Patches
{
    /// <summary>
    /// Shared raiding behavior for custom NPCs.
    /// Runs after AI thinking to allow NPCs to raid when no LOS to target player OR when RaidGoalActive is true.
    /// Only runs for NPCs with IsRaidingNpc=true OR when EnableRaidingForAllNpcs config is true.
    /// 
    /// IMPORTANT: This does NOT prevent NPCs from using normal combat weapons (rocket launchers, etc.) against players.
    /// NPCs will use normal combat weapons when they have LOS to players (handled by ThinkPatches).
    /// Raiding only activates when LOS is blocked (finds blocking structures) OR when RaidGoalActive is true.
    /// </summary>
    [HarmonyPatch(typeof(BaseAIBrain), nameof(BaseAIBrain.Think))]
    public class BaseAIBrain_Think_Raiding_Patch
    {
        static void Postfix(BaseAIBrain __instance)
        {
            if (__instance == null) return;

            ScientistNPC npc = __instance.GetBaseEntity() as ScientistNPC;
            if (npc == null || !GrimmNPC.IsCustomNpc(npc)) return;

            ulong netId = npc.net?.ID.Value ?? 0;
            if (netId == 0) return;

            var npcData = GrimmNPC.GetNpcData(netId);
            if (npcData == null) return;

            // Only run raiding if explicitly enabled for this NPC OR if global config allows it
            var config = GrimmNPC.GetConfig();
            if (!npcData.IsRaidingNpc && !config.EnableRaidingForAllNpcs)
                return;

            Raid.TickRaid(npc);
        }
    }
}
