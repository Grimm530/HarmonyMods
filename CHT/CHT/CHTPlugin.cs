/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Facepunch.Extend;
using HarmonyLib;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Rust;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Pool = Facepunch.Pool;
using Random = UnityEngine.Random;
using Time = UnityEngine.Time;

namespace Oxide.Plugins
{
    [Info("CHT", "VisEntities", "1.5.12")]
    [Description("Replaces Rust patrol helicopters with smarter, stronger, and fully customizable tiered helicopters.")]
    public class CustomHelicopterTiers2 : RustPlugin
    {
        #region 3rd Party Dependencies

        #endregion 3rd Party Dependencies

        #region Fields

        private static CustomHelicopterTiers2 _plugin;
        private static Configuration _config;

        private CooldownData _cooldownData;
        private TieredHelicopterManager _tieredHelicopterManager = new TieredHelicopterManager();

        private const string PREFAB_CRATE = "assets/prefabs/npc/patrol helicopter/heli_crate.prefab";
        private const string PREFAB_HELICOPTER = "assets/prefabs/npc/patrol helicopter/patrolhelicopter.prefab";
        private const string PREFAB_GIBS = "assets/prefabs/npc/patrol helicopter/servergibs_patrolhelicopter.prefab";

        private const string PREFAB_FIREBALL = "assets/bundled/prefabs/fireball.prefab";
        private const string PREFAB_FIREBALL_GROUND = "assets/bundled/prefabs/oilfireballsmall.prefab";
        private const string PREFAB_FIREBALL_SMOKE = "assets/prefabs/npc/m2bradley/oilfireball2.prefab";

        private const string PREFAB_ROCKET = "assets/prefabs/npc/patrol helicopter/rocket_heli.prefab";
        private const string PREFAB_ROCKET_NAPALM = "assets/prefabs/npc/patrol helicopter/rocket_heli_napalm.prefab";

        public const int LAYER_HELICOPTER = Layers.Mask.Default;
        public const int LAYER_PLAYERS = Layers.Mask.Player_Server;
        public const int LAYER_WRECKAGE = Layers.Mask.Ragdoll | Layers.Mask.Default;
        public const int LAYER_GROUND = Layers.Mask.Terrain | Layers.Mask.World | Layers.Mask.Default;

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

            [JsonProperty("Enable Debug")]
            public bool EnableDebug { get; set; }

            [JsonProperty("Global Population Limit")]
            public int GlobalPopulationLimit { get; set; }

            [JsonProperty("Rarity Weights")]
            public Dictionary<Rarity, int> RarityWeights { get; set; }

            [JsonProperty("Heli Shop Chat Command")]
            public string HeliShopChatCommand { get; set; }

            [JsonProperty("Disable Vanilla Patrol Helicopter")]
            public bool DisableVanillaHelicopter { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<Configuration>();
            if (_config == null)
            {
                LoadDefaultConfig();
                SaveConfig();
                return;
            }

            // Use numeric version compare -- string.Compare treats "1.5.12" as older than "1.5.3".
            if (IsConfigVersionOlder(_config.Version, Version.ToString()))
                UpdateConfig();

            SaveConfig();
        }

        private static bool IsConfigVersionOlder(string configVersion, string modVersion)
        {
            if (string.IsNullOrEmpty(configVersion))
                return true;
            if (!TryParseVersion(configVersion, out int cMaj, out int cMin, out int cPat))
                return true;
            if (!TryParseVersion(modVersion, out int mMaj, out int mMin, out int mPat))
                return false;
            if (cMaj != mMaj) return cMaj < mMaj;
            if (cMin != mMin) return cMin < mMin;
            return cPat < mPat;
        }

        private static bool TryParseVersion(string version, out int major, out int minor, out int patch)
        {
            major = minor = patch = 0;
            if (string.IsNullOrEmpty(version)) return false;
            string[] parts = version.Split('.');
            if (parts.Length < 1 || !int.TryParse(parts[0], out major)) return false;
            if (parts.Length > 1 && !int.TryParse(parts[1], out minor)) minor = 0;
            if (parts.Length > 2 && !int.TryParse(parts[2], out patch)) patch = 0;
            return true;
        }

        protected override void LoadDefaultConfig()
        {
            _config = GetDefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void UpdateConfig()
        {
            PrintWarning("Config changes detected! Updating...");

            Configuration defaultConfig = GetDefaultConfig();

            if (string.Compare(_config.Version, "1.0.0") < 0)
                _config = defaultConfig;

            if (string.Compare(_config.Version, "1.1.0") < 0)
            {
                _config.HeliShopChatCommand = defaultConfig.HeliShopChatCommand;
            }

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                EnableDebug = false,
                GlobalPopulationLimit = 3,
                RarityWeights = new Dictionary<Rarity, int>
                {
                    {
                        Rarity.Common, 50
                    },
                    {
                        Rarity.Uncommon, 30
                    },
                    {
                        Rarity.Rare, 15
                    },
                    {
                        Rarity.VeryRare, 5
                    }
                },
                HeliShopChatCommand = "heli.shop",
                DisableVanillaHelicopter = true
            };
        }

        #endregion Configuration

        #region Stored Data

        public class CooldownData
        {
            [JsonProperty("Next Call Times")]
            public Dictionary<ulong, Dictionary<string, double>> NextCallTimes = new Dictionary<ulong, Dictionary<string, double>>();

            [JsonProperty("Daily Call Counts")]
            public Dictionary<ulong, Dictionary<string, int>> DailyCallCounts = new Dictionary<ulong, Dictionary<string, int>>();

            [JsonProperty("Daily Call Date")]
            public Dictionary<ulong, Dictionary<string, string>> DailyCallDate = new Dictionary<ulong, Dictionary<string, string>>();
        }

        public class TierData
        {
            [JsonProperty("SchemaVersion")]
            public int SchemaVersion { get; set; }

            [JsonProperty("Name")]
            public string Name { get; set; }

            [JsonProperty("Enabled")]
            public bool Enabled { get; set; }

            [JsonProperty("Lifetime Minutes")]
            public float LifetimeMinutes { get; set; }

            [JsonProperty("Speed")]
            [JsonConverter(typeof(StringEnumConverter))]
            public Speed Speed { get; set; }

            [JsonProperty("Health")]
            public HealthData Health { get; set; }

            [JsonProperty("Spawn")]
            public SpawnData Spawn { get; set; }

            [JsonProperty("Patrol")]
            public PatrolData Patrol { get; set; }

            [JsonProperty("Targeting")]
            public TargetingData Targeting { get; set; }

            [JsonProperty("Strafe")]
            public StrafeData Strafe { get; set; }

            [JsonProperty("Machine Gun")]
            public MachineGunData MachineGun { get; set; }
            
            [JsonProperty("Homing")]
            public HomingData Homing { get; set; }

            [JsonProperty("Danger Zone")]
            public DangerZoneData DangerZone { get; set; }

            [JsonProperty("Crash")]
            public CrashData Crash { get; set; }

            [JsonProperty("Debris")]
            public DebrisData Debris { get; set; }

            [JsonProperty("PVE")]
            public PVEData PVE { get; set; }

            [JsonProperty("Loot")]
            public LootData Loot { get; set; }

            [JsonProperty("Call Profiles")]
            public List<CallProfileData> CallProfiles { get; set; } = new List<CallProfileData>();

            [JsonProperty("Run Random Death Command Set")]
            public bool RunRandomDeathCommandSet { get; set; }

            [JsonProperty("Death Command Sets")]
            public List<CommandSetData> DeathCommandSets { get; set; } = new List<CommandSetData>();
        }

        public class CommandSetData
        {
            [JsonProperty("Commands")]
            public List<CommandData> Commands { get; set; } = new List<CommandData>();
        }

        public class CommandData
        {
            [JsonProperty("Type")]
            [JsonConverter(typeof(StringEnumConverter))]
            public CommandType Type { get; set; }

            [JsonProperty("Command")]
            public string Command { get; set; }
        }

        public class HealthData
        {
            [JsonProperty("Body Health")]
            public float BodyHealth { get; set; }

            [JsonProperty("Main Rotor Health")]
            public float MainRotorHealth { get; set; }

            [JsonProperty("Tail Rotor Health")]
            public float TailRotorHealth { get; set; }
        }

        public class SpawnData
        {
            [JsonProperty("Enable Automated Spawns")]
            public bool EnableAutomatedSpawns { get; set; }

            [JsonProperty("Maximum Population")]
            public int MaximumPopulation { get; set; }

            [JsonProperty("Initial Spawn")]
            public bool InitialSpawn { get; set; }

            [JsonProperty("Minimum Respawn Delay Minutes")]
            public float MinimumRespawnDelayMinutes { get; set; }

            [JsonProperty("Maximum Respawn Delay Minutes")]
            public float MaximumRespawnDelayMinutes { get; set; }

            [JsonProperty("Minimum Number To Spawn Per Tick")]
            public int MinimumNumberToSpawnPerTick { get; set; }

            [JsonProperty("Maximum Number To Spawn Per Tick")]
            public int MaximumNumberToSpawnPerTick { get; set; }

            [JsonProperty("Spawn Locations")]
            public List<string> SpawnLocations { get; set; } = new List<string>();

            [JsonProperty("Minimum Spawn Radius From Caller (used when a heli is called by a player)")]
            public float MinimumSpawnRadiusFromCaller { get; set; }

            [JsonProperty("Maximum Spawn Radius From Caller (used when a heli is called by a player)")]
            public float MaximumSpawnRadiusFromCaller { get; set; }
        }

        public class PatrolData
        {
            [JsonProperty("Chance To Pick Monument Instead Of Random Position")]
            public int ChanceToPickMonumentInsteadOfRandomPosition { get; set; }

            [JsonProperty("No Go Monuments")]
            public List<string> NoGoMonuments { get; set; } = new List<string>();
        }

        public class MachineGunData
        {
            [JsonProperty("Time Between Individual Shots Seconds")]
            public float TimeBetweenIndividualShotsSeconds { get; set; }

            [JsonProperty("Burst Firing Duration Seconds")]
            public float BurstFiringDurationSeconds { get; set; }

            [JsonProperty("Cooldown Time Between Bursts Seconds")]
            public float CooldownTimeBetweenBurstsSeconds { get; set; }

            [JsonProperty("Maximum Target Engagement Range")]
            public float MaximumTargetEngagementRange { get; set; }

            [JsonProperty("Target Tracking Duration Before Loss Seconds")]
            public float TargetTrackingDurationBeforeLossSeconds { get; set; }

            [JsonProperty("Base Bullet Damage")]
            public float BaseBulletDamage { get; set; }

            [JsonProperty("Bullet Spread Accuracy")]
            public float BulletSpreadAccuracy { get; set; }
        }

        public class StrafeData
        {
            [JsonProperty("Can Strafe Players Near Enemy Bases")]
            public bool CanStrafePlayersNearEnemyBases { get; set; }

            [JsonProperty("Maximum Rockets Fired Per Strafe")]
            public int MaximumRocketsFiredPerStrafe { get; set; }

            [JsonProperty("Delay Between Rocket Launches Seconds")]
            public float DelayBetweenRocketLaunchesSeconds { get; set; }

            [JsonProperty("Cooldown Between Strafes Seconds")]
            public float CooldownBetweenStrafesSeconds { get; set; }

            [JsonProperty("Rocket Damage Multiplier")]
            public float RocketDamageMultiplier { get; set; }

            [JsonProperty("Chance To Upgrade From Strafe To Orbit Strafe")]
            public int ChanceToUpgradeFromStrafeToOrbitStrafe { get; set; }

            [JsonProperty("Maximum Rockets Fired Per Orbit Strafe")]
            public int MaximumRocketsFiredPerOrbitStrafe { get; set; }

            [JsonProperty("Delay Between Rocket Launches While Orbiting Seconds")]
            public float DelayBetweenRocketLaunchesWhileOrbitingSeconds { get; set; }

            [JsonProperty("Can Use Napalm Rockets")]
            public bool CanUseNapalmRockets { get; set; }

            [JsonProperty("Cooldown Between Napalm Strafes Seconds")]
            public float CooldownBetweenNapalmStrafesSeconds { get; set; }
        }

        public class TargetingData
        {
            [JsonProperty("Target Acquisition Range")]
            public float TargetAcquisitionRange { get; set; }

            [JsonProperty("Seconds Before Dropping Unseen Targets")]
            public float SecondsBeforeDroppingUnseenTargets { get; set; }

            [JsonProperty("Chance Of Final Strafe Before Dropping Target")]
            public int ChanceOfFinalStrafeBeforeDroppingTarget { get; set; }

            [JsonProperty("Only Retaliate If Attacked")]
            public bool OnlyRetaliateIfAttacked { get; set; }
        }

        public class HomingData
        {
            [JsonProperty("Can Be Homing Targeted")]
            public bool CanBeHomingTargeted { get; set; }

            [JsonProperty("Can Defend With Flares")]
            public bool CanDefendWithFlares { get; set; }

            [JsonProperty("Flare Duration Seconds")]
            public float FlareDurationSeconds { get; set; }           
        }

        public class DangerZoneData
        {
            [JsonProperty("Maximum Allowed Danger Zones")]
            public int MaximumAllowedDangerZones { get; set; }

            [JsonProperty("Base Danger Zone Radius")]
            public float BaseDangerZoneRadius { get; set; }

            [JsonProperty("Remove Least Significant Danger Zone When Full")]
            public bool RemoveLeastSignificantDangerZoneWhenFull { get; set; }

            [JsonProperty("Seconds Before Danger Zone Expires")]
            public float SecondsBeforeDangerZoneExpires { get; set; }

            [JsonProperty("No Go Zone Radius")]
            public float NoGoZoneRadius { get; set; }

            [JsonProperty("Flee Damage Percentage")]
            public int FleeDamagePercentage { get; set; }

            [JsonProperty("Seconds Before No Go Zone Expires")]
            public float SecondsBeforeNoGoZoneExpires { get; set; }
        }

        public class CrashData
        {
            [JsonProperty("Maximum Fire Balls To Spawn")]
            public int MaximumFireBallsToSpawn { get; set; }

            [JsonProperty("Fire Ball")]
            public FireBallData FireBall { get; set; }
        }

        public class PVEData
        {
            [JsonProperty("Block Damage To Non Caller Players")]
            public bool BlockDamageToNonCallerPlayers { get; set; }

            [JsonProperty("Block Damage To Non Caller Owned Entities")]
            public bool BlockDamageToNonCallerOwnedEntities { get; set; }
        }

        public class DebrisData
        {
            [JsonProperty("Spawn Gibs")]
            public bool SpawnGibs { get; set; }

            [JsonProperty("Hit Points")]
            public float HitPoints { get; set; }

            [JsonProperty("Cooling Period Seconds")]
            public float CoolingPeriodSeconds { get; set; }

            [JsonProperty("Override Default Salvage")]
            public bool OverrideDefaultSalvage { get; set; }

            [JsonProperty("Salvage Override Items")]
            public List<ItemData> SalvageOverrideItems { get; set; } = new List<ItemData>();
        }

        public class FireBallData
        {
            [JsonProperty("Minimum Lifetime Seconds")]
            public float MinimumLifetimeSeconds { get; set; }

            [JsonProperty("Maximum Lifetime Seconds")]
            public float MaximumLifetimeSeconds { get; set; }

            [JsonProperty("Damage Per Second")]
            public float DamagePerSecond { get; set; }

            [JsonProperty("Try To Spread")]
            public bool TryToSpread { get; set; }

            [JsonProperty("Water To Extinguish")]
            public int WaterToExtinguish { get; set; }
        }

        public class LootData
        {
            [JsonProperty("Maximum Crates To Spawn")]
            public int MaximumCratesToSpawn { get; set; }

            [JsonProperty("Crate Lifetime Seconds")]
            public float CrateLifetimeSeconds { get; set; }

            [JsonProperty("Lock Crates To Caller")]
            public bool LockCratesToCaller { get; set; }

            [JsonProperty("Locking Fire Ball")]
            public FireBallData LockingFireBall { get; set; }

            [JsonProperty("Alpha Loot Profile")]
            public string AlphaLootProfile { get; set; }

            [JsonProperty("Use Custom Loot Table")]
            public bool UseCustomLootTable { get; set; }

            [JsonProperty("Custom Loot Table")]
            public List<LootTableData> CustomLootTable { get; set; } = new List<LootTableData>();
        }

        public class LootTableData
        {
            [JsonProperty("Rarity")]
            [JsonConverter(typeof(StringEnumConverter))]
            public Rarity Rarity { get; set; }

            [JsonProperty("Minimum Loot Spawn Slots")]
            public int MinimumLootSpawnSlots { get; set; }

            [JsonProperty("Maximum Loot Spawn Slots")]
            public int MaximumLootSpawnSlots { get; set; }

            [JsonProperty("Items")]
            public List<ItemData> Items { get; set; } = new List<ItemData>();
        }

        public class ItemData
        {
            [JsonProperty("Short Name")]
            public string ShortName { get; set; }

            [JsonProperty("Display Name")]
            public string DisplayName { get; set; }

            [JsonProperty("Skin Id")]
            public ulong SkinId { get; set; }

            [JsonProperty("Minimum Amount")]
            public int MinimumAmount { get; set; }

            [JsonProperty("Maximum Amount")]
            public int MaximumAmount { get; set; }

            [JsonProperty("Spawn As Blueprint")]
            public bool SpawnAsBlueprint { get; set; }

            [JsonProperty("Rarity")]
            [JsonConverter(typeof(StringEnumConverter))]
            public Rarity Rarity { get; set; }
        }

        public class CallProfileData
        {
            [JsonProperty("Enabled")]
            public bool Enabled { get; set; }

            [JsonProperty("Lock On Caller")]
            public bool LockOnCaller { get; set; }

            [JsonProperty("Include Caller Allies (teammates)")]
            public bool IncludeCallerAllies { get; set; }

            [JsonProperty("Suffix")]
            public string Suffix { get; set; }

            [JsonProperty("Number To Spawn")]
            public int NumberToSpawn { get; set; }

            [JsonProperty("Priority")]
            public int Priority { get; set; }

            [JsonProperty("Cooldown Minutes")]
            public float CooldownMinutes { get; set; }

            [JsonProperty("Daily Call Limit")]
            public int DailyCallLimit { get; set; }

            [JsonProperty("Personal Message")]
            public string PersonalMessage { get; set; }

            [JsonProperty("Global Message")]
            public string GlobalMessage { get; set; }

            [JsonProperty("Skill Tree XP Rewarded")]
            public double SkillTreeXPRewarded { get; set; }

            [JsonProperty("Cost To Call", Order = 99)]
            public CurrencyData[] CostToCall = Array.Empty<CurrencyData>();

            [JsonIgnore]
            public string Permission { get; set; }

            public void InitializePermission(string tierName)
            {
                if (string.IsNullOrEmpty(Suffix))
                    return;

                Permission = ConstructPermission(tierName, Suffix);
                PermissionUtil.AddPermission(Permission, register: true);
                _plugin.Puts($"Registered permission: {Permission} (Tier={tierName}, Profile={Suffix})");
            }

            public void InitializePriceList()
            {
                if (CostToCall == null || CostToCall.Length == 0)
                    return;

                foreach (CurrencyData currency in CostToCall)
                {
                    currency.CreatePaymentGateway();
                }
            }

            private static string ConstructPermission(string tierName, string suffix)
            {
                return string.Join(".", nameof(CustomHelicopterTiers2), tierName, suffix).ToLower();
            }
        }

        public class CurrencyData
        {
            [JsonProperty("Enabled")]
            public bool Enabled { get; set; }

            [JsonProperty("Name")]
            public string Name { get; set; }

            [JsonProperty("Amount")]
            public int Amount { get; set; }

            [JsonIgnore]
            public IPaymentGateway PaymentGateway;

            [JsonIgnore]
            public PaymentGatewayType PaymentGatewayType;

            [JsonIgnore]
            public bool Valid
            {
                get
                {
                    if (PaymentGateway != null && PaymentGateway.Available && PaymentGatewayType != PaymentGatewayType.Unknown)
                        return true;

                    return false;
                }
            }

            [JsonIgnore]
            private ItemDefinition _itemDefinition;

            [JsonIgnore]
            private bool _itemInitialized;

            [JsonIgnore]
            public ItemDefinition ItemDef
            {
                get
                {
                    if (!_itemInitialized)
                    {
                        ItemDefinition foundItemDefinition = ItemManager.FindItemDefinition(Name);
                        if (foundItemDefinition != null)
                            _itemDefinition = foundItemDefinition;
                        else
                            return null;

                        _itemInitialized = true;
                    }

                    return _itemDefinition;
                }
            }

            public void CreatePaymentGateway()
            {
                if (string.IsNullOrEmpty(Name))
                    return;

                // ServerRewards "point" is not supported in this Harmony port.
                if (Name.IndexOf("point", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    PaymentGateway = null;
                    PaymentGatewayType = PaymentGatewayType.Unknown;
                    if (Enabled)
                        _plugin.PrintWarning("Cost currency 'point' is not supported (ServerRewards removed). Disable it or use 'coin'/item shortnames.");
                    return;
                }

                if (Name.IndexOf("coin", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    PaymentGateway = new CoinPaymentGateway(_plugin);
                    PaymentGatewayType = PaymentGatewayType.Coin;
                }
                else if (ItemDef != null)
                {
                    PaymentGateway = new ItemPaymentGateway(ItemDef.itemid);
                    PaymentGatewayType = PaymentGatewayType.Item;
                }
                else
                {
                    PaymentGateway = null;
                    PaymentGatewayType = PaymentGatewayType.Unknown;
                    if (Enabled)
                        _plugin.PrintWarning("Unknown cost currency '" + Name + "'. Use 'coin' (Economics) or an item shortname.");
                }
            }

            public bool CanAfford(BasePlayer player)
            {
                return PaymentGateway.Get(player) >= Amount;
            }

            public void Charge(BasePlayer player)
            {
                PaymentGateway.Deduct(player, Amount);
            }

            public void GivePlayer(BasePlayer player)
            {
                PaymentGateway.Give(player, Amount);
            }
            
            public string GetDisplayName()
            {
                if (Name.Equals("coin", StringComparison.OrdinalIgnoreCase))
                    return "Coins";

                if (PaymentGatewayType == PaymentGatewayType.Item && ItemDef != null)
                    return ItemDef.shortname;

                return Name;
            }
        }

        #endregion Stored Data

        #region Data Migration

        public static class DataMigration
        {
            private const int LatestSchemaVersion = 5;

            public static bool MigrateToLatest(TierData data)
            {
                if (data == null)
                    return false;

                bool changed = false;

                changed = MigrateV0ToV1(data) || changed;
                changed = MigrateV1ToV2(data) || changed;
                changed = MigrateV2ToV3(data) || changed;
                changed = MigrateV3ToV4(data) || changed;
                changed = MigrateV4ToV5(data) || changed;

                return changed;
            }

            private static bool MigrateV0ToV1(TierData data)
            {
                if (data.SchemaVersion != 0)
                    return false;

                bool changedAnything = false;

                if (data.Homing == null)
                {
                    data.Homing = new HomingData
                    {
                        CanBeHomingTargeted = true,
                        CanDefendWithFlares = true,
                        FlareDurationSeconds = 5f
                    };
                    changedAnything = true;
                }

                data.SchemaVersion = 1;
                return changedAnything;
            }

            private static bool MigrateV1ToV2(TierData data)
            {
                if (data.SchemaVersion != 1)
                    return false;

                bool changedAnything = false;

                if (data.DeathCommandSets == null)
                {
                    data.DeathCommandSets = new List<CommandSetData>();
                    changedAnything = true;
                }

                if (data.DeathCommandSets.Count == 0)
                {
                    CommandSetData set1 = new CommandSetData();
                    set1.Commands.Add(new CommandData
                    {
                        Type = CommandType.Chat,
                        Command = "I have just taken down the {TierName} helicopter at grid {Grid}!"
                    });
                    data.DeathCommandSets.Add(set1);

                    CommandSetData set2 = new CommandSetData();
                    set2.Commands.Add(new CommandData
                    {
                        Type = CommandType.Client,
                        Command = "gametip.showgametip {PlayerName}, you destroyed the {TierName}!"
                    });
                    set2.Commands.Add(new CommandData
                    {
                        Type = CommandType.Server,
                        Command = "inventory.giveto {PlayerId} scrap 100"
                    });
                    data.DeathCommandSets.Add(set2);

                    changedAnything = true;
                }

                data.RunRandomDeathCommandSet = false;

                if (data.CallProfiles != null)
                {
                    foreach (CallProfileData callProfile in data.CallProfiles)
                    {
                        callProfile.SkillTreeXPRewarded = 0.0;
                    }

                    changedAnything = true;
                }

                data.SchemaVersion = 2;
                return changedAnything;
            }

            private static bool MigrateV2ToV3(TierData data)
            {
                if (data == null || data.SchemaVersion != 2)
                    return false;

                bool changedAnything = false;

                if (data.Strafe != null && data.Strafe.RocketDamageMultiplier == 0f)
                {
                    data.Strafe.RocketDamageMultiplier = 1f;
                    changedAnything = true;
                }

                if (data.CallProfiles != null)
                {
                    foreach (var cp in data.CallProfiles)
                    {
                        if (cp == null) continue;
                        if (cp.DailyCallLimit == 0)
                        {
                            cp.DailyCallLimit = 0;
                            changedAnything = true;
                        }
                    }
                }

                data.SchemaVersion = 3;
                return changedAnything;
            }

            private static bool MigrateV3ToV4(TierData data)
            {
                if (data.SchemaVersion != 3)
                    return false;

                bool changed = false;

                if (data.CallProfiles != null)
                {
                    foreach (var callProfile in data.CallProfiles)
                    {
                        if (string.IsNullOrWhiteSpace(callProfile.PersonalMessage))
                        {
                            callProfile.PersonalMessage = "You have called in the {TierName} helicopter.";
                            changed = true;
                        }

                        if (string.IsNullOrWhiteSpace(callProfile.GlobalMessage))
                        {
                            callProfile.GlobalMessage = "{PlayerName} has called in the {TierName} helicopter!";
                            changed = true;
                        }
                    }
                }

                data.SchemaVersion = 4;
                return changed;
            }

            private static bool MigrateV4ToV5(TierData data)
            {
                if (data == null || data.SchemaVersion != 4)
                    return false;

                if (data.CallProfiles != null)
                {
                    foreach (var cp in data.CallProfiles)
                    {
                        if (cp != null)
                            cp.IncludeCallerAllies = true;
                    }
                }

                data.RunRandomDeathCommandSet = false;
                data.DeathCommandSets = new List<CommandSetData>();

                CommandSetData set1 = new CommandSetData();
                set1.Commands.Add(new CommandData
                {
                    Type = CommandType.Chat,
                    Command = "I have just taken down the {TierName} helicopter at grid {Grid}!"
                });
                data.DeathCommandSets.Add(set1);

                CommandSetData group2 = new CommandSetData();
                group2.Commands.Add(new CommandData
                {
                    Type = CommandType.Client,
                    Command = "gametip.showgametip {PlayerName}, you destroyed the {TierName}!"
                });
                group2.Commands.Add(new CommandData
                {
                    Type = CommandType.Server,
                    Command = "inventory.giveto {PlayerId} scrap 100"
                });
                data.DeathCommandSets.Add(group2);

                if (data.Spawn != null)
                {
                    data.Spawn.MinimumSpawnRadiusFromCaller = 500f;
                    data.Spawn.MaximumSpawnRadiusFromCaller = 700f;
                }

                data.SchemaVersion = 5;
                return true;
            }
        }

        #endregion Data Migration

        #region Oxide Hooks

        private void Init()
        {
            _plugin = this;
            DataFileUtil.EnsureFolderCreated();
            _cooldownData = DataFileUtil.LoadOrCreateCooldowns<CooldownData>();
            PermissionUtil.RegisterPermissions();
            cmd.AddChatCommand(_config.HeliShopChatCommand, this, nameof(cmdHeliShop));
        }

        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null)
                    continue;

