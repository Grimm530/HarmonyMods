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
    [Info("RustPlane", "Karuza", "1.36.0")]
    public class RustPlane : RustPlugin, IKaruzaEntityPlugin
    {
        public static RustPlane Instance;

        private Configuration configuration;
        public Dictionary<string, PlaneConfig> TempConfigs = new Dictionary<string, PlaneConfig>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, PlaneConfig> PlaneConfigs = new Dictionary<string, PlaneConfig>(StringComparer.OrdinalIgnoreCase);

        private bool IsUnloading = false;

        internal void OnServerInitialized()
        {
            Instance = this;
            LoadConfig();

            if (TempConfigs.Count > 0)
            {
                RegisterVehicleBundles(TempConfigs);
                TempConfigs.Clear();
            }

            RequestEntityConfigs<PlaneConfig>(this, 1);
        }

        internal void Unload()
        {
            IsUnloading = true;

            SaveAndUnregisterVehicleBundles();

            configuration = null;
            Instance = null;
        }

        [ConsoleCommand("rustplane.reload")]
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

        #region Plane Behaviors

        public class PlaneController : BaseVehicle, IRadio, ISeekerTargetOwner, IEngineControllerUser, SamSite.ISamSiteTarget, ICustomEntity, IWeaponController, IVehicle, ILandingGearVehicle, IRadarVehicle
        {
            #region Constants

            const string ALARM_SFX = "assets/prefabs/locks/keypad/effects/lock.code.unlock.prefab";

            #endregion

            public bool IsActive { get; set; } = false;
            public List<Wheel> Wheels = new List<Wheel>();

            PlaneInputState currentInputState = new PlaneInputState();

            Vector3 torqueScale = Vector3.zero;

            //int loadedPrimary;
            //int loadedSecondary;
            //int loadedFlares;

            float nextDamageTime;
            float lastPlayerInputTime;
            float lastWaterCheck;
            float onGroundTilRealTime;
            float lastFlipTime;
            float engineThrustMax;
            float fuelPerSec;
            float pendingImpactDamage;
            float currentTime;
            float collisionAvailable;
            float lastEngineOnTime;
            float loseControlHealthThreshold;
            float nextHostileUpdate;
            float timeSinceCachedFuelFraction;
            float cachedFuelFraction;
            float fuelGaugeMax = 100f;
            float currentThrottle;
            float outsideDecayMinutes;
            float insideDecayMinutes;
            float timeAfterEngineOffToStartDecay;
            float groundedDistanceCheck;

            bool forceSeekerCanLock;
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
            bool hasFuelStorage;
            bool hasStorage;
            bool loseControl;
            bool lowHealthEffectsValid;
            bool invokingLowHealthFx;
            bool collisionReady;
            bool centralLockingEnabled;
            bool hasCodeLock;
            bool invokingDelayedImpactDamage;
            bool hasTowVehicle;
            bool hasPhysicalEngine;
            bool hasRespawns;
            bool enableServerOcclusion;
            bool invokingResetSleep;
            bool hasBoostStorage;
            bool canStall = true;
            bool wheelsDisabled;
            bool hasCreator;
            ulong currentRadarUserId;
            int towVehicles;

            int parentedId;

            HitInfo lastServerProjectileAttack;
            bool invokingClearLastAttack;

            ulong vehicleNetId;
            ulong currentFuelModifierSkinId;

            LowHealthEffectConfig spawnedLowHealthEffect;

            //PlaneEngine engine;
            Telephone cockpitRadio;
            SpecialBroadcaster radioBroadcaster;
            SpecialBaseCombatEntity lowHealthPrefab;
            CodeLock codeLock;
            Buoyancy buoyancy;
            Ray ray;
            RaycastHit[] raycastHit = new RaycastHit[1];
            VehicleEngineController<PlaneController> engineController;
            Collider dismountCollider;
            EngineStorageContainer physicalEngine;
            GameObjectRef crashEffect;
            BasePlayer currentRadarUser;

            Transform cachedTransform;
            Transform waterSample;
            List<SpecialFakeEngine> fakeEngines = new List<SpecialFakeEngine>();
            List<BaseEntity> cockpitElectical = new List<BaseEntity>();
            List<SpecialLight> vehicleLights = new List<SpecialLight>();
            List<Connection> cachedConnections = new List<Connection>();
            List<SleepingBag> respawnPoints = new List<SleepingBag>();

            OrderedDictionary driverCache = new OrderedDictionary();
            List<Tuple<float, LowHealthEffectConfig>> lowHealthThresholds = new List<Tuple<float, LowHealthEffectConfig>>();
            Dictionary<ulong, FuelModifierSetting> fuelModifiers = new Dictionary<ulong, FuelModifierSetting>();

            int tick;
            Vector3 oldPos;
            Quaternion oldRot;
            public override float PositionTickRate => 0.1f;
            public override bool AlwaysAllowBradleyTargeting => true;

            public PlaneConfig PlaneConfig;
            public BodyType BodyType { get { return BaseConfig.BodyType; } }
            public SuperBaseEntityConfig BaseConfig { get { return PlaneConfig; } }
            public BaseVehicleConfig VehicleConfig { get { return PlaneConfig; } }

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

                    return (PlaneConfig?.EnableSaving).GetValueOrDefault();
                }
            }

            public virtual bool HasDefaultInventory => false;
            public virtual bool DefaultInventoryHandledByBaseType => false;
            public ItemContainer DefaultInventory { get; set; } = null;
            public virtual int DefaultInventoryCapacity { get { return 0; } }
            #endregion
            public List<IStorageContainer> StorageContainers { get; private set; } = new List<IStorageContainer>();
            public VehicleFuelContainer FuelContainer { get; private set; }
            public VehicleBoostContainer BoostContainer { get; private set; }
            public Dictionary<HurtTriggerType, Dictionary<HurtTriggerConfig, TriggerHurtNotChild>> HurtTriggers { get; } = new Dictionary<HurtTriggerType, Dictionary<HurtTriggerConfig, TriggerHurtNotChild>>();
            public SamSite.SamTargetType SAMTargetType { get { return PlaneConfig.SAMTargetSettings; } }
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
                if (IsEngineOn())
                {
                    if (currentInputState.throttle >= 1)
                    {
                        return currentInputState.throttle * 10;
                    }

                    return currentInputState.throttle;
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

                    if (hasBuoyancy && (buoyancy.InWater || buoyancy.timeOutOfWater < 0.10f))
                    {
                        this.onGroundTilRealTime = Time.realtimeSinceStartup + (IsEngineOn() ? 0.01f : PlaneConfig.EngineStartupTime);
                        return true;
                    }

                    if (Wheels.Count == 0 || !LandingGearOn)
                    {
                        if (Physics.RaycastNonAlloc(cachedTransform.position, cachedTransform.TransformDirection(-Vector3.up), raycastHit, groundedDistanceCheck, GROUND_LAYER) > 0)
                        {
                            this.onGroundTilRealTime = Time.realtimeSinceStartup + (IsEngineOn() ? 0.01f : PlaneConfig.EngineStartupTime);
                            return true;
                        }
                    }
                    else
                    {
                        for (int i = 0; i < Wheels.Count; i++)
                        {
                            var wheel = Wheels[i];
                            if (wheel.wheelCollider.isGrounded)
                            {
                                this.onGroundTilRealTime = Time.realtimeSinceStartup + (IsEngineOn() ? 0.01f : PlaneConfig.EngineStartupTime);
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
                        this.onGroundTilRealTime = Time.realtimeSinceStartup + (IsEngineOn() ? 0.01f : PlaneConfig.EngineStartupTime);
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

            public bool IsStartingUp
            {
                get
                {
                    if (engineController != null)
                    {
                        return engineController.IsStarting;
                    }

                    return false;
                }
            }

            public VehicleEngineController<PlaneController>.EngineState CurEngineState
            {
                get
                {
                    if (engineController == null)
                    {
                        return VehicleEngineController<PlaneController>.EngineState.Off;
                    }

                    return engineController.CurEngineState;
                }
            }

            public bool IsEjectEligible { get { return !IsOnGround; } }

            public override void DestroyShared()
            {
                base.DestroyShared();
                Handler?.DestroyShared();
            }

            public override void PreServerLoad()
            {
                base.PreServerLoad();
                Handler?.PreServerLoad();
            }

            public void Awake()
            {
                cachedTransform = this.transform;

                this.rigidBody = this.gameObject.GetOrAddComponent<Rigidbody>();
                this.rigidBody.isKinematic = false;
                this.rigidBody.useGravity = true;
                this.rigidBody.detectCollisions = true;
                this.rigidBody.interpolation = RigidbodyInterpolation.None;
                this.rigidBody.collisionDetectionMode = CollisionDetectionMode.Continuous;
                this.rigidBody.constraints = RigidbodyConstraints.None;
                this.rigidBody.sleepThreshold = 0;

                var prefabGuid = GameManifest.pathToGuid[DEBRIS_EFFECT_PREFAB];
                this.crashEffect = new GameObjectRef() { guid = prefabGuid };

                canTriggerParent = false;

                this.propDirection = new DirectionProperties[0];
                this.bounds = new Bounds() { size = new Vector3(4, 4, 4) };

                this.mountPoints = new List<MountPointInfo>();
                this.dismountPositions = new Transform[0];

                var waterSample = new GameObject();
                waterSample.transform.SetParent(cachedTransform, false);
                this.waterSample = waterSample.transform;

                this.syncPosition = true;

                this.explosionForceMultiplier = 0;
                this.explosionForceMax = 0;

                if (!string.IsNullOrEmpty(this.ShortPrefabName))
                {
                    this.PlaneConfig = Instance.PlaneConfigs[this.ShortPrefabName];
                }

                CustomHandler.AttachNewHandlerToCustomEntityIfNotPrototype(this);

                SetupEdgeTransfer();
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
                    InvokeRandomized(DecayTick, UnityEngine.Random.Range(30f, 60f), 60f, 6f);
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
            }

            public override GameObjectRef GetCollisionFX()
            {
                return crashEffect;
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
                if (base.isServer)
                {
                    if (oldWasOn && CurEngineState == VehicleEngineController<PlaneController>.EngineState.Off)
                    {
                        lastEngineOnTime = Time.time;
                        forceSeekerCanLock = true;
                        Invoke(DisableForceSeekerLock, 5);
                    }
                    else if (newIsOn && forceSeekerCanLock)
                    {
                        CancelInvoke(DisableForceSeekerLock);
                    }

                    // Re-add when nexus is added
                    //if (rigidBody != null)
                    //{
                    //    rigidBody.isKinematic = IsTransferProtected();
                    //}
                }
            }

            void DisableForceSeekerLock()
            {
                forceSeekerCanLock = false;
            }

            public void UpdateNetwork()
            {
                Flags flags = base.flags;
                if (CustomHasDriver())
                {
                    SendNetworkUpdate();
                }
                else if (flags != base.flags)
                {
                    SendNetworkUpdate_Flags();
                }
            }

            public void DecayTick()
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

            public override void PlayerServerInput(InputState inputState, BasePlayer player)
            {
                var driver = CustomGetDriver();
                if (object.ReferenceEquals(driver, null) || driver.net.ID.Value != player.net.ID.Value)
                {
                    return;
                }

                PilotInput(inputState, player);
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

                if (!IsActive)
                {
                    return;
                }

                if (isPowered && lastWaterCheck + 1f < currentTime)
                {
                    lastWaterCheck = currentTime;
                    if (InWater)
                    {
                        TogglePower(false);
                    }
                }

                if (IsOn())
                {
                    if (hasPhysicalEngine && !physicalEngine.IsUsable)
                    {
                        engineController.StopEngine();
                        return;
                    }

                    EdgeTransferCheck();
                    var lgs = PlaneConfig.LandingGearSettings;
                    if (lgs.Enabled && LandingGearOn && CustomHasDriver())
                    {
                        if (lgs.AutoRetract && autoRetractReady && this.onGroundTilRealTime + lgs.AutoRetractAfterSeconds < currentTime && rigidBody.linearVelocity.magnitude > PlaneConfig.StallVelocity)
                        {
                            ToggleLandingGear(false);
                            KaruzaEntitiesCommon.Instance.ShowToast(CustomGetDriver(), VehicleConfig.GeneralToastSettings.LandingGearSwitchOffToast, GameTip.Styles.Red_Normal);
                        }
                        else if (PlaneConfig.GeneralToastSettings.Enabled && PlaneConfig.GeneralToastSettings.LandingGearOnWarningEnabled)
                        {
                            if (landingGearToastDisplayed)
                            {
                                if (this.onGroundTilRealTime >= currentTime)
                                {
                                    landingGearToastDisplayed = false;
                                }
                            }
                            else if (this.onGroundTilRealTime + PlaneConfig.GeneralToastSettings.LandingGearOnWarningDelay < currentTime)
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

            #region Props

            public void InitializeVehicle()
            {
                var config = PlaneConfig;
                this.torqueScale = config.TorqueScale;
                this.engineThrustMax = config.EngineThrustMax;
                this.fuelPerSec = VehicleConfig.FuelSettings.FuelPerSec;
                this.enableServerOcclusion = Instance.configuration.ForceServerOcclusion.HasValue ? Instance.configuration.ForceServerOcclusion.Value : config.EnableServerOcclusion;

                outsideDecayMinutes = Instance.configuration.ForceOutsideDecayMinutes ?? PlaneConfig.OutsideDecayMinutes;
                insideDecayMinutes = Instance.configuration.ForceInsideDecayMinutes ?? PlaneConfig.InsideDecayMinutes;
                timeAfterEngineOffToStartDecay = Instance.configuration.ForceTimeAfterEngineOffToStartDecay ?? PlaneConfig.TimeAfterEngineOffToStartDecay;
                groundedDistanceCheck = PlaneConfig.GroundedDistanceCheck;
                NoAmmoToast = config.GeneralToastSettings.Enabled && !string.IsNullOrEmpty(config.GeneralToastSettings.NoAmmoToast) ? config.GeneralToastSettings.NoAmmoToast : string.Empty;

                _maxHealth = config.MaxHealth;

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

                if (config.Bounds.Size != Vector3.zero)
                {
                    bounds = new Bounds() { size = config.Bounds.Size, center = config.Bounds.Center };
                }

                this.rigidBody.maxAngularVelocity = config.MaxAngularVelocity;
                this.waterSample.localPosition += config.WaterSampleModifier;

                loseControlHealthThreshold = MaxHealth() * PlaneConfig.LoseControlThreshold;

                this.mountAnchor = cachedTransform;

                InitializeWheelColliders();
                InitializeSamCollider();

                PropUtilities.InitializeVehicle(this);

                InitializeLowHealthEffects();

                InitializeChildrenCollections();
                if (PlaneConfig.DisableGlobalNetwork)
                {
                    this.EnableGlobalBroadcast(false);
                }

                IsActive = true;
                vehicleNetId = this.net.ID.Value;

                this.IsOnGround = true;
                this.onGroundTilRealTime = Time.realtimeSinceStartup + 1f;
                this.collisionAvailable = Time.time + 2.5f;
                this.nextDamageTime = Time.time + 1f;
                this.isPowered = false;
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

                engineController = new VehicleEngineController<PlaneController>(this, fuelSystem, true, PlaneConfig.EngineStartupTime, waterSample, Flags.Reserved4);

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

                hasBuoyancy = (PlaneConfig.BuoyancySettings?.Enabled).GetValueOrDefault();
                if (hasBuoyancy)
                {
                    buoyancy = GetComponent<Buoyancy>();
                }

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
                for (int i = 0; i < PlaneConfig.WheelColliders.Count; i++)
                {
                    var wcc = PlaneConfig.WheelColliders[i];
                    var wc = PropUtilities.InitializeWheelCollider(this, wcc, out BaseEntity visWheel);

                    var wheel = new Wheel()
                    {
                        wheelCollider = wc,
                        powerWheel = wcc.Power,
                        steering = wcc.Steering,
                        steeringModifier = wcc.SteeringModifier,
                        groundedTest = true
                    };

                    Wheels.Add(wheel);
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
            public bool UnlimitedAmmo { get { return Instance.configuration.ForceUnlimitedAmmo || VehicleConfig.UnlimitedAmmo; } }
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
                if (!invokingResetSleep)
                {
                    invokingResetSleep = true;
                    Invoke(ResetSleep, 1);
                }
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

            public void ResetSleep()
            {
                invokingResetSleep = false;
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
                if (LandingGearSettings.Enabled && LandingGearSettings.DisableWeaponsWhenOn && LandingGearOn)
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
                if (PlaneConfig.DoHitEffect && info.Initiator is BasePlayer attacker)
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
                    foreach (var fx in this.PlaneConfig.ExplosionFxs)
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

            public override void OnDied(HitInfo info)
            {
                if (Instance.configuration.DontKillPlayersOnDeath)
                {
                    base.OnDied(info);
                    return;
                }

                foreach (MountPointInfo mountPoint in mountPoints)
                {
                    if (!mountPoint.mountable.IsMounted())
                    {
                        continue;
                    }

                    var mounted = mountPoint.mountable.GetMounted();
                    HitInfo hitInfo = new HitInfo(info.Initiator, this, DamageType.Explosion, 1000f, base.transform.position);
                    hitInfo.Weapon = info.Weapon;
                    hitInfo.WeaponPrefab = info.WeaponPrefab;
                    mounted.Hurt(hitInfo);
                }

                base.OnDied(info);
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
                if (PlaneConfig.LowHealthEffectSettings == null || !PlaneConfig.LowHealthEffectSettings.Enabled)
                {
                    return;
                }

                var effects = PlaneConfig.LowHealthEffectSettings.Effects.OrderBy(e => e.HealthPercent).ToList();
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
                        if (hasRadio)
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

                        if (VehicleConfig.RadarSettings.Enabled && !isPowered)
                        {
                            var driverId = driver.userID.Get();
                            if (currentRadarUserId != driverId)
                            {
                                if (currentRadarUserId != 0)
                                {
                                    KaruzaEntitiesCommon.Instance.Radar.ClearRadarVehicles(this, currentRadarUser);
                                }

                                currentRadarUser = driver;
                                currentRadarUserId = driverId;
                            }

                            KaruzaEntitiesCommon.Instance.Radar.DrawRadarVehicles(this);
                            KaruzaEntitiesCommon.Instance.Radar.RegisterRadarVehicle(this);
                        }
                    }
                    else
                    {
                        if (VehicleConfig.RadarSettings.Enabled)
                        {
                            if (currentRadarUserId != 0)
                            {
                                KaruzaEntitiesCommon.Instance.Radar.ClearRadarVehicles(this, currentRadarUser);
                                currentRadarUser = null;
                                currentRadarUserId = 0;
                            }

                            KaruzaEntitiesCommon.Instance.Radar.RemoveRadarVehicle(this);
                        }

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
                else if (!on)
                {
                    if (VehicleConfig.RadarSettings.Enabled)
                    {
                        if (currentRadarUserId != 0)
                        {
                            KaruzaEntitiesCommon.Instance.Radar.ClearRadarVehicles(this, currentRadarUser);
                            currentRadarUser = null;
                            currentRadarUserId = 0;
                        }

                        KaruzaEntitiesCommon.Instance.Radar.RemoveRadarVehicle(this);
                    }
                }

                isPowered = on;
                OnPowerToggle?.Invoke(IsPowered);

                if (VehicleConfig.PowerSettings.TogglesLight)
                {
                    CustomLightToggle();
                }
            }

            #endregion

            #region Mounting

            public override bool GetDismountPosition(BasePlayer player, out Vector3 res, bool silent = false)
            {
                bool debugDismounts = ConVar.Debugging.DebugDismounts;
                if (PlaneConfig.CustomDismountPositions != null)
                {
                    Vector3 dismountCheckStart = GetDismountCheckStart(player);
                    List<Vector3> validDismounts = new List<Vector3>();

                    for (int i = 0; i < PlaneConfig.CustomDismountPositions.Count; i++)
                    {
                        var dmp = PlaneConfig.CustomDismountPositions[i];

                        var disPos = cachedTransform.TransformPoint(dmp.Location);

                        if (debugDismounts)
                        {
                            player.ChatMessage($"CustomDismountPositions debug: Checking dismount point {disPos} (CDM: {i}) from {dismountCheckStart}.");
                        }

                        if (Physics.CheckSphere(disPos, 0.5f, 1537319169))
                        {
                            if (debugDismounts)
                            {
                                player.ChatMessage($"<color=red>CustomDismountPositions debug: Dismount point {disPos} (CDM: {i}) is colliding</color>");
                            }

                            continue;
                        }

                        Vector3 position = disPos + cachedTransform.up * 0.5f;
                        if (!IsVisibleAndCanSee(position))
                        {
                            if (debugDismounts)
                            {
                                player.ChatMessage($"<color=red>CustomDismountPositions debug: Dismount point {disPos} (CDM: {i}) is not visible.</color>");
                            }

                            continue;
                        }

                        Vector3 vector = disPos + BasePlayer.NoClipOffset();
                        if (AntiHack.TestNoClipping(player, dismountCheckStart, vector, BasePlayer.NoClipRadius(ConVar.AntiHack.noclip_margin_dismount), ConVar.AntiHack.noclip_backtracking, out dismountCollider, overlapVehicleLayer: false, this))
                        {
                            if (debugDismounts)
                            {
                                player.ChatMessage($"<color=red>CustomDismountPositions debug: Dismount point {disPos} (CDM: {i}) is clipping.</color>");
                            }

                            continue;
                        }

                        if (debugDismounts)
                        {
                            player.ChatMessage($"<color=green>CustomDismountPositions debug: Dismount point {disPos} (CDM: {i}) is valid.</color>");
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

                    if (VehicleConfig.RadarSettings.Enabled)
                    {
                        if (currentRadarUserId != 0)
                        {
                            KaruzaEntitiesCommon.Instance.Radar.ClearRadarVehicles(this, currentRadarUser);
                            currentRadarUser = null;
                            currentRadarUserId = 0;
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
                var hasDriver = CustomHasDriver();
                var hasAccessToLock = !hasCodeLock || !codeLock.IsLocked() || (codeLock.whitelistPlayers.Contains(player.userID.Get()) || codeLock.guestPlayers.Contains(player.userID.Get()));

                if (hasAccessToLock)
                {
                    return true;
                }

                if (!hasDriver)
                {
                    return false;
                }

                var driver = CustomGetDriver();
                var filterDriver = false;
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
                var hasAccessToLock = !hasCodeLock || !codeLock.IsLocked() || (codeLock.whitelistPlayers.Contains(player.userID.Get()) || codeLock.guestPlayers.Contains(player.userID.Get()));
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
                    var creatorPlayer = creatorEntity as BasePlayer;
                    if (creatorPlayer.Team != null && player.Team != null && creatorPlayer.Team.members.Contains(player.userID.Get()))
                    {
                        filterDriver = true;
                    }
                    else
                    {
                        return null;
                    }
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
                    else
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

            #region Collision

            private void OnCollisionStay()
            {
                this.IsOnGround = true;
            }

            private void OnCollisionEnter(Collision collision)
            {
                this.ProcessCollision(collision);
            }

            public void DelayedImpactDamage()
            {
                float num = explosionForceMultiplier;
                explosionForceMultiplier = 0f;
                Hurt(pendingImpactDamage * MaxHealth(), DamageType.Explosion, this, useProtection: false);
                pendingImpactDamage = 0f;
                explosionForceMultiplier = num;
                invokingDelayedImpactDamage = false;
            }

            public void ProcessCollision(Collision collision)
            {
                if (!collisionReady && Time.time < collisionAvailable)
                {
                    return;
                }

                collisionReady = true;

                float magnitude = collision.relativeVelocity.magnitude;
                if ((IsOnGround && magnitude < PlaneConfig.CollisionMagnitude) || Time.time < nextDamageTime)
                {
                    return;
                }

                if (!object.ReferenceEquals(collision.gameObject, null))
                {
                    if (((1 << collision.collider.gameObject.layer) & 0x48A18101) <= 0)
                    {
                        return;
                    }

                    var entity = CollisionEx.GetEntity(collision);
                    if (!object.ReferenceEquals(entity, null) && entity is Parachute)
                    {
                        return;
                    }
                }

                float num = Mathf.InverseLerp(6.5f, 40f, magnitude);
                if (!(num > 0f))
                {
                    return;
                }

                pendingImpactDamage += Mathf.Max(num, 0.15f);
                if (Vector3.Dot(cachedTransform.up, Vector3.up) < 0.5f)
                {
                    pendingImpactDamage *= 5f;
                }

                var contact = collision.GetContact(0);
                var point = contact.point;
                TryShowCollisionFX(point);

                rigidBody.AddForceAtPosition(contact.normal * (0.3f * num), point, ForceMode.VelocityChange);
                nextDamageTime = Time.time + 0.333f;
                UpdateLoseControl(false);

                if (!invokingDelayedImpactDamage)
                {
                    invokingDelayedImpactDamage = true;
                    Invoke(DelayedImpactDamage, 0.015f);
                }
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
                    engineThrustMax = PlaneConfig.EngineThrustMax * PlaneConfig.BoostSettings.EnginePowerModifier;
                }
                else
                {
                    engineThrustMax = PlaneConfig.EngineThrustMax;
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
                    engineThrustMax = PlaneConfig.EngineThrustMax;
                    fuelPerSec = VehicleConfig.FuelSettings.FuelPerSec;
                    return;
                }

                var fuelModifier = fuelModifiers[currentFuelModifierSkinId];
                engineThrustMax = PlaneConfig.EngineThrustMax * fuelModifier.SpeedMultiplier;

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

            #region Landing Gear

            bool autoRetractReady;
            bool landingGearToastDisplayed;

            public bool LandingGearOn { get; set; }
            public Action<bool> OnLandingGearToggle { get; set; }
            public LandingGearSettings LandingGearSettings { get { return PlaneConfig.LandingGearSettings; } }

            public void ToggleLandingGear(bool wantsOn)
            {
                if (!LandingGearSettings.Enabled)
                {
                    LandingGearOn = true;
                    return;
                }

                if (LandingGearOn == wantsOn)
                {
                    return;
                }

                LandingGearOn = wantsOn;
                groundedDistanceCheck = PlaneConfig.GroundedDistanceCheck;

                if (wantsOn && LandingGearSettings.GroundedDistanceCheckWhileOn > -1)
                {
                    groundedDistanceCheck = LandingGearSettings.GroundedDistanceCheckWhileOn;
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

                if (LandingGearSettings.UpdateWheelColliders)
                {
                    var curVelocity = rigidBody.linearVelocity;

                    for (int i = 0; i < Wheels.Count; i++)
                    {
                        var wheel = Wheels[i];
                        wheel.wheelCollider.gameObject.SetActive(LandingGearOn);
                    }

                    rigidBody.linearVelocity = curVelocity;
                }

                OnLandingGearToggle?.Invoke(wantsOn);
            }

            #endregion

            #region Flight

            public void UpdateLoseControl(bool loseControl)
            {
                this.loseControl = loseControl;
            }

            void ResetStallState()
            {
                canStall = true;
            }

            public virtual void PilotInput(InputState inputState, BasePlayer player)
            {
                var wasVtol = this.currentInputState.enableVTOL;

                this.currentInputState.Reset();
                if (isPowered && !IsOn() && !IsStartingUp && inputState.IsDown(BUTTON.FORWARD) && !inputState.WasDown(BUTTON.FORWARD) && (!hasPhysicalEngine || physicalEngine.IsUsable) && (!hasWeakpoints || !Weakspots.Exists(wp => wp.LoseControlOnDeath && wp.IsDestroyed)))
                {
                    engineController.TryStartEngine(player);
                }

                if (PlaneConfig.EnableVTOL)
                {
                    if (inputState.IsDown(BUTTON.DUCK))
                    {
                        this.currentInputState.enableVTOL = true;
                    }

                    if (PlaneConfig.LandingGearSettings.Enabled)
                    {
                        if (LandingGearOn)
                        {
                            if (PlaneConfig.LandingGearSettings.ForceVTOLWhenOn)
                            {
                                this.currentInputState.enableVTOL = true;
                            }
                        }
                        else if (PlaneConfig.LandingGearSettings.DisableVTOLWhenOff)
                        {
                            this.currentInputState.enableVTOL = false;
                        }
                    }

                    if (this.currentInputState.enableVTOL)
                    {
                        if (this.rigidBody.linearVelocity.magnitude > PlaneConfig.MaxVTOLVelocity || IsFlipped())
                        {
                            this.currentInputState.enableVTOL = false;
                        }
                    }
                }

                if (wasVtol && !this.currentInputState.enableVTOL)
                {
                    canStall = false;
                    Invoke(ResetStallState, 2f);
                }

                if (inputState.IsDown(BUTTON.FORWARD))
                {
                    if (inputState.IsDown(BUTTON.DUCK) || this.currentInputState.enableVTOL)
                    {
                        this.currentInputState.throttle = 0.6f;
                    }
                    else
                    {
                        this.currentInputState.throttle = 1f;
                    }
                }
                else if (inputState.IsDown(BUTTON.BACKWARD))
                {
                    this.currentInputState.throttle = -1f;
                }
                else
                {
                    this.currentInputState.throttle = 0f;
                }

                if (!this.IsEngineOn())
                {
                    this.currentInputState.throttle = 0f;
                }

                this.currentInputState.yaw = inputState.IsDown(BUTTON.RIGHT) ? 1f : 0.0f;
                this.currentInputState.yaw -= inputState.IsDown(BUTTON.LEFT) ? 1f : 0.0f;
                this.currentInputState.pitch = HelperUtilities.MouseToBinary(inputState.current.mouseDelta.y, -0.95f, 0.70f);
                this.currentInputState.roll = HelperUtilities.MouseToBinary(-inputState.current.mouseDelta.x, -1f, 1f);
                this.lastPlayerInputTime = Time.time;

                UpdateWeapons(player);
                BoostUtilities.UpdateBoost(this, player);
            }

            public virtual void SetDefaultInputState()
            {
                this.currentInputState.Reset();
                this.currentInputState.throttle = 0f;
            }

            public virtual bool IsEngineOn()
            {
                return IsOn();
            }

            bool CustomHasChanged
            {
                get
                {
                    if (!invokingResetSleep && rigidBody.IsSleeping())
                    {
                        return false;
                    }

                    if (IsOnWater && rigidBody.linearVelocity.magnitude <= 0.5f)
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
                    if (tick >= 2)
                    {
                        tick = 0;
                        OnPhysicsUpdate();
                    }
                    else if (CustomHasChanged && ++tick >= 2)
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

            public override void VehicleFixedUpdate()
            {
                var isEngineOn = HasFlag(Flags.On);
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

                base.VehicleFixedUpdate();
                if (Time.time > lastPlayerInputTime + 0.5f)
                {
                    SetDefaultInputState();
                }

                engineController.CheckEngineState();
                engineController.TickFuel(fuelPerSec);

                var isMounted = IsMounted();
                if (!PlaneConfig.DisableGlobalNetwork)
                {
                    if (isMounted || isEngineOn || isPowered || loseControl)
                    {
                        EnableGlobalBroadcast(true);
                    }
                    else if (rigidBody.IsSleeping())
                    {
                        EnableGlobalBroadcast(false);
                    }
                    else if (rigidBody.linearVelocity.magnitude > 0.5f)
                    {
                        EnableGlobalBroadcast(true);
                    }
                    else
                    {
                        EnableGlobalBroadcast(false);
                    }
                }

                MovementUpdate(isEngineOn);
                UpdateGForces(isMounted);
                UpdateWheels();
            }

            void UpdateWheels()
            {
                if (!ConVar.vehicle.disable_wheels_when_sleeping)
                {
                    return;
                }

                if (Wheels.Count <= 0)
                {
                    return;
                }

                var disableWheels = !CustomHasDriver() && rigidBody.IsSleeping();
                if (disableWheels != wheelsDisabled)
                {
                    for (int i = 0; i < Wheels.Count; i++)
                    {
                        var w = Wheels[i];
                        w.wheelCollider.enabled = !disableWheels;
                    }

                    wheelsDisabled = disableWheels;
                }
            }

            private void PerformStall(bool aboveCeiling = false)
            {
                float horizonAngle = Vector3.Dot(cachedTransform.forward, Vector3.up);
                var force = PlaneConfig.ServiceCeilingForce;

                if (!aboveCeiling)
                {
                    force = PlaneConfig.StallForce;
                    if (LandingGearOn && PlaneConfig.LandingGearSettings.Enabled && PlaneConfig.LandingGearSettings.StallForceWhileOn > 0)
                    {
                        force = PlaneConfig.LandingGearSettings.StallForceWhileOn;
                    }
                }

                if (CustomHasDriver() && (horizonAngle > 0.1f || horizonAngle < -0.1f))
                {
                    this.rigidBody.AddForce(Vector3.up * force * 0.75f, ForceMode.Force);
                }
                else
                {
                    this.rigidBody.AddForce(Vector3.up * force, ForceMode.Force);
                }
            }

            public void ApplyForceAtWheels()
            {
                if (Wheels.Count <= 0)
                {
                    return;
                }

                var brakeScale = ((currentInputState.throttle == 0f) ? 50f : 0f);
                var throttle = currentThrottle;

                foreach (Wheel wheel in Wheels)
                {
                    ApplyWheelForce(wheel, throttle, brakeScale);
                }
            }

            public override void Spawn()
            {
                base.Spawn();
                PreventSleep();
                rigidBody.WakeUp();
            }

            public void ApplyWheelForce(Wheel wheel, float gasScale, float brakeScale)
            {
                if (!wheel.wheelCollider.isGrounded)
                {
                    return;
                }

                var yaw = currentInputState.yaw;
                var turning = wheel.steering ? yaw : 0;
                float num = gasScale * 750;
                float num2 = brakeScale * 2500;
                float num3 = wheel.steeringModifier * turning;

                if (wheel.powerWheel)
                {
                    if (!Mathf.Approximately(wheel.wheelCollider.motorTorque, num))
                    {
                        wheel.wheelCollider.motorTorque = num;
                    }
                }

                if (!Mathf.Approximately(wheel.wheelCollider.brakeTorque, num2))
                {
                    wheel.wheelCollider.brakeTorque = num2;
                }

                if (!Mathf.Approximately(wheel.wheelCollider.steerAngle, num3))
                {
                    wheel.wheelCollider.steerAngle = num3;
                }
            }

            public virtual float GetMinimumAltitudeTerrain()
            {
                return HotAirBalloon.minimumAltitudeTerrain;
            }

            public void MovementUpdate(bool isEngineOn)
            {
                if (hasTowVehicle)
                {
                    return;
                }

                var stallVelocity = PlaneConfig.StallVelocity;
                if (PlaneConfig.LandingGearSettings.Enabled)
                {
                    if (PlaneConfig.LandingGearSettings.DragWhileOn > 0)
                    {
                        var newDrag = PlaneConfig.Drag;
                        if (LandingGearOn)
                        {
                            newDrag = PlaneConfig.LandingGearSettings.DragWhileOn;
                        }

                        if (rigidBody.linearDamping != newDrag)
                        {
                            rigidBody.linearDamping = Mathf.Lerp(rigidBody.linearDamping, newDrag, 0.25f * Time.fixedDeltaTime);
                        }
                    }

                    if (PlaneConfig.LandingGearSettings.StallVelocityWhileOn > -1)
                    {
                        if (LandingGearOn)
                        {
                            stallVelocity = PlaneConfig.LandingGearSettings.StallVelocityWhileOn;
                        }
                    }
                }

                Vector3 localTorqueScale = PlaneConfig.TorqueScale;
                var lerpModifier = PlaneConfig.LerpModifier;
                var throttle = currentInputState.throttle;
                var roll = currentInputState.roll;
                var updateLerp = false;
                var doVTOL = currentInputState.enableVTOL;
                var isOnGround = this.IsOnGround;

                if (isOnGround)
                {
                    autoRetractReady = true;
                }

                if (!isOnGround && !this.InWater && !loseControl && lastFlipTime < Time.realtimeSinceStartup)
                {
                    bool isStalled = false;
                    bool aboveCeiling = false;

                    if (canStall && this.rigidBody.linearVelocity.magnitude < stallVelocity && !doVTOL)
                    {
                        isStalled = this.rigidBody.linearVelocity.magnitude < stallVelocity;
                    }

                    if (PlaneConfig.ServiceCeiling > 0)
                    {
                        var terrainHeight = Mathf.Max(GetMinimumAltitudeTerrain(), TerrainMeta.HeightMap.GetHeight(cachedTransform.position));
                        aboveCeiling = terrainHeight + PlaneConfig.ServiceCeiling < cachedTransform.position.y;
                    }

                    if (isStalled || aboveCeiling)
                    {
                        PerformStall(aboveCeiling);
                        localTorqueScale = torqueScale * 0.5f;
                        if (aboveCeiling)
                        {
                            return;
                        }
                    }
                    else
                    {
                        updateLerp = true;
                    }
                }
                else if (isOnGround)
                {
                    localTorqueScale = PlaneConfig.TakeOffTorqueScale;
                    UpdateLoseControl(false);
                }
                else if (this.InWater)
                {
                    UpdateLoseControl(false);
                }

                if (updateLerp && !doVTOL)
                {
                    // If engine is not on and has driver, then we're gliding 
                    // If we dont have a driver, yolo, make it slow down but little slower
                    // Otherwise if the engine is on and no throttle is applied then we also "gliding"
                    // Finally if negative throttle is applied (Reverse then make it slower than acceleration)
                    if (!isEngineOn)
                    {
                        if (this.CustomHasDriver())
                        {
                            lerpModifier *= 0.10f;
                        }
                        else
                        {
                            lerpModifier *= 0.20f;
                        }
                    }
                    else if (currentInputState.throttle == 0)
                    {
                        lerpModifier *= 0.1f;

                    }
                    else if (currentInputState.throttle < 0)
                    {
                        lerpModifier *= 0.25f;
                    }
                }

                if (loseControl)
                {
                    throttle = 1;
                    roll = 0.7f;
                }
                else if (IsBoosting)
                {
                    throttle = 1;
                }

                this.currentThrottle = Mathf.Lerp(this.currentThrottle, throttle, lerpModifier * Time.fixedDeltaTime);
                if (isOnGround && isEngineOn && !currentInputState.enableVTOL)
                {
                    this.currentThrottle = Mathf.Clamp(this.currentThrottle, -0.35f, PlaneConfig.MaxThrottle);
                }
                else
                {
                    this.currentThrottle = Mathf.Clamp(this.currentThrottle, -0.01f, PlaneConfig.MaxThrottle);
                }

                if (this.currentThrottle <= 0.1 && !isEngineOn)
                {
                    if (this.currentThrottle > 0 && (isOnGround || InWater))
                    {
                        this.currentThrottle = 0;
                        ApplyForceAtWheels();
                    }

                    return;
                }

                var localEngineThrust = engineThrustMax;
                var localLiftModifier = PlaneConfig.LiftModifier;
                var localTakeOffLiftModifier = PlaneConfig.TakeOffLiftModifier;
                var localVTOLMod = PlaneConfig.VTOLModifier;
                if (hasPhysicalEngine && !loseControl)
                {
                    localEngineThrust *= physicalEngine.PerformanceFractionTopSpeed;
                    localTorqueScale *= physicalEngine.PerformanceFractionAcceleration;
                    localLiftModifier *= physicalEngine.PerformanceFractionTopSpeed;
                    localTakeOffLiftModifier *= physicalEngine.PerformanceFractionTopSpeed;
                    localVTOLMod *= physicalEngine.PerformanceFractionAcceleration;
                }

                float angleOfAttack = -Mathf.Deg2Rad * Vector3.Dot(this.rigidBody.linearVelocity, cachedTransform.up);
                float slipAoA = Mathf.Deg2Rad * Vector3.Dot(this.rigidBody.linearVelocity, cachedTransform.right);

                var sideSlipCoefficient = 0.001f * slipAoA;
                var liftCoefficient = 8000 * angleOfAttack;
                var thrustCoefficient = this.currentThrottle * localEngineThrust;

                var rollCoEfficient = roll * localTorqueScale.z;
                var pitchCoEfficient = currentInputState.pitch * localTorqueScale.x;
                float yawCoEfficient = currentInputState.yaw * localTorqueScale.y;
                pitchCoEfficient += liftCoefficient * 0.001f;
                yawCoEfficient += -sideSlipCoefficient * 2f;
                rollCoEfficient += -sideSlipCoefficient * 0.1f;

                var ThrustForceVector = (this.rigidBody.linearVelocity.normalized + (cachedTransform.forward * 2f)) * thrustCoefficient;
                var LiftVector = cachedTransform.up * liftCoefficient;
                var SideSlip = Vector3.right * sideSlipCoefficient;

                var RollTorque = Vector3.forward * rollCoEfficient;
                var YawTorque = cachedTransform.up * yawCoEfficient;
                var PitchTorque = (Vector3.right * pitchCoEfficient) * 2;

                if (doVTOL && !loseControl)
                {
                    this.rigidBody.AddForce((cachedTransform.up * localVTOLMod) * thrustCoefficient, ForceMode.Force);
                    this.rigidBody.AddRelativeTorque(RollTorque);
                    this.rigidBody.AddTorque(YawTorque);

                    if (PlaneConfig.VTOLAllowForwardMovement && thrustCoefficient != 0)
                    {
                        this.rigidBody.AddForce(ThrustForceVector, ForceMode.Force);
                    }
                }
                else
                {
                    if (thrustCoefficient != 0)
                    {
                        this.rigidBody.AddForce(ThrustForceVector, ForceMode.Force);
                    }

                    if (isOnGround)
                    {
                        if (this.currentThrottle != 0)
                        {
                            this.rigidBody.AddForce(LiftVector * localTakeOffLiftModifier, ForceMode.Force);
                            if (Wheels.Count <= 0)
                            {
                                if (currentInputState.yaw > 0)
                                {
                                    this.rigidBody.transform.Rotate(this.rigidBody.transform.up, 1);
                                }
                                else if (currentInputState.yaw < 0)
                                {
                                    this.rigidBody.transform.Rotate(this.rigidBody.transform.up, -1);
                                }
                            }
                            else
                            {
                                ApplyForceAtWheels();
                                this.rigidBody.AddRelativeTorque(RollTorque);
                            }
                        }
                    }
                    else
                    {
                        if (loseControl)
                        {
                            var eulerAngles = cachedTransform.rotation.eulerAngles;
                            if (eulerAngles.x > 90 || eulerAngles.x < 35)
                            {
                                eulerAngles = Vector3.RotateTowards(eulerAngles, Vector3.down, 0.15f * Time.deltaTime, 0);
                                cachedTransform.rotation = Quaternion.Euler(eulerAngles);
                            }
                        }
                        else
                        {
                            this.rigidBody.AddForce(LiftVector * localLiftModifier, ForceMode.Force);
                            this.rigidBody.AddForce(SideSlip, ForceMode.Force);
                        }

                        this.rigidBody.AddRelativeTorque(RollTorque);
                        this.rigidBody.AddTorque(YawTorque);
                    }
                }

                this.rigidBody.AddRelativeTorque(PitchTorque);
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

            public override float AirFactor()
            {
                if (blackoutThresholdExceeded || redoutThresholdExceeded)
                {
                    return 0;
                }

                return base.AirFactor();
            }

            public override bool BlocksWaterFor(BasePlayer player)
            {
                if (blackoutThresholdExceeded || redoutThresholdExceeded)
                {
                    return true;
                }

                return base.BlocksWaterFor(player);
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

            public bool IsValidHomingTarget()
            {
                return forceSeekerCanLock || IsEngineOn();
            }

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

            #region Engine

            public void OnEngineStartFailed()
            {
            }

            public void FinishStartingEngine()
            {

            }

            public bool MeetsEngineRequirements()
            {
                if (engineController.IsOff)
                {
                    return HasDriver();
                }

                if (!HasDriver())
                {
                    return Time.time <= lastPlayerInputTime + 1f;
                }

                if (!isPowered)
                {
                    return false;
                }

                return true;
            }

            #endregion

            #region SAMSite

            void InitializeSamCollider()
            {
                var col = this.gameObject.AddComponent<SphereCollider>();
                col.radius = 0.1f;
            }

            public bool IsValidSAMTarget(bool staticRespawn)
            {
                return IsOn() && !InSafeZone();
            }

            #endregion

            #region Misc

            public override bool SupportsServerOcclusion()
            {
                return enableServerOcclusion;
            }

            public void AddTowingVehicle()
            {
                hasTowVehicle = true;
                ++towVehicles;
            }

            public void RemoveTowingVehicle()
            {
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

            public override IFuelSystem GetFuelSystem()
            {
                return engineController?.FuelSystem;
            }

            public float GetFuelFraction(bool force = false)
            {
                if (base.isServer && (timeSinceCachedFuelFraction > 1f || force))
                {
                    cachedFuelFraction = Mathf.Clamp01(GetFuelSystem().GetFuelAmount() / fuelGaugeMax);
                    timeSinceCachedFuelFraction = 0f;
                }

                return cachedFuelFraction;
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

                if (!isPowered)
                {
                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(BaseEntity.Flags.Reserved5, false);
                    }
                }
                else
                {
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
                if (timeSinceLastPush < PlaneConfig.PushCooldown)
                {
                    return false;
                }

                if (base.CanPushNow(pusher) && pusher.IsOnGround() && (!hasCodeLock || !codeLock.IsLocked() || codeLock.OnTryToOpen(pusher)))
                {
                    return !pusher.isMounted && !this.HasDriver();
                }

                return false;
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

                for (int i = 0; i < mountPoints.Count; i++)
                {
                    var mountPoint = mountPoints[i];
                    if (mountPoint.mountable == null)
                    {
                        continue;
                    }

                    if (!mountPoint.mountable.AnyMounted())
                    {
                        continue;
                    }

                    var mounted = mountPoint.mountable.GetMounted();

                    if (VehicleConfig.GForceSettings.Enabled)
                    {
                        GForceGUIUtilities.HideBlackoutGUI(mounted);
                        GForceGUIUtilities.HideRedoutGUI(mounted);
                    }

                    if (!Instance.IsUnloading)
                    {
                        var hitInfo = Pool.Get<HitInfo>();
                        hitInfo.Init(this, mounted, DamageType.Explosion, 1000f, cachedTransform.position);
                        mounted.Hurt(hitInfo);
                    }
                }

                SeismicSensor.Notify(cachedTransform.position, 1);
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
                Wheels?.Clear();
                Wheels = null;
                cachedConnections?.Clear();
                cachedConnections = null;
                WeaponSystems?.Clear();
                WeaponSystems = null;
                SpecialGuns?.Clear();
                SpecialGuns = null;
                raycastHit = null;
                cockpitRadio = null;
                radioBroadcaster = null;
                FuelContainer = null;
                AmmoContainer = null;
                buoyancy = null;
                engineController = null;
            }

            public override void Load(LoadInfo info)
            {
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
                if (PlaneConfig == null)
                {
                    return PREFAB_WORLD;
                }

                switch (PlaneConfig.BodyType)
                {
                    case BodyType.Gib:
                    case BodyType.Prefab:
                        return PlaneConfig.BodyGibPrefab;

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
                switch (PlaneConfig.BodyType)
                {
                    case BodyType.Gib:
                        info.msg.servergib = Facepunch.Pool.Get<ProtoBuf.ServerGib>();
                        info.msg.servergib.gibName = PlaneConfig.BodyGibName;
                        break;

                    case BodyType.WorldItem:
                    default:
                        info.msg.worldItem = Facepunch.Pool.Get<ProtoBuf.WorldItem>();
                        info.msg.worldItem.item = Pool.Get<ProtoBuf.Item>();
                        info.msg.worldItem.item.itemid = PlaneConfig.BodyItemId;
                        info.msg.worldItem.item.skinid = PlaneConfig.BodySkinId;
                        info.msg.worldItem.item.name = " ";
                        break;
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
                else if (!PlaneConfig.EnableEdgeTransfer)
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

            #region Inner Classes

            public class Wheel
            {
                public WheelCollider wheelCollider;

                public bool groundedTest = true;

                public bool powerWheel;
                public bool steering;
                public float steeringModifier;
            }

            public class PlaneInputState
            {
                public float throttle;

                public float roll;

                public float yaw;

                public float pitch;

                public bool enableVTOL;

                public void Reset()
                {
                    throttle = 0f;
                    roll = 0f;
                    yaw = 0f;
                    pitch = 0f;
                    enableVTOL = false;
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
                    var wp = Weakspots[i];
                    hasWeakpoints = true;
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
        }

        #endregion

        #region Configuration

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
            public bool DontKillPlayersOnDeath { get; set; } = false;
        }

        public class PlaneConfig : CustomVehicleConfig, ILandingGearConfig
        {
            public override SeekerStrength SeekerStrength { get; set; } = SeekerStrength.MEDIUM;
            public ConfigVector TakeOffTorqueScale { get; set; }
            public float MaxThrottle { get; set; }
            public float LiftModifier { get; set; }
            public float TakeOffLiftModifier { get; set; }
            public float StallForce { get; set; }
            public float StallVelocity { get; set; }
            public float LerpModifier { get; set; }
            public float ServiceCeiling { get; set; }
            public float ServiceCeilingForce { get; set; }
            public float CollisionMagnitude { get; set; } = 40f;
            public bool EnableVTOL { get; set; }
            public float VTOLModifier { get; set; }
            public float MaxVTOLVelocity { get; set; } = 15f;
            public bool VTOLAllowForwardMovement { get; set; } = false;
            public float LoseControlThreshold { get; set; } = 0.10f;
            public bool DisableGlobalNetwork { get; set; } = false;
            public float EngineThrustMax { get; set; }
            public float GroundedDistanceCheck { get; set; } = 3f;
            public bool EnableEdgeTransfer { get; set; }
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
                    TryUpdatePlaneConfig(hc.Key, hc.Value);
                }

                SaveConfig();
            }
            catch (Exception ex)
            {
                Config.WriteObject(configuration, false, $"{Interface.Oxide.ConfigDirectory}/{Name}.jsonError");
                PrintError($"The configuration file contains an error. {ex}");
            }
        }

        public static void TryUpdatePlaneConfig(string vehicleName, PlaneConfig config)
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

                var pc = Config.ReadObject<PlaneConfig>(filePath);
                TempConfigs.Add(fileName, pc);
            }
        }

        void LoadVehicleConfig(string vehicleName)
        {
            try
            {
                var filePath = $"{DirectoryPath}{vehicleName}.json";
                var config = Config.ReadObject<PlaneConfig>(filePath);

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

        public static void RegisterVehicleBundle(string vehicleName, PlaneConfig planeConfig)
        {
            Instance.PlaneConfigs[vehicleName] = planeConfig;
            InitializeAmmoTypes(planeConfig);

            var path = $"assets/custom/{vehicleName}.prefab";
            if (GameManifest.pathToGuid.ContainsKey(path))
            {
                return;
            }

            var baseCombat = new CustomEntities.CustomPrefabBaseCombat
            {
                HealthStart = planeConfig.MaxHealth,
                HealthMax = planeConfig.MaxHealth,
                MarkAttackerHostile = true,
                RepairEnabled = false,
                PickupEnabled = false,
                ProtectionProperties = planeConfig.ProtectionProperties
                    .OrderBy(k => k.Key)
                    .Select(v => v.Value)
                    .ToArray(),
            };

            var recipe = new CustomEntities.CustomPrefabRecipe(vehicleName, typeof(PlaneController), Layer.Vehicle_World, baseCombat, true);
            var recipes = bundle?.Recipes?.ToList() ?? new List<GenericPrefabRecipe>();
            recipes.Add(recipe);

            bundle = new CustomEntities.CustomPrefabBundle(Instance, recipes.ToArray());
            if (!CustomEntities.CustomPrefabs.RegisterAndLoadBundle(bundle))
            {
                Instance.PrintError("Bundles failed to load");
            }
        }

        public static void RegisterVehicleBundles(Dictionary<string, PlaneConfig> planeConfigs)
        {
            var recipes = bundle?.Recipes?.ToList() ?? new List<GenericPrefabRecipe>();
            foreach (var config in planeConfigs)
            {
                var vehicleName = config.Key;
                var vehicleConfig = config.Value;
                var path = $"assets/custom/{vehicleName}.prefab";

                Instance.PlaneConfigs[config.Key] = config.Value;
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

                var recipe = new CustomEntities.CustomPrefabRecipe(vehicleName, typeof(PlaneController), Layer.Vehicle_World, baseCombat, true);
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
                var vehicleConfig = config.Value as PlaneConfig;
                TryUpdatePlaneConfig(config.Key, vehicleConfig);

                Instance.PlaneConfigs[config.Key] = vehicleConfig;
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

                var recipe = new CustomEntities.CustomPrefabRecipe(vehicleName, typeof(PlaneController), Layer.Vehicle_World, baseCombat, true);
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
