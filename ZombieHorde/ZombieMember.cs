using System;
using System.Collections.Generic;
using System.Reflection;
using Facepunch;
using Rust;
using UnityEngine;

namespace ZombieHorde
{
    /// <summary>
    /// MonoBehaviour wrapper around GrimmNPC-spawned ScientistNPC.
    /// Keeps horde member APIs (Horde, Loadout, IsGroupLeader, etc.) without CustomScientistNPC.
    /// </summary>
    public class ZombieNPC : MonoBehaviour
    {
        public ScientistNPC Npc { get; private set; }
        public Horde Horde { get; internal set; }
        public bool IsGroupLeader { get; internal set; }
        public ConfigData.MemberOptions.Loadout Loadout { get; set; }

        public Vector3 DestinationOverride { get; set; }
        public bool unreachableLastFrame;
        public bool isHidingInside;
        public Item ThrowableExplosive { get; private set; }

        private float lastSetDestinationOverride;
        private BasePlayer recentAttacker;
        private SphereEntity noiseEmitter;
        private float lastThrowTime;
        private bool lightsOn;

        public bool RecentlySetDestination => Time.time - lastSetDestinationOverride < 0.4f;

        public bool IsDestroyed => this == null || Npc == null || Npc.IsDestroyed;

        // Unity fake-null: never touch .transform on a destroyed component (throws NRE).
        public Transform Transform => this == null ? null : transform;

        public BaseAIBrain Brain => Npc != null ? Npc.Brain : null;

        public PlayerInventory inventory => Npc != null ? Npc.inventory : null;

        public BaseEntity CurrentTarget
        {
            get
            {
                // GrimmNPC owns targeting via CustomScientistNpc.CurrentTarget (not Events.Memory).
                BaseEntity grimm = GrimmNpcBridge.GetCombatTarget(Npc);
                if (grimm != null) return grimm;
                try
                {
                    if (Brain?.Events?.Memory?.Entity == null) return null;
                    return Brain.Events.Memory.Entity.Get(Brain.Events.CurrentInputMemorySlot);
                }
                catch { return null; }
            }
            set
            {
                GrimmNpcBridge.SetCombatTarget(Npc, value);
                try
                {
                    if (Brain?.Events?.Memory?.Entity == null) return;
                    Brain.Events.Memory.Entity.Set(value, Brain.Events.CurrentInputMemorySlot);
                }
                catch { }
            }
        }

        public bool HasTarget => CurrentTarget != null;

        public AIState CurrentState
        {
            get
            {
                try
                {
                    return Brain?.CurrentState != null ? Brain.CurrentState.StateType : AIState.Idle;
                }
                catch { return AIState.Idle; }
            }
        }

        public bool HasHumanTargetInRange
        {
            get
            {
                if (IsDestroyed || !CurrentTarget || !CurrentTarget.transform) return false;
                Transform self = Transform;
                if (!self) return false;
                float lost = Loadout?.Sensory?.TargetLostRange ?? 40f;
                return CurrentTarget is BasePlayer && Vector3.Distance(self.position, CurrentTarget.transform.position) < lost;
            }
        }

        public bool IsBrainSleeping => GrimmNpcBridge.IsBrainSleeping(Npc);

        public static ZombieNPC Get(BaseEntity entity)
        {
            if (entity == null) return null;
            return entity.GetComponent<ZombieNPC>();
        }

        public static ZombieNPC Get(BasePlayer player) => Get((BaseEntity)player);

        public static ZombieNPC Get(ScientistNPC npc) => Get((BaseEntity)npc);

        private static bool IsSteamId(BasePlayer player)
        {
            if (player == null) return false;
            ulong id = (ulong)player.userID;
            return id > 76561197960265728UL;
        }

        public void Initialize(ScientistNPC npc, ConfigData.MemberOptions.Loadout loadout, Horde horde)
        {
            Npc = npc;
            Loadout = loadout;
            Horde = horde;
            DestinationOverride = Vector3.zero;

            FindThrowableExplosive();

            if (ConfigData.Configuration != null && ConfigData.Configuration.Member.EnableZombieNoises)
                Invoke(nameof(SetupNoiseObject), 1f);

            InvokeRepeating(nameof(LightCheck), 1f, 30f);
            InvokeRepeating(nameof(LeaderTick), 1f, 1f);
            InvokeRepeating(nameof(EnsureRoaming), 3f, 8f);
        }

