using System.Collections.Generic;
using System.Reflection;
using Network;
using Rust.Ai.Gen2;
using UnityEngine;
using UnityEngine.AI;

namespace GrimmNPC2
{
    /// <summary>
    /// Thin, assembly-backed accessors for stock GEN2 components. BossMonster2 and other plugins
    /// should prefer these over scattering GetComponent calls; keeps the support surface auditable.
    /// </summary>
    public partial class GrimmNPC2
    {
        public static bool TryGetSense(BaseEntity entity, out SenseComponent sense)
        {
            sense = null;
            if (entity == null) return false;
            sense = entity.GetComponent<SenseComponent>();
            return sense != null;
        }

        public static bool TryGetLimitedTurnNavAgent(BaseEntity entity, out LimitedTurnNavAgent nav)
        {
            nav = null;
            if (entity == null) return false;
            nav = entity.GetComponent<LimitedTurnNavAgent>();
            return nav != null;
        }

        public static bool TryGetNpcShooting(BaseEntity entity, out NpcShootingComponent shooting)
        {
            shooting = null;
            if (entity == null) return false;
            shooting = entity.GetComponent<NpcShootingComponent>();
            return shooting != null;
        }

        public static bool TryGetFsm(BaseEntity entity, out FSMComponent fsm)
        {
            fsm = null;
            if (entity == null) return false;
            fsm = entity.GetComponent<FSMComponent>();
            return fsm != null;
        }

        public static bool TryGetBlackboard(BaseEntity entity, out BlackboardComponent blackboard)
        {
            blackboard = null;
            if (entity == null) return false;
            blackboard = entity.GetComponent<BlackboardComponent>();
            return blackboard != null;
        }

        public static bool TryGetEncounterTimer(BaseEntity entity, out NPCEncounterTimer encounter)
        {
            encounter = null;
            if (entity == null) return false;
            encounter = entity.GetComponent<NPCEncounterTimer>();
            return encounter != null;
        }

        public static bool TryGetNpcZone(BaseEntity entity, out NpcZoneComponent zone)
        {
            zone = null;
            if (entity == null) return false;
            zone = entity.GetComponent<NpcZoneComponent>();
            return zone != null;
        }

        public static bool TryGetRootMotionPlayer(BaseEntity entity, out RootMotionPlayer rootMotion)
        {
            rootMotion = null;
            if (entity == null) return false;
            rootMotion = entity.GetComponent<RootMotionPlayer>();
            return rootMotion != null;
        }

        public static bool TryGetCoverComponent(BaseEntity entity, out CoverComponent cover)
        {
            cover = null;
            if (entity == null) return false;
            cover = entity.GetComponent<CoverComponent>();
            return cover != null;
        }

        public static NpcBarkManager TryGetBarkManager()
        {
            return SingletonComponent<NpcBarkManager>.Instance;
        }

        /// <summary>Resolves a server entity by network id (O(1) realm lookup).</summary>
        public static bool TryResolveEntity(ulong netId, out BaseEntity entity)
        {
            entity = null;
            if (netId == 0) return false;
            return BaseNetworkable.serverEntities.TryGetEntity(new NetworkableId(netId), out entity);
        }

        // --- Blackboard (stock API: Add/Increment/Remove/Clear/Has/Count) ---

        public static bool TryBlackboardAdd(BaseEntity entity, string fact, float durationSeconds = 30f)
        {
            if (string.IsNullOrEmpty(fact) || !TryGetBlackboard(entity, out var bb)) return false;
            bb.Add(fact, durationSeconds);
            return true;
        }

        public static bool TryBlackboardIncrement(BaseEntity entity, string fact, float durationSeconds = 30f)
        {
            if (string.IsNullOrEmpty(fact) || !TryGetBlackboard(entity, out var bb)) return false;
            bb.Increment(fact, durationSeconds);
            return true;
        }

        public static bool TryBlackboardRemove(BaseEntity entity, string fact)
        {
            if (string.IsNullOrEmpty(fact) || !TryGetBlackboard(entity, out var bb)) return false;
            bb.Remove(fact);
            return true;
        }

        public static bool TryBlackboardClear(BaseEntity entity)
        {
            if (!TryGetBlackboard(entity, out var bb)) return false;
            bb.Clear();
            return true;
        }

        public static bool TryBlackboardHas(BaseEntity entity, string fact, out bool has)
        {
            has = false;
            if (string.IsNullOrEmpty(fact) || !TryGetBlackboard(entity, out var bb)) return false;
            has = bb.Has(fact);
            return true;
        }

        public static bool TryBlackboardCount(BaseEntity entity, string fact, out int count)
        {
            count = 0;
            if (string.IsNullOrEmpty(fact) || !TryGetBlackboard(entity, out var bb)) return false;
            return bb.Count(fact, out count);
        }

