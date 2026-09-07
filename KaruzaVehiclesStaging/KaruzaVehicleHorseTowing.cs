// Requires: KaruzaEntitiesCommon

using System.Linq;
using UnityEngine;
using static BaseEntity;
using static KaruzaVehicles.KaruzaEntitiesCommon;
using static RidableHorse;

namespace KaruzaVehicles
{
    [Info("KaruzaVehicleHorseTowing", "Karuza", "1.01.00")]
    public class KaruzaVehicleHorseTowing : RustPlugin
    {
        public static KaruzaVehicleHorseTowing Instance;

        internal void OnServerInitialized()
        {
            Instance = this;
            UpdateExisting();
        }

        internal void Unload()
        {
            DestroyExisting();
            Instance = null;
        }

        internal void OnEntitySpawned(RidableHorse horse)
        {
            InitializeHorse(horse);
        }

        static void InitializeHorse(RidableHorse horse)
        {
            horse.gameObject.AddComponent<HorseWrapper>();
        }

        void DestroyExisting()
        {
            var wrappers = GameObject.FindObjectsOfType<HorseWrapper>();
            for (int i = 0; i < wrappers.Length; i++)
            {
                var wrapper = wrappers[i];
                GameObject.Destroy(wrapper);
            }
        }

        void UpdateExisting()
        {
            var horses = BaseEntity.serverEntities.OfType<RidableHorse>().ToArray();
            for (int i = 0; i < horses.Length; i++)
            {
                var horse = horses[i];
                var horseWrapper = horse.GetComponent<HorseWrapper>();
                if (!object.ReferenceEquals(horseWrapper, null))
                {
                    continue;
                }

                InitializeHorse(horse);
            }
        }

        public class HorseWrapper : MonoBehaviour
        {
            public RidableHorse Horse;
            public bool HasInputProvider;

            CustomVehicleTriggerTowing cvtt;
            CustomVehicleHorseInputProvider cvhip;

            void Awake()
            {
                Horse = GetComponent<RidableHorse>();
                var tt = Horse.towingTrigger;
                //tt.enabled = false;
                //tt.SetActive(false);
                tt.OnEntityEnterTrigger = null;
                tt.OnEntityLeaveTrigger = null;

                cvtt = tt.gameObject.AddComponent<CustomVehicleTriggerTowing>();
                cvtt.OnEntityEnterTrigger = HandleTowTrigger;
                cvtt.OnEntityLeaveTrigger = HandleTowTriggerLeave;
                cvtt.interestLayers = tt.interestLayers;
                enabled = true;
            }

            public void HandleTowTrigger(BaseNetworkable networkable)
            {
                if (!(networkable is ITowing towing) || towing.IsTowing)
                {
                    Horse.towableEntity = null;
                    Horse.ClientRPC(RpcTarget.NetworkGroup("CLIENT_CanTow"), arg1: false);
                }
                else
                {
                    Horse.towableEntity = towing;
                    Horse.ClientRPC(RpcTarget.NetworkGroup("CLIENT_CanTow"), arg1: true);
                }
            }

            public void HandleTowTriggerLeave(BaseNetworkable networkable)
            {
                Horse.ClientRPC(RpcTarget.NetworkGroup("CLIENT_CanTow"), arg1: false);
                Horse.towableEntity = null;
            }

            void LateUpdate()
            {
                if (!Horse.IsTowing)
                {
                    HasInputProvider = false;
                    return;
                }

                if (Horse.IsMounted())
                {
                    HasInputProvider = false;
                    return;
                }

                if (!HasInputProvider)
                {
                    HasInputProvider = true;
                    var vehicle = BaseNetworkable.serverEntities.Find(Horse.towingEntityId) as IVehicle;
                    if (object.ReferenceEquals(vehicle, null))
                    {
                        // If not Custom Vehicle, don't add input provider. These will be vanilla vehicles
                        // HasInputProvider will prevent additional attempts until disconnected
                        return;
                    }

                    var newGO = new GameObject();
                    cvhip = newGO.AddComponent<CustomVehicleHorseInputProvider>();
                    cvhip.Initialize(vehicle, this);
                }
            }

            void OnDestroy()
            {
                var tt = Horse.towingTrigger;
                tt.OnEntityEnterTrigger = Horse.HandleTowTrigger;
                tt.OnEntityLeaveTrigger = Horse.HandleTowTriggerLeave;
                tt.enabled = true;

                if (Horse.IsTowing)
                {
                    Horse.TowDetach();
                }

                if (Horse.inputProvider == cvhip)
                {
                    Horse.inputProvider = null;
                }

                GameObject.Destroy(cvtt);
                GameObject.Destroy(cvhip);
            }
        }