        private void LeaderTick()
        {
            if (IsDestroyed) return;
            if (IsGroupLeader && Horde != null)
                Horde.Update();
        }

        /// <summary>Nudge idle zombies to keep GrimmNPC RoamState moving within local roam radius.</summary>
        private void EnsureRoaming()
        {
            if (IsDestroyed || Npc == null || Brain == null) return;
            if (IsBrainSleeping) GrimmNpcBridge.SetBrainSleeping(Npc, false);
            if (HasTarget) return;
            if (CurrentState > AIState.Roam) return;

            try
            {
                if (Npc.NavAgent != null && !Npc.NavAgent.enabled)
                    Npc.NavAgent.enabled = true;
                Npc.IsDormant = false;

                Vector3 home = Horde != null ? Horde.InitialPosition : Transform.position;
                float roam = Horde != null && Horde.IsLocalHorde && Horde.MaximumRoamDistance > 0
                    ? Horde.MaximumRoamDistance
                    : (ConfigData.Configuration?.Horde?.LocalRoam == true ? ConfigData.Configuration.Horde.RoamDistance : 40f);
                if (roam < 5f) roam = 40f;

                Vector2 circle = UnityEngine.Random.insideUnitCircle * UnityEngine.Random.Range(roam * 0.25f, roam * 0.85f);
                Vector3 dest = home + new Vector3(circle.x, 0f, circle.y);
                if (TerrainMeta.HeightMap != null)
                    dest.y = TerrainMeta.HeightMap.GetHeight(dest);
                if (NavmeshSpawnPoint.Find(dest, 10f, out Vector3 nav))
                    dest = nav;

                Brain.SwitchToState(AIState.Roam, 0);
                Brain.Navigator?.SetDestination(dest, ZombieHordePlugin.DefaultRoamSpeedPublic, 0f, 0f);
            }
            catch { }
        }

        private void OnDestroy()
        {
            CancelInvoke(nameof(LeaderTick));
            CancelInvoke(nameof(LightCheck));
            CancelInvoke(nameof(EnsureRoaming));
            DestroyNoiseEmitter();
        }

        /// <summary>
        /// Drop the head-parented visualization sphere before corpse TakeChildren.
        /// SphereEntity does not override SwitchParent, so vanilla logs "SwitchParent Missed".
        /// </summary>
        internal void DestroyNoiseEmitter()
        {
            CancelInvoke(nameof(MakeZombieNoises));
            SphereEntity sphere = noiseEmitter;
            noiseEmitter = null;
            if (sphere == null || sphere.IsDestroyed)
                return;
            sphere.Kill();
        }

        public void Kill(BaseNetworkable.DestroyMode mode = BaseNetworkable.DestroyMode.None)
        {
            if (Npc != null && !Npc.IsDestroyed)
                Npc.Kill(mode);
        }

        public void SetRoamTargetOverride(Vector3 position)
        {
            if (!IsGroupLeader) return;
            lastSetDestinationOverride = Time.time;
            DestinationOverride = position;
            ResetRoamState();
            try
            {
                if (Brain?.Navigator != null)
                    Brain.Navigator.SetDestination(position, BaseNavigator.NavigationSpeed.Fast, 0f, 0f);
            }
            catch { }
        }

        public void ResetRoamState()
        {
            try
            {
                if (Brain?.Navigator == null) return;
                if (DestinationOverride != Vector3.zero)
                    Brain.Navigator.SetDestination(DestinationOverride, BaseNavigator.NavigationSpeed.Normal, 0f, 0f);
            }
            catch { }
        }

        public void OnInitialSpawn()
        {
            if (IsGroupLeader || Horde?.Leader == null) return;
            if (!Horde.Leader.HasTarget) return;
            SetKnown(Horde.Leader.CurrentTarget);
        }

        public void SetKnown(BaseEntity entity)
        {
            if (entity == null || Npc == null) return;
            try
            {
                MethodInfo setKnown = Npc.GetType().GetMethod("SetKnown", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(BaseEntity) }, null);
                if (setKnown != null)
                    setKnown.Invoke(Npc, new object[] { entity });
                else if (Brain?.Senses?.Memory != null)
                    Brain.Senses.Memory.SetKnown(entity, Npc, Brain.Senses);
            }
            catch { }
        }

