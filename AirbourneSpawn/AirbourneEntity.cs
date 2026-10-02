using System;
using System.Collections.Generic;
using System.Reflection;
using Facepunch;
using Network;
using ProtoBuf;
using Rust;
using UnityEngine;
using Random = UnityEngine.Random;
using Time = UnityEngine.Time;

namespace AirbourneSpawnHarmony
{
    public abstract class AirbourneEntity : BaseEntity
    {
        protected List<BaseMountable> m_OccupiedMountPoints = new List<BaseMountable>();
        protected List<BasePlayer> m_NetworkToPlayers = new List<BasePlayer>();
        private readonly Hash<ulong, int> m_Cooldowns = new Hash<ulong, int>();

        public static List<Item> ParachuteItems = new List<Item>();

        public virtual Vector3 Position { get; }

        public abstract string LocalizedName { get; }

        protected virtual void Awake() { }

        protected virtual void Start() { }

        protected virtual void Update() { }

        protected virtual void OnDestroy()
        {
            ParachuteItems.Clear();
        }

        public override bool ShouldNetworkTo(BasePlayer player) => m_NetworkToPlayers.Contains(player);

        public abstract bool IsInJumpRange();

        public abstract NetworkableId GetNetworkableId();

        public int GetPlayerCooldown(BasePlayer player)
        {
            m_Cooldowns.TryGetValue(player.GetUserId(), out int cooldown);
            return cooldown;
        }

        public virtual void IssueCooldown(BasePlayer player)
        {
            var cfg = AirbourneSpawnPlugin.Configuration;
            if (cfg.Spawn.ForceRandomRespawns || player.HasPermission(AirbourneSpawnPlugin.IgnoreCooldownPermission))
                return;

            m_Cooldowns[player.GetUserId()] = EpochNow() + cfg.Spawn.Cooldown;
        }

        public abstract void MountPlayer(BasePlayer player);

        public abstract void DismountPlayer(BasePlayer player, bool disconnecting);

        public void DismountAllPlayers(bool unloading)
        {
            for (int i = 0; i < m_OccupiedMountPoints.Count; i++)
            {
                BasePlayer player = m_OccupiedMountPoints[i].GetMounted();
                if (player)
                    DismountPlayer(player, unloading);
            }
        }

        protected Vector3 RandomDropPosition()
        {
            float size = TerrainMeta.Size.x * 0.333f;
            Vector3 v = Vector3Ex.Range(-size, size);
            v.y = Mathf.Clamp(AirbourneSpawnPlugin.Configuration.Flight.Altitude, 150f, 450f);
            return v;
        }

        protected void GiveParachute(BasePlayer player)
        {
            bool originalCanEquip = ConVar.Server.canEquipBackpacksInAir;
            ConVar.Server.canEquipBackpacksInAir = true;

            Item slot = player.inventory.containerWear.GetSlot(7);
            if (slot != null)
            {
                if (slot.MoveToContainer(player.inventory.containerMain))
                    goto GIVE_PARACHUTE;

                slot.RemoveFromContainer();

                for (int i = 0; i < player.inventory.containerMain.capacity; i++)
                {
                    Item occupiedSlot = player.inventory.containerMain.GetSlot(i);
                    if (occupiedSlot != null)
                        continue;

                    slot.SetParent(player.inventory.containerMain);
                    slot.position = i;
                    slot.MarkDirty();
                    player.inventory.containerMain.MarkDirty();
                    goto GIVE_PARACHUTE;
                }

                slot.Remove();
            }

            GIVE_PARACHUTE:

            Item item = ItemManager.CreateByName("parachute");
            if (item != null)
            {
                item.position = 7;
                if (!item.MoveToContainer(player.inventory.containerWear, 7))
                    item.SetParent(player.inventory.containerWear);
                item.conditionNormalized = Mathf.Clamp01(AirbourneSpawnPlugin.Configuration.Parachute.Condition);
                if (AirbourneSpawnPlugin.Configuration.Parachute.DestroyOnLand)
                    ParachuteItems.Add(item);
            }
            ConVar.Server.canEquipBackpacksInAir = originalCanEquip;
        }