        public class CustomVehicleHorseInputProvider : MonoBehaviour, IHorseInputProvider
        {
            IVehicle vehicle;
            HorseWrapper horseWrapper;
            BasePlayer driver;
            bool hasDriver;
            GaitType defaultTowingGate;

            void Awake()
            {
                enabled = false;
            }

            public void Initialize(IVehicle vehicle, HorseWrapper horseWrapper)
            {
                this.vehicle = vehicle;
                this.horseWrapper = horseWrapper;

                defaultTowingGate = horseWrapper.Horse.maxTowingGait;
                horseWrapper.Horse.maxTowingGait = vehicle.VehicleConfig.TowSettings.VanillaTowingSettings.MaxTowingGait;
                horseWrapper.Horse.inputProvider = this;
                enabled = true;
            }

            public float GetMoveInput()
            {
                if (!hasDriver)
                {
                    return 0;
                }

                float result = 0f;
                if (driver.serverInput.IsDown(BUTTON.FORWARD))
                {
                    result = 1f;
                }
                else if (driver.serverInput.IsDown(BUTTON.BACKWARD))
                {
                    result = -1f;
                }

                if (!horseWrapper.Horse.IsLeading)
                {
                    return result;
                }

                horseWrapper.Horse.sprintInputJustPressed = driver.serverInput.WasJustPressed(BUTTON.SPRINT);
                bool flag = driver.serverInput.IsDown(BUTTON.SPRINT);
                if (horseWrapper.Horse.sprintInputJustPressed)
                {
                    horseWrapper.Horse.IncrementGait(flag);
                }

                if (driver.serverInput.WasJustReleased(BUTTON.SPRINT) && horseWrapper.Horse.currentGait == GaitType.Gallop)
                {
                    horseWrapper.Horse.RetrogradeGait();
                }

                if (flag)
                {
                    if (horseWrapper.Horse.sprintInputHoldTime == 0f)
                    {
                        horseWrapper.Horse.sprintInputHoldTime = UnityEngine.Time.time;
                    }

                    if (UnityEngine.Time.time - horseWrapper.Horse.sprintInputHoldTime >= horseWrapper.Horse.gaitProgressionInterval && (int)horseWrapper.Horse.currentGait < 3)
                    {
                        horseWrapper.Horse.sprintInputHoldTime = UnityEngine.Time.time;
                        horseWrapper.Horse.IncrementGait(sprintHeld: true);
                    }
                }
                else
                {
                    horseWrapper.Horse.sprintInputHoldTime = 0f;
                }

                return result;
            }

            public float GetSteerInput()
            {
                if (!hasDriver)
                {
                    return 0;
                }

                float result = 0f;
                if (driver.serverInput.IsDown(BUTTON.LEFT))
                {
                    result = -1f;
                }
                else if (driver.serverInput.IsDown(BUTTON.RIGHT))
                {
                    result = 1f;
                }

                return result;
            }

            void Update()
            {
                if (!horseWrapper.Horse.IsTowing)
                {
                    Destroy(this);
                    horseWrapper.Horse.leadingPlayer = null;
                    horseWrapper.HasInputProvider = false;
                    return;
                }

                if (horseWrapper.Horse.IsMounted())
                {
                    Destroy(this);
                    horseWrapper.Horse.leadingPlayer = null;
                    horseWrapper.HasInputProvider = false;
                    return;
                }

                if (vehicle.IsDead())
                {
                    Destroy(this);
                    horseWrapper.Horse.leadingPlayer = null;
                    horseWrapper.HasInputProvider = false;
                    return;
                }

                hasDriver = vehicle.CustomHasDriver();
                if (!hasDriver)
                {
                    if (horseWrapper.Horse.IsLeading)
                    {
                        horseWrapper.Horse.leadingPlayer = null;
                        horseWrapper.Horse.SetFlag(Flags.Reserved16, false, false, false);
                    }

                    return;
                }

                driver = vehicle.CustomGetDriver();
                horseWrapper.Horse.leadingPlayer = driver;
                horseWrapper.Horse.SetFlag(Flags.Reserved16, true, false, false);
            }

            void OnDestroy()
            {
                horseWrapper.Horse.maxTowingGait = defaultTowingGate;
            }
        }

        class CustomVehicleTriggerTowing : TriggerEntityType<BaseVehicle>
        {

        }
    }
}
