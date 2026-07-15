using System.Collections.Generic;
using System.Linq;
using Facepunch;
using Rust;
using UnityEngine;
using Random = UnityEngine.Random;

namespace ZombieHorde
{
    /// <summary>
    /// Port of Oxide RaidingZombies 3.2.1 — picks raid-capable hordes and feeds TC targets into GrimmNPC.
    /// Raid AI (C4/rockets/melee) is GrimmNPC RaidState / RaidStateMelee (AIState.Cooldown).
    /// Do not AddState(Cooldown) — that duplicates GrimmNPC and spams "Trying to add duplicate state: Cooldown".
    /// Config: HarmonyConfig/ZombieHorde.json → "Raiding Zombies Options".
    /// </summary>
    public static class RaidingZombies
    {
        public static bool DebugLog;
        public static bool IsInit { get; private set; }

        public static readonly HashSet<ulong> RaidingZombieIds = new HashSet<ulong>();
        private static readonly HashSet<ulong> TurretRetaliatedZombies = new HashSet<ulong>();
        private static readonly Dictionary<string, string> RocketTypes = new Dictionary<string, string>();

        private static Collider[] _colBuffer;
        private static int _targetLayer;
        private static int _totalZombies;

        private static ConfigData.RaidingZombiesOptions Settings =>
            ConfigData.Configuration?.Raiding ?? new ConfigData.RaidingZombiesOptions();

        public static void Init()
        {
            InitializeRocketTypes();
            _colBuffer = new Collider[8192];
            _targetLayer = LayerMask.GetMask("Deployed", "Construction");
            _totalZombies = 0;

            SanitizeConfig();

            Compat.Timer.Once(60f, () =>
            {
                for (int i = Horde.AllHordes.Count - 1; i >= 0; i--)
                {
                    Horde g = Horde.AllHordes[i];
                    if (g?.Leader == null || g.Leader.IsDestroyed) continue;
                    if (Random.Range(0, 100) <= Settings.Chance)
                    {
                        GetOrAddComponent<RaidTrigger>(g.Leader.gameObject);
                        _totalZombies++;
                        if (DebugLog) Compat.PrintWarning("Added Raid Leader");
                    }
                }

                SanitizeConfig();
                IsInit = true;
                Compat.PrintWarning($"Added a total of {_totalZombies} Groups of zombie raiders (GrimmNPC raid AI)");
            });
        }

        public static void Shutdown()
        {
            IsInit = false;
            RaidingZombieIds.Clear();
            TurretRetaliatedZombies.Clear();
        }

        /// <summary>Called after a zombie is spawned/attached (replaces Oxide OnEntitySpawned).</summary>
        public static void OnZombieSpawned(ZombieNPC npc)
        {
            if (!IsInit || npc == null || npc.IsDestroyed || !npc.IsGroupLeader) return;

            Compat.Timer.Once(5f, () =>
            {
                if (npc == null || npc.IsDestroyed || !npc.IsGroupLeader) return;
                if (Random.Range(0, 100) <= Settings.Chance)
                {
                    GetOrAddComponent<RaidTrigger>(npc.gameObject);
                    if (DebugLog) Compat.PrintWarning("Added Raid Leader OnZombieSpawned");
                }
            });
        }

        public static void OnEntityTakeDamage(BaseCombatEntity baseCombatEntity, HitInfo hitInfo)
        {
            if (hitInfo == null || baseCombatEntity == null) return;

            ZombieNPC zombieVictim = ZombieNPC.Get(baseCombatEntity as BasePlayer);
            if (zombieVictim != null && hitInfo.Initiator is AutoTurret turret && !(turret is NPCAutoTurret))
            {
                TryFireTurretRetaliationRocket(zombieVictim, turret);
                return;
            }

            if (Settings.DamageScale <= 0f) return;

            ZombieNPC hordeMember = ZombieNPC.Get(hitInfo.InitiatorPlayer);
            if (hordeMember == null) return;

            if (hitInfo.damageTypes.Get(DamageType.Explosion) > 0)
            {
                if (baseCombatEntity is BuildingBlock
                    && hitInfo.WeaponPrefab != null
                    && !hitInfo.WeaponPrefab.ShortPrefabName.Contains("beancan"))
                {
                    hitInfo.damageTypes.ScaleAll(Settings.DamageScale);
                    return;
                }

                hitInfo.damageTypes.ScaleAll(Settings.DamageScale);
            }
        }

        private static void SanitizeConfig()
        {
            var s = ConfigData.Configuration?.Raiding;
            if (s == null)
            {
                if (ConfigData.Configuration != null)
                    ConfigData.Configuration.Raiding = new ConfigData.RaidingZombiesOptions();
                return;
            }

            if (s.Chance < 0) s.Chance = 0;
            if (s.Chance > 100) s.Chance = 100;
            if (s.TotalPerHorde < 1) s.TotalPerHorde = 1;
            if (s.DamageScale < 0f) s.DamageScale = 0f;

            InitializeRocketTypes();
            if (s.RocketPrefabTypes != null)
            {
                s.RocketPrefabTypes = s.RocketPrefabTypes
                    .Where(k => RocketTypes.ContainsKey(k))
                    .Distinct()
                    .ToList();
            }

            if (s.ThrowExplosiveItemTypes != null)
            {
                s.ThrowExplosiveItemTypes = s.ThrowExplosiveItemTypes
                    .Where(shortName => ItemManager.FindItemDefinition(shortName) != null)
                    .Distinct()
                    .ToList();
            }
        }

