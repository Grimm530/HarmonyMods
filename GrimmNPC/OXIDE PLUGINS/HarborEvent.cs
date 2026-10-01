using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using System.Collections.Generic;
using UnityEngine;
using System.Collections;
using System.IO;
using System.Reflection;
using Facepunch;
using UnityEngine.Networking;
using Oxide.Plugins.HarborEventExtensionMethods;

namespace Oxide.Plugins
{
    [Info("HarborEvent", "Grimm530", "2.3.71")]
    internal class HarborEvent : RustPlugin
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
            if (_config.PluginVersion < new VersionNumber(2, 1, 7))
            {
                _config.MainPoint = new PointConfig
                {
                    Enabled = true,
                    Text = "◈",
                    Size = 45,
                    Color = "#CCFF00"
                };
                _config.AdditionalPoint = new PointConfig
                {
                    Enabled = true,
                    Text = "◆",
                    Size = 25,
                    Color = "#FFC700"
                };
            }
            if (_config.PluginVersion < new VersionNumber(2, 1, 9))
            {
                _config.GameTip = new GameTipConfig
                {
                    IsGameTip = false,
                    Style = 2
                };
                _config.Marker = new MarkerConfig
                {
                    Enabled = true,
                    Type = 1,
                    Radius = 0.37967f,
                    Alpha = 0.35f,
                    Color = new ColorConfig { R = 0.81f, G = 0.25f, B = 0.15f },
                    Text = "HarborEvent"
                };
            }
            if (_config.PluginVersion < new VersionNumber(2, 2, 1))
            {
                _config.PveMode.ScaleDamage = new Dictionary<string, float>
                {
                    ["Npc"] = 1f,
                    ["Bradley"] = 2f,
                    ["Helicopter"] = 2f
                };
            }
            if (_config.PluginVersion < new VersionNumber(2, 3, 0))
            {
                _config.Chat = new ChatConfig
                {
                    IsChat = true,
                    Prefix = "[HarborEvent]"
                };
                _config.DistanceAlerts = 0f;
                _config.Helicopter.IsLongDistance = false;
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
            [JsonProperty(En ? "Chance [0.0-100.0]" : "Шанс выпадения предмета [0.0-100.0]")] public float Chance { get; set; }
            [JsonProperty(En ? "Is this a blueprint? [true/false]" : "Это чертеж? [true/false]")] public bool IsBluePrint { get; set; }
            [JsonProperty("SkinID (0 - default)")] public ulong SkinId { get; set; }
            [JsonProperty(En ? "Name (empty - default)" : "Название (empty - default)")] public string Name { get; set; }
        }

        public class LootTableConfig
        {
            [JsonProperty(En ? "Minimum numbers of items" : "Минимальное кол-во элементов")] public int Min { get; set; }
            [JsonProperty(En ? "Maximum numbers of items" : "Максимальное кол-во элементов")] public int Max { get; set; }
            [JsonProperty(En ? "Use minimum and maximum values? [true/false]" : "Использовать минимальное и максимальное значение? [true/false]")] public bool UseCount { get; set; }
            [JsonProperty(En ? "List of items" : "Список предметов")] public List<ItemConfig> Items { get; set; }
        }

        public class PrefabConfig
        {
            [JsonProperty(En ? "Chance [0.0-100.0]" : "Шанс выпадения [0.0-100.0]")] public float Chance { get; set; }
            [JsonProperty(En ? "The path to the prefab" : "Путь к prefab-у")] public string PrefabDefinition { get; set; }
        }

        public class PrefabLootTableConfig
        {
            [JsonProperty(En ? "Minimum numbers of prefabs" : "Минимальное кол-во prefab-ов")] public int Min { get; set; }
            [JsonProperty(En ? "Maximum numbers of prefabs" : "Максимальное кол-во prefab-ов")] public int Max { get; set; }
            [JsonProperty(En ? "Use minimum and maximum values? [true/false]" : "Использовать минимальное и максимальное значение? [true/false]")] public bool UseCount { get; set; }
            [JsonProperty(En ? "List of prefabs" : "Список prefab-ов")] public List<PrefabConfig> Prefabs { get; set; }
        }

        public class CrateConfig
        {
            [JsonProperty("Prefab")] public string Prefab { get; set; }
            [JsonProperty(En ? "Which loot table should the plugin use? (0 - default; 1 - own; 2 - AlphaLoot; 3 - CustomLoot; 4 - loot table of the Rust objects; 5 - combine the 1 and 4 methods)" : "Какую таблицу предметов необходимо использовать? (0 - стандартную; 1 - собственную; 2 - AlphaLoot; 3 - CustomLoot; 4 - таблица предметов объектов Rust; 5 - совместить 1 и 4 методы)")] public int TypeLootTable { get; set; }
            [JsonProperty(En ? "Loot table from prefabs (if the loot table type is 4 or 5)" : "Таблица предметов из prefab-ов (если тип таблицы предметов - 4 или 5)")] public PrefabLootTableConfig PrefabLootTable { get; set; }
            [JsonProperty(En ? "Own loot table (if the loot table type is 1 or 5)" : "Собственная таблица предметов (если тип таблицы предметов - 1 или 5)")] public LootTableConfig OwnLootTable { get; set; }
        }

        public class HackCrateConfig
        {
            [JsonProperty(En ? "Time to unlock the Crates [sec.]" : "Время разблокировки ящиков [sec.]")] public float UnlockTime { get; set; }
            [JsonProperty(En ? "Increase the event time if it's not enough to unlock the locked crate? [true/false]" : "Увеличивать время ивента, если недостаточно чтобы разблокировать заблокированный ящик? [true/false]")] public bool IncreaseEventTime { get; set; }
            [JsonProperty(En ? "Calling a patrol helicopter when the unlock begins?" : "Вызывать патрульный вертолет, когда начинается взлом? [true/false]")] public bool CallHelicopter { get; set; }
            [JsonProperty(En ? "Which loot table should the plugin use? (0 - default; 1 - own; 2 - AlphaLoot; 3 - CustomLoot; 4 - loot table of the Rust objects; 5 - combine the 1 and 4 methods)" : "Какую таблицу лута необходимо использовать? (0 - стандартную; 1 - собственную; 2 - AlphaLoot; 3 - CustomLoot; 4 - таблица предметов объектов Rust; 5 - совместить 1 и 4 методы)")] public int TypeLootTable { get; set; }
            [JsonProperty(En ? "Loot table from prefabs (if the loot table type is 4 or 5)" : "Таблица предметов из prefab-ов (если тип таблицы предметов - 4 или 5)")] public PrefabLootTableConfig PrefabLootTable { get; set; }
            [JsonProperty(En ? "Own loot table (if the loot table type is 1 or 5)" : "Собственная таблица предметов (если тип таблицы предметов - 1 или 5)")] public LootTableConfig OwnLootTable { get; set; }
        }

        public class ColorConfig
        {
            [JsonProperty("r")] public float R { get; set; }
            [JsonProperty("g")] public float G { get; set; }
            [JsonProperty("b")] public float B { get; set; }
        }

        public class MarkerConfig
        {
            [JsonProperty(En ? "Use map marker? [true/false]" : "Использовать маркер на карте? [true/false]")] public bool Enabled { get; set; }
            [JsonProperty(En ? "Type (0 - simple, 1 - advanced)" : "Тип (0 - упрощенный, 1 - расширенный)")] public int Type { get; set; }
            [JsonProperty(En ? "Background radius (if the marker type is 0)" : "Радиус фона (если тип маркера - 0)")] public float Radius { get; set; }
            [JsonProperty(En ? "Background transparency" : "Прозрачность фона")] public float Alpha { get; set; }
            [JsonProperty(En ? "Color" : "Цвет")] public ColorConfig Color { get; set; }
            [JsonProperty(En ? "Text" : "Текст")] public string Text { get; set; }
        }

        public class PointConfig
        {
            [JsonProperty(En ? "Enabled? [true/false]" : "Включен? [true/false]")] public bool Enabled { get; set; }
            [JsonProperty(En ? "Text" : "Текст")] public string Text { get; set; }
            [JsonProperty(En ? "Size" : "Размер")] public int Size { get; set; }
            [JsonProperty(En ? "Color" : "Цвет")] public string Color { get; set; }
        }

        public class GuiConfig
        {
            [JsonProperty(En ? "Do you use the countdown GUI? [true/false]" : "Использовать ли GUI обратного отсчета? [true/false]")] public bool IsGui { get; set; }
            [JsonProperty("OffsetMin Y")] public string OffsetMinY { get; set; }
        }

        public class ChatConfig
        {
            [JsonProperty(En ? "Do you use the chat? [true/false]" : "Использовать ли чат? [true/false]")] public bool IsChat { get; set; }
            [JsonProperty(En ? "Prefix of chat messages" : "Префикс сообщений в чате")] public string Prefix { get; set; }
        }

        public class GameTipConfig
        {
            [JsonProperty(En ? "Use Facepunch Game Tips (notification bar above hotbar)? [true/false]" : "Использовать ли Facepunch Game Tip (оповещения над слотами быстрого доступа игрока)? [true/false]")] public bool IsGameTip { get; set; }
            [JsonProperty(En ? "Style (0 - Blue Normal, 1 - Red Normal, 2 - Blue Long, 3 - Blue Short, 4 - Server Event)" : "Стиль (0 - Blue Normal, 1 - Red Normal, 2 - Blue Long, 3 - Blue Short, 4 - Server Event)")] public int Style { get; set; }
        }

        public class GuiAnnouncementsConfig
        {
            [JsonProperty(En ? "Do you use the GUI Announcements? [true/false]" : "Использовать ли GUI Announcements? [true/false]")] public bool IsGuiAnnouncements { get; set; }
            [JsonProperty(En ? "Banner color" : "Цвет баннера")] public string BannerColor { get; set; }
            [JsonProperty(En ? "Text color" : "Цвет текста")] public string TextColor { get; set; }
            [JsonProperty(En ? "Adjust Vertical Position" : "Отступ от верхнего края")] public float ApiAdjustVPosition { get; set; }
        }

        public class NotifyConfig
        {
            [JsonProperty(En ? "Do you use the Notify? [true/false]" : "Использовать ли Notify? [true/false]")] public bool IsNotify { get; set; }
            [JsonProperty(En ? "Type" : "Тип")] public int Type { get; set; }
        }

        public class DiscordConfig
        {
            [JsonProperty(En ? "Do you use the Discord? [true/false]" : "Использовать ли Discord? [true/false]")] public bool IsDiscord { get; set; }
            [JsonProperty("Webhook URL")] public string WebhookUrl { get; set; }
            [JsonProperty(En ? "Embed Color (DECIMAL)" : "Цвет полосы (DECIMAL)")] public int EmbedColor { get; set; }
            [JsonProperty(En ? "Keys of required messages" : "Ключи необходимых сообщений")] public HashSet<string> Keys { get; set; }
        }

        public class EconomyConfig
        {
            [JsonProperty(En ? "Which economy plugins do you want to use? (Economics, Server Rewards, IQEconomic)" : "Какие плагины экономики вы хотите использовать? (Economics, Server Rewards, IQEconomic)")] public HashSet<string> Plugins { get; set; }
            [JsonProperty(En ? "The minimum value that a player must collect to get points for the economy" : "Минимальное значение, которое игрок должен заработать, чтобы получить баллы за экономику")] public double Min { get; set; }
            [JsonProperty(En ? "Looting of crates" : "Ограбление ящиков")] public Dictionary<string, double> Crates { get; set; }
            [JsonProperty(En ? "Destruction of Bradley" : "Уничтожение Bradley")] public double Bradley { get; set; }
            [JsonProperty(En ? "Killing an NPC" : "Убийство NPC")] public double Npc { get; set; }
            [JsonProperty(En ? "Hacking a locked crate" : "Взлом заблокированного ящика")] public double LockedCrate { get; set; }
            [JsonProperty(En ? "Pressing the button" : "Нажатие кнопки")] public double Button { get; set; }
            [JsonProperty(En ? "List of commands that are executed in the console at the end of the event ({steamid} - the player who collected the highest number of points)" : "Список команд, которые выполняются в консоли по окончанию ивента ({steamid} - игрок, который набрал наибольшее кол-во баллов)")] public HashSet<string> Commands { get; set; }
        }

        public class PveModeConfig
        {
            [JsonProperty(En ? "Use the PVE mode of the plugin? [true/false]" : "Использовать PVE режим работы плагина? [true/false]")] public bool Pve { get; set; }
            [JsonProperty(En ? "The amount of damage that the player has to do to become the Event Owner" : "Кол-во урона, которое должен нанести игрок, чтобы стать владельцем ивента")] public float Damage { get; set; }
            [JsonProperty(En ? "Damage Multipliers for calculate to become the Event Owner" : "Коэффициенты урона для подсчета, чтобы стать владельцем ивента")] public Dictionary<string, float> ScaleDamage { get; set; }
            [JsonProperty(En ? "Can the non-owner of the event loot the crates? [true/false]" : "Может ли не владелец ивента грабить ящики? [true/false]")] public bool LootCrate { get; set; }
            [JsonProperty(En ? "Can the non-owner of the event hack locked crates? [true/false]" : "Может ли не владелец ивента взламывать заблокированные ящики? [true/false]")] public bool HackCrate { get; set; }
            [JsonProperty(En ? "Can the non-owner of the event loot NPC corpses? [true/false]" : "Может ли не владелец ивента грабить трупы NPC? [true/false]")] public bool LootNpc { get; set; }
            [JsonProperty(En ? "Can the non-owner of the event deal damage to the NPC? [true/false]" : "Может ли не владелец ивента наносить урон по NPC? [true/false]")] public bool DamageNpc { get; set; }
            [JsonProperty(En ? "Can the non-owner of the event do damage to Bradley? [true/false]" : "Может ли не владелец ивента наносить урон по Bradley? [true/false]")] public bool DamageTank { get; set; }
            [JsonProperty(En ? "Can the non-owner of the event do damage to Patrol Helicopter? [true/false]" : "Может ли не владелец ивента наносить урон по патрульному вертолету? [true/false]")] public bool DamageHelicopter { get; set; }
            [JsonProperty(En ? "Can an Npc attack a non-owner of the event? [true/false]" : "Может ли Npc атаковать не владельца ивента? [true/false]")] public bool TargetNpc { get; set; }
            [JsonProperty(En ? "Can Bradley attack a non-owner of the event? [true/false]" : "Может ли Bradley атаковать не владельца ивента? [true/false]")] public bool TargetTank { get; set; }
            [JsonProperty(En ? "Can Patrol Helicopter attack a non-owner of the event? [true/false]" : "Может ли патрульный вертолет атаковать не владельца ивента? [true/false]")] public bool TargetHelicopter { get; set; }
            [JsonProperty(En ? "Allow the non-owner of the event to enter the event zone? [true/false]" : "Разрешать входить внутрь зоны ивента не владельцу ивента? [true/false]")] public bool CanEnter { get; set; }
            [JsonProperty(En ? "Allow a player who has an active cooldown of the Event Owner to enter the event zone? [true/false]" : "Разрешать входить внутрь зоны ивента игроку, у которого активен кулдаун на получение статуса владельца ивента? [true/false]")] public bool CanEnterCooldownPlayer { get; set; }
            [JsonProperty(En ? "The time that the Event Owner may not be inside the event zone [sec.]" : "Время, которое владелец ивента может не находиться внутри зоны ивента [сек.]")] public int TimeExitOwner { get; set; }
            [JsonProperty(En ? "The time until the end of Event Owner status when it is necessary to warn the player [sec.]" : "Время таймера до окончания действия статуса владельца ивента, когда необходимо предупредить игрока [сек.]")] public int AlertTime { get; set; }
            [JsonProperty(En ? "Prevent the actions of the RestoreUponDeath plugin in the event zone? [true/false]" : "Запрещать работу плагина RestoreUponDeath в зоне действия ивента? [true/false]")] public bool RestoreUponDeath { get; set; }
            [JsonProperty(En ? "The time that the player can`t become the Event Owner, after the end of the event and the player was its owner [sec.]" : "Время, которое игрок не сможет стать владельцем ивента, после того как ивент окончен и игрок был его владельцем [sec.]")] public double CooldownOwner { get; set; }
            [JsonProperty(En ? "Darkening the dome (0 - disables the dome)" : "Затемнение купола (0 - отключает купол)")] public int Darkening { get; set; }
        }

        public class NpcBelt
        {
            [JsonProperty("ShortName")] public string ShortName { get; set; }
            [JsonProperty(En ? "Amount" : "Кол-во")] public int Amount { get; set; }
            [JsonProperty("SkinID (0 - default)")] public ulong SkinId { get; set; }
            [JsonProperty(En ? "Mods" : "Модификации на оружие")] public HashSet<string> Mods { get; set; }
            [JsonProperty(En ? "Ammo" : "Боеприпасы")] public string Ammo { get; set; }
        }

        public class NpcWear
        {
            [JsonProperty("ShortName")] public string ShortName { get; set; }
            [JsonProperty("SkinID (0 - default)")] public ulong SkinId { get; set; }
        }

        public class NpcConfig
        {
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
            [JsonProperty(En ? "Disable radio effects? [true/false]" : "Отключать эффекты рации? [true/false]")] public bool DisableRadio { get; set; }
            [JsonProperty(En ? "Is this a stationary NPC? [true/false]" : "Это стационарный NPC? [true/false]")] public bool Stationary { get; set; }
            [JsonProperty(En ? "Remove a corpse after death? (it is recommended to use the true value to improve performance) [true/false]" : "Удалять труп после смерти? (рекомендуется использовать значение true для повышения производительности) [true/false]")] public bool IsRemoveCorpse { get; set; }
            [JsonProperty(En ? "Wear items" : "Одежда")] public HashSet<NpcWear> WearItems { get; set; }
            [JsonProperty(En ? "Belt items" : "Быстрые слоты")] public HashSet<NpcBelt> BeltItems { get; set; }
            [JsonProperty(En ? "Kit (it is recommended to use the previous 2 settings to improve performance)" : "Kit (рекомендуется использовать предыдущие 2 пункта настройки для повышения производительности)")] public string Kit { get; set; }
        }

        public class PresetConfig
        {
            [JsonProperty(En ? "Minimum" : "Минимальное кол-во")] public int Min { get; set; }
            [JsonProperty(En ? "Maximum" : "Максимальное кол-во")] public int Max { get; set; }
            [JsonProperty(En ? "List of locations" : "Список расположений")] public HashSet<string> Positions { get; set; }
            [JsonProperty(En ? "NPCs setting" : "Настройки NPC")] public NpcConfig Config { get; set; }
            [JsonProperty(En ? "Which loot table should the plugin use? (0 - default; 1 - own; 2 - AlphaLoot; 3 - CustomLoot; 4 - loot table of the Rust objects; 5 - combine the 1 and 4 methods)" : "Какую таблицу предметов необходимо использовать? (0 - стандартную; 1 - собственную; 2 - AlphaLoot; 3 - CustomLoot; 4 - таблица предметов объектов Rust; 5 - совместить 1 и 4 методы)")] public int TypeLootTable { get; set; }
            [JsonProperty(En ? "Loot table from prefabs (if the loot table type is 4 or 5)" : "Таблица предметов из prefab-ов (если тип таблицы предметов - 4 или 5)")] public PrefabLootTableConfig PrefabLootTable { get; set; }
            [JsonProperty(En ? "Own loot table (if the loot table type is 1 or 5)" : "Собственная таблица предметов (если тип таблицы предметов - 1 или 5)")] public LootTableConfig OwnLootTable { get; set; }
        }

        public class NpcConfigCargo
        {
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
            [JsonProperty(En ? "Disable radio effects? [true/false]" : "Отключать эффекты рации? [true/false]")] public bool DisableRadio { get; set; }
            [JsonProperty(En ? "Remove a corpse after death? (it is recommended to use the true value to improve performance) [true/false]" : "Удалять труп после смерти? (рекомендуется использовать значение true для повышения производительности) [true/false]")] public bool IsRemoveCorpse { get; set; }
            [JsonProperty(En ? "Wear items" : "Одежда")] public HashSet<NpcWear> WearItems { get; set; }
            [JsonProperty(En ? "Belt items" : "Быстрые слоты")] public HashSet<NpcBelt> BeltItems { get; set; }
            [JsonProperty(En ? "Kit (it is recommended to use the previous 2 settings to improve performance)" : "Kit (рекомендуется использовать предыдущие 2 пункта настройки для повышения производительности)")] public string Kit { get; set; }
            [JsonProperty(En ? "Which loot table should the plugin use? (0 - default; 1 - own; 2 - AlphaLoot; 3 - CustomLoot; 4 - loot table of the Rust objects; 5 - combine the 1 and 4 methods)" : "Какую таблицу предметов необходимо использовать? (0 - стандартную; 1 - собственную; 2 - AlphaLoot; 3 - CustomLoot; 4 - таблица предметов объектов Rust; 5 - совместить 1 и 4 методы)")] public int TypeLootTable { get; set; }
            [JsonProperty(En ? "Loot table from prefabs (if the loot table type is 4 or 5)" : "Таблица предметов из prefab-ов (если тип таблицы предметов - 4 или 5)")] public PrefabLootTableConfig PrefabLootTable { get; set; }
            [JsonProperty(En ? "Own loot table (if the loot table type is 1 or 5)" : "Собственная таблица предметов (если тип таблицы предметов - 1 или 5)")] public LootTableConfig OwnLootTable { get; set; }
        }

        public class BradleyConfig
        {
            [JsonProperty(En ? "Can Bradley appear? [true/false]" : "Должен ли появляться Bradley? [true/false]")] public bool IsBradley { get; set; }
            [JsonProperty(En ? "Health" : "Кол-во ХП")] public float Hp { get; set; }
            [JsonProperty(En ? "The viewing distance" : "Дальность обзора")] public float ViewDistance { get; set; }
            [JsonProperty(En ? "Radius of search" : "Радиус поиска")] public float SearchRange { get; set; }
            [JsonProperty(En ? "Scale damage" : "Множитель урона")] public float ScaleDamage { get; set; }
            [JsonProperty(En ? "The multiplier of Machine-gun aim cone" : "Множитель разброса пулемёта")] public float CoaxAimCone { get; set; }
            [JsonProperty(En ? "The multiplier of Machine-gun fire rate" : "Множитель скорострельности пулемёта")] public float CoaxFireRate { get; set; }
            [JsonProperty(En ? "Amount of Machine-gun burst shots" : "Кол-во выстрелов очереди пулемёта")] public int CoaxBurstLength { get; set; }
            [JsonProperty(En ? "Time that Bradley holds in memory the position of its last target [sec.]" : "Время, которое Bradley помнит позицию своей последней цели [sec.]")] public float MemoryDuration { get; set; }
            [JsonProperty(En ? "The time between shots of the main gun [sec.]" : "Время между залпами основного орудия [sec.]")] public float NextFireTime { get; set; }
            [JsonProperty(En ? "The time between shots of the main gun in a fire rate [sec.]" : "Время между выстрелами основного орудия в залпе [sec.]")] public float TopTurretFireRate { get; set; }
            [JsonProperty(En ? "Numbers of Crates" : "Кол-во ящиков после уничтожения")] public int CountCrates { get; set; }
            [JsonProperty(En ? "Which loot table should the plugin use? (0 - default; 1 - own; 2 - AlphaLoot; 3 - CustomLoot; 4 - loot table of the Rust objects; 5 - combine the 1 and 4 methods)" : "Какую таблицу лута необходимо использовать? (0 - стандартную; 1 - собственную; 2 - AlphaLoot; 3 - CustomLoot; 4 - таблица предметов объектов Rust; 5 - совместить 1 и 4 методы)")] public int TypeLootTable { get; set; }
            [JsonProperty(En ? "Loot table from prefabs (if the loot table type is 4 or 5)" : "Таблица предметов из prefab-ов (если тип таблицы предметов - 4 или 5)")] public PrefabLootTableConfig PrefabLootTable { get; set; }
            [JsonProperty(En ? "Own loot table (if the loot table type is 1 or 5)" : "Собственная таблица предметов (если тип таблицы предметов - 1 или 5)")] public LootTableConfig OwnLootTable { get; set; }
        }

        public class HelicopterConfig
        {
            [JsonProperty(En ? "Health" : "Кол-во ХП")] public float Hp { get; set; }
            [JsonProperty(En ? "Health Main Rotor" : "Кол-во ХП основного винта")] public float HpMainRotor { get; set; }
            [JsonProperty(En ? "Health Tail Rotor" : "Кол-во ХП хвостового винта")] public float HpTailRotor { get; set; }
            [JsonProperty(En ? "Numbers of Crates" : "Кол-во ящиков после уничтожения")] public int CountCrates { get; set; }
            [JsonProperty(En ? "Time between firing rockets" : "Время между выстрелом ракет")] public float TimeBetweenRockets { get; set; }
            [JsonProperty(En ? "Time between turret shots" : "Время между выстрелами пулемета")] public float FireRate { get; set; }
            [JsonProperty(En ? "Time between turret bursts" : "Время между очередями пулемета")] public float TimeBetweenBursts { get; set; }
            [JsonProperty(En ? "Duration of the burst turret" : "Продолжительность очереди пулемета")] public float BurstLength { get; set; }
            [JsonProperty(En ? "Turret firing radius" : "Дистанция стрельбы пулемета")] public float MaxTargetRange { get; set; }
            [JsonProperty(En ? "Should a helicopter spawn in the corner of the map? [true/false]" : "Должен ли появляться вертолет в углу карты? [true/false]")] public bool IsLongDistance { get; set; }
        }

