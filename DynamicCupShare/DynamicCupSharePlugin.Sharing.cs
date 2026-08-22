using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Facepunch;
using Newtonsoft.Json;
using UnityEngine;

namespace DynamicCupShareHarmony
{
    public partial class DynamicCupSharePlugin
    {
        private abstract class SimpleWorkQueue<T> where T : class
        {
            private readonly List<T> _items = new List<T>();
            private int _index;

            public void Add(T item)
            {
                if (item == null || !ShouldAdd(item) || _items.Contains(item))
                    return;
                _items.Add(item);
            }

            public void Remove(T item) => _items.Remove(item);

            public void Clear() => _items.Clear();

            public void RunList(double maxMs)
            {
                int count = _items.Count;
                if (count == 0)
                    return;

                var sw = System.Diagnostics.Stopwatch.StartNew();
                int processed = 0;
                while (processed < count && sw.Elapsed.TotalMilliseconds < maxMs)
                {
                    if (_index >= _items.Count)
                        _index = 0;
                    if (_items.Count == 0)
                        break;

                    T item = _items[_index];
                    _index++;
                    processed++;
                    RunJob(item);
                }
            }

            protected abstract void RunJob(T item);
            protected abstract bool ShouldAdd(T item);
        }

        private class UpdateCycler : MonoBehaviour
        {
            private static UpdateCycler _instance;

            private static readonly UpdateCupboardQueue Queue = new UpdateCupboardQueue();

            private const float QueueRunFrequency = 1f / 10f;

            private float _nextJobRun;

            private void Update()
            {
                _nextJobRun += Time.deltaTime;
                if (_nextJobRun < QueueRunFrequency)
                    return;

                Queue.RunList(0.1);
                _nextJobRun = 0f;
            }

            private void OnDestroy()
            {
                Queue.Clear();
            }

            public static void Enqueue(PlayerPrivilege playerPrivilege)
            {
                if (!_instance)
                    _instance = new GameObject("UpdateCupboardWorker").AddComponent<UpdateCycler>();

                Queue.Add(playerPrivilege);
            }

            public static void Dequeue(PlayerPrivilege playerPrivilege)
            {
                if (!_instance)
                    return;

                Queue.Remove(playerPrivilege);
            }

            public static void OnUnload()
            {
                if (_instance)
                    Destroy(_instance.gameObject);
                _instance = null;
            }

            public class UpdateCupboardQueue : SimpleWorkQueue<PlayerPrivilege>
            {
                protected override void RunJob(PlayerPrivilege playerPrivilege)
                {
                    if (!playerPrivilege)
                        return;

                    playerPrivilege.UpdateNearbyCupboards();
                }

                protected override bool ShouldAdd(PlayerPrivilege playerPrivilege) => playerPrivilege;
            }
        }

        internal class PlayerPrivilege : MonoBehaviour
        {
            private BasePlayer _player;
            private ulong _playerID;
            private BuildingPrivlidge _lastRegisteredTo;
            private bool _inAdminMode;

            public bool InAdminMode
            {
                get => _inAdminMode;
                set
                {
                    if (_inAdminMode && !value)
                    {
                        if (_lastRegisteredTo)
                            TemporaryShares.RemovePlayerFrom(_player, _lastRegisteredTo);

                        _lastRegisteredTo = null;
                    }

                    _inAdminMode = value;
                }
            }

            private static readonly Hash<BasePlayer, PlayerPrivilege> PlayerLookup = new Hash<BasePlayer, PlayerPrivilege>();

            private void Awake()
            {
                _player = GetComponent<BasePlayer>();
                _playerID = _player.GetUserId();
                UpdateCycler.Enqueue(this);
            }

            private void OnDestroy()
            {
                UpdateCycler.Dequeue(this);

                if (InAdminMode && _lastRegisteredTo && !_lastRegisteredTo.IsDestroyed)
                    TemporaryShares.RemovePlayerFrom(_player, _lastRegisteredTo);
            }

