using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Facepunch;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rust.Ai.Gen2;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Assertions;
using AnimalSpawn.AnimalSpawnExtensionMethods;

namespace AnimalSpawn
{
    /// <summary>
    /// Harmony port of Oxide AnimalSpawn 1.0.81. Custom BaseAnimalNPC + SpawnAnimal API for GrimmBoss.
    /// Horse purchase/claim limits stay in Shop — this mod does not register animalspawn.horse.
    /// </summary>
    public class AnimalSpawn : IHarmonyModHooks
    {
        public static AnimalSpawn Instance { get; private set; }
        public static readonly ulong CUSTOM_ANIMAL_SKIN_ID = 11491311214163UL;
        public static readonly VersionNumber Version = new VersionNumber(1, 0, 81);

        public const string AppDomainTypeKey = "AnimalSpawn.Type";
        public const string AppDomainInstanceKey = "AnimalSpawn.Instance";
        public const string AppDomainApiKey = "AnimalSpawn_ApiType";
        public const string AppDomainDamageKey = "AnimalSpawn_CanEntityTakeDamage";
        public const string AppDomainTargetKey = "AnimalSpawn_CanEntityBeTargeted";

        private static AnimalSpawn _ins;
        private const bool En = true;
        private int _navTraceCounter;
        private PluginConfig _config;
        private readonly Dictionary<string, AnimalConfig> _presets = new Dictionary<string, AnimalConfig>();
        private readonly Dictionary<string, Dictionary<int, Dictionary<int, PointNavMeshFile>>> _allNavMeshes =
            new Dictionary<string, Dictionary<int, Dictionary<int, PointNavMeshFile>>>();
        private readonly Dictionary<ulong, CustomAnimalNpc> _animals = new Dictionary<ulong, CustomAnimalNpc>();

        internal OxideCompat.TimerHelper timer => OxideCompat.Timer;

        #region Config

        private string NextNavTraceId(string prefix) => $"{prefix}-{++_navTraceCounter}";

        private string GetNavMeshFailureReason(Vector3 position, float radius = 4f, int areaMask = NavMesh.AllAreas)
        {
            float terrainY = TerrainMeta.HeightMap != null ? TerrainMeta.HeightMap.GetHeight(position) : 0f;
            float waterY = TerrainMeta.WaterMap != null ? TerrainMeta.WaterMap.GetHeight(position) : 0f;

            if (!NavMesh.SamplePosition(position, out NavMeshHit navHit, radius, areaMask))
                return $"no NavMesh sample within {radius:0.0}m (terrainY={terrainY:0.00}, waterY={waterY:0.00})";

            float distance = Vector3.Distance(position, navHit.position);
            float verticalDelta = Mathf.Abs(navHit.position.y - position.y);
            if (distance > 1.5f || verticalDelta > 1f)
                return $"nearest NavMesh sample is {distance:0.00}m away at {navHit.position} (vertical delta {verticalDelta:0.00}, terrainY={terrainY:0.00}, waterY={waterY:0.00})";

            return "NavMesh sample exists nearby; likely wrong agentTypeID/areaMask or spawn order";
        }

        private void TraceNavMeshRisk(string traceId, string subject, Vector3 requestedPosition, string context, float radius = 4f, int areaMask = NavMesh.AllAreas)
        {
            string reason = GetNavMeshFailureReason(requestedPosition, radius, areaMask);
            if (reason.StartsWith("NavMesh sample exists nearby"))
                return;
            PrintWarning($"[NavTrace {traceId}] {subject} Failed to create agent because it is not close enough to the NavMesh. requested={requestedPosition} reason={reason} context={context}");
        }

        private void TraceSpawnedNavEntity(string traceId, BaseEntity entity, string subject, string context, float radius = 4f, int areaMask = NavMesh.AllAreas)
        {
            if (entity == null || entity.IsDestroyed) return;
            NavMeshAgent agent = entity.GetComponent<NavMeshAgent>();
            if (agent == null || agent.isOnNavMesh) return;
            string reason = GetNavMeshFailureReason(entity.transform.position, radius, areaMask);
            PrintWarning($"[NavTrace {traceId}] {subject} Failed to create agent because it is not close enough to the NavMesh. actual={entity.transform.position} reason={reason} context={context}");
        }

        private void LoadConfig()
        {
            _config = OxideCompat.ReadConfig<PluginConfig>();
            if (_config == null) _config = PluginConfig.DefaultConfig();
            if (_config.PluginVersion.Major == 0 && _config.PluginVersion.Minor == 0 && _config.PluginVersion.Patch == 0)
                _config.PluginVersion = Version;
            if (_config.PluginVersion < Version)
            {
                _config.PluginVersion = Version;
                SaveConfig();
            }
        }

        private void SaveConfig() => OxideCompat.WriteConfig(_config);

        private class PluginConfig
        {
            [JsonProperty(En ? "Can AnimalSpawn animals attack other animals? [true/false]" : "Могут ли кастомные животные атаковать других животных? [true/false]")]
            public bool CanTargetAnimal { get; set; }

            [JsonProperty(En ? "Can AnimalSpawn animals attack NPCs? [true/false]" : "Могут ли кастомные животные атаковать NPC? [true/false]")]
            public bool CanTargetNpc { get; set; }

            [JsonProperty(En ? "Can AnimalSpawn animals attack sleeping players? [true/false]" : "Могут ли кастомные животные атаковать спящих игроков? [true/false]")]
            public bool CanTargetSleepingPlayer { get; set; }

            [JsonProperty(En ? "Can AnimalSpawn animals attack wounded players? [true/false]" : "Могут ли кастомные животные атаковать игроков в состоянии Wounded? [true/false]")]
            public bool CanTargetWoundedPlayer { get; set; }

            [JsonProperty(En ? "Configuration version" : "Версия конфигурации")]
            public VersionNumber PluginVersion { get; set; }

            public static PluginConfig DefaultConfig()
            {
                return new PluginConfig
                {
                    CanTargetAnimal = false,
                    CanTargetNpc = false,
                    CanTargetSleepingPlayer = false,
                    CanTargetWoundedPlayer = false,
                    PluginVersion = Version
                };
            }
        }
        #endregion Config

        public class AnimalConfig
        {
            public string Prefab { get; set; }
            public float Health { get; set; }
            public float RoamRange { get; set; }
            public float ChaseRange { get; set; }
            public float SenseRange { get; set; }
            public float ListenRange { get; set; }
            public float AttackRange { get; set; }
            public bool CheckVisionCone { get; set; }
            public float VisionCone { get; set; }
            public bool HostileTargetsOnly { get; set; }
            public float AttackDamage { get; set; }
            public float AttackRate { get; set; }
            public float TurretDamageScale { get; set; }
            public bool CanRunAwayWater { get; set; }
            public bool CanSleep { get; set; }
            public float SleepDistance { get; set; }
            public float Speed { get; set; }
            public int AreaMask { get; set; }
            public int AgentTypeID { get; set; }
            public string HomePosition { get; set; }
            public float MemoryDuration { get; set; }
            public HashSet<string> States { get; set; }
        }

        #region Methods

        public static bool IsCustomAnimal(BaseEntity entity) => entity != null && entity.skinID == CUSTOM_ANIMAL_SKIN_ID;

