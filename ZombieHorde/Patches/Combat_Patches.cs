using HarmonyLib;
using Rust;

namespace ZombieHorde.Patches
{
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), typeof(HitInfo))]
    internal static class BaseCombatEntity_Hurt_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            if (ConfigData.Configuration == null || info == null || __instance == null)
                return true;

            // Skip Hurt entirely when NPC/animal damage to zombies is disabled
            ZombieNPC victim = ZombieNPC.Get(__instance as BasePlayer);
            if (victim != null)
            {
                BasePlayer initiator = info.InitiatorPlayer;
                if (initiator != null && initiator.IsNpc && ZombieNPC.Get(initiator) == null
                    && !ConfigData.Configuration.Member.TargetedByNPCs)
                    return false;

                if (info.Initiator is BaseNpc && !(info.Initiator is BasePlayer)
                    && !ConfigData.Configuration.Member.TargetedByAnimals)
                    return false;
            }

            ZombieHordePlugin.Instance?.OnEntityTakeDamage(__instance, info);
            return true;
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Die), typeof(HitInfo))]
    internal static class BasePlayer_Die_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BasePlayer __instance, HitInfo info)
        {
            ZombieHordePlugin.Instance?.OnPlayerDeath(__instance, info);
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill), typeof(BaseNetworkable.DestroyMode), typeof(bool))]
    internal static class BaseNetworkable_Kill_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BaseNetworkable __instance)
        {
            ZombieHordePlugin.Instance?.OnEntityKill(__instance);
        }
    }

    /// <summary>Oxide OnExplosiveDud — ExplodeDudExplosives forces real explode; DespawnDudExplosives kills after dud.</summary>
    [HarmonyPatch(typeof(DudTimedExplosive), nameof(DudTimedExplosive.BecomeDud))]
    internal static class DudTimedExplosive_BecomeDud_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(DudTimedExplosive __instance)
        {
            var plugin = ZombieHordePlugin.Instance;
            if (plugin == null) return true;

            object result = plugin.OnExplosiveDud(__instance);
            if (result is bool b && !b)
            {
                // Force explode instead of becoming a dud (skip BecomeDud body).
                try
                {
                    var explode = AccessTools.Method(typeof(TimedExplosive), nameof(TimedExplosive.Explode));
                    explode?.Invoke(__instance, null);
                }
                catch { }
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ResourceDispenser), nameof(ResourceDispenser.DoGather))]
    internal static class ResourceDispenser_DoGather_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ResourceDispenser __instance)
        {
            ZombieHordePlugin.Instance?.OnDispenserGather(__instance);
        }
    }
}
