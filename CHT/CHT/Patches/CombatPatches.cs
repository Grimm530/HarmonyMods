using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using PluginBody = Oxide.Plugins.CustomHelicopterTiers2;
using TierComp = Oxide.Plugins.CustomHelicopterTiers2.TieredHelicopterComponent;

namespace CHT.Patches
{
    /// <summary>
    /// Core combat / AI patches from Oxide CustomHelicopterTiers (formerly nested + [AutoPatch]).
    /// Must be top-level types so Facepunch HarmonyLoader PatchAll reliably applies them.
    /// </summary>

    [HarmonyPatch(typeof(PatrolHelicopterAI), "Update")]
    public static class PatrolHelicopterAI_Update_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(PatrolHelicopterAI __instance)
        {
            if (__instance == null || __instance.helicopterBase == null)
                return true;

            if (TierComp.GetComponent(__instance.helicopterBase) == null)
                return true;

            // Skip vanilla UpdateTargetList — custom aggression runs on TieredHelicopterComponent.Update.
            PatrolHelicopterAI.heliInstance = __instance;
            __instance.MoveToDestination();
            __instance.UpdateRotation();
            __instance.UpdateSpotlight();
            __instance.anim.UpdateAnimation();
            __instance.anim.UpdateLastPosition();
            __instance.AIThink();
            __instance.DoMachineGuns();
            return false;
        }
    }

    [HarmonyPatch(typeof(HelicopterTurret), "FireGun")]
    public static class HelicopterTurret_FireGun_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(HelicopterTurret __instance)
        {
            if (__instance._target == null || __instance._heliAI == null || __instance._heliAI.helicopterBase == null)
                return true;

            TierComp tiered = TierComp.GetComponent(__instance._heliAI.helicopterBase);
            float aimCone = tiered?.TierData?.MachineGun != null
                ? tiered.TierData.MachineGun.BulletSpreadAccuracy
                : ConVar.PatrolHelicopter.bulletAccuracy;

            __instance._heliAI.FireGun(__instance._target.transform.position + new Vector3(0f, 0.25f, 0f), aimCone, __instance.left);
            return false;
        }
    }

    [HarmonyPatch(typeof(PatrolHelicopterAI), "StartStrafe")]
    public static class PatrolHelicopterAI_StartStrafe_Rockets_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(PatrolHelicopterAI __instance)
        {
            if (__instance?.helicopterBase == null) return;
            TierComp tiered = TierComp.GetComponent(__instance.helicopterBase);
            if (tiered?.TierData?.Strafe != null)
                __instance.numRocketsLeft = tiered.TierData.Strafe.MaximumRocketsFiredPerStrafe;
        }
    }

    [HarmonyPatch(typeof(PatrolHelicopterAI), "State_OrbitStrafe_Enter")]
    public static class PatrolHelicopterAI_State_OrbitStrafe_Enter_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(PatrolHelicopterAI __instance)
        {
            if (__instance?.helicopterBase == null) return;
            TierComp tiered = TierComp.GetComponent(__instance.helicopterBase);
            if (tiered?.TierData?.Strafe != null)
                __instance.numRocketsLeft = tiered.TierData.Strafe.MaximumRocketsFiredPerOrbitStrafe;
        }
    }

    [HarmonyPatch(typeof(PatrolHelicopterAI), "State_Strafe_Think")]
    public static class PatrolHelicopterAI_State_Strafe_Think_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var methodToCall = AccessTools.Method(typeof(PatrolHelicopterAI_State_Strafe_Think_Patch), nameof(GetOrbitStrafeChance));

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Ldc_R4
                    && code[i].operand is float f
                    && Mathf.Approximately(f, 0.6f))
                {
                    code[i] = new CodeInstruction(OpCodes.Ldarg_0);
                    code.Insert(i + 1, new CodeInstruction(OpCodes.Call, methodToCall));
                    i++;
                }
            }

            return code;
        }

        private static float GetOrbitStrafeChance(PatrolHelicopterAI patrolHelicopterAi)
        {
            TierComp tiered = TierComp.GetComponent(patrolHelicopterAi.helicopterBase);
            if (tiered?.TierData?.Strafe != null)
            {
                int percent = tiered.TierData.Strafe.ChanceToUpgradeFromStrafeToOrbitStrafe;
                return 1f - Mathf.Clamp01(percent / 100f);
            }

            return 0.6f;
        }
    }

    [HarmonyPatch(typeof(PatrolHelicopterAI), "GenerateRandomDestination", new[] { typeof(bool) })]
    public static class PatrolHelicopterAI_GenerateRandomDestination_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(PatrolHelicopterAI __instance, bool forceMonument, ref Vector3 __result)
        {
            if (__instance?.helicopterBase == null)
                return true;

            TierComp tieredHelicopter = TierComp.GetComponent(__instance.helicopterBase);
            if (tieredHelicopter == null)
                return true;

            if (tieredHelicopter.CallProfile != null && tieredHelicopter.CallProfile.LockOnCaller)
            {
                BasePlayer lockOnTarget = tieredHelicopter.GetPreferredLockOnTarget();
                if (lockOnTarget != null)
                {
                    Vector3 finalPos = PluginBody.TerrainUtil.GetRandomPositionAround(
                        position: lockOnTarget.transform.position,
                        minimumRadius: 65f,
                        maximumRadius: 195f,
                        adjustToWaterHeight: false,
                        adjustToTerrainHeight: true
                    );
                    finalPos.y += UnityEngine.Random.Range(20f, 60f);
                    if (finalPos != Vector3.zero)
                    {
                        __result = finalPos;
                        return false;
                    }
                }
            }

            Vector3 vector = Vector3.zero;
            int chance = Mathf.Clamp(
                tieredHelicopter.TierData?.Patrol?.ChanceToPickMonumentInsteadOfRandomPosition ?? 60, 0, 100);
            bool flag = forceMonument || PluginBody.ChanceSucceeded(chance);

            if (flag)
            {
                if (TerrainMeta.Path?.Monuments != null && TerrainMeta.Path.Monuments.Count > 0)
                {
                    MonumentInfo monumentInfo = null;
                    if (__instance._visitedMonuments.Count > 0)
                    {
                        foreach (MonumentInfo monumentInfo2 in TerrainMeta.Path.Monuments)
                        {
                            if (monumentInfo2.IsSafeZone || tieredHelicopter.IsNoGoMonument(monumentInfo2))
                                continue;

                            bool visited = false;
                            foreach (MonumentInfo y in __instance._visitedMonuments)
                            {
                                if (monumentInfo2 == y)
                                {
                                    visited = true;
                                    break;
                                }
                            }

                            if (!visited)
                            {
                                monumentInfo = monumentInfo2;
                                break;
                            }
                        }
                    }

                    if (monumentInfo == null)
                    {
                        __instance._visitedMonuments.Clear();
                        for (int i = 0; i < 5; i++)
                        {
                            monumentInfo = TerrainMeta.Path.Monuments[UnityEngine.Random.Range(0, TerrainMeta.Path.Monuments.Count)];
                            if (!monumentInfo.IsSafeZone && !tieredHelicopter.IsNoGoMonument(monumentInfo))
                                break;
                        }
                    }

                    if (monumentInfo)
                    {
                        vector = monumentInfo.transform.position;
                        __instance._visitedMonuments.Add(monumentInfo);
                        vector.y = TerrainMeta.HeightMap.GetHeight(vector) + 200f;
                        if (TransformUtil.GetGroundInfo(vector, out RaycastHit raycastHit, 300f, 1235288065, null))
                            vector.y = raycastHit.point.y;
                        vector.y += 30f;
                    }
                }
                else
                {
                    vector = __instance.GetRandomMapPosition();
                }
            }
            else
            {
                vector = __instance.GetRandomMapPosition();
            }

            __result = vector;
            return false;
        }
    }

    [HarmonyPatch(typeof(PatrolHelicopterAI), "MakeZone")]
    public static class PatrolHelicopterAI_MakeZone_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(PatrolHelicopterAI __instance, Vector3 position, float damage, BaseEntity parent)
        {
            PatrolHelicopter patrolHelicopter = __instance.helicopterBase;
            if (patrolHelicopter == null)
                return true;

            TierComp tieredHelicopter = TierComp.GetComponent(patrolHelicopter);
            if (tieredHelicopter?.TierData?.DangerZone == null)
                return true;

            float radius = tieredHelicopter.TierData.DangerZone.BaseDangerZoneRadius;
            if (__instance.dangerZones.Count >= tieredHelicopter.TierData.DangerZone.MaximumAllowedDangerZones)
            {
                if (tieredHelicopter.TierData.DangerZone.RemoveLeastSignificantDangerZoneWhenFull)
                    __instance.RemoveLeastSignificantZone();
                else
                    return false;
            }

            var dangerZone = new PatrolHelicopterAI.DangerZone(position, radius, parent);
            dangerZone.Score += damage;
            __instance.dangerZones.Add(dangerZone);
            tieredHelicopter.OnDangerZoneAdded(dangerZone);
            return false;
        }
    }

    [HarmonyPatch(typeof(PatrolHelicopterAI.DangerZone), "IsStale")]
    public static class DangerZone_IsStale_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(PatrolHelicopterAI.DangerZone __instance, ref bool __result)
        {
            var plugin = CHTMod.Plugin;
            if (plugin == null)
                return true;

            foreach (var tieredHelicopter in plugin.GetAllLiveTieredHelicopters())
            {
                if (tieredHelicopter == null || !tieredHelicopter.HasDangerZone(__instance))
                    continue;
                if (tieredHelicopter.TierData?.DangerZone == null)
                    continue;

                float expireSecs = tieredHelicopter.TierData.DangerZone.SecondsBeforeDangerZoneExpires;
                bool isStaleNow = Time.realtimeSinceStartup - __instance.LastActiveTime > expireSecs;
                __result = isStaleNow;
                if (isStaleNow)
                    tieredHelicopter.OnDangerZoneRemoved(__instance);
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(PatrolHelicopterAI), "UpdateNoGoZones")]
    public static class PatrolHelicopterAI_UpdateNoGoZones_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(PatrolHelicopterAI __instance, PatrolHelicopterAI.DangerZone zone)
        {
            PatrolHelicopter patrolHelicopter = __instance.helicopterBase;
            if (patrolHelicopter == null)
                return true;

            TierComp tieredHelicopter = TierComp.GetComponent(__instance.helicopterBase);
            if (tieredHelicopter?.TierData?.DangerZone == null)
                return true;

            float fleeFraction = tieredHelicopter.TierData.DangerZone.FleeDamagePercentage / 100f;
            float thresholdDamage = patrolHelicopter.startHealth * fleeFraction;

            if (zone.Score >= thresholdDamage)
            {
                __instance.dangerZones.Remove(zone);
                tieredHelicopter.OnDangerZoneRemoved(zone);
                zone.Radius = tieredHelicopter.TierData.DangerZone.NoGoZoneRadius;
                __instance.noGoZones.Add(zone);
                __instance.NoGoZoneAdded(zone);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(PatrolHelicopterAI), "WasAttacked")]
    public static class PatrolHelicopterAI_WasAttacked_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(PatrolHelicopterAI __instance, HitInfo info)
        {
            if (__instance.helicopterBase == null)
                return true;

            object hookResult = CHTMod.Plugin?.OnPatrolHelicopterAttacked(__instance, info);
            return hookResult == null;
        }
    }

    [HarmonyPatch(typeof(PatrolHelicopter), "OnDied")]
    public static class PatrolHelicopter_OnDied_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var customMethod = AccessTools.Method(typeof(PatrolHelicopter_OnDied_Patch), nameof(GetScatteredFireBallCount));

            for (int i = 0; i < code.Count - 3; i++)
            {
                if (code[i].opcode == OpCodes.Ldc_I4_S
                    && code[i].operand is sbyte operand && operand == 12
                    && code[i + 1].opcode == OpCodes.Ldarg_0
                    && code[i + 2].opcode == OpCodes.Ldfld
                    && code[i + 2].operand is FieldInfo field && field.Name == nameof(PatrolHelicopter.maxCratesToSpawn)
                    && code[i + 3].opcode == OpCodes.Sub)
                {
                    code.RemoveRange(i, 4);
                    code.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
                    code.Insert(i + 1, new CodeInstruction(OpCodes.Call, customMethod));
                    break;
                }
            }

            return code;
        }

        private static int GetScatteredFireBallCount(PatrolHelicopter patrolHelicopter)
        {
            TierComp tieredHelicopter = TierComp.GetComponent(patrolHelicopter);
            if (tieredHelicopter?.TierData?.Crash == null)
                return 12 - patrolHelicopter.maxCratesToSpawn;
            return tieredHelicopter.TierData.Crash.MaximumFireBallsToSpawn;
        }
    }

    [HarmonyPatch(typeof(PatrolHelicopter), "OnEntityMessage")]
    public static class PatrolHelicopter_OnEntityMessage_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(PatrolHelicopter __instance, BaseEntity from, string msg)
        {
            TierComp tieredHelicopter = TierComp.GetComponent(__instance);
            if (tieredHelicopter?.TierData?.Homing == null)
                return true;

            if (msg == "RadarLock" && !tieredHelicopter.TierData.Homing.CanDefendWithFlares)
                return false;

            return true;
        }
    }
}