        // --- Zone (NpcZoneComponent + NpcZone) ---

        public static bool TryIsPointInsideNpcZone(BaseEntity entity, Vector3 worldPoint, out bool inside)
        {
            inside = true;
            if (!TryGetNpcZone(entity, out var zc)) return false;
            inside = zc.IsPointInsideZone(worldPoint);
            return true;
        }

        public static bool TryGetNpcZoneForEntity(BaseEntity entity, out NpcZone zone)
        {
            zone = null;
            if (!TryGetNpcZone(entity, out var zc) || zc.zone == null) return false;
            zone = zc.zone;
            return true;
        }

        /// <summary>Stock GEN2 FSM on/off. Prefer leaving FSM active unless you know the prefab tolerates pausing.</summary>
        public static bool TrySetFsmActive(BaseEntity entity, bool active)
        {
            if (!TryGetFsm(entity, out var fsm)) return false;
            fsm.SetFsmActive(active);
            return true;
        }

        // --- Swim / LimitedTurnNavAgent (GEN2; stock FSM + transitions own most water behavior) ---

        /// <summary>
        /// Reads <see cref="LimitedTurnNavAgent.IsSwimming"/> (stock flag <see cref="BaseEntity.Flags.Reserved1"/>).
        /// Gen1-style nav replacement is not used here; GEN2 agent + FSM transitions handle swimming.
        /// </summary>
        public static bool TryGetLimitedTurnNavIsSwimming(BaseEntity entity, out bool isSwimming)
        {
            isSwimming = false;
            if (!TryGetLimitedTurnNavAgent(entity, out var nav)) return false;
            isSwimming = nav.IsSwimming;
            return true;
        }

        /// <summary>
        /// Sets <see cref="LimitedTurnNavAgent.canSwim"/> (serialized field on the agent). Prefer config at spawn;
        /// use this for runtime policy flips (e.g. BossMonster2 phase locks).
        /// </summary>
        public static bool TrySetLimitedTurnNavCanSwim(BaseEntity entity, bool canSwim)
        {
            if (!TryGetLimitedTurnNavAgent(entity, out var nav)) return false;
            nav.canSwim = canSwim;
            return true;
        }

        /// <summary>
        /// Applies <see cref="CustomNpcData2.ListenRange"/> and <see cref="CustomNpcData2.SenseRange"/> to private serialized
        /// sense fields via one-time cached reflection. Safe no-op if fields rename across game updates.
        /// Public <see cref="SenseComponent.timeToForgetSightings"/> is set in <see cref="Patches.SpawnPatches2"/> without reflection.
        /// </summary>
        public static void TryApplySpawnSenseRangeTuning(SenseComponent sense, CustomNpcData2 data)
        {
            SenseSpawnTuning.Apply(sense, data);
        }

        // --- BaseEntityTargettingExtensions (Rust.Ai.Gen2); documented forwards ---

        /// <summary>
        /// Forward to <see cref="BaseEntityTargettingExtensions.InSameNpcTeam"/>. Note: implementation compares
        /// <see cref="object.GetType"/> equality, not <see cref="NPCTeam"/>; useful for "same prefab class" checks only.
        /// </summary>
        public static bool TargetingExtInSameNpcTeam(BaseEntity a, BaseEntity b)
        {
            return a != null && a.InSameNpcTeam(b);
        }

        /// <summary>Forward to <see cref="BaseEntityTargettingExtensions.IsNonNpcPlayer"/>.</summary>
        public static bool TargetingExtIsNonNpcPlayer(BaseEntity entity)
        {
            return entity != null && entity.IsNonNpcPlayer();
        }

        /// <summary>Forward to <see cref="BaseEntityTargettingExtensions.IsNpcPlayer"/>.</summary>
        public static bool TargetingExtIsNpcPlayer(BaseEntity entity)
        {
            return entity != null && entity.IsNpcPlayer();
        }

        /// <summary>Forward to <see cref="BaseEntityTargettingExtensions.ToNonNpcPlayer"/>.</summary>
        public static bool TargetingExtTryToNonNpcPlayer(BaseEntity entity, out BasePlayer player)
        {
            player = null;
            return entity != null && entity.ToNonNpcPlayer(out player);
        }

        // --- NpcCoverManager / cover queries (support layer: no tactic selection here) ---

        /// <summary>Singleton used by stock cover states; null if not yet created on server.</summary>
        public static bool TryGetNpcCoverManager(out NpcCoverManager manager)
        {
            manager = SingletonComponent<NpcCoverManager>.Instance;
            return manager != null;
        }