            public void UpdateNearbyCupboards()
            {
                if (!_player)
                    return;

                BuildingPrivlidge buildingPrivilege = _player.GetBuildingPrivilege();
                if (!buildingPrivilege || buildingPrivilege == _lastRegisteredTo)
                    return;

                if (buildingPrivilege.IsAuthed(_playerID))
                    return;

                if (InAdminMode)
                {
                    TemporaryShares.RegisterPlayerTo(_player, buildingPrivilege);

                    if (buildingPrivilege != _lastRegisteredTo)
                        TemporaryShares.RemovePlayerFrom(_player, _lastRegisteredTo);

                    _lastRegisteredTo = buildingPrivilege;
                    return;
                }

                if (ShouldRegisterToCupboard(buildingPrivilege))
                {
                    TemporaryShares.RegisterPlayerTo(_player, buildingPrivilege);
                    _lastRegisteredTo = buildingPrivilege;
                }
            }

            private bool ShouldRegisterToCupboard(BuildingPrivlidge buildingPrivilege)
            {
                if (Configuration.Security.PreventShareNoOwner && !buildingPrivilege.IsAuthed(buildingPrivilege.OwnerID))
                    return false;

                StoredData.PlayerData data = storedData.FindPlayerData(buildingPrivilege.OwnerID);
                if (data == null)
                    return false;

                if (!CanShare(ShareType.Cupboard))
                    return false;

                if (CanShare(TeamType.Clan, buildingPrivilege.OwnerID) && data.IsSharing(TeamType.Clan, ShareType.Cupboard) && AreClanMates(buildingPrivilege.OwnerID, _playerID))
                    return true;

                if (CanShare(TeamType.Friend, buildingPrivilege.OwnerID) && data.IsSharing(TeamType.Friend, ShareType.Cupboard) && AreFriends(buildingPrivilege.OwnerID, _playerID))
                    return true;

                if (CanShare(TeamType.Team, buildingPrivilege.OwnerID) && data.IsSharing(TeamType.Team, ShareType.Cupboard) && AreTeamMates(buildingPrivilege.OwnerID, _playerID))
                    return true;

                return false;
            }

            public static bool Find(BasePlayer player, out PlayerPrivilege playerPrivilege) => PlayerLookup.TryGetValue(player, out playerPrivilege);

            public static bool IsAdmin(BasePlayer player)
            {
                if (PlayerLookup.TryGetValue(player, out PlayerPrivilege playerPrivilege) && playerPrivilege)
                    return playerPrivilege.InAdminMode;

                return false;
            }

            public static void AddPlayer(BasePlayer player)
            {
                if (PlayerLookup.TryGetValue(player, out PlayerPrivilege playerPrivilege) && playerPrivilege)
                    return;

                playerPrivilege = player.gameObject.AddComponent<PlayerPrivilege>();
                playerPrivilege.InAdminMode = Configuration.Permission.ToggleAdminPermissionOnJoin && player.HasPermission(Configuration.Permission.AdminPermission);
                PlayerLookup[player] = playerPrivilege;
            }

            public static void RemovePlayer(BasePlayer player)
            {
                if (!PlayerLookup.TryGetValue(player, out PlayerPrivilege playerPrivilege))
                    return;

                PlayerLookup.Remove(player);
                Destroy(playerPrivilege);
            }

            public static void OnUnload()
            {
                List<PlayerPrivilege> components = new List<PlayerPrivilege>(PlayerLookup.Values);
                for (int i = components.Count - 1; i >= 0; i--)
                {
                    PlayerPrivilege admin = components[i];
                    if (admin)
                        Destroy(admin);
                }

                PlayerLookup.Clear();
            }
        }

        private class AuthorizationQueue : MonoBehaviour
        {
            private readonly Queue<IEnumerator> _authorizationQueue = new Queue<IEnumerator>();
            private bool _queueRunning;
            private Coroutine _current;
            private static AuthorizationQueue _instance;

            private void Awake()
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
            }

            protected void OnDestroy()
            {
                _authorizationQueue.Clear();
                if (_current != null)
                    StopCoroutine(_current);
                _instance = null;
            }

            public static void Enqueue(IEnumerator enumerator)
            {
                if (!_instance)
                    _instance = new GameObject("DCS_AuthorizationQueue").AddComponent<AuthorizationQueue>();

                _instance._authorizationQueue.Enqueue(enumerator);

                if (!_instance._queueRunning)
                    _instance.StartProcessingQueue();
            }

