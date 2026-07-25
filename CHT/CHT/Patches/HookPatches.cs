using HarmonyLib;
using UnityEngine;

namespace CHT.Patches
{
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), new[] { typeof(HitInfo) })]
    public static class HurtPatch
    {
        [HarmonyPrefix]
        static bool Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            var plugin = CHTMod.Plugin;
            if (plugin == null || info == null) return true;

            if (__instance is PatrolHelicopter heli)
                plugin.OnPatrolHelicopterTakeDamage(heli, info);

            // Handler clears damageTypes when blocked; always continue so 0-damage Hurt is cheap.
            plugin.OnEntityTakeDamage(__instance, info);
            return true;
        }
    }

    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Die), new[] { typeof(HitInfo) })]
    public static class DiePatch
    {
        [HarmonyPostfix]
        static void Postfix(BaseCombatEntity __instance, HitInfo info)
        {
            if (__instance is PatrolHelicopter heli)
                CHTMod.Plugin?.OnEntityDeath(heli, info);
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Spawn))]
    public static class SpawnPatch
    {
        [HarmonyPostfix]
        static void Postfix(BaseNetworkable __instance)
        {
            if (__instance is TimedExplosive explosive)
                CHTMod.Plugin?.OnEntitySpawned(explosive);
        }
    }

    [HarmonyPatch(typeof(ResourceDispenser), "GiveResourceFromItem")]
    public static class GatherPatch
    {
        [HarmonyPrefix]
        static bool Prefix(ResourceDispenser __instance, BasePlayer entity)
        {
            // Skip vanilla salvage when tier override applies.
            return CHTMod.Plugin?.TryOverrideDebrisSalvage(__instance, entity) != true;
        }
    }

    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.StartLootingEntity), new[] { typeof(BaseEntity), typeof(bool) })]
    public static class LootPatch
    {
        [HarmonyPrefix]
        static bool Prefix(PlayerLoot __instance, BaseEntity targetEntity, ref bool __result)
        {
            BasePlayer player = __instance?.baseEntity;
            if (player == null || !(targetEntity is LootContainer container))
                return true;

            if (CHTMod.Plugin?.CanLootEntity(player, container) != null)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(PatrolHelicopterAI), nameof(PatrolHelicopterAI.CanStrafe))]
    public static class CanStrafePatch
    {
        [HarmonyPrefix]
        static bool Prefix(PatrolHelicopterAI __instance, ref bool __result)
        {
            object r = CHTMod.Plugin?.CanHelicopterStrafe(__instance);
            if (r is bool b)
            {
                __result = b;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(PatrolHelicopterAI), nameof(PatrolHelicopterAI.CanUseNapalm))]
    public static class CanNapalmPatch
    {
        [HarmonyPrefix]
        static bool Prefix(PatrolHelicopterAI __instance, ref bool __result)
        {
            object r = CHTMod.Plugin?.CanHelicopterUseNapalm(__instance);
            if (r is bool b)
            {
                __result = b;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(PatrolHelicopterAI), "StartStrafe")]
    public static class StartStrafeTargetPatch
    {
        [HarmonyPrefix]
        static bool Prefix(PatrolHelicopterAI __instance, BasePlayer strafeTarget)
        {
            object r = CHTMod.Plugin?.CanHelicopterStrafeTarget(__instance, strafeTarget);
            return !(r is bool b && !b);
        }
    }

    [HarmonyPatch(typeof(PatrolHelicopter), nameof(PatrolHelicopter.IsValidHomingTarget))]
    public static class HomingPatch
    {
        [HarmonyPrefix]
        static bool Prefix(PatrolHelicopter __instance, ref bool __result)
        {
            object r = CHTMod.Plugin?.OnCanBeHomingTargeted(__instance);
            if (r is bool b)
            {
                __result = b;
                return false;
            }
            return true;
        }
    }
}