                CuiHelper.DestroyUi(player, UI_OVERLAY);
                CuiHelper.DestroyUi(player, UI_DETAILS_OVERLAY);
            }

            if (_tieredHelicopterManager != null)
                _tieredHelicopterManager.Unload();

            _config = null;
            _plugin = null;
        }

        private void OnServerInitialized(bool isStartup)
        {
            RemoveLegacyRealtimeCooldowns();

            _tieredHelicopterManager.LoadTiers();


            if (_config.DisableVanillaHelicopter)
            {
                TriggeredEventPrefab[] eventPrefabs = UnityEngine.Object.FindObjectsOfType<TriggeredEventPrefab>();
                if (eventPrefabs != null && eventPrefabs.Length > 0)
                {
                    TriggeredEventPrefab eventSpawner = null;
                    foreach (TriggeredEventPrefab prefab in eventPrefabs)
                    {
                        if (prefab == null || prefab.targetPrefab == null || prefab.targetPrefab.resourcePath == null)
                            continue;

                        if (prefab.targetPrefab.resourcePath.Contains("heli"))
                        {
                            eventSpawner = prefab;
                            break;
                        }
                    }

                    if (eventSpawner != null)
                    {
                        UnityEngine.Object.Destroy(eventSpawner);
                        _plugin.Puts("Destroyed the vanilla patrol helicopter event prefab (DisableVanillaHelicopter = true).");
                    }
                }
            }
        }

        public void OnEntityDeath(PatrolHelicopter patrolHelicopter, HitInfo hitInfo)
        {
            if (patrolHelicopter == null || hitInfo == null)
                return;

            TieredHelicopterComponent tieredHelicopter = _tieredHelicopterManager.GetTieredComponentForHelicopter(patrolHelicopter);
            if (tieredHelicopter == null)
                return;

            TierData tierData = tieredHelicopter.TierData;
            Vector3 deathPosition = patrolHelicopter.transform.position;
            CallProfileData callProfile = tieredHelicopter.CallProfile;
            BasePlayer callingPlayer = tieredHelicopter.CallingPlayer;

            NextTick(() =>
            {
                CollectHelicopterWreckage(deathPosition, tierData, callProfile, callingPlayer);
                RunDeathCommands(tierData, callingPlayer, deathPosition);
                if (callingPlayer != null && callProfile != null)
                {
                    if (callProfile.SkillTreeXPRewarded > 0)
                        SkillTreeUtil.AwardSkillTreeXP(callingPlayer, callProfile.SkillTreeXPRewarded);

                }
            });
        }

        /// <summary>
        /// Harmony replacement for Oxide OnDispenserGather on heli debris.
        /// Returns true when vanilla salvage should be skipped (override applied or empty override table).
        /// </summary>
        public bool TryOverrideDebrisSalvage(ResourceDispenser dispenser, BasePlayer player)
        {
            if (dispenser == null || player == null)
                return false;

            HelicopterDebris helicopterDebris = dispenser.GetComponent<HelicopterDebris>();
            if (helicopterDebris == null)
                return false;

            string tierName = _tieredHelicopterManager.GetTierNameByDebris(helicopterDebris);
            if (string.IsNullOrEmpty(tierName))
                return false;

            TierData tierData = _tieredHelicopterManager.GetTierDataByTierName(tierName);
            if (tierData?.Debris == null || !tierData.Debris.OverrideDefaultSalvage)
                return false;

            List<ItemData> salvageList = tierData.Debris.SalvageOverrideItems;
            if (salvageList == null || salvageList.Count == 0)
                return true;

            ItemData chosenItemData = ChooseRandomItem(salvageList, _config.RarityWeights);
            if (chosenItemData == null)
                return true;

            ItemDefinition itemDef = ItemManager.FindItemDefinition(chosenItemData.ShortName);
            if (itemDef == null)
                return true;

            int amount = Random.Range(chosenItemData.MinimumAmount, chosenItemData.MaximumAmount + 1);
            Item newItem = ItemManager.Create(itemDef, amount, chosenItemData.SkinId);
            if (newItem == null)
                return true;

            if (chosenItemData.SpawnAsBlueprint)
            {
                ItemDefinition blueprintBaseDef = GetBlueprintBaseDef();
                if (blueprintBaseDef != null)
                {
                    Item bpItem = ItemManager.Create(blueprintBaseDef, 1);
                    bpItem.blueprintTarget = itemDef.itemid;
                    newItem.Remove();
                    player.GiveItem(bpItem, BaseEntity.GiveItemReason.ResourceHarvested);
                    return true;
                }
            }

            player.GiveItem(newItem, BaseEntity.GiveItemReason.ResourceHarvested);
            return true;
        }

        public object CanHelicopterStrafe(PatrolHelicopterAI patrolHelicopterAi)
        {
            TieredHelicopterComponent tieredHelicopter = _tieredHelicopterManager.GetTieredComponentForHelicopter(patrolHelicopterAi.helicopterBase);
            if (tieredHelicopter == null)
                return null;

            StrafeData strafeData = tieredHelicopter.TierData.Strafe;
            if (strafeData == null)
                return null;

            float timeSinceLastStrafe = Time.realtimeSinceStartup - patrolHelicopterAi.lastStrafeTime;
            if (timeSinceLastStrafe < strafeData.CooldownBetweenStrafesSeconds)
                return false;

            return true;
        }

        public object CanHelicopterUseNapalm(PatrolHelicopterAI patrolHelicopterAi)
        {
            TieredHelicopterComponent tieredHelicopter = _tieredHelicopterManager.GetTieredComponentForHelicopter(patrolHelicopterAi.helicopterBase);
            if (tieredHelicopter == null)
                return null;

            StrafeData strafeData = tieredHelicopter.TierData.Strafe;
            if (strafeData == null)
                return null;

            if (!strafeData.CanUseNapalmRockets)
                return false;

            float timeSinceLastNapalm = Time.realtimeSinceStartup - patrolHelicopterAi.lastNapalmTime;
            if (timeSinceLastNapalm < strafeData.CooldownBetweenNapalmStrafesSeconds)
                return false;

            return true;
        }

        public object CanHelicopterStrafeTarget(PatrolHelicopterAI patrolHelicopterAi, BasePlayer player)
        {
            if (player == null)
                return null;

            TieredHelicopterComponent tieredHelicopter = _tieredHelicopterManager.GetTieredComponentForHelicopter(patrolHelicopterAi.helicopterBase);
            if (tieredHelicopter == null)
                return null;

            if (!tieredHelicopter.CanTargetPlayer(player))
                return false;

            StrafeData strafeData = tieredHelicopter.TierData.Strafe;
            if (strafeData == null)
                return null;

            if (!strafeData.CanStrafePlayersNearEnemyBases && PlayerUtil.NearEnemyBase(player))
                return false;

            return true;
        }

        public object OnCanBeHomingTargeted(PatrolHelicopter patrolHelicopter)
        {
            if (patrolHelicopter == null)
                return null;

            TieredHelicopterComponent tieredHelicopter = _tieredHelicopterManager.GetTieredComponentForHelicopter(patrolHelicopter);
            if (tieredHelicopter == null)
                return null;

            if (tieredHelicopter.TierData == null || tieredHelicopter.TierData.Homing == null)
                return null;

            if (!tieredHelicopter.TierData.Homing.CanBeHomingTargeted)
                return false;

            return null;
        }