            public static void OnUnload()
            {
                if (_instance)
                    Destroy(_instance.gameObject);
            }

            private void StartProcessingQueue()
            {
                _current = StartCoroutine(RunQueue());
            }

            private IEnumerator RunQueue()
            {
                _queueRunning = true;

                while (_authorizationQueue.Count > 0)
                {
                    IEnumerator enumerator = _authorizationQueue.Dequeue();
                    yield return StartCoroutine(enumerator);
                }

                _queueRunning = false;
            }
        }

        internal class TargetTriggerEntity
        {
            private readonly BaseEntity _entity;
            private readonly Func<BaseEntity, BasePlayer, bool> _shouldIgnorePlayer;

            private static readonly Hash<TargetTrigger, TargetTriggerEntity> TargetTriggers = new Hash<TargetTrigger, TargetTriggerEntity>();
            private static readonly HashSet<TargetTrigger> IgnoreTriggers = new HashSet<TargetTrigger>();

            public TargetTriggerEntity(TargetTrigger targetTrigger, BaseEntity entity, Func<BaseEntity, BasePlayer, bool> shouldIgnorePlayer)
            {
                _entity = entity;
                _shouldIgnorePlayer = shouldIgnorePlayer;
                TargetTriggers[targetTrigger] = this;
            }

            public object ShouldIgnore(BasePlayer player)
                => _shouldIgnorePlayer != null && _shouldIgnorePlayer(_entity, player) ? (object)true : null;

            public static bool TryGet(TargetTrigger targetTrigger, out TargetTriggerEntity targetTriggerEntity)
                => TargetTriggers.TryGetValue(targetTrigger, out targetTriggerEntity);

            public static void Remove(TargetTrigger targetTrigger) => TargetTriggers.Remove(targetTrigger);

            public static void IgnoreTrigger(TargetTrigger targetTrigger) => IgnoreTriggers.Add(targetTrigger);

            public static bool ShouldIgnoreTrigger(TargetTrigger targetTrigger) => IgnoreTriggers.Contains(targetTrigger);

            public static void OnUnload()
            {
                IgnoreTriggers.Clear();
                TargetTriggers.Clear();
            }
        }

        internal class SamSiteMemory
        {
            private static readonly Hash<NetworkableId, SamSiteMemory> SamSites = new Hash<NetworkableId, SamSiteMemory>();

            private readonly SamSite _samSite;
            private readonly Hash<BaseCombatEntity, bool> _memory = new Hash<BaseCombatEntity, bool>();

            public SamSiteMemory(SamSite samSite)
            {
                _samSite = samSite;
                SamSites[samSite.net.ID] = this;
            }

            public static bool TryGet(SamSite samSite, out SamSiteMemory samSiteMemory)
                => SamSites.TryGetValue(samSite.net.ID, out samSiteMemory);

            public bool IsKnown(BaseCombatEntity baseCombatEntity, out bool shouldIgnore)
                => _memory.TryGetValue(baseCombatEntity, out shouldIgnore);

            public void Remember(BaseCombatEntity baseCombatEntity, bool shouldIgnore)
            {
                _memory[baseCombatEntity] = shouldIgnore;
                _samSite.Invoke(() => _memory.Remove(baseCombatEntity), 10f);
            }

            public static void OnUnload() => SamSites.Clear();
        }

        internal class PlayerEntities
        {
            private List<BuildingPrivlidge> _buildingPrivileges;
            private List<AutoTurret> _autoTurrets;
            private List<CodeLock> _codeLocks;

            private static readonly Hash<ulong, PlayerEntities> Entities = new Hash<ulong, PlayerEntities>();

            public static PlayerEntities GetOrCreate(ulong playerId)
            {
                if (!playerId.IsSteamId())
                    return null;

                if (!Entities.TryGetValue(playerId, out PlayerEntities playerEntities))
                    playerEntities = Entities[playerId] = new PlayerEntities();

                return playerEntities;
            }

            public static PlayerEntities Get(ulong playerId)
            {
                if (!playerId.IsSteamId())
                    return null;

                if (Entities.TryGetValue(playerId, out PlayerEntities playerEntities))
                    return playerEntities;

                return null;
            }

