using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace CustomMagazineHarmony
{
    public sealed class CustomMagazinePlugin
    {
        private readonly string _configPath;
        private PluginConfig _config;
        private bool _hasCrateSpawns;

        public PluginConfig Config => _config;

        public class CrateConfig
        {
            [JsonProperty("Prefab")]
            public string Prefab { get; set; }

            [JsonProperty("Chance probability [0.0-100.0]")]
            public float Chance { get; set; }
        }

        public class MagazineConfig
        {
            [JsonProperty("Name")]
            public string Name { get; set; }

            [JsonProperty("SkinID")]
            public ulong SkinID { get; set; }

            [JsonProperty("Ammo Multiplier")]
            public float Scale { get; set; }

            [JsonProperty("Settings spawn in crates")]
            public List<CrateConfig> Crates { get; set; }
        }

        public class PluginConfig
        {
            [JsonProperty("List of custom magazines")]
            public List<MagazineConfig> Magazines { get; set; }

            [JsonProperty("Allow OnItemSplit hook to work? [true/false]")]
            public bool OnItemSplit { get; set; } = true;

            [JsonProperty("Configuration version")]
            public VersionBlock PluginVersion { get; set; }
        }

        public class VersionBlock
        {
            public int Major { get; set; } = 1;
            public int Minor { get; set; }
            public int Patch { get; set; } = 9;
        }

        public CustomMagazinePlugin(string serverRoot)
        {
            _configPath = Path.Combine(serverRoot, "HarmonyConfig", "CustomMagazine.json");
        }

        public void LoadConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(_configPath))
                    _config = JsonConvert.DeserializeObject<PluginConfig>(File.ReadAllText(_configPath));

                if (_config == null)
                {
                    Debug.LogWarning("[CustomMagazine] Creating default configuration.");
                    _config = DefaultConfig();
                }

                if (_config.Magazines == null)
                    _config.Magazines = new List<MagazineConfig>();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CustomMagazine] FAIL: load config — using defaults. " + ex.Message);
                _config = DefaultConfig();
            }
            SaveConfig();
        }

        public void SaveConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(_configPath, JsonConvert.SerializeObject(_config ?? DefaultConfig(), Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CustomMagazine] FAIL: save config: " + ex.Message);
            }
        }

        private static PluginConfig DefaultConfig()
        {
            return new PluginConfig
            {
                Magazines = new List<MagazineConfig>
                {
                    new MagazineConfig
                    {
                        Name = "Extended Magazine +50%",
                        SkinID = 2817854052,
                        Scale = 1.5f,
                        Crates = new List<CrateConfig>
                        {
                            new CrateConfig { Prefab = "assets/bundled/prefabs/radtown/crate_normal.prefab", Chance = 25f }
                        }
                    },
                    new MagazineConfig
                    {
                        Name = "Extended Magazine +75%",
                        SkinID = 2817854377,
                        Scale = 1.75f,
                        Crates = new List<CrateConfig>
                        {
                            new CrateConfig { Prefab = "assets/bundled/prefabs/radtown/crate_elite.prefab", Chance = 25f }
                        }
                    },
                    new MagazineConfig
                    {
                        Name = "Extended Magazine +100%",
                        SkinID = 2817854677,
                        Scale = 2f,
                        Crates = new List<CrateConfig>
                        {
                            new CrateConfig { Prefab = "assets/prefabs/misc/supply drop/supply_drop.prefab", Chance = 25f },
                            new CrateConfig { Prefab = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate_oilrig.prefab", Chance = 25f },
                            new CrateConfig { Prefab = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab", Chance = 25f }
                        }
                    }
                },
                OnItemSplit = true,
                PluginVersion = new VersionBlock()
            };
        }

        public void OnServerInitialized()
        {
            _hasCrateSpawns = false;
            if (_config?.Magazines == null) return;
            for (int i = 0; i < _config.Magazines.Count; i++)
            {
                var mag = _config.Magazines[i];
                if (mag?.Crates == null) continue;
                if (mag.Crates.Count > 0)
                {
                    _hasCrateSpawns = true;
                    break;
                }
            }
        }

        public MagazineConfig FindBySkin(ulong skin)
        {
            if (_config?.Magazines == null) return null;
            for (int i = 0; i < _config.Magazines.Count; i++)
            {
                var mag = _config.Magazines[i];
                if (mag != null && mag.SkinID == skin)
                    return mag;
            }
            return null;
        }

        public bool IsCustomSkin(ulong skin) => FindBySkin(skin) != null;

        public void OnLootSpawn(LootContainer container)
        {
            if (!_hasCrateSpawns) return;
            if (container == null || container.inventory == null || _config?.Magazines == null)
                return;

            string prefab = container.PrefabName;
            MagazineConfig magazine = null;
            CrateConfig crate = null;
            for (int i = 0; i < _config.Magazines.Count; i++)
            {
                var mag = _config.Magazines[i];
                if (mag?.Crates == null) continue;
                for (int j = 0; j < mag.Crates.Count; j++)
                {
                    var c = mag.Crates[j];
                    if (c != null && string.Equals(c.Prefab, prefab, StringComparison.OrdinalIgnoreCase))
                    {
                        magazine = mag;
                        crate = c;
                        break;
                    }
                }
                if (magazine != null) break;
            }

            if (magazine == null || crate == null) return;
            if (UnityEngine.Random.Range(0f, 100f) > crate.Chance) return;

            if (container.inventory.itemList.Count == container.inventory.capacity)
                container.inventory.capacity++;
            Item item = GetMagazine(magazine);
            if (item == null) return;
            if (!item.MoveToContainer(container.inventory))
                item.Remove();
        }

        public void ApplyMagazineScale(BaseProjectile weapon)
        {
            if (weapon == null || weapon.children == null) return;
            for (int i = 0; i < weapon.children.Count; i++)
            {
                BaseEntity entity = weapon.children[i];
                if (entity == null) continue;
                if (entity.ShortPrefabName != "extendedmags.entity") continue;
                var magazine = entity as ProjectileWeaponMod;
                if (magazine == null) continue;
                if (magazine.skinID == 0) return;
                MagazineConfig config = FindBySkin(magazine.skinID);
                if (config == null) return;
                var cap = magazine.magazineCapacity;
                cap.scalar = config.Scale;
                magazine.magazineCapacity = cap;
                if (weapon.primaryMagazine != null)
                    weapon.primaryMagazine.capacity = (int)(weapon.primaryMagazine.definition.builtInSize * config.Scale);
                return;
            }
        }

        public bool TryCustomSplit(Item item, int amount, out Item result)
        {
            result = null;
            if (_config == null || !_config.OnItemSplit) return false;
            if (item == null || amount <= 0) return false;
            if (!IsCustomSkin(item.skin)) return false;

            item.amount -= amount;
            Item newItem = ItemManager.CreateByItemID(item.info.itemid, amount, item.skin);
            if (newItem == null) return false;
            newItem.name = item.name;
            item.MarkDirty();
            result = newItem;
            return true;
        }

        public static Item GetMagazine(MagazineConfig config)
        {
            Item item = ItemManager.CreateByName("weapon.mod.extendedmags", 1, config.SkinID);
            if (item == null) return null;
            if (!string.IsNullOrEmpty(config.Name))
                item.name = config.Name;
            var magazine = item.GetHeldEntity() as ProjectileWeaponMod;
            if (magazine != null)
            {
                var cap = magazine.magazineCapacity;
                cap.scalar = config.Scale;
                magazine.magazineCapacity = cap;
            }
            return item;
        }

        public void ConsoleGiveMagazine(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Args == null || arg.Args.Length < 2 || arg.Player() != null)
                return;

            if (!ulong.TryParse(arg.Args[0].ToString(), out ulong skinid))
            {
                Debug.LogWarning("[CustomMagazine] givemagazine: invalid SkinID");
                return;
            }

            MagazineConfig config = FindBySkin(skinid);
            if (config == null)
            {
                Debug.LogWarning($"[CustomMagazine] Custom Magazine with SkinID {skinid} not found in plugin configuration!");
                return;
            }

            if (!ulong.TryParse(arg.Args[1].ToString(), out ulong steamid))
            {
                Debug.LogWarning("[CustomMagazine] givemagazine: invalid SteamID");
                return;
            }

            BasePlayer target = BasePlayer.FindByID(steamid);
            if (target == null)
            {
                Debug.LogWarning($"[CustomMagazine] Player with SteamID {steamid} not found!");
                return;
            }

            Item item = GetMagazine(config);
            if (item == null) return;
            int slots = target.inventory.containerMain.capacity + target.inventory.containerBelt.capacity;
            int taken = target.inventory.containerMain.itemList.Count + target.inventory.containerBelt.itemList.Count;
            if (slots - taken > 0)
                target.inventory.GiveItem(item);
            else
                item.Drop(target.transform.position, Vector3.up);
            Debug.Log($"[CustomMagazine] Player {target.displayName} has successfully received a custom magazine with SkinID = {config.SkinID}");
        }
    }
}
