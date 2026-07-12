using Facepunch;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins.BossMonsterExtensionMethods;
using Rust;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using Facepunch;
using UnityEngine;
using UnityEngine.AI;
using Oxide.Plugins.BossMonsterExtensionMethods;

namespace Oxide.Plugins
{
    public enum Gender { Random, Male, Female }
    public enum SkinTone { Random, Lightest, Light, Dark, Darkest }
}

namespace Oxide.Plugins
{
    [Info("BossMonster", "Grimm530", "2.2.3")]
    internal class BossMonster : RustPlugin
    {
        #region Config
        private const bool En = true;

        private PluginConfig _config;

        private void DebugLog(string message, bool verbose = false, bool spawnInit = false)
        {
            if (_config == null || !_config.Debug) return;
            if (spawnInit)
            {
                if (!_config.DebugSpawnInit) return;
            }
            else if (verbose)
            {
                if (!_config.DebugVerbose) return;
            }
            else if (!_config.DebugMinimal)
            {
                return;
            }
            Puts(message);
        }

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
            if (_config.PluginVersion < new VersionNumber(2, 1, 1)) _config.AmountBosses = 5;
            if (_config.PluginVersion < new VersionNumber(2, 2, 1))
            {
                _config.DebugBossBootstrap = true;
                _config.DebugHelperEngagement = true;
            }
            _config.PluginVersion = Version;
            Puts("Config update completed!");
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        public class GuiAnnouncementsConfig
        {
            [JsonProperty(En ? "Do you use the GUI Announcements plugin? [true/false]" : "Использовать ли GUI Announcements? [true/false]")] public bool IsGuiAnnouncements { get; set; }
            [JsonProperty(En ? "Banner color" : "Цвет баннера")] public string BannerColor { get; set; }
            [JsonProperty(En ? "Text color" : "Цвет текста")] public string TextColor { get; set; }
            [JsonProperty(En ? "Adjust Vertical Position" : "Отступ от верхнего края")] public float ApiAdjustVPosition { get; set; }
        }

        public class NotifyConfig
        {
            [JsonProperty(En ? "Do you use the Notify plugin? [true/false]" : "Использовать ли плагин Notify? [true/false]")] public bool IsNotify { get; set; }
            [JsonProperty(En ? "Type" : "Тип")] public string Type { get; set; }
        }

        public class DiscordConfig
        {
            [JsonProperty(En ? "Do you use the Discord Messages plugin? [true/false]" : "Использовать ли плагин Discord Messages? [true/false]")] public bool IsDiscord { get; set; }
            [JsonProperty("Webhook URL")] public string WebhookUrl { get; set; }
            [JsonProperty(En ? "Embed Color (DECIMAL)" : "Цвет полосы (DECIMAL)")] public int EmbedColor { get; set; }
            [JsonProperty(En ? "Keys of required messages" : "Ключи необходимых сообщений")] public HashSet<string> Keys { get; set; }
        }

        private class PluginConfig
        {
            [JsonProperty(En ? "Prefix of chat messages" : "Префикс сообщений в чате")] public string Prefix { get; set; }
            [JsonProperty(En ? "Do you use the chat? [true/false]" : "Использовать ли чат? [true/false]")] public bool IsChat { get; set; }
            [JsonProperty(En ? "GUI Announcements setting" : "Настройка GUI Announcements")] public GuiAnnouncementsConfig GuiAnnouncements { get; set; }
            [JsonProperty(En ? "Notify setting" : "Настройка Notify")] public NotifyConfig Notify { get; set; }
            [JsonProperty(En ? "Discord setting (only for users DiscordMessages plugin)" : "Настройка оповещений в Discord (только для тех, кто использует плагин DiscordMessages)")] public DiscordConfig Discord { get; set; }
            [JsonProperty(En ? "Use the PVE mode of the plugin? (only for users PveMode plugin)" : "Использовать PVE режим работы плагина? (только для тех, кто использует плагин PveMode)")] public bool Pve { get; set; }
            [JsonProperty(En ? "NPC Turret Damage Multiplier" : "Множитель урона от турелей по NPC")] public float TurretDamageScale { get; set; }
            [JsonProperty(En ? "Total maintained number of bosses on the map at once" : "Кол-во боссов на карте одновременно")] public int AmountBosses { get; set; }
            [JsonProperty(En ? "Configuration version" : "Версия конфигурации")] public VersionNumber PluginVersion { get; set; }
            [JsonProperty(En ? "Enable debug logging (attack cycle / abilities) [true/false]" : "Отладочный лог [true/false]", DefaultValueHandling = DefaultValueHandling.Populate)]
            [DefaultValue(false)]
            public bool Debug { get; set; }

            [JsonProperty(En ? "Debug: minimal (phases, eligibility, cycle timer expired, strafe start/stop) [true/false]" : "Отладка: кратко (фазы, таймер цикла, strafe) [true/false]")]
            [DefaultValue(true)]
            public bool DebugMinimal { get; set; } = true;

            [JsonProperty(En ? "Debug: verbose (AOE positions, per-hit damage, helper aggro pulse, 5s timer ticks, nav details) [true/false]" : "Отладка: подробно (AOE, урон, пульс хелперов, навигация) [true/false]")]
            [DefaultValue(false)]
            public bool DebugVerbose { get; set; }

            [JsonProperty(En ? "Debug: boss spawn init (ability timers, pool, monument registration lines) [true/false]" : "Отладка: инициализация босса (таймеры, пул способностей) [true/false]")]
            [DefaultValue(false)]
            public bool DebugSpawnInit { get; set; }

            [JsonProperty(En ? "Debug: post-spawn boss nav bootstrap (Resume, PlaceOnNavMesh) — no SetTarget [true/false]" : "Отладка: бутстрап навигации после спавна [true/false]")]
            [DefaultValue(true)]
            public bool DebugBossBootstrap { get; set; } = true;

            [JsonProperty(En ? "Debug: per-helper KickHelperNpcEngagement + staggered kicks [true/false]" : "Отладка: вовлечение хелперов [true/false]")]
            [DefaultValue(true)]
            public bool DebugHelperEngagement { get; set; } = true;

            public static PluginConfig DefaultConfig()
            {
                return new PluginConfig()
                {
                    Prefix = "[BossMonster]",
                    IsChat = true,
                    Debug = false,
                    DebugMinimal = true,
                    DebugVerbose = false,
                    DebugSpawnInit = false,
                    DebugBossBootstrap = true,
                    DebugHelperEngagement = true,
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
                        Type = "0"
                    },
                    Discord = new DiscordConfig
                    {
                        IsDiscord = false,
                        WebhookUrl = "https://support.discordapp.com/hc/en-us/articles/228383668-Intro-to-Webhooks",
                        EmbedColor = 13516583,
                        Keys = new HashSet<string>
                        {
                            "Start",
                            "Finish"
                        }
                    },
                    Pve = false,
                    TurretDamageScale = 0.5f,
                    AmountBosses = 5,
                    PluginVersion = new VersionNumber()
                };
            }
        }
        #endregion Config

        #region Data
        public class NpcWear
        {
            [JsonProperty("ShortName")] public string ShortName { get; set; }
            [JsonProperty("SkinID (0 - default)")] public ulong SkinID { get; set; }
        }

        public class NpcBelt
        {
            [JsonProperty("ShortName")] public string ShortName { get; set; }
            [JsonProperty(En ? "Amount" : "Кол-во")] public int Amount { get; set; }
            [JsonProperty("SkinID (0 - default)")] public ulong SkinID { get; set; }
            [JsonProperty(En ? "Mods" : "Модификации на оружие")] public HashSet<string> Mods { get; set; }
            [JsonProperty(En ? "Ammo" : "Боеприпасы")] public string Ammo { get; set; }
        }

        public class ColorConfig
        {
            [JsonProperty("r")] public float R { get; set; }
            [JsonProperty("g")] public float G { get; set; }
            [JsonProperty("b")] public float B { get; set; }
        }

        public class MarkerConfig
        {
            [JsonProperty(En ? "Do you use the Marker? [true/false]" : "Использовать ли маркер? [true/false]")] public bool IsMarker { get; set; }
            [JsonProperty(En ? "Radius" : "Радиус")] public float Radius { get; set; }
            [JsonProperty(En ? "Transparency" : "Прозрачность")] public float Alpha { get; set; }
            [JsonProperty(En ? "Marker color" : "Цвет маркера")] public ColorConfig Color { get; set; }
        }

        public class NpcEconomic
        {
            [JsonProperty("Economics")] public double Economics { get; set; }
            [JsonProperty(En ? "Server Rewards (minimum 1)" : "Server Rewards (минимум 1)")] public int ServerRewards { get; set; }
            [JsonProperty(En ? "IQEconomic (minimum 1)" : "IQEconomic (минимум 1)")] public int IQEconomic { get; set; }
            [JsonProperty("XPerience")] public double XPerience { get; set; }
        }

        public class MonumentPositionsConfig
        {
            [JsonProperty(En ? "Name of monument" : "Название монумента")] public string Name { get; set; }
            [JsonProperty(En ? "List of positions" : "Список позиций")] public HashSet<string> Positions { get; set; }
        }

        public class ItemConfig
        {
            [JsonProperty("ShortName")] public string ShortName { get; set; }
            [JsonProperty(En ? "Minimum" : "Минимальное кол-во")] public int MinAmount { get; set; }
            [JsonProperty(En ? "Maximum" : "Максимальное кол-во")] public int MaxAmount { get; set; }
            [JsonProperty(En ? "Chance probability [0.0-100.0]" : "Шанс выпадения предмета [0.0-100.0]")] public float Chance { get; set; }
            [JsonProperty(En ? "Is this a blueprint? [true/false]" : "Это чертеж? [true/false]")] public bool IsBluePrint { get; set; }
            [JsonProperty("SkinID (0 - default)")] public ulong SkinID { get; set; }
            [JsonProperty(En ? "Text (empty - default)" : "Текст (empty - default)")] public string Text { get; set; }
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
            [JsonProperty(En ? "The path to the prefab" : "Путь к prefab-у")] public string PrefabDefinition { get; set; }
        }

        public class PrefabLootTableConfig
        {
            [JsonProperty(En ? "Minimum number of prefabs" : "Минимальное кол-во prefab-ов")] public int Min { get; set; }
            [JsonProperty(En ? "Maximum number of prefabs" : "Максимальное кол-во prefab-ов")] public int Max { get; set; }
            [JsonProperty(En ? "Use minimum and maximum values? [true/false]" : "Использовать минимальное и максимальное значение? [true/false]")] public bool UseCount { get; set; }
            [JsonProperty(En ? "List of prefabs" : "Список prefab-ов")] public List<PrefabConfig> Prefabs { get; set; }
        }

        public class MultiPointAOEConfig
        {
            [JsonProperty(En ? "Enable multi-point AOE patterns? [true/false]" : "Мульти-точечный AOE [true/false]")] public bool EnableMultiPointAOE { get; set; }
            [JsonProperty(En ? "Number of AOE locations (8-16 typical)" : "Кол-во точек AOE")] public int AOELocationCount { get; set; }
            [JsonProperty(En ? "Warning time before damage [seconds]" : "Предупреждение (сек)")] public float WarningTime { get; set; }
            [JsonProperty(En ? "Pattern spread radius" : "Радиус паттерна")] public float PatternRadius { get; set; }
            [JsonProperty(En ? "Show visual warning circles? [true/false]" : "Круги предупреждения [true/false]")] public bool ShowWarningCircles { get; set; }
            [JsonProperty(En ? "Warning circle colors (Spikes, Fire, Ice, Electric)" : "Цвета кругов")] public Dictionary<string, string> WarningCircleColors { get; set; }
        }

        public class RadiusActionsConfig
        {
            [JsonProperty(En ? "Use only one ability at a time? [true/false]" : "Одновременно использовать только одну способность? [true/false]")] public bool UseOnlyOneAbility { get; set; }
            [JsonProperty(En ? "Radius (to disable all abilities, set the value to 0)" : "Радиус (чтобы отключить все способности установите значение 0)")] public float Radius { get; set; }
            [JsonProperty(En ? "Multi-Point AOE Settings" : "Настройки мульти-точечного AOE")] public MultiPointAOEConfig MultiPointAOE { get; set; }
            [JsonProperty(En ? "Spikes ability cooldown time (to disable the ability, set the value -1)" : "Время перезарядки способности Spikes (чтобы отключить способность установите значение -1)")] public int TimeToSpikes { get; set; }
            [JsonProperty(En ? "Applied damage to player from Spikes" : "Получаемый урон игроком от Spikes")] public float DamageSpikes { get; set; }
            [JsonProperty(En ? "FireBall ability cooldown time (to disable the ability, set the value -1)" : "Время перезарядки способности FireBall (чтобы отключить способность установите значение -1)")] public int TimeToFire { get; set; }
            [JsonProperty(En ? "Applied damage to player from FireBall" : "Получаемый урон игроком от FireBall")] public float DamageFire { get; set; }
            [JsonProperty(En ? "ElectricShock ability cooldown time (to disable the ability, set the value -1)" : "Время перезарядки способности ElectricShock (чтобы отключить способность установите значение -1)")] public int TimeToElectricShock { get; set; }
            [JsonProperty(En ? "Applied damage to player from ElectricShock" : "Получаемый урон игроком от ElectricShock")] public float DamageElectricShock { get; set; }
            [JsonProperty(En ? "Wounded ability cooldown time (to disable the ability, set the value -1)" : "Время перезарядки способности Wounded (чтобы отключить способность установите значение -1)")] public int TimeToWounded { get; set; }
            [JsonProperty(En ? "Freeze ability cooldown time (to disable the ability, set the value -1)" : "Время перезарядки способности Freeze (чтобы отключить способность установите значение -1)")] public int TimeToFreeze { get; set; }
            [JsonProperty(En ? "Animal Ability Settings" : "Настройки способности Animal")] public AnimalAbility AnimalAbility { get; set; }
            [JsonProperty(En ? "NPC Ability Settings" : "Настройки способности NPC")] public NpcAbility NpcAbility { get; set; }
            [JsonProperty(En ? "Radiation" : "Радиация")] public float Radiation { get; set; }
            [JsonProperty(En ? "Temperature" : "Температура")] public float Temperature { get; set; }
        }

        public class AnimalAbility
        {
            [JsonProperty(En ? "Ability Cooldown Time (to disable the ability, set the value -1)" : "Время перезарядки способности (чтобы отключить способность установите значение -1)")] public int Time { get; set; }
            [JsonProperty(En ? "Type of animal (Wolf, Bear)" : "Тип животного (Wolf, Bear, Polar Bear)")] public string Type { get; set; }
            [JsonProperty(En ? "Number of animals" : "Кол-во животных")] public int Count { get; set; }
            [JsonProperty(En ? "Despawn time animals" : "Время удаления животных")] public float DespawnTime { get; set; }
        }

        public class NpcAbility
        {
            [JsonProperty(En ? "Ability Cooldown Time (to disable the ability, set the value -1)" : "Время перезарядки способности (чтобы отключить способность установите значение -1)")] public int Time { get; set; }
            [JsonProperty(En ? "NPC Settings" : "Настройки NPC")] public AddNpcConfig ConfigNpc { get; set; }
            [JsonProperty(En ? "Number of NPCs" : "Кол-во NPC")] public int Count { get; set; }
            [JsonProperty(En ? "Despawn time NPCs" : "Время удаления NPC")] public float DespawnTime { get; set; }
        }

        public class AddNpcConfig
        {
            [JsonProperty(En ? "Names" : "Названия")] public List<string> Names { get; set; }
            [JsonProperty(En ? "Health" : "Кол-во ХП")] public float Health { get; set; }
            [JsonProperty(En ? "Roam Range" : "Дальность патрулирования местности")] public float RoamRange { get; set; }
            [JsonProperty(En ? "Chase Range" : "Дальность погони за целью")] public float ChaseRange { get; set; }
            [JsonProperty(En ? "Attack Range Multiplier" : "Множитель радиуса атаки")] public float AttackRangeMultiplier { get; set; }
            [JsonProperty(En ? "Sense Range" : "Радиус обнаружения цели")] public float SenseRange { get; set; }
            [JsonProperty(En ? "Target Memory Duration [sec.]" : "Длительность памяти цели [sec.]")] public float MemoryDuration { get; set; }
            [JsonProperty(En ? "Scale damage" : "Множитель урона")] public float DamageScale { get; set; }
            [JsonProperty(En ? "Aim Cone Scale" : "Множитель разброса")] public float AimConeScale { get; set; }
            [JsonProperty(En ? "Detect the target only in the NPC's viewing vision cone? [true/false]" : "Обнаруживать цель только в углу обзора NPC? [true/false]")] public bool CheckVisionCone { get; set; }
            [JsonProperty(En ? "Vision Cone" : "Угол обзора")] public float VisionCone { get; set; }
            [JsonProperty(En ? "Speed" : "Скорость")] public float Speed { get; set; }
            [JsonProperty(En ? "Disable radio effects? [true/false]" : "Отключать эффекты рации? [true/false]")] public bool DisableRadio { get; set; }
            [JsonProperty(En ? "Wear items" : "Одежда")] public HashSet<NpcWear> WearItems { get; set; }
            [JsonProperty(En ? "Belt items" : "Быстрые слоты")] public HashSet<NpcBelt> BeltItems { get; set; }
            [JsonProperty(En ? "Kit (it is recommended to use the previous 2 settings to improve performance)" : "Kit (рекомендуется использовать предыдущие 2 пункта настройки для повышения производительности)")] public string Kit { get; set; }
        }

        public class TakeDamageActionsConfig
        {
            [JsonProperty(En ? "Disable all abilities when applying damage? [true/false]" : "Отключить все способности при нанесении урона? [true/false]")] public bool IsDisable { get; set; }
            [JsonProperty(En ? "Regeneration of health from the applied damage [%]" : "Восстановление здоровья от нанесенного урона [%]")] public float Vampirism { get; set; }
            [JsonProperty(En ? "The amount of calories consumed" : "Кол-во калорий, которое расходуется")] public float CaloriesTarget { get; set; }
            [JsonProperty(En ? "The amount of water consumed" : "Кол-во воды, которое расходуется")] public float HydrationTarget { get; set; }
            [JsonProperty(En ? "The amount of added radiation" : "Кол-во добавляемой радиации")] public float RadiationTarget { get; set; }
            [JsonProperty(En ? "The amount of added bleeding" : "Кол-во добавляемого кровотечения")] public float BleedingTarget { get; set; }
        }

        public class NpcConfig
        {
            [JsonProperty(En ? "Enabled? [true/false]" : "Включен? [true/false]")] public bool Enabled { get; set; }
            [JsonProperty(En ? "Name" : "Название")] public string Name { get; set; }
            [JsonProperty(En ? "Health" : "Кол-во ХП")] public float Health { get; set; }
            [JsonProperty(En ? "Roam Range" : "Дальность патрулирования местности")] public float RoamRange { get; set; }
            [JsonProperty(En ? "Chase Range" : "Дальность погони за целью")] public float ChaseRange { get; set; }
            [JsonProperty(En ? "Attack Range Multiplier" : "Множитель радиуса атаки")] public float AttackRangeMultiplier { get; set; }
            [JsonProperty(En ? "Sense Range" : "Радиус обнаружения цели")] public float SenseRange { get; set; }
            [JsonProperty(En ? "Target Memory Duration [sec.]" : "Длительность памяти цели [sec.]")] public float MemoryDuration { get; set; }
            [JsonProperty(En ? "Scale damage" : "Множитель урона")] public float DamageScale { get; set; }
            [JsonProperty(En ? "Aim Cone Scale" : "Множитель разброса")] public float AimConeScale { get; set; }
            [JsonProperty(En ? "Detect the target only in the NPC's viewing vision cone? [true/false]" : "Обнаруживать цель только в углу обзора NPC? [true/false]")] public bool CheckVisionCone { get; set; }
            [JsonProperty(En ? "Vision Cone" : "Угол обзора")] public float VisionCone { get; set; }
            [JsonProperty(En ? "Speed" : "Скорость")] public float Speed { get; set; }
            [JsonProperty(En ? "Minimum time of appearance after death [sec.]" : "Минимальное время появления после смерти [sec.]")] public float MinTime { get; set; }
            [JsonProperty(En ? "Maximum time of appearance after death [sec.]" : "Максимальное время появления после смерти [sec.]")] public float MaxTime { get; set; }
            [JsonProperty(En ? "Disable automatic respawning of boss after death? (True to disable auto respawn) [true/false]" : "Отключить автоматическое появление после смерти? [true/false]")] public bool DisableTimer { get; set; }
            [JsonProperty(En ? "Disable radio effects? [true/false]" : "Отключать эффекты рации? [true/false]")] public bool DisableRadio { get; set; }
            [JsonProperty(En ? "Remove a corpse after death? (it is recommended to use the true value to improve performance) [true/false]" : "Удалять труп после смерти? (рекомендуется использовать значение true для повышения производительности) [true/false]")] public bool IsRemoveCorpse { get; set; }
            [JsonProperty(En ? "Wear items" : "Одежда")] public HashSet<NpcWear> WearItems { get; set; }
            [JsonProperty(En ? "Belt items" : "Быстрые слоты")] public HashSet<NpcBelt> BeltItems { get; set; }
            [JsonProperty(En ? "Kit (it is recommended to use the previous 2 settings to improve performance)" : "Kit (рекомендуется использовать предыдущие 2 пункта настройки для повышения производительности)")] public string Kit { get; set; }
            [JsonProperty(En ? "Marker settings" : "Настройки маркера")] public MarkerConfig Marker { get; set; }
            [JsonProperty(En ? "The amount of economics that is given for killing the boss" : "Кол-во экономики, которое выдается за убийство босса")] public NpcEconomic Economic { get; set; }
            [JsonProperty(En ? "List of monument locations" : "Список расположений на монументах")] public HashSet<MonumentPositionsConfig> Monuments { get; set; }
            [JsonProperty(En ? "If the boss ends up below ocean sea level, should the boss return to it's place of appearance? [true/false]" : "Должен ли босс убегать на место своего появления, если он находится ниже уровня океана? [true/false]")] public bool CanRunAwayWater { get; set; }
            [JsonProperty(En ? "Type of navigation grid (0 - used mainly on the island, 1 - used mainly under water or under land, as well as outside the map, can be used on some monuments)" : "Тип навигационной сетки (0 - используется в основном на острове, 1 - используется в основном под водой или землей, а также за пределами карты, может использоваться на некоторых монументах)")] public int TypeNavMesh { get; set; }
            [JsonProperty(En ? "The distance at which you can apply damage to the boss (use 0 at any distance)" : "Дистанция, при которой можно наносить урон по боссу (при любой дистанции использовать 0)")] public float PreventDamageRange { get; set; }
            [JsonProperty(En ? "Notify in a chat about actions with the boss? [true/false]" : "Оповещать в чате о действиях с боссом? [true/false]")] public bool IsChat { get; set; }
            [JsonProperty(En ? "The path to the crate that appears at the place of death (empty - not used)" : "Путь к ящику, который появляется на месте смерти (empty - not used)")] public string CratePrefab { get; set; }
            [JsonProperty(En ? "Which loot table should the plugin use? (0 - default; 1 - own; 2 - AlphaLoot; 3 - CustomLoot; 4 - loot table of the Rust objects; 5 - combine the 1 and 4 methods)" : "Какую таблицу предметов необходимо использовать? (0 - стандартную; 1 - собственную; 2 - AlphaLoot; 3 - CustomLoot; 4 - таблица предметов объектов Rust; 5 - совместить 1 и 4 методы)")] public int TypeLootTable { get; set; }
            [JsonProperty(En ? "Loot table from prefabs (if the loot table type is 4 or 5)" : "Таблица предметов из prefab-ов (если тип таблицы предметов - 4 или 5)")] public PrefabLootTableConfig PrefabLootTable { get; set; }
            [JsonProperty(En ? "Own loot table (if the loot table type is 1 or 5)" : "Собственная таблица предметов (если тип таблицы предметов - 1 или 5)")] public LootTableConfig OwnLootTable { get; set; }
            [JsonProperty(En ? "All actions that occur with the player within the NPC radius" : "Все действия, которые происходят с игроком в радиусе NPC")] public RadiusActionsConfig RadiusActions { get; set; }
            [JsonProperty(En ? "All actions that occur when applying NPC damage" : "Все действия, которые происходят при нанесении урона от NPC")] public TakeDamageActionsConfig TakeDamageActions { get; set; }
            [JsonProperty(En ? "Use the invisibility ability? (use only for bosses with melee weapons) [true/false]" : "Использовать способность невидимости? (использовать только для боссов с оружием ближнего боя) [true/false]")] public bool UseInvisible { get; set; }
            [JsonProperty(En ? "Enable damage to the boss with melee weapons only? [true/false]" : "Включить нанесение урона по боссу только оружием ближнего боя? [true/false]")] public bool OnlyMeleeWeapon { get; set; }
            [JsonProperty(En ? "Return to spawn point during battle? [true/false]" : "Возврат к точке спавна в бою [true/false]")] public bool ReturnToSpawnPoint { get; set; }
            [JsonProperty(En ? "Return to spawn interval (seconds, 0 = off)" : "Интервал возврата к спавну (сек)")] public float ReturnToSpawnPointInterval { get; set; }
            [JsonProperty(En ? "Melee hold distance (m, 0 = default ~2.2)" : "Дистанция ближнего боя (м)")] public float MeleeHoldDistance { get; set; }
            [JsonProperty(En ? "AOE standoff distance (m, 0 = default ~11)" : "Дистанция перед AOE (м)")] public float AoeStandoffDistance { get; set; }
            [JsonProperty(En ? "GrimmNPC: allow swimming (default true; set false for land-only) [true/false]" : "GrimmNPC: плавание (по умолчанию true) [true/false]", DefaultValueHandling = DefaultValueHandling.Populate)]
            [DefaultValue(true)]
            public bool CanSwim { get; set; } = true;
            [JsonProperty(En ? "GrimmNPC: deploy wooden cover barricade when low HP in combat (belt needs barricade.wood.cover + syringe) [true/false]" : "GrimmNPC: укрытие [true/false]", DefaultValueHandling = DefaultValueHandling.Populate)]
            [DefaultValue(true)]
            public bool GrimmEnableBarricadeCover { get; set; } = true;
            [JsonProperty(En ? "GrimmNPC: barricade when health fraction at or below this (0-1, e.g. 0.35)" : "GrimmNPC: порог HP для укрытия", DefaultValueHandling = DefaultValueHandling.Populate)]
            [DefaultValue(0.35f)]
            public float GrimmBarricadeMaxHealthFraction { get; set; } = 0.35f;
            [JsonProperty(En ? "GrimmNPC: min distance to target (m) to place barricade" : "GrimmNPC: дистанция баррикады", DefaultValueHandling = DefaultValueHandling.Populate)]
            [DefaultValue(12f)]
            public float GrimmBarricadeMinTargetDistance { get; set; } = 12f;
            [JsonProperty(En ? "GrimmNPC: seconds between barricade attempts" : "GrimmNPC: CD баррикады", DefaultValueHandling = DefaultValueHandling.Populate)]
            [DefaultValue(45f)]
            public float GrimmBarricadeCooldownSeconds { get; set; } = 45f;
            [JsonProperty(En ? "GrimmNPC: seconds between syringe heals" : "GrimmNPC: CD шприца", DefaultValueHandling = DefaultValueHandling.Populate)]
            [DefaultValue(4f)]
            public float GrimmSyringeCooldownSeconds { get; set; } = 4f;
            [JsonProperty(En ? "GrimmNPC: in combat/chase with target, only heal when health fraction at or below this (0-1)" : "GrimmNPC: порог лечения в бою", DefaultValueHandling = DefaultValueHandling.Populate)]
            [DefaultValue(0.5f)]
            public float GrimmSyringeCombatMaxHealthFraction { get; set; } = 0.5f;
            [JsonProperty(En ? "GrimmNPC: syringe heal amount scale (1 = default)" : "GrimmNPC: множитель лечения", DefaultValueHandling = DefaultValueHandling.Populate)]
            [DefaultValue(1f)]
            public float GrimmHealingScale { get; set; } = 1f;
        }

        internal HashSet<NpcConfig> Configs = new HashSet<NpcConfig>();

        private void EnsureBossMonsterDataDirectories()
        {
            string root = Path.Combine(Interface.Oxide.DataDirectory, "BossMonster");
            Directory.CreateDirectory(Path.Combine(root, "Bosses"));
            Directory.CreateDirectory(Path.Combine(root, "CustomMap"));
        }

        private void LoadConfigs()
        {
            EnsureBossMonsterDataDirectories();
            Puts("Loading files on the /oxide/data/BossMonster/Bosses/ path has started...");
            HashSet<string> allNamesForBosses = new HashSet<string>();
            foreach (string name in Interface.Oxide.DataFileSystem.GetFiles("BossMonster/Bosses/"))
            {
                string fileName = name.Split('/').Last().Split('.').First();
                NpcConfig config = Interface.Oxide.DataFileSystem.ReadObject<NpcConfig>($"BossMonster/Bosses/{fileName}");
                if (config != null)
                {
                    CheckLootTable(config.OwnLootTable);
                    CheckPrefabLootTable(config.PrefabLootTable);

                    if (allNamesForBosses.Contains(config.Name))
                    {
                        PrintWarning($"You can't use the same names for bosses! ({config.Name} -> {config.Name}|)");
                        config.Name += "|";
                    }
                    allNamesForBosses.Add(config.Name);

                    if (config.RoamRange > config.ChaseRange)
                    {
                        config.RoamRange = config.ChaseRange;
                        PrintWarning($"Roam Range should not be higher than Chase Range! ({fileName})");
                    }

                    if (config.RadiusActions.AnimalAbility.Time != -1)
                    {
                        if (config.RadiusActions.AnimalAbility.DespawnTime > config.RadiusActions.AnimalAbility.Time)
                        {
                            config.RadiusActions.AnimalAbility.DespawnTime = config.RadiusActions.AnimalAbility.Time;
                            PrintWarning($"Despawn time animals should not be higher than Ability Cooldown Time! ({fileName})");
                        }
                    }

                    if (config.RadiusActions.NpcAbility.Time != -1)
                    {
                        if (config.RadiusActions.NpcAbility.DespawnTime > config.RadiusActions.NpcAbility.Time)
                        {
                            config.RadiusActions.NpcAbility.DespawnTime = config.RadiusActions.NpcAbility.Time;
                            PrintWarning($"Despawn time NPCs should not be higher than Ability Cooldown Time! ({fileName})");
                        }
                        if (config.RadiusActions.NpcAbility.ConfigNpc.RoamRange > config.RadiusActions.NpcAbility.ConfigNpc.ChaseRange) config.RadiusActions.NpcAbility.ConfigNpc.RoamRange = config.RadiusActions.NpcAbility.ConfigNpc.ChaseRange;
                    }

                    if (config.RadiusActions.TimeToSpikes == 0) config.RadiusActions.TimeToSpikes = -1;
                    if (config.RadiusActions.TimeToFreeze == 0) config.RadiusActions.TimeToFreeze = -1;
                    if (config.RadiusActions.TimeToFire == 0) config.RadiusActions.TimeToFire = -1;
                    if (config.RadiusActions.TimeToElectricShock == 0) config.RadiusActions.TimeToElectricShock = -1;
                    if (config.RadiusActions.TimeToWounded == 0) config.RadiusActions.TimeToWounded = -1;
                    if (config.RadiusActions.TimeToFreeze == 0) config.RadiusActions.TimeToFreeze = -1;
                    if (config.RadiusActions.AnimalAbility.Time == 0) config.RadiusActions.AnimalAbility.Time = -1;
                    if (config.RadiusActions.NpcAbility.Time == 0) config.RadiusActions.NpcAbility.Time = -1;

                    Interface.Oxide.DataFileSystem.WriteObject($"BossMonster/Bosses/{fileName}", config);

                    if (!config.Enabled) continue;

                    Configs.Add(config);
                    Puts($"File {fileName} has been loaded successfully!");
                }
                else PrintError($"File {fileName} is corrupted and cannot be loaded!");
            }
        }
        #endregion Data