        public void SwitchToChase(BaseEntity entity)
        {
            if (entity == null || Brain == null) return;
            try
            {
                CurrentTarget = entity;
                Brain.SwitchToState(AIState.Chase, 1);
            }
            catch { }
        }

        public void SetSleeping(bool sleep) => GrimmNpcBridge.SetBrainSleeping(Npc, sleep);

        public void OnSensation(Sensation sensation)
        {
            var cfg = ConfigData.Configuration;
            if (cfg == null) return;

            if (cfg.Member.EnableDormantSystem && cfg.Member.DormantUntilSensedOnly && cfg.Horde.UseSenses && Horde != null && Horde.IsSleeping)
            {
                if (!(sensation.Initiator is ScientistNPC initiatorNpc && Get(initiatorNpc) != null)
                    && !(sensation.UsedEntity is TimedExplosive && sensation.Type == SensationType.ThrownWeapon))
                {
                    Horde.ForceWakeFromSleep();
                }
            }

            if (!cfg.Horde.UseSenses || sensation.Type == SensationType.Explosion)
                return;

            if (sensation.UsedEntity is TimedExplosive && sensation.Type == SensationType.ThrownWeapon)
                return;

            if (sensation.Initiator)
            {
                if (Get(sensation.Initiator) != null)
                    return;
                SetKnown(sensation.Initiator);
            }

            if (IsGroupLeader && CurrentState <= AIState.Roam && !HasTarget)
                Horde?.SetLeaderRoamTarget(sensation.Position);
        }

        public void OnAttackedNotify(HitInfo info)
        {
            Horde?.ForceWakeFromSleep();
            if (info?.Initiator == null || Npc == null) return;
            if (info.Initiator.EqualNetID(Npc)) return;

            BaseEntity initiator = info.Initiator;
            if (initiator == recentAttacker) return;
            recentAttacker = initiator as BasePlayer;

            if ((initiator is BasePlayer player && CanTargetBasePlayer(player)) || (initiator is BaseNpc && CanTargetEntity(initiator)))
            {
                // Always force live chase on damage — static roam-to-last-known felt like laggy pathing.
                Horde?.RegisterInterestInTarget(this, initiator, true);
            }
        }

        public void OnHurtNotify(HitInfo info)
        {
            if (info == null) return;
            if (Get(info.InitiatorPlayer) != null)
            {
                info.damageTypes.ScaleAll(0);
                return;
            }
            if (info.Initiator is ResourceEntity)
            {
                info.damageTypes.ScaleAll(0);
                return;
            }

            var cfg = ConfigData.Configuration;
            if (cfg != null && cfg.Member.HeadshotKills && info.isHeadshot)
            {
                if (info.damageTypes.Total() >= cfg.Member.MinimumHeadshotDamage)
                    info.damageTypes.ScaleAll(1000);
            }

            OnAttackedNotify(info);
        }

        public bool CanTargetBasePlayer(BasePlayer player)
        {
            if (!player.IsValid() || player.IsFlying || player is NPCShopKeeper)
                return false;

            if (IsSteamId(player) && !player.IsConnected)
                return false;

            var cfg = ConfigData.Configuration;
            if (cfg == null) return true;

            if (cfg.Member.IgnoreSleepers && player.IsSleeping())
                return false;

            if (!cfg.Member.TargetHumanNPCs && !player.IsNpc && !IsSteamId(player))
                return false;

            if (!cfg.Member.TargetNPCs && player.IsNpc)
            {
                if (cfg.Member.TargetNPCsThatAttack && recentAttacker == player)
                    return true;
                return false;
            }

            if (IsSteamId(player) &&
                (Compat.Permission.UserHasPermission(player.UserIDString, ZombieHordePlugin.IGNORE_PERMISSION)
                 || ZombieHordePlugin.IgnoreUntilHurtPlayers.Contains(player.UserIDString)))
                return false;

            if (Loadout?.Sensory != null && Loadout.Sensory.IgnoreSafeZonePlayers && player.InSafeZone())
                return false;

            if (!cfg.Member.TargetInBuildings && ZombieHordePlugin.IsInOrOnBuilding(player))
                return false;

            return true;
        }