            private PlayerEntities() { }

            public void AddEntity(BuildingPrivlidge buildingPrivlidge, bool rebuild)
            {
                if (!buildingPrivlidge)
                    return;

                if (rebuild)
                    AuthorizationQueue.Enqueue(TemporaryShares.RebuildSharesFor(buildingPrivlidge));

                if (_buildingPrivileges == null)
                    _buildingPrivileges = Facepunch.Pool.Get<List<BuildingPrivlidge>>();
                else if (_buildingPrivileges.Contains(buildingPrivlidge))
                    return;

                _buildingPrivileges.Add(buildingPrivlidge);
            }

            public void RemoveEntity(BuildingPrivlidge buildingPrivlidge, bool destroyed)
            {
                if (!buildingPrivlidge)
                    return;

                if (destroyed)
                    TemporaryShares.OnEntityDestroyed(buildingPrivlidge);

                if (_buildingPrivileges == null)
                    return;

                _buildingPrivileges.Remove(buildingPrivlidge);

                if (_buildingPrivileges.Count == 0)
                    Facepunch.Pool.FreeUnmanaged(ref _buildingPrivileges);
            }

            public void AddEntity(AutoTurret autoTurret, bool rebuild)
            {
                if (!autoTurret)
                    return;

                if (rebuild)
                    AuthorizationQueue.Enqueue(TemporaryShares.RebuildSharesFor(autoTurret));

                if (_autoTurrets == null)
                    _autoTurrets = Facepunch.Pool.Get<List<AutoTurret>>();
                else if (_autoTurrets.Contains(autoTurret))
                    return;

                _autoTurrets.Add(autoTurret);
            }

            public void RemoveEntity(AutoTurret autoTurret, bool destroyed)
            {
                if (!autoTurret)
                    return;

                if (destroyed)
                    TemporaryShares.OnEntityDestroyed(autoTurret);

                if (_autoTurrets == null)
                    return;

                _autoTurrets.Remove(autoTurret);

                if (_autoTurrets.Count == 0)
                    Facepunch.Pool.FreeUnmanaged(ref _autoTurrets);
            }

            public void AddEntity(CodeLock codeLock, bool rebuild)
            {
                if (!codeLock)
                    return;

                ShareType shareType = GetShareTypeFromEntity(codeLock.GetParentEntity());
                if (shareType == ShareType.None)
                    return;

                if (rebuild)
                    AuthorizationQueue.Enqueue(TemporaryShares.RebuildSharesFor(shareType, codeLock));

                if (_codeLocks == null)
                    _codeLocks = Facepunch.Pool.Get<List<CodeLock>>();
                else if (_codeLocks.Contains(codeLock))
                    return;

                _codeLocks.Add(codeLock);
            }

            public void RemoveEntity(CodeLock codeLock, bool destroyed)
            {
                if (!codeLock)
                    return;

                if (destroyed)
                    TemporaryShares.OnEntityDestroyed(codeLock);

                if (_codeLocks == null)
                    return;

                _codeLocks.Remove(codeLock);

                if (_codeLocks.Count == 0)
                    Facepunch.Pool.FreeUnmanaged(ref _codeLocks);
            }

            public void OnToggleShareType(ShareType shareType)
            {
                if (shareType == ShareType.Blueprint)
                    return;

                if (shareType == ShareType.Cupboard)
                {
                    if (_buildingPrivileges?.Count > 0)
                        TemporaryShares.RebuildSharesFor(_buildingPrivileges);
                }
                else if (shareType == ShareType.Turret)
                {
                    if (_autoTurrets?.Count > 0)
                        TemporaryShares.RebuildSharesFor(_autoTurrets);
                }
                else if (_codeLocks?.Count > 0)
                {
                    TemporaryShares.RebuildSharesFor(shareType, _codeLocks);
                }
            }

            public void RebuildAll()
            {
                if (_buildingPrivileges?.Count > 0)
                    TemporaryShares.RebuildSharesFor(_buildingPrivileges);

                if (_autoTurrets?.Count > 0)
                    TemporaryShares.RebuildSharesFor(_autoTurrets);

                if (_codeLocks?.Count > 0)
                    TemporaryShares.RebuildSharesFor(_codeLocks);
            }
        }

