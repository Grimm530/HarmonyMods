// OnEntityTakeDamage / OnEntityDeath
// Damage: prefix on BaseCombatEntity.Hurt(HitInfo). Non-null dispatch result cancels (return false).
// Death:  postfix on BaseCombatEntity.Die(HitInfo). Routes heli/bradley/npc for Loot Defender.
using HarmonyLib;
using UnityEngine;
using TPVE = Oxide.Plugins.TruePVE;

namespace TruePVEHarmony.Patches
{
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), new[] { typeof(HitInfo) })]
    public static class Patch_BaseCombatEntity_Hurt
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            // OnPlayerAttack runs first so Loot Defender can clear laptop HitBone before damage rules.
            if (info?.InitiatorPlayer != null)
                TPVE.Dispatch_OnPlayerAttack(info.InitiatorPlayer, info);

            object result = TPVE.Dispatch_OnEntityTakeDamage(__instance, info);
            return result == null; // null -> allow; non-null -> block
        }
    }

    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Die), new[] { typeof(HitInfo) })]
    public static class Patch_BaseCombatEntity_Die
    {
        [HarmonyPostfix]
        public static void Postfix(BaseCombatEntity __instance, HitInfo info)
        {
            if (__instance == null) return;
            try { TPVE.Dispatch_OnEntityDeath(__instance, info); }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] OnEntityDeath: " + ex.Message); }
        }
    }
}
