using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Facepunch;
using Rust;
using UnityEngine;
using Random = UnityEngine.Random;

namespace ZombieHorde
{
    /// <summary>
    /// Port of Oxide RaidingZombies 3.2.1 — C4/rocket raiders attached to ZombieHorde leaders.
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
        private static Vector3 _vector3Down;
        private static int _totalZombies;

        private static ConfigData.RaidingZombiesOptions Settings =>
            ConfigData.Configuration?.Raiding ?? new ConfigData.RaidingZombiesOptions();

        public static void Init()
        {
            InitializeRocketTypes();
            _vector3Down = new Vector3(0f, -1f, 0f);
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
                Compat.PrintWarning($"Added a total of {_totalZombies} Groups of zombie raiders");
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
                // BuildingBlock path also handled for TruePVE CanEntityTakeDamage parity
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

        #region Raid AI state

        private class RaidCooldownState : BaseAIBrain.BasicAIState
        {
            public ZombieNPC zombieNpc;
            private float nextThrowTime = Time.time;
            private float nextPositionUpdateTime;
            private bool isThrowingWeapon;
            public int totalTossed;
            public int totalBoom;
            public ThrownWeapon _throwableWeapon;
            public BaseProjectile _projectileWeapon;
            public BaseCombatEntity targetEntity;
            private bool isSetup;
            private float nextRaidFire;
            public bool canLeave;

            public RaidCooldownState(ZombieNPC zombieNpc) : base(AIState.Cooldown)
            {
                if (DebugLog) Compat.PrintWarning("Added cooldown State");
                this.zombieNpc = zombieNpc;
                if (!isSetup) Setup();
            }

            public bool SetTarget(BaseCombatEntity newTarget)
            {
                targetEntity = newTarget;
                return true;
            }

            public void Setup()
            {
                isSetup = true;
                totalBoom = Settings.TotalExplosivesToUse <= 0 ? 5 : Settings.TotalExplosivesToUse;
            }

            internal void TryThrowBoom(bool rocket = false)
            {
                if (isThrowingWeapon) return;
                nextRaidFire = Time.time + Random.Range(10f, 15f);
                isThrowingWeapon = true;
                zombieNpc.StartCoroutine(ThrowWeaponBoom(rocket));
            }

            private IEnumerator ThrowWeaponBoom(bool rocket)
            {
                EquipThrowable(rocket);
                yield return CoroutineEx.waitForSeconds(1.5f);

                if (targetEntity != null && !targetEntity.IsDestroyed)
                {
                    Vector3 origin = zombieNpc.eyes != null ? zombieNpc.eyes.position : zombieNpc.transform.position;
                    bool hasLos = targetEntity.IsVisible(origin, targetEntity.CenterPoint());
                    if (!hasLos && !rocket)
                    {
                        isThrowingWeapon = false;
                        yield break;
                    }

                    zombieNpc.SetAimDirection((targetEntity.transform.position - zombieNpc.transform.position).normalized);
                    yield return CoroutineEx.waitForSeconds(0.1f);

                    if (!rocket && _throwableWeapon != null)
                    {
                        if (targetEntity != null && !targetEntity.IsDestroyed)
                            ServerThrow(targetEntity.transform.position);
                    }
                    else if (rocket)
                    {
                        yield return CoroutineEx.waitForSeconds(0.6f);
                        if (targetEntity != null && !targetEntity.IsDestroyed)
                            zombieNpc.SetAimDirection((targetEntity.transform.position - zombieNpc.transform.position).normalized);
                        yield return CoroutineEx.waitForSeconds(0.5f);
                        FireRocket();
                        yield return CoroutineEx.waitForSeconds(2.0f);
                    }

                    totalBoom--;
                    nextThrowTime = Time.time + 6f;
                }

                yield return CoroutineEx.waitForSeconds(1f);
                if (zombieNpc != null && !zombieNpc.IsDestroyed)
                    zombieNpc.EquipWeapon();
                RemoveWeapons();
                yield return CoroutineEx.waitForSeconds(0.1f);

                totalTossed++;
                isThrowingWeapon = false;
            }

            private void EquipThrowable(bool rocket = false)
            {
                if (targetEntity == null || targetEntity.IsDestroyed || targetEntity.IsDead()) return;
                if (zombieNpc?.inventory?.containerBelt == null) return;

                var belt = zombieNpc.inventory.containerBelt;
                int slotIndex = Mathf.Min(5, Mathf.Max(0, belt.capacity - 1));
                Item items = belt.GetSlot(slotIndex);
                if (items != null) zombieNpc.inventory.Take(null, items.info.itemid, items.amount);

                if (!rocket)
                {
                    if (Settings.ThrowExplosiveItemTypes == null || Settings.ThrowExplosiveItemTypes.Count == 0) return;
                    Item itemC4 = ItemManager.CreateByName(Settings.ThrowExplosiveItemTypes.GetRandom(), 1);
                    if (itemC4 == null) return;
                    itemC4.MoveToContainer(belt, slotIndex);
                    _throwableWeapon = itemC4.GetHeldEntity() as ThrownWeapon;
                    Item slotItem = belt.GetSlot(slotIndex);
                    if (slotItem == null) return;
                    zombieNpc.UpdateActiveItem(slotItem.uid);
                    if (_throwableWeapon == null) return;
                    _throwableWeapon.SetHeld(true);
                    zombieNpc.inventory.UpdatedVisibleHolsteredItems();
                    zombieNpc.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                }
                else
                {
                    Item launcher = ItemManager.CreateByName("rocket.launcher", 1);
                    if (launcher == null) return;
                    launcher.MoveToContainer(belt, slotIndex);
                    Item slotItem = belt.GetSlot(slotIndex);
                    if (slotItem == null) return;
                    zombieNpc.UpdateActiveItem(slotItem.uid);
                    _projectileWeapon = launcher.GetHeldEntity() as BaseProjectile;
                    if (_projectileWeapon == null) return;
                    _projectileWeapon.SetHeld(true);
                    zombieNpc.inventory.UpdatedVisibleHolsteredItems();
                    zombieNpc.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                }
            }

            public void ServerThrow(Vector3 targetPosition)
            {
                ThrownWeapon wep = _throwableWeapon;
                if (wep == null) return;
                BasePlayer ownerPlayer = wep.GetOwnerPlayer();
                if (ownerPlayer == null) return;

                Vector3 position = ownerPlayer.eyes.position;
                Vector3 vector3 = ownerPlayer.eyes.BodyForward();
                wep.SignalBroadcast(BaseEntity.Signal.Throw, string.Empty);
                BaseEntity entity = GameManager.server.CreateEntity(
                    wep.prefabToThrow.resourcePath,
                    position,
                    Quaternion.LookRotation(wep.overrideAngle == Vector3.zero ? -vector3 : wep.overrideAngle));
                if (entity == null) return;

                entity.creatorEntity = ownerPlayer;
                Vector3 aimDir = vector3 + Quaternion.AngleAxis(10f, Vector3.right) * Vector3.up;
                float f = 5f;
                entity.SetVelocity(aimDir * f);
                if (wep.tumbleVelocity > 0.0)
                    entity.SetAngularVelocity(new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * wep.tumbleVelocity);

                DudTimedExplosive dud = entity.GetComponent<DudTimedExplosive>();
                if (dud != null) dud.dudChance = 0f;
                entity.Spawn();
                wep.StartAttackCooldown(wep.repeatDelay);
            }

            private void FireRocket()
            {
                string type = GetRocketPrefabPath();
                Vector3 origin = zombieNpc.IsMounted()
                    ? zombieNpc.eyes.position + new Vector3(0f, 0.5f, 0f)
                    : zombieNpc.eyes.position;

                Vector3 direction = AimConeUtil.GetModifiedAimConeDirection(2.25f, zombieNpc.eyes.BodyForward());
                float distance = 1f;
                if (Physics.Raycast(origin, direction, out RaycastHit raycastHit, distance, 1236478737))
                    distance = raycastHit.distance - 0.1f;

                BaseEntity rocket = GameManager.server.CreateEntity(type, origin + direction * distance, Quaternion.LookRotation(direction));
                if (rocket == null) return;
                ServerProjectile proj = rocket.GetComponent<ServerProjectile>();
                if (proj == null) return;
                rocket.creatorEntity = zombieNpc.Npc;
                proj.InitializeVelocity(zombieNpc.GetInheritedProjectileVelocity(direction) + direction * proj.speed * 2f);
                rocket.Spawn();
            }

            private void RemoveWeapons()
            {
                if (zombieNpc == null || zombieNpc.IsDestroyed) return;
                var belt = zombieNpc.inventory?.containerBelt;
                if (belt == null) return;
                for (int i = 0; i < belt.capacity; i++)
                {
                    Item item = belt.GetSlot(i);
                    if (item == null) continue;
                    BaseEntity held = item.GetHeldEntity();
                    if (held is ThrownWeapon || held is BaseProjectile)
                        zombieNpc.inventory.Take(null, item.info.itemid, item.amount);
                }
            }

            internal bool TargetInThrowableRange() => HasWall();

            internal bool HasWall()
            {
                if (targetEntity == null || targetEntity.IsDestroyed) return false;
                Vector3 origin = zombieNpc.eyes != null ? zombieNpc.eyes.position : zombieNpc.transform.position;
                Vector3 dir = (targetEntity.transform.position - origin).normalized;
                return Physics.Raycast(origin, dir, out _, 8f, _targetLayer);
            }

            public override bool CanInterrupt()
            {
                if (canLeave) return true;
                if (targetEntity == null) return true;
                return false;
            }

            public override void StateEnter(BaseAIBrain brain, BaseEntity entity)
            {
                zombieNpc.SetPlayerFlag(BasePlayer.PlayerFlags.Relaxed, false);
                nextRaidFire = Time.time + 2f;
                ulong id = zombieNpc.netId.Value;
                if (id != 0) RaidingZombieIds.Add(id);
                base.StateEnter(brain, entity);
            }

            public override void StateLeave(BaseAIBrain brain, BaseEntity entity)
            {
                ulong id = zombieNpc.netId.Value;
                if (id != 0) RaidingZombieIds.Remove(id);
                base.StateLeave(brain, entity);
            }

            public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
            {
                base.StateThink(delta, brain, entity);

                if (zombieNpc.IsDormant)
                    zombieNpc.IsDormant = false;

                if (targetEntity == null)
                    return StateStatus.Error;

                if (totalBoom <= 0 && nextPositionUpdateTime < Time.time)
                {
                    targetEntity = null;
                    canLeave = true;
                    zombieNpc.ResetRoamState();
                    try { brain.states?.Remove(AIState.Cooldown); } catch { }
                    return StateStatus.Error;
                }

                if (totalBoom > 0 && !isThrowingWeapon && nextRaidFire < Time.time && Time.time >= nextThrowTime)
                {
                    nextPositionUpdateTime = Time.time + 2f;
                    brain.Navigator.SetDestination(zombieNpc.transform.position, BaseNavigator.NavigationSpeed.Slow, 0.0f, 0f);
                    brain.Navigator.Stop();
                    zombieNpc.SetAimDirection((targetEntity.transform.position - zombieNpc.transform.position).normalized);

                    bool canThrow = Settings.ThrowExplosiveItemTypes != null && Settings.ThrowExplosiveItemTypes.Count > 0;
                    bool canRocket = Settings.RocketPrefabTypes != null && Settings.RocketPrefabTypes.Count > 0;
                    bool inThrowRangeHasWall = TargetInThrowableRange();

                    if (canThrow && inThrowRangeHasWall)
                        TryThrowBoom(false);
                    else if (canRocket)
                        TryThrowBoom(true);
                    else
                        Compat.PrintWarning("No valid explosive types configured for raiders.");
                }
                else if (Time.time > nextPositionUpdateTime)
                {
                    Vector3 position = brain.PathFinder.GetRandomPositionAround(targetEntity.transform.position, 2f, 12f);
                    brain.Navigator.SetDestination(position, BaseNavigator.NavigationSpeed.Fast, 0.1f, 0f);
                    nextPositionUpdateTime = Time.time + 1.5f;
                    return StateStatus.Running;
                }

                return StateStatus.Running;
            }
        }

        #endregion

        #region Raid trigger

        public class RaidTrigger : MonoBehaviour
        {
            private ZombieNPC zombieNpc;
            private Dictionary<ZombieNPC, RaidCooldownState> classFind = new Dictionary<ZombieNPC, RaidCooldownState>();
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
                    if (DebugLog) Compat.PrintWarning("Setting leader of Zombies");
                    int total = Settings.TotalPerHorde;
                    RaidCooldownState theClass = new RaidCooldownState(zombieNpc);
                    zombieNpc.Brain?.AddState(theClass);
                    classFind[zombieNpc] = theClass;

                    if (zombieNpc.Horde?.members != null)
                    {
                        foreach (ZombieNPC member in zombieNpc.Horde.members.ToList())
                        {
                            if (member != null && !member.IsDestroyed && !classFind.ContainsKey(member) && !member.IsGroupLeader)
                            {
                                theClass = new RaidCooldownState(member);
                                member.Brain?.AddState(theClass);
                                classFind[member] = theClass;
                                total--;
                            }
                            if (total <= 1) break;
                        }
                    }
                }
                else
                {
                    if (DebugLog) Compat.PrintWarning("Setting new leader of Zombies already spawned");
                    foreach (var member in classFind.ToList())
                    {
                        if (member.Key != null && !member.Key.IsDestroyed && member.Key != zombieNpc)
                        {
                            RaidTrigger newLeader = member.Key.gameObject.AddComponent<RaidTrigger>();
                            classFind.Remove(zombieNpc);
                            newLeader.classFind = classFind;
                            break;
                        }
                    }
                }
            }

            private void OnDestroy()
            {
                CancelInvoke(nameof(UpdateTriggerArea));
                FindNewLeader(false);
            }

            private void UpdateTriggerArea()
            {
                if (classFind.Count <= 0)
                {
                    FindNewLeader(true);
                    return;
                }
                if (zombieNpc == null || zombieNpc.IsDestroyed)
                {
                    Destroy(this);
                    return;
                }

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
                foreach (var raider in classFind.ToList())
                {
                    if (raider.Key != null && !raider.Key.IsDestroyed && raider.Value != null)
                    {
                        raider.Value.SetTarget(priv);
                        try { raider.Key.Brain?.SwitchToState(AIState.Cooldown, 0); } catch { }
                    }
                    else
                    {
                        classFind.Remove(raider.Key);
                    }
                }
            }
        }

        #endregion
    }
}