        public BaseAnimalNPC SpawnPreset(Vector3 position, string preset) =>
            !string.IsNullOrEmpty(preset) && _presets.ContainsKey(preset) ? CreateCustomAnimal(position, _presets[preset]) : null;

        /// <summary>GrimmBoss / Plugin.Call entry: SpawnAnimal(position, JObject|AnimalConfig).</summary>
        public BaseAnimalNPC SpawnAnimal(Vector3 position, object configObj)
        {
            if (configObj == null) return null;
            if (configObj is AnimalConfig typed)
                return CreateCustomAnimal(position, typed);
            try
            {
                JObject jo = configObj as JObject ?? configObj as JToken as JObject;
                if (jo == null && configObj is string json)
                    jo = JObject.Parse(json);
                if (jo == null)
                {
                    PrintError($"SpawnAnimal: unsupported config type {configObj.GetType().FullName}");
                    return null;
                }
                return SpawnAnimal(position, jo);
            }
            catch (Exception ex)
            {
                PrintError($"SpawnAnimal(object) failed: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        public BaseAnimalNPC SpawnAnimal(Vector3 position, JObject jo)
        {
            if (jo == null) return null;
            try
            {
                AnimalConfig cfg = jo.ToObject<AnimalConfig>();
                if (cfg == null || string.IsNullOrEmpty(cfg.Prefab)) return null;
                if (cfg.States == null || cfg.States.Count == 0)
                    cfg.States = new HashSet<string>(new[] { "RoamState", "ChaseState", "CombatState" });
                if (cfg.AreaMask == 0) cfg.AreaMask = 1;
                return CreateCustomAnimal(position, cfg);
            }
            catch (Exception ex)
            {
                PrintError($"SpawnAnimal(JObject) failed: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        public object Call(string method, params object[] args)
        {
            if (string.IsNullOrEmpty(method) || args == null) return null;
            try
            {
                if (string.Equals(method, "SpawnAnimal", StringComparison.OrdinalIgnoreCase) && args.Length >= 2 && args[0] is Vector3 pos)
                    return SpawnAnimal(pos, args[1]);
                if (string.Equals(method, "SpawnPreset", StringComparison.OrdinalIgnoreCase) && args.Length >= 2 && args[0] is Vector3 presetPos)
                    return SpawnPreset(presetPos, args[1] as string);
                if (string.Equals(method, "IsCustomAnimal", StringComparison.OrdinalIgnoreCase) && args.Length >= 1)
                    return IsCustomAnimal(args[0] as BaseEntity);
            }
            catch (Exception ex)
            {
                PrintError($"Call({method}) failed: {ex.Message}");
            }
            return null;
        }

        private static readonly HashSet<string> _loggedUnsupportedPrefabs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Old Gen1 wolf prefab was deleted; current wolf is Gen2 wolf2 (not wrappable here).</summary>
        private static string ResolveAnimalPrefab(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return prefab;
            if (prefab.IndexOf("/wolf/wolf.prefab", StringComparison.OrdinalIgnoreCase) >= 0)
                return "assets/rust.ai/agents/wolf/wolf2.prefab";
            return prefab;
        }

        private CustomAnimalNpc CreateCustomAnimal(Vector3 position, AnimalConfig config)
        {
            if (config == null || string.IsNullOrEmpty(config.Prefab))
            {
                PrintError("CreateCustomAnimal: null config or prefab");
                return null;
            }

            config.Prefab = ResolveAnimalPrefab(config.Prefab);

            GameObject prefabGo = GameManager.server.FindPrefab(config.Prefab);
            if (prefabGo == null)
            {
                PrintError($"CreateCustomAnimal: prefab not found '{config.Prefab}' at {position}");
                return null;
            }
            if (prefabGo.GetComponent<BaseAnimalNPC>() == null)
            {
                if (_loggedUnsupportedPrefabs.Add(config.Prefab))
                    PrintError($"CreateCustomAnimal: '{config.Prefab}' is not a Gen1 BaseAnimalNPC (Wolf2/Tiger/etc. must be spawned by GrimmBoss Gen2 path).");
                return null;
            }

            string traceId = NextNavTraceId("animal");
            TraceNavMeshRisk(traceId, "Custom animal spawn", position,
                $"prefab={config.Prefab} areaMask={config.AreaMask} agentTypeID={config.AgentTypeID}",
                4f, config.AreaMask != 0 ? config.AreaMask : NavMesh.AllAreas);

            BaseAnimalNPC animalNpc = GameManager.server.CreateEntity(config.Prefab, position, Quaternion.identity, false) as BaseAnimalNPC;
            if (animalNpc == null)
            {
                PrintError($"CreateCustomAnimal: CreateEntity returned null for '{config.Prefab}' at {position}");
                return null;
            }

            AnimalBrain animalBrain = animalNpc.GetComponent<AnimalBrain>();
            if (animalBrain == null)
            {
                PrintError($"CreateCustomAnimal: '{config.Prefab}' has no AnimalBrain");
                UnityEngine.Object.DestroyImmediate(animalNpc.gameObject);
                return null;
            }

            CustomAnimalNpc customAnimal = animalNpc.gameObject.AddComponent<CustomAnimalNpc>();
            CustomAnimalBrain customAnimalBrain = animalNpc.gameObject.AddComponent<CustomAnimalBrain>();

            CopySerializableFields(animalNpc, customAnimal);
            CopySerializableFields(animalBrain, customAnimalBrain);

            UnityEngine.Object.DestroyImmediate(animalNpc, true);
            UnityEngine.Object.DestroyImmediate(animalBrain, true);

            customAnimal.Config = config;
            customAnimal.brain = customAnimalBrain;
            customAnimal.enableSaving = false;
            customAnimal.gameObject.AwakeFromInstantiate();
            customAnimal.Spawn();
            timer.Once(0.1f, () => TraceSpawnedNavEntity(traceId, customAnimal, "Custom animal spawn",
                $"prefab={config.Prefab} home={customAnimal?.HomePosition} areaMask={config.AreaMask} agentTypeID={config.AgentTypeID}",
                4f, config.AreaMask != 0 ? config.AreaMask : NavMesh.AllAreas));

            customAnimal.skinID = CUSTOM_ANIMAL_SKIN_ID;
            if (customAnimal.net != null)
                _animals[customAnimal.net.ID.Value] = customAnimal;

            return customAnimal;
        }

        private static void CopySerializableFields<T>(T src, T dst)
        {
            FieldInfo[] srcFields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (FieldInfo field in srcFields)
            {
                object value = field.GetValue(src);
                field.SetValue(dst, value);
            }
        }

        public void SetParentEntity(CustomAnimalNpc animal, BaseEntity parent, Vector3 pos)
        {
            if (IsCustomAnimal(animal) && parent != null) animal.SetParentEntity(parent, pos);
        }

        public void SetHomePosition(CustomAnimalNpc animal, Vector3 pos)
        {
            if (IsCustomAnimal(animal)) animal.HomePosition = pos;
        }

        public void SetCustomNavMesh(CustomAnimalNpc animal, Transform transform, string navMeshName)
        {
            if (!IsCustomAnimal(animal) || transform == null || !_allNavMeshes.ContainsKey(navMeshName)) return;
            Dictionary<int, Dictionary<int, PointNavMeshFile>> navMesh = _allNavMeshes[navMeshName];
            for (int i = 0; i < navMesh.Count; i++)
            {
                if (!animal.CustomNavMesh.ContainsKey(i)) animal.CustomNavMesh.Add(i, new Dictionary<int, PointNavMesh>());
                for (int j = 0; j < navMesh[i].Count; j++)
                {
                    PointNavMeshFile pointNavMesh = navMesh[i][j];
                    animal.CustomNavMesh[i].Add(j, new PointNavMesh { Position = transform.TransformPoint(pointNavMesh.Position.ToVector3()), Enabled = pointNavMesh.Enabled });
                }
            }
            animal.InitCustomNavMesh();
        }

        public void AddStates(CustomAnimalNpc animal, HashSet<string> states)
        {
            if (animal?.brain == null || states == null) return;
            if (states.Contains("RoamState")) animal.brain.AddState(new CustomAnimalBrain.RoamState(animal));
            if (states.Contains("ChaseState")) animal.brain.AddState(new CustomAnimalBrain.ChaseState(animal));
            if (states.Contains("CombatState")) animal.brain.AddState(new CustomAnimalBrain.CombatState(animal));
            if (states.Contains("DestroyState")) animal.brain.AddState(new CustomAnimalBrain.DestroyState(animal));
        }
        #endregion Methods

        #region Controller
        public class CustomAnimalNpc : BaseAnimalNPC
        {
            public AnimalConfig Config { get; set; }
            public Vector3 HomePosition { get; set; }
            public float DistanceFromBase => Vector3.Distance(transform.position, HomePosition);

            public override void ServerInit()
            {
                base.ServerInit();

                HomePosition = string.IsNullOrEmpty(Config.HomePosition) ? transform.position : Config.HomePosition.ToVector3();

                if (NavAgent == null) NavAgent = GetComponent<RustNavMeshAgent>();
                if (NavAgent != null)
                {
                    NavAgent.areaMask = Config.AreaMask;
                    NavAgent.agentTypeID = Config.AgentTypeID;
                }

                AttackDamage = Config.AttackDamage;
                AttackRate = Config.AttackRate;
                AttackRange = Config.AttackRange;

                startHealth = Config.Health;
                _health = Config.Health;
                _maxHealth = Config.Health;

                InvokeRepeating(UpdateTick, 1f, 2f);
            }

            private void OnDestroy() => CancelInvoke();

            private void UpdateTick()
            {
                if (CanRunAwayWater()) RunAwayWater();
                UpdateSleep();
            }

            public override void OnDied(HitInfo hitInfo = null)
            {
                if (hitInfo != null)
                {
                    BasePlayer initiatorPlayer = hitInfo.InitiatorPlayer;
                    if (initiatorPlayer != null)
                    {
                        if (UnityEngine.Application.isEditor || ConVar.Server.official)
                            initiatorPlayer.ClientRPC(RpcTarget.Player("RecieveAchievement", initiatorPlayer), "KILL_ANIMAL");
                        if (!string.IsNullOrEmpty(deathStatName))
                        {
                            initiatorPlayer.stats.Add(deathStatName, 1, global::Stats.Steam | global::Stats.Life);
                            initiatorPlayer.stats.Save();
                        }
                        initiatorPlayer.LifeStoryKill(this);
                    }
                }
                Assert.IsTrue(isServer, "OnDied called on client!");
                if (OxideCompat.CallHook("CanCustomAnimalSpawnCorpse", this) == null)
                {
                    BaseCorpse baseCorpse = DropCorpse(CorpsePrefab.resourcePath);
                    if (baseCorpse)
                    {
                        baseCorpse.Spawn();
                        baseCorpse.TakeChildren(this);
                    }
                }
                KillMessage();
            }

            internal void UpdateTarget()
            {
                BaseEntity target = null;
                float single = -1f;
                foreach (BaseEntity entity in brain.Senses.Players)
                {
                    if (!CanTargetEntity(entity)) continue;
                    float single2 = GetSingle2(entity);
                    if (single2 <= single) continue;
                    target = entity;
                    single = single2;
                }
                if (brain.Senses.senseTypes.HasFlag(EntityType.NPC))
                {
                    foreach (BaseEntity entity in brain.Senses.Memory.LOS)
                    {
                        if (!CanTargetEntity(entity)) continue;
                        float single2 = GetSingle2(entity);
                        if (single2 <= single) continue;
                        target = entity;
                        single = single2;
                    }
                    foreach (BaseEntity entity in brain.Senses.Memory.Targets)
                    {
                        if (!CanTargetEntity(entity)) continue;
                        float single2 = GetSingle2(entity);
                        if (single2 <= single) continue;
                        target = entity;
                        single = single2;
                    }
                    foreach (BaseEntity entity in brain.Senses.Memory.Threats)
                    {
                        if (!CanTargetEntity(entity)) continue;
                        float single2 = GetSingle2(entity);
                        if (single2 <= single) continue;
                        target = entity;
                        single = single2;
                    }
                    foreach (BaseEntity entity in brain.Senses.Memory.Friendlies)
                    {
                        if (!CanTargetEntity(entity)) continue;
                        float single2 = GetSingle2(entity);
                        if (single2 <= single) continue;
                        target = entity;
                        single = single2;
                    }
                }
                AttackTarget = target;
            }

            private float GetSingle2(BaseEntity entity)
            {
                float single2 = 1f - Mathf.InverseLerp(1f, brain.SenseRange, Vector3.Distance(entity.transform.position, transform.position));
                single2 += Mathf.InverseLerp(brain.VisionCone, 1f, Vector3.Dot((entity.transform.position - transform.position).normalized, transform.forward)) / 2f;
                single2 += brain.Senses.Memory.IsLOS(entity) ? 2f : 0f;
                return single2;
            }

            internal bool CanTargetEntity(BaseEntity target)
            {
                if (target == null || target.Health() <= 0f) return false;
                object hook = OxideCompat.CallHook("OnCustomAnimalTarget", this, target);
                if (hook is bool) return (bool)hook;
                if (target is BasePlayer)
                {
                    BasePlayer basePlayer = target as BasePlayer;
                    if (basePlayer.IsDead()) return false;
                    if (basePlayer.userID.IsSteamId()) return CanTargetPlayer(basePlayer);
                    if (basePlayer is NPCPlayer) return CanTargetNpcPlayer(basePlayer as NPCPlayer);
                    return false;
                }
                if (target is BaseAnimalNPC) return CanTargetAnimal(target as BaseAnimalNPC);
                return false;
            }

            internal bool CanTargetPlayer(BasePlayer target)
            {
                if (_ins?._config != null)
                {
                    if (!_ins._config.CanTargetSleepingPlayer && target.IsSleeping()) return false;
                    if (!_ins._config.CanTargetWoundedPlayer && target.IsWounded()) return false;
                }
                if (target._limitedNetworking) return false;
                return true;
            }

            internal bool CanTargetNpcPlayer(NPCPlayer target)
            {
                if (target is FrankensteinPet) return true;
                if (target.skinID == 11162132011012) return false;
                return _ins?._config != null && _ins._config.CanTargetNpc;
            }

            internal bool CanTargetAnimal(BaseAnimalNPC animal)
            {
                if (animal.IsDead()) return false;
                return _ins?._config != null && _ins._config.CanTargetAnimal;
            }

            public float DistanceToTarget => Vector3.Distance(transform.position, AttackTarget.transform.position);

            internal bool IsRunAwayWater { get; set; }

            private bool CanRunAwayWater()
            {
                if (!Config.CanRunAwayWater || IsRunAwayWater) return false;
                if (AttackTarget == null)
                    return transform.position.y < -0.25f;
                if (transform.position.y > -0.25f || TerrainMeta.HeightMap.GetHeight(AttackTarget.transform.position) > -0.25f) return false;
                if (DistanceToTarget < EngagementRange()) return false;
                return true;
            }

            private void RunAwayWater()
            {
                IsRunAwayWater = true;
                AttackTarget = null;
                Invoke(FinishRunAwayWater, 20f);
            }

            private void FinishRunAwayWater() => IsRunAwayWater = false;

            private BaseEntity ParentEntity { get; set; }
            private Vector3 LocalPos { get; set; } = Vector3.zero;

            internal void SetParentEntity(BaseEntity parent, Vector3 pos)
            {
                ParentEntity = parent;
                LocalPos = pos;
                InvokeRepeating(UpdateHomePositionParent, 0f, 0.1f);
            }

            private void UpdateHomePositionParent()
            {
                if (ParentEntity != null) HomePosition = ParentEntity.transform.TransformPoint(LocalPos);
                else
                {
                    LocalPos = Vector3.zero;
                    CancelInvoke(UpdateHomePositionParent);
                }
            }

            internal Dictionary<int, Dictionary<int, PointNavMesh>> CustomNavMesh = new Dictionary<int, Dictionary<int, PointNavMesh>>();
            internal int CurrentI { get; set; }
            internal int CurrentJ { get; set; }
            internal List<PointPath> Path { get; set; } = new List<PointPath>();
            internal CustomNavMeshController NavMeshController { get; set; }

            public class PointPath { public Vector3 Position; public int I; public int J; }

            internal void InitCustomNavMesh()
            {
                if (NavAgent != null && NavAgent.enabled) NavAgent.enabled = false;

                NavMeshController = gameObject.AddComponent<CustomNavMeshController>();
                NavMeshController.enabled = false;

                Vector3 result = Vector3.zero;
                float finishDistance = float.PositiveInfinity;
                for (int i = 0; i < CustomNavMesh.Count; i++)
                {
                    for (int j = 0; j < CustomNavMesh[i].Count; j++)
                    {
                        PointNavMesh pointNavMesh = CustomNavMesh[i][j];
                        if (!pointNavMesh.Enabled) continue;
                        float pointDistance = Vector3.Distance(pointNavMesh.Position, transform.position);
                        if (pointDistance < finishDistance)
                        {
                            result = pointNavMesh.Position;
                            CurrentI = i; CurrentJ = j;
                            finishDistance = pointDistance;
                        }
                    }
                }
                transform.position = result;
            }

            private void CalculatePath(Vector3 targetPos)
            {
                if (Path.Count > 0 && Vector3.Distance(Path.Last().Position, targetPos) <= 1.5f) return;
                Vector3 finishPos = GetNearPos(targetPos);
                if (Vector3.Distance(transform.position, finishPos) <= 1.5f) return;
                HashSet<Vector3> blacklist = new HashSet<Vector3>();
                Vector3 currentPos = CustomNavMesh[CurrentI][CurrentJ].Position, nextPos = Vector3.zero;
                int currentI = CurrentI, nextI, currentJ = CurrentJ, nextJ;
                List<PointPath> unsortedPath = Pool.Get<List<PointPath>>();
                int protection = 500;
                while (nextPos != finishPos && protection > 0)
                {
                    protection--;
                    FindNextPosToTarget(currentI, currentJ, blacklist, targetPos, out nextPos, out nextI, out nextJ);
                    if (nextPos == Vector3.zero) break;
                    blacklist.Add(currentPos);
                    currentPos = nextPos;
                    currentI = nextI; currentJ = nextJ;
                    unsortedPath.Add(new PointPath { Position = nextPos, I = nextI, J = nextJ });
                }
                PointPath currentPoint = unsortedPath.Last();
                List<PointPath> reversePath = Pool.Get<List<PointPath>>(); reversePath.Add(currentPoint);
                protection = 500;
                while ((Math.Abs(currentPoint.I - CurrentI) > 1 || Math.Abs(currentPoint.J - CurrentJ) > 1) && protection > 0)
                {
                    protection--;
                    PointPath nextPoint = null;
                    float finishDistance = float.PositiveInfinity;
                    foreach (PointPath point in unsortedPath)
                    {
                        if (Math.Abs(point.I - currentPoint.I) > 1) continue;
                        if (Math.Abs(point.J - currentPoint.J) > 1) continue;
                        if (reversePath.Contains(point)) continue;
                        float pointDistance = Vector3.Distance(point.Position, transform.position);
                        if (pointDistance < finishDistance)
                        {
                            nextPoint = point;
                            finishDistance = pointDistance;
                        }
                    }
                    if (nextPoint == null) break;
                    reversePath.Add(nextPoint);
                    currentPoint = nextPoint;
                }
                Pool.FreeUnmanaged(ref unsortedPath);
                Path.Clear();
                for (int i = reversePath.Count - 1; i >= 0; i--) Path.Add(reversePath[i]);
                Pool.FreeUnmanaged(ref reversePath);
                if (!NavMeshController.enabled && Path.Count > 0) NavMeshController.enabled = true;
            }

            private void FindNextPosToTarget(int currentI, int currentJ, ICollection<Vector3> blacklist, Vector3 targetPos, out Vector3 nextPos, out int nextI, out int nextJ)
            {
                nextPos = Vector3.zero; nextI = 0; nextJ = 0;
                float finishDistance = float.PositiveInfinity, pointDistance;

                if (currentI > 0)
                {
                    if (currentJ + 1 < CustomNavMesh[currentI - 1].Count)
                    {
                        PointNavMesh point1 = CustomNavMesh[currentI - 1][currentJ + 1];
                        if (IsPointEnabled(point1, blacklist))
                        {
                            pointDistance = Vector3.Distance(point1.Position, targetPos);
                            if (pointDistance < finishDistance)
                            {
                                nextPos = point1.Position;
                                nextI = currentI - 1; nextJ = currentJ + 1;
                                finishDistance = pointDistance;
                            }
                        }
                    }

                    PointNavMesh point4 = CustomNavMesh[currentI - 1][currentJ];
                    if (IsPointEnabled(point4, blacklist))
                    {
                        pointDistance = Vector3.Distance(point4.Position, targetPos);
                        if (pointDistance < finishDistance)
                        {
                            nextPos = point4.Position;
                            nextI = currentI - 1; nextJ = currentJ;
                            finishDistance = pointDistance;
                        }
                    }

                    if (currentJ > 0)
                    {
                        PointNavMesh point6 = CustomNavMesh[currentI - 1][currentJ - 1];
                        if (IsPointEnabled(point6, blacklist))
                        {
                            pointDistance = Vector3.Distance(point6.Position, targetPos);
                            if (pointDistance < finishDistance)
                            {
                                nextPos = point6.Position;
                                nextI = currentI - 1; nextJ = currentJ - 1;
                                finishDistance = pointDistance;
                            }
                        }
                    }
                }

                if (currentJ + 1 < CustomNavMesh[currentI].Count)
                {
                    PointNavMesh point2 = CustomNavMesh[currentI][currentJ + 1];
                    if (IsPointEnabled(point2, blacklist))
                    {
                        pointDistance = Vector3.Distance(point2.Position, targetPos);
                        if (pointDistance < finishDistance)
                        {
                            nextPos = point2.Position;
                            nextI = currentI; nextJ = currentJ + 1;
                            finishDistance = pointDistance;
                        }
                    }
                }

                if (currentJ > 0)
                {
                    PointNavMesh point7 = CustomNavMesh[currentI][currentJ - 1];
                    if (IsPointEnabled(point7, blacklist))
                    {
                        pointDistance = Vector3.Distance(point7.Position, targetPos);
                        if (pointDistance < finishDistance)
                        {
                            nextPos = point7.Position;
                            nextI = currentI; nextJ = currentJ - 1;
                            finishDistance = pointDistance;
                        }
                    }
                }

                if (currentI + 1 < CustomNavMesh.Count)
                {
                    if (currentJ + 1 < CustomNavMesh[currentI + 1].Count)
                    {
                        PointNavMesh point3 = CustomNavMesh[currentI + 1][currentJ + 1];
                        if (IsPointEnabled(point3, blacklist))
                        {
                            pointDistance = Vector3.Distance(point3.Position, targetPos);
                            if (pointDistance < finishDistance)
                            {
                                nextPos = point3.Position;
                                nextI = currentI + 1; nextJ = currentJ + 1;
                                finishDistance = pointDistance;
                            }
                        }
                    }

                    PointNavMesh point5 = CustomNavMesh[currentI + 1][currentJ];
                    if (IsPointEnabled(point5, blacklist))
                    {
                        pointDistance = Vector3.Distance(point5.Position, targetPos);
                        if (pointDistance < finishDistance)
                        {
                            nextPos = point5.Position;
                            nextI = currentI + 1; nextJ = currentJ;
                            finishDistance = pointDistance;
                        }
                    }

                    if (currentJ > 0)
                    {
                        PointNavMesh point8 = CustomNavMesh[currentI + 1][currentJ - 1];
                        if (IsPointEnabled(point8, blacklist))
                        {
                            pointDistance = Vector3.Distance(point8.Position, targetPos);
                            if (pointDistance < finishDistance)
                            {
                                nextPos = point8.Position;
                                nextI = currentI + 1; nextJ = currentJ - 1;
                                finishDistance = pointDistance;
                            }
                        }
                    }
                }
            }

            private static bool IsPointEnabled(PointNavMesh point, ICollection<Vector3> blacklist) => point.Enabled && !blacklist.Contains(point.Position);

            private Vector3 GetNearPos(Vector3 targetPos)
            {
                Vector3 result = Vector3.zero;
                float finishDistance = float.PositiveInfinity;
                for (int i = 0; i < CustomNavMesh.Count; i++)
                {
                    for (int j = 0; j < CustomNavMesh[i].Count; j++)
                    {
                        PointNavMesh pointNavMesh = CustomNavMesh[i][j];
                        if (!pointNavMesh.Enabled) continue;
                        float pointDistance = Vector3.Distance(pointNavMesh.Position, targetPos);
                        if (pointDistance < finishDistance)
                        {
                            result = pointNavMesh.Position;
                            finishDistance = pointDistance;
                        }
                    }
                }
                return result;
            }

            internal class CustomNavMeshController : FacepunchBehaviour
            {
                private Vector3 _startPos;
                private Vector3 _finishPos;
                private float _secondsTaken;
                private float _secondsToTake;
                private float _waypointDone = 1f;
                internal float Speed;
                private CustomAnimalNpc _animal;

                private void Awake() { _animal = GetComponent<CustomAnimalNpc>(); }

                private void FixedUpdate()
                {
                    if (_waypointDone >= 1f)
                    {
                        if (_animal.Path.Count == 0)
                        {
                            enabled = false;
                            return;
                        }
                        _startPos = _animal.transform.position;
                        PointPath point = _animal.Path[0];
                        if (point.Position != _startPos)
                        {
                            _animal.CurrentI = point.I; _animal.CurrentJ = point.J;
                            _finishPos = point.Position;
                            _secondsTaken = 0f;
                            _secondsToTake = Vector3.Distance(_finishPos, _startPos) / Speed;
                            _waypointDone = 0f;
                        }
                        _animal.Path.RemoveAt(0);
                    }
                    if (_startPos != _finishPos)
                    {
                        _secondsTaken += Time.deltaTime;
                        _waypointDone = Mathf.InverseLerp(0f, _secondsToTake, _secondsTaken);
                        _animal.transform.position = Vector3.Lerp(_startPos, _finishPos, _waypointDone);
                        if (!_animal.brain.Navigator.IsOverridingFacingDirection) _animal.brain.Navigator.SetFacingDirectionOverride((_finishPos - _startPos).normalized);
                    }
                    else _waypointDone = 1f;
                }
            }

            internal void SetDestination(Vector3 pos, float radius, BaseNavigator.NavigationSpeed speed)
            {
                if (CustomNavMesh.Count > 0 && NavMeshController != null)
                {
                    CalculatePath(pos);
                    NavMeshController.Speed = brain.Navigator.Speed * brain.Navigator.GetSpeedFraction(speed);
                }
                else
                {
                    Vector3 sample = GetSamplePosition(pos, radius);
                    sample.y += 2f;
                    if (!sample.IsEqualVector3(brain.Navigator.Destination)) brain.Navigator.SetDestination(sample, speed);
                }
            }

            internal Vector3 GetSamplePosition(Vector3 source, float radius)
            {
                NavMeshHit navMeshHit;
                int mask = NavAgent != null ? NavAgent.areaMask : NavMesh.AllAreas;
                if (NavMesh.SamplePosition(source, out navMeshHit, radius, mask))
                {
                    NavMeshPath path = new NavMeshPath();
                    if (NavMesh.CalculatePath(transform.position, navMeshHit.position, mask, path))
                    {
                        if (path.status == NavMeshPathStatus.PathComplete) return navMeshHit.position;
                        return path.corners.Last();
                    }
                }
                return source;
            }

            internal Vector3 GetRandomPos(Vector3 source, float radius)
            {
                Vector2 vector2 = UnityEngine.Random.insideUnitCircle * radius;
                return source + new Vector3(vector2.x, 0f, vector2.y);
            }

            internal bool IsPath(Vector3 start, Vector3 finish)
            {
                if (NavAgent == null) return false;
                NavMeshHit navMeshHit;
                if (NavMesh.SamplePosition(finish, out navMeshHit, AttackRange, NavAgent.areaMask))
                {
                    NavMeshPath path = new NavMeshPath();
                    if (NavMesh.CalculatePath(start, navMeshHit.position, NavAgent.areaMask, path))
                    {
                        if (path.status == NavMeshPathStatus.PathComplete) return Vector3.Distance(navMeshHit.position, finish) < AttackRange;
                        return Vector3.Distance(path.corners.Last(), finish) < AttackRange;
                    }
                    return false;
                }
                return false;
            }

            internal new bool IsMoving => NavMeshController != null ? NavMeshController.enabled : brain.Navigator.Moving;

            internal bool CanChaseState()
            {
                if (IsRunAwayWater) return false;
                if (DistanceFromBase > Config.ChaseRange) return false;
                if (AttackTarget == null) return false;
                return true;
            }

            internal bool CanCombatState()
            {
                if (IsRunAwayWater) return false;
                if (!AttackReady()) return false;
                if (AttackTarget == null) return false;
                if (DistanceToTarget > AttackRange) return false;
                if (!CanSeeTarget(AttackTarget)) return false;
                return true;
            }

            internal bool CanDestroyState()
            {
                if (AttackTarget != null && CanSeeTarget(AttackTarget) && IsPath(transform.position, AttackTarget.transform.position) && DistanceToTarget < DistanceFromBase) return false;
                return true;
            }

            private void UpdateSleep()
            {
                if (!Config.CanSleep) return;
                bool sleep = BaseEntity.Query.Server.PlayerGrid.Query(transform.position.x, transform.position.z, Config.SleepDistance, AIBrainSenses.playerQueryResults, x => x.IsPlayer() && !x.IsSleeping()) == 0;
                if (brain.sleeping == sleep) return;
                brain.sleeping = sleep;
                if (brain.sleeping) SetDestination(HomePosition, 2f, BaseNavigator.NavigationSpeed.Fast);
                else if (NavAgent != null) NavAgent.enabled = true;
            }
        }

        public class CustomAnimalBrain : AnimalBrain
        {
            private CustomAnimalNpc _animal;

            public override void AddStates()
            {
                if (_animal == null) _animal = GetEntity() as CustomAnimalNpc;
                states = new Dictionary<AIState, BasicAIState>();
                if (_animal.Config.States.Contains("IdleState")) AddState(new IdleState(_animal));
                if (_animal.Config.States.Contains("RoamState")) AddState(new RoamState(_animal));
                if (_animal.Config.States.Contains("ChaseState")) AddState(new ChaseState(_animal));
                if (_animal.Config.States.Contains("CombatState")) AddState(new CombatState(_animal));
                if (_animal.Config.States.Contains("DestroyState")) AddState(new DestroyState(_animal));
            }

            public override void InitializeAI()
            {
                if (_animal == null) _animal = GetEntity() as CustomAnimalNpc;
                _animal.HasBrain = true;
                Navigator = GetComponent<BaseNavigator>();
                Navigator.Speed = _animal.Config.Speed;
                InvokeRandomized(DoMovementTick, 1f, 0.1f, 0.01f);

                AttackRangeMultiplier = 1f;
                MemoryDuration = _animal.Config.MemoryDuration;
                SenseRange = _animal.Config.SenseRange;
                TargetLostRange = SenseRange * 2f;
                VisionCone = Vector3.Dot(Vector3.forward, Quaternion.Euler(0f, _animal.Config.VisionCone, 0f) * Vector3.forward);
                CheckVisionCone = _animal.Config.CheckVisionCone;
                CheckLOS = true;
                IgnoreNonVisionSneakers = true;
                MaxGroupSize = 0;
                ListenRange = _animal.Config.ListenRange;
                HostileTargetsOnly = _animal.Config.HostileTargetsOnly;
                IgnoreSafeZonePlayers = !HostileTargetsOnly;
                SenseTypes = EntityType.Player;
                if (_ins?._config != null && _ins._config.CanTargetNpc) SenseTypes |= EntityType.BasePlayerNPC;
                if (_ins?._config != null && _ins._config.CanTargetAnimal) SenseTypes |= EntityType.NPC;
                RefreshKnownLOS = false;
                IgnoreNonVisionMaxDistance = ListenRange / 3f;
                IgnoreSneakersMaxDistance = IgnoreNonVisionMaxDistance / 3f;
                Senses.Init(_animal, this, MemoryDuration, SenseRange, TargetLostRange, VisionCone, CheckVisionCone, CheckLOS, IgnoreNonVisionSneakers, ListenRange, HostileTargetsOnly, false, IgnoreSafeZonePlayers, SenseTypes, RefreshKnownLOS);

                ThinkMode = AIThinkMode.Interval;
                thinkRate = 0.25f;
                PathFinder = new BasePathFinder();
            }

            public override void Think(float delta)
            {
                if (_animal == null) return;
                lastThinkTime = Time.time;
                if (sleeping)
                {
                    if (_animal.NavAgent != null && _animal.NavAgent.enabled && _animal.DistanceFromBase < _animal.Config.RoamRange) _animal.NavAgent.enabled = false;
                    return;
                }
                if (!_animal.IsRunAwayWater)
                {
                    Senses.Update();
                    _animal.UpdateTarget();
                }
                CurrentState?.StateThink(delta, this, _animal);
                float single = 0f;
                BasicAIState newState = null;
                foreach (BasicAIState value in states.Values)
                {
                    if (value == null) continue;
                    float weight = value.GetWeight();
                    if (weight < single) continue;
                    single = weight;
                    newState = value;
                }
                if (newState != CurrentState)
                {
                    CurrentState?.StateLeave(this, _animal);
                    CurrentState = newState;
                    CurrentState?.StateEnter(this, _animal);
                }
            }

            public new class IdleState : BasicAIState
            {
                private readonly CustomAnimalNpc _animal;
                public IdleState(CustomAnimalNpc animal) : base(AIState.Idle) { _animal = animal; }
                public override float GetWeight() => 10f;
            }

            public new class RoamState : BasicAIState
            {
                private readonly CustomAnimalNpc _animal;
                public RoamState(CustomAnimalNpc animal) : base(AIState.Roam) { _animal = animal; }
                public override float GetWeight() => 20f;

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    if (_animal.DistanceFromBase > _animal.Config.RoamRange)
                        _animal.SetDestination(_animal.HomePosition, 2f, _animal.Config.RoamRange == 0f && _animal.DistanceFromBase < 10f ? BaseNavigator.NavigationSpeed.Slowest : BaseNavigator.NavigationSpeed.Fast);
                    else if (!_animal.IsMoving && _animal.Config.RoamRange > 2f)
                        _animal.SetDestination(_animal.GetRandomPos(_animal.HomePosition, _animal.Config.RoamRange - 2f), 2f, BaseNavigator.NavigationSpeed.Slowest);
                    return StateStatus.Running;
                }
            }

            public new class ChaseState : BasicAIState
            {
                private readonly CustomAnimalNpc _animal;
                public ChaseState(CustomAnimalNpc animal) : base(AIState.Chase) { _animal = animal; }
                public override float GetWeight() => _animal.CanChaseState() ? 30f : 0f;

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    if (_animal.AttackTarget == null) return StateStatus.Error;
                    _animal.SetDestination(_animal.AttackTarget.transform.position, 2f, BaseNavigator.NavigationSpeed.Fast);
                    return StateStatus.Running;
                }
            }

            public class CombatState : BasicAIState
            {
                private readonly CustomAnimalNpc _animal;
                public CombatState(CustomAnimalNpc animal) : base(AIState.Combat) { _animal = animal; }
                public override float GetWeight() => _animal.CanCombatState() ? 40f : 0f;
                public override void StateLeave(BaseAIBrain brain, BaseEntity entity) => brain.Navigator.ClearFacingDirectionOverride();

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    if (_animal.AttackTarget == null) return StateStatus.Error;
                    brain.Navigator.SetFacingDirectionEntity(_animal.AttackTarget);
                    _animal.nextAttackTime = Time.realtimeSinceStartup + _animal.AttackRate;
                    _animal.CombatTarget.Hurt(_animal.AttackDamage, _animal.AttackDamageType, _animal, true);
                    _animal.ClientRPC(RpcTarget.NetworkGroup("Attack"), _animal.AttackTarget.transform.position);
                    _animal.SetDestination(_animal.AttackTarget.transform.position, 2f, BaseNavigator.NavigationSpeed.Fast);
                    return StateStatus.Running;
                }
            }

            public class DestroyState : BasicAIState
            {
                private readonly CustomAnimalNpc _animal;
                public DestroyState(CustomAnimalNpc animal) : base(AIState.Cooldown) { _animal = animal; }
                public override float GetWeight() => _animal.CanDestroyState() ? 50f : 0f;
                public override void StateLeave(BaseAIBrain brain, BaseEntity entity) => brain.Navigator.ClearFacingDirectionOverride();

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    if (_animal.DistanceFromBase <= _animal.AttackRange)
                    {
                        brain.Navigator.SetFacingDirectionOverride((_animal.HomePosition - _animal.transform.position).normalized);
                        if (_animal.AttackReady())
                        {
                            _animal.nextAttackTime = Time.realtimeSinceStartup + _animal.AttackRate;
                            _animal.ClientRPC(RpcTarget.NetworkGroup("Attack"), _animal.HomePosition);
                        }
                    }
                    else if (!_animal.IsMoving) _animal.SetDestination(GetMovePos(), _animal.AttackRange / 2f, BaseNavigator.NavigationSpeed.Fast);
                    return StateStatus.Running;
                }

                private Vector3 GetMovePos()
                {
                    Vector3 normal3 = (_animal.transform.position - _animal.HomePosition).normalized;
                    Vector2 vector2 = new Vector2(normal3.x, normal3.z) * _animal.AttackRange / 2f;
                    return _animal.HomePosition + new Vector3(vector2.x, 0f, vector2.y);
                }
            }
        }
        #endregion Controller

        #region Harmony lifecycle

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            _ins = this;
            OxideCompat.EnsureDataFolders();
            LoadConfig();
            PublishApi();
            OxideCompat.RunWhenServerInitialized(() =>
            {
                if (Instance == null) return;
                OnServerInitialized();
            });
            Debug.Log("[AnimalSpawn] Loaded v" + Version + ". Config: HarmonyConfig/AnimalSpawn.json. Horse limits: Shop.");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            UnloadAnimals();
            UnpublishApi();
            Instance = null;
            _ins = null;
            Debug.Log("[AnimalSpawn] Unloaded");
        }

        private void PublishApi()
        {
            try
            {
                AppDomain.CurrentDomain.SetData(AppDomainTypeKey, typeof(AnimalSpawn));
                AppDomain.CurrentDomain.SetData(AppDomainInstanceKey, this);
                AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(AnimalSpawn));
                AppDomain.CurrentDomain.SetData(AppDomainDamageKey, (Func<BaseEntity, HitInfo, object>)CanEntityTakeDamageApi);
                AppDomain.CurrentDomain.SetData(AppDomainTargetKey, (Func<BaseEntity, BaseEntity, object>)CanEntityBeTargetedApi);
            }
            catch (Exception ex)
            {
                PrintWarning("PublishApi: " + ex.Message);
            }
        }

