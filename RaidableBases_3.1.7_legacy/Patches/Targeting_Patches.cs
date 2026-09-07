using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RaidableBases
{
    /// <summary>
    /// Oxide injects OnEntityEnter / CanEntityBeTargeted / OnTrapTrigger / OnInterferenceUpdate.
    /// Under Harmony those hooks must be invoked explicitly.
    /// Raid AutoTurrets use RB_SKIN_ID (Oxide used 14922524) — both are treated as raid skins.
    /// </summary>
    internal static class RaidTurretIds
    {
        internal const ulong Skin = 3710562502UL;
        internal const ulong OxideSkin = 14922524UL;

        internal static bool IsRaidSkin(ulong skin) => skin == Skin || skin == OxideSkin;

        internal static bool IsSteamPlayer(BasePlayer player)
        {
            if (player == null || player.IsDestroyed || player.IsNpc)
                return false;
            // Steam64 IDs are 17-digit values starting with 7656...
            ulong id = (ulong)player.userID;
            return id > 70000000000000000UL;
        }
    }

    /// <summary>
    /// Oxide: OnInterferenceUpdate — non-null skips jam. Clustered raid turrets must not stay OnFire.
    /// </summary>
    [HarmonyPatch(typeof(AutoTurret), "ShouldApplyInterference")]
    internal static class AutoTurret_ShouldApplyInterference_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(AutoTurret __instance, ref bool __result)
        {
            if (__instance == null)
                return;
            if (RaidTurretIds.IsRaidSkin(__instance.skinID))
            {
                __result = false;
                return;
            }
            var hook = Interface.CallHook("OnInterferenceUpdate", __instance);
            if (hook is bool skip && skip)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(AutoTurret), "RecalculateInterference")]
    internal static class AutoTurret_RecalculateInterference_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(AutoTurret __instance, ref bool __result)
        {
            if (__instance == null)
                return true;

            if (RaidTurretIds.IsRaidSkin(__instance.skinID)
                || Interface.CallHook("OnInterferenceUpdate", __instance) is bool)
            {
                if (__instance.HasFlag(BaseEntity.Flags.OnFire))
                    __instance.SetFlag(BaseEntity.Flags.OnFire, false, false, true);
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(AutoTurret), "AddNearbyTurrets")]
    internal static class AutoTurret_AddNearbyTurrets_Patch
    {
        private static readonly FieldInfo NearbyField =
            typeof(AutoTurret).GetField("nearbyTurrets", BindingFlags.Instance | BindingFlags.NonPublic);

        [HarmonyPrefix]
        private static bool Prefix(AutoTurret __instance)
        {
            if (__instance == null)
                return true;

            var list = Facepunch.Pool.Get<List<AutoTurret>>();
            object hook = Interface.CallHook("OnNearbyTurretsScan", __instance, list)
                          ?? Interface.CallHook("OnNearbyTurretsScan", __instance);
            Facepunch.Pool.FreeUnmanaged(ref list);

            if (hook != null || RaidTurretIds.IsRaidSkin(__instance.skinID))
            {
                if (NearbyField?.GetValue(__instance) is HashSet<AutoTurret> nearby)
                    nearby.Clear();
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(TriggerBase), nameof(TriggerBase.OnEntityEnter), typeof(BaseEntity))]
    internal static class TriggerBase_OnEntityEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(TriggerBase __instance, BaseEntity ent)
        {
            if (__instance == null || ent == null)
                return true;

            if (__instance is TriggerEnterTimer timer)
            {
                var hopperResult = Interface.CallHook("OnEntityEnter", timer, ent);
                return hopperResult == null;
            }

            // Raid AutoTurret / trap triggers: steam players must always enter (Oxide + vanilla need entityContents).
            // Never cancel their enter based on CanEntityBeTargeted — that was emptying triggers so turrets went blind.
            if (ent is BasePlayer player && RaidTurretIds.IsSteamPlayer(player))
            {
                BaseEntity owner = null;
                try { owner = __instance.gameObject.ToBaseEntity(); } catch { }
                if (owner != null && RaidTurretIds.IsRaidSkin(owner.skinID)
                    && (owner is AutoTurret || owner is GunTrap || owner is FlameTurret || owner is BaseDetector))
                {
                    return true;
                }
            }

            if (ent is BasePlayer bp)
            {
                var result = Interface.CallHook("OnEntityEnter", __instance, bp);
                // Oxide: non-null cancels enter (used to keep raid NPCs out of GunTraps).
                return result == null;
            }

            if (ent is Drone drone)
            {
                var result = Interface.CallHook("OnEntityEnter", __instance, drone);
                return result == null;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(AutoTurret), nameof(AutoTurret.ObjectVisible))]
    internal static class AutoTurret_ObjectVisible_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(AutoTurret __instance, BaseCombatEntity obj, ref bool __result)
        {
            if (__instance == null || obj == null)
                return true;

            // Raid turrets: never deny steam players via plugin hook — vanilla LOS only.
            if (RaidTurretIds.IsRaidSkin(__instance.skinID)
                && obj is BasePlayer player
                && RaidTurretIds.IsSteamPlayer(player))
            {
                return true;
            }

            if (obj is not BasePlayer bp)
                return true;

            var result = Interface.CallHook("CanEntityBeTargeted", bp, (BaseEntity)__instance);
            if (result is bool can && !can)
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(AutoTurret), nameof(AutoTurret.SetTarget))]
    internal static class AutoTurret_SetTarget_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(AutoTurret __instance, BaseCombatEntity targ)
        {
            if (__instance == null || targ == null)
                return true;

            if (RaidTurretIds.IsRaidSkin(__instance.skinID)
                && targ is BasePlayer player
                && RaidTurretIds.IsSteamPlayer(player))
            {
                return true;
            }

            if (targ is not BasePlayer bp)
                return true;

            var result = Interface.CallHook("CanEntityBeTargeted", bp, (BaseEntity)__instance);
            return result is not bool can || can;
        }
    }

    /// <summary>
    /// Safety net: if trigger contents stayed empty (other mods / race), still lock onto nearby raiders.
    /// </summary>
    [HarmonyPatch(typeof(AutoTurret), "TargetScan")]
    internal static class AutoTurret_TargetScan_RaidForce_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(AutoTurret __instance)
        {
            if (__instance == null || __instance.IsDestroyed)
                return;
            if (!RaidTurretIds.IsRaidSkin(__instance.skinID))
                return;
            if (__instance.IsOffline() || __instance.HasTarget())
                return;
            if (__instance.HasInterference())
            {
                if (__instance.HasFlag(BaseEntity.Flags.OnFire))
                    __instance.SetFlag(BaseEntity.Flags.OnFire, false, false, true);
            }

            float range = Mathf.Max(1f, __instance.sightRange);
            float rangeSqr = range * range;
            BasePlayer best = null;
            float bestSqr = float.MaxValue;
            Vector3 origin = __instance.transform.position;

            foreach (var p in BasePlayer.activePlayerList)
            {
                if (!RaidTurretIds.IsSteamPlayer(p) || p.IsDead() || p.IsSleeping())
                    continue;
                if (__instance.IsAuthed(p))
                    continue;
                float sqr = (p.transform.position - origin).sqrMagnitude;
                if (sqr > rangeSqr || sqr >= bestSqr)
                    continue;
                best = p;
                bestSqr = sqr;
            }

            if (best == null)
                return;

            if (__instance.ObjectVisible(best) && __instance.InFiringArc(best))
                __instance.SetTarget(best);
        }
    }

    [HarmonyPatch(typeof(GunTrap), nameof(GunTrap.CheckTrigger))]
    internal static class GunTrap_CheckTrigger_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(GunTrap __instance, ref bool __result)
        {
            return TrapTargeting.AllowTrapTrigger(__instance, ref __result);
        }
    }

    [HarmonyPatch(typeof(FlameTurret), nameof(FlameTurret.CheckTrigger))]
    internal static class FlameTurret_CheckTrigger_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(FlameTurret __instance, ref bool __result)
        {
            return TrapTargeting.AllowTrapTrigger(__instance, ref __result);
        }
    }

    [HarmonyPatch(typeof(BaseTrap), nameof(BaseTrap.ObjectEntered))]
    internal static class BaseTrap_ObjectEntered_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(BaseTrap __instance, GameObject obj)
        {
            if (__instance == null || obj == null)
                return true;

            var onTrap = Interface.CallHook("OnTrapTrigger", __instance, obj);
            if (onTrap != null)
                return false;

            var player = obj.GetComponent<BasePlayer>();
            var can = Interface.CallHook("CanEntityTrapTrigger", __instance, player);
            if (can is bool allow && !allow)
                return false;

            return true;
        }
    }

    internal static class TrapTargeting
    {
        internal static bool AllowTrapTrigger(BaseEntity trap, ref bool __result)
        {
            if (trap == null)
                return true;

            // Raid-skinned traps always fire on steam players (Oxide skin check intent).
            if (RaidTurretIds.IsRaidSkin(trap.skinID))
                return true;

            TriggerBase trigger = trap switch
            {
                GunTrap gt => gt.trigger,
                FlameTurret ft => ft.trigger,
                _ => null
            };

            if (trigger?.entityContents == null || trigger.entityContents.Count == 0)
                return true;

            bool sawPlayer = false;
            bool anyAllowed = false;

            foreach (BaseEntity ent in trigger.entityContents)
            {
                if (ent is not BasePlayer player || player.IsDestroyed)
                    continue;

                sawPlayer = true;
                var result = Interface.CallHook("CanEntityBeTargeted", player, trap);
                if (result is bool can)
                {
                    if (can)
                        anyAllowed = true;
                    continue;
                }

                anyAllowed = true;
            }

            if (sawPlayer && !anyAllowed)
            {
                __result = false;
                return false;
            }

            return true;
        }
    }
}
