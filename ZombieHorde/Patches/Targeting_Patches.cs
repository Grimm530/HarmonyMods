using HarmonyLib;
using UnityEngine;

namespace ZombieHorde.Patches
{
    [HarmonyPatch(typeof(AutoTurret), nameof(AutoTurret.SetTarget))]
    internal static class AutoTurret_SetTarget_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(AutoTurret __instance, BaseCombatEntity targ)
        {
            object result = ZombieHordePlugin.Instance?.CanBeTargeted(targ, __instance);
            if (result is bool b && !b)
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(AutoTurret), nameof(AutoTurret.ObjectVisible))]
    internal static class AutoTurret_ObjectVisible_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(AutoTurret __instance, BaseCombatEntity obj, ref bool __result)
        {
            object result = ZombieHordePlugin.Instance?.CanBeTargeted(obj, __instance);
            if (result is bool b)
            {
                __result = b;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(BradleyAPC), nameof(BradleyAPC.VisibilityTest))]
    internal static class BradleyAPC_VisibilityTest_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(BradleyAPC __instance, BaseEntity ent, ref bool __result)
        {
            object result = ZombieHordePlugin.Instance?.CanBradleyApcTarget(__instance, ent);
            if (result is bool b)
            {
                __result = b;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(PatrolHelicopterAI), "TryAddTarget")]
    internal static class PatrolHelicopterAI_TryAddTarget_Patch
    {
        static bool Prepare() => AccessTools.Method(typeof(PatrolHelicopterAI), "TryAddTarget") != null;

        [HarmonyPrefix]
        private static bool Prefix(BasePlayer ply)
        {
            object result = ZombieHordePlugin.Instance?.CanHelicopterTarget(null, ply);
            if (result is bool b && !b)
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(HumanNPC), nameof(HumanNPC.GetBestTarget))]
    internal static class HumanNPC_GetBestTarget_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(HumanNPC __instance, ref BaseEntity __result)
        {
            if (__instance == null || __result == null) return;
            // Block CH47 / vanilla scientists from selecting horde zombies
            if (ZombieNPC.Get(__result as BasePlayer) == null) return;
            if (ConfigData.Configuration?.Member != null && ConfigData.Configuration.Member.TargetedByNPCs)
                return;
            __result = null;
        }
    }

    [HarmonyPatch(typeof(HumanNPC), nameof(HumanNPC.IsTarget))]
    internal static class HumanNPC_IsTarget_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(BaseEntity entity, ref bool __result)
        {
            if (entity == null || ZombieNPC.Get(entity as BasePlayer) == null) return true;
            if (ConfigData.Configuration?.Member != null && !ConfigData.Configuration.Member.TargetedByNPCs)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(HumanNPC), nameof(HumanNPC.IsThreat))]
    internal static class HumanNPC_IsThreat_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(BaseEntity entity, ref bool __result)
        {
            if (entity == null || ZombieNPC.Get(entity as BasePlayer) == null) return true;
            if (ConfigData.Configuration?.Member != null && !ConfigData.Configuration.Member.TargetedByNPCs)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(BaseNpc), nameof(BaseNpc.GetWantsToAttack))]
    internal static class BaseNpc_GetWantsToAttack_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(BaseNpc __instance, BaseEntity target, ref float __result)
        {
            if (__instance == null || target == null) return true;
            object r = ZombieHordePlugin.Instance?.OnNpcTarget(__instance, target);
            if (r != null)
            {
                __result = 0f;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(TriggerBase), "OnTriggerEnter")]
    internal static class TriggerSafeZone_Enter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(TriggerBase __instance, Collider collider)
        {
            if (!(__instance is TriggerSafeZone safeZone) || collider == null)
                return true;

            BaseEntity ent = collider.ToBaseEntity();
            object result = ZombieHordePlugin.Instance?.OnEntityEnterSafeZone(safeZone, ent);
            if (result != null)
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(NPCPlayer), nameof(NPCPlayer.CreateCorpse))]
    internal static class NPCPlayer_CreateCorpse_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(NPCPlayer __instance, BaseCorpse __result)
        {
            if (__result is NPCPlayerCorpse corpse && __instance is ScientistNPC scientist)
                ZombieHordePlugin.Instance?.OnCorpsePopulate(corpse, scientist);
        }
    }

    [HarmonyPatch(typeof(TravellingVendor), "IsInvalidPlayer")]
    internal static class TravellingVendor_IsInvalidPlayer_Patch
    {
        static bool Prepare() => AccessTools.Method(typeof(TravellingVendor), "IsInvalidPlayer") != null;

        [HarmonyPrefix]
        private static bool Prefix(BasePlayer player, ref bool __result)
        {
            if (player is NPCPlayer)
            {
                __result = true;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// GunTrap/FlameTurret have no Oxide-style CanBeTargeted entry; cancel damage from traps when config forbids targeting.
    /// GrimmNPC NpcConfig flags also apply for CustomScientistNpc skin NPCs.
    /// </summary>
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), typeof(HitInfo))]
    internal static class TrapDamage_Hurt_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            if (info?.Initiator == null || __instance == null) return true;
            if (!(info.Initiator is GunTrap or FlameTurret)) return true;

            ZombieNPC zombie = ZombieNPC.Get(__instance as BasePlayer);
            if (zombie == null) return true;

            object result = ZombieHordePlugin.Instance?.CanBeTargeted(__instance, info.Initiator);
            if (result is bool b && !b)
            {
                info.damageTypes.ScaleAll(0f);
                return false;
            }
            return true;
        }
    }
}