        internal static class TemporaryShares
        {
            private static readonly Hash<BuildingPrivlidge, List<ulong>> BuildingPrivilegeShares = new Hash<BuildingPrivlidge, List<ulong>>();
            private static readonly Hash<AutoTurret, List<ulong>> AutoTurretShares = new Hash<AutoTurret, List<ulong>>();
            private static readonly Hash<CodeLock, List<ulong>> CodeLockShares = new Hash<CodeLock, List<ulong>>();

            private static List<ulong> _memberShareBuffer = new List<ulong>();
            private static List<ulong> _tempShareBuffer = new List<ulong>();

            public static void RebuildSharesFor(List<BuildingPrivlidge> list)
            {
                for (int i = 0; i < list.Count; i++)
                    AuthorizationQueue.Enqueue(RebuildSharesFor(list[i]));
            }

            public static IEnumerator RebuildSharesFor(BuildingPrivlidge buildingPrivlidge)
            {
                yield return null;

                if (buildingPrivlidge && buildingPrivlidge.OwnerID != 0UL && (!Configuration.Security.PreventShareNoOwner || buildingPrivlidge.IsAuthed(buildingPrivlidge.OwnerID)))
                {
                    bool hasChanges = false;
                    if (BuildingPrivilegeShares.TryGetValue(buildingPrivlidge, out List<ulong> currentShares))
                    {
                        List<ulong> snapshot = Facepunch.Pool.Get<List<ulong>>();
                        foreach (ulong authorizedPlayer in buildingPrivlidge.authorizedPlayers)
                            snapshot.Add(authorizedPlayer);

                        for (int i = snapshot.Count - 1; i >= 0; i--)
                        {
                            ulong authorizedPlayer = snapshot[i];
                            if (currentShares.Contains(authorizedPlayer))
                            {
                                buildingPrivlidge.authorizedPlayers.Remove(authorizedPlayer);
                                hasChanges = true;
                            }
                        }

                        Facepunch.Pool.FreeUnmanaged(ref snapshot);
                        currentShares.Clear();
                    }

                    _memberShareBuffer.Clear();
                    _tempShareBuffer.Clear();

                    yield return null;

                    if (CanShare(ShareType.Cupboard))
                    {
                        StoredData.PlayerData playerData = storedData.FindPlayerData(buildingPrivlidge.OwnerID);
                        if (playerData != null)
                        {
                            if (Configuration.Sharing.Clan.Enabled && playerData.IsSharing(TeamType.Clan, ShareType.Cupboard))
                                GetClanMembers(buildingPrivlidge.OwnerID, ref _memberShareBuffer);

                            if (Configuration.Sharing.Friend.Enabled && playerData.IsSharing(TeamType.Friend, ShareType.Cupboard))
                                GetFriends(buildingPrivlidge.OwnerID, ref _memberShareBuffer);

                            if (Configuration.Sharing.Team.Enabled && playerData.IsSharing(TeamType.Team, ShareType.Cupboard))
                                GetTeamMembers(buildingPrivlidge.OwnerID, ref _memberShareBuffer);
                        }

                        yield return null;

                        foreach (ulong memberId in _memberShareBuffer)
                        {
                            if (_tempShareBuffer.Contains(memberId) || memberId == buildingPrivlidge.OwnerID)
                                continue;

                            if (!buildingPrivlidge.IsAuthed(memberId))
                                buildingPrivlidge.authorizedPlayers.Add(memberId);

                            _tempShareBuffer.Add(memberId);
                            hasChanges = true;
                        }

                        if (currentShares == null)
                            currentShares = BuildingPrivilegeShares[buildingPrivlidge] = Facepunch.Pool.Get<List<ulong>>();

                        if (_tempShareBuffer.Count > 0)
                        {
                            for (int i = 0; i < _tempShareBuffer.Count; i++)
                            {
                                if (!currentShares.Contains(_tempShareBuffer[i]))
                                    currentShares.Add(_tempShareBuffer[i]);
                            }
                        }
                    }

                    if (hasChanges)
                        buildingPrivlidge.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                }
            }

