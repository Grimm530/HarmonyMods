using System;
using System.Collections.Generic;
using Facepunch;
using UnityEngine;

namespace DynamicCupShareHarmony
{
    public partial class DynamicCupSharePlugin
    {
        private const float BlueprintShareTargetCacheDuration = 10f;
        private const string ResearchSuccessEffect = "assets/prefabs/deployable/research table/effects/research-success.prefab";

        private readonly Dictionary<ulong, List<ulong>> _blueprintShareTargetCache = new Dictionary<ulong, List<ulong>>();
        private readonly Dictionary<ulong, Action> _blueprintShareCacheExpiry = new Dictionary<ulong, Action>();
        private readonly List<Action> _blueprintPendingInvokes = new List<Action>();
        private readonly HashSet<ulong> _techTreeUnlockPlayers = new HashSet<ulong>();
        private readonly Dictionary<ulong, List<ItemDefinition>> _techTreeUnlockItems = new Dictionary<ulong, List<ItemDefinition>>();

        private enum BlueprintRelation { Team, Clan, Friend }

        private static class BlueprintListPool
        {
            private static readonly Stack<List<int>> Pool = new Stack<List<int>>();

            public static List<int> Get() => Pool.Count > 0 ? Pool.Pop() : new List<int>();

            public static void Free(List<int> list)
            {
                if (list == null) return;
                list.Clear();
                Pool.Push(list);
            }
        }

        private sealed class BlueprintUnlockTask
        {
            public ulong TargetId;
            public List<int> Blueprints = BlueprintListPool.Get();
        }

        internal void CancelBlueprintShareTimers()
        {
            if (ServerMgr.Instance != null)
            {
                foreach (var kv in _blueprintShareCacheExpiry)
                    ServerMgr.Instance.CancelInvoke(kv.Value);
                for (int i = 0; i < _blueprintPendingInvokes.Count; i++)
                    ServerMgr.Instance.CancelInvoke(_blueprintPendingInvokes[i]);
            }

            _blueprintShareCacheExpiry.Clear();
            _blueprintShareTargetCache.Clear();
            _blueprintPendingInvokes.Clear();
            _techTreeUnlockPlayers.Clear();
            _techTreeUnlockItems.Clear();
        }

        private bool BlueprintSharingAllowed()
            => Configuration?.Blueprints != null && CanShare(ShareType.Blueprint);

        private bool HasBlueprintUsePermission(BasePlayer player)
        {
            if (player == null) return false;
            string perm = Configuration?.Permission?.BlueprintUse;
            if (string.IsNullOrEmpty(perm)) return true;
            return player.HasPermission(perm);
        }

        private bool HasBlueprintPermission(BasePlayer player, string perm)
        {
            if (player == null) return false;
            if (string.IsNullOrEmpty(perm)) return true;
            return player.HasPermission(perm);
        }

        private bool IsBlueprintSharing(ulong playerId, TeamType teamType)
        {
            if (!CanShare(teamType, playerId))
                return false;
            StoredData.PlayerData data = storedData?.SetupPlayer(playerId);
            return data != null && data.IsSharing(teamType, ShareType.Blueprint);
        }

        private bool BlueprintBlocked(ItemDefinition item)
        {
            if (item == null) return true;
            HashSet<string> blocked = Configuration?.Blueprints?.BlockedItems;
            return blocked != null && blocked.Contains(item.shortname);
        }

        internal bool TryShareBlueprint(ItemDefinition item, BasePlayer player)
        {
            if (!BlueprintSharingAllowed() || item == null || player == null)
                return false;
            if (!HasBlueprintUsePermission(player))
                return false;

            if (BlueprintBlocked(item))
            {
                MessageBlueprint(player, "BP.BlueprintBlocked", true, item.displayName.translated);
                return false;
            }

            ulong playerId = player.GetUserId();
            if (!HasBlueprintSocialConnections(playerId))
                return false;
            if (!SomeoneWillLearnBlueprint(playerId, item))
                return false;

            List<ulong> targetIds = GetCachedShareTargets(playerId);
            ShareBlueprint(player, targetIds, item);
            return true;
        }

        internal void BeginTechTreeUnlock(BasePlayer player)
        {
            if (player == null) return;
            ulong id = player.GetUserId();
            _techTreeUnlockPlayers.Add(id);
            if (!_techTreeUnlockItems.TryGetValue(id, out List<ItemDefinition> list) || list == null)
            {
                list = new List<ItemDefinition>();
                _techTreeUnlockItems[id] = list;
            }
            else
                list.Clear();
        }

        internal void NoteTechTreeUnlock(BasePlayer player, ItemDefinition item)
        {
            if (player == null || item == null) return;
            ulong id = player.GetUserId();
            if (!_techTreeUnlockPlayers.Contains(id)) return;
            if (!_techTreeUnlockItems.TryGetValue(id, out List<ItemDefinition> list) || list == null)
                return;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == item)
                    return;
            }
            list.Add(item);
        }

        internal void FinishTechTreeUnlock(BasePlayer player)
        {
            if (player == null) return;
            ulong id = player.GetUserId();
            _techTreeUnlockPlayers.Remove(id);
            if (!_techTreeUnlockItems.TryGetValue(id, out List<ItemDefinition> list) || list == null)
                return;

            _techTreeUnlockItems.Remove(id);
            if (Configuration?.Blueprints == null || !Configuration.Blueprints.TechTreeSharingEnabled)
                return;

            for (int i = 0; i < list.Count; i++)
                TryShareBlueprint(list[i], player);
        }

        internal void OnStudiedBlueprint(BasePlayer player, ItemDefinition item)
        {
            if (Configuration?.Blueprints == null || !Configuration.Blueprints.ItemSharingEnabled)
                return;
            TryShareBlueprint(item, player);
        }

        private bool QueueBlueprintUnlock(ulong playerId, ulong sharerId, int blueprintId, List<int> unlockQueue)
        {
            if (ServerMgr.Instance?.persistance == null) return false;
            var playerInfo = ServerMgr.Instance.persistance.GetPlayerInfo(playerId);
            if (playerInfo?.unlockedItems == null) return false;
            if (playerInfo.unlockedItems.Contains(blueprintId)) return false;

            unlockQueue.Add(blueprintId);
            BlueprintDebug($"Added {GetItemNameById(blueprintId)} to unlock queue for {GetBlueprintPlayerName(playerId)}");

            if (Configuration.Blueprints.LoseBlueprintsOnLeave)
                AddBlueprintToDatabase(playerId, sharerId, blueprintId);

            return true;
        }

        private int ProcessQueuedBlueprintUnlocks(ulong playerId, List<int> unlockQueue)
        {
            if (unlockQueue == null || unlockQueue.Count == 0) return 0;
            var persistance = ServerMgr.Instance?.persistance;
            if (persistance == null) return 0;

            var playerInfo = persistance.GetPlayerInfo(playerId);
            if (playerInfo?.unlockedItems == null) return 0;

            playerInfo.unlockedItems.AddRange(unlockQueue);
            persistance.SetPlayerInfo(playerId, playerInfo);

            BasePlayer player = FindBlueprintPlayer(playerId);
            if (player != null)
            {
                for (int i = 0; i < unlockQueue.Count; i++)
                {
                    int blueprint = unlockQueue[i];
                    if (player.PersistantPlayerInfo?.unlockedItems != null
                        && !player.PersistantPlayerInfo.unlockedItems.Contains(blueprint))
                        player.PersistantPlayerInfo.unlockedItems.Add(blueprint);

                    player.ClientRPC(RpcTarget.Player("UnlockedBlueprint", player), blueprint);
                }

                player.stats.Add("blueprint_studied", unlockQueue.Count);
                player.SendNetworkUpdateImmediate();
                PlayResearchSuccessEffect(player);
            }

            BlueprintDebug($"Unlocked {unlockQueue.Count} blueprint(s) for {GetBlueprintPlayerName(playerId)}");
            return unlockQueue.Count;
        }

        private void ShareBlueprint(BasePlayer sharer, List<ulong> targetIds, ItemDefinition item)
        {
            if (sharer == null || item == null || targetIds == null) return;

            int blueprintId = item.itemid;
            int sharedCount = 0;
            var tasks = new List<BlueprintUnlockTask>();

            BlueprintDebug($"{sharer.displayName} is sharing {GetItemNameById(blueprintId)} with {targetIds.Count} player(s)");

            for (int i = 0; i < targetIds.Count; i++)
            {
                ulong targetId = targetIds[i];
                if (targetId == sharer.GetUserId()) continue;

                var task = new BlueprintUnlockTask { TargetId = targetId };
                ItemBlueprint bp = item.Blueprint;
                if (bp?.additionalUnlocks != null)
                {
                    for (int a = 0; a < bp.additionalUnlocks.Count; a++)
                    {
                        ItemDefinition extra = bp.additionalUnlocks[a];
                        if (extra != null)
                            QueueBlueprintUnlock(targetId, sharer.GetUserId(), extra.itemid, task.Blueprints);
                    }
                }

                QueueBlueprintUnlock(targetId, sharer.GetUserId(), blueprintId, task.Blueprints);
                sharedCount++;

                if (task.Blueprints.Count > 0)
                    tasks.Add(task);
                else
                    BlueprintListPool.Free(task.Blueprints);
            }

            for (int i = 0; i < tasks.Count; i++)
            {
                BlueprintUnlockTask task = tasks[i];
                ProcessQueuedBlueprintUnlocks(task.TargetId, task.Blueprints);

                BasePlayer target = FindBlueprintPlayer(task.TargetId);
                if (target != null && Configuration.Blueprints.ReceiveMessagesEnabled)
                    MessageBlueprint(target, "BP.TargetLearntBlueprint", true, sharer.displayName, item.displayName.translated);

                BlueprintListPool.Free(task.Blueprints);
            }

            if (sharedCount > 0 && Configuration.Blueprints.ShareMessagesEnabled)
                MessageBlueprint(sharer, "BP.PlayerSharedBlueprint", true, item.displayName.translated, sharedCount);
        }

        private void ShareWithPlayer(BasePlayer sharer, BasePlayer target)
        {
            if (sharer == null || target == null) return;

            ulong playerId = sharer.GetUserId();
            ulong targetId = target.GetUserId();
            BlueprintDebug($"{sharer.displayName} started sharing their blueprints with {target.displayName}");

            if (!TargetAcceptsBlueprintShare(playerId, targetId) && !HasBlueprintPermission(sharer, Configuration.Permission.BlueprintBypass))
            {
                MessageBlueprint(sharer, "BP.TargetSharingDisabled", true, target.displayName);
                return;
            }

            bool related = AreTeamMates(playerId, targetId) || AreClanMates(playerId, targetId)
                           || SocialBridges.Friends.AreMutualFriends(playerId, targetId);
            if (!related && !HasBlueprintPermission(sharer, Configuration.Permission.BlueprintBypass))
            {
                MessageBlueprint(sharer, "BP.CannotShare", true);
                return;
            }

            List<int> filtered = RemoveBlockedBlueprints(sharer.PersistantPlayerInfo?.unlockedItems);
            if (filtered.Count == 0)
            {
                MessageBlueprint(sharer, "BP.NoBlueprintsToShare", true, target.displayName);
                return;
            }

            List<int> queue = BlueprintListPool.Get();
            for (int i = 0; i < filtered.Count; i++)
                QueueBlueprintUnlock(targetId, playerId, filtered[i], queue);

            int unlocked = ProcessQueuedBlueprintUnlocks(targetId, queue);
            BlueprintListPool.Free(queue);

            if (unlocked > 0)
            {
                if (Configuration.Blueprints.ShareMessagesEnabled)
                    MessageBlueprint(sharer, "BP.PlayerSharedBlueprints", true, unlocked, target.displayName);
                if (Configuration.Blueprints.ReceiveMessagesEnabled)
                    MessageBlueprint(target, "BP.TargetLearntBlueprints", true, sharer.displayName, unlocked);
            }
            else
                MessageBlueprint(sharer, "BP.NoBlueprintsToShare", true, target.displayName);
        }

        private bool TargetAcceptsBlueprintShare(ulong sharerId, ulong targetId)
        {
            if (AreTeamMates(sharerId, targetId) && IsBlueprintSharing(targetId, TeamType.Team))
                return true;
            if (AreClanMates(sharerId, targetId) && IsBlueprintSharing(targetId, TeamType.Clan))
                return true;
            if (SocialBridges.Friends.AreMutualFriends(sharerId, targetId) && IsBlueprintSharing(targetId, TeamType.Friend))
                return true;
            return false;
        }

        private bool HasBlueprintSocialConnections(ulong playerId)
        {
            return InBlueprintTeam(playerId) || InBlueprintClan(playerId) || HasBlueprintFriends(playerId);
        }

        private bool InBlueprintTeam(ulong playerId)
        {
            if (!RelationshipManager.TeamsEnabled()) return false;
            var team = RelationshipManager.ServerInstance?.FindPlayersTeam(playerId);
            return team != null && team.members != null && team.members.Count > 1;
        }

        private bool InBlueprintClan(ulong playerId)
            => !string.IsNullOrEmpty(SocialBridges.Clans.GetClanOf(playerId));

        private bool HasBlueprintFriends(ulong playerId)
        {
            ulong[] friends = SocialBridges.Friends.GetFriends(playerId);
            return friends != null && friends.Length > 0;
        }

        private List<int> RemoveBlockedBlueprints(List<int> blueprints)
        {
            var result = new List<int>();
            if (blueprints == null) return result;
            for (int i = 0; i < blueprints.Count; i++)
            {
                ItemDefinition item = ItemManager.FindItemDefinition(blueprints[i]);
                if (item == null || BlueprintBlocked(item)) continue;
                result.Add(item.itemid);
            }
            return result;
        }

        private bool SomeoneWillLearnBlueprint(ulong playerId, ItemDefinition item)
        {
            if (item == null) return false;
            List<ulong> targetIds = GetCachedShareTargets(playerId);
            if (targetIds.Count == 0) return false;

            for (int i = 0; i < targetIds.Count; i++)
            {
                ulong targetId = targetIds[i];
                if (PlayerWouldLearnBlueprint(targetId, item.itemid))
                    return true;

                ItemBlueprint bp = item.Blueprint;
                if (bp?.additionalUnlocks == null) continue;
                for (int a = 0; a < bp.additionalUnlocks.Count; a++)
                {
                    ItemDefinition extra = bp.additionalUnlocks[a];
                    if (extra != null && PlayerWouldLearnBlueprint(targetId, extra.itemid))
                        return true;
                }
            }

            return false;
        }

        private bool PlayerWouldLearnBlueprint(ulong playerId, int blueprintId)
        {
            var playerInfo = ServerMgr.Instance?.persistance?.GetPlayerInfo(playerId);
            if (playerInfo?.unlockedItems == null) return false;
            return !playerInfo.unlockedItems.Contains(blueprintId);
        }

        private List<ulong> GetPlayerIdsToShareWith(ulong playerId)
        {
            var ids = new HashSet<ulong>();
            if (!BlueprintSharingAllowed())
                return new List<ulong>();

            if (IsBlueprintSharing(playerId, TeamType.Clan) && InBlueprintClan(playerId))
            {
                List<ulong> members = Facepunch.Pool.Get<List<ulong>>();
                SocialBridges.Clans.GetMembers(playerId, members);
                for (int i = 0; i < members.Count; i++)
                {
                    ulong memberId = members[i];
                    if (memberId != playerId && IsBlueprintSharing(memberId, TeamType.Clan) && AreClanMates(playerId, memberId))
                        ids.Add(memberId);
                }
                Facepunch.Pool.FreeUnmanaged(ref members);
            }

            if (IsBlueprintSharing(playerId, TeamType.Friend) && HasBlueprintFriends(playerId))
            {
                ulong[] friends = SocialBridges.Friends.GetFriends(playerId);
                if (friends != null)
                {
                    for (int i = 0; i < friends.Length; i++)
                    {
                        ulong friendId = friends[i];
                        if (friendId == playerId) continue;
                        if (!SocialBridges.Friends.AreMutualFriends(playerId, friendId)) continue;
                        if (IsBlueprintSharing(friendId, TeamType.Friend))
                            ids.Add(friendId);
                    }
                }
            }

            if (IsBlueprintSharing(playerId, TeamType.Team) && InBlueprintTeam(playerId))
            {
                var team = RelationshipManager.ServerInstance?.FindPlayersTeam(playerId);
                if (team?.members != null)
                {
                    for (int i = 0; i < team.members.Count; i++)
                    {
                        ulong memberId = team.members[i];
                        if (memberId != playerId && IsBlueprintSharing(memberId, TeamType.Team))
                            ids.Add(memberId);
                    }
                }
            }

            var list = new List<ulong>(ids.Count);
            foreach (ulong id in ids)
                list.Add(id);
            return list;
        }

        private List<ulong> GetCachedShareTargets(ulong playerId)
        {
            if (_blueprintShareTargetCache.TryGetValue(playerId, out List<ulong> cached) && cached != null)
                return cached;

            List<ulong> fresh = GetPlayerIdsToShareWith(playerId);
            _blueprintShareTargetCache[playerId] = fresh;

            if (_blueprintShareCacheExpiry.TryGetValue(playerId, out Action old) && ServerMgr.Instance != null)
                ServerMgr.Instance.CancelInvoke(old);

            Action clear = null;
            clear = () =>
            {
                _blueprintShareTargetCache.Remove(playerId);
                _blueprintShareCacheExpiry.Remove(playerId);
            };
            _blueprintShareCacheExpiry[playerId] = clear;
            ServerMgr.Instance?.Invoke(clear, BlueprintShareTargetCacheDuration);
            return fresh;
        }

        private void AddBlueprintToDatabase(ulong playerId, ulong sharerId, int blueprint)
        {
            StoredData.PlayerData data = storedData?.SetupPlayer(playerId);
            if (data == null) return;
            BlueprintLearnData learnt = data.Blueprints;

            if (AreTeamMates(playerId, sharerId))
                AddUnique(learnt.Team ??= new List<int>(), blueprint);

            if (AreClanMates(playerId, sharerId))
                AddUnique(learnt.Clan ??= new List<int>(), blueprint);

            if (SocialBridges.Friends.HasFriend(sharerId, playerId))
                AddUnique(learnt.FriendList(sharerId.ToString()), blueprint);
        }

        private static void AddUnique(List<int> list, int value)
        {
            if (list == null) return;
            if (!list.Contains(value))
                list.Add(value);
        }

        private List<int> GetSharedBlueprintList(ulong playerId, BlueprintRelation type, string friendId = "")
        {
            StoredData.PlayerData data = storedData?.SetupPlayer(playerId);
            if (data == null) return new List<int>();
            BlueprintLearnData learnt = data.Blueprints;
            switch (type)
            {
                case BlueprintRelation.Team:
                    if (learnt.Team == null) learnt.Team = new List<int>();
                    return learnt.Team;
                case BlueprintRelation.Clan:
                    if (learnt.Clan == null) learnt.Clan = new List<int>();
                    return learnt.Clan;
                case BlueprintRelation.Friend:
                    return learnt.FriendList(friendId);
                default:
                    return new List<int>();
            }
        }

        private void RemoveBlueprintsFromDatabase(BlueprintRelation type, ulong playerId, string friendId)
        {
            List<int> list = GetSharedBlueprintList(playerId, type, friendId);
            list.Clear();
        }

        private void RemoveBlueprints(ulong playerId, List<int> blueprintIds, BlueprintRelation type, string friendId = "")
        {
            if (blueprintIds == null || blueprintIds.Count == 0) return;
            var persistance = ServerMgr.Instance?.persistance;
            if (persistance == null) return;

            var playerInfo = persistance.GetPlayerInfo(playerId);
            if (playerInfo?.unlockedItems == null) return;

            BasePlayer player = FindBlueprintPlayer(playerId);
            int removed = 0;
            for (int i = 0; i < blueprintIds.Count; i++)
            {
                int blueprintId = blueprintIds[i];
                if (!playerInfo.unlockedItems.Contains(blueprintId)) continue;
                playerInfo.unlockedItems.Remove(blueprintId);
                removed++;
                if (player?.PersistantPlayerInfo?.unlockedItems != null
                    && player.PersistantPlayerInfo.unlockedItems.Contains(blueprintId))
                    player.PersistantPlayerInfo.unlockedItems.Remove(blueprintId);
            }

            if (removed == 0) return;

            persistance.SetPlayerInfo(playerId, playerInfo);
            RemoveBlueprintsFromDatabase(type, playerId, friendId);

            if (player != null)
            {
                player.SendNetworkUpdateImmediate();
                MessageBlueprint(player, "BP.BlueprintsRemoved", true, removed);
            }
        }

        internal void OnBlueprintTeamJoined(RelationshipManager.PlayerTeam playerTeam, BasePlayer joiningPlayer)
        {
            if (!BlueprintSharingAllowed() || Configuration.Blueprints == null) return;
            if (!Configuration.Blueprints.ShareToExistingMembers && !Configuration.Blueprints.ShareToNewMembers)
                return;
            if (playerTeam == null || joiningPlayer == null) return;

            Action delayed = null;
            delayed = () =>
            {
                _blueprintPendingInvokes.Remove(delayed);
                ShareBlueprintsBetweenGroup(joiningPlayer, playerTeam.members);
            };
            _blueprintPendingInvokes.Add(delayed);
            if (ServerMgr.Instance != null)
                ServerMgr.Instance.Invoke(delayed, 1f);
            else
                delayed();
        }

        internal void OnBlueprintTeamLeft(ulong playerId)
        {
            if (Configuration?.Blueprints == null || !Configuration.Blueprints.LoseBlueprintsOnLeave)
                return;
            List<int> shared = GetSharedBlueprintList(playerId, BlueprintRelation.Team);
            if (shared.Count == 0) return;
            RemoveBlueprints(playerId, new List<int>(shared), BlueprintRelation.Team);
        }

        internal void OnBlueprintFriendAdded(string playerIdStr, string friendIdStr)
        {
            if (!BlueprintSharingAllowed() || Configuration.Blueprints == null) return;
            if (!Configuration.Blueprints.ShareToExistingMembers && !Configuration.Blueprints.ShareToNewMembers)
                return;
            if (!ulong.TryParse(playerIdStr, out ulong playerId) || !ulong.TryParse(friendIdStr, out ulong friendId))
                return;
            if (!SocialBridges.Friends.AreMutualFriends(playerId, friendId))
                return;

            BasePlayer player = FindBlueprintPlayer(playerId);
            BasePlayer friend = FindBlueprintPlayer(friendId);
            if (player == null || friend == null) return;

            if (Configuration.Blueprints.ShareToExistingMembers)
                ShareWithPlayer(friend, player);
            if (Configuration.Blueprints.ShareToNewMembers)
                ShareWithPlayer(player, friend);
        }

        internal void OnBlueprintFriendRemoved(string playerIdStr, string friendIdStr)
        {
            if (Configuration?.Blueprints == null || !Configuration.Blueprints.LoseBlueprintsOnLeave)
                return;
            if (string.IsNullOrEmpty(playerIdStr) || string.IsNullOrEmpty(friendIdStr))
                return;
            if (!ulong.TryParse(playerIdStr, out ulong playerId) || !ulong.TryParse(friendIdStr, out ulong friendId))
                return;

            if (SocialBridges.Friends.HasFriend(friendId, playerId))
                return;

            List<int> playerToFriend = GetSharedBlueprintList(playerId, BlueprintRelation.Friend, friendIdStr);
            List<int> friendToPlayer = GetSharedBlueprintList(friendId, BlueprintRelation.Friend, playerIdStr);
            if (playerToFriend.Count > 0)
                RemoveBlueprints(playerId, new List<int>(playerToFriend), BlueprintRelation.Friend, friendIdStr);
            if (friendToPlayer.Count > 0)
                RemoveBlueprints(friendId, new List<int>(friendToPlayer), BlueprintRelation.Friend, playerIdStr);
        }

        internal void OnBlueprintClanMemberJoined(ulong playerId)
        {
            if (!BlueprintSharingAllowed() || Configuration.Blueprints == null) return;
            if (!Configuration.Blueprints.ShareToExistingMembers && !Configuration.Blueprints.ShareToNewMembers)
                return;

            BasePlayer player = FindBlueprintPlayer(playerId);
            if (player == null) return;

            List<ulong> members = Facepunch.Pool.Get<List<ulong>>();
            SocialBridges.Clans.GetMembers(playerId, members);
            ShareBlueprintsBetweenGroup(player, members);
            Facepunch.Pool.FreeUnmanaged(ref members);
        }

        internal void OnBlueprintClanMemberGone(ulong playerId)
        {
            if (Configuration?.Blueprints == null || !Configuration.Blueprints.LoseBlueprintsOnLeave)
                return;
            List<int> shared = GetSharedBlueprintList(playerId, BlueprintRelation.Clan);
            if (shared.Count == 0) return;
            RemoveBlueprints(playerId, new List<int>(shared), BlueprintRelation.Clan);
        }

        internal void OnBlueprintClanDisbanded(List<ulong> clanMembers)
        {
            if (Configuration?.Blueprints == null || !Configuration.Blueprints.LoseBlueprintsOnLeave)
                return;
            if (clanMembers == null) return;
            for (int i = 0; i < clanMembers.Count; i++)
                RemoveBlueprintsFromDatabase(BlueprintRelation.Clan, clanMembers[i], string.Empty);
        }

        private void ShareBlueprintsBetweenGroup(BasePlayer joiningPlayer, List<ulong> memberIds)
        {
            if (joiningPlayer == null || memberIds == null) return;
            ulong joiningId = joiningPlayer.GetUserId();

            for (int i = 0; i < memberIds.Count; i++)
            {
                ulong memberId = memberIds[i];
                if (memberId == joiningId) continue;
                BasePlayer member = FindBlueprintPlayer(memberId);
                if (member == null) continue;

                if (Configuration.Blueprints.ShareToExistingMembers)
                    ShareWithPlayer(joiningPlayer, member);
                if (Configuration.Blueprints.ShareToNewMembers)
                    ShareWithPlayer(member, joiningPlayer);
            }
        }

        internal void CmdBlueprintShare(BasePlayer player, string[] args)
        {
            if (player == null) return;
            if (!BlueprintSharingAllowed())
            {
                MessageBlueprint(player, "BP.Disabled", true);
                return;
            }

            if (args == null || args.Length == 0)
            {
                OpenBlueprintCommandsUi(player);
                return;
            }

            string key = args[0].ToLowerInvariant();
            switch (key)
            {
                case "help":
                    OpenBlueprintCommandsUi(player);
                    break;
                case "toggle":
                    CmdBlueprintToggle(player);
                    break;
                case "share":
                    CmdBlueprintShareWith(player, args);
                    break;
                case "show":
                    CmdBlueprintShow(player, args);
                    break;
                default:
                    MessageBlueprint(player, "BP.ArgumentsError", true);
                    break;
            }
        }

        private void OpenBlueprintCommandsUi(BasePlayer player)
        {
            if (player == null) return;
            if (!HasShareUiAccess(player) && !BlueprintSharingAllowed())
            {
                MessageBlueprint(player, "BP.NoPermission", true);
                return;
            }

            StoredData.PlayerData playerData = storedData.SetupPlayer(player.GetUserId());
            OpenShareMenu(player, playerData, 0UL, ShareUiPage.Commands);
        }

        private void CmdBlueprintHelp(BasePlayer player)
            => OpenBlueprintCommandsUi(player);

        private void CmdBlueprintToggle(BasePlayer player)
        {
            if (!HasBlueprintPermission(player, Configuration.Permission.BlueprintToggle))
            {
                MessageBlueprint(player, "BP.NoPermission", true);
                return;
            }

            ulong playerId = player.GetUserId();
            StoredData.PlayerData data = storedData.SetupPlayer(playerId);
            if (data == null) return;

            bool anyOff = false;
            if (CanShare(TeamType.Clan, playerId) && !data.IsSharing(TeamType.Clan, ShareType.Blueprint))
                anyOff = true;
            if (CanShare(TeamType.Friend, playerId) && !data.IsSharing(TeamType.Friend, ShareType.Blueprint))
                anyOff = true;
            if (CanShare(TeamType.Team, playerId) && !data.IsSharing(TeamType.Team, ShareType.Blueprint))
                anyOff = true;

            bool enable = anyOff;
            if (CanShare(TeamType.Clan, playerId))
            {
                if (enable) data.Share(TeamType.Clan, ShareType.Blueprint);
                else data.Unshare(TeamType.Clan, ShareType.Blueprint);
            }
            if (CanShare(TeamType.Friend, playerId))
            {
                if (enable) data.Share(TeamType.Friend, ShareType.Blueprint);
                else data.Unshare(TeamType.Friend, ShareType.Blueprint);
            }
            if (CanShare(TeamType.Team, playerId))
            {
                if (enable) data.Share(TeamType.Team, ShareType.Blueprint);
                else data.Unshare(TeamType.Team, ShareType.Blueprint);
            }

            MessageBlueprint(player, enable ? "BP.ToggleOn" : "BP.ToggleOff", true);
        }

        private void CmdBlueprintShareWith(BasePlayer player, string[] args)
        {
            if (!HasBlueprintPermission(player, Configuration.Permission.BlueprintShare))
            {
                MessageBlueprint(player, "BP.NoPermission", true);
                return;
            }

            if (args == null || args.Length < 2)
            {
                MessageBlueprint(player, "BP.NoTarget", true);
                return;
            }

            if (!TryGetOtherPlayer(player, args[1], out BasePlayer target))
                return;

            ShareWithPlayer(player, target);
        }

        private void CmdBlueprintShow(BasePlayer player, string[] args)
        {
            if (Configuration.Blueprints == null || !Configuration.Blueprints.LoseBlueprintsOnLeave)
            {
                MessageBlueprint(player, "BP.LoseBlueprintsDisabled", true);
                return;
            }

            if (!HasBlueprintPermission(player, Configuration.Permission.BlueprintShow))
            {
                MessageBlueprint(player, "BP.NoPermission", true);
                return;
            }

            if (args == null || args.Length < 2)
            {
                MessageBlueprint(player, "BP.ShowMissingArgument", true);
                return;
            }

            switch (args[1].ToLowerInvariant())
            {
                case "clan":
                    DisplayLearntBlueprints(player, BlueprintRelation.Clan);
                    break;
                case "team":
                    DisplayLearntBlueprints(player, BlueprintRelation.Team);
                    break;
                case "friend":
                    if (args.Length < 3)
                    {
                        MessageBlueprint(player, "BP.ShowFriendArgumentMissing", true);
                        return;
                    }
                    if (!TryGetOtherPlayer(player, args[2], out BasePlayer friend))
                        return;
                    if (!SocialBridges.Friends.HasFriend(friend.GetUserId(), player.GetUserId()))
                    {
                        MessageBlueprint(player, "BP.NotFriends", true);
                        return;
                    }
                    DisplayLearntBlueprints(player, BlueprintRelation.Friend, friend.UserIDString);
                    break;
                default:
                    MessageBlueprint(player, "BP.ShowMissingArgument", true);
                    break;
            }
        }

        private bool TryGetOtherPlayer(BasePlayer sender, string name, out BasePlayer target)
        {
            target = BasePlayer.FindAwakeOrSleeping(name);
            if (target == null)
            {
                MessageBlueprint(sender, "BP.PlayerNotFound", true);
                return false;
            }
            if (target == sender)
            {
                MessageBlueprint(sender, "BP.TargetEqualsPlayer", true);
                return false;
            }
            return true;
        }

        private void DisplayLearntBlueprints(BasePlayer player, BlueprintRelation type, string friendId = "")
        {
            if (player == null) return;
            List<int> shared = GetSharedBlueprintList(player.GetUserId(), type, friendId);
            if (shared == null || shared.Count == 0)
            {
                MessageBlueprint(player, "BP.NoSharedBlueprints", true);
                return;
            }

            var grouped = new SortedDictionary<int, List<string>>();
            for (int i = 0; i < shared.Count; i++)
            {
                ItemDefinition item = ItemManager.FindItemDefinition(shared[i]);
                if (item?.Blueprint == null) continue;
                int tier = item.Blueprint.workbenchLevelRequired;
                if (!grouped.TryGetValue(tier, out List<string> names))
                {
                    names = new List<string>();
                    grouped[tier] = names;
                }
                names.Add(item.displayName.translated);
            }

            MessageBlueprint(player, "BP.SharedBlueprintsTitle", true);
            foreach (var kv in grouped)
            {
                string joined = string.Join(", ", kv.Value);
                string msg = GetString("BP.ShowSharedBlueprints", player);
                player.ChatMessage(string.Format(msg, kv.Key, joined));
            }
        }

        private void PlayResearchSuccessEffect(BasePlayer player)
        {
            if (player == null) return;
            var effect = new Effect(ResearchSuccessEffect, player.transform.position, Vector3.zero);
            EffectNetwork.Send(effect, player.net.connection);
        }

        private static BasePlayer FindBlueprintPlayer(ulong playerId)
            => BasePlayer.FindAwakeOrSleepingByID(playerId);

        private static string GetItemNameById(int itemId)
            => ItemManager.FindItemDefinition(itemId)?.displayName?.translated ?? string.Empty;

        private static string GetBlueprintPlayerName(ulong playerId)
            => FindBlueprintPlayer(playerId)?.displayName ?? playerId.ToString();

        private void MessageBlueprint(BasePlayer player, string key, bool prefix, params object[] args)
        {
            if (player == null) return;
            string msg = GetString(key, player);
            if (args != null && args.Length > 0)
                msg = string.Format(msg, args);
            if (prefix)
                msg = GetString("BP.Prefix", player) + msg;
            player.ChatMessage(msg);
        }

        private void BlueprintDebug(string message)
        {
            if (Configuration?.Blueprints == null || !Configuration.Blueprints.Debug)
                return;
            Debug.Log("[DynamicCupShare Blueprint] " + message);
        }
    }
}
