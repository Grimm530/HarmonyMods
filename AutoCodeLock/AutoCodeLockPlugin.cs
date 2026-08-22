using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Oxide.Ext.Chaos;
using Oxide.Ext.Chaos.UIFramework;
using UnityEngine;
using UnityEngine.UI;

using Color = Oxide.Ext.Chaos.UIFramework.Color;
using Font = Oxide.Ext.Chaos.UIFramework.Font;
using UIAnchor = Oxide.Ext.Chaos.UIFramework.Anchor;

namespace AutoCodeLockHarmony
{
    public class AutoCodeLockPlugin
    {
        #region Fields

        [Permission] private const string PERMISSION_DEPLOY_DOOR = "autocodelock.deploydoor";
        [Permission] private const string PERMISSION_DEPLOY_BOX = "autocodelock.deploybox";
        [Permission] private const string PERMISSION_DEPLOY_LOCKER = "autocodelock.deploylocker";
        [Permission] private const string PERMISSION_DEPLOY_CUPBOARD = "autocodelock.deploycup";
        [Permission] private const string PERMISSION_AUTO_LOCK = "autocodelock.autolock";
        [Permission] private const string PERMISSION_NO_LOCK_NEEDED = "autocodelock.nolockneed";
        [Permission] private const string PERMISSION_DOOR_CLOSER = "autocodelock.doorcloser";

        private static readonly DateTime Epoch = new DateTime(1970, 1, 1);

        public static AutoCodeLockPlugin Instance { get; private set; }

        internal static StoredData Stored => storedData;

        public string Title => "AutoCodeLock";

        public CommandCallbackHandler CallbackHandler => m_CallbackHandler;

        private static StoredData storedData;
        private ConfigData m_Configuration;

        private ConfigData Configuration => m_Configuration;

        #endregion

        #region Lifecycle

        public void HarmonyInit()
        {
            Instance = this;
            LoadConfig();
            RegisterLangMessages();
            // Prefer existing HarmonyLanguage/AutoCodeLock.json over embedded defaults.
            AutoCodeLockHost.Instance?.ReloadLanguage();
            SetupUIComponents();
            LoadData();
        }

        public void HarmonyServerInitialized()
        {
            FindRegisterEntities();

            if (Configuration.Data.PurgeAfter > 0)
                PurgeOldData();

            foreach (BasePlayer player in BasePlayer.activePlayerList)
                OnPlayerConnected(player);

            PlayerEntities.EnqueueUpdateDoorCloserDelays();
        }

        public void HarmonyUnload(bool shuttingDown)
        {
            if (!shuttingDown)
                SaveData();

            foreach (BasePlayer player in BasePlayer.activePlayerList)
                ChaosUI.Destroy(player, UI_MENU);

            UpdateQueue.OnUnload();

            if (m_CallbackHandler != null)
            {
                m_CallbackHandler.Clear();
                m_CallbackHandler.Unregister();
                m_CallbackHandler = null;
            }

            Instance = null;
        }

        public void RegisterPermissions()
        {
            PermissionsBridge.RegisterPermission(PERMISSION_DEPLOY_DOOR);
            PermissionsBridge.RegisterPermission(PERMISSION_DEPLOY_BOX);
            PermissionsBridge.RegisterPermission(PERMISSION_DEPLOY_LOCKER);
            PermissionsBridge.RegisterPermission(PERMISSION_DEPLOY_CUPBOARD);
            PermissionsBridge.RegisterPermission(PERMISSION_AUTO_LOCK);
            PermissionsBridge.RegisterPermission(PERMISSION_NO_LOCK_NEEDED);
            PermissionsBridge.RegisterPermission(PERMISSION_DOOR_CLOSER);
        }

        #endregion

        #region Public Hooks

        public void OnPlayerConnected(BasePlayer player)
        {
            if (!HasAnyPermission(player))
                return;

            ulong userId = player.GetUserId();
            StoredData.PlayerData playerData = storedData.FindPlayerData(userId);
            if (playerData == null)
                playerData = storedData.SetupPlayer(userId, Configuration);

            playerData.UpdateDelays(Configuration.Delay);
            playerData.SetLastOnline(UnixTimeStampUtc());
        }

        public void OnPlayerDisconnected(BasePlayer player)
        {
            if (player == null)
                return;

            StoredData.PlayerData playerData = storedData.FindPlayerData(player.GetUserId());
            playerData?.SetLastOnline(UnixTimeStampUtc());
        }

        public void OnEntitySpawnedCodeLock(CodeLock codeLock)
        {
            Interface.NextTick(() =>
            {
                if (!codeLock)
                    return;

                PlayerEntities.GetOrCreate(codeLock.OwnerID)?.AddEntity(codeLock);
            });
        }

        public void OnEntitySpawnedDoorCloser(DoorCloser doorCloser)
        {
            Interface.NextTick(() =>
            {
                if (!doorCloser)
                    return;

                PlayerEntities.GetOrCreate(doorCloser.OwnerID)?.AddEntity(doorCloser);
            });
        }

        public void OnEntityKillCodeLock(CodeLock codeLock)
        {
            if (!codeLock)
                return;

            PlayerEntities.Get(codeLock.OwnerID)?.RemoveEntity(codeLock, true);
        }

        public void OnEntityKillDoorCloser(DoorCloser doorCloser)
        {
            if (!doorCloser)
                return;

            PlayerEntities.Get(doorCloser.OwnerID)?.RemoveEntity(doorCloser);
        }

        public void OnItemDeployed(Deployer deployer, BaseEntity entity)
        {
            if (!deployer || !entity || entity.OwnerID == 0UL)
                return;

            if (deployer.GetDeployable().slot != BaseEntity.Slot.Lock || entity.GetSlot(BaseEntity.Slot.Lock) is not CodeLock)
                return;

            BasePlayer owner = deployer.GetOwnerPlayer();
            if (!owner)
                return;

            if (!owner.HasPermission(PERMISSION_AUTO_LOCK))
                return;

            ulong ownerId = owner.GetUserId();
            StoredData.PlayerData playerData = storedData.FindPlayerData(ownerId) ?? storedData.SetupPlayer(ownerId, Configuration);

            if (!playerData.IsSet(Options.AutoLock) || !CanDeployLock(owner, entity))
                return;

            CodeLock codelock = entity.GetSlot(BaseEntity.Slot.Lock) as CodeLock;
            if (!codelock)
                return;

            SetCodeLock(codelock, owner, playerData);
        }

        public void OnEntityBuilt(Planner planner, GameObject obj)
        {
            if (!obj || !planner)
                return;

            BaseEntity entity = obj.ToBaseEntity();
            BasePlayer player = planner.GetOwnerPlayer();
            if (!entity || !player)
                return;

            TryAutoDeployForBuiltEntity(entity, player);
        }

        /// <summary>Primary build/deploy hook — OwnerID and player are already set.</summary>
        public void OnEntityBuilt(BaseEntity entity, BasePlayer player)
        {
            if (!entity || !player)
                return;

            TryAutoDeployForBuiltEntity(entity, player);
        }