        public void OnEntitySpawned(TimedExplosive explosive)
        {
            if (explosive == null)
                return;

            string prefabName = explosive.PrefabName;
            if (!prefabName.Equals(PREFAB_ROCKET, StringComparison.OrdinalIgnoreCase)
                && !prefabName.Equals(PREFAB_ROCKET_NAPALM, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            PatrolHelicopter patrolHelicopter = FindHelicopterInVicinity(explosive.transform.position, 10f, true);
            if (patrolHelicopter == null)
                return;

            TieredHelicopterComponent tieredHelicopter = _tieredHelicopterManager.GetTieredComponentForHelicopter(patrolHelicopter);
            if (tieredHelicopter == null)
                return;

            explosive.creatorEntity = patrolHelicopter;

            if (tieredHelicopter.TierData != null && tieredHelicopter.TierData.Strafe != null && tieredHelicopter.TierData.Strafe.RocketDamageMultiplier is float mult && mult != 1f)
            {
                explosive.SetDamageScale(mult);
            }
        }

        public object OnPatrolHelicopterTakeDamage(PatrolHelicopter patrolHelicopter, HitInfo hitInfo)
        {
            if (patrolHelicopter == null || hitInfo == null)
                return null;

            TieredHelicopterComponent tieredHelicopter = _tieredHelicopterManager.GetTieredComponentForHelicopter(patrolHelicopter);
            if (tieredHelicopter == null)
                return null;

            BasePlayer attackerPlayer = hitInfo.Initiator as BasePlayer;
            if (attackerPlayer != null)
            {
                if (!tieredHelicopter.CanDamage(attackerPlayer))
                {
                    hitInfo.damageTypes.Clear();
                    return true;
                }

                tieredHelicopter.OnDamageTaken(attackerPlayer, hitInfo);
            }

            return null;
        }

        public object OnPatrolHelicopterAttacked(PatrolHelicopterAI PatrolHelicopterAi, HitInfo hitInfo)
        {
            if (PatrolHelicopterAi == null || PatrolHelicopterAi.helicopterBase == null || hitInfo == null)
                return null;

            TieredHelicopterComponent tieredHelicopter = _tieredHelicopterManager.GetTieredComponentForHelicopter(PatrolHelicopterAi.helicopterBase);
            if (tieredHelicopter == null)
                return null;

            BasePlayer attackerPlayer = hitInfo.Initiator as BasePlayer;
            if (attackerPlayer != null)
            {
                if (tieredHelicopter.CanTargetPlayer(attackerPlayer))
                    PatrolHelicopterAi.TryAddTarget(attackerPlayer);
            }

            return true;
        }

        public object OnEntityTakeDamage(BaseEntity entity, HitInfo hitInfo)
        {
            if (entity == null || hitInfo == null)
                return null;

            BaseEntity attacker = hitInfo.Initiator;
            if (attacker == null)
                return null;

            TieredHelicopterComponent tieredHelicopter = null;
            if (attacker is PatrolHelicopter patrolHelicopter)
            {
                tieredHelicopter = TieredHelicopterComponent.GetComponent(patrolHelicopter);
            }

            else if (attacker is FireBall fireBall)
            {
                tieredHelicopter = _tieredHelicopterManager.GetTieredHelicopterByFireBall(fireBall);
            }

            if (tieredHelicopter == null)
                return null;

            if (!tieredHelicopter.CanHurtEntity(entity))
            {
                hitInfo.damageTypes.Clear();
                return true;
            }

            return null;
        }

        public object CanLootEntity(BasePlayer player, LootContainer lootContainer)
        {
            if (lootContainer == null)
                return null;

            HelicopterCrateLockComponent crateLock = lootContainer.GetComponent<HelicopterCrateLockComponent>();
            if (crateLock == null)
                return null;

            if (player.userID == crateLock.CallerId)
                return null;

            if (crateLock.IncludeTeam && PlayerUtil.AreAllies(player.userID, crateLock.CallerId))
                return null;

            MessagePlayer(player, "This crate is locked to the helicopter caller!");
            return true;
        }

        #endregion Oxide Hooks

        #region Helicopter Wreckage

        public void CollectHelicopterWreckage(Vector3 position, TierData tierData, CallProfileData callProfile, BasePlayer callingPlayer, float searchRadius = 15f)
        {
            List<BaseEntity> nearbyEntities = Pool.Get<List<BaseEntity>>();
            Vis.Entities(position, searchRadius, nearbyEntities, LAYER_WRECKAGE, QueryTriggerInteraction.Ignore);

            foreach (BaseEntity entity in nearbyEntities)
            {
                if (entity is LootContainer crate)
                {
                    crate.CancelInvoke(crate.RemoveMe);
                    crate.Invoke(new Action(crate.RemoveMe), tierData.Loot.CrateLifetimeSeconds);

                    _tieredHelicopterManager.AddCrateToTier(tierData.Name, crate);

                    if (callProfile != null && callProfile.LockOnCaller &&
                        callingPlayer != null && tierData.Loot.LockCratesToCaller)
                    {
                        HelicopterCrateLockComponent crateLock = crate.gameObject.AddComponent<HelicopterCrateLockComponent>();
                        crateLock.CallerId = callingPlayer.userID;
                        crateLock.IncludeTeam = callProfile.IncludeCallerAllies;
                    }

                    List<FireBall> childFireBalls = FindChildrenOfType<FireBall>(crate);
                    foreach (FireBall fb in childFireBalls)
                    {
                        if (tierData.Loot.LockingFireBall != null)
                        {
                            fb.damagePerSecond = tierData.Loot.LockingFireBall.DamagePerSecond;
                            fb.waterToExtinguish = tierData.Loot.LockingFireBall.WaterToExtinguish;

                            float lifetime = Random.Range(
                                tierData.Loot.LockingFireBall.MinimumLifetimeSeconds,
                                tierData.Loot.LockingFireBall.MaximumLifetimeSeconds
                            );

                            if (lifetime <= 0f)
                            {
                                fb.Extinguish();
                            }
                            else
                            {
                                fb.CancelInvoke(fb.Extinguish);
                                fb.Invoke(new Action(fb.Extinguish), lifetime);
                            }

                            if (!tierData.Loot.LockingFireBall.TryToSpread)
                            {
                                fb.CancelInvoke(fb.TryToSpread);
                            }
                        }

                        _tieredHelicopterManager.AddFireBallToTier(tierData.Name, fb);
                    }

                    if (!TryPopulateWithAlphaLoot(crate, tierData.Loot))
                    {
                        PopulateWithCustomLoot(crate, tierData.Loot);
                    }
                }
                else if (entity is HelicopterDebris debris)
                {
                    if (tierData.Debris != null)
                    {
                        if (!tierData.Debris.SpawnGibs)
                        {
                            debris.Kill();
                            continue;
                        }
                        else
                        {
                            debris.InitializeHealth(tierData.Debris.HitPoints, tierData.Debris.HitPoints);
                            debris.tooHotUntil = Time.realtimeSinceStartup + tierData.Debris.CoolingPeriodSeconds;
                        }
                    }

                    _tieredHelicopterManager.AddDebrisToTier(tierData.Name, debris);
                }
                else if (entity is FireBall fireBall)
                {
                    if (fireBall.GetParentEntity() is LootContainer)
                        continue;

                    if (tierData.Crash != null && tierData.Crash.FireBall != null)
                    {
                        fireBall.damagePerSecond = tierData.Crash.FireBall.DamagePerSecond;
                        fireBall.waterToExtinguish = tierData.Crash.FireBall.WaterToExtinguish;

                        float lifetime = Random.Range(
                            tierData.Crash.FireBall.MinimumLifetimeSeconds,
                            tierData.Crash.FireBall.MaximumLifetimeSeconds
                        );
                        if (lifetime <= 0f)
                        {
                            fireBall.Extinguish();
                        }
                        else
                        {
                            fireBall.CancelInvoke(fireBall.Extinguish);
                            fireBall.Invoke(new Action(fireBall.Extinguish), lifetime);
                        }

                        if (!tierData.Crash.FireBall.TryToSpread)
                        {
                            fireBall.CancelInvoke(fireBall.TryToSpread);
                        }
                    }

                    _tieredHelicopterManager.AddFireBallToTier(tierData.Name, fireBall);
                }
            }
            Pool.FreeUnmanaged(ref nearbyEntities);
        }

        #endregion Helicopter Wreckage

        #region Loot Population

        private bool TryPopulateWithAlphaLoot(LootContainer crate, LootData lootData)
        {
            if (string.IsNullOrEmpty(lootData.AlphaLootProfile))
                return false;

            if (!AlphaLootUtil.ProfileExists(lootData.AlphaLootProfile))
            {
                Puts($"Alpha Loot profile '{lootData.AlphaLootProfile}' does not exist. Falling back to custom loot.");
                return false;
            }

            bool success = AlphaLootUtil.PopulateLoot(crate, lootData.AlphaLootProfile);
            if (success)
            {
                Puts($"Alpha Loot successfully populated crate with profile '{lootData.AlphaLootProfile}'.");
            }
            else
            {
                Puts($"Alpha Loot failed to populate crate with profile '{lootData.AlphaLootProfile}'. Falling back to custom loot.");
            }

            return success;
        }

        private void PopulateWithCustomLoot(LootContainer crate, LootData lootData)
        {
            if (!lootData.UseCustomLootTable || lootData.CustomLootTable == null || lootData.CustomLootTable.Count == 0)
            {
                Puts("Custom loot table disabled or empty; using vanilla loot.");
                return;
            }

            crate.inventory.Clear();

            LootTableData chosenLootTable = ChooseRandomLootTable(lootData.CustomLootTable, _config.RarityWeights);
            if (chosenLootTable == null || chosenLootTable.Items == null || chosenLootTable.Items.Count == 0)
                return;

            int minSlots = chosenLootTable.MinimumLootSpawnSlots;
            int maxSlots = chosenLootTable.MaximumLootSpawnSlots;
            int slotsToPopulate = Random.Range(minSlots, Mathf.Min(maxSlots, 12) + 1);

            crate.inventory.capacity = slotsToPopulate;

            List<ItemData> availableItems = Pool.Get<List<ItemData>>();
            availableItems.AddRange(chosenLootTable.Items);

            for (int i = 0; i < slotsToPopulate; i++)
            {
                if (availableItems.Count == 0)
                    break;

                ItemData chosenItem = ChooseRandomItem(availableItems, _config.RarityWeights);
                if (chosenItem != null)
                {
                    availableItems.Remove(chosenItem);

                    ItemDefinition itemDef = ItemManager.FindItemDefinition(chosenItem.ShortName);
                    if (itemDef != null)
                    {
                        if (chosenItem.SpawnAsBlueprint)
                        {
                            ItemDefinition blueprintBaseDef = GetBlueprintBaseDef();
                            if (blueprintBaseDef != null)
                            {
                                Item bpItem = ItemManager.Create(blueprintBaseDef, 1);
                                bpItem.blueprintTarget = itemDef.itemid;

                                if (!bpItem.MoveToContainer(crate.inventory))
                                    bpItem.Remove(0f);
                            }
                        }
                        else
                        {
                            int amount = Random.Range(chosenItem.MinimumAmount, chosenItem.MaximumAmount + 1);
                            Item item = ItemManager.Create(itemDef, amount, chosenItem.SkinId);
                            if (item != null)
                            {
                                item.OnVirginSpawn();

                                if (!item.MoveToContainer(crate.inventory))
                                    item.Remove(0f);
                            }
                        }
                    }
                }
            }
            Pool.FreeUnmanaged(ref availableItems);

            int spawnedCount = crate.inventory.itemList.Count;
            crate.inventory.capacity = Mathf.Clamp(spawnedCount, 0, 12);
        }

        private ItemDefinition GetBlueprintBaseDef()
        {
            return ItemManager.FindItemDefinition("blueprintbase");
        }

        private LootTableData ChooseRandomLootTable(List<LootTableData> lootTables, Dictionary<Rarity, int> rarityWeights)
        {
            if (lootTables == null || lootTables.Count == 0)
                return null;

            int totalWeight = 0;
            List<int> cumulativeWeights = new List<int>();

            foreach (LootTableData lootTable in lootTables)
            {
                Rarity rarity = lootTable.Rarity;
                int weight = 0;
                if (rarityWeights.ContainsKey(rarity))
                    weight = rarityWeights[rarity];

                totalWeight += weight;
                cumulativeWeights.Add(totalWeight);
            }

            int randomValue = Random.Range(0, totalWeight);
            for (int i = 0; i < cumulativeWeights.Count; i++)
            {
                if (randomValue < cumulativeWeights[i])
                    return lootTables[i];
            }
            return lootTables[lootTables.Count - 1];
        }

        private ItemData ChooseRandomItem(List<ItemData> items, Dictionary<Rarity, int> rarityWeights)
        {
            if (items == null || items.Count == 0)
                return null;

            int totalWeight = 0;
            List<int> cumulativeWeights = new List<int>();

            foreach (ItemData item in items)
            {
                Rarity rarity = item.Rarity;
                int weight = 0;
                if (rarityWeights.ContainsKey(rarity))
                    weight = rarityWeights[rarity];

                totalWeight += weight;
                cumulativeWeights.Add(totalWeight);
            }

            int randomValue = Random.Range(0, totalWeight);
            for (int i = 0; i < cumulativeWeights.Count; i++)
            {
                if (randomValue < cumulativeWeights[i])
                    return items[i];
            }
            return items[items.Count - 1];
        }

        #endregion Loot Population

        #region Tiered Helicopter Component

        public class TieredHelicopterComponent : FacepunchBehaviour
        {
            #region Fields

            public TierData TierData { get; private set; }
            public PatrolHelicopter PatrolHelicopter { get; private set; }
            public PatrolHelicopterAI PatrolHelicopterAi { get; private set; }
            public CallProfileData CallProfile { get; private set; }
            public BasePlayer CallingPlayer { get; private set; }

            private TieredHelicopterManager _manager;
            private HashSet<AttackerInfo> _attackerInfos = new HashSet<AttackerInfo>();
            private float _baseMaxSpeed = -1f;
            private bool _tierSettingsApplied;
            private int _tierApplyAttempts;

            #endregion Fields

            #region Initialization and Quitting

            public static TieredHelicopterComponent Install(PatrolHelicopter patrolHelicopter, TierData tierData, TieredHelicopterManager manager, BasePlayer callingPlayer = null)
            {
                TieredHelicopterComponent tieredHelicopter = patrolHelicopter.gameObject.AddComponent<TieredHelicopterComponent>();
                tieredHelicopter.Initialize(tierData, manager, callingPlayer);
                return tieredHelicopter;
            }

            public void Initialize(TierData tierData, TieredHelicopterManager manager, BasePlayer callingPlayer = null)
            {
                PatrolHelicopter = GetComponent<PatrolHelicopter>();
                PatrolHelicopterAi = PatrolHelicopter != null ? PatrolHelicopter.myAI : null;
                TierData = tierData;
                if (callingPlayer != null)
                {
                    CallingPlayer = callingPlayer;
                    CallProfile = _plugin.GetCallProfileFor(CallingPlayer, TierData);
                }

                _manager = manager;

                // Apply immediately — do not wait only on Unity Start() (guns/AI can be ready now).
                ApplyTierSettings();
            }

            public static TieredHelicopterComponent GetComponent(PatrolHelicopter patrolHelicopter)
            {
                return patrolHelicopter != null ? patrolHelicopter.gameObject.GetComponent<TieredHelicopterComponent>() : null;
            }

            public void DestroySelf()
            {
                DestroyImmediate(this);
            }

            #endregion Initialization and Quitting

            #region Component Lifecycle

            private void Start()
            {
                if (!_tierSettingsApplied)
                    ApplyTierSettings();
            }

            /// <summary>
            /// Writes tier MachineGun / Speed / Health / Strafe / Homing onto the live PatrolHelicopter.
            /// Retries briefly if turrets are not ready yet.
            /// </summary>
            public void ApplyTierSettings(bool force = false)
            {
                if (PatrolHelicopter == null || PatrolHelicopter.IsDestroyed)
                    return;

                if (PatrolHelicopterAi == null)
                    PatrolHelicopterAi = PatrolHelicopter.myAI;
                if (PatrolHelicopterAi == null || TierData == null)
                    return;

                if (_tierSettingsApplied && !force)
                    return;

                if (TierData.Health != null)
                    InitializeHealth();

                if (_baseMaxSpeed < 0f)
                    _baseMaxSpeed = PatrolHelicopterAi.maxSpeed > 0f ? PatrolHelicopterAi.maxSpeed : 42f;
                PatrolHelicopterAi.maxSpeed = _baseMaxSpeed * GetSpeedMultiplier();

                if (TierData.MachineGun != null)
                    PatrolHelicopter.bulletDamage = TierData.MachineGun.BaseBulletDamage;

                if (TierData.Loot != null)
                    PatrolHelicopter.maxCratesToSpawn = TierData.Loot.MaximumCratesToSpawn;

                if (TierData.Strafe != null)
                {
                    PatrolHelicopterAi.numRocketsLeft = TierData.Strafe.MaximumRocketsFiredPerStrafe;
                    PatrolHelicopterAi.timeBetweenRockets = TierData.Strafe.DelayBetweenRocketLaunchesSeconds;
                    PatrolHelicopterAi.timeBetweenRocketsOrbit = TierData.Strafe.DelayBetweenRocketLaunchesWhileOrbitingSeconds;
                }

                if (TierData.Homing != null)
                    PatrolHelicopter.flareDuration = TierData.Homing.FlareDurationSeconds;

                bool gunsReady = PatrolHelicopterAi.leftGun != null && PatrolHelicopterAi.rightGun != null;
                if (gunsReady && TierData.MachineGun != null)
                {
                    InitializeMachineGuns();
                    _tierSettingsApplied = true;
                    UnityEngine.Debug.Log(
                        $"[CHT] Applied tier '{TierData.Name}': fireRate={TierData.MachineGun.TimeBetweenIndividualShotsSeconds}, " +
                        $"burst={TierData.MachineGun.BurstFiringDurationSeconds}s, cooldown={TierData.MachineGun.CooldownTimeBetweenBurstsSeconds}s, " +
                        $"spread={TierData.MachineGun.BulletSpreadAccuracy}, speed={TierData.Speed} (maxSpeed={PatrolHelicopterAi.maxSpeed:0.##}), " +
                        $"HP={TierData.Health?.BodyHealth}, rockets={TierData.Strafe?.MaximumRocketsFiredPerStrafe}, " +
                        $"targetRange={TierData.Targeting?.TargetAcquisitionRange}, dropUnseen={TierData.Targeting?.SecondsBeforeDroppingUnseenTargets}s");
                    return;
                }

                _tierApplyAttempts++;
                if (_tierApplyAttempts <= 50)
                    Invoke(nameof(RetryApplyTierSettings), 0.1f);
                else
                    UnityEngine.Debug.LogWarning($"[CHT] Tier '{TierData.Name}' applied without turrets (left/right gun still null).");
            }

            private void RetryApplyTierSettings()
            {
                if (!_tierSettingsApplied)
                    ApplyTierSettings();
            }

            private void Update()
            {
                UpdateTargetList();

                if (Time.realtimeSinceStartup >= _nextNoGoCheckTime)
                {
                    _nextNoGoCheckTime = Time.realtimeSinceStartup + _noGoCheckInterval;
                    RemoveExpiredNoGoZones();
                }

                if (RemainingLifetimeSeconds <= 0f)
                {
                    if (!PatrolHelicopterAi.isRetiring && !PatrolHelicopterAi.isDead)
                        PatrolHelicopterAi.Retire();
                }

                if (_config.EnableDebug)
                {
                    foreach (BasePlayer player in BasePlayer.activePlayerList)
                    {
                        if (player != null && PermissionUtil.HasPermission(player, PermissionUtil.ADMIN))
                        {
                            Debug(player);
                        }
                    }
                }
            }

            private void OnDestroy()
            {
                _manager.HandleTieredHelicopterDestroyed(this);
            }

            #endregion Component Lifecycle

            #region Lifetime

            public float RemainingLifetimeSeconds
            {
                get
                {
                    return TierData.LifetimeMinutes * 60f - (Time.realtimeSinceStartup - PatrolHelicopterAi.spawnTime);
                }
            }

            #endregion Lifetime

            #region Health

            private void InitializeHealth()
            {
                PatrolHelicopter.InitializeHealth(TierData.Health.BodyHealth, TierData.Health.BodyHealth);

                if (PatrolHelicopter.weakspots != null)
                {
                    foreach (var weakspot in PatrolHelicopter.weakspots)
                    {
                        if (weakspot.bonenames.Contains("main_rotor_col"))
                        {
                            weakspot.health = TierData.Health.MainRotorHealth;
                            weakspot.maxHealth = TierData.Health.MainRotorHealth;
                        }
                        else if (weakspot.bonenames.Contains("tail_rotor_col"))
                        {
                            weakspot.health = TierData.Health.TailRotorHealth;
                            weakspot.maxHealth = TierData.Health.TailRotorHealth;
                        }
                    }
                }
            }

            #endregion Health

            #region Speed

            private void InitializeSpeed()
            {
                float multiplier = GetSpeedMultiplier();
                PatrolHelicopterAi.maxSpeed *= multiplier;
            }

            private float GetSpeedMultiplier()
            {
                switch (TierData.Speed)
                {
                    case Speed.VerySlow:
                        return 0.75f;
                    case Speed.Slow:
                        return 0.90f;
                    case Speed.Normal:
                        return 1.00f;
                    case Speed.Fast:
                        return 1.10f;
                    case Speed.VeryFast:
                        return 1.20f;
                    default:
                        return 1.00f;
                }
            }

            #endregion Speed

            #region Machine Guns

            private void InitializeMachineGuns()
            {
                if (PatrolHelicopterAi.leftGun != null)
                {
                    PatrolHelicopterAi.leftGun.fireRate = TierData.MachineGun.TimeBetweenIndividualShotsSeconds;
                    PatrolHelicopterAi.leftGun.burstLength = TierData.MachineGun.BurstFiringDurationSeconds;
                    PatrolHelicopterAi.leftGun.timeBetweenBursts = TierData.MachineGun.CooldownTimeBetweenBurstsSeconds;
                    PatrolHelicopterAi.leftGun.maxTargetRange = TierData.MachineGun.MaximumTargetEngagementRange;
                    PatrolHelicopterAi.leftGun.loseTargetAfter = TierData.MachineGun.TargetTrackingDurationBeforeLossSeconds;
                }

                if (PatrolHelicopterAi.rightGun != null)
                {
                    PatrolHelicopterAi.rightGun.fireRate = TierData.MachineGun.TimeBetweenIndividualShotsSeconds;
                    PatrolHelicopterAi.rightGun.burstLength = TierData.MachineGun.BurstFiringDurationSeconds;
                    PatrolHelicopterAi.rightGun.timeBetweenBursts = TierData.MachineGun.CooldownTimeBetweenBurstsSeconds;
                    PatrolHelicopterAi.rightGun.maxTargetRange = TierData.MachineGun.MaximumTargetEngagementRange;
                    PatrolHelicopterAi.rightGun.loseTargetAfter = TierData.MachineGun.TargetTrackingDurationBeforeLossSeconds;
                }
            }

            #endregion Machine Guns

            #region Kill

            public void Kill(bool simulateDeath)
            {
                if (PatrolHelicopter != null)
                {
                    if (simulateDeath)
                        PatrolHelicopter.Hurt(PatrolHelicopter.health * 2f, DamageType.Generic, null, false);
                    else
                        PatrolHelicopter.Kill();
                }
            }

            #endregion Kill

            #region Targeting

            private void UpdateTargetList()
            {
                // This will hold whichever player we decide to strafe, if any.
                BasePlayer potentialStrafeTarget = null;
                // Indicates whether we have already picked someone to strafe during this update cycle.
                bool foundStrafeOpportunity = false;
                // Tells us if we should be using napalm rockets in the strafe.
                bool useNapalmInStrafe = false;
                // Track the highest "danger zone" score among potential targets, to find the best one.
                float highestDangerZoneScore = 0f;
                // Store whichever target is leading in the "danger zone" check.
                PatrolHelicopterAI.targetinfo bestDangerZoneCandidate = null;
                // Our config says if we are allowed to strafe players near their own bases or not.
                bool canStrafeNearEnemyBases = TierData.Strafe.CanStrafePlayersNearEnemyBases;
                // How many seconds to hold onto a missing or unseen target before dropping them.
                float unseenTargetTimeout = TierData.Targeting.SecondsBeforeDroppingUnseenTargets;

                // =====================================================
                // 1) Clean up old or invalid targets in the AI's list
                // =====================================================
                for (int i = PatrolHelicopterAi._targetList.Count - 1; i >= 0; i--)
                {
                    PatrolHelicopterAI.targetinfo targetInfo = PatrolHelicopterAi._targetList[i];

                    // If the target record is null or the entity is invalid, remove it right away.
                    if (targetInfo == null || !targetInfo.ent.IsValid())
                    {
                        PatrolHelicopterAi.RemoveTargetAt(i);
                        continue;
                    }

                    // If the helicopter is using danger zones, we skip any target that is physically inside a "no go" zone.
                    else if (PatrolHelicopterAI.use_danger_zones &&
                             PatrolHelicopterAi.IsInNoGoZone(targetInfo.ply.transform.position))
                    {
                        PatrolHelicopterAi.RemoveTargetAt(i);
                        continue;
                    }

                    // If AI is ignoring players or if the target is in the global “ignore list,” remove them.
                    else if (ConVar.AI.ignoreplayers ||
                             Rust.Ai.SimpleAIMemory.PlayerIgnoreList.Contains(targetInfo.ply))
                    {
                        PatrolHelicopterAi.RemoveTargetAt(i);
                        continue;
                    }

                    // Otherwise, let's do some further checks
                    else
                    {
                        // Update the last time we had clear line-of-sight on this target
                        PatrolHelicopterAi.UpdateTargetLineOfSightTime(targetInfo);

                        // Evaluate if the target is dead OR we haven't seen them for 6+ seconds
                        bool targetIsDead = (targetInfo.ent.Health() <= 0f);
                        if (targetInfo.ply != null)
                            targetIsDead = targetInfo.ply.IsDead();

                        if (targetInfo.TimeSinceSeen() >= unseenTargetTimeout || targetIsDead)
                        {
                            // See if we do a last-second strafe as they’re “dropping off” the list.
                            bool shouldCheckLastSecondStrafe = ChanceSucceeded(TierData.Targeting.ChanceOfFinalStrafeBeforeDroppingTarget);

                            // Are they near an enemy base (and we may or may not strafe such a location).
                            bool isNearEnemyBase = PlayerUtil.NearEnemyBase(targetInfo.ply);

                            // This next block says: If we haven’t found a strafe yet, we can strafe, and the target
                            // is still “locked” by the helicopter guns, we may do a last-second strafe.
                            if ((PatrolHelicopterAi.CanStrafe() || PatrolHelicopterAi.CanUseNapalm()) &&
                                PatrolHelicopterAi.IsAlive() &&
                                !foundStrafeOpportunity &&
                                !targetIsDead &&
                                (PatrolHelicopterAi.leftGun._target == targetInfo.ply ||
                                 PatrolHelicopterAi.rightGun._target == targetInfo.ply) &&
                                shouldCheckLastSecondStrafe)
                            {
                                // If the target isn't near an enemy base OR we allow strafe near enemy bases
                                if (!isNearEnemyBase || canStrafeNearEnemyBases)
                                {
                                    // Decide if napalm rockets are used
                                    useNapalmInStrafe =
                                        (!PatrolHelicopterAi.ValidRocketTarget(targetInfo.ply) ||
                                          Random.Range(0f, 1f) > 0.75f);

                                    foundStrafeOpportunity = true;
                                    potentialStrafeTarget = targetInfo.ply;
                                }
                            }

                            // Removing the target now that we have processed its final checks.
                            PatrolHelicopterAi.RemoveTargetAt(i);

                            // If either helicopter gun was specifically targeting this player, clear it out.
                            if (PatrolHelicopterAi.leftGun._target == targetInfo.ply)
                                PatrolHelicopterAi.leftGun._target = null;
                            if (PatrolHelicopterAi.rightGun._target == targetInfo.ply)
                                PatrolHelicopterAi.rightGun._target = null;
                        }
                        else
                        {
                            // If the target is still valid and visible, check if they're in a "danger zone"
                            // with a high enough score to trigger a strafe.
                            PatrolHelicopterAI.DangerZone dangerZone;
                            bool canStrafeOrNapalm = (PatrolHelicopterAi.CanStrafe() || PatrolHelicopterAi.CanUseNapalm());
                            float timeSinceLastNapalm = Time.realtimeSinceStartup - PatrolHelicopterAi.lastNapalmTime;
                            float timeSinceLastStrafe = Time.realtimeSinceStartup - PatrolHelicopterAi.lastStrafeTime;
                            bool canTriggerDangerStrafe = (timeSinceLastNapalm > 20f || timeSinceLastStrafe > 15f);

                            if (PatrolHelicopterAI.use_danger_zones &&
                                !foundStrafeOpportunity &&
                                canStrafeOrNapalm &&
                                PatrolHelicopterAi.IsAlive() &&
                                canTriggerDangerStrafe &&
                                PatrolHelicopterAi.IsInDangerZone(targetInfo.ply.transform.position, out dangerZone) &&
                                dangerZone != null &&
                                dangerZone.Score > highestDangerZoneScore)
                            {
                                // If the best danger zone so far is overshadowed by this one, store it
                                bool isNearEnemyBase = PlayerUtil.NearEnemyBase(targetInfo.ply);
                                if (!isNearEnemyBase || canStrafeNearEnemyBases)
                                {
                                    highestDangerZoneScore = dangerZone.Score;
                                    bestDangerZoneCandidate = targetInfo;
                                }
                            }
                        }
                    }
                }

                // =====================================================
                // 2) If we haven't found a strafe yet, see if
                //    there's a "best danger zone" target we can strafe
                // =====================================================
                if (PatrolHelicopterAI.use_danger_zones &&
                    !foundStrafeOpportunity &&
                    bestDangerZoneCandidate != null)
                {
                    bool isNearEnemyBase = PlayerUtil.NearEnemyBase(bestDangerZoneCandidate.ply);
                    if (!isNearEnemyBase || canStrafeNearEnemyBases)
                    {
                        // Possibly use napalm
                        useNapalmInStrafe =
                            (!PatrolHelicopterAi.ValidRocketTarget(bestDangerZoneCandidate.ply) ||
                              Random.Range(0f, 1f) > 0.75f);

                        foundStrafeOpportunity = true;
                        potentialStrafeTarget = bestDangerZoneCandidate.ply;
                    }
                }

                // =====================================================
                // 3) Add new nearby players into the target list
                // =====================================================
                AddNewTargetsToList();

                // =====================================================
                // 4) If we found a strafe target, force the helicopter
                //    to strafe them right now
                // =====================================================
                if (foundStrafeOpportunity &&
                    !PatrolHelicopterAi.isRetiring &&
                    !PatrolHelicopterAi.isDead)
                {
                    PatrolHelicopterAi.ExitCurrentState();
                    PatrolHelicopterAi.State_Strafe_Enter(potentialStrafeTarget, useNapalmInStrafe);
                }
            }

            public void AddNewTargetsToList()
            {
                // If the AI is told to ignore all players globally, don't even look
                if (!ConVar.AI.ignoreplayers)
                {
                    using (PooledList<BasePlayer> nearbyPlayers = Facepunch.Pool.Get<PooledList<BasePlayer>>())
                    {
                        // Collect all players within 150 units of this helicopter's position
                        BaseEntity.Query.Server.GetPlayersInSphere(
                            PatrolHelicopter.transform.position,
                            TierData.Targeting.TargetAcquisitionRange,
                            nearbyPlayers,
                            BaseEntity.Query.DistanceCheckType.None,
                            false
                        );

                        // We loop through each candidate player
                        foreach (BasePlayer candidatePlayer in nearbyPlayers)
                        {
                            // Skip if they're in the global AI memory ignore list
                            if (Rust.Ai.SimpleAIMemory.PlayerIgnoreList.Contains(candidatePlayer))
                                continue;

                            // Skip if the player is inside a safe zone
                            if (candidatePlayer.InSafeZone())
                                continue;

                            // Skip if the player is in a tutorial
                            if (candidatePlayer.IsInTutorial)
                                continue;

                            // If using "danger zones," skip if they are in a no-go zone
                            if (PatrolHelicopterAI.use_danger_zones &&
                                PatrolHelicopterAi.IsInNoGoZone(candidatePlayer.transform.position))
                            {
                                continue;
                            }

                            if (!CanTargetPlayer(candidatePlayer))
                                continue;

                            // Finally, if they aren't already in the AI target list, they have
                            // a "threat level" over 0.5, and the helicopter can "see" them,
                            // we add them to the target list.
                            if (!PatrolHelicopterAi.IsAlreadyInTargets(candidatePlayer) &&
                                candidatePlayer.GetThreatLevel() > 0.5f &&
                                PatrolHelicopterAi.PlayerVisible(candidatePlayer))
                            {
                                PatrolHelicopterAi.TryAddTarget(candidatePlayer);
                            }
                        }
                    }
                }
            }

            public bool CanTargetPlayer(BasePlayer player)
            {
                if (player == null)
                    return false;

                if (CallProfile != null && CallProfile.LockOnCaller && CallingPlayer != null)
                {
                    if (player == CallingPlayer)
                        return true;

                    if (CallProfile.IncludeCallerAllies && PlayerUtil.AreAllies(player.userID, CallingPlayer.userID))
                        return true;

                    return false;
                }

                if (TierData.Targeting != null && TierData.Targeting.OnlyRetaliateIfAttacked == true)
                {
                    AttackerInfo info = FindAttackerInfo(player);
                    if (info == null)
                        return false;
                }

                return true;
            }

            public bool CanDamage(BasePlayer attacker)
            {
                if (CallProfile == null || !CallProfile.LockOnCaller)
                    return true;

                if (attacker == null)
                    return true;

                if (CallProfile.IncludeCallerAllies)
                {
                    if (attacker == CallingPlayer)
                        return true;

                    if (PlayerUtil.AreAllies(attacker.userID, CallingPlayer.userID))
                        return true;

                    return false;
                }
                else
                {
                    return (attacker == CallingPlayer);
                }
            }

            public void OnDamageTaken(BasePlayer attacker, HitInfo hitInfo)
            {
                if (attacker == null)
                    return;

                AttackerInfo info = GetOrCreateAttackerInfo(attacker);
                if (info != null)
                {
                    info.TotalDamage += hitInfo.damageTypes.Total();
                    info.LastHitTime = Time.realtimeSinceStartup;
                }
            }

            #endregion Targeting

            #region Attacker Info

            private AttackerInfo GetOrCreateAttackerInfo(BasePlayer player)
            {
                if (player == null)
                    return null;

                foreach (AttackerInfo info in _attackerInfos)
                {
                    if (info.Player == player)
                        return info;
                }

                AttackerInfo newInfo = new AttackerInfo(player);
                _attackerInfos.Add(newInfo);
                return newInfo;
            }

            private AttackerInfo FindAttackerInfo(BasePlayer player)
            {
                if (player == null)
                    return null;

                foreach (AttackerInfo info in _attackerInfos)
                {
                    if (info.Player == player)
                    {
                        return info;
                    }
                }
                return null;
            }

            public class AttackerInfo
            {
                public BasePlayer Player;
                public float TotalDamage;
                public float LastHitTime;

                public AttackerInfo(BasePlayer player)
                {
                    Player = player;
                    TotalDamage = 0f;
                    LastHitTime = 0f;
                }
            }

            #endregion Attacker Info

            #region Lock On

            public BasePlayer GetPreferredLockOnTarget()
            {
                if (PatrolHelicopter == null)
                    return null;

                if (CallProfile == null || !CallProfile.LockOnCaller)
                    return null;

                if (CallProfile.IncludeCallerAllies)
                {
                    if (CallingPlayer == null)
                        return null;

                    List<ulong> allyIds = PlayerUtil.GetAllies(CallingPlayer.userID);
                    if (allyIds != null && allyIds.Count > 0)
                    {
                        List<BasePlayer> livingAllies = Pool.Get<List<BasePlayer>>();
                        try
                        {
                            foreach (ulong id in allyIds)
                            {
                                BasePlayer ally = PlayerUtil.FindById(id);
                                if (ally != null && !PlayerUtil.Offline(ally.userID) && ally.IsAlive() && !ally.IsDead() && !ally.InSafeZone())
                                {
                                    livingAllies.Add(ally);
                                }
                            }

                            if (livingAllies.Count == 0)
                            {
                                return null;
                            }

                            return GetHighestScoringTarget(livingAllies);
                        }
                        finally
                        {
                            livingAllies.Clear();
                            Pool.FreeUnmanaged(ref livingAllies);
                            Pool.FreeUnmanaged(ref allyIds);
                        }
                    }
                    else
                    {
                        if (CallingPlayer != null && !CallingPlayer.IsDead() && !CallingPlayer.InSafeZone())
                        {
                            return CallingPlayer;
                        }
                        return null;
                    }
                }
                else
                {
                    if (CallingPlayer != null && !CallingPlayer.IsDead() && !CallingPlayer.InSafeZone())
                    {
                        return CallingPlayer;
                    }
                    return null;
                }
            }

            private BasePlayer GetHighestScoringTarget(List<BasePlayer> candidates)
            {
                Vector3 helicopterPosition = PatrolHelicopter.transform.position;

                BasePlayer bestCandidate = null;
                float highestPointsSoFar = 0f;
                bool hasChosenCandidate = false;

                foreach (BasePlayer candidate in candidates)
                {
                    if (candidate == null)
                        continue;

                    AttackerInfo info = FindAttackerInfo(candidate);
                    float candidatePoints = 0f;

                    //--------------------------------
                    // 1) Distance Condition
                    //--------------------------------
                    float distance = Vector3.Distance(helicopterPosition, candidate.transform.position);
                    if (distance < 100f)
                    {
                        candidatePoints += 5f;
                    }
                    else if (distance < 200f)
                    {
                        candidatePoints += 3f;
                    }

                    //--------------------------------
                    // 2) Total Damage Dealt Condition
                    //--------------------------------
                    float totalDamage = 0f;
                    if (info != null)
                        totalDamage = info.TotalDamage;

                    if (totalDamage >= 150f)
                    {
                        candidatePoints += 10f;
                    }
                    else if (totalDamage >= 50f)
                    {
                        candidatePoints += 5f;
                    }

                    //--------------------------------
                    // 3) Time Since Last Hit Condition
                    //--------------------------------
                    if (info != null)
                    {
                        float timeSinceLastAttack = Time.realtimeSinceStartup - info.LastHitTime;
                        if (timeSinceLastAttack <= 10f)
                        {
                            candidatePoints += 5f;
                        }
                    }

                    //--------------------------------
                    // 4) Player Health Condition
                    //--------------------------------
                    float candidateHealth = candidate.health;
                    if (candidateHealth < 30f)
                    {
                        candidatePoints += 2f;
                    }

                    //--------------------------------
                    // 5) Wounded Condition
                    //--------------------------------
                    if (PlayerUtil.Wounded(candidate))
                    {
                        candidatePoints += 2f;
                    }

                    //--------------------------------
                    // 6) Swimming Condition
                    //--------------------------------
                    if (PlayerUtil.Swimming(candidate))
                    {
                        candidatePoints += 2f;
                    }

                    //--------------------------------
                    // Compare to the best so far
                    //--------------------------------
                    if (!hasChosenCandidate || candidatePoints > highestPointsSoFar)
                    {
                        hasChosenCandidate = true;
                        highestPointsSoFar = candidatePoints;
                        bestCandidate = candidate;
                    }
                }

                if (!hasChosenCandidate)
                    return null;

                return bestCandidate;
            }

            #endregion Lock On

            #region PVE

            public bool CanHurtEntity(BaseEntity victim)
            {
                if (CallProfile == null || !CallProfile.LockOnCaller)
                    return true;

                if (TierData.PVE == null || !TierData.PVE.BlockDamageToNonCallerPlayers)
                    return true;

                BasePlayer victimPlayer = victim as BasePlayer;
                if (victimPlayer != null)
                {
                    if (CallProfile.IncludeCallerAllies)
                    {
                        if (victimPlayer == CallingPlayer)
                            return true;

                        if (PlayerUtil.AreAllies(victimPlayer.userID, CallingPlayer.userID))
                            return true;

                        return false;
                    }
                    else
                    {
                        return (victimPlayer == CallingPlayer);
                    }
                }

                if (TierData.PVE.BlockDamageToNonCallerOwnedEntities)
                {
                    if (victim is BaseEntity)
                    {
                        ulong ownerId = victim.OwnerID;
                        if (ownerId == 0UL)
                            return false;

                        if (ownerId == CallingPlayer.userID)
                            return true;

                        if (CallProfile.IncludeCallerAllies
                            && PlayerUtil.AreAllies(ownerId, CallingPlayer.userID))
                        {
                            return true;
                        }

                        return false;
                    }
                }

                return true;
            }

            #endregion PVE

            #region Danger and No-Go Zones

            private List<PatrolHelicopterAI.DangerZone> _dangerZones = new List<PatrolHelicopterAI.DangerZone>();

            public void OnDangerZoneAdded(PatrolHelicopterAI.DangerZone zone)
            {
                if (!_dangerZones.Contains(zone))
                    _dangerZones.Add(zone);
            }

            public void OnDangerZoneRemoved(PatrolHelicopterAI.DangerZone zone)
            {
                _dangerZones.Remove(zone);
            }

            public bool HasDangerZone(PatrolHelicopterAI.DangerZone zone)
            {
                return _dangerZones.Contains(zone);
            }

            private float _noGoCheckInterval = 5f;
            private float _nextNoGoCheckTime = 0f;

            private void RemoveExpiredNoGoZones()
            {
                if (TierData == null || TierData.DangerZone == null)
                    return;

                float expireTime = TierData.DangerZone.SecondsBeforeNoGoZoneExpires;
                if (expireTime <= 0f)
                    return;

                for (int i = PatrolHelicopterAi.noGoZones.Count - 1; i >= 0; i--)
                {
                    var zone = PatrolHelicopterAi.noGoZones[i];
                    float timeSinceActive = Time.realtimeSinceStartup - zone.LastActiveTime;

                    if (timeSinceActive > expireTime)
                    {
                        PatrolHelicopterAi.noGoZones.RemoveAt(i);
                    }
                }
            }

            #endregion Danger and No-Go Zones

            #region No Go Monuments

            public bool IsNoGoMonument(MonumentInfo monument)
            {
                if (monument == null || string.IsNullOrEmpty(monument.name))
                    return false;

                List<string> list = TierData.Patrol.NoGoMonuments;
                if (list == null || list.Count == 0)
                    return false;

                string monumentNameLower = monument.name.ToLower();
                foreach (string badName in list)
                {
                    if (!string.IsNullOrEmpty(badName)
                        && monumentNameLower.Contains(badName.ToLower()))
                    {
                        return true;
                    }
                }

                return false;
            }

            #endregion No Go Monuments

            #region Helper Functions

            public void SetInitialDestination(Vector3 destination)
            {
                PatrolHelicopterAi.hasInterestZone = true;
                PatrolHelicopterAi.interestZoneOrigin = destination;
                PatrolHelicopterAi.ExitCurrentState();
                PatrolHelicopterAi.State_Move_Enter(destination);
            }

            #endregion Helper Functions

            #region Debug

            private void Debug(BasePlayer debugPlayer)
            {
                if (debugPlayer == null)
                    return;

                float drawDuration = 0.005f;

                bool TurretFiring(HelicopterTurret turret)
                {
                    if (turret == null)
                        return false;

                    float now = Time.time;
                    float endOfBurst = turret.lastBurstTime + turret.burstLength;
                    return (now >= turret.lastBurstTime && now <= endOfBurst);
                }

                bool LaunchingRockets()
                {
                    float timeSinceLastRocket = Time.realtimeSinceStartup - PatrolHelicopterAi.lastRocketTime;
                    return (timeSinceLastRocket < 1f);
                }

                float mainRotorHp = -1f, mainRotorMax = -1f;
                float tailRotorHp = -1f, tailRotorMax = -1f;
                float engineHp = -1f, engineMax = -1f;

                if (PatrolHelicopter.weakspots != null)
                {
                    foreach (var ws in PatrolHelicopter.weakspots)
                    {
                        if (ws.bonenames.Any(b => b.Contains("main_rotor_col")))
                        {
                            mainRotorHp = ws.health;
                            mainRotorMax = ws.maxHealth;
                        }
                        else if (ws.bonenames.Any(b => b.Contains("tail_rotor_col")))
                        {
                            tailRotorHp = ws.health;
                            tailRotorMax = ws.maxHealth;
                        }
                        else if (ws.bonenames.Any(b => b.Contains("engine_col")))
                        {
                            engineHp = ws.health;
                            engineMax = ws.maxHealth;
                        }
                    }
                }

                {
                    Vector3 heliPos = PatrolHelicopter.transform.position;
                    float distToDest = Vector3.Distance(heliPos, PatrolHelicopterAi.destination);

                    int totalTargets = PatrolHelicopterAi._targetList.Count;
                    int totalAttackers = _attackerInfos.Count;

                    string tierName = "Unknown";
                    if (TierData != null && TierData.Name != null)
                    {
                        tierName = TierData.Name;
                    }

                    float bodyHp = PatrolHelicopter.health;
                    float maxBodyHp = PatrolHelicopter.MaxHealth();

                    int dangerCount = PatrolHelicopterAi.dangerZones.Count;
                    int noGoCount = PatrolHelicopterAi.noGoZones.Count;
                    int maxDangerZones = 0;
                    if (TierData != null && TierData.DangerZone != null)
                    {
                        maxDangerZones = TierData.DangerZone.MaximumAllowedDangerZones;
                    }

                    var targetNames = new List<string>();
                    foreach (var t in PatrolHelicopterAi._targetList.Take(4))
                    {
                        if (t != null && t.ply != null && t.ply.displayName != null)
                            targetNames.Add(t.ply.displayName);
                        else
                            targetNames.Add("Unknown");
                    }

                    var attackerNames = new List<string>();
                    foreach (var a in _attackerInfos.Take(4))
                    {
                        if (a != null && a.Player != null && a.Player.displayName != null)
                            attackerNames.Add(a.Player.displayName);
                        else
                            attackerNames.Add("Unknown");
                    }

                    bool lockOn = (CallProfile != null && CallProfile.LockOnCaller);
                    string lockedOnLabel = "Locked On: No";
                    if (lockOn)
                    {
                        lockedOnLabel = "Locked On: Yes";

                        if (CallingPlayer != null)
                        {
                            lockedOnLabel += $" (Caller: {CallingPlayer.displayName})";
                        }
                        else
                        {
                            lockedOnLabel += " (Caller: Unknown)";
                        }
                    }

                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"<size=45>Tiered Helicopter ({tierName})</size>");
                    sb.AppendLine($"<size=38>State: {PatrolHelicopterAi._currentState}</size>");
                    sb.AppendLine($"<size=38>{lockedOnLabel}</size>");
                    sb.AppendLine($"<size=38>Remaining Lifetime: {RemainingLifetimeSeconds:F1}s</size>");
                    sb.AppendLine($"<size=38>Destination Distance: {distToDest:F1}m</size>");
                    sb.AppendLine($"<size=38>Targets: {totalTargets} ({string.Join(", ", targetNames)})</size>");
                    sb.AppendLine($"<size=38>Attackers: {totalAttackers} ({string.Join(", ", attackerNames)})</size>");
                    sb.AppendLine($"<size=38>Danger Zones: {dangerCount}/{maxDangerZones} | No-Go Zones: {noGoCount}</size>");

                    sb.AppendLine($"<size=38>Body HP: {bodyHp:F1}/{maxBodyHp:F1}</size>");
                    if (mainRotorHp >= 0)
                        sb.AppendLine($"<size=38>Main Rotor: {mainRotorHp:F1}/{mainRotorMax:F1}</size>");
                    if (tailRotorHp >= 0)
                        sb.AppendLine($"<size=38>Tail Rotor: {tailRotorHp:F1}/{tailRotorMax:F1}</size>");
                    if (engineHp >= 0)
                        sb.AppendLine($"<size=38>Engine: {engineHp:F1}/{engineMax:F1}</size>");

                    Vector3 overheadPos = heliPos + Vector3.up * 45f;
                    DrawUtil.Text(debugPlayer, drawDuration, Color.green, overheadPos, sb.ToString());
                }

                {
                    Vector3 fromPos = PatrolHelicopter.transform.position;
                    Vector3 toPos = PatrolHelicopterAi.destination;

                    DrawUtil.Line(debugPlayer, drawDuration, Color.green, fromPos, toPos);

                    Vector3 labelPos = toPos + Vector3.up * 2f;
                    DrawUtil.Text(debugPlayer, drawDuration, Color.green, labelPos, "<size=38>AI Destination</size>");
                }

                if (PatrolHelicopterAi._currentState == PatrolHelicopterAI.aiState.STRAFE ||
                    PatrolHelicopterAi._currentState == PatrolHelicopterAI.aiState.ORBITSTRAFE)
                {
                    Vector3 strafePos = PatrolHelicopterAi.strafe_target_position;
                    bool isNapalm = PatrolHelicopterAi.useNapalm;

                    DrawUtil.Sphere(debugPlayer, drawDuration, Color.red, strafePos, 4f);

                    string label = "Strafe Target (Normal)";
                    if (isNapalm)
                    {
                        label = "Strafe Target (Napalm)";
                    }

                    DrawUtil.Text(debugPlayer, drawDuration, Color.red, strafePos + Vector3.up * 3f, $"<size=38>{label}</size>");
                }

                {
                    float startHealth = PatrolHelicopterAi.helicopterBase.startHealth;
                    float fleePct = PatrolHelicopterAI.flee_damage_percentage;
                    if (TierData != null && TierData.DangerZone != null)
                    {
                        fleePct = TierData.DangerZone.FleeDamagePercentage;
                    }
                    float threshold = startHealth * (fleePct / 100f);

                    float dangerExpire = 0f;
                    if (TierData != null && TierData.DangerZone != null)
                    {
                        dangerExpire = TierData.DangerZone.SecondsBeforeDangerZoneExpires;
                    }
                    float noGoExpire = 0f;
                    if (TierData != null && TierData.DangerZone != null)
                    {
                        noGoExpire = TierData.DangerZone.SecondsBeforeNoGoZoneExpires;
                    }

                    foreach (var zone in PatrolHelicopterAi.dangerZones)
                    {
                        bool isStale = zone.IsStale();
                        Color zoneColor = Color.red;
                        if (isStale)
                        {
                            zoneColor = Color.gray;
                        }

                        Vector3 center = zone.Centre;
                        float radius = zone.Radius;
                        DrawUtil.Sphere(debugPlayer, drawDuration, zoneColor, center, radius);

                        float timeSinceActive = Time.realtimeSinceStartup - zone.LastActiveTime;
                        float timeLeft = -1f;
                        if (dangerExpire > 0f)
                        {
                            timeLeft = dangerExpire - timeSinceActive;
                        }

                        float score = zone.Score;
                        float toNoGo = threshold - score;

                        string damageNeededText = "No-Go threshold reached!";
                        if (toNoGo > 0)
                        {
                            damageNeededText = $"Damage for No-Go (Threshold: {fleePct}%): {toNoGo:F1}";
                        }

                        string staleNote = "";
                        if (isStale)
                        {
                            staleNote = "\nStale (Inactive)";
                        }

                        string expireText = "";
                        if (timeLeft > 0f)
                        {
                            expireText = $"Expire In: {timeLeft:F1}s";
                        }
                        else if (dangerExpire > 0f)
                        {
                            expireText = "Expire In: 0s";
                        }

                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine("<size=38>Danger Zone" + staleNote + "</size>");
                        sb.AppendLine($"<size=38>Score: {score:F1}</size>");
                        sb.AppendLine($"<size=38>{damageNeededText}</size>");
                        sb.AppendLine($"<size=38>Last Active: {timeSinceActive:F1}s</size>");
                        if (!string.IsNullOrEmpty(expireText))
                            sb.AppendLine($"<size=38>{expireText}</size>");

                        DrawUtil.Text(debugPlayer, drawDuration, zoneColor, center + Vector3.up * 2f, sb.ToString());
                    }

                    foreach (var zone in PatrolHelicopterAi.noGoZones)
                    {
                        Color noGoColor = Color.magenta;

                        Vector3 center = zone.Centre;
                        float radius = zone.Radius;
                        DrawUtil.Sphere(debugPlayer, drawDuration, noGoColor, center, radius);

                        float timeSinceActive = Time.realtimeSinceStartup - zone.LastActiveTime;
                        float timeLeft = -1f;
                        if (noGoExpire > 0f)
                        {
                            timeLeft = noGoExpire - timeSinceActive;
                        }

                        string expireText = "";
                        if (timeLeft > 0f)
                        {
                            expireText = $"Expire In: {timeLeft:F1}s";
                        }
                        else if (noGoExpire > 0f)
                        {
                            expireText = "Expire In: 0s";
                        }

                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine("<size=38>No-Go Zone</size>");
                        sb.AppendLine($"<size=38>Score: {zone.Score:F1}</size>");
                        sb.AppendLine($"<size=38>Last Active: {timeSinceActive:F1}s</size>");
                        if (!string.IsNullOrEmpty(expireText))
                            sb.AppendLine($"<size=38>{expireText}</size>");

                        DrawUtil.Text(debugPlayer, drawDuration, noGoColor, center + Vector3.up * 2f, sb.ToString());
                    }
                }

                {
                    float now = Time.time;

                    if (PatrolHelicopterAi.leftGun != null &&
                        PatrolHelicopterAi.leftGun.HasTarget() &&
                        TurretFiring(PatrolHelicopterAi.leftGun))
                    {
                        var turret = PatrolHelicopterAi.leftGun;
                        var targetPly = turret._target as BasePlayer;
                        if (targetPly != null && targetPly.transform != null)
                        {
                            Vector3 muzzlePos = turret.muzzleTransform.position;
                            Vector3 targetPos = targetPly.transform.position + Vector3.up * 0.25f;

                            float endOfBurst = turret.lastBurstTime + turret.burstLength;
                            float timeLeft = Mathf.Max(0f, endOfBurst - now);

                            DrawUtil.Arrow(debugPlayer, drawDuration, Color.yellow, muzzlePos, targetPos, 1.2f);
                            DrawUtil.Text(debugPlayer, drawDuration, Color.yellow, muzzlePos, $"<size=38>Left Turret Firing ({timeLeft:F1}s)</size>");
                        }
                    }

                    if (PatrolHelicopterAi.rightGun != null &&
                        PatrolHelicopterAi.rightGun.HasTarget() &&
                        TurretFiring(PatrolHelicopterAi.rightGun))
                    {
                        var turret = PatrolHelicopterAi.rightGun;
                        var targetPly = turret._target as BasePlayer;
                        if (targetPly != null && targetPly.transform != null)
                        {
                            Vector3 muzzlePos = turret.muzzleTransform.position;
                            Vector3 targetPos = targetPly.transform.position + Vector3.up * 0.25f;

                            float endOfBurst = turret.lastBurstTime + turret.burstLength;
                            float timeLeft = Mathf.Max(0f, endOfBurst - now);

                            DrawUtil.Arrow(debugPlayer, drawDuration, Color.yellow, muzzlePos, targetPos, 1.2f);
                            DrawUtil.Text(debugPlayer, drawDuration, Color.yellow, muzzlePos, $"<size=38>Right Turret Firing ({timeLeft:F1}s)</size>");
                        }
                    }
                }

                {
                    var aiState = PatrolHelicopterAi._currentState;
                    if ((aiState == PatrolHelicopterAI.aiState.STRAFE || aiState == PatrolHelicopterAI.aiState.ORBITSTRAFE)
                        && PatrolHelicopterAi.numRocketsLeft > 0)
                    {
                        bool firingNow = LaunchingRockets();

                        Transform rocketTubeTrans = PatrolHelicopterAi.helicopterBase.rocket_tube_right.transform;
                        if (PatrolHelicopterAi.leftTubeFiredLast)
                        {
                            rocketTubeTrans = PatrolHelicopterAi.helicopterBase.rocket_tube_left.transform;
                        }

                        Vector3 offsetPos = rocketTubeTrans.position + (Vector3.down * 1.5f);

                        string rocketMsg = $"Rockets Left: {PatrolHelicopterAi.numRocketsLeft}";
                        if (firingNow)
                        {
                            rocketMsg = $"Tube Launching ({PatrolHelicopterAi.numRocketsLeft} left)";
                        }

                        DrawUtil.Text(debugPlayer, drawDuration, Color.yellow, offsetPos, $"<size=38>{rocketMsg}</size>");
                    }
                }

                {
                    Vector3 heliPos = PatrolHelicopter.transform.position;
                    float range = 150f;
                    if (TierData != null && TierData.Targeting != null)
                    {
                        range = TierData.Targeting.TargetAcquisitionRange;
                    }
                    DrawUtil.Sphere(debugPlayer, drawDuration, Color.yellow, heliPos, range);

                    var matchedAttackers = new HashSet<BasePlayer>();

                    foreach (var tinfo in PatrolHelicopterAi._targetList)
                    {
                        if (tinfo == null || tinfo.ply == null)
                            continue;

                        BasePlayer ply = tinfo.ply;

                        float distance = Vector3.Distance(heliPos, ply.transform.position);
                        Vector3 plyPos = ply.transform.position + Vector3.up * 2.5f;

                        float timeSinceSeen;
                        if (float.IsInfinity(tinfo.lastSeenTime))
                        {
                            timeSinceSeen = -1f;
                        }
                        else
                        {
                            float dt = Time.realtimeSinceStartup - tinfo.lastSeenTime;
                            timeSinceSeen = Mathf.Max(0f, dt);
                        }

                        bool isCaller = (CallingPlayer != null && ply == CallingPlayer);
                        bool isCallerTeammate = (CallingPlayer != null && CallProfile != null && CallProfile.IncludeCallerAllies
                                             && PlayerUtil.AreTeammates(ply.userID, CallingPlayer.userID));

                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"<size=38>{ply.displayName}{(isCaller ? " [CALLER]" : (isCallerTeammate ? " [TEAM]" : ""))}</size>");
                        sb.AppendLine($"<size=38>Distance: {distance:F1}m</size>");

                        if (timeSinceSeen < 0)
                            sb.AppendLine("<size=38>Last Seen: Never</size>");
                        else
                            sb.AppendLine($"<size=38>Last Seen: {timeSinceSeen:F1}s ago</size>");

                        sb.AppendLine($"<size=38>Visible For: {tinfo.visibleFor:F1}s</size>");

                        bool leftGunTarget = (PatrolHelicopterAi.leftGun != null && PatrolHelicopterAi.leftGun._target == ply);
                        bool rightGunTarget = (PatrolHelicopterAi.rightGun != null && PatrolHelicopterAi.rightGun._target == ply);
                        if (leftGunTarget || rightGunTarget)
                        {
                            string gunLabel;
                            if (leftGunTarget && rightGunTarget)
                            {
                                gunLabel = "Left and Right";
                            }
                            else if (leftGunTarget)
                            {
                                gunLabel = "Left";
                            }
                            else
                            {
                                gunLabel = "Right";
                            }

                            sb.AppendLine($"<size=38>Machine Gun Target: {gunLabel}</size>");
                        }

                        var attackerInfo = FindAttackerInfo(ply);
                        if (attackerInfo != null)
                        {
                            matchedAttackers.Add(ply);
                            float timeSinceHit = Time.realtimeSinceStartup - attackerInfo.LastHitTime;
                            sb.AppendLine($"<size=38>Attacker: Damage: {attackerInfo.TotalDamage:F1}, Last Hit: {timeSinceHit:F1}s ago</size>");
                        }

                        DrawUtil.Text(debugPlayer, drawDuration, Color.white, plyPos, sb.ToString());
                    }

                    foreach (var attacker in _attackerInfos)
                    {
                        if (attacker == null || attacker.Player == null)
                            continue;

                        if (matchedAttackers.Contains(attacker.Player))
                            continue;

                        BasePlayer ply = attacker.Player;
                        float distance = Vector3.Distance(heliPos, ply.transform.position);
                        Vector3 plyPos = ply.transform.position + Vector3.up * 2.5f;
                        float timeSinceHit = Time.realtimeSinceStartup - attacker.LastHitTime;

                        bool isCaller = (CallingPlayer != null && ply == CallingPlayer);
                        bool isCallerTeammate = (CallingPlayer != null && CallProfile != null && CallProfile.IncludeCallerAllies
                                             && PlayerUtil.AreTeammates(ply.userID, CallingPlayer.userID));

                        var sb = new System.Text.StringBuilder();

                        string namePart = ply.displayName;
                        if (isCaller)
                        {
                            namePart += " [CALLER]";
                        }
                        else if (isCallerTeammate)
                        {
                            namePart += " [TEAM]";
                        }

                        sb.AppendLine($"<size=38>{namePart}</size>");
                        sb.AppendLine("<size=38>Attacker (Not Targeted)</size>");
                        sb.AppendLine($"<size=38>Distance: {distance:F1}m</size>");
                        sb.AppendLine($"<size=38>Damage: {attacker.TotalDamage:F1}</size>");
                        sb.AppendLine($"<size=38>Last Hit: {timeSinceHit:F1}s ago</size>");

                        DrawUtil.Text(debugPlayer, drawDuration, Color.red, plyPos, sb.ToString());
                    }
                }
            }

            #endregion Debug
        }

