using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Facepunch;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Game.Rust.Cui;
using Rust;
using UnityEngine;

namespace LootQoLHarmony
{
    /// <summary>
    /// Combined FastLoot 1.1.0 + LootBouncer 1.0.11 + SortButton 2.8.0 logic.
    /// </summary>
    public sealed class LootQoLPlugin
    {
        public const string PermFastLoot = "fastloot.use";
        private const string FastLootLayer = "UI_FastLootLayer";
        private const string AdminGroup = "admin";

        private readonly string _serverRoot;
        private readonly string _configPath;
        private readonly LangStore _lang = new LangStore();
        private Configuration _config;
        private SortButtonFeature _sortButton;

        private readonly Dictionary<ulong, int> _lootEntities = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, HashSet<ulong>> _entityPlayers = new Dictionary<ulong, HashSet<ulong>>();
        private bool _wantBarrelHooks;

        public Configuration Config => _config;
        public SortButtonFeature SortButton => _sortButton;

        public LootQoLPlugin(string serverRoot)
        {
            _serverRoot = serverRoot;
            _configPath = Path.Combine(serverRoot, "HarmonyConfig", "LootQoL.json");
        }

        #region Config

        public class FastLootSettings
        {
            [JsonProperty("Color background")]
            public string ColorBackground = "0.968627453 0.921631568632 0.882352948 0.03529412";

            [JsonProperty("Color font")]
            public string ColorFont = "0.87 0.84 0.80 1.00";

            [JsonProperty("Coordinates OffsetMin")]
            public string OffsetMin = "-115 -15";

            [JsonProperty("Coordinates OffsetMax")]
            public string OffsetMax = "168 15";
        }

        public class LootBouncerSettings
        {
            [JsonProperty("Time before the loot containers are empties (seconds)")]
            public float TimeBeforeLootEmpty = 30f;

            [JsonProperty("Empty the entire junkpile when automatically empty loot")]
            public bool EmptyJunkpile;

            [JsonProperty("Empty the nearby loot when emptying junkpile")]
            public bool DropNearbyLoot;

            [JsonProperty("Time before the junkpile are empties (seconds)")]
            public float TimeBeforeJunkpileEmpty = 150f;

            [JsonProperty("Slaps players who don't empty containers")]
            public bool SlapPlayer;

            [JsonProperty("Remove instead bouncing")]
            public bool RemoveItems;

            [JsonProperty("Chat Settings")]
            public ChatSettings Chat = new ChatSettings();

            [JsonProperty("Loot container settings")]
            public Dictionary<string, bool> LootContainers = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            public class ChatSettings
            {
                [JsonProperty("Chat Prefix")]
                public string Prefix = "<color=#00FFFF>[LootBouncer]</color>: ";

                [JsonProperty("Chat SteamID Icon")]
                public ulong SteamIDIcon;
            }
        }

        public class Configuration
        {
            [JsonProperty("FastLoot")]
            public FastLootSettings FastLoot = new FastLootSettings();

            [JsonProperty("LootBouncer")]
            public LootBouncerSettings LootBouncer = new LootBouncerSettings();

            [JsonProperty("SortButton")]
            public SortButtonFeature.Settings SortButton = new SortButtonFeature.Settings();
        }

        public void LoadConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                bool hadSortSection = false;
                if (File.Exists(_configPath))
                {
                    string json = File.ReadAllText(_configPath);
                    try
                    {
                        var jo = JObject.Parse(json);
                        hadSortSection = jo["SortButton"] != null;
                    }
                    catch { }
                    _config = JsonConvert.DeserializeObject<Configuration>(json);
                    if (_config?.FastLoot == null || _config.LootBouncer == null)
                        TryMigrateFlatConfig(json);
                }

                if (_config == null)
                {
                    Debug.LogWarning("[LootQoL] Creating new configuration file.");
                    _config = new Configuration();
                }

                if (_config.FastLoot == null) _config.FastLoot = new FastLootSettings();
                if (_config.LootBouncer == null) _config.LootBouncer = new LootBouncerSettings();
                if (_config.LootBouncer.LootContainers == null)
                    _config.LootBouncer.LootContainers = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                if (_config.SortButton == null)
                    _config.SortButton = new SortButtonFeature.Settings();

                _sortButton = new SortButtonFeature(this, _serverRoot);
                if (!hadSortSection)
                {
                    var migrated = _sortButton.TryLoadStandaloneConfig();
                    if (migrated != null)
                        _config.SortButton = migrated;
                }
                _sortButton.Load();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LootQoL] FAIL: load config — using defaults. " + ex.Message);
                _config = new Configuration();
                if (_config.SortButton != null)
                    _config.SortButton.UsingDefaults = true;
            }

            if (_sortButton == null)
            {
                _sortButton = new SortButtonFeature(this, _serverRoot);
                _sortButton.Load();
            }

            SaveConfig();
        }

        private void TryMigrateFlatConfig(string json)
        {
            try
            {
                var jo = JObject.Parse(json);
                if (jo["FastLoot"] != null) return;
                _config = new Configuration
                {
                    FastLoot = jo.ToObject<FastLootSettings>() ?? new FastLootSettings(),
                    LootBouncer = new LootBouncerSettings()
                };
            }
            catch { }
        }

        public void SaveConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(_configPath, JsonConvert.SerializeObject(_config ?? new Configuration(), Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LootQoL] FAIL: save config: " + ex.Message);
            }
        }

        #endregion

        #region Lang / perms

        public void LoadDefaultMessages()
        {
            _lang.RegisterMessages(new Dictionary<string, string>
            {
                ["FASTLOOT_TAKE"] = "Take all",
                ["SlapMessage"] = "You didn't empty the container. You got slapped by the container!!!",
                ["Error.NoPermission"] = "You do not have permission to use this command",
                ["Format.ButtonText"] = "Sort",
                ["Format.Category"] = "<color=#D2691E>Category</color>",
                ["Format.Disabled"] = "<color=#B22222>Disabled</color>",
                ["Format.Enabled"] = "<color=#228B22>Enabled</color>",
                ["Format.Name"] = "<color=#00BFFF>Name</color>",
                ["Format.Prefix"] = "<color=#00FF00>[Sort Button]</color>: ",
                ["Info.ButtonStatus"] = "Sort Button is now {0}",
                ["Info.SortType"] = "Sort Type is now {0}",
                ["Info.Help"] = "List Commands:\n<color=#FFFF00>/{0}</color> - Enable/Disable Sort Button.\n<color=#FFFF00>/{0} <sort | type></color> - change sort type."
            }, "en");
            _lang.LoadHarmonyLanguageOverrides(_serverRoot, "LootQoL");
        }

        public void RegisterPermissions()
        {
            PermissionsBridge.RegisterPermission(PermFastLoot);
            PermissionsBridge.RegisterPermission(SortButtonFeature.PermUse);
            if (!PermissionsBridge.IsAvailable) return;
            if (!PermissionsBridge.GroupExists(AdminGroup))
                PermissionsBridge.CreateGroup(AdminGroup, "Administrators", 0);
            PermissionsBridge.GrantGroupPermission(AdminGroup, PermFastLoot);
            PermissionsBridge.GrantGroupPermission(AdminGroup, SortButtonFeature.PermUse);
        }

        public string GetMessage(string key) => _lang.GetMessage(key);

        public void OnServerInitialized()
        {
            _sortButton?.OnServerInitialized();
            UpdateLootContainerConfig();
            _wantBarrelHooks = false;
            if (_config?.LootBouncer?.LootContainers != null)
            {
                foreach (var kv in _config.LootBouncer.LootContainers)
                {
                    if (kv.Value && IsBarrel(kv.Key))
                    {
                        _wantBarrelHooks = true;
                        break;
                    }
                }
            }
        }

        public void Unload()
        {
            LootQoLMod.Instance?.Runner?.CancelAll();
            try { _sortButton?.Unload(); } catch { }
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected) continue;
                CuiHelper.DestroyUi(player, FastLootLayer);
            }
        }

        #endregion

        #region FastLoot

        public void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (player == null || entity == null) return;

            OnLootBouncerStart(player, entity as LootContainer);
            _sortButton?.OnLootEntity(player, entity);

            if (!PermissionsBridge.UserHasPermission(player.UserIDString, PermFastLoot))
                return;
            if (!IsValidLootTarget(entity))
                return;
            if (!HasInventory(entity))
                return;

            if (entity is StorageContainer storageContainer)
                storageContainer.onlyOneUser = true;

            UIFastLoot(player);
        }

        public void OnLootEntityEnd(BasePlayer player, BaseEntity entity)
        {
            if (player != null)
                CuiHelper.DestroyUi(player, FastLootLayer);
            _sortButton?.OnLootEntityEnd(player);
            OnLootBouncerEnd(player, entity as LootContainer);
        }

        private static bool IsValidLootTarget(BaseEntity entity)
        {
            if (entity == null) return false;
            if (entity is VendingMachine || entity is MarketTerminal) return false;
            return entity is LootContainer || entity is LootableCorpse || entity is DroppedItemContainer;
        }

        private static bool HasInventory(BaseEntity entity)
        {
            if (entity is StorageContainer storageContainer)
                return storageContainer.inventory != null;
            if (entity is DroppedItemContainer droppedContainer)
                return droppedContainer.inventory != null;
            if (entity is IItemContainerEntity itemContainerEntity)
                return itemContainerEntity.inventory != null;
            if (entity is LootableCorpse corpse)
                return corpse.containers != null && corpse.containers.Length > 0;
            return false;
        }

        private static ItemContainer GetInventory(BaseEntity entity)
        {
            if (entity is StorageContainer storageContainer)
                return storageContainer.inventory;
            if (entity is DroppedItemContainer droppedContainer)
                return droppedContainer.inventory;
            if (entity is IItemContainerEntity itemContainerEntity)
                return itemContainerEntity.inventory;
            if (entity is LootableCorpse corpse && corpse.containers != null && corpse.containers.Length > 0)
                return corpse.containers[0];
            return null;
        }

        public void FastLootTakeAll(BasePlayer player)
        {
            if (player == null) return;
            if (!PermissionsBridge.UserHasPermission(player.UserIDString, PermFastLoot))
                return;

            BaseEntity entitySource = player.inventory?.loot?.entitySource;
            if (entitySource == null || !IsValidLootTarget(entitySource) || player.inventory?.containerMain == null)
            {
                CuiHelper.DestroyUi(player, FastLootLayer);
                return;
            }

            if (entitySource is LootableCorpse corpse)
                MoveAllItemsFromCorpse(corpse, player.inventory.containerMain);
            else
            {
                ItemContainer sourceInventory = GetInventory(entitySource);
                if (sourceInventory != null)
                    MoveAllInventoryItems(sourceInventory, player.inventory.containerMain);
                else
                    CuiHelper.DestroyUi(player, FastLootLayer);
            }
        }

        private static void MoveAllItemsFromCorpse(LootableCorpse corpse, ItemContainer dest)
        {
            if (corpse.containers == null || corpse.containers.Length == 0)
                return;

            for (int i = 0; i < corpse.containers.Length; i++)
            {
                ItemContainer container = corpse.containers[i];
                if (container == null) continue;
                if (!CanLootCorpseContainer(corpse, container, i)) continue;
                MoveAllInventoryItems(container, dest);
            }
        }

        private static bool CanLootCorpseContainer(LootableCorpse corpse, ItemContainer container, int index)
        {
            if (container == null) return false;
            try
            {
                if (!corpse.CanLootContainer(container, index))
                    return false;
            }
            catch
            {
                if (corpse is NPCPlayerCorpse && (index == 1 || index == 2))
                    return false;
            }
            return container.itemList != null && container.itemList.Count > 0;
        }

        public static bool MoveAllInventoryItems(ItemContainer source, ItemContainer dest)
        {
            if (source == null || dest == null) return false;
            bool flag = true;
            int n = Mathf.Min(source.capacity, dest.capacity);
            for (int i = 0; i < n; i++)
            {
                Item slot = source.GetSlot(i);
                if (slot == null) continue;
                if (!slot.MoveToContainer(dest, -1, true, false))
                    flag = false;
            }
            return flag;
        }

        private void UIFastLoot(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, FastLootLayer);
            var fl = _config.FastLoot;
            var container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = fl.OffsetMin, OffsetMax = fl.OffsetMax },
                Image = { Color = fl.ColorBackground, Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
            }, "Overlay", FastLootLayer);

            container.Add(new CuiButton
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Button = { Color = "0 0 0 0", Command = "cui.endtest LOOTQOL take" },
                Text = { Text = _lang.GetMessage("FASTLOOT_TAKE"), FontSize = 17, Align = TextAnchor.MiddleCenter, Color = fl.ColorFont }
            }, FastLootLayer);
            CuiHelper.AddUi(player, container);
        }

        #endregion

        #region LootBouncer

        private static bool IsSteamId(ulong id) => id > 76561197960265728UL;

        private static bool IsBarrel(string shortPrefabName)
        {
            if (string.IsNullOrEmpty(shortPrefabName)) return false;
            return shortPrefabName.IndexOf("barrel", StringComparison.OrdinalIgnoreCase) >= 0
                   || shortPrefabName.IndexOf("roadsign", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsTradeBox(LootContainer lootContainer)
        {
            try
            {
                if (AppDomain.CurrentDomain.GetData("Trade_ApiType") is not Type apiType)
                    return false;
                MethodInfo mi = apiType.GetMethod("IsTradeBox", BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
                if (mi == null) return false;
                object target = mi.IsStatic ? null : apiType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                object obj = mi.Invoke(target, new object[] { lootContainer });
                return obj is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        private bool IsLootEnabled(LootContainer lootContainer)
        {
            if (lootContainer == null) return false;
            if (_config.LootBouncer.LootContainers.TryGetValue(lootContainer.ShortPrefabName, out bool enabled))
                return enabled;
            return true;
        }

        private void OnLootBouncerStart(BasePlayer player, LootContainer lootContainer)
        {
            if (lootContainer == null || lootContainer.net == null || player == null)
                return;
            if (IsTradeBox(lootContainer))
                return;
            if (!IsLootEnabled(lootContainer))
                return;

            ulong entityID = lootContainer.net.ID.Value;
            if (!_lootEntities.ContainsKey(entityID))
                _lootEntities[entityID] = lootContainer.inventory != null ? lootContainer.inventory.itemList.Count : 0;

            ulong pid = (ulong)player.userID;
            if (_entityPlayers.TryGetValue(entityID, out HashSet<ulong> looters))
                looters.Add(pid);
            else
                _entityPlayers[entityID] = new HashSet<ulong> { pid };

            var runner = LootQoLMod.Instance?.Runner;
            if (runner != null && runner.HasTimer(entityID))
            {
                _lootEntities[entityID] = 666;
                runner.Cancel(entityID);
            }
        }

        private void OnLootBouncerEnd(BasePlayer player, LootContainer lootContainer)
        {
            if (lootContainer == null || lootContainer.net == null || player == null)
                return;

            ulong entityID = lootContainer.net.ID.Value;
            if (!(lootContainer.inventory?.itemList?.Count > 0))
            {
                _lootEntities.Remove(entityID);
                if (_entityPlayers.TryGetValue(entityID, out HashSet<ulong> lootersEmpty))
                    lootersEmpty.Remove((ulong)player.userID);
                return;
            }

            if (_lootEntities.TryGetValue(entityID, out int tempItemsCount))
            {
                _lootEntities.Remove(entityID);
                if (lootContainer.inventory.itemList.Count < tempItemsCount)
                {
                    var runner = LootQoLMod.Instance?.Runner;
                    if (runner != null && !runner.HasTimer(entityID))
                    {
                        LootContainer captured = lootContainer;
                        runner.Once(entityID, _config.LootBouncer.TimeBeforeLootEmpty, () => DropItems(captured));
                    }
                }
                else if (_entityPlayers.TryGetValue(entityID, out HashSet<ulong> looters))
                {
                    looters.Remove((ulong)player.userID);
                }
                EmptyJunkPile(lootContainer);
            }
        }

        public void OnPlayerAttack(BasePlayer attacker, HitInfo info)
        {
            if (!_wantBarrelHooks) return;
            if (attacker == null || !IsSteamId((ulong)attacker.userID)) return;
            var barrel = info?.HitEntity as LootContainer;
            if (barrel == null || barrel.net == null) return;
            if (!IsBarrel(barrel.ShortPrefabName)) return;
            if (!IsLootEnabled(barrel)) return;

            ulong barrelID = barrel.net.ID.Value;
            ulong pid = (ulong)attacker.userID;
            if (_entityPlayers.TryGetValue(barrelID, out HashSet<ulong> attackers))
                attackers.Add(pid);
            else
                _entityPlayers[barrelID] = new HashSet<ulong> { pid };

            var runner = LootQoLMod.Instance?.Runner;
            if (runner != null && !runner.HasTimer(barrelID))
            {
                LootContainer captured = barrel;
                runner.Once(barrelID, _config.LootBouncer.TimeBeforeLootEmpty, () => DropItems(captured));
            }
            EmptyJunkPile(barrel);
        }

        public void OnEntityDeath(LootContainer barrel, HitInfo info)
        {
            if (!_wantBarrelHooks) return;
            if (barrel == null || barrel.net == null) return;
            if (!IsBarrel(barrel.ShortPrefabName)) return;
            var attacker = info?.InitiatorPlayer;
            if (attacker == null || !IsSteamId((ulong)attacker.userID)) return;
            if (_entityPlayers.TryGetValue(barrel.net.ID.Value, out HashSet<ulong> attackers))
                attackers.Remove((ulong)attacker.userID);
        }

        public void OnEntityKill(LootContainer lootContainer)
        {
            if (lootContainer == null || lootContainer.net == null) return;
            ulong entityID = lootContainer.net.ID.Value;
            _lootEntities.Remove(entityID);
            LootQoLMod.Instance?.Runner?.Cancel(entityID);

            if (!_entityPlayers.TryGetValue(entityID, out HashSet<ulong> playerIDs))
                return;
            _entityPlayers.Remove(entityID);

            // Slap plugin does not exist in this Harmony stack — no-op even if config.SlapPlayer is true.
            _ = playerIDs;
        }

        private void DropItems(LootContainer lootContainer)
        {
            if (lootContainer == null || lootContainer.IsDestroyed)
                return;
            if (_config.LootBouncer.RemoveItems)
                lootContainer.inventory?.Clear();
            else
                DropUtil.DropItems(lootContainer.inventory, lootContainer.GetDropPosition());
            lootContainer.RemoveMe();
        }

        private static bool SpawnGroupsContains(SpawnGroup[] groups, SpawnGroup group)
        {
            if (groups == null || group == null) return false;
            for (int i = 0; i < groups.Length; i++)
            {
                if (groups[i] == group)
                    return true;
            }
            return false;
        }

        private void EmptyJunkPile(LootContainer lootContainer)
        {
            if (!_config.LootBouncer.EmptyJunkpile) return;
            var spawnPoint = lootContainer.GetComponent<SpawnPointInstance>();
            var spawnGroup = spawnPoint != null ? spawnPoint.parentSpawnPointUser as SpawnGroup : null;
            if (spawnGroup == null) return;

            var junkPiles = Pool.Get<List<JunkPile>>();
            Vis.Entities(lootContainer.transform.position, 10f, junkPiles, Layers.Solid);
            JunkPile junkPile = null;
            for (int i = 0; i < junkPiles.Count; i++)
            {
                JunkPile jp = junkPiles[i];
                if (jp != null && SpawnGroupsContains(jp.spawngroups, spawnGroup))
                {
                    junkPile = jp;
                    break;
                }
            }
            Pool.FreeUnmanaged(ref junkPiles);
            if (junkPile == null || junkPile.net == null) return;

            ulong junkId = junkPile.net.ID.Value;
            var runner = LootQoLMod.Instance?.Runner;
            if (runner == null || runner.HasTimer(junkId)) return;

            JunkPile captured = junkPile;
            float delay = _config.LootBouncer.TimeBeforeJunkpileEmpty;
            bool dropNearby = _config.LootBouncer.DropNearbyLoot;
            runner.Once(junkId, delay, () =>
            {
                if (captured == null || captured.IsDestroyed) return;
                if (dropNearby)
                {
                    var lootContainers = Pool.Get<List<LootContainer>>();
                    Vis.Entities(captured.transform.position, 10f, lootContainers, Layers.Solid);
                    for (int i = 0; i < lootContainers.Count; i++)
                    {
                        LootContainer loot = lootContainers[i];
                        if (loot == null) continue;
                        var lootSpawn = loot.GetComponent<SpawnPointInstance>();
                        var lootSpawnGroup = lootSpawn != null ? lootSpawn.parentSpawnPointUser as SpawnGroup : null;
                        if (lootSpawnGroup != null && SpawnGroupsContains(captured.spawngroups, lootSpawnGroup))
                            DropItems(loot);
                    }
                    Pool.FreeUnmanaged(ref lootContainers);
                }
                captured.SinkAndDestroy();
            });
        }

        private void UpdateLootContainerConfig()
        {
            try
            {
                if (GameManifest.Current?.entities == null) return;
                string[] entities = GameManifest.Current.entities;
                for (int i = 0; i < entities.Length; i++)
                {
                    string prefab = entities[i];
                    if (string.IsNullOrEmpty(prefab)) continue;
                    var go = GameManager.server.FindPrefab(prefab.ToLowerInvariant());
                    if (go == null) continue;
                    var lootContainer = go.GetComponent<LootContainer>();
                    if (lootContainer == null || string.IsNullOrEmpty(lootContainer.ShortPrefabName))
                        continue;
                    if (!_config.LootBouncer.LootContainers.ContainsKey(lootContainer.ShortPrefabName))
                    {
                        string name = lootContainer.ShortPrefabName;
                        bool enabled = name.IndexOf("stocking", StringComparison.OrdinalIgnoreCase) < 0
                                       && name.IndexOf("roadsign", StringComparison.OrdinalIgnoreCase) < 0;
                        _config.LootBouncer.LootContainers.Add(name, enabled);
                    }
                }
                SaveConfig();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LootQoL] UpdateLootContainerConfig: " + ex.Message);
            }
        }

        #endregion
    }

    internal sealed class LangStore
    {
        private readonly Dictionary<string, string> _en = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private bool _fileLoaded;

        public void RegisterMessages(Dictionary<string, string> messages, string language)
        {
            if (messages == null) return;
            if (!string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)) return;
            foreach (var kv in messages)
                _en[kv.Key] = kv.Value ?? "";
        }

        public void LoadHarmonyLanguageOverrides(string serverRoot, string modName)
        {
            if (_fileLoaded) return;
            _fileLoaded = true;
            try
            {
                string path = Path.Combine(serverRoot, "HarmonyLanguage", modName + ".json");
                if (!File.Exists(path)) return;
                var map = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                if (map == null || map.Count == 0) return;
                foreach (var kv in map)
                    _en[kv.Key] = kv.Value ?? "";
                Debug.Log($"[LootQoL] Loaded {map.Count} language strings from HarmonyLanguage/{modName}.json");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LootQoL] HarmonyLanguage load failed: " + ex.Message);
            }
        }

        public string GetMessage(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            return _en.TryGetValue(key, out string msg) && !string.IsNullOrEmpty(msg) ? msg : key;
        }
    }
}