            public static void OnEntityDestroyed(BuildingPrivlidge buildingPrivlidge)
            {
                if (BuildingPrivilegeShares.TryGetValue(buildingPrivlidge, out List<ulong> currentShares))
                {
                    Facepunch.Pool.FreeUnmanaged(ref currentShares);
                    BuildingPrivilegeShares.Remove(buildingPrivlidge);
                }
            }

            public static void RegisterPlayerTo(BasePlayer player, BuildingPrivlidge buildingPrivlidge)
            {
                if (!BuildingPrivilegeShares.TryGetValue(buildingPrivlidge, out List<ulong> currentShares))
                    currentShares = BuildingPrivilegeShares[buildingPrivlidge] = Facepunch.Pool.Get<List<ulong>>();

                ulong userId = player.GetUserId();
                if (!buildingPrivlidge.IsAuthed(userId))
                {
                    buildingPrivlidge.authorizedPlayers.Add(userId);
                    buildingPrivlidge.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);

                    if (!currentShares.Contains(userId))
                        currentShares.Add(userId);
                }
            }

            public static void RemovePlayerFrom(BasePlayer player, BuildingPrivlidge buildingPrivlidge)
            {
                if (!player || !buildingPrivlidge)
                    return;

                ulong userId = player.GetUserId();
                if (BuildingPrivilegeShares.TryGetValue(buildingPrivlidge, out List<ulong> currentShares) && currentShares.Contains(userId))
                    currentShares.Remove(userId);

                buildingPrivlidge.authorizedPlayers.RemoveWhere(id => id == userId);
                buildingPrivlidge.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
            }

            public static void RebuildSharesFor(List<AutoTurret> list)
            {
                for (int i = 0; i < list.Count; i++)
                    AuthorizationQueue.Enqueue(RebuildSharesFor(list[i]));
            }

            public static IEnumerator RebuildSharesFor(AutoTurret autoTurret)
            {
                yield return null;

                if (autoTurret && autoTurret.OwnerID != 0UL && (!Configuration.Security.PreventShareNoOwner || autoTurret.IsAuthed(autoTurret.OwnerID)))
                {
                    bool hasChanges = false;
                    if (AutoTurretShares.TryGetValue(autoTurret, out List<ulong> currentShares))
                    {
                        List<ulong> snapshot = Facepunch.Pool.Get<List<ulong>>();
                        foreach (ulong authorizedPlayer in autoTurret.authorizedPlayers)
                            snapshot.Add(authorizedPlayer);

                        for (int i = snapshot.Count - 1; i >= 0; i--)
                        {
                            ulong authorizedPlayer = snapshot[i];
                            if (currentShares.Contains(authorizedPlayer))
                            {
                                autoTurret.authorizedPlayers.Remove(authorizedPlayer);
                                hasChanges = true;
                            }
                        }

                        Facepunch.Pool.FreeUnmanaged(ref snapshot);
                        currentShares.Clear();
                    }

                    _memberShareBuffer.Clear();
                    _tempShareBuffer.Clear();

                    yield return null;

                    if (CanShare(ShareType.Turret))
                    {
                        StoredData.PlayerData playerData = storedData.FindPlayerData(autoTurret.OwnerID);
                        if (playerData != null)
                        {
                            if (Configuration.Sharing.Clan.Enabled && playerData.IsSharing(TeamType.Clan, ShareType.Turret))
                                GetClanMembers(autoTurret.OwnerID, ref _memberShareBuffer);

                            if (Configuration.Sharing.Friend.Enabled && playerData.IsSharing(TeamType.Friend, ShareType.Turret))
                                GetFriends(autoTurret.OwnerID, ref _memberShareBuffer);

                            if (Configuration.Sharing.Team.Enabled && playerData.IsSharing(TeamType.Team, ShareType.Turret))
                                GetTeamMembers(autoTurret.OwnerID, ref _memberShareBuffer);
                        }

                        yield return null;

                        foreach (ulong memberId in _memberShareBuffer)
                        {
                            if (_tempShareBuffer.Contains(memberId) || memberId == autoTurret.OwnerID)
                                continue;

                            if (!autoTurret.IsAuthed(memberId))
                                autoTurret.authorizedPlayers.Add(memberId);

                            _tempShareBuffer.Add(memberId);
                            hasChanges = true;
                        }

                        if (currentShares == null)
                            currentShares = AutoTurretShares[autoTurret] = Facepunch.Pool.Get<List<ulong>>();

                        if (_tempShareBuffer.Count > 0)
                        {
                            for (int i = 0; i < _tempShareBuffer.Count; i++)
                            {
                                if (!currentShares.Contains(_tempShareBuffer[i]))
                                    currentShares.Add(_tempShareBuffer[i]);
                            }
                        }
                    }

                    autoTurret.target = null;

                    GameObject gameObject = autoTurret.targetTrigger.gameObject;
                    gameObject.SetActive(false);
                    yield return null;
                    gameObject.SetActive(true);

                    if (hasChanges)
                        autoTurret.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                }
            }

