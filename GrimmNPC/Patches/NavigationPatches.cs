using HarmonyLib;
using Rust;
using Rust.Ai;
using System;
using UnityEngine;
using UnityEngine.AI;

namespace GrimmNPC.Patches
{
    /// <summary>
    /// Enforces range from home when NPCs set destinations.
    /// 
    /// Integration with Rust's Navigation System:
    /// - Intercepts BaseNavigator.SetDestination() BEFORE pathfinding executes
    /// - Clamps destinations to RoamRange when idle (no brain target); to ChaseRange when chasing/combat
    ///   (BossMonster and ThinkPatches combat use ChaseRange — clamping only RoamRange previously blocked movement beyond ~25m)
    /// - When idle and still inside roam, caps each leg length from current position (State_Patrol-style short passes)
    /// - Uses horizontal (XZ) distances to prevent Y-axis issues
    /// 
    /// Pathfinding Flow:
    /// State.StateThink() → SetDestination() → [GrimmNPC] Clamp → BaseNavigator.SetDestination() → Pathfinding
    /// 
    /// Performance:
    /// - Fast prefix check (early exit for non-custom NPCs)
    /// - Simple vector math (no pathfinding overhead)
    /// - Runs on every SetDestination() call (~5-10 calls/second per NPC)
    /// - Performance Impact: <0.001ms per call (negligible)
    /// 
    /// See INSTRUCTIONAL.md "Pathfinding and Navigation Integration" section for details.
    /// </summary>
    [HarmonyPatch(typeof(BaseNavigator), nameof(BaseNavigator.SetDestination), new Type[] { typeof(Vector3), typeof(BaseNavigator.NavigationSpeed), typeof(float), typeof(float) })]
    public class BaseNavigator_SetDestination_Patch
    {
        /// <summary>True when event memory indicates chase/combat (see <see cref="GrimmNPC.HasBrainEventMemoryCombatTarget"/>).</summary>
        private static bool HasActiveBrainTarget(ScientistNPC npc) => GrimmNPC.HasBrainEventMemoryCombatTarget(npc);

