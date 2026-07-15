using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Facepunch;
using UnityEngine;

namespace ZombieHorde
{
    public class Horde
    {
        public static List<Horde> AllHordes = new List<Horde>();

        private static readonly SimpleGrid<Horde> HordeGrid = new SimpleGrid<Horde>(32, 8096f);
        private static readonly Horde[] HordeGridQueryResults = new Horde[4];
        private static readonly BasePlayer[] PlayerVicinityQueryResults = new BasePlayer[32];

        public List<ZombieNPC> members;

        public readonly Vector3 InitialPosition;
        public readonly bool IsLocalHorde;
        public readonly float MaximumRoamDistance;

        private readonly int initialMemberCount;
        private readonly int maximumMemberCount;
        private readonly string hordeProfile;

        private float nextUpdateTime;
        private float nextSeperationCheckTime;
        private float nextGrowthTime;
        private float nextMergeTime;
        private float nextSleepTime;
        private float nextOnlineBaseRaidScanTime;

        internal bool isDespawning;

        private const float HORDE_UPDATE_RATE = 1f;
        private const float SEPERATION_CHECK_RATE = 10f;
        private const float MERGE_CHECK_RATE = 10f;
        private const float SLEEP_CHECK_RATE = 5f;

        public ZombieNPC Leader { get; private set; }
        public bool IsSleeping { get; private set; }
        public Vector3 CentralLocation { get; private set; }
        public bool HordeOnAlert { get; private set; }
        public float NextThrownWeaponTime { get; set; }
        public int MemberCount => members?.Count ?? 0;

        public static bool Create(SpawnOrder spawnOrder)
        {
            if (spawnOrder == null || ConfigData.Configuration?.Horde == null || ConfigData.Configuration.Member?.Loadouts == null)
            {
                Compat.PrintWarning("Horde.Create aborted: missing config or spawn order.");
                return false;
            }

            if (ConfigData.Configuration.Member.Loadouts.Count == 0)
            {
                Compat.PrintWarning("Horde.Create aborted: no loadouts configured.");
                return false;
            }

            Horde horde = new Horde(spawnOrder);

            try
            {
                for (int i = 0; i < spawnOrder.InitialMemberCount; i++)
                    horde.SpawnMember(spawnOrder.Position);
            }
            catch (Exception ex)
            {
                Compat.PrintWarning("Horde.Create spawn loop: " + ex.Message);
            }

            if (horde.members == null || horde.members.Count == 0)
            {
                // Permanent: do not schedule Destroy→Create(this) retry (that NRE'd after reload / empty Grimm spawns).
                Compat.PrintWarning("Horde.Create produced 0 members near " + spawnOrder.Position + " — not auto-retrying via Destroy.");
                horde.Destroy(true, false);
                return false;
            }

            AllHordes.Add(horde);
            horde.CentralLocation = horde.CalculateCentralLocation();
            HordeGrid.Add(horde, horde.CentralLocation.x, horde.CentralLocation.z);

            if (ConfigData.Configuration.Member.EnableDormantSystem && ConfigData.Configuration.Member.DormantUntilSensedOnly)
                horde.SetSleeping(true);

            return true;
        }

        public Horde(SpawnOrder spawnOrder)
        {
            members = Pool.Get<List<ZombieNPC>>();
            InitialPosition = CentralLocation = spawnOrder.Position;
            IsLocalHorde = spawnOrder.MaximumRoamDistance > 0;
            MaximumRoamDistance = spawnOrder.MaximumRoamDistance;
            initialMemberCount = spawnOrder.InitialMemberCount;
            maximumMemberCount = spawnOrder.MaximumMemberCount;
            hordeProfile = spawnOrder.HordeProfile;

            nextSeperationCheckTime = Time.time + SEPERATION_CHECK_RATE;
            nextGrowthTime = Time.time + ConfigData.Configuration.Horde.GrowthRate;
            nextMergeTime = Time.time + MERGE_CHECK_RATE;
            nextSleepTime = Time.time + SLEEP_CHECK_RATE + UnityEngine.Random.Range(1f, 5f);
        }

        public void Update()
        {
            if (members == null || members.Count == 0) return;
            if (Time.time < nextUpdateTime) return;
            nextUpdateTime = Time.time + HORDE_UPDATE_RATE;

            CentralLocation = CalculateCentralLocation();

            if (ConfigData.Configuration.Member.EnableDormantSystem)
                DoSleepChecks();

            TryFindOnlineBaseRaidTarget();

            if (IsSleeping)
            {
                TryGrowHorde();
                return;
            }

            HordeGrid.Move(this, CentralLocation.x, CentralLocation.z);
            TryMergeHordes();
            TryGrowHorde();
            TryCongregateHorde();
            MoveRoamersTowardsTarget();
        }

        private void MoveRoamersTowardsTarget()
        {
            HordeOnAlert = AnyHasTarget(out BaseEntity target);
            if (Leader && !Leader.IsDestroyed && target && !target.IsDestroyed)
                SetLeaderRoamTarget(target.transform.position);
        }

        private void TryFindOnlineBaseRaidTarget()
        {
            if (!ConfigData.Configuration.Horde.RaidOnlinePlayersAtBases || !Leader || Leader.IsDestroyed || Leader.Brain == null)
                return;

            if (Time.time < nextOnlineBaseRaidScanTime) return;
            nextOnlineBaseRaidScanTime = Time.time + Mathf.Max(1f, ConfigData.Configuration.Horde.OnlineBaseRaidScanInterval);

            if (HasHumanTarget()) return;

            float scanRange = Mathf.Min(ConfigData.Configuration.Horde.OnlineBaseRaidScanRange, Leader.Brain.TargetLostRange);
            if (scanRange <= 0f) return;

            int count = BaseEntity.Query.Server.GetPlayersInSphere(CentralLocation, scanRange, PlayerVicinityQueryResults, OnlineBaseRaidPlayerFilter);
            BasePlayer bestPlayer = null;
            float bestDistance = float.PositiveInfinity;

            for (int i = 0; i < count; i++)
            {
                BasePlayer player = PlayerVicinityQueryResults[i];
                if (!player || !Leader.CanTargetBasePlayer(player) || !ZombieHordePlugin.IsInOrOnBuilding(player))
                    continue;

                float distance = Vector3.Distance(player.transform.position, CentralLocation);
                if (distance >= bestDistance) continue;
                bestPlayer = player;
                bestDistance = distance;
            }

            if (bestPlayer)
            {
                ForceWakeFromSleep();
                RegisterInterestInTarget(null, bestPlayer, true);
            }
        }

        private void TryCongregateHorde()
        {
            if (Time.time > nextSeperationCheckTime)
            {
                nextSeperationCheckTime = Time.time + SEPERATION_CHECK_RATE;
                if (GetLargestSeperation() > 30f)
                {
                    if (Leader != null && Leader.CurrentState <= AIState.Roam)
                        SetLeaderRoamTarget(CentralLocation);
                }
            }
        }

        public void RegisterInterestInTarget(ZombieNPC interestedMember, BaseEntity baseEntity, bool force)
        {
            if (!baseEntity || members == null) return;

            if (ConfigData.Configuration.Member.EnableDormantSystem && ConfigData.Configuration.Member.DormantUntilSensedOnly
                && baseEntity != null && ZombieNPC.Get(baseEntity) == null)
                ForceWakeFromSleep();

            for (int i = 0; i < members.Count; i++)
            {
                ZombieNPC hordeMember = members[i];
                if (!hordeMember || hordeMember.IsDestroyed || interestedMember == hordeMember)
                    continue;

                hordeMember.SetKnown(baseEntity);
                if (force)
                    hordeMember.SwitchToChase(baseEntity);
            }

            if (!force && Leader && !Leader.IsDestroyed && !Leader.HasTarget)
                SetLeaderRoamTarget(baseEntity.transform.position);
        }

        public bool HasTarget()
        {
            if (members == null) return false;
            for (int i = 0; i < members.Count; i++)
            {
                ZombieNPC m = members[i];
                if (!m || m.IsDestroyed) continue;
                if (m.HasTarget) return true;
            }
            return false;
        }

        public bool HasHumanTarget()
        {
            if (members == null) return false;
            for (int i = 0; i < members.Count; i++)
            {
                ZombieNPC m = members[i];
                if (!m || m.IsDestroyed) continue;
                if (m.HasHumanTargetInRange) return true;
            }
            return false;
        }

        public bool AnyHasTarget(out BaseEntity target)
        {
            if (members != null)
            {
                for (int i = 0; i < members.Count; i++)
                {
                    if (members[i].HasTarget)
                    {
                        target = members[i].CurrentTarget;
                        return true;
                    }
                }
            }
            target = null;
            return false;
        }

        public void ResetRoamTarget()
        {
            if (members == null) return;
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i].IsGroupLeader) continue;
                members[i].ResetRoamState();
            }
        }

        public void SetLeaderRoamTarget(Vector3 position)
        {
            if (Leader && !Leader.IsDestroyed && !Leader.RecentlySetDestination)
            {
                Leader.SetRoamTargetOverride(position);
                ResetRoamTarget();
            }
        }

        public Vector3 GetLeaderDestination()
        {
            if (!Leader || Leader.IsDestroyed || Leader.Brain == null || Leader.Brain.Navigator == null)
                return CentralLocation;
            return Leader.Brain.Navigator.Destination;
        }

        public void OnMemberKilled(ZombieNPC zombieNpc, BaseEntity initiator)
        {
            if (!zombieNpc || members == null || !members.Contains(zombieNpc))
                return;

            members.Remove(zombieNpc);

            if (members.Count == 0)
                Destroy();
            else
            {
                if (zombieNpc.IsGroupLeader)
                {
                    Leader = members.GetRandom();
                    Leader.IsGroupLeader = true;
                }

                if ((initiator is BasePlayer player && Leader.CanTargetBasePlayer(player))
                    || (initiator is BaseNpc && Leader.CanTargetEntity(initiator)))
                    RegisterInterestInTarget(null, initiator, zombieNpc.isHidingInside || zombieNpc.unreachableLastFrame);
            }
        }

        public void OnPlayerKilled(BasePlayer player)
        {
            if (ConfigData.Configuration.Horde.CreateOnDeath && MemberCount < maximumMemberCount)
            {
                Vector3 center = player != null ? player.transform.position : CentralLocation;
                SpawnMember(center);
            }
        }

        public void SpawnMember(Vector3 position)
        {
            var cfg = ConfigData.Configuration;
            if (cfg?.Member?.Loadouts == null || cfg.Member.Loadouts.Count == 0)
                return;

            ConfigData.MemberOptions.Loadout loadout = null;

            if (!string.IsNullOrEmpty(hordeProfile) && cfg.HordeProfiles != null
                && cfg.HordeProfiles.ContainsKey(hordeProfile))
            {
                var profileLoadouts = cfg.HordeProfiles[hordeProfile];
                if (profileLoadouts != null && profileLoadouts.Count > 0)
                {
                    string loadoutId = profileLoadouts.GetRandom();
                    loadout = cfg.Member.Loadouts.FirstOrDefault(x => x != null && x.LoadoutID == loadoutId);
                }
            }

            if (loadout == null)
                loadout = cfg.Member.Loadouts.GetRandom();

            if (loadout == null)
                return;

            // Final safety: never place exactly on top of an existing member.
            Vector3 spawnPos = GetSpreadSpawnPosition(position, members?.Count ?? 0);

            ZombieNPC zombieNpc = GrimmNpcBridge.Spawn(spawnPos, loadout, this);
            if (!zombieNpc) return;

            if (members == null)
                members = Pool.Get<List<ZombieNPC>>();

            members.Add(zombieNpc);

            if (members.Count == 1)
            {
                Leader = zombieNpc;
                Leader.IsGroupLeader = true;
            }
            else zombieNpc.Invoke("OnInitialSpawn", 1f);

            if (IsSleeping)
                zombieNpc.SetSleeping(true);

            RaidingZombies.OnZombieSpawned(zombieNpc);
        }

        public void Destroy(bool permanent = false, bool killNpcs = true)
        {
            isDespawning = true;

            if (killNpcs && members != null)
            {
                for (int i = members.Count - 1; i >= 0; i--)
                {
                    ZombieNPC zombieNpc = members[i];
                    if (zombieNpc && !zombieNpc.IsDestroyed)
                        zombieNpc.Kill();
                }
            }

            if (members != null)
            {
                members.Clear();
                Pool.FreeUnmanaged(ref members);
            }

            HordeGrid.Remove(this);
            AllHordes.Remove(this);

            var cfg = ConfigData.Configuration;
            if (permanent || cfg?.Horde == null)
                return;

            if (AllHordes.Count > cfg.Horde.MaximumHordes)
                return;

            // Snapshot values — do not call SpawnOrder.Create(this) on a torn-down Horde after reload.
            Vector3 localPos = InitialPosition;
            bool local = IsLocalHorde;
            float roam = MaximumRoamDistance;
            int initial = initialMemberCount;
            int maxMembers = maximumMemberCount;
            string profile = hordeProfile;
            float delay = cfg.Horde.RespawnTime;

            Compat.Timer.In(delay, () =>
            {
                if (ZombieHordePlugin.Instance == null || ConfigData.Configuration?.Horde == null)
                    return;
                if (AllHordes.Count > ConfigData.Configuration.Horde.MaximumHordes)
                    return;

                try
                {
                    if (local)
                        SpawnOrder.Create(localPos, initial, maxMembers, roam, profile);
                    else
                        SpawnOrder.Create(
                            ZombieHordePlugin.Instance.GetSpawnPoint(),
                            initial, maxMembers, roam, profile);
                }
                catch (Exception ex)
                {
                    Compat.PrintWarning("Horde respawn Create: " + ex.Message);
                }
            });
        }

        private Vector3 CalculateCentralLocation()
        {
            Vector3 location = Vector3.zero;
            if (members == null || members.Count == 0) return location;

            int count = 0;
            for (int i = 0; i < members.Count; i++)
            {
                ZombieNPC zombieNpc = members[i];
                if (!zombieNpc || zombieNpc.IsDestroyed) continue;
                location += zombieNpc.Transform.position;
                count++;
            }
            return count > 0 ? location / count : location;
        }

        private float GetLargestSeperation()
        {
            float distance = 0;
            if (members == null) return distance;
            for (int i = 0; i < members.Count; i++)
            {
                ZombieNPC zombieNpc = members[i];
                if (zombieNpc && !zombieNpc.IsDestroyed)
                {
                    float d = Vector3.Distance(zombieNpc.Transform.position, CentralLocation);
                    if (d > distance) distance = d;
                }
            }
            return distance;
        }

        private void TryGrowHorde()
        {
            if (ConfigData.Configuration.Horde.GrowthRate <= 0 || Time.time < nextGrowthTime)
                return;

            if (MemberCount < maximumMemberCount && members.Count > 0)
            {
                ZombieNPC anchor = members.GetRandom();
                Vector3 center = anchor && !anchor.IsDestroyed ? anchor.Transform.position : CentralLocation;
                SpawnMember(center);
            }

            nextGrowthTime = Time.time + ConfigData.Configuration.Horde.GrowthRate;
        }

        /// <summary>
        /// Spread new members around a center so they do not spawn stacked inside each other.
        /// Uses a golden-angle ring + NavMesh sample.
        /// </summary>
        private static Vector3 GetSpreadSpawnPosition(Vector3 center, int index)
        {
            const float minRadius = 1.5f;
            const float maxRadius = 5f;
            const float searchRadius = 8f;

            // Golden angle (~137.5°) gives even packing around the center.
            float angle = (index * 137.508f + UnityEngine.Random.Range(-18f, 18f)) * Mathf.Deg2Rad;
            float radius = Mathf.Lerp(minRadius, maxRadius, (index % 5) / 4f) + UnityEngine.Random.Range(-0.4f, 0.8f);
            if (radius < minRadius) radius = minRadius;

            Vector3 candidate = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            if (TerrainMeta.HeightMap != null)
                candidate.y = TerrainMeta.HeightMap.GetHeight(candidate);

            if (NavmeshSpawnPoint.Find(candidate, searchRadius, out Vector3 navPos))
                return navPos;

            // Fallback: random ring attempts
            for (int i = 0; i < 6; i++)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(minRadius, maxRadius);
                Vector3 sample = center + new Vector3(offset.x, 0f, offset.y);
                if (TerrainMeta.HeightMap != null)
                    sample.y = TerrainMeta.HeightMap.GetHeight(sample);
                if (NavmeshSpawnPoint.Find(sample, searchRadius, out navPos))
                    return navPos;
            }

            if (NavmeshSpawnPoint.Find(center, searchRadius, out navPos))
                return navPos;

            return center;
        }

        private static bool HordeMergeQuery(Horde horde) => horde.MemberCount < horde.maximumMemberCount;

        private void TryMergeHordes()
        {
            if (!ConfigData.Configuration.Horde.MergeHordes || nextMergeTime > Time.time)
                return;

            nextMergeTime = Time.time + MERGE_CHECK_RATE;
            if (members == null || MemberCount >= maximumMemberCount) return;

            int results = HordeGrid.Query(CentralLocation.x, CentralLocation.z, 30f, HordeGridQueryResults, HordeMergeQuery);
            if (results <= 1) return;

            int amountToMerge = maximumMemberCount - members.Count;
            for (int i = 0; i < results; i++)
            {
                Horde otherHorde = HordeGridQueryResults[i];
                if (otherHorde == this) continue;
                if (MemberCount >= maximumMemberCount || otherHorde.members == null) break;

                if (amountToMerge >= otherHorde.members.Count)
                {
                    for (int y = 0; y < otherHorde.members.Count; y++)
                    {
                        ZombieNPC zombieNpc = otherHorde.members[y];
                        members.Add(zombieNpc);
                        zombieNpc.Horde = this;
                        zombieNpc.IsGroupLeader = false;
                        zombieNpc.OnInitialSpawn();
                    }
                    otherHorde.members.Clear();
                    otherHorde.Destroy();
                }
                else
                {
                    for (int y = 0; y < amountToMerge; y++)
                    {
                        if (otherHorde.members.Count > 0)
                        {
                            ZombieNPC zombieNpc = otherHorde.members[otherHorde.MemberCount - 1];
                            members.Add(zombieNpc);
                            zombieNpc.Horde = this;
                            zombieNpc.IsGroupLeader = false;
                            zombieNpc.OnInitialSpawn();
                            otherHorde.members.Remove(zombieNpc);
                        }
                    }
                }
            }
        }

        private void DoSleepChecks()
        {
            if (Time.time < nextSleepTime) return;
            nextSleepTime = Time.time + SLEEP_CHECK_RATE + UnityEngine.Random.Range(1f, 5f);

            // Local roam requires RoamState ticking — never freeze brains while LocalRoam is on.
            if (ConfigData.Configuration?.Horde?.LocalRoam == true)
            {
                if (IsSleeping) SetSleeping(false);
                return;
            }

            if (ConfigData.Configuration.Member.EnableDormantSystem && ConfigData.Configuration.Member.DormantUntilSensedOnly)
            {
                if (!AnyHasTarget(out _) && !HasHumanTarget())
                    SetSleeping(true);
                return;
            }

            if (!ConfigData.Configuration.Member.EnableDormantSystem)
            {
                if (IsSleeping) SetSleeping(false);
                return;
            }

            // Wake/roam when players are within listen/sense range — not only the old fixed 80m.
            // (Online base raid scan 250m is separate and does not control combat sense.)
            float wakeRange = GetHordeWakeRange();
            int count = BaseEntity.Query.Server.GetPlayersInSphere(CentralLocation, wakeRange, PlayerVicinityQueryResults, HordeSleepPlayerFilter);
            if (count > 0)
            {
                if (IsSleeping) SetSleeping(false);
            }
            else if (!IsSleeping)
                SetSleeping(true);
        }

        private static float GetHordeWakeRange()
        {
            float range = 80f;
            var loadouts = ConfigData.Configuration?.Member?.Loadouts;
            if (loadouts != null)
            {
                for (int i = 0; i < loadouts.Count; i++)
                {
                    var sensory = loadouts[i]?.Sensory;
                    if (sensory == null) continue;
                    if (sensory.ListenRange > range) range = sensory.ListenRange;
                    if (sensory.SenseRange > range) range = sensory.SenseRange;
                }
            }
            return range;
        }

        private void SetSleeping(bool sleep)
        {
            if (members == null) return;
            for (int i = 0; i < members.Count; i++)
            {
                ZombieNPC zombieNpc = members[i];
                if (!zombieNpc || zombieNpc.Brain == null) continue;
                if (zombieNpc.IsBrainSleeping == sleep) continue;
                zombieNpc.SetSleeping(sleep);
            }
            IsSleeping = sleep;
        }

        public void ForceWakeFromSleep()
        {
            if (!IsSleeping) return;
            SetSleeping(false);
        }

        private static bool HordeSleepPlayerFilter(BaseEntity entity)
        {
            BasePlayer basePlayer = entity as BasePlayer;
            if (!basePlayer || !basePlayer.IsConnected) return false;
            if (ZombieNPC.Get(basePlayer) != null) return false;
            if (ConfigData.Configuration.Member.IgnoreSleepers && basePlayer.IsSleeping()) return false;
            return true;
        }

        private static bool OnlineBaseRaidPlayerFilter(BaseEntity entity)
        {
            BasePlayer basePlayer = entity as BasePlayer;
            if (!basePlayer || !basePlayer.IsConnected) return false;
            if (ZombieNPC.Get(basePlayer) != null) return false;
            if (ConfigData.Configuration.Member.IgnoreSleepers && basePlayer.IsSleeping()) return false;
            return true;
        }

        public int GetMemberIndex(ZombieNPC zombieNpc) => members != null ? members.IndexOf(zombieNpc) : -1;

        public class SpawnOrder
        {
            public Vector3 Position { get; private set; }
            public int InitialMemberCount { get; private set; }
            public int MaximumMemberCount { get; private set; }
            public float MaximumRoamDistance { get; private set; }
            public string HordeProfile { get; private set; }

            public SpawnOrder(Vector3 position, int initialMemberCount, int maximumMemberCount, float maximumRoamDistance, string hordeProfile)
            {
                Position = position;
                InitialMemberCount = initialMemberCount;
                MaximumMemberCount = maximumMemberCount;
                MaximumRoamDistance = maximumRoamDistance;
                HordeProfile = hordeProfile;
            }

            private static readonly Queue<SpawnOrder> _spawnOrders = new Queue<SpawnOrder>();
            private static Coroutine _spawnRoutine;
            private static Coroutine _despawnRoutine;
            private static bool _isSpawning;
            private static bool _isDespawning;
            public static SpawnState State;

            public static void InitializeSpawnOrders()
            {
                State = ConfigData.Configuration.TimedSpawns.Enabled
                    ? (ShouldSpawn() ? SpawnState.Spawn : SpawnState.Despawn)
                    : SpawnState.Spawn;

                if (ConfigData.Configuration.TimedSpawns.Enabled)
                    StartTimer();
            }

            internal static void Create(Vector3 position, int initialMemberCount, int maximumMemberCount, float maximumRoamDistance, string hordeProfile)
            {
                if (NavmeshSpawnPoint.Find(position, 10f, out position))
                {
                    _spawnOrders.Enqueue(new SpawnOrder(position, initialMemberCount, maximumMemberCount, maximumRoamDistance, hordeProfile));
                    if (!_isSpawning && State == SpawnState.Spawn)
                        DequeueAndSpawn();
                }
            }

            internal static void Create(Horde horde)
            {
                if (horde == null || ZombieHordePlugin.Instance == null || ConfigData.Configuration?.Horde == null)
                    return;

                Vector3 basePos;
                try
                {
                    basePos = horde.IsLocalHorde ? horde.InitialPosition : ZombieHordePlugin.Instance.GetSpawnPoint();
                }
                catch (Exception ex)
                {
                    Compat.PrintWarning("SpawnOrder.Create(Horde) GetSpawnPoint: " + ex.Message);
                    return;
                }

                if (NavmeshSpawnPoint.Find(basePos, 10f, out Vector3 position))
                {
                    _spawnOrders.Enqueue(new SpawnOrder(position, horde.initialMemberCount, horde.maximumMemberCount,
                        horde.IsLocalHorde ? horde.MaximumRoamDistance : -1, horde.hordeProfile));
                    if (!_isSpawning && State == SpawnState.Spawn)
                        DequeueAndSpawn();
                }
            }

            internal static void Create(Vector3 position, ConfigData.MonumentSpawn.MonumentSettings settings)
            {
                if (NavmeshSpawnPoint.Find(position, 10f, out position))
                {
                    _spawnOrders.Enqueue(new SpawnOrder(position, ConfigData.Configuration.Horde.InitialMemberCount,
                        settings.HordeSize, settings.RoamDistance, settings.Profile));
                    if (!_isSpawning && State == SpawnState.Spawn)
                        DequeueAndSpawn();
                }
            }

            private static void DequeueAndSpawn()
            {
                if (ServerMgr.Instance == null) return;
                if (_spawnRoutine != null)
                    ServerMgr.Instance.StopCoroutine(_spawnRoutine);
                _spawnRoutine = ServerMgr.Instance.StartCoroutine(ProcessSpawnOrders());
            }

            private static void QueueAndDespawn()
            {
                if (ServerMgr.Instance == null) return;
                if (_despawnRoutine != null)
                    ServerMgr.Instance.StopCoroutine(_despawnRoutine);
                _despawnRoutine = ServerMgr.Instance.StartCoroutine(ProcessDespawn());
            }

            private static void StopSpawning()
            {
                if (_spawnRoutine != null && ServerMgr.Instance != null)
                    ServerMgr.Instance.StopCoroutine(_spawnRoutine);
                _isSpawning = false;
            }

            private static void StopDespawning()
            {
                if (_despawnRoutine != null && ServerMgr.Instance != null)
                    ServerMgr.Instance.StopCoroutine(_despawnRoutine);
                _isDespawning = false;
            }

            private static IEnumerator ProcessSpawnOrders()
            {
                if (_spawnOrders.Count == 0) yield break;
                _isSpawning = true;

                RESTART:
                if (_isDespawning) StopDespawning();

                var cfg = ConfigData.Configuration;
                if (cfg?.Horde == null || ZombieHordePlugin.Instance == null)
                {
                    _spawnOrders.Clear();
                    _spawnRoutine = null;
                    _isSpawning = false;
                    yield break;
                }

                while (AllHordes.Count > cfg.Horde.MaximumHordes)
                {
                    yield return CoroutineEx.waitForSeconds(10f);
                    cfg = ConfigData.Configuration;
                    if (cfg?.Horde == null || ZombieHordePlugin.Instance == null)
                    {
                        _spawnOrders.Clear();
                        _spawnRoutine = null;
                        _isSpawning = false;
                        yield break;
                    }
                }

                if (_spawnOrders.Count == 0)
                {
                    _spawnRoutine = null;
                    _isSpawning = false;
                    yield break;
                }

                SpawnOrder spawnOrder = _spawnOrders.Dequeue();
                if (spawnOrder != null)
                {
                    try { Horde.Create(spawnOrder); }
                    catch (Exception ex) { Compat.PrintWarning("ProcessSpawnOrders Horde.Create: " + ex.Message); }
                }

                if (_spawnOrders.Count > 0)
                {
                    yield return CoroutineEx.waitForSeconds(3f);
                    goto RESTART;
                }

                _spawnRoutine = null;
                _isSpawning = false;
            }

            private static IEnumerator ProcessDespawn()
            {
                _isDespawning = true;
                if (_isSpawning) StopSpawning();

                while (AllHordes.Count > 0)
                {
                    Horde horde = AllHordes.GetRandom();
                    if (!horde.HasHumanTarget())
                    {
                        Create(horde);
                        horde.Destroy(true, true);
                    }
                    yield return CoroutineEx.waitForSeconds(3f);
                }

                _despawnRoutine = null;
                _isDespawning = false;
            }

            internal static void OnUnload()
            {
                if (ServerMgr.Instance != null)
                {
                    if (_spawnRoutine != null) ServerMgr.Instance.StopCoroutine(_spawnRoutine);
                    if (_despawnRoutine != null) ServerMgr.Instance.StopCoroutine(_despawnRoutine);
                }
                _isDespawning = false;
                _isSpawning = false;
                State = SpawnState.Spawn;
                _spawnOrders.Clear();
            }

            private static void StartTimer() => Compat.Timer.In(1f, CheckTime);

            private static bool ShouldSpawn()
            {
                float currentTime = TOD_Sky.Instance.Cycle.Hour;
                if (ConfigData.Configuration.TimedSpawns.Start > ConfigData.Configuration.TimedSpawns.End)
                    return currentTime > ConfigData.Configuration.TimedSpawns.Start || currentTime < ConfigData.Configuration.TimedSpawns.End;
                return currentTime > ConfigData.Configuration.TimedSpawns.Start && currentTime < ConfigData.Configuration.TimedSpawns.End;
            }

            private static void CheckTime()
            {
                if (ShouldSpawn())
                {
                    if (State == SpawnState.Despawn)
                    {
                        if (ConfigData.Configuration.TimedSpawns.BroadcastStart)
                            SendNotification("Notification.BeginSpawn");
                        State = SpawnState.Spawn;
                        StopDespawning();
                        DequeueAndSpawn();
                    }
                }
                else if (State == SpawnState.Spawn)
                {
                    if (ConfigData.Configuration.TimedSpawns.BroadcastEnd)
                        SendNotification("Notification.BeginDespawn");
                    State = SpawnState.Despawn;
                    if (ConfigData.Configuration.TimedSpawns.Despawn)
                    {
                        StopSpawning();
                        QueueAndDespawn();
                    }
                }
                StartTimer();
            }

            private static void SendNotification(string key)
            {
                foreach (BasePlayer player in BasePlayer.activePlayerList)
                    player.ChatMessage(Compat.Lang.GetMessage(key, null, player.UserIDString));
            }
        }
    }

    public enum SpawnSystem { None, Random, SpawnsDatabase }
    public enum SpawnState { Spawn, Despawn }
}
