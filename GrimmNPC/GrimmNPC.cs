using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Facepunch;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProtoBuf;
using Rust;
using Rust.Ai;
using Rust.Ai.Gen2;
using UnityEngine;
using UnityEngine.AI;
using GrimmNPC.NpcSpawnExtensionMethods;
using OxideCompat = GrimmNPC.OxideCompat;

namespace GrimmNPC
{
    /// <summary>
    /// Harmony port of Oxide NpcSpawn 3.3.04. Logic kept identical; only loader/config/hooks adapted for Harmony.
    /// </summary>
    public class GrimmNPC : IHarmonyModHooks
    {
        public static GrimmNPC Instance { get; private set; }
        public static readonly VersionNumber Version = new VersionNumber(3, 3, 4);

        // Public classes for inter-plugin communication
        public class NpcBelt { public string ShortName; public int Amount; public ulong SkinID; public HashSet<string> Mods; public string Ammo; }
        public class NpcWear { public string ShortName; public ulong SkinID; }
        
        public class NpcConfig
        {
            public string Name { get; set; }
            public HashSet<NpcWear> WearItems { get; set; }
            public HashSet<NpcBelt> BeltItems { get; set; }
            public string Kit { get; set; }
            public float Health { get; set; }
            public float RoamRange { get; set; }
            public float ChaseRange { get; set; }
            public float SenseRange { get; set; }
            public float ListenRange { get; set; }
            public float AttackRangeMultiplier { get; set; }
            public bool CheckVisionCone { get; set; }
            public float VisionCone { get; set; }
            public bool HostileTargetsOnly { get; set; }
            public float DamageScale { get; set; }
            public float TurretDamageScale { get; set; }
            public float AimConeScale { get; set; }
            public bool DisableRadio { get; set; }
            public bool CanRunAwayWater { get; set; }
            public bool CanSwim { get; set; }
            public bool CanSleep { get; set; }
            public float SleepDistance { get; set; }
            public float Speed { get; set; }
            public int AreaMask { get; set; }
            public int AgentTypeID { get; set; }
            public string HomePosition { get; set; }
            public float MemoryDuration { get; set; }
            public HashSet<string> States { get; set; }
            public string Gender { get; set; } = "Random";
            public string SkinTone { get; set; } = "Random";
            
            // Teammate NPC options
            [JsonProperty(PropertyName = "Owner UserID (for teammate NPCs)")] public ulong OwnerUserID { get; set; } = 0UL;
            [JsonProperty(PropertyName = "Is Teammate NPC")] public bool IsTeammateNpc { get; set; } = false;
            [JsonProperty(PropertyName = "Can Farm Resources")] public bool CanFarm { get; set; } = false;
            [JsonProperty(PropertyName = "Can Build Structures")] public bool CanBuild { get; set; } = false;
            [JsonProperty(PropertyName = "Farm Range")] public float FarmRange { get; set; } = 50f;
            [JsonProperty(PropertyName = "Build Range")] public float BuildRange { get; set; } = 30f;
            
            // Targeting control options
            [JsonProperty(PropertyName = "Can Be Targeted By AutoTurrets")] public bool CanBeTargetedByAutoTurrets = true;
            [JsonProperty(PropertyName = "Can Be Targeted By GunTraps")] public bool CanBeTargetedByGunTraps = true;
            [JsonProperty(PropertyName = "Can Be Targeted By FlameTurrets")] public bool CanBeTargetedByFlameTurrets = true;
            [JsonProperty(PropertyName = "Can Be Targeted By APC")] public bool CanBeTargetedByAPC = true;
            [JsonProperty(PropertyName = "DisplaySashTargetsOnly")] public bool DisplaySashTargetsOnly { get; set; }
            [JsonProperty(PropertyName = "IgnoreSafeZonePlayers")] public bool IgnoreSafeZonePlayers { get; set; } = true;
            [JsonProperty(PropertyName = "IgnoreSleepingPlayers")] public bool IgnoreSleepingPlayers { get; set; }
            [JsonProperty(PropertyName = "IgnoreWoundedPlayers")] public bool IgnoreWoundedPlayers { get; set; }
            [JsonProperty(PropertyName = "Underwear")] public uint Underwear { get; set; }
            [JsonProperty(PropertyName = "InstantDeathIfHitHead")] public bool InstantDeathIfHitHead { get; set; }
            [JsonProperty(PropertyName = "DestroyTrapsOnDeath")] public bool DestroyTrapsOnDeath { get; set; }
            [JsonProperty(PropertyName = "HeadDamageScale")] public float HeadDamageScale { get; set; } = 1f;
            [JsonProperty(PropertyName = "BodyDamageScale")] public float BodyDamageScale { get; set; } = 1f;
            [JsonProperty(PropertyName = "LegDamageScale")] public float LegDamageScale { get; set; } = 1f;

            // Multiplier for melee damage dealt by this NPC; if 0, DamageScale is used.
            [JsonProperty(PropertyName = "MeleeDamageScale")] public float MeleeDamageScale { get; set; }

            // When true (e.g. from Convoy API), use spawn position as-is after water check; skip strict navmesh Find. Not saved in presets.
            [JsonIgnore] public bool TrustSpawnPosition { get; set; }

            /// <summary>GrimmBoss CustomMap/Global.json spawn: when feet are below the terrain shell, do not apply open-ocean swim column kick or dry-nav bypass.</summary>
            [JsonIgnore] public bool CustomMapAbsolutePosition { get; set; }

            /// <summary>When true (e.g. GrimmBoss), CombatState picks new strafe destinations more often with wider steps.</summary>
            [JsonProperty(PropertyName = "AggressiveCombatStrafe")] public bool AggressiveCombatStrafe { get; set; }
        }

        public enum Gender
        {
            Random,
            Male,
            Female
        }

        public enum SkinTone
        {
            Random,
            Lightest,
            Light,
            Dark,
            Darkest
        }
        #region Config
        private const bool En = true;

        private PluginConfig _config;

        private void LoadDefaultConfig()
        {
            Puts("Creating a default config...");
            _config = PluginConfig.DefaultConfig();
            _config.PluginVersion = GrimmNPC.Version;
            SaveConfig();
            Puts("Creation of the default config completed!");
        }

        private void LoadConfig()
        {
            _config = OxideCompat.ReadConfig<PluginConfig>();
            if (_config == null)
                _config = PluginConfig.DefaultConfig();
            bool configChanged = false;
            if (string.IsNullOrWhiteSpace(_config.Prefab))
            {
                _config.Prefab = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab";
                configChanged = true;
            }
            // Old Harmony GrimmNPC.json / partial configs often lack Weapons maps — required by EquipWeapon.
            if (_config.Weapons == null || _config.Weapons.Count == 0 || _config.WeaponsParameters == null || _config.WeaponsParameters.Count == 0)
            {
                PluginConfig defaults = PluginConfig.DefaultConfig();
                if (_config.Weapons == null || _config.Weapons.Count == 0)
                    _config.Weapons = defaults.Weapons;
                if (_config.WeaponsParameters == null || _config.WeaponsParameters.Count == 0)
                    _config.WeaponsParameters = defaults.WeaponsParameters;
                configChanged = true;
            }
            else
            {
                // Ensure category keys 0-4 exist so GetTypeWeaponItem never KeyNotFound.
                PluginConfig defaults = PluginConfig.DefaultConfig();
                for (int i = 0; i <= 4; i++)
                {
                    if (!_config.Weapons.ContainsKey(i) || _config.Weapons[i] == null)
                    {
                        _config.Weapons[i] = defaults.Weapons.ContainsKey(i) ? defaults.Weapons[i] : new HashSet<string>();
                        configChanged = true;
                    }
                }
            }
            // Ensure PreventScarecrowTargeting is initialized if missing (defaults to true)
            if (_config.PluginVersion < new VersionNumber(2, 8, 32))
            {
                _config.PreventScarecrowTargeting = true;
                configChanged = true;
            }
            // Ensure ForceRespectAiDormant and DefaultSleepDistance are initialized if missing
            if (_config.PluginVersion < new VersionNumber(2, 8, 33))
            {
                _config.ForceRespectAiDormant = false;
                _config.DefaultSleepDistance = 160f;
                _config.PluginVersion = new VersionNumber(2, 8, 33);
                configChanged = true;
            }
            // Ensure EnableUpdateInventoryDebug is initialized if missing (defaults to false)
            if (_config.PluginVersion < new VersionNumber(2, 8, 42))
            {
                _config.EnableUpdateInventoryDebug = false;
                _config.PluginVersion = new VersionNumber(2, 8, 42);
                configChanged = true;
            }
            if (_config.PluginVersion < GrimmNPC.Version)
            {
                UpdateConfigValues();
            }
            else if (configChanged)
            {
                // Save config if we initialized new fields
                SaveConfig();
            }
        }

        private void MergeAdditionalWeaponEntries()
        {
            if (_config.WeaponsParameters == null)
                _config.WeaponsParameters = new Dictionary<string, DefaultSettings>();
            void addParam(string key, DefaultSettings d)
            {
                if (!_config.WeaponsParameters.ContainsKey(key))
                    _config.WeaponsParameters[key] = d;
            }
            addParam("rifle.lr300.space", new DefaultSettings { EffectiveRange = 20f, AttackLengthMin = 0.4f, AttackLengthMax = 0.4f });
            addParam("krieg.shotgun", new DefaultSettings { EffectiveRange = 10f, AttackLengthMin = 0.3f, AttackLengthMax = 1f });
            addParam("krieg.chainsword", new DefaultSettings { EffectiveRange = 3f, AttackLengthMin = -1f, AttackLengthMax = -1f });
            if (_config.Weapons == null) return;
            void addWeapon(int cat, string shortname)
            {
                if (!_config.Weapons.TryGetValue(cat, out HashSet<string> set) || set == null)
                    _config.Weapons[cat] = new HashSet<string> { shortname };
                else
                    set.Add(shortname);
            }
            addWeapon(0, "krieg.chainsword");
            addWeapon(1, "krieg.shotgun");
            addWeapon(3, "rifle.lr300.space");
        }

        private void UpdateConfigValues()
        {
            Puts("Config update detected! Updating config values...");
            if (_config.PluginVersion < new VersionNumber(2, 8, 2))
            {
                _config.WeaponsParameters = new Dictionary<string, DefaultSettings>
                {
                    ["rifle.bolt"] = new DefaultSettings { EffectiveRange = 150f, AttackLengthMin = -1f, AttackLengthMax = -1f },
                    ["speargun"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = -1f, AttackLengthMax = -1f },
                    ["bow.compound"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = -1f, AttackLengthMax = -1f },
                    ["crossbow"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = -1f, AttackLengthMax = -1f },
                    ["bow.hunting"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = -1f, AttackLengthMax = -1f },
                    ["smg.2"] = new DefaultSettings { EffectiveRange = 20f, AttackLengthMin = 0.4f, AttackLengthMax = 0.4f },
                    ["shotgun.double"] = new DefaultSettings { EffectiveRange = 10f, AttackLengthMin = 0.3f, AttackLengthMax = 1f },
                    ["pistol.eoka"] = new DefaultSettings { EffectiveRange = 10f, AttackLengthMin = -1f, AttackLengthMax = -1f },
                    ["rifle.l96"] = new DefaultSettings { EffectiveRange = 150f, AttackLengthMin = -1f, AttackLengthMax = -1f },
                    ["pistol.nailgun"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = 0f, AttackLengthMax = 0.46f },
                    ["pistol.python"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = 0.175f, AttackLengthMax = 0.525f },
                    ["pistol.semiauto"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = 0f, AttackLengthMax = 0.46f },
                    ["pistol.prototype17"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = 0f, AttackLengthMax = 0.46f },
                    ["smg.thompson"] = new DefaultSettings { EffectiveRange = 20f, AttackLengthMin = 0.4f, AttackLengthMax = 0.4f },
                    ["shotgun.waterpipe"] = new DefaultSettings { EffectiveRange = 10f, AttackLengthMin = -1f, AttackLengthMax = -1f },
                    ["multiplegrenadelauncher"] = new DefaultSettings { EffectiveRange = 20f, AttackLengthMin = -1f, AttackLengthMax = -1f },
                    ["snowballgun"] = new DefaultSettings { EffectiveRange = 5f, AttackLengthMin = 2f, AttackLengthMax = 2f },
                    ["legacy bow"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = -1f, AttackLengthMax = -1f },
                    ["blunderbuss"] = new DefaultSettings { EffectiveRange = 10f, AttackLengthMin = 0.3f, AttackLengthMax = 1f },
                    ["revolver.hc"] = new DefaultSettings { EffectiveRange = 20f, AttackLengthMin = 0.4f, AttackLengthMax = 0.4f },
                    ["t1_smg"] = new DefaultSettings { EffectiveRange = 20f, AttackLengthMin = 0.4f, AttackLengthMax = 0.4f },
                    ["minicrossbow"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = -1f, AttackLengthMax = -1f },
                    ["blowpipe"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = -1f, AttackLengthMax = -1f },
                    ["rifle.lr300.space"] = new DefaultSettings { EffectiveRange = 20f, AttackLengthMin = 0.4f, AttackLengthMax = 0.4f },
                    ["krieg.shotgun"] = new DefaultSettings { EffectiveRange = 10f, AttackLengthMin = 0.3f, AttackLengthMax = 1f },
                    ["krieg.chainsword"] = new DefaultSettings { EffectiveRange = 3f, AttackLengthMin = -1f, AttackLengthMax = -1f }
                };
                _config.Weapons = new Dictionary<int, HashSet<string>>
                {
                    [0] = new HashSet<string>
                    {
                        "bone.club",
                        "knife.bone",
                        "knife.butcher",
                        "candycaneclub",
                        "knife.combat",
                        "longsword",
                        "mace",
                        "machete",
                        "paddle",
                        "pitchfork",
                        "salvaged.cleaver",
                        "salvaged.sword",
                        "spear.stone",
                        "spear.wooden",
                        "chainsaw",
                        "hatchet",
                        "jackhammer",
                        "pickaxe",
                        "axe.salvaged",
                        "hammer.salvaged",
                        "icepick.salvaged",
                        "stonehatchet",
                        "stone.pickaxe",
                        "torch",
                        "sickle",
                        "rock",
                        "snowball",
                        "mace.baseballbat",
                        "concretepickaxe",
                        "concretehatchet",
                        "lumberjack.hatchet",
                        "lumberjack.pickaxe",
                        "diverhatchet",
                        "diverpickaxe",
                        "divertorch",
                        "knife.skinning",
                        "vampire.stake",
                        "shovel",
                        "spear.cny",
                        "frontier_hatchet",
                        "boomerang",
                        "krieg.chainsword"
                    },
                    [1] = new HashSet<string>
                    {
                        "speargun",
                        "bow.compound",
                        "crossbow",
                        "bow.hunting",
                        "shotgun.double",
                        "pistol.eoka",
                        "flamethrower",
                        "pistol.m92",
                        "pistol.nailgun",
                        "multiplegrenadelauncher",
                        "shotgun.pump",
                        "pistol.python",
                        "pistol.revolver",
                        "pistol.semiauto",
                        "pistol.prototype17",
                        "snowballgun",
                        "shotgun.spas12",
                        "shotgun.waterpipe",
                        "shotgun.m4",
                        "legacy bow",
                        "military flamethrower",
                        "blunderbuss",
                        "minicrossbow",
                        "blowpipe",
                        "krieg.shotgun"
                    },
                    [2] = new HashSet<string>
                    {
                        "smg.2",
                        "smg.mp5",
                        "rifle.semiauto",
                        "smg.thompson",
                        "rifle.sks",
                        "revolver.hc",
                        "t1_smg"
                    },
                    [3] = new HashSet<string>
                    {
                        "rifle.ak",
                        "rifle.lr300",
                        "lmg.m249",
                        "rifle.m39",
                        "hmlmg",
                        "rifle.ak.ice",
                        "rifle.ak.diver",
                        "minigun",
                        "rifle.ak.med",
                        "rifle.lr300.space"
                    },
                    [4] = new HashSet<string>
                    {
                        "rifle.bolt",
                        "rifle.l96"
                    }
                };
            }
            // Add PreventScarecrowTargeting for existing configs (default to true)
            // Check if version is less than 2.8.32 (when this feature was added)
            if (_config.PluginVersion < new VersionNumber(2, 8, 32))
            {
                // Initialize PreventScarecrowTargeting if it doesn't exist (defaults to false, set to true)
                _config.PreventScarecrowTargeting = true;
            }
            // Add ForceRespectAiDormant and DefaultSleepDistance for existing configs
            // Check if version is less than 2.8.33 (when this feature was added)
            if (_config.PluginVersion < new VersionNumber(2, 8, 33))
            {
                // Initialize ForceRespectAiDormant if it doesn't exist (defaults to false)
                _config.ForceRespectAiDormant = false;
                // Initialize DefaultSleepDistance if it doesn't exist (defaults to 160f)
                _config.DefaultSleepDistance = 160f;
            }
            // Add EnableUpdateInventoryDebug for existing configs
            // Check if version is less than 2.8.42 (when this feature was added)
            if (_config.PluginVersion < new VersionNumber(2, 8, 42))
            {
                // Initialize EnableUpdateInventoryDebug if it doesn't exist (defaults to false)
                _config.EnableUpdateInventoryDebug = false;
            }
            if (_config.PluginVersion < new VersionNumber(2, 8, 45))
                MergeAdditionalWeaponEntries();
            _config.PluginVersion = GrimmNPC.Version;
            Puts("Config update completed!");
            SaveConfig();
        }

        private void SaveConfig() => OxideCompat.WriteConfig(_config);

        internal class DefaultSettings
        {
            [JsonProperty(En ? "Effective Range" : "Дальность прицельной стрельбы")] public float EffectiveRange { get; set; }
            [JsonProperty(En ? "Minimum Attack Duration" : "Минимальная продолжительность стрельбы")] public float AttackLengthMin { get; set; }
            [JsonProperty(En ? "Maximum Attack Duration" : "Максимальная продолжительность стрельбы")] public float AttackLengthMax { get; set; }
        }

        private void DebugLog(string message)
        {
            if (_config != null && _config.EnableDebugLogging)
                Puts(message);
        }

        private class PluginConfig
        {
            [JsonProperty(En ? "Can NpcSpawn NPCs attack animals? [true/false]" : "Могут ли кастомные NPC атаковать животных? [true/false]")] public bool CanTargetAnimal { get; set; }
            [JsonProperty(En ? "Can NpcSpawn NPCs attack other NPCs? [true/false]" : "Могут ли кастомные NPC атаковать других NPC? [true/false]")] public bool CanTargetNpc { get; set; }
            [JsonProperty(En ? "Can NpcSpawn NPCs attack sleeping players? [true/false]" : "Могут ли кастомные NPC атаковать спящих игроков? [true/false]")] public bool CanTargetSleepingPlayer { get; set; }
            [JsonProperty(En ? "Can NpcSpawn NPCs attack wounded players? [true/false]" : "Могут ли кастомные NPC атаковать игроков в состоянии Wounded? [true/false]")] public bool CanTargetWoundedPlayer { get; set; }
            [JsonProperty(En ? "Can NpcSpawn NPCs attack players in SafeZone? [true/false]" : "Могут ли кастомные NPC атаковать игроков в SafeZone? [true/false]")] public bool CanTargetSafeZonePlayer { get; set; }
            [JsonProperty(En ? "Prevent all NPCs (vanilla and custom) from targeting Scarecrow NPCs? [true/false]" : "Предотвратить всем NPC (ванильным и кастомным) атаковать NPC Пугало? [true/false]")] public bool PreventScarecrowTargeting { get; set; }
            [JsonProperty(En ? "Force all NpcSpawn NPCs to respect ai_dormant server command? [true/false] - If true, all NPCs will use server's ai_dormant setting. If false, individual plugins control sleep behavior." : "Принудительно применять команду ai_dormant ко всем NPC от NpcSpawn? [true/false] - Если true, все NPC будут использовать настройку ai_dormant сервера. Если false, отдельные плагины контролируют поведение сна.")] public bool ForceRespectAiDormant { get; set; }
            [JsonProperty(En ? "Default sleep distance for NPCs when ForceRespectAiDormant is enabled (meters) - NPCs will sleep when no players are within this distance. Uses maximum of this value and server's ai_to_player_distance_wakeup_range." : "Расстояние по умолчанию для сна NPC при включенном ForceRespectAiDormant (метры) - NPC будут спать, когда игроки находятся дальше этого расстояния. Используется максимум этого значения и ai_to_player_distance_wakeup_range сервера.")] public float DefaultSleepDistance { get; set; }
            [JsonProperty(En ? "Prefab path used for all NpcSpawn NPCs" : "Используемый prefab для кастомных NPC")] public string Prefab { get; set; }
            [JsonProperty(En ? "Weapons with custom NPC parameters" : "Список оружия, у которого нет значений стандартных параметров для использования NPC")] public Dictionary<string, DefaultSettings> WeaponsParameters { get; set; }
            [JsonProperty(En ? "NPC Weapons by Distance Category" : "Список оружия для использования NPC по категориям в зависимости от расстояния до цели")] public Dictionary<int, HashSet<string>> Weapons { get; set; }
            [JsonProperty(En ? "Configuration version" : "Версия конфигурации")] public VersionNumber PluginVersion { get; set; }
            [JsonProperty(En ? "Enable Debug Logging [true/false]" : "Включить отладочное логирование [true/false]")] public bool EnableDebugLogging { get; set; }
            [JsonProperty(En ? "Enable UpdateInventory Debug Logging [true/false]" : "Включить отладочное логирование UpdateInventory [true/false]")] public bool EnableUpdateInventoryDebug { get; set; }

            public static PluginConfig DefaultConfig()
            {
                return new PluginConfig
                {
                    CanTargetAnimal = false,
                    CanTargetNpc = false,
                    CanTargetSleepingPlayer = false,
                    CanTargetWoundedPlayer = false,
                    CanTargetSafeZonePlayer = false,
                    PreventScarecrowTargeting = true,
                    ForceRespectAiDormant = false,
                    DefaultSleepDistance = 160f,
                    Prefab = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab",
                    EnableDebugLogging = false,
                    EnableUpdateInventoryDebug = false,
                    WeaponsParameters = new Dictionary<string, DefaultSettings>
                    {
                        ["rifle.bolt"] = new DefaultSettings { EffectiveRange = 150f, AttackLengthMin = -1f, AttackLengthMax = -1f },
                        ["speargun"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = -1f, AttackLengthMax = -1f },
                        ["bow.compound"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = -1f, AttackLengthMax = -1f },
                        ["crossbow"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = -1f, AttackLengthMax = -1f },
                        ["bow.hunting"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = -1f, AttackLengthMax = -1f },
                        ["smg.2"] = new DefaultSettings { EffectiveRange = 20f, AttackLengthMin = 0.4f, AttackLengthMax = 0.4f },
                        ["shotgun.double"] = new DefaultSettings { EffectiveRange = 10f, AttackLengthMin = 0.3f, AttackLengthMax = 1f },
                        ["pistol.eoka"] = new DefaultSettings { EffectiveRange = 10f, AttackLengthMin = -1f, AttackLengthMax = -1f },
                        ["rifle.l96"] = new DefaultSettings { EffectiveRange = 150f, AttackLengthMin = -1f, AttackLengthMax = -1f },
                        ["pistol.nailgun"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = 0f, AttackLengthMax = 0.46f },
                        ["pistol.python"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = 0.175f, AttackLengthMax = 0.525f },
                        ["pistol.semiauto"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = 0f, AttackLengthMax = 0.46f },
                        ["pistol.prototype17"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = 0f, AttackLengthMax = 0.46f },
                        ["smg.thompson"] = new DefaultSettings { EffectiveRange = 20f, AttackLengthMin = 0.4f, AttackLengthMax = 0.4f },
                        ["shotgun.waterpipe"] = new DefaultSettings { EffectiveRange = 10f, AttackLengthMin = -1f, AttackLengthMax = -1f },
                        ["multiplegrenadelauncher"] = new DefaultSettings { EffectiveRange = 20f, AttackLengthMin = -1f, AttackLengthMax = -1f },
                        ["snowballgun"] = new DefaultSettings { EffectiveRange = 5f, AttackLengthMin = 2f, AttackLengthMax = 2f },
                        ["legacy bow"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = -1f, AttackLengthMax = -1f },
                        ["blunderbuss"] = new DefaultSettings { EffectiveRange = 10f, AttackLengthMin = 0.3f, AttackLengthMax = 1f },
                        ["revolver.hc"] = new DefaultSettings { EffectiveRange = 20f, AttackLengthMin = 0.4f, AttackLengthMax = 0.4f },
                        ["t1_smg"] = new DefaultSettings { EffectiveRange = 20f, AttackLengthMin = 0.4f, AttackLengthMax = 0.4f },
                        ["minicrossbow"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = -1f, AttackLengthMax = -1f },
                        ["blowpipe"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = -1f, AttackLengthMax = -1f },
                        ["rifle.lr300.space"] = new DefaultSettings { EffectiveRange = 20f, AttackLengthMin = 0.4f, AttackLengthMax = 0.4f },
                        ["krieg.shotgun"] = new DefaultSettings { EffectiveRange = 10f, AttackLengthMin = 0.3f, AttackLengthMax = 1f },
                        ["krieg.chainsword"] = new DefaultSettings { EffectiveRange = 3f, AttackLengthMin = -1f, AttackLengthMax = -1f }
                    },
                    Weapons = new Dictionary<int, HashSet<string>>
                    {
                        [0] = new HashSet<string>
                        {
                            "bone.club",
                            "knife.bone",
                            "knife.butcher",
                            "candycaneclub",
                            "knife.combat",
                            "longsword",
                            "mace",
                            "machete",
                            "paddle",
                            "pitchfork",
                            "salvaged.cleaver",
                            "salvaged.sword",
                            "spear.stone",
                            "spear.wooden",
                            "chainsaw",
                            "hatchet",
                            "jackhammer",
                            "pickaxe",
                            "axe.salvaged",
                            "hammer.salvaged",
                            "icepick.salvaged",
                            "stonehatchet",
                            "stone.pickaxe",
                            "torch",
                            "sickle",
                            "rock",
                            "snowball",
                            "mace.baseballbat",
                            "concretepickaxe",
                            "concretehatchet",
                            "lumberjack.hatchet",
                            "lumberjack.pickaxe",
                            "diverhatchet",
                            "diverpickaxe",
                            "divertorch",
                            "knife.skinning",
                            "vampire.stake",
                            "shovel",
                            "spear.cny",
                            "frontier_hatchet",
                            "boomerang",
                            "krieg.chainsword"
                        },
                        [1] = new HashSet<string>
                        {
                            "speargun",
                            "bow.compound",
                            "crossbow",
                            "bow.hunting",
                            "shotgun.double",
                            "pistol.eoka",
                            "flamethrower",
                            "pistol.m92",
                            "pistol.nailgun",
                            "multiplegrenadelauncher",
                            "shotgun.pump",
                            "pistol.python",
                            "pistol.revolver",
                            "pistol.semiauto",
                            "pistol.prototype17",
                            "snowballgun",
                            "shotgun.spas12",
                            "shotgun.waterpipe",
                            "shotgun.m4",
                            "legacy bow",
                            "military flamethrower",
                            "blunderbuss",
                            "minicrossbow",
                            "blowpipe",
                            "krieg.shotgun"
                        },
                        [2] = new HashSet<string>
                        {
                            "smg.2",
                            "smg.mp5",
                            "rifle.semiauto",
                            "smg.thompson",
                            "rifle.sks",
                            "revolver.hc",
                            "t1_smg"
                        },
                        [3] = new HashSet<string>
                        {
                            "rifle.ak",
                            "rifle.lr300",
                            "lmg.m249",
                            "rifle.m39",
                            "hmlmg",
                            "rifle.ak.ice",
                            "rifle.ak.diver",
                            "minigun",
                            "rifle.ak.med",
                            "rifle.lr300.space"
                        },
                        [4] = new HashSet<string>
                        {
                            "rifle.bolt",
                            "rifle.l96"
                        }
                    },
                    PluginVersion = new VersionNumber()
                };
            }
        }
        #endregion Config

        #region Methods
        // Gender/Skin Tone functionality
        private Dictionary<Gender, Dictionary<SkinTone, List<ulong>>> Models = new Dictionary<Gender, Dictionary<SkinTone, List<ulong>>>();

        private static bool UserIdMatchesFemaleModel(GrimmNPC ins, ulong userId)
        {
            if (ins?.Models == null || userId == 0UL) return false;
            if (!ins.Models.TryGetValue(Gender.Female, out Dictionary<SkinTone, List<ulong>> byTone) || byTone == null) return false;
            foreach (List<ulong> list in byTone.Values)
            {
                if (list != null && list.Contains(userId)) return true;
            }
            return false;
        }
        
        // Track NPC userIDs in use to avoid duplicates across the server
        private readonly HashSet<ulong> UsedNpcUserIds = new HashSet<ulong>();
        
        public static string Get(ulong v) => RandomUsernames.Get((int)(v % 2147483647uL));

        private Gender ParseGender(string genderStr)
        {
            if (string.IsNullOrEmpty(genderStr)) return Gender.Random;
            
            switch (genderStr.ToLower())
            {
                case "male": case "1": return Gender.Male;
                case "female": case "2": return Gender.Female;
                default: return Gender.Random;
            }
        }

        private SkinTone ParseSkinTone(string skinToneStr)
        {
            if (string.IsNullOrEmpty(skinToneStr)) return SkinTone.Random;
            
            switch (skinToneStr.ToLower())
            {
                case "lightest": case "1": return SkinTone.Lightest;
                case "light": case "2": return SkinTone.Light;
                case "dark": case "3": return SkinTone.Dark;
                case "darkest": case "4": return SkinTone.Darkest;
                default: return SkinTone.Random;
            }
        }

        private void SetupModels()
        {
            //PrintWarning("[GENDER DEBUG] Setting up Models dictionary...");
            Models[Gender.Male] = new Dictionary<SkinTone, List<ulong>>();
            Models[Gender.Female] = new Dictionary<SkinTone, List<ulong>>();
            for (int i = 1; i < 5; i++)
            {
                Models[Gender.Male].Add((SkinTone)i, new List<ulong>());
                Models[Gender.Female].Add((SkinTone)i, new List<ulong>());
            }

            for (ulong j = 1; j < 10000000; j++)
            {
                int skinType;
                float meshNumber, skinNumber;
                GetPlayerRandomBodyDetails(j, out skinType, out skinNumber, out meshNumber);
                if (skinType == 1)
                {
                    if (meshNumber < 0.41 && meshNumber > 0.4 && (skinNumber < 1 && skinNumber > 0.66))
                        Models[Gender.Female][(SkinTone)1].Add(j);
                    if (meshNumber < 0.61 && meshNumber > 0.6 && (skinNumber < 0.33 && skinNumber > 0.0))
                        Models[Gender.Female][(SkinTone)2].Add(j);
                    if (meshNumber < 0.71 && meshNumber > 0.7)
                    {
                        if (skinNumber < 1 && skinNumber > 0.66)
                            Models[Gender.Female][(SkinTone)3].Add(j);
                        if (skinNumber < 0.2 && skinNumber > 0.0)
                            Models[Gender.Female][(SkinTone)4].Add(j);
                    }
                }
                else if (skinType == 0)
                {
                    if (meshNumber < 1.00 && meshNumber > 0.99 && (skinNumber < 0.2 && skinNumber > 0.0))
                        Models[Gender.Male][(SkinTone)1].Add(j);
                    if (meshNumber < 0.01 && meshNumber > 0.0 && (skinNumber < 1 && skinNumber > 0.8))
                        Models[Gender.Male][(SkinTone)2].Add(j);
                    if (meshNumber < 0.71 && meshNumber > 0.7)
                    {
                        if (skinNumber < 1 && skinNumber > 0.8)
                            Models[Gender.Male][(SkinTone)3].Add(j);
                        if (skinNumber < 0.2 && skinNumber > 0.0)
                            Models[Gender.Male][(SkinTone)4].Add(j);
                    }
                }
            }
            
            // Log the results
            foreach (var gender in Models.Keys)
            {
                foreach (var skinTone in Models[gender].Keys)
                {
                    //PrintWarning($"[GENDER DEBUG] Models[{gender}][{skinTone}]: {Models[gender][skinTone].Count} user IDs");
                }
            }
        }

        private void GetPlayerRandomBodyDetails(ulong userID, out int skinType, out float skinNumber, out float meshNumber)
        {
            skinType = (GetRandomFloatBasedOnUserID(userID, (ulong)4332) > 0.5f ? 1 : 0);
            meshNumber = GetRandomFloatBasedOnUserID(userID, (ulong)2647);
            skinNumber = GetRandomFloatBasedOnUserID(userID, (ulong)3975);
        }

        private float GetRandomFloatBasedOnUserID(ulong steamid, ulong seed)
        {
            UnityEngine.Random.State state = UnityEngine.Random.state;
            UnityEngine.Random.InitState((int)(seed + steamid));
            float single = UnityEngine.Random.Range(0f, 1f);
            UnityEngine.Random.state = state;
            return single;
        }

