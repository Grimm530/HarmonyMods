using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Facepunch;
using Newtonsoft.Json;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace LootQoLHarmony
{
    /// <summary>
    /// Oxide SortButton 2.8.0 port (no Oxide runtime). Overlay sort CUI on supported storage.
    /// </summary>
    public sealed class SortButtonFeature
    {
        public const string PermUse = "sortbutton.use";
        private const string GUIPanelName = "UISortButton";
        private const int MaxRows = 8;
        private const float BaseYOffset = 113.5f;
        private const float YOffsetPerRow = 62f;
        private const float SortButtonWidth = 79f;
        private const float SortOrderButtonWidth = 17f;
        private const string ButtonHeightString = "23";

        private readonly LootQoLPlugin _plugin;
        private readonly string _dataPath;
        private readonly string _standaloneConfigPath;
        private readonly string[] _legacyDataPaths;

        private readonly Dictionary<string, string> OffsetYByLootPanel = new Dictionary<string, string>
        {
            ["dropboxcontents"] = (BaseYOffset + YOffsetPerRow * 2).ToString(CultureInfo.InvariantCulture),
            ["furnace"] = "277",
            ["generic"] = (BaseYOffset + YOffsetPerRow * 6).ToString(CultureInfo.InvariantCulture),
            ["genericsmall"] = (BaseYOffset + YOffsetPerRow).ToString(CultureInfo.InvariantCulture),
            ["largefurnace"] = "395",
            ["toolcupboard"] = "595",
            ["vendingmachine.storage"] = (BaseYOffset + YOffsetPerRow * 5).ToString(CultureInfo.InvariantCulture),
        };

        private readonly string[] OffsetYByRow = new string[MaxRows];
        private readonly Dictionary<string, string> HeightOverrideByLootPanel = new Dictionary<string, string>
        {
            ["animal-storage"] = "21",
            ["dropboxcontents"] = "21",
            ["furnace"] = "21",
            ["largefurnace"] = "21",
            ["toolcupboard"] = "21.5",
            ["vendingmachine.storage"] = "21",
        };

        private int[] _itemCategoryToSortIndex;
        private PlayerData _defaultPlayerData;
        private string _cachedUI;
        private readonly string[] _uiArguments = new string[6];
        private readonly HashSet<ulong> _uiViewers = new HashSet<ulong>();
        private readonly Dictionary<ulong, int> _lootSession = new Dictionary<ulong, int>();
        private StoredData _storedData = new StoredData();
        private readonly HashSet<string> _chatCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private Settings Config => _plugin.Config?.SortButton;

        public SortButtonFeature(LootQoLPlugin plugin, string serverRoot)
        {
            _plugin = plugin;
            _dataPath = Path.Combine(serverRoot, "HarmonyData", "LootQoL", "SortButton.json");
            _standaloneConfigPath = Path.Combine(serverRoot, "HarmonyConfig", "SortButton.json");
            _legacyDataPaths = new[]
            {
                Path.Combine(serverRoot, "HarmonyData", "LootQoL", "SortButton.json"),
                Path.Combine(serverRoot, "HarmonyData", "SortButton", "SortButton.json"),
                Path.Combine(serverRoot, "HarmonyData", "SortButton.json"),
            };
        }

        public Settings TryLoadStandaloneConfig()
        {
            if (!File.Exists(_standaloneConfigPath))
                return null;
            try
            {
                var loaded = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(_standaloneConfigPath));
                if (loaded?.ContainersByPrefabPath != null && loaded.ContainersByPrefabPath.Count > 0)
                {
                    Debug.Log("[LootQoL] Migrated HarmonyConfig/SortButton.json into LootQoL SortButton section.");
                    return loaded;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LootQoL] SortButton standalone config migrate failed: " + ex.Message);
            }
            return null;
        }

        public void Load()
        {
            LoadData();
            SetupItemCategories();
            Settings cfg = Config ?? new Settings();
            _defaultPlayerData = new PlayerData
            {
                Enabled = cfg.DefaultEnabled,
                SortByCategory = cfg.DefaultSortByCategory,
            };
            for (int i = 0; i < MaxRows; i++)
                OffsetYByRow[i] = (BaseYOffset + YOffsetPerRow * (i + 1)).ToString(CultureInfo.InvariantCulture);
            RefreshChatCommands();
        }

        public void RefreshChatCommands()
        {
            _chatCommands.Clear();
            List<string> commands = Config?.Commands;
            if (commands == null || commands.Count == 0)
            {
                _chatCommands.Add("sortbutton");
                return;
            }
            for (int i = 0; i < commands.Count; i++)
            {
                string cmd = commands[i];
                if (!string.IsNullOrWhiteSpace(cmd))
                    _chatCommands.Add(cmd.Trim());
            }
        }

        public void OnServerInitialized()
        {
            Config?.OnServerInitialized(_plugin);
            RefreshChatCommands();
        }

        public void Unload()
        {
            var list = BasePlayer.activePlayerList;
            for (int i = 0; i < list.Count; i++)
                DestroyUi(list[i]);
            _uiViewers.Clear();
            _lootSession.Clear();
        }

        public void OnLootEntity(BasePlayer player, BaseEntity entity) =>
            HandleOnLootEntity(player, entity, delay: true);

        public void OnLootEntityEnd(BasePlayer player)
        {
            if (player == null)
                return;
            InvalidateLootSession(GetUserId(player));
            DestroyUi(player);
        }

        public bool TryHandleChat(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrEmpty(message) || Config == null || !Config.Enabled)
                return false;

            string text = message.Trim();
            if (text.StartsWith("/") || text.StartsWith("\\"))
                text = text.Substring(1).Trim();
            if (text.Length == 0)
                return false;

            string[] parts = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !_chatCommands.Contains(parts[0]))
                return false;

            string[] args = parts.Length > 1 ? new string[parts.Length - 1] : Array.Empty<string>();
            for (int i = 1; i < parts.Length; i++)
                args[i - 1] = parts[i];
            CmdSortButton(player, parts[0], args);
            return true;
        }

        public void HandleCui(BasePlayer player, string action)
        {
            if (player == null || Config == null || !Config.Enabled)
                return;
            if (string.Equals(action, "order", StringComparison.OrdinalIgnoreCase))
                Command_SortType(player);
            else if (string.Equals(action, "sort", StringComparison.OrdinalIgnoreCase))
                Command_Sort(player);
        }

        private void CmdSortButton(BasePlayer basePlayer, string cmd, string[] args)
        {
            if (!PermissionsBridge.UserHasPermission(basePlayer.UserIDString, PermUse))
            {
                PlayerSendMessage(basePlayer, Lang("Error.NoPermission"));
                return;
            }

            PlayerData playerData = GetPlayerData(GetUserId(basePlayer), createIfMissing: true);
            if (args == null || args.Length == 0)
            {
                playerData.Enabled = !playerData.Enabled;
                SaveData();
                string status = playerData.Enabled ? Lang("Format.Enabled") : Lang("Format.Disabled");
                PlayerSendMessage(basePlayer, string.Format(Lang("Info.ButtonStatus"), status));
                return;
            }

            string arg0 = args[0].ToLowerInvariant();
            if (arg0 == "sort" || arg0 == "type")
            {
                playerData.SortByCategory = !playerData.SortByCategory;
                SaveData();
                string typeName = playerData.SortByCategory ? Lang("Format.Category") : Lang("Format.Name");
                PlayerSendMessage(basePlayer, string.Format(Lang("Info.SortType"), typeName));
                return;
            }

            string commandName = Config.Commands != null && Config.Commands.Count > 0 ? Config.Commands[0] : "sortbutton";
            PlayerSendMessage(basePlayer, string.Format(Lang("Info.Help"), commandName));
        }

        private void Command_SortType(BasePlayer basePlayer)
        {
            if (!PermissionsBridge.UserHasPermission(basePlayer.UserIDString, PermUse))
                return;
            PlayerData playerData = GetPlayerData(GetUserId(basePlayer), createIfMissing: true);
            playerData.SortByCategory = !playerData.SortByCategory;
            SaveData();
            RecreateSortButton(basePlayer);
        }

        private void Command_Sort(BasePlayer basePlayer)
        {
            if (!PermissionsBridge.UserHasPermission(basePlayer.UserIDString, PermUse))
                return;
            var loot = basePlayer.inventory?.loot;
            if (loot?.containers == null || loot.containers.Count != 1)
                return;
            BaseEntity entitySource = loot.entitySource;
            ContainerConfiguration containerConfiguration = Config.GetContainerConfiguration(entitySource);
            if (containerConfiguration == null || !containerConfiguration.Enabled)
                return;
            if (!CanPlayerSortEntity(basePlayer, entitySource))
                return;
            PlayerData playerData = GetPlayerData(GetUserId(basePlayer));
            if (!playerData.Enabled)
                return;
            ulong ownerID = entitySource.OwnerID;
            if (Config.CheckOwnership && ownerID != 0 && !IsAlly(GetUserId(basePlayer), ownerID))
                return;

            var containers = loot.containers;
            for (int i = 0; i < containers.Count; i++)
            {
                ItemContainer container = containers[i];
                if (!IsSortableContainer(container))
                    continue;
                SortContainer(container, basePlayer, playerData.SortByCategory);
            }
        }

        private void SetupItemCategories()
        {
            string[] names = Enum.GetNames(typeof(ItemCategory));
            var values = (ItemCategory[])Enum.GetValues(typeof(ItemCategory));
            int[] indices = new int[values.Length];
            for (int i = 0; i < values.Length; i++)
                indices[i] = i;
            Array.Sort(names, indices, StringComparer.Ordinal);
            int max = 0;
            for (int i = 0; i < values.Length; i++)
            {
                int v = (int)values[i];
                if (v > max) max = v;
            }
            _itemCategoryToSortIndex = new int[max + 1];
            for (int i = 0; i < values.Length; i++)
                _itemCategoryToSortIndex[(int)values[indices[i]]] = i;
        }

        private bool IsAlly(ulong playerId, ulong targetId)
        {
            if (playerId == targetId || IsOnSameTeam(playerId, targetId))
                return true;
            return IsClanMemberOrAlly(playerId, targetId) || IsFriend(playerId, targetId);
        }

        private bool IsClanMemberOrAlly(ulong playerId, ulong targetId)
        {
            if (!Config.UseClans)
                return false;
            return TryCallSocial("Clans_ApiType", "IsMemberOrAlly",
                new[] { "ClansHarmony.ClansMod", "Clans", "Oxide.Plugins.Clans" },
                playerId, targetId);
        }

        private bool IsFriend(ulong playerId, ulong targetId)
        {
            if (!Config.UseFriends)
                return false;
            // Friends plugins typically take (owner, requester) as (target, player).
            return TryCallSocial("Friends_ApiType", "HasFriend",
                new[] { "FriendsHarmony.FriendsMod", "Friends", "Oxide.Plugins.Friends" },
                targetId, playerId);
        }

        private static bool TryCallSocial(string dataKey, string method, string[] typeNames, ulong a, ulong b)
        {
            try
            {
                Type type = AppDomain.CurrentDomain.GetData(dataKey) as Type;
                if (type == null)
                    type = ResolveType(typeNames);
                if (type == null)
                    return false;

                MethodInfo mi = type.GetMethod(method, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance, null,
                    new[] { typeof(ulong), typeof(ulong) }, null)
                    ?? type.GetMethod(method, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance, null,
                        new[] { typeof(string), typeof(string) }, null);
                if (mi == null)
                    return false;

                object target = mi.IsStatic ? null : type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                object[] args = mi.GetParameters()[0].ParameterType == typeof(string)
                    ? new object[] { a.ToString(), b.ToString() }
                    : new object[] { a, b };
                object result = mi.Invoke(target, args);
                return result is bool ok && ok;
            }
            catch
            {
                return false;
            }
        }

        private static Type ResolveType(string[] typeNames)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                for (int n = 0; n < typeNames.Length; n++)
                {
                    try
                    {
                        Type t = assemblies[i].GetType(typeNames[n], false);
                        if (t != null)
                            return t;
                    }
                    catch { }
                }
            }
            return null;
        }

        private bool IsOnSameTeam(ulong playerId, ulong targetId)
        {
            if (!Config.UseTeams)
                return false;
            RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance?.FindPlayersTeam(playerId);
            if (playerTeam?.members == null)
                return false;
            for (int i = 0; i < playerTeam.members.Count; i++)
            {
                if (playerTeam.members[i] == targetId)
                    return true;
            }
            return false;
        }

        private void PlayerSendMessage(BasePlayer player, string message)
        {
            message = Lang("Format.Prefix") + message;
            player.SendConsoleCommand("chat.add", 2, Config.SteamIDIcon, message);
        }

        private static T GetChildEntity<T>(BaseEntity entity) where T : BaseEntity
        {
            if (entity?.children == null)
                return null;
            for (int i = 0; i < entity.children.Count; i++)
            {
                if (entity.children[i] is T childOfType)
                    return childOfType;
            }
            return null;
        }

        private static bool HasIndustrialAdaptor(BaseEntity entity, out IndustrialStorageAdaptor adaptor)
        {
            adaptor = null;
            if (entity is not StorageContainer storageContainer)
                return false;
            if (!storageContainer.allowSorting)
                return false;
            adaptor = GetChildEntity<IndustrialStorageAdaptor>(storageContainer);
            return adaptor != null;
        }

        private int BumpLootSession(ulong userId)
        {
            _lootSession.TryGetValue(userId, out int session);
            session++;
            _lootSession[userId] = session;
            return session;
        }

        private void InvalidateLootSession(ulong userId) => BumpLootSession(userId);

        private bool IsPendingLootSession(ulong userId, int session)
        {
            return _lootSession.TryGetValue(userId, out int current) && current == session;
        }

        private static bool StillLootingEntity(BasePlayer player, BaseEntity entity)
        {
            if (player == null || player.IsDestroyed || !player.IsConnected || entity == null || entity.IsDestroyed)
                return false;
            var loot = player.inventory?.loot;
            if (loot == null || loot.entitySource != entity || !loot.IsLooting())
                return false;
            return loot.containers != null && loot.containers.Count == 1;
        }

        private void HandleOnLootEntityDelayed(BasePlayer basePlayer, BaseEntity entity, string offsetXString, bool sortByCategory)
        {
            if (!StillLootingEntity(basePlayer, entity))
            {
                DestroyUi(basePlayer);
                return;
            }
            var loot = basePlayer.inventory.loot;
            ItemContainer container = loot.containers[0];
            if (entity is RidableHorse horse && container != horse.storageInventory)
                return;
            string lootPanelName = DetermineLootPanelName(entity);
            if (!TryDetermineYOffset(container, lootPanelName, out string offsetYString))
                return;
            if (!HeightOverrideByLootPanel.TryGetValue(lootPanelName, out string heightString))
                heightString = ButtonHeightString;
            CreateButtonUI(basePlayer, offsetXString, offsetYString, heightString, sortByCategory);
        }

        private void HandleOnLootEntity(BasePlayer basePlayer, BaseEntity entity, bool delay)
        {
            if (basePlayer == null || entity == null || Config == null || !Config.Enabled)
                return;
            if (!PermissionsBridge.UserHasPermission(basePlayer.UserIDString, PermUse))
                return;
            ContainerConfiguration containerConfiguration = Config.GetContainerConfiguration(entity);
            if (containerConfiguration == null || !containerConfiguration.Enabled)
                return;
            if (!CanPlayerSortEntity(basePlayer, entity))
                return;
            PlayerData playerData = GetPlayerData(GetUserId(basePlayer));
            if (!playerData.Enabled)
                return;
            ulong ownerID = entity.OwnerID;
            if (Config.CheckOwnership && ownerID != 0 && !IsAlly(GetUserId(basePlayer), ownerID))
                return;
            bool industrialSortingEnabled = HasIndustrialAdaptor(entity, out IndustrialStorageAdaptor adaptor);
            if (industrialSortingEnabled && adaptor.IsPowered())
                return;
            string offsetXString = containerConfiguration.GetOffsetXString(industrialSortingEnabled);
            bool sortByCategory = playerData.SortByCategory;
            ulong userId = GetUserId(basePlayer);
            int session = BumpLootSession(userId);
            if (delay)
            {
                LootQoLMod.Instance?.NextTick(() =>
                {
                    if (!IsPendingLootSession(userId, session))
                        return;
                    HandleOnLootEntityDelayed(basePlayer, entity, offsetXString, sortByCategory);
                });
            }
            else if (IsPendingLootSession(userId, session))
                HandleOnLootEntityDelayed(basePlayer, entity, offsetXString, sortByCategory);
        }

        private static bool IsSortableContainer(ItemContainer container)
        {
            if (container == null)
                return false;
            if (container.IsLocked() || container.PlayerItemInputBlocked()
                || container.HasFlag(ItemContainer.Flag.IsPlayer) || container.capacity <= 1)
                return false;
            return true;
        }

        private static bool CanPlayerSortEntity(BasePlayer basePlayer, BaseEntity entity)
        {
            if (entity == null || entity.IsDestroyed)
                return false;
            if (entity is DropBox dropBox)
                return dropBox.PlayerBehind(basePlayer);
            if (entity is VendingMachine vendingMachine)
                return vendingMachine.PlayerBehind(basePlayer);
            return true;
        }

        private static string DetermineLootPanelName(BaseEntity entity)
        {
            if (entity is Mailbox mailbox)
                return mailbox.ownerPanel;
            if (entity is StorageContainer storageContainer)
                return storageContainer.panelName;
            if (entity is RidableHorse horse)
                return horse.storagePanelName;
            return "generic_resizable";
        }

        private bool TryDetermineYOffset(ItemContainer container, string lootPanelName, out string offsetYString)
        {
            if (lootPanelName == "generic_resizable" || lootPanelName == "animal-storage")
            {
                int numRows = Math.Min(1 + (container.capacity - 1) / 6, MaxRows);
                offsetYString = OffsetYByRow[numRows - 1];
                return true;
            }
            return OffsetYByLootPanel.TryGetValue(lootPanelName, out offsetYString);
        }

        private int CompareItems(Item a, Item b, bool byCategory)
        {
            if (byCategory)
            {
                int categoryIndex = CategorySortIndex(a);
                int otherCategoryIndex = CategorySortIndex(b);
                int categoryComparison = categoryIndex.CompareTo(otherCategoryIndex);
                if (categoryComparison != 0)
                    return categoryComparison;
            }
            int nameComparison = string.Compare(a.info.displayName.translated, b.info.displayName.translated, StringComparison.Ordinal);
            if (nameComparison != 0)
                return nameComparison;
            return a.amount.CompareTo(b.amount);
        }

        private int CategorySortIndex(Item item)
        {
            int cat = (int)item.info.category;
            if (_itemCategoryToSortIndex == null || cat < 0 || cat >= _itemCategoryToSortIndex.Length)
                return cat;
            return _itemCategoryToSortIndex[cat];
        }

        private void SortContainer(ItemContainer container, BasePlayer initiator, bool byCategory)
        {
            List<Item> itemList = Pool.Get<List<Item>>();
            if (container.entityOwner is BuildingPrivlidge)
            {
                for (int i = container.itemList.Count - 1; i >= 0; i--)
                {
                    Item item = container.itemList[i];
                    if (item.position >= 24)
                        continue;
                    item.RemoveFromContainer();
                    itemList.Add(item);
                }
            }
            else
            {
                for (int i = container.itemList.Count - 1; i >= 0; i--)
                {
                    Item item = container.itemList[i];
                    item.RemoveFromContainer();
                    itemList.Add(item);
                }
            }

            itemList.Sort((a, b) => CompareItems(a, b, byCategory));
            for (int i = 0; i < itemList.Count; i++)
            {
                Item item = itemList[i];
                if (!item.MoveToContainer(container))
                    initiator.GiveItem(item);
            }
            Pool.FreeUnmanaged(ref itemList);
        }

        private void CreateButtonUI(BasePlayer player, string offsetXString, string offsetYString, string heightString, bool sortByCategory)
        {
            if (player == null || player.net == null)
                return;
            ulong userId = GetUserId(player);
            CuiHelper.DestroyUi(player, GUIPanelName);
            _uiViewers.Add(userId);
            if (_cachedUI == null)
            {
                var elements = new CuiElementContainer();
                elements.Add(new CuiPanel
                {
                    Image = { Color = "0 0 0 0" },
                    RectTransform =
                    {
                        AnchorMin = "0.5 0",
                        AnchorMax = "0.5 0",
                        OffsetMin = "{0} {1}",
                        OffsetMax = "{0} {1}",
                    },
                    CursorEnabled = false,
                }, "Overlay", GUIPanelName, GUIPanelName);

                elements.Add(new CuiButton
                {
                    Button = { Command = "cui.endtest LOOTQOL order", Color = "{2}" },
                    RectTransform =
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "0 0",
                        OffsetMin = "0 0",
                        OffsetMax = SortOrderButtonWidth.ToString(CultureInfo.InvariantCulture) + " {3}",
                    },
                    Text =
                    {
                        Text = "{4}",
                        FontSize = 12,
                        Align = TextAnchor.MiddleCenter,
                        Color = "0.77 0.92 0.67 0.8",
                    },
                }, GUIPanelName);

                elements.Add(new CuiButton
                {
                    Button = { Command = "cui.endtest LOOTQOL sort", Color = "0.41 0.50 0.25 0.8" },
                    RectTransform =
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "0 0",
                        OffsetMin = SortOrderButtonWidth.ToString(CultureInfo.InvariantCulture) + " 0",
                        OffsetMax = (SortOrderButtonWidth + SortButtonWidth).ToString(CultureInfo.InvariantCulture) + " {3}",
                    },
                    Text =
                    {
                        Text = "{5}",
                        FontSize = 12,
                        Align = TextAnchor.MiddleCenter,
                        Color = "0.77 0.92 0.67 0.8",
                    },
                }, GUIPanelName);

                _cachedUI = CuiHelper.ToJson(elements);
                _cachedUI = _cachedUI.Replace("{", "{{").Replace("}", "}}");
                for (int i = 0; i < _uiArguments.Length; i++)
                    _cachedUI = _cachedUI.Replace("{{" + i + "}}", "{" + i + "}");
            }

            _uiArguments[0] = offsetXString;
            _uiArguments[1] = offsetYString;
            _uiArguments[2] = sortByCategory ? "0.75 0.43 0.18 0.8" : "0.26 0.58 0.80 0.8";
            _uiArguments[3] = heightString;
            _uiArguments[4] = sortByCategory ? "C" : "N";
            _uiArguments[5] = Lang("Format.ButtonText");
            CuiHelper.AddUi(player, string.Format(_cachedUI, _uiArguments));
        }

        private void RecreateSortButton(BasePlayer player)
        {
            DestroyUi(player);
            BaseEntity source = player.inventory?.loot?.entitySource;
            if (source != null)
                HandleOnLootEntity(player, source, delay: false);
        }

        private void DestroyUi(BasePlayer player)
        {
            if (player == null)
                return;
            _uiViewers.Remove(GetUserId(player));
            CuiHelper.DestroyUi(player, GUIPanelName);
        }

        private class StoredData
        {
            public Dictionary<ulong, PlayerData> PlayerData = new Dictionary<ulong, PlayerData>();
        }

        private class PlayerData
        {
            public bool Enabled;
            public bool SortByCategory;
        }

        private PlayerData GetPlayerData(ulong userID, bool createIfMissing = false)
        {
            if (_storedData.PlayerData != null && _storedData.PlayerData.TryGetValue(userID, out PlayerData playerData) && playerData != null)
                return playerData;
            if (createIfMissing)
            {
                playerData = new PlayerData
                {
                    Enabled = Config.DefaultEnabled,
                    SortByCategory = Config.DefaultSortByCategory,
                };
                _storedData.PlayerData[userID] = playerData;
                return playerData;
            }
            return _defaultPlayerData;
        }

        private void LoadData()
        {
            _storedData = new StoredData();
            string path = null;
            for (int i = 0; i < _legacyDataPaths.Length; i++)
            {
                if (File.Exists(_legacyDataPaths[i]))
                {
                    path = _legacyDataPaths[i];
                    break;
                }
            }
            if (path == null)
                return;
            try
            {
                var loaded = JsonConvert.DeserializeObject<StoredData>(File.ReadAllText(path));
                if (loaded?.PlayerData != null)
                    _storedData = loaded;
            }
            catch
            {
                _storedData = new StoredData();
            }
        }

        private void SaveData()
        {
            try
            {
                string dir = Path.GetDirectoryName(_dataPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(_dataPath, JsonConvert.SerializeObject(_storedData, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LootQoL] SortButton SaveData: " + ex.Message);
            }
        }

        private string Lang(string key)
        {
            return _plugin.GetMessage(key);
        }

        private static ulong GetUserId(BasePlayer player)
        {
            if (player == null)
                return 0UL;
            return player.userID.Get();
        }

        public class ContainerConfiguration
        {
            private const float DefaultOffsetX = 476.5f;
            private const float XOffsetForIndustrialAdapter = 49f;
            private const float MaxOffsetXForIndustrialAdapterAdjustment = DefaultOffsetX - XOffsetForIndustrialAdapter;

            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("OffsetX")]
            public float OffsetX = DefaultOffsetX;

            [JsonIgnore] private string _offsetXString;
            [JsonIgnore] private string _offsetXStringForIndustrialAdapter;

            public string GetOffsetXString(bool industrialSortingEnabled)
            {
                if (industrialSortingEnabled && OffsetX >= MaxOffsetXForIndustrialAdapterAdjustment)
                    return _offsetXStringForIndustrialAdapter ??= MaxOffsetXForIndustrialAdapterAdjustment.ToString(CultureInfo.InvariantCulture);
                return _offsetXString ??= OffsetX.ToString(CultureInfo.InvariantCulture);
            }
        }

        public class Settings
        {
            private static readonly HashSet<string> OldRemovedPrefabs = new HashSet<string>
            {
                "assets/rust.ai/nextai/testridablehorse.prefab",
                "assets/content/vehicles/horse/ridablehorse2.prefab",
                "assets/content/vehicles/horse/_old/testridablehorse.prefab",
            };

            [JsonIgnore]
            public bool UsingDefaults;

            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Default enabled")]
            public bool DefaultEnabled = true;

            [JsonProperty("Default sort by category")]
            public bool DefaultSortByCategory = true;

            [JsonProperty("Check ownership")]
            public bool CheckOwnership = true;

            [JsonProperty("Use Clans")]
            public bool UseClans = true;

            [JsonProperty("Use Friends")]
            public bool UseFriends = true;

            [JsonProperty("Use Teams")]
            public bool UseTeams = true;

            [JsonProperty("Chat steamID icon")]
            public ulong SteamIDIcon;

            [JsonProperty("Chat command", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Commands = new List<string> { "sortbutton" };

            [JsonProperty("Containers by short prefab name", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, ContainerConfiguration> ContainersByPrefabPath = CreateDefaultContainers();

            [JsonProperty("Containers by skin ID")]
            public Dictionary<ulong, ContainerConfiguration> ContainersBySkinId = new Dictionary<ulong, ContainerConfiguration>();

            [JsonIgnore]
            private readonly Dictionary<uint, ContainerConfiguration> ContainersByPrefabId = new Dictionary<uint, ContainerConfiguration>();

            private static Dictionary<string, ContainerConfiguration> CreateDefaultContainers()
            {
                return new Dictionary<string, ContainerConfiguration>
                {
                    ["assets/content/vehicles/boats/rhib/subents/rhib_storage.prefab"] = new ContainerConfiguration(),
                    ["assets/content/vehicles/boats/rowboat/subents/rowboat_storage.prefab"] = new ContainerConfiguration(),
                    ["assets/content/vehicles/horse/ridablehorse.prefab"] = new ContainerConfiguration(),
                    ["assets/content/vehicles/modularcar/subents/modular_car_1mod_storage.prefab"] = new ContainerConfiguration(),
                    ["assets/content/vehicles/modularcar/subents/modular_car_camper_storage.prefab"] = new ContainerConfiguration(),
                    ["assets/content/vehicles/snowmobiles/subents/snowmobileitemstorage.prefab"] = new ContainerConfiguration(),
                    ["assets/content/vehicles/submarine/subents/submarineitemstorage.prefab"] = new ContainerConfiguration(),
                    ["assets/prefabs/deployable/composter/composter.prefab"] = new ContainerConfiguration(),
                    ["assets/prefabs/deployable/dropbox/dropbox.deployed.prefab"] = new ContainerConfiguration(),
                    ["assets/prefabs/deployable/fridge/fridge.deployed.prefab"] = new ContainerConfiguration(),
                    ["assets/prefabs/deployable/hitch & trough/hitchtrough.deployed.prefab"] = new ContainerConfiguration(),
                    ["assets/prefabs/deployable/hot air balloon/subents/hab_storage.prefab"] = new ContainerConfiguration(),
                    ["assets/prefabs/deployable/large wood storage/box.wooden.large.prefab"] = new ContainerConfiguration(),
                    ["assets/prefabs/deployable/large wood storage/skins/abyss_dlc_large_wood_box/abyss_dlc_storage_horizontal/abyss_barrel_horizontal.prefab"] = new ContainerConfiguration(),
                    ["assets/prefabs/deployable/large wood storage/skins/abyss_dlc_large_wood_box/abyss_dlc_storage_vertical/abyss_barrel_vertical.prefab"] = new ContainerConfiguration(),
                    ["assets/prefabs/deployable/large wood storage/skins/jungle_dlc_large_wood_box/jungle_dlc_storage_horizontal/wicker_barrel.prefab"] = new ContainerConfiguration(),
                    ["assets/prefabs/deployable/large wood storage/skins/jungle_dlc_large_wood_box/jungle_dlc_storage_vertical/bamboo_barrel.prefab"] = new ContainerConfiguration(),
                    ["assets/prefabs/deployable/large wood storage/skins/medieval_large_wood_box/medieval.box.wooden.large.prefab"] = new ContainerConfiguration(),
                    ["assets/prefabs/deployable/large wood storage/skins/warhammer_dlc_large_wood_box/krieg_storage_horizontal/krieg_storage_horizontal.prefab"] = new ContainerConfiguration(),
                    ["assets/prefabs/deployable/large wood storage/skins/warhammer_dlc_large_wood_box/krieg_storage_vertical/krieg_storage_vertical.prefab"] = new ContainerConfiguration(),
                    ["assets/prefabs/deployable/minifridge/minifridge.deployed.prefab"] = new ContainerConfiguration(),
                    ["assets/prefabs/deployable/small stash/small_stash_deployed.prefab"] = new ContainerConfiguration(),
                    ["assets/prefabs/deployable/tool cupboard/cupboard.tool.deployed.prefab"] = new ContainerConfiguration(),
                    ["assets/prefabs/deployable/tool cupboard/retro/cupboard.tool.retro.deployed.prefab"] = new ContainerConfiguration(),
                    ["assets/prefabs/deployable/tool cupboard/shockbyte/cupboard.tool.shockbyte.deployed.prefab"] = new ContainerConfiguration(),
                    ["assets/prefabs/deployable/vendingmachine/vendingmachine.deployed.prefab"] = new ContainerConfiguration(),
                    ["assets/prefabs/deployable/wall cabinet/electric.wallcabinet.deployed.prefab"] = new ContainerConfiguration(),
                    ["assets/prefabs/deployable/woodenbox/skins/pilot_hazmat_wooden_box/pilot_hazmat_woodbox_deployed.prefab"] = new ContainerConfiguration(),
                    ["assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab"] = new ContainerConfiguration(),
                    ["assets/prefabs/misc/decor_dlc/storagebarrel/storage_barrel_b.prefab"] = new ContainerConfiguration(),
                    ["assets/prefabs/misc/decor_dlc/storagebarrel/storage_barrel_c.prefab"] = new ContainerConfiguration(),
                    ["assets/prefabs/misc/halloween/coffin/coffinstorage.prefab"] = new ContainerConfiguration(),
                };
            }

            public void OnServerInitialized(LootQoLPlugin plugin)
            {
                if (ContainersByPrefabPath == null)
                    ContainersByPrefabPath = new Dictionary<string, ContainerConfiguration>();
                if (ContainersBySkinId == null)
                    ContainersBySkinId = new Dictionary<ulong, ContainerConfiguration>();
                if (Commands == null || Commands.Count == 0)
                    Commands = new List<string> { "sortbutton" };

                List<string> addedPrefabs = null;
                List<string> prefabsToRemove = null;

                DiscoverPrefabs(DiscoverDeployableStoragePrefabs(), ref addedPrefabs);
                DiscoverPrefabs(DiscoverBoxStoragePrefabs(), ref addedPrefabs);

                foreach (var kvp in ContainersByPrefabPath)
                {
                    string prefabPath = kvp.Key;
                    ContainerConfiguration containerConfig = kvp.Value;
                    BaseEntity baseEntity = GameManager.server.FindPrefab(prefabPath)?.GetComponent<BaseEntity>();
                    if (baseEntity == null)
                    {
                        if (OldRemovedPrefabs.Contains(prefabPath))
                        {
                            prefabsToRemove ??= new List<string>();
                            prefabsToRemove.Add(prefabPath);
                        }
                        else
                            Debug.LogWarning("[LootQoL] SortButton invalid prefab in configuration: " + prefabPath);
                        continue;
                    }
                    ContainersByPrefabId[baseEntity.prefabID] = containerConfig;
                }

                if (prefabsToRemove != null)
                {
                    for (int i = 0; i < prefabsToRemove.Count; i++)
                        ContainersByPrefabPath.Remove(prefabsToRemove[i]);
                }

                if (!UsingDefaults && ((prefabsToRemove != null && prefabsToRemove.Count > 0) || (addedPrefabs != null && addedPrefabs.Count > 0)))
                {
                    plugin.SaveConfig();
                    if (addedPrefabs != null && addedPrefabs.Count > 0)
                        Debug.LogWarning("[LootQoL] SortButton discovered and added " + addedPrefabs.Count + " storage entity prefabs to configuration.");
                }
            }

            private void DiscoverPrefabs(List<string> prefabList, ref List<string> addedPrefabs)
            {
                if (prefabList == null)
                    return;
                for (int i = 0; i < prefabList.Count; i++)
                {
                    string prefabPath = prefabList[i];
                    if (string.IsNullOrEmpty(prefabPath) || ContainersByPrefabPath.ContainsKey(prefabPath))
                        continue;
                    ContainersByPrefabPath[prefabPath] = new ContainerConfiguration();
                    addedPrefabs ??= new List<string>();
                    addedPrefabs.Add(prefabPath);
                }
            }

            public ContainerConfiguration GetContainerConfiguration(BaseEntity entity)
            {
                if (entity == null)
                    return null;
                ContainerConfiguration containerConfiguration;
                if (entity.skinID != 0 && ContainersBySkinId != null && ContainersBySkinId.TryGetValue(entity.skinID, out containerConfiguration))
                    return containerConfiguration;
                if (ContainersByPrefabId.TryGetValue(entity.prefabID, out containerConfiguration))
                    return containerConfiguration;
                return null;
            }

            private static List<string> DiscoverDeployableStoragePrefabs()
            {
                var prefabList = new List<string>();
                var itemList = ItemManager.itemList;
                if (itemList == null)
                    return prefabList;
                for (int i = 0; i < itemList.Count; i++)
                {
                    ItemDefinition itemDefinition = itemList[i];
                    if (itemDefinition == null)
                        continue;
                    var itemModDeployable = itemDefinition.GetComponent<ItemModDeployable>();
                    if (itemModDeployable == null)
                        continue;
                    BaseEntity deployableEntity = itemModDeployable.entityPrefab?.GetEntity();
                    if (deployableEntity is not (BoxStorage or BuildingPrivlidge or Fridge))
                        continue;
                    if (deployableEntity.PrefabName.IndexOf("unused", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;
                    prefabList.Add(deployableEntity.PrefabName);
                }
                return prefabList;
            }

            private static List<string> DiscoverBoxStoragePrefabs()
            {
                var prefabList = new List<string>();
                string[] entities = GameManifest.Current?.entities;
                if (entities == null)
                    return prefabList;
                for (int i = 0; i < entities.Length; i++)
                {
                    string assetPath = entities[i];
                    if (string.IsNullOrEmpty(assetPath))
                        continue;
                    var entity = GameManager.server.FindPrefab(assetPath)?.GetComponent<BoxStorage>();
                    if (entity == null)
                        continue;
                    string prefabPath = entity.PrefabName;
                    if (prefabPath.IndexOf("unused", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;
                    if (prefabPath.IndexOf("apartment", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    prefabList.Add(prefabPath);
                }
                return prefabList;
            }
        }
    }
}