        #endregion Tiered Helicopter Component

        #region Tiered Helicopter Manager

        public class TieredHelicopterManager
        {
            private readonly HashSet<TieredHelicopterComponent> _tieredHelicopters = new HashSet<TieredHelicopterComponent>();
            private readonly Dictionary<string, HelicopterSpawnerComponent> _helicopterSpawners = new Dictionary<string, HelicopterSpawnerComponent>();
            private readonly Dictionary<string, List<FireBall>> _fireBalls = new Dictionary<string, List<FireBall>>();
            private readonly Dictionary<string, List<LootContainer>> _crates = new Dictionary<string, List<LootContainer>>();
            private readonly Dictionary<string, List<HelicopterDebris>> _debris = new Dictionary<string, List<HelicopterDebris>>();

            public int GetTotalTieredHelicopters()
            {
                return _tieredHelicopters.Count;
            }

            public void Unload()
            {
                foreach (TieredHelicopterComponent tieredHelicopter in _tieredHelicopters)
                {
                    if (tieredHelicopter != null && tieredHelicopter.PatrolHelicopter != null)
                        tieredHelicopter.Kill(simulateDeath: false);
                }
                _tieredHelicopters.Clear();

                foreach (HelicopterSpawnerComponent spawner in _helicopterSpawners.Values)
                {
                    if (spawner != null)
                        spawner.Destroy();
                }
                _helicopterSpawners.Clear();

                foreach (List<FireBall> fireBalls in _fireBalls.Values)
                {
                    foreach (FireBall fb in fireBalls)
                    {
                        if (fb != null)
                            fb.Kill();
                    }
                }
                _fireBalls.Clear();

                foreach (List<LootContainer> crates in _crates.Values)
                {
                    foreach (LootContainer crate in crates)
                    {
                        if (crate != null)
                            crate.Kill();
                    }
                }
                _crates.Clear();

                foreach (List<HelicopterDebris> debriss in _debris.Values)
                {
                    foreach (HelicopterDebris d in debriss)
                    {
                        if (d != null)
                            d.Kill();
                    }
                }
                _debris.Clear();
            }

            public void LoadTiers()
            {
                _plugin.Puts("Initializing helicopter tiers...");

                string[] filePaths = DataFileUtil.GetAllFilePaths();
                int enabledCount = 0;

                foreach (string filePath in filePaths)
                {
                    // Skip non-tier files stored alongside tiers (e.g. CHT/Cooldowns.json).
                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    if (string.IsNullOrEmpty(fileName) ||
                        fileName.Equals("Cooldowns", StringComparison.OrdinalIgnoreCase) ||
                        fileName.Equals("CustomHelicopterTiers2", StringComparison.OrdinalIgnoreCase))
                        continue;

                    TierData tierData = DataFileUtil.LoadIfExists<TierData>(filePath);
                    if (tierData == null || string.IsNullOrEmpty(tierData.Name))
                        continue;

                    bool changed = DataMigration.MigrateToLatest(tierData);
                    if (changed)
                    {
                        DataFileUtil.Save(filePath, tierData);
                        _plugin.Puts($"Data file for tier '{tierData.Name}' was updated to schema version {tierData.SchemaVersion}.");
                    }

                    if (!tierData.Enabled)
                    {
                        _plugin.Puts($"Tier '{tierData.Name}' is disabled.");
                        continue;
                    }

                    HelicopterSpawnerComponent spawner = HelicopterSpawnerComponent.Create(tierData, this);
                    RegisterSpawner(tierData.Name, spawner);
                    enabledCount++;

                    _plugin.Puts($"Tier '{tierData.Name}' is enabled.");

                    if (tierData.CallProfiles != null && tierData.CallProfiles.Count > 0)
                    {
                        foreach (CallProfileData profile in tierData.CallProfiles)
                        {
                            profile.InitializePermission(tierData.Name);
                            profile.InitializePriceList();
                        }

                        _plugin.Puts($"  Tier '{tierData.Name}' has {tierData.CallProfiles.Count} call definitions:");
                        foreach (CallProfileData profile in tierData.CallProfiles)
                        {
                            _plugin.Puts($"    - Suffix: '{profile.Suffix}', Permission: '{profile.Permission}'");
                        }
                    }
                    else
                    {
                        _plugin.Puts($"  Tier '{tierData.Name}' does not have call definitions listed.");
                    }
                }

                _plugin.Puts($"Setup is complete. {enabledCount} tier(s) are enabled.");
            }

            public TieredHelicopterComponent CreateTieredHelicopter(PatrolHelicopter patrolHelicopter, TierData tierData, BasePlayer callingPlayer = null)
            {
                TieredHelicopterComponent tieredHelicopter = TieredHelicopterComponent.Install(patrolHelicopter, tierData, this, callingPlayer);
                _tieredHelicopters.Add(tieredHelicopter);
                return tieredHelicopter;
            }

            public void HandleTieredHelicopterDestroyed(TieredHelicopterComponent tieredHelicopter)
            {
                _tieredHelicopters.Remove(tieredHelicopter);

                if (_helicopterSpawners.TryGetValue(tieredHelicopter.TierData.Name, out HelicopterSpawnerComponent spawner))
                {
                    spawner.OnHelicopterRetired(tieredHelicopter.PatrolHelicopter);
                }
            }

            public HelicopterSpawnerComponent GetSpawnerByTierName(string tierName)
            {
                if (_helicopterSpawners.TryGetValue(tierName, out HelicopterSpawnerComponent spawner))
                    return spawner;

                return null;
            }

            public TieredHelicopterComponent GetTieredComponentForHelicopter(PatrolHelicopter patrolHelicopter)
            {
                return TieredHelicopterComponent.GetComponent(patrolHelicopter);
            }

            public IEnumerable<TieredHelicopterComponent> GetAllTieredHelicopters()
            {
                return _tieredHelicopters;
            }

            public IEnumerable<HelicopterSpawnerComponent> GetAllSpawners()
            {
                return _helicopterSpawners.Values;
            }

            public void RegisterSpawner(string tierName, HelicopterSpawnerComponent spawner)
            {
                _helicopterSpawners[tierName] = spawner;
            }

            public TierData GetTierDataByTierName(string tierName)
            {
                HelicopterSpawnerComponent spawner = GetSpawnerByTierName(tierName);
                if (spawner != null)
                {
                    return spawner.TierData;
                }
                return null;
            }

            public TieredHelicopterComponent GetTieredHelicopterByTierName(string tierName)
            {
                if (string.IsNullOrEmpty(tierName))
                    return null;

                foreach (TieredHelicopterComponent tieredHelicopter in _tieredHelicopters)
                {
                    if (tieredHelicopter != null && tieredHelicopter.TierData != null &&
                        tieredHelicopter.TierData.Name.Equals(tierName, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        return tieredHelicopter;
                    }
                }
                return null;
            }

            public TieredHelicopterComponent GetTieredHelicopterByFireBall(FireBall fireBall)
            {
                if (fireBall == null)
                    return null;

                foreach (var kvp in _fireBalls)
                {
                    string tierName = kvp.Key;
                    List<FireBall> fireBalls = kvp.Value;
                    if (fireBalls != null && fireBalls.Contains(fireBall))
                    {
                        foreach (TieredHelicopterComponent tieredHelicopter in _tieredHelicopters)
                        {
                            if (tieredHelicopter != null && tieredHelicopter.TierData.Name == tierName)
                                return tieredHelicopter;
                        }
                    }
                }
                return null;
            }

            public List<FireBall> GetFireBallsByTierName(string tierName)
            {
                if (_fireBalls.TryGetValue(tierName, out var list))
                    return list;
                return null;
            }

            public List<LootContainer> GetCratesByTierName(string tierName)
            {
                if (_crates.TryGetValue(tierName, out var list))
                    return list;
                return null;
            }

            public List<HelicopterDebris> GetDebrisByTierName(string tierName)
            {
                if (_debris.TryGetValue(tierName, out var list))
                    return list;
                return null;
            }

            public void AddFireBallToTier(string tierName, FireBall fb)
            {
                if (!_fireBalls.TryGetValue(tierName, out var list))
                {
                    list = new List<FireBall>();
                    _fireBalls[tierName] = list;
                }
                list.Add(fb);
            }

            public void AddCrateToTier(string tierName, LootContainer crate)
            {
                if (!_crates.TryGetValue(tierName, out var list))
                {
                    list = new List<LootContainer>();
                    _crates[tierName] = list;
                }
                list.Add(crate);
            }

            public void AddDebrisToTier(string tierName, HelicopterDebris debris)
            {
                if (!_debris.TryGetValue(tierName, out var list))
                {
                    list = new List<HelicopterDebris>();
                    _debris[tierName] = list;
                }
                list.Add(debris);
            }

            public string GetTierNameByDebris(HelicopterDebris debris)
            {
                foreach (var kvp in _debris)
                {
                    if (kvp.Value.Contains(debris))
                        return kvp.Key;
                }
                return null;
            }

            public TierData GetRandomTierData()
            {
                HelicopterSpawnerComponent chosenSpawner = null;
                int seenCount = 0;

                foreach (var kvp in _helicopterSpawners)
                {
                    HelicopterSpawnerComponent spawner = kvp.Value;
                    if (spawner == null)
                        continue;

                    seenCount++;
                    if (Random.Range(0, seenCount) == 0)
                    {
                        chosenSpawner = spawner;
                    }
                }

                if (chosenSpawner == null)
                    return null;

                return chosenSpawner.TierData;
            }
        }

        #endregion Tiered Helicopter Manager

        #region Helicopter Spawner Component

        public class HelicopterSpawnerComponent : FacepunchBehaviour
        {
            public TierData TierData;
            public int CurrentPopulation
            {
                get
                {
                    return _spawnedHelicopters.Count;
                }
            }
            private TieredHelicopterManager _manager;
            private readonly List<PatrolHelicopter> _spawnedHelicopters = new List<PatrolHelicopter>();

            public static HelicopterSpawnerComponent Create(TierData tierData, TieredHelicopterManager manager)
            {
                HelicopterSpawnerComponent spawner = new GameObject().AddComponent<HelicopterSpawnerComponent>();
                spawner.Initialize(tierData, manager);
                return spawner;
            }

            private void Initialize(TierData tierData, TieredHelicopterManager manager)
            {
                TierData = tierData;
                _manager = manager;
            }

            public void Destroy()
            {
                DestroyImmediate(this);
            }

            private void Start()
            {
                if (!TierData.Spawn.EnableAutomatedSpawns)
                    return;

                float initialSpawnTime = GetNextSpawnTime();
                if (TierData.Spawn.InitialSpawn)
                    initialSpawnTime = 0f;

                if (initialSpawnTime <= 0f)
                    _plugin.Puts($"Tier '{TierData.Name}' will attempt the first spawn now.");
                else
                    _plugin.Puts($"Tier '{TierData.Name}' will attempt the first spawn in {initialSpawnTime / 60f:F1} minute(s).");

                Invoke(nameof(ScheduledSpawn), initialSpawnTime);
            }