        private void ApplyGenderAndSkinTone(ScientistNPC npc, NpcConfig config)
        {
            //PrintWarning($"[GENDER DEBUG] ApplyGenderAndSkinTone called - npc null: {npc == null}, config null: {config == null}");
            if (npc == null || config == null) return;

            // Initialize Models dictionary if not already done
            if (Models.Count == 0)
            {
                SetupModels();
            }

            // Convert string values to enums
            Gender selectedGender = ParseGender(config.Gender);
            SkinTone selectedSkinTone = ParseSkinTone(config.SkinTone);

            //PrintWarning($"[GENDER DEBUG] Original: Gender={config.Gender} -> {selectedGender}, SkinTone={config.SkinTone} -> {selectedSkinTone}");

            // Handle random gender selection
            if (selectedGender == Gender.Random)
            {
                selectedGender = UnityEngine.Random.Range(0, 2) == 0 ? Gender.Male : Gender.Female;
            }

            // Handle random skin tone selection
            if (selectedSkinTone == SkinTone.Random)
            {
                SkinTone[] skinTones = { SkinTone.Lightest, SkinTone.Light, SkinTone.Dark, SkinTone.Darkest };
                selectedSkinTone = skinTones[UnityEngine.Random.Range(0, skinTones.Length)];
            }

            //PrintWarning($"[GENDER DEBUG] Selected: Gender={selectedGender}, SkinTone={selectedSkinTone}");

            // Apply the selected appearance - using BotReSpawn approach
            if (Models.ContainsKey(selectedGender) && Models[selectedGender].ContainsKey(selectedSkinTone))
            {
                List<ulong> availableUserIds = Models[selectedGender][selectedSkinTone];
                //PrintWarning($"[GENDER DEBUG] Available user IDs for {selectedGender}/{selectedSkinTone}: {availableUserIds.Count}");
                if (availableUserIds.Count > 0)
                {
                    // Pick a userID that is not currently in use by any NPC on the server
                    ulong chosen = 0UL;
                    int attempts = Mathf.Min(200, availableUserIds.Count);
                    for (int i = 0; i < attempts; i++)
                    {
                        ulong candidate = availableUserIds[UnityEngine.Random.Range(0, availableUserIds.Count)];
                        if (!UsedNpcUserIds.Contains(candidate))
                        {
                            chosen = candidate;
                            break;
                        }
                    }
                    if (chosen == 0UL)
                    {
                        // Fallback: linear scan to find any free id
                        for (int i = 0; i < availableUserIds.Count; i++)
                        {
                            ulong candidate = availableUserIds[i];
                            if (!UsedNpcUserIds.Contains(candidate))
                            {
                                chosen = candidate;
                                break;
                            }
                        }
                    }
                    if (chosen == 0UL)
                    {
                        // As a last resort, pick any id (very unlikely to collide given the pool size)
                        chosen = availableUserIds[UnityEngine.Random.Range(0, availableUserIds.Count)];
                    }

                    ulong oldUserId = npc.userID;
                    npc.userID = chosen;
                    npc.UserIDString = chosen.ToString();
                    // Do not override npc.displayName here; keep the configured name to avoid init-time race issues

                    // Remember this id to avoid future duplicates until NPC is destroyed
                    if (!UsedNpcUserIds.Contains(chosen)) UsedNpcUserIds.Add(chosen);
                    DebugLog($"[Appearance] '{config.Name ?? npc.displayName ?? "NPC"}': Gender req='{config.Gender}' -> {selectedGender}, SkinTone req='{config.SkinTone}' -> {selectedSkinTone}, userID {oldUserId} -> {chosen}");
                    //PrintWarning($"[GENDER DEBUG] Changed userID from {oldUserId} to {chosen}");
                }
                else
                {
                    //PrintWarning($"[GENDER DEBUG] No user IDs available for {selectedGender}/{selectedSkinTone}");
                }
            }
            else
            {
                //PrintWarning($"[GENDER DEBUG] Models dictionary missing entry for {selectedGender}/{selectedSkinTone}");
            }
        }

        private static bool IsCustomScientist(BaseEntity entity) => entity != null && entity.skinID == 11162132011012;

        /// <summary>
        /// Public API: Spawn NPC by config object. Used by other plugins (e.g. Convoy) via Call("SpawnNpc", position, config).
        /// Accepts NpcConfig or JObject (Convoy 2.9.6 style) for cross-plugin compatibility.
        /// </summary>
        /// <remarks>Oxide plugin interop uses hook names: <c>Plugin.Call("SpawnNpc", …)</c> invokes <see cref="HookMethodAttribute"/> targets.
        /// A private overload must not share the same name or it would also register as hook <c>SpawnNpc</c> and run twice.</remarks>
        public ScientistNPC SpawnNpc(Vector3 position, object configObj)
        {
            DebugLog($"SpawnNpc API: position={position}, argType={configObj?.GetType().Name ?? "null"}");
            NpcConfig config = configObj as NpcConfig;
            if (config != null)
                return SpawnNpcFromConfig(position, config);
            JObject jo = configObj as JObject;
            if (jo != null)
            {
                config = NpcConfigFromJObject(jo);
                if (config == null)
                {
                    PrintWarning($" SpawnNpc: failed to parse JObject config at position {position}");
                    return null;
                }
                DebugLog($"SpawnNpc API: parsed JObject -> Name='{config.Name}', AreaMask={config.AreaMask}, TrustSpawnPosition={config.TrustSpawnPosition}, Gender='{config.Gender}', SkinTone='{config.SkinTone}'");
                return SpawnNpcFromConfig(position, config);
            }
            PrintWarning($" SpawnNpc called with invalid config type at position {position} (expected NpcConfig or JObject).");
            return null;
        }

        /// <summary>BetterNpc compatibility: load preset JSON from oxide/data/NpcSpawn/Preset/.</summary>
        public JObject GetJObject(string presetName) => TryLoadPresetJObject(presetName);

        #region BetterNpc 2.x / legacy preset interop (isolated from SpawnNpc JObject API)

        /// <summary>
        /// NpcSpawn 3.3.0 (KpucTaJl): presets were Oxide <c>ReadObject&lt;NpcConfig&gt;</c> from <c>NpcSpawn/Preset/</c>,
        /// cached in <c>Presets</c>, and <c>SpawnPreset</c> called <c>CreateCustomNpc(position, config)</c> with <b>no</b>
        /// monument nav re-snap — spawn at the given world position using <c>config.Prefab</c>.
        /// This Grimm build uses a heavier <see cref="CreateCustomNpc"/>; only callers that use <see cref="SpawnPreset"/>
        /// (BetterNpc 2.x, etc.) get these in-memory flags so placement matches that legacy behavior.
        /// Does <b>not</b> apply to <see cref="SpawnNpc(Vector3, object)"/> when callers pass their own <see cref="NpcConfig"/> or JObject.
        /// </summary>
        private static void ApplyBetterNpcPresetInteropPlacementHints(NpcConfig cfg)
        {
            if (cfg == null) return;
            cfg.TrustSpawnPosition = true;
            cfg.CustomMapAbsolutePosition = true;
            if (cfg.AreaMask == 1)
                cfg.AreaMask = 25;
        }

        /// <summary>
        /// Legacy preset pipeline: oxide/data/NpcSpawn/Preset/*.json → <see cref="NpcConfigFromJObject"/> → interop hints → private spawn path with <see cref="NpcConfig"/>.
        /// Convoy / Harbor-style integrations should keep using <see cref="SpawnNpc(Vector3, object)"/> with their own config objects.
        /// </summary>
        public ScientistNPC SpawnPreset(Vector3 position, string presetName)
        {
            DebugLog($"SpawnPreset: request '{presetName}' at {position}");
            JObject jo = TryLoadPresetJObject(presetName);
            if (jo == null)
            {
                PrintWarning($" SpawnPreset: no preset file found for '{presetName}' (expected oxide/data/NpcSpawn/Preset/).");
                return null;
            }

            NpcConfig config = NpcConfigFromJObject(jo);
            if (config == null)
            {
                PrintWarning($" SpawnPreset: failed to parse preset JSON for '{presetName}'.");
                return null;
            }

            ApplyBetterNpcPresetInteropPlacementHints(config);
            DebugLog($"SpawnPreset: parsed '{presetName}' -> Name='{config.Name}', calling SpawnNpcFromConfig");
            return SpawnNpcFromConfig(position, config);
        }

        #endregion

        /// <summary>BetterNpc compatibility: literal <c>AreaMask</c> from preset JSON (no spawn-time overrides). Other plugins use <see cref="SpawnNpc"/> with their own config.</summary>
        public int GetAreaMask(string presetName)
        {
            JObject jo = TryLoadPresetJObject(presetName);
            if (jo == null) return 1;
            JToken t = jo["AreaMask"];
            return t != null && t.Type != JTokenType.Null ? t.Value<int>() : 1;
        }

        /// <summary>BetterNpc compatibility: stationary flag from preset States.</summary>
        public bool IsStationaryPreset(string presetName)
        {
            JObject jo = TryLoadPresetJObject(presetName);
            if (jo == null) return false;
            if (jo["States"] is not JArray arr) return false;
            foreach (JToken t in arr)
            {
                string s = t?.ToString();
                if (s == "IdleState" || s == "CombatStationaryState") return true;
            }

            return false;
        }

        /// <summary>BetterNpc compatibility: parent NPC to a cargo / moving entity transform.</summary>
        public void SetParent(ScientistNPC npc, object parentObj, Vector3 localPos, float unusedPadding = 0f)
        {
            if (npc == null || parentObj == null) return;
            Transform tr = parentObj as Transform;
            if (tr == null && parentObj is Component comp) tr = comp.transform;
            if (tr == null) return;
            BaseEntity parent = tr.GetComponentInParent<BaseEntity>();
            if (parent == null) return;
            if (npc is CustomScientistNpc custom) SetParentEntity(custom, parent, localPos);
        }

        /// <summary>Optional preset usage tracking for BetterNpc (no-op if unused).</summary>
        public void RegisterPresetUsage(string presetName, string sourcePlugin, string spawnPointName) { }

        /// <summary>Optional preset usage tracking for BetterNpc (no-op if unused).</summary>
        public void UnregisterPresetUsage(string presetName, string sourcePlugin, string spawnPointName) { }

        private static JObject TryLoadPresetJObject(string presetName)
        {
            if (string.IsNullOrWhiteSpace(presetName)) return null;
            string baseDir = Path.Combine(OxideCompat.DataDirectory, "NpcSpawn", "Preset");
            if (!Directory.Exists(baseDir)) return null;

            string[] candidates =
            {
                presetName + ".json",
                presetName.Replace(' ', '-') + ".json",
                presetName.Replace(" ", string.Empty) + ".json"
            };

            foreach (string file in candidates)
            {
                string full = Path.Combine(baseDir, file);
                if (!File.Exists(full)) continue;
                try
                {
                    return JObject.Parse(File.ReadAllText(full));
                }
                catch (Exception ex)
                {
                    OxideCompat.LogWarning($"[NpcSpawn] Preset file exists but JSON parse failed ({file}): {ex.Message}");
                    continue;
                }
            }

            try
            {
                foreach (string path in Directory.GetFiles(baseDir, "*.json", SearchOption.TopDirectoryOnly))
                {
                    string fn = Path.GetFileNameWithoutExtension(path);
                    if (string.Equals(fn, presetName, StringComparison.OrdinalIgnoreCase)) return JObject.Parse(File.ReadAllText(path));
                    string norm = fn.Replace("-", "").Replace(" ", "");
                    string want = presetName.Replace("-", "").Replace(" ", "");
                    if (norm.Equals(want, StringComparison.OrdinalIgnoreCase)) return JObject.Parse(File.ReadAllText(path));
                }
            }
            catch { /* ignored */ }

            return null;
        }

        /// <summary>Parse Convoy-style JObject into NpcConfig for SpawnNpc API.</summary>
        private static NpcConfig NpcConfigFromJObject(JObject jo)
        {
            if (jo == null) return null;
            var config = new NpcConfig();
            if (jo["Name"] != null && jo["Name"].Type != JTokenType.Null)
                config.Name = jo["Name"].ToString();
            else if (jo["Names"] is JArray nameArr)
            {
                var nameParts = new List<string>(nameArr.Count);
                foreach (JToken t in nameArr) nameParts.Add(t.ToString());
                config.Name = string.Join(", ", nameParts);
            }
            else
                config.Name = jo["Names"]?.ToString() ?? string.Empty;
            config.Kit = jo["Kit"]?.ToString() ?? string.Empty;
            config.Health = jo["Health"] != null ? (float)jo["Health"] : 100f;
            config.RoamRange = jo["RoamRange"] != null ? (float)jo["RoamRange"] : 20f;
            config.ChaseRange = jo["ChaseRange"] != null ? (float)jo["ChaseRange"] : 50f;
            config.SenseRange = jo["SenseRange"] != null ? (float)jo["SenseRange"] : 30f;
            config.ListenRange = jo["ListenRange"] != null ? (float)jo["ListenRange"] : 15f;
            config.AttackRangeMultiplier = jo["AttackRangeMultiplier"] != null ? (float)jo["AttackRangeMultiplier"] : 1f;
            config.CheckVisionCone = jo["CheckVisionCone"] != null && (bool)jo["CheckVisionCone"];
            config.VisionCone = jo["VisionCone"] != null ? (float)jo["VisionCone"] : 120f;
            config.HostileTargetsOnly = jo["HostileTargetsOnly"] != null && (bool)jo["HostileTargetsOnly"];
            config.DisplaySashTargetsOnly = jo["DisplaySashTargetsOnly"] != null && (bool)jo["DisplaySashTargetsOnly"];
            if (jo["IgnoreSafeZonePlayers"] != null) config.IgnoreSafeZonePlayers = (bool)jo["IgnoreSafeZonePlayers"];
            config.IgnoreSleepingPlayers = jo["IgnoreSleepingPlayers"] != null && (bool)jo["IgnoreSleepingPlayers"];
            config.IgnoreWoundedPlayers = jo["IgnoreWoundedPlayers"] != null && (bool)jo["IgnoreWoundedPlayers"];
            config.Underwear = jo["Underwear"] != null ? Convert.ToUInt32(jo["Underwear"].ToObject<long>()) : 0u;
            config.InstantDeathIfHitHead = jo["InstantDeathIfHitHead"] != null && (bool)jo["InstantDeathIfHitHead"];
            config.DestroyTrapsOnDeath = jo["DestroyTrapsOnDeath"] != null && (bool)jo["DestroyTrapsOnDeath"];
            config.HeadDamageScale = jo["HeadDamageScale"] != null ? (float)jo["HeadDamageScale"] : 1f;
            config.BodyDamageScale = jo["BodyDamageScale"] != null ? (float)jo["BodyDamageScale"] : 1f;
            config.LegDamageScale = jo["LegDamageScale"] != null ? (float)jo["LegDamageScale"] : 1f;
            config.MeleeDamageScale = jo["MeleeDamageScale"] != null ? (float)jo["MeleeDamageScale"] : 0f;
            config.DamageScale = jo["DamageScale"] != null ? (float)jo["DamageScale"] : 1f;
            config.TurretDamageScale = jo["TurretDamageScale"] != null ? (float)jo["TurretDamageScale"] : 1f;
            config.AimConeScale = jo["AimConeScale"] != null ? (float)jo["AimConeScale"] : 1f;
            config.DisableRadio = jo["DisableRadio"] != null && (bool)jo["DisableRadio"];
            config.CanRunAwayWater = jo["CanRunAwayWater"] == null || (bool)jo["CanRunAwayWater"];
            // Default true when omitted: GrimmBoss / many callers omit the field; missing must not mean "cannot swim"
            // (PowerShell ConvertTo-Json on boss data files strips absent keys and would otherwise force false here).
            config.CanSwim = jo["CanSwim"] == null || (bool)jo["CanSwim"];
            config.CanSleep = jo["CanSleep"] != null && (bool)jo["CanSleep"];
            config.SleepDistance = jo["SleepDistance"] != null ? (float)jo["SleepDistance"] : 100f;
            config.Speed = jo["Speed"] != null ? (float)jo["Speed"] : 5f;
            config.AreaMask = jo["AreaMask"] != null ? (int)jo["AreaMask"] : 0;
            config.AgentTypeID = jo["AgentTypeID"] != null ? (int)jo["AgentTypeID"] : -1372625422;
            config.HomePosition = jo["HomePosition"]?.ToString() ?? string.Empty;
            config.MemoryDuration = jo["MemoryDuration"] != null ? (float)jo["MemoryDuration"] : 5f;
            // Accept enum names ("Male", "Light") and numeric values ("1", "2").
            config.Gender = jo["Gender"]?.ToString() ?? "Random";
            config.SkinTone = jo["SkinTone"]?.ToString() ?? "Random";
            config.WearItems = new HashSet<NpcWear>();
            if (jo["WearItems"] is JArray wearArr)
                foreach (JToken t in wearArr)
                    if (t is JObject wo && wo["ShortName"] != null)
                        config.WearItems.Add(new NpcWear { ShortName = wo["ShortName"].ToString(), SkinID = wo["SkinID"] != null ? (ulong)wo["SkinID"] : 0UL });
            config.BeltItems = new HashSet<NpcBelt>();
            if (jo["BeltItems"] is JArray beltArr)
                foreach (JToken t in beltArr)
                    if (t is JObject bo && bo["ShortName"] != null)
                    {
                        var modsToken = bo["Mods"] ?? bo["mods"];
                        var mods = new HashSet<string>();
                        if (modsToken is JArray ma) foreach (JToken m in ma) mods.Add(m.ToString());
                        config.BeltItems.Add(new NpcBelt
                        {
                            ShortName = bo["ShortName"].ToString(),
                            Amount = bo["Amount"] != null ? (int)bo["Amount"] : 1,
                            SkinID = bo["SkinID"] != null ? (ulong)bo["SkinID"] : 0UL,
                            Mods = mods,
                            Ammo = bo["Ammo"]?.ToString() ?? string.Empty
                        });
                    }
            config.States = new HashSet<string>();
            if (jo["States"] is JArray statesArr)
                foreach (JToken t in statesArr)
                    config.States.Add(t.ToString());
            config.TrustSpawnPosition = jo["TrustPosition"] != null && (bool)jo["TrustPosition"];
            config.CustomMapAbsolutePosition = jo["CustomMapAbsolutePosition"] != null && (bool)jo["CustomMapAbsolutePosition"];
            config.AggressiveCombatStrafe = jo["AggressiveCombatStrafe"] != null && (bool)jo["AggressiveCombatStrafe"];
            return config;
        }

        /// <summary>
        /// Spawns a custom NPC at the specified position with the given configuration.
        /// </summary>
        private ScientistNPC SpawnNpcFromConfig(Vector3 position, NpcConfig config)
        {
            if (config == null)
            {
                PrintWarning($" SpawnNpc called with null config at position {position}");
                return null;
            }
            
            // Enhanced: Position validation is now handled in CreateCustomNpc
            CustomScientistNpc npc = CreateCustomNpc(position, config);
            if (npc == null)
            {
                DebugLog($"SpawnNpc: CreateCustomNpc returned null for '{config.Name}' at {position} (AreaMask={config.AreaMask}, TrustSpawnPosition={config.TrustSpawnPosition}). Check console for NpcSpawn warnings above.");
                return null;
            }
            npc.skinID = 11162132011012;
            Scientists.Add(npc.net.ID.Value, npc);
            return npc;
        }

        /// <summary>
        /// Spawns a custom-structure-capable NPC. 
        /// Only NPCs spawned via this entry point will enable structure navmesh walking to avoid interfering with regular NPCs.
        /// </summary>
        private ScientistNPC BuildingNPC(Vector3 position, NpcConfig config)
        {
            if (config == null)
            {
                PrintWarning($" BuildingNPC called with null config at position {position}");
                return null;
            }
            
            // Enhanced: Position validation is now handled in CreateCustomNpc
            CustomScientistNpc npc = CreateCustomNpc(position, config);
            if (npc == null) return null;
            npc.skinID = 11162132011012;
            Scientists.Add(npc.net.ID.Value, npc);
            return npc;
        }

