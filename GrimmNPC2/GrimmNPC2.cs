using System;
using System.Collections.Generic;
using System.Text;
using Rust.Ai.Gen2;
using UnityEngine;
using UnityEngine.AI;

namespace GrimmNPC2
{
    /// <summary>
    /// GEN2-native support/runtime layer for custom <see cref="ScientistNPC2"/> spawns.
    /// Owns registration, policy metadata, bounded helpers, and stock component accessors; not boss gameplay.
    /// </summary>
    public partial class GrimmNPC2 : IHarmonyModHooks
    {
        public static GrimmNPC2 Instance { get; private set; }

        private readonly Dictionary<int, CustomNpcData2> _pending = new Dictionary<int, CustomNpcData2>(512);
        private readonly Dictionary<ulong, CustomNpcData2> _npcs = new Dictionary<ulong, CustomNpcData2>(2048);
        private readonly Dictionary<ulong, CustomNpcRuntimeState2> _runtime = new Dictionary<ulong, CustomNpcRuntimeState2>(2048);

        /// <summary>Owner boss/summoner net id → custom NPCs that declare that owner (helpers/minions).</summary>
        private readonly Dictionary<ulong, List<ulong>> _netIdsByOwner = new Dictionary<ulong, List<ulong>>(256);

        /// <summary>Squad/group id → member net ids (includes any role).</summary>
        private readonly Dictionary<ulong, List<ulong>> _netIdsBySquad = new Dictionary<ulong, List<ulong>>(256);

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            LoadConfig();
            var cfg = GetConfig();
            UnityEngine.Debug.Log("[GrimmNPC2] Loaded - EnableDebugLogging=" + cfg.EnableDebugLogging
                + ", EnableAssistCallouts=" + cfg.EnableAssistCallouts
                + ", AssistRange=" + cfg.AssistRange
                + ", DefaultSleepDistance=" + cfg.DefaultSleepDistance);
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            _pending.Clear();
            _npcs.Clear();
            _runtime.Clear();
            _netIdsByOwner.Clear();
            _netIdsBySquad.Clear();
            ClearProfileTemplates();
            Instance = null;
            UnityEngine.Debug.Log("[GrimmNPC2] Unloaded");
        }

        public static bool RegisterPending(BaseEntity entity, CustomNpcData2 data)
        {
            return TryRegisterPending(entity, data, out _);
        }

        public static bool TryRegisterPending(BaseEntity entity, CustomNpcData2 data, out CustomNpcRegisterResult reason)
        {
            reason = CustomNpcRegisterResult.Success;
            if (Instance == null)
            {
                reason = CustomNpcRegisterResult.ModNotLoaded;
                return false;
            }

            if (entity == null)
            {
                reason = CustomNpcRegisterResult.NullEntity;
                return false;
            }

            if (data == null)
            {
                reason = CustomNpcRegisterResult.NullData;
                return false;
            }

            Instance._pending[entity.GetInstanceID()] = data.CloneNormalized();
            return true;
        }

        public static bool RegisterNpc(ulong netId, CustomNpcData2 data)
        {
            if (Instance == null || netId == 0 || data == null) return false;
            CustomNpcData2 normalized = data.CloneNormalized();
            Instance._npcs[netId] = normalized;
            Instance.RegisterLinkageIndices(netId, normalized, null);
            return true;
        }

        public static bool TryConsumePending(BaseEntity entity, out CustomNpcData2 data)
        {
            data = null;
            if (Instance == null || entity == null) return false;
            int id = entity.GetInstanceID();
            if (!Instance._pending.TryGetValue(id, out data)) return false;
            Instance._pending.Remove(id);
            return true;
        }

        /// <summary>
        /// Read pending registration without removing it. Used during <see cref="BaseEntity.Spawn"/> before the spawn postfix runs
        /// (e.g. <see cref="Rust.Ai.Gen2.NpcShootingComponent.ServerInitPostNetworkGroupAssign"/>), so weapon override can see boss profile data.
        /// </summary>
        public static bool TryPeekPendingNpcData(BaseEntity entity, out CustomNpcData2 data)
        {
            data = null;
            if (Instance == null || entity == null) return false;
            return Instance._pending.TryGetValue(entity.GetInstanceID(), out data);
        }

        public static bool IsCustomNpc(BaseEntity entity)
        {
            if (entity == null || Instance == null) return false;
            ulong netId = entity.net?.ID.Value ?? 0;
            return netId != 0 && Instance._npcs.ContainsKey(netId);
        }

        public static CustomNpcData2 GetNpcData(ulong netId)
        {
            if (Instance == null || netId == 0) return null;
            Instance._npcs.TryGetValue(netId, out var data);
            return data;
        }

        public static void UnregisterNpc(ulong netId)
        {
            if (Instance == null || netId == 0) return;
            Instance._npcs.TryGetValue(netId, out var data);
            Instance._runtime.TryGetValue(netId, out var rt);
            Instance.RemoveLinkageIndices(netId, data, rt);
            Instance._npcs.Remove(netId);
            Instance._runtime.Remove(netId);
        }

        public static void UnregisterEntity(BaseEntity entity)
        {
            if (Instance == null || entity == null) return;
            Instance._pending.Remove(entity.GetInstanceID());
            ulong netId = entity.net?.ID.Value ?? 0;
            if (netId != 0) UnregisterNpc(netId);
        }