        public bool CanTargetEntity(BaseEntity baseEntity)
        {
            if (!(baseEntity is BasePlayer) && !(baseEntity is BaseNpc))
                return false;

            var cfg = ConfigData.Configuration;
            if (cfg != null && cfg.Horde.RestrictLocalChaseDistance && Horde != null && Horde.IsLocalHorde)
            {
                if (Vector3.Distance(baseEntity.transform.position, Horde.InitialPosition) > Horde.MaximumRoamDistance * 1.5f)
                    return false;
            }

            if (cfg != null && !cfg.Member.TargetAnimals && baseEntity is BaseNpc)
                return false;

            if (Loadout?.Movement != null && !Loadout.Movement.CanSwim && baseEntity.WaterFactor() >= 0.95f)
                return false;

            float lost = Brain != null ? Brain.TargetLostRange : (Loadout?.Sensory?.TargetLostRange ?? 40f);
            if (Vector3.Distance(baseEntity.transform.position, Transform.position) > lost)
                return false;

            return true;
        }

        public AttackEntity GetAttackEntity() => Npc != null ? Npc.GetAttackEntity() : null;

        // Proxies used by RaidingZombies (original ChaosNPC ZombieNPC entity API)
        public PlayerEyes eyes => Npc != null ? Npc.eyes : null;
        public NetworkableId netId => Npc != null && Npc.net != null ? Npc.net.ID : default;
        public bool IsAlive() => Npc != null && Npc.IsAlive();
        public bool IsDormant
        {
            get => Npc != null && Npc.IsDormant;
            set { if (Npc != null) Npc.IsDormant = value; }
        }
        public bool IsMounted() => Npc != null && Npc.isMounted;
        public void EquipWeapon(bool skipDeployDelay = false)
        {
            try { Npc?.EquipWeapon(skipDeployDelay); } catch { }
        }
        public void SetAimDirection(Vector3 dir)
        {
            try { Npc?.SetAimDirection(dir); } catch { }
        }
        public void SetPlayerFlag(BasePlayer.PlayerFlags flag, bool value)
        {
            try { Npc?.SetPlayerFlag(flag, value); } catch { }
        }
        public void UpdateActiveItem(ItemId itemId)
        {
            try { Npc?.UpdateActiveItem(itemId); } catch { }
        }
        public void SendNetworkUpdate(BasePlayer.NetworkQueue queue = BasePlayer.NetworkQueue.Update)
        {
            try { Npc?.SendNetworkUpdate(queue); } catch { }
        }
        public Vector3 GetInheritedProjectileVelocity(Vector3 direction)
        {
            try { return Npc != null ? Npc.GetInheritedProjectileVelocity(direction) : Vector3.zero; }
            catch { return Vector3.zero; }
        }

        private void FindThrowableExplosive()
        {
            if (inventory?.containerBelt == null) return;
            for (int i = 0; i < inventory.containerBelt.itemList.Count; i++)
            {
                Item item = inventory.containerBelt.GetSlot(i);
                if (item != null && item.GetHeldEntity() is ThrownWeapon)
                {
                    ThrowableExplosive = item;
                    break;
                }
            }
        }

        public bool TryThrownWeapon(BasePlayer target)
        {
            if (Time.time - lastThrowTime < 5f) return false;
            if (ThrowableExplosive == null)
            {
                lastThrowTime = Time.time;
                return false;
            }
            if (target == null || Npc == null) return false;

            Vector3 targetPosition = target.transform.position;
            float distanceToTarget = Vector3.Distance(targetPosition, Transform.position);
            float maxRange = ConfigData.Configuration?.Member?.MaxExplosiveThrowRange ?? 20f;
            if (distanceToTarget <= 2f || distanceToTarget > maxRange)
                return false;

            try
            {
                Npc.UpdateActiveItem(ThrowableExplosive.uid);
                ThrownWeapon thrownWeapon = Npc.GetActiveItem()?.GetHeldEntity() as ThrownWeapon;
                if (!thrownWeapon) return false;

                if (!(ConfigData.Configuration?.Member?.ConsumeThrowables ?? false))
                    thrownWeapon.GetItem().amount++;

                thrownWeapon.ResetAttackCooldown();
                thrownWeapon.ServerThrow(targetPosition);
                lastThrowTime = Time.time;
                return true;
            }
            catch { return false; }
        }

