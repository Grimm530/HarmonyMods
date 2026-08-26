using System;
using System.Collections.Generic;
using System.IO;
using Facepunch;
using Newtonsoft.Json;
using Oxide.Ext.Chaos.UIFramework;
using UnityEngine;
using UnityEngine.UI;

using Color = Oxide.Ext.Chaos.UIFramework.Color;
using Font = Oxide.Ext.Chaos.UIFramework.Font;
using UIAnchor = Oxide.Ext.Chaos.UIFramework.Anchor;

namespace DynamicCupShareHarmony
{
    public partial class DynamicCupSharePlugin
    {
        #region Fields

        private static readonly DateTime Epoch = new DateTime(1970, 1, 1);

        public static DynamicCupSharePlugin Instance { get; private set; }

        public string Title => "DynamicCupShare";

        public CommandCallbackHandler CallbackHandler => _callbackHandler;

        internal static ConfigData Configuration;
        internal static StoredData storedData;
        internal static List<ShareType> AllowedShareTypes;
        private bool _seedBlueprintFlags;

        private RaycastHit _raycastHit;
        private Func<BaseEntity, BasePlayer, bool> _shouldIgnoreTrap;
        private Func<BaseEntity, BasePlayer, bool> _shouldIgnoreTurret;
        private bool _entitiesReady;

        public bool SamSitesEnabled =>
            Configuration != null
            && Configuration.Turrets.IncludeSameSites
            && (CanShare(ShareType.Turret) || Configuration.Security.TurretShareOverride);

        public bool BuildingRestrictionsEnabled =>
            Configuration != null
            && (Configuration.Building.PreventIceberg || Configuration.Building.PreventIcelake || Configuration.Building.PreventIcesheet);

        #endregion

        #region Lifecycle