        #region Lang
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Start"] = "{0} <color=#55aaff>{1}</color> <color=#738d43>arrived</color> to zone <color=#55aaff>{2}</color>!",
                ["Finish"] = "{0} <color=#55aaff>{1}</color> killed <color=#55aaff>{2}</color> to zone <color=#55aaff>{3}</color>",
                ["NoDamage"] = "{0} You <color=#ce3f27>cannot</color> damage an boss from your position!",
                ["OnlyMeleeWeapon"] = "{0} You can only deal damage to this boss <color=#55aaff>with melee weapons</color>!"
            }, this);

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Start"] = "{0} <color=#55aaff>{1}</color> <color=#738d43>прибыл</color> в квадрат <color=#55aaff>{2}</color>!",
                ["Finish"] = "{0} <color=#55aaff>{1}</color> убил <color=#55aaff>{2}</color> в квадрате <color=#55aaff>{3}</color>",
                ["NoDamage"] = "{0} Вы <color=#ce3f27>не можете</color> нанести урон боссу с текущей позиции!",
                ["OnlyMeleeWeapon"] = "{0} Вы <color=#738d43>можете</color> нанести урон по этому боссу <color=#55aaff>только оружием ближнего боя</color>!"
            }, this, "ru");
        }

        private string GetMessage(string langKey, string userID) => lang.GetMessage(langKey, _ins, userID);

        private string GetMessage(string langKey, string userID, params object[] args) => (args.Length == 0) ? GetMessage(langKey, userID) : string.Format(GetMessage(langKey, userID), args);
        #endregion Lang

        #region Oxide Hooks
        [PluginReference] private readonly Plugin PveMode, AnimalSpawn;

        private static BossMonster _ins;

        private void Init()
        {
            _ins = this;
            if (_scientistNpcGetBestTarget == null)
                _scientistNpcGetBestTarget = typeof(ScientistNPC).GetMethod("GetBestTarget", BindingFlags.Public | BindingFlags.Instance);
            if (_scientistNpcSetKnown == null)
                _scientistNpcSetKnown = typeof(ScientistNPC).GetMethod("SetKnown", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private bool _bossWorldInitialized;
        private int _terrainWaitAttempts;
        private const int MaxTerrainWaitAttempts = 30;

        private void OnServerInitialized() => InitializeBossWorldAndGrimm();

        private void InitializeBossWorldAndGrimm()
        {
            if (TerrainMeta.Path == null || TerrainMeta.Path.Monuments == null)
            {
                _terrainWaitAttempts++;
                if (_terrainWaitAttempts > MaxTerrainWaitAttempts)
                {
                    PrintError($"BossMonster: TerrainMeta.Path / Monuments did not become ready after ~{MaxTerrainWaitAttempts * 2}s. Reload the plugin after the map finishes loading.");
                    return;
                }
                timer.Once(2f, InitializeBossWorldAndGrimm);
                return;
            }

            _terrainWaitAttempts = 0;

            InitializeGrimmNpc();
            if (!_grimmNpcAvailable)
            {
                PrintError("BossMonster requires the GrimmNPC Harmony mod (see .cursor/HarmonyMods/GrimmNPC/README.md). Deploy GrimmNPC.dll to HarmonyMods and run: harmony.load GrimmNPC");
                NextTick(() => Interface.Oxide.UnloadPlugin(Name));
                return;
            }

            if (!IsGrimmNpcInstanceReady())
            {
                PrintWarning("GrimmNPC.Instance is null (Harmony mod loads after Oxide). Reing in 5s — ensure GrimmNPC is loaded.");
                timer.Once(5f, InitializeBossWorldAndGrimm);
                return;
            }

            if (_bossWorldInitialized)
                return;
            _bossWorldInitialized = true;

            LoadConfigs();
            LoadIDs();
            LoadCustomMapPositions();

            _monuments = new HashSet<MonumentInfo>();
            foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
            {
                if (IsNecessaryMonument(monument))
                    _monuments.Add(monument);
            }

            foreach (NpcConfig config in Configs) if (!config.DisableTimer) _whatSpawnBosses.Add(config.Name);
            timer.In(10f, CheckSpawnBoss);
        }

        private void Unload()
        {
            foreach (ControllerBoss controller in _controllers.Values)
            {
                if (controller.Npc.IsExists())
                {
                    UnregisterNpcFromGrimmNpc(controller.Npc);
                    controller.Npc.Kill();
                }
            }
            _ins = null;
        }

        private void OnEntityTakeDamage(BasePlayer player, HitInfo info)
        {
            if (!player.IsPlayer() || info == null) return;
            ScientistNPC npc = info.Initiator as ScientistNPC;
            if (npc == null) return;
            ulong bossId = npc.net.ID.Value;
            if (_controllers.ContainsKey(bossId))
                _controllers[bossId].TakeDamageActions(player, info);
        }

        private object OnEntityTakeDamage(ScientistNPC entity, HitInfo info)
        {
            if (entity == null || info == null) return null;
            if (_controllers.ContainsKey(entity.net.ID.Value))
            {
                BasePlayer attacker = info.InitiatorPlayer;
                BaseEntity weaponPrefab = info.WeaponPrefab;

                if (!attacker.IsPlayer()) return null;

                if (info.damageTypes != null
                    && (weaponPrefab == null || weaponPrefab.ShortPrefabName == "grenade.molotov.deployed" || weaponPrefab.ShortPrefabName == "rocket_fire")
                    && info.damageTypes.GetMajorityDamageType() == Rust.DamageType.Heat)
                    return true;

                NpcConfig config = Configs.FirstOrDefault(x => x.Name == entity.displayName);
                if (config == null) return null;

                if (config.PreventDamageRange > 0f && Vector3.Distance(attacker.transform.position, entity.transform.position) > config.PreventDamageRange)
                {
                    AlertToPlayer(attacker, GetMessage("NoDamage", attacker.UserIDString, _config.Prefix));
                    return true;
                }

                if (config.OnlyMeleeWeapon)
                {
                    if (weaponPrefab == null || !(weaponPrefab is BaseMelee))
                    {
                        AlertToPlayer(attacker, GetMessage("OnlyMeleeWeapon", attacker.UserIDString, _config.Prefix));
                        return true;
                    }
                    if (weaponPrefab.ShortPrefabName == "jackhammer.entity" || weaponPrefab.ShortPrefabName == "chainsaw.entity") info.damageTypes.ScaleAll(0.25f);
                }
            }
            return null;
        }

        private object OnEntityTakeDamage(BaseAnimalNPC animal, HitInfo info)
        {
            if (animal == null || info == null) return null;
            if (_controllers.Any(x => x.Value.Animals.Contains(animal)))
            {
                if (info.InitiatorPlayer.IsPlayer()) return null;
                else return true;
            }
            else return null;
        }

        private void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (!player.IsPlayer()) return;
            ControllerBoss controller = _controllers.Values.FirstOrDefault(x => x.Players.Contains(player));
            if (controller != null) controller.Players.Remove(player);
        }
        #endregion Oxide Hooks

        #region GrimmNPC Integration
        private static Assembly _grimmNpcAssembly;
        private static Type _grimmNpcType;
        private static Type _customNpcDataType;
        private static MethodInfo _registerPendingMethod;
        private static MethodInfo _unregisterNpcMethod;
        private static MethodInfo _grimmNpcSetKnownMethod;
        private static MethodInfo _scientistNpcGetBestTarget;
        private static MethodInfo _scientistNpcSetKnown;
        private static bool _grimmNpcAvailable;
        private const ulong CustomNpcSkinId = 11162132011012UL;
        private const int NavAreaTerrain = 1;
        private const int NavAreaMonument = 25;
        private const int NavAgentTerrain = -1372625422;

        private static bool GetGrimmNpcFromHarmonyLoader(Type harmonyLoaderType, out Assembly assembly, out Type grimmNpcType)
        {
            assembly = null;
            grimmNpcType = null;
            if (harmonyLoaderType == null) return false;
            FieldInfo loadedModsField = harmonyLoaderType.GetField("loadedMods", BindingFlags.Static | BindingFlags.NonPublic);
            if (loadedModsField == null) return false;
            IEnumerable loadedMods = loadedModsField.GetValue(null) as IEnumerable;
            if (loadedMods == null) return false;
            foreach (object mod in loadedMods)
            {
                if (mod == null) continue;
                Type modType = mod.GetType();
                PropertyInfo nameProp = modType.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
                string modName = nameProp?.GetValue(mod) as string;
                if (!string.Equals(modName, "GrimmNPC", StringComparison.OrdinalIgnoreCase))
                    continue;
                PropertyInfo asmProp = modType.GetProperty("Assembly", BindingFlags.Public | BindingFlags.Instance);
                Assembly asm = asmProp?.GetValue(mod) as Assembly;
                if (asm == null) continue;
                Type t = asm.GetType("GrimmNPC.GrimmNPC") ?? asm.GetType("GrimmNPC");
                if (t == null) continue;
                assembly = asm;
                grimmNpcType = t;
                return true;
            }
            return false;
        }

        private void InitializeGrimmNpc()
        {
            Type harmonyLoaderType = Type.GetType("HarmonyLoader");
            if (harmonyLoaderType == null)
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    harmonyLoaderType = asm.GetType("HarmonyLoader");
                    if (harmonyLoaderType != null) break;
                }
            }

            _grimmNpcAssembly = null;
            _grimmNpcType = null;

            if (GetGrimmNpcFromHarmonyLoader(harmonyLoaderType, out Assembly loaderAsm, out Type loaderType))
            {
                PropertyInfo instProp = loaderType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                object inst = instProp?.GetValue(null);
                if (inst != null)
                {
                    _grimmNpcAssembly = loaderAsm;
                    _grimmNpcType = loaderType;
                }
            }

            if (_grimmNpcType == null)
            {
                Type fallbackType = null;
                Assembly fallbackAssembly = null;
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = assembly.GetType("GrimmNPC.GrimmNPC") ?? assembly.GetType("GrimmNPC");
                    if (t == null) continue;
                    PropertyInfo instanceProp = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                    object instance = instanceProp?.GetValue(null);
                    if (instance != null)
                    {
                        _grimmNpcAssembly = assembly;
                        _grimmNpcType = t;
                        break;
                    }
                    if (fallbackType == null)
                    {
                        fallbackType = t;
                        fallbackAssembly = assembly;
                    }
                }
                if (_grimmNpcType == null && fallbackType != null)
                {
                    _grimmNpcType = fallbackType;
                    _grimmNpcAssembly = fallbackAssembly;
                }
            }

            if (_grimmNpcType == null)
            {
                PrintWarning("GrimmNPC type not found in loaded assemblies.");
                _grimmNpcAvailable = false;
                return;
            }

            _customNpcDataType = _grimmNpcAssembly.GetType("GrimmNPC.CustomNpcData");
            if (_customNpcDataType == null)
            {
                PrintWarning("GrimmNPC.CustomNpcData not found.");
                _grimmNpcAvailable = false;
                return;
            }

            _registerPendingMethod = _grimmNpcType.GetMethod("RegisterPending", BindingFlags.Public | BindingFlags.Static);
            _unregisterNpcMethod = _grimmNpcType.GetMethod("UnregisterNpc", BindingFlags.Public | BindingFlags.Static);
            if (_registerPendingMethod == null || _unregisterNpcMethod == null)
            {
                PrintWarning("GrimmNPC RegisterPending/UnregisterNpc missing.");
                _grimmNpcAvailable = false;
                return;
            }

            ParameterInfo[] rp = _registerPendingMethod.GetParameters();
            if (rp.Length != 2 || !typeof(BaseEntity).IsAssignableFrom(rp[0].ParameterType) || rp[1].ParameterType.FullName != _customNpcDataType.FullName)
            {
                PrintWarning("GrimmNPC.RegisterPending signature unexpected.");
                _grimmNpcAvailable = false;
                return;
            }

            _grimmNpcSetKnownMethod = _grimmNpcType.GetMethod("SetKnown", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ScientistNPC), typeof(BaseEntity) }, null);
            _grimmNpcAvailable = true;
            object live = _grimmNpcType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null);
            Puts($"GrimmNPC integration bound ({_grimmNpcAssembly.GetName().Name}). Instance={(live != null ? "OK" : "pending — reload after harmony.load GrimmNPC")}.");
        }

        private bool IsGrimmNpcInstanceReady()
        {
            if (!_grimmNpcAvailable || _grimmNpcType == null) return false;
            PropertyInfo p = _grimmNpcType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            return p != null && p.GetValue(null) != null;
        }

        private void SetProperty(Type type, object instance, string propertyName, object value)
        {
            PropertyInfo prop = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanWrite)
                prop.SetValue(instance, value);
            else
            {
                FieldInfo field = type.GetField(propertyName, BindingFlags.Public | BindingFlags.Instance);
                field?.SetValue(instance, value);
            }
        }

        private void FillGrimmCustomNpcDataBoss(object npcData, NpcConfig config, Vector3 homePosition, bool forceTerrainNav)
        {
            bool raiding = config.BeltItems != null && config.BeltItems.Any(x => x.ShortName == "rocket.launcher" || x.ShortName == "explosive.timed");
            // TypeNavMesh 0 is terrain by default, but monument-local spawns still need mesh 25 or NPCs embed in prefab geometry.
            bool autoMonument = !forceTerrainNav && config.TypeNavMesh == 0
                && IsPositionInAnyMonumentBounds(homePosition)
                && NavMesh.SamplePosition(homePosition, out _, 20f, NavAreaMonument);
            bool useMonumentNav = !forceTerrainNav && (config.TypeNavMesh != 0 || autoMonument);
            int areaMask = useMonumentNav ? NavAreaMonument : NavAreaTerrain;
            int agentTypeId = useMonumentNav ? 0 : NavAgentTerrain;

            SetProperty(_customNpcDataType, npcData, "Name", config.Name);
            SetProperty(_customNpcDataType, npcData, "Health", config.Health);
            SetProperty(_customNpcDataType, npcData, "DamageScale", config.DamageScale);
            SetProperty(_customNpcDataType, npcData, "TurretDamageScale", _config.TurretDamageScale);
            SetProperty(_customNpcDataType, npcData, "AimConeScale", config.AimConeScale);
            SetProperty(_customNpcDataType, npcData, "CanBeTargetedByAutoTurrets", true);
            SetProperty(_customNpcDataType, npcData, "CanBeTargetedByGunTraps", true);
            SetProperty(_customNpcDataType, npcData, "CanBeTargetedByFlameTurrets", true);
            SetProperty(_customNpcDataType, npcData, "CanBeTargetedByAPC", true);
            SetProperty(_customNpcDataType, npcData, "HomePosition", homePosition);
            SetProperty(_customNpcDataType, npcData, "RoamRange", config.RoamRange);
            SetProperty(_customNpcDataType, npcData, "ChaseRange", config.ChaseRange);
            SetProperty(_customNpcDataType, npcData, "SenseRange", config.SenseRange);
            SetProperty(_customNpcDataType, npcData, "CanSleep", false);
            SetProperty(_customNpcDataType, npcData, "SleepDistance", 100f);
            SetProperty(_customNpcDataType, npcData, "AreaMask", areaMask);
            SetProperty(_customNpcDataType, npcData, "AgentTypeID", agentTypeId);
            SetProperty(_customNpcDataType, npcData, "StrafeOnlyWhenAttacking", true);
            SetProperty(_customNpcDataType, npcData, "CanSwim", config.CanSwim);
            SetProperty(_customNpcDataType, npcData, "IsRaidingNpc", raiding);
            SetProperty(_customNpcDataType, npcData, "EnableBarricadeCover", config.GrimmEnableBarricadeCover);
            SetProperty(_customNpcDataType, npcData, "BarricadeMaxHealthFraction", config.GrimmBarricadeMaxHealthFraction);
            SetProperty(_customNpcDataType, npcData, "BarricadeMinTargetDistance", config.GrimmBarricadeMinTargetDistance);
            SetProperty(_customNpcDataType, npcData, "BarricadeCooldownSeconds", config.GrimmBarricadeCooldownSeconds);
            SetProperty(_customNpcDataType, npcData, "SyringeCooldownSeconds", config.GrimmSyringeCooldownSeconds);
            SetProperty(_customNpcDataType, npcData, "SyringeCombatMaxHealthFraction", config.GrimmSyringeCombatMaxHealthFraction);
            SetProperty(_customNpcDataType, npcData, "HealingScale", config.GrimmHealingScale);

            if (useMonumentNav)
            {
                int resolvedAgent = GetMonumentAgentTypeID(homePosition);
                if (resolvedAgent != 0)
                    SetProperty(_customNpcDataType, npcData, "AgentTypeID", resolvedAgent);
            }
        }

        private void FillGrimmCustomNpcDataHelper(object npcData, AddNpcConfig config, string displayName, Vector3 homePosition, bool forceTerrainNav, int parentTypeNavMesh)
        {
            bool raiding = config.BeltItems != null && config.BeltItems.Any(x => x.ShortName == "rocket.launcher" || x.ShortName == "explosive.timed");
            bool autoHelperMonument = !forceTerrainNav && parentTypeNavMesh == 0
                && IsPositionInAnyMonumentBounds(homePosition)
                && NavMesh.SamplePosition(homePosition, out _, 20f, NavAreaMonument);
            bool helperMonument = !forceTerrainNav && (parentTypeNavMesh != 0 || autoHelperMonument);
            int areaMask = helperMonument ? NavAreaMonument : NavAreaTerrain;
            int agentTypeId = helperMonument ? 0 : NavAgentTerrain;
            float chaseRange = config.ChaseRange > 0 ? config.ChaseRange : 50f;
            float senseRange = config.SenseRange > 0 ? config.SenseRange : 50f;
            float roamRange = config.RoamRange > 0 ? config.RoamRange : Mathf.Max(25f, chaseRange * 0.85f);

            SetProperty(_customNpcDataType, npcData, "Name", displayName);
            SetProperty(_customNpcDataType, npcData, "Health", config.Health);
            SetProperty(_customNpcDataType, npcData, "DamageScale", config.DamageScale);
            SetProperty(_customNpcDataType, npcData, "TurretDamageScale", _config.TurretDamageScale);
            SetProperty(_customNpcDataType, npcData, "AimConeScale", config.AimConeScale);
            SetProperty(_customNpcDataType, npcData, "CanBeTargetedByAutoTurrets", true);
            SetProperty(_customNpcDataType, npcData, "CanBeTargetedByGunTraps", true);
            SetProperty(_customNpcDataType, npcData, "CanBeTargetedByFlameTurrets", true);
            SetProperty(_customNpcDataType, npcData, "CanBeTargetedByAPC", true);
            SetProperty(_customNpcDataType, npcData, "HomePosition", homePosition);
            SetProperty(_customNpcDataType, npcData, "RoamRange", roamRange);
            SetProperty(_customNpcDataType, npcData, "ChaseRange", chaseRange);
            SetProperty(_customNpcDataType, npcData, "SenseRange", senseRange);
            SetProperty(_customNpcDataType, npcData, "CanSleep", false);
            SetProperty(_customNpcDataType, npcData, "SleepDistance", 100f);
            SetProperty(_customNpcDataType, npcData, "AreaMask", areaMask);
            SetProperty(_customNpcDataType, npcData, "AgentTypeID", agentTypeId);
            SetProperty(_customNpcDataType, npcData, "StrafeOnlyWhenAttacking", true);
            SetProperty(_customNpcDataType, npcData, "CanSwim", true);
            SetProperty(_customNpcDataType, npcData, "IsRaidingNpc", raiding);
            if (helperMonument)
            {
                int resolvedAgent = GetMonumentAgentTypeID(homePosition);
                if (resolvedAgent != 0)
                    SetProperty(_customNpcDataType, npcData, "AgentTypeID", resolvedAgent);
            }
        }

        private bool RegisterPendingBossWithGrimm(ScientistNPC npc, NpcConfig config, Vector3 homePosition, bool forceTerrainNav)
        {
            if (!_grimmNpcAvailable || npc == null || config == null || _registerPendingMethod == null || !IsGrimmNpcInstanceReady())
                return false;
            object npcData = Activator.CreateInstance(_customNpcDataType);
            if (npcData == null) return false;
            FillGrimmCustomNpcDataBoss(npcData, config, homePosition, forceTerrainNav);
            _registerPendingMethod.Invoke(null, new object[] { npc, npcData });
            return true;
        }

        private bool RegisterPendingHelperWithGrimm(ScientistNPC npc, AddNpcConfig config, string displayName, Vector3 homePosition, bool forceTerrainNav, int parentTypeNavMesh)
        {
            if (!_grimmNpcAvailable || npc == null || config == null || _registerPendingMethod == null || !IsGrimmNpcInstanceReady())
                return false;
            object npcData = Activator.CreateInstance(_customNpcDataType);
            if (npcData == null) return false;
            FillGrimmCustomNpcDataHelper(npcData, config, displayName, homePosition, forceTerrainNav, parentTypeNavMesh);
            _registerPendingMethod.Invoke(null, new object[] { npc, npcData });
            return true;
        }

        private void RegisterAndSpawnBoss(ScientistNPC npc, NpcConfig config, Vector3 position, bool forceTerrainNav)
        {
            if (npc == null || npc.IsDestroyed) return;
            if (!RegisterPendingBossWithGrimm(npc, config, position, forceTerrainNav))
            {
                PrintError($"Boss '{config?.Name}': GrimmNPC registration failed; aborting spawn.");
                npc.Kill();
                return;
            }
            npc.Spawn();
        }

        private void RegisterAndSpawnHelper(ScientistNPC npc, AddNpcConfig config, string displayName, Vector3 position, bool forceTerrainNav, int parentTypeNavMesh)
        {
            if (npc == null || npc.IsDestroyed) return;
            if (!RegisterPendingHelperWithGrimm(npc, config, displayName, position, forceTerrainNav, parentTypeNavMesh))
            {
                PrintError("Helper NPC: GrimmNPC registration failed; aborting spawn.");
                npc.Kill();
                return;
            }
            npc.EnableSaving(false);
            npc.Spawn();
        }

        private void UnregisterNpcFromGrimmNpc(ScientistNPC npc)
        {
            if (!_grimmNpcAvailable || npc == null || _unregisterNpcMethod == null) return;
            ulong netId = npc.net?.ID.Value ?? 0;
            if (netId == 0) return;
            _unregisterNpcMethod.Invoke(null, new object[] { netId });
        }

        private string GetGrimmNpcPrefabOrDefault()
        {
            if (!_grimmNpcAvailable || _grimmNpcType == null)
                return "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab";
            MethodInfo getConfigMethod = _grimmNpcType.GetMethod("GetConfig", BindingFlags.Public | BindingFlags.Static);
            object npcConfig = getConfigMethod?.Invoke(null, null);
            if (npcConfig == null) return "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab";
            PropertyInfo prefabProperty = npcConfig.GetType().GetProperty("Prefab");
            object prefabValue = prefabProperty?.GetValue(npcConfig);
            if (prefabValue != null && !string.IsNullOrEmpty(prefabValue.ToString()))
                return prefabValue.ToString();
            return "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab";
        }

        private ScientistNPC CreateScientistNpcEntity(Vector3 position)
        {
            ScientistNPC npc = GameManager.server.CreateEntity(GetGrimmNpcPrefabOrDefault(), position, Quaternion.identity) as ScientistNPC;
            if (npc != null)
                npc.skinID = CustomNpcSkinId;
            else
                PrintWarning("Failed to create ScientistNPC from GrimmNPC prefab path.");
            return npc;
        }

        private struct ScientistBrainPostSpawnConfig
        {
            public float AttackRangeMultiplier;
            public float MemoryDuration;
            public float ConfigSenseRange;
            public float VisionConeDegrees;
            public float NavigatorSpeed;
            public bool CheckVisionCone;
            public bool UseBrainSenseRangeInSensesInit;
            public bool UseConfigSenseRangeForListenRange;
            public float RoamRangeForNavigator;
        }

        private static void ClearItemContainer(ItemContainer container)
        {
            if (container == null) return;
            foreach (Item existing in container.itemList.ToList())
            {
                if (existing == null) continue;
                existing.RemoveFromContainer();
                existing.Remove();
            }
        }

        private void EquipNpcWearAndBelt(ScientistNPC npc, HashSet<NpcWear> wearItems, HashSet<NpcBelt> beltItems, string logName)
        {
            if (npc == null || npc.inventory == null)
            {
                PrintWarning($"Equip NPC failed ({logName}): no inventory");
                return;
            }
            bool hasWear = wearItems != null && wearItems.Count > 0;
            bool hasBelt = beltItems != null && beltItems.Count > 0;
            if (hasWear && npc.inventory.containerWear != null)
                ClearItemContainer(npc.inventory.containerWear);
            if (hasBelt && npc.inventory.containerBelt != null)
                ClearItemContainer(npc.inventory.containerBelt);
            if (hasWear && npc.inventory.containerWear != null)
            {
                foreach (NpcWear wearItem in wearItems)
                {
                    if (wearItem == null || string.IsNullOrEmpty(wearItem.ShortName)) continue;
                    Item item = ItemManager.CreateByName(wearItem.ShortName, 1, wearItem.SkinID);
                    if (item != null)
                    {
                        npc.inventory.GiveItem(item, npc.inventory.containerWear);
                        item.MarkDirty();
                    }
                }
            }
            if (hasBelt && npc.inventory.containerBelt != null)
            {
                Item firstWeapon = null;
                int slotIndex = 0;
                foreach (NpcBelt beltItem in beltItems)
                {
                    if (beltItem == null || string.IsNullOrEmpty(beltItem.ShortName)) continue;
                    Item item = ItemManager.CreateByName(beltItem.ShortName, beltItem.Amount, beltItem.SkinID);
                    if (item == null) continue;
                    if (beltItem.Mods != null && beltItem.Mods.Count > 0 && item.contents != null)
                    {
                        foreach (string mod in beltItem.Mods)
                        {
                            if (string.IsNullOrEmpty(mod)) continue;
                            Item modItem = ItemManager.CreateByName(mod, 1, 0);
                            if (modItem != null && modItem.info != null)
                                item.contents.AddItem(modItem.info, 1);
                        }
                    }
                    string ammoType = beltItem.Ammo;
                    if (string.IsNullOrEmpty(ammoType) && beltItem.ShortName == "pistol.nailgun")
                        ammoType = "ammo.nailgun";
                    if (!string.IsNullOrEmpty(ammoType) && item.contents != null)
                    {
                        ItemDefinition ammoDef = ItemManager.FindItemDefinition(ammoType);
                        if (ammoDef != null)
                        {
                            int reserveAmmo = ammoDef.stackable > 0 ? Mathf.Min(ammoDef.stackable, 500) : 100;
                            item.contents.AddItem(ammoDef, reserveAmmo);
                        }
                    }
                    if (slotIndex == 0)
                    {
                        item.MoveToContainer(npc.inventory.containerBelt, 0);
                        firstWeapon = item;
                    }
                    else
                        npc.inventory.GiveItem(item, npc.inventory.containerBelt);
                    item.MarkDirty();
                    slotIndex++;
                }
                if (firstWeapon != null)
                {
                    npc.UpdateActiveItem(firstWeapon.uid);
                    BaseEntity heldEntity = firstWeapon.GetHeldEntity();
                    if (heldEntity is BaseProjectile baseProjectile)
                        baseProjectile.TopUpAmmo();
                }
            }
        }

        private void ScheduleNpcInventoryEquip(ScientistNPC npc, HashSet<NpcWear> wearItems, HashSet<NpcBelt> beltItems, string kit, string logName)
        {
            ScientistNPC npcRef = npc;
            timer.Once(0.2f, () =>
            {
                if (npcRef == null || npcRef.IsDestroyed) return;
                EquipNpcWearAndBelt(npcRef, wearItems, beltItems, logName);
                if (!string.IsNullOrEmpty(kit) && plugins.Exists("Kits"))
                    Interface.CallHook("GiveKit", npcRef, kit);
            });
        }

        private static void EnsureScientistNpcActiveAndDismounted(ScientistNPC npc)
        {
            if (npc == null) return;
            npc.IsDormant = false;
            npc.syncPosition = true;
            if (npc.isMounted)
                npc.DismountObject();
        }

        private void ApplyScientistBrainPostSpawn(ScientistNPC npc, ScientistBrain brain, ScientistBrainPostSpawnConfig cfg)
        {
            brain.AttackRangeMultiplier = cfg.AttackRangeMultiplier > 0 ? cfg.AttackRangeMultiplier : 1f;
            brain.MemoryDuration = cfg.MemoryDuration > 0 ? cfg.MemoryDuration : 10f;
            float senseEff = brain.SenseRange > 0 ? brain.SenseRange : (cfg.ConfigSenseRange > 0 ? cfg.ConfigSenseRange : 50f);
            brain.TargetLostRange = senseEff * 2f;
            float visionConeDeg = cfg.VisionConeDegrees > 0 ? cfg.VisionConeDegrees : 120f;
            brain.VisionCone = Vector3.Dot(Vector3.forward, Quaternion.Euler(0f, visionConeDeg, 0f) * Vector3.forward);
            brain.CheckVisionCone = cfg.CheckVisionCone;
            brain.CheckLOS = true;
            brain.IgnoreNonVisionSneakers = true;
            brain.MaxGroupSize = 0;
            brain.ListenRange = cfg.UseConfigSenseRangeForListenRange ? cfg.ConfigSenseRange / 2f : senseEff / 2f;
            brain.HostileTargetsOnly = false;
            brain.IgnoreSafeZonePlayers = false;
            brain.SenseTypes = EntityType.Player;
            brain.RefreshKnownLOS = false;
            brain.UseAIDesign = true;

            if (brain.Senses != null)
            {
                float senseForInit = cfg.UseBrainSenseRangeInSensesInit ? brain.SenseRange : senseEff;
                brain.Senses.Init(
                    npc,
                    brain,
                    brain.MemoryDuration,
                    senseForInit,
                    brain.TargetLostRange,
                    brain.VisionCone,
                    brain.CheckVisionCone,
                    true,
                    brain.IgnoreNonVisionSneakers,
                    brain.ListenRange,
                    brain.HostileTargetsOnly,
                    false,
                    brain.IgnoreSafeZonePlayers,
                    brain.SenseTypes,
                    brain.RefreshKnownLOS);
                brain.Senses.nextUpdateTime = 0f;
                brain.Senses.Update();
            }

            EnsureScientistNpcActiveAndDismounted(npc);
            brain.sleeping = false;
            brain.enabled = true;
            if (brain is IAISleepable sleepable)
                sleepable.WakeAI();

            if (brain.Navigator != null)
            {
                brain.Navigator.Speed = cfg.NavigatorSpeed > 0 ? cfg.NavigatorSpeed : 2f;
                brain.Navigator.MoveTowardsSpeed = BaseNavigator.NavigationSpeed.Fast;
                brain.Navigator.FaceMoveTowardsTarget = true;
                if (cfg.RoamRangeForNavigator > 0f)
                {
                    float r = Mathf.Max(5f, cfg.RoamRangeForNavigator);
                    brain.Navigator.MaxRoamDistanceFromHome = r;
                    brain.Navigator.BestRoamPointMaxDistance = Mathf.Clamp(r * 0.75f, 10f, 50f);
                    if (brain.Events?.Memory?.Position != null)
                        brain.Events.Memory.Position.Set(npc.transform.position, 4);
                }
            }
        }

        private void ScheduleScientistPostSpawnNextTick(
            ScientistNPC npc,
            string displayName,
            ScientistBrainPostSpawnConfig brainCfg,
            bool disableRadio,
            HashSet<NpcWear> wearItems,
            HashSet<NpcBelt> beltItems,
            string kit,
            string equipLogName)
        {
            ScientistNPC cap = npc;
            string nameCap = displayName;
            ScientistBrainPostSpawnConfig cfg = brainCfg;
            bool radio = disableRadio;
            HashSet<NpcWear> wear = wearItems;
            HashSet<NpcBelt> belt = beltItems;
            string kitCap = kit;
            string equipName = equipLogName;

            NextTick(() =>
            {
                if (cap == null || cap.IsDestroyed) return;
                if (!string.IsNullOrEmpty(nameCap) && cap.displayName != nameCap)
                    cap.displayName = nameCap;
                EnsureScientistNpcActiveAndDismounted(cap);
                ScientistBrain brain = cap.GetComponent<ScientistBrain>();
                if (brain != null)
                    ApplyScientistBrainPostSpawn(cap, brain, cfg);
                if (radio && cap.inventory != null && cap.inventory.containerBelt != null)
                {
                    ItemDefinition radioDef = ItemManager.FindItemDefinition("radio");
                    Item radioItem = radioDef != null ? cap.inventory.containerBelt.FindItemByItemID(radioDef.itemid) : null;
                    radioItem?.Remove();
                }
                ScheduleNpcInventoryEquip(cap, wear, belt, kitCap, equipName);
            });
        }

        private ScientistNPC SpawnBossDirectly(Vector3 position, NpcConfig config, bool terrainRoamSpawn)
        {
            if (config == null) return null;
            ScientistNPC npc = CreateScientistNpcEntity(position);
            if (npc == null) return null;
            npc.startHealth = config.Health;
            npc._health = config.Health;
            npc.displayName = config.Name;
            npc.damageScale = config.DamageScale;
            RegisterAndSpawnBoss(npc, config, position, terrainRoamSpawn);
            if (npc.IsDestroyed) return null;

            var brainCfg = new ScientistBrainPostSpawnConfig
            {
                AttackRangeMultiplier = config.AttackRangeMultiplier,
                MemoryDuration = config.MemoryDuration,
                ConfigSenseRange = config.SenseRange,
                VisionConeDegrees = config.VisionCone,
                NavigatorSpeed = config.Speed,
                CheckVisionCone = config.CheckVisionCone,
                UseBrainSenseRangeInSensesInit = true,
                UseConfigSenseRangeForListenRange = true,
                RoamRangeForNavigator = config.RoamRange > 0f ? config.RoamRange : 25f
            };
            ScheduleScientistPostSpawnNextTick(npc, config.Name, brainCfg, config.DisableRadio, config.WearItems, config.BeltItems, config.Kit, config.Name);
            return npc;
        }

        private ScientistNPC SpawnHelperNpc(Vector3 position, AddNpcConfig config, bool terrainRoamSpawn, int parentTypeNavMesh)
        {
            if (config == null) return null;
            string helperName = config.Names != null && config.Names.Count > 0 ? config.Names.GetRandom() : "Helper NPC";
            ScientistNPC npc = CreateScientistNpcEntity(position);
            if (npc == null) return null;
            npc.startHealth = config.Health;
            npc._health = config.Health;
            npc.displayName = helperName;
            npc.damageScale = config.DamageScale;
            RegisterAndSpawnHelper(npc, config, helperName, position, terrainRoamSpawn, parentTypeNavMesh);
            if (npc.IsDestroyed) return null;

            float helperRoam = config.RoamRange > 0f ? config.RoamRange : 25f;
            var brainCfg = new ScientistBrainPostSpawnConfig
            {
                AttackRangeMultiplier = config.AttackRangeMultiplier,
                MemoryDuration = config.MemoryDuration,
                ConfigSenseRange = config.SenseRange,
                VisionConeDegrees = config.VisionCone,
                NavigatorSpeed = config.Speed,
                CheckVisionCone = config.CheckVisionCone,
                UseBrainSenseRangeInSensesInit = false,
                UseConfigSenseRangeForListenRange = false,
                RoamRangeForNavigator = helperRoam
            };
            ScheduleScientistPostSpawnNextTick(npc, helperName, brainCfg, config.DisableRadio, config.WearItems, config.BeltItems, config.Kit, helperName);
            return npc;
        }

        /// <summary>
        /// NpcSpawn-style helper placement: snap within 6m of the desired ring around the boss and require a complete navmesh path from the boss.
        /// Reduces spawns on wrong mesh islands and helpers that immediately pathfail or hug walls.
        /// </summary>
        private Vector3 ResolveHelperSpawnNavMesh(Vector3 desired, Vector3 pathFrom, bool forceTerrainNav, int parentTypeNavMesh)
        {
            bool autoHelperMonument = !forceTerrainNav && parentTypeNavMesh == 0
                && IsPositionInAnyMonumentBounds(desired)
                && NavMesh.SamplePosition(desired, out _, 20f, NavAreaMonument);
            bool helperMonument = !forceTerrainNav && (parentTypeNavMesh != 0 || autoHelperMonument);
            int areaMask = helperMonument ? NavAreaMonument : NavAreaTerrain;
            const float sampleR = 6f;

            Vector3 pathStart = pathFrom;
            if (NavMesh.SamplePosition(pathFrom, out NavMeshHit startHit, 5f, areaMask))
                pathStart = startHit.position;

            for (int attempt = 0; attempt < 6; attempt++)
            {
                Vector3 probe = desired;
                if (attempt > 0)
                    probe += new Vector3(UnityEngine.Random.Range(-2.2f, 2.2f), 0f, UnityEngine.Random.Range(-2.2f, 2.2f));

                if (!NavMesh.SamplePosition(probe, out NavMeshHit hit, sampleR, areaMask))
                    continue;

                NavMeshPath path = new NavMeshPath();
                if (!NavMesh.CalculatePath(pathStart, hit.position, areaMask, path))
                    continue;
                if (path.status != NavMeshPathStatus.PathComplete)
                    continue;
                return hit.position;
            }

            return Vector3.zero;
        }

        private void FinalizeBossSpawn(ScientistNPC npc, NpcConfig config, bool terrainRoamSpawn, bool announceAndPve = true)
        {
            if (npc == null) return;
            ScientistNPC npcCap = npc;
            NextTick(() => TrySnapBossSpawnToWalkableNav(npcCap));

            ControllerBoss controller = npc.gameObject.AddComponent<ControllerBoss>();
            controller.InitData(config, terrainRoamSpawn);
            _controllers.Add(npc.net.ID.Value, controller);
            if (announceAndPve)
            {
                if (_config.Pve && plugins.Exists("PveMode")) PveMode.Call("ScientistAddPveMode", npc);
                if (config.IsChat) AlertToAllPlayers("Start", _config.Prefix, config.Name, MapHelper.GridToString(MapHelper.PositionToGrid(npc.transform.position)));
            }
            Interface.Oxide.CallHook("OnBossSpawn", npc);
        }

        private BasePlayer GetCurrentTarget(ScientistNPC npc)
        {
            if (npc == null) return null;
            ScientistBrain brain = npc.GetComponent<ScientistBrain>();
            if (brain == null) return null;
            if (brain.Events != null && brain.Events.Memory != null && brain.Events.CurrentInputMemorySlot >= 0)
            {
                BaseEntity t = brain.Events.Memory.Entity?.Get(brain.Events.CurrentInputMemorySlot);
                if (t is BasePlayer player && npc.CanSeeTarget(player))
                    return player;
            }
            if (_scientistNpcGetBestTarget != null)
            {
                BaseEntity tEnt = _scientistNpcGetBestTarget.Invoke(npc, null) as BaseEntity;
                if (tEnt is BasePlayer player2 && npc.CanSeeTarget(player2))
                    return player2;
            }
            return null;
        }

        /// <summary>
        /// Target for combat/movement cycles: strict LOS first (same as <see cref="GetCurrentTarget"/>), then any player in this boss&apos;s
        /// proximity sphere that passes <see cref="ControllerBoss.CanEngagePlayerForMovement"/>. Prevents handing the brain to idle roam whenever
        /// <c>CanSeeTarget</c> flickers (boss runs to a random roam point and stares at a wall for seconds).
        /// </summary>
        private BasePlayer GetBossCombatTarget(ScientistNPC npc)
        {
            if (npc == null) return null;
            BasePlayer t = GetCurrentTarget(npc);
            if (t != null && t.IsExists()) return t;
            ulong net = npc.net?.ID.Value ?? 0;
            if (net == 0 || !_controllers.TryGetValue(net, out ControllerBoss ctrl)) return null;
            foreach (BasePlayer p in ctrl.Players)
            {
                if (p != null && ctrl.CanEngagePlayerForMovement(p)) return p;
            }
            return null;
        }

        private static Vector3 SampleRandomTerrainPosition()
        {
            float half = TerrainMeta.Size.x * 0.45f;
            var p = new Vector3(UnityEngine.Random.Range(-half, half), 0f, UnityEngine.Random.Range(-half, half));
            p.y = TerrainMeta.HeightMap.GetHeight(p);
            return p;
        }

        private static bool SnapSpawnToNavMesh(ref Vector3 pos, float maxDistance = 80f)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(pos, out hit, maxDistance, NavAreaTerrain))
            {
                pos = hit.position;
                return true;
            }
            return false;
        }

        /// <summary>Horizontal ray from spawn: reject nav points that require passing through building colliders.</summary>
        private static bool LineClearSpawnNoBuildings(Vector3 from, Vector3 to)
        {
            Vector3 flat = to - from;
            flat.y = 0f;
            float d = flat.magnitude;
            if (d < 0.06f) return true;
            Vector3 dir = flat / d;
            const int layerMask = 10551552;
            Vector3 start = from + Vector3.up * 0.55f;
            if (!Physics.Raycast(start, dir, out RaycastHit h, d + 0.45f, layerMask))
                return true;
            float hitDist = new Vector3(h.point.x - from.x, 0f, h.point.z - from.z).magnitude;
            if (hitDist >= d * 0.88f) return true;
            BaseEntity hitEntity = h.collider?.GetComponentInParent<BaseEntity>();
            if (hitEntity == null) return true;
            if (hitEntity is BuildingBlock || hitEntity is SimpleBuildingBlock) return false;
            string pn = hitEntity.ShortPrefabName?.ToLowerInvariant() ?? "";
            return !pn.Contains("gate") && !pn.Contains("door") && !pn.Contains("prison");
        }

        /// <summary>
        /// Pulls boss off embedded monument geometry / wrong layer onto walkable nav after Grimm spawn init.
        /// </summary>
        private void TrySnapBossSpawnToWalkableNav(ScientistNPC npc)
        {
            if (npc == null || npc.IsDestroyed) return;
            BaseNavigator navigator = npc.Brain?.Navigator;
            if (navigator == null) return;

            Vector3 p = npc.transform.position;
            Vector3 best = p;
            float bestD = float.MaxValue;
            bool found = false;

            void ConsiderWalkable(Vector3 cand, bool requireLineClear)
            {
                if (requireLineClear && !LineClearSpawnNoBuildings(p, cand)) return;
                float dd = new Vector3(cand.x - p.x, 0f, cand.z - p.z).magnitude;
                if (dd < bestD && dd < 28f)
                {
                    bestD = dd;
                    best = cand;
                    found = true;
                }
            }

            for (int ring = 0; ring < 2; ring++)
            {
                float r = ring == 0 ? 2.5f : 5.5f;
                for (int i = 0; i < 8; i++)
                {
                    float ang = (Mathf.PI * 2f / 8f) * i;
                    Vector3 tryPos = p + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                    if (NavMesh.SamplePosition(tryPos, out NavMeshHit hitM, 18f, NavAreaMonument))
                        ConsiderWalkable(hitM.position, true);
                    if (NavMesh.SamplePosition(tryPos, out NavMeshHit hitT, 18f, NavAreaTerrain))
                        ConsiderWalkable(hitT.position, true);
                }
            }

            if (!found)
            {
                if (NavMesh.SamplePosition(p, out NavMeshHit hitM, 22f, NavAreaMonument))
                    ConsiderWalkable(hitM.position, false);
                if (!found && NavMesh.SamplePosition(p, out NavMeshHit hitT, 30f, NavAreaTerrain))
                    ConsiderWalkable(hitT.position, false);
                if (!found && NavMesh.SamplePosition(p, out NavMeshHit hitW, 40f, NavAreaTerrain))
                    ConsiderWalkable(hitW.position, false);
                if (!found && NavMesh.SamplePosition(p, out NavMeshHit hitW2, 40f, NavAreaMonument))
                    ConsiderWalkable(hitW2.position, false);
            }

            if (!found) return;

            if (bestD > 0.25f)
                npc.transform.position = best;

            navigator.PlaceOnNavMesh(10f);
        }
        #endregion GrimmNPC Integration

        #region Boss combat helpers (onhold parity)
        private static IEnumerable<MonumentInfo> EnumerateMonumentsContaining(Vector3 position, bool requireNavmesh)
        {
            if (TerrainMeta.Path?.Monuments == null) yield break;
            foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
            {
                if (monument == null || !monument.IsInBounds(position)) continue;
                if (requireNavmesh && !monument.HasNavmesh) continue;
                yield return monument;
            }
        }

        private bool IsPositionOnMonument(Vector3 position)
        {
            foreach (MonumentInfo _ in EnumerateMonumentsContaining(position, true))
                return true;
            NavMeshHit hit;
            return NavMesh.SamplePosition(position, out hit, 5f, NavAreaMonument);
        }

        private bool IsPositionInAnyMonumentBounds(Vector3 position)
        {
            foreach (MonumentInfo _ in EnumerateMonumentsContaining(position, false))
                return true;
            return false;
        }

        private int GetPreferredHumanoidAgentTypeId()
        {
            int id = BaseNavigator.GetNavMeshAgentID("Humanoid");
            if (id != -1 && id != 0) return id;
            return NavAgentTerrain;
        }

        private bool ResolveMonumentNavMeshAgentType(Vector3 position, out int agentTypeId)
        {
            agentTypeId = 0;
            if (TerrainMeta.Path?.Monuments == null) return false;
            MonumentNavMesh chosen = null;
            float bestScore = float.MaxValue;
            foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
            {
                if (monument == null || !monument.HasNavmesh) continue;
                foreach (MonumentNavMesh mnm in monument.GetComponentsInChildren<MonumentNavMesh>(true))
                {
                    if (mnm == null) continue;
                    Bounds b = mnm.GetBounds();
                    Vector3 c = b.ClosestPoint(position);
                    float dist = Vector3.Distance(c, position);
                    if (dist > 200f) continue;
                    float score = dist + (monument.IsInBounds(position) ? 0f : 800f);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        chosen = mnm;
                    }
                }
            }
            if (chosen == null) return false;
            agentTypeId = NavMesh.GetSettingsByIndex(chosen.NavMeshAgentTypeIndex).agentTypeID;
            return agentTypeId != 0;
        }

        private int DetectAgentTypeBySampling(Vector3 position)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(position, out hit, 10f, NavAreaMonument))
            {
                if (ResolveMonumentNavMeshAgentType(hit.position, out int resolved) && resolved != 0)
                    return resolved;
                for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
                {
                    int aid = NavMesh.GetSettingsByIndex(i).agentTypeID;
                    if (aid != 0) return aid;
                }
            }
            if (NavMesh.SamplePosition(position, out hit, 10f, NavAreaTerrain))
                return NavAgentTerrain;
            return NavAgentTerrain;
        }

        private int FinalizeMonumentAgentTypeId(int candidate, Vector3 position)
        {
            if (candidate != 0) return candidate;
            if (ResolveMonumentNavMeshAgentType(position, out int sampled) && sampled != 0) return sampled;
            return GetPreferredHumanoidAgentTypeId();
        }

        private int GetMonumentAgentTypeID(Vector3 position)
        {
            if (TerrainMeta.Path?.Monuments == null) return GetPreferredHumanoidAgentTypeId();
            foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
            {
                if (monument == null || !monument.IsInBounds(position) || !monument.HasNavmesh) continue;
                MonumentNavMesh monumentNavMesh = monument.GetComponentInChildren<MonumentNavMesh>();
                if (monumentNavMesh != null)
                {
                    int agentTypeID = NavMesh.GetSettingsByIndex(monumentNavMesh.NavMeshAgentTypeIndex).agentTypeID;
                    if (agentTypeID != 0) return agentTypeID;
                    if (ResolveMonumentNavMeshAgentType(position, out int resolved) && resolved != 0)
                        return resolved;
                    return FinalizeMonumentAgentTypeId(0, position);
                }
                if (ResolveMonumentNavMeshAgentType(position, out int resolved2) && resolved2 != 0)
                    return resolved2;
                return FinalizeMonumentAgentTypeId(0, position);
            }
            if (ResolveMonumentNavMeshAgentType(position, out int globalResolved) && globalResolved != 0)
                return globalResolved;
            return FinalizeMonumentAgentTypeId(DetectAgentTypeBySampling(position), position);
        }

        /// <param name="allowCombatMemoryWithoutLos">
        /// When false (default), skips <see cref="PushBrainCombatTargetIntoEventMemory"/> unless <c>npc.CanSeeTarget(target)</c>.
        /// Forcing event memory + Chase without real LOS fights GrimmNPC&apos;s lost-target investigate (empty/wrong event slot while player is near).
        /// Use true for helper NPCs that must engage before they have a clean sense tick.
        /// </param>
        private void SetTarget(ScientistNPC npc, BasePlayer target, bool allowCombatMemoryWithoutLos = false)
        {
            if (npc == null || target == null) return;
            ScientistBrain brain = npc.GetComponent<ScientistBrain>();
            if (brain?.Senses == null) return;

            if (_grimmNpcSetKnownMethod != null)
                _grimmNpcSetKnownMethod.Invoke(null, new object[] { npc, target });
            else if (_scientistNpcSetKnown != null)
                _scientistNpcSetKnown.Invoke(npc, new object[] { target });
            else if (brain.Senses.Memory != null)
            {
                brain.Senses.Memory.SetKnown(target, npc, brain.Senses);
                if (npc.CanSeeTarget(target))
                    brain.Senses.Memory.SetLOS(target, true);
            }

            if (allowCombatMemoryWithoutLos || npc.CanSeeTarget(target))
                PushBrainCombatTargetIntoEventMemory(brain, target);
        }

        /// <summary>
        /// Re-applies navigator + target after GrimmNPC async navmesh enable (batch-spawned helpers often miss the first tick).
        /// </summary>
        private void KickHelperNpcEngagement(ScientistNPC helper, BasePlayer target)
        {
            if (helper == null || helper.IsDestroyed) return;
            try
            {
                if (helper.Brain?.Navigator != null)
                {
                    helper.Brain.Navigator.Resume();
                    helper.Brain.Navigator.SetNavMeshEnabled(true);
                    if (helper.NavAgent != null && helper.NavAgent.enabled && !helper.NavAgent.isOnNavMesh)
                        helper.Brain.Navigator.PlaceOnNavMesh(6f);
                }
                if (target != null)
                    SetTarget(helper, target, allowCombatMemoryWithoutLos: true);
            }
            catch
            {
                // ignore — entity may be mid-destroy
            }
        }

        private static void PushBrainCombatTargetIntoEventMemory(ScientistBrain brain, BasePlayer target)
        {
            if (brain?.Events?.Memory?.Entity == null || target == null) return;
            void SetAtSlot(int slot)
            {
                if (slot >= 0 && slot < 8)
                    brain.Events.Memory.Entity.Set(target, slot);
            }
            bool passive = brain.CurrentState == null
                || brain.CurrentState.StateType == AIState.Roam
                || brain.CurrentState.StateType == AIState.Idle;
            if (passive && brain.AIDesign != null && brain.HasState(AIState.Chase))
            {
                AIStateContainer chase = brain.AIDesign.GetFirstStateContainerOfType(AIState.Chase);
                if (chase != null)
                    brain.SwitchToState(AIState.Chase, chase.ID);
            }
            if (brain.AIDesign != null)
            {
                foreach (AIState st in new AIState[] { AIState.Chase, AIState.Combat, AIState.Attack })
                {
                    AIStateContainer c = brain.AIDesign.GetFirstStateContainerOfType(st);
                    if (c != null)
                        SetAtSlot(c.InputMemorySlot);
                }
            }
            SetAtSlot(brain.Events.CurrentInputMemorySlot);
            if (brain.Events.CurrentInputMemorySlot >= 0
                && brain.Events.Memory.Entity.Get(brain.Events.CurrentInputMemorySlot) == null)
                SetAtSlot(0);
        }

        private Vector3 GetTeleportGroundSurface(Vector3 desired, Vector3 referencePosition)
        {
            Vector3 structTop;
            if (GetStructureTop(desired, out structTop))
                return structTop + Vector3.up * 0.08f;
            float terrainH = TerrainMeta.HeightMap.GetHeight(desired);
            float rayOriginY = Mathf.Max(desired.y, referencePosition.y, terrainH) + 45f;
            RaycastHit hit;
            Vector3 rayStart = new Vector3(desired.x, rayOriginY, desired.z);
            if (Physics.Raycast(rayStart, Vector3.down, out hit, 130f, LayerMask.GetMask("Terrain", "World", "Construction")))
                return hit.point + Vector3.up * 0.08f;
            return new Vector3(desired.x, terrainH + 0.12f, desired.z);
        }

        internal Vector3 SnapBossTeleportPosition(ScientistNPC npc, Vector3 worldPos, Vector3 referencePosition)
        {
            if (npc == null) return worldPos;
            Vector3 surface = GetTeleportGroundSurface(worldPos, referencePosition);
            NavMeshAgent agent = npc.NavAgent;
            if (agent == null) return surface;
            int mask = agent.areaMask;
            NavMeshHit nmh;
            if (NavMesh.SamplePosition(surface, out nmh, 5f, mask))
            {
                if (nmh.position.y >= surface.y - 2.5f && nmh.position.y <= surface.y + 3.5f)
                    return nmh.position;
            }
            if (NavMesh.SamplePosition(surface, out nmh, 10f, NavMesh.AllAreas))
            {
                if (nmh.position.y >= surface.y - 2.5f && nmh.position.y <= surface.y + 3.5f)
                    return nmh.position;
            }
            return surface;
        }

        private const int ConstructionLayerMaskBoss = 1 << 21;

        internal bool GetStructureTop(Vector3 desired, out Vector3 topPos)
        {
            float upRange = 20f;
            float downRange = 20f;
            bool Ray(Vector3 rayOrigin, float distance, out Vector3 best)
            {
                best = Vector3.zero;
                RaycastHit[] hits = Physics.RaycastAll(rayOrigin, Vector3.down, distance, ConstructionLayerMaskBoss, QueryTriggerInteraction.Ignore);
                if (hits == null || hits.Length == 0) return false;
                float bestDy = float.PositiveInfinity;
                float bestDyBelow = float.PositiveInfinity;
                Vector3 bestBelow = Vector3.zero;
                for (int i = 0; i < hits.Length; i++)
                {
                    var blk = hits[i].GetEntity() as BuildingBlock;
                    if (blk == null) continue;
                    string spn = blk.ShortPrefabName ?? string.Empty;
                    if (spn != "foundation" && spn != "foundation.triangle" && spn != "floor" && spn != "floor.triangle") continue;
                    float dy = Mathf.Abs(hits[i].point.y - desired.y);
                    if (dy < bestDy)
                    {
                        bestDy = dy;
                        best = hits[i].point;
                    }
                    if (hits[i].point.y <= desired.y + 0.25f && dy < bestDyBelow)
                    {
                        bestDyBelow = dy;
                        bestBelow = hits[i].point;
                    }
                }
                if (bestDyBelow != float.PositiveInfinity)
                {
                    best = bestBelow;
                    return true;
                }
                return bestDy != float.PositiveInfinity;
            }
            Vector3 origin = new Vector3(desired.x, desired.y + upRange, desired.z);
            if (Ray(origin, upRange + downRange, out Vector3 bestPoint))
            {
                topPos = bestPoint + Vector3.up * 0.05f;
                return true;
            }
            float[] offsets = new float[] { 0f, 0.75f, -0.75f, 1.5f, -1.5f };
            float bestDy2 = float.PositiveInfinity;
            Vector3 best2 = Vector3.zero;
            for (int ix = 0; ix < offsets.Length; ix++)
            {
                for (int iz = 0; iz < offsets.Length; iz++)
                {
                    Vector3 probe = new Vector3(desired.x + offsets[ix], desired.y + upRange, desired.z + offsets[iz]);
                    if (Ray(probe, upRange + downRange, out Vector3 cand))
                    {
                        float dy = Mathf.Abs(cand.y - desired.y);
                        if (dy < bestDy2)
                        {
                            bestDy2 = dy;
                            best2 = cand;
                        }
                    }
                }
            }
            if (bestDy2 != float.PositiveInfinity)
            {
                topPos = best2 + Vector3.up * 0.05f;
                return true;
            }
            topPos = Vector3.zero;
            return false;
        }
        #endregion Boss combat helpers

        #region Controller
        private readonly List<string> _whatSpawnBosses = new List<string>();

        private readonly Dictionary<ulong, ControllerBoss> _controllers = new Dictionary<ulong, ControllerBoss>();

        private void CheckSpawnBoss()
        {
            if (_controllers.Count >= _config.AmountBosses) return;
            int current = _controllers.Count;
            for (int i = 0; i < _config.AmountBosses - current; i++) SpawnRandomBoss();
        }

        private void SpawnRandomBoss()
        {
            if (_controllers.Count >= _config.AmountBosses || _whatSpawnBosses.Count == 0) return;

            string name = _whatSpawnBosses.GetRandom();
            _whatSpawnBosses.Remove(name);

            NpcConfig config = Configs.FirstOrDefault(x => x.Name == name);
            Vector3 pos = GetSpawnPos(config, out bool terrainRoamSpawn);

            if (pos == Vector3.zero || Interface.CallHook("CanBossSpawn", config.Name, pos) is bool)
            {
                timer.In(UnityEngine.Random.Range(config.MinTime, config.MaxTime), () =>
                {
                    _whatSpawnBosses.Add(name);
                    CheckSpawnBoss();
                });
                return;
            }

            ScientistNPC npc = SpawnBossDirectly(pos, config, terrainRoamSpawn);
            if (npc == null)
            {
                SpawnRandomBoss();
                return;
            }

            FinalizeBossSpawn(npc, config, terrainRoamSpawn);
        }

        private void SpawnBoss(NpcConfig config)
        {
            Vector3 pos = GetSpawnPos(config, out bool terrainRoamSpawn);

            if (pos == Vector3.zero || Interface.CallHook("CanBossSpawn", config.Name, pos) is bool) return;

            ScientistNPC npc = SpawnBossDirectly(pos, config, terrainRoamSpawn);
            if (npc == null) return;

            FinalizeBossSpawn(npc, config, terrainRoamSpawn);
        }

        internal class ControllerBoss : FacepunchBehaviour
        {
            internal ScientistNPC Npc;
            internal NpcConfig Config; // Store config reference for loot spawning
            internal string BossName; // Store boss name since displayName is read-only

            private MapMarkerGenericRadius _mapmarker;
            private VendingMachineMapMarker _vendingMarker;

            private float _maxHealth;
            private bool _canRunAwayWater;
            private int _typeNavMesh;

            internal RadiusActionsConfig radiusActions = null;
            /// <summary>True when JSON radius &gt; 0 (enables RadiusActions loop and ability radius checks against config value).</summary>
            private bool _abilityRadiusTriggersLoop = false;
            /// <summary>Sphere trigger radius for player proximity (never zero - at least SenseRange when abilities radius is 0).</summary>
            internal float ProximityPlayerRadius { get; private set; } = 40f;
            private readonly GameObject _customSphere = new GameObject();
            private int _timeToSpikes = -1;
            private int _timeToFire = -1;
            private int _timeToElectricShock = -1;
            private int _timeToWounded = -1;
            private int _timeToFreeze = -1;
            private int _timeToAnimal = -1;
            private int _timeToNpc = -1;
            private int _lastAbilityUsed = -1; // Track which ability was used last for rotation
            private float _attackCycleTimer = 0f; // 6-second attack cycle timer
            private float _nextRangedNavStuckRecoverRealtime;
            private bool _isStationary = false; // Flag to track when boss should remain stationary during AOE
            private bool _npcHelpersActive = false; // Freeze/immunity flag while spawned helper NPCs are alive
            private bool _pendingNpcHelpers = false; // Queue NPC helper spawn until after teleport
            private float _pendingNpcHelpersDuration = 0f;
            private bool _pendingAnimal = false; // Queue Animal ability until after teleport
            private float _pendingAnimalDespawn = 0f;
            /// <summary>Until this realtime, radius AOEs (spikes/fire/ice/electric), animals, and NPC helper waves are blocked so the boss does not instantly dump abilities after spawn.</summary>
            private float _postSpawnRadiusAbilityGraceEndsAt;
            private const float PostSpawnRadiusAbilityGraceSeconds = 10f;
            private readonly HashSet<int> _roundRobinUsed = new HashSet<int>(); // Rotation blocklist until all configured abilities are used
            private readonly HashSet<Barricade> _allSpikes = new HashSet<Barricade>();
            private readonly HashSet<BaseEntity> _warningCircles = new HashSet<BaseEntity>();
            private Coroutine _fireBallCoroutine = null;
            private Coroutine _electricShockCoroutine = null;
            private readonly HashSet<BasePlayer> _woundedPlayers = new HashSet<BasePlayer>();
            private Coroutine _freezeCoroutine = null;
            private readonly HashSet<IceFence> _allWalls = new HashSet<IceFence>();
            private readonly Dictionary<BasePlayer, Vector3> _freezePlayers = new Dictionary<BasePlayer, Vector3>();
			private Coroutine _strafeCoroutine = null;
            private float _strafeLegEndRealtime = 0f;
            private Coroutine _helperAggroCoroutine = null;
            /// <summary>Delayed nav/target refresh for batch-spawned helpers (GrimmNPC dynamic navmesh).</summary>
            private const float HelperEngagementKickDelay1 = 0.28f;
            private const float HelperEngagementKickDelay2 = 0.72f;
            private const float HelperEngagementKickDelay3 = 1.15f;
            private float _meleePressureEndRealtime = 0f;
            private float _aoeStandoffEndRealtime = 0f;
            private const float DefaultMeleeHoldDistance = 2.2f;
            private const float DefaultAoeStandoffDistance = 11f;
            /// <summary>Sentinel duration for strafe coroutines that should run until combat ends (see StartStrafe).</summary>
            private const float STRAFE_DURATION_CONTINUOUS = 999f;
            /// <summary>Max horizontal distance from player for combat teleports (invis path-fail relocate, phase teleports).</summary>
            private const float MaxCombatTeleportDistanceFromPlayer = 20f;
            internal HashSet<BaseAnimalNPC> Animals = new HashSet<BaseAnimalNPC>();
            internal HashSet<BaseEntity> AnimalsAny = new HashSet<BaseEntity>();
            internal HashSet<ScientistNPC> Scientists = new HashSet<ScientistNPC>();
            private bool _isShuttingDown = false;
            private readonly HashSet<int> _roundRobinUniverse = new HashSet<int>();
            private readonly HashSet<int> _abilityPool = new HashSet<int>();
			// Anti face-tank bucket
			private float _recentDamageBucket = 0f;
			private float _recentDamageBucketExpiresAt = 0f;
            private float _nextNavRecoverRealtime = 0f;

            private TakeDamageActionsConfig _takeDamageActions = null;

            internal HashSet<BasePlayer> Players = new HashSet<BasePlayer>();

            internal Vector3 SpawnPosition { get; private set; } // Boss spawn position for loot spawning
            private Vector3 _homePosition;
            private int _timeToInvis = 5;
            private int _timeToGoHome = 0;
            private int _timeToGhost = 3;
            private bool _returnToSpawnPoint = false;
            private float _returnToSpawnPointInterval = 0f;
            private float _returnToSpawnPointTimer = 0f;
            private bool _isOnMonument = false; // Track if boss spawned on monument (persistent flag)
            /// <summary>Spawn came from GetSpawnPos section 3 (random map / emergency) - keep terrain NavMesh; do not force monument agent/mask.</summary>
            internal bool TerrainRoamSpawn;

            // Optional confinement bounds
            private bool _useBounds = false;
            private Vector3 _boundsMin = Vector3.zero;
            private Vector3 _boundsMax = Vector3.zero;

            internal void SetConfineBounds(Vector3 min, Vector3 max)
            {
                _useBounds = true;
                _boundsMin = new Vector3(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Min(min.z, max.z));
                _boundsMax = new Vector3(Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y), Mathf.Max(min.z, max.z));
            }

            private Vector3 ClampToBounds(Vector3 pos, float margin = 0.25f)
            {
                if (!_useBounds) return pos;
                return new Vector3(
                    Mathf.Clamp(pos.x, _boundsMin.x + margin, _boundsMax.x - margin),
                    pos.y,
                    Mathf.Clamp(pos.z, _boundsMin.z + margin, _boundsMax.z - margin)
                );
            }

            private Vector3 SampleInsideBounds(Vector3 desired)
            {
                Vector3 clamped = ClampToBounds(desired);
                NavMeshHit hit;
                if (NavMesh.SamplePosition(clamped, out hit, 3f, Npc.NavAgent.areaMask))
                {
                    return ClampToBounds(hit.position);
                }
                for (int i = 0; i < 6; i++)
                {
                    Vector3 rnd = new Vector3(UnityEngine.Random.Range(_boundsMin.x, _boundsMax.x), clamped.y, UnityEngine.Random.Range(_boundsMin.z, _boundsMax.z));
                    if (NavMesh.SamplePosition(rnd, out hit, 3f, Npc.NavAgent.areaMask))
                    {
                        return ClampToBounds(hit.position);
                    }
                }
                return clamped;
            }

            private void Awake() { Npc = GetComponent<ScientistNPC>(); }

            internal void InitData(NpcConfig config, bool terrainRoamSpawn = false)
            {
                TerrainRoamSpawn = terrainRoamSpawn;
                Config = config; // Store config reference for later retrieval
                BossName = config.Name; // Store boss name since displayName is read-only and always returns "Scientist"
                if (config.Marker.IsMarker) SpawnMapMarker(config.Marker);

                _maxHealth = config.Health;
                _canRunAwayWater = config.CanRunAwayWater;

                _typeNavMesh = config.TypeNavMesh;

                radiusActions = config.RadiusActions;
                _abilityRadiusTriggersLoop = radiusActions != null && radiusActions.Radius > 0f;
                float sense = config.SenseRange > 0f ? config.SenseRange : 50f;
                ProximityPlayerRadius = (_abilityRadiusTriggersLoop && radiusActions != null) ? radiusActions.Radius : Mathf.Max(sense, 35f);

                _customSphere.name = $"BossProximity_{BossName}";
                _customSphere.transform.SetParent(transform, false);
                _customSphere.transform.localPosition = Vector3.zero;
                _customSphere.AddComponent<CustomSphereCollider>().InitData(this, ProximityPlayerRadius);

                if (_abilityRadiusTriggersLoop)
                {
                    // Convert seconds to 0.5s ticks for all timers that are enabled (>0)
                    _timeToSpikes = radiusActions.TimeToSpikes > 0 ? radiusActions.TimeToSpikes * 2 : radiusActions.TimeToSpikes;
                    _timeToFire = radiusActions.TimeToFire > 0 ? radiusActions.TimeToFire * 2 : radiusActions.TimeToFire;
                    _timeToElectricShock = radiusActions.TimeToElectricShock > 0 ? radiusActions.TimeToElectricShock * 2 : radiusActions.TimeToElectricShock;
                    _timeToWounded = radiusActions.TimeToWounded > 0 ? radiusActions.TimeToWounded * 2 : radiusActions.TimeToWounded;
                    _timeToFreeze = radiusActions.TimeToFreeze > 0 ? radiusActions.TimeToFreeze * 2 : radiusActions.TimeToFreeze;
                    // Animals/NPCs: allow immediate first-cast (no initial cooldown)
                    _timeToAnimal = radiusActions.AnimalAbility != null && radiusActions.AnimalAbility.Time > 0 ? 0 : (radiusActions.AnimalAbility?.Time ?? -1);
                    _timeToNpc = radiusActions.NpcAbility != null && radiusActions.NpcAbility.Time > 0 ? 0 : (radiusActions.NpcAbility?.Time ?? -1);

                    _ins.DebugLog($"[{BossName}] Timers init (ticks): spikes={_timeToSpikes}, fire={_timeToFire}, freeze={_timeToFreeze}, electric={_timeToElectricShock}, animals={_timeToAnimal}, npcs={_timeToNpc}", spawnInit: true);

                    // Build fixed ability pool from config (1=Spikes,2=Fire,3=Ice,4=Electric,5=Animals,6=NPC)
                    _abilityPool.Clear();
                    if (radiusActions.TimeToSpikes != -1) _abilityPool.Add(1);
                    if (radiusActions.TimeToFire != -1) _abilityPool.Add(2);
                    if (radiusActions.TimeToFreeze != -1) _abilityPool.Add(3);
                    if (radiusActions.TimeToElectricShock != -1) _abilityPool.Add(4);
                    if (radiusActions.AnimalAbility != null && radiusActions.AnimalAbility.Time != -1) _abilityPool.Add(5);
                    if (radiusActions.NpcAbility != null && radiusActions.NpcAbility.Time != -1) _abilityPool.Add(6);
                    _ins.DebugLog($"[{BossName}] Ability pool: {string.Join(",", _abilityPool)}", spawnInit: true);

                    InvokeRepeating(RadiusActions, 0.5f, 0.5f); // Check AOE abilities twice per second for more frequent attacks
                }

                if (radiusActions != null && radiusActions.Radiation > 0f) InitRadiation(radiusActions.Radiation);
                if (radiusActions != null && radiusActions.Temperature != 0f) InitTemperature(radiusActions.Temperature);

                if (!config.TakeDamageActions.IsDisable) _takeDamageActions = config.TakeDamageActions;

                _homePosition = Npc.transform.position;
                SpawnPosition = Npc.transform.position; // Store spawn position for loot spawning
                _postSpawnRadiusAbilityGraceEndsAt = Time.realtimeSinceStartup + PostSpawnRadiusAbilityGraceSeconds;
                if (config.UseInvisible) InvokeRepeating(CheckInvisible, 1f, 1f);
                
                // NOTE: Navmesh configuration is handled by GrimmNPC during spawn
                // Map fallback bosses (full-map roam) register terrain NavMesh only; IsPositionOnMonument uses mask-25 sampling
                // and is unsafe for "anywhere on map" spawns (HumanNPC areas exist outside monument OBBs).
                bool hasMonumentPositions = config.Monuments != null && config.Monuments.Count > 0;
                bool isInMonumentBounds = _ins.IsPositionInAnyMonumentBounds(Npc.transform.position);
                if (terrainRoamSpawn)
                {
                    _isOnMonument = false;
                    if (_ins._config != null && _ins._config.Debug)
                    {
                        _ins.DebugLog($"[{BossName}] Map fallback spawn - terrain NavMesh registration (monument late-fix and monument-only warnings disabled).", spawnInit: true);
                    }
                }
                else
                {
                    _isOnMonument = _ins.IsPositionOnMonument(Npc.transform.position) || (hasMonumentPositions && isInMonumentBounds);
                    if (_isOnMonument && _ins._config != null && _ins._config.Debug)
                    {
                        _ins.DebugLog($"[{BossName}] Boss detected on monument (GrimmNPC will handle navmesh configuration)", spawnInit: true);
                    }
                }

                if (_isOnMonument)
                    Invoke(nameof(LateMonumentNavPlace), 2.5f);
                
                // Initialize return to spawn point settings
                _returnToSpawnPoint = config.ReturnToSpawnPoint;
                _returnToSpawnPointInterval = config.ReturnToSpawnPointInterval;
                _returnToSpawnPointTimer = 0f;
                if (_returnToSpawnPoint && _returnToSpawnPointInterval > 0f)
                {
                    InvokeRepeating(nameof(CheckReturnToSpawnPoint), 1f, 1f);
                    _ins.DebugLog($"[{BossName}] Return to spawn point enabled: interval={_returnToSpawnPointInterval}s", spawnInit: true);
                }
                
                // Start the 6-second attack cycle system for all bosses
                InvokeRepeating(nameof(AttackCycleManager), 1f, 1f);

                // GrimmNPC often enables NavMeshAgent asynchronously; early ticks can leave the boss paused/off-mesh until something else touches the navigator.
                Invoke(nameof(BootstrapBossCombatNavigation), 0.08f);
                Invoke(nameof(BootstrapBossCombatNavigation), 0.35f);
                Invoke(nameof(BootstrapBossCombatNavigation), 0.9f);

                // After GrimmNPC init + LateMonumentNavPlace (2.5s), log console warnings if nav still looks wrong (helps fix bad /SavePos).
                Invoke(nameof(LogSpawnNavMeshWarningIfNeeded), 2.75f);
            }

            /// <summary>Mirrors EndNpcHelpers navigator recovery so it runs on spawn, not only after helper waves.</summary>
            private void BootstrapBossCombatNavigation()
            {
                if (Npc == null || Npc.IsDestroyed || !Npc.IsExists()) return;
                try
                {
                    if (Npc.Brain?.Navigator != null)
                    {
                        Npc.Brain.Navigator.Resume();
                        Npc.Brain.Navigator.SetNavMeshEnabled(true);
                        Npc.Brain.Navigator.PlaceOnNavMesh(6f);
                    }
                    // Do not SetTarget here: early event-memory + Chase while senses/nav are still settling causes GrimmNPC
                    // "lost target; investigate" and wall-hugging while the player is actually present.
                    if (_ins._config != null && _ins._config.Debug && _ins._config.DebugBossBootstrap)
                    {
                        BasePlayer t = _ins.GetBossCombatTarget(Npc);
                        _ins.Puts($"[{BossName}] BootstrapBossCombatNavigation: Resume/PlaceOnNavMesh only (target left to GrimmNPC senses): would-be={(t != null ? t.displayName : "none")}");
                    }
                }
                catch
                {
                    // entity may be mid-destroy
                }
            }

            /// <summary>
            /// One-shot post-spawn check: prints to server console when NavMesh setup looks broken so admins can re-save spawn.
            /// </summary>
            private void LogSpawnNavMeshWarningIfNeeded()
            {
                if (Npc == null || !Npc.IsExists()) return;
                // Random map / open-terrain bosses: GrimmNPC uses terrain registration; agent may still be settling - skip false alarms.
                if (TerrainRoamSpawn)
                    return;
                NavMeshAgent agent = Npc.NavAgent;
                Vector3 pos = Npc.transform.position;

                bool nearMonumentMesh = false;
                NavMeshHit meshHit;
                if (NavMesh.SamplePosition(pos, out meshHit, 5f, NavAreaMonument))
                    nearMonumentMesh = true;
                bool monumentContext = !TerrainRoamSpawn && (_isOnMonument || nearMonumentMesh);

                List<string> issues = new List<string>(6);
                if (agent == null)
                    issues.Add("NavMeshAgent missing");
                else
                {
                    if (!agent.enabled)
                        issues.Add("NavMeshAgent disabled");
                    if (!agent.isOnNavMesh)
                        issues.Add("not on NavMesh");
                    if (monumentContext)
                    {
                        if (agent.areaMask != NavAreaMonument)
                            issues.Add($"AreaMask={agent.areaMask} (want {NavAreaMonument} on monument)");
                        if (agent.agentTypeID == 0)
                            issues.Add("AgentTypeID=0 (monument agent not resolved)");
                    }
                }

                if (issues.Count == 0) return;

                string hint = monumentContext
                    ? $"Fix: stand on valid ground and run /SavePos {BossName} (or edit monument local position in Bosses json)."
                    : "Fix: use /CustomPos or a position that snaps to NavMesh.";
                string extra = string.Empty;
                if (_ins._config != null && _ins._config.Debug)
                {
                    string inBounds = "none";
                    if (TerrainMeta.Path?.Monuments != null)
                    {
                        foreach (MonumentInfo mi in TerrainMeta.Path.Monuments)
                        {
                            if (mi == null)
                                continue;
                            if (mi.IsInBounds(pos))
                            {
                                inBounds = $"{mi.name} HasNavmesh={mi.HasNavmesh}";
                                break;
                            }
                        }
                    }
                    string grimm = "n/a";
                    if (_grimmNpcType != null && _customNpcDataType != null)
                    {
                        var getNpcData = _grimmNpcType.GetMethod("GetNpcData", BindingFlags.Public | BindingFlags.Static);
                        object gd = getNpcData?.Invoke(null, new object[] { Npc.net.ID.Value });
                        if (gd != null)
                        {
                            int gMask = (int)(_customNpcDataType.GetProperty("AreaMask")?.GetValue(gd) ?? 0);
                            int gAgent = (int)(_customNpcDataType.GetProperty("AgentTypeID")?.GetValue(gd) ?? 0);
                            bool locked = (bool)(_customNpcDataType.GetProperty("NavmeshLocked")?.GetValue(gd) ?? false);
                            grimm = $"GrimmNPC data AreaMask={gMask} AgentTypeID={gAgent} NavmeshLocked={locked}";
                        }
                    }
                    int wantAgent = _ins.GetMonumentAgentTypeID(pos);
                    extra = $" | DEBUG: InBounds={inBounds} | SampleMonument5m={nearMonumentMesh} | wantAgentType={wantAgent} | {grimm}";
                }
                _ins.PrintWarning($"SPAWN NAV - '{BossName}' @ {pos} | {string.Join(" | ", issues)} | {hint}{extra}");
            }

            // One-shot recovery if still off-mesh after GrimmNPC init (engine PlaceOnNavMesh only; no custom nav stepping).
            private void LateMonumentNavPlace()
            {
                if (Npc == null || !Npc.IsExists()) return;
                if (TerrainRoamSpawn)
                    return;
                var agent = Npc.NavAgent;
                if (agent == null) return;

                Vector3 pos = Npc.transform.position;
                bool nearMonumentMesh = NavMesh.SamplePosition(pos, out _, 5f, NavAreaMonument);
                bool monumentCtx = _isOnMonument || nearMonumentMesh;
                if (!monumentCtx)
                    return;

                int want = _ins.GetMonumentAgentTypeID(pos);
                if (want == 0 && _ins.ResolveMonumentNavMeshAgentType(pos, out int fromScene))
                    want = fromScene;
                if (want == 0)
                    want = _ins.GetPreferredHumanoidAgentTypeId();

                bool wrongMask = agent.areaMask != NavAreaMonument;
                bool wrongType = agent.agentTypeID == 0 || (want != 0 && agent.agentTypeID != want);
                bool needsPlacement = !agent.isOnNavMesh || !agent.enabled;

                if (!wrongMask && !wrongType && !needsPlacement)
                    return;

                if (want != 0 && agent.agentTypeID != want)
                    agent.agentTypeID = want;
                agent.areaMask = NavAreaMonument;
                agent.enabled = true;
                Npc.Brain?.Navigator?.PlaceOnNavMesh(18f);
            }

            private void OnDestroy()
            {
                if (_isShuttingDown) return;
                _isShuttingDown = true;
                
                // Unregister from GrimmNPC before destroying
                if (Npc != null && Npc.IsExists())
                {
                    _ins.UnregisterNpcFromGrimmNpc(Npc);
                }
                
                CancelInvoke(UpdateMapMarker);
                if (_mapmarker.IsExists()) _mapmarker.Kill();
                if (_vendingMarker.IsExists()) _vendingMarker.Kill();

                CancelInvoke(RadiusActions);
                if (_customSphere != null) Destroy(_customSphere);

                CancelInvoke(DeleteAllSpikes);
                DeleteAllSpikes();

                if (_fireBallCoroutine != null) ServerMgr.Instance.StopCoroutine(_fireBallCoroutine);

                if (_electricShockCoroutine != null) ServerMgr.Instance.StopCoroutine(_electricShockCoroutine);

                CancelInvoke(FinishWounded);
                FinishWounded();

                if (_freezeCoroutine != null) ServerMgr.Instance.StopCoroutine(_freezeCoroutine);
                DeleteAllWalls();

				CancelInvoke(DeleteAllAnimals);
                DeleteAllAnimals();

				CancelInvoke(DeleteAllScientists);
                DeleteAllScientists();
                EndNpcHelpers();

                DeleteAllWarningCircles();

                CancelInvoke(nameof(TeleportAwayAfterMelee));
                CancelInvoke(nameof(ResumeMovement));
                CancelInvoke(nameof(AttackCycleManager));
                CancelInvoke(nameof(LateMonumentNavPlace));
                CancelInvoke(nameof(LogSpawnNavMeshWarningIfNeeded));
                CancelInvoke(nameof(BootstrapBossCombatNavigation));
                CancelInvoke(nameof(KickSpawnedHelpersEngagement));
				StopStrafe();

                CancelInvoke(CheckInvisible);
                CancelInvoke(nameof(CheckReturnToSpawnPoint));
            }

            private void SpawnMapMarker(MarkerConfig config)
            {
                _mapmarker = GameManager.server.CreateEntity("assets/prefabs/tools/map/genericradiusmarker.prefab", transform.position) as MapMarkerGenericRadius;
                _mapmarker.Spawn();
                _mapmarker.radius = config.Radius;
                _mapmarker.alpha = config.Alpha;
                _mapmarker.color1 = new Color(config.Color.R, config.Color.G, config.Color.B);
                // _mapmarker.appType = (AppMarkerType)1; // Let prefab handle appType

                _vendingMarker = GameManager.server.CreateEntity("assets/prefabs/deployable/vendingmachine/vending_mapmarker.prefab", transform.position) as VendingMachineMapMarker;
                _vendingMarker.Spawn();
                _vendingMarker.markerShopName = $"{BossName} ({(int)Npc.health} HP)";

                InvokeRepeating(UpdateMapMarker, 0, 1f);
            }

            private void UpdateMapMarker()
            {
                _mapmarker.transform.position = transform.position;
                _mapmarker.SendUpdate();
                _mapmarker.SendNetworkUpdate();

                _vendingMarker.transform.position = transform.position;
                _vendingMarker.markerShopName = $"{BossName} ({(int)Npc.health} HP)";
                _vendingMarker.SendNetworkUpdate();
            }

            private void InitRadiation(float value)
            {
                TriggerRadiation trigger = _customSphere.AddComponent<TriggerRadiation>();
                trigger.RadiationAmountOverride = value;
                trigger.InterestLayers = 1 << 17;
            }

            private void InitTemperature(float value)
            {
                TriggerTemperature trigger = _customSphere.AddComponent<TriggerTemperature>();
                trigger.Temperature = value;
                trigger.triggerSize = ProximityPlayerRadius;
                trigger.InterestLayers = 1 << 17;
            }

            /// <summary>In proximity and valid for combat — no LOS/CanSee requirement. Used so attack/strafe cycles do not drop to idle roam when LOS flickers.</summary>
            internal bool CanEngagePlayerForMovement(BasePlayer target)
            {
                if (target == null || !target.IsExists()) return false;
                if (target.IsSleeping()) return false;
                if (target.IsWounded()) return false;
                if (target.InSafeZone()) return false;
                if (target._limitedNetworking) return false;
                if (Vector3.Distance(transform.position, target.transform.position) > ProximityPlayerRadius) return false;
                if (Interface.CallHook("CanBossAbilityTarget", Npc, target) is bool) return false;
                return true;
            }

            private bool CanTargetPlayer(BasePlayer target)
            {
                if (!CanEngagePlayerForMovement(target)) return false;
                if (!Npc.CanSeeTarget(target)) return false;

                // Line-of-sight check to prevent AOE damage through walls
                Vector3 posNpc = Npc.eyes.position;
                Vector3 posPlayer = target.eyes.position;
                RaycastHit raycastHit;
                if (Physics.Raycast(posNpc, (posPlayer - posNpc).normalized, out raycastHit, Vector3.Distance(posNpc, posPlayer), 1236478737))
                {
                    // If raycast hits something (wall/building) before reaching the player, they are protected
                    return false;
                }

                return true;
            }

            private bool CanTargetPlayerForAOE(BasePlayer target)
            {
                if (target.IsSleeping()) return false;
                if (target.IsWounded()) return false;
                if (target.InSafeZone()) return false;
                if (target._limitedNetworking) return false;
                if (Vector3.Distance(transform.position, target.transform.position) > ProximityPlayerRadius) return false;

                // No line-of-sight check for AOE attacks - they can target through cover
                return true;
            }

            private bool HasAnyStrictTargetablePlayer()
            {
                foreach (BasePlayer p in Players)
                    if (CanTargetPlayer(p)) return true;
                return false;
            }

            private BasePlayer FindFirstStrictTargetablePlayer()
            {
                foreach (BasePlayer p in Players)
                    if (CanTargetPlayer(p)) return p;
                return null;
            }

            /// <summary>Strict brain target, else proximity engage target, else first AOE-eligible player in radius.</summary>
            private BasePlayer ResolveCombatOrProximityTarget()
            {
                BasePlayer t = _ins.GetBossCombatTarget(Npc);
                if (t != null && t.IsExists()) return t;
                foreach (BasePlayer p in Players)
                    if (CanTargetPlayerForAOE(p)) return p;
                return null;
            }

            private bool IsPostSpawnRadiusAbilityGraceActive()
            {
                return Time.realtimeSinceStartup < _postSpawnRadiusAbilityGraceEndsAt;
            }

            private float PostSpawnRadiusAbilityGraceRemaining()
            {
                return Mathf.Max(0f, _postSpawnRadiusAbilityGraceEndsAt - Time.realtimeSinceStartup);
            }

            private static void DecrementRadiusTick(ref int ticks)
            {
                if (ticks > 0) ticks--;
            }

            private void RadiusActions()
            {
                if (!_abilityRadiusTriggersLoop || radiusActions == null) return;
                // ONLY decrement cooldown timers - DO NOT reset them to 0
                DecrementRadiusTick(ref _timeToSpikes);
                DecrementRadiusTick(ref _timeToFire);
                DecrementRadiusTick(ref _timeToElectricShock);
                DecrementRadiusTick(ref _timeToWounded);
                DecrementRadiusTick(ref _timeToFreeze);
                DecrementRadiusTick(ref _timeToAnimal);
                DecrementRadiusTick(ref _timeToNpc);

                if (Players.Count == 0) return;

                // Strafe backup + attack-cycle alignment now use GetBossCombatTarget + SetTarget, so proximity
                // engagement without strict LOS no longer fights AttackCycleManager.
                if (!_npcHelpersActive && PluginStrafeWanted() && _strafeCoroutine == null)
                {
                    BasePlayer target = _ins.GetBossCombatTarget(Npc);
                    if (target != null)
                    {
                        if (_ins.GetCurrentTarget(Npc) == null)
                            _ins.SetTarget(Npc, target);
                        StartStrafe(target, STRAFE_DURATION_CONTINUOUS);
                        if (_ins._config != null && _ins._config.Debug)
                            _ins.DebugLog($"[{BossName}] RadiusActions: Restarted strafe (backup, brain target)", true);
                    }
                }

                // All attacks now handled by the 6-second cycle system, this just manages cooldowns

                // While helpers are active, boss must remain frozen and not perform abilities
                if (_npcHelpersActive) return;

                if (_timeToAnimal == 0)
                {
                    if (IsPostSpawnRadiusAbilityGraceActive())
                    {
                        _timeToAnimal = 2; // re-check in ~1s (RadiusActions interval 0.5s)
                    }
                    else
                    {
                        // Defer animals to the post-teleport rotation so they behave like other AOEs
                        _pendingAnimal = true;
                        _pendingAnimalDespawn = radiusActions.AnimalAbility.DespawnTime;
                        _ins.DebugLog($"[{BossName}] Queued Animal ability: Type={radiusActions.AnimalAbility.Type}, Count={radiusActions.AnimalAbility.Count}, Despawn={_pendingAnimalDespawn}s", true);
                        // Reset cooldown for next time (convert seconds to 0.5s ticks like other abilities)
                        _timeToAnimal = Mathf.Max(radiusActions.AnimalAbility.Time, 10) * 2;
                        if (radiusActions.UseOnlyOneAbility) return;
                    }
                }

                if (_timeToNpc == 0)
                {
                    if (radiusActions.NpcAbility.ConfigNpc == null)
                    {
                        _timeToNpc = -1; // Disable if config is null
                        return;
                    }
                    if (IsPostSpawnRadiusAbilityGraceActive())
                    {
                        _timeToNpc = 2; // hold queue until post-spawn grace ends
                    }
                    else
                    {
                        // NOTE: Do not gate on _attackCycleTimer here — that value is *remaining* time in the
                        // current cycle (≤20s), not elapsed fight time. A check like _attackCycleTimer < 25f was
                        // always true and forced _timeToNpc into a 10-tick loop forever (NPCs never queued).
                        _timeToNpc = Mathf.Max(radiusActions.NpcAbility.Time, 10) * 2; // reset cooldown in ticks
                        _pendingNpcHelpers = true;
                        _pendingNpcHelpersDuration = radiusActions.NpcAbility.DespawnTime;
                        _ins.DebugLog($"[{BossName}] Queued NPC helpers: Count={radiusActions.NpcAbility.Count}, Despawn={_pendingNpcHelpersDuration}s", true);
                        if (radiusActions.UseOnlyOneAbility) return;
                    }
                }
            }

            internal void TakeDamageActions(BasePlayer player, HitInfo info)
            {
				// Accumulate recent incoming damage for anti face-tank teleport
				if (info != null)
				{
					float now = Time.realtimeSinceStartup;
					if (now > _recentDamageBucketExpiresAt)
					{
						_recentDamageBucket = 0f;
					}
					_recentDamageBucket += info.damageTypes.Total();
					_recentDamageBucketExpiresAt = now + 3.0f; // 3s rolling window
				}
				// If player is very close and bursts > 300 dmg within the window, instantly blink behind them ~5m
				// Only teleport if UseInvisible is enabled
				if (_recentDamageBucket >= 300f && player != null && Config != null && Config.UseInvisible)
				{
					_recentDamageBucket = 0f; // reset bucket so it doesn't retrigger immediately
					Vector3 playerPos = player.transform.position;
					Vector3 forward = player.transform.forward; forward.y = 0f; forward.Normalize();
					Vector3 behind = -forward;
					float desired = 5f;
					Vector3 pos = Vector3.zero;
					float[] angles = new float[] { 0f, 20f, -20f, 35f, -35f };
					for (int i = 0; i < angles.Length && pos == Vector3.zero; i++)
					{
						Vector3 dir = Quaternion.Euler(0f, angles[i], 0f) * behind;
						Vector3 cand = playerPos + dir * desired;
						pos = GetPositionGhost(cand);
					}
					if (pos == Vector3.zero) pos = playerPos + behind * desired;
					pos = ClampHorizontalNearPlayer(pos, player, MaxCombatTeleportDistanceFromPlayer);
					pos = _ins.SnapBossTeleportPosition(Npc, pos, playerPos);
					Invisible(true);
					Npc.Brain.Navigator.Stop();
					Npc.transform.position = pos;
					Npc.Brain?.Navigator?.PlaceOnNavMesh(18f);
					Vector3 look = playerPos - pos; look.y = 0f; if (look.sqrMagnitude > 0.001f) Npc.viewAngles = Quaternion.LookRotation(look).eulerAngles;
					Invisible(false);
					Npc.Brain.Navigator.Pause();
					Invoke(nameof(ResumeMovement), 1.25f);
				}
                if (_timeToInvis == 0)
                {
                    Invisible(false);
                    _timeToInvis = 5;
                    _timeToGoHome = 0;
                    info.damageTypes.ScaleAll(0.2f);
                }
                if (_takeDamageActions == null) return;
                if (_takeDamageActions.Vampirism > 0f && Npc.health < _maxHealth)
                {
                    float newHealth = Npc.health + info.damageTypes.Total() * _takeDamageActions.Vampirism / 100f;
                    if (newHealth > _maxHealth) newHealth = _maxHealth;
                    _ins.NextTick(() => Npc._health = newHealth);
                }
                if (_takeDamageActions.CaloriesTarget != 0f) player.metabolism.calories.Add(-_takeDamageActions.CaloriesTarget);
                if (_takeDamageActions.HydrationTarget != 0f) player.metabolism.hydration.Add(-_takeDamageActions.HydrationTarget);
                if (_takeDamageActions.RadiationTarget != 0f) player.metabolism.radiation_poison.Add(_takeDamageActions.RadiationTarget);
                if (_takeDamageActions.BleedingTarget != 0f) player.metabolism.bleeding.Add(_takeDamageActions.BleedingTarget);
            }

            private IEnumerator MultiPointSpikesAbility(List<BasePlayer> targetPlayers, MultiPointAOEConfig config)
            {
                _ins.DebugLog($"[{BossName}] Starting spike AOE coroutine with {targetPlayers.Count} players");
                
                List<Vector3> allAOEPositions = new List<Vector3>();

                // Generate AOE patterns for each player
                foreach (BasePlayer player in targetPlayers)
                {
                    if (!CanTargetPlayerForAOE(player)) continue;
                    List<Vector3> playerPositions = GenerateAOEPattern(player.transform.position, config);
                    allAOEPositions.AddRange(playerPositions);
                }

                _ins.DebugLog($"[{BossName}] Generated {allAOEPositions.Count} AOE positions, showing warnings for {config.WarningTime} seconds");

                // Show warning circles
                foreach (Vector3 pos in allAOEPositions)
                {
                    CreateWarningCircle(pos, config, 1); // Spikes = Purple
                }

                // Wait for warning period
                yield return CoroutineEx.waitForSeconds(config.WarningTime);

                _ins.DebugLog($"[{BossName}] Warning period over, spawning spikes and dealing damage");

                // Remove warning circles
                DeleteAllWarningCircles();

                // Spawn spikes and damage
                foreach (Vector3 pos in allAOEPositions)
                {
                    Barricade spikes = GameManager.server.CreateEntity("assets/prefabs/deployable/floor spikes/spikes.floor.prefab", pos, Quaternion.identity) as Barricade;
                    spikes.enableSaving = false;
                    spikes.Spawn();
                    foreach (Collider collider in spikes.GetComponentsInChildren<Collider>()) DestroyImmediate(collider);
                    _allSpikes.Add(spikes);

                    // Check if any players are in range of this spike location (AOE ignores LOS)
                    foreach (BasePlayer player in Players.ToList())
                    {
                        if (!CanTargetPlayerForAOE(player)) continue;
                        if (Vector3.Distance(player.transform.position, pos) <= 5f) // 5 meter damage radius
                        {
                            player.Hurt(radiusActions.DamageSpikes, DamageType.Stab, Npc, false);
                            _ins.DebugLog($"[{BossName}] Spike damaged {player.displayName} for {radiusActions.DamageSpikes}", true);
                        }
                    }
                }

                // Clean up spikes after 6 seconds
                yield return CoroutineEx.waitForSeconds(6f);
                _ins.DebugLog($"[{BossName}] Spike AOE complete, cleaning up");
                DeleteAllSpikes();
            }

            private IEnumerator MultiPointFireBallAbility(List<BasePlayer> targetPlayers, MultiPointAOEConfig config)
            {
                List<Vector3> allAOEPositions = new List<Vector3>();

                // Generate AOE patterns for each player
                foreach (BasePlayer player in targetPlayers)
                {
                    if (!CanTargetPlayerForAOE(player)) continue;
                    List<Vector3> playerPositions = GenerateAOEPattern(player.transform.position, config);
                    allAOEPositions.AddRange(playerPositions);
                }

                // Show warning circles
                foreach (Vector3 pos in allAOEPositions)
                {
                    CreateWarningCircle(pos, config, 2); // Fire = Red
                }

                // Wait for warning period
                yield return CoroutineEx.waitForSeconds(config.WarningTime);

                // Remove warning circles
                DeleteAllWarningCircles();

				// Spawn fireballs with shorter lifespan and track them for cleanup
				for (int j = 0; j < 3; j++) // Reduced waves for performance
                {
                    foreach (Vector3 pos in allAOEPositions)
                    {
						// Prefer snapping to structure top (foundation/floor) to avoid roofs; fallback to world/terrain
						Vector3 grounded = pos;
						Vector3 structTop;
						if (_ins.GetStructureTop(pos, out structTop))
						{
							grounded = structTop + Vector3.up * 0.05f;
						}
						else
						{
							RaycastHit rh;
							if (Physics.Raycast(pos + Vector3.up * 10f, Vector3.down, out rh, 30f, Physics.DefaultRaycastLayers))
							{
								grounded = rh.point + Vector3.up * 0.05f;
							}
							else
							{
								grounded.y = TerrainMeta.HeightMap.GetHeight(grounded);
								grounded += Vector3.up * 0.05f;
							}
						}
                        FireBall fireBall = GameManager.server.CreateEntity("assets/bundled/prefabs/fireball.prefab", grounded, Quaternion.identity) as FireBall;
                        if (fireBall != null)
                        {
                            fireBall.enableSaving = false;
                            fireBall.lifeTimeMax = 6f; // Set max lifetime to 6 seconds
                            fireBall.lifeTimeMin = 5f; // Set min lifetime to 5 seconds
                            fireBall.Spawn();
                            // Ensure each fireball self-extinguishes and does not try to spread
                            float lifetime = UnityEngine.Random.Range(5f, 6f);
                            fireBall.CancelInvoke(fireBall.Extinguish);
                            fireBall.Invoke(new Action(fireBall.Extinguish), lifetime);
                            fireBall.CancelInvoke(fireBall.TryToSpread);

                            // Check if any players are in range
                            foreach (BasePlayer player in Players.ToList())
                            {
                                if (!CanTargetPlayer(player)) continue;
                                if (Vector3.Distance(player.transform.position, grounded) <= 5f)
                                {
                                    player.Hurt(radiusActions.DamageFire, DamageType.Heat, Npc, false);
                                }
                            }
                        }
                    }
                    yield return CoroutineEx.waitForSeconds(1f);
                }

                // Extra safety: extinguish any remaining fireballs
                // No tracking list needed; each fireball self-extinguishes via scheduled invoke
            }

            private IEnumerator MultiPointFreezeAbility(List<BasePlayer> targetPlayers, MultiPointAOEConfig config)
            {
                List<Vector3> allAOEPositions = new List<Vector3>();

                // Generate AOE patterns for each player
                foreach (BasePlayer player in targetPlayers)
                {
                    if (!CanTargetPlayerForAOE(player)) continue;
                    List<Vector3> playerPositions = GenerateAOEPattern(player.transform.position, config);
                    allAOEPositions.AddRange(playerPositions);
                }

                // Show warning circles with ice-blue effects
                foreach (Vector3 pos in allAOEPositions)
                {
                    CreateWarningCircle(pos, config, 3); // Ice = Green
                    Effect.server.Run("assets/bundled/prefabs/fx/impacts/stab/rock/stab_rock_01.prefab", pos); // Ice breaking effect for warning
                }

                // Wait for warning period
                yield return CoroutineEx.waitForSeconds(config.WarningTime);

                // Remove warning circles
                DeleteAllWarningCircles();

                // Spawn ice walls and freeze effects at all positions
                foreach (Vector3 pos in allAOEPositions)
                {
                    IceFence wall1 = GameManager.server.CreateEntity("assets/prefabs/misc/xmas/icewalls/icewall.prefab", pos + new Vector3(0f, -2f, 0f), Quaternion.identity) as IceFence;
                    wall1.enableSaving = false;
                    wall1.Spawn();
                    _allWalls.Add(wall1);

                    IceFence wall2 = GameManager.server.CreateEntity("assets/prefabs/misc/xmas/icewalls/icewall.prefab", pos + new Vector3(0f, -2f, 0f), Quaternion.Euler(0f, 90f, 0f)) as IceFence;
                    wall2.enableSaving = false;
                    wall2.Spawn();
                    _allWalls.Add(wall2);

                    // Check if any players are in range of this freeze location
                    foreach (BasePlayer player in Players.ToList())
                    {
                        if (!CanTargetPlayer(player)) continue;
                        if (Vector3.Distance(player.transform.position, pos) <= 5f) // 5 meter freeze radius
                        {
                            player.metabolism.temperature.SetValue(-100f);
                            _freezePlayers.Add(player, player.transform.position);
                        }
                    }
                }

                // Keep players frozen for longer duration
                for (int j = 0; j < 80; j++) // Increased from 50 to 80 (8 seconds instead of 5)
                {
                    foreach (KeyValuePair<BasePlayer, Vector3> dic in _freezePlayers)
                        if (Vector3.Distance(dic.Key.transform.position, dic.Value) > 1f)
                            dic.Key.MovePosition(dic.Value);
                    yield return CoroutineEx.waitForSeconds(0.1f);
                }

                DeleteAllWalls();
                _freezePlayers.Clear();
            }

			private IEnumerator MultiPointElectricShockAbility(List<BasePlayer> targetPlayers, MultiPointAOEConfig config)
            {
                List<Vector3> allAOEPositions = new List<Vector3>();

                // Generate AOE patterns for each player
                foreach (BasePlayer player in targetPlayers)
                {
                    if (!CanTargetPlayerForAOE(player)) continue;
                    List<Vector3> playerPositions = GenerateAOEPattern(player.transform.position, config);
                    allAOEPositions.AddRange(playerPositions);
                }

                // Show warning circles with electric effects
                foreach (Vector3 pos in allAOEPositions)
                {
                    CreateWarningCircle(pos, config, 4); // Electric = Blue
                    Effect.server.Run("assets/prefabs/locks/keypad/effects/lock.code.shock.prefab", pos); // Electric warning effect
                }

                // Wait for warning period
                yield return CoroutineEx.waitForSeconds(config.WarningTime);

                // Remove warning circles
                DeleteAllWarningCircles();

				// Electric shock at all positions (5 waves like original)
                for (int j = 0; j < 5; j++)
                {
                    // Ensure a player only takes damage once per wave even if overlapping multiple circles
                    HashSet<BasePlayer> damagedThisWave = new HashSet<BasePlayer>();

					foreach (Vector3 pos in allAOEPositions)
                    {
						// Ground to structure/terrain for consistent effect and damage radius
						Vector3 grounded = pos;
						Vector3 structTop;
						if (_ins.GetStructureTop(pos, out structTop))
						{
							grounded = structTop + Vector3.up * 0.05f;
						}
						else
						{
							RaycastHit rh;
							if (Physics.Raycast(pos + Vector3.up * 10f, Vector3.down, out rh, 30f, Physics.DefaultRaycastLayers))
							{
								grounded = rh.point + Vector3.up * 0.05f;
							}
							else
							{
								grounded.y = TerrainMeta.HeightMap.GetHeight(grounded);
								grounded += Vector3.up * 0.05f;
							}
						}

						// Electric shock effects at grounded position
						for (int i = 0; i < 15; i++) Effect.server.Run("assets/prefabs/locks/keypad/effects/lock.code.shock.prefab", grounded + UnityEngine.Random.insideUnitSphere * 2f);

						// Check if any players are in range of this shock location (AOE ignores LOS)
						foreach (BasePlayer player in Players.ToList())
                        {
                            if (!CanTargetPlayerForAOE(player)) continue;
                            if (damagedThisWave.Contains(player)) continue;
							// Use horizontal (XZ) radius to ignore small vertical offsets between floors
							Vector2 p2 = new Vector2(player.transform.position.x, player.transform.position.z);
							Vector2 g2 = new Vector2(grounded.x, grounded.z);
							if (Vector2.Distance(p2, g2) <= 5f) // 5 meter shock radius
                            {
                                damagedThisWave.Add(player);
								float dmg = radiusActions.DamageElectricShock;
								if (dmg <= 0f) dmg = 5f; // safety fallback
								player.Hurt(dmg, DamageType.ElectricShock, Npc, false);
								_ins.DebugLog($"[{BossName}] Electric shocked {player.displayName} for {dmg}", true);
                                // Additional shock effects on the player
                                for (int i = 0; i < 5; i++) Effect.server.Run("assets/prefabs/locks/keypad/effects/lock.code.shock.prefab", player.transform.position + UnityEngine.Random.insideUnitSphere * 1f);
                            }
                        }
                    }
                    yield return CoroutineEx.waitForSeconds(1f);
                }
            }

            private IEnumerator AbilityFireBall()
            {
                for (int j = 0; j < 5; j++)
                {
                    foreach (BasePlayer player in Players.ToList())
                    {
                        if (!CanTargetPlayer(player)) continue;
                        FireBall fireBall = GameManager.server.CreateEntity("assets/bundled/prefabs/fireball.prefab", player.transform.position, player.transform.rotation) as FireBall;
                        fireBall.enableSaving = false;
                        fireBall.Spawn();
                        player.Hurt(radiusActions.DamageFire, DamageType.Heat, Npc, false);
                    }
                    yield return CoroutineEx.waitForSeconds(1f);
                }
            }

            private IEnumerator AbilityElectricShock()
            {
                for (int j = 0; j < 5; j++)
                {
                    for (int i = 0; i < 10; i++) Effect.server.Run("assets/prefabs/locks/keypad/effects/lock.code.shock.prefab", Npc.transform.position + UnityEngine.Random.insideUnitSphere * 1.5f);
                    foreach (BasePlayer player in Players.ToList())
                    {
                        if (!CanTargetPlayer(player)) continue;
                        for (int i = 0; i < 10; i++) Effect.server.Run("assets/prefabs/locks/keypad/effects/lock.code.shock.prefab", player.transform.position + UnityEngine.Random.insideUnitSphere * 1.5f);
                        player.Hurt(radiusActions.DamageElectricShock, DamageType.ElectricShock, Npc, false);
                    }
                    yield return CoroutineEx.waitForSeconds(1f);
                }
            }

            private IEnumerator AbilityFreeze()
            {
                // NOTE: This method appears to be legacy/unused code. The new MultiPointFreezeAbility() is used instead.
                // Removed unnecessary bone knife drop that was causing performance issues with underwater items.
                foreach (BasePlayer player in Players.ToList())
                {
                    if (!CanTargetPlayer(player)) continue;

                    IceFence wall1 = GameManager.server.CreateEntity("assets/prefabs/misc/xmas/icewalls/icewall.prefab", player.transform.position + new Vector3(0f, -2f, 0f), player.transform.rotation) as IceFence;
                    wall1.enableSaving = false;
                    wall1.Spawn();
                    _allWalls.Add(wall1);

                    IceFence wall2 = GameManager.server.CreateEntity("assets/prefabs/misc/xmas/icewalls/icewall.prefab", player.transform.position + new Vector3(0f, -2f, 0f), Quaternion.Euler(player.transform.rotation.eulerAngles + new Vector3(0f, 90f, 0f))) as IceFence;
                    wall2.enableSaving = false;
                    wall2.Spawn();
                    _allWalls.Add(wall2);

                    player.metabolism.temperature.SetValue(-100f);

                    _freezePlayers.Add(player, player.transform.position);
                }
                for (int j = 0; j < 80; j++) // Increased from 50 to 80 (8 seconds instead of 5)
                {
                    foreach (KeyValuePair<BasePlayer, Vector3> dic in _freezePlayers) if (Vector3.Distance(dic.Key.transform.position, dic.Value) > 1f) dic.Key.MovePosition(dic.Value);
                    yield return CoroutineEx.waitForSeconds(0.1f);
                }
                DeleteAllWalls();
                _freezePlayers.Clear();
            }

            private void DeleteAllSpikes()
            {
                foreach (Barricade spikes in _allSpikes) if (spikes.IsExists()) spikes.Kill();
                _allSpikes.Clear();
            }

            private void DeleteAllWalls()
            {
                foreach (IceFence wall in _allWalls) if (wall.IsExists()) wall.Kill();
                _allWalls.Clear();
            }

            private void DeleteAllWarningCircles()
            {
                // Clean up all warning markers
                foreach (BaseEntity circle in _warningCircles) 
                {
                    if (circle != null && circle.IsExists()) 
                    {
                        circle.Kill();
                    }
                }
                _warningCircles.Clear();
            }

            private List<Vector3> GenerateAOEPattern(Vector3 playerPosition, MultiPointAOEConfig config)
            {
                List<Vector3> positions = new List<Vector3>();

                _ins.DebugLog($"[{BossName}] Generating AOE pattern at {playerPosition} with {config.AOELocationCount} locations", true);

                // Always include the player's current position as first AOE point
                positions.Add(playerPosition);

                // Generate additional points - use full config count for total AOEs
                int totalAOEs = Mathf.Clamp(config.AOELocationCount, 8, 16); // Support 8-16 total AOEs  
                int additionalPoints = totalAOEs - 1; // Subtract 1 for player position
                
                // Split into inner (8) and outer (remaining) rings  
                int innerRingPoints = 8;
                int outerRingPoints = additionalPoints - innerRingPoints;
                
                // Inner ring (tighter spread around player)
                float innerAngleStep = 360f / innerRingPoints;
                for (int i = 0; i < innerRingPoints; i++)
                {
                    float angle = i * innerAngleStep + UnityEngine.Random.Range(-10f, 10f);
                    float distance = UnityEngine.Random.Range(4f, 8f); // Tighter inner ring: 4-8 meters
                    
                    Vector3 direction = new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad), 0, Mathf.Cos(angle * Mathf.Deg2Rad));
                    Vector3 newPosition = playerPosition + direction * distance;
                    
                    positions.Add(newPosition);
                    _ins.DebugLog($"[{BossName}] Added inner AOE position {i + 1}: {newPosition}", true);
                }
                
                // Outer ring (extra 8 positions further out)
                if (outerRingPoints > 0)
                {
                    float outerAngleStep = 360f / outerRingPoints;
                    for (int i = 0; i < outerRingPoints; i++)
                    {
                        float angle = i * outerAngleStep + UnityEngine.Random.Range(-15f, 15f);
                        float distance = UnityEngine.Random.Range(10f, 15f); // Outer ring: 10-15 meters
                        
                        Vector3 direction = new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad), 0, Mathf.Cos(angle * Mathf.Deg2Rad));
                        Vector3 newPosition = playerPosition + direction * distance;
                        
                        positions.Add(newPosition);
                        _ins.DebugLog($"[{BossName}] Added outer AOE position {i + 1}: {newPosition}", true);
                    }
                }

                _ins.DebugLog($"[{BossName}] Generated {positions.Count} total AOE positions", true);
                return positions;
            }

            private void CreateWarningCircle(Vector3 position, MultiPointAOEConfig config, int attackType = 0)
            {
                if (!config.ShowWarningCircles) return;

                // Resolve a reliable surface point on foundations/floors first, then fallback to terrain/world ray
                Vector3 groundPos = position;
                Vector3 structTop;
                if (_ins != null && _ins.GetStructureTop(position, out structTop))
                {
                    groundPos = structTop + Vector3.up * 0.05f; // avoid z-fighting with floor
                }
                else
                {
                    RaycastHit hit;
                    if (Physics.Raycast(position + Vector3.up * 10f, Vector3.down, out hit, 30f, Physics.DefaultRaycastLayers))
                    {
                        groundPos = hit.point + Vector3.up * 0.05f;
                    }
                    else
                    {
                        // Last resort: snap to terrain height
                        groundPos.y = TerrainMeta.HeightMap.GetHeight(groundPos);
                        groundPos += Vector3.up * 0.05f;
                    }
                }

                // Choose color based on attack type (supports overrides via WarningCircleColors)
                string spherePrefab = "assets/bundled/prefabs/modding/events/twitch/br_sphere_red.prefab"; // Default red
                string attackName = "Unknown";
                
                switch (attackType)
                {
                    case 1: attackName = "Spikes"; break;
                    case 2: attackName = "Fire"; break;
                    case 3: attackName = "Ice"; break;
                    case 4: attackName = "Electric"; break;
                }

                // Default mapping (after your request): Spikes -> Green, Fire -> Red, Ice -> Blue, Electric -> Purple
                Dictionary<string, string> defaultColorMap = new Dictionary<string, string>
                {
                    ["Spikes"] = "green",
                    ["Fire"] = "red",
                    ["Ice"] = "blue",
                    ["Electric"] = "purple"
                };

                string colorKey = defaultColorMap.ContainsKey(attackName) ? defaultColorMap[attackName] : "red";
                if (config.WarningCircleColors != null && config.WarningCircleColors.ContainsKey(attackName))
                {
                    colorKey = config.WarningCircleColors[attackName]?.ToLowerInvariant() ?? colorKey;
                }

                // Map color name to prefab path
                switch (colorKey)
                {
                    case "red": spherePrefab = "assets/bundled/prefabs/modding/events/twitch/br_sphere_red.prefab"; break;
                    case "green": spherePrefab = "assets/bundled/prefabs/modding/events/twitch/br_sphere_green.prefab"; break;
                    case "blue": spherePrefab = "assets/bundled/prefabs/modding/events/twitch/br_sphere.prefab"; break; // Blue
                    case "purple": spherePrefab = "assets/bundled/prefabs/modding/events/twitch/br_sphere_purple.prefab"; break;
                    default: spherePrefab = "assets/bundled/prefabs/modding/events/twitch/br_sphere_red.prefab"; break;
                }

                // Create colored warning circle
                SphereEntity sphere = GameManager.server.CreateEntity(spherePrefab, groundPos) as SphereEntity;
                if (sphere != null)
                {
                    sphere.enableSaving = false;
                    sphere.Spawn();
                    // Set radius after spawn and push a network update so clients see it immediately
                    sphere.currentRadius = 2.5f; // 5 meter damage radius
                    sphere.lerpRadius = sphere.currentRadius;
                    sphere.SendNetworkUpdate();
                    _warningCircles.Add(sphere);
                    _ins.DebugLog($"[{BossName}] Created {attackName} warning circle at {groundPos}", true);
                }
            }

            private void DeleteAllAnimals()
            {
                int countLegacy = Animals != null ? Animals.Count : 0;
                int countGen2 = AnimalsAny != null ? AnimalsAny.Count : 0;
                foreach (BaseAnimalNPC animal in Animals)
                    if (animal != null && animal.IsExists()) animal.Kill();
                Animals.Clear();
                foreach (BaseEntity ent in AnimalsAny)
                    if (ent != null && ent.IsExists()) ent.Kill();
                AnimalsAny.Clear();
                if (_ins != null && countLegacy + countGen2 > 0)
                {
                    string name = (Npc != null) ? Npc.displayName : "Boss";
                    _ins.DebugLog($"[{name}] Cleaned up {countLegacy + countGen2} animal(s) (legacy={countLegacy}, gen2={countGen2})", true);
                }
            }

            private void DeleteAllScientists()
            {
                // Unregister helper NPCs from GrimmNPC before killing them
                foreach (ScientistNPC npc in Scientists)
                {
                    if (npc != null && npc.IsExists())
                    {
                        // Unregister from GrimmNPC before killing
                        _ins.UnregisterNpcFromGrimmNpc(npc);
                        npc.Kill();
                    }
                }
                Scientists.Clear();
                EndNpcHelpers();
            }

            private void FinishWounded()
            {
                foreach (BasePlayer player in _woundedPlayers) if (player.IsExists() && player.HasPlayerFlag(BasePlayer.PlayerFlags.Wounded)) player.SetPlayerFlag(BasePlayer.PlayerFlags.Wounded, false);
                _woundedPlayers.Clear();
            }

            private void CheckInvisible()
            {
                BasePlayer target = _ins.GetBossCombatTarget(Npc);
                if (target != null && _ins.GetCurrentTarget(Npc) == null)
                    _ins.SetTarget(Npc, target);
                CheckPath(target);
                CheckGhost(target);
            }

            // Periodically teleports boss back to spawn point during combat to keep them centralized
            private void CheckReturnToSpawnPoint()
            {
                if (Npc == null || Npc.IsDestroyed) return;
                if (!_returnToSpawnPoint || _returnToSpawnPointInterval <= 0f) return;

                // Only return to spawn if boss is in combat (has players nearby)
                if (Players.Count == 0 || !HasAnyStrictTargetablePlayer()) return;

                // Don't return during special states (AOE attacks, helpers active, etc.)
                if (_npcHelpersActive || _isStationary) return;

                // Update timer
                _returnToSpawnPointTimer += 1f;

                // Check if it's time to return
                if (_returnToSpawnPointTimer >= _returnToSpawnPointInterval)
                {
                    float distanceFromHome = Vector3.Distance(Npc.transform.position, _homePosition);
                    
                    // Only teleport if boss has wandered away from spawn point (more than 10 meters)
                    if (distanceFromHome > 10f)
                    {
                        Vector3 teleportPos = GetPositionGhost(_homePosition);
                        if (teleportPos == Vector3.zero) teleportPos = _homePosition;
                        teleportPos = _ins.SnapBossTeleportPosition(Npc, teleportPos, _homePosition);

                        Npc.Brain.Navigator.Stop();
                        Npc.transform.position = teleportPos;
                        Npc.Brain?.Navigator?.PlaceOnNavMesh(18f);
                        // Stop() disables the NavMeshAgent; must resume like other teleport paths or the boss cannot move.
                        Npc.Brain.Navigator.Resume();
                        Npc.Brain.Navigator.SetNavMeshEnabled(true);
                        _ins.DebugLog($"[{BossName}] Returned to spawn point (was {distanceFromHome:F1}m away)");
                    }

                    // Reset timer
                    _returnToSpawnPointTimer = 0f;
                }
            }

            internal bool NpcHelpersActive => _npcHelpersActive;

            internal void BeginNpcHelpers(float duration)
            {
                if (_npcHelpersActive) return;
                _npcHelpersActive = true;
                _isStationary = true;
                // StrafeRoutine calls RecoverCombatNavigation every tick; kill it immediately so we don't spam Stop/Warp while "frozen".
                StopStrafe();
                if (Npc != null && Npc.Brain != null && Npc.Brain.Navigator != null)
                {
                    Npc.Brain.Navigator.Pause();
                }
                // Cancel any pending attack cycle invokes while frozen
                CancelInvoke(nameof(TeleportAwayAndWait));
                CancelInvoke(nameof(ChooseAndExecuteNextAttack));
                CancelInvoke(nameof(TeleportAwayAfterMelee));
                CancelInvoke(nameof(ResumeMovement));
                CancelInvoke(nameof(EndNpcHelpers));
                if (_ins?._config != null && _ins._config.Debug)
                    _ins.DebugLog($"[{BossName}] BeginNpcHelpers: wave started, duration={duration:F0}s, navigator Paused, attack invokes cancelled (boss idle until wave ends)", true);
                // Do NOT Invoke(EndNpcHelpers) here — same-time ordering vs DeleteAllScientists could Resume the boss while helpers still exist.
                // DeleteAllScientists(duration) always calls EndNpcHelpers after cleanup.
            }

            internal void EndNpcHelpers()
            {
                CancelInvoke(nameof(EndNpcHelpers));
                if (!_npcHelpersActive) return;
                _npcHelpersActive = false;
                _isStationary = false;
                if (Npc != null && Npc.Brain != null && Npc.Brain.Navigator != null)
                {
                    Npc.Brain.Navigator.Resume();
                    // Pause() can leave NavMeshAgent disabled; Resume alone is not always enough for stock brain combat aim.
                    Npc.Brain.Navigator.SetNavMeshEnabled(true);
                    // Large snap radius warped bosses into bad geometry; keep tight like GrimmNPC helper kicks.
                    Npc.Brain?.Navigator?.PlaceOnNavMesh(6f);
                }
                StopHelperAggroPulse();

                BasePlayer resumeTarget = ResolveCombatOrProximityTarget();
                if (resumeTarget != null && Npc != null)
                    _ins.SetTarget(Npc, resumeTarget);

                // AttackCycleManager returned early while helpers were active, so _attackCycleTimer never ticked down — boss looked "stuck" for minutes.
                _attackCycleTimer = 0f;

                if (_ins?._config != null && _ins._config.Debug)
                {
                    string tname = resumeTarget != null ? resumeTarget.displayName : "none";
                    _ins.DebugLog($"[{BossName}] EndNpcHelpers: navigator Resumed, re-target={tname}, attackCycleTimer=0 (new cycle on next manager tick)", true);
                }

                if (Npc != null)
                {
                    ScientistNPC npcCap = Npc;
                    _ins.NextTick(() =>
                    {
                        if (npcCap == null || npcCap.IsDestroyed) return;
                        BasePlayer t2 = ResolveCombatOrProximityTarget();
                        if (t2 != null)
                            _ins.SetTarget(npcCap, t2);
                    });
                }
            }

            private void StartHelperAggroPulse()
            {
                StopHelperAggroPulse();
                if (radiusActions?.NpcAbility?.ConfigNpc != null)
                {
                    _helperAggroCoroutine = ServerMgr.Instance.StartCoroutine(HelperAggroPulse());
                }
            }

            private void StopHelperAggroPulse()
            {
                if (_helperAggroCoroutine != null)
                {
                    ServerMgr.Instance.StopCoroutine(_helperAggroCoroutine);
                    _helperAggroCoroutine = null;
                }
            }

            /// <summary>
            /// GrimmNPC often enables the NavMeshAgent a few frames after spawn; first helper can fight while others stay idle without this.
            /// </summary>
            private void ScheduleHelperEngagementKicks()
            {
                CancelInvoke(nameof(KickSpawnedHelpersEngagement));
                Invoke(nameof(KickSpawnedHelpersEngagement), HelperEngagementKickDelay1);
                Invoke(nameof(KickSpawnedHelpersEngagement), HelperEngagementKickDelay2);
                Invoke(nameof(KickSpawnedHelpersEngagement), HelperEngagementKickDelay3);
            }

            private void KickSpawnedHelpersEngagement()
            {
                if (_isShuttingDown || Scientists == null || Scientists.Count == 0) return;
                BasePlayer t = ResolveCombatOrProximityTarget();
                foreach (ScientistNPC h in Scientists)
                {
                    if (h == null || h.IsDestroyed) continue;
                    _ins.KickHelperNpcEngagement(h, t);
                }
            }

            /// <summary>Per-helper delayed kicks so each agent gets a tick after GrimmNPC enables navmesh.</summary>
            private void ScheduleStaggeredHelperEngagementKicks()
            {
                if (_isShuttingDown || Scientists == null || Scientists.Count == 0) return;
                BasePlayer snapshot = ResolveCombatOrProximityTarget();
                int idx = 0;
                foreach (ScientistNPC h in Scientists)
                {
                    if (h == null || h.IsDestroyed) continue;
                    ScientistNPC cap = h;
                    float delay = 0.03f + idx * 0.15f;
                    idx++;
                    _ins.timer.Once(delay, () => StaggeredKickOneHelper(cap, snapshot));
                }
            }

            private void StaggeredKickOneHelper(ScientistNPC cap, BasePlayer snapshotTarget)
            {
                if (_isShuttingDown || cap == null || cap.IsDestroyed) return;
                BasePlayer t = ResolveCombatOrProximityTarget() ?? snapshotTarget;
                _ins.KickHelperNpcEngagement(cap, t);
                if (_ins._config != null && _ins._config.Debug && _ins._config.DebugHelperEngagement)
                    _ins.Puts($"[{BossName}] Staggered helper kick: net={cap.net?.ID.Value} target={(t != null ? t.displayName : "none")}");
            }

            private IEnumerator HelperAggroPulse()
            {
                while (!_isShuttingDown && _npcHelpersActive && Scientists.Count > 0)
                {
                    foreach (ScientistNPC helper in Scientists)
                    {
                        if (helper == null || helper.IsDestroyed) continue;
                        
                        // Find nearest player in sense range
                        float senseRange = Mathf.Max(30f, radiusActions.NpcAbility.ConfigNpc.SenseRange);
                        float bestDist = float.MaxValue;
                        BasePlayer bestTarget = null;

                        foreach (BasePlayer player in Players)
                        {
                            if (player == null || !player.userID.IsSteamId() || player.isInvisible) continue;
                            float dist = Vector3.Distance(player.transform.position, helper.transform.position);
                            if (dist < bestDist && dist <= senseRange)
                            {
                                bestDist = dist;
                                bestTarget = player;
                            }
                        }

                        // If no player in Players set, check all active players
                        if (bestTarget == null)
                        {
                            foreach (BasePlayer player in BasePlayer.activePlayerList)
                            {
                                if (player == null || !player.userID.IsSteamId() || player.isInvisible) continue;
                                float dist = Vector3.Distance(player.transform.position, helper.transform.position);
                                if (dist < bestDist && dist <= senseRange)
                                {
                                    bestDist = dist;
                                    bestTarget = player;
                                }
                            }
                        }

                        // Always refresh target to ensure NPCs stay engaged
                        if (bestTarget != null)
                        {
                            _ins.SetTarget(helper, bestTarget, true);
                            _ins.DebugLog($"[{BossName}] Helper NPC aggro pulse: targeting {bestTarget.displayName} (distance: {bestDist:F1}m)", true);
                        }
                    }
                    yield return CoroutineEx.waitForSeconds(2f);
                }
                StopHelperAggroPulse();
            }

            /// <summary>
            /// Navigator.Stop() disables the NavMeshAgent (stock BaseNavigator.StopNavMesh â†’ SetNavMeshEnabled(false)).
            /// SetDestination then fails until the agent is re-enabled (see game BaseNavigator.SetDestination). GrimmNPC avoids Stop() for idle halt; this covers BossMonster teleports and similar.
            /// Stock PlaceOnNavMesh only searches ~6m from the entity; wider SamplePosition + Warp helps tight monument edges.
            /// </summary>
            /// <summary>
            /// When the boss has no combat target, stop plugin-driven strafe and hand movement back to
            /// ScientistBrain Roam/Idle (per AI_NPC_Framework: stock brain owns idle patrol).
            /// </summary>
            private void ReleaseNavigatorForStockRoam()
            {
                if (_npcHelpersActive) return;
                if (Npc == null || !Npc.IsExists() || Npc.Brain?.Navigator == null) return;
                BaseNavigator nav = Npc.Brain.Navigator;
                nav.Resume();
                nav.SetNavMeshEnabled(true);
            }

            private void RecoverCombatNavigation(bool bypassCooldown = false)
            {
                if (_npcHelpersActive) return;
                if (Npc == null || !Npc.IsExists() || Npc.Brain?.Navigator == null) return;
                NavMeshAgent agent = Npc.Brain.Navigator.Agent;
                if (agent == null) return;
                if (agent.enabled && agent.isOnNavMesh) return;
                if (!bypassCooldown && Time.realtimeSinceStartup < _nextNavRecoverRealtime) return;
                _nextNavRecoverRealtime = Time.realtimeSinceStartup + 1f;

                BaseNavigator nav = Npc.Brain.Navigator;
                nav.Resume();
                nav.SetNavMeshEnabled(true);

                if (agent.enabled && agent.isOnNavMesh) return;

                Vector3 p = Npc.transform.position;
                NavMeshHit hit;
                bool got;
                if (TerrainRoamSpawn)
                {
                    got = NavMesh.SamplePosition(p, out hit, 45f, NavAreaTerrain)
                        || NavMesh.SamplePosition(p, out hit, 70f, NavMesh.AllAreas);
                }
                else
                {
                    got = NavMesh.SamplePosition(p, out hit, 28f, NavAreaMonument)
                        || NavMesh.SamplePosition(p, out hit, 42f, NavAreaMonument)
                        || NavMesh.SamplePosition(p, out hit, 55f, NavMesh.AllAreas);
                }
                if (got)
                    nav.Warp(hit.position);
                else
                    nav.PlaceOnNavMesh(2f);
            }

            /// <summary>
            /// Plugin-driven strafe fights GrimmNPC/ScientistBrain ranged combat when left on for rifle bosses.
            /// Only enable while melee pressure, AOE standoff kiting, or a melee-primary belt needs it.
            /// </summary>
            private bool PluginStrafeWanted()
            {
                if (_npcHelpersActive) return false;
                float now = Time.realtimeSinceStartup;
                if (BeltPrimarilyMelee()) return true;
                if (now < _meleePressureEndRealtime) return true;
                if (now < _aoeStandoffEndRealtime) return true;
                return false;
            }

            /// <summary>Drop destroyed helpers so we can detect &quot;all dead&quot; before DespawnTime elapses.</summary>
            private void PruneDestroyedScientistsFromSet()
            {
                if (Scientists == null || Scientists.Count == 0) return;
                var toRemove = new List<ScientistNPC>();
                foreach (ScientistNPC s in Scientists)
                {
                    if (s == null || s.IsDestroyed || !s.IsExists())
                        toRemove.Add(s);
                }
                foreach (ScientistNPC s in toRemove)
                {
                    Scientists.Remove(s);
                    if (s != null)
                    {
                        try { _ins.UnregisterNpcFromGrimmNpc(s); }
                        catch { /* ignore */ }
                    }
                }
            }

            private void AttackCycleManager()
            {
                if (Npc == null || Npc.IsDestroyed) return;

                if (_npcHelpersActive)
                {
                    PruneDestroyedScientistsFromSet();
                    if (Scientists == null || Scientists.Count == 0)
                    {
                        CancelInvoke(nameof(DeleteAllScientists));
                        if (_ins?._config != null && _ins._config.Debug)
                            _ins.DebugLog($"[{BossName}] Helper wave: all eliminated early — resuming boss (cancelled scheduled despawn)", true);
                        EndNpcHelpers();
                    }
                    else
                    {
                        StopStrafe();
                        if (_ins?._config != null && _ins._config.Debug)
                            _ins.DebugLog($"[{BossName}] Helper wave active: {Scientists.Count} alive, boss frozen (navigator Paused)", true);
                        return;
                    }
                }

                BasePlayer target = _ins.GetBossCombatTarget(Npc);
                if (target == null)
                {
                    // No brain combat target — do not fight stock Roam with plugin SetDestination strafe.
                    StopStrafe();
                    ReleaseNavigatorForStockRoam();
                    return;
                }
                if (_ins.GetCurrentTarget(Npc) == null)
                    _ins.SetTarget(Npc, target);
                
                // Diagnostic logging for movement state
                float distanceToTarget = Vector3.Distance(Npc.transform.position, target.transform.position);
                bool navigatorActive = Npc?.Brain?.Navigator != null;
                bool navigatorMoving = navigatorActive && Npc.Brain.Navigator.Moving;
                bool navMeshEnabled = navigatorActive && Npc.Brain.Navigator.Agent != null && Npc.Brain.Navigator.Agent.enabled;
                bool onNavMesh = navigatorActive && Npc.Brain.Navigator.Agent != null && Npc.Brain.Navigator.Agent.isOnNavMesh;
                bool navigatorStopped = navigatorActive && onNavMesh && Npc.Brain.Navigator.Agent != null && Npc.Brain.Navigator.Agent.isStopped;
                int areaMask = Npc?.NavAgent?.areaMask ?? 0;
                
                // CRITICAL: Fix navmesh if boss is on monument but has wrong AreaMask (not for map-roam / terrain-registered spawns)
                bool isOnMonumentNavmesh = false;
                if (!TerrainRoamSpawn)
                {
                    isOnMonumentNavmesh = _isOnMonument;
                    if (!isOnMonumentNavmesh)
                    {
                        NavMeshHit hit;
                        if (NavMesh.SamplePosition(Npc.transform.position, out hit, 5f, NavAreaMonument))
                        {
                            isOnMonumentNavmesh = true;
                            if (!_isOnMonument)
                            {
                                _ins.DebugLog($"[{BossName}] Detected monument navmesh via sampling (was not detected during spawn)", true);
                                _isOnMonument = true;
                            }
                        }
                    }

                    if (isOnMonumentNavmesh && areaMask != NavAreaMonument && Npc?.NavAgent != null)
                    {
                        _ins.DebugLog($"[{BossName}] Fixing navmesh: Boss is on monument but has AreaMask={areaMask} (should be {NavAreaMonument})", true);
                        int correctAgentTypeID = _ins.GetMonumentAgentTypeID(Npc.transform.position);
                        Npc.NavAgent.areaMask = NavAreaMonument;
                        Npc.NavAgent.agentTypeID = correctAgentTypeID;
                        Npc.NavAgent.enabled = true;
                        if (!Npc.NavAgent.isOnNavMesh && Npc.Brain?.Navigator != null)
                            Npc.Brain.Navigator.PlaceOnNavMesh(5f);
                        areaMask = NavAreaMonument;
                        onNavMesh = Npc.NavAgent.isOnNavMesh;
                        navMeshEnabled = Npc.NavAgent.enabled;
                    }

                    if (isOnMonumentNavmesh && Npc?.NavAgent != null)
                    {
                        int wantAgent = _ins.GetMonumentAgentTypeID(Npc.transform.position);
                        if (wantAgent == 0 && _ins.ResolveMonumentNavMeshAgentType(Npc.transform.position, out int fromScene))
                            wantAgent = fromScene;
                        if (wantAgent != 0 && Npc.NavAgent.agentTypeID != wantAgent)
                        {
                            _ins.DebugLog($"[{BossName}] Correcting NavMesh agentTypeID {Npc.NavAgent.agentTypeID} -> {wantAgent} (monument)", true);
                            Npc.NavAgent.agentTypeID = wantAgent;
                            Npc.NavAgent.enabled = true;
                            if (!Npc.NavAgent.isOnNavMesh)
                                Npc.Brain?.Navigator?.PlaceOnNavMesh(8f);
                            onNavMesh = Npc.NavAgent.isOnNavMesh;
                            navMeshEnabled = Npc.NavAgent.enabled;
                        }
                    }
                }

                if (navigatorActive && Npc.Brain.Navigator.Agent != null && (!onNavMesh || !navMeshEnabled))
                    RecoverCombatNavigation();
                if (navigatorActive && Npc.Brain.Navigator.Agent != null)
                {
                    navMeshEnabled = Npc.Brain.Navigator.Agent.enabled;
                    onNavMesh = Npc.Brain.Navigator.Agent.isOnNavMesh;
                    navigatorMoving = Npc.Brain.Navigator.Moving;
                }

                string maskExpect = TerrainRoamSpawn ? "terrain roam" : $"{NavAreaMonument} if monument";
                bool wrongMonumentMask = !TerrainRoamSpawn && _isOnMonument && areaMask != NavAreaMonument;
                if ((!navigatorMoving || wrongMonumentMask || !onNavMesh || !navMeshEnabled) && PluginStrafeWanted())
                {
                    _ins.DebugLog($"[{BossName}] Movement Issue - Distance: {distanceToTarget:F1}m, Moving: {navigatorMoving}, " +
                        $"OnNavMesh: {onNavMesh}, NavMeshEnabled: {navMeshEnabled}, AreaMask: {areaMask} (expect {maskExpect}), " +
                        $"AgentTypeID: {Npc?.NavAgent?.agentTypeID ?? -1}, StrafeActive: {_strafeCoroutine != null}", true);
                }

                if (PluginStrafeWanted())
                {
                    if (_strafeCoroutine == null)
                    {
                        StartStrafe(target, STRAFE_DURATION_CONTINUOUS);
                        _ins.DebugLog($"[{BossName}] Restarted strafe coroutine (was null)", true);
                    }
                }
                else
                {
                    if (_strafeCoroutine != null)
                    {
                        StopStrafe();
                        ReleaseNavigatorForStockRoam();
                        _ins.DebugLog($"[{BossName}] Stopped plugin strafe — ranged/AOE-off phase; brain owns movement and weapon fire");
                    }
                }

                // Ranged: ScientistBrain/GrimmNPC can keep a long partial path (e.g. sliding along a wall toward a far nav corner).
                // Same symptom clears when the player breaks LOS. Nudge the agent off the bad path without disabling the agent (no Stop()).
                if (!PluginStrafeWanted() && _strafeCoroutine == null && Time.realtimeSinceStartup >= _nextRangedNavStuckRecoverRealtime)
                {
                    NavMeshAgent ag = Npc.NavAgent;
                    if (ag != null && ag.isOnNavMesh && ag.enabled && !ag.pathPending && ag.hasPath)
                    {
                        float rem = ag.remainingDistance;
                        float v2 = ag.velocity.sqrMagnitude;
                        // TakeCover / kiting can show high remainingDistance with non-trivial velocity; still recover absurd paths.
                        if ((rem > 42f && v2 < 0.06f) || rem > 72f)
                        {
                            ag.ResetPath();
                            _ins.SetTarget(Npc, target);
                            _nextRangedNavStuckRecoverRealtime = Time.realtimeSinceStartup + 2.5f;
                            _ins.DebugLog($"[{BossName}] Ranged nav stuck recover: remainingPath≈{rem:F0}m, vel²≈{v2:F3} — ResetPath + SetTarget", true);
                        }
                    }
                }
                
                // Update attack cycle timer
                if (_attackCycleTimer > 0f) 
                {
                    _attackCycleTimer -= 1f;
                    // Debug timer countdown
                    if (_attackCycleTimer % 5 == 0) // Log every 5 seconds
                    {
                        _ins.DebugLog($"[{BossName}] Attack cycle timer: {_attackCycleTimer} seconds remaining", true);
                    }
                    return; // Don't do anything else while timer is counting down
                }

                // Start new attack cycle only if timer is exactly 0 (not negative)
                if (_attackCycleTimer == 0f)
                {
                    _ins.DebugLog($"[{BossName}] Timer expired, starting new structured attack cycle");
                    StartStructuredAttackCycle(target);
                }
            }

            private void StartStructuredAttackCycle(BasePlayer target)
            {
                // Cancel any pending invokes to prevent overlapping cycles
                CancelInvoke(nameof(TeleportAwayAndWait));
                CancelInvoke(nameof(ChooseAndExecuteNextAttack));
                CancelInvoke(nameof(TeleportAwayAfterMelee));
                CancelInvoke(nameof(ResumeMovement)); // Cancel any pending movement resume
                // DON'T stop strafe - maintain continuous movement during combat
                
                // Set timer immediately to prevent multiple cycles starting
                _attackCycleTimer = 20f; // 20 seconds total cycle time (will be adjusted based on attack choice)

                _aoeStandoffEndRealtime = 0f;
                // Ranged belt: a forced "melee pressure" window makes StrafeRoutine spam SetDestination and blocks ScientistBrain ranged fire.
                bool wantMeleeRunIn = BeltPrimarilyMelee() || (Config != null && Config.UseInvisible);
                if (wantMeleeRunIn)
                {
                    _meleePressureEndRealtime = Time.realtimeSinceStartup + 4.25f;
                    _ins.DebugLog($"[{BossName}] Phase 1: Running in for melee attack (4 seconds)");
                    ExecuteMeleeAttack(target);
                    if (Config != null && Config.UseInvisible)
                        Invoke(nameof(TeleportAwayAndWait), 4f);
                    else
                        Invoke(nameof(ChooseAndExecuteNextAttack), 4f);
                }
                else
                {
                    _meleePressureEndRealtime = 0f;
                    _ins.DebugLog($"[{BossName}] Phase 1: Ranged loadout — skipping melee run-in so primary weapons can fire");
                    Invoke(nameof(ChooseAndExecuteNextAttack), 0.08f);
                }

                // Match AttackCycleManager: do not start plugin strafe for ranged phases (blocks brain aim/fire).
                if (PluginStrafeWanted() && _strafeCoroutine == null && target != null)
                    StartStrafe(target, STRAFE_DURATION_CONTINUOUS);
            }

            private void TeleportAwayAndWait()
            {
                // Only teleport if UseInvisible is enabled
                if (Config == null || !Config.UseInvisible)
                {
                    // Skip teleportation, just proceed to next attack phase
                    Invoke(nameof(ChooseAndExecuteNextAttack), 2f);
                    return;
                }
                
                BasePlayer target = _ins.GetBossCombatTarget(Npc);
                if (target != null)
                {
                    if (_ins.GetCurrentTarget(Npc) == null)
                        _ins.SetTarget(Npc, target);
					_ins.DebugLog($"[{BossName}] Phase 2: Teleporting behind player and waiting (2 seconds)");
					Vector3 bossPos = transform.position;
					Vector3 playerPos = target.transform.position;
					float initialDistance = Vector3.Distance(bossPos, playerPos);
					
					// Enhanced: Reduced teleport distance to keep boss closer (was 12f, now 6-8f range)
					float desiredDist = 7f; // Closer distance for better engagement
					Vector3 forward = target.transform.forward; forward.y = 0f; forward.Normalize();
					Vector3 behind = -forward;
					float[] distOptions = new float[] { desiredDist, desiredDist - 1.5f, desiredDist + 1.5f, desiredDist - 3f, desiredDist + 3f };
					float[] angleOptions = new float[] { 0f, 20f, -20f, 35f, -35f, 60f, -60f };
					Vector3 pos = Vector3.zero;
					// If extremely close, try a shorter hop first so the boss phases through/behind
					if (initialDistance <= 1.5f)
					{
						float closeBack = 5.5f;
						Vector3 closeRetreat = playerPos + behind * closeBack;
						pos = GetPositionGhost(closeRetreat);
					}
					if (pos == Vector3.zero)
					{
						for (int i = 0; i < distOptions.Length && pos == Vector3.zero; i++)
						{
							for (int j = 0; j < angleOptions.Length && pos == Vector3.zero; j++)
							{
								Vector3 dir = Quaternion.Euler(0f, angleOptions[j], 0f) * behind;
								Vector3 candidate = playerPos + dir * distOptions[i];
								pos = GetPositionGhost(candidate);
							}
						}
					}
					// Fallback sampling: away from player (from player toward current boss) with angle sweep
					if (pos == Vector3.zero)
					{
						Vector3 away = (bossPos - playerPos); away.y = 0f; if (away.sqrMagnitude < 0.01f) away = behind; away.Normalize();
						for (int i = 0; i < distOptions.Length && pos == Vector3.zero; i++)
						{
							for (int j = 0; j < angleOptions.Length && pos == Vector3.zero; j++)
							{
								Vector3 dir = Quaternion.Euler(0f, angleOptions[j], 0f) * away;
								Vector3 candidate = playerPos + dir * distOptions[i];
								pos = GetPositionGhost(candidate);
							}
						}
					if (pos == Vector3.zero)
					{
						// Absolute last resort: raw position
						pos = playerPos + away * desiredDist;
					}
					}
					pos = ClampHorizontalNearPlayer(pos, target, MaxCombatTeleportDistanceFromPlayer);
					pos = _ins.SnapBossTeleportPosition(Npc, pos, playerPos);
					
					_ins.DebugLog($"[{BossName}] Teleporting from {bossPos} to {pos} (distance: {Vector3.Distance(playerPos, pos)})", true);
                    
                    Invisible(true);
                    Npc.Brain.Navigator.Stop();
                    Npc.transform.position = pos;
                    Npc.Brain?.Navigator?.PlaceOnNavMesh(18f);
                    Vector3 lookAway = target.transform.position - pos; lookAway.y = 0f;
                    if (lookAway.sqrMagnitude > 0.001f) Npc.viewAngles = Quaternion.LookRotation(lookAway).eulerAngles;
					Invisible(false);
					// Keep moving during the wait window - ensure continuous strafe
					Npc.Brain.Navigator.Resume();
					if (PluginStrafeWanted() && _strafeCoroutine == null)
						StartStrafe(target, STRAFE_DURATION_CONTINUOUS);
                    
                    // Step 3: After 2 seconds, choose and execute attack
                    Invoke(nameof(ChooseAndExecuteNextAttack), 2f);
                }
            }

            private void ChooseAndExecuteNextAttack()
            {
                BasePlayer target = _ins.GetBossCombatTarget(Npc);
                if (target == null) return;
                if (_ins.GetCurrentTarget(Npc) == null)
                    _ins.SetTarget(Npc, target);
                
                _ins.DebugLog($"[{BossName}] Phase 3: Choosing attack type");
				// Ensure movement is active even if target is stationary
				ResumeMovement();
                if (PluginStrafeWanted() && _strafeCoroutine == null && target != null)
					StartStrafe(target, STRAFE_DURATION_CONTINUOUS);

                // Radius = 0: no ability loop - melee-only (this used to check radiusActions==null and missed Radius=0 with a valid config object)
                if (!_abilityRadiusTriggersLoop)
                {
                    _ins.DebugLog($"[{BossName}] No radius actions configured (Radius=0), using melee attack only");
                    if (!_isStationary)
                    {
                        Npc.Brain.Navigator.Resume();
                    }
                    if (PluginStrafeWanted() && _strafeCoroutine == null && target != null)
                        StartStrafe(target, STRAFE_DURATION_CONTINUOUS);
                    _aoeStandoffEndRealtime = 0f;
                    _meleePressureEndRealtime = Time.realtimeSinceStartup + 4.25f;
                    ExecuteMeleeAttack(target);
                    _attackCycleTimer = 6f; // 4s melee + 2s retreat
                    return;
                }

                // If helper NPCs were requested by cooldown, spawn them now (after teleport) and freeze boss during their lifetime
                if (_pendingNpcHelpers)
                {
                    if (IsPostSpawnRadiusAbilityGraceActive())
                    {
                        if (_ins?._config != null && _ins._config.Debug)
                            _ins.DebugLog($"[{BossName}] NPC helpers queued — waiting post-spawn grace ({PostSpawnRadiusAbilityGraceRemaining():F1}s left)", true);
                    }
                    else
                    {
                        _pendingNpcHelpers = false;
                        float duration = Mathf.Max(0f, _pendingNpcHelpersDuration);
                        int spawnedHelpers = 0;
                        for (int i = 0; i < radiusActions.NpcAbility.Count; i++)
                        {
                            // Pick a NavMesh-safe position around the boss
                            float angle = (360f / Mathf.Max(1, radiusActions.NpcAbility.Count)) * i + UnityEngine.Random.Range(-15f, 15f);
                            float radius = UnityEngine.Random.Range(3f, 8f);
                            Vector3 desired = transform.position + new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad), 0f, Mathf.Cos(angle * Mathf.Deg2Rad)) * radius;
                            Vector3 spawnPos = _ins.ResolveHelperSpawnNavMesh(desired, transform.position, TerrainRoamSpawn, _typeNavMesh);
                            if (spawnPos == Vector3.zero) spawnPos = GetPositionGhost(desired);
                            if (spawnPos == Vector3.zero) spawnPos = desired;

                            ScientistNPC helper = _ins.SpawnHelperNpc(spawnPos, radiusActions.NpcAbility.ConfigNpc, TerrainRoamSpawn, _typeNavMesh);
                            if (helper != null)
                            {
                                spawnedHelpers++;
                                Interface.Oxide.CallHook("OnBossSpawnedAdditionalNpc", Npc, helper);
                                Scientists.Add(helper);
                                BasePlayer primeTarget = ResolveCombatOrProximityTarget();
                                if (primeTarget != null) _ins.SetTarget(helper, primeTarget, true);
                            }
                        }
                        if (spawnedHelpers > 0)
                        {
                            KickSpawnedHelpersEngagement();
                            ScheduleStaggeredHelperEngagementKicks();
                            Invoke(DeleteAllScientists, duration);
                            BeginNpcHelpers(duration);
                            StartHelperAggroPulse();
                            ScheduleHelperEngagementKicks();
                            // Treat helper NPCs as an ability used this cycle to prevent repeats before AOEs are used
                            _roundRobinUsed.Add(6);
                            _lastAbilityUsed = 6;
                            _attackCycleTimer = 14f;
                            _ins.DebugLog($"[{BossName}] Chose AOE attack: 6 (NPC helpers), duration={duration}s");
                            
                            // If UseOnlyOneAbility is true, skip selecting other attacks this cycle
                            if (radiusActions.UseOnlyOneAbility) return;
                        }
                        else
                        {
                            _ins.DebugLog($"[{BossName}] NPC helper spawn failed (no valid positions). Not freezing; will retry later.");
                            // Continue to select other abilities even if NPC spawn failed
                        }
                    }
                    // Continue to select additional abilities if UseOnlyOneAbility is false
                }
                
                // Get available abilities (single pooled model): any in the configured pool that are ready/queued
                bool aoeSpawnGrace = IsPostSpawnRadiusAbilityGraceActive();
                List<int> aoeAttacks = new List<int>();
                if (!aoeSpawnGrace)
                {
                    if (_abilityPool.Contains(1) && _timeToSpikes <= 0) aoeAttacks.Add(1);
                    if (_abilityPool.Contains(2) && _timeToFire <= 0) aoeAttacks.Add(2);
                    if (_abilityPool.Contains(3) && _timeToFreeze <= 0) aoeAttacks.Add(3);
                    if (_abilityPool.Contains(4) && _timeToElectricShock <= 0) aoeAttacks.Add(4);
                    // Consider Animals/NPCs ready if either queued or cooldown reached 0
                    if (_abilityPool.Contains(5) && (_pendingAnimal || _timeToAnimal <= 0)) aoeAttacks.Add(5);
                    if (_abilityPool.Contains(6) && (_pendingNpcHelpers || _timeToNpc <= 0)) aoeAttacks.Add(6);
                }

                // Build or update the universe of enabled abilities for this round (keys: 1=Spikes,2=Fire,3=Ice,4=Electric,5=Animals,6=NPC)
                _roundRobinUniverse.Clear();
                if (radiusActions.TimeToSpikes != -1) _roundRobinUniverse.Add(1);
                if (radiusActions.TimeToFire != -1) _roundRobinUniverse.Add(2);
                if (radiusActions.TimeToFreeze != -1) _roundRobinUniverse.Add(3);
                if (radiusActions.TimeToElectricShock != -1) _roundRobinUniverse.Add(4);
                if (radiusActions.AnimalAbility != null && radiusActions.AnimalAbility.Time != -1) _roundRobinUniverse.Add(5);
                if (radiusActions.NpcAbility != null && radiusActions.NpcAbility.Time != -1) _roundRobinUniverse.Add(6);

                // If we've used every enabled ability at least once, reset the round
                bool allUsed = true;
                foreach (int key in _roundRobinUniverse) if (!_roundRobinUsed.Contains(key)) { allUsed = false; break; }
                if (allUsed) _roundRobinUsed.Clear();

                // Eligible this pick = (available now by cooldown/queue) minus (used this round)
                List<int> roundEligible = new List<int>();
                foreach (int a in aoeAttacks) if (!_roundRobinUsed.Contains(a)) roundEligible.Add(a);

                // Log a concise eligibility status for all enabled abilities
                LogAbilityEligibility(aoeAttacks, roundEligible);

                // 70% chance for AOE only if at least one round-eligible ability exists; otherwise force melee
                bool useAOE = roundEligible.Count > 0 && UnityEngine.Random.Range(0f, 1f) < 0.7f;

                if (useAOE)
                {
                    // When UseOnlyOneAbility is false, allow multiple abilities to be cast simultaneously
                    List<int> abilitiesToCast = new List<int>();
                    
                    if (radiusActions.UseOnlyOneAbility)
                    {
                        // Single ability mode: pick one as before
                        List<int> coreEligible = new List<int>();
                        foreach (int id in roundEligible) if (id >= 1 && id <= 4) coreEligible.Add(id);
                        int chosenAOE = (coreEligible.Count > 0)
                            ? coreEligible[UnityEngine.Random.Range(0, coreEligible.Count)]
                            : roundEligible[UnityEngine.Random.Range(0, roundEligible.Count)];
                        abilitiesToCast.Add(chosenAOE);
                    }
                    else
                    {
                        // Multiple ability mode: select 1-3 random abilities from eligible list
                        List<int> coreEligible = new List<int>();
                        foreach (int id in roundEligible) if (id >= 1 && id <= 4) coreEligible.Add(id);
                        
                        // Always include at least one core AOE if available
                        if (coreEligible.Count > 0)
                        {
                            int primaryAOE = coreEligible[UnityEngine.Random.Range(0, coreEligible.Count)];
                            abilitiesToCast.Add(primaryAOE);
                            
                            // 60% chance to add a second ability, 30% chance to add a third
                            List<int> remaining = new List<int>(roundEligible);
                            remaining.Remove(primaryAOE);
                            if (remaining.Count > 0 && UnityEngine.Random.Range(0f, 1f) < 0.6f)
                            {
                                int secondAOE = remaining[UnityEngine.Random.Range(0, remaining.Count)];
                                abilitiesToCast.Add(secondAOE);
                                remaining.Remove(secondAOE);
                                
                                if (remaining.Count > 0 && UnityEngine.Random.Range(0f, 1f) < 0.3f)
                                {
                                    int thirdAOE = remaining[UnityEngine.Random.Range(0, remaining.Count)];
                                    abilitiesToCast.Add(thirdAOE);
                                }
                            }
                        }
                        else
                        {
                            // No core AOEs available, just pick from all eligible
                            int chosenAOE = roundEligible[UnityEngine.Random.Range(0, roundEligible.Count)];
                            abilitiesToCast.Add(chosenAOE);
                        }
                    }
                    
                    _ins.DebugLog($"[{BossName}] Chose {abilitiesToCast.Count} AOE attack(s): {string.Join(", ", abilitiesToCast)}");
                    
                    // Execute all selected abilities
                    bool executedAny = false;
                    foreach (int chosenAOE in abilitiesToCast)
                    {
                        _lastAbilityUsed = chosenAOE;
                        _roundRobinUsed.Add(chosenAOE);
                        
                        // =================== AOE 5: Animals ===================
                        if (chosenAOE == 5)
                    {
                        if (IsPostSpawnRadiusAbilityGraceActive())
                        {
                            _roundRobinUsed.Remove(5);
                            if (_ins?._config != null && _ins._config.Debug)
                                _ins.DebugLog($"[{BossName}] Animals spawn skipped — post-spawn grace ({PostSpawnRadiusAbilityGraceRemaining():F1}s)", true);
                            continue;
                        }
                        _pendingAnimal = false;

                        // pick a target
                        BasePlayer curTarget = ResolveCombatOrProximityTarget();
                        if (curTarget == null)
                        {
                            _ins.DebugLog($"[{BossName}] Animals ability: no eligible player target; skipping spawn.", true);
                            _roundRobinUsed.Remove(5);
                            return;
                        }

                        // Gen2 map (prefab paths for internal spawn)
                        var gen2Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Tiger"]       = "assets/rust.ai/agents/tiger/tiger.prefab",
                            ["Panther"]     = "assets/rust.ai/agents/panther/panther.prefab",
                            ["Crocodile"]   = "assets/rust.ai/agents/crocodile/crocodile.prefab",
                            ["Snake"]       = "assets/rust.ai/agents/snake/snake.entity.prefab"
                        };

                        string requestedType = radiusActions.AnimalAbility.Type ?? string.Empty;
                        bool isGen2 = gen2Map.ContainsKey(requestedType);
                        string gen2PrefabPath = isGen2 ? gen2Map[requestedType] : null;

                        int toSpawn = Mathf.Max(0, radiusActions.AnimalAbility.Count);
                        if (toSpawn <= 0)
                        {
                            _ins.DebugLog($"[{BossName}] Animals ability count is 0; skipping spawn.", true);
                            return;
                        }

                        for (int i = 0; i < toSpawn; i++)
                        {
                            if (isGen2)
                            {
                                // === GEN2: INTERNAL prefab spawn (no APIs) ===
                                // Spawn near the current target like the F1 `entity.spawn <animal>`
                                Vector3 tpos = curTarget.transform.position;
                                Vector2 off2 = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(4f, 7f);
                                Vector3 spawnPos = new Vector3(tpos.x + off2.x, 0f, tpos.z + off2.y);
                                // Ground to terrain (simple grounding; let Gen2 FSM handle movement/logic)
                                spawnPos.y = TerrainMeta.HeightMap.GetHeight(spawnPos);

                                BaseEntity ent = GameManager.server.CreateEntity(gen2PrefabPath, spawnPos, Quaternion.identity);
                                if (ent != null)
                                {
                                    ent.enableSaving = false; // ephemeral helper, optional
                                    ent.Spawn();
                                    AnimalsAny.Add(ent);

                                    var nameForLog = string.IsNullOrEmpty(requestedType) ? "Gen2 Animal" : requestedType;
                                    _ins.DebugLog($"[{BossName}] Spawned {nameForLog} (Gen2) id={ent.net?.ID.Value} at {spawnPos}", true);
                                }
                                else
                                {
                                    var nameForLog = string.IsNullOrEmpty(requestedType) ? "Gen2 Animal" : requestedType;
                                    _ins.DebugLog($"[{BossName}] FAILED to create entity for {nameForLog} using '{gen2PrefabPath}'", true);
                                }
                            }
                            else
                            {
                                // === GEN1: original working path with NAVMESH SNAP + pathing kick (no AIFlags) ===
                                string prefab = radiusActions.AnimalAbility.Type == "Wolf"
                                    ? "assets/rust.ai/agents/wolf/wolf.prefab"
                                    : radiusActions.AnimalAbility.Type == "Bear"
                                        ? "assets/rust.ai/agents/bear/bear.prefab"
                                        : "assets/rust.ai/agents/bear/polarbear.prefab";

                                // --- compute a spawn point on the NAVMESH near the target ---
                                Vector3 tpos = curTarget.transform.position;

                                // random ring 3â€“6m around the player, then navmesh-snap (areaMask=1 Walkable)
                                Vector2 ring = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(3.0f, 6.0f);
                                Vector3 desired = new Vector3(tpos.x + ring.x, tpos.y + 1.0f, tpos.z + ring.y);

                                UnityEngine.AI.NavMeshHit nhit;
                                Vector3 spawnPos;
                                if (UnityEngine.AI.NavMesh.SamplePosition(desired, out nhit, 6f, 1))
                                {
                                    spawnPos = nhit.position;
                                }
                                else
                                {
                                    // fallback: terrain height, then second-chance smaller navmesh snap
                                    spawnPos = new Vector3(desired.x, TerrainMeta.HeightMap.GetHeight(desired), desired.z);
                                    if (UnityEngine.AI.NavMesh.SamplePosition(spawnPos, out nhit, 2f, 1))
                                        spawnPos = nhit.position;
                                }

                                var config = new JObject
                                {
                                    ["Prefab"] = prefab,
                                    ["Health"] = 200f,
                                    ["RoamRange"] = 12f,
                                    ["ChaseRange"] = 40f,
                                    ["SenseRange"] = 40f,
                                    ["ListenRange"] = 15f,
                                    ["AttackRange"] = 2.5f,
                                    ["CheckVisionCone"] = true,
                                    ["VisionCone"] = 120f,
                                    ["HostileTargetsOnly"] = false,
                                    ["AttackDamage"] = 25f,
                                    ["AttackRate"] = 1.5f,
                                    ["TurretDamageScale"] = _ins._config.TurretDamageScale,
                                    ["CanRunAwayWater"] = true,
                                    ["CanSleep"] = false,
                                    ["SleepDistance"] = 100f,
                                    ["Speed"] = 3.5f,
                                    ["HomePosition"] = "",
                                    ["MemoryDuration"] = 8f,
                                    ["States"] = new JArray { "RoamState", "ChaseState", "CombatState" }
                                };

                                var spawned = _ins.AnimalSpawn?.Call("SpawnAnimal", spawnPos, config) as BaseAnimalNPC;
                                if (spawned == null)
                                {
                                    _ins.DebugLog($"[{BossName}] AnimalSpawn.SpawnAnimal returned null for '{prefab}'", true);
                                    continue;
                                }

                                // Aggro nudge toward current target
                                AnimalBrain brain = spawned.brain;
                                if (brain?.Senses?.Memory != null)
                                    brain.Senses.Memory.SetKnown(curTarget, spawned, brain.Senses);

                                Vector3 dir2 = curTarget.transform.position - spawnPos; dir2.y = 0f;
                                if (dir2.sqrMagnitude > 0.01f)
                                    spawned.transform.rotation = Quaternion.LookRotation(dir2);

                                BaseNavigator nav = spawned.GetComponent<BaseNavigator>();
                                if (nav != null)
                                    nav.SetDestination(curTarget.transform.position, BaseNavigator.NavigationSpeed.Fast, 0f, 0f);

                                spawned.AttackTarget = curTarget;

                                Animals.Add(spawned);
                                _ins.DebugLog($"[{BossName}] Spawned {radiusActions.AnimalAbility.Type} id={spawned.net?.ID.Value}", true);
                            }
                        }

                        Invoke(DeleteAllAnimals, radiusActions.AnimalAbility.DespawnTime);
                        _timeToAnimal = radiusActions.AnimalAbility.Time == -1 ? -1 : Mathf.RoundToInt(radiusActions.AnimalAbility.Time * 2f);
                        executedAny = true;
                        if (radiusActions.UseOnlyOneAbility) break; // Exit loop if only one ability allowed
                    }
                    else if (chosenAOE == 6)
                    {
                        // =================== AOE 6: NPCs ===================
                        if (IsPostSpawnRadiusAbilityGraceActive())
                        {
                            _roundRobinUsed.Remove(6);
                            if (_ins?._config != null && _ins._config.Debug)
                                _ins.DebugLog($"[{BossName}] NPC helper spawn skipped — post-spawn grace ({PostSpawnRadiusAbilityGraceRemaining():F1}s)", true);
                        }
                        // NPC helpers: if not already queued, proactively execute spawn now
                        else if (!_pendingNpcHelpers)
                        {
                            float duration = Mathf.Max(0f, radiusActions.NpcAbility.DespawnTime);
                            int spawnedHelpers = 0;

                            for (int i = 0; i < radiusActions.NpcAbility.Count; i++)
                            {
                                // Pick a NavMesh-safe position around the boss
                                float angle = (360f / Mathf.Max(1, radiusActions.NpcAbility.Count)) * i + UnityEngine.Random.Range(-15f, 15f);
                                float radius = UnityEngine.Random.Range(3f, 8f);
                                Vector3 desired = transform.position + new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad), 0f, Mathf.Cos(angle * Mathf.Deg2Rad)) * radius;
                                Vector3 spawnPos = _ins.ResolveHelperSpawnNavMesh(desired, transform.position, TerrainRoamSpawn, _typeNavMesh);
                                if (spawnPos == Vector3.zero) spawnPos = GetPositionGhost(desired);
                                if (spawnPos == Vector3.zero) spawnPos = desired;

                                ScientistNPC helper = _ins.SpawnHelperNpc(spawnPos, radiusActions.NpcAbility.ConfigNpc, TerrainRoamSpawn, _typeNavMesh);
                                if (helper != null)
                                {
                                    spawnedHelpers++;
                                    Interface.Oxide.CallHook("OnBossSpawnedAdditionalNpc", Npc, helper);
                                    Scientists.Add(helper);
                                    BasePlayer primeTarget = ResolveCombatOrProximityTarget();
                                    if (primeTarget != null) _ins.SetTarget(helper, primeTarget, true);
                                }
                            }
                            if (spawnedHelpers > 0)
                            {
                                KickSpawnedHelpersEngagement();
                                ScheduleStaggeredHelperEngagementKicks();
                                Invoke(DeleteAllScientists, duration);
                                BeginNpcHelpers(duration);
                                StartHelperAggroPulse();
                                ScheduleHelperEngagementKicks();
                                // Start cooldown proactively to avoid immediate re-queuing
                                _timeToNpc = Mathf.Max(radiusActions.NpcAbility.Time, 10) * 2;
                                executedAny = true;
                                if (radiusActions.UseOnlyOneAbility) break; // Exit loop if only one ability allowed
                            }
                            else
                            {
                                _ins.DebugLog($"[{BossName}] NPC helper spawn failed (no valid positions). Not freezing and not starting cooldown.");
                                _roundRobinUsed.Remove(6);
                                // Don't break here - allow other abilities to execute
                            }
                        }
                        // If it was queued, fall through to queued spawn handling at the top
                    }
                    else
                    {
                        ExecuteAOEAttackStationary(chosenAOE, target);
                        executedAny = true;
                        // Don't break here - allow other abilities to execute when UseOnlyOneAbility is false
                    }
                    }
                    
                    // Set timer for full cycle: 8 seconds (AOE) + 4 seconds (next melee) + 2 seconds (retreat) = 14 seconds
                    if (executedAny) _attackCycleTimer = 14f;
                }
                else
                {
                    // Either we decided melee by chance, or no round-eligible AOE exists (to enforce round-robin)
                    if (roundEligible.Count == 0)
                    {
                        if (aoeAttacks.Count == 0) _ins.DebugLog($"[{BossName}] No AOE abilities are currently ready (see eligibility log above).");
                        else _ins.DebugLog($"[{BossName}] All ready AOE abilities are blocked by round-robin until others are used.");
                    }
                    if (BeltPrimarilyMelee())
                    {
                        _ins.DebugLog($"[{BossName}] Chose melee attack (no eligible AOE or chance), running in again");
                        if (!_isStationary)
                            Npc.Brain.Navigator.Resume();
                        if (PluginStrafeWanted() && _strafeCoroutine == null && target != null)
                            StartStrafe(target, STRAFE_DURATION_CONTINUOUS);
                        _aoeStandoffEndRealtime = 0f;
                        _meleePressureEndRealtime = Time.realtimeSinceStartup + 4.25f;
                        ExecuteMeleeAttack(target);
                        _attackCycleTimer = 6f;
                    }
                    else
                    {
                        _ins.DebugLog($"[{BossName}] No eligible AOE — ranged loadout: skip fake melee phase (do not block GrimmNPC gunfire)");
                        _aoeStandoffEndRealtime = 0f;
                        _meleePressureEndRealtime = 0f;
                        StopStrafe();
                        if (!_isStationary && Npc?.Brain?.Navigator != null)
                        {
                            Npc.Brain.Navigator.Resume();
                            Npc.Brain.Navigator.SetNavMeshEnabled(true);
                        }
                        _attackCycleTimer = 3f;
                    }
                }
            }

            private void LogAbilityEligibility(List<int> readyNow, List<int> roundEligible)
            {
                // Only log when debug is enabled to avoid spam
                if (_ins == null) return;
                
                List<string> parts = new List<string>();
                for (int id = 1; id <= 6; id++)
                {
                    if (!_roundRobinUniverse.Contains(id))
                    {
                        // Disabled in config
                        parts.Add($"{GetAbilityName(id)}: disabled");
                        continue;
                    }

                    // Determine readiness/cooldown
                    int ticks = 0;
                    bool queued = false;
                    switch (id)
                    {
                        case 1: ticks = _timeToSpikes; break;
                        case 2: ticks = _timeToFire; break;
                        case 3: ticks = _timeToFreeze; break;
                        case 4: ticks = _timeToElectricShock; break;
                        case 5: ticks = _timeToAnimal; queued = _pendingAnimal; break;
                        case 6: ticks = _timeToNpc; queued = _pendingNpcHelpers; break;
                    }

                    bool isReady = readyNow.Contains(id);
                    bool blockedByRound = !_roundRobinUsed.Contains(id) ? false : isReady && !roundEligible.Contains(id);

                    if (isReady)
                    {
                        if (blockedByRound) parts.Add($"{GetAbilityName(id)}: ready but blocked by round-robin");
                        else parts.Add($"{GetAbilityName(id)}: ready");
                    }
                    else
                    {
                        if (id == 5 || id == 6)
                        {
                            if (!queued)
                            {
                                if (ticks > 0) parts.Add($"{GetAbilityName(id)}: cooldown {Mathf.CeilToInt(ticks / 2f)}s");
                                else parts.Add($"{GetAbilityName(id)}: not queued yet");
                            }
                            else parts.Add($"{GetAbilityName(id)}: queued");
                        }
                        else
                        {
                            if (ticks > 0) parts.Add($"{GetAbilityName(id)}: cooldown {Mathf.CeilToInt(ticks / 2f)}s");
                            else parts.Add($"{GetAbilityName(id)}: not ready");
                        }
                    }
                }

                if (parts.Count > 0)
                {
                    string graceNote = IsPostSpawnRadiusAbilityGraceActive()
                        ? $"[post-spawn grace {PostSpawnRadiusAbilityGraceRemaining():F1}s — no radius AOEs/helpers yet] "
                        : "";
                    _ins.DebugLog($"[{BossName}] {graceNote}Ability eligibility -> {string.Join("; ", parts.ToArray())}");
                }
            }

            private string GetAbilityName(int id)
            {
                switch (id)
                {
                    case 1: return "Spikes";
                    case 2: return "Fire";
                    case 3: return "Ice";
                    case 4: return "Electric";
                    case 5: return "Animals";
                    case 6: return "NPCs";
                }
                return "Unknown";
            }

            // [LEGACY] Legacy method - mostly empty, logic handled by attack cycle system
            private void CheckGhost(BasePlayer target)
            {
                if (target == null) return;
                // Legacy method - logic now handled by attack cycle system (TeleportAwayAndWait, etc.)
                // Distance check kept for potential future use
                float distanceToTarget = Vector3.Distance(transform.position, target.transform.position);
            }



            private void ExecuteMeleeAttack(BasePlayer target)
            {
                // Only teleport if UseInvisible is enabled
                if (Config != null && Config.UseInvisible)
                {
                    // Teleport close to player for melee attack (5-7 meter distance to prevent clipping inside player)
                    Vector3 directionToPlayer = (target.transform.position - transform.position).normalized;
                    Vector3 attackPosition = target.transform.position - directionToPlayer * UnityEngine.Random.Range(4f, 6f);
                    Vector3 pos = GetPositionGhost(attackPosition);
                    if (pos != Vector3.zero)
                        pos = ClampHorizontalNearPlayer(pos, target, MaxCombatTeleportDistanceFromPlayer);
                    if (pos != Vector3.zero)
                        pos = _ins.SnapBossTeleportPosition(Npc, pos, target.transform.position);
                    
                    if (pos != Vector3.zero)
                    {
                        Invisible(true);
                        Npc.Brain.Navigator.Stop();
                        Npc.transform.position = pos;
                        Npc.Brain?.Navigator?.PlaceOnNavMesh(18f);
                        RecoverCombatNavigation(bypassCooldown: true);
                        Vector3 lookMelee = target.transform.position - transform.position; lookMelee.y = 0f;
                        if (lookMelee.sqrMagnitude > 0.001f) Npc.viewAngles = Quaternion.LookRotation(lookMelee).eulerAngles;
                        Invisible(false);
                        // Immediately resume movement and continue strafing after teleport
                        ResumeMovement();
                        if (PluginStrafeWanted() && _strafeCoroutine == null)
                            StartStrafe(target, STRAFE_DURATION_CONTINUOUS);
                    }
                }
                else
                {
                    if (PluginStrafeWanted() && _strafeCoroutine == null && target != null)
                        StartStrafe(target, STRAFE_DURATION_CONTINUOUS);
                }
                // Note: TeleportAwayAndWait is scheduled by StartStructuredAttackCycle, not here
                // This prevents double-scheduling when UseInvisible is enabled
            }

            private void TeleportAwayAfterMelee()
            {
                // Only teleport if UseInvisible is enabled
                if (Config == null || !Config.UseInvisible) return;
                
                BasePlayer target = _ins.GetBossCombatTarget(Npc);
                if (target != null)
                {
                    if (_ins.GetCurrentTarget(Npc) == null)
                        _ins.SetTarget(Npc, target);
					// Prefer teleporting behind the player's facing direction to force turns
					Vector3 bossPos = transform.position;
					Vector3 playerPos = target.transform.position;
					float initialDistance = Vector3.Distance(bossPos, playerPos);
					// Enhanced: Reduced teleport distance to keep boss closer (was 10-14f, now 6-9f range)
					float backDist = UnityEngine.Random.Range(6f, 9f);
					Vector3 forward = target.transform.forward; forward.y = 0f; forward.Normalize();
					Vector3 behind = -forward;
					float[] distOptions = new float[] { backDist, Mathf.Max(5f, backDist - 1.5f), backDist + 1.5f, backDist - 3f, backDist + 3f };
					float[] angleOptions = new float[] { 0f, 20f, -20f, 35f, -35f, 60f, -60f };
					Vector3 pos = Vector3.zero;
					// Enhanced: If extremely close, try a shorter hop (reduced from 5.5f to 4f to stay closer)
					if (initialDistance <= 1.5f)
					{
						float closeBack = 4f;
						Vector3 closeRetreat = playerPos + behind * closeBack;
						pos = GetPositionGhost(closeRetreat);
					}
					if (pos == Vector3.zero)
					{
						for (int i = 0; i < distOptions.Length && pos == Vector3.zero; i++)
						{
							for (int j = 0; j < angleOptions.Length && pos == Vector3.zero; j++)
							{
								Vector3 dir = Quaternion.Euler(0f, angleOptions[j], 0f) * behind;
								Vector3 candidate = playerPos + dir * distOptions[i];
								pos = GetPositionGhost(candidate);
							}
						}
					}
					// Fallback: original away-from-player behavior with angle sweep
					if (pos == Vector3.zero)
					{
						Vector3 away = (bossPos - playerPos); away.y = 0f; if (away.sqrMagnitude < 0.01f) away = behind; away.Normalize();
						for (int i = 0; i < distOptions.Length && pos == Vector3.zero; i++)
						{
							for (int j = 0; j < angleOptions.Length && pos == Vector3.zero; j++)
							{
								Vector3 dir = Quaternion.Euler(0f, angleOptions[j], 0f) * away;
								Vector3 candidate = playerPos + dir * distOptions[i];
								pos = GetPositionGhost(candidate);
							}
						}
						if (pos == Vector3.zero)
						{
							pos = playerPos + away * backDist;
						}
					}
					if (pos != Vector3.zero)
						pos = ClampHorizontalNearPlayer(pos, target, MaxCombatTeleportDistanceFromPlayer);
					if (pos != Vector3.zero)
						pos = _ins.SnapBossTeleportPosition(Npc, pos, playerPos);
                    
                    if (pos != Vector3.zero)
                    {
                        Invisible(true);
                        Npc.Brain.Navigator.Stop();
                        Npc.transform.position = pos;
                        Npc.Brain?.Navigator?.PlaceOnNavMesh(18f);
                        RecoverCombatNavigation(bypassCooldown: true);
                        Vector3 lookAfter = target.transform.position - transform.position; lookAfter.y = 0f;
                        if (lookAfter.sqrMagnitude > 0.001f) Npc.viewAngles = Quaternion.LookRotation(lookAfter).eulerAngles;
                        Invisible(false);
					// Immediately resume movement and continue strafing
					ResumeMovement();
					if (PluginStrafeWanted() && _strafeCoroutine == null)
						StartStrafe(target, STRAFE_DURATION_CONTINUOUS);
                    }
                }
            }

            private void ExecuteAOEAttackStationary(int attackType, BasePlayer target)
            {
                _ins.DebugLog($"[{BossName}] Executing stationary AOE attack type {attackType}");
                _meleePressureEndRealtime = 0f;
                _aoeStandoffEndRealtime = Time.realtimeSinceStartup + 1.2f;
                
				// Keep navigation active with continuous strafe during AOE to avoid standing still
				_isStationary = false;
				if (PluginStrafeWanted() && _strafeCoroutine == null)
					StartStrafe(target, STRAFE_DURATION_CONTINUOUS);
                
                // Ensure navigator is active for movement (GrimmNPC handles navmesh configuration)
				if (Npc.Brain != null && Npc.Brain.Navigator != null)
				{
					Npc.Brain.Navigator.Resume();
				}

                List<BasePlayer> validPlayers = new List<BasePlayer>(Players.Count);
                foreach (BasePlayer p in Players)
                    if (CanTargetPlayerForAOE(p)) validPlayers.Add(p);
                _ins.DebugLog($"[{BossName}] Total players in range: {Players.Count}, Valid AOE players: {validPlayers.Count}");
                
                if (validPlayers.Count == 0) 
                {
                    _ins.DebugLog($"[{BossName}] No valid players found, aborting AOE attack");
                    return;
                }

                // Ensure MultiPointAOE config exists
                if (radiusActions.MultiPointAOE == null)
                {
                    _ins.DebugLog($"[{BossName}] MultiPointAOE config is null! Creating default config.", true);
                    radiusActions.MultiPointAOE = new MultiPointAOEConfig
                    {
                        EnableMultiPointAOE = true,
                        AOELocationCount = 8,
                        WarningTime = 2.0f, // 2-second warning as requested
                        PatternRadius = 15f,
                        ShowWarningCircles = true,
                        WarningCircleColors = new Dictionary<string, string>
                        {
                            ["Spikes"] = "green",
                            ["Fire"] = "red",
                            ["Ice"] = "blue",
                            ["Electric"] = "purple"
                        }
                    };
                }

                // Debug: Log AOE config values
                _ins.DebugLog($"[{BossName}] AOE Config - Enabled: {radiusActions.MultiPointAOE.EnableMultiPointAOE}, Warning: {radiusActions.MultiPointAOE.ShowWarningCircles}, WarningTime: {radiusActions.MultiPointAOE.WarningTime}s, Count: {radiusActions.MultiPointAOE.AOELocationCount}", true);

                switch (attackType)
                {
                    case 1: // Spikes
                        _timeToSpikes = Mathf.Max(radiusActions.TimeToSpikes, 10) * 2; // Convert to 0.5s ticks (10 seconds = 20 ticks)
                        _ins.DebugLog($"[{BossName}] Set spike cooldown to {_timeToSpikes/2} seconds ({_timeToSpikes} ticks). Starting coroutine with {validPlayers.Count} players", true);
                        StartCoroutine(MultiPointSpikesAbility(validPlayers, radiusActions.MultiPointAOE));
                        break;
                    case 2: // Fire
                        _timeToFire = Mathf.Max(radiusActions.TimeToFire, 10) * 2; // Convert to 0.5s ticks (10 seconds = 20 ticks)
                        _ins.DebugLog($"[{BossName}] Set fire cooldown to {_timeToFire/2} seconds ({_timeToFire} ticks). MultiPointAOE enabled: {radiusActions.MultiPointAOE?.EnableMultiPointAOE}", true);
                        _fireBallCoroutine = ServerMgr.Instance.StartCoroutine(MultiPointFireBallAbility(validPlayers, radiusActions.MultiPointAOE));
                        break;
                    case 3: // Ice
                        _timeToFreeze = Mathf.Max(radiusActions.TimeToFreeze, 10) * 2; // Convert to 0.5s ticks (10 seconds = 20 ticks)
                        _ins.DebugLog($"[{BossName}] Set ice cooldown to {_timeToFreeze/2} seconds ({_timeToFreeze} ticks). MultiPointAOE enabled: {radiusActions.MultiPointAOE?.EnableMultiPointAOE}", true);
                        _freezeCoroutine = ServerMgr.Instance.StartCoroutine(MultiPointFreezeAbility(validPlayers, radiusActions.MultiPointAOE));
                        break;
                    case 4: // ElectricShock
                        _timeToElectricShock = Mathf.Max(radiusActions.TimeToElectricShock, 10) * 2; // Convert to 0.5s ticks (10 seconds = 20 ticks)
                        _ins.DebugLog($"[{BossName}] Set electric cooldown to {_timeToElectricShock/2} seconds ({_timeToElectricShock} ticks). MultiPointAOE enabled: {radiusActions.MultiPointAOE?.EnableMultiPointAOE}", true);
                        _electricShockCoroutine = ServerMgr.Instance.StartCoroutine(MultiPointElectricShockAbility(validPlayers, radiusActions.MultiPointAOE));
                        break;
                }
            }
            
            private void ResumeMovement()
            {
                _isStationary = false;
                if (Npc != null && Npc.Brain != null && Npc.Brain.Navigator != null)
                {
                    // Resume navigator (GrimmNPC handles navmesh configuration)
                    Npc.Brain.Navigator.Resume();
                    Npc.Brain.Navigator.SetNavMeshEnabled(true);
                }
                else if (_ins != null && _ins._config != null && _ins._config.Debug)
                {
                    _ins.DebugLog($"[{Npc?.displayName ?? "Unknown"}] ResumeMovement: Navigator is null", true);
                }
            }

            private bool BeltPrimarilyMelee()
            {
                if (Config?.BeltItems == null || Config.BeltItems.Count == 0) return false;
                foreach (NpcBelt b in Config.BeltItems)
                {
                    string sn = b.ShortName?.ToLowerInvariant() ?? "";
                    if (string.IsNullOrEmpty(sn)) continue;
                    if (sn.Contains("rifle") || sn.Contains("pistol") || sn.Contains("smg") || sn.Contains("lmg")
                        || sn.Contains("shotgun") || sn.Contains("bow") || sn.Contains("crossbow")
                        || sn.Contains("launcher") || sn.Contains("flamethrower") || sn.Contains("mgl")
                        || sn.Contains("snowball") || sn.Contains("nailgun")) return false;
                }
                return true;
            }

            private float GetMeleeHoldDistance()
            {
                if (Config != null && Config.MeleeHoldDistance > 0.05f) return Config.MeleeHoldDistance;
                return DefaultMeleeHoldDistance;
            }

            private float GetAoeStandoffDistance()
            {
                if (Config != null && Config.AoeStandoffDistance > 0.5f) return Config.AoeStandoffDistance;
                return DefaultAoeStandoffDistance;
            }

            /// <summary>Clamp strafe targets to navmesh and line-of-path checks (walls / prefab blockers).</summary>
            private Vector3 SanitizeStrafeDestination(Vector3 fromXZ, Vector3 desired)
            {
                Vector3 d = desired;
                d.y = fromXZ.y;
                var agent = Npc?.NavAgent;
                int mask = agent != null && agent.areaMask != 0 ? agent.areaMask : NavAreaMonument;
                foreach (float sampleR in new float[] { 3f, 7f, 12f, 18f })
                {
                    if (!NavMesh.SamplePosition(d, out NavMeshHit hit, sampleR, mask))
                        continue;
                    Vector3 cand = hit.position;
                    cand.y = fromXZ.y;
                    if (IsPathClearForBoss(fromXZ, cand))
                        return cand;
                }
                foreach (float sampleR in new float[] { 6f, 14f, 24f })
                {
                    if (!NavMesh.SamplePosition(d, out NavMeshHit hit, sampleR, NavAreaTerrain))
                        continue;
                    Vector3 cand = hit.position;
                    cand.y = fromXZ.y;
                    if (IsPathClearForBoss(fromXZ, cand))
                        return cand;
                }
                return fromXZ;
            }

			private void StartStrafe(BasePlayer target, float duration)
			{
				StopStrafe();
				if (target == null || Npc == null || Npc.Brain == null)
				{
					_ins.DebugLog($"[{Npc?.displayName ?? "Unknown"}] StartStrafe: FAILED - target={target != null}, Npc={Npc != null}, Brain={Npc?.Brain != null}", true);
					return;
				}
				_strafeLegEndRealtime = 0f;
				_strafeCoroutine = ServerMgr.Instance.StartCoroutine(StrafeRoutine(target, duration));
				_ins.DebugLog($"[{BossName}] StartStrafe: Started strafe coroutine for target {target.displayName}, duration={duration}", true);
			}

			private void StopStrafe()
			{
				if (_strafeCoroutine != null)
				{
					ServerMgr.Instance.StopCoroutine(_strafeCoroutine);
					_strafeCoroutine = null;
				}
			}

			private IEnumerator StrafeRoutine(BasePlayer target, float duration)
			{
				// Continuous strafe - retarget on "legs" (1â€“2s) so the boss commits to each direction instead of jittering every 0.25s
				float end = duration >= STRAFE_DURATION_CONTINUOUS ? float.MaxValue : Time.realtimeSinceStartup + Mathf.Max(0.5f, duration);
				_strafeLegEndRealtime = 0f;

				while (Time.realtimeSinceStartup < end && Npc != null && Npc.IsExists())
				{
					if (_npcHelpersActive)
					{
						yield return CoroutineEx.waitForSeconds(0.35f);
						continue;
					}

					bool shouldContinue = false;

					BasePlayer currentTarget = _ins.GetBossCombatTarget(Npc);
					if (currentTarget == null || !currentTarget.IsExists())
					{
						_strafeCoroutine = null;
						ReleaseNavigatorForStockRoam();
						yield break;
					}
					if (_ins.GetCurrentTarget(Npc) == null)
						_ins.SetTarget(Npc, currentTarget);

					if (Npc.Brain != null && Npc.Brain.Navigator != null)
					{
						Npc.Brain.Navigator.Resume();
						RecoverCombatNavigation();
						Vector3 center = Npc.transform.position;
						float now = Time.realtimeSinceStartup;
						bool newLeg = now >= _strafeLegEndRealtime;

						if (now < _aoeStandoffEndRealtime)
						{
							if (newLeg)
							{
								_strafeLegEndRealtime = now + UnityEngine.Random.Range(0.7f, 1.15f);
								Vector3 p = currentTarget.transform.position;
								Vector3 away = center - p;
								away.y = 0f;
								if (away.sqrMagnitude < 0.01f) away = Npc.transform.forward;
								away.Normalize();
								Vector3 desiredBack = p + away * GetAoeStandoffDistance();
								desiredBack.y = center.y;
								desiredBack = SanitizeStrafeDestination(center, desiredBack);
								Npc.Brain.Navigator.SetDestination(desiredBack, BaseNavigator.NavigationSpeed.Fast, 0f, 2f);
							}
						}
						else
						{
							// Ranged + Radius=0 must NOT force permanent melee pressure (fights ScientistBrain ranged movement).
							// Melee-only when radius disabled AND belt is melee, or during explicit melee phase, or melee belt with abilities on.
							bool pressureMelee = (now < _meleePressureEndRealtime)
								|| (!_abilityRadiusTriggersLoop && BeltPrimarilyMelee())
								|| (BeltPrimarilyMelee() && _abilityRadiusTriggersLoop);
							Vector3 toPlayer = currentTarget.transform.position - center;
							toPlayer.y = 0f;
							float dist = toPlayer.magnitude;

							if (pressureMelee && dist > 0.05f)
							{
								float hold = GetMeleeHoldDistance();
								if (dist > hold + 0.5f)
								{
									if (newLeg)
									{
										_strafeLegEndRealtime = now + UnityEngine.Random.Range(0.55f, 0.85f);
										toPlayer.Normalize();
										Vector3 desiredClose = currentTarget.transform.position - toPlayer * hold;
										desiredClose.y = center.y;
										desiredClose = SanitizeStrafeDestination(center, desiredClose);
										Npc.Brain.Navigator.SetDestination(desiredClose, BaseNavigator.NavigationSpeed.Fast, 0f, 2f);
									}
								}
								else if (newLeg)
								{
									_strafeLegEndRealtime = now + UnityEngine.Random.Range(1.75f, 3.1f);
									Vector3 perp = new Vector3(-toPlayer.z, 0f, toPlayer.x);
									if (perp.sqrMagnitude < 0.01f) perp = Npc.transform.right;
									perp.Normalize();
									float tight = UnityEngine.Random.Range(5.5f, 9.5f);
									Vector3 desired = center + perp * tight * (UnityEngine.Random.value < 0.5f ? 1f : -1f);
									Vector3 td = dist > 0.12f ? toPlayer / dist : Npc.transform.forward;
									desired += td * UnityEngine.Random.Range(-0.9f, 0.9f);
									desired.y = center.y;
									desired = SanitizeStrafeDestination(center, desired);
									Npc.Brain.Navigator.SetDestination(desired, BaseNavigator.NavigationSpeed.Normal, 0f, 2.5f);
								}
							}
							else if (newLeg)
							{
								// Ranged belt: avoid endless wide strafe — constant SetDestination prevents Scientist ranged attack cycles.
								if (!BeltPrimarilyMelee())
								{
									_strafeLegEndRealtime = now + UnityEngine.Random.Range(1.35f, 2.45f);
									if (toPlayer.sqrMagnitude < 0.01f) toPlayer = Npc.transform.forward;
									toPlayer.Normalize();
									Vector3 perpR = new Vector3(-toPlayer.z, 0f, toPlayer.x);
									perpR.Normalize();
									float side = UnityEngine.Random.Range(6f, 11f);
									Vector3 desiredR = center + perpR * side * (UnityEngine.Random.value < 0.5f ? 1f : -1f);
									desiredR.y = center.y;
									desiredR = SanitizeStrafeDestination(center, desiredR);
									Npc.Brain.Navigator.SetDestination(desiredR, BaseNavigator.NavigationSpeed.Normal, 0f, 2.5f);
								}
								else
								{
									_strafeLegEndRealtime = now + UnityEngine.Random.Range(1.65f, 2.85f);
									if (toPlayer.sqrMagnitude < 0.01f) toPlayer = Npc.transform.forward;
									toPlayer.Normalize();
									Vector3 perpWide = new Vector3(-toPlayer.z, 0f, toPlayer.x) * (UnityEngine.Random.value < 0.5f ? 1f : -1f);
									float radius = UnityEngine.Random.Range(7f, 12f);
									Vector3 desired = center + perpWide * radius;
									desired += toPlayer * UnityEngine.Random.Range(-1.8f, 1.8f);
									desired.y = center.y;
									desired = SanitizeStrafeDestination(center, desired);
									bool destinationSet = Npc.Brain.Navigator.SetDestination(desired, BaseNavigator.NavigationSpeed.Normal, 0f, 2.5f);
									if (!destinationSet && _ins._config != null && _ins._config.Debug)
										_ins.DebugLog($"[{BossName}] StrafeRoutine: SetDestination returned false - desired={desired}, Moving={Npc.Brain.Navigator.Moving}", true);
								}
							}
						}
					}
					else
					{
						if (_ins._config != null && _ins._config.Debug)
							_ins.DebugLog($"[{BossName}] StrafeRoutine: Navigator is null - cannot move", true);
						shouldContinue = true;
					}

					yield return CoroutineEx.waitForSeconds(0.35f);

					if (shouldContinue) continue;
				}
				
				// Always clear the coroutine reference when it ends (prevents stuck state)
				_strafeCoroutine = null;
				
				// Only stop if duration expired (not for continuous strafe)
				if (duration < STRAFE_DURATION_CONTINUOUS)
				{
					StopStrafe();
				}
			}

            private Vector3 GetPositionGhost(Vector3 pos)
            {
                // Apply confinement bounds if set
                if (_useBounds) pos = ClampToBounds(pos);
                
                // Check for walls/obstacles before accepting position
                if (Npc != null && Npc.transform != null)
                {
                    if (!IsPathClearForBoss(Npc.transform.position, pos))
                    {
                        // Path blocked - try to find alternative position within bounds
                        Vector3 alternative = FindAlternativeBossPosition(pos);
                        if (alternative != Vector3.zero)
                        {
                            pos = alternative;
                        }
                        else
                        {
                            // Can't find clear position - return zero to try next candidate
                            return Vector3.zero;
                        }
                    }
                }
                
                // Structure-surface fallback when position might be in air
                Vector3 structTop;
                if (_ins != null && _ins.GetStructureTop(pos, out structTop))
                {
                    Vector3 clamped = ClampToBounds(structTop);
                    if (Npc != null && Npc.transform != null && IsPathClearForBoss(Npc.transform.position, clamped))
                    {
                        return clamped;
                    }
                }
                
                // If bounds are set, try to find position inside bounds
                if (_useBounds)
                {
                    Vector3 inside = SampleInsideBounds(pos);
                    if (inside != Vector3.zero && (Npc == null || Npc.transform == null || IsPathClearForBoss(Npc.transform.position, inside)))
                    {
                        return inside;
                    }
                }
                
                // Return position if path is clear (GrimmNPC and Rust's navigator will handle navmesh/pathfinding)
                if (Npc != null && Npc.transform != null && IsPathClearForBoss(Npc.transform.position, pos))
                {
                    return pos;
                }
                
                return Vector3.zero;
            }
            
            // Enhanced: Check if path is clear of walls/obstacles for boss teleportation
            private bool IsPathClearForBoss(Vector3 from, Vector3 to)
            {
                if (from == Vector3.zero || to == Vector3.zero) return false;
                
                Vector3 direction = (to - from).normalized;
                float distance = Vector3.Distance(from, to);
                
                // Use same layerMask as BaseNavigator (10551552 = buildings, structures, etc.)
                int layerMask = 10551552;
                
                // Check for obstacles using Raycast
                RaycastHit hitInfo;
                Vector3 rayStart = from + Vector3.up * 0.5f;
                
                if (Physics.Raycast(rayStart, direction, out hitInfo, distance + 0.5f, layerMask))
                {
                    float hitDistance = Vector3.Distance(from, hitInfo.point);
                    if (hitDistance < distance * 0.9f)
                    {
                        BaseEntity hitEntity = hitInfo.collider?.GetComponentInParent<BaseEntity>();
                        // Enhanced: Also check for doors and gates (prevents teleporting through prison.gate.wall)
                        if (hitEntity != null)
                        {
                            string prefabName = hitEntity.ShortPrefabName?.ToLower() ?? "";
                            if (hitEntity is BuildingBlock || hitEntity is SimpleBuildingBlock || 
                                prefabName.Contains("gate") || prefabName.Contains("door") || prefabName.Contains("prison"))
                            {
                                return false; // Blocked by building structure, door, or gate
                            }
                        }
                    }
                }
                
                // Also check with SphereCast for better detection
                float sphereRadius = 0.3f; // Boss radius approximation
                if (Physics.SphereCast(rayStart, sphereRadius, direction, out hitInfo, distance, layerMask))
                {
                    float hitDistance = Vector3.Distance(from, hitInfo.point);
                    if (hitDistance < distance * 0.8f)
                    {
                        BaseEntity hitEntity = hitInfo.collider?.GetComponentInParent<BaseEntity>();
                        // Enhanced: Also check for doors and gates
                        if (hitEntity != null)
                        {
                            string prefabName = hitEntity.ShortPrefabName?.ToLower() ?? "";
                            if (hitEntity is BuildingBlock || hitEntity is SimpleBuildingBlock || 
                                prefabName.Contains("gate") || prefabName.Contains("door") || prefabName.Contains("prison"))
                            {
                                return false;
                            }
                        }
                    }
                }
                
                return true; // Path appears clear
            }
            
            // Enhanced: Find alternative position when path is blocked
            private Vector3 FindAlternativeBossPosition(Vector3 blockedPos)
            {
                // Try positions around the blocked destination
                for (int i = 0; i < 12; i++)
                {
                    float angle = (360f / 12f) * i;
                    float radius = 2f + (i % 3) * 1f; // Try different radii
                    Vector3 offset = Quaternion.Euler(0, angle, 0) * Vector3.forward * radius;
                    Vector3 candidate = blockedPos + offset;
                    
                    // Ensure candidate is within bounds
                    if (_useBounds) candidate = ClampToBounds(candidate);
                    
                    // Check if path is clear (GrimmNPC and Rust's navigator will handle navmesh/pathfinding)
                    if (Npc != null && Npc.transform != null && IsPathClearForBoss(Npc.transform.position, candidate))
                    {
                        return candidate;
                    }
                }
                
                return Vector3.zero; // No alternative found
            }

            private static float HorizontalDistanceXZ(Vector3 a, Vector3 b)
            {
                float dx = a.x - b.x, dz = a.z - b.z;
                return Mathf.Sqrt(dx * dx + dz * dz);
            }

            /// <summary>Keeps a world position within a horizontal ring around the player (does not change Y).</summary>
            private static Vector3 ClampHorizontalNearPlayer(Vector3 worldPos, BasePlayer player, float maxHoriz)
            {
                if (player == null) return worldPos;
                Vector3 p = player.transform.position;
                float dx = worldPos.x - p.x, dz = worldPos.z - p.z;
                float sqr = dx * dx + dz * dz;
                if (sqr <= maxHoriz * maxHoriz) return worldPos;
                float m = Mathf.Sqrt(sqr);
                float s = maxHoriz / m;
                return new Vector3(p.x + dx * s, worldPos.y, p.z + dz * s);
            }

            /// <summary>Find a navmesh-safe point near the player for path-fail / invis recovery (never used for long-range snap to spawn).</summary>
            private bool GetNavRelocateNearPlayer(BasePlayer player, out Vector3 result)
            {
                result = Vector3.zero;
                if (player == null || !player.IsConnected) return false;
                Vector3 center = player.transform.position;
                float[] radii = { 5f, 8f, 11f, 14f, 17f, MaxCombatTeleportDistanceFromPlayer };
                float[] anglesDeg = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };
                foreach (float r in radii)
                {
                    foreach (float a in anglesDeg)
                    {
                        Vector3 flat = Quaternion.Euler(0f, a, 0f) * Vector3.forward * r;
                        Vector3 cand = center + new Vector3(flat.x, 0f, flat.z);
                        Vector3 ghost = GetPositionGhost(cand);
                        if (ghost == Vector3.zero) continue;
                        if (HorizontalDistanceXZ(ghost, center) > MaxCombatTeleportDistanceFromPlayer + 0.5f) continue;
                        result = ghost;
                        return true;
                    }
                }
                NavMeshHit hit;
                int mask = Npc?.NavAgent != null ? Npc.NavAgent.areaMask : NavMesh.AllAreas;
                if (NavMesh.SamplePosition(center, out hit, MaxCombatTeleportDistanceFromPlayer, mask) &&
                    HorizontalDistanceXZ(hit.position, center) <= MaxCombatTeleportDistanceFromPlayer + 0.5f)
                {
                    result = hit.position;
                    return true;
                }
                if (NavMesh.SamplePosition(center, out hit, MaxCombatTeleportDistanceFromPlayer, NavMesh.AllAreas) &&
                    HorizontalDistanceXZ(hit.position, center) <= MaxCombatTeleportDistanceFromPlayer + 0.5f)
                {
                    result = hit.position;
                    return true;
                }
                return false;
            }

            private void CheckPath(BasePlayer target)
            {
                if (_timeToGoHome > 0)
                {
                    _timeToGoHome--;
                    if (_timeToGoHome == 0)
                    {
                        Npc.Brain.Navigator.Stop();
                        BasePlayer anchor = target;
                        if (anchor == null || !anchor.IsConnected)
                            anchor = FindFirstStrictTargetablePlayer();
                        Vector3 relocateTo = _homePosition;
                        if (anchor != null && anchor.IsConnected)
                        {
                            if (GetNavRelocateNearPlayer(anchor, out Vector3 nearPlayer))
                                relocateTo = nearPlayer;
                            else
                            {
                                Vector3 stepToward = Vector3.MoveTowards(Npc.transform.position, anchor.transform.position, 8f);
                                Vector3 g = GetPositionGhost(stepToward);
                                if (g != Vector3.zero)
                                    relocateTo = ClampHorizontalNearPlayer(g, anchor, MaxCombatTeleportDistanceFromPlayer);
                                else
                                    relocateTo = ClampHorizontalNearPlayer(Npc.transform.position, anchor, MaxCombatTeleportDistanceFromPlayer);
                            }
                        }
                        relocateTo = _ins.SnapBossTeleportPosition(Npc, relocateTo, anchor != null ? anchor.transform.position : relocateTo);
                        Npc.transform.position = relocateTo;
                        Npc.Brain?.Navigator?.PlaceOnNavMesh(18f);
                        Npc.Brain.Navigator.Resume();
                        Npc.Brain.Navigator.SetNavMeshEnabled(true);
                        Invisible(false);
                        _timeToInvis = 5;
                        _timeToGoHome = 0;
                    }
                    return;
                }

                if (target == null) return;

                if (IsPath(target.transform.position)) _timeToInvis = 5;
                else
                {
                    if (_timeToInvis > 0)
                    {
                        _timeToInvis--;
                        if (_timeToInvis == 0)
                        {
                            Invisible(true);
                            _timeToGoHome = 10;
                        }
                    }
                }
            }

            private bool IsPath(Vector3 pos)
            {
                // Simplified: Check if position is within reasonable distance and path is clear
                // GrimmNPC and Rust's navigator handle pathfinding, so we just check basic reachability
                if (Npc == null || Npc.transform == null) return false;
                
                float distance = Vector3.Distance(transform.position, pos);
                
                // If very close, assume reachable
                if (distance < 3f) return true;
                
                // Check if path is clear of obstacles (GrimmNPC will handle navmesh pathfinding)
                return IsPathClearForBoss(transform.position, pos);
            }

            private void Invisible(bool enabled)
            {
                Effect.server.Run("assets/prefabs/weapons/flashbang/effects/fx-flashbang-boom.prefab", transform.position, Vector3.up, null, true);
                Npc.limitNetworking = enabled;
                if (enabled) Npc.Brain.Navigator.Speed *= 2f;
                else Npc.Brain.Navigator.Speed /= 2f;
            }
        }
        internal class CustomSphereCollider : FacepunchBehaviour
        {
            private SphereCollider _sphereCollider;
            private ControllerBoss _controller;
            private Transform _transform;

            internal void InitData(ControllerBoss controller, float proximityRadius)
            {
                _controller = controller;
                _transform = controller.transform;

                gameObject.layer = 3;
                _sphereCollider = gameObject.AddComponent<SphereCollider>();
                _sphereCollider.isTrigger = true;
                _sphereCollider.radius = proximityRadius;

                InvokeRepeating(UpdatePosition, 0, 1f);
            }

            private void OnDestroy() => CancelInvoke(UpdatePosition);

            private void OnTriggerEnter(Collider other)
            {
                BasePlayer player = other.GetComponentInParent<BasePlayer>();
                if (player.IsPlayer()) _controller.Players.Add(player);
            }

            private void OnTriggerExit(Collider other)
            {
                BasePlayer player = other.GetComponentInParent<BasePlayer>();
                if (player.IsPlayer()) _controller.Players.Remove(player);
            }

            private void UpdatePosition() => transform.position = _transform.position;
        }
        #endregion Controller

        #region Spawn Loot
        private void OnCorpsePopulate(ScientistNPC entity, NPCPlayerCorpse corpse)
        {
            if (entity == null || entity.net == null) return;
            ulong scientistId = entity.net.ID.Value;
            if (_controllers.ContainsKey(scientistId))
            {
                _controllers.Remove(scientistId);
                UnregisterNpcFromGrimmNpc(entity);

                NpcConfig config = Configs.FirstOrDefault(x => x.Name == entity.displayName);

                if (!config.DisableTimer)
                {
                    timer.In(UnityEngine.Random.Range(config.MinTime, config.MaxTime), () =>
                    {
                        _whatSpawnBosses.Add(config.Name);
                        CheckSpawnBoss();
                    });
                }

                BasePlayer attacker = entity.lastAttacker as BasePlayer;

                if (attacker.IsPlayer())
                {
                    if (config.IsChat) AlertToAllPlayers("Finish", _config.Prefix, attacker.displayName, entity.displayName, MapHelper.GridToString(MapHelper.PositionToGrid(entity.transform.position)));
                    SendBalance(attacker.userID, config.Economic);
                }

                Interface.Oxide.CallHook("OnBossKilled", entity, attacker);

                if (!string.IsNullOrEmpty(config.CratePrefab))
                {
                    BaseEntity crate = GameManager.server.CreateEntity(config.CratePrefab, entity.transform.position, entity.transform.rotation);
                    if (crate == null) _ins.PrintWarning($"Unknown entity! ({config.CratePrefab})");
                    else
                    {
                        crate.enableSaving = false;
                        crate.Spawn();
                        if (_config.Pve && plugins.Exists("PveMode")) PveMode.Call("CrateAddScientistPveMode", crate.net.ID.Value, scientistId);
                    }
                }

                NextTick(() =>
                {
                    if (corpse == null) return;
                    ItemContainer container = corpse.containers[0];
                    if (config.TypeLootTable == 0)
                    {
                        for (int i = container.itemList.Count - 1; i >= 0; i--)
                        {
                            Item item = container.itemList[i];
                            if (config.WearItems.Any(x => x.ShortName == item.info.shortname))
                            {
                                item.RemoveFromContainer();
                                item.Remove();
                            }
                        }
                        return;
                    }
                    if (config.TypeLootTable == 2 || config.TypeLootTable == 3)
                    {
                        if (config.IsRemoveCorpse && !corpse.IsDestroyed) corpse.Kill();
                        return;
                    }
                    container.ClearItemsContainer();
                    if (config.TypeLootTable == 4 || config.TypeLootTable == 5) AddToContainerPrefab(container, config.PrefabLootTable);
                    if (config.TypeLootTable == 1 || config.TypeLootTable == 5) AddToContainerItem(container, config.OwnLootTable);
                    if (config.IsRemoveCorpse && !corpse.IsDestroyed) corpse.Kill();
                });
            }
            else if (_controllers.Any(x => x.Value.Scientists.Contains(entity)))
            {
                foreach (ControllerBoss c in _controllers.Values)
                {
                    if (!c.Scientists.Contains(entity)) continue;
                    c.Scientists.Remove(entity);
                    UnregisterNpcFromGrimmNpc(entity);
                    break;
                }
                NextTick(() =>
                {
                    if (corpse == null) return;
                    corpse.containers[0].ClearItemsContainer();
                    if (!corpse.IsDestroyed) corpse.Kill();
                });
            }
        }

        private object CanPopulateLoot(ScientistNPC entity, NPCPlayerCorpse corpse)
        {
            if (entity == null || entity.net == null) return null;
            if (_controllers.ContainsKey(entity.net.ID.Value))
            {
                NpcConfig config = Configs.FirstOrDefault(x => x.Name == entity.displayName);
                if (config.TypeLootTable == 2) return null;
                else return true;
            }
            return null;
        }

        private object OnCustomLootNPC(NetworkableId netID)
        {
            if (_controllers.ContainsKey(netID.Value))
            {
                ScientistNPC entity = _controllers[netID.Value].Npc;
                NpcConfig config = Configs.FirstOrDefault(x => x.Name == entity.displayName);
                if (config.TypeLootTable == 3) return null;
                else return true;
            }
            return null;
        }

        private object OnCorpsePopulate(LootableCorpse corpse)
        {
            if (corpse == null) return null;

            NpcConfig config = Configs.FirstOrDefault(x => x.Name == corpse.playerName);
            if (config == null) return null;

            if (config.TypeLootTable == 6) return null;
            else return true;
        }

        private void AddToContainerPrefab(ItemContainer container, PrefabLootTableConfig lootTable)
        {
            HashSet<string> prefabsInContainer = new HashSet<string>();
            container.capacity = 36;
            if (lootTable.UseCount)
            {
                int count = 0, max = UnityEngine.Random.Range(lootTable.Min, lootTable.Max + 1);
                while (count < max)
                {
                    foreach (PrefabConfig prefab in lootTable.Prefabs)
                    {
                        if (prefabsInContainer.Count < lootTable.Prefabs.Count && prefabsInContainer.Contains(prefab.PrefabDefinition)) continue;
                        if (UnityEngine.Random.Range(0f, 100f) > prefab.Chance) continue;
                        SpawnIntoContainer(container, prefab.PrefabDefinition);
                        if (!prefabsInContainer.Contains(prefab.PrefabDefinition)) prefabsInContainer.Add(prefab.PrefabDefinition);
                        count++;
                        if (count == max)
                        {
                            prefabsInContainer = null;
                            return;
                        }
                    }
                }
            }
            else
            {
                foreach (PrefabConfig prefab in lootTable.Prefabs)
                {
                    if (prefabsInContainer.Contains(prefab.PrefabDefinition)) continue;
                    if (UnityEngine.Random.Range(0f, 100f) > prefab.Chance) continue;
                    SpawnIntoContainer(container, prefab.PrefabDefinition);
                    prefabsInContainer.Add(prefab.PrefabDefinition);
                }
            }
            prefabsInContainer = null;
        }

        private void SpawnIntoContainer(ItemContainer container, string prefab)
        {
            if (_allLootSpawnSlots.ContainsKey(prefab))
            {
                foreach (LootContainer.LootSpawnSlot lootSpawnSlot in _allLootSpawnSlots[prefab])
                    for (int j = 0; j < lootSpawnSlot.numberToSpawn; j++)
                        if (UnityEngine.Random.Range(0f, 1f) <= lootSpawnSlot.probability)
                            lootSpawnSlot.definition.SpawnIntoContainer(container);
            }
            else _allLootSpawn[prefab].SpawnIntoContainer(container);
        }

        private void AddToContainerItem(ItemContainer container, LootTableConfig lootTable)
        {
            HashSet<int> indexMove = new HashSet<int>();
            if (lootTable.UseCount)
            {
                int count = UnityEngine.Random.Range(lootTable.Min, lootTable.Max + 1);
                while (indexMove.Count < count)
                {
                    foreach (ItemConfig item in lootTable.Items)
                    {
                        if (indexMove.Contains(lootTable.Items.IndexOf(item))) continue;
                        if (UnityEngine.Random.Range(0.0f, 100.0f) <= item.Chance)
                        {
                            Item newItem = item.IsBluePrint ? ItemManager.CreateByName("blueprintbase") : ItemManager.CreateByName(item.ShortName, UnityEngine.Random.Range(item.MinAmount, item.MaxAmount + 1), item.SkinID);
                            if (newItem == null)
                            {
                                PrintWarning($"Failed to create item! ({item.ShortName})");
                                continue;
                            }
                            if (item.IsBluePrint) newItem.blueprintTarget = ItemManager.FindItemDefinition(item.ShortName).itemid;
                            if (!string.IsNullOrEmpty(item.Name)) newItem.name = item.Name;
                            if (!string.IsNullOrEmpty(item.Text)) newItem.text = item.Text;
                            if (container.capacity < container.itemList.Count + 1) container.capacity++;
                            if (!newItem.MoveToContainer(container)) newItem.Remove();
                            else
                            {
                                indexMove.Add(lootTable.Items.IndexOf(item));
                                if (indexMove.Count == count) return;
                            }
                        }
                    }
                }
            }
            else
            {
                foreach (ItemConfig item in lootTable.Items)
                {
                    if (indexMove.Contains(lootTable.Items.IndexOf(item))) continue;
                    if (UnityEngine.Random.Range(0.0f, 100.0f) <= item.Chance)
                    {
                        Item newItem = item.IsBluePrint ? ItemManager.CreateByName("blueprintbase") : ItemManager.CreateByName(item.ShortName, UnityEngine.Random.Range(item.MinAmount, item.MaxAmount + 1), item.SkinID);
                        if (newItem == null)
                        {
                            PrintWarning($"Failed to create item! ({item.ShortName})");
                            continue;
                        }
                        if (item.IsBluePrint) newItem.blueprintTarget = ItemManager.FindItemDefinition(item.ShortName).itemid;
                        if (!string.IsNullOrEmpty(item.Name)) newItem.name = item.Name;
                        if (!string.IsNullOrEmpty(item.Text)) newItem.text = item.Text;
                        if (container.capacity < container.itemList.Count + 1) container.capacity++;
                        if (!newItem.MoveToContainer(container)) newItem.Remove();
                        else indexMove.Add(lootTable.Items.IndexOf(item));
                    }
                }
            }
        }

        private static void CheckLootTable(LootTableConfig lootTable)
        {
            lootTable.Items = lootTable.Items.OrderByQuickSort(x => x.Chance);
            if (lootTable.Max > lootTable.Items.Count) lootTable.Max = lootTable.Items.Count;
            if (lootTable.Min > lootTable.Max) lootTable.Min = lootTable.Max;
        }

        private void CheckPrefabLootTable(PrefabLootTableConfig lootTable)
        {
            List<PrefabConfig> prefabs = Pool.Get<List<PrefabConfig>>();
            foreach (PrefabConfig prefabConfig in lootTable.Prefabs)
            {
                if (prefabs.Any(x => x.PrefabDefinition == prefabConfig.PrefabDefinition)) PrintWarning($"Duplicate prefab removed from loot table! ({prefabConfig.PrefabDefinition})");
                else
                {
                    GameObject gameObject = GameManager.server.FindPrefab(prefabConfig.PrefabDefinition);
                    global::HumanNPC humanNpc = gameObject.GetComponent<global::HumanNPC>();
                    ScarecrowNPC scarecrowNPC = gameObject.GetComponent<ScarecrowNPC>();
                    LootContainer lootContainer = gameObject.GetComponent<LootContainer>();
                    if (humanNpc != null && humanNpc.LootSpawnSlots.Length != 0)
                    {
                        if (!_allLootSpawnSlots.ContainsKey(prefabConfig.PrefabDefinition)) _allLootSpawnSlots.Add(prefabConfig.PrefabDefinition, humanNpc.LootSpawnSlots);
                        prefabs.Add(prefabConfig);
                    }
                    else if (scarecrowNPC != null && scarecrowNPC.LootSpawnSlots.Length != 0)
                    {
                        if (!_allLootSpawnSlots.ContainsKey(prefabConfig.PrefabDefinition)) _allLootSpawnSlots.Add(prefabConfig.PrefabDefinition, scarecrowNPC.LootSpawnSlots);
                        prefabs.Add(prefabConfig);
                    }
                    else if (lootContainer != null && lootContainer.LootSpawnSlots.Length != 0)
                    {
                        if (!_allLootSpawnSlots.ContainsKey(prefabConfig.PrefabDefinition)) _allLootSpawnSlots.Add(prefabConfig.PrefabDefinition, lootContainer.LootSpawnSlots);
                        prefabs.Add(prefabConfig);
                    }
                    else if (lootContainer != null && lootContainer.lootDefinition != null)
                    {
                        if (!_allLootSpawn.ContainsKey(prefabConfig.PrefabDefinition)) _allLootSpawn.Add(prefabConfig.PrefabDefinition, lootContainer.lootDefinition);
                        prefabs.Add(prefabConfig);
                    }
                    else PrintWarning($"Unknown prefab removed! ({prefabConfig.PrefabDefinition})");
                }
            }
            lootTable.Prefabs = prefabs.OrderByQuickSort(x => x.Chance).ToList();
            Pool.FreeUnmanaged(ref prefabs);
            if (lootTable.Max > lootTable.Prefabs.Count) lootTable.Max = lootTable.Prefabs.Count;
            if (lootTable.Min > lootTable.Max) lootTable.Min = lootTable.Max;
        }

        private readonly Dictionary<string, LootSpawn> _allLootSpawn = new Dictionary<string, LootSpawn>();

        private readonly Dictionary<string, LootContainer.LootSpawnSlot[]> _allLootSpawnSlots = new Dictionary<string, LootContainer.LootSpawnSlot[]>();
        #endregion Spawn Loot

        #region NTeleportation
        private object CanTeleport(BasePlayer player, Vector3 to)
        {
            if (!player.IsPlayer()) return null;
            ControllerBoss controller = _controllers.Values.FirstOrDefault(x => x.Players.Contains(player));
            if (controller != null) controller.Players.Remove(player);
            return null;
        }
        #endregion NTeleportation

        #region Economy
        [PluginReference] private readonly Plugin Economics, ServerRewards, IQEconomic, XPerience;

        internal void SendBalance(ulong playerId, NpcEconomic economic)
        {
            if (plugins.Exists("Economics") && economic.Economics > 0) Economics?.Call("Deposit", playerId.ToString(), economic.Economics);
            if (plugins.Exists("ServerRewards") && economic.ServerRewards > 0) ServerRewards?.Call("AddPoints", playerId, economic.ServerRewards);
            if (plugins.Exists("IQEconomic") && economic.IQEconomic > 0) IQEconomic?.Call("API_SET_BALANCE", playerId, economic.IQEconomic);
            if (plugins.Exists("XPerience") && economic.XPerience > 0)
            {
                BasePlayer player = BasePlayer.FindByID(playerId);
                if (player != null) XPerience?.Call("GiveXP", player, economic.XPerience);
            }
        }
        #endregion Economy

        #region Alerts
        [PluginReference] private readonly Plugin GUIAnnouncements, DiscordMessages, Notify;

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

        private bool CanSendDiscordMessage() => _config.Discord.IsDiscord && !string.IsNullOrEmpty(_config.Discord.WebhookUrl) && _config.Discord.WebhookUrl != "https://support.discordapp.com/hc/en-us/articles/228383668-Intro-to-Webhooks";

        private void AlertToAllPlayers(string langKey, params object[] args)
        {
            if (CanSendDiscordMessage() && _config.Discord.Keys.Contains(langKey))
            {
                object fields = new[] { new { name = Title, value = ClearColorAndSize(GetMessage(langKey, null, args)), inline = false } };
                DiscordMessages?.Call("API_SendFancyMessage", _config.Discord.WebhookUrl, "", _config.Discord.EmbedColor, JsonConvert.SerializeObject(fields), null, this);
            }
            foreach (BasePlayer player in BasePlayer.activePlayerList) AlertToPlayer(player, GetMessage(langKey, player.UserIDString, args));
        }

        private void AlertToPlayer(BasePlayer player, string message)
        {
            if (_config.IsChat) PrintToChat(player, message);
            if (_config.GuiAnnouncements.IsGuiAnnouncements) GUIAnnouncements?.Call("CreateAnnouncement", ClearColorAndSize(message), _config.GuiAnnouncements.BannerColor, _config.GuiAnnouncements.TextColor, player, _config.GuiAnnouncements.ApiAdjustVPosition);
            if (_config.Notify.IsNotify && plugins.Exists("Notify")) Notify?.Call("SendNotify", player, Convert.ToInt32(_config.Notify.Type), message);
        }
        #endregion Alerts

        #region Spawn Position
        private HashSet<MonumentInfo> _monuments = new HashSet<MonumentInfo>();

        private readonly HashSet<string> _unnecessaryMonuments = new HashSet<string>
        {
            "Substation",
            "Outpost",
            "Bandit Camp",
            "Fishing Village",
            "Large Fishing Village",
            "Ranch",
            "Large Barn",
            "Ice Lake",
            "Mountain"
        };

        private static string GetNameMonument(MonumentInfo monument)
        {
            if (monument == null || monument.name == null || monument.displayPhrase?.english == null) return string.Empty;
            if (monument.name.Contains("harbor_1")) return "Small " + monument.displayPhrase.english.Replace("\n", string.Empty);
            if (monument.name.Contains("harbor_2")) return "Large " + monument.displayPhrase.english.Replace("\n", string.Empty);
            if (monument.name.Contains("desert_military_base_a")) return monument.displayPhrase.english.Replace("\n", string.Empty) + " A";
            if (monument.name.Contains("desert_military_base_b")) return monument.displayPhrase.english.Replace("\n", string.Empty) + " B";
            if (monument.name.Contains("desert_military_base_c")) return monument.displayPhrase.english.Replace("\n", string.Empty) + " C";
            if (monument.name.Contains("desert_military_base_d")) return monument.displayPhrase.english.Replace("\n", string.Empty) + " D";
            return monument.displayPhrase.english.Replace("\n", string.Empty);
        }

        private bool IsNecessaryMonument(MonumentInfo monument)
        {
            string name = GetNameMonument(monument);
            if (string.IsNullOrEmpty(name) || _unnecessaryMonuments.Contains(name)) return false;
            return Configs.Any(x => x.Monuments.Any(y => y.Name == name));
        }

        private Vector3 GetSpawnPos(NpcConfig config, out bool terrainRoamSpawn)
        {
            terrainRoamSpawn = false;
            List<string> results = Pool.Get<List<string>>();

            foreach (MonumentInfo monument in _monuments)
            {
                MonumentPositionsConfig monumentConfig = config.Monuments.FirstOrDefault(x => x.Name == GetNameMonument(monument));
                if (monumentConfig == null) continue;
                foreach (string position in monumentConfig.Positions) results.Add(monument.transform.TransformPoint(position.ToVector3()).ToString());
            }

            foreach (CustomMapConfig customMap in _customMaps)
                foreach (CustomMapBossPositionsConfig customMapBoss in customMap.Bosses)
                    if (customMapBoss.NameBoss == config.Name)
                        foreach (string position in customMapBoss.Positions)
                            results.Add(position);

            if (results.Count > 0)
            {
                Vector3 result = results.GetRandom().ToVector3();
                Pool.FreeUnmanaged(ref results);
                return result;
            }

            Pool.FreeUnmanaged(ref results);

            for (int attempt = 0; attempt < 48; attempt++)
            {
                Vector3 p = SampleRandomTerrainPosition();
                if (SnapSpawnToNavMesh(ref p, 100f))
                {
                    terrainRoamSpawn = true;
                    return p;
                }
            }

            return Vector3.zero;
        }

        public class CustomMapBossPositionsConfig
        {
            [JsonProperty(En ? "Boss Name" : "Название босса")] public string NameBoss { get; set; }
            [JsonProperty(En ? "List of positions" : "Список позиций")] public HashSet<string> Positions { get; set; }
        }

        public class CustomMapConfig
        {
            [JsonProperty(En ? "ID" : "Идентификатор")] public string ID { get; set; }
            [JsonProperty(En ? "List of bosses" : "Список боссов")] public HashSet<CustomMapBossPositionsConfig> Bosses { get; set; }
        }

        private void LoadCustomMapPositions()
        {
            EnsureBossMonsterDataDirectories();
            Puts("Loading files on the /oxide/data/BossMonster/CustomMap/ path has started...");
            foreach (string name in Interface.Oxide.DataFileSystem.GetFiles("BossMonster/CustomMap/"))
            {
                string fileName = name.Split('/').Last().Split('.').First();
                CustomMapConfig config = Interface.Oxide.DataFileSystem.ReadObject<CustomMapConfig>($"BossMonster/CustomMap/{fileName}");
                if (config == null)
                {
                    PrintError($"File {fileName} is corrupted and cannot be loaded!");
                    continue;
                }
                if (!string.IsNullOrEmpty(config.ID) && !_ids.Any(x => Math.Abs(x - Convert.ToSingle(config.ID)) < 0.001f))
                {
                    PrintWarning($"File {fileName} cannot be loaded on the current map!");
                    continue;
                }
                Puts($"File {fileName} has been loaded successfully!");
                _customMaps.Add(config);
            }
        }

        private readonly HashSet<CustomMapConfig> _customMaps = new HashSet<CustomMapConfig>();

        private readonly HashSet<float> _ids = new HashSet<float>();

        private void LoadIDs() { foreach (RANDSwitch entity in BaseNetworkable.serverEntities.OfType<RANDSwitch>()) _ids.Add(entity.transform.position.x + entity.transform.position.y + entity.transform.position.z); }
        #endregion Spawn Position

        #region API
        private ScientistNPC SpawnBoss(string name, Vector3 pos)
        {
            NpcConfig config = Configs.FirstOrDefault(x => x.Name == name);
            if (config == null) return null;

            ScientistNPC npc = SpawnBossDirectly(pos, config, false);
            if (npc == null) return null;

            FinalizeBossSpawn(npc, config, false, announceAndPve: false);

            return npc;
        }

        private void DestroyBoss(ScientistNPC entity)
        {
            if (entity == null) return;
            if (_controllers.ContainsKey(entity.net.ID.Value))
            {
                _controllers.Remove(entity.net.ID.Value);
                UnregisterNpcFromGrimmNpc(entity);
            }
            if (entity.IsExists()) entity.Kill();
        }

        private HashSet<ScientistNPC> GetAllBosses()
        {
            HashSet<ScientistNPC> result = new HashSet<ScientistNPC>();
            foreach (KeyValuePair<ulong, ControllerBoss> dic in _controllers) result.Add(dic.Value.Npc);
            return result;
        }
        #endregion API

        #region Commands
        [ChatCommand("WorldPos")]
        private void ChatCommandWorldPos(BasePlayer player)
        {
            if (!player.IsAdmin) return;
            Puts($"Position: {player.transform.position}");
            PrintToChat(player, $"Position: {player.transform.position}");
        }

        [ChatCommand("SavePos")]
        private void ChatCommandSavePos(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;

            if (args == null || args.Length == 0)
            {
                PrintToChat(player, "You didn't write the name of the NPC");
                return;
            }

            string name = "";
            for (int i = 0; i < args.Length; i++) name += i == 0 ? args[i] : $" {args[i]}";

            NpcConfig config = Configs.FirstOrDefault(x => x.Name == name);
            if (config == null)
            {
                PrintToChat(player, $"The NPC named <color=#55aaff>{name}</color> <color=#ce3f27>does not exist</color> in the configuration");
                return;
            }

            MonumentInfo Monument = null;
            foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
            {
                string monumentName = GetNameMonument(monument);
                if (string.IsNullOrEmpty(monumentName) || _unnecessaryMonuments.Contains(monumentName)) continue;
                if (Monument == null || Vector3.Distance(player.transform.position, monument.transform.position) < Vector3.Distance(player.transform.position, Monument.transform.position)) Monument = monument;
            }
            if (Monument == null) return;
            string MonumentName = GetNameMonument(Monument);
            MonumentPositionsConfig monumentPositionsConfig = config.Monuments.FirstOrDefault(x => x.Name == MonumentName);
            string pos = Monument.transform.InverseTransformPoint(player.transform.position).ToString();

            if (monumentPositionsConfig == null) config.Monuments.Add(new MonumentPositionsConfig { Name = MonumentName, Positions = new HashSet<string> { pos } });
            else monumentPositionsConfig.Positions.Add(pos);

            Interface.Oxide.DataFileSystem.WriteObject($"BossMonster/Bosses/{config.Name}", config);

            PrintToChat(player, $"You <color=#738d43>have added</color> new coordinates to the <color=#55aaff>List of locations on standard monuments</color>:\nMonument: <color=#55aaff>{MonumentName}</color>\nPosition: <color=#55aaff>{pos}</color>");
        }

        [ChatCommand("SpawnBoss")]
        private void ChatCommandSpawnBoss(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;

            if (args == null || args.Length == 0)
            {
                PrintToChat(player, "You didn't write the name of the NPC");
                return;
            }

            string name = "";
            for (int i = 0; i < args.Length; i++) name += i == 0 ? args[i] : $" {args[i]}";

            SpawnBoss(name, player.transform.position);
        }

        [ConsoleCommand("SpawnBoss")]
        private void ConsoleCommandSpawnBoss(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null) return;

            if (arg.Args == null || arg.Args.Length == 0)
            {
                Puts("You didn't write the name of the NPC");
                return;
            }

            string name = "";
            for (int i = 0; i < arg.Args.Length; i++) name += i == 0 ? arg.Args[i] : $" {arg.Args[i]}";

            NpcConfig config = Configs.FirstOrDefault(x => x.Name == name);
            if (config == null)
            {
                Puts($"There is no configuration named boss - {name}");
                return;
            }

            SpawnBoss(config);
        }

        [ConsoleCommand("KillBoss")]
        private void ConsoleCommandKillBoss(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null) return;

            if (arg.Args == null || arg.Args.Length == 0)
            {
                Puts("You didn't write the name of the NPC");
                return;
            }

            string name = "";
            for (int i = 0; i < arg.Args.Length; i++) name += i == 0 ? arg.Args[i] : $" {arg.Args[i]}";

            while (_controllers.Any(x => x.Value.Npc.displayName == name))
            {
                ScientistNPC boss = _controllers.FirstOrDefault(x => x.Value.Npc.displayName == name).Value.Npc;
                _controllers.Remove(boss.net.ID.Value);
                UnregisterNpcFromGrimmNpc(boss);
                boss.Kill();
            }
        }
        #endregion Commands
    }
}

