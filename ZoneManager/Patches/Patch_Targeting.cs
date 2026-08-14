using HarmonyLib;
using UnityEngine;
using ZM = Oxide.Plugins.ZoneManager;

namespace ZoneManagerHarmony.Patches
{
    [HarmonyPatch(typeof(AutoTurret), nameof(AutoTurret.SetTarget))]
    public static class Patch_AutoTurret_SetTarget
    {
        [HarmonyPrefix]
        public static bool Prefix(AutoTurret __instance, BaseCombatEntity targ)
        {
            if (targ is BasePlayer player && ZM.Dispatch_OnTurretTarget(__instance, player) != null)
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(BradleyAPC), nameof(BradleyAPC.VisibilityTest))]
    public static class Patch_Bradley_VisibilityTest
    {
        [HarmonyPrefix]
        public static bool Prefix(BradleyAPC __instance, BaseEntity ent, ref bool __result)
        {
            if (ent is BasePlayer player)
            {
                object result = ZM.Dispatch_CanBradleyApcTarget(__instance, player);
                if (result is bool b)
                {
                    __result = b;
                    return false;
                }
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(PatrolHelicopterAI), nameof(PatrolHelicopterAI.PlayerVisible))]
    public static class Patch_Heli_PlayerVisible
    {
        [HarmonyPrefix]
        public static bool Prefix(PatrolHelicopterAI __instance, BasePlayer ply, ref bool __result)
        {
            object result = ZM.Dispatch_CanHelicopterTarget(__instance, ply);
            if (result is bool b)
            {
                __result = b;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(PatrolHelicopterAI), nameof(PatrolHelicopterAI.ValidRocketTarget))]
    public static class Patch_Heli_ValidRocketTarget
    {
        [HarmonyPrefix]
        public static bool Prefix(PatrolHelicopterAI __instance, BasePlayer ply, ref bool __result)
        {
            object result = ZM.Dispatch_CanHelicopterStrafeTarget(__instance, ply);
            if (result is bool b)
            {
                __result = b;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(HelicopterTurret), nameof(HelicopterTurret.SetTarget))]
    public static class Patch_HeliTurret_SetTarget
    {
        [HarmonyPrefix]
        public static bool Prefix(HelicopterTurret __instance, BaseCombatEntity newTarget)
        {
            if (newTarget is BasePlayer player && ZM.Dispatch_OnHelicopterTarget(__instance, player) != null)
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(HumanNPC), nameof(HumanNPC.GetBestTarget))]
    public static class Patch_HumanNPC_GetBestTarget
    {
        [HarmonyPostfix]
        public static void Postfix(HumanNPC __instance, ref BaseEntity __result)
        {
            if (__result is BasePlayer player && ZM.Dispatch_OnNpcTarget(__instance, player) != null)
                __result = null;
        }
    }

    [HarmonyPatch(typeof(BaseNpc), nameof(BaseNpc.GetWantsToAttack))]
    public static class Patch_BaseNpc_GetWantsToAttack
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseNpc __instance, BaseEntity target, ref float __result)
        {
            if (target is BasePlayer player && ZM.Dispatch_OnNpcTarget(__instance, player) != null)
            {
                __result = 0f;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(BaseMountable), nameof(BaseMountable.MountPlayer))]
    public static class Patch_CanMountEntity
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseMountable __instance, BasePlayer player)
        {
            return ZM.Dispatch_CanMountEntity(player, __instance) == null;
        }
    }

    [HarmonyPatch(typeof(BaseMountable), nameof(BaseMountable.DismountPlayer), new[] { typeof(BasePlayer), typeof(bool) })]
    public static class Patch_CanDismountEntity
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseMountable __instance, BasePlayer player)
        {
            return ZM.Dispatch_CanDismountEntity(player, __instance) == null;
        }
    }
}
