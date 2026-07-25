using System;
using System.Collections.Generic;
using Oxide.Ext.Chaos.UIFramework;
using UnityEngine;

namespace TeleportGUI
{
    /// <summary>
    /// Disconnect / save / wipe / sleeping-bag home lifecycle handlers.
    /// Called from Harmony patches under Patches/.
    /// </summary>
    public partial class TeleportGUIMod
    {
        private const string RequestPopupPanel = "teleportrequest.ui.popup";

        /// <summary>Cancel outgoing/incoming TPR and delayed TP; destroy CUI on disconnect.</summary>
        public void OnPlayerDisconnected(BasePlayer player)
        {
            if (player == null) return;

            ulong id = player.userID;

            if (_outgoingRequests.TryGetValue(id, out var outgoing))
            {
                ClearRequest(outgoing, refund: true);
                if (outgoing.To != null && outgoing.To.IsConnected)
                    SendMessage(outgoing.To, (outgoing.From != null ? outgoing.From.displayName : "A player") + " disconnected. Teleport request cancelled.");
            }

            if (_incomingRequests.TryGetValue(id, out var incoming))
            {
                ClearRequest(incoming, refund: true);
                if (incoming.From != null && incoming.From.IsConnected)
                    SendMessage(incoming.From, (incoming.To != null ? incoming.To.displayName : "A player") + " disconnected. Teleport request cancelled.");
            }

            if (_playersInDelayedTeleport.ContainsKey(id))
                _cancelTeleportRequested.Add(id);

            DestroyTeleportUI(player);
            ChaosUI.Destroy(player, RequestPopupPanel);
            _uiState.Remove(id);
            _showingModal.Remove(id);
            _pendingWarpPosition.Remove(id);
        }

        /// <summary>Oxide OnServerSave — persist userdata / warpdata.</summary>
        public void OnServerSave()
        {
            SaveData();
        }

        /// <summary>
        /// Called after ServerMgr.Initialize when the world was not loaded from a save
        /// (fresh wipe / procedural start). Optionally clears homes then re-validates entity links.
        /// </summary>
        public void OnNewServerSave()
        {
            if (_data?.Users == null) return;

            if (_config?.Home?.WipeHomesOnNewServerSave == true)
                WipeHomesData();

            AssignHomeEntities();
        }

        /// <summary>Clear all users' Homes and reset HomeUsage; preserve locations / TP+warp usage / warps.</summary>
        internal void WipeHomesData()
        {
            if (_data?.Users == null) return;

            foreach (var user in _data.Users.Values)
            {
                if (user == null) continue;
                user.Homes?.Clear();
                user.HomeUsage ??= new TeleportGUIData.UserData.Usage();
                user.HomeUsage.Reset();
            }

            SaveData();
            UnityEngine.Debug.Log("[TeleportGUI] Homes wiped (WipeHomesOnNewServerSave).");
        }

        /// <summary>Drop entity-linked homes whose entities no longer exist.</summary>
        internal void AssignHomeEntities()
        {
            if (_data?.Users == null) return;

            var removeKeys = new List<string>();
            foreach (var user in _data.Users.Values)
            {
                if (user?.Homes == null || user.Homes.Count == 0) continue;
                removeKeys.Clear();

                foreach (var kvp in user.Homes)
                {
                    if (kvp.Value == null || kvp.Value.EntityID == 0UL)
                        continue;

                    if (!TryResolveHomeEntity(kvp.Value.EntityID, out _))
                        removeKeys.Add(kvp.Key);
                }

                for (int i = 0; i < removeKeys.Count; i++)
                    user.Homes.Remove(removeKeys[i]);
            }
        }