        private void TryAutoDeployForBuiltEntity(BaseEntity entity, BasePlayer player)
        {
            if (!entity || !player || entity.IsDestroyed)
                return;

            if (entity is not (Door or BoxStorage or Locker or BuildingPrivlidge))
                return;

            ulong userId = player.GetUserId();
            if (userId == 0UL)
                return;

            StoredData.PlayerData playerData = storedData.FindPlayerData(userId) ?? storedData.SetupPlayer(userId, Configuration);
            if (playerData == null)
                return;

            Interface.NextTick(() =>
            {
                if (!player || !entity || entity.IsDestroyed)
                    return;

                // OwnerID is often assigned after Planner.DoBuild returns; re-check next tick.
                if (entity.OwnerID == 0UL)
                    entity.OwnerID = userId;

                if (entity is Door door)
                {
                    bool canLock = door.canTakeLock || door.HasSlot(BaseEntity.Slot.Lock);
                    if (canLock && player.HasPermission(PERMISSION_DEPLOY_DOOR) && playerData.IsSet(Options.DeployDoor))
                        PlaceCodeLock(player, door, playerData);

                    if ((door.canTakeCloser || door.HasSlot(BaseEntity.Slot.UpperModifier)) &&
                        player.HasPermission(PERMISSION_DOOR_CLOSER) && playerData.IsSet(Options.DeployDoorCloser))
                        PlaceDoorCloser(player, door, playerData);
                    return;
                }

                if (entity is BoxStorage && entity.HasSlot(BaseEntity.Slot.Lock))
                {
                    if (player.HasPermission(PERMISSION_DEPLOY_BOX) && playerData.IsSet(Options.DeployBox))
                        PlaceCodeLock(player, entity, playerData);
                    return;
                }

                if (entity is Locker && entity.HasSlot(BaseEntity.Slot.Lock))
                {
                    if (player.HasPermission(PERMISSION_DEPLOY_LOCKER) && playerData.IsSet(Options.DeployLocker))
                        PlaceCodeLock(player, entity, playerData);
                    return;
                }

                if (entity is BuildingPrivlidge && entity.HasSlot(BaseEntity.Slot.Lock))
                {
                    if (player.HasPermission(PERMISSION_DEPLOY_CUPBOARD) && playerData.IsSet(Options.DeployCupboard))
                        PlaceCodeLock(player, entity, playerData);
                }
            });
        }

        public object CanPickupEntity(BasePlayer player, DoorCloser closer)
        {
            if (player.IsAdmin && Configuration.Other.AdminBypass)
                return null;

            if (Configuration.Other.PreventDoorCloserPickup)
                return false;

            return null;
        }

        public void OnServerSave()
        {
            SaveData();
        }

        public bool ShouldDisableDoorCloser(DoorCloser closer)
        {
            if (!closer)
                return false;

            BaseEntity entity = closer.GetParentEntity();
            if (!entity)
                return false;

            StoredData.PlayerData playerData = storedData.FindPlayerData(entity.OwnerID);
            if (playerData == null)
                return false;

            return playerData.IsSet(Options.DisableCloser);
        }

        public bool TryHandleChat(BasePlayer player, string command, string[] args)
        {
            if (player == null || string.IsNullOrEmpty(command))
                return false;

            if (string.Equals(command, Configuration.Command, StringComparison.OrdinalIgnoreCase))
            {
                CodelockCommand(player, command, args);
                return true;
            }

            if (string.Equals(command, Configuration.SkinCommand, StringComparison.OrdinalIgnoreCase))
            {
                CodelockSkinCommand(player, command, args);
                return true;
            }

            if (string.Equals(command, Configuration.CloserCommand, StringComparison.OrdinalIgnoreCase))
            {
                AutoDoorCommand(player, command, args);
                return true;
            }

            return false;
        }

        #endregion

        #region Functions

        private void FindRegisterEntities()
        {
            foreach (BaseNetworkable baseNetworkable in BaseNetworkable.serverEntities)
            {
                if (baseNetworkable is DoorCloser doorCloser)
                {
                    AdjustDoorCloserPosition(doorCloser.GetParentEntity(), doorCloser);
                    PlayerEntities.GetOrCreate(doorCloser.OwnerID)?.AddEntity(doorCloser);
                }
                else if (baseNetworkable is CodeLock codeLock)
                {
                    PlayerEntities.GetOrCreate(codeLock.OwnerID)?.AddEntity(codeLock);
                }
            }
        }

        private bool CanDeployLock(BasePlayer player, BaseEntity entity)
        {
            if (!(player.IsAdmin && Configuration.Other.AdminBypass))
            {
                ulong userId = player.GetUserId();
                // OwnerID can lag one tick behind build; treat unset owner as the placing player.
                if (entity.OwnerID != 0UL && entity.OwnerID != userId)
                {
                    SendMessage(player, "Notification.NotLocked");
                    return false;
                }

                if (entity.OwnerID == 0UL)
                    entity.OwnerID = userId;

                object externalPlugins = Interface.CallHook("CanAutoLock", player);
                if (externalPlugins != null)
                {
                    SendMessage(player, "Notification.NotLocked.Plugin",
                        externalPlugins is string s ? s : string.Empty);
                    return false;
                }

                if (NoEscape.IsLoaded)
                {
                    if (Configuration.Other.CheckRaidBlock && NoEscape.IsRaidBlocked(player))
                    {
                        SendMessage(player, "Notification.NotLocked.RaidBlock");
                        return false;
                    }

                    if (Configuration.Other.CheckCombatBlock && NoEscape.IsCombatBlocked(player))
                    {
                        SendMessage(player, "Notification.NotLocked.CombatBlock");
                        return false;
                    }
                }
            }

            return true;
        }

        private void PlaceCodeLock(BasePlayer player, BaseEntity entity, StoredData.PlayerData playerData)
        {
            if (!CanDeployLock(player, entity))
                return;

            if (entity.GetSlot(BaseEntity.Slot.Lock) != null)
                return;

            ulong userId = player.GetUserId();
            bool freeLock = player.HasPermission(PERMISSION_NO_LOCK_NEEDED);

            if (!freeLock)
            {
                Item lockItem = FindLockItem(player);
                if (lockItem == null)
                    return;

                lockItem.UseItem(1);
            }

            string lockPrefab = GetPrefabForSkin(playerData.codeSkin);
            CodeLock codelock = GameManager.server.CreateEntity(lockPrefab) as CodeLock;
            if (!codelock)
                return;

            // Required for networked child entities (same as DoorCloser / RaidableBases lock create).
            codelock.gameObject.Identity();
            codelock.OwnerID = userId;

            string anchor = entity.GetSlotAnchorName(BaseEntity.Slot.Lock);
            codelock.SetParent(entity, anchor);
            codelock.transform.localPosition = Vector3.zero;
            codelock.transform.localRotation = Quaternion.identity;
            codelock.OnDeployed(entity, player, null);

            if (player.HasPermission(PERMISSION_AUTO_LOCK) && playerData.IsSet(Options.AutoLock))
                SetCodeLock(codelock, player, playerData);

            codelock.Spawn();
            entity.SetSlot(BaseEntity.Slot.Lock, codelock);
            codelock.SendNetworkUpdate();
            entity.SendNetworkUpdate();
            NotifyDynamicCupShare(codelock);
        }

        private static Item FindLockItem(BasePlayer player)
        {
            if (player?.inventory == null)
                return null;

            Item item = player.inventory.FindItemByItemID(1159991980); // lock.code
            if (item != null)
                return item;

            item = player.inventory.FindItemByItemID(1586884551); // lock.code.a.pilot
            if (item != null)
                return item;

            item = player.inventory.FindItemByItemID(-850982208); // lock.key
            if (item != null)
                return item;

            // Fallbacks if ID lookup is patched/intercepted by other mods.
            item = player.inventory.FindItemByItemID("lock.code");
            if (item != null)
                return item;

            item = player.inventory.FindItemByItemName("lock.code");
            if (item != null)
                return item;

            return player.inventory.FindItemByItemName("Code Lock");
        }

        private string GetPrefabForSkin(CodeLockSkin skin)
        {
            const string CODELOCK_PREFAB = "assets/prefabs/locks/keypad/lock.code.prefab";
            const string PILOT_CODELOCK_PREFAB = "assets/prefabs/locks/keypad/skins/codelock_a_pilot/lock.code.a.pilot.prefab";

            return skin switch
            {
                CodeLockSkin.Pilot => PILOT_CODELOCK_PREFAB,
                _ => CODELOCK_PREFAB,
            };
        }