        private static void InitializeRocketTypes()
        {
            if (RocketTypes.Count > 0) return;
            RocketTypes["rocket_basic"] = "assets/prefabs/ammo/rocket/rocket_basic.prefab";
            RocketTypes["rocket_fire"] = "assets/prefabs/ammo/rocket/rocket_fire.prefab";
            RocketTypes["rocket_heli"] = "assets/prefabs/npc/patrol helicopter/rocket_heli.prefab";
            RocketTypes["rocket_heli_airburst"] = "assets/prefabs/npc/patrol helicopter/rocket_heli_airburst.prefab";
            RocketTypes["rocket_heli_napalm"] = "assets/prefabs/npc/patrol helicopter/rocket_heli_napalm.prefab";
            RocketTypes["rocket_hv"] = "assets/prefabs/ammo/rocket/rocket_hv.prefab";
            RocketTypes["rocket_sam"] = "assets/prefabs/npc/sam_site_turret/rocket_sam.prefab";
            RocketTypes["rocket_smoke"] = "assets/prefabs/ammo/rocket/rocket_smoke.prefab";
            RocketTypes["rocket_mlrs"] = "assets/content/vehicles/mlrs/rocket_mlrs.prefab";
        }

        private static string GetRocketPrefabPath()
        {
            InitializeRocketTypes();
            List<string> configured = Settings.RocketPrefabTypes;
            if (configured != null && configured.Count > 0)
            {
                for (int i = 0; i < configured.Count; i++)
                {
                    string key = configured.GetRandom();
                    if (RocketTypes.TryGetValue(key, out string prefabPath))
                        return prefabPath;
                }
            }
            return RocketTypes["rocket_basic"];
        }

        private static bool TryFireTurretRetaliationRocket(ZombieNPC zombieNpc, BaseCombatEntity turret)
        {
            if (zombieNpc == null || zombieNpc.IsDestroyed || !zombieNpc.IsAlive() || turret == null || turret.IsDestroyed || turret.IsDead())
                return false;

            ulong id = zombieNpc.netId.Value;
            if (id == 0 || !TurretRetaliatedZombies.Add(id))
                return false;

            if (zombieNpc.Horde != null && zombieNpc.Horde.IsSleeping)
                zombieNpc.Horde.ForceWakeFromSleep();

            string prefabPath = GetRocketPrefabPath();
            if (string.IsNullOrEmpty(prefabPath)) return false;

            Vector3 origin = zombieNpc.eyes != null ? zombieNpc.eyes.position : zombieNpc.transform.position + Vector3.up * 1.5f;
            Vector3 direction = turret.CenterPoint() - origin;
            if (direction.sqrMagnitude < 0.01f) return false;
            direction.Normalize();
            zombieNpc.SetAimDirection(direction);

            float spawnDistance = 1f;
            if (Physics.Raycast(origin, direction, out RaycastHit raycastHit, spawnDistance, 1236478737))
                spawnDistance = Mathf.Max(0.1f, raycastHit.distance - 0.1f);

            BaseEntity rocket = GameManager.server.CreateEntity(prefabPath, origin + direction * spawnDistance, Quaternion.LookRotation(direction));
            if (rocket == null) return false;

            ServerProjectile projectile = rocket.GetComponent<ServerProjectile>();
            if (projectile == null)
            {
                rocket.Kill();
                return false;
            }

            rocket.creatorEntity = zombieNpc.Npc;
            projectile.InitializeVelocity(zombieNpc.GetInheritedProjectileVelocity(direction) + direction * projectile.speed * 2f);
            rocket.Spawn();
            return true;
        }

        private static T GetOrAddComponent<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            if (c == null) c = go.AddComponent<T>();
            return c;
        }

        #region Raid trigger

        /// <summary>
        /// Scans for TCs and assigns raid targets via GrimmNpcBridge (GrimmNPC Foundations / Cooldown).
        /// </summary>
        public class RaidTrigger : MonoBehaviour
        {
            private ZombieNPC zombieNpc;
            private readonly HashSet<ZombieNPC> raiders = new HashSet<ZombieNPC>();
            private readonly HashSet<BuildingPrivlidge> triggerEntitys = new HashSet<BuildingPrivlidge>();
            private readonly Dictionary<BasePlayer, float> targetPlayers = new Dictionary<BasePlayer, float>();
            public float collisionRadius;

            private void Awake()
            {
                zombieNpc = GetComponent<ZombieNPC>();
                collisionRadius = Settings.BaseScanDistance;
                InvokeRepeating(nameof(UpdateTriggerArea), Random.Range(5, 9), Random.Range(10, 15));
            }