        private class PluginConfig
        {
            [JsonProperty(En ? "Minimum time between events [sec.]" : "Минимальное время между ивентами [sec.]")] public float MinStartTime { get; set; }
            [JsonProperty(En ? "Maximum time between events [sec.]" : "Максимальное время между ивентами [sec.]")] public float MaxStartTime { get; set; }
            [JsonProperty(En ? "Is active the timer on to start the event? [true/false]" : "Активен ли таймер для запуска ивента? [true/false]")] public bool EnabledTimer { get; set; }
            [JsonProperty(En ? "Duration of the event [sec.]" : "Время проведения ивента [sec.]")] public int FinishTime { get; set; }
            [JsonProperty(En ? "Time before the starting of the event after receiving a chat message [sec.]" : "Время до начала ивента после сообщения в чате [sec.]")] public float PreStartTime { get; set; }
            [JsonProperty(En ? "Notification time until the end of the event [sec.]" : "Время оповещения до окончания ивента [sec.]")] public int PreFinishTime { get; set; }
            [JsonProperty(En ? "Crates settings in Cargo Container" : "Настройка ящиков в контейнере")] public HashSet<CrateConfig> ContainerCrates { get; set; }
            [JsonProperty(En ? "Crates settings on Cargo Ship" : "Настройка ящиков на корабле")] public HashSet<CrateConfig> CargoCrates { get; set; }
            [JsonProperty(En ? "Locked crates settings in Cargo Container" : "Настройка заблокированных ящиков в контейнере")] public HackCrateConfig ContainerHackCrates { get; set; }
            [JsonProperty(En ? "Locked crates settings on Cargo Ship" : "Настройка заблокированных ящиков на корабле")] public HackCrateConfig CargoHackCrates { get; set; }
            [JsonProperty(En ? "NPCs settings in Small Harbor" : "Настройка NPC в Малом Порту")] public HashSet<PresetConfig> NpcSmall { get; set; }
            [JsonProperty(En ? "NPCs settings in Large Harbor" : "Настройка NPC в Большом Порту")] public HashSet<PresetConfig> NpcLarge { get; set; }
            [JsonProperty(En ? "Mobile NPCs settings on Cargo Ship" : "Настройка двигающихся NPC на корабле")] public NpcConfigCargo NpcMovingCargo { get; set; }
            [JsonProperty(En ? "Stationary NPCs settings inside Cargo Ship" : "Настройка стационарных NPC внутри корабля")] public NpcConfigCargo NpcStationaryInsideCargo { get; set; }
            [JsonProperty(En ? "Stationary NPCs settings outside Cargo Ship" : "Настройка стационарных NPC снаружи корабля")] public NpcConfigCargo NpcStationaryOutsideCargo { get; set; }
            [JsonProperty(En ? "Marker configuration on the map" : "Настройка маркера на карте")] public MarkerConfig Marker { get; set; }
            [JsonProperty(En ? "Main marker settings for key event points shown on players screen" : "Настройки основного маркера на экране игрока")] public PointConfig MainPoint { get; set; }
            [JsonProperty(En ? "Additional marker settings for key event points shown on players screen" : "Настройки дополнительного маркера на экране игрока")] public PointConfig AdditionalPoint { get; set; }
            [JsonProperty(En ? "GUI setting" : "Настройки GUI")] public GuiConfig Gui { get; set; }
            [JsonProperty(En ? "Chat setting" : "Настройки чата")] public ChatConfig Chat { get; set; }
            [JsonProperty(En ? "Facepunch Game Tips setting" : "Настройка сообщений Facepunch Game Tip")] public GameTipConfig GameTip { get; set; }
            [JsonProperty(En ? "GUI Announcements setting" : "Настройка GUI Announcements")] public GuiAnnouncementsConfig GuiAnnouncements { get; set; }
            [JsonProperty(En ? "Notify setting" : "Настройка Notify")] public NotifyConfig Notify { get; set; }
            [JsonProperty(En ? "The distance from the event to the player for global alerts (0 - no limit)" : "Расстояние от ивента до игрока для глобальных оповещений (0 - нет ограничений)")] public float DistanceAlerts { get; set; }
            [JsonProperty(En ? "Discord setting (only for users DiscordMessages plugin)" : "Настройка оповещений в Discord (только для тех, кто использует плагин DiscordMessages)")] public DiscordConfig Discord { get; set; }
            [JsonProperty(En ? "Radius of the event zone" : "Радиус зоны ивента")] public float Radius { get; set; }
            [JsonProperty(En ? "Do you create a PVP zone in the event area? (only for users TruePVE plugin) [true/false]" : "Создавать зону PVP в зоне проведения ивента? (только для тех, кто использует плагин TruePVE) [true/false]")] public bool IsCreateZonePvp { get; set; }
            [JsonProperty(En ? "PVE Mode Setting (only for users PveMode plugin)" : "Настройка PVE режима работы плагина (только для тех, кто использует плагин PveMode)")] public PveModeConfig PveMode { get; set; }
            [JsonProperty(En ? "Interrupt the teleport in harbor? (only for users NTeleportation plugin) [true/false]" : "Запрещать телепорт в порту? (только для тех, кто использует плагин NTeleportation) [true/false]")] public bool NTeleportationInterrupt { get; set; }
            [JsonProperty(En ? "Disable NPCs from the BetterNpc plugin on the monument while the event is on? [true/false]" : "Отключать NPC из плагина BetterNpc на монументе пока проходит ивент? [true/false]")] public bool RemoveBetterNpc { get; set; }
            [JsonProperty(En ? "Economy setting (total values will be added up and rewarded at the end of the event)" : "Настройка экономики (конечное значение суммируется и будет выдано игрокам по окончанию ивента)")] public EconomyConfig Economy { get; set; }
            [JsonProperty(En ? "List of commands banned in the event zone" : "Список команд запрещенных в зоне ивента")] public HashSet<string> Commands { get; set; }
            [JsonProperty(En ? "Bradley setting" : "Настройка танка")] public BradleyConfig Bradley { get; set; }
            [JsonProperty(En ? "Helicopter setting" : "Настройка вертолета")] public HelicopterConfig Helicopter { get; set; }
            [JsonProperty(En ? "The CCTV camera" : "Название камеры")] public string Cctv { get; set; }
            [JsonProperty(En ? "Can an event appear in a Small Harbor? [true/false]" : "Должен ли ивент появляться в Малом Порту? [true/false]")] public bool IsSmallHarbor { get; set; }
            [JsonProperty(En ? "Can an event appear in a Large Harbor? [true/false]" : "Должен ли ивент появляться в Большом Порту? [true/false]")] public bool IsLargeHarbor { get; set; }
            [JsonProperty(En ? "Configuration version" : "Версия конфигурации")] public VersionNumber PluginVersion { get; set; }

