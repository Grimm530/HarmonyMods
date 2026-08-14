using System.Collections.Generic;
using Facepunch;
using UnityEngine;

namespace DynamicCupShareHarmony
{
    public partial class DynamicCupSharePlugin
    {
        #region Lockable Containers

        private readonly Hash<string, StorageContainer> _customLockableReferences = new Hash<string, StorageContainer>();

        private void SetupLockableContainers()
        {
            SetupLockableContainer("assets/prefabs/deployable/composter/composter.prefab", Configuration.Sharing.Allowed.Composters);
            SetupLockableContainer("assets/prefabs/deployable/dropbox/dropbox.deployed.prefab", Configuration.Sharing.Allowed.DropBoxes);
            SetupLockableContainer("assets/prefabs/deployable/vendingmachine/vendingmachine.deployed.prefab", Configuration.Sharing.Allowed.VendingMachines);
            SetupLockableContainer("assets/prefabs/deployable/furnace/furnace.prefab", Configuration.Sharing.Allowed.Furnace);
            SetupLockableContainer("assets/prefabs/deployable/furnace.large/furnace.large.prefab", Configuration.Sharing.Allowed.Furnace);
            SetupLockableContainer("assets/prefabs/deployable/oil refinery/refinery_small_deployed.prefab", Configuration.Sharing.Allowed.Refinery);
            SetupLockableContainer("assets/prefabs/deployable/bbq/bbq.deployed.prefab", Configuration.Sharing.Allowed.Bbq);
            SetupLockableContainer("assets/prefabs/deployable/planters/planter.small.deployed.prefab", Configuration.Sharing.Allowed.Planters);
            SetupLockableContainer("assets/prefabs/deployable/planters/planter.large.deployed.prefab", Configuration.Sharing.Allowed.Planters);
            SetupLockableContainer("assets/prefabs/deployable/hitch & trough/hitchtrough.deployed.prefab", Configuration.Sharing.Allowed.Hitch);
            SetupLockableContainer("assets/prefabs/deployable/mixingtable/mixingtable.deployed.prefab", Configuration.Sharing.Allowed.MixingTable);
            SetupLockableContainer("assets/prefabs/deployable/chickencoop/chickencoop.deployed.prefab", Configuration.Sharing.Allowed.ChickenCoop);
            SetupLockableContainer("assets/prefabs/deployable/beehive/beehive.deployed.prefab", Configuration.Sharing.Allowed.Beehive);

            foreach (BaseNetworkable baseNetworkable in BaseNetworkable.serverEntities)
            {
                if (baseNetworkable is not StorageContainer storage)
                    continue;

                if (_customLockableReferences.TryGetValue(baseNetworkable.PrefabName, out StorageContainer prefab))
                    storage.isLockable = prefab.isLockable;
            }
        }

        private void SetupLockableContainer(string prefabPath, bool enabled)
        {
            GameObject prefab = GameManager.server.FindPrefab(prefabPath);
            StorageContainer storageContainer = prefab ? prefab.GetComponent<StorageContainer>() : null;
            if (storageContainer)
            {
                storageContainer.isLockable = enabled;
                _customLockableReferences[prefabPath] = storageContainer;
            }
        }

        private void ResetLockableContainerPrefabs()
        {
            foreach (StorageContainer storageContainer in _customLockableReferences.Values)
            {
                if (storageContainer)
                    storageContainer.isLockable = false;
            }
        }