            private void OnDestroy()
            {
                CancelInvoke(nameof(ScheduledSpawn));

                foreach (PatrolHelicopter patrolHelicopter in _spawnedHelicopters)
                {
                    if (patrolHelicopter != null && !patrolHelicopter.IsDestroyed)
                        patrolHelicopter.Kill();
                }
                _spawnedHelicopters.Clear();
            }

            public bool PopulationLimitReached()
            {
                return CurrentPopulation >= TierData.Spawn.MaximumPopulation;
            }

            private void ScheduledSpawn()
            {
                if (CurrentPopulation < TierData.Spawn.MaximumPopulation)
                {
                    int numberToSpawn = Random.Range(
                        TierData.Spawn.MinimumNumberToSpawnPerTick,
                        TierData.Spawn.MaximumNumberToSpawnPerTick + 1
                    );

                    var (success, explanation, spawnedCount) = TrySpawn(numberToSpawn);
                    _plugin.Puts($"[Scheduled Spawn] {explanation}");
                }

                // Reschedule with a fresh random interval (InvokeRepeating froze the first random delay).
                float next = GetNextSpawnTime();
                if (next < 1f) next = 1f;
                Invoke(nameof(ScheduledSpawn), next);
            }

            public (bool success, string explanation, int spawnedCount) TrySpawn(int requestedCount, BasePlayer callingPlayer = null)
            {
                string tierName = TierData.Name;

                int finalUsedCount = Mathf.Min(
                    requestedCount,
                    TierData.Spawn.MaximumPopulation - CurrentPopulation,
                    _config.GlobalPopulationLimit - _manager.GetTotalTieredHelicopters()
                );

                if (finalUsedCount <= 0)
                {
                    string msg =
                        $"All helicopter spawns for tier '{tierName}' failed.\n" +
                        "Cannot spawn more helicopters. Population limit reached.";

                    return (false, msg, 0);
                }

                var attemptLogs = new List<string>();
                int successCount = 0;

                for (int i = 0; i < finalUsedCount; i++)
                {
                    int attemptNumber = i + 1;

                    Vector3 spawnPosition = FindSpawnPosition(callingPlayer);
                    if (spawnPosition == Vector3.zero)
                    {
                        attemptLogs.Add($"Helicopter #{attemptNumber} failed: no valid spawn position found.");
                        continue;
                    }

                    PatrolHelicopter patrolHelicopter = GameManager.server.CreateEntity(PREFAB_HELICOPTER, spawnPosition) as PatrolHelicopter;
                    if (patrolHelicopter == null)
                    {
                        attemptLogs.Add($"Helicopter #{attemptNumber} failed: entity creation returned null.");
                        continue;
                    }

                    patrolHelicopter.GetComponent<PatrolHelicopterAI>().hasInterestZone = true;
                    patrolHelicopter.Spawn();

                    OnHelicopterSpawned(patrolHelicopter);
                    TieredHelicopterComponent tieredHelicopter = _manager.CreateTieredHelicopter(patrolHelicopter, TierData, callingPlayer);
                    if (tieredHelicopter != null && callingPlayer != null)
                    {
                        Vector3 destination = callingPlayer.transform.position + new Vector3(0f, 20f, 0f);
                        tieredHelicopter.SetInitialDestination(destination);
                    }

                    successCount++;
                    attemptLogs.Add($"Helicopter #{attemptNumber} spawned at {spawnPosition}.");
                }

                if (successCount == 0)
                {
                    string msg = $"All helicopter spawns for tier '{tierName}' failed.\n" + string.Join("\n", attemptLogs);
                    return (false, msg, 0);
                }

                if (finalUsedCount < requestedCount || successCount < finalUsedCount)
                {
                    var partialReasons = new List<string>();

                    if (finalUsedCount < requestedCount)
                    {
                        partialReasons.Add($"Population limit reduced your request from {requestedCount} to {finalUsedCount}.");
                    }

                    if (successCount < finalUsedCount)
                    {
                        partialReasons.Add($"Some spawn attempts failed (see below).");
                    }

                    string partialReasonsCombined = string.Join(" ", partialReasons);

                    string msg =
                        $"Partially succeeded for tier '{tierName}': {successCount}/{requestedCount} requested.\n" +
                        $"{partialReasonsCombined}\n" +
                        string.Join("\n", attemptLogs);

                    return (true, msg, successCount);
                }

                {
                    string msg =
                        $"All {successCount} helicopter(s) out of {requestedCount} requested for tier '{tierName}' spawned successfully.\n" +
                        string.Join("\n", attemptLogs);
                    return (true, msg, successCount);
                }
            }

            private float GetNextSpawnTime()
            {
                return Random.Range(TierData.Spawn.MinimumRespawnDelayMinutes, TierData.Spawn.MaximumRespawnDelayMinutes) * 60f;
            }

            public void OnHelicopterRetired(PatrolHelicopter patrolHelicopter)
            {
                _spawnedHelicopters.Remove(patrolHelicopter);
            }

            public void OnHelicopterSpawned(PatrolHelicopter patrolHelicopter)
            {
                _spawnedHelicopters.Add(patrolHelicopter);
            }

            private Vector3 FindSpawnPosition(BasePlayer caller = null)
            {
                int maxAttempts = 1000;
                Vector3 selectedPosition = Vector3.zero;

                if (caller != null)
                {
                    float minRadius = TierData.Spawn.MinimumSpawnRadiusFromCaller;
                    float maxRadius = TierData.Spawn.MaximumSpawnRadiusFromCaller;

                    if (minRadius <= 0f)
                        minRadius = 500f;

                    if (maxRadius <= 0f)
                        maxRadius = 700f;

                    if (maxRadius < minRadius + 1f)
                        maxRadius = minRadius + 1f;

                    Vector3 candidate = TerrainUtil.GetRandomPositionAround(caller.transform.position, minRadius, maxRadius, adjustToWaterHeight: false, adjustToTerrainHeight: true);

                    candidate.y += 100f;
                    return candidate;
                }

                List<string> spawnLocations = TierData.Spawn.SpawnLocations;

                SpawnLocation locationToUse = SpawnLocation.Ocean;
                if (spawnLocations != null && spawnLocations.Count > 0)
                {
                    int index = Random.Range(0, spawnLocations.Count);
                    string locationString = spawnLocations[index];

                    if (!Enum.TryParse(locationString, true, out locationToUse))
                    {
                        locationToUse = SpawnLocation.Ocean;
                    }
                }

                if (locationToUse == SpawnLocation.Ocean)
                {
                    for (int attempt = 0; attempt < maxAttempts; attempt++)
                    {
                        Vector3 candidate = TerrainUtil.GetRandomOceanPatrolPoint();
                        if (TerrainUtil.InWater(candidate))
                        {
                            candidate.y += 50f;
                            selectedPosition = candidate;
                            break;
                        }
                    }
                }
                else if (locationToUse == SpawnLocation.Mainland)
                {
                    int halfWorldSize = ConVar.Server.worldsize / 2;

                    for (int attempt = 0; attempt < maxAttempts; attempt++)
                    {
                        Vector3 candidate = new Vector3
                        {
                            x = Random.Range(-halfWorldSize, halfWorldSize),
                            z = Random.Range(-halfWorldSize, halfWorldSize)
                        };

                        candidate.y = TerrainMeta.HeightMap.GetHeight(candidate);

                        if (!TerrainUtil.InWater(candidate))
                        {
                            candidate.y += 50f;
                            selectedPosition = candidate;
                            break;
                        }
                    }
                }

                return selectedPosition;
            }
        }

        #endregion Helicopter Spawner Component

        #region Helicopter Crate Lock Component

        public class HelicopterCrateLockComponent : FacepunchBehaviour
        {
            public ulong CallerId;
            public bool IncludeTeam;
        }

        #endregion Helicopter Crate Lock Component

        #region Death Commands

        private void RunDeathCommands(TierData tierData, BasePlayer caller, Vector3 deathPosition)
        {
            if (tierData == null || tierData.DeathCommandSets == null || tierData.DeathCommandSets.Count == 0)
                return;

            if (tierData.RunRandomDeathCommandSet)
            {
                CommandSetData set = tierData.DeathCommandSets.GetRandom();
                if (set != null && set.Commands != null)
                {
                    foreach (var cmd in set.Commands)
                    {
                        ExecuteDeathCommand(cmd, caller, deathPosition, tierData.Name);
                    }
                }
            }
            else
            {
                foreach (var set in tierData.DeathCommandSets)
                {
                    foreach (var cmd in set.Commands)
                        ExecuteDeathCommand(cmd, caller, deathPosition, tierData.Name);
                }
            }
        }

        private void ExecuteDeathCommand(CommandData cmdData, BasePlayer player, Vector3 position, string tierName)
        {
            if (cmdData == null)
                return;

            string processed = ReplacePlaceholders(cmdData.Command, player, position, tierName);

            switch (cmdData.Type)
            {
                case CommandType.Chat:
                    if (player != null)
                        player.Command($"chat.say \"{processed}\"");
                    break;

                case CommandType.Client:
                    if (player != null)
                        player.Command(processed);
                    break;

                case CommandType.Server:
                    Server.Command(processed);
                    break;
            }
        }

        private string ReplacePlaceholders(string text, BasePlayer player, Vector3 position, string tierName)
        {
            string playerName = "Unknown";
            if (player != null)
                playerName = player.displayName;

            string playerId = "0";
            if (player != null)
                playerId = player.UserIDString;

            string grid = MapHelper.PositionToString(position);
            string x = position.x.ToString("F1");
            string y = position.y.ToString("F1");
            string z = position.z.ToString("F1");

            return text
                .Replace("{PlayerName}", playerName)
                .Replace("{PlayerId}", playerId)
                .Replace("{PositionX}", x)
                .Replace("{PositionY}", y)
                .Replace("{PositionZ}", z)
                .Replace("{Grid}", grid)
                .Replace("{TierName}", tierName);
        }

        #endregion Death Commands

        #region Enums

        public enum Rarity
        {
            Common,
            Uncommon,
            Rare,
            VeryRare
        }

        public enum Speed
        {
            VerySlow,
            Slow,
            Normal,
            Fast,
            VeryFast
        }

        public enum SpawnLocation
        {
            Ocean,
            Mainland
        }

        public enum CommandType
        {
            Chat,
            Server,
            Client
        }

        #endregion Enums

        #region Permissions
        private static class PermissionUtil
        {
            public const string ADMIN = "customhelicoptertiers2.admin";
            private static readonly List<string> _permissions = new List<string> { ADMIN };
            public static IEnumerable<string> Permissions => _permissions;
            public static void RegisterPermissions() { foreach (var p in _permissions) global::CHT.PermissionsBridge.RegisterPermission(p); }
            public static bool HasPermission(BasePlayer player, string permissionName) => global::CHT.PermissionsBridge.UserHasPermission(player, permissionName);
            public static void AddPermission(string permission, bool register = true) { if (!_permissions.Contains(permission)) { _permissions.Add(permission); if (register) global::CHT.PermissionsBridge.RegisterPermission(permission); } }
        }
        #endregion Permissions

        #region 3rd Party Integration
        public static class AlphaLootUtil
        {
            public static bool PopulateLoot(LootContainer container, string profileName) => global::CHT.AlphaLootBridge.PopulateLoot(container, profileName);
            public static bool ProfileExists(string name) => global::CHT.AlphaLootBridge.ProfileExists(name);
        }
        public static class SkillTreeUtil
        {
            public static void AwardSkillTreeXP(BasePlayer player, double xpAmount)
            {
                if (player != null && xpAmount > 0) global::CHT.SkillTreeBridge.AwardXP((ulong)player.userID, xpAmount, "CustomHelicopterTiers2", false);
            }
        }
        #endregion 3rd Party Integration

        #region Payment Gateways

        public enum PaymentGatewayType
        {
            Unknown,
            Item,
            Coin,
        }

        public interface IPaymentGateway
        {
            bool Available { get; }

            int Get(BasePlayer player);

            void Give(BasePlayer player, int amount);

            void Deduct(BasePlayer player, int amount);
        }

        public class ItemPaymentGateway : IPaymentGateway
        {
            private int _itemId;

            public ItemPaymentGateway(int itemId)
            {
                _itemId = itemId;
            }

            public bool Available
            {
                get { return true; }
            }

            public int Get(BasePlayer player)
            {
                return player.inventory.GetAmount(_itemId);
            }

            public void Give(BasePlayer player, int amount)
            {
                player.GiveItem(ItemManager.CreateByItemID(_itemId, amount));
                player.Command("note.inv", _itemId, +amount);
            }

            public void Deduct(BasePlayer player, int amount)
            {
                player.inventory.Take(null, _itemId, amount);
                player.Command("note.inv", _itemId, -amount);
            }
        }

        public class CoinPaymentGateway : IPaymentGateway
        {
            public CoinPaymentGateway(CustomHelicopterTiers2 plugin) { }
            public bool Available => global::CHT.EconomicsBridge.IsAvailable;
            public int Get(BasePlayer player) => global::CHT.EconomicsBridge.Balance(player);
            public void Give(BasePlayer player, int amount) => global::CHT.EconomicsBridge.Deposit(player, amount);
            public void Deduct(BasePlayer player, int amount) => global::CHT.EconomicsBridge.Withdraw(player, amount);
        }

        #endregion Payment Gateways

        #region Helper Functions

        private static string Today => DateTime.UtcNow.ToString("yyyy-MM-dd");

        private static double UtcNowSeconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private void RemoveLegacyRealtimeCooldowns()
        {
            if (_cooldownData == null || _cooldownData.NextCallTimes == null || _cooldownData.NextCallTimes.Count == 0)
                return;

            foreach (var kvUser in _cooldownData.NextCallTimes.ToArray())
            {
                bool hasLegacy = kvUser.Value.Values.Any(t => t < 1e8);
                if (hasLegacy)
                    _cooldownData.NextCallTimes.Remove(kvUser.Key);
            }

            DataFileUtil.Save(DataFileUtil.CooldownPath, _cooldownData);
        }

        private void BroadcastHelicopterCall(CallProfileData callProfile, TierData tier, BasePlayer caller)
        {
            if (callProfile == null)
                return;

            Vector3 callerPosition = Vector3.zero;
            if (caller != null)
                callerPosition = caller.transform.position;

            string personalised = ReplacePlaceholders(
                callProfile.PersonalMessage,
                caller,
                callerPosition,
                tier.Name);

            string global = ReplacePlaceholders(
                callProfile.GlobalMessage,
                caller,
                callerPosition,
                tier.Name);

            if (!string.IsNullOrWhiteSpace(personalised) && caller != null)
                MessagePlayer(caller, personalised);

            if (!string.IsNullOrWhiteSpace(global))
                foreach (var activePlayer in BasePlayer.activePlayerList)
                {
                    MessagePlayer(activePlayer, global);
                }
        }

        private int GetDailyCallsUsed(BasePlayer player, CallProfileData profile)
        {
            if (profile.DailyCallLimit <= 0)
                return 0;

            if (!_cooldownData.DailyCallCounts.TryGetValue(player.userID, out var perPerm))
                return 0;

            if (_cooldownData.DailyCallDate.TryGetValue(player.userID, out var perPermDate))
            {
                if (!perPermDate.TryGetValue(profile.Permission, out var lastDay) || lastDay != Today)
                    return 0;
            }

            return perPerm.TryGetValue(profile.Permission, out var count) ? count : 0;
        }

        private void IncrementDailyCalls(BasePlayer player, CallProfileData profile)
        {
            if (profile.DailyCallLimit <= 0)
                return;

            var id = player.userID;
            if (!_cooldownData.DailyCallCounts.TryGetValue(id, out var perPerm))
                _cooldownData.DailyCallCounts[id] = perPerm = new Dictionary<string, int>();

            if (!_cooldownData.DailyCallDate.TryGetValue(id, out var perPermDate))
                _cooldownData.DailyCallDate[id] = perPermDate = new Dictionary<string, string>();

            if (!perPermDate.TryGetValue(profile.Permission, out var lastDay) || lastDay != Today)
            {
                perPerm[profile.Permission] = 0;
                perPermDate[profile.Permission] = Today;
            }

            perPerm[profile.Permission]++;
            DataFileUtil.Save(DataFileUtil.CooldownPath, _cooldownData);
        }

        private double GetTimeLeftUntilHeliCall(BasePlayer player, CallProfileData callProfile)
        {
            if (callProfile.CooldownMinutes <= 0f)
                return 0;

            if (!_cooldownData.NextCallTimes.TryGetValue(player.userID, out var userCooldownDict))
                return 0;

            if (!userCooldownDict.TryGetValue(callProfile.Permission, out double nextAllowed))
                return 0;

            double now = UtcNowSeconds();
            if (now >= nextAllowed)
                return 0;

            double remainingSeconds = nextAllowed - now;
            return remainingSeconds;
        }

        private void SetNextAllowedHeliCallTime(BasePlayer player, CallProfileData callProfile)
        {
            if (callProfile.CooldownMinutes <= 0f)
                return;

            double newAllowed = UtcNowSeconds() + (callProfile.CooldownMinutes * 60.0);

            if (!_cooldownData.NextCallTimes.TryGetValue(player.userID, out var userCooldownDict))
            {
                userCooldownDict = new Dictionary<string, double>();
                _cooldownData.NextCallTimes[player.userID] = userCooldownDict;
            }

            userCooldownDict[callProfile.Permission] = newAllowed;
            DataFileUtil.Save(DataFileUtil.CooldownPath, _cooldownData);
        }

        private bool CheckAffordability(BasePlayer player, CallProfileData callProfile)
        {
            if (callProfile == null || callProfile.CostToCall == null)
                return true;

            foreach (CurrencyData currency in callProfile.CostToCall)
            {
                if (!currency.Enabled)
                    continue;

                if (!currency.Valid)
                    continue;

                if (!currency.CanAfford(player))
                    return false;
            }
            return true;
        }

        private bool PopulationLimitReached(TierData tierData)
        {
            HelicopterSpawnerComponent spawner = _tieredHelicopterManager.GetSpawnerByTierName(tierData.Name);
            if (spawner == null)
                return false;

            return spawner.PopulationLimitReached() || _tieredHelicopterManager.GetTotalTieredHelicopters() >= _config.GlobalPopulationLimit;
        }

        private CallProfileData GetCallProfileFor(BasePlayer player, TierData tierData)
        {
            if (tierData == null || tierData.CallProfiles == null)
                return null;

            CallProfileData bestPermitted = null;
            CallProfileData bestPurchasable = null;

            foreach (CallProfileData profile in tierData.CallProfiles)
            {
                if (profile == null || !profile.Enabled)
                    continue;

                if (string.IsNullOrEmpty(profile.Permission))
                    continue;

                bool hasPerm = player != null &&
                    (PermissionUtil.HasPermission(player, profile.Permission) ||
                     PermissionUtil.HasPermission(player, PermissionUtil.ADMIN));

                if (hasPerm)
                {
                    if (bestPermitted == null || profile.Priority > bestPermitted.Priority)
                        bestPermitted = profile;
                    continue;
                }

                // Economy-gated public profiles: enabled cost (coin/item) lets anyone see/attempt a call.
                if (HasEnabledPurchaseCost(profile))
                {
                    if (bestPurchasable == null || profile.Priority > bestPurchasable.Priority)
                        bestPurchasable = profile;
                }
            }

            return bestPermitted ?? bestPurchasable;
        }