            public static PluginConfig DefaultConfig()
            {
                return new PluginConfig
                {
                    MinStartTime = 10800f,
                    MaxStartTime = 10800f,
                    EnabledTimer = true,
                    FinishTime = 3600,
                    PreStartTime = 300f,
                    PreFinishTime = 300,
                    ContainerCrates = new HashSet<CrateConfig>
                    {
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_elite.prefab",
                            TypeLootTable = 0,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_elite.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 25, MaxAmount = 25, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_normal.prefab",
                            TypeLootTable = 0,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_normal.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 8, MaxAmount = 8, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/dmloot/dm c4.prefab",
                            TypeLootTable = 0,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/dmloot/dm c4.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 5, MaxAmount = 10, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/dmloot/dm ammo.prefab",
                            TypeLootTable = 0,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/dmloot/dm ammo.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 5, MaxAmount = 10, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        }
                    },
                    CargoCrates = new HashSet<CrateConfig>
                    {
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_elite.prefab",
                            TypeLootTable = 0,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_elite.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 25, MaxAmount = 25, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_normal.prefab",
                            TypeLootTable = 0,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_normal.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 8, MaxAmount = 8, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_normal_2.prefab",
                            TypeLootTable = 0,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_normal_2.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 5, MaxAmount = 5, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        }
                    },
                    ContainerHackCrates = new HackCrateConfig
                    {
                        UnlockTime = 600f,
                        IncreaseEventTime = true,
                        CallHelicopter = true,
                        TypeLootTable = 0,
                        PrefabLootTable = new PrefabLootTableConfig
                        {
                            Min = 1,
                            Max = 1,
                            UseCount = true,
                            Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab" } }
                        },
                        OwnLootTable = new LootTableConfig
                        {
                            Min = 1,
                            Max = 1,
                            UseCount = true,
                            Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 5, MaxAmount = 10, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                        }
                    },
                    CargoHackCrates = new HackCrateConfig
                    {
                        UnlockTime = 600f,
                        IncreaseEventTime = true,
                        TypeLootTable = 0,
                        CallHelicopter = false,
                        PrefabLootTable = new PrefabLootTableConfig
                        {
                            Min = 1,
                            Max = 1,
                            UseCount = true,
                            Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab" } }
                        },
                        OwnLootTable = new LootTableConfig
                        {
                            Min = 1,
                            Max = 1,
                            UseCount = true,
                            Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 5, MaxAmount = 10, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                        }
                    },
                    NpcSmall = new HashSet<PresetConfig>
                    {
                        new PresetConfig
                        {
                            Min = 4,
                            Max = 4,
                            Positions = new HashSet<string>
                            {
                                "(-19.329, 28.419, 87.115)",
                                "(-43.801, 28.372, 3.077)",
                                "(63.992, 28.372, -12.193)",
                                "(100.617, 28.419, 115.251)",
                            },
                            Config = new NpcConfig
                            {
                                Name = "Soldier",
                                Health = 200f,
                                RoamRange = 0f,
                                ChaseRange = 0f,
                                AttackRangeMultiplier = 5f,
                                SenseRange = 100f,
                                MemoryDuration = 10f,
                                DamageScale = 2f,
                                AimConeScale = 0f,
                                CheckVisionCone = false,
                                VisionCone = 135f,
                                Speed = 0f,
                                DisableRadio = false,
                                Stationary = true,
                                IsRemoveCorpse = true,
                                WearItems = new HashSet<NpcWear>
                                {
                                    new NpcWear { ShortName = "hoodie", SkinId = 2563940111 },
                                    new NpcWear { ShortName = "pants", SkinId = 2563935722 },
                                    new NpcWear { ShortName = "shoes.boots", SkinId = 2575506021 },
                                    new NpcWear { ShortName = "roadsign.jacket", SkinId = 2570233552 },
                                    new NpcWear { ShortName = "burlap.gloves", SkinId = 2582714399 },
                                    new NpcWear { ShortName = "coffeecan.helmet", SkinId = 2570227850 },
                                    new NpcWear { ShortName = "roadsign.kilt", SkinId = 2570237224 }
                                },
                                BeltItems = new HashSet<NpcBelt>
                                {
                                    new NpcBelt { ShortName = "rifle.m39", Amount = 1, SkinId = 0, Mods = new HashSet<string> { "weapon.mod.flashlight", "weapon.mod.small.scope" }, Ammo = string.Empty },
                                    new NpcBelt { ShortName = "syringe.medical", Amount = 5, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty },
                                    new NpcBelt { ShortName = "grenade.f1", Amount = 2, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty }
                                },
                                Kit = ""
                            },
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = false,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_oilrig.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = false,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "scrap", MinAmount = 8, MaxAmount = 33, Chance = 36f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            }
                        },
                        new PresetConfig
                        {
                            Min = 1,
                            Max = 1,
                            Positions = new HashSet<string>
                            {
                                "(53.501, 28.372, 36.665)"
                            },
                            Config = new NpcConfig
                            {
                                Name = "Sniper",
                                Health = 200f,
                                RoamRange = 0f,
                                ChaseRange = 0f,
                                AttackRangeMultiplier = 1f,
                                SenseRange = 150f,
                                MemoryDuration = 30f,
                                DamageScale = 0.25f,
                                AimConeScale = 1f,
                                CheckVisionCone = false,
                                VisionCone = 135f,
                                Speed = 0f,
                                DisableRadio = false,
                                Stationary = true,
                                IsRemoveCorpse = true,
                                WearItems = new HashSet<NpcWear>
                                {
                                    new NpcWear { ShortName = "hat.boonie", SkinId = 1275532550 },
                                    new NpcWear { ShortName = "mask.bandana", SkinId = 1623665052 },
                                    new NpcWear { ShortName = "burlap.gloves", SkinId = 1113475533 },
                                    new NpcWear { ShortName = "hoodie", SkinId = 1275521888 },
                                    new NpcWear { ShortName = "pants", SkinId = 1277403128 },
                                    new NpcWear { ShortName = "shoes.boots", SkinId = 0 }
                                },
                                BeltItems = new HashSet<NpcBelt>
                                {
                                    new NpcBelt { ShortName = "rifle.bolt", Amount = 1, SkinId = 897867582, Mods = new HashSet<string> { "weapon.mod.flashlight", "weapon.mod.8x.scope" }, Ammo = string.Empty },
                                    new NpcBelt { ShortName = "syringe.medical", Amount = 5, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty }
                                },
                                Kit = ""
                            },
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = false,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_oilrig.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = false,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "scrap", MinAmount = 8, MaxAmount = 33, Chance = 36f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            }
                        },
                        new PresetConfig
                        {
                            Min = 8,
                            Max = 8,
                            Positions = new HashSet<string>
                            {
                                "(29.882, 4.325, 32.478)",
                                "(49.629, 4.325, 27.236)",
                                "(-30.202, 4.25, 102.732)",
                                "(85.564, 4.138, 110.003)",
                                "(-52.878, 4.25, 19.443)",
                                "(72.046, 4.25, 20.064)",
                                "(20.473, 4.25, 107.782)",
                                "(-3.444, 1.25, 87.991)"
                            },
                            Config = new NpcConfig
                            {
                                Name = "Porter",
                                Health = 175f,
                                RoamRange = 10f,
                                ChaseRange = 30f,
                                AttackRangeMultiplier = 2.5f,
                                SenseRange = 70f,
                                MemoryDuration = 120f,
                                DamageScale = 1.7f,
                                AimConeScale = 0.6f,
                                CheckVisionCone = false,
                                VisionCone = 135f,
                                Speed = 7.5f,
                                DisableRadio = true,
                                Stationary = false,
                                IsRemoveCorpse = true,
                                WearItems = new HashSet<NpcWear>
                                {
                                    new NpcWear { ShortName = "hoodie", SkinId = 1819497052 },
                                    new NpcWear { ShortName = "shoes.boots", SkinId = 0 },
                                    new NpcWear { ShortName = "hat.beenie", SkinId = 0 },
                                    new NpcWear { ShortName = "movembermoustache", SkinId = 0 },
                                    new NpcWear { ShortName = "burlap.gloves", SkinId = 0 },
                                    new NpcWear { ShortName = "pants", SkinId = 1819498178 }
                                },
                                BeltItems = new HashSet<NpcBelt>
                                {
                                    new NpcBelt { ShortName = "smg.mp5", Amount = 1, SkinId = 0, Mods = new HashSet<string> { "weapon.mod.flashlight", "weapon.mod.holosight" }, Ammo = string.Empty },
                                    new NpcBelt { ShortName = "syringe.medical", Amount = 5, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty },
                                    new NpcBelt { ShortName = "grenade.f1", Amount = 2, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty }
                                },
                                Kit = ""
                            },
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = false,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_oilrig.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = false,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "scrap", MinAmount = 8, MaxAmount = 33, Chance = 36f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            }
                        }
                    },
                    NpcLarge = new HashSet<PresetConfig>
                    {
                        new PresetConfig
                        {
                            Min = 4,
                            Max = 4,
                            Positions = new HashSet<string>
                            {
                                "(118.098, 28.122, -99.779)",
                                "(122.412, 28.122, 42.108)",
                                "(47.479, 28.122, 31.648)",
                                "(33.447, 28.122, -104.953)"
                            },
                            Config = new NpcConfig
                            {
                                Name = "Soldier",
                                Health = 200f,
                                RoamRange = 0f,
                                ChaseRange = 0f,
                                AttackRangeMultiplier = 5f,
                                SenseRange = 100f,
                                MemoryDuration = 10f,
                                DamageScale = 2f,
                                AimConeScale = 0f,
                                CheckVisionCone = false,
                                VisionCone = 135f,
                                Speed = 0f,
                                DisableRadio = false,
                                Stationary = true,
                                IsRemoveCorpse = true,
                                WearItems = new HashSet<NpcWear>
                                {
                                    new NpcWear { ShortName = "hoodie", SkinId = 2563940111 },
                                    new NpcWear { ShortName = "pants", SkinId = 2563935722 },
                                    new NpcWear { ShortName = "shoes.boots", SkinId = 2575506021 },
                                    new NpcWear { ShortName = "roadsign.jacket", SkinId = 2570233552 },
                                    new NpcWear { ShortName = "burlap.gloves", SkinId = 2582714399 },
                                    new NpcWear { ShortName = "coffeecan.helmet", SkinId = 2570227850 },
                                    new NpcWear { ShortName = "roadsign.kilt", SkinId = 2570237224 }
                                },
                                BeltItems = new HashSet<NpcBelt>
                                {
                                    new NpcBelt { ShortName = "rifle.m39", Amount = 1, SkinId = 0, Mods = new HashSet<string> { "weapon.mod.flashlight", "weapon.mod.small.scope" }, Ammo = string.Empty },
                                    new NpcBelt { ShortName = "syringe.medical", Amount = 5, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty },
                                    new NpcBelt { ShortName = "grenade.f1", Amount = 2, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty }
                                },
                                Kit = ""
                            },
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = false,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_oilrig.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = false,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "scrap", MinAmount = 8, MaxAmount = 33, Chance = 36f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            }
                        },
                        new PresetConfig
                        {
                            Min = 1,
                            Max = 1,
                            Positions = new HashSet<string>
                            {
                                "(46.434, 28.122, -30.754)"
                            },
                            Config = new NpcConfig
                            {
                                Name = "Sniper",
                                Health = 200f,
                                RoamRange = 0f,
                                ChaseRange = 0f,
                                AttackRangeMultiplier = 1f,
                                SenseRange = 150f,
                                MemoryDuration = 30f,
                                DamageScale = 0.25f,
                                AimConeScale = 1f,
                                CheckVisionCone = false,
                                VisionCone = 135f,
                                Speed = 0f,
                                DisableRadio = false,
                                Stationary = true,
                                IsRemoveCorpse = true,
                                WearItems = new HashSet<NpcWear>
                                {
                                    new NpcWear { ShortName = "hat.boonie", SkinId = 1275532550 },
                                    new NpcWear { ShortName = "mask.bandana", SkinId = 1623665052 },
                                    new NpcWear { ShortName = "burlap.gloves", SkinId = 1113475533 },
                                    new NpcWear { ShortName = "hoodie", SkinId = 1275521888 },
                                    new NpcWear { ShortName = "pants", SkinId = 1277403128 },
                                    new NpcWear { ShortName = "shoes.boots", SkinId = 0 }
                                },
                                BeltItems = new HashSet<NpcBelt>
                                {
                                    new NpcBelt { ShortName = "rifle.bolt", Amount = 1, SkinId = 897867582, Mods = new HashSet<string> { "weapon.mod.flashlight", "weapon.mod.8x.scope" }, Ammo = string.Empty },
                                    new NpcBelt { ShortName = "syringe.medical", Amount = 5, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty }
                                },
                                Kit = ""
                            },
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = false,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_oilrig.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = false,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "scrap", MinAmount = 8, MaxAmount = 33, Chance = 36f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            }
                        },
                        new PresetConfig
                        {
                            Min = 8,
                            Max = 8,
                            Positions = new HashSet<string>
                            {
                                "(105.21, 3.889, 25.266)",
                                "(101.475, 3.933, -71.055)",
                                "(91.687, 4, -40.688)",
                                "(107.994, 3.872, -2.054)",
                                "(105.175, 3.888, -23.856)",
                                "(13.714, 4, -5.558)",
                                "(37.923, 4, 68.334)",
                                "(32.177, 4, -73.32)"
                            },
                            Config = new NpcConfig
                            {
                                Name = "Porter",
                                Health = 175f,
                                RoamRange = 10f,
                                ChaseRange = 30f,
                                AttackRangeMultiplier = 2.5f,
                                SenseRange = 70f,
                                MemoryDuration = 120f,
                                DamageScale = 1.7f,
                                AimConeScale = 0.6f,
                                CheckVisionCone = false,
                                VisionCone = 135f,
                                Speed = 7.5f,
                                DisableRadio = true,
                                Stationary = false,
                                IsRemoveCorpse = true,
                                WearItems = new HashSet<NpcWear>
                                {
                                    new NpcWear { ShortName = "hoodie", SkinId = 1819497052 },
                                    new NpcWear { ShortName = "shoes.boots", SkinId = 0 },
                                    new NpcWear { ShortName = "hat.beenie", SkinId = 0 },
                                    new NpcWear { ShortName = "movembermoustache", SkinId = 0 },
                                    new NpcWear { ShortName = "burlap.gloves", SkinId = 0 },
                                    new NpcWear { ShortName = "pants", SkinId = 1819498178 }
                                },
                                BeltItems = new HashSet<NpcBelt>
                                {
                                    new NpcBelt { ShortName = "smg.mp5", Amount = 1, SkinId = 0, Mods = new HashSet<string> { "weapon.mod.flashlight", "weapon.mod.holosight" }, Ammo = string.Empty },
                                    new NpcBelt { ShortName = "syringe.medical", Amount = 5, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty },
                                    new NpcBelt { ShortName = "grenade.f1", Amount = 2, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty }
                                },
                                Kit = ""
                            },
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = false,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_oilrig.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = false,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "scrap", MinAmount = 8, MaxAmount = 33, Chance = 36f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            }
                        }
                    },
                    NpcMovingCargo = new NpcConfigCargo
                    {
                        Name = "Scientist",
                        Health = 250f,
                        RoamRange = 30f,
                        ChaseRange = 60f,
                        AttackRangeMultiplier = 1f,
                        SenseRange = 50f,
                        MemoryDuration = 30f,
                        DamageScale = 1.15f,
                        AimConeScale = 1.3f,
                        CheckVisionCone = false,
                        VisionCone = 135f,
                        Speed = 7.5f,
                        DisableRadio = false,
                        IsRemoveCorpse = true,
                        WearItems = new HashSet<NpcWear>
                        {
                            new NpcWear { ShortName = "hat.cap", SkinId = 2891590451 },
                            new NpcWear { ShortName = "hoodie", SkinId = 2882740093 },
                            new NpcWear { ShortName = "pants", SkinId = 2882737241 },
                            new NpcWear { ShortName = "shoes.boots", SkinId = 826587881 },
                            new NpcWear { ShortName = "sunglasses", SkinId = 0 }
                        },
                        BeltItems = new HashSet<NpcBelt>
                        {
                            new NpcBelt { ShortName = "rifle.lr300", Amount = 1, SkinId = 0, Mods = new HashSet<string> { "weapon.mod.flashlight", "weapon.mod.holosight" }, Ammo = string.Empty },
                            new NpcBelt { ShortName = "syringe.medical", Amount = 5, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty },
                            new NpcBelt { ShortName = "grenade.f1", Amount = 2, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty }
                        },
                        Kit = "",
                        TypeLootTable = 5,
                        PrefabLootTable = new PrefabLootTableConfig
                        {
                            Min = 1,
                            Max = 1,
                            UseCount = false,
                            Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_oilrig.prefab" } }
                        },
                        OwnLootTable = new LootTableConfig
                        {
                            Min = 1,
                            Max = 1,
                            UseCount = false,
                            Items = new List<ItemConfig>
                            {
                                new ItemConfig { ShortName = "scrap", MinAmount = 8, MaxAmount = 33, Chance = 36f, IsBluePrint = false, SkinId = 0, Name = "" }
                            }
                        }
                    },
                    NpcStationaryInsideCargo = new NpcConfigCargo
                    {
                        Name = "Scientist",
                        Health = 250f,
                        RoamRange = 10f,
                        ChaseRange = 100f,
                        AttackRangeMultiplier = 1f,
                        SenseRange = 50f,
                        MemoryDuration = 30f,
                        DamageScale = 1.15f,
                        AimConeScale = 1.3f,
                        CheckVisionCone = false,
                        VisionCone = 135f,
                        Speed = 7.5f,
                        DisableRadio = false,
                        IsRemoveCorpse = true,
                        WearItems = new HashSet<NpcWear>
                        {
                            new NpcWear { ShortName = "hat.cap", SkinId = 0 },
                            new NpcWear { ShortName = "hoodie", SkinId = 2408787588 },
                            new NpcWear { ShortName = "pants", SkinId = 2408786118 },
                            new NpcWear { ShortName = "shoes.boots", SkinId = 826587881 },
                            new NpcWear { ShortName = "sunglasses", SkinId = 0 }
                        },
                        BeltItems = new HashSet<NpcBelt>
                        {
                            new NpcBelt { ShortName = "rifle.lr300", Amount = 1, SkinId = 0, Mods = new HashSet<string> { "weapon.mod.flashlight", "weapon.mod.holosight" }, Ammo = string.Empty },
                            new NpcBelt { ShortName = "syringe.medical", Amount = 5, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty }
                        },
                        Kit = "",
                        TypeLootTable = 5,
                        PrefabLootTable = new PrefabLootTableConfig
                        {
                            Min = 1,
                            Max = 1,
                            UseCount = false,
                            Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_oilrig.prefab" } }
                        },
                        OwnLootTable = new LootTableConfig
                        {
                            Min = 1,
                            Max = 1,
                            UseCount = false,
                            Items = new List<ItemConfig>
                            {
                                new ItemConfig { ShortName = "scrap", MinAmount = 8, MaxAmount = 33, Chance = 36f, IsBluePrint = false, SkinId = 0, Name = "" }
                            }
                        }
                    },
                    NpcStationaryOutsideCargo = new NpcConfigCargo
                    {
                        Name = "Scientist",
                        Health = 175f,
                        RoamRange = 10f,
                        ChaseRange = 100f,
                        AttackRangeMultiplier = 1f,
                        SenseRange = 50f,
                        MemoryDuration = 30f,
                        DamageScale = 1.15f,
                        AimConeScale = 1.3f,
                        CheckVisionCone = false,
                        VisionCone = 135f,
                        Speed = 7.5f,
                        DisableRadio = false,
                        IsRemoveCorpse = true,
                        WearItems = new HashSet<NpcWear>
                        {
                            new NpcWear { ShortName = "hazmatsuit_scientist", SkinId = 0 }
                        },
                        BeltItems = new HashSet<NpcBelt>
                        {
                            new NpcBelt { ShortName = "rifle.lr300", Amount = 1, SkinId = 0, Mods = new HashSet<string> { "weapon.mod.flashlight", "weapon.mod.holosight" }, Ammo = string.Empty },
                            new NpcBelt { ShortName = "syringe.medical", Amount = 5, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty },
                            new NpcBelt { ShortName = "grenade.f1", Amount = 2, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty }
                        },
                        Kit = "",
                        TypeLootTable = 5,
                        PrefabLootTable = new PrefabLootTableConfig
                        {
                            Min = 1,
                            Max = 1,
                            UseCount = false,
                            Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_oilrig.prefab" } }
                        },
                        OwnLootTable = new LootTableConfig
                        {
                            Min = 1,
                            Max = 1,
                            UseCount = false,
                            Items = new List<ItemConfig>
                            {
                                new ItemConfig { ShortName = "scrap", MinAmount = 8, MaxAmount = 33, Chance = 36f, IsBluePrint = false, SkinId = 0, Name = "" }
                            }
                        }
                    },
                    Marker = new MarkerConfig
                    {
                        Enabled = true,
                        Type = 1,
                        Radius = 0.37967f,
                        Alpha = 0.35f,
                        Color = new ColorConfig { R = 0.81f, G = 0.25f, B = 0.15f },
                        Text = "HarborEvent"
                    },
                    MainPoint = new PointConfig
                    {
                        Enabled = true,
                        Text = "◈",
                        Size = 45,
                        Color = "#CCFF00"
                    },
                    AdditionalPoint = new PointConfig
                    {
                        Enabled = true,
                        Text = "◆",
                        Size = 25,
                        Color = "#FFC700"
                    },
                    Gui = new GuiConfig
                    {
                        IsGui = true,
                        OffsetMinY = "-56"
                    },
                    Chat = new ChatConfig
                    {
                        IsChat = true,
                        Prefix = "[HarborEvent]"
                    },
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
                    DistanceAlerts = 0f,
                    Discord = new DiscordConfig
                    {
                        IsDiscord = false,
                        WebhookUrl = "https://support.discordapp.com/hc/en-us/articles/228383668-Intro-to-Webhooks",
                        EmbedColor = 13516583,
                        Keys = new HashSet<string>
                        {
                            "PreStart",
                            "Start",
                            "PreFinish",
                            "Finish",
                            "KillBradley",
                            "OpenDoor"
                        }
                    },
                    Radius = 170f,
                    IsCreateZonePvp = false,
                    PveMode = new PveModeConfig
                    {
                        Pve = false,
                        Damage = 500f,
                        ScaleDamage = new Dictionary<string, float> { ["Npc"] = 1f, ["Bradley"] = 2f, ["Helicopter"] = 2f },
                        LootCrate = false,
                        HackCrate = false,
                        LootNpc = false,
                        DamageNpc = false,
                        DamageTank = false,
                        DamageHelicopter = false,
                        TargetNpc = false,
                        TargetTank = false,
                        TargetHelicopter = false,
                        CanEnter = false,
                        CanEnterCooldownPlayer = true,
                        TimeExitOwner = 300,
                        AlertTime = 60,
                        RestoreUponDeath = true,
                        CooldownOwner = 86400,
                        Darkening = 12
                    },
                    NTeleportationInterrupt = true,
                    RemoveBetterNpc = true,
                    Economy = new EconomyConfig
                    {
                        Plugins = new HashSet<string> { "Economics", "Server Rewards", "IQEconomic" },
                        Min = 0,
                        Crates = new Dictionary<string, double>
                        {
                            ["crate_elite"] = 0.4,
                            ["crate_normal"] = 0.2,
                            ["crate_normal_2"] = 0.1,
                            ["dm c4"] = 0.4,
                            ["dm ammo"] = 0.3
                        },
                        Bradley = 0.8,
                        Npc = 0.3,
                        LockedCrate = 0.5,
                        Button = 0.4,
                        Commands = new HashSet<string>()
                    },
                    Commands = new HashSet<string>
                    {
                        "/remove",
                        "remove.toggle"
                    },
                    Bradley = new BradleyConfig
                    {
                        IsBradley = true,
                        Hp = 1000f,
                        ViewDistance = 100.0f,
                        SearchRange = 100.0f,
                        ScaleDamage = 1.0f,
                        CoaxAimCone = 1.1f,
                        CoaxFireRate = 1.0f,
                        CoaxBurstLength = 10,
                        MemoryDuration = 20f,
                        NextFireTime = 10f,
                        TopTurretFireRate = 0.25f,
                        CountCrates = 3,
                        TypeLootTable = 0,
                        PrefabLootTable = new PrefabLootTableConfig
                        {
                            Min = 1,
                            Max = 1,
                            UseCount = true,
                            Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/prefabs/npc/m2bradley/bradley_crate.prefab" } }
                        },
                        OwnLootTable = new LootTableConfig
                        {
                            Min = 1,
                            Max = 1,
                            UseCount = true,
                            Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 5, MaxAmount = 10, Chance = 100f, IsBluePrint = false, SkinId = 0, Name = "" } }
                        }
                    },
                    Helicopter = new HelicopterConfig
                    {
                        Hp = 10000f,
                        HpMainRotor = 750f,
                        HpTailRotor = 375f,
                        CountCrates = 4,
                        TimeBetweenRockets = 0.2f,
                        FireRate = 0.125f,
                        TimeBetweenBursts = 3f,
                        BurstLength = 3f,
                        MaxTargetRange = 300f,
                        IsLongDistance = false
                    },
                    Cctv = "Harbor",
                    IsSmallHarbor = true,
                    IsLargeHarbor = true,
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
                ["PreStart"] = "{0} Valuable cargo will begin unloading from the cargo ship at harbor in <color=#55aaff>{1}</color>!",
                ["Start"] = "{0} The cargo ship <color=#738d43>arrives</color> at harbor in (square <color=#55aaff>{1}</color>)\nCargo unloading will <color=#738d43>begin</color> soon!\nCamera: <color=#55aaff>{2}</color>",
                ["PreFinish"] = "{0} Cargo ship unloading will <color=#ce3f27>finish</color> in <color=#55aaff>{1}</color>!",
                ["Finish"] = "{0} Cargo ship unloading <color=#ce3f27>completed</color>! The ship will head away from the island now",
                ["SetOwner"] = "{0} Player <color=#55aaff>{1}</color> <color=#738d43>has received</color> the owner status for the <color=#55aaff>Harbor Event</color>",
                ["EventActive"] = "{0} This event is active now. To finish this event (<color=#55aaff>/harborstop</color>), then (<color=#55aaff>/harborstart</color>) to start the next one!",
                ["EnterPVP"] = "{0} You <color=#ce3f27>have entered</color> the PVP zone, now other players <color=#ce3f27>can damage</color> you!",
                ["ExitPVP"] = "{0} You <color=#738d43>have left</color> the PVP zone, now other players <color=#738d43>cannot damage</color> you!",
                ["HeliArrive"] = "{0} A patrol helicopter <color=#ce3f27>has been called</color> to protect some of the important loot at the harbor!",
                ["KillBradley"] = "{0} <color=#55aaff>{1}</color> <color=#738d43>destroyed</color> the tank!",
                ["OpenDoor"] = "{0} <color=#55aaff>{1}</color> <color=#738d43>started</color> unloading a container with valuable loot!",
                ["NTeleportation"] = "{0} You <color=#ce3f27>cannot</color> teleport into the event zone!",
                ["SendEconomy"] = "{0} You <color=#738d43>have earned</color> <color=#55aaff>{1}</color> points in economics for participating in the event",
                ["NoCommand"] = "{0} You <color=#ce3f27>cannot</color> use this command in the event zone!"
            }, this);

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["PreStart"] = "{0} Через <color=#55aaff>{1}</color> начнется разгрузка ценного груза с корабля в порту!",
                ["Start"] = "{0} Корабль <color=#738d43>прибывает</color> в порт (квадрат <color=#55aaff>{1}</color>)\nРазгрузка груза скоро <color=#738d43>начнется</color>!\nКамера: <color=#55aaff>{2}</color>",
                ["PreFinish"] = "{0} Погрузка груза <color=#ce3f27>закончится</color> через <color=#55aaff>{1}</color>!",
                ["Finish"] = "{0} Разгрузка груза <color=#ce3f27>закончена</color>! Корабль уплывает с карты",
                ["SetOwner"] = "{0} Игрок <color=#55aaff>{1}</color> <color=#738d43>получил</color> статус владельца ивента для <color=#55aaff>Harbor Event</color>",
                ["EventActive"] = "{0} Ивент в данный момент активен, сначала завершите текущий ивент (<color=#55aaff>/harborstop</color>), чтобы начать следующий!",
                ["EnterPVP"] = "{0} Вы <color=#ce3f27>вошли</color> в PVP зону, теперь другие игроки <color=#ce3f27>могут</color> наносить вам урон!",
                ["ExitPVP"] = "{0} Вы <color=#738d43>вышли</color> из PVP зоны, теперь другие игроки <color=#738d43>не могут</color> наносить вам урон!",
                ["HeliArrive"] = "{0} К месту разгрузки ценного груза в порту <color=#ce3f27>вылетел</color> патрульный вертолет!",
                ["KillBradley"] = "{0} <color=#55aaff>{1}</color> <color=#738d43>уничтожил</color> танк!",
                ["OpenDoor"] = "{0} <color=#55aaff>{1}</color> <color=#738d43>начал</color> разгрузку контейнера с ценным грузом!",
                ["NTeleportation"] = "{0} Вы <color=#ce3f27>не можете</color> телепортироваться в зоне ивента!",
                ["SendEconomy"] = "{0} Вы <color=#738d43>получили</color> <color=#55aaff>{1}</color> баллов в экономику за прохождение ивента",
                ["NoCommand"] = "{0} Вы <color=#ce3f27>не можете</color> использовать данную команду в зоне ивента!"
            }, this, "ru");
        }

        private string GetMessage(string langKey, string userId) => lang.GetMessage(langKey, _ins, userId);

        private string GetMessage(string langKey, string userId, params object[] args) => (args.Length == 0) ? GetMessage(langKey, userId) : string.Format(GetMessage(langKey, userId), args);
        #endregion Lang

        #region Oxide Hooks
        private static HarborEvent _ins;

        private void Init()
        {
            _ins = this;
            ToggleHooks(false);
        }

        private void OnServerInitialized()
        {
            if (GetMonument() == null)
            {
                PrintError("The harbor location is missing on the map. The plugin cannot be loaded!");
                NextTick(() => Interface.Oxide.UnloadPlugin(Name));
                return;
            }
            CheckAllLootTables();
            ServerMgr.Instance.StartCoroutine(DownloadImages());
            StartTimer();
        }

        private void Unload()
        {
            if (Controller != null) Finish();
            _ins = null;
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null) return null;
            if (Controller.Entities.Contains(entity))
            {
                if (entity is SamSite) return null;
                return true;
            }
            if (Controller.Bradley.IsExists())
            {
                if (entity is BradleyAPC && entity as BradleyAPC == Controller.Bradley && Controller.Bradley.myRigidBody.isKinematic) return true;
                if (info.Initiator == Controller.Bradley) info.damageTypes.ScaleAll(_config.Bradley.ScaleDamage);
            }
            return null;
        }

        private object CanBuild(Planner planner, Construction prefab, Construction.Target target)
        {
            if (planner == null) return null;
            BasePlayer player = planner.GetOwnerPlayer();
            if (player == null) return null;
            if (Controller.Players.Contains(player)) return true;
            return null;
        }

        private object CanChangeGrade(BasePlayer player, BuildingBlock block, BuildingGrade.Enum grade, ulong skin)
        {
            if (block != null && Controller.Entities.Contains(block)) return false;
            else return null;
        }

        private object OnStructureRotate(BuildingBlock block, BasePlayer player)
        {
            if (block != null && Controller.Entities.Contains(block)) return true;
            else return null;
        }

        private object OnButtonPress(PressButton button, BasePlayer player)
        {
            if (button == null || player == null) return null;
            if (button == Controller.Button && !Controller.Bradley.IsExists() && Controller.Cargo.HasFlag(BaseEntity.Flags.Reserved1))
            {
                if (ActivePveMode && PveMode.Call("CanActionEvent", Name, player) != null) return true;
                Controller.Button = null;
                Controller.ContainerHackCrate.SetParent(null, true, true);
                Controller.CraneGoToContainer();
                ActionEconomy(player.userID, "Button");
                AlertToAllPlayers("OpenDoor", _config.Chat.Prefix, player.displayName);
            }
            return null;
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (!_config.Marker.Enabled || Controller == null || !player.IsPlayer()) return;
            if (player.HasPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot)) timer.In(2f, () => OnPlayerConnected(player));
            else Controller.UpdateMapMarkers();
        }

        private void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (player != null && Controller.Players.Contains(player))
                Controller.ExitPlayer(player);
        }

        private void OnEntityDeath(BradleyAPC bradley, HitInfo info)
        {
            if (bradley == null || info == null) return;
            if (bradley == Controller.Bradley)
            {
                Controller.DestroyPositionBradley = bradley.transform.position;
                BasePlayer attacker = info.InitiatorPlayer;
                if (attacker != null)
                {
                    ActionEconomy(attacker.userID, "Bradley");
                    AlertToAllPlayers("KillBradley", _config.Chat.Prefix, attacker.displayName);
                }
            }
        }

        private void OnEntityDeath(ScientistNPC npc, HitInfo info)
        {
            if (npc == null || info == null) return;
            BasePlayer attacker = info.InitiatorPlayer;
            if (Controller.Scientists.Contains(npc) && attacker.IsPlayer()) ActionEconomy(attacker.userID, "Npc");
        }

        private object CanBradleyApcTarget(BradleyAPC bradley, BaseEntity entity)
        {
            if (bradley == null || entity == null) return null;
            if (bradley == Controller.Bradley)
            {
                if ((entity as BasePlayer).IsPlayer())
                {
                    if (bradley.myRigidBody.isKinematic) return false;
                    else return null;
                }
                else return false;
            }
            return null;
        }

        private object OnEntityKill(CargoShip entity)
        {
            if (entity == null || Controller == null) return null;
            if (entity == Controller.Cargo) return true;
            return null;
        }

        private void OnEntityKill(LootContainer entity)
        {
            if (entity == null || Controller == null) return;
            if (Controller.Crates.ContainsKey(entity)) Controller.Crates.Remove(entity);
            else if (entity is HackableLockedCrate)
            {
                HackableLockedCrate hackcrate = entity as HackableLockedCrate;
                if (Controller.HackCrates.ContainsKey(hackcrate)) Controller.HackCrates.Remove(hackcrate);
            }
        }

        private void OnEntitySpawned(ScientistNPC npc)
        {
            if (npc != null && (npc.ShortPrefabName == "scientistnpc_cargo_turret_lr300" || npc.ShortPrefabName == "scientistnpc_cargo_turret_any" || npc.ShortPrefabName == "scientistnpc_cargo"))
                timer.In(1.5f, () => Controller.SpawnCargoScientist(npc));
        }

        private void OnEntitySpawned(LootContainer crate)
        {
            if (crate != null && (crate.ShortPrefabName == "codelockedhackablecrate" || crate.ShortPrefabName == "crate_elite" || crate.ShortPrefabName == "crate_normal" || crate.ShortPrefabName == "crate_normal_2"))
                timer.In(1.5f, () => Controller.SpawnCargoCrate(crate));
        }

        private void OnEntitySpawned(CargoShipContainer container)
        {
            timer.In(1.5f, () =>
            {
                if (container == null || Vector3.Distance(container.transform.position, Controller.transform.position) > _config.Radius) return;
                container.EnableSaving(false);
                foreach (BaseEntity entity in container.children)
                {
                    if (entity == null) continue;
                    entity.EnableSaving(false);
                }
            });
        }

        private object OnCargoShipHarborApproach(CargoShip cargo, CargoNotifier cn)
        {
            if (cargo == null || Controller == null) return null;
            if (cargo.harborIndex == Controller.HarborIndex) return true;
            else return null;
        }

        private Dictionary<ulong, ulong> StartHackCrates { get; } = new Dictionary<ulong, ulong>();

        private void CanHackCrate(BasePlayer player, HackableLockedCrate crate)
        {
            if (player == null || crate == null) return;
            if (Controller.HackCrates.ContainsKey(crate))
            {
                ulong crateId = crate.net.ID.Value;
                if (StartHackCrates.ContainsKey(crateId)) StartHackCrates[crateId] = player.userID;
                else StartHackCrates.Add(crateId, player.userID);
            }
        }

        private void OnCrateHack(HackableLockedCrate crate)
        {
            if (crate == null) return;
            ulong crateId = crate.net.ID.Value;
            ulong playerId;
            if (StartHackCrates.TryGetValue(crateId, out playerId))
            {
                StartHackCrates.Remove(crateId);
                ActionEconomy(playerId, "LockedCrate");
                if (crate == Controller.ContainerHackCrate)
                {
                    if (_config.ContainerHackCrates.IncreaseEventTime && Controller.TimeToFinish < (int)_config.ContainerHackCrates.UnlockTime) Controller.TimeToFinish += (int)_config.ContainerHackCrates.UnlockTime;
                    if (_config.ContainerHackCrates.CallHelicopter && !Controller.Helicopter.IsExists())
                    {
                        AlertToAllPlayers("HeliArrive", _config.Chat.Prefix);
                        Controller.SpawnHelicopter();
                    }
                }
                else
                {
                    if (_config.CargoHackCrates.IncreaseEventTime && Controller.TimeToFinish < (int)_config.CargoHackCrates.UnlockTime && Controller.Cargo.HasFlag(BaseEntity.Flags.Reserved1)) Controller.TimeToFinish += (int)_config.CargoHackCrates.UnlockTime;
                    if (_config.CargoHackCrates.CallHelicopter && !Controller.Helicopter.IsExists())
                    {
                        AlertToAllPlayers("HeliArrive", _config.Chat.Prefix);
                        Controller.SpawnHelicopter();
                    }
                }
            }
        }

        private HashSet<ulong> LootableCrates { get; } = new HashSet<ulong>();

        private void OnLootEntity(BasePlayer player, LootContainer container)
        {
            if (!player.IsPlayer() || !container.IsExists() || LootableCrates.Contains(container.net.ID.Value)) return;
            if (Controller.Crates.ContainsKey(container))
            {
                LootableCrates.Add(container.net.ID.Value);
                ActionEconomy(player.userID, "Crates", container.ShortPrefabName);
            }
        }

        private object OnPlayerCommand(BasePlayer player, string command, string[] args)
        {
            if (player != null && Controller.Players.Contains(player))
            {
                command = "/" + command;
                if (_config.Commands.Contains(command.ToLower()))
                {
                    AlertToPlayer(player, GetMessage("NoCommand", player.UserIDString, _config.Chat.Prefix));
                    return true;
                }
            }
            return null;
        }

        private object OnServerCommand(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.cmd == null) return null;
            BasePlayer player = arg.Player();
            if (player != null && Controller.Players.Contains(player))
            {
                if (_config.Commands.Contains(arg.cmd.Name.ToLower()) || _config.Commands.Contains(arg.cmd.FullName.ToLower()))
                {
                    AlertToPlayer(player, GetMessage("NoCommand", player.UserIDString, _config.Chat.Prefix));
                    return true;
                }
            }
            return null;
        }
        #endregion Oxide Hooks

        #region Controller
        internal class Prefab { public string Path; public Vector3 Pos; public Vector3 Rot; }
        internal HashSet<Prefab> ContainerEntities { get; } = new HashSet<Prefab>
        {
            new Prefab { Path = "assets/prefabs/building core/wall/wall.prefab", Pos = new Vector3(-1.5f, -0.1f, -3f), Rot = new Vector3(0f, 0f, 270f) },
            new Prefab { Path = "assets/prefabs/building core/wall/wall.prefab", Pos = new Vector3(-1.5f, -0.1f, 0f), Rot = new Vector3(0f, 0f, 270f) },
            new Prefab { Path = "assets/prefabs/building core/wall/wall.prefab", Pos = new Vector3(-1.5f, -0.1f, 3f), Rot = new Vector3(0f, 0f, 270f) },
            new Prefab { Path = "assets/prefabs/building core/wall/wall.prefab", Pos = new Vector3(-1.5f, 2.9f, -3f), Rot = new Vector3(0f, 180f, 90f) },
            new Prefab { Path = "assets/prefabs/building core/wall/wall.prefab", Pos = new Vector3(-1.5f, 2.9f, 0f), Rot = new Vector3(0f, 180f, 90f) },
            new Prefab { Path = "assets/prefabs/building core/wall/wall.prefab", Pos = new Vector3(-1.5f, 2.9f, 3f), Rot = new Vector3(0f, 180f, 90f) },
            new Prefab { Path = "assets/prefabs/building core/wall/wall.prefab", Pos = new Vector3(-1.5f, -0.1f, 3f), Rot = new Vector3(0f, 180f, 0f) },
            new Prefab { Path = "assets/prefabs/building core/wall/wall.prefab", Pos = new Vector3(-1.5f, -0.1f, 0f), Rot = new Vector3(0f, 180f, 0f) },
            new Prefab { Path = "assets/prefabs/building core/wall/wall.prefab", Pos = new Vector3(-1.5f, -0.1f, -3f), Rot = new Vector3(0f, 180f, 0f) },
            new Prefab { Path = "assets/prefabs/building core/wall/wall.prefab", Pos = new Vector3(1.5f, -0.1f, -3f), Rot = new Vector3(0f, 0f, 0f) },
            new Prefab { Path = "assets/prefabs/building core/wall/wall.prefab", Pos = new Vector3(1.5f, -0.1f, 0f), Rot = new Vector3(0f, 0f, 0f) },
            new Prefab { Path = "assets/prefabs/building core/wall/wall.prefab", Pos = new Vector3(1.5f, -0.1f, 3f), Rot = new Vector3(0f, 0f, 0f) },
            new Prefab { Path = "assets/prefabs/building core/wall.doorway/wall.doorway.prefab", Pos = new Vector3(0f, -0.1f, 4.5f), Rot = new Vector3(0f, 270f, 0f) },
            new Prefab { Path = "assets/prefabs/building core/wall.doorway/wall.doorway.prefab", Pos = new Vector3(0f, -0.1f, -4.5f), Rot = new Vector3(0f, 90f, 0f) },
            new Prefab { Path = "assets/bundled/prefabs/static/door.hinged.security.red.prefab", Pos = new Vector3(0f, -0.1f, 4.5f), Rot = new Vector3(0f, 270f, 0f) },
            new Prefab { Path = "assets/bundled/prefabs/static/door.hinged.security.red.prefab", Pos = new Vector3(0f, -0.1f, -4.5f), Rot = new Vector3(0f, 90f, 0f) },
            new Prefab { Path = "assets/prefabs/deployable/cctvcamera/cctv.static.prefab", Pos = new Vector3(0f, 2.4f, 4.5f), Rot = new Vector3(18f, 180f, 0f) }
        };
        internal HashSet<Prefab> ContainerCrates { get; } = new HashSet<Prefab>
        {
            new Prefab { Path = "assets/bundled/prefabs/radtown/crate_elite.prefab", Pos = new Vector3(0.97f, 0f, -2.273f), Rot = new Vector3(0f, 270f, 0f) },
            new Prefab { Path = "assets/bundled/prefabs/radtown/crate_elite.prefab", Pos = new Vector3(-0.97f, 0f, -2.273f), Rot = new Vector3(0f, 90f, 0f) },
            new Prefab { Path = "assets/bundled/prefabs/radtown/crate_normal.prefab", Pos = new Vector3(0.97f, 0f, 2.181f), Rot = new Vector3(0f, 90f, 0f) },
            new Prefab { Path = "assets/bundled/prefabs/radtown/crate_normal.prefab", Pos = new Vector3(-0.97f, 0f, 2.181f), Rot = new Vector3(0f, 270f, 0f) },
            new Prefab { Path = "assets/bundled/prefabs/radtown/dmloot/dm c4.prefab", Pos = new Vector3(0f, 0f, 2.181f), Rot = new Vector3(0f, 0f, 0f) },
            new Prefab { Path = "assets/bundled/prefabs/radtown/dmloot/dm ammo.prefab", Pos = new Vector3(0f, 0f, -2.273f), Rot = new Vector3(0f, 180f, 0f) }
        };
        internal HashSet<Prefab> Bridge { get; } = new HashSet<Prefab>
        {
            new Prefab { Path = "assets/prefabs/building core/ramp/ramp.prefab", Pos = new Vector3(97.139f, 3.901f, -74.311f), Rot = new Vector3(358.44f, 256.365f, 16.24f) },
            new Prefab { Path = "assets/prefabs/building core/ramp/ramp.prefab", Pos = new Vector3(96.557f, 5.457f, -71.729f), Rot = new Vector3(358.44f, 256.365f, 16.24f) },
            new Prefab { Path = "assets/prefabs/building core/ramp/ramp.prefab", Pos = new Vector3(95.977f, 7.004f, -69.16f), Rot = new Vector3(358.44f, 256.365f, 16.447f) },
            new Prefab { Path = "assets/prefabs/building core/ramp/ramp.prefab", Pos = new Vector3(94.821f, 10.112f, -64.034f), Rot = new Vector3(358.44f, 256.365f, 16.305f) },
            new Prefab { Path = "assets/prefabs/building core/ramp/ramp.prefab", Pos = new Vector3(94.305f, 11.483f, -61.75f), Rot = new Vector3(358.44f, 256.365f, 16.305f) },
            new Prefab { Path = "assets/prefabs/building core/ramp/ramp.prefab", Pos = new Vector3(95.399f, 8.559f, -66.6f), Rot = new Vector3(358.44f, 256.365f, 16.305f) },
            new Prefab { Path = "assets/prefabs/building core/wall.frame/wall.frame.prefab", Pos = new Vector3(95.812f, 2.596f, -68.988f), Rot = new Vector3(358.44f, 256.365f, 0f) },
            new Prefab { Path = "assets/prefabs/building core/wall.frame/wall.frame.prefab", Pos = new Vector3(95.237f, 4.115f, -66.446f), Rot = new Vector3(358.44f, 256.365f, 0f) },
            new Prefab { Path = "assets/prefabs/building core/wall.frame/wall.frame.prefab", Pos = new Vector3(94.66f, 5.635f, -63.89f), Rot = new Vector3(358.44f, 256.365f, 0f) },
            new Prefab { Path = "assets/prefabs/building core/wall.frame/wall.frame.prefab", Pos = new Vector3(94.582f, 2.699f, -63.908f), Rot = new Vector3(358.44f, 256.365f, 0f) },
            new Prefab { Path = "assets/prefabs/building core/wall.frame/wall.frame.prefab", Pos = new Vector3(94.077f, 7.197f, -61.307f), Rot = new Vector3(358.44f, 256.365f, 0f) },
            new Prefab { Path = "assets/prefabs/building core/wall.frame/wall.frame.prefab", Pos = new Vector3(93.999f, 4.224f, -61.326f), Rot = new Vector3(358.44f, 256.365f, 0f) }
        };

        internal HashSet<Vector3> Marker { get; } = new HashSet<Vector3>
        {
            new Vector3(48f, 0f, 8f),
            new Vector3(48f, 0f, 6f),
            new Vector3(48f, 0f, 4f),
            new Vector3(48f, 0f, 2f),
            new Vector3(48f, 0f, 0f),
            new Vector3(48f, 0f, -2f),
            new Vector3(48f, 0f, -4f),
            new Vector3(46f, 0f, 16f),
            new Vector3(46f, 0f, 14f),
            new Vector3(46f, 0f, 12f),
            new Vector3(46f, 0f, 10f),
            new Vector3(46f, 0f, 8f),
            new Vector3(46f, 0f, 6f),
            new Vector3(46f, 0f, 4f),
            new Vector3(46f, 0f, 2f),
            new Vector3(46f, 0f, 0f),
            new Vector3(46f, 0f, -2f),
            new Vector3(46f, 0f, -4f),
            new Vector3(46f, 0f, -6f),
            new Vector3(46f, 0f, -8f),
            new Vector3(46f, 0f, -10f),
            new Vector3(46f, 0f, -12f),
            new Vector3(44f, 0f, 22f),
            new Vector3(44f, 0f, 20f),
            new Vector3(44f, 0f, 18f),
            new Vector3(44f, 0f, 16f),
            new Vector3(44f, 0f, 14f),
            new Vector3(44f, 0f, 12f),
            new Vector3(44f, 0f, 10f),
            new Vector3(44f, 0f, -4f),
            new Vector3(44f, 0f, -6f),
            new Vector3(44f, 0f, -8f),
            new Vector3(44f, 0f, -10f),
            new Vector3(44f, 0f, -12f),
            new Vector3(44f, 0f, -14f),
            new Vector3(44f, 0f, -16f),
            new Vector3(42f, 0f, 26f),
            new Vector3(42f, 0f, 24f),
            new Vector3(42f, 0f, 22f),
            new Vector3(42f, 0f, 20f),
            new Vector3(42f, 0f, 18f),
            new Vector3(42f, 0f, -14f),
            new Vector3(42f, 0f, -16f),
            new Vector3(42f, 0f, -18f),
            new Vector3(42f, 0f, -20f),
            new Vector3(40f, 0f, 30f),
            new Vector3(40f, 0f, 28f),
            new Vector3(40f, 0f, 26f),
            new Vector3(40f, 0f, 24f),
            new Vector3(40f, 0f, -18f),
            new Vector3(40f, 0f, -20f),
            new Vector3(40f, 0f, -22f),
            new Vector3(40f, 0f, -24f),
            new Vector3(38f, 0f, 32f),
            new Vector3(38f, 0f, 30f),
            new Vector3(38f, 0f, 28f),
            new Vector3(38f, 0f, -22f),
            new Vector3(38f, 0f, -24f),
            new Vector3(38f, 0f, -26f),
            new Vector3(36f, 0f, 34f),
            new Vector3(36f, 0f, 32f),
            new Vector3(36f, 0f, 30f),
            new Vector3(36f, 0f, -24f),
            new Vector3(36f, 0f, -26f),
            new Vector3(36f, 0f, -28f),
            new Vector3(34f, 0f, 36f),
            new Vector3(34f, 0f, 34f),
            new Vector3(34f, 0f, 32f),
            new Vector3(34f, 0f, -28f),
            new Vector3(34f, 0f, -30f),
            new Vector3(32f, 0f, 38f),
            new Vector3(32f, 0f, 36f),
            new Vector3(32f, 0f, 34f),
            new Vector3(32f, 0f, -30f),
            new Vector3(32f, 0f, -32f),
            new Vector3(30f, 0f, 40f),
            new Vector3(30f, 0f, 38f),
            new Vector3(30f, 0f, 36f),
            new Vector3(30f, 0f, -6f),
            new Vector3(30f, 0f, -8f),
            new Vector3(30f, 0f, -10f),
            new Vector3(30f, 0f, -12f),
            new Vector3(30f, 0f, -14f),
            new Vector3(30f, 0f, -32f),
            new Vector3(30f, 0f, -34f),
            new Vector3(28f, 0f, 42f),
            new Vector3(28f, 0f, 40f),
            new Vector3(28f, 0f, 38f),
            new Vector3(28f, 0f, 2f),
            new Vector3(28f, 0f, 0f),
            new Vector3(28f, 0f, -2f),
            new Vector3(28f, 0f, -4f),
            new Vector3(28f, 0f, -6f),
            new Vector3(28f, 0f, -8f),
            new Vector3(28f, 0f, -10f),
            new Vector3(28f, 0f, -12f),
            new Vector3(28f, 0f, -34f),
            new Vector3(28f, 0f, -36f),
            new Vector3(26f, 0f, 42f),
            new Vector3(26f, 0f, 40f),
            new Vector3(26f, 0f, 2f),
            new Vector3(26f, 0f, 0f),
            new Vector3(26f, 0f, -2f),
            new Vector3(26f, 0f, -4f),
            new Vector3(26f, 0f, -6f),
            new Vector3(26f, 0f, -8f),
            new Vector3(26f, 0f, -10f),
            new Vector3(26f, 0f, -34f),
            new Vector3(26f, 0f, -36f),
            new Vector3(26f, 0f, -38f),
            new Vector3(24f, 0f, 44f),
            new Vector3(24f, 0f, 42f),
            new Vector3(24f, 0f, 0f),
            new Vector3(24f, 0f, -2f),
            new Vector3(24f, 0f, -4f),
            new Vector3(24f, 0f, -6f),
            new Vector3(24f, 0f, -8f),
            new Vector3(24f, 0f, -10f),
            new Vector3(24f, 0f, -12f),
            new Vector3(24f, 0f, -36f),
            new Vector3(24f, 0f, -38f),
            new Vector3(22f, 0f, 44f),
            new Vector3(22f, 0f, 42f),
            new Vector3(22f, 0f, 0f),
            new Vector3(22f, 0f, -2f),
            new Vector3(22f, 0f, -4f),
            new Vector3(22f, 0f, -6f),
            new Vector3(22f, 0f, -8f),
            new Vector3(22f, 0f, -10f),
            new Vector3(22f, 0f, -12f),
            new Vector3(22f, 0f, -14f),
            new Vector3(22f, 0f, -16f),
            new Vector3(22f, 0f, -38f),
            new Vector3(22f, 0f, -40f),
            new Vector3(20f, 0f, 46f),
            new Vector3(20f, 0f, 44f),
            new Vector3(20f, 0f, 0f),
            new Vector3(20f, 0f, -2f),
            new Vector3(20f, 0f, -4f),
            new Vector3(20f, 0f, -6f),
            new Vector3(20f, 0f, -8f),
            new Vector3(20f, 0f, -10f),
            new Vector3(20f, 0f, -12f),
            new Vector3(20f, 0f, -14f),
            new Vector3(20f, 0f, -16f),
            new Vector3(20f, 0f, -18f),
            new Vector3(20f, 0f, -38f),
            new Vector3(20f, 0f, -40f),
            new Vector3(18f, 0f, 46f),
            new Vector3(18f, 0f, 44f),
            new Vector3(18f, 0f, -2f),
            new Vector3(18f, 0f, -4f),
            new Vector3(18f, 0f, -6f),
            new Vector3(18f, 0f, -12f),
            new Vector3(18f, 0f, -14f),
            new Vector3(18f, 0f, -16f),
            new Vector3(18f, 0f, -18f),
            new Vector3(18f, 0f, -20f),
            new Vector3(18f, 0f, -40f),
            new Vector3(18f, 0f, -42f),
            new Vector3(16f, 0f, 48f),
            new Vector3(16f, 0f, 46f),
            new Vector3(16f, 0f, -2f),
            new Vector3(16f, 0f, -4f),
            new Vector3(16f, 0f, -6f),
            new Vector3(16f, 0f, -14f),
            new Vector3(16f, 0f, -16f),
            new Vector3(16f, 0f, -18f),
            new Vector3(16f, 0f, -20f),
            new Vector3(16f, 0f, -40f),
            new Vector3(16f, 0f, -42f),
            new Vector3(14f, 0f, 48f),
            new Vector3(14f, 0f, 46f),
            new Vector3(14f, 0f, -4f),
            new Vector3(14f, 0f, -16f),
            new Vector3(14f, 0f, -18f),
            new Vector3(14f, 0f, -20f),
            new Vector3(14f, 0f, -22f),
            new Vector3(14f, 0f, -40f),
            new Vector3(14f, 0f, -42f),
            new Vector3(12f, 0f, 48f),
            new Vector3(12f, 0f, 46f),
            new Vector3(12f, 0f, 12f),
            new Vector3(12f, 0f, 10f),
            new Vector3(12f, 0f, 8f),
            new Vector3(12f, 0f, -18f),
            new Vector3(12f, 0f, -20f),
            new Vector3(12f, 0f, -22f),
            new Vector3(12f, 0f, -42f),
            new Vector3(12f, 0f, -44f),
            new Vector3(10f, 0f, 50f),
            new Vector3(10f, 0f, 48f),
            new Vector3(10f, 0f, 12f),
            new Vector3(10f, 0f, 10f),
            new Vector3(10f, 0f, 8f),
            new Vector3(10f, 0f, -18f),
            new Vector3(10f, 0f, -20f),
            new Vector3(10f, 0f, -22f),
            new Vector3(10f, 0f, -24f),
            new Vector3(10f, 0f, -42f),
            new Vector3(10f, 0f, -44f),
            new Vector3(8f, 0f, 50f),
            new Vector3(8f, 0f, 48f),
            new Vector3(8f, 0f, 12f),
            new Vector3(8f, 0f, 10f),
            new Vector3(8f, 0f, 8f),
            new Vector3(8f, 0f, -20f),
            new Vector3(8f, 0f, -22f),
            new Vector3(8f, 0f, -24f),
            new Vector3(8f, 0f, -42f),
            new Vector3(8f, 0f, -44f),
            new Vector3(6f, 0f, 50f),
            new Vector3(6f, 0f, 48f),
            new Vector3(6f, 0f, 32f),
            new Vector3(6f, 0f, 30f),
            new Vector3(6f, 0f, 28f),
            new Vector3(6f, 0f, 26f),
            new Vector3(6f, 0f, 24f),
            new Vector3(6f, 0f, 12f),
            new Vector3(6f, 0f, 10f),
            new Vector3(6f, 0f, 8f),
            new Vector3(6f, 0f, -20f),
            new Vector3(6f, 0f, -22f),
            new Vector3(6f, 0f, -24f),
            new Vector3(6f, 0f, -42f),
            new Vector3(6f, 0f, -44f),
            new Vector3(4f, 0f, 50f),
            new Vector3(4f, 0f, 48f),
            new Vector3(4f, 0f, 34f),
            new Vector3(4f, 0f, 32f),
            new Vector3(4f, 0f, 26f),
            new Vector3(4f, 0f, 24f),
            new Vector3(4f, 0f, 22f),
            new Vector3(4f, 0f, 12f),
            new Vector3(4f, 0f, 10f),
            new Vector3(4f, 0f, 8f),
            new Vector3(4f, 0f, -20f),
            new Vector3(4f, 0f, -22f),
            new Vector3(4f, 0f, -24f),
            new Vector3(4f, 0f, -42f),
            new Vector3(4f, 0f, -44f),
            new Vector3(2f, 0f, 50f),
            new Vector3(2f, 0f, 48f),
            new Vector3(2f, 0f, 36f),
            new Vector3(2f, 0f, 34f),
            new Vector3(2f, 0f, 24f),
            new Vector3(2f, 0f, 22f),
            new Vector3(2f, 0f, 20f),
            new Vector3(2f, 0f, 18f),
            new Vector3(2f, 0f, 16f),
            new Vector3(2f, 0f, 14f),
            new Vector3(2f, 0f, 12f),
            new Vector3(2f, 0f, 10f),
            new Vector3(2f, 0f, 8f),
            new Vector3(2f, 0f, 6f),
            new Vector3(2f, 0f, 4f),
            new Vector3(2f, 0f, 2f),
            new Vector3(2f, 0f, 0f),
            new Vector3(2f, 0f, -2f),
            new Vector3(2f, 0f, -4f),
            new Vector3(2f, 0f, -6f),
            new Vector3(2f, 0f, -8f),
            new Vector3(2f, 0f, -10f),
            new Vector3(2f, 0f, -12f),
            new Vector3(2f, 0f, -14f),
            new Vector3(2f, 0f, -16f),
            new Vector3(2f, 0f, -18f),
            new Vector3(2f, 0f, -20f),
            new Vector3(2f, 0f, -22f),
            new Vector3(2f, 0f, -24f),
            new Vector3(2f, 0f, -26f),
            new Vector3(2f, 0f, -28f),
            new Vector3(2f, 0f, -44f),
            new Vector3(0f, 0f, 50f),
            new Vector3(0f, 0f, 48f),
            new Vector3(0f, 0f, 36f),
            new Vector3(0f, 0f, 34f),
            new Vector3(0f, 0f, 24f),
            new Vector3(0f, 0f, 22f),
            new Vector3(0f, 0f, 20f),
            new Vector3(0f, 0f, 18f),
            new Vector3(0f, 0f, 16f),
            new Vector3(0f, 0f, 14f),
            new Vector3(0f, 0f, 12f),
            new Vector3(0f, 0f, 10f),
            new Vector3(0f, 0f, 8f),
            new Vector3(0f, 0f, 6f),
            new Vector3(0f, 0f, 4f),
            new Vector3(0f, 0f, 2f),
            new Vector3(0f, 0f, 0f),
            new Vector3(0f, 0f, -2f),
            new Vector3(0f, 0f, -4f),
            new Vector3(0f, 0f, -6f),
            new Vector3(0f, 0f, -8f),
            new Vector3(0f, 0f, -10f),
            new Vector3(0f, 0f, -12f),
            new Vector3(0f, 0f, -14f),
            new Vector3(0f, 0f, -16f),
            new Vector3(0f, 0f, -18f),
            new Vector3(0f, 0f, -20f),
            new Vector3(0f, 0f, -22f),
            new Vector3(0f, 0f, -24f),
            new Vector3(0f, 0f, -26f),
            new Vector3(0f, 0f, -28f),
            new Vector3(0f, 0f, -30f),
            new Vector3(0f, 0f, -44f),
            new Vector3(-2f, 0f, 50f),
            new Vector3(-2f, 0f, 48f),
            new Vector3(-2f, 0f, 36f),
            new Vector3(-2f, 0f, 34f),
            new Vector3(-2f, 0f, 24f),
            new Vector3(-2f, 0f, 22f),
            new Vector3(-2f, 0f, 20f),
            new Vector3(-2f, 0f, 18f),
            new Vector3(-2f, 0f, 16f),
            new Vector3(-2f, 0f, 14f),
            new Vector3(-2f, 0f, 12f),
            new Vector3(-2f, 0f, 10f),
            new Vector3(-2f, 0f, 8f),
            new Vector3(-2f, 0f, 6f),
            new Vector3(-2f, 0f, 4f),
            new Vector3(-2f, 0f, 2f),
            new Vector3(-2f, 0f, 0f),
            new Vector3(-2f, 0f, -2f),
            new Vector3(-2f, 0f, -4f),
            new Vector3(-2f, 0f, -6f),
            new Vector3(-2f, 0f, -8f),
            new Vector3(-2f, 0f, -10f),
            new Vector3(-2f, 0f, -12f),
            new Vector3(-2f, 0f, -14f),
            new Vector3(-2f, 0f, -16f),
            new Vector3(-2f, 0f, -18f),
            new Vector3(-2f, 0f, -20f),
            new Vector3(-2f, 0f, -22f),
            new Vector3(-2f, 0f, -24f),
            new Vector3(-2f, 0f, -26f),
            new Vector3(-2f, 0f, -28f),
            new Vector3(-2f, 0f, -44f),
            new Vector3(-4f, 0f, 50f),
            new Vector3(-4f, 0f, 48f),
            new Vector3(-4f, 0f, 34f),
            new Vector3(-4f, 0f, 32f),
            new Vector3(-4f, 0f, 30f),
            new Vector3(-4f, 0f, 26f),
            new Vector3(-4f, 0f, 24f),
            new Vector3(-4f, 0f, 12f),
            new Vector3(-4f, 0f, 10f),
            new Vector3(-4f, 0f, 8f),
            new Vector3(-4f, 0f, -20f),
            new Vector3(-4f, 0f, -22f),
            new Vector3(-4f, 0f, -24f),
            new Vector3(-4f, 0f, -42f),
            new Vector3(-4f, 0f, -44f),
            new Vector3(-6f, 0f, 50f),
            new Vector3(-6f, 0f, 48f),
            new Vector3(-6f, 0f, 32f),
            new Vector3(-6f, 0f, 30f),
            new Vector3(-6f, 0f, 28f),
            new Vector3(-6f, 0f, 26f),
            new Vector3(-6f, 0f, 12f),
            new Vector3(-6f, 0f, 10f),
            new Vector3(-6f, 0f, 8f),
            new Vector3(-6f, 0f, -20f),
            new Vector3(-6f, 0f, -22f),
            new Vector3(-6f, 0f, -24f),
            new Vector3(-6f, 0f, -42f),
            new Vector3(-6f, 0f, -44f),
            new Vector3(-8f, 0f, 50f),
            new Vector3(-8f, 0f, 48f),
            new Vector3(-8f, 0f, 12f),
            new Vector3(-8f, 0f, 10f),
            new Vector3(-8f, 0f, 8f),
            new Vector3(-8f, 0f, -20f),
            new Vector3(-8f, 0f, -22f),
            new Vector3(-8f, 0f, -24f),
            new Vector3(-8f, 0f, -42f),
            new Vector3(-8f, 0f, -44f),
            new Vector3(-10f, 0f, 50f),
            new Vector3(-10f, 0f, 48f),
            new Vector3(-10f, 0f, 12f),
            new Vector3(-10f, 0f, 10f),
            new Vector3(-10f, 0f, 8f),
            new Vector3(-10f, 0f, -18f),
            new Vector3(-10f, 0f, -20f),
            new Vector3(-10f, 0f, -22f),
            new Vector3(-10f, 0f, -24f),
            new Vector3(-10f, 0f, -42f),
            new Vector3(-10f, 0f, -44f),
            new Vector3(-12f, 0f, 48f),
            new Vector3(-12f, 0f, 46f),
            new Vector3(-12f, 0f, 10f),
            new Vector3(-12f, 0f, 8f),
            new Vector3(-12f, 0f, -4f),
            new Vector3(-12f, 0f, -18f),
            new Vector3(-12f, 0f, -20f),
            new Vector3(-12f, 0f, -22f),
            new Vector3(-12f, 0f, -42f),
            new Vector3(-12f, 0f, -44f),
            new Vector3(-14f, 0f, 48f),
            new Vector3(-14f, 0f, 46f),
            new Vector3(-14f, 0f, -4f),
            new Vector3(-14f, 0f, -6f),
            new Vector3(-14f, 0f, -16f),
            new Vector3(-14f, 0f, -18f),
            new Vector3(-14f, 0f, -20f),
            new Vector3(-14f, 0f, -22f),
            new Vector3(-14f, 0f, -42f),
            new Vector3(-16f, 0f, 48f),
            new Vector3(-16f, 0f, 46f),
            new Vector3(-16f, 0f, -2f),
            new Vector3(-16f, 0f, -4f),
            new Vector3(-16f, 0f, -6f),
            new Vector3(-16f, 0f, -14f),
            new Vector3(-16f, 0f, -16f),
            new Vector3(-16f, 0f, -18f),
            new Vector3(-16f, 0f, -20f),
            new Vector3(-16f, 0f, -40f),
            new Vector3(-16f, 0f, -42f),
            new Vector3(-18f, 0f, 46f),
            new Vector3(-18f, 0f, 44f),
            new Vector3(-18f, 0f, -2f),
            new Vector3(-18f, 0f, -4f),
            new Vector3(-18f, 0f, -6f),
            new Vector3(-18f, 0f, -8f),
            new Vector3(-18f, 0f, -12f),
            new Vector3(-18f, 0f, -14f),
            new Vector3(-18f, 0f, -16f),
            new Vector3(-18f, 0f, -18f),
            new Vector3(-18f, 0f, -40f),
            new Vector3(-18f, 0f, -42f),
            new Vector3(-20f, 0f, 46f),
            new Vector3(-20f, 0f, 44f),
            new Vector3(-20f, 0f, 0f),
            new Vector3(-20f, 0f, -2f),
            new Vector3(-20f, 0f, -4f),
            new Vector3(-20f, 0f, -6f),
            new Vector3(-20f, 0f, -8f),
            new Vector3(-20f, 0f, -10f),
            new Vector3(-20f, 0f, -12f),
            new Vector3(-20f, 0f, -14f),
            new Vector3(-20f, 0f, -16f),
            new Vector3(-20f, 0f, -38f),
            new Vector3(-20f, 0f, -40f),
            new Vector3(-22f, 0f, 46f),
            new Vector3(-22f, 0f, 44f),
            new Vector3(-22f, 0f, 42f),
            new Vector3(-22f, 0f, 0f),
            new Vector3(-22f, 0f, -2f),
            new Vector3(-22f, 0f, -4f),
            new Vector3(-22f, 0f, -6f),
            new Vector3(-22f, 0f, -8f),
            new Vector3(-22f, 0f, -10f),
            new Vector3(-22f, 0f, -12f),
            new Vector3(-22f, 0f, -14f),
            new Vector3(-22f, 0f, -38f),
            new Vector3(-22f, 0f, -40f),
            new Vector3(-24f, 0f, 44f),
            new Vector3(-24f, 0f, 42f),
            new Vector3(-24f, 0f, 2f),
            new Vector3(-24f, 0f, 0f),
            new Vector3(-24f, 0f, -2f),
            new Vector3(-24f, 0f, -4f),
            new Vector3(-24f, 0f, -6f),
            new Vector3(-24f, 0f, -8f),
            new Vector3(-24f, 0f, -10f),
            new Vector3(-24f, 0f, -12f),
            new Vector3(-24f, 0f, -36f),
            new Vector3(-24f, 0f, -38f),
            new Vector3(-26f, 0f, 42f),
            new Vector3(-26f, 0f, 40f),
            new Vector3(-26f, 0f, 2f),
            new Vector3(-26f, 0f, 0f),
            new Vector3(-26f, 0f, -2f),
            new Vector3(-26f, 0f, -4f),
            new Vector3(-26f, 0f, -6f),
            new Vector3(-26f, 0f, -8f),
            new Vector3(-26f, 0f, -10f),
            new Vector3(-26f, 0f, -12f),
            new Vector3(-26f, 0f, -36f),
            new Vector3(-26f, 0f, -38f),
            new Vector3(-28f, 0f, 42f),
            new Vector3(-28f, 0f, 40f),
            new Vector3(-28f, 0f, 38f),
            new Vector3(-28f, 0f, 0f),
            new Vector3(-28f, 0f, -2f),
            new Vector3(-28f, 0f, -4f),
            new Vector3(-28f, 0f, -6f),
            new Vector3(-28f, 0f, -8f),
            new Vector3(-28f, 0f, -10f),
            new Vector3(-28f, 0f, -12f),
            new Vector3(-28f, 0f, -34f),
            new Vector3(-28f, 0f, -36f),
            new Vector3(-30f, 0f, 40f),
            new Vector3(-30f, 0f, 38f),
            new Vector3(-30f, 0f, -10f),
            new Vector3(-30f, 0f, -12f),
            new Vector3(-30f, 0f, -14f),
            new Vector3(-30f, 0f, -32f),
            new Vector3(-30f, 0f, -34f),
            new Vector3(-32f, 0f, 38f),
            new Vector3(-32f, 0f, 36f),
            new Vector3(-32f, 0f, -30f),
            new Vector3(-32f, 0f, -32f),
            new Vector3(-34f, 0f, 36f),
            new Vector3(-34f, 0f, 34f),
            new Vector3(-34f, 0f, -28f),
            new Vector3(-34f, 0f, -30f),
            new Vector3(-36f, 0f, 34f),
            new Vector3(-36f, 0f, 32f),
            new Vector3(-36f, 0f, 30f),
            new Vector3(-36f, 0f, -26f),
            new Vector3(-36f, 0f, -28f),
            new Vector3(-38f, 0f, 32f),
            new Vector3(-38f, 0f, 30f),
            new Vector3(-38f, 0f, 28f),
            new Vector3(-38f, 0f, -22f),
            new Vector3(-38f, 0f, -24f),
            new Vector3(-38f, 0f, -26f),
            new Vector3(-40f, 0f, 30f),
            new Vector3(-40f, 0f, 28f),
            new Vector3(-40f, 0f, 26f),
            new Vector3(-40f, 0f, 24f),
            new Vector3(-40f, 0f, -20f),
            new Vector3(-40f, 0f, -22f),
            new Vector3(-40f, 0f, -24f),
            new Vector3(-42f, 0f, 26f),
            new Vector3(-42f, 0f, 24f),
            new Vector3(-42f, 0f, 22f),
            new Vector3(-42f, 0f, 20f),
            new Vector3(-42f, 0f, -14f),
            new Vector3(-42f, 0f, -16f),
            new Vector3(-42f, 0f, -18f),
            new Vector3(-42f, 0f, -20f),
            new Vector3(-44f, 0f, 22f),
            new Vector3(-44f, 0f, 20f),
            new Vector3(-44f, 0f, 18f),
            new Vector3(-44f, 0f, 16f),
            new Vector3(-44f, 0f, 14f),
            new Vector3(-44f, 0f, -8f),
            new Vector3(-44f, 0f, -10f),
            new Vector3(-44f, 0f, -12f),
            new Vector3(-44f, 0f, -14f),
            new Vector3(-44f, 0f, -16f),
            new Vector3(-44f, 0f, -18f),
            new Vector3(-46f, 0f, 18f),
            new Vector3(-46f, 0f, 16f),
            new Vector3(-46f, 0f, 14f),
            new Vector3(-46f, 0f, 12f),
            new Vector3(-46f, 0f, 10f),
            new Vector3(-46f, 0f, 8f),
            new Vector3(-46f, 0f, 6f),
            new Vector3(-46f, 0f, 4f),
            new Vector3(-46f, 0f, 2f),
            new Vector3(-46f, 0f, 0f),
            new Vector3(-46f, 0f, -2f),
            new Vector3(-46f, 0f, -4f),
            new Vector3(-46f, 0f, -6f),
            new Vector3(-46f, 0f, -8f),
            new Vector3(-46f, 0f, -10f),
            new Vector3(-46f, 0f, -12f),
            new Vector3(-48f, 0f, 12f),
            new Vector3(-48f, 0f, 10f),
            new Vector3(-48f, 0f, 8f),
            new Vector3(-48f, 0f, 6f),
            new Vector3(-48f, 0f, 4f),
            new Vector3(-48f, 0f, 2f),
            new Vector3(-48f, 0f, 0f),
            new Vector3(-48f, 0f, -2f),
            new Vector3(-48f, 0f, -4f),
            new Vector3(-48f, 0f, -6)
        };

        internal HashSet<string> TrashList { get; } = new HashSet<string>
        {
            "harbor_dynamic_container",
            "minicopter.entity",
            "scraptransporthelicopter",
            "hotairballoon",
            "attackhelicopter.entity",
            "ch47.entity",
            "rowboat",
            "rhib",
            "tugboat",
            "kayak",
            "submarinesolo.entity",
            "submarineduo.entity",
            "snowmobile",
            "tomahasnowmobile",
            "sled.deployed",
            "magnetcrane.entity",
            "sedantest.entity",
            "2module_car_spawned.entity",
            "3module_car_spawned.entity",
            "4module_car_spawned.entity",
            "testridablehorse",
            "servergibs_bradley",
            "servergibs_patrolhelicopter",
            "servergibs_ch47",
            "servergibs_scraptransport",
            "loot_barrel_2",
            "loot-barrel-2",
            "oil_barrel",
            "loot_barrel_1",
            "shreddable_pickuptruck"
        };

        private ControllerHarborEvent Controller { get; set; } = null;
        private bool Active { get; set; } = false;

        private void StartTimer()
        {
            if (!_config.EnabledTimer) return;
            timer.In(UnityEngine.Random.Range(_config.MinStartTime, _config.MaxStartTime), () =>
            {
                if (!Active) Start(null);
                else Puts("This event is active now. To finish this event (harborstop), then to start the next one");
            });
        }

        private void Start(BasePlayer player)
        {
            if (!PluginExistsForStart("NpcSpawn")) return;
            CheckVersionPlugin();
            Active = true;
            AlertToAllPlayers("PreStart", _config.Chat.Prefix, GetTimeFormat((int)_config.PreStartTime));
            timer.In(_config.PreStartTime, () =>
            {
                Puts($"{Name} has begun");
                ToggleHooks(true);
                Controller = new GameObject().AddComponent<ControllerHarborEvent>();
                if (_config.RemoveBetterNpc && plugins.Exists("BetterNpc")) BetterNpc.Call("DestroyController", Controller.IsSmallHarbor ? "Small Harbor" : "Large Harbor");
                if (plugins.Exists("MonumentOwner")) MonumentOwner.Call("RemoveZone", Controller.Monument);
                Controller.EnablePveMode(_config.PveMode, player);
                Interface.Oxide.CallHook($"On{Name}Start", Controller.transform.position, _config.Radius);
                AlertToAllPlayers("Start", _config.Chat.Prefix, MapHelper.GridToString(MapHelper.PositionToGrid(Controller.transform.position)), _config.Cctv);
            });
        }

        private void Finish()
        {
            ToggleHooks(false);
            if (ActivePveMode) PveMode.Call("EventRemovePveMode", Name, true);
            if (Controller != null)
            {
                if (plugins.Exists("MonumentOwner")) MonumentOwner.Call("CreateZone", Controller.Monument);
                if (_config.RemoveBetterNpc && plugins.Exists("BetterNpc")) BetterNpc.Call("CreateController", Controller.IsSmallHarbor ? "Small Harbor" : "Large Harbor");
                EnableRadiation(Controller.Puzzle);
                UnityEngine.Object.Destroy(Controller.gameObject);
            }
            Active = false;
            SendBalance();
            LootableCrates.Clear();
            StartHackCrates.Clear();
            AlertToAllPlayers("Finish", _config.Chat.Prefix);
            Interface.Oxide.CallHook($"On{Name}End");
            Puts($"{Name} has ended");
            StartTimer();
        }

        internal class ControllerHarborEvent : FacepunchBehaviour
        {
            private PluginConfig _config => _ins._config;

            internal MonumentInfo Monument { get; set; } = null;
            internal bool IsSmallHarbor => Monument.name.Contains("harbor_1");
            internal int HarborIndex { get; set; } = 0;
            internal PuzzleReset Puzzle { get; set; } = null;

            private SphereCollider SphereCollider { get; set; } = null;

            private VendingMachineMapMarker VendingMarker { get; set; } = null;
            private HashSet<MapMarkerGenericRadius> Markers { get; } = new HashSet<MapMarkerGenericRadius>();

            internal HashSet<BaseEntity> Entities { get; } = new HashSet<BaseEntity>();
            internal PressButton Button { get; set; } = null;

            private HarborCraneContainerPickup Crane { get; set; } = null;
            private AnimationTransformCrane AnimationCrane { get; set; } = null;
            private Vector3 DefaultPosCrane { get; set; } = Vector3.zero;
            private Vector3 DropPosBradley { get; set; } = Vector3.zero;
            private Vector3 DropPosContainer { get; set; } = Vector3.zero;

            private HashSet<Door> ContainerDoors { get; } = new HashSet<Door>();
            private HashSet<BaseEntity> ContainerEntities { get; } = new HashSet<BaseEntity>();
            private HashSet<LootContainer> ContainerCrates { get; } = new HashSet<LootContainer>();
            internal HackableLockedCrate ContainerHackCrate { get; set; } = null;
            private Vector3 GetContainerHackCratePos => ContainerHackCrate.transform.position;
            private Vector3 GetContainerPos => GetContainerHackCratePos.WithY(GetContainerPosY);
            private float GetContainerPosY => GetContainerHackCratePos.y + 2.9f;

            private Coroutine CargoCoroutine { get; set; } = null;
            internal CargoShip Cargo { get; set; } = null;
            private int CargoTime { get; set; } = 5;
            private int CargoNpcCount { get; set; } = 13;

            internal BradleyAPC Bradley { get; set; } = null;
            private Coroutine BradleyCoroutine { get; set; } = null;
            private Dictionary<int, KeyValuePair<Vector3, bool>> PatrolPathBradleySmall { get; } = new Dictionary<int, KeyValuePair<Vector3, bool>>
            {
                [0] = new KeyValuePair<Vector3, bool>(new Vector3(-112f, 5f, -36.63f), true),
                [1] = new KeyValuePair<Vector3, bool>(new Vector3(-78.773f, 5f, -36.63f), false),
                [2] = new KeyValuePair<Vector3, bool>(new Vector3(-35.219f, 5f, -36.63f), false),
                [3] = new KeyValuePair<Vector3, bool>(new Vector3(17f, 5f, -36.63f), false),
                [4] = new KeyValuePair<Vector3, bool>(new Vector3(24.5f, 5f, -29f), false),
                [5] = new KeyValuePair<Vector3, bool>(new Vector3(32f, 5f, -21.62f), false),
                [6] = new KeyValuePair<Vector3, bool>(new Vector3(57.71f, 5f, -21.62f), false),
                [7] = new KeyValuePair<Vector3, bool>(new Vector3(85.509f, 5f, -21.62f), true),
                [8] = new KeyValuePair<Vector3, bool>(new Vector3(85.509f, 5f, -14f), false),
                [9] = new KeyValuePair<Vector3, bool>(new Vector3(85.509f, 5f, 10.19f), false),
                [10] = new KeyValuePair<Vector3, bool>(new Vector3(85.509f, 5f, 27.359f), true)
            };
            private Dictionary<int, KeyValuePair<Vector3, bool>> PatrolPathBradleyLarge { get; } = new Dictionary<int, KeyValuePair<Vector3, bool>>
            {
                [0] = new KeyValuePair<Vector3, bool>(new Vector3(12.869f, 5.083f, -85.55f), true),
                [1] = new KeyValuePair<Vector3, bool>(new Vector3(-9.321f, 5.083f, -85.55f), false),
                [2] = new KeyValuePair<Vector3, bool>(new Vector3(-30.612f, 5.083f, -85.55f), false),
                [3] = new KeyValuePair<Vector3, bool>(new Vector3(-58.019f, 5.083f, -85.55f), false),
                [4] = new KeyValuePair<Vector3, bool>(new Vector3(-81.578f, 4.885f, -85.494f), false),
                [5] = new KeyValuePair<Vector3, bool>(new Vector3(-81.578f, 4.885f, -57.483f), false),
                [6] = new KeyValuePair<Vector3, bool>(new Vector3(-81.578f, 4.885f, -32.998f), false),
                [7] = new KeyValuePair<Vector3, bool>(new Vector3(-74.063f, 4.885f, -25.504f), false),
                [8] = new KeyValuePair<Vector3, bool>(new Vector3(-66.646f, 4.885f, -25.504f), true),
                [9] = new KeyValuePair<Vector3, bool>(new Vector3(-33.637f, 4.885f, -25.504f), false),
                [10] = new KeyValuePair<Vector3, bool>(new Vector3(-2.082f, 4.885f, -25.613f), false),
                [11] = new KeyValuePair<Vector3, bool>(new Vector3(12.822f, 4.885f, -23.685f), false),
                [12] = new KeyValuePair<Vector3, bool>(new Vector3(33.915f, 4.885f, -25.613f), false),
                [13] = new KeyValuePair<Vector3, bool>(new Vector3(41.44f, 4.885f, -18.155f), false),
                [14] = new KeyValuePair<Vector3, bool>(new Vector3(41.44f, 4.884f, 9.11f), false),
                [15] = new KeyValuePair<Vector3, bool>(new Vector3(33.927f, 4.878f, 16.514f), false),
                [16] = new KeyValuePair<Vector3, bool>(new Vector3(-2.082f, 4.878f, 16.514f), false),
                [17] = new KeyValuePair<Vector3, bool>(new Vector3(-33.548f, 4.878f, 16.514f), false),
                [18] = new KeyValuePair<Vector3, bool>(new Vector3(-59.03f, 4.878f, 16.514f), false),
                [19] = new KeyValuePair<Vector3, bool>(new Vector3(-66.567f, 4.878f, 23.999f), false),
                [20] = new KeyValuePair<Vector3, bool>(new Vector3(-64.401f, 4.878f, 42.06f), false),
                [21] = new KeyValuePair<Vector3, bool>(new Vector3(-66.567f, 4.878f, 59.952f), false),
                [22] = new KeyValuePair<Vector3, bool>(new Vector3(-66.567f, 4.878f, 96.011f), true)
            };
            private HashSet<Vector3> CurrentPathBradleySmall { get; } = new HashSet<Vector3>
            {
                new Vector3(26.023f, 5f, 32.478f),
                new Vector3(29.882f, 5f, 32.478f),
                new Vector3(48.693f, 5f, 32.478f),
                new Vector3(49.629f, 5f, 27.236f),
                new Vector3(48.835f, 5f, 12.244f),
                new Vector3(45.333f, 5f, 7.495f),
                new Vector3(42.938f, 5f, -21.626f),
                new Vector3(31.932f, 5f, -21.626f),
                new Vector3(17.048f, 5f, -36.634f),
                new Vector3(-111.976f, 5f, -36.634f)
            };
            private HashSet<Vector3> CurrentPathBradleyLarge { get; } = new HashSet<Vector3>
            {
                new Vector3(97.594f, 4.804f, -76.257f),
                new Vector3(94.677f, 4.804f, -81.066f),
                new Vector3(92.111f, 5.063f, -85.488f),
                new Vector3(89.194f, 5.421f, -85.55f),
                new Vector3(73.917f, 5.421f, -85.55f),
                new Vector3(66.586f, 5.421f, -83.474f),
                new Vector3(59.29f, 5.421f, -85.55f),
                new Vector3(37.386f, 5.421f, -85.55f),
                new Vector3(32.398f, 5.083f, -85.55f),
                new Vector3(12.869f, 5.083f, -85.55f)
            };
            private AnimationTransformVehicle AnimationBradley { get; set; } = null;
            private Dictionary<Vector3, Vector3> AnimationPathBradley { get; } = new Dictionary<Vector3, Vector3>
            {
                [new Vector3(93.672f, 12.798f, -59.056f)] = new Vector3(0f, 167.284f, 0f),
                [new Vector3(93.965f, 12.127f, -60.276f)] = new Vector3(30.488f, 167.284f, 1.81f),
                [new Vector3(94.106f, 11.751f, -60.899f)] = new Vector3(30.488f, 167.284f, 1.81f),
                [new Vector3(94.546f, 10.572f, -62.852f)] = new Vector3(30.488f, 167.284f, 1.81f),
                [new Vector3(96.251f, 6.003f, -70.406f)] = new Vector3(30.488f, 167.284f, 1.81f),
                [new Vector3(96.495f, 5.351f, -71.486f)] = new Vector3(30.488f, 167.284f, 1.81f),
                [new Vector3(97.594f, 3.804f, -76.257f)] = new Vector3(0f, 167.284f, 0f)
            };
            internal Vector3 DestroyPositionBradley { get; set; } = Vector3.zero;

            internal PatrolHelicopter Helicopter { get; set; } = null;
            private bool UseDangerZones { get; set; } = true;

            internal HashSet<ScientistNPC> Scientists { get; } = new HashSet<ScientistNPC>();

            internal Dictionary<LootContainer, int> Crates { get; } = new Dictionary<LootContainer, int>();
            internal Dictionary<HackableLockedCrate, int> HackCrates { get; } = new Dictionary<HackableLockedCrate, int>();

            internal int TimeToFinish { get; set; } = _ins._config.FinishTime;

            internal HashSet<BasePlayer> Players { get; } = new HashSet<BasePlayer>();
            internal BasePlayer Owner { get; set; } = null;

            private void Awake()
            {
                FindMonument();
                if (Monument == null)
                {
                    _ins.PrintWarning("There is no Harbor monument anywhere on the map to be able to launch this event!");
                    _ins.Finish();
                    return;
                }
                HarborIndex = GetHarborIndex;

                UseDangerZones = PatrolHelicopterAI.use_danger_zones;

                transform.position = Monument.transform.position;
                transform.rotation = Monument.transform.rotation;

                Puzzle = GetPuzzleReset(Monument);
                DisableRadiation(Puzzle);

                gameObject.layer = 3;
                SphereCollider = gameObject.AddComponent<SphereCollider>();
                SphereCollider.isTrigger = true;
                SphereCollider.radius = _config.Radius;

                HashSet<PresetConfig> presets = IsSmallHarbor ? _config.NpcSmall : _config.NpcLarge;
                if (presets == null || presets.Count == 0)
                {
                    _ins.PrintWarning("HarborEvent: No NPC presets configured! Event NPCs will not spawn.");
                }
                else
                {
                    _ins.Puts($"HarborEvent: Starting to spawn {presets.Count} NPC preset(s) for {(IsSmallHarbor ? "Small" : "Large")} Harbor...");
                    foreach (PresetConfig preset in presets) SpawnPreset(preset);
                    _ins.Puts($"HarborEvent: NPC spawning complete. Total event NPCs: {Scientists.Count}");
                }

                InitCrane();

                if (!IsSmallHarbor) SpawnBridgeForBradley();

                CargoCoroutine = ServerMgr.Instance.StartCoroutine(CargoControl());

                SpawnMapMarker(_config.Marker);

                InvokeRepeating(InvokeUpdates, 0f, 1f);
            }

            private void OnDestroy()
            {
                if (CargoCoroutine != null) ServerMgr.Instance.StopCoroutine(CargoCoroutine);
                if (BradleyCoroutine != null) ServerMgr.Instance.StopCoroutine(BradleyCoroutine);

                CancelInvoke(InvokeUpdates);

                if (SphereCollider != null) Destroy(SphereCollider);

                if (VendingMarker.IsExists()) VendingMarker.Kill();
                foreach (MapMarkerGenericRadius marker in Markers) if (marker.IsExists()) marker.Kill();

                foreach (BasePlayer player in Players) CuiHelper.DestroyUi(player, "Tabs_KpucTaJl");

                if (Cargo.IsExists()) Cargo.Kill();

                PatrolHelicopterAI.use_danger_zones = UseDangerZones;
                if (Helicopter.IsExists()) Helicopter.Kill();

                if (AnimationBradley != null) Destroy(AnimationBradley);
                if (Bradley.IsExists()) Bradley.Kill();

                foreach (ScientistNPC npc in Scientists) if (npc.IsExists()) npc.Kill();

                foreach (KeyValuePair<LootContainer, int> dic in Crates) if (dic.Key.IsExists()) dic.Key.Kill();
                foreach (KeyValuePair<HackableLockedCrate, int> dic in HackCrates) if (dic.Key.IsExists()) dic.Key.Kill();

                if (AnimationCrane != null) Destroy(AnimationCrane);
                ReturnCranes();

                foreach (BaseEntity entity in Entities) if (entity.IsExists()) entity.Kill();
            }

            private void OnTriggerEnter(Collider other) => EnterPlayer(other.GetComponentInParent<BasePlayer>());

            internal void EnterPlayer(BasePlayer player)
            {
                if (!player.IsPlayer()) return;
                if (Players.Contains(player)) return;
                Players.Add(player);
                Interface.Oxide.CallHook($"OnPlayerEnter{_ins.Name}", player);
                if (_config.IsCreateZonePvp) _ins.AlertToPlayer(player, _ins.GetMessage("EnterPVP", player.UserIDString, _config.Chat.Prefix));
                if (_config.Gui.IsGui) UpdateGui(player);
            }

            private void OnTriggerExit(Collider other) => ExitPlayer(other.GetComponentInParent<BasePlayer>());

            internal void ExitPlayer(BasePlayer player)
            {
                if (!player.IsPlayer()) return;
                if (!Players.Contains(player)) return;
                Players.Remove(player);
                Interface.Oxide.CallHook($"OnPlayerExit{_ins.Name}", player);
                if (_config.IsCreateZonePvp) _ins.AlertToPlayer(player, _ins.GetMessage("ExitPVP", player.UserIDString, _config.Chat.Prefix));
                if (_config.Gui.IsGui) CuiHelper.DestroyUi(player, "Tabs_KpucTaJl");
            }

            private void InvokeUpdates()
            {
                if (_config.Gui.IsGui) foreach (BasePlayer player in Players) UpdateGui(player);
                if (_config.Marker.Enabled) UpdateVendingMarker();
                UpdateMarkerForPlayers();
                CheckTrashCargoShip();
                CheckTrashBradley();
                CheckMoveHelicopter();
                UpdateTimeToFinish();
            }

            private void UpdateGui(BasePlayer player)
            {
                Dictionary<string, string> dic = new Dictionary<string, string> { ["Clock_KpucTaJl"] = GetTimeFormat(TimeToFinish) };
                if (Scientists.Count > 0) dic.Add("Npc_KpucTaJl", Scientists.Count.ToString());
                if (Crates.Count + HackCrates.Count > 0) dic.Add("Crate_KpucTaJl", $"{Crates.Count + HackCrates.Count}");
                _ins.CreateTabs(player, dic);
            }

            private void SpawnMapMarker(MarkerConfig config)
            {
                if (!config.Enabled) return;

                MapMarkerGenericRadius background = GameManager.server.CreateEntity("assets/prefabs/tools/map/genericradiusmarker.prefab", transform.position) as MapMarkerGenericRadius;
                background.Spawn();
                background.radius = config.Type == 0 ? config.Radius : 0.37967f;
                background.alpha = config.Alpha;
                background.color1 = new Color(config.Color.R, config.Color.G, config.Color.B);
                background.color2 = new Color(config.Color.R, config.Color.G, config.Color.B);
                Markers.Add(background);

                if (config.Type == 1)
                {
                    foreach (Vector3 pos in _ins.Marker)
                    {
                        MapMarkerGenericRadius marker = GameManager.server.CreateEntity("assets/prefabs/tools/map/genericradiusmarker.prefab", transform.position + pos) as MapMarkerGenericRadius;
                        marker.Spawn();
                        marker.radius = 0.008f;
                        marker.alpha = 1f;
                        marker.color1 = new Color(config.Color.R, config.Color.G, config.Color.B);
                        marker.color2 = new Color(config.Color.R, config.Color.G, config.Color.B);
                        Markers.Add(marker);
                    }
                }

                VendingMarker = GameManager.server.CreateEntity("assets/prefabs/deployable/vendingmachine/vending_mapmarker.prefab", transform.position) as VendingMachineMapMarker;
                VendingMarker.Spawn();

                UpdateVendingMarker();
                UpdateMapMarkers();
            }

            private void UpdateVendingMarker()
            {
                VendingMarker.markerShopName = $"{_config.Marker.Text}\n{GetTimeFormat(TimeToFinish)}";
                if (_ins.ActivePveMode) VendingMarker.markerShopName += Owner == null ? "\nNo Owner" : $"\n{Owner.displayName}";
                VendingMarker.SendNetworkUpdate();
            }

            internal void UpdateMapMarkers() { foreach (MapMarkerGenericRadius marker in Markers) marker.SendUpdate(); }

            private void UpdateMarkerForPlayers()
            {
                if (Players.Count == 0) return;

                if (_config.MainPoint.Enabled)
                {
                    HashSet<Vector3> points = new HashSet<Vector3>();
                    if (Bradley.IsExists())
                    {
                        if (!Bradley.myRigidBody.isKinematic)
                            points.Add(Bradley.transform.position);
                    }
                    else
                    {
                        if (Button != null)
                            points.Add(Button.transform.position);
                    }
                    if (points.Count > 0) foreach (BasePlayer player in Players) foreach (Vector3 point in points) UpdateMarkerForPlayer(player, point, _config.MainPoint);
                    points = null;
                }

                if (_config.AdditionalPoint.Enabled && ContainerDoors.Count == 0 && Crates.Count + HackCrates.Count > 0)
                {
                    HashSet<Vector3> points = new HashSet<Vector3>();
                    foreach (KeyValuePair<LootContainer, int> dic in Crates) if (dic.Key.IsExists()) points.Add(dic.Key.transform.position);
                    foreach (KeyValuePair<HackableLockedCrate, int> dic in HackCrates) if (dic.Key.IsExists()) points.Add(dic.Key.transform.position);
                    if (points.Count > 0) foreach (BasePlayer player in Players) foreach (Vector3 point in points) UpdateMarkerForPlayer(player, point, _config.AdditionalPoint);
                    points = null;
                }
            }

            private void UpdateTimeToFinish()
            {
                if (CargoTime > 0 && Cargo.lootRoundsPassed >= CargoShip.loot_rounds) CargoTime--;

                if (CargoTime == 0 && Crates.Count + HackCrates.Count == 0 && TimeToFinish > _config.PreFinishTime) TimeToFinish = _config.PreFinishTime;
                else TimeToFinish--;

                if (TimeToFinish <= 120 && Cargo.HasFlag(BaseEntity.Flags.Reserved1))
                {
                    Button = null;
                    Cargo.PlayHorn();
                    Cargo.SetFlag(BaseEntity.Flags.Reserved1, false);
                    Cargo.SetFlag(BaseEntity.Flags.Reserved2, true);
                    Cargo.currentHarborApproachNode++;
                }

                if (TimeToFinish == _config.PreFinishTime) _ins.AlertToAllPlayers("PreFinish", _config.Chat.Prefix, GetTimeFormat(_config.PreFinishTime));
                else if (TimeToFinish == 0)
                {
                    CancelInvoke(InvokeUpdates);
                    _ins.Finish();
                }
            }

            private Vector3 GetGlobalPosition(Vector3 localPosition) => transform.TransformPoint(localPosition);

            private Quaternion GetGlobalRotation(Vector3 localRotation) => transform.rotation * Quaternion.Euler(localRotation);

            private static HashSet<T> GetEntities<T>(Vector3 position, float radius, int layerMask) where T : BaseEntity
            {
                HashSet<T> result = new HashSet<T>();
                List<T> list = Pool.Get<List<T>>();
                Vis.Entities<T>(position, radius, list, layerMask);
                foreach (T entity in list) result.Add(entity);
                Pool.FreeUnmanaged(ref list);
                return result;
            }

            private static void CheckTrash(Vector3 pos, float radius) { foreach (BaseEntity entity in GetEntities<BaseEntity>(pos, radius, -1)) if (_ins.TrashList.Contains(entity.ShortPrefabName) && entity.IsExists()) entity.Kill(); }

            private void FindMonument()
            {
                List<MonumentInfo> list = Pool.Get<List<MonumentInfo>>();

                foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
                    if (((monument.name.Contains("harbor_1") && _config.IsSmallHarbor) || (monument.name.Contains("harbor_2") && _config.IsLargeHarbor)) && IsFreeHarbor(monument))
                        list.Add(monument);

                Monument = list.Count > 0 ? list.GetRandom() : null;
                Pool.FreeUnmanaged(ref list);
            }

            private bool IsFreeHarbor(MonumentInfo monument) => GetEntities<CargoShip>(monument.transform.position, _config.Radius, 1 << 27).Count == 0;

            private IEnumerator CargoControl()
            {
                SpawnCargoShip();
                SpawnContainer();
                SpawnBradley();

                while (!Cargo.HasFlag(BaseEntity.Flags.Reserved1)) yield return CoroutineEx.waitForSeconds(1f);

                ReturnCranes();
                Cargo.CancelInvoke(Cargo.GetPrivateAction("LeaveHarbor"));

                if (_config.Bradley.IsBradley)
                {
                    Bradley.SetParent(null, true, true);
                    CraneGoToBradley();
                }

                UpdateContainerEntities();
            }

            private static void SetToDefaultCrane(HarborCraneContainerPickup crane)
            {
                Vector3 pos = crane.transform.position;
                crane.transform.rotation = Quaternion.LookRotation(((pos + (Vector3)crane.GetPrivateFieldValue("startForward") * 26f).WithY(pos.y) - pos).normalized, Vector3.up);
                crane.ServerTowerGrab.localPosition = new Vector3(0f, 18.154f, 26f);
                crane.CraneGrab.localPosition = crane.ServerTowerGrab.localPosition + new Vector3(0f, 19f, 0f);
                crane.SendNetworkUpdate();
            }

            private void InitCrane()
            {
                Crane = HarborCraneContainerPickup.AllCranes.Min(x => Vector3.Distance(x.transform.position, transform.position));
                HarborCraneContainerPickup.AllCranes.Remove(Crane);

                SetToDefaultCrane(Crane);

                AnimationCrane = Crane.gameObject.AddComponent<AnimationTransformCrane>();

                DefaultPosCrane = IsSmallHarbor ? GetGlobalPosition(new Vector3(10.3f, 22.452f, 39.875f)) : GetGlobalPosition(new Vector3(90.63f, 22.15f, -3.39f));
                DropPosBradley = IsSmallHarbor ? GetGlobalPosition(new Vector3(26.023f, 4.325f, 38.543f)) : GetGlobalPosition(new Vector3(93.071f, 12.798f, -56.393f));
                DropPosContainer = IsSmallHarbor ? DropPosBradley : GetGlobalPosition(new Vector3(91.637f, 4f, -15.827f));

                SpawnSubEntitiesCrane();
            }

            private void ReturnCranes()
            {
                if (!HarborCraneContainerPickup.AllCranes.Contains(Crane)) HarborCraneContainerPickup.AllCranes.Add(Crane);
                SetToDefaultCrane(Crane);
            }

            private void CraneGoToBradley()
            {
                AnimationCrane.AddPath(new HashSet<Vector3>
                {
                    Crane.ServerTowerGrab.position.WithY(GetContainerPosY + 3f),
                    Bradley.transform.position.WithY(GetContainerPosY + 3f),
                    Bradley.transform.position.WithY(Bradley.transform.position.y + 1.25f)
                }, 2f, null, 0f, Vector3.zero);
            }

            private void CraneAndBradleyGoToCargoDropPos()
            {
                Vector3 dropPos = DropPosBradley.WithY(DropPosBradley.y + 1.25f);
                AnimationCrane.AddPath(new HashSet<Vector3>
                {
                    Bradley.transform.position.WithY(GetContainerPosY + 3f),
                    IsSmallHarbor ? (Crane.transform.position + (DropPosBradley.WithY(Crane.transform.position.y) - Crane.transform.position).normalized * Vector3.Distance(Crane.transform.position, Bradley.transform.position.WithY(Crane.transform.position.y))).WithY(GetContainerPosY + 3f) : dropPos.WithY(GetContainerPosY + 3f),
                    dropPos
                }, 1.5f, Bradley, 1.25f, IsSmallHarbor ? GetGlobalRotation(new Vector3(0f, 180f, 0f)).eulerAngles : GetGlobalRotation(new Vector3(0f, 167.284f, 0f)).eulerAngles);
            }

            private void CraneGoToDefaultPos()
            {
                AnimationCrane.AddPath(new HashSet<Vector3>
                {
                    DefaultPosCrane
                }, 2f, null, 0f, Vector3.zero);
            }

            internal void CraneGoToContainer()
            {
                AnimationCrane.AddPath(new HashSet<Vector3>
                {
                    Crane.ServerTowerGrab.position.WithY(GetContainerPosY + 3f),
                    GetContainerPos.WithY(GetContainerPosY + 3f),
                    GetContainerPos
                }, 2f, null, 0f, Vector3.zero);
            }

            private void CraneAndContainerGoToCargoDropPos()
            {
                Vector3 dropPos = DropPosContainer.WithY(DropPosContainer.y + 3f);
                AnimationCrane.AddPath(new HashSet<Vector3>
                {
                    GetContainerPos.WithY(GetContainerPosY + 3f),
                    dropPos.WithY(GetContainerPosY + 3f),
                    dropPos
                }, 1f, ContainerHackCrate, 2.9f, IsSmallHarbor ? GetGlobalRotation(new Vector3(0f, 180f, 0f)).eulerAngles : GetGlobalRotation(new Vector3(0f, 90f, 0f)).eulerAngles);
            }

            internal void FinishPathCrane()
            {
                if (Bradley != null && AnimationCrane.Cargo == Bradley)
                {
                    if (IsSmallHarbor) BradleyStart();
                    else
                    {
                        HashSet<PointAnimationTransform> path = new HashSet<PointAnimationTransform>();
                        foreach (KeyValuePair<Vector3, Vector3> dic in AnimationPathBradley)
                        {
                            path.Add(new PointAnimationTransform
                            {
                                Pos = GetGlobalPosition(dic.Key),
                                Rot = GetGlobalRotation(dic.Value).eulerAngles
                            });
                        }
                        AnimationBradley = Bradley.gameObject.AddComponent<AnimationTransformVehicle>();
                        AnimationBradley.AddPath(path, 2f);
                    }
                    CraneGoToDefaultPos();
                    return;
                }

                if (ContainerHackCrate != null && AnimationCrane.Cargo == ContainerHackCrate)
                {
                    foreach (BaseEntity entity in ContainerEntities) entity.SetParent(null, true, true);
                    foreach (LootContainer crate in ContainerCrates)
                    {
                        crate.lifestate = BaseCombatEntity.LifeState.Alive;
                        crate.SetParent(null, true, true);
                    }
                    UpdateContainerEntities();
                    ContainerEntities.Clear();
                    ContainerCrates.Clear();
                    Invoke(() =>
                    {
                        foreach (Door door in ContainerDoors) door.SetOpen(true);
                        ContainerDoors.Clear();
                    }, 1f);
                    CraneGoToDefaultPos();
                    return;
                }

                if (AnimationCrane.Cargo == null)
                {
                    if (ContainerDoors.Count == 0)
                    {
                        if (AnimationCrane != null) Destroy(AnimationCrane);
                        SetToDefaultCrane(Crane);
                        return;
                    }

                    if (Bradley != null && Bradley.myRigidBody.isKinematic)
                    {
                        CheckTrash(DropPosBradley, 5f);
                        CraneAndBradleyGoToCargoDropPos();
                        return;
                    }

                    if (ContainerHackCrate != null && Vector3.Distance(Crane.ServerTowerGrab.position, GetContainerPos) < 1f)
                    {
                        CheckTrash(DropPosContainer, 5f);
                        foreach (BaseEntity entity in ContainerEntities)
                        {
                            if (entity is BuildingBlock buildingBlock)
                            {
                                buildingBlock.SetConditionalModel(1);
                                buildingBlock.SendNetworkUpdate();
                            }
                        }
                        CraneAndContainerGoToCargoDropPos();
                        return;
                    }
                }
            }

            private void SpawnCargoShip()
            {
                CheckHarborApproachNodes();

                List<BasePathNode> nodes = CargoShip.harbors[HarborIndex].harborPath.nodes;

                Cargo = GameManager.server.CreateEntity("assets/content/vehicles/boats/cargoship/cargoshiptest.prefab", nodes[0].Position, Quaternion.LookRotation(nodes[1].Position - nodes[0].Position)) as CargoShip;

                Cargo.enableSaving = false;
                Cargo.Spawn();

                Cargo.SetPrivateFieldValue("layoutChoice", (uint)0);
                Cargo.SendNetworkUpdate();
                Cargo.RefreshActiveLayout();

                Cargo.skinID = 81182151852251420;

                foreach (BaseEntity entity in Cargo.children)
                {
                    if (entity == null) continue;
                    entity.EnableSaving(false);
                    foreach (BaseEntity children in entity.children)
                    {
                        if (children == null) continue;
                        children.EnableSaving(false);
                    }
                }

                Cargo.harborIndex = HarborIndex;
                Cargo.harborApproachPath = CargoShip.harbors[HarborIndex].harborPath;
                Cargo.proxManager = Cargo.harborApproachPath.GetComponent<HarborProximityManager>();
                Cargo.StartHarborApproach(null);
            }

            internal int GetHarborIndex
            {
                get
                {
                    for (int i = 0; i < CargoShip.harbors.Count; i++)
                        if (Vector3.Distance(Monument.transform.position, CargoShip.harbors[i].harborPath.nodes[5].Position) < _config.Radius)
                            return i;
                    return 0;
                }
            }

            private static void CheckHarborApproachNodes()
            {
                if (CargoShip.hasCalculatedApproaches) return;
                CargoShip.hasCalculatedApproaches = true;

                for (int i = 0; i < CargoShip.harbors.Count; i++)
                {
                    CargoShip.HarborInfo item = CargoShip.harbors[i];
                    float single = float.MaxValue;
                    int num = -1;

                    for (int j = 0; j < TerrainMeta.Path.OceanPatrolFar.Count; j++)
                    {
                        Vector3 vector3 = TerrainMeta.Path.OceanPatrolFar[j];
                        Vector3 position = item.harborPath.nodes[0].Position;
                        float distance = Vector3.Distance(vector3, position);
                        Vector3 vector31 = Vector3.up * 3f;

                        if (!GamePhysics.LineOfSightRadius(vector3 + vector31, position + vector31, 1084293377, 3f)) distance *= 20f;

                        if (distance < single)
                        {
                            single = distance;
                            num = j;
                        }
                    }

                    if (num == -1) return;

                    item.approachNode = num;
                    CargoShip.harbors[i] = item;
                }
            }

            private void CheckTrashCargoShip()
            {
                if (!Cargo.IsExists() || Cargo.HasFlag(BaseEntity.Flags.Reserved1)) return;
                Vector3 pos = Cargo.transform.TransformPoint(new Vector3(0f, 0f, 85f));
                CheckTrash(pos, 15f);
            }

            private void SpawnContainer()
            {
                ContainerHackCrate = GameManager.server.CreateEntity("assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab") as HackableLockedCrate;

                ContainerHackCrate.transform.localPosition = IsSmallHarbor ? new Vector3(-6f, 18.6f, -16.5f) : new Vector3(6f, 18.6f, -16.5f);
                ContainerHackCrate.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
                ContainerHackCrate.SetParent(Cargo);

                ContainerHackCrate.enableSaving = false;
                ContainerHackCrate.Spawn();

                ContainerHackCrate.GetComponent<Rigidbody>().isKinematic = true;

                SetHackCrate(ContainerHackCrate, _config.ContainerHackCrates);

                foreach (Prefab prefab in _ins.ContainerEntities)
                {
                    BaseEntity entity = GameManager.server.CreateEntity(prefab.Path);

                    entity.transform.localPosition = prefab.Pos;
                    entity.transform.localRotation = Quaternion.Euler(prefab.Rot);
                    entity.SetParent(ContainerHackCrate);

                    entity.enableSaving = false;
                    entity.Spawn();

                    if (entity is BuildingBlock buildingBlock)
                    {
                        buildingBlock.lifestate = BaseCombatEntity.LifeState.Dead;
                        buildingBlock.grounded = true;

                        buildingBlock.ChangeGradeAndSkin(BuildingGrade.Enum.Metal, 10221);
                        buildingBlock.SetCustomColour(5);

                        buildingBlock.SetConditionalModel(1);
                        buildingBlock.SendNetworkUpdate();
                    }

                    if (entity is Door door)
                    {
                        door.lifestate = BaseCombatEntity.LifeState.Dead;
                        door.grounded = true;
                        door.pickup.enabled = false;

                        door.canTakeLock = false;
                        door.canTakeCloser = false;
                        door.canTakeKnocker = false;

                        door.canNpcOpen = false;
                        door.canHandOpen = false;
                        door.hasHatch = false;

                        ContainerDoors.Add(door);
                    }

                    if (entity is CCTV_RC cctv)
                    {
                        cctv.UpdateFromInput(5, 0);
                        cctv.rcIdentifier = _config.Cctv;
                    }

                    ContainerEntities.Add(entity);
                    Entities.Add(entity);
                }

                foreach (Prefab prefab in _ins.ContainerCrates)
                {
                    LootContainer crate = GameManager.server.CreateEntity(prefab.Path) as LootContainer;

                    crate.transform.localPosition = prefab.Pos;
                    crate.transform.localRotation = Quaternion.Euler(prefab.Rot);
                    crate.SetParent(ContainerHackCrate);

                    crate.enableSaving = false;
                    crate.Spawn();

                    crate.lifestate = BaseCombatEntity.LifeState.Dead;

                    SetCrate(crate, _config.ContainerCrates.FirstOrDefault(x => x.Prefab == prefab.Path));
                    ContainerCrates.Add(crate);
                }
            }

            private void UpdateContainerEntities()
            {
                foreach (BaseEntity entity in ContainerEntities)
                {
                    entity.limitNetworking = true;
                    entity.limitNetworking = false;
                }
                foreach (LootContainer entity in ContainerCrates)
                {
                    entity.limitNetworking = true;
                    entity.limitNetworking = false;
                }
                ContainerHackCrate.limitNetworking = true;
                ContainerHackCrate.limitNetworking = false;
            }

            private void SpawnBridgeForBradley()
            {
                foreach (Prefab prefab in _ins.Bridge)
                {
                    BuildingBlock entity = GameManager.server.CreateEntity(prefab.Path, GetGlobalPosition(prefab.Pos), GetGlobalRotation(prefab.Rot)) as BuildingBlock;
                    entity.enableSaving = false;
                    entity.Spawn();
                    entity.grounded = true;
                    entity.ChangeGradeAndSkin(BuildingGrade.Enum.Metal, 10221);
                    entity.SetCustomColour(10);
                    Entities.Add(entity);
                }
            }

            private void SpawnBradley()
            {
                if (!_config.Bradley.IsBradley) return;

                Bradley = GameManager.server.CreateEntity("assets/prefabs/npc/m2bradley/bradleyapc.prefab") as BradleyAPC;

                Bradley.transform.localPosition = IsSmallHarbor ? new Vector3(-9.32f, 6.5f, -29.446f) : new Vector3(9.32f, 6.5f, -29.446f);
                Bradley.transform.localRotation = Quaternion.Euler(new Vector3(0f, 0f, 0f));
                Bradley.SetParent(Cargo);

                Bradley.enableSaving = false;
                Bradley.Spawn();

                foreach (BradleyAPC.ScientistSpawnGroup spawnGroup in Bradley.ScientistSpawns) spawnGroup.Spawned = true;

                Bradley.skinID = 81182151852251420;

                Bradley.patrolPath = null;
                Bradley.currentPath.Clear();
                Bradley.currentPathIndex = -1;

                Bradley._maxHealth = _config.Bradley.Hp;
                Bradley.health = Bradley._maxHealth;

                Bradley.maxCratesToSpawn = _config.Bradley.CountCrates;

                Bradley.viewDistance = _config.Bradley.ViewDistance;
                Bradley.searchRange = _config.Bradley.SearchRange;

                Bradley.coaxAimCone *= _config.Bradley.CoaxAimCone;
                Bradley.coaxFireRate *= _config.Bradley.CoaxFireRate;
                Bradley.coaxBurstLength = _config.Bradley.CoaxBurstLength;

                Bradley.nextFireTime = _config.Bradley.NextFireTime;
                Bradley.topTurretFireRate = _config.Bradley.TopTurretFireRate;

                Bradley.memoryDuration = _config.Bradley.MemoryDuration;

                Bradley.myRigidBody.isKinematic = true;
                Bradley.myRigidBody.useGravity = false;
                Bradley.myRigidBody.detectCollisions = false;
            }

            private void InstallCurrentPathBradley()
            {
                foreach (Vector3 pos in IsSmallHarbor ? CurrentPathBradleySmall : CurrentPathBradleyLarge) Bradley.currentPath.Add(GetGlobalPosition(pos));
                Bradley.currentPathIndex = 0;
                Bradley.pathLooping = false;
            }

            private void InstallPatrolPathBradley(Dictionary<int, KeyValuePair<Vector3, bool>> path)
            {
                RuntimePath runtimePath = new RuntimePath { Nodes = new IAIPathNode[path.Count] };

                foreach (KeyValuePair<int, KeyValuePair<Vector3, bool>> dic in path)
                {
                    Vector3 pos = GetGlobalPosition(dic.Value.Key);

                    IAIPathNode runtimePathNode = new RuntimePathNode(pos);
                    runtimePath.Nodes[dic.Key] = runtimePathNode;

                    if (dic.Key > 0)
                    {
                        IAIPathNode aIPathNode = runtimePath.Nodes[dic.Key - 1];
                        runtimePathNode.AddLink(aIPathNode);
                        aIPathNode.AddLink(runtimePathNode);
                    }

                    if (dic.Value.Value) runtimePath.AddInterestNode(new RuntimeInterestNode(pos));
                }

                Bradley.InstallPatrolPath(runtimePath);
            }

            internal void BradleyStart()
            {
                if (AnimationBradley != null) Destroy(AnimationBradley);
                Bradley.myRigidBody.detectCollisions = true;
                Bradley.myRigidBody.useGravity = true;
                Bradley.myRigidBody.isKinematic = false;
                BradleyCoroutine = ServerMgr.Instance.StartCoroutine(BradleyControl());
            }

            private IEnumerator BradleyControl()
            {
                InstallCurrentPathBradley();

                Vector3 endPos = Bradley.currentPath[Bradley.currentPath.Count - 1];

                while (Bradley.IsExists() && Vector3.Distance(Bradley.transform.position, endPos) > 4f) yield return CoroutineEx.waitForSeconds(1f);

                if (!Bradley.IsExists()) yield break;

                Bradley.currentPath.Clear();
                Bradley.currentPathIndex = -1;
                Bradley.pathLooping = true;
                InstallPatrolPathBradley(IsSmallHarbor ? PatrolPathBradleySmall : PatrolPathBradleyLarge);
            }

            private void CheckTrashBradley()
            {
                if (!Bradley.IsExists() || Bradley.myRigidBody.isKinematic) return;
                Vector3 pos = Bradley.transform.TransformPoint(new Vector3(0f, 1f, 5f));
                CheckTrash(pos, 2f);
            }

            private void SpawnSubEntitiesCrane()
            {
                Button = GameManager.server.CreateEntity("assets/prefabs/deployable/playerioents/button/button.prefab") as PressButton;

                Button.transform.localPosition = new Vector3(2.036f, 16.318f, 1.726f);
                Button.transform.localRotation = Quaternion.Euler(new Vector3(0f, 180f, 0f));
                Button.SetParent(Crane);

                GroundWatch groundWatch1 = Button.GetComponent<GroundWatch>();
                if (groundWatch1 != null) DestroyImmediate(groundWatch1);

                DestroyOnGroundMissing destroyOnGroundMissing1 = Button.GetComponent<DestroyOnGroundMissing>();
                if (destroyOnGroundMissing1 != null) DestroyImmediate(destroyOnGroundMissing1);

                Button.enableSaving = false;
                Button.Spawn();

                Button.pickup.enabled = false;

                Entities.Add(Button);

                if (!IsSmallHarbor)
                {
                    Barricade barricade = GameManager.server.CreateEntity("assets/prefabs/deployable/barricades/barricade.stone.prefab", GetGlobalPosition(new Vector3(90.396f, 4f, -35.348f)), GetGlobalRotation(new Vector3(0f, 0f, 0f))) as Barricade;

                    GroundWatch groundWatch2 = barricade.GetComponent<GroundWatch>();
                    if (groundWatch2 != null) DestroyImmediate(groundWatch2);

                    DestroyOnGroundMissing destroyOnGroundMissing2 = barricade.GetComponent<DestroyOnGroundMissing>();
                    if (destroyOnGroundMissing2 != null) DestroyImmediate(destroyOnGroundMissing2);

                    barricade.enableSaving = false;
                    barricade.Spawn();

                    barricade.pickup.enabled = false;

                    if (barricade.NpcTriggerBox != null) DestroyImmediate(barricade.NpcTriggerBox);

                    Entities.Add(barricade);
                }
            }

            internal void SpawnCargoCrate(LootContainer crate)
            {
                if (Cargo == null || crate.GetParentEntity() != Cargo) return;
                if (crate is HackableLockedCrate hackCrate)
                {
                    if (HackCrates.ContainsKey(hackCrate)) return;
                    SetHackCrate(hackCrate, _config.CargoHackCrates);
                }
                else
                {
                    if (Crates.ContainsKey(crate)) return;
                    SetCrate(crate, _config.CargoCrates.FirstOrDefault(x => x.Prefab == crate.PrefabName));
                }
                if (_ins.ActivePveMode) _ins.PveMode.Call("EventAddCrates", _ins.Name, new HashSet<ulong> { crate.net.ID.Value });
            }

            private void SetHackCrate(HackableLockedCrate crate, HackCrateConfig config)
            {
                HackCrates.Add(crate, config.TypeLootTable);

                crate.hackSeconds = HackableLockedCrate.requiredHackSeconds - config.UnlockTime;

                crate.shouldDecay = false;
                crate.CancelInvoke(crate.DelayedDestroy);

                crate.KillMapMarker();

                if (config.TypeLootTable is 1 or 4 or 5)
                {
                    crate.inventory.ClearItemsContainer();
                    if (config.TypeLootTable is 4 or 5) _ins.AddToContainerPrefab(crate.inventory, config.PrefabLootTable);
                    if (config.TypeLootTable is 1 or 5) _ins.AddToContainerItem(crate.inventory, config.OwnLootTable);
                }
            }

            private void SetCrate(LootContainer crate, CrateConfig config)
            {
                Crates.Add(crate, config.TypeLootTable);
                if (config.TypeLootTable == 1 || config.TypeLootTable == 4 || config.TypeLootTable == 5)
                {
                    _ins.NextTick(() =>
                    {
                        crate.inventory.ClearItemsContainer();
                        if (config.TypeLootTable == 4 || config.TypeLootTable == 5) _ins.AddToContainerPrefab(crate.inventory, config.PrefabLootTable);
                        if (config.TypeLootTable == 1 || config.TypeLootTable == 5) _ins.AddToContainerItem(crate.inventory, config.OwnLootTable);
                    });
                }
            }

            private void SpawnPreset(PresetConfig preset)
            {
                if (preset == null)
                {
                    _ins.PrintError("SpawnPreset: preset is null!");
                    return;
                }

                if (preset.Config == null)
                {
                    _ins.PrintError($"SpawnPreset: preset.Config is null for preset!");
                    return;
                }

                if (_ins.NpcSpawn == null)
                {
                    _ins.PrintError("SpawnPreset: NpcSpawn plugin reference is null! Cannot spawn NPCs.");
                    return;
                }

                if (!_ins.plugins.Exists("NpcSpawn"))
                {
                    _ins.PrintError("SpawnPreset: NpcSpawn plugin is not loaded! Cannot spawn NPCs.");
                    return;
                }

                if (preset.Positions == null || preset.Positions.Count == 0)
                {
                    _ins.PrintError($"SpawnPreset: No positions configured for preset '{preset.Config.Name}'!");
                    return;
                }

                int count = UnityEngine.Random.Range(preset.Min, preset.Max + 1);
                _ins.Puts($"SpawnPreset: Attempting to spawn {count} NPCs of type '{preset.Config.Name}' at {preset.Positions.Count} positions");

                List<Vector3> positions = Pool.Get<List<Vector3>>();
                int validPositions = 0;
                foreach (string posStr in preset.Positions)
                {
                    try
                    {
                        Vector3 localPos = posStr.ToVector3();
                        Vector3 globalPos = GetGlobalPosition(localPos);
                        positions.Add(globalPos);
                        validPositions++;
                    }
                    catch (Exception ex)
                    {
                        _ins.PrintError($"SpawnPreset: Failed to parse position '{posStr}' for preset '{preset.Config.Name}': {ex.Message}");
                    }
                }

                if (positions.Count == 0)
                {
                    _ins.PrintError($"SpawnPreset: No valid positions found for preset '{preset.Config.Name}'!");
                    Pool.FreeUnmanaged(ref positions);
                    return;
                }

                if (validPositions < count)
                {
                    _ins.PrintWarning($"SpawnPreset: Only {validPositions} valid positions available, but trying to spawn {count} NPCs for preset '{preset.Config.Name}'");
                    count = validPositions;
                }

                object config = GetObjectConfig(preset.Config);
                if (config == null)
                {
                    _ins.PrintError($"SpawnPreset: Failed to create config object for preset '{preset.Config.Name}'!");
                    Pool.FreeUnmanaged(ref positions);
                    return;
                }

                int spawnedCount = 0;
                int failedCount = 0;

                for (int i = 0; i < count; i++)
                {
                    if (positions.Count == 0)
                    {
                        _ins.PrintWarning($"SpawnPreset: No more positions available for preset '{preset.Config.Name}' (spawned {spawnedCount}/{count})");
                        break;
                    }

                    Vector3 pos = positions.GetRandom();
                    positions.Remove(pos);

                    try
                    {
                        object spawnResult = _ins.NpcSpawn?.Call("SpawnNpc", pos, config);
                        if (spawnResult == null)
                        {
                            _ins.PrintError($"SpawnPreset: NpcSpawn.Call('SpawnNpc') returned null for preset '{preset.Config.Name}' at position {pos}. Check if NpcSpawn plugin is loaded and working correctly.");
                            failedCount++;
                            continue;
                        }

                        ScientistNPC npc = spawnResult as ScientistNPC;
                        if (npc == null)
                        {
                            _ins.PrintError($"SpawnPreset: NpcSpawn.Call('SpawnNpc') returned unexpected type '{spawnResult.GetType().Name}' for preset '{preset.Config.Name}' at position {pos}");
                            failedCount++;
                            continue;
                        }

                        if (!npc.IsExists())
                        {
                            _ins.PrintError($"SpawnPreset: NPC spawned but IsExists() returned false for preset '{preset.Config.Name}' at position {pos}");
                            failedCount++;
                            continue;
                        }

                        Scientists.Add(npc);
                        spawnedCount++;
                    }
                    catch (Exception ex)
                    {
                        _ins.PrintError($"SpawnPreset: Exception while spawning NPC for preset '{preset.Config.Name}' at position {pos}: {ex.Message}\n{ex.StackTrace}");
                        failedCount++;
                    }
                }

                _ins.Puts($"SpawnPreset: Successfully spawned {spawnedCount} NPCs of type '{preset.Config.Name}' ({failedCount} failed)");
                if (failedCount > 0)
                {
                    _ins.PrintWarning($"SpawnPreset: {failedCount} NPCs failed to spawn for preset '{preset.Config.Name}'. Check NpcSpawn plugin configuration and server logs.");
                }

                Pool.FreeUnmanaged(ref positions);
            }

            private static object GetObjectConfig(NpcConfig config)
            {
                if (config == null || _ins.NpcSpawn == null) return null;

                HashSet<string> states = config.Stationary ? new HashSet<string> { "IdleState", "CombatStationaryState" } : new HashSet<string> { "RoamState", "ChaseState", "CombatState" };
                if (config.BeltItems != null && config.BeltItems.Any(x => x.ShortName == "rocket.launcher" || x.ShortName == "explosive.timed")) states.Add("RaidState");

                // Use reflection to access NpcSpawn.NpcConfig (nested public class)
                var npcSpawnType = _ins.NpcSpawn?.GetType();
                if (npcSpawnType == null)
                {
                    _ins.PrintError("GetObjectConfig: NpcSpawn plugin type is null!");
                    return null;
                }

                var npcConfigType = npcSpawnType.GetNestedType("NpcConfig", BindingFlags.Public);
                if (npcConfigType == null)
                {
                    _ins.PrintError("GetObjectConfig: Failed to find NpcSpawn.NpcConfig type. Make sure NpcSpawn plugin is loaded.");
                    return null;
                }

                var npcSpawnConfig = Activator.CreateInstance(npcConfigType);

                // Get NpcSpawn's NpcWear and NpcBelt types for conversion
                var npcWearType = npcSpawnType.GetNestedType("NpcWear", BindingFlags.Public);
                var npcBeltType = npcSpawnType.GetNestedType("NpcBelt", BindingFlags.Public);

                if (npcWearType == null || npcBeltType == null)
                {
                    _ins.PrintError("GetObjectConfig: Failed to find NpcSpawn.NpcWear or NpcBelt types.");
                    return null;
                }

                // Convert WearItems from HarborEvent.NpcWear to NpcSpawn.NpcWear
                // Always create HashSet (even if empty) to ensure UpdateInventory() has a valid collection
                object convertedWearItems = null;
                if (npcWearType != null)
                {
                    var hashSetType = typeof(HashSet<>).MakeGenericType(npcWearType);
                    convertedWearItems = Activator.CreateInstance(hashSetType);
                    
                    if (config.WearItems != null && config.WearItems.Count > 0)
                    {
                        var addMethod = hashSetType.GetMethod("Add");
                        foreach (var item in config.WearItems)
                        {
                            if (item == null) continue;
                            // Skip items with empty or null ShortName (NpcSpawn cannot create items without ShortName)
                            if (string.IsNullOrWhiteSpace(item.ShortName)) continue;
                            var npcWear = Activator.CreateInstance(npcWearType);
                            // NpcSpawn uses fields, not properties
                            SetField(npcWearType, npcWear, "ShortName", item.ShortName);
                            SetField(npcWearType, npcWear, "SkinID", item.SkinId);
                            addMethod.Invoke(convertedWearItems, new[] { npcWear });
                        }
                    }
                }

                // Convert BeltItems from HarborEvent.NpcBelt to NpcSpawn.NpcBelt
                // Always create HashSet (even if empty) to ensure UpdateInventory() has a valid collection
                object convertedBeltItems = null;
                if (npcBeltType != null)
                {
                    var hashSetType = typeof(HashSet<>).MakeGenericType(npcBeltType);
                    convertedBeltItems = Activator.CreateInstance(hashSetType);
                    
                    if (config.BeltItems != null && config.BeltItems.Count > 0)
                    {
                        var addMethod = hashSetType.GetMethod("Add");
                        foreach (var item in config.BeltItems)
                        {
                            if (item == null) continue;
                            // Skip items with empty or null ShortName (NpcSpawn cannot create items without ShortName)
                            if (string.IsNullOrWhiteSpace(item.ShortName)) continue;
                            var npcBelt = Activator.CreateInstance(npcBeltType);
                            // NpcSpawn uses fields, not properties
                            SetField(npcBeltType, npcBelt, "ShortName", item.ShortName);
                            SetField(npcBeltType, npcBelt, "Amount", item.Amount);
                            SetField(npcBeltType, npcBelt, "SkinID", item.SkinId);
                            SetField(npcBeltType, npcBelt, "Mods", item.Mods != null ? new HashSet<string>(item.Mods) : new HashSet<string>());
                            SetField(npcBeltType, npcBelt, "Ammo", item.Ammo ?? string.Empty);
                            addMethod.Invoke(convertedBeltItems, new[] { npcBelt });
                        }
                    }
                }

                // WearItems and BeltItems should already be initialized (even if empty) from above
                // But add safety check in case npcWearType/npcBeltType were null
                if (convertedWearItems == null && npcWearType != null)
                {
                    var hashSetType = typeof(HashSet<>).MakeGenericType(npcWearType);
                    convertedWearItems = Activator.CreateInstance(hashSetType);
                }
                if (convertedBeltItems == null && npcBeltType != null)
                {
                    var hashSetType = typeof(HashSet<>).MakeGenericType(npcBeltType);
                    convertedBeltItems = Activator.CreateInstance(hashSetType);
                }

                // Set properties using reflection
                SetProperty(npcConfigType, npcSpawnConfig, "Name", config.Name ?? string.Empty);
                SetProperty(npcConfigType, npcSpawnConfig, "WearItems", convertedWearItems);
                SetProperty(npcConfigType, npcSpawnConfig, "BeltItems", convertedBeltItems);
                // Kit: Set to empty string to force UpdateInventory() to use WearItems/BeltItems
                // If Kit is set, NpcSpawn will use GiveKit instead of UpdateInventory
                SetProperty(npcConfigType, npcSpawnConfig, "Kit", string.Empty);
                SetProperty(npcConfigType, npcSpawnConfig, "Health", config.Health);
                SetProperty(npcConfigType, npcSpawnConfig, "RoamRange", config.RoamRange);
                SetProperty(npcConfigType, npcSpawnConfig, "ChaseRange", config.ChaseRange);
                SetProperty(npcConfigType, npcSpawnConfig, "SenseRange", config.SenseRange);
                SetProperty(npcConfigType, npcSpawnConfig, "ListenRange", config.SenseRange / 2f);
                SetProperty(npcConfigType, npcSpawnConfig, "AttackRangeMultiplier", config.AttackRangeMultiplier);
                SetProperty(npcConfigType, npcSpawnConfig, "CheckVisionCone", config.CheckVisionCone);
                SetProperty(npcConfigType, npcSpawnConfig, "HostileTargetsOnly", false);
                SetProperty(npcConfigType, npcSpawnConfig, "VisionCone", config.VisionCone);
                SetProperty(npcConfigType, npcSpawnConfig, "DamageScale", config.DamageScale);
                SetProperty(npcConfigType, npcSpawnConfig, "TurretDamageScale", 0f);
                SetProperty(npcConfigType, npcSpawnConfig, "AimConeScale", config.AimConeScale);
                SetProperty(npcConfigType, npcSpawnConfig, "DisableRadio", config.DisableRadio);
                SetProperty(npcConfigType, npcSpawnConfig, "CanRunAwayWater", true);
                SetProperty(npcConfigType, npcSpawnConfig, "CanSleep", false);
                SetProperty(npcConfigType, npcSpawnConfig, "SleepDistance", 100f);
                SetProperty(npcConfigType, npcSpawnConfig, "Speed", config.Speed);
                SetProperty(npcConfigType, npcSpawnConfig, "AreaMask", 1);
                SetProperty(npcConfigType, npcSpawnConfig, "AgentTypeID", -1372625422);
                SetProperty(npcConfigType, npcSpawnConfig, "HomePosition", string.Empty);
                SetProperty(npcConfigType, npcSpawnConfig, "MemoryDuration", config.MemoryDuration);
                SetProperty(npcConfigType, npcSpawnConfig, "States", states);

                return npcSpawnConfig;
            }

            private static void SetProperty(Type type, object obj, string propertyName, object value)
            {
                if (type == null || obj == null) return;
                var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (property != null && property.CanWrite)
                {
                    try
                    {
                        property.SetValue(obj, value, null);
                    }
                    catch (Exception ex)
                    {
                        _ins.PrintError($"SetProperty: Failed to set {propertyName} on {type.Name}: {ex.Message}");
                    }
                }
            }

            private static void SetField(Type type, object obj, string fieldName, object value)
            {
                if (type == null || obj == null) return;
                var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    try
                    {
                        field.SetValue(obj, value);
                    }
                    catch (Exception ex)
                    {
                        _ins.PrintError($"SetField: Failed to set {fieldName} on {type.Name}: {ex.Message}");
                    }
                }
            }

            internal void SpawnCargoScientist(ScientistNPC entity)
            {
                if (entity == null || !entity.IsExists() || entity.skinID == 11162132011012 || Cargo == null || !Cargo.IsExists() || entity.GetParentEntity() != Cargo) return;

                bool isStationary = entity.ShortPrefabName == "scientistnpc_cargo_turret_lr300" || entity.ShortPrefabName == "scientistnpc_cargo_turret_any";

                if (entity.transform == null) return;

                if ((isStationary && Scientists.Any(x => x != null && x.IsExists() && x.transform != null && entity.transform != null && Vector3.Distance(x.transform.position, entity.transform.position) < 1f)) || CargoNpcCount <= 0)
                {
                    entity.Kill();
                    return;
                }

                NpcConfigCargo config = entity.ShortPrefabName == "scientistnpc_cargo_turret_lr300" ? _config.NpcStationaryOutsideCargo : entity.ShortPrefabName == "scientistnpc_cargo_turret_any" ? _config.NpcStationaryInsideCargo : _config.NpcMovingCargo;

                if (config == null)
                {
                    _ins.PrintError($"SpawnCargoScientist: Config is null for entity '{entity.ShortPrefabName}'");
                    entity.Kill();
                    return;
                }

                object objectConfig = GetObjectConfigCargo(config, isStationary);
                if (objectConfig == null)
                {
                    _ins.PrintError($"SpawnCargoScientist: Failed to create config object for '{config.Name}'");
                    entity.Kill();
                    return;
                }

                object spawnResult = _ins.NpcSpawn?.Call("SpawnNpc", entity.transform.position, objectConfig);
                if (spawnResult == null)
                {
                    _ins.PrintError($"SpawnCargoScientist: NpcSpawn.Call('SpawnNpc') returned null for '{config.Name}' at position {entity.transform.position}");
                    entity.Kill();
                    return;
                }

                ScientistNPC npc = spawnResult as ScientistNPC;
                if (npc == null)
                {
                    _ins.PrintError($"SpawnCargoScientist: NpcSpawn.Call('SpawnNpc') returned unexpected type '{spawnResult.GetType().Name}' for '{config.Name}'");
                    entity.Kill();
                    return;
                }

                if (!npc.IsExists())
                {
                    _ins.PrintError($"SpawnCargoScientist: NPC spawned but IsExists() returned false for '{config.Name}'");
                    entity.Kill();
                    return;
                }
                Scientists.Add(npc);

                _ins.NextTick(() =>
                {
                    if (npc == null || !npc.IsExists() || Cargo == null || !Cargo.IsExists() || entity == null || !entity.IsExists() || entity.transform == null) return;
                    _ins.NpcSpawn.Call("SetParentEntity", npc, Cargo, Cargo.transform.InverseTransformPoint(entity.transform.position));
                    if (npc.Brain?.Navigator != null)
                    {
                        npc.Brain.Navigator.CanUseNavMesh = false;
                        if (!isStationary && entity.Brain?.Navigator != null)
                        {
                            npc.Brain.Navigator.AStarGraph = entity.Brain.Navigator.AStarGraph;
                            npc.Brain.Navigator.CanUseAStar = true;
                        }
                    }
                    entity.Kill();
                });

                CargoNpcCount--;

                if (_ins.ActivePveMode && npc.net != null) _ins.PveMode.Call("EventAddScientists", _ins.Name, new HashSet<ulong> { npc.net.ID.Value });
            }

            private static object GetObjectConfigCargo(NpcConfigCargo config, bool isStationary)
            {
                if (config == null || _ins.NpcSpawn == null) return null;

                HashSet<string> states = isStationary ? new HashSet<string> { "IdleState", "CombatStationaryState" } : new HashSet<string> { "RoamState", "ChaseState", "CombatState" };
                if (config.BeltItems != null && config.BeltItems.Any(x => x.ShortName == "rocket.launcher" || x.ShortName == "explosive.timed")) states.Add("RaidState");

                // Use reflection to access NpcSpawn.NpcConfig (nested public class)
                var npcSpawnType = _ins.NpcSpawn?.GetType();
                if (npcSpawnType == null)
                {
                    _ins.PrintError("GetObjectConfigCargo: NpcSpawn plugin type is null!");
                    return null;
                }

                var npcConfigType = npcSpawnType.GetNestedType("NpcConfig", BindingFlags.Public);
                if (npcConfigType == null)
                {
                    _ins.PrintError("GetObjectConfigCargo: Failed to find NpcSpawn.NpcConfig type. Make sure NpcSpawn plugin is loaded.");
                    return null;
                }

                var npcSpawnConfig = Activator.CreateInstance(npcConfigType);

                // Get NpcSpawn's NpcWear and NpcBelt types for conversion
                var npcWearType = npcSpawnType.GetNestedType("NpcWear", BindingFlags.Public);
                var npcBeltType = npcSpawnType.GetNestedType("NpcBelt", BindingFlags.Public);

                if (npcWearType == null || npcBeltType == null)
                {
                    _ins.PrintError("GetObjectConfigCargo: Failed to find NpcSpawn.NpcWear or NpcBelt types.");
                    return null;
                }

                // Convert WearItems from HarborEvent.NpcWear to NpcSpawn.NpcWear
                // Always create HashSet (even if empty) to ensure UpdateInventory() has a valid collection
                object convertedWearItems = null;
                if (npcWearType != null)
                {
                    var hashSetType = typeof(HashSet<>).MakeGenericType(npcWearType);
                    convertedWearItems = Activator.CreateInstance(hashSetType);
                    
                    if (config.WearItems != null && config.WearItems.Count > 0)
                    {
                        var addMethod = hashSetType.GetMethod("Add");
                        foreach (var item in config.WearItems)
                        {
                            if (item == null) continue;
                            // Skip items with empty or null ShortName (NpcSpawn cannot create items without ShortName)
                            if (string.IsNullOrWhiteSpace(item.ShortName)) continue;
                            var npcWear = Activator.CreateInstance(npcWearType);
                            // NpcSpawn uses fields, not properties
                            SetField(npcWearType, npcWear, "ShortName", item.ShortName);
                            SetField(npcWearType, npcWear, "SkinID", item.SkinId);
                            addMethod.Invoke(convertedWearItems, new[] { npcWear });
                        }
                    }
                }

                // Convert BeltItems from HarborEvent.NpcBelt to NpcSpawn.NpcBelt
                // Always create HashSet (even if empty) to ensure UpdateInventory() has a valid collection
                object convertedBeltItems = null;
                if (npcBeltType != null)
                {
                    var hashSetType = typeof(HashSet<>).MakeGenericType(npcBeltType);
                    convertedBeltItems = Activator.CreateInstance(hashSetType);
                    
                    if (config.BeltItems != null && config.BeltItems.Count > 0)
                    {
                        var addMethod = hashSetType.GetMethod("Add");
                        foreach (var item in config.BeltItems)
                        {
                            if (item == null) continue;
                            // Skip items with empty or null ShortName (NpcSpawn cannot create items without ShortName)
                            if (string.IsNullOrWhiteSpace(item.ShortName)) continue;
                            var npcBelt = Activator.CreateInstance(npcBeltType);
                            // NpcSpawn uses fields, not properties
                            SetField(npcBeltType, npcBelt, "ShortName", item.ShortName);
                            SetField(npcBeltType, npcBelt, "Amount", item.Amount);
                            SetField(npcBeltType, npcBelt, "SkinID", item.SkinId);
                            SetField(npcBeltType, npcBelt, "Mods", item.Mods != null ? new HashSet<string>(item.Mods) : new HashSet<string>());
                            SetField(npcBeltType, npcBelt, "Ammo", item.Ammo ?? string.Empty);
                            addMethod.Invoke(convertedBeltItems, new[] { npcBelt });
                        }
                    }
                }

                // WearItems and BeltItems should already be initialized (even if empty) from above
                // But add safety check in case npcWearType/npcBeltType were null
                if (convertedWearItems == null && npcWearType != null)
                {
                    var hashSetType = typeof(HashSet<>).MakeGenericType(npcWearType);
                    convertedWearItems = Activator.CreateInstance(hashSetType);
                }
                if (convertedBeltItems == null && npcBeltType != null)
                {
                    var hashSetType = typeof(HashSet<>).MakeGenericType(npcBeltType);
                    convertedBeltItems = Activator.CreateInstance(hashSetType);
                }

                // Set properties using reflection
                SetProperty(npcConfigType, npcSpawnConfig, "Name", config.Name ?? string.Empty);
                SetProperty(npcConfigType, npcSpawnConfig, "WearItems", convertedWearItems);
                SetProperty(npcConfigType, npcSpawnConfig, "BeltItems", convertedBeltItems);
                // Kit: Set to empty string to force UpdateInventory() to use WearItems/BeltItems
                // If Kit is set, NpcSpawn will use GiveKit instead of UpdateInventory
                SetProperty(npcConfigType, npcSpawnConfig, "Kit", string.Empty);
                SetProperty(npcConfigType, npcSpawnConfig, "Health", config.Health);
                SetProperty(npcConfigType, npcSpawnConfig, "RoamRange", config.RoamRange);
                SetProperty(npcConfigType, npcSpawnConfig, "ChaseRange", config.ChaseRange);
                SetProperty(npcConfigType, npcSpawnConfig, "SenseRange", config.SenseRange);
                SetProperty(npcConfigType, npcSpawnConfig, "ListenRange", config.SenseRange / 2f);
                SetProperty(npcConfigType, npcSpawnConfig, "AttackRangeMultiplier", config.AttackRangeMultiplier);
                SetProperty(npcConfigType, npcSpawnConfig, "CheckVisionCone", config.CheckVisionCone);
                SetProperty(npcConfigType, npcSpawnConfig, "HostileTargetsOnly", false);
                SetProperty(npcConfigType, npcSpawnConfig, "VisionCone", config.VisionCone);
                SetProperty(npcConfigType, npcSpawnConfig, "DamageScale", config.DamageScale);
                SetProperty(npcConfigType, npcSpawnConfig, "TurretDamageScale", 0f);
                SetProperty(npcConfigType, npcSpawnConfig, "AimConeScale", config.AimConeScale);
                SetProperty(npcConfigType, npcSpawnConfig, "DisableRadio", config.DisableRadio);
                SetProperty(npcConfigType, npcSpawnConfig, "CanRunAwayWater", false);
                SetProperty(npcConfigType, npcSpawnConfig, "CanSleep", false);
                SetProperty(npcConfigType, npcSpawnConfig, "SleepDistance", 100f);
                SetProperty(npcConfigType, npcSpawnConfig, "Speed", config.Speed);
                SetProperty(npcConfigType, npcSpawnConfig, "AreaMask", 25);
                SetProperty(npcConfigType, npcSpawnConfig, "AgentTypeID", 0);
                SetProperty(npcConfigType, npcSpawnConfig, "HomePosition", string.Empty);
                SetProperty(npcConfigType, npcSpawnConfig, "MemoryDuration", config.MemoryDuration);
                SetProperty(npcConfigType, npcSpawnConfig, "States", states);

                return npcSpawnConfig;
            }

            internal void SpawnHelicopter()
            {
                Helicopter = GameManager.server.CreateEntity("assets/prefabs/npc/patrol helicopter/patrolhelicopter.prefab") as PatrolHelicopter;
                Helicopter.enableSaving = false;
                Helicopter.Spawn();

                Helicopter.skinID = 81182151852251420;

                Helicopter.startHealth = _config.Helicopter.Hp;
                Helicopter.InitializeHealth(_config.Helicopter.Hp, _config.Helicopter.Hp);
                PatrolHelicopter.weakspot[] weakspots = Helicopter.weakspots;
                weakspots[0].maxHealth = _config.Helicopter.HpMainRotor;
                weakspots[1].maxHealth = _config.Helicopter.HpTailRotor;
                weakspots[0].health = _config.Helicopter.HpMainRotor;
                weakspots[1].health = _config.Helicopter.HpTailRotor;

                Helicopter.maxCratesToSpawn = _config.Helicopter.CountCrates;

                Helicopter.myAI.timeBetweenRockets = _config.Helicopter.TimeBetweenRockets;
                Helicopter.myAI.leftGun.fireRate = Helicopter.myAI.rightGun.fireRate = _config.Helicopter.FireRate;
                Helicopter.myAI.leftGun.burstLength = Helicopter.myAI.rightGun.burstLength = _config.Helicopter.BurstLength;
                Helicopter.myAI.leftGun.timeBetweenBursts = Helicopter.myAI.rightGun.timeBetweenBursts = _config.Helicopter.TimeBetweenBursts;
                Helicopter.myAI.leftGun.maxTargetRange = Helicopter.myAI.rightGun.maxTargetRange = _config.Helicopter.MaxTargetRange;

                Helicopter.transform.position = GetSpawnPosHelicopter();

                if (_ins.ActivePveMode) _ins.PveMode.Call("EventAddHelicopters", _ins.Name, new HashSet<ulong> { Helicopter.net.ID.Value });

                GoToNewPosHelicopter();

                PatrolHelicopterAI.use_danger_zones = false;
            }

            private Vector3 GetSpawnPosHelicopter()
            {
                if (_config.Helicopter.IsLongDistance)
                {
                    float height = transform.position.y + 70f;
                    float size = World.Size / 2f;
                    List<Vector3> list = Pool.Get<List<Vector3>>();
                    list.Add(new Vector3(-size, height, -size));
                    list.Add(new Vector3(-size, height, size));
                    list.Add(new Vector3(size, height, -size));
                    list.Add(new Vector3(size, height, size));
                    Vector3 spawnPos3 = list.Min(x => Vector3.Distance(x, transform.position));
                    Pool.FreeUnmanaged(ref list);
                    return spawnPos3;
                }
                else return transform.position + new Vector3(0f, 70f, 0f);
            }

            private void CheckMoveHelicopter()
            {
                if (!Helicopter.IsExists() || Helicopter.myAI == null) return;
                if (Helicopter.myAI._currentState != PatrolHelicopterAI.aiState.PATROL) return;
                GoToNewPosHelicopter();
            }

            private void GoToNewPosHelicopter()
            {
                Vector2 vector2 = UnityEngine.Random.insideUnitCircle * 70f;
                Vector3 pos = Cargo.transform.position + new Vector3(vector2.x, 70f, vector2.y);
                Helicopter.myAI.hasInterestZone = true;
                Helicopter.myAI.interestZoneOrigin = pos;
                Helicopter.myAI.ExitCurrentState();
                Helicopter.myAI.State_Move_Enter(pos);
            }

            internal void EnablePveMode(PveModeConfig config, BasePlayer player)
            {
                if (!_ins.ActivePveMode) return;

                Dictionary<string, object> dic = new Dictionary<string, object>
                {
                    ["Damage"] = config.Damage,
                    ["ScaleDamage"] = config.ScaleDamage,
                    ["LootCrate"] = config.LootCrate,
                    ["HackCrate"] = config.HackCrate,
                    ["LootNpc"] = config.LootNpc,
                    ["DamageNpc"] = config.DamageNpc,
                    ["DamageTank"] = config.DamageTank,
                    ["DamageHelicopter"] = config.DamageHelicopter,
                    ["DamageTurret"] = false,
                    ["TargetNpc"] = config.TargetNpc,
                    ["TargetTank"] = config.TargetTank,
                    ["TargetHelicopter"] = config.TargetHelicopter,
                    ["TargetTurret"] = false,
                    ["CanEnter"] = config.CanEnter,
                    ["CanEnterCooldownPlayer"] = config.CanEnterCooldownPlayer,
                    ["TimeExitOwner"] = config.TimeExitOwner,
                    ["AlertTime"] = config.AlertTime,
                    ["RestoreUponDeath"] = config.RestoreUponDeath,
                    ["CooldownOwner"] = config.CooldownOwner,
                    ["Darkening"] = config.Darkening
                };

                HashSet<ulong> crates = new HashSet<ulong>();
                foreach (KeyValuePair<LootContainer, int> kvp in Crates)
                {
                    if (kvp.Key != null && kvp.Key.IsExists() && kvp.Key.net != null)
                        crates.Add(kvp.Key.net.ID.Value);
                }
                foreach (KeyValuePair<HackableLockedCrate, int> kvp in HackCrates)
                {
                    if (kvp.Key != null && kvp.Key.IsExists() && kvp.Key.net != null)
                        crates.Add(kvp.Key.net.ID.Value);
                }

                HashSet<ulong> scientistIds = new HashSet<ulong>();
                foreach (ScientistNPC scientist in Scientists)
                {
                    if (scientist != null && scientist.IsExists() && scientist.net != null)
                        scientistIds.Add(scientist.net.ID.Value);
                }
                _ins.PveMode.Call("EventAddPveMode", _ins.Name, dic, transform.position, _config.Radius, crates, scientistIds, _config.Bradley.IsBradley ? new HashSet<ulong> { Bradley.net.ID.Value } : new HashSet<ulong>(), new HashSet<ulong>(), new HashSet<ulong>(), new HashSet<ulong>(), player);
            }
        }
        #endregion Controller

        #region Crane Animation
        internal class AnimationTransformCrane : FacepunchBehaviour
        {
            private HarborCraneContainerPickup Crane { get; set; } = null;

            internal BaseEntity Cargo { get; set; } = null;
            private float OffsetYCargo { get; set; } = 0f;
            private Vector3 StartRotCargo { get; set; } = Vector3.zero;
            internal Vector3 EndRotCargo { get; set; } = Vector3.zero;

            private List<Vector3> Path { get; } = new List<Vector3>();

            private float SecondsTaken { get; set; } = 0f;
            private float SecondsToTake { get; set; } = 0f;
            private float WaypointDone { get; set; } = 0f;

            private Vector3 StartPos { get; set; } = Vector3.zero;
            private Vector3 EndPos { get; set; } = Vector3.zero;

            private float Speed { get; set; } = 0f;

            private void Awake()
            {
                Crane = GetComponent<HarborCraneContainerPickup>();
                enabled = false;
            }

            internal void AddPath(HashSet<Vector3> path, float speed, BaseEntity cargo, float offsetY, Vector3 cargoRotation)
            {
                foreach (Vector3 point in path) Path.Add(point);
                Speed = speed;
                Cargo = cargo;
                OffsetYCargo = offsetY;
                EndRotCargo = cargoRotation;
                enabled = true;
            }

            private void Update()
            {
                if (SecondsTaken == 0f)
                {
                    if (Path.Count == 0)
                    {
                        StartPos = EndPos = Vector3.zero;
                        SecondsToTake = 0f;
                        SecondsTaken = 0f;
                        WaypointDone = 0f;
                        OffsetYCargo = 0f;
                        enabled = false;
                        _ins.Controller.FinishPathCrane();
                        return;
                    }
                    StartPos = Crane.ServerTowerGrab.position;
                    if (EndRotCargo != Vector3.zero && Path.Count == 1) StartRotCargo = Cargo.transform.rotation.eulerAngles;
                    if (Path[0] != StartPos)
                    {
                        EndPos = Path[0];
                        SecondsToTake = Vector3.Distance(EndPos, StartPos) / Speed;
                        SecondsTaken = 0f;
                        WaypointDone = 0f;
                    }
                    Path.RemoveAt(0);
                }
                if (StartPos != EndPos)
                {
                    SecondsTaken += Time.deltaTime;
                    WaypointDone = Mathf.InverseLerp(0f, SecondsToTake, SecondsTaken);
                    Vector3 target = Vector3.Lerp(StartPos, EndPos, WaypointDone);
                    Crane.transform.rotation = Quaternion.LookRotation((target.WithY(Crane.transform.position.y) - Crane.transform.position).normalized, Vector3.up);
                    Crane.ServerTowerGrab.localPosition = Crane.transform.InverseTransformPoint(target);
                    Crane.CraneGrab.localPosition = Crane.ServerTowerGrab.localPosition + new Vector3(0f, 19f, 0f);
                    Crane.SendNetworkUpdate();
                    if (Cargo != null)
                    {
                        if (EndRotCargo != Vector3.zero && Path.Count == 0) Cargo.transform.rotation = Quaternion.Lerp(Quaternion.Euler(StartRotCargo), Quaternion.Euler(EndRotCargo), WaypointDone);
                        Cargo.transform.position = new Vector3(Crane.ServerTowerGrab.position.x, Crane.ServerTowerGrab.position.y - OffsetYCargo, Crane.ServerTowerGrab.position.z);
                        Cargo.TransformChanged();
                        Cargo.SendNetworkUpdate();
                    }
                    if (WaypointDone >= 1f)
                    {
                        if (EndRotCargo != Vector3.zero && Path.Count == 0) StartRotCargo = EndRotCargo = Vector3.zero;
                        SecondsTaken = 0f;
                    }
                }
            }
        }
        #endregion Crane Animation

        #region Bradley Animation
        internal class PointAnimationTransform { public Vector3 Pos; public Vector3 Rot; }

        internal class AnimationTransformVehicle : FacepunchBehaviour
        {
            private BaseEntity Main { get; set; } = null;

            private List<PointAnimationTransform> Path { get; } = new List<PointAnimationTransform>();

            private float SecondsTaken { get; set; } = 0f;
            private float SecondsToTake { get; set; } = 0f;
            private float WaypointDone { get; set; } = 0f;

            private Vector3 StartPos { get; set; } = Vector3.zero;
            private Vector3 EndPos { get; set; } = Vector3.zero;

            private Vector3 StartRot { get; set; } = Vector3.zero;
            private Vector3 EndRot { get; set; } = Vector3.zero;

            private float Speed { get; set; } = 0f;

            private void Awake()
            {
                Main = GetComponent<BaseEntity>();
                enabled = false;
            }

            internal void AddPath(HashSet<PointAnimationTransform> path, float speed)
            {
                foreach (PointAnimationTransform point in path) Path.Add(point);
                Speed = speed;
                enabled = true;
            }

            private void Update()
            {
                if (SecondsTaken == 0f)
                {
                    if (Path.Count == 0)
                    {
                        StartPos = EndPos = Vector3.zero;
                        StartRot = EndRot = Vector3.zero;
                        SecondsToTake = 0f;
                        SecondsTaken = 0f;
                        WaypointDone = 0f;
                        enabled = false;
                        _ins.Controller.BradleyStart();
                        return;
                    }
                    StartPos = transform.position;
                    StartRot = transform.rotation.eulerAngles;
                    if (Path[0].Pos != StartPos || Path[0].Rot != StartRot)
                    {
                        EndPos = Path[0].Pos != StartPos ? Path[0].Pos : StartPos;
                        EndRot = Path[0].Rot != StartRot ? Path[0].Rot : StartRot;
                        SecondsToTake = Vector3.Distance(EndPos, StartPos) / Speed;
                        SecondsTaken = 0f;
                        WaypointDone = 0f;
                    }
                    Path.RemoveAt(0);
                }
                if (StartPos != EndPos || StartRot != EndRot)
                {
                    SecondsTaken += Time.deltaTime;
                    WaypointDone = Mathf.InverseLerp(0f, SecondsToTake, SecondsTaken);
                    if (StartPos != EndPos) transform.position = Vector3.Lerp(StartPos, EndPos, WaypointDone);
                    if (StartRot != EndRot) transform.rotation = Quaternion.Lerp(Quaternion.Euler(StartRot), Quaternion.Euler(EndRot), WaypointDone);
                    Main.TransformChanged();
                    Main.SendNetworkUpdate();
                    if (WaypointDone >= 1f) SecondsTaken = 0f;
                }
            }
        }
        #endregion Bradley Animation

        #region Find Position
        internal MonumentInfo GetMonument()
        {
            List<MonumentInfo> list = Pool.Get<List<MonumentInfo>>();
            foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
            {
                if ((monument.name.Contains("harbor_1") && _config.IsSmallHarbor) ||
                    (monument.name.Contains("harbor_2") && _config.IsLargeHarbor))
                    list.Add(monument);
            }
            MonumentInfo result = list.Count > 0 ? list.GetRandom() : null;
            Pool.FreeUnmanaged(ref list);
            return result;
        }
        #endregion Find Position

        #region Spawn Loot
        #region NPC
        private void OnCorpsePopulate(ScientistNPC entity, NPCPlayerCorpse corpse)
        {
            if (entity == null) return;

            if (!Controller.Scientists.Contains(entity)) return;
            Controller.Scientists.Remove(entity);

            int typeLootTable;
            PrefabLootTableConfig prefabLootTable;
            LootTableConfig ownTableConfig;
            bool isRemoveCorpse;

            if (entity.displayName == _config.NpcMovingCargo.Name)
            {
                typeLootTable = _config.NpcMovingCargo.TypeLootTable;
                prefabLootTable = _config.NpcMovingCargo.PrefabLootTable;
                ownTableConfig = _config.NpcMovingCargo.OwnLootTable;
                isRemoveCorpse = _config.NpcMovingCargo.IsRemoveCorpse;
            }
            else if (entity.displayName == _config.NpcStationaryInsideCargo.Name)
            {
                typeLootTable = _config.NpcStationaryInsideCargo.TypeLootTable;
                prefabLootTable = _config.NpcStationaryInsideCargo.PrefabLootTable;
                ownTableConfig = _config.NpcStationaryInsideCargo.OwnLootTable;
                isRemoveCorpse = _config.NpcStationaryInsideCargo.IsRemoveCorpse;
            }
            else if (entity.displayName == _config.NpcStationaryOutsideCargo.Name)
            {
                typeLootTable = _config.NpcStationaryOutsideCargo.TypeLootTable;
                prefabLootTable = _config.NpcStationaryOutsideCargo.PrefabLootTable;
                ownTableConfig = _config.NpcStationaryOutsideCargo.OwnLootTable;
                isRemoveCorpse = _config.NpcStationaryOutsideCargo.IsRemoveCorpse;
            }
            else
            {
                PresetConfig preset = Controller.IsSmallHarbor ? _config.NpcSmall.FirstOrDefault(x => x.Config.Name == entity.displayName) : _config.NpcLarge.FirstOrDefault(x => x.Config.Name == entity.displayName);
                typeLootTable = preset.TypeLootTable;
                prefabLootTable = preset.PrefabLootTable;
                ownTableConfig = preset.OwnLootTable;
                isRemoveCorpse = preset.Config.IsRemoveCorpse;
            }

            NextTick(() =>
            {
                if (corpse == null) return;
                ItemContainer container = corpse.containers[0];
                if (typeLootTable == 1 || typeLootTable == 4 || typeLootTable == 5)
                {
                    container.ClearItemsContainer();
                    if (typeLootTable == 4 || typeLootTable == 5) AddToContainerPrefab(container, prefabLootTable);
                    if (typeLootTable == 1 || typeLootTable == 5) AddToContainerItem(container, ownTableConfig);
                }
                if (isRemoveCorpse && corpse.IsExists()) corpse.Kill();
            });
        }

        private object CanPopulateLoot(ScientistNPC entity, NPCPlayerCorpse corpse)
        {
            if (entity == null || corpse == null || Controller == null) return null;
            if (!Controller.Scientists.Contains(entity)) return null;
            if (GetTypeLootTableNpc(entity) == 2) return null;
            else return true;
        }

        private object OnCustomLootNPC(NetworkableId netId)
        {
            if (Controller == null) return null;
            ScientistNPC entity = Controller.Scientists.FirstOrDefault(x => x.IsExists() && x.net.ID.Value == netId.Value);
            if (entity == null) return null;
            if (GetTypeLootTableNpc(entity) == 3) return null;
            else return true;
        }

        private int GetTypeLootTableNpc(ScientistNPC entity)
        {
            if (_config.NpcMovingCargo.Name == entity.displayName) return _config.NpcMovingCargo.TypeLootTable;
            else if (_config.NpcStationaryInsideCargo.Name == entity.displayName) return _config.NpcStationaryInsideCargo.TypeLootTable;
            else if (_config.NpcStationaryOutsideCargo.Name == entity.displayName) return _config.NpcStationaryOutsideCargo.TypeLootTable;
            else
            {
                PresetConfig preset = Controller.IsSmallHarbor ? _config.NpcSmall.FirstOrDefault(x => x.Config.Name == entity.displayName) : _config.NpcLarge.FirstOrDefault(x => x.Config.Name == entity.displayName);
                return preset.TypeLootTable;
            }
        }
        #endregion NPC

        #region Crates
        private bool IsEventBradleyCrate(LootContainer container) => container is LockedByEntCrate && container.ShortPrefabName == "bradley_crate" && Vector3.Distance(container.transform.position, Controller.DestroyPositionBradley) < 10f;

        private void OnEntitySpawned(LockedByEntCrate crate)
        {
            if (crate == null) return;

            if (IsEventBradleyCrate(crate))
            {
                if (ActivePveMode) PveMode.Call("EventAddCrates", Name, new HashSet<ulong> { crate.net.ID.Value });
                if (_config.Bradley.TypeLootTable is 1 or 4 or 5)
                {
                    NextTick(() =>
                    {
                        crate.inventory.ClearItemsContainer();
                        if (_config.Bradley.TypeLootTable is 4 or 5) AddToContainerPrefab(crate.inventory, _config.Bradley.PrefabLootTable);
                        if (_config.Bradley.TypeLootTable is 1 or 5) AddToContainerItem(crate.inventory, _config.Bradley.OwnLootTable);
                    });
                }
            }

            if (ActivePveMode && crate.ShortPrefabName == "heli_crate" && Vector3.Distance(crate.transform.position, Controller.transform.position) < _config.Radius)
                PveMode.Call("EventAddCrates", Name, new HashSet<ulong> { crate.net.ID.Value });
        }

        private object CanPopulateLoot(LootContainer container)
        {
            if (container == null || Controller == null) return null;
            else return GetResultLoot(container, 2);
        }

        private object OnCustomLootContainer(NetworkableId netId)
        {
            if (Controller == null) return null;
            LootContainer container = BaseNetworkable.serverEntities.Find(netId) as LootContainer;
            return container == null ? null : GetResultLoot(container, 3);
        }

        private object OnContainerPopulate(LootContainer container)
        {
            if (container == null || Controller == null) return null;
            else return GetResultLoot(container, 6);
        }

        private object GetResultLoot(LootContainer container, int type)
        {
            int typeLootTable;
            if (Controller.Crates.TryGetValue(container, out typeLootTable))
            {
                if (typeLootTable == type) return null;
                else return true;
            }
            else if (container is HackableLockedCrate hackCrate && Controller.HackCrates.TryGetValue(hackCrate, out typeLootTable))
            {
                if (typeLootTable == type) return null;
                else return true;
            }
            else if (IsEventBradleyCrate(container))
            {
                if (_config.Bradley.TypeLootTable == type) return null;
                else return true;
            }
            else return null;
        }
        #endregion Crates

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
            foreach (CrateConfig crateConfig in _config.ContainerCrates)
            {
                CheckLootTable(crateConfig.OwnLootTable);
                CheckPrefabLootTable(crateConfig.PrefabLootTable);
            }
            foreach (CrateConfig crateConfig in _config.CargoCrates)
            {
                CheckLootTable(crateConfig.OwnLootTable);
                CheckPrefabLootTable(crateConfig.PrefabLootTable);
            }

            CheckLootTable(_config.ContainerHackCrates.OwnLootTable);
            CheckPrefabLootTable(_config.ContainerHackCrates.PrefabLootTable);

            CheckLootTable(_config.CargoHackCrates.OwnLootTable);
            CheckPrefabLootTable(_config.CargoHackCrates.PrefabLootTable);

            CheckLootTable(_config.Bradley.OwnLootTable);
            CheckPrefabLootTable(_config.Bradley.PrefabLootTable);

            foreach (PresetConfig preset in _config.NpcSmall)
            {
                CheckLootTable(preset.OwnLootTable);
                CheckPrefabLootTable(preset.PrefabLootTable);
            }

            foreach (PresetConfig preset in _config.NpcLarge)
            {
                CheckLootTable(preset.OwnLootTable);
                CheckPrefabLootTable(preset.PrefabLootTable);
            }

            CheckLootTable(_config.NpcMovingCargo.OwnLootTable);
            CheckPrefabLootTable(_config.NpcMovingCargo.PrefabLootTable);

            CheckLootTable(_config.NpcStationaryInsideCargo.OwnLootTable);
            CheckPrefabLootTable(_config.NpcStationaryInsideCargo.PrefabLootTable);

            CheckLootTable(_config.NpcStationaryOutsideCargo.OwnLootTable);
            CheckPrefabLootTable(_config.NpcStationaryOutsideCargo.PrefabLootTable);

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

        #region PveMode
        [PluginReference] private readonly Plugin PveMode;

        internal bool ActivePveMode => _config.PveMode.Pve && plugins.Exists("PveMode");

        private void SetOwnerPveMode(string shortname, BasePlayer player)
        {
            if (string.IsNullOrEmpty(shortname) || shortname != Name || !player.IsPlayer()) return;
            Controller.Owner = player;
            AlertToAllPlayers("SetOwner", _config.Chat.Prefix, player.displayName);
        }

        private void ClearOwnerPveMode(string shortname)
        {
            if (string.IsNullOrEmpty(shortname) || shortname != Name) return;
            Controller.Owner = null;
        }
        #endregion PveMode

        #region TruePVE
        private object CanEntityTakeDamage(BasePlayer victim, HitInfo hitinfo)
        {
            if (!_config.IsCreateZonePvp || victim == null || hitinfo == null || Controller == null) return null;
            BasePlayer attacker = hitinfo.InitiatorPlayer;
            if (Controller.Players.Contains(victim) && (attacker == null || Controller.Players.Contains(attacker))) return true;
            else return null;
        }
        #endregion TruePVE

        #region NTeleportation
        private object CanTeleport(BasePlayer player, Vector3 to)
        {
            if (_config.NTeleportationInterrupt && Controller != null && (Controller.Players.Contains(player) || Vector3.Distance(Controller.transform.position, to) < _config.Radius)) return GetMessage("NTeleportation", player.UserIDString, _config.Chat.Prefix);
            else return null;
        }

        private void OnPlayerTeleported(BasePlayer player, Vector3 oldPos, Vector3 newPos)
        {
            if (Controller == null || !player.IsPlayer()) return;
            if (!Controller.Players.Contains(player) && Vector3.Distance(Controller.transform.position, newPos) < _config.Radius) Controller.EnterPlayer(player);
            if (Controller.Players.Contains(player) && Vector3.Distance(Controller.transform.position, newPos) > _config.Radius) Controller.ExitPlayer(player);
        }
        #endregion NTeleportation

        #region BetterNpc
        private object CanBradleySpawnNpc(BradleyAPC bradley)
        {
            if (Controller == null || Controller.Bradley == null) return null;
            if (bradley == Controller.Bradley) return true;
            else return null;
        }

        private object CanHelicopterSpawnNpc(PatrolHelicopter helicopter)
        {
            if (Controller == null || Controller.Helicopter == null) return null;
            if (helicopter == Controller.Helicopter) return true;
            else return null;
        }

        private object CanCargoShipSpawnNpc(CargoShip cargo)
        {
            if (Controller == null || Controller.Cargo == null) return null;
            if (cargo == Controller.Cargo) return true;
            else return null;
        }
        #endregion BetterNpc

        #region Bradley Tiers
        private object CanBradleyTiersEdit(BradleyAPC bradley)
        {
            if (Controller == null || Controller.Bradley == null) return null;
            if (bradley == Controller.Bradley) return true;
            else return null;
        }
        #endregion Bradley Tiers

        #region Economy
        [PluginReference] private readonly Plugin Economics, ServerRewards, IQEconomic, XPerience;

        private Dictionary<ulong, double> PlayersBalance { get; } = new Dictionary<ulong, double>();

        private void ActionEconomy(ulong playerId, string type, string arg = "")
        {
            switch (type)
            {
                case "Crates":
                    if (_config.Economy.Crates.ContainsKey(arg)) AddBalance(playerId, _config.Economy.Crates[arg]);
                    break;
                case "Bradley":
                    AddBalance(playerId, _config.Economy.Bradley);
                    break;
                case "Npc":
                    AddBalance(playerId, _config.Economy.Npc);
                    break;
                case "LockedCrate":
                    AddBalance(playerId, _config.Economy.LockedCrate);
                    break;
                case "Button":
                    AddBalance(playerId, _config.Economy.Button);
                    break;
            }
        }

        private void AddBalance(ulong playerId, double balance)
        {
            if (balance == 0) return;
            if (PlayersBalance.ContainsKey(playerId)) PlayersBalance[playerId] += balance;
            else PlayersBalance.Add(playerId, balance);
        }

        private void SendBalance()
        {
            if (PlayersBalance.Count == 0) return;
            if (_config.Economy.Plugins.Count > 0)
            {
                foreach (KeyValuePair<ulong, double> dic in PlayersBalance)
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
                        AlertToPlayer(player, GetMessage("SendEconomy", player.UserIDString, _config.Chat.Prefix, dic.Value));
                    }
                }
            }
            ulong winnerId = PlayersBalance.Max(x => x.Value).Key;
            Interface.Oxide.CallHook($"On{Name}Winner", winnerId);
            foreach (string command in _config.Economy.Commands) Server.Command(command.Replace("{steamid}", $"{winnerId}"));
            PlayersBalance.Clear();
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
            if (!string.IsNullOrEmpty(_config.Chat.Prefix)) message = message.Replace(_config.Chat.Prefix + " ", string.Empty);
            return message;
        }

        private bool CanSendDiscordMessage => _config.Discord.IsDiscord && !string.IsNullOrEmpty(_config.Discord.WebhookUrl) && _config.Discord.WebhookUrl != "https://support.discordapp.com/hc/en-us/articles/228383668-Intro-to-Webhooks";

        private void AlertToAllPlayers(string langKey, params object[] args)
        {
            if (CanSendDiscordMessage && _config.Discord.Keys.Contains(langKey))
            {
                object fields = new[] { new { name = Title, value = ClearColorAndSize(GetMessage(langKey, null, args)), inline = false } };
                DiscordMessages?.Call("API_SendFancyMessage", _config.Discord.WebhookUrl, "", _config.Discord.EmbedColor, JsonConvert.SerializeObject(fields), null, this);
            }
            foreach (BasePlayer player in BasePlayer.activePlayerList)
                if (_config.DistanceAlerts == 0f || Vector3.Distance(player.transform.position, Controller.transform.position) <= _config.DistanceAlerts)
                    AlertToPlayer(player, GetMessage(langKey, player.UserIDString, args));
        }

        private void AlertToPlayer(BasePlayer player, string message)
        {
            if (_config.Chat.IsChat) PrintToChat(player, message);
            if (_config.GameTip.IsGameTip) player.SendConsoleCommand("gametip.showtoast", _config.GameTip.Style, ClearColorAndSize(message), string.Empty);
            if (_config.GuiAnnouncements.IsGuiAnnouncements) GUIAnnouncements?.Call("CreateAnnouncement", ClearColorAndSize(message), _config.GuiAnnouncements.BannerColor, _config.GuiAnnouncements.TextColor, player, _config.GuiAnnouncements.ApiAdjustVPosition);
            if (_config.Notify.IsNotify && plugins.Exists("Notify")) Notify?.Call("SendNotify", player, _config.Notify.Type, ClearColorAndSize(message));
        }
        #endregion Alerts

        #region Radiation Puzzle Reset
        private static PuzzleReset GetPuzzleReset(MonumentInfo monument)
        {
            PuzzleReset result = null;
            float distance = float.MaxValue;
            foreach (PuzzleReset puzzleReset in PuzzleReset.AllResets)
            {
                if (!puzzleReset.radiationReset) continue;
                float single = Vector3.Distance(monument.transform.position, puzzleReset.transform.position);
                if (single < distance)
                {
                    result = puzzleReset;
                    distance = single;
                }
            }
            return result;
        }

        private static void DisableRadiation(PuzzleReset puzzleReset)
        {
            if (puzzleReset == null) return;
            puzzleReset.CallPrivateMethod("SetRadiusRadiationAmount", 0f);
            puzzleReset.radiationReset = false;
        }

        private static void EnableRadiation(PuzzleReset puzzleReset)
        {
            if (puzzleReset == null) return;
            puzzleReset.radiationReset = true;
        }
        #endregion Radiation Puzzle Reset

        #region GUI
        private HashSet<string> Names { get; } = new HashSet<string>
        {
            "Tab_KpucTaJl",
            "Clock_KpucTaJl",
            "Crate_KpucTaJl",
            "Npc_KpucTaJl"
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
        [PluginReference] private readonly Plugin NpcSpawn, BetterNpc, MonumentOwner;

        private HashSet<string> HooksInsidePlugin { get; } = new HashSet<string>
        {
            "OnEntityTakeDamage",
            "CanBuild",
            "CanChangeGrade",
            "OnStructureRotate",
            "OnButtonPress",
            "OnPlayerConnected",
            "OnPlayerDeath",
            "OnEntityDeath",
            "CanBradleyApcTarget",
            "OnEntityKill",
            "CanHackCrate",
            "OnCrateHack",
            "OnLootEntity",
            "OnPlayerCommand",
            "OnServerCommand",
            "OnCorpsePopulate",
            "CanPopulateLoot",
            "OnCustomLootNPC",
            "OnEntitySpawned",
            "OnCargoShipHarborApproach",
            "OnCustomLootContainer",
            "OnContainerPopulate",
            "SetOwnerPveMode",
            "ClearOwnerPveMode",
            "CanEntityTakeDamage",
            "CanTeleport",
            "OnPlayerTeleported",
            "CanBradleySpawnNpc",
            "CanHelicopterSpawnNpc",
            "CanCargoShipSpawnNpc",
            "CanBradleyTiersEdit"
        };

        private void ToggleHooks(bool subscribe)
        {
            foreach (string hook in HooksInsidePlugin)
            {
                if (subscribe) Subscribe(hook);
                else Unsubscribe(hook);
            }
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

        private static void UpdateMarkerForPlayer(BasePlayer player, Vector3 pos, PointConfig config)
        {
            if (player == null || player.IsSleeping()) return;
            bool isAdmin = player.IsAdmin;
            if (!isAdmin)
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                player.SendNetworkUpdateImmediate();
            }
            try
            {
                player.SendConsoleCommand("ddraw.text", 1f, Color.white, pos, $"<size={config.Size}><color={config.Color}>{config.Text}</color></size>");
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

        private void CheckVersionPlugin()
        {
            webrequest.Enqueue("http://37.153.157.216:5000/Api/GetPluginVersions?pluginName=HarborEvent", null, (code, response) =>
            {
                if (code != 200 || string.IsNullOrEmpty(response)) return;
                string[] array = response.Replace("\"", string.Empty).Split('.');
                VersionNumber latestVersion = new VersionNumber(Convert.ToInt32(array[0]), Convert.ToInt32(array[1]), Convert.ToInt32(array[2]));
                if (Version < latestVersion) PrintWarning($"A new version ({latestVersion}) of the plugin is available! You need to update the plugin:\n- https://lone.design/product/harbor-event-rust-plugin\n- https://codefling.com/plugins/harbor-event");
            }, this);
        }

        private bool PluginExistsForStart(string pluginName)
        {
            if (plugins.Exists(pluginName)) return true;
            PrintError($"{pluginName} plugin doesn`t exist! (https://drive.google.com/drive/folders/1-18L-mG7yiGxR-PQYvd11VvXC2RQ4ZCu?usp=sharing)");
            Interface.Oxide.UnloadPlugin(Name);
            return false;
        }
        #endregion Helpers

        #region Commands
        [ChatCommand("harborstart")]
        private void ChatStartEvent(BasePlayer player)
        {
            if (player.IsAdmin)
            {
                if (!Active) Start(null);
                else PrintToChat(player, GetMessage("EventActive", player.UserIDString, _config.Chat.Prefix));
            }
        }

        [ChatCommand("harborstop")]
        private void ChatStopEvent(BasePlayer player)
        {
            if (player.IsAdmin)
            {
                if (Controller != null) Finish();
                else Interface.Oxide.ReloadPlugin(Name);
            }
        }

        [ChatCommand("harborpos")]
        private void ChatCommandPos(BasePlayer player)
        {
            if (!player.IsAdmin || Controller == null) return;
            Vector3 pos = Controller.transform.InverseTransformPoint(player.transform.position);
            Puts($"Position: {pos}");
            PrintToChat(player, $"Position: {pos}");
        }

        [ConsoleCommand("harborstart")]
        private void ConsoleStartEvent(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null) return;
            if (!Active)
            {
                if (arg.Args == null || arg.Args.Length != 1)
                {
                    Start(null);
                    return;
                }
                ulong steamId = Convert.ToUInt64(arg.Args[0]);
                BasePlayer target = BasePlayer.FindByID(steamId);
                if (target == null)
                {
                    Start(null);
                    Puts($"Player with SteamID {steamId} not found!");
                    return;
                }
                Start(target);
            }
            else Puts("This event is active now. To finish this event (harborstop), then to start the next one");
        }

        [ConsoleCommand("harborstop")]
        private void ConsoleStopEvent(ConsoleSystem.Arg arg)
        {
            if (arg.Player() == null)
            {
                if (Controller != null) Finish();
                else Interface.Oxide.ReloadPlugin(Name);
            }
        }
        #endregion Commands
    }
}

namespace Oxide.Plugins.HarborEventExtensionMethods
{
    public static class ExtensionMethods
    {
        public static bool Any<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) if (predicate(enumerator.Current)) return true;
            return false;
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

        public static FieldInfo GetPrivateFieldInfo(Type type, string fieldName)
        {
            foreach (FieldInfo fi in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) if (fi.Name == fieldName) return fi;
            return null;
        }

        public static object GetPrivateFieldValue(this object obj, string fieldName)
        {
            FieldInfo fi = GetPrivateFieldInfo(obj.GetType(), fieldName);
            if (fi != null) return fi.GetValue(obj);
            else return null;
        }

        public static void SetPrivateFieldValue(this object obj, string fieldName, object value)
        {
            FieldInfo info = GetPrivateFieldInfo(obj.GetType(), fieldName);
            if (info != null) info.SetValue(obj, value);
        }

        public static object CallPrivateMethod(this object obj, string methodName, params object[] args)
        {
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            MethodInfo mi = obj.GetType().GetMethod(methodName, flags);
            if (mi != null) return mi.Invoke(obj, args);
            else return null;
        }
    }
}