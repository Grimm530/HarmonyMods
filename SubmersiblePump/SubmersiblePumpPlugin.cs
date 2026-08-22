using System;
using System.Collections.Generic;
using System.IO;
using Facepunch;
using Newtonsoft.Json;
using UnityEngine;

namespace SubmersiblePump
{
    public class SubmersiblePumpPlugin
    {
        private static readonly Dictionary<ulong, int> PlacedPumps = new Dictionary<ulong, int>();
        private static readonly HashSet<ulong> HandledPlacements = new HashSet<ulong>();
        internal const ulong SubmersiblePumpSkin = 2593673595;
        private const string PumpPrefab = "assets/prefabs/deployable/playerioents/waterpump/water.pump.deployed.prefab";
        private static readonly int ConstructionMask = LayerMask.GetMask("Construction");

        private readonly string _configPath;
        private readonly string _langPath;
        private readonly string _dataPath;
        private readonly Dictionary<string, string> _lang = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public PluginConfig ConfigData;
        public PluginData Data = new PluginData();

        public SubmersiblePumpPlugin(string serverRoot)
        {
            _configPath = Path.Combine(serverRoot, "HarmonyConfig", "SubmersiblePump.json");
            _langPath = Path.Combine(serverRoot, "HarmonyLanguage", "SubmersiblePump.json");
            _dataPath = Path.Combine(serverRoot, "HarmonyData", "SubmersiblePump", "SubmersiblePump.json");
        }

        public void Load()
        {
            LoadDefaultMessages();
            LoadLangFile();
            LoadConfig();
            LoadData();
            PlacedPumps.Clear();
            HandledPlacements.Clear();
            RegisterPermissions();
        }

        public void RegisterPermissions()
        {
            if (ConfigData.requirePerm)
                PermissionsBridge.RegisterPermission("submersiblepump.use");
            PermissionsBridge.RegisterPermission("submersiblepump.give");
            if (ConfigData.permissionLimit != null)
            {
                foreach (var perm in ConfigData.permissionLimit.Keys)
                    PermissionsBridge.RegisterPermission(perm);
            }
        }

        public void OnServerInitialized()
        {
            for (int i = 0; i < Data.pumps.Count; i++)
            {
                ulong pumpId = Data.pumps[i];
                WaterPump pumpEntity = BaseNetworkable.serverEntities.Find(new NetworkableId(pumpId)) as WaterPump;
                if (pumpEntity)
                {
                    if (pumpEntity.skinID == SubmersiblePumpSkin)
                        TerrainMeta.TopologyMap.AddTopology(pumpEntity.transform.position, 65536);
                    ulong owner = pumpEntity.OwnerID;
                    int count;
                    if (!PlacedPumps.TryGetValue(owner, out count))
                        PlacedPumps[owner] = 0;
                    PlacedPumps[owner]++;
                }
            }
        }

        public void Unload()
        {
            SaveData();
            PlacedPumps.Clear();
            HandledPlacements.Clear();
        }

        public void OnEntityKill(WaterPump entity)
        {
            if (entity.OwnerID == 0) return;
            if (entity.net == null) return;
            if (!Data.pumps.Contains(entity.net.ID.Value)) return;

            Item item = ItemManager.CreateByName("electric.fuelgenerator.small", 1, SubmersiblePumpSkin);
            item.name = ConfigData.pumpName;
            item.Drop(entity.transform.position, Vector3.zero);
            if (PlacedPumps.ContainsKey(entity.OwnerID))
                PlacedPumps[entity.OwnerID]--;
            Data.pumps.Remove(entity.net.ID.Value);
        }

        public object CanPickupEntity(BasePlayer player, WaterPump entity)
        {
            if (entity.net == null || !Data.pumps.Contains(entity.net.ID.Value))
                return null;
            BuildingPrivlidge priv = entity.GetBuildingPrivilege();
            if (!priv)
            {
                entity.Kill();
                return false;
            }
            if (!priv.IsAuthed(player)) return false;
            entity.Kill();
            return false;
        }

        public void TryConvertPlacedGenerator(BaseEntity entity, BasePlayer player, ulong itemSkin, string itemName)
        {
            if (entity == null || entity.IsDestroyed) return;
            if (entity is WaterPump) return;
            ulong netId = entity.net != null ? entity.net.ID.Value : 0UL;
            if (netId != 0 && !HandledPlacements.Add(netId)) return;

            if (!IsSubmersiblePumpDeployable(entity, itemSkin, itemName))
            {
                if (netId != 0)
                    HandledPlacements.Remove(netId);
                return;
            }

            if (IsOurPumpSkin(itemSkin) && entity.skinID != itemSkin)
                entity.skinID = itemSkin;
            else if (entity.skinID == 0 && IsPumpItemName(itemName))
                entity.skinID = SubmersiblePumpSkin;

            BasePlayer capturedPlayer = player;
            BaseEntity capturedEntity = entity;
            SubmersiblePumpMod.Instance?.NextTick(() =>
            {
                try { ConvertPlacedGenerator(capturedEntity, capturedPlayer); }
                catch (Exception ex) { Debug.LogWarning("[SubmersiblePump] ConvertPlacedGenerator: " + ex.Message); }
            });
        }

        private void ConvertPlacedGenerator(BaseEntity entity, BasePlayer player)
        {
            if (entity == null || entity.IsDestroyed) return;
            if (entity is WaterPump) return;
            if (!(entity is FuelGenerator)) return;

            if (player == null || player.IsDestroyed)
                player = BasePlayer.FindByID(entity.OwnerID);

            if (player && ConfigData.requirePerm && !PermissionsBridge.UserHasPermission(player.UserIDString, "submersiblepump.use"))
            {
                RefundGenerator(player, entity);
                player.ChatMessage(Lang("NoPermission", player.UserIDString));
                return;
            }

            ulong uid = player ? GetUserId(player) : entity.OwnerID;
            if (ConfigData.pumpLimit > 0)
            {
                int permLimit = ConfigData.pumpLimit;
                if (player && ConfigData.permissionLimit != null)
                {
                    foreach (var perm in ConfigData.permissionLimit)
                    {
                        if (PermissionsBridge.UserHasPermission(player.UserIDString, perm.Key))
                            permLimit = perm.Value;
                    }
                }
                int placed;
                if (PlacedPumps.TryGetValue(uid, out placed) && placed >= permLimit)
                {
                    RefundGenerator(player, entity);
                    if (player)
                        player.ChatMessage(Lang("PumpLimit", player.UserIDString));
                    return;
                }
            }

            Vector3 pumpPos = entity.transform.position;
            Quaternion pumpRot = entity.transform.rotation;
            Vector3 heightCheck = new Vector3(pumpPos.x, TerrainMeta.HeightMap.GetHeight(pumpPos), pumpPos.z);
            bool isOnWaterLevel = pumpPos.y > 0 && pumpPos.y < 1.4f;
            bool isOnGround = Vector3.Distance(heightCheck, pumpPos) < 1.4f;
            bool cannotPlace = ConfigData.checkGround && !isOnGround;
            if (cannotPlace && ConfigData.allowWater && isOnWaterLevel)
                cannotPlace = false;
            if (cannotPlace)
            {
                RefundGenerator(player, entity);
                if (player)
                    player.ChatMessage(Lang("TooHigh", player.UserIDString));
                return;
            }

            BaseEntity parent = FindSupport(entity);
            WaterPump pump = GameManager.server.CreateEntity(PumpPrefab, pumpPos, pumpRot) as WaterPump;
            if (pump == null)
            {
                RefundGenerator(player, entity);
                return;
            }

            bool isSprinting = player && player.serverInput != null && player.serverInput.IsDown(BUTTON.SPRINT);
            ulong sourceSkin = IsOurPumpSkin(entity.skinID) ? entity.skinID : SubmersiblePumpSkin;
            if (sourceSkin == SubmersiblePumpSkin + 1)
                sourceSkin = SubmersiblePumpSkin;
            if (ConfigData.doNotFreshUpdate || (ConfigData.doNotFreshSprintButton && isSprinting))
                pump.skinID = sourceSkin + 1;
            else
            {
                pump.skinID = sourceSkin;
                TerrainMeta.TopologyMap.AddTopology(pumpPos, 65536);
            }

            if (player)
            {
                if (ConfigData.doNotFreshSprintButton && !isSprinting)
                    player.ChatMessage(Lang("SprintSalt", player.UserIDString));
                else if (ConfigData.doNotFreshSprintButton && isSprinting)
                    player.ChatMessage(Lang("PumpPlaced", player.UserIDString));
            }

            pump.OwnerID = uid;
            pump.Spawn();
            if (parent != null && !parent.IsDestroyed && parent != pump)
                pump.SetParent(parent, worldPositionStays: true, sendImmediate: true);
            StripGroundWatch(pump);
            pump.SendNetworkUpdateImmediate();

            if (!entity.IsDestroyed)
                entity.Kill(BaseNetworkable.DestroyMode.None);

            int count;
            if (!PlacedPumps.TryGetValue(uid, out count))
                PlacedPumps[uid] = 0;
            PlacedPumps[uid]++;
            if (pump.net != null)
                Data.pumps.Add(pump.net.ID.Value);
        }

        private bool IsSubmersiblePumpDeployable(BaseEntity entity, ulong itemSkin, string itemName)
        {
            if (entity is WaterPump) return false;
            if (IsOurPumpSkin(entity.skinID) || IsOurPumpSkin(itemSkin) || IsPumpItemName(itemName))
                return entity is FuelGenerator;
            return false;
        }

        private static bool IsOurPumpSkin(ulong skin)
        {
            return skin == SubmersiblePumpSkin || skin == SubmersiblePumpSkin + 1;
        }

        private bool IsPumpItemName(string itemName)
        {
            if (string.IsNullOrEmpty(itemName)) return false;
            if (string.Equals(itemName, "Submersible Pump", StringComparison.OrdinalIgnoreCase))
                return true;
            return ConfigData != null && !string.IsNullOrEmpty(ConfigData.pumpName) &&
                   string.Equals(itemName, ConfigData.pumpName, StringComparison.OrdinalIgnoreCase);
        }

        private static BaseEntity FindSupport(BaseEntity entity)
        {
            BaseEntity parent = entity.GetParentEntity();
            if (parent != null && !parent.IsDestroyed)
                return parent;
            Vector3 origin = entity.transform.position + Vector3.up * 0.15f;
            RaycastHit hit;
            if (Physics.Raycast(origin, Vector3.down, out hit, 4f, ConstructionMask, QueryTriggerInteraction.Ignore))
            {
                BaseEntity support = hit.GetEntity();
                if (support != null && !support.IsDestroyed && support != entity)
                    return support;
            }
            return null;
        }

        public void OnHammerHit(BasePlayer player, HitInfo info)
        {
            if (ConfigData == null || !ConfigData.hammerHitPickup) return;
            WaterPump pump = info.HitEntity as WaterPump;
            if (!pump) return;
            if (pump.net == null || !Data.pumps.Contains(pump.net.ID.Value)) return;
            BuildingPrivlidge tc = pump.GetBuildingPrivilege();
            if (tc && !tc.IsAuthed(player)) return;
            var captured = pump;
            SubmersiblePumpMod.Instance?.NextTick(() =>
            {
                if (captured != null && !captured.IsDestroyed)
                    captured.Kill();
            });
        }

        public void CraftPumpCommand(BasePlayer player, string command, string[] args)
        {
            if (ConfigData.requirePerm && !PermissionsBridge.UserHasPermission(player.UserIDString, "submersiblepump.use"))
            {
                player.ChatMessage(Lang("NoPermission", player.UserIDString));
                return;
            }
            if (!ConfigData.enablePumpCraft)
            {
                player.ChatMessage(Lang("NotEnabled", player.UserIDString));
                return;
            }
            if (args == null || args.Length == 0)
            {
                ShowHelp(player);
                return;
            }
            if (args.Length == 1 && args[0].ToLowerInvariant() == "craft")
            {
                if (ConfigData.requireBlueprint && !player.blueprints.HasUnlocked(ItemManager.FindItemDefinition("waterpump")))
                {
                    player.ChatMessage(Lang("NoBlueprint", player.UserIDString));
                    return;
                }
                if (ConfigData.requiredWorkbench > player.currentCraftLevel)
                {
                    player.ChatMessage(Lang("NoWorkbench", player.UserIDString, player.currentCraftLevel, ConfigData.requiredWorkbench));
                    return;
                }
                if (!TakeResources(player))
                {
                    player.ChatMessage(Lang("NoItems", player.UserIDString));
                    return;
                }
                player.ChatMessage(Lang("ItemCrafted", player.UserIDString));
                Item item = ItemManager.CreateByName("electric.fuelgenerator.small", 1, SubmersiblePumpSkin);
                item.name = ConfigData.pumpName;
                if (player.inventory.GiveItem(item))
                    player.SendConsoleCommand($"note.inv -1284169891 1 \"{ConfigData.pumpName}\"");
                else
                    item.Drop(player.transform.position + new Vector3(0, 1, 0), Vector3.zero);
            }
            else
                ShowHelp(player);
        }

        public void GivePumpCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player && !PermissionsBridge.UserHasPermission(player.UserIDString, "submersiblepump.give"))
            {
                arg.ReplyWith(Lang("NoPermission", player.UserIDString));
                return;
            }
            if (!player && (arg.Args == null || arg.Args.Length == 0))
            {
                arg.ReplyWith(Lang("PlayerNotFound"));
                return;
            }
            if (arg.Args != null && arg.Args.Length >= 1)
            {
                ulong id;
                if (!ulong.TryParse(arg.Args[0].ToString(), out id))
                {
                    arg.ReplyWith(Lang("PlayerNotFound"));
                    return;
                }
                player = BasePlayer.FindByID(id);
                if (!player)
                {
                    arg.ReplyWith(Lang("PlayerNotFound"));
                    return;
                }
            }
            Item item = ItemManager.CreateByName("electric.fuelgenerator.small", 1, SubmersiblePumpSkin);
            item.name = ConfigData.pumpName;
            player.inventory.GiveItem(item);
            player.SendConsoleCommand($"note.inv -1284169891 1 \"{ConfigData.pumpName}\"");
            arg.ReplyWith(Lang("ItemCrafted"));
        }

        private static void StripGroundWatch(BaseEntity entity)
        {
            if (entity == null) return;
            DestroyOnGroundMissing[] missing = entity.GetComponentsInChildren<DestroyOnGroundMissing>(true);
            for (int i = 0; i < missing.Length; i++)
            {
                if (missing[i] != null)
                    UnityEngine.Object.DestroyImmediate(missing[i]);
            }
            GroundWatch[] watches = entity.GetComponentsInChildren<GroundWatch>(true);
            for (int i = 0; i < watches.Length; i++)
            {
                if (watches[i] != null)
                    UnityEngine.Object.DestroyImmediate(watches[i]);
            }
        }

        private void RefundGenerator(BasePlayer player, BaseEntity generator)
        {
            Item item = ItemManager.CreateByName("electric.fuelgenerator.small", 1, SubmersiblePumpSkin);
            item.name = ConfigData.pumpName;
            if (player && !player.IsDestroyed)
                player.inventory.GiveItem(item);
            else
                item.Drop(generator.transform.position, Vector3.zero);
            if (generator != null && !generator.IsDestroyed)
                generator.Kill(BaseNetworkable.DestroyMode.None);
        }

        private bool TakeResources(BasePlayer player)
        {
            for (int r = 0; r < ConfigData.pumpCraftCost.Count; r++)
            {
                var requiredItem = ConfigData.pumpCraftCost[r];
                bool haveRequired = false;
                int inventoryAmount = 0;
                for (int i = 0; i < player.inventory.containerMain.itemList.Count; i++)
                {
                    var item = player.inventory.containerMain.itemList[i];
                    if (item.skin == requiredItem.skin && item.info.shortname == requiredItem.shortname)
                    {
                        inventoryAmount += item.amount;
                        if (inventoryAmount >= requiredItem.amount)
                        {
                            haveRequired = true;
                            break;
                        }
                    }
                }
                if (!haveRequired)
                {
                    for (int i = 0; i < player.inventory.containerBelt.itemList.Count; i++)
                    {
                        var item = player.inventory.containerBelt.itemList[i];
                        if (item.skin == requiredItem.skin && item.info.shortname == requiredItem.shortname)
                        {
                            inventoryAmount += item.amount;
                            if (inventoryAmount >= requiredItem.amount)
                            {
                                haveRequired = true;
                                break;
                            }
                        }
                    }
                }
                if (!haveRequired)
                    return false;
            }

            for (int r = 0; r < ConfigData.pumpCraftCost.Count; r++)
            {
                var requiredItem = ConfigData.pumpCraftCost[r];
                ItemDefinition itemDef = ItemManager.FindItemDefinition(requiredItem.shortname);
                player.SendConsoleCommand($"note.inv {itemDef.itemid} -{requiredItem.amount}");
                int takenItems = 0;
                List<Item> invItems = Pool.Get<List<Item>>();
                invItems.AddRange(player.inventory.containerMain.itemList);
                invItems.AddRange(player.inventory.containerBelt.itemList);
                for (int i = 0; i < invItems.Count; i++)
                {
                    var item = invItems[i];
                    if (item.skin == requiredItem.skin && item.info.shortname == requiredItem.shortname)
                    {
                        if (takenItems < requiredItem.amount)
                        {
                            if (item.amount > requiredItem.amount - takenItems)
                            {
                                item.amount -= requiredItem.amount - takenItems;
                                item.MarkDirty();
                                break;
                            }
                            if (item.amount <= requiredItem.amount - takenItems)
                            {
                                takenItems += item.amount;
                                item.GetHeldEntity()?.Kill();
                                item.Remove();
                            }
                        }
                        else break;
                    }
                }
                Pool.FreeUnmanaged(ref invItems);
            }
            return true;
        }

        private void ShowHelp(BasePlayer player)
        {
            string items = string.Empty;
            if (ConfigData.requireBlueprint)
                items += Lang("BlueprintRequired", player.UserIDString);
            for (int i = 0; i < ConfigData.pumpCraftCost.Count; i++)
            {
                var item = ConfigData.pumpCraftCost[i];
                var def = ItemManager.FindItemDefinition(item.shortname);
                items += Lang("ItemFormat", player.UserIDString, item.amount, def != null ? def.displayName.english : item.shortname);
            }
            player.ChatMessage(Lang("Help", player.UserIDString, ConfigData.command, ConfigData.requiredWorkbench, items));
        }

        private static ulong GetUserId(BasePlayer player)
        {
            try { return player.userID.Get(); }
            catch { return (ulong)player.userID; }
        }

        private string Lang(string key, string id = null, params object[] args)
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
            Add("TooHigh", "You are <color=#5c81ed>too far from ground</color> to place submersible pump!");
            Add("NotEnabled", "Submersible Pump crafting is <color=#5c81ed>not enabled</color>!");
            Add("NoItems", "You don't have <color=#5c81ed>required items</color> to craft Submersible Pump!");
            Add("ItemCrafted", "You've succesfully crafted your <color=#5c81ed>Submersible Pump</color>!");
            Add("NoBlueprint", "You need to learn Water Pump <color=#5c81ed>blueprint</color> first to craft Submersible Pump!");
            Add("NoWorkbench", "Submersible Pump require higher <color=#5c81ed>workbench level</color>!\n(Current: {0}, Required: {1})");
            Add("Help", "<color=#5c81ed>/{0} craft</color> - Craft Submersible Pump\n\nRequired Workbench Level: <color=#5c81ed>{1}</color>\nRequired Items:\n{2}");
            Add("BlueprintRequired", "  - Water Pump Blueprint\n");
            Add("ItemFormat", "  - <color=#5c81ed>x{0}</color> {1}\n");
            Add("NoPermission", "You don't have permission to craft and place <color=#5c81ed>Submersible Pumps</color>!");
            Add("PlayerNotFound", "We couldn't find player with this ID!");
            Add("PumpLimit", "You've reached your submersible pump limit!");
            Add("PumpPlaced", "Successfully placed submersible pump. This pump won't collect freshwater all the time! If you want fresh water, do not press SPRINT button while placing!");
            Add("SprintSalt", "Successfully placed submersible pump. If you press SPRINT button while placing submersible pump, it will keep their original water type!");
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
            catch (Exception ex) { Debug.LogWarning("[SubmersiblePump] Lang load: " + ex.Message); }
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                    ConfigData = JsonConvert.DeserializeObject<PluginConfig>(File.ReadAllText(_configPath));
            }
            catch (Exception ex) { Debug.LogWarning("[SubmersiblePump] Config load: " + ex.Message); }
            if (ConfigData == null)
            {
                ConfigData = new PluginConfig
                {
                    pumpCraftCost = new List<ItemConfig>
                    {
                        new ItemConfig { shortname = "metal.fragments", amount = 1000, skin = 0 },
                        new ItemConfig { shortname = "gears", amount = 10, skin = 0 },
                        new ItemConfig { shortname = "metalpipe", amount = 20, skin = 0 }
                    },
                    permissionLimit = new Dictionary<string, int>
                    {
                        { "submersiblepump.vip", 5 },
                        { "submersiblepump.admin", 100 }
                    }
                };
            }
            ConfigData.pumpCraftCost ??= new List<ItemConfig>();
            ConfigData.permissionLimit ??= new Dictionary<string, int>();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_configPath));
                File.WriteAllText(_configPath, JsonConvert.SerializeObject(ConfigData, Formatting.Indented));
            }
            catch (Exception ex) { Debug.LogWarning("[SubmersiblePump] Config save: " + ex.Message); }
        }

        private void LoadData()
        {
            if (!File.Exists(_dataPath)) return;
            try
            {
                var loaded = JsonConvert.DeserializeObject<PluginData>(File.ReadAllText(_dataPath));
                if (loaded != null) Data = loaded;
            }
            catch (Exception ex)
            {
                Debug.LogError("[SubmersiblePump] Data file is corrupted: " + ex.Message);
                Data = new PluginData();
            }
        }

        public void SaveData()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_dataPath));
                File.WriteAllText(_dataPath, JsonConvert.SerializeObject(Data, Formatting.Indented));
            }
            catch (Exception ex) { Debug.LogWarning("[SubmersiblePump] Data save: " + ex.Message); }
        }

        public class PluginConfig
        {
            [JsonProperty("Misc - Require Permission")]
            public bool requirePerm = false;
            [JsonProperty("Misc - Pump Item Name")]
            public string pumpName = "Submersible Pump";
            [JsonProperty("Misc - Pump Ground Check")]
            public bool checkGround = true;
            [JsonProperty("Misc - Allow Placing On Water Level")]
            public bool allowWater = false;
            [JsonProperty("Misc - Allow Hammer Hit Pickup")]
            public bool hammerHitPickup = true;
            [JsonProperty("Water Type - Do Not Update Water Type To Freshwater")]
            public bool doNotFreshUpdate = false;
            [JsonProperty("Water Type - Do Not Update Water Type When Pressing SPRINT Button")]
            public bool doNotFreshSprintButton = true;
            [JsonProperty("Limit - Default Pump Limit (0, to disable)")]
            public int pumpLimit = 0;
            [JsonProperty("Limit - Permission Limits")]
            public Dictionary<string, int> permissionLimit = new Dictionary<string, int>();
            [JsonProperty("Craft - Enable Pump Craft")]
            public bool enablePumpCraft = true;
            [JsonProperty("Craft - Chat Command")]
            public string command = "pump";
            [JsonProperty("Craft - Require Blueprint For Pump")]
            public bool requireBlueprint = true;
            [JsonProperty("Craft - Required Workbench Level (0-3)")]
            public int requiredWorkbench = 2;
            [JsonProperty("Craft - Pump Craft Cost")]
            public List<ItemConfig> pumpCraftCost = new List<ItemConfig>();
        }

        public class ItemConfig
        {
            [JsonProperty("Item Shortname")]
            public string shortname;
            [JsonProperty("Item Amount")]
            public int amount;
            [JsonProperty("Item Skin")]
            public ulong skin;
        }

        public class PluginData
        {
            [JsonProperty("Placed Pumps")]
            public List<ulong> pumps = new List<ulong>();
        }
    }
}