        /// <summary>
        /// SleepingBag.OnPlaced — create entity-linked home for bag / bed / beach towel per config.
        /// Preferred over BedMade to avoid duplicate creation.
        /// </summary>
        public void OnSleepingBagPlaced(SleepingBag sleepingBag, BasePlayer player)
        {
            if (sleepingBag == null || sleepingBag.IsDestroyed || sleepingBag.net == null || player == null)
                return;

            if (!ShouldCreateHomeForBag(sleepingBag))
                return;

            if (_config?.Home?.SleepingBags?.OnlyCreateInBuilding == true && sleepingBag.IsOutside())
                return;

            if (TeleportGUIIntegrations.ZoneManager.IsLoaded &&
                TeleportGUIIntegrations.ZoneManager.PlayerHasFlag(player, "notp"))
            {
                SendLang(player, "Home.Error.NoTPZone");
                return;
            }

            var userData = GetOrCreateUser(player);
            ulong entityId = sleepingBag.net.ID.Value;

            if (HasHomeForEntity(userData, entityId))
                return;

            if (HasMaximumHomes(player, userData))
            {
                SendLang(player, "Home.Error.LimitReached");
                return;
            }

            string baseName = string.IsNullOrEmpty(sleepingBag.niceName) ? "Unnamed Sleeping Bag" : sleepingBag.niceName;
            string newName = userData.Homes.ContainsKey(baseName)
                ? GetUniqueBagName(userData, baseName)
                : baseName;

            userData.Homes[newName] = CreateEntityHomePoint(sleepingBag);

            int maxHomes = GetMaxHomesForPlayer(player);
            if (maxHomes == 0)
                SendLang(player, "Home.Success.Created.Bed", newName);
            else
                SendLang(player, "Home.Success.Created.Bed.Remaining", newName, maxHomes - userData.Homes.Count);
        }

        /// <summary>SleepingBag.Rename postfix — rename the linked home key to the new niceName (unique).</summary>
        public void OnSleepingBagRenamed(SleepingBag sleepingBag)
        {
            if (sleepingBag == null || sleepingBag.IsDestroyed || sleepingBag.net == null)
                return;

            ulong ownerId = sleepingBag.OwnerID != 0UL ? sleepingBag.OwnerID : sleepingBag.deployerUserID;
            if (ownerId == 0UL || _data?.Users == null)
                return;

            if (!_data.Users.TryGetValue(ownerId, out var userData) || userData?.Homes == null)
                return;

            ulong entityId = sleepingBag.net.ID.Value;
            string homeName = null;
            TeleportGUIData.UserData.HomePoint homePoint = null;

            foreach (var kvp in userData.Homes)
            {
                if (kvp.Value != null && kvp.Value.EntityID == entityId)
                {
                    homeName = kvp.Key;
                    homePoint = kvp.Value;
                    break;
                }
            }

            if (string.IsNullOrEmpty(homeName) || homePoint == null)
                return;

            // Defer one tick so Rename's network update / filter settle (Oxide CanRenameBed NextTick).
            NextTick(() =>
            {
                if (sleepingBag == null || sleepingBag.IsDestroyed || userData?.Homes == null)
                    return;

                string nice = string.IsNullOrEmpty(sleepingBag.niceName) ? "Unnamed Sleeping Bag" : sleepingBag.niceName;
                string newName = userData.Homes.ContainsKey(nice) &&
                                 !string.Equals(homeName, nice, StringComparison.OrdinalIgnoreCase)
                    ? GetUniqueBagName(userData, nice)
                    : nice;

                if (string.Equals(homeName, newName, StringComparison.Ordinal))
                    return;

                userData.Homes.Remove(homeName);
                userData.Homes[newName] = homePoint;
            });
        }

        /// <summary>SleepingBag killed/destroyed — remove matching entity-linked home.</summary>
        public void OnSleepingBagDestroyed(SleepingBag sleepingBag)
        {
            if (sleepingBag == null || sleepingBag.net == null)
                return;

            var bags = _config?.Home?.SleepingBags;
            if (bags == null) return;
            if (!bags.CreateHomeOnBagPlacement && !bags.CreateHomeOnBedPlacement && !bags.CreateHomeOnBeachTowelPlacement)
                return;

            ulong entityId = sleepingBag.net.ID.Value;
            ulong ownerId = sleepingBag.OwnerID != 0UL ? sleepingBag.OwnerID : sleepingBag.deployerUserID;
            if (ownerId == 0UL || _data?.Users == null)
                return;

            if (!_data.Users.TryGetValue(ownerId, out var userData) || userData?.Homes == null)
                return;

            string removeKey = null;
            foreach (var kvp in userData.Homes)
            {
                if (kvp.Value != null && kvp.Value.EntityID == entityId)
                {
                    removeKey = kvp.Key;
                    break;
                }
            }

            if (removeKey == null) return;

            userData.Homes.Remove(removeKey);

            BasePlayer player = BasePlayer.FindByID(ownerId);
            if (player != null && player.IsConnected)
                SendLang(player, "Notification.BedHomeDestroyed", removeKey);
        }

        internal bool ShouldCreateHomeForBag(SleepingBag sleepingBag)
        {
            var bags = _config?.Home?.SleepingBags;
            if (bags == null || sleepingBag == null) return false;

            string prefab = sleepingBag.ShortPrefabName ?? string.Empty;
            if (prefab == "sleepingbag_leather_deployed")
                return bags.CreateHomeOnBagPlacement;
            if (prefab == "bed_deployed")
                return bags.CreateHomeOnBedPlacement;
            if (prefab == "beachtowel.deployed")
                return bags.CreateHomeOnBeachTowelPlacement;
            return false;
        }

        internal int GetMaxHomesForPlayer(BasePlayer player)
        {
            if (AdminsBypassLimits && player != null && player.IsAdmin)
                return 0;

            int max = GetMaxHomes(player);
            // Default 0 in config disables limits entirely (Oxide GetMaxHomesForPlayer).
            if ((_config?.Home?.MaxHomes?.Default ?? 5) <= 0)
                return 0;
            return max;
        }

        internal bool HasMaximumHomes(BasePlayer player, TeleportGUIData.UserData userData)
        {
            int maxHomes = GetMaxHomesForPlayer(player);
            if (maxHomes == 0)
                return false;
            int count = userData?.Homes?.Count ?? 0;
            return count >= maxHomes;
        }

        internal static string GetUniqueBagName(TeleportGUIData.UserData userData, string bedName)
        {
            bedName ??= "home";
            // Unity Random.Range(int,int) max is exclusive → 1000..9998
            int random = UnityEngine.Random.Range(1000, 9999);
            string candidate = bedName + " " + random;
            if (userData?.Homes != null && userData.Homes.ContainsKey(candidate))
                return GetUniqueBagName(userData, bedName);
            return candidate;
        }

        internal static TeleportGUIData.UserData.HomePoint CreateEntityHomePoint(SleepingBag sleepingBag)
        {
            return new TeleportGUIData.UserData.HomePoint
            {
                Position = default,
                Offset = Vector3.zero,
                EntityID = sleepingBag.net.ID.Value
            };
        }

        internal static bool HasHomeForEntity(TeleportGUIData.UserData userData, ulong entityId)
        {
            if (userData?.Homes == null || entityId == 0UL) return false;
            foreach (var home in userData.Homes.Values)
            {
                if (home != null && home.EntityID == entityId)
                    return true;
            }
            return false;
        }

        internal static bool TryResolveHomeEntity(ulong entityId, out BaseEntity entity)
        {
            entity = null;
            if (entityId == 0UL || BaseNetworkable.serverEntities == null)
                return false;

            entity = BaseNetworkable.serverEntities.Find(new NetworkableId(entityId)) as BaseEntity;
            return entity != null && !entity.IsDestroyed;
        }

        private void NextTick(Action action)
        {
            if (action == null) return;
            var mgr = ServerMgr.Instance;
            if (mgr != null)
                mgr.Invoke(() => action(), 0f);
            else
                action();
        }
    }
}
