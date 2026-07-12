using HarmonyLib;
using Rust.Ai.Gen2;

namespace GrimmNPC2.Patches
{
    /// <summary>
    /// Merges <see cref="GrimmNPC2.TryEvaluateTargetAgainstPolicy"/> into stock GEN2 sense targeting so
    /// HarmonyConfig/GrimmNPC2.json (CanTarget*, exclusions, per-NPC Ignore*) actually applies to automatic AI.
    /// </summary>
    [HarmonyPatch(typeof(SenseComponent), nameof(SenseComponent.CanTarget))]
    internal static class SenseComponentCanTargetPatchGen2
    {
        private static void Postfix(SenseComponent __instance, BaseEntity entity, ref bool __result)
        {
            if (!__result) return;
            BaseEntity owner = __instance?.baseEntity;
            if (owner == null || !GrimmNPC2.IsCustomNpc(owner)) return;
            if (!GrimmNPC2.TryEvaluateTargetAgainstPolicy(owner, entity, out _))
                __result = false;
        }
    }
}