            private void FindNewLeader(bool newLeaderEntity = true)
            {
                if (newLeaderEntity)
                {
                    if (DebugLog) Compat.PrintWarning("Setting leader of Zombies (GrimmNPC raid)");
                    int total = Settings.TotalPerHorde;
                    TryRegisterRaider(zombieNpc);
                    total--;

                    if (zombieNpc.Horde?.members != null)
                    {
                        foreach (ZombieNPC member in zombieNpc.Horde.members.ToList())
                        {
                            if (member == null || member.IsDestroyed || member.IsGroupLeader) continue;
                            if (raiders.Contains(member)) continue;
                            if (!GrimmNpcBridge.HasGrimmRaidState(member.Npc)) continue;

                            TryRegisterRaider(member);
                            total--;
                            if (total <= 0) break;
                        }
                    }
                }
                else
                {
                    if (DebugLog) Compat.PrintWarning("Setting new leader of Zombies already spawned");
                    foreach (ZombieNPC member in raiders.ToList())
                    {
                        if (member != null && !member.IsDestroyed && member != zombieNpc)
                        {
                            RaidTrigger newLeader = member.gameObject.AddComponent<RaidTrigger>();
                            raiders.Remove(zombieNpc);
                            foreach (ZombieNPC r in raiders)
                                newLeader.raiders.Add(r);
                            break;
                        }
                    }
                }
            }

            private void TryRegisterRaider(ZombieNPC member)
            {
                if (member == null || member.IsDestroyed || member.Npc == null) return;
                if (!GrimmNpcBridge.HasGrimmRaidState(member.Npc))
                {
                    if (DebugLog)
                        Compat.PrintWarning($"Skip raider {member.Npc.displayName}: no GrimmNPC Cooldown/RaidState");
                    return;
                }

                raiders.Add(member);
                ulong id = member.netId.Value;
                if (id != 0) RaidingZombieIds.Add(id);
            }

            private void OnDestroy()
            {
                CancelInvoke(nameof(UpdateTriggerArea));
                foreach (ZombieNPC member in raiders)
                {
                    if (member == null) continue;
                    ulong id = member.netId.Value;
                    if (id != 0) RaidingZombieIds.Remove(id);
                }
                FindNewLeader(false);
            }

            private void UpdateTriggerArea()
            {
                if (raiders.Count <= 0)
                {
                    FindNewLeader(true);
                    return;
                }
                if (zombieNpc == null || zombieNpc.IsDestroyed)
                {
                    Destroy(this);
                    return;
                }

                // Drop dead / destroyed members
                raiders.RemoveWhere(m => m == null || m.IsDestroyed);

                if (zombieNpc.CurrentTarget is BasePlayer current)
                {
                    if (!current.IsConnected)
                    {
                        targetPlayers.Remove(current);
                    }
                    else if (targetPlayers.ContainsKey(current))
                    {
                        if (Time.time > targetPlayers[current] || current.IsSleeping())
                            targetPlayers.Remove(current);
                    }
                    else if (!current.IsSleeping())
                    {
                        targetPlayers[current] = Time.time + Settings.ForgetTargetTime;
                    }
                }

                if (Settings.TargetPlayerOnly && targetPlayers.Count <= 0) return;

                int count = Physics.OverlapSphereNonAlloc(transform.position, collisionRadius, _colBuffer, _targetLayer);
                var collidePriv = new HashSet<BuildingPrivlidge>();
                for (int i = 0; i < count; i++)
                {
                    Collider collider = _colBuffer[i];
                    _colBuffer[i] = null;
                    BuildingPrivlidge priv = collider?.GetComponentInParent<BuildingPrivlidge>();
                    if (priv != null)
                    {
                        collidePriv.Add(priv);
                        if (triggerEntitys.Add(priv)) OnEnterCollision(priv);
                    }
                }

                var removePriv = new HashSet<BuildingPrivlidge>();
                foreach (BuildingPrivlidge priv in triggerEntitys)
                    if (!collidePriv.Contains(priv)) removePriv.Add(priv);
                foreach (BuildingPrivlidge priv in removePriv)
                    triggerEntitys.Remove(priv);
            }

            private void OnEnterCollision(BuildingPrivlidge priv)
            {
                if (Settings.TargetPlayerOnly)
                {
                    foreach (var player in targetPlayers)
                    {
                        if (player.Key == null || !player.Key.IsConnected || priv == null || !priv.IsAuthed(player.Key))
                            continue;

                        AssignRaidTarget(priv);
                        break;
                    }
                }
                else
                {
                    AssignRaidTarget(priv);
                }
            }

            private void AssignRaidTarget(BuildingPrivlidge priv)
            {
                foreach (ZombieNPC raider in raiders.ToList())
                {
                    if (raider == null || raider.IsDestroyed || raider.Npc == null)
                    {
                        raiders.Remove(raider);
                        continue;
                    }

                    GrimmNpcBridge.AssignRaidTarget(raider.Npc, priv);
                }
            }
        }

        #endregion
    }
}