        private void PlaceDoorCloser(BasePlayer player, BaseEntity entity, StoredData.PlayerData playerData)
        {
            if (entity.GetSlot(BaseEntity.Slot.UpperModifier) != null)
                return;

            const string DOOR_CLOSER_PREFAB = "assets/prefabs/misc/doorcloser/doorcloser.prefab";

            DoorCloser doorCloser = GameManager.server.CreateEntity(DOOR_CLOSER_PREFAB) as DoorCloser;
            if (!doorCloser)
                return;

            doorCloser.gameObject.Identity();
            doorCloser.OwnerID = player.GetUserId();

            if (entity.ShortPrefabName is "floor.ladder.hatch" or "floor.triangle.ladder.hatch")
                doorCloser.delay = playerData.hatchCloseDelay;
            else
                doorCloser.delay = playerData.doorCloseDelay;

            doorCloser.SetParent(entity, entity.GetSlotAnchorName(BaseEntity.Slot.UpperModifier));
            doorCloser.OnDeployed(entity, null, null);

            AdjustDoorCloserPosition(entity, doorCloser);

            doorCloser.Spawn();
            entity.SetSlot(BaseEntity.Slot.UpperModifier, doorCloser);
        }

        private void AdjustDoorCloserPosition(BaseEntity entity, DoorCloser doorCloser)
        {
            if (!entity || !doorCloser)
                return;

            bool isHidden = Configuration.HideDoorClosers;

            if (entity.ShortPrefabName == "floor.ladder.hatch")
                doorCloser.transform.localPosition = isHidden ? new Vector3(0.75f, 0f, 0f) : new Vector3(0.7f, 0f, 0f);
            else if (entity.ShortPrefabName == "floor.triangle.ladder.hatch")
                doorCloser.transform.localPosition = isHidden ? new Vector3(-0.85f, 0f, 0f) : new Vector3(-0.8f, 0f, 0f);
            else if (entity.ShortPrefabName.StartsWith("door.double.hinged"))
            {
                doorCloser.transform.localPosition = isHidden ? new Vector3(0f, 2.4f, 0f) : new Vector3(0f, 2.3f, 0f);
                doorCloser.transform.localRotation = isHidden ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.identity;
            }
            else if (entity.ShortPrefabName == "wall.frame.garagedoor")
                doorCloser.transform.localPosition = isHidden ? new Vector3(-0.15f, 2.9f, 0f) : new Vector3(0f, 2.85f, 0f);
            else
            {
                doorCloser.transform.localPosition = isHidden ? new Vector3(0.01f, 0.21f, 0f) : Vector3.zero;
                doorCloser.transform.localRotation = isHidden ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.identity;
            }
        }

        private void SetCodeLock(CodeLock codelock, BasePlayer owner, StoredData.PlayerData playerData)
        {
            codelock.code = GetOrSetPin(playerData);
            codelock.hasCode = true;
            codelock.whitelistPlayers.Add(owner.GetUserId());

            bool guestCodeEnabled = playerData.IsSet(Options.EnableGuestCode);
            if (guestCodeEnabled)
            {
                codelock.guestCode = GetOrSetGuest(playerData);
                codelock.hasGuestCode = true;
            }

            codelock.SetFlag(BaseEntity.Flags.Locked, true, false);
            Effect.server.Run("assets/prefabs/locks/keypad/effects/lock.code.updated.prefab", codelock.transform.position);

            bool streamerMode = owner.net.connection.info.GetBool("global.streamermode");

            string code = streamerMode ? "****" : codelock.code;
            string guestCode = streamerMode ? "****" : codelock.guestCode;

            if (guestCodeEnabled)
                SendMessage(owner, "Notification.CodelockSecured.Guest", code, guestCode);
            else
                SendMessage(owner, "Notification.CodelockSecured", code);

            NotifyDynamicCupShare(codelock);
        }

        /// <summary>
        /// DynamicCupShare owns team/clan lock sharing. Ask it to whitelist teammates after we set a PIN.
        /// </summary>
        internal static void NotifyDynamicCupShare(CodeLock codeLock)
        {
            if (!codeLock)
                return;
            try
            {
                Type api = AppDomain.CurrentDomain.GetData("DynamicCupShare_ApiType") as Type;
                api?.GetMethod("NotifyCodeLockChanged", BindingFlags.Public | BindingFlags.Static)
                    ?.Invoke(null, new object[] { codeLock });
            }
            catch
            {
            }
        }

        private string GetOrSetPin(StoredData.PlayerData playerData)
        {
            if (string.IsNullOrEmpty(playerData.pinCode))
                playerData.pinCode = $"{UnityEngine.Random.Range(1, 9999)}".PadLeft(4, '0');

            return playerData.pinCode;
        }

        private string GetOrSetGuest(StoredData.PlayerData playerData)
        {
            if (string.IsNullOrEmpty(playerData.guestCode))
                playerData.guestCode = $"{UnityEngine.Random.Range(1, 9999)}".PadLeft(4, '0');

            return playerData.guestCode;
        }

        private static int UnixTimeStampUtc() => (int)DateTime.UtcNow.Subtract(Epoch).TotalSeconds;

        private void SendMessage(BasePlayer player, string key, params object[] args)
        {
            string prefix = Configuration.Other.ChatPrefix
                ? GetString("Prefix", player)
                : string.Empty;

            if (args?.Length > 0)
                player.ChatMessage(prefix + string.Format(GetString(key, player), args));
            else
                player.ChatMessage(prefix + GetString(key, player));
        }

        private string GetString(string key, BasePlayer player)
        {
            return AutoCodeLockHost.Instance.Lang.GetMessage(key, player?.UserIDString);
        }

        private void PurgeOldData()
        {
            List<ulong> purgeList = Facepunch.Pool.Get<List<ulong>>();

            int currentTimeStamp = UnixTimeStampUtc();

            foreach (KeyValuePair<ulong, StoredData.PlayerData> kvp in storedData.playerData)
            {
                if (currentTimeStamp - kvp.Value.lastOnline > Configuration.Data.PurgeAfter * 86400)
                    purgeList.Add(kvp.Key);
            }

            for (int i = 0; i < purgeList.Count; i++)
                storedData.playerData.Remove(purgeList[i]);

            Facepunch.Pool.FreeUnmanaged(ref purgeList);
        }

        #endregion

        #region Commands

        private bool HasAnyPermission(BasePlayer player) =>
            player.HasPermission(PERMISSION_AUTO_LOCK) ||
            player.HasPermission(PERMISSION_DEPLOY_BOX) ||
            player.HasPermission(PERMISSION_DEPLOY_DOOR) ||
            player.HasPermission(PERMISSION_DOOR_CLOSER) ||
            player.HasPermission(PERMISSION_DEPLOY_LOCKER) ||
            player.HasPermission(PERMISSION_DEPLOY_CUPBOARD);

        public void CodelockCommand(BasePlayer player, string command, string[] args)
        {
            if (!HasAnyPermission(player))
            {
                SendMessage(player, "Notification.NoPermission");
                return;
            }

            CreateCodelockUI(player);
        }

        public void CodelockSkinCommand(BasePlayer player, string command, string[] args)
        {
            if (!HasAnyPermission(player) || !player.HasPermission(PERMISSION_NO_LOCK_NEEDED))
            {
                SendMessage(player, "Notification.NoPermission");
                return;
            }

            if (!PlayerDlcApi.IsLoaded)
            {
                SendMessage(player, "Notification.NoPlayerDlcApi");
                return;
            }

            ulong userId = player.GetUserId();
            StoredData.PlayerData playerData = storedData.FindPlayerData(userId);
            if (playerData == null)
            {
                SendMessage(player, "Notification.NoPermission");
                return;
            }

            if (args == null || args.Length == 0)
            {
                SendMessage(player, "Notification.SkinArgs", ToSentence(Enum.GetNames(typeof(CodeLockSkin))));
                return;
            }

            CodeLockSkin skin = ParseType<CodeLockSkin>(args[0]);
            bool canUseSkin = skin switch
            {
                CodeLockSkin.Regular => true,
                CodeLockSkin.Pilot => PlayerDlcApi.IsOwnedOrFreeItem(player, 1586884551),
                _ => false
            };

            if (!canUseSkin)
            {
                SendMessage(player, "Notification.NoSkinPermission");
                return;
            }

            playerData.codeSkin = skin;

            SendMessage(player, "Notification.SkinSet", skin);
        }