        private CustomScientistNpc CreateCustomNpc(Vector3 position, NpcConfig config)
        {
            //PrintWarning($"[GENDER DEBUG] CreateCustomNpc called for config with Gender={config?.Gender}, SkinTone={config?.SkinTone}");
            
            // Enhanced position validation with retry logic and water checking
            // Use areaMask 25 (building navigation) by default - allows spawns on ground, construction, and buildings
            int areaMask = config.AreaMask > 0 ? config.AreaMask : 25;
            if (config.TrustSpawnPosition)
            {
                // API caller (e.g. Convoy, ArmoredTrain): prefer a valid navmesh point near the requested position
                Vector3 requestedPos = position;
                int tryMask = areaMask > 0 ? areaMask : 25;

                // GrimmBoss CustomMapAbsolutePosition (JObject from BuildNpcSpawnJObjectForBoss): exact world XYZ.
                // Independent of CanSwim — if false, the branch below runs EnhancedNavmeshSpawnPoint.Find and/or
                // underwater land-hint rescue, which snaps to nearby dry NavMesh (shore / above ground).
                if (config.CustomMapAbsolutePosition)
                {
                    position = requestedPos;
                }
                else
                {
                float waterLevelProbe = WaterLevel.GetWaterSurface(requestedPos, waves: false, volumes: true);
                // Sunken custom monuments / deep markers: no NavMesh on prefab, but EnhancedNavmeshSpawnPoint.Find
                // can still "succeed" via its dry-land rescue (random samples up to ~150m) and teleport the NPC to shore.
                // When swim is enabled and the marker is intentionally below the water surface, use the exact position.
                // GetWaterSurface can return a value below the walkable deck in deep columns (surface below floor Y),
                // so "below surface" alone is false and rescue still runs — also trust very low Y (sunken POI / harbor floor).
                const float trustSpawnDeepY = -12f;
                bool belowOpenWater = requestedPos.y < waterLevelProbe;
                bool sunkenFloor = config.CanSwim && requestedPos.y <= trustSpawnDeepY;
                if (config.CanSwim && (belowOpenWater || sunkenFloor))
                {
                    position = requestedPos;
                }
                else
                {
                bool found = EnhancedNavmeshSpawnPoint.Find(requestedPos, 20f, out position, tryMask);
                if (!found)
                    found = EnhancedNavmeshSpawnPoint.Find(requestedPos, 20f, out position, 25);
                if (!found)
                {
                    // No valid point within 20m (e.g. train on rails); check water and either use 150m search or nudge
                    float waterLevel = WaterLevel.GetWaterSurface(requestedPos, waves: false, volumes: true);
                    if (requestedPos.y < waterLevel)
                    {
                        if (config.CanSwim)
                        {
                            // Intentional water spawn for swim-enabled NPCs (e.g. GrimmBoss water bosses).
                            position = requestedPos;
                        }
                        else if (!EnhancedNavmeshSpawnPoint.Find(requestedPos, 150f, out position, tryMask))
                        {
                            if (!EnhancedNavmeshSpawnPoint.Find(requestedPos, 150f, out position, 25))
                            {
                                // Land anchor at terrain height (API may pass ocean/world origin), then re-search.
                                float th = TerrainMeta.HeightMap.GetHeight(new Vector3(requestedPos.x, 0f, requestedPos.z));
                                Vector3 landHint = new Vector3(requestedPos.x, th + 1.5f, requestedPos.z);
                                if (EnhancedNavmeshSpawnPoint.Find(landHint, 200f, out position, 25) ||
                                    EnhancedNavmeshSpawnPoint.Find(landHint, 200f, out position, tryMask) ||
                                    EnhancedNavmeshSpawnPoint.Find(landHint, 200f, out position, 1))
                                {
                                    // ok
                                }
                                else
                                {
                                    NavMeshHit allHit;
                                    if (NavMesh.SamplePosition(landHint, out allHit, 280f, NavMesh.AllAreas))
                                    {
                                        float w2 = WaterLevel.GetWaterSurface(allHit.position, waves: false, volumes: true);
                                        if (allHit.position.y >= w2 - 0.25f)
                                            position = allHit.position;
                                        else
                                        {
                                            PrintWarning($" TrustSpawnPosition: position underwater, no valid dry NavMesh within rescue range for '{config?.Name ?? "NPC"}'.");
                                            return null;
                                        }
                                    }
                                    else
                                    {
                                        PrintWarning($" TrustSpawnPosition: position underwater, no valid spot within 150m (and rescue failed) for '{config?.Name ?? "NPC"}'.");
                                        return null;
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // No navmesh within 20m (e.g. train on rails); try larger radius to find ground beside tracks
                        bool foundFar = EnhancedNavmeshSpawnPoint.Find(requestedPos, 50f, out position, tryMask) ||
                                        EnhancedNavmeshSpawnPoint.Find(requestedPos, 50f, out position, 25);
                        if (!foundFar)
                            foundFar = EnhancedNavmeshSpawnPoint.Find(requestedPos, 100f, out position, tryMask) ||
                                       EnhancedNavmeshSpawnPoint.Find(requestedPos, 100f, out position, 25);
                        if (!foundFar)
                        {
                            NavMeshHit rescueHit;
                            if (NavMesh.SamplePosition(requestedPos, out rescueHit, 150f, tryMask) ||
                                NavMesh.SamplePosition(requestedPos, out rescueHit, 150f, 25) ||
                                NavMesh.SamplePosition(requestedPos, out rescueHit, 150f, 1))
                                position = rescueHit.position;
                            // Monuments / DLC tiles sometimes bake walkable mesh on area types outside 1+8+16; AllAreas matches underwater rescue used above.
                            else if (NavMesh.SamplePosition(requestedPos, out rescueHit, 280f, NavMesh.AllAreas))
                            {
                                float wDry = WaterLevel.GetWaterSurface(rescueHit.position, waves: false, volumes: true);
                                if (rescueHit.position.y >= wDry - 0.25f)
                                    position = rescueHit.position;
                                else
                                {
                                    PrintWarning($" TrustSpawnPosition: AllAreas hit underwater near {requestedPos} for '{config?.Name ?? "NPC"}'.");
                                    return null;
                                }
                            }
                            else
                            {
                                PrintWarning($" TrustSpawnPosition: no NavMesh within 150m of {requestedPos} for '{config?.Name ?? "NPC"}'.");
                                return null;
                            }
                        }
                    }
                }
                }
                }
            }
            else
            {
                float searchRadius = 60f;
                if (!EnhancedNavmeshSpawnPoint.Find(position, searchRadius, out position, areaMask))
                {
                    searchRadius = 120f;
                    if (!EnhancedNavmeshSpawnPoint.Find(position, searchRadius, out position, areaMask))
                    {
                        if (NavMesh.SamplePosition(position, out NavMeshHit nmFallback, 200f, NavMesh.AllAreas))
                        {
                            float w = WaterLevel.GetWaterSurface(nmFallback.position, waves: false, volumes: true);
                            if (nmFallback.position.y >= w - 0.25f)
                                position = nmFallback.position;
                            else
                            {
                                PrintWarning($" Failed to find dry spawn position near {position} (60m/120m + AllAreas underwater) for '{config?.Name ?? "NPC"}'.");
                                return null;
                            }
                        }
                        else
                        {
                            PrintWarning($" Failed to find valid spawn position near {position} (tried 60m and 120m)");
                            return null;
                        }
                    }
                }
            }
            
            string prefabPath = string.IsNullOrWhiteSpace(_config?.Prefab)
                ? "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab"
                : _config.Prefab;
            ScientistNPC scientistNpc = GameManager.server.CreateEntity(prefabPath, position, Quaternion.identity, false) as ScientistNPC;
            if (scientistNpc == null)
            {
                string prefabInfo = prefabPath ?? "null";
                PrintWarning($" CreateCustomNpc: CreateEntity returned null at {position} (prefab={prefabInfo}). If using TrustPosition, ensure position is above ground/water.");
                return null;
            }
            
            ScientistBrain scientistBrain = scientistNpc.GetComponent<ScientistBrain>();
            NPCPlayerNavigator navigator = scientistNpc.GetComponent<NPCPlayerNavigator>();

            //PrintWarning($"[GENDER DEBUG] About to call ApplyGenderAndSkinTone...");
            // Apply gender and skin tone before creating custom components
            ApplyGenderAndSkinTone(scientistNpc, config);

            CustomScientistNpc customScientist = scientistNpc.gameObject.AddComponent<CustomScientistNpc>();
            CustomScientistBrain customScientistBrain = scientistNpc.gameObject.AddComponent<CustomScientistBrain>();

            // Copy fields before destroying base components
            CopySerializableFields(scientistNpc, customScientist);
            CopySerializableFields(scientistBrain, customScientistBrain);

            // Preserve additional properties before destruction
            customScientist.DeathEffects = scientistNpc.DeathEffects;
            customScientist.RadioChatterEffects = scientistNpc.RadioChatterEffects;
            customScientist.IdleChatterRepeatRange = scientistNpc.IdleChatterRepeatRange;

            // Set config before destroying base components
            customScientist.Config = config;
            customScientist.Brain = customScientistBrain;
            customScientist.enableSaving = false;
            
            // Set Npc reference in brain BEFORE destroying components to prevent NullReferenceException
            // AddStates() can be called during Start() before InitializeAI(), so we need this set early
            customScientistBrain.Npc = customScientist;

            // PERFORMANCE OPTIMIZATION: Use DestroyImmediate but only after ensuring all data is copied
            // The deferred Destroy() was causing timing issues (double init, null references)
            // We still optimize by ensuring everything is ready before destroying
            // This ensures FSMComponent and other EntityComponents use CustomScientistNpc as baseEntity
            // EntityComponent.baseEntity uses GameObjectEx.ToBaseEntity() which will find CustomScientistNpc
            
            // Now destroy base components - must use DestroyImmediate to prevent double initialization
            // The performance hit is acceptable since we've optimized the copy operations
            UnityEngine.Object.DestroyImmediate(scientistNpc, true);
            UnityEngine.Object.DestroyImmediate(scientistBrain, true);
            // KEEP the original NPC navigator component to satisfy NPCPlayer.ServerInit expectations

            // Awake and Spawn after base components are destroyed
            // CustomScientistNpc will be the primary BaseEntity component now
            customScientist.gameObject.AwakeFromInstantiate();
            
            // Call spawn hook before actually spawning
            object hookResult = OxideCompat.CallHook("OnCustomNpcSpawned", customScientist);
            if (hookResult is bool && !(bool)hookResult)
            {
                PrintWarning($" CreateCustomNpc: OnCustomNpcSpawned hook cancelled spawn for '{config?.Name ?? "NPC"}' at {position}");
                UnityEngine.Object.DestroyImmediate(customScientist.gameObject);
                return null;
            }
            
            // Spawn will call ServerInit - CustomScientistNpc is now the primary BaseEntity
            // FSMComponent will use CustomScientistNpc as baseEntity via GameObjectEx.ToBaseEntity()
            customScientist.Spawn();

            // TrustPosition (GrimmBoss / API): ServerInit finishes before NavMeshAgent always reports isOnNavMesh; one tick helps registration on monument bakes.
            if (config.TrustSpawnPosition)
            {
                CustomScientistNpc npcDeferred = customScientist;
                timer.Once(0.08f, () =>
                {
                    if (npcDeferred == null || npcDeferred.IsDestroyed) return;
                    try
                    {
                        if (npcDeferred.Brain?.Navigator != null)
                        {
                            npcDeferred.Brain.Navigator.SetNavMeshEnabled(true);
                            npcDeferred.Brain.Navigator.PlaceOnNavMesh(14f);
                        }
                    }
                    catch { }
                });
            }

            try
            {
                // Minimal post-spawn diagnostics to help trace navmesh issues
                var agent = customScientist.GetComponent<UnityEngine.AI.NavMeshAgent>();
                string navInfo = agent != null ? $"agentType={agent.agentTypeID} onNavMesh={agent.isOnNavMesh}" : "no NavMeshAgent";
                DebugLog($"Spawned '{config?.Name ?? "NPC"}' at {position} ({navInfo}), areaMask={areaMask}");
            }
            catch {}

            return customScientist;
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

        private void AddTargetRaid(CustomScientistNpc npc, HashSet<BuildingBlock> foundations) { if (IsCustomScientist(npc)) npc.Foundations = foundations; }

        private void AddTargetGuard(CustomScientistNpc npc, BaseEntity target)
        {
            if (!IsCustomScientist(npc) || target == null) return;
            if (npc.GuardTarget != null) return;
            npc.AddTargetGuard(target);
        }

        private void SetParentEntity(CustomScientistNpc npc, BaseEntity parent, Vector3 pos) { if (IsCustomScientist(npc) && parent != null) npc.SetParentEntity(parent, pos); }

        private void SetHomePosition(CustomScientistNpc npc, Vector3 pos)
        {
            if (!IsCustomScientist(npc)) return;
            npc.HomePosition = pos;
            // If currently roaming, switch to idle state when home position changes
            if (npc.Brain != null && npc.Brain.CurrentState != null && npc.Brain.CurrentState.StateType == AIState.Roam)
            {
                npc.Brain.SwitchToState(AIState.Idle, 0);
            }
        }

        private void SetCurrentWeapon(CustomScientistNpc npc, Item weapon) { if (IsCustomScientist(npc)) npc.EquipCurrentWeapon(weapon); }

        /// <summary>
        /// Sets the owner of a teammate NPC. This makes the NPC friendly to the owner and their team.
        /// </summary>
        private void SetTeammateOwner(CustomScientistNpc npc, ulong ownerUserID)
        {
            if (!IsCustomScientist(npc)) return;
            npc.Config.IsTeammateNpc = true;
            npc.Config.OwnerUserID = ownerUserID;
        }

        private BasePlayer GetCurrentTarget(CustomScientistNpc npc) => IsCustomScientist(npc) && npc.IsBasePlayerTarget ? npc.GetBasePlayerTarget : null;

        private void AddStates(CustomScientistNpc npc, HashSet<string> states)
        {
            if (states.Contains("RoamState")) npc.Brain.AddState(new CustomScientistBrain.RoamState(npc));
            if (states.Contains("ChaseState")) npc.Brain.AddState(new CustomScientistBrain.ChaseState(npc));
            if (states.Contains("CombatState")) npc.Brain.AddState(new CustomScientistBrain.CombatState(npc));
            if (states.Contains("CombatStationaryState")) npc.Brain.AddState(new CustomScientistBrain.CombatStationaryState(npc));
            if (states.Contains("RaidState")) npc.Brain.AddState(new CustomScientistBrain.RaidState(npc));
            if (states.Contains("RaidStateMelee")) npc.Brain.AddState(new CustomScientistBrain.RaidStateMelee(npc));
            if (states.Contains("SledgeState")) npc.Brain.AddState(new CustomScientistBrain.SledgeState(npc));
            if (states.Contains("BlazerState")) npc.Brain.AddState(new CustomScientistBrain.BlazerState(npc));
            if (states.Contains("FarmState")) npc.Brain.AddState(new CustomScientistBrain.FarmState(npc));
            if (states.Contains("BuildState")) npc.Brain.AddState(new CustomScientistBrain.BuildState(npc));
        }
        #endregion Methods

        #region Controller
        public class CustomScientistNpc : ScientistNPC
        {
            private PluginConfig _config => _ins._config;

            public NpcConfig Config { get; set; } = null;

            public Vector3 HomePosition { get; set; } = Vector3.zero;

            public float DistanceFromBase => Vector3.Distance(transform.position, HomePosition);

            // Enhanced roam point management (from BotReSpawn improvements)
            public Vector3 RoamPoint { get; set; } = Vector3.zero;
            public float RoamDistance { get; set; } = 0f;
            public float RoamDistance1 { get; set; } = 0f;
            public DateTime LastMove { get; set; } = DateTime.Now;

            private float _nextSwimNavDebugRealtime;
            private float _nextSwimColumnKickRealtime;
            private bool _swimColumnKickDone;

            public override void ServerInit()
            {
                // Ensure Config is set before doing anything (prevents NullReferenceException)
                if (Config == null)
                {
                    _ins.PrintWarning($" CustomScientistNpc.ServerInit() called but Config is null! Destroying to avoid NRE.");
                    try { Kill(); } catch { }
                    return; // do NOT call base.ServerInit when misconfigured
                }

                LegacyNavigation = false;
                
                // Ensure Brain is set before calling base.ServerInit()
                // HumanNPC.ServerInit() does Brain = GetComponent<ScientistBrain>(),
                // which should find CustomScientistBrain since it extends ScientistBrain
                // But we've already set it manually, so this is just a safeguard
                if (Brain == null)
                {
                    Brain = GetComponent<CustomScientistBrain>();
                    if (Brain == null)
                    {
                        Brain = GetComponent<ScientistBrain>();
                    }
                }
                
                base.ServerInit();

                // Ensure we have a valid position before setting HomePosition
                if (transform == null)
                {
                    _ins.PrintWarning($" CustomScientistNpc.ServerInit() called but transform is null!");
                    return;
                }
                
                // Double-check Brain is set after base.ServerInit() (in case it was overwritten)
                if (Brain == null || !(Brain is CustomScientistBrain))
                {
                    Brain = GetComponent<CustomScientistBrain>();
                    if (Brain == null)
                    {
                        _ins.PrintWarning($" CustomScientistBrain not found after base.ServerInit()!");
                    }
                }

                HomePosition = string.IsNullOrEmpty(Config.HomePosition) ? transform.position : Config.HomePosition.ToVector3();
                
                // Initialize roam point to home position
                RoamPoint = HomePosition;
                LastMove = DateTime.Now;

                if (NavAgent == null) NavAgent = GetComponent<RustNavMeshAgent>();
                if (NavAgent != null)
                {
                    // Enhanced: Use config values if set, otherwise use defaults optimized for building navigation
                    // AreaMask 25 = Ground (1) + Construction (8) + Buildings (16) = all building surfaces
                    int effectiveAreaMask = Config.AreaMask > 0 ? Config.AreaMask : 25;
                    NavAgent.areaMask = effectiveAreaMask;
                    // Agent type 0 is invalid on Rust NavMeshAgents (Unity: "No navmesh areas matching agent type? Agent type: 0"). Humanoid matches terrain + construction (mask 25).
                    int agentTypeId = Config.AgentTypeID;
                    if (agentTypeId == 0)
                        agentTypeId = -1372625422;
                    NavAgent.agentTypeID = agentTypeId;
                    
                    // Ensure BaseNavigator is configured for building navigation
                    if (Brain != null && Brain.Navigator != null)
                    {
                        Brain.Navigator.CanUseBaseNav = true;
                        Brain.Navigator.CanUseNavMesh = true;
                        Brain.Navigator.DefaultArea = "NavMesh";
                        Brain.Navigator.MoveTowardsSpeed = BaseNavigator.NavigationSpeed.Fast;
                        Brain.Navigator.FaceMoveTowardsTarget = true;
                    }
                }

                startHealth = Config.Health;
                _health = Config.Health;

                damageScale = Config.DamageScale;

                if (Config.DisableRadio)
                {
                    CancelInvoke(PlayRadioChatter);
                    RadioChatterEffects = Array.Empty<GameObjectRef>();
                    DeathEffects = Array.Empty<GameObjectRef>();
                }

                inventory.containerWear.ClearItemsContainer();
                inventory.containerBelt.ClearItemsContainer();
                if (!string.IsNullOrEmpty(Config.Kit) && _ins.Kits.Exists) _ins.Kits.Call("GiveKit", this, Config.Kit);
                else UpdateInventory();

                ApplyUnderwearSkin();

                if (IsBomber) SpawnTimedExplosive();

                InvokeRepeating(LightCheck, 1f, 30f);
                InvokeRepeating(UpdateTick, 1f, 2f);
                InvokeRepeating(CheckDestinationReached, 0.5f, 0.5f);
            }

            

            // Freeze support
            public bool IsFrozen { get; private set; } = false;

            public void Freeze(float duration)
            {
                IsFrozen = true;
                if (Brain != null && Brain.Navigator != null)
                {
                    Brain.Navigator.Stop();
                    Brain.Navigator.Pause();
                }
                CancelInvoke(nameof(Unfreeze));
                if (duration > 0f) Invoke(nameof(Unfreeze), duration);
            }

            public void Unfreeze()
            {
                IsFrozen = false;
                if (Brain != null && Brain.Navigator != null)
                {
                    Brain.Navigator.Resume();
                }
            }

			private void UpdateInventory()
			{
				// Guard against null config or containers during early initialization
				if (Config == null || inventory == null || inventory.containerWear == null || inventory.containerBelt == null)
				{
					if (_ins._config.EnableUpdateInventoryDebug)
						_ins.PrintWarning($" UpdateInventory: Config or inventory is null. Config={Config != null}, inventory={inventory != null}, containerWear={inventory?.containerWear != null}, containerBelt={inventory?.containerBelt != null}");
					return;
				}

				// Debug: Check if WearItems/BeltItems are null or empty
				if (_ins._config.EnableUpdateInventoryDebug)
				{
					_ins.PrintWarning($" UpdateInventory: WearItems={Config.WearItems != null} (Count={Config.WearItems?.Count ?? 0}), BeltItems={Config.BeltItems != null} (Count={Config.BeltItems?.Count ?? 0})");
				}

				if (Config.WearItems != null && Config.WearItems.Count > 0)
				{
					int addedCount = 0;
					// Use IEnumerable to handle runtime-created HashSet types
					foreach (object wearObj in Config.WearItems)
					{
						if (wearObj == null)
						{
							if (_ins._config.EnableUpdateInventoryDebug)
								_ins.PrintWarning($" UpdateInventory: Found null NpcWear item in WearItems");
							continue;
						}
						
						// Use reflection to get ShortName and SkinID fields (NpcWear uses fields, not properties)
						var wearType = wearObj.GetType();
						var shortNameField = wearType.GetField("ShortName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
						var skinIdField = wearType.GetField("SkinID", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
						
						if (shortNameField == null || skinIdField == null)
						{
							if (_ins._config.EnableUpdateInventoryDebug)
								_ins.PrintWarning($" UpdateInventory: NpcWear type missing ShortName or SkinID field");
							continue;
						}
						
						string shortName = shortNameField.GetValue(wearObj) as string ?? string.Empty;
						ulong skinID = skinIdField.GetValue(wearObj) is ulong sid ? sid : 0UL;
						
						Item item = ItemManager.CreateByName(shortName, 1, skinID);
						if (item == null)
						{
							if (_ins._config.EnableUpdateInventoryDebug)
								_ins.PrintWarning($" UpdateInventory: Failed to create item '{shortName}'");
							continue;
						}
						if (item.MoveToContainer(inventory.containerWear))
							addedCount++;
						else
						{
							item.Remove();
							if (_ins._config.EnableUpdateInventoryDebug)
								_ins.PrintWarning($" UpdateInventory: Failed to move '{shortName}' to containerWear");
						}
					}
					if (_ins._config.EnableDebugLogging)
						_ins.PrintWarning($" UpdateInventory: Added {addedCount}/{Config.WearItems.Count} wear items");
				}
				else if (_ins._config.EnableUpdateInventoryDebug)
				{
					_ins.PrintWarning($" UpdateInventory: WearItems is null or empty, skipping wear items");
				}

				if (Config.BeltItems != null && Config.BeltItems.Count > 0)
				{
					int addedCount = 0;
					// Use IEnumerable to handle runtime-created HashSet types
					foreach (object beltObj in Config.BeltItems)
					{
						if (beltObj == null)
						{
							if (_ins._config.EnableUpdateInventoryDebug)
								_ins.PrintWarning($" UpdateInventory: Found null NpcBelt item in BeltItems");
							continue;
						}
						
						// Use reflection to get fields (NpcBelt uses fields, not properties)
						var beltType = beltObj.GetType();
						var shortNameField = beltType.GetField("ShortName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
						var amountField = beltType.GetField("Amount", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
						var skinIdField = beltType.GetField("SkinID", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
						var modsField = beltType.GetField("Mods", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
						
						if (shortNameField == null || amountField == null || skinIdField == null)
						{
							if (_ins._config.EnableUpdateInventoryDebug)
								_ins.PrintWarning($" UpdateInventory: NpcBelt type missing required fields");
							continue;
						}
						
						string shortName = shortNameField.GetValue(beltObj) as string ?? string.Empty;
						int amount = amountField.GetValue(beltObj) is int amt ? amt : 1;
						ulong skinID = skinIdField.GetValue(beltObj) is ulong sid ? sid : 0UL;
						
						Item item = ItemManager.CreateByName(shortName, amount, skinID);
						if (item == null)
						{
							if (_ins._config.EnableUpdateInventoryDebug)
								_ins.PrintWarning($" UpdateInventory: Failed to create item '{shortName}'");
							continue;
						}
						if (item.MoveToContainer(inventory.containerBelt))
						{
							addedCount++;
							// Handle mods using reflection
							if (modsField != null && item.contents != null)
							{
								var modsValue = modsField.GetValue(beltObj);
								if (modsValue != null && modsValue is System.Collections.IEnumerable modsEnumerable)
								{
									foreach (object modObj in modsEnumerable)
									{
										string modShortName = modObj?.ToString() ?? string.Empty;
										if (string.IsNullOrEmpty(modShortName)) continue;
										ItemDefinition mod = ItemManager.FindItemDefinition(modShortName);
										if (mod == null) continue;
										item.contents.AddItem(mod, 1);
									}
								}
							}
						}
						else
						{
							item.Remove();
							if (_ins._config.EnableUpdateInventoryDebug)
								_ins.PrintWarning($" UpdateInventory: Failed to move '{shortName}' to containerBelt");
						}
					}
					if (_ins._config.EnableDebugLogging)
						_ins.PrintWarning($" UpdateInventory: Added {addedCount}/{Config.BeltItems.Count} belt items");
				}
				else if (_ins._config.EnableUpdateInventoryDebug)
				{
					_ins.PrintWarning($" UpdateInventory: BeltItems is null or empty, skipping belt items");
				}
			}

            private void ApplyUnderwearSkin()
            {
                if (Config == null) return;
                uint uw = Config.Underwear;
                if (uw == 0) return;
                if (uw == 359039573u && !UserIdMatchesFemaleModel(_ins, userID)) uw = 0;
                if (uw == 2059471831u && UserIdMatchesFemaleModel(_ins, userID)) uw = 0;
                if (uw == 0) return;
                nextUnderwearValidationTime = float.PositiveInfinity;
                lastValidUnderwearSkin = uw;
            }

            private void FinalizeDeployedTraps()
            {
                if (_deployedTraps == null || _deployedTraps.Count == 0) return;
                if (Config != null && Config.DestroyTrapsOnDeath)
                {
                    foreach (BaseTrap t in _deployedTraps)
                    {
                        if (t != null && t.IsExists()) t.Kill();
                    }
                }
                else
                {
                    foreach (BaseTrap t in _deployedTraps)
                    {
                        if (t == null || !t.IsExists()) continue;
                        t.decay = PrefabAttribute.server.Find<Decay>(2982625522);
                    }
                }
                _deployedTraps.Clear();
                _deployedTraps = null;
            }

            internal void TryDeployTrapDuringRoam()
            {
                if (_ins == null || Config == null || Brain == null) return;
                if (!Config.States.Contains("RoamState") || Config.RoamRange <= 2f) return;
                if (!(Brain.CurrentState is CustomScientistBrain.RoamState)) return;
                if (DistanceFromBase > Config.RoamRange) return;
                if (InSafeZone()) return;
                if (UnityEngine.Random.Range(0f, 100f) > 25f) return;
                if (inventory?.containerBelt?.itemList == null) return;
                Item item = null;
                foreach (Item beltItem in inventory.containerBelt.itemList)
                {
                    if (_ins.Traps.Contains(beltItem.info.shortname)) { item = beltItem; break; }
                }
                if (item == null) return;
                if (!Physics.Raycast(transform.position, Vector3.down, out RaycastHit raycastHit, 0.25f, 8454144)) return;
                List<BaseTrap> nearby = Pool.Get<List<BaseTrap>>();
                Vis.Entities<BaseTrap>(transform.position, 6f, nearby, 256);
                bool hasTrap = false;
                foreach (BaseTrap trap in nearby)
                {
                    if (trap != null && (trap is Landmine || trap is BearTrap)) { hasTrap = true; break; }
                }
                Pool.FreeUnmanaged(ref nearby);
                if (hasTrap) return;
                string prefab = item.info.shortname switch
                {
                    "trap.landmine" => "assets/prefabs/deployable/landmine/landmine.prefab",
                    "trap.bear" => "assets/prefabs/deployable/bear trap/beartrap.prefab",
                    _ => string.Empty
                };
                if (string.IsNullOrEmpty(prefab)) return;
                if (item.amount == 1) item.Remove();
                else
                {
                    item.amount--;
                    item.MarkDirty();
                }
                BaseTrap entity = GameManager.server.CreateEntity(prefab, raycastHit.point, transform.rotation) as BaseTrap;
                if (entity == null) return;
                entity.OwnerID = userID;
                entity.pickup.enabled = false;
                entity.startHealth = entity._health = 25f;
                entity.enableSaving = false;
                entity.Spawn();
                entity.SetFlag(BaseEntity.Flags.Busy, true);
                _deployedTraps ??= new HashSet<BaseTrap>();
                _deployedTraps.Add(entity);
            }

            private void OnDestroy()
            {
                FinalizeDeployedTraps();
                if (HealCoroutine != null) ServerMgr.Instance.StopCoroutine(HealCoroutine);
                if (FireC4Coroutine != null) ServerMgr.Instance.StopCoroutine(FireC4Coroutine);
                if (FireRocketLauncherCoroutine != null) ServerMgr.Instance.StopCoroutine(FireRocketLauncherCoroutine);
                CancelInvoke();
                if (BomberTimedExplosive.IsExists()) BomberTimedExplosive.Kill();
            }

            // Helper method to check if NPC should be dormant
            // Note: Uses different name to avoid conflict with NPCPlayer.IsDormant property
            internal bool ShouldBeDormant()
            {
                if (Brain == null || transform == null || Config == null) return false;
                
                // Check global ForceRespectAiDormant setting
                if (_config.ForceRespectAiDormant && AiManager.ai_dormant)
                {
                    // Use the maximum of Config.SleepDistance, DefaultSleepDistance, and server's wakeup range
                    float serverWakeupRange = AiManager.ai_to_player_distance_wakeup_range;
                    float configSleepDistance = Config.CanSleep ? Config.SleepDistance : 0f;
                    float defaultSleepDistance = _config.DefaultSleepDistance;
                    float wakeupRange = Mathf.Max(serverWakeupRange, Mathf.Max(configSleepDistance, defaultSleepDistance));
                    
                    // Use BotReSpawn approach: GetPlayersInSphere (not Fast) with simpler filter
                    // GetPlayersInSphere does proper distance filtering, GetPlayersInSphereFast doesn't
                    BasePlayer[] localPlayerResults = new BasePlayer[64];
                    int playerCount = Query.Server.GetPlayersInSphere(transform.position, wakeupRange, localPlayerResults, x => x != null && x.net?.connection != null && x.userID.IsSteamId() && !x.IsNpc && !x.IsSleeping());
                    bool hasNearbyPlayers = playerCount > 0;
                    
                    // CRITICAL: If players are nearby and brain is sleeping, FORCE WAKE UP immediately
                    // BotReSpawn approach: Set IsDormant = false directly (simpler and more reliable)
                    if (hasNearbyPlayers)
                    {
                        // CRITICAL: Set IsDormant = false FIRST (this is the key property)
                        IsDormant = false;
                        
                        // Then aggressively wake up the brain - do ALL of these to be sure
                        if (Brain != null)
                        {
                            Brain.sleeping = false; // Set directly first
                            if (Brain is IAISleepable sleepable)
                            {
                                sleepable.WakeAI(); // Use proper wake method
                            }
                            if (Brain.Navigator != null)
                            {
                                Brain.Navigator.Resume(); // Resume navigation
                            }
                        }
                        // CRITICAL: Ensure NavAgent is enabled (required for movement)
                        if (NavAgent != null)
                        {
                            NavAgent.enabled = true; // Always enable when players nearby
                        }
                        
                        // Return false (not dormant) when players are nearby
                        return false;
                    }
                    else
                    {
                        // No players nearby - set dormant (like BotReSpawn does)
                        // Only set dormant if no current target (NPCs with targets should stay awake)
                        IsDormant = AiManager.ai_dormant && CurrentTarget == null;
                        return IsDormant;
                    }
                }
                
                // If ForceRespectAiDormant is disabled, check Brain.sleeping state (set by Think() method)
                // But also check if players are nearby - if so, wake up even if sleeping
                if (Brain.sleeping)
                {
                    // Double-check: if players are nearby, force wake up
                    float checkRange = 100f; // Reasonable default range
                    BasePlayer[] checkResults = new BasePlayer[16];
                    int checkCount = Query.Server.GetPlayersInSphere(transform.position, checkRange, checkResults, x => x != null && x.net?.connection != null && x.userID.IsSteamId() && !x.IsNpc && !x.IsSleeping());
                    if (checkCount > 0)
                    {
                        // Players nearby but brain is sleeping - force wake up
                        IsDormant = false;
                        Brain.sleeping = false;
                        if (Brain is IAISleepable sleepable)
                        {
                            sleepable.WakeAI();
                        }
                        if (Brain.Navigator != null)
                        {
                            Brain.Navigator.Resume();
                        }
                        if (NavAgent != null && !NavAgent.enabled)
                        {
                            NavAgent.enabled = true;
                        }
                        return false; // Not dormant
                    }
                    return true; // No players nearby, can be dormant
                }
                
                // Also check the IsDormant property as fallback
                if (IsDormant) return true;
                
                return false;
            }
            
            private void UpdateTick()
            {
                if (IsFrozen) return;
                
                // CRITICAL: Check for nearby players FIRST, even if dormant
                // This ensures NPCs wake up when players enter range, even if Think() isn't called frequently
                if (_config.ForceRespectAiDormant && AiManager.ai_dormant && Brain != null)
                {
                    float serverWakeupRange = AiManager.ai_to_player_distance_wakeup_range;
                    float configSleepDistance = Config.CanSleep ? Config.SleepDistance : 0f;
                    float defaultSleepDistance = _config.DefaultSleepDistance;
                    float wakeupRange = Mathf.Max(serverWakeupRange, Mathf.Max(configSleepDistance, defaultSleepDistance));
                    
                    // Use BotReSpawn approach: GetPlayersInSphere (not Fast) with simpler filter
                    BasePlayer[] localPlayerResults = new BasePlayer[64];
                    int playerCount = Query.Server.GetPlayersInSphere(transform.position, wakeupRange, localPlayerResults, x => x != null && x.net?.connection != null && x.userID.IsSteamId() && !x.IsNpc && !x.IsSleeping());
                    bool hasNearbyPlayers = playerCount > 0;
                    
                    if (hasNearbyPlayers)
                    {
                        // BotReSpawn approach: Set IsDormant = false directly (simpler and more reliable)
                        IsDormant = false;
                        
                        // FORCE WAKE UP - be aggressive about it (even if already awake, ensure everything is enabled)
                        if (Brain.sleeping)
                        {
                            // Directly set sleeping to false first
                            Brain.sleeping = false;
                            // Use IAISleepable interface to properly wake (it will handle Navigator and movement tick)
                            if (Brain is IAISleepable sleepable)
                            {
                                sleepable.WakeAI();
                            }
                            // Manually ensure Navigator is resumed if it exists
                            if (Brain.Navigator != null)
                            {
                                Brain.Navigator.Resume();
                            }
                        }
                        // CRITICAL: Ensure NavAgent is enabled (required for movement)
                        // Always check and enable, even if not sleeping, to handle edge cases
                        if (NavAgent != null && !NavAgent.enabled)
                        {
                            NavAgent.enabled = true;
                        }
                    }
                    else
                    {
                        // No players nearby - set dormant (like BotReSpawn: IsDormant = ai_dormant && CurrentTarget == null)
                        // Only set dormant if no current target (NPCs with targets should stay awake)
                        IsDormant = AiManager.ai_dormant && CurrentTarget == null;
                        if (IsDormant && Brain.Navigator != null)
                        {
                            Brain.Navigator.Stop(); // Like BotReSpawn does
                        }
                    }
                }
                
                // PERFORMANCE FIX: Skip UpdateTick when NPC is dormant to reduce CPU usage
                // BUT: Only skip if we just checked and confirmed no players nearby
                // Note: ShouldBeDormant() also checks for nearby players, so this is a double-check
                if (ShouldBeDormant()) return;
                
                if (CanRunAwayWater()) RunAwayWater();
                if (CanThrownGrenade() && CurrentTarget != null) ThrownGrenade(CurrentTarget.transform.position);
                if (CanHeal()) HealCoroutine = ServerMgr.Instance.StartCoroutine(Heal());
                EquipWeapon();
                TryRaidWithoutFoundations();
                UpdateGuardPosition();
                UpdateSleep();
                UpdateNavigationMode(); // Enhanced: Dynamic navigation switching

                TrySwimColumnKickAndDebug();
                
                // Enhanced: Stuck detection and recovery (from BotReSpawn improvements)
                if (Brain != null && Brain.Navigator != null && Brain.Navigator.StuckOffNavmesh)
                {
                    // Open-water swim NPCs: do NOT snap to NavMesh / home on the seabed — that reads as "running on ocean floor".
                    if (ShouldBypassDryNavmeshPlanning())
                    {
                        MaybeLogSwimNavDebug("StuckOffNavmesh: skipped seabed recovery (swim bypass)");
                    }
                    else
                    {
                        if (RoamPoint != Vector3.zero)
                            transform.position = RoamPoint;
                        else if (HomePosition != Vector3.zero)
                            transform.position = HomePosition;
                        Brain.Navigator.SetNavMeshEnabled(true);
                        Brain.Navigator.PlaceOnNavMesh(2f);
                    }
                }
            }
            
            // Enhanced: Dynamic navigation mode switching (from ChaosNPC improvements)
            // Switches between NavMesh and CustomNav based on context (swimming, building navigation, etc.)
            private void UpdateNavigationMode()
            {
                if (Brain == null || Brain.Navigator == null || NavAgent == null || transform == null) return;
                
                try
                {
                    // Stock NPCPlayerNavigator.IsSwimming() often stays false on deep open water (feet on seabed NavMesh).
                    // ChaosNPC fixes this with CustomScientistNavigator.IsSwimming() (modelState.waterLevel) — we are not subclassing the navigator,
                    // so align with our existing deep-water bypass used in SetDestination / ShouldBypassDryNavmeshPlanning.
                    bool isSwimming = Brain.Navigator.IsSwimming() || ShouldBypassDryNavmeshPlanning();

                    // Dynamic navigation switching:
                    // - When swimming: Disable NavMesh, enable CustomNav (better for water)
                    // - When on buildings: NavMesh may fail, BaseNav will handle it
                    // - Normal: Use NavMesh for best performance
                    if (isSwimming)
                    {
                        Brain.Navigator.CanUseNavMesh = false;
                        Brain.Navigator.CanUseCustomNav = true;
                    }
                    else
                    {
                        // Unstick swim anim / custom-nav from older false-positive IsSwimming frames.
                        NpcSpawnOpenWaterSwim.ClearStickySwimModelState(this);
                        Brain.Navigator.CanUseNavMesh = true;
                        Brain.Navigator.CanUseCustomNav = false;
                        
                        // If NavMesh path fails and we're near buildings, ensure BaseNav is prioritized
                        // This helps NPCs navigate inside player bases when NavMesh is limited
                        if (Brain.Navigator.Moving && NavAgent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid)
                        {
                            // BaseNav will handle building navigation automatically
                            Brain.Navigator.CanUseBaseNav = true;
                        }
                    }
                }
                catch (NullReferenceException)
                {
                    // Navigator component may have been destroyed
                    return;
                }
            }

            #region Targeting
            public new BaseEntity GetBestTarget()
            {
                // Null checks for performance and safety
                if (Brain == null || Brain.Senses == null || Brain.Senses.Memory == null)
                    return null;
                
                BaseEntity target = null;
                float single = float.MinValue;
                
                // Cache Memory.All to avoid repeated property access
                var memoryAll = Brain.Senses.Memory.All;
                if (memoryAll == null || memoryAll.Count == 0)
                    return null;
                
                foreach (SimpleAIMemory.SeenInfo info in memoryAll)
                {
                    BaseEntity entity = info.Entity;
                    if (entity == null) continue; // Skip null entities
                    if (entity != CurrentTarget && !CanSeeTarget(entity)) continue;
                    if (!CanTargetEntity(entity)) continue;
                    float single2 = GetSingle2(entity);
                    if (single2 <= single) continue;
                    target = entity;
                    single = single2;
                }
                return target;
            }

            private float GetSingle2(BaseEntity entity)
            {
                // Null checks for safety (entity already checked in GetBestTarget, but defensive programming)
                if (entity == null || entity.transform == null || Brain == null || eyes == null)
                    return float.MinValue;
                
                float single2 = 1f - Mathf.InverseLerp(1f, Brain.SenseRange, Vector3.Distance(entity.transform.position, transform.position));
                single2 += Mathf.InverseLerp(Brain.VisionCone, 1f, Vector3.Dot((entity.transform.position - eyes.position).normalized, eyes.BodyForward())) / 2f;
                
                // Null check before accessing Memory
                if (Brain.Senses != null && Brain.Senses.Memory != null)
                {
                    single2 += Brain.Senses.Memory.IsLOS(entity) ? 2f : 0f;
                }
                
                return single2;
            }

            internal bool CanTargetEntity(BaseEntity target)
            {
                if (target == null || target.Health() <= 0f) return false;
                if (target is BasePlayer)
                {
                    BasePlayer basePlayer = target as BasePlayer;
                    if (basePlayer.IsDead()) return false;
                    object hook = OxideCompat.CallHook("OnCustomNpcTarget", this, basePlayer);
                    if (hook is bool) return (bool)hook;
                    if (basePlayer.userID.IsSteamId()) return CanTargetPlayer(basePlayer);
                    if (basePlayer.skinID != 0 && _ins.SkinIDs.Contains(basePlayer.skinID)) return true;
                    if (basePlayer is NPCPlayer) return CanTargetNpcPlayer(basePlayer as NPCPlayer);
                    return false;
                }
                if (target is BaseAnimalNPC) return CanTargetAnimal(target as BaseAnimalNPC);
                if (target is Drone) return CanTargetDrone(target as Drone);
                return false;
            }

            internal bool CanTargetNpcPlayer(NPCPlayer target)
            {
                if (target is FrankensteinPet) return true;
                if (target.skinID == 11162132011012) return false;
                
                // Teammate NPCs can always target other NPCs (except custom ones from same plugin)
                if (Config.IsTeammateNpc) return true;
                
                return _config.CanTargetNpc;
            }

            internal bool CanTargetPlayer(BasePlayer target)
            {
                if (target._limitedNetworking) return false;
                if (target.isInvisible) return false;
                
                // Teammate NPCs should not target their owner or teammates
                if (Config.IsTeammateNpc && Config.OwnerUserID != 0UL)
                {
                    if (IsTeam(Config.OwnerUserID, target.userID)) return false;
                }

                if (Config.DisplaySashTargetsOnly && target.IsNoob()) return false;
                if ((!_config.CanTargetSleepingPlayer || Config.IgnoreSleepingPlayers) && target.IsSleeping()) return false;
                if ((!_config.CanTargetWoundedPlayer || Config.IgnoreWoundedPlayers) && target.IsWounded()) return false;
                if ((!_config.CanTargetSafeZonePlayer || Config.IgnoreSafeZonePlayers) && target.InSafeZone()) return false;
                return true;
            }

            internal bool CanTargetAnimal(BaseAnimalNPC animal)
            {
                if (animal.IsDead()) return false;
                if (animal.skinID == 11491311214163) return false;
                if (Vector3.Distance(transform.position, animal.transform.position) > 30f) return false;
                
                // Teammate NPCs can always target animals
                if (Config.IsTeammateNpc) return true;
                
                return _config.CanTargetAnimal;
            }

            private bool CanTargetDrone(Drone target) => !(CurrentWeapon is BaseMelee);

            public BaseEntity CurrentTarget { get; set; }

            public float DistanceToTarget => Vector3.Distance(transform.position, CurrentTarget.transform.position);

            public bool IsBasePlayerTarget => CurrentTarget is BasePlayer;

            internal BasePlayer GetBasePlayerTarget => CurrentTarget as BasePlayer;

            internal void SetKnown(BaseEntity entity)
            {
                for (int i = 0; i < Brain.Senses.Memory.All.Count; i++)
                {
                    SimpleAIMemory.SeenInfo info = Brain.Senses.Memory.All[i];
                    if (info.Entity != entity) continue;
                    info.Position = entity.transform.position;
                    info.Timestamp = Time.realtimeSinceStartup;
                    return;
                }
                Brain.Senses.Memory.All.Add(new SimpleAIMemory.SeenInfo { Entity = entity, Position = entity.transform.position, Timestamp = Time.realtimeSinceStartup });
            }
            #endregion Targeting

			#region Visible
			private int VisibleLayerMaskOriginal { get; } = 1218519041;

			internal new bool CanSeeTarget(BaseEntity target)
            {
                if (target == null || eyes == null) return false;
                // Ignore built-in admin invisible (BasePlayer.isInvisible)
                BasePlayer bpCheck = target as BasePlayer;
                if (bpCheck != null && bpCheck.isInvisible) return false;
				int mask = VisibleLayerMaskOriginal;
                Vector3 main = isMounted ? eyes.worldMountedPosition : IsDucked() ? eyes.worldCrouchedPosition : IsCrawling() ? eyes.worldCrawlingPosition : eyes.worldStandingPosition;
                if (target is BasePlayer)
                {
                    BasePlayer targetBp = target as BasePlayer;
					if (!targetBp.IsVisibleSpecificLayers(main, targetBp.CenterPoint(), mask) && !targetBp.IsVisibleSpecificLayers(main, targetBp.transform.position, mask) && !targetBp.IsVisibleSpecificLayers(main, targetBp.eyes.position, mask)) return false;
					if (!IsVisibleSpecificLayers(targetBp.CenterPoint(), main, mask) && !IsVisibleSpecificLayers(targetBp.transform.position, main, mask) && !IsVisibleSpecificLayers(targetBp.eyes.position, main, mask)) return false;
                }
                else
                {
					if (!target.IsVisibleSpecificLayers(main, target.CenterPoint(), mask) && !target.IsVisibleSpecificLayers(main, target.transform.position, mask)) return false;
					if (!IsVisibleSpecificLayers(target.CenterPoint(), main, mask) && !IsVisibleSpecificLayers(target.transform.position, main, mask)) return false;
                }
                return true;
            }
            #endregion Visible

            #region Equip Weapons
            public AttackEntity CurrentWeapon { get; set; }
            private bool IsEquipping { get; set; } = false;

            private bool CanEquipWeapon()
            {
                if (inventory == null || inventory.containerBelt == null) return false;
                if (IsEquipping) return false;
                if (IsFireRocketLauncher) return false;
                if (IsHealing) return false;
                return true;
            }

            public override void EquipWeapon(bool skipDeployDelay = false)
            {
                if (!CanEquipWeapon()) return;
                Item weapon = null;
                if (CurrentTarget == null)
                {
                    if (CurrentWeapon == null)
                    {
                        Dictionary<int, List<Item>> weapons = new Dictionary<int, List<Item>> { [0] = new List<Item>(), [1] = new List<Item>(), [2] = new List<Item>(), [3] = new List<Item>(), [4] = new List<Item>() };
                        foreach (Item item in inventory.containerBelt.itemList)
                        {
                            int type = GetTypeWeaponItem(item);
                            if (type == -1) continue;
                            weapons[type].Add(item);
                        }
						// Prefer ranged for stationary/idle so NPCs can shoot without moving
						if (weapons[4].Count > 0) weapon = weapons[4].GetRandom();
						else if (weapons[3].Count > 0) weapon = weapons[3].GetRandom();
                        else if (weapons[2].Count > 0) weapon = weapons[2].GetRandom();
                        else if (weapons[1].Count > 0) weapon = weapons[1].GetRandom();
                        else if (weapons[4].Count > 0) weapon = weapons[4].GetRandom();
                        else if (weapons[0].Count > 0) weapon = weapons[0].GetRandom();
                    }
                    else return;
                }
                else
                {
                    float distanceToTarget = DistanceToTarget;
                    int type = -1;
                    foreach (Item item in inventory.containerBelt.itemList)
                    {
                        int currentType = GetTypeWeaponItem(item);
                        if (currentType == -1) continue;
                        if (type == -1)
                        {
                            weapon = item;
                            type = currentType;
                        }
                        else
                        {
                            if (type == currentType) continue;
                            float oldDistance = type > 0 ? Config.AttackRangeMultiplier * type * 10f : 2f;
                            float newDistance = currentType > 0 ? Config.AttackRangeMultiplier * currentType * 10f : 2f;
                            if ((oldDistance > distanceToTarget && newDistance > distanceToTarget && newDistance < oldDistance) ||
                                (oldDistance < distanceToTarget && newDistance > distanceToTarget) ||
                                (oldDistance < distanceToTarget && newDistance < distanceToTarget && newDistance > oldDistance))
                            {
                                weapon = item;
                                type = currentType;
                            }
                        }
                    }
                }
                EquipCurrentWeapon(weapon);
            }

            internal void EquipCurrentWeapon(Item weapon)
            {
                if (weapon == null) return;
                AttackEntity attackEntity = weapon.GetHeldEntity() as AttackEntity;
                if (attackEntity == null) return;
                if (CurrentWeapon == attackEntity) return;
                IsEquipping = true;
                UpdateActiveItem(weapon.uid);
                CurrentWeapon = attackEntity;
                attackEntity.TopUpAmmo();
                if (attackEntity is Chainsaw) (attackEntity as Chainsaw).ServerNPCStart();
                if (attackEntity is BaseProjectile)
                {
                    if (_config?.WeaponsParameters != null && weapon.info != null && _config.WeaponsParameters.ContainsKey(weapon.info.shortname))
                    {
                        attackEntity.effectiveRange = _config.WeaponsParameters[weapon.info.shortname].EffectiveRange;
                        attackEntity.attackLengthMin = _config.WeaponsParameters[weapon.info.shortname].AttackLengthMin;
                        attackEntity.attackLengthMax = _config.WeaponsParameters[weapon.info.shortname].AttackLengthMax;
                    }
                    attackEntity.aiOnlyInRange = true;
                    BaseProjectile baseProjectile = attackEntity as BaseProjectile;
						if (baseProjectile.MuzzlePoint == null) baseProjectile.MuzzlePoint = baseProjectile.transform;
						// Force magazine ammo type and top up to reduce misfires when stationary
						if (baseProjectile.primaryMagazine != null && baseProjectile.primaryMagazine.contents <= 0)
						{
							baseProjectile.TopUpAmmo();
						}
                    NpcBelt npcBelt = null;
                    if (Config?.BeltItems != null)
                    {
                        foreach (NpcBelt b in Config.BeltItems)
                        {
                            if (b != null && b.ShortName == weapon.info.shortname) { npcBelt = b; break; }
                        }
                    }
                    if (npcBelt != null)
                    {
                        string ammo = npcBelt.Ammo;
                        if (!string.IsNullOrEmpty(ammo))
                        {
                            baseProjectile.primaryMagazine.ammoType = ItemManager.FindItemDefinition(ammo);
                            baseProjectile.SendNetworkUpdateImmediate();
                            if (npcBelt.ShortName == "multiplegrenadelauncher") AmmoTypeGrenadeLauncher = ammo;
                        }
                    }
                }
                // Tune stopping distance by weapon type for smoother approaches
                try
                {
                    if (Brain != null && Brain.Navigator != null)
                    {
                        if (CurrentWeapon is BaseMelee)
                        {
                            Brain.Navigator.StoppingDistance = 0.5f;
                        }
                        else
                        {
                            Brain.Navigator.StoppingDistance = 2f;
                        }
                    }
                }
                catch { }
                Invoke(FinishEquiping, 1.5f);
            }

            private void FinishEquiping() => IsEquipping = false;

            private int GetTypeWeaponItem(Item item)
            {
                if (item?.info == null) return -1;
                var weapons = _config?.Weapons;
                if (weapons == null) return -1;
                string shortname = item.info.shortname;
                if (string.IsNullOrEmpty(shortname)) return -1;
                for (int i = 0; i <= 4; i++)
                {
                    if (weapons.TryGetValue(i, out HashSet<string> set) && set != null && set.Contains(shortname))
                        return i;
                }
                return -1;
            }
            #endregion Equip Weapons

            #region Cover
            public float NextCoverCheckTime { get; set; } = 0f;

            internal bool TryTakeCover(float maxRange = 12f, float minRange = 0f)
            {
                if (Brain == null || CurrentTarget == null) return false;
                AIInformationZone zone = AIInformationZone.GetForPoint(transform.position, true);
                if (zone == null) return false;
                AICoverPoint cover = zone.GetBestCoverPoint(transform.position, CurrentTarget.transform.position, minRange, maxRange, this, allowObjectToReuse: true);
                if (cover == null) return false;
                SetDestination(cover.transform.position, 1.5f, BaseNavigator.NavigationSpeed.Fast);
                return true;
            }
            #endregion Cover

            public override void AttackerInfo(PlayerLifeStory.DeathInfo info)
            {
                base.AttackerInfo(info);
                if (CurrentWeapon != null) info.inflictorName = CurrentWeapon.ShortPrefabName;
                info.attackerName = displayName;
            }

            public override float GetAimConeScale() => Config.AimConeScale;

            public override string displayName => Config.Name;

            internal bool IsAttackingBaseProjectile { get; set; } = false;

            public override bool IsNpc
            {
                get
                {
                    if (_ins?._config == null || Brain == null || Brain.CurrentState == null) return true;
                    if (IsAttackingBaseProjectile && (_config.CanTargetNpc || _config.CanTargetAnimal) && Brain.CurrentState.StateType is AIState.Combat or AIState.CombatStationary) return false;
                    if (HasBarricadeTriggerBox()) return false;
                    return true;
                }
            }

            #region NPCBarricadeTriggerBox
            private bool HasBarricadeTriggerBox()
            {
                List<BoxCollider> list = Pool.Get<List<BoxCollider>>();
                Vis.Colliders<BoxCollider>(transform.position, 2f, list, 1 << 18);
                bool hasCollider = false;
                foreach (BoxCollider col in list)
                {
                    if (col.gameObject.GetComponent<NPCBarricadeTriggerBox>() != null) { hasCollider = true; break; }
                }
                Pool.FreeUnmanaged(ref list);
                return hasCollider;
            }
            #endregion NPCBarricadeTriggerBox

            #region Heal
            private Coroutine HealCoroutine { get; set; } = null;
            private bool IsHealing { get; set; } = false;

            private bool CanHeal()
            {
                if (IsHealing || health >= Config.Health || CurrentTarget != null || IsFireC4 || IsFireRocketLauncher || IsEquipping || inventory == null || inventory.containerBelt == null) return false;
                foreach (Item beltItem in inventory.containerBelt.itemList)
                    if (beltItem.info.shortname == "syringe.medical") return true;
                return false;
            }

            private IEnumerator Heal()
            {
                IsHealing = true;
                Item syringe = null;
                foreach (Item beltItem in inventory.containerBelt.itemList)
                {
                    if (beltItem.info.shortname == "syringe.medical") { syringe = beltItem; break; }
                }
                if (syringe == null)
                {
                    IsHealing = false;
                    yield break;
                }
                CurrentWeapon = null;
                UpdateActiveItem(syringe.uid);
                MedicalTool medicalTool = syringe.GetHeldEntity() as MedicalTool;
                yield return CoroutineEx.waitForSeconds(1.5f);
                if (medicalTool != null) medicalTool.ServerUse();
                InitializeHealth(health + 15f > Config.Health ? Config.Health : health + 15f, Config.Health);
                yield return CoroutineEx.waitForSeconds(2f);
                IsHealing = false;
                EquipWeapon();
            }
            #endregion Heal

            #region Grenades
            private HashSet<string> Barricades { get; } = new HashSet<string>
            {
                "barricade.cover.wood",
                "barricade.sandbags",
                "barricade.concrete",
                "barricade.stone"
            };
            private bool IsReloadGrenade { get; set; } = false;
            private bool IsReloadSmoke { get; set; } = false;

            private void FinishReloadGrenade() => IsReloadGrenade = false;

            private void FinishReloadSmoke() => IsReloadSmoke = false;

            private bool CanThrownGrenade()
            {
                if (IsReloadGrenade || CurrentTarget == null || !IsBasePlayerTarget || inventory == null || inventory.containerBelt == null) return false;
                if (DistanceToTarget >= 15f || !(!CanSeeTarget(CurrentTarget) || IsBehindBarricade())) return false;
                foreach (Item beltItem in inventory.containerBelt.itemList)
                {
                    string sn = beltItem.info.shortname;
                    if (sn is "grenade.f1" or "grenade.beancan" or "grenade.molotov" or "grenade.flashbang" or "grenade.bee")
                        return true;
                }
                return false;
            }

            internal bool IsBehindBarricade() => CanSeeTarget(CurrentTarget) && IsBarricade();

            private bool IsBarricade()
            {
                SetAimDirection((CurrentTarget.transform.position - transform.position).normalized);
                RaycastHit[] hits = Physics.RaycastAll(eyes.HeadRay());
                GamePhysics.Sort(hits);
                foreach (RaycastHit rh in hits)
                {
                    Barricade b = rh.GetEntity() as Barricade;
                    if (b != null && Barricades.Contains(b.ShortPrefabName) && Vector3.Distance(transform.position, b.transform.position) < DistanceToTarget)
                        return true;
                }
                return false;
            }

            private void ThrownGrenade(Vector3 target)
            {
                Item item = null;
                foreach (Item beltItem in inventory.containerBelt.itemList)
                {
                    string sn = beltItem.info.shortname;
                    if (sn is "grenade.f1" or "grenade.beancan" or "grenade.molotov" or "grenade.flashbang" or "grenade.bee") { item = beltItem; break; }
                }
                if (item == null) return;
                GrenadeWeapon weapon = item.GetHeldEntity() as GrenadeWeapon;
                if (weapon == null) return;
                Brain.Navigator.Stop();
                SetAimDirection((target - transform.position).normalized);
                weapon.ServerThrow(target);
                IsReloadGrenade = true;
                Invoke(FinishReloadGrenade, 10f);
            }

            internal void ThrownSmoke()
            {
                if (IsReloadSmoke) return;
                Item item = null;
                foreach (Item beltItem in inventory.containerBelt.itemList)
                {
                    if (beltItem.info.shortname == "grenade.smoke") { item = beltItem; break; }
                }
                if (item == null) return;
                GrenadeWeapon weapon = item.GetHeldEntity() as GrenadeWeapon;
                if (weapon == null) return;
                weapon.ServerThrow(transform.position);
                IsReloadSmoke = true;
                Invoke(FinishReloadSmoke, 30f);
            }
            #endregion Grenades

            #region Run Away Water
            internal bool IsRunAwayWater { get; set; } = false;

            private bool CanRunAwayWater()
            {
                if (!Config.CanRunAwayWater || IsRunAwayWater) return false;
                if (CurrentTarget == null)
                {
                    if (transform.position.y < -0.25f) return true;
                    else return false;
                }
                if (transform.position.y > -0.25f || TerrainMeta.HeightMap.GetHeight(CurrentTarget.transform.position) > -0.25f) return false;
                if (CurrentWeapon is BaseProjectile && DistanceToTarget < EngagementRange()) return false;
                if (CurrentWeapon is BaseMelee && DistanceToTarget < CurrentWeapon.effectiveRange) return false;
                return true;
            }

            private void RunAwayWater()
            {
                IsRunAwayWater = true;
                CurrentTarget = null;
                Invoke(FinishRunAwayWater, 20f);
            }

            private void FinishRunAwayWater() => IsRunAwayWater = false;
            #endregion Run Away Water

            #region Raid
            internal bool IsRaidState { get; set; } = false;
            internal bool IsRaidStateMelee { get; set; } = false;

            internal bool IsReloadC4 { get; set; } = false;
            internal bool IsReloadRocketLauncher { get; set; } = false;

            internal bool IsFireRocketLauncher { get; set; } = false;
            internal bool IsFireC4 { get; set; } = false;

            private Coroutine FireC4Coroutine { get; set; } = null;
            private Coroutine FireRocketLauncherCoroutine { get; set; } = null;

            internal BaseCombatEntity Turret { get; set; } = null;
            internal BaseCombatEntity PlayerTarget { get; set; } = null;
            internal HashSet<BuildingBlock> Foundations { get; set; } = new HashSet<BuildingBlock>();
            internal BaseCombatEntity CurrentRaidTarget { get; set; } = null;

            internal float DistanceToCurrentRaidTarget => Vector3.Distance(transform.position, CurrentRaidTarget.transform.position);

            internal void AddTurret(BaseCombatEntity turret)
            {
                if (!Turret.IsExists() || Vector3.Distance(transform.position, turret.transform.position) < Vector3.Distance(transform.position, Turret.transform.position))
                {
                    Turret = turret;
                    BuildingBlock block = GetNearEntity<BuildingBlock>(Turret.transform.position, 0.1f, 1 << 21);
                    CurrentRaidTarget = block.IsExists() ? block : Turret;
                }
            }

            private static T GetNearEntity<T>(Vector3 position, float radius, int layerMask) where T : BaseCombatEntity
            {
                List<T> list = Pool.Get<List<T>>();
                Vis.Entities<T>(position, radius, list, layerMask);
                T result = list.Count == 0 ? null : list.Min(s => Vector3.Distance(position, s.transform.position));
                Pool.FreeUnmanaged(ref list);
                return result;
            }

            internal BaseCombatEntity GetRaidTarget()
            {
                UpdateTargets();

                BaseCombatEntity main = null;

                if (IsRaidState)
                {
                    if (Turret != null)
                    {
                        BuildingBlock block = GetNearEntity<BuildingBlock>(Turret.transform.position, 0.1f, 1 << 21);
                        main = block.IsExists() ? block : Turret;
                    }
                    else if (Foundations.Count > 0) main = Foundations.Min(x => Vector3.Distance(transform.position, x.transform.position));
                    else if (PlayerTarget != null) main = PlayerTarget;
                }
                else if (IsRaidStateMelee)
                {
                    if (Foundations.Count > 0) main = Foundations.Min(x => Vector3.Distance(transform.position, x.transform.position));
                }

                if (main == null) return null;

                if (IsMounted()) return main;

                NavMeshHit navMeshHit;
                if (IsRaidState)
                {
                    float heightGround = TerrainMeta.HeightMap.GetHeight(main.transform.position);

                    if (main.transform.position.y - heightGround > 15f)
                    {
                        main = GetNearEntity<BuildingBlock>(new Vector3(main.transform.position.x, heightGround, main.transform.position.z), 15f, 1 << 21);
                        if (main == null) return null;
                    }

                    if (NavMesh.SamplePosition(main.transform.position, out navMeshHit, 30f, NavAgent.areaMask))
                    {
                        NavMeshPath path = new NavMeshPath();
                        if (NavMesh.CalculatePath(transform.position, navMeshHit.position, NavAgent.areaMask, path))
                        {
                            if (path.status == NavMeshPathStatus.PathComplete) return main;
                            else return GetNearEntity<BaseCombatEntity>(path.corners.Last(), 5f, 1 << 8 | 1 << 21);
                        }
                    }

                    Vector2 pos1 = new Vector2(transform.position.x, transform.position.z);
                    Vector2 pos2 = new Vector2(main.transform.position.x, main.transform.position.z);
                    Vector2 pos3 = pos1 + (pos2 - pos1).normalized * (Vector2.Distance(pos1, pos2) - 30f);
                    Vector3 pos = new Vector3(pos3.x, 0f, pos3.y);
                    pos.y = TerrainMeta.HeightMap.GetHeight(pos);

                    main = GetNearEntity<BuildingBlock>(pos, 15f, 1 << 21);
                    if (main == null) return null;

                    if (NavMesh.SamplePosition(main.transform.position, out navMeshHit, 30f, NavAgent.areaMask))
                    {
                        NavMeshPath path = new NavMeshPath();
                        if (NavMesh.CalculatePath(transform.position, navMeshHit.position, NavAgent.areaMask, path))
                        {
                            if (path.status == NavMeshPathStatus.PathComplete) return main;
                            else return GetNearEntity<BaseCombatEntity>(path.corners.Last(), 5f, 1 << 8 | 1 << 21);
                        }
                    }
                }
                else if (IsRaidStateMelee)
                {
                    if (NavMesh.SamplePosition(main.transform.position, out navMeshHit, 30f, NavAgent.areaMask))
                    {
                        NavMeshPath path = new NavMeshPath();
                        if (NavMesh.CalculatePath(transform.position, navMeshHit.position, NavAgent.areaMask, path))
                        {
                            if (path.status == NavMeshPathStatus.PathComplete && Vector3.Distance(navMeshHit.position, main.transform.position) < 6f) return main;
                            else return GetNearEntity<BaseCombatEntity>(path.corners.Last(), 6f, 1 << 8 | 1 << 21);
                        }
                    }
                }

                return main;
            }

            private void UpdateTargets()
            {
                if (!Turret.IsExists()) Turret = null;
                if (!PlayerTarget.IsExists()) PlayerTarget = null;
                List<BuildingBlock> deadFoundations = Pool.Get<List<BuildingBlock>>();
                foreach (BuildingBlock b in Foundations)
                    if (!b.IsExists()) deadFoundations.Add(b);
                foreach (BuildingBlock b in deadFoundations) Foundations.Remove(b);
                Pool.FreeUnmanaged(ref deadFoundations);
                if (!CurrentRaidTarget.IsExists()) CurrentRaidTarget = null;
            }

            internal bool StartExplosion(BaseCombatEntity target)
            {
                if (target == null) return false;
                if (CanThrownC4(target))
                {
                    FireC4Coroutine = ServerMgr.Instance.StartCoroutine(ThrownC4(target));
                    return true;
                }
                if (CanRaidRocketLauncher(target))
                {
                    ThrownSmoke();
                    FireRocketLauncherCoroutine = ServerMgr.Instance.StartCoroutine(ProcessFireRocketLauncher(target));
                    return true;
                }
                return false;
            }

            internal bool HasRocketLauncher()
            {
                foreach (Item beltItem in inventory.containerBelt.itemList)
                    if (beltItem.info.shortname == "rocket.launcher") return true;
                return false;
            }

            private bool CanRaidRocketLauncher(BaseCombatEntity target) => !IsReloadRocketLauncher && !IsFireRocketLauncher && !IsEquipping && !IsHealing && HasRocketLauncher() && Vector3.Distance(transform.position, target.transform.position) < 30f;

            private IEnumerator ProcessFireRocketLauncher(BaseCombatEntity target)
            {
                IsFireRocketLauncher = true;
                EquipRocketLauncher();
                if (!IsMounted()) SetDucked(true);
                Brain.Navigator.Stop();
                Brain.Navigator.SetFacingDirectionEntity(target);
                yield return CoroutineEx.waitForSeconds(1.5f);
                if (target.IsExists())
                {
                    if (target.ShortPrefabName.Contains("foundation"))
                    {
                        Brain.Navigator.ClearFacingDirectionOverride();
                        SetAimDirection((target.transform.position - new Vector3(0f, 1.5f, 0f) - transform.position).normalized);
                    }
                    FireRocketLauncher();
                    IsReloadRocketLauncher = true;
                    Invoke(FinishReloadRocketLauncher, 6f);
                }
                IsFireRocketLauncher = false;
                EquipWeapon();
                Brain.Navigator.ClearFacingDirectionOverride();
                if (!IsMounted()) SetDucked(false);
            }

            private void EquipRocketLauncher()
            {
                Item item = null;
                foreach (Item beltItem in inventory.containerBelt.itemList)
                {
                    if (beltItem.info.shortname == "rocket.launcher") { item = beltItem; break; }
                }
                if (item == null) return;
                CurrentWeapon = null;
                UpdateActiveItem(item.uid);
            }

            private void FireRocketLauncher()
            {
                RaycastHit raycastHit;
                SignalBroadcast(Signal.Attack, string.Empty);
                Vector3 vector3 = IsMounted() ? eyes.position + new Vector3(0f, 0.5f, 0f) : eyes.position;
                Vector3 modifiedAimConeDirection = AimConeUtil.GetModifiedAimConeDirection(2.25f, eyes.BodyForward());
                float single = 1f;
                if (Physics.Raycast(vector3, modifiedAimConeDirection, out raycastHit, single, 1236478737)) single = raycastHit.distance - 0.1f;
                TimedExplosive rocket = GameManager.server.CreateEntity("assets/prefabs/ammo/rocket/rocket_basic.prefab", vector3 + modifiedAimConeDirection * single) as TimedExplosive;
                rocket.creatorEntity = this;
                ServerProjectile serverProjectile = rocket.GetComponent<ServerProjectile>();
                serverProjectile.InitializeVelocity(GetInheritedProjectileVelocity(modifiedAimConeDirection) + modifiedAimConeDirection * serverProjectile.speed * 2f);
                rocket.Spawn();
            }

            private void FinishReloadRocketLauncher() => IsReloadRocketLauncher = false;

            internal bool HasC4()
            {
                foreach (Item beltItem in inventory.containerBelt.itemList)
                    if (beltItem.info.shortname == "explosive.timed") return true;
                return false;
            }

            private bool CanThrownC4(BaseCombatEntity target) => !IsReloadC4 && !IsFireC4 && HasC4() && Vector3.Distance(transform.position, target.transform.position) < 6f;

            private IEnumerator ThrownC4(BaseCombatEntity target)
            {
                Item item = null;
                foreach (Item beltItem in inventory.containerBelt.itemList)
                {
                    if (beltItem.info.shortname == "explosive.timed") { item = beltItem; break; }
                }
                if (item == null) yield break;
                IsFireC4 = true;
                Brain.Navigator.Stop();
                Brain.Navigator.SetFacingDirectionEntity(target);
                yield return CoroutineEx.waitForSeconds(1.5f);
                if (target.IsExists())
                {
                    ThrownWeapon weapon = item.GetHeldEntity() as ThrownWeapon;
                    if (weapon != null) weapon.ServerThrow(target.transform.position);
                    IsReloadC4 = true;
                    Invoke(FinishReloadC4, 15f);
                }
                IsFireC4 = false;
                Brain.Navigator.ClearFacingDirectionOverride();
            }

            private void FinishReloadC4() => IsReloadC4 = false;

            private static bool IsTeam(ulong playerId, ulong targetId)
            {
                if (playerId == 0 || targetId == 0) return false;
                if (playerId == targetId) return true;
                RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance.FindPlayersTeam(playerId);
                if (playerTeam != null && playerTeam.members.Contains(targetId)) return true;
                if (_ins.Friends.Exists && (bool)_ins.Friends.Call("AreFriends", playerId, targetId)) return true;
                if (_ins.Clans.Exists && _ins.Clans.Author == "k1lly0u" && (bool)_ins.Clans.Call("IsMemberOrAlly", playerId.ToString(), targetId.ToString())) return true;
                return false;
            }

            private void TryRaidWithoutFoundations()
            {
                if (!IsRaidState || Foundations.Count != 0) return;
                if (CurrentTarget == null || CurrentTarget is Drone)
                {
                    PlayerTarget = null;
                    CurrentRaidTarget = null;
                }
                else if (IsBasePlayerTarget)
                {
                    bool isNull = true;
                    BuildingBlock block = GetNearEntity<BuildingBlock>(CurrentTarget.transform.position, 0.1f, 1 << 21);
                    if (block.IsExists() && IsTeam(GetBasePlayerTarget.userID, block.OwnerID))
                    {
                        PlayerTarget = block;
                        isNull = false;
                    }
                    Tugboat tugboat = CurrentTarget.GetParentEntity() as Tugboat;
                    if (tugboat.IsExists())
                    {
                        PlayerTarget = tugboat;
                        isNull = false;
                    }
                    BaseVehicle vehicle = GetBasePlayerTarget.GetMountedVehicle();
                    if (vehicle.IsExists() && (vehicle is SubmarineDuo || vehicle is BaseSubmarine))
                    {
                        PlayerTarget = vehicle;
                        isNull = false;
                    }
                    if (isNull)
                    {
                        PlayerTarget = null;
                        CurrentRaidTarget = null;
                    }
                }
            }
            #endregion Raid

            #region Guard
            private Vector3 BeforeGuardHomePosition { get; set; } = Vector3.zero;
            internal BaseEntity GuardTarget { get; set; } = null;

            internal void AddTargetGuard(BaseEntity target)
            {
                BeforeGuardHomePosition = HomePosition;
                GuardTarget = target;
            }

            private void UpdateGuardPosition()
            {
                if (BeforeGuardHomePosition == Vector3.zero) return;
                if (GuardTarget.IsExists()) HomePosition = GuardTarget.transform.position;
                else
                {
                    HomePosition = BeforeGuardHomePosition;
                    BeforeGuardHomePosition = Vector3.zero;
                    GuardTarget = null;
                    OxideCompat.CallHook("OnCustomNpcGuardTargetEnd", this);
                }
            }
            #endregion Guard

            #region Parent
            private BaseEntity ParentEntity { get; set; } = null;
            private Vector3 LocalPos { get; set; } = Vector3.zero;

            internal void SetParentEntity(BaseEntity parent, Vector3 pos)
            {
                ParentEntity = parent;
                LocalPos = pos;
                InvokeRepeating(UpdateHomePositionParent, 0f, 0.1f);
            }

            private void UpdateHomePositionParent()
            {
                if (ParentEntity != null) HomePosition = ParentEntity.transform.TransformPoint(LocalPos);
                else
                {
                    LocalPos = Vector3.zero;
                    CancelInvoke(UpdateHomePositionParent);
                }
            }
            #endregion Parent

            #region Multiple Grenade Launcher
            internal bool IsReloadGrenadeLauncher { get; set; } = false;
            private int CountAmmoInGrenadeLauncher { get; set; } = 6;
            private string AmmoTypeGrenadeLauncher { get; set; } = "40mm_grenade_he";

            internal void FireGrenadeLauncher()
            {
                RaycastHit raycastHit;
                SignalBroadcast(Signal.Attack, string.Empty);
                Vector3 vector3 = IsMounted() ? eyes.position + new Vector3(0f, 0.5f, 0f) : eyes.position;
                Vector3 modifiedAimConeDirection = AimConeUtil.GetModifiedAimConeDirection(0.675f, eyes.BodyForward());
                float single = 1f;
                if (Physics.Raycast(vector3, modifiedAimConeDirection, out raycastHit, single, 1236478737)) single = raycastHit.distance - 0.1f;
                TimedExplosive grenade = GameManager.server.CreateEntity($"assets/prefabs/ammo/40mmgrenade/{AmmoTypeGrenadeLauncher}.prefab", vector3 + modifiedAimConeDirection * single) as TimedExplosive;
                grenade.creatorEntity = this;
                ServerProjectile serverProjectile = grenade.GetComponent<ServerProjectile>();
                serverProjectile.InitializeVelocity(GetInheritedProjectileVelocity(modifiedAimConeDirection) + modifiedAimConeDirection * serverProjectile.speed * 2f);
                grenade.Spawn();
                CountAmmoInGrenadeLauncher--;
                if (CountAmmoInGrenadeLauncher == 0)
                {
                    IsReloadGrenadeLauncher = true;
                    Invoke(FinishReloadGrenadeLauncher, 8f);
                }
            }

            private void FinishReloadGrenadeLauncher()
            {
                CountAmmoInGrenadeLauncher = 6;
                IsReloadGrenadeLauncher = false;
            }
            #endregion Multiple Grenade Launcher

            #region Flame Thrower
            internal bool IsReloadFlameThrower { get; set; } = false;

            internal void FireFlameThrower()
            {
                FlameThrower flameThrower = CurrentWeapon as FlameThrower;
                if (flameThrower == null || flameThrower.IsFlameOn()) return;
                if (flameThrower.ammo <= 0)
                {
                    IsReloadFlameThrower = true;
                    Invoke(FinishReloadFlameThrower, 4f);
                    return;
                }
                flameThrower.SetFlameState(true);
                Invoke(flameThrower.StopFlameState, 0.25f);
            }

            private void FinishReloadFlameThrower()
            {
                FlameThrower flameThrower = CurrentWeapon as FlameThrower;
                if (flameThrower == null) return;
                flameThrower.TopUpAmmo();
                IsReloadFlameThrower = false;
            }
            #endregion Flame Thrower

            #region Melee Weapon
            internal void UseMeleeWeapon(bool damage = true)
            {
                BaseMelee weapon = CurrentWeapon as BaseMelee;
                if (weapon.HasAttackCooldown()) return;
                weapon.StartAttackCooldown(weapon.repeatDelay * 2f);
                SignalBroadcast(Signal.Attack, string.Empty, null);
                if (weapon.swingEffect.isValid) Effect.server.Run(weapon.swingEffect.resourcePath, weapon.transform.position, Vector3.forward, net.connection, false);
                if (weapon is Chainsaw)
                {
                    Chainsaw chainsaw = weapon as Chainsaw;
                    chainsaw.SetAttackStatus(true, FlagsUpdateMode.SendNetworkUpdate);
                    Invoke(() => chainsaw.SetAttackStatus(false, FlagsUpdateMode.SendNetworkUpdate), chainsaw.attackSpacing + 0.5f);
                }
                if (weapon is Jackhammer)
                {
                    Jackhammer jackhammer = weapon as Jackhammer;
                    jackhammer.SetEngineStatus(true);
                    Invoke(() => jackhammer.SetEngineStatus(false), jackhammer.attackSpacing + 0.5f);
                }
                if (!damage) return;
                Vector3 vector31 = eyes.BodyForward();
                for (int i = 0; i < 2; i++)
                {
                    List<RaycastHit> list = Pool.Get<List<RaycastHit>>();
                    GamePhysics.TraceAll(new Ray(eyes.position - (vector31 * (i == 0 ? 0f : 0.2f)), vector31), i == 0 ? 0f : weapon.attackRadius, list, weapon.effectiveRange + 0.2f, 1219701521);
                    bool flag = false;
                    for (int j = 0; j < list.Count; j++)
                    {
                        RaycastHit item = list[j];
                        BaseEntity entity = item.GetEntity();
                        if (entity != null && entity != this && !entity.EqualNetID(this) && !entity.isClient)
                        {
                            float single = weapon.damageTypes.Sum(x => x.amount);
                            float meleeMul = Config.MeleeDamageScale > 0f ? Config.MeleeDamageScale : Config.DamageScale;
                            entity.OnAttacked(new HitInfo(this, entity, DamageType.Slash, single * weapon.npcDamageScale * meleeMul));
                            HitInfo hitInfo = Pool.Get<HitInfo>();
                            hitInfo.HitEntity = entity;
                            hitInfo.HitPositionWorld = item.point;
                            hitInfo.HitNormalWorld = -vector31;
                            if (entity is BaseNpc || entity is BasePlayer) hitInfo.HitMaterial = StringPool.Get("Flesh");
                            else hitInfo.HitMaterial = StringPool.Get(item.GetCollider().sharedMaterial != null ? item.GetCollider().sharedMaterial.GetName() : "generic");
                            weapon.ServerUse_OnHit(hitInfo);
                            Effect.server.ImpactEffect(hitInfo);
                            Pool.Free(ref hitInfo);
                            flag = true;
                            if (entity.ShouldBlockProjectiles()) break;
                        }
                    }
                    Pool.FreeUnmanaged(ref list);
                    if (flag) break;
                }
            }
            #endregion Melee Weapon          
            #endregion Controller 


            #region Move
            private Action _onDestinationReachedCallback = null;

            /// <summary>
            /// Swim-capable NPCs in deep open water: stock "find dry NavMesh point near destination" logic snaps to seabed / shoreline
            /// and fights GrimmNPC swim patches. Bypass dry NavMesh planning for those frames.
            /// </summary>
            private bool ShouldBypassDryNavmeshPlanning()
            {
                if (Config == null || !Config.CanSwim || Brain?.Navigator == null || transform == null)
                    return false;

                Vector3 p = transform.position;
                // Sunken custom monument (GrimmBoss CustomMap): feet below terrain shell — not open-ocean swim; use monument/NavMesh planning.
                if (IsCustomMapSunkenStructureSpawn(p))
                    return false;
                // Use the same strict open-water classification that powers our BaseNavigator swim Harmony.
                // This avoids false positives on dry terrain that happens to be below sea level.
                if (NpcSpawnOpenWaterSwim.TryEvaluate(this))
                    return true;

                return Brain.Navigator.IsSwimming();
            }

            /// <summary>CustomMap world spawn inside/under the terrain volume (e.g. harbor under sea level). Open-ocean swim helpers would mis-detect as deep water column.</summary>
            private bool IsCustomMapSunkenStructureSpawn(Vector3 p)
            {
                if (Config == null || !Config.CustomMapAbsolutePosition)
                    return false;
                if (TerrainMeta.HeightMap == null || !TerrainMeta.HeightMap.isInitialized)
                    return false;
                float terrainH = TerrainMeta.HeightMap.GetHeight(p);
                const float belowShellMeters = 4f;
                return p.y < terrainH - belowShellMeters;
            }

            private void TrySwimColumnKickAndDebug()
            {
                if (Config == null || !Config.CanSwim || Brain?.Navigator == null || transform == null)
                    return;

                Vector3 p = transform.position;
                if (IsCustomMapSunkenStructureSpawn(p))
                    return;
                if (!NpcSpawnOpenWaterSwim.TryEvaluate(this) && !Brain.Navigator.IsSwimming())
                    return;
                float water = WaterLevel.GetWaterSurface(p, waves: false, volumes: true);
                float submerged = water - p.y;
                if (submerged < 0.15f)
                    return;

                float terrainH = TerrainMeta.HeightMap != null && TerrainMeta.HeightMap.isInitialized
                    ? TerrainMeta.HeightMap.GetHeight(p)
                    : float.NegativeInfinity;

                // Shallow water on terrain — do not kick.
                if (!float.IsNegativeInfinity(terrainH) && water <= terrainH + 0.35f)
                    return;

                bool swimming = Brain.Navigator.IsSwimming();
                float wf = NpcSpawnOpenWaterSwim.SafeWaterFactor(this);
                MaybeLogSwimNavDebug($"tick submerged={submerged:F2}m swim={swimming} wf={wf:F3} bypass={ShouldBypassDryNavmeshPlanning()}");

                // One-shot: pull the NPC into the water column so swimming probes/model water can engage.
                if (!_swimColumnKickDone && submerged > 0.75f && !swimming && wf < 0.35f
                    && Time.realtimeSinceStartup >= _nextSwimColumnKickRealtime)
                {
                    _nextSwimColumnKickRealtime = Time.realtimeSinceStartup + 2f;
                    float targetY = water - 0.85f;
                    if (!float.IsNegativeInfinity(terrainH))
                        targetY = Mathf.Max(targetY, terrainH + 0.35f);

                    Vector3 moved = new Vector3(p.x, targetY, p.z);
                    transform.position = moved;
                    ServerPosition = moved;
                    _swimColumnKickDone = true;
                    MaybeLogSwimNavDebug($"SwimColumnKick: y {p.y:F2} -> {moved.y:F2} (water={water:F2} terrain={(float.IsNegativeInfinity(terrainH) ? -9999f : terrainH):F2})");
                }
            }

            private void MaybeLogSwimNavDebug(string reason)
            {
                if (_ins?._config == null || !_ins._config.EnableDebugLogging)
                    return;
                float now = Time.realtimeSinceStartup;
                if (now < _nextSwimNavDebugRealtime)
                    return;
                _nextSwimNavDebugRealtime = now + 2.5f;

                Vector3 p = transform.position;
                float wSurf = WaterLevel.GetWaterSurface(p, waves: false, volumes: true);
                float wLevel = TerrainMeta.WaterMap != null ? WaterLevel.GetWaterLevel(p, waves: true) : float.NaN;
                float terrainH = TerrainMeta.HeightMap != null && TerrainMeta.HeightMap.isInitialized
                    ? TerrainMeta.HeightMap.GetHeight(p)
                    : float.NaN;
                WaterLevel.WaterInfo wi = WaterLevel.GetWaterInfo(p, waves: true, volumes: true, this);
                string name = Config?.Name ?? displayName ?? "?";
                ulong id = net?.ID.Value ?? 0UL;
                _ins.DebugLog(
                    $"[SwimNav] '{name}' net={id} reason={reason} pos={p} " +
                    $"waterSurf={wSurf:F2} waterLevel={(float.IsNaN(wLevel) ? -9999f : wLevel):F2} terrainH={(float.IsNaN(terrainH) ? -9999f : terrainH):F2} " +
                    $"wi.valid={wi.isValid} wi.surface={wi.surfaceLevel:F2} wi.terrainY={wi.terrainHeight:F2} wi.curDepth={wi.currentDepth:F2} wi.overall={wi.overallDepth:F2} " +
                    $"wf={NpcSpawnOpenWaterSwim.SafeWaterFactor(this):F3} swim={Brain.Navigator.IsSwimming()} stuckOff={Brain.Navigator.StuckOffNavmesh} " +
                    $"dest={Brain.Navigator.Destination} canSwim={Config.CanSwim}");
            }

            internal bool SetDestination(Vector3 pos, float radius, BaseNavigator.NavigationSpeed speed, Action onReached = null)
            {
                // Null checks to prevent NullReferenceException
                if (Brain == null || Brain.Navigator == null || transform == null)
                {
                    return false; // Navigator not ready yet
                }

                if (ShouldBypassDryNavmeshPlanning())
                {
                    MaybeLogSwimNavDebug($"SetDestination bypass dry-nav snap -> raw dest={pos}");
                    if (!pos.IsEqualVector3(Brain.Navigator.Destination))
                    {
                        _onDestinationReachedCallback = onReached;
                        try
                        {
                            return Brain.Navigator.SetDestination(pos, speed);
                        }
                        catch (NullReferenceException)
                        {
                            return false;
                        }
                    }
                    return true;
                }
                
                // Enhanced: Find valid navmesh position before setting destination
                if (!EnhancedNavmeshSpawnPoint.Find(pos, radius, out pos))
                {
                    // If we can't find a valid position, try using the original with GetSamplePosition
                    pos = GetSamplePosition(pos, radius);
                }
                
                // Enhanced: Check for walls/obstacles before setting destination (prevents walking through structures)
                if (!IsPathClear(transform.position, pos))
                {
                    // Path blocked - try to find alternative position
                    Vector3 alternativePos = FindAlternativePosition(pos, radius);
                    if (alternativePos != Vector3.zero)
                    {
                        pos = alternativePos;
                    }
                    else
                    {
                        // Can't find clear path, return false
                        return false;
                    }
                }
                
                if (!pos.IsEqualVector3(Brain.Navigator.Destination))
                {
                    _onDestinationReachedCallback = onReached;
                    try
                    {
                        return Brain.Navigator.SetDestination(pos, speed);
                    }
                    catch (NullReferenceException)
                    {
                        // Navigator component may have been destroyed
                        return false;
                    }
                }
                return true; // Already at destination
            }
            
            // Enhanced: Check if path to destination is clear of walls/obstacles (from BaseNavigator collision detection)
            private bool IsPathClear(Vector3 from, Vector3 to)
            {
                if (from == Vector3.zero || to == Vector3.zero) return false;
                
                Vector3 direction = (to - from).normalized;
                float distance = Vector3.Distance(from, to);
                
                // Use same layerMask as BaseNavigator Base navigation (10551552 = buildings, structures, etc.)
                int layerMask = 10551552;
                
                // Check for obstacles in path using Raycast (similar to BaseNavigator)
                RaycastHit hitInfo;
                Vector3 rayStart = from + Vector3.up * 0.5f; // Start slightly above ground
                
                // Raycast from current position to destination
                if (Physics.Raycast(rayStart, direction, out hitInfo, distance + 0.5f, layerMask))
                {
                    // Hit something - check if it's a wall/structure (not just ground)
                    float hitDistance = Vector3.Distance(from, hitInfo.point);
                    if (hitDistance < distance * 0.9f) // If hit is close to destination, might be okay
                    {
                        // Enhanced: Check if hit is a building block, door, or gate (prevents walking through gates/doors)
                        BaseEntity hitEntity = hitInfo.collider?.GetComponentInParent<BaseEntity>();
                        if (hitEntity != null)
                        {
                            string prefabName = hitEntity.ShortPrefabName?.ToLower() ?? "";
                            if (hitEntity is BuildingBlock || hitEntity is SimpleBuildingBlock || 
                                prefabName.Contains("gate") || prefabName.Contains("door") || prefabName.Contains("prison"))
                            {
                                // Blocked by building structure, door, or gate
                                return false;
                            }
                        }
                    }
                }
                
                // Also check with SphereCast for better detection (like BaseNavigator)
                float sphereRadius = 0.25f; // NPC radius approximation
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
            private Vector3 FindAlternativePosition(Vector3 blockedPos, float radius)
            {
                // Try positions around the blocked destination
                for (int i = 0; i < 8; i++)
                {
                    float angle = (360f / 8f) * i;
                    Vector3 offset = Quaternion.Euler(0, angle, 0) * Vector3.forward * (radius * 0.5f);
                    Vector3 candidate = blockedPos + offset;
                    
                    if (IsPathClear(transform.position, candidate))
                    {
                        // Validate with EnhancedNavmeshSpawnPoint
                        if (EnhancedNavmeshSpawnPoint.Find(candidate, radius, out Vector3 validPos, areaMask: 25))
                        {
                            return validPos;
                        }
                    }
                }
                
                return Vector3.zero; // No alternative found
            }
            
            // Check if destination reached and trigger callback
            private void CheckDestinationReached()
            {
                // PERFORMANCE FIX: Skip when dormant
                if (ShouldBeDormant()) return;
                
                if (_onDestinationReachedCallback == null || Brain == null || Brain.Navigator == null || transform == null)
                    return;
                
                try
                {
                    if (!Brain.Navigator.Moving && Brain.Navigator.Destination != Vector3.zero)
                    {
                        float distance = Vector3.Distance(transform.position, Brain.Navigator.Destination);
                        if (distance < 2f)
                        {
                            Action callback = _onDestinationReachedCallback;
                            _onDestinationReachedCallback = null;
                            callback?.Invoke();
                        }
                    }
                }
                catch (NullReferenceException)
                {
                    // Navigator component may have been destroyed
                    _onDestinationReachedCallback = null;
                }
            }

            internal Vector3 GetSamplePosition(Vector3 source, float radius)
            {
                // Enhanced: Use enhanced navmesh finder with validation
                // Use areaMask 25 (building navigation) for movement destinations
                if (EnhancedNavmeshSpawnPoint.Find(source, radius, out Vector3 validPosition, areaMask: 25))
                {
                    return validPosition;
                }
                
                // Fallback to original method
                NavMeshHit navMeshHit;
                if (NavMesh.SamplePosition(source, out navMeshHit, radius, NavAgent.areaMask))
                {
                    NavMeshPath path = new NavMeshPath();
                    if (NavMesh.CalculatePath(transform.position, navMeshHit.position, NavAgent.areaMask, path))
                    {
                        if (path.status == NavMeshPathStatus.PathComplete) return navMeshHit.position;
                        else return path.corners.Last();
                    }
                }
                
                {
                    // Broader search fallback to find any nearby navmesh when standing on foundations
                    if (NavMesh.SamplePosition(source, out navMeshHit, Mathf.Max(30f, radius), NavAgent.areaMask))
                    {
                        NavMeshPath path = new NavMeshPath();
                        if (NavMesh.CalculatePath(transform.position, navMeshHit.position, NavAgent.areaMask, path))
                        {
                            if (path.status == NavMeshPathStatus.PathComplete) return navMeshHit.position;
                            else return path.corners.Last();
                        }
                    }

                    // Any-area fallback
                    if (NavMesh.SamplePosition(source, out navMeshHit, Mathf.Max(30f, radius), NavMesh.AllAreas))
                    {
                        NavMeshPath path = new NavMeshPath();
                        if (NavMesh.CalculatePath(transform.position, navMeshHit.position, NavMesh.AllAreas, path))
                        {
                            if (path.status == NavMeshPathStatus.PathComplete) return navMeshHit.position;
                            else return path.corners.Last();
                        }
                    }
                }
                return source;
            }

            internal Vector3 GetRandomPos(Vector3 source, float radius)
            {
                Vector2 vector2 = UnityEngine.Random.insideUnitCircle * radius;
                return source + new Vector3(vector2.x, 0f, vector2.y);
            }

            // Enhanced: Get near nav point with better spawn finding (from BotReSpawn improvements)
            internal Vector3 GetNearNavPoint(int radius = 30)
            {
                Vector3 targetPos = CurrentTarget != null ? CurrentTarget.transform.position : HomePosition;
                Vector3 sourcePos = CurrentTarget == null ? transform.position : targetPos;
                
                // Use EnhancedNavmeshSpawnPoint with increased attempts
                if (EnhancedNavmeshSpawnPoint.Find(sourcePos, radius, out Vector3 validPosition, areaMask: 25))
                {
                    return validPosition;
                }
                
                // Fallback to current position if no valid nav point found
                return transform.position;
            }

            internal bool IsMoving => Brain != null && Brain.Navigator != null && Brain.Navigator.Moving;
            #endregion Move

            #region States
            internal bool CanChaseState()
            {
                if (IsRunAwayWater) return false;
                if (IsFireC4 || IsFireRocketLauncher) return false;
                if (DistanceFromBase > Config.ChaseRange) return false;
                if (IsRaidState && CurrentRaidTarget != null) return false;
                if (CurrentTarget == null) return false;
                if (_ins.IsGasStationNpc(CurrentTarget)) return false;
                return true;
            }

            internal bool CanCombatState()
            {
                if (CurrentWeapon == null) return false;
                if (CurrentWeapon.ShortPrefabName == "mgl.entity" && IsReloadGrenadeLauncher) return false;
                if (CurrentWeapon is FlameThrower && IsReloadFlameThrower) return false;
                if (IsRunAwayWater) return false;
                if (IsFireC4 || IsFireRocketLauncher) return false;
                if (CurrentTarget == null) return false;
                if (DistanceFromBase > Config.ChaseRange)
                {
                    if (CurrentWeapon is BaseMelee) return false;
                    if (GuardTarget != null) return false;
                }
                if (DistanceToTarget > EngagementRange()) return false;
                if (_ins.IsGasStationNpc(CurrentTarget) && DistanceFromBase > Config.RoamRange) return false;
                if (!CanSeeTarget(CurrentTarget)) return false;
                if (IsBehindBarricade()) return false;
                return true;
            }

            internal bool CanCombatStationaryState()
            {
                if (CurrentWeapon == null) return false;
                if (CurrentWeapon.ShortPrefabName == "mgl.entity" && IsReloadGrenadeLauncher) return false;
                if (CurrentWeapon is FlameThrower && IsReloadFlameThrower) return false;
                if (IsFireC4 || IsFireRocketLauncher) return false;
                if (CurrentTarget == null) return false;
                if (DistanceToTarget > EngagementRange()) return false;
                if (!CanSeeTarget(CurrentTarget)) return false;
                if (IsBehindBarricade()) return false;
                return true;
            }

            internal bool CanRaidState()
            {
                if (IsFireC4 || IsFireRocketLauncher) return true;
                if (IsRunAwayWater) return false;
                if (CurrentRaidTarget == null) return false;
                if (CurrentTarget != null && CanSeeTarget(CurrentTarget) && DistanceToTarget < EngagementRange()) return false;
                if (HasRocketLauncher() || HasC4()) return true;
                return false;
            }

            internal bool CanRaidStateMelee()
            {
                if (IsRunAwayWater) return false;
                if (CurrentRaidTarget == null) return false;
                if (CurrentTarget != null && CanSeeTarget(CurrentTarget)) return false;
                if (CurrentWeapon is BaseMelee || IsTimedExplosiveCurrentWeapon) return true;
                return false;
            }
            #endregion States

            #region Bomber
            internal bool IsBomber => displayName == "Bomber";

            internal bool IsTimedExplosiveCurrentWeapon => CurrentWeapon != null && CurrentWeapon.ShortPrefabName == "explosive.timed.entity";

            private RFTimedExplosive BomberTimedExplosive { get; set; } = null;

            private HashSet<BaseTrap> _deployedTraps;

            private void SpawnTimedExplosive()
            {
                BomberTimedExplosive = GameManager.server.CreateEntity("assets/prefabs/tools/c4/explosive.timed.deployed.prefab") as RFTimedExplosive;
                BomberTimedExplosive.enableSaving = false;
                BomberTimedExplosive.timerAmountMin = float.PositiveInfinity;
                BomberTimedExplosive.timerAmountMax = float.PositiveInfinity;
                BomberTimedExplosive.transform.localPosition = new Vector3(0f, 1f, 0f);
                BomberTimedExplosive.SetParent(this);
                BomberTimedExplosive.Spawn();
            }

            internal void ExplosionBomber(BaseEntity target = null)
            {
                Effect.server.Run("assets/prefabs/tools/c4/effects/c4_explosion.prefab", transform.position + new Vector3(0f, 1f, 0f), Vector3.up, null, true);
                OxideCompat.CallHook("OnBomberExplosion", this, target);
                Kill();
            }
            #endregion Bomber

            #region Sleep
            private void UpdateSleep()
            {
                if (transform == null || Brain == null) return; // Entity not ready
                
                bool shouldSleep = false;
                
                // Check global ForceRespectAiDormant setting
                if (_config.ForceRespectAiDormant)
                {
                    // Respect server's ai_dormant command
                    if (AiManager.ai_dormant)
                    {
                    // Use the maximum of Config.SleepDistance, DefaultSleepDistance, and server's wakeup range
                    float serverWakeupRange = AiManager.ai_to_player_distance_wakeup_range;
                    float configSleepDistance = Config.CanSleep ? Config.SleepDistance : 0f;
                    float defaultSleepDistance = _config.DefaultSleepDistance;
                    float wakeupRange = Mathf.Max(serverWakeupRange, Mathf.Max(configSleepDistance, defaultSleepDistance));
                    
                    // Use local array to avoid static array conflicts
                    BasePlayer[] localPlayerResults = new BasePlayer[64];
                    
                    // Try GetPlayersInSphereFast first (more reliable)
                    int playerCount = Query.Server.GetPlayersInSphereFast(transform.position, wakeupRange, localPlayerResults, x => x != null && x.IsPlayer() && !x.IsSleeping());
                    
                    // Fallback to PlayerGrid.Query if needed
                    bool hasNearbyPlayers = playerCount > 0;
                    if (!hasNearbyPlayers && wakeupRange > 0)
                    {
                        int gridCount = Query.Server.PlayerGrid.Query(transform.position.x, transform.position.z, wakeupRange, localPlayerResults, x => x != null && x.IsPlayer() && !x.IsSleeping());
                        hasNearbyPlayers = gridCount > 0;
                    }
                    
                    shouldSleep = !hasNearbyPlayers;
                    }
                    else
                    {
                        // ai_dormant is false, so NPCs should not sleep
                        shouldSleep = false;
                    }
                }
                else
                {
                    // Use individual plugin's sleep system (original behavior)
                    if (!Config.CanSleep) return;
                    
                    // Use BotReSpawn approach: GetPlayersInSphere (not Fast) with simpler filter
                    BasePlayer[] localPlayerResults = new BasePlayer[64];
                    int playerCount = Query.Server.GetPlayersInSphere(transform.position, Config.SleepDistance, localPlayerResults, x => x != null && x.net?.connection != null && x.userID.IsSteamId() && !x.IsNpc && !x.IsSleeping());
                    shouldSleep = playerCount == 0;
                }
                
                if (Brain.sleeping == shouldSleep) return;
                Brain.sleeping = shouldSleep;
                
                {
                    if (Brain.sleeping && Brain.Navigator != null)
                    {
                        SetDestination(HomePosition, 2f, BaseNavigator.NavigationSpeed.Fast);
                    }
                    else if (NavAgent != null)
                    {
                        NavAgent.enabled = true;
                    }
                }
            }
            #endregion Sleep
        }

        public class CustomScientistBrain : ScientistBrain
        {
            internal CustomScientistNpc Npc { get; set; } = null;

            public override void AddStates()
            {
                // PERFORMANCE FIX: Ensure Npc is set before accessing Config
                // AddStates can be called before InitializeAI, so we need to get the entity here
                if (Npc == null) Npc = GetEntity() as CustomScientistNpc;
                
                // Null check to prevent NullReferenceException during initialization
                if (Npc == null || Npc.Config == null)
                {
                    _ins.PrintWarning($" CustomScientistBrain.AddStates() called but Npc or Config is null! Skipping state initialization.");
                    states = new Dictionary<AIState, BasicAIState>();
                    return;
                }
                
                states = new Dictionary<AIState, BasicAIState>();
                if (Npc.Config.States != null)
                {
                    if (Npc.Config.States.Contains("RoamState")) AddState(new RoamState(Npc));
                    if (Npc.Config.States.Contains("ChaseState")) AddState(new ChaseState(Npc));
                    if (Npc.Config.States.Contains("CombatState")) AddState(new CombatState(Npc));
                    if (Npc.Config.States.Contains("IdleState"))
                    {
                        AddState(new IdleState(Npc));
                    }
                    if (Npc.Config.States.Contains("CombatStationaryState"))
                    {
                        AddState(new CombatStationaryState(Npc));
                    }
                    if (Npc.Config.States.Contains("RaidState"))
                    {
                        Npc.IsRaidState = true;
                        AddState(new RaidState(Npc));
                    }
                    if (Npc.Config.States.Contains("RaidStateMelee"))
                    {
                        Npc.IsRaidStateMelee = true;
                        AddState(new RaidStateMelee(Npc));
                    }
                    if (Npc.Config.States.Contains("SledgeState")) AddState(new SledgeState(Npc));
                    if (Npc.Config.States.Contains("BlazerState")) AddState(new BlazerState(Npc));
                    if (Npc.Config.States.Contains("FarmState")) AddState(new FarmState(Npc));
                    if (Npc.Config.States.Contains("BuildState")) AddState(new BuildState(Npc));
                }
            }

            public override void InitializeAI()
            {
                if (Npc == null) Npc = GetEntity() as CustomScientistNpc;
                
                // Critical null checks before proceeding
                if (Npc == null || Npc.Config == null)
                {
                    _ins.PrintWarning($" CustomScientistBrain.InitializeAI() called but Npc or Config is null! Skipping initialization.");
                    return;
                }
                
                Npc.HasBrain = true;
                Navigator = GetComponent<BaseNavigator>();
                
                // Null check Navigator before using
                if (Navigator == null)
                {
                    _ins.PrintWarning($" CustomScientistBrain.InitializeAI() failed to get BaseNavigator component!");
                    return;
                }
                
                Navigator.Speed = Npc.Config.Speed;
                
                // Enhanced navigation configuration (from ChaosNPC improvements)
                // Enable all navigation methods for maximum flexibility
                Navigator.CanUseNavMesh = true;
                Navigator.CanUseBaseNav = true;
                Navigator.CanUseAStar = true;
                Navigator.CanUseCustomNav = false;
                
                // Set DefaultArea for BaseNavigator (helps with building navigation)
                // BaseNavigator will use this as fallback when NavMesh is unavailable
                Navigator.DefaultArea = "NavMesh";
                // Improve pursuit defaults for aggressive NPCs
                Navigator.MoveTowardsSpeed = BaseNavigator.NavigationSpeed.Fast;
                Navigator.FaceMoveTowardsTarget = true;
                
                InvokeRandomized(DoMovementTick, 1f, 0.1f, 0.01f);

                AttackRangeMultiplier = Npc.Config.AttackRangeMultiplier;
                MemoryDuration = Npc.Config.MemoryDuration;
                SenseRange = Npc.Config.SenseRange;
                TargetLostRange = SenseRange * 2f;
                VisionCone = Vector3.Dot(Vector3.forward, Quaternion.Euler(0f, Npc.Config.VisionCone, 0f) * Vector3.forward);
                CheckVisionCone = Npc.Config.CheckVisionCone;
                CheckLOS = true;
                IgnoreNonVisionSneakers = true;
                MaxGroupSize = 0;
                ListenRange = Npc.Config.ListenRange;
                HostileTargetsOnly = Npc.Config.HostileTargetsOnly;
                IgnoreSafeZonePlayers = Npc.Config.IgnoreSafeZonePlayers || !HostileTargetsOnly;
                SenseTypes = EntityType.Player;
                if (_ins._config.CanTargetNpc) SenseTypes |= EntityType.BasePlayerNPC;
                if (_ins._config.CanTargetAnimal) SenseTypes |= EntityType.NPC;
                RefreshKnownLOS = false;
                IgnoreNonVisionMaxDistance = ListenRange / 3f;
                IgnoreSneakersMaxDistance = IgnoreNonVisionMaxDistance / 3f;
                Senses.Init(Npc, this, MemoryDuration, SenseRange, TargetLostRange, VisionCone, CheckVisionCone, CheckLOS, IgnoreNonVisionSneakers, ListenRange, HostileTargetsOnly, false, IgnoreSafeZonePlayers, SenseTypes, RefreshKnownLOS);

                ThinkMode = AIThinkMode.Interval;
                thinkRate = 0.25f;
                PathFinder = new HumanPathFinder();
                ((HumanPathFinder)PathFinder).Init(Npc);
            }

            public override void Think(float delta)
            {
                // Early exit checks - order matters for performance (most common first)
                if (Npc == null) return;
                if (Npc.IsFrozen) return;
                
                // Null check Config early to avoid repeated checks
                if (Npc.Config == null) return;
                
                lastThinkTime = Time.time;
                
                // CRITICAL: Check for nearby players FIRST, even if sleeping
                // This ensures NPCs wake up when players enter range
                if (_ins._config.ForceRespectAiDormant && AiManager.ai_dormant)
                {
                    // Check if NPC should be dormant based on player distance
                    // Use the maximum of Config.SleepDistance, DefaultSleepDistance, and server's wakeup range
                    float serverWakeupRange = AiManager.ai_to_player_distance_wakeup_range;
                    float configSleepDistance = Npc.Config.CanSleep ? Npc.Config.SleepDistance : 0f;
                    float defaultSleepDistance = _ins._config.DefaultSleepDistance;
                    float wakeupRange = Mathf.Max(serverWakeupRange, Mathf.Max(configSleepDistance, defaultSleepDistance));
                    
                    // Use BotReSpawn approach: GetPlayersInSphere (not Fast) with simpler filter
                    BasePlayer[] localPlayerResults = new BasePlayer[64];
                    int playerCount = BaseEntity.Query.Server.GetPlayersInSphere(Npc.transform.position, wakeupRange, localPlayerResults, x => x != null && x.net?.connection != null && x.userID.IsSteamId() && !x.IsNpc && !x.IsSleeping());
                    bool hasNearbyPlayers = playerCount > 0;
                    
                    if (!hasNearbyPlayers)
                    {
                        // NPC is dormant - disable NavAgent and sleep
                        if (Npc.NavAgent != null && Npc.NavAgent.enabled)
                        {
                            Npc.NavAgent.enabled = false;
                        }
                        // Use IAISleepable interface to properly sleep (it checks sleeping internally)
                        if (this is IAISleepable sleepable && !sleeping)
                        {
                            sleepable.SleepAI();
                        }
                        else if (!sleeping)
                        {
                            sleeping = true;
                        }
                        return;
                    }
                    else
                    {
                        // Player nearby - FORCE WAKE UP (BotReSpawn approach: set IsDormant = false)
                        Npc.IsDormant = false;
                        
                        // Use IAISleepable interface to properly wake (it checks sleeping internally)
                        if (this is IAISleepable sleepable && sleeping)
                        {
                            sleepable.WakeAI();
                        }
                        else if (sleeping)
                        {
                            sleeping = false;
                        }
                        // Ensure NavAgent is enabled (WakeAI should do this, but be safe)
                        if (Npc.NavAgent != null && !Npc.NavAgent.enabled)
                        {
                            Npc.NavAgent.enabled = true;
                        }
                        // Continue with normal Think() processing below
                    }
                }
                
                // Handle sleeping state (only if ForceRespectAiDormant is not enabled or ai_dormant is false)
                if (sleeping)
                {
                    // Null check NavAgent before accessing
                    if (Npc.NavAgent != null && Npc.NavAgent.enabled && Npc.DistanceFromBase < Npc.Config.RoamRange)
                    {
                        Npc.NavAgent.enabled = false;
                    }
                    return;
                }
                
                // Update senses and targeting (only if not running away from water)
                if (!Npc.IsRunAwayWater)
                {
                    // Null check Senses before Update (may not be initialized yet)
                    if (Senses != null)
                    {
                        Senses.Update();
                        Npc.CurrentTarget = Npc.GetBestTarget();
                    }
                    
                    // Update raid target if in raid state (always update when in raid state)
                    if (Npc.IsRaidState || Npc.IsRaidStateMelee)
                    {
                        Npc.CurrentRaidTarget = Npc.GetRaidTarget();
                    }
                }
                
                // Execute current state logic
                CurrentState?.StateThink(delta, this, Npc);
                
                // State selection - only process if states dictionary exists
                if (states == null) return;
                
                float single = 0f;
                BasicAIState newState = null;
                
                // Optimized state selection loop
                foreach (BasicAIState value in states.Values)
                {
                    if (value == null) continue;
                    float weight = value.GetWeight();
                    if (weight <= single) continue; // Use <= instead of < for slight optimization
                    single = weight;
                    newState = value;
                }
                
                // State transition
                if (newState != CurrentState)
                {
                    CurrentState?.StateLeave(this, Npc);
                    CurrentState = newState;
                    CurrentState?.StateEnter(this, Npc);
                }
            }

            public new class RoamState : BasicAIState
            {
                private readonly CustomScientistNpc _npc;

                public RoamState(CustomScientistNpc npc) : base(AIState.Roam) { _npc = npc; }

                public override float GetWeight() => 20f;

                public override void StateLeave(BaseAIBrain brain, BaseEntity entity) { _npc.ThrownSmoke(); }

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    // Enhanced: Better roam point management with distance tracking (from BotReSpawn improvements)
                    if (_npc.DistanceFromBase > _npc.Config.RoamRange)
                    {
                        // Return home if too far
                        BaseNavigator.NavigationSpeed speed = _npc.DistanceFromBase > 10f ? BaseNavigator.NavigationSpeed.Fast : _npc.DistanceFromBase > 5f ? BaseNavigator.NavigationSpeed.Normal : BaseNavigator.NavigationSpeed.Slow;
                        if (_npc.SetDestination(_npc.HomePosition, 2f, speed))
                        {
                            _npc.RoamPoint = _npc.HomePosition;
                            _npc.LastMove = DateTime.Now;
                            _npc.RoamDistance = Vector3.Distance(_npc.HomePosition, _npc.transform.position);
                        }
                    }
                    else
                    {
                        // Enhanced roam point logic with distance tracking
                        _npc.RoamDistance1 = Vector3.Distance(_npc.RoamPoint, _npc.transform.position);
                        
                        // If we're getting closer to roam point and still have distance to go, continue
                        if (_npc.RoamDistance1 < _npc.RoamDistance && _npc.RoamDistance1 > 2f && _npc.RoamPoint != Vector3.zero)
                        {
                            _npc.LastMove = DateTime.Now;
                            _npc.RoamDistance = _npc.RoamDistance1;
                            _npc.SetDestination(_npc.RoamPoint, 2f, BaseNavigator.NavigationSpeed.Slow);
                        }
                        else
                        {
                            // Check pause length before finding new roam point
                            float pauseLength = 0f; // Config could have Roam_Pause_Length, but using 0 for now
                            if ((DateTime.Now - _npc.LastMove).TotalSeconds < pauseLength + 2.1f)
                            {
                                return StateStatus.Running;
                            }
                            
                            // Find new roam point
                            if (_npc.Config.RoamRange > 2f)
                            {
                                Vector3 newRoamPoint = _npc.GetNearNavPoint((int)(_npc.Config.RoamRange - 2f));
                                
                                if (newRoamPoint != Vector3.zero && newRoamPoint != _npc.transform.position)
                                {
                                    if (_npc.SetDestination(newRoamPoint, 2f, BaseNavigator.NavigationSpeed.Slow))
                                    {
                                        _npc.LastMove = DateTime.Now;
                                        _npc.RoamDistance = Vector3.Distance(newRoamPoint, _npc.transform.position);
                                        _npc.RoamPoint = newRoamPoint;
                                    }
                                }
                                else
                                {
                                    // Fallback: Use building-aware finder for random position (better for building navigation)
                                    Vector3 randomPos = _npc.GetRandomPos(_npc.HomePosition, _npc.Config.RoamRange - 2f);
                                    // Use EnhancedNavmeshSpawnPoint to find valid building position
                                    if (EnhancedNavmeshSpawnPoint.Find(randomPos, (int)(_npc.Config.RoamRange - 2f), out Vector3 validPos, areaMask: 25))
                                    {
                                        randomPos = validPos;
                                    }
                                    if (_npc.SetDestination(randomPos, 2f, BaseNavigator.NavigationSpeed.Slow))
                                    {
                                        _npc.LastMove = DateTime.Now;
                                        _npc.RoamDistance = Vector3.Distance(randomPos, _npc.transform.position);
                                        _npc.RoamPoint = randomPos;
                                    }
                                }
                            }
                        }
                        
                        // Enhanced: Stuck detection and recovery during roam
                        if (brain.Navigator != null && brain.Navigator.StuckOffNavmesh)
                        {
                            if (_npc.RoamPoint != Vector3.zero)
                                _npc.transform.position = _npc.RoamPoint;
                            else if (_npc.HomePosition != Vector3.zero)
                                _npc.transform.position = _npc.HomePosition;
                            brain.Navigator.SetNavMeshEnabled(true);
                            brain.Navigator.PlaceOnNavMesh(2f);
                        }
                    }

                    _npc.TryDeployTrapDuringRoam();
                    return StateStatus.Running;
                }
            }

            public new class ChaseState : BasicAIState
            {
                private readonly CustomScientistNpc _npc;

                public ChaseState(CustomScientistNpc npc) : base(AIState.Chase) { _npc = npc; }

                public override float GetWeight() => _npc.CanChaseState() ? 30f : 0f;

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    if (_npc.CurrentTarget == null) return StateStatus.Error;
                    Vector3 targetPos = _npc.CurrentTarget.transform.position;
                    float distance = 2f;
                    float height = targetPos.y - TerrainMeta.HeightMap.GetHeight(targetPos);
                    if (height > 0f) distance += height;
                    // Enhanced: Always use Fast when chasing a target (from BotReSpawn improvements)
                    _npc.SetDestination(targetPos, distance, BaseNavigator.NavigationSpeed.Fast);
                    return StateStatus.Running;
                }
            }

            public new class CombatState : BasicAIState
            {
                private readonly CustomScientistNpc _npc;
                private float _nextStrafeTime;

                public CombatState(CustomScientistNpc npc) : base(AIState.Combat) { _npc = npc; }

                public override float GetWeight() => _npc.CanCombatState() ? 40f : 0f;

                public override void StateLeave(BaseAIBrain brain, BaseEntity entity)
                {
                    _npc.SetDucked(false);
                    brain.Navigator.ClearFacingDirectionOverride();
                }

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    if (_npc.CurrentTarget == null) return StateStatus.Error;
                    
                    float distanceToTarget = _npc.DistanceToTarget;
                    const float idealEngagementDistance = 8f; // Target distance: 8 meters when aggro
                    
                    // Opportunistic cover seeking
                    if (Time.time >= _npc.NextCoverCheckTime)
                    {
                        _npc.NextCoverCheckTime = Time.time + UnityEngine.Random.Range(2f, 4f);
                        // Prefer cover when ranged and not already very close
                        if (!(_npc.CurrentWeapon is BaseMelee) && distanceToTarget > 6f)
                        {
                            if (_npc.TryTakeCover(12f)) return StateStatus.Running;
                        }
                    }
                    brain.Navigator.SetFacingDirectionEntity(_npc.CurrentTarget);
                    
                    if (_npc.CurrentWeapon is BaseProjectile)
                    {
                        bool aggressiveStrafe = _npc.Config != null && _npc.Config.AggressiveCombatStrafe;
                        float strafeRadius = aggressiveStrafe ? 3f : 2f;
                        // Enhanced: Actively close the gap when target is far (like BotReSpawn)
                        // If target is beyond 8 meters, move toward target while shooting
                        if (distanceToTarget > idealEngagementDistance)
                        {
                            // Move toward target to close the gap to ~8 meters (shoot while moving)
                            Vector3 targetPos = _npc.CurrentTarget.transform.position;
                            Vector3 direction = (targetPos - _npc.transform.position).normalized;
                            Vector3 desiredPos = targetPos - direction * idealEngagementDistance;
                            _npc.SetDestination(desiredPos, 2f, BaseNavigator.NavigationSpeed.Fast);
                            
                            // Shoot while closing the gap
                            if (_npc.CurrentWeapon is BaseLauncher) _npc.FireGrenadeLauncher();
                            else
                            {
                                _npc.IsAttackingBaseProjectile = true;
                                _npc.ShotTest(distanceToTarget);
                                _npc.IsAttackingBaseProjectile = false;
                            }
                        }
                        else
                        {
                            // Close enough - use strafing behavior (original logic)
                            if (Time.time > _nextStrafeTime)
                            {
                                int duckRoll = aggressiveStrafe ? UnityEngine.Random.Range(0, 5) : UnityEngine.Random.Range(0, 3);
                                if (duckRoll == 1)
                                {
                                    float deltaTime = aggressiveStrafe
                                        ? (_npc.CurrentWeapon is BaseLauncher ? UnityEngine.Random.Range(0.35f, 0.65f) : UnityEngine.Random.Range(0.55f, 1f))
                                        : (_npc.CurrentWeapon is BaseLauncher ? UnityEngine.Random.Range(0.5f, 1f) : UnityEngine.Random.Range(1f, 2f));
                                    _nextStrafeTime = Time.time + deltaTime;
                                    _npc.SetDucked(true);
                                    brain.Navigator.Stop();
                                }
                                else
                                {
                                    float deltaTime = aggressiveStrafe
                                        ? (_npc.CurrentWeapon is BaseLauncher ? UnityEngine.Random.Range(0.65f, 1.05f) : UnityEngine.Random.Range(0.95f, 1.55f))
                                        : (_npc.CurrentWeapon is BaseLauncher ? UnityEngine.Random.Range(1f, 1.5f) : UnityEngine.Random.Range(2f, 3f));
                                    _nextStrafeTime = Time.time + deltaTime;
                                    _npc.SetDucked(false);
                                    // Enhanced: Use Fast when in combat with target (from BotReSpawn improvements)
                                    _npc.SetDestination(_npc.GetRandomPos(_npc.transform.position, strafeRadius), 2f, BaseNavigator.NavigationSpeed.Fast);
                                }
                                if (_npc.CurrentWeapon is BaseLauncher) _npc.FireGrenadeLauncher();
                                else
                                {
                                    _npc.IsAttackingBaseProjectile = true;
                                    _npc.ShotTest(distanceToTarget);
                                    _npc.IsAttackingBaseProjectile = false;
                                }
                            }
                        }
                    }
                    else if (_npc.CurrentWeapon is FlameThrower)
                    {
                        if (_npc.CurrentWeapon.ShortPrefabName == "militaryflamethrower.entity")
                        {
                            if (distanceToTarget < _npc.CurrentWeapon.effectiveRange)
                            {
                                // Enhanced: Use Fast when in combat with target (from BotReSpawn improvements)
                                _npc.SetDestination(_npc.GetRandomPos(_npc.transform.position, 2f), 2f, BaseNavigator.NavigationSpeed.Fast);
                                _npc.FireFlameThrower();
                            }
                            else _npc.SetDestination(GetDestinationPos(_npc.CurrentTarget.transform.position), 2f, BaseNavigator.NavigationSpeed.Fast);
                        }
                        else if (_npc.CurrentWeapon.ShortPrefabName == "flamethrower.entity")
                        {
                            if (distanceToTarget < _npc.CurrentWeapon.effectiveRange) _npc.FireFlameThrower();
                            _npc.SetDestination(GetDestinationPos(_npc.CurrentTarget.transform.position), 2f, BaseNavigator.NavigationSpeed.Fast);
                        }
                    }
                    else if (_npc.CurrentWeapon is BaseMelee)
                    {
                        if (distanceToTarget < _npc.CurrentWeapon.effectiveRange * 2f) _npc.UseMeleeWeapon();
                        _npc.SetDestination(GetDestinationPos(_npc.CurrentTarget.transform.position), 2f, BaseNavigator.NavigationSpeed.Fast);
                    }
                    else if (_npc.IsTimedExplosiveCurrentWeapon)
                    {
                        _npc.ExplosionBomber(_npc.CurrentTarget);
                    }
                    return StateStatus.Running;
                }

                private Vector3 GetDestinationPos(Vector3 pos)
                {
                    if ((_ins._config.CanTargetNpc && _npc.CurrentTarget is NPCPlayer) || (_ins._config.CanTargetAnimal && _npc.CurrentTarget is BaseAnimalNPC)) return _npc.GetRandomPos(pos, 2f);
                    else return pos;
                }
            }

            public new class IdleState : BasicAIState
            {
                private readonly CustomScientistNpc _npc;

                public IdleState(CustomScientistNpc npc) : base(AIState.Idle) { _npc = npc; }

                public override float GetWeight() => 10f;

                public override void StateLeave(BaseAIBrain brain, BaseEntity entity) { _npc.ThrownSmoke(); }
            }

            public new class CombatStationaryState : BasicAIState
            {
                private readonly CustomScientistNpc _npc;
                private float _nextStrafeTime;

                public CombatStationaryState(CustomScientistNpc npc) : base(AIState.CombatStationary) { _npc = npc; }

                public override float GetWeight() => _npc.CanCombatStationaryState() ? 40f : 0f;

                public override void StateLeave(BaseAIBrain brain, BaseEntity entity)
                {
                    if (!_npc.IsMounted()) _npc.SetDucked(false);
                    brain.Navigator.ClearFacingDirectionOverride();
                }

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    if (_npc.CurrentTarget == null) return StateStatus.Error;
                    // Opportunistic cover seeking when stationary with ranged
                    if (Time.time >= _npc.NextCoverCheckTime)
                    {
                        _npc.NextCoverCheckTime = Time.time + UnityEngine.Random.Range(3f, 5f);
                        if (!(_npc.CurrentWeapon is BaseMelee) && _npc.DistanceToTarget > 6f)
                        {
                            if (_npc.TryTakeCover(10f)) return StateStatus.Running;
                        }
                    }
                    brain.Navigator.SetFacingDirectionEntity(_npc.CurrentTarget);
                    if (_npc.CurrentWeapon is BaseProjectile)
                    {
                        bool aggressiveStrafe = _npc.Config != null && _npc.Config.AggressiveCombatStrafe;
                        if (Time.time > _nextStrafeTime)
                        {
                            int duckRoll = aggressiveStrafe ? UnityEngine.Random.Range(0, 5) : UnityEngine.Random.Range(0, 3);
                            if (duckRoll == 1)
                            {
                                float deltaTime = aggressiveStrafe
                                    ? (_npc.CurrentWeapon is BaseLauncher ? UnityEngine.Random.Range(0.35f, 0.65f) : UnityEngine.Random.Range(0.55f, 1f))
                                    : (_npc.CurrentWeapon is BaseLauncher ? UnityEngine.Random.Range(0.5f, 1f) : UnityEngine.Random.Range(1f, 2f));
                                _nextStrafeTime = Time.time + deltaTime;
                                if (!_npc.IsMounted()) _npc.SetDucked(true);
                            }
                            else
                            {
                                float deltaTime = aggressiveStrafe
                                    ? (_npc.CurrentWeapon is BaseLauncher ? UnityEngine.Random.Range(0.65f, 1.05f) : UnityEngine.Random.Range(0.95f, 1.55f))
                                    : (_npc.CurrentWeapon is BaseLauncher ? UnityEngine.Random.Range(1f, 1.5f) : UnityEngine.Random.Range(2f, 3f));
                                _nextStrafeTime = Time.time + deltaTime;
                                if (!_npc.IsMounted()) _npc.SetDucked(false);
                            }
                            if (_npc.CurrentWeapon is BaseLauncher) _npc.FireGrenadeLauncher();
                            else
                            {
                                _npc.IsAttackingBaseProjectile = true;
                                _npc.ShotTest(_npc.DistanceToTarget);
                                _npc.IsAttackingBaseProjectile = false;
                            }
                        }
                    }
                    else if (_npc.CurrentWeapon is FlameThrower && _npc.DistanceToTarget < _npc.CurrentWeapon.effectiveRange) _npc.FireFlameThrower();
                    else if (_npc.CurrentWeapon is BaseMelee && _npc.DistanceToTarget < _npc.CurrentWeapon.effectiveRange * 2f) _npc.UseMeleeWeapon();
                    return StateStatus.Running;
                }
            }

            public class RaidState : BasicAIState
            {
                private readonly CustomScientistNpc _npc;

                public RaidState(CustomScientistNpc npc) : base(AIState.Cooldown) { _npc = npc; }

                public override float GetWeight() => _npc.CanRaidState() ? 50f : 0f;

                public override void StateLeave(BaseAIBrain brain, BaseEntity entity)
                {
                    if (!_npc.IsMounted()) _npc.SetDucked(false);
                    brain.Navigator.ClearFacingDirectionOverride();
                }

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    if (_npc.IsFireC4 || _npc.IsFireRocketLauncher) return StateStatus.Running;
                    if (_npc.CurrentRaidTarget == null) return StateStatus.Error;
                    float distance = _npc.DistanceToCurrentRaidTarget;
                    if (distance > 5f && !_npc.StartExplosion(_npc.CurrentRaidTarget) && !_npc.IsMounted())
                    {
                        _npc.SetDucked(false);
                        // Enhanced: Use Fast when raiding targets (from BotReSpawn improvements)
                        _npc.SetDestination(_npc.CurrentRaidTarget.transform.position, 5f, _npc.CurrentRaidTarget is AutoTurret || _npc.CurrentRaidTarget is GunTrap || _npc.CurrentRaidTarget is FlameTurret || distance > 30f ? BaseNavigator.NavigationSpeed.Fast : distance > 5f ? BaseNavigator.NavigationSpeed.Fast : BaseNavigator.NavigationSpeed.Slow);
                    }
                    return StateStatus.Running;
                }
            }

            public class RaidStateMelee : BasicAIState
            {
                private readonly CustomScientistNpc _npc;

                public RaidStateMelee(CustomScientistNpc npc) : base(AIState.Cooldown) { _npc = npc; }

                public override float GetWeight() => _npc.CanRaidStateMelee() ? 50f : 0f;

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    if (_npc.CurrentRaidTarget == null) return StateStatus.Error;
                    if (_npc.DistanceToCurrentRaidTarget < 6f)
                    {
                        _npc.viewAngles = Quaternion.LookRotation(_npc.CurrentRaidTarget.transform.position - _npc.transform.position).eulerAngles;
                        if (_npc.CurrentWeapon is BaseMelee)
                        {
                            BaseMelee weapon = _npc.CurrentWeapon as BaseMelee;
                            if (!weapon.HasAttackCooldown())
                            {
                                DealDamage(weapon);
                                _npc.UseMeleeWeapon(false);
                            }
                        }
                        else if (_npc.IsTimedExplosiveCurrentWeapon) _npc.ExplosionBomber(_npc.CurrentRaidTarget);
                        else return StateStatus.Error;
                    }
                    else _npc.SetDestination(_npc.CurrentRaidTarget.transform.position, 6f, BaseNavigator.NavigationSpeed.Fast);
                    return StateStatus.Running;
                }

                private void DealDamage(BaseMelee weapon)
                {
                    float meleeMul = _npc.Config.MeleeDamageScale > 0f ? _npc.Config.MeleeDamageScale : _npc.Config.DamageScale;
                    _npc.CurrentRaidTarget.health -= weapon.damageTypes.Sum(x => x.amount) * weapon.npcDamageScale * meleeMul;
                    _npc.CurrentRaidTarget.SendNetworkUpdate();
                    if (_npc.CurrentRaidTarget.health <= 0f && _npc.CurrentRaidTarget.IsExists()) _npc.CurrentRaidTarget.Kill(BaseNetworkable.DestroyMode.Gib);
                }
            }

            public class SledgeState : BasicAIState
            {
                private readonly CustomScientistNpc _npc;
                private readonly HashSet<Vector3> _positions;

                public SledgeState(CustomScientistNpc npc) : base(AIState.Cooldown)
                {
                    _npc = npc;
                    _positions = new HashSet<Vector3>(_ins.WallFrames);
                    _positions.Add(_ins.GeneralPosition);
                }

                public override float GetWeight()
                {
                    if (_npc.CurrentTarget != null && _npc.CanSeeTarget(_npc.CurrentTarget)) return 0f;
                    return 50f;
                }

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    Vector3 barricadePos = _ins.CustomBarricades.Count == 0 ? Vector3.zero : _ins.CustomBarricades.Min(DistanceToPos);
                    bool haveBarricade = barricadePos != Vector3.zero;

                    Vector3 generalPos = _ins.GeneralPosition;
                    bool haveGeneral = _ins.GeneralPosition != Vector3.zero;

                    bool nearBarricade = haveBarricade && DistanceToPos(barricadePos) < 1.5f;
                    bool nearGeneral = haveGeneral && DistanceToPos(generalPos) < 1.5f;

                    if (nearBarricade || nearGeneral)
                    {
                        _npc.viewAngles = nearBarricade ? Quaternion.LookRotation(barricadePos + new Vector3(0f, 0.5f, 0f) - _npc.transform.position).eulerAngles : Quaternion.LookRotation(generalPos - _npc.transform.position).eulerAngles;
                        if (_npc.CurrentWeapon is BaseMelee) _npc.UseMeleeWeapon(false);
                        else if (_npc.IsTimedExplosiveCurrentWeapon) _npc.ExplosionBomber();
                    }
                    else if (!brain.Navigator.Moving) _npc.SetDestination(GetResultPos(), 1.5f, BaseNavigator.NavigationSpeed.Fast);

                    return StateStatus.Running;
                }

                private Vector3 GetResultPos()
                {
                    List<Vector3> list = Pool.Get<List<Vector3>>();
                    foreach (Vector3 pos in _positions) if (NecessaryPos(pos)) list.Add(pos);
                    list = list.OrderByQuickSort(DistanceToPos);

                    Vector3 point1 = list[0];
                    Vector3 point2 = list[1];

                    float distance0 = DistanceToPos(_ins.GeneralPosition);
                    float distance3 = Vector3.Distance(_ins.GeneralPosition, point1);

                    Vector3 result = _npc.GetRandomPos(distance3 < Vector3.Distance(_ins.GeneralPosition, point2) ? point1 : distance0 >= DistanceToPos(point2) ? distance0 < distance3 ? point2 : point1 : point2, 1.5f);

                    Pool.FreeUnmanaged(ref list);

                    return result;
                }

                private float DistanceToPos(Vector3 pos) => Vector3.Distance(_npc.transform.position, pos);

                private bool NecessaryPos(Vector3 pos)
                {
                    if (pos.IsEqualVector3(_ins.GeneralPosition) || Vector3.Distance(_npc.transform.position, pos) > 0.5f) return true;
                    foreach (Vector3 b in _ins.CustomBarricades)
                        if (pos.IsEqualVector3(b)) return true;
                    return false;
                }
            }

            public class BlazerState : BasicAIState
            {
                private readonly CustomScientistNpc _npc;
                private readonly float _radius;
                private readonly Vector3 _center;
                private readonly List<Vector3> _circlePositions = new List<Vector3>();

                public BlazerState(CustomScientistNpc npc) : base(AIState.Cooldown)
                {
                    _npc = npc;
                    _radius = _npc.Config.VisionCone;
                    _center = _ins.GeneralPosition;
                    for (int i = 1; i <= 36; i++) _circlePositions.Add(new Vector3(_center.x + _radius * Mathf.Sin(i * 10f * Mathf.Deg2Rad), _center.y, _center.z + _radius * Mathf.Cos(i * 10f * Mathf.Deg2Rad)));
                }

                public override float GetWeight()
                {
                    if (IsInside) return 45f;
                    if (_npc.CurrentTarget == null) return 45f;
                    else
                    {
                        if (IsOutsideTarget) return 0f;
                        else
                        {
                            Vector3 vector3 = GetCirclePos(GetMovePos(_npc.CurrentTarget.transform.position));
                            if (DistanceToPos(vector3) > 2f) return 45f;
                            else return 0f;
                        }
                    }
                }

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    if (IsInside) _npc.SetDestination(GetCirclePos(GetMovePos(_npc.transform.position)), 2f, BaseNavigator.NavigationSpeed.Fast);
                    if (_npc.CurrentTarget == null) _npc.CurrentTarget = GetTargetPlayer();
                    if (_npc.CurrentTarget == null) _npc.SetDestination(GetCirclePos(GetMovePos(_npc.transform.position)), 2f, BaseNavigator.NavigationSpeed.Fast);
                    else _npc.SetDestination(GetNextPos(GetMovePos(_npc.CurrentTarget.transform.position)), 2f, BaseNavigator.NavigationSpeed.Fast);
                    return StateStatus.Running;
                }

                private Vector3 GetNextPos(Vector3 targetPos)
                {
                    int numberTarget = _circlePositions.IndexOf(GetCirclePos(targetPos));
                    int numberNear = _circlePositions.IndexOf(GetNearCirclePos);
                    int countNext = numberTarget < numberNear ? _circlePositions.Count - 1 - numberNear + numberTarget : numberTarget - numberNear;
                    if (countNext < 18)
                    {
                        if (numberNear + 1 > 35) return _circlePositions[0];
                        else return _circlePositions[numberNear + 1];
                    }
                    else
                    {
                        if (numberNear - 1 < 0) return _circlePositions[35];
                        else return _circlePositions[numberNear - 1];
                    }
                }

                private Vector3 GetCirclePos(Vector3 targetPos) => _circlePositions.Min(x => Vector3.Distance(targetPos, x));

                private Vector3 GetMovePos(Vector3 targetPos)
                {
                    Vector3 normal3 = (targetPos - _center).normalized;
                    Vector2 vector2 = new Vector2(normal3.x, normal3.z) * _radius;
                    return _center + new Vector3(vector2.x, _center.y, vector2.y);
                }

                private BasePlayer GetTargetPlayer()
                {
                    List<BasePlayer> list = Pool.Get<List<BasePlayer>>();
                    Vis.Entities(_center, _npc.Config.ChaseRange, list, 1 << 17);
                    HashSet<BasePlayer> players = list.Where(x => x.IsPlayer());
                    Pool.FreeUnmanaged(ref list);
                    return players.Count == 0 ? null : players.Min(x => DistanceToPos(x.transform.position));
                }

                private Vector3 GetNearCirclePos => _circlePositions.Min(DistanceToPos);

                private bool IsInside => DistanceToPos(_center) < _radius - 2f;

                private bool IsOutsideTarget => Vector3.Distance(_center, _npc.CurrentTarget.transform.position) > _radius + 2f;

                private float DistanceToPos(Vector3 pos) => Vector3.Distance(_npc.transform.position, pos);
            }

            public class FarmState : BasicAIState
            {
                private readonly CustomScientistNpc _npc;
                private BaseEntity _currentFarmTarget;
                private float _nextFarmCheckTime;
                private float _lastHarvestTime;

                public FarmState(CustomScientistNpc npc) : base(AIState.Cooldown) { _npc = npc; }

                public override float GetWeight()
                {
                    if (!_npc.Config.CanFarm) return 0f;
                    if (_npc.CurrentTarget != null) return 0f; // Don't farm when in combat
                    if (UnityEngine.Time.time < _nextFarmCheckTime) return 0f;
                    
                    // Check for nearby resources to farm
                    _currentFarmTarget = FindNearbyResource();
                    return _currentFarmTarget != null ? 35f : 0f;
                }

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    if (_currentFarmTarget == null || !_currentFarmTarget.IsExists())
                    {
                        _currentFarmTarget = FindNearbyResource();
                        if (_currentFarmTarget == null)
                        {
                            _nextFarmCheckTime = UnityEngine.Time.time + 5f;
                            return StateStatus.Error;
                        }
                    }

                    float distance = Vector3.Distance(_npc.transform.position, _currentFarmTarget.transform.position);
                    
                    if (distance > 3f)
                    {
                        // Move closer to resource
                        _npc.SetDestination(_currentFarmTarget.transform.position, 2f, BaseNavigator.NavigationSpeed.Normal);
                        return StateStatus.Running;
                    }

                    // At resource - harvest it
                    if (UnityEngine.Time.time - _lastHarvestTime > 1f) // Harvest every second
                    {
                        HarvestResource(_currentFarmTarget);
                        _lastHarvestTime = UnityEngine.Time.time;
                    }
                    
                    // Check if resource is depleted
                    ResourceDispenser dispenser = _currentFarmTarget.GetComponent<ResourceDispenser>();
                    if (dispenser != null && dispenser.fractionRemaining <= 0f)
                    {
                        _currentFarmTarget = null;
                        _nextFarmCheckTime = UnityEngine.Time.time + 2f;
                    }
                    
                    return StateStatus.Running;
                }

                private BaseEntity FindNearbyResource()
                {
                    List<BaseEntity> resources = Pool.Get<List<BaseEntity>>();
                    Vis.Entities(_npc.transform.position, _npc.Config.FarmRange, resources);
                    
                    BaseEntity bestResource = null;
                    float closestDistance = float.MaxValue;
                    
                    foreach (BaseEntity entity in resources)
                    {
                        if (entity == null || entity.IsDestroyed) continue;
                        
                        // Check for ResourceDispenser (trees, ore nodes, etc.)
                        ResourceDispenser dispenser = entity.GetComponent<ResourceDispenser>();
                        if (dispenser != null && dispenser.fractionRemaining > 0f)
                        {
                            float distance = Vector3.Distance(_npc.transform.position, entity.transform.position);
                            if (distance < closestDistance)
                            {
                                bestResource = entity;
                                closestDistance = distance;
                            }
                        }
                        
                        // Check for GrowableEntity (plants) - use reflection to avoid type issues
                        var growableType = entity.GetType();
                        if (growableType.Name == "GrowableEntity" || growableType.Name.Contains("Growable"))
                        {
                            var healthProperty = growableType.GetProperty("health");
                            if (healthProperty != null)
                            {
                                float health = (float)healthProperty.GetValue(entity);
                                if (health > 0f)
                                {
                                    float distance = Vector3.Distance(_npc.transform.position, entity.transform.position);
                                    if (distance < closestDistance)
                                    {
                                        bestResource = entity;
                                        closestDistance = distance;
                                    }
                                }
                            }
                        }
                    }
                    
                    Pool.FreeUnmanaged(ref resources);
                    return bestResource;
                }

                private void HarvestResource(BaseEntity resource)
                {
                    if (resource == null || resource.IsDestroyed) return;
                    
                    // Equip appropriate tool for harvesting
                    Item tool = GetHarvestTool(resource);
                    if (tool != null && _npc.CurrentWeapon?.GetItem() != tool)
                    {
                        _npc.EquipCurrentWeapon(tool);
                    }
                    
                    // Use melee attack to harvest (simulates hitting resource)
                    if (_npc.CurrentWeapon is BaseMelee melee)
                    {
                        // Face the resource
                        _npc.SetAimDirection((resource.transform.position - _npc.transform.position).normalized);
                        
                        // Use melee weapon (false = don't apply damage to entities, but still triggers resource gathering)
                        _npc.UseMeleeWeapon(false);
                        
                        // Trigger resource gathering via damage
                        ResourceDispenser dispenser = resource.GetComponent<ResourceDispenser>();
                        if (dispenser != null)
                        {
                            // Deal damage to trigger resource gathering
                            HitInfo hitInfo = new HitInfo(_npc, resource, DamageType.Generic, 10f);
                            resource.OnAttacked(hitInfo);
                        }
                        
                        // Handle GrowableEntity (plants) via reflection
                        var growableType = resource.GetType();
                        if (growableType.Name == "GrowableEntity" || growableType.Name.Contains("Growable"))
                        {
                            HitInfo hitInfo = new HitInfo(_npc, resource, DamageType.Generic, 10f);
                            resource.OnAttacked(hitInfo);
                        }
                    }
                }

                private Item GetHarvestTool(BaseEntity resource)
                {
                    if (_npc.inventory == null || _npc.inventory.containerBelt == null) return null;
                    
                    // Determine tool based on resource type
                    string toolName = null;
                    
                    // Check resource type via reflection to avoid type issues
                    string resourceTypeName = resource.GetType().Name;
                    
                    if (resourceTypeName.Contains("Tree"))
                    {
                        toolName = "hatchet"; // Prefer hatchet for trees
                    }
                    else if (resourceTypeName.Contains("Ore") || resourceTypeName.Contains("Stone") || resourceTypeName.Contains("Metal") || resourceTypeName.Contains("Sulfur"))
                    {
                        toolName = "pickaxe"; // Prefer pickaxe for ore
                    }
                    else if (resourceTypeName.Contains("Growable") || resourceTypeName.Contains("Plant"))
                    {
                        toolName = "knife.combat"; // Use knife for plants
                    }
                    
                    if (toolName != null)
                    {
                        foreach (Item item in _npc.inventory.containerBelt.itemList)
                        {
                            if (item.info.shortname == toolName) return item;
                        }
                    }
                    
                    // Fallback: use any melee tool
                    foreach (Item item in _npc.inventory.containerBelt.itemList)
                    {
                        if (item.GetHeldEntity() is BaseMelee) return item;
                    }
                    
                    return null;
                }
            }

            public class BuildState : BasicAIState
            {
                private readonly CustomScientistNpc _npc;
                private Vector3 _buildPosition;
                private float _nextBuildCheckTime;
                private bool _hasBuildPlan;

                public BuildState(CustomScientistNpc npc) : base(AIState.Cooldown) { _npc = npc; }

                public override float GetWeight()
                {
                    if (!_npc.Config.CanBuild) return 0f;
                    if (_npc.CurrentTarget != null) return 0f; // Don't build when in combat
                    if (UnityEngine.Time.time < _nextBuildCheckTime) return 0f;
                    
                    // Check if owner is nearby and wants building
                    if (_npc.Config.IsTeammateNpc && _npc.Config.OwnerUserID != 0UL)
                    {
                        BasePlayer owner = BasePlayer.FindByID(_npc.Config.OwnerUserID);
                        if (owner != null && Vector3.Distance(_npc.transform.position, owner.transform.position) < _npc.Config.BuildRange)
                        {
                            // Check if owner has building materials
                            if (HasBuildingMaterials(owner))
                            {
                                _buildPosition = FindBuildPosition(owner);
                                _hasBuildPlan = _buildPosition != Vector3.zero;
                                return _hasBuildPlan ? 30f : 0f;
                            }
                        }
                    }
                    
                    return 0f;
                }

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    if (!_hasBuildPlan || _buildPosition == Vector3.zero)
                    {
                        _nextBuildCheckTime = UnityEngine.Time.time + 10f; // Check again in 10 seconds
                        return StateStatus.Error;
                    }

                    float distance = Vector3.Distance(_npc.transform.position, _buildPosition);
                    
                    if (distance > 3f)
                    {
                        // Move to build position
                        _npc.SetDestination(_buildPosition, 2f, BaseNavigator.NavigationSpeed.Normal);
                        return StateStatus.Running;
                    }

                    // At build position - attempt to place structure
                    // Note: Actual building placement would require more complex logic
                    // This is a simplified version that simulates building
                    AttemptBuild(_buildPosition);
                    _nextBuildCheckTime = UnityEngine.Time.time + 15f; // Wait before next build attempt
                    _hasBuildPlan = false;
                    _buildPosition = Vector3.zero;
                    
                    return StateStatus.Running;
                }

                private Vector3 FindBuildPosition(BasePlayer owner)
                {
                    // Look for a suitable build position near the owner
                    // This is a simplified version - in practice, you'd want more sophisticated logic
                    Vector3 ownerPos = owner.transform.position;
                    Vector3 forward = owner.eyes.BodyForward();
                    
                    // Try position 2 meters in front of owner
                    Vector3 candidatePos = ownerPos + forward * 2f;
                    candidatePos.y = TerrainMeta.HeightMap.GetHeight(candidatePos);
                    
                    // Check if position is clear
                    if (IsPositionClear(candidatePos))
                    {
                        return candidatePos;
                    }
                    
                    // Try positions around owner
                    for (int i = 0; i < 8; i++)
                    {
                        float angle = (360f / 8f) * i;
                        Vector3 offset = Quaternion.Euler(0, angle, 0) * Vector3.forward * 2f;
                        candidatePos = ownerPos + offset;
                        candidatePos.y = TerrainMeta.HeightMap.GetHeight(candidatePos);
                        
                        if (IsPositionClear(candidatePos))
                        {
                            return candidatePos;
                        }
                    }
                    
                    return Vector3.zero;
                }

                private bool IsPositionClear(Vector3 position)
                {
                    // Check for collisions
                    List<BaseEntity> entities = Pool.Get<List<BaseEntity>>();
                    Vis.Entities(position, 1f, entities);
                    bool hasBlockingEntity = false;
                    foreach (BaseEntity e in entities)
                    {
                        if (e is BuildingBlock || e is SimpleBuildingBlock)
                        {
                            hasBlockingEntity = true;
                            break;
                        }
                    }
                    Pool.FreeUnmanaged(ref entities);
                    
                    return !hasBlockingEntity;
                }

                private bool HasBuildingMaterials(BasePlayer player)
                {
                    if (player.inventory == null) return false;
                    
                    // Check for common building materials
                    int wood = player.inventory.GetAmount(ItemManager.FindItemDefinition("wood"));
                    int stone = player.inventory.GetAmount(ItemManager.FindItemDefinition("stone"));
                    int metal = player.inventory.GetAmount(ItemManager.FindItemDefinition("metal.fragments"));
                    
                    // Need at least some materials to build
                    return wood >= 100 || stone >= 100 || metal >= 50;
                }

                private void AttemptBuild(Vector3 position)
                {
                    // Simplified build attempt
                    // In a full implementation, you would:
                    // 1. Get building materials from owner
                    // 2. Create BuildingBlock entity
                    // 3. Place it at position
                    // 4. Handle permissions and ownership
                    
                    // For now, just log that building was attempted
                    // Actual implementation would require more complex logic
                    _ins.DebugLog($"Teammate NPC '{_npc.displayName}' attempted to build at {position}");
                }
            }
        }
        // end Controller region


        #region Harmony lifecycle
        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Init();
            LoadConfig();
            OxideCompat.EnsureDataFolders();
            OxideCompat.RegisterCommands(this);
            // Cross-mod discovery: HarmonyLoader renames assemblies, so Convoy/etc. resolve via SetData.
            AppDomain.CurrentDomain.SetData("GrimmNPC.Type", typeof(GrimmNPC));
            AppDomain.CurrentDomain.SetData("GrimmNPC.Instance", this);
            // Delay server-init work until ServerMgr exists (Harmony may load BeforeSceneLoad).
            OxideCompat.RunWhenServerInitialized(() =>
            {
                if (Instance == null) return;
                OnServerInitialized();
            });
            UnityEngine.Debug.Log("[GrimmNPC] Loaded (NpcSpawn Harmony port 3.3.04)");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            OxideCompat.UnregisterCommands();
            Unload();
            AppDomain.CurrentDomain.SetData("GrimmNPC.Instance", null);
            AppDomain.CurrentDomain.SetData("GrimmNPC.Type", null);
            Instance = null;
            UnityEngine.Debug.Log("[GrimmNPC] Unloaded");
        }

        /// <summary>Oxide timer.Once replacement.</summary>
        internal OxideCompat.TimerHelper timer => OxideCompat.Timer;

        private void Puts(string message) => UnityEngine.Debug.Log("[GrimmNPC] " + message);
        private void PrintWarning(string message) => UnityEngine.Debug.LogWarning("[GrimmNPC] " + message);
        private void SendReply(BasePlayer player, string message)
        {
            if (player != null && player.IsConnected)
                player.ChatMessage(message);
            else
                Puts(message);
        }
        #endregion Harmony lifecycle

        #region Oxide Hooks
        private static GrimmNPC _ins;
        
        // Throttling for ScarecrowProtection messages (per NPC, max once per 10 seconds)
        private readonly Dictionary<ulong, float> _scarecrowMessageThrottle = new Dictionary<ulong, float>();
        private const float SCARECROW_MESSAGE_THROTTLE_SECONDS = 10f;

        private void Init()
        {
            _ins = this;
            Instance = this;
            Kits = OxideCompat.PluginRef.Find("Kits");
            Friends = OxideCompat.PluginRef.Find("Friends");
            Clans = OxideCompat.PluginRef.Find("Clans");
        }

        private void OnServerInitialized()
        {
            CreateAllFolders();
            GeneratePositions();
            // Seed currently used NPC userIDs to prevent duplicates
            SeedUsedNpcUserIds();
            // Late-bind Kits (Harmony may load Kits after GrimmNPC; PluginRef retries via Kits_ApiType).
            if (Kits != null && Kits.Exists)
                Puts("Kits Harmony mod linked - NPC Config.Kit / GiveKit enabled.");
            else
                Puts("Kits not found yet - NPC Config.Kit skipped until Kits.dll loads (wear/belt still apply).");
            // Display NPC count on server
            GetNpcCounts(out int customScientists, out int totalScientists, out int otherScientists, out int animals, out int otherNpcPlayers, out int totalNpcs);
            Puts($"NPCs on server -> CustomScientists: {customScientists}, Vanilla Scientists: {otherScientists}, Total Scientists: {totalScientists}, Animals: {animals}, Other NPC Players: {otherNpcPlayers}, Total NPCs: {totalNpcs}");
        }

        private void Unload()
        {
            // Oxide timers are automatically cleaned up on plugin unload
            HashSet<CustomScientistNpc> killSnapshot = new HashSet<CustomScientistNpc>();
            foreach (CustomScientistNpc n in Scientists.Values) killSnapshot.Add(n);
            foreach (CustomScientistNpc npc in killSnapshot) if (npc.IsExists()) npc.Kill();
            Scientists.Clear();
            UsedNpcUserIds.Clear();
            _ins = null;
        }

        internal void OnEntityKill(CustomScientistNpc npc)
        {
            if (npc == null || npc.net == null) return;
            ulong id = npc.net.ID.Value;
            if (Scientists.ContainsKey(id)) Scientists.Remove(id);
            // Free up the reserved NPC userID
            UsedNpcUserIds.Remove(npc.userID);
        }

        internal void OnCorpsePopulate(CustomScientistNpc entity, NPCPlayerCorpse corpse) { if (corpse != null && IsCustomScientist(entity)) corpse.containers[1].ClearItemsContainer(); }

        internal object CanBradleyApcTarget(BradleyAPC apc, CustomScientistNpc entity)
        {
            if (apc == null || entity == null) return null;
            if (IsCustomScientist(entity) && entity.Config != null)
            {
                if (!entity.Config.CanBeTargetedByAPC)
                {
                    return false; // Block targeting based on config
                }
            }
            return null;
        }

        internal object OnNpcTarget(NPCPlayer attacker, CustomScientistNpc victim)
        {
            if (attacker == null || !IsCustomScientist(victim)) return null;
            // Block all targeting of CustomScientistNpc
            return false;
        }

        internal object OnNpcTarget(BaseAnimalNPC attacker, CustomScientistNpc victim)
        {
            if (attacker == null || !IsCustomScientist(victim)) return null;
            // Block all targeting of CustomScientistNpc
            return false;
        }

        // Check config options for AutoTurret targeting
        internal object OnTurretTarget(AutoTurret turret, BaseCombatEntity target)
        {
            if (turret == null || target == null) return null;
            CustomScientistNpc npc = target as CustomScientistNpc;
            if (npc != null && IsCustomScientist(npc) && npc.Config != null)
            {
                // PERFORMANCE FIX: Skip targeting checks when NPC is dormant
                if (npc.ShouldBeDormant()) return false; // Block targeting dormant NPCs
                
                if (!npc.Config.CanBeTargetedByAutoTurrets)
                {
                    return false; // Block targeting based on config
                }
            }
            return null;
        }

        // Check config options for AutoTurret targeting (CanBeTargeted hook)
        internal object CanBeTargeted(BaseCombatEntity target, AutoTurret turret)
        {
            if (turret == null || target == null) return null;
            CustomScientistNpc npc = target as CustomScientistNpc;
            if (npc != null && IsCustomScientist(npc) && npc.Config != null)
            {
                // PERFORMANCE FIX: Skip targeting checks when NPC is dormant
                if (npc.ShouldBeDormant()) return false; // Block targeting dormant NPCs
                
                if (!npc.Config.CanBeTargetedByAutoTurrets)
                {
                    return false; // Block targeting based on config
                }
            }
            return null;
        }

        // Check config options for GunTrap targeting (BasePlayer overload)
        internal object CanBeTargeted(BasePlayer target, GunTrap trap)
        {
            if (trap == null || target == null) return null;
            CustomScientistNpc npc = target as CustomScientistNpc;
            if (npc != null && IsCustomScientist(npc) && npc.Config != null)
            {
                // PERFORMANCE FIX: Skip targeting checks when NPC is dormant
                if (npc.ShouldBeDormant()) return false; // Block targeting dormant NPCs
                
                if (!npc.Config.CanBeTargetedByGunTraps)
                {
                    return false; // Block targeting based on config
                }
            }
            return null;
        }

        // Check config options for GunTrap targeting (BaseCombatEntity overload)
        internal object CanBeTargeted(BaseCombatEntity target, GunTrap trap)
        {
            if (trap == null || target == null) return null;
            CustomScientistNpc npc = target as CustomScientistNpc;
            if (npc != null && IsCustomScientist(npc) && npc.Config != null)
            {
                // PERFORMANCE FIX: Skip targeting checks when NPC is dormant
                if (npc.ShouldBeDormant()) return false; // Block targeting dormant NPCs
                
                if (!npc.Config.CanBeTargetedByGunTraps)
                {
                    return false; // Block targeting based on config
                }
            }
            return null;
        }

        // Prevent NPCs from targeting CustomScientistNpc (hook used by NpcSpawn and other plugins)
        // NOTE: Use 'target is CustomScientistNpc' not IsCustomScientist(target) - IsCustomScientist checks skinID
        // which would incorrectly block human players wearing the custom scientist skin (conflict with AbandonedBases etc)
        internal object OnCustomNpcTarget(ScientistNPC npc, BaseEntity target)
        {
            if (npc == null || target == null) return null;
            if (!(target is CustomScientistNpc targetNpc)) return null;
            // PERFORMANCE FIX: Skip targeting when target NPC is dormant
            if (targetNpc.ShouldBeDormant()) return false; // Block targeting dormant NPCs
            return false; // Block targeting CustomScientistNpc
        }

        // Prevent all NPCs (vanilla and custom) from targeting Scarecrow NPCs
        // This hook catches ALL ScientistNPC instances (both vanilla and custom)
        // No filtering by IsCustomScientist() - applies to ALL ScientistNPC
        internal object OnNpcTarget(ScientistNPC npc, BaseEntity target)
        {
            if (npc == null || target == null) return null;
            if (_config.PreventScarecrowTargeting && target is ScarecrowNPC)
            {
                // Block ALL ScientistNPC (vanilla heavy scientists, junkpile scientists, and custom NPCs)
                // from targeting scarecrows
                bool isCustom = IsCustomScientist(npc);
                ulong npcId = npc.net?.ID.Value ?? 0UL;
                float currentTime = UnityEngine.Time.realtimeSinceStartup;
                
                // Throttle debug messages (max once per 10 seconds per NPC)
                if (_config.EnableDebugLogging)
                {
                    if (!_scarecrowMessageThrottle.ContainsKey(npcId) || 
                        (currentTime - _scarecrowMessageThrottle[npcId]) >= SCARECROW_MESSAGE_THROTTLE_SECONDS)
                    {
                        Puts($"[ScarecrowProtection] Blocked {(isCustom ? "CUSTOM" : "VANILLA")} {npc.GetType().Name} (skinID: {npc.skinID}) from targeting ScarecrowNPC");
                        _scarecrowMessageThrottle[npcId] = currentTime;
                    }
                }
                return false; // Block targeting scarecrows
            }
            return null;
        }

        // Handle BaseEntity owner case (from AIBrainSenses.GetNearest)
        // This catches cases where AIBrainSenses calls the hook with owner as BaseEntity
        // Applies to ALL HumanNPC/ScientistNPC (vanilla and custom)
        internal object OnNpcTarget(BaseEntity owner, BaseEntity target)
        {
            if (owner == null || target == null) return null;
            // Only process if owner is an NPC type and target is a scarecrow
            if (!(owner is HumanNPC || owner is ScientistNPC)) return null;
            if (_config.PreventScarecrowTargeting && target is ScarecrowNPC)
            {
                // Block ALL NPCs (vanilla and custom) from targeting scarecrows
                bool isCustom = IsCustomScientist(owner);
                ulong ownerId = (owner as BaseEntity)?.net?.ID.Value ?? 0UL;
                float currentTime = UnityEngine.Time.realtimeSinceStartup;
                
                // Throttle debug messages (max once per 10 seconds per NPC)
                if (_config.EnableDebugLogging)
                {
                    if (!_scarecrowMessageThrottle.ContainsKey(ownerId) || 
                        (currentTime - _scarecrowMessageThrottle[ownerId]) >= SCARECROW_MESSAGE_THROTTLE_SECONDS)
                    {
                        Puts($"[ScarecrowProtection] Blocked {(isCustom ? "CUSTOM" : "VANILLA")} {owner.GetType().Name} (via AIBrainSenses, skinID: {(owner as BaseEntity)?.skinID ?? 0}) from targeting ScarecrowNPC");
                        _scarecrowMessageThrottle[ownerId] = currentTime;
                    }
                }
                return false; // Block targeting scarecrows
            }
            return null;
        }

        internal object OnEntityTakeDamage(BaseCombatEntity victim, HitInfo info)
        {
            if (victim == null || info == null) return null;

            BaseEntity attacker = info.Initiator;

            if (IsCustomScientist(victim))
            {
                CustomScientistNpc victimNpc = victim as CustomScientistNpc;
                if (victimNpc == null) return null;
                
                // Block all damage while frozen
                if (victimNpc.IsFrozen) return true;
                
                // PERFORMANCE FIX: Skip processing when NPC is dormant (unless being attacked)
                // Only skip if attacker is not a player (players should wake dormant NPCs)
                if (victimNpc.ShouldBeDormant() && (attacker == null || !(attacker is BasePlayer && (attacker as BasePlayer).IsPlayer())))
                {
                    return null; // Let dormant NPCs take damage but don't process targeting logic
                }

                // CRITICAL: Force wake dormant NPCs when attacked by a player (shooting should always wake them)
                if (attacker is BasePlayer playerAttacker && playerAttacker.IsPlayer())
                {
                    // Force wake the NPC immediately - set IsDormant first (BotReSpawn approach)
                    victimNpc.IsDormant = false;
                    
                    // Then wake up the brain
                    if (victimNpc.Brain != null)
                    {
                        victimNpc.Brain.sleeping = false;
                        if (victimNpc.Brain is IAISleepable sleepable)
                        {
                            sleepable.WakeAI();
                        }
                        if (victimNpc.Brain.Navigator != null)
                        {
                            victimNpc.Brain.Navigator.Resume();
                        }
                    }
                    if (victimNpc.NavAgent != null && !victimNpc.NavAgent.enabled)
                    {
                        victimNpc.NavAgent.enabled = true;
                    }
                    
                    // ASSIST SYSTEM: Wake nearby NPCs when one is attacked (like BotReSpawn)
                    // Check if assist is enabled (default to true, can be configured later)
                    float assistRange = 300f; // Default assist range (can be made configurable)
                    float quietWeaponRange = 40f; // Reduced range for quiet weapons
                    bool isQuietWeapon = info.Weapon != null && (info.Weapon is BaseMelee || info.Weapon.ShortPrefabName.Contains("bow") || info.Weapon.ShortPrefabName.Contains("crossbow"));
                    float actualAssistRange = Mathf.Min(isQuietWeapon ? quietWeaponRange : assistRange, assistRange);
                    
                    BasePlayer[] nearbyNpcs = new BasePlayer[64];
                    int nearbyCount = BaseEntity.Query.Server.GetPlayersInSphere(victimNpc.transform.position, actualAssistRange, nearbyNpcs, x => x != null && x.IsNpc && x != victimNpc);
                    
                    for (int i = 0; i < nearbyCount; i++)
                    {
                        ScientistNPC nearbyNpc = nearbyNpcs[i] as ScientistNPC;
                        if (nearbyNpc == null || !Scientists.ContainsKey(nearbyNpc.userID)) continue;
                        
                        CustomScientistNpc customNpc = Scientists[nearbyNpc.userID];
                        if (customNpc == null || customNpc.IsDestroyed || customNpc.Brain == null) continue;
                        
                        // BotReSpawn logic: Only assist if already targeting same player, or are allies, or already have this player in targets
                        bool shouldAssist = false;
                        if (customNpc.CurrentTarget == playerAttacker)
                        {
                            shouldAssist = true; // Already targeting the same player
                        }
                        else if (customNpc.CanTargetEntity(playerAttacker))
                        {
                            // Check if NPC can target this player (respects faction, safe zones, etc.)
                            shouldAssist = true;
                        }
                        
                        if (!shouldAssist) continue;
                        
                        // CRITICAL: Wake up the nearby NPC using CustomScientistNpc property
                        customNpc.IsDormant = false;
                        
                        // Force wake up the brain aggressively
                        if (customNpc.Brain != null)
                        {
                            customNpc.Brain.sleeping = false;
                            if (customNpc.Brain is IAISleepable nearbySleepable)
                            {
                                nearbySleepable.WakeAI();
                            }
                            if (customNpc.Brain.Navigator != null)
                            {
                                customNpc.Brain.Navigator.Resume();
                            }
                        }
                        if (customNpc.NavAgent != null && !customNpc.NavAgent.enabled)
                        {
                            customNpc.NavAgent.enabled = true;
                        }
                        
                        // Make nearby NPCs aware of the attacker and set them as target
                        customNpc.SetKnown(playerAttacker);
                        
                        // Set target with delay based on distance (like BotReSpawn)
                        float distance = Vector3.Distance(customNpc.transform.position, victimNpc.transform.position);
                        float delay = Mathf.Min(5f, distance / 20f);
                        timer.Once(delay, () =>
                        {
                            if (customNpc != null && !customNpc.IsDestroyed && customNpc.Brain != null && 
                                playerAttacker != null && !playerAttacker.IsDead() && customNpc.CanTargetEntity(playerAttacker))
                            {
                                customNpc.CurrentTarget = playerAttacker;
                                customNpc.Brain.Senses.Memory.SetKnown(playerAttacker, customNpc, customNpc.Brain.Senses);
                            }
                        });
                    }
                }

                if (attacker == null || attacker.skinID == 11162132011012) return true;

                // Allow turret/trap damage (with TurretDamageScale) - check config options
                if (attacker is AutoTurret)
                {
                    if (!victimNpc.Config.CanBeTargetedByAutoTurrets)
                    {
                        if (info.damageTypes != null) info.damageTypes.ScaleAll(0f);
                        return true; // Block damage if not allowed to be targeted
                    }
                    if (attacker.OwnerID.IsSteamId()) victimNpc.AddTurret(attacker as BaseCombatEntity);
                    info.damageTypes.ScaleAll(victimNpc.Config.TurretDamageScale);
                    return null; // Allow turret damage
                }
                
                if (attacker is GunTrap)
                {
                    if (!victimNpc.Config.CanBeTargetedByGunTraps)
                    {
                        if (info.damageTypes != null) info.damageTypes.ScaleAll(0f);
                        return true; // Block damage if not allowed to be targeted
                    }
                    if (attacker.OwnerID.IsSteamId()) victimNpc.AddTurret(attacker as BaseCombatEntity);
                    info.damageTypes.ScaleAll(victimNpc.Config.TurretDamageScale);
                    return null; // Allow trap damage
                }
                
                if (attacker is FlameTurret)
                {
                    if (!victimNpc.Config.CanBeTargetedByFlameTurrets)
                    {
                        if (info.damageTypes != null) info.damageTypes.ScaleAll(0f);
                        return true; // Block damage if not allowed to be targeted
                    }
                    if (attacker.OwnerID.IsSteamId()) victimNpc.AddTurret(attacker as BaseCombatEntity);
                    info.damageTypes.ScaleAll(victimNpc.Config.TurretDamageScale);
                    return null; // Allow flame turret damage
                }

                // Allow player damage
                if (attacker is BasePlayer)
                {
                    BasePlayer attackerBp = attacker as BasePlayer;

                    if (attackerBp.userID.IsSteamId())
                    {
                        victimNpc.SetKnown(attackerBp);
                        if (info.damageTypes != null && info.damageTypes.Total() > 0f)
                        {
                            if (info.isHeadshot && victimNpc.Config.InstantDeathIfHitHead)
                            {
                                info.damageTypes.ScaleAll(victimNpc.health / info.damageTypes.Total());
                                return null;
                            }
                            float zoneScale = info.boneArea switch
                            {
                                HitArea.Head => victimNpc.Config.HeadDamageScale,
                                HitArea.Chest or HitArea.Stomach or HitArea.Arm or HitArea.Hand => victimNpc.Config.BodyDamageScale,
                                HitArea.Foot or HitArea.Leg => victimNpc.Config.LegDamageScale,
                                _ => 1f
                            };
                            if (!Mathf.Approximately(zoneScale, 1f))
                                info.damageTypes.ScaleAll(zoneScale);
                        }
                        return null; // Allow player damage
                    }

                    if (attackerBp.skinID != 0 && SkinIDs.Contains(attackerBp.skinID)) return null;

                    if (attackerBp is NPCPlayer)
                    {
                        NPCPlayer attackerNpc = attackerBp as NPCPlayer;

                        if (attackerNpc is FrankensteinPet || _config.CanTargetNpc)
                        {
                            victimNpc.SetKnown(attackerNpc);
                            return null;
                        }
                    }
                }

                // Allow animal damage if configured
                if (attacker is BaseAnimalNPC)
                {
                    BaseAnimalNPC attackerAnimal = attacker as BaseAnimalNPC;
                    if (_config.CanTargetAnimal)
                    {
                        victimNpc.SetKnown(attackerAnimal);
                        return null;
                    }
                }

                // Block damage from other sources (other NPCs, etc.)
                return true;
            }

            if (IsCustomScientist(attacker))
            {
                if (victim.skinID == 11162132011012) return true;

                if (victim is BasePlayer)
                {
                    BasePlayer victimBp = victim as BasePlayer;

                    if (victimBp.userID.IsSteamId()) return null;

                    if (victimBp.skinID != 0 && SkinIDs.Contains(victimBp.skinID)) return null;

                    if (victimBp is NPCPlayer && (victimBp is FrankensteinPet || _config.CanTargetNpc)) return null;
                }

                if (victim is BaseAnimalNPC && _config.CanTargetAnimal)
                {
                    info.damageTypes.ScaleAll(4.28571f);
                    return null;
                }

                if (victim is Drone) return null;
                if (victim is Tugboat) return null;
                if (victim is SubmarineDuo) return null;
                if (victim is BaseSubmarine) return null;

                if (victim.OwnerID.IsSteamId())
                {
                    BaseEntity weaponPrefab = info.WeaponPrefab;
                    if (weaponPrefab != null && (weaponPrefab.ShortPrefabName == "rocket_basic" || weaponPrefab.ShortPrefabName == "explosive.timed.deployed"))
                    {
                        CustomScientistNpc attackerNpc = attacker as CustomScientistNpc;
                        if (attackerNpc == null) return null;
                        info.damageTypes.ScaleAll(attackerNpc.Config.DamageScale);
                        return null;
                    }
                }

                return true;
            }

            return null;
        }

        internal void OnLoseCondition(Item item, ref float amount)
        {
            if (item == null || amount == 0f) return;
            ScientistNPC npc = item.GetOwnerPlayer() as ScientistNPC;
            if (npc == null)
            {
                if (!item.info.shortname.Contains("mod")) return;
                ItemContainer container = item.GetRootContainer();
                if (container == null) return;
                npc = container.GetOwnerPlayer() as ScientistNPC;
            }
            if (IsCustomScientist(npc)) amount = 0f;
        }

        /// <summary>
        /// Public API: Set the owner of a teammate NPC. Makes the NPC friendly to the owner and their team.
        /// </summary>
        /// <param name="npc">The CustomScientistNpc instance</param>
        /// <param name="ownerUserID">The Steam ID of the owner player</param>
        /// <returns>True if successful, false otherwise</returns>
        private object SetTeammateOwner(ScientistNPC npc, ulong ownerUserID)
        {
            CustomScientistNpc customNpc = npc as CustomScientistNpc;
            if (customNpc == null && npc != null && npc.net != null)
            {
                ulong id = npc.net.ID.Value;
                if (Scientists.ContainsKey(id)) customNpc = Scientists[id];
            }
            
            if (customNpc == null || !IsCustomScientist(customNpc)) return false;
            
            SetTeammateOwner(customNpc, ownerUserID);
            return true;
        }
        #endregion Oxide Hooks

        #region True PVE
        private object CanEntityTakeDamage(BaseCombatEntity victim, HitInfo info)
        {
            if (victim == null || info == null) return null;

            BaseEntity attacker = info.Initiator;

            if (IsCustomScientist(victim))
            {
                if (attacker == null || attacker.skinID == 11162132011012) return false;

                if (attacker is AutoTurret || attacker is GunTrap || attacker is FlameTurret) return true;

                if (attacker is BasePlayer)
                {
                    BasePlayer attackerBp = attacker as BasePlayer;

                    if (attackerBp.userID.IsSteamId()) return null;

                    if (attackerBp.skinID != 0 && SkinIDs.Contains(attackerBp.skinID)) return true;

                    if (attackerBp is NPCPlayer && (attackerBp is FrankensteinPet || _config.CanTargetNpc)) return true;
                }

                if (attacker is BaseAnimalNPC && _config.CanTargetAnimal) return true;

                return false;
            }

            if (IsCustomScientist(attacker))
            {
                if (victim.skinID == 11162132011012) return false;

                if (victim is BasePlayer)
                {
                    BasePlayer victimBp = victim as BasePlayer;

                    if (victimBp.userID.IsSteamId()) return null;

                    if (victimBp.skinID != 0 && SkinIDs.Contains(victimBp.skinID)) return true;

                    if (victimBp is NPCPlayer && (victimBp is FrankensteinPet || _config.CanTargetNpc)) return true;
                }

                if (victim is BaseAnimalNPC && _config.CanTargetAnimal) return true;

                if (victim is Drone) return null;
                if (victim is Tugboat) return null;
                if (victim is SubmarineDuo) return null;
                if (victim is BaseSubmarine) return null;

                if (victim.OwnerID.IsSteamId())
                {
                    BaseEntity weaponPrefab = info.WeaponPrefab;
                    if (weaponPrefab != null && (weaponPrefab.ShortPrefabName == "rocket_basic" || weaponPrefab.ShortPrefabName == "explosive.timed.deployed")) return true;
                }

                return false;
            }

            return null;
        }
        #endregion True PVE

        #region Npc Kits
        private object OnNpcKits(CustomScientistNpc npc)
        {
            if (IsCustomScientist(npc)) return true;
            else return null;
        }
        #endregion Npc Kits

        #region Defendable Bases
        internal Vector3 GeneralPosition { get; set; } = Vector3.zero;
        internal HashSet<Vector3> WallFrames { get; } = new HashSet<Vector3>();
        internal HashSet<Vector3> CustomBarricades { get; } = new HashSet<Vector3>();

        private void SetGeneralPos(Vector3 pos) => GeneralPosition = pos;
        private void OnGeneralKill() => GeneralPosition = Vector3.zero;

        private void SetWallFramesPos(List<Vector3> positions)
        {
            foreach (Vector3 pos in positions)
                WallFrames.Add(pos);
        }

        private void OnCustomBarricadeSpawn(Vector3 pos) => CustomBarricades.Add(pos);
        private void OnCustomBarricadeKill(Vector3 pos) => CustomBarricades.Remove(pos);

        private void OnDefendableBasesEnd()
        {
            GeneralPosition = Vector3.zero;
            WallFrames.Clear();
            CustomBarricades.Clear();
        }
        #endregion Defendable Bases

        #region Gas Station Event
        internal HashSet<ulong> GasStationNpc = new HashSet<ulong>();

        internal bool IsGasStationNpc(BaseEntity entity)
        {
            if (entity.skinID != 11162132011012 || entity.net == null) return false;
            return GasStationNpc.Contains(entity.net.ID.Value);
        }

        private void OnGasStationNpcSpawn(HashSet<ulong> ids)
        {
            foreach (ulong id in ids)
                if (!GasStationNpc.Contains(id))
                    GasStationNpc.Add(id);
        }

        private void OnGasStationEventEnd()
        {
            GasStationNpc.Clear();
        }
        #endregion Gas Station Event

        

        #region Find Random Points
        private void GeneratePositions()
        {
            GenerateBiomePositions(10000);
            GenerateRoadPositions();
            GenerateRailPositions();
        }

        private Dictionary<string, List<Vector3>> BiomePoints { get; } = new Dictionary<string, List<Vector3>>
        {
            ["Arid"] = new List<Vector3>(),
            ["Temperate"] = new List<Vector3>(),
            ["Tundra"] = new List<Vector3>(),
            ["Arctic"] = new List<Vector3>(),
            ["Jungle"] = new List<Vector3>()
        };

        private const int EntityLayers = 1 << 8 | 1 << 21;
        private const int GroundLayers = 1 << 4 | 1 << 10 | 1 << 16 | 1 << 23 | 1 << 25;
        private const int BlockedTopology = (int)(TerrainTopology.Enum.Cliff | TerrainTopology.Enum.Cliffside |
                                                  TerrainTopology.Enum.Beach | TerrainTopology.Enum.Beachside |
                                                  TerrainTopology.Enum.Ocean | TerrainTopology.Enum.Oceanside |
                                                  TerrainTopology.Enum.Monument | TerrainTopology.Enum.Building |
                                                  TerrainTopology.Enum.River | TerrainTopology.Enum.Riverside |
                                                  TerrainTopology.Enum.Lake | TerrainTopology.Enum.Lakeside);

        private HashSet<string> BlacklistBiomes { get; } = new HashSet<string>();

        private void GenerateBiomePositions(int attempts)
        {
            for (int i = 0; i < attempts; i++)
            {
                Vector2 random = World.Size * 0.475f * UnityEngine.Random.insideUnitCircle;
                Vector3 position = new Vector3(random.x, 500f, random.y);

                if (!IsAvailableTopology(position)) continue;

                if (IsRaycast(position, out RaycastHit raycastHit)) position.y = raycastHit.point.y;
                else continue;

                if (IsNavMesh(position, out NavMeshHit navMeshHit)) position = navMeshHit.position;
                else continue;

                if (IsEntities(position, 6f)) continue;

                TerrainBiome.Enum majorityBiome = (TerrainBiome.Enum)TerrainMeta.BiomeMap.GetBiomeMaxType(position);

                BiomePoints[majorityBiome.ToString()].Add(position);
            }

            BlacklistBiomes.Clear();
            foreach (KeyValuePair<string, List<Vector3>> kvp in BiomePoints) if (kvp.Value.Count == 0) BlacklistBiomes.Add(kvp.Key);

            DebugLog($"List of biome positions: Arid = {BiomePoints["Arid"].Count}, Temperate = {BiomePoints["Temperate"].Count}, Tundra = {BiomePoints["Tundra"].Count}, Arctic = {BiomePoints["Arctic"].Count}, Jungle = {BiomePoints["Jungle"].Count}");
        }

        public object GetSpawnPoint(string biome)
        {
            if (!BiomePoints.TryGetValue(biome, out List<Vector3> positions)) return null;

            if (positions.Count < 100 && !BlacklistBiomes.Contains(biome)) GenerateBiomePositions(1000);

            int attempts = 100;
            while (attempts > 0)
            {
                attempts--;
                if (positions.Count == 0) continue;

                Vector3 position = positions.GetRandom();

                if (IsEntities(position, 6f))
                {
                    positions.Remove(position);
                    if (positions.Count < 100 && !BlacklistBiomes.Contains(biome)) GenerateBiomePositions(1000);
                    continue;
                }

                return position;
            }

            return null;
        }

        private static bool IsAvailableTopology(Vector3 position) => (TerrainMeta.TopologyMap.GetTopology(position) & BlockedTopology) == 0;

        private static bool IsRaycast(Vector3 position, out RaycastHit raycastHit) => Physics.Raycast(position, Vector3.down, out raycastHit, 500f, GroundLayers);

        private static bool IsNavMesh(Vector3 position, out NavMeshHit navMeshHit) => NavMesh.SamplePosition(position, out navMeshHit, 2f, NavMesh.AllAreas);

        private static bool IsEntities(Vector3 position, float radius)
        {
            List<BaseEntity> list = Pool.Get<List<BaseEntity>>();
            Vis.Entities(position, radius, list, EntityLayers);
            bool hasEntity = list.Count > 0;
            Pool.FreeUnmanaged(ref list);
            return hasEntity;
        }

        private Dictionary<string, List<Vector3>> RoadPoints { get; } = new Dictionary<string, List<Vector3>>
        {
            ["ExtraWide"] = new List<Vector3>(),
            ["Standard"] = new List<Vector3>(),
            ["ExtraNarrow"] = new List<Vector3>()
        };

        private void GenerateRoadPositions()
        {
            foreach (PathList path in TerrainMeta.Path.Roads)
            {
                string name = path.Width < 5f ? "ExtraNarrow" : path.Width > 10 ? "ExtraWide" : "Standard";
                foreach (Vector3 vector3 in path.Path.Points) RoadPoints[name].Add(vector3);
            }
            DebugLog($"List of road positions: ExtraWide = {RoadPoints["ExtraWide"].Count}, Standard = {RoadPoints["Standard"].Count}, ExtraNarrow = {RoadPoints["ExtraNarrow"].Count}");
        }

        public object GetRoadSpawnPoint(string road)
        {
            if (!RoadPoints.ContainsKey(road)) return null;
            List<Vector3> positions = RoadPoints[road];
            if (positions.Count == 0) return null;
            return positions.GetRandom();
        }

        private List<Vector3> RailPositions { get; } = new List<Vector3>();

        private void GenerateRailPositions()
        {
            foreach (PathList path in TerrainMeta.Path.Rails)
                foreach (Vector3 vector3 in path.Path.Points)
                    RailPositions.Add(vector3);
            DebugLog($"{RailPositions.Count} railway positions found");
        }

        private object GetRailSpawnPoint()
        {
            if (RailPositions.Count == 0) return null;
            return RailPositions.GetRandom();
        }
        #endregion Find Random Points

        #region Enhanced Navmesh Spawn Point
        // Enhanced position validation with retry logic, water checking, and collider validation
        private static class EnhancedNavmeshSpawnPoint
        {
            private static readonly Collider[] _colliderBuffer = new Collider[256];
            private static readonly string[] _blacklistedPrefabs = new string[]
            {
                "assets/bundled/prefabs/radtown/",
                "assets/prefabs/radtown/",
                "assets/prefabs/static/",
                "assets/prefabs/autospawn/",
                "monument"
            };
            // Note: Removed "assets/prefabs/structures/" and "assets/prefabs/building/" from blacklist
            // to allow dungeon/building spawns

            /// <param name="allowHighAltitudeOffNavMesh">
            /// When true, if Y &gt; 100m and SamplePosition fails, may still return the requested position (dungeon/BaseNav-style spawns).
            /// When false (default), every success path must come from a real NavMesh hit — required for API spawns (GrimmBoss, TrustPosition) on tall monuments.
            /// </param>
            public static bool Find(Vector3 targetPosition, float maxDistance, out Vector3 position, int areaMask = 25, bool allowHighAltitudeOffNavMesh = false)
            {
                // Check if position is at building height (likely a dungeon/building spawn)
                bool isBuildingHeight = targetPosition.y > 100f;
                
                int attempts = 0;
                // Enhanced: Increased attempts from 10 to 40 for better spawn point finding (from BotReSpawn improvements)
                const int maxAttempts = 40;
                
                while (attempts < maxAttempts)
                {
                    // First attempt uses exact position, subsequent attempts use random offset
                    if (attempts == 0)
                    {
                        position = targetPosition;
                    }
                    else
                    {
                        Vector2 randomCircle = UnityEngine.Random.insideUnitCircle;
                        // Use a random radius within the search ring instead of always max distance
                        float radius = UnityEngine.Random.Range(maxDistance * 0.25f, maxDistance);
                        position = targetPosition + new Vector3(randomCircle.x, 0f, randomCircle.y).normalized * radius;
                    }
                    
                    // Use provided areaMask (default 25 = ground + construction + buildings) for building navigation
                    NavMeshHit hit;
                    bool navMeshFound = NavMesh.SamplePosition(position, out hit, maxDistance, areaMask);
                    
                    // For building spawns, be more lenient - navmesh might not exist at building height
                    // BaseNavigator will handle building navigation anyway
                    if (!navMeshFound)
                    {
                        if (allowHighAltitudeOffNavMesh)
                        {
                            // If building height, trust the provided position and skip navmesh validation
                            // NPCs will use BaseNav for building navigation which doesn't require navmesh
                            if (isBuildingHeight && attempts == 0)
                            {
                                float buildingWaterLevel = WaterLevel.GetWaterSurface(position, waves: false, volumes: true);
                                if (position.y >= buildingWaterLevel)
                                    return true;
                            }

                            // Try with ground-only areaMask as fallback
                            if (isBuildingHeight && attempts < 2 && areaMask != 1)
                            {
                                if (NavMesh.SamplePosition(position, out hit, maxDistance, 1))
                                {
                                    position = hit.position;
                                    return true;
                                }
                            }
                        }

                        attempts++;
                        continue;
                    }
                    
                    position = hit.position;
                    
                    // Check water level
                    float waterLevel = WaterLevel.GetWaterSurface(position, waves: false, volumes: true);
                    if (position.y < waterLevel)
                    {
                        attempts++;
                        continue;
                    }
                    
                    // Previously we rejected positions near world colliders (e.g., monuments) for ground-level spawns.
                    // This prevented bosses from spawning at monument positions provided by other plugins (e.g., GrimmBoss).
                    // Accept any navmesh + non-underwater position here; caller decides suitability of the target area.
                    return true;
                }

                // Final fallback: accept the requested position without a nav hit (dungeon / BaseNav callers only)
                if (allowHighAltitudeOffNavMesh)
                {
                    float finalWater = WaterLevel.GetWaterSurface(targetPosition, waves: false, volumes: true);
                    if (targetPosition.y >= finalWater)
                    {
                        position = targetPosition;
                        return true;
                    }
                }
                // Position is underwater (e.g. convoy near shore): try one more pass with larger radius to find dry land
                const float rescueRadius = 150f;
                for (int i = 0; i < 15; i++)
                {
                    Vector2 r = UnityEngine.Random.insideUnitCircle.normalized;
                    float d = UnityEngine.Random.Range(rescueRadius * 0.3f, rescueRadius);
                    Vector3 tryPos = targetPosition + new Vector3(r.x, 0f, r.y) * d;
                    if (NavMesh.SamplePosition(tryPos, out NavMeshHit rescueHit, rescueRadius, areaMask))
                    {
                        float w = WaterLevel.GetWaterSurface(rescueHit.position, waves: false, volumes: true);
                        if (rescueHit.position.y >= w)
                        {
                            position = rescueHit.position;
                            return true;
                        }
                    }
                }
                position = default(Vector3);
                return false;
            }

            private static bool IsNearWorldCollider(Vector3 position)
            {
                Physics.queriesHitBackfaces = true;
                RaycastHit hit;
                if (Physics.Raycast(position, Vector3.up, out hit, 20f, 65536, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider?.gameObject?.name != null)
                    {
                        string name = hit.collider.gameObject.name;
                        foreach (string blacklist in _blacklistedPrefabs)
                        {
                            if (name.Contains(blacklist, StringComparison.OrdinalIgnoreCase))
                            {
                                Physics.queriesHitBackfaces = false;
                                return true;
                            }
                        }
                    }
                }
                Physics.queriesHitBackfaces = false;
                return false;
            }

            private static bool IsNearBlockingEntity(Vector3 position)
            {
                int count = Physics.OverlapSphereNonAlloc(position, 2f, _colliderBuffer, 65536, QueryTriggerInteraction.Ignore);
                if (count == 0) return false;
                
                // Filter out world colliders that are blacklisted
                int nonWorldColliders = 0;
                for (int i = 0; i < count; i++)
                {
                    if (_colliderBuffer[i]?.gameObject?.name != null)
                    {
                        string name = _colliderBuffer[i].gameObject.name;
                        bool isBlacklisted = false;
                        foreach (string blacklist in _blacklistedPrefabs)
                        {
                            if (name.Contains(blacklist, StringComparison.OrdinalIgnoreCase))
                            {
                                isBlacklisted = true;
                                break;
                            }
                        }
                        if (!isBlacklisted) nonWorldColliders++;
                    }
                }
                
                return nonWorldColliders > 0;
            }
        }
        #endregion Enhanced Navmesh Spawn Point

        #region Weapon and belt metadata
        private Dictionary<string, HashSet<string>> AllowedAmmoForWeapons { get; } = new Dictionary<string, HashSet<string>>
        {
            ["rifle.ak.diver"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["rifle.ak"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["blowpipe"] = new HashSet<string>
            {
                "dart.wood",
                "dart.incapacitate",
                "dart.radiation",
                "dart.scatter"
            },
            ["blunderbuss"] = new HashSet<string>
            {
                "ammo.handmade.shell",
                "ammo.shotgun",
                "ammo.shotgun.fire",
                "ammo.shotgun.slug"
            },
            ["rifle.bolt"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["bow.compound"] = new HashSet<string>
            {
                "arrow.wooden",
                "arrow.bone",
                "arrow.fire",
                "arrow.hv"
            },
            ["crossbow"] = new HashSet<string>
            {
                "arrow.wooden",
                "arrow.bone",
                "arrow.fire",
                "arrow.hv"
            },
            ["smg.2"] = new HashSet<string>
            {
                "ammo.pistol",
                "ammo.pistol.hv",
                "ammo.pistol.fire"
            },
            ["shotgun.double"] = new HashSet<string>
            {
                "ammo.handmade.shell",
                "ammo.shotgun",
                "ammo.shotgun.fire",
                "ammo.shotgun.slug"
            },
            ["rocket.launcher.dragon"] = new HashSet<string>
            {
                "ammo.rocket.basic",
                "ammo.rocket.hv",
                "ammo.rocket.fire"
            },
            ["pistol.eoka"] = new HashSet<string>
            {
                "ammo.handmade.shell",
                "ammo.shotgun",
                "ammo.shotgun.fire",
                "ammo.shotgun.slug"
            },
            ["t1_smg"] = new HashSet<string>
            {
                "ammo.pistol",
                "ammo.pistol.hv",
                "ammo.pistol.fire"
            },
            ["revolver.hc"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["hmlmg"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["bow.hunting"] = new HashSet<string>
            {
                "arrow.wooden",
                "arrow.bone",
                "arrow.fire",
                "arrow.hv"
            },
            ["rifle.ak.ice"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["rifle.l96"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["legacy bow"] = new HashSet<string>
            {
                "arrow.wooden",
                "arrow.bone",
                "arrow.fire",
                "arrow.hv"
            },
            ["rifle.lr300"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["lmg.m249"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["rifle.m39"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["shotgun.m4"] = new HashSet<string>
            {
                "ammo.shotgun",
                "ammo.handmade.shell",
                "ammo.shotgun.fire",
                "ammo.shotgun.slug"
            },
            ["pistol.m92"] = new HashSet<string>
            {
                "ammo.pistol",
                "ammo.pistol.hv",
                "ammo.pistol.fire"
            },
            ["rifle.ak.med"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["minicrossbow"] = new HashSet<string>
            {
                "arrow.wooden",
                "arrow.bone",
                "arrow.fire",
                "arrow.hv"
            },
            ["minigun"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["smg.mp5"] = new HashSet<string>
            {
                "ammo.pistol",
                "ammo.pistol.hv",
                "ammo.pistol.fire"
            },
            ["multiplegrenadelauncher"] = new HashSet<string>
            {
                "ammo.grenadelauncher.he",
                "ammo.grenadelauncher.smoke"
            },
            ["pistol.prototype17"] = new HashSet<string>
            {
                "ammo.pistol",
                "ammo.pistol.hv",
                "ammo.pistol.fire"
            },
            ["shotgun.pump"] = new HashSet<string>
            {
                "ammo.shotgun",
                "ammo.handmade.shell",
                "ammo.shotgun.fire",
                "ammo.shotgun.slug"
            },
            ["pistol.python"] = new HashSet<string>
            {
                "ammo.pistol",
                "ammo.pistol.hv",
                "ammo.pistol.fire"
            },
            ["pistol.revolver"] = new HashSet<string>
            {
                "ammo.pistol",
                "ammo.pistol.hv",
                "ammo.pistol.fire"
            },
            ["pistol.semiauto"] = new HashSet<string>
            {
                "ammo.pistol",
                "ammo.pistol.hv",
                "ammo.pistol.fire"
            },
            ["rifle.semiauto"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["rifle.sks"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["shotgun.spas12"] = new HashSet<string>
            {
                "ammo.shotgun",
                "ammo.handmade.shell",
                "ammo.shotgun.fire",
                "ammo.shotgun.slug"
            },
            ["smg.thompson"] = new HashSet<string>
            {
                "ammo.pistol",
                "ammo.pistol.hv",
                "ammo.pistol.fire"
            },
            ["shotgun.waterpipe"] = new HashSet<string>
            {
                "ammo.handmade.shell",
                "ammo.shotgun",
                "ammo.shotgun.fire",
                "ammo.shotgun.slug"
            },
            ["krieg.shotgun"] = new HashSet<string>
            {
                "ammo.shotgun",
                "ammo.handmade.shell",
                "ammo.shotgun.fire",
                "ammo.shotgun.slug"
            },
            ["rifle.lr300.space"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            }
        };
        private Dictionary<string, int> MaxAmountMods { get; } = new Dictionary<string, int>
        {
            ["rifle.l96"] = 3,
            ["rifle.m39"] = 3,
            ["shotgun.pump"] = 2,
            ["krieg.shotgun"] = 2
        };
        private Dictionary<string, HashSet<HashSet<string>>> AllowedModsForWeapons { get; } = new Dictionary<string, HashSet<HashSet<string>>>
        {
            ["rifle.ak.diver"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                }
            },
            ["rifle.ak"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                }
            },
            ["blunderbuss"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                }
            },
            ["rifle.bolt"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                }
            },
            ["crossbow"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                }
            },
            ["smg.2"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                },
                new HashSet<string>
                {
                    "weapon.mod.gascompressionovedrive"
                }
            },
            ["shotgun.double"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                }
            },
            ["t1_smg"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                },
                new HashSet<string>
                {
                    "weapon.mod.gascompressionovedrive"
                }
            },
            ["revolver.hc"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                }
            },
            ["hmlmg"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                }
            },
            ["rifle.ak.ice"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                }
            },
            ["rifle.l96"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                }
            },
            ["rifle.lr300"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                }
            },
            ["lmg.m249"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                }
            },
            ["rifle.m39"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                },
                new HashSet<string>
                {
                    "weapon.mod.gascompressionovedrive"
                }
            },
            ["shotgun.m4"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                }
            },
            ["pistol.m92"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                },
                new HashSet<string>
                {
                    "weapon.mod.gascompressionovedrive"
                },
                new HashSet<string>
                {
                    "weapon.mod.burstmodule"
                }
            },
            ["rifle.ak.med"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                }
            },
            ["minicrossbow"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                }
            },
            ["smg.mp5"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                }
            },
            ["multiplegrenadelauncher"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                }
            },
            ["pistol.prototype17"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                }
            },
            ["shotgun.pump"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                }
            },
            ["pistol.python"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                }
            },
            ["pistol.revolver"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                }
            },
            ["pistol.semiauto"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                },
                new HashSet<string>
                {
                    "weapon.mod.gascompressionovedrive"
                },
                new HashSet<string>
                {
                    "weapon.mod.burstmodule"
                }
            },
            ["rifle.semiauto"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                },
                new HashSet<string>
                {
                    "weapon.mod.gascompressionovedrive"
                },
                new HashSet<string>
                {
                    "weapon.mod.burstmodule"
                }
            },
            ["rifle.sks"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                },
                new HashSet<string>
                {
                    "weapon.mod.gascompressionovedrive"
                },
                new HashSet<string>
                {
                    "weapon.mod.burstmodule"
                }
            },
            ["shotgun.spas12"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                }
            },
            ["smg.thompson"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                },
                new HashSet<string>
                {
                    "weapon.mod.gascompressionovedrive"
                }
            },
            ["shotgun.waterpipe"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                }
            },
            ["krieg.shotgun"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                }
            },
            ["rifle.lr300.space"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                }
            }
        };
        private HashSet<string> GetMods(NpcBelt belt)
        {
            HashSet<string> result = new HashSet<string>();
            if (belt == null || string.IsNullOrEmpty(belt.ShortName)) return result;
            int modCount = belt.Mods?.Count ?? 0;
            if (MaxAmountMods.TryGetValue(belt.ShortName, out int maxValue) && modCount >= maxValue) return result;
            if (!AllowedModsForWeapons.TryGetValue(belt.ShortName, out var modGroups) || modGroups == null) return result;
            foreach (HashSet<string> hashSet in modGroups)
            {
                bool modConflict = false;
                if (belt.Mods != null)
                {
                    foreach (string m in belt.Mods)
                    {
                        if (hashSet.Contains(m)) { modConflict = true; break; }
                    }
                }
                if (modConflict) continue;
                foreach (string shortName in hashSet) result.Add(shortName);
            }
            return result;
        }
        public bool IsAmountItem(string shortname)
        {
            if (Traps.Contains(shortname)) return true;
            if (AttackingGrenades.Contains(shortname)) return true;
            if (shortname == ShortnameFlare || shortname == ShortnameSmokeGrenade || shortname == ShortnameC4 || shortname == ShortnameBarricadeCover) return true;
            if (HealingItems.ContainsKey(shortname)) return true;
            return false;
        }

        private HashSet<string> Barricades { get; } = new HashSet<string>
        {
            "barricade.cover.wood_double",
            "barricade.sandbags",
            "barricade.concrete",
            "barricade.stone",
            "barricade.medieval",
            "barricade.metal",
            "barricade.woodwire",
            "barricade.wood",
            "icewall"
        };

        private string ShortnameFlare { get; } = "flare";
        private string ShortnameSmokeGrenade { get; } = "grenade.smoke";
        private HashSet<string> AttackingGrenades { get; } = new HashSet<string>
        {
            "grenade.beancan",
            "grenade.bee",
            "grenade.f1",
            "grenade.flashbang",
            "grenade.molotov"
        };

        private HashSet<string> Traps { get; } = new HashSet<string>()
        {
            "trap.landmine",
            "trap.bear"
        };

        private string ShortnameBarricadeCover { get; } = "barricade.wood.cover";

        private string ShortnameC4 { get; } = "explosive.timed";
        private HashSet<string> RocketLaunchers { get; } = new HashSet<string>
        {
            "rocket.launcher",
            "rocket.launcher.dragon",
            "rocket.launcher.rpg7"
        };

        private Dictionary<string, float> HealingItems { get; } = new Dictionary<string, float>
        {
            ["syringe.medical"] = 3f,
            ["bandage"] = 6f
        };
        #endregion Weapon and belt metadata
        #region Helpers
        private OxideCompat.PluginRef Kits;
        private OxideCompat.PluginRef Friends;
        private OxideCompat.PluginRef Clans;

        private Dictionary<ulong, CustomScientistNpc> Scientists { get; } = new Dictionary<ulong, CustomScientistNpc>();

        // Public API: Freeze/Unfreeze any custom NPC by instance or userID
        private void FreezeNpc(ScientistNPC npc, float duration = 0f)
        {
            CustomScientistNpc custom = npc as CustomScientistNpc;
            if (custom == null && npc != null && npc.net != null)
            {
                ulong id = npc.net.ID.Value;
                if (Scientists.ContainsKey(id)) custom = Scientists[id];
            }
            if (custom != null) custom.Freeze(duration);
        }

        private void UnfreezeNpc(ScientistNPC npc)
        {
            CustomScientistNpc custom = npc as CustomScientistNpc;
            if (custom == null && npc != null && npc.net != null)
            {
                ulong id = npc.net.ID.Value;
                if (Scientists.ContainsKey(id)) custom = Scientists[id];
            }
            if (custom != null) custom.Unfreeze();
        }

        private static void CreateAllFolders()
        {
            string url = OxideCompat.DataDirectory + "/NpcSpawn/";
            if (!Directory.Exists(url)) Directory.CreateDirectory(url);
            if (!Directory.Exists(url + "Preset/")) Directory.CreateDirectory(url + "Preset/");
        }

        internal HashSet<ulong> SkinIDs = new HashSet<ulong>
        {
            14922524,
            19395142091920,
            8151920175
        };

        private void SeedUsedNpcUserIds()
        {
            // Include all custom scientists tracked by this plugin
            foreach (KeyValuePair<ulong, CustomScientistNpc> kv in Scientists)
            {
                if (kv.Value != null) UsedNpcUserIds.Add(kv.Value.userID);
            }

            // Best-effort: include any existing ScientistNPC entities on the server
            var enumerator = BaseNetworkable.serverEntities.GetEnumerator();
            try
            {
                while (enumerator.MoveNext())
                {
                    BaseNetworkable entity = enumerator.Current;
                    ScientistNPC scientist = entity as ScientistNPC;
                    if (scientist != null) UsedNpcUserIds.Add(scientist.userID);
                }
            }
            finally
            {
                enumerator.Dispose();
            }
        }
        #endregion Helpers

		#region Commands
		internal void CmdNpcCount(BasePlayer player, string command, string[] args)
		{
			if (player == null || !player.IsAdmin) return;
			GetNpcCounts(out int customScientists, out int totalScientists, out int otherScientists, out int animals, out int otherNpcPlayers, out int totalNpcs);
			SendReply(player, $"NPCs -> CustomScientists: {customScientists}, OtherScientists: {otherScientists}, TotalScientists: {totalScientists}, Animals: {animals}, OtherNPCPlayers: {otherNpcPlayers}, TotalNPCs: {totalNpcs}");
		}

		internal void ConNpcCount(ConsoleSystem.Arg arg)
		{
			if (arg == null) return;
			// Allow server console; restrict in-game to admins
			BasePlayer ply = arg.Player(); if (ply != null && !ply.IsAdmin) return;
			GetNpcCounts(out int customScientists, out int totalScientists, out int otherScientists, out int animals, out int otherNpcPlayers, out int totalNpcs);
			string msg = $"NPCs -> CustomScientists: {customScientists}, OtherScientists: {otherScientists}, TotalScientists: {totalScientists}, Animals: {animals}, OtherNPCPlayers: {otherNpcPlayers}, TotalNPCs: {totalNpcs}";
			if (ply != null) SendReply(ply, msg); else Puts(msg);
		}

		internal void CmdNpcDiag(BasePlayer player, string command, string[] args)
		{
			if (player == null || !player.IsAdmin) return;
			PrintNpcDiagnostics(player.transform.position);
			SendReply(player, "NPC diagnostics printed to server console");
		}

		internal void ConNpcDiag(ConsoleSystem.Arg arg)
		{
			if (arg == null) return;
			BasePlayer ply = arg.Player();
			if (ply != null && !ply.IsAdmin) return;
			
			Vector3 position = ply != null ? ply.transform.position : Vector3.zero;
			if (position == Vector3.zero && arg.Args != null && arg.Args.Length >= 3)
			{
				// Allow manual position input: npcdiag x y z
				if (float.TryParse(arg.GetString(0), out float x) && float.TryParse(arg.GetString(1), out float y) && float.TryParse(arg.GetString(2), out float z))
				{
					position = new Vector3(x, y, z);
				}
			}
			
			if (position == Vector3.zero)
			{
				Puts("Usage: npcdiag (from in-game) or npcdiag x y z (from console)");
				return;
			}
			
			PrintNpcDiagnostics(position);
		}

		private void PrintNpcDiagnostics(Vector3 position)
		{
			// Find nearest custom NPCs
			List<CustomScientistNpc> nearbyNpcs = new List<CustomScientistNpc>();
			foreach (var npc in Scientists.Values)
			{
				if (npc != null && npc.IsExists() && Vector3.Distance(position, npc.transform.position) < 100f)
				{
					nearbyNpcs.Add(npc);
				}
			}
			
			if (nearbyNpcs.Count == 0)
			{
				Puts("No custom NPCs found within 100m of position");
				return;
			}
			
			// Sort by distance
			nearbyNpcs.Sort((a, b) => Vector3.Distance(position, a.transform.position).CompareTo(Vector3.Distance(position, b.transform.position)));
			
			Puts("=== NPC Diagnostics (showing up to 10 nearest) ===");
			int count = 0;
			foreach (var npc in nearbyNpcs)
			{
				if (count++ >= 10) break;
				
				float dist = Vector3.Distance(position, npc.transform.position);
				bool isDormant = npc.ShouldBeDormant();
				bool hasNavAgent = npc.NavAgent != null && npc.NavAgent.enabled;
				bool hasBrain = npc.Brain != null;
				bool brainSleeping = hasBrain && npc.Brain.sleeping;
				bool hasSenses = hasBrain && npc.Brain.Senses != null;
				bool hasMemory = hasSenses && npc.Brain.Senses.Memory != null;
				int memoryCount = hasMemory ? npc.Brain.Senses.Memory.All?.Count ?? 0 : 0;
				bool hasTarget = npc.CurrentTarget != null;
				string targetType = hasTarget ? npc.CurrentTarget.GetType().Name : "None";
				bool canSee = hasTarget && npc.CanSeeTarget(npc.CurrentTarget);
				string state = hasBrain && npc.Brain.CurrentState != null ? npc.Brain.CurrentState.StateType.ToString() : "Unknown";
				bool isFrozen = npc.IsFrozen;
				float wakeupRange = 0f;
				if (_config.ForceRespectAiDormant && AiManager.ai_dormant)
				{
					float serverWakeupRange = AiManager.ai_to_player_distance_wakeup_range;
					float configSleepDistance = npc.Config.CanSleep ? npc.Config.SleepDistance : 0f;
					float defaultSleepDistance = _config.DefaultSleepDistance;
					wakeupRange = Mathf.Max(serverWakeupRange, Mathf.Max(configSleepDistance, defaultSleepDistance));
				}
				// Use BotReSpawn approach: GetPlayersInSphere (not Fast) with simpler filter
				BasePlayer[] localPlayerResults = new BasePlayer[64];
				int nearbyPlayerCount = BaseEntity.Query.Server.GetPlayersInSphere(npc.transform.position, wakeupRange > 0 ? wakeupRange : 100f, localPlayerResults, x => x != null && x.net?.connection != null && x.userID.IsSteamId() && !x.IsNpc && !x.IsSleeping());
				
				Puts($"{npc.displayName} (dist: {dist:F1}m, pos: {npc.transform.position})");
				Puts($"  Dormant: {isDormant}, Frozen: {isFrozen}, NavAgent: {hasNavAgent}, BrainSleeping: {brainSleeping}");
				Puts($"  State: {state}, Target: {targetType}, CanSee: {canSee}");
				Puts($"  Memory: {memoryCount} entities, Senses: {hasSenses}, NearbyPlayers: {nearbyPlayerCount}");
				if (wakeupRange > 0) Puts($"  WakeupRange: {wakeupRange:F1}m");
			}
		}

		private void GetNpcCounts(out int customScientists, out int totalScientists, out int otherScientists, out int animals, out int otherNpcPlayers, out int totalNpcs)
		{
			// Count by scanning live entities so results survive plugin reloads and other swaps
			customScientists = 0;
			int scientists = 0;
			animals = 0;
			otherNpcPlayers = 0;
			var enumerator = BaseNetworkable.serverEntities.GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					BaseNetworkable ent = enumerator.Current;
					ScientistNPC sci = ent as ScientistNPC;
					if (sci != null)
					{
						scientists++;
						if (IsCustomScientist(sci)) customScientists++;
						continue;
					}
					if (ent is BaseAnimalNPC) { animals++; continue; }
					var npcPlayer = ent as NPCPlayer; if (npcPlayer != null) { otherNpcPlayers++; continue; }
				}
			}
			finally { enumerator.Dispose(); }
			totalScientists = scientists;
			otherScientists = Mathf.Max(0, totalScientists - customScientists);
			// Exclude ScientistNPCs from generic NPCPlayer count to avoid double-counting
			// otherNpcPlayers as counted above excludes ScientistNPC (handled by earlier branch)
			totalNpcs = totalScientists + animals + otherNpcPlayers;
		}

        /// <summary>
        /// Optional Harmony swim patches (same approach as SkillTree <see cref="AutoPatchAttribute"/>).
        /// When <c>GrimmNPC.dll</c> is present, these prefixes no-op so BaseNavigator is not double-patched.
        /// Scope: <see cref="CustomScientistNpc"/> with <see cref="NpcConfig.CanSwim"/> only (not <c>Oxide.Ext.ChaosNPC</c> types — see <c>.cursor/Extensions/Oxide.Ext.ChaosNPC/PerformanceSuggestions.md</c>).
        /// Prefixes check entity type before the Grimm guard so unrelated navigators skip extra work.
        /// </summary>
        private static class NpcSpawnSwimHarmonyGuard
        {
            /// <summary>
            /// Formerly deferred swim Harmony to a separate GrimmNPC mod. This assembly <b>is</b> GrimmNPC, so never defer.
            /// </summary>
            internal static bool DeferToGrimmNpcMod() => false;
        }

        private static class NpcSpawnOpenWaterSwim
        {
            private const float OpenWaterMinWaterPlane = -80f;
            private const float OpenWaterMinSubmergeDepth = 0.65f;
            /// <summary>BasePlayer.IsSwimming / ChaosNPC modelState threshold.</summary>
            private const float MinImmersionForSwim = 0.65f;

            internal static float SafeWaterFactor(ScientistNPC npc)
            {
                if (npc?.playerCollider == null)
                    return 0f;

                try
                {
                    return npc.WaterFactor();
                }
                catch (NullReferenceException)
                {
                    return 0f;
                }
            }

            internal static bool SafeIsSwimming(ScientistNPC npc)
            {
                if (npc?.playerCollider == null)
                    return false;

                try
                {
                    return npc.IsSwimming();
                }
                catch (NullReferenceException)
                {
                    return false;
                }
            }

            /// <summary>
            /// True only when the NPC is actually immersed in a swimable water body.
            /// Geometric ocean-plane checks alone false-positive on dry inland terrain below sea level
            /// (quarries / valleys) — that disables NavMesh and plays the swim anim on land.
            /// </summary>
            internal static bool TryEvaluate(ScientistNPC npc)
            {
                if (npc == null || TerrainMeta.WaterMap == null)
                    return false;

                // Hard gate: must be immersed via WaterFactor. Do not trust modelState.waterLevel here —
                // older builds forced it to 0.85 and that stuck zombies in "swim" on dry land.
                float wf = SafeWaterFactor(npc);
                if (wf < MinImmersionForSwim)
                    return false;

                Vector3 p = npc.ServerPosition;
                float waterPlane = WaterLevel.GetWaterLevel(p, waves: true);
                float terrainH = 0f;
                if (TerrainMeta.HeightMap != null && TerrainMeta.HeightMap.isInitialized)
                    terrainH = TerrainMeta.HeightMap.GetHeight(p);

                if (waterPlane < OpenWaterMinWaterPlane)
                    return false;

                // Dry ground: water surface at/near terrain (and no deep volume) => not swimming.
                WaterLevel.WaterInfo wi = WaterLevel.GetWaterInfo(p, waves: true, volumes: true, npc);
                float volumeDepth = wi.isValid ? wi.currentDepth : 0f;
                float planeAboveTerrain = waterPlane - terrainH;
                if (planeAboveTerrain < OpenWaterMinSubmergeDepth && volumeDepth < OpenWaterMinSubmergeDepth)
                    return false;

                // Standing on walkable ground with only a shallow puddle / ocean-column phantom.
                float feetAboveTerrain = p.y - terrainH;
                if (feetAboveTerrain >= -0.2f && feetAboveTerrain <= 1.35f
                    && volumeDepth < OpenWaterMinSubmergeDepth
                    && planeAboveTerrain < 1.45f)
                    return false;

                const float subsurfaceM = 0.45f;
                if (p.y > waterPlane - subsurfaceM && volumeDepth < OpenWaterMinSubmergeDepth)
                    return false;

                if (p.y < terrainH - 25f)
                    return false;

                if (wi.isValid)
                {
                    if (wi.currentDepth < OpenWaterMinSubmergeDepth && planeAboveTerrain < OpenWaterMinSubmergeDepth)
                        return false;
                }
                else if (waterPlane - p.y < OpenWaterMinSubmergeDepth)
                {
                    return false;
                }

                return true;
            }

            /// <summary>Clear sticky swim model state forced by older builds so clients stop playing swim on land.
            /// Safe to call often: no-ops when waterLevel already dry.</summary>
            internal static void ClearStickySwimModelState(ScientistNPC npc)
            {
                if (npc?.modelState == null)
                    return;
                if (npc.modelState.waterLevel < 0.05f)
                    return;
                // Only clear when we are sure we are not in swimable water (avoid fighting real immersion).
                float wf = SafeWaterFactor(npc);
                if (wf >= MinImmersionForSwim)
                    return;

                npc.modelState.waterLevel = 0f;
                try
                {
                    NpcSpawnSwimMovementHelper.PushModelState(npc);
                }
                catch
                {
                    try { npc.SendModelState(force: true); } catch { }
                }
            }
        }

        private static class NpcSpawnSwimNavGate
        {
            private static readonly MethodInfo CanUpdateMovementMethod =
                AccessTools.Method(typeof(BaseNavigator), "CanUpdateMovement");
            private static readonly FieldInfo LastSetDestinationTimeField =
                typeof(BaseNavigator).GetField("lastSetDestinationTime", BindingFlags.NonPublic | BindingFlags.Instance);
            private static readonly FieldInfo PausedField =
                typeof(BaseNavigator).GetField("paused", BindingFlags.NonPublic | BindingFlags.Instance);
            internal static readonly FieldInfo CurrentSpeedFractionField =
                typeof(BaseNavigator).GetField("currentSpeedFraction", BindingFlags.NonPublic | BindingFlags.Instance);

            internal static bool ShouldBlockNavApi(BaseNavigator nav)
            {
                if (nav == null)
                    return false;
                if (!(nav.BaseEntity is CustomScientistNpc npc) || npc.Config == null || !npc.Config.CanSwim)
                    return false;
                                return nav.IsSwimming();
            }

            internal static bool StockCanUpdateMovement(BaseNavigator nav)
            {
                if (CanUpdateMovementMethod == null || nav == null)
                    return true;
                try
                {
                    object r = CanUpdateMovementMethod.Invoke(nav, null);
                    return r is bool b && b;
                }
                catch
                {
                    return true;
                }
            }

            internal static bool TryRecordSwimDestination(
                BaseNavigator __instance,
                ref Vector3 pos,
                float speedFraction,
                float updateInterval,
                ref bool __result)
            {
                if (!ConVar.AI.move)
                {
                    __result = false;
                    return false;
                }

                if (!ConVar.AI.navthink)
                {
                    __result = false;
                    return false;
                }

                if (updateInterval > 0f && !__instance.UpdateIntervalElapsed(updateInterval))
                {
                    __result = true;
                    return false;
                }

                if (LastSetDestinationTimeField != null)
                    LastSetDestinationTimeField.SetValue(__instance, Time.time);
                if (PausedField != null)
                    PausedField.SetValue(__instance, false);
                if (CurrentSpeedFractionField != null)
                    CurrentSpeedFractionField.SetValue(__instance, speedFraction);

                Vector3 here = __instance.BaseEntity.ServerPosition;
                if (Vector3.Distance(pos, here) <= __instance.StoppingDistance)
                {
                    __result = true;
                    return false;
                }

                __instance.Destination = pos;
                __instance.SetCurrentNavigationType(BaseNavigator.NavigationType.Base);
                __result = true;
                return false;
            }
        }

        private static class NpcSpawnSwimMovementHelper
        {
            private static readonly MethodInfo GetTargetSpeedMethod = typeof(BaseNavigator)
                .GetMethod("GetTargetSpeed", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            private static readonly MethodInfo BasePlayerUpdateModelState = typeof(BasePlayer)
                .GetMethod("UpdateModelState", BindingFlags.NonPublic | BindingFlags.Instance);

            private const float SwimDepthBelowSurface = 1.1f;
            private const float SwimVerticalLerp = 14f;
            private const float SwimVerticalSnapMeters = 2.5f;
            private const float SeabedClearance = 0.25f;
            private const float ModelStateSwimSendInterval = 0.12f;
            private static readonly Dictionary<ulong, float> LastSwimModelStateSend = new Dictionary<ulong, float>(32);

            private static float ComputeSwimHoldY(Vector3 xzPosition, BasePlayer forEntity)
            {
                float waterSurface = WaterLevel.GetWaterSurface(xzPosition, waves: true, volumes: true, forEntity);
                float terrainH = TerrainMeta.HeightMap != null && TerrainMeta.HeightMap.isInitialized
                    ? TerrainMeta.HeightMap.GetHeight(xzPosition)
                    : float.NegativeInfinity;
                float holdY = waterSurface - SwimDepthBelowSurface;
                float floorY = terrainH + SeabedClearance;
                if (holdY < floorY)
                    holdY = floorY;
                float maxY = waterSurface - 0.12f;
                if (holdY > maxY)
                    holdY = maxY;
                return holdY;
            }

            internal static void PushModelState(ScientistNPC npc)
            {
                if (npc == null) return;
                BasePlayerUpdateModelState?.Invoke(npc, null);
                npc.SendModelState(force: true);
            }

            private static void TryPushSwimModelState(ScientistNPC npc)
            {
                if (npc?.modelState == null || npc.net == null)
                    return;
                ulong id = npc.net.ID.Value;
                float now = Time.time;
                if (LastSwimModelStateSend.TryGetValue(id, out float last) && (now - last) < ModelStateSwimSendInterval)
                    return;

                // Use real immersion only — never force waterLevel up to 0.85 (that stuck zombies in swim anim on land).
                float wf = Mathf.Clamp01(NpcSpawnOpenWaterSwim.SafeWaterFactor(npc));
                float wl = wf;
                if (Mathf.Abs(npc.modelState.waterLevel - wl) < 0.02f)
                {
                    LastSwimModelStateSend[id] = now;
                    return;
                }

                npc.modelState.waterLevel = wl;
                PushModelState(npc);
                LastSwimModelStateSend[id] = now;
            }

            internal static void Apply(BaseNavigator __instance, Vector3 moveToPosition, float delta)
            {
                var npc = __instance.BaseEntity as ScientistNPC;
                if (npc == null) return;

                // Belt-and-suspenders: if immersion gate failed, do not run custom swim locomotion.
                if (!NpcSpawnOpenWaterSwim.TryEvaluate(npc))
                {
                    NpcSpawnOpenWaterSwim.ClearStickySwimModelState(npc);
                    return;
                }

                Vector3 currentPos = __instance.BaseEntity.transform.position;
                float targetSpeed = GetTargetSpeedMethod != null
                    ? (float)GetTargetSpeedMethod.Invoke(__instance, null)
                    : __instance.Speed;

                Vector3 flatCur = new Vector3(currentPos.x, 0f, currentPos.z);
                Vector3 flatDest = new Vector3(moveToPosition.x, 0f, moveToPosition.z);
                Vector3 flatNew = Vector3.MoveTowards(flatCur, flatDest, targetSpeed * delta);
                Vector3 xzProbe = new Vector3(flatNew.x, currentPos.y, flatNew.z);

                float swimHoldY = ComputeSwimHoldY(xzProbe, npc);
                float newY = Mathf.Abs(currentPos.y - swimHoldY) > SwimVerticalSnapMeters
                    ? swimHoldY
                    : Mathf.Lerp(currentPos.y, swimHoldY, Mathf.Clamp01(delta * SwimVerticalLerp));
                Vector3 newPosition = new Vector3(flatNew.x, newY, flatNew.z);

                var ent = __instance.BaseEntity;
                ent.transform.position = newPosition;
                ent.ServerPosition = newPosition;

                TryPushSwimModelState(npc);

                Vector3 direction2D;
                BaseEntity faceEntity = (npc is CustomScientistNpc cn) ? cn.GetBestTarget() : null;
                if (faceEntity != null && !faceEntity.IsDestroyed)
                {
                    Vector3 toTarget = faceEntity.transform.position - currentPos;
                    direction2D = new Vector3(toTarget.x, 0f, toTarget.z);
                }
                else
                {
                    Vector3 direction3D = moveToPosition - currentPos;
                    direction2D = new Vector3(direction3D.x, 0f, direction3D.z);
                }

                if (direction2D.sqrMagnitude > 0.001f && npc.eyes != null)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(direction2D.normalized, Vector3.up);
                    npc.eyes.rotation = Quaternion.Lerp(npc.eyes.rotation, targetRotation, delta * 25f);
                    npc.viewAngles = npc.eyes.rotation.eulerAngles;
                    npc.ServerRotation = npc.eyes.rotation;
                }
            }
        }

                [HarmonyPatch(typeof(BaseNavigator), nameof(BaseNavigator.IsSwimming))]
        private class Patch_NpcSpawnBaseNavigator_IsSwimming
        {
            private static bool Prefix(BaseNavigator __instance, ref bool __result)
            {
                if (!(__instance.BaseEntity is CustomScientistNpc npc) || npc.Config == null || !npc.Config.CanSwim)
                    return true;

                // Do NOT OR BasePlayer.IsSwimming alone — WaterFactor/ocean plane false-positives on dry
                // inland terrain below sea level. TryEvaluate requires real immersion + water body depth.
                __result = NpcSpawnOpenWaterSwim.TryEvaluate(npc);
                return false;
            }
        }

                [HarmonyPatch(typeof(BaseNavigator), "GetTargetSpeed")]
        private class Patch_NpcSpawnBaseNavigator_GetTargetSpeed
        {
            private const float DefaultSwimSpeedMultiplier = 0.4f;

            private static bool Prefix(BaseNavigator __instance, ref float __result)
            {
                if (!(__instance.BaseEntity is CustomScientistNpc npc) || npc.Config == null || !npc.Config.CanSwim)
                    return true;
                                if (!__instance.IsSwimming())
                    return true;

                float currentSpeedFraction = NpcSpawnSwimNavGate.CurrentSpeedFractionField != null
                    ? (float)NpcSpawnSwimNavGate.CurrentSpeedFractionField.GetValue(__instance)
                    : 1f;

                float baseSpeed = __instance.Speed * currentSpeedFraction;
                __result = baseSpeed * DefaultSwimSpeedMultiplier;
                return false;
            }
        }

                [HarmonyPatch(typeof(BaseNavigator), nameof(BaseNavigator.UpdateNavigation))]
        private class Patch_NpcSpawnBaseNavigator_UpdateNavigation_Swim
        {
            private static bool Prefix(BaseNavigator __instance, float delta)
            {
                if (!NpcSpawnSwimNavGate.ShouldBlockNavApi(__instance))
                    return true;
                if (!NpcSpawnSwimNavGate.StockCanUpdateMovement(__instance))
                    return true;

                Vector3 moveTo = __instance.Destination;
                if (__instance.CurrentNavigationType == BaseNavigator.NavigationType.None)
                    moveTo = __instance.BaseEntity.transform.position;

                NpcSpawnSwimMovementHelper.Apply(__instance, moveTo, delta);
                return false;
            }
        }

                [HarmonyPatch(typeof(BaseNavigator), nameof(BaseNavigator.SetDestination), new Type[] { typeof(Vector3), typeof(float), typeof(float), typeof(float) })]
        private class Patch_NpcSpawnBaseNavigator_SetDestination_SwimGate
        {
            private static bool Prefix(
                BaseNavigator __instance,
                ref Vector3 pos,
                float speedFraction,
                float updateInterval,
                float navmeshSampleDistance,
                ref bool __result)
            {
                if (!NpcSpawnSwimNavGate.ShouldBlockNavApi(__instance))
                    return true;
                return NpcSpawnSwimNavGate.TryRecordSwimDestination(__instance, ref pos, speedFraction, updateInterval, ref __result);
            }
        }

                [HarmonyPatch(typeof(BaseNavigator), nameof(BaseNavigator.Stop))]
        private class Patch_NpcSpawnBaseNavigator_Stop_SwimGate
        {
            private static bool Prefix(BaseNavigator __instance)
            {
                return !NpcSpawnSwimNavGate.ShouldBlockNavApi(__instance);
            }
        }

                [HarmonyPatch(typeof(BaseNavigator), nameof(BaseNavigator.Pause))]
        private class Patch_NpcSpawnBaseNavigator_Pause_SwimGate
        {
            private static bool Prefix(BaseNavigator __instance)
            {
                return !NpcSpawnSwimNavGate.ShouldBlockNavApi(__instance);
            }
        }

                [HarmonyPatch(typeof(BaseNavigator), nameof(BaseNavigator.Resume))]
        private class Patch_NpcSpawnBaseNavigator_Resume_SwimGate
        {
            private static bool Prefix(BaseNavigator __instance)
            {
                return !NpcSpawnSwimNavGate.ShouldBlockNavApi(__instance);
            }
        }

		#endregion Commands
    }

    namespace NpcSpawnExtensionMethods
    {
        public static class ExtensionMethods
        {
        // Safe zone extension method for Vector3 (from ChaosNPC improvements)
        public static bool IsInSafeZone(this Vector3 position)
        {
            int num = Physics.OverlapSphereNonAlloc(position, 1f, Vis.colBuffer, 262144, QueryTriggerInteraction.Collide);
            for (int i = 0; i < num; i++)
            {
                if (Vis.colBuffer[i]?.GetComponent<TriggerSafeZone>() != null)
                    return true;
            }
            return false;
        }
        
        // Helper extension for XZ3D (flatten to XZ plane)
        public static Vector3 XZ3D(this Vector3 vector)
        {
            return new Vector3(vector.x, 0f, vector.z).normalized;
        }
        
        public static Vector3 XZ3D(this Vector2 vector)
        {
            return new Vector3(vector.x, 0f, vector.y).normalized;
        }
        
        public static Vector3 XZ3D(this Vector2 vector, float y)
        {
            return new Vector3(vector.x, y, vector.y);
        }
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

        public static HashSet<TSource> ToHashSet<TSource>(this IEnumerable<TSource> source)
        {
            HashSet<TSource> result = new HashSet<TSource>();
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) result.Add(enumerator.Current);
            return result;
        }

        public static HashSet<TResult> Select<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> predicate)
        {
            HashSet<TResult> result = new HashSet<TResult>();
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) result.Add(predicate(enumerator.Current));
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

        public static float Sum<TSource>(this IList<TSource> source, Func<TSource, float> predicate)
        {
            float result = 0;
            for (int i = 0; i < source.Count; i++) result += predicate(source[i]);
            return result;
        }

        public static TSource Last<TSource>(this IList<TSource> source) => source[source.Count - 1];

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

        public static bool IsEqualVector3(this Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 0.1f;
    }
    }
}
