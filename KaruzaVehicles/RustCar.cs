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
    [Info("RustCar", "Karuza", "1.24.0")]
    public class RustCar : RustPlugin, IKaruzaEntityPlugin
    {
        public static RustCar Instance;

        private Configuration configuration;
        public Dictionary<string, CarConfig> TempConfigs = new Dictionary<string, CarConfig>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, CarConfig> CarConfigs = new Dictionary<string, CarConfig>(StringComparer.OrdinalIgnoreCase);

        public bool IsUnloading = false;

        internal void OnServerInitialized()
        {
            Instance = this;
            LoadConfig();

            if (TempConfigs.Count > 0)
            {
                RegisterVehicleBundles(TempConfigs);
                TempConfigs.Clear();
            }

            RequestEntityConfigs<CarConfig>(this, 3);
        }

        internal void Unload()
        {
            IsUnloading = true;
            SaveAndUnregisterVehicleBundles();

            configuration = null;
            Instance = null;
        }

        [ConsoleCommand("rustcar.reload")]
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

        #region Car Behaviors

        public class CustomCarWheel
        {
            public BaseEntity BaseEntity;
            public CarWheel CarWheel;
            public bool IsFront;
            public bool IsLeft;
        }

        public class RustCarController : GroundVehicle, TakeCollisionDamage.ICanRestoreVelocity, CarPhysics<RustCarController>.ICar, IRadio, ICustomEntity, IVehicle, IWeaponController, ISeekerTargetOwner, ITowing
        {
            #region Constants

            const float PI_DOUBLED = 3.14159274101257f * 2.0f;

            #endregion
            
            #region Variables

            public bool IsActive { get; set; } = false;
            public bool WasStable;

            public CarConfig CarConfig;
            public BaseEntityConfig Config { get { return CarConfig; } }
            public BaseVehicleConfig VehicleConfig { get { return CarConfig; } }
            public DriverSeatInputs CurrentInputState = new DriverSeatInputs();

            Telephone cockpitRadio;
            SpecialBroadcaster radioBroadcaster;
            SpecialBaseCombatEntity lowHealthPrefab;
            CodeLock codeLock;
            EngineStorageContainer physicalEngine;
            Transform cachedTransform;

            List<SpecialFakeEngine> fakeEngines = new List<SpecialFakeEngine>();
            List<BaseEntity> cockpitElectical = new List<BaseEntity>();
            List<SpecialLight> vehicleLights = new List<SpecialLight>();
            List<Connection> cachedConnections = new List<Connection>();
            List<SleepingBag> respawnPoints = new List<SleepingBag>();

            List<Tuple<float, LowHealthEffectConfig>> lowHealthThresholds = new List<Tuple<float, LowHealthEffectConfig>>();

            OrderedDictionary driverCache = new OrderedDictionary();

            float currentTime;
            float lastFlipTime;
            float onGroundTilRealTime;
            float timeSinceCachedFuelFraction;
            float cachedFuelFraction;
            float fuelGaugeMax = 100f;
            float currentHoverThrottle;
            float hoverGroundedDistanceCheck;
            float hoverMaxSpeed;
            float outsideDecayMinutes;
            float insideDecayMinutes;
            float timeAfterEngineOffToStartDecay;
            float fuelPerSec;
            float cachedSpeed;

            bool hasSwitch;
            bool hasRadio;
            bool hasBroadcaster;
            bool hasEngine;
            bool hasHurtTriggers;
            bool invokeStopSpecialGunsStarted;
            bool isFakeEnginesOn;
            bool loadedFromSave;
            bool isPowered;
            bool hasStorage;
            bool hasFuelStorage;
            bool hasPhysicalEngine;
            bool hasBoostStorage;
            bool hasDriftHandbrake;
            bool steeringMode;
            bool enableServerOcclusion;
            bool hasBuoyancy;
            bool centralLockingEnabled;
            bool hasCodeLock;
            bool lowHealthEffectsValid;
            bool hasCarPhysics;
            bool hasHoverEngines;
            bool hasRespawns;
            bool invokingLowHealthFx;
            bool isAmphibious;
            bool hasCreator;

            int towVehicles;
            int towedVehicles;

            bool hasVisibleWheels;
            Buoyancy buoyancy;
            List<CustomCarWheel> visibleWheels = new List<CustomCarWheel>();
            HoverEngine[] hoverEngines = new HoverEngine[0];
            Dictionary<ulong, FuelModifierSetting> fuelModifiers = new Dictionary<ulong, FuelModifierSetting>();
            List<JointController> inputJoints = new List<JointController>();

            LowHealthEffectConfig spawnedLowHealthEffect;

            HitInfo lastServerProjectileAttack;
            bool invokingClearLastAttack;

            RaycastHit[] raycastHit;
            //IRemoteControllable ptzCamera;

            ulong vehicleNetId;
            ulong currentFuelModifierSkinId;

            int tick;
            Vector3 oldPos;
            Quaternion oldRot;

            public override float PositionTickRate => 0.1f;
            public override bool AlwaysAllowBradleyTargeting => true;
            public bool IsDrifting;
            public float EngineKw;
            public bool HasTowedVehicle;
            public bool HasTowVehicle;

            public CustomCarPhysics carPhysics;

            public VehicleTerrainHandler serverTerrainHandler;

            private CarWheel[] wheels = new CarWheel[0];

            CarSettings carSettings;
            public float lastEngineOnTime;
            public Action<bool> OnDriftToggle;

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

                    return (CarConfig?.EnableSaving).GetValueOrDefault();
                }
            }

            public virtual bool HasDefaultInventory => false;
            public virtual bool DefaultInventoryHandledByBaseType => false;
            public ItemContainer DefaultInventory { get; set; } = null;
            public virtual int DefaultInventoryCapacity { get { return 0; } }
            #endregion

            #region WeaponController

            public IKaruzaCustomEntity KaruzaCustomEntity { get { return this; } }
            public BaseEntity WeaponControllerEntity { get { return KaruzaCustomEntity.BaseEntity; } }


            #endregion

            public List<IStorageContainer> StorageContainers { get; private set; } = new List<IStorageContainer>();
            public VehicleBoostContainer BoostContainer { get; private set; }
            public VehicleFuelContainer FuelContainer { get; private set; }
            public Dictionary<HurtTriggerType, Dictionary<HurtTriggerConfig, TriggerHurtNotChild>> HurtTriggers { get; } = new Dictionary<HurtTriggerType, Dictionary<HurtTriggerConfig, TriggerHurtNotChild>>();
            public BaseEntity BaseEntity { get { return this; } }

            public bool IsPowered
            {
                get
                {
                    return isPowered;
                }
            }

            public SuperBaseEntityConfig BaseConfig { get { return CarConfig; } }
            public BodyType BodyType { get { return BaseConfig.BodyType; } }
            public BaseVehicle BaseVehicle { get { return this; } }
            public List<MountPointInfo> MountPoints { get { return this.mountPoints; } }
            public IFuelSystem FuelSystem { get { return this.engineController.FuelSystem; } }
            public int RadioFrequency { get { return radioBroadcaster.frequency; } }
            public float LastAmmoUpdate { get; set; }
            public float NextDryFireEffect { get; set; }
            public Action OnPhysicsUpdate { get; set; }
            public Action OnMountedChange { get; set; }
            public Action<bool> OnToggled { get; set; }
            public Action<bool> OnPowerToggle { get; set; }
            public Action<float, float> OnHealthChange { get; set; }
            public float SteerAngle { get; set; }
            public float BoostTime { get; set; }
            public bool BoostOnCooldown { get; set; }
            public Action OnBoostTimeUpdate { get; set; }

            public bool HasLock { get { return hasCodeLock; } }

            public bool IsStunting { get; set; }
            public bool Stabilize { get; set; }
            public bool IsOnGround
            {
                get
                {
                    if (this.onGroundTilRealTime > Time.realtimeSinceStartup || this.rigidBody.IsSleeping())
                    {
                        return true;
                    }

                    if (hasBuoyancy && (buoyancy.InWater || buoyancy.timeOutOfWater < 0.10f))
                    {
                        this.onGroundTilRealTime = Time.realtimeSinceStartup + (IsEngineOn() ? 0.01f : CarConfig.EngineStartupTime);
                        return true;
                    }

                    if (hasCarPhysics)
                    {
                        foreach (var wheel in wheels)
                        {
                            if (wheel.wheelCollider.isGrounded)
                            {
                                this.onGroundTilRealTime = Time.realtimeSinceStartup + 0.01f;
                                return true;
                            }
                        }
                    }
                    else if (hasHoverEngines)
                    {
                        for (int i = 0; i < hoverEngines.Length; i++)
                        {
                            var he = hoverEngines[i];
                            if (he.IsGrounded)
                            {
                                this.onGroundTilRealTime = Time.realtimeSinceStartup + 0.01f;
                                return true;
                            }
                        }
                    }
                    else if (Physics.RaycastNonAlloc(cachedTransform.position, cachedTransform.TransformDirection(-Vector3.up), raycastHit, hoverGroundedDistanceCheck, HOVER_COLLIDER) > 0)
                    {
                        this.onGroundTilRealTime = Time.realtimeSinceStartup + (IsEngineOn() ? 0.01f : CarConfig.EngineStartupTime);
                        return true;
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

            public bool IsStartingUp
            {
                get
                {
                    return engineController.IsStarting;
                }
            }

            public override float DriveWheelVelocity
            {
                get
                {
                    if (IsTowing)
                    {
                        return rigidBody.linearVelocity.magnitude;
                    }

                    if (hasCarPhysics)
                    {
                        return carPhysics.DriveWheelVelocity;
                    }

                    return 0f;
                }
            }

            public VehicleTerrainHandler.Surface OnSurface
            {
                get
                {
                    if (serverTerrainHandler == null)
                    {
                        return VehicleTerrainHandler.Surface.Default;
                    }

                    return serverTerrainHandler.OnSurface;
                }
            }

            public void RestoreVelocity(Vector3 vel)
            {
                if (rigidBody.linearVelocity.sqrMagnitude < vel.sqrMagnitude)
                {
                    vel.y = rigidBody.linearVelocity.y;
                    rigidBody.linearVelocity = vel;
                }
            }

            public bool IsEjectEligible { get { return true; } }

            public virtual bool IsEngineOn()
            {
                return IsOn();
            }

            public override float GetThrottleInput()
            {
                if (HasTowVehicle)
                {
                    return 0;
                }

                if (IsBoosting)
                {
                    return 1;
                }
                else if (IsEngineOn())
                {
                    float throttle = CurrentInputState.throttleInput;
                    return Mathf.Clamp(throttle, -1f, 1f);
                }

                return 0f;
            }

            public override float GetMaxForwardSpeed()
            {
                var num = GetMaxDriveForce() / rigidBody.mass * 30f;
                return Mathf.Pow(0.9945f, num) * num;
            }

            public float GetMaxDriveForce()
            {
                var pfts = CarConfig.PerformanceFractionTopSpeed;
                if (hasPhysicalEngine)
                {
                    pfts = physicalEngine.PerformanceFractionTopSpeed * CarConfig.PerformanceFractionTopSpeed;
                }

                var force = EngineKw * 12.75f * pfts;
                return RollOffDriveForce(force);
            }

            public float RollOffDriveForce(float driveForce)
            {
                return Mathf.Pow(0.9999175f, driveForce) * driveForce;
            }

            public override float GetBrakeInput()
            {
                if (HasTowVehicle)
                {
                    return 0;
                }

                var brakeInput = CurrentInputState.brakeInput;
                return Mathf.Clamp01(brakeInput);
            }

            public override bool MeetsEngineRequirements()
            {
                if (!isPowered)
                {
                    return false;
                }

                if (hasPhysicalEngine && !physicalEngine.IsUsable)
                {
                    return false;
                }

                return HasDriver();
            }

            public override void OnEngineStartFailed()
            {
            }


            public float GetSteerInput()
            {
                var steerInput = CurrentInputState.steerInput;
                return Mathf.Clamp(steerInput, -1f, 1f);
            }

            public bool GetSteerSpeedMod(float speed)
            {
                if (CurrentInputState.steerMod)
                {
                    return true;
                }

                return false;
            }

            public float GetSteerMaxMult(float speed)
            {
                return 1f;
            }

            public float GetAdjustedDriveForce(float absSpeed, float topSpeed)
            {
                float maxDriveForce = GetMaxDriveForce();
                var pfa = CarConfig.PerformanceFractionAcceleration;
                if (hasPhysicalEngine)
                {
                    pfa = physicalEngine.PerformanceFractionAcceleration * CarConfig.PerformanceFractionAcceleration;
                }

                float num = UnityEngine.MathEx.BiasedLerp(bias: Mathf.Lerp(0.0002f, 0.7f, pfa), x: 1f - absSpeed / topSpeed);
                var adjustedDriveForce = maxDriveForce * num;

                return RollOffDriveForce(adjustedDriveForce);
            }

            public CarWheel[] GetWheels()
            {
                return wheels;
            }

            public float GetWheelsMidPos()
            {
                if (wheels.Length <= 0)
                {
                    return cachedTransform.localPosition.z;
                }

                if (wheels.Length == 1)
                {
                    return wheels[0].wheelCollider.transform.localPosition.z * 0.5f;
                }

                if (wheels.Length == 2)
                {
                    return (wheels[0].wheelCollider.transform.localPosition.z - wheels[1].wheelCollider.transform.localPosition.z) * 0.5f;
                }

                return (wheels[0].wheelCollider.transform.localPosition.z - wheels[2].wheelCollider.transform.localPosition.z) * 0.5f;
            }

            public void AddTowingVehicle()
            {
                if (!HasTowVehicle)
                {
                    HasTowVehicle = true;
                    if (hasCarPhysics)
                    {
                        carPhysics.vehicleSettings.disableHandbrakes = true;
                    }
                }

                ++towVehicles;
            }

            public void RemoveTowingVehicle()
            {
                if (!HasTowVehicle)
                {
                    return;
                }

                --towVehicles;
                if (towVehicles <= 0)
                {
                    towVehicles = 0;
                    HasTowVehicle = false;
                    if (hasCarPhysics)
                    {
                        carPhysics.vehicleSettings.disableHandbrakes = false;
                    }
                }
            }

            public void AddTowedVehicle()
            {
                if (!HasTowedVehicle)
                {
                    HasTowedVehicle = true;
                }

                ++towedVehicles;
            }

            public void RemoveTowedVehicle()
            {
                if (!HasTowedVehicle)
                {
                    return;
                }

                --towedVehicles;
                if (towedVehicles <= 0)
                {
                    towedVehicles = 0;
                    HasTowedVehicle = false;
                }
            }

            #endregion

            #region Hooks

            public void Awake()
            {
                this.cachedTransform = transform;

                this.rigidBody = this.gameObject.GetOrAddComponent<Rigidbody>();
                this.rigidBody.isKinematic = false;
                this.rigidBody.useGravity = true;
                this.rigidBody.detectCollisions = true;
                this.rigidBody.interpolation = RigidbodyInterpolation.None;
                this.rigidBody.collisionDetectionMode = CollisionDetectionMode.Discrete;
                savedCollisionDetectionMode = CollisionDetectionMode.Discrete;

                this.rigidBody.constraints = RigidbodyConstraints.None;
                this.rigidBody.sleepThreshold = 0;

                var waterSample = new GameObject();
                waterSample.transform.SetParent(cachedTransform, false);
                this.waterloggedPoint = waterSample.transform;


                var mountAnchorObj = new GameObject();
                mountAnchorObj.transform.SetParent(cachedTransform, false);
                this.mountAnchor = mountAnchorObj.transform;

                this.propDirection = new DirectionProperties[0];
                this.legacyDismount = false;

                this.syncPosition = true;
                this.mountPoints = new List<MountPointInfo>();
                //this.nextDamageTime = Time.time + 1f;

                this.explosionForceMultiplier = 200;
                this.explosionForceMax = 40000;
                this.canTriggerParent = false;

                if (!string.IsNullOrEmpty(this.ShortPrefabName))
                {
                    this.CarConfig = Instance.CarConfigs[this.ShortPrefabName];
                }

                CustomHandler.AttachNewHandlerToCustomEntityIfNotPrototype(this);
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

                clearRecentDriverAction = ClearRecentDriver;

                Query.Server.Add(this);
                AllMountables.Add(this);

                carSettings = new CarSettings()
                {
                    rollingResistance = CarConfig.RollingResistance,
                    antiRoll = CarConfig.AntiRoll,
                    canSleep = CarConfig.CanSleep,
                    tankSteering = CarConfig.TankSteering,
                    maxSteerAngle = CarConfig.MaxSteerAngle,
                    steeringAssist = CarConfig.SteeringAssist,
                    steeringAssistRatio = CarConfig.SteeringAssistRatio,
                    steeringLimit = CarConfig.SteeringLimit,
                    minSteerLimitAngle = CarConfig.MinSteerLimitAngle,
                    minSteerLimitSpeed = CarConfig.MinSteerLimitSpeed,
                    rearWheelSteer = CarConfig.RearWheelSteer,
                    steerMinLerpSpeed = CarConfig.SteerMinLerpSpeed,
                    steerMaxLerpSpeed = CarConfig.SteerMaxLerpSpeed,
                    steerReturnLerpSpeed = CarConfig.SteerReturnLerpSpeed,
                    maxDriveSlip = CarConfig.MaxDriveSlip,
                    driveForceToMaxSlip = CarConfig.DriveForceToMaxSlip,
                    reversePercentSpeed = CarConfig.ReversePercentSpeed,
                    brakeForceMultiplier = CarConfig.BrakeForceMultiplier,
                    handlingBias = CarConfig.HandlingBias
                };

                if (CarConfig.DrivingStyle.HasFlags(DrivingStyle.Car) || CarConfig.DrivingStyle.HasFlags(DrivingStyle.Bike))
                {
                    carPhysics = new CustomCarPhysics(this, cachedTransform, rigidBody, carSettings);
                    hasCarPhysics = true;
                }

                if (CarConfig.DrivingStyle.HasFlags(DrivingStyle.Hover))
                {
                    hasHoverEngines = true;
                    hoverEngines = GetComponentsInChildren<HoverEngine>();
                }

                if (CarConfig.DrivingStyle.HasFlag(DrivingStyle.Amphibious))
                {
                    isAmphibious = true;
                }

                serverTerrainHandler = new VehicleTerrainHandler(this);

                lastEngineOnTime = Time.time;
                InvokeRandomized(UpdateNetwork, 0f, 0.15f, 0.02f);

                if (!Instance.configuration.DisableDecay)
                {
                    InvokeRandomized(CustomDecayTick, UnityEngine.Random.Range(30f, 60f), 60f, 6f);
                }

                timeSinceDragModSet = default(TimeSince);
                timeSinceDragModSet = float.MaxValue;

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
                
                SeekerTarget.SetSeekerTarget(this, VehicleConfig.SeekerStrength);
            }

            bool CustomHasChanged
            {
                get
                {
                    if (!invokingResetSleep && rigidBody.IsSleeping())
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

            public override bool ShouldInheritNetworkGroup()
            {
                return false;
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

            protected void TryStartEngine(BasePlayer player)
            {
                if (!isPowered)
                {
                    return;
                }

                engineController.TryStartEngine(player);
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
                if (!isPowered && HasFlag(Flags.On))
                {
                    engineController.StopEngine();
                }

                UpdateVisibleWheels();
                TryEjection();
            }

            void TryEjection()
            {
                if (IsOn())
                {
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

            void UpdateVisibleWheels()
            {
                if (!hasVisibleWheels)
                {
                    return;
                }

                if (IsMoving() || CustomHasDriver())
                {
                    var dt = Time.deltaTime;
                    var wheelRotation = this.DriveWheelVelocity * 17.29578f * dt;
                    var steer = (this.SteerAngle * CarConfig.VisibleSteerAngleModifier);

                    for (int i = 0; i < visibleWheels.Count; i++)
                    {
                        var vw = visibleWheels[i];
                        var radius = PI_DOUBLED * vw.CarWheel.wheelCollider.radius;

                        var localEulerAngles = vw.BaseEntity.transform.localEulerAngles;
                        var isLeft = vw.IsLeft;
                        var isFront = vw.IsFront;

                        if (hasDriftHandbrake && IsDrifting && !vw.IsFront)
                        {
                            // Lock that wheel baby
                        }
                        else if (isLeft)
                        {
                            localEulerAngles.z -= wheelRotation;
                        }
                        else
                        {
                            localEulerAngles.z += wheelRotation;
                        }

                        if (CarConfig.VisibleWheelsFollowSuspension)
                        {
                            vw.CarWheel.wheelCollider.GetWorldPose(out Vector3 pos, out Quaternion rot);
                            if (pos != Vector3.zero)
                            {
                                var wlp = cachedTransform.InverseTransformPoint(pos);

                                var lp = vw.BaseEntity.transform.localPosition;
                                lp.y = wlp.y;
                                vw.BaseEntity.transform.localPosition = lp;
                            }
                        }

                        vw.BaseEntity.transform.localEulerAngles = localEulerAngles;

                        if (vw.CarWheel.steerWheel)
                        {
                            var forwardAngle = isLeft ? -90f : 90f;
                            forwardAngle += (steer * (isFront ? -1 : 1));

                            UpdateSteerRotation(vw.BaseEntity.transform, forwardAngle, 1);
                        }
                        else
                        {
                            var forwardAngle = isLeft ? -90 : 90;
                            UpdateSteerRotation(vw.BaseEntity.transform, forwardAngle, 1);
                        }
                    }
                }
            }

            protected static void UpdateSteerRotation(Transform t, float steer, int axis)
            {
                Vector3 localEulerAngles = t.localEulerAngles;
                switch (axis)
                {
                    case 0:
                        localEulerAngles.x = steer;
                        break;
                    case 2:
                        localEulerAngles.z = steer;
                        break;
                    default:
                        localEulerAngles.y = steer;
                        break;
                }

                t.localEulerAngles = localEulerAngles;
            }

            public override void DoCollisionDamage(BaseEntity hitEntity, float damage)
            {
                Hurt(damage, DamageType.Collision, this, useProtection: false);
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
                    EngineKw = CarConfig.EngineKW * CarConfig.BoostSettings.EnginePowerModifier;
                    hoverMaxSpeed = CarConfig.HoverMaxSpeed * CarConfig.BoostSettings.EnginePowerModifier;
                    configHoverTurnTorque = CarConfig.HoverTurnTorque * CarConfig.BoostSettings.EnginePowerModifier;
                }
                else
                {
                    EngineKw = CarConfig.EngineKW;
                    hoverMaxSpeed = CarConfig.HoverMaxSpeed;
                    configHoverTurnTorque = CarConfig.HoverTurnTorque;
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
                    EngineKw = CarConfig.EngineKW;
                    hoverMaxSpeed = CarConfig.HoverMaxSpeed;
                    fuelPerSec = VehicleConfig.FuelSettings.FuelPerSec;
                    return;
                }

                var fuelModifier = fuelModifiers[currentFuelModifierSkinId];
                EngineKw = CarConfig.EngineKW * fuelModifier.SpeedMultiplier;
                hoverMaxSpeed = CarConfig.HoverMaxSpeed * fuelModifier.SpeedMultiplier;

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

            #region Physics

            private float prevPitchStabError;

            private float prevRollStabError;

            private float prevRollStabRoll;

            [Range(0f, 1f)]
            private float pitchStabP = 0.004f;

            [Range(0f, 1f)]
            private float pitchStabD = 0.001f;


            [Range(1f, 500f)]
            private float manyWheelStabP = 40f;

            [Range(1f, 100f)]
            private float manyWheelStabD = 10f;

            protected virtual void AwakeBikePhysicsTick(float speed)
            {
                if (rigidBody.isKinematic || rigidBody.IsSleeping())
                {
                    return;
                }

                var isOnGround = IsOnGround;
                if (CarConfig.DrivingStyle.HasFlags(DrivingStyle.Bike))
                {
                    if (CarConfig.StabilizePitch)
                    {
                        PDPitchStab();
                    }

                    if (isOnGround)
                    {
                        if (CarConfig.StabilizeDirection)
                        {
                            PDDirectionStab(isOnGround);
                        }

                        PDRollStab(speed, CarConfig.MaxLeanSpeed);
                    }
                    else if (CarConfig.MaxLeanSpeedAirborne > 0)
                    {
                        PDRollStab(speed, CarConfig.MaxLeanSpeedAirborne);
                    }
                }
                else if (CarConfig.DrivingStyle.HasFlags(DrivingStyle.Hover))
                {
                    if (IsOn())
                    {
                        if (CarConfig.StabilizePitch)
                        {
                            PDPitchStab();
                        }

                        if (CarConfig.StabilizeDirection)
                        {
                            PDDirectionStab(isOnGround);
                        }

                        PDRollStab(speed, CarConfig.MaxLeanSpeed);
                    }
                }
                else
                {
                    StabiliseSnowmobileStyle();
                    if (CarConfig.StabilizeDirection)
                    {
                        PDDirectionStab(isOnGround);
                    }

                    if (CarConfig.StabilizePitch && !isOnGround)
                    {
                        PDPitchStab();
                    }
                }


                //PDPitchStab();
                //PDDirectionStab();

                //float num2 = 0f;
                //if (!carPhysics.IsGrounded())
                //{
                //    if (SprintInput && !DuckInput)
                //    {
                //        num2 = 0f - airControlTorquePower;
                //    }
                //    else if (DuckInput && !SprintInput)
                //    {
                //        num2 = airControlTorquePower;
                //    }
                //}

                //if (num2 != 0f)
                //{
                //    rigidBody.AddRelativeTorque(num2, 0f, 0f, ForceMode.VelocityChange);
                //}
            }

            private void PDPitchStab()
            {
                float num = cachedTransform.localEulerAngles.x;
                if (num > 180f)
                {
                    num -= 360f;
                }

                float num2 = 0f - num;
                float num3 = num2;
                float num4 = (num2 - prevPitchStabError) / UnityEngine.Time.fixedDeltaTime;
                float x = pitchStabP * num3 + pitchStabD * num4;
                rigidBody.AddRelativeTorque(x, 0f, 0f, ForceMode.VelocityChange);
                prevPitchStabError = num2;
            }

            private void PDDirectionStab(bool isOnGround)
            {
                Vector3 angularVelocity = rigidBody.angularVelocity;
                float num = (isOnGround ? (0.05f + Mathf.Abs(SteerAngle) * CarConfig.GroundedDirectionStabilizationModifier) : CarConfig.AirborneDirectionStabilizationModifier);
                angularVelocity.y = Mathf.Clamp(angularVelocity.y, 0f - num, num);
                rigidBody.angularVelocity = angularVelocity;
            }

            private void PDRollStab(float speed, float maxLeanSpeed)
            {
                float num = ((speed >= 0f) ? speed : ((0f - speed) * 0.33f));
                float num2 = 0f - SteerAngle / CarConfig.MaxSteerAngle * Mathf.Clamp01(num / maxLeanSpeed);
                num2 = ((!(num2 < 0f)) ? (num2 * CarConfig.MaxLean) : (num2 * CarConfig.MaxLean));
                float num3 = cachedTransform.localEulerAngles.z;
                if (num3 > 180f)
                {
                    num3 -= 360f;
                }

                float num4 = num2 - num3;
                float num5 = num4;
                float num6 = 0f - AngleDifference(num3, prevRollStabRoll) / UnityEngine.Time.fixedDeltaTime;
                float z = CarConfig.TwoWheelRollStabP * num5 + CarConfig.TwoWheelRollStabD * num6;
                rigidBody.AddRelativeTorque(0f, 0f, z, ForceMode.VelocityChange);
                prevRollStabError = num4;
                prevRollStabRoll = num3;
            }

            private float AngleDifference(float a, float b)
            {
                return (a - b + 540f) % 360f - 180f;
            }

            private void StabiliseSnowmobileStyle()
            {
                if (UnityEngine.Physics.Raycast(cachedTransform.position, Vector3.down, out var hitInfo, 10f, 1218511105, QueryTriggerInteraction.Ignore))
                {
                    Vector3 normal = hitInfo.normal;
                    Vector3 right = cachedTransform.right;
                    right.y = 0f;
                    normal = Vector3.ProjectOnPlane(normal, right);
                    float num = Vector3.Angle(normal, Vector3.up);
                    float angle = rigidBody.angularVelocity.magnitude * 57.29578f * manyWheelStabD / manyWheelStabP;
                    if (num <= 45f)
                    {
                        Vector3 direction = Vector3.Cross(Quaternion.AngleAxis(angle, rigidBody.angularVelocity) * cachedTransform.up, normal) * manyWheelStabP * manyWheelStabP;
                        Vector3 torque = rigidBody.transform.InverseTransformDirection(direction);
                        rigidBody.AddRelativeTorque(torque);
                    }
                }
            }

            #endregion

            #region Combat
            public List<WeaponSystem> WeaponSystems { get; private set; } = new List<WeaponSystem>();
            public List<SpecialGun> SpecialGuns { get; private set; } = new List<SpecialGun>();
            public Transform Transform { get { return cachedTransform; } }
            public Vector3 Forward { get { return cachedTransform.forward; } }
            public string NoAmmoToast { get; private set; }
            public bool UnlimitedAmmo { get { return Instance.configuration.ForceUnlimitedAmmo || VehicleConfig.UnlimitedAmmo; } }
            public bool HasAmmoContainer { get; private set; }
            public ISpecialAmmoContainer AmmoContainer { get; private set; }

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
                if (hasCarPhysics && !invokingResetSleep && CarConfig.CanSleep)
                {
                    carPhysics.vehicleSettings.canSleep = false;
                    invokingResetSleep = true;
                    Invoke(ResetSleep, 1);
                }
            }

            bool invokingResetSleep;
            public void ResetSleep()
            {
                if (hasCarPhysics)
                {
                    carPhysics.vehicleSettings.canSleep = true;
                }

                invokingResetSleep = false;
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

                if (!inWater)
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

                for (int i = 0; i < WeaponSystems.Count; i++)
                {
                    var ws = WeaponSystems[i];

                    WeaponUtilities.UpdateWeapon(this, driver, ws);
                }
            }

            private void UpdateJoints(InputState inputState)
            {
                if (!isPowered || !IsOn())
                {
                    return;
                }

                for (int i = 0; i < inputJoints.Count; i++)
                {
                    var ij = inputJoints[i];

                    ij.PlayerServerInput(inputState);
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
                if (CarConfig.DoHitEffect && info.Initiator is BasePlayer attacker)
                {
                    Effect.reusableInstance.Init(Effect.Type.Generic, attacker.transform.position, Vector3.zero);
                    Effect.reusableInstance.pooledString = Instance.configuration.PlayerHitMarkerFx;
                    EffectNetwork.Send(Effect.reusableInstance, attacker.net.connection);
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
                    foreach (var fx in this.CarConfig.ExplosionFxs)
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
                if (CarConfig.LowHealthEffectSettings == null || !CarConfig.LowHealthEffectSettings.Enabled)
                {
                    return;
                }

                var effects = CarConfig.LowHealthEffectSettings.Effects.OrderBy(e => e.HealthPercent).ToList();
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

            #region ISeekerTargetOwner

            bool playingRadarWarning;
            bool playingRadarLock;

            public bool IsValidHomingTarget()
            {
                return IsEngineOn();
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

            #region Lock

            const string ALARM_SFX = "assets/prefabs/locks/keypad/effects/lock.code.unlock.prefab";

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

            #region Props

            public void InitializeVehicle()
            {
                var config = this.CarConfig;
                EngineKw = CarConfig.EngineKW;
                fuelPerSec = VehicleConfig.FuelSettings.FuelPerSec;
                hoverMaxSpeed = CarConfig.HoverMaxSpeed;
                configHoverTurnTorque = CarConfig.HoverTurnTorque;
                outsideDecayMinutes = Instance.configuration.ForceOutsideDecayMinutes ?? CarConfig.OutsideDecayMinutes;
                insideDecayMinutes = Instance.configuration.ForceInsideDecayMinutes ?? CarConfig.InsideDecayMinutes;
                timeAfterEngineOffToStartDecay = Instance.configuration.ForceTimeAfterEngineOffToStartDecay ?? CarConfig.TimeAfterEngineOffToStartDecay;
                this.enableServerOcclusion = Instance.configuration.ForceServerOcclusion.HasValue ? Instance.configuration.ForceServerOcclusion.Value : config.EnableServerOcclusion;
                NoAmmoToast = config.GeneralToastSettings.Enabled && !string.IsNullOrEmpty(config.GeneralToastSettings.NoAmmoToast) ? config.GeneralToastSettings.NoAmmoToast : string.Empty;

                _maxHealth = config.MaxHealth;

                this.rigidBody.sleepThreshold = config.SleepThreshold;
                this.rigidBody.mass = config.Mass;
                this.rigidBody.linearDamping = config.Drag;
                this.rigidBody.angularDamping = config.AngularDrag;
                this.rigidBody.maxDepenetrationVelocity = config.MaxDepenetrationVelocity;
                Stabilize = CarConfig.Stabilize;

                if (config.ApplyCenterOfMass)
                {
                    if (config.AutomaticCenterOfMass)
                    {
                        this.rigidBody.automaticCenterOfMass = true;
                    }
                    else
                    {
                        this.rigidBody.centerOfMass = config.CenterOfMass;
                    }

                    realLocalCOM = this.rigidBody.centerOfMass;
                    rigidBody.centerOfMass = Vector3.Scale(realLocalCOM, config.GroundedCOMMultiplier);
                }
                else
                {
                    rigidBody.centerOfMass = Vector3.zero;
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
                this.rigidBody.freezeRotation = false;
                this.bounds = new Bounds() { size = config.Bounds.Size, center = config.Bounds.Center };

                this.waterloggedPoint.localPosition += config.WaterSampleModifier;

                if (!string.IsNullOrEmpty(config.CollisionFx))
                {
                    var prefabGuid = GameManifest.pathToGuid[config.CollisionFx];
                    this.collisionEffect = new GameObjectRef() { guid = prefabGuid };
                }

                InitializeWheelColliders();
                PropUtilities.InitializeVehicle(this);
                InitializeVanillaTowing();
                InitializeLowHealthEffects();
                InitializeDrift();

                InitializeChildrenCollections();
                if (CarConfig.DisableGlobalNetwork)
                {
                    this.EnableGlobalBroadcast(false);
                }

                IsActive = true;
                vehicleNetId = this.net.ID.Value;

                this.IsOnGround = true;
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

                    if (child is ISpecialAmmoContainer ammo)
                    {
                        AmmoContainer = ammo;
                        HasAmmoContainer = true;
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

                        radioBroadcaster.SendNetworkUpdate();

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

                engineController = new VehicleEngineController<GroundVehicle>(this, fuelSystem, true, CarConfig.EngineStartupTime, waterloggedPoint, Flags.Reserved4);

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

                foreach (var ht in HurtTriggers)
                {
                    if (ht.Value.Count > 0)
                    {
                        hasHurtTriggers = true;
                        break;
                    }
                }

                if (CarConfig.DriftSettings.Enabled && CarConfig.DriftSettings.HandbrakeForce > 0)
                {
                    hasDriftHandbrake = true;
                }

                var jcs = this.transform.GetComponentsInChildren<JointController>();
                for (int i = 0; i < jcs.Length; i++)
                {
                    var jc = jcs[i];
                    if (!jc.ListenForPlayerInputs)
                    {
                        continue;
                    }

                    inputJoints.Add(jc);
                }

                hasBuoyancy = (CarConfig.BuoyancySettings?.Enabled).GetValueOrDefault();
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

            void InitializeDrift()
            {
                var driftSettings = CarConfig.DriftSettings;
                if (driftSettings == null || !driftSettings.Enabled)
                {
                    return;
                }

                for (int i = 0; i < driftSettings.DriftPropConfigs.Count; i++)
                {
                    var dpc = driftSettings.DriftPropConfigs[i];

                    if (string.IsNullOrEmpty(dpc.PrefabPath))
                    {
                        Instance.Puts($"{ShortPrefabName} - Has Drift Prop without a prefab path, Skipping");
                        continue;
                    }

                    var prop = PropUtilities.CreateCustomEntity(dpc.Location, dpc.Rotation, Vector3.one, dpc.PrefabPath, this);

                    var currentFlags = prop.flags;
                    for (int n = 0; n < PropUtilities.FlagValues.Length; n++)
                    {
                        var flag = PropUtilities.FlagValues[n];
                        if (dpc.Flags.HasFlag(flag))
                        {
                            prop.SetFlagLocal(flag, true);
                        }
                    }

                    if (currentFlags != prop.flags)
                    {
                        prop.SendNetworkUpdate();
                        GlobalNetworkHandler.server?.TrySendNetworkUpdate(prop);
                    }

                    prop.gameObject.AddComponent<DriftWatcher>();
                }
            }

            private void InitializeWheelColliders()
            {
                var wheelList = new List<CarWheel>();
                for (int i = 0; i < CarConfig.WheelColliders.Count; i++)
                {
                    var wcc = CarConfig.WheelColliders[i];
                    var wc = PropUtilities.InitializeWheelCollider(this, wcc, out BaseEntity visWheel);

                    var wheel = new CarWheel()
                    {
                        wheelCollider = wc,
                        steerWheel = wcc.Steering,
                        powerWheel = wcc.Power,
                        brakeWheel = wcc.Brake,
                        tyreFriction = wcc.TyreFriction
                    };

                    if (wcc.PropConfig != null)
                    {
                        hasVisibleWheels = true;

                        var isLeft = visWheel.transform.localPosition.x < 0f;
                        var isFront = visWheel.transform.localPosition.z < 0f;

                        visibleWheels.Add(new CustomCarWheel()
                        {
                            IsLeft = isLeft,
                            IsFront = isFront,
                            BaseEntity = visWheel,
                            CarWheel = wheel,
                        });
                    }

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
                if (CarConfig.CustomDismountPositions != null)
                {
                    bool debugDismounts = ConVar.Debugging.DebugDismounts;

                    Vector3 dismountCheckStart = GetDismountCheckStart(player);
                    List<Vector3> validDismounts = new List<Vector3>();

                    for (int i = 0; i < CarConfig.CustomDismountPositions.Count; i++)
                    {
                        var dmp = CarConfig.CustomDismountPositions[i];

                        var disPos = cachedTransform.TransformPoint(dmp.Location);
                        if (Physics.CheckSphere(disPos, 0.5f, 1537319169))
                        {
                            if (debugDismounts)
                            {
                                player.ChatMessage($"GetDismountPosition debug: {disPos} Failed Sphere check");
                                player.SendConsoleCommand("ddraw.sphere", 5f, "1 0 0 0.5", disPos, 0.25f);
                            }

                            continue;
                        }

                        Vector3 position = disPos + cachedTransform.up * 0.5f;
                        if (!IsVisibleAndCanSee(position))
                        {
                            if (debugDismounts)
                            {
                                player.ChatMessage($"GetDismountPosition debug: {disPos} Cant see");
                                player.SendConsoleCommand("ddraw.sphere", 5f, "1 0 0 0.5", disPos, 0.25f);
                            }

                            continue;
                        }

                        Vector3 vector = disPos + BasePlayer.NoClipOffset();
                        if (CustomAntiHack.TestNoClipping(dismountCheckStart, vector, BasePlayer.NoClipRadius(ConVar.AntiHack.noclip_margin_dismount), ConVar.AntiHack.noclip_backtracking, sphereCast: true, out var _, vehicleLayer: true, this))
                        {
                            if (debugDismounts)
                            {
                                player.ChatMessage($"GetDismountPosition debug: {disPos} Clipping");
                                player.SendConsoleCommand("ddraw.sphere", 5f, "1 0 0 0.5", disPos, 0.25f);
                            }

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
                bool debugDismounts = ConVar.Debugging.DebugDismounts;
                Vector3 dismountCheckStart = GetDismountCheckStart(player);
                if (debugDismounts)
                {
                    player.ChatMessage($"ValidDismountPosition debug: Checking dismount point {disPos} from {dismountCheckStart}.");
                }

                Vector3 start = disPos + DISMOUNT_START_MODIFIER;
                Vector3 end = disPos + DISMOUNT_END_MODIFIER;
                if (!UnityEngine.Physics.CheckCapsule(start, end, 0.5f, 1537319169))
                {
                    Vector3 position = disPos + cachedTransform.up * 0.5f;
                    if (IsVisibleAndCanSee(position))
                    {
                        Vector3 vector = disPos + BasePlayer.NoClipOffset();
                        if (!CustomAntiHack.TestNoClipping(dismountCheckStart, vector, BasePlayer.NoClipRadius(ConVar.AntiHack.noclip_margin_dismount), ConVar.AntiHack.noclip_backtracking, sphereCast: true, out var _, vehicleLayer: false, legacyDismount ? null : this))
                        {
                            if (debugDismounts)
                            {
                                player.ChatMessage($"<color=green>ValidDismountPosition debug: Point is valid {disPos}</color>");
                                player.SendConsoleCommand("ddraw.sphere", 5f, "0 1 0 0.9", disPos, 0.25f);
                            }

                            return true;
                        }
                        else if (debugDismounts)
                        {
                            player.ChatMessage($"<color=red>ValidDismountPosition debug: Cannot dismount at point {disPos} - Is Clipping</color>");
                            player.SendConsoleCommand("ddraw.sphere", 5f, "1 0 0 0.5", disPos, 0.25f);
                        }
                    }
                    else if (debugDismounts)
                    {
                        player.ChatMessage($"<color=red>ValidDismountPosition debug: Cannot dismount at point {disPos} - Is Visible/Can see: {IsVisibleAndCanSee(position)}</color>");
                        player.SendConsoleCommand("ddraw.sphere", 5f, "1 0 0 0.5", disPos, 0.25f);
                    }
                }
                else if (debugDismounts)
                {
                    player.ChatMessage($"<color=red>ValidDismountPosition debug: Cannot dismount at point {disPos} - Object in the way.</color>");
                    player.SendConsoleCommand("ddraw.sphere", 5f, "1 0 0 0.5", disPos, 0.25f);
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

                if (!player.IsNpc && !player.IsBot && CustomHasDriver())
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

                    if (CarConfig.DrivingStyle.HasFlags(DrivingStyle.Bike) && CarConfig.LoseStabilityOnDismount && CarConfig.Stabilize && !CustomHasDriver())
                    {
                        var velocity = rigidBody.linearVelocity.magnitude;
                        if (velocity > CarConfig.LoseStabilityOnDismountVelocity)
                        {
                            Stabilize = false;
                            rigidBody.automaticCenterOfMass = false;
                            rigidBody.centerOfMass = Vector3.zero;
                            WasStable = true;
                        }
                    }

                    UpdateBoost(false);
                    UpdateDrift(false);

                    if (hasRadio && !player.IsNpc && !player.IsBot)
                    {
                        Radio.RemoveRadio(player);

                        if (cockpitRadio.Controller.currentPlayer != null && player.net.ID.Value == cockpitRadio.Controller.currentPlayer.net.ID.Value)
                        {
                            cockpitRadio.Controller.currentPlayer.SetActiveTelephone(null);
                            cockpitRadio.Controller.ServerHangUp();
                        }
                    }

                    if (wasDriver || player.IsNpc || player.IsBot)
                    {
                        TryToggleOff();
                    }

                    BoostUtilities.TryDisableBoost(this);
                });
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

                if (WasStable)
                {
                    Stabilize = true;
                    rigidBody.centerOfMass = CarConfig.CenterOfMass;
                    rigidBody.automaticCenterOfMass = CarConfig.AutomaticCenterOfMass;
                    WasStable = false;
                }
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

                var filterDriver = false;

                if (hasAccessToLock)
                {
                    return true;
                }

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
                    base.AttemptMount(player, false);
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

            #region Control

            public void PilotInput(InputState inputState, BasePlayer player)
            {
                CurrentInputState.steerInput = 0f;
                if (inputState.IsDown(BUTTON.LEFT))
                {
                    CurrentInputState.steerInput = -1f;
                }
                else if (inputState.IsDown(BUTTON.RIGHT))
                {
                    CurrentInputState.steerInput = 1f;
                }

                CurrentInputState.steerMod = inputState.IsDown(BUTTON.SPRINT);
                float accel = 0f;
                if (inputState.IsDown(BUTTON.FORWARD))
                {
                    accel = 1f;
                }
                else if (inputState.IsDown(BUTTON.BACKWARD))
                {
                    accel = -1f;
                }

                CurrentInputState.throttleInput = 0f;
                CurrentInputState.brakeInput = 0f;
                if (GetSpeed() > 3f && accel < -0.1f)
                {
                    CurrentInputState.throttleInput = 0f;
                    CurrentInputState.brakeInput = 0f - accel;
                }
                else
                {
                    CurrentInputState.throttleInput = accel;
                    CurrentInputState.brakeInput = 0f;
                }

                if (engineController.IsOff && isPowered && ((inputState.IsDown(BUTTON.FORWARD) && !inputState.WasDown(BUTTON.FORWARD)) || (inputState.IsDown(BUTTON.BACKWARD) && !inputState.WasDown(BUTTON.BACKWARD))))
                {
                    engineController.TryStartEngine(player);
                }

                UpdateWeapons(player);
                UpdateJoints(inputState);
                BoostUtilities.UpdateBoost(this, player);
                UpdateDrift(player);
                UpdateStunting(player);
            }

            public override void PlayerServerInput(InputState inputState, BasePlayer player)
            {
                if (IsTowing && CarConfig.TowSettings.VanillaTowingSettings.PreventInputWhileTowed)
                {
                    return;
                }

                if (!player.IsNpc && !player.IsBot)
                {
                    var driver = CustomGetDriver();
                    if ((object.ReferenceEquals(driver, null) || driver.net.ID.Value != player.net.ID.Value))
                    {
                        return;
                    }
                }

                PilotInput(inputState, player);
            }

            public Vector3 prevCOMMultiplier;

            public Vector3 realLocalCOM;


            public Vector3 GetCOMMultiplier()
            {
                if (!IsOnGround)
                {
                    return CarConfig.AirborneCOMMultiplier;
                }

                if (IsStunting)
                {
                    return CarConfig.StuntSettings.CenterOfMassModifier;
                }

                return CarConfig.GroundedCOMMultiplier;
            }

            public override void VehicleFixedUpdate()
            {
                if (hasEngine)
                {
                    if (IsStartingUp || IsEngineOn())
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

                var isMounted = IsMounted();
                if (!CarConfig.DisableGlobalNetwork)
                {
                    EnableGlobalBroadcast(isPowered || isMounted);
                }

                var speed = GetSpeed();
                if (hasCarPhysics)
                {
                    carPhysics.CustomFixedUpdate(Time.fixedDeltaTime, speed);
                    SteerAngle = carPhysics.SteerAngle;
                    serverTerrainHandler.FixedUpdate();
                }

                if (!IsOn())
                {
                    this.currentHoverThrottle = 0;
                }
                else if (hasHoverEngines)
                {
                    HandleHoverMovement();
                }
                else if (isAmphibious)
                {
                    HandleAmphibiousMovement();
                }

                var setDiscrete = true;
                if (IsMoving())
                {
                    if ((HasTowVehicle || isPowered) && speed > 5)
                    {
                        UpdateCollisionDetectionMode(CollisionDetectionMode.ContinuousDynamic);
                        setDiscrete = false;
                    }

                    if (CarConfig.ApplyCenterOfMass)
                    {
                        Vector3 cOMMultiplier = GetCOMMultiplier();
                        if (cOMMultiplier != prevCOMMultiplier)
                        {
                            rigidBody.centerOfMass = Vector3.Scale(realLocalCOM, cOMMultiplier);
                            prevCOMMultiplier = cOMMultiplier;
                        }
                    }

                    UpdateGForces(isMounted);
                }

                if (setDiscrete)
                {
                    UpdateCollisionDetectionMode(CollisionDetectionMode.Discrete);
                }

                if (Stabilize)
                {
                    AwakeBikePhysicsTick(speed);
                }

                //if (hasTowVehicle)
                //{
                //    SetFlag(Flags.On, true, false, false);
                //}

                engineController.CheckEngineState();
                engineController.TickFuel(fuelPerSec);

                if (speed != cachedSpeed)
                {
                    UpdateHurtTriggers(speed);
                    cachedSpeed = speed;
                }
            }

            void UpdateCollisionDetectionMode(CollisionDetectionMode mode)
            {
                if (savedCollisionDetectionMode == mode)
                {
                    return;
                }

                savedCollisionDetectionMode = mode;
                this.rigidBody.collisionDetectionMode = savedCollisionDetectionMode;
            }

            void UpdateHurtTriggers(float speed)
            {
                if (hasHurtTriggers)
                {
                    var isEngineOn = IsEngineOn();
                    foreach (var ht in HurtTriggers)
                    {
                        var type = ht.Key;
                        foreach (var htv in ht.Value)
                        {
                            var isActive = (!htv.Key.RequireEngineOn || isEngineOn) && type == HurtTriggerType.Front ? speed > htv.Key.HurtTriggerMinSpeed : speed < -htv.Key.HurtTriggerMinSpeed;
                            htv.Value.gameObject.SetActive(isActive);
                        }
                    }
                }
            }

            float hoverTurnTorque = 0;
            float configHoverTurnTorque;

            private void HandleHoverMovement()
            {
                float throttle = GetThrottleInput();
                var driver = CustomGetDriver();
                if (throttle == 0)
                {
                    this.rigidBody.linearVelocity *= 1 - CarConfig.HoverAirBrakeModifier;
                }

                var deltaTime = Time.fixedDeltaTime;
                var steerInput = GetSteerInput();
                var configHoverTurnModifier = CarConfig.HoverTurnModifier;
                this.currentHoverThrottle = Mathf.Lerp(this.currentHoverThrottle, throttle, 0.5f * deltaTime);
                var thrust = this.currentHoverThrottle * EngineKw;

                this.rigidBody.AddForce(cachedTransform.forward * thrust);

                if (CarConfig.HoverTurnLerp > 0)
                {
                    this.hoverTurnTorque = Mathf.Lerp(this.hoverTurnTorque, configHoverTurnTorque * steerInput, CarConfig.HoverTurnLerp * deltaTime);
                    if (!IsOnGround)
                    {
                        configHoverTurnModifier.y = 0;
                    }

                    this.rigidBody.AddForceAtPosition(cachedTransform.right * hoverTurnTorque, cachedTransform.position + (cachedTransform.forward * configHoverTurnModifier.z) + (cachedTransform.up * configHoverTurnModifier.y) + (cachedTransform.right * configHoverTurnModifier.x));
                }
                else
                {
                    if (steerInput != 0)
                    {
                        this.rigidBody.AddForceAtPosition(cachedTransform.right * (configHoverTurnTorque * steerInput), cachedTransform.position + (cachedTransform.forward * configHoverTurnModifier.z) + (cachedTransform.up * configHoverTurnModifier.y) + (cachedTransform.right * configHoverTurnModifier.x));
                        this.rigidBody.AddForceAtPosition(cachedTransform.right * (thrust * 0.8f * steerInput), cachedTransform.position);
                    }
                    else
                    {
                        this.rigidBody.angularVelocity *= 0.95f;
                    }
                }

                if (this.rigidBody.linearVelocity.magnitude >= hoverMaxSpeed)
                {
                    this.rigidBody.linearVelocity = this.rigidBody.linearVelocity.normalized * hoverMaxSpeed;
                }

                if (this.rigidBody.angularVelocity.magnitude >= 2)
                {
                    this.rigidBody.angularVelocity *= 0.85f;
                }

                if (CarConfig.HoverDownforce != 0 && !IsOnGround)
                {
                    this.rigidBody.AddForce(Vector3.up * CarConfig.HoverDownforce, ForceMode.Force);
                }
                else if (UnityEngine.Physics.Raycast(cachedTransform.position, Vector3.down, out var hitInfo, 10f, 1218511105, QueryTriggerInteraction.Ignore))
                {
                    Vector3 normal = hitInfo.normal;
                    Vector3 right = cachedTransform.right;
                    right.y = 0f;
                    normal = Vector3.ProjectOnPlane(normal, right);
                    float num = Vector3.Angle(normal, Vector3.up);
                    float angle = rigidBody.angularVelocity.magnitude * 57.29578f * CarConfig.AirControlStability / CarConfig.AirControlPower;
                    if (num <= 45f)
                    {
                        Vector3 torque = Vector3.Cross(Quaternion.AngleAxis(angle, rigidBody.angularVelocity) * cachedTransform.up, normal) * CarConfig.AirControlPower * CarConfig.AirControlPower;
                        rigidBody.AddTorque(torque);
                    }
                }
            }

            private void HandleAmphibiousMovement()
            {
                if (!IsOnWater)
                {
                    return;
                }

                float throttle = GetThrottleInput();
                var driver = CustomGetDriver();
                if (throttle == 0)
                {
                    this.rigidBody.linearVelocity *= 1 - CarConfig.HoverAirBrakeModifier;
                }

                var deltaTime = Time.fixedDeltaTime;
                var steerInput = GetSteerInput();
                var configHoverTurnModifier = CarConfig.HoverTurnModifier;
                var right = cachedTransform.right;
                if (CarConfig.FlipHoverSteering)
                {
                    steerInput *= -1;
                }

                this.currentHoverThrottle = Mathf.Lerp(this.currentHoverThrottle, throttle, 0.5f * deltaTime);
                var thrust = this.currentHoverThrottle * CarConfig.AmphibiousThrust;

                this.rigidBody.AddForce(cachedTransform.forward * thrust);

                if (CarConfig.HoverTurnLerp > 0)
                {
                    this.hoverTurnTorque = Mathf.Lerp(this.hoverTurnTorque, configHoverTurnTorque * steerInput, CarConfig.HoverTurnLerp * deltaTime);
                    this.rigidBody.AddForceAtPosition(right * hoverTurnTorque, cachedTransform.position + (cachedTransform.forward * configHoverTurnModifier.z) + (cachedTransform.up * configHoverTurnModifier.y) + (right * configHoverTurnModifier.x));
                }
                else
                {
                    if (steerInput != 0)
                    {
                        this.rigidBody.AddForceAtPosition(right * (configHoverTurnTorque * steerInput), cachedTransform.position + (cachedTransform.forward * configHoverTurnModifier.z) + (cachedTransform.up * configHoverTurnModifier.y) + (right * configHoverTurnModifier.x));
                        this.rigidBody.AddForceAtPosition(right * (thrust * 0.8f * steerInput), cachedTransform.position);
                    }
                    else
                    {
                        this.rigidBody.angularVelocity *= 0.95f;
                    }
                }

                if (this.rigidBody.linearVelocity.magnitude >= hoverMaxSpeed)
                {
                    this.rigidBody.linearVelocity = this.rigidBody.linearVelocity.normalized * hoverMaxSpeed;
                }

                if (this.rigidBody.angularVelocity.magnitude >= 2)
                {
                    this.rigidBody.angularVelocity *= 0.85f;
                }
            }

            void UpdateDrift(BasePlayer player)
            {
                if (!CarConfig.DriftSettings.Enabled)
                {
                    return;
                }

                if (!player.serverInput.IsDown(CarConfig.DriftSettings.Button))
                {
                    UpdateDrift(false);
                    return;
                }

                UpdateDrift(true);
            }

            void UpdateStunting(BasePlayer player)
            {
                if (!CarConfig.StuntSettings.Enabled)
                {
                    return;
                }

                if (player.serverInput.IsDown(CarConfig.StuntSettings.Button))
                {
                    if (!IsStunting)
                    {
                        IsStunting = true;
                        Stabilize = false;
                        UpdateStuntStiffness();
                    }

                    return;
                }

                if (IsStunting)
                {
                    IsStunting = false;
                    Stabilize = true;
                    UpdateStuntStiffness();
                }
            }

            void UpdateStuntStiffness()
            {
                for (int i = 0; i < this.wheels.Length; i++)
                {
                    var wheel = this.wheels[i];
                    float frictionModifier;
                    float damper;

                    if (wheel.wheelCollider.transform.position.z >= 0)
                    {
                        frictionModifier = CarConfig.StuntSettings.FrontStiffnessModifier;
                        damper = CarConfig.StuntSettings.FrontDamperModifier;
                    }
                    else
                    {
                        frictionModifier = CarConfig.StuntSettings.RearStiffnessModifier;
                        damper = CarConfig.StuntSettings.RearDamperModifier;
                    }

                    if (frictionModifier != 1)
                    {
                        var ss = wheel.wheelCollider.suspensionSpring;
                        if (IsStunting)
                        {
                            ss.spring *= frictionModifier;
                        }
                        else
                        {
                            ss.spring /= frictionModifier;
                        }

                        wheel.wheelCollider.suspensionSpring = ss;
                    }

                    if (damper != 1)
                    {
                        var ss = wheel.wheelCollider.suspensionSpring;
                        if (IsStunting)
                        {
                            ss.damper *= damper;
                        }
                        else
                        {
                            ss.damper /= damper;
                        }

                        wheel.wheelCollider.suspensionSpring = ss;
                    }
                }
            }

            void UpdateFriction(bool drift)
            {
                for (int i = 0; i < this.wheels.Length; i++)
                {
                    var wheel = this.wheels[i];
                    DriftWheelFrictionCurveConfig sidewaysModifiers = null;
                    DriftWheelFrictionCurveConfig forwardModifiers = null;

                    if (wheel.wheelCollider.transform.position.z >= 0)
                    {
                        sidewaysModifiers = CarConfig.DriftSettings.FrontSidewaysFrictionModifiers;
                        forwardModifiers = CarConfig.DriftSettings.FrontForwardFrictionModifiers;
                    }
                    else
                    {
                        sidewaysModifiers = CarConfig.DriftSettings.RearSidewaysFrictionModifiers;
                        forwardModifiers = CarConfig.DriftSettings.RearForwardFrictionModifiers;
                    }

                    UpdateWheelFriction(sidewaysModifiers, wheel.wheelCollider, drift, true);
                    UpdateWheelFriction(forwardModifiers, wheel.wheelCollider, drift, false);
                }
            }

            void UpdateWheelFriction(DriftWheelFrictionCurveConfig modifiers, WheelCollider wheelCollider, bool drift, bool sideways)
            {
                if (!modifiers.Enabled)
                {
                    return;
                }

                var friction = sideways ? wheelCollider.sidewaysFriction : wheelCollider.forwardFriction;
                if (drift)
                {
                    friction.extremumSlip *= modifiers.ExtremumSlip;
                    friction.extremumValue *= modifiers.ExtremumValue;
                    friction.asymptoteSlip *= modifiers.AsymptoteSlip;
                    friction.asymptoteValue *= modifiers.AsymptoteSlip;
                    friction.stiffness *= modifiers.Stiffness;
                }
                else
                {
                    friction.extremumSlip /= modifiers.ExtremumSlip;
                    friction.extremumValue /= modifiers.ExtremumValue;
                    friction.asymptoteSlip /= modifiers.AsymptoteSlip;
                    friction.asymptoteValue /= modifiers.AsymptoteSlip;
                    friction.stiffness /= modifiers.Stiffness;
                }

                if (sideways)
                {
                    wheelCollider.sidewaysFriction = friction;
                }
                else
                {
                    wheelCollider.forwardFriction = friction;
                }
            }

            void UpdateDrift(bool drift)
            {
                if (drift == IsDrifting)
                {
                    return;
                }

                UpdateFriction(drift);

                if (hasCarPhysics)
                {
                    if (drift)
                    {
                        if (CarConfig.DriftSettings.MaxDriveSlip > -1)
                        {
                            carPhysics.vehicleSettings.maxDriveSlip = CarConfig.DriftSettings.MaxDriveSlip;
                        }

                        if (CarConfig.DriftSettings.MaxSteerAngle > -1)
                        {
                            carPhysics.vehicleSettings.maxSteerAngle = CarConfig.DriftSettings.MaxSteerAngle;
                        }
                    }
                    else
                    {
                        carPhysics.vehicleSettings.maxDriveSlip = CarConfig.MaxDriveSlip;
                        carPhysics.vehicleSettings.maxSteerAngle = CarConfig.MaxSteerAngle;
                    }
                }

                OnDriftToggle?.Invoke(drift);
                IsDrifting = drift;
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

            #region Collision


            protected new void OnCollisionEnter(Collision collision)
            {
                CustomProcessCollision(collision);
            }

            protected bool CustomDoCollisionDamage(float collisionForce)
            {
                var collisionDamage = Mathf.Lerp(1f, 200f, collisionForce) * CarConfig.CollisionDamageMultiplier;

                if (CarConfig.CollisionsDamagePlayer && collisionDamage > CarConfig.PlayerDamageThreshold)
                {
                    float driverDamage = (collisionDamage > CarConfig.PlayerDeathThreshold) ? 9999f : (collisionDamage - CarConfig.PlayerDamageThreshold) / 2f;
                    float passengerDamage = (collisionDamage > CarConfig.PlayerDeathThreshold) ? 9999f : driverDamage * 0.5f;
                    foreach (var mountPoint in allMountPoints)
                    {
                        if (!mountPoint.mountable.AnyMounted())
                        {
                            continue;
                        }

                        BasePlayer mounted = mountPoint.mountable.GetMounted();
                        var amount = (mountPoint.isDriver ? driverDamage : passengerDamage);
                        mounted.Hurt(amount, DamageType.Collision, this, useProtection: false);
                    }
                }

                if (CarConfig.RagdollOnCollisions && collisionForce >= CarConfig.RagdollThreshold)
                {
                    foreach (var mountPoint in allMountPoints)
                    {
                        if (!mountPoint.mountable.AnyMounted())
                        {
                            continue;
                        }

                        var mounted = mountPoint.mountable.GetMounted();
                        mounted.Ragdoll(this.rigidBody.linearVelocity.normalized * (CarConfig.RagdollForceModifier.z * collisionForce) + Vector3.up * (CarConfig.RagdollForceModifier.y * collisionForce), matchPlayerGravity: false, flailInAir: true, dieOnImpact: CarConfig.RagdollDieOnImpact, this);
                    }
                }

                Hurt(collisionDamage, DamageType.Collision, this, useProtection: false);
                return true;
            }

            protected void CustomProcessCollision(Collision collision)
            {
                if (Time.time < nextCollisionDamageTime)
                {
                    return;
                }

                nextCollisionDamageTime = Time.time + 0.01f;

                Vector3 impulse = collision.impulse;
                impulse.y *= 0.5f;
                float forceMagnitude = impulse.magnitude / Time.fixedDeltaTime;

                var contact = collision.GetContact(0);
                var collisionForce = Mathf.InverseLerp(CarConfig.MinCollisionDamageForce, CarConfig.MaxCollisionDamageForce, forceMagnitude);
                rigidBody.AddForceAtPosition(contact.normal * (0.5f * collisionForce), contact.point, ForceMode.VelocityChange);

                if (collisionForce <= 0f)
                {
                    return;
                }

                CustomDoCollisionDamage(collisionForce);
                TryShowCollisionFX(contact.point, collisionEffect);
            }

            //void OnTriggerEnter(Collider col)
            //{
            //    if (IsDead())
            //    {
            //        return;
            //    }

            //    Debug.LogWarning($"{col.GetComponent<TriggerParentEnclosed>() != null}");
            //    if (col.GetComponent<TriggerParentEnclosed>() != null)
            //    {
            //        CustomSetParent(col.ToBaseEntity());
            //    }
            //}

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

            #region Misc

            public void UpdateLoseControl(bool loseControl)
            {
            }

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

                if (oldWasOn && CurEngineState == VehicleEngineController<GroundVehicle>.EngineState.Off)
                {
                    lastEngineOnTime = Time.time;
                }

                base.OnFlagsChanged(old, next);
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
                if (timeSinceLastPush < CarConfig.PushCooldown)
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

                if (WasStable)
                {
                    Stabilize = true;
                    rigidBody.centerOfMass = CarConfig.CenterOfMass;
                    rigidBody.automaticCenterOfMass = CarConfig.AutomaticCenterOfMass;
                    WasStable = false;
                    return;
                }

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
                OnDriftToggle = null;

                SeekerTarget.SetSeekerTarget(this, SeekerStrength.OFF);

                Interface.CallHook("OnVehicleDestroyed", vehicleNetId);

                foreach (BasePlayer driver in driverCache.Values)
                {
                    if (driver != null && !driver.IsNpc)
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
                visibleWheels.Clear();
                visibleWheels = null;
                hasVisibleWheels = false;
                wheels = null;
                cachedConnections?.Clear();
                cachedConnections = null;
                WeaponSystems?.Clear();
                WeaponSystems = null;
                SpecialGuns?.Clear();
                SpecialGuns = null;
                cockpitRadio = null;
                radioBroadcaster = null;
                AmmoContainer = null;
                engineController = null;
                FuelContainer = null;
                StorageContainers.Clear();
                StorageContainers = null;
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
                if (CarConfig == null)
                {
                    return PREFAB_WORLD;
                }

                switch (CarConfig.BodyType)
                {
                    case BodyType.Prefab:
                    case BodyType.Gib:
                        return CarConfig.BodyGibPrefab;

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
                switch (CarConfig.BodyType)
                {
                    case BodyType.Gib:
                        info.msg.servergib = Facepunch.Pool.Get<ProtoBuf.ServerGib>();
                        info.msg.servergib.gibName = CarConfig.BodyGibName;
                        break;

                    case BodyType.WorldItem:
                    default:
                        info.msg.worldItem = Facepunch.Pool.Get<ProtoBuf.WorldItem>();
                        info.msg.worldItem.item = Pool.Get<ProtoBuf.Item>();
                        info.msg.worldItem.item.itemid = CarConfig.BodyItemId;
                        info.msg.worldItem.item.skinid = CarConfig.BodySkinId;
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
                if (hasCarPhysics)
                {
                    carPhysics.vehicleSettings.disableHandbrakes = true;
                    carPhysics.vehicleSettings.canSleep = false;
                }
            }

            public virtual void OnTowDetach()
            {
                if (hasCarPhysics)
                {
                    carPhysics.vehicleSettings.disableHandbrakes = false;
                    carPhysics.vehicleSettings.canSleep = true;
                }
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

            public class DriverSeatInputs
            {
                public float steerInput;

                public bool steerMod;

                public float brakeInput;

                public float throttleInput;
            }
        }

        public class DriftWatcher : MonoBehaviour
        {
            RustCarController vehicle;
            BaseEntity entity;

            void Awake()
            {
                vehicle = GetComponentInParent<RustCarController>();
                entity = GetComponent<BaseEntity>();
                entity.limitNetworking = true;
                enabled = false;

                vehicle.OnDriftToggle += OnDriftToggle;
            }

            void OnDriftToggle(bool driftOn)
            {
                entity.limitNetworking = !driftOn;
            }
        }

        #endregion

        #region CustomCarPhysics

        public class CustomCarPhysics : CarPhysics<RustCarController>
        {
            bool updateWheels = true;

            public CustomCarPhysics(RustCarController car, Transform transform, Rigidbody rBody, CarSettings vehicleSettings) : base(car, transform, rBody, vehicleSettings)
            {
            }

            public void CustomFixedUpdate(float dt, float speed)
            {
                using (TimeWarning.New("CarPhysics.FixedUpdate"))
                {
                    if (rBody.centerOfMass != prevLocalCOM)
                    {
                        COMChanged();
                    }

                    float num = Mathf.Abs(speed);
                    hasDriver = car.CustomHasDriver();
                    if (!hasDriver && hadDriver)
                    {
                        if (num <= 4f)
                        {
                            slowSpeedExitFlag = true;
                        }
                    }
                    else if (hasDriver && !hadDriver)
                    {
                        slowSpeedExitFlag = false;
                    }

                    if ((hasDriver || !vehicleSettings.canSleep || car.HasTowedVehicle || car.HasTowVehicle) && rBody.IsSleeping())
                    {
                        rBody.WakeUp();
                    }

                    if (!rBody.IsSleeping())
                    {
                        if ((wasSleeping && !rBody.isKinematic) || num > 0.25f || Mathf.Abs(rBody.angularVelocity.magnitude) > 0.25f)
                        {
                            lastMovingTime = UnityEngine.Time.time;
                        }

                        var goToSleep = vehicleSettings.canSleep && !car.HasTowedVehicle && !car.HasTowVehicle && !hasDriver && UnityEngine.Time.time > lastMovingTime + 10f;
                        if (goToSleep && (car.GetParentEntity() as BaseVehicle).IsValid())
                        {
                            goToSleep = false;
                            updateWheels = true;
                        }

                        if (goToSleep)
                        {
                            if (updateWheels)
                            {
                                for (int i = 0; i < wheelData.Length; i++)
                                {
                                    ServerWheelData serverWheelData = wheelData[i];
                                    serverWheelData.wheelCollider.motorTorque = 0f;
                                    serverWheelData.wheelCollider.brakeTorque = 0f;
                                    serverWheelData.wheelCollider.steerAngle = 0f;
                                    if (ConVar.vehicle.disable_wheels_when_sleeping && !car.HasTowedVehicle && !car.HasTowVehicle)
                                    {
                                        serverWheelData.wheelCollider.enabled = false;
                                    }
                                }

                                rBody.Sleep();
                            }
                        }
                        else
                        {
                            speedAngle = Vector3.Angle(rBody.linearVelocity, transform.forward) * Mathf.Sign(Vector3.Dot(rBody.linearVelocity, transform.right));
                            float maxDriveForce = car.GetMaxDriveForce();
                            float maxForwardSpeed = car.GetMaxForwardSpeed();
                            float num2 = (car.IsOn() ? car.GetThrottleInput() : 0f);
                            float steerInput = car.GetSteerInput();
                            float brakeInput = (InSlowSpeedExitMode ? 1f : car.GetBrakeInput());
                            float num3 = 1f;
                            if (num < 3f)
                            {
                                num3 = 2.75f;
                            }
                            else if (num < 9f)
                            {
                                float t = Mathf.InverseLerp(9f, 3f, num);
                                num3 = Mathf.Lerp(1f, 2.75f, t);
                            }

                            maxDriveForce *= num3;
                            ComputeSteerAngle(num2, steerInput, dt, speed);
                            if (timeSinceWaterCheck > 0.25f)
                            {
                                float a = car.WaterFactor();
                                float b = 0f;
                                if (car.FindTrigger<TriggerVehicleDrag>(out var result))
                                {
                                    b = result.vehicleDrag;
                                }

                                float a2 = ((num2 != 0f) ? 0f : 0.25f);
                                float a3 = Mathf.Max(a, b);
                                a3 = Mathf.Max(a3, car.GetModifiedDrag());
                                rBody.drag = Mathf.Max(a2, a3);
                                rBody.angularDrag = a3 * 0.5f;
                                timeSinceWaterCheck = 0f;
                            }

                            int num4 = 0;
                            float num5 = 0f;
                            bool flag2 = !vehicleSettings.disableHandbrakes && !hasDriver && rBody.linearVelocity.magnitude < 2.5f && (float)car.timeSinceLastPush > 2f && car.OnSurface != VehicleTerrainHandler.Surface.Frictionless;
                            bool flag3 = !vehicleSettings.disableHandbrakes && !flag2 && num2 == 0f && num < 0.2f && (float)car.timeSinceLastPush > 2f && car.OnSurface != VehicleTerrainHandler.Surface.Frictionless;

                            for (int j = 0; j < wheelData.Length; j++)
                            {
                                ServerWheelData serverWheelData2 = wheelData[j];
                                if (!serverWheelData2.wheelCollider.enabled)
                                {
                                    serverWheelData2.wheelCollider.enabled = true;
                                    serverWheelData2.wheelCollider.ConfigureVehicleSubsteps(1000f, 1, 1);
                                }

                                serverWheelData2.wheelCollider.motorTorque = 1E-05f;
                                var isDrifting = car.IsDrifting;
                                if (isDrifting && car.CarConfig.DriftSettings.HandbrakeForce > 0 && serverWheelData2.wheelCollider.transform.localPosition.z < 0)
                                {
                                    serverWheelData2.wheelCollider.motorTorque = 0;
                                    serverWheelData2.wheelCollider.brakeTorque = car.CarConfig.DriftSettings.HandbrakeForce;
                                    num3 = 0;
                                }
                                else if (flag2)
                                {
                                    serverWheelData2.wheelCollider.brakeTorque = 10000f;
                                }
                                else if (flag3)
                                {
                                    serverWheelData2.wheelCollider.brakeTorque = 1000f;
                                }
                                else
                                {
                                    serverWheelData2.wheelCollider.brakeTorque = 0f;
                                }

                                if (serverWheelData2.wheel.steerWheel)
                                {
                                    serverWheelData2.wheel.wheelCollider.steerAngle = (serverWheelData2.isFrontWheel ? SteerAngle : (vehicleSettings.rearWheelSteer * (0f - SteerAngle)));
                                }

                                UpdateSuspension(serverWheelData2);
                                if (serverWheelData2.isGrounded)
                                {
                                    num4++;
                                    num5 += wheelData[j].downforce;
                                }
                            }

                            AdjustHitForces(num4, num5 / (float)num4);
                            for (int k = 0; k < wheelData.Length; k++)
                            {
                                ServerWheelData wd = wheelData[k];
                                UpdateLocalFrame(wd, dt);
                                CustomComputeTyreForces(wd, speed, maxDriveForce, maxForwardSpeed, num2, brakeInput, num3);
                                ApplyTyreForces(wd);
                            }

                            ComputeOverallForces();
                        }

                        wasSleeping = false;
                    }
                    else
                    {
                        wasSleeping = true;
                    }

                    hadDriver = hasDriver;
                }
            }

            float cachedRearTraction;
            float cachedRearFriction;
            float cachedFrontTraction;
            float cachedFrontFriction;

            public void CustomComputeTyreForces(ServerWheelData wd, float speed, float maxDriveForce, float maxSpeed, float throttleInput, float brakeInput, float driveForceMultiplier)
            {
                float absSpeed = Mathf.Abs(speed);
                if (vehicleSettings.tankSteering && brakeInput == 0f)
                {
                    throttleInput = ((!wd.isLeftWheel) ? TankThrottleRight : TankThrottleLeft);
                }

                float num = (wd.wheel.powerWheel ? throttleInput : 0f);
                wd.hasThrottleInput = num != 0f;
                float num2 = vehicleSettings.maxDriveSlip;
                if (Mathf.Sign(num) != Mathf.Sign(wd.localVelocity.y))
                {
                    num2 -= wd.localVelocity.y * Mathf.Sign(num);
                }

                float num3 = Mathf.Abs(num);
                float num4 = 0f - vehicleSettings.rollingResistance + num3 * (1f + vehicleSettings.rollingResistance) - brakeInput * (1f - vehicleSettings.rollingResistance);
                if (InSlowSpeedExitMode || num4 < 0f || maxDriveForce == 0f)
                {
                    num4 *= -1f;
                    wd.isBraking = true;
                }
                else
                {
                    num4 *= Mathf.Sign(num);
                    wd.isBraking = false;
                }

                float num6;
                if (wd.isBraking)
                {
                    float num5 = Mathf.Clamp(car.GetMaxForwardSpeed() * vehicleSettings.brakeForceMultiplier, 10f * vehicleSettings.brakeForceMultiplier, 50f * vehicleSettings.brakeForceMultiplier);
                    num5 += rBody.mass * 1.5f;
                    num6 = num4 * num5;
                }
                else
                {
                    num6 = ComputeDriveForce(speed, absSpeed, num4 * maxDriveForce, maxDriveForce, maxSpeed, driveForceMultiplier);
                }

                if (wd.isGrounded)
                {
                    wd.tyreSlip.x = wd.localVelocity.x;
                    wd.tyreSlip.y = wd.localVelocity.y - wd.angularVelocity * wd.wheelCollider.radius;


                    // Custom

                    var isFrontWheel = wd.wheelCollider.transform.localPosition.z >= 0;
                    var traction = -1f;
                    var friction = -1f;
                    if (car.IsDrifting)
                    {
                        if (car.CarConfig.DriftSettings.DriftLerpSpeed > 0)
                        {
                            if (!isFrontWheel)
                            {
                                if (cachedRearTraction == car.CarConfig.DriftSettings.RearTraction)
                                {
                                    traction = cachedRearTraction;
                                }
                                else
                                {
                                    traction = Mathf.Lerp(cachedRearTraction, car.CarConfig.DriftSettings.RearTraction, car.CarConfig.DriftSettings.DriftLerpSpeed);
                                }

                                if (cachedRearFriction == car.CarConfig.DriftSettings.RearTyreFriction)
                                {
                                    friction = cachedRearFriction;
                                }
                                else
                                {
                                    friction = Mathf.Lerp(cachedRearFriction, car.CarConfig.DriftSettings.RearTyreFriction, car.CarConfig.DriftSettings.DriftLerpSpeed);
                                }

                                cachedRearFriction = friction;
                                cachedRearTraction = traction;
                            }
                            else
                            {
                                if (cachedFrontTraction == car.CarConfig.DriftSettings.FrontTraction)
                                {
                                    traction = cachedFrontTraction;
                                }
                                else
                                {
                                    traction = Mathf.Lerp(cachedFrontTraction, car.CarConfig.DriftSettings.FrontTraction, car.CarConfig.DriftSettings.DriftLerpSpeed);
                                }

                                if (cachedFrontFriction == car.CarConfig.DriftSettings.FrontTyreFriction)
                                {
                                    friction = cachedFrontFriction;
                                }
                                else
                                {
                                    friction = Mathf.Lerp(cachedFrontFriction, car.CarConfig.DriftSettings.FrontTyreFriction, car.CarConfig.DriftSettings.DriftLerpSpeed);
                                }
                            }
                        }
                        else
                        {
                            if (!isFrontWheel)
                            {
                                traction = car.CarConfig.DriftSettings.RearTraction;
                                friction = car.CarConfig.DriftSettings.RearTyreFriction;
                            }
                            else
                            {
                                traction = car.CarConfig.DriftSettings.FrontTraction;
                                friction = car.CarConfig.DriftSettings.FrontTyreFriction;
                            }
                        }
                    }

                    if (traction < 0)
                    {
                        var surface = car.OnSurface;
                        traction = surface switch
                        {
                            VehicleTerrainHandler.Surface.Road => 1f,
                            VehicleTerrainHandler.Surface.Ice => 0.25f,
                            VehicleTerrainHandler.Surface.Frictionless => 0f,
                            _ => 0.75f,
                        };
                    }

                    if (friction < 0)
                    {
                        friction = wd.wheel.tyreFriction;
                    }

                    if (!isFrontWheel)
                    {
                        cachedRearFriction = friction;
                        cachedRearTraction = traction;
                    }
                    else
                    {
                        cachedFrontFriction = friction;
                        cachedFrontTraction = traction;
                    }

                    // End Custom
                    float num8 = friction * wd.downforce * traction;
                    float num9 = 0f;
                    if (!wd.isBraking)
                    {
                        num9 = Mathf.Min(Mathf.Abs(num6 * wd.tyreSlip.x) / num8, num2);
                        if (num6 != 0f && num9 < 0.1f)
                        {
                            num9 = 0.1f;
                        }
                    }

                    if (Mathf.Abs(wd.tyreSlip.y) < num9)
                    {
                        wd.tyreSlip.y = num9 * Mathf.Sign(wd.tyreSlip.y);
                    }

                    Vector2 vector = (0f - num8) * wd.tyreSlip.normalized;
                    vector.x = Mathf.Abs(vector.x) * 1.5f;
                    vector.y = Mathf.Abs(vector.y);
                    wd.tyreForce.x = Mathf.Clamp(wd.localRigForce.x, 0f - vector.x, vector.x);
                    if (wd.isBraking)
                    {
                        float num10 = Mathf.Min(vector.y, num6);
                        wd.tyreForce.y = Mathf.Clamp(wd.localRigForce.y, 0f - num10, num10);
                    }
                    else
                    {
                        wd.tyreForce.y = Mathf.Clamp(num6, 0f - vector.y, vector.y);
                    }
                }
                else
                {
                    wd.tyreSlip = Vector2.zero;
                    wd.tyreForce = Vector2.zero;
                }

                if (wd.isGrounded)
                {
                    float num11;
                    if (wd.isBraking)
                    {
                        num11 = 0f;
                    }
                    else
                    {
                        float driveForceToMaxSlip = vehicleSettings.driveForceToMaxSlip;
                        num11 = Mathf.Clamp01((Mathf.Abs(num6) - Mathf.Abs(wd.tyreForce.y)) / driveForceToMaxSlip) * num2 * Mathf.Sign(num6);
                    }

                    wd.angularVelocity = (wd.localVelocity.y + num11) / wd.wheelCollider.radius;
                    return;
                }

                float num12 = 50f;
                float num13 = 10f;
                if (num > 0f)
                {
                    wd.angularVelocity += num12 * num;
                }
                else
                {
                    wd.angularVelocity -= num13;
                }

                wd.angularVelocity -= num12 * brakeInput;
                wd.angularVelocity = Mathf.Clamp(wd.angularVelocity, 0f, maxSpeed / wd.wheelCollider.radius);
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
            public bool? ForceServerOcclusion { get; set; } = null;
            public bool DisableDecay { get; set; } = false;
            public int DefaultRadioFrequency { get; set; } = -1;
        }

        public class CarConfig : CustomVehicleConfig
        {
            public bool VisibleWheelsFollowSuspension { get; set; } = false;

            public bool DisableGlobalNetwork { get; set; } = true;

            public DrivingStyle DrivingStyle { get; set; }

            public float PerformanceFractionTopSpeed { get; set; }

            public float PerformanceFractionAcceleration { get; set; }

            public float EngineKW { get; set; }

            public float RollingResistance { get; set; } = 0.05f;

            public float AntiRoll { get; set; }

            public bool CanSleep { get; set; } = true;

            public bool TankSteering { get; set; }

            public float MaxSteerAngle { get; set; } = 35f;

            public float VisibleSteerAngleModifier { get; set; } = 0.75f;

            public bool SteeringAssist { get; set; } = true;

            public float SteeringAssistRatio { get; set; } = 0.5f;

            public bool SteeringLimit { get; set; }

            public float MinSteerLimitAngle { get; set; } = 6f;

            public float MinSteerLimitSpeed { get; set; } = 30f;

            public float RearWheelSteer { get; set; } = 1f;

            public float SteerMinLerpSpeed { get; set; } = 75f;

            public float SteerMaxLerpSpeed { get; set; } = 150f;

            public float SteerReturnLerpSpeed { get; set; } = 200f;

            public bool RetainLerpSpeed { get; set; }

            public float MaxDriveSlip { get; set; } = 4f;

            public float DriveForceToMaxSlip { get; set; } = 1000f;

            public float ReversePercentSpeed { get; set; } = 0.3f;

            public float BrakeForceMultiplier { get; set; } = 1000f;

            public float HandlingBias { get; set; } = 0.5f;

            public bool Stabilize { get; set; } = false;

            public bool StabilizePitch { get; set; } = true;

            public bool StabilizeDirection { get; set; } = true;

            public float GroundedDirectionStabilizationModifier { get; set; } = 0.15f;

            public float AirborneDirectionStabilizationModifier { get; set; } = 0.05f;

            public float MaxLeanSpeed { get; set; } = 20f;

            public float MaxLeanSpeedAirborne { get; set; } = 20f;

            public float MaxLean { get; set; } = 72f;

            public string CollisionFx { get; set; } = "assets/content/vehicles/modularcar/carcollisioneffect.prefab";

            public bool CollisionsDamagePlayer { get; set; }

            public float PlayerDeathThreshold { get; set; } = 80f;

            public float PlayerDamageThreshold { get; set; } = 40f;

            public float CollisionDamageMultiplier { get; set; } = 1f;
            public float AmphibiousThrust { get; set; }
            public bool FlipHoverSteering { get; set; }

            public float HoverTurnTorque { get; set; } = 200f;

            public ConfigVector HoverTurnModifier { get; set; } = new Vector3(0, 0, 2);

            public float HoverMaxSpeed { get; set; }

            public float HoverTurnLerp { get; set; }

            public float HoverAirBrakeModifier { get; set; } = 0.05f;

            public float HoverDownforce { get; set; }

            public float AirControlStability { get; set; } = 10f;

            public float AirControlPower { get; set; } = 40f;

            public float TwoWheelRollStabP { get; set; } = 0.15f;

            public float TwoWheelRollStabD { get; set; } = 0.01f;

            public bool RagdollOnCollisions { get; set; } = false;

            public float RagdollThreshold { get; set; } = 50f;

            public ConfigVector RagdollForceModifier { get; set; } = new Vector3(0, 10, 10);

            public bool RagdollDieOnImpact { get; set; } = false;

            public float MinCollisionDamageForce { get; set; } = 20000f;

            public float MaxCollisionDamageForce { get; set; } = 2500000f;

            public bool ApplyCenterOfMass { get; set; } = false;

            public bool LoseStabilityOnDismount { get; set; }

            public float LoseStabilityOnDismountVelocity { get; set; } = 5f;

            public ConfigVector GroundedCOMMultiplier { get; set; } = new Vector3(0.25f, 0.3f, 0.25f);

            public ConfigVector AirborneCOMMultiplier { get; set; } = new Vector3(0.25f, 0.75f, 0.25f);

            public DriftModeSettings DriftSettings { get; set; } = new DriftModeSettings();

            public StuntSettings StuntSettings { get; set; } = new StuntSettings();
        }

        public class StuntSettings
        {
            public bool Enabled { get; set; }
            public BUTTON Button { get; set; } = BUTTON.SPRINT;
            public ConfigVector CenterOfMassModifier { get; set; }
            public float FrontStiffnessModifier { get; set; } = 1f;
            public float FrontDamperModifier { get; set; } = 1f;
            public float RearStiffnessModifier { get; set; } = 1f;
            public float RearDamperModifier { get; set; } = 1f;
        }

        public class DriftModeSettings
        {
            public bool Enabled { get; set; }
            public BUTTON Button { get; set; } = BUTTON.SPRINT;
            public float MaxDriveSlip { get; set; } = -1f;
            public float MaxSteerAngle { get; set; } = -1f;
            public float HandbrakeForce { get; set; } = -1f;
            public float FrontTraction { get; set; } = 0.75f;
            public float RearTraction { get; set; } = 0.75f;
            public float FrontTyreFriction { get; set; } = -1;
            public float RearTyreFriction { get; set; } = -1;
            public float DriftLerpSpeed { get; set; } = 0;
            public DriftWheelFrictionCurveConfig FrontSidewaysFrictionModifiers { get; set; } = new DriftWheelFrictionCurveConfig()
            {
                ExtremumSlip = 1f,
                ExtremumValue = 1f,
                AsymptoteValue = 1f,
                AsymptoteSlip = 1f,
                Stiffness = 1f,
            };

            public DriftWheelFrictionCurveConfig FrontForwardFrictionModifiers { get; set; } = new DriftWheelFrictionCurveConfig()
            {
                ExtremumSlip = 1f,
                ExtremumValue = 1f,
                AsymptoteValue = 1f,
                AsymptoteSlip = 1f,
                Stiffness = 1f,
            };

            public DriftWheelFrictionCurveConfig RearSidewaysFrictionModifiers { get; set; } = new DriftWheelFrictionCurveConfig()
            {
                ExtremumSlip = 1f,
                ExtremumValue = 1f,
                AsymptoteValue = 1f,
                AsymptoteSlip = 1f,
                Stiffness = 1f,
            };

            public DriftWheelFrictionCurveConfig RearForwardFrictionModifiers { get; set; } = new DriftWheelFrictionCurveConfig()
            {
                ExtremumSlip = 1f,
                ExtremumValue = 1f,
                AsymptoteValue = 1f,
                AsymptoteSlip = 1f,
                Stiffness = 1f,
            };

            public List<PropConfigSettings> DriftPropConfigs { get; set; } = new List<PropConfigSettings>();
        }

        [Flags]
        public enum DrivingStyle
        {
            None = 0,
            Car = 1 << 0,
            Bike = 1 << 1,
            Hover = 1 << 2,
            // DriftCar = 1 << 3,
            Amphibious = 1 << 4
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
                    TryUpdateCarConfig(hc.Key, hc.Value);
                }

                SaveConfig();
            }
            catch (Exception ex)
            {
                Config.WriteObject(configuration, false, $"{Interface.Oxide.ConfigDirectory}/{Name}.jsonError");
                PrintError($"The configuration file contains an error. {ex}");
            }
        }

        public static void TryUpdateCarConfig(string vehicleName, CarConfig config)
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

                var pc = Config.ReadObject<CarConfig>(filePath);
                TempConfigs.Add(fileName, pc);
            }
        }

        void LoadVehicleConfig(string vehicleName)
        {
            try
            {
                var filePath = $"{DirectoryPath}{vehicleName}.json";
                var config = Config.ReadObject<CarConfig>(filePath);

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

        public static void RegisterVehicleBundle(string vehicleName, CarConfig carConfig)
        {
            Instance.CarConfigs[vehicleName] = carConfig;
            InitializeAmmoTypes(carConfig);

            var path = $"assets/custom/{vehicleName}.prefab";
            if (GameManifest.pathToGuid.ContainsKey(path))
            {
                return;
            }

            var baseCombat = new CustomEntities.CustomPrefabBaseCombat
            {
                HealthStart = carConfig.MaxHealth,
                HealthMax = carConfig.MaxHealth,
                MarkAttackerHostile = true,
                RepairEnabled = false,
                PickupEnabled = false,
                ProtectionProperties = carConfig.ProtectionProperties
                    .OrderBy(k => k.Key)
                    .Select(v => v.Value)
                    .ToArray(),
            };

            var recipe = new CustomEntities.CustomPrefabRecipe(vehicleName, typeof(RustCarController), Layer.Vehicle_World, baseCombat, true);
            var recipes = bundle?.Recipes?.ToList() ?? new List<GenericPrefabRecipe>();
            recipes.Add(recipe);

            bundle = new CustomEntities.CustomPrefabBundle(Instance, recipes.ToArray());
            if (!CustomEntities.CustomPrefabs.RegisterAndLoadBundle(bundle))
            {
                Instance.PrintError("Bundles failed to load");
            }
        }

        public static void RegisterVehicleBundles(Dictionary<string, CarConfig> carConfigs)
        {
            var recipes = bundle?.Recipes?.ToList() ?? new List<GenericPrefabRecipe>();
            foreach (var config in carConfigs)
            {
                var vehicleName = config.Key;
                var vehicleConfig = config.Value;
                var path = $"assets/custom/{vehicleName}.prefab";

                Instance.CarConfigs[config.Key] = config.Value;
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

                var recipe = new CustomEntities.CustomPrefabRecipe(vehicleName, typeof(RustCarController), Layer.Vehicle_World, baseCombat, true);
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
                var vehicleConfig = config.Value as CarConfig;
                TryUpdateCarConfig(config.Key, vehicleConfig);

                Instance.CarConfigs[config.Key] = vehicleConfig;

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

                var recipe = new CustomEntities.CustomPrefabRecipe(vehicleName, typeof(RustCarController), Layer.Vehicle_World, baseCombat, true);
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