        private static bool HasEnabledPurchaseCost(CallProfileData profile)
        {
            if (profile?.CostToCall == null)
                return false;

            foreach (CurrencyData currency in profile.CostToCall)
            {
                if (currency == null || !currency.Enabled || string.IsNullOrEmpty(currency.Name))
                    continue;

                // Listing gate: do not require Economics to already be Available (may load later).
                if (currency.Name.IndexOf("coin", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (currency.ItemDef != null)
                    return true;
            }

            return false;
        }

        private PatrolHelicopter FindHelicopterInVicinity(Vector3 position, float radius = 10f, bool mustBeTiered = true)
        {
            List<PatrolHelicopter> nearbyPatrolHelis = Pool.Get<List<PatrolHelicopter>>();
            Vis.Entities(position, radius, nearbyPatrolHelis);

            PatrolHelicopter foundHeli = null;
            foreach (PatrolHelicopter heli in nearbyPatrolHelis)
            {
                if (heli == null)
                    continue;

                if (!mustBeTiered)
                {
                    foundHeli = heli;
                    break;
                }

                TieredHelicopterComponent tieredHelicopter = TieredHelicopterComponent.GetComponent(heli);
                if (tieredHelicopter != null)
                {
                    foundHeli = heli;
                    break;
                }
            }

            Pool.FreeUnmanaged(ref nearbyPatrolHelis);
            return foundHeli;
        }

        private BasePlayer FindPlayerByPartialNameOrId(string searchTerm)
        {
            List<BasePlayer> foundPlayers = new List<BasePlayer>();
            searchTerm = searchTerm.ToLower();

            foreach (var p in BasePlayer.activePlayerList)
            {
                if (p.UserIDString == searchTerm)
                {
                    return p;
                }
                else if (p.displayName.ToLower().Contains(searchTerm))
                {
                    foundPlayers.Add(p);
                }
            }

            if (foundPlayers.Count == 1)
                return foundPlayers[0];
            else
                return null;
        }

        private static List<T> FindChildrenOfType<T>(BaseEntity parentEntity, string prefabName = null) where T : BaseEntity
        {
            List<T> foundChildren = new List<T>();
            foreach (BaseEntity child in parentEntity.children)
            {
                T childOfType = child as T;
                if (childOfType != null && (prefabName == null || child.PrefabName == prefabName))
                    foundChildren.Add(childOfType);
            }

            return foundChildren;
        }

        private static bool PluginLoaded(Plugin plugin)
        {
            if (plugin != null && plugin.IsLoaded)
                return true;
            else
                return false;
        }

        public static bool ChanceSucceeded(int percent)
        {
            if (percent <= 0)
                return false;

            if (percent >= 100)
                return true;

            float roll = Random.Range(0f, 100f);
            return roll < percent;
        }

        /// <summary>Used by top-level Harmony combat patches (danger-zone stale checks).</summary>
        public IEnumerable<TieredHelicopterComponent> GetAllLiveTieredHelicopters()
        {
            return _tieredHelicopterManager != null
                ? _tieredHelicopterManager.GetAllTieredHelicopters()
                : Array.Empty<TieredHelicopterComponent>();
        }

        public static void RunEffectAttachedToEntity(string effectName, BaseEntity attachedEntity, uint entityBoneID = 0u, Vector3 localPosition = default(Vector3),
            Vector3 localNormal = default(Vector3), Connection suppressFor = null, bool sendToAll = false, List<Connection> recipients = null)
        {
            Effect.server.Run(effectName, attachedEntity, entityBoneID, localPosition, localNormal, suppressFor, sendToAll, recipients);
        }

        private string FormatTime(double totalSeconds)
        {
            int total = (int)System.Math.Floor(totalSeconds);
            if (total < 1)
            {
                return "0s";
            }

            int hours = total / 3600;
            int remainder = total % 3600;
            int minutes = remainder / 60;
            int secs = remainder % 60;

            var parts = new List<string>();
            if (hours > 0)
            {
                parts.Add($"{hours}h");
            }
            if (minutes > 0)
            {
                parts.Add($"{minutes}m");
            }
            if (secs > 0)
            {
                parts.Add($"{secs}s");
            }

            if (parts.Count == 0)
            {
                return "0s";
            }

            return string.Join(" ", parts);
        }

        #endregion Helper Functions

        #region Helper Classes

        public static class TerrainUtil
        {
            public static bool OnTerrain(Vector3 position, float radius)
            {
                return Physics.CheckSphere(position, radius, Layers.Mask.Terrain, QueryTriggerInteraction.Ignore);
            }

            public static bool InNoBuildZone(Vector3 position, float radius)
            {
                return Physics.CheckSphere(position, radius, Layers.Mask.Prevent_Building);
            }

            public static bool InWater(Vector3 position)
            {
                return WaterLevel.Test(position, false, false);
            }

            public static bool InWater(Vector3 position, float minDepth)
            {
                WaterLevel.WaterInfo info = WaterLevel.GetWaterInfo(position, waves: true, volumes: true, null);

                if (!info.isValid)
                    return false;

                if (info.currentDepth >= minDepth)
                    return true;

                return false;
            }

            public static bool InsideRock(Vector3 position, float radius)
            {
                List<Collider> colliders = Pool.Get<List<Collider>>();
                Vis.Colliders(position, radius, colliders, Layers.Mask.World, QueryTriggerInteraction.Ignore);

                bool result = false;

                foreach (Collider collider in colliders)
                {
                    if (collider.name.Contains("rock", CompareOptions.OrdinalIgnoreCase)
                        || collider.name.Contains("cliff", CompareOptions.OrdinalIgnoreCase)
                        || collider.name.Contains("formation", CompareOptions.OrdinalIgnoreCase))
                    {
                        result = true;
                        break;
                    }
                }

                Pool.FreeUnmanaged(ref colliders);
                return result;
            }

            public static bool InRadTown(Vector3 position, bool shouldDisplayOnMap = false)
            {
                foreach (var monumentInfo in TerrainMeta.Path.Monuments)
                {
                    bool inBounds = monumentInfo.IsInBounds(position);

                    bool hasLandMarker = true;
                    if (shouldDisplayOnMap)
                        hasLandMarker = monumentInfo.shouldDisplayOnMap;

                    if (inBounds && hasLandMarker)
                        return true;
                }

                return false;
            }

            public static bool HasEntityNearby(Vector3 position, float radius, LayerMask mask, string prefabName = null)
            {
                List<Collider> hitColliders = Pool.Get<List<Collider>>();
                GamePhysics.OverlapSphere(position, radius, hitColliders, mask, QueryTriggerInteraction.Ignore);

                bool hasEntityNearby = false;
                foreach (Collider collider in hitColliders)
                {
                    BaseEntity entity = collider.gameObject.ToBaseEntity();
                    if (entity != null)
                    {
                        if (prefabName == null || entity.PrefabName == prefabName)
                        {
                            hasEntityNearby = true;
                            break;
                        }
                    }
                }

                Pool.FreeUnmanaged(ref hitColliders);
                return hasEntityNearby;
            }

            public static string GetBiome(Vector3 position)
            {
                return "Unknown";
            }

            public static string GetNearbyLandmark(Vector3 position)
            {
                GameObject[] allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
                GameObject closestObject = null;
                float closestDistance = -1;

                foreach (GameObject gobject in allObjects)
                {
                    if (!gobject.name.Contains("autospawn/monument"))
                        continue;

                    float distance = Vector3.Distance(gobject.transform.position, position);

                    if (closestDistance == -1 || distance < closestDistance)
                    {
                        closestObject = gobject;
                        closestDistance = distance;
                    }
                }

                if (closestObject != null)
                {
                    string simpleName = ExtractPrefabSimpleName(closestObject.name);

                    if (!string.IsNullOrEmpty(simpleName))
                        return simpleName;
                }

                return string.Empty;
            }

            public static string ExtractPrefabSimpleName(string fullPrefabName)
            {
                int lastSlashIndex = fullPrefabName.LastIndexOf("/", StringComparison.Ordinal);
                if (lastSlashIndex >= 0 && lastSlashIndex < fullPrefabName.Length - 1)
                {
                    fullPrefabName = fullPrefabName.Substring(lastSlashIndex + 1);
                }

                if (fullPrefabName.EndsWith(".prefab"))
                {
                    fullPrefabName = fullPrefabName.Remove(fullPrefabName.Length - ".prefab".Length);
                }

                return fullPrefabName;
            }

            public static Vector3 GetRandomPointOffshore()
            {
                Vector3 position = TerrainMeta.RandomPointOffshore();
                position.y = TerrainMeta.WaterMap.GetHeight(position);

                return position;
            }

            public static Vector3 GetRandomOceanPatrolPoint()
            {
                var nodes = TerrainMeta.Path.OceanPatrolFar;

                if (nodes == null || nodes.Count == 0)
                    return Vector3.zero;

                Vector3 point = nodes[Random.Range(0, nodes.Count)];
                return point;
            }

            public static Vector3 GetRandomPositionAround(Vector3 position, float minimumRadius, float maximumRadius, bool adjustToWaterHeight = false, bool adjustToTerrainHeight = false)
            {
                Vector3 randomDirection = Random.insideUnitSphere.normalized;

                float randomDistance = Random.Range(minimumRadius, maximumRadius);

                Vector3 randomPosition = position + randomDirection * randomDistance;

                if (adjustToWaterHeight)
                    randomPosition.y = TerrainMeta.WaterMap.GetHeight(randomPosition);
                else if (adjustToTerrainHeight)
                    randomPosition.y = TerrainMeta.HeightMap.GetHeight(randomPosition);

                return randomPosition;
            }

            public static Vector3 GetPositionOnCircle(Vector3 position, float radius, float angle)
            {
                float radians = angle * Mathf.Deg2Rad;
                return new Vector3(position.x + Mathf.Cos(radians) * radius, position.y, position.z + Mathf.Sin(radians) * radius);
            }

            public static bool HasLineOfSight(Vector3 startPosition, Vector3 endPosition, LayerMask mask)
            {
                return GamePhysics.LineOfSight(startPosition, endPosition, mask);
            }

            public static bool GetGroundInfo(Vector3 startPosition, out RaycastHit raycastHit, float range, LayerMask mask)
            {
                return Physics.Linecast(startPosition + new Vector3(0.0f, range, 0.0f), startPosition - new Vector3(0.0f, range, 0.0f), out raycastHit, mask);
            }

            public static bool GetGroundInfo(Vector3 startPosition, out RaycastHit raycastHit, float range, LayerMask mask, Transform ignoreTransform = null)
            {
                startPosition.y += 0.25f;
                range += 0.25f;
                raycastHit = default;

                RaycastHit hit;
                if (!GamePhysics.Trace(new Ray(startPosition, Vector3.down), 0f, out hit, range, mask, QueryTriggerInteraction.UseGlobal, null))
                    return false;

                if (ignoreTransform != null && hit.collider != null
                    && (hit.collider.transform == ignoreTransform || hit.collider.transform.IsChildOf(ignoreTransform)))
                {
                    return GetGroundInfo(startPosition - new Vector3(0f, 0.01f, 0f), out raycastHit, range, mask, ignoreTransform);
                }

                raycastHit = hit;
                return true;
            }
        }

        public static class DataFileUtil
        {
            private const string FOLDER = "CHT";

            /// <summary>Player call cooldowns / daily limits: HarmonyData/CHT/Cooldowns.json</summary>
            public static string CooldownPath => Path.Combine(FOLDER, "Cooldowns");

            public static void EnsureFolderCreated()
            {
                string path = Path.Combine(Interface.Oxide.DataDirectory, FOLDER);

                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
            }

            public static string GetFilePath(string filename = null, bool useSubfolder = true)
            {
                if (filename == null)
                    filename = _plugin.Name;

                if (useSubfolder)
                    return Path.Combine(FOLDER, filename);

                return filename;
            }

            public static string[] GetAllFilePaths(bool filenameOnly = false)
            {
                string[] filePaths = Interface.Oxide.DataFileSystem.GetFiles(FOLDER);

                for (int i = 0; i < filePaths.Length; i++)
                {
                    filePaths[i] = filePaths[i].Substring(0, filePaths[i].Length - 5);

                    if (filenameOnly)
                    {
                        filePaths[i] = Path.GetFileName(filePaths[i]);
                    }
                }
                return filePaths;
            }

            public static bool Exists(string filePath)
            {
                return Interface.Oxide.DataFileSystem.ExistsDatafile(filePath);
            }

            public static T Load<T>(string filePath) where T : class, new()
            {
                T data = Interface.Oxide.DataFileSystem.ReadObject<T>(filePath);
                if (data == null)
                    data = new T();

                return data;
            }

            public static T LoadIfExists<T>(string filePath) where T : class, new()
            {
                if (Exists(filePath))
                    return Load<T>(filePath);
                else
                    return null;
            }

            public static T LoadOrCreate<T>(string filePath) where T : class, new()
            {
                T data = LoadIfExists<T>(filePath);
                if (data == null)
                {
                    data = new T();
                    Save(filePath, data);
                }
                return data;
            }

            /// <summary>
            /// Loads HarmonyData/CHT/Cooldowns.json, migrating from legacy CustomHelicopterTiers2.json if present.
            /// </summary>
            public static T LoadOrCreateCooldowns<T>() where T : class, new()
            {
                EnsureFolderCreated();
                if (!Exists(CooldownPath))
                {
                    string[] legacy =
                    {
                        "CustomHelicopterTiers2",
                        Path.Combine(FOLDER, "CustomHelicopterTiers2"),
                    };
                    foreach (string path in legacy)
                    {
                        if (!Exists(path))
                            continue;
                        T migrated = Load<T>(path);
                        Save(CooldownPath, migrated);
                        _plugin?.Puts("Migrated cooldowns from " + path + ".json to CHT/Cooldowns.json");
                        return migrated;
                    }
                }
                return LoadOrCreate<T>(CooldownPath);
            }

            public static void Save<T>(string filePath, T data)
            {
                Interface.Oxide.DataFileSystem.WriteObject<T>(filePath, data);
            }

            public static void Delete(string filePath)
            {
                Interface.Oxide.DataFileSystem.DeleteDataFile(filePath);
            }
        }

        public static class DrawUtil
        {
            public static void Box(BasePlayer player, float durationSeconds, Color color, Vector3 position, float radius)
            {
                player.SendConsoleCommand("ddraw.box", durationSeconds, color, position, radius);
            }

            public static void Sphere(BasePlayer player, float durationSeconds, Color color, Vector3 position, float radius)
            {
                player.SendConsoleCommand("ddraw.sphere", durationSeconds, color, position, radius);
            }

            public static void Line(BasePlayer player, float durationSeconds, Color color, Vector3 fromPosition, Vector3 toPosition)
            {
                player.SendConsoleCommand("ddraw.line", durationSeconds, color, fromPosition, toPosition);
            }

            public static void Arrow(BasePlayer player, float durationSeconds, Color color, Vector3 fromPosition, Vector3 toPosition, float headSize)
            {
                player.SendConsoleCommand("ddraw.arrow", durationSeconds, color, fromPosition, toPosition, headSize);
            }

            public static void Text(BasePlayer player, float durationSeconds, Color color, Vector3 position, string text)
            {
                player.SendConsoleCommand("ddraw.text", durationSeconds, color, position, text);
            }
        }

        public static class PlayerUtil
        {
            public static BasePlayer FindById(ulong playerId)
            {
                return RelationshipManager.FindByID(playerId);
            }

            public static bool IsNPC(BasePlayer player)
            {
                return player == null || player.IsNpc || (ulong)player.userID < 76561197960265728UL;
            }

            public static bool HasPlayerNearby(Vector3 position, float radius)
            {
                return BaseNetworkable.HasCloseConnections(position, radius);
            }

            public static RelationshipManager.PlayerTeam GetTeam(ulong playerId)
            {
                if (RelationshipManager.ServerInstance == null)
                    return null;

                return RelationshipManager.ServerInstance.FindPlayersTeam(playerId);
            }

            public static bool AreTeammates(ulong firstPlayerId, ulong secondPlayerId)
            {
                var team = GetTeam(firstPlayerId);
                if (team != null && team.members.Contains(secondPlayerId))
                    return true;

                return false;
            }

            public static bool AreAllies(ulong idA, ulong idB) => idA == idB || AreTeammates(idA, idB);
            public static List<ulong> GetAllies(ulong playerId)
            {
                var ids = new List<ulong>();
                var team = GetTeam(playerId);
                if (team != null) foreach (var id in team.members) if (id != playerId) ids.Add(id);
                return ids;
            }

            public static bool AreEnemies(ulong firstPlayerId, ulong secondPlayerId)
            {
                var attackerTeam = GetTeam(firstPlayerId);
                var victimTeam = GetTeam(secondPlayerId);
                return attackerTeam == null || victimTeam == null || attackerTeam != victimTeam;
            }

            public static bool Offline(ulong playerId)
            {
                BasePlayer player = FindById(playerId);
                return player == null || !player.IsConnected;
            }

            public static bool Sleeping(BasePlayer player)
            {
                return player != null && player.IsSleeping();
            }

            public static bool Swimming(BasePlayer player)
            {
                return player != null && player.IsSwimming();
            }

            public static bool Boating(BasePlayer player)
            {
                return player != null && player.isMounted && player.GetMounted().mountTimeStatType == BaseMountable.MountStatType.Boating;
            }

            public static bool Flying(BasePlayer player)
            {
                return player != null && player.isMounted && player.GetMounted().mountTimeStatType == BaseMountable.MountStatType.Flying;
            }

            public static bool Driving(BasePlayer player)
            {
                return player != null && player.isMounted && player.GetMounted().mountTimeStatType == BaseMountable.MountStatType.Driving;
            }

            public static bool InBase(BasePlayer player)
            {
                return player != null && player.IsBuildingAuthed();
            }

            public static bool NearEnemyBase(BasePlayer player)
            {
                return player != null && player.IsBuildingBlocked();
            }

            public static bool Wounded(BasePlayer player)
            {
                return player != null && player.IsWounded();
            }

            public static bool Crawling(BasePlayer player)
            {
                return player != null && player.IsCrawling();
            }

            public static bool OnGround(BasePlayer player)
            {
                return player != null && player.IsOnGround();
            }
        }

        #endregion Helper Classes

        #region Commands

        private static class Cmd
        {
            /// <summary>
            /// cht.tier create <tierName>
            /// cht.tier remove <tierName>
            /// </summary>
            public const string TIER = "cht.tier";

            /// <summary>
            /// cht.callprofile add <tierName> <suffix>
            /// cht.callprofile remove <tierName> <suffix>
            /// </summary>
            public const string CALL_PROFILE = "cht.callprofile";

            /// <summary>
            /// cht.heli kill <tierName/all> [crash/instant]
            /// cht.heli call <tierName/random> <playerNameOrId> [numberToSpawn] [useCallProfile]
            /// cht.heli call2me <tierName/random> [numberToSpawn] [useCallProfile]
            /// cht.heli spawn <tierName/random> [numberToSpawn]
            /// </summary>
            public const string HELI = "cht.heli";

            /// <summary>
            /// cht.crate kill <tierName>
            /// cht.crate unlock <tierName>
            /// </summary>
            public const string CRATE = "cht.crate";

            /// <summary>
            /// cht.gib kill <tierName>
            /// </summary>
            public const string GIB = "cht.gib";
            
            public const string SHOP_CONTROLLER = "cht.shopcontroller";
        }

        [ConsoleCommand(Cmd.TIER)]
        public void cmdTier(ConsoleSystem.Arg arg)
        {
            BasePlayer caller = arg.Player();
            string[] args = Array.ConvertAll(arg.Args, a => a.ToString());

            if (caller != null)
            {
                if (!PermissionUtil.HasPermission(caller, PermissionUtil.ADMIN))
                {
                    MessagePlayer(caller, "You do not have permission to use this command.");
                    return;
                }
            }

            if (args == null || args.Length == 0)
            {
                if (caller != null)
                    MessagePlayer(caller, "Usage: cht.tier create <tierName>");
                else
                    Puts("Usage: cht.tier create <tierName>");
                return;
            }

            string subCommand = args[0].ToLower();
            switch (subCommand)
            {
                case "create":
                    {
                        if (args.Length < 2)
                        {
                            if (caller != null)
                                MessagePlayer(caller, "Usage: cht.tier create <tierName>");
                            else
                                Puts("Usage: cht.tier create <tierName>");
                            return;
                        }

                        string tierName = args[1];
                        string filePath = DataFileUtil.GetFilePath(tierName);

                        if (DataFileUtil.Exists(filePath))
                        {
                            if (caller != null)
                                MessagePlayer(caller, $"Tier '{tierName}' already exists.");
                            else
                                Puts($"Tier '{tierName}' already exists.");
                            return;
                        }

                        TierData newTier = new TierData
                        {
                            Name = tierName,
                            Enabled = true,
                            LifetimeMinutes = 30f,
                            Speed = Speed.Normal,
                            Health = new HealthData
                            {
                                BodyHealth = 10000f,
                                MainRotorHealth = 900f,
                                TailRotorHealth = 500f,
                            },
                            Spawn = new SpawnData
                            {
                                EnableAutomatedSpawns = true,
                                MaximumPopulation = 1,
                                InitialSpawn = false,
                                MinimumRespawnDelayMinutes = 120f,
                                MaximumRespawnDelayMinutes = 240f,
                                MinimumNumberToSpawnPerTick = 1,
                                MaximumNumberToSpawnPerTick = 1,
                                SpawnLocations = new List<string>
                                {
                                    SpawnLocation.Ocean.ToString(),
                                    SpawnLocation.Mainland.ToString()
                                },
                                MinimumSpawnRadiusFromCaller = 500f,
                                MaximumSpawnRadiusFromCaller = 700f
                            },
                            Patrol = new PatrolData
                            {
                                ChanceToPickMonumentInsteadOfRandomPosition = 60,
                                NoGoMonuments = new List<string>()
                            },
                            DangerZone = new DangerZoneData
                            {
                                MaximumAllowedDangerZones = 20,
                                BaseDangerZoneRadius = 20f,
                                NoGoZoneRadius = 250f,
                                RemoveLeastSignificantDangerZoneWhenFull = true,
                                SecondsBeforeDangerZoneExpires = 5f,
                                FleeDamagePercentage = 35,
                                SecondsBeforeNoGoZoneExpires = 300f,
                            },
                            Targeting = new TargetingData
                            {
                                TargetAcquisitionRange = 150f,
                                SecondsBeforeDroppingUnseenTargets = 6f,
                                ChanceOfFinalStrafeBeforeDroppingTarget = 100,
                                OnlyRetaliateIfAttacked = false
                            },
                            MachineGun = new MachineGunData
                            {
                                TimeBetweenIndividualShotsSeconds = 0.09f,
                                BurstFiringDurationSeconds = 3f,
                                CooldownTimeBetweenBurstsSeconds = 4f,
                                MaximumTargetEngagementRange = 150f,
                                TargetTrackingDurationBeforeLossSeconds = 5f,
                                BaseBulletDamage = 20f,
                                BulletSpreadAccuracy = 2
                            },
                            Strafe = new StrafeData
                            {
                                CanStrafePlayersNearEnemyBases = false,
                                MaximumRocketsFiredPerStrafe = 12,
                                DelayBetweenRocketLaunchesSeconds = 0.2f,
                                RocketDamageMultiplier = 1f,
                                CooldownBetweenStrafesSeconds = 20f,
                                ChanceToUpgradeFromStrafeToOrbitStrafe = 60,
                                MaximumRocketsFiredPerOrbitStrafe = 18,
                                DelayBetweenRocketLaunchesWhileOrbitingSeconds = 0.5f,
                                CanUseNapalmRockets = true,
                                CooldownBetweenNapalmStrafesSeconds = 30f
                            },
                            Homing = new HomingData
                            {
                                CanBeHomingTargeted = true,
                                CanDefendWithFlares = true,
                                FlareDurationSeconds = 5f
                            },
                            Debris = new DebrisData
                            {
                                SpawnGibs = true,
                                HitPoints = 500f,
                                CoolingPeriodSeconds = 480f,
                                OverrideDefaultSalvage = false,
                                SalvageOverrideItems = new List<ItemData>
                                {
                                new ItemData
                                {
                                    ShortName = "charcoal",
                                    DisplayName = null,
                                    SkinId = 0,
                                    MinimumAmount = 24,
                                    MaximumAmount = 24,
                                    SpawnAsBlueprint = false,
                                    Rarity = Rarity.Common
                                },
                                new ItemData
                                {
                                    ShortName = "metal.fragments",
                                    DisplayName = null,
                                    SkinId = 0,
                                    MinimumAmount = 24,
                                    MaximumAmount = 24,
                                    SpawnAsBlueprint = false,
                                    Rarity = Rarity.Common
                                },
                                new ItemData
                                {
                                    ShortName = "metal.refined",
                                    DisplayName = null,
                                    SkinId = 0,
                                    MinimumAmount = 7,
                                    MaximumAmount = 7,
                                    SpawnAsBlueprint = false,
                                    Rarity = Rarity.Rare
                                }
                                }
                            },
                            PVE = new PVEData
                            {
                                BlockDamageToNonCallerOwnedEntities = true,
                                BlockDamageToNonCallerPlayers = true
                            },
                            Crash = new CrashData
                            {
                                MaximumFireBallsToSpawn = 8,
                                FireBall = new FireBallData
                                {
                                    MinimumLifetimeSeconds = 180f,
                                    MaximumLifetimeSeconds = 300f,
                                    DamagePerSecond = 8f,
                                    TryToSpread = true,
                                    WaterToExtinguish = 2500
                                }
                            },
                            Loot = new LootData
                            {
                                MaximumCratesToSpawn = 4,
                                CrateLifetimeSeconds = 1800f,
                                LockCratesToCaller = true,
                                LockingFireBall = new FireBallData
                                {
                                    MinimumLifetimeSeconds = 180f,
                                    MaximumLifetimeSeconds = 300f,
                                    DamagePerSecond = 8f,
                                    TryToSpread = false,
                                    WaterToExtinguish = 2500
                                },
                                AlphaLootProfile = "",
                                UseCustomLootTable = false,
                                CustomLootTable = new List<LootTableData>
                                {
                                    new LootTableData
                                    {
                                        Rarity = Rarity.Common,
                                        MinimumLootSpawnSlots = 4,
                                        MaximumLootSpawnSlots = 4,
                                        Items = new List<ItemData>
                                        {
                                            new ItemData { ShortName = "ammo.rifle", DisplayName = null, SkinId = 0, MinimumAmount = 120, MaximumAmount = 120, SpawnAsBlueprint = false, Rarity = Rarity.Common },
                                            new ItemData { ShortName = "ammo.rifle.incendiary", DisplayName = null, SkinId = 0, MinimumAmount = 60, MaximumAmount = 60, SpawnAsBlueprint = false, Rarity = Rarity.Common },
                                            new ItemData { ShortName = "ammo.rifle.explosive", DisplayName = null, SkinId = 0, MinimumAmount = 30, MaximumAmount = 30, SpawnAsBlueprint = false, Rarity = Rarity.Rare },
                                            new ItemData { ShortName = "ammo.rifle.hv", DisplayName = null, SkinId = 0, MinimumAmount = 40, MaximumAmount = 40, SpawnAsBlueprint = false, Rarity = Rarity.Rare }
                                        }
                                    },
                                    new LootTableData
                                    {
                                        Rarity = Rarity.Common,
                                        MinimumLootSpawnSlots = 3,
                                        MaximumLootSpawnSlots = 3,
                                        Items = new List<ItemData>
                                        {
                                            new ItemData { ShortName = "rifle.l96", DisplayName = null, SkinId = 0, MinimumAmount = 1, MaximumAmount = 1, SpawnAsBlueprint = false, Rarity = Rarity.VeryRare },
                                            new ItemData { ShortName = "weapon.mod.8x.scope", DisplayName = null, SkinId = 0, MinimumAmount = 1, MaximumAmount = 1, SpawnAsBlueprint = false, Rarity = Rarity.Uncommon },
                                            new ItemData { ShortName = "ammo.rifle", DisplayName = null, SkinId = 0, MinimumAmount = 8, MaximumAmount = 8, SpawnAsBlueprint = false, Rarity = Rarity.Common }
                                        }
                                    },
                                    new LootTableData
                                    {
                                        Rarity = Rarity.Common,
                                        MinimumLootSpawnSlots = 2,
                                        MaximumLootSpawnSlots = 2,
                                        Items = new List<ItemData>
                                        {
                                            new ItemData { ShortName = "pistol.m92", DisplayName = null, SkinId = 0, MinimumAmount = 1, MaximumAmount = 1, SpawnAsBlueprint = false, Rarity = Rarity.Uncommon },
                                            new ItemData { ShortName = "ammo.pistol", DisplayName = null, SkinId = 0, MinimumAmount = 30, MaximumAmount = 30, SpawnAsBlueprint = false, Rarity = Rarity.Common }
                                        }
                                    },
                                    new LootTableData
                                    {
                                        Rarity = Rarity.Common,
                                        MinimumLootSpawnSlots = 2,
                                        MaximumLootSpawnSlots = 2,
                                        Items = new List<ItemData>
                                        {
                                            new ItemData { ShortName = "lmg.m249", DisplayName = null, SkinId = 0, MinimumAmount = 1, MaximumAmount = 1, SpawnAsBlueprint = false, Rarity = Rarity.Rare },
                                            new ItemData { ShortName = "ammo.rifle", DisplayName = null, SkinId = 0, MinimumAmount = 50, MaximumAmount = 50, SpawnAsBlueprint = false, Rarity = Rarity.Common }
                                        }
                                    },
                                    new LootTableData
                                    {
                                        Rarity = Rarity.Common,
                                        MinimumLootSpawnSlots = 2,
                                        MaximumLootSpawnSlots = 2,
                                        Items = new List<ItemData>
                                        {
                                            new ItemData { ShortName = "explosive.timed", DisplayName = null, SkinId = 0, MinimumAmount = 2, MaximumAmount = 2, SpawnAsBlueprint = false, Rarity = Rarity.VeryRare },
                                            new ItemData { ShortName = "ammo.rocket.basic", DisplayName = null, SkinId = 0, MinimumAmount = 3, MaximumAmount = 3, SpawnAsBlueprint = false, Rarity = Rarity.Uncommon }
                                        }
                                    },
                                    new LootTableData
                                    {
                                        Rarity = Rarity.Common,
                                        MinimumLootSpawnSlots = 2,
                                        MaximumLootSpawnSlots = 2,
                                        Items = new List<ItemData>
                                        {
                                            new ItemData { ShortName = "rifle.ak", DisplayName = null, SkinId = 0, MinimumAmount = 1, MaximumAmount = 1, SpawnAsBlueprint = false, Rarity = Rarity.Rare },
                                            new ItemData { ShortName = "ammo.rifle", DisplayName = null, SkinId = 0, MinimumAmount = 12, MaximumAmount = 12, SpawnAsBlueprint = false, Rarity = Rarity.Common }
                                        }
                                    },
                                    new LootTableData
                                    {
                                        Rarity = Rarity.Common,
                                        MinimumLootSpawnSlots = 2,
                                        MaximumLootSpawnSlots = 2,
                                        Items = new List<ItemData>
                                        {
                                            new ItemData { ShortName = "ammo.rocket.fire", DisplayName = null, SkinId = 0, MinimumAmount = 5, MaximumAmount = 5, SpawnAsBlueprint = false, Rarity = Rarity.Rare },
                                            new ItemData { ShortName = "ammo.rocket.hv", DisplayName = null, SkinId = 0, MinimumAmount = 3, MaximumAmount = 3, SpawnAsBlueprint = false, Rarity = Rarity.Rare },
                                            new ItemData { ShortName = "ammo.rocket.mlrs", DisplayName = null, SkinId = 0, MinimumAmount = 2, MaximumAmount = 2, SpawnAsBlueprint = false, Rarity = Rarity.VeryRare },
                                            new ItemData { ShortName = "ammo.rocket.seeker", DisplayName = null, SkinId = 0, MinimumAmount = 4, MaximumAmount = 4, SpawnAsBlueprint = false, Rarity = Rarity.VeryRare }
                                        }
                                    },
                                    new LootTableData
                                    {
                                        Rarity = Rarity.Common,
                                        MinimumLootSpawnSlots = 4,
                                        MaximumLootSpawnSlots = 4,
                                        Items = new List<ItemData>
                                        {
                                            new ItemData { ShortName = "weapon.mod.lasersight", DisplayName = null, SkinId = 0, MinimumAmount = 1, MaximumAmount = 1, SpawnAsBlueprint = false, Rarity = Rarity.Uncommon },
                                            new ItemData { ShortName = "weapon.mod.small.scope", DisplayName = null, SkinId = 0, MinimumAmount = 1, MaximumAmount = 1, SpawnAsBlueprint = false, Rarity = Rarity.Common },
                                            new ItemData { ShortName = "weapon.mod.burstmodule", DisplayName = null, SkinId = 0, MinimumAmount = 1, MaximumAmount = 1, SpawnAsBlueprint = false, Rarity = Rarity.Rare },
                                            new ItemData { ShortName = "weapon.mod.extendedmags", DisplayName = null, SkinId = 0, MinimumAmount = 1, MaximumAmount = 1, SpawnAsBlueprint = false, Rarity = Rarity.Uncommon }
                                        }
                                    },
                                    new LootTableData
                                    {
                                        Rarity = Rarity.Common,
                                        MinimumLootSpawnSlots = 2,
                                        MaximumLootSpawnSlots = 2,
                                        Items = new List<ItemData>
                                        {
                                            new ItemData { ShortName = "rifle.bolt", DisplayName = null, SkinId = 0, MinimumAmount = 1, MaximumAmount = 1, SpawnAsBlueprint = false, Rarity = Rarity.VeryRare },
                                            new ItemData { ShortName = "ammo.rifle", DisplayName = null, SkinId = 0, MinimumAmount = 8, MaximumAmount = 8, SpawnAsBlueprint = false, Rarity = Rarity.Common }
                                        }
                                    }
                                }
                            },
                            RunRandomDeathCommandSet = false,
                            DeathCommandSets = new List<CommandSetData>
                            {
                                new CommandSetData
                                {
                                    Commands = new List<CommandData>
                                    {
                                        new CommandData
                                        {
                                            Type = CommandType.Chat,
                                            Command = "I have just taken down the {TierName} helicopter at grid {Grid}!"
                                        }
                                    }
                                },
                                new CommandSetData
                                {
                                    Commands = new List<CommandData>
                                    {
                                        new CommandData
                                        {
                                            Type = CommandType.Client,
                                            Command = "gametip.showgametip {PlayerName}, you destroyed the {TierName}!"
                                        },
                                        new CommandData
                                        {
                                            Type = CommandType.Server,
                                            Command = "inventory.giveto {PlayerId} scrap 100"
                                        }
                                    }
                                }
                            }
                        };

                        DataFileUtil.Save(filePath, newTier);

                        HelicopterSpawnerComponent spawner = HelicopterSpawnerComponent.Create(newTier, _tieredHelicopterManager);
                        _tieredHelicopterManager.RegisterSpawner(tierName, spawner);

                        if (caller != null)
                            MessagePlayer(caller, $"Tier '{tierName}' created and initialized.");
                        else
                            Puts($"Tier '{tierName}' created and initialized.");

                        break;
                    }

                default:
                    {
                        if (caller != null)
                            MessagePlayer(caller, $"Unknown subcommand '{subCommand}'. Usage: cht.tier create <tierName>");
                        else
                            Puts($"Unknown subcommand '{subCommand}'. Usage: cht.tier create <tierName>");
                        break;
                    }
            }
        }

        [ConsoleCommand(Cmd.CALL_PROFILE)]
        public void cmdCallProfile(ConsoleSystem.Arg arg)
        {
            BasePlayer caller = arg.Player();
            string[] args = Array.ConvertAll(arg.Args, a => a.ToString());

            if (caller != null)
            {
                if (!PermissionUtil.HasPermission(caller, PermissionUtil.ADMIN))
                {
                    MessagePlayer(caller, "You do not have permission to use this command.");
                    return;
                }
            }

            if (args == null || args.Length == 0)
            {
                if (caller != null)
                {
                    MessagePlayer(
                        caller,
                        "Usage:\n" +
                        "  cht.callprofile add <tierName> <suffix>"
                    );
                }
                else
                {
                    Puts(
                        "Usage:\n" +
                        "  cht.callprofile add <tierName> <suffix>"
                    );
                }
                return;
            }

            string subCommand = args[0].ToLowerInvariant();

            switch (subCommand)
            {
                case "add":
                    {
                        if (args.Length < 3)
                        {
                            if (caller != null)
                            {
                                MessagePlayer(caller,
                                    "Usage:\n" +
                                    "  cht.callprofile add <tierName> <suffix>"
                                );
                            }
                            else
                            {
                                Puts(
                                    "Usage:\n" +
                                    "  cht.callprofile add <tierName> <suffix>"
                                );
                            }
                            return;
                        }

                        string tierName = args[1];
                        string suffix = args[2];

                        string filePath = DataFileUtil.GetFilePath(tierName);
                        if (!DataFileUtil.Exists(filePath))
                        {
                            if (caller != null)
                                MessagePlayer(caller, $"No data file found for tier '{tierName}'. Please create it first.");
                            else
                                Puts($"No data file found for tier '{tierName}'. Please create it first.");
                            return;
                        }

                        TierData tierData = DataFileUtil.Load<TierData>(filePath);
                        if (tierData == null)
                        {
                            if (caller != null)
                                MessagePlayer(caller, $"Failed to load tier '{tierName}'. The file may be corrupt.");
                            else
                                Puts($"Failed to load tier '{tierName}'. The file may be corrupt.");
                            return;
                        }

                        if (tierData.CallProfiles == null)
                            tierData.CallProfiles = new List<CallProfileData>();

                        bool existsAlready = tierData.CallProfiles
                            .Any(profile => profile.Suffix.Equals(suffix, StringComparison.OrdinalIgnoreCase));

                        if (existsAlready)
                        {
                            if (caller != null)
                                MessagePlayer(caller, $"A call profile with suffix '{suffix}' already exists on tier '{tierName}'. Operation aborted.");
                            else
                                Puts($"A call profile with suffix '{suffix}' already exists on tier '{tierName}'. Operation aborted.");
                            return;
                        }

                        CallProfileData newProfile = new CallProfileData
                        {
                            Enabled = true,
                            Suffix = suffix,
                            LockOnCaller = true,
                            IncludeCallerAllies = true,
                            NumberToSpawn = 1,
                            Priority = 1,
                            CooldownMinutes = 360f,
                            DailyCallLimit = 0,
                            SkillTreeXPRewarded = 0.0f,
                            CostToCall = new CurrencyData[]
                            {
                                new CurrencyData
                                {
                                    Enabled = true,
                                    Name    = "scrap",
                                    Amount  = 100
                                },
                                new CurrencyData
                                {
                                    Enabled = true,
                                    Name    = "coin",
                                    Amount  = 50
                                },
                            }
                        };

                        newProfile.InitializePermission(tierData.Name);
                        newProfile.InitializePriceList();

                        tierData.CallProfiles.Add(newProfile);
                        DataFileUtil.Save(filePath, tierData);

                        if (caller != null)
                        {
                            MessagePlayer(caller,
                                $"Successfully added new call profile with suffix '{suffix}' " +
                                $"to tier '{tierName}'.\n" +
                                $"Permission: {newProfile.Permission}");
                        }
                        else
                        {
                            Puts(
                                $"Successfully added new call profile with suffix '{suffix}' " +
                                $"to tier '{tierName}'.\n" +
                                $"Permission: {newProfile.Permission}");
                        }

                        break;
                    }

                default:
                    {
                        if (caller != null)
                        {
                            MessagePlayer(caller,
                                $"Unknown subcommand '{subCommand}'.\n" +
                                "Usage:\n" +
                                "  cht.callprofile add <tierName> <suffix>"
                            );
                        }
                        else
                        {
                            Puts(
                                $"Unknown subcommand '{subCommand}'.\n" +
                                "Usage:\n" +
                                "  cht.callprofile add <tierName> <suffix>"
                            );
                        }
                        break;
                    }
            }
        }

        [ConsoleCommand(Cmd.HELI)]
        public void cmdHeli(ConsoleSystem.Arg arg)
        {
            BasePlayer caller = arg.Player();
            string[] args = Array.ConvertAll(arg.Args, a => a.ToString());

            if (args == null || args.Length == 0)
            {
                if (caller != null)
                    MessagePlayer(caller,
                        "Usage:\n" +
                        "  - cht.heli kill <tierName/all> [crash/instant]\n" +
                        "  - cht.heli call <tierName/random> <playerNameOrId> [numberToSpawn] [useCallProfile]\n" +
                        "  - cht.heli call2me <tierName/random> [numberToSpawn] [useCallProfile]\n" +
                        "  - cht.heli spawn <tierName/random> [numberToSpawn]"
                    );
                else
                    Puts(
                        "Usage:\n" +
                        "  - cht.heli kill <tierName/all> [crash/instant]\n" +
                        "  - cht.heli call <tierName/random> <playerNameOrId> [numberToSpawn] [useCallProfile]\n" +
                        "  - cht.heli call2me <tierName/random> [numberToSpawn] [useCallProfile]\n" +
                        "  - cht.heli spawn <tierName/random> [numberToSpawn]"
                    );
                return;
            }

            string subCommand = args[0].ToLowerInvariant();

            if (caller != null)
            {
                if (!PermissionUtil.HasPermission(caller, PermissionUtil.ADMIN))
                {
                    MessagePlayer(caller, "You do not have permission to use this command.");
                    return;
                }
            }

            switch (subCommand)
            {
                case "kill":
                    {
                        if (args.Length < 2)
                        {
                            if (caller != null)
                                MessagePlayer(caller, "Usage: cht.heli kill <tierName/all> [crash/instant]");
                            else
                                Puts("Usage: cht.heli kill <tierName/all> [crash/instant]");
                            return;
                        }

                        string targetTier = args[1];
                        bool simulateDeath = false;

                        if (args.Length >= 3)
                        {
                            string mode = args[2].ToLowerInvariant();
                            if (mode == "crash")
                                simulateDeath = true;
                            else if (mode == "instant")
                                simulateDeath = false;
                            else
                            {
                                if (caller != null)
                                    MessagePlayer(caller, "Unknown destruction mode. Valid modes: crash, instant.");
                                else
                                    Puts("Unknown destruction mode. Valid modes: crash, instant.");
                                return;
                            }
                        }

                        int killedCount = 0;
                        foreach (TieredHelicopterComponent tieredHelicopter in _tieredHelicopterManager.GetAllTieredHelicopters())
                        {
                            if (targetTier.Equals("all", StringComparison.OrdinalIgnoreCase)
                                || tieredHelicopter.TierData.Name.Equals(targetTier, StringComparison.OrdinalIgnoreCase))
                            {
                                if (tieredHelicopter != null && tieredHelicopter.PatrolHelicopter != null && !tieredHelicopter.PatrolHelicopter.IsDestroyed)
                                {
                                    tieredHelicopter.Kill(simulateDeath);
                                    killedCount++;
                                }
                            }
                        }

                        if (targetTier.Equals("all", StringComparison.OrdinalIgnoreCase))
                        {
                            if (caller != null)
                                MessagePlayer(caller, $"Killed {killedCount} helicopters from all tiers.");
                            else
                                Puts($"Killed {killedCount} helicopters from all tiers.");
                        }
                        else
                        {
                            if (caller != null)
                                MessagePlayer(caller, $"Killed {killedCount} helicopters for tier '{targetTier}'.");
                            else
                                Puts($"Killed {killedCount} helicopters for tier '{targetTier}'.");
                        }
                        break;
                    }

                case "call":
                    {
                        if (args.Length < 3)
                        {
                            if (caller != null)
                                MessagePlayer(caller, "Usage: cht.heli call <tierName/random> <playerNameOrId> [numberToSpawn] [useCallProfile]");
                            else
                                Puts("Usage: cht.heli call <tierName/random> <playerNameOrId> [numberToSpawn] [useCallProfile]");
                            return;
                        }

                        string tierArg = args[1];
                        string playerSearch = args[2];

                        int numberToSpawn = 1;
                        if (args.Length >= 4)
                            int.TryParse(args[3], out numberToSpawn);

                        bool useCallProfile = false;
                        if (args.Length >= 5)
                        {
                            bool.TryParse(args[4], out useCallProfile);
                        }

                        TierData tierData;
                        if (tierArg.Equals("random", StringComparison.OrdinalIgnoreCase))
                        {
                            tierData = _tieredHelicopterManager.GetRandomTierData();
                            if (tierData == null)
                            {
                                if (caller != null)
                                    MessagePlayer(caller, "No tiers found to choose from!");
                                else
                                    Puts("No tiers found to choose from!");
                                return;
                            }
                        }
                        else
                        {
                            tierData = _tieredHelicopterManager.GetTierDataByTierName(tierArg);
                            if (tierData == null)
                            {
                                if (caller != null)
                                    MessagePlayer(caller, $"Tier '{tierArg}' not found.");
                                else
                                    Puts($"Tier '{tierArg}' not found.");
                                return;
                            }
                        }

                        BasePlayer targetPlayer = FindPlayerByPartialNameOrId(playerSearch);
                        if (targetPlayer == null)
                        {
                            if (caller != null)
                                MessagePlayer(caller, $"No players found matching '{playerSearch}'!");
                            else
                                Puts($"No players found matching '{playerSearch}'!");
                            return;
                        }
                        HelicopterSpawnerComponent spawner = _tieredHelicopterManager.GetSpawnerByTierName(tierData.Name);
                        if (spawner == null)
                        {
                            if (caller != null)
                                MessagePlayer(caller, $"No spawner found for tier '{tierData.Name}'.");
                            else
                                Puts($"No spawner found for tier '{tierData.Name}'.");
                            return;
                        }

                        if (useCallProfile)
                        {
                            CallProfileData callProfile = GetCallProfileFor(targetPlayer, tierData);
                            if (callProfile == null)
                            {
                                if (caller != null)
                                    MessagePlayer(caller, $"No call profile found in tier '{tierData.Name}' for {targetPlayer.displayName}.");
                                else
                                    Puts($"No call profile found in tier '{tierData.Name}' for {targetPlayer.displayName}.");
                                return;
                            }

                            if (!callProfile.Enabled || callProfile.NumberToSpawn <= 0)
                            {
                                if (caller != null)
                                    MessagePlayer(caller, $"Call profile '{callProfile.Suffix}' is disabled or spawns 0 helicopters.");
                                else
                                    Puts($"Call profile '{callProfile.Suffix}' is disabled or spawns 0 helicopters.");
                                return;
                            }

                            double remain = GetTimeLeftUntilHeliCall(targetPlayer, callProfile);
                            if (remain > 0)
                            {
                                MessagePlayer(targetPlayer, $"You must wait {FormatTime(remain)} before you can call that helicopter again.");

                                if (caller != null && caller != targetPlayer)
                                    MessagePlayer(caller, $"'{targetPlayer.displayName}' is on cooldown and cannot call a helicopter yet.");

                                return;
                            }

                            int usedToday = GetDailyCallsUsed(targetPlayer, callProfile);
                            if (callProfile.DailyCallLimit > 0 && usedToday >= callProfile.DailyCallLimit)
                            {
                                MessagePlayer(targetPlayer,
                                    $"Daily limit reached. You can only call this helicopter {callProfile.DailyCallLimit} time(s) today.");
                                if (caller != null && caller != targetPlayer)
                                    MessagePlayer(caller,
                                        $"'{targetPlayer.displayName}' has hit today’s limit for that helicopter.");
                                return;
                            }

                            List<string> missingPaymentReasons = new List<string>();
                            foreach (var currency in callProfile.CostToCall)
                            {
                                if (!currency.Enabled)
                                    continue;

                                if (!currency.Valid)
                                {
                                    missingPaymentReasons.Add($"Payment method '{currency.Name}' is unavailable.");
                                }
                                else if (!currency.CanAfford(targetPlayer))
                                {
                                    missingPaymentReasons.Add($"- {targetPlayer.displayName} needs {currency.Amount} × {currency.Name}.");
                                }
                            }
                            if (missingPaymentReasons.Count > 0)
                            {
                                string msg = "Insufficient payment:\n" + string.Join("\n", missingPaymentReasons);

                                MessagePlayer(targetPlayer, msg);

                                if (caller != null && caller != targetPlayer)
                                {
                                    MessagePlayer(caller, $"'{targetPlayer.displayName}' cannot afford this helicopter call.");
                                }

                                return;
                            }

                            var (success, explanation, actualSpawned) = spawner.TrySpawn(numberToSpawn, targetPlayer);
                            if (!success)
                            {
                                if (caller != null)
                                    MessagePlayer(caller, explanation);
                                else
                                    Puts(explanation);
                                return;
                            }

                            foreach (var currency in callProfile.CostToCall)
                            {
                                if (currency.Enabled && currency.Valid)
                                {
                                    currency.Charge(targetPlayer);
                                }
                            }
                            SetNextAllowedHeliCallTime(targetPlayer, callProfile);
                            IncrementDailyCalls(targetPlayer, callProfile);

                            if (caller != null)
                                MessagePlayer(caller, explanation);
                            else
                                Puts(explanation);

                            BroadcastHelicopterCall(callProfile, tierData, targetPlayer);
                        }
                        else
                        {
                            var (success, explanation, _) = spawner.TrySpawn(numberToSpawn, targetPlayer);
                            if (!success)
                            {
                                if (caller != null)
                                    MessagePlayer(caller, explanation);
                                else
                                    Puts(explanation);
                                return;
                            }

                            if (caller != null)
                                MessagePlayer(caller, explanation);
                            else
                                Puts(explanation);

                            MessagePlayer(targetPlayer, "A helicopter has been called to your location!");
                        }

                        break;
                    }

                case "call2me":
                    {
                        if (args.Length < 2)
                        {
                            if (caller != null)
                                MessagePlayer(caller, "Usage: cht.heli call2me <tierName/random> [numberToSpawn] [useCallProfile]");
                            else
                                Puts("Usage: cht.heli call2me <tierName/random> [numberToSpawn] [useCallProfile]");
                            return;
                        }

                        string tierArg = args[1];
                        int numberToSpawn = 1;
                        if (args.Length >= 3)
                            int.TryParse(args[2], out numberToSpawn);

                        bool useCallProfile = false;
                        if (args.Length >= 4)
                        {
                            bool.TryParse(args[3], out useCallProfile);
                        }

                        if (caller == null)
                        {
                            Puts("This command requires a player. (call2me invoked from console?)");
                            return;
                        }

                        TierData tierData;
                        if (tierArg.Equals("random", StringComparison.OrdinalIgnoreCase))
                        {
                            tierData = _tieredHelicopterManager.GetRandomTierData();
                            if (tierData == null)
                            {
                                MessagePlayer(caller, "No tiers found to choose from!");
                                return;
                            }
                        }
                        else
                        {
                            tierData = _tieredHelicopterManager.GetTierDataByTierName(tierArg);
                            if (tierData == null)
                            {
                                MessagePlayer(caller, $"Tier '{tierArg}' not found.");
                                return;
                            }
                        }

                        HelicopterSpawnerComponent spawner = _tieredHelicopterManager.GetSpawnerByTierName(tierData.Name);
                        if (spawner == null)
                        {
                            MessagePlayer(caller, $"No spawner found for tier '{tierData.Name}'.");
                            return;
                        }

                        if (useCallProfile)
                        {
                            CallProfileData callProfile = GetCallProfileFor(caller, tierData);
                            if (callProfile == null || !callProfile.Enabled || callProfile.NumberToSpawn <= 0)
                            {
                                MessagePlayer(caller, $"No valid call profile found in tier '{tierData.Name}' for you (enabled + # spawn > 0).");
                                return;
                            }

                            double remain = GetTimeLeftUntilHeliCall(caller, callProfile);
                            if (remain > 0)
                            {
                                MessagePlayer(caller, $"You must wait {FormatTime(remain)} before calling that helicopter again.");
                                return;
                            }

                            int usedToday = GetDailyCallsUsed(caller, callProfile);
                            if (callProfile.DailyCallLimit > 0 && usedToday >= callProfile.DailyCallLimit)
                            {
                                MessagePlayer(caller,
                                    $"Daily limit reached. You can only call this helicopter {callProfile.DailyCallLimit} time(s) today.");
                                return;
                            }

                            List<string> missingPaymentReasons = new List<string>();
                            foreach (var currency in callProfile.CostToCall)
                            {
                                if (!currency.Enabled)
                                    continue;

                                if (!currency.Valid)
                                {
                                    missingPaymentReasons.Add($"Payment method '{currency.Name}' is unavailable.");
                                }
                                else if (!currency.CanAfford(caller))
                                {
                                    missingPaymentReasons.Add($"- Requires {currency.Amount} × {currency.Name}.");
                                }
                            }
                            if (missingPaymentReasons.Count > 0)
                            {
                                string msg = "Insufficient payment:\n" + string.Join("\n", missingPaymentReasons);
                                MessagePlayer(caller, msg);
                                return;
                            }

                            var (success, explanation, actualSpawned) = spawner.TrySpawn(numberToSpawn, caller);
                            if (!success)
                            {
                                MessagePlayer(caller, explanation);
                                return;
                            }

                            foreach (var currency in callProfile.CostToCall)
                            {
                                if (currency.Enabled && currency.Valid)
                                {
                                    currency.Charge(caller);
                                }
                            }
                            SetNextAllowedHeliCallTime(caller, callProfile);
                            IncrementDailyCalls(caller, callProfile);

                            MessagePlayer(caller, explanation);
                            BroadcastHelicopterCall(callProfile, tierData, caller);
                        }
                        else
                        {
                            var (success, explanation, _) = spawner.TrySpawn(numberToSpawn, caller);
                            if (!success)
                            {
                                MessagePlayer(caller, explanation);
                                return;
                            }

                            MessagePlayer(caller, explanation);
                        }

                        break;
                    }

                case "spawn":
                    {
                        if (args.Length < 2)
                        {
                            if (caller != null)
                                MessagePlayer(caller, "Usage: cht.heli spawn <tierName/random> [numberToSpawn]");
                            else
                                Puts("Usage: cht.heli spawn <tierName/random> [numberToSpawn]");
                            return;
                        }

                        string tierArg = args[1];
                        int numberToSpawn = 1;
                        if (args.Length >= 3)
                            int.TryParse(args[2], out numberToSpawn);

                        TierData tierData;
                        if (tierArg.Equals("random", StringComparison.OrdinalIgnoreCase))
                        {
                            tierData = _tieredHelicopterManager.GetRandomTierData();
                            if (tierData == null)
                            {
                                if (caller != null)
                                    MessagePlayer(caller, "No tiers found to choose from!");
                                else
                                    Puts("No tiers found to choose from!");
                                return;
                            }
                        }
                        else
                        {
                            tierData = _tieredHelicopterManager.GetTierDataByTierName(tierArg);
                            if (tierData == null)
                            {
                                if (caller != null)
                                    MessagePlayer(caller, $"Tier '{tierArg}' not found.");
                                else
                                    Puts($"Tier '{tierArg}' not found.");
                                return;
                            }
                        }

                        HelicopterSpawnerComponent spawner = _tieredHelicopterManager.GetSpawnerByTierName(tierData.Name);
                        if (spawner == null)
                        {
                            if (caller != null)
                                MessagePlayer(caller, $"No spawner found for tier '{tierData.Name}'.");
                            else
                                Puts($"No spawner found for tier '{tierData.Name}'.");
                            return;
                        }

                        var (success, explanation, actualSpawned) = spawner.TrySpawn(numberToSpawn, null);

                        if (caller != null)
                            MessagePlayer(caller, explanation);
                        else
                            Puts(explanation);

                        break;
                    }

                default:
                    {
                        if (caller != null)
                            MessagePlayer(caller,
                                "Unknown subcommand. Usage:\n" +
                                "  - cht.heli kill <tierName/all> [crash/instant]\n" +
                                "  - cht.heli call <tierName/random> <playerNameOrId> [numberToSpawn] [useCallProfile]\n" +
                                "  - cht.heli call2me <tierName/random> [numberToSpawn] [useCallProfile]\n" +
                                "  - cht.heli spawn <tierName/random> [numberToSpawn]"
                            );
                        else
                            Puts(
                                "Unknown subcommand. Usage:\n" +
                                "  - cht.heli kill <tierName/all> [crash/instant]\n" +
                                "  - cht.heli call <tierName/random> <playerNameOrId> [numberToSpawn] [useCallProfile]\n" +
                                "  - cht.heli call2me <tierName/random> [numberToSpawn] [useCallProfile]\n" +
                                "  - cht.heli spawn <tierName/random> [numberToSpawn]"
                            );
                        break;
                    }
            }
        }

        [ConsoleCommand(Cmd.GIB)]
        public void cmdGib(ConsoleSystem.Arg arg)
        {
            BasePlayer caller = arg.Player();
            string[] args = Array.ConvertAll(arg.Args, a => a.ToString());

            if (caller != null)
            {
                if (!PermissionUtil.HasPermission(caller, PermissionUtil.ADMIN))
                {
                    MessagePlayer(caller, "You do not have permission to use this command.");
                    return;
                }
            }

            if (args == null || args.Length < 2)
            {
                if (caller != null)
                    MessagePlayer(caller, "Usage: cht.gib kill <tierName>");
                else
                    Puts("Usage: cht.gib kill <tierName>");
                return;
            }

            string subCommand = args[0].ToLower();
            if (subCommand != "kill")
            {
                if (caller != null)
                    MessagePlayer(caller, "Unknown subcommand. Usage: cht.gib kill <tierName>");
                else
                    Puts("Unknown subcommand. Usage: cht.gib kill <tierName>");
                return;
            }

            string targetTier = args[1];

            List<HelicopterDebris> debrisList = _tieredHelicopterManager.GetDebrisByTierName(targetTier);
            if (debrisList == null || debrisList.Count == 0)
            {
                if (caller != null)
                    MessagePlayer(caller, $"No helicopter debris found for tier '{targetTier}'.");
                else
                    Puts($"No helicopter debris found for tier '{targetTier}'.");
                return;
            }

            int killCount = 0;
            foreach (var debris in debrisList.ToArray())
            {
                if (debris != null && !debris.IsDestroyed)
                {
                    debris.Kill();
                    killCount++;
                }
            }

            debrisList.Clear();

            if (caller != null)
                MessagePlayer(caller, $"Killed {killCount} helicopter debris for tier '{targetTier}'.");
            else
                Puts($"Killed {killCount} helicopter debris for tier '{targetTier}'.");
        }

        [ConsoleCommand(Cmd.CRATE)]
        public void cmdCrate(ConsoleSystem.Arg arg)
        {
            BasePlayer caller = arg.Player();
            string[] args = Array.ConvertAll(arg.Args, a => a.ToString());

            if (caller != null)
            {
                if (!PermissionUtil.HasPermission(caller, PermissionUtil.ADMIN))
                {
                    MessagePlayer(caller, "You do not have permission to use this command.");
                    return;
                }
            }

            if (args == null || args.Length < 2)
            {
                if (caller != null)
                    MessagePlayer(caller, "Usage: cht.crate kill <tierName> or cht.crate unlock <tierName>");
                else
                    Puts("Usage: cht.crate kill <tierName> or cht.crate unlock <tierName>");
                return;
            }

            string subCommand = args[0].ToLower();
            string targetTier = args[1];

            List<LootContainer> crateList = _tieredHelicopterManager.GetCratesByTierName(targetTier);
            if (crateList == null || crateList.Count == 0)
            {
                if (caller != null)
                    MessagePlayer(caller, $"No crates found for tier '{targetTier}'.");
                else
                    Puts($"No crates found for tier '{targetTier}'.");
                return;
            }

            switch (subCommand)
            {
                case "kill":
                    {
                        int killCount = 0;
                        foreach (var crate in crateList.ToArray())
                        {
                            if (crate != null && !crate.IsDestroyed)
                            {
                                crate.Kill();
                                killCount++;
                            }
                        }

                        crateList.Clear();

                        if (caller != null)
                            MessagePlayer(caller, $"Killed {killCount} crates for tier '{targetTier}'.");
                        else
                            Puts($"Killed {killCount} crates for tier '{targetTier}'.");
                        break;
                    }

                case "unlock":
                    {
                        int unlockCount = 0;
                        foreach (var crate in crateList)
                        {
                            if (crate == null || crate.IsDestroyed) continue;

                            List<FireBall> childFireBalls = FindChildrenOfType<FireBall>(crate);
                            if (childFireBalls.Count > 0)
                            {
                                foreach (var fireball in childFireBalls)
                                {
                                    if (fireball != null && !fireball.IsDestroyed)
                                    {
                                        fireball.Extinguish();
                                        unlockCount++;
                                    }
                                }
                            }
                        }

                        if (caller != null)
                            MessagePlayer(caller, $"Extinguished {unlockCount} locking fireballs on crates for tier '{targetTier}'.");
                        else
                            Puts($"Extinguished {unlockCount} locking fireballs on crates for tier '{targetTier}'.");
                        break;
                    }

                default:
                    {
                        if (caller != null)
                            MessagePlayer(caller, "Unknown subcommand. Usage: cht.crate kill <tierName> or cht.crate unlock <tierName>");
                        else
                            Puts("Unknown subcommand. Usage: cht.crate kill <tierName> or cht.crate unlock <tierName>");
                        break;
                    }
            }
        }
        
        public void cmdHeliShop(BasePlayer player, string cmd, string[] args)
        {
            if (player == null)
                return;

            OpenShopUI(player);
        }

        /// <summary>Shop list order: Easy → Medium → Hard → Elite → VIP → Vanilla → others A–Z.</summary>
        private static int GetShopTierSortOrder(string tierName)
        {
            if (string.IsNullOrEmpty(tierName))
                return 1000;

            switch (tierName.Trim().ToLowerInvariant())
            {
                case "easy": return 10;
                case "medium": return 20;
                case "hard": return 30;
                case "elite":
                case "nightmare": return 40;
                case "vip": return 50;
                case "vanilla": return 60;
                default: return 100;
            }
        }

        #endregion Commands

        #region Localization

        private class Lang
        {
            public const string NoPermission = "NoPermission";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.NoPermission] = "You do not have permission to use this command.",

            }, this, "en");
        }

