using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Facepunch;
using Newtonsoft.Json;
using Rust;
using UnityEngine;

namespace ChestStacks
{
    public class Hash<TKey, TValue> : Dictionary<TKey, TValue>
    {
        public new TValue this[TKey key]
        {
            get => TryGetValue(key, out var value) ? value : default;
            set => base[key] = value;
        }
    }

    public class ChestStacksPlugin
    {
        private const string UsePermission = "cheststacks.use";
        private const string SmallBoxPrefabPath = "assets/prefabs/deployable/woodenbox";
        private const string LargeBoxPrefabPath = "assets/prefabs/deployable/large wood storage";
        private const string LargeBoxPrefab = "assets/prefabs/deployable/large wood storage/box.wooden.large.prefab";
        private const string DecorDlcPrefabPath = "assets/prefabs/misc/decor_dlc/storagebarrel";
        private const string ComponentDlcPrefabPath =
            "assets/prefabs/deployable/large wood storage/skins/component_storage_boxes_dlc";
        private const string SmallBoxEffect = "assets/prefabs/deployable/woodenbox/effects/wooden-box-deploy.prefab";
        private const string LargeBoxEffect = "assets/prefabs/deployable/large wood storage/effects/large-wood-box-deploy.prefab";
        private const int BoxLayer = Layers.Mask.Deployed;
        private const int ConstructionLayer = Layers.Mask.Construction;
        private const int VehicleLargeLayer = Layers.Mask.Vehicle_Large;
        private const BaseEntity.Flags StackedFlag = BaseEntity.Flags.Reserved1;

        internal static ChestStacksPlugin PluginInstance;
        internal readonly Hash<ulong, BoxStackHandler> Components = new Hash<ulong, BoxStackHandler>();
        private readonly Dictionary<int, string> _itemPrefabs = new Dictionary<int, string>();
        private readonly Dictionary<uint, Vector3> _boxStorageOffsets = new Dictionary<uint, Vector3>();
        private readonly RaycastHit[] _raycastHits = new RaycastHit[3];
        private readonly Vector3 _raycastOffset = new Vector3(0f, 0.2f, 0f);
        private readonly object _returnObject = true;
        private readonly string _configPath;
        private readonly string _langPath;
        private readonly string _dataPath;
        private readonly Dictionary<string, string> _lang = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal PluginConfiguration ConfigData;
        internal PluginData Data;

        public enum BoxType
        {
            None = 0,
            SmallBox = 1,
            LargeBox = 2
        }

        public ChestStacksPlugin(string serverRoot)
        {
            PluginInstance = this;
            _configPath = Path.Combine(serverRoot, "HarmonyConfig", "ChestStacks.json");
            _langPath = Path.Combine(serverRoot, "HarmonyLanguage", "ChestStacks.json");
            _dataPath = Path.Combine(serverRoot, "HarmonyData", "ChestStacks", "boxes.json");
        }

        public void Load()
        {
            LoadDefaultMessages();
            LoadLangFile();
            LoadConfig();
            LoadData();
            RegisterPermissions();
        }

        public void RegisterPermissions()
        {
            if (ConfigData?.ChestStacksAmount != null)
            {
                foreach (string perm in ConfigData.ChestStacksAmount.Keys)
                    PermissionsBridge.RegisterPermission(perm);
            }
            if (!PermissionsBridge.PermissionExists(UsePermission))
                PermissionsBridge.RegisterPermission(UsePermission);
        }

        public void OnServerInitialized()
        {
            CacheItemPrefabs();
            var list = BasePlayer.activePlayerList;
            for (int i = 0; i < list.Count; i++)
                OnPlayerConnected(list[i]);
            SaveData();
        }

        public void Unload()
        {
            var handlers = Pool.Get<List<BoxStackHandler>>();
            foreach (var v in Components.Values)
                handlers.Add(v);
            int count = handlers.Count;
            for (int i = 0; i < count; i++)
                handlers[i].Destroy();
            Pool.FreeUnmanaged(ref handlers);
            SaveData();
            PluginInstance = null;
        }

