using UnityEngine;
using Oxide.Core;

namespace KaruzaVehicles
{
    [Info("KaruzaVehiclePush", "Karuza", "1.0.6")]
    public class KaruzaVehiclePush : RustPlugin
    {
        public static KaruzaVehiclePush Instance;

        private static int GENERAL_COLLIDER = LayerMask.GetMask("Deployable", "Default", "Deployed", "Deployable", "Vehicle Large", "Vehicle World", "Vehicle Detailed", "Resource", "Terrain", "Water", "World", "Tree", "Construction", "Transparent", "Ragdoll", "Clutter");

        internal void OnServerInitialized()
        {
            Instance = this;
            ApplyToPlayers();
        }

        void ApplyToPlayers()
        {
            for (int i = 0; i < BasePlayer.activePlayerList.Count; i++)
            {
                UpdatePlayer(BasePlayer.activePlayerList[i]);
            }
        }

        void UpdatePlayer(BasePlayer player)
        {
            player.gameObject.AddComponent<PushController>();
        }

        internal void OnPlayerConnected(BasePlayer player)
        {
            var pushController = player.GetComponent<PushController>();
            if (pushController != null)
            {
                return;
            }

            player.gameObject.AddComponent<PushController>();
        }

        internal void OnPlayerDisconnected(BasePlayer player)
        {
            var pushController = player.GetComponent<PushController>();
            if (pushController == null)
            {
                return;
            }

            UnityEngine.Object.Destroy(pushController);
        }

        internal void Unload()
        {
            var pushControllers = UnityEngine.Object.FindObjectsOfType<PushController>();
            foreach (var pushController in pushControllers)
            {
                UnityEngine.Object.Destroy(pushController);
            }
            Instance = null;
        }

        public class PushController : MonoBehaviour
        {
            private BasePlayer player;
            private float lastUpdatetime = 0.0f;
            private float heldFor = 0.0f;
            private float timeToHold = 0.3f;
            private float pushedTime = 0f;
            bool pushed;

            private void Awake()
            {
                this.player = this.GetComponent<BasePlayer>();
            }

            void LateUpdate()
            {
                if (!this.player.serverInput.IsDown(BUTTON.USE))
                {
                    pushed = false;
                    pushedTime = 0;
                    return;
                }

                if (pushed)
                {
                    return;
                }

                var currentTime = UnityEngine.Time.realtimeSinceStartup;
                if (pushedTime == 0)
                {
                    pushedTime = currentTime;
                }

                if (pushedTime + timeToHold > currentTime)
                {
                    return;
                }

                pushedTime = 0;
                if (this.player.isMounted || this.player.IsDead())
                {
                    return;
                }

                lastUpdatetime = currentTime;
                if (!Physics.Raycast(player.eyes.position, Quaternion.Euler(player.serverInput.current.aimAngles) * Vector3.forward, out RaycastHit raycastHit, 2f, GENERAL_COLLIDER))
                {
                    return;
                }

                var hitEnt = raycastHit.GetEntity();
                if (object.ReferenceEquals(hitEnt, null) || hitEnt is StorageContainer || hitEnt is BaseMountable || hitEnt is CodeLock || hitEnt is NPCVendingMachine)
                {
                    return;
                }

                var parentEnt = hitEnt.parentEntity.Get(true);
                if (!(parentEnt is BaseVehicle vehicle))
                {
                    return;
                }

                var playerPlarent = player.GetParentEntity();
                if (playerPlarent is BaseVehicle && parentEnt.net.ID.Value == vehicle.net.ID.Value)
                {
                    return;
                }

                if (vehicle.CanPushNow(player) && !(vehicle.rigidBody == null) && (!vehicle.OnlyOwnerAccessible() || !(player != vehicle.creatorEntity)) && Interface.CallHook("OnVehiclePush", this, player) == null)
                {
                    player.metabolism.calories.Subtract(3f);
                    player.metabolism.SendChanges();
                    if (vehicle.rigidBody.IsSleeping())
                    {
                        vehicle.rigidBody.WakeUp();
                    }

                    vehicle.DoPushAction(player);
                    pushed = true;
                }
            }
        }
    }
}
