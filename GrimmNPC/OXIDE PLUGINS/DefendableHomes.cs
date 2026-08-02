using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using UnityEngine;
using Facepunch;
using UnityEngine.AI;
using System.Collections;
using Oxide.Game.Rust.Cui;
using System.IO;
using Rust;
using UnityEngine.Networking;
using System.Reflection;
using Oxide.Plugins.DefendableHomesExtensionMethods;

namespace Oxide.Plugins
{
    [Info("DefendableHomes", "KpucTaJl | Grimm530", "1.1.71")]
    internal class DefendableHomes : RustPlugin
    {
        #region Config
        private const bool En = true;

        private PluginConfig _config;

        protected override void LoadDefaultConfig()
        {
            Puts("Creating a default config...");
            _config = PluginConfig.DefaultConfig();
            _config.PluginVersion = Version;
            SaveConfig();
            Puts("Creation of the default config completed!");
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<PluginConfig>();
            if (_config.PluginVersion < Version) UpdateConfigValues();
        }

        private void UpdateConfigValues()
        {
            Puts("Config update detected! Updating config values...");
            if (_config.PluginVersion < new VersionNumber(1, 0, 1))
            {
                _config.DamageNpc = true;
                _config.TargetNpc = true;
            }
            if (_config.PluginVersion < new VersionNumber(1, 1, 0))
            {
                foreach (DifficultyLevelConfig difficultyLevel in _config.DifficultyLevels)
                {
                    if (difficultyLevel.Name == "EASY")
                    {
                        difficultyLevel.MaxTurrets = 3;
                        difficultyLevel.MaxFoundations = 9;
                    }
                    else if (difficultyLevel.Name == "MEDIUM")
                    {
                        difficultyLevel.MaxTurrets = 6;
                        difficultyLevel.MaxFoundations = 16;
                    }
                    else if (difficultyLevel.Name == "HARD")
                    {
                        difficultyLevel.MaxTurrets = 9;
                        difficultyLevel.MaxFoundations = 25;
                    }
                    else
                    {
                        difficultyLevel.MaxTurrets = 6;
                        difficultyLevel.MaxFoundations = 16;
                    }
                    difficultyLevel.Cooldown = 21600;
                }
            }
            if (_config.PluginVersion < new VersionNumber(1, 1, 1))
            {
                _config.Bomber = new BomberConfig
                {
                    WearItems = new HashSet<NpcWear>
                    {
                        new NpcWear { ShortName = "scarecrow.suit", SkinId = 0 },
                        new NpcWear { ShortName = "metal.plate.torso", SkinId = 860210174 },
                    },
                    Health = 100f,
                    AttackRangeMultiplier = 1f,
                    Speed = 5.5f,
                    DamageFoundation = 150f,
                    DamageConstruction = 150f,
                    DamagePlayer = 60f
                };
            }
            if (_config.PluginVersion < new VersionNumber(1, 1, 2))
            {
                foreach (DifficultyLevelConfig level in _config.DifficultyLevels) level.MaxCount = 3;
                _config.Gui = new GuiConfig
                {
                    IsGui = true,
                    OffsetMinY = "-56"
                };
                _config.GameTip = new GameTipConfig
                {
                    IsGameTip = false,
                    Style = 2
                };
                _config.OnItemSplit = true;
            }
            if (_config.PluginVersion < new VersionNumber(1, 1, 5))
            {
                int countEasy = 0, countMedium = 0;
                foreach (DifficultyLevelConfig difficultyLevel in _config.DifficultyLevels)
                {
                    switch (difficultyLevel.Name)
                    {
                        case "EASY":
                            difficultyLevel.MinFoundations = 4;
                            countEasy = difficultyLevel.MaxFoundations;
                            break;
                        case "MEDIUM":
                            difficultyLevel.MinFoundations = countEasy > 0 ? countEasy : 4;
                            countMedium = difficultyLevel.MaxFoundations;
                            break;
                        case "HARD":
                            difficultyLevel.MinFoundations = countMedium > 0 ? countMedium : 4;
                            break;
                        default:
                            difficultyLevel.MinFoundations = 4;
                            break;
                    }
                }

                _config.Economy = new EconomyConfig
                {
                    Plugins = new HashSet<string> { "Economics", "Server Rewards", "IQEconomic", "XPerience" },
                    Min = 0,
                    KillNpc = new Dictionary<string, double>
                    {
                        ["Sledge"] = 0.2,
                        ["Juggernaut"] = 0.8,
                        ["Rocketman"] = 0.8,
                        ["Bomber"] = 0.5
                    },
                    Commands = new HashSet<string>()
                };
            }
            _config.PluginVersion = Version;
            Puts("Config update completed!");
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        public class ItemConfig
        {
            [JsonProperty("ShortName")] public string ShortName { get; set; }
            [JsonProperty(En ? "Minimum" : "Минимальное кол-во")] public int MinAmount { get; set; }
            [JsonProperty(En ? "Maximum" : "Максимальное кол-во")] public int MaxAmount { get; set; }
            [JsonProperty(En ? "Chance probability [0.0-100.0]" : "Шанс выпадения предмета [0.0-100.0]")] public float Chance { get; set; }
            [JsonProperty(En ? "Is this a blueprint? [true/false]" : "Это чертеж? [true/false]")] public bool IsBluePrint { get; set; }
            [JsonProperty("SkinID (0 - default)")] public ulong SkinId { get; set; }
            [JsonProperty(En ? "Name (empty - default)" : "Название (empty - default)")] public string Name { get; set; }
        }

        public class LootTableConfig
        {
            [JsonProperty(En ? "Minimum number of items" : "Минимальное кол-во элементов")] public int Min { get; set; }
            [JsonProperty(En ? "Maximum number of items" : "Максимальное кол-во элементов")] public int Max { get; set; }
            [JsonProperty(En ? "Use minimum and maximum values? [true/false]" : "Использовать минимальное и максимальное значение? [true/false]")] public bool UseCount { get; set; }
            [JsonProperty(En ? "List of items" : "Список предметов")] public List<ItemConfig> Items { get; set; }
        }

        public class PrefabConfig
        {
            [JsonProperty(En ? "Chance probability [0.0-100.0]" : "Шанс выпадения [0.0-100.0]")] public float Chance { get; set; }
            [JsonProperty(En ? "Prefab path" : "Путь к prefab-у")] public string PrefabDefinition { get; set; }
        }

        public class PrefabLootTableConfig
        {
            [JsonProperty(En ? "Minimum number of prefabs" : "Минимальное кол-во prefab-ов")] public int Min { get; set; }
            [JsonProperty(En ? "Maximum number of prefabs" : "Максимальное кол-во prefab-ов")] public int Max { get; set; }
            [JsonProperty(En ? "Use minimum and maximum values? [true/false]" : "Использовать минимальное и максимальное значение? [true/false]")] public bool UseCount { get; set; }
            [JsonProperty(En ? "List of prefabs" : "Список prefab-ов")] public List<PrefabConfig> Prefabs { get; set; }
        }

        public class HackCrateConfig
        {
            [JsonProperty(En ? "Time to unlock Crates [sec.]" : "Время разблокировки ящиков [sec.]")] public float UnlockTime { get; set; }
            [JsonProperty(En ? "Which loot table should the plugin use? (0 - default; 1 - own; 2 - AlphaLoot; 3 - CustomLoot; 4 - loot table of the Rust objects; 5 - combine the 1 and 4 methods)" : "Какую таблицу лута необходимо использовать? (0 - стандартную; 1 - собственную; 2 - AlphaLoot; 3 - CustomLoot; 4 - таблица предметов объектов Rust; 5 - совместить 1 и 4 методы)")] public int TypeLootTable { get; set; }
            [JsonProperty(En ? "Loot table from prefabs (if the loot table type is 4 or 5)" : "Таблица предметов из prefab-ов (если тип таблицы предметов - 4 или 5)")] public PrefabLootTableConfig PrefabLootTable { get; set; }
            [JsonProperty(En ? "Own loot table (if the loot table type is 1 or 5)" : "Собственная таблица предметов (если тип таблицы предметов - 1 или 5)")] public LootTableConfig OwnLootTable { get; set; }
        }

        public class GuiConfig
        {
            [JsonProperty(En ? "Use the GUI? [true/false]" : "Использовать ли GUI? [true/false]")] public bool IsGui { get; set; }
            [JsonProperty("OffsetMin Y")] public string OffsetMinY { get; set; }
        }

        public class GameTipConfig
        {
            [JsonProperty(En ? "Use Facepunch Game Tips (notification bar above hotbar)? [true/false]" : "Использовать ли Facepunch Game Tip (оповещения над слотами быстрого доступа игрока)? [true/false]")] public bool IsGameTip { get; set; }
            [JsonProperty(En ? "Style (0 - Blue Normal, 1 - Red Normal, 2 - Blue Long, 3 - Blue Short, 4 - Server Event)" : "Стиль (0 - Blue Normal, 1 - Red Normal, 2 - Blue Long, 3 - Blue Short, 4 - Server Event)")] public int Style { get; set; }
        }

        public class GuiAnnouncementsConfig
        {
            [JsonProperty(En ? "Do you use the GUI Announcements plugin? [true/false]" : "Использовать ли плагин GUI Announcements? [true/false]")] public bool IsGuiAnnouncements { get; set; }
            [JsonProperty(En ? "Banner color" : "Цвет баннера")] public string BannerColor { get; set; }
            [JsonProperty(En ? "Text color" : "Цвет текста")] public string TextColor { get; set; }
            [JsonProperty(En ? "Adjust Vertical Position" : "Отступ от верхнего края")] public float ApiAdjustVPosition { get; set; }
        }

        public class NotifyConfig
        {
            [JsonProperty(En ? "Do you use the Notify plugin? [true/false]" : "Использовать ли плагин Notify? [true/false]")] public bool IsNotify { get; set; }
            [JsonProperty(En ? "Type" : "Тип")] public int Type { get; set; }
        }

        public class EconomyConfig
        {
            [JsonProperty(En ? "Which plugins do you want to use for rewards? (Economics, Server Rewards, IQEconomic, XPerience)" : "Какие плагины экономики вы хотите использовать? (Economics, Server Rewards, IQEconomic, XPerience)")] public HashSet<string> Plugins { get; set; }
            [JsonProperty(En ? "The minimum value that a player must collect to earn rewards" : "Минимальное значение, которое игрок должен заработать, чтобы получить баллы за экономику")] public double Min { get; set; }
            [JsonProperty(En ? "Killing an Npc" : "Убийство Npc")] public Dictionary<string, double> KillNpc { get; set; }
            [JsonProperty(En ? "List of commands that are executed in the console at the end of the event ({steamid} - the player who collected the highest number of points)" : "Список команд, которые выполняются в консоли по окончанию ивента ({steamid} - игрок, который набрал наибольшее кол-во баллов)")] public HashSet<string> Commands { get; set; }
        }

        public class NpcWear
        {
            [JsonProperty("ShortName")] public string ShortName { get; set; }
            [JsonProperty("SkinID (0 - default)")] public ulong SkinId { get; set; }
        }

        public class SledgeBelt
        {
            [JsonProperty("ShortName")] public string ShortName { get; set; }
            [JsonProperty("SkinID (0 - default)")] public ulong SkinId { get; set; }
            [JsonProperty(En ? "Probability Percent [0.0-100.0]" : "Вероятность [0.0-100.0]")] public float Chance { get; set; }
        }

        public class JuggernautBelt
        {
            [JsonProperty("ShortName")] public string ShortName { get; set; }
            [JsonProperty("SkinID (0 - default)")] public ulong SkinId { get; set; }
            [JsonProperty(En ? "Mods" : "Модификации на оружие")] public HashSet<string> Mods { get; set; }
            [JsonProperty(En ? "Ammo" : "Боеприпасы")] public string Ammo { get; set; }
            [JsonProperty(En ? "Probability Percent [0.0-100.0]" : "Вероятность [0.0-100.0]")] public float Chance { get; set; }
            [JsonProperty(En ? "Damage Scale" : "Множитель урона")] public float DamageScale { get; set; }
        }

        public class RocketmanBelt
        {
            [JsonProperty("ShortName")] public string ShortName { get; set; }
            [JsonProperty("SkinID (0 - default)")] public ulong SkinId { get; set; }
            [JsonProperty(En ? "Mods" : "Модификации на оружие")] public HashSet<string> Mods { get; set; }
            [JsonProperty(En ? "Ammo" : "Боеприпасы")] public string Ammo { get; set; }
            [JsonProperty(En ? "Probability Percent [0.0-100.0]" : "Вероятность [0.0-100.0]")] public float Chance { get; set; }
        }

        public class SledgeConfig
        {
            [JsonProperty(En ? "Worn items" : "Одежда")] public HashSet<NpcWear> WearItems { get; set; }
            [JsonProperty(En ? "Weapons" : "Оружие")] public HashSet<SledgeBelt> Weapons { get; set; }
            [JsonProperty(En ? "The probability of Beancan Grenade appearance" : "Вероятность появления бобовой гранаты")] public float BeancanGrenade { get; set; }
            [JsonProperty(En ? "Health Points" : "Кол-во ХП")] public float Health { get; set; }
            [JsonProperty(En ? "Damage Scale" : "Множитель урона")] public float DamageScale { get; set; }
            [JsonProperty(En ? "Turret damage scale" : "Множитель урона от турелей")] public float TurretDamageScale { get; set; }
            [JsonProperty(En ? "Speed" : "Скорость")] public float Speed { get; set; }
        }

        public class JuggernautConfig
        {
            [JsonProperty(En ? "Worn items" : "Одежда")] public HashSet<NpcWear> WearItems { get; set; }
            [JsonProperty(En ? "Weapons" : "Оружие")] public HashSet<JuggernautBelt> Weapons { get; set; }
            [JsonProperty(En ? "The probability of Beancan Grenade appearance" : "Вероятность появления бобовой гранаты")] public float BeancanGrenade { get; set; }
            [JsonProperty(En ? "Health Points" : "Кол-во ХП")] public float Health { get; set; }
            [JsonProperty(En ? "Turret damage scale" : "Множитель урона от турелей")] public float TurretDamageScale { get; set; }
            [JsonProperty(En ? "Aim Cone Scale" : "Множитель разброса")] public float AimConeScale { get; set; }
            [JsonProperty(En ? "Speed" : "Скорость")] public float Speed { get; set; }
            [JsonProperty(En ? "Attack Range Multiplier" : "Множитель радиуса атаки")] public float AttackRangeMultiplier { get; set; }
        }

        public class RocketmanConfig
        {
            [JsonProperty(En ? "Worn items" : "Одежда")] public HashSet<NpcWear> WearItems { get; set; }
            [JsonProperty(En ? "Weapons" : "Оружие")] public HashSet<RocketmanBelt> Weapons { get; set; }
            [JsonProperty(En ? "Rocket ammo shortname" : "Shortname ракет")] public string RocketAmmoShortName { get; set; }
            [JsonProperty(En ? "The probability of F1 Grenade appearance" : "Вероятность появления гранаты F1")] public float GrenadeF1 { get; set; }
            [JsonProperty(En ? "Health Points" : "Кол-во ХП")] public float Health { get; set; }
            [JsonProperty(En ? "Damage Scale" : "Множитель урона")] public float DamageScale { get; set; }
            [JsonProperty(En ? "Turret damage scale" : "Множитель урона от турелей")] public float TurretDamageScale { get; set; }
            [JsonProperty(En ? "Aim Cone Scale" : "Множитель разброса")] public float AimConeScale { get; set; }
            [JsonProperty(En ? "Speed" : "Скорость")] public float Speed { get; set; }
            [JsonProperty(En ? "Attack Range Multiplier" : "Множитель радиуса атаки")] public float AttackRangeMultiplier { get; set; }
        }

        public class BomberConfig
        {
            [JsonProperty(En ? "Worn items" : "Одежда")] public HashSet<NpcWear> WearItems { get; set; }
            [JsonProperty(En ? "Health Points" : "Кол-во ХП")] public float Health { get; set; }
            [JsonProperty(En ? "Attack Range Multiplier" : "Множитель радиуса атаки")] public float AttackRangeMultiplier { get; set; }
            [JsonProperty(En ? "Speed" : "Скорость")] public float Speed { get; set; }
            [JsonProperty(En ? "NPC damage to foundation" : "Кол-во урона, которое наносит NPC по фундаменту")] public float DamageFoundation { get; set; }
            [JsonProperty(En ? "NPC damage to сonstruction" : "Кол-во урона, которое наносит NPC по строительному объекту")] public float DamageConstruction { get; set; }
            [JsonProperty(En ? "NPC damage to player" : "Кол-во урона, которое наносит NPC по игроку")] public float DamagePlayer { get; set; }
        }

        public class AmountConfig
        {
            [JsonProperty(En ? "Chance probability [0.0-100.0]" : "Шанс [0.0-100.0]")] public float Chance { get; set; }
            [JsonProperty(En ? "Count" : "Кол-во")] public int Count { get; set; }
        }

        public class PresetConfig
        {
            [JsonProperty(En ? "Type of NPC" : "Тип NPC")] public string ShortName { get; set; }
            [JsonProperty(En ? "Setting the number of NPCs depending on the probability" : "Настройка кол-ва NPC в зависимoсти от вероятности")] public HashSet<AmountConfig> Config { get; set; }
        }

        public class WaveConfig
        {
            [JsonProperty(En ? "Level" : "Уровень")] public int Level { get; set; }
            [JsonProperty(En ? "Preparation time [sec.]" : "Время подготовки [sec.]")] public int TimeToStart { get; set; }
            [JsonProperty(En ? "Duration [sec.]" : "Длительность [sec.]")] public int Duration { get; set; }
            [JsonProperty(En ? "Time until appearance of new NPCs [sec.]" : "Время появления новых NPC [sec.]")] public int TimerNpc { get; set; }
            [JsonProperty(En ? "NPC Sets" : "Наборы NPC")] public HashSet<PresetConfig> Presets { get; set; }
        }

        public class DifficultyLevelConfig
        {
            [JsonProperty(En ? "Name" : "Название")] public string Name { get; set; }
            [JsonProperty(En ? "The maximum number of Auto Turrets in the house to start the event" : "Максимальное кол-во турелей в доме для запуска ивента")] public int MaxTurrets { get; set; }
            [JsonProperty(En ? "The minimum number of foundations in the house to start the event" : "Минимальное кол-во фундаментов в доме для запуска ивента")] public int MinFoundations { get; set; }
            [JsonProperty(En ? "The maximum number of foundations in the house to start the event" : "Максимальное кол-во фундаментов в доме для запуска ивента")] public int MaxFoundations { get; set; }
            [JsonProperty(En ? "Amount of time that a player will have to wait to launch an event at the same level once an event has finished" : "Время, которое игрок не сможет запускать ивент этого уровня, после того как ивент был окончен [sec.]")] public double Cooldown { get; set; }
            [JsonProperty(En ? "The maximum number of events allowed simultaneously for this difficulty" : "Максимальное кол-во ивентов этого уровня одновременно на сервере")] public int MaxCount { get; set; }
            [JsonProperty(En ? "List of attack waves" : "Список волн атаки")] public HashSet<WaveConfig> Waves { get; set; }
            [JsonProperty(En ? "Locked crate setting" : "Настройка заблокированного ящика")] public HackCrateConfig HackCrate { get; set; }
        }

        public class CrateConfig
        {
            [JsonProperty("Prefab")] public string Prefab { get; set; }
            [JsonProperty(En ? "Chance [0.0-100.0]" : "Шанс выпадения предмета [0.0-100.0]")] public float Chance { get; set; }
        }

        public class FlareConfig
        {
            [JsonProperty(En ? "Name" : "Название")] public string Name { get; set; }
            [JsonProperty("SkinID")] public ulong SkinId { get; set; }
            [JsonProperty(En ? "Name of difficulty level" : "Название уровня сложности")] public string NameDifficultyLevel { get; set; }
            [JsonProperty(En ? "Crate appearance settings" : "Настройка появления в ящиках")] public HashSet<CrateConfig> Crates { get; set; }
        }

        private class PluginConfig
        {
            [JsonProperty(En ? "NPC Configuration - Sledge" : "Конфигурация NPC - Sledge")] public SledgeConfig Sledge { get; set; }
            [JsonProperty(En ? "NPC Configuration - Juggernaut" : "Конфигурация NPC - Juggernaut")] public JuggernautConfig Juggernaut { get; set; }
            [JsonProperty(En ? "NPC Configuration - Rocketman" : "Конфигурация NPC - Rocketman")] public RocketmanConfig Rocketman { get; set; }
            [JsonProperty(En ? "NPC Configuration - Bomber" : "Конфигурация NPC - Bomber")] public BomberConfig Bomber { get; set; }
            [JsonProperty(En ? "Infinite ammo for NPCs" : "Бесконечные патроны для NPC")] public bool InfiniteAmmo { get; set; }
            [JsonProperty(En ? "NPC damage multipliers depending on the attacker's weapon" : "Множители урона по NPC в зависимости от оружия атакующего")] public Dictionary<string, float> WeaponToScaleDamageNpc { get; set; }
            [JsonProperty(En ? "Difficulty levels setting" : "Настройка уровней сложности")] public HashSet<DifficultyLevelConfig> DifficultyLevels { get; set; }
            [JsonProperty(En ? "Flares setting" : "Настройка сигнальных шашек")] public HashSet<FlareConfig> Flares { get; set; }
            [JsonProperty(En ? "GUI setting" : "Настройки GUI")] public GuiConfig Gui { get; set; }
            [JsonProperty(En ? "Prefix of chat messages" : "Префикс сообщений в чате")] public string Prefix { get; set; }
            [JsonProperty(En ? "Do you use global chat? [true/false]" : "Использовать ли чат? [true/false]")] public bool IsChat { get; set; }
            [JsonProperty(En ? "Facepunch Game Tips setting" : "Настройка сообщений Facepunch Game Tip")] public GameTipConfig GameTip { get; set; }
            [JsonProperty(En ? "GUI Announcements setting" : "Настройка GUI Announcements")] public GuiAnnouncementsConfig GuiAnnouncements { get; set; }
            [JsonProperty(En ? "Notify setting" : "Настройка Notify")] public NotifyConfig Notify { get; set; }
            [JsonProperty(En ? "Rewards settings (total values will be added up and rewarded at the end of the event)" : "Настройка экономики (конечное значение суммируется и будет выдано игрокам по окончанию ивента)")] public EconomyConfig Economy { get; set; }
            [JsonProperty(En ? "Can the non-owner of the event loot the locked crate? [true/false]" : "Может ли не владелец ивента грабить заблокированный ящик? [true/false]")] public bool CanLootCrateNonOwner { get; set; }
            [JsonProperty(En ? "Can the non-owner of the event deal damage to the NPC? [true/false]" : "Может ли не владелец ивента наносить урон по NPC? [true/false]")] public bool DamageNpc { get; set; }
            [JsonProperty(En ? "Can an Npc attack a non-owner of the event? [true/false]" : "Может ли Npc атаковать не владельца ивента? [true/false]")] public bool TargetNpc { get; set; }
            [JsonProperty(En ? "List of prefabs that you can build in the event area" : "Список prefab-ов, которые вы можете строить в зоне ивента")] public HashSet<string> Prefabs { get; set; }
            [JsonProperty(En ? "Allow OnItemSplit hook to work? [true/false]" : "Должен ли работать хук OnItemSplit? [true/false]")] public bool OnItemSplit { get; set; }
            [JsonProperty(En ? "Enable Adaptive Cleanup" : "Включить адаптивную очистку")] public bool EnableAdaptiveCleanup { get; set; }
            [JsonProperty(En ? "Base Batch Size" : "Базовый размер пакета")] public int BaseBatchSize { get; set; }
            [JsonProperty(En ? "Base Batch Delay (seconds)" : "Базовая задержка пакета (секунды)")] public float BaseBatchDelay { get; set; }
            [JsonProperty(En ? "FPS Threshold (below this, use batch size 1)" : "Порог FPS (ниже этого, использовать размер пакета 1)")] public float AdaptiveCleanupFpsThreshold { get; set; }
            [JsonProperty(En ? "Configuration version" : "Версия конфигурации")] public VersionNumber PluginVersion { get; set; }

            public static PluginConfig DefaultConfig()
            {
                return new PluginConfig
                {
                    Sledge = new SledgeConfig
                    {
                        WearItems = new HashSet<NpcWear>
                        {
                            new NpcWear { ShortName = "frankensteins.monster.01.head", SkinId = 0 },
                            new NpcWear { ShortName = "frankensteins.monster.01.torso", SkinId = 0 },
                            new NpcWear { ShortName = "frankensteins.monster.01.legs", SkinId = 0 }
                        },
                        Weapons = new HashSet<SledgeBelt>
                        {
                            new SledgeBelt { ShortName = "paddle", SkinId = 0, Chance = 100f },
                            new SledgeBelt { ShortName = "spear.stone", SkinId = 0, Chance = 100f },
                            new SledgeBelt { ShortName = "bone.club", SkinId = 0, Chance = 100f },
                            new SledgeBelt { ShortName = "knife.combat", SkinId = 0, Chance = 100f },
                            new SledgeBelt { ShortName = "longsword", SkinId = 0, Chance = 100f },
                            new SledgeBelt { ShortName = "pitchfork", SkinId = 0, Chance = 100f },
                            new SledgeBelt { ShortName = "knife.bone", SkinId = 0, Chance = 100f },
                            new SledgeBelt { ShortName = "mace", SkinId = 0, Chance = 100f },
                            new SledgeBelt { ShortName = "salvaged.cleaver", SkinId = 0, Chance = 100f },
                            new SledgeBelt { ShortName = "knife.butcher", SkinId = 0, Chance = 100f },
                            new SledgeBelt { ShortName = "machete", SkinId = 0, Chance = 100f },
                            new SledgeBelt { ShortName = "salvaged.sword", SkinId = 0, Chance = 100f },
                            new SledgeBelt { ShortName = "spear.wooden", SkinId = 0, Chance = 100f },
                            new SledgeBelt { ShortName = "stone.pickaxe", SkinId = 0, Chance = 100f },
                            new SledgeBelt { ShortName = "torch", SkinId = 0, Chance = 100f },
                            new SledgeBelt { ShortName = "axe.salvaged", SkinId = 0, Chance = 100f },
                            new SledgeBelt { ShortName = "pickaxe", SkinId = 0, Chance = 100f },
                            new SledgeBelt { ShortName = "hammer.salvaged", SkinId = 0, Chance = 100f },
                            new SledgeBelt { ShortName = "hatchet", SkinId = 0, Chance = 100f },
                            new SledgeBelt { ShortName = "stonehatchet", SkinId = 0, Chance = 100f },
                            new SledgeBelt { ShortName = "icepick.salvaged", SkinId = 0, Chance = 100f },
                            new SledgeBelt { ShortName = "sickle", SkinId = 0, Chance = 100f },
                            new SledgeBelt { ShortName = "mace.baseballbat", SkinId = 0, Chance = 100f }
                        },
                        BeancanGrenade = 7.5f,
                        Health = 50f,
                        DamageScale = 0.4f,
                        TurretDamageScale = 0.25f,
                        Speed = 8f
                    },
                    Juggernaut = new JuggernautConfig
                    {
                        WearItems = new HashSet<NpcWear>
                        {
                            new NpcWear { ShortName = "frankensteins.monster.03.head", SkinId = 0 },
                            new NpcWear { ShortName = "frankensteins.monster.03.torso", SkinId = 0 },
                            new NpcWear { ShortName = "frankensteins.monster.03.legs", SkinId = 0 }
                        },
                        Weapons = new HashSet<JuggernautBelt>
                        {
                            new JuggernautBelt { ShortName = "hmlmg", SkinId = 0, Ammo = string.Empty, Mods = new HashSet<string> { "weapon.mod.holosight", "weapon.mod.flashlight" }, Chance = 100f, DamageScale = 0.2f }
                        },
                        BeancanGrenade = 0f,
                        Health = 300f,
                        TurretDamageScale = 0.25f,
                        AimConeScale = 1.1f,
                        Speed = 5f,
                        AttackRangeMultiplier = 1f
                    },
                    Rocketman = new RocketmanConfig
                    {
                        WearItems = new HashSet<NpcWear>
                        {
                            new NpcWear { ShortName = "hazmatsuit.lumberjack", SkinId = 0 }
                        },
                        Weapons = new HashSet<RocketmanBelt>
                        {
                            new RocketmanBelt { ShortName = "rifle.lr300", SkinId = 0, Mods = new HashSet<string> { "weapon.mod.holosight", "weapon.mod.flashlight" }, Ammo = string.Empty, Chance = 100f }
                        },
                        RocketAmmoShortName = "rocket_basic",
                        GrenadeF1 = 15f,
                        Health = 175f,
                        DamageScale = 0.4f,
                        TurretDamageScale = 0.25f,
                        AimConeScale = 1.5f,
                        Speed = 7.5f,
                        AttackRangeMultiplier = 0.5f
                    },
                    Bomber = new BomberConfig
                    {
                        WearItems = new HashSet<NpcWear>
                        {
                            new NpcWear { ShortName = "scarecrow.suit", SkinId = 0 },
                            new NpcWear { ShortName = "metal.plate.torso", SkinId = 860210174 },
                        },
                        Health = 100f,
                        AttackRangeMultiplier = 1f,
                        Speed = 5.5f,
                        DamageFoundation = 150f,
                        DamageConstruction = 150f,
                        DamagePlayer = 60f
                    },
                    InfiniteAmmo = true,
                    WeaponToScaleDamageNpc = new Dictionary<string, float>
                    {
                        ["grenade.beancan.deployed"] = 0.5f,
                        ["grenade.f1.deployed"] = 0.5f,
                        ["explosive.satchel.deployed"] = 0.5f,
                        ["explosive.timed.deployed"] = 0.5f,
                        ["rocket_hv"] = 0.5f,
                        ["rocket_basic"] = 0.5f,
                        ["40mm_grenade_he"] = 0.5f
                    },
                    DifficultyLevels = new HashSet<DifficultyLevelConfig>
                    {
                        new DifficultyLevelConfig
                        {
                            Name = "EASY",
                            MaxTurrets = 3,
                            MinFoundations = 4,
                            MaxFoundations = 9,
                            Cooldown = 21600,
                            MaxCount = 5,
                            Waves = new HashSet<WaveConfig>
                            {
                                new WaveConfig
                                {
                                    Level = 1,
                                    TimeToStart = 30,
                                    Duration = 90,
                                    TimerNpc = 15,
                                    Presets = new HashSet<PresetConfig>
                                    {
                                        new PresetConfig
                                        {
                                            ShortName = "Sledge",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 6 } }
                                        }
                                    }
                                },
                                new WaveConfig
                                {
                                    Level = 2,
                                    TimeToStart = 30,
                                    Duration = 120,
                                    TimerNpc = 15,
                                    Presets = new HashSet<PresetConfig>
                                    {
                                        new PresetConfig
                                        {
                                            ShortName = "Sledge",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 6 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Bomber",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 1 } }
                                        }
                                    }
                                },
                                new WaveConfig
                                {
                                    Level = 3,
                                    TimeToStart = 30,
                                    Duration = 120,
                                    TimerNpc = 15,
                                    Presets = new HashSet<PresetConfig>
                                    {
                                        new PresetConfig
                                        {
                                            ShortName = "Sledge",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 6 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Bomber",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 1 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Juggernaut",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 75f, Count = 1 } }
                                        }
                                    }
                                }
                            },
                            HackCrate = new HackCrateConfig
                            {
                                UnlockTime = 10f,
                                TypeLootTable = 5,
                                PrefabLootTable = new PrefabLootTableConfig
                                {
                                    Min = 3, Max = 3, UseCount = true,
                                    Prefabs = new List<PrefabConfig>
                                    {
                                        new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab" },
                                        new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab" },
                                        new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_normal.prefab" }
                                    }
                                },
                                OwnLootTable = new LootTableConfig
                                {
                                    Min = 1, Max = 1, UseCount = false,
                                    Items = new List<ItemConfig>
                                    {
                                        new ItemConfig { ShortName = "scrap", MinAmount = 500, MaxAmount = 1000, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = string.Empty },
                                        new ItemConfig { ShortName = "ammo.rifle", MinAmount = 500, MaxAmount = 750, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = string.Empty },
                                        new ItemConfig { ShortName = "ammo.pistol", MinAmount = 500, MaxAmount = 750, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = string.Empty },
                                        new ItemConfig { ShortName = "syringe.medical", MinAmount = 30, MaxAmount = 60, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = string.Empty },
                                        new ItemConfig { ShortName = "largemedkit", MinAmount = 5, MaxAmount = 10, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = string.Empty },
                                        new ItemConfig { ShortName = "grenade.f1", MinAmount = 5, MaxAmount = 10, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = string.Empty },
                                        new ItemConfig { ShortName = "flare", MinAmount = 1, MaxAmount = 1, Chance = 100f, IsBluePrint = false, SkinId = 2888602942, Name = "Flare for MEDIUM" }
                                    }
                                }
                            }
                        },
                        new DifficultyLevelConfig
                        {
                            Name = "MEDIUM",
                            MaxTurrets = 6,
                            MinFoundations = 9,
                            MaxFoundations = 16,
                            Cooldown = 21600,
                            MaxCount = 3,
                            Waves = new HashSet<WaveConfig>
                            {
                                new WaveConfig
                                {
                                    Level = 1,
                                    TimeToStart = 30,
                                    Duration = 90,
                                    TimerNpc = 10,
                                    Presets = new HashSet<PresetConfig>
                                    {
                                        new PresetConfig
                                        {
                                            ShortName = "Sledge",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 8 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Bomber",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 1 } }
                                        }
                                    }
                                },
                                new WaveConfig
                                {
                                    Level = 2,
                                    TimeToStart = 30,
                                    Duration = 120,
                                    TimerNpc = 10,
                                    Presets = new HashSet<PresetConfig>
                                    {
                                        new PresetConfig
                                        {
                                            ShortName = "Sledge",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 8 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Bomber",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 1 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Juggernaut",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 50f, Count = 1 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Rocketman",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 2 } }
                                        }
                                    }
                                },
                                new WaveConfig
                                {
                                    Level = 3,
                                    TimeToStart = 30,
                                    Duration = 120,
                                    TimerNpc = 15,
                                    Presets = new HashSet<PresetConfig>
                                    {
                                        new PresetConfig
                                        {
                                            ShortName = "Sledge",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 8 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Bomber",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 1 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Juggernaut",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 1 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Rocketman",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 6 } }
                                        }
                                    }
                                },
                                new WaveConfig
                                {
                                    Level = 4,
                                    TimeToStart = 30,
                                    Duration = 150,
                                    TimerNpc = 15,
                                    Presets = new HashSet<PresetConfig>
                                    {
                                        new PresetConfig
                                        {
                                            ShortName = "Sledge",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 6 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Bomber",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 2 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Juggernaut",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 1 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Juggernaut",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 50f, Count = 1 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Rocketman",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 4 } }
                                        }
                                    }
                                }
                            },
                            HackCrate = new HackCrateConfig
                            {
                                UnlockTime = 10f,
                                TypeLootTable = 5,
                                PrefabLootTable = new PrefabLootTableConfig
                                {
                                    Min = 5, Max = 5, UseCount = true,
                                    Prefabs = new List<PrefabConfig>
                                    {
                                        new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab" },
                                        new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/prefabs/npc/patrol helicopter/heli_crate.prefab" },
                                        new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_elite.prefab" },
                                        new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab" },
                                        new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_normal.prefab" }
                                    }
                                },
                                OwnLootTable = new LootTableConfig
                                {
                                    Min = 1, Max = 1, UseCount = false,
                                    Items = new List<ItemConfig>
                                    {
                                        new ItemConfig { ShortName = "scrap", MinAmount = 750, MaxAmount = 1500, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = string.Empty },
                                        new ItemConfig { ShortName = "ammo.rifle", MinAmount = 1000, MaxAmount = 2000, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = string.Empty },
                                        new ItemConfig { ShortName = "syringe.medical", MinAmount = 50, MaxAmount = 100, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = string.Empty },
                                        new ItemConfig { ShortName = "largemedkit", MinAmount = 7, MaxAmount = 15, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = string.Empty },
                                        new ItemConfig { ShortName = "grenade.f1", MinAmount = 7, MaxAmount = 15, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = string.Empty },
                                        new ItemConfig { ShortName = "flare", MinAmount = 1, MaxAmount = 1, Chance = 100f, IsBluePrint = false, SkinId = 2888602942, Name = "Flare for MEDIUM" },
                                        new ItemConfig { ShortName = "flare", MinAmount = 1, MaxAmount = 1, Chance = 100f, IsBluePrint = false, SkinId = 2888603247, Name = "Flare for HARD" }
                                    }
                                }
                            }
                        },
                        new DifficultyLevelConfig
                        {
                            Name = "HARD",
                            MaxTurrets = 9,
                            MinFoundations = 16,
                            MaxFoundations = 25,
                            Cooldown = 21600,
                            MaxCount = 2,
                            Waves = new HashSet<WaveConfig>
                            {
                                new WaveConfig
                                {
                                    Level = 1,
                                    TimeToStart = 30,
                                    Duration = 110,
                                    TimerNpc = 10,
                                    Presets = new HashSet<PresetConfig>
                                    {
                                        new PresetConfig
                                        {
                                            ShortName = "Sledge",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 10 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Bomber",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 1 } }
                                        }
                                    }
                                },
                                new WaveConfig
                                {
                                    Level = 2,
                                    TimeToStart = 30,
                                    Duration = 140,
                                    TimerNpc = 10,
                                    Presets = new HashSet<PresetConfig>
                                    {
                                        new PresetConfig
                                        {
                                            ShortName = "Sledge",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 8 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Juggernaut",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 50f, Count = 1 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Rocketman",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 4 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Bomber",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 1 } }
                                        }
                                    }
                                },
                                new WaveConfig
                                {
                                    Level = 3,
                                    TimeToStart = 30,
                                    Duration = 150,
                                    TimerNpc = 10,
                                    Presets = new HashSet<PresetConfig>
                                    {
                                        new PresetConfig
                                        {
                                            ShortName = "Sledge",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 8 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Juggernaut",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 1 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Rocketman",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 6 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Bomber",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 2 } }
                                        }
                                    }
                                },
                                new WaveConfig
                                {
                                    Level = 4,
                                    TimeToStart = 30,
                                    Duration = 180,
                                    TimerNpc = 15,
                                    Presets = new HashSet<PresetConfig>
                                    {
                                        new PresetConfig
                                        {
                                            ShortName = "Sledge",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 10 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Juggernaut",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 1 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Rocketman",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 9 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Bomber",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 2 } }
                                        }
                                    }
                                },
                                new WaveConfig
                                {
                                    Level = 5,
                                    TimeToStart = 30,
                                    Duration = 180,
                                    TimerNpc = 15,
                                    Presets = new HashSet<PresetConfig>
                                    {
                                        new PresetConfig
                                        {
                                            ShortName = "Sledge",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 10 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Juggernaut",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 1 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Juggernaut",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 50f, Count = 1 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Rocketman",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 7 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Rocketman",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 50f, Count = 3 } }
                                        },
                                        new PresetConfig
                                        {
                                            ShortName = "Bomber",
                                            Config = new HashSet<AmountConfig> { new AmountConfig { Chance = 100f, Count = 3 } }
                                        }
                                    }
                                }
                            },
                            HackCrate = new HackCrateConfig
                            {
                                UnlockTime = 10f,
                                TypeLootTable = 5,
                                PrefabLootTable = new PrefabLootTableConfig
                                {
                                    Min = 21, Max = 21, UseCount = true,
                                    Prefabs = new List<PrefabConfig>
                                    {
                                        new PrefabConfig { Chance = 100.0f, PrefabDefinition = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab" },
                                        new PrefabConfig { Chance = 100.0f, PrefabDefinition = "assets/prefabs/npc/patrol helicopter/heli_crate.prefab" },
                                        new PrefabConfig { Chance = 100.0f, PrefabDefinition = "assets/prefabs/npc/m2bradley/bradley_crate.prefab" },
                                        new PrefabConfig { Chance = 100.0f, PrefabDefinition = "assets/prefabs/misc/supply drop/supply_drop.prefab" },
                                        new PrefabConfig { Chance = 100.0f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_elite.prefab" },
                                        new PrefabConfig { Chance = 100.0f, PrefabDefinition = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab" },
                                        new PrefabConfig { Chance = 100.0f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_normal.prefab" }
                                    }
                                },
                                OwnLootTable = new LootTableConfig
                                {
                                    Min = 1, Max = 1, UseCount = false,
                                    Items = new List<ItemConfig>
                                    {
                                        new ItemConfig { ShortName = "scrap", MinAmount = 1000, MaxAmount = 2000, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = string.Empty },
                                        new ItemConfig { ShortName = "ammo.rifle", MinAmount = 2000, MaxAmount = 4000, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = string.Empty },
                                        new ItemConfig { ShortName = "syringe.medical", MinAmount = 100, MaxAmount = 200, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = string.Empty },
                                        new ItemConfig { ShortName = "largemedkit", MinAmount = 10, MaxAmount = 20, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = string.Empty },
                                        new ItemConfig { ShortName = "grenade.f1", MinAmount = 10, MaxAmount = 20, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = string.Empty },
                                        new ItemConfig { ShortName = "flare", MinAmount = 1, MaxAmount = 1, Chance = 100f, IsBluePrint = false, SkinId = 2888603247, Name = "Flare for HARD" }
                                    }
                                }
                            }
                        },
                    },
                    Flares = new HashSet<FlareConfig>
                    {
                        new FlareConfig
                        {
                            Name = "Flare for EASY",
                            SkinId = 2888602635,
                            NameDifficultyLevel = "EASY",
                            Crates = new HashSet<CrateConfig>
                            {
                                new CrateConfig { Prefab = "assets/bundled/prefabs/radtown/crate_elite.prefab", Chance = 50f },
                                new CrateConfig { Prefab = "assets/prefabs/npc/m2bradley/bradley_crate.prefab", Chance = 50f },
                                new CrateConfig { Prefab = "assets/bundled/prefabs/radtown/crate_normal.prefab", Chance = 33.33333f },
                                new CrateConfig { Prefab = "assets/prefabs/misc/supply drop/supply_drop.prefab", Chance = 50f }
                            }
                        },
                        new FlareConfig
                        {
                            Name = "Flare for MEDIUM",
                            SkinId = 2888602942,
                            NameDifficultyLevel = "MEDIUM",
                            Crates = new HashSet<CrateConfig>()
                        },
                        new FlareConfig
                        {
                            Name = "Flare for HARD",
                            SkinId = 2888603247,
                            NameDifficultyLevel = "HARD",
                            Crates = new HashSet<CrateConfig>()
                        }
                    },
                    Gui = new GuiConfig
                    {
                        IsGui = true,
                        OffsetMinY = "-56"
                    },
                    Prefix = "[DefendableHomes]",
                    IsChat = true,
                    GameTip = new GameTipConfig
                    {
                        IsGameTip = false,
                        Style = 2
                    },
                    GuiAnnouncements = new GuiAnnouncementsConfig
                    {
                        IsGuiAnnouncements = false,
                        BannerColor = "Orange",
                        TextColor = "White",
                        ApiAdjustVPosition = 0.03f
                    },
                    Notify = new NotifyConfig
                    {
                        IsNotify = false,
                        Type = 0
                    },
                    Economy = new EconomyConfig
                    {
                        Plugins = new HashSet<string> { "Economics", "Server Rewards", "IQEconomic", "XPerience" },
                        Min = 0,
                        KillNpc = new Dictionary<string, double>
                        {
                            ["Sledge"] = 0.2,
                            ["Juggernaut"] = 0.8,
                            ["Rocketman"] = 0.8,
                            ["Bomber"] = 0.5
                        },
                        Commands = new HashSet<string>()
                    },
                    CanLootCrateNonOwner = false,
                    DamageNpc = true,
                    TargetNpc = true,
                    Prefabs = new HashSet<string>
                    {
                        "assets/prefabs/building/ladder.wall.wood/ladder.wooden.wall.prefab"
                    },
                    OnItemSplit = true,
                    EnableAdaptiveCleanup = true,
                    BaseBatchSize = 10,
                    BaseBatchDelay = 0.1f,
                    AdaptiveCleanupFpsThreshold = 15f,
                    PluginVersion = new VersionNumber()
                };
            }
        }
        #endregion Config

        #region Lang
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Start"] = "{0} You <color=#738d43>started</color> the base defense event with <color=#55aaff>{1}</color> difficulty",
                ["Enter"] = "{0} You <color=#ce3f27>have entered</color> the base defense event zone with <color=#55aaff>{1}</color> difficulty",
                ["Exit"] = "{0} You <color=#738d43>have left</color> the base defense event zone with <color=#55aaff>{1}</color> difficulty",
                ["PreparationTime"] = "{0} Preparation for <color=#55aaff>{1}</color> enemy attack wave <color=#738d43>has begun</color>!",
                ["AttackWave"] = "{0} The enemy <color=#ce3f27>is attacking</color>. <color=#55aaff>Don't let</color> him <color=#55aaff>destroy</color> all the <color=#55aaff>foundations</color> of your base!",
                ["NoBuild"] = "{0} You <color=#ce3f27>can't</color> build anything in the base defense event area! You can build again after the event has ended",
                ["NoLootCrate"] = "You <color=#ce3f27>can't</color> loot this crate because you are not the owner of the event and are not on a team with the owner of the event!",
                ["Finish"] = "{0} All enemy attack waves <color=#738d43>are contained</color>!\nIn the near future, a <color=#55aaff>CH47</color> will arrive to <color=#55aaff>help</color> you",
                ["Defeat"] = "{0} All the <color=#55aaff>foundations</color> of your base <color=#ce3f27>are destroyed</color>. You <color=#ce3f27>have failed</color> the base defense event",
                ["NoUseFlareMax"] = "{0} You <color=#ce3f27>can't</color> start an event now due to the <color=#ce3f27>server limit</color> on the number of simultaneously launched events of this difficulty level (server limit is <color=#55aaff>{1}</color>). Try to start again later or try launching another difficulty",
                ["NoUseFlareBuilding"] = "{0} You <color=#ce3f27>can't</color> start the event at the current location because the cupboard doesn't exist at the current location or it's not your base!",
                ["NoUseFlareEvent"] = "{0} You <color=#ce3f27>can't</color> start an event because you already have an event running for this cupboard!",
                ["NoUseFlareOwner"] = "{0} You already have an event running, you <color=#ce3f27>can not</color> start another event!",
                ["NoUseFlareOwnerFoundation"] = "{0} You <color=#ce3f27>can't</color> start the event here because one or more of the bases foundation do not belong to you or your friends (foundations must all be owned by a member of the current team)",
                ["NoUseFlareHeightFoundation"] = "{0} You <color=#ce3f27>can't</color> start the event here because one or more of the bases foundation are too high or deep (some prefabs are not measured, the ground below is measured instead)",
                ["NoUseFlareTopologyFoundation"] = "{0} You <color=#ce3f27>can't</color> start the event here because one or more of the bases foundation are on an unusable map topology (if topology appears compatible, check map file)",
                ["NoUseFlareMarkersFoundation"] = "{0} You <color=#ce3f27>can't</color> start the event here because one or more foundations of the base are located near a map marker (flares unusable near map markers)",
                ["NoUseFlareMaxTurrets"] = "{0} You <color=#ce3f27>can't</color> start the event here because the maximum number of Auto Turrets ({1} Auto Turrets max per base) has been exceeded. Reduce to the allowable amount and try again",
                ["NoUseFlareMinFoundations"] = "{0} You <color=#ce3f27>can not</color> start the event here because you have too few foundations. The limit is {1} foundations per base minimum, increase to this number or higher",
                ["NoUseFlareMaxFoundations"] = "{0} You <color=#ce3f27>can not</color> start the event here because you have too many foundations. The limit is {1} foundations per base maximum, reduce to this number or lower",
                ["NoUseFlareNotSpawnNpc"] = "{0} You <color=#ce3f27>can not</color> start the event here because there is no place for NPCs to appear in the event zone, clear out some room!",
                ["NoUseFlareCooldown"] = "{0} You <color=#ce3f27>can not</color> start a <color=#55aaff>{1}</color> event until your cooldown expires in <color=#55aaff>{2}</color>",
                ["FinishAdmin"] = "{0} You <color=#ce3f27>have disabled</color> the event in your position",
                ["NoDamageScientistEvent"] = "{0} You <color=#ce3f27>cannot</color> damage NPC! You are not the Event Owner and you are not on their team!",
                ["SendEconomy"] = "{0} You <color=#738d43>have earned</color> <color=#55aaff>{1}</color> points in economics for participating in the event"
            }, this);

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Start"] = "{0} Вы <color=#738d43>начали</color> ивент защиты дома со сложностью <color=#55aaff>{1}</color>",
                ["Enter"] = "{0} Вы <color=#ce3f27>вошли</color> в зону ивента защиты дома со сложностью <color=#55aaff>{1}</color>",
                ["Exit"] = "{0} Вы <color=#738d43>вышли</color> из зоны ивента защиты дома со сложностью <color=#55aaff>{1}</color>",
                ["PreparationTime"] = "{0} <color=#738d43>Началась</color> подготовка к <color=#55aaff>{1}</color> волне атаки противника!",
                ["AttackWave"] = "{0} Противник <color=#ce3f27>атакует</color>. <color=#55aaff>Не дай</color> ему <color=#55aaff>уничтожить</color> все <color=#55aaff>фундаменты</color> вашего дома!",
                ["NoBuild"] = "{0} Вы <color=#ce3f27>не можете</color> ничего строить в зоне ивента защиты дома! Пожалуйста, подождите окончания ивента",
                ["NoLootCrate"] = "{0} Вы <color=#ce3f27>не можете</color> ограбить этот ящик, потому что не являетесь владельцем ивента и не состоите в команде с владельцем ивента!",
                ["Finish"] = "{0} Все волны атаки противника <color=#738d43>сдержаны</color>!\nВ ближайшее время к вам прилетит <color=#55aaff>транспортный вертолет</color>, чтобы оказать вам <color=#55aaff>помощь</color>",
                ["Defeat"] = "{0} Все <color=#55aaff>фундаменты</color> вашего дома <color=#ce3f27>разрушены</color>. Вы <color=#ce3f27>проиграли</color> ивент защиты дома",
                ["NoUseFlareMax"] = "{0} Вы <color=#ce3f27>не можете</color> начать ивент сейчас, потому что на сервере <color=#ce3f27>установлен лимит</color> на кол-во одновременно запущенных ивентов этого уровня сложности (<color=#55aaff>{1}</color> шт.). Попробуйте запустить ивент позже, когда другой игрок закончит ивент",
                ["NoUseFlareBuilding"] = "{0} Вы <color=#ce3f27>не можете</color> начать ивент в текущем месте, потому что шкафа в текущем месте не существует или это не ваш дом!",
                ["NoUseFlareEvent"] = "{0} Вы <color=#ce3f27>не можете</color> начать ивент, потому что у вас уже запущен ивент для этого шкафа!",
                ["NoUseFlareOwner"] = "{0} Вы <color=#ce3f27>не можете</color> начать ивент, потому что у вас уже запущен ивент!",
                ["NoUseFlareOwnerFoundation"] = "{0} Вы <color=#ce3f27>не можете</color> начать ивент в текущем месте, потому что один или более фундаментов этого дома не принадлежат вам или вашим друзьям!",
                ["NoUseFlareHeightFoundation"] = "{0} Вы <color=#ce3f27>не можете</color> начать ивент в текущем месте, потому что один или более фундаментов этого дома находятся очень высоко или очень глубоко!",
                ["NoUseFlareTopologyFoundation"] = "{0} Вы <color=#ce3f27>не можете</color> начать ивент в текущем месте, потому что один или более фундаментов этого дома находятся на запрещенной топологии!",
                ["NoUseFlareMarkersFoundation"] = "{0} Вы <color=#ce3f27>не можете</color> начать ивент в текущем месте, потому что один или более фундаментов этого дома находятся вблизи маркера на карте!",
                ["NoUseFlareMaxTurrets"] = "{0} Вы <color=#ce3f27>не можете</color> начать ивент в текущем месте, потому что кол-во турелей этого дома превышает максимально допустимое значение ({1} шт.)!",
                ["NoUseFlareMinFoundations"] = "{0} Вы <color=#ce3f27>не можете</color> начать ивент в текущем месте, потому что кол-во фундаментов этого дома меньше допустимого значения ({1} шт.)!",
                ["NoUseFlareMaxFoundations"] = "{0} Вы <color=#ce3f27>не можете</color> начать ивент в текущем месте, потому что кол-во фундаментов этого дома превышает максимально допустимое значение ({1} шт.)!",
                ["NoUseFlareNotSpawnNpc"] = "{0} Вы <color=#ce3f27>не можете</color> начать ивент в текущем месте, потому что в зоне ивента нет места для появления NPC!",
                ["NoUseFlareCooldown"] = "{0} Вы <color=#ce3f27>не можете</color> начать ивент, следующий запуск уровня <color=#55aaff>{1}</color> будет доступен для вас через <color=#55aaff>{2}</color>",
                ["FinishAdmin"] = "{0} Вы <color=#ce3f27>отключили</color> ивент в вашей позиции",
                ["NoDamageScientistEvent"] = "{0} Вы <color=#ce3f27>не можете</color> нанести урон этому NPC, потому что не являетесь владельцем ивента и не состоите в команде с владельцем ивента!",
                ["SendEconomy"] = "{0} Вы <color=#738d43>получили</color> <color=#55aaff>{1}</color> баллов в экономику за прохождение ивента"
            }, this, "ru");
        }

        private string GetMessage(string langKey, string userId) => lang.GetMessage(langKey, _ins, userId);

        private string GetMessage(string langKey, string userId, params object[] args) => (args.Length == 0) ? GetMessage(langKey, userId) : string.Format(GetMessage(langKey, userId), args);
        #endregion Lang

        #region Data
        internal class PlayerData { public ulong SteamId; public Dictionary<string, double> LastTime; }

        private HashSet<PlayerData> PlayersData { get; set; } = null;

        private void LoadData() => PlayersData = Interface.Oxide.DataFileSystem.ReadObject<HashSet<PlayerData>>(Name);

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, PlayersData);

        private static readonly DateTime Epoch = new DateTime(2024, 1, 1, 0, 0, 0);

        private static double CurrentTime => DateTime.UtcNow.Subtract(Epoch).TotalSeconds;

        private bool CanTimeStart(DifficultyLevelConfig difficultyLevel, ulong steamId)
        {
            PlayerData playerData = PlayersData.FirstOrDefault(x => x.SteamId == steamId);
            if (playerData == null) return true;
            if (!playerData.LastTime.ContainsKey(difficultyLevel.Name)) return true;
            return playerData.LastTime[difficultyLevel.Name] + difficultyLevel.Cooldown < CurrentTime;
        }
        #endregion Data

        #region Oxide Hooks
        private static DefendableHomes _ins;

        private void Init()
        {
            _ins = this;
            ToggleHooks(false);
            _specialWeaponsHandler = new SpecialWeaponsHandler(this);
        }

        private void OnServerInitialized()
        {
            // Check if GrimmNPC Harmony mod is loaded
            if (!CheckGrimmNpcAvailable())
            {
                PrintError("GrimmNPC Harmony mod is not loaded! Please ensure GrimmNPC.dll is loaded via harmony.load");
                NextTick(() => Interface.Oxide.UnloadPlugin(Name));
                return;
            }
            LoadData();
            CheckAllLootTables();
            foreach (MonumentInfo monument in TerrainMeta.Path.Monuments) if (monument.name == "assets/bundled/prefabs/modding/volumes_and_triggers/monument_marker.prefab") Markers.Add(monument.transform.position);
            ServerMgr.Instance.StartCoroutine(DownloadImages());
        }

        private void Unload()
        {
            foreach (ControllerHomeRaid controller in Controllers) UnityEngine.Object.Destroy(controller.gameObject);
            _specialWeaponsHandler?.StopAll();
            _ins = null;
        }

        private object OnEntityTakeDamage(ScientistNPC entity, HitInfo info)
        {
            if (entity == null || info == null) return null;

            ControllerHomeRaid controller = Controllers.FirstOrDefault(x => x.Scientists.Contains(entity));
            if (controller == null) return null;

            BasePlayer attacker = info.InitiatorPlayer;
            BaseEntity weaponPrefab = info.WeaponPrefab;

            if (attacker.IsPlayer() && (weaponPrefab == null || weaponPrefab.ShortPrefabName == "grenade.molotov.deployed" || weaponPrefab.ShortPrefabName == "rocket_fire") && info.damageTypes.GetMajorityDamageType() == DamageType.Heat) return true;

            if (weaponPrefab != null && _config.WeaponToScaleDamageNpc.TryGetValue(weaponPrefab.ShortPrefabName, out float scale)) info.damageTypes.ScaleAll(scale);

            if (!_config.DamageNpc && attacker.IsPlayer() && !IsTeam(attacker.userID, controller.Owner.userID))
            {
                AlertToPlayer(attacker, GetMessage("NoDamageScientistEvent", attacker.UserIDString, _config.Prefix));
                return true;
            }
            
            return null;
        }

        private object CanBuild(Planner planner, Construction prefab, Construction.Target target)
        {
            if (planner == null || prefab == null) return null;

            BasePlayer player = planner.GetOwnerPlayer();
            if (!player.IsPlayer()) return null;

            if (Controllers.Any(x => !x.AllWavesEnd && x.Players.Contains(player)) && !_config.Prefabs.Contains(prefab.fullName))
            {
                AlertToPlayer(player, GetMessage("NoBuild", player.UserIDString, _config.Prefix));
                return true;
            }

            return null;
        }

        private object OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (!player.IsPlayer()) return null;
            ControllerHomeRaid controller = Controllers.FirstOrDefault(x => x.Players.Contains(player));
            if (controller != null) controller.ExitPlayer(player);
            return null;
        }

        private void OnEntityDeath(ScientistNPC npc, HitInfo info)
        {
            if (npc == null || info == null) return;
            BasePlayer attacker = info.InitiatorPlayer;
            if (!attacker.IsPlayer()) return;
            ControllerHomeRaid controller = Controllers.FirstOrDefault(x => x.Scientists.Contains(npc));
            if (controller == null) return;
            ActionEconomy(controller, attacker.userID, "KillNpc", npc.displayName);
            
            // Clean up raid targets and guard targets
            ClearNpcRaidData(npc);
            
            // Unregister from GrimmNPC
            UnregisterNpcFromGrimmNpc(npc);
        }

        private void OnEntityKill(BuildingBlock entity)
        {
            if (entity == null || !IsFoundation(entity)) return;

            ControllerHomeRaid controller = Controllers.FirstOrDefault(x => x.Foundations.Contains(entity));
            if (controller == null) return;

            controller.Foundations.Remove(entity);
            if (controller.Foundations.Count == 0)
            {
                foreach (BasePlayer player in controller.Players) AlertToPlayer(player, GetMessage("Defeat", player.UserIDString, _config.Prefix));
                Finish(controller);
            }
        }

        private object CanLootEntity(BasePlayer player, HackableLockedCrate container)
        {
            if (_config.CanLootCrateNonOwner || !player.IsPlayer() || container == null) return null;

            ControllerHomeRaid controller = Controllers.FirstOrDefault(x => x.Crate.IsExists() && x.Crate == container);
            if (controller == null) return null;

            if (IsTeam(controller.Owner.userID, player.userID)) return null;

            AlertToPlayer(player, GetMessage("NoLootCrate", player.UserIDString, _config.Prefix));
            return true;
        }

        private object CanStackItem(Item item, Item targetItem)
        {
            if (item == null || targetItem == null) return null;
            if (item.info.itemid == targetItem.info.itemid && item.skin != targetItem.skin) return false;
            return null;
        }

        private object CanCombineDroppedItem(DroppedItem drItem, DroppedItem anotherDrItem)
        {
            if (drItem == null || anotherDrItem == null) return null;
            if (drItem.item.info.itemid == anotherDrItem.item.info.itemid && drItem.item.skin != anotherDrItem.item.skin) return true;
            return null;
        }

        private Item OnItemSplit(Item item, int amount)
        {
            if (!_config.OnItemSplit || !_config.Flares.Any(x => x.SkinId == item.skin)) return null;
            item.amount -= amount;
            Item newItem = ItemManager.CreateByItemID(item.info.itemid, amount, item.skin);
            newItem.name = item.name;
            item.MarkDirty();
            return newItem;
        }

        private void OnLootSpawn(LootContainer container)
        {
            if (container == null || container.inventory == null) return;

            FlareConfig config = _config.Flares.FirstOrDefault(x => x.Crates.Any(y => y.Prefab == container.PrefabName));
            if (config == null) return;

            if (UnityEngine.Random.Range(0f, 100f) <= config.Crates.FirstOrDefault(x => x.Prefab == container.PrefabName).Chance)
            {
                if (container.inventory.itemList.Count == container.inventory.capacity) container.inventory.capacity++;
                Item flare = GetFlare(config);
                if (!flare.MoveToContainer(container.inventory)) flare.Remove();
            }
        }

        private void OnExplosiveThrown(BasePlayer player, BaseEntity entity, ThrownWeapon weapon)
        {
            if (!player.IsPlayer() || entity == null || weapon == null || entity.ShortPrefabName != "flare.deployed") return;

            Item item = weapon.GetItem();

            FlareConfig flareConfig = _config.Flares.FirstOrDefault(x => x.SkinId == item.skin);
            if (flareConfig == null) return;

            DifficultyLevelConfig difficultyLevel = _config.DifficultyLevels.FirstOrDefault(x => x.Name == flareConfig.NameDifficultyLevel);
            if (difficultyLevel == null) return;

            int current = 0;
            foreach (ControllerHomeRaid controllerHomeRaid in Controllers) if (controllerHomeRaid.DifficultyLevel.Name == difficultyLevel.Name) current++;
            if (current >= difficultyLevel.MaxCount)
            {
                ReturnFlare(player, entity, flareConfig);
                AlertToPlayer(player, GetMessage("NoUseFlareMax", player.UserIDString, _config.Prefix, difficultyLevel.MaxCount));
                return;
            }

            BuildingPrivlidge cupboard = player.GetBuildingPrivilege();

            if (cupboard == null || !cupboard.authorizedPlayers.Any(x => IsTeam(player.userID, x)))
            {
                ReturnFlare(player, entity, flareConfig);
                AlertToPlayer(player, GetMessage("NoUseFlareBuilding", player.UserIDString, _config.Prefix));
                return;
            }

            if (Controllers.Any(x => x.Cupboards.Contains(cupboard)))
            {
                ReturnFlare(player, entity, flareConfig);
                AlertToPlayer(player, GetMessage("NoUseFlareEvent", player.UserIDString, _config.Prefix));
                return;
            }

            if (Controllers.Any(x => x.Owner == player))
            {
                ReturnFlare(player, entity, flareConfig);
                AlertToPlayer(player, GetMessage("NoUseFlareOwner", player.UserIDString, _config.Prefix));
                return;
            }

            if (!CanTimeStart(difficultyLevel, player.userID))
            {
                ReturnFlare(player, entity, flareConfig);
                AlertToPlayer(player, GetMessage("NoUseFlareCooldown", player.UserIDString, _config.Prefix, difficultyLevel.Name, GetTimeFormat((int)(PlayersData.FirstOrDefault(x => x.SteamId == player.userID).LastTime[difficultyLevel.Name] + difficultyLevel.Cooldown - CurrentTime))));
                return;
            }

            HashSet<BuildingPrivlidge> cupboards; HashSet<BuildingBlock> foundations; Vector3 center3; float radius; float maxRadius;
            FindAllBuildings(cupboard, out cupboards, out foundations, out center3, out radius, out maxRadius);

            if (foundations.Count < difficultyLevel.MinFoundations)
            {
                ReturnFlare(player, entity, flareConfig);
                AlertToPlayer(player, GetMessage("NoUseFlareMinFoundations", player.UserIDString, _config.Prefix, difficultyLevel.MinFoundations));
                return;
            }

            if (foundations.Count > difficultyLevel.MaxFoundations)
            {
                ReturnFlare(player, entity, flareConfig);
                AlertToPlayer(player, GetMessage("NoUseFlareMaxFoundations", player.UserIDString, _config.Prefix, difficultyLevel.MaxFoundations));
                return;
            }

            if (foundations.Any(x => !IsTeam(player.userID, x.OwnerID)))
            {
                ReturnFlare(player, entity, flareConfig);
                AlertToPlayer(player, GetMessage("NoUseFlareOwnerFoundation", player.UserIDString, _config.Prefix));
                return;
            }

            if (foundations.Any(x => !IsValidHeightFoundation(x.transform.position)))
            {
                ReturnFlare(player, entity, flareConfig);
                AlertToPlayer(player, GetMessage("NoUseFlareHeightFoundation", player.UserIDString, _config.Prefix));
                return;
            }

            if (foundations.Any(x => !IsValidTopologyFoundation(x.transform.position)))
            {
                ReturnFlare(player, entity, flareConfig);
                AlertToPlayer(player, GetMessage("NoUseFlareTopologyFoundation", player.UserIDString, _config.Prefix));
                return;
            }

            if (foundations.Any(x => !IsValidMarkersFoundation(x.transform.position)))
            {
                ReturnFlare(player, entity, flareConfig);
                AlertToPlayer(player, GetMessage("NoUseFlareMarkersFoundation", player.UserIDString, _config.Prefix));
                return;
            }

            if (cupboards.SelectMany(x => x.GetBuilding().decayEntities.OfType<AutoTurret>()).Count > difficultyLevel.MaxTurrets)
            {
                ReturnFlare(player, entity, flareConfig);
                AlertToPlayer(player, GetMessage("NoUseFlareMaxTurrets", player.UserIDString, _config.Prefix, difficultyLevel.MaxTurrets));
                return;
            }

            int maxCountNpc = 0;
            foreach (WaveConfig wave in difficultyLevel.Waves)
            {
                int count = 0;
                foreach (PresetConfig preset in wave.Presets)
                {
                    AmountConfig amountConfig = preset.Config.Max(x => x.Count);
                    count += amountConfig.Count;
                }
                if (maxCountNpc < count) maxCountNpc = count;
            }

            HashSet<Vector3> positions = new HashSet<Vector3>();
            for (int i = 0; i < maxCountNpc * 2; i++)
            {
                Vector3 pos = GetRandomPos(center3, radius, maxRadius);
                if (pos == Vector3.zero || positions.Any(x => Vector3.Distance(x, pos) < 1f)) continue;
                positions.Add(pos);
                if (positions.Count >= maxCountNpc) break;
            }

            if (positions.Count < maxCountNpc)
            {
                ReturnFlare(player, entity, flareConfig);
                AlertToPlayer(player, GetMessage("NoUseFlareNotSpawnNpc", player.UserIDString, _config.Prefix));
                return;
            }

            if (Interface.CallHook("CanDefendableHomesStart", player, difficultyLevel.Name, cupboards, foundations, center3, radius, maxRadius) is bool)
            {
                ReturnFlare(player, entity, flareConfig);
                return;
            }

            CheckVersionPlugin();

            ControllerHomeRaid controller = new GameObject().AddComponent<ControllerHomeRaid>();
            controller.transform.position = center3;
            controller.DifficultyLevel = difficultyLevel;
            controller.Cupboards = cupboards;
            controller.Foundations = foundations;
            controller.Radius = radius;
            controller.MaxRadius = maxRadius;
            controller.Owner = player;
            controller.StartEvent();

            Controllers.Add(controller);
            if (Controllers.Count == 1) ToggleHooks(true);

            Interface.Oxide.CallHook($"On{Name}Start", controller.transform.position, controller.MaxRadius);
            AlertToPlayer(player, GetMessage("Start", player.UserIDString, _config.Prefix, difficultyLevel.Name));
        }

        private void OnExplosiveDropped(BasePlayer player, BaseEntity entity, ThrownWeapon weapon) => OnExplosiveThrown(player, entity, weapon);

        private void OnCorpsePopulate(ScientistNPC entity, NPCPlayerCorpse corpse)
        {
            if (entity == null) return;

            ControllerHomeRaid controller = Controllers.FirstOrDefault(x => x.Scientists.Contains(entity));
            if (controller == null) return;

            if (entity.displayName == "Bomber")
            {
                Effect.server.Run("assets/prefabs/tools/c4/effects/c4_explosion.prefab", entity.transform.position + new Vector3(0f, 1f, 0f), Vector3.up, null, true);
                OnBomberExplosion(entity, null);
            }

            controller.Scientists.Remove(entity);
            
            // Unregister from GrimmNPC
            UnregisterNpcFromGrimmNpc(entity);

            NextTick(() =>
            {
                if (corpse == null) return;
                corpse.containers[0].ClearItemsContainer();
                if (!corpse.IsDestroyed) corpse.Kill();
            });
        }

        private void OnEntitySpawned(HackableLockedCrate crate)
        {
            if (crate == null) return;

            ControllerHomeRaid controller = Controllers.FirstOrDefault(x => Vector2.Distance(new Vector2(x.transform.position.x, x.transform.position.z), new Vector2(crate.transform.position.x, crate.transform.position.z)) < x.MaxRadius);
            if (controller == null) return;

            HackCrateConfig config = controller.DifficultyLevel.HackCrate;
            controller.Crate = crate;

            crate.hackSeconds = HackableLockedCrate.requiredHackSeconds - config.UnlockTime;

            crate.shouldDecay = false;
            crate.CancelInvoke(crate.DelayedDestroy);

            crate.KillMapMarker();

            if (config.TypeLootTable is 1 or 4 or 5)
            {
                NextTick(() =>
                {
                    crate.inventory.ClearItemsContainer();
                    if (config.TypeLootTable is 1 or 5)
                    {
                        if (config.OwnLootTable.UseCount) crate.inventory.capacity = 36 - config.OwnLootTable.Max;
                        else crate.inventory.capacity = 36 - config.OwnLootTable.Items.Count;
                    }
                    else crate.inventory.capacity = 36;
                    if (config.TypeLootTable is 4 or 5) AddToContainerPrefab(crate.inventory, config.PrefabLootTable);
                    if (config.TypeLootTable is 1 or 5) AddToContainerItem(crate.inventory, config.OwnLootTable);
                });
            }
        }
        #endregion Oxide Hooks

        #region Performance Optimizations
        // Entity cleanup comparer for optimal cleanup order
        private class EntityCleanupComparer : IComparer<BaseEntity>
        {
            public int Compare(BaseEntity x, BaseEntity y)
            {
                if (x == null && y == null) return 0;
                if (x == null) return 1;
                if (y == null) return -1;
                
                // Sort by entity type complexity (simple entities first)
                int xPriority = GetEntityPriority(x);
                int yPriority = GetEntityPriority(y);
                
                if (xPriority != yPriority)
                    return xPriority.CompareTo(yPriority);
                
                // Secondary sort by network ID for consistency
                return (x.net?.ID.Value ?? 0UL).CompareTo(y.net?.ID.Value ?? 0UL);
            }
            
            private int GetEntityPriority(BaseEntity entity)
            {
                // Lower priority = cleaned up first
                if (entity is Item) return 1;           // Items first
                if (entity is LootContainer) return 2;
                if (entity is Deployable) return 3;
                if (entity is BuildingBlock) return 4; // Building blocks last
                if (entity is BaseVehicle) return 5;   // Vehicles last (may have children)
                if (entity is ScientistNPC) return 3;   // NPCs (same as deployables)
                return 6; // Unknown types
            }
        }
        
        // Adaptive batched cleanup coroutine
        private IEnumerator AdaptiveBatchedKillEntitiesCo(
            List<BaseEntity> entities, 
            int baseBatchSize, 
            float baseDelay, 
            Action onComplete = null)
        {
            if (entities == null || entities.Count == 0)
            {
                onComplete?.Invoke();
                yield break;
            }

            // Remove already destroyed entities
            entities.RemoveAll(e => e == null || e.IsDestroyed);
            
            if (entities.Count == 0)
            {
                onComplete?.Invoke();
                yield break;
            }

            // Sort entities for optimal cleanup order
            entities.Sort(new EntityCleanupComparer());

            int threshold = baseBatchSize;
            int processed = 0;
            WaitForSeconds delay = CoroutineEx.waitForSeconds(baseDelay);

            while (entities.Count > 0)
            {
                // ADAPTIVE: Check FPS and adjust batch size
                if (processed >= threshold)
                {
                    processed = 0;
                    
                    // If FPS drops below threshold, process 1 at a time
                    float currentFPS = Performance.report.frameRate;
                    threshold = currentFPS < _config.AdaptiveCleanupFpsThreshold ? 1 : baseBatchSize;
                    
                    yield return delay;
                }

                BaseEntity entity = entities[0];
                entities.RemoveAt(0);
                processed++;

                if (entity != null && !entity.IsDestroyed)
                {
                    try 
                    { 
                        entity.Kill();
                    } 
                    catch { }
                }
            }

            onComplete?.Invoke();
        }
        
        // Adaptive batched cleanup for ScientistNPC with GrimmNPC unregistration
        private IEnumerator AdaptiveBatchedKillNpcsCo(
            List<ScientistNPC> npcs, 
            int baseBatchSize, 
            float baseDelay, 
            Action onComplete = null)
        {
            if (npcs == null || npcs.Count == 0)
            {
                onComplete?.Invoke();
                yield break;
            }

            // Remove already destroyed NPCs
            npcs.RemoveAll(e => e == null || e.IsDestroyed);
            
            if (npcs.Count == 0)
            {
                onComplete?.Invoke();
                yield break;
            }

            int threshold = baseBatchSize;
            int processed = 0;
            WaitForSeconds delay = CoroutineEx.waitForSeconds(baseDelay);

            while (npcs.Count > 0)
            {
                // ADAPTIVE: Check FPS and adjust batch size
                if (processed >= threshold)
                {
                    processed = 0;
                    
                    // If FPS drops below threshold, process 1 at a time
                    float currentFPS = Performance.report.frameRate;
                    threshold = currentFPS < _config.AdaptiveCleanupFpsThreshold ? 1 : baseBatchSize;
                    
                    yield return delay;
                }

                ScientistNPC npc = npcs[0];
                npcs.RemoveAt(0);
                processed++;

                if (npc != null && !npc.IsDestroyed)
                {
                    try 
                    { 
                        // Unregister from GrimmNPC before killing
                        UnregisterNpcFromGrimmNpc(npc);
                        npc.Kill();
                    } 
                    catch { }
                }
            }

            onComplete?.Invoke();
        }
        #endregion Performance Optimizations

        #region Controller
        private HashSet<ControllerHomeRaid> Controllers { get; } = new HashSet<ControllerHomeRaid>();
        
        // Performance optimization: Track active operations to defer file I/O
        private readonly HashSet<string> _operationsInProgress = new HashSet<string>();

        internal void Finish(ControllerHomeRaid controller)
        {
            string operationId = $"finish_{controller.GetInstanceID()}";
            _operationsInProgress.Add(operationId);
            
            PlayerData playerData = PlayersData.FirstOrDefault(x => x.SteamId == controller.Owner.userID);
            if (playerData == null) PlayersData.Add(new PlayerData { SteamId = controller.Owner.userID, LastTime = new Dictionary<string, double> { [controller.DifficultyLevel.Name] = CurrentTime } });
            else
            {
                string name = controller.DifficultyLevel.Name;
                if (playerData.LastTime.ContainsKey(name)) playerData.LastTime[name] = CurrentTime;
                else playerData.LastTime.Add(name, CurrentTime);
            }
            
            SendBalance(controller);
            Controllers.Remove(controller);
            if (Controllers.Count == 0) ToggleHooks(false);
            Interface.Oxide.CallHook($"On{Name}End", controller.transform.position);
            
            // Save data (cleanup happens in OnDestroy, so save is safe here)
            _operationsInProgress.Remove(operationId);
            SaveData();
            
            UnityEngine.Object.Destroy(controller.gameObject);
        }

        internal class ControllerHomeRaid : FacepunchBehaviour
        {
            private PluginConfig _config => _ins._config;
            internal DifficultyLevelConfig DifficultyLevel { get; set; } = null;

            private SphereCollider SphereCollider { get; set; } = null;

            internal HashSet<BuildingPrivlidge> Cupboards { get; set; } = null;
            internal HashSet<BuildingBlock> Foundations { get; set; } = null;
            internal float Radius { get; set; } = 0f;
            internal float MaxRadius { get; set; } = 0f;

            private Coroutine WaveCoroutine { get; set; } = null;
            internal int Seconds { get; set; } = 0;
            internal int MaxSeconds { get; set; } = 0;
            internal bool AllWavesEnd { get; set; } = false;

            internal HashSet<ScientistNPC> Scientists { get; } = new HashSet<ScientistNPC>();

            private Coroutine Ch47Coroutine { get; set; } = null;
            private CH47Helicopter Ch47 { get; set; } = null;
            private CH47HelicopterAIController Ch47Ai { get; set; } = null;
            internal HackableLockedCrate Crate { get; set; } = null;

            internal Dictionary<ulong, double> PlayersBalance { get; } = new Dictionary<ulong, double>();

            internal HashSet<BasePlayer> Players { get; } = new HashSet<BasePlayer>();
            internal BasePlayer Owner { get; set; } = null;

            internal void StartEvent()
            {
                gameObject.layer = 3;
                SphereCollider = gameObject.AddComponent<SphereCollider>();
                SphereCollider.isTrigger = true;
                SphereCollider.radius = MaxRadius;
                WaveCoroutine = ServerMgr.Instance.StartCoroutine(ProcessWave(1));
            }

            private void OnDestroy()
            {
                if (WaveCoroutine != null) ServerMgr.Instance.StopCoroutine(WaveCoroutine);
                if (Ch47Coroutine != null) ServerMgr.Instance.StopCoroutine(Ch47Coroutine);

                if (SphereCollider != null) Destroy(SphereCollider);

                foreach (BasePlayer player in Players) CuiHelper.DestroyUi(player, "Tabs_KpucTaJl");
                
                // Clean up stuck NPC checks
                foreach (var npc in Scientists)
                {
                    if (_ins._npcStuckCheck.ContainsKey(npc))
                        _ins._npcStuckCheck.Remove(npc);
                }

                // Use adaptive batched cleanup for NPCs
                if (Scientists.Count > 0)
                {
                    var npcsToKill = Pool.Get<List<ScientistNPC>>();
                    try
                    {
                        foreach (ScientistNPC npc in Scientists)
                        {
                            if (npc.IsExists())
                                npcsToKill.Add(npc);
                        }
                        
                        if (npcsToKill.Count > 0 && _config.EnableAdaptiveCleanup)
                        {
                            ServerMgr.Instance.StartCoroutine(
                                _ins.AdaptiveBatchedKillNpcsCo(
                                    npcsToKill, 
                                    _config.BaseBatchSize, 
                                    _config.BaseBatchDelay, 
                                    null
                                )
                            );
                        }
                        else
                        {
                            // Fallback to immediate cleanup if adaptive is disabled
                            foreach (ScientistNPC npc in npcsToKill)
                            {
                                if (npc.IsExists())
                                {
                                    _ins.UnregisterNpcFromGrimmNpc(npc);
                                    npc.Kill();
                                }
                            }
                        }
                    }
                    finally
                    {
                        // Don't free if passed to coroutine - it will handle cleanup
                        if (!_config.EnableAdaptiveCleanup)
                            Pool.FreeUnmanaged(ref npcsToKill);
                    }
                }

                if (Ch47.IsExists()) Ch47.Kill();
            }

            private void OnTriggerEnter(Collider other) => EnterPlayer(other.GetComponentInParent<BasePlayer>());

            internal void EnterPlayer(BasePlayer player)
            {
                if (!player.IsPlayer()) return;
                if (Players.Contains(player)) return;
                Players.Add(player);
                Interface.Oxide.CallHook($"OnPlayerEnter{_ins.Name}", player);
                _ins.AlertToPlayer(player, _ins.GetMessage("Enter", player.UserIDString, _config.Prefix, DifficultyLevel.Name));
            }

            private void OnTriggerExit(Collider other) => ExitPlayer(other.GetComponentInParent<BasePlayer>());

            internal void ExitPlayer(BasePlayer player)
            {
                if (!player.IsPlayer()) return;
                if (!Players.Contains(player)) return;
                Players.Remove(player);
                Interface.Oxide.CallHook($"OnPlayerExit{_ins.Name}", player);
                _ins.AlertToPlayer(player, _ins.GetMessage("Exit", player.UserIDString, _config.Prefix, DifficultyLevel.Name));
                if (_config.Gui.IsGui) CuiHelper.DestroyUi(player, "Tabs_KpucTaJl");
            }

            private IEnumerator ProcessWave(int level)
            {
                WaveConfig wave = DifficultyLevel.Waves.FirstOrDefault(x => x.Level == level);

                foreach (BasePlayer player in Players) _ins.AlertToPlayer(player, _ins.GetMessage("PreparationTime", player.UserIDString, _config.Prefix, level));

                Seconds = MaxSeconds = wave.TimeToStart;
                while (Seconds > 0)
                {
                    if (Foundations.Count == 0) yield break;
                    foreach (BasePlayer player in Players) _ins.CreateTabs(player, new Dictionary<string, string> { ["Clock_KpucTaJl"] = GetTimeFormat(Seconds), ["Foundation_KpucTaJl"] = $"{Foundations.Count}" });
                    yield return CoroutineEx.waitForSeconds(1f);
                    Seconds--;
                }

                foreach (BasePlayer player in Players) _ins.AlertToPlayer(player, _ins.GetMessage("AttackWave", player.UserIDString, _config.Prefix));

                int timerNpc = Seconds = MaxSeconds = wave.Duration;
                while (Seconds > 0)
                {
                    if (Foundations.Count == 0) yield break;
                    foreach (BasePlayer player in Players) _ins.CreateTabs(player, new Dictionary<string, string> { ["Clock_KpucTaJl"] = GetTimeFormat(Seconds), ["Foundation_KpucTaJl"] = $"{Foundations.Count}", ["Npc_KpucTaJl"] = $"{Scientists.Count}" });
                    if (timerNpc == Seconds)
                    {
                        timerNpc = Seconds - wave.TimerNpc;
                        float wait = 0f;
                        foreach (PresetConfig preset in wave.Presets)
                        {
                            float chance = UnityEngine.Random.Range(0f, 100f);
                            AmountConfig amountConfig = preset.Config.FirstOrDefault(x => chance <= x.Chance);
                            if (amountConfig == null) continue;
                            int count = amountConfig.Count;
                            JObject objectConfig = preset.ShortName switch
                            {
                                "Sledge" => GetSledgeObjectConfig(),
                                "Juggernaut" => GetJuggernautObjectConfig(),
                                "Rocketman" => GetRocketmanObjectConfig(),
                                "Bomber" => GetBomberObjectConfig(),
                                _ => null
                            };
                            for (int i = 0; i < count; i++)
                            {
                                Vector3 pos = GetRandomPos(transform.position, Radius, MaxRadius);
                                if (pos == Vector3.zero) continue;
                                switch (preset.ShortName)
                                {
                                    case "Sledge":
                                        SetSledgeWeapons(objectConfig);
                                        break;
                                    case "Juggernaut":
                                        SetJuggernautWeapons(objectConfig);
                                        break;
                                    case "Rocketman":
                                        SetRocketmanWeapons(objectConfig);
                                        break;
                                }
                                ScientistNPC npc = _ins.SpawnNpcWithGrimmNpc(pos, objectConfig);
                                if (npc != null)
                                {
                                    Scientists.Add(npc);
                                    
                                    // 🏠 RAID INTENT ENFORCEMENT: Assign raid mission immediately on spawn
                                    // CRITICAL: Set raid targets for NPCs that can raid (Sledge, Rocketman, Bomber)
                                    // Priority: TC > Doors > Walls > Foundations (replaces NpcSpawn's AddTargetRaid)
                                    if (preset.ShortName is "Sledge" or "Rocketman" or "Bomber")
                                    {
                                        _ins.SetNpcRaidTargets(npc, this);
                                        // Assign raid mission with role, target building, and preferred breach point
                                        _ins.AssignRaidMission(npc, preset.ShortName, this);
                                    }
                                    
                                    // Handle Juggernaut guard target
                                    if (preset.ShortName == "Juggernaut")
                                    {
                                        SetJuggernautGuardTarget(npc, this);
                                    }
                                    
                                    // Handle Bomber explosive weapon
                                    if (preset.ShortName == "Bomber")
                                    {
                                        Item item = ItemManager.CreateByName("explosive.timed");
                                        if (item != null)
                                        {
                                            if (item.MoveToContainer(npc.inventory.containerBelt))
                                            {
                                                npc.UpdateActiveItem(item.uid);
                                            }
                                            else
                                            {
                                                item.Remove();
                                            }
                                        }
                                    }
                                    
                                    // Start periodic check for all NPCs - shoot if LOS, pathfind to TC if no LOS
                                    _ins.StartStuckNpcCheck(npc, this);
                                }
                                yield return CoroutineEx.waitForSeconds(0.01f);
                            }
                            wait += 0.01f * count;
                        }
                        if (wait < 1f) yield return CoroutineEx.waitForSeconds(1f - wait);
                        else if (wait >= 2f) Seconds -= (int)(wait - 1f);
                    }
                    else yield return CoroutineEx.waitForSeconds(1f);
                    Seconds--;
                }

                ClearNpc();

                if (level < DifficultyLevel.Waves.Count) WaveCoroutine = ServerMgr.Instance.StartCoroutine(ProcessWave(level + 1));
                else
                {
                    foreach (BasePlayer player in Players)
                    {
                        CuiHelper.DestroyUi(player, "Tabs_KpucTaJl");
                        _ins.AlertToPlayer(player, _ins.GetMessage("Finish", player.UserIDString, _config.Prefix));
                    }
                    Ch47Coroutine = ServerMgr.Instance.StartCoroutine(ProcessCh47());
                    AllWavesEnd = true;
                }
            }

            private void SetSledgeWeapons(JObject objectConfig)
            {
                float chance = UnityEngine.Random.Range(0f, 100f);
                List<SledgeBelt> list = Pool.Get<List<SledgeBelt>>();
                foreach (SledgeBelt x in _config.Sledge.Weapons) if (chance <= x.Chance) list.Add(x);
                SledgeBelt belt = list.GetRandom();
                Pool.FreeUnmanaged(ref list);
                JArray result = new JArray { new JObject { ["ShortName"] = belt.ShortName, ["Amount"] = 1, ["SkinID"] = belt.SkinId, ["Mods"] = new JArray(), ["Ammo"] = string.Empty } };
                if (chance <= _config.Sledge.BeancanGrenade) result.Add(new JObject { ["ShortName"] = "grenade.beancan", ["Amount"] = 1, ["SkinID"] = 0, ["Mods"] = new JArray(), ["Ammo"] = string.Empty });
                
                // Always add satchels to Sledge NPCs so they can raid doors when they can't reach players
                result.Add(new JObject { ["ShortName"] = "explosive.satchel", ["Amount"] = 3, ["SkinID"] = 0, ["Mods"] = new JArray(), ["Ammo"] = string.Empty });
                
                objectConfig["BeltItems"] = result;
            }

            private void SetJuggernautWeapons(JObject objectConfig)
            {
                float chance = UnityEngine.Random.Range(0f, 100f);
                List<JuggernautBelt> list = Pool.Get<List<JuggernautBelt>>();
                foreach (JuggernautBelt x in _config.Juggernaut.Weapons) if (chance <= x.Chance) list.Add(x);
                JuggernautBelt belt = list.GetRandom();
                Pool.FreeUnmanaged(ref list);
                JArray result = new JArray { new JObject { ["ShortName"] = belt.ShortName, ["Amount"] = 1, ["SkinID"] = belt.SkinId, ["Mods"] = new JArray { belt.Mods }, ["Ammo"] = belt.Ammo } };
                if (chance <= _config.Juggernaut.BeancanGrenade) result.Add(new JObject { ["ShortName"] = "grenade.beancan", ["Amount"] = 1, ["SkinID"] = 0, ["Mods"] = new JArray(), ["Ammo"] = string.Empty });
                objectConfig["BeltItems"] = result;
                objectConfig["DamageScale"] = belt.DamageScale;
            }

            private void SetRocketmanWeapons(JObject objectConfig)
            {
                float chance = UnityEngine.Random.Range(0f, 100f);
                List<RocketmanBelt> list = Pool.Get<List<RocketmanBelt>>();
                foreach (RocketmanBelt x in _config.Rocketman.Weapons) if (chance <= x.Chance) list.Add(x);
                RocketmanBelt belt = list.GetRandom();
                Pool.FreeUnmanaged(ref list);
                string rocketAmmo = string.IsNullOrEmpty(_config.Rocketman.RocketAmmoShortName) ? "rocket_basic" : _config.Rocketman.RocketAmmoShortName;
                JArray result = new JArray
                {
                    new JObject { ["ShortName"] = belt.ShortName, ["Amount"] = 1, ["SkinID"] = belt.SkinId, ["Mods"] = new JArray { belt.Mods }, ["Ammo"] = belt.Ammo },
                    new JObject { ["ShortName"] = "rocket.launcher", ["Amount"] = 1, ["SkinID"] = 0, ["Mods"] = new JArray(), ["Ammo"] = rocketAmmo }
                };
                if (chance <= _config.Rocketman.GrenadeF1) result.Add(new JObject { ["ShortName"] = "grenade.f1", ["Amount"] = 1, ["SkinID"] = 0, ["Mods"] = new JArray(), ["Ammo"] = string.Empty });
                objectConfig["BeltItems"] = result;
            }

            private JObject GetSledgeObjectConfig()
            {
                return new JObject
                {
                    ["Name"] = "Sledge",
                    ["WearItems"] = new JArray { _config.Sledge.WearItems.Select(x => new JObject { ["ShortName"] = x.ShortName, ["SkinID"] = x.SkinId }) },
                    ["BeltItems"] = new JArray(),
                    ["Kit"] = string.Empty,
                    ["Health"] = _config.Sledge.Health,
                    ["RoamRange"] = 10f,
                    ["ChaseRange"] = MaxRadius,
                    ["SenseRange"] = MaxRadius, // Full range to detect players
                    ["ListenRange"] = MaxRadius / 2f,
                    ["AttackRangeMultiplier"] = 1f,
                    ["CheckVisionCone"] = true, // Enable LOS checking - shoot if LOS, raid TC if no LOS
                    ["VisionCone"] = 135f,
                    ["HostileTargetsOnly"] = false,
                    ["DamageScale"] = _config.Sledge.DamageScale,
                    ["TurretDamageScale"] = _config.Sledge.TurretDamageScale,
                    ["AimConeScale"] = 2f,
                    ["DisableRadio"] = true,
                    ["CanRunAwayWater"] = false,
                    ["CanSleep"] = false,
                    ["SleepDistance"] = 100f,
                    ["Speed"] = _config.Sledge.Speed,
                    ["AreaMask"] = 1,
                    ["AgentTypeID"] = -1372625422,
                    ["HomePosition"] = transform.position.ToString(),
                    ["MemoryDuration"] = 10f, // Normal memory duration - LOS check handles unreachable targets
                    ["States"] = new JArray { "RoamState", "CombatState", "ChaseState", "RaidStateMelee" } // Prioritize CombatState (shooting) when LOS, then RaidStateMelee (TC) when no LOS
                };
            }

            private JObject GetJuggernautObjectConfig()
            {
                return new JObject
                {
                    ["Name"] = "Juggernaut",
                    ["WearItems"] = new JArray { _config.Juggernaut.WearItems.Select(x => new JObject { ["ShortName"] = x.ShortName, ["SkinID"] = x.SkinId }) },
                    ["BeltItems"] = new JArray(),
                    ["Kit"] = string.Empty,
                    ["Health"] = _config.Juggernaut.Health,
                    ["RoamRange"] = 10f,
                    ["ChaseRange"] = MaxRadius,
                    ["SenseRange"] = MaxRadius,
                    ["ListenRange"] = MaxRadius / 2f,
                    ["AttackRangeMultiplier"] = _config.Juggernaut.AttackRangeMultiplier,
                    ["CheckVisionCone"] = true, // Enable LOS checking - shoot if LOS, raid TC if no LOS
                    ["VisionCone"] = 135f,
                    ["HostileTargetsOnly"] = false,
                    ["TurretDamageScale"] = _config.Juggernaut.TurretDamageScale,
                    ["AimConeScale"] = _config.Juggernaut.AimConeScale,
                    ["DisableRadio"] = true,
                    ["CanRunAwayWater"] = false,
                    ["CanSleep"] = false,
                    ["SleepDistance"] = 100f,
                    ["Speed"] = _config.Juggernaut.Speed,
                    ["AreaMask"] = 1,
                    ["AgentTypeID"] = -1372625422,
                    ["HomePosition"] = transform.position.ToString(),
                    ["MemoryDuration"] = 10f,
                    ["States"] = new JArray { "RoamState", "CombatState", "ChaseState" } // Juggernaut has HMLMG, no raiding equipment typically
                };
            }

            private JObject GetRocketmanObjectConfig()
            {
                return new JObject
                {
                    ["Name"] = "Rocketman",
                    ["WearItems"] = new JArray { _config.Rocketman.WearItems.Select(x => new JObject { ["ShortName"] = x.ShortName, ["SkinID"] = x.SkinId }) },
                    ["BeltItems"] = new JArray(),
                    ["Kit"] = string.Empty,
                    ["Health"] = _config.Rocketman.Health,
                    ["RoamRange"] = 10f,
                    ["ChaseRange"] = MaxRadius,
                    ["SenseRange"] = MaxRadius,
                    ["ListenRange"] = MaxRadius / 2f,
                    ["AttackRangeMultiplier"] = _config.Rocketman.AttackRangeMultiplier,
                    ["CheckVisionCone"] = true, // Enable LOS checking - shoot if LOS, raid TC if no LOS
                    ["VisionCone"] = 135f,
                    ["HostileTargetsOnly"] = false,
                    ["DamageScale"] = _config.Rocketman.DamageScale,
                    ["TurretDamageScale"] = _config.Rocketman.TurretDamageScale,
                    ["AimConeScale"] = _config.Rocketman.AimConeScale,
                    ["DisableRadio"] = true,
                    ["CanRunAwayWater"] = false,
                    ["CanSleep"] = false,
                    ["SleepDistance"] = 100f,
                    ["Speed"] = _config.Rocketman.Speed,
                    ["AreaMask"] = 1,
                    ["AgentTypeID"] = -1372625422,
                    ["HomePosition"] = transform.position.ToString(),
                    ["MemoryDuration"] = 10f,
                    ["States"] = new JArray { "RoamState", "CombatState", "ChaseState", "RaidState" } // Rocketman has rocket launcher - can door raid
                };
            }

            private JObject GetBomberObjectConfig()
            {
                return new JObject
                {
                    ["Name"] = "Bomber",
                    ["WearItems"] = new JArray { _config.Bomber.WearItems.Select(x => new JObject { ["ShortName"] = x.ShortName, ["SkinID"] = x.SkinId }) },
                    ["BeltItems"] = new JArray(),
                    ["Kit"] = string.Empty,
                    ["Health"] = _config.Bomber.Health,
                    ["RoamRange"] = 10f,
                    ["ChaseRange"] = MaxRadius,
                    ["SenseRange"] = MaxRadius, // Full range to detect players
                    ["ListenRange"] = MaxRadius / 2f,
                    ["AttackRangeMultiplier"] = _config.Bomber.AttackRangeMultiplier,
                    ["CheckVisionCone"] = true, // Enable LOS checking - shoot if LOS, raid TC if no LOS
                    ["VisionCone"] = 135f,
                    ["HostileTargetsOnly"] = false,
                    ["DamageScale"] = 1f,
                    ["TurretDamageScale"] = 1f,
                    ["AimConeScale"] = 2f,
                    ["DisableRadio"] = true,
                    ["CanRunAwayWater"] = false,
                    ["CanSleep"] = false,
                    ["SleepDistance"] = 100f,
                    ["Speed"] = _config.Bomber.Speed,
                    ["AreaMask"] = 1,
                    ["AgentTypeID"] = -1372625422,
                    ["HomePosition"] = transform.position.ToString(),
                    ["MemoryDuration"] = 10f, // Normal memory duration - LOS check handles unreachable targets
                    ["States"] = new JArray { "RoamState", "CombatState", "ChaseState", "RaidStateMelee" } // Prioritize CombatState (shooting) when LOS, then RaidStateMelee (TC) when no LOS
                };
            }

            internal void SetJuggernautGuardTarget(ScientistNPC npc, ControllerHomeRaid controller)
            {
                // Find nearest Sledge NPC to guard
                HashSet<ScientistNPC> sledges = controller.Scientists.Where(x => x.IsExists() && x.displayName == "Sledge");
                if (sledges != null && sledges.Count > 0)
                {
                    ScientistNPC nearestSledge = sledges.Min(x => Vector3.Distance(npc.transform.position, x.transform.position));
                    if (nearestSledge != null && nearestSledge.IsExists())
                    {
                        // Store guard target (replaces NpcSpawn's AddTargetGuard)
                        _ins.SetNpcGuardTarget(npc, nearestSledge);
                    }
                }
            }

            private void ClearNpc()
            {
                if (Scientists.Count == 0) return;
                
                var npcsToKill = Pool.Get<List<ScientistNPC>>();
                try
                {
                    foreach (ScientistNPC npc in Scientists)
                    {
                        if (npc.IsExists())
                            npcsToKill.Add(npc);
                    }
                    
                    Scientists.Clear();
                    
                    if (npcsToKill.Count > 0 && _config.EnableAdaptiveCleanup)
                    {
                        ServerMgr.Instance.StartCoroutine(
                            _ins.AdaptiveBatchedKillNpcsCo(
                                npcsToKill, 
                                _config.BaseBatchSize, 
                                _config.BaseBatchDelay, 
                                null
                            )
                        );
                    }
                    else
                    {
                        // Fallback to immediate cleanup if adaptive is disabled
                        foreach (ScientistNPC npc in npcsToKill)
                        {
                            if (npc.IsExists())
                            {
                                _ins.UnregisterNpcFromGrimmNpc(npc);
                                npc.Kill();
                            }
                        }
                        Pool.FreeUnmanaged(ref npcsToKill);
                    }
                }
                catch
                {
                    // Ensure pool is freed on error
                    if (npcsToKill != null && !_config.EnableAdaptiveCleanup)
                        Pool.FreeUnmanaged(ref npcsToKill);
                }
            }

            private IEnumerator ProcessCh47()
            {
                SpawnNewCh47(new Vector3(transform.position.x, 200f, transform.position.z), Quaternion.identity, transform.position, 1);

                while (Ch47.transform.position.y - Ch47Ai.currentDesiredAltitude > 1f) yield return CoroutineEx.waitForSeconds(1f);

                Ch47Ai.DropCrate();

                List<float> list = Pool.Get<List<float>>();
                list.Add(-World.Size / 2f);
                list.Add(World.Size / 2f);
                Vector3 destroyPos = new Vector3(list.GetRandom(), 200f, list.GetRandom());
                Pool.FreeUnmanaged(ref list);

                SpawnNewCh47(Ch47.transform.position, Ch47.transform.rotation, destroyPos, 0);

                while (Vector2.Distance(new Vector2(Ch47.transform.position.x, Ch47.transform.position.z), new Vector2(destroyPos.x, destroyPos.z)) > 1f) yield return CoroutineEx.waitForSeconds(1f);

                _ins.Finish(this);
            }

            private void SpawnNewCh47(Vector3 pos, Quaternion rot, Vector3 landingTarget, int numCrates)
            {
                CH47Helicopter ch47New = GameManager.server.CreateEntity("assets/prefabs/npc/ch47/ch47scientists.entity.prefab", pos, rot) as CH47Helicopter;
                CH47HelicopterAIController ch47AInew = ch47New.GetComponent<CH47HelicopterAIController>();

                ch47AInew.SetLandingTarget(landingTarget);

                if (Ch47.IsExists()) Ch47.Kill();

                Ch47 = ch47New;
                Ch47Ai = ch47AInew;

                Ch47.Spawn();
                Ch47Ai.CancelInvoke(Ch47Ai.GetPrivateAction("CheckSpawnScientists"));
                Ch47.rigidBody.detectCollisions = false;
                Ch47Ai.numCrates = numCrates;
            }
        }

        private void OnCustomNpcGuardTargetEnd(ScientistNPC npc)
        {
            // Note: GrimmNPC handles targeting automatically
            // This hook is kept for compatibility but no action needed
        }

        private object OnCustomNpcTarget(ScientistNPC attacker, BasePlayer player)
        {
            if (_config.TargetNpc || attacker == null || !player.IsPlayer()) return null;
            ControllerHomeRaid controller = Controllers.FirstOrDefault(x => x.Scientists.Contains(attacker));
            if (controller != null && !IsTeam(player.userID, controller.Owner.userID)) return false;
            return null;
        }

        private void OnBomberExplosion(ScientistNPC npc, BaseEntity target)
        {
            ControllerHomeRaid controller = Controllers.FirstOrDefault(x => x.Scientists.Contains(npc));
            if (controller == null) return;

            float radius = 2f;

            if (target.IsExists() && target is not BasePlayer && target is not BuildingBlock)
            {
                float distance = Vector3.Distance(npc.transform.position, target.transform.position);
                if (distance < radius) DealDamage(target as BaseCombatEntity, _config.Bomber.DamageConstruction);
                else if (distance < radius * 2f) DealDamage(target as BaseCombatEntity, _config.Bomber.DamageConstruction * 0.5f);
                else if (distance < radius * 3f) DealDamage(target as BaseCombatEntity, _config.Bomber.DamageConstruction * 0.25f);
            }

            foreach (BuildingBlock block in controller.Foundations.ToHashSet())
            {
                if (!block.IsExists()) continue;
                float distance = Vector3.Distance(npc.transform.position, block.transform.position);
                if (distance < radius) DealDamage(block, _config.Bomber.DamageFoundation);
                else if (distance < radius * 2f) DealDamage(block, _config.Bomber.DamageFoundation * 0.5f);
                else if (distance < radius * 3f) DealDamage(block, _config.Bomber.DamageFoundation * 0.25f);
            }

            foreach (BasePlayer player in controller.Players.ToHashSet())
            {
                float distance = Vector3.Distance(npc.transform.position, player.transform.position);
                if (distance < radius) player.Hurt(_config.Bomber.DamagePlayer, DamageType.Explosion, npc, false);
                else if (distance < radius * 2f) player.Hurt(_config.Bomber.DamagePlayer * 0.5f, DamageType.Explosion, npc, false);
                else if (distance < radius * 3f) player.Hurt(_config.Bomber.DamagePlayer * 0.25f, DamageType.Explosion, npc, false);
            }

            controller.Scientists.Remove(npc);
        }

        private static void DealDamage(BaseCombatEntity entity, float damage)
        {
            entity.health -= damage;
            entity.SendNetworkUpdate();
            if (entity.health <= 0f && entity.IsExists()) entity.Kill(BaseNetworkable.DestroyMode.Gib);
        }
        #endregion Controller

        #region Find Buildings
        private const int BlockedTopologyFoundation = (int)(TerrainTopology.Enum.Cliff | TerrainTopology.Enum.Cliffside | TerrainTopology.Enum.Decor | TerrainTopology.Enum.Building | TerrainTopology.Enum.Monument | TerrainTopology.Enum.Mountain);

        private HashSet<Vector3> Markers { get; set; } = new HashSet<Vector3>();

        private static bool IsValidHeightFoundation(Vector3 pos)
        {
            float height = TerrainMeta.HeightMap.GetHeight(pos);
            return !(pos.y - height < -0.5f) && !(height < -1.5f) && !(pos.y - height > 3f);
        }

        private static bool IsValidTopologyFoundation(Vector3 pos) => (TerrainMeta.TopologyMap.GetTopology(pos) & BlockedTopologyFoundation) == 0;

        private bool IsValidMarkersFoundation(Vector3 pos) => Markers.Count <= 0 || !Markers.Any(x => Vector3.Distance(x, pos) < 40f);

        private static Vector2 GetCenterHomePos(HashSet<BuildingBlock> blocks)
        {
            float xmin = blocks.Min(x => x.transform.position.x).transform.position.x;
            float xmax = blocks.Max(x => x.transform.position.x).transform.position.x;
            float zmin = blocks.Min(x => x.transform.position.z).transform.position.z;
            float zmax = blocks.Max(x => x.transform.position.z).transform.position.z;
            return new Vector2((xmin + xmax) / 2, (zmin + zmax) / 2);
        }

        // Check if a building block is a wall
        private static bool IsWall(BuildingBlock block)
        {
            if (block == null) return false;
            string prefabName = block.ShortPrefabName?.ToLower() ?? string.Empty;
            return prefabName.Contains("wall") || 
                   prefabName == "wall" || 
                   prefabName == "wall.external" ||
                   prefabName == "wall.frame" ||
                   prefabName == "wall.window" ||
                   prefabName == "wall.doorway";
        }
        
        // Find doors that block the path to tool cupboard
        private HashSet<Door> FindDoorsBlockingPathToTC(ScientistNPC npc, BuildingPrivlidge tc, ControllerHomeRaid controller)
        {
            HashSet<Door> blockingDoors = new HashSet<Door>();
            if (npc == null || tc == null || controller == null) return blockingDoors;
            
            Vector3 npcPos = npc.transform.position;
            Vector3 tcPos = tc.transform.position;
            Vector3 directionToTC = (tcPos - npcPos).normalized;
            float distanceToTC = Vector3.Distance(npcPos, tcPos);
            
            // Search for doors between NPC and TC
            List<Door> doors = Pool.Get<List<Door>>();
            try
            {
                Vis.Entities(npcPos, distanceToTC + 20f, doors, 2097152); // Building layer
                
                foreach (var door in doors)
                {
                    if (door == null || !door.IsExists()) continue;
                    if (door.IsOpen()) continue; // Skip open doors
                    
                    Vector3 doorPos = door.transform.position;
                    float distToDoor = Vector3.Distance(npcPos, doorPos);
                    float distFromDoorToTC = Vector3.Distance(doorPos, tcPos);
                    
                    // Door should be between NPC and TC
                    if (distToDoor < distanceToTC && distFromDoorToTC < distanceToTC)
                    {
                        // Check if door is in the direction of TC
                        Vector3 toDoor = (doorPos - npcPos).normalized;
                        float dot = Vector3.Dot(directionToTC, toDoor);
                        
                        if (dot > 0.3f) // Door is in general direction of TC
                        {
                            // Check if door blocks LOS to TC
                            Vector3 npcEyePos = npc.eyes.position;
                            RaycastHit hit;
                            Vector3 direction = (tcPos - npcEyePos).normalized;
                            
                            if (Physics.Raycast(npcEyePos, direction, out hit, distanceToTC, 2097152))
                            {
                                BaseEntity hitEntity = hit.collider?.GetComponentInParent<BaseEntity>();
                                if (hitEntity == door)
                                {
                                    blockingDoors.Add(door);
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                Pool.FreeUnmanaged(ref doors);
            }
            
            return blockingDoors;
        }
        
        // Find walls that block the path to tool cupboard
        private HashSet<BuildingBlock> FindWallsBlockingPathToTC(ScientistNPC npc, BuildingPrivlidge tc, ControllerHomeRaid controller)
        {
            HashSet<BuildingBlock> blockingWalls = new HashSet<BuildingBlock>();
            if (npc == null || tc == null || controller == null) return blockingWalls;
            
            Vector3 npcPos = npc.transform.position;
            Vector3 tcPos = tc.transform.position;
            Vector3 directionToTC = (tcPos - npcPos).normalized;
            float distanceToTC = Vector3.Distance(npcPos, tcPos);
            
            // Search for walls between NPC and TC
            List<BuildingBlock> blocks = Pool.Get<List<BuildingBlock>>();
            try
            {
                Vis.Entities<BuildingBlock>(npcPos, distanceToTC + 20f, blocks, 1 << 21);
                
                foreach (var block in blocks)
                {
                    if (block == null || !block.IsExists()) continue;
                    if (!IsWall(block)) continue; // Only walls
                    
                    Vector3 blockPos = block.transform.position;
                    float distToBlock = Vector3.Distance(npcPos, blockPos);
                    float distFromBlockToTC = Vector3.Distance(blockPos, tcPos);
                    
                    // Wall should be between NPC and TC
                    if (distToBlock < distanceToTC && distFromBlockToTC < distanceToTC)
                    {
                        // Check if wall is in the direction of TC
                        Vector3 toBlock = (blockPos - npcPos).normalized;
                        float dot = Vector3.Dot(directionToTC, toBlock);
                        
                        if (dot > 0.3f) // Wall is in general direction of TC
                        {
                            // Check if wall blocks LOS to TC
                            Vector3 npcEyePos = npc.eyes.position;
                            RaycastHit hit;
                            Vector3 direction = (tcPos - npcEyePos).normalized;
                            
                            if (Physics.Raycast(npcEyePos, direction, out hit, distanceToTC, 2097152))
                            {
                                BaseEntity hitEntity = hit.collider?.GetComponentInParent<BaseEntity>();
                                if (hitEntity == block)
                                {
                                    blockingWalls.Add(block);
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                Pool.FreeUnmanaged(ref blocks);
            }
            
            return blockingWalls;
        }
        
        private static bool IsFoundation(BuildingBlock block)
        {
            switch (block.ShortPrefabName)
            {
                case "foundation":
                case "foundation.triangle":
                    return true;
                default:
                    return false;
            }
        }

        // Helper method to check if a BuildingPrivlidge is a tool cupboard (supports all variants)
        private static bool IsToolCupboard(BuildingPrivlidge cupboard)
        {
            if (cupboard == null || !cupboard.IsExists()) return false;
            
            // Check prefab name to ensure it's a tool cupboard (supports all variants)
            string prefabName = cupboard.ShortPrefabName?.ToLower() ?? string.Empty;
            string fullPrefabPath = cupboard.PrefabName?.ToLower() ?? string.Empty;
            
            // Standard tool cupboard variants
            return prefabName.Contains("cupboard.tool") || 
                   fullPrefabPath.Contains("cupboard.tool") ||
                   prefabName == "cupboard.tool.retro.deployed" ||
                   fullPrefabPath.Contains("cupboard.tool.deployed") ||
                   fullPrefabPath.Contains("cupboard.tool.shockbyte");
        }

        private void FindAllBuildings(BuildingPrivlidge cupboard, out HashSet<BuildingPrivlidge> cupboards, out HashSet<BuildingBlock> foundations, out Vector3 center3, out float radius, out float maxRadius)
        {
            // Validate that the initial cupboard is actually a tool cupboard
            if (!IsToolCupboard(cupboard))
            {
                PrintWarning($"FindAllBuildings: Initial cupboard is not a valid tool cupboard! Prefab: {cupboard?.PrefabName ?? "null"}");
            }
            
            cupboards = new HashSet<BuildingPrivlidge> { cupboard };
            foundations = cupboard.GetBuilding().buildingBlocks.Where(x => IsFoundation(x) && x.OwnerID != 0);

            HashSet<ulong> ids = cupboard.authorizedPlayers.ToHashSet();
            foreach (BuildingBlock block in foundations) if (!ids.Contains(block.OwnerID)) ids.Add(block.OwnerID);

            Vector2 center2 = GetCenterHomePos(foundations);
            center3 = new Vector3(center2.x, TerrainMeta.HeightMap.GetHeight(new Vector3(center2.x, 0f, center2.y)), center2.y);
            BuildingBlock farthest = foundations.Max(x => Vector2.Distance(center2, new Vector2(x.transform.position.x, x.transform.position.z)));
            radius = Vector2.Distance(center2, new Vector2(farthest.transform.position.x, farthest.transform.position.z));
            maxRadius = radius * 2f > 50f ? radius * 2f : 50f;

            int attempts = 10;

            while (attempts > 0)
            {
                attempts--;

                HashSet<BuildingPrivlidge> anotherCupboards = new HashSet<BuildingPrivlidge>();
                HashSet<BuildingBlock> anotherFoundations = new HashSet<BuildingBlock>();

                foreach (BuildingBlock foundation in foundations)
                {
                    List<BuildingBlock> list = Pool.Get<List<BuildingBlock>>();
                    Vis.Entities<BuildingBlock>(foundation.transform.position, 10f, list, 1 << 21);
                    foreach (BuildingBlock block in list)
                    {
                        if (!IsFoundation(block)) continue;
                        if (block.OwnerID == 0) continue;
                        if (foundations.Contains(block)) continue;

                        BuildingPrivlidge buildingPrivlidge = block.GetBuildingPrivilege();

                        if (buildingPrivlidge == null)
                        {
                            if (Vector2.Distance(center2, new Vector2(block.transform.position.x, block.transform.position.z)) > maxRadius) continue;
                            if (!anotherFoundations.Contains(block)) anotherFoundations.Add(block);
                        }
                        else
                        {
                            // Only add tool cupboards (not other building privileges)
                            if (!IsToolCupboard(buildingPrivlidge))
                            {
                                // Not a tool cupboard, treat as foundation-only
                                if (Vector2.Distance(center2, new Vector2(block.transform.position.x, block.transform.position.z)) > maxRadius) continue;
                                if (!anotherFoundations.Contains(block)) anotherFoundations.Add(block);
                                continue;
                            }
                            
                            if (cupboards.Contains(buildingPrivlidge))
                            {
                                if (!anotherFoundations.Contains(block)) 
                                    anotherFoundations.Add(block);
                            }
                            else if (buildingPrivlidge.authorizedPlayers.Any(x => ids.Any(y => IsTeam(y, x))))
                            {
                                if (!anotherCupboards.Contains(buildingPrivlidge))
                                    anotherCupboards.Add(buildingPrivlidge);
                            }
                        }
                    }
                    Pool.FreeUnmanaged(ref list);
                }

                if (anotherFoundations.Count > 0)
                {
                    foreach (BuildingBlock foundation in anotherFoundations)
                    {
                        if (!foundations.Contains(foundation)) foundations.Add(foundation);
                        if (!ids.Contains(foundation.OwnerID)) ids.Add(foundation.OwnerID);
                    }
                }

                if (anotherCupboards.Count > 0)
                {
                    foreach (BuildingPrivlidge anotherCupboard in anotherCupboards)
                    {
                        if (!cupboards.Contains(anotherCupboard)) cupboards.Add(anotherCupboard);

                        foreach (BuildingBlock block in anotherCupboard.GetBuilding().buildingBlocks)
                        {
                            if (!IsFoundation(block)) continue;
                            if (block.OwnerID == 0) continue;
                            if (foundations.Contains(block)) continue;
                            foundations.Add(block);
                            if (!ids.Contains(block.OwnerID)) ids.Add(block.OwnerID);
                        }

                        foreach (ulong id in anotherCupboard.authorizedPlayers) if (!ids.Contains(id)) ids.Add(id);
                    }
                }

                if (anotherCupboards.Count == 0 && anotherFoundations.Count == 0)
                {
                    anotherFoundations = null;
                    anotherCupboards = null;
                    break;
                }
                else
                {
                    anotherFoundations.Clear();
                    anotherFoundations = null;
                    anotherCupboards.Clear();
                    anotherCupboards = null;

                    center2 = GetCenterHomePos(foundations);
                    center3 = new Vector3(center2.x, TerrainMeta.HeightMap.GetHeight(new Vector3(center2.x, 0f, center2.y)), center2.y);
                    farthest = foundations.Max(x => Vector2.Distance(center2, new Vector2(x.transform.position.x, x.transform.position.z)));
                    radius = Vector2.Distance(center2, new Vector2(farthest.transform.position.x, farthest.transform.position.z));
                    maxRadius = radius * 2f > 50f ? radius * 2f : 50f;
                }
            }

            List<BuildingBlock> blocks = Pool.Get<List<BuildingBlock>>();
            Vis.Entities<BuildingBlock>(center3, maxRadius, blocks, 1 << 21);
            foreach (BuildingBlock block in blocks)
            {
                if (!IsFoundation(block)) continue;
                if (block.OwnerID == 0) continue;
                if (foundations.Contains(block)) continue;
                if (ids.Any(x => IsTeam(x, block.OwnerID))) foundations.Add(block);
            }
            Pool.FreeUnmanaged(ref blocks);
        }
        #endregion Find Buildings

        #region Find position for Npc
        private const int BlockedTopologyNpc = (int)(TerrainTopology.Enum.Cliff | TerrainTopology.Enum.Cliffside | TerrainTopology.Enum.Decor | TerrainTopology.Enum.Building | TerrainTopology.Enum.Monument);
        private const int GroundLayers = 1 << 4 | 1 << 16 | 1 << 23;
        private const int EntityLayers = 1 << 8 | 1 << 21;

        private static Vector3 GetRandomPos(Vector3 center, float radius, float maxRadius)
        {
            RaycastHit raycastHit;
            NavMeshHit navmeshHit;

            int attempts = 0;

            while (attempts < 10)
            {
                attempts++;

                float d = UnityEngine.Random.Range(0f, 360f);
                float r = UnityEngine.Random.Range(radius, maxRadius);

                Vector3 result = new Vector3(center.x + r * Mathf.Sin(d * Mathf.Deg2Rad), 500f, center.z + r * Mathf.Cos(d * Mathf.Deg2Rad));

                if ((TerrainMeta.TopologyMap.GetTopology(result) & BlockedTopologyNpc) != 0) continue;

                if (!Physics.Raycast(result, Vector3.down, out raycastHit, 500f, GroundLayers)) continue;
                result.y = raycastHit.point.y;

                if (!NavMesh.SamplePosition(result, out navmeshHit, 2f, 1)) continue;
                result = navmeshHit.position;

                List<BaseCombatEntity> list = Pool.Get<List<BaseCombatEntity>>();
                Vis.Entities(result, 10f, list, EntityLayers);
                int count = list.Count;
                Pool.FreeUnmanaged(ref list);
                if (count > 0) continue;

                return result;
            }

            return Vector3.zero;
        }
        #endregion Find position for Npc

        #region Spawn Loot
        private object CanPopulateLoot(HackableLockedCrate container)
        {
            if (container == null) return null;
            ControllerHomeRaid controller = Controllers.FirstOrDefault(x => x.Crate.IsExists() && x.Crate == container);
            if (controller == null) return null;
            if (controller.DifficultyLevel.HackCrate.TypeLootTable != 2) return true;
            return null;
        }

        private object OnCustomLootContainer(NetworkableId netId)
        {
            ControllerHomeRaid controller = Controllers.FirstOrDefault(x => x.Crate.IsExists() && x.Crate.net != null && x.Crate.net.ID.Value == netId.Value);
            if (controller == null) return null;
            if (controller.DifficultyLevel.HackCrate.TypeLootTable != 3) return true;
            return null;
        }

        private object OnContainerPopulate(HackableLockedCrate container)
        {
            if (container == null) return null;
            ControllerHomeRaid controller = Controllers.FirstOrDefault(x => x.Crate.IsExists() && x.Crate == container);
            if (controller == null) return null;
            if (controller.DifficultyLevel.HackCrate.TypeLootTable != 6) return true;
            return null;
        }

        private void AddToContainerPrefab(ItemContainer container, PrefabLootTableConfig lootTable)
        {
            if (lootTable.UseCount)
            {
                int count = 0, max = UnityEngine.Random.Range(lootTable.Min, lootTable.Max + 1);
                while (count < max)
                {
                    foreach (PrefabConfig prefab in lootTable.Prefabs)
                    {
                        if (UnityEngine.Random.Range(0f, 100f) > prefab.Chance) continue;
                        SpawnIntoContainer(container, prefab.PrefabDefinition);
                        count++;
                        if (count == max) break;
                    }
                }
            }
            else foreach (PrefabConfig prefab in lootTable.Prefabs) if (UnityEngine.Random.Range(0f, 100f) <= prefab.Chance) SpawnIntoContainer(container, prefab.PrefabDefinition);
        }

        private void SpawnIntoContainer(ItemContainer container, string prefab)
        {
            if (AllLootSpawnSlots.ContainsKey(prefab))
            {
                foreach (LootContainer.LootSpawnSlot lootSpawnSlot in AllLootSpawnSlots[prefab])
                    for (int j = 0; j < lootSpawnSlot.numberToSpawn; j++)
                        if (UnityEngine.Random.Range(0f, 1f) <= lootSpawnSlot.probability)
                            lootSpawnSlot.definition.SpawnIntoContainer(container);
            }
            else AllLootSpawn[prefab].SpawnIntoContainer(container);
        }

        private void AddToContainerItem(ItemContainer container, LootTableConfig lootTable)
        {
            if (lootTable.UseCount)
            {
                HashSet<int> indexMove = new HashSet<int>();
                int count = UnityEngine.Random.Range(lootTable.Min, lootTable.Max + 1);
                while (indexMove.Count < count)
                {
                    for (int i = 0; i < lootTable.Items.Count; i++)
                    {
                        if (indexMove.Contains(i)) continue;
                        if (SpawnIntoContainer(container, lootTable.Items[i]))
                        {
                            indexMove.Add(i);
                            if (indexMove.Count == count) break;
                        }
                    }
                }
                indexMove = null;
            }
            else foreach (ItemConfig item in lootTable.Items) SpawnIntoContainer(container, item);
        }

        private bool SpawnIntoContainer(ItemContainer container, ItemConfig config)
        {
            if (UnityEngine.Random.Range(0f, 100f) > config.Chance) return false;
            Item item = config.IsBluePrint ? ItemManager.CreateByName("blueprintbase") : ItemManager.CreateByName(config.ShortName, UnityEngine.Random.Range(config.MinAmount, config.MaxAmount + 1), config.SkinId);
            if (item == null)
            {
                PrintWarning($"Failed to create item! ({config.ShortName})");
                return false;
            }
            if (config.IsBluePrint) item.blueprintTarget = ItemManager.FindItemDefinition(config.ShortName).itemid;
            if (!string.IsNullOrEmpty(config.Name)) item.name = config.Name;
            if (container.capacity < container.itemList.Count + 1) container.capacity++;
            if (!item.MoveToContainer(container))
            {
                item.Remove();
                return false;
            }
            return true;
        }

        private void CheckAllLootTables()
        {
            foreach (DifficultyLevelConfig level in _config.DifficultyLevels)
            {
                CheckLootTable(level.HackCrate.OwnLootTable);
                CheckPrefabLootTable(level.HackCrate.PrefabLootTable);
            }
            SaveConfig();
        }

        private void CheckLootTable(LootTableConfig lootTable)
        {
            for (int i = lootTable.Items.Count - 1; i >= 0; i--)
            {
                ItemConfig item = lootTable.Items[i];

                if (!ItemManager.itemList.Any(x => x.shortname == item.ShortName))
                {
                    PrintWarning($"Unknown item removed! ({item.ShortName})");
                    lootTable.Items.Remove(item);
                    continue;
                }
                if (item.Chance <= 0f)
                {
                    PrintWarning($"An item with an incorrect probability has been removed from the loot table ({item.ShortName})");
                    lootTable.Items.Remove(item);
                    continue;
                }

                if (item.MinAmount <= 0) item.MinAmount = 1;
                if (item.MaxAmount < item.MinAmount) item.MaxAmount = item.MinAmount;
            }

            lootTable.Items = lootTable.Items.OrderByQuickSort(x => x.Chance);
            if (lootTable.Items.Any(x => x.Chance >= 100f))
            {
                HashSet<ItemConfig> newItems = new HashSet<ItemConfig>();

                for (int i = lootTable.Items.Count - 1; i >= 0; i--)
                {
                    ItemConfig itemConfig = lootTable.Items[i];
                    if (itemConfig.Chance < 100f) break;
                    newItems.Add(itemConfig);
                    lootTable.Items.Remove(itemConfig);
                }

                int count = newItems.Count;

                if (count > 0)
                {
                    foreach (ItemConfig itemConfig in lootTable.Items) newItems.Add(itemConfig);
                    lootTable.Items.Clear();
                    foreach (ItemConfig itemConfig in newItems) lootTable.Items.Add(itemConfig);
                }

                newItems = null;

                if (lootTable.Min < count) lootTable.Min = count;
                if (lootTable.Max < count) lootTable.Max = count;
            }

            if (lootTable.Max > lootTable.Items.Count) lootTable.Max = lootTable.Items.Count;
            if (lootTable.Min > lootTable.Max) lootTable.Min = lootTable.Max;
            if (lootTable.Items.Count == 0) lootTable.UseCount = false;
        }

        private void CheckPrefabLootTable(PrefabLootTableConfig lootTable)
        {
            HashSet<string> prefabs = new HashSet<string>();

            for (int i = lootTable.Prefabs.Count - 1; i >= 0; i--)
            {
                PrefabConfig prefab = lootTable.Prefabs[i];
                if (prefabs.Any(x => x == prefab.PrefabDefinition))
                {
                    lootTable.Prefabs.Remove(prefab);
                    PrintWarning($"Duplicate prefab removed from loot table! ({prefab.PrefabDefinition})");
                }
                else
                {
                    GameObject gameObject = GameManager.server.FindPrefab(prefab.PrefabDefinition);
                    global::HumanNPC humanNpc = gameObject.GetComponent<global::HumanNPC>();
                    ScarecrowNPC scarecrowNpc = gameObject.GetComponent<ScarecrowNPC>();
                    LootContainer lootContainer = gameObject.GetComponent<LootContainer>();
                    if (humanNpc != null && humanNpc.LootSpawnSlots.Length != 0)
                    {
                        if (!AllLootSpawnSlots.ContainsKey(prefab.PrefabDefinition)) AllLootSpawnSlots.Add(prefab.PrefabDefinition, humanNpc.LootSpawnSlots);
                        prefabs.Add(prefab.PrefabDefinition);
                    }
                    else if (scarecrowNpc != null && scarecrowNpc.LootSpawnSlots.Length != 0)
                    {
                        if (!AllLootSpawnSlots.ContainsKey(prefab.PrefabDefinition)) AllLootSpawnSlots.Add(prefab.PrefabDefinition, scarecrowNpc.LootSpawnSlots);
                        prefabs.Add(prefab.PrefabDefinition);
                    }
                    else if (lootContainer != null && lootContainer.LootSpawnSlots.Length != 0)
                    {
                        if (!AllLootSpawnSlots.ContainsKey(prefab.PrefabDefinition)) AllLootSpawnSlots.Add(prefab.PrefabDefinition, lootContainer.LootSpawnSlots);
                        prefabs.Add(prefab.PrefabDefinition);
                    }
                    else if (lootContainer != null && lootContainer.lootDefinition != null)
                    {
                        if (!AllLootSpawn.ContainsKey(prefab.PrefabDefinition)) AllLootSpawn.Add(prefab.PrefabDefinition, lootContainer.lootDefinition);
                        prefabs.Add(prefab.PrefabDefinition);
                    }
                    else
                    {
                        lootTable.Prefabs.Remove(prefab);
                        PrintWarning($"Unknown prefab removed! ({prefab.PrefabDefinition})");
                    }
                }
            }

            prefabs = null;

            lootTable.Prefabs = lootTable.Prefabs.OrderByQuickSort(x => x.Chance);
            if (lootTable.Prefabs.Any(x => x.Chance >= 100f))
            {
                HashSet<PrefabConfig> newPrefabs = new HashSet<PrefabConfig>();

                for (int i = lootTable.Prefabs.Count - 1; i >= 0; i--)
                {
                    PrefabConfig prefabConfig = lootTable.Prefabs[i];
                    if (prefabConfig.Chance < 100f) break;
                    newPrefabs.Add(prefabConfig);
                    lootTable.Prefabs.Remove(prefabConfig);
                }

                int count = newPrefabs.Count;

                if (count > 0)
                {
                    foreach (PrefabConfig prefabConfig in lootTable.Prefabs) newPrefabs.Add(prefabConfig);
                    lootTable.Prefabs.Clear();
                    foreach (PrefabConfig prefabConfig in newPrefabs) lootTable.Prefabs.Add(prefabConfig);
                }

                newPrefabs = null;

                if (lootTable.Min < count) lootTable.Min = count;
                if (lootTable.Max < count) lootTable.Max = count;
            }

            if (lootTable.Min > lootTable.Max) lootTable.Min = lootTable.Max;
            if (lootTable.Prefabs.Count == 0) lootTable.UseCount = false;
        }

        private Dictionary<string, LootSpawn> AllLootSpawn { get; } = new Dictionary<string, LootSpawn>();
        private Dictionary<string, LootContainer.LootSpawnSlot[]> AllLootSpawnSlots { get; } = new Dictionary<string, LootContainer.LootSpawnSlot[]>();
        #endregion Spawn Loot

        #region BetterNpc
        private object CanCh47SpawnNpc(CH47HelicopterAIController ai)
        {
            if (Controllers.Any(x => Vector3.Distance(x.transform.position, ai.transform.position) < x.MaxRadius)) return true;
            else return null;
        }
        #endregion BetterNpc

        #region BaseRepair
        private object OnBaseRepair(BuildingManager.Building building, BasePlayer player)
        {
            if (player.IsPlayer() && Controllers.Any(x => x.Players.Contains(player))) return true;
            else return null;
        }
        #endregion BaseRepair

        #region TruePve
        private object CanEntityBeTargeted(ScientistNPC npc, AutoTurret turret)
        {
            if (npc == null || npc.skinID != 11162132011012) return null;
            if (Controllers.Any(x => x.Scientists.Contains(npc))) return true;
            else return null;
        }
        #endregion TruePve

        #region NTeleportation
        private void OnPlayerTeleported(BasePlayer player, Vector3 oldPos, Vector3 newPos)
        {
            if (!player.IsPlayer()) return;

            ControllerHomeRaid controller1 = Controllers.FirstOrDefault(x => x.Players.Contains(player));
            if (controller1 != null && Vector3.Distance(controller1.transform.position, newPos) > controller1.MaxRadius) controller1.ExitPlayer(player);

            ControllerHomeRaid controller2 = Controllers.FirstOrDefault(x => Vector3.Distance(x.transform.position, newPos) < x.MaxRadius);
            if (controller2 != null && !controller2.Players.Contains(player)) controller2.EnterPlayer(player);
        }
        #endregion NTeleportation

        #region Economy
        [PluginReference] private readonly Plugin Economics, ServerRewards, IQEconomic, XPerience;

        private void ActionEconomy(ControllerHomeRaid controller, ulong playerId, string type, string arg = "")
        {
            if (type == "KillNpc")
            {
                if (_config.Economy.KillNpc.TryGetValue(arg, out double value)) 
                    AddBalance(controller, playerId, value);
            }
        }

        private static void AddBalance(ControllerHomeRaid controller, ulong playerId, double balance)
        {
            if (balance == 0) return;
            if (controller.PlayersBalance.ContainsKey(playerId)) controller.PlayersBalance[playerId] += balance;
            else controller.PlayersBalance.Add(playerId, balance);
        }

        private void SendBalance(ControllerHomeRaid controller)
        {
            if (controller.PlayersBalance.Count == 0) return;
            if (_config.Economy.Plugins.Count > 0)
            {
                foreach (KeyValuePair<ulong, double> dic in controller.PlayersBalance)
                {
                    if (dic.Value < _config.Economy.Min) continue;
                    int intCount = Convert.ToInt32(dic.Value);
                    if (_config.Economy.Plugins.Contains("Economics") && plugins.Exists("Economics") && dic.Value > 0) Economics.Call("Deposit", dic.Key.ToString(), dic.Value);
                    if (_config.Economy.Plugins.Contains("Server Rewards") && plugins.Exists("ServerRewards") && intCount > 0) ServerRewards.Call("AddPoints", dic.Key, intCount);
                    if (_config.Economy.Plugins.Contains("IQEconomic") && plugins.Exists("IQEconomic") && intCount > 0) IQEconomic.Call("API_SET_BALANCE", dic.Key, intCount);
                    BasePlayer player = BasePlayer.FindByID(dic.Key);
                    if (player != null)
                    {
                        if (_config.Economy.Plugins.Contains("XPerience") && plugins.Exists("XPerience") && dic.Value > 0) XPerience?.Call("GiveXP", player, dic.Value);
                        AlertToPlayer(player, GetMessage("SendEconomy", player.UserIDString, _config.Prefix, dic.Value));
                    }
                }
            }
            ulong winnerId = controller.PlayersBalance.Max(x => x.Value).Key;
            Interface.Oxide.CallHook($"On{Name}Winner", winnerId);
            foreach (string command in _config.Economy.Commands) Server.Command(command.Replace("{steamid}", $"{winnerId}"));
            controller.PlayersBalance.Clear();
        }
        #endregion Economy

        #region Alerts
        [PluginReference] private readonly Plugin GUIAnnouncements, Notify;

        private string ClearColorAndSize(string message)
        {
            message = message.Replace("</color>", string.Empty);
            message = message.Replace("</size>", string.Empty);
            while (message.Contains("<color="))
            {
                int index = message.IndexOf("<color=", StringComparison.Ordinal);
                message = message.Remove(index, message.IndexOf(">", index, StringComparison.Ordinal) - index + 1);
            }
            while (message.Contains("<size="))
            {
                int index = message.IndexOf("<size=", StringComparison.Ordinal);
                message = message.Remove(index, message.IndexOf(">", index, StringComparison.Ordinal) - index + 1);
            }
            if (!string.IsNullOrEmpty(_config.Prefix)) message = message.Replace(_config.Prefix + " ", string.Empty);
            return message;
        }

        private void AlertToPlayer(BasePlayer player, string message)
        {
            if (_config.IsChat) PrintToChat(player, message);
            if (_config.GameTip.IsGameTip) player.SendConsoleCommand("gametip.showtoast", _config.GameTip.Style, ClearColorAndSize(message), string.Empty);
            if (_config.GuiAnnouncements.IsGuiAnnouncements) GUIAnnouncements?.Call("CreateAnnouncement", ClearColorAndSize(message), _config.GuiAnnouncements.BannerColor, _config.GuiAnnouncements.TextColor, player, _config.GuiAnnouncements.ApiAdjustVPosition);
            if (_config.Notify.IsNotify && plugins.Exists("Notify")) Notify?.Call("SendNotify", player, _config.Notify.Type, ClearColorAndSize(message));
        }
        #endregion Alerts

        #region GUI
        private HashSet<string> Names { get; } = new HashSet<string>
        {
            "Tab_KpucTaJl",
            "Clock_KpucTaJl",
            "Npc_KpucTaJl",
            "Foundation_KpucTaJl"
        };
        private Dictionary<string, string> Images { get; } = new Dictionary<string, string>();

        private IEnumerator DownloadImages()
        {
            foreach (string name in Names)
            {
                string url = "file://" + Interface.Oxide.DataDirectory + Path.DirectorySeparatorChar + "Images" + Path.DirectorySeparatorChar + name + ".png";
                using (UnityWebRequest unityWebRequest = UnityWebRequestTexture.GetTexture(url))
                {
                    yield return unityWebRequest.SendWebRequest();
                    if (unityWebRequest.result != UnityWebRequest.Result.Success)
                    {
                        PrintError($"Image {name} was not found. Maybe you didn't upload it to the .../oxide/data/Images/ folder");
                        break;
                    }
                    else
                    {
                        Texture2D tex = DownloadHandlerTexture.GetContent(unityWebRequest);
                        Images.Add(name, FileStorage.server.Store(tex.EncodeToPNG(), FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID).ToString());
                        Puts($"Image {name} download is complete");
                        UnityEngine.Object.DestroyImmediate(tex);
                    }
                }
            }
            if (Images.Count < Names.Count) Interface.Oxide.UnloadPlugin(Name);
        }

        private void CreateTabs(BasePlayer player, Dictionary<string, string> tabs)
        {
            CuiHelper.DestroyUi(player, "Tabs_KpucTaJl");

            CuiElementContainer container = new CuiElementContainer();

            float border = 52.5f + 54.5f * (tabs.Count - 1);
            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"{-border} {_config.Gui.OffsetMinY}", OffsetMax = $"{border} {_config.Gui.OffsetMinY + 20}" },
                CursorEnabled = false,
            }, "Under", "Tabs_KpucTaJl");

            int i = 0;

            foreach (KeyValuePair<string, string> dic in tabs)
            {
                i++;
                float xmin = 109f * (i - 1);
                container.Add(new CuiElement
                {
                    Name = $"Tab_{i}_KpucTaJl",
                    Parent = "Tabs_KpucTaJl",
                    Components =
                    {
                        new CuiRawImageComponent { Png = Images["Tab_KpucTaJl"] },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = $"{xmin} 0", OffsetMax = $"{xmin + 105f} 20" }
                    }
                });
                container.Add(new CuiElement
                {
                    Parent = $"Tab_{i}_KpucTaJl",
                    Components =
                    {
                        new CuiRawImageComponent { Png = Images[dic.Key] },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "9 3", OffsetMax = "23 17" }
                    }
                });
                container.Add(new CuiElement
                {
                    Parent = $"Tab_{i}_KpucTaJl",
                    Components =
                    {
                        new CuiTextComponent() { Color = "1 1 1 1", Text = dic.Value, Align = TextAnchor.MiddleCenter, FontSize = 10, Font = "robotocondensed-bold.ttf" },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "28 0", OffsetMax = "100 20" }
                    }
                });
            }

            CuiHelper.AddUi(player, container);
        }
        #endregion GUI

        #region Helpers
        [PluginReference] private readonly Plugin Friends, Clans;
        
        // GrimmNPC integration
        private const ulong CUSTOM_NPC_SKIN_ID = 11162132011012UL;
        private System.Type _grimmNpcType = null;
        private System.Type _customNpcDataType = null;
        private System.Type _raidSettingsType = null;
        private System.Reflection.MethodInfo _registerPendingMethod = null;
        private System.Reflection.MethodInfo _registerNpcMethod = null;
        private System.Reflection.MethodInfo _unregisterNpcMethod = null;
        private bool _grimmNpcAvailable = false;
        
        private bool CheckGrimmNpcAvailable()
        {
            try
            {
                // Step 1: Check if HarmonyLoader exists
                Type harmonyLoaderType = Type.GetType("HarmonyLoader");
                if (harmonyLoaderType == null)
                {
                    // Search all assemblies for HarmonyLoader
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        harmonyLoaderType = assembly.GetType("HarmonyLoader");
                        if (harmonyLoaderType != null) break;
                    }
                }

                if (harmonyLoaderType == null)
                {
                    Puts("HarmonyLoader not found - GrimmNPC integration disabled");
                    return false;
                }

                // Step 2: Search for GrimmNPC type (with and without namespace)
                Assembly grimmNpcAssembly = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        // Try with namespace first
                        var modType = assembly.GetType("GrimmNPC.GrimmNPC");
                        if (modType == null)
                        {
                            // Try without namespace
                            modType = assembly.GetType("GrimmNPC");
                        }

                        if (modType != null)
                        {
                            grimmNpcAssembly = assembly;
                            _grimmNpcType = modType;
                            break;
                        }
                    }
                    catch { }
                }

                if (_grimmNpcType == null)
                {
                    Puts("GrimmNPC type not found - GrimmNPC integration disabled");
                    return false;
                }

                // Step 3: Find CustomNpcData type
                _customNpcDataType = grimmNpcAssembly.GetType("GrimmNPC.CustomNpcData");
                if (_customNpcDataType == null)
                {
                    Puts("GrimmNPC.CustomNpcData type not found - GrimmNPC integration disabled");
                    return false;
                }

                // Step 3a: Find RaidSettings type (optional)
                _raidSettingsType = grimmNpcAssembly.GetType("GrimmNPC.RaidSettings");

                // Step 4: Get static methods and cache them
                _registerPendingMethod = _grimmNpcType.GetMethod("RegisterPending",
                    BindingFlags.Public | BindingFlags.Static);
                _registerNpcMethod = _grimmNpcType.GetMethod("RegisterNpc",
                    BindingFlags.Public | BindingFlags.Static);
                _unregisterNpcMethod = _grimmNpcType.GetMethod("UnregisterNpc",
                    BindingFlags.Public | BindingFlags.Static);

                if (_registerPendingMethod == null || _registerNpcMethod == null || _unregisterNpcMethod == null)
                {
                    Puts("GrimmNPC methods not found - GrimmNPC integration disabled");
                    return false;
                }

                // Step 5: Check if Instance is available
                var instanceProperty = _grimmNpcType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProperty != null)
                {
                    var instance = instanceProperty.GetValue(null);
                    _grimmNpcAvailable = instance != null;
                    if (_grimmNpcAvailable)
                    {
                        Puts("GrimmNPC Harmony mod detected and loaded successfully!");
                    }
                }
            }
            catch (Exception ex)
            {
                PrintError($"Failed to check GrimmNPC availability: {ex.Message}");
            }
            return _grimmNpcAvailable;
        }
        
        private object CreateCustomNpcDataFromConfig(JObject config, Vector3 spawnPosition)
        {
            if (_customNpcDataType == null) return null;
            
            try
            {
                object npcData = Activator.CreateInstance(_customNpcDataType);
                
                // Set properties using reflection
                SetProperty(npcData, "Name", config["Name"]?.ToString() ?? "Custom NPC");
                SetProperty(npcData, "Health", config["Health"]?.ToObject<float>() ?? 100f);
                SetProperty(npcData, "DamageScale", config["DamageScale"]?.ToObject<float>() ?? 1f);
                SetProperty(npcData, "TurretDamageScale", config["TurretDamageScale"]?.ToObject<float>() ?? 1f);
                SetProperty(npcData, "AimConeScale", config["AimConeScale"]?.ToObject<float>() ?? 1f);
                SetProperty(npcData, "RoamRange", config["RoamRange"]?.ToObject<float>() ?? 10f);
                SetProperty(npcData, "ChaseRange", config["ChaseRange"]?.ToObject<float>() ?? 100f);
                SetProperty(npcData, "SenseRange", config["SenseRange"]?.ToObject<float>() ?? 50f);
                SetProperty(npcData, "CanSleep", config["CanSleep"]?.ToObject<bool>() ?? false);
                SetProperty(npcData, "SleepDistance", config["SleepDistance"]?.ToObject<float>() ?? 100f);
                
                // CRITICAL: Always force terrain navmesh values
                // NPCs spawn on terrain and need to navigate to building blocks (player bases) later
                // Even if spawn position is near a monument, we want terrain navmesh because:
                // 1. NPCs will be raiding player bases (building blocks), not monuments
                // 2. Base Navigation (type: Base) works with terrain navmesh settings (AreaMask=1, AgentTypeID=-1372625422)
                // 3. GrimmNPC's runtime navmesh fix (1Hz) has a hard guard: only overrides when monument bounds check is true
                //    Since NPCs spawn on terrain (not in monument bounds), the fix will not override these values
                //    The fix only runs for NPCs with AreaMask=1 and only overrides if bounds check confirms monument location
                SetProperty(npcData, "AreaMask", 1);  // Terrain navmesh area (required for Base Navigation on building blocks)
                SetProperty(npcData, "AgentTypeID", -1372625422);  // Terrain agent type (required for Base Navigation on building blocks)
                
                SetProperty(npcData, "HomePosition", spawnPosition);
                
                // Set combat behavior
                SetProperty(npcData, "StrafeOnlyWhenAttacking", true);

                // Enable raiding behavior (GrimmNPC Raiding API)
                SetProperty(npcData, "IsRaidingNpc", true);
                if (_raidSettingsType != null)
                {
                    object raidSettings = Activator.CreateInstance(_raidSettingsType);
                    SetProperty(raidSettings, "Enable", true);
                    SetProperty(raidSettings, "RequireTargetAuth", true);
                    SetProperty(raidSettings, "DisableAtMonuments", true);
                    SetProperty(raidSettings, "AllowTargetSleeping", false);
                    SetProperty(raidSettings, "AllowTargetOffline", false);
                    SetProperty(raidSettings, "AllowExplosives", true);
                    SetProperty(raidSettings, "AttackRangeMelee", 6f);
                    SetProperty(raidSettings, "AttackRangeRanged", 25f);
                    SetProperty(raidSettings, "FallbackDamageMelee", 20f);
                    SetProperty(raidSettings, "FallbackDamageRanged", 40f);
                    SetProperty(npcData, "RaidSettings", raidSettings);
                }
                
                // Generate unique UserID
                ulong userId = GenerateUniqueUserId();
                SetProperty(npcData, "UserID", userId);
                
                return npcData;
            }
            catch (Exception ex)
            {
                PrintError($"Failed to create CustomNpcData: {ex.Message}");
                return null;
            }
        }
        
        private void SetProperty(object obj, string propertyName, object value)
        {
            if (obj == null) return;
            var property = obj.GetType().GetProperty(propertyName);
            if (property != null && property.CanWrite)
            {
                property.SetValue(obj, value);
            }
        }
        
        private ulong GenerateUniqueUserId()
        {
            // Generate a unique user ID based on timestamp and random
            // Using a range that won't conflict with real Steam IDs
            ulong baseId = 90000000000000000UL; // Start from 90... to avoid Steam ID conflicts
            ulong timestamp = (ulong)(DateTime.UtcNow.Ticks % 1000000000);
            ulong random = (ulong)UnityEngine.Random.Range(1000, 9999);
            return baseId + timestamp + random;
        }
        
        private ScientistNPC SpawnNpcWithGrimmNpc(Vector3 position, JObject config)
        {
            if (!_grimmNpcAvailable || _grimmNpcType == null)
            {
                PrintError("GrimmNPC is not available! Cannot spawn NPC.");
                return null;
            }
            
            try
            {
                // Get prefab from config or use default
                string prefabPath = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab";
                var getConfigMethod = _grimmNpcType.GetMethod("GetConfig", BindingFlags.Public | BindingFlags.Static);
                if (getConfigMethod != null)
                {
                    var npcConfig = getConfigMethod.Invoke(null, null);
                    if (npcConfig != null)
                    {
                        var prefabProperty = npcConfig.GetType().GetProperty("Prefab");
                        if (prefabProperty != null)
                        {
                            var prefabValue = prefabProperty.GetValue(npcConfig);
                            if (prefabValue != null && !string.IsNullOrEmpty(prefabValue.ToString()))
                            {
                                prefabPath = prefabValue.ToString();
                            }
                        }
                    }
                }
                
                // Create NPC entity
                ScientistNPC npc = GameManager.server.CreateEntity(prefabPath, position, Quaternion.identity) as ScientistNPC;
                if (npc == null)
                {
                    PrintWarning($"Failed to create NPC entity from prefab: {prefabPath}");
                    return null;
                }
                
                // Set custom skin ID (required for GrimmNPC identification)
                npc.skinID = CUSTOM_NPC_SKIN_ID;
                
                // Create CustomNpcData from config
                object npcData = CreateCustomNpcDataFromConfig(config, position);
                if (npcData == null)
                {
                    PrintWarning("Failed to create CustomNpcData, NPC will use defaults");
                    npc.Spawn();
                    return npc;
                }
                
                // Register with GrimmNPC BEFORE spawn (recommended)
                if (_registerPendingMethod != null)
                {
                    _registerPendingMethod.Invoke(null, new object[] { npc, npcData });
                }
                
                // Spawn the NPC
                npc.Spawn();
                
                // Register with GrimmNPC AFTER spawn (with netId)
                ulong netId = npc.net?.ID.Value ?? 0;
                if (netId != 0 && _registerNpcMethod != null)
                {
                    _registerNpcMethod.Invoke(null, new object[] { netId, npcData });
                }
                
                // Setup post-spawn configuration (kit management, item equipping, navigator unpause)
                SetupPostSpawnCallbacks(npc, config);
                
                return npc;
            }
            catch (Exception ex)
            {
                PrintError($"Failed to spawn NPC with GrimmNPC: {ex.Message}");
                PrintError($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }
        
        private void SetupPostSpawnCallbacks(ScientistNPC npc, JObject config)
        {
            if (npc == null || config == null) return;
            
            // FALLBACK: Ensure navigator is unpaused and navigation type is determined after spawn
            NextTick(() =>
            {
                if (npc == null || !npc.IsExists()) return;
                
                var brain = npc.GetComponent<ScientistBrain>();
                if (brain?.Navigator != null)
                {
                    try
                    {
                        var navigatorType = brain.Navigator.GetType();
                        
                        // Check if paused and unpause
                        var pausedField = navigatorType.GetField("paused", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (pausedField != null)
                        {
                            bool isPaused = (bool)pausedField.GetValue(brain.Navigator);
                            if (isPaused)
                            {
                                pausedField.SetValue(brain.Navigator, false);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Silently handle - this is a fallback only
                    }
                }
            });
            
            // Equip items after spawn
            NextTick(() =>
            {
                if (npc == null || !npc.IsExists()) return;
                
                // Clear default inventory first
                npc.inventory.containerWear.Clear();
                npc.inventory.containerBelt.Clear();
                npc.inventory.containerMain.Clear();
                
                // Apply kit FIRST if specified (kits will populate inventory)
                bool kitApplied = false;
                string kitName = config["Kit"]?.ToString();
                if (!string.IsNullOrEmpty(kitName) && plugins.Exists("Kits"))
                {
                    object kitResult = Interface.CallHook("GiveKit", npc, kitName);
                    if (kitResult != null && kitResult.ToString().ToLower() != "false")
                    {
                        kitApplied = true;
                    }
                }
                
                // Only manually equip items if no kit was applied (or if kit failed)
                if (!kitApplied)
                {
                    // Equip wear items
                    JArray wearItems = config["WearItems"] as JArray;
                    if (wearItems != null)
                    {
                        foreach (JObject wearItem in wearItems)
                        {
                            string shortName = wearItem["ShortName"]?.ToString();
                            ulong skinId = wearItem["SkinID"]?.ToObject<ulong>() ?? 0;
                            
                            if (string.IsNullOrEmpty(shortName)) continue;
                            
                            Item item = ItemManager.CreateByName(shortName, 1, skinId);
                            if (item != null)
                            {
                                npc.inventory.GiveItem(item, npc.inventory.containerWear);
                            }
                        }
                    }
                    
                    // Equip belt items
                    JArray beltItems = config["BeltItems"] as JArray;
                    if (beltItems != null)
                    {
                        foreach (JObject beltItem in beltItems)
                        {
                            string shortName = beltItem["ShortName"]?.ToString();
                            ulong skinId = beltItem["SkinID"]?.ToObject<ulong>() ?? 0;
                            int amount = beltItem["Amount"]?.ToObject<int>() ?? 1;
                            string ammo = beltItem["Ammo"]?.ToString() ?? string.Empty;
                            JArray mods = beltItem["Mods"] as JArray;
                            
                            if (string.IsNullOrEmpty(shortName)) continue;
                            
                            Item item = ItemManager.CreateByName(shortName, amount, skinId);
                            if (item == null) continue;
                            
                            // Add mods
                            if (mods != null)
                            {
                                foreach (string modShortName in mods)
                                {
                                    if (string.IsNullOrEmpty(modShortName)) continue;
                                    Item mod = ItemManager.CreateByName(modShortName, 1, 0);
                                    if (mod != null && item.contents != null)
                                    {
                                        item.contents.AddItem(mod.info, 1);
                                    }
                                }
                            }
                            
                            // Add ammo
                            if (!string.IsNullOrEmpty(ammo) && item.hasCondition)
                            {
                                Item ammoItem = ItemManager.CreateByName(ammo, 1, 0);
                                if (ammoItem != null && item.contents != null)
                                {
                                    item.contents.AddItem(ammoItem.info, 1);
                                }
                            }
                            
                            // Give item to belt
                            npc.inventory.GiveItem(item, npc.inventory.containerBelt);
                        }
                    }
                }
                
                // Make NPC equip items
                npc.EquipTest();
            });
        }
        
        private void UnregisterNpcFromGrimmNpc(ScientistNPC npc)
        {
            if (!_grimmNpcAvailable || npc == null || npc.net == null)
                return;

            ulong netId = npc.net.ID.Value;
            if (netId == 0)
                return;

            try
            {
                _unregisterNpcMethod?.Invoke(null, new object[] { netId });
            }
            catch (Exception ex)
            {
                PrintError($"UnregisterNpcFromGrimmNpc failed: {ex}");
            }
        }
        
        // Track NPCs that need stuck checking
        private readonly Dictionary<ScientistNPC, ControllerHomeRaid> _npcStuckCheck = new Dictionary<ScientistNPC, ControllerHomeRaid>();
        
        private SpecialWeaponsHandler _specialWeaponsHandler;
        
        // Raid target structure for NPCs - Priority: TC > Doors > Walls > Foundations
        private class NpcRaidTargets
        {
            public BuildingPrivlidge ToolCupboard { get; set; }
            public HashSet<Door> Doors { get; set; } = new HashSet<Door>();
            public HashSet<BuildingBlock> Walls { get; set; } = new HashSet<BuildingBlock>();
            public HashSet<BuildingBlock> Foundations { get; set; } = new HashSet<BuildingBlock>();
            
            // 🏠 RAID INTENT ENFORCEMENT: Store raid mission data
            public BaseCombatEntity PreferredBreachPoint { get; set; } // Nearest door/wall to TC (preferred breach point)
            public string Role { get; set; } // "rocket", "satchel", or "support"
            public BaseEntity TargetBuilding { get; set; } // Primary target building (TC or nearest structure)
        }
        
        // Track raid targets for each NPC (replaces NpcSpawn's Foundations property)
        private readonly Dictionary<ScientistNPC, NpcRaidTargets> _npcRaidTargets = new Dictionary<ScientistNPC, NpcRaidTargets>();
        
        // Track guard targets for NPCs (replaces NpcSpawn's GuardTarget property)
        private readonly Dictionary<ScientistNPC, BaseEntity> _npcGuardTargets = new Dictionary<ScientistNPC, BaseEntity>();

        private enum WeaponIntent
        {
            Combat,
            Raid
        }

        // NOTE: This SpecialWeaponsHandler is ONLY used for combat scenarios (non-raiding)
        // For raiding, GrimmNPC's raiding system handles all special weapon firing via SpecialWeaponsHandler.HandleRaidAttack()
        // This prevents duplicate weapon handling and ensures consistent behavior across combat and raiding
        private class SpecialWeaponsHandler
        {
            private readonly DefendableHomes _plugin;
            private readonly Dictionary<ulong, Coroutine> _activeWeaponCoroutines = new Dictionary<ulong, Coroutine>();
            private readonly Dictionary<ulong, float> _nextRocketFireTime = new Dictionary<ulong, float>();
            private readonly Dictionary<ulong, float> _nextGrenadeFireTime = new Dictionary<ulong, float>();
            private readonly Dictionary<ulong, float> _nextFlameFireTime = new Dictionary<ulong, float>();
            private readonly Dictionary<ulong, float> _nextBowFireTime = new Dictionary<ulong, float>();
            private readonly Dictionary<ulong, int> _grenadeRoundsRemaining = new Dictionary<ulong, int>();

            internal SpecialWeaponsHandler(DefendableHomes plugin)
            {
                _plugin = plugin;
            }

            internal void StopAll()
            {
                foreach (var kvp in _activeWeaponCoroutines)
                {
                    if (kvp.Value != null)
                    {
                        ServerMgr.Instance.StopCoroutine(kvp.Value);
                    }
                }
                _activeWeaponCoroutines.Clear();
            }

            internal float GetPreferredAttackRange(ScientistNPC npc, float defaultRange)
            {
                if (!TryGetSpecialWeapon(npc, out SpecialWeaponType weaponType, out _))
                    return defaultRange;

                switch (weaponType)
                {
                    case SpecialWeaponType.RocketLauncher:
                    case SpecialWeaponType.GrenadeLauncher:
                        return 30f;
                    case SpecialWeaponType.FlameThrower:
                        return 8f;
                    case SpecialWeaponType.CompoundBow:
                        return 25f;
                    default:
                        return defaultRange;
                }
            }

            // NOTE: This method is ONLY called for combat scenarios (WeaponIntent.Combat)
            // Raiding weapon handling (WeaponIntent.Raid) is now handled by GrimmNPC's raiding system
            // GrimmNPC's Raid.TickRaid() calls SpecialWeaponsHandler.HandleRaidAttack() which handles all special weapons
            internal bool TryHandleSpecialWeapon(ScientistNPC npc, BaseEntity target, WeaponIntent intent)
            {
                // Only handle combat scenarios - raiding is handled by GrimmNPC
                if (intent == WeaponIntent.Raid)
                    return false;
                    
                if (!IsValidEntity(npc) || !IsValidEntity(target))
                    return false;

                ulong netId = npc.net?.ID.Value ?? 0;
                if (netId == 0)
                    return false;

                if (_activeWeaponCoroutines.ContainsKey(netId))
                    return true;

                if (!TryGetSpecialWeapon(npc, out SpecialWeaponType weaponType, out Item weaponItem))
                    return false;

                float now = Time.time;
                if (IsOnCooldown(netId, weaponType, now))
                    return true;

                var brain = npc.GetComponent<ScientistBrain>();
                if (!IsTargetInRange(npc, target, weaponType, brain))
                    return false;

                if (!HasLineOfSight(npc, target))
                    return false;

                Coroutine coroutine = StartWeaponCoroutine(weaponType, npc, target, weaponItem, intent);
                if (coroutine == null)
                    return false;

                _activeWeaponCoroutines[netId] = coroutine;
                return true;
            }

            private enum SpecialWeaponType
            {
                RocketLauncher,
                GrenadeLauncher,
                FlameThrower,
                CompoundBow
            }

            private static bool IsValidEntity(BaseEntity entity)
            {
                return entity != null && !entity.IsDestroyed && entity.IsExists();
            }

            private bool TryGetSpecialWeapon(ScientistNPC npc, out SpecialWeaponType weaponType, out Item weaponItem)
            {
                weaponType = default(SpecialWeaponType);
                weaponItem = null;
                if (npc == null || npc.inventory == null || npc.inventory.containerBelt == null)
                    return false;

                Item activeItem = npc.GetActiveItem();
                if (activeItem != null && TryMapSpecialWeapon(activeItem, out weaponType))
                {
                    weaponItem = activeItem;
                    return true;
                }

                foreach (var item in npc.inventory.containerBelt.itemList)
                {
                    if (item == null)
                        continue;
                    if (TryMapSpecialWeapon(item, out weaponType))
                    {
                        weaponItem = item;
                        return true;
                    }
                }

                return false;
            }

            private static bool TryMapSpecialWeapon(Item item, out SpecialWeaponType weaponType)
            {
                weaponType = default(SpecialWeaponType);
                string shortName = item?.info?.shortname?.ToLower() ?? string.Empty;
                if (shortName == "rocket.launcher")
                {
                    weaponType = SpecialWeaponType.RocketLauncher;
                    return true;
                }
                if (shortName == "multiplegrenadelauncher" || shortName == "grenade.launcher")
                {
                    weaponType = SpecialWeaponType.GrenadeLauncher;
                    return true;
                }
                if (shortName == "flamethrower" || shortName == "military flamethrower")
                {
                    weaponType = SpecialWeaponType.FlameThrower;
                    return true;
                }
                if (shortName == "bow.compound")
                {
                    weaponType = SpecialWeaponType.CompoundBow;
                    return true;
                }
                return false;
            }

            private bool IsOnCooldown(ulong netId, SpecialWeaponType weaponType, float now)
            {
                switch (weaponType)
                {
                    case SpecialWeaponType.RocketLauncher:
                        return _nextRocketFireTime.TryGetValue(netId, out float rocketTime) && now < rocketTime;
                    case SpecialWeaponType.GrenadeLauncher:
                        return _nextGrenadeFireTime.TryGetValue(netId, out float grenadeTime) && now < grenadeTime;
                    case SpecialWeaponType.FlameThrower:
                        return _nextFlameFireTime.TryGetValue(netId, out float flameTime) && now < flameTime;
                    case SpecialWeaponType.CompoundBow:
                        return _nextBowFireTime.TryGetValue(netId, out float bowTime) && now < bowTime;
                    default:
                        return false;
                }
            }

            private bool IsTargetInRange(ScientistNPC npc, BaseEntity target, SpecialWeaponType weaponType, ScientistBrain brain)
            {
                float distance = Vector3.Distance(npc.transform.position, target.transform.position);
                float minRange = 0f;
                float maxRange = 0f;

                switch (weaponType)
                {
                    case SpecialWeaponType.RocketLauncher:
                        minRange = 6f;
                        maxRange = 30f;
                        break;
                    case SpecialWeaponType.GrenadeLauncher:
                        minRange = 6f;
                        maxRange = 30f;
                        break;
                    case SpecialWeaponType.FlameThrower:
                        minRange = 0f;
                        maxRange = 8f;
                        break;
                    case SpecialWeaponType.CompoundBow:
                        minRange = 5f;
                        maxRange = 35f;
                        break;
                }

                if (distance < minRange)
                {
                    if (brain?.Navigator != null)
                    {
                        Vector3 directionAway = (npc.transform.position - target.transform.position).normalized;
                        Vector3 retreatPos = npc.transform.position + directionAway * (minRange - distance + 2f);
                        try
                        {
                            brain.Navigator.SetDestination(retreatPos, BaseNavigator.NavigationSpeed.Normal);
                        }
                        catch { }
                    }
                    return true;
                }

                if (distance > maxRange)
                    return false;

                return true;
            }

            private static bool HasLineOfSight(ScientistNPC npc, BaseEntity target)
            {
                if (npc == null || target == null)
                    return false;

                Vector3 npcEyePos = npc.eyes != null ? npc.eyes.position : npc.transform.position;
                Vector3 targetPos = target.CenterPoint();
                int layerMask = 1219701505;
                RaycastHit hit;
                if (Physics.Raycast(npcEyePos, (targetPos - npcEyePos).normalized, out hit, Vector3.Distance(npcEyePos, targetPos), layerMask))
                {
                    return hit.collider.GetComponentInParent<BaseEntity>() == target;
                }
                return true;
            }

            private Coroutine StartWeaponCoroutine(SpecialWeaponType weaponType, ScientistNPC npc, BaseEntity target, Item weaponItem, WeaponIntent intent)
            {
                switch (weaponType)
                {
                    case SpecialWeaponType.RocketLauncher:
                        return ServerMgr.Instance.StartCoroutine(ProcessRocketLauncher(npc, target, weaponItem, intent));
                    case SpecialWeaponType.GrenadeLauncher:
                        return ServerMgr.Instance.StartCoroutine(ProcessGrenadeLauncher(npc, target, weaponItem, intent));
                    case SpecialWeaponType.FlameThrower:
                        return ServerMgr.Instance.StartCoroutine(ProcessFlameThrower(npc, target, weaponItem, intent));
                    case SpecialWeaponType.CompoundBow:
                        return ServerMgr.Instance.StartCoroutine(ProcessCompoundBow(npc, target, weaponItem, intent));
                    default:
                        return null;
                }
            }

            private IEnumerator ProcessRocketLauncher(ScientistNPC npc, BaseEntity target, Item weaponItem, WeaponIntent intent)
            {
                ulong netId = npc.net?.ID.Value ?? 0;
                try
                {
                    if (!EnsureEquipped(npc, weaponItem))
                        yield break;

                    LockMovementAndAim(npc, target, crouch: true);
                    yield return CoroutineEx.waitForSeconds(1.5f);

                    if (!IsValidEntity(npc) || !IsValidEntity(target))
                        yield break;

                    if (!HasLineOfSight(npc, target))
                        yield break;

                    bool fired = FireRocketProjectile(npc, weaponItem);
                    if (!fired)
                        ApplyFallbackDamage(npc, target, DamageType.Explosion, 50f, intent);

                    _nextRocketFireTime[netId] = Time.time + 6f;
                }
                finally
                {
                    UnlockMovement(npc);
                    ClearActive(netId);
                }
            }

            private IEnumerator ProcessGrenadeLauncher(ScientistNPC npc, BaseEntity target, Item weaponItem, WeaponIntent intent)
            {
                ulong netId = npc.net?.ID.Value ?? 0;
                try
                {
                    if (!EnsureEquipped(npc, weaponItem))
                        yield break;

                    LockMovementAndAim(npc, target, crouch: false);
                    yield return CoroutineEx.waitForSeconds(1.0f);

                    if (!IsValidEntity(npc) || !IsValidEntity(target))
                        yield break;

                    if (!HasLineOfSight(npc, target))
                        yield break;

                    int roundsRemaining = GetGrenadeRoundsRemaining(netId, weaponItem);
                    if (roundsRemaining <= 0)
                    {
                        _nextGrenadeFireTime[netId] = Time.time + 8f;
                        ResetGrenadeRounds(netId, weaponItem);
                        yield break;
                    }

                    bool fired = FireGrenadeProjectile(npc, weaponItem);
                    if (!fired)
                        ApplyFallbackDamage(npc, target, DamageType.Explosion, 40f, intent);

                    _grenadeRoundsRemaining[netId] = roundsRemaining - 1;
                    if (_grenadeRoundsRemaining[netId] <= 0)
                    {
                        _nextGrenadeFireTime[netId] = Time.time + 8f;
                        ResetGrenadeRounds(netId, weaponItem);
                    }
                }
                finally
                {
                    UnlockMovement(npc);
                    ClearActive(netId);
                }
            }

            private IEnumerator ProcessFlameThrower(ScientistNPC npc, BaseEntity target, Item weaponItem, WeaponIntent intent)
            {
                ulong netId = npc.net?.ID.Value ?? 0;
                try
                {
                    if (!EnsureEquipped(npc, weaponItem))
                        yield break;

                    AttackEntity attackEntity = npc.GetAttackEntity();
                    FlameThrower flameThrower = attackEntity as FlameThrower;
                    if (flameThrower == null)
                        yield break;

                    if (flameThrower.ammo <= 0)
                    {
                        flameThrower.ServerReload();
                        _nextFlameFireTime[netId] = Time.time + 2f;
                        yield break;
                    }

                    LockMovementAndAim(npc, target, crouch: false);
                    SetFlameState(flameThrower, true);
                    yield return CoroutineEx.waitForSeconds(0.25f);
                    SetFlameState(flameThrower, false);
                    _nextFlameFireTime[netId] = Time.time + 1.5f;
                }
                finally
                {
                    UnlockMovement(npc);
                    ClearActive(netId);
                }
            }

            private IEnumerator ProcessCompoundBow(ScientistNPC npc, BaseEntity target, Item weaponItem, WeaponIntent intent)
            {
                ulong netId = npc.net?.ID.Value ?? 0;
                try
                {
                    if (!EnsureEquipped(npc, weaponItem))
                        yield break;

                    LockMovementAndAim(npc, target, crouch: false);
                    yield return CoroutineEx.waitForSeconds(0.8f);

                    AttackEntity attackEntity = npc.GetAttackEntity();
                    BaseProjectile projectile = attackEntity as BaseProjectile;
                    if (projectile == null)
                        yield break;

                    if (projectile.primaryMagazine != null && projectile.primaryMagazine.contents <= 0)
                    {
                        projectile.ServerReload();
                        yield break;
                    }

                    attackEntity.ServerUse(new HeldEntityServerUseParams(1f));
                    _nextBowFireTime[netId] = Time.time + 1.2f;
                }
                finally
                {
                    UnlockMovement(npc);
                    ClearActive(netId);
                }
            }

            private void ClearActive(ulong netId)
            {
                _activeWeaponCoroutines.Remove(netId);
            }

            private bool EnsureEquipped(ScientistNPC npc, Item weaponItem)
            {
                if (npc == null || weaponItem == null)
                    return false;

                if (npc.GetActiveItem() != weaponItem)
                {
                    npc.UpdateActiveItem(weaponItem.uid);
                    npc.inventory?.UpdatedVisibleHolsteredItems();
                    npc.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                }

                if (_plugin._config != null && _plugin._config.InfiniteAmmo)
                {
                    _plugin.TopUpNpcAmmo(npc);
                }

                return true;
            }

            private void LockMovementAndAim(ScientistNPC npc, BaseEntity target, bool crouch)
            {
                var brain = npc.GetComponent<ScientistBrain>();
                if (brain?.Navigator != null)
                {
                    try
                    {
                        brain.Navigator.SetDestination(npc.transform.position, BaseNavigator.NavigationSpeed.Normal);
                        brain.Navigator.Stop();
                    }
                    catch { }
                }

                Vector3 direction = (target.transform.position - npc.transform.position).normalized;
                _plugin.FaceTargetAndUpdateAim(npc, direction);

                if (crouch && !npc.IsMounted())
                {
                    npc.SetDucked(true);
                }
            }

            private void UnlockMovement(ScientistNPC npc)
            {
                if (npc == null)
                    return;

                npc.SetDucked(false);
            }

            private bool FireRocketProjectile(ScientistNPC npc, Item weaponItem)
            {
                BaseProjectile projectile = weaponItem?.GetHeldEntity() as BaseProjectile;
                ItemDefinition ammoDefinition = projectile?.primaryMagazine?.ammoType;
                if (ammoDefinition == null)
                {
                    string ammoShortName = _plugin._config?.Rocketman?.RocketAmmoShortName;
                    if (string.IsNullOrEmpty(ammoShortName))
                        ammoShortName = "rocket_basic";
                    ammoDefinition = ItemManager.FindItemDefinition(ammoShortName);
                }

                ItemModProjectile mod = ammoDefinition?.GetComponent<ItemModProjectile>();
                string prefabPath = mod?.projectileObject?.resourcePath;
                if (string.IsNullOrEmpty(prefabPath))
                    return false;

                Vector3 origin = npc.eyes != null ? npc.eyes.position : npc.transform.position;
                Vector3 forward = npc.eyes != null ? npc.eyes.BodyForward() : npc.transform.forward;
                Vector3 direction = AimConeUtil.GetModifiedAimConeDirection(2.25f, forward);
                float distance = 1f;
                RaycastHit hit;
                if (Physics.Raycast(origin, direction, out hit, distance, 1236478737))
                    distance = hit.distance - 0.1f;

                BaseEntity rocket = GameManager.server.CreateEntity(prefabPath, origin + direction * distance, Quaternion.LookRotation(direction));
                if (rocket == null)
                    return false;

                ServerProjectile serverProjectile = rocket.GetComponent<ServerProjectile>();
                if (serverProjectile == null)
                    return false;

                rocket.creatorEntity = npc;
                serverProjectile.InitializeVelocity(npc.GetInheritedProjectileVelocity(direction) + direction * serverProjectile.speed * 2f);
                rocket.Spawn();

                if (projectile?.primaryMagazine != null && projectile.primaryMagazine.contents > 0)
                {
                    projectile.primaryMagazine.contents = Mathf.Max(0, projectile.primaryMagazine.contents - 1);
                }

                return true;
            }

            private bool FireGrenadeProjectile(ScientistNPC npc, Item weaponItem)
            {
                BaseProjectile projectile = weaponItem?.GetHeldEntity() as BaseProjectile;
                ItemDefinition ammoDefinition = projectile?.primaryMagazine?.ammoType;
                ItemModProjectile mod = ammoDefinition?.GetComponent<ItemModProjectile>();
                string prefabPath = mod?.projectileObject?.resourcePath;
                if (string.IsNullOrEmpty(prefabPath))
                    return false;

                Vector3 origin = npc.eyes != null ? npc.eyes.position : npc.transform.position;
                Vector3 forward = npc.eyes != null ? npc.eyes.BodyForward() : npc.transform.forward;
                Vector3 direction = AimConeUtil.GetModifiedAimConeDirection(3f, forward);
                float distance = 1f;
                RaycastHit hit;
                if (Physics.Raycast(origin, direction, out hit, distance, 1236478737))
                    distance = hit.distance - 0.1f;

                BaseEntity grenade = GameManager.server.CreateEntity(prefabPath, origin + direction * distance, Quaternion.LookRotation(direction));
                if (grenade == null)
                    return false;

                ServerProjectile serverProjectile = grenade.GetComponent<ServerProjectile>();
                if (serverProjectile == null)
                    return false;

                grenade.creatorEntity = npc;
                serverProjectile.InitializeVelocity(npc.GetInheritedProjectileVelocity(direction) + direction * serverProjectile.speed * 1.6f);
                grenade.Spawn();

                if (projectile?.primaryMagazine != null && projectile.primaryMagazine.contents > 0)
                {
                    projectile.primaryMagazine.contents = Mathf.Max(0, projectile.primaryMagazine.contents - 1);
                }

                return true;
            }

            private int GetGrenadeRoundsRemaining(ulong netId, Item weaponItem)
            {
                if (_grenadeRoundsRemaining.TryGetValue(netId, out int remaining))
                    return remaining;

                BaseProjectile projectile = weaponItem?.GetHeldEntity() as BaseProjectile;
                int capacity = projectile?.primaryMagazine?.capacity ?? 6;
                _grenadeRoundsRemaining[netId] = capacity > 0 ? capacity : 6;
                return _grenadeRoundsRemaining[netId];
            }

            private void ResetGrenadeRounds(ulong netId, Item weaponItem)
            {
                BaseProjectile projectile = weaponItem?.GetHeldEntity() as BaseProjectile;
                int capacity = projectile?.primaryMagazine?.capacity ?? 6;
                _grenadeRoundsRemaining[netId] = capacity > 0 ? capacity : 6;
            }

            private static void SetFlameState(FlameThrower flameThrower, bool enabled)
            {
                if (flameThrower == null)
                    return;

                var method = flameThrower.GetType().GetMethod("SetFlameState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method != null)
                {
                    method.Invoke(flameThrower, new object[] { enabled });
                }
                else
                {
                    flameThrower.ServerUse(new HeldEntityServerUseParams(1f));
                }
            }

            private static void ApplyFallbackDamage(ScientistNPC npc, BaseEntity target, DamageType damageType, float damage, WeaponIntent intent)
            {
                BaseCombatEntity combatEntity = target as BaseCombatEntity;
                if (combatEntity == null)
                    return;
                if (target is BasePlayer)
                    return;

                combatEntity.OnAttacked(damage, damageType, npc, false);
            }
        }
        
        // Set raid targets for an NPC - finds TC, doors, walls, and foundations
        private void SetNpcRaidTargets(ScientistNPC npc, ControllerHomeRaid controller)
        {
            if (npc == null || controller == null) return;
            
            NpcRaidTargets targets = new NpcRaidTargets();
            
            // PRIORITY 1: Find nearest tool cupboard (primary goal)
            BuildingPrivlidge nearestTC = null;
            float nearestTCDist = float.MaxValue;
            foreach (var cupboard in controller.Cupboards)
            {
                if (cupboard == null || !cupboard.IsExists()) continue;
                if (!IsToolCupboard(cupboard)) continue;
                
                float dist = Vector3.Distance(npc.transform.position, cupboard.transform.position);
                if (dist < nearestTCDist)
                {
                    nearestTCDist = dist;
                    nearestTC = cupboard;
                }
            }
            targets.ToolCupboard = nearestTC;
            
            // 🏠 CRITICAL: Even if no TC found initially, try to set minimal raid goal
            // This ensures NPCs still get Raid Goal set and can move toward the base
            if (nearestTC == null)
            {
                // Try one more time to find any TC in controller (fallback)
                foreach (var cupboard in controller.Cupboards)
                {
                    if (cupboard == null || !cupboard.IsExists()) continue;
                    if (!IsToolCupboard(cupboard)) continue;
                    
                    nearestTC = cupboard;
                    targets.ToolCupboard = nearestTC;
                    targets.TargetBuilding = nearestTC;
                    targets.PreferredBreachPoint = nearestTC;
                    break; // Use first valid TC found
                }
                
                // If still no TC, can't set raid targets - but AssignRaidMission will handle this
                if (nearestTC == null) return;
            }
            
            // PRIORITY 2: Find doors that block path to TC
            targets.Doors = FindDoorsBlockingPathToTC(npc, nearestTC, controller);
            
            // PRIORITY 3: Find walls that block path to TC
            targets.Walls = FindWallsBlockingPathToTC(npc, nearestTC, controller);
            
            // PRIORITY 4: Store foundations as fallback
            HashSet<BuildingBlock> validFoundations = new HashSet<BuildingBlock>();
            foreach (var foundation in controller.Foundations)
            {
                if (foundation != null && foundation.IsExists() && IsFoundation(foundation))
                {
                    validFoundations.Add(foundation);
                }
            }
            targets.Foundations = validFoundations;
            
            _npcRaidTargets[npc] = targets;
            
            // 🏠 RAID INTENT ENFORCEMENT: Determine preferred breach point (nearest door/wall to TC)
            if (nearestTC != null)
            {
                BaseCombatEntity preferredBreach = null;
                float nearestBreachDist = float.MaxValue;
                
                // Check doors first (preferred breach point)
                foreach (var door in targets.Doors)
                {
                    if (door == null || !door.IsExists()) continue;
                    float dist = Vector3.Distance(door.transform.position, nearestTC.transform.position);
                    if (dist < nearestBreachDist)
                    {
                        nearestBreachDist = dist;
                        preferredBreach = door;
                    }
                }
                
                // Check walls if no door found
                if (preferredBreach == null)
                {
                    foreach (var wall in targets.Walls)
                    {
                        if (wall == null || !wall.IsExists()) continue;
                        float dist = Vector3.Distance(wall.transform.position, nearestTC.transform.position);
                        if (dist < nearestBreachDist)
                        {
                            nearestBreachDist = dist;
                            preferredBreach = wall;
                        }
                    }
                }
                
                targets.PreferredBreachPoint = preferredBreach;
                targets.TargetBuilding = nearestTC; // Primary target is the TC
            }
            
            // Set TC as initial destination when NPC spawns
            var brain = npc.GetComponent<ScientistBrain>();
            if (brain != null && brain.Navigator != null && nearestTC != null)
            {
                try
                {
                    brain.Navigator.SetDestination(nearestTC.transform.position, BaseNavigator.NavigationSpeed.Normal);
                }
                catch { }
            }
        }
        
        /// <summary>
        /// 🏠 RAID INTENT ENFORCEMENT: Assigns raid mission on spawn.
        /// Stores target building, preferred breach point, role, and targeting priority override.
        /// Sets RaidGoalActive, RaidGoalPosition, and RaidGoalEntityId in npcData.
        /// </summary>
        private void AssignRaidMission(ScientistNPC npc, string npcType, ControllerHomeRaid controller)
        {
            if (npc == null || controller == null) return;
            
            NpcRaidTargets targets = GetNpcRaidTargets(npc);
            
            // 🏠 CRITICAL: Even if SetNpcRaidTargets failed (no TC found), try to set Raid Goal from controller
            // This ensures NPCs still have a raid goal even if TC detection failed
            if (targets == null)
            {
                // Try to find TC directly from controller
                BuildingPrivlidge nearestTC = null;
                float nearestTCDist = float.MaxValue;
                foreach (var cupboard in controller.Cupboards)
                {
                    if (cupboard == null || !cupboard.IsExists()) continue;
                    if (!IsToolCupboard(cupboard)) continue;
                    
                    float dist = Vector3.Distance(npc.transform.position, cupboard.transform.position);
                    if (dist < nearestTCDist)
                    {
                        nearestTCDist = dist;
                        nearestTC = cupboard;
                    }
                }
                
                if (nearestTC != null)
                {
                    // Create minimal raid targets with just TC
                    targets = new NpcRaidTargets
                    {
                        ToolCupboard = nearestTC,
                        TargetBuilding = nearestTC,
                        PreferredBreachPoint = nearestTC,
                        Doors = new HashSet<Door>(),
                        Walls = new HashSet<BuildingBlock>(),
                        Foundations = new HashSet<BuildingBlock>()
                    };
                    _npcRaidTargets[npc] = targets;
                }
                else
                {
                    // No TC found at all - can't set raid goal
                    return;
                }
            }
            
            if (targets == null) return;
            
            // Get npcData from GrimmNPC
            ulong netId = npc.net?.ID.Value ?? 0;
            if (netId == 0) return;
            
            if (!_grimmNpcAvailable || _grimmNpcType == null) return;
            
            try
            {
                // Get GetNpcData method from GrimmNPC
                var getNpcDataMethod = _grimmNpcType.GetMethod("GetNpcData", 
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (getNpcDataMethod == null) return;
                
                object npcData = getNpcDataMethod.Invoke(null, new object[] { netId });
                if (npcData == null) return;
                
                // 🏠 RAID GOAL: Set RaidGoalActive, RaidGoalPosition, and RaidGoalEntityId
                SetProperty(npcData, "RaidGoalActive", true);
                
                // Set RaidGoalPosition to TC position (primary target)
                if (targets.ToolCupboard != null && targets.ToolCupboard.IsExists())
                {
                    SetProperty(npcData, "RaidGoalPosition", targets.ToolCupboard.transform.position);
                    SetProperty(npcData, "RaidGoalEntityId", targets.ToolCupboard.net?.ID.Value ?? 0UL);
                }
                else if (targets.PreferredBreachPoint != null && targets.PreferredBreachPoint.IsExists())
                {
                    // Fallback to preferred breach point if TC not available
                    SetProperty(npcData, "RaidGoalPosition", targets.PreferredBreachPoint.transform.position);
                    SetProperty(npcData, "RaidGoalEntityId", targets.PreferredBreachPoint.net?.ID.Value ?? 0UL);
                }
                
                // Determine role based on NPC type and equipment
                string role = "support"; // Default role
                if (npcType == "Rocketman")
                {
                    role = "rocket";
                }
                else if (npcType == "Bomber")
                {
                    // Check if NPC has satchel or timed explosive
                    if (npc.inventory != null && npc.inventory.containerBelt != null)
                    {
                        var beltItems = npc.inventory.containerBelt.itemList;
                        if (beltItems != null)
                        {
                            foreach (var item in beltItems)
                            {
                                if (item?.info?.shortname?.ToLower() == "explosive.satchel" ||
                                    item?.info?.shortname?.ToLower() == "explosive.timed")
                                {
                                    role = "satchel";
                                    break;
                                }
                            }
                        }
                    }
                }
                else if (npcType == "Sledge")
                {
                    // Sledge uses melee, so support role
                    role = "support";
                }
                
                // Store role in raid targets
                targets.Role = role;
                
                // 🎯 TARGETING PRIORITY OVERRIDE: Rocket NPCs must prefer structures, not players
                // This is now handled by TargetingPatches which checks RaidGoalActive FIRST before player targets
                // The raiding system will handle structure targeting when RaidGoalActive is true
            }
            catch (Exception ex)
            {
                PrintError($"Failed to assign raid mission: {ex.Message}");
            }
        }
        
        // Get raid targets for an NPC
        private NpcRaidTargets GetNpcRaidTargets(ScientistNPC npc)
        {
            if (npc == null || !_npcRaidTargets.ContainsKey(npc))
                return null;
            
            NpcRaidTargets targets = _npcRaidTargets[npc];
            
            // Clean up destroyed targets
            if (targets.ToolCupboard != null && !targets.ToolCupboard.IsExists())
                targets.ToolCupboard = null;
            
            targets.Doors.RemoveWhere(d => d == null || !d.IsExists());
            targets.Walls.RemoveWhere(w => w == null || !w.IsExists());
            targets.Foundations.RemoveWhere(f => f == null || !f.IsExists());
            
            if (targets.ToolCupboard == null && targets.Doors.Count == 0 && 
                targets.Walls.Count == 0 && targets.Foundations.Count == 0)
            {
                _npcRaidTargets.Remove(npc);
                return null;
            }
            
            return targets;
        }
        
        // Set guard target for an NPC (replaces NpcSpawn's AddTargetGuard)
        private void SetNpcGuardTarget(ScientistNPC npc, BaseEntity target)
        {
            if (npc == null || target == null || !target.IsExists()) return;
            
            _npcGuardTargets[npc] = target;
        }
        
        // Get guard target for an NPC
        private BaseEntity GetNpcGuardTarget(ScientistNPC npc)
        {
            if (npc == null || !_npcGuardTargets.ContainsKey(npc))
                return null;
            
            BaseEntity target = _npcGuardTargets[npc];
            if (target == null || !target.IsExists())
            {
                _npcGuardTargets.Remove(npc);
                return null;
            }
            
            return target;
        }
        
        // Clear raid targets and guard targets when NPC dies
        private void ClearNpcRaidData(ScientistNPC npc)
        {
            if (npc == null) return;
            
            _npcRaidTargets.Remove(npc);
            _npcGuardTargets.Remove(npc);
            _npcStuckCheck.Remove(npc);
        }
        
        // Start periodic check for NPCs - shoot at players with LOS, pathfind to tool cupboard if no LOS
        private void StartStuckNpcCheck(ScientistNPC npc, ControllerHomeRaid controller)
        {
            if (npc == null || controller == null) return;
            
            _npcStuckCheck[npc] = controller;
            
            // Check every 2 seconds: if NPC has LOS to player, let them shoot; if no LOS, pathfind to TC or door raid
            timer.Repeat(2f, 0, () =>
            {
                if (npc == null || !npc.IsExists() || !_npcStuckCheck.ContainsKey(npc))
                {
                    if (_npcStuckCheck.ContainsKey(npc))
                        _npcStuckCheck.Remove(npc);
                    return;
                }
                
                CheckAndFixStuckNpc(npc, controller);
                
                // If NPC has raiding equipment, check if they're near a door and should raid it
                if (HasRaidingEquipment(npc))
                {
                    CheckDoorRaid(npc, controller);
                }
            });
            
            // Attack building blocks every 0.5 seconds when in range
            timer.Repeat(0.5f, 0, () =>
            {
                if (npc == null || !npc.IsExists() || !_npcStuckCheck.ContainsKey(npc))
                {
                    return;
                }
                
                AttackRaidTargets(npc, controller);
            });
        }
        
        // Check if NPC is near a foundation and should attack it, or near a door and should raid it
        private void CheckDoorRaid(ScientistNPC npc, ControllerHomeRaid controller)
        {
            if (npc == null || !npc.IsExists() || controller == null) return;
            
            var brain = npc.GetComponent<ScientistBrain>();
            if (brain == null || brain.Navigator == null) return;
            
            // Get raid targets with priority: TC > Doors > Walls > Foundations
            NpcRaidTargets targets = GetNpcRaidTargets(npc);
            if (targets == null) return;
            
            // NOTE: GetPreferredAttackRange is only used for range checking here, not weapon firing
            // Actual weapon firing (rockets, grenades, etc.) is handled by GrimmNPC's raiding system
            float attackRange = _specialWeaponsHandler.GetPreferredAttackRange(npc, 6f);
            
            // PRIORITY 1: Check doors
            if (targets.Doors != null && targets.Doors.Count > 0)
            {
                Door nearestDoor = null;
                float nearestDoorDist = float.MaxValue;
                
                foreach (var door in targets.Doors)
                {
                    if (door == null || !door.IsExists() || door.IsOpen()) continue;
                    
                    float dist = Vector3.Distance(npc.transform.position, door.transform.position);
                    if (dist < nearestDoorDist)
                    {
                        nearestDoorDist = dist;
                        nearestDoor = door;
                    }
                }
                
                if (nearestDoor != null)
                {
                    HandleRaidTarget(npc, brain, nearestDoor, nearestDoorDist, attackRange);
                    return; // Found a door to raid
                }
            }
            
            // PRIORITY 2: Check walls
            if (targets.Walls != null && targets.Walls.Count > 0)
            {
                BuildingBlock nearestWall = null;
                float nearestWallDist = float.MaxValue;
                
                foreach (var wall in targets.Walls)
                {
                    if (wall == null || !wall.IsExists()) continue;
                    
                    float dist = Vector3.Distance(npc.transform.position, wall.transform.position);
                    if (dist < nearestWallDist)
                    {
                        nearestWallDist = dist;
                        nearestWall = wall;
                    }
                }
                
                if (nearestWall != null)
                {
                    HandleRaidTarget(npc, brain, nearestWall, nearestWallDist, attackRange);
                    return; // Found a wall to raid
                }
            }
            
            // PRIORITY 3: Check foundations (fallback)
            if (targets.Foundations != null && targets.Foundations.Count > 0)
            {
                BuildingBlock nearestFoundation = null;
                float nearestFoundationDist = float.MaxValue;
                
                foreach (var foundation in targets.Foundations)
                {
                    if (foundation == null || !foundation.IsExists()) continue;
                    
                    float dist = Vector3.Distance(npc.transform.position, foundation.transform.position);
                    if (dist < nearestFoundationDist)
                    {
                        nearestFoundationDist = dist;
                        nearestFoundation = foundation;
                    }
                }
                
                if (nearestFoundation != null)
                {
                    HandleRaidTarget(npc, brain, nearestFoundation, nearestFoundationDist, attackRange);
                    return; // Found a foundation to raid
                }
            }
            
            // No targets in range - continue pathfinding to TC
            if (targets.ToolCupboard != null && targets.ToolCupboard.IsExists())
            {
                try
                {
                    brain.Navigator.SetDestination(targets.ToolCupboard.transform.position, BaseNavigator.NavigationSpeed.Normal);
                }
                catch { }
                return;
            }
            
            // FALLBACK: Old door finding logic if no targets set
            // Find nearest tool cupboard to determine which doors are on the path
            BuildingPrivlidge nearestCupboard = null;
            float nearestDist = float.MaxValue;
            
            foreach (var cupboard in controller.Cupboards)
            {
                if (cupboard == null || !cupboard.IsExists()) continue;
                
                float dist = Vector3.Distance(npc.transform.position, cupboard.transform.position);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearestCupboard = cupboard;
                }
            }
            
            if (nearestCupboard == null) return;
            
            // Find door on path to TC (prioritize doors that block path)
            Door targetDoor = FindNearestDoorToTC(npc, nearestCupboard, 10f);
            
            // If no door found on path, check for any nearby locked/closed door
            if (targetDoor == null)
            {
                List<Door> nearbyDoors = Pool.Get<List<Door>>();
                try
                {
                    Vis.Entities(npc.transform.position, 5f, nearbyDoors, 2097152); // Building layer
                    
                    foreach (var door in nearbyDoors)
                    {
                        if (door == null || !door.IsExists()) continue;
                        if (door.IsOpen()) continue; // Skip open doors
                        
                        bool isLocked = door.IsLocked();
                        bool isClosed = !door.IsOpen();
                        
                        // Prioritize locked doors or security doors
                        if (isLocked || (isClosed && door.isSecurityDoor))
                        {
                            float dist = Vector3.Distance(npc.transform.position, door.transform.position);
                            if (dist < 5f)
                            {
                                targetDoor = door;
                                break; // Found a valid door to raid
                            }
                        }
                    }
                }
                finally
                {
                    Pool.FreeUnmanaged(ref nearbyDoors);
                }
            }
            
            // Old fallback logic - should not be reached if SetNpcRaidTargets worked correctly
            if (targetDoor != null && targetDoor.IsExists())
            {
                float distToDoor = Vector3.Distance(npc.transform.position, targetDoor.transform.position);
                HandleRaidTarget(npc, brain, targetDoor, distToDoor, attackRange);
            }
        }
        
        // Use explosive on target (foundation, door, etc.)
        private void UseExplosiveOnTarget(ScientistNPC npc, BaseCombatEntity target)
        {
            if (npc == null || target == null || !target.IsExists() || npc.inventory == null) return;
            
            // Check for satchels first (melee explosive)
            Item satchel = npc.inventory.containerBelt?.itemList?.FirstOrDefault(x => 
                x != null && x.info?.shortname?.ToLower() == "explosive.satchel");
            
            if (satchel != null)
            {
                // Equip satchel and throw it
                npc.UpdateActiveItem(satchel.uid);
                ThrownWeapon satchelWeapon = npc.GetActiveItem()?.GetHeldEntity() as ThrownWeapon;
                if (satchelWeapon != null)
                {
                    satchelWeapon.ServerThrow(target.transform.position);
                    return;
                }
            }
            
            // Check for C4 (timed explosive)
            Item c4 = npc.inventory.containerBelt?.itemList?.FirstOrDefault(x => 
                x != null && x.info?.shortname?.ToLower() == "explosive.timed");
            
            if (c4 != null)
            {
                // Equip C4 and throw it
                npc.UpdateActiveItem(c4.uid);
                ThrownWeapon c4Weapon = npc.GetActiveItem()?.GetHeldEntity() as ThrownWeapon;
                if (c4Weapon != null)
                {
                    c4Weapon.ServerThrow(target.transform.position);
                    return;
                }
            }
            
            // For ranged weapons (rocket launcher, grenade launcher), they should already be equipped
            // The NPC will shoot at the target automatically if they have LOS
        }

        private void FaceTargetAndUpdateAim(ScientistNPC npc, Vector3 direction)
        {
            if (npc == null || direction == Vector3.zero) return;

            direction.Normalize();
            npc.viewAngles = Quaternion.LookRotation(direction).eulerAngles;
            npc.SetAimDirection(direction);
            npc.serverInput.current.aimAngles = npc.viewAngles;
        }
        
        // Handle raiding a specific target (door, wall, or foundation)
        private void HandleRaidTarget(ScientistNPC npc, ScientistBrain brain, BaseCombatEntity target, float distance, float attackRange)
        {
            if (npc == null || brain == null || target == null || !target.IsExists()) return;
            
            if (distance <= attackRange)
            {
                // In attack range - stop and face target (attack handled by SpecialWeaponsHandler)
                try
                {
                    brain.Navigator.SetDestination(npc.transform.position, BaseNavigator.NavigationSpeed.Normal);
                    brain.Navigator.Stop();
                    
                    Vector3 direction = (target.transform.position - npc.transform.position).normalized;
                    FaceTargetAndUpdateAim(npc, direction);
                }
                catch { }
            }
            else
            {
                // Move closer to target
                try
                {
                    brain.Navigator.SetDestination(target.transform.position, BaseNavigator.NavigationSpeed.Normal);
                }
                catch { }
            }
        }
        
        // Attack raid targets (doors, walls, foundations) when in range
        private void AttackRaidTargets(ScientistNPC npc, ControllerHomeRaid controller)
        {
            if (npc == null || !npc.IsExists() || controller == null) return;
            
            var brain = npc.GetComponent<ScientistBrain>();
            if (brain == null) return;
            
            // 🏠 CRITICAL: If NPC is a raiding NPC with RaidGoalActive, let GrimmNPC handle all weapon firing
            // GrimmNPC's raiding system (Raid.TickRaid) calls SpecialWeaponsHandler.HandleRaidAttack()
            // which handles satchel cycles, rockets, and all special weapons properly
            if (_grimmNpcAvailable && _grimmNpcType != null)
            {
                try
                {
                    ulong netId = npc.net?.ID.Value ?? 0;
                    if (netId != 0)
                    {
                        var getNpcDataMethod = _grimmNpcType.GetMethod("GetNpcData", 
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (getNpcDataMethod != null)
                        {
                            object npcData = getNpcDataMethod.Invoke(null, new object[] { netId });
                            if (npcData != null)
                            {
                                // Check if IsRaidingNpc and RaidGoalActive
                                var isRaidingNpcProp = _customNpcDataType?.GetProperty("IsRaidingNpc");
                                var raidGoalActiveProp = _customNpcDataType?.GetProperty("RaidGoalActive");
                                
                                if (isRaidingNpcProp != null && raidGoalActiveProp != null)
                                {
                                    bool isRaidingNpc = (bool)(isRaidingNpcProp.GetValue(npcData) ?? false);
                                    bool raidGoalActive = (bool)(raidGoalActiveProp.GetValue(npcData) ?? false);
                                    
                                    // If raiding NPC with active raid goal, let GrimmNPC handle all weapon firing
                                    if (isRaidingNpc && raidGoalActive)
                                    {
                                        return; // GrimmNPC's raiding system handles this
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }
            
            // Get raid targets with priority: TC > Doors > Walls > Foundations
            NpcRaidTargets targets = GetNpcRaidTargets(npc);
            if (targets == null) return;
            
            // NOTE: GetPreferredAttackRange is only used for range checking here, not weapon firing
            // Actual weapon firing (rockets, grenades, etc.) is handled by GrimmNPC's raiding system
            float attackRange = _specialWeaponsHandler.GetPreferredAttackRange(npc, 6f);
            
            BaseCombatEntity attackTarget = null;
            float targetDistance = float.MaxValue;
            
            // PRIORITY 1: Check doors
            if (targets.Doors != null && targets.Doors.Count > 0)
            {
                foreach (var door in targets.Doors)
                {
                    if (door == null || !door.IsExists() || door.IsOpen()) continue;
                    
                    float dist = Vector3.Distance(npc.transform.position, door.transform.position);
                    if (dist <= attackRange && dist < targetDistance)
                    {
                        targetDistance = dist;
                        attackTarget = door;
                    }
                }
            }
            
            // PRIORITY 2: Check walls (if no door in range)
            if (attackTarget == null && targets.Walls != null && targets.Walls.Count > 0)
            {
                foreach (var wall in targets.Walls)
                {
                    if (wall == null || !wall.IsExists()) continue;
                    
                    float dist = Vector3.Distance(npc.transform.position, wall.transform.position);
                    if (dist <= attackRange && dist < targetDistance)
                    {
                        targetDistance = dist;
                        attackTarget = wall;
                    }
                }
            }
            
            // PRIORITY 3: Check foundations (if no door or wall in range)
            if (attackTarget == null && targets.Foundations != null && targets.Foundations.Count > 0)
            {
                foreach (var foundation in targets.Foundations)
                {
                    if (foundation == null || !foundation.IsExists()) continue;
                    
                    float dist = Vector3.Distance(npc.transform.position, foundation.transform.position);
                    if (dist <= attackRange && dist < targetDistance)
                    {
                        targetDistance = dist;
                        attackTarget = foundation;
                    }
                }
            }
            
            // Attack the target if found
            if (attackTarget != null && attackTarget.IsExists())
            {
                // NOTE: Special weapon handling (rockets, grenades, flamethrowers) is now handled by GrimmNPC's raiding system
                // GrimmNPC's Raid.TickRaid() calls SpecialWeaponsHandler.HandleRaidAttack() which handles all special weapons
                // This plugin's SpecialWeaponsHandler is only used for combat scenarios (non-raiding)
                
                // Face the target and update aim for projectile weapons
                Vector3 direction = (attackTarget.transform.position - npc.transform.position).normalized;
                FaceTargetAndUpdateAim(npc, direction);
                
                // Check if NPC has melee weapon
                AttackEntity attackEntity = npc.GetAttackEntity();
                if (attackEntity != null && attackEntity is BaseMelee)
                {
                    // Use melee attack
                    if (npc.MeleeAttack())
                    {
                        return;
                    }
                }
                
                // Check for explosive items (satchels, C4)
                Item satchel = npc.inventory?.containerBelt?.itemList?.FirstOrDefault(x => 
                    x != null && (x.info?.shortname?.ToLower() == "explosive.satchel" || 
                                 x.info?.shortname?.ToLower() == "explosive.timed"));
                
                if (satchel != null)
                {
                    // Use explosive on target
                    UseExplosiveOnTarget(npc, attackTarget);
                }
                else
                {
                    // Fallback: Direct melee damage
                    float damage = 25f; // Base melee damage
                    attackTarget.OnAttacked(damage, DamageType.Generic, npc, false);
                }
            }
        }
        
        // Check if NPC has raiding equipment (timed explosives, satchels, rocket launchers, grenade launchers, explosive ammo)
        private bool HasRaidingEquipment(ScientistNPC npc)
        {
            if (npc == null || npc.inventory == null) return false;
            
            // Check belt items
            if (npc.inventory.containerBelt != null)
            {
                foreach (var item in npc.inventory.containerBelt.itemList)
                {
                    if (item == null) continue;
                    
                    string shortName = item.info?.shortname?.ToLower() ?? string.Empty;
                    
                    // Check for raiding equipment
                    if (shortName == "explosive.timed" || 
                        shortName == "explosive.satchel" ||
                        shortName == "rocket.launcher" ||
                        shortName == "grenade.launcher" ||
                        shortName.Contains("rocket") ||
                        shortName.Contains("grenade.launcher"))
                    {
                        return true;
                    }
                    
                    // Check for explosive ammo in weapon
                    if (item.contents != null)
                    {
                        foreach (var ammoItem in item.contents.itemList)
                        {
                            if (ammoItem == null) continue;
                            string ammoName = ammoItem.info?.shortname?.ToLower() ?? string.Empty;
                            if (ammoName.Contains("explosive") || 
                                ammoName.Contains("rocket") ||
                                ammoName == "40mm_grenade_he" ||
                                ammoName.Contains("40mm"))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            
            // Check active item
            Item activeItem = npc.GetActiveItem();
            if (activeItem != null)
            {
                string activeShortName = activeItem.info?.shortname?.ToLower() ?? string.Empty;
                if (activeShortName == "explosive.timed" || 
                    activeShortName == "explosive.satchel" ||
                    activeShortName == "rocket.launcher" ||
                    activeShortName == "grenade.launcher" ||
                    activeShortName.Contains("rocket") ||
                    activeShortName.Contains("grenade.launcher"))
                {
                    return true;
                }
                
                // Check ammo in active weapon
                if (activeItem.contents != null)
                {
                    foreach (var ammoItem in activeItem.contents.itemList)
                    {
                        if (ammoItem == null) continue;
                        string ammoName = ammoItem.info?.shortname?.ToLower() ?? string.Empty;
                        if (ammoName.Contains("explosive") || 
                            ammoName.Contains("rocket") ||
                            ammoName == "40mm_grenade_he" ||
                            ammoName.Contains("40mm"))
                        {
                            return true;
                        }
                    }
                }
            }
            
            return false;
        }
        
        // Check if NPC has ranged explosive weapon (rocket launcher, grenade launcher) that should maintain distance
        private bool HasRangedExplosiveWeapon(ScientistNPC npc)
        {
            if (npc == null || npc.inventory == null) return false;
            
            // Check active item first
            Item activeItem = npc.GetActiveItem();
            if (activeItem != null)
            {
                string activeShortName = activeItem.info?.shortname?.ToLower() ?? string.Empty;
                if (activeShortName == "rocket.launcher" || 
                    activeShortName == "grenade.launcher" ||
                    activeShortName.Contains("rocket.launcher") ||
                    activeShortName.Contains("grenade.launcher"))
                {
                    return true;
                }
            }
            
            // Check belt items
            if (npc.inventory.containerBelt != null)
            {
                foreach (var item in npc.inventory.containerBelt.itemList)
                {
                    if (item == null) continue;
                    
                    string shortName = item.info?.shortname?.ToLower() ?? string.Empty;
                    if (shortName == "rocket.launcher" || 
                        shortName == "grenade.launcher" ||
                        shortName.Contains("rocket.launcher") ||
                        shortName.Contains("grenade.launcher"))
                    {
                        return true;
                    }
                }
            }
            
            return false;
        }

        private void EnsureRaidWeaponEquipped(ScientistNPC npc, bool preferRanged)
        {
            if (npc == null) return;

            bool equippedRanged = preferRanged && TryEquipRangedExplosiveWeapon(npc);
            if (!equippedRanged)
                TryEquipThrownExplosiveWeapon(npc);

            TopUpNpcAmmo(npc);
        }

        private bool TryEquipRangedExplosiveWeapon(ScientistNPC npc)
        {
            if (npc == null || npc.inventory == null || npc.inventory.containerBelt == null)
                return false;

            Item ranged = npc.inventory.containerBelt.itemList?.FirstOrDefault(item =>
            {
                if (item == null || item.info == null) return false;
                string shortName = item.info.shortname?.ToLower() ?? string.Empty;
                return shortName == "rocket.launcher" ||
                       shortName == "grenade.launcher" ||
                       shortName.Contains("rocket.launcher") ||
                       shortName.Contains("grenade.launcher");
            });

            if (ranged == null)
                return false;

            if (npc.GetActiveItem() != ranged)
                npc.UpdateActiveItem(ranged.uid);

            BaseProjectile projectile = npc.GetHeldEntity() as BaseProjectile;
            if (projectile != null && projectile.primaryMagazine != null && projectile.primaryMagazine.contents <= 0)
                projectile.ServerReload();

            return true;
        }

        private bool TryEquipThrownExplosiveWeapon(ScientistNPC npc)
        {
            if (npc == null || npc.inventory == null || npc.inventory.containerBelt == null)
                return false;

            Item explosive = npc.inventory.containerBelt.itemList?.FirstOrDefault(item =>
                item != null && (item.info?.shortname == "explosive.timed" || item.info?.shortname == "explosive.satchel"));

            if (explosive == null)
                return false;

            if (npc.GetActiveItem() != explosive)
                npc.UpdateActiveItem(explosive.uid);

            return true;
        }

        private void TopUpNpcAmmo(ScientistNPC npc)
        {
            if (npc == null || _config == null || !_config.InfiniteAmmo)
                return;

            BaseProjectile projectile = npc.GetHeldEntity() as BaseProjectile;
            if (projectile == null || projectile.primaryMagazine == null)
                return;

            Item activeItem = npc.GetActiveItem();
            if (activeItem != null && activeItem.info != null && activeItem.info.shortname == "rocket.launcher")
            {
                string ammoShortName = _config.Rocketman?.RocketAmmoShortName;
                if (!string.IsNullOrEmpty(ammoShortName))
                {
                    ItemDefinition ammoDefinition = ItemManager.FindItemDefinition(ammoShortName);
                    if (ammoDefinition != null)
                        projectile.primaryMagazine.ammoType = ammoDefinition;
                }
            }

            if (projectile.primaryMagazine.capacity > 0)
                projectile.primaryMagazine.contents = projectile.primaryMagazine.capacity;
        }
        
        // Find nearest door on path to tool cupboard
        private Door FindNearestDoorToTC(ScientistNPC npc, BuildingPrivlidge tc, float searchRadius = 20f)
        {
            if (npc == null || tc == null) return null;
            
            Vector3 npcPos = npc.transform.position;
            Vector3 tcPos = tc.transform.position;
            Vector3 directionToTC = (tcPos - npcPos).normalized;
            
            // Search for doors in direction of TC
            List<Door> doors = Pool.Get<List<Door>>();
            try
            {
                Vis.Entities(npcPos, searchRadius, doors, 2097152); // Building layer
                
                Door nearestDoor = null;
                float nearestDist = float.MaxValue;
                
                foreach (var door in doors)
                {
                    if (door == null || !door.IsExists()) continue;
                    
                    Vector3 doorPos = door.transform.position;
                    float distToDoor = Vector3.Distance(npcPos, doorPos);
                    float distToTC = Vector3.Distance(doorPos, tcPos);
                    
                    // Door should be between NPC and TC, and closer to TC than NPC is
                    Vector3 toDoor = (doorPos - npcPos).normalized;
                    float dot = Vector3.Dot(directionToTC, toDoor);
                    
                    // Door is in direction of TC (dot > 0.3) and closer to TC
                    if (dot > 0.3f && distToTC < Vector3.Distance(npcPos, tcPos) && distToDoor < nearestDist)
                    {
                        nearestDoor = door;
                        nearestDist = distToDoor;
                    }
                }
                
                return nearestDoor;
            }
            finally
            {
                Pool.FreeUnmanaged(ref doors);
            }
        }
        
        private void CheckAndFixStuckNpc(ScientistNPC npc, ControllerHomeRaid controller)
        {
            if (npc == null || !npc.IsExists() || controller == null) return;
            
            var brain = npc.GetComponent<ScientistBrain>();
            if (brain == null || brain.Navigator == null) return;

            TopUpNpcAmmo(npc);
            
            // Check if NPC has raiding equipment
            bool hasRaidingEquipment = HasRaidingEquipment(npc);
            
            // Check if NPC has a player target and LOS
            BasePlayer targetPlayer = null;
            bool hasLOS = false;
            
            if (brain.Events?.Memory?.Entity != null)
            {
                var memorySlot = brain.Events.CurrentInputMemorySlot;
                if (memorySlot >= 0)
                {
                    var target = brain.Events.Memory.Entity.Get(memorySlot);
                    if (target is BasePlayer player && player.IsPlayer())
                    {
                        targetPlayer = player;
                        
                        // Check if NPC has line of sight to player
                        if (brain.Senses != null && brain.Senses.Memory != null)
                        {
                            hasLOS = brain.Senses.Memory.IsLOS(targetPlayer);
                        }
                        
                        // Also do a direct LOS check using raycast
                        if (!hasLOS)
                        {
                            Vector3 npcEyePos = npc.eyes.position;
                            Vector3 playerEyePos = targetPlayer.eyes.position;
                            RaycastHit hit;
                            int layerMask = 1219701505; // Default layer mask for LOS checks
                            
                            if (Physics.Raycast(npcEyePos, (playerEyePos - npcEyePos).normalized, out hit, Vector3.Distance(npcEyePos, playerEyePos), layerMask))
                            {
                                hasLOS = hit.collider.GetComponentInParent<BasePlayer>() == targetPlayer;
                            }
                            else
                            {
                                hasLOS = true; // No obstacles = has LOS
                            }
                        }
                    }
                }
            }
            
            bool handledSpecialCombat = targetPlayer != null && hasLOS && _specialWeaponsHandler.TryHandleSpecialWeapon(npc, targetPlayer, WeaponIntent.Combat);
            if (handledSpecialCombat)
            {
                return;
            }
            
            // NPCs WITHOUT raiding equipment: ONLY focus on finding players
            if (!hasRaidingEquipment)
            {
                // If NPC has LOS to player, let them attack
                if (targetPlayer != null && hasLOS)
                {
                    return; // Let them attack player
                }
                
                // If no LOS but has player in memory, try to pathfind to last known location
                if (targetPlayer != null)
                {
                    // Pathfind to player's last known location
                    try
                    {
                        brain.Navigator.SetDestination(targetPlayer.transform.position, BaseNavigator.NavigationSpeed.Normal);
                    }
                    catch { }
                    return;
                }
                
                // No player target - let GrimmNPC handle roaming
                return;
            }
            
            // NPCs WITH raiding equipment: Priority TC > Doors > Walls > Foundations
            // If NPC has LOS to player, let them shoot first
            if (targetPlayer != null && hasLOS)
            {
                return; // Let them shoot
            }
            
            // Get raid targets with priority: TC > Doors > Walls > Foundations
            NpcRaidTargets targets = GetNpcRaidTargets(npc);
            if (targets == null)
            {
                // No raid targets set - try to find TC and set targets
                BuildingPrivlidge nearestTC = null;
                float nearestDist = float.MaxValue;
                
                foreach (var cupboard in controller.Cupboards)
                {
                    if (cupboard == null || !cupboard.IsExists()) continue;
                    if (!IsToolCupboard(cupboard)) continue;
                    
                    float dist = Vector3.Distance(npc.transform.position, cupboard.transform.position);
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearestTC = cupboard;
                    }
                }
                
                if (nearestTC != null)
                {
                    // Set raid targets now
                    SetNpcRaidTargets(npc, controller);
                    targets = GetNpcRaidTargets(npc);
                }
            }
            
            if (targets != null && targets.ToolCupboard != null && targets.ToolCupboard.IsExists())
            {
                // Pathfind to TC (NPCs know where TC is - they're AI)
                try
                {
                    brain.Navigator.SetDestination(targets.ToolCupboard.transform.position, BaseNavigator.NavigationSpeed.Normal);
                }
                catch { }
            }
            
            // Raiding logic is handled by CheckDoorRaid which uses the priority system:
            // TC > Doors > Walls > Foundations
            // The NPC will pathfind to TC and CheckDoorRaid will handle door/wall/foundation raiding
        }

        private HashSet<string> HooksInsidePlugin { get; } = new HashSet<string>
        {
            "OnEntityTakeDamage",
            "CanBuild",
            "OnPlayerDeath",
            "OnEntityDeath",
            "OnEntityKill",
            "CanLootEntity",
            "OnCorpsePopulate",
            "OnEntitySpawned",
            "CanPopulateLoot",
            "OnCustomLootContainer",
            "OnContainerPopulate",
            "CanCh47SpawnNpc",
            "OnBaseRepair",
            "CanEntityBeTargeted",
            "OnPlayerTeleported",
            "OnCustomNpcGuardTargetEnd",
            "OnCustomNpcTarget",
            "OnBomberExplosion"
        };

        private void ToggleHooks(bool subscribe)
        {
            foreach (string hook in HooksInsidePlugin)
            {
                if (subscribe) Subscribe(hook);
                else Unsubscribe(hook);
            }
        }

        private static Item GetFlare(FlareConfig config)
        {
            Item item = ItemManager.CreateByName("flare", 1, config.SkinId);
            item.name = config.Name;
            return item;
        }

        private static void ReturnFlare(BasePlayer player, BaseEntity entity, FlareConfig config)
        {
            if (entity.IsExists()) entity.Kill();
            Item item = GetFlare(config);
            int slots = player.inventory.containerMain.capacity + player.inventory.containerBelt.capacity;
            int taken = player.inventory.containerMain.itemList.Count + player.inventory.containerBelt.itemList.Count;
            if (slots - taken > 0) player.inventory.GiveItem(item);
            else item.Drop(player.transform.position, Vector3.up);
        }

        private bool IsTeam(ulong playerId, ulong targetId)
        {
            if (playerId == 0 || targetId == 0) return false;
            if (playerId == targetId) return true;
            RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance.FindPlayersTeam(playerId);
            if (playerTeam != null && playerTeam.members.Contains(targetId)) return true;
            if (plugins.Exists("Friends") && (bool)Friends.Call("AreFriends", playerId, targetId)) return true;
            if (plugins.Exists("Clans") && Clans.Author == "k1lly0u" && (bool)Clans.Call("IsMemberOrAlly", playerId.ToString(), targetId.ToString())) return true;
            return false;
        }

        private const string StrSec = En ? "sec." : "сек.";
        private const string StrMin = En ? "min." : "мин.";
        private const string StrH = En ? "h." : "ч.";

        private static string GetTimeFormat(int time)
        {
            if (time <= 60) return $"{time} {StrSec}";
            else if (time <= 3600)
            {
                int sec = time % 60;
                int min = (time - sec) / 60;
                return sec == 0 ? $"{min} {StrMin}" : $"{min} {StrMin} {sec} {StrSec}";
            }
            else
            {
                int minSec = time % 3600;
                int hour = (time - minSec) / 3600;
                int sec = minSec % 60;
                int min = (minSec - sec) / 60;
                if (min == 0 && sec == 0) return $"{hour} {StrH}";
                else if (sec == 0) return $"{hour} {StrH} {min} {StrMin}";
                else return $"{hour} {StrH} {min} {StrMin} {sec} {StrSec}";
            }
        }

        private void CheckVersionPlugin()
        {
            webrequest.Enqueue("http://37.153.157.216:5000/Api/GetPluginVersions?pluginName=DefendableHomes", null, (code, response) =>
            {
                if (code != 200 || string.IsNullOrEmpty(response)) return;
                string[] array = response.Replace("\"", string.Empty).Split('.');
                VersionNumber latestVersion = new VersionNumber(Convert.ToInt32(array[0]), Convert.ToInt32(array[1]), Convert.ToInt32(array[2]));
                if (Version < latestVersion) PrintWarning($"A new version ({latestVersion}) of the plugin is available! You need to update the plugin:\n- https://lone.design/product/defendable-homes-rust-plugin\n- https://codefling.com/plugins/defendable-homes");
            }, this);
        }
        #endregion Helpers

        #region API
        private bool IsCupboardDefendableHomes(BuildingPrivlidge cupboard)
        {
            if (cupboard == null || Controllers.Count == 0) return false;
            else return Controllers.Any(x => x.Cupboards.Contains(cupboard));
        }

        private Dictionary<Vector3, float> GetCurrentActiveEvents()
        {
            if (Controllers == null || Controllers.Count == 0) return null;
            Dictionary<Vector3, float> result = new Dictionary<Vector3, float>();
            foreach (ControllerHomeRaid controller in Controllers) result.Add(controller.transform.position, controller.MaxRadius);
            return result;
        }
        #endregion API

        #region Commands
        [ConsoleCommand("giveflare")]
        private void ConsoleCommandGiveFlare(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length != 2 || arg.Player() != null) return;
            ulong skinId = Convert.ToUInt64(arg.Args[0]);
            FlareConfig config = _config.Flares.FirstOrDefault(x => x.SkinId == skinId);
            if (config == null)
            {
                Puts($"Custom Flare with SkinID {skinId} not found in plugin configuration!");
                return;
            }
            ulong steamId = Convert.ToUInt64(arg.Args[1]);
            BasePlayer target = BasePlayer.FindByID(steamId);
            if (target == null)
            {
                Puts($"Player with SteamID {steamId} not found!");
                return;
            }
            Item item = GetFlare(config);
            int slots = target.inventory.containerMain.capacity + target.inventory.containerBelt.capacity;
            int taken = target.inventory.containerMain.itemList.Count + target.inventory.containerBelt.itemList.Count;
            if (slots - taken > 0) target.inventory.GiveItem(item);
            else item.Drop(target.transform.position, Vector3.up);
            Puts($"Player {target.displayName} has successfully received a custom flare with SkinID = {config.SkinId}");
        }

        [ChatCommand("defstop")]
        private void ChatStopEvent(BasePlayer player)
        {
            if (!player.IsAdmin) return;
            ControllerHomeRaid controller = Controllers.FirstOrDefault(x => x.Players.Contains(player));
            if (controller == null) return;
            Finish(controller);
            AlertToPlayer(player, GetMessage("FinishAdmin", player.UserIDString, _config.Prefix));
        }

        [ChatCommand("checkfoundations")]
        private void ChatCheckFoundations(BasePlayer player)
        {
            if (!player.IsAdmin && !player.IsDeveloper && player.IsFlying) return;

            BuildingPrivlidge cupboard = player.GetBuildingPrivilege();
            if (cupboard == null) return;
            if (!player.IsAdmin && !cupboard.authorizedPlayers.Any(x => IsTeam(player.userID, x))) return;

            HashSet<BuildingPrivlidge> cupboards; HashSet<BuildingBlock> foundations; Vector3 center3; float radius; float maxRadius;
            FindAllBuildings(cupboard, out cupboards, out foundations, out center3, out radius, out maxRadius);

            foreach (BuildingBlock foundation in foundations)
            {
                string text = string.Empty;

                if (!player.IsAdmin && !IsTeam(player.userID, foundation.OwnerID)) text += "⊘";
                if (!IsValidHeightFoundation(foundation.transform.position)) text += "⊝";
                if (!IsValidTopologyFoundation(foundation.transform.position)) text += "⊛";
                if (!IsValidMarkersFoundation(foundation.transform.position)) text += "◉";

                bool isAdmin = player.IsAdmin;
                if (!isAdmin)
                {
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                    player.SendNetworkUpdateImmediate();
                }
                try
                {
                    if (string.IsNullOrEmpty(text)) player.SendConsoleCommand("ddraw.text", 30f, Color.green, foundation.transform.position, "<size=40>✓</size>");
                    else
                    {
                        player.SendConsoleCommand("ddraw.line", 30f, Color.red, foundation.transform.position, foundation.transform.position + Vector3.up * 100f);
                        player.SendConsoleCommand("ddraw.text", 30f, Color.red, foundation.transform.position, $"<size=80>{text}</size>");
                    }
                }
                finally
                {
                    if (!isAdmin)
                    {
                        player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                        player.SendNetworkUpdateImmediate();
                    }
                }
            }
        }
        #endregion Commands
    }
}

namespace Oxide.Plugins.DefendableHomesExtensionMethods
{
    public static class ExtensionMethods
    {
        public static bool Any<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) if (predicate(enumerator.Current)) return true;
            return false;
        }

        public static HashSet<TSource> Where<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            HashSet<TSource> result = new HashSet<TSource>();
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) if (predicate(enumerator.Current)) result.Add(enumerator.Current);
            return result;
        }

        public static TSource FirstOrDefault<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) if (predicate(enumerator.Current)) return enumerator.Current;
            return default(TSource);
        }

        public static TSource Max<TSource>(this IEnumerable<TSource> source, Func<TSource, double> predicate)
        {
            TSource result = default(TSource);
            double resultValue = double.MinValue;
            using (var enumerator = source.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    TSource element = enumerator.Current;
                    double elementValue = predicate(element);
                    if (elementValue > resultValue)
                    {
                        result = element;
                        resultValue = elementValue;
                    }
                }
            }
            return result;
        }

        public static HashSet<TResult> Select<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> predicate)
        {
            HashSet<TResult> result = new HashSet<TResult>();
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) result.Add(predicate(enumerator.Current));
            return result;
        }

        public static HashSet<TResult> SelectMany<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, IEnumerable<TResult>> predicate)
        {
            HashSet<TResult> result = new HashSet<TResult>();
            foreach (TSource elements in source) foreach (TResult element in predicate(elements)) result.Add(element);
            return result;
        }

        public static HashSet<T> OfType<T>(this IEnumerable<BaseEntity> source)
        {
            HashSet<T> result = new HashSet<T>();
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) if (enumerator.Current is T) result.Add((T)(object)enumerator.Current);
            return result;
        }

        public static HashSet<TSource> ToHashSet<TSource>(this IEnumerable<TSource> source)
        {
            HashSet<TSource> result = new HashSet<TSource>();
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) result.Add(enumerator.Current);
            return result;
        }

        private static void Replace<TSource>(this IList<TSource> source, int x, int y)
        {
            TSource t = source[x];
            source[x] = source[y];
            source[y] = t;
        }

        private static List<TSource> QuickSort<TSource>(this List<TSource> source, Func<TSource, float> predicate, int minIndex, int maxIndex)
        {
            if (minIndex >= maxIndex) return source;

            int pivotIndex = minIndex - 1;
            for (int i = minIndex; i < maxIndex; i++)
            {
                if (predicate(source[i]) < predicate(source[maxIndex]))
                {
                    pivotIndex++;
                    source.Replace(pivotIndex, i);
                }
            }
            pivotIndex++;
            source.Replace(pivotIndex, maxIndex);

            QuickSort(source, predicate, minIndex, pivotIndex - 1);
            QuickSort(source, predicate, pivotIndex + 1, maxIndex);

            return source;
        }

        public static List<TSource> OrderByQuickSort<TSource>(this List<TSource> source, Func<TSource, float> predicate) => source.QuickSort(predicate, 0, source.Count - 1);

        public static TSource Min<TSource>(this IEnumerable<TSource> source, Func<TSource, float> predicate)
        {
            TSource result = default(TSource);
            float resultValue = float.MaxValue;
            using (var enumerator = source.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    TSource element = enumerator.Current;
                    float elementValue = predicate(element);
                    if (elementValue < resultValue)
                    {
                        result = element;
                        resultValue = elementValue;
                    }
                }
            }
            return result;
        }

        public static TSource Max<TSource>(this IEnumerable<TSource> source, Func<TSource, float> predicate)
        {
            TSource result = default(TSource);
            float resultValue = float.MinValue;
            using (var enumerator = source.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    TSource element = enumerator.Current;
                    float elementValue = predicate(element);
                    if (elementValue > resultValue)
                    {
                        result = element;
                        resultValue = elementValue;
                    }
                }
            }
            return result;
        }

        public static TSource Max<TSource>(this IEnumerable<TSource> source, Func<TSource, int> predicate)
        {
            TSource result = default(TSource);
            int resultValue = int.MinValue;
            using (var enumerator = source.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    TSource element = enumerator.Current;
                    int elementValue = predicate(element);
                    if (elementValue > resultValue)
                    {
                        result = element;
                        resultValue = elementValue;
                    }
                }
            }
            return result;
        }

        public static bool IsPlayer(this BasePlayer player) => player != null && player.userID.IsSteamId();

        public static bool IsExists(this BaseNetworkable entity) => entity != null && !entity.IsDestroyed;

        public static void ClearItemsContainer(this ItemContainer container)
        {
            for (int i = container.itemList.Count - 1; i >= 0; i--)
            {
                Item item = container.itemList[i];
                item.RemoveFromContainer();
                item.Remove();
            }
        }

        public static void KillMapMarker(this HackableLockedCrate crate)
        {
            if (!crate.mapMarkerInstance.IsExists()) return;
            crate.mapMarkerInstance.Kill();
            crate.mapMarkerInstance = null;
        }

        public static Action GetPrivateAction(this object obj, string methodName)
        {
            MethodInfo mi = obj.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (mi != null) return (Action)Delegate.CreateDelegate(typeof(Action), obj, mi);
            else return null;
        }
    }
}