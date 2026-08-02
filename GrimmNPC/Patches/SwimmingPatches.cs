using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace GrimmNPC.Patches
{
    /// <summary>
    /// Swimming support for GrimmNPC NPCs.
    ///
    /// While swimming (custom NPC + CanSwim): stock navigation must not run — no NavMeshAgent pathing,
    /// Stop/Pause/Resume, or UpdateMovement (which touches Agent speed and NavMesh branches).
    /// Swim intent is recorded via a gated SetDestination prefix; movement is applied from
    /// UpdateNavigation (replacing the entire stock tick for that frame).
    ///
    /// Swimming detection: <see cref="BaseNavigator_IsSwimming_Patch"/> (player + open-water fallback).
    /// </summary>

    /// <summary>
    /// Patches IsSwimming() for custom NPCs using player + open-water probes (CanSwim=false opts out).
    /// </summary>
    [HarmonyPatch(typeof(BaseNavigator), nameof(BaseNavigator.IsSwimming))]
    public class BaseNavigator_IsSwimming_Patch
    {
        private const float SwimDebugIntervalSeconds = 2f;
        private const float OpenWaterMinWaterPlane = -80f;
        private const float OpenWaterMinSubmergeDepth = 0.65f;
        private static readonly Dictionary<ulong, float> SwimDebugLastLogTime = new Dictionary<ulong, float>(64);

        static bool Prefix(BaseNavigator __instance, ref bool __result)
        {
            if (__instance.BaseEntity == null || !GrimmNPC.IsCustomNpc(__instance.BaseEntity))
                return true;

            var npc = __instance.BaseEntity as ScientistNPC;
            if (npc == null) return true;

            ulong netId = npc.net?.ID.Value ?? 0;
            if (netId == 0) return true;

            var npcData = GrimmNPC.GetNpcData(netId);
            if (!SwimNavGate.TryResolveCanSwim(npc, out bool canSwim))
                canSwim = true;
            if (!canSwim)
            {
                __result = false;
                GrimmNPC.LogSwimmingFailure(
                    "SwimBlocked",
                    $"CanSwim=false netId={netId} pos={npc.transform.position}",
                    netId);
                MaybeLogSwimmingDiagnostics(__instance, npc, npcData, netId, swimming: false, skipReason: "CanSwim=false");
                return false;
            }

            bool playerSwim = npc.IsSwimming();
            bool openWater = TryGrimmOpenWaterSwim(npc);
            if (playerSwim || openWater)
            {
                __result = true;
                if (npcData != null)
                    MaybeLogSwimmingDiagnostics(__instance, npc, npcData, netId, swimming: true, skipReason: null);
                return false;
            }

            __result = false;
            if (npcData != null)
                MaybeLogSwimmingDiagnostics(__instance, npc, npcData, netId, swimming: false,
                    skipReason: "playerSwim=false openWaterFallback=false");
            return false;
        }

        private static bool TryGrimmOpenWaterSwim(ScientistNPC npc)
        {
            if (npc == null || TerrainMeta.WaterMap == null)
                return false;

            Vector3 p = npc.ServerPosition;
            float waterPlane = WaterLevel.GetWaterLevel(p, waves: true);
            float terrainH = 0f;
            if (TerrainMeta.HeightMap != null && TerrainMeta.HeightMap.isInitialized)
                terrainH = TerrainMeta.HeightMap.GetHeight(p);

            if (waterPlane < OpenWaterMinWaterPlane)
                return false;

            // Terrain height at XZ is not a reliable "dry land" signal in deep water (seabed vs water column mismatch).
            if (waterPlane <= terrainH + 0.08f && p.y > terrainH + 0.25f)
                return false;

            const float subsurfaceM = 0.45f;
            if (p.y > waterPlane - subsurfaceM)
                return false;

            // Old guard rejected deep seabed spawns (GrimmBoss ocean positions). Only reject obvious "under map" voids.
            if (p.y < terrainH - 25f)
                return false;

            WaterLevel.WaterInfo wi = WaterLevel.GetWaterInfo(p, waves: true, volumes: true, npc);
            float wf = npc.WaterFactor();
            if (wf >= 0.58f && wi.currentDepth < 1.45f)
                return false;

            if (wi.isValid)
            {
                if (wi.currentDepth < OpenWaterMinSubmergeDepth)
                    return false;
            }
            else
            {
                if (waterPlane - p.y < OpenWaterMinSubmergeDepth)
                    return false;
            }

            return true;
        }

        private static void MaybeLogSwimmingDiagnostics(
            BaseNavigator navigator,
            ScientistNPC npc,
            CustomNpcData npcData,
            ulong netId,
            bool swimming,
            string skipReason)
        {
            if (!GrimmNPC.GetConfig().EnableSwimmingDebug)
                return;

            float now = Time.realtimeSinceStartup;
            if (SwimDebugLastLogTime.TryGetValue(netId, out float last) && (now - last) < SwimDebugIntervalSeconds)
                return;
            SwimDebugLastLogTime[netId] = now;

            Vector3 pos = npc.ServerPosition;
            float waterPlaneQuick = TerrainMeta.WaterMap != null ? WaterLevel.GetWaterLevel(pos, waves: true) : float.NaN;
            if (!swimming && !float.IsNaN(waterPlaneQuick) && waterPlaneQuick < OpenWaterMinWaterPlane)
                return;

            float waterFactor = npc.WaterFactor();
            float modelWater = npc.modelState != null ? npc.modelState.waterLevel : -1f;
            bool playerSwim = npc.IsSwimming();
            bool openWaterFb = TryGrimmOpenWaterSwim(npc);
            float waterPlane = waterPlaneQuick;
            float mapTerrainH = TerrainMeta.HeightMap != null && TerrainMeta.HeightMap.isInitialized
                ? TerrainMeta.HeightMap.GetHeight(pos)
                : float.NaN;

            WaterLevel.WaterInfo info = WaterLevel.GetWaterInfo(pos, waves: true, volumes: true, npc);

            UnityEngine.Debug.Log(
                "[GrimmNPC SwimDebug] " +
                $"netId={netId} name={npcData.Name ?? "?"} canSwim={npcData.CanSwim} swimming={swimming} " +
                $"skip={skipReason ?? "-"} playerSwim={playerSwim} openWaterFallback={openWaterFb} " +
                $"waterFactor={waterFactor:F3} (player if >=0.65) modelWater={modelWater:F3} " +
                $"posY={pos.y:F2} waterPlane={waterPlane:F2} mapTerrainH={(float.IsNaN(mapTerrainH) ? -1f : mapTerrainH):F2} " +
                $"info.valid={info.isValid} surfaceY={info.surfaceLevel:F2} terrainY={info.terrainHeight:F2} " +
                $"curDepth={info.currentDepth:F2} overallDepth={info.overallDepth:F2} navMoving={navigator.Moving}");
        }
    }

    [HarmonyPatch(typeof(BaseNavigator), "GetTargetSpeed")]
    public class BaseNavigator_GetTargetSpeed_Patch
    {
        private const float DefaultSwimSpeedMultiplier = 0.4f;

        private static readonly FieldInfo _currentSpeedFractionField = typeof(BaseNavigator)
            .GetField("currentSpeedFraction", BindingFlags.NonPublic | BindingFlags.Instance);

        static bool Prefix(BaseNavigator __instance, ref float __result)
        {
            if (__instance.BaseEntity == null || !GrimmNPC.IsCustomNpc(__instance.BaseEntity))
                return true;

            if (!__instance.IsSwimming())
                return true;

            var npc = __instance.BaseEntity as ScientistNPC;
            if (npc == null) return true;

            ulong netId = npc.net?.ID.Value ?? 0;
            if (netId == 0) return true;

            var npcData = GrimmNPC.GetNpcData(netId);
            if (!SwimNavGate.TryResolveCanSwim(npc, out bool canSwim))
                canSwim = true;
            if (!canSwim)
                return true;

            float mult = npcData != null ? npcData.SwimmingSpeedMultiplier : DefaultSwimSpeedMultiplier;

            float currentSpeedFraction = _currentSpeedFractionField != null
                ? (float)_currentSpeedFractionField.GetValue(__instance)
                : 1f;

            float baseSpeed = __instance.Speed * currentSpeedFraction;
            __result = baseSpeed * mult;
            return false;
        }
    }

    /// <summary>
    /// Shared reflection + swim gate for custom CanSwim NPCs while <see cref="BaseNavigator.IsSwimming"/> is true.
    /// </summary>
    internal static class SwimNavGate
    {
        private static readonly MethodInfo _canUpdateMovementMethod =
            AccessTools.Method(typeof(BaseNavigator), "CanUpdateMovement");
        private static readonly PropertyInfo _npcConfigProp =
            AccessTools.Property(typeof(ScientistNPC), "Config");

        private static readonly FieldInfo _lastSetDestinationTimeField =
            typeof(BaseNavigator).GetField("lastSetDestinationTime", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo _pausedField =
            typeof(BaseNavigator).GetField("paused", BindingFlags.NonPublic | BindingFlags.Instance);

        internal static readonly FieldInfo CurrentSpeedFractionField = typeof(BaseNavigator)
            .GetField("currentSpeedFraction", BindingFlags.NonPublic | BindingFlags.Instance);

        internal static bool ShouldBlockNavApi(BaseNavigator nav)
        {
            if (nav?.BaseEntity == null || !GrimmNPC.IsCustomNpc(nav.BaseEntity))
                return false;
            if (!(nav.BaseEntity is ScientistNPC npc))
                return false;
            if (!TryResolveCanSwim(npc, out bool canSwim) || !canSwim)
                return false;
            ulong netId = npc.net?.ID.Value ?? 0;
            return nav.IsSwimming() || GrimmNPC.IsInSpawnSwimInitWindow(netId);
        }

        internal static bool TryResolveCanSwim(ScientistNPC npc, out bool canSwim)
        {
            canSwim = true;
            if (npc == null) return false;

            ulong netId = npc.net?.ID.Value ?? 0;
            if (netId != 0)
            {
                var npcData = GrimmNPC.GetNpcData(netId);
                if (npcData != null)
                {
                    canSwim = npcData.CanSwim;
                    return true;
                }
            }

            // Fallback for NpcSpawn-created CustomScientistNpc when GrimmNPC registration hasn't happened.
            try
            {
                PropertyInfo cfgProp = _npcConfigProp ?? npc.GetType().GetProperty("Config", BindingFlags.Public | BindingFlags.Instance);
                object cfg = cfgProp?.GetValue(npc, null);
                if (cfg == null) return false;

                PropertyInfo canSwimProp = cfg.GetType().GetProperty("CanSwim", BindingFlags.Public | BindingFlags.Instance);
                if (canSwimProp == null) return false;

                object v = canSwimProp.GetValue(cfg, null);
                if (v is bool b)
                {
                    canSwim = b;
                    return true;
                }
            }
            catch
            {
                // best effort
            }

            return false;
        }

        internal static bool StockCanUpdateMovement(BaseNavigator nav)
        {
            if (_canUpdateMovementMethod == null || nav == null)
                return true;
            try
            {
                object r = _canUpdateMovementMethod.Invoke(nav, null);
                return r is bool b && b;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Records swim destination intent without touching NavMeshAgent (mirrors early SetDestination state writes).
        /// </summary>
        internal static bool TryRecordSwimDestination(
            BaseNavigator __instance,
            ref Vector3 pos,
            float speedFraction,
            float updateInterval,
            ref bool __result)
        {
            if (!ConVar.AI.move)
            {
                __result = false;
                return false;
            }

            if (!ConVar.AI.navthink)
            {
                __result = false;
                return false;
            }

            if (updateInterval > 0f && !__instance.UpdateIntervalElapsed(updateInterval))
            {
                __result = true;
                return false;
            }

            if (_lastSetDestinationTimeField != null)
                _lastSetDestinationTimeField.SetValue(__instance, Time.time);
            if (_pausedField != null)
                _pausedField.SetValue(__instance, false);
            if (CurrentSpeedFractionField != null)
                CurrentSpeedFractionField.SetValue(__instance, speedFraction);

            Vector3 here = __instance.BaseEntity.ServerPosition;
            if (Vector3.Distance(pos, here) <= __instance.StoppingDistance)
            {
                __result = true;
                return false;
            }

            __instance.Destination = pos;
            __instance.SetCurrentNavigationType(BaseNavigator.NavigationType.Base);
            __result = true;
            return false;
        }
    }

    /// <summary>
    /// Custom horizontal + vertical swim step; replaces stock UpdateMovement for swimming frames.
    /// </summary>
    internal static class GrimmSwimMovement
    {
        private static readonly MethodInfo _getTargetSpeedMethod = typeof(BaseNavigator)
            .GetMethod("GetTargetSpeed", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        private static readonly MethodInfo _basePlayerUpdateModelState = typeof(BasePlayer)
            .GetMethod("UpdateModelState", BindingFlags.NonPublic | BindingFlags.Instance);

        private const float SwimDepthBelowSurface = 1.1f;
        private const float SwimVerticalLerp = 14f;
        private const float SwimVerticalSnapMeters = 2.5f;
        private const float SeabedClearance = 0.25f;
        private const float ModelStateSwimSendInterval = 0.12f;
        private static readonly Dictionary<ulong, float> LastSwimModelStateSend = new Dictionary<ulong, float>(32);

        private static float ComputeSwimHoldY(Vector3 xzPosition, BasePlayer forEntity)
        {
            float waterSurface = WaterLevel.GetWaterSurface(xzPosition, waves: true, volumes: true, forEntity);
            float terrainH = TerrainMeta.HeightMap != null && TerrainMeta.HeightMap.isInitialized
                ? TerrainMeta.HeightMap.GetHeight(xzPosition)
                : float.NegativeInfinity;
            float holdY = waterSurface - SwimDepthBelowSurface;
            float floorY = terrainH + SeabedClearance;
            if (holdY < floorY)
                holdY = floorY;
            float maxY = waterSurface - 0.12f;
            if (holdY > maxY)
                holdY = maxY;
            return holdY;
        }

        private static void TryPushSwimModelState(ScientistNPC npc)
        {
            if (npc?.modelState == null || npc.net == null)
                return;
            ulong id = npc.net.ID.Value;
            float now = Time.time;
            if (LastSwimModelStateSend.TryGetValue(id, out float last) && (now - last) < ModelStateSwimSendInterval)
                return;

            float wf = Mathf.Clamp01(npc.WaterFactor());
            float wl = wf >= 0.65f ? wf : Mathf.Max(wf, 0.85f);
            if (Mathf.Abs(npc.modelState.waterLevel - wl) < 0.02f)
            {
                LastSwimModelStateSend[id] = now;
                return;
            }

            npc.modelState.waterLevel = wl;
            _basePlayerUpdateModelState?.Invoke(npc, null);
            npc.SendModelState(force: true);
            LastSwimModelStateSend[id] = now;
        }

        internal static void Apply(BaseNavigator __instance, Vector3 moveToPosition, float delta)
        {
            var npc = __instance.BaseEntity as ScientistNPC;
            if (npc == null) return;

            Vector3 currentPos = __instance.BaseEntity.transform.position;
            float targetSpeed = _getTargetSpeedMethod != null
                ? (float)_getTargetSpeedMethod.Invoke(__instance, null)
                : __instance.Speed;

            Vector3 flatCur = new Vector3(currentPos.x, 0f, currentPos.z);
            Vector3 flatDest = new Vector3(moveToPosition.x, 0f, moveToPosition.z);
            Vector3 flatNew = Vector3.MoveTowards(flatCur, flatDest, targetSpeed * delta);
            Vector3 xzProbe = new Vector3(flatNew.x, currentPos.y, flatNew.z);

            float swimHoldY = ComputeSwimHoldY(xzProbe, npc);
            float newY = Mathf.Abs(currentPos.y - swimHoldY) > SwimVerticalSnapMeters
                ? swimHoldY
                : Mathf.Lerp(currentPos.y, swimHoldY, Mathf.Clamp01(delta * SwimVerticalLerp));
            Vector3 newPosition = new Vector3(flatNew.x, newY, flatNew.z);

            var ent = __instance.BaseEntity;
            ent.transform.position = newPosition;
            ent.ServerPosition = newPosition;

            TryPushSwimModelState(npc);

            Vector3 direction2D;
            BaseEntity faceEntity = npc.GetBestTarget();
            if (faceEntity != null && !faceEntity.IsDestroyed)
            {
                Vector3 toTarget = faceEntity.transform.position - currentPos;
                direction2D = new Vector3(toTarget.x, 0f, toTarget.z);
            }
            else
            {
                Vector3 direction3D = moveToPosition - currentPos;
                direction2D = new Vector3(direction3D.x, 0f, direction3D.z);
            }

            if (direction2D.sqrMagnitude > 0.001f && npc.eyes != null)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction2D.normalized, Vector3.up);
                npc.eyes.rotation = Quaternion.Lerp(npc.eyes.rotation, targetRotation, delta * 25f);
                npc.viewAngles = npc.eyes.rotation.eulerAngles;
                npc.ServerRotation = npc.eyes.rotation;
            }
        }
    }

    [HarmonyPatch(typeof(BaseNavigator), nameof(BaseNavigator.UpdateNavigation))]
    public class BaseNavigator_UpdateNavigation_SwimGatePatch
    {
        static bool Prefix(BaseNavigator __instance, float delta)
        {
            if (!SwimNavGate.ShouldBlockNavApi(__instance))
                return true;

            if (!SwimNavGate.StockCanUpdateMovement(__instance))
                return true;

            Vector3 moveTo = __instance.Destination;
            if (__instance.CurrentNavigationType == BaseNavigator.NavigationType.None)
                moveTo = __instance.BaseEntity.transform.position;

            GrimmSwimMovement.Apply(__instance, moveTo, delta);
            return false;
        }
    }

    [HarmonyPatch(typeof(BaseNavigator), nameof(BaseNavigator.SetDestination), new Type[] { typeof(Vector3), typeof(float), typeof(float), typeof(float) })]
    public class BaseNavigator_SetDestination_Float_SwimGatePatch
    {
        static bool Prefix(
            BaseNavigator __instance,
            ref Vector3 pos,
            float speedFraction,
            float updateInterval,
            float navmeshSampleDistance,
            ref bool __result)
        {
            if (!SwimNavGate.ShouldBlockNavApi(__instance))
                return true;
            return SwimNavGate.TryRecordSwimDestination(__instance, ref pos, speedFraction, updateInterval, ref __result);
        }
    }

    [HarmonyPatch(typeof(BaseNavigator), nameof(BaseNavigator.Stop))]
    public class BaseNavigator_Stop_SwimGatePatch
    {
        static bool Prefix(BaseNavigator __instance)
        {
            if (!SwimNavGate.ShouldBlockNavApi(__instance))
                return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(BaseNavigator), nameof(BaseNavigator.Pause))]
    public class BaseNavigator_Pause_SwimGatePatch
    {
        static bool Prefix(BaseNavigator __instance)
        {
            if (!SwimNavGate.ShouldBlockNavApi(__instance))
                return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(BaseNavigator), nameof(BaseNavigator.Resume))]
    public class BaseNavigator_Resume_SwimGatePatch
    {
        static bool Prefix(BaseNavigator __instance)
        {
            if (!SwimNavGate.ShouldBlockNavApi(__instance))
                return true;
            return false;
        }
    }
}