        /// <summary>
        /// Wraps <see cref="NpcCoverManager.GetCoversAround"/>. Caller supplies a reusable <paramref name="covers"/> list;
        /// clears/reuse policy is caller-owned to match stock pooling patterns.
        /// </summary>
        public static bool TryGetCoversAround(BaseEntity entity, Vector3 origin, Vector3 threatPosition, float range, List<Cover> covers)
        {
            if (entity == null || covers == null || !TryGetNpcCoverManager(out var mgr)) return false;
            mgr.GetCoversAround(entity, origin, threatPosition, range, covers);
            return true;
        }

        /// <summary>
        /// Wraps <see cref="NpcCoverManager.FindBestCover"/> for plugins that already hold a <see cref="NavMeshPath"/>.
        /// Full cover tactics remain stock FSM / BossMonster2; this only exposes the stock query.
        /// </summary>
        public static Cover? TryFindBestCover(
            LimitedTurnNavAgent agent,
            Vector3 threatPosition,
            float radius,
            float preferredEngagementDistance,
            ref NavMeshPath path,
            bool requireLineOfSight,
            float? targetRadius = null)
        {
            if (agent == null || !TryGetNpcCoverManager(out var mgr)) return null;
            return mgr.FindBestCover(agent, threatPosition, radius, preferredEngagementDistance, ref path, requireLineOfSight, targetRadius);
        }

        /// <summary>Stock reservation map: entity → cover in use (<see cref="NpcCoverManager.TryGetCover"/>).</summary>
        public static bool TryGetNpcCoverReserved(BaseEntity entity, out Cover cover)
        {
            cover = default;
            if (entity == null || !TryGetNpcCoverManager(out var mgr)) return false;
            return mgr.TryGetCover(entity, out cover);
        }

        /// <summary>Delegates to <see cref="NpcCoverManager.Reserve"/>.</summary>
        public static bool TryNpcCoverReserve(Cover cover, BaseEntity entity)
        {
            if (entity == null || !TryGetNpcCoverManager(out var mgr)) return false;
            mgr.Reserve(cover, entity);
            return true;
        }

        /// <summary>Delegates to <see cref="NpcCoverManager.Release"/>.</summary>
        public static bool TryNpcCoverRelease(Cover cover)
        {
            if (!TryGetNpcCoverManager(out var mgr)) return false;
            mgr.Release(cover);
            return true;
        }

        // --- FSM introspection (stock graphs: Scientist2FSM / Heavy / Shotgun) ---

        /// <summary>Current leaf state display name from <see cref="FSMComponent.CurrentState"/>.</summary>
        public static bool TryGetFsmCurrentStateName(BaseEntity entity, out string stateName)
        {
            stateName = null;
            if (!TryGetFsm(entity, out var fsm) || fsm.CurrentState == null) return false;
            stateName = fsm.CurrentState.Name;
            return !string.IsNullOrEmpty(stateName);
        }

        /// <summary>Stock FSM state object for ancestry / debugging; do not mutate transitions.</summary>
        public static bool TryGetFsmCurrentState(BaseEntity entity, out FSMStateBase state)
        {
            state = null;
            if (!TryGetFsm(entity, out var fsm) || fsm.CurrentState == null) return false;
            state = fsm.CurrentState;
            return true;
        }

        public static bool TryGetScientist2FsmDefault(BaseEntity entity, out Scientist2FSM fsm)
        {
            fsm = null;
            if (entity == null) return false;
            fsm = entity.GetComponent<Scientist2FSM>();
            return fsm != null;
        }

        public static bool TryGetScientist2FsmHeavy(BaseEntity entity, out Scientist2FSM_Heavy fsm)
        {
            fsm = null;
            if (entity == null) return false;
            fsm = entity.GetComponent<Scientist2FSM_Heavy>();
            return fsm != null;
        }

        public static bool TryGetScientist2FsmShotgun(BaseEntity entity, out Scientist2FSM_Shotgun fsm)
        {
            fsm = null;
            if (entity == null) return false;
            fsm = entity.GetComponent<Scientist2FSM_Shotgun>();
            return fsm != null;
        }

        /// <summary>
        /// Delegates to <see cref="NPCFlankSpot.Find"/> (expensive: multiple <see cref="NavMeshPath"/> objects).
        /// Used by stock <see cref="State_Flank"/>; exposed for BossMonster2 / custom steering that reuses the same math.
        /// </summary>
        public static bool TryNpcFlankSpotFind(
            LimitedTurnNavAgent agent,
            Vector3 enemyPositionNavSpace,
            NavMeshPath directPath,
            NavMeshPath pathToFlank,
            NavMeshPath pathFromFlankToEnemy,
            float flankWidth = 15f,
            float sampleRadius = 3.5f,
            float minAngle = 30f,
            float minSimilarity = 0.25f)
        {
            if (agent == null || directPath == null || pathToFlank == null || pathFromFlankToEnemy == null) return false;
            return NPCFlankSpot.Find(agent, enemyPositionNavSpace, directPath, pathToFlank, pathFromFlankToEnemy, flankWidth, sampleRadius, minAngle, minSimilarity);
        }