        public void AutoDoorCommand(BasePlayer player, string command, string[] args)
        {
            if (!player.HasPermission(PERMISSION_DOOR_CLOSER))
            {
                SendMessage(player, "Notification.NoPermission");
                return;
            }

            if (!player.IsBuildingAuthed())
            {
                SendMessage(player, "Notification.NoBuildPriv");
                return;
            }

            if (Physics.Raycast(player.eyes.HeadRay(), out RaycastHit raycastHit, 3f, 1 << (int)Rust.Layer.Construction, QueryTriggerInteraction.Ignore))
            {
                Door door = raycastHit.GetEntity() as Door;
                if (door)
                {
                    ulong userId = player.GetUserId();
                    StoredData.PlayerData playerData = storedData.FindPlayerData(userId) ?? storedData.SetupPlayer(userId, Configuration);

                    if (door.OwnerID != userId)
                    {
                        SendMessage(player, "Notification.NotDoorOwner");
                        return;
                    }

                    DoorCloser doorCloser = door.GetSlot(BaseEntity.Slot.UpperModifier) as DoorCloser;
                    if (doorCloser)
                    {
                        doorCloser.Kill(BaseNetworkable.DestroyMode.None);
                        SendMessage(player, "Notification.DoorCloserRemoved");
                    }
                    else
                    {
                        PlaceDoorCloser(player, door, playerData);
                        SendMessage(player, "Notification.DoorCloserPlaced");
                    }

                    return;
                }
            }

            SendMessage(player, "Notification.NoDoorFound");
        }

        #endregion

        #region UI

        private const string UI_MENU = "acl.menu";

        private Style m_BackgroundStyle;
        private Style m_PanelStyle;
        private Style m_ButtonStyle;
        private Style m_TitleStyle;
        private Style m_CloseStyle;

        private Color m_ToggleColor;
        private OutlineComponent m_OutlineRed;

        private CommandCallbackHandler m_CallbackHandler;

        private void SetupUIComponents()
        {
            m_CallbackHandler = new CommandCallbackHandler(this);

            m_BackgroundStyle = new Style
            {
                ImageColor = new Color(Configuration.Colors.Background.Hex, Configuration.Colors.Background.Alpha),
                Material = Materials.BackgroundBlur,
                Sprite = Sprites.Background_Rounded,
                ImageType = Image.Type.Tiled
            };

            m_PanelStyle = new Style
            {
                ImageColor = new Color(Configuration.Colors.Panel.Hex, Configuration.Colors.Panel.Alpha),
                Sprite = Sprites.Background_Rounded,
                ImageType = Image.Type.Tiled
            };

            m_ButtonStyle = new Style
            {
                ImageColor = new Color(Configuration.Colors.Button.Hex, Configuration.Colors.Button.Alpha),
                Sprite = Sprites.Background_Rounded,
                ImageType = Image.Type.Tiled,
                Alignment = TextAnchor.MiddleCenter,
                FontSize = 14
            };

            m_TitleStyle = new Style
            {
                FontSize = 18,
                Font = Font.PermanentMarker,
                Alignment = TextAnchor.MiddleLeft,
                WrapMode = VerticalWrapMode.Overflow
            };

            m_CloseStyle = new Style
            {
                FontSize = 18,
                Alignment = TextAnchor.MiddleCenter,
                WrapMode = VerticalWrapMode.Overflow,
            };

            m_ToggleColor = new Color(Configuration.Colors.Highlight.Hex, Configuration.Colors.Highlight.Alpha);
            m_OutlineRed = new OutlineComponent(new Color(Configuration.Colors.Close.Hex, Configuration.Colors.Close.Alpha));
        }

        private void CreateCodelockUI(BasePlayer player)
        {
            ulong userId = player.GetUserId();
            StoredData.PlayerData playerData = storedData.FindPlayerData(userId) ?? storedData.SetupPlayer(userId, Configuration);

            const float BASE_HEIGHT = 45;
            const float ELEMENT_HEIGHT = 30;

            float height = BASE_HEIGHT;

            bool canAutoLock = player.HasPermission(PERMISSION_AUTO_LOCK);
            if (canAutoLock)
            {
                if (playerData.IsSet(Options.AutoLock))
                {
                    height += ELEMENT_HEIGHT * 3;
                    if (playerData.IsSet(Options.EnableGuestCode))
                        height += ELEMENT_HEIGHT;
                }
                else
                {
                    height += ELEMENT_HEIGHT;
                }
            }

            bool canDoorCloser = player.HasPermission(PERMISSION_DOOR_CLOSER);
            if (canDoorCloser)
                height += ELEMENT_HEIGHT * 4;

            bool canDoorDeploy = player.HasPermission(PERMISSION_DEPLOY_DOOR);
            if (canDoorDeploy)
                height += ELEMENT_HEIGHT;

            bool canBoxDeploy = player.HasPermission(PERMISSION_DEPLOY_BOX);
            if (canBoxDeploy)
                height += ELEMENT_HEIGHT;

            bool canCupboardDeploy = player.HasPermission(PERMISSION_DEPLOY_CUPBOARD);
            if (canCupboardDeploy)
                height += ELEMENT_HEIGHT;

            bool canLockerDeploy = player.HasPermission(PERMISSION_DEPLOY_LOCKER);
            if (canLockerDeploy)
                height += ELEMENT_HEIGHT;

            height += 5f;

            BaseContainer root = ImageContainer.Create(UI_MENU, Layer.Overall, UIAnchor.Center, new Offset(-125f, -(height * 0.5f), 125f, (height * 0.5f)))
                .WithStyle(m_BackgroundStyle)
                .WithChildren(parent =>
                {
                    CreateHeaderBar(parent, player);

                    ImageContainer.Create(parent, UIAnchor.FullStretch, new Offset(5f, 5f, -5f, -40f))
                        .WithStyle(m_PanelStyle)
                        .WithChildren(layout =>
                        {
                            int elementIndex = 0;

                            if (canAutoLock)
                            {
                                CreateToggleElement(layout, player, playerData, Options.AutoLock, ++elementIndex);

                                if (playerData.IsSet(Options.AutoLock))
                                {
                                    CreateInputElement(layout, player, playerData, Options.PinCode, ++elementIndex, 4);
                                    CreateToggleElement(layout, player, playerData, Options.EnableGuestCode, ++elementIndex);

                                    if (playerData.IsSet(Options.EnableGuestCode))
                                        CreateInputElement(layout, player, playerData, Options.GuestCode, ++elementIndex, 4);
                                }
                            }

                            if (canDoorCloser)
                            {
                                CreateToggleElement(layout, player, playerData, Options.DeployDoorCloser, ++elementIndex);
                                CreateInputElement(layout, player, playerData, Options.DoorDelay, ++elementIndex);
                                CreateInputElement(layout, player, playerData, Options.HatchDelay, ++elementIndex);
                                CreateToggleElement(layout, player, playerData, Options.DisableCloser, ++elementIndex);
                            }

                            if (canDoorDeploy)
                                CreateToggleElement(layout, player, playerData, Options.DeployDoor, ++elementIndex);

                            if (canBoxDeploy)
                                CreateToggleElement(layout, player, playerData, Options.DeployBox, ++elementIndex);

                            if (canLockerDeploy)
                                CreateToggleElement(layout, player, playerData, Options.DeployLocker, ++elementIndex);

                            if (canCupboardDeploy)
                                CreateToggleElement(layout, player, playerData, Options.DeployCupboard, ++elementIndex);
                        });
                })
                .NeedsCursor()
                .NeedsKeyboard()
                .DestroyExisting();

            ChaosUI.Show(player, root);
        }

        private void CreateHeaderBar(BaseContainer parent, BasePlayer player)
        {
            ImageContainer.Create(parent, UIAnchor.TopStretch, new Offset(5f, -35f, -5f, -5f))
                .WithStyle(m_PanelStyle)
                .WithChildren(titleBar =>
                {
                    TextContainer.Create(titleBar, UIAnchor.CenterLeft, new Offset(5f, -15f, 205f, 15f))
                        .WithStyle(m_TitleStyle)
                        .WithText(GetString("Label.Title", player));

                    ImageContainer.Create(titleBar, UIAnchor.CenterRight, new Offset(-25f, -10f, -5f, 10f))
                        .WithStyle(m_ButtonStyle)
                        .WithOutline(m_OutlineRed)
                        .WithChildren(exit =>
                        {
                            TextContainer.Create(exit, UIAnchor.FullStretch, Offset.zero)
                                .WithText("X")
                                .WithStyle(m_CloseStyle);

                            ButtonContainer.Create(exit, UIAnchor.FullStretch, Offset.zero)
                                .WithColor(Color.Clear)
                                .WithCallback(m_CallbackHandler, arg => ChaosUI.Destroy(player, UI_MENU), $"{player.UserIDString}.exit");
                        });
                });
        }

        private void CreateInputElement(BaseContainer layout, BasePlayer player, StoredData.PlayerData playerData, Options option, int index, int characterLimit = 0)
        {
            const float ELEMENT_HEIGHT = 30;
            float bottom = -(ELEMENT_HEIGHT * index);

            BaseContainer.Create(layout, UIAnchor.TopStretch, new Offset(5f, bottom, -5, bottom + 25f))
                .WithChildren(inputTemplate =>
                {
                    ImageContainer.Create(inputTemplate, UIAnchor.FullStretch, new Offset(0f, 0f, -95f, 0f))
                        .WithStyle(m_PanelStyle);

                    ImageContainer.Create(inputTemplate, UIAnchor.FullStretch, new Offset(137.5f, 0f, -27.5f, 0f))
                        .WithStyle(m_PanelStyle);

                    ImageContainer.Create(inputTemplate, UIAnchor.CenterRight, new Offset(-25f, -12.5f, 0f, 12.5f))
                        .WithStyle(m_PanelStyle);

                    TextContainer.Create(inputTemplate, UIAnchor.FullStretch, new Offset(5f, 0f, 0f, 0f))
                        .WithText(GetString($"Label.{option}", player))
                        .WithAlignment(TextAnchor.MiddleLeft);

                    ImageContainer.Create(inputTemplate, UIAnchor.CenterRight, new Offset(-90f, -10f, -30f, 10f))
                        .WithStyle(m_ButtonStyle)
                        .WithChildren(inputField =>
                        {
                            string currentValue = GetValue(player, playerData, option);

                            InputFieldContainer.Create(inputField, UIAnchor.FullStretch, Offset.zero)
                                .WithText(currentValue)
                                .WithAlignment(TextAnchor.MiddleCenter)
                                .WithCharacterLimit(characterLimit)
                                .WithCallback(m_CallbackHandler, arg =>
                                {
                                    SetValue(playerData, option, arg);
                                    CreateCodelockUI(player);
                                }, $"{player.UserIDString}.{option}");
                        });

                    ImageContainer.Create(inputTemplate, UIAnchor.CenterRight, new Offset(-22.5f, -10f, -2.5f, 10f))
                        .WithStyle(m_ButtonStyle)
                        .WithChildren(applyButton =>
                        {
                            ImageContainer.Create(applyButton, UIAnchor.FullStretch, new Offset(2.5f, 2.5f, -2.5f, -2.5f))
                                .WithSprite(Icon.Download);

                            ButtonContainer.Create(applyButton, UIAnchor.FullStretch, Offset.zero)
                                .WithColor(Color.Clear)
                                .WithCallback(m_CallbackHandler, arg => ApplyChanges(player, playerData, option), $"{player.UserIDString}.{option}.apply");
                        });
                });
        }

        private void ApplyChanges(BasePlayer player, StoredData.PlayerData playerData, Options option)
        {
            PlayerEntities playerEntities = PlayerEntities.GetOrCreate(player.GetUserId());

            switch (option)
            {
                case Options.PinCode:
                    playerEntities.OnCodeChanged(playerData.pinCode, false);
                    SendMessage(player, "Notification.ApplyingCodeChanges");
                    return;

                case Options.GuestCode:
                    playerEntities.OnCodeChanged(playerData.guestCode, true);
                    SendMessage(player, "Notification.ApplyingGuestCodeChanges");
                    return;

                case Options.DoorDelay:
                    playerEntities.OnCloserDelayChanged(playerData.doorCloseDelay, false);
                    SendMessage(player, "Notification.ApplyingDoorCloserChanges");
                    return;

                case Options.HatchDelay:
                    playerEntities.OnCloserDelayChanged(playerData.hatchCloseDelay, true);
                    SendMessage(player, "Notification.ApplyingHatchCloserChanges");
                    return;

                default:
                    return;
            }
        }

        private string GetValue(BasePlayer player, StoredData.PlayerData playerData, Options option)
        {
            bool streamerMode = player.net.connection.info.GetBool("global.streamermode");

            switch (option)
            {
                case Options.PinCode:
                    return streamerMode ? "****" : playerData.pinCode;

                case Options.GuestCode:
                    return streamerMode ? "****" : playerData.guestCode;

                case Options.DoorDelay:
                    return playerData.doorCloseDelay.ToString("N2");

                case Options.HatchDelay:
                    return playerData.hatchCloseDelay.ToString("N2");

                default:
                    return string.Empty;
            }
        }

        private void SetValue(StoredData.PlayerData playerData, Options option, ConsoleSystem.Arg arg)
        {
            switch (option)
            {
                case Options.PinCode:
                    playerData.pinCode = $"{arg.GetInt(1)}".PadLeft(4, '0');
                    return;

                case Options.GuestCode:
                    playerData.guestCode = $"{arg.GetInt(1)}".PadLeft(4, '0');
                    return;

                case Options.DoorDelay:
                    playerData.doorCloseDelay = Configuration.Delay.DoorCloser.Clamp(arg.GetFloat(1, Configuration.Delay.DoorCloser.Minimum));
                    return;

                case Options.HatchDelay:
                    playerData.hatchCloseDelay = Configuration.Delay.LadderHatch.Clamp(arg.GetFloat(1, Configuration.Delay.LadderHatch.Minimum));
                    return;

                default:
                    return;
            }
        }

        private void CreateToggleElement(BaseContainer layout, BasePlayer player, StoredData.PlayerData playerData, Options option, int index)
        {
            const float ELEMENT_HEIGHT = 30;
            float bottom = -(ELEMENT_HEIGHT * index);

            BaseContainer.Create(layout, UIAnchor.TopStretch, new Offset(5f, bottom, -5, bottom + 25f))
                .WithChildren(toggleTemplate =>
                {
                    ImageContainer.Create(toggleTemplate, UIAnchor.FullStretch, new Offset(0f, 0f, -27.5f, 0f))
                        .WithStyle(m_PanelStyle)
                        .WithImageType(Image.Type.Tiled);

                    ImageContainer.Create(toggleTemplate, UIAnchor.FullStretch, new Offset(205f, 0f, 0f, 0f))
                        .WithStyle(m_PanelStyle)
                        .WithImageType(Image.Type.Tiled);

                    TextContainer.Create(toggleTemplate, UIAnchor.FullStretch, new Offset(5f, 0f, 0f, 0f))
                        .WithText(GetString($"Label.{option}", player))
                        .WithAlignment(TextAnchor.MiddleLeft);

                    ImageContainer.Create(toggleTemplate, UIAnchor.CenterRight, new Offset(-22.5f, -10f, -2.5f, 10f))
                        .WithStyle(m_ButtonStyle)
                        .WithChildren(toggle =>
                        {
                            bool isOn = playerData.IsSet(option);

                            if (isOn)
                            {
                                ImageContainer.Create(toggle, UIAnchor.FullStretch, new Offset(2.5f, 2.5f, -2.5f, -2.5f))
                                    .WithColor(m_ToggleColor)
                                    .WithSprite(Sprites.Background_Rounded)
                                    .WithImageType(Image.Type.Tiled);
                            }

                            ButtonContainer.Create(toggle, UIAnchor.FullStretch, Offset.zero)
                                .WithColor(Color.Clear)
                                .WithCallback(m_CallbackHandler, arg =>
                                {
                                    if (isOn)
                                        playerData.UnsetOption(option);
                                    else
                                        playerData.SetOption(option);

                                    SaveData();
                                    CreateCodelockUI(player);
                                }, $"{player.GetUserId()}.{option}");
                        });
                });
        }

        #endregion

        #region Components

        private class UpdateQueue : MonoBehaviour
        {
            private readonly Queue<IEnumerator> m_UpdateQueue = new Queue<IEnumerator>();
            private bool m_QueueRunning;
            private Coroutine m_Current;
            private static UpdateQueue m_Instance;

            private void Awake()
            {
                m_Instance = this;
                DontDestroyOnLoad(gameObject);
            }

            protected void OnDestroy()
            {
                m_UpdateQueue.Clear();

                if (m_Current != null)
                    StopCoroutine(m_Current);

                m_Instance = null;
            }

            public static void Enqueue(IEnumerator enumerator)
            {
                if (!m_Instance)
                    m_Instance = new GameObject("ACL_UpdateQueue").AddComponent<UpdateQueue>();

                m_Instance.m_UpdateQueue.Enqueue(enumerator);

                if (!m_Instance.m_QueueRunning)
                    m_Instance.StartProcessingQueue();
            }

            public static void OnUnload()
            {
                if (m_Instance)
                    Destroy(m_Instance.gameObject);
            }

            private void StartProcessingQueue()
            {
                m_Current = StartCoroutine(RunQueue());
            }

            private IEnumerator RunQueue()
            {
                m_QueueRunning = true;

                while (m_UpdateQueue.Count > 0)
                {
                    IEnumerator enumerator = m_UpdateQueue.Dequeue();
                    yield return StartCoroutine(enumerator);
                }

                m_QueueRunning = false;
            }
        }

        private class PlayerEntities
        {
            private List<DoorCloser> m_DoorClosers;
            private List<CodeLock> m_CodeLocks;

            private static Hash<ulong, PlayerEntities> m_PlayerEntities = new Hash<ulong, PlayerEntities>();

            public static PlayerEntities GetOrCreate(ulong playerId)
            {
                if (!playerId.IsSteamId())
                    return null;

                if (!m_PlayerEntities.TryGetValue(playerId, out PlayerEntities playerEntities))
                    playerEntities = m_PlayerEntities[playerId] = new PlayerEntities();

                return playerEntities;
            }

            public static void EnqueueUpdateDoorCloserDelays()
            {
                foreach (KeyValuePair<ulong, PlayerEntities> kvp in m_PlayerEntities)
                {
                    if (kvp.Value.m_DoorClosers == null || kvp.Value.m_DoorClosers.Count == 0)
                        continue;

                    StoredData.PlayerData playerData = storedData.FindPlayerData(kvp.Key);
                    if (playerData == null)
                        continue;

                    foreach (DoorCloser doorCloser in kvp.Value.m_DoorClosers)
                    {
                        BaseEntity parentEntity = doorCloser.GetParentEntity();
                        if (!parentEntity)
                            continue;

                        bool isHatches = parentEntity.ShortPrefabName is "floor.ladder.hatch" or "floor.triangle.ladder.hatch";

                        UpdateQueue.Enqueue(UpdateDoorCloser(doorCloser, isHatches ? playerData.hatchCloseDelay : playerData.doorCloseDelay, isHatches));
                    }
                }
            }

            public static PlayerEntities Get(ulong playerId)
            {
                if (!playerId.IsSteamId())
                    return null;

                if (m_PlayerEntities.TryGetValue(playerId, out PlayerEntities playerEntities))
                    return playerEntities;

                return null;
            }

            private PlayerEntities() { }

            public void AddEntity(DoorCloser doorCloser)
            {
                if (!doorCloser)
                    return;

                if (m_DoorClosers == null)
                    m_DoorClosers = Facepunch.Pool.Get<List<DoorCloser>>();
                else if (m_DoorClosers.Contains(doorCloser))
                    return;

                m_DoorClosers.Add(doorCloser);
            }

            public void RemoveEntity(DoorCloser doorCloser)
            {
                if (!doorCloser)
                    return;

                if (m_DoorClosers == null)
                    return;

                m_DoorClosers.Remove(doorCloser);

                if (m_DoorClosers.Count == 0)
                    Facepunch.Pool.FreeUnmanaged(ref m_DoorClosers);
            }

            public void AddEntity(CodeLock codeLock)
            {
                if (!codeLock)
                    return;

                if (m_CodeLocks == null)
                    m_CodeLocks = Facepunch.Pool.Get<List<CodeLock>>();
                else if (m_CodeLocks.Contains(codeLock))
                    return;

                m_CodeLocks.Add(codeLock);
            }

            public void RemoveEntity(CodeLock codeLock, bool destroyed)
            {
                if (!codeLock)
                    return;

                if (m_CodeLocks == null)
                    return;

                m_CodeLocks.Remove(codeLock);

                if (m_CodeLocks.Count == 0)
                    Facepunch.Pool.FreeUnmanaged(ref m_CodeLocks);
            }

            public void OnCodeChanged(string code, bool isGuestCode)
            {
                if (m_CodeLocks == null || m_CodeLocks.Count == 0)
                    return;

                foreach (CodeLock codelock in m_CodeLocks)
                    UpdateQueue.Enqueue(UpdateDoorCode(codelock, code, isGuestCode));
            }

            public void OnCloserDelayChanged(float time, bool isHatches)
            {
                if (m_DoorClosers == null || m_DoorClosers.Count == 0)
                    return;

                foreach (DoorCloser doorCloser in m_DoorClosers)
                    UpdateQueue.Enqueue(UpdateDoorCloser(doorCloser, time, isHatches));
            }

            private static IEnumerator UpdateDoorCode(CodeLock codelock, string code, bool isGuestCode)
            {
                if (codelock && !codelock.IsDestroyed)
                {
                    if (isGuestCode)
                    {
                        codelock.guestCode = code;
                        codelock.hasGuestCode = true;
                        if (!codelock.guestPlayers.Contains(codelock.OwnerID))
                            codelock.guestPlayers.Add(codelock.OwnerID);
                    }
                    else
                    {
                        codelock.code = code;
                        codelock.hasCode = true;
                    }

                    codelock.SendNetworkUpdate();
                    NotifyDynamicCupShare(codelock);
                    yield return null;
                }
            }

            private static IEnumerator UpdateDoorCloser(DoorCloser doorCloser, float time, bool isHatches)
            {
                if (doorCloser && !doorCloser.IsDestroyed)
                {
                    BaseEntity parent = doorCloser.GetParentEntity();
                    if (parent && !parent.IsDestroyed)
                    {
                        if (parent.ShortPrefabName is "floor.ladder.hatch" or "floor.triangle.ladder.hatch")
                        {
                            if (isHatches)
                                doorCloser.delay = time;
                        }
                        else
                        {
                            if (!isHatches)
                                doorCloser.delay = time;
                        }

                        doorCloser.SendNetworkUpdate();
                    }

                    yield return null;
                }
            }
        }

        #endregion

        #region Config

        internal class ConfigData
        {
            [JsonProperty("Chat command")]
            public string Command { get; set; }

            [JsonProperty("Chat skin command")]
            public string SkinCommand { get; set; }

            [JsonProperty("Door Closer chat command")]
            public string CloserCommand { get; set; }

            [JsonProperty("Other Options")]
            public OtherOptions Other { get; set; }

            [JsonProperty("Delay Options")]
            public DelayOptions Delay { get; set; }

            [JsonProperty("Default Settings")]
            public DefaultSettings Defaults { get; set; }

            [JsonProperty(PropertyName = "Data Management")]
            public DataManagement Data { get; set; }

            [JsonProperty(PropertyName = "UI Colors")]
            public UIColors Colors { get; set; }

            [JsonProperty(PropertyName = "Hide door closers")]
            public bool HideDoorClosers { get; set; }

            public class DelayOptions
            {
                [JsonProperty("Door closer")]
                public MinMax DoorCloser { get; set; }

                [JsonProperty("Ladder hatch")]
                public MinMax LadderHatch { get; set; }

                public class MinMax
                {
                    public float Minimum { get; set; }
                    public float Maximum { get; set; }

                    public float Clamp(float input) => Mathf.Clamp(input, Minimum, Maximum);
                }
            }

            public class OtherOptions
            {
                [JsonProperty("Use prefix in chat messages")]
                public bool ChatPrefix { get; set; }

                [JsonProperty("Admins bypass restrictions")]
                public bool AdminBypass { get; set; }

                [JsonProperty("Prevent use if player is raid blocked")]
                public bool CheckRaidBlock { get; set; }

                [JsonProperty("Prevent use if player is combat blocked")]
                public bool CheckCombatBlock { get; set; }

                [JsonProperty("Prevent pick up of door closes")]
                public bool PreventDoorCloserPickup { get; set; }
            }

            public class DefaultSettings
            {
                [JsonProperty("Auto-lock on placement")]
                public bool AutoLock { get; set; }

                [JsonProperty("Deploy on doors")]
                public bool DeployDoor { get; set; }

                [JsonProperty("Deploy on boxes")]
                public bool DeployBox { get; set; }

                [JsonProperty("Deploy on lockers")]
                public bool DeployLocker { get; set; }

                [JsonProperty("Deploy on cupboards")]
                public bool DeployCupboard { get; set; }

                [JsonProperty("Deploy door closer")]
                public bool DeployDoorCloser { get; set; }

                [JsonProperty("Door close delay")]
                public float CloseDelay { get; set; }

                [JsonProperty("Ladder hatch close delay")]
                public float HatchDelay { get; set; }

                [JsonProperty("Use guest code")]
                public bool UseGuestCode { get; set; }

                private Options m_Defaults = Options.None;

                [JsonIgnore]
                public Options DefaultOptions
                {
                    get
                    {
                        if (m_Defaults == Options.None)
                        {
                            if (AutoLock)
                                m_Defaults |= Options.AutoLock;

                            if (DeployDoor)
                                m_Defaults |= Options.DeployDoor;

                            if (DeployBox)
                                m_Defaults |= Options.DeployBox;

                            if (DeployCupboard)
                                m_Defaults |= Options.DeployCupboard;

                            if (DeployLocker)
                                m_Defaults |= Options.DeployLocker;

                            if (DeployDoorCloser)
                                m_Defaults |= Options.DeployDoorCloser;

                            if (UseGuestCode)
                                m_Defaults |= Options.EnableGuestCode;
                        }

                        return m_Defaults;
                    }
                }
            }

            public class DataManagement
            {
                [JsonProperty(PropertyName = "Save data in ProtoBuf format")]
                public bool UseProtoStorage { get; set; }

                [JsonProperty(PropertyName = "Purge user data after X days of inactivity (0 is disabled)")]
                public int PurgeAfter { get; set; }
            }

            public class UIColors
            {
                public UIColor Background { get; set; }
                public UIColor Panel { get; set; }
                public UIColor Button { get; set; }
                public UIColor Highlight { get; set; }
                public UIColor Close { get; set; }

                public class UIColor
                {
                    public string Hex { get; set; }
                    public float Alpha { get; set; }
                }
            }
        }

        private void LoadConfig()
        {
            string path = AutoCodeLockHost.Instance.ConfigPath;

            try
            {
                if (File.Exists(path))
                {
                    m_Configuration = JsonConvert.DeserializeObject<ConfigData>(File.ReadAllText(path));
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AutoCodeLock] Config load failed: " + ex.Message);
            }

            if (m_Configuration == null)
            {
                m_Configuration = GenerateDefaultConfiguration();
                SaveConfig();
            }
            else
            {
                ConfigData defaults = GenerateDefaultConfiguration();
                if (string.IsNullOrEmpty(m_Configuration.Command))
                    m_Configuration.Command = defaults.Command;
                if (string.IsNullOrEmpty(m_Configuration.SkinCommand))
                    m_Configuration.SkinCommand = defaults.SkinCommand;
                if (string.IsNullOrEmpty(m_Configuration.CloserCommand))
                    m_Configuration.CloserCommand = defaults.CloserCommand;
                if (m_Configuration.Other == null)
                    m_Configuration.Other = defaults.Other;
                if (m_Configuration.Delay == null)
                    m_Configuration.Delay = defaults.Delay;
                if (m_Configuration.Defaults == null)
                    m_Configuration.Defaults = defaults.Defaults;
                if (m_Configuration.Data == null)
                    m_Configuration.Data = defaults.Data;
                if (m_Configuration.Colors == null)
                    m_Configuration.Colors = defaults.Colors;
            }
        }

        private void SaveConfig()
        {
            if (m_Configuration == null)
                return;

            try
            {
                string path = AutoCodeLockHost.Instance.ConfigPath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(path, JsonConvert.SerializeObject(m_Configuration, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AutoCodeLock] Config save failed: " + ex.Message);
            }
        }

        private static ConfigData GenerateDefaultConfiguration()
        {
            return new ConfigData
            {
                Command = "codelock",
                SkinCommand = "codelock.skin",
                CloserCommand = "closer",
                Other = new ConfigData.OtherOptions
                {
                    ChatPrefix = true,
                    CheckRaidBlock = true,
                    CheckCombatBlock = false,
                    PreventDoorCloserPickup = true
                },
                Delay = new ConfigData.DelayOptions
                {
                    LadderHatch = new ConfigData.DelayOptions.MinMax
                    {
                        Minimum = 3f,
                        Maximum = 15f
                    },
                    DoorCloser = new ConfigData.DelayOptions.MinMax
                    {
                        Minimum = 2f,
                        Maximum = 15f
                    }
                },
                Defaults = new ConfigData.DefaultSettings
                {
                    AutoLock = false,
                    DeployDoor = false,
                    DeployBox = false,
                    DeployLocker = false,
                    DeployCupboard = false,
                    CloseDelay = 3f,
                    HatchDelay = 5f,
                    DeployDoorCloser = false,
                    UseGuestCode = false
                },
                Colors = new ConfigData.UIColors
                {
                    Background = new ConfigData.UIColors.UIColor
                    {
                        Hex = "151515",
                        Alpha = 0.94f
                    },
                    Panel = new ConfigData.UIColors.UIColor
                    {
                        Hex = "FFFFFF",
                        Alpha = 0.165f
                    },
                    Button = new ConfigData.UIColors.UIColor
                    {
                        Hex = "2A2E32",
                        Alpha = 1f
                    },
                    Highlight = new ConfigData.UIColors.UIColor
                    {
                        Hex = "C4FF00",
                        Alpha = 1f
                    },
                    Close = new ConfigData.UIColors.UIColor
                    {
                        Hex = "CE422B",
                        Alpha = 1f
                    }
                },
                Data = new ConfigData.DataManagement
                {
                    UseProtoStorage = false,
                    PurgeAfter = 7
                }
            };
        }

        #endregion

        #region Data

        [Flags]
        internal enum Options
        {
            AutoLock = 1 << 0,
            DeployDoor = 1 << 1,
            DeployBox = 1 << 2,
            DeployLocker = 1 << 3,
            DeployCupboard = 1 << 4,
            DeployDoorCloser = 1 << 5,
            EnableGuestCode = 1 << 6,
            None = 1 << 7,
            PinCode = 1 << 8,
            GuestCode = 1 << 9,
            DoorDelay = 1 << 10,
            HatchDelay = 1 << 11,
            DisableCloser = 1 << 12
        }

        internal enum CodeLockSkin : byte
        {
            Regular = 0,
            Pilot = 1
        }

        internal class StoredData
        {
            public Hash<ulong, PlayerData> playerData = new Hash<ulong, PlayerData>();
            public int timeSaved;

            internal PlayerData SetupPlayer(ulong playerId, ConfigData configuration)
            {
                if (playerId < 76561197960265729UL)
                    return null;

                if (!playerData.TryGetValue(playerId, out PlayerData data))
                    playerData[playerId] = data = new PlayerData(configuration);

                return data;
            }

            internal PlayerData FindPlayerData(ulong playerId) =>
                playerData.TryGetValue(playerId, out PlayerData data) ? data : null;

            public class PlayerData
            {
                public Options options;
                public string pinCode;
                public string guestCode;
                public float doorCloseDelay;
                public float hatchCloseDelay;
                public int lastOnline;
                public CodeLockSkin codeSkin;

                public PlayerData() { }

                public PlayerData(ConfigData configuration)
                {
                    options = configuration.Defaults.DefaultOptions;
                    pinCode = $"{UnityEngine.Random.Range(1, 9999)}".PadLeft(4, '0');
                    guestCode = $"{UnityEngine.Random.Range(1, 9999)}".PadLeft(4, '0');
                    doorCloseDelay = configuration.Delay.DoorCloser.Clamp(configuration.Defaults.CloseDelay);
                    hatchCloseDelay = configuration.Delay.LadderHatch.Clamp(configuration.Defaults.HatchDelay);
                }

                internal bool IsSet(Options option) => (options & option) == option;

                internal void SetOption(Options option) => options |= option;

                internal void UnsetOption(Options option) => options &= ~option;

                internal void UpdateDelays(ConfigData.DelayOptions delayOptions)
                {
                    doorCloseDelay = delayOptions.DoorCloser.Clamp(doorCloseDelay);
                    hatchCloseDelay = delayOptions.LadderHatch.Clamp(hatchCloseDelay);
                }

                internal void SetLastOnline(int i) => lastOnline = UnixTimeStampUtc();

                internal void SetSkin(CodeLockSkin skin) => codeSkin = skin;
            }
        }

        private void SaveData()
        {
            if (storedData == null)
                return;

            storedData.timeSaved = UnixTimeStampUtc();

            try
            {
                string path = AutoCodeLockHost.Instance.DataPath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                // Serialize player keys as strings — ulong Dictionary keys are unreliable across reload.
                var dto = new StoredDataDto
                {
                    timeSaved = storedData.timeSaved,
                    playerData = new Dictionary<string, StoredData.PlayerData>()
                };
                foreach (var kv in storedData.playerData)
                    dto.playerData[kv.Key.ToString()] = kv.Value;

                File.WriteAllText(path, JsonConvert.SerializeObject(dto, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AutoCodeLock] Data save failed: " + ex.Message);
            }
        }

        private void LoadData()
        {
            try
            {
                string path = AutoCodeLockHost.Instance.DataPath;

                if (File.Exists(path))
                {
                    var dto = JsonConvert.DeserializeObject<StoredDataDto>(File.ReadAllText(path));
                    storedData = new StoredData { timeSaved = dto?.timeSaved ?? 0 };
                    if (dto?.playerData != null)
                    {
                        foreach (var kv in dto.playerData)
                        {
                            if (ulong.TryParse(kv.Key, out ulong id) && kv.Value != null)
                                storedData.playerData[id] = kv.Value;
                        }
                    }
                    Debug.Log($"[AutoCodeLock] OK: Loaded {storedData.playerData.Count} player data entries");
                }
                else
                {
                    Debug.Log("[AutoCodeLock] No data file found. Creating new data file.");
                    storedData = new StoredData();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AutoCodeLock] Data load failed: " + ex.Message);
                storedData = new StoredData();
            }

            if (storedData?.playerData == null)
                storedData = new StoredData();
        }

        private class StoredDataDto
        {
            public Dictionary<string, StoredData.PlayerData> playerData = new Dictionary<string, StoredData.PlayerData>();
            public int timeSaved;
        }

        #endregion

        #region Localization

        private void RegisterLangMessages()
        {
            AutoCodeLockHost.Instance.Lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Prefix"] = "[<color=#62cd32>AutoCodeLock</color>] : ",

                ["Notification.CodelockSecured"] = "Codelock secured and locked\nCode: <color=#62cd32>{0}</color>",
                ["Notification.CodelockSecured.Guest"] = "Codelock secured and locked\nCode: <color=#62cd32>{0}</color>\nGuest code: <color=#62cd32>{1}</color>",
                ["Notification.NotLocked"] = "Codelock not auto-locked. You are not the object owner",
                ["Notification.NotLocked.RaidBlock"] = "Codelock not auto-locked. You are currently raid blocked",
                ["Notification.NotLocked.CombatBlock"] = "Codelock not auto-locked. You are currently combat blocked",
                ["Notification.NotLocked.Plugin"] = "Codelock not auto-locked for reason: {0}",

                ["Notification.NoPermission"] = "You do not have permission to use this command",
                ["Notification.NoDoorFound"] = "You are not looking at a door",
                ["Notification.NoBuildPriv"] = "You need building privilege to use this command",
                ["Notification.NotDoorOwner"] = "You are not the owner of this door",
                ["Notification.DoorCloserPlaced"] = "You have placed a door closer on this door",
                ["Notification.DoorCloserRemoved"] = "You have removed the door closer from this door",

                ["Notification.ApplyingCodeChanges"] = "Applying pin code to all of your codelocks",
                ["Notification.ApplyingGuestCodeChanges"] = "Applying guest code to all of your codelocks",
                ["Notification.ApplyingDoorCloserChanges"] = "Applying close delay to all of your door closers",
                ["Notification.ApplyingHatchCloserChanges"] = "Applying close delay to all of your ladder hatches",

                ["Notification.NoPlayerDlcApi"] = "The server must installed PlayerDLCAPI to utilize this feature",
                ["Notification.SkinArgs"] = "Enter a skin type to select: {0}",
                ["Notification.NoSkinPermission"] = "You do not have permission to use this skin",
                ["Notification.SkinSet"] = "Preferred skin set to: {0}",
                ["Notification.NoLockItem"] = "Codelock not deployed. Carry a codelock or grant autocodelock.nolockneed",

                ["Label.Title"] = "AutoCodeLock",
                ["Label.AutoLock"] = "Auto-lock",
                ["Label.DeployDoor"] = "Deploy on doors",
                ["Label.DeployBox"] = "Deploy on boxes",
                ["Label.DeployLocker"] = "Deploy on lockers",
                ["Label.DeployCupboard"] = "Deploy on cupboards",
                ["Label.DeployDoorCloser"] = "Deploy door closer",
                ["Label.EnableGuestCode"] = "Set guest code",
                ["Label.PinCode"] = "Auto-set pin code",
                ["Label.GuestCode"] = "Auto-set guest code",
                ["Label.DoorDelay"] = "Close delay (doors)",
                ["Label.HatchDelay"] = "Close delay (hatches)",
                ["Label.DisableCloser"] = "Disable door closers"
            });
        }

        #endregion

        #region Helpers

        private static T ParseType<T>(string type)
        {
            try { return (T)Enum.Parse(typeof(T), type, true); }
            catch { return default; }
        }

        private static string ToSentence(IEnumerable<string> items)
        {
            if (items == null)
                return string.Empty;

            using IEnumerator<string> enumerator = items.GetEnumerator();
            if (!enumerator.MoveNext())
                return string.Empty;

            string firstItem = enumerator.Current;
            if (!enumerator.MoveNext())
                return firstItem;

            StringBuilder builder = new StringBuilder(firstItem);
            bool moreItems = true;
            while (moreItems)
            {
                string item = enumerator.Current;
                moreItems = enumerator.MoveNext();
                builder.Append(moreItems ? ", " : " and ");
                builder.Append(item);
            }

            return builder.ToString();
        }

        #endregion
    }
}