        private void UnpublishApi()
        {
            try
            {
                AppDomain.CurrentDomain.SetData(AppDomainTypeKey, null);
                AppDomain.CurrentDomain.SetData(AppDomainInstanceKey, null);
                AppDomain.CurrentDomain.SetData(AppDomainApiKey, null);
                AppDomain.CurrentDomain.SetData(AppDomainDamageKey, null);
                AppDomain.CurrentDomain.SetData(AppDomainTargetKey, null);
            }
            catch { }
        }

        private static object CanEntityTakeDamageApi(BaseEntity entity, HitInfo info)
        {
            return Instance?.CanEntityTakeDamage(entity as BaseCombatEntity, info);
        }

        private static object CanEntityBeTargetedApi(BaseEntity target, BaseEntity attacker)
        {
            if (Instance == null || !(target is CustomAnimalNpc victim)) return null;
            if (attacker is BaseAnimalNPC animal) return Instance.CanEntityBeTargeted(victim, animal);
            if (attacker is NPCPlayer npc) return Instance.CanEntityBeTargeted(victim, npc);
            return null;
        }

        private void OnServerInitialized()
        {
            OxideCompat.EnsureDataFolders();
            OxideCompat.MigrateOxideDataIfNeeded();
            LoadPresets();
            LoadNavMeshes();
        }