        public void HarmonyInit()
        {
            Instance = this;
            LoadConfig();
            RegisterLangMessages();
            DynamicCupShareHost.Instance?.ReloadLanguage();
            SetupUIComponents();
            LoadData();

            AllowedShareTypes = Configuration.Sharing.Allowed.AllowedShareTypes;

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected) continue;
                ChaosUI.Destroy(player, UI_MENU);
            }
        }

        public void HarmonyServerInitialized()
        {
            _shouldIgnoreTrap = (entity, player) =>
            {
                if (!player || !entity || InAdminMode(player))
                    return true;

                if (player.IsBuildingAuthed())
                    return true;

                return CanUseTurret(player, entity);
            };

            _shouldIgnoreTurret = (entity, player) =>
            {
                if (!player || !entity || InAdminMode(player))
                    return true;

                if (entity is AutoTurret autoTurret)
                {
                    if (autoTurret.IsAuthed(player))
                        return true;

                    if (!autoTurret.AnyAuthed() || !autoTurret.IsAuthed(autoTurret.OwnerID))
                        return false;
                }

                return CanUseTurret(player, entity);
            };

            SetupLockableContainers();

            if (Configuration.Data.PurgeAfter > 0)
                PurgeOldData();

            foreach (BasePlayer player in BasePlayer.activePlayerList)
                OnPlayerConnected(player);

            StartBuildingWorkbench();

            if (ServerMgr.Instance != null)
                ServerMgr.Instance.Invoke(FindRegisterEntitiesDelayed, 5f);
            else
                FindRegisterEntitiesDelayed();
        }

        private void FindRegisterEntitiesDelayed()
        {
            try { FindRegisterEntities(); }
            catch (Exception ex) { Debug.LogWarning("[DynamicCupShare] FindRegisterEntities: " + ex.Message); }
            _entitiesReady = true;
        }

        public void HarmonyUnload()
        {
            AuthorizationQueue.OnUnload();
            UpdateCycler.OnUnload();

            TemporaryShares.Save();
            TemporaryShares.OnUnload();

            ResetLockableContainerPrefabs();

            PlayerPrivilege.OnUnload();
            SamSiteMemory.OnUnload();
            TargetTriggerEntity.OnUnload();

            foreach (BasePlayer player in BasePlayer.activePlayerList)
                OnPlayerDisconnected(player);

            StopBuildingWorkbench();

            SaveData();
            CancelBlueprintShareTimers();

            if (_callbackHandler != null)
            {
                _callbackHandler.Clear();
                _callbackHandler.Unregister();
                _callbackHandler = null;
            }

            Configuration = null;
            Instance = null;
        }

        public void RegisterPermissions()
        {
            if (Configuration == null) return;
            PermissionsBridge.RegisterPermission(Configuration.Permission.ClanShare.Permission);
            PermissionsBridge.RegisterPermission(Configuration.Permission.FriendShare.Permission);
            PermissionsBridge.RegisterPermission(Configuration.Permission.TeamShare.Permission);
            PermissionsBridge.RegisterPermission(Configuration.Permission.AdminPermission);
            if (!string.IsNullOrEmpty(Configuration.Permission.BlueprintUse))
                PermissionsBridge.RegisterPermission(Configuration.Permission.BlueprintUse);
            if (!string.IsNullOrEmpty(Configuration.Permission.BlueprintToggle))
                PermissionsBridge.RegisterPermission(Configuration.Permission.BlueprintToggle);
            if (!string.IsNullOrEmpty(Configuration.Permission.BlueprintShare))
                PermissionsBridge.RegisterPermission(Configuration.Permission.BlueprintShare);
            if (!string.IsNullOrEmpty(Configuration.Permission.BlueprintShow))
                PermissionsBridge.RegisterPermission(Configuration.Permission.BlueprintShow);
            if (!string.IsNullOrEmpty(Configuration.Permission.BlueprintBypass))
                PermissionsBridge.RegisterPermission(Configuration.Permission.BlueprintBypass);
            if (!string.IsNullOrEmpty(Configuration.Permission.BuildingWorkbenchUse))
                PermissionsBridge.RegisterPermission(Configuration.Permission.BuildingWorkbenchUse);
            if (!string.IsNullOrEmpty(Configuration.Permission.BuildingWorkbenchCancelCraft))
                PermissionsBridge.RegisterPermission(Configuration.Permission.BuildingWorkbenchCancelCraft);

            GrantDefaultPlayerPermissions();
        }

        private void GrantDefaultPlayerPermissions()
        {
            if (!PermissionsBridge.IsAvailable)
                return;

            string[] playerPerms =
            {
                Configuration.Permission.ClanShare.Permission,
                Configuration.Permission.FriendShare.Permission,
                Configuration.Permission.TeamShare.Permission,
                Configuration.Permission.BlueprintUse,
                Configuration.Permission.BlueprintToggle,
                Configuration.Permission.BlueprintShare,
                Configuration.Permission.BlueprintShow,
                Configuration.Permission.BuildingWorkbenchUse,
                Configuration.Permission.BuildingWorkbenchCancelCraft,
            };

            int granted = 0;
            for (int i = 0; i < playerPerms.Length; i++)
            {
                string perm = playerPerms[i];
                if (string.IsNullOrEmpty(perm))
                    continue;
                PermissionsBridge.RegisterPermission(perm);
                if (PermissionsBridge.GrantGroupPermission("default", perm))
                    granted++;
            }

            if (granted > 0)
                Debug.Log($"[DynamicCupShare] Granted {granted} player permission(s) to group 'default'.");
        }

        public void OnServerSave()
        {
            SaveData();
            TemporaryShares.Save();
        }

        public void OnNewSave()
        {
            if (Configuration?.Blueprints == null || !Configuration.Blueprints.ClearDataOnWipe)
                return;

            if (storedData?.playerData == null) return;
            foreach (var kvp in storedData.playerData)
                kvp.Value.learntBlueprints = new BlueprintLearnData();
            SaveData();
            Debug.Log("[DynamicCupShare] Blueprint share data cleared on wipe.");
        }

        #endregion

        #region Player / Chat

        public void OnPlayerConnected(BasePlayer player)
        {
            if (player == null) return;
            PlayerPrivilege.AddPlayer(player);
            ulong userId = player.GetUserId();
            storedData.SetupPlayer(userId).lastOnline = UnixTimeStampUtc();
            PlayerEntities.Get(userId)?.RebuildAll();
            OnBuildingWorkbenchPlayerConnected(player);
        }

        public void OnPlayerDisconnected(BasePlayer player)
        {
            if (player == null) return;
            OnBuildingWorkbenchPlayerDisconnected(player);
            PlayerPrivilege.RemovePlayer(player);
            ChaosUI.Destroy(player, UI_MENU);
        }

        public bool TryHandleChat(BasePlayer player, string command, string[] args)
        {
            if (player == null || string.IsNullOrEmpty(command) || Configuration == null)
                return false;

            if (string.Equals(command, Configuration.Sharing.ChatCommand, StringComparison.OrdinalIgnoreCase))
            {
                CmdShare(player);
                return true;
            }

            if (string.Equals(command, "shareplayer", StringComparison.OrdinalIgnoreCase))
            {
                CmdSharePlayer(player, args);
                return true;
            }

            if (string.Equals(command, "dcsadmin", StringComparison.OrdinalIgnoreCase))
            {
                CmdDcsAdmin(player);
                return true;
            }

            string bpCommand = Configuration.Blueprints?.ChatCommand;
            if (!string.IsNullOrEmpty(bpCommand) && string.Equals(command, bpCommand, StringComparison.OrdinalIgnoreCase))
            {
                CmdBlueprintShare(player, args);
                return true;
            }

            return false;
        }

        private void CmdShare(BasePlayer player)
        {
            if (!HasShareUiAccess(player))
            {
                Message(player, "Error.NoPermissions");
                return;
            }

            ulong userId = player.GetUserId();
            StoredData.PlayerData playerData = storedData.SetupPlayer(userId);
            OpenShareMenu(player, playerData, 0UL, ShareUiPage.Sharing);
        }

        private void CmdSharePlayer(BasePlayer player, string[] args)
        {
            if (!player.IsAdmin)
                return;

            if (args == null || args.Length != 1)
            {
                player.ChatMessage("/shareplayer <steamID>");
                return;
            }

            if (!ulong.TryParse(args[0], out ulong target))
            {
                player.ChatMessage("Invalid Steam ID entered");
                return;
            }

            StoredData.PlayerData playerData = storedData.SetupPlayer(target);
            if (playerData == null)
            {
                player.ChatMessage("Failed to get or create data for the target user ID");
                return;
            }

            OpenShareMenu(player, playerData, target);
        }

        private void CmdDcsAdmin(BasePlayer player)
        {
            if (!player.HasPermission(Configuration.Permission.AdminPermission))
            {
                Message(player, "Error.NoPermissions");
                return;
            }

            if (PlayerPrivilege.Find(player, out PlayerPrivilege playerPrivilege))
            {
                if (playerPrivilege.InAdminMode)
                {
                    playerPrivilege.InAdminMode = false;
                    Message(player, "Message.AdminDisabled");
                }
                else
                {
                    playerPrivilege.InAdminMode = true;
                    Message(player, "Message.AdminEnabled");
                }
            }
        }

        #endregion

        #region Entity Management

        public void OnEntitySpawned(BuildingPrivlidge buildingPrivlidge)
        {
            if (!_entitiesReady) return;
            Interface.NextTick(() =>
            {
                if (buildingPrivlidge)
                    PlayerEntities.GetOrCreate(buildingPrivlidge.OwnerID)?.AddEntity(buildingPrivlidge, true);
            });
        }

        public void OnEntitySpawned(AutoTurret autoTurret)
        {
            if (!_entitiesReady) return;
            Interface.NextTick(() =>
            {
                if (autoTurret)
                    PlayerEntities.GetOrCreate(autoTurret.OwnerID)?.AddEntity(autoTurret, true);
            });
        }

        public void OnEntitySpawned(CodeLock codeLock)
        {
            if (!_entitiesReady) return;
            Interface.NextTick(() =>
            {
                if (!codeLock)
                    return;

                PlayerEntities.GetOrCreate(GetShareOwnerId(codeLock))?.AddEntity(codeLock, true);

                StorageContainer storageContainer = codeLock.GetParentEntity() as StorageContainer;
                if (!storageContainer)
                    return;

                SetLockPositionRotation(storageContainer, codeLock);
            });
        }

        public void OnEntityKill(BuildingPrivlidge buildingPrivlidge)
        {
            if (buildingPrivlidge)
            {
                PlayerEntities.Get(buildingPrivlidge.OwnerID)?.RemoveEntity(buildingPrivlidge, true);
                OnBuildingWorkbenchCupboardCleared(buildingPrivlidge.buildingID);
            }
        }

        public void OnEntityKill(AutoTurret autoTurret)
        {
            if (!autoTurret) return;
            PlayerEntities.Get(autoTurret.OwnerID)?.RemoveEntity(autoTurret, true);
            TargetTriggerEntity.Remove(autoTurret.targetTrigger);
        }

        public void OnEntityKill(GunTrap gunTrap)
        {
            if (gunTrap)
                TargetTriggerEntity.Remove(gunTrap.trigger);
        }

        public void OnEntityKill(FlameTurret flameTurret)
        {
            if (flameTurret)
                TargetTriggerEntity.Remove(flameTurret.trigger);
        }

        public void OnEntityKill(CodeLock codeLock)
        {
            if (codeLock)
                PlayerEntities.Get(GetShareOwnerId(codeLock))?.RemoveEntity(codeLock, true);
        }

        #endregion

        #region Lock / Auth / Targeting hooks

        public void CanChangeCode(BasePlayer player, CodeLock codeLock)
        {
            RebuildCodeLockShares(codeLock);
        }

        public void RebuildCodeLockShares(CodeLock codeLock)
        {
            Interface.NextTick(() =>
            {
                if (!codeLock)
                    return;

                ShareType shareType = GetShareTypeFromEntity(codeLock.GetParentEntity());
                if (shareType != ShareType.None)
                    AuthorizationQueue.Enqueue(TemporaryShares.RebuildSharesFor(shareType, codeLock));
            });
        }

        public object CanUseLockedEntity(BasePlayer player, BaseLock baseLock)
        {
            if (!player || !baseLock || !baseLock.IsLocked())
                return null;

            if (InAdminMode(player))
                return true;

            ulong userId = player.GetUserId();
            if (GetShareOwnerId(baseLock) == userId)
                return true;

            if (baseLock is KeyLock && Configuration.Sharing.DisableKeylocks)
                return null;

            if (CanUseLockedObject(player, baseLock))
                return true;

            return null;
        }

        public object CanUnlock(BasePlayer player, BaseLock baseLock)
        {
            if (!player || !baseLock)
                return null;

            if (InAdminMode(player))
            {
                if (baseLock is CodeLock adminCodeLock)
                    Effect.server.Run(adminCodeLock.effectUnlocked.resourcePath, baseLock, 0U, Vector3.zero, Vector3.forward, null, false);

                baseLock.SetFlagLocal(BaseEntity.Flags.Locked, false);
                baseLock.SendNetworkUpdate();
                return true;
            }

            if (Configuration.Security.ShareLockUnlock && CanUseLockedObject(player, baseLock))
            {
                if (baseLock is CodeLock codeLock)
                {
                    if (codeLock.IsCodeEntryBlocked())
                        return null;

                    Effect.server.Run(codeLock.effectUnlocked.resourcePath, codeLock, 0, Vector3.zero, Vector3.forward, null, false);
                    codeLock.SetFlagLocal(BaseEntity.Flags.Locked, false);
                    codeLock.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                    return true;
                }

                if (baseLock is KeyLock keyLock)
                {
                    keyLock.SetFlagLocal(BaseEntity.Flags.Locked, false);
                    keyLock.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                    return true;
                }
            }

            return null;
        }

        public object CanLock(BasePlayer player, BaseLock baseLock)
        {
            if (!player || !baseLock)
                return null;

            if (InAdminMode(player))
            {
                if (baseLock is CodeLock adminCodeLock)
                    Effect.server.Run(adminCodeLock.effectLocked.resourcePath, baseLock, 0u, Vector3.zero, Vector3.forward, null, false);

                baseLock.SetFlagLocal(BaseEntity.Flags.Locked, true);
                baseLock.SendNetworkUpdate();
                return true;
            }

            if (Configuration.Security.ShareLockUnlock && CanUseLockedObject(player, baseLock))
            {
                if (baseLock is CodeLock codeLock)
                {
                    if (codeLock.IsCodeEntryBlocked())
                        return null;

                    Effect.server.Run(codeLock.effectLocked.resourcePath, codeLock, 0, Vector3.zero, Vector3.forward, null, false);
                    codeLock.SetFlagLocal(BaseEntity.Flags.Locked, true);
                    codeLock.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                    return true;
                }

                if (baseLock is KeyLock keyLock)
                {
                    keyLock.SetFlagLocal(BaseEntity.Flags.Locked, true);
                    keyLock.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                    return true;
                }
            }

            return null;
        }

        public object OnEntityEnter(TargetTrigger targetTrigger, BasePlayer player)
        {
            if (!player || !player.GetUserId().IsSteamId() || !targetTrigger)
                return null;

            if (!CanShare(ShareType.Turret) && !Configuration.Security.TurretShareOverride)
                return null;

            if (TargetTriggerEntity.ShouldIgnoreTrigger(targetTrigger))
                return null;

            if (!TargetTriggerEntity.TryGet(targetTrigger, out TargetTriggerEntity targetTriggerEntity))
            {
                BaseEntity baseEntity = targetTrigger.GetComponentInParent<BaseEntity>();
                if (!baseEntity)
                    return null;

                if (baseEntity is NPCAutoTurret { OwnerID: 0UL })
                {
                    TargetTriggerEntity.IgnoreTrigger(targetTrigger);
                    return null;
                }

                if (baseEntity is GunTrap gunTrap && (gunTrap.OwnerID == 0UL || !Configuration.Turrets.IncludeGunTraps))
                    return null;

                if (baseEntity is FlameTurret flameTurret && (flameTurret.OwnerID == 0UL || !Configuration.Turrets.IncludeFlameTurrets))
                    return null;

                if (baseEntity is AutoTurret { OwnerID: 0UL })
                    return null;

                targetTriggerEntity = new TargetTriggerEntity(targetTrigger, baseEntity,
                    baseEntity is AutoTurret ? _shouldIgnoreTurret : _shouldIgnoreTrap);
            }

            return targetTriggerEntity?.ShouldIgnore(player);
        }

        public object OnSamSiteTarget(SamSite samSite, SamSite.ISamSiteTarget samSiteTarget)
        {
            if (!samSite || samSite.OwnerID == 0UL)
                return null;

            if (samSiteTarget is not BaseCombatEntity baseCombatEntity)
                return null;

            if (baseCombatEntity is BaseVehicle baseVehicle && !baseVehicle.AnyMounted())
                return true;

            if (!SamSiteMemory.TryGet(samSite, out SamSiteMemory samSiteMemory))
                samSiteMemory = new SamSiteMemory(samSite);

            if (samSiteMemory.IsKnown(baseCombatEntity, out bool shouldIgnore))
                return shouldIgnore ? (object)true : null;

            shouldIgnore = CanUseSamSite(samSite, baseCombatEntity);
            samSiteMemory.Remember(baseCombatEntity, shouldIgnore);
            return shouldIgnore ? (object)true : null;
        }

        public object CanBuild(Planner planner, Construction construction, Construction.Target target)
        {
            if (!BuildingRestrictionsEnabled)
                return null;

            BasePlayer player = planner.GetOwnerPlayer();
            if (!player)
                return null;

            Construction.Placement placement = new Construction.Placement(target);
            if (target.socket != null)
            {
                List<Socket_Base> list = Facepunch.Pool.Get<List<Socket_Base>>();
                construction.FindMaleSockets(target, list);

                foreach (Socket_Base current in list)
                {
                    if (!(target.entity) || !(target.socket) || !target.entity.IsOccupied(target.socket))
                    {
                        placement = current.DoPlacement(target);
                        if (placement.isPopulated)
                            break;
                    }
                }

                Facepunch.Pool.FreeUnmanaged(ref list);

                if (!placement.isPopulated)
                    return null;
            }
            else
            {
                placement.position = target.position;
                placement.rotation = Quaternion.Euler(target.rotation);

                if (placement.rotation == Quaternion.identity)
                    placement.rotation = Quaternion.Euler(0, planner.GetOwnerPlayer().transform.rotation.y, 0);
            }

            if (Physics.Raycast(placement.position, Vector3.down, out _raycastHit, placement.position.y, 65536))
            {
                string colliderName = _raycastHit.collider.name.ToLower();
                if (Configuration.Building.PreventIceberg && colliderName.StartsWith("iceberg"))
                {
                    Message(player, "Error.NoBuild.Iceberg");
                    return false;
                }

                if (Configuration.Building.PreventIcelake && colliderName.StartsWith("ice_lake"))
                {
                    Message(player, "Error.NoBuild.IceLake");
                    return false;
                }

                if (Configuration.Building.PreventIcesheet && colliderName.StartsWith("ice_sheet"))
                {
                    Message(player, "Error.NoBuild.IceSheet");
                    return false;
                }
            }

            return null;
        }

        public object OnTurretAuthorize(AutoTurret autoTurret, BasePlayer player)
        {
            if (!autoTurret || !player)
                return null;

            if (autoTurret.OwnerID == player.GetUserId() || !autoTurret.IsAuthed(autoTurret.OwnerID))
                return null;

            if (InAdminMode(player))
                return null;

            bool isFriendly = IsFriendly(autoTurret.OwnerID, player.GetUserId());

            if (Configuration.Security.BlockNonAuth && !isFriendly)
            {
                Message(player, "Error.ClearAuthDenied");
                return true;
            }

            if (Configuration.Security.BlockAuth && isFriendly)
            {
                Message(player, "Error.ClearAuthDenied");
                return true;
            }

            return null;
        }

        public object OnTurretClearList(AutoTurret autoTurret, BasePlayer player)
        {
            if (!autoTurret || !player)
                return null;

            if (autoTurret.OwnerID == player.GetUserId())
                return null;

            if (InAdminMode(player))
                return null;

            bool isFriendly = IsFriendly(autoTurret.OwnerID, player.GetUserId());

            if (Configuration.Security.BlockNonAuth && !isFriendly)
            {
                Message(player, "Error.ClearAuthDenied");
                return true;
            }

            if (Configuration.Security.BlockAuth && isFriendly)
            {
                Message(player, "Error.ClearAuthDenied");
                return true;
            }

            return null;
        }

        public object OnCupboardClearList(BuildingPrivlidge buildingPrivlidge, BasePlayer player)
        {
            if (!buildingPrivlidge || !player)
                return null;

            if (buildingPrivlidge.OwnerID == player.GetUserId())
                return null;

            if (InAdminMode(player))
                return null;

            bool isFriendly = IsFriendly(buildingPrivlidge.OwnerID, player.GetUserId());

            if (Configuration.Security.BlockNonAuth && !isFriendly)
            {
                Message(player, "Error.ClearAuthDenied");
                return true;
            }

            if (Configuration.Security.BlockAuth && isFriendly)
            {
                Message(player, "Error.ClearAuthDenied");
                return true;
            }

            return null;
        }

        public object OnCupboardAuthorize(BuildingPrivlidge buildingPrivlidge, BasePlayer player)
        {
            if (!buildingPrivlidge || !player)
                return null;

            if (InAdminMode(player))
                return null;

            if (Configuration.Security.MaxCupboardAuth > 0 && buildingPrivlidge.authorizedPlayers.Count >= Configuration.Security.MaxCupboardAuth)
            {
                Message(player, "Error.MaxCupboardAuth");
                return true;
            }

            if (buildingPrivlidge.OwnerID == player.GetUserId() || !buildingPrivlidge.IsAuthed(buildingPrivlidge.OwnerID))
                return null;

            bool isFriendly = IsFriendly(buildingPrivlidge.OwnerID, player.GetUserId());

            if (Configuration.Security.BlockNonAuth && !isFriendly)
            {
                Message(player, "Error.AuthDenied");
                return true;
            }

            if (Configuration.Security.BlockAuth && isFriendly)
            {
                Message(player, "Error.AuthDenied");
                return true;
            }

            return null;
        }

        private bool IsFriendly(ulong ownerId, ulong playerId)
        {
            return (Configuration.Sharing.Clan.Enabled && AreClanMates(ownerId, playerId)) ||
                   (Configuration.Sharing.Friend.Enabled && AreFriends(ownerId, playerId)) ||
                   (Configuration.Sharing.Team.Enabled && AreTeamMates(ownerId, playerId));
        }

        #endregion

        #region Clan / Friend / Team events

        public void OnFriendRemoved(string playerId, string friendId)
        {
            if (Configuration != null && Configuration.Sharing.Friend.Enabled
                && ulong.TryParse(playerId, out ulong id))
                PlayerEntities.Get(id)?.RebuildAll();

            OnBlueprintFriendRemoved(playerId, friendId);
        }

        public void OnFriendAdded(string playerId, string friendId)
        {
            if (Configuration != null && Configuration.Sharing.Friend.Enabled
                && ulong.TryParse(playerId, out ulong id))
                PlayerEntities.Get(id)?.RebuildAll();

            OnBlueprintFriendAdded(playerId, friendId);
        }

        public void OnClanMemberJoined(string tag, ulong playerId, List<ulong> clanMembers)
        {
            if (Configuration != null && Configuration.Sharing.Clan.Enabled)
            {
                PlayerEntities.Get(playerId)?.RebuildAll();
                if (clanMembers != null)
                {
                    for (int i = 0; i < clanMembers.Count; i++)
                        PlayerEntities.Get(clanMembers[i])?.RebuildAll();
                }
            }

            OnBlueprintClanMemberJoined(playerId);
        }

        public void OnClanMemberGone(string tag, ulong playerId, List<ulong> clanMembers)
        {
            if (Configuration != null && Configuration.Sharing.Clan.Enabled)
            {
                PlayerEntities.Get(playerId)?.RebuildAll();
                if (clanMembers != null)
                {
                    for (int i = 0; i < clanMembers.Count; i++)
                        PlayerEntities.Get(clanMembers[i])?.RebuildAll();
                }
            }

            OnBlueprintClanMemberGone(playerId);
        }

        public void OnClanDisbanded(string tag, List<ulong> clanMembers)
        {
            if (Configuration != null && Configuration.Sharing.Clan.Enabled && clanMembers != null)
            {
                for (int i = 0; i < clanMembers.Count; i++)
                    PlayerEntities.Get(clanMembers[i])?.RebuildAll();
            }

            OnBlueprintClanDisbanded(clanMembers);
        }

        public void RebuildClanMemberEntities(ulong playerId, List<ulong> clanMembers)
        {
            PlayerEntities.Get(playerId)?.RebuildAll();
            if (clanMembers == null) return;
            for (int i = 0; i < clanMembers.Count; i++)
                PlayerEntities.Get(clanMembers[i])?.RebuildAll();
        }

        public void OnClanAllianceDissolved(string tag, string alliedTag)
        {
            if (Configuration == null || !Configuration.Sharing.Clan.Alliances)
                return;

            List<ulong> members = Facepunch.Pool.Get<List<ulong>>();
            SocialBridges.Clans.GetMembersByTag(tag, members);
            SocialBridges.Clans.GetMembersByTag(alliedTag, members);

            for (int i = 0; i < members.Count; i++)
                PlayerEntities.Get(members[i])?.RebuildAll();

            Facepunch.Pool.FreeUnmanaged(ref members);
        }

        public void OnTeamAcceptInvite(RelationshipManager.PlayerTeam playerTeam, BasePlayer player)
        {
            OnTeamLeave(playerTeam, player);
            OnBlueprintTeamJoined(playerTeam, player);
        }

        public void OnTeamMemberAdded(RelationshipManager.PlayerTeam playerTeam, ulong playerId)
        {
            OnTeamLeave(playerTeam, null);
        }

        public void OnTeamMemberRemoved(RelationshipManager.PlayerTeam playerTeam, ulong playerId)
        {
            if (Configuration != null && Configuration.Sharing.Team.Enabled && playerTeam != null)
            {
                PlayerEntities.Get(playerId)?.RebuildAll();
                if (playerTeam.members != null)
                {
                    for (int i = 0; i < playerTeam.members.Count; i++)
                        PlayerEntities.Get(playerTeam.members[i])?.RebuildAll();
                }
            }

            OnBlueprintTeamLeft(playerId);
        }

        public void OnTeamLeave(RelationshipManager.PlayerTeam playerTeam, BasePlayer player)
        {
            if (Configuration == null || !Configuration.Sharing.Team.Enabled || playerTeam?.members == null)
                return;

            for (int i = 0; i < playerTeam.members.Count; i++)
                PlayerEntities.Get(playerTeam.members[i])?.RebuildAll();
        }

        public void OnTeamDisband(RelationshipManager.PlayerTeam playerTeam)
        {
            OnTeamLeave(playerTeam, null);
            if (playerTeam?.members == null) return;
            for (int i = 0; i < playerTeam.members.Count; i++)
                OnBlueprintTeamLeft(playerTeam.members[i]);
        }

        #endregion
    }
}