        protected void ModifyMetabolism(BasePlayer player, bool isMounted)
        {
            if (isMounted)
            {
                player.metabolism.calories.min = 500;
                player.metabolism.calories.value = 500;
                player.metabolism.hydration.min = 250;
                player.metabolism.hydration.value = 250;
                player.metabolism.temperature.min = 32;
                player.metabolism.temperature.max = 32;
                player.metabolism.temperature.value = 32;
            }
            else
            {
                player.metabolism.calories.min = 0;
                player.metabolism.calories.max = 500;
                player.metabolism.hydration.min = 0;
                player.metabolism.hydration.max = 250;
                player.metabolism.temperature.min = -100;
                player.metabolism.temperature.max = 100;
            }

            player.metabolism.SendChanges();
        }

        protected void SendEntitySnapshot(BasePlayer player)
        {
            NetWrite netWrite = Net.sv.StartWrite();
            netWrite.PacketID(Message.Type.Entities);
            player.net.connection.validate.entityUpdates++;
            SaveInfo saveInfo = new SaveInfo
            {
                forConnection = player.net.connection,
                forDisk = false
            };
            netWrite.UInt32(player.net.connection.validate.entityUpdates);
            ToStreamForNetwork(netWrite, saveInfo);
            netWrite.SendImmediate(new SendInfo(player.net.connection));
        }

        protected void DestroyEntityForDismountedClients()
        {
            for (int i = m_NetworkToPlayers.Count - 1; i >= 0; i--)
            {
                BasePlayer player = m_NetworkToPlayers[i];
                if (!player || player.IsDestroyed)
                {
                    m_NetworkToPlayers.RemoveAt(i);
                    continue;
                }

                bool stillMounted = false;
                for (int m = 0; m < m_OccupiedMountPoints.Count; m++)
                {
                    if (m_OccupiedMountPoints[m].GetMounted() == player)
                    {
                        stillMounted = true;
                        break;
                    }
                }

                if (!stillMounted)
                {
                    NetWrite netWrite = Net.sv.StartWrite();
                    netWrite.PacketID(Message.Type.EntityDestroy);
                    netWrite.EntityID(net.ID);
                    netWrite.UInt8(0);
                    netWrite.Send(new SendInfo(player.net.connection));
                    m_NetworkToPlayers.RemoveAt(i);
                }
            }
        }

        internal static int EpochNow()
        {
            try { return Facepunch.Math.Epoch.Current; }
            catch { return (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(); }
        }

        public static T Spawn<T>(string prefabPath) where T : AirbourneEntity
        {
            BaseEntity baseEntity = GameManager.server.CreateEntity(prefabPath);
            T component = baseEntity.gameObject.AddComponent<T>();

            FieldInfo[] srcFields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < srcFields.Length; i++)
            {
                FieldInfo field = srcFields[i];
                try
                {
                    object value = field.GetValue(baseEntity);
                    field.SetValue(component, value);
                }
                catch { }
            }

            UnityEngine.Object.DestroyImmediate(baseEntity, true);

            component.enableSaving = false;
            component.Spawn();
            return component;
        }
    }

    public abstract class AirbourneEntity<T> : AirbourneEntity where T : BaseEntity
    {
        protected Transform m_Transform;
        protected Vector3 m_StartPosition, m_EndPosition;
        protected float m_TimeToTake;
        protected float _timeTaken;
        protected bool _wasInJumpRange;

        protected abstract Vector3 MountPosition { get; }
        protected abstract Quaternion MountRotation { get; }
        protected abstract Vector3 DismountPosition { get; }

        private readonly Queue<BaseMountable> m_FreeMountPoints = new Queue<BaseMountable>();

        public override Vector3 Position => m_Transform.position;

        protected override void Awake()
        {
            base.Awake();
            globalBroadcast = true;
            m_Transform = transform;
        }

        protected override void Start()
        {
            base.Start();
            GenerateStartEndPositions(RandomDropPosition());
            InvokeRepeating(nameof(UpdateValidJumpZone), 1f, 1f);
        }

