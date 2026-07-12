using System;
using HarmonyLib;
using GrimmNPC2;
using Rust.Ai.Gen2;
using UnityEngine;

namespace GrimmNPC2.Patches
{
    [HarmonyPatch(typeof(BaseEntity), nameof(BaseEntity.Spawn))]
    internal static class BaseEntitySpawnPatchGen2
    {
        private static void Postfix(BaseEntity __instance)
        {
            if (__instance == null) return;
            if (!(__instance is ScientistNPC2)) return;

            if (!GrimmNPC2.TryConsumePending(__instance, out var data)) return;

            if (data.AutoApplyPresetFromRegistry && !string.IsNullOrWhiteSpace(data.PresetId)
                && GrimmNPC2.TryGetProfileTemplateClone(data.PresetId.Trim(), out var fromRegistry))
            {
                data = fromRegistry;
            }

            data.Normalize();
            if (data.ForceHomeToSpawnPoint)
            {
                data.HomePosition = __instance.transform.position;
            }

            if (!GrimmNPC2.TryRegisterSpawnedNpc(__instance, data, out var runtime)) return;

            if (data.FsmKindHint != ScientistGen2FsmKind.Unknown
                && runtime != null
                && runtime.ResolvedFsmKind != ScientistGen2FsmKind.Unknown
                && runtime.ResolvedFsmKind != data.FsmKindHint)
            {
                Debug.LogWarning("[GrimmNPC2] FsmKindHint does not match spawned prefab FSM: hint="
                    + data.FsmKindHint + " resolved=" + runtime.ResolvedFsmKind + " preset=" + (data.PresetId ?? "(none)"));
            }

            if (data.SetInitialDestinationToHome)
            {
                BaseEntity ent = __instance;
                Vector3 home = data.HomePosition;
                // Home is spawn for GrimmBoss; SetDestination to the same point still runs path calc and fails while NavMeshAgent is not ready.
                if ((ent.transform.position - home).sqrMagnitude >= 2.25f)
                {
                    // NavMeshAgent often stays off-mesh for several frames after Spawn; retry until path sets or cap.
                    int[] attempt = new int[1];
                    System.Action tryDest = null;
                    tryDest = () =>
                    {
                        if (ent == null || ent.IsDestroyed) return;
                        if (GrimmNPC2.TrySetDestinationRespectingHomeTether(ent, home)) return;
                        attempt[0]++;
                        if (attempt[0] >= 18)
                        {
                            if (GrimmNPC2.GetConfig().EnableNavMeshValidation)
                                GrimmNPC2.LogDebug("Spawn: initial destination to home failed after retries (off-mesh or path) at " + ent.transform.position);
                            return;
                        }

                        ent.Invoke(tryDest, 0.12f);
                    };
                    ent.Invoke(tryDest, 0.02f);
                }
            }

            __instance.name = data.Name;
            GrimmNPC2.LogDebug("Spawned custom GEN2 NPC name=" + data.Name + " pos=" + __instance.transform.position);

            ScientistNPC2 scientist = __instance as ScientistNPC2;
            if (scientist != null)
            {
                scientist.canBeHeadshot = data.CanBeHeadshot;
            }

            SenseComponent senses = __instance.GetComponent<SenseComponent>();
            if (senses != null)
            {
                senses.timeToForgetSightings.Value = data.TargetMemorySeconds;
                GrimmNPC2.TryApplySpawnSenseRangeTuning(senses, data);
            }

            LimitedTurnNavAgent navAgent = __instance.GetComponent<LimitedTurnNavAgent>();
            if (navAgent != null)
            {
                navAgent.canSwim = data.CanSwim;
                GrimmNPC2.TryApplySpawnNavSpeedMultiplier(navAgent, data);
            }

            NpcShootingComponent shooting = __instance.GetComponent<NpcShootingComponent>();
            if (shooting != null)
            {
                shooting.AllowShooting = data.AllowShooting;
                shooting.AllowBeingAccurate = data.AllowAccurateShooting;
                shooting.OnlyShootIfTargetIsVisible = data.OnlyShootVisibleTargets;
                GrimmNPC2.TryApplySpawnShootingOffset(shooting, data);
            }
        }
    }

    [HarmonyPatch(typeof(BaseEntity), "DoServerDestroy")]
    internal static class BaseEntityDestroyPatchGen2
    {
        private static void Prefix(BaseEntity __instance)
        {
            if (__instance == null) return;
            GrimmNPC2.UnregisterEntity(__instance);
        }
    }

    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.OnAttacked), new Type[] { typeof(HitInfo) })]
    internal static class BaseCombatEntityOnAttackedPatchGen2
    {
        private static void Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            if (__instance == null || info == null || !info.hasDamage) return;

            ApplyIncomingDamageScaling(__instance, info);
            ApplyOutgoingDamageScaling(info);
        }

        private static void ApplyIncomingDamageScaling(BaseCombatEntity victim, HitInfo info)
        {
            if (!GrimmNPC2.IsCustomNpc(victim)) return;

            ulong victimNetId = victim.net?.ID.Value ?? 0;
            CustomNpcData2 data = GrimmNPC2.GetNpcData(victimNetId);
            if (data == null) return;

            float scale = data.DamageScale;

            if (info.Initiator is AutoTurret || info.Initiator is GunTrap || info.Initiator is FlameTurret)
            {
                scale *= data.TurretDamageScaleIncoming;
            }

            HitArea area = info.boneArea;
            if (info.isHeadshot || area == HitArea.Head) scale *= data.HeadDamageScale;
            else if (area == HitArea.Leg || area == HitArea.Foot) scale *= data.LegDamageScale;
            else if (area == HitArea.Chest || area == HitArea.Stomach || area == HitArea.Arm || area == HitArea.Hand) scale *= data.BodyDamageScale;

            if (scale != 1f) info.damageTypes.ScaleAll(scale);
        }

        private static void ApplyOutgoingDamageScaling(HitInfo info)
        {
            BaseEntity initiator = info.Initiator;
            if (initiator == null || !GrimmNPC2.IsCustomNpc(initiator)) return;

            ulong attackerNetId = initiator.net?.ID.Value ?? 0;
            CustomNpcData2 data = GrimmNPC2.GetNpcData(attackerNetId);
            if (data == null) return;

            float scale = data.DamageScale;

            if (info.Weapon is BaseMelee || info.WeaponPrefab is BaseMelee)
            {
                scale *= data.MeleeDamageScale;
            }

            if (info.HitEntity is AutoTurret || info.HitEntity is GunTrap || info.HitEntity is FlameTurret)
            {
                scale *= data.TurretDamageScaleOutgoing;
            }

            if (scale != 1f) info.damageTypes.ScaleAll(scale);
        }
    }

    [HarmonyPatch(typeof(AutoTurret), nameof(AutoTurret.ShouldTarget))]
    internal static class AutoTurretShouldTargetPatchGen2
    {
        private static bool Prefix(BaseCombatEntity targ, ref bool __result)
        {
            if (targ == null || !GrimmNPC2.IsCustomNpc(targ)) return true;

            ulong netId = targ.net?.ID.Value ?? 0;
            CustomNpcData2 data = GrimmNPC2.GetNpcData(netId);
            if (data == null || data.CanTurretTarget) return true;

            __result = false;
            return false;
        }
    }
}