        /// <summary>Unregisters all custom NPCs linked to <paramref name="ownerNetId"/> (helpers/minions). Does not kill entities.</summary>
        public static int UnregisterAllLinkedToOwner(ulong ownerNetId)
        {
            if (Instance == null || ownerNetId == 0) return 0;
            if (!Instance._netIdsByOwner.TryGetValue(ownerNetId, out var list) || list.Count == 0) return 0;

            int n = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                ulong id = list[i];
                UnregisterNpc(id);
                n++;
            }

            return n;
        }

        /// <summary>Unregisters all custom NPCs in <paramref name="squadId"/>. Does not kill entities.</summary>
        public static int UnregisterAllInSquad(ulong squadId)
        {
            if (Instance == null || squadId == 0) return 0;
            if (!Instance._netIdsBySquad.TryGetValue(squadId, out var list) || list.Count == 0) return 0;

            int n = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                ulong id = list[i];
                UnregisterNpc(id);
                n++;
            }

            return n;
        }

        public static IReadOnlyList<ulong> GetNetIdsWithOwner(ulong ownerNetId)
        {
            if (Instance == null || ownerNetId == 0) return Array.Empty<ulong>();
            return Instance._netIdsByOwner.TryGetValue(ownerNetId, out var list) ? list : Array.Empty<ulong>();
        }

        public static IReadOnlyList<ulong> GetNetIdsInSquad(ulong squadId)
        {
            if (Instance == null || squadId == 0) return Array.Empty<ulong>();
            return Instance._netIdsBySquad.TryGetValue(squadId, out var list) ? list : Array.Empty<ulong>();
        }

        public static int CountNetIdsWithOwner(ulong ownerNetId)
        {
            if (Instance == null || ownerNetId == 0) return 0;
            return Instance._netIdsByOwner.TryGetValue(ownerNetId, out var list) ? list.Count : 0;
        }

        public static bool TryRegisterSpawnedNpc(BaseEntity entity, CustomNpcData2 data, out CustomNpcRuntimeState2 runtime)
        {
            runtime = null;
            if (Instance == null || entity == null || data == null) return false;

            ulong netId = entity.net?.ID.Value ?? 0;
            if (netId == 0) return false;

            CustomNpcData2 normalized = data.CloneNormalized();
            Instance._npcs[netId] = normalized;

            runtime = new CustomNpcRuntimeState2
            {
                NetId = netId,
                EntityInstanceId = entity.GetInstanceID(),
                SpawnTimeRealtime = Time.realtimeSinceStartup,
                HomePosition = normalized.HomePosition,
                OwnerNetId = normalized.OwnerNetId,
                SquadId = normalized.SquadId,
                Kind = normalized.Kind,
                ResolvedFsmKind = DetectScientistFsmKind(entity)
            };

            Instance._runtime[netId] = runtime;
            Instance.RegisterLinkageIndices(netId, normalized, runtime);
            return true;
        }

        public static CustomNpcRuntimeState2 GetRuntimeState(ulong netId)
        {
            if (Instance == null || netId == 0) return null;
            Instance._runtime.TryGetValue(netId, out var state);
            return state;
        }

        public static void SetRuntimeReleased(ulong netId, bool released)
        {
            if (Instance == null || netId == 0) return;
            if (!Instance._runtime.TryGetValue(netId, out var rt)) return;
            rt.IsReleased = released;
        }

        public static void SetRuntimeMovementFrozenPolicy(ulong netId, bool frozen)
        {
            if (Instance == null || netId == 0) return;
            if (!Instance._runtime.TryGetValue(netId, out var rt)) return;
            rt.MovementFrozenPolicy = frozen;
        }

        public static void SetRuntimeHelperPhaseActive(ulong netId, bool active)
        {
            if (Instance == null || netId == 0) return;
            if (!Instance._runtime.TryGetValue(netId, out var rt)) return;
            rt.HelperPhaseActive = active;
        }

        public static bool TryUpdateLastKnownTarget(BaseEntity npcEntity, BaseEntity target)
        {
            if (Instance == null || npcEntity == null || target == null) return false;
            ulong netId = npcEntity.net?.ID.Value ?? 0;
            if (netId == 0 || !Instance._runtime.TryGetValue(netId, out var state)) return false;
            state.LastKnownTargetNetId = target.net?.ID.Value ?? 0;
            state.LastKnownTargetTimeRealtime = Time.realtimeSinceStartup;
            return true;
        }

        public static bool TrySetTarget(BaseEntity npcEntity, BaseEntity target)
        {
            if (npcEntity == null || target == null) return false;
            SenseComponent sense = npcEntity.GetComponent<SenseComponent>();
            if (sense == null || !sense.TrySetTarget(target, bypassCooldown: false)) return false;
            TryUpdateLastKnownTarget(npcEntity, target);
            return true;
        }

        public static bool TrySetTargetWithCooldownPolicy(BaseEntity npcEntity, BaseEntity target, bool bypassCooldown)
        {
            if (npcEntity == null || target == null) return false;
            SenseComponent sense = npcEntity.GetComponent<SenseComponent>();
            if (sense == null || !sense.TrySetTarget(target, bypassCooldown)) return false;
            TryUpdateLastKnownTarget(npcEntity, target);
            return true;
        }

        /// <summary>Stock GEN2 current target from <see cref="SenseComponent.Target"/> (read-only).</summary>
        public static bool TryGetSenseCurrentTarget(BaseEntity npcEntity, out BaseEntity target)
        {
            target = null;
            if (npcEntity == null) return false;
            SenseComponent sense = npcEntity.GetComponent<SenseComponent>();
            if (sense == null) return false;
            target = sense.Target;
            return target != null;
        }

        /// <summary>Delegates to <see cref="SenseComponent.Forget"/>; does not replace sense ticks.</summary>
        public static bool TrySenseForget(BaseEntity npcEntity, BaseEntity entityToForget)
        {
            if (npcEntity == null || entityToForget == null) return false;
            SenseComponent sense = npcEntity.GetComponent<SenseComponent>();
            return sense != null && sense.Forget(entityToForget);
        }

        /// <summary>Delegates to <see cref="SenseComponent.GetVisibilityStatus"/> for LKP / clarity / camping hints.</summary>
        public static bool TryGetSenseVisibilityStatus(
            BaseEntity npcEntity,
            BaseEntity observed,
            out SenseComponent.VisibilityStatus status)
        {
            status = null;
            if (npcEntity == null || observed == null) return false;
            SenseComponent sense = npcEntity.GetComponent<SenseComponent>();
            return sense != null && sense.GetVisibilityStatus(observed, out status);
        }

        /// <summary>Delegates to <see cref="SenseComponent.FindMostRelevantNoise"/> (hearing / investigation).</summary>
        public static bool TryFindMostRelevantNoise(BaseEntity npcEntity, out NpcNoiseEvent mostRelevantNoise)
        {
            mostRelevantNoise = default;
            if (npcEntity == null) return false;
            SenseComponent sense = npcEntity.GetComponent<SenseComponent>();
            return sense != null && sense.FindMostRelevantNoise(out mostRelevantNoise);
        }

        /// <summary>
        /// Bounded assist: sets the same target on linked custom NPCs (same owner or squad) within <see cref="CustomNpcData2.AssistRadius"/>.
        /// Does not replace stock sense ticks; use for explicit alert propagation from boss plugins.
        /// </summary>
        public static int TryPropagateTargetToAssistGroup(BaseEntity source, BaseEntity target, bool bypassCooldown = true)
        {
            if (Instance == null || source == null || target == null) return 0;
            if (!IsCustomNpc(source)) return 0;
            if (!GetConfig().EnableAssistCallouts) return 0;

            ulong sid = source.net?.ID.Value ?? 0;
            if (sid == 0) return 0;

            if (!Instance._npcs.TryGetValue(sid, out var data) || !data.PropagateTargetToAssistGroup) return 0;

            float r = data.AssistRadius;
            if (data.GroupAlertEnabled && data.GroupAlertRadius > 0f)
                r = data.GroupAlertRadius;
            float assistCap = GetConfig().AssistRange;
            if (assistCap > 0f)
                r = Mathf.Min(r, assistCap);
            if (r <= 0f) return 0;

            float r2 = r * r;
            Vector3 pos = source.transform.position;
            int n = 0;
            var visited = new HashSet<ulong> { sid };

            void TryOne(ulong nid)
            {
                if (nid == sid || !visited.Add(nid)) return;
                if (!TryResolveEntity(nid, out var ent) || ent == null) return;
                if ((ent.transform.position - pos).sqrMagnitude > r2) return;
                SenseComponent sense = ent.GetComponent<SenseComponent>();
                if (sense != null && sense.TrySetTarget(target, bypassCooldown))
                {
                    TryUpdateLastKnownTarget(ent, target);
                    n++;
                }
            }

            ulong owner = data.OwnerNetId;
            if (owner != 0 && Instance._netIdsByOwner.TryGetValue(owner, out var owned))
            {
                for (int i = 0; i < owned.Count; i++)
                    TryOne(owned[i]);
            }

            ulong squad = data.SquadId;
            if (squad != 0 && Instance._netIdsBySquad.TryGetValue(squad, out var squadList))
            {
                for (int i = 0; i < squadList.Count; i++)
                    TryOne(squadList[i]);
            }

            return n;
        }

        /// <summary>
        /// Optional filter using <see cref="GrimmNPC2Config"/> plus per-NPC <see cref="CustomNpcData2"/> rules.
        /// Does not replace <see cref="SenseComponent.CanTarget"/>; use before explicit <see cref="TrySetTarget"/> / propagation when you want gating.
        /// </summary>
        public static bool TryEvaluateTargetAgainstPolicy(
            BaseEntity npcEntity,
            BaseEntity candidate,
            out string rejectReason)
        {
            rejectReason = null;
            if (candidate == null)
            {
                rejectReason = "null candidate";
                return false;
            }

            GrimmNPC2Config cfg = GetConfig();

            ulong nid = npcEntity != null ? npcEntity.net?.ID.Value ?? 0 : 0;
            CustomNpcData2 data = nid != 0 ? GetNpcData(nid) : null;
            if (data == null)
                return true;

            if (cfg.ExcludedTargetTypes != null && cfg.ExcludedTargetTypes.Count > 0)
            {
                if (cfg.ExcludedTargetTypes.Contains(candidate.GetType().Name))
                {
                    rejectReason = "excluded type (mod config)";
                    return false;
                }
            }

            if (cfg.PreventScarecrowTargeting && candidate is ScarecrowNPC)
            {
                rejectReason = "scarecrow";
                return false;
            }

            if (candidate is NPCPlayer && !cfg.CanTargetNpc)
            {
                rejectReason = "npc player target disabled (mod config)";
                return false;
            }

            if (candidate is BaseAnimalNPC && !cfg.CanTargetAnimal)
            {
                rejectReason = "animal target disabled (mod config)";
                return false;
            }

            ulong candNet = candidate.net?.ID.Value ?? 0;
            ulong candUser = 0;
            if (candidate is BasePlayer bp0)
                candUser = bp0.userID.Get();

            if (data.NpcBlacklistIds != null && data.NpcBlacklistIds.Length > 0)
            {
                for (int i = 0; i < data.NpcBlacklistIds.Length; i++)
                {
                    ulong id = data.NpcBlacklistIds[i];
                    if (id == candNet || (candUser != 0 && id == candUser))
                    {
                        rejectReason = "npc blacklist";
                        return false;
                    }
                }
            }

            if (candidate is BasePlayer bp)
            {
                if (bp.IsSleeping() && (data.IgnoreSleepingPlayers || !cfg.CanTargetSleepingPlayer))
                {
                    rejectReason = "sleeping";
                    return false;
                }

                if (bp.IsWounded() && (data.IgnoreWoundedPlayers || !cfg.CanTargetWoundedPlayer))
                {
                    rejectReason = "wounded";
                    return false;
                }

                if (bp.InSafeZone() && (data.IgnoreSafeZonePlayers || !cfg.CanTargetSafeZonePlayer))
                {
                    rejectReason = "safezone";
                    return false;
                }

                if (data.HostileTargetsOnly && !bp.IsHostile())
                {
                    rejectReason = "not hostile";
                    return false;
                }

                if (data.DisplaySashTargetsOnly && !bp.HasPlayerFlag(BasePlayer.PlayerFlags.DisplaySash))
                {
                    rejectReason = "no display sash";
                    return false;
                }
            }

            if (data.NpcWhitelistIds != null && data.NpcWhitelistIds.Length > 0)
            {
                bool ok = false;
                for (int i = 0; i < data.NpcWhitelistIds.Length; i++)
                {
                    ulong id = data.NpcWhitelistIds[i];
                    if (id == candNet || (candUser != 0 && id == candUser))
                    {
                        ok = true;
                        break;
                    }
                }

                if (!ok)
                {
                    rejectReason = "not in npc whitelist";
                    return false;
                }
            }

            if (candidate is BaseNpc && data.AnimalBlacklistIds != null)
            {
                for (int i = 0; i < data.AnimalBlacklistIds.Length; i++)
                {
                    if (candNet != 0 && data.AnimalBlacklistIds[i] == candNet)
                    {
                        rejectReason = "animal blacklist";
                        return false;
                    }
                }
            }

            if (candidate is BaseNpc && data.AnimalWhitelistIds != null && data.AnimalWhitelistIds.Length > 0 && candNet != 0)
            {
                bool okA = false;
                for (int i = 0; i < data.AnimalWhitelistIds.Length; i++)
                {
                    if (data.AnimalWhitelistIds[i] == candNet)
                    {
                        okA = true;
                        break;
                    }
                }

                if (!okA)
                {
                    rejectReason = "animal not whitelisted";
                    return false;
                }
            }

            return true;
        }

        public static bool TrySetDestination(BaseEntity npcEntity, Vector3 worldDestination)
        {
            if (npcEntity == null) return false;
            LimitedTurnNavAgent nav = npcEntity.GetComponent<LimitedTurnNavAgent>();
            if (nav == null) return false;

            // SetDestination -> NavMeshAgent.CalculatePath requires the agent to be on the mesh.
            // Spawn postfix runs before LimitedTurnNavAgent.Tick can Warp; warp here first.
            NavMeshAgent agent = npcEntity.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                if (!agent.enabled)
                    agent.enabled = true;
                if (!agent.isOnNavMesh)
                {
                    Vector3 p = npcEntity.transform.position;
                    bool warped = false;
                    float[] radii = { 40f, 80f, 120f };
                    for (int i = 0; i < radii.Length && !warped; i++)
                    {
                        if (nav.SamplePosition(p, out Vector3 sample, radii[i]))
                        {
                            agent.Warp(sample);
                            warped = true;
                            break;
                        }
                    }

                    if (!warped)
                    {
                        for (int i = 0; i < radii.Length && !warped; i++)
                        {
                            NavMeshHit hit;
                            if (NavMesh.SamplePosition(p, out hit, radii[i], NavMesh.AllAreas))
                            {
                                agent.Warp(hit.position);
                                warped = true;
                            }
                        }
                    }
                }

                if (!agent.isOnNavMesh)
                    return false;
            }

            return nav.SetDestination(worldDestination, allowPartialPaths: true);
        }

        public static bool TrySetDestinationRespectingHomeTether(BaseEntity npcEntity, Vector3 worldDestination)
        {
            if (npcEntity == null) return false;
            ulong netId = npcEntity.net?.ID.Value ?? 0;
            if (netId == 0) return TrySetDestination(npcEntity, worldDestination);

            CustomNpcData2 data = GetNpcData(netId);
            if (data == null || data.HomeTetherDistance <= 0f) return TrySetDestination(npcEntity, worldDestination);

            Vector3 home = data.HomePosition;
            Vector3 horizontal = worldDestination - home;
            horizontal.y = 0f;
            float sqrDistance = horizontal.sqrMagnitude;
            float maxDistance = data.HomeTetherDistance;
            if (sqrDistance > maxDistance * maxDistance && sqrDistance > 0.0001f)
            {
                horizontal = horizontal.normalized * maxDistance;
                worldDestination = new Vector3(home.x + horizontal.x, worldDestination.y, home.z + horizontal.z);
            }

            return TrySetDestination(npcEntity, worldDestination);
        }

        public static bool TryFindLastKnownPosition(BaseEntity npcEntity, BaseEntity target, out Vector3 lkp)
        {
            lkp = Vector3.zero;
            if (npcEntity == null || target == null) return false;
            SenseComponent sense = npcEntity.GetComponent<SenseComponent>();
            return sense != null && sense.FindLKP(target, out lkp, applyHeightOffset: false, predict: true, ignoreCrouch: true);
        }

        public static bool TryInvestigateLastKnownPosition(BaseEntity npcEntity)
        {
            if (npcEntity == null) return false;
            SenseComponent sense = npcEntity.GetComponent<SenseComponent>();
            if (sense == null) return false;
            if (!sense.FindTargetLKP(out Vector3 lkp, applyHeightOffset: false, predict: true, ignoreCrouch: true))
                return false;
            return TrySetDestinationRespectingHomeTether(npcEntity, lkp);
        }

        public static bool TrySetTargetAndInvestigate(BaseEntity npcEntity, BaseEntity target)
        {
            if (!TrySetTarget(npcEntity, target)) return false;
            if (TryFindLastKnownPosition(npcEntity, target, out var lkp))
            {
                return TrySetDestinationRespectingHomeTether(npcEntity, lkp);
            }

            return false;
        }

        public static string BuildDebugSnapshot(ulong netId)
        {
            if (Instance == null || netId == 0) return "GrimmNPC2: invalid netId";
            var sb = new StringBuilder(256);
            if (!Instance._npcs.TryGetValue(netId, out var data))
            {
                sb.Append("not registered: ").Append(netId);
                return sb.ToString();
            }

            Instance._runtime.TryGetValue(netId, out var rt);
            sb.Append("netId=").Append(netId);
            sb.Append(" name=").Append(data.Name);
            sb.Append(" preset=").Append(data.PresetId ?? "(none)");
            sb.Append(" kind=").Append(data.Kind);
            sb.Append(" owner=").Append(data.OwnerNetId);
            sb.Append(" squad=").Append(data.SquadId);
            sb.Append(" home=").Append(data.HomePosition);
            sb.Append(" tether=").Append(data.HomeTetherDistance);
            sb.Append(" canSwimCfg=").Append(data.CanSwim);
            sb.Append(" fsmHint=").Append(data.FsmKindHint);
            if (TryResolveEntity(netId, out var ent) && ent != null)
            {
                if (TryGetLimitedTurnNavIsSwimming(ent, out bool swim))
                    sb.Append(" navSwimming=").Append(swim);
                if (TryGetFsmCurrentStateName(ent, out var st))
                    sb.Append(" fsmState=").Append(st);
            }

            if (rt != null)
            {
                sb.Append(" fsmResolved=").Append(rt.ResolvedFsmKind);
                sb.Append(" rtOwner=").Append(rt.OwnerNetId);
                sb.Append(" released=").Append(rt.IsReleased);
                sb.Append(" moveFrozen=").Append(rt.MovementFrozenPolicy);
                sb.Append(" helperPhase=").Append(rt.HelperPhaseActive);
                sb.Append(" lastTarget=").Append(rt.LastKnownTargetNetId);
            }

            return sb.ToString();
        }

        private void RegisterLinkageIndices(ulong netId, CustomNpcData2 data, CustomNpcRuntimeState2 rt)
        {
            ulong owner = rt?.OwnerNetId ?? data.OwnerNetId;
            if (owner != 0 && netId != owner)
            {
                if (!_netIdsByOwner.TryGetValue(owner, out var list))
                {
                    list = new List<ulong>(8);
                    _netIdsByOwner[owner] = list;
                }

                if (!list.Contains(netId)) list.Add(netId);
            }

            ulong squad = rt?.SquadId ?? data.SquadId;
            if (squad != 0)
            {
                if (!_netIdsBySquad.TryGetValue(squad, out var list))
                {
                    list = new List<ulong>(16);
                    _netIdsBySquad[squad] = list;
                }

                if (!list.Contains(netId)) list.Add(netId);
            }
        }

        private void RemoveLinkageIndices(ulong netId, CustomNpcData2 data, CustomNpcRuntimeState2 rt)
        {
            ulong owner = rt?.OwnerNetId ?? data?.OwnerNetId ?? 0;
            if (owner != 0 && _netIdsByOwner.TryGetValue(owner, out var olist))
            {
                olist.Remove(netId);
                if (olist.Count == 0) _netIdsByOwner.Remove(owner);
            }

            ulong squad = rt?.SquadId ?? data?.SquadId ?? 0;
            if (squad != 0 && _netIdsBySquad.TryGetValue(squad, out var slist))
            {
                slist.Remove(netId);
                if (slist.Count == 0) _netIdsBySquad.Remove(squad);
            }
        }
    }

    public enum CustomNpcKind
    {
        Unspecified = 0,
        Primary = 1,
        Helper = 2,
        Minion = 3,
        Summon = 4,
        Escort = 5,
        Support = 6
    }

    public enum CustomNpcRegisterResult
    {
        Success = 0,
        ModNotLoaded = 1,
        NullEntity = 2,
        NullData = 3
    }

    public class CustomNpcData2
    {
        public string Name { get; set; } = "NPC2";

        /// <summary>
        /// Key for <see cref="GrimmNPC2.RegisterProfileTemplate"/> / <see cref="GrimmNPC2.TryGetProfileTemplateClone"/>.
        /// When <see cref="AutoApplyPresetFromRegistry"/> is true, spawn replaces this instance with the registered template clone.
        /// </summary>
        public string PresetId { get; set; }

        /// <summary>
        /// When true with a non-empty <see cref="PresetId"/>, spawn uses the registered profile template as the full <see cref="CustomNpcData2"/> before normalization.
        /// </summary>
        public bool AutoApplyPresetFromRegistry { get; set; }

        /// <summary>
        /// Optional: spawn plugin should pick a <see cref="ScientistNPC2"/> prefab whose
        /// <see cref="ScientistGen2FsmKind"/> matches (e.g. Heavy / Shotgun). GrimmNPC2 does not swap FSM
        /// components at runtime; this is a hint for tooling and validation only.
        /// </summary>
        public ScientistGen2FsmKind FsmKindHint { get; set; } = ScientistGen2FsmKind.Unknown;

        public Vector3 HomePosition { get; set; } = Vector3.zero;
        public bool ForceHomeToSpawnPoint { get; set; } = true;
        public bool SetInitialDestinationToHome { get; set; } = true;

        public float HomeTetherDistance { get; set; } = 25f;
        public float RoamRange { get; set; } = 25f;
        public float ChaseRange { get; set; } = 100f;
        public float SenseRange { get; set; } = 50f;
        public float ListenRange { get; set; } = 25f;
        public float TargetMemorySeconds { get; set; } = 30f;

        public bool AllowShooting { get; set; } = true;
        public bool AllowAccurateShooting { get; set; } = true;
        public bool OnlyShootVisibleTargets { get; set; } = true;
        public bool CanBeHeadshot { get; set; } = true;

        /// <summary>
        /// GEN2: <see cref="ScientistNPC2"/> is not a <see cref="BasePlayer"/>; no <see cref="PlayerInventory"/>.
        /// Stock <see cref="Rust.Ai.Gen2.NpcShootingComponent"/> creates the held weapon from <c>weaponItemDefinition</c> at init.
        /// Set this to the first belt item short name from boss JSON so GrimmNPC2 can patch the definition before init (see <c>NpcShootingWeaponPatch</c>).
        /// </summary>
        public string Gen2WeaponItemShortName { get; set; }

        public float DamageScale { get; set; } = 1f;
        public float MeleeDamageScale { get; set; } = 1f;
        public float TurretDamageScaleIncoming { get; set; } = 1f;
        public float TurretDamageScaleOutgoing { get; set; } = 1f;
        public bool CanTurretTarget { get; set; } = true;
        public float HeadDamageScale { get; set; } = 1f;
        public float BodyDamageScale { get; set; } = 1f;
        public float LegDamageScale { get; set; } = 1f;

        /// <summary>
        /// Support-layer policy mirrored to <see cref="LimitedTurnNavAgent.canSwim"/> at spawn; use
        /// <see cref="GrimmNPC2.TrySetLimitedTurnNavCanSwim"/> for runtime changes. Stock FSM transitions
        /// (<c>Trans_IsSwimming</c>, <c>Trans_IsInWater_Slow</c>, etc.) own combat/movement in water.
        /// </summary>
        public bool CanSwim { get; set; } = true;
        public bool IsBoss { get; set; } = false;

        /// <summary>Boss/summoner this NPC belongs to (helpers/minions). 0 = none.</summary>
        public ulong OwnerNetId { get; set; }

        /// <summary>Optional squad for assist propagation and plugin bookkeeping.</summary>
        public ulong SquadId { get; set; }

        public CustomNpcKind Kind { get; set; } = CustomNpcKind.Unspecified;

        /// <summary>Radius for <see cref="GrimmNPC2.TryPropagateTargetToAssistGroup"/> (horizontal distance).</summary>
        public float AssistRadius { get; set; } = 40f;

        /// <summary>When true, explicit propagation helper may alert linked NPCs (still bounded by radius).</summary>
        public bool PropagateTargetToAssistGroup { get; set; } = false;

        /// <summary>
        /// Optional override for <see cref="TryPropagateTargetToAssistGroup"/> radius when
        /// <see cref="GroupAlertEnabled"/> is true; 0 = use <see cref="AssistRadius"/>.
        /// </summary>
        public bool GroupAlertEnabled { get; set; }

        public float GroupAlertRadius { get; set; }

        /// <summary>
        /// Half-angle (degrees) for serialized <c>ShortRangeVisionCone</c> at spawn; 0 = keep prefab.
        /// Stock default in assembly is ~100° half-angle on the short cone.
        /// </summary>
        public float ShortRangeVisionHalfAngleDegrees { get; set; }

        /// <summary>Multiplies serialized <see cref="LimitedTurnNavAgent"/> speed fields at spawn (1 = stock).</summary>
        public float NavSpeedMultiplier { get; set; } = 1f;

        /// <summary>
        /// Optional local offset applied to <c>NpcShootingComponent</c> muzzle/aim offset at spawn (serialized private field).
        /// Zero vector = do not override prefab.
        /// </summary>
        public Vector3 ShootingLocalOffset { get; set; } = Vector3.zero;

        /// <summary>NpcSpawn-style legacy: when &gt; 0, overrides <see cref="SenseRange"/> during <see cref="Normalize"/>.</summary>
        public float NpcSenseRange { get; set; }

        /// <summary>Opaque plugin policy tag (e.g. NpcSpawn attack mode); not enforced by stock sense.</summary>
        public int NpcTargetPolicyMode { get; set; }

        /// <summary>
        /// Optional player/NPC id allowlist (comma-separated ulongs: steam ids and/or net ids). Empty = no allowlist filter.
        /// Parsed into <see cref="NpcWhitelistIds"/> in <see cref="Normalize"/>.
        /// </summary>
        public string NpcWhitelistCsv { get; set; }

        /// <summary>Comma-separated ulongs; parsed into <see cref="NpcBlacklistIds"/>.</summary>
        public string NpcBlacklistCsv { get; set; }

        /// <summary>Parsed allowlist from <see cref="NpcWhitelistCsv"/>; do not set manually.</summary>
        public ulong[] NpcWhitelistIds { get; set; }

        /// <summary>Parsed blocklist from <see cref="NpcBlacklistCsv"/>.</summary>
        public ulong[] NpcBlacklistIds { get; set; }

        public bool HostileTargetsOnly { get; set; }

        public bool DisplaySashTargetsOnly { get; set; }

        public bool IgnoreSafeZonePlayers { get; set; } = true;

        public bool IgnoreSleepingPlayers { get; set; } = true;

        public bool IgnoreWoundedPlayers { get; set; } = true;

        /// <summary>Metadata for animal GEN2 prefabs / shared presets; not applied to <see cref="ScientistNPC2"/> spawn.</summary>
        public int AnimalAttackMode { get; set; }

        public float AnimalSenseRange { get; set; }

        public float AnimalDamageScale { get; set; } = 1f;

        public string AnimalWhitelistCsv { get; set; }

        public string AnimalBlacklistCsv { get; set; }

        public ulong[] AnimalWhitelistIds { get; set; }

        public ulong[] AnimalBlacklistIds { get; set; }

        /// <summary>Loot plugin preset name (AlphaLoot / custom table id); metadata only.</summary>
        public string LootPreset { get; set; }

        /// <summary>Optional JSON or opaque hint for external loot loaders; metadata only.</summary>
        public string LootTableJsonHint { get; set; }

        public string CratePrefab { get; set; }

        public bool RemoveCorpseOnDeath { get; set; }

        /// <summary>Combat tuning hints for plugins; stock GEN2 uses weapon range and sense geometry instead.</summary>
        public float DamageRangeHint { get; set; } = -1f;

        public float ShortRangeHint { get; set; } = -1f;

        public float AttackLengthMaxShortRangeScale { get; set; } = 2f;

        public float AttackRangeMultiplier { get; set; } = 1f;

        public bool CheckVisionCone { get; set; }

        /// <summary>Documentation / migration field; use <see cref="ShortRangeVisionHalfAngleDegrees"/> for spawn cone tuning.</summary>
        public float VisionConeDegrees { get; set; } = -1f;

        public float AimConeScale { get; set; } = 1f;

        public bool CanRunAwayWater { get; set; } = true;

        public bool CanSleep { get; set; }

        public float SleepDistance { get; set; }

        public float BarricadeHealthThreshold { get; set; }

        public float BarricadeDistanceThreshold { get; set; }

        /// <summary>Scales healing received by plugins that honor this field; not applied automatically.</summary>
        public float HealingScale { get; set; } = 1f;

        public CustomNpcData2 CloneNormalized()
        {
            var clone = (CustomNpcData2)MemberwiseClone();
            clone.NpcWhitelistIds = NpcWhitelistIds != null ? (ulong[])NpcWhitelistIds.Clone() : null;
            clone.NpcBlacklistIds = NpcBlacklistIds != null ? (ulong[])NpcBlacklistIds.Clone() : null;
            clone.AnimalWhitelistIds = AnimalWhitelistIds != null ? (ulong[])AnimalWhitelistIds.Clone() : null;
            clone.AnimalBlacklistIds = AnimalBlacklistIds != null ? (ulong[])AnimalBlacklistIds.Clone() : null;
            clone.Normalize();
            return clone;
        }

        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(Name)) Name = "NPC2";
            PresetId = string.IsNullOrWhiteSpace(PresetId) ? null : PresetId.Trim();
            Gen2WeaponItemShortName = string.IsNullOrWhiteSpace(Gen2WeaponItemShortName) ? null : Gen2WeaponItemShortName.Trim();

            if (HomeTetherDistance < 0f) HomeTetherDistance = 0f;
            if (RoamRange < 0f) RoamRange = 0f;
            if (ChaseRange < 0f) ChaseRange = 0f;
            if (SenseRange < 0f) SenseRange = 0f;
            if (ListenRange < 0f) ListenRange = 0f;
            if (TargetMemorySeconds < 1f) TargetMemorySeconds = 1f;
            if (AssistRadius < 0f) AssistRadius = 0f;
            if (GroupAlertRadius < 0f) GroupAlertRadius = 0f;

            if (NpcSenseRange > 0f) SenseRange = NpcSenseRange;

            if (RoamRange > 0f && ChaseRange > 0f && RoamRange > ChaseRange) RoamRange = ChaseRange;
            if (HomeTetherDistance <= 0f && RoamRange > 0f) HomeTetherDistance = RoamRange;

            if (DamageScale < 0f) DamageScale = 0f;
            if (MeleeDamageScale < 0f) MeleeDamageScale = 0f;
            if (TurretDamageScaleIncoming < 0f) TurretDamageScaleIncoming = 0f;
            if (TurretDamageScaleOutgoing < 0f) TurretDamageScaleOutgoing = 0f;
            if (HeadDamageScale <= 0f) HeadDamageScale = 1f;
            if (BodyDamageScale <= 0f) BodyDamageScale = 1f;
            if (LegDamageScale <= 0f) LegDamageScale = 1f;

            if (ShortRangeVisionHalfAngleDegrees < 0f) ShortRangeVisionHalfAngleDegrees = 0f;
            if (ShortRangeVisionHalfAngleDegrees > 0f)
                ShortRangeVisionHalfAngleDegrees = Mathf.Clamp(ShortRangeVisionHalfAngleDegrees, 1f, 179f);

            if (NavSpeedMultiplier < 0.05f) NavSpeedMultiplier = 0.05f;
            if (NavSpeedMultiplier > 10f) NavSpeedMultiplier = 10f;

            if (AttackLengthMaxShortRangeScale < 0f) AttackLengthMaxShortRangeScale = 0f;
            if (AttackRangeMultiplier < 0f) AttackRangeMultiplier = 0f;
            if (AimConeScale < 0f) AimConeScale = 0f;

            if (AnimalSenseRange < 0f) AnimalSenseRange = 0f;
            if (AnimalDamageScale < 0f) AnimalDamageScale = 0f;

            if (BarricadeHealthThreshold < 0f) BarricadeHealthThreshold = 0f;
            if (BarricadeDistanceThreshold < 0f) BarricadeDistanceThreshold = 0f;
            if (HealingScale < 0f) HealingScale = 0f;
            if (SleepDistance < 0f) SleepDistance = 0f;

            LootPreset = string.IsNullOrWhiteSpace(LootPreset) ? null : LootPreset.Trim();
            LootTableJsonHint = string.IsNullOrWhiteSpace(LootTableJsonHint) ? null : LootTableJsonHint.Trim();
            CratePrefab = string.IsNullOrWhiteSpace(CratePrefab) ? null : CratePrefab.Trim();

            NpcWhitelistIds = ParseCommaSeparatedUlongs(NpcWhitelistCsv);
            NpcBlacklistIds = ParseCommaSeparatedUlongs(NpcBlacklistCsv);
            AnimalWhitelistIds = ParseCommaSeparatedUlongs(AnimalWhitelistCsv);
            AnimalBlacklistIds = ParseCommaSeparatedUlongs(AnimalBlacklistCsv);
        }

        internal static ulong[] ParseCommaSeparatedUlongs(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return Array.Empty<ulong>();
            var parts = csv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return Array.Empty<ulong>();
            var list = new List<ulong>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string s = parts[i].Trim();
                if (s.Length == 0) continue;
                if (ulong.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var u))
                    list.Add(u);
            }

            return list.Count == 0 ? Array.Empty<ulong>() : list.ToArray();
        }
    }

    public class CustomNpcRuntimeState2
    {
        public ulong NetId { get; set; }
        public int EntityInstanceId { get; set; }
        public float SpawnTimeRealtime { get; set; }
        public Vector3 HomePosition { get; set; }
        public ulong LastKnownTargetNetId { get; set; }
        public float LastKnownTargetTimeRealtime { get; set; }

        public ulong OwnerNetId { get; set; }
        public ulong SquadId { get; set; }
        public CustomNpcKind Kind { get; set; }

        public bool IsReleased { get; set; }

        /// <summary>Plugin-owned policy hint (e.g. pause plugin moves); not automatically enforced here.</summary>
        public bool MovementFrozenPolicy { get; set; }

        /// <summary>Plugin-owned hint for helper wave / phase UI or logic in BossMonster2.</summary>
        public bool HelperPhaseActive { get; set; }

        /// <summary>Detected from <see cref="GrimmNPC2.DetectScientistFsmKind"/> at spawn time.</summary>
        public ScientistGen2FsmKind ResolvedFsmKind { get; set; }
    }
}