        private static string GetMessage(BasePlayer player, string messageKey, params object[] args)
        {
            string message = _plugin.lang.GetMessage(messageKey, _plugin, player.UserIDString);

            if (args.Length > 0)
                message = string.Format(message, args);

            return message;
        }

        public static void MessagePlayer(BasePlayer player, string messageKey, params object[] args)
        {
            string message = GetMessage(player, messageKey, args);
            _plugin.SendReply(player, message);
        }

        public static void ShowToast(BasePlayer player, string messageKey, GameTip.Styles style = GameTip.Styles.Blue_Normal, params object[] args)
        {
            if (player == null) return;
            string message = GetMessage(player, messageKey, args);
            // Framework §13: never use obsolete gametip.showtoast.
            try
            {
                player.SendConsoleCommand("gametip.showtoast_translated", (int)style, "cht.toast", message, false, Array.Empty<string>());
            }
            catch
            {
                player.SendConsoleCommand("gametip.showgametip", message);
            }
        }

        #endregion Localization

        // Combat Harmony patches live in CHT.Patches.CombatPatches (top-level) so Facepunch PatchAll applies them.

        #region Shop UI

        private const string UI_OVERLAY = "cht-ui-overlay";
        private const string UI_CONTENT = "cht-ui-content";
        private const string UI_DETAILS_OVERLAY = "cht-ui-details-overlay";
        private const string UI_DETAILS_CONTENT = "cht-ui-details-content";
        // Shop / ServerPanel use OverlayNonScaled. Overlay sits underneath, so a Shop purchase
        // that leaves UI.Shop open would bury the heli menu.
        private const string UI_PARENT = "OverlayNonScaled";