namespace Oxide.Plugins.BossMonsterExtensionMethods
{
    public static class ExtensionMethods
    {
        public static bool Any<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) if (predicate(enumerator.Current)) return true;
            return false;
        }

        public static bool Any<TKey, TValue>(this Dictionary<TKey, TValue> source, Func<KeyValuePair<TKey, TValue>, bool> predicate)
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

        public static HashSet<TResult> Select<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> predicate)
        {
            HashSet<TResult> result = new HashSet<TResult>();
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) result.Add(predicate(enumerator.Current));
            return result;
        }

        public static List<TSource> ToList<TSource>(this IEnumerable<TSource> source)
        {
            List<TSource> result = new List<TSource>();
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) result.Add(enumerator.Current);
            return result;
        }

        public static TSource First<TSource>(this IList<TSource> source) => source[0];

        public static TSource Last<TSource>(this IList<TSource> source) => source[source.Count - 1];

        public static HashSet<T> OfType<T>(this IEnumerable<BaseNetworkable> source)
        {
            HashSet<T> result = new HashSet<T>();
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) if (enumerator.Current is T) result.Add((T)(object)enumerator.Current);
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

        public static bool IsExists(this BaseNetworkable entity) => entity != null && !entity.IsDestroyed;

        public static bool IsPlayer(this BasePlayer player) => player != null && player.userID.IsSteamId();

        public static void ClearItemsContainer(this ItemContainer container)
        {
            for (int i = container.itemList.Count - 1; i >= 0; i--)
            {
                Item item = container.itemList[i];
                item.RemoveFromContainer();
                item.Remove();
            }
        }
    }
}