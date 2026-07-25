using System;
using HarmonyLib;
using Rust;
using UnityEngine;

namespace GrimmNPC
{
    /// <summary>
    /// Harmony patches that replace Oxide hooks used by NpcSpawn. Calls the same methods on GrimmNPC.
    /// Nested types: GrimmNPC.CustomScientistNpc
    /// </summary>
    internal static class OxideHooksPatches
    {
        private static GrimmNPC Ins => GrimmNPC.Instance;

        [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill))]
        private static class Patch_OnEntityKill
        {
            private static void Prefix(BaseNetworkable __instance)
            {
                if (Ins == null) return;
                if (__instance is GrimmNPC.CustomScientistNpc npc)
                    Ins.OnEntityKill(npc);
            }
        }

        [HarmonyPatch(typeof(NPCPlayer), nameof(NPCPlayer.CreateCorpse))]
        private static class Patch_OnCorpsePopulate
        {
            private static void Postfix(NPCPlayer __instance, BaseCorpse __result)
            {
                if (Ins == null || __result == null) return;
                if (__instance is GrimmNPC.CustomScientistNpc npc && __result is NPCPlayerCorpse corpse)
                    Ins.OnCorpsePopulate(npc, corpse);
            }
        }

        [HarmonyPatch(typeof(BradleyAPC), nameof(BradleyAPC.VisibilityTest))]
        private static class Patch_CanBradleyApcTarget
        {
            private static bool Prefix(BradleyAPC __instance, BaseEntity ent, ref bool __result)
            {
                if (Ins == null || !(ent is GrimmNPC.CustomScientistNpc npc)) return true;
                object r = Ins.CanBradleyApcTarget(__instance, npc);
                if (r is bool b)
                {
                    __result = b;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(AutoTurret), nameof(AutoTurret.SetTarget))]
        private static class Patch_OnTurretTarget
        {
            private static bool Prefix(AutoTurret __instance, BaseCombatEntity targ)
            {
                if (Ins == null || targ == null) return true;
                object r = Ins.OnTurretTarget(__instance, targ);
                return r == null;
            }
        }

        [HarmonyPatch(typeof(AutoTurret), nameof(AutoTurret.ObjectVisible))]
        private static class Patch_CanBeTargeted_AutoTurret
        {
            private static bool Prefix(AutoTurret __instance, BaseCombatEntity obj, ref bool __result)
            {
                if (Ins == null || obj == null) return true;
                object r = Ins.CanBeTargeted(obj, __instance);
                if (r is bool b)
                {
                    __result = b;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), typeof(HitInfo))]
        private static class Patch_OnEntityTakeDamage
        {
            private static bool Prefix(BaseCombatEntity __instance, HitInfo info)
            {
                if (Ins == null || __instance == null || info == null) return true;
                object r = Ins.OnEntityTakeDamage(__instance, info);
                return r == null;
            }
        }

        [HarmonyPatch(typeof(Item), nameof(Item.LoseCondition))]
        private static class Patch_OnLoseCondition
        {
            private static void Prefix(Item __instance, ref float amount)
            {
                Ins?.OnLoseCondition(__instance, ref amount);
            }
        }

        [HarmonyPatch(typeof(HumanNPC), nameof(HumanNPC.GetBestTarget))]
        private static class Patch_OnNpcTarget_GetBestTarget
        {
            private static void Postfix(HumanNPC __instance, ref BaseEntity __result)
            {
                if (Ins == null || __instance == null || __result == null) return;
                if (__instance is ScientistNPC sci)
                {
                    object r = Ins.OnNpcTarget(sci, __result);
                    if (r != null) { __result = null; return; }
                }
                if (__result is GrimmNPC.CustomScientistNpc victim && __instance is NPCPlayer attacker)
                {
                    object r2 = Ins.OnNpcTarget(attacker, victim);
                    if (r2 != null) __result = null;
                }
            }
        }

        // GetWantsToAttack is declared on BaseNpc (BaseAnimalNPC inherits it; do not patch BaseAnimalNPC directly).
        [HarmonyPatch(typeof(BaseNpc), nameof(BaseNpc.GetWantsToAttack))]
        private static class Patch_OnNpcTarget_BaseNpc
        {
            private static bool Prefix(BaseNpc __instance, BaseEntity target, ref float __result)
            {
                if (Ins == null || __instance == null || target == null) return true;
                if (!(__instance is BaseAnimalNPC animal) || !(target is GrimmNPC.CustomScientistNpc victim))
                    return true;

                object r = Ins.OnNpcTarget(animal, victim);
                if (r != null)
                {
                    __result = 0f;
                    return false;
                }
                return true;
            }
        }
    }
}