        private static class SenseSpawnTuning
        {
            private static readonly FieldInfo HearingRangeField;
            private static readonly FieldInfo ShortConeField;
            private static readonly FieldInfo LongRectField;
            private static readonly bool Ok;

            static SenseSpawnTuning()
            {
                var t = typeof(SenseComponent);
                HearingRangeField = t.GetField("hearingRange", BindingFlags.Instance | BindingFlags.NonPublic);
                ShortConeField = t.GetField("ShortRangeVisionCone", BindingFlags.Instance | BindingFlags.NonPublic);
                LongRectField = t.GetField("LongRangeVisionRectangle", BindingFlags.Instance | BindingFlags.NonPublic);
                Ok = HearingRangeField != null && ShortConeField != null && LongRectField != null
                    && HearingRangeField.FieldType == typeof(float)
                    && ShortConeField.FieldType == typeof(SenseComponent.Cone)
                    && LongRectField.FieldType == typeof(Vector3);
            }

            public static void Apply(SenseComponent sense, CustomNpcData2 data)
            {
                if (!Ok || sense == null || data == null) return;

                HearingRangeField.SetValue(sense, data.ListenRange);

                var cone = (SenseComponent.Cone)ShortConeField.GetValue(sense);
                cone.range = data.SenseRange;
                if (data.ShortRangeVisionHalfAngleDegrees > 0f)
                    cone.halfAngle = data.ShortRangeVisionHalfAngleDegrees;
                ShortConeField.SetValue(sense, cone);

                var rect = (Vector3)LongRectField.GetValue(sense);
                rect.z = Mathf.Max(data.SenseRange, 15f);
                LongRectField.SetValue(sense, rect);
            }
        }

        /// <summary>
        /// Multiplies serialized speed fields on <see cref="LimitedTurnNavAgent"/> at spawn. Safe no-op if reflection fails.
        /// </summary>
        public static void TryApplySpawnNavSpeedMultiplier(LimitedTurnNavAgent nav, CustomNpcData2 data)
        {
            NavSpawnTuning.Apply(nav, data);
        }

        /// <summary>
        /// Applies optional <see cref="CustomNpcData2.ShootingLocalOffset"/> to serialized muzzle offset on
        /// <see cref="NpcShootingComponent"/> (replaces prefab offset when non-zero).
        /// </summary>
        public static void TryApplySpawnShootingOffset(NpcShootingComponent shooting, CustomNpcData2 data)
        {
            ShootingSpawnTuning.Apply(shooting, data);
        }

        private static class NavSpawnTuning
        {
            private static readonly FieldInfo[] SpeedFields;
            private static readonly bool Ok;

            static NavSpawnTuning()
            {
                var t = typeof(LimitedTurnNavAgent);
                string[] names =
                {
                    "sneakSpeed", "walkSpeed", "jogSpeed", "runSpeed", "sprintSpeed", "fullSprintSpeed",
                    "swimSpeed", "swimSprintSpeed"
                };
                var list = new List<FieldInfo>(names.Length);
                foreach (var n in names)
                {
                    var f = t.GetField(n, BindingFlags.Instance | BindingFlags.NonPublic);
                    if (f != null && f.FieldType == typeof(float))
                        list.Add(f);
                }

                SpeedFields = list.ToArray();
                Ok = SpeedFields.Length > 0;
            }

            public static void Apply(LimitedTurnNavAgent nav, CustomNpcData2 data)
            {
                if (!Ok || nav == null || data == null) return;
                float m = data.NavSpeedMultiplier;
                if (Mathf.Approximately(m, 1f)) return;

                for (int i = 0; i < SpeedFields.Length; i++)
                {
                    var f = SpeedFields[i];
                    float v = (float)f.GetValue(nav);
                    f.SetValue(nav, v * m);
                }
            }
        }

        private static class ShootingSpawnTuning
        {
            private static readonly FieldInfo OffsetField;
            private static readonly bool Ok;

            static ShootingSpawnTuning()
            {
                var t = typeof(NpcShootingComponent);
                OffsetField = t.GetField("offset", BindingFlags.Instance | BindingFlags.NonPublic);
                Ok = OffsetField != null && OffsetField.FieldType == typeof(Vector3);
            }

            public static void Apply(NpcShootingComponent shooting, CustomNpcData2 data)
            {
                if (!Ok || shooting == null || data == null) return;
                if (data.ShootingLocalOffset.sqrMagnitude <= 1e-8f) return;
                OffsetField.SetValue(shooting, data.ShootingLocalOffset);
            }
        }
    }
}