            public static void OnEntityDestroyed(AutoTurret autoTurret)
            {
                if (AutoTurretShares.TryGetValue(autoTurret, out List<ulong> currentShares))
                {
                    Facepunch.Pool.FreeUnmanaged(ref currentShares);
                    AutoTurretShares.Remove(autoTurret);
                }
            }

            public static void RebuildSharesFor(ShareType shareType, List<CodeLock> list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    CodeLock codeLock = list[i];
                    if (codeLock)
                    {
                        ShareType entityShareType = GetShareTypeFromEntity(codeLock.GetParentEntity());
                        if (entityShareType == shareType)
                            AuthorizationQueue.Enqueue(RebuildSharesFor(shareType, list[i]));
                    }
                }
            }

            public static void RebuildSharesFor(List<CodeLock> list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    CodeLock codeLock = list[i];
                    ShareType shareType = GetShareTypeFromEntity(codeLock.GetParentEntity());
                    AuthorizationQueue.Enqueue(RebuildSharesFor(shareType, list[i]));
                }
            }

            public static IEnumerator RebuildSharesFor(ShareType shareType, CodeLock codeLock)
            {
                yield return null;

                ulong ownerId = GetShareOwnerId(codeLock);
                if (codeLock && ownerId != 0UL && (!Configuration.Security.PreventShareNoOwner
                    || codeLock.whitelistPlayers.Contains(ownerId)
                    || codeLock.whitelistPlayers.Contains(codeLock.OwnerID)))
                {
                    bool hasChanges = false;
                    if (CodeLockShares.TryGetValue(codeLock, out List<ulong> currentShares))
                    {
                        for (int i = currentShares.Count - 1; i >= 0; i--)
                        {
                            ulong memberId = currentShares[i];
                            if (codeLock.guestPlayers.Contains(memberId))
                            {
                                codeLock.guestPlayers.Remove(memberId);
                                hasChanges = true;
                            }
                            if (memberId != ownerId && codeLock.whitelistPlayers.Contains(memberId))
                            {
                                codeLock.whitelistPlayers.Remove(memberId);
                                hasChanges = true;
                            }
                        }

                        currentShares.Clear();
                    }

                    yield return null;

                    _memberShareBuffer.Clear();
                    _tempShareBuffer.Clear();

                    if (CanShare(shareType))
                    {
                        if (CanShare(TeamType.Clan, ownerId) && OwnerIsSharing(ownerId, TeamType.Clan, shareType))
                            GetClanMembers(ownerId, ref _memberShareBuffer);

                        if (CanShare(TeamType.Friend, ownerId) && OwnerIsSharing(ownerId, TeamType.Friend, shareType))
                            GetFriends(ownerId, ref _memberShareBuffer);

                        if (CanShare(TeamType.Team, ownerId) && OwnerIsSharing(ownerId, TeamType.Team, shareType))
                            GetTeamMembers(ownerId, ref _memberShareBuffer);

                        yield return null;

                        foreach (ulong memberId in _memberShareBuffer)
                        {
                            if (_tempShareBuffer.Contains(memberId) || memberId == ownerId || codeLock.whitelistPlayers.Contains(memberId))
                                continue;

                            // Whitelist so the client gets hasAuth and can open doors without the PIN.
                            codeLock.whitelistPlayers.Add(memberId);
                            hasChanges = true;
                            _tempShareBuffer.Add(memberId);
                        }

                        if (currentShares == null)
                            currentShares = CodeLockShares[codeLock] = Facepunch.Pool.Get<List<ulong>>();

                        if (_tempShareBuffer.Count > 0)
                        {
                            for (int i = 0; i < _tempShareBuffer.Count; i++)
                            {
                                if (!currentShares.Contains(_tempShareBuffer[i]))
                                    currentShares.Add(_tempShareBuffer[i]);
                            }
                        }
                    }

                    if (hasChanges)
                        codeLock.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                }
            }

            public static void OnEntityDestroyed(CodeLock codeLock)
            {
                if (CodeLockShares.TryGetValue(codeLock, out List<ulong> currentShares))
                {
                    Facepunch.Pool.FreeUnmanaged(ref currentShares);
                    CodeLockShares.Remove(codeLock);
                }
            }

            private static void GetClanMembers(ulong playerId, ref List<ulong> list)
            {
                SocialBridges.Clans.GetMembers(playerId, list);
            }

            private static void GetFriends(ulong playerId, ref List<ulong> list)
            {
                ulong[] array = SocialBridges.Friends.GetFriends(playerId);
                if (array == null) return;
                for (int i = 0; i < array.Length; i++)
                    list.Add(array[i]);
            }

            private static void GetTeamMembers(ulong playerId, ref List<ulong> list)
            {
                RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance?.FindPlayersTeam(playerId);
                if (playerTeam == null)
                    return;

                for (int i = 0; i < playerTeam.members.Count; i++)
                    list.Add(playerTeam.members[i]);
            }

            public static void Save()
            {
                TemporaryShareData temporaryShareData = new TemporaryShareData();

                foreach (KeyValuePair<BuildingPrivlidge, List<ulong>> kvp in BuildingPrivilegeShares)
                {
                    if (kvp.Key && kvp.Key.net != null && kvp.Value?.Count > 0)
                        temporaryShareData.temporaryCupboardShares[kvp.Key.net.ID.Value] = kvp.Value;
                }

                foreach (KeyValuePair<AutoTurret, List<ulong>> kvp in AutoTurretShares)
                {
                    if (kvp.Key && kvp.Key.net != null && kvp.Value?.Count > 0)
                        temporaryShareData.temporaryTurretShares[kvp.Key.net.ID.Value] = kvp.Value;
                }

                foreach (KeyValuePair<CodeLock, List<ulong>> kvp in CodeLockShares)
                {
                    if (kvp.Key && kvp.Key.net != null && kvp.Value?.Count > 0)
                        temporaryShareData.temporaryCodeLockShare[kvp.Key.net.ID.Value] = kvp.Value;
                }

                try
                {
                    string path = DynamicCupShareHost.Instance?.TemporarySharesPath;
                    if (string.IsNullOrEmpty(path)) return;
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                    File.WriteAllText(path, JsonConvert.SerializeObject(temporaryShareData, Formatting.Indented));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[DynamicCupShare] temporary shares save failed: " + ex.Message);
                }
            }

            public static TemporaryShareData Load()
            {
                try
                {
                    string path = DynamicCupShareHost.Instance?.TemporarySharesPath;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        return JsonConvert.DeserializeObject<TemporaryShareData>(File.ReadAllText(path)) ?? new TemporaryShareData();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[DynamicCupShare] temporary shares load failed: " + ex.Message);
                }

                return new TemporaryShareData();
            }

            public static void OnUnload()
            {
                foreach (KeyValuePair<BuildingPrivlidge, List<ulong>> kvp in BuildingPrivilegeShares)
                {
                    List<ulong> list = kvp.Value;
                    Facepunch.Pool.FreeUnmanaged(ref list);
                }

                foreach (KeyValuePair<AutoTurret, List<ulong>> kvp in AutoTurretShares)
                {
                    List<ulong> list = kvp.Value;
                    Facepunch.Pool.FreeUnmanaged(ref list);
                }

                foreach (KeyValuePair<CodeLock, List<ulong>> kvp in CodeLockShares)
                {
                    List<ulong> list = kvp.Value;
                    Facepunch.Pool.FreeUnmanaged(ref list);
                }

                BuildingPrivilegeShares.Clear();
                AutoTurretShares.Clear();
                CodeLockShares.Clear();
            }
        }
    }
}