        private void SetLockPositionRotation(StorageContainer storageContainer, BaseLock baseLock)
        {
            if (storageContainer is HitchTrough)
            {
                baseLock.transform.localPosition = new Vector3(0.79f, 0.73f, -0.32f);
                baseLock.transform.localRotation = Quaternion.Euler(0, 90, 0);
                return;
            }

            if (storageContainer is Composter)
            {
                baseLock.transform.localPosition = new Vector3(0f, 0.75f, 0.59f);
                baseLock.transform.localRotation = Quaternion.Euler(0, 90, 0);
                return;
            }

            if (storageContainer is PlanterBox)
            {
                switch (storageContainer.ShortPrefabName)
                {
                    case "planter.small.deployed":
                        baseLock.transform.localPosition = new Vector3(0f, 0.3f, 0.55f);
                        baseLock.transform.localRotation = Quaternion.Euler(0, 90, 0);
                        return;
                    case "planter.large.deployed":
                        baseLock.transform.localPosition = new Vector3(0f, 0.3f, 1.47f);
                        baseLock.transform.localRotation = Quaternion.Euler(0, 90, 0);
                        return;
                    case "planter.triangle.deployed":
                        baseLock.transform.localPosition = new Vector3(0.57f, 0.25f, 0f);
                        baseLock.transform.localRotation = Quaternion.Euler(0, 0, 0);
                        return;
                    case "bathtub.planter.deployed":
                        baseLock.transform.localPosition = new Vector3(0f, 0.405f, -0.58f);
                        baseLock.transform.localRotation = Quaternion.Euler(0, 90, 0);
                        return;
                    case "minecart.planter.deployed":
                        baseLock.transform.localPosition = new Vector3(-0.75f, 0.65f, 0f);
                        baseLock.transform.localRotation = Quaternion.Euler(0, 0, 0);
                        return;
                    case "railroadplanter.deployed":
                        baseLock.transform.localPosition = new Vector3(1.46f, 0.35f, 0f);
                        baseLock.transform.localRotation = Quaternion.Euler(0, 0, 0);
                        return;
                    case "triangle_railroad_planter.deployed":
                        baseLock.transform.localPosition = new Vector3(0.55f, 0.2f, 0f);
                        baseLock.transform.localRotation = Quaternion.Euler(0, 0, 0);
                        return;
                    default:
                        return;
                }
            }

            if (storageContainer is MixingTable)
            {
                baseLock.transform.localPosition = new Vector3(-0.27f, 0.685f, 0.38f);
                baseLock.transform.localRotation = Quaternion.Euler(0, 90, 0);
                return;
            }

            if (storageContainer is ChickenCoop)
            {
                baseLock.transform.localPosition = new Vector3(-1.52f, 0.8f, -0.95f);
                baseLock.transform.localRotation = Quaternion.Euler(0, 0, 0);
                return;
            }

            if (storageContainer is Beehive)
            {
                baseLock.transform.localPosition = new Vector3(0f, 0.8f, 0.25f);
                baseLock.transform.localRotation = Quaternion.Euler(0, 90, 0);
                return;
            }

            if (storageContainer is BaseOven)
            {
                if (storageContainer.ShortPrefabName.Equals("furnace"))
                {
                    baseLock.transform.localPosition = new Vector3(-0.035f, 0.375f, 0.45f);
                    baseLock.transform.localRotation = Quaternion.Euler(0, 90, 0);
                    return;
                }

                if (storageContainer.ShortPrefabName.Equals("furnace.large"))
                {
                    baseLock.transform.localPosition = new Vector3(-0.93f, 0.56f, 0.93f);
                    baseLock.transform.localRotation = Quaternion.Euler(0, 45, 0);
                    return;
                }

                if (storageContainer.ShortPrefabName.Equals("refinery_small_deployed"))
                {
                    baseLock.transform.localPosition = new Vector3(-0.01f, 1.25f, -0.6f);
                    baseLock.transform.localRotation = Quaternion.Euler(0, 90, 0);
                    return;
                }

                if (storageContainer.ShortPrefabName.Equals("bbq.deployed"))
                {
                    baseLock.transform.localPosition = new Vector3(0.25f, 0.545f, 0f);
                    baseLock.transform.localRotation = Quaternion.identity;
                }
            }
        }

        #endregion

        #region Functions

        internal static ShareType GetShareTypeFromEntity(BaseEntity entity)
        {
            if (entity is Door)
                return ShareType.Door;
            if (entity is BoxStorage)
                return ShareType.Box;
            if (entity is Locker)
                return ShareType.Locker;
            if (entity is BuildingPrivlidge)
                return ShareType.Cupboard;
            if (entity is Composter)
                return ShareType.Composter;
            if (entity is DropBox)
                return ShareType.Dropbox;
            if (entity is VendingMachine)
                return ShareType.VendingMachine;

            if (entity is BaseOven)
            {
                if (entity.ShortPrefabName.Equals("furnace") || entity.ShortPrefabName.Equals("furnace.large"))
                    return ShareType.Furnace;
                if (entity.ShortPrefabName.Equals("refinery_small_deployed"))
                    return ShareType.Refinery;
                if (entity.ShortPrefabName.Equals("bbq.deployed"))
                    return ShareType.Bbq;
            }

            if (entity is PlanterBox)
                return ShareType.Planters;
            if (entity is HitchTrough)
                return ShareType.Hitch;
            if (entity is MixingTable)
                return ShareType.MixingTable;
            if (entity is ChickenCoop)
                return ShareType.ChickenCoop;
            if (entity is Beehive)
                return ShareType.Beehive;

            return ShareType.None;
        }

        private void PurgeOldData()
        {
            List<ulong> purgeList = Facepunch.Pool.Get<List<ulong>>();
            int currentTimeStamp = UnixTimeStampUtc();

            foreach (KeyValuePair<ulong, StoredData.PlayerData> kvp in storedData.playerData)
            {
                if (currentTimeStamp - kvp.Value.lastOnline > (Configuration.Data.PurgeAfter * 86400))
                    purgeList.Add(kvp.Key);
            }

            for (int i = 0; i < purgeList.Count; i++)
                storedData.playerData.Remove(purgeList[i]);

            Facepunch.Pool.FreeUnmanaged(ref purgeList);
        }

        private void FindRegisterEntities()
        {
            TemporaryShareData temporaryShareData = TemporaryShares.Load();
            List<ulong> tempShareData = null;

            foreach (BaseNetworkable baseNetworkable in BaseNetworkable.serverEntities)
            {
                if (baseNetworkable is BuildingPrivlidge cupboard)
                {
                    if (temporaryShareData.temporaryCupboardShares.TryGetValue(baseNetworkable.net.ID.Value, out tempShareData))
                        cupboard.authorizedPlayers.RemoveWhere(userId => tempShareData.Contains(userId));

                    PlayerEntities.GetOrCreate(cupboard.OwnerID)?.AddEntity(cupboard, true);
                }
                else if (baseNetworkable is AutoTurret turret)
                {
                    if (temporaryShareData.temporaryTurretShares.TryGetValue(baseNetworkable.net.ID.Value, out tempShareData))
                        turret.authorizedPlayers.RemoveWhere(userId => tempShareData.Contains(userId));

                    PlayerEntities.GetOrCreate(turret.OwnerID)?.AddEntity(turret, true);
                }
                else if (baseNetworkable is CodeLock codeLock)
                {
                    if (temporaryShareData.temporaryCodeLockShare.TryGetValue(baseNetworkable.net.ID.Value, out tempShareData))
                        codeLock.guestPlayers.RemoveAll(playerId => tempShareData.Contains(playerId));

                    PlayerEntities.GetOrCreate(codeLock.OwnerID)?.AddEntity(codeLock, true);
                }
            }
        }

        private bool CanUseLockedObject(BasePlayer player, BaseEntity entity)
        {
            if (!entity)
                return false;

            ShareType shareType = GetShareTypeFromEntity(entity.GetParentEntity());
            if (!CanShare(shareType))
                return false;

            StoredData.PlayerData data = storedData.FindPlayerData(entity.OwnerID);
            if (data == null)
                return false;

            ulong userId = player.GetUserId();
            if (CanShare(TeamType.Clan, entity.OwnerID) && data.IsSharing(TeamType.Clan, shareType) && AreClanMates(entity.OwnerID, userId))
                return true;
            if (CanShare(TeamType.Friend, entity.OwnerID) && data.IsSharing(TeamType.Friend, shareType) && AreFriends(entity.OwnerID, userId))
                return true;
            if (CanShare(TeamType.Team, entity.OwnerID) && data.IsSharing(TeamType.Team, shareType) && AreTeamMates(entity.OwnerID, userId))
                return true;

            return false;
        }

        private bool CanUseTurret(BasePlayer player, BaseEntity entity)
        {
            if (!player || !entity)
                return false;

            StoredData.PlayerData data = storedData.FindPlayerData(entity.OwnerID);
            if (data == null)
                return false;

            if (!CanShare(ShareType.Turret) && !Configuration.Security.TurretShareOverride)
                return false;

            ulong userId = player.GetUserId();
            if (CanShare(TeamType.Clan, entity.OwnerID) && (data.IsSharing(TeamType.Clan, ShareType.Turret) || Configuration.Security.TurretShareOverride) && AreClanMates(entity.OwnerID, userId))
                return true;
            if (CanShare(TeamType.Friend, entity.OwnerID) && (data.IsSharing(TeamType.Friend, ShareType.Turret) || Configuration.Security.TurretShareOverride) && AreFriends(entity.OwnerID, userId))
                return true;
            if (CanShare(TeamType.Team, entity.OwnerID) && (data.IsSharing(TeamType.Team, ShareType.Turret) || Configuration.Security.TurretShareOverride) && AreTeamMates(entity.OwnerID, userId))
                return true;

            return false;
        }

        private bool CanUseSamSite(SamSite samSite, BaseCombatEntity baseCombatEntity)
        {
            if (!samSite || !baseCombatEntity)
                return false;

            if (!CanShare(ShareType.Turret) && !Configuration.Security.TurretShareOverride)
                return false;

            StoredData.PlayerData data = storedData.FindPlayerData(samSite.OwnerID);
            if (data == null)
                return false;

            bool canShareClan = CanShare(TeamType.Clan, samSite.OwnerID) && (data.IsSharing(TeamType.Clan, ShareType.Turret) || Configuration.Security.TurretShareOverride);
            bool canShareFriend = CanShare(TeamType.Friend, samSite.OwnerID) && (data.IsSharing(TeamType.Friend, ShareType.Turret) || Configuration.Security.TurretShareOverride);
            bool canShareTeam = CanShare(TeamType.Team, samSite.OwnerID) && (data.IsSharing(TeamType.Team, ShareType.Turret) || Configuration.Security.TurretShareOverride);

            bool shouldIgnore = false;

            if (baseCombatEntity is HotAirBalloon hotAirBalloon)
            {
                for (int i = 0; i < hotAirBalloon.children.Count; i++)
                {
                    if (hotAirBalloon.children[i] is not BasePlayer player)
                        continue;
                    if (IsSamFriendly(samSite.OwnerID, player.GetUserId(), canShareClan, canShareFriend, canShareTeam))
                    {
                        shouldIgnore = true;
                        break;
                    }
                }
            }

            if (baseCombatEntity is BaseVehicle baseVehicle)
            {
                for (int i = 0; i < baseVehicle.mountPoints.Count; i++)
                {
                    var mountPoint = baseVehicle.mountPoints[i];
                    if (!mountPoint.mountable || mountPoint.mountable.GetMounted() is not BasePlayer player)
                        continue;
                    if (IsSamFriendly(samSite.OwnerID, player.GetUserId(), canShareClan, canShareFriend, canShareTeam))
                    {
                        shouldIgnore = true;
                        break;
                    }
                }
            }

            return shouldIgnore;
        }

        private static bool IsSamFriendly(ulong ownerId, ulong playerId, bool canShareClan, bool canShareFriend, bool canShareTeam)
        {
            if (playerId == ownerId)
                return true;
            if (canShareClan && AreClanMates(ownerId, playerId))
                return true;
            if (canShareTeam && AreTeamMates(ownerId, playerId))
                return true;
            return canShareFriend && AreFriends(ownerId, playerId);
        }

        #endregion

        #region Helpers

        internal static bool CanShare(TeamType teamType, ulong playerId)
        {
            switch (teamType)
            {
                case TeamType.Clan:
                    return SocialBridges.Clans.IsAvailable && Configuration.Sharing.Clan.Enabled
                           && (!Configuration.Permission.ClanShare.Enabled || playerId.HasPermission(Configuration.Permission.ClanShare.Permission));
                case TeamType.Friend:
                    return SocialBridges.Friends.IsLoaded && Configuration.Sharing.Friend.Enabled
                           && (!Configuration.Permission.FriendShare.Enabled || playerId.HasPermission(Configuration.Permission.FriendShare.Permission));
                case TeamType.Team:
                    return RelationshipManager.maxTeamSize > 0 && Configuration.Sharing.Team.Enabled
                           && (!Configuration.Permission.TeamShare.Enabled || playerId.HasPermission(Configuration.Permission.TeamShare.Permission));
            }

            return false;
        }

        internal static bool CanShare(ShareType shareType) => AllowedShareTypes != null && AllowedShareTypes.Contains(shareType);

        private bool InAdminMode(BasePlayer player) => PlayerPrivilege.IsAdmin(player);

        internal static bool AreClanMates(ulong owner, ulong player)
        {
            if (Configuration.Sharing.Clan.Alliances)
                return SocialBridges.Clans.IsMemberOrAlly(owner, player);

            return SocialBridges.Clans.IsClanMember(owner, player);
        }

        internal static bool AreTeamMates(ulong owner, ulong player)
            => RelationshipManager.ServerInstance?.FindPlayersTeam(owner)?.members?.Contains(player) ?? false;

        internal static bool AreFriends(ulong owner, ulong player) => SocialBridges.Friends.HasFriend(owner, player);

        internal static int UnixTimeStampUtc() => (int)(System.DateTime.UtcNow - Epoch).TotalSeconds;

        private string GetString(string key, BasePlayer player)
            => DynamicCupShareHost.Instance?.Lang.GetMessage(key, player?.UserIDString) ?? key;

        private void Message(BasePlayer player, string key, params object[] args)
        {
            if (player == null) return;
            string msg = GetString(key, player);
            if (args != null && args.Length > 0)
                msg = string.Format(msg, args);
            player.ChatMessage(msg);
        }

        private void RegisterLangMessages()
        {
            DynamicCupShareHost.Instance?.Lang.RegisterMessages(Messages);
        }

        #endregion

        #region Flags

        internal enum TeamType { Clan, Friend, Team }

        [System.Flags]
        internal enum ShareType
        {
            None = 0,
            Cupboard = 1 << 0,
            Door = 1 << 1,
            Box = 1 << 2,
            Locker = 1 << 3,
            Turret = 1 << 4,
            Furnace = 1 << 5,
            Bbq = 1 << 6,
            Refinery = 1 << 7,
            Composter = 1 << 8,
            Planters = 1 << 9,
            Dropbox = 1 << 10,
            VendingMachine = 1 << 11,
            MixingTable = 1 << 12,
            Hitch = 1 << 13,
            Beehive = 1 << 14,
            ChickenCoop = 1 << 15,
            Blueprint = 1 << 16,
        }

        #endregion
    }
}
