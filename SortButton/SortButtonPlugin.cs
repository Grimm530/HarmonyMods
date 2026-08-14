using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Facepunch;
using Newtonsoft.Json;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace SortButton
{
    public class Hash<TKey, TValue> : Dictionary<TKey, TValue>
    {
        public new TValue this[TKey key]
        {
            get => TryGetValue(key, out var value) ? value : default;
            set => base[key] = value;
        }
    }

    public class SortButtonPlugin
    {
        private const string PermissionUse = "sortbutton.use";
        private const string GUIPanelName = "UISortButton";
        private const int MaxRows = 8;
        private const float BaseYOffset = 113.5f;
        private const float YOffsetPerRow = 62;
        private const float SortButtonWidth = 79;
        private const float SortOrderButtonWidthString = 17;
        private const string ButtonHeightString = "23";

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
        private readonly Dictionary<string, string> _lang = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly string _configPath;
        private readonly string _langPath;
        private readonly string _dataPath;
        private Configuration _config;
        private StoredData _storedData;

        public List<string> Commands => _config?.Commands;

        public SortButtonPlugin(string serverRoot)
        {
            _configPath = Path.Combine(serverRoot, "HarmonyConfig", "SortButton.json");
            _langPath = Path.Combine(serverRoot, "HarmonyLanguage", "SortButton.json");
            _dataPath = Path.Combine(serverRoot, "HarmonyData", "SortButton", "SortButton.json");
        }

        public void Load()
        {
            LoadDefaultMessages();
            LoadLangFile();
            LoadConfig();
            LoadData();
            SetupItemCategories();
            _defaultPlayerData = new PlayerData
            {
                Enabled = _config.DefaultEnabled,
                SortByCategory = _config.DefaultSortByCategory,
            };
            for (int i = 0; i < MaxRows; i++)
                OffsetYByRow[i] = (BaseYOffset + YOffsetPerRow * (i + 1)).ToString(CultureInfo.InvariantCulture);
            RegisterPermissions();
        }

        public void RegisterPermissions() => PermissionsBridge.RegisterPermission(PermissionUse);

        public void OnServerInitialized() => _config.OnServerInitialized(this);

        public void Unload()
        {
            var list = BasePlayer.activePlayerList;
            for (int i = 0; i < list.Count; i++)
                DestroyUi(list[i]);
        }

        public void OnLootEntity(BasePlayer basePlayer, BaseEntity entity) =>
            HandleOnLootEntity(basePlayer, entity, delay: true);

        public void OnPlayerLootEnd(BasePlayer player)
        {
            if (player != null) DestroyUi(player);
        }

        public void OnLootEntityEnd(BasePlayer player) => DestroyUi(player);

        public void CmdSortButton(BasePlayer basePlayer, string cmd, string[] args)
        {
            if (!PermissionsBridge.UserHasPermission(basePlayer.UserIDString, PermissionUse))
            {
                PlayerSendMessage(basePlayer, Lang(LangKeys.Error.NoPermission, basePlayer.UserIDString));
                return;
            }

            var playerData = GetPlayerData(GetUserId(basePlayer), createIfMissing: true);
            if (args == null || args.Length == 0)
            {
                playerData.Enabled = !playerData.Enabled;
                SaveData();
                string enabledOrDisabledMessage = playerData.Enabled
                    ? Lang(LangKeys.Format.Enabled, basePlayer.UserIDString)
                    : Lang(LangKeys.Format.Disabled, basePlayer.UserIDString);
                PlayerSendMessage(basePlayer, Lang(LangKeys.Info.ButtonStatus, basePlayer.UserIDString, enabledOrDisabledMessage));
                return;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "sort":
                case "type":
                    playerData.SortByCategory = !playerData.SortByCategory;
                    SaveData();
                    string sortTypeLangKey = playerData.SortByCategory ? LangKeys.Format.Category : LangKeys.Format.Name;
                    PlayerSendMessage(basePlayer, Lang(LangKeys.Info.SortType, basePlayer.UserIDString, Lang(sortTypeLangKey, basePlayer.UserIDString)));
                    return;
            }

            PlayerSendMessage(basePlayer, Lang(LangKeys.Info.Help, basePlayer.UserIDString, _config.Commands[0]));
        }

        public void Command_SortType(BasePlayer basePlayer)
        {
            if (!PermissionsBridge.UserHasPermission(basePlayer.UserIDString, PermissionUse))
                return;
            var playerData = GetPlayerData(GetUserId(basePlayer), createIfMissing: true);
            playerData.SortByCategory = !playerData.SortByCategory;
            SaveData();
            RecreateSortButton(basePlayer);
        }

        public void Command_Sort(BasePlayer basePlayer)
        {
            if (!PermissionsBridge.UserHasPermission(basePlayer.UserIDString, PermissionUse))
                return;
            var containers = basePlayer.inventory.loot.containers;
            if (containers.Count != 1) return;
            var entitySource = basePlayer.inventory.loot.entitySource;
            var containerConfiguration = _config.GetContainerConfiguration(entitySource);
            if (containerConfiguration == null || !containerConfiguration.Enabled) return;
            if (!CanPlayerSortEntity(basePlayer, entitySource)) return;
            var playerData = GetPlayerData(GetUserId(basePlayer));
            if (!playerData.Enabled) return;
            ulong ownerID = entitySource.OwnerID;
            if (_config.CheckOwnership && ownerID != 0 && !IsAlly(GetUserId(basePlayer), ownerID))
                return;
            for (int i = 0; i < containers.Count; i++)
            {
                var container = containers[i];
                if (!IsSortableContainer(container)) continue;
                SortContainer(container, basePlayer, playerData.SortByCategory);
            }
        }

        private void SetupItemCategories()
        {
            var names = Enum.GetNames(typeof(ItemCategory));
            var values = (ItemCategory[])Enum.GetValues(typeof(ItemCategory));
            var indices = new int[values.Length];
            for (int i = 0; i < values.Length; i++)
                indices[i] = i;
            Array.Sort(names, indices, StringComparer.Ordinal);
            _itemCategoryToSortIndex = new int[values.Length];
            for (int i = 0; i < values.Length; i++)
                _itemCategoryToSortIndex[(int)values[indices[i]]] = i;
        }

        private bool IsAlly(ulong playerId, ulong targetId)
        {
            if (playerId == targetId || IsOnSameTeam(playerId, targetId))
                return true;
            return IsClanMemberOrAlly(playerId.ToString(), targetId.ToString())
                || IsFriend(playerId.ToString(), targetId.ToString());
        }

        private bool IsClanMemberOrAlly(string playerId, string targetId)
        {
            if (!_config.UseClans) return false;
            return TryCallPluginApi("Clans_ApiType", "IsMemberOrAlly", playerId, targetId);
        }

        private bool IsFriend(string playerId, string targetId)
        {
            if (!_config.UseFriends) return false;
            return TryCallPluginApi("Friends_ApiType", "HasFriend", targetId, playerId);
        }

        private static bool TryCallPluginApi(string dataKey, string method, string a, string b)
        {
            try
            {
                var type = AppDomain.CurrentDomain.GetData(dataKey) as Type;
                if (type == null) return false;
                var mi = type.GetMethod(method, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
                if (mi == null) return false;
                object target = mi.IsStatic ? null : type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var result = mi.Invoke(target, new object[] { a, b });
                return result is bool ok && ok;
            }
            catch { return false; }
        }

        private bool IsOnSameTeam(ulong playerId, ulong targetId)
        {
            if (!_config.UseTeams) return false;
            var playerTeam = RelationshipManager.ServerInstance.FindPlayersTeam(playerId);
            if (playerTeam?.members == null) return false;
            for (int i = 0; i < playerTeam.members.Count; i++)
            {
                if (playerTeam.members[i] == targetId)
                    return true;
            }
            return false;
        }

        private void PlayerSendMessage(BasePlayer player, string message)
        {
            message = Lang(LangKeys.Format.Prefix, player.UserIDString) + message;
            player.SendConsoleCommand("chat.add", 2, _config.SteamIDIcon, message);
        }

        private static T GetChildEntity<T>(BaseEntity entity) where T : BaseEntity
        {
            if (entity?.children == null) return null;
            for (int i = 0; i < entity.children.Count; i++)
            {
                var childOfType = entity.children[i] as T;
                if (childOfType != null)
                    return childOfType;
            }
            return null;
        }

        private static bool HasIndustrialAdaptor(BaseEntity entity, out IndustrialStorageAdaptor adaptor)
        {
            adaptor = null;
            var storageContainer = entity as StorageContainer;
            if (storageContainer == null) return false;
            if (!storageContainer.allowSorting) return false;
            adaptor = GetChildEntity<IndustrialStorageAdaptor>(storageContainer);
            return adaptor != null;
        }

        private void HandleOnLootEntityDelayed(BasePlayer basePlayer, BaseEntity entity, string offsetXString, bool sortByCategory)
        {
            if (basePlayer.inventory.loot.containers.Count != 1)
                return;
            var container = basePlayer.inventory.loot.containers[0];
            var horse = entity as RidableHorse;
            if (horse != null && container != horse.storageInventory)
                return;
            string lootPanelName = DetermineLootPanelName(entity);
            string offsetYString;
            if (!TryDetermineYOffset(container, lootPanelName, out offsetYString))
                return;
            string heightString;
            if (!HeightOverrideByLootPanel.TryGetValue(lootPanelName, out heightString))
                heightString = ButtonHeightString;
            CreateButtonUI(basePlayer, offsetXString, offsetYString, heightString, sortByCategory);
        }

        private void HandleOnLootEntity(BasePlayer basePlayer, BaseEntity entity, bool delay = true)
        {
            if (basePlayer == null || !PermissionsBridge.UserHasPermission(basePlayer.UserIDString, PermissionUse))
                return;
            var containerConfiguration = _config.GetContainerConfiguration(entity);
            if (containerConfiguration == null || !containerConfiguration.Enabled)
                return;
            if (!CanPlayerSortEntity(basePlayer, entity))
                return;
            var playerData = GetPlayerData(GetUserId(basePlayer));
            if (!playerData.Enabled) return;
            ulong ownerID = entity.OwnerID;
            if (_config.CheckOwnership && ownerID != 0 && !IsAlly(GetUserId(basePlayer), ownerID))
                return;
            IndustrialStorageAdaptor adaptor;
            bool industrialSortingEnabled = HasIndustrialAdaptor(entity, out adaptor);
            if (industrialSortingEnabled && adaptor.IsPowered())
                return;
            string offsetXString = containerConfiguration.GetOffsetXString(industrialSortingEnabled);
            bool sortByCategory = playerData.SortByCategory;
            if (delay)
            {
                SortButtonMod.Instance?.NextTick(() =>
                {
                    if (basePlayer == null || basePlayer.IsDestroyed || entity == null || entity.IsDestroyed)
                        return;
                    HandleOnLootEntityDelayed(basePlayer, entity, offsetXString, sortByCategory);
                });
            }
            else
                HandleOnLootEntityDelayed(basePlayer, entity, offsetXString, sortByCategory);
        }

        private static bool IsSortableContainer(ItemContainer container)
        {
            if (container.IsLocked() || container.PlayerItemInputBlocked()
                || container.HasFlag(ItemContainer.Flag.IsPlayer) || container.capacity <= 1)
                return false;
            return true;
        }

        private static bool CanPlayerSortEntity(BasePlayer basePlayer, BaseEntity entity)
        {
            if (entity == null || entity.IsDestroyed) return false;
            var dropBox = entity as DropBox;
            if (dropBox != null) return dropBox.PlayerBehind(basePlayer);
            var vendingMachine = entity as VendingMachine;
            if (vendingMachine != null) return vendingMachine.PlayerBehind(basePlayer);
            return true;
        }

        private static string DetermineLootPanelName(BaseEntity entity)
        {
            var mailbox = entity as Mailbox;
            if (mailbox != null) return mailbox.ownerPanel;
            var storageContainer = entity as StorageContainer;
            if (storageContainer != null) return storageContainer.panelName;
            var horse = entity as RidableHorse;
            if (horse != null) return horse.storagePanelName;
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
                int categoryIndex = _itemCategoryToSortIndex[(int)a.info.category];
                int otherCategoryIndex = _itemCategoryToSortIndex[(int)b.info.category];
                int categoryComparison = categoryIndex.CompareTo(otherCategoryIndex);
                if (categoryComparison != 0)
                    return categoryComparison;
            }
            int nameComparison = a.info.displayName.translated.CompareTo(b.info.displayName.translated);
            if (nameComparison != 0)
                return nameComparison;
            return a.amount.CompareTo(b.amount);
        }

        private void SortContainer(ItemContainer container, BasePlayer initiator, bool byCategory)
        {
            var itemList = Pool.Get<List<Item>>();
            if (container.entityOwner is BuildingPrivlidge)
            {
                for (int i = container.itemList.Count - 1; i >= 0; i--)
                {
                    var item = container.itemList[i];
                    if (item.position >= 24) continue;
                    item.RemoveFromContainer();
                    itemList.Add(item);
                }
            }
            else
            {
                for (int i = container.itemList.Count - 1; i >= 0; i--)
                {
                    var item = container.itemList[i];
                    item.RemoveFromContainer();
                    itemList.Add(item);
                }
            }

            itemList.Sort((a, b) => CompareItems(a, b, byCategory));
            for (int i = 0; i < itemList.Count; i++)
            {
                var item = itemList[i];
                if (!item.MoveToContainer(container))
                    initiator.GiveItem(item);
            }
            Pool.FreeUnmanaged(ref itemList);
        }

        private void CreateButtonUI(BasePlayer player, string offsetXString, string offsetYString, string heightString, bool sortByCategory)
        {
            if (!_uiViewers.Add(GetUserId(player)))
                return;
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
                }, "Overlay", GUIPanelName);

                elements.Add(new CuiButton
                {
                    Button = { Command = "sortbutton.order", Color = "{2}" },
                    RectTransform =
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "0 0",
                        OffsetMin = "0 0",
                        OffsetMax = $"{SortOrderButtonWidthString} {{3}}",
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
                    Button = { Command = "sortbutton.sort", Color = "0.41 0.50 0.25 0.8" },
                    RectTransform =
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "0 0",
                        OffsetMin = $"{SortOrderButtonWidthString} 0",
                        OffsetMax = $"{SortOrderButtonWidthString + SortButtonWidth} {{3}}",
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
            _uiArguments[5] = Lang(LangKeys.Format.ButtonText, player.UserIDString);
            CuiHelper.AddUi(player, string.Format(_cachedUI, _uiArguments));
        }

        private void RecreateSortButton(BasePlayer player)
        {
            DestroyUi(player);
            var storage = player.inventory.loot?.entitySource as StorageContainer;
            if (storage != null)
                HandleOnLootEntity(player, storage, delay: false);
        }

        private void DestroyUi(BasePlayer player)
        {
            if (player == null) return;
            if (!_uiViewers.Remove(GetUserId(player)))
                return;
            CuiHelper.DestroyUi(player, GUIPanelName);
        }

        private class StoredData
        {
            public Hash<ulong, PlayerData> PlayerData = new Hash<ulong, PlayerData>();
        }

        private class PlayerData
        {
            public bool Enabled;
            public bool SortByCategory;
        }

        private PlayerData GetPlayerData(ulong userID, bool createIfMissing = false)
        {
            var playerData = _storedData.PlayerData[userID];
            if (playerData != null) return playerData;
            if (createIfMissing)
            {
                playerData = new PlayerData
                {
                    Enabled = _config.DefaultEnabled,
                    SortByCategory = _config.DefaultSortByCategory,
                };
                _storedData.PlayerData[userID] = playerData;
                return playerData;
            }
            return _defaultPlayerData;
        }

        private void LoadData()
        {
            _storedData = new StoredData();
            if (!File.Exists(_dataPath)) return;
            try
            {
                var loaded = JsonConvert.DeserializeObject<StoredData>(File.ReadAllText(_dataPath));
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
                Directory.CreateDirectory(Path.GetDirectoryName(_dataPath));
                File.WriteAllText(_dataPath, JsonConvert.SerializeObject(_storedData, Formatting.Indented));
            }
            catch (Exception ex) { Debug.LogWarning("[SortButton] SaveData: " + ex.Message); }
        }

        private class ContainerConfiguration
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

        private class Configuration
        {
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
            public ulong SteamIDIcon = 0;
            [JsonProperty("Chat command", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Commands = new List<string> { "sortbutton" };
            [JsonProperty("Containers by short prefab name")]
            public Dictionary<string, ContainerConfiguration> ContainersByPrefabPath = new Dictionary<string, ContainerConfiguration>();
            [JsonProperty("Containers by skin ID")]
            public Dictionary<ulong, ContainerConfiguration> ContainersBySkinId = new Dictionary<ulong, ContainerConfiguration>();
            [JsonIgnore]
            private Dictionary<uint, ContainerConfiguration> ContainersByPrefabId = new Dictionary<uint, ContainerConfiguration>();

            public void OnServerInitialized(SortButtonPlugin plugin)
            {
                if (ContainersByPrefabPath == null)
                    ContainersByPrefabPath = new Dictionary<string, ContainerConfiguration>();
                foreach (var kvp in ContainersByPrefabPath)
                {
                    var baseEntity = GameManager.server.FindPrefab(kvp.Key)?.GetComponent<BaseEntity>();
                    if (baseEntity == null) continue;
                    ContainersByPrefabId[baseEntity.prefabID] = kvp.Value;
                }
            }

            public ContainerConfiguration GetContainerConfiguration(BaseEntity entity)
            {
                ContainerConfiguration containerConfiguration;
                if (entity.skinID != 0 && ContainersBySkinId != null && ContainersBySkinId.TryGetValue(entity.skinID, out containerConfiguration))
                    return containerConfiguration;
                if (ContainersByPrefabId.TryGetValue(entity.prefabID, out containerConfiguration))
                    return containerConfiguration;
                return null;
            }
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                    _config = JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(_configPath));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SortButton] Config invalid: " + ex.Message);
            }
            _config ??= new Configuration();
            _config.Commands ??= new List<string> { "sortbutton" };
            _config.ContainersByPrefabPath ??= new Dictionary<string, ContainerConfiguration>();
            _config.ContainersBySkinId ??= new Dictionary<ulong, ContainerConfiguration>();
        }

        private static class LangKeys
        {
            public static class Error { public const string NoPermission = "Error.NoPermission"; }
            public static class Info
            {
                public const string ButtonStatus = "Info.ButtonStatus";
                public const string Help = "Info.Help";
                public const string SortType = "Info.SortType";
            }
            public static class Format
            {
                public const string ButtonText = "Format.ButtonText";
                public const string Category = "Format.Category";
                public const string Disabled = "Format.Disabled";
                public const string Enabled = "Format.Enabled";
                public const string Name = "Format.Name";
                public const string Prefix = "Format.Prefix";
            }
        }

        private string Lang(string key, string userIDString = null, params object[] args)
        {
            string msg;
            if (!_lang.TryGetValue(key, out msg) || msg == null)
                msg = key;
            if (args == null || args.Length == 0) return msg;
            try { return string.Format(msg, args); }
            catch { return msg; }
        }

        private void LoadDefaultMessages()
        {
            void Add(string k, string v) { if (!_lang.ContainsKey(k)) _lang[k] = v; }
            Add(LangKeys.Error.NoPermission, "You do not have permission to use this command");
            Add(LangKeys.Format.ButtonText, "Sort");
            Add(LangKeys.Format.Category, "<color=#D2691E>Category</color>");
            Add(LangKeys.Format.Disabled, "<color=#B22222>Disabled</color>");
            Add(LangKeys.Format.Enabled, "<color=#228B22>Enabled</color>");
            Add(LangKeys.Format.Name, "<color=#00BFFF>Name</color>");
            Add(LangKeys.Format.Prefix, "<color=#00FF00>[Sort Button]</color>: ");
            Add(LangKeys.Info.ButtonStatus, "Sort Button is now {0}");
            Add(LangKeys.Info.SortType, "Sort Type is now {0}");
            Add(LangKeys.Info.Help, "List Commands:\n<color=#FFFF00>/{0}</color> - Enable/Disable Sort Button.\n<color=#FFFF00>/{0} <sort | type></color> - change sort type.");
        }

        private void LoadLangFile()
        {
            if (!File.Exists(_langPath)) return;
            try
            {
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(_langPath));
                if (loaded == null) return;
                foreach (var kv in loaded)
                {
                    if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
                        _lang[kv.Key] = kv.Value;
                }
            }
            catch (Exception ex) { Debug.LogWarning("[SortButton] Lang: " + ex.Message); }
        }

        private static ulong GetUserId(BasePlayer player)
        {
            try { return player.userID.Get(); }
            catch { return (ulong)player.userID; }
        }
    }
}