        protected override void Update()
        {
            base.Update();

            if (_wasInJumpRange)
            {
                for (int index = m_OccupiedMountPoints.Count - 1; index >= 0; index--)
                {
                    BasePlayer player = m_OccupiedMountPoints[index].GetMounted();
                    if (player && player.serverInput.WasJustReleased(BUTTON.JUMP))
                        DismountPlayer(player, false);
                }
            }

            _timeTaken += Time.deltaTime;
            float delta = Mathf.InverseLerp(0f, m_TimeToTake, _timeTaken);
            m_Transform.position = Vector3.Lerp(m_StartPosition, m_EndPosition, delta);
            m_Transform.hasChanged = true;

            if (delta >= 1f && !IsDestroyed)
            {
                _timeTaken = 0f;
                GenerateStartEndPositions(RandomDropPosition());
                DestroyEntityForDismountedClients();
            }
        }

        private void UpdateValidJumpZone()
        {
            bool isInJumpRange = IsInJumpRange();
            if (!_wasInJumpRange && isInJumpRange)
                UpdatePlayerHints("Jump.Allowed");

            if (_wasInJumpRange && !isInJumpRange)
                UpdatePlayerHints("Jump.Blocked");

            _wasInJumpRange = isInJumpRange;
        }

        private void UpdatePlayerHints(string key)
        {
            for (int i = 0; i < m_OccupiedMountPoints.Count; i++)
            {
                BasePlayer player = m_OccupiedMountPoints[i].GetMounted();
                if (player)
                    player.ShowToast(GameTip.Styles.Blue_Long, AirbourneSpawnPlugin.GetLocalizedString(player, key));
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (!IsDestroyed)
                Kill();
        }

        public override bool IsInJumpRange()
        {
            Vector3 position = m_Transform.position;
            float size = (TerrainMeta.Size.x * 0.5f) + 400f;
            return position.x > -size && position.x < size &&
                   position.z > -size && position.z < size;
        }

        private BaseMountable GetOrCreateMountPoint()
        {
            if (m_FreeMountPoints.Count > 0)
                return m_FreeMountPoints.Dequeue();

            const string MOUNT_PREFAB = "assets/prefabs/vehicle/seats/transporthelipilot.prefab";
            BaseMountable baseMountable = GameManager.server.CreateEntity(MOUNT_PREFAB, m_Transform.position) as BaseMountable;
            baseMountable.isMobile = true;
            baseMountable.enableSaving = false;
            baseMountable.globalBroadcast = true;
            baseMountable.SetParent(this);
            baseMountable.transform.localPosition = MountPosition;
            baseMountable.transform.localRotation = MountRotation;
            baseMountable.Spawn();
            return baseMountable;
        }

        public override void MountPlayer(BasePlayer player)
        {
            m_NetworkToPlayers.Add(player);
            AirbourneSpawnPlugin.MarkRespawning(player);

            player.RespawnAt(Position, Quaternion.identity);
            player.EndSleeping();

            ModifyMetabolism(player, true);
            SendEntitySnapshot(player);

            BaseMountable baseMountable = GetOrCreateMountPoint();

            player.EnsureDismounted();
            baseMountable._mounted = player;

            Transform mountAnchor = baseMountable.mountAnchor.transform;

            player.EnableGlobalBroadcast(true);
            player.limitNetworking = true;

            player.SetMounted(baseMountable);
            player.MovePosition(mountAnchor.position);
            player.transform.rotation = mountAnchor.rotation;
            player.ServerRotation = mountAnchor.rotation;
            player.OverrideViewAngles(mountAnchor.rotation.eulerAngles);
            baseMountable._mounted.eyes.NetworkUpdate(mountAnchor.rotation);
            player.ClientRPC(RpcTarget.Player("ForcePositionTo", player), player.transform.position);
            baseMountable.OnPlayerMounted();

            m_OccupiedMountPoints.Add(baseMountable);

            if (_wasInJumpRange)
                UpdatePlayerHints("Jump.Allowed");
            else
                UpdatePlayerHints("Jump.Blocked");

            AirbourneSpawnPlugin.ScheduleAutoKit(player);
        }

        public override void DismountPlayer(BasePlayer player, bool disconnecting)
        {
            AirbourneSpawnPlugin.UnmarkRespawning(player);

            BaseMountable baseMountable = player.GetMounted();
            if (baseMountable && baseMountable.GetParentEntity() == this)
            {
                if (!disconnecting)
                    player.SendConsoleCommand("gametip.hidegametip");

                ModifyMetabolism(player, false);

                if (player.playerRigidbody != null && !player.playerRigidbody.isKinematic)
                    player.playerRigidbody.linearVelocity = Vector3.zero;
                player.UpdateEstimatedVelocity(player.transform.position, player.transform.position, Time.deltaTime);

                player.PauseFlyHackDetection(15f);
                player.PauseSpeedHackDetection(15f);
                player.PauseVehicleNoClipDetection(15f);
                AirbourneSpawnPlugin.ResetAntiHackCompat(player);
                AirbourneSpawnPlugin.StartFlyhackProtectionForJump(player);

                player.mounted.Set(null);
                player.transform.position = m_Transform.TransformPoint(DismountPosition);

                player.limitNetworking = false;
                player.EnableGlobalBroadcast(false);

                if (!disconnecting)
                    player.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);

                baseMountable._mounted = null;
                baseMountable.OnPlayerDismounted(player);

                m_FreeMountPoints.Enqueue(baseMountable);

                if (!disconnecting)
                {
                    player.SendConsoleCommand("gametip.hidegametip");
                    GiveParachute(player);
                    AirbourneSpawnPlugin.ScheduleDeployParachute(player);
                    IssueCooldown(player);
                }
            }
        }

        public override NetworkableId GetNetworkableId() => net.ID;

        private void GenerateStartEndPositions(Vector3 dropZone)
        {
            float size = TerrainMeta.Size.x;
            float randomA = Random.Range(0f, 1f);
            float randomB = Random.Range(0f, 1f) * 0.333f;
            float randomV = Random.value;

            Vector3 offset = new Vector3(randomA <= 0.5f ? dropZone.x : 0f, 0f, randomA > 0.5f ? dropZone.z : 0f);

            m_StartPosition = new Vector3(
                randomA > 0.5f ? (randomV > 0.5f ? -1f : 1f) : randomB,
                0f,
                randomA <= 0.5f ? (randomV > 0.5f ? -1f : 1f) : randomB);

            m_StartPosition *= (size * 0.5f) + 1000f;
            m_EndPosition = m_StartPosition * -1f;
            m_StartPosition += offset;
            m_EndPosition += offset;
            m_StartPosition.y = m_EndPosition.y = AirbourneSpawnPlugin.Configuration.Flight.Altitude;

            m_TimeToTake = Vector3.Distance(m_StartPosition, m_EndPosition) / AirbourneSpawnPlugin.Configuration.Flight.Speed;
            m_TimeToTake *= Random.Range(0.95f, 1.05f);

            transform.position = m_StartPosition;
            transform.rotation = Quaternion.LookRotation(m_EndPosition - m_StartPosition);
        }
    }

    public class AirbourneCH47 : AirbourneEntity<CH47Helicopter>
    {
        protected override Vector3 MountPosition => new Vector3(0f, 17.5f, -17.5f);
        protected override Quaternion MountRotation => Quaternion.Euler(45, 0f, 0f);
        protected override Vector3 DismountPosition => new Vector3(0f, 3f, -15f);
        public override string LocalizedName => "CH47";
    }

    public class AirbourneCargoPlane : AirbourneEntity<CargoPlane>
    {
        protected override Vector3 MountPosition => new Vector3(0f, 32f, -32f);
        protected override Quaternion MountRotation => Quaternion.Euler(45, 0f, 0f);
        protected override Vector3 DismountPosition => new Vector3(0f, 3f, -15f);
        public override string LocalizedName => "CargoPlane";
    }

    public class AirbourneF15 : AirbourneEntity<F15>
    {
        protected override Vector3 MountPosition => new Vector3(0f, 15f, -15f);
        protected override Quaternion MountRotation => Quaternion.Euler(45, 0f, 0f);
        protected override Vector3 DismountPosition => new Vector3(0f, 0f, -15f);
        public override string LocalizedName => "F15";
    }
}