        /// <summary>
        /// Prefix intercepts destination position and clamps it to RoamRange (idle) or ChaseRange (combat) boundary.
        /// 
        /// Process:
        /// 1. Early exit for non-custom NPCs (fast skinID check)
        /// 2. Get NPC data from registration
        /// 3. Calculate horizontal distance from HomePosition (prevents Y-axis issues)
        /// 4. If outside RoamRange, clamp to boundary (preserves direction from home)
        /// 5. Return clamped position to original method
        /// 
        /// Critical: Uses horizontal (XZ) distances to prevent NPCs from looking at ceiling
        /// when targets are at different Y levels. See INSTRUCTIONAL.md "Troubleshooting" section.
        /// </summary>
        static bool Prefix(BaseNavigator __instance, ref Vector3 pos, BaseNavigator.NavigationSpeed speed, float updateInterval, float navmeshSampleDistance, ref bool __result)
        {
            // Early exit - only process for custom NPCs (fast check)
            if (__instance.BaseEntity == null || !GrimmNPC.IsCustomNpc(__instance.BaseEntity))
                return true; // Continue to original

            var npc = __instance.BaseEntity as ScientistNPC;
            if (npc == null) return true;

            ulong netId = npc.net?.ID.Value ?? 0;
            if (netId == 0) return true;

            var npcData = GrimmNPC.GetNpcData(netId);
            if (npcData == null) return true;

            // Raiders with active raid goals must reach the goal; do not clamp roam.
            if (npcData.IsRaidingNpc && npcData.RaidGoalActive)
                return true;

            // Plugin opt-in: block movement when no players nearby (replaces BossMonster component scanning).
            if (npcData.FreezeMovementUnlessPlayersNearby)
            {
                float radius = npcData.FreezeMovementPlayerCheckRadius > 0f ? npcData.FreezeMovementPlayerCheckRadius : 150f;
                BasePlayer[] players = new BasePlayer[64];
                int playerCount = BaseEntity.Query.Server.GetPlayersInSphere(
                    npc.transform.position,
                    radius,
                    players,
                    x => x != null && x.net?.connection != null && x.userID > 76561197960265728UL && !x.IsNpc && !x.IsSleeping()
                );

                if (playerCount == 0)
                {
                    GrimmNPC.LogTargetingFailure(
                        "DestinationBlocked",
                        $"FreezeUnlessPlayersNearby netId={netId} radius={radius:F1} pos={npc.transform.position}",
                        netId);
                    __result = false;
                    return false;
                }
            }

            float distanceFromHome = GrimmNPC.GetDistanceFromHome(npcData, pos);

            float maxDistanceFromHome = HasActiveBrainTarget(npc)
                ? Mathf.Max(npcData.RoamRange, npcData.ChaseRange)
                : npcData.RoamRange;

            if (distanceFromHome > maxDistanceFromHome)
            {
                Vector3 toDestination = pos - npcData.HomePosition;
                Vector3 directionFromHome = new Vector3(toDestination.x, 0f, toDestination.z).normalized;

                float clampedY = pos.y;
                if (__instance.CurrentNavigationType == BaseNavigator.NavigationType.Base)
                    clampedY = npc.transform.position.y;
                else if (npcData.HomePosition != Vector3.zero)
                {
                    float yDelta = Mathf.Abs(pos.y - npc.transform.position.y);
                    if (yDelta > 100f)
                        clampedY = npcData.HomePosition.y;
                }

                pos = new Vector3(
                    npcData.HomePosition.x + directionFromHome.x * maxDistanceFromHome,
                    clampedY,
                    npcData.HomePosition.z + directionFromHome.z * maxDistanceFromHome
                );
            }

            var cfg = GrimmNPC.GetConfig();
            // Gen2 State_Patrol: ~4–6 m from current pos. Stock Roam/Dismounted picks anywhere in the roam disk → 60 m sprints.
            if (cfg != null && cfg.EnableIdlePatrolLegCap && !HasActiveBrainTarget(npc))
            {
                float distNpcFromHome = GrimmNPC.GetDistanceFromHome(npcData, npc.transform.position);
                if (distNpcFromHome <= npcData.RoamRange && npcData.RoamRange > 0.5f)
                {
                    float maxLeg = cfg.IdlePatrolMaxLegMeters > 0.5f ? cfg.IdlePatrolMaxLegMeters : 8f;
                    Vector3 cur = npc.transform.position;
                    Vector3 delta = pos - cur;
                    delta.y = 0f;
                    float leg = delta.magnitude;
                    if (leg > maxLeg)
                    {
                        Vector3 dir = delta / leg;
                        float clampedY = pos.y;
                        if (__instance.CurrentNavigationType == BaseNavigator.NavigationType.Base)
                            clampedY = cur.y;
                        else if (npcData.HomePosition != Vector3.zero)
                        {
                            float yDelta = Mathf.Abs(pos.y - cur.y);
                            if (yDelta > 100f)
                                clampedY = npcData.HomePosition.y;
                        }

                        pos = new Vector3(
                            cur.x + dir.x * maxLeg,
                            clampedY,
                            cur.z + dir.z * maxLeg);

                        distanceFromHome = GrimmNPC.GetDistanceFromHome(npcData, pos);
                        if (distanceFromHome > maxDistanceFromHome)
                        {
                            Vector3 toDestination = pos - npcData.HomePosition;
                            Vector3 directionFromHome = new Vector3(toDestination.x, 0f, toDestination.z).normalized;
                            if (__instance.CurrentNavigationType == BaseNavigator.NavigationType.Base)
                                clampedY = npc.transform.position.y;
                            else if (npcData.HomePosition != Vector3.zero)
                            {
                                float yDelta2 = Mathf.Abs(pos.y - npc.transform.position.y);
                                if (yDelta2 > 100f)
                                    clampedY = npcData.HomePosition.y;
                            }
                            pos = new Vector3(
                                npcData.HomePosition.x + directionFromHome.x * maxDistanceFromHome,
                                clampedY,
                                npcData.HomePosition.z + directionFromHome.z * maxDistanceFromHome);
                        }
                    }
                }
            }

            if (cfg != null && cfg.EnableNavDestinationSanitize)
                SanitizeDestinationForNavMesh(__instance, npc, ref pos, navmeshSampleDistance);

            return true;
        }

        /// <summary>NpcSpawn-style tight sample + path check — snap within a few meters of the requested goal, not a distant mesh point.</summary>
        private static void SanitizeDestinationForNavMesh(BaseNavigator navigator, ScientistNPC npc, ref Vector3 pos, float sampleRadius)
        {
            if (navigator == null || npc == null) return;
            var agent = navigator.Agent;
            if (agent == null || !agent.isOnNavMesh) return;
            int mask = agent.areaMask;
            if (mask == 0) return;

            var cfg = GrimmNPC.GetConfig();
            float maxSample = cfg != null ? cfg.NavDestinationSanitizeSampleMaxMeters : 6f;
            if (maxSample < 0.25f) maxSample = 6f;

            float stock = sampleRadius > 0.1f ? sampleRadius : 2f;
            float radius = Mathf.Min(stock, maxSample);
            if (!NavMesh.SamplePosition(pos, out NavMeshHit hit, radius, mask))
                return;

            Vector3 npcPos = npc.transform.position;
            Vector3 pathStart = npcPos;
            float startSnap = Mathf.Min(maxSample, 4f);
            if (NavMesh.SamplePosition(npcPos, out NavMeshHit startHit, startSnap, mask))
                pathStart = startHit.position;

            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(pathStart, hit.position, mask, path))
                return;

            if (path.status == NavMeshPathStatus.PathComplete)
            {
                pos = hit.position;
                return;
            }

            if (path.corners != null && path.corners.Length > 0)
            {
                pos = path.corners[path.corners.Length - 1];
            }
        }
        
        // PERFORMANCE NOTE: Removed Postfix - Prefix already handles clamping efficiently
        // Postfix approach would require reflection which is slow. Prefix approach is better.
    }
}
