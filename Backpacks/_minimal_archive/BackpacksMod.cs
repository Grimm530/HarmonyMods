using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Rust;
using Facepunch;
using Network;

namespace Backpacks
{
    public class BackpacksMod : IHarmonyModHooks
    {
        public static BackpacksMod Instance { get; private set; }

        private const string DroppedBackpackPrefab = "assets/prefabs/misc/item drop/item_drop_backpack.prefab";
        private const string CoffinPrefab = "assets/prefabs/misc/halloween/coffin/coffinstorage.prefab";
        private const string ResizableLootPanelName = "generic_resizable";
        private static readonly Vector3 HiddenPosition = new Vector3(0, -500, 0);

        private sealed class BackpackOpenState
        {
            public List<StorageContainer> Pages = new List<StorageContainer>();
            public int CurrentPageIndex;
        }

        private readonly Dictionary<ulong, BackpackOpenState> _openBackpackState = new Dictionary<ulong, BackpackOpenState>();
        /// <summary>When set, we're switching pages (Clear() was called to swap container); don't save/kill/remove state.</summary>
        private readonly HashSet<ulong> _suppressBackpackCleanup = new HashSet<ulong>();
        private ConsoleSystem.Command _backpackCmd;
        private ConsoleSystem.Command _backpackPageCmd;
        private uint _buttonPngId;
        private string _buttonJson;
        private bool _buttonLoadRetryScheduled;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            BackpacksConfig.LoadConfig();
            RegisterBackpackCommand();
            RegisterBackpackPageCommand();
            // Build button with fallback icon first; image loaded next tick so FileStorage is ready (TCUpgrade pattern).
            BuildButtonJson();
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player != null && !player.IsNpc && player.IsConnected) ShowBackpackButton(player);
            }
            UnityEngine.Debug.Log("[Backpacks] Harmony mod loaded. Use 'backpack' in F1 or click the on-screen button. Drop/erase on death per config.");
            UnityEngine.Debug.Log("[Backpacks] Backpack data folder: " + GetDataFolder());
            NextTick(DeferredLoadButtonAndBuild);
        }

        /// <summary>Load button image from HarmonyImages/Backpack/backpackgz.png into FileStorage, cache ID, rebuild JSON, refresh for all players. Retries after 5s if CommunityEntity/FileStorage not ready.</summary>
        private void DeferredLoadButtonAndBuild()
        {
            try
            {
                LoadButtonImageIntoFileStorage();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[Backpacks] LoadButtonImage failed: " + ex.Message);
                if (!_buttonLoadRetryScheduled && ServerMgr.Instance != null)
                {
                    _buttonLoadRetryScheduled = true;
                    ServerMgr.Instance.StartCoroutine(DelayedRetryLoadButtonCoroutine());
                }
            }
            BuildButtonJson();
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player != null && !player.IsNpc && player.IsConnected) ShowBackpackButton(player);
            }
        }

        private IEnumerator DelayedRetryLoadButtonCoroutine()
        {
            yield return CoroutineEx.waitForSeconds(5f);
            _buttonLoadRetryScheduled = false;
            DeferredLoadButtonAndBuild();
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            UnregisterBackpackCommand();
            UnregisterBackpackPageCommand();
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player != null && player.IsConnected)
                {
                    BackpackButtonUI.Destroy(player);
                    BackpackPageButtonsUI.Destroy(player);
                }
            }
            _buttonJson = null;
            foreach (var kv in _openBackpackState.ToList())
            {
                var userId = kv.Key;
                var state = kv.Value;
                if (state?.Pages != null)
                {
                    var cfg = BackpacksConfig.Config;
                    var pageCount = cfg != null && cfg.PageCount > 1 ? Mathf.Clamp(cfg.PageCount, 1, 8) : 1;
                    if (pageCount > 1)
                    {
                        for (int i = 0; i < state.Pages.Count && i < pageCount; i++)
                        {
                            var ent = state.Pages[i];
                            if (ent != null && !ent.IsDestroyed)
                            {
                                SaveBackpackPage(userId, i, ent.inventory);
                                ent.Kill();
                            }
                        }
                    }
                    else if (state.Pages.Count > 0 && state.Pages[0] != null && !state.Pages[0].IsDestroyed)
                    {
                        SaveBackpackFromContainer(userId, state.Pages[0].inventory);
                        state.Pages[0].Kill();
                    }
                }
            }
            _openBackpackState.Clear();
            _suppressBackpackCleanup.Clear();
            Instance = null;
            UnityEngine.Debug.Log("[Backpacks] Harmony mod unloaded.");
        }

        private void RegisterBackpackCommand()
        {
            try
            {
                _backpackCmd = new ConsoleSystem.Command
                {
                    Name = "backpack",
                    FullName = "global.backpack",
                    Variable = false,
                    ServerAdmin = false,
                    ServerUser = true,
                    Call = CmdBackpack
                };
                if (ConsoleSystem.Index.Server.Dict != null && !ConsoleSystem.Index.Server.Dict.ContainsKey("global.backpack"))
                {
                    ConsoleSystem.Index.Server.Dict["global.backpack"] = _backpackCmd;
                    if (ConsoleSystem.Index.Server.GlobalDict != null)
                        ConsoleSystem.Index.Server.GlobalDict["backpack"] = _backpackCmd;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[Backpacks] backpack command registration failed: " + ex.Message);
            }
        }

        private void UnregisterBackpackCommand()
        {
            try
            {
                if (ConsoleSystem.Index.Server.Dict != null)
                    ConsoleSystem.Index.Server.Dict.Remove("global.backpack");
                if (ConsoleSystem.Index.Server.GlobalDict != null)
                    ConsoleSystem.Index.Server.GlobalDict?.Remove("backpack");
            }
            catch { }
        }

        private void RegisterBackpackPageCommand()
        {
            try
            {
                _backpackPageCmd = new ConsoleSystem.Command
                {
                    Name = "backpack.page",
                    FullName = "global.backpack.page",
                    Variable = false,
                    ServerAdmin = false,
                    ServerUser = true,
                    Call = CmdBackpackPage
                };
                if (ConsoleSystem.Index.Server.Dict != null && !ConsoleSystem.Index.Server.Dict.ContainsKey("global.backpack.page"))
                {
                    ConsoleSystem.Index.Server.Dict["global.backpack.page"] = _backpackPageCmd;
                    if (ConsoleSystem.Index.Server.GlobalDict != null)
                        ConsoleSystem.Index.Server.GlobalDict["backpack.page"] = _backpackPageCmd;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[Backpacks] backpack.page command registration failed: " + ex.Message);
            }
        }

        private void UnregisterBackpackPageCommand()
        {
            try
            {
                if (ConsoleSystem.Index.Server.Dict != null)
                    ConsoleSystem.Index.Server.Dict.Remove("global.backpack.page");
                if (ConsoleSystem.Index.Server.GlobalDict != null)
                    ConsoleSystem.Index.Server.GlobalDict?.Remove("backpack.page");
            }
            catch { }
        }

        private void CmdBackpackPage(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null || player.IsNpc) return;
            if (!_openBackpackState.TryGetValue(player.userID, out var state) || state?.Pages == null || state.Pages.Count <= 1) return;
            int pageNum = 1;
            if (arg.HasArgs() && arg.Args != null && arg.Args.Length > 0)
                int.TryParse(arg.Args[0], out pageNum);
            int pageIndex = Mathf.Clamp(pageNum - 1, 0, state.Pages.Count - 1);
            if (pageIndex == state.CurrentPageIndex) return;
            var ent = state.Pages[pageIndex];
            if (ent == null || ent.IsDestroyed) return;
            var container = ent.inventory;
            if (container == null) return;

            _suppressBackpackCleanup.Add(player.userID);
            try
            {
                player.inventory.loot.Clear();
                if (!player.inventory.loot.StartLootingEntity(ent, false))
                    return;
                player.inventory.loot.AddContainer(container);
                player.inventory.loot.SendImmediate();
                player.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", player), ResizableLootPanelName);
                state.CurrentPageIndex = pageIndex;
                var cfg = BackpacksConfig.Config;
                var numPages = cfg != null ? Mathf.Clamp(cfg.PageCount, 1, 8) : 3;
                var slotsPerPage = cfg != null ? Mathf.Clamp(cfg.SlotsPerPage, 6, 48) : 48;
                BackpackPageButtonsUI.Destroy(player);
                BackpackPageButtonsUI.Show(player, BackpackPageButtonsUI.BuildJson(numPages, pageIndex, slotsPerPage));
            }
            finally
            {
                _suppressBackpackCleanup.Remove(player.userID);
            }
        }

        private void CmdBackpack(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null || player.IsNpc) return;
            // Defer one tick so previous loot close has fully completed (avoids StartLootingEntity failing on second open)
            NextTick(() => OpenBackpack(player));
        }

        /// <summary>
        /// Load HarmonyImages/Backpack/backpackgz.png (or config path) into FileStorage, cache texture ID.
        /// CUI RawImage uses this id so the client gets the image via CL_ReceiveFilePng. Skip if Button image URL is set.
        /// </summary>
        private void LoadButtonImageIntoFileStorage()
        {
            _buttonPngId = 0;
            var cfg = BackpacksConfig.Config;
            if (cfg == null || !cfg.ShowButton) return;
            if (!string.IsNullOrWhiteSpace(cfg.ButtonImageUrl)) return;

            var ce = CommunityEntity.ServerInstance;
            if (ce == null || ce.IsDestroyed) ce = FindCommunityEntity();
            if (ce == null || ce.IsDestroyed)
            {
                if (!_buttonLoadRetryScheduled && ServerMgr.Instance != null)
                {
                    _buttonLoadRetryScheduled = true;
                    ServerMgr.Instance.StartCoroutine(DelayedRetryLoadButtonCoroutine());
                }
                return;
            }

            var serverRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var path = Path.Combine(serverRoot, (cfg.ButtonImagePath ?? "").Trim());
            if (string.IsNullOrWhiteSpace(cfg.ButtonImagePath) || !File.Exists(path))
                path = Path.Combine(serverRoot, "HarmonyImages", "Backpack", "backpackgz.png");
            if (!File.Exists(path)) return;

            var bytes = File.ReadAllBytes(path);
            if (bytes == null || bytes.Length == 0) return;
            _buttonPngId = FileStorage.server.Store(bytes, FileStorage.Type.png, ce.net.ID);
            UnityEngine.Debug.Log("[Backpacks] Button image loaded from " + path + " (FileStorage id " + _buttonPngId + "). Cached for CUI.");
        }

        private static CommunityEntity FindCommunityEntity()
        {
            if (BaseNetworkable.serverEntities == null) return null;
            foreach (var e in BaseNetworkable.serverEntities)
            {
                if (e is CommunityEntity c && c != null && !c.IsDestroyed) return c;
            }
            return null;
        }

        private void BuildButtonJson()
        {
            var cfg = BackpacksConfig.Config;
            if (cfg == null || !cfg.ShowButton)
            {
                _buttonJson = null;
                return;
            }
            var useUrl = !string.IsNullOrWhiteSpace(cfg.ButtonImageUrl);
            var url = useUrl ? cfg.ButtonImageUrl.Trim() : "";
            var usePngId = !useUrl && _buttonPngId != 0;
            var min = string.IsNullOrWhiteSpace(cfg.ButtonAnchorsMin) ? "0.5 0.0" : cfg.ButtonAnchorsMin.Trim();
            var max = string.IsNullOrWhiteSpace(cfg.ButtonAnchorsMax) ? "0.5 0.0" : cfg.ButtonAnchorsMax.Trim();
            var offMin = string.IsNullOrWhiteSpace(cfg.ButtonOffsetsMin) ? "-260 18" : cfg.ButtonOffsetsMin.Trim();
            var offMax = string.IsNullOrWhiteSpace(cfg.ButtonOffsetsMax) ? "-200 78" : cfg.ButtonOffsetsMax.Trim();
            _buttonJson = BackpackButtonUI.BuildJson(min, max, offMin, offMax, useUrl, url, usePngId, _buttonPngId);
        }

        public void ShowBackpackButton(BasePlayer player)
        {
            var cfg = BackpacksConfig.Config;
            if (cfg == null || !cfg.ShowButton || string.IsNullOrEmpty(_buttonJson)) return;
            BackpackButtonUI.Show(player, _buttonJson);
        }

        private void DestroyBackpackButton(BasePlayer player)
        {
            BackpackButtonUI.Destroy(player);
        }

        private static string GetDataPath(ulong userId)
        {
            var dir = GetDataFolder();
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return Path.Combine(dir, userId + ".json");
        }

        private static string GetDataFolder()
        {
            var serverRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var cfg = BackpacksConfig.Config;
            var relative = (cfg != null && !string.IsNullOrWhiteSpace(cfg.DataFolderPath)) ? cfg.DataFolderPath.Trim() : "HarmonyData/BackpacksData";
            if (Path.IsPathRooted(relative)) return relative;
            return Path.Combine(serverRoot, relative);
        }

        private static string GetOxideDataPath(ulong userId)
        {
            var serverRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(serverRoot, "oxide", "data", "Backpacks", userId + ".json");
        }

        public void OnPlayerDie(BasePlayer player)
        {
            if (player == null || player.IsNpc) return;
            DestroyBackpackButton(player);
            BackpackPageButtonsUI.Destroy(player);
            var cfg = BackpacksConfig.Config;
            if (cfg == null) return;

            if (_openBackpackState.TryGetValue(player.userID, out var state) && state?.Pages != null)
            {
                var pageCount = cfg.PageCount > 1 ? Mathf.Clamp(cfg.PageCount, 1, 8) : 1;
                if (pageCount > 1)
                {
                    var pagesData = new BackpackPagesData();
                    for (int i = 0; i < state.Pages.Count && i < pageCount; i++)
                    {
                        var ent = state.Pages[i];
                        if (ent != null && !ent.IsDestroyed && ent.inventory != null)
                            pagesData.SetPage(i, SerializeContainer(ent.inventory));
                    }
                    if (!cfg.EraseOnDeath) SaveBackpackPagesData(player.userID, pagesData);
                    foreach (var p in state.Pages) { if (p != null && !p.IsDestroyed) p.Kill(); }
                }
                else if (state.Pages.Count > 0 && state.Pages[0] != null && !state.Pages[0].IsDestroyed)
                {
                    if (!cfg.EraseOnDeath) SaveBackpackFromContainer(player.userID, state.Pages[0].inventory);
                    state.Pages[0].Kill();
                }
                _openBackpackState.Remove(player.userID);
            }

            if (cfg.EraseOnDeath)
            {
                var path = GetDataPath(player.userID);
                if (File.Exists(path)) try { File.Delete(path); } catch { }
                return;
            }

            if (cfg.DropOnDeath)
            {
                var pageCount = cfg.PageCount > 1 ? Mathf.Clamp(cfg.PageCount, 1, 8) : 1;
                var slotsPerPage = Mathf.Clamp(cfg.SlotsPerPage, 6, 48);
                List<BackpackItemEntry> entries = null;
                if (pageCount > 1)
                {
                    var pagesData = LoadBackpackPagesData(player.userID);
                    entries = pagesData.GetPage(0);
                    if (entries == null || entries.Count == 0) return;
                }
                else
                {
                    entries = LoadBackpackData(player.userID);
                    if (entries == null || entries.Count == 0) return;
                }

                var position = player.transform.position;
                var capacity = pageCount > 1 ? slotsPerPage : Mathf.Clamp(cfg.Capacity, 1, 48);
                var entity2 = GameManager.server.CreateEntity(DroppedBackpackPrefab, position, Quaternion.Euler(0, 90, 0)) as DroppedItemContainer;
                if (entity2 == null) return;

                entity2.lootPanelName = ResizableLootPanelName;
                entity2.playerName = player.displayName + "'s Backpack";
                entity2.playerSteamID = player.userID;
                entity2.inventory = new ItemContainer();
                entity2.inventory.ServerInitialize(null, capacity);
                entity2.inventory.GiveUID();
                entity2.inventory.entityOwner = entity2;
                entity2.inventory.SetFlag(ItemContainer.Flag.NoItemInput, true);

                var itemsToGive = DeserializeToItems(entries);
                foreach (var item in itemsToGive)
                {
                    if (item != null && !item.MoveToContainer(entity2.inventory))
                        item.Remove();
                }

                entity2.Spawn();
                var minTime = Mathf.Max(cfg.MinimumDespawnTime, 60f);
                entity2.ResetRemovalTime(Mathf.Max(minTime, entity2.CalculateRemovalTime()));

                if (pageCount > 1)
                {
                    var pagesData = LoadBackpackPagesData(player.userID);
                    var remaining = new BackpackPagesData();
                    remaining.SetPage(0, new List<BackpackItemEntry>());
                    for (int i = 1; i < pageCount; i++) remaining.SetPage(i, pagesData.GetPage(i));
                    SaveBackpackPagesData(player.userID, remaining);
                }
                else
                {
                    var path2 = GetDataPath(player.userID);
                    try { if (File.Exists(path2)) File.Delete(path2); } catch { }
                }
            }
        }

        public void OpenBackpack(BasePlayer player)
        {
            if (player == null || player.IsNpc) return;
            var cfg = BackpacksConfig.Config;
            if (cfg == null) return;

            if (_openBackpackState.TryGetValue(player.userID, out var existingState) && existingState?.Pages != null)
            {
                if (player.inventory.loot.containers != null && existingState.Pages.Any(p => p != null && !p.IsDestroyed && player.inventory.loot.containers.Any(c => c?.entityOwner == p)))
                    return;
                BackpackPageButtonsUI.Destroy(player);
                foreach (var p in existingState.Pages)
                {
                    if (p != null && !p.IsDestroyed) p.Kill();
                }
                _openBackpackState.Remove(player.userID);
            }

            int pageCount = Mathf.Clamp(cfg.PageCount, 1, 8);
            int slotsPerPage = Mathf.Clamp(cfg.SlotsPerPage, 6, 48);
            int capacity = pageCount > 1 ? slotsPerPage : Mathf.Clamp(cfg.Capacity, 1, 48);

            if (pageCount > 1)
            {
                var state = new BackpackOpenState { CurrentPageIndex = 0 };
                var pagesData = LoadBackpackPagesData(player.userID);
                for (int i = 0; i < pageCount; i++)
                {
                    var storageEntity = GameManager.server.CreateEntity(CoffinPrefab, HiddenPosition + new Vector3(i * 2f, 0, 0), Quaternion.identity) as StorageContainer;
                    if (storageEntity == null) continue;
                    storageEntity.SetFlag(BaseEntity.Flags.Disabled, true);
                    storageEntity.panelName = ResizableLootPanelName;
                    storageEntity.Spawn();
                    var marker = storageEntity.gameObject.AddComponent<BackpackStorageMarker>();
                    marker.OwnerId = player.userID;
                    marker.PageIndex = i;
                    state.Pages.Add(storageEntity);

                    var container = storageEntity.inventory;
                    if (container == null)
                    {
                        container = new ItemContainer();
                        container.ServerInitialize(null, capacity);
                        container.GiveUID();
                        container.entityOwner = storageEntity;
                    }
                    else
                    {
                        container.capacity = capacity;
                        while (container.itemList?.Count > 0)
                            container.itemList[0]?.Remove();
                    }
                    var entries = pagesData.GetPage(i);
                    if (entries != null && entries.Count > 0)
                    {
                        var items = DeserializeToItems(entries);
                        foreach (var item in items)
                        {
                            if (item != null && !item.MoveToContainer(container))
                                item.Remove();
                        }
                    }
                }
                if (state.Pages.Count == 0) return;
                _openBackpackState[player.userID] = state;
                var firstEnt = state.Pages[0];
                var firstContainer = firstEnt.inventory;
                if (player.inventory.loot.StartLootingEntity(firstEnt, false))
                {
                    player.inventory.loot.AddContainer(firstContainer);
                    player.inventory.loot.SendImmediate();
                    player.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", player), ResizableLootPanelName);
                    BackpackPageButtonsUI.Show(player, BackpackPageButtonsUI.BuildJson(pageCount, 0, slotsPerPage));
                }
                else
                {
                    // Clean up so we don't leave orphan entities; player can click again
                    foreach (var p in state.Pages)
                    {
                        if (p != null && !p.IsDestroyed) p.Kill();
                    }
                    _openBackpackState.Remove(player.userID);
                }
                return;
            }

            var singleEntity = GameManager.server.CreateEntity(CoffinPrefab, HiddenPosition, Quaternion.identity) as StorageContainer;
            if (singleEntity == null) return;
            singleEntity.SetFlag(BaseEntity.Flags.Disabled, true);
            singleEntity.panelName = ResizableLootPanelName;
            singleEntity.Spawn();
            var singleMarker = singleEntity.gameObject.AddComponent<BackpackStorageMarker>();
            singleMarker.OwnerId = player.userID;
            singleMarker.PageIndex = 0;
            _openBackpackState[player.userID] = new BackpackOpenState { Pages = new List<StorageContainer> { singleEntity }, CurrentPageIndex = 0 };

            var singleContainer = singleEntity.inventory;
            if (singleContainer == null)
            {
                singleContainer = new ItemContainer();
                singleContainer.ServerInitialize(null, capacity);
                singleContainer.GiveUID();
                singleContainer.entityOwner = singleEntity;
            }
            else
            {
                singleContainer.capacity = capacity;
                while (singleContainer.itemList?.Count > 0)
                    singleContainer.itemList[0]?.Remove();
            }
            var singleEntries = LoadBackpackData(player.userID);
            if (singleEntries != null && singleEntries.Count > 0)
            {
                var items = DeserializeToItems(singleEntries);
                foreach (var item in items)
                {
                    if (item != null && !item.MoveToContainer(singleContainer))
                        item.Remove();
                }
            }
            if (player.inventory.loot.StartLootingEntity(singleEntity, false))
            {
                player.inventory.loot.AddContainer(singleContainer);
                player.inventory.loot.SendImmediate();
                player.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", player), ResizableLootPanelName);
            }
        }

        public void SaveBackpackWhenLootClosed(PlayerLoot loot)
        {
            if (loot == null) return;
            var player = GetPlayerFromPlayerLoot(loot);
            if (player == null) return;

            BackpackPageButtonsUI.Destroy(player);
            if (_suppressBackpackCleanup.Contains(player.userID)) return;

            if (loot.containers == null) return;

            foreach (var container in loot.containers)
            {
                if (container?.entityOwner == null) continue;
                var marker = container.entityOwner.GetComponent<BackpackStorageMarker>();
                if (marker == null) continue;
                var userId = marker.OwnerId;
                if (!_openBackpackState.TryGetValue(userId, out var state) || state?.Pages == null) continue;
                var cfg = BackpacksConfig.Config;
                var pageCount = cfg != null && cfg.PageCount > 1 ? Mathf.Clamp(cfg.PageCount, 1, 8) : 1;
                if (pageCount > 1)
                {
                    var pagesData = new BackpackPagesData();
                    for (int i = 0; i < state.Pages.Count && i < pageCount; i++)
                    {
                        var ent = state.Pages[i];
                        if (ent != null && !ent.IsDestroyed && ent.inventory != null)
                            pagesData.SetPage(i, SerializeContainer(ent.inventory));
                    }
                    SaveBackpackPagesData(userId, pagesData);
                    foreach (var p in state.Pages)
                    {
                        if (p != null && !p.IsDestroyed) p.Kill();
                    }
                }
                else
                {
                    var inv = (container.entityOwner as StorageContainer)?.inventory ?? container;
                    SaveBackpackFromContainer(userId, inv);
                    if (state.Pages.Count > 0 && state.Pages[0] != null && !state.Pages[0].IsDestroyed)
                        state.Pages[0].Kill();
                }
                _openBackpackState.Remove(userId);
                break;
            }
        }

        private void SaveBackpackFromContainer(ulong userId, ItemContainer container)
        {
            if (container == null) return;
            var entries = SerializeContainer(container);
            SaveBackpackData(userId, entries);
        }

        private static BasePlayer GetPlayerFromPlayerLoot(PlayerLoot loot)
        {
            if (loot == null) return null;
            var t = loot.GetType();
            while (t != null)
            {
                var f = t.GetField("_baseEntity", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (f != null)
                    return f.GetValue(loot) as BasePlayer;
                t = t.BaseType;
            }
            return null;
        }

        /// <summary>
        /// Called from patch when our backpack entity receives PlayerStoppedLooting. Does full cleanup (save, kill entities, remove state, destroy UI).
        /// This is the reliable path since we have the player and entity; SaveBackpackWhenLootClosed may not find the player if reflection fails.
        /// </summary>
        public void OnBackpackEntityStoppedLooting(BasePlayer player, ulong backpackOwnerId)
        {
            if (player == null) return;
            BackpackPageButtonsUI.Destroy(player);
            if (_suppressBackpackCleanup.Contains(player.userID)) return;
            if (!_openBackpackState.TryGetValue(backpackOwnerId, out var state) || state?.Pages == null) return;

            var cfg = BackpacksConfig.Config;
            var pageCount = cfg != null && cfg.PageCount > 1 ? Mathf.Clamp(cfg.PageCount, 1, 8) : 1;
            if (pageCount > 1)
            {
                var pagesData = new BackpackPagesData();
                for (int i = 0; i < state.Pages.Count && i < pageCount; i++)
                {
                    var ent = state.Pages[i];
                    if (ent != null && !ent.IsDestroyed && ent.inventory != null)
                        pagesData.SetPage(i, SerializeContainer(ent.inventory));
                }
                SaveBackpackPagesData(backpackOwnerId, pagesData);
                foreach (var p in state.Pages)
                {
                    if (p != null && !p.IsDestroyed) p.Kill();
                }
            }
            else if (state.Pages.Count > 0 && state.Pages[0] != null && !state.Pages[0].IsDestroyed)
            {
                SaveBackpackFromContainer(backpackOwnerId, state.Pages[0].inventory);
                state.Pages[0].Kill();
            }
            _openBackpackState.Remove(backpackOwnerId);
        }

        private static List<BackpackItemEntry> LoadBackpackData(ulong userId)
        {
            var path = GetDataPath(userId);
            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    var list = JsonConvert.DeserializeObject<List<BackpackItemEntry>>(json);
                    if (list != null && list.Count > 0) return list;
                    if (json != null && json.IndexOf("\"Items\"", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var pagesData = TryLoadFromOxideFormat(path, 48);
                        if (pagesData != null) return MergePagesToFlatList(pagesData);
                    }
                }
                catch { }
            }

            var oxidePath = GetOxideDataPath(userId);
            if (File.Exists(oxidePath))
            {
                var pagesData = TryLoadFromOxideFormat(oxidePath, 48);
                if (pagesData != null) return MergePagesToFlatList(pagesData);
            }

            return null;
        }

        private static List<BackpackItemEntry> MergePagesToFlatList(BackpackPagesData pagesData)
        {
            if (pagesData == null) return null;
            var merged = new List<BackpackItemEntry>();
            for (int i = 0; i <= 2; i++)
            {
                var page = pagesData.GetPage(i);
                if (page == null) continue;
                foreach (var e in page)
                {
                    merged.Add(new BackpackItemEntry
                    {
                        ItemId = e.ItemId,
                        Amount = e.Amount,
                        Slot = i * 48 + e.Slot,
                        Condition = e.Condition,
                        MaxCondition = e.MaxCondition,
                        Blueprint = e.Blueprint,
                        Skin = e.Skin,
                        Contents = e.Contents
                    });
                }
            }
            return merged.Count > 0 ? merged : null;
        }

        private static BackpackPagesData LoadBackpackPagesData(ulong userId)
        {
            var path = GetDataPath(userId);
            var logLoad = BackpacksConfig.Config?.LogLoadPath ?? true;

            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                try
                {
                    var data = JsonConvert.DeserializeObject<BackpackPagesData>(json);
                    if (data != null && HasAnyPageItems(data))
                    {
                        if (logLoad) UnityEngine.Debug.Log("[Backpacks] Load " + userId + " from " + path + " (our format), items: " + CountPageItems(data));
                        return data;
                    }
                }
                catch { }
                try
                {
                    var list = JsonConvert.DeserializeObject<List<BackpackItemEntry>>(json);
                    var data = new BackpackPagesData();
                    if (list != null && list.Count > 0)
                    {
                        data.SetPage(0, list);
                        if (logLoad) UnityEngine.Debug.Log("[Backpacks] Load " + userId + " from " + path + " (legacy list), items: " + list.Count);
                        return data;
                    }
                }
                catch { }
                if (json != null && json.IndexOf("\"Items\"", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var data = TryLoadFromOxideFormat(path, 48);
                    if (data != null && HasAnyPageItems(data))
                    {
                        if (logLoad) UnityEngine.Debug.Log("[Backpacks] Load " + userId + " from " + path + " (Oxide format), items: " + CountPageItems(data));
                        return data;
                    }
                }
            }

            var oxidePath = GetOxideDataPath(userId);
            if (File.Exists(oxidePath))
            {
                var data = TryLoadFromOxideFormat(oxidePath, 48);
                if (data != null && HasAnyPageItems(data))
                {
                    if (logLoad) UnityEngine.Debug.Log("[Backpacks] Load " + userId + " from " + oxidePath + " (Oxide format), items: " + CountPageItems(data));
                    return data;
                }
            }

            if (logLoad) UnityEngine.Debug.Log("[Backpacks] Load " + userId + ": no data. Tried: " + path + " ; " + GetOxideDataPath(userId));
            return new BackpackPagesData();
        }

        private static int CountPageItems(BackpackPagesData data)
        {
            if (data == null) return 0;
            return (data.Page0?.Count ?? 0) + (data.Page1?.Count ?? 0) + (data.Page2?.Count ?? 0);
        }

        private static bool HasAnyPageItems(BackpackPagesData data)
        {
            if (data == null) return false;
            return (data.Page0?.Count ?? 0) > 0 || (data.Page1?.Count ?? 0) > 0 || (data.Page2?.Count ?? 0) > 0;
        }

        /// <summary>
        /// Parse Oxide Backpacks JSON (OwnerID, Items array with Position/ID/Amount/etc.) into our BackpackPagesData.
        /// Oxide uses flat Position; we split by slotsPerPage into pages.
        /// </summary>
        private static BackpackPagesData TryLoadFromOxideFormat(string path, int slotsPerPage)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path) || slotsPerPage < 1) return null;
            try
            {
                var json = File.ReadAllText(path);
                var jobj = JObject.Parse(json);
                var itemsToken = jobj["Items"];
                if (itemsToken == null || itemsToken.Type != JTokenType.Array) return null;
                var items = (JArray)itemsToken;
                var perPage = new Dictionary<int, List<BackpackItemEntry>>();
                foreach (var itemToken in items)
                {
                    if (itemToken is not JObject item) continue;
                    var entry = ParseOxideItem(item);
                    if (entry == null) continue;
                    int position = entry.Slot;
                    int pageIndex = position / slotsPerPage;
                    int slotInPage = position % slotsPerPage;
                    entry.Slot = slotInPage;
                    if (!perPage.TryGetValue(pageIndex, out var list))
                    {
                        list = new List<BackpackItemEntry>();
                        perPage[pageIndex] = list;
                    }
                    list.Add(entry);
                }
                var data = new BackpackPagesData();
                foreach (var kv in perPage.OrderBy(k => k.Key))
                {
                    if (kv.Key >= 0 && kv.Key <= 2) data.SetPage(kv.Key, kv.Value);
                }
                return data;
            }
            catch { return null; }
        }

        private static BackpackItemEntry ParseOxideItem(JObject item)
        {
            var idToken = item["ID"] ?? item["itemid"];
            if (idToken == null) return null;
            var entry = new BackpackItemEntry
            {
                ItemId = idToken.Value<int>(),
                Amount = (item["Amount"] ?? item["amount"])?.Value<int>() ?? 1,
                Slot = (item["Position"] ?? item["position"] ?? item["slot"])?.Value<int>() ?? 0,
                Condition = (item["Condition"] ?? item["condition"])?.Value<float>() ?? 100f,
                MaxCondition = (item["MaxCondition"] ?? item["maxCondition"])?.Value<float>() ?? 100f,
                Skin = (item["Skin"] ?? item["skin"])?.Value<ulong>() ?? 0,
                Blueprint = (item["Blueprint"] ?? item["blueprint"])?.Value<bool>() ?? ((item["BlueprintTarget"] ?? item["blueprintTarget"])?.Value<int>() ?? 0) > 0
            };
            var contentsToken = item["Contents"];
            if (contentsToken is JArray contentsArr && contentsArr.Count > 0)
            {
                entry.Contents = new List<BackpackItemEntry>();
                foreach (var c in contentsArr.OfType<JObject>())
                {
                    var sub = ParseOxideItem(c);
                    if (sub != null) entry.Contents.Add(sub);
                }
            }
            return entry;
        }

        private static void SaveBackpackPagesData(ulong userId, BackpackPagesData data)
        {
            if (data == null) return;
            var path = GetDataPath(userId);
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[Backpacks] Save pages error: " + ex.Message);
            }
        }

        private static void SaveBackpackPage(ulong userId, int pageIndex, ItemContainer container)
        {
            if (container == null) return;
            var path = GetDataPath(userId);
            try
            {
                var data = File.Exists(path) ? LoadBackpackPagesData(userId) : new BackpackPagesData();
                data.SetPage(pageIndex, SerializeContainer(container));
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[Backpacks] Save page error: " + ex.Message);
            }
        }

        private static void SaveBackpackData(ulong userId, List<BackpackItemEntry> entries)
        {
            var path = GetDataPath(userId);
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonConvert.SerializeObject(entries ?? new List<BackpackItemEntry>(), Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[Backpacks] Save error: " + ex.Message);
            }
        }

        private static List<BackpackItemEntry> SerializeContainer(ItemContainer container)
        {
            var list = new List<BackpackItemEntry>();
            if (container?.itemList == null) return list;
            foreach (var item in container.itemList)
            {
                if (item?.info == null) continue;
                var e = new BackpackItemEntry
                {
                    ItemId = item.info.itemid,
                    Amount = item.amount,
                    Slot = item.position,
                    Condition = item.condition,
                    MaxCondition = item.maxCondition,
                    Blueprint = item.IsBlueprint(),
                    Skin = item.skin
                };
                if (item.contents?.itemList != null && item.contents.itemList.Count > 0)
                {
                    e.Contents = new List<BackpackItemEntry>();
                    foreach (var sub in item.contents.itemList)
                    {
                        if (sub?.info == null) continue;
                        e.Contents.Add(new BackpackItemEntry
                        {
                            ItemId = sub.info.itemid,
                            Amount = sub.amount,
                            Slot = sub.position,
                            Condition = sub.condition,
                            MaxCondition = sub.maxCondition,
                            Blueprint = sub.IsBlueprint(),
                            Skin = sub.skin
                        });
                    }
                }
                list.Add(e);
            }
            return list;
        }

        private static List<Item> DeserializeToItems(List<BackpackItemEntry> entries)
        {
            var list = new List<Item>();
            if (entries == null) return list;
            foreach (var e in entries.OrderBy(x => x.Slot))
            {
                var def = ItemManager.FindItemDefinition(e.ItemId);
                if (def == null) continue;
                var item = ItemManager.Create(def, e.Amount, e.Skin);
                if (item == null) continue;
                item.condition = Mathf.Clamp(e.Condition, 0, e.MaxCondition > 0 ? e.MaxCondition : 100f);
                item.maxCondition = e.MaxCondition > 0 ? e.MaxCondition : 100f;
                if (e.Blueprint) item.blueprintTarget = def.itemid;
                if (e.Contents != null && e.Contents.Count > 0 && item.contents != null)
                {
                    foreach (var sub in e.Contents)
                    {
                        var subDef = ItemManager.FindItemDefinition(sub.ItemId);
                        if (subDef == null) continue;
                        var subItem = ItemManager.Create(subDef, sub.Amount, sub.Skin);
                        if (subItem != null)
                        {
                            subItem.condition = Mathf.Clamp(sub.Condition, 0, sub.MaxCondition > 0 ? sub.MaxCondition : 100f);
                            subItem.maxCondition = sub.MaxCondition > 0 ? sub.MaxCondition : 100f;
                            if (sub.Blueprint) subItem.blueprintTarget = subDef.itemid;
                            subItem.MoveToContainer(item.contents);
                        }
                    }
                }
                list.Add(item);
            }
            return list;
        }

        private static void NextTick(Action action)
        {
            if (ServerMgr.Instance == null) { try { action?.Invoke(); } catch { } return; }
            ServerMgr.Instance.StartCoroutine(NextTickCoroutine(action));
        }

        private static IEnumerator NextTickCoroutine(Action action)
        {
            yield return null;
            try { action?.Invoke(); } catch (Exception ex) { UnityEngine.Debug.LogWarning("[Backpacks] NextTick error: " + ex.Message); }
        }
    }
}