        public void TryMountTargetsVehicle(BaseEntity baseEntity)
        {
            if (ConfigData.Configuration == null || !ConfigData.Configuration.Member.CanMountVehicles) return;
            if (Npc == null || !(baseEntity is BasePlayer player) || !player.isMounted) return;
            try
            {
                if (Npc.isMounted) return;
                BaseMountable mountable = player.GetMounted();
                if (mountable == null) return;

                BaseVehicle vehicle = mountable.VehicleParent();
                if (vehicle != null)
                {
                    foreach (BaseVehicle.MountPointInfo point in vehicle.allMountPoints)
                    {
                        if (point == null || point.mountable == null || point.mountable.IsMounted())
                            continue;
                        point.mountable.AttemptMount(Npc, false);
                        if (Npc.isMounted) return;
                    }
                }

                if (!mountable.IsMounted())
                    mountable.AttemptMount(Npc, false);
            }
            catch { }
        }

        public void TryDismount()
        {
            try
            {
                if (Npc != null && Npc.isMounted)
                    Npc.EnsureDismounted();
            }
            catch { }
        }

        private void SetupNoiseObject()
        {
            if (Npc == null || Npc.IsDestroyed) return;
            try
            {
                noiseEmitter = GameManager.server.CreateEntity("assets/prefabs/visualization/sphere.prefab", transform.position) as SphereEntity;
                if (noiseEmitter == null) return;
                noiseEmitter.SetParent(Npc, StringPool.Get("head"));
                noiseEmitter.transform.localPosition = Vector3.zero;
                noiseEmitter.transform.localScale = Vector3.zero;
                noiseEmitter.currentRadius = noiseEmitter.lerpRadius = 0f;
                noiseEmitter.lerpSpeed = 1000;
                noiseEmitter.enabled = false;
                noiseEmitter.enableSaving = false;
                noiseEmitter.Spawn();
                MakeZombieNoises();
            }
            catch { }
        }

        private void MakeZombieNoises()
        {
            if (IsDestroyed || noiseEmitter == null || noiseEmitter.IsDestroyed) return;
            Invoke(nameof(MakeZombieNoises), UnityEngine.Random.Range(8, 15));
        }

        private void LightCheck()
        {
            if (IsDestroyed || inventory == null) return;
            bool night = TOD_Sky.Instance != null && (TOD_Sky.Instance.Cycle.Hour > 18 || TOD_Sky.Instance.Cycle.Hour < 6);
            if (night == lightsOn) return;
            lightsOn = night;
            try
            {
                foreach (Item item in inventory.containerWear.itemList)
                {
                    ItemModWearable wearable = item.info.GetComponent<ItemModWearable>();
                    if (wearable != null && wearable.emissive)
                    {
                        item.SetFlag(global::Item.Flag.IsOn, lightsOn);
                        item.MarkDirty();
                    }
                }
            }
            catch { }
        }

        public void PrepareCorpseLoot()
        {
            if (inventory == null || ConfigData.Configuration == null) return;
            var cfg = ConfigData.Configuration;

            if (cfg.Member.GiveGlowEyes && inventory.containerWear != null)
            {
                for (int i = inventory.containerWear.itemList.Count - 1; i >= 0; i--)
                {
                    Item item = inventory.containerWear.itemList[i];
                    if (item.info == ConfigData.MemberOptions.Loadout.GlowEyes)
                    {
                        item.RemoveFromContainer();
                        item.Remove(0f);
                    }
                }
            }

            if (cfg.Loot.DropInventory && cfg.Loot.DroppedBlacklist != null && cfg.Loot.DroppedBlacklist.Length > 0)
            {
                void StripBlacklist(ItemContainer container)
                {
                    if (container == null) return;
                    for (int i = container.itemList.Count - 1; i >= 0; i--)
                    {
                        Item item = container.itemList[i];
                        if (Array.IndexOf(cfg.Loot.DroppedBlacklist, item.info.shortname) >= 0)
                        {
                            item.RemoveFromContainer();
                            item.Remove(0f);
                        }
                    }
                }
                StripBlacklist(inventory.containerBelt);
                StripBlacklist(inventory.containerMain);
            }
        }
    }
}