        private const string FX_CLICK_SUCCESS = "assets/bundled/prefabs/fx/notice/loot.drag.grab.fx.prefab";
        private const string FX_CLICK_DENIED = "assets/prefabs/locks/keypad/effects/lock.code.denied.prefab";

        [ConsoleCommand(Cmd.SHOP_CONTROLLER)]
        public void cmdShopController(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs())
                return;

            string subCmd = arg.GetString(0).ToLowerInvariant();
            switch (subCmd)
            {
                case "open":
                    {
                        int page = 1;
                        if (arg.Args.Length >= 2)
                        {
                            int.TryParse(arg.GetString(1), out page);
                            if (page < 1) page = 1;
                        }
                        PlayClickFX(player, FX_CLICK_SUCCESS);
                        OpenShopUI(player, page);
                        break;
                    }

                case "showdetails":
                    {
                        if (arg.Args.Length < 2)
                            return;

                        string tierName = arg.GetString(1);

                        TierData tierData = _tieredHelicopterManager.GetTierDataByTierName(tierName);
                        if (tierData == null) return;

                        PlayClickFX(player, FX_CLICK_SUCCESS);
                        OpenTierDetailsUI(player, tierData);
                        break;
                    }

                case "closedetails":
                    {
                        PlayClickFX(player, FX_CLICK_SUCCESS);
                        CuiHelper.DestroyUi(player, UI_DETAILS_OVERLAY);
                        OpenShopUI(player);
                        break;
                    }

                case "buy":
                    {
                        if (arg.Args.Length < 2)
                            return;

                        string tierName = arg.GetString(1);

                        TierData tierData = _tieredHelicopterManager.GetTierDataByTierName(tierName);
                        if (tierData == null)
                        {
                            MessagePlayer(player, $"Tier '{tierName}' not found!");
                            PlayClickFX(player, FX_CLICK_DENIED);
                            return;
                        }

                        CallProfileData callProfile = GetCallProfileFor(player, tierData);
                        if (callProfile == null || !callProfile.Enabled)
                        {
                            MessagePlayer(player, $"No valid call profile for Tier '{tierName}'.");
                            OpenTierDetailsUI(player, tierData);
                            PlayClickFX(player, FX_CLICK_DENIED);
                            return;
                        }

                        double remain = GetTimeLeftUntilHeliCall(player, callProfile);
                        if (remain > 0)
                        {
                            MessagePlayer(player, $"Wait {FormatTime(remain)} before calling again.");
                            OpenTierDetailsUI(player, tierData);
                            PlayClickFX(player, FX_CLICK_DENIED);
                            return;
                        }

                        int usedToday = GetDailyCallsUsed(player, callProfile);
                        if (callProfile.DailyCallLimit > 0 && usedToday >= callProfile.DailyCallLimit)
                        {
                            MessagePlayer(player, $"Daily limit reached. You can only call this helicopter {callProfile.DailyCallLimit} time(s) today.");
                            OpenTierDetailsUI(player, tierData);
                            PlayClickFX(player, FX_CLICK_DENIED);
                            return;
                        }

                        if (!CheckAffordability(player, callProfile))
                        {
                            MessagePlayer(player, "You can't afford this helicopter call.");
                            OpenTierDetailsUI(player, tierData);
                            PlayClickFX(player, FX_CLICK_DENIED);
                            return;
                        }

                        HelicopterSpawnerComponent spawner = _tieredHelicopterManager.GetSpawnerByTierName(tierData.Name);
                        if (spawner == null)
                        {
                            MessagePlayer(player, "No spawner available for that Tier!");
                            PlayClickFX(player, FX_CLICK_DENIED);
                            return;
                        }

                        bool popLimit = PopulationLimitReached(tierData);
                        if (popLimit)
                        {
                            MessagePlayer(player, "Population limit reached. Try again later.");
                            OpenTierDetailsUI(player, tierData);
                            PlayClickFX(player, FX_CLICK_DENIED);
                            return;
                        }

                        var (success, explanation, spawnedCount) = spawner.TrySpawn(callProfile.NumberToSpawn, player);
                        if (!success)
                        {
                            MessagePlayer(player, explanation);
                            OpenTierDetailsUI(player, tierData);
                            PlayClickFX(player, FX_CLICK_DENIED);
                            return;
                        }

                        foreach (var currency in callProfile.CostToCall)
                        {
                            if (currency.Enabled && currency.Valid)
                            {
                                currency.Charge(player);
                            }
                        }

                        SetNextAllowedHeliCallTime(player, callProfile);
                        IncrementDailyCalls(player, callProfile);
                        MessagePlayer(player, explanation);
                        BroadcastHelicopterCall(callProfile, tierData, player);
                        PlayClickFX(player, FX_CLICK_SUCCESS);
                        CuiHelper.DestroyUi(player, UI_DETAILS_OVERLAY);
                        OpenShopUI(player);
                        break;
                    }

                case "close":
                    {
                        PlayClickFX(player, FX_CLICK_SUCCESS);
                        break;
                    }

                case "deny":
                    {
                        PlayClickFX(player, FX_CLICK_DENIED);
                        break;
                    }
            }
        }

        private void OpenShopUI(BasePlayer player, int page = 1)
        {
            CuiHelper.DestroyUi(player, UI_OVERLAY);
            var container = new CuiElementContainer();

            container.Add(new CuiElement
            {
                Name = UI_OVERLAY,
                Parent = UI_PARENT,
                Components =
        {
            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" },
            new CuiImageComponent { Color = "0.14 0.15 0.16 0.88" },
            new CuiNeedsCursorComponent()
        }
            });

            container.Add(new CuiButton
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Button =
        {
            Close = UI_OVERLAY,
            Color = "0 0 0 0"
        },
                Text =
        {
            Text = "",
            Font = "permanentmarker.ttf"
        }
            },
            UI_OVERLAY);

            container.Add(new CuiPanel
            {
                RectTransform =
        {
            AnchorMin = "0.5 0.5",
            AnchorMax = "0.5 0.5",
            OffsetMin = "-280 -340",
            OffsetMax = "280 340"
        },
                Image =
        {
            Color = "0.23 0.25 0.28 0.85",
            Material = "assets/content/ui/uibackgroundblur.mat"
        }
            },
            UI_OVERLAY,
            UI_CONTENT);

            container.Add(new CuiLabel
            {
                RectTransform =
        {
            AnchorMin = "0 0.93",
            AnchorMax = "1 1",
            OffsetMin = "0 0",
            OffsetMax = "0 -10"
        },
                Text =
        {
            Text = "<color=#D2D2CF><size=22>Helicopter Shop</size></color>",
            Font = "permanentmarker.ttf",
            FontSize = 22,
            Align = TextAnchor.MiddleCenter
        }
            },
            UI_CONTENT);

            List<TierData> validTiersAll = new List<TierData>();
            foreach (var spawner in _tieredHelicopterManager.GetAllSpawners())
            {
                if (spawner?.TierData == null) continue;
                var callProfile = GetCallProfileFor(player, spawner.TierData);
                if (callProfile != null && callProfile.Enabled)
                    validTiersAll.Add(spawner.TierData);
            }

            // Difficulty order for the shop UI (file/dict order is alphabetical: Hard before Medium).
            validTiersAll.Sort((a, b) =>
            {
                int cmp = GetShopTierSortOrder(a?.Name).CompareTo(GetShopTierSortOrder(b?.Name));
                return cmp != 0 ? cmp : string.Compare(a?.Name, b?.Name, StringComparison.OrdinalIgnoreCase);
            });

            int totalPages = (validTiersAll.Count + 9) / 10;
            if (totalPages < 1) totalPages = 1;
            page = Mathf.Clamp(page, 1, totalPages);

            if (totalPages > 1)
            {
                float baseX = 0.02f;
                float baseY = 0.93f;
                float btnWidth = 0.035f;
                float btnHeight = 0.05f;
                float gap = 0.002f;

                for (int p = 1; p <= totalPages; p++)
                {
                    float offsetX = baseX + (p - 1) * (btnWidth + gap);
                    bool isActive = (p == page);

                    container.Add(new CuiButton
                    {
                        RectTransform =
                {
                    AnchorMin = $"{offsetX} {baseY}",
                    AnchorMax = $"{offsetX + btnWidth} {baseY + btnHeight}"
                },
                        Button =
                {
                    Command = $"cht.shopcontroller open {p}",
                    Color   = isActive ? "0.55 0.55 0.25 1.0" : "0.35 0.40 0.45 1.0"
                },
                        Text =
                {
                    Text     = p.ToString(),
                    Font     = "permanentmarker.ttf",
                    FontSize = 14,
                    Align    = TextAnchor.MiddleCenter,
                    Color    = "0.88 0.9 0.92 1.0"
                }
                    },
                    UI_CONTENT);
                }
            }

            container.Add(new CuiButton
            {
                RectTransform =
        {
            AnchorMin = "1 1",
            AnchorMax = "1 1",
            OffsetMin = "-30 -30",
            OffsetMax = "-5 -5"
        },
                Button =
        {
            Command = "cht.shopcontroller close",
            Close   = UI_OVERLAY,
            Color   = "0.55 0.22 0.22 1.0",
            Sprite  = "assets/icons/close.png"
        },
                Text =
        {
            Text     = "",
            Font     = "permanentmarker.ttf",
            FontSize = 0,
            Align    = TextAnchor.MiddleCenter
        }
            },
            UI_CONTENT);

            const string contentPanel = UI_CONTENT + ".Content";
            container.Add(new CuiPanel
            {
                RectTransform =
        {
            AnchorMin = "0 0",
            AnchorMax = "1 1",
            OffsetMin = "20 20",
            OffsetMax = "-20 -60"
        },
                Image = { Color = "0 0 0 0" }
            },
            UI_CONTENT,
            contentPanel);

            var validTiers = validTiersAll
                .Skip((page - 1) * 10)
                .Take(10)
                .ToList();

            float rowHeight = 0.1f;
            for (int i = 0; i < validTiers.Count; i++)
            {
                TierData tierData = validTiers[i];
                var callProfile = GetCallProfileFor(player, tierData);
                if (callProfile == null) continue;

                float top = 1f - (rowHeight * i);
                float bottom = top - rowHeight;
                string rowPanelName = $"{contentPanel}.Row{i}";

                container.Add(new CuiPanel
                {
                    RectTransform =
            {
                AnchorMin = $"0 {bottom}",
                AnchorMax = $"1 {top}"
            },
                    Image =
            {
                Color = "0.25 0.27 0.3 0.8"
            }
                },
                contentPanel,
                rowPanelName);

                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0.02 0", AnchorMax = "0.4 1" },
                    Text =
            {
                Text  = $"<color=#DFDFDC><size=14>{tierData.Name}</size></color>",
                Font  = "permanentmarker.ttf",
                Align = TextAnchor.MiddleLeft
            }
                },
                rowPanelName);

                double timeLeft = GetTimeLeftUntilHeliCall(player, callProfile);
                bool isOnCooldown = (timeLeft > 0);
                bool canAfford = CheckAffordability(player, callProfile);
                bool popLimit = PopulationLimitReached(tierData);

                int usedToday = GetDailyCallsUsed(player, callProfile);
                bool dailyLimitReached = callProfile.DailyCallLimit > 0 && usedToday >= callProfile.DailyCallLimit;

                string statusText;
                if (popLimit)
                    statusText = "Population limit";
                else if (dailyLimitReached)
                    statusText = "Daily limit reached";
                else if (isOnCooldown)
                    statusText = $"Available again in: {FormatTime(timeLeft)}";
                else if (!canAfford)
                    statusText = "Insufficient resources";
                else
                    statusText = "Ready";

                container.Add(new CuiLabel
                {
                    RectTransform =
            {
                AnchorMin = "0.42 0",
                AnchorMax = "0.80 1"
            },
                    Text =
            {
                Text  = $"<color=#E9D58B><size=12>{statusText}</size></color>",
                Font  = "permanentmarker.ttf",
                Align = TextAnchor.MiddleRight
            }
                },
                rowPanelName);

                string detailsCmd = $"cht.shopcontroller showdetails {tierData.Name}";
                container.Add(new CuiButton
                {
                    RectTransform =
            {
                AnchorMin = "0.82 0.2",
                AnchorMax = "0.95 0.8"
            },
                    Button =
            {
                Command = detailsCmd,
                Color   = "0.35 0.40 0.45 1.0"
            },
                    Text =
            {
                Text     = "VIEW",
                Font     = "permanentmarker.ttf",
                FontSize = 14,
                Align    = TextAnchor.MiddleCenter,
                Color    = "0.88 0.9 0.92 1.0"
            }
                },
                rowPanelName);
            }

            CuiHelper.AddUi(player, container);
        }

        private void OpenTierDetailsUI(BasePlayer player, TierData tierData)
        {
            CuiHelper.DestroyUi(player, UI_DETAILS_OVERLAY);
            var container = new CuiElementContainer();

            container.Add(new CuiElement
            {
                Name = UI_DETAILS_OVERLAY,
                Parent = UI_PARENT,
                Components =
        {
            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" },
            new CuiImageComponent { Color = "0.14 0.15 0.16 0.88" },
            new CuiNeedsCursorComponent()
        }
            });

            container.Add(new CuiButton
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Button =
        {
            Command = "cht.shopcontroller close",
            Close   = UI_DETAILS_OVERLAY,
            Color   = "0 0 0 0"
        },
                Text =
        {
            Text = "",
            Font = "permanentmarker.ttf"
        }
            },
            UI_DETAILS_OVERLAY);

            container.Add(new CuiPanel
            {
                RectTransform =
        {
            AnchorMin = "0.5 0.5",
            AnchorMax = "0.5 0.5",
            OffsetMin = "-400 -320",
            OffsetMax = "400 320"
        },
                Image =
        {
            Color    = "0.23 0.25 0.28 0.85",
            Material = "assets/content/ui/uibackgroundblur.mat"
        }
            },
            UI_DETAILS_OVERLAY,
            UI_DETAILS_CONTENT);

            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "0 0.92", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 -10" },
                Text =
        {
            Text     = $"<color=#D2D2CF><size=20>{tierData.Name} - Details</size></color>",
            Font     = "permanentmarker.ttf",
            FontSize = 20,
            Align    = TextAnchor.MiddleCenter
        }
            },
            UI_DETAILS_CONTENT);

            container.Add(new CuiButton
            {
                RectTransform =
        {
            AnchorMin = "1 1",
            AnchorMax = "1 1",
            OffsetMin = "-30 -30",
            OffsetMax = "-5 -5"
        },
                Button =
        {
            Command = "cht.shopcontroller close",
            Close   = UI_DETAILS_OVERLAY,
            Color   = "0.55 0.22 0.22 1.0",
            Sprite  = "assets/icons/close.png"
        },
                Text =
        {
            Text     = "",
            Font     = "permanentmarker.ttf",
            FontSize = 0,
            Align    = TextAnchor.MiddleCenter
        }
            },
            UI_DETAILS_CONTENT);

            const string bodyPanel = UI_DETAILS_CONTENT + ".Body";
            const string footerPanel = UI_DETAILS_CONTENT + ".Footer";

            container.Add(new CuiPanel
            {
                RectTransform =
        {
            AnchorMin = "0 0.2",
            AnchorMax = "1 0.92",
            OffsetMin = "10 0",
            OffsetMax = "-10 -5"
        },
                Image = { Color = "0 0 0 0" }
            },
            UI_DETAILS_CONTENT,
            bodyPanel);

            container.Add(new CuiPanel
            {
                RectTransform =
        {
            AnchorMin = "0 0",
            AnchorMax = "1 0.2",
            OffsetMin = "10 10",
            OffsetMax = "-10 -5"
        },
                Image = { Color = "0 0 0 0" }
            },
            UI_DETAILS_CONTENT,
            footerPanel);

            var callProfile = GetCallProfileFor(player, tierData);
            if (callProfile == null || !callProfile.Enabled)
            {
                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    Text =
            {
                Text  = "<color=#D3AAAA><size=16>No valid call profile found!</size></color>",
                Font  = "permanentmarker.ttf",
                Align = TextAnchor.MiddleCenter
            }
                },
                bodyPanel);

                CuiHelper.AddUi(player, container);
                return;
            }

            const string costInfoLabel = bodyPanel + ".CostInfo";
            container.Add(new CuiLabel
            {
                RectTransform =
        {
            AnchorMin = "0 0.9",
            AnchorMax = "1 1",
            OffsetMin = "0 0",
            OffsetMax = "0 -5"
        },
                Text =
        {
            Text  = "<color=#DEDEDC><size=14>Below are the resources required to call this helicopter:</size></color>",
            Font  = "permanentmarker.ttf",
            Align = TextAnchor.MiddleCenter
        }
            },
            bodyPanel,
            costInfoLabel);

            const string tablePanel = bodyPanel + ".Table";
            container.Add(new CuiPanel
            {
                RectTransform =
        {
            AnchorMin = "0 0",
            AnchorMax = "1 0.9",
            OffsetMin = "0 0",
            OffsetMax = "0 0"
        },
                Image = { Color = "0 0 0 0" }
            },
            bodyPanel,
            tablePanel);

            var costs = callProfile.CostToCall.Where(c => c.Enabled && c.Valid).ToList();
            if (costs.Count == 0)
            {
                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0 0.4", AnchorMax = "1 0.6" },
                    Text =
            {
                Text  = "<color=#E2E2E2><size=14>Free</size></color>",
                Font  = "permanentmarker.ttf",
                Align = TextAnchor.MiddleCenter
            }
                },
                tablePanel);
            }
            else
            {
                float rowHeight = 0.07f;
                AddTableRow(container, tablePanel, -1, rowHeight,
                    "NEEDED", "RESOURCE", "YOU HAVE",
                    header: true);

                for (int i = 0; i < costs.Count; i++)
                {
                    var currency = costs[i];
                    string itemName = currency.GetDisplayName();
                    int amount = currency.Amount;
                    int have = (int)currency.PaymentGateway.Get(player);
                    bool enough = (have >= amount);

                    AddTableRow(
                        container,
                        tablePanel,
                        i,
                        rowHeight,
                        $"{amount}",
                        itemName,
                        $"{have:N0}",
                        header: false,
                        insufficient: !enough
                    );
                }
            }

            double remain = GetTimeLeftUntilHeliCall(player, callProfile);
            bool isOnCooldown = (remain > 0);
            bool canAffordAll = CheckAffordability(player, callProfile);
            bool popLimit = PopulationLimitReached(tierData);

            int usedToday = GetDailyCallsUsed(player, callProfile);
            bool dailyLimitReached = callProfile.DailyCallLimit > 0 && usedToday >= callProfile.DailyCallLimit;

            string callButtonText;
            string callButtonColor;
            string callButtonCommand = $"cht.shopcontroller buy {tierData.Name}";

            if (popLimit)
            {
                callButtonText = "LIMIT REACHED";
                callButtonColor = "0.45 0.45 0.1 1.0";
                callButtonCommand = "cht.shopcontroller deny";
            }
            else if (dailyLimitReached)
            {
                callButtonText = "DAILY LIMIT";
                callButtonColor = "0.55 0.42 0.25 1.0";
                callButtonCommand = "cht.shopcontroller deny";
            }
            else if (isOnCooldown)
            {
                callButtonText = "COOLDOWN";
                callButtonColor = "0.55 0.42 0.25 1.0";
                container.Add(new CuiLabel
                {
                    RectTransform =
            {
                AnchorMin = "0 0.6",
                AnchorMax = "1 1"
            },
                    Text =
            {
                Text  = $"<color=#F0DDA0><size=14>Time left: {FormatTime(remain)}</size></color>",
                Font  = "permanentmarker.ttf",
                Align = TextAnchor.MiddleCenter
            }
                },
                footerPanel);
                callButtonCommand = "cht.shopcontroller deny";
            }
            else if (!canAffordAll)
            {
                callButtonText = "INSUFFICIENT";
                callButtonColor = "0.55 0.25 0.25 1.0";
                callButtonCommand = "cht.shopcontroller deny";
            }
            else
            {
                callButtonText = "CALL";
                callButtonColor = "0.25 0.46 0.25 1.0";
                callButtonCommand = $"cht.shopcontroller buy {tierData.Name}";
            }

            container.Add(new CuiButton
            {
                RectTransform =
        {
            AnchorMin = "0.3 0.1",
            AnchorMax = "0.7 0.5"
        },
                Button =
        {
            Command = callButtonCommand,
            Color   = callButtonColor
        },
                Text =
        {
            Text     = callButtonText,
            Font     = "permanentmarker.ttf",
            FontSize = 14,
            Align    = TextAnchor.MiddleCenter,
            Color    = "0.85 0.9 0.85 1.0"
        }
            },
            footerPanel);

            CuiHelper.AddUi(player, container);
        }

        private void AddTableRow(CuiElementContainer container, string parent, int rowIndex, float rowHeight, string colNeeded, string colItem, string colYouHave, bool header = false, bool insufficient = false)
        {
            float top, bottom;
            if (rowIndex < 0)
            {
                top = 1f;
                bottom = 1f - rowHeight;
            }
            else
            {
                top = 1f - (rowIndex + 1) * rowHeight;
                bottom = top - rowHeight;
            }

            float col0Width = 0.20f;
            float col1Width = 0.50f;
            float col2Width = 0.30f;

            string baseColor;
            if (header)
                baseColor = "0.18 0.18 0.18 0.95";
            else if (insufficient)
                baseColor = "0.45 0.15 0.15 0.75";
            else
                baseColor = "0.12 0.12 0.13 0.75";

            string normalTextColor = header ? "#DDDDDD" : "#ECECEC";
            string textColor = insufficient ? "#E7DB8F" : normalTextColor;
            string fontSize = header ? "14" : "13";

            TextAnchor col0Align = TextAnchor.MiddleRight;
            TextAnchor col1Align = TextAnchor.MiddleLeft;
            TextAnchor col2Align = TextAnchor.MiddleLeft;

            {
                float left = 0f;
                float right = left + col0Width;
                string cellName = $"{parent}.Row{rowIndex}.Col0";

                container.Add(new CuiPanel
                {
                    RectTransform =
            {
                AnchorMin = $"{left} {bottom}",
                AnchorMax = $"{right} {top}"
            },
                    Image = { Color = baseColor }
                }, parent, cellName);

                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0.02 0", AnchorMax = "0.98 1" },
                    Text =
            {
                Text  = $"<color={textColor}><size={fontSize}>{colNeeded}</size></color>",
                Font  = "permanentmarker.ttf",
                Align = col0Align
            }
                }, cellName);
            }

            {
                float left = col0Width;
                float right = left + col1Width;
                string cellName = $"{parent}.Row{rowIndex}.Col1";

                container.Add(new CuiPanel
                {
                    RectTransform =
            {
                AnchorMin = $"{left} {bottom}",
                AnchorMax = $"{right} {top}"
            },
                    Image = { Color = baseColor }
                }, parent, cellName);

                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0.02 0", AnchorMax = "0.98 1" },
                    Text =
            {
                Text  = $"<color={textColor}><size={fontSize}>{colItem}</size></color>",
                Font  = "permanentmarker.ttf",
                Align = col1Align
            }
                }, cellName);
            }

            {
                float left = col0Width + col1Width;
                float right = left + col2Width;
                string cellName = $"{parent}.Row{rowIndex}.Col2";

                container.Add(new CuiPanel
                {
                    RectTransform =
            {
                AnchorMin = $"{left} {bottom}",
                AnchorMax = $"{right} {top}"
            },
                    Image = { Color = baseColor }
                }, parent, cellName);

                container.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0.02 0", AnchorMax = "0.98 1" },
                    Text =
            {
                Text  = $"<color={textColor}><size={fontSize}>{colYouHave}</size></color>",
                Font  = "permanentmarker.ttf",
                Align = col2Align
            }
                }, cellName);
            }
        }

        public static void PlayClickFX(BasePlayer player, string effect)
        {
            if (player == null || player.net == null || player.net.connection == null)
                return;

            var receivingPlayers = Pool.Get<List<Connection>>();
            receivingPlayers.Add(player.net.connection);

            RunEffectAttachedToEntity(
                effect,
                player,
                0u,
                Vector3.zero,
                Vector3.zero,
                null,
                false,
                receivingPlayers
            );

            Pool.FreeUnmanaged(ref receivingPlayers);
        }

        #endregion Shop UI

        // Harmony lifecycle and patch dispatch entry points.
        public void CallInit() => Init();
        public void CallUnload() => Unload();
        public void CallOnServerInitialized() => OnServerInitialized(true);
        public static IEnumerable<string> GetRegisteredPermissions() => PermissionUtil.Permissions;
        public string GetShopChatCommand() => _config?.HeliShopChatCommand ?? "heli.shop";
    }
}