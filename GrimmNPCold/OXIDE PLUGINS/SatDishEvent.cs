using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using Facepunch;
using System.Reflection;
using UnityEngine.Networking;
using Oxide.Plugins.SatDishEventExtensionMethods;

namespace Oxide.Plugins
{
    [Info("SatDishEvent", "KpucTaJl | Updated by Grimm530", "2.2.91")]
    internal class SatDishEvent : RustPlugin
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
            if (_config.PluginVersion < new VersionNumber(2, 0, 5))
            {
                _config.Gui = new GuiConfig
                {
                    IsGui = true,
                    OffsetMinY = "-56"
                };
                foreach (PresetConfig preset in _config.Npc) foreach (NpcBelt belt in preset.Config.BeltItems) belt.Ammo = string.Empty;
            }
            if (_config.PluginVersion < new VersionNumber(2, 0, 8))
            {
                _config.Commands = new HashSet<string>
                {
                    "/remove",
                    "remove.toggle"
                };
            }
            if (_config.PluginVersion < new VersionNumber(2, 1, 0))
            {
                _config.Radius = 90f;
            }
            if (_config.PluginVersion < new VersionNumber(2, 1, 4))
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
            if (_config.PluginVersion < new VersionNumber(2, 1, 7))
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
                    Text = "SatDishEvent"
                };
            }
            if (_config.PluginVersion < new VersionNumber(2, 1, 9))
            {
                _config.PveMode.ScaleDamage = new Dictionary<string, float>
                {
                    ["Npc"] = 1f,
                    ["Bradley"] = 2f
                };
            }
            if (_config.PluginVersion < new VersionNumber(2, 2, 1))
            {
                _config.Chat = new ChatConfig
                {
                    IsChat = true,
                    Prefix = "[SatDishEvent]"
                };
                _config.DistanceAlerts = 0f;
                _config.Notify.Type = 0;
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
            [JsonProperty(En ? "Position" : "Позиция")] public string Position { get; set; }
            [JsonProperty(En ? "Rotation" : "Вращение")] public string Rotation { get; set; }
            [JsonProperty(En ? "Loot table from prefabs (if the loot table type is 4 or 5)" : "Таблица предметов из prefab-ов (если тип таблицы предметов - 4 или 5)")] public PrefabLootTableConfig PrefabLootTable { get; set; }
            [JsonProperty(En ? "Own loot table (if the loot table type is 1 or 5)" : "Собственная таблица предметов (если тип таблицы предметов - 1 или 5)")] public LootTableConfig OwnLootTable { get; set; }
        }

        public class HackCrateConfig
        {
            [JsonProperty(En ? "Time to unlock the Crates [sec.]" : "Время разблокировки ящиков [sec.]")] public float UnlockTime { get; set; }
            [JsonProperty(En ? "Increase the event time if it's not enough to unlock the locked crate? [true/false]" : "Увеличивать время ивента, если недостаточно чтобы разблокировать заблокированный ящик? [true/false]")] public bool IncreaseEventTime { get; set; }
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
            [JsonProperty(En ? "Killing an Zombie" : "Убийство зомби")] public double Zombie { get; set; }
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
            [JsonProperty(En ? "Can an Npc attack a non-owner of the event? [true/false]" : "Может ли Npc атаковать не владельца ивента? [true/false]")] public bool TargetNpc { get; set; }
            [JsonProperty(En ? "Can Bradley attack a non-owner of the event? [true/false]" : "Может ли Bradley атаковать не владельца ивента? [true/false]")] public bool TargetTank { get; set; }
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

        public class BradleyConfig
        {
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

        public class ZombieConfig
        {
            [JsonProperty(En ? "Health" : "Кол-во ХП")] public float Hp { get; set; }
            [JsonProperty(En ? "Movement speed" : "Скорость движения")] public float Speed { get; set; }
            [JsonProperty(En ? "Remove a corpse after death? (it is recommended to use the true value to improve performance) [true/false]" : "Удалять труп после смерти? (рекомендуется использовать значение true для повышения производительности) [true/false]")] public bool IsRemoveCorpse { get; set; }
            [JsonProperty(En ? "Which loot table should the plugin use? (0 - default; 1 - own; 2 - AlphaLoot; 3 - CustomLoot; 4 - loot table of the Rust objects; 5 - combine the 1 and 4 methods)" : "Какую таблицу лута необходимо использовать? (0 - стандартную; 1 - собственную; 2 - AlphaLoot; 3 - CustomLoot; 4 - таблица предметов объектов Rust; 5 - совместить 1 и 4 методы)")] public int TypeLootTable { get; set; }
            [JsonProperty(En ? "Loot table from prefabs (if the loot table type is 4 or 5)" : "Таблица предметов из prefab-ов (если тип таблицы предметов - 4 или 5)")] public PrefabLootTableConfig PrefabLootTable { get; set; }
            [JsonProperty(En ? "Own loot table (if the loot table type is 1 or 5)" : "Собственная таблица предметов (если тип таблицы предметов - 1 или 5)")] public LootTableConfig OwnLootTable { get; set; }
        }

        private class PluginConfig
        {
            [JsonProperty(En ? "Minimum time between events [sec.]" : "Минимальное время между ивентами [sec.]")] public float MinStartTime { get; set; }
            [JsonProperty(En ? "Maximum time between events [sec.]" : "Максимальное время между ивентами [sec.]")] public float MaxStartTime { get; set; }
            [JsonProperty(En ? "Is active the timer on to start the event? [true/false]" : "Активен ли таймер для запуска ивента? [true/false]")] public bool EnabledTimer { get; set; }
            [JsonProperty(En ? "Duration of the event [sec.]" : "Время проведения ивента [sec.]")] public int FinishTime { get; set; }
            [JsonProperty(En ? "Time before the starting of the event after receiving a chat message [sec.]" : "Время до начала ивента после сообщения в чате [sec.]")] public float PreStartTime { get; set; }
            [JsonProperty(En ? "Notification time until the end of the event [sec.]" : "Время оповещения до окончания ивента [sec.]")] public int PreFinishTime { get; set; }
            [JsonProperty(En ? "Which loot table should the plugin use in the crates? (0 - default; 1 - own; 2 - AlphaLoot; 3 - CustomLoot; 4 - loot table of the Rust objects; 5 - combine the 1 and 4 methods)" : "Какую таблицу лута необходимо использовать в ящиках? (0 - стандартную; 1 - собственную; 2 - AlphaLoot; 3 - CustomLoot; 4 - таблица предметов объектов Rust; 5 - совместить 1 и 4 методы)")] public int TypeLootTableCrates { get; set; }
            [JsonProperty(En ? "Crates setting" : "Настройка ящиков")] public HashSet<CrateConfig> DefaultCrates { get; set; }
            [JsonProperty(En ? "Locked crate setting" : "Настройка заблокированного ящика")] public HackCrateConfig HackCrate { get; set; }
            [JsonProperty(En ? "NPCs setting" : "Настройка NPC")] public HashSet<PresetConfig> Npc { get; set; }
            [JsonProperty(En ? "Does an additional Bradley appear at the beginning of the event? [true/false]" : "Появляется дополнительный Bradley в начале ивента? [true/false]")] public bool IsAdditionalBradley { get; set; }
            [JsonProperty(En ? "Bradley setting" : "Настройка танка")] public BradleyConfig Bradley { get; set; }
            [JsonProperty(En ? "Zombies setting" : "Настройка зомби")] public ZombieConfig Zombies { get; set; }
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
            [JsonProperty(En ? "Interrupt the teleport in Satellite Dish? (only for users NTeleportation plugin) [true/false]" : "Запрещать телепорт на спутниковых тарелках? (только для тех, кто использует плагин NTeleportation) [true/false]")] public bool NTeleportationInterrupt { get; set; }
            [JsonProperty(En ? "Disable NPCs from the BetterNpc plugin on the monument while the event is on? [true/false]" : "Отключать NPC из плагина BetterNpc на монументе пока проходит ивент? [true/false]")] public bool RemoveBetterNpc { get; set; }
            [JsonProperty(En ? "Economy setting (total values will be added up and rewarded at the end of the event)" : "Настройка экономики (конечное значение суммируется и будет выдано игрокам по окончанию ивента)")] public EconomyConfig Economy { get; set; }
            [JsonProperty(En ? "List of commands banned in the event zone" : "Список команд запрещенных в зоне ивента")] public HashSet<string> Commands { get; set; }
            [JsonProperty(En ? "The CCTV camera" : "Название камеры")] public string Cctv { get; set; }
            [JsonProperty(En ? "Can SAM Site turrets appear in the event zone? [true/false]" : "Должны ли появляться Sam Site турели в зоне ивента? [true/false]")] public bool IsSamSites { get; set; }
            [JsonProperty(En ? "Delayed departure of CH47 after the start of the event [sec.]" : "Задержка вылета CH47 после начала ивента [sec.]")] public float DelayCh47 { get; set; }
            [JsonProperty(En ? "Flight altitude CH47 [m.]" : "Высота полета CH47 [m.]")] public float HeightCh47 { get; set; }
            [JsonProperty(En ? "Plane flight speed multiplier" : "Множитель скорости полета самолета")] public float ScaleSpeedPlane { get; set; }
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
                    TypeLootTableCrates = 0,
                    DefaultCrates = new HashSet<CrateConfig>
                    {
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_elite.prefab",
                            Position = "(-4.393, 5.991, -7.11)",
                            Rotation = "(0, 335.197, 0)",
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 50.0f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_elite.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50.0f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_elite.prefab",
                            Position = "(2.995, 6.069, -18.281)",
                            Rotation = "(0, 0, 0)",
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 50.0f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_elite.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50.0f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_normal.prefab",
                            Position = "(-14.655, 6.245, -37.951)",
                            Rotation = "(0, 29.72, 0)",
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 50.0f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_normal.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50.0f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_normal.prefab",
                            Position = "(-5.894, 6.855, -25.436)",
                            Rotation = "(0, 233.92, 326.004)",
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 50.0f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_normal.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50.0f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_normal.prefab",
                            Position = "(-64.114, 0.089, -44.697)",
                            Rotation = "(0, 60.775, 0)",
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 50.0f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_normal.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50.0f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_normal_2.prefab",
                            Position = "(0.022, 6.069, -18.171)",
                            Rotation = "(0, 0, 0)",
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 50.0f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_normal_2.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50.0f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_normal_2.prefab",
                            Position = "(-12.004, 5.893, -29.853)",
                            Rotation = "(0, 32.067, 0)",
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 50.0f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_normal_2.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50.0f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/crate_medical.prefab",
                            Position = "(-63.339, 1.263, -38.308)",
                            Rotation = "(5.213, 0, 0)",
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 50.0f, PrefabDefinition = "assets/bundled/prefabs/radtown/underwater_labs/crate_medical.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50.0f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/crate_medical.prefab",
                            Position = "(-7.506, 7.041, -23.058)",
                            Rotation = "(5.335, 323.221, 0)",
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 50.0f, PrefabDefinition = "assets/bundled/prefabs/radtown/underwater_labs/crate_medical.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50.0f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/crate_medical.prefab",
                            Position = "(2.758, 7.284, -7.781)",
                            Rotation = "(0, 0, 0)",
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 50.0f, PrefabDefinition = "assets/bundled/prefabs/radtown/underwater_labs/crate_medical.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50.0f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/crate_ammunition.prefab",
                            Position = "(-0.005, 7.284, -8.157)",
                            Rotation = "(0, 0, 0)",
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 50.0f, PrefabDefinition = "assets/bundled/prefabs/radtown/underwater_labs/crate_ammunition.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50.0f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/crate_ammunition.prefab",
                            Position = "(-30.254, 6.932, -40.082)",
                            Rotation = "(0, 0, 0)",
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 50.0f, PrefabDefinition = "assets/bundled/prefabs/radtown/underwater_labs/crate_ammunition.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50.0f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/tech_parts_2.prefab",
                            Position = "(0.433, 7.284, -6.215)",
                            Rotation = "(0, 0, 0)",
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 50.0f, PrefabDefinition = "assets/bundled/prefabs/radtown/underwater_labs/tech_parts_2.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50.0f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/tech_parts_1.prefab",
                            Position = "(-60.971, 1.265, -43.093)",
                            Rotation = "(2.554, 355.733, 12.007)",
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 50.0f, PrefabDefinition = "assets/bundled/prefabs/radtown/underwater_labs/tech_parts_1.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50.0f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/crate_tools.prefab",
                            Position = "(-27.461, 5.893, -43.416)",
                            Rotation = "(0, 58.631, 0)",
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 50.0f, PrefabDefinition = "assets/bundled/prefabs/radtown/underwater_labs/crate_tools.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50.0f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/crate_normal_2_medical.prefab",
                            Position = "(-10.263, 6.054, -18.183)",
                            Rotation = "(0, 0, 0)",
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 50.0f, PrefabDefinition = "assets/bundled/prefabs/radtown/crate_normal_2_medical.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50.0f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/crate_food_1.prefab",
                            Position = "(9.189, 6.067, -4.821)",
                            Rotation = "(0, 80.919, 0)",
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 50.0f, PrefabDefinition = "assets/bundled/prefabs/radtown/underwater_labs/crate_food_1.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50.0f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/crate_food_1.prefab",
                            Position = "(11.024, 6.067, -4.711)",
                            Rotation = "(0, 16.709, 0)",
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 50.0f, PrefabDefinition = "assets/bundled/prefabs/radtown/underwater_labs/crate_food_1.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50.0f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/crate_food_1.prefab",
                            Position = "(8.289, 6.067, -18.256)",
                            Rotation = "(0, 322.681, 0)",
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 50.0f, PrefabDefinition = "assets/bundled/prefabs/radtown/underwater_labs/crate_food_1.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50.0f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/crate_food_2.prefab",
                            Position = "(7.022, 6.067, -9.697)",
                            Rotation = "(0, 15.952, 0)",
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 50.0f, PrefabDefinition = "assets/bundled/prefabs/radtown/underwater_labs/crate_food_2.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50.0f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/crate_food_2.prefab",
                            Position = "(11.098, 6.067, -11.957)",
                            Rotation = "(0, 15.952, 0)",
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 50.0f, PrefabDefinition = "assets/bundled/prefabs/radtown/underwater_labs/crate_food_2.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50.0f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        },
                        new CrateConfig
                        {
                            Prefab = "assets/bundled/prefabs/radtown/underwater_labs/crate_food_2.prefab",
                            Position = "(11.102, 6.067, -15.587)",
                            Rotation = "(0, 334.754, 0)",
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 50.0f, PrefabDefinition = "assets/bundled/prefabs/radtown/underwater_labs/crate_food_2.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1,
                                Max = 1,
                                UseCount = true,
                                Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50.0f, IsBluePrint = false, SkinId = 0, Name = "" } }
                            }
                        }
                    },
                    HackCrate = new HackCrateConfig
                    {
                        UnlockTime = 600f,
                        IncreaseEventTime = true,
                        TypeLootTable = 0,
                        PrefabLootTable = new PrefabLootTableConfig
                        {
                            Min = 1,
                            Max = 1,
                            UseCount = true,
                            Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 50.0f, PrefabDefinition = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab" } }
                        },
                        OwnLootTable = new LootTableConfig
                        {
                            Min = 1,
                            Max = 1,
                            UseCount = true,
                            Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50.0f, IsBluePrint = false, SkinId = 0, Name = "" } }
                        }
                    },
                    Npc = new HashSet<PresetConfig>
                    {
                        new PresetConfig
                        {
                            Min = 8,
                            Max = 13,
                            Positions = new HashSet<string>
                            {
                                "(-55.8, 1.2, -48.1)",
                                "(-62.8, 0.2, -45.0)",
                                "(-58.3, 0.7, -38.4)",
                                "(-5.8, 6.2, -17.7)",
                                "(-6.2, 6.1, 3.6)",
                                "(-10.6, 6.0, -6.3)",
                                "(-35.8, 6.0, -7.0)",
                                "(-67.5, 6.1, -18.8)",
                                "(-67.6, 6.1, 4.2)",
                                "(-32.8, 0.2, 20.1)",
                                "(-26.1, 0.0, 7.1)",
                                "(-18.8, 0.0, 15.0)",
                                "(31.1, 0.0, -1.7)",
                                "(33.1, 0.0, 11.7)",
                                "(22.3, 0.0, 14.1)",
                                "(26.7, 0.0, -21.4)",
                                "(17.6, 0.0, -29.1)",
                                "(35.6, -0.1, -27.9)",
                                "(42.4, 6.0, -7.1)",
                                "(67.4, 6.1, 4.5)",
                                "(67.7, 6.1, -18.5)",
                                "(-33.1, 0.5, -56.6)",
                                "(-11.5, 0.8, -52.6)",
                                "(3.5, 0.4, -41.8)",
                                "(-32.0, 0.0, -16.0)",
                                "(-22.0, 0.0, -24.9)",
                                "(-38.2, 0.1, -29.1)",
                                "(-26.7, 6.1, 50.5)",
                                "(-4.8, 6.1, 56.3)",
                                "(-3.1, 6.1, 33.0)"
                            },
                            Config = new NpcConfig
                            {
                                Name = "Scientist",
                                Health = 150f,
                                RoamRange = 8f,
                                ChaseRange = 100f,
                                AttackRangeMultiplier = 2f,
                                SenseRange = 85f,
                                MemoryDuration = 30f,
                                DamageScale = 0.4f,
                                AimConeScale = 1f,
                                CheckVisionCone = false,
                                VisionCone = 135f,
                                Speed = 7.5f,
                                DisableRadio = false,
                                Stationary = false,
                                IsRemoveCorpse = true,
                                WearItems = new HashSet<NpcWear>
                                {
                                    new NpcWear { ShortName = "hoodie", SkinId = 2187105866 },
                                    new NpcWear { ShortName = "shoes.boots", SkinId = 0 },
                                    new NpcWear { ShortName = "sunglasses", SkinId = 0 },
                                    new NpcWear { ShortName = "pants", SkinId = 2187107432 }
                                },
                                BeltItems = new HashSet<NpcBelt>
                                {
                                    new NpcBelt { ShortName = "pistol.m92", Amount = 1, SkinId = 0, Mods = new HashSet<string> { "weapon.mod.flashlight" }, Ammo = string.Empty },
                                    new NpcBelt { ShortName = "syringe.medical", Amount = 5, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty },
                                    new NpcBelt { ShortName = "grenade.f1", Amount = 1, SkinId = 0, Mods = new HashSet<string>(), Ammo = string.Empty }
                                },
                                Kit = ""
                            },
                            TypeLootTable = 5,
                            PrefabLootTable = new PrefabLootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_oilrig.prefab" } }
                            },
                            OwnLootTable = new LootTableConfig
                            {
                                Min = 1, Max = 1, UseCount = true,
                                Items = new List<ItemConfig>
                                {
                                    new ItemConfig { ShortName = "scrap", MinAmount = 5, MaxAmount = 10, Chance = 50f, IsBluePrint = false, SkinId = 0, Name = "" },
                                    new ItemConfig { ShortName = "syringe.medical", MinAmount = 1, MaxAmount = 2, Chance = 70.0f, IsBluePrint = false, SkinId = 0, Name = "" }
                                }
                            }
                        }
                    },
                    IsAdditionalBradley = true,
                    Bradley = new BradleyConfig
                    {
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
                            Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 50.0f, PrefabDefinition = "assets/prefabs/npc/m2bradley/bradley_crate.prefab" } }
                        },
                        OwnLootTable = new LootTableConfig
                        {
                            Min = 1,
                            Max = 1,
                            UseCount = true,
                            Items = new List<ItemConfig> { new ItemConfig { ShortName = "scrap", MinAmount = 100, MaxAmount = 200, Chance = 50.0f, IsBluePrint = false, SkinId = 0, Name = "" } }
                        }
                    },
                    Zombies = new ZombieConfig
                    {
                        Hp = 200f,
                        Speed = 1f,
                        IsRemoveCorpse = true,
                        TypeLootTable = 5,
                        PrefabLootTable = new PrefabLootTableConfig
                        {
                            Min = 1,
                            Max = 1,
                            UseCount = true,
                            Prefabs = new List<PrefabConfig> { new PrefabConfig { Chance = 100f, PrefabDefinition = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab" } }
                        },
                        OwnLootTable = new LootTableConfig
                        {
                            Min = 1,
                            Max = 1,
                            UseCount = true,
                            Items = new List<ItemConfig>
                            {
                                new ItemConfig { ShortName = "scrap", MinAmount = 5, MaxAmount = 10, Chance = 50f, IsBluePrint = false, SkinId = 0, Name = "" },
                                new ItemConfig { ShortName = "syringe.medical", MinAmount = 1, MaxAmount = 2, Chance = 70.0f, IsBluePrint = false, SkinId = 0, Name = "" }
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
                        Text = "SatDishEvent"
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
                        Prefix = "[SatDishEvent]"
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
                            "StartDeal",
                            "TakeCH47",
                            "BrokeDeal",
                            "AnswerPhone",
                            "CallReinforcement",
                            "OpenLockedCrate",
                            "KillBradley"
                        }
                    },
                    Radius = 90f,
                    IsCreateZonePvp = false,
                    PveMode = new PveModeConfig
                    {
                        Pve = false,
                        Damage = 500f,
                        ScaleDamage = new Dictionary<string, float> { ["Npc"] = 1f, ["Bradley"] = 2f },
                        LootCrate = false,
                        HackCrate = false,
                        LootNpc = false,
                        DamageNpc = false,
                        DamageTank = false,
                        TargetNpc = false,
                        TargetTank = false,
                        CanEnter = false,
                        CanEnterCooldownPlayer = true,
                        TimeExitOwner = 300,
                        AlertTime = 60,
                        RestoreUponDeath = true,
                        CooldownOwner = 86400,
                        Darkening = 12
                    },
                    NTeleportationInterrupt = false,
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
                            ["crate_medical"] = 0.1,
                            ["crate_ammunition"] = 0.2,
                            ["tech_parts_2"] = 0.1,
                            ["tech_parts_1"] = 0.1,
                            ["crate_tools"] = 0.1,
                            ["crate_normal_2_medical"] = 0.1,
                            ["crate_food_1"] = 0.1,
                            ["crate_food_2"] = 0.1
                        },
                        Bradley = 0.8,
                        Npc = 0.3,
                        LockedCrate = 0.5,
                        Zombie = 0.4,
                        Commands = new HashSet<string>()
                    },
                    Commands = new HashSet<string>
                    {
                        "/remove",
                        "remove.toggle"
                    },
                    Cctv = "SatDish",
                    IsSamSites = true,
                    DelayCh47 = 0f,
                    HeightCh47 = 200f,
                    ScaleSpeedPlane = 4f,
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
                ["PreStart"] = "{0} The Biological Weapons Transaction will begin at the <color=#55aaff>Satellite Dish</color> location in <color=#55aaff>{1}</color>!",
                ["Start"] = "{0} The Chinook <color=#738d43>has flown out</color> to grid <color=#55aaff>{1}</color> in order to pick up prototypes for the bioweapons transaction!\nCCTV: <color=#55aaff>{2}</color>",
                ["PreFinish"] = "{0} The Biological Weapons Transaction <color=#ce3f27>will end</color> in <color=#55aaff>{1}</color>!",
                ["Finish"] = "{0} The Biological Weapons Transaction <color=#ce3f27>has concluded</color>!",
                ["StartDeal"] = "{0} The Biological Weapons Transaction <color=#738d43>has begun</color>! Chinook <color=#738d43>has dropped</color> the locked crate and <color=#738d43>started loading</color> prototypes onto the Chinook!",
                ["TakeCH47"] = "{0} The Chinook was able to obtain <color=#55aaff>{1}</color> biological prototypes!",
                ["BrokeDeal"] = "{0} <color=#55aaff>{1}</color> has disturbed The Biological Weapons Transaction! You have to answer the phone, otherwise reinforcements will be sent into the Event Zone.",
                ["AnswerPhone"] = "{0} <color=#55aaff>{1}</color> answered the phone call! Reinforcements will not be sent as we were able to fake the all clear!",
                ["CallReinforcement"] = "{0} Nobody has answered the phone! Reinforcements will arrive to the <color=#55aaff>Satellite Dish</color> soon! The plane is already on its way to the island",
                ["OpenLockedCrate"] = "{0} <color=#55aaff>{1}</color> <color=#738d43>has started hacking</color> the locked crate!",
                ["KillBradley"] = "{0} <color=#55aaff>{1}</color> <color=#738d43>destroyed</color> the tank!",
                ["SetOwner"] = "{0} Player <color=#55aaff>{1}</color> <color=#738d43>has received</color> the owner status for the <color=#55aaff>Satellite Dish Event</color>",
                ["EventActive"] = "{0} This event is active. To finish this event (<color=#55aaff>/satdishstop</color>), then (<color=#55aaff>/satdishstart</color> to start the next one!",
                ["EnterPVP"] = "{0} You <color=#ce3f27>have entered</color> the PVP zone, now other players <color=#ce3f27>can damage</color> you!",
                ["ExitPVP"] = "{0} You <color=#738d43>have left</color> the PVP zone, now other players <color=#738d43>cannot damage</color> you!",
                ["NTeleportation"] = "{0} You <color=#ce3f27>cannot</color> teleport into the Event Zone!",
                ["SendEconomy"] = "{0} You <color=#738d43>have earned</color> <color=#55aaff>{1}</color> points in economics for participating in the event",
                ["NoCommand"] = "{0} You <color=#ce3f27>cannot</color> use this command in the event zone!"
            }, this);

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["PreStart"] = "{0} Через <color=#55aaff>{1}</color> в локации <color=#55aaff>Спутниковые тарелки</color> начнется сделка по продаже биологического оружия!",
                ["Start"] = "{0} На сделку по продаже биологического оружия <color=#738d43>вылетел</color> CH47 в квадрат <color=#55aaff>{1}</color>, чтобы забрать опытные образцы!\nКамера: <color=#55aaff>{2}</color>",
                ["PreFinish"] = "{0} Сделка по продаже биологического оружия <color=#ce3f27>закончится</color> через <color=#55aaff>{1}</color>!",
                ["Finish"] = "{0} Сделка по продаже биологического оружия <color=#ce3f27>закончена</color>!",
                ["StartDeal"] = "{0} Сделка по продаже биологического оружия <color=#738d43>началась</color>! CH47 <color=#738d43>скинул</color> заблокированный ящик и <color=#738d43>начал погрузку</color> опытных образцов к себе на борт",
                ["TakeCH47"] = "{0} CH47 удалось забрать <color=#55aaff>{1}</color> опытных образцов!",
                ["BrokeDeal"] = "{0} <color=#55aaff>{1}</color> сорвал сделку по продаже биологического оружия! Необходимо ответить на телефонный звонок, иначе в зону ивента прибудет подкрепление",
                ["AnswerPhone"] = "{0} <color=#55aaff>{1}</color> ответил на телефонный звонок! Вызов подкрепления отменен",
                ["CallReinforcement"] = "{0} Никто не ответил на телефонный звонок! В локацию <color=#55aaff>Спутниковые тарелки</color> скоро прибудет подкрепление! Самолет уже вылетел к острову",
                ["OpenLockedCrate"] = "{0} <color=#55aaff>{1}</color> <color=#738d43>начал</color> взлом заблокированного ящика!",
                ["KillBradley"] = "{0} <color=#55aaff>{1}</color> <color=#738d43>уничтожил</color> танк!",
                ["SetOwner"] = "{0} Игрок <color=#55aaff>{1}</color> <color=#738d43>получил</color> статус владельца ивента для <color=#55aaff>Satellite Dish Event</color>",
                ["EventActive"] = "{0} Ивент в данный момент активен, сначала завершите текущий ивент (<color=#55aaff>/satdishstop</color>), чтобы начать следующий!",
                ["EnterPVP"] = "{0} Вы <color=#ce3f27>вошли</color> в PVP зону, теперь другие игроки <color=#ce3f27>могут</color> наносить вам урон!",
                ["ExitPVP"] = "{0} Вы <color=#738d43>вышли</color> из PVP зоны, теперь другие игроки <color=#738d43>не могут</color> наносить вам урон!",
                ["NTeleportation"] = "{0} Вы <color=#ce3f27>не можете</color> телепортироваться в зоне ивента!",
                ["SendEconomy"] = "{0} Вы <color=#738d43>получили</color> <color=#55aaff>{1}</color> баллов в экономику за прохождение ивента",
                ["NoCommand"] = "{0} Вы <color=#ce3f27>не можете</color> использовать данную команду в зоне ивента!"
            }, this, "ru");
        }

        private string GetMessage(string langKey, string userId) => lang.GetMessage(langKey, _ins, userId);

        private string GetMessage(string langKey, string userId, params object[] args) => (args.Length == 0) ? GetMessage(langKey, userId) : string.Format(GetMessage(langKey, userId), args);
        #endregion Lang

        #region Oxide Hooks
        private static SatDishEvent _ins;

        private void Init()
        {
            _ins = this;
            ToggleHooks(false);
        }

        private void OnServerInitialized()
        {
            if (GetMonument() == null)
            {
                PrintError("The Satellite Dish location is missing on the map. The plugin cannot be loaded!");
                NextTick(() => Interface.Oxide.UnloadPlugin(Name));
                return;
            }
            CheckAllLootTables();
            LoadSound();
            ServerMgr.Instance.StartCoroutine(DownloadImages());
            StartTimer();
        }

        private void Unload()
        {
            if (Controller != null) Finish();
            if (PlayCoroutine != null) ServerMgr.Instance.StopCoroutine(PlayCoroutine);
            _ins = null;
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null) return null;

            if (Controller.Entities.Contains(entity)) return true;

            if (entity is ScientistNPC)
            {
                ScientistNPC npc = entity as ScientistNPC;
                if (Controller.Zombies.Contains(npc) && !Controller.IsEvacuation) return true;
            }

            if (entity is BradleyAPC)
            {
                BradleyAPC bradley = entity as BradleyAPC;
                if (bradley == Controller.Bradley && !bradley.myRigidBody.useGravity) return true;
            }

            if (entity is CH47Helicopter && entity == Controller.Ch47) return true;

            if (entity is BasePlayer && entity == Controller.Dummy) return true;

            if (entity is Telephone && entity == Controller.Phone) return true;

            BradleyAPC attacker = info.Initiator as BradleyAPC;
            if (attacker != null && (attacker == Controller.Bradley || attacker == Controller.AddBradley)) info.damageTypes.ScaleAll(_config.Bradley.ScaleDamage);

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

        private void OnSupplyDropDropped(SupplyDrop supplyDrop, CargoPlane cargoPlane)
        {
            if (supplyDrop == null || cargoPlane == null) return;
            if (cargoPlane != Controller.Plane) return;
            Controller.BradleyCoroutine = ServerMgr.Instance.StartCoroutine(Controller.ProcessBradley(supplyDrop.transform.position.y));
            if (supplyDrop.IsExists()) supplyDrop.Kill();
            Unsubscribe("OnSupplyDropDropped");
        }

        private Dictionary<ulong, BasePlayer> StartHackCrates { get; } = new Dictionary<ulong, BasePlayer>();

        private void CanHackCrate(BasePlayer player, HackableLockedCrate crate)
        {
            if (player == null || crate == null) return;
            if (crate == Controller.HackCrate)
            {
                ulong crateId = crate.net.ID.Value;
                if (StartHackCrates.ContainsKey(crateId)) StartHackCrates[crateId] = player;
                else StartHackCrates.Add(crateId, player);
            }
        }

        private void OnCrateHack(HackableLockedCrate crate)
        {
            if (crate == null) return;
            ulong crateId = crate.net.ID.Value;
            BasePlayer player;
            if (StartHackCrates.TryGetValue(crateId, out player))
            {
                StartHackCrates.Remove(crateId);
                if (_config.HackCrate.IncreaseEventTime && Controller.TimeToFinish < (int)_config.HackCrate.UnlockTime) Controller.TimeToFinish += (int)_config.HackCrate.UnlockTime;
                ActionEconomy(player.userID, "LockedCrate");
                AlertToAllPlayers("OpenLockedCrate", _config.Chat.Prefix, player.displayName);
                Unsubscribe("CanHackCrate");
                Unsubscribe("OnCrateHack");
            }
        }

        private object CanBradleyApcTarget(BradleyAPC bradley, BaseEntity entity)
        {
            if (bradley == null || entity == null) return null;
            if (bradley == Controller.Bradley || bradley == Controller.AddBradley)
            {
                if (bradley == Controller.Bradley && !bradley.myRigidBody.useGravity) return false;
                if ((entity as BasePlayer).IsPlayer()) return null;
                else return false;
            }
            else return null;
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
            BasePlayer attacker = info.InitiatorPlayer;
            if (bradley == Controller.Bradley)
            {
                if (attacker != null)
                {
                    ActionEconomy(attacker.userID, "Bradley");
                    AlertToAllPlayers("KillBradley", _config.Chat.Prefix, attacker.displayName);
                }
                if (Controller.TimeToFinish > _config.PreFinishTime)
                {
                    if (_config.HackCrate.IncreaseEventTime && Controller.HackCrate != null && Controller.HackCrate.IsBeingHacked()) Controller.TimeToFinish = _config.PreFinishTime + (int)(HackableLockedCrate.requiredHackSeconds - Controller.HackCrate.hackSeconds);
                    else Controller.TimeToFinish = _config.PreFinishTime;
                }
            }
            else if (bradley == Controller.AddBradley && attacker != null) ActionEconomy(attacker.userID, "Bradley");
        }

        private void OnEntityDeath(ScientistNPC npc, HitInfo info)
        {
            if (npc == null || info == null) return;
            BasePlayer attacker = info.InitiatorPlayer;
            if (attacker == null) return;
            if (Controller.Zombies.Contains(npc))
            {
                ActionEconomy(attacker.userID, "Zombie");
                if (!Controller.IsAlarm)
                {
                    Controller.IsAlarm = true;
                    AlertToAllPlayers("BrokeDeal", _config.Chat.Prefix, attacker.displayName);
                    Controller.Alarm.UpdateFromInput(1, 0);
                    Controller.Siren.UpdateFromInput(1, 0);
                    Controller.CallPhone();
                }
            }
            else if (Controller.Scientists.Contains(npc)) ActionEconomy(attacker.userID, "Npc");
        }

        private object CanMountEntity(BasePlayer player, BaseMountable entity)
        {
            if (entity == null || player == null) return null;
            BaseEntity parent = entity.GetParentEntity();
            if (parent == null) return null;
            if ((Controller.Entities.Contains(parent) || parent == Controller.Ch47) && Controller.Players.Contains(player)) return true;
            if (parent == Controller.Ch47 && player.IsPlayer())
            {
                if (Controller.Zombies.Count == 0)
                {
                    if (Controller.Ch47Coroutine != null) ServerMgr.Instance.StopCoroutine(Controller.Ch47Coroutine);
                    Controller.Ch47.rigidBody.detectCollisions = true;
                    Controller.Ch47Ai.ClearLandingTarget();
                    Controller.Ch47 = null;
                    Controller.Ch47Ai = null;
                }
                else
                {
                    if (Controller.Ch47Coroutine != null) ServerMgr.Instance.StopCoroutine(Controller.Ch47Coroutine);
                    foreach (Door door in Controller.Doors) door.SetOpen(true);
                    Controller.IsEvacuation = true;
                    Controller.Ch47.rigidBody.detectCollisions = true;
                    Controller.Ch47Ai.ClearLandingTarget();
                    Controller.Ch47 = null;
                    Controller.Ch47Ai = null;
                    Controller.IsAlarm = true;
                    AlertToAllPlayers("BrokeDeal", _config.Chat.Prefix, player.displayName);
                    Controller.Alarm.UpdateFromInput(1, 0);
                    Controller.Siren.UpdateFromInput(1, 0);
                    Controller.CallPhone();
                }
            }
            return null;
        }

        private object OnNpcTarget(ScientistNPC npc, BaseEntity entity)
        {
            if (npc == null || entity == null) return null;
            if (Controller.Zombies.Contains(npc)) return true;
            else return null;
        }

        private object OnNpcTarget(BaseEntity npc, BasePlayer entity)
        {
            if (npc == null || entity == null) return null;
            if (entity == Controller.Dummy || Controller.Zombies.Contains(entity as ScientistNPC)) return true;
            else return null;
        }

        private void OnPhoneAnswered(PhoneController receiverPhone, PhoneController callerPhone)
        {
            if (receiverPhone == null || callerPhone == null) return;
            if (receiverPhone == Controller.PhoneMonument.Controller && callerPhone == Controller.Phone.Controller)
            {
                PlayCoroutine = ServerMgr.Instance.StartCoroutine(PlaySoundToPlayer(receiverPhone.currentPlayer));
                string name = receiverPhone.currentPlayer.displayName;
                timer.In(10f, () =>
                {
                    receiverPhone.SetPhoneStateWithPlayer(Telephone.CallState.Idle);
                    Controller.Alarm.UpdateFromInput(0, 0);
                    Controller.Siren.UpdateFromInput(0, 0);
                    AlertToAllPlayers("AnswerPhone", _config.Chat.Prefix, name);
                    if (Controller.TimeToFinish > _config.PreFinishTime)
                    {
                        if (_config.HackCrate.IncreaseEventTime && Controller.HackCrate != null && Controller.HackCrate.IsBeingHacked()) Controller.TimeToFinish = _config.PreFinishTime + (int)(HackableLockedCrate.requiredHackSeconds - Controller.HackCrate.hackSeconds);
                        else Controller.TimeToFinish = _config.PreFinishTime;
                    }
                });
            }
        }

        private void OnPhoneDialTimedOut(PhoneController callerPhone, PhoneController receiverPhone, BasePlayer player)
        {
            if (callerPhone == null || receiverPhone == null) return;
            if (receiverPhone == Controller.PhoneMonument.Controller && callerPhone == Controller.Phone.Controller)
            {
                Controller.SpawnPlane();
                AlertToAllPlayers("CallReinforcement", _config.Chat.Prefix);
            }
        }

        private object OnPhoneDial(PhoneController callerPhone, PhoneController receiverPhone, BasePlayer player)
        {
            if (callerPhone == null || receiverPhone == null) return null;
            if (callerPhone == Controller.Phone.Controller && receiverPhone == Controller.PhoneMonument.Controller) return null;
            if (callerPhone == Controller.PhoneMonument.Controller || receiverPhone == Controller.PhoneMonument.Controller) return true;
            else return null;
        }

        private object OnEntityKill(BaseEntity entity)
        {
            if (entity == null || Controller == null) return null;

            if (!Controller.KillEntities)
            {
                if (Controller.Entities.Contains(entity)) return true;
                if (entity is Telephone && entity == Controller.Phone) return true;
            }

            if (entity is LootContainer)
            {
                LootContainer container = entity as LootContainer;
                if (Controller.Crates.Contains(container)) Controller.Crates.Remove(container);
            }

            return null;
        }

        private HashSet<ulong> LootableCrates { get; } = new HashSet<ulong>();

        private void OnLootEntity(BasePlayer player, LootContainer container)
        {
            if (player == null || container == null || LootableCrates.Contains(container.net.ID.Value)) return;
            if (Controller.Crates.Contains(container))
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
        internal HashSet<Prefab> Prefabs { get; } = new HashSet<Prefab>
        {
            //sedantest.entity
            new Prefab { Path = "assets/content/vehicles/sedan_a/sedantest.entity.prefab", Pos = new Vector3(-64.318f, 0.114f, -39.859f), Rot = new Vector3(359.604f, 212.535f, 359.278f) },
            new Prefab { Path = "assets/content/vehicles/sedan_a/sedantest.entity.prefab", Pos = new Vector3(-58.631f, 0.55f, -44.455f), Rot = new Vector3(8.012f, 301.821f, 5.017f) },
            new Prefab { Path = "assets/content/vehicles/sedan_a/sedantest.entity.prefab", Pos = new Vector3(-28.056f, 5.828f, -39.16f), Rot = new Vector3(0f, 247.503f, 0f) },
            new Prefab { Path = "assets/content/vehicles/sedan_a/sedantest.entity.prefab", Pos = new Vector3(-6.493f, 5.893f, -24.499f), Rot = new Vector3(0f, 143.92f, 0f) },
            //barricade.concrete
            new Prefab { Path = "assets/prefabs/deployable/barricades/barricade.concrete.prefab", Pos = new Vector3(-64.79f, 0.17f, -45.018f), Rot = new Vector3(0f, 61.32f, 0f) },
            new Prefab { Path = "assets/prefabs/deployable/barricades/barricade.concrete.prefab", Pos = new Vector3(-27.891f, 5.882f, -43.674f), Rot = new Vector3(0f, 58.369f, 0f) },
            new Prefab { Path = "assets/prefabs/deployable/barricades/barricade.concrete.prefab", Pos = new Vector3(-11.787f, 5.894f, -31.016f), Rot = new Vector3(0f, 32.462f, 0f) },
            //wall.frame
            new Prefab { Path = "assets/prefabs/building core/wall.frame/wall.frame.prefab", Pos = new Vector3(6.352f, 5.987f, -17.156f), Rot = new Vector3(0f, 0f, 0f) },
            new Prefab { Path = "assets/prefabs/building core/wall.frame/wall.frame.prefab", Pos = new Vector3(6.352f, 5.987f, -14.156f), Rot = new Vector3(0f, 0f, 0f) },
            new Prefab { Path = "assets/prefabs/building core/wall.frame/wall.frame.prefab", Pos = new Vector3(6.352f, 5.987f, -11.156f), Rot = new Vector3(0f, 0f, 0f) },
            new Prefab { Path = "assets/prefabs/building core/wall.frame/wall.frame.prefab", Pos = new Vector3(6.352f, 5.987f, -8.156f), Rot = new Vector3(0f, 0f, 0f) },
            new Prefab { Path = "assets/prefabs/building core/wall.frame/wall.frame.prefab", Pos = new Vector3(6.352f, 5.987f, -5.156f), Rot = new Vector3(0f, 0f, 0f) },
            new Prefab { Path = "assets/prefabs/building core/wall.frame/wall.frame.prefab", Pos = new Vector3(12.144f, 5.987f, -8.44f), Rot = new Vector3(0f, 0f, 0f) },
            new Prefab { Path = "assets/prefabs/building core/wall.frame/wall.frame.prefab", Pos = new Vector3(12.144f, 5.987f, -5.44f), Rot = new Vector3(0f, 0f, 0f) },
            //wall.frame.fence
            new Prefab { Path = "assets/prefabs/building/wall.frame.fence/wall.frame.fence.prefab", Pos = new Vector3(6.352f, 5.987f, -17.156f), Rot = new Vector3(0f, 0f, 0f) },
            new Prefab { Path = "assets/prefabs/building/wall.frame.fence/wall.frame.fence.prefab", Pos = new Vector3(6.352f, 5.987f, -14.156f), Rot = new Vector3(0f, 0f, 0f) },
            new Prefab { Path = "assets/prefabs/building/wall.frame.fence/wall.frame.fence.prefab", Pos = new Vector3(6.352f, 5.987f, -11.156f), Rot = new Vector3(0f, 0f, 0f) },
            new Prefab { Path = "assets/prefabs/building/wall.frame.fence/wall.frame.fence.prefab", Pos = new Vector3(6.352f, 5.987f, -5.156f), Rot = new Vector3(0f, 0f, 0f) },
            new Prefab { Path = "assets/prefabs/building/wall.frame.fence/wall.frame.fence.prefab", Pos = new Vector3(12.144f, 5.987f, -5.44f), Rot = new Vector3(0f, 0f, 0f) },
            //wall.frame.cell.gate
            new Prefab { Path = "assets/prefabs/building/wall.frame.cell/wall.frame.cell.gate.prefab", Pos = new Vector3(12.144f, 5.987f, -8.44f), Rot = new Vector3(0f, 180f, 0f) },
            new Prefab { Path = "assets/prefabs/building/wall.frame.cell/wall.frame.cell.gate.prefab", Pos = new Vector3(6.352f, 5.987f, -8.156f), Rot = new Vector3(0f, 0f, 0f) },
            //cctv_deployed
            new Prefab { Path = "assets/prefabs/deployable/cctvcamera/cctv.static.prefab", Pos = new Vector3(-2.973f, 8.774f, -18.899f), Rot = new Vector3(7.488f, 31.633f, 0f) },
            //electric.sirenlight.deployed
            new Prefab { Path = "assets/prefabs/deployable/playerioents/lights/sirenlight/electric.sirenlight.deployed.prefab", Pos = new Vector3(9.328f, 10.249f, -3.887f), Rot = new Vector3(270f, 0f, 0f) },
            //audioalarm
            new Prefab { Path = "assets/prefabs/deployable/playerioents/alarms/audioalarm.prefab", Pos = new Vector3(9.336f, 9.942f, -3.896f), Rot = new Vector3(0f, 180f, 0f) },
            //searchlight.deployed
            new Prefab { Path = "assets/prefabs/deployable/search light/searchlight.deployed.prefab", Pos = new Vector3(-57.828f, 10.455f, -13.166f), Rot = new Vector3(-61.816f, 0.17f, -41.567f) },
            new Prefab { Path = "assets/prefabs/deployable/search light/searchlight.deployed.prefab", Pos = new Vector3(-54.752f, 40.844f, -17.276f), Rot = new Vector3(-24.01f, 5.901f, -41.144f) },
            new Prefab { Path = "assets/prefabs/deployable/search light/searchlight.deployed.prefab", Pos = new Vector3(49.403f, 10.566f, -7.368f), Rot = new Vector3(-7.892f, 5.901f, -28.549f) },
            new Prefab { Path = "assets/prefabs/deployable/search light/searchlight.deployed.prefab", Pos = new Vector3(-53.096f, 18.766f, -0.508f), Rot = new Vector3(-3.957f, 6.048f, -5.545f) },
            new Prefab { Path = "assets/prefabs/deployable/search light/searchlight.deployed.prefab", Pos = new Vector3(-10.223f, 9.785f, 36.017f), Rot = new Vector3(28f, 6.002f, -7.106f) },
            //sam_static
            new Prefab { Path = "assets/prefabs/npc/sam_site_turret/sam_static.prefab", Pos = new Vector3(9.336f, 10.13f, -2.138f), Rot = new Vector3(0f, 180f, 0f) }
        };

        internal HashSet<Vector3> Marker { get; } = new HashSet<Vector3>
        {
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
            new Vector3(44f, 0f, 18f),
            new Vector3(44f, 0f, 16f),
            new Vector3(44f, 0f, 14f),
            new Vector3(44f, 0f, 12f),
            new Vector3(44f, 0f, 10f),
            new Vector3(44f, 0f, 8f),
            new Vector3(44f, 0f, 6f),
            new Vector3(44f, 0f, 4f),
            new Vector3(44f, 0f, 2f),
            new Vector3(44f, 0f, 0f),
            new Vector3(44f, 0f, -2f),
            new Vector3(44f, 0f, -4f),
            new Vector3(44f, 0f, -6f),
            new Vector3(44f, 0f, -8f),
            new Vector3(44f, 0f, -10f),
            new Vector3(44f, 0f, -12f),
            new Vector3(44f, 0f, -14f),
            new Vector3(44f, 0f, -16f),
            new Vector3(44f, 0f, -18f),
            new Vector3(42f, 0f, 22f),
            new Vector3(42f, 0f, 20f),
            new Vector3(42f, 0f, 18f),
            new Vector3(42f, 0f, 16f),
            new Vector3(42f, 0f, 14f),
            new Vector3(42f, 0f, -14f),
            new Vector3(42f, 0f, -16f),
            new Vector3(42f, 0f, -18f),
            new Vector3(42f, 0f, -20f),
            new Vector3(42f, 0f, -22f),
            new Vector3(40f, 0f, 26f),
            new Vector3(40f, 0f, 24f),
            new Vector3(40f, 0f, 22f),
            new Vector3(40f, 0f, 20f),
            new Vector3(40f, 0f, -20f),
            new Vector3(40f, 0f, -22f),
            new Vector3(40f, 0f, -24f),
            new Vector3(40f, 0f, -26f),
            new Vector3(38f, 0f, 28f),
            new Vector3(38f, 0f, 26f),
            new Vector3(38f, 0f, 24f),
            new Vector3(38f, 0f, -24f),
            new Vector3(38f, 0f, -26f),
            new Vector3(38f, 0f, -28f),
            new Vector3(36f, 0f, 30f),
            new Vector3(36f, 0f, 28f),
            new Vector3(36f, 0f, 26f),
            new Vector3(36f, 0f, -26f),
            new Vector3(36f, 0f, -28f),
            new Vector3(36f, 0f, -30f),
            new Vector3(34f, 0f, 32f),
            new Vector3(34f, 0f, 30f),
            new Vector3(34f, 0f, -28f),
            new Vector3(34f, 0f, -30f),
            new Vector3(34f, 0f, -32f),
            new Vector3(32f, 0f, 34f),
            new Vector3(32f, 0f, 32f),
            new Vector3(32f, 0f, -2f),
            new Vector3(32f, 0f, -4f),
            new Vector3(32f, 0f, -6f),
            new Vector3(32f, 0f, -8f),
            new Vector3(32f, 0f, -10f),
            new Vector3(32f, 0f, -12f),
            new Vector3(32f, 0f, -14f),
            new Vector3(32f, 0f, -32f),
            new Vector3(32f, 0f, -34f),
            new Vector3(30f, 0f, 36f),
            new Vector3(30f, 0f, 34f),
            new Vector3(30f, 0f, 0f),
            new Vector3(30f, 0f, -2f),
            new Vector3(30f, 0f, -4f),
            new Vector3(30f, 0f, -6f),
            new Vector3(30f, 0f, -8f),
            new Vector3(30f, 0f, -10f),
            new Vector3(30f, 0f, -12f),
            new Vector3(30f, 0f, -14f),
            new Vector3(30f, 0f, -34f),
            new Vector3(30f, 0f, -36f),
            new Vector3(28f, 0f, 38f),
            new Vector3(28f, 0f, 36f),
            new Vector3(28f, 0f, 4f),
            new Vector3(28f, 0f, 2f),
            new Vector3(28f, 0f, 0f),
            new Vector3(28f, 0f, -2f),
            new Vector3(28f, 0f, -4f),
            new Vector3(28f, 0f, -6f),
            new Vector3(28f, 0f, -18f),
            new Vector3(28f, 0f, -20f),
            new Vector3(28f, 0f, -22f),
            new Vector3(28f, 0f, -36f),
            new Vector3(28f, 0f, -38f),
            new Vector3(26f, 0f, 40f),
            new Vector3(26f, 0f, 38f),
            new Vector3(26f, 0f, 36f),
            new Vector3(26f, 0f, 6f),
            new Vector3(26f, 0f, 4f),
            new Vector3(26f, 0f, 2f),
            new Vector3(26f, 0f, 0f),
            new Vector3(26f, 0f, -2f),
            new Vector3(26f, 0f, -20f),
            new Vector3(26f, 0f, -22f),
            new Vector3(26f, 0f, -36f),
            new Vector3(26f, 0f, -38f),
            new Vector3(26f, 0f, -40f),
            new Vector3(24f, 0f, 40f),
            new Vector3(24f, 0f, 38f),
            new Vector3(24f, 0f, 6f),
            new Vector3(24f, 0f, 4f),
            new Vector3(24f, 0f, 0f),
            new Vector3(24f, 0f, -2f),
            new Vector3(24f, 0f, -22f),
            new Vector3(24f, 0f, -24f),
            new Vector3(24f, 0f, -38f),
            new Vector3(24f, 0f, -40f),
            new Vector3(22f, 0f, 42f),
            new Vector3(22f, 0f, 40f),
            new Vector3(22f, 0f, 8f),
            new Vector3(22f, 0f, 6f),
            new Vector3(22f, 0f, 2f),
            new Vector3(22f, 0f, 0f),
            new Vector3(22f, 0f, -22f),
            new Vector3(22f, 0f, -24f),
            new Vector3(22f, 0f, -26f),
            new Vector3(22f, 0f, -40f),
            new Vector3(22f, 0f, -42f),
            new Vector3(20f, 0f, 42f),
            new Vector3(20f, 0f, 40f),
            new Vector3(20f, 0f, 8f),
            new Vector3(20f, 0f, 6f),
            new Vector3(20f, 0f, 2f),
            new Vector3(20f, 0f, 0f),
            new Vector3(20f, 0f, -22f),
            new Vector3(20f, 0f, -24f),
            new Vector3(20f, 0f, -26f),
            new Vector3(20f, 0f, -40f),
            new Vector3(20f, 0f, -42f),
            new Vector3(18f, 0f, 44f),
            new Vector3(18f, 0f, 42f),
            new Vector3(18f, 0f, 22f),
            new Vector3(18f, 0f, 20f),
            new Vector3(18f, 0f, 18f),
            new Vector3(18f, 0f, 16f),
            new Vector3(18f, 0f, 14f),
            new Vector3(18f, 0f, 12f),
            new Vector3(18f, 0f, 10f),
            new Vector3(18f, 0f, 8f),
            new Vector3(18f, 0f, 2f),
            new Vector3(18f, 0f, 0f),
            new Vector3(18f, 0f, -22f),
            new Vector3(18f, 0f, -24f),
            new Vector3(18f, 0f, -26f),
            new Vector3(18f, 0f, -42f),
            new Vector3(18f, 0f, -44f),
            new Vector3(16f, 0f, 44f),
            new Vector3(16f, 0f, 42f),
            new Vector3(16f, 0f, 26f),
            new Vector3(16f, 0f, 24f),
            new Vector3(16f, 0f, 22f),
            new Vector3(16f, 0f, 20f),
            new Vector3(16f, 0f, 18f),
            new Vector3(16f, 0f, 16f),
            new Vector3(16f, 0f, 14f),
            new Vector3(16f, 0f, 12f),
            new Vector3(16f, 0f, 10f),
            new Vector3(16f, 0f, 8f),
            new Vector3(16f, 0f, 2f),
            new Vector3(16f, 0f, 0f),
            new Vector3(16f, 0f, -22f),
            new Vector3(16f, 0f, -24f),
            new Vector3(16f, 0f, -26f),
            new Vector3(16f, 0f, -28f),
            new Vector3(16f, 0f, -42f),
            new Vector3(16f, 0f, -44f),
            new Vector3(14f, 0f, 46f),
            new Vector3(14f, 0f, 44f),
            new Vector3(14f, 0f, 28f),
            new Vector3(14f, 0f, 26f),
            new Vector3(14f, 0f, 24f),
            new Vector3(14f, 0f, 0f),
            new Vector3(14f, 0f, -2f),
            new Vector3(14f, 0f, -22f),
            new Vector3(14f, 0f, -24f),
            new Vector3(14f, 0f, -26f),
            new Vector3(14f, 0f, -28f),
            new Vector3(14f, 0f, -44f),
            new Vector3(14f, 0f, -46f),
            new Vector3(12f, 0f, 46f),
            new Vector3(12f, 0f, 44f),
            new Vector3(12f, 0f, 30f),
            new Vector3(12f, 0f, 28f),
            new Vector3(12f, 0f, 26f),
            new Vector3(12f, 0f, 24f),
            new Vector3(12f, 0f, 22f),
            new Vector3(12f, 0f, 20f),
            new Vector3(12f, 0f, 18f),
            new Vector3(12f, 0f, 0f),
            new Vector3(12f, 0f, -2f),
            new Vector3(12f, 0f, -4f),
            new Vector3(12f, 0f, -20f),
            new Vector3(12f, 0f, -22f),
            new Vector3(12f, 0f, -26f),
            new Vector3(12f, 0f, -28f),
            new Vector3(12f, 0f, -44f),
            new Vector3(12f, 0f, -46f),
            new Vector3(10f, 0f, 46f),
            new Vector3(10f, 0f, 44f),
            new Vector3(10f, 0f, 32f),
            new Vector3(10f, 0f, 30f),
            new Vector3(10f, 0f, 28f),
            new Vector3(10f, 0f, 26f),
            new Vector3(10f, 0f, 24f),
            new Vector3(10f, 0f, 22f),
            new Vector3(10f, 0f, 20f),
            new Vector3(10f, 0f, 18f),
            new Vector3(10f, 0f, 16f),
            new Vector3(10f, 0f, 14f),
            new Vector3(10f, 0f, -2f),
            new Vector3(10f, 0f, -4f),
            new Vector3(10f, 0f, -6f),
            new Vector3(10f, 0f, -18f),
            new Vector3(10f, 0f, -20f),
            new Vector3(10f, 0f, -26f),
            new Vector3(10f, 0f, -44f),
            new Vector3(10f, 0f, -46f),
            new Vector3(8f, 0f, 46f),
            new Vector3(8f, 0f, 44f),
            new Vector3(8f, 0f, 34f),
            new Vector3(8f, 0f, 32f),
            new Vector3(8f, 0f, 30f),
            new Vector3(8f, 0f, 16f),
            new Vector3(8f, 0f, 14f),
            new Vector3(8f, 0f, 12f),
            new Vector3(8f, 0f, -4f),
            new Vector3(8f, 0f, -6f),
            new Vector3(8f, 0f, -8f),
            new Vector3(8f, 0f, -10f),
            new Vector3(8f, 0f, -12f),
            new Vector3(8f, 0f, -14f),
            new Vector3(8f, 0f, -16f),
            new Vector3(8f, 0f, -18f),
            new Vector3(8f, 0f, -24f),
            new Vector3(8f, 0f, -26f),
            new Vector3(8f, 0f, -44f),
            new Vector3(8f, 0f, -46f),
            new Vector3(6f, 0f, 48f),
            new Vector3(6f, 0f, 46f),
            new Vector3(6f, 0f, 34f),
            new Vector3(6f, 0f, 32f),
            new Vector3(6f, 0f, 14f),
            new Vector3(6f, 0f, 12f),
            new Vector3(6f, 0f, -8f),
            new Vector3(6f, 0f, -10f),
            new Vector3(6f, 0f, -12f),
            new Vector3(6f, 0f, -14f),
            new Vector3(6f, 0f, -24f),
            new Vector3(6f, 0f, -26f),
            new Vector3(6f, 0f, -46f),
            new Vector3(6f, 0f, -48f),
            new Vector3(4f, 0f, 48f),
            new Vector3(4f, 0f, 46f),
            new Vector3(4f, 0f, 34f),
            new Vector3(4f, 0f, 32f),
            new Vector3(4f, 0f, 12f),
            new Vector3(4f, 0f, 10f),
            new Vector3(4f, 0f, -22f),
            new Vector3(4f, 0f, -24f),
            new Vector3(4f, 0f, -46f),
            new Vector3(4f, 0f, -48f),
            new Vector3(2f, 0f, 48f),
            new Vector3(2f, 0f, 46f),
            new Vector3(2f, 0f, 34f),
            new Vector3(2f, 0f, 12f),
            new Vector3(2f, 0f, 10f),
            new Vector3(2f, 0f, -20f),
            new Vector3(2f, 0f, -22f),
            new Vector3(2f, 0f, -46f),
            new Vector3(2f, 0f, -48f),
            new Vector3(0f, 0f, 48f),
            new Vector3(0f, 0f, 46f),
            new Vector3(0f, 0f, 12f),
            new Vector3(0f, 0f, 10f),
            new Vector3(0f, 0f, -18f),
            new Vector3(0f, 0f, -20f),
            new Vector3(0f, 0f, -22f),
            new Vector3(0f, 0f, -46f),
            new Vector3(0f, 0f, -48f),
            new Vector3(-2f, 0f, 48f),
            new Vector3(-2f, 0f, 46f),
            new Vector3(-2f, 0f, 34f),
            new Vector3(-2f, 0f, 12f),
            new Vector3(-2f, 0f, 10f),
            new Vector3(-2f, 0f, -20f),
            new Vector3(-2f, 0f, -22f),
            new Vector3(-2f, 0f, -46f),
            new Vector3(-2f, 0f, -48f),
            new Vector3(-4f, 0f, 48f),
            new Vector3(-4f, 0f, 46f),
            new Vector3(-4f, 0f, 34f),
            new Vector3(-4f, 0f, 32f),
            new Vector3(-4f, 0f, 12f),
            new Vector3(-4f, 0f, 10f),
            new Vector3(-4f, 0f, -22f),
            new Vector3(-4f, 0f, -24f),
            new Vector3(-4f, 0f, -46f),
            new Vector3(-4f, 0f, -48f),
            new Vector3(-6f, 0f, 48f),
            new Vector3(-6f, 0f, 46f),
            new Vector3(-6f, 0f, 34f),
            new Vector3(-6f, 0f, 32f),
            new Vector3(-6f, 0f, 12f),
            new Vector3(-6f, 0f, 10f),
            new Vector3(-6f, 0f, -24f),
            new Vector3(-6f, 0f, -26f),
            new Vector3(-6f, 0f, -46f),
            new Vector3(-6f, 0f, -48f),
            new Vector3(-8f, 0f, 48f),
            new Vector3(-8f, 0f, 46f),
            new Vector3(-8f, 0f, 34f),
            new Vector3(-8f, 0f, 32f),
            new Vector3(-8f, 0f, 30f),
            new Vector3(-8f, 0f, 14f),
            new Vector3(-8f, 0f, 12f),
            new Vector3(-8f, 0f, -6f),
            new Vector3(-8f, 0f, -8f),
            new Vector3(-8f, 0f, -10f),
            new Vector3(-8f, 0f, -16f),
            new Vector3(-8f, 0f, -18f),
            new Vector3(-8f, 0f, -24f),
            new Vector3(-8f, 0f, -26f),
            new Vector3(-8f, 0f, -46f),
            new Vector3(-8f, 0f, -48f),
            new Vector3(-10f, 0f, 46f),
            new Vector3(-10f, 0f, 44f),
            new Vector3(-10f, 0f, 32f),
            new Vector3(-10f, 0f, 30f),
            new Vector3(-10f, 0f, 28f),
            new Vector3(-10f, 0f, 26f),
            new Vector3(-10f, 0f, 18f),
            new Vector3(-10f, 0f, 16f),
            new Vector3(-10f, 0f, 14f),
            new Vector3(-10f, 0f, -2f),
            new Vector3(-10f, 0f, -4f),
            new Vector3(-10f, 0f, -6f),
            new Vector3(-10f, 0f, -8f),
            new Vector3(-10f, 0f, -16f),
            new Vector3(-10f, 0f, -18f),
            new Vector3(-10f, 0f, -20f),
            new Vector3(-10f, 0f, -26f),
            new Vector3(-10f, 0f, -44f),
            new Vector3(-10f, 0f, -46f),
            new Vector3(-12f, 0f, 46f),
            new Vector3(-12f, 0f, 44f),
            new Vector3(-12f, 0f, 32f),
            new Vector3(-12f, 0f, 30f),
            new Vector3(-12f, 0f, 28f),
            new Vector3(-12f, 0f, 26f),
            new Vector3(-12f, 0f, 24f),
            new Vector3(-12f, 0f, 22f),
            new Vector3(-12f, 0f, 20f),
            new Vector3(-12f, 0f, 18f),
            new Vector3(-12f, 0f, 16f),
            new Vector3(-12f, 0f, -2f),
            new Vector3(-12f, 0f, -4f),
            new Vector3(-12f, 0f, -20f),
            new Vector3(-12f, 0f, -22f),
            new Vector3(-12f, 0f, -26f),
            new Vector3(-12f, 0f, -44f),
            new Vector3(-12f, 0f, -46f),
            new Vector3(-14f, 0f, 46f),
            new Vector3(-14f, 0f, 44f),
            new Vector3(-14f, 0f, 30f),
            new Vector3(-14f, 0f, 28f),
            new Vector3(-14f, 0f, 26f),
            new Vector3(-14f, 0f, 22f),
            new Vector3(-14f, 0f, 0f),
            new Vector3(-14f, 0f, -2f),
            new Vector3(-14f, 0f, -20f),
            new Vector3(-14f, 0f, -22f),
            new Vector3(-14f, 0f, -26f),
            new Vector3(-14f, 0f, -28f),
            new Vector3(-14f, 0f, -44f),
            new Vector3(-14f, 0f, -46f),
            new Vector3(-16f, 0f, 46f),
            new Vector3(-16f, 0f, 44f),
            new Vector3(-16f, 0f, 26f),
            new Vector3(-16f, 0f, 24f),
            new Vector3(-16f, 0f, 22f),
            new Vector3(-16f, 0f, 20f),
            new Vector3(-16f, 0f, 12f),
            new Vector3(-16f, 0f, 10f),
            new Vector3(-16f, 0f, 8f),
            new Vector3(-16f, 0f, 0f),
            new Vector3(-16f, 0f, -22f),
            new Vector3(-16f, 0f, -24f),
            new Vector3(-16f, 0f, -26f),
            new Vector3(-16f, 0f, -28f),
            new Vector3(-16f, 0f, -44f),
            new Vector3(-16f, 0f, -46f),
            new Vector3(-18f, 0f, 44f),
            new Vector3(-18f, 0f, 42f),
            new Vector3(-18f, 0f, 24f),
            new Vector3(-18f, 0f, 22f),
            new Vector3(-18f, 0f, 20f),
            new Vector3(-18f, 0f, 18f),
            new Vector3(-18f, 0f, 16f),
            new Vector3(-18f, 0f, 14f),
            new Vector3(-18f, 0f, 12f),
            new Vector3(-18f, 0f, 10f),
            new Vector3(-18f, 0f, 8f),
            new Vector3(-18f, 0f, 2f),
            new Vector3(-18f, 0f, 0f),
            new Vector3(-18f, 0f, -22f),
            new Vector3(-18f, 0f, -24f),
            new Vector3(-18f, 0f, -26f),
            new Vector3(-18f, 0f, -42f),
            new Vector3(-18f, 0f, -44f),
            new Vector3(-20f, 0f, 44f),
            new Vector3(-20f, 0f, 42f),
            new Vector3(-20f, 0f, 8f),
            new Vector3(-20f, 0f, 2f),
            new Vector3(-20f, 0f, 0f),
            new Vector3(-20f, 0f, -22f),
            new Vector3(-20f, 0f, -24f),
            new Vector3(-20f, 0f, -26f),
            new Vector3(-20f, 0f, -42f),
            new Vector3(-20f, 0f, -44f),
            new Vector3(-22f, 0f, 42f),
            new Vector3(-22f, 0f, 40f),
            new Vector3(-22f, 0f, 8f),
            new Vector3(-22f, 0f, 6f),
            new Vector3(-22f, 0f, 2f),
            new Vector3(-22f, 0f, 0f),
            new Vector3(-22f, 0f, -22f),
            new Vector3(-22f, 0f, -24f),
            new Vector3(-22f, 0f, -26f),
            new Vector3(-22f, 0f, -40f),
            new Vector3(-22f, 0f, -42f),
            new Vector3(-24f, 0f, 42f),
            new Vector3(-24f, 0f, 40f),
            new Vector3(-24f, 0f, 8f),
            new Vector3(-24f, 0f, 6f),
            new Vector3(-24f, 0f, 0f),
            new Vector3(-24f, 0f, -2f),
            new Vector3(-24f, 0f, -22f),
            new Vector3(-24f, 0f, -24f),
            new Vector3(-24f, 0f, -40f),
            new Vector3(-24f, 0f, -42f),
            new Vector3(-26f, 0f, 40f),
            new Vector3(-26f, 0f, 38f),
            new Vector3(-26f, 0f, 6f),
            new Vector3(-26f, 0f, 4f),
            new Vector3(-26f, 0f, 0f),
            new Vector3(-26f, 0f, -2f),
            new Vector3(-26f, 0f, -20f),
            new Vector3(-26f, 0f, -22f),
            new Vector3(-26f, 0f, -24f),
            new Vector3(-26f, 0f, -38f),
            new Vector3(-26f, 0f, -40f),
            new Vector3(-28f, 0f, 40f),
            new Vector3(-28f, 0f, 38f),
            new Vector3(-28f, 0f, 36f),
            new Vector3(-28f, 0f, 4f),
            new Vector3(-28f, 0f, 2f),
            new Vector3(-28f, 0f, -2f),
            new Vector3(-28f, 0f, -4f),
            new Vector3(-28f, 0f, -20f),
            new Vector3(-28f, 0f, -22f),
            new Vector3(-28f, 0f, -36f),
            new Vector3(-28f, 0f, -38f),
            new Vector3(-28f, 0f, -40f),
            new Vector3(-30f, 0f, 38f),
            new Vector3(-30f, 0f, 36f),
            new Vector3(-30f, 0f, 2f),
            new Vector3(-30f, 0f, 0f),
            new Vector3(-30f, 0f, -2f),
            new Vector3(-30f, 0f, -4f),
            new Vector3(-30f, 0f, -6f),
            new Vector3(-30f, 0f, -8f),
            new Vector3(-30f, 0f, -18f),
            new Vector3(-30f, 0f, -20f),
            new Vector3(-30f, 0f, -36f),
            new Vector3(-30f, 0f, -38f),
            new Vector3(-32f, 0f, 36f),
            new Vector3(-32f, 0f, 34f),
            new Vector3(-32f, 0f, -2f),
            new Vector3(-32f, 0f, -4f),
            new Vector3(-32f, 0f, -6f),
            new Vector3(-32f, 0f, -8f),
            new Vector3(-32f, 0f, -10f),
            new Vector3(-32f, 0f, -12f),
            new Vector3(-32f, 0f, -14f),
            new Vector3(-32f, 0f, -34f),
            new Vector3(-32f, 0f, -36f),
            new Vector3(-34f, 0f, 34f),
            new Vector3(-34f, 0f, 32f),
            new Vector3(-34f, 0f, -8f),
            new Vector3(-34f, 0f, -10f),
            new Vector3(-34f, 0f, -32f),
            new Vector3(-34f, 0f, -34f),
            new Vector3(-36f, 0f, 32f),
            new Vector3(-36f, 0f, 30f),
            new Vector3(-36f, 0f, -28f),
            new Vector3(-36f, 0f, -30f),
            new Vector3(-36f, 0f, -32f),
            new Vector3(-38f, 0f, 30f),
            new Vector3(-38f, 0f, 28f),
            new Vector3(-38f, 0f, 26f),
            new Vector3(-38f, 0f, -26f),
            new Vector3(-38f, 0f, -28f),
            new Vector3(-38f, 0f, -30f),
            new Vector3(-40f, 0f, 28f),
            new Vector3(-40f, 0f, 26f),
            new Vector3(-40f, 0f, 24f),
            new Vector3(-40f, 0f, -24f),
            new Vector3(-40f, 0f, -26f),
            new Vector3(-40f, 0f, -28f),
            new Vector3(-42f, 0f, 26f),
            new Vector3(-42f, 0f, 24f),
            new Vector3(-42f, 0f, 22f),
            new Vector3(-42f, 0f, 20f),
            new Vector3(-42f, 0f, -20f),
            new Vector3(-42f, 0f, -22f),
            new Vector3(-42f, 0f, -24f),
            new Vector3(-42f, 0f, -26f),
            new Vector3(-44f, 0f, 22f),
            new Vector3(-44f, 0f, 20f),
            new Vector3(-44f, 0f, 18f),
            new Vector3(-44f, 0f, 16f),
            new Vector3(-44f, 0f, 14f),
            new Vector3(-44f, 0f, -14f),
            new Vector3(-44f, 0f, -16f),
            new Vector3(-44f, 0f, -18f),
            new Vector3(-44f, 0f, -20f),
            new Vector3(-44f, 0f, -22f),
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
            new Vector3(-46f, 0f, -14f),
            new Vector3(-46f, 0f, -16f),
            new Vector3(-46f, 0f, -18f),
            new Vector3(-48f, 0f, 12f),
            new Vector3(-48f, 0f, 10f),
            new Vector3(-48f, 0f, 8f),
            new Vector3(-48f, 0f, 6f),
            new Vector3(-48f, 0f, 4f),
            new Vector3(-48f, 0f, 2f),
            new Vector3(-48f, 0f, 0f),
            new Vector3(-48f, 0f, -2f),
            new Vector3(-48f, 0f, -4f),
            new Vector3(-48f, 0f, -6f),
            new Vector3(-48f, 0f, -8f),
            new Vector3(-48f, 0f, -10f),
            new Vector3(-48f, 0f, -12)
        };

        internal HashSet<string> TrashList { get; } = new HashSet<string>
        {
            "minicopter.entity",
            "scraptransporthelicopter",
            "hotairballoon",
            "rowboat",
            "rhib",
            "submarinesolo.entity",
            "submarineduo.entity",
            "sled.deployed",
            "magnetcrane.entity",
            "2module_car_spawned.entity",
            "3module_car_spawned.entity",
            "4module_car_spawned.entity",
            "wolf",
            "chicken",
            "boar",
            "stag",
            "bear",
            "testridablehorse",
            "servergibs_bradley",
            "servergibs_patrolhelicopter"
        };

        private ControllerSatDishEvent Controller { get; set; } = null;
        private bool Active { get; set; } = false;

        private void StartTimer()
        {
            if (!_config.EnabledTimer) return;
            timer.In(UnityEngine.Random.Range(_config.MinStartTime, _config.MaxStartTime), () =>
            {
                if (!Active) Start(null);
                else Puts("This event is active now. To finish this event (satdishstop), then to start the next one");
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
                if (_config.RemoveBetterNpc && plugins.Exists("BetterNpc")) BetterNpc.Call("DestroyController", "Satellite Dish");
                ToggleHooks(true);
                Controller = new GameObject().AddComponent<ControllerSatDishEvent>();
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
                EnableRadiation(Controller.Puzzle);
                UnityEngine.Object.Destroy(Controller.gameObject);
            }
            Active = false;
            SendBalance();
            LootableCrates.Clear();
            StartHackCrates.Clear();
            AlertToAllPlayers("Finish", _config.Chat.Prefix);
            Interface.Oxide.CallHook($"On{Name}End");
            if (_config.RemoveBetterNpc && plugins.Exists("BetterNpc")) BetterNpc.Call("CreateController", "Satellite Dish");
            Puts($"{Name} has ended");
            StartTimer();
        }

        internal class ControllerSatDishEvent : FacepunchBehaviour
        {
            private PluginConfig _config => _ins._config;

            internal MonumentInfo Monument { get; set; } = null;
            internal PuzzleReset Puzzle { get; set; } = null;

            private SphereCollider SphereCollider { get; set; } = null;

            private VendingMachineMapMarker VendingMarker { get; set; } = null;
            private HashSet<MapMarkerGenericRadius> Markers { get; } = new HashSet<MapMarkerGenericRadius>();

            internal Coroutine Ch47Coroutine { get; set; } = null;
            internal CH47Helicopter Ch47 { get; set; } = null;
            internal CH47HelicopterAIController Ch47Ai { get; set; } = null;
            internal Vector2 DropCratePos2 { get; set; } = Vector2.zero;
            internal bool IsEvacuation { get; set; } = false;

            internal Coroutine BradleyCoroutine { get; set; } = null;
            internal BradleyAPC Bradley { get; set; } = null;
            internal Vector3 LandingBradleyPos { get; set; } = Vector3.zero;
            internal CargoPlane Plane { get; set; } = null;
            private BaseVehicle Parachute { get; set; } = null;
            private BasePlayer PlayerParachute { get; set; } = null;

            internal BradleyAPC AddBradley { get; set; } = null;
            internal Vector3 AddBradleyPos { get; set; } = Vector3.zero;

            internal Telephone Phone { get; set; } = null;
            internal Telephone PhoneMonument { get; set; } = null;
            internal BasePlayer Dummy { get; set; } = null;

            internal bool KillEntities { get; set; } = false;
            internal HashSet<BaseEntity> Entities { get; } = new HashSet<BaseEntity>();
            internal HashSet<Door> Doors { get; } = new HashSet<Door>();
            internal AudioAlarm Alarm { get; set; } = null;
            internal SirenLight Siren { get; set; } = null;
            internal bool IsAlarm { get; set; } = false;

            private HashSet<Vector3> Path { get; } = new HashSet<Vector3>();
            internal HashSet<ScientistNPC> Zombies { get; } = new HashSet<ScientistNPC>();
            private int Ch47TakeZombies { get; set; } = 0;

            internal HashSet<ScientistNPC> Scientists { get; } = new HashSet<ScientistNPC>();

            internal HashSet<LootContainer> Crates { get; } = new HashSet<LootContainer>();
            internal HackableLockedCrate HackCrate { get; set; } = null;

            internal int TimeToFinish { get; set; } = _ins._config.FinishTime;

            internal HashSet<BasePlayer> Players { get; } = new HashSet<BasePlayer>();
            internal BasePlayer Owner { get; set; } = null;

            private void Awake()
            {
                Monument = _ins.GetMonument();
                transform.position = Monument.transform.position;
                transform.rotation = Monument.transform.rotation;

                Puzzle = GetPuzzleReset(Monument);
                DisableRadiation(Puzzle);

                gameObject.layer = 3;
                SphereCollider = gameObject.AddComponent<SphereCollider>();
                SphereCollider.isTrigger = true;
                SphereCollider.radius = _config.Radius;

                SpawnEntities();

                PhoneMonument = GetNearEntity<Telephone>(GetGlobalPosition(new Vector3(6.257f, 6.113f, -1.543f)), 1f, 1 << 16);
                SpawnPhone();
                SpawnDummy(GetGlobalPosition(new Vector3(7f, 6.067f, -1.576f)), GetGlobalRotation(new Vector3(0f, 270f, 0f)).eulerAngles);

                SpawnCrates();

                Ch47Coroutine = ServerMgr.Instance.StartCoroutine(ProcessCh47());

                Path.Add(GetGlobalPosition(new Vector3(10.382f, 6.067f, -7.845f)));
                Path.Add(GetGlobalPosition(new Vector3(12.157f, 6.067f, -7.845f)));
                Path.Add(GetGlobalPosition(new Vector3(28.011f, 6.047f, -6.973f)));
                SpawnZombies();

                LandingBradleyPos = GetGlobalPosition(new Vector3(-3.521f, 5.808f, 0.88f));
                if (_config.IsAdditionalBradley) SpawnAddBradley();

                foreach (PresetConfig preset in _config.Npc) SpawnPreset(preset);

                SpawnMapMarker(_config.Marker);

                InvokeRepeating(InvokeUpdates, 0f, 1f);
            }

            private void OnDestroy()
            {
                if (Ch47Coroutine != null) ServerMgr.Instance.StopCoroutine(Ch47Coroutine);
                if (BradleyCoroutine != null) ServerMgr.Instance.StopCoroutine(BradleyCoroutine);

                CancelInvoke(InvokeUpdates);

                if (SphereCollider != null) Destroy(SphereCollider);

                if (VendingMarker.IsExists()) VendingMarker.Kill();
                foreach (MapMarkerGenericRadius marker in Markers) if (marker.IsExists()) marker.Kill();

                foreach (BasePlayer player in Players) CuiHelper.DestroyUi(player, "Tabs_KpucTaJl");

                foreach (ScientistNPC npc in Scientists) if (npc.IsExists()) npc.Kill();

                foreach (ScientistNPC zombie in Zombies) if (zombie.IsExists()) zombie.Kill();

                foreach (LootContainer crate in Crates) if (crate.IsExists()) crate.Kill();
                if (HackCrate.IsExists()) HackCrate.Kill();

                if (Ch47.IsExists()) Ch47.Kill();

                if (AddBradley.IsExists()) AddBradley.Kill();

                if (Plane.IsExists()) Plane.Kill();
                DestroyParachute();
                if (Bradley.IsExists()) Bradley.Kill();

                KillEntities = true;
                foreach (BaseEntity entity in Entities) if (entity.IsExists()) entity.Kill();

                if (Phone.IsExists()) Phone.Kill();
                if (Dummy.IsExists()) Dummy.Kill();
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
                UpdateLight();
                UpdateTimeToFinish();
            }

            private void UpdateGui(BasePlayer player)
            {
                Dictionary<string, string> dic = new Dictionary<string, string> { ["Clock_KpucTaJl"] = GetTimeFormat(TimeToFinish) };
                if (Scientists.Count > 0) dic.Add("Npc_KpucTaJl", Scientists.Count.ToString());
                if (Zombies.Count == 0 && (Crates.Count > 0 || HackCrate != null))
                {
                    int count = Crates.Count;
                    if (HackCrate != null) count++;
                    dic.Add("Crate_KpucTaJl", count.ToString());
                }
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
                    if (AddBradley.IsExists()) points.Add(AddBradley.transform.position);
                    if (Bradley.IsExists()) points.Add(Bradley.transform.position);
                    if (IsEvacuation)
                    {
                        if (IsAlarm)
                        {
                            if (PhoneMonument.IsExists() && ((Siren.IsExists() && Siren.HasFlag(BaseEntity.Flags.Reserved8)) || (Alarm.IsExists() && Alarm.HasFlag(BaseEntity.Flags.Reserved8))))
                            {
                                points.Add(PhoneMonument.transform.position);
                            }
                        }
                        else
                        {
                            if (Zombies.Count == 5)
                                foreach (ScientistNPC zombie in Zombies)
                                    points.Add(zombie.transform.position);
                        }
                    }
                    if (points.Count > 0) foreach (BasePlayer player in Players) foreach (Vector3 point in points) UpdateMarkerForPlayer(player, point, _config.MainPoint);
                    points = null;
                }

                if (_config.AdditionalPoint.Enabled)
                {
                    HashSet<Vector3> points = new HashSet<Vector3>();
                    if (IsAlarm && Zombies.Count > 0 && Zombies.Count < 5) foreach (ScientistNPC zombie in Zombies) points.Add(zombie.transform.position);
                    if (Zombies.Count == 0)
                    {
                        foreach (LootContainer crate in Crates) if (crate.IsExists()) points.Add(crate.transform.position);
                        if (HackCrate.IsExists()) points.Add(HackCrate.transform.position);
                    }
                    if (points.Count > 0) foreach (BasePlayer player in Players) foreach (Vector3 point in points) UpdateMarkerForPlayer(player, point, _config.AdditionalPoint);
                    points = null;
                }
            }

            private void UpdateTimeToFinish()
            {
                TimeToFinish--;
                if (TimeToFinish == _config.PreFinishTime) _ins.AlertToAllPlayers("PreFinish", _config.Chat.Prefix, GetTimeFormat(_config.PreFinishTime));
                else if (TimeToFinish == 0)
                {
                    CancelInvoke(InvokeUpdates);
                    _ins.Finish();
                }
            }

            private double DayStartHours { get; } = TimeSpan.Parse("8:00").TotalHours;
            private double DayEndHours { get; } = TimeSpan.Parse("20:00").TotalHours;
            private static float CurrentHours => TOD_Sky.Instance.Cycle.Hour;
            private HashSet<SearchLight> SearchLights { get; } = new HashSet<SearchLight>();
            private bool IsLight { get; set; } = true;

            private void UpdateLight()
            {
                if (IsLight)
                {
                    if (CurrentHours < DayEndHours && CurrentHours > DayStartHours)
                        SwitchLight(false);
                }
                else
                {
                    if (CurrentHours > DayEndHours || CurrentHours < DayStartHours)
                        SwitchLight(true);
                }
            }

            private void SwitchLight(bool on)
            {
                foreach (SearchLight light in SearchLights) light.UpdateFromInput(on ? 10 : 0, 0);
                IsLight = on;
            }

            private Vector3 GetGlobalPosition(Vector3 localPosition) => transform.TransformPoint(localPosition);

            private Quaternion GetGlobalRotation(Vector3 localRotation) => transform.rotation * Quaternion.Euler(localRotation);

            private static T GetNearEntity<T>(Vector3 position, float radius, int layerMask) where T : BaseEntity
            {
                List<T> list = Pool.Get<List<T>>();
                Vis.Entities<T>(position, radius, list, layerMask);
                T result = list.Count == 0 ? null : list.Min(s => Vector3.Distance(position, s.transform.position));
                Pool.FreeUnmanaged(ref list);
                return result;
            }

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

            private void SpawnEntities()
            {
                foreach (Prefab prefab in _ins.Prefabs)
                {
                    if (prefab.Path == "assets/prefabs/npc/sam_site_turret/sam_static.prefab" && !_config.IsSamSites) continue;

                    BaseEntity entity = SpawnEntity(prefab.Path, GetGlobalPosition(prefab.Pos), GetGlobalRotation(prefab.Rot));

                    if (entity is BuildingBlock)
                    {
                        BuildingBlock buildingBlock = entity as BuildingBlock;
                        buildingBlock.ChangeGradeAndSkin(BuildingGrade.Enum.Metal, 0);
                    }

                    if (entity is CCTV_RC)
                    {
                        CCTV_RC cctv = entity as CCTV_RC;
                        cctv.UpdateFromInput(5, 0);
                        cctv.rcIdentifier = _config.Cctv;
                    }

                    if (entity is AudioAlarm) Alarm = entity as AudioAlarm;
                    if (entity is SirenLight) Siren = entity as SirenLight;

                    if (entity is Door)
                    {
                        Door door = entity as Door;
                        door.canTakeCloser = false;
                        door.canTakeKnocker = false;
                        door.canTakeLock = false;
                        door.canHandOpen = false;
                        door.hasHatch = false;
                        Doors.Add(door);
                    }

                    if (entity is BasicCar)
                    {
                        BasicCar basicCar = entity as BasicCar;
                        basicCar.SetToKinematic();
                        FlasherLight flasherLight = GameManager.server.CreateEntity("assets/prefabs/deployable/playerioents/lights/flasherlight/electric.flasherlight.deployed.prefab") as FlasherLight;
                        flasherLight.enableSaving = false;
                        flasherLight.SetParent(basicCar);
                        flasherLight.transform.localPosition = new Vector3(0f, 1.64f, 0f);
                        flasherLight.Spawn();
                        flasherLight.pickup.enabled = false;
                        flasherLight.UpdateFromInput(1, 0);
                        Entities.Add(flasherLight);
                    }

                    if (entity is SearchLight)
                    {
                        SearchLight light = entity as SearchLight;
                        light.UpdateFromInput(10, 0);
                        light.needsBuildingPrivilegeToUse = true;
                        light.SetTargetAimpoint(GetGlobalPosition(prefab.Rot));
                        SearchLights.Add(light);
                    }

                    Entities.Add(entity);
                }
            }

            private void SpawnPhone()
            {
                Phone = SpawnEntity("assets/prefabs/voiceaudio/telephone/telephone.deployed.prefab", GetGlobalPosition(new Vector3(56.195f, 16.855f, -6.724f)), Quaternion.identity) as Telephone;
                Phone.UpdateFromInput(1, 0);
            }

            internal void CallPhone() => Phone.Controller.CallPhone(PhoneMonument.Controller.PhoneNumber);

            private void SpawnDummy(Vector3 pos, Vector3 rot)
            {
                Dummy = GameManager.server.CreateEntity("assets/prefabs/player/player.prefab", pos) as BasePlayer;
                Dummy.viewAngles = rot;
                Dummy.enableSaving = false;
                Dummy.Spawn();
            }

            private void SpawnCrates()
            {
                foreach (CrateConfig crateConfig in _config.DefaultCrates)
                {
                    LootContainer crate = GameManager.server.CreateEntity(crateConfig.Prefab, GetGlobalPosition(crateConfig.Position.ToVector3()), GetGlobalRotation(crateConfig.Rotation.ToVector3())) as LootContainer;
                    crate.enableSaving = false;
                    crate.Spawn();
                    Crates.Add(crate);
                    if (_config.TypeLootTableCrates == 1 || _config.TypeLootTableCrates == 4 || _config.TypeLootTableCrates == 5)
                    {
                        _ins.NextTick(() =>
                        {
                            crate.inventory.ClearItemsContainer();
                            if (_config.TypeLootTableCrates == 4 || _config.TypeLootTableCrates == 5) _ins.AddToContainerPrefab(crate.inventory, crateConfig.PrefabLootTable);
                            if (_config.TypeLootTableCrates == 1 || _config.TypeLootTableCrates == 5) _ins.AddToContainerItem(crate.inventory, crateConfig.OwnLootTable);
                        });
                    }
                }
            }

            private void SpawnPreset(PresetConfig preset)
            {
                int count = UnityEngine.Random.Range(preset.Min, preset.Max + 1);

                List<Vector3> positions = Pool.Get<List<Vector3>>();
                foreach (string pos in preset.Positions) positions.Add(GetGlobalPosition(pos.ToVector3()));

                object config = GetObjectConfig(preset.Config);
                if (config == null)
                {
                    _ins.PrintError("SpawnPreset: Failed to create NPC config. Make sure NpcSpawn plugin is loaded.");
                    Pool.FreeUnmanaged(ref positions);
                    return;
                }

                for (int i = 0; i < count; i++)
                {
                    Vector3 pos = positions.GetRandom();
                    positions.Remove(pos);
                    ScientistNPC npc = (ScientistNPC)_ins.NpcSpawn.Call("SpawnNpc", pos, config);
                    if (npc != null) Scientists.Add(npc);
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

                var npcConfigType = npcSpawnType.GetNestedType("NpcConfig", BindingFlags.Public | BindingFlags.NonPublic);
                if (npcConfigType == null)
                {
                    _ins.PrintError("GetObjectConfig: Failed to find NpcSpawn.NpcConfig type. Make sure NpcSpawn plugin is loaded.");
                    return null;
                }

                var npcSpawnConfig = Activator.CreateInstance(npcConfigType);

                // Get NpcSpawn's NpcWear and NpcBelt types for conversion
                var npcWearType = npcSpawnType.GetNestedType("NpcWear", BindingFlags.Public | BindingFlags.NonPublic);
                var npcBeltType = npcSpawnType.GetNestedType("NpcBelt", BindingFlags.Public | BindingFlags.NonPublic);

                if (npcWearType == null || npcBeltType == null)
                {
                    _ins.PrintError("GetObjectConfig: Failed to find NpcSpawn.NpcWear or NpcBelt types.");
                    return null;
                }

                // Convert WearItems from SatDishEvent.NpcWear to NpcSpawn.NpcWear
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

                // Convert BeltItems from SatDishEvent.NpcBelt to NpcSpawn.NpcBelt
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

            private void SpawnZombies()
            {
                Zombies.Add(SpawnZombie(new Vector3(9.3f, 6.1f, -15.9f), new Vector3(6.5f, 1.4f, 0f)));
                Zombies.Add(SpawnZombie(new Vector3(7.8f, 6.1f, -12.1f), new Vector3(7.4f, 32f, 0f)));
                Zombies.Add(SpawnZombie(new Vector3(8f, 6.1f, -6.3f), new Vector3(5.2f, 106.4f, 0f)));
                Zombies.Add(SpawnZombie(new Vector3(10.4f, 6.1f, -8.5f), new Vector3(8.7f, 102.3f, 0f)));
                Zombies.Add(SpawnZombie(new Vector3(10.6f, 6.1f, -13.8f), new Vector3(22.5f, 332.9f, 0f)));
            }

            private ScientistNPC SpawnZombie(Vector3 pos, Vector3 rot)
            {
                ScientistNPC npc = GameManager.server.CreateEntity("assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_cargo_turret_any.prefab", GetGlobalPosition(pos)) as ScientistNPC;
                npc.enableSaving = false;
                npc.Spawn();
                npc.viewAngles = GetGlobalRotation(rot).eulerAngles;

                npc.displayName = "Zombie";

                npc.startHealth = _config.Zombies.Hp;
                npc.InitializeHealth(_config.Zombies.Hp, _config.Zombies.Hp);

                npc.inventory.containerWear.Clear();
                npc.inventory.containerBelt.Clear();
                Item mummysuit = ItemManager.CreateByName("halloween.mummysuit");
                if (!mummysuit.MoveToContainer(npc.inventory.containerWear)) mummysuit.Remove();
                Item gloweyes = ItemManager.CreateByName("gloweyes");
                if (!gloweyes.MoveToContainer(npc.inventory.containerWear)) gloweyes.Remove();

                npc.CancelInvoke(npc.PlayRadioChatter);
                npc.RadioChatterEffects = Array.Empty<GameObjectRef>();
                npc.DeathEffects = Array.Empty<GameObjectRef>();

                return npc;
            }

            internal void FinishPathZombie(ScientistNPC npc)
            {
                Zombies.Remove(npc);
                if (npc.IsExists()) npc.Kill();
                Ch47TakeZombies++;
            }

            private Vector3 GetSpawnPosition()
            {
                List<Vector2> list = Pool.Get<List<Vector2>>();
                float size = World.Size / 2f;
                list.Add(new Vector2(-size, -size));
                list.Add(new Vector2(-size, size));
                list.Add(new Vector2(size, -size));
                list.Add(new Vector2(size, size));
                Vector2 pos2 = list.GetRandom();
                Vector3 pos3 = new Vector3(pos2.x, _config.HeightCh47, pos2.y);
                Pool.FreeUnmanaged(ref list);
                return pos3;
            }

            private IEnumerator ProcessCh47()
            {
                yield return CoroutineEx.waitForSeconds(_config.DelayCh47);

                Vector3 spawnPos3 = GetSpawnPosition();
                Vector2 spawnPos2 = new Vector2(spawnPos3.x, spawnPos3.z);
                Vector3 dropCratePos3 = GetGlobalPosition(new Vector3(1.157f, 6.047f, -12.599f));
                DropCratePos2 = new Vector2(dropCratePos3.x, dropCratePos3.z);
                Vector3 targetPos = new Vector3(dropCratePos3.x, _config.HeightCh47, dropCratePos3.z);
                Vector3 landingPos3 = GetGlobalPosition(new Vector3(28.011f, 9.047f, -6.973f));
                Vector2 landingPos2 = new Vector2(landingPos3.x, landingPos3.z);
                Vector3 landingRot = GetGlobalRotation(new Vector3(0f, 90f, 0f)).eulerAngles;

                SpawnNewCh47(spawnPos3, Quaternion.identity, targetPos, 0);

                while (Vector2.Distance(new Vector2(Ch47.transform.position.x, Ch47.transform.position.z), DropCratePos2) > 1f) yield return CoroutineEx.waitForSeconds(1f);

                SpawnNewCh47(Ch47.transform.position, Ch47.transform.rotation, new Vector3(dropCratePos3.x, dropCratePos3.y + 15f, dropCratePos3.z), 1);
                Ch47.transform.rotation = Quaternion.Euler(landingRot);

                while (Ch47.transform.position.y - Ch47Ai.currentDesiredAltitude > 1f) yield return CoroutineEx.waitForSeconds(1f);

                Ch47Ai.AiAltitudeForce = 0f;
                Ch47Ai.rigidBody.AddForce(Vector3.down * 10000f, ForceMode.Force);

                while (Ch47.transform.position.y - dropCratePos3.y - 15f > 1f)
                {
                    Ch47Ai.rigidBody.AddForce(Vector3.down * 10000f, ForceMode.Force);
                    yield return CoroutineEx.waitForSeconds(1f);
                }

                CheckTrash(dropCratePos3, 10f);
                Ch47Ai.DropCrate();
                SpawnNewCh47(Ch47.transform.position, Ch47.transform.rotation, landingPos3, 0);

                while (Vector2.Distance(new Vector2(Ch47.transform.position.x, Ch47.transform.position.z), landingPos2) > 1f) yield return CoroutineEx.waitForSeconds(1f);

                Ch47.transform.rotation = Quaternion.Euler(landingRot);
                Ch47Ai.AiAltitudeForce = 0f;
                CheckTrash(landingPos3, 10f);
                Ch47Ai.rigidBody.AddForce(Vector3.down * 10000f, ForceMode.Force);

                while (Ch47.transform.position.y - landingPos3.y > 1f)
                {
                    Ch47Ai.rigidBody.AddForce(Vector3.down * 10000f, ForceMode.Force);
                    yield return CoroutineEx.waitForSeconds(1f);
                }

                Ch47.transform.position = landingPos3;
                Ch47.transform.rotation = Quaternion.Euler(landingRot);

                IsEvacuation = true;

                _ins.AlertToAllPlayers("StartDeal", _config.Chat.Prefix);
                foreach (Door door in Doors) door.SetOpen(true);
                foreach (ScientistNPC npc in Zombies)
                {
                    AnimationTransformScientist animation = npc.gameObject.AddComponent<AnimationTransformScientist>();
                    animation.AddPath(Path, _config.Zombies.Speed);
                }
                if (_ins.ActivePveMode) _ins.PveMode.Call("EventAddScientists", _ins.Name, Zombies.Select(x => x.net.ID.Value));

                while (Zombies.Count > 0) yield return CoroutineEx.waitForSeconds(1f);

                IsEvacuation = false;

                SpawnNewCh47(Ch47.transform.position, Ch47.transform.rotation, spawnPos3, 0);
                _ins.AlertToAllPlayers("TakeCH47", _config.Chat.Prefix, Ch47TakeZombies);
                if (!IsAlarm && TimeToFinish > _config.PreFinishTime)
                {
                    if (HackCrate != null && HackCrate.IsBeingHacked()) TimeToFinish = _config.PreFinishTime + (int)(HackableLockedCrate.requiredHackSeconds - HackCrate.hackSeconds);
                    else TimeToFinish = _config.PreFinishTime;
                }

                while (Vector2.Distance(new Vector2(Ch47.transform.position.x, Ch47.transform.position.z), spawnPos2) > 1f) yield return CoroutineEx.waitForSeconds(1f);

                if (Ch47.IsExists()) Ch47.Kill();
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
                Ch47Ai.SetMinHoverHeight(0f);
            }

            internal void SpawnPlane()
            {
                Plane = GameManager.server.CreateEntity("assets/prefabs/npc/cargo plane/cargo_plane.prefab", GetSpawnPosition()) as CargoPlane;
                Plane.enableSaving = false;
                Plane.Spawn();
                Plane.UpdateDropPosition(LandingBradleyPos);
                Plane.secondsToTake *= 1f / _config.ScaleSpeedPlane;
            }

            private static void SpawnSmoke(Vector3 pos)
            {
                SmokeGrenade grenade = GameManager.server.CreateEntity("assets/prefabs/tools/smoke grenade/grenade.smoke.deployed.prefab", pos) as SmokeGrenade;
                grenade.enableSaving = false;
                grenade.Spawn();
                grenade.GetComponent<Rigidbody>().useGravity = false;
            }

            private void SpawnAddBradley()
            {
                AddBradleyPos = GetGlobalPosition(new Vector3(-8.413f, 5.807f, -33.424f));

                CheckTrash(AddBradleyPos, 10f);

                SpawnSmoke(AddBradleyPos);

                AddBradley = GameManager.server.CreateEntity("assets/prefabs/npc/m2bradley/bradleyapc.prefab", AddBradleyPos, GetGlobalRotation(new Vector3(0f, 217.118f, 0f))) as BradleyAPC;

                AddBradley.ScientistSpawnCount = 0;

                AddBradley.enableSaving = false;
                AddBradley.Spawn();

                AddBradley.InstallPatrolPath(new BasePath());
                AddBradley.patrolPath = null;

                AddBradley._maxHealth = _config.Bradley.Hp;
                AddBradley.health = AddBradley._maxHealth;

                AddBradley.maxCratesToSpawn = _config.Bradley.CountCrates;

                AddBradley.viewDistance = _config.Bradley.ViewDistance;
                AddBradley.searchRange = _config.Bradley.SearchRange;

                AddBradley.coaxAimCone *= _config.Bradley.CoaxAimCone;
                AddBradley.coaxFireRate *= _config.Bradley.CoaxFireRate;
                AddBradley.coaxBurstLength = _config.Bradley.CoaxBurstLength;

                AddBradley.nextFireTime = _config.Bradley.NextFireTime;
                AddBradley.topTurretFireRate = _config.Bradley.TopTurretFireRate;

                AddBradley.memoryDuration = _config.Bradley.MemoryDuration;
            }

            internal IEnumerator ProcessBradley(float y)
            {
                Quaternion rot = GetGlobalRotation(new Vector3(0f, 180f, 0f));

                Bradley = GameManager.server.CreateEntity("assets/prefabs/npc/m2bradley/bradleyapc.prefab", new Vector3(LandingBradleyPos.x, y, LandingBradleyPos.z), rot) as BradleyAPC;

                Bradley.ScientistSpawnCount = 0;

                Bradley.enableSaving = false;
                Bradley.Spawn();

                Bradley.myRigidBody.useGravity = false;
                Bradley.myRigidBody.detectCollisions = false;

                Bradley.InstallPatrolPath(new BasePath());
                Bradley.patrolPath = null;

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

                SpawnParachute();

                Bradley.myRigidBody.AddForce(Vector3.down * 1000000f, ForceMode.Force);

                while (Bradley.transform.position.y - LandingBradleyPos.y > 1f)
                {
                    Bradley.myRigidBody.AddForce(Bradley.transform.position.y > _config.HeightCh47 ? Vector3.down * 1000000f : Vector3.down * 100000f, ForceMode.Force);
                    yield return CoroutineEx.waitForSeconds(1f);
                }

                DestroyParachute();
                CheckTrash(LandingBradleyPos, 10f);
                Bradley.transform.position = LandingBradleyPos;
                Bradley.myRigidBody.useGravity = true;
                Bradley.myRigidBody.detectCollisions = true;
                if (_ins.ActivePveMode) _ins.PveMode.Call("EventAddTanks", _ins.Name, new HashSet<ulong> { Bradley.net.ID.Value });
            }

            private void SpawnParachute()
            {
                Parachute parachute = GameManager.server.CreateEntity("assets/prefabs/misc/parachute/parachute.prefab") as Parachute;

                Parachute = parachute.gameObject.AddComponent<BaseVehicle>();
                CopySerializableFields(parachute, Parachute);
                DestroyImmediate(parachute, true);

                Parachute.enableSaving = false;

                Parachute.transform.localPosition = new Vector3(0f, 0.5f, 0f);
                Parachute.SetParent(Bradley);

                Parachute.Spawn();

                Parachute.SetToKinematic();

                PlayerParachute = GameManager.server.CreateEntity("assets/prefabs/player/player.prefab", Bradley.transform.position) as BasePlayer;
                PlayerParachute.Spawn();

                PlayerParachute.DisablePlayerCollider();
                PlayerParachute.playerRigidbody.isKinematic = true;

                Parachute.AttemptMount(PlayerParachute, false);
            }

            private void DestroyParachute()
            {
                if (Parachute.IsExists()) Parachute.Kill();
                if (PlayerParachute.IsExists()) PlayerParachute.Kill();
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
                    ["DamageHelicopter"] = false,
                    ["DamageTurret"] = false,
                    ["TargetNpc"] = config.TargetNpc,
                    ["TargetTank"] = config.TargetTank,
                    ["TargetHelicopter"] = false,
                    ["TargetTurret"] = false,
                    ["CanEnter"] = config.CanEnter,
                    ["CanEnterCooldownPlayer"] = config.CanEnterCooldownPlayer,
                    ["TimeExitOwner"] = config.TimeExitOwner,
                    ["AlertTime"] = config.AlertTime,
                    ["RestoreUponDeath"] = config.RestoreUponDeath,
                    ["CooldownOwner"] = config.CooldownOwner,
                    ["Darkening"] = config.Darkening
                };

                HashSet<ulong> tanks = _config.IsAdditionalBradley ? new HashSet<ulong> { AddBradley.net.ID.Value } : new HashSet<ulong>();

                _ins.PveMode.Call("EventAddPveMode", _ins.Name, dic, transform.position, _config.Radius, Crates.Where(x => x != null && !x.IsDestroyed).Select(x => x.net.ID.Value), Scientists.Where(x => x != null && !x.IsDestroyed).Select(x => x.net.ID.Value), tanks, new HashSet<ulong>(), new HashSet<ulong>(), new HashSet<ulong>(), player);
            }
        }
        #endregion Controller

        #region Animation
        internal class AnimationTransformScientist : FacepunchBehaviour
        {
            private ScientistNPC Main { get; set; } = null;

            private List<Vector3> Path { get; } = new List<Vector3>();

            private float SecondsTaken { get; set; } = 0f;
            private float SecondsToTake { get; set; } = 0f;
            private float WaypointDone { get; set; } = 0f;

            private Vector3 StartPos { get; set; } = Vector3.zero;
            private Vector3 EndPos { get; set; } = Vector3.zero;

            private float Speed { get; set; } = 0f;

            private void Awake()
            {
                Main = GetComponent<ScientistNPC>();
                enabled = false;
            }

            internal void AddPath(HashSet<Vector3> path, float speed)
            {
                foreach (Vector3 point in path) Path.Add(point);
                Speed = speed;
                enabled = true;
            }

            private void FixedUpdate()
            {
                if (SecondsTaken == 0f)
                {
                    if (Path.Count == 0)
                    {
                        StartPos = EndPos = Vector3.zero;
                        SecondsToTake = 0f;
                        SecondsTaken = 0f;
                        WaypointDone = 0f;
                        enabled = false;
                        _ins.Controller.FinishPathZombie(Main);
                        return;
                    }
                    StartPos = transform.position;
                    if (Path[0] != StartPos)
                    {
                        EndPos = Path[0];
                        SecondsToTake = Vector3.Distance(EndPos, StartPos) / Speed;
                        Main.viewAngles = Quaternion.LookRotation(EndPos - StartPos).eulerAngles;
                        SecondsTaken = 0f;
                        WaypointDone = 0f;
                    }
                    Path.RemoveAt(0);
                }
                if (StartPos != EndPos)
                {
                    SecondsTaken += Time.deltaTime;
                    WaypointDone = Mathf.InverseLerp(0f, SecondsToTake, SecondsTaken);
                    transform.position = Vector3.Lerp(StartPos, EndPos, WaypointDone);
                    Main.viewAngles = Quaternion.LookRotation(EndPos - StartPos).eulerAngles;
                    Main.TransformChanged();
                    Main.SendNetworkUpdate();
                    if (WaypointDone >= 1f) SecondsTaken = 0f;
                }
            }
        }
        #endregion Animation

        #region Find Position
        internal MonumentInfo GetMonument()
        {
            List<MonumentInfo> list = Pool.Get<List<MonumentInfo>>();
            foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
            {
                if (monument.displayPhrase.english != "Satellite Dish") continue;
                list.Add(monument);
            }
            MonumentInfo result = list.Count > 0 ? list.GetRandom() : null;
            Pool.FreeUnmanaged(ref list);
            return result;
        }
        #endregion Find Position

        #region Sound
        private Dictionary<string, List<byte[]>> Sound { get; } = new Dictionary<string, List<byte[]>>();

        private void LoadSound()
        {
            if (Interface.Oxide.DataFileSystem.ExistsDatafile("SatelliteDishEvent/sound_en")) Sound.Add("en", Interface.Oxide.DataFileSystem.ReadObject<List<byte[]>>("SatelliteDishEvent/sound_en"));
            if (Interface.Oxide.DataFileSystem.ExistsDatafile("SatelliteDishEvent/sound_ru")) Sound.Add("ru", Interface.Oxide.DataFileSystem.ReadObject<List<byte[]>>("SatelliteDishEvent/sound_ru"));
        }

        private Coroutine PlayCoroutine { get; set; } = null;

        private IEnumerator PlaySoundToPlayer(BasePlayer player)
        {
            if (Sound == null || Sound.Count == 0) yield break;
            string language = lang.GetLanguage(player.UserIDString);
            foreach (byte[] data in Sound.ContainsKey(language) ? Sound[language] : Sound["en"])
            {
                Network.NetWrite netWrite = Network.Net.sv.StartWrite();
                netWrite.PacketID(Network.Message.Type.VoiceData);
                netWrite.UInt64(Controller.Dummy.net.ID.Value);
                netWrite.BytesWithSize(data);
                netWrite.Send(new Network.SendInfo(player.Connection) { priority = Network.Priority.Immediate });
                yield return CoroutineEx.waitForSeconds(0.07f);
            }
        }
        #endregion Sound

        #region Spawn Loot
        #region NPC
        private void OnCorpsePopulate(ScientistNPC entity, NPCPlayerCorpse corpse)
        {
            if (entity == null) return;
            if (Controller.Scientists.Contains(entity))
            {
                Controller.Scientists.Remove(entity);
                PresetConfig preset = _config.Npc.FirstOrDefault(x => x.Config.Name == entity.displayName);
                if (preset == null) return;
                NextTick(() =>
                {
                    if (corpse == null) return;
                    ItemContainer container = corpse.containers[0];
                    if (preset.TypeLootTable == 1 || preset.TypeLootTable == 4 || preset.TypeLootTable == 5)
                    {
                        container.ClearItemsContainer();
                        if (preset.TypeLootTable == 4 || preset.TypeLootTable == 5) AddToContainerPrefab(container, preset.PrefabLootTable);
                        if (preset.TypeLootTable == 1 || preset.TypeLootTable == 5) AddToContainerItem(container, preset.OwnLootTable);
                    }
                    if (preset.Config.IsRemoveCorpse && !corpse.IsDestroyed) corpse.Kill();
                });
            }
            else if (Controller.Zombies.Contains(entity))
            {
                Controller.Zombies.Remove(entity);
                NextTick(() =>
                {
                    if (corpse == null) return;
                    ItemContainer container = corpse.containers[0];
                    if (_config.Zombies.TypeLootTable == 1 || _config.Zombies.TypeLootTable == 4 || _config.Zombies.TypeLootTable == 5)
                    {
                        container.ClearItemsContainer();
                        if (_config.Zombies.TypeLootTable == 4 || _config.Zombies.TypeLootTable == 5) AddToContainerPrefab(container, _config.Zombies.PrefabLootTable);
                        if (_config.Zombies.TypeLootTable == 1 || _config.Zombies.TypeLootTable == 5) AddToContainerItem(container, _config.Zombies.OwnLootTable);
                    }
                    if (_config.Zombies.IsRemoveCorpse && !corpse.IsDestroyed) corpse.Kill();
                });
            }
        }

        private object CanPopulateLoot(ScientistNPC entity, NPCPlayerCorpse corpse)
        {
            if (entity == null || Controller == null) return null;

            if (Controller.Scientists.Contains(entity))
            {
                PresetConfig preset = _config.Npc.FirstOrDefault(x => x.Config.Name == entity.displayName);
                if (preset == null) return null;
                if (preset.TypeLootTable == 2) return null;
                else return true;
            }

            if (Controller.Zombies.Contains(entity))
            {
                if (_config.Zombies.TypeLootTable == 2) return null;
                else return true;
            }

            return null;
        }

        private object OnCustomLootNPC(NetworkableId netId)
        {
            if (Controller == null) return null;

            ScientistNPC entity = Controller.Scientists.FirstOrDefault(x => x.IsExists() && x.net.ID.Value == netId.Value);
            if (entity != null)
            {
                PresetConfig preset = _config.Npc.FirstOrDefault(x => x.Config.Name == entity.displayName);
                if (preset == null) return null;
                if (preset.TypeLootTable == 3) return null;
                else return true;
            }

            if (Controller.Zombies.Any(x => x.IsExists() && x.net.ID.Value == netId.Value))
            {
                if (_config.Zombies.TypeLootTable == 3) return null;
                else return true;
            }

            return null;
        }
        #endregion NPC

        #region Crates
        private bool IsEventBradleyCrate(LootContainer container) => container is LockedByEntCrate && container.ShortPrefabName == "bradley_crate" && (Vector3.Distance(container.transform.position, Controller.LandingBradleyPos) < 10f || (Controller.AddBradleyPos != Vector3.zero && Vector3.Distance(container.transform.position, Controller.AddBradleyPos) < 10f));

        private void OnEntitySpawned(LockedByEntCrate crate)
        {
            if (crate == null) return;
            if (!IsEventBradleyCrate(crate)) return;
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

        private void OnEntitySpawned(HackableLockedCrate crate)
        {
            if (crate == null) return;
            if (Vector2.Distance(new Vector2(crate.transform.position.x, crate.transform.position.z), Controller.DropCratePos2) > 1f) return;

            Controller.HackCrate = crate;

            crate.hackSeconds = HackableLockedCrate.requiredHackSeconds - _config.HackCrate.UnlockTime;

            crate.shouldDecay = false;
            crate.CancelInvoke(crate.DelayedDestroy);

            crate.KillMapMarker();

            if (ActivePveMode) PveMode.Call("EventAddCrates", Name, new HashSet<ulong> { crate.net.ID.Value });

            if (_config.HackCrate.TypeLootTable is 1 or 4 or 5)
            {
                NextTick(() =>
                {
                    crate.inventory.ClearItemsContainer();
                    if (_config.HackCrate.TypeLootTable is 4 or 5) AddToContainerPrefab(crate.inventory, _config.HackCrate.PrefabLootTable);
                    if (_config.HackCrate.TypeLootTable is 1 or 5) AddToContainerItem(crate.inventory, _config.HackCrate.OwnLootTable);
                });
            }
        }

        private object CanPopulateLoot(LootContainer container)
        {
            if (container == null || Controller == null) return null;
            if (Controller.Crates.Contains(container))
            {
                if (_config.TypeLootTableCrates == 2) return null;
                else return true;
            }
            else if (container is HackableLockedCrate && container == Controller.HackCrate)
            {
                if (_config.HackCrate.TypeLootTable == 2) return null;
                else return true;
            }
            else if (IsEventBradleyCrate(container))
            {
                if (_config.Bradley.TypeLootTable == 2) return null;
                else return true;
            }
            else return null;
        }

        private object OnCustomLootContainer(NetworkableId netId)
        {
            if (Controller == null) return null;
            if (Controller.Crates.Any(x => x.IsExists() && x.net.ID.Value == netId.Value))
            {
                if (_config.TypeLootTableCrates == 3) return null;
                else return true;
            }
            else if (Controller.HackCrate.IsExists() && Controller.HackCrate.net.ID.Value == netId.Value)
            {
                if (_config.HackCrate.TypeLootTable == 3) return null;
                else return true;
            }
            LootContainer crate = BaseNetworkable.serverEntities.Find(netId) as LootContainer;
            if (crate != null && IsEventBradleyCrate(crate))
            {
                if (_config.Bradley.TypeLootTable == 3) return null;
                else return true;
            }
            return null;
        }

        private object OnContainerPopulate(LootContainer container)
        {
            if (container == null || Controller == null) return null;
            if (Controller.Crates.Contains(container))
            {
                if (_config.TypeLootTableCrates == 6) return null;
                else return true;
            }
            else if (container is HackableLockedCrate && container == Controller.HackCrate)
            {
                if (_config.HackCrate.TypeLootTable == 6) return null;
                else return true;
            }
            else if (IsEventBradleyCrate(container))
            {
                if (_config.Bradley.TypeLootTable == 6) return null;
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
            foreach (CrateConfig crateConfig in _config.DefaultCrates)
            {
                CheckLootTable(crateConfig.OwnLootTable);
                CheckPrefabLootTable(crateConfig.PrefabLootTable);
            }

            CheckLootTable(_config.HackCrate.OwnLootTable);
            CheckPrefabLootTable(_config.HackCrate.PrefabLootTable);

            CheckLootTable(_config.Bradley.OwnLootTable);
            CheckPrefabLootTable(_config.Bradley.PrefabLootTable);

            foreach (PresetConfig preset in _config.Npc)
            {
                CheckLootTable(preset.OwnLootTable);
                CheckPrefabLootTable(preset.PrefabLootTable);
            }

            CheckLootTable(_config.Zombies.OwnLootTable);
            CheckPrefabLootTable(_config.Zombies.PrefabLootTable);

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

        #region NPCKits
        private object OnNpcKits(ScientistNPC npc)
        {
            if (npc == null || Controller == null) return null;
            if (Controller.Zombies.Contains(npc)) return true;
            else return null;
        }
        #endregion NPCKits

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
            if (Controller == null) return null;
            if (Vector3.Distance(bradley.transform.position, Controller.transform.position) < _config.Radius) return true;
            else return null;
        }

        private object CanCh47SpawnNpc(CH47HelicopterAIController ai)
        {
            if (Controller == null) return null;
            if (Vector3.Distance(ai.transform.position, Controller.transform.position) < _config.Radius) return true;
            else return null;
        }
        #endregion BetterNpc

        #region Bradley Tiers
        private object CanBradleyTiersEdit(BradleyAPC bradley)
        {
            if (Controller == null) return null;
            if (Vector3.Distance(bradley.transform.position, Controller.transform.position) < _config.Radius) return true;
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
                case "Zombie":
                    AddBalance(playerId, _config.Economy.Zombie);
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
            "Npc_KpucTaJl",
            "Crate_KpucTaJl"
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
            "OnSupplyDropDropped",
            "CanHackCrate",
            "OnCrateHack",
            "CanBradleyApcTarget",
            "OnPlayerConnected",
            "OnPlayerDeath",
            "OnEntityDeath",
            "CanMountEntity",
            "OnNpcTarget",
            "OnPhoneAnswered",
            "OnPhoneDialTimedOut",
            "OnPhoneDial",
            "OnEntityKill",
            "OnLootEntity",
            "OnPlayerCommand",
            "OnServerCommand",
            "OnCorpsePopulate",
            "CanPopulateLoot",
            "OnCustomLootNPC",
            "OnCustomLootContainer",
            "OnContainerPopulate",
            "OnEntitySpawned",
            "SetOwnerPveMode",
            "ClearOwnerPveMode",
            "CanEntityTakeDamage",
            "OnNpcKits",
            "CanTeleport",
            "OnPlayerTeleported",
            "CanBradleySpawnNpc",
            "CanCh47SpawnNpc",
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

        private static BaseEntity SpawnEntity(string prefab, Vector3 pos, Quaternion rot)
        {
            BaseEntity entity = GameManager.server.CreateEntity(prefab, pos, rot);
            entity.enableSaving = false;

            GroundWatch groundWatch = entity.GetComponent<GroundWatch>();
            if (groundWatch != null) UnityEngine.Object.DestroyImmediate(groundWatch);

            DestroyOnGroundMissing destroyOnGroundMissing = entity.GetComponent<DestroyOnGroundMissing>();
            if (destroyOnGroundMissing != null) UnityEngine.Object.DestroyImmediate(destroyOnGroundMissing);

            entity.Spawn();

            if (entity is StabilityEntity) (entity as StabilityEntity).grounded = true;
            if (entity is BaseCombatEntity) (entity as BaseCombatEntity).pickup.enabled = false;

            return entity;
        }

        private static void CopySerializableFields<T>(T src, T dst)
        {
            FieldInfo[] srcFields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (FieldInfo field in srcFields)
            {
                object value = field.GetValue(src);
                field.SetValue(dst, value);
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
            webrequest.Enqueue("http://37.153.157.216:5000/Api/GetPluginVersions?pluginName=SatDishEvent", null, (code, response) =>
            {
                if (code != 200 || string.IsNullOrEmpty(response)) return;
                string[] array = response.Replace("\"", string.Empty).Split('.');
                VersionNumber latestVersion = new VersionNumber(Convert.ToInt32(array[0]), Convert.ToInt32(array[1]), Convert.ToInt32(array[2]));
                if (Version < latestVersion) PrintWarning($"A new version ({latestVersion}) of the plugin is available! You need to update the plugin:\n- https://lone.design/product/sat-dish-event-rust-plugin\n- https://codefling.com/plugins/satellite-dish-event");
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
        [ChatCommand("satdishstart")]
        private void ChatStartEvent(BasePlayer player)
        {
            if (player.IsAdmin)
            {
                if (!Active) Start(null);
                else PrintToChat(player, GetMessage("EventActive", player.UserIDString, _config.Chat.Prefix));
            }
        }

        [ChatCommand("satdishstop")]
        private void ChatStopEvent(BasePlayer player)
        {
            if (player.IsAdmin)
            {
                if (Controller != null) Finish();
                else Interface.Oxide.ReloadPlugin(Name);
            }
        }

        [ChatCommand("satdishpos")]
        private void ChatCommandPos(BasePlayer player)
        {
            if (!player.IsAdmin || Controller == null) return;
            Vector3 pos = Controller.transform.InverseTransformPoint(player.transform.position);
            Puts($"Position: {pos}");
            PrintToChat(player, $"Position: {pos}");
        }

        [ConsoleCommand("satdishstart")]
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
            else Puts("This event is active now. To finish this event (satdishstop), then to start the next one");
        }

        [ConsoleCommand("satdishstop")]
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

namespace Oxide.Plugins.SatDishEventExtensionMethods
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

        public static object CallPrivateMethod(this object obj, string methodName, params object[] args)
        {
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            MethodInfo mi = obj.GetType().GetMethod(methodName, flags);
            if (mi != null) return mi.Invoke(obj, args);
            else return null;
        }
    }
}