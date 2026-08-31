// Requires: KaruzaEntitiesCommon
// Requires: CustomEntities
// Requires: Radio

using Facepunch;
using Facepunch.Extend;
using Network;
using Oxide.Core;
using Rust;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using UnityEngine;
using VLB;
using static KaruzaVehicles.CustomEntities;
using static KaruzaVehicles.KaruzaEntitiesCommon;
using static KaruzaVehicles.Radio;
using static SeekerTarget;

namespace KaruzaVehicles
{
    [Info("RustHelicopter", "Karuza", "1.30.0")]
    public class RustHelicopter : RustPlugin, IKaruzaEntityPlugin
    {
        public static RustHelicopter Instance;
        
        private Configuration configuration;
        public Dictionary<string, HelicopterConfig> TempConfigs = new Dictionary<string, HelicopterConfig>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, HelicopterConfig> HelicopterConfigs = new Dictionary<string, HelicopterConfig>(StringComparer.OrdinalIgnoreCase);

        public bool IsUnloading = false;

        internal void OnServerInitialized()
        {
            Instance = this;
            LoadConfig();

            InitializeAmmoTypes(HelicopterConfigs.Values.ToList<BaseVehicleConfig>());

            if (TempConfigs.Count > 0)
            {
                RegisterVehicleBundles(TempConfigs);
                TempConfigs.Clear();
            }

            RequestEntityConfigs<HelicopterConfig>(this, 2);
        }

        internal void Unload()
        {
            IsUnloading = true;
            SaveAndUnregisterVehicleBundles();

            configuration = null;
            Instance = null;
        }

        [ConsoleCommand("rusthelicopter.reload")]
        private void ReloadVehicle(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null)
            {
                return;
            }

            if (arg.Player() != null)
            {
                return;
            }

            if (arg.Args.Length <= 0)
            {
                return;
            }

            LoadVehicleConfig(arg.GetString(0));
        }

        #region Heli Behaviors

        public class RustHelicopterController : PlayerHelicopter, IRadio, ISeekerTargetOwner, ICustomEntity, IVehicle, IWeaponController, SamSite.ISamSiteTarget, IEntity, ILandingGearVehicle
        {
            #region Constants

            const int ITEM_LONGSWORD = -1469578201;

            const ulong LONGSWORD_REAR_PROPELLER_SKIN_ID = 1652079216;
            const ulong LONGSWORD_TOP_PROPELLER_SKIN_ID = 3218520235;

            const string ALARM_SFX = "assets/prefabs/locks/keypad/effects/lock.code.unlock.prefab";
            const string FLARE_PREFAB = "assets/content/vehicles/attackhelicopter/attackhelipilotflare.prefab";

            const float FLARE_LAUNCH_VAL = 10f;

            #endregion

            #region Variables
            public bool IsActive { get; set; } = false;

            public HelicopterConfig HeliConfig;
            public BaseEntityConfig Config { get { return HeliConfig; } }
            public BaseVehicleConfig VehicleConfig { get { return HeliConfig; } }

            Telephone cockpitRadio;
            SpecialBroadcaster radioBroadcaster;
            SpecialBaseCombatEntity lowHealthPrefab;
            Buoyancy buoyancy;
            CodeLock codeLock;
            Ray ray;
            RaycastHit[] raycastHit = new RaycastHit[1];
            Collider dismountCollider;
            EngineStorageContainer physicalEngine;
            Transform cachedTransform;

            List<SpecialFakeEngine> fakeEngines = new List<SpecialFakeEngine>();
            List<BaseEntity> cockpitElectical = new List<BaseEntity>();
            List<SpecialLight> vehicleLights = new List<SpecialLight>();
            List<Connection> cachedConnections = new List<Connection>();
            List<SleepingBag> respawnPoints = new List<SleepingBag>();

            List<Tuple<float, LowHealthEffectConfig>> lowHealthThresholds = new List<Tuple<float, LowHealthEffectConfig>>();
            Dictionary<ulong, FuelModifierSetting> fuelModifiers = new Dictionary<ulong, FuelModifierSetting>();

            OrderedDictionary driverCache = new OrderedDictionary();


            float currentTime;
            float onGroundTilRealTime;
            float collisionAvailable;
            float lastFlipTime;
            float loseControlHealthThreshold;
            float outsideDecayMinutes;
            float insideDecayMinutes;
            float timeAfterEngineOffToStartDecay;
            float groundedDistanceCheck;

            int parentedId;

            bool hasSwitch;
            bool hasRadio;
            bool hasBroadcaster;
            bool hasBuoyancy;
            bool hasEngine = false;
            bool invokeStopSpecialGunsStarted = false;
            bool isFakeEnginesOn = false;
            bool loadedFromSave = false;
            bool isPowered;
            bool playingRadarWarning;
            bool playingRadarLock;
            bool hasStorage;
            bool hasFuelStorage;
            bool hasPhysicalEngine;
            bool centralLockingEnabled;
            bool hasCodeLock;
            bool lowHealthEffectsValid;
            bool hasTowVehicle;
            bool hasRespawns;
            bool enableServerOcclusion;
            bool invokingLowHealthFx;
            bool hasBoostStorage;
            bool hasCreator;

            int towVehicles;

            LowHealthEffectConfig spawnedLowHealthEffect;

            HitInfo lastServerProjectileAttack;
            bool invokingClearLastAttack;
            bool loseControl;

            //IRemoteControllable ptzCamera;

            ulong vehicleNetId;
            ulong currentFuelModifierSkinId;

            int tick;
            Vector3 oldPos;
            Quaternion oldRot;
            public override float PositionTickRate => 0.1f;
            public override bool AlwaysAllowBradleyTargeting => true;
            
            #region Custom Entities
            public CustomHandler Handler { get; set; }
            public List<BaseEntity> SaveListInDataFile { get; set; } = null;
            public virtual bool ShouldDoNormalVanillaBaseCombatEntityHurt => true;
            public virtual bool EnableSavingToDiskByDefault
            {
                get
                {
                    if (Instance.configuration.ForceSaving.HasValue)
                    {
                        return Instance.configuration.ForceSaving.Value;
                    }

                    return (HeliConfig?.EnableSaving).GetValueOrDefault();
                }
            }

            public virtual bool HasDefaultInventory => false;
            public virtual bool DefaultInventoryHandledByBaseType => false;
            public ItemContainer DefaultInventory { get; set; } = null;
            public virtual int DefaultInventoryCapacity { get { return 0; } }
            #endregion
            public SuperBaseEntityConfig BaseConfig { get { return HeliConfig; } }
            public BodyType BodyType { get { return BaseConfig.BodyType; } }
            public List<IStorageContainer> StorageContainers { get; private set; } = new List<IStorageContainer>();
            public VehicleFuelContainer FuelContainer { get; private set; }
            public VehicleBoostContainer BoostContainer { get; private set; }
            public Dictionary<HurtTriggerType, Dictionary<HurtTriggerConfig, TriggerHurtNotChild>> HurtTriggers { get; } = new Dictionary<HurtTriggerType, Dictionary<HurtTriggerConfig, TriggerHurtNotChild>>();
            public BaseEntity BaseEntity { get { return this; } }
            public bool HasLock { get { return hasCodeLock; } }
            public bool IsPowered
            {
                get
                {
                    return isPowered;
                }
            }
            public BaseVehicle BaseVehicle { get { return this; } }
            public List<MountPointInfo> MountPoints { get { return this.mountPoints; } }
            public IFuelSystem FuelSystem { get { return this.engineController.FuelSystem; } }
            public int RadioFrequency { get { return radioBroadcaster.frequency; } }
            public Action OnPhysicsUpdate { get; set; }
            public Action OnMountedChange { get; set; }
            public Action<bool> OnToggled { get; set; }
            public Action<bool> OnPowerToggle { get; set; }
            public Action<float, float> OnHealthChange { get; set; }
            public float SteerAngle { get { return 0; } }
            public float DriveWheelVelocity { get { return rigidBody.linearVelocity.magnitude; } }
            public float BoostTime { get; set; }
            public bool BoostOnCooldown { get; set; }
            public Action OnBoostTimeUpdate { get; set; }
            public new SamSite.SamTargetType SAMTargetType { get { return HeliConfig.SAMTargetSettings; } }

            public float GetBrakeInput()
            {
                if (hasTowVehicle)
                {
                    return 0;
                }

                return 0;
            }

            public float GetThrottleInput()
            {
                if (hasTowVehicle)
                {
                    return 0;
                }
                else if (IsEngineOn())
                {
                    float throttle = currentInputState.throttle;
                    return Mathf.Clamp(throttle, -1f, 1f);
                }

                return 0f;
            }

            public bool IsOnGround
            {
                get
                {
                    if (this.rigidBody.IsSleeping())
                    {
                        this.onGroundTilRealTime = Time.realtimeSinceStartup;
                        return true;
                    }

                    if (this.onGroundTilRealTime > Time.realtimeSinceStartup)
                    {
                        return true;
                    }

                    if (wheels.Length == 0 || !LandingGearOn)
                    {
                        if (hasBuoyancy && buoyancy.timeOutOfWater < 0.10f)
                        {
                            return true;
                        }

                        if (Physics.RaycastNonAlloc(cachedTransform.position, cachedTransform.TransformDirection(-Vector3.up), raycastHit, groundedDistanceCheck, GROUND_LAYER) > 0)
                        {
                            this.onGroundTilRealTime = Time.realtimeSinceStartup + 0.01f;
                            return true;
                        }
                    }
                    else
                    {
                        foreach (Wheel wheel in wheels)
                        {
                            if (wheel.wheelCollider.isGrounded)
                            {
                                this.onGroundTilRealTime = Time.realtimeSinceStartup + 0.01f;
                                return true;
                            }
                        }
                    }

                    return false;
                }
                set
                {
                    if (value)
                    {
                        this.onGroundTilRealTime = Time.realtimeSinceStartup + 0.01f;
                    }
                }
            }

            public bool IsOnWater
            {
                get
                {
                    if (hasBuoyancy && (buoyancy.InWater || buoyancy.timeOutOfWater < 0.10f))
                    {
                        return true;
                    }

                    return false;
                }
            }

            public bool InWater
            {
                get
                {
                    if (IsOnGround)
                    {
                        return false;
                    }

                    return WaterLevel.Test(waterSample.transform.position, false, true);
                }
            }

            public override float GetServiceCeiling()
            {
                if (HeliConfig.ServiceCeiling > 0)
                {
                    return HeliConfig.ServiceCeiling;
                }

                return base.GetServiceCeiling();
            }

            public bool IsEjectEligible { get { return !IsOnGround; } }

            #endregion

            #region Hooks

            public void Awake()
            {
                cachedTransform = this.transform;

                this.rigidBody = this.gameObject.GetOrAddComponent<Rigidbody>();
                this.rigidBody.isKinematic = false;
                this.rigidBody.useGravity = true;
                this.rigidBody.detectCollisions = true;
                this.rigidBody.interpolation = RigidbodyInterpolation.None;
                this.rigidBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                this.rigidBody.constraints = RigidbodyConstraints.None;

                var waterSample = new GameObject();
                waterSample.transform.SetParent(cachedTransform, false);
                this.waterSample = waterSample.transform;

                this.serverGibs = new GameObjectRef();
                this.explosionEffect = new GameObjectRef();
                this.fireBall = new GameObjectRef();

                var prefabGuid = GameManifest.pathToGuid[DEBRIS_EFFECT_PREFAB];
                this.crashEffect = new GameObjectRef() { guid = prefabGuid };
                this.wheels = new Wheel[0];
                this.killTriggers = new GameObject[0];
                this.propDirection = new DirectionProperties[0];
                this.legacyDismount = true;

                this.syncPosition = true;
                this.mountPoints = new List<MountPointInfo>();
                this.onGroundTilRealTime = Time.realtimeSinceStartup + 1f;
                this.collisionAvailable = Time.time + 2.5f;
                this.nextDamageTime = Time.time + 1f;

                this.explosionForceMultiplier = 0;
                this.explosionForceMax = 0;
                this.canTriggerParent = false;
                this.globalBroadcast = false;

                if (!string.IsNullOrEmpty(this.ShortPrefabName))
                {
                    this.HeliConfig = Instance.HelicopterConfigs[this.ShortPrefabName];
                }

                CustomHandler.AttachNewHandlerToCustomEntityIfNotPrototype(this);

                SetupEdgeTransfer();
            }

            // Since we are init engine in InitializeVehicle, we want to skip the base.InitShared
            // While still performing base.base.InitShared. This is copied from there.
            public override void InitShared()
            {
                using (TimeWarning.New("InitEntityLinks"))
                {
                    if (base.isServer)
                    {
                        links.AddLinks(this, PrefabAttribute.server.FindAll<Socket_Base>(prefabID));
                    }
                }
            }

            public override void ServerInit()
            {
                EnableSaving(false);

                try
                {
                    InitializeVehicle();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{ShortPrefabName} Failed to Spawn - Error: {ex.ToString()}");
                    Kill();
                    return;
                }

                Handler?.ServerInit();

                serverEntities.RegisterID(this);
                if (net != null)
                {
                    net.handler = this;
                }
               
                if (syncPosition && PositionTickRate >= 0f)
                {
                    if (PositionTickFixedTime)
                    {
                        InvokeRepeatingFixedTime(CustomNetworkPositionTick);
                    }
                    else
                    {
                        InvokeRandomized(CustomNetworkPositionTick, PositionTickRate, PositionTickRate - PositionTickRate * 0.05f, PositionTickRate * 0.05f);
                    }
                }

                Query.Server.Add(this);
                AllMountables.Add(this);
                clearRecentDriverAction = ClearRecentDriver;
                lastEngineOnTime = Time.time;
                InvokeRandomized(UpdateNetwork, 0f, 0.2f, 0.05f);

                if (!Instance.configuration.DisableDecay)
                {
                    InvokeRandomized(CustomDecayTick, UnityEngine.Random.Range(30f, 60f), 60f, 6f);
                }

                SeekerTarget.SetSeekerTarget(this, VehicleConfig.SeekerStrength);
                KaruzaEntitiesCommon.Instance.CustomSAMTargets.Add(this);
                ToggleLandingGear(true);

#if CARBON
                // Necessary for carbon
                // Carbon for some reason has hp at 0 when oxide doesn't
                if (ResetLifeStateOnSpawn)
                {
                    InitializeHealth(StartHealth(), StartMaxHealth());
                    lifestate = LifeState.Alive;
                }
#endif

                if (hasPhysicalEngine)
                {
                    physicalEngine.RefreshLoadoutData();
                }

                SetupEdgeTransfer();
            }

            bool CustomHasChanged
            {
                get
                {
                    if (rigidBody.IsSleeping())
                    {
                        return false;
                    }

                    var changed = false;
                    // base has changed seems unreliable and appears to be always true
                    // Except when recently spawned
                    if (!cachedTransform.hasChanged)
                    {
                        changed = false;
                    }
                    else if ((oldPos - cachedTransform.position).sqrMagnitude > 0.0001f)
                    {
                        changed = true;
                    }
                    else if (Quaternion.Angle(oldRot, cachedTransform.rotation) > 0.01f)
                    {
                        changed = true;
                    }

                    oldPos = cachedTransform.position;
                    oldRot = cachedTransform.rotation;

                    return changed;
                }
            }

            void CustomNetworkPositionTick()
            {
                if (OnPhysicsUpdate != null)
                {
                    var changed = CustomHasChanged;
                    if (changed)
                    {
                        ++tick;
                    }

                    if (tick >= 2)
                    {
                        tick = 0;
                        OnPhysicsUpdate();
                    }
                }

                if (!cachedTransform.hasChanged)
                {
                    if (ticksSinceStopped >= 6)
                    {
                        return;
                    }

                    ++ticksSinceStopped;
                }
                else
                {
                    ticksSinceStopped = 0;
                }

                TransformChanged();
                cachedTransform.hasChanged = false;
            }

            public override bool ShouldNetworkTo(BasePlayer player)
            {
                var sn = base.ShouldNetworkTo(player);
                if (sn && !IsOn())
                {
                    tick += 2;
                }

                return sn;
            }

            public void CustomDecayTick()
            {
                if (base.healthFraction != 0f && !IsOn() && !(Time.time < lastEngineOnTime + timeAfterEngineOffToStartDecay))
                {
                    var decayRate = 1f;
                    if (IsOutside())
                    {
                        if (VehicleConfig.CheckingBuildingPrivForInsideDecay && !object.ReferenceEquals(GetBuildingPrivilege(), null))
                        {
                            decayRate /= insideDecayMinutes;
                        }
                        else
                        {
                            decayRate /= outsideDecayMinutes;
                        }
                    }
                    else
                    {
                        decayRate /= insideDecayMinutes;
                    }

                    Hurt(MaxHealth() * decayRate, DamageType.Decay, this, useProtection: false);
                }
            }

            public override void TryStartEngine(BasePlayer player)
            {
                if (!isPowered || (hasPhysicalEngine && !physicalEngine.IsUsable) || (hasWeakpoints && Weakspots.Exists(wp => wp.IsDestroyed)))
                {
                    return;
                }

                base.TryStartEngine(player);
            }

            private void Update()
            {
                currentTime = Time.realtimeSinceStartup;
            }

            private void LateUpdate()
            {
                if (hasRadio)
                {
                    var driver = CustomGetDriver();
                    if (!object.ReferenceEquals(cockpitRadio.Controller.currentPlayer, null))
                    {
                        if (object.ReferenceEquals(driver, null))
                        {
                            Radio.RemoveRadio(cockpitRadio.Controller.currentPlayer);
                            cockpitRadio.Controller.currentPlayer = null;
                        }
                    }
                    else if (isPowered)
                    {
                        cockpitRadio.Controller.currentPlayer = driver;
                    }
                }

                currentTime = Time.realtimeSinceStartup;
                if (IsOn())
                {
                    if (!isPowered || (hasPhysicalEngine && !physicalEngine.IsUsable))
                    {
                        engineController.StopEngine();
                        return;
                    }

                    EdgeTransferCheck();
                    var lgs = HeliConfig.LandingGearSettings;
                    if (lgs.Enabled && LandingGearOn && CustomHasDriver())
                    {
                        if (lgs.AutoRetract && autoRetractReady && !IsOnGround && this.onGroundTilRealTime + lgs.AutoRetractAfterSeconds < currentTime)
                        {
                            ToggleLandingGear(false);
                            KaruzaEntitiesCommon.Instance.ShowToast(CustomGetDriver(), VehicleConfig.GeneralToastSettings.LandingGearSwitchOffToast, GameTip.Styles.Red_Normal);
                        }
                        else if (HeliConfig.GeneralToastSettings.Enabled && HeliConfig.GeneralToastSettings.LandingGearOnWarningEnabled)
                        {
                            if (landingGearToastDisplayed)
                            {
                                if (this.onGroundTilRealTime >= currentTime)
                                {
                                    landingGearToastDisplayed = false;
                                }
                            }
                            else if (this.onGroundTilRealTime + HeliConfig.GeneralToastSettings.LandingGearOnWarningDelay < currentTime)
                            {
                                landingGearToastDisplayed = true;
                                var driver = CustomGetDriver();
                                KaruzaEntitiesCommon.Instance.ShowToast(driver, VehicleConfig.GeneralToastSettings.LandingGearOnWarning, GameTip.Styles.Red_Normal);
                            }
                        }
                    }

                    if (EjectUtilities.ShouldEject(this) && EjectUtilities.CanEject(this))
                    {
                        for (int i = 0; i < mountPoints.Count; i++)
                        {
                            var mp = mountPoints[i];
                            if (!mp.mountable.IsBusy())
                            {
                                continue;
                            }

                            var mounted = mp.mountable.GetMounted();
                            EjectUtilities.Eject(this, mounted);
                        }
                    }
                }
            }

            #endregion

            #region Lock

            void PlayWarningAlarm()
            {
                if (!playingRadarWarning)
                {
                    CancelInvoke(PlayWarningAlarm);
                    return;
                }

                Effect.server.Run(ALARM_SFX, cachedTransform.position);
            }

            void PlayLockAlarm()
            {
                if (!playingRadarLock)
                {
                    CancelInvoke(PlayLockAlarm);
                    return;
                }

                Effect.server.Run(ALARM_SFX, this.mountPoints[0].mountable.transform.position);
            }

            public void ClearRadarLock()
            {
                playingRadarLock = false;
                CancelInvoke(PlayLockAlarm);
            }

            public void ClearRadarWarning()
            {
                playingRadarWarning = false;
                CancelInvoke(PlayWarningAlarm);
            }

            #endregion

            #region Combat

            public BaseEntity WeaponControllerEntity { get { return this; } }
            public ISpecialAmmoContainer AmmoContainer { get; private set; }
            public bool UnlimitedAmmo { get { return Instance.configuration.ForceUnlimitedAmmo || HeliConfig.UnlimitedAmmo; } }
            public List<WeaponSystem> WeaponSystems { get; private set; } = new List<WeaponSystem>();
            public List<SpecialGun> SpecialGuns { get; private set; } = new List<SpecialGun>();
            public Transform Transform { get { return cachedTransform; } }
            public Vector3 Forward { get { return cachedTransform.forward; } }
            public bool HasAmmoContainer { get; private set; }
            public string NoAmmoToast { get; private set; }
            public float LastAmmoUpdate { get; set; }
            public float NextDryFireEffect { get; set; }

            public override void ScaleDamageForPlayer(BasePlayer player, HitInfo info)
            {
                if (VehicleConfig.DriverDamageScaling == 1 && VehicleConfig.PassengerDamageScaling == 1)
                {
                    base.ScaleDamageForPlayer(player, info);
                    return;
                }

                if (VehicleConfig.DriverDamageScaling == VehicleConfig.PassengerDamageScaling)
                {
                    info.damageTypes.ScaleAll(VehicleConfig.DriverDamageScaling);
                }
                else
                {
                    var isDriver = CustomHasDriver() ? player.net.ID.Value == CustomGetDriver().net.ID.Value : false;
                    if (isDriver)
                    {
                        if (VehicleConfig.DriverDamageScaling != 1)
                        {
                            info.damageTypes.ScaleAll(VehicleConfig.DriverDamageScaling);
                        }
                    }
                    else
                    {
                        if (VehicleConfig.PassengerDamageScaling != 1)
                        {
                            info.damageTypes.ScaleAll(VehicleConfig.PassengerDamageScaling);
                        }
                    }
                }

                base.ScaleDamageForPlayer(player, info);
            }

            public void OnJointUpdate()
            {
                tick++;
            }

            public override void OnServerWake()
            {
                if (hasBuoyancy)
                {
                    buoyancy.Wake();
                }
            }

            public override bool BuoyancySleep(bool inWater)
            {
                if (!hasBuoyancy)
                {
                    return false;
                }

                SetToKinematic();
                return true;
            }

            public override bool BuoyancyWake()
            {
                if (!hasBuoyancy)
                {
                    return false;
                }

                SetToNonKinematic();
                return true;
            }

            public override void OnHealthChanged(float oldvalue, float newvalue)
            {
                StatusUtilities.UpdateHealth(this);
                OnHealthChange?.Invoke(oldvalue, newvalue);
            }

            private void UpdateWeapons(BasePlayer driver)
            {
                if (!isPowered || !IsOn() || InSafeZone())
                {
                    return;
                }

                bool weaponsDisabled = false;
                if (HeliConfig.LandingGearSettings.Enabled && HeliConfig.LandingGearSettings.DisableWeaponsWhenOn && LandingGearOn)
                {
                    weaponsDisabled = true;
                }

                for (int i = 0; i < WeaponSystems.Count; i++)
                {
                    var ws = WeaponSystems[i];

                    var isFlare = ws.Config.ProjectileType == ProjectileType.Flare;
                    if (weaponsDisabled)
                    {
                        if (isFlare)
                        {
                            WeaponUtilities.UpdateWeapon(this, driver, ws);
                        }
                    }
                    else
                    {
                        WeaponUtilities.UpdateWeapon(this, driver, ws);
                    }
                }
            }

            public void InvokeStopSpecialGuns()
            {
                if (!invokeStopSpecialGunsStarted)
                {
                    invokeStopSpecialGunsStarted = true;
                    Invoke(StopSpecialGuns, SPECIAL_GUN_REPEAT_DELAY);
                }
            }

            void StopSpecialGuns()
            {
                foreach (var specialGun in SpecialGuns)
                {
                    specialGun.ToggleOff();
                }

                invokeStopSpecialGunsStarted = false;
            }

            public override void OnAttacked(HitInfo info)
            {
                var hasPrevAttack = lastServerProjectileAttack != null;
                if (info.ProjectileID > 0 && hasPrevAttack && info.ProjectileID != lastServerProjectileAttack.ProjectileID)
                {
                    return;
                }

                if (hasPrevAttack && lastServerProjectileAttack.ProjectileID <= 0 && info.ProjectileID <= 0 && info.PointStart == lastServerProjectileAttack.PointStart && info.ProjectileDistance == lastServerProjectileAttack.ProjectileDistance)
                {
                    return;
                }

                lastServerProjectileAttack = info;
                if (!invokingClearLastAttack)
                {
                    invokingClearLastAttack = true;
                    Invoke(ClearLastAttack, 0.001f);
                }

                if (info.damageTypes.Total() <= 0)
                {
                    info.damageTypes.Add(DamageType.Bullet, 20);
                }

                Hurt(info);
                if (HeliConfig.DoHitEffect && info.Initiator is BasePlayer attacker)
                {
                    Effect.reusableInstance.Init(Effect.Type.Generic, attacker.transform.position, Vector3.zero);
                    Effect.reusableInstance.pooledString = Instance.configuration.PlayerHitMarkerFx;
                    EffectNetwork.Send(Effect.reusableInstance, attacker.net.connection);
                }

                if (loseControlHealthThreshold > 0 && loseControlHealthThreshold >= health && !IsOnGround && !InWater)
                {
                    UpdateLoseControl(true);
                }
            }

            void ClearLastAttack()
            {
                lastServerProjectileAttack = null;
                invokingClearLastAttack = false;
            }

            public override void Hurt(HitInfo info)
            {
                if (IsDead())
                {
                    return;
                }

                var position = cachedTransform.position;
                var subs = this.net.group.subscribers;

                base.Hurt(info);

                if (IsDead())
                {
                    foreach (var fx in this.HeliConfig.ExplosionFxs)
                    {
                        Effect.server.Run(fx, position, Vector3.zero, targets: subs);
                    }

                    if (this.VehicleConfig.NukeOnExplosion && this.VehicleConfig.ExplosionRadius > 0 && this.VehicleConfig.NukeExplosionSteps > 0)
                    {
                        Instance.NextTick(() =>
                        {
                            ServerMgr.Instance.StartCoroutine(WeaponUtilities.DoNukeExplosion(creatorEntity, this, position, this.VehicleConfig.MinExplosionRadius, this.VehicleConfig.ExplosionRadius, this.VehicleConfig.NukeExplosionSteps, 1210222849, replaceTreeWithDeadVariantAtStep: this.VehicleConfig.NukeReplaceTreeWithDeadVariantAtStep));
                        });
                    }
                    else if (this.VehicleConfig.ExplosionDamage > 0 && this.VehicleConfig.ExplosionRadius > 0)
                    {
                        Instance.NextTick(() =>
                        {
                            DamageUtil.RadiusDamage(null, this, position, this.VehicleConfig.MinExplosionRadius, this.VehicleConfig.ExplosionRadius, new List<DamageTypeEntry>() { new DamageTypeEntry() { type = DamageType.Explosion, amount = this.VehicleConfig.ExplosionDamage } }, 1075980544, true);
                        });
                    }
                }
                else
                {
                    HurtWeakpoints(info);
                    InvokeLowHealthEffect(0.1f);
                }
            }


            // Copy from base.DoRepair, removed checks for repair enabled since repairing is false
            // to prevent exceptions from attempts to translate the custom vehicle prefab name
            // that would not exist in the translations
            public override void DoRepair(BasePlayer player)
            {
                if (Interface.CallHook("OnStructureRepair", this, player) != null)
                {
                    return;
                }

                float num = 30f;
                if (SecondsSinceAttacked <= num)
                {
                    OnRepairFailed(player, RecentlyDamagedError, (num - SecondsSinceAttacked).ToString("N0"));
                    return;
                }

                float num2 = MaxHealth() - Health();
                float num3 = num2 / MaxHealth();
                if (num2 <= 0f || num3 <= 0f)
                {
                    OnRepairFailed(player, NotDamagedError);
                    return;
                }

                List<ItemAmount> list = RepairCost(num3);
                if (list == null)
                {
                    return;
                }

                float num4 = list.Sum((ItemAmount x) => x.amount);
                float healthBefore = health;
                if (num4 > 0f)
                {
                    float num5 = list.Min(x => Mathf.Clamp01(player.inventory.GetAmount(x.itemid) / x.amount));
                    if (float.IsNaN(num5))
                    {
                        num5 = 0f;
                    }

                    num5 = Mathf.Min(num5, 50f / num2);
                    if (num5 <= 0f)
                    {
                        OnRepairFailedResources(player, list);
                        return;
                    }

                    int num6 = 0;
                    foreach (ItemAmount item in list)
                    {
                        int amount = Mathf.CeilToInt(num5 * item.amount);
                        int num7 = player.inventory.Take(null, item.itemid, amount);
                        Facepunch.Rust.Analytics.Azure.LogResource(Facepunch.Rust.Analytics.Azure.ResourceMode.Consumed, "repair_entity", item.itemDef.shortname, num7, this, null, safezone: false, null, player.userID);
                        if (num7 > 0)
                        {
                            num6 += num7;
                            player.Command("note.inv", item.itemid, num7 * -1);
                        }
                    }

                    float num8 = (float)num6 / num4;
                    health += num2 * num8;
                    RepairWeakpoints(num2);
                    SendNetworkUpdate();
                }
                else
                {
                    health += num2;
                    RepairWeakpoints(num2);
                    SendNetworkUpdate();
                }

                Facepunch.Rust.Analytics.Azure.OnEntityRepaired(player, this, healthBefore, health);
                if (Health() >= MaxHealth())
                {
                    OnRepairFinished(player);
                }
                else
                {
                    OnRepair();
                }

                InvokeLowHealthEffect(0.1f);
            }

            private void InvokeLowHealthEffect(float time)
            {
                if (!lowHealthEffectsValid)
                {
                    return;
                }

                invokingLowHealthFx = true;
                Invoke(UpdateLowHealthEffect, time);
            }

            private void UpdateLowHealthEffect()
            {
                invokingLowHealthFx = false;
                if (!lowHealthEffectsValid)
                {
                    return;
                }

                var spawnEffect = false;
                LowHealthEffectConfig spawnEffectConfig = null;

                if (!IsDestroyed)
                {
                    foreach (var kv in lowHealthThresholds)
                    {
                        if (kv.Item1 >= health)
                        {
                            spawnEffect = true;
                            spawnEffectConfig = kv.Item2;
                            break;
                        }
                    }
                }

                if (spawnEffect)
                {
                    if (spawnedLowHealthEffect != null)
                    {
                        if (spawnedLowHealthEffect == spawnEffectConfig)
                        {
                            return;
                        }

                        lowHealthPrefab.Kill(DestroyMode.None);
                    }

                    lowHealthPrefab = PropUtilities.CreateCustomEntity(spawnEffectConfig.Location, spawnEffectConfig.Rotation, spawnEffectConfig.Scale, spawnEffectConfig.PrefabPath, this, spawnEffectConfig.SkinId);
                    lowHealthPrefab.DestroyParentOnDestroy = false;
                    spawnedLowHealthEffect = spawnEffectConfig;
                }
                else if (spawnedLowHealthEffect != null)
                {
                    lowHealthPrefab.Kill(DestroyMode.None);
                    spawnedLowHealthEffect = null;
                }
            }


            private void InitializeLowHealthEffects()
            {
                if (HeliConfig.LowHealthEffectSettings == null || !HeliConfig.LowHealthEffectSettings.Enabled)
                {
                    return;
                }

                var effects = HeliConfig.LowHealthEffectSettings.Effects.OrderBy(e => e.HealthPercent).ToList();
                foreach (var effect in effects)
                {
                    if (!effect.Enabled)
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(effect.PrefabPath) || effect.HealthPercent <= 0)
                    {
                        continue;
                    }

                    var lowHealthThreshold = MaxHealth() * effect.HealthPercent;
                    if (lowHealthThreshold > 0)
                    {
                        lowHealthThresholds.Add(new Tuple<float, LowHealthEffectConfig>(lowHealthThreshold, effect));
                    }
                }

                lowHealthEffectsValid = lowHealthThresholds.Count > 0;
            }

            #endregion

            #region Props

            public void InitializeVehicle()
            {
                var config = this.HeliConfig;
                outsideDecayMinutes = Instance.configuration.ForceOutsideDecayMinutes ?? HeliConfig.OutsideDecayMinutes;
                insideDecayMinutes = Instance.configuration.ForceInsideDecayMinutes ?? HeliConfig.InsideDecayMinutes;
                timeAfterEngineOffToStartDecay = Instance.configuration.ForceTimeAfterEngineOffToStartDecay ?? HeliConfig.TimeAfterEngineOffToStartDecay;
                this.enableServerOcclusion = Instance.configuration.ForceServerOcclusion.HasValue ? Instance.configuration.ForceServerOcclusion.Value : config.EnableServerOcclusion;
                groundedDistanceCheck = HeliConfig.GroundedDistanceCheck;
                NoAmmoToast = config.GeneralToastSettings.Enabled && !string.IsNullOrEmpty(config.GeneralToastSettings.NoAmmoToast) ? config.GeneralToastSettings.NoAmmoToast : string.Empty;

                _maxHealth = config.MaxHealth;

                this.torqueScale = config.TorqueScale;
                this.engineThrustMax = config.EngineThrustMax;
                this.fuelPerSec = VehicleConfig.FuelSettings.FuelPerSec;
                this.rigidBody.sleepThreshold = config.SleepThreshold;
                this.rigidBody.mass = config.Mass;
                this.rigidBody.linearDamping = config.Drag;
                this.rigidBody.angularDamping = config.AngularDrag;
                this.rigidBody.maxDepenetrationVelocity = config.MaxDepenetrationVelocity;

                if (config.AutomaticCenterOfMass)
                {
                    this.rigidBody.automaticCenterOfMass = true;
                }
                else
                {
                    this.rigidBody.centerOfMass = config.CenterOfMass;
                }

                if (config.AutomaticInertiaTensor)
                {
                    this.rigidBody.automaticInertiaTensor = true;
                }
                else
                {
                    this.rigidBody.inertiaTensor = config.InertiaTensor;
                }

                this.rigidBody.maxAngularVelocity = config.MaxAngularVelocity;
                this.motorForceConstant = config.MotorForceConstant;

                this.hoverForceScale = config.HoverForceScale;
                this.liftDotMax = config.LiftDotMax;
                this.liftFraction = config.LiftDotFraction;
                this.bounds = new Bounds() { size = config.Bounds.Size, center = config.Bounds.Center };
                this.waterSample.localPosition += config.WaterSampleModifier;

                loseControlHealthThreshold = MaxHealth() * HeliConfig.LoseControlThreshold;

                this.mountAnchor = cachedTransform;

                InitializeWheelColliders();

                PropUtilities.InitializeVehicle(this);

                InitializeLowHealthEffects();

                InitializeChildrenCollections();
                if (HeliConfig.DisableGlobalNetwork)
                {
                    this.EnableGlobalBroadcast(false);
                }

                IsActive = true;
                vehicleNetId = this.net.ID.Value;
            }

            private void InitializeChildrenCollections()
            {
                var fuelCounters = new List<VehicleFuelCounter>();

                var vehicleChildren = cachedTransform.GetComponentsInChildren<BaseEntity>();
                for (int i = 0; i < vehicleChildren.Length; i++)
                {
                    var child = vehicleChildren[i];
                    if (child is SpecialStorageContainerProxy)
                    {
                        continue;
                    }

                    if (child is VehicleBoostContainer vbc)
                    {
                        if (VehicleConfig.BoostSettings.Enabled)
                        {
                            BoostContainer = vbc;
                            hasBoostStorage = true;
                        }

                        continue;
                    }

                    if (child is ISpecialAmmoContainer ammo)
                    {
                        AmmoContainer = ammo;
                        HasAmmoContainer = true;
                        continue;
                    }

                    if (child is VehicleFuelContainer vfc)
                    {
                        FuelContainer = vfc;
                        hasFuelStorage = true;
                        continue;
                    }

                    if (child is EngineStorageContainer es)
                    {
                        hasPhysicalEngine = true;
                        physicalEngine = es;
                        continue;
                    }

                    if (child is IStorageContainer ssc)
                    {
                        hasStorage = true;
                        StorageContainers.Add(ssc);
                        continue;
                    }

                    if (child is SpecialSleepingBag ssb)
                    {
                        hasRespawns = true;
                        respawnPoints.Add(ssb);
                        continue;
                    }

                    if (child is SpecialLight specialLight)
                    {
                        if (specialLight.CanVehicleToggle)
                        {
                            vehicleLights.Add(specialLight);
                        }

                        continue;
                    }

                    if (child is VehiclePowerButton || child is VehiclePowerSwitch)
                    {
                        hasSwitch = true;
                        cockpitElectical.Add(child);
                        continue;
                    }

                    if (child is Telephone tele)
                    {
                        hasRadio = true;
                        cockpitRadio = tele;
                        cockpitElectical.Add(child);
                        continue;
                    }

                    if (child is SpecialBroadcaster broadcaster)
                    {
                        hasBroadcaster = true;
                        radioBroadcaster = broadcaster;

                        if (Instance.configuration.DefaultRadioFrequency > 0)
                        {
                            radioBroadcaster.frequency = Instance.configuration.DefaultRadioFrequency;
                        }
                        else
                        {
                            radioBroadcaster.frequency = UnityEngine.Random.Range(1, 99999);
                        }


                        cockpitElectical.Add(child);
                        continue;
                    }

                    if ((VehicleConfig.DisableCounterUpdates || Instance.configuration.GlobalDisableCounterUpdates) && child is SpecialCounter)
                    {
                        child.enabled = false;
                    }

                    if (child is SpecialIOEntity || child is IOEntity)
                    {
                        if (child is DeployableBoomBox boombox && !VehicleConfig.BoomboxSettings.RequireVehiclePower)
                        {
                            using (FlagsUpdateScope flagsUpdateScope = boombox.StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                            {
                                flagsUpdateScope.Set(BaseEntity.Flags.Reserved8, true);
                            }

                            continue;
                        }

                        else if (child is VehicleFuelCounter fuelCounter)
                        {
                            fuelCounters.Add(fuelCounter);
                        }
                        else if (child is SpecialAudioVisualisationEntity ave && !ave.CanVehicleToggle)
                        {
                            continue;
                        }

                        cockpitElectical.Add(child);
                        continue;
                    }

                    if (child is SpecialGun specialGun && object.ReferenceEquals(child.transform.GetComponentInParent<Gimbal>(), null) && object.ReferenceEquals(child.transform.GetComponentInParent<SpecialComputerStation>(), null))
                    {
                        SpecialGuns.Add(specialGun);
                        continue;
                    }

                    if (child is SpecialFakeEngine specialFakeEngine)
                    {
                        fakeEngines.Add(specialFakeEngine);
                        continue;
                    }

                    if (child is CodeLock cl)
                    {
                        codeLock = cl;
                        hasCodeLock = true;
                        centralLockingEnabled = true;
                        continue;
                    }
                }

                if (VehicleConfig.Bounciness > -1 || VehicleConfig.DynamicFriction > -1)
                {
                    var colliders = cachedTransform.GetComponentsInChildren<Collider>();
                    for (int i = 0; i < colliders.Length; i++)
                    {
                        var col = colliders[i];
                        if (col.isTrigger)
                        {
                            continue;
                        }

                        if (col is WheelCollider)
                        {
                            continue;
                        }

                        if (VehicleConfig.Bounciness > -1)
                        {
                            col.material.bounciness = VehicleConfig.Bounciness;
                            col.material.bounceCombine = VehicleConfig.BounceCombine;
                        }

                        if (VehicleConfig.DynamicFriction > -1)
                        {
                            col.material.dynamicFriction = VehicleConfig.DynamicFriction;
                            col.material.frictionCombine = VehicleConfig.FrictionCombine;
                        }
                    }
                }

                var fuelSystem = new KaruzaVehicleFuelSystem(VehicleConfig.FuelSettings.FuelSource, FuelContainer);
                for (int i = 0; i < fuelCounters.Count; i++)
                {
                    var fc = fuelCounters[i];
                    fc.SetFuelSystem(fuelSystem);
                }

                if (VehicleConfig.FuelSettings?.FuelSource == FuelSource.Container && VehicleConfig.FuelSettings.FuelContainer != null && VehicleConfig.FuelSettings.FuelContainer.FuelModifiers.Count > 0)
                {
                    for (int i = 0; i < VehicleConfig.FuelSettings.FuelContainer.FuelModifiers.Count; i++)
                    {
                        var fm = VehicleConfig.FuelSettings.FuelContainer.FuelModifiers[i];
                        fuelModifiers.Add(fm.SkinId, fm);
                    }

                    fuelSystem.OnFuelUsed += OnFuelUsed;
                }

                engineController = new VehicleEngineController<PlayerHelicopter>(this, fuelSystem, true, HeliConfig.EngineStartupTime, waterSample, Flags.Reserved4);

                if (!this.loadedFromSave)
                {
                    if (hasStorage)
                    {
                        for (int i = 0; i < StorageContainers.Count; i++)
                        {
                            var storageContainer = StorageContainers[i];
                            var storageContainerConfig = storageContainer.Config;

                            if (storageContainerConfig.DefaultItems == null)
                            {
                                continue;
                            }

                            for (int n = 0; n < storageContainerConfig.DefaultItems.Count; n++)
                            {
                                var di = storageContainerConfig.DefaultItems[n];
                                var item = ItemUtilities.SpawnItem(di.ShortName, di.Amount, di.SkinId);
                                storageContainer.inventory.GiveItem(item);
                            }
                        }
                    }

                    if (hasPhysicalEngine)
                    {
                        if (physicalEngine.Config.DefaultItems?.Count > 0)
                        {
                            for (int i = 0; i < physicalEngine.Config.DefaultItems.Count; i++)
                            {
                                var di = physicalEngine.Config.DefaultItems[i];
                                for (int n = 0; n < di.Amount; n++)
                                {
                                    var item = ItemUtilities.SpawnItem(di.ShortName, 1, di.SkinId);
                                    physicalEngine.inventory.GiveItem(item);
                                }
                            }
                        }
                    }

                    if (hasFuelStorage)
                    {
                        for (int i = 0; i < FuelContainer.Config.DefaultItems.Count; i++)
                        {
                            var di = FuelContainer.Config.DefaultItems[i];
                            if (di.Amount <= 0)
                            {
                                continue;
                            }

                            var item = ItemUtilities.SpawnItem(di.ShortName, di.Amount, di.SkinId);
                            FuelContainer.inventory.GiveItem(item);
                        }
                    }

                    if (hasBoostStorage)
                    {
                        for (int i = 0; i < BoostContainer.Config.DefaultItems?.Count; i++)
                        {
                            var di = BoostContainer.Config.DefaultItems[i];
                            if (di.Amount <= 0)
                            {
                                continue;
                            }

                            var item = ItemUtilities.SpawnItem(di.ShortName, di.Amount, di.SkinId);
                            BoostContainer.inventory.GiveItem(item);
                        }
                    }

                    if (HasAmmoContainer)
                    {
                        for (int i = 0; i < AmmoContainer.Config.DefaultItems?.Count; i++)
                        {
                            var di = AmmoContainer.Config.DefaultItems[i];
                            if (di.Amount <= 0)
                            {
                                continue;
                            }

                            var item = ItemUtilities.SpawnItem(di.ShortName, di.Amount, di.SkinId);
                            AmmoContainer.inventory.GiveItem(item);
                        }
                    }
                }

                if (fakeEngines != null && fakeEngines.Count > 0)
                {
                    this.hasEngine = true;
                }

                buoyancy = GetComponent<Buoyancy>();
                hasBuoyancy = buoyancy != null;

                var scs = cachedTransform.GetComponentsInChildren<SpecialComputerStation>();
                for (int i = 0; i < scs.Length; i++)
                {
                    var cs = scs[i];
                    cs.ConfigureWeaponController(this);
                }

                var gimbals = cachedTransform.GetComponentsInChildren<Gimbal>();
                for (int i = 0; i < gimbals.Length; i++)
                {
                    var g = gimbals[i];
                    g.ConfigureWeaponController(this);
                }

                PrepareWeakspots();
            }

            private void InitializeWheelColliders()
            {
                if (HeliConfig.WheelColliders.Count <= 0)
                {
                    wheels = new Wheel[0];
                    return;
                }

                var wheelList = new List<Wheel>();
                for (int i = 0; i < HeliConfig.WheelColliders.Count; i++)
                {
                    var wcc = HeliConfig.WheelColliders[i];
                    var wc = PropUtilities.InitializeWheelCollider(this, wcc, out BaseEntity visWheel);

                    var wheel = new Wheel()
                    {
                        wheelCollider = wc,
                        steering = wcc.Steering,
                    };

                    wheelList.Add(wheel);
                }

                wheels = wheelList.ToArray();
            }

            #endregion

            #region Power

            public bool TogglePower(BasePlayer player)
            {
                if (VehicleConfig.PowerSettings.RequireDriver)
                {
                    var driver = CustomGetDriver();
                    var isDriver = driver != null ? player.net.ID.Value == driver.net.ID.Value : false;
                    if (!isDriver)
                    {
                        return false;
                    }
                }

                TogglePower(!isPowered);
                return true;
            }

            public void TogglePower(bool on, bool force = false)
            {
                var hasDriver = CustomHasDriver();
                if (VehicleConfig.PowerSettings.RequireDriver)
                {
                    if (!force && on && !hasDriver)
                    {
                        on = false;
                    }
                }

                foreach (var ele in cockpitElectical)
                {
                    if (ele is DeployableBoomBox)
                    {
                        continue;
                    }

                    using (FlagsUpdateScope flagsUpdateScope = ele.StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(BaseEntity.Flags.Reserved8, on, true);
                    }
                }

                if (hasDriver)
                {
                    var driver = CustomGetDriver();
                    if (on)
                    {
                        if (hasRadio && !driver.IsNpc && !driver.IsBot)
                        {
                            if (cockpitRadio.Controller.currentPlayer?.userID.Get() != driver.userID.Get())
                            {
                                if (cockpitRadio.Controller.currentPlayer != null)
                                {
                                    Radio.RemoveRadio(cockpitRadio.Controller.currentPlayer);
                                    cockpitRadio.Controller.currentPlayer.SetActiveTelephone(null);
                                }

                                cockpitRadio.Controller.currentPlayer = driver;

                                using (FlagsUpdateScope flagsUpdateScope = cockpitRadio.StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                                {
                                    flagsUpdateScope.Set(BaseEntity.Flags.Busy, true);
                                }
                            }

                            cockpitRadio.Controller.SetPhoneStateWithPlayer(Telephone.CallState.InProcess);

                            //ActiveRadios.Add(cockpitRadio.net.ID.Value, this);
                            Radio.RegisterRadio(driver, this);
                        }
                    }
                    else
                    {
                        this.currentInputState.Reset();
                        if (cockpitRadio != null)
                        {
                            if (driver == null && recentDrivers.Count > 0)
                            {
                                driver = recentDrivers.Peek();
                            }

                            cockpitRadio.Controller.ServerHangUp();
                            //ActiveRadios.Remove(cockpitRadio.net.ID.Value);
                            Radio.RemoveRadio(driver);
                        }
                    }
                }

                isPowered = on;
                OnPowerToggle?.Invoke(isPowered);
                if (VehicleConfig.PowerSettings.TogglesLight)
                {
                    CustomLightToggle();
                }
            }

            #endregion

            #region Mounting

            public override bool GetDismountPosition(BasePlayer player, out Vector3 res, bool silent = false)
            {
                if (HeliConfig.CustomDismountPositions != null)
                {
                    Vector3 dismountCheckStart = GetDismountCheckStart(player);
                    List<Vector3> validDismounts = new List<Vector3>();

                    for (int i = 0; i < HeliConfig.CustomDismountPositions.Count; i++)
                    {
                        var dmp = HeliConfig.CustomDismountPositions[i];

                        var disPos = cachedTransform.TransformPoint(dmp.Location);
                        if (Physics.CheckSphere(disPos, 0.5f, 1537319169))
                        {
                            continue;
                        }

                        Vector3 position = disPos + cachedTransform.up * 0.5f;
                        if (!IsVisibleAndCanSee(position))
                        {
                            continue;
                        }

                        Vector3 vector = disPos + BasePlayer.NoClipOffset();
                        if (AntiHack.TestNoClipping(player, dismountCheckStart, vector, BasePlayer.NoClipRadius(ConVar.AntiHack.noclip_margin_dismount), ConVar.AntiHack.noclip_backtracking, out dismountCollider, overlapVehicleLayer: false, this))
                        {
                            continue;
                        }

                        validDismounts.Add(disPos);
                    }

                    if (validDismounts.Count > 0)
                    {
                        Vector3 pos = player.transform.position;
                        validDismounts.Sort((Vector3 a, Vector3 b) => Vector3.Distance(a, pos).CompareTo(Vector3.Distance(b, pos)));
                        res = validDismounts[0];
                        return true;
                    }
                }

                return base.GetDismountPosition(player, out res, silent);
            }

            public override bool ValidDismountPosition(BasePlayer player, Vector3 disPos)
            {
                if (!Physics.CheckCapsule(disPos + DISMOUNT_START_MODIFIER, disPos + DISMOUNT_END_MODIFIER, 0.5f, 1537319169))
                {
                    return true;
                }

                return false;
            }

            public BasePlayer CustomGetDriver()
            {
                if (driverCache.Count <= 0)
                {
                    return null;
                }

                return driverCache[0] as BasePlayer;
            }

            public bool CustomHasDriver()
            {
                return driverCache.Count > 0;
            }

            private void TryRemoveDriver(BasePlayer player)
            {
                if (!driverCache.Contains(player.net.ID.Value))
                {
                    return;
                }

                driverCache.Remove(player.net.ID.Value);
            }

            private bool TryAddDriver(BasePlayer player)
            {
                var isDriver = false;
                foreach (MountPointInfo allMountPoint in allMountPoints)
                {
                    if (!allMountPoint.isDriver)
                    {
                        continue;
                    }

                    var mount = allMountPoint.mountable;
                    if (!mount.IsMounted())
                    {
                        continue;
                    }

                    var mounted = mount.GetMounted();
                    if (mounted != null && mounted.net.ID.Value == player.net.ID.Value)
                    {
                        isDriver = true;
                        break;
                    }
                }

                if (isDriver)
                {
                    if (!driverCache.Contains(player.net.ID.Value))
                    {
                        driverCache.Add(player.net.ID.Value, player);
                    }

                    return true;
                }

                TryRemoveDriver(player);
                return false;
            }

            public override void PlayerDismounted(BasePlayer player, BaseMountable seat)
            {
                base.PlayerDismounted(player, seat);
                StatusUtilities.HideHealth(player);

                var wasDriver = false;
                if (CustomHasDriver())
                {
                    var driver = CustomGetDriver();
                    wasDriver = driver.net.ID.Value == player.net.ID.Value;
                }

                Instance.NextTick(() =>
                {
                    if (IsDestroyed || Instance.IsUnloading || !IsActive || IsDead())
                    {
                        if (!player.IsNpc && !player.IsBot)
                        {
                            Radio.RemoveRadio(player);
                        }

                        return;
                    }

                    OnMountedChange?.Invoke();
                    StatusUtilities.UpdateHealth(this);

                    var isStillDriver = TryUpdateDriver(player);
                    if (isStillDriver)
                    {
                        return;
                    }

                    if (hasRadio && !player.IsNpc && !player.IsBot)
                    {
                        Radio.RemoveRadio(player);

                        if (cockpitRadio.Controller.currentPlayer != null && player.net.ID.Value == cockpitRadio.Controller.currentPlayer.net.ID.Value)
                        {
                            cockpitRadio.Controller.currentPlayer.SetActiveTelephone(null);
                            cockpitRadio.Controller.ServerHangUp();
                        }
                    }

                    if ((wasDriver && IsOnGround) || player.IsNpc || player.IsBot)
                    {
                        TryToggleOff();
                    }

                    BoostUtilities.TryDisableBoost(this);

                    if (!player.isMounted)
                    {
                        EjectUtilities.TryEject(this, player);
                    }
                });

                if (!IsOnGround)
                {
                    Invoke(TryToggleOff, 3);
                }
            }

            void TryToggleOff()
            {
                if (VehicleConfig.PowerSettings.RequireDriver)
                {
                    TogglePower(CustomHasDriver());
                }
                else
                {
                    TogglePower(AnyMounted());
                }
            }

            public override void PlayerMounted(BasePlayer player, BaseMountable mountPoint)
            {
                base.PlayerMounted(player, mountPoint);
                TryUpdateDriver(player);
                StatusUtilities.UpdateHealth(this);
                OnMountedChange?.Invoke();
            }

            private bool TryUpdateDriver(BasePlayer player)
            {
                var isDriver = TryAddDriver(player);
                if (isDriver && !hasSwitch)
                {
                    isPowered = true;
                    TogglePower(true);
                }

                return isDriver;
            }

            public bool CanSwapToSeat(BasePlayer player, BaseMountable mount)
            {
                var hasAccessToLock = !hasCodeLock || !codeLock.IsLocked() || (codeLock.whitelistPlayers.Contains(player.userID.Get()) || codeLock.guestPlayers.Contains(player.userID.Get()));

                var filterDriver = false;

                if (hasAccessToLock)
                {
                    return true;
                }

                var hasDriver = CustomHasDriver();
                if (!hasDriver)
                {
                    return false;
                }

                var driver = CustomGetDriver();
                if (player.userID.Get() != driver.userID.Get())
                {
                    if (driver.Team == null || player.Team == null)
                    {
                        filterDriver = true;
                    }
                    else if (!driver.Team.members.Contains(player.userID.Get()))
                    {
                        filterDriver = true;
                    }
                }

                if (!filterDriver)
                {
                    return true;
                }

                if (centralLockingEnabled)
                {
                    return false;
                }

                foreach (MountPointInfo allMountPoint in allMountPoints)
                {
                    if (allMountPoint.mountable.net.ID.Value != mount.net.ID.Value)
                    {
                        continue;
                    }

                    if (allMountPoint.isDriver)
                    {
                        return !filterDriver;
                    }

                    return true;
                }

                return false;
            }

            public override void AttemptMount(BasePlayer player, bool doMountChecks = true)
            {
                if (_mounted != null || !MountEligable(player))
                {
                    return;
                }

                BaseMountable idealMountPointFor = CustomGetIdealMountPointFor(player.eyes.position, player.eyes.position + player.eyes.HeadForward() * 1f, player);
                if (idealMountPointFor == null)
                {
                    return;
                }

                if (idealMountPointFor == this)
                {
                    base.AttemptMount(player, doMountChecks);
                }
                else
                {
                    idealMountPointFor.MountPlayer(player);
                }
            }

            public BaseMountable CustomGetIdealMountPointFor(Vector3 eyePos, Vector3 pos, BasePlayer player)
            {
                var hasDriver = CustomHasDriver();
                var hasAccessToLock = !hasCodeLock || !codeLock.IsLocked() || (codeLock.whitelistPlayers.Contains(player.userID) || codeLock.guestPlayers.Contains(player.userID));
                var filterDriver = false;

                if (!hasAccessToLock)
                {
                    if (!hasDriver)
                    {
                        return null;
                    }

                    var driver = CustomGetDriver();
                    if (player.userID.Get() != driver.userID.Get())
                    {
                        if (driver.Team == null || player.Team == null)
                        {
                            filterDriver = true;
                        }
                        else if (!driver.Team.members.Contains(player.userID.Get()))
                        {
                            filterDriver = true;
                        }
                    }

                    if (filterDriver && centralLockingEnabled)
                    {
                        return null;
                    }
                }

                if (OnlyOwnerAccessible() && !filterDriver && creatorEntity.net.ID.Value != player.net.ID.Value)
                {
                    return null;
                }

                if (!filterDriver && !hasDriver)
                {
                    for (int i = 0; i < mountPoints.Count; i++)
                    {
                        var mountPoint = mountPoints[i];
                        if (!mountPoint.isDriver)
                        {
                            continue;
                        }

                        if (!mountPoint.mountable.DirectlyMountable())
                        {
                            continue;
                        }

                        if (mountPoint.mountable.IsMounted())
                        {
                            continue;
                        }

                        return mountPoint.mountable;
                    }
                }

                BaseMountable result = null;
                float num = float.PositiveInfinity;
                foreach (MountPointInfo allMountPoint in allMountPoints)
                {
                    if (allMountPoint.mountable.AnyMounted())
                    {
                        continue;
                    }

                    if (!allMountPoint.mountable.DirectlyMountable())
                    {
                        continue;
                    }

                    float num2 = Vector3.Distance(allMountPoint.mountable.mountAnchor.position, pos);
                    if (num2 > num)
                    {
                        continue;
                    }

                    if (IsSeatClipping(allMountPoint.mountable))
                    {
                        if (UnityEngine.Application.isEditor)
                        {
                            Debug.Log($"Skipping seat {allMountPoint.mountable} - it's clipping");
                        }
                    }
                    else if (!IsSeatVisible(allMountPoint.mountable, eyePos))
                    {
                        if (UnityEngine.Application.isEditor)
                        {
                            Debug.Log($"Skipping seat {allMountPoint.mountable} - it's not visible");
                        }
                    }
                    else if (!IsSeatClipping(allMountPoint.mountable) && IsSeatVisible(allMountPoint.mountable, eyePos))
                    {
                        if (filterDriver && allMountPoint.isDriver)
                        {
                            continue;
                        }

                        result = allMountPoint.mountable;
                        num = num2;
                    }
                }

                return result;
            }

            #endregion

            #region Boost

            public bool IsBoosting { get; set; }
            public float LastBoostTime { get; set; }
            public Action<bool> OnBoostToggle { get; set; }
            public KaruzaVehicleFuelSystem BoostFuelSystem { get; set; }

            public void UpdateBoost(bool isBoosting)
            {
                if (IsBoosting == isBoosting)
                {
                    return;
                }

                IsBoosting = isBoosting;
                OnBoostToggle?.Invoke(IsBoosting);

                if (IsBoosting)
                {
                    engineThrustMax = HeliConfig.EngineThrustMax * HeliConfig.BoostSettings.EnginePowerModifier;
                }
                else
                {
                    engineThrustMax = HeliConfig.EngineThrustMax;
                }
            }

            #endregion

            #region Fuel Modifiers

            void OnFuelUsed(int fuelUsed, Item item)
            {
                if (currentFuelModifierSkinId == item.skin)
                {
                    return;
                }

                if (!fuelModifiers.ContainsKey(item.skin))
                {
                    currentFuelModifierSkinId = 0;
                }
                else
                {
                    currentFuelModifierSkinId = item.skin;
                }

                AdjustFuelModifier();
            }

            void AdjustFuelModifier()
            {
                if (currentFuelModifierSkinId == 0)
                {
                    engineThrustMax = HeliConfig.EngineThrustMax;
                    fuelPerSec = VehicleConfig.FuelSettings.FuelPerSec;
                    return;
                }

                var fuelModifier = fuelModifiers[currentFuelModifierSkinId];
                engineThrustMax = HeliConfig.EngineThrustMax * fuelModifier.SpeedMultiplier;

                if (fuelModifier.FuelPerSec < 0)
                {
                    fuelPerSec = VehicleConfig.FuelSettings.FuelPerSec;
                }
                else
                {
                    fuelPerSec = fuelModifier.FuelPerSec;
                }
            }

            #endregion

            #region GForce

            Vector3 lastFrameVelocity;
            Vector3 currentVelocity;
            bool blackoutThresholdExceeded;
            bool redoutThresholdExceeded;
            public void UpdateGForces(bool isMounted)
            {
                if (!VehicleConfig.GForceSettings.Enabled)
                {
                    return;
                }

                if (!isMounted)
                {
                    return;
                }

                currentVelocity = rigidBody.linearVelocity;
                var gForce = (currentVelocity - lastFrameVelocity) / (Time.deltaTime * Physics.gravity.magnitude);
                var signedG = Vector3.Dot(gForce, cachedTransform.up);
                lastFrameVelocity = currentVelocity;

                if (signedG <= VehicleConfig.GForceSettings.RedoutThreshold)
                {
                    if (!redoutThresholdExceeded)
                    {
                        redoutThresholdExceeded = true;
                        for (int i = 0; i < mountPoints.Count; i++)
                        {
                            var mp = mountPoints[i];
                            if (!mp.mountable.AnyMounted())
                            {
                                continue;
                            }

                            var mountedPlayer = mp.mountable._mounted;
                            GForceGUIUtilities.ShowRedoutGUI(mountedPlayer);

                            if (VehicleConfig.GForceSettings.DamagePlayer && mountedPlayer.metabolism.oxygen.value > 0.75f)
                            {
                                mountedPlayer.metabolism.oxygen.SetValue(0.75f);
                                mountedPlayer.metabolism.SendChanges();
                            }
                        }
                    }
                }
                else if (redoutThresholdExceeded)
                {
                    redoutThresholdExceeded = false;
                    for (int i = 0; i < mountPoints.Count; i++)
                    {
                        var mp = mountPoints[i];
                        if (!mp.mountable.AnyMounted())
                        {
                            continue;
                        }

                        var mountedPlayer = mp.mountable._mounted;
                        GForceGUIUtilities.HideRedoutGUI(mountedPlayer);
                    }
                }


                if (signedG >= VehicleConfig.GForceSettings.BlackoutThreshold)
                {
                    if (!blackoutThresholdExceeded)
                    {
                        blackoutThresholdExceeded = true;
                        for (int i = 0; i < mountPoints.Count; i++)
                        {
                            var mp = mountPoints[i];
                            if (!mp.mountable.AnyMounted())
                            {
                                continue;
                            }

                            var mountedPlayer = mp.mountable._mounted;
                            GForceGUIUtilities.ShowBlackoutGUI(mountedPlayer);

                            if (VehicleConfig.GForceSettings.DamagePlayer && mountedPlayer.metabolism.oxygen.value > 0.75f)
                            {
                                mountedPlayer.metabolism.oxygen.SetValue(0.75f);
                                mountedPlayer.metabolism.SendChanges();
                            }
                        }
                    }
                }
                else if (blackoutThresholdExceeded)
                {
                    blackoutThresholdExceeded = false;
                    for (int i = 0; i < mountPoints.Count; i++)
                    {
                        var mp = mountPoints[i];
                        if (!mp.mountable.AnyMounted())
                        {
                            continue;
                        }

                        var mountedPlayer = mp.mountable._mounted;
                        GForceGUIUtilities.HideBlackoutGUI(mountedPlayer);
                    }
                }
            }

            #endregion

            #region Landing Gear

            bool autoRetractReady;
            bool landingGearToastDisplayed;

            public bool LandingGearOn { get; set; }
            public Action<bool> OnLandingGearToggle { get; set; }
            public LandingGearSettings LandingGearSettings { get { return HeliConfig.LandingGearSettings; } }

            public void ToggleLandingGear(bool wantsOn)
            {
                if (!HeliConfig.LandingGearSettings.Enabled)
                {
                    LandingGearOn = true;
                    return;
                }

                if (LandingGearOn == wantsOn)
                {
                    return;
                }

                LandingGearOn = wantsOn;
                groundedDistanceCheck = HeliConfig.GroundedDistanceCheck;

                if (wantsOn && HeliConfig.LandingGearSettings.GroundedDistanceCheckWhileOn > -1)
                {
                    groundedDistanceCheck = HeliConfig.LandingGearSettings.GroundedDistanceCheckWhileOn;
                }

                if (IsOnGround)
                {
                    if (LandingGearOn)
                    {
                        landingGearToastDisplayed = false;
                        autoRetractReady = true;
                    }
                }
                else if (!LandingGearOn)
                {
                    if (!landingGearToastDisplayed)
                    {
                        landingGearToastDisplayed = true;
                    }

                    autoRetractReady = false;
                }

                if (rigidBody.IsSleeping())
                {
                    rigidBody.WakeUp();
                }

                if (HeliConfig.LandingGearSettings.UpdateWheelColliders)
                {
                    var curVelocity = rigidBody.linearVelocity;

                    for (int i = 0; i < wheels.Length; i++)
                    {
                        var wheel = wheels[i];
                        wheel.wheelCollider.gameObject.SetActive(LandingGearOn);
                    }

                    rigidBody.linearVelocity = curVelocity;
                }

                OnLandingGearToggle?.Invoke(wantsOn);
            }

            #endregion

            #region Flight

            public override void PlayerServerInput(InputState inputState, BasePlayer player)
            {
                var driver = CustomGetDriver();
                if (object.ReferenceEquals(driver, null) || driver.net.ID.Value != player.net.ID.Value)
                {
                    PassengerInput(inputState, player);
                }
                else
                {
                    if (!autoHover)
                    {
                        PilotInput(inputState, player);
                    }
                }
            }

            public override void PilotInput(InputState inputState, BasePlayer player)
            {
                currentInputState.Reset();
                currentInputState.throttle = (inputState.IsDown(BUTTON.FORWARD) ? 1f : 0f);
                currentInputState.throttle -= ((inputState.IsDown(BUTTON.BACKWARD) || inputState.IsDown(BUTTON.DUCK)) ? 1f : 0f);
                currentInputState.pitch = inputState.current.mouseDelta.y;
                currentInputState.roll = 0f - inputState.current.mouseDelta.x;
                currentInputState.yaw = (inputState.IsDown(BUTTON.RIGHT) ? 1f : 0f);
                currentInputState.yaw -= (inputState.IsDown(BUTTON.LEFT) ? 1f : 0f);
                currentInputState.pitch = HelperUtilities.MouseToBinary(currentInputState.pitch, -1f, 1f);
                currentInputState.roll = HelperUtilities.MouseToBinary(currentInputState.roll, -1f, 1f);
                lastPlayerInputTime = Time.time;

                if (isPowered && !IsOn() && !IsStartingUp && inputState.IsDown(BUTTON.FORWARD) && (!hasPhysicalEngine || physicalEngine.IsUsable) && (!hasWeakpoints || !Weakspots.Exists(wp => wp.LoseControlOnDeath && wp.IsDestroyed)))
                {
                    engineController.TryStartEngine(player);
                }

                currentInputState.groundControl = inputState.IsDown(BUTTON.DUCK);
                if (currentInputState.groundControl)
                {
                    currentInputState.roll = 0f;
                    currentInputState.throttle = (inputState.IsDown(BUTTON.FORWARD) ? 1f : 0f);
                    currentInputState.throttle -= (inputState.IsDown(BUTTON.BACKWARD) ? 1f : 0f);
                }

                cachedRoll = currentInputState.roll;
                cachedYaw = currentInputState.yaw;
                cachedPitch = currentInputState.pitch;

                UpdateWeapons(player);
                BoostUtilities.UpdateBoost(this, player);
            }

            public override void VehicleFixedUpdate()
            {
                var isEngineOn = IsEngineOn();
                if (hasEngine)
                {
                    if (IsStartingUp || isEngineOn)
                    {
                        if (!isFakeEnginesOn)
                        {
                            for (int i = 0; i < fakeEngines.Count; i++)
                            {
                                var fakeEngine = fakeEngines[i];

                                using (FlagsUpdateScope flagsUpdateScope = fakeEngine.StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                                {
                                    flagsUpdateScope.Set(BaseEntity.Flags.On, true);
                                }
                            }

                            isFakeEnginesOn = true;
                        }
                    }
                    else if (isFakeEnginesOn)
                    {
                        for (int i = 0; i < fakeEngines.Count; i++)
                        {
                            var fakeEngine = fakeEngines[i];

                            using (FlagsUpdateScope flagsUpdateScope = fakeEngine.StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                            {
                                flagsUpdateScope.Set(BaseEntity.Flags.On, false);
                            }
                        }

                        isFakeEnginesOn = false;
                    }
                }

                if (Time.time > lastPlayerInputTime + 0.5f)
                {
                    SetDefaultInputState();
                }

                var isMounted = IsMounted();
                if (!HeliConfig.DisableGlobalNetwork)
                {
                    EnableGlobalBroadcast(isPowered || loseControl || isEngineOn || isMounted || (!rigidBody.IsSleeping() && (!IsOnWater || rigidBody.linearVelocity.magnitude > 0.5f)));
                }
                
                if (isEngineOn || ForceMovementHandling)
                {
                    MovementUpdate();
                }

                UpdateGForces(isMounted);

                GameObject[] array = killTriggers;
                foreach (GameObject obj in array)
                {
                    bool active = rigidBody.linearVelocity.y < 0f;
                    obj.SetActive(active);
                }

                engineController.CheckEngineState();
                engineController.TickFuel(fuelPerSec);

                if (OnlyOwnerAccessible() && safeAreaRadius != -1f && Vector3.Distance(cachedTransform.position, safeAreaOrigin) > safeAreaRadius)
                {
                    ClearOwnerEntry();
                }
            }

            public void UpdateLoseControl(bool loseControl)
            {
                this.loseControl = loseControl;
            }

            public override void MovementUpdate()
            {
                var isOnGround = IsOnGround;
                if (isOnGround)
                {
                    UpdateLoseControl(false);
                    if (wheels.Length != 0)
                    {
                        ApplyForceAtWheels();
                    }
                    else
                    {
                        ApplyForceWithoutWheels();
                    }
                }
                else if (InWater)
                {
                    UpdateLoseControl(false);
                }

                if (!currentInputState.groundControl || !isOnGround)
                {
                    HelicopterInputState helicopterInputState = currentInputState;
                    if (autoHover)
                    {
                        float num = 50f - cachedTransform.position.y;
                        helicopterInputState.throttle = Mathf.Clamp(num * 0.01f, -1f, 1f);
                        helicopterInputState.pitch = 0f;
                        helicopterInputState.roll = 0f;
                        helicopterInputState.yaw = 0f;
                    }
                    else if (IsBoosting)
                    {
                        helicopterInputState.throttle = 1;
                    }

                    var localEngineThrust = engineThrustMax;
                    var localTorqueScale = torqueScale;
                    var localLiftFraction = liftFraction;
                    if (hasPhysicalEngine && !loseControl)
                    {
                        localEngineThrust *= physicalEngine.PerformanceFractionTopSpeed;
                        localTorqueScale *= physicalEngine.PerformanceFractionAcceleration;
                        localLiftFraction *= physicalEngine.PerformanceFractionAcceleration;
                    }

                    var torqueScaleY = localTorqueScale.y;
                    if (helicopterInputState.groundControl)
                    {
                        currentThrottle = -0.75f;
                    }
                    else
                    {
                        if (loseControl)
                        {
                            helicopterInputState.throttle = -2;
                            helicopterInputState.yaw = 2f;
                            torqueScaleY *= 2.0f;
                        }

                        currentThrottle = Mathf.Lerp(currentThrottle, helicopterInputState.throttle, 2f * Time.fixedDeltaTime);
                        currentThrottle = Mathf.Clamp(currentThrottle, -0.8f, 1f);
                        if (helicopterInputState.pitch != 0f || helicopterInputState.roll != 0f || helicopterInputState.yaw != 0f)
                        {
                            rigidBody.AddRelativeTorque(new Vector3(helicopterInputState.pitch * localTorqueScale.x, helicopterInputState.yaw * torqueScaleY, helicopterInputState.roll * localTorqueScale.z), ForceMode.Force);
                        }
                    }

                    if (damageTorque != Vector3.zero)
                    {
                        rigidBody.AddRelativeTorque(new Vector3(damageTorque.x, damageTorque.y, damageTorque.z), ForceMode.Force);
                    }

                    avgThrust = Mathf.Lerp(avgThrust, localEngineThrust * currentThrottle, Time.fixedDeltaTime * thrustLerpSpeed);
                    float value = Mathf.Clamp01(Vector3.Dot(cachedTransform.up, Vector3.up));
                    float num2 = Mathf.InverseLerp(liftDotMax, 1f, value);
                    float serviceCeiling = GetServiceCeiling();
                    float b = Mathf.Max(GetMinimumAltitudeTerrain(), TerrainMeta.HeightMap.GetHeight(cachedTransform.position));
                    avgTerrainHeight = Mathf.Lerp(avgTerrainHeight, b, Time.deltaTime);
                    float num3 = 1f - Mathf.InverseLerp(avgTerrainHeight + serviceCeiling - 20f, avgTerrainHeight + serviceCeiling, cachedTransform.position.y);
                    num2 *= num3;
                    float num4 = 1f - Mathf.InverseLerp(altForceDotMin, 1f, value);
                    Vector3 force = Vector3.up * localEngineThrust * localLiftFraction * currentThrottle * num2;
                    Vector3 force2 = (cachedTransform.up - Vector3.up).normalized * localEngineThrust * currentThrottle * num4;
                    float num5 = rigidBody.mass * (0f - Physics.gravity.y);
                    rigidBody.AddForce(cachedTransform.up * num5 * num2 * hoverForceScale, ForceMode.Force);
                    rigidBody.AddForce(force, ForceMode.Force);
                    rigidBody.AddForce(force2, ForceMode.Force);
                }
            }

            #endregion

            #region Radio

            public bool CanTransmitRadio()
            {
                if (!hasRadio)
                {
                    return false;
                }

                if (!isPowered)
                {
                    return false;
                }

                return true;
            }

            public bool CanReceiveRadioCommunication(BasePlayer player, IRadio transmittingRadio)
            {
                if (!hasRadio)
                {
                    return false;
                }

                if (!isPowered)
                {
                    return false;
                }

                if (IsDestroyed)
                {
                    return false;
                }

                foreach (MountPointInfo allMountPoint in allMountPoints)
                {
                    if (!allMountPoint.mountable.IsMounted())
                    {
                        continue;
                    }

                    var mounted = allMountPoint.mountable.GetMounted();
                    if (mounted == null)
                    {
                        continue;
                    }

                    if (mounted.userID.Get() == player.userID.Get())
                    {
                        cachedConnections.Clear();
                        return false;
                    }

                    cachedConnections.Add(mounted.Connection);
                }

                var frequency = transmittingRadio.GetRadioFrequency();
                if (frequency != 0 && this.GetRadioFrequency() != frequency)
                {
                    cachedConnections.Clear();
                    return false;
                }

                return cachedConnections.Count > 0;
            }

            public void ReceiveRadioCommunication(byte[] data)
            {
                if (cachedConnections.Count <= 0)
                {
                    return;
                }

                var target = RpcTarget.SendInfo("OnReceivedVoice", new SendInfo(cachedConnections)
                {
                    priority = Priority.Immediate
                });

                cockpitRadio.ClientRPC(target, data.Length, data);
                cachedConnections.Clear();
            }

            public int GetRadioFrequency()
            {
                if (!hasBroadcaster)
                {
                    return 0;
                }

                return radioBroadcaster.frequency;
            }

            #endregion

            #region Codelock

            public void ToggleCentralLocking(bool centralLockingOn)
            {
                centralLockingEnabled = !centralLockingOn;
            }

            #endregion

            #region ISeekerTargetOwner

            public override void OnEntityMessage(BaseEntity from, string msg)
            {
                if (!isPowered)
                {
                    playingRadarLock = false;
                    playingRadarWarning = false;
                    return;
                }

                if (msg == RADAR_LOCK)
                {
                    if (!playingRadarLock)
                    {
                        playingRadarLock = true;
                        playingRadarWarning = false;

                        this.CancelInvoke(PlayWarningAlarm);
                        this.CancelInvoke(ClearRadarWarning);

                        this.InvokeRepeating(PlayLockAlarm, 0, 0.1f);
                        this.Invoke(ClearRadarLock, 0.51f);

                        PlayLockAlarm();
                    }

                    return;
                }

                if (msg == RADAR_WARNING)
                {
                    if (!playingRadarWarning && !playingRadarLock)
                    {
                        playingRadarWarning = true;
                        this.InvokeRepeating(PlayWarningAlarm, 0, 0.5f);
                        this.Invoke(ClearRadarWarning, 1.01f);

                        PlayWarningAlarm();
                    }

                    return;
                }
            }

            #endregion

            #region Misc

            public override bool SupportsServerOcclusion()
            {
                return enableServerOcclusion;
            }

            public override void OnFlagsChanged(Flags old, Flags next)
            {
                var oldWasOn = old.HasFlags(Flags.On);
                var newIsOn = next.HasFlags(Flags.On);
                if (oldWasOn != newIsOn)
                {
                    OnToggled?.Invoke(HasFlag(Flags.On));
                }

                var hasPower = next.HasFlag(Flags.Reserved8);
                if (old.HasFlag(Flags.Reserved8) != hasPower)
                {
                    TogglePower(hasPower);
                }

                if (VehicleConfig.GeneralToastSettings.Enabled && VehicleConfig.GeneralToastSettings.OnEngineOnToastEnabled && !oldWasOn && newIsOn && CustomHasDriver())
                {
                    var driver = CustomGetDriver();
                    KaruzaEntitiesCommon.Instance.ShowToast(driver, VehicleConfig.GeneralToastSettings.OnEngineOnToast);
                }

                base.OnFlagsChanged(old, next);
            }

            public void AddTowingVehicle()
            {
                if (!hasTowVehicle)
                {
                    hasTowVehicle = true;
                }

                ++towVehicles;
            }

            public void RemoveTowingVehicle()
            {
                if (!hasTowVehicle)
                {
                    return;
                }

                --towVehicles;
                if (towVehicles <= 0)
                {
                    towVehicles = 0;
                    hasTowVehicle = false;
                }
            }

            public void AddTowedVehicle()
            {
            }

            public void RemoveTowedVehicle()
            {
            }

            public override void SetCreatorEntity(BaseEntity newCreatorEntity)
            {
                creatorEntity = newCreatorEntity;
                hasCreator = true;
            }

            public bool CanAccess(BasePlayer player)
            {
                if (OnlyOwnerAccessible())
                {
                    if (!hasCreator)
                    {
                        return false;
                    }

                    if (creatorEntity.net.ID.Value != player.net.ID.Value)
                    {
                        var hasDriver = CustomHasDriver();
                        if (!hasDriver)
                        {
                            return false;
                        }

                        var driver = CustomGetDriver();
                        if (player.userID.Get() != driver.userID.Get())
                        {
                            if (driver.Team == null || player.Team == null)
                            {
                                return false;
                            }
                            else if (!driver.Team.members.Contains(player.userID.Get()))
                            {
                                return false;
                            }
                        }
                    }
                }

                if (!hasCodeLock || !codeLock.IsLocked() || codeLock.whitelistPlayers.Contains(player.userID) || codeLock.guestPlayers.Contains(player.userID))
                {
                    return true;
                }

                Effect.server.Run(codeLock.effectDenied.resourcePath, this, 0u, Vector3.zero, Vector3.forward);
                return false;
            }

            void OnTriggerEnter(Collider col)
            {
                if (IsDead())
                {
                    return;
                }

                if (parentedId != 0)
                {
                    return;
                }

                if (!col.isTrigger)
                {
                    return;
                }

                var colId = col.GetInstanceID();
                if (!KaruzaEntitiesCommon.Instance.CargoColliders.ContainsKey(colId))
                {
                    return;
                }

                parentedId = colId;
                var cargo = KaruzaEntitiesCommon.Instance.CargoColliders[colId];
                this.CustomSetParent(cargo);
            }

            void OnTriggerExit(Collider col)
            {
                if (IsDead())
                {
                    return;
                }

                if (parentedId == 0)
                {
                    return;
                }

                if (!col.isTrigger)
                {
                    return;
                }

                var colId = col.GetInstanceID();
                if (colId != parentedId)
                {
                    return;
                }

                this.parentedId = 0;
                this.CustomSetParent(null);
            }

            public void CustomSetParent(BaseEntity entity)
            {
                BaseEntity baseEntity = GetParentEntity();
                if (object.ReferenceEquals(entity, null))
                {
                    OnParentChanging(baseEntity, null);
                    cachedTransform.SetParent(null, true);
                }
                else
                {
                    OnParentChanging(baseEntity, entity);
                    cachedTransform.SetParent(entity.transform, true);
                }

                SendNetworkUpdateImmediate();
            }

            public override Vector3 GetNetworkPosition()
            {
                return cachedTransform.position;
            }

            public override Quaternion GetNetworkRotation()
            {
                return cachedTransform.rotation;
            }

            public bool LootFuel(BasePlayer player)
            {
                if (!hasFuelStorage)
                {
                    return false;
                }

                return FuelContainer.PlayerOpenLoot(player);
            }

            public bool LootStorage(BasePlayer player)
            {
                if (!hasStorage)
                {
                    return false;
                }

                return StorageContainers[0].PlayerOpenLoot(player);
            }

            public override void LightToggle(BasePlayer player)
            {
                var driver = CustomGetDriver();
                if (object.ReferenceEquals(driver, null) || driver.net.ID.Value != player.net.ID.Value)
                {
                    return;
                }

                CustomLightToggle();
            }

            public void CustomLightToggle(bool? forcedState = null)
            {
                bool lightsOn = false;
                if (forcedState.HasValue)
                {
                    lightsOn = forcedState.Value;

                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(BaseEntity.Flags.Reserved5, lightsOn);
                    }
                }
                else
                {
                    if (!isPowered)
                    {
                        using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                        {
                            flagsUpdateScope.Set(BaseEntity.Flags.Reserved5, false);
                        }
                    }
                    else
                    {
                        lightsOn = !HasFlag(Flags.Reserved5);
                        using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                        {
                            flagsUpdateScope.Set(BaseEntity.Flags.Reserved5, lightsOn);
                        }
                    }
                }

                for (int i = 0; i < vehicleLights.Count; i++)
                {
                    var vl = vehicleLights[i];
                    vl.ToggleLights(lightsOn);
                }
            }

            public override bool CanPushNow(BasePlayer pusher)
            {
                if (timeSinceLastPush < HeliConfig.PushCooldown)
                {
                    return false;
                }

                var hasAccessToLock = !hasCodeLock || !codeLock.IsLocked() || codeLock.OnTryToOpen(pusher);
                if (!hasAccessToLock)
                {
                    return false;
                }

                return base.CanPushNow(pusher);
            }

            public override void DoPushAction(BasePlayer player)
            {
                PreventSleep();

                if (IsFlipped())
                {
                    var force = WaterLevel.Test(cachedTransform.position, false, true) ? VehicleConfig.SubmergedPushActionForce : 4.5f;
                    Vector3 vector = cachedTransform.InverseTransformPoint(player.transform.position);
                    float num = force;
                    if (vector.x > 0f)
                    {
                        num = 0f - num;
                    }

                    rigidBody.AddRelativeTorque(Vector3.forward * num, ForceMode.VelocityChange);
                    rigidBody.AddForce(Vector3.up * force, ForceMode.VelocityChange);
                    this.lastFlipTime = Time.realtimeSinceStartup + 2.55f;

                    return;
                }

                base.DoPushAction(player);
                timeSinceLastPush = 0f;
            }

            public override float GetPushActionForce()
            {
                return rigidBody.mass * VehicleConfig.PushActionForce;
            }

            public override void Spawn()
            {
                base.Spawn();
                PreventSleep();
                rigidBody.WakeUp();
            }

            void PreventSleep()
            {
                rigidBody.sleepThreshold = 0;
                Invoke(UpdateSleep, 2f);
            }

            void UpdateSleep()
            {
                rigidBody.sleepThreshold = VehicleConfig.SleepThreshold;
            }

            public override void OnKilled()
            {
                if (!Instance.IsUnloading)
                {
                    if (hasStorage)
                    {
                        for (int i = 0; i < StorageContainers.Count; i++)
                        {
                            var storageContainer = StorageContainers[i];
                            if (!storageContainer.Config.DropsLoot)
                            {
                                continue;
                            }

                            storageContainer.CustomDropItems(null);
                        }
                    }

                    if (HasAmmoContainer && VehicleConfig.AmmoContainer.DropsLoot)
                    {
                        AmmoContainer.CustomDropItems(null);
                    }

                    if (hasFuelStorage && VehicleConfig.FuelSettings.FuelContainer.DropsLoot)
                    {
                        FuelContainer.CustomDropItems(null);
                    }

                    if (hasBoostStorage && VehicleConfig.BoostSettings.BoostFuelContainer.DropsLoot)
                    {
                        BoostContainer.DropItems(null);
                    }

                    if (hasPhysicalEngine && VehicleConfig.PhysicalEngineSettings.DropsLoot)
                    {
                        physicalEngine.CustomDropItems(null);
                    }
                }

                base.OnKilled();
            }


            void OnDestroy()
            {
                this.IsActive = false;
                OnPhysicsUpdate = null;
                OnMountedChange = null;
                OnPowerToggle = null;
                OnToggled = null;
                OnHealthChange = null;
                OnBoostTimeUpdate = null;

                Interface.CallHook("OnVehicleDestroyed", vehicleNetId);

                SeekerTarget.SetSeekerTarget(this, SeekerStrength.OFF);
                KaruzaEntitiesCommon.Instance.CustomSAMTargets.Remove(this);

                foreach (BasePlayer driver in driverCache.Values)
                {
                    if (driver != null)
                    {
                        Radio.RemoveRadio(driver);
                    }
                }

                Destroy(this.gameObject);

                cockpitElectical?.Clear();
                cockpitElectical = null;
                vehicleLights?.Clear();
                vehicleLights = null;
                fakeEngines?.Clear();
                fakeEngines = null;
                wheels = null;
                cachedConnections?.Clear();
                cachedConnections = null;
                SpecialGuns?.Clear();
                SpecialGuns = null;
                WeaponSystems?.Clear();
                WeaponSystems = null;
                raycastHit = null;
                cockpitRadio = null;
                radioBroadcaster = null;
                AmmoContainer = null;
                buoyancy = null;
                engineController = null;
            }

            public override void Load(LoadInfo info)
            {
                var fsId = engineController.FuelSystem.GetInstanceID();

                info.msg.parent = null;
                if (info.fromDisk)
                {
                    var saveFlags = ((Flags)info.msg.baseEntity.flags);
                    if (saveFlags.HasFlags(Flags.Disabled))
                    {
                        saveFlags &= ~Flags.Disabled;
                        info.msg.baseEntity.flags = (int)saveFlags;
                    }
                }

                base.Load(info);
                engineController.FuelSystem.SetInstanceID(fsId);

                if (HasAmmoContainer && info.msg.storageBox != null)
                {
                    AmmoContainer.inventory.Load(info.msg.storageBox.contents);
                    info.msg.storageBox.ResetToPool();
                    info.msg.storageBox = null;
                }

                if (hasStorage && info.msg.reclaimManager?.reclaimEntries != null)
                {
                    var reclaimEntries = info.msg.reclaimManager.reclaimEntries;
                    for (int i = 0; i < reclaimEntries.Count; i++)
                    {
                        var reclaimEntry = reclaimEntries[i];
                        var storageContainer = StorageContainers[i];
                        storageContainer.inventory.Load(reclaimEntry.beltInventory);

                        reclaimEntry.ResetToPool();
                    }

                    info.msg.reclaimManager.reclaimEntries = null;
                }
                
                if (hasFuelStorage && info.msg.worldItem != null && info.msg.worldItem.item != null)
                {
                    FuelContainer.inventory.Load(info.msg.worldItem.item.contents);
                    info.msg.worldItem.ResetToPool();
                    info.msg.worldItem = null;
                }

                var resetPlayer = false;
                if (hasPhysicalEngine && info.msg.basePlayer?.inventory?.invBelt != null)
                {
                    physicalEngine.inventory.Load(info.msg.basePlayer.inventory.invBelt);
                    resetPlayer = true;
                }

                if (hasBoostStorage && info.msg.basePlayer?.inventory?.invWear != null)
                {
                    BoostContainer.inventory.Load(info.msg.basePlayer.inventory.invWear);
                    resetPlayer = true;
                }

                if (resetPlayer)
                {
                    info.msg.basePlayer.ResetToPool();
                    info.msg.basePlayer = null;
                }

                if (hasCodeLock && info.msg.codeLock != null)
                {
                    codeLock.hasCode = info.msg.codeLock.hasCode;
                    codeLock.hasGuestCode = info.msg.codeLock.hasGuestCode;
                    if (info.msg.codeLock.pv != null)
                    {
                        codeLock.code = info.msg.codeLock.pv.code;
                        codeLock.whitelistPlayers = info.msg.codeLock.pv.users.ShallowClonePooled();
                        codeLock.guestCode = info.msg.codeLock.pv.guestCode;
                        codeLock.guestPlayers = info.msg.codeLock.pv.guestUsers.ShallowClonePooled();
                    }

                    codeLock.SetFlagLocal(Flags.Locked, codeLock.hasCode);
                    info.msg.codeLock.ResetToPool();
                    info.msg.codeLock = null;
                }

                if (hasRespawns && info.msg.whitelist?.users != null)
                {
                    for (int i = 0; i < info.msg.whitelist.users.Count; i++)
                    {
                        var userId = info.msg.whitelist.users[i];
                        var respawnPoint = respawnPoints[i];
                        respawnPoint.deployerUserID = userId;
                    }

                    info.msg.whitelist.ResetToPool();
                    info.msg.whitelist = null;
                }

                Handler?.Load(info);
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);
                info.msg.baseEntity.pos = GetNetworkPosition();
                info.msg.baseEntity.rot = GetNetworkRotation().eulerAngles;
                info.msg.parent = null;

                if (hasCodeLock)
                {
                    info.msg.codeLock = Pool.Get<ProtoBuf.CodeLock>();
                    info.msg.codeLock.hasGuestCode = codeLock.guestCode.Length > 0;
                    info.msg.codeLock.hasCode = codeLock.code.Length > 0;
                }

                if (info.forDisk)
                {
                    info.msg.baseVehicle.mountPoints = Pool.Get<List<ProtoBuf.BaseVehicle.MountPoint>>();

                    if (HasAmmoContainer)
                    {
                        info.msg.storageBox = Pool.Get<ProtoBuf.StorageBox>();
                        info.msg.storageBox.contents = AmmoContainer.inventory.Save();
                    }

                    if (hasStorage)
                    {
                        info.msg.reclaimManager = Pool.Get<ProtoBuf.ReclaimManager>();
                        info.msg.reclaimManager.reclaimEntries = Pool.Get<List<ProtoBuf.ReclaimManager.ReclaimInfo>>();

                        for (int i = 0; i < StorageContainers.Count; i++)
                        {
                            var storageContainer = StorageContainers[i];

                            var reclaimInfo = Pool.Get<ProtoBuf.ReclaimManager.ReclaimInfo>();

                            reclaimInfo.mainInventory = Pool.Get<ProtoBuf.ItemContainer>();
                            reclaimInfo.wearInventory = Pool.Get<ProtoBuf.ItemContainer>();
                            reclaimInfo.backpackInventory = Pool.Get<ProtoBuf.ItemContainer>();

                            reclaimInfo.beltInventory = storageContainer.inventory.Save();

                            info.msg.reclaimManager.reclaimEntries.Add(reclaimInfo);
                        }
                    }

                    if (hasFuelStorage)
                    {
                        info.msg.worldItem = Pool.Get<ProtoBuf.WorldItem>();
                        info.msg.worldItem.item = Pool.Get<ProtoBuf.Item>();
                        info.msg.worldItem.item.contents = FuelContainer.inventory.Save();
                    }

                    if (hasPhysicalEngine)
                    {
                        if (info.msg.basePlayer == null)
                        {
                            info.msg.basePlayer = Pool.Get<ProtoBuf.BasePlayer>();
                            info.msg.basePlayer.inventory = Pool.Get<ProtoBuf.PlayerInventory>();
                        }

                        info.msg.basePlayer.inventory.invBelt = physicalEngine.inventory.Save();
                    }

                    if (hasBoostStorage)
                    {
                        if (info.msg.basePlayer == null)
                        {
                            info.msg.basePlayer = Pool.Get<ProtoBuf.BasePlayer>();
                            info.msg.basePlayer.inventory = Pool.Get<ProtoBuf.PlayerInventory>();
                        }

                        info.msg.basePlayer.inventory.invWear = BoostContainer.inventory.Save();
                    }

                    if (hasCodeLock)
                    {
                        info.msg.codeLock.pv = Pool.Get<ProtoBuf.CodeLock.Private>();
                        info.msg.codeLock.pv.code = codeLock.code;
                        info.msg.codeLock.pv.users = Pool.Get<List<ulong>>();
                        info.msg.codeLock.pv.users.AddRange(codeLock.whitelistPlayers);
                        info.msg.codeLock.pv.guestCode = codeLock.guestCode;
                        info.msg.codeLock.pv.guestUsers = Pool.Get<List<ulong>>();
                        info.msg.codeLock.pv.guestUsers.AddRange(codeLock.guestPlayers);
                    }

                    if (hasRespawns)
                    {
                        info.msg.whitelist = Pool.Get<ProtoBuf.Whitelist>();
                        info.msg.whitelist.users = Pool.Get<List<ulong>>();
                        for (int i = 0; i < respawnPoints.Count; i++)
                        {
                            var respawnPoint = respawnPoints[i];
                            info.msg.whitelist.users.Add(respawnPoint.deployerUserID);
                        }
                    }
                }
                else if (hasCodeLock && info.forConnection != null)
                {
                    info.msg.codeLock.hasAuth = codeLock.whitelistPlayers.Contains(info.forConnection.userid);
                    info.msg.codeLock.hasGuestAuth = codeLock.guestPlayers.Contains(info.forConnection.userid);
                }

                Handler?.Save(info);
            }

            public void OnDefaultInventoryPreAnnihilation()
            {
                //throw new NotImplementedException();
            }

            public void OnDefaultInventoryFirstCreated()
            {
                //throw new NotImplementedException();
            }

            public bool DefaultInventoryItemFilter(Item item, int targetSlot)
            {
                //throw new NotImplementedException();
                return true;
            }

            public void OnItemAddedOrRemoved(Item item, bool added)
            {
                //throw new NotImplementedException();
            }

            public void OnDefaultInventoryDirty()
            {
                //throw new NotImplementedException();
            }

            public string DefaultClientsideFullPrefabName()
            {
                if (HeliConfig == null)
                {
                    return PREFAB_WORLD;
                }

                switch (HeliConfig.BodyType)
                {
                    case BodyType.Prefab:
                    case BodyType.Gib:
                        return HeliConfig.BodyGibPrefab;

                    case BodyType.WorldItem:
                    default:
                        return PREFAB_WORLD;
                }
            }

            public void OnCustomPrefabPrototypeEntityRegistered()
            {
                //throw new NotImplementedException();
            }

            public void OnCustomPrefabPrototypeEntityUnregistered()
            {
                //throw new NotImplementedException();
            }

            public bool IsBaseCombat()
            {
                return true;
            }

            public void LoadExtra(Stream stream, BinaryReader reader)
            {
                loadedFromSave = true;
            }

            public void PostLoadExtra(Stream stream, BinaryReader reader)
            {
            }

            public void SaveExtra(Stream stream, BinaryWriter writer)
            {
            }

            public void OnEntitySaveForNetwork(SaveInfo info)
            {
                if (HeliConfig.BodyType == BodyType.Prefab)
                {
                    return;
                }

                switch (HeliConfig.BodyType)
                {
                    case BodyType.Gib:
                        if (!string.IsNullOrEmpty(HeliConfig.BodyGibName))
                        {
                            info.msg.servergib = Facepunch.Pool.Get<ProtoBuf.ServerGib>();
                            info.msg.servergib.gibName = HeliConfig.BodyGibName;
                        }
                        break;

                    case BodyType.WorldItem:
                        info.msg.worldItem = Facepunch.Pool.Get<ProtoBuf.WorldItem>();
                        info.msg.worldItem.item = Pool.Get<ProtoBuf.Item>();
                        info.msg.worldItem.item.itemid = HeliConfig.BodyItemId;
                        info.msg.worldItem.item.skinid = HeliConfig.BodySkinId;
                        info.msg.worldItem.item.name = " ";
                        break;

                }
            }

            #endregion

            #region Towing

            static LayerMask TOW_LAYER = LayerMask.GetMask(LayerMask.LayerToName((int)Layer.Default), LayerMask.LayerToName((int)Layer.Water), LayerMask.LayerToName((int)Layer.TransparentFX));

            public bool IsTowing => HasFlag(Flags.Reserved14);

            public bool IsTowingAllowed => CheckTowingAllowed();

            public BaseEntity TowEntity => this;

            public Transform TowAnchor { get; set; }

            public Rigidbody TowBody => rigidBody;

            public virtual void OnTowAttach()
            {
                EnablePhysics();
            }

            public virtual void OnTowDetach()
            {
            }

            public void DisablePhysics()
            {
                rigidBody.isKinematic = true;
            }

            public void EnablePhysics()
            {
                rigidBody.isKinematic = false;
                rigidBody.WakeUp();
            }

            public virtual bool CheckTowingAllowed()
            {
                return !IsTowing;
            }

            void InitializeVanillaTowing()
            {
                if (!VehicleConfig.TowSettings.Enabled)
                {
                    return;
                }

                if (!VehicleConfig.TowSettings.VanillaTowingSettings.Enabled)
                {
                    return;
                }

                var towAnchorGo = new GameObject();
                towAnchorGo.transform.SetParent(cachedTransform, false);
                towAnchorGo.transform.localPosition = VehicleConfig.TowSettings.VanillaTowingSettings.TowAnchorPoint;
                var bc = towAnchorGo.AddComponent<BoxCollider>();
                bc.size = VehicleConfig.TowSettings.VanillaTowingSettings.TowAnchorTriggerSize;
                towAnchorGo.layer = TOW_LAYER;

                TowAnchor = towAnchorGo.transform;

                var towConfig = PrefabAttribute.server.Find<TowConfig>(this.prefabID);
                if (towConfig == null)
                {
                    towConfig = new TowConfig();
                    PrefabAttribute.server.Add(this.prefabID, towConfig);
                }
            }

            #endregion


            #region Weakspots

            Dictionary<ulong, WeakspotData> weakspotByEntity = new Dictionary<ulong, WeakspotData>();
            public List<Weakspot> Weakspots { get; set; } = new List<Weakspot>();

            bool hasWeakpoints;

            void HurtWeakpoints(HitInfo info)
            {
                if (!hasWeakpoints)
                {
                    return;
                }

                if (!object.ReferenceEquals(info.HitEntity, null) && weakspotByEntity.TryGetValue(info.HitEntity.net.ID.Value, out WeakspotData weakSpotData) && !weakSpotData.Weakspot.IsDestroyed)
                {
                    var weakspot = weakSpotData.Weakspot;
                    weakspot.Hurt(info.damageTypes.Total(), info);
                }
            }

            void RepairWeakpoints(float healAmount)
            {
                for (int i = 0; i < Weakspots.Count; i++)
                {
                    var wp = Weakspots[i];
                    wp.Heal(healAmount);
                }
            }

            void PrepareWeakspots()
            {
                for (int i = 0; i < Weakspots.Count; i++)
                {
                    hasWeakpoints = true;
                    var wp = Weakspots[i];
                    for (int n = 0; n < wp.Entities.Count; n++)
                    {
                        var wpEnt = wp.Entities[n];
                        weakspotByEntity.Add(wpEnt.net.ID.Value, new WeakspotData()
                        {
                            Entity = wpEnt,
                            Weakspot = wp
                        });
                    }
                }
            }

            #endregion

            #region Edge Transfer

            private float min;
            private float max;

            void SetupEdgeTransfer()
            {
                var limits = World.Size * 0.7f;
                min = limits * -1;
                max = limits;
            }

            private void EdgeTransferCheck()
            {
                if (Instance.configuration.ForceEnableEdgeTransfer.HasValue)
                {
                    if (!Instance.configuration.ForceEnableEdgeTransfer.Value)
                    {
                        return;
                    }
                }
                else if (!HeliConfig.EnableEdgeTransfer)
                {
                    return;
                }

                var pos = cachedTransform.position;
                bool needsUpdate = false;

                if (pos.x > max || pos.x < min)
                {
                    pos.x *= -0.98f;
                    needsUpdate = true;
                }
                else if (pos.z > max || pos.z < min)
                {
                    pos.z *= -0.98f;
                    needsUpdate = true;
                }

                if (needsUpdate)
                {
                    cachedTransform.position = pos;
                    this.TransformChanged();
                }
            }

            #endregion



            public override IFuelSystem GetFuelSystem()
            {
                return engineController?.FuelSystem;
            }

        }

        #endregion

        #region Config

        public class Configuration
        {
            public string PlayerHitMarkerFx { get; set; } = "assets/bundled/prefabs/fx/hit_notify.prefab";
            public bool ForceUnlimitedAmmo { get; set; }
            public bool? ForceSaving { get; set; } = null;
            public bool GlobalDisableCounterUpdates { get; set; }
            public float? ForceOutsideDecayMinutes { get; set; } = null;
            public float? ForceInsideDecayMinutes { get; set; } = null;
            public float? ForceTimeAfterEngineOffToStartDecay { get; set; } = null;
            public bool? ForceEnableEdgeTransfer { get; set; } = null;
            public bool? ForceServerOcclusion { get; set; } = null;
            public bool DisableDecay { get; set; } = false;
            public int DefaultRadioFrequency { get; set; } = -1;
        }

        public class HelicopterConfig : CustomVehicleConfig, ILandingGearConfig
        {
            public override SeekerStrength SeekerStrength { get; set; } = SeekerStrength.MEDIUM;
            public float HoverForceScale { get; set; } = 0.99f;
            public float LiftDotMax { get; set; }
            public float LiftDotFraction { get; set; }
            public float MotorForceConstant { get; set; } = 150;
            public float LoseControlThreshold { get; set; } = 0.10f;
            public float ServiceCeiling { get; set; }
            public bool DisableGlobalNetwork { get; set; } = false;
            public float EngineThrustMax { get; set; }
            public bool EnableEdgeTransfer { get; set; } = false;
            public float GroundedDistanceCheck { get; set; } = 3f;
            public LandingGearSettings LandingGearSettings { get; set; } = new LandingGearSettings();
            public SamTargetType SAMTargetSettings { get; set; } = new SamTargetType(150f, 1f, 5f);
        }
        
        public string DirectoryPath { get { return $"{Interface.Oxide.ConfigDirectory}/{Name}/"; } }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            Config.Settings.Converters.Add(new ConfigVectorConverter());

            try
            {

                configuration = Config.ReadObject<Configuration>();
                if (configuration == null)
                {
                    throw new Exception();
                }

                LoadVehicleConfigs();

                foreach (var hc in TempConfigs)
                {
                    TryUpdateHeliConfig(hc.Key, hc.Value);
                }

                SaveConfig();
            }
            catch
            {
                Config.WriteObject(configuration, false, $"{Interface.Oxide.ConfigDirectory}/{Name}.jsonError");
                PrintError("The configuration file contains an error.");
            }

            SaveConfig();
        }

        public static void TryUpdateHeliConfig(string vehicleName, HelicopterConfig config)
        {
            TryUpdateConfig(vehicleName, config);
        }

        void LoadVehicleConfigs()
        {
            TempConfigs.Clear();

            if (!Directory.Exists(DirectoryPath))
            {
                Directory.CreateDirectory(DirectoryPath);
                return;
            }

            var files = Directory.EnumerateFiles(DirectoryPath);
            foreach (var filePath in files)
            {
                var isJson = Path.GetExtension(filePath) == ".json";
                if (!isJson)
                {
                    continue;
                }

                var fileName = Path.GetFileNameWithoutExtension(filePath);
                if (fileName.StartsWith("_"))
                {
                    continue;
                }

                var pc = Config.ReadObject<HelicopterConfig>(filePath);
                TempConfigs.Add(fileName, pc);
            }
        }

        void LoadVehicleConfig(string vehicleName)
        {
            try
            {
                var filePath = $"{DirectoryPath}{vehicleName}.json";
                var config = Config.ReadObject<HelicopterConfig>(filePath);

                RegisterVehicleBundle(vehicleName, config);

                Instance.Puts($"Loaded {vehicleName}");
            }
            catch (Exception ex)
            {
                PrintError($"Vehicle failed to load: {vehicleName} - {ex.ToString()}");
            }
        }

        void SaveVehicleConfigs()
        {
            if (TempConfigs == null)
            {
                return;
            }

            foreach (var config in TempConfigs)
            {
                var filePath = $"{DirectoryPath}{config.Key}.json";
                Config.WriteObject(config.Value, false, filePath);
            }
        }

        protected override void LoadDefaultConfig()
        {
            configuration = new Configuration();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(configuration);
            SaveVehicleConfigs();
        }

        #endregion

        #region Vehicle Bundles

        static CustomPrefabBundle bundle;
        public static void RegisterVehicleBundle(string vehicleName, HelicopterConfig heliConfig)
        {
            Instance.HelicopterConfigs[vehicleName] = heliConfig;
            InitializeAmmoTypes(heliConfig);

            var path = $"assets/custom/{vehicleName}.prefab";
            if (GameManifest.pathToGuid.ContainsKey(path))
            {
                return;
            }

            var baseCombat = new CustomEntities.CustomPrefabBaseCombat
            {
                HealthStart = heliConfig.MaxHealth,
                HealthMax = heliConfig.MaxHealth,
                MarkAttackerHostile = true,
                RepairEnabled = false,
                PickupEnabled = false,
                ProtectionProperties = heliConfig.ProtectionProperties
                    .OrderBy(k => k.Key)
                    .Select(v => v.Value)
                    .ToArray(),
            };

            var recipe = new CustomEntities.CustomPrefabRecipe(vehicleName, typeof(RustHelicopterController), Layer.Vehicle_World, baseCombat, true);
            var recipes = bundle?.Recipes?.ToList() ?? new List<GenericPrefabRecipe>();
            recipes.Add(recipe);

            bundle = new CustomEntities.CustomPrefabBundle(Instance, recipes.ToArray());
            if (!CustomEntities.CustomPrefabs.RegisterAndLoadBundle(bundle))
            {
                Instance.PrintError("Bundles failed to load");
            }
        }

        public static void RegisterVehicleBundles(Dictionary<string, HelicopterConfig> heliConfigs)
        {
            var recipes = bundle?.Recipes?.ToList() ?? new List<GenericPrefabRecipe>();
            foreach (var config in heliConfigs)
            {
                var vehicleName = config.Key;
                var vehicleConfig = config.Value;
                var path = $"assets/custom/{vehicleName}.prefab";

                Instance.HelicopterConfigs[config.Key] = config.Value;
                InitializeAmmoTypes(config.Value);

                if (GameManifest.pathToGuid.ContainsKey(path))
                {
                    continue;
                }

                var baseCombat = new CustomEntities.CustomPrefabBaseCombat
                {
                    HealthStart = vehicleConfig.MaxHealth,
                    HealthMax = vehicleConfig.MaxHealth,
                    MarkAttackerHostile = true,
                    RepairEnabled = false,
                    PickupEnabled = false,
                    ProtectionProperties = vehicleConfig.ProtectionProperties
                        .OrderBy(k => k.Key)
                        .Select(v => v.Value)
                        .ToArray(),
                };

                var recipe = new CustomEntities.CustomPrefabRecipe(vehicleName, typeof(RustHelicopterController), Layer.Vehicle_World, baseCombat, true);
                recipes.Add(recipe);

            }

            bundle = new CustomEntities.CustomPrefabBundle(Instance, recipes.ToArray());
            if (!CustomEntities.CustomPrefabs.RegisterAndLoadBundle(bundle))
            {
                Instance.PrintError("Bundles failed to load");
            }
        }

        public void RegisterEntityBundles(Dictionary<string, SuperBaseEntityConfig> configs)
        {
            var recipes = bundle?.Recipes?.ToList() ?? new List<GenericPrefabRecipe>();
            foreach (var config in configs)
            {
                var vehicleName = config.Key;
                var path = $"assets/custom/{vehicleName}.prefab";
                var vehicleConfig = config.Value as HelicopterConfig;
                TryUpdateHeliConfig(vehicleName, vehicleConfig);

                Instance.HelicopterConfigs[config.Key] = vehicleConfig;
                InitializeAmmoTypes(vehicleConfig);

                if (GameManifest.pathToGuid.ContainsKey(path))
                {
                    continue;
                }

                var baseCombat = new CustomEntities.CustomPrefabBaseCombat
                {
                    HealthStart = vehicleConfig.MaxHealth,
                    HealthMax = vehicleConfig.MaxHealth,
                    MarkAttackerHostile = true,
                    RepairEnabled = false,
                    PickupEnabled = false,
                    ProtectionProperties = vehicleConfig.ProtectionProperties
                        .OrderBy(k => k.Key)
                        .Select(v => v.Value)
                        .ToArray(),
                };

                var recipe = new CustomEntities.CustomPrefabRecipe(vehicleName, typeof(RustHelicopterController), Layer.Vehicle_World, baseCombat, true);
                recipes.Add(recipe);

            }

            bundle = new CustomEntities.CustomPrefabBundle(Instance, recipes.ToArray());
            if (!CustomEntities.CustomPrefabs.RegisterAndLoadBundle(bundle))
            {
                Instance.PrintError("Bundles failed to load");
            }
        }
        public static void SaveAndUnregisterVehicleBundles()
        {
            if (bundle == null)
            {
                return;
            }

            if (!CustomEntities.CustomPrefabs.SaveAndUnregisterBundle(bundle))
            {
                Instance.PrintError("Bundles failed to unload");
            }

            bundle.Recipes = null;
            bundle = null;
        }

        #endregion
    }
}