        private void UnloadAnimals()
        {
            List<CustomAnimalNpc> snapshot = new List<CustomAnimalNpc>(_animals.Values);
            _animals.Clear();
            foreach (CustomAnimalNpc animal in snapshot)
            {
                if (animal.IsExists()) animal.Kill();
            }
        }

        private void Puts(string message) => Debug.Log("[AnimalSpawn] " + message);
        private void PrintWarning(string message) => Debug.LogWarning("[AnimalSpawn] " + message);
        private void PrintError(string message) => Debug.LogError("[AnimalSpawn] " + message);
        #endregion Harmony lifecycle

        #region Hooks

        internal void OnEntityKill(CustomAnimalNpc animal)
        {
            if (animal != null && animal.net != null)
                _animals.Remove(animal.net.ID.Value);
        }

        internal object OnNpcTarget(BaseAnimalNPC attacker, CustomAnimalNpc victim)
        {
            if (attacker == null || !IsCustomAnimal(victim)) return null;
            if (_config != null && _config.CanTargetAnimal) return null;
            return true;
        }

        internal object OnNpcTarget(NPCPlayer attacker, CustomAnimalNpc victim)
        {
            if (attacker == null || !IsCustomAnimal(victim)) return null;
            if (_config != null && _config.CanTargetNpc) return null;
            return true;
        }

        internal object OnEntityTakeDamage(BaseCombatEntity victim, HitInfo info)
        {
            if (victim == null || info == null) return null;

            BaseEntity attacker = info.Initiator;

            if (IsCustomAnimal(victim))
            {
                if (attacker == null || attacker.skinID == CUSTOM_ANIMAL_SKIN_ID) return true;

                CustomAnimalNpc victimAnimal = victim as CustomAnimalNpc;

                if (attacker is AutoTurret || attacker is GunTrap || attacker is FlameTurret)
                {
                    if (victimAnimal?.Config != null)
                        info.damageTypes.ScaleAll(victimAnimal.Config.TurretDamageScale);
                    return null;
                }

                if (attacker is BasePlayer)
                {
                    BasePlayer attackerBasePlayer = attacker as BasePlayer;
                    if (attackerBasePlayer.userID.IsSteamId())
                    {
                        if (victimAnimal != null && victimAnimal.CanTargetPlayer(attackerBasePlayer))
                            victimAnimal.brain.Senses.Memory.SetKnown(attackerBasePlayer, victimAnimal, victimAnimal.brain.Senses);
                        return null;
                    }
                    if (attackerBasePlayer is NPCPlayer)
                    {
                        NPCPlayer attackerNpcPlayer = attackerBasePlayer as NPCPlayer;
                        if (victimAnimal != null && victimAnimal.CanTargetNpcPlayer(attackerNpcPlayer))
                        {
                            victimAnimal.brain.Senses.Memory.SetKnown(attackerNpcPlayer, victimAnimal, victimAnimal.brain.Senses);
                            return null;
                        }
                        return true;
                    }
                }

                if (attacker is BaseAnimalNPC)
                {
                    BaseAnimalNPC attackerAnimalNpc = attacker as BaseAnimalNPC;
                    if (victimAnimal != null && victimAnimal.CanTargetAnimal(attackerAnimalNpc))
                    {
                        victimAnimal.brain.Senses.Memory.SetKnown(attackerAnimalNpc, victimAnimal, victimAnimal.brain.Senses);
                        return null;
                    }
                    return true;
                }

                return true;
            }

            if (IsCustomAnimal(attacker))
            {
                if (victim.skinID == CUSTOM_ANIMAL_SKIN_ID) return true;

                CustomAnimalNpc attackerAnimal = attacker as CustomAnimalNpc;

                if (victim is BasePlayer)
                {
                    BasePlayer victimBasePlayer = victim as BasePlayer;
                    if (victimBasePlayer.userID.IsSteamId()) return null;
                    if (victimBasePlayer is NPCPlayer)
                    {
                        NPCPlayer victimNpcPlayer = victimBasePlayer as NPCPlayer;
                        if (attackerAnimal != null && attackerAnimal.CanTargetNpcPlayer(victimNpcPlayer)) return null;
                        return true;
                    }
                }

                if (victim is BaseAnimalNPC)
                {
                    BaseAnimalNPC victimAnimalNpc = victim as BaseAnimalNPC;
                    if (attackerAnimal != null && attackerAnimal.CanTargetAnimal(victimAnimalNpc)) return null;
                    return true;
                }

                return true;
            }

            return null;
        }

        internal object CanEntityBeTargeted(CustomAnimalNpc victim, BaseAnimalNPC attacker)
        {
            if (attacker == null || !IsCustomAnimal(victim)) return null;
            return _config != null && _config.CanTargetAnimal;
        }

        internal object CanEntityBeTargeted(CustomAnimalNpc victim, NPCPlayer attacker)
        {
            if (attacker == null || !IsCustomAnimal(victim)) return null;
            return _config != null && _config.CanTargetNpc;
        }

        internal object CanEntityTakeDamage(BaseCombatEntity victim, HitInfo info)
        {
            if (victim == null || info == null) return null;

            BaseEntity attacker = info.Initiator;

            if (IsCustomAnimal(victim))
            {
                if (attacker == null || attacker.skinID == CUSTOM_ANIMAL_SKIN_ID) return false;

                CustomAnimalNpc victimAnimal = victim as CustomAnimalNpc;

                if (attacker is AutoTurret || attacker is GunTrap || attacker is FlameTurret) return null;

                if (attacker is BasePlayer)
                {
                    BasePlayer attackerBasePlayer = attacker as BasePlayer;
                    if (attackerBasePlayer.userID.IsSteamId()) return null;
                    if (attackerBasePlayer is NPCPlayer)
                    {
                        NPCPlayer attackerNpcPlayer = attackerBasePlayer as NPCPlayer;
                        if (victimAnimal != null && victimAnimal.CanTargetNpcPlayer(attackerNpcPlayer)) return null;
                        return false;
                    }
                }

                if (attacker is BaseAnimalNPC)
                {
                    BaseAnimalNPC attackerAnimalNpc = attacker as BaseAnimalNPC;
                    if (victimAnimal != null && victimAnimal.CanTargetAnimal(attackerAnimalNpc)) return null;
                    return false;
                }

                return false;
            }

            if (IsCustomAnimal(attacker))
            {
                if (victim.skinID == CUSTOM_ANIMAL_SKIN_ID) return false;

                CustomAnimalNpc attackerAnimal = attacker as CustomAnimalNpc;

                if (victim is BasePlayer)
                {
                    BasePlayer victimBasePlayer = victim as BasePlayer;
                    if (victimBasePlayer.userID.IsSteamId()) return null;
                    if (victimBasePlayer is NPCPlayer)
                    {
                        NPCPlayer victimNpcPlayer = victimBasePlayer as NPCPlayer;
                        if (attackerAnimal != null && attackerAnimal.CanTargetNpcPlayer(victimNpcPlayer)) return null;
                        return false;
                    }
                }

                if (victim is BaseAnimalNPC)
                {
                    BaseAnimalNPC victimAnimalNpc = victim as BaseAnimalNPC;
                    if (attackerAnimal != null && attackerAnimal.CanTargetAnimal(victimAnimalNpc)) return null;
                    return false;
                }

                return false;
            }

            return null;
        }
        #endregion Hooks

        #region Data

        private void LoadPresets()
        {
            _presets.Clear();
            LoadPresetDir(OxideCompat.DataDirectory);
            LoadPresetDir(OxideCompat.PresetDirectory);
            Puts($"Loaded {_presets.Count} animal preset(s)");
        }

        private void LoadPresetDir(string dir)
        {
            foreach (string path in OxideCompat.EnumerateJsonFiles(dir))
            {
                string fileName = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrEmpty(fileName)) continue;
                AnimalConfig config = OxideCompat.ReadJson<AnimalConfig>(path);
                if (config != null && !string.IsNullOrEmpty(config.Prefab))
                {
                    if (!_presets.ContainsKey(fileName))
                        _presets.Add(fileName, config);
                }
                else
                    PrintError($"File {fileName} is corrupted and cannot be loaded!");
            }
        }

        public class PointNavMeshFile { public string Position; public bool Enabled; public bool Border; }
        public class PointNavMesh { public Vector3 Position; public bool Enabled; }

        private void LoadNavMeshes()
        {
            _allNavMeshes.Clear();
            Puts("Loading custom navigation mesh files...");
            foreach (string path in OxideCompat.EnumerateJsonFiles(OxideCompat.NavMeshDirectory))
            {
                string fileName = Path.GetFileNameWithoutExtension(path);
                Dictionary<int, Dictionary<int, PointNavMeshFile>> navMesh =
                    OxideCompat.ReadJson<Dictionary<int, Dictionary<int, PointNavMeshFile>>>(path);
                if (navMesh == null || navMesh.Count == 0)
                    PrintError($"File {fileName} is corrupted and cannot be loaded!");
                else
                {
                    _allNavMeshes[fileName] = navMesh;
                    Puts($"File {fileName} has been loaded successfully!");
                }
            }
            Puts("All custom navigation mesh files have loaded successfully!");
        }
        #endregion Data
    }
}