        public void OnPlayerConnected(BasePlayer player)
        {
            if (player == null) return;
            ulong id = GetUserId(player);
            BoxStackHandler boxStackHandler = Components[id];
            if (boxStackHandler)
                return;
            boxStackHandler = player.gameObject.AddComponent<BoxStackHandler>();
            boxStackHandler.Initialize(player);
        }

        public void OnPlayerDisconnected(BasePlayer player)
        {
            if (player == null) return;
            BoxStackHandler boxStackHandler = Components[GetUserId(player)];
            if (!boxStackHandler) return;
            boxStackHandler.Destroy();
        }

        public void OnEntityKill(BoxStorage boxStorage)
        {
            if (!boxStorage) return;
            if (GetTugboat(boxStorage))
                CheckNearbyBoxes(boxStorage, 2f);
            if (!IsStacked(boxStorage)) return;
            if (boxStorage.net != null)
                HandleBoxUnstack(boxStorage.net.ID.Value);
        }

        public object OnEntityGroundMissing(BoxStorage boxStorage)
        {
            if (!boxStorage || !IsStacked(boxStorage))
                return null;
            HandleBoxGroundMissing(boxStorage);
            return _returnObject;
        }

        private void HandleBoxGroundMissing(BoxStorage boxStorage)
        {
            if (IsStackedOnBox(boxStorage))
                return;
            boxStorage.Die();
        }

        private void CacheItemPrefabs()
        {
            List<ItemDefinition> itemDefinitions = ItemManager.GetItemDefinitions();
            int itemDefinitionCount = itemDefinitions.Count;
            for (int i = 0; i < itemDefinitionCount; i++)
            {
                ItemDefinition itemDefinition = itemDefinitions[i];
                ItemModDeployable deployable = itemDefinition.GetComponent<ItemModDeployable>();
                if (!deployable) continue;
                _itemPrefabs[itemDefinition.itemid] = deployable.entityPrefab.resourcePath;
            }
        }

        public string GetItemPrefab(int itemId)
        {
            string path;
            return _itemPrefabs.TryGetValue(itemId, out path) ? path : null;
        }

        internal Vector3 GetBoxOffset(string prefab)
        {
            uint prefabId = StringPool.Get(prefab);
            Vector3 offset;
            if (_boxStorageOffsets.TryGetValue(prefabId, out offset))
                return offset;

            offset = Vector3.zero;
            if (ConfigData.SupportedBoxOffsets != null && ConfigData.SupportedBoxOffsets.TryGetValue(prefab, out offset) && offset != Vector3.zero)
                return _boxStorageOffsets[prefabId] = offset;

            if (ConfigData.SupportedPrefabPathOffsets != null)
            {
                foreach (var entry in ConfigData.SupportedPrefabPathOffsets)
                {
                    if (!prefab.StartsWith(entry.Key, StringComparison.Ordinal))
                        continue;
                    return _boxStorageOffsets[prefabId] = entry.Value;
                }
            }
            return Vector3.zero;
        }

        private void CheckNearbyBoxes(BoxStorage boxStorage, float radius)
        {
            List<BoxStorage> boxStorages = Pool.Get<List<BoxStorage>>();
            Vis.Entities(boxStorage.transform.position, radius, boxStorages, BoxLayer);
            int boxStorageCount = boxStorages.Count;
            try
            {
                for (int i = 0; i < boxStorageCount; i++)
                {
                    BoxStorage foundBoxStorage = boxStorages[i];
                    if (!foundBoxStorage || boxStorage == foundBoxStorage || !IsStacked(foundBoxStorage))
                        continue;
                    var captured = foundBoxStorage;
                    var runner = ChestStacksMod.Instance != null
                        ? GameObject.Find("ChestStacks_Runner")?.GetComponent<ChestStacksRunner>()
                        : null;
                    if (runner != null)
                        runner.Delay(() => HandleBoxGroundMissing(captured), 0f);
                    else
                        HandleBoxGroundMissing(captured);
                }
            }
            finally
            {
                Pool.FreeUnmanaged(ref boxStorages);
            }
        }

        internal bool HasStackedBox(BoxStorage boxStorage)
        {
            if (!boxStorage) return false;
            Vector3 position = boxStorage.transform.position;
            int raycastHitCount = Physics.RaycastNonAlloc(position, Vector3.up, _raycastHits, 1.5f, BoxLayer);
            for (int i = 0; i < raycastHitCount; i++)
            {
                RaycastHit raycastHit = _raycastHits[i];
                var stacked = GameObjectEx.ToBaseEntity(raycastHit.collider.gameObject) as BoxStorage;
                if (stacked == null || stacked == boxStorage) continue;
                return true;
            }
            return false;
        }

        private bool IsStackedOnBox(BoxStorage boxStorage)
        {
            if (!boxStorage) return false;
            Vector3 position = boxStorage.transform.position + _raycastOffset;
            int raycastHitCount = Physics.RaycastNonAlloc(position, Vector3.down, _raycastHits, 0.25f, BoxLayer);
            for (int i = 0; i < raycastHitCount; i++)
            {
                RaycastHit raycastHit = _raycastHits[i];
                var stacked = GameObjectEx.ToBaseEntity(raycastHit.collider.gameObject) as BoxStorage;
                if (stacked == null || stacked == boxStorage) continue;
                return true;
            }
            return false;
        }

        private static bool IsStacked(BoxStorage boxStorage) =>
            boxStorage.HasFlag(StackedFlag) || boxStorage.GetParentEntity() is BoxStorage;

        internal ulong GetBottomBoxId(ulong boxId) => Data.StoredBoxes[boxId]?.BottomBoxId ?? 0;
        internal int GetStackedBoxes(ulong bottomBoxId) => Data.StoredBoxes[bottomBoxId]?.Boxes ?? 0;

        private void HandleBoxUnstack(ulong boxId)
        {
            ulong bottomBoxId = GetBottomBoxId(boxId);
            if (bottomBoxId == 0) return;
            var data = Data.StoredBoxes[bottomBoxId];
            if (data != null)
                data.Boxes--;
        }

        internal Tugboat GetTugboat<T>(T type) where T : BaseEntity => type.GetParentEntity() as Tugboat;

        internal bool HasPermission(BasePlayer player, string perm) =>
            PermissionsBridge.UserHasPermission(player.UserIDString, perm);

        internal int GetPermissionValue(BasePlayer player, Hash<string, ChestTypeConfig> permissions, BoxType boxType)
        {
            int best = 0;
            bool any = false;
            foreach (var kvp in permissions)
            {
                if (!HasPermission(player, kvp.Key)) continue;
                int limit = 0;
                if (kvp.Value?.BoxTypeLimits != null && kvp.Value.BoxTypeLimits.TryGetValue(boxType, out limit))
                {
                    if (!any || limit > best)
                    {
                        best = limit;
                        any = true;
                    }
                }
            }
            return any ? best : 0;
        }

        internal string GetLangKeyString(string langKey)
        {
            string msg;
            return _lang.TryGetValue(langKey, out msg) && msg != null ? msg : langKey;
        }

        internal string GetLangKeyString(string langKey, object arg) =>
            string.Format(GetLangKeyString(langKey), arg);

        internal static ulong GetUserId(BasePlayer player)
        {
            if (player == null) return 0UL;
            try { return player.userID.Get(); }
            catch { return (ulong)player.userID; }
        }

        public class BoxStackHandler : FacepunchBehaviour
        {
            private BasePlayer _player;
            private float _cooldown;

            public void Initialize(BasePlayer player)
            {
                _player = player;
                PluginInstance.Components[GetUserId(_player)] = this;
            }

            private void Update()
            {
                var inst = PluginInstance;
                if (inst == null || !_player || !inst.HasPermission(_player, UsePermission))
                    return;
                if (!_player.serverInput.WasJustPressed(BUTTON.FIRE_SECONDARY))
                    return;
                if (_cooldown > Time.time)
                    return;
                _cooldown = Time.time + 0.5f;

                Item sourceItem = _player.GetActiveItem();
                if (sourceItem == null || sourceItem.info.category != ItemCategory.Items)
                    return;
                if (inst.ConfigData.BlacklistedSkins != null && inst.ConfigData.BlacklistedSkins.Contains(sourceItem.skin))
                    return;

                string sourcePrefab = inst.GetItemPrefab(sourceItem.info.itemid);
                if (sourcePrefab == null)
                    return;

                Vector3 offset = inst.GetBoxOffset(sourcePrefab);
                if (offset == Vector3.zero)
                    return;

                BoxStorage boxStorage = GetBox(_player);
                if (!boxStorage)
                    return;

                BoxType sourceBoxType = GetBoxType(sourcePrefab);
                if (sourceBoxType == BoxType.None)
                    return;

                string targetPrefab = boxStorage.PrefabName;
                BoxType targetBoxType = GetBoxType(targetPrefab);
                if (targetBoxType == BoxType.None)
                    return;

                if (sourceBoxType != targetBoxType || sourcePrefab != targetPrefab && !CanStackDifferentPrefabs(sourcePrefab, targetPrefab))
                {
                    _player.ChatMessage(inst.GetLangKeyString(LangKeys.OnlyStackSameType));
                    return;
                }

                StackBoxStorage(boxStorage, sourceItem, sourcePrefab, targetBoxType, offset);
            }

            private void StackBoxStorage(BoxStorage boxStorage, Item sourceItem, string sourcePrefab, BoxType targetBoxType, Vector3 offset)
            {
                var inst = PluginInstance;
                ulong bottomBoxId = inst.GetBottomBoxId(boxStorage.net.ID.Value);
                int boxes = inst.GetStackedBoxes(bottomBoxId);
                int allowedBoxAmount = inst.GetPermissionValue(_player, inst.ConfigData.ChestStacksAmount, targetBoxType);

                if (boxes >= allowedBoxAmount)
                {
                    _player.ChatMessage(inst.GetLangKeyString(LangKeys.MaxStackAmount, allowedBoxAmount));
                    return;
                }

                Tugboat tugboat = inst.GetTugboat(_player);
                PlayerBoat playerBoat = PlayerBoat.GetParentPlayerBoat(_player);
                bool isOnVehicle = tugboat || playerBoat;

                if (inst.ConfigData.BuildingPrivilegeRequired)
                {
                    if (tugboat && !_player.CanBuild() || playerBoat && !playerBoat.IsAuthedForBuilding(_player) ||
                        !isOnVehicle && !_player.IsBuildingAuthed())
                    {
                        _player.ChatMessage(inst.GetLangKeyString(LangKeys.BuildingBlock));
                        return;
                    }
                }

                Transform boxTransform = boxStorage.transform;
                Vector3 position = boxTransform.position;
                if (HasCeiling(position + offset, targetBoxType, isOnVehicle))
                {
                    _player.ChatMessage(inst.GetLangKeyString(LangKeys.CeilingBlock));
                    return;
                }

                if (inst.HasStackedBox(boxStorage))
                    return;

                BoxStorage createdBoxStorage = (BoxStorage)GameManager.server.CreateEntity(sourcePrefab,
                    position + offset, boxTransform.rotation);
                if (!createdBoxStorage)
                    return;

                if (tugboat)
                    createdBoxStorage.SetParent(tugboat, true);
                if (playerBoat)
                    createdBoxStorage.SetParent(playerBoat, true);

                createdBoxStorage.SetFlag(StackedFlag, true);
                createdBoxStorage.Spawn();
                createdBoxStorage.OwnerID = GetUserId(_player);
                createdBoxStorage.skinID = sourceItem.skin;
                createdBoxStorage.AttachToBuilding(boxStorage.buildingID);

                Effect.server.Run(targetBoxType == BoxType.SmallBox ? SmallBoxEffect : LargeBoxEffect, position);
                createdBoxStorage.SendNetworkUpdateImmediate();
                ProcessBoxStacks(boxStorage, createdBoxStorage);
                sourceItem.UseItem();
                inst.SaveData();
            }

            private void ProcessBoxStacks(BoxStorage lastBox, BoxStorage newBox)
            {
                var inst = PluginInstance;
                ulong bottomBoxId = inst.GetBottomBoxId(lastBox.net.ID.Value);
                if (bottomBoxId == 0)
                {
                    inst.Data.StoredBoxes[newBox.net.ID.Value] = new BoxData
                    {
                        BottomBoxId = newBox.net.ID.Value,
                        Boxes = 2
                    };
                    return;
                }

                int stackedBoxes = inst.GetStackedBoxes(bottomBoxId);
                inst.Data.StoredBoxes[newBox.net.ID.Value] = new BoxData
                {
                    BottomBoxId = bottomBoxId
                };
                inst.Data.StoredBoxes[bottomBoxId].Boxes = ++stackedBoxes;
            }

            private static bool CanStackDifferentPrefabs(string sourcePrefab, string targetPrefab) =>
                sourcePrefab.StartsWith(ComponentDlcPrefabPath, StringComparison.Ordinal) ||
                sourcePrefab == LargeBoxPrefab && targetPrefab.StartsWith(ComponentDlcPrefabPath, StringComparison.Ordinal) ||
                targetPrefab == LargeBoxPrefab;

            private static bool HasCeiling(Vector3 position, BoxType boxType, bool isOnVehicle)
            {
                int layerMask = isOnVehicle ? VehicleLargeLayer : ConstructionLayer;
                switch (boxType)
                {
                    case BoxType.SmallBox: return Physics.Raycast(position, Vector3.up, 0.5f, layerMask);
                    case BoxType.LargeBox: return Physics.Raycast(position, Vector3.up, 0.9f, layerMask);
                    default: return false;
                }
            }

            private static BoxStorage GetBox(BasePlayer player)
            {
                RaycastHit raycastHit;
                if (!Physics.Raycast(player.eyes.HeadRay(), out raycastHit, 4f, BoxLayer))
                    return null;
                return raycastHit.GetEntity() as BoxStorage;
            }

            private static BoxType GetBoxType(string prefab)
            {
                if (prefab.StartsWith(SmallBoxPrefabPath, StringComparison.Ordinal))
                    return BoxType.SmallBox;
                return prefab.StartsWith(LargeBoxPrefabPath, StringComparison.Ordinal) || prefab.StartsWith(DecorDlcPrefabPath, StringComparison.Ordinal)
                    ? BoxType.LargeBox
                    : BoxType.None;
            }

            public void Destroy()
            {
                if (_player != null)
                    PluginInstance?.Components.Remove(GetUserId(_player));
                DestroyImmediate(this);
            }
        }

        public class PluginConfiguration
        {
            [DefaultValue(true)]
            [JsonProperty("Building privilege required")]
            public bool BuildingPrivilegeRequired { get; set; }

            [JsonProperty("Blacklisted Skins")]
            public HashSet<ulong> BlacklistedSkins { get; set; }

            [JsonProperty("Permissions & their amount of stacked chests lmits")]
            public Hash<string, ChestTypeConfig> ChestStacksAmount { get; set; }

            [JsonProperty("Supported box prefab offsets")]
            public Dictionary<string, Vector3> SupportedBoxOffsets { get; set; }

            [JsonProperty("Supported prefab path offsets")]
            public Dictionary<string, Vector3> SupportedPrefabPathOffsets { get; set; }
        }

        public class ChestTypeConfig
        {
            [JsonProperty("Chest type limits")]
            public Dictionary<BoxType, int> BoxTypeLimits { get; set; }
        }

        public class PluginData
        {
            public Hash<ulong, BoxData> StoredBoxes { get; set; } = new Hash<ulong, BoxData>();
        }

        public class BoxData
        {
            public ulong BottomBoxId { get; set; }
            public int Boxes { get; set; }
        }

        private static class LangKeys
        {
            public const string MaxStackAmount = nameof(MaxStackAmount);
            public const string OnlyStackSameType = nameof(OnlyStackSameType);
            public const string CeilingBlock = nameof(CeilingBlock);
            public const string BuildingBlock = nameof(BuildingBlock);
        }

        private void LoadDefaultMessages()
        {
            void Add(string k, string v) { if (!_lang.ContainsKey(k)) _lang[k] = v; }
            Add(LangKeys.MaxStackAmount, "You are trying to stack more than {0} chests!");
            Add(LangKeys.OnlyStackSameType, "You can only stack the same type of chests!");
            Add(LangKeys.CeilingBlock, "A ceiling is blocking you from stacking this chest!");
            Add(LangKeys.BuildingBlock, "You need to be Building Privileged in order to stack chests!");
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
            catch (Exception ex) { Debug.LogWarning("[ChestStacks] Lang file load failed: " + ex.Message); }
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                    ConfigData = JsonConvert.DeserializeObject<PluginConfiguration>(File.ReadAllText(_configPath));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ChestStacks] Config load failed: " + ex.Message);
            }
            ConfigData = AdditionalConfig(ConfigData ?? new PluginConfiguration());
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_configPath));
                File.WriteAllText(_configPath, JsonConvert.SerializeObject(ConfigData, Formatting.Indented));
            }
            catch (Exception ex) { Debug.LogWarning("[ChestStacks] Config save failed: " + ex.Message); }
        }

        private static PluginConfiguration AdditionalConfig(PluginConfiguration pluginConfiguration)
        {
            pluginConfiguration.BlacklistedSkins ??= new HashSet<ulong> { 2618923347 };
            pluginConfiguration.ChestStacksAmount ??= new Hash<string, ChestTypeConfig>
            {
                ["cheststacks.use"] = new ChestTypeConfig
                {
                    BoxTypeLimits = new Dictionary<BoxType, int>
                    {
                        [BoxType.SmallBox] = 3,
                        [BoxType.LargeBox] = 5
                    }
                },
                ["cheststacks.vip"] = new ChestTypeConfig
                {
                    BoxTypeLimits = new Dictionary<BoxType, int>
                    {
                        [BoxType.SmallBox] = 5,
                        [BoxType.LargeBox] = 10
                    }
                }
            };
            pluginConfiguration.SupportedBoxOffsets ??= new Dictionary<string, Vector3>
            {
                ["assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab"] = new Vector3(0f, 0.57f),
                ["assets/prefabs/deployable/large wood storage/box.wooden.large.prefab"] = new Vector3(0f, 0.75f)
            };
            pluginConfiguration.SupportedPrefabPathOffsets ??= new Dictionary<string, Vector3>
            {
                ["assets/prefabs/deployable/large wood storage/skins/component_storage_boxes_dlc"] = new Vector3(0f, 0.76f)
            };
            return pluginConfiguration;
        }

        private void LoadData()
        {
            Data = new PluginData();
            if (!File.Exists(_dataPath)) return;
            try
            {
                var loaded = JsonConvert.DeserializeObject<PluginData>(File.ReadAllText(_dataPath));
                if (loaded?.StoredBoxes != null)
                    Data = loaded;
            }
            catch (Exception ex) { Debug.LogWarning("[ChestStacks] Data load failed: " + ex.Message); }
        }

        public void SaveData()
        {
            if (Data == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_dataPath));
                File.WriteAllText(_dataPath, JsonConvert.SerializeObject(Data, Formatting.Indented));
            }
            catch (Exception ex) { Debug.LogWarning("[ChestStacks] Data save failed: " + ex.Message); }
        }
    }
}
