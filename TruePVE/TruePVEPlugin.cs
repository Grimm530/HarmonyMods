using Facepunch;
using Facepunch.Math;
using HarmonyLib;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Rust;
using Rust.Ai.Gen2;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("TruePVE", "Nivex & Grimm530", "2.4.31")]
    [Description("Improvement of the default Rust PVE behavior")]
    // Thanks to the original author, ignignokt84.
    public partial class TruePVE : RustPlugin
    {
        #region Planterbox Protection (Harmony Patches)
        
        [AutoPatch]
        [HarmonyPatch(typeof(GrowableEntity), "TakeClones", new Type[] { typeof(BasePlayer) })]
        public class Patch_TakeClones
        {
            public static bool Prefix(GrowableEntity __instance, BasePlayer player)
            {
                var instance = Instance;
                if (instance == null) return true;
                return instance.AllowGrowableHarvest(__instance, player, nameof(CanTakeCutting));
            }
        }

        [AutoPatch]
        [HarmonyPatch(typeof(GrowableEntity), "PickFruit", new Type[] { typeof(BasePlayer), typeof(bool) })]
        public class Patch_PickFruit
        {
            public static bool Prefix(GrowableEntity __instance, BasePlayer player, bool eat)
            {
                var instance = Instance;
                if (instance == null) return true;
                return instance.AllowGrowableHarvest(__instance, player, nameof(OnGrowableGather));
            }
        }

        private static TruePVE Instance { get; set; }

        #endregion

        #region Variables
        // config/data container
        private Configuration config = new();

        [PluginReference] Plugin 
        AbandonedBases, 
        BradleyDrops, 
        Clans, 
        Convoy,
        CustomHelicopterTiers2, 
        DynamicPVP, 
        Economics,
        Friends, 
        HeliSignals, 
        HelpfulSupply, 
        LiteZones, 
        NpcRandomRaids, 
        Permissions,
        PersonalHeli, 
        RaidableBases, 
        RustRewards, 
        ShoppyStock, 
        SkillTree, 
        XLevels, 
        XPerience, 
        ZoneManager;

        public string usageString;
        public enum Command { def, sched, trace, usage, enable, sleepers, map, unmap };
        public enum DamageResult { None, Allow, Block }

        [Flags]
        public enum RuleFlags : ulong
        {
            None = 0,
            AdminsHurtSleepers = 1uL << 1,
            AdminsHurtPlayers = 1uL << 2,
            AnimalsIgnoreSleepers = 1uL << 3,
            AuthorizedDamage = 1uL << 4,
            AuthorizedDamageRequiresOwnership = 1uL << 5,
            CupboardOwnership = 1uL << 6,
            FriendlyFire = 1uL << 7,
            HeliDamageLocked = 1uL << 8,
            HumanNPCDamage = 1uL << 9,
            LockedBoxesImmortal = 1uL << 10,
            LockedDoorsImmortal = 1uL << 11,
            NoPlayerDamageToCar = 1uL << 12,
            NoPlayerDamageToMini = 1uL << 13,
            NoPlayerDamageToScrap = 1uL << 14,
            NoHeliDamage = 1uL << 15,
            NoHeliDamagePlayer = 1uL << 16,
            NoHeliDamageQuarry = 1uL << 17,
            NoHeliDamageRidableHorses = 1uL << 18,
            NoHeliDamageSleepers = 1uL << 19,
            NoMLRSDamage = 1uL << 20,
            NpcsCanHurtAnything = 1uL << 21,
            PlayerSamSitesIgnorePlayers = 1uL << 22,
            ProtectedSleepers = 1uL << 23,
            TrapsIgnorePlayers = 1uL << 24,
            TrapsIgnoreScientist = 1uL << 25,
            TurretsIgnorePlayers = 1uL << 26,
            TurretsIgnoreScientist = 1uL << 27,
            StaticTurretsIgnoreScientist = 1uL << 28,
            TwigDamage = 1uL << 29,
            TwigDamageRequiresOwnership = 1uL << 30,
            VehiclesTakeCollisionDamageWithoutDriver = 1uL << 31,
            SamSitesIgnoreMLRS = 1uL << 32,
            SelfDamage = 1uL << 33,
            StaticSamSitesIgnorePlayers = 1uL << 34,
            StaticTurretsIgnorePlayers = 1uL << 35,
            SafeZoneTurretsIgnorePlayers = 1uL << 36,
            SuicideBlocked = 1uL << 37,
            NoHeliDamageBuildings = 1uL << 38,
            WoodenDamage = 1uL << 39,
            WoodenDamageRequiresOwnership = 1uL << 40,
            AuthorizedDamageCheckPrivilege = 1uL << 41,
            ExcludeTugboatFromImmortalFlags = 1uL << 42,
            LockedVehiclesImmortal = 1uL << 43,
            TurretsIgnoreBradley = 1uL << 44,
            AuthorizedFarmableDamage = 1uL << 45,
            HopperCannotTargetEnemyLoot = 1uL << 46,
            VehiclesTakeCollisionDamage = 1uL << 47
        }

        private bool IsUnloading;
        private Timer scheduleUpdateTimer;                              // timer to check for schedule updates
        private bool shareRedirectDudEnabled;                           // undocumented. UAYOR.
        private RuleSet dudRuleSet;                                     // dud ruleset when no locations are shared
        private RuleSet currentRuleSet;                                 // current ruleset
        private string currentBroadcastMessage;                         // current broadcast message
        private bool useZones;                                          // internal useZones flag
        private const string Any = "any";                               // constant "any" string for rules
        private const string AllZones = "allzones";                     // constant "allzones" string for mappings
        private const string PermCanMap = "truepve.canmap";             // permission for mapping command
        private bool animalsIgnoreSleepers;                             // toggle flag to protect sleepers
        private bool trace;                                             // trace flag
        private const string traceFile = "ruletrace";                   // tracefile name
        private const float traceTimeout = 300f;                        // auto-disable trace after 300s (5m)
        private Timer traceTimer;                                       // trace timeout timer
        private bool tpveEnabled = true;                                // toggle flag for damage handling
        private List<DamageType> _damageTypes = new()
        {
            DamageType.Arrow,
            DamageType.Blunt,
            DamageType.Bullet,
            DamageType.Explosion,
            DamageType.Cold,
            DamageType.Heat,
            DamageType.Generic,
            DamageType.Slash,
            DamageType.Stab,
        };

        private uint maincannonshell = 3032863244;
        private uint trainbarricade = 1221760186;
        private uint trainbarricadeheavy = 1363243026;
        private uint loot_trash = 3279100614;
        private uint giftbox_loot = 2216891097;
        private uint campfire = 4160694184;
        private uint oilfireballsmall = 3550347674;
        private uint heli_napalm = 184893264;
        private uint rocket_heli_napalm = 200672762;
        private uint rocket_heli = 129320027;

        private bool excludeAllZones;
        private readonly Dictionary<ulong, double> _waiting = new();
        private readonly HashSet<string> _deployables = new();
        private readonly HashSet<string> exclusionLocationsSet = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<ulong, List<PlayerExclusion>> playerDelayExclusions = new();
        private readonly Dictionary<string, RuleSet> ruleSetByNameDictionary = new(StringComparer.OrdinalIgnoreCase);

        // Integrated Loot Defender state
        private enum LDDamageType { None, Bradley, Heli, NPC }
        
        private class LockoutInfo
        {
            public double Bradley { get; set; }
            public double Heli { get; set; }
            public bool Any() => Bradley > 0 || Heli > 0;
        }
        private class LDDamageEntry
        {
            public float DamageDealt;
            public DateTime Timestamp;
            public ulong TeamID;
            public string Weapon = "";
            public LDDamageEntry() { }
            public LDDamageEntry(ulong teamID)
            {
                DamageDealt = 0f;
                Timestamp = DateTime.Now;
                TeamID = teamID;
                Weapon = "";
            }
            public bool IsOutdated(int timeoutSeconds) => timeoutSeconds > 0 && DateTime.Now.Subtract(Timestamp).TotalSeconds >= timeoutSeconds;
        }
        private class LDDamageKey
        {
            public ulong UserId;
            public string Name;
            public LDDamageEntry Entry;
            [JsonIgnore]
            public BasePlayer Player;
            public LDDamageKey() { }
            public LDDamageKey(BasePlayer player)
            {
                Player = player;
                UserId = player?.userID ?? 0;
                Name = player?.displayName ?? string.Empty;
                Entry = new(player != null ? player.currentTeam : 0);
            }
        }
        private class LDDamageInfo
        {
            public List<LDDamageKey> Keys = new();
            public LDDamageType Type = LDDamageType.None;
            public Vector3 Position;
            public DateTime Start;
            public ulong VictimId;
            public int LockSeconds;
            public float LockRadius;
            public bool IsKilled;
            public LDDamageInfo() { }
            public LDDamageInfo(LDDamageType type, BaseEntity victim, DateTime start, int lockSeconds, float lockRadius)
            {
                Type = type;
                VictimId = victim?.net?.ID.Value ?? 0;
                Position = victim != null ? victim.transform.position : Vector3.zero;
                Start = start;
                LockSeconds = lockSeconds;
                LockRadius = lockRadius;
            }
            public void AddDamage(BaseCombatEntity entity, BasePlayer attacker, float amount, string weapon = "")
            {
                if (attacker == null || amount <= 0f) return;
                for (int i = 0; i < Keys.Count; i++)
                {
                    var key = Keys[i];
                    if (key.UserId == attacker.userID)
                    {
                        key.Entry.DamageDealt += amount;
                        key.Entry.Timestamp = DateTime.Now;
                        key.Entry.TeamID = attacker.currentTeam;
                        if (!string.IsNullOrEmpty(weapon)) key.Entry.Weapon = weapon;
                        return;
                    }
                }
                var dk = new LDDamageKey(attacker);
                dk.Entry.DamageDealt = amount;
                if (!string.IsNullOrEmpty(weapon)) dk.Entry.Weapon = weapon;
                Keys.Add(dk);
            }
            public float TotalDamage()
            {
                float t = 0f;
                for (int i = 0; i < Keys.Count; i++) t += Keys[i].Entry.DamageDealt;
                return t;
            }
        }
        private class LDLockInfo
        {
            public HashSet<ulong> Owners = new();
            public DateTime LockedAt;
            public int LockSeconds; // 0 = forever
            public bool AllowAllies;
            public bool GroupByTeam;
            [JsonIgnore]
            public Timer ExpireTimer;
            public bool IsExpired => LockSeconds > 0 && DateTime.Now.Subtract(LockedAt).TotalSeconds >= LockSeconds;
            public bool CanInteract(ulong userId, BasePlayer player, Func<ulong, ulong, bool> isAlly)
            {
                if (Owners.Contains(userId)) return true;
                if (AllowAllies && player != null)
                {
                    foreach (var owner in Owners)
                    {
                        if (isAlly(owner, userId)) return true;
                    }
                }
                return false;
            }
        }

        private readonly Dictionary<ulong, LDDamageInfo> _ldDamage = new(); // victimId -> damage info
        private readonly Dictionary<ulong, LDLockInfo> _ldLocks = new();   // entityId -> lock info
        private readonly Dictionary<string, DateTime> _ownerToastLast = new(); // "attacker:victim" -> last toast time
        // Supply drop owner tracking (plane -> player)
        private readonly Dictionary<ulong, ulong> _supplyPlaneOwner = new();
        // Track supply signals that already spawned a drop via bypass at throw-time
        private readonly HashSet<ulong> _bypassedSupplySignals = new();
        private const string SupplyDropPrefab = "assets/prefabs/misc/supply drop/supply_drop.prefab";
        
        // F15 event tracking
        private bool _isF15EventActive = false;
        
        // Lockout tracking (playerId -> lockout info)
        private readonly Dictionary<string, LockoutInfo> _lockouts = new();
        
        // Hackable crate notification cooldowns (playerId -> last notification time)
        private readonly Dictionary<ulong, DateTime> _hackableNotifyCooldown = new();

        // UI tracking
        private readonly List<BasePlayer> _ldUIPlayers = new();
        private readonly Dictionary<ulong, LDUITimers> _ldUITimers = new();
        private readonly Dictionary<string, LDUISettings> _ldUISettings = new();
        private Type _helicopterCrateLockComponentType;

        private class LDUITimers
        {
            public Timer Lockout;
        }

        private class LDUISettings
        {
            public bool Enabled = true;
            public bool Lockouts = true;
        }

        private class PlayerExclusion : Pool.IPooled
        {
            public Plugin plugin;
            public float time;
            public bool IsExpired => Time.time > time;
            public void EnterPool()
            {
                plugin = null;
                time = 0f;
            }
            public void LeavePool()
            {
            }
        }

        // PreventLooting data structures
        private class PreventLootingEntityData
        {
            public List<ulong> Share = new List<ulong>();
            public Dictionary<string, List<ulong>> Quarry = new Dictionary<string, List<ulong>>();
            public PreventLootingEntityData() { }
        }

        private class PreventLootingStoredData
        {
            public Dictionary<ulong, PreventLootingEntityData> Data = new Dictionary<ulong, PreventLootingEntityData>();
            public PreventLootingStoredData() { }
        }

        // PreventLooting variables
        private PreventLootingStoredData _preventLootingData;
        private bool _preventLootingWipeDetected = false;
        private const string PLPerm = "truepve.preventlooting.use";
        private const string PlayerPerm = "truepve.preventlooting.player";
        private const string CorpsePerm = "truepve.preventlooting.corpse";
        private const string BackpackPerm = "truepve.preventlooting.backpack";
        private const string StoragePerm = "truepve.preventlooting.storage";
        private const string AdmPerm = "truepve.preventlooting.admin";

        #endregion

        #region Loading/Unloading

		protected new static void Puts(string format, params object[] args)
        {
            if (!string.IsNullOrEmpty(format))
            {
                Interface.Oxide.LogInfo("[{0}] {1}", "TruePVE", (args.Length != 0) ? string.Format(format, args) : format);
            }
        }
		
        private void Unload()
        {
            bool save = false;
            if (_removeMappingTimer is { Destroyed: false })
            {
                _removeMappingTimer.Destroy();
                SaveData();
                save = true;
            }
            if (_auMappingTimer is { Destroyed: false })
            {
                _auMappingTimer.Destroy();
                if (!save) SaveData();
            }
            StopUpdateLastSeenCo();
            IsUnloading = true;
            scheduleUpdateTimer?.Destroy();
            SaveData();
            // Clear instance for Harmony patches
            Instance = null;
            // Save PreventLooting data
            if (config.PreventLooting.Enabled && _preventLootingData != null)
            {
                Interface.Oxide.DataFileSystem.WriteObject("PreventLooting", _preventLootingData);
            }
            // Destroy all lockout UI
            if (config.LootDefender.Enabled)
            {
                LDUIClass.DestroyAllLockoutUI(this);
            }
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin.Name == "ZoneManager")
                ZoneManager = plugin;
            if (plugin.Name == "LiteZones")
                LiteZones = plugin;
            if (ZoneManager != null || LiteZones != null)
                SetUseZones();
        }

        private void OnPluginUnloaded(Plugin plugin)
        {
            if (plugin.Name == "ZoneManager")
                ZoneManager = null;
            if (plugin.Name == "LiteZones")
                LiteZones = null;
            if (plugin.Name == "CustomHelicopterTiers2")
                _helicopterCrateLockComponentType = null;
            if (ZoneManager == null && LiteZones == null)
                useZones = false;
        }

        private void OnCreatedDynamicPVP() => SetUseZones();

        private void OnDeletedDynamicPVP() => SetUseZones();

        protected void SetUseZones()
        {
            useZones = config != null && config.mappings != null && config.options != null && config.options.useZones && (LiteZones != null || ZoneManager != null);

            if (!useZones)
            {
                return;
            }

            foreach (var mapping in config.mappings)
            {
                if (!mapping.Key.Equals(config.defaultRuleSet))
                {
                    return;
                }
            }

            foreach (var mapping in data.mappings)
            {
                if (!mapping.Key.Equals(config.defaultRuleSet))
                {
                    return;
                }
            }

            useZones = false;
        }

        private void Init()
        {
            if (!_canKillOfflinePlayerEnabled)
            {
                Unsubscribe(nameof(OnPlayerDisconnected));
                Unsubscribe(nameof(OnPlayerSleepEnded));
                Unsubscribe(nameof(OnPlayerSleep));
            }
            if (!config.options.Loot.NoShieldDrop)
            {
                Unsubscribe(nameof(OnPlayerActiveShieldDrop));
            }
            if (!config.options.Loot.NoActiveItemDrop)
            {
                Unsubscribe(nameof(OnPlayerDropActiveItem));
            }
            if (!config.options.Loot.NoRustBackpackDrop)
            {
                Unsubscribe(nameof(OnBackpackDrop));
            }
            if (!config.BlockSprayCanInSafeZones)
            {
                Unsubscribe(nameof(OnSprayCreate));
            }
            Unsubscribe(nameof(CanTakeCutting));
            Unsubscribe(nameof(OnGrowableGather));
            Unsubscribe(nameof(OnConstructionPlace));
            Unsubscribe(nameof(CanLootEntity));
            Unsubscribe(nameof(OnCodeEntered));
            Unsubscribe(nameof(OnCupboardAuthorize));
            Unsubscribe(nameof(CanHelicopterStrafeTarget));
			Unsubscribe(nameof(CanWaterBallSplash));
            Unsubscribe(nameof(OnEntityMarkHostile));
            Unsubscribe(nameof(OnEntitySpawned));
            Unsubscribe(nameof(OnEntityBuilt));
            Unsubscribe(nameof(OnEntityEnter));
            Unsubscribe(nameof(OnTurretTarget));
            Unsubscribe(nameof(OnTimedExplosiveExplode));
            Unsubscribe(nameof(OnWallpaperRemove));
            Unsubscribe(nameof(OnEntityTakeDamage));
            Unsubscribe(nameof(OnPlayerConnected));
            Unsubscribe(nameof(OnSamSiteTarget));
            Unsubscribe(nameof(OnTrapTrigger));
            Unsubscribe(nameof(OnMlrsFire));
            Unsubscribe(nameof(OnNpcConversationRespond));
            Unsubscribe(nameof(OnVendingTransaction));
            Unsubscribe(nameof(OnRentableShopBreakInComplete));
            Unsubscribe(nameof(OnApartmentRoomBreakInComplete));
            Unsubscribe(nameof(CanAffordApartmentMasterKey));
            Unsubscribe(nameof(OnApartmentMasterKeyPurchase));
            Unsubscribe(nameof(CanChangeGrade));
            // register console commands automagically
            foreach (Command command in Enum.GetValues(typeof(Command)))
            {
                AddCovalenceCommand($"tpve.{command}", nameof(CommandDelegator));
            }
            // register chat commands
            AddCovalenceCommand("tpve_prod", nameof(CommandDelegator));
            AddCovalenceCommand("tpve_enable", nameof(CommandDelegator));
            AddCovalenceCommand("tpve", nameof(CommandDelegator));
            permission.RegisterPermission(PermCanMap, this);
            
            // Register PreventLooting permissions and commands
            if (config.PreventLooting.Enabled)
            {
                permission.RegisterPermission(PLPerm, this);
                permission.RegisterPermission(AdmPerm, this);
                permission.RegisterPermission(PlayerPerm, this);
                permission.RegisterPermission(CorpsePerm, this);
                permission.RegisterPermission(BackpackPerm, this);
                permission.RegisterPermission(StoragePerm, this);
                _preventLootingData = Interface.Oxide.DataFileSystem.ReadObject<PreventLootingStoredData>("PreventLooting");
                if (_preventLootingData == null)
                    _preventLootingData = new PreventLootingStoredData();
                // Register PreventLooting chat commands
                AddCovalenceCommand("share", nameof(PreventLootingShare));
                AddCovalenceCommand("plshare", nameof(PreventLootingShare));
                AddCovalenceCommand("unshare", nameof(PreventLootingUnshare));
                AddCovalenceCommand("plunshare", nameof(PreventLootingUnshare));
                AddCovalenceCommand("sharelist", nameof(PreventLootingSharelist));
                AddCovalenceCommand("shareclear", nameof(PreventLootingShareclear));
                AddCovalenceCommand("checkit", nameof(PreventLootingCheckit));
            }
            
            // Register LootDefender permissions
            if (config.LootDefender.Enabled)
            {
                permission.RegisterPermission("truepve.lootdefender.bypassbradleylock", this);
                permission.RegisterPermission("truepve.lootdefender.bypasshelilock", this);
                permission.RegisterPermission("truepve.lootdefender.bypassnpclock", this);
                permission.RegisterPermission("truepve.lootdefender.bypass.loot", this);
                permission.RegisterPermission("truepve.lootdefender.bypass.damage", this);
                permission.RegisterPermission("truepve.lootdefender.bypass.lockouts", this);
                
                // Register hackable crate permissions
                if (config.LootDefender.HackableEnabled && config.LootDefender.HackablePermissionsEnabled)
                {
                    foreach (var perm in config.LootDefender.HackablePermissions)
                    {
                        string permName = perm.Permission;
                        if (!permName.StartsWith("truepve."))
                        {
                            permName = "truepve." + permName;
                        }
                        permission.RegisterPermission(permName, this);
                    }
                }
                
                // Register lockout UI command
                if (config.LootDefender.LockoutUIBradleyEnabled || config.LootDefender.LockoutUIHeliEnabled)
                {
                    AddCovalenceCommand(config.LootDefender.LockoutUICommand, nameof(CommandLockoutUI));
                    Subscribe(nameof(OnPlayerSleepEnded));
                }
                
                // Register lockout times command
                if (config.LootDefender.LockoutBradleyMinutes > 0 || config.LootDefender.LockoutHeliMinutes > 0)
                {
                    AddCovalenceCommand(config.LootDefender.LockoutCommand, nameof(CommandLockouts));
                }
                
                // Subscribe to F15 event if enabled
                if (config.LootDefender.LockoutBypassF15)
                {
                    Subscribe(nameof(OnEventTrigger));
                }
                
                // Subscribe to CH47 gibs if enabled
                if (config.SupplyDrops.DisableCH47Gibs)
                {
                    Subscribe(nameof(OnEntitySpawned));
                }
                
                // Subscribe to NpcRandomRaids if enabled
                if (config.SupplyDrops.LockFromNpcRandomRaids && NpcRandomRaids != null)
                {
                    Subscribe(nameof(OnRandomRaidWin));
                }
                
                // Subscribe to hackable crates if enabled
                if (config.LootDefender.HackableEnabled)
                {
                    Subscribe(nameof(OnPlayerAttack));
                    Subscribe(nameof(OnGuardedCrateEventEnded));
                }
                
                // Initialize HeliLockHarbor to use Bradley setting if not set
                if (!config.LootDefender.HeliLockHarbor.HasValue)
                {
                    config.LootDefender.HeliLockHarbor = config.LootDefender.BradleyLockHarbor;
                }
                
                // Start periodic check for heli distance unlock if enabled
                if (config.LootDefender.HeliUnlockDistance > 0f)
                {
                    timer.Every(5f, CheckHeliDistanceUnlock); // Check every 5 seconds
                }
            }
            // build usage string for console (without sizing)
            usageString = WrapColor("orange", GetMessage("Header_Usage")) + $" - {Version}{Environment.NewLine}" +
                          WrapColor("cyan", $"tpve.{Command.map}") + $" - {GetMessage("Cmd_Usage_mapzone")}{Environment.NewLine}" +
                          WrapColor("cyan", $"tpve.{Command.unmap}") + $" - {GetMessage("Cmd_Usage_unmapzone")}{Environment.NewLine}" +
                          WrapColor("cyan", $"tpve.{Command.def}") + $" - {GetMessage("Cmd_Usage_def")}{Environment.NewLine}" +
                          WrapColor("cyan", $"tpve.{Command.trace}") + $" - {GetMessage("Cmd_Usage_trace")}{Environment.NewLine}" +
                          WrapColor("cyan", $"tpve.{Command.sched} [enable|disable]") + $" - {GetMessage("Cmd_Usage_sched")}{Environment.NewLine}" +
                          WrapColor("cyan", $"/tpve_prod") + $" - {GetMessage("Cmd_Usage_prod")}{Environment.NewLine}" +
                          WrapColor("cyan", $"/tpve map") + $" - {GetMessage("Cmd_Usage_map")}";
            LoadData();
        }

        private bool IsAnimalsIgnoringSleepers() => animalsIgnoreSleepers || config.ruleSets.Exists(ruleSet => ruleSet.HasFlag(RuleFlags.AnimalsIgnoreSleepers));
        
        private bool IsAnimalsIgnoringSleepers(RuleSet ruleSet) => animalsIgnoreSleepers || ruleSet.HasFlag(RuleFlags.AnimalsIgnoreSleepers);
        
        private void OnServerInitialized(bool isStartup)
        {
            isServerStartingUp = false;
            // Set instance for Harmony patches
            Instance = this;
            // load configuration
            config.Init(this);
            ApplyGamePveBrowserTag();
            currentRuleSet = config.GetDefaultRuleSet();
            dudRuleSet = config.GetDudRuleSet();
            if (currentRuleSet == null)
                Puts(GetMessage("Warning_NoRuleSet"), config.defaultRuleSet);
            SetUseZones();
            if (config.schedule.enabled)
            {
                TimerLoop(true);
            }
            if (!IsAnimalsIgnoringSleepers())
            {
                Unsubscribe(nameof(OnNpcTarget));
            }
            if (config.PreventSafeZoneStrafing)
            {
                Subscribe(nameof(CanHelicopterStrafeTarget));
            }
            if (config.PreventThrowingWaterInFreezingBiome || config.BlockRadioactiveWaterDamage)
            {
                Subscribe(nameof(CanWaterBallSplash));
            }
            
            // Show lockout UI to all players if enabled
            if (config.LootDefender.Enabled && (config.LootDefender.LockoutUIBradleyEnabled || config.LootDefender.LockoutUIHeliEnabled))
            {
                timer.Once(2f, () =>
                {
                    foreach (var player in BasePlayer.activePlayerList)
                    {
                        if (player != null && player.IsConnected)
                        {
                            LDUIClass.ShowLockouts(this, player);
                        }
                    }
                });
            }
            if (currentRuleSet == null)
            {
                return;
            }
            if (config.ruleSets.Exists(ruleSet => (ruleSet._flags & (RuleFlags.SafeZoneTurretsIgnorePlayers | RuleFlags.StaticTurretsIgnorePlayers | RuleFlags.StaticTurretsIgnoreScientist | RuleFlags.TrapsIgnorePlayers | RuleFlags.TrapsIgnoreScientist | RuleFlags.TurretsIgnorePlayers | RuleFlags.TurretsIgnoreScientist | RuleFlags.TurretsIgnoreBradley)) != 0))
            {
                Subscribe(nameof(OnEntityEnter));
                Subscribe(nameof(OnTurretTarget));
            }
            if (config.ruleSets.Exists(ruleSet => (ruleSet._flags & (RuleFlags.SamSitesIgnoreMLRS | RuleFlags.PlayerSamSitesIgnorePlayers | RuleFlags.StaticSamSitesIgnorePlayers)) != 0))
            {
                Subscribe(nameof(OnSamSiteTarget));
            }
            if (config.ruleSets.Exists(ruleSet => (ruleSet._flags & (RuleFlags.TrapsIgnorePlayers | RuleFlags.TrapsIgnoreScientist)) != 0))
            {
                Subscribe(nameof(OnTrapTrigger));
            }
            if (config.schedule.enabled && config.schedule.broadcast && !string.IsNullOrEmpty(currentBroadcastMessage))
            {
                Subscribe(nameof(OnPlayerConnected));
            }
            if (config.options.disableBaseOvenSplash)
            {
                ServerMgr.Instance.StartCoroutine(OvenCo());
            }
            if (config.options.disableHostility)
            {
                Subscribe(nameof(OnEntityMarkHostile));
            }
            RuleSet ruleSet = currentRuleSet;
            if (config.options.handleDamage && ruleSet != null && !ruleSet.IsEmpty() && ruleSet.enabled)
            {
                Subscribe(nameof(OnEntityTakeDamage));
                tpveEnabled = true;
            }
            if (config.wallpaper)
            {
                Subscribe(nameof(OnWallpaperRemove));
                Subscribe(nameof(OnTimedExplosiveExplode));
            }
            if (config.options.Loot.Planters)
            {
                Subscribe(nameof(CanTakeCutting));
                Subscribe(nameof(OnGrowableGather));
                Subscribe(nameof(OnConstructionPlace));
            }
            if (config.options.Loot.ProtectTC)
            {
                Subscribe(nameof(OnCupboardAuthorize));
                Subscribe(nameof(CanLootEntity));
            }
            else if (config.options.Loot.Lifts)
            {
                Subscribe(nameof(CanLootEntity));
            }
            else if (config.options.Loot.Sleepers)
            {
                Subscribe(nameof(CanLootEntity));
            }
            else if (config.options.Loot.Corpses)
            {
                Subscribe(nameof(CanLootEntity));
            }
            else if (config.options.Loot.Backpacks)
            {
                Subscribe(nameof(CanLootEntity));
            }
            // Ensure CanLootEntity is active for integrated loot protection
            if (config.LootDefender.Enabled || config.PreventLooting.Enabled)
            {
                Subscribe(nameof(CanLootEntity));
            }
            if (config.options.Loot.Sleepers)
                Subscribe(nameof(CanLootPlayer));
            // Subscribe to CanLootPlayer and PreventLooting hooks when PreventLooting is enabled
            if (config.PreventLooting.Enabled)
            {
                Subscribe(nameof(CanLootPlayer));
                Subscribe(nameof(OnLootPlayer)); // Backup hook to stop looting if it somehow gets past CanLootPlayer
                Subscribe(nameof(OnItemDropped));
                Subscribe(nameof(OnItemPickup));
                Subscribe(nameof(OnLootEntity));
                Subscribe(nameof(CanMannequinChangePose));
                Subscribe(nameof(CanMannequinSwap));
                Subscribe(nameof(OnRackedWeaponMount));
                Subscribe(nameof(OnRackedWeaponSwap));
                Subscribe(nameof(OnRackedWeaponTake));
                Subscribe(nameof(OnRackedWeaponUnload));
                Subscribe(nameof(OnRackedWeaponLoad));
                Subscribe(nameof(OnOvenToggle));
                Subscribe(nameof(CanPickupEntity));
                Subscribe(nameof(CanAdministerVending));
            }
            // Handle PreventLooting wipe detection
            if (config.PreventLooting.Enabled && _preventLootingWipeDetected)
            {
                PrintWarning("Wipe detected! Clearing all PreventLooting share data!");
                if (_preventLootingData != null)
                    _preventLootingData.Data.Clear();
            }
            // Subscribe to OnStartBeingLooted to override onlyOwnerLoot for DroppedItemContainer
            Subscribe(nameof(OnStartBeingLooted));
            if (config.options.Apartments.Enabled)
            {
                if (!config.options.Apartments.Bribe)
                {
                    Subscribe(nameof(OnNpcConversationRespond));
                }
                if (!config.options.Apartments.MasterKey)
                {
                    Subscribe(nameof(OnVendingTransaction));
                    Subscribe(nameof(CanAffordApartmentMasterKey));
                    Subscribe(nameof(OnApartmentMasterKeyPurchase));
                }
                if (!config.options.Apartments.Shop)
                {
                    Subscribe(nameof(OnRentableShopBreakInComplete));
                }
                if (!config.options.Apartments.Room)
                {
                    Subscribe(nameof(OnApartmentRoomBreakInComplete));
                }
            }
            Subscribe(nameof(OnEntitySpawned));
            Subscribe(nameof(CanChangeGrade));
            if (config.PreventLooting.ProtectPlanterboxes || config.options.Loot.Planters)
            {
                Subscribe(nameof(OnEntityBuilt));
            }
            Subscribe(nameof(OnMlrsFire));
            BuildPrefabIds();
            AllowLocksOnContainers();
            if (config.options.AutoRemove) RemoveTemporaryZones();
            InitDeepSea();
            if (_canKillOfflinePlayerEnabled)
            {
                _updateLastSeenCo = UpdateLastSeenCo();
                ServerMgr.Instance.StartCoroutine(_updateLastSeenCo);
            }
        }

        private long GetFrameDeadline(double milliseconds = 1) => Stopwatch.GetTimestamp() + (long)Math.Ceiling(milliseconds * stopwatchTicksPerMillisecond);
        private readonly double stopwatchTicksPerMillisecond = Stopwatch.Frequency / 1000d;

        private IEnumerator OvenCo()
        {
            long deadline = GetFrameDeadline();
            foreach (var ent in BaseNetworkable.serverEntities)
            {
                if (Stopwatch.GetTimestamp() >= deadline)
                {
                    yield return null;
                    deadline = GetFrameDeadline();
                }
                if (ent is BaseOven oven)
                {
                    oven.disabledBySplash = false;
                }
            }
        }

        private void BuildPrefabIds()
        {
            if (StringPool.toNumber.TryGetValue("assets/prefabs/npc/m2bradley/maincannonshell.prefab", out var prefab1)) maincannonshell = prefab1;
            if (StringPool.toNumber.TryGetValue("assets/content/props/train_tunnels/trainbarricade.prefab", out var prefab2)) trainbarricade = prefab2;
            if (StringPool.toNumber.TryGetValue("assets/content/props/train_tunnels/trainbarricadeheavy.prefab", out var prefab3)) trainbarricadeheavy = prefab3;
            if (StringPool.toNumber.TryGetValue("assets/bundled/prefabs/radtown/loot_trash.prefab", out var prefab4)) loot_trash = prefab4;
            if (StringPool.toNumber.TryGetValue("assets/prefabs/misc/xmas/giftbox/giftbox_loot.prefab", out var prefab5)) giftbox_loot = prefab5;
            if (StringPool.toNumber.TryGetValue("assets/prefabs/deployable/campfire/campfire.prefab", out var prefab6)) campfire = prefab6;
            if (StringPool.toNumber.TryGetValue("assets/bundled/prefabs/oilfireballsmall.prefab", out var prefab7)) oilfireballsmall = prefab7;
            if (StringPool.toNumber.TryGetValue("assets/bundled/prefabs/napalm.prefab", out var prefab8)) heli_napalm = prefab8;
            if (StringPool.toNumber.TryGetValue("assets/prefabs/npc/patrol helicopter/rocket_heli_napalm.prefab", out var prefab9)) rocket_heli_napalm = prefab9;
            if (StringPool.toNumber.TryGetValue("assets/prefabs/npc/patrol helicopter/rocket_heli.prefab", out var prefab10)) rocket_heli = prefab10;
        }
        #endregion

        #region Data

        private void OnNewSave()
        {
            data = new();
            SaveData();
            // Handle PreventLooting wipe detection
            if (config.PreventLooting.Enabled)
            {
                _preventLootingWipeDetected = true;
            }
        }

        private class StoredData
        {
            public Dictionary<string, string> mappings = new();
            public Dictionary<ulong, int> LastSeen = new();
            public DateTime LastRunTime = DateTime.MinValue;
            public bool HasMapping(string key)
            {
                return mappings.ContainsKey(key) || mappings.ContainsKey(AllZones);
            }
        }

        private StoredData data = new();

        private void LoadData()
        {
            try { data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name); } catch (Exception ex) { Puts(ex.ToString()); }
            data ??= new();
            data.LastSeen ??= new();
            data.mappings ??= new();
            if (data.LastRunTime != DateTime.MinValue && DateTime.Now.Subtract(data.LastRunTime).TotalHours >= 24)
            {
                if (_canKillOfflinePlayerEnabled && data.LastSeen.Count > 0)
                {
                    Puts("Last seen data wiped due to plugin not being loaded for {0} day(s).", DateTime.Now.Subtract(data.LastRunTime).Days);
                }
                data = new();
                data.LastRunTime = DateTime.Now;
            }
            if (!_canKillOfflinePlayerEnabled && data.LastSeen.Count > 0)
            {
                data.LastSeen.Clear();
                SaveData();
            }
        }

        private IEnumerator _updateLastSeenCo;

        public void StopUpdateLastSeenCo()
        {
            if (_updateLastSeenCo != null)
            {
                ServerMgr.Instance.StopCoroutine(_updateLastSeenCo);
                _updateLastSeenCo = null;
            }
        }

        private void SaveData()
        {
            data.LastRunTime = DateTime.Now;
            Interface.Oxide.DataFileSystem.WriteObject(Name, data);
        }

        public IEnumerator UpdateLastSeenCo()
        {
            var instruction1 = CoroutineEx.waitForSeconds(0.075f);
            var instruction2 = CoroutineEx.waitForSeconds(60f);
            while (!IsUnloading)
            {
                long deadline = GetFrameDeadline();
                bool changed = false;
                foreach (var sleeper in BasePlayer.sleepingPlayerList)
                {
                    if (Stopwatch.GetTimestamp() >= deadline)
                    {
                        yield return null;
                        deadline = GetFrameDeadline();
                    }
                    if (sleeper == null || !sleeper.userID.IsSteamId())
                    {
                        continue;
                    }
                    if (data.LastSeen.ContainsKey(sleeper.userID))
                    {
                        continue;
                    }
                    if (!IsOfflinePlayerProtected(sleeper))
                    {
                        data.LastSeen[sleeper.userID] = Epoch.Current;
                        changed = true;
                    }
                    yield return instruction1;
                    deadline = GetFrameDeadline();
                }
                foreach (var player in BasePlayer.activePlayerList)
                {
                    if (Stopwatch.GetTimestamp() >= deadline)
                    {
                        yield return null;
                        deadline = GetFrameDeadline();
                    }
                    if (data.LastSeen.Remove(player.userID))
                    {
                        changed = true;
                    }
                }
                if (changed)
                {
                    SaveData();
                }
                yield return instruction2;
                deadline = GetFrameDeadline();
            }
        }

        private void OnPlayerSleep(BasePlayer player)
        {
            if (player != null && !IsOfflinePlayerProtected(player))
            {
                data.LastSeen[player.userID] = Epoch.Current;
            }
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            if (player != null && !IsOfflinePlayerProtected(player))
            {
                data.LastSeen[player.userID] = Epoch.Current;
            }
        }

        private void OnServerSave()
        {
            // Save PreventLooting data
            if (config.PreventLooting.Enabled && _preventLootingData != null)
            {
                Interface.Oxide.DataFileSystem.WriteObject("PreventLooting", _preventLootingData);
            }
        }

        public void UpdateLastSeen()
        {
            bool changed = false;
            foreach (var sleeper in BasePlayer.sleepingPlayerList)
            {
                if (sleeper == null || !sleeper.userID.IsSteamId())
                {
                    continue;
                }
                if (!data.LastSeen.ContainsKey(sleeper.userID) && !IsOfflinePlayerProtected(sleeper))
                {
                    data.LastSeen[sleeper.userID] = Epoch.Current;
                    changed = true;
                }
            }
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (data.LastSeen.Remove(player.userID))
                {
                    changed = true;
                }
            }
            if (changed)
            {
                SaveData();
            }
        }

        public bool CanKillOfflinePlayer(BasePlayer victim, out double timeLeft)
        {
            timeLeft = 0;
            if (victim.IsConnected || !victim.IsSleeping())
            {
                data.LastSeen.Remove(victim.userID);
                return false;
            }
            if (!data.LastSeen.TryGetValue(victim.userID, out var lastSeen))
            {
                return false;
            }
            if (IsOfflinePlayerProtected(victim))
            {
                data.LastSeen.Remove(victim.userID);
                return false;
            }
            double timeOffline = Epoch.Current - lastSeen;
            double allowedOfflineTime = config.AllowKillingSleepersHoursOffline * 3600.0;
            timeLeft = Math.Max(0, allowedOfflineTime - timeOffline);
            return timeOffline > allowedOfflineTime;
        }

        private readonly List<ApartmentRoom> _rooms = new();
        private bool TryGetApartmentRoom(BasePlayer player, out ApartmentRoom room)
        {
            room = null;
            if (player == null || player.IsDestroyed) return false;
            if (_rooms.Count == 0)
            {
                foreach (var triggerSafeZone in TriggerSafeZone.allSafeZones)
                {
                    if (triggerSafeZone?.triggerCollider == null) continue;
                    if (triggerSafeZone.Apartment?.rooms == null) continue;
                    _rooms.AddRange(triggerSafeZone.Apartment.rooms);
                }
            }
            foreach (var _room in _rooms)
            {
                if (_room == null || _room.IsDestroyed || _room.owners == null) continue;
                if (!_room.IsCurrentlyRented() || !_room.IsInsideRoom(player)) continue;
                room = _room;
                return true;
            }
            return false;
        }

        private bool IsInsideApartmentRoom(BasePlayer player)
        {
            if (!TryGetApartmentRoom(player, out var room))
            {
                return false;
            }
            if (room.owners.Contains(player.userID))
            {
                return true;
            }
            foreach (var owner in room.owners)
            {
                if (IsAlly(player, owner))
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsOfflinePlayerProtected(BasePlayer victim)
        {
            if (IsInsideApartmentRoom(victim))
            {
                return true;
            }

            BaseEntity priv = victim.GetVehicleBuildingPrivilege(true) ?? victim.GetBuildingPrivilege(true);
            if (priv == null)
            {
                return false;
            }

            return priv switch
            {
                BoatBuildingStation bbs => bbs.CanPlayerBuild(victim),
                VehiclePrivilege vp => vp.IsAuthed(victim),
                BuildingPrivlidge bp => bp.IsAuthed(victim),
                _ => false
            };
        }

        #endregion Data

        #region Command Handling
        // delegation method for commands
        private void CommandDelegator(IPlayer user, string command, string[] args)
        {
            // return if user doesn't have access to run console command
            if (!user.IsAdmin) return;

            if (args.Length > 0 && (args[0] == "map" || args[0] == "unmap"))
            {
                if (user.HasPermission(PermCanMap))
                {
                    CommandMap(user, command, args);
                }
                else user.Reply("You do not have the required truepve.canmap permission.");
                return;
            }

            if (args.Contains("pvp"))
            {
                if (currentRuleSet.rules.Remove("players cannot hurt players"))
                {
                    currentRuleSet.rules.Add("players can hurt players");
                }
                else if (currentRuleSet.rules.Remove("player can hurt players"))
                {
                    currentRuleSet.rules.Add("player cannot hurt players");
                }

                Puts("PVP toggled {0}", currentRuleSet.rules.Contains("players can hurt players") ? "on" : "off");
                SaveConfig();
                return;
            }

            if (command == "tpve_prod")
            {
                HandleProd(user);
                return;
            }

            if (command == "tpve_enable")
            {
				tpveEnabled = !tpveEnabled;
                ValidateCurrentDamageHook();
                Message(user, "Enable", tpveEnabled);
                return;
            }

            // /tpve with no args -> usage. /tpve <sub> uses the subcommand.
            // Console tpve.<sub> strips the "tpve." prefix.
            if (string.Equals(command, "tpve", StringComparison.OrdinalIgnoreCase))
            {
                if (args == null || args.Length == 0)
                {
                    ShowUsage(user);
                    return;
                }
                command = args[0];
            }
            else
            {
                command = command.Replace("tpve.", string.Empty);
            }

            if (!Enum.TryParse(command, out Command @enum))
            {
                user.Reply($"Invalid argument: {command}");
                return;
            }

            switch (@enum)
            {
                case Command.sleepers:
                    HandleSleepers(user);
                    return;
                case Command.def:
                    HandleDef(user);
                    return;
                case Command.sched:
                    HandleScheduleSet(user, args);
                    return;
                case Command.trace:
                    HandleTrace(user);
                    return;
                case Command.enable:
					tpveEnabled = !tpveEnabled;
                    ValidateCurrentDamageHook();
                    Message(user, "Enable", tpveEnabled);
                    return;
                case Command.usage:
                default:
                    ShowUsage(user);
                    return;
            }
        }

        protected void HandleTrace(IPlayer user)
        {
            if (!IsTraceEnabled(user))
            {
                return;
            }
            if (user.IsServer)
            {
                traceDistance = 0f;
            }
            else traceDistance = config.options.MaxTraceDistance;
            trace = !trace;
            if (!trace)
            {
                tracePlayer = null;
                traceEntity = null;
            }
            else tracePlayer = user.Object as BasePlayer;
            Message(user, "Notify_TraceToggle", new object[] { trace ? "on" : "off" });
            traceTimer?.Destroy();
            if (trace)
            {
                traceTimer = timer.In(traceTimeout, () => trace = false);
            }
        }

        private bool IsTraceEnabled(IPlayer user)
        {
            if (config.options.PlayerConsole || config.options.ServerConsole)
            {
                return true;
            }
            Message(user, "`Trace To Player Console` or `Trace To Server Console` must be enabled in the config!");
            return false;
        }

        private void HandleSleepers(IPlayer user)
        {
            if (animalsIgnoreSleepers)
            {
                animalsIgnoreSleepers = false;
                if (!IsAnimalsIgnoringSleepers())
                {
                    Unsubscribe(nameof(OnNpcTarget));
                }
                user.Reply("Sleepers are no longer protected from animals.");
            }
            else
            {
                animalsIgnoreSleepers = true;
                Subscribe(nameof(OnNpcTarget));
                user.Reply("Sleepers are now protected from animals.");
            }
        }

        // handle setting defaults
        private void HandleDef(IPlayer user)
        {
            config.options = new();
            Message(user, "Notify_DefConfigLoad");
            LoadDefaultData();
            data.mappings ??= new();
            data.mappings.Clear();
            Message(user, "Notify_DefDataLoad");
            CheckData();
            SaveConfig();
            SaveData();
        }

        // handle prod command (raycast to determine what player is looking at)
        private void HandleProd(IPlayer user)
        {
            var player = user.Object as BasePlayer;
            if (player == null || !player.IsAdmin)
            {
                Message(user, "Error_NoPermission");
                return;
            }

            if (!GetRaycastTarget(player, out var entity))
            {
                SendReply(player, WrapSize(12, WrapColor("red", GetMessage("Error_NoEntityFound", player.UserIDString))));
                return;
            }

            Message(player, "Notify_ProdResult", entity.GetType(), entity.ShortPrefabName);
        }

        private void CommandMap(IPlayer user, string command, string[] args)
        {
            if (args.Length > 0) command = args[0];
            args = args.Length > 1 ? args[1..] : Array.Empty<string>();

            if (command != "map" && command != "unmap")
            {
                Message(user, "Error_InvalidCommand");
                return;
            }

            string from;
            string to;

            if (args.Length == 0)
            {
                BasePlayer player = user.Object as BasePlayer;
                if (player == null)
                {
                    Message(user, "Error_InvalidParamForCmd", command);
                    return;
                }

                using var locs = GetLocationKeys(player);
                if (locs.Count == 0)
                {
                    Message(user, "Error_InvalidParamForCmd", command);
                    return;
                }

                from = locs[0];
                to = command == "map" ? "exclude" : null;
            }
            else
            {
                from = args[0];
                to = args.Length == 2 ? args[1] : null;
            }

            if (to != null)
            {
                if (to != "exclude" && !config.ruleSets.Exists(r => r.name == to))
                {
                    Message(user, "Error_InvalidMapping", from, to);
                    return;
                }

                bool changes = false;
                if (config.mappings.TryGetValue(from, out string old))
                {
                    changes = true;
                    Message(user, "Notify_MappingUpdated", from, old, to);
                    config.mappings[from] = to;
                    SaveConfig();
                    TryBuildExclusionMappings();
                }

                if (data.mappings.TryGetValue(from, out old))
                {
                    changes = true;
                    Message(user, "Notify_MappingUpdated", from, old, to);
                    data.mappings[from] = to;
                    SaveData();
                    TryBuildExclusionMappings();
                }

                if (!changes)
                {
                    Message(user, "Notify_MappingCreated", from, to);
                    config.mappings[from] = to;
                    SaveConfig();
                    TryBuildExclusionMappings();
                }
            }
            else
            {
                if (config.HasMapping(from))
                {
                    Message(user, "Notify_MappingDeleted", from, config.mappings.TryGetValue(from, out var old) ? old : AllZones);
                    if (config.mappings.Remove(from)) SaveConfig();
                    TryBuildExclusionMappings();
                }
                else if (data.HasMapping(from))
                {
                    Message(user, "Notify_MappingDeleted", from, data.mappings.TryGetValue(from, out var old) ? old : AllZones);
                    if (data.mappings.Remove(from)) SaveData();
                    TryBuildExclusionMappings();
                }
                else
                {
                    Message(user, "Error_NoMappingToDelete", from);
                }
            }
        }

        // handles schedule enable/disable
        private void HandleScheduleSet(IPlayer user, string[] args)
        {
            if (args.Length == 0)
            {
                Message(user, "Error_InvalidParamForCmd");
                return;
            }
            if (!config.schedule.valid)
            {
                Message(user, "Notify_InvalidSchedule");
            }
            else if (args[0] == "enable")
            {
                if (config.schedule.enabled) return;
                config.schedule.enabled = true;
                TimerLoop();
                Message(user, "Notify_SchedSetEnabled");
            }
            else if (args[0] == "disable")
            {
                if (!config.schedule.enabled) return;
                config.schedule.enabled = false;
                if (scheduleUpdateTimer != null)
                    scheduleUpdateTimer.Destroy();
                Message(user, "Notify_SchedSetDisabled");
            }
            else
            {
                Message(user, "Error_InvalidParameter", args[0]);
            }
        }

        // PreventLooting Commands
        private void PreventLootingShare(IPlayer user, string command, string[] args)
        {
            if (!config.PreventLooting.Enabled) return;
            var player = user.Object as BasePlayer;
            if (player == null) return;
            if (config.PreventLooting.UsePermissions && !permission.UserHasPermission(player.UserIDString, PLPerm))
            {
                SendReply(player, GetMessage("NoPermission", player.UserIDString));
                return;
            }
            IPlayer player0 = null;
            ulong ID;
            if (args == null || args.Length <= 0) ID = 0;
            else
            {
                player0 = PreventLootingCheckPlayer(player, args);
                if (player0 == null) return;
                ID = Convert.ToUInt64(player0.Id);
            }
            object success;
            if (PreventLootingFindEntityFromRay(player, out success))
            {
                if (success is StorageContainer || success is ContainerIOEntity || success is IndustrialCrafter)
                {
                    BaseEntity entity = success as BaseEntity;
                    BaseEntity childentity = entity;
                    entity = PreventLootingCheckParent(entity);
                    if (entity.OwnerID == ID)
                    {
                        SendReply(player, GetMessage("OwnEntity", player.UserIDString));
                        return;
                    }
                    if (entity.OwnerID != player.userID && (!player.IsAdmin || (player.IsAdmin && !config.PreventLooting.AdminCanLoot)))
                    {
                        SendReply(player, GetMessage("NoAccess", player.UserIDString));
                        return;
                    }
                    if (_preventLootingData == null) _preventLootingData = new PreventLootingStoredData();
                    if (!_preventLootingData.Data.ContainsKey(entity.net.ID.Value))
                    {
                        var data = new PreventLootingEntityData();
                        _preventLootingData.Data.Add(entity.net.ID.Value, data);
                        if (childentity != entity)
                        {
                            data.Quarry = new Dictionary<string, List<ulong>>();
                            data.Quarry.Add(childentity.ShortPrefabName, new List<ulong> { ID });
                        }
                        else
                        {
                            data.Share = new List<ulong>();
                            data.Share.Add(ID);
                        }
                        if (ID == 0) SendReply(player, GetMessage("ShareAll", player.UserIDString));
                        else SendReply(player, string.Format(GetMessage("SharePlayer", player.UserIDString), "<color=#FFA500>" + player0.Name + "</color>"));
                    }
                    else
                    {
                        if (childentity == entity)
                        {
                            if (_preventLootingData.Data[entity.net.ID.Value].Share.Contains(ID))
                            {
                                if (ID == 0) SendReply(player, GetMessage("HasShareAll", player.UserIDString));
                                else SendReply(player, string.Format(GetMessage("HasSharePlayer", player.UserIDString), "<color=#FFA500>" + player0.Name + "</color>"));
                            }
                            else
                            {
                                _preventLootingData.Data[entity.net.ID.Value].Share.Add(ID);
                                if (ID == 0) SendReply(player, GetMessage("ShareAll", player.UserIDString));
                                else SendReply(player, string.Format(GetMessage("SharePlayer", player.UserIDString), "<color=#FFA500>" + player0.Name + "</color>"));
                            }
                        }
                        else
                        {
                            if (!_preventLootingData.Data[entity.net.ID.Value].Quarry.ContainsKey(childentity.ShortPrefabName)) _preventLootingData.Data[entity.net.ID.Value].Quarry.Add(childentity.ShortPrefabName, new List<ulong> { });
                            if (_preventLootingData.Data[entity.net.ID.Value].Quarry[childentity.ShortPrefabName].Contains(ID))
                            {
                                if (ID == 0) SendReply(player, GetMessage("HasShareAll", player.UserIDString));
                                else SendReply(player, string.Format(GetMessage("HasSharePlayer", player.UserIDString), "<color=#FFA500>" + player0.Name + "</color>"));
                            }
                            else
                            {
                                if (_preventLootingData.Data[entity.net.ID.Value].Quarry.ContainsKey(childentity.ShortPrefabName)) _preventLootingData.Data[entity.net.ID.Value].Quarry[childentity.ShortPrefabName].Add(ID);
                                else _preventLootingData.Data[entity.net.ID.Value].Quarry.Add(childentity.ShortPrefabName, new List<ulong> { ID });
                                if (ID == 0) SendReply(player, GetMessage("ShareAll", player.UserIDString));
                                else SendReply(player, string.Format(GetMessage("SharePlayer", player.UserIDString), "<color=#FFA500>" + player0.Name + "</color>"));
                            }
                        }
                    }
                }
                else
                {
                    SendReply(player, GetMessage("EntityNotFound", player.UserIDString));
                }
            }
            else
            {
                SendReply(player, GetMessage("EntityNotFound", player.UserIDString));
            }
        }

        private void PreventLootingUnshare(IPlayer user, string command, string[] args)
        {
            if (!config.PreventLooting.Enabled) return;
            var player = user.Object as BasePlayer;
            if (player == null) return;
            if (config.PreventLooting.UsePermissions && !permission.UserHasPermission(player.UserIDString, PLPerm))
            {
                SendReply(player, GetMessage("NoPermission", player.UserIDString));
                return;
            }
            IPlayer player0 = null;
            ulong ID;
            if (args == null || args.Length <= 0) ID = 0;
            else
            {
                player0 = PreventLootingCheckPlayer(player, args);
                if (player0 == null) return;
                ID = Convert.ToUInt64(player0.Id);
            }
            object success;
            if (PreventLootingFindEntityFromRay(player, out success))
            {
                if (success is StorageContainer || success is ContainerIOEntity || success is IndustrialCrafter)
                {
                    BaseEntity entity = success as BaseEntity;
                    BaseEntity childentity = entity;
                    entity = PreventLootingCheckParent(entity);
                    if (entity.OwnerID != player.userID && (!player.IsAdmin || (player.IsAdmin && !config.PreventLooting.AdminCanLoot)))
                    {
                        SendReply(player, GetMessage("NoAccess", player.UserIDString));
                        return;
                    }
                    if (_preventLootingData == null || !_preventLootingData.Data.ContainsKey(entity.net.ID.Value))
                    {
                        SendReply(player, GetMessage("NoShare", player.UserIDString));
                    }
                    else
                    {
                        if (childentity == entity)
                        {
                            if (!_preventLootingData.Data[entity.net.ID.Value].Share.Contains(ID))
                            {
                                if (ID == 0) SendReply(player, GetMessage("HasUnShareAll", player.UserIDString));
                                else SendReply(player, string.Format(GetMessage("HasUnSharePlayer", player.UserIDString), "<color=#FFA500>" + player0.Name + "</color>"));
                            }
                            else
                            {
                                _preventLootingData.Data[entity.net.ID.Value].Share.Remove(ID);
                                if (_preventLootingData.Data[entity.net.ID.Value].Share.Count == 0) _preventLootingData.Data.Remove(entity.net.ID.Value);
                                if (ID == 0) SendReply(player, GetMessage("WasUnShareAll", player.UserIDString));
                                else SendReply(player, string.Format(GetMessage("WasUnSharePlayer", player.UserIDString), "<color=#FFA500>" + player0.Name + "</color>"));
                            }
                        }
                        else
                        {
                            if (!_preventLootingData.Data[entity.net.ID.Value].Quarry.ContainsKey(childentity.ShortPrefabName))
                            {
                                SendReply(player, GetMessage("NoShare", player.UserIDString));
                                return;
                            }
                            if (!_preventLootingData.Data[entity.net.ID.Value].Quarry[childentity.ShortPrefabName].Contains(ID))
                            {
                                if (ID == 0) SendReply(player, GetMessage("HasUnShareAll", player.UserIDString));
                                else SendReply(player, string.Format(GetMessage("HasUnSharePlayer", player.UserIDString), "<color=#FFA500>" + player0.Name + "</color>"));
                            }
                            else
                            {
                                _preventLootingData.Data[entity.net.ID.Value].Quarry[childentity.ShortPrefabName].Remove(ID);
                                if (_preventLootingData.Data[entity.net.ID.Value].Quarry[childentity.ShortPrefabName].Count == 0) _preventLootingData.Data[entity.net.ID.Value].Quarry.Remove(childentity.ShortPrefabName);
                                if (ID == 0) SendReply(player, GetMessage("WasUnShareAll", player.UserIDString));
                                else SendReply(player, string.Format(GetMessage("WasUnSharePlayer", player.UserIDString), "<color=#FFA500>" + player0.Name + "</color>"));
                            }
                        }
                        PreventLootingSharelist(user, "sharelist", null);
                    }
                }
                else
                {
                    SendReply(player, GetMessage("EntityNotFound", player.UserIDString));
                }
            }
            else
            {
                SendReply(player, GetMessage("EntityNotFound", player.UserIDString));
            }
        }

        private void PreventLootingSharelist(IPlayer user, string command, string[] args)
        {
            if (!config.PreventLooting.Enabled) return;
            var player = user.Object as BasePlayer;
            if (player == null) return;
            if (config.PreventLooting.UsePermissions && !permission.UserHasPermission(player.UserIDString, PLPerm))
            {
                SendReply(player, GetMessage("NoPermission", player.UserIDString));
                return;
            }
            object success;
            if (PreventLootingFindEntityFromRay(player, out success))
            {
                if (success is StorageContainer || success is ContainerIOEntity || success is IndustrialCrafter)
                {
                    BaseEntity entity = success as BaseEntity;
                    BaseEntity childentity = entity;
                    entity = PreventLootingCheckParent(entity);
                    if (entity.OwnerID != player.userID && (!player.IsAdmin || (player.IsAdmin && !config.PreventLooting.AdminCanLoot)))
                    {
                        SendReply(player, GetMessage("NoAccess", player.UserIDString));
                        return;
                    }
                    if (_preventLootingData == null || !_preventLootingData.Data.ContainsKey(entity.net.ID.Value))
                    {
                        SendReply(player, GetMessage("NoShare", player.UserIDString));
                    }
                    else
                    {
                        if (childentity == entity)
                        {
                            if (_preventLootingData.Data[entity.net.ID.Value].Share.Contains(0))
                            {
                                SendReply(player, GetMessage("HasShareAllList", player.UserIDString));
                                return;
                            }
                            var message = "<color=#FFFF00>" + GetMessage("ListShare", player.UserIDString) + "</color>\n";
                            int i = 0;
                            foreach (var share in _preventLootingData.Data[entity.net.ID.Value].Share)
                            {
                                i++;
                                var pl = covalence.Players.FindPlayer(share.ToString());
                                if (pl != null)
                                    message += string.Format("{0}. <color=#00FF00>{1}</color> ({2})\n\r", i, pl.Name, pl.Id);
                            }
                            SendReply(player, message);
                        }
                        else
                        {
                            if (!_preventLootingData.Data[entity.net.ID.Value].Quarry.ContainsKey(childentity.ShortPrefabName))
                            {
                                SendReply(player, GetMessage("NoShare", player.UserIDString));
                                return;
                            }
                            if (_preventLootingData.Data[entity.net.ID.Value].Quarry[childentity.ShortPrefabName].Contains(0))
                            {
                                SendReply(player, GetMessage("HasShareAllList", player.UserIDString));
                                return;
                            }
                            var message = "<color=#FFFF00>" + GetMessage("ListShare", player.UserIDString) + "</color>\n";
                            int i = 0;
                            foreach (var share in _preventLootingData.Data[entity.net.ID.Value].Quarry[childentity.ShortPrefabName])
                            {
                                i++;
                                var pl = covalence.Players.FindPlayer(share.ToString());
                                if (pl != null)
                                    message += string.Format("{0}. <color=#00FF00>{1}</color> ({2})\n\r", i, pl.Name, pl.Id);
                            }
                            SendReply(player, message);
                        }
                    }
                }
                else
                {
                    SendReply(player, GetMessage("EntityNotFound", player.UserIDString));
                }
            }
            else
            {
                SendReply(player, GetMessage("EntityNotFound", player.UserIDString));
            }
        }

        private void PreventLootingShareclear(IPlayer user, string command, string[] args)
        {
            if (!config.PreventLooting.Enabled) return;
            var player = user.Object as BasePlayer;
            if (player == null) return;
            if (config.PreventLooting.UsePermissions && !permission.UserHasPermission(player.UserIDString, PLPerm))
            {
                SendReply(player, GetMessage("NoPermission", player.UserIDString));
                return;
            }
            object success;
            if (PreventLootingFindEntityFromRay(player, out success))
            {
                if (success is StorageContainer || success is ContainerIOEntity || success is IndustrialCrafter)
                {
                    BaseEntity entity = success as BaseEntity;
                    BaseEntity childentity = entity;
                    entity = PreventLootingCheckParent(entity);
                    if (entity.OwnerID != player.userID && (!player.IsAdmin || (player.IsAdmin && !config.PreventLooting.AdminCanLoot)))
                    {
                        SendReply(player, GetMessage("NoAccess", player.UserIDString));
                        return;
                    }
                    if (_preventLootingData == null || !_preventLootingData.Data.ContainsKey(entity.net.ID.Value))
                    {
                        SendReply(player, GetMessage("NoShare", player.UserIDString));
                    }
                    else
                    {
                        if (childentity == entity)
                        {
                            _preventLootingData.Data[entity.net.ID.Value].Share.Clear();
                            if (_preventLootingData.Data[entity.net.ID.Value].Share.Count == 0) _preventLootingData.Data.Remove(entity.net.ID.Value);
                            SendReply(player, GetMessage("ShareClear", player.UserIDString));
                        }
                        else
                        {
                            if (!_preventLootingData.Data[entity.net.ID.Value].Quarry.ContainsKey(childentity.ShortPrefabName))
                            {
                                SendReply(player, GetMessage("NoShare", player.UserIDString));
                                return;
                            }
                            _preventLootingData.Data[entity.net.ID.Value].Quarry[childentity.ShortPrefabName].Clear();
                            if (_preventLootingData.Data[entity.net.ID.Value].Quarry[childentity.ShortPrefabName].Count == 0) _preventLootingData.Data[entity.net.ID.Value].Quarry.Remove(childentity.ShortPrefabName);
                            SendReply(player, GetMessage("ShareClear", player.UserIDString));
                        }
                    }
                }
                else
                {
                    SendReply(player, GetMessage("EntityNotFound", player.UserIDString));
                }
            }
            else
            {
                SendReply(player, GetMessage("EntityNotFound", player.UserIDString));
            }
        }

        private void PreventLootingCheckit(IPlayer user, string command, string[] args)
        {
            if (!config.PreventLooting.Enabled) return;
            var player = user.Object as BasePlayer;
            if (player == null) return;
            if (config.PreventLooting.UsePermissions && !permission.UserHasPermission(player.UserIDString, PLPerm))
            {
                SendReply(player, GetMessage("NoPermission", player.UserIDString));
                return;
            }
            object success;
            if (PreventLootingFindEntityFromRay(player, out success))
            {
                if (success is StorageContainer || success is ContainerIOEntity || success is IndustrialCrafter)
                {
                    BaseEntity entity = success as BaseEntity;
                    entity = PreventLootingCheckParent(entity);
                    if (entity.OwnerID != player.userID && (!player.IsAdmin || (player.IsAdmin && !config.PreventLooting.AdminCanLoot)))
                    {
                        SendReply(player, GetMessage("NoAccess", player.UserIDString));
                        return;
                    }
                    if (config.PreventLooting.OnlyInCupboardRange)
                    {
                        BuildingPrivlidge bprev = player.GetBuildingPrivilege(new OBB(entity.transform.position, entity.transform.rotation, entity.bounds));
                        if (bprev == null) SendReply(player, "<color=#FF0000>" + GetMessage("EntNoPrevent", player.UserIDString) + "</color>\n");
                        else SendReply(player, "<color=#CCFF00>" + GetMessage("EntPrevent", player.UserIDString) + "</color>\n");
                    }
                    else SendReply(player, "<color=#CCFF00>" + GetMessage("EntPrevent", player.UserIDString) + "</color>\n");
                }
            }
            else
            {
                SendReply(player, GetMessage("EntityNotFound", player.UserIDString));
            }
        }
        #endregion

        #region Configuration/Data
        private bool _playersTriggerOption, _playersHurtOption, _canKillOfflinePlayerEnabled, _pvpReflectionEnabled, _allowKillingSleepersEnabled, _buildingBlockHandlerEnabled;

        // load config
        protected override void LoadConfig()
        {
            base.LoadConfig();
            canSaveConfig = false;
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null) LoadDefaultConfig();
                canSaveConfig = true;
                CheckData();
                SaveConfig();
            }
            catch (Exception ex)
            {
                Puts(ex.ToString());
                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig()
        {
            config = new()
            {
                configVersion = Version.ToString(),
                options = new()
            };
            LoadDefaultData();
            Puts("Loaded default config.");
        }

        private bool canSaveConfig = true;

        // save data
        protected override void SaveConfig()
        {
            if (canSaveConfig)
            {
                Config.WriteObject(config);
            }
        }

        // check rulesets and groups
        private void CheckData()
        {
            if (string.IsNullOrEmpty(config.defaultRuleSet))
            {
                config.defaultRuleSet = "default";
                Puts("Loaded default ruleset (no default ruleset was configured)");
            }
            if (config.mappings.IsNullOrEmpty())
            {
                BuildDefaultMappings();
                Puts("Loaded default mappings (no mappings were configured)");
            }
            if (config.schedule == null)
            {
                Puts("Loaded default schedule (schedule was null)");
                BuildDefaultSchedule();
            }
            if (config.groups.IsNullOrEmpty())
            {
                BuildDefaultGroups();
                Puts("Loaded default entity groups (no entity groups were configured)");
            }
            if (config.ruleSets.IsNullOrEmpty())
            {
                BuildDefaultRuleset();
                Puts("Loaded default rulesets (no rulesets were configured)");
            }
            if (config._AllowKillingSleepersAuthorization != null)
            {
                config.AllowKillingSleepersAuthorization.Enabled = config._AllowKillingSleepersAuthorization.Value;
                config._AllowKillingSleepersAuthorization = null;
            }
            if (config.options.BlockHandler._Online != null)
            {
                config.options.BlockHandler.BlockWhenOnline = config.options.BlockHandler._Online.Value;
                config.options.BlockHandler._Online = null;
            }
            if (config.options.BlockHandler._Twig != null)
            {
                config.options.BlockHandler.Twig = config.options.BlockHandler._Twig.Value;
                config.options.BlockHandler._Twig = null;
            }
            TryUpdateConfig();
            config.configVersion = Version.ToString();
            CheckMappings();
            BuildRuleSetDictionary();
            BuildExclusionMappings();
            _allowKillingSleepersEnabled = config.AllowKillingSleepersAlly || config.AllowKillingSleepers || config.AllowKillingSleepersAuthorization.Enabled || config.AllowKillingSleepersIds.Exists(x => x.IsSteamId());
            _buildingBlockHandlerEnabled = config.options.BlockHandler.Any;
            _pvpReflectionEnabled = config.options.Reflect.Any;
            _canKillOfflinePlayerEnabled = config.AllowKillingSleepersHoursOffline > 0;
            _playersTriggerOption = config.PlayersTriggerTraps || config.PlayersTriggerTurrets;
            _playersHurtOption = config.PlayersHurtTraps || config.PlayersHurtTurrets;
            // ensure loot defender/prevent looting defaults exist
            config.LootDefender ??= new();
            config.PreventLooting ??= new();
            config.SupplyDrops ??= new();
            config.Notify ??= new();
        }

        private void TryUpdateConfig()
        {
            if (!TryParseVersionNumber(config.configVersion, out var vn) || vn >= Version)
                return;

            Dictionary<string, string> updates = new(StringComparer.OrdinalIgnoreCase)
            {
                ["npcs"] = "SnakeHazard",
                ["dispensers"] = "VineSwingingTree"
            };

            for (int i = 0; i < config.groups.Count; i++)
            {
                var group = config.groups[i];
                if (string.IsNullOrWhiteSpace(group.members))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(group.name))
                {
                    group.name = $"group{i}";
                    continue;
                }

                if (group.name == "ridablehorses" && group.members.Equals("RidableHorse2"))
                {
                    group.members = "RidableHorse";
                    continue;
                }

                if (updates.TryGetValue(group.name, out var update) && !ContainedInGroups(update))
                {
                    group.members = $"{group.members.TrimEnd(',', ' ')}{", "}{update}";
                    continue;
                }
            }
        }

        private bool ContainedInGroups(string member) => config.groups.Exists(g => g.members.Contains(member, CompareOptions.OrdinalIgnoreCase) || g.exclusions.Contains(member, CompareOptions.OrdinalIgnoreCase));

        private bool TryParseVersionNumber(string input, out VersionNumber version)
        {
            version = default;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            var parts = input.Split('.');
            if (parts.Length != 3)
                return false;

            if (int.TryParse(parts[0], out int major) && int.TryParse(parts[1], out int minor) && int.TryParse(parts[2], out int patch))
            {
                version = new(major, minor, patch);
                return true;
            }

            return false;
        }

        // rebuild mappings
        private bool CheckMappings()
        {
            bool dirty = false;
            // Build HashSet of rule set names manually (without LINQ)
            HashSet<string> ruleSetNames = new HashSet<string>();
            for (int i = 0; i < config.ruleSets.Count; i++)
            {
                var ruleSet = config.ruleSets[i];
                if (ruleSet != null && !string.IsNullOrEmpty(ruleSet.name))
                {
                    ruleSetNames.Add(ruleSet.name);
                }
            }
            
            foreach (RuleSet ruleSet in config.ruleSets)
            {
                if (!config.mappings.ContainsValue(ruleSet.name))
                {
                    config.mappings[ruleSet.name] = ruleSet.name;
                    dirty = true;
                }
            }
            if (config.options.AutoRemove)
            {
                var keysToRemove = new List<string>();
                foreach (var kvp in data.mappings)
                {
                    if (!ruleSetNames.Contains(kvp.Value))
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }
                for (int i = 0; i < keysToRemove.Count; i++)
                {
                    data.mappings.Remove(keysToRemove[i]);
                    dirty = true;
                }
                if (dirty) SaveData();
            }
            return dirty;
        }

        protected void BuildRuleSetDictionary()
        {
            ruleSetByNameDictionary.Clear();

            foreach (RuleSet ruleSet in config.ruleSets)
            {
                if (ruleSet.enabled)
                {
                    ruleSetByNameDictionary[ruleSet.name] = ruleSet;
                }
            }
        }

        protected void TryBuildExclusionMappings()
        {
            if (!config.mappings.TryGetValue(AllZones, out var val) || !val.Equals("exclude", StringComparison.OrdinalIgnoreCase))
            {
                BuildExclusionMappings();
            }
            else if (!data.mappings.TryGetValue(AllZones, out val) || !val.Equals("exclude", StringComparison.OrdinalIgnoreCase))
            {
                BuildExclusionMappings();
            }
        }

        protected void BuildExclusionMappings()
        {
            excludeAllZones = false;
            exclusionLocationsSet.Clear();

            using var mappings = Pool.Get<PooledList<KeyValuePair<string, string>>>();
            mappings.AddRange(config.mappings);
            mappings.AddRange(data.mappings);

            for (int i = mappings.Count - 1; i >= 0; i--)
            {
                var (key, value) = mappings[i];

                if (key == AllZones && string.Equals(value, "exclude", StringComparison.OrdinalIgnoreCase))
                {
                    excludeAllZones = true;
                    return;
                }
            }

            foreach (var (key, value) in mappings)
            {
                if (!value.Equals("exclude", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var ruleSet in config.ruleSets)
                    {
                        if (ruleSet.name.Equals(value, StringComparison.OrdinalIgnoreCase) && ruleSet.IsEmpty())
                        {
                            exclusionLocationsSet.Add(key);
                        }
                    }
                }
                else
                {
                    exclusionLocationsSet.Add(key);
                }
            }

            foreach (var group in config.groups)
            {
                foreach (var exclusion in group._exclusionSet)
                {
                    if (!string.IsNullOrWhiteSpace(exclusion))
                    {
                        exclusionLocationsSet.Add(exclusion.Trim());
                    }
                }
            }
        }

        // load default data to mappings, rulesets, and groups
        protected bool LoadDefaultData()
        {
            BuildDefaultMappings();

            BuildDefaultSchedule();

            BuildDefaultGroups();

            BuildDefaultRuleset();

            return true;
        }

        protected void BuildDefaultSchedule()
        {
            config.schedule = new();
        }

        protected void BuildDefaultMappings()
        {
            config.mappings ??= new();
            config.mappings.Clear();
            config.defaultRuleSet = "default";
            config.mappings[config.defaultRuleSet] = config.defaultRuleSet; // create mapping for ruleset
        }

        protected void BuildDefaultGroups()
        {
            config.groups ??= new();
            config.groups.Clear();

            config.groups.Add(new("barricades")
            {
                members = "door_barricade_a, door_barricade_a_large, door_barricade_b, door_barricade_dbl_a, door_barricade_dbl_a_large, door_barricade_dbl_b, door_barricade_dbl_b_large, gingerbread_barricades_house, gingerbread_barricades_snowman, gingerbread_barricades_tree, wooden_crate_gingerbread",
                exclusions = "barricade.concrete, barricade.sandbags, barricade.stone"
            });

            config.groups.Add(new("barricades2")
            {
                members = "spikes_static, barricade.metal, barricade.wood, barricade.woodwire, spikes.floor, icewall, GraveyardFence",
            });

            config.groups.Add(new("dispensers")
            {
                members = "BaseCorpse, HelicopterDebris, PlayerCorpse, NPCPlayerCorpse, HorseCorpse, SkyLantern, Pinata"
            });

            config.groups.Add(new("fire")
            {
                members = "FireBall, FlameExplosive, FlameThrower, BaseOven, FlameTurret, napalm, oilfireball2"
            });

            config.groups.Add(new("guards")
            {
                members = "bandit_guard, scientistpeacekeeper, sentry.scientist.static, sentry.bandit.static"
            });

            config.groups.Add(new("heli")
            {
                members = "PatrolHelicopter, oilfireballsmall, heli_napalm, rocket_heli, rocket_heli_napalm"
            });

            config.groups.Add(new("highwalls")
            {
                members = "SimpleBuildingBlock, wall.external.high.ice, gates.external.high.stone, gates.external.high.wood"
            });

            config.groups.Add(new("ridablehorses")
            {
                members = "RidableHorse"
            });

            config.groups.Add(new("cars")
            {
                members = "BasicCar, ModularCar, BaseModularVehicle, BaseVehicleModule, VehicleModuleEngine, VehicleModuleSeating, VehicleModuleStorage, VehicleModuleTaxi, ModularCarSeat, Bike"
            });

            config.groups.Add(new("mini")
            {
                members = "minicopter.entity"
            });

            config.groups.Add(new("scrapheli")
            {
                members = "ScrapTransportHelicopter"
            });

            config.groups.Add(new("ch47")
            {
                members = "ch47.entity"
            });

            config.groups.Add(new("npcs")
            {
                members = "ch47scientists.entity, BradleyAPC, CustomScientistNpc, SnakeHazard, ScarecrowNPC, HumanNPC, NPCPlayer, ScientistNPC, TunnelDweller, SimpleShark, UnderwaterDweller, ZombieNPC"
            });

            config.groups.Add(new("players")
            {
                members = "BasePlayer, FrankensteinPet"
            });

            config.groups.Add(new("resources")
            {
                members = "ResourceEntity, TreeEntity, OreResourceEntity, LootContainer, NaturalBeehive, VineSwingingTree",
                exclusions = "hobobarrel.deployed"
            });

            config.groups.Add(new("snowmobiles")
            {
                members = "snowmobile, tomahasnowmobile"
            });

            config.groups.Add(new("traps")
            {
                members = "AutoTurret, BearTrap, FlameTurret, Landmine, GunTrap, ReactiveTarget, TeslaCoil, spikes.floor"
            });

            config.groups.Add(new("junkyard")
            {
                members = "magnetcrane.entity, carshredder.entity"
            });

            config.groups.Add(new("tugboats")
            {
                members = "Tugboat"
            });

            config.groups.Add(new("heliturrets")
            {
                members = "turret_attackheli"
            });

            config.groups.Add(new("ramhead")
            {
                members = "BatteringRamHead"
            });

            config.groups.Add(new("siege")
            {
                members = "SiegeTower, Catapult, Ballista, BallistaGun, BatteringRam, ConstructableEntity"
            });

            config.groups.Add(new("bees")
            {
                members = "BeeSwarmAI, Beehive, BeeGrenade, BeeSwarmMaster, NaturalBeehive"
            });

            config.groups.Add(new("farm")
            {
                members = "simplechicken.entity, FarmableAnimal, ChickenCoop"
            });
        }

        protected void BuildDefaultRuleset()
        {
            config.ruleSets ??= new();
            config.ruleSets.Clear();

            // create default ruleset
            RuleSet defaultRuleSet = new(config.defaultRuleSet)
            {
                _flags = RuleFlags.HopperCannotTargetEnemyLoot | RuleFlags.AuthorizedFarmableDamage | RuleFlags.HumanNPCDamage | RuleFlags.LockedBoxesImmortal | RuleFlags.LockedDoorsImmortal | RuleFlags.PlayerSamSitesIgnorePlayers | RuleFlags.TrapsIgnorePlayers | RuleFlags.TurretsIgnorePlayers,
                flags = "HopperCannotTargetEnemyLoot, AuthorizedFarmableDamage, HumanNPCDamage, LockedBoxesImmortal, LockedDoorsImmortal, PlayerSamSitesIgnorePlayers, TrapsIgnorePlayers, TurretsIgnorePlayers"
            };

            // create rules and add to ruleset
            defaultRuleSet.AddRule(this, "anything can hurt dispensers");
            defaultRuleSet.AddRule(this, "anything can hurt resources");
            defaultRuleSet.AddRule(this, "anything can hurt barricades");
            defaultRuleSet.AddRule(this, "anything can hurt traps");
            defaultRuleSet.AddRule(this, "anything can hurt heli");
            defaultRuleSet.AddRule(this, "anything can hurt npcs");
            defaultRuleSet.AddRule(this, "anything can hurt players");
            defaultRuleSet.AddRule(this, "nothing can hurt ch47");
            defaultRuleSet.AddRule(this, "nothing can hurt cars");
            defaultRuleSet.AddRule(this, "nothing can hurt mini");
            defaultRuleSet.AddRule(this, "nothing can hurt snowmobiles");
            defaultRuleSet.AddRule(this, "nothing can hurt ridablehorses");
            defaultRuleSet.AddRule(this, "cars cannot hurt anything");
            defaultRuleSet.AddRule(this, "mini cannot hurt anything");
            defaultRuleSet.AddRule(this, "ch47 cannot hurt anything");
            defaultRuleSet.AddRule(this, "scrapheli cannot hurt anything");
            defaultRuleSet.AddRule(this, "players cannot hurt players");
            defaultRuleSet.AddRule(this, "players cannot hurt traps");
            defaultRuleSet.AddRule(this, "guards cannot hurt players");
            defaultRuleSet.AddRule(this, "fire cannot hurt players");
            defaultRuleSet.AddRule(this, "traps cannot hurt players");
            defaultRuleSet.AddRule(this, "highwalls cannot hurt players");
            defaultRuleSet.AddRule(this, "barricades2 cannot hurt players");
            defaultRuleSet.AddRule(this, "mini cannot hurt mini");
            defaultRuleSet.AddRule(this, "npcs can hurt players");
            defaultRuleSet.AddRule(this, "junkyard cannot hurt anything");
            defaultRuleSet.AddRule(this, "junkyard can hurt cars");
            defaultRuleSet.AddRule(this, "players cannot hurt tugboats");
            defaultRuleSet.AddRule(this, "heliturrets cannot hurt players");
            defaultRuleSet.AddRule(this, "ramhead can hurt ramhead");
            defaultRuleSet.AddRule(this, "siege cannot hurt players");
            defaultRuleSet.AddRule(this, "players cannot hurt farm");

            config.ruleSets.Add(defaultRuleSet); // add ruleset to rulesets list
        }

        private bool ResetRules(string key)
        {
            if (string.IsNullOrEmpty(key) || config == null)
            {
                return false;
            }

            string old = config.defaultRuleSet;
            config.defaultRuleSet = key;
            currentRuleSet = config.GetDefaultRuleSet();

            if (currentRuleSet == null)
            {
                config.defaultRuleSet = old;
                currentRuleSet = config.GetDefaultRuleSet();
            }

            ValidateCurrentDamageHook();
            return currentRuleSet != null;
        }
        #endregion

        #region Trace
        private StringBuilder _tsb = new();
        private BaseEntity traceEntity;
        private BasePlayer tracePlayer;
        private float traceDistance;

        private void Trace(string message, int indentation = 0)
        {
            if (traceEntity == null || traceEntity.IsDestroyed)
            {
                return;
            }

            bool playerInRange = tracePlayer != null && !tracePlayer.IsDestroyed && InRange(tracePlayer.transform.position, traceEntity.transform.position, traceDistance);
            bool shouldLogToConsole = (config.options.PlayerConsole && playerInRange) || (config.options.ServerConsole && (traceDistance == 0 || playerInRange));

            if (shouldLogToConsole)
            {
                _tsb.Append(new string(' ', indentation)).AppendLine(message);
            }
        }

        private void LogTrace()
        {
            var text = _tsb.ToString();
            traceEntity = null;
            _tsb.Length = 0;
            try
            {
                if (!string.IsNullOrEmpty(text))
                {
                    if (config.options.ServerConsole)
                    {
                        Puts(text);
                    }
                    if (config.options.PlayerConsole && tracePlayer != null && tracePlayer.IsConnected)
                    {
                        tracePlayer.ConsoleMessage(text);
                    }
                    if (config.options.LogToFile)
                    {
                        LogToFile(traceFile, text, this);
                    }
                }
            }
            catch (IOException)
            {
                timer.Once(1f, () => LogToFile(traceFile, text, this));
            }
        }

        #endregion Trace

        #region Hooks/Handler Procedures
        private void OnPlayerConnected(BasePlayer player)
        {
            if (config.schedule.broadcast && !string.IsNullOrEmpty(currentBroadcastMessage))
            {
                SendReply(player, GetMessage("Prefix") + currentBroadcastMessage);
            }
            
            // Show lockout UI if enabled
            if (config.LootDefender.Enabled && (config.LootDefender.LockoutUIBradleyEnabled || config.LootDefender.LockoutUIHeliEnabled))
            {
                timer.Once(1f, () => LDUIClass.ShowLockouts(this, player));
            }
        }

        private void OnPlayerSleepEnded(BasePlayer player)
        {
            if (player != null && player.IsConnected)
            {
                data.LastSeen.Remove(player.userID);
            }
            // Show lockout UI when player wakes up
            if (config.LootDefender.Enabled && (config.LootDefender.LockoutUIBradleyEnabled || config.LootDefender.LockoutUIHeliEnabled))
            {
                timer.Once(0.5f, () => LDUIClass.ShowLockouts(this, player));
            }
        }

        private string CurrentRuleSetName() => currentRuleSet?.name;

        private bool IsEnabled() => tpveEnabled;

        private void OnTimedExplosiveExplode(TimedExplosive explosive, Vector3 explosionFxPos)
        {
            if (explosive != null)
            {
                explosive.splashWallpaperThroughWalls = false;
            }
        }

        private object OnWallpaperRemove(BuildingBlock block, int side)
        {
            if (block == null || block.IsDestroyed)
            {
                return null;
            }
            switch (side)
            {
                case 0:
                    {
                        if (block.wallpaperID != 0 && block.wallpaperHealth <= 0f)
                        {
                            using var entityLocations = GetLocationKeys(block);
                            if (CheckExclusion(entityLocations, entityLocations, trace))
                            {
                                return null;
                            }

                            if (trace) Trace("Block Damage Wallpaper1 enabled; block and return", 1);
                            block.wallpaperHealth = block.health;
                            return true;
                        }
                        break;
                    }
                case 1:
                    {
                        if (block.wallpaperID2 != 0 && block.wallpaperHealth2 <= 0f)
                        {
                            using var entityLocations = GetLocationKeys(block);
                            if (CheckExclusion(entityLocations, entityLocations, trace))
                            {
                                return null;
                            }

                            if (trace) Trace("Block Damage Wallpaper2 enabled; block and return", 1);
                            block.wallpaperHealth2 = block.health;
                            return true;
                        }
                        break;
                    }
            }
            return null;
        }

        private object OnEntityTakeDamage(ResourceEntity entity, HitInfo info)
        {
            if (info == null || info.Initiator == null || currentRuleSet == null)
            {
                return null;
            }

            RuleSet ruleSet;
            if (useZones)
            {
                // get entity and initiator locations (zones)
                using var entityLocations = GetLocationKeys(entity);
                using var initiatorLocations = GetLocationKeys(info.Initiator);

                // check for exclusion zones (zones with no rules mapped)
                if (CheckExclusion(entityLocations, initiatorLocations, trace))
                {
                    if (trace) Trace("Exclusion found; allow and return", 1);
                    return null;
                }

                if (trace) Trace("No exclusion found - looking up RuleSet...", 1);

                // process location rules
                if (config.PVEZones && initiatorLocations.IsNullOrEmpty())
                {
                    ruleSet = GetRuleSet(entityLocations, entityLocations);
                }
                else ruleSet = GetRuleSet(entityLocations, initiatorLocations);
            }
            else ruleSet = currentRuleSet;

            if (ruleSet == null)
            {
                return null;
            }

            return EvaluateRules(entity, info.Initiator, ruleSet) != DamageResult.Block ? (object)null : true;
        }

        private object OnEntityTakeDamage(BaseEntity entity, HitInfo info)
        {
            if (info == null || entity == null || entity.IsDestroyed || currentRuleSet == null)
            {
                return null;
            }

            // Skip processing for BradleyAPC and ScientistNPC entities when BradleyDrops plugin is loaded
            // This allows BradleyDrops to handle Bradley and scientist damage without interference
            if ((entity is BradleyAPC || entity is ScientistNPC) && BradleyDrops != null && BradleyDrops.IsLoaded)
            {
                return null;
            }

            // Integrated LootDefender: record damage early
            if (config.LootDefender.Enabled && info?.Initiator is BasePlayer damagePlayer && entity is BaseCombatEntity bce)
            {
                LD_RecordDamage(bce, damagePlayer, info);
            }

            if (!AllowDamage(entity, info))
            {
                if (trace) LogTrace();
                if (info.Weapon is BlowPipeWeapon)
                {
                    info.HitEntity = null;
                }
                info.damageTypes?.Clear();
                info.DidHit = false;
                info.DoHitEffects = false;
                return true;
            }

            if (trace) LogTrace();
            return null;
        }

        // =====================
        // Integrated LootDefender: Handle deaths and apply locks
        private void OnEntityDeath(PatrolHelicopter heli, HitInfo info)
        {
            if (!config.LootDefender.Enabled || heli == null) return;
            // Capture damage info for notification before it's removed
            ulong id = heli.net?.ID.Value ?? 0;
            LDDamageInfo di = null;
            if (id != 0)
            {
                _ldDamage.TryGetValue(id, out di);
            }
            LD_OnDeath(heli, LDDamageType.Heli);
            if (di != null && di.Keys != null && di.Keys.Count > 0)
            {
                TryShowKillToast(di, "Notify_HeliDestroyed");
            }
        }

        private void OnEntityDeath(BradleyAPC apc, HitInfo info)
        {
            if (!config.LootDefender.Enabled || apc == null) return;
            // Capture damage info for notification before it's removed
            ulong id = apc.net?.ID.Value ?? 0;
            LDDamageInfo di = null;
            if (id != 0)
            {
                _ldDamage.TryGetValue(id, out di);
            }
            LD_OnDeath(apc, LDDamageType.Bradley);
            if (di != null && di.Keys != null && di.Keys.Count > 0)
            {
                TryShowKillToast(di, "Notify_BradleyDestroyed");
            }
        }

        private void OnEntityDeath(BaseNpc npc, HitInfo info)
        {
            if (!config.LootDefender.Enabled || npc == null) return;
            LD_OnNpcDeath(npc);
        }

        private void OnEntityDeath(BaseNPC2 npc, HitInfo info)
        {
            if (!config.LootDefender.Enabled || npc == null) return;
            LD_OnNpcDeath(npc);
        }

        private void TryShowKillToast(LDDamageInfo di, string langKey)
        {
            try
            {
                if (di == null || config?.Notify == null || !config.Notify.Enabled)
                {
                    return;
                }

                // Duration
                TimeSpan span = DateTime.Now.Subtract(di.Start);
                string duration = FormatDuration(span);

                // Contributors list
                string top = BuildTopContributors(di);

                string fmt = GetMessage(langKey, null);
                string text = string.IsNullOrEmpty(fmt) ? $"{langKey}: {duration} {top}" : string.Format(fmt, duration, top);

                foreach (var player in BasePlayer.activePlayerList)
                {
                    if (player != null)
                    {
                        player.SendConsoleCommand("gametip.showtoast", config.Notify.Style, text, string.Empty);
                    }
                }
            }
            catch { }
        }

        private string FormatDuration(TimeSpan span)
        {
            if (span.TotalHours >= 1.0)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}h {1}m {2}s", (int)span.TotalHours, span.Minutes, span.Seconds);
            }
            if (span.TotalMinutes >= 1.0)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}m {1}s", (int)span.TotalMinutes, span.Seconds);
            }
            return string.Format(CultureInfo.InvariantCulture, "{0}s", Math.Max(0, (int)span.TotalSeconds));
        }

        private string BuildTopContributors(LDDamageInfo di)
        {
            if (di == null || di.Keys == null || di.Keys.Count == 0)
            {
                return "n/a";
            }
            int take = Math.Max(1, config.Notify.TopContributors);
            var parts = new List<string>(take);
            
            // Sort by damage descending (manual sort instead of OrderByDescending)
            var sortedKeys = new List<LDDamageKey>(di.Keys);
            sortedKeys.Sort((a, b) => 
            {
                float dmgA = a.Entry?.DamageDealt ?? 0f;
                float dmgB = b.Entry?.DamageDealt ?? 0f;
                return dmgB.CompareTo(dmgA);
            });
            
            // Take only the top N (manual take instead of Take())
            int count = Math.Min(take, sortedKeys.Count);
            for (int i = 0; i < count; i++)
            {
                var k = sortedKeys[i];
                string name = string.IsNullOrWhiteSpace(k.Name) ? (k.UserId != 0 ? k.UserId.ToString() : "Unknown") : k.Name;
                int dmg = Mathf.RoundToInt(k.Entry?.DamageDealt ?? 0f);
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1}", name, dmg));
            }
            return string.Join(", ", parts);
        }

        private string DescribeOwners(LDDamageInfo di)
        {
            if (di == null || di.Keys == null || di.Keys.Count == 0)
            {
                return string.Empty;
            }
            // Determine winners/owners as in LD_SelectOwners logic, but produce names
            var owners = LD_SelectOwners(di);
            if (owners == null || owners.Count == 0)
            {
                return string.Empty;
            }
            var names = new List<string>(owners.Count);
            foreach (var id in owners)
            {
                BasePlayer p = BasePlayer.FindAwakeOrSleepingByID(id);
                string name;
                if (p != null)
                {
                    name = p.displayName;
                }
                else
                {
                    // Manual FirstOrDefault - find first key matching the user ID
                    LDDamageKey foundKey = null;
                    foreach (var k in di.Keys)
                    {
                        if (k.UserId == id)
                        {
                            foundKey = k;
                            break;
                        }
                    }
                    name = foundKey?.Name ?? id.ToString();
                }
                if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
            }
            return string.Join(", ", names);
        }

        private void LD_OnNpcDeath(BaseEntity npc)
        {
            // NPCs: use the tracked damage, then lock corpses/loot nearby after a brief delay
            ulong id = npc.net?.ID.Value ?? 0;
            if (id == 0) return;
            if (_ldDamage.TryGetValue(id, out var di))
            {
                di.IsKilled = true;
                LD_ApplyLocks(di);
                _ldDamage.Remove(id);
            }
            // second pass to catch newly spawned corpse/loot
            timer.Once(0.3f, () =>
            {
                if (npc == null) return;
                var di2 = new LDDamageInfo(LDDamageType.NPC, npc, DateTime.Now, config.LootDefender.LockSeconds, config.LootDefender.LockRadius)
                {
                    Keys = new(),
                };
                LD_ApplyLocks(di2);
            });
        }

        // =====================
        // Heli Distance Unlock Check
        // =====================
        private void CheckHeliDistanceUnlock()
        {
            if (!config.LootDefender.Enabled || config.LootDefender.HeliUnlockDistance <= 0f) return;
            
            // Check all locked entities - find helis
            var heliIdsToUnlock = Pool.Get<List<ulong>>();
            heliIdsToUnlock.Clear();
            
            foreach (var kvp in _ldLocks)
            {
                ulong entityId = kvp.Key;
                var lockInfo = kvp.Value;
                
                if (lockInfo.Owners == null || lockInfo.Owners.Count == 0) continue;
                
                // Find the entity - only check PatrolHelicopter
                var entity = BaseNetworkable.serverEntities.Find(new(entityId)) as PatrolHelicopter;
                if (entity == null || entity.IsDestroyed || !(entity is PatrolHelicopter)) continue;
                
                // Check distance from all owners
                bool allOwnersTooFar = true;
                foreach (ulong ownerId in lockInfo.Owners)
                {
                    BasePlayer owner = BasePlayer.FindAwakeOrSleepingByID(ownerId);
                    if (owner != null && owner.IsConnected)
                    {
                        float distance = Vector3.Distance(entity.transform.position, owner.transform.position);
                        if (distance <= config.LootDefender.HeliUnlockDistance)
                        {
                            allOwnersTooFar = false;
                            break;
                        }
                    }
                }
                
                // If all owners are too far, mark for unlock
                if (allOwnersTooFar)
                {
                    heliIdsToUnlock.Add(entityId);
                }
            }
            
            // Unlock helis that are too far from owners
            for (int i = 0; i < heliIdsToUnlock.Count; i++)
            {
                ulong heliId = heliIdsToUnlock[i];
                if (!_ldLocks.TryGetValue(heliId, out var lockInfo)) continue;
                
                var heli = BaseNetworkable.serverEntities.Find(new(heliId)) as PatrolHelicopter;
                if (heli != null && !heli.IsDestroyed)
                {
                    _ldLocks.Remove(heliId);
                    if (lockInfo.ExpireTimer != null)
                    {
                        lockInfo.ExpireTimer.Destroy();
                    }
                    heli.OwnerID = 0;
                    
                    // Broadcast unlock notification if configured
                    if (config.LootDefender.HeliBroadcastUnlocked)
                    {
                        string msg = GetMessage("HeliUnlocked", null);
                        if (string.IsNullOrEmpty(msg)) msg = $"The heli at {PositionToGrid(heli.transform.position)} has been unlocked.";
                        else msg = string.Format(msg, PositionToGrid(heli.transform.position));
                        foreach (var player in BasePlayer.activePlayerList)
                        {
                            if (player != null && player.IsConnected)
                            {
                                player.ChatMessage(msg);
                            }
                        }
                    }
                }
            }
            
            Pool.FreeUnmanaged(ref heliIdsToUnlock);
        }

        #region Discord Messages
        private bool CanSendDiscordMessage()
        {
            if (string.IsNullOrEmpty(config.LootDefender.DiscordWebhookUrl) || 
                config.LootDefender.DiscordWebhookUrl == "https://support.discordapp.com/hc/en-us/articles/228383668-Intro-to-Webhooks")
            {
                return false;
            }
            return true;
        }

        private void SendDiscordMessage(LDDamageInfo di, LDDamageType type)
        {
            if (!CanSendDiscordMessage() || di == null || di.Keys == null || di.Keys.Count == 0) return;

            HashSet<ulong> members = new();
            List<string> usernames = new();

            foreach (var key in di.Keys)
            {
                if (key.UserId > 0 && !members.Contains(key.UserId))
                {
                    members.Add(key.UserId);
                    usernames.Add(key.Name);
                }
            }

            if (members.Count == 0) return;

            if (config.LootDefender.DiscordNotifyConsole)
            {
                string typeName = type == LDDamageType.Bradley ? "Bradley" : type == LDDamageType.Heli ? "Heli" : "NPC";
                Puts($"{typeName} killed by {string.Join(", ", usernames)} at {di.Position}");
            }

            Dictionary<string, string> players = new();
            foreach (ulong memberId in members)
            {
                var memberIdString = memberId.ToString();
                var memberName = covalence.Players.FindPlayerById(memberIdString)?.Name ?? memberIdString;

                if (config.LootDefender.DiscordBattleMetrics)
                {
                    players[memberName] = $"https://www.battlemetrics.com/rcon/players?filter%5Bsearch%5D={memberIdString}&filter%5Bservers%5D=false&filter%5BplayerFlags%5D=&sort=score&showServers=true";
                }
                else
                {
                    players[memberName] = memberIdString;
                }
            }

            string messageText = type == LDDamageType.Bradley ? "A bradley was killed." : "A heli was killed.";
            SendDiscordMessage(players, di.Position, messageText);
        }

        private void SendDiscordMessage(Dictionary<string, string> members, Vector3 position, string text)
        {
            string grid = $"{PositionToGrid(position)} {position}";
            StringBuilder log = new();

            foreach (var member in members)
            {
                log.AppendLine($"[{DateTime.Now}] {member.Key} {member.Value} @ {grid}): {text}");
            }

            LogToFile("kills", log.ToString(), this);

            List<object> _fields = new();
            foreach (var member in members)
            {
                _fields.Add(new
                {
                    name = config.LootDefender.DiscordEmbedPlayer,
                    value = $"[{member.Key}]({member.Value})",
                    inline = true
                });
            }

            _fields.Add(new
            {
                name = config.LootDefender.DiscordEmbedMessage,
                value = text,
                inline = false
            });

            _fields.Add(new
            {
                name = ConVar.Server.hostname,
                value = grid,
                inline = false
            });

            _fields.Add(new
            {
                name = config.LootDefender.DiscordEmbedServer,
                value = $"steam://connect/{ConVar.Server.ip}:{ConVar.Server.port}",
                inline = false
            });

            string json = JsonConvert.SerializeObject(_fields.ToArray());
            Interface.CallHook("API_SendFancyMessage", config.LootDefender.DiscordWebhookUrl, config.LootDefender.DiscordEmbedTitle, config.LootDefender.DiscordMessageColor, json, null, this);
        }
        #endregion

        #region Damage Reports
        private class LDDamageGroup
        {
            public float TotalDamage { get; set; }
            public LDDamageKey FirstDamagerDealer { get; set; }
            private List<ulong> additionalPlayers { get; set; } = new();

            [JsonIgnore]
            public List<ulong> Players
            {
                get
                {
                    List<ulong> players = new() { FirstDamagerDealer.UserId };
                    foreach (var targetId in additionalPlayers)
                    {
                        if (!players.Contains(targetId))
                        {
                            players.Add(targetId);
                        }
                    }
                    return players;
                }
            }

            public LDDamageGroup() { }

            public LDDamageGroup(LDDamageKey x)
            {
                TotalDamage = x.Entry.DamageDealt;
                FirstDamagerDealer = x;

                if (RelationshipManager.ServerInstance.playerToTeam.TryGetValue(x.UserId, out var team))
                {
                    for (int i = 0; i < team.members.Count; i++)
                    {
                        ulong member = team.members[i];
                        if (member == x.UserId || additionalPlayers.Contains(member))
                        {
                            continue;
                        }
                        additionalPlayers.Add(member);
                    }
                }
            }

            public string ToReport(LDDamageKey damageKey, LDDamageInfo damageInfo, TruePVE instance)
            {
                float damage = 0f;
                if (damageInfo.Keys != null)
                {
                    for (int i = 0; i < damageInfo.Keys.Count; i++)
                    {
                        var key = damageInfo.Keys[i];
                        if (key.UserId == damageKey.UserId)
                        {
                            damage = key.Entry?.DamageDealt ?? 0f;
                            break;
                        }
                    }
                }
                var totalDamage = damageInfo.TotalDamage();
                var percent = damage > 0 && totalDamage > 0 ? damage / totalDamage * 100 : 0;
                var color = additionalPlayers.Count == 0 ? instance.config.LootDefender.ReportSinglePlayerColor : instance.config.LootDefender.ReportTeamColor;
                var damageLine = $"{damage:0.00} ({percent:0.00}%)";
                return $"<color={color}>{damageKey.Name}</color> <color=#C0C0C0>{damageLine}</color>";
            }
        }

        private void DisplayDamageReport(LDDamageInfo di, LDDamageType type)
        {
            if (di == null || di.Keys == null || di.Keys.Count == 0) return;

            if (type == LDDamageType.Bradley || type == LDDamageType.Heli)
            {
                foreach (var target in BasePlayer.activePlayerList)
                {
                    if (target != null && target.IsConnected && CanDisplayReport(target, di, type))
                    {
                        target.ChatMessage(GetDamageReport(di, type, target.userID));
                    }
                }
            }
            else if (type == LDDamageType.NPC)
            {
                foreach (var key in di.Keys)
                {
                    BasePlayer attacker = BasePlayer.FindAwakeOrSleepingByID(key.UserId);
                    if (attacker != null && attacker.IsConnected && CanDisplayReport(attacker, di, type))
                    {
                        attacker.ChatMessage(GetDamageReport(di, type, key.UserId));
                    }
                }
            }
        }

        private bool CanDisplayReport(BasePlayer target, LDDamageInfo di, LDDamageType type)
        {
            if (target == null || !target.IsConnected || type == LDDamageType.None)
            {
                return false;
            }

            // For now, always show reports if enabled (can be enhanced with per-type settings later)
            return true;
        }

        private string GetDamageReport(LDDamageInfo di, LDDamageType type, ulong targetId)
        {
            if (di == null || di.Keys == null || di.Keys.Count == 0) return string.Empty;

            var nameKey = type == LDDamageType.Bradley ? "Bradley" : type == LDDamageType.Heli ? "Heli" : "NPC";
            var sb = new StringBuilder();
            sb.AppendLine($"<color={config.LootDefender.ReportOkColor}>Damage report for {nameKey}</color>:");

            if (type == LDDamageType.Bradley || type == LDDamageType.Heli)
            {
                var seconds = Math.Ceiling((DateTime.Now - di.Start).TotalSeconds);
                sb.AppendLine($"{nameKey} was taken down after {seconds} seconds");
            }

            // Build damage groups
            List<LDDamageGroup> damageGroups = new();
            foreach (var key in di.Keys)
            {
                damageGroups.Add(new LDDamageGroup(key));
            }
            damageGroups.Sort((x, y) => y.TotalDamage.CompareTo(x.TotalDamage));

            // Determine who can interact (top damage dealers)
            HashSet<ulong> canInteract = new();
            if (damageGroups.Count > 0)
            {
                var topGroup = damageGroups[0];
                canInteract.Add(topGroup.FirstDamagerDealer.UserId);
                foreach (var playerId in topGroup.Players)
                {
                    canInteract.Add(playerId);
                }
            }

            foreach (var damageGroup in damageGroups)
            {
                bool canLoot = canInteract.Contains(damageGroup.FirstDamagerDealer.UserId);
                sb.Append(canLoot ? $"<color={config.LootDefender.ReportOkColor}>√</color> " : $"<color={config.LootDefender.ReportNotOkColor}>X</color> ");
                sb.AppendLine(damageGroup.ToReport(damageGroup.FirstDamagerDealer, di, this));
            }

            return sb.ToString();
        }
        #endregion

        private void LD_OnDeath(BaseCombatEntity victim, LDDamageType type)
        {
            ulong id = victim.net?.ID.Value ?? 0;
            if (id == 0) return;
            if (_ldDamage.TryGetValue(id, out var di))
            {
                di.IsKilled = true;
                
                // Display damage report
                DisplayDamageReport(di, type);
                
                // Send Discord message
                if (type == LDDamageType.Bradley || type == LDDamageType.Heli)
                {
                    SendDiscordMessage(di, type);
                }
                
                // Give rewards to all participants
                if (di.Keys != null && di.Keys.Count > 0)
                {
                    int totalParticipants = di.Keys.Count;
                    for (int i = 0; i < di.Keys.Count; i++)
                    {
                        var key = di.Keys[i];
                        BasePlayer player = BasePlayer.FindAwakeOrSleepingByID(key.UserId);
                        if (player != null && player.IsConnected)
                        {
                            string weapon = key.Entry?.Weapon ?? "";
                            float distance = Vector3.Distance(player.transform.position, di.Position);
                            GiveRewards(di, player, weapon, distance, totalParticipants);
                        }
                    }
                }
                
                // Set lockouts for participants
                if (di.Keys != null && di.Keys.Count > 0)
                {
                    double lockoutMinutes = 0;
                    if (type == LDDamageType.Bradley && config.LootDefender.LockoutBradleyMinutes > 0)
                    {
                        lockoutMinutes = config.LootDefender.LockoutBradleyMinutes;
                    }
                    else if (type == LDDamageType.Heli && config.LootDefender.LockoutHeliMinutes > 0)
                    {
                        lockoutMinutes = config.LootDefender.LockoutHeliMinutes;
                    }
                    
                    if (lockoutMinutes > 0)
                    {
                        for (int i = 0; i < di.Keys.Count; i++)
                        {
                            var key = di.Keys[i];
                            BasePlayer player = BasePlayer.FindAwakeOrSleepingByID(key.UserId);
                            if (player != null)
                            {
                                SetLockout(player, type, lockoutMinutes);
                                
                                // Also lockout team/clan if configured
                                if (config.LootDefender.LockoutTeam)
                                {
                                    var team = RelationshipManager.ServerInstance.playerToTeam.GetValueOrDefault(key.UserId);
                                    if (team != null)
                                    {
                                        foreach (var memberId in team.members)
                                        {
                                            if (memberId != key.UserId)
                                            {
                                                BasePlayer member = BasePlayer.FindAwakeOrSleepingByID(memberId);
                                                if (member != null)
                                                {
                                                    SetLockout(member, type, lockoutMinutes);
                                                }
                                            }
                                        }
                                    }
                                }
                                
                                if (config.LootDefender.LockoutClan && Clans != null)
                                {
                                    var clanMembers = Clans.Call("GetClanMembers", key.UserId) as List<string>;
                                    if (clanMembers != null)
                                    {
                                        foreach (var memberIdStr in clanMembers)
                                        {
                                            if (ulong.TryParse(memberIdStr, out ulong memberId) && memberId != key.UserId)
                                            {
                                                BasePlayer member = BasePlayer.FindAwakeOrSleepingByID(memberId);
                                                if (member != null)
                                                {
                                                    SetLockout(member, type, lockoutMinutes);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                
                LD_ApplyLocks(di);
                _ldDamage.Remove(id);
            }
            // second pass for delayed spawns (crates/gibs)
            timer.Once(0.3f, () =>
            {
                var di2 = new LDDamageInfo(type, victim, DateTime.Now, config.LootDefender.LockSeconds, config.LootDefender.LockRadius)
                {
                    Keys = new(),
                };
                LD_ApplyLocks(di2);
            });
        }

        // =====================
        // Can Lock Checks (with Harbor, Monument, etc.)
        // =====================
        private bool CanLockBradley(BaseEntity entity)
        {
            if (entity == null || !entity.IsValid()) return false;
            
            // Check threshold
            if (config.LootDefender.BradleyThreshold <= 0f) return false;
            
            // Check if from Monument Bradley Plugin
            if (!config.LootDefender.BradleyLockMonument && BradleyDrops != null)
            {
                if (BradleyDrops.CallHook("IsBradleyDrop", entity.skinID) != null)
                {
                    return false;
                }
            }
            
            // Check harbor (skinID 81182151852251420)
            if (entity.skinID == 81182151852251420)
            {
                return config.LootDefender.BradleyLockHarbor;
            }
            
            // Check convoy (skinID 755446)
            if (entity.skinID == 755446)
            {
                return true; // Default to true for convoy
            }
            
            // Check monument (skinID 8675309)
            if (entity.skinID == 8675309)
            {
                return config.LootDefender.BradleyLockMonument;
            }
            
            // Default to true for other bradleys
            return true;
        }

        private bool CanLockHeli(BaseCombatEntity entity)
        {
            if (entity == null || !entity.IsValid()) return false;
            
            // Check threshold
            if (config.LootDefender.HeliThreshold <= 0f) return false;
            
            // Check if it's a HeliSignal object (skip locking)
            if (HeliSignals != null && HeliSignals.CallHook("IsHeliSignalObject", entity.skinID) != null)
            {
                return false;
            }
            
            // Check harbor (skinID 81182151852251420)
            if (entity.skinID == 81182151852251420)
            {
                // Use Heli setting if set, otherwise use Bradley setting
                if (config.LootDefender.HeliLockHarbor.HasValue)
                {
                    return config.LootDefender.HeliLockHarbor.Value;
                }
                return config.LootDefender.BradleyLockHarbor;
            }
            
            // Check convoy (skinID 755446)
            if (entity.skinID == 755446)
            {
                return true; // Default to true for convoy
            }
            
            // Default to true for other helis
            return true;
        }

        private bool CanLockNpc(BaseEntity entity)
        {
            if (entity == null || !entity.IsValid()) return false;
            
            // Check threshold
            if (config.LootDefender.NpcThreshold <= 0f) return false;
            
            // Check if owner ID is a Steam ID (player corpse)
            if (entity.OwnerID.IsSteamId())
            {
                return false;
            }
            
            return true;
        }

        private void LD_ApplyLocks(LDDamageInfo di)
        {
            if (di == null) return;

            // Check if we should lock this entity type
            bool shouldLock = false;
            if (di.Type == LDDamageType.Bradley)
            {
                if (!config.LootDefender.LockBradley) return;
                // Note: CanLockBradley check would need entity reference, which we don't have here
                // This is handled in damage threshold checks
                shouldLock = true;
            }
            else if (di.Type == LDDamageType.Heli)
            {
                if (!config.LootDefender.LockHeli) return;
                shouldLock = true;
            }
            else if (di.Type == LDDamageType.NPC)
            {
                if (!config.LootDefender.LockNpc) return;
                shouldLock = true;
            }
            
            if (!shouldLock) return;

            // Determine owners
            var owners = LD_SelectOwners(di);
            if (owners == null || owners.Count == 0) return;
            
            // Broadcast NPC locked notification if configured
            if (di.Type == LDDamageType.NPC && config.LootDefender.NpcBroadcastLocked)
            {
                string npcName = di.Keys != null && di.Keys.Count > 0 ? di.Keys[0].Name : "NPC";
                string ownerNames = DescribeOwners(di);
                if (!string.IsNullOrEmpty(ownerNames))
                {
                    string msg = GetMessage("Notify_NPCLocked", null);
                    if (string.IsNullOrEmpty(msg)) msg = $"{npcName} has been locked to {ownerNames} and their team";
                    else msg = string.Format(msg, npcName, ownerNames);
                    foreach (var player in BasePlayer.activePlayerList)
                    {
                        if (player != null && player.IsConnected)
                        {
                            player.ChatMessage(msg);
                        }
                    }
                }
            }

            // Remove fire if configured
            if (config.LootDefender.RemoveFireFromCrates)
            {
                using var fires = Pool.Get<PooledList<FireBall>>();
                Vis.Entities(di.Position, 30f, fires, -1);
                for (int i = 0; i < fires.Count; i++)
                {
                    FireBall fb = fires[i];
                    if (fb != null && !fb.IsDestroyed) fb.Kill();
                }
            }

            // Lock relevant loot entities nearby (strict filters to avoid world loot)
            using var ents = Pool.Get<PooledList<BaseEntity>>();
            Vis.Entities(di.Position, di.LockRadius, ents, -1);
            for (int i = 0; i < ents.Count; i++)
            {
                var e = ents[i];
                if (e == null || e.IsDestroyed) continue;

                // Filter by type depending on death type and options
                bool lockIt = false;
                if (di.Type == LDDamageType.Bradley && config.LootDefender.LockBradley)
                {
                    // Bradley: only lock crates spawned by entity, not generic world loot
                    lockIt = e is LockedByEntCrate;
                }
                else if (di.Type == LDDamageType.Heli && config.LootDefender.LockHeli)
                {
                    // Heli: only lock heli crates
                    lockIt = e is LockedByEntCrate;
                }
                else if (di.Type == LDDamageType.NPC && config.LootDefender.LockNpc)
                {
                    // NPC: lock only NPC corpses/containers (non-Steam IDs)
                    if (e is LootableCorpse lc)
                    {
                        lockIt = !lc.playerSteamID.IsSteamId();
                    }
                    else if (e is DroppedItemContainer dic)
                    {
                        lockIt = !dic.playerSteamID.IsSteamId();
                    }
                }

                if (!lockIt) continue;
                LD_LockEntity(e, owners, di.LockSeconds);
            }
        }

        private HashSet<ulong> LD_SelectOwners(LDDamageInfo di)
        {
            var owners = Pool.Get<HashSet<ulong>>();
            owners.Clear();

            if (di.Keys == null || di.Keys.Count == 0)
            {
                // No damage info; nothing to lock to
                return owners;
            }

            if (config.LootDefender.GroupByTeam)
            {
                var teamToDamage = Pool.Get<Dictionary<ulong, float>>();
                teamToDamage.Clear();
                for (int i = 0; i < di.Keys.Count; i++)
                {
                    var k = di.Keys[i];
                    ulong team = k.Entry?.TeamID ?? 0;
                    if (!teamToDamage.TryGetValue(team, out var sum)) sum = 0f;
                    sum += k.Entry?.DamageDealt ?? 0f;
                    teamToDamage[team] = sum;
                }
                ulong bestTeam = 0;
                float best = -1f;
                foreach (var kvp in teamToDamage)
                {
                    if (kvp.Value > best)
                    {
                        best = kvp.Value;
                        bestTeam = kvp.Key;
                    }
                }
                for (int i = 0; i < di.Keys.Count; i++)
                {
                    var k = di.Keys[i];
                    if ((k.Entry?.TeamID ?? 0) == bestTeam && k.UserId.IsSteamId()) owners.Add(k.UserId);
                }
                Pool.FreeUnmanaged(ref teamToDamage);
            }
            else
            {
                LDDamageKey bestKey = null;
                float best = -1f;
                for (int i = 0; i < di.Keys.Count; i++)
                {
                    var k = di.Keys[i];
                    if ((k.Entry?.DamageDealt ?? 0f) > best)
                    {
                        best = k.Entry.DamageDealt;
                        bestKey = k;
                    }
                }
                if (bestKey != null && bestKey.UserId.IsSteamId()) owners.Add(bestKey.UserId);
            }

            return owners;
        }

        private void LD_LockEntity(BaseEntity entity, HashSet<ulong> owners, int lockSeconds)
        {
            if (entity == null || entity.IsDestroyed || entity.net == null) return;
            ulong eid = entity.net.ID.Value;
            if (!_ldLocks.TryGetValue(eid, out var li))
            {
                li = new LDLockInfo
                {
                    LockedAt = DateTime.Now,
                    LockSeconds = lockSeconds,
                    AllowAllies = config.LootDefender.AllowAllies,
                    GroupByTeam = config.LootDefender.GroupByTeam,
                };
                _ldLocks[eid] = li;
            }
            foreach (var o in owners) li.Owners.Add(o);
            if (li.ExpireTimer != null)
            {
                li.ExpireTimer.Destroy();
                li.ExpireTimer = null;
            }
            if (lockSeconds > 0)
            {
                li.ExpireTimer = timer.Once(lockSeconds, () =>
                {
                    _ldLocks.Remove(eid);
                });
            }
        }

        private void LD_RecordDamage(BaseCombatEntity entity, BasePlayer attacker, HitInfo info)
        {
            if (attacker == null || entity == null || entity.IsDestroyed) return;
            if (entity is BasePlayer) return; // ignore players for LD

            LDDamageType type = LDDamageType.None;
            if (entity is PatrolHelicopter) type = LDDamageType.Heli;
            else if (entity is BradleyAPC) type = LDDamageType.Bradley;
            else if (entity is BaseNpc or BaseNPC2) type = LDDamageType.NPC;
            if (type == LDDamageType.None) return;

            // Check lockouts
            if (HasLockout(attacker, type, entity.skinID))
            {
                // Block damage if locked out (unless BlockLootingOnly is true)
                if (!config.LootDefender.BlockLootingOnly && info != null)
                {
                    info.damageTypes.Clear();
                }
                return;
            }

            ulong id = entity.net?.ID.Value ?? 0;
            if (id == 0) return;

            // Check damage threshold and permissions for locking
            float damage = info.damageTypes?.Total() ?? 0f;
            float maxHealth = entity.MaxHealth();
            bool shouldLock = false;
            
            if (type == LDDamageType.Bradley && config.LootDefender.BradleyThreshold > 0f)
            {
                if (CanLockBradley(entity))
                {
                    if (damage >= maxHealth * config.LootDefender.BradleyThreshold && !permission.UserHasPermission(attacker.UserIDString, "truepve.lootdefender.bypassbradleylock"))
                    {
                        shouldLock = true;
                    }
                }
            }
            else if (type == LDDamageType.Heli && config.LootDefender.HeliThreshold > 0f)
            {
                if (CanLockHeli(entity))
                {
                    if (damage >= maxHealth * config.LootDefender.HeliThreshold && !permission.UserHasPermission(attacker.UserIDString, "truepve.lootdefender.bypasshelilock"))
                    {
                        shouldLock = true;
                    }
                }
            }
            else if (type == LDDamageType.NPC && config.LootDefender.NpcThreshold > 0f)
            {
                if (CanLockNpc(entity))
                {
                    if (damage >= maxHealth * config.LootDefender.NpcThreshold && !permission.UserHasPermission(attacker.UserIDString, "truepve.lootdefender.bypassnpclock"))
                    {
                        shouldLock = true;
                    }
                }
            }

            if (!_ldDamage.TryGetValue(id, out var di))
            {
                int lockTime = type == LDDamageType.Bradley ? config.LootDefender.BradleyLockTime :
                               type == LDDamageType.Heli ? config.LootDefender.HeliLockTime :
                               config.LootDefender.NpcLockTime;
                di = new LDDamageInfo(type, entity, DateTime.Now, lockTime, config.LootDefender.LockRadius);
                _ldDamage[id] = di;
            }
            // Get weapon name
            string weapon = "";
            if (info?.WeaponPrefab != null)
            {
                weapon = info.WeaponPrefab.ShortPrefabName;
            }
            else if (attacker.GetHeldEntity() is HeldEntity held)
            {
                weapon = held.ShortPrefabName;
            }
            di.AddDamage(entity, attacker, damage, weapon);

            // Periodic owner toast while fighting, for Bradley/Heli
            if (config.LootDefender.OwnerToastCombatSeconds > 0 && (type == LDDamageType.Bradley || type == LDDamageType.Heli))
            {
                string key = attacker.userID.ToString() + ":" + id.ToString();
                if (!_ownerToastLast.TryGetValue(key, out var last) || (DateTime.Now - last).TotalSeconds >= config.LootDefender.OwnerToastCombatSeconds)
                {
                    _ownerToastLast[key] = DateTime.Now;
                    string owners = DescribeOwners(di);
                    if (!string.IsNullOrEmpty(owners) && !owners.Equals(attacker.displayName, StringComparison.Ordinal))
                    {
                        string msgKey = type == LDDamageType.Heli ? "Notify_HeliOwned" : "Notify_BradleyOwned";
                        string fmt = GetMessage(msgKey, attacker.UserIDString);
                        string text = string.IsNullOrEmpty(fmt) ? owners : string.Format(fmt, owners);
                        attacker.SendConsoleCommand("gametip.showtoast", config.Notify.Style, text, string.Empty);
                    }
                }
            }
        }

        // =====================
        // F15 Event Handler
        // =====================
        private void OnEventTrigger(TriggeredEventPrefab prefab)
        {
            if (config.LootDefender.Enabled && config.LootDefender.LockoutBypassF15 && !_isF15EventActive && prefab != null && prefab.name == "assets/bundled/prefabs/world/event_f15e.prefab")
            {
                Puts("F15 event has started; bypassing player lockouts!");
                _isF15EventActive = true;
            }
        }

        // =====================
        // Lockout System
        // =====================
        private bool HasLockout(BasePlayer player, LDDamageType type, ulong skinID = 0)
        {
            if (player == null || !player.IsValid() || _isF15EventActive) return false;
            if (permission.UserHasPermission(player.UserIDString, "truepve.lootdefender.bypass.lockouts")) return false;
            
            string playerId = player.UserIDString;
            if (!_lockouts.TryGetValue(playerId, out var lockout)) return false;
            
            // Check skin ID exceptions
            if (skinID != 0 && config.LootDefender.LockoutExceptions.Contains(skinID)) return false;
            
            double currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (type == LDDamageType.Bradley && config.LootDefender.LockoutBradleyMinutes > 0)
            {
                return lockout.Bradley > currentTime;
            }
            if (type == LDDamageType.Heli && config.LootDefender.LockoutHeliMinutes > 0)
            {
                return lockout.Heli > currentTime;
            }
            return false;
        }

        private void SetLockout(BasePlayer player, LDDamageType type, double minutes)
        {
            if (player == null || !player.IsValid() || minutes <= 0) return;
            if (permission.UserHasPermission(player.UserIDString, "truepve.lootdefender.bypass.lockouts")) return;
            
            string playerId = player.UserIDString;
            if (!_lockouts.TryGetValue(playerId, out var lockout))
            {
                lockout = new LockoutInfo();
                _lockouts[playerId] = lockout;
            }
            
            double currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            double lockoutTime = currentTime + (minutes * 60);
            
            if (type == LDDamageType.Bradley)
            {
                lockout.Bradley = lockoutTime;
            }
            else if (type == LDDamageType.Heli)
            {
                lockout.Heli = lockoutTime;
            }
            
            // Update UI if player is connected
            if (player.IsConnected && config.LootDefender.Enabled && (config.LootDefender.LockoutUIBradleyEnabled || config.LootDefender.LockoutUIHeliEnabled))
            {
                timer.Once(0.1f, () => LDUIClass.UpdateLockoutUI(this, player));
            }
        }

        // =====================
        // Reward System (XP, ShoppyStock, Multipliers)
        // =====================
        private void GiveRewards(LDDamageInfo di, BasePlayer attacker, string weapon, float distance, int totalParticipants)
        {
            if (attacker == null || di == null || di.Keys == null || di.Keys.Count == 0) return;
            
            // Find the attacker's damage key
            LDDamageKey attackerKey = null;
            for (int i = 0; i < di.Keys.Count; i++)
            {
                if (di.Keys[i].UserId == attacker.userID)
                {
                    attackerKey = di.Keys[i];
                    break;
                }
            }
            if (attackerKey == null) return;
            
            // Get base reward amounts
            double xpAmount = 0;
            double shopAmount = 0;
            
            if (di.Type == LDDamageType.Bradley)
            {
                xpAmount = config.LootDefender.BradleyXP;
                shopAmount = config.LootDefender.BradleySS;
            }
            else if (di.Type == LDDamageType.Heli)
            {
                xpAmount = config.LootDefender.HeliXP;
                shopAmount = config.LootDefender.HeliSS;
            }
            else if (di.Type == LDDamageType.NPC)
            {
                xpAmount = config.LootDefender.NpcXP;
                shopAmount = config.LootDefender.NpcSS;
            }
            
            // Apply multipliers for NPC only
            if (di.Type == LDDamageType.NPC)
            {
                // Apply distance multiplier
                if (config.LootDefender.NpcDistanceMultiplier != null)
                {
                    double distanceMult = config.LootDefender.NpcDistanceMultiplier.GetDistanceMult(distance);
                    xpAmount = Math.Round(distanceMult * xpAmount, 0);
                    shopAmount = Math.Round(distanceMult * shopAmount, 0);
                }
                
                // Apply weapon multiplier
                if (!string.IsNullOrEmpty(weapon) && config.LootDefender.NpcWeaponMultipliers != null && config.LootDefender.NpcWeaponMultipliers.TryGetValue(weapon, out double weaponMult))
                {
                    xpAmount = Math.Round(weaponMult * xpAmount, 0);
                    shopAmount = Math.Round(weaponMult * shopAmount, 0);
                    if (xpAmount < 1) xpAmount = 1;
                    if (shopAmount < 1) shopAmount = 1;
                }
            }
            
            // Give XP rewards
            if (xpAmount > 0)
            {
                GiveXPReward(attacker, xpAmount, di.Type);
            }
            
            // Give ShoppyStock rewards
            if (shopAmount > 0 && totalParticipants > 0)
            {
                shopAmount = Math.Round(shopAmount / totalParticipants, 0);
                shopAmount = Math.Max(1, shopAmount);
                GiveShoppyStockReward(attacker, shopAmount, di.Type);
            }
        }

        private void GiveXPReward(BasePlayer player, double amount, LDDamageType type)
        {
            if (player == null || amount <= 0) return;
            
            string shopName = type == LDDamageType.Bradley ? config.LootDefender.BradleyShoppyStockShopName :
                             type == LDDamageType.Heli ? config.LootDefender.HeliShoppyStockShopName :
                             config.LootDefender.NpcShoppyStockShopName;
            
            if (SkillTree != null)
            {
                SkillTree?.Call("AwardXP", player, amount, Name);
            }
            if (XPerience != null)
            {
                XPerience?.Call("GiveXPID", player.userID, amount);
            }
            if (XLevels != null)
            {
                XLevels?.Call("API_GiveXP", player, (float)amount);
            }
        }

        private void GiveShoppyStockReward(BasePlayer player, double amount, LDDamageType type)
        {
            if (player == null || amount <= 0) return;
            
            string shopName = type == LDDamageType.Bradley ? config.LootDefender.BradleyShoppyStockShopName :
                             type == LDDamageType.Heli ? config.LootDefender.HeliShoppyStockShopName :
                             config.LootDefender.NpcShoppyStockShopName;

            int units = Mathf.Max(1, (int)amount);

            // Prefer Harmony ShoppyStock when present; otherwise deposit via Economics Harmony mod.
            if (ShoppyStock != null && ShoppyStock.IsLoaded)
            {
                if (string.IsNullOrEmpty(shopName)) return;
                ShoppyStock.Call("GiveCurrency", shopName, player.userID, units);
            }
            else if (Economics != null && Economics.IsLoaded)
            {
                Economics.Call("Deposit", player.UserIDString, (double)units);
                shopName = string.IsNullOrEmpty(shopName) ? "RP" : shopName;
            }
            else return;
            
            if (player.IsConnected)
            {
                string msg = GetMessage("ShoppyStockReward", player.UserIDString);
                if (string.IsNullOrEmpty(msg)) msg = $"Added {amount} {shopName} to your account.";
                else msg = string.Format(msg, amount, shopName);
                player.ChatMessage(msg);
            }
        }

        // =====================
        // Hackable Crate Handlers
        // =====================
        private void OnGuardedCrateEventEnded(BasePlayer player, HackableLockedCrate crate)
        {
            if (!config.LootDefender.Enabled || !config.LootDefender.HackableEnabled || crate == null || player == null) return;
            
            if (crate.OwnerID == 0 && CanLockHackableCrate(player, crate))
            {
                var owners = Pool.Get<HashSet<ulong>>();
                owners.Clear();
                owners.Add(player.userID);
                LD_LockEntity(crate, owners, config.LootDefender.HackableLockTime);
                Pool.FreeUnmanaged(ref owners);
                
                // Broadcast locked notification if configured
                if (config.LootDefender.HackableBroadcastLocked)
                {
                    BroadcastHackableNotification(player, crate, true);
                }
            }
        }

        private bool CanLockHackableCrate(BasePlayer player, HackableLockedCrate crate)
        {
            if (player == null || crate == null) return false;
            
            // Check harbor locking
            if (config.LootDefender.HackableLockHarbor)
            {
                // Check if crate is at harbor (would need position check)
                // For now, allow all if enabled
            }
            
            // Allow other plugins to override
            object hookResult = Interface.CallHook("OnLootLockedEntity", player, crate);
            return hookResult == null;
        }

        private void BroadcastHackableNotification(BasePlayer player, HackableLockedCrate crate, bool locked)
        {
            if (player == null || crate == null) return;
            
            // Check cooldown
            if (config.LootDefender.HackableNotifyCooldown > 0f)
            {
                if (_hackableNotifyCooldown.TryGetValue(player.userID, out var lastNotify))
                {
                    if ((DateTime.Now - lastNotify).TotalSeconds < config.LootDefender.HackableNotifyCooldown)
                    {
                        return; // Still on cooldown
                    }
                }
                _hackableNotifyCooldown[player.userID] = DateTime.Now;
            }
            
            string msg = locked ? "Crate locked to {0}" : "Crate at {1} is no longer locked to {0}";
            string position = PositionToGrid(crate.transform.position);
            string text = string.Format(msg, player.displayName, position);
            
            foreach (var p in BasePlayer.activePlayerList)
            {
                if (p != null && p.IsConnected)
                {
                    p.ChatMessage(text);
                }
            }
        }

        private object OnPlayerAttack(BasePlayer attacker, HitInfo info)
        {
            if (!config.LootDefender.Enabled || !config.LootDefender.HackableEnabled || attacker == null || info == null) return null;
            
            // Block timer increase on damage to laptop (bone 242862488 is laptop collision)
            if (config.LootDefender.HackableBlockLaptopDamage && info.HitBone == 242862488 && info.HitEntity is HackableLockedCrate crate)
            {
                // Check if crate is locked
                if (crate.net != null && _ldLocks.ContainsKey(crate.net.ID.Value))
                {
                    // Block damage to laptop if thresholds are set (or if BlockLaptopDamage is true regardless)
                    // Fix: Block when BlockLaptopDamage is true OR when thresholds are > 0
                    if (config.LootDefender.HackableBlockLaptopDamage || 
                        (config.LootDefender.BradleyThreshold > 0f || config.LootDefender.HeliThreshold > 0f))
                    {
                        info.HitBone = 0; // Clear hit bone to prevent damage
                        return null;
                    }
                }
            }
            
            return null;
        }

        private string PositionToGrid(Vector3 position)
        {
            // Simple grid conversion (A1, B2, etc.)
            int gridX = Mathf.FloorToInt((position.x + World.Size / 2) / 146.3f);
            int gridZ = Mathf.FloorToInt((position.z + World.Size / 2) / 146.3f);
            char letter = (char)('A' + Mathf.Clamp(gridX, 0, 25));
            int number = Mathf.Clamp(gridZ + 1, 1, 26);
            return $"{letter}{number}";
        }

        // =====================
        // Command Handlers
        // =====================
        private void CommandLockoutUI(IPlayer player, string command, string[] args)
        {
            if (player == null || !player.IsConnected) return;
            BasePlayer basePlayer = player.Object as BasePlayer;
            if (basePlayer == null) return;
            
            var settings = LDUIClass.GetSettings(this, basePlayer.UserIDString);
            settings.Enabled = !settings.Enabled;

            if (!settings.Enabled)
            {
                LDUIClass.DestroyLockoutUI(this, basePlayer);
                player.Reply("Lockout UI disabled");
            }
            else
            {
                LDUIClass.UpdateLockoutUI(this, basePlayer);
                player.Reply("Lockout UI enabled");
            }
        }

        private void CommandLockouts(IPlayer player, string command, string[] args)
        {
            if (player == null || !player.IsConnected) return;
            BasePlayer basePlayer = player.Object as BasePlayer;
            if (basePlayer == null) return;
            
            string playerId = basePlayer.UserIDString;
            if (!_lockouts.TryGetValue(playerId, out var lockout) || !lockout.Any())
            {
                player.Reply(GetMessage("NoLockouts", playerId));
                return;
            }
            
            double currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var messages = new List<string>();
            
            if (lockout.Bradley > currentTime && config.LootDefender.LockoutBradleyMinutes > 0)
            {
                double minutes = (lockout.Bradley - currentTime) / 60.0;
                messages.Add($"Bradley: {Math.Ceiling(minutes)}m");
            }
            
            if (lockout.Heli > currentTime && config.LootDefender.LockoutHeliMinutes > 0)
            {
                double minutes = (lockout.Heli - currentTime) / 60.0;
                messages.Add($"Heli: {Math.Ceiling(minutes)}m");
            }
            
            if (messages.Count > 0)
            {
                player.Reply(string.Join(", ", messages));
            }
            else
            {
                player.Reply(GetMessage("NoLockouts", playerId));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string ConcatenateListOrDefault(List<string> list, string defaultValue)
        {
            return (list == null || list.Count == 0) ? defaultValue : string.Join(", ", list);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string ConcatenateRuleSetNames(List<RuleSet> sets)
        {
            if (sets == null || sets.Count == 0)
                return string.Empty;
            var sb = Pool.Get<StringBuilder>();
            for (int i = 0; i < sets.Count; i++)
            {
                sb.Append(sets[i].name);
                if (i < sets.Count - 1)
                {
                    sb.Append(", ");
                }
            }
            string text = sb.ToString();
            Pool.FreeUnmanaged(ref sb);
            return text;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DamageResult HandleMetabolismDamage(HitInfo info, BasePlayer victim, DamageType damageType, float damageAmount)
        {
            if (damageType != DamageType.Cold && damageType != DamageType.Heat)
            {
                return DamageResult.None;
            }

            if (victim == null || !victim.userID.IsSteamId())
            {
                return DamageResult.None;
            }

            float delta = victim.metabolism.timeSinceLastMetabolism;
            if (delta <= ConVar.Server.metabolismtick)
            {
                //delta = damageAmount / (normalized * multiplier);
                return DamageResult.None;
            }

            float expected;
            float temperature = victim.metabolism.temperature.value;
            float multiplier;
            float normalized;

            if (damageType == DamageType.Cold)
            {
                if (temperature >= 1f)
                {
                    return DamageResult.None;
                }

                multiplier = temperature < -20f ? 1f :
                             temperature < -10f ? 0.3f : 0.1f;

                normalized = (temperature - 1f) / -51f;
                if (normalized < 0f) normalized = 0f;
                else if (normalized > 1f) normalized = 1f;

                expected = normalized * delta * multiplier;
            }
            else // DamageType.Heat
            {
                if (temperature <= 60f)
                {
                    return DamageResult.None;
                }

                normalized = (temperature - 60f) / 140f;
                if (normalized < 0f) normalized = 0f;
                else if (normalized > 1f) normalized = 1f;

                expected = normalized * delta * 5f;
            }

            float tolerance = expected * 0.0005f; 
            if (tolerance < 0.0005f) tolerance = 0.0005f;
            float diff = damageAmount - expected; 
            if (diff < 0f) diff = -diff;
            if (diff > tolerance) return DamageResult.None;

            bool option = damageType == DamageType.Cold ? config.options.Cold : config.options.Heat;
            DamageResult damageResult = option ? DamageResult.Allow : DamageResult.Block;

            if (trace)
            {
                string action = damageResult == DamageResult.Allow ? "allow and return" : "block and return";
                Trace($"Initiator is {damageType} metabolism damage; {action}", 1);
                LogTrace();
            }

            if (damageResult == DamageResult.Block)
            {
                info.damageTypes.Clear();
            }

            return damageResult;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CanPlayerBeHurtFromMonumentTopology(BaseEntity weapon, Vector3 worldPos)
        {
            if (!(config.PlayersTriggerTraps && (weapon is BaseTrap or BaseDetector or GunTrap) || config.PlayersTriggerTurrets && (weapon is FlameTurret or AutoTurret)))
            {
                return false;
            }
            if (!_monumentTopologyTargets.TryGetValue(weapon.net.ID.Value, out bool value))
            {
                _monumentTopologyHurt[weapon.net.ID.Value] = value = (TerrainMeta.TopologyMap.GetTopology(worldPos, 5f) & (int)TerrainTopology.Enum.Monument) != 0;
                if (_monumentTopologyHurt.Count == 1) timer.Once(60f, _monumentTopologyHurt.Clear);
            }
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CanPlayerHurtTargetInMonumentTopology(BaseEntity entity, Vector3 worldPos)
        {
            if (!(config.PlayersHurtTraps && (entity is BaseTrap or BaseDetector or GunTrap)) && !(config.PlayersHurtTurrets && (entity is FlameTurret or AutoTurret)))
            {
                return false;
            }
            if (!_monumentTopologyTargets.TryGetValue(entity.net.ID.Value, out bool value))
            {
                _monumentTopologyTargets[entity.net.ID.Value] = value = (TerrainMeta.TopologyMap.GetTopology(worldPos, 5f) & (int)TerrainTopology.Enum.Monument) != 0;
                if (_monumentTopologyTargets.Count == 1) timer.Once(60f, _monumentTopologyTargets.Clear);
            }
            return value;
        }

        private Dictionary<ulong, bool> _monumentTopologyTargets = new(), _monumentTopologyHurt = new();

        private bool AllowKillingSleepers(BaseEntity entity, BaseEntity initiator)
        {
            if (entity is BasePlayer victim && victim.userID.IsSteamId() && victim.IsSleeping())
            {
                if (config.AllowKillingSleepersAuthorization.Enabled && initiator is BasePlayer attacker && AllowAuthorizationDamage(victim, attacker))
                {
                    return true;
                }
                if (config.AllowKillingSleepersIds.Count > 0 && initiator is BasePlayer attacker3 && attacker3.userID.IsSteamId() && config.AllowKillingSleepersIds.Contains(attacker3.userID))
                {
                    return true;
                }
                if (config.AllowKillingSleepersAlly && initiator is BasePlayer attacker2 && attacker2.userID.IsSteamId())
                {
                    return IsAlly(victim.userID, attacker2.userID);
                }
                return config.AllowKillingSleepers;
            }
            return false;
        }

        private bool AllowAuthorizationDamage(BasePlayer victim, BasePlayer attacker)
        {
            if (!attacker.userID.IsSteamId())
            {
                return false;
            }
            Tugboat tugboat = victim.GetParentEntity() as Tugboat;
            if (tugboat != null && IsAuthed(tugboat, attacker.userID))
            {
                return true;
            }
            BuildingPrivlidge priv = victim.GetBuildingPrivilege(true);
            if (priv != null && priv.authorizedPlayers.Contains(attacker.userID))
            {

                if (config.PreventLooting?.Enabled == true && config.PreventLooting.UseCupboardAuth)
                {
                    if (priv.OwnerID != victim.userID && !priv.authorizedPlayers.Contains(victim.userID))
                        return false;
                }
                return config.AllowKillingSleepersAuthorization.MeetsMinimumRequirements(priv);
            }
            return false;
        }

        private static bool IsAuthed(DroppedItem entity, BaseEntity attacker)
        {
            if (entity == null || entity.IsDestroyed) return false;
            BuildingPrivlidge priv = entity.GetBuildingPrivilege(entity.WorldSpaceBounds(), true);
            return priv != null && priv.authorizedPlayers.Contains(entity.DroppedBy);
        }

        private static bool IsAuthed(PlayerCorpse entity, BaseEntity attacker)
        {
            if (entity == null || entity.IsDestroyed) return false;
            BuildingPrivlidge priv = entity.GetBuildingPrivilege(entity.WorldSpaceBounds(), true);
            return priv != null && priv.authorizedPlayers.Contains(entity.playerSteamID);
        }

        private static bool IsAuthed(DecayEntity entity, BasePlayer attacker)
        {
            if (entity is LegacyShelter || entity is LegacyShelterDoor)
            {
                EntityPrivilege entityPriv = entity.GetEntityBuildingPrivilege();
                return entityPriv != null && entityPriv.authorizedPlayers.Contains(attacker.userID);
            }
            BuildingManager.Building building = entity.GetBuilding();
            if (building != null)
            {
                BuildingPrivlidge priv = building.GetDominatingBuildingPrivilege();
                if (priv != null)
                {
                    return priv.authorizedPlayers.Contains(attacker.userID);
                }
            }
            BuildingPrivlidge priv2 = entity.GetBuildingPrivilege(entity.WorldSpaceBounds(), true);
            return priv2 != null && priv2.authorizedPlayers.Contains(attacker.userID);
        }

        private static bool IsAuthed(Tugboat tugboat, ulong userid)
        {
            if (tugboat.children == null)
            {
                return false;
            }
            foreach (var child in tugboat.children)
            {
                VehiclePrivilege priv = child as VehiclePrivilege;
                if (priv != null && priv.authorizedPlayers.Contains(userid))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsAuthed(BaseHelicopter heli, BasePlayer attacker)
        {
            BuildingPrivlidge priv = attacker.GetBuildingPrivilege(heli.WorldSpaceBounds(), true);
            return priv != null && priv.authorizedPlayers.Contains(attacker.userID);
        }

        // determines if an entity is "allowed" to take damage
        private bool AllowDamage(BaseEntity entity, HitInfo info)
        {
            if (trace)
            {
                traceEntity = entity;
                _tsb.Length = 0;
            }

            var initiator = info.Initiator switch
            {
                BasePlayer player => player,
                { creatorEntity: BasePlayer player } => player,
                { parentEntity: EntityRef parentRef } when parentRef.Get(true) is BasePlayer player => player,
                _ => info.Initiator ?? info.WeaponPrefab
            };

            var victim = entity as BasePlayer;
            var attacker = initiator as BasePlayer;
            var isAttacker = attacker != null && !attacker.IsDestroyed;
            var isAtkId = isAttacker && attacker.userID.IsSteamId();
            var isVictim = victim != null && !victim.IsDestroyed;
            var isVicId = isVictim && victim.userID.IsSteamId();

            if (Interface.CallHook("CanEntityTakeDamage", new object[] { entity, info }) is bool val)
            {
                if (val && config.options.ArmorDamage.Enabled && isAttacker && !isAtkId && isVicId)
                {
                    HandleHitArea(victim, info);
                }
                return val;
            }

            var damageAmount = info.damageTypes.Total();

            if (damageAmount <= 0f)
            {
                return true;
            }

            if (config.laptop && info.HitBone == 242862488 && info.HitEntity is HackableLockedCrate) // laptopcollision
            {
                info.HitBone = 0;
                return false;
            }

            if (config.options.ArmorDamage.Enabled && isAttacker && !isAtkId && isVicId)
            {
                HandleHitArea(victim, info);
            }

            if (config.options.DeepSeaRaiding && !isVictim && (entity is BaseMountable || entity.OwnerID.IsSteamId()) && IsInDeepSea(entity.transform.position))
            {
                if (trace) Trace($"Initiator is {initiator}; target is {entity}; raiding in deep sea; allow and return", 1);
                return true;
            }

            if (_allowKillingSleepersEnabled && AllowKillingSleepers(entity, initiator))
            {
                return true;
            }

            var weapon = initiator ?? info.WeaponPrefab ?? info.Weapon;

            if (entity is BaseNpc || entity is BaseNPC2)
            {
                if (trace) Trace($"Target is animal; allow and return {weapon} -> {entity}", 1);
                return true;
            }

            if (entity.OwnerID != 0 && entity is Igniter)
            {
                if (config.igniter) info.damageTypes.Clear();
                return true;
            }

            if (weapon != null && IsSkinExclusion(weapon))
            {
                if (trace) Trace($"Target is {entity}; allow and return -> {weapon} skin ID {weapon.skinID}", 1);
                return true;
            }

            if (config.scrap)
            {
                if (victim != null && weapon is ScrapTransportHelicopter)
                {
                    victim.Teleport(weapon.transform.position + new Vector3(0f, 2.5f, 0f));
                    info.damageTypes.Clear();
                    return true;
                }
                if (weapon is BasePlayer driver && (driver.GetMountedVehicle() is ScrapTransportHelicopter || info.WeaponPrefab is ScrapTransportHelicopter))
                {
                    victim.Teleport(weapon.transform.position + new Vector3(0f, 2.5f, 0f));
                    info.damageTypes.Clear();
                    return true;
                }
            }

            // 2.4.3 added this option (default false) but never read it in AllowDamage; honor it here.
            if (config.lift && victim != null && weapon is ElevatorLift)
            {
                victim.Teleport(weapon.transform.position + new Vector3(0f, 0.5f, 0f));
                info.damageTypes.Clear();
                return true;
            }

            // allow damage to door barricades and covers 
            if (entity.prefabID == trainbarricade || entity.prefabID == trainbarricadeheavy || (entity is Barricade && (entity.ShortPrefabName.Contains("door_barricade") || entity.ShortPrefabName.Contains("cover"))))
            {
                if (trace) Trace($"Target is {entity.ShortPrefabName}; allow and return", 1);
                return true;
            }

            // if entity is a barrel, trash can, or giftbox, allow damage (exclude waterbarrel and hobobarrel)
            if (entity is LootContainer && (entity.prefabID == giftbox_loot || entity.prefabID == loot_trash || entity.ShortPrefabName.Contains("barrel")))
            {
                if (trace) Trace($"Target is {entity.ShortPrefabName} ({GetTypeName(entity)}); allow and return", 1);
                return true;
            }

            var damageType = info.damageTypes.GetMajorityDamageType();

            if (damageType == DamageType.Fall || damageType == DamageType.Radiation)
            {
                return true;
            }

            if (damageType == DamageType.Decay)
            {
                if (entity is BaseVehicle v)
                {
                    return entity is HitchTrough.IHitchable && v.healthFraction > 0.9f ? true : !config.BlockDecayDamageToVehicles;
                }
                return true;
            }

            if (damageAmount < 15f && !isAttacker && HandleMetabolismDamage(info, victim, damageType, damageAmount) != DamageResult.None)
            {
                return true;
            }

            if (trace)
            {
                // Sometimes the initiator is not the attacker (turrets)
                Trace("======================" + Environment.NewLine +
                  "==  STARTING TRACE  ==" + Environment.NewLine +
                  "==  " + DateTime.Now.ToString("HH:mm:ss.fffff") + "  ==" + Environment.NewLine +
                  "======================");
                //string weaponid = $"{(weapon != null ? weapon.OwnerID : (info.Initiator != null ? $"initiator '{info.Initiator.OwnerID}' & creator: '{info.Initiator.creatorEntity?.OwnerID ?? 0}'" : 0))}";
                //string weaponce = $"{(weapon != null ? weapon.creatorEntity : string.Empty)}";
                //string weaponpr = $"{(weapon != null ? weapon.ShortPrefabName : (info.Initiator != null ? $"initiator '{info.Initiator}' & creator: '{info.Initiator.creatorEntity}'" : "Unknown_Prefab"))}";
                //Trace($"From: {GetTypeName(weapon, "Unknown_Weapon")}, {weaponpr} {weaponce} {weaponid}", 1);
                Trace($"From: {GetTypeName(weapon, "Unknown_Weapon")}, {weapon?.ShortPrefabName ?? "Unknown_Prefab"}", 1);
                Trace($"To: {GetTypeName(entity)}, {entity.ShortPrefabName}", 1);
            }

            var ruleSet = currentRuleSet;
            if (useZones)
            {
                // get entity and initiator locations (zones)
                using var entityLocations = GetLocationKeys(entity);
                using var initiatorLocations = GetLocationKeys(weapon);
                // check for exclusion zones (zones with no rules mapped)
                if (CheckExclusion(entityLocations, initiatorLocations, trace))
                {
                    if (trace) Trace("Exclusion found; allow and return", 1);
                    return true;
                }
                ruleSet = GetRuleSet(entityLocations, initiatorLocations);
            }

            // Harmony loads with hooks subscribed by default; currentRuleSet is null until OnServerInitialized.
            if (ruleSet == null)
            {
                return true;
            }

            if (trace) Trace("No exclusion found - looking up RuleSet...", 1);

            // process location rules
            RuleFlags _flags = ruleSet._flags;

            if (trace) Trace($"Using RuleSet \"{ruleSet.name}\"", 1);

            var selfDamageFlag = (_flags & RuleFlags.SelfDamage) != 0;
            var mountRulesEvaluated = false;

            if (isVicId)
            {
                if (isAtkId)
                {
                    // allow damage to players by admins if configured
                    if (attacker.IsAdmin && (_flags & RuleFlags.AdminsHurtPlayers) != 0)
                    {
                        if (trace) Trace("Initiator is admin player and target is player, with AdminsHurtPlayers flag set; allow and return", 1);
                        return true;
                    }

                    // allow sleeper damage by admins if configured
                    if (attacker.IsAdmin && (_flags & RuleFlags.AdminsHurtSleepers) != 0 && victim.IsSleeping())
                    {
                        if (trace) Trace("Initiator is admin player and target is sleeping player, with AdminsHurtSleepers flag set; allow and return", 1);
                        return true;
                    }

                    if ((_flags & RuleFlags.FriendlyFire) != 0 && victim.userID != attacker.userID && IsAlly(victim, attacker))
                    {
                        if (trace) Trace("Initiator and target are allied players, with FriendlyFire flag set; allow and return", 1);
                        return true;
                    }

                    if (_canKillOfflinePlayerEnabled && CanKillOfflinePlayer(victim, out _))
                    {
                        if (trace) Trace($"Initiator ({attacker}) and target ({victim} exceeds Allow Killing Sleepers offline time); allow and return", 1);
                        return true;
                    }

                    if (PlayerHasExclusion(attacker, info.PointStart) && PlayerHasExclusion(victim, info.HitPositionWorld))
                    {
                        if (trace) Trace($"Initiator ({attacker}) and target ({victim}) meet exclusion conditions; allow and return", 1);
                        return true;
                    }

                    if (config.options.Apartments.Enabled && victim.HasPlayerFlag(BasePlayer.PlayerFlags.CombatZone))
                    {
                        if (attacker.userID == victim.userID) return selfDamageFlag;
                        if (config.options.Apartments.Alive && !attacker.IsAlive()) return false;
                        if (attacker.HasPlayerFlag(BasePlayer.PlayerFlags.CombatZone))
                        {
                            if (!config.options.Apartments.PVP) return false;
                            if (!config.options.Apartments.SameRoom) return true;
                            return TryGetApartmentRoom(attacker, out var room) && room.IsInsideRoom(victim);
                        }
                    }

                    if (_pvpReflectionEnabled && victim.userID != attacker.userID)
                    {
                        float multiplier = damageType != DamageType.Explosion && info.WeaponPrefab is TimedExplosive ? config.options.Reflect.Get(DamageType.Explosion) : config.options.Reflect.Get(damageType);
                        if (multiplier != 0 && !IsAlly(victim, attacker))
                        {
                            float reflectedDamage = damageAmount * multiplier;
                            DamageType reflectType = selfDamageFlag ? damageType : DamageType.Radiation;
                            if (trace) Trace($"Reflect damage ({reflectedDamage} {reflectType})", 1);
                            attacker.Hurt(reflectedDamage, reflectType, attacker, config.options.Reflect.Protection);
                        }
                    }

                    mountRulesEvaluated = true;
                    var mounted = attacker.GetMounted();
                    if (mounted != null)
                    {
                        var parent = GetParentEntity(mounted);
                        if (parent != null && EvaluateRules(entity, parent, ruleSet, false) == DamageResult.Block)
                        {
                            if (trace) Trace($"Player is mounted; evaluation? block and return", 1);
                            return false;
                        }
                    }
                }

                if (config.options.UnderworldOther > -500f && (!isAttacker || !attacker.userID.IsSteamId()) && info.HitPositionWorld.y <= config.options.UnderworldOther && info.PointStart.y <= config.options.UnderworldOther)
                {
                    if (trace) Trace($"Initiator is {weapon} under world; Target is player; allow and return", 1);
                    return true;
                }

                if (config.options.AboveworldOther < 5000f && (!isAttacker || !attacker.userID.IsSteamId()) && info.HitPositionWorld.y >= config.options.AboveworldOther && info.PointStart.y >= config.options.AboveworldOther)
                {
                    if (trace) Trace($"Initiator is {weapon} above world; Target is player; allow and return", 1);
                    return true;
                }

                if (_playersTriggerOption && weapon != null && weapon.net != null && weapon.OwnerID == 0uL && CanPlayerBeHurtFromMonumentTopology(weapon, info.PointStart))
                {
                    if (trace) Trace($"Initiator is turret or trap in monument topology; Target is player; allow and return", 1);
                    return true;
                }
            }

            if (_playersHurtOption && isAtkId && entity.OwnerID == 0uL && entity.net != null && CanPlayerHurtTargetInMonumentTopology(entity, info.HitPositionWorld))
            {
                if (trace) Trace($"Initiator is player; Target is turret or trap in monument topology; allow and return", 1);
                return true;
            }

            // LockedVehiclesImmortal flag with modular car
            // CarLock is created during ModularCar.ServerInit and can be null on early collision damage.
            if (((_flags & RuleFlags.LockedVehiclesImmortal) != 0) && entity.PrefabName != null && entity.PrefabName.Contains("modular"))
            {
                ModularCar car = entity.HasParent() ? entity.GetParentEntity() as ModularCar : entity as ModularCar;
                if (car != null && car.CarLock != null && car.CarLock.HasALock)
                {
                    if (trace) Trace($"Initiator is {weapon}; Target is locked {car}; block and return (LockedVehiclesImmortal)", 1);
                    return false;
                }
            }

            if (isVictim)
            {
                // Game update moved lastAdminCheatTime onto AntiHack.PlayerStates (TruePVE 2.4.3).
                // Isolated so TypeLoadException on older Assembly-CSharp (no AntiHack.PlayerState)
                // cannot poison OnEntityTakeDamage / AllowDamage JIT on mismatched server builds.
                if (config.PreventRagdolling && isVicId && damageType == DamageType.Collision
                    && victim != null && victim.ActivePlayerInd != -1)
                {
                    TryPreventCollisionRagdoll(victim);
                }

                double secondsLeft = 0;

                if (isAtkId && isVicId)
                {
                    if (_canKillOfflinePlayerEnabled && CanKillOfflinePlayer(victim, out secondsLeft))
                    {
                        if (trace) Trace($"Initiator ({attacker}) and target ({victim} exceeds Allow Killing Sleepers offline time); allow and return", 1);
                        return true;
                    }

                    if (!useZones)
                    {
                        if (PlayerHasExclusion(attacker, info.PointStart) && PlayerHasExclusion(victim, info.HitPositionWorld))
                        {
                            if (trace) Trace($"Initiator ({attacker}) and target ({victim}) meet exclusion conditions; allow and return", 1);
                            return true;
                        }
                    }
                }

                if (!isAtkId && isVicId && config.options.UnderworldOther > -500f && info.HitPositionWorld.y <= config.options.UnderworldOther)
                {
                    if (trace) Trace($"Initiator is {weapon} under world; Target is player; allow and return", 1);
                    return true;
                }

                if (!isAtkId && isVicId && config.options.AboveworldOther < 5000f && info.HitPositionWorld.y >= config.options.AboveworldOther)
                {
                    if (trace) Trace($"Initiator is {weapon} above world; Target is player; allow and return", 1);
                    return true;
                }

                if (isAtkId && secondsLeft > 0 && damageType != DamageType.Heat)
                {
                    ulong userid = attacker.userID;
                    double now = Time.timeAsDouble;

                    if (_waiting.Count > 10) _waiting.Clear();

                    if (!_waiting.TryGetValue(userid, out var time) || time <= now)
                    {
                        _waiting[userid] = now + 1;
                        TimeSpan t = TimeSpan.FromSeconds(secondsLeft);
                        Message(attacker, "Error_OfflineTimeLeft", t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m {t.Seconds}s" : t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds}s" : $"{t.Seconds}s");
                    }
                }
            }

            if (entity is PatrolHelicopter)
            {
                if (isAttacker || weapon is PatrolHelicopter)
                {
                    bool isBlocked = EvaluateRules(entity, weapon, ruleSet, false) == DamageResult.Block;
                    if (trace)
                    {
                        string action = isBlocked ? "block and return" : "allow and return";
                        Trace($"Target is PatrolHelicopter; Initiator is {attacker}; {action}", 1);
                    }
                    return !isBlocked;
                }
                if (trace) Trace($"Target is PatrolHelicopter; Initiator is {GetTypeName(weapon)}; allow and return", 1);
                return true;
            }

            if (weapon != null && (weapon is BradleyAPC || weapon.prefabID == maincannonshell))
            {
                if (trace) Trace("Initiator is BradleyAPC; evaluating RuleSet rules...", 1);
                return EvaluateRules(entity, weapon, ruleSet) != DamageResult.Block;
            }

            if ((_flags & RuleFlags.VehiclesTakeCollisionDamageWithoutDriver) != 0 && entity is BaseVehicle vehicle && weapon == vehicle && !vehicle.GetDriver())
            {
                if (trace) Trace($"VehiclesTakeCollisionDamageWithoutDriver; allow and return", 1);
                return true;
            }

            if ((_flags & RuleFlags.VehiclesTakeCollisionDamage) != 0 && entity is BaseVehicle vehicle2 && weapon == vehicle2)
            {
                if (trace) Trace($"VehiclesTakeCollisionDamage: allow and return", 1);
                return true;
            }

            // check heli and turret
            DamageResult heli = CheckHeliInitiator(ruleSet, initiator, info.WeaponPrefab);

            if (config.Firework && entity is BaseFirework)
            {
                if (trace)
                {
                    string action = heli == DamageResult.None ? "allow and return" : "block and return";
                    Trace($"Target is firework; {action}", 1);
                }
                return heli == DamageResult.None;
            }

            if (heli != DamageResult.None)
            {
                DamageResult immortalFlag = CheckImmortalFlag(entity, ruleSet, initiator, info.WeaponPrefab);
                if (immortalFlag != DamageResult.None)
                {
                    return immortalFlag == DamageResult.Allow;
                }
                return HandleHelicopter(ruleSet, entity, weapon, victim, isVicId, heli == DamageResult.Allow);
            }

            if ((_flags & RuleFlags.NoMLRSDamage) != 0 && info.WeaponPrefab is MLRSRocket)
            {
                if (trace) Trace("Initiator is MLRS rocket with NoMLRSDamage set; block and return", 1);
                return false;
            }

            // after heli check, return true if initiator is null
            if (initiator == null)
            {
                if (entity is ParachuteUnpacked)
                {
                    if (trace) Trace("Initiator is server hurting parachute; allow and return", 1);
                    return true;
                }
                if (weapon is MLRSRocket)
                {
                    if (trace) Trace($"Initiator empty for MLRS Rocket; block and return", 1);
                    return false;
                }
                if ((damageType == DamageType.Slash || damageType == DamageType.Stab || damageType == DamageType.Cold) && isVictim &&
                   (!(victim.lastAttacker is BasePlayer lastAttacker) || !lastAttacker.userID.IsSteamId() || lastAttacker == entity))
                {
                    if (trace) Trace("Initiator is hurt trigger; allow and return", 1);
                    return true;
                }
                if (entity is FarmableAnimal && damageType == DamageType.Generic)
                {
                    if (trace) Trace($"Initiator is thirst or hunger; {(config.options.FarmableMetabolism ? "allow and return" : "block and return")}", 1);
                    return config.options.FarmableMetabolism;
                }
                foreach (DamageType _damageType in _damageTypes)
                {
                    if (info.damageTypes.Has(_damageType))
                    {
                        if ((entity is NPCPlayerCorpse or NPCPlayer) || (entity is BaseCorpse and not PlayerCorpse))
                        {
                            if (trace) _tsb.Clear(); // reduce useless spam
                            return true;
                        }
                        bool tut = IsTutorialNetworkGroup(entity);
                        if (trace)
                        {
                            string action = tut ? "allow and return (Tutorial Zone)" : ruleSet.defaultAllowDamage ? "allow and return" : "block and return";
                            Trace($"Initiator empty for player damage; {action} (Damage Type: {damageType}, Damage Amount: {damageAmount})", 1);
                        }
                        return tut || ruleSet.defaultAllowDamage;
                    }
                }
                if (trace) Trace($"Initiator empty; allow and return {damageType} {damageAmount}", 1);
                return true;
            }

            DamageResult immortalFlag2 = CheckImmortalFlag(entity, ruleSet, initiator, info.WeaponPrefab);
            if (immortalFlag2 != DamageResult.None)
            {
                return immortalFlag2 == DamageResult.Allow;
            }

            if (initiator is SamSite ss && (isVictim || entity is BaseMountable))
            {
                if (CheckExclusion(ss))
                {
                    if (trace) Trace($"Initiator is samsite, and target is player; exclusion found; allow and return", 1);
                    return true;
                }

                bool isAllowed = ss.staticRespawn
                                 ? ((_flags & RuleFlags.StaticSamSitesIgnorePlayers) == 0)
                                 : ((_flags & RuleFlags.PlayerSamSitesIgnorePlayers) == 0);
                if (trace)
                {
                    string action = isAllowed ? "flag not set; allow and return" : "flag set; block and return";
                    Trace($"Initiator is samsite, and target is player; {action}", 1);
                }
                return isAllowed;
            }

            if ((isAttacker && !isAtkId) || (initiator is BaseNpc or BaseNPC2 or BeeSwarmAI))
            {
                if (isVictim && (_flags & RuleFlags.ProtectedSleepers) != 0 && victim.IsSleeping())
                {
                    if (trace) Trace("Target is sleeping player, with ProtectedSleepers flag set; block and return", 1);
                    return false;
                }

                if ((_flags & RuleFlags.NpcsCanHurtAnything) != 0)
                {
                    if (trace) Trace("Initiator is NPC; flag set; allow damage and return", 1);
                    return true;
                }
            }

            if (isVictim)
            {
                if (isVicId && initiator is AutoTurret)
                {
                    if (initiator.OwnerID == 0)
                    {
                        if (initiator is NPCAutoTurret)
                        {
                            bool safezoneFlag = (_flags & RuleFlags.SafeZoneTurretsIgnorePlayers) == 0;
                            if (trace)
                            {
                                string action = safezoneFlag ? "allow and return" : "block and return";
                                Trace($"Initiator is npc turret; Target is player; {action}", 1);
                            }
                            return safezoneFlag;
                        }
                        bool staticFlag = (_flags & RuleFlags.StaticTurretsIgnorePlayers) == 0;
                        if (trace)
                        {
                            string action = staticFlag ? "allow and return" : "block and return";
                            Trace($"Initiator is static turret; Target is player; {action}", 1);
                        }
                        return staticFlag;
                    }
                    if (initiator.OwnerID.IsSteamId() && (_flags & RuleFlags.TurretsIgnorePlayers) != 0)
                    {
                        if (trace) Trace($"Initiator is RC turret; Target is player; block and return", 1);
                        return false;
                    }
                }

                // handle suicide
                if (isVicId && damageType == DamageType.Suicide)
                {
                    bool isBlocked = (_flags & RuleFlags.SuicideBlocked) != 0;
                    if (trace)
                    {
                        string action = isBlocked ? "block and return" : "allow and return";
                        Trace($"DamageType is suicide; {action}", 1);
                    }
                    if (isBlocked) Message(victim, "Error_NoSuicide");
                    return !isBlocked;
                }

                // allow players to inflict self damage
                if (selfDamageFlag && isVicId && isAtkId && attacker.userID == victim.userID)
                {
                    if (trace) Trace($"SelfDamage flag; player inflicted damage to self; allow and return", 1);
                    return true;
                }
            }

            if (isAttacker)
            {
                if (isAtkId && !mountRulesEvaluated)
                {
                    var mounted = attacker.GetMounted();
                    if (mounted != null)
                    {
                        var parent = GetParentEntity(mounted);
                        if (parent != null && EvaluateRules(entity, parent, ruleSet, false) == DamageResult.Block)
                        {
                            if (trace) Trace($"Player is mounted; evaluation? block and return", 1);
                            return false;
                        }
                    }
                }

                if (isAtkId && entity is BuildingBlock block && block.OwnerID != 0)
                {
                    if (!mountRulesEvaluated && attacker.GetMounted() is Minicopter mini && EvaluateRules(block, mini, ruleSet, false) == DamageResult.Block)
                    {
                        if (trace) Trace("Initiator is player in minicopter, target is building; block and return", 1);
                        return false;
                    }

                    if (block.grade == BuildingGrade.Enum.Twigs && (_flags & RuleFlags.TwigDamage) != 0)
                    {
                        bool isAllowed = ShouldAllowBuildingBlockDamage(block, attacker, (_flags & RuleFlags.TwigDamageRequiresOwnership) != 0, blockAllyDamage: false, blockWhenOnline: false, checkAndAllowWhenAuthed: config.options.BlockHandler.CheckAndAllowWhenAuthed);
                        if (!isAllowed && _buildingBlockHandlerEnabled) HandleBlockOutput(block, damageType, damageAmount, attacker, selfDamageFlag);
                        if (trace) Trace($"Initiator is player and target is twig block, with TwigDamage flag set; {(isAllowed ? "allow" : "block")} and return", 1);
                        return isAllowed;
                    }

                    if (block.grade == BuildingGrade.Enum.Wood && (_flags & RuleFlags.WoodenDamage) != 0)
                    {
                        bool isAllowed = ShouldAllowBuildingBlockDamage(block, attacker, (_flags & RuleFlags.WoodenDamageRequiresOwnership) != 0, blockAllyDamage: false, blockWhenOnline: false, checkAndAllowWhenAuthed: config.options.BlockHandler.CheckAndAllowWhenAuthed);
                        if (!isAllowed && _buildingBlockHandlerEnabled) HandleBlockOutput(block, damageType, damageAmount, attacker, selfDamageFlag);
                        if (trace) Trace($"Initiator is player and target is wooden block, with WoodenDamage flag set; {(isAllowed ? "allow" : "block")} and return", 1);
                        return isAllowed;
                    }

                    if (_buildingBlockHandlerEnabled && config.options.BlockHandler.CanHandleGrade(block.grade, _flags))
                    {
                        DamageResult result = HandleBuildingBlockByGrade(block, attacker, damageType, damageAmount, selfDamageFlag);
                        if (result != DamageResult.None)
                        {
                            if (trace) Trace($"Initiator is player and target is {block.grade} block, with damage option {(result == DamageResult.Allow ? "enabled" : "disabled")}; {(result == DamageResult.Allow ? "allow" : "block")} and return", 1);
                            return result == DamageResult.Allow;
                        }
                    }
                }

                if ((_flags & RuleFlags.NoPlayerDamageToMini) != 0 && entity is Minicopter)
                {
                    if (trace) Trace("Initiator is player and target is Minicopter, with NoPlayerDamageToMini flag set; block and return", 1);
                    return false;
                }

                if ((_flags & RuleFlags.NoPlayerDamageToScrap) != 0 && entity is ScrapTransportHelicopter)
                {
                    if (trace) Trace("Initiator is player and target is ScrapTransportHelicopter, with NoPlayerDamageToScrap flag set; block and return", 1);
                    return false;
                }

                if ((_flags & RuleFlags.NoPlayerDamageToCar) != 0 && entity.PrefabName.Contains("modularcar"))
                {
                    if (trace) Trace("Initiator is player and target is ModularCar, with NoPlayerDamageToCar flag set; block and return", 1);
                    return false;
                }

                if (entity.OwnerID == 0 && entity is ChristmasLights)
                {
                    if (trace) Trace($"Entity is christmas lights; block and return", 1);
                    return false;
                }

                if (entity is GrowableEntity)
                {
                    bool isAllowed = !(entity.GetParentEntity() is PlanterBox planter) || IsAlly(attacker, planter.OwnerID);
                    if (trace)
                    {
                        string action = isAllowed ? "allow ally" : "block non-ally";
                        Trace($"Entity is growable entity; {action} and return", 1);
                    }
                    return isAllowed;
                }

                if (config.SleepingBags && entity is SleepingBag)
                {
                    if (trace) Trace("Initiator is player and target is sleeping bag; allow and return", 1);
                    return true;
                }

                if (config.Campfires && entity.prefabID == campfire)
                {
                    if (trace) Trace("Initiator is player and target is campfire; allow and return", 1);
                    return true;
                }

                if (config.Ladders && entity is BaseLadder)
                {
                    if (trace) Trace("Initiator is player and target is ladder; allow and return", 1);
                    return true;
                }

                if (isVictim)
                {
                    // allow Human NPC damage if configured
                    if ((_flags & RuleFlags.HumanNPCDamage) != 0 && (!isAtkId || !isVicId))
                    {
                        if (trace) Trace("Initiator or target is HumanNPC, with HumanNPCDamage flag set; allow and return", 1);
                        return true;
                    }
                }
                else if ((_flags & RuleFlags.AuthorizedFarmableDamage) != 0 && isAtkId && entity is FarmableAnimal)
                {
                    var parent = entity.GetParentEntity() as ChickenCoop;
                    bool isAllowed = parent == null || parent.OwnerID == 0 || IsAlly(attacker, parent.OwnerID) || ((_flags & RuleFlags.CupboardOwnership) != 0 && CheckCupboardOwnership(parent, attacker));
                    if (trace) Trace($"Initiator is player {(isAllowed ? "with farm authorization; allow and return" : "without farm authorization; block and return")}", 1);
                    return isAllowed;
                }
                else if ((_flags & RuleFlags.AuthorizedDamage) != 0 && !isVictim && !entity.IsNpc && isAtkId && !(entity is FarmableAnimal))
                { // ignore checks if authorized damage enabled (except for players and npcs)
                    if ((_flags & RuleFlags.AuthorizedDamageCheckPrivilege) != 0)
                    {
                        if (entity is DecayEntity decayEntity && IsAuthed(decayEntity, attacker))
                        {
                            if (trace) Trace("Initiator is player with building priv over target; allow and return", 1);
                            return true;
                        }
                        if (entity is BaseHelicopter playerHelicopter && !(entity is PatrolHelicopter) && IsAuthed(playerHelicopter, attacker))
                        {
                            if (trace) Trace("Initiator is player with heli priv over target; allow and return", 1);
                            return true;
                        }
                        if (entity is Tugboat tugboat && IsAuthed(tugboat, attacker.userID))
                        {
                            if (trace) Trace("Initiator is player with tugboat priv over target; allow and return", 1);
                            return true;
                        }
                        if (entity.HasParent() && entity.GetParentEntity() is Tugboat tugboat2 && IsAuthed(tugboat2, attacker.userID))
                        {
                            if (trace) Trace("Initiator is player with tugboat priv over target; allow and return", 1);
                            return true;
                        }
                    }

                    if ((_flags & RuleFlags.AuthorizedDamageRequiresOwnership) != 0 && !IsAlly(attacker, entity.OwnerID) && CanAuthorize(entity, attacker, ruleSet))
                    {
                        if (trace) Trace("Initiator is player who does not own the target; block and return", 1);
                        return false;
                    }

                    bool cupboardOwnership = (_flags & RuleFlags.CupboardOwnership) != 0;

                    if (CheckAuthorized(entity, attacker, ruleSet, cupboardOwnership))
                    {
                        if (entity is SamSite || entity is BaseMountable || entity.PrefabName.Contains("modular"))
                        {
                            if (trace) Trace($"Target is {entity.ShortPrefabName}; evaluate and return", 1);
                            return EvaluateRules(entity, attacker, ruleSet) != DamageResult.Block;
                        }
                        if (trace) Trace("Initiator is player with authorization over target; allow and return", 1);
                        return true;
                    }

                    if (cupboardOwnership)
                    {
                        if (trace) Trace("Initiator is player without authorization over target; block and return", 1);
                        return false;
                    }
                }
            }

            if (trace) Trace("No match in pre-checks; evaluating RuleSet rules...", 1);
            return EvaluateRules(entity, weapon, ruleSet) != DamageResult.Block;
        }

        private void HandleHitArea(BasePlayer victim, HitInfo info)
        {
            if (victim == null || victim.inventory == null || victim.inventory.containerWear == null || victim.inventory.containerWear.itemList == null)
            {
                return;
            }
            float relative = (info.HitPositionWorld.y - victim.transform.position.y) / (victim.IsDucked() ? 1.1f : 1.8f);
            HitArea area = victim.IsDucked() switch
            {
                true => relative switch
                {
                    <= 0.07f => HitArea.Foot,
                    >= 0.85f => HitArea.Head,
                    >= 0.65f => HitArea.Chest,
                    >= 0.45f => HitArea.Stomach,
                    _ => HitArea.Leg,
                },
                false => relative switch
                {
                    <= 0.07f => HitArea.Foot,
                    >= 0.8f => HitArea.Head,
                    >= 0.7f => HitArea.Chest,
                    >= 0.5f => HitArea.Stomach,
                    _ => HitArea.Leg,
                }
            };
            if (victim.inventory.containerWear.itemList.Count > 0)
            {
                using var obj = Facepunch.Pool.Get<PooledList<Item>>();
                obj.AddRange(victim.inventory.containerWear.itemList);
                bool serverUpdate = false;
                for (int i = 0; i < obj.Count; i++)
                {
                    Item item = obj[i];
                    if (item != null && !item.isBroken)
                    {
                        if (config.options.ArmorDamage.ImmuneSkins.Contains(item.skin))
                        {
                            info.HitBone = 0u; // prevent immune skins from taking damage from other plugins
                            continue;
                        }
                        ItemModWearable wearable = item.info.ItemModWearable;
                        if (wearable != null && wearable.ProtectsArea(area))
                        {
                            item.OnAttacked(info);
                            serverUpdate = true;
                        }
                    }
                }
                if (serverUpdate)
                {
                    info.HitBone = 0u; // prevent double armor damage
                    victim.inventory.ServerUpdate(0f);
                }
            }
            if (config.options.ArmorDamage.Headshots && area == HitArea.Head)
            {
                Effect.server.Run("assets/bundled/prefabs/fx/headshot.prefab", victim, 0u, new Vector3(0f, 2f, 0f), Vector3.zero, null);
            }
        }

        private static BaseEntity GetParentEntity(BaseEntity m)
        {
            int n = 0;
            while (m != null && m.HasParent() && ++n < 30)
            {
                if (!(m.GetParentEntity() is BaseEntity parent)) break;
                m = parent;
            }

            return m;
        }

        private bool ShouldAllowBuildingBlockDamage(BuildingBlock block, BasePlayer attacker, bool requiresOwnerOrAuthed, bool blockAllyDamage, bool blockWhenOnline, bool checkAndAllowWhenAuthed)
        {
            if (IsOwner(block.OwnerID, attacker.userID)) return true;
            if (IsAlly(attacker, block.OwnerID)) return !blockAllyDamage;
            bool isAuthed = false;
            if (checkAndAllowWhenAuthed || requiresOwnerOrAuthed) isAuthed = IsAuthed(block, attacker);
            if (checkAndAllowWhenAuthed && isAuthed) return true;
            if (requiresOwnerOrAuthed && !isAuthed) return false;
            if (blockWhenOnline && BasePlayer.FindByID(block.OwnerID) != null) return false;
            return true;
        }

        private DamageResult HandleBuildingBlockByGrade(BuildingBlock block, BasePlayer attacker, DamageType damageType, float damageAmount, bool selfDamageFlag)
        {
            TwigDamageOptions opt = config.options.BlockHandler;
            if (!ShouldAllowBuildingBlockDamage(block, attacker, requiresOwnerOrAuthed: false, blockAllyDamage: opt.BlockAllyDamage, blockWhenOnline: opt.BlockWhenOnline, checkAndAllowWhenAuthed: opt.CheckAndAllowWhenAuthed))
            {
                HandleBlockOutput(block, damageType, damageAmount, attacker, selfDamageFlag);
                return DamageResult.Block;
            }

            return DamageResult.Allow;
        }

        private void HandleBlockOutput(BuildingBlock block, DamageType damageType, float damageAmount, BasePlayer attacker, bool selfDamageFlag)
        {
            if (config.options.BlockHandler.Log)
            {
                string grade = block.grade.ToString();
                string ownerDisplayName = BasePlayer.FindAwakeOrSleepingByID(block.OwnerID) is BasePlayer owner ? owner.displayName : "Unknown Owner";
                Puts($"{grade} Damage: Attacker - {attacker.displayName} ({attacker.userID}) | {grade} Owner: {ownerDisplayName} ({block.OwnerID}) at Location: {block.transform.position} | Damage Amount: {damageAmount}");
            }

            if (config.options.BlockHandler.Notify)
            {
                SendReply(attacker, GetMessage("Twig", attacker.UserIDString));
            }

            if (config.options.BlockHandler.ReflectDamageMultiplier > 0f)
            {
                float reflectedDamage = damageAmount * config.options.BlockHandler.ReflectDamageMultiplier;

                if (!selfDamageFlag)
                {
                    damageType = DamageType.Radiation;
                }

                bool t = trace;
                trace = false;
                attacker.Hurt(reflectedDamage, damageType, attacker, config.options.BlockHandler.ReflectDamageProtection);
                trace = t;

                if (config.options.BlockHandler.Log)
                {
                    Puts($"Debug: Attacker {attacker.displayName} ({attacker.userID}) was hurt for {reflectedDamage} damage. New Health: {attacker.health}");
                }
            }
        }

        private bool IsTutorialNetworkGroup(BaseEntity entity)
        {
            if (entity.net == null || entity.net.group == null) return false;
            return TutorialIsland.IsTutorialNetworkGroup(entity.net.group.ID);
        }

        private DamageResult CheckImmortalFlag(BaseEntity entity, RuleSet ruleSet, BaseEntity initiator, BaseEntity weaponPrefab)
        {
            // Check storage containers and doors for locks for player entity only
            if ((ruleSet._flags & RuleFlags.LockedBoxesImmortal) != 0 && entity is StorageContainer c && !(c is LootContainer or ChickenCoop or Beehive) || (ruleSet._flags & RuleFlags.LockedDoorsImmortal) != 0 && entity is Door)
            {
                if ((ruleSet._flags & RuleFlags.ExcludeTugboatFromImmortalFlags) != 0 && entity.GetParentEntity() is Tugboat)
                {
                    if (trace) Trace($"Player Door/StorageContainer detected with immortal flag on tugboat with ImmortalExcludesTugboats flag; allow and return", 1);
                    return DamageResult.Allow;
                }
                DamageResult hurt = CheckLock(ruleSet, entity, initiator, weaponPrefab); // check for lock
                if (trace)
                {
                    string action = hurt == DamageResult.None ? "null (no lock or unlocked); continue checks" : hurt == DamageResult.Allow ? "allow and return" : "block and return";
                    Trace($"Player Door/StorageContainer detected with immortal flag; lock check results: {action}", 1);
                }
                return hurt;
            }
            return DamageResult.None;
        }

        private object CanHelicopterStrafeTarget(PatrolHelicopterAI ai, BasePlayer ply)
        {
            if (ai == null || ai.isDead || ai.isRetiring || ply == null || ply.IsDestroyed || !ply.InSafeZone())
            {
                return null;
            }
            TriggerSafeZone zone = null;
            if (ply.triggers != null)
            {
                for (int i = 0; i < ply.triggers.Count; i++)
                {
                    TriggerSafeZone triggerSafeZone = ply.triggers[i] as TriggerSafeZone;
                    if (triggerSafeZone != null)
                    {
                        zone = triggerSafeZone;
                        break;
                    }
                }
            }
            if (zone == null || zone.triggerCollider == null || InRange(ply.transform.position, zone.transform.position, zone.triggerCollider.bounds.extents.Max() * 0.85f))
            {
                ai.Invoke("ClearTargets", 0f);
                ai.ClearAimTarget();
                ai.leftGun?.ClearTarget();
                ai.rightGun?.ClearTarget();
                ai.ExitCurrentState();
                ai.State_Patrol_Enter();
                return false;
            }
            return null;
        }

        public static bool InRange(Vector3 a, Vector3 b, float distance)
        {
            return (a - b).sqrMagnitude <= distance * distance;
        }

        private bool HandleHelicopter(RuleSet ruleSet, BaseEntity entity, BaseEntity weapon, BasePlayer victim, bool isVicId, bool allow)
        {
            if (entity is FarmableAnimal or ChickenCoop or Beehive)
            {
                if (trace) Trace($"Initiator is heli, target is {entity.ShortPrefabName}; block and return", 1);
                return false;
            }
            var eval = EvaluateRules(entity, weapon, ruleSet, false);
            if (eval != DamageResult.None)
            {
                string action = eval == DamageResult.Allow ? "allow and return" : "block and return";
                Trace($"Initiator is heli, target is {entity.ShortPrefabName}; {action}", 1);
                return eval == DamageResult.Allow;
            }
            if (isVicId)
            {
                if ((ruleSet._flags & RuleFlags.NoHeliDamageSleepers) != 0)
                {
                    if (trace)
                    {
                        string action1 = victim.IsSleeping() ? "victim is sleeping; block and return" : "victim is not sleeping; continue checks";
                        Trace($"Initiator is heli, and target is player; flag check results: {action1}", 1);
                    }
                    if (victim.IsSleeping()) return false;
                }
                bool val = (ruleSet._flags & RuleFlags.NoHeliDamagePlayer) != 0;
                if (trace)
                {
                    string action = val ? "flag set; block and return" : "flag not set; allow and return";
                    Trace($"Initiator is heli, and target is player; flag check results: {action}", 1);
                }
                return !val;
            }
            if (entity is MiningQuarry)
            {
                bool val = (ruleSet._flags & RuleFlags.NoHeliDamageQuarry) != 0;
                if (trace)
                {
                    string action = val ? "flag set; block and return" : "flag not set; allow and return";
                    Trace($"Initiator is heli, and target is quarry; flag check results: {action}", 1);
                }
                return !val;
            }
            if (entity is HitchTrough.IHitchable)
            {
                bool val = (ruleSet._flags & RuleFlags.NoHeliDamageRidableHorses) != 0;
                if (trace)
                {
                    string action = val ? "flag set; block and return" : "flag not set; allow and return";
                    Trace($"Initiator is heli, and target is ridablehorse; flag check results: {action}", 1);
                }
                return !val;
            }
            if ((ruleSet._flags & RuleFlags.NoHeliDamageBuildings) != 0 && IsPlayerEntity(entity))
            {
                if (!entity.HasParent() && entity is DecayEntity decayEntity && !HasBuildingPrivilege(decayEntity))
                {
                    if (trace) Trace($"Initiator is heli, {entity.ShortPrefabName} is not within TC; allow and return", 1);
                    return true;
                }
                if (trace) Trace($"Initiator is heli, {entity.ShortPrefabName} is within TC; block and return", 1);
                return false;
            }
            if (trace)
            {
                string action = allow ? "allow and return" : "block and return";
                Trace($"Initiator is heli, target is {entity.ShortPrefabName}; {action}", 1);
            }
            return allow;
        }

        private bool HasBuildingPrivilege(DecayEntity decayEntity)
        {
            var building = decayEntity.GetBuilding();
            if (building == null) return false;
            return building.GetDominatingBuildingPrivilege() != null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsOwner(ulong a, ulong b) => a == b;

        private bool IsAlly(BasePlayer a, BasePlayer b)
        {
            if (config.options.Clans && a.serverClan != null && a.clanId != 0 && a.clanId == b.clanId) return true;
            return IsAlly(a.userID, b.userID);
        }

        private bool IsAlly(BasePlayer a, ulong b)
        {
            if (config.options.Clans && a.serverClan != null)
            {
                foreach (var member in a.serverClan.Members)
                {
                    if (member.SteamId == b)
                    {
                        return true;
                    }
                }
                if (a.serverClan.Creator == b)
                {
                    return true;
                }
            }
            return IsAlly(a.userID, b);
        }

        private static BasePlayer FindPlayerOrSleeper(ulong userId) => BasePlayer.FindByID(userId) ?? BasePlayer.FindSleeping(userId);

        private bool IsNativeClanMate(ulong a, ulong b)
        {
            if (!config.options.Clans || a == b)
            {
                return false;
            }

            BasePlayer playerA = FindPlayerOrSleeper(a);
            BasePlayer playerB = FindPlayerOrSleeper(b);
            if (playerA != null && playerB != null && playerA.clanId != 0 && playerA.clanId == playerB.clanId)
            {
                return true;
            }

            if (playerA?.serverClan != null)
            {
                foreach (var member in playerA.serverClan.Members)
                {
                    if (member.SteamId == b)
                    {
                        return true;
                    }
                }

                if (playerA.serverClan.Creator == b)
                {
                    return true;
                }
            }

            if (playerB?.serverClan != null)
            {
                foreach (var member in playerB.serverClan.Members)
                {
                    if (member.SteamId == a)
                    {
                        return true;
                    }
                }

                if (playerB.serverClan.Creator == a)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsAlly(ulong vic, ulong atk)
        {
            if (IsOwner(vic, atk))
            {
                return true;
            }

            if (config.options.Teams)
            {
                if (RelationshipManager.ServerInstance.playerToTeam.TryGetValue(vic, out var team) && team.members.Contains(atk))
                {
                    return true;
                }

                if (RelationshipManager.ServerInstance.playerToTeam.TryGetValue(atk, out var team2) && team2.members.Contains(vic))
                {
                    return true;
                }
            }

            if (IsNativeClanMate(vic, atk))
            {
                return true;
            }

            if (config.options.Clans && Clans != null && Convert.ToBoolean(Clans?.Call("IsClanMember", vic, atk)))
            {
                return true;
            }

            if (config.options.Friends && Friends != null && Convert.ToBoolean(Friends?.Call("AreFriends", vic, atk)))
            {
                return true;
            }

            return false;
        }

        private bool LootCanBypass(BasePlayer looter) => looter != null && (looter.isInvisible || looter.limitNetworking);

        private object LootIsPlayerProtected(BasePlayer looter, BaseEntity target, ulong? ownerID, bool optionEnabled)
        {
            if (!optionEnabled || !ownerID.HasValue || ownerID == 0 || !ownerID.Value.IsSteamId() || looter == null || target == null) return null;
            if (LootCanBypass(looter)) return null;
            if (useZones)
            {
                using var looterLocations = GetLocationKeys(looter);
                using var targetLocations = GetLocationKeys(target);
                if (CheckExclusion(looterLocations, targetLocations, trace))
                    return null;
            }
            return IsAlly(looter, ownerID.Value) ? null : (object)true;
        }

        private bool CanAuthorize(BaseEntity entity, BasePlayer attacker, RuleSet ruleSet)
        {
            if (entity is BaseVehicle && EvaluateRules(entity, attacker, ruleSet, false) == DamageResult.Block)
            {
                return false;
            }

            if (entity.OwnerID == 0)
            {
                return entity is Minicopter;
            }

            return IsPlayerEntity(entity);
        }

        private bool IsPlayerEntity(BaseEntity entity)
        {
            if (entity is BaseMountable || entity is LegacyShelter || entity is LegacyShelterDoor || entity is FarmableAnimal)
            {
                return true;
            }

            if (entity.PrefabName.IndexOf("building", StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            if (entity.PrefabName.IndexOf("modular", StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            if (_deployables.Count == 0)
            {
                foreach (var def in ItemManager.GetItemDefinitions())
                {
                    if (def.TryGetComponent<ItemModDeployable>(out var imd))
                    {
                        _deployables.Add(imd.entityPrefab.resourcePath);
                    }
                }
            }

            return _deployables.Contains(entity.PrefabName);
        }

        private void ExcludePlayer(ulong userid, float maxDelayLength, Plugin plugin)
        {
            if (plugin == null)
            {
                return;
            }
            if (!playerDelayExclusions.TryGetValue(userid, out var exclusions))
            {
                playerDelayExclusions[userid] = exclusions = Pool.Get<List<PlayerExclusion>>();
            }
            var exclusion = exclusions.Find(x => x.plugin == plugin);
            if (maxDelayLength <= 0f)
            {
                if (exclusion != null)
                {
                    exclusions.Remove(exclusion);
                    exclusion.plugin = null;
                    exclusion.time = 0f;
                    Pool.Free(ref exclusion);
                }
                if (exclusions.Count == 0)
                {
                    playerDelayExclusions.Remove(userid);
                    Pool.FreeUnmanaged(ref exclusions);
                }
            }
            else
            {
                if (exclusion == null)
                {
                    exclusion = Pool.Get<PlayerExclusion>();
                    exclusions.Add(exclusion);
                }
                exclusion.plugin = plugin;
                exclusion.time = Time.time + maxDelayLength;
            }
        }

        private bool HasDelayExclusion(ulong userid)
        {
            if (playerDelayExclusions.TryGetValue(userid, out var exclusions))
            {
                for (int i = 0; i < exclusions.Count; i++)
                {
                    var exclusion = exclusions[i];
                    if (!exclusion.IsExpired)
                    {
                        return true;
                    }
                    exclusions.RemoveAt(i);
                    exclusion.plugin = null;
                    exclusion.time = 0f;
                    Pool.Free(ref exclusion);
                    i--;
                }
                if (exclusions.Count == 0)
                {
                    playerDelayExclusions.Remove(userid);
                    Pool.Free(ref exclusions);
                }
            }
            return false;
        }

        private float GetAboveworld() => config.options.Aboveworld;
        private float GetUnderworld() => config.options.Underworld;
        private float GetAboveworldOther() => config.options.AboveworldOther;
        private float GetUnderworldOther() => config.options.UnderworldOther;
        private bool GetDeepSeaPVP() => config.options.DeepSeaPVP;
        private bool GetDeepSeaRaiding() => config.options.DeepSeaRaiding;
        private bool GetDeepSea() => config.options.DeepSeaPVP && config.options.DeepSeaRaiding;
        private bool GetApartmentEnabled() => config.options.Apartments.Enabled;
        private bool GetApartmentPVP() => GetApartmentEnabled() && config.options.Apartments.PVP;
        private bool GetApartmentPVPAlive() => GetApartmentEnabled() && GetApartmentPVP() && config.options.Apartments.Alive;
        private bool GetApartmentMasterKeyBlocked() => GetApartmentEnabled() && config.options.Apartments.MasterKey;
        private bool GetApartmentBribesBlocked() => GetApartmentEnabled() && config.options.Apartments.Bribe;
        private bool GetApartmentRentalShopBreakInBlocked() => GetApartmentEnabled() && config.options.Apartments.Shop;
        private bool GetApartmentRoomBreakInBlocked() => GetApartmentEnabled() && config.options.Apartments.Room;

        private bool GetWorldNoAlloc(Dictionary<string, object> dict)
        {
            if (dict == null) return false;
            dict["world_above"] = GetAboveworld();
            dict["world_under"] = GetUnderworld();
            dict["world_aboveother"] = GetAboveworldOther();
            dict["world_underother"] = GetUnderworldOther();
            return true;
        }

        private bool GetDeepSeaNoAlloc(Dictionary<string, object> dict)
        {
            if (dict == null) return false;
            dict["deepsea_pvp"] = GetDeepSeaPVP();
            dict["deepsea_raiding"] = GetDeepSeaRaiding();
            dict["deepsea_pvpraiding"] = GetDeepSea();
            return true;
        }

        private bool GetApartmentsNoAlloc(Dictionary<string, object> dict)
        {
            if (dict == null) return false;
            dict["apartment_enabled"] = GetApartmentEnabled();
            dict["apartment_pvp"] = GetApartmentPVP();
            dict["apartment_alive"] = GetApartmentPVPAlive();
            dict["apartment_masterkey"] = GetApartmentMasterKeyBlocked();
            dict["apartment_bribes"] = GetApartmentBribesBlocked();
            dict["apartment_rentalshopbreakin"] = GetApartmentRentalShopBreakInBlocked();
            dict["apartment_apartmentroombreakin"] = GetApartmentRoomBreakInBlocked();
            return true;
        }

        private bool GetOptionsNoAlloc(Dictionary<string, object> dict)
        {
            if (dict == null) return false;
            GetWorldNoAlloc(dict);
            GetDeepSeaNoAlloc(dict);
            GetApartmentsNoAlloc(dict);
            return true;
        }

        private bool IsInDeepSea(Vector3 worldPos)
        {
            return worldPos.x >= deepSeaMinX && worldPos.x <= deepSeaMaxX && worldPos.y >= deepSeaMinY && worldPos.y <= deepSeaMaxY && worldPos.z >= deepSeaMinZ && worldPos.z <= deepSeaMaxZ;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool PlayerHasExclusion(BasePlayer player, PooledList<string> locs)
        {
            if (playerDelayExclusions.Count > 0 && HasDelayExclusion(player.userID))
            {
                return true;
            }
            Vector3 worldPos = player.transform.position;
            if ((config.options.Aboveworld < 5000f && worldPos.y >= config.options.Aboveworld) ||
                (config.options.Underworld > -500f && worldPos.y <= config.options.Underworld))
            {
                return true;
            }
            if (config.options.DeepSeaPVP && IsInDeepSea(worldPos))
            {
                return true;
            }
            if (locs != null && locs.Count > 0)
            {
                foreach (var loc in locs)
                {
                    if (config.mappings.TryGetValue(loc, out var mapping) && mapping == "exclude")
                    {
                        return true;
                    }
                    if (data.mappings.TryGetValue(loc, out mapping) && mapping == "exclude")
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private float deepSeaMinX, deepSeaMaxX, deepSeaMinY, deepSeaMaxY, deepSeaMinZ, deepSeaMaxZ;

        private void InitDeepSea()
        {
            var b = DeepSeaManager.DeepSeaBounds;
            var min = b.center - b.extents;
            var max = b.center + b.extents;
            
            deepSeaMinX = min.x; 
            deepSeaMaxX = max.x;
            deepSeaMinY = min.y; 
            deepSeaMaxY = max.y;
            deepSeaMinZ = min.z; 
            deepSeaMaxZ = max.z;
        }

        private bool PlayerHasExclusion(BasePlayer player) => 
            player != null && !player.IsDestroyed && PlayerHasExclusion(player, player.transform.position);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool PlayerHasExclusion(BasePlayer player, Vector3 worldPos)
        {
            if (playerDelayExclusions.Count > 0 && HasDelayExclusion(player.userID))
            {
                return true;
            }
            if ((config.options.Aboveworld < 5000f && worldPos.y >= config.options.Aboveworld) ||
                (config.options.Underworld > -500f && worldPos.y <= config.options.Underworld))
            {
                return true;
            }
            if (config.options.DeepSeaPVP && IsInDeepSea(worldPos))
            {
                return true;
            }
            if (useZones)
            {
                using var locs = GetLocationKeys(player);
                if (locs != null && locs.Count > 0)
                {
                    foreach (var loc in locs)
                    {
                        if (config.mappings.TryGetValue(loc, out var mapping) && mapping == "exclude")
                        {
                            return true;
                        }
                        if (data.mappings.TryGetValue(loc, out mapping) && mapping == "exclude")
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        [HookMethod("GetPlayerMapping")]
        public string GetPlayerMapping(BasePlayer player)
        {
            var t = trace;
            trace = false;
            RuleSet ruleSet;
            if (useZones)
            {
                using var entityLocations = GetLocationKeys(player);
                ruleSet = GetRuleSet(entityLocations, entityLocations);
                trace = t;
                if (!entityLocations.IsNullOrEmpty())
                {
                    foreach (var loc in entityLocations)
                    {
                        if (config.mappings.TryGetValue(loc, out var locMapping))
                        {
                            return locMapping;
                        }
                        if (data.mappings.TryGetValue(loc, out locMapping))
                        {
                            return locMapping;
                        }
                    }
                }
            }
            else ruleSet = currentRuleSet;
            trace = t;
            if (ruleSet != null && ruleSet.enabled && !ruleSet.IsEmpty())
            {
                if (config.mappings.TryGetValue(ruleSet.name, out var ruleSetMapping) || data.mappings.TryGetValue(ruleSet.name, out ruleSetMapping))
                {
                    return ruleSetMapping;
                }
            }
            return "default";
        }

        // process rules to determine whether to allow damage
        private DamageResult EvaluateRules(BaseEntity entity, BaseEntity attacker, RuleSet ruleSet, bool returnDefaultValue = true)
        {
            List<string> e0Groups = config.ResolveEntityGroups(attacker);
            List<string> e1Groups = config.ResolveEntityGroups(entity);

            if (trace)
            {
                string action1 = ConcatenateListOrDefault(e0Groups, "none");
                string action2 = ConcatenateListOrDefault(e1Groups, "none");
                Trace($"Initiator EntityGroup matches: {action1}", 2);
                Trace($"Target EntityGroup matches: {action2}", 2);
            }

            return ruleSet.Evaluate(this, attacker, e0Groups, entity, e1Groups, returnDefaultValue);
        }

        // checks an entity to see if it has a lock
        private DamageResult CheckLock(RuleSet ruleSet, BaseEntity entity, BaseEntity initiator, BaseEntity weaponPrefab)
        {
            var slot = entity.GetSlot(BaseEntity.Slot.Lock); // check for lock

            if (slot == null || !slot.IsLocked())
            {
                return DamageResult.None; // no lock or unlocked, continue checks
            }

            // if HeliDamageLocked flag is false or NoHeliDamage flag, all damage is cancelled from immortal flag
            if ((ruleSet._flags & RuleFlags.HeliDamageLocked) == 0 || (ruleSet._flags & RuleFlags.NoHeliDamage) != 0)
            {
                return DamageResult.Block;
            }

            return CheckHeliInitiator(ruleSet, initiator, weaponPrefab); // cancel damage except from heli
        }
        
        private DamageResult CheckHeliInitiator(RuleSet ruleSet, BaseEntity initiator, BaseEntity weaponPrefab)
        {
            // Check for heli initiator
            if (initiator is PatrolHelicopter || (initiator != null && (initiator.prefabID.Equals(oilfireballsmall) || initiator.prefabID.Equals(heli_napalm))))
            {
                return (ruleSet._flags & RuleFlags.NoHeliDamage) == 0 ? DamageResult.Allow : DamageResult.Block;
            }
            else if (weaponPrefab != null && (weaponPrefab.prefabID.Equals(rocket_heli) || weaponPrefab.prefabID.Equals(rocket_heli_napalm)))
            {
                return (ruleSet._flags & RuleFlags.NoHeliDamage) == 0 ? DamageResult.Allow : DamageResult.Block;
            }
            return DamageResult.None;
        }

        // checks if the player is authorized to damage the entity
        private bool CheckAuthorized(BaseEntity entity, BasePlayer player, RuleSet ruleSet, bool cupboardOwnership)
        {
            if (!cupboardOwnership)
            {
                return entity.OwnerID == 0 && !entity.InSafeZone() || IsAlly(player, entity.OwnerID); // allow damage to entities that the player owns or is an ally of
            }

            return CheckCupboardOwnership(entity, player);
        }

        private bool CheckCupboardOwnership(BaseEntity entity, BasePlayer player)
        {
            // treat entities outside of cupboard range as unowned, and entities inside cupboard range require authorization
            if (entity is LegacyShelter || entity is LegacyShelterDoor)
            {
                var entityPriv = entity.GetEntityBuildingPrivilege();

                return entityPriv == null || entityPriv.AnyAuthed() && entityPriv.IsAuthed(player);
            }

            if (entity is PlayerBoat playerBoat)
            {
                return playerBoat.IsPlayerAuthed(player, false);
            }

            if (entity is ResourceEntity)
            {
                return true;
            }

            BuildingPrivlidge priv = null;
            if (entity is DecayEntity decayEntity)
            {
                BuildingManager.Building building = decayEntity.GetBuilding();
                if (building != null)
                {
                    priv = building.GetDominatingBuildingPrivilege();
                }
            }

            if (priv == null)
            {
                priv = player.GetBuildingPrivilege(entity.WorldSpaceBounds(), true);
            }

            return priv == null || priv.AnyAuthed() && priv.IsAuthed(player);
        }

        private bool IsFunTurret(AutoTurret turret)
        {
            return turret.GetAttachedWeapon() is BaseProjectile projectile && projectile.GetItem() is Item weapon && weapon.info.shortname.StartsWith("fun.");
        }

        private object OnSamSiteTarget(BaseEntity attacker, BaseEntity entity)
        {
            SamSite ss = attacker as SamSite;
            if (Interface.CallHook("CanEntityBeTargeted", new object[] { entity, attacker }) is bool val)
            {
                if (val)
                {
                    if (trace) Trace($"CanEntityBeTargeted allowed {entity.ShortPrefabName} to be targetted by SamSite", 1);
                    return null;
                }

                if (trace) Trace($"CanEntityBeTargeted blocked {entity.ShortPrefabName} from being targetted by SamSite", 1);
                if (ss != null)
                {
                    ss.CancelInvoke(ss.WeaponTick);
                }
                return true;
            }

            if (attacker != null && IsSkinExclusion(attacker))
            {
                if (trace) Trace($"Target is {entity}; allow and return -> {attacker} skin ID {attacker.skinID}", 1);
                return null;
            }

            RuleSet ruleSet = GetRuleSet(entity, attacker);

            if (ruleSet == null)
            {
                if (trace) Trace($"OnSamSiteTarget allowed {entity.ShortPrefabName} to be targetted; no ruleset found.", 1);
                return null;
            }

            if (entity is MLRSRocket)
            {
                if ((ruleSet._flags & RuleFlags.SamSitesIgnoreMLRS) != 0) return SamSiteHelper(ss, entity);
                return null;
            }

            var staticRespawn = ss == null ? attacker.OwnerID == 0 : ss.staticRespawn;
            if (staticRespawn && (ruleSet._flags & RuleFlags.StaticSamSitesIgnorePlayers) != 0) return SamSiteHelper(attacker, entity);
            if (!staticRespawn && (ruleSet._flags & RuleFlags.PlayerSamSitesIgnorePlayers) != 0) return SamSiteHelper(attacker, entity);

            return null;
        }

        private object OnMlrsFire(MLRS mlrs, BasePlayer player)
        {
            if (mlrs == null || player == null)
            {
                return true;
            }

            if (Interface.CallHook("CanMlrsTargetLocation", new object[] { mlrs, player }) is bool val)
            {
                if (val)
                {
                    if (trace) Trace($"CanMlrsTargetLocation allowed {mlrs.TrueHitPos} to be targetted by {player.displayName}", 1);
                    return null;
                }

                if (trace) Trace($"CanMlrsTargetLocation blocked {mlrs.TrueHitPos} from being targetted by {player.displayName}", 1);
                return true;
            }

            //if (IsSkinExclusion(mlrs))
            //{
            //    if (trace) Trace($"MLRS attacker is {player}; allow and return -> {mlrs} skin ID {mlrs.skinID}", 1);
            //    return null;
            //}

            RuleSet ruleSet = GetRuleSet(player, mlrs);

            if (ruleSet == null)
            {
                if (trace) Trace($"OnMlrsFire allowed {mlrs.TrueHitPos} to be targetted by {player.displayName}; no ruleset found.", 1);
                return null;
            }

            return (ruleSet._flags & RuleFlags.NoMLRSDamage) != 0 ? true : (object)null;
        }

        // =====================
        // Integrated Supply Drop locking (lightweight)
        private void OnExplosiveDropped(BasePlayer player, SupplySignal ss, ThrownWeapon tw) => OnExplosiveThrown(player, ss, tw);

        private void OnExplosiveThrown(BasePlayer player, SupplySignal ss, ThrownWeapon tw)
        {
            if (player == null || ss == null || ss.IsDestroyed)
            {
                return;
            }

            // Ignore BradleyDrops custom signals so that plugin can handle delivery itself
            TryCopyThrownItemSkin(ss, tw);
            if (Dispatch_IsBradleyDrop(ss.skinID, tw))
            {
                return;
            }

            if (!config.SupplyDrops.LockSupplyDropsToPlayers)
            {
                return;
            }

            // Respect allowed skins list (0 means allow any)
            if (config.SupplyDrops.AllowedSignalSkins != null && config.SupplyDrops.AllowedSignalSkins.Count > 0)
            {
                if (!config.SupplyDrops.AllowedSignalSkins.Contains(0) && !config.SupplyDrops.AllowedSignalSkins.Contains(ss.skinID))
                {
                    return;
                }
            }

            // If bypass is enabled, spawn a drop immediately in front of the player and skip planes entirely
            if (config.SupplyDrops.BypassSpawningCargoPlane)
            {
                // Check if player is building blocked (not authorized on TC) - if so, skip bypass
                // This matches LootDefender 2.2.7 behavior: "Ignore Bypass Spawning Cargo Plane when player is building blocked (requires at least 1 player to be authed on TC)"
                BuildingPrivlidge priv = ss.GetBuildingPrivilege(ss.WorldSpaceBounds(), true);
                if (priv != null)
                {
                    // Check if player is authorized on this TC
                    bool playerAuthorized = priv.IsAuthed(player.userID);
                    // Check if there's at least 1 authorized player on this TC
                    bool hasAuthorizedPlayers = priv.authorizedPlayers != null && priv.authorizedPlayers.Count > 0;
                    
                    // If player is NOT authorized AND there's at least 1 authorized player, skip bypass
                    if (!playerAuthorized && hasAuthorizedPlayers)
                    {
                        // Player is building blocked - don't bypass, let normal cargo plane spawn
                        return;
                    }
                }
                
                Vector3 origin = player.eyes?.position ?? player.transform.position + Vector3.up * 1.5f;
                Vector3 forward = player.eyes?.BodyForward() ?? player.transform.forward;
                Vector3 pos = origin + forward.normalized * 3f;
                // Mark this signal as handled to prevent a second spawn on plane signal
                if (ss.net != null)
                {
                    _bypassedSupplySignals.Add(ss.net.ID.Value);
                }
                // keep within configured maximum distance from signal if set
                if (config.SupplyDrops.MaximumDropDistanceFromSignal > 0f)
                {
                    Vector3 ssPos = ss.transform.position;
                    if ((pos - ssPos).magnitude > config.SupplyDrops.MaximumDropDistanceFromSignal)
                    {
                        pos = ssPos + (pos - ssPos).normalized * config.SupplyDrops.MaximumDropDistanceFromSignal;
                    }
                }
                // Adjust height above ground a bit
                float terrain = TerrainMeta.HeightMap != null ? TerrainMeta.HeightMap.GetHeight(pos) : pos.y;
                pos.y = Mathf.Max(pos.y, terrain + 1.5f);
                SpawnAndLockSupplyDrop(pos, player.userID);
                // Remove signal smoke by killing the signal entity immediately
                if (ss != null && !ss.IsDestroyed)
                {
                    ss.Kill();
                }
            }
        }

        private void OnRandomRaidWin(SupplyDrop drop, List<ulong> playerIDs)
        {
            if (drop == null || drop.IsDestroyed || !config.SupplyDrops.LockFromNpcRandomRaids) return;
            if (NpcRandomRaids == null) return;
            
            if (playerIDs != null && playerIDs.Count > 0 && !drop.OwnerID.IsSteamId())
            {
                drop.OwnerID = playerIDs[0];
                if (config.SupplyDrops.LockSupplyDropsToPlayers && drop.OwnerID.IsSteamId())
                {
                    var owners = Pool.Get<HashSet<ulong>>();
                    owners.Clear();
                    owners.Add(drop.OwnerID);
                    LD_LockEntity(drop, owners, config.SupplyDrops.LockSeconds);
                    Pool.FreeUnmanaged(ref owners);
                }
            }
        }

        private void OnCargoPlaneSignaled(CargoPlane plane, SupplySignal ss)
        {
            try
            {
                if (plane == null || ss == null)
                {
                    return;
                }

                // Ignore BradleyDrops custom signals so that plugin can handle delivery itself
                if (Dispatch_IsBradleyDrop(ss.skinID))
                {
                    return;
                }
                
                // Apply low altitude drop if configured
                if (config.SupplyDrops.LowDrop)
                {
                    float y = plane.transform.position.y;
                    y /= UnityEngine.Random.Range(2, 4); // Lower altitude for faster drop
                    plane.transform.position = new Vector3(plane.transform.position.x, y, plane.transform.position.z);
                    plane.startPos = new Vector3(plane.startPos.x, y, plane.startPos.z);
                    if (plane.endPos.y > 0)
                    {
                        plane.endPos = new Vector3(plane.endPos.x, y, plane.endPos.z);
                    }
                }

                // Bypass plane entirely if configured
                if (config.SupplyDrops.BypassSpawningCargoPlane)
                {
                    ulong ownerId = 0;
                    BasePlayer ownerPlayer = null;
                    if (ss.creatorEntity is BasePlayer ssp)
                    {
                        ownerId = ssp.userID;
                        ownerPlayer = ssp;
                    }
                    else if (ss.OwnerID.IsSteamId())
                    {
                        ownerId = ss.OwnerID;
                        ownerPlayer = BasePlayer.FindAwakeOrSleepingByID(ownerId);
                    }
                    // If we already spawned a drop at throw-time for this signal, just kill the plane and do not spawn again
                    if (ss.net != null && _bypassedSupplySignals.Remove(ss.net.ID.Value))
                    {
                        plane.Kill();
                        if (ss != null && !ss.IsDestroyed)
                        {
                            ss.Kill();
                        }
                        return;
                    }
                    
                    // Check if owner player is building blocked (not authorized on TC) - if so, skip bypass
                    // This matches LootDefender 2.2.7 behavior: "Ignore Bypass Spawning Cargo Plane when player is building blocked (requires at least 1 player to be authed on TC)"
                    if (ownerPlayer != null)
                    {
                        BuildingPrivlidge priv = ss.GetBuildingPrivilege(ss.WorldSpaceBounds(), true);
                        if (priv != null)
                        {
                            // Check if owner player is authorized on this TC
                            bool playerAuthorized = priv.IsAuthed(ownerPlayer.userID);
                            // Check if there's at least 1 authorized player on this TC
                            bool hasAuthorizedPlayers = priv.authorizedPlayers != null && priv.authorizedPlayers.Count > 0;
                            
                            // If owner player is NOT authorized AND there's at least 1 authorized player, skip bypass
                            if (!playerAuthorized && hasAuthorizedPlayers)
                            {
                                // Owner player is building blocked - don't bypass, let normal cargo plane spawn
                                return;
                            }
                        }
                    }
                    
                    Vector3 pos = ss.transform.position;
                    float terrain = TerrainMeta.HeightMap != null ? TerrainMeta.HeightMap.GetHeight(pos) : pos.y;
                    pos.y = Mathf.Max(pos.y, terrain + 1.5f);
                    SpawnAndLockSupplyDrop(pos, ownerId);
                    plane.Kill();
                    if (ss != null && !ss.IsDestroyed)
                    {
                        ss.Kill();
                    }
                    return;
                }

                if (!config.SupplyDrops.LockSupplyDropsToPlayers)
                {
                    return;
                }

                // Check allowed skins (0 in list means allow any)
                if (config.SupplyDrops.AllowedSignalSkins != null && config.SupplyDrops.AllowedSignalSkins.Count > 0)
                {
                    if (!config.SupplyDrops.AllowedSignalSkins.Contains(0) && !config.SupplyDrops.AllowedSignalSkins.Contains(ss.skinID))
                    {
                        return;
                    }
                }

                ulong owner = 0;
                if (ss.creatorEntity is BasePlayer p1)
                {
                    owner = p1.userID;
                }
                else if (ss.OwnerID.IsSteamId())
                {
                    owner = ss.OwnerID;
                }

                if (owner == 0)
                {
                    return;
                }

                if (plane.net != null)
                {
                    _supplyPlaneOwner[plane.net.ID.Value] = owner;
                }
            }
            catch { }
        }

        private void OnSupplyDropDropped(SupplyDrop drop, CargoPlane plane)
        {
            try
            {
                if (drop == null || plane == null)
                {
                    return;
                }

                // Ignore drops originating from BradleyDrops delivery planes
                if (Dispatch_IsBradleyDrop(plane.skinID))
                {
                    return;
                }

                if (plane.net == null || drop.net == null)
                {
                    return;
                }

                if (config.SupplyDrops.LockSupplyDropsToPlayers && _supplyPlaneOwner.TryGetValue(plane.net.ID.Value, out var owner) && owner.IsSteamId())
                {
                    var owners = Pool.Get<HashSet<ulong>>();
                    owners.Clear();
                    owners.Add(owner);
                    LD_LockEntity(drop, owners, config.SupplyDrops.LockSeconds);
                    Pool.FreeUnmanaged(ref owners);
                    _supplyPlaneOwner.Remove(plane.net.ID.Value);
                }

                // Optional timed cleanup
                if (config.SupplyDrops.DestroyDropAfterSeconds > 0)
                {
                    drop.Invoke(() =>
                    {
                        if (drop != null && !drop.IsDestroyed)
                        {
                            drop.Kill();
                        }
                    }, config.SupplyDrops.DestroyDropAfterSeconds);
                }
            }
            catch { }
        }

        private void OnEntitySpawned(SupplyDrop drop)
        {
            if (drop == null || drop.IsDestroyed) return;
            
            // Handle Helpful Supply plugin drops
            if (config.SupplyDrops.LockFromHelpfulSupply && HelpfulSupply != null)
            {
                // Check if this is from HelpfulSupply plugin
                // If HelpfulSupply is configured to allow all players, we don't need to lock
                // This is handled in OnCargoPlaneSignaled
            }
            
            // Handle timed destruction
            if (config.SupplyDrops.DestroyDropAfterSeconds > 0)
            {
                drop.Invoke(() =>
                {
                    if (drop != null && !drop.IsDestroyed)
                    {
                        drop.Kill();
                    }
                }, config.SupplyDrops.DestroyDropAfterSeconds);
            }
        }
        
        private void OnEntitySpawned(CH47Helicopter heli)
        {
            // Handle CH47 gibs disable
            if (config.SupplyDrops.DisableCH47Gibs && heli != null && !heli.IsDestroyed)
            {
                // Prevent CH47 gibs from spawning by clearing the guid
                if (heli.serverGibs != null)
                {
                    heli.serverGibs.guid = string.Empty;
                }
            }
        }
        
        private void OnEntitySpawned(BaseEntity entity)
        {
            // This method is for general entity spawn handling
            // Specific entity types (SupplyDrop, CH47Helicopter) are handled by their specific handlers above
        }

        private void SpawnAndLockSupplyDrop(Vector3 position, ulong ownerId)
        {
            try
            {
                var ent = GameManager.server.CreateEntity(SupplyDropPrefab, position) as SupplyDrop;
                if (ent == null)
                {
                    return;
                }
                if (ownerId.IsSteamId())
                {
                    ent.OwnerID = ownerId;
                }
                ent.Spawn();

                if (config.SupplyDrops.LockSupplyDropsToPlayers && ownerId.IsSteamId())
                {
                    var owners = Pool.Get<HashSet<ulong>>();
                    owners.Clear();
                    owners.Add(ownerId);
                    LD_LockEntity(ent, owners, config.SupplyDrops.LockSeconds);
                    Pool.FreeUnmanaged(ref owners);
                }

                if (config.SupplyDrops.DestroyDropAfterSeconds > 0)
                {
                    ent.Invoke(() =>
                    {
                        if (ent != null && !ent.IsDestroyed)
                        {
                            ent.Kill();
                        }
                    }, config.SupplyDrops.DestroyDropAfterSeconds);
                }
            }
            catch { }
        }

        private object CanWaterBallSplash(ItemDefinition liquidDef, Vector3 position, float radius, int amount)
        {
            if (config.PreventThrowingWaterInFreezingBiome && TerrainMeta.BiomeMap != null)
            {
                TerrainBiome.Enum biome = (TerrainBiome.Enum)TerrainMeta.BiomeMap.GetBiomeMaxType(position);
                if (biome == TerrainBiome.Enum.Arctic || biome == TerrainBiome.Enum.Tundra)
                {
                    return false;
                }
            }
            if (config.BlockRadioactiveWaterDamage && liquidDef == WaterTypes.RadioactiveWaterItemDef)
            {
                return false;
            }
            return null;
        }

        private object OnEntityMarkHostile(BasePlayer player, float duration)
        {
            if (player == null || Interface.CallHook("CanMarkEntityHostile", player, duration) is bool val && val)
            {
                return null;
            }
            return true;
        }

        private void OnExplosiveDropped(BasePlayer player, TimedExplosive te, ThrownWeapon tw)
        {
            if (player != null && te != null && te.creatorPlayer == null)
            {
                te.creatorPlayer = player;
            }
        }

        /// <summary>
        /// Sets AntiHack.PlayerStates[].LastAdminCheatTime to suppress vehicle collision ragdoll.
        /// NoInlining + catch: servers without AntiHack.PlayerState must not fail AllowDamage JIT.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void TryPreventCollisionRagdoll(BasePlayer victim)
        {
            try
            {
                if (!AntiHack.PlayerStates.IsCreated)
                    return;
                // UsedAdminCheat is (now - LastAdminCheatTime < seconds). +1.9s matches 2.4.2's
                // lastAdminCheatTime future-stamp; 2.4.3's -1.9s only covers ~0.1s and is too short.
                ref AntiHack.PlayerState playerState = ref ((Span<AntiHack.PlayerState>)AntiHack.PlayerStates)[victim.ActivePlayerInd];
                playerState.LastAdminCheatTime = UnityEngine.Time.realtimeSinceStartup + 1.9f;
            }
            catch (TypeLoadException) { }
            catch (MissingFieldException) { }
            catch (MissingMemberException) { }
        }

#if OXIDE_PUBLICIZED || CARBON
        private void OnEntitySpawned(RidableHorse horse)
        {
            if (config.PreventRagdolling && horse != null)
            {
                horse.playerRagdollThreshold = float.MaxValue;
            }
        }

        private void CanRagdollDismount(BaseRagdoll ragdoll, BasePlayer player)
        {
            if (config.PreventRagdolling && ragdoll != null)
            {            
                ragdoll.dieOnImpact = false;
            }
        }
#endif

        #region Locks etc

        private object OnPlayerActiveShieldDrop(BasePlayer player, Shield shield) => true; // Shield

        private object OnPlayerDropActiveItem(BasePlayer player, Item item) => true; // Active held item

        private object OnBackpackDrop(Item backpack, PlayerInventory inv) => true; // Rust backpack


        private int wrongCodes;
        private object OnCodeEntered(CodeLock codeLock, BasePlayer player, string code)
        {
            if (codeLock == null || player == null || player.limitNetworking || player.isInvisible) 
                return null;
            var parent = codeLock.GetParentEntity() as BaseEntity;
            if (parent != null && parent.OwnerID.IsSteamId() && !IsAlly(player, parent.OwnerID))
            {
                Effect.server.Run(codeLock.effectDenied.resourcePath, codeLock, 0u, Vector3.zero, Vector3.forward);
                Effect.server.Run(codeLock.effectShock.resourcePath, codeLock, 0u, Vector3.zero, Vector3.forward);
                player.Hurt((float)(wrongCodes + 1) * 5f, DamageType.ElectricShock, codeLock, useProtection: false);
                if (++wrongCodes % 5 == 0)
                {
                    player.ShowToast(GameTip.Styles.Red_Normal, CodeLock.blockwarning);
                }
                return true;
            }
            wrongCodes = 0;
            return null;
        }

        private void AllowLocksOnContainers()
        {
            if (config.options.Loot.Locks)
            {
                ServerMgr.Instance.StartCoroutine(LockCo());
            }
            if (config.options.Loot.Antigrief)
            {
                Subscribe(nameof(OnCodeEntered));
            }
        }

        private IEnumerator LockCo()
        {
            long deadline = GetFrameDeadline();
            foreach (var ent in BaseNetworkable.serverEntities)
            {
                if (Stopwatch.GetTimestamp() >= deadline)
                {
                    yield return null;
                    deadline = GetFrameDeadline();
                }
                if (IsUnloading)
                {
                    yield break;
                }
                if (ent is StorageContainer c && c != null && !c.isLockable)
                {
                    OnEntitySpawned(c);
                }
            }
        }

        private void CreateKeyLock(BaseEntity entity, ulong userid)
        {
            if (GameManager.server.CreateEntity(StringPool.Get(2106860026)) is KeyLock keyLock && keyLock != null)
            {
                keyLock.gameObject.Identity();
                keyLock.SetParent(entity, entity.GetSlotAnchorName(BaseEntity.Slot.Lock));
                keyLock.Spawn();
                entity.SetSlot(BaseEntity.Slot.Lock, keyLock);
                keyLock.OwnerID = userid;
                keyLock.firstKeyCreated = true;
                keyLock.SetFlag(BaseEntity.Flags.Locked, true);
            }
        }

        private void CreateCodeLock(BaseEntity entity, ulong userid)
        {
            if (GameManager.server.CreateEntity(StringPool.Get(3518824735)) is CodeLock codeLock && codeLock != null)
            {
                codeLock.gameObject.Identity();
                codeLock.SetParent(entity, entity.GetSlotAnchorName(BaseEntity.Slot.Lock));
                codeLock.Spawn();
                entity.SetSlot(BaseEntity.Slot.Lock, codeLock);
                codeLock.code = UnityEngine.Random.Range(1000, 9999).ToString();
                codeLock.hasCode = true;
                codeLock.OwnerID = userid;
                codeLock.guestCode = string.Empty;
                codeLock.hasGuestCode = false;
                codeLock.guestPlayers.Clear();
                codeLock.whitelistPlayers.Clear();
                codeLock.whitelistPlayers.Add(userid);
                codeLock.SetFlag(BaseEntity.Flags.Locked, true);
            }
        }

        private readonly List<string> doors = new() { "shutter.wood.a" };

        private void OnEntitySpawned(Door door)
        {
            if (config.options.Loot.Locks && door != null && !door.canTakeLock && door.OwnerID.IsSteamId() && doors.Contains(door.ShortPrefabName))
            {
                door.canTakeLock = true;
            }
        }

        private void OnEntitySpawned(StorageContainer container)
        {
            if (container == null || !container.OwnerID.IsSteamId())
                return;

            if (config.options.Loot.Locks && !container.isLockable)
            {
                container.isLockable = !config.options.Loot.NoLocks.Contains(container.ShortPrefabName) && !config.options.Loot.NoLocks.Contains(GetTypeName(container));
            }

            if (config.options.Loot.AutoLock.TryGetValue(container.ShortPrefabName, out string type) || config.options.Loot.AutoLock.TryGetValue(GetTypeName(container), out type))
            {
                if (type.Equals("nothing", StringComparison.OrdinalIgnoreCase))
                    return;

                container.Invoke(() => TryCreateLock(container, type), 0.3f);
            }
        }

        private void OnEntitySpawned(ContainerIOEntity container)
        {
            if (config.options.Loot.AutoLock.TryGetValue(container.ShortPrefabName, out string type) || config.options.Loot.AutoLock.TryGetValue(GetTypeName(container), out type))
            {
                if (type.Equals("nothing", StringComparison.OrdinalIgnoreCase))
                    return;

                container.Invoke(() => TryCreateLock(container, type), 0.3f);
            }
        }

        private bool TryCreateLock(BaseEntity container, string type)
        {
            if (container.IsDestroyed)
                return false;

            var slot = container.GetSlot(BaseEntity.Slot.Lock);

            if (slot != null)
                return false;

            if (type == "codelock")
            {
                CreateCodeLock(container, container.OwnerID);
            }
            else if (type == "keylock")
            {
                CreateKeyLock(container, container.OwnerID);
            }

            return true;
        }

        private void OnEntitySpawned(BaseLock baseLock)
        {
            if (!config.options.Loot.Locks || baseLock == null)
            {
                return;
            }

            BaseEntity entity = baseLock.GetParentEntity();
            if (entity == null || !entity.OwnerID.IsSteamId())
            {
                return;
            }

            if (config.options.Loot.NoLocks.Count > 0)
            {
                if (config.options.Loot.NoLocks.Contains(entity.ShortPrefabName))
                {
                    return;
                }
                if (config.options.Loot.NoLocks.Contains(GetTypeName(entity)))
                {
                    return;
                }
            }
            
            if (entity is StashContainer)
            {
                baseLock.transform.localPosition = new Vector3(0, -0.3f, 0f);
                baseLock.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
            else if (entity is Beehive)
            {
                baseLock.transform.localPosition = new Vector3(0, 0.8f, 0.3f);
                baseLock.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            }
            else if (entity is FishMount or HuntingTrophy or PhotoFrame)
            {
                baseLock.transform.localPosition = new Vector3(0, entity.bounds.extents.y + 0.25f, 0f);
                baseLock.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            }
            else if (entity is WeaponRack)
            {
                baseLock.transform.localPosition = new Vector3(-entity.bounds.extents.x + 0.15f, entity.bounds.extents.y * 1.25f, 0f);
                if (entity.ShortPrefabName == "weaponrack_stand.deployed") baseLock.transform.localPosition += new Vector3(0f, 0.65f);
                baseLock.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            }
            else if (entity.ShortPrefabName == "bbq.deployed")
            {
                baseLock.transform.localPosition = new Vector3(0.3f, 0.75f, 0f);
            }
            else if (entity is CookingWorkbenchBbq)
            {
                baseLock.transform.localPosition = new Vector3(0.3f, -3f, -0.3f);
            }
            else if (entity is ChickenCoop)
            {
                baseLock.transform.localPosition = new Vector3(-0.3f, 0.35f, 1.5f);
                baseLock.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            }
            else if (entity is Composter || entity.ShortPrefabName == "refinery_small_deployed")
            {
                baseLock.transform.localPosition += new Vector3(0.6f, 0.75f, 0f);
            }
            else if (entity.ShortPrefabName == "fireplace.deployed")
            {
                baseLock.transform.localPosition += new Vector3(-1.0f, 0.9f, -0.225f);
            }
            else if (entity is FlameTurret)
            {
                baseLock.transform.localPosition += new Vector3(-0.075f, 0.165f, 0.075f);
                baseLock.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            }
            else if (entity.ShortPrefabName == "furnace")
            {
                baseLock.transform.localPosition += new Vector3(0f, 1.2f, 0.2f);
                baseLock.transform.localRotation = new Quaternion(0f, -0.7f, 0f, 0.7f);
            }
            else if (entity.ShortPrefabName == "legacy_furnace")
            {
                baseLock.transform.localPosition += new Vector3(0f, 1.2f, 0.275f);
                baseLock.transform.localRotation = new Quaternion(0f, -0.7f, 0f, 0.7f);
            }
            else if (entity.ShortPrefabName == "furnace.large")
            {
                baseLock.transform.localPosition += new Vector3(0.75f, 1f, -0.75f);
                baseLock.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            }
            else if (entity.ShortPrefabName == "electricfurnace.deployed")
            {
                baseLock.transform.localPosition += new Vector3(0f, 0.215f, 0.275f);
                baseLock.transform.localRotation = new Quaternion(0f, -0.7f, 0f, 0.7f);
            }
            else if (entity is Stocking)
            {
                baseLock.transform.localPosition += new Vector3(-0.1f, 0.25f, 0f);
                baseLock.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            }
            else if (entity is GunTrap or TorchDeployableLightSource or BaseFuelLightSource)
            {
                baseLock.transform.localPosition += new Vector3(0f, 0.4f, 0f);
            }
            else if (entity.ShortPrefabName == "hitchtrough.deployed")
            {
                baseLock.transform.localPosition = new Vector3(-1.115f, 0.503f, 0.1f);
                baseLock.transform.localRotation = Quaternion.Euler(0f, 180f, 10f);
            }
            else if (entity is MixingTable)
            {
                baseLock.transform.localPosition = new Vector3(-0.575f, 0.4f, 0.275f);
            }
            else if (entity is Mailbox)
            {
                baseLock.transform.localPosition = new Vector3(-0.1f, 1.1675f, 0.2f);
            }
            else if (entity.ShortPrefabName == "planter.large.deployed" || entity.ShortPrefabName == "planter.triangle.deployed")
            {
                baseLock.transform.localPosition = new Vector3(0f, 0.45f, 0f);
                baseLock.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
            else if (entity.ShortPrefabName == "planter.small.deployed")
            {
                baseLock.transform.localPosition = new Vector3(0f, 0.45f, 0f);
                baseLock.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
            else if (entity.ShortPrefabName == "bathtub.planter.deployed")
            {
                baseLock.transform.localPosition = new Vector3(0f, 0.45f, 0.65f);
                baseLock.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            }
            else if (entity.ShortPrefabName == "minecart.planter.deployed")
            {
                baseLock.transform.localPosition = new Vector3(0f, 0.65f, 0.55f);
                baseLock.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            }
            else if (entity is ResearchTable)
            {
                baseLock.transform.localPosition += new Vector3(0f, 0.3f, 0f);
                baseLock.transform.localRotation = new Quaternion(0f, -0.7f, 0f, 0.7f);
            }
            else if (entity is Workbench)
            {
                if (entity.ShortPrefabName == "io.table.deployed")
                {
                    baseLock.transform.localPosition += new Vector3(0f, 1.1f, 0f);
                    baseLock.transform.localRotation = Quaternion.Euler(0f, 90f, -45f);
                }
                else
                {
                    baseLock.transform.localPosition += new Vector3(0f, 0.9f, 0f);
                    baseLock.transform.localRotation = new Quaternion(0f, 0f, 1f, 1f);
                }
            }
            else if (entity is VendingMachine)
            {
                baseLock.transform.localPosition += new Vector3(-0.5175f, 0.15f, -0.5f);
            }
        }

        private object CanChangeGrade(BasePlayer player, BuildingBlock block, BuildingGrade.Enum grade, ulong skin)
        {
            if (player == null || block == null || block.OwnerID == 0 || block.OwnerID == player.userID)
            {
                return null;
            }

            if (IsAlly(player, block.OwnerID))
            {
                return true;
            }

            return null;
        }

        private object OnCupboardAuthorize(BuildingPrivlidge priv, BasePlayer player)
        {
            if (player == null || player.limitNetworking || player.isInvisible || priv == null || !priv.OwnerID.IsSteamId()) return null;
            // Allow admins to access TCs if AdminCanLoot is enabled - check early to bypass all restrictions
            if (player.IsAdmin && config?.PreventLooting != null && config.PreventLooting.AdminCanLoot) return null;
            // All TCs (vanilla and retro): only entity OwnerID or same clan/team may authorize or loot - run before any config that would allow (e.g. CanAuthorizeCupboard)
            ulong ownerId = priv.OwnerID;
            if (ownerId != 0 && ownerId != player.userID && !IsAlly(player, ownerId))
            {
                SendReply(player, GetMessage("OnTryAuthCB", player.UserIDString));
                return false;
            }
            // Check PreventLooting CanAuthorizeCupboard setting (only matters for other checks below)
            if (config.PreventLooting.Enabled && config.PreventLooting.CanAuthorizeCupboard) return null;
            if (config.PreventLooting.Enabled && !config.PreventLooting.CanAuthorizeCupboard)
            {
                BaseEntity entity = priv as BaseEntity;
                if (player.IsAdmin && config.PreventLooting.AdminCanLoot) return null;
                if (PreventLootingCheckHelper(player, entity)) return null;
                if (entity.OwnerID != 0 && entity.OwnerID != player.userID && !PreventLootingIsFriend(entity.OwnerID, player.userID))
                {
                    SendReply(player, GetMessage("OnTryAuthCB", player.UserIDString));
                    return false;
                }
            }
            BaseLock baseLock = priv.GetSlot(BaseEntity.Slot.Lock) as BaseLock;
            if (baseLock != null && baseLock.IsLocked()) return null;
            if (player.IsAdmin && config?.PreventLooting != null && config.PreventLooting.AdminCanLoot) return null;
            if (IsAlly(player, priv.OwnerID)) return null;
            Message(player, "Error_CannotAccessEntity");
            return true;
        }

        private object CanLootEntity(BasePlayer player, BuildingPrivlidge priv)
        {
            return OnCupboardAuthorize(priv, player);
        }

        private object OnVendingTransaction(InvisibleVendingMachine vm, BasePlayer buyer, int sellOrderId, int numberOfTransactions, ItemContainer targetContainer)
        {
            if (vm == null || vm.IsDestroyed || sellOrderId < 0 || sellOrderId >= vm.sellOrders.sellOrders.Count) return null;
            var sellOrder = vm.sellOrders.sellOrders[sellOrderId];
            var key = ItemManager.Items?.MasterKey ?? ItemManager.FindItemDefinition("apartment.master_key");
            if (key == null || key.itemid != sellOrder.itemToSellID) return null;
            if (Interface.CallHook("CanPurchaseMasterKey", buyer, vm, sellOrderId, numberOfTransactions, targetContainer) is true) return null;
            if (buyer != null) Message(buyer, "Error_MasterKeyDisabled");
            return false;
        }

        private object OnNpcConversationRespond(NPCApartmentSecurity nas, BasePlayer player, ConversationData conversationFor, ConversationData.ResponseNode responseNode)
        {
            string actionString = responseNode.GetActionString();
            if (actionString == "PaidDoor" && !config.options.Apartments.Bribe)
            {
                if (Interface.CallHook("CanBribeSecurityGuard", player, nas, conversationFor, responseNode) is true) return null;
                Message(player, "Error_BribeDisabled");
                nas.ForceEndConversation(player);
                return false;
            }
            return null;
        }

        private object CanAffordApartmentMasterKey(BasePlayer player)
        {
            if (!config.options.Apartments.MasterKey)
            {
                if (Interface.CallHook("CanPurchaseMasterKey", player) is true) return null;
                Message(player, "Error_MasterKeyDisabled");
                return false;
            }
            return null;
        }

        private object OnApartmentMasterKeyPurchase(BasePlayer player) => CanAffordApartmentMasterKey(player) != null ? (object)true : null;

        private object OnRentableShopBreakInComplete(RentableShop shop, BasePlayer player)
        {
            if (Interface.CallHook("CanPlayerCompleteBreakIn", player, shop) is true) return null;
            Message(player, "Error_MasterKeyDisabled");
            return true;
        }

        private object OnApartmentRoomBreakInComplete(ApartmentRoom apt, BasePlayer player, ApartmentDoor door)
        {
            if (Interface.CallHook("CanPlayerCompleteBreakIn", player, door, apt) is true) return null;
            Message(player, "Error_MasterKeyDisabled");
            return true;
        }

        private object CanTakeCutting(BasePlayer player, GrowableEntity plant) => CanHarvest(player, plant, nameof(CanTakeCutting));

        private object OnGrowableGather(GrowableEntity plant, BasePlayer player, bool eat) => CanHarvest(player, plant, nameof(OnGrowableGather));

        private object OnConstructionPlace(GrowableEntity plant, Construction component, Construction.Target placement, BasePlayer ownerPlayer) => CanHarvest(ownerPlayer, plant, nameof(OnConstructionPlace), placement.entity);

        private object CanHarvest(BasePlayer looter, GrowableEntity plant, string caller, BaseEntity parent = null)
        {
            if (!config.options.Loot.Planters) return null;
            if (looter == null || plant == null) return null;

            var planter = plant.GetPlanter() ?? parent as PlanterBox;
            if (planter == null) return null;
            if (!planter.OwnerID.IsSteamId()) return null;

            BuildingPrivlidge priv = planter.GetBuildingPrivilege(true);
            if (priv != null && priv.IsAuthed(looter)) return null;
            if (Interface.CallHook("CanUsePlanterBox", looter, planter, plant, caller) is true) return null;
            Message(looter, "Error_Harvest");
            return true;
        }

        internal bool AllowGrowableHarvest(GrowableEntity plant, BasePlayer player, string caller)
        {
            if (config.PreventLooting.ProtectPlanterboxes && CanLootGrowableEntity(plant, player) != null)
                return false;
            if (config.options.Loot.Planters && CanHarvest(player, plant, caller) != null)
                return false;
            return true;
        }

        private object CanLootPlayer(BasePlayer target, BasePlayer looter)
        {
            if (target == null || looter == null) return null;
            if (config.options.Loot.Sleepers && LootIsPlayerProtected(looter, target, target?.userID, true) != null)
                return false;
            if (!config.PreventLooting.Enabled)
            {
                if (config.PreventLooting.Debug)
                {
                    Puts($"[PreventLooting] CanLootPlayer: PreventLooting disabled - allowing {looter?.displayName} to loot {target?.displayName}");
                }
                return null;
            }
            if (config.PreventLooting.AdminCanLoot && looter.IsAdmin)
            {
                if (config.PreventLooting.Debug)
                {
                    Puts($"[PreventLooting] CanLootPlayer: Admin override - allowing {looter?.displayName} to loot {target?.displayName}");
                }
                return null;
            }
            if (config.PreventLooting.AllowLootPlayers)
            {
                if (config.PreventLooting.Debug)
                {
                    Puts($"[PreventLooting] CanLootPlayer: AllowLootPlayers enabled - allowing {looter?.displayName} to loot {target?.displayName}");
                }
                return null;
            }
            if (IsAlly(looter, target.userID))
            {
                if (config.PreventLooting.Debug)
                {
                    Puts($"[PreventLooting] CanLootPlayer: Ally check passed - allowing {looter?.displayName} to loot {target?.displayName}");
                }
                return null;
            }
            if (config.PreventLooting.UseCupboardAuth && AllowAuthorizationDamage(target, looter))
            {
                // Do not allow TC auth alone to permit looting offline/sleeping players (only owner or ally may loot them)
                if (target.IsSleeping() || !target.IsConnected)
                {
                    if (config.PreventLooting.Debug)
                    {
                        Puts($"[PreventLooting] CanLootPlayer: Blocking TC-auth looting of offline/sleeping player {target?.displayName} by {looter?.displayName}");
                    }
                    Message(looter, "Error_CannotAccessEntity");
                    return false;
                }
                if (config.PreventLooting.Debug)
                {
                    Puts($"[PreventLooting] CanLootPlayer: Cupboard auth check passed - allowing {looter?.displayName} to loot {target?.displayName}");
                }
                return null;
            }
            if (config.PreventLooting.Debug)
            {
                Puts($"[PreventLooting] CanLootPlayer: BLOCKING {looter?.displayName} from looting {target?.displayName}");
            }
            Message(looter, "Error_CannotAccessEntity");
            return false;
        }

        // Backup hook to stop looting if it somehow gets past CanLootPlayer
        private void OnLootPlayer(BasePlayer target, BasePlayer looter)
        {
            if (target == null || looter == null) return;
            if (!config.PreventLooting.Enabled) return;
            if (config.PreventLooting.AdminCanLoot && looter.IsAdmin) return;
            if (config.PreventLooting.AllowLootPlayers) return;
            if (IsAlly(looter, target.userID)) return;
            if (config.PreventLooting.UseCupboardAuth && AllowAuthorizationDamage(target, looter))
            {
                // Do not allow TC auth alone to permit looting offline/sleeping players
                if (target.IsSleeping() || !target.IsConnected) { /* fall through to block */ }
                else return;
            }

            // If we get here, looting should be blocked - stop it immediately
            if (config.PreventLooting.Debug)
            {
                Puts($"[PreventLooting] OnLootPlayer: BLOCKING and stopping looting - {looter?.displayName} attempting to loot {target?.displayName}");
            }
            looter.EndLooting();
            Message(looter, "Error_CannotAccessEntity");
        }

        // Override OnStartBeingLooted to bypass onlyOwnerLoot and safe zone restrictions for DroppedItemContainer
        private object OnStartBeingLooted(DroppedItemContainer container, BasePlayer player)
        {
            if (container == null || player == null) return null;
            
            // Admin override: admins can always loot DroppedItemContainer regardless of restrictions
            if (player.IsAdmin)
            {
                if (config.PreventLooting.Debug)
                {
                    Puts($"[TruePVE] OnStartBeingLooted: Admin override - allowing DroppedItemContainer {container.ShortPrefabName} (owner={container.playerSteamID}, admin={player.userID})");
                }
                container.SetFlag(BaseEntity.Flags.Reserved2, true);
                return true; // Bypass all Rust checks for admins
            }
            
            // For unowned items (playerSteamID = 0), always allow regardless of safe zone or onlyOwnerLoot
            if (container.playerSteamID == 0UL)
            {
                if (config.PreventLooting.Debug)
                {
                    Puts($"[TruePVE] OnStartBeingLooted: Allowing unowned DroppedItemContainer {container.ShortPrefabName} in safe zone (looter={player.userID})");
                }
                container.SetFlag(BaseEntity.Flags.Reserved2, true);
                return true; // Bypass all Rust checks for unowned items
            }
            
            // For owned items, still respect safe zone restrictions (Rust's first check)
            if ((player.InSafeZone() || container.InSafeZone()) && (ulong)player.userID != container.playerSteamID)
            {
                return null; // Let Rust handle safe zone blocking for owned items
            }
            
            // If onlyOwnerLoot is enabled and player doesn't match, bypass it (our plugin handles access control via CanLootEntity)
            if (container.onlyOwnerLoot && (ulong)player.userID != container.playerSteamID)
            {
                if (config.PreventLooting.Debug)
                {
                    Puts($"[TruePVE] OnStartBeingLooted: Bypassing onlyOwnerLoot for DroppedItemContainer {container.ShortPrefabName} (owner={container.playerSteamID}, looter={player.userID})");
                }
                // Return true to allow looting (bypass Rust's onlyOwnerLoot check)
                // Set the flag that Rust would set (Reserved2 = HasBeenOpened)
                container.SetFlag(BaseEntity.Flags.Reserved2, true);
                // Our CanLootEntity hook will handle the actual access control
                return true;
            }
            return null; // Let Rust handle other cases normally
        }

        // Integrated PreventLooting + LootDefender enforcement
        private object CanLootEntity(BasePlayer player, LootableCorpse corpse)
        {
            if (corpse == null || player == null) return null;
            if (config.options.Loot.Corpses)
            {
                var protectedResult = LootIsPlayerProtected(player, corpse, corpse?.playerSteamID, true);
                if (protectedResult != null) return protectedResult;
            }
            // Check PreventLooting first if enabled
            if (config.PreventLooting.Enabled && !config.PreventLooting.AllowLootCorpses)
            {
                if (PreventLootingCheckHelper(player, corpse as BaseEntity)) return null;
                if (PreventLootingIsFriend(corpse.playerSteamID, player.userID)) return null;
                if (config.PreventLooting.UsePermissions && !permission.UserHasPermission(corpse.playerSteamID.ToString(), CorpsePerm)) return null;
                if (corpse.playerSteamID < 76561197960265728L || player.userID == corpse.playerSteamID) return null;
                if ((config.PreventLooting.UseCupboard || config.PreventLooting.UseCupboardAuth || config.PreventLooting.OnlyInCupboardRange))
                    if (PreventLootingCheckAuthCupboard(corpse, player)) return null;
                SendReply(player, GetMessage("OnTryLootCorpse", player.UserIDString));
                return true;
            }
            return CanLootEntityUnified(player, corpse);
        }

        private object CanLootEntity(BasePlayer player, DroppedItemContainer container)
        {
            if (container == null || player == null) return null;
            if (config.options.Loot.Backpacks)
            {
                var protectedResult = LootIsPlayerProtected(player, container as BaseEntity, container?.playerSteamID, true);
                if (protectedResult != null) return protectedResult;
            }
            // Check PreventLooting first if enabled
            if (config.PreventLooting.Enabled)
            {
                if (config.PreventLooting.AllowLootBackpacks && config.PreventLooting.AllowLootBackpackPlugin) return CanLootEntityUnified(player, container);
                if (PreventLootingCheckHelper(player, container as BaseEntity)) return CanLootEntityUnified(player, container);
                BaseEntity entity = container as BaseEntity;
                if (((entity.name.Contains("item_drop_backpack") && !config.PreventLooting.AllowLootBackpacks) || (entity.name.Contains("droppedbackpack") && !config.PreventLooting.AllowLootBackpackPlugin)))
                {
                    if (PreventLootingIsFriend(container.playerSteamID, player.userID)) return CanLootEntityUnified(player, container);
                    if (config.PreventLooting.UsePermissions && !permission.UserHasPermission(container.playerSteamID.ToString(), BackpackPerm)) return CanLootEntityUnified(player, container);
                    if (container.playerSteamID < 76561197960265728L || player.userID == container.playerSteamID) return CanLootEntityUnified(player, container);
                    if ((config.PreventLooting.UseCupboard || config.PreventLooting.UseCupboardAuth || config.PreventLooting.OnlyInCupboardRange))
                        if (PreventLootingCheckAuthCupboard(container, player)) return CanLootEntityUnified(player, container);
                    SendReply(player, GetMessage("OnTryLootBackpack", player.UserIDString));
                    return true;
                }
            }
            return CanLootEntityUnified(player, container);
        }

        private object CanLootEntity(BasePlayer player, StorageContainer container)
        {
            if (container == null || player == null) return null;
            // Check PreventLooting first if enabled
            if (config.PreventLooting.Enabled && !config.PreventLooting.AllowLootStorage)
            {
                BaseEntity entity = container as BaseEntity;
                if (!string.IsNullOrEmpty(container.ShortPrefabName) && container.ShortPrefabName == "cookingworkbench.bbq")
                {
                    entity = container.GetComponentInParent<CookingWorkbench>() as BaseEntity;
                }
                if (entity == null) return CanLootEntityUnified(player, container);
                var result = PreventLootingCanLootEntity(player, entity);
                if (result != null) return result;
            }
            return CanLootEntityUnified(player, container);
        }

        private object CanLootEntity(BasePlayer player, BaseEntity entity)
        {
            return CanLootEntityUnified(player, entity);
        }

        // Planterbox protection: Check if player can harvest from growable entity
        private object CanLootGrowableEntity(GrowableEntity plant, BasePlayer player)
        {
            if (player == null || plant == null) return true; // Block if invalid

            // Admin bypass
            if (config.PreventLooting.AdminCanLoot && player.IsAdmin) return null;

            // Check if plant has a planter box parent
            if (plant.GetPlanter() == null) return null; // No planter = allow

            PlanterBox planter = plant.GetPlanter();
            ulong plantOwner = planter.OwnerID;

            // Owner can always harvest
            if (plantOwner == player.userID) return null;

            // Allies can harvest (team/clan/friends)
            if (IsAlly(player, plantOwner)) return null;

            // Check cupboard authorization if enabled
            if (config.PreventLooting.UseCupboardAuth)
            {
                BuildingPrivlidge priv = planter.GetBuildingPrivilege();
                if (priv?.IsAuthed(player) == true) return null;
            }

            // Block unauthorized harvesting
            if (config.PreventLooting.Debug)
            {
                Puts($"[PlanterboxProtection] Blocked {player.displayName} ({player.userID}) from harvesting {plant.ShortPrefabName} owned by {plantOwner}");
            }
            SendReply(player, GetMessage("Planterbox_NoHarvest", player.UserIDString));
            return true; // Block
        }

        // Resolve once from the loaded plugin assembly. Type.GetType(..., CustomHelicopterTiers2) hits Harmony's
        // AssemblyResolve on every LootContainer check (~900+ times per session) and tanks server FPS.
        private Type GetHelicopterCrateLockComponentType()
        {
            if (CustomHelicopterTiers2 == null || !CustomHelicopterTiers2.IsLoaded)
            {
                _helicopterCrateLockComponentType = null;
                return null;
            }

            if (_helicopterCrateLockComponentType != null)
                return _helicopterCrateLockComponentType;

            _helicopterCrateLockComponentType = CustomHelicopterTiers2.GetType().Assembly
                .GetType("Oxide.Plugins.CustomHelicopterTiers2+HelicopterCrateLockComponent");

            return _helicopterCrateLockComponentType;
        }

        private object CanLootEntityUnified(BasePlayer player, BaseEntity entity)
        {
            if (player == null || entity == null || !entity.IsValid()) return null;

            // Early check: unowned entities (OwnerID == 0) are always allowed unless locked by LootDefender
            // This matches LootDefender's behavior
            if (entity.OwnerID == 0UL && (!config.LootDefender.Enabled || entity.net == null || !_ldLocks.ContainsKey(entity.net.ID.Value)))
            {
                // Check for CustomHelicopterTiers2 locked crates even if unowned
                if (entity is LootContainer lootContainer)
                {
                    var componentType = GetHelicopterCrateLockComponentType();
                    if (componentType != null)
                    {
                        var helicopterCrateLock = lootContainer.GetComponent(componentType);
                        if (helicopterCrateLock != null)
                        {
                            // Let CustomHelicopterTiers2 handle it
                            return null;
                        }
                    }
                }
                // No locks, allow
                return null;
            }

            // Admin override: admins can always loot any container in safe zones (Outpost/Compound)
            if (player.IsAdmin && (player.InSafeZone() || entity.InSafeZone()))
            {
                if (config.PreventLooting.Debug)
                {
                    Puts($"[TruePVE] CanLootEntity: Admin override - allowing {entity.GetType().Name} {entity.ShortPrefabName} in safe zone (admin={player.userID}, playerInSafeZone={player.InSafeZone()}, entityInSafeZone={entity.InSafeZone()})");
                }
                return null; // Allow admins to loot in safe zones
            }

            // Allow Convoy event crates explicitly
            try
            {
                if (Convoy != null && Convoy.IsLoaded && (bool)(Convoy.Call("IsConvoyCrate", entity) ?? false))
                {
                    return null;
                }
            }
            catch { }

            // Allow Backpacks plugin's internal storage container (coffinstorage) to open regardless of TC auth
            // to avoid blocking the backpack UI. Restrict to ownerless/disabled instances used by the plugin so
            // player-owned coffins in the world are still governed by PreventLooting.
            if (entity is StorageContainer sc && sc.ShortPrefabName == "coffinstorage")
            {
                if (sc.OwnerID == 0UL || sc.HasFlag(BaseEntity.Flags.Disabled))
                {
                    return null;
                }
            }

            // Vehicle ownership bypass: allow interacting with containers mounted on your vehicle (or allies)
            BaseEntity root = entity;
            for (int s = 0; s < 25 && root != null && root.HasParent(); s++)
            {
                root = root.GetParentEntity();
            }
            // Always allow casino/tabletop card game storages (blackjack/roulette) and their pot containers
            // These are spawned under a BaseCardGameEntity and use CardGamePlayerStorage
            if (entity is StorageContainer && (root is BaseCardGameEntity || entity is CardGamePlayerStorage))
            {
                return null;
            }
            // Allow Big Wheel betting terminals and Slot Machine storages regardless of PreventLooting rules
            // Heuristic by prefab names to avoid hard type references
            {
                string entName = entity.ShortPrefabName ?? string.Empty;
                string rootName = root?.ShortPrefabName ?? string.Empty;
                entName = entName.ToLowerInvariant();
                rootName = rootName.ToLowerInvariant();
                // Big Wheel betting terminals live under big_wheel game prefab
                bool isBigWheelContext = entName.Contains("betting") || entName.Contains("terminal") || rootName.Contains("big_wheel") || entName.Contains("big_wheel");
                // Slot machine storage lives under slot_machine prefab
                bool isSlotMachineContext = rootName.Contains("slot_machine") || entName.Contains("slot_machine") || entName.Contains("slotmachine");
                if (isBigWheelContext || isSlotMachineContext)
                {
                    return null;
                }
            }
            if (root is BaseVehicle vehicle && vehicle.OwnerID.IsSteamId())
            {
                bool allow = vehicle.OwnerID == player.userID || IsAlly(player, vehicle.OwnerID);
                if (config.PreventLooting.Debug)
                {
                    Puts($"[PreventLooting] Vehicle container check: entity={entity.ShortPrefabName} vehicleOwner={vehicle.OwnerID} player={player.userID} allow={allow}");
                }
                if (allow) return null;
            }

            // Check for CustomHelicopterTiers2 locked crates BEFORE LootDefender checks
            // This ensures CustomHelicopterTiers2's locking takes precedence
            if (entity is LootContainer lootContainer2)
            {
                var componentType = GetHelicopterCrateLockComponentType();
                if (componentType != null)
                {
                    var helicopterCrateLock = lootContainer2.GetComponent(componentType);
                    if (helicopterCrateLock != null)
                    {
                        // Let CustomHelicopterTiers2's CanLootEntity hook handle the locking logic
                        // Return null to allow other hooks (CustomHelicopterTiers2) to process it
                        return null;
                    }
                }
            }

            // Allow DroppedItemContainer (player-dropped items/backpacks) - always allow, skip LootDefender and PreventLooting
            // DroppedItemContainer is player-dropped loot and should be accessible to everyone
            if (entity is DroppedItemContainer dic)
            {
                // Admin override: admins can always loot in safe zones (Outpost/Compound)
                if (player.IsAdmin)
                {
                    if (config.PreventLooting.Debug)
                    {
                        Puts($"[TruePVE] CanLootEntity: Admin override - allowing DroppedItemContainer {entity.ShortPrefabName} in safe zone (owner={dic.playerSteamID}, admin={player.userID}, inSafeZone={player.InSafeZone() || dic.InSafeZone()})");
                    }
                    return null; // Allow admins to loot in safe zones
                }
                
                // Skip LootDefender checks (LootDefender is for Bradley/Heli/NPC event loot only)
                // Always allow DroppedItemContainer - it's already handled by OnStartBeingLooted
                if (config.PreventLooting.Debug)
                {
                    Puts($"[PreventLooting] Allow DroppedItemContainer {entity.ShortPrefabName} (playerSteamID={dic.playerSteamID}, OwnerID={entity.OwnerID}) for {player.userID}");
                }
                return null;
            }

            // First enforce LootDefender locks (for Bradley/Heli/NPC loot only)
            // This matches LootDefender's CanLootEntityHandler logic
            if (config.LootDefender.Enabled && entity.net != null)
            {
                if (_ldLocks.TryGetValue(entity.net.ID.Value, out var li))
                {
                    // Clean up expired locks or entities with OwnerID == 0 (matches LootDefender behavior)
                    if (li.IsExpired || entity.OwnerID == 0UL)
                    {
                        _ldLocks.Remove(entity.net.ID.Value);
                        if (entity.OwnerID == 0UL && config.PreventLooting.Debug)
                        {
                            Puts($"[LootDefender] Cleaned up lock for unowned entity {entity.ShortPrefabName}");
                        }
                    }
                    else if (!li.CanInteract(player.userID, player, IsAlly))
                    {
                        // Call OnLootLockedEntity hook to allow other plugins to override (matches LootDefender)
                        object hookResult = Interface.CallHook("OnLootLockedEntity", player, entity);
                        if (hookResult != null && Convert.ToBoolean(hookResult))
                        {
                            // Another plugin overrode the lock
                            return null;
                        }

                        if (config.PreventLooting.Debug)
                        {
                            Puts($"[LootDefender] Deny looting locked entity {entity.ShortPrefabName} for {player.userID}");
                        }
                        Message(player, "Notify_LockedToOthers");
                        if (config.LootDefender.OwnerToastOnLootDenied)
                        {
                            var ownerNames = new List<string>();
                            foreach (ulong ownerId in li.Owners)
                            {
                                var ownerPlayer = BasePlayer.FindAwakeOrSleepingByID(ownerId);
                                ownerNames.Add(ownerPlayer != null ? ownerPlayer.displayName : ownerId.ToString());
                            }
                            string owners = string.Join(", ", ownerNames);
                            if (!string.IsNullOrEmpty(owners))
                            {
                                string fmt = GetMessage("Notify_LootOwned", player.UserIDString);
                                string text = string.IsNullOrEmpty(fmt) ? owners : string.Format(fmt, owners);
                                player.SendConsoleCommand("gametip.showtoast", config.Notify.Style, text, string.Empty);
                            }
                        }
                        return true;
                    }
                    else
                    {
                        if (config.PreventLooting.Debug)
                        {
                            Puts($"[LootDefender] Allow looting locked entity {entity.ShortPrefabName} for {player.userID}");
                        }
                        Message(player, "Notify_LockedToYou");
                    }
                }
            }

            // Allow storage and recycler access inside world-spawned vehicles (e.g., camper interiors)
            // Only applies when the root vehicle has no owner (OwnerID == 0)
            if (root is BaseVehicle worldVehicle && worldVehicle.OwnerID == 0UL && (entity is StorageContainer || entity is ContainerIOEntity))
            {
                return null;
            }

            // Allow world/monument recyclers (unowned) regardless of cupboard auth settings
            if (entity is Recycler && entity.OwnerID == 0UL)
            {
                return null;
            }

            // Always allow interacting with vending-related entities (NPC and player-run)
            // Covers NPCVendingMachine (Outpost/Bandit), VendingMachine, ShopFront, and MarketTerminal
            if (entity is VendingMachine || entity is NPCVendingMachine || entity is ShopFront || entity is MarketTerminal)
            {
                return null;
            }

            // Public/world loot should remain open unless explicitly locked by LD above
            // Regular LootContainer (not locked by CustomHelicopterTiers2 or LootDefender) - allow
            if (entity is LootContainer)
            {
                return null;
            }
            if (entity is SupplyDrop || entity is HackableLockedCrate || entity is DroppedItemContainer)
            {
                return null;
            }
            if (entity is LockedByEntCrate && (entity.net == null || !_ldLocks.ContainsKey(entity.net.ID.Value)))
            {
                return null;
            }

            // Allow Large Excavator fuel/engine and output piles regardless of other restrictions
            if (IsExcavatorEntity(entity))
            {
                return null;
            }

            // Then PreventLooting-style checks (if enabled)
            if (!config.PreventLooting.Enabled)
            {
                return null;
            }

            if (config.PreventLooting.AdminCanLoot && player.IsAdmin)
            {
                return null;
            }

            // Exclusions by prefab
            if (entity != null && (config.PreventLooting.ExcludedShortPrefabNames.Count > 0 || config.PreventLooting.ExcludeEntities.Count > 0))
            {
                string sp = entity.ShortPrefabName;
                string tn = GetTypeName(entity);
                if (config.PreventLooting.ExcludedShortPrefabNames.Contains(sp) || config.PreventLooting.ExcludedShortPrefabNames.Contains(tn) || config.PreventLooting.ExcludeEntities.Contains(sp))
                {
                    if (config.PreventLooting.Debug)
                    {
                        Puts($"[PreventLooting] Excluded prefab {sp}/{tn} for {player.userID}");
                    }
                    return null;
                }
            }

            // Owner/allies bypass any further checks (including cupboard auth)
            if (entity.OwnerID != 0UL && IsAlly(player, entity.OwnerID))
            {
                return null;
            }

            // Respect cupboard authorization if configured
            // Skip cupboard auth check for unowned entities (world-spawned static entities like repairbench_static)
            if (config.PreventLooting.UseCupboardAuth && entity is DecayEntity decay && entity.OwnerID != 0UL)
            {
                bool authed;
                if (entity is BuildingPrivlidge cupboard)
                    authed = cupboard.IsAuthed(player);
                else
                    authed = IsAuthed(decay, player);
                if (config.PreventLooting.Debug)
                {
                    Puts($"[PreventLooting] Cupboard auth check: authed={authed} entity={entity.ShortPrefabName} player={player.userID}");
                }
                if (!authed) return true;
            }

            // Only in cupboard range option
            if (config.PreventLooting.OnlyInCupboardRange && entity is DecayEntity d2)
            {
                if (d2.GetBuilding() is BuildingManager.Building b && b.GetDominatingBuildingPrivilege() == null)
                {
                    return null; // outside TC range => do not block
                }
            }

            // Entity-specific allowances
            if (entity is LootableCorpse corpse)
            {
                if (config.PreventLooting.AllowLootCorpses)
                {
                    if (config.PreventLooting.Debug)
                    {
                        Puts($"[PreventLooting] Allow corpse by config for {player.userID}");
                    }
                    return null;
                }
                
                // For corpses, check playerSteamID instead of OwnerID
                ulong corpseOwner = corpse.playerSteamID;
                if (corpseOwner == 0 || !corpseOwner.IsSteamId())
                {
                    if (config.PreventLooting.Debug)
                    {
                        Puts($"[PreventLooting] Corpse has no valid playerSteamID - allowing: {entity.ShortPrefabName}");
                    }
                    return null; // NPC corpse or invalid - allow
                }
                
                // Check if looter is the owner or an ally
                if (corpseOwner == player.userID)
                {
                    if (config.PreventLooting.Debug)
                    {
                        Puts($"[PreventLooting] Corpse owner is looter - allowing: {player.userID}");
                    }
                    return null; // Owner can always loot their own corpse
                }
                
                if (IsAlly(player, corpseOwner))
                {
                    if (config.PreventLooting.Debug)
                    {
                        Puts($"[PreventLooting] Corpse owner is ally - allowing: owner={corpseOwner} player={player.userID}");
                    }
                    return null;
                }
                
                // Block looting - not owner and not ally
                if (config.PreventLooting.Debug)
                {
                    Puts($"[PreventLooting] BLOCKING corpse looting: entity={entity.ShortPrefabName} owner={corpseOwner} player={player.userID}");
                }
                return true;
            }
            else if (entity is StorageContainer)
            {
                if (config.PreventLooting.AllowLootStorage)
                {
                    if (config.PreventLooting.Debug)
                    {
                        Puts($"[PreventLooting] Allow storage by config for {player.userID}");
                    }
                    return null;
                }
            }

            // Default: block if allied/ownership isn't met
            ulong owner = entity.OwnerID;
            if (owner == 0)
            {
                if (config.PreventLooting.Debug)
                {
                    Puts($"[PreventLooting] Unowned entity allow: {entity.ShortPrefabName}");
                }
                return null; // unowned structures: allow by default
            }
            if (IsAlly(player, owner))
            {
                if (config.PreventLooting.Debug)
                {
                    Puts($"[PreventLooting] Ally allow: owner={owner} player={player.userID}");
                }
                return null;
            }
            // Check PreventLooting sharing system
            if (_preventLootingData != null && _preventLootingData.Data.ContainsKey(entity.net.ID.Value))
            {
                BaseEntity childentity = entity;
                entity = PreventLootingCheckParent(entity);
                if (childentity == entity)
                {
                    if (_preventLootingData.Data[entity.net.ID.Value].Share.Contains(player.userID) || _preventLootingData.Data[entity.net.ID.Value].Share.Contains(0))
                    {
                        if (config.PreventLooting.Debug)
                        {
                            Puts($"[PreventLooting] Shared entity allow: entity={entity.ShortPrefabName} player={player.userID}");
                        }
                        return null;
                    }
                }
                else
                {
                    if (_preventLootingData.Data[entity.net.ID.Value].Quarry.ContainsKey(childentity.ShortPrefabName))
                        if (_preventLootingData.Data[entity.net.ID.Value].Quarry[childentity.ShortPrefabName].Contains(player.userID) || _preventLootingData.Data[entity.net.ID.Value].Quarry[childentity.ShortPrefabName].Contains(0))
                        {
                            if (config.PreventLooting.Debug)
                            {
                                Puts($"[PreventLooting] Shared quarry entity allow: entity={childentity.ShortPrefabName} player={player.userID}");
                            }
                            return null;
                        }
                }
            }
            if (config.PreventLooting.Debug)
            {
                Puts($"[PreventLooting] Blocked: entity={entity.ShortPrefabName} owner={owner} player={player.userID}");
            }
            return true;
        }

        // Backpacks.cs integration
        private object CanOpenBackpack(BasePlayer looter, ulong ownerId)
        {
            if (looter == null) return null;
            // Only enforce if Prevent Looting is enabled; otherwise allow
            if (!config.PreventLooting.Enabled) return null;
            if (config.PreventLooting.AdminCanLoot && looter.IsAdmin) return null;
            if (ownerId == 0 || ownerId == looter.userID) return null;
            if (IsAlly(looter, ownerId)) return null;
            if (config.PreventLooting.Debug)
            {
                Puts($"[PreventLooting] CanOpenBackpack blocked: looter={looter.userID} owner={ownerId} admin={looter.IsAdmin}");
            }
            return false;
        }

        private void OnBackpackClosed(BasePlayer looter, ulong ownerId, ItemContainer container)
        {
            // Ensure any residual UI from previous block attempts is cleared by letting Backpacks run its normal flow
        }

        private object CanLootEntity(BasePlayer player, ModularCarGarage carLift)
        {
            if (player == null || carLift == null || carLift.OwnerID == player.userID)
                return null;

            if (carLift.carOccupant != null && carLift.carOccupant.HasSlot(BaseEntity.Slot.Lock))
                return null;

            if (carLift.OwnerID.IsSteamId() && !IsAlly(player, carLift.OwnerID))
                return true;

            return null;
        }

        #endregion Locks

        #region PreventLooting Hooks
        // PreventLooting hook implementations - all check config.PreventLooting.Enabled first
        
        // Planterbox protection: Prevent unauthorized planting
        private void OnEntityBuilt(Planner plan, GameObject seed)
        {
            if (!config.PreventLooting.ProtectPlanterboxes) return;
            
            var player = plan.GetOwnerPlayer();
            var growableEntity = seed.GetComponent<GrowableEntity>();
            if (player == null || growableEntity == null) return;

            var held = player.GetActiveItem();
            if (held == null) return;

            NextTick(() =>
            {
                var parent = growableEntity.GetParentEntity();
                if (!(parent is PlanterBox planter)) return;

                ulong plantOwner = planter.OwnerID;
                
                // Owner can always plant
                if (plantOwner == player.userID) return;

                // Allies can plant
                if (IsAlly(player, plantOwner)) return;

                // Check cupboard authorization if enabled
                if (config.PreventLooting.UseCupboardAuth)
                {
                    BuildingPrivlidge priv = planter.GetBuildingPrivilege();
                    if (priv?.IsAuthed(player) == true) return;
                }

                // Block unauthorized planting and refund seed
                SendReply(player, GetMessage("Planterbox_NoPlant", player.UserIDString));
                var refund = ItemManager.CreateByName(held.info.shortname, 1);
                if (refund != null)
                {
                    player.inventory.GiveItem(refund);
                }
                
                // Remove the planted entity
                if (growableEntity.IsValid())
                {
                    growableEntity.Kill();
                }
            });
        }
        
        private void OnItemDropped(Item item, BaseEntity entity)
        {
            if (!config.PreventLooting.Enabled || item == null || entity == null) return;
            // Set owner for dropped backpacks
            if (item.info.itemid == -907422733 || item.info.itemid == 2068884361 || item.info.itemid == -874650016)
            {
                entity.OwnerID = item.GetOwnerPlayer()?.userID ?? 0;
            }
        }

        private object OnItemPickup(Item item, BasePlayer player)
        {
            if (!config.PreventLooting.Enabled || item == null || player == null) return null;
            BaseEntity entity = item?.GetWorldEntity();
            if (entity == null) return null;
            if (PreventLootingCheckHelper(player, entity)) return null;
            if (entity.OwnerID != 0 && entity.OwnerID != player.userID && !PreventLootingIsFriend(entity.OwnerID, player.userID))
            {
                if (item.info.itemid == -907422733 || item.info.itemid == 2068884361 || item.info.itemid == -874650016)
                {
                    if ((config.PreventLooting.UseCupboard || config.PreventLooting.UseCupboardAuth || config.PreventLooting.OnlyInCupboardRange))
                        if (PreventLootingCheckAuthCupboard(entity, player)) return null;
                    SendReply(player, GetMessage("OnTryPickup", player.UserIDString));
                    return false;
                }
            }
            return null;
        }

        private void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (!config.PreventLooting.Enabled || player == null || entity == null) return;
            Item item = entity?.GetItem();
            if (item == null) return;
            if (PreventLootingCheckHelper(player, entity)) return;
            if (item.info.itemid == -907422733 || item.info.itemid == 2068884361 || item.info.itemid == -874650016)
            {
                if (config.PreventLooting.AllowLootBackpacks) return;
                if (PreventLootingIsFriend(entity.OwnerID, player.userID)) return;
                if (config.PreventLooting.UsePermissions && !permission.UserHasPermission(entity.OwnerID.ToString(), BackpackPerm)) return;
                if (entity.OwnerID < 76561197960265728L || player.userID == entity.OwnerID) return;
                if (config.PreventLooting.UseCupboard || config.PreventLooting.OnlyInCupboardRange)
                    if (PreventLootingCheckAuthCupboard(entity, player)) return;
                NextFrame(() =>
                {
                    player.inventory.loot.Clear();
                    player.inventory.loot.SendImmediate();
                });
                SendReply(player, GetMessage("OnTryLootBackpack", player.UserIDString));
            }
        }

        private object CanMannequinChangePose(Mannequin mannequin, BasePlayer player)
        {
            if (!config.PreventLooting.Enabled || mannequin == null || player == null) return null;
            if (config.PreventLooting.AllowLootStorage) return null;
            BaseEntity entity = mannequin as BaseEntity;
            return PreventLootingCanLootEntity(player, entity);
        }

        private object CanMannequinSwap(Mannequin mannequin, BasePlayer player)
        {
            if (!config.PreventLooting.Enabled || mannequin == null || player == null) return null;
            if (config.PreventLooting.AllowLootStorage) return null;
            BaseEntity entity = mannequin as BaseEntity;
            return PreventLootingCanLootEntity(player, entity);
        }

        private object OnRackedWeaponMount(Item weapon, BasePlayer player, WeaponRack rack)
        {
            if (!config.PreventLooting.Enabled || rack == null || player == null || weapon == null) return null;
            if (config.PreventLooting.AllowRackedWeaponMount) return null;
            BaseEntity entity = rack as BaseEntity;
            if (PreventLootingCheckRackedWeapon(player, entity)) return null;
            return false;
        }

        private object OnRackedWeaponSwap(Item weaponMounting, WeaponRackSlot weaponTaking, BasePlayer player, WeaponRack rack)
        {
            if (!config.PreventLooting.Enabled || rack == null || player == null || weaponMounting == null) return null;
            if (config.PreventLooting.AllowRackedWeaponSwap) return null;
            BaseEntity entity = rack as BaseEntity;
            if (PreventLootingCheckRackedWeapon(player, entity)) return null;
            return false;
        }

        private object OnRackedWeaponTake(Item weapon, BasePlayer player, WeaponRack rack)
        {
            if (!config.PreventLooting.Enabled || rack == null || player == null || weapon == null) return null;
            if (config.PreventLooting.AllowRackedWeaponTake) return null;
            BaseEntity entity = rack as BaseEntity;
            if (PreventLootingCheckRackedWeapon(player, entity)) return null;
            return false;
        }

        private object OnRackedWeaponUnload(Item weapon, BasePlayer player, WeaponRack rack)
        {
            if (!config.PreventLooting.Enabled || rack == null || player == null || weapon == null) return null;
            if (config.PreventLooting.AllowRackedWeaponUnload) return null;
            BaseEntity entity = rack as BaseEntity;
            if (PreventLootingCheckRackedWeapon(player, entity)) return null;
            return false;
        }

        private object OnRackedWeaponLoad(Item weapon, ItemDefinition ammoItem, BasePlayer player, WeaponRack rack)
        {
            if (!config.PreventLooting.Enabled || rack == null || player == null || weapon == null) return null;
            if (config.PreventLooting.AllowRackedWeaponLoad) return null;
            BaseEntity entity = rack as BaseEntity;
            if (PreventLootingCheckRackedWeapon(player, entity)) return null;
            return false;
        }

        private object OnOvenToggle(BaseOven oven, BasePlayer player)
        {
            if (!config.PreventLooting.Enabled || oven == null || player == null) return null;
            if (config.PreventLooting.AllowOvenToggle) return null;
            BaseEntity entity = oven as BaseEntity;
            if (PreventLootingCheckHelper(player, entity)) return null;
            if (entity.OwnerID == player.userID) return null;
            if (entity.OwnerID != 0 && entity.OwnerID != player.userID && !PreventLootingIsFriend(entity.OwnerID, player.userID))
            {
                if ((config.PreventLooting.UseCupboard || config.PreventLooting.UseCupboardAuth || config.PreventLooting.OnlyInCupboardRange))
                    if (PreventLootingCheckAuthCupboard(entity, player)) return null;
                SendReply(player, GetMessage("OnTryOnOff", player.UserIDString));
                return false;
            }
            return null;
        }

        private object CanPickupEntity(BasePlayer player, BaseCombatEntity ent)
        {
            if (!config.PreventLooting.Enabled || ent == null || player == null) return null;
            if (config.PreventLooting.AllowPickup) return null;
            BaseEntity entity = ent as BaseEntity;
            if (PreventLootingCheckHelper(player, entity)) return null;
            if (entity.OwnerID != 0 && entity.OwnerID != player.userID && !PreventLootingIsFriend(entity.OwnerID, player.userID))
            {
                if ((config.PreventLooting.UseCupboard || config.PreventLooting.UseCupboardAuth || config.PreventLooting.OnlyInCupboardRange))
                    if (PreventLootingCheckAuthCupboard(entity, player)) return null;
                SendReply(player, GetMessage("OnTryPickup", player.UserIDString));
                return false;
            }
            return null;
        }

        private object CanAdministerVending(BasePlayer player, VendingMachine machine)
        {
            if (!config.PreventLooting.Enabled || machine == null || player == null) return null;
            if (config.PreventLooting.AllowLootStorage) return null;
            BaseEntity entity = machine as BaseEntity;
            if (PreventLootingCheckHelper(player, entity)) return null;
            if (config.PreventLooting.UsePermissions && !permission.UserHasPermission(entity.OwnerID.ToString(), StoragePerm)) return null;
            if (entity.OwnerID == player.userID) return null;
            if (config.PreventLooting.UseExcludeEntities && (config.PreventLooting.ExcludedShortPrefabNames?.Contains(entity.ShortPrefabName) == true || config.PreventLooting.ExcludeEntities?.Contains(entity.ShortPrefabName) == true)) return null;
            if (PreventLootingIsFriend(entity.OwnerID, player.userID)) return null;
            if (entity.OwnerID != player.userID && entity.OwnerID != 0)
            {
                if ((config.PreventLooting.UseCupboard || config.PreventLooting.UseCupboardAuth || config.PreventLooting.OnlyInCupboardRange))
                    if (PreventLootingCheckAuthCupboard(entity, player)) return null;
                SendReply(player, GetMessage("OnTryLootEntity", player.UserIDString));
                return false;
            }
            return null;
        }

        // PreventLooting helper methods
        private bool PreventLootingCheckHelper(BasePlayer player, BaseEntity entity)
        {
            if (entity == null || player == null) return true;
            if (player.IsAdmin && config.PreventLooting.AdminCanLoot) return true;
            if (config.PreventLooting.UsePermissions && permission.UserHasPermission(player.userID.ToString(), AdmPerm)) return true;
            if (config.PreventLooting.UseZoneManager && ZoneManager != null)
            {
                if (PreventLootingCheckDynamicPVP(player)) return true;
                var zones = (string[])ZoneManager.Call("GetPlayerZoneIDs", player);
                if (zones != null)
                {
                    if (!config.PreventLooting.ZoneManagerIncludeMode)
                    {
                        foreach (var zoneID in config.PreventLooting.ZoneIDs)
                        {
                            if ((bool)ZoneManager.Call("isPlayerInZone", zoneID, player)) return true;
                        }
                    }
                    else
                    {
                        bool inAnyZone = false;
                        foreach (var zoneID in config.PreventLooting.ZoneIDs)
                        {
                            if ((bool)ZoneManager.Call("isPlayerInZone", zoneID, player))
                            {
                                inAnyZone = true;
                                break;
                            }
                        }
                        if (!inAnyZone) return true;
                    }
                }
            }
            if (entity is SupplyDrop) return true;
            return false;
        }

        private bool PreventLootingCheckDynamicPVP(BasePlayer player)
        {
            if (config.PreventLooting.UseDynamicPVP && DynamicPVP != null)
            {
                var zones = (string[])ZoneManager?.Call("GetPlayerZoneIDs", player);
                if (zones != null)
                {
                    foreach (var zoneID in zones)
                    {
                        if ((bool)DynamicPVP.Call("IsDynamicPVPZone", zoneID)) return true;
                    }
                }
            }
            return false;
        }

        private BaseEntity PreventLootingCheckParent(BaseEntity entity)
        {
            if (entity.HasParent())
            {
                BaseEntity parententity = entity.GetParentEntity();
                if (parententity is MiningQuarry)
                {
                    entity.OwnerID = parententity.OwnerID;
                    entity = parententity;
                }
            }
            return entity;
        }

        private bool PreventLootingIsVendingOpen(BasePlayer player, BaseEntity entity)
        {
            if (entity is VendingMachine)
            {
                VendingMachine shopFront = entity as VendingMachine;
                if (shopFront.PlayerInfront(player)) return true;
            }
            return false;
        }

        private bool PreventLootingIsDropBoxOpen(BasePlayer player, BaseEntity entity)
        {
            if (entity is DropBox)
            {
                DropBox dropboxFront = entity as DropBox;
                if (dropboxFront.PlayerInfront(player)) return true;
            }
            return false;
        }

        private bool PreventLootingIsFriend(ulong friendid, ulong playerid)
        {
            if (friendid == playerid)
            {
                return true;
            }

            if (config.options.Clans && IsNativeClanMate(friendid, playerid))
            {
                return true;
            }

            if (config.options.Clans && Clans != null && Convert.ToBoolean(Clans?.Call("IsClanMember", friendid, playerid)))
            {
                return true;
            }

            if (config.PreventLooting.UseFriendsAPI && config.options.Friends && Friends != null)
            {
                var fr = Friends.CallHook("AreFriends", friendid, playerid);
                if (fr != null && (bool)fr)
                {
                    return true;
                }
            }

            if (config.PreventLooting.UseTeams && config.options.Teams)
            {
                if (RelationshipManager.ServerInstance.playerToTeam.TryGetValue(friendid, out var ownerTeam) && ownerTeam.members.Contains(playerid))
                {
                    return true;
                }

                if (RelationshipManager.ServerInstance.playerToTeam.TryGetValue(playerid, out var playerTeam) && playerTeam.members.Contains(friendid))
                {
                    return true;
                }
            }

            return false;
        }

        private object PreventLootingCanLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (PreventLootingCheckHelper(player, entity)) return null;
            BaseEntity childentity = entity;

            if (childentity.HasParent())
            {
                var parent = childentity.GetParentEntity();
                if (parent is Tugboat tug && tug.OwnerID == 1234)
                    return null;
            }
            entity = PreventLootingCheckParent(entity);
            if (entity.OwnerID == player.userID) return null;
            if (config.PreventLooting.UsePermissions && !permission.UserHasPermission(entity.OwnerID.ToString(), StoragePerm)) return null;
            if (config.PreventLooting.UseExcludeEntities && (config.PreventLooting.ExcludedShortPrefabNames?.Contains(entity.ShortPrefabName) == true || config.PreventLooting.ExcludeEntities?.Contains(entity.ShortPrefabName) == true)) return null;
            if (PreventLootingIsVendingOpen(player, entity) || PreventLootingIsDropBoxOpen(player, entity)) return null;
            if (PreventLootingIsFriend(entity.OwnerID, player.userID)) return null;
            if (_preventLootingData != null && _preventLootingData.Data.ContainsKey(entity.net.ID.Value))
            {
                if (childentity == entity)
                {
                    if (_preventLootingData.Data[entity.net.ID.Value].Share.Contains(player.userID) || _preventLootingData.Data[entity.net.ID.Value].Share.Contains(0)) return null;
                }
                else
                {
                    if (_preventLootingData.Data[entity.net.ID.Value].Quarry.ContainsKey(childentity.ShortPrefabName))
                        if (_preventLootingData.Data[entity.net.ID.Value].Quarry[childentity.ShortPrefabName].Contains(player.userID) || _preventLootingData.Data[entity.net.ID.Value].Quarry[childentity.ShortPrefabName].Contains(0)) return null;
                }
            }
            if (entity.OwnerID != player.userID && entity.OwnerID != 0)
            {
                if ((config.PreventLooting.UseCupboard || config.PreventLooting.UseCupboardAuth || config.PreventLooting.OnlyInCupboardRange))
                    if (PreventLootingCheckAuthCupboard(entity, player)) return null;
                SendReply(player, GetMessage("OnTryLootEntity", player.UserIDString));
                return false;
            }
            return null;
        }

        private bool PreventLootingCheckRackedWeapon(BasePlayer player, BaseEntity entity)
        {
            if (PreventLootingCheckHelper(player, entity)) return true;
            if (entity.OwnerID == player.userID) return true;
            if (config.PreventLooting.UsePermissions && !permission.UserHasPermission(entity.OwnerID.ToString(), StoragePerm)) return true;
            if (config.PreventLooting.UseExcludeEntities && (config.PreventLooting.ExcludedShortPrefabNames?.Contains(entity.ShortPrefabName) == true || config.PreventLooting.ExcludeEntities?.Contains(entity.ShortPrefabName) == true)) return true;
            if (PreventLootingIsFriend(entity.OwnerID, player.userID)) return true;
            if (_preventLootingData != null && _preventLootingData.Data.ContainsKey(entity.net.ID.Value))
            {
                if (_preventLootingData.Data[entity.net.ID.Value].Share.Contains(player.userID) || _preventLootingData.Data[entity.net.ID.Value].Share.Contains(0)) return true;
            }
            if (entity.OwnerID != player.userID && entity.OwnerID != 0)
            {
                if (config.PreventLooting.UseCupboard || config.PreventLooting.OnlyInCupboardRange)
                    if (PreventLootingCheckAuthCupboard(entity, player)) return true;
                SendReply(player, GetMessage("OnTryLootWeaponRack", player.UserIDString));
                return false;
            }
            return true;
        }

        private bool PreventLootingCheckAuthCupboard(object ent, BasePlayer player)
        {
            BaseEntity entity = ent as BaseEntity;
            ulong ownerid = 0;
            string type = "";

            if (ent is BuildingPrivlidge cupboard)
            {
                if (cupboard.IsAuthed(player)) return true;
            }
            if (ent is BaseCombatEntity)
                if ((ent as BaseCombatEntity).pickup.enabled) type = "pickup";
            if (ent is StorageContainer || ent is MiningQuarry || ent is WeaponRack || ent is ContainerIOEntity || ent is IndustrialCrafter)
            {
                ownerid = entity.OwnerID;
                type = "storage";
            }
            else if (ent is BasePlayer)
            {
                ownerid = (ent as BasePlayer).userID;
                type = "player";
            }
            else if (ent is LootableCorpse)
            {
                ownerid = (ent as LootableCorpse).playerSteamID;
                type = "corpse";
            }
            else if (ent is DroppedItemContainer)
            {
                ownerid = (ent as DroppedItemContainer).playerSteamID;
                if (entity.name.Contains("item_drop_backpack")) type = "backpack";
                else if (entity.name.Contains("droppedbackpack")) type = "backpackplugin";
            }
            BuildingPrivlidge bprev = player.GetBuildingPrivilege(new OBB(entity.transform.position, entity.transform.rotation, entity.bounds));
            if (config.PreventLooting.OnlyInCupboardRangeInclude.Contains(type) && bprev == null)
            {
                if (config.PreventLooting.OnlyInCupboardRange) return true;
                if (!config.PreventLooting.OnlyInCupboardRange) return false;
            }
            if ((config.PreventLooting.UseCupboard || config.PreventLooting.UseCupboardAuth) && config.PreventLooting.UseCupboardInclude.Contains(type) && bprev != null)
            {
                if (ownerid != 0)
                {
                    if (bprev.IsAuthed(player) && bprev.authorizedPlayers.Contains(ownerid)) return true;
                }
                else
                {
                    if (bprev.IsAuthed(player)) return true;
                }
            }
            return false;
        }

        private bool PreventLootingFindEntityFromRay(BasePlayer player, out object success)
        {
            success = null;
            RaycastHit hit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out hit, 2.2f))
                return false;
            success = hit.GetEntity();
            return true;
        }

        private IPlayer PreventLootingCheckPlayer(BasePlayer player, string[] args)
        {
            if (args == null || args.Length == 0 || args[0].Length < 4 || (args[0].StartsWith("765611") && args[0].Length < 17))
            {
                SendReply(player, GetMessage("InvalidSearch", player.UserIDString));
                return null;
            }

            // Convert IEnumerable to List without LINQ
            var playerEnum = covalence.Players.FindPlayers(args[0]);
            List<IPlayer> playerlist = new List<IPlayer>();
            foreach (var pl in playerEnum)
            {
                playerlist.Add(pl);
            }
            
            if (playerlist.Count > 1)
            {
                var message = "<color=#FF0000>" + GetMessage("MultiplePlayerFind", player.UserIDString) + "</color>\n";
                int i = 0;
                foreach (var pl in playerlist)
                {
                    i++;
                    message += string.Format("{0}. <color=#FFA500>{1}</color> ({2})\n\r", i, pl.Name, pl.Id);
                }
                SendReply(player, message);
                return null;
            }
            var player0 = covalence.Players.FindPlayer(args[0]);
            if (player0 == null)
            {
                SendReply(player, string.Format(GetMessage("PlayerNotFound", player.UserIDString), "<color=#FFA500>" + args[0] + "</color>"));
                return null;
            }
            return player0;
        }
        #endregion PreventLooting Hooks

        private object OnSprayCreate(SprayCan sc, Vector3 pos, Quaternion rot)
        {
            if (sc == null || sc.IsDestroyed) return null;
            BasePlayer player = sc.GetOwnerPlayer();
            if (player == null || player.IsDestroyed) return null;
            if (player.InSafeZone()) return true;
            return null;
        }

        private void OnEntitySpawned(BaseOven oven)
        {
            if (config.options.disableBaseOvenSplash && oven != null && oven.OwnerID.IsSteamId())
            {
                oven.disabledBySplash = false;
            }
            if (config.options.Loot.Locks && oven != null && oven is StorageContainer c)
            {
                OnEntitySpawned(c);
            }
        }

        private void OnEntitySpawned(MLRSRocket rocket)
        {
            if (rocket == null || rocket.IsDestroyed) return;
            using var systems = Pool.Get<PooledList<MLRS>>();
            Vis.Entities(rocket.transform.position, 15f, systems, -1);
            if (systems.Count == 0 || CheckIsEventTerritory(systems[0].TrueHitPos)) return;
            if (systems[0].rocketOwnerRef.Get(true) is not BasePlayer owner) return;
            rocket.creatorEntity = owner;
            rocket.OwnerID = owner.userID;
        }

        private bool CheckIsEventTerritory(Vector3 position)
        {
            if (AbandonedBases != null && AbandonedBases.IsLoaded && Convert.ToBoolean(AbandonedBases?.Call("EventTerritory", position))) return true;
            if (RaidableBases != null && RaidableBases.IsLoaded && Convert.ToBoolean(RaidableBases?.Call("EventTerritory", position))) return true;
            return false;
        }

        private bool IsSkinExclusion(BaseEntity entity) => entity != null && entity.skinID != 0 && config.options.SkinExclusions.Count > 0 && config.options.SkinExclusions.Contains(entity.skinID);

        private object SamSiteHelper(BaseEntity attacker, BaseEntity entity)
        {
            if (useZones)
            {
                using var entityLocations = GetLocationKeys(entity);
                using var initiatorLocations = GetLocationKeys(attacker);

                // check for exclusion zones (zones with no rules mapped)
                if (CheckExclusion(entityLocations, initiatorLocations, false))
                {
                    if (trace) Trace($"OnSamSiteTarget allowed {entity.ShortPrefabName} to be targetted; exclusion of zone found.", 1);
                    return null;
                }
            }

            // check for exclusions in entity groups
            if (CheckExclusion(attacker))
            {
                if (trace) Trace($"OnSamSiteTarget allowed {entity.ShortPrefabName} to be targetted; exclusion found in entity group.", 1);
                return null;
            }

            if (trace && entity is BasePlayer) Trace($"SamSitesIgnorePlayers blocked {entity.ShortPrefabName} from being targetted.", 1);
            else if (trace && entity is MLRSRocket) Trace($"SamSitesIgnoreMLRS blocked {entity.ShortPrefabName} from being targetted.", 1);
            if (attacker is SamSite ss)
            {
                ss.CancelInvoke(ss.WeaponTick);
            }
            return true;
        }

        // Check if entity can be targeted
        private object OnEntityEnter(TargetTrigger trigger, BasePlayer target)
        {
            if (trigger == null || target == null)
            {
                return null;
            }

            var entity = trigger.gameObject.ToBaseEntity();
            if (!entity.IsValid())
            {
                return null;
            }

            return OnEntityEnterInternal(entity, target);
        }

        private object OnEntityEnter(TriggerEnterTimer trigger, BaseEntity target)
        {
            if (trigger == null || target == null)
            {
                return null;
            }

            var entity = trigger.gameObject.ToBaseEntity();
            if (!entity.IsValid())
            {
                return null;
            }

            if (Interface.CallHook("CanEntityBeTargeted", new object[] { target, entity }) is bool val)
            {
                return val ? (object)null : true;
            }

            if (entity != null && IsSkinExclusion(entity))
            {
                //if (trace) Trace($"Target is {target}; allow and return -> {entity} skin ID {entity.skinID}", 1);
                return null;
            }

            RuleSet ruleSet = GetRuleSet(target, entity);

            if (ruleSet == null)
            {
                return null;
            }
            
            if ((ruleSet._flags & RuleFlags.HopperCannotTargetEnemyLoot) != 0 && entity is Hopper)
            {
                DroppedItem di = target as DroppedItem;
                if (di != null)
                {
                    if (di.DroppedBy != 0 && !di.DroppedBy.IsSteamId())
                    {
                        if (trace) Trace($"Dropped item does not belong to a player; allow and return", 2);
                        return null;
                    }
                    if (di.DroppedBy == 0 || di.DroppedBy == entity.OwnerID || IsAuthed(di, entity))
                    {
                        if (trace) Trace($"{entity} is authorized to loot the dropped item; allow and return", 2);
                        return null;
                    }
                }

                PlayerCorpse corpse = target as PlayerCorpse;
                if (corpse != null)
                {
                    if (corpse.playerSteamID != 0 && !corpse.playerSteamID.IsSteamId())
                    {
                        if (trace) Trace($"Corpse does not belong to a player; allow and return", 2);
                        return null;
                    }
                    if (corpse.playerSteamID == 0 || corpse.playerSteamID == entity.OwnerID || corpse.playerSteamID.IsSteamId() && IsAuthed(corpse, entity))
                    {
                        if (trace) Trace($"{entity} is authorized to loot the corpse; allow and return", 2);
                        return null;
                    }
                }

                if (useZones)
                {
                    using var entityLocations = GetLocationKeys(target);
                    using var initiatorLocations = GetLocationKeys(entity);

                    // check for exclusion zones (zones with no rules mapped)
                    if (CheckExclusion(entityLocations, initiatorLocations, trace))
                    {
                        return null;
                    }
                }

                if (CheckExclusion(target, entity))
                {
                    if (trace) Trace($"{entity} and {target} are both excluded in entity groups", 2);
                    return null;
                }

                if (CheckExclusion(entity))
                {
                    if (trace) Trace($"{entity} is excluded in entity groups", 2);
                    return null;
                }

                return true;
            }

            return null;
        }

        private object OnEntityEnterInternal(BaseEntity entity, BasePlayer target)
        {
            if (Interface.CallHook("CanEntityBeTargeted", new object[] { target, entity }) is bool val)
            {
                return val ? (object)null : true;
            }
            
            if (entity != null && IsSkinExclusion(entity))
            {
                //if (trace) Trace($"Target is {target}; allow and return -> {entity} skin ID {entity.skinID}", 1);
                return null;
            }

            RuleSet ruleSet = GetRuleSet(target, entity);

            if (ruleSet == null)
            {
                return null;
            }

            if (config.PlayersTriggerTurrets && entity.OwnerID == 0uL && target.userID.IsSteamId() && (entity is FlameTurret or AutoTurret) && !entity.HasParent())
            {
                if (entity is NPCAutoTurret && (ruleSet._flags & RuleFlags.SafeZoneTurretsIgnorePlayers) != 0 && target.InSafeZone()) return true;
                return null;
            }

            var isAutoTurret = entity is AutoTurret;

            if (!target.userID.IsSteamId())
            {
                if (isAutoTurret)
                {
                    return (ruleSet._flags & (entity.OwnerID == 0 ? RuleFlags.StaticTurretsIgnoreScientist : RuleFlags.TurretsIgnoreScientist)) != 0 ? true : (object)null;
                }
                else
                {
                    return (ruleSet._flags & RuleFlags.TrapsIgnoreScientist) != 0 ? true : (object)null;
                }
            }
            else if (entity is NPCAutoTurret && entity.OwnerID == 0)
            {
                return (ruleSet._flags & RuleFlags.SafeZoneTurretsIgnorePlayers) != 0 ? true : (object)null;
            }
            else if (isAutoTurret && (ruleSet._flags & (entity.OwnerID == 0 ? RuleFlags.StaticTurretsIgnorePlayers : RuleFlags.TurretsIgnorePlayers)) != 0 || !isAutoTurret && (ruleSet._flags & RuleFlags.TrapsIgnorePlayers) != 0)
            {
                if (isAutoTurret && IsFunTurret(entity as AutoTurret))
                {
                    return null;
                }

                if (useZones)
                {
                    using var entityLocations = GetLocationKeys(target);
                    using var initiatorLocations = GetLocationKeys(entity);

                    // check for exclusion zones (zones with no rules mapped)
                    if (CheckExclusion(entityLocations, initiatorLocations, trace))
                    {
                        return null;
                    }
                }

                // check for exclusions in entity group
                if (CheckExclusion(target, entity) || CheckExclusion(entity))
                {
                    return null;
                }

                return true;
            }

            return null;
        }

        private object OnTurretTarget(AutoTurret turret, BradleyAPC target)
        {
            if (turret == null || target == null) return null;
            RuleSet ruleSet = GetRuleSet(target, turret);

            if (ruleSet == null)
            {
                return null;
            }

            if ((ruleSet._flags & RuleFlags.TurretsIgnoreBradley) == 0)
            {
                // flag not set, do nothing
                return null;
            }

            if (useZones)
            {
                using var entityLocations = GetLocationKeys(target);
                using var initiatorLocations = GetLocationKeys(turret);

                // check for exclusion zones (zones with no rules mapped)
                if (CheckExclusion(entityLocations, initiatorLocations, trace))
                {
                    // zone exclusion, do nothing
                    return null;
                }
            }

            // check for exclusions in entity group
            if (CheckExclusion(target, turret))
            {
                // group exclusion, do nothing
                return null;
            }

            // prevent turret from targeting bradley
            return true;
        }

        private object OnTurretTarget(AutoTurret turret, BasePlayer target)
        {
            if (turret == null || target == null) return null;
            return OnEntityEnterInternal(turret, target);
        }

        // ignore players stepping on traps if configured
        private object OnTrapTrigger(BaseTrap trap, GameObject go)
        {
            if (go == null || trap == null || !go.TryGetComponent(out BasePlayer player))
            {
                return null;
            }

            if (Interface.CallHook("CanEntityTrapTrigger", new object[] { trap, player }) is bool val)
            {
                return val ? (object)null : true;
            }

            RuleSet ruleSet = GetRuleSet(player, trap);

            if (ruleSet == null)
            {
                return null;
            }

            if ((player.IsNpc || !player.userID.IsSteamId()) && (ruleSet._flags & RuleFlags.TrapsIgnoreScientist) != 0)
            {
                return true;
            }
            else if (player.userID.IsSteamId() && (ruleSet._flags & RuleFlags.TrapsIgnorePlayers) != 0)
            {
                if (useZones)
                {
                    using var entityLocations = GetLocationKeys(player);
                    using var initiatorLocations = GetLocationKeys(trap);

                    // check for exclusion zones (zones with no rules mapped)
                    if (CheckExclusion(entityLocations, initiatorLocations, false))
                    {
                        return null;
                    }
                }

                if (CheckExclusion(trap))
                {
                    return null;
                }

                if (config.PlayersTriggerTraps && trap.OwnerID == 0uL && !trap.HasParent())
                {
                    return null;
                }

                return true;
            }

            return null;
        }

        private object OnNpcTarget(BaseNpc npc, BasePlayer target) => OnNpcTargetInternal(npc, target);

        private object OnNpcTarget(BaseNPC2 npc, BasePlayer target) => OnNpcTargetInternal(npc, target);

        private bool isServerStartingUp = true;

        private object OnNpcTargetInternal(BaseEntity npc, BasePlayer target)
        {
            if (isServerStartingUp)
            {
                return true;
            }

            if (target == null)
            {
                return true;
            }

            if (!target.userID.IsSteamId() || !target.IsSleeping())
            {
                return null;
            }

            if (npc == null)
            {
                return true;
            }

            RuleSet ruleSet = GetRuleSet(target, npc);

            if (ruleSet == null || !IsAnimalsIgnoringSleepers(ruleSet))
            {
                return null;
            }

            if (useZones)
            {
                using var entityLocations = GetLocationKeys(target);
                using var initiatorLocations = GetLocationKeys(npc);

                // check for exclusion zones (zones with no rules mapped)
                if (CheckExclusion(entityLocations, initiatorLocations, false))
                {
                    return null;
                }
            }

            return true;
        }

        private readonly Dictionary<uint, string> _typeNameLookup = new();
        private string GetTypeName(BaseEntity entity, string defaultValue = "Unknown")
        {
            if (entity == null)
            {
                return defaultValue;
            }

            if (!_typeNameLookup.TryGetValue(entity.prefabID, out string name))
            {
                BaseEntity prefab = entity.LookupPrefab();
                if (prefab == null)
                {
                    prefab = entity;
                }
                _typeNameLookup[entity.prefabID] = name = prefab.GetType().Name;
            }

            return name;
        }

        private bool IsExcavatorEntity(BaseEntity entity)
        {
            if (entity == null)
            {
                return false;
            }

            string sp = entity.ShortPrefabName ?? string.Empty;
            string pn = entity.PrefabName ?? string.Empty;

            if (sp.IndexOf("excavator", StringComparison.OrdinalIgnoreCase) >= 0 ||
                pn.IndexOf("excavator", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            // Check root parent for excavator context
            BaseEntity root = entity;
            for (int i = 0; i < 25 && root != null && root.HasParent(); i++)
            {
                root = root.GetParentEntity();
            }

            if (root != null)
            {
                string rsp = root.ShortPrefabName ?? string.Empty;
                string rpn = root.PrefabName ?? string.Empty;
                if (rsp.IndexOf("excavator", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    rpn.IndexOf("excavator", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        // Check for exclusions in entity groups (attacker)
        private bool CheckExclusion(BaseEntity attacker)
        {
            string attackerName = GetTypeName(attacker);
            foreach (var group in config.groups)
            {
                if (group.IsExclusion(attacker.ShortPrefabName) || group.IsExclusion(attackerName))
                {
                    return true;
                }
            }

            return false;
        }

        // Check for exclusions in entity groups (target, attacker)
        private bool CheckExclusion(BaseEntity target, BaseEntity attacker)
        {
            string targetName = GetTypeName(target);
            string attackerName = GetTypeName(attacker);

            foreach (var vicGroup in config.groups)
            {
                if (vicGroup.IsMember(target.ShortPrefabName) || vicGroup.IsExclusion(targetName))
                {
                    // Target is in a relevant group; now check attacker exclusions
                    foreach (var atkGroup in config.groups)
                    {
                        if (atkGroup.IsExclusion(attacker.ShortPrefabName) || atkGroup.IsExclusion(attackerName))
                        {
                            return true; // Exclusion found for attacker
                        }
                    }

                    return false; // Target is in a group, but no attacker exclusion found
                }
            }

            return false; // Target is not in any member or exclusion group
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private RuleSet GetRuleSet(PooledList<string> vicLocations, PooledList<string> atkLocations)
        {
            bool hasAtkLocations = !atkLocations.IsNullOrEmpty();
            bool hasVicLocations = !vicLocations.IsNullOrEmpty();

            if (shareRedirectDudEnabled && (hasAtkLocations ^ hasVicLocations))
            {
                return dudRuleSet;
            }

            if (!hasVicLocations)
            {
                if (trace) Trace("No shared locations with attacker (empty locations for victim) - no exclusions", 3);
                return currentRuleSet;
            }

            if (!hasAtkLocations)
            {
                if (trace) Trace("No shared locations with victim (empty locations for attacker) - no exclusions", 3);
                return currentRuleSet;
            }

            if (trace)
            {
                string str1 = ConcatenateListOrDefault(vicLocations, "empty");
                string str2 = ConcatenateListOrDefault(atkLocations, "empty");

                Trace($"Beginning RuleSet lookup for [{str1}] and [{str2}]", 2);
            }

            RuleSet ruleSet = currentRuleSet;

            using var sharedLocations = GetSharedLocations(vicLocations, atkLocations);

            if (trace)
            {
                string str = ConcatenateListOrDefault(sharedLocations, "none");

                Trace($"Shared locations: {str}", 3);
            }

            if (sharedLocations.Count > 0)
            {
                using var names = Pool.Get<PooledList<string>>();
                foreach (var loc in sharedLocations)
                {
                    if (config.mappings.TryGetValue(loc, out string mapping) || data.mappings.TryGetValue(loc, out mapping))
                    {
                        names.Add(mapping);
                    }
                }

                using var sets = Pool.Get<PooledList<RuleSet>>();
                foreach (var name in names)
                {
                    if (ruleSetByNameDictionary.TryGetValue(name, out RuleSet set))
                    {
                        sets.Add(set);
                    }
                }

                if (trace)
                {
                    Trace($"Found {names.Count} location names, with {sets.Count} mapped RuleSets", 3);
                }

                if (sets.Count == 0 && config.mappings.TryGetValue(AllZones, out var val) && ruleSetByNameDictionary.TryGetValue(val, out RuleSet all))
                {
                    sets.Add(all);

                    if (trace)
                    {
                        Trace("Found allzones mapped RuleSet in config", 3);
                    }
                }

                if (sets.Count == 0 && data.mappings.TryGetValue(AllZones, out val) && ruleSetByNameDictionary.TryGetValue(val, out all))
                {
                    sets.Add(all);

                    if (trace)
                    {
                        Trace("Found allzones mapped RuleSet in data file", 3);
                    }
                }

                if (sets.Count > 1)
                {
                    string ruleSetNames = ConcatenateRuleSetNames(sets);

                    if (trace)
                    {
                        Trace($"WARNING: Found multiple RuleSets: {ruleSetNames}", 3);
                    }

                    Puts(ruleSetNames);
                }

                if (sets.Count > 0)
                {
                    ruleSet = sets[0];

                    if (trace)
                    {
                        Trace($"Found RuleSet: {ruleSet?.name ?? "null"}", 3);
                    }
                }
            }

            if (ruleSet == null)
            {
                ruleSet = currentRuleSet;

                if (trace)
                {
                    Trace($"No RuleSet found; assigned current global RuleSet: {ruleSet?.name ?? "null"}", 3);
                }
            }

            return ruleSet;
        }

        private RuleSet GetRuleSet(BaseEntity e0, BaseEntity e1)
        {
            using var vic = GetLocationKeys(e0);
            using var atk = GetLocationKeys(e1);
            return GetRuleSet(vic, atk);
        }

        // get locations shared between the two passed location lists
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
        private PooledList<string> GetSharedLocations(PooledList<string> e0Locations, PooledList<string> e1Locations)
        {
            var sharedLocations = Pool.Get<PooledList<string>>();

            if (e0Locations == null || e1Locations == null || e0Locations.Count == 0 || e1Locations.Count == 0)
                return sharedLocations;

            foreach (string loc in e0Locations)
            {
                if (e1Locations.Contains(loc) && (config.HasMapping(loc) || data.HasMapping(loc)))
                {
                    sharedLocations.Add(loc);
                }
            }

            return sharedLocations;
        }

        // Check exclusion for given entity locations
        private bool CheckExclusion(PooledList<string> e0Locations, PooledList<string> e1Locations, bool trace)
        {
            if (e0Locations == null || e1Locations == null)
            {
                if (trace) Trace("No shared locations (empty location) - no exclusions", 3);
                return false;
            }
            if (excludeAllZones)
            {
                if (trace) Trace("All zones are excluded via 'AllZones' mapping. Exclusion found.", 3);
                return true;
            }
            using var sharedLocations = GetSharedLocations(e0Locations, e1Locations);
            if (trace)
            {
                string action1 = ConcatenateListOrDefault(e0Locations, "empty");
                string action2 = ConcatenateListOrDefault(e1Locations, "empty");
                string action3 = ConcatenateListOrDefault(sharedLocations, "none");
                Trace($"Checking exclusions between [{action1}] and [{action2}]", 2);
                Trace($"Shared locations: {action3}", 3);
            }
            if (sharedLocations.Count > 0)
            {
                foreach (string loc in sharedLocations)
                {
                    if (exclusionLocationsSet.Contains(loc))
                    {
                        if (trace) Trace($"Found exclusion mapping for location: {loc}", 3);
                        return true;
                    }
                }
            }
            if (trace) Trace("No shared locations, or no matching exclusion mapping - no exclusions", 3);
            return false;
        }

        private Dictionary<string, string> _mappings = new();
        private void SetExposedMappings()
        {
            _mappings.Clear();
            GetMappingsDictionaryNoAlloc(_mappings);
        }

        // add or update a mapping
        private Timer _auMappingTimer;
        private bool AddOrUpdateMapping(string key, string ruleset)
        {
            if (string.IsNullOrEmpty(key) || config == null || data == null || data.mappings == null || ruleset == null || (ruleset != "exclude" && !config.ruleSets.Exists(r => r.name == ruleset)))
            {
                return false;
            }

            data.mappings[key] = ruleset;
            TryBuildExclusionMappings();
            SetUseZones();
            
            if (_auMappingTimer is { Destroyed: false }) _auMappingTimer.Reset();
            else _auMappingTimer = timer.Once(1f, () =>
            {
                SaveData();
                SetExposedMappings();
                Interface.CallHook("OnUpdatedMappings", _mappings);
            });

            return true;
        }

        // remove a mapping
        private Timer _removeMappingTimer;
        private bool RemoveMapping(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (data.mappings.Remove(key))
            {
                if (_removeMappingTimer is { Destroyed: false }) _removeMappingTimer.Reset();
                else _removeMappingTimer = timer.Once(1f, () =>
                {
                    SaveData();
                    SetExposedMappings();
                    Interface.CallHook("OnRemovedMappings", _mappings);
                });
                SetUseZones();
                return true;
            }
            return false;
        }

        // remove a list of mappings, optionally add removed mappings to results
        private bool RemoveMappings(List<string> keys, List<string> results = null)
        {
            bool ret = false;
            if (!keys.IsNullOrEmpty())
            {
                foreach (var key in keys)
                {
                    if (RemoveMapping(key))
                    {
                        ret = true;
                        results?.Add(key);
                    }
                }
            }
            return ret;
        }

        // get all mappings
        private void GetMappingsDictionaryNoAlloc(Dictionary<string, string> dict)
        {
            foreach (var pair in data.mappings)
            {
                dict[pair.Key] = pair.Value;
            }
            foreach (var pair in config.mappings)
            {
                dict[pair.Key] = pair.Value;
            }
        }

        private void GetMappingsListNoAlloc(List<string> list)
        {
            foreach (var key in config.mappings.Keys)
            {
                if (!list.Contains(key)) list.Add(key);
            }
            foreach (var key in data.mappings.Keys)
            {
                if (!list.Contains(key)) list.Add(key);
            }
        }

        #endregion

        #region Messaging
        private void Message(BasePlayer player, string key, params object[] args)
        {
            string message = BuildMessage(player, key, args);
            if (string.IsNullOrEmpty(message)) return;
            SendReply(player, message);
        }

        private void Message(IPlayer user, string key, params object[] args)
        {
            string message = BuildMessage(user.Object as BasePlayer, key, args);
            if (string.IsNullOrEmpty(message)) return;
            user.Reply(RemoveFormatting(message));
        }

        // build message string
        private string BuildMessage(BasePlayer player, string key, params object[] args)
        {
            string message = GetMessage(key, player?.UserIDString);
            if (string.IsNullOrEmpty(message)) return string.Empty;
            if (args.Length > 0) message = string.Format(message, args);
            string type = key.Split('_')[0];
            if (player != null)
            {
                string size = GetMessage("Format_" + type + "Size");
                string color = GetMessage("Format_" + type + "Color");
                return WrapSize(size, WrapColor(color, message));
            }
            else
            {
                string color = GetMessage("Format_" + type + "Color");
                return WrapColor(color, message);
            }
        }

        // prints the value of an Option
        private void PrintValue(ConsoleSystem.Arg arg, string text, bool value)
        {
            SendReply(arg, WrapSize(GetMessage("Format_NotifySize"), WrapColor(GetMessage("Format_NotifyColor"), text + ": ") + value));
        }

        // wrap string in <size> tag, handles parsing size string to integer
        private string WrapSize(string size, string input)
        {
            return int.TryParse(size, out var i) ? WrapSize(i, input) : input;
        }

        // wrap a string in a <size> tag with the passed size
        private string WrapSize(int size, string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            return "<size=" + size + ">" + input + "</size>";
        }

        // wrap a string in a <color> tag with the passed color
        private string WrapColor(string color, string input)
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(color))
                return input;
            return "<color=" + color + ">" + input + "</color>";
        }

        // show usage information
        private void ShowUsage(IPlayer user) => user.Message(RemoveFormatting(usageString));

        public string RemoveFormatting(string source) => source.Contains('>') ? Regex.Replace(source, "<.*?>", string.Empty) : source;

        // warn that the server is set to PVE mode
        private void WarnPve() => Puts(GetMessage("Warning_PveMode"));

        /// <summary>
        /// Enable ConVar.Server.pve for Steam/browser listing while TruePVE RuleSets own damage.
        /// Vanilla PvP/building reflect is suppressed by Patch_SuppressVanillaPve while handleDamage is on.
        /// </summary>
        private void ApplyGamePveBrowserTag()
        {
            if (config?.options == null) return;
            if (config.options.UseGamePveBrowserTag)
            {
                if (!ConVar.Server.pve)
                {
                    ConVar.Server.pve = true;
                    Puts("Enabled ConVar server.pve for server browser / Steam PVE tag. TruePVE RuleSets still own damage rules.");
                }
            }
            else if (ConVar.Server.pve)
            {
                WarnPve();
            }
        }
        #endregion

        #region Helper Procedures

        private bool RemoveTemporaryZones()
        {
            using var zones = Facepunch.Pool.Get<PooledList<string>>();
            using var mappings = Facepunch.Pool.Get<PooledList<string>>();

            return RemoveTemporaryZones(zones, mappings);
        }

        private bool RemoveTemporaryZones(List<string> zones, List<string> mappings)
        {
            if (ZoneManager == null) 
                return false;

            if (zones.Count == 0)
                ZoneManager.Call("GetZoneIDsNoAlloc", zones);

            if (mappings.Count == 0)
                GetMappingsListNoAlloc(mappings);

            bool any = false;
            foreach (var mapping in mappings)
            {
                if (!zones.Contains(mapping) && mapping.IsNumeric() && RemoveMapping(mapping))
                {
                    any = true;
                }
            }

            return any;
        }

        // get location keys from ZoneManager (zone IDs) or LiteZones (zone names)
        private PooledList<string> GetLocationKeys(BaseEntity entity)
        {
            if (!useZones || entity == null) return null;
            var locations = Pool.Get<PooledList<string>>();
            if (ZoneManager != null && ZoneManager.IsLoaded)
            {
                using var locs = Pool.Get<PooledList<string>>();
                if (entity is BasePlayer player)
                {
                    // BasePlayer fix from chadomat
                    string[] array = (string[])ZoneManager.Call("GetPlayerZoneIDs", new object[] { player });
                    if (array != null && array.Length > 0)
                    {
                        foreach (string loc in array)
                        {
                            if (!string.IsNullOrEmpty(loc) && !locs.Contains(loc))
                            {
                                locs.Add(loc);
                            }
                        }
                    }
                }
                else if (entity.IsValid())
                {
                    string[] array = (string[])ZoneManager.Call("GetEntityZoneIDs", new object[] { entity });
                    if (array != null && array.Length > 0)
                    {
                        foreach (string loc in array)
                        {
                            if (!string.IsNullOrEmpty(loc) && !locs.Contains(loc))
                            {
                                locs.Add(loc);
                            }
                        }
                    }
                }
                if (locs.Count > 0)
                {
                    // Add names into list of ID numbers
                    foreach (string loc in locs)
                    {
                        if (!locations.Contains(loc)) locations.Add(loc);
                        string zname = (string)ZoneManager.Call("GetZoneName", loc);
                        if (!string.IsNullOrEmpty(zname) && !locations.Contains(zname)) locations.Add(zname);
                    }
                }
            }
            if (LiteZones != null && LiteZones.IsLoaded)
            {
                List<string> locs = (List<string>)LiteZones?.Call("GetEntityZones", new object[] { entity });
                if (locs != null && locs.Count > 0)
                {
                    foreach (string loc in locs)
                    {
                        if (!locations.Contains(loc))
                        {
                            locations.Add(loc);
                        }
                    }
                }
            }
            return locations;
        }

        // handle raycast from player (for prodding)
        private bool GetRaycastTarget(BasePlayer player, out BaseEntity closestEntity)
        {
            if (Physics.Raycast(player.eyes.HeadRay(), out var hit, 10f) && hit.GetEntity() is BaseEntity hitEntity)
            {
                closestEntity = hitEntity;
                return closestEntity != null;
            }
            closestEntity = null;
            return false;
        }

        // loop to update current ruleset
        private void TimerLoop(bool firstRun = false)
        {
            config.schedule.ClockUpdate(out var ruleSetName, out currentBroadcastMessage);
            if (firstRun || currentRuleSet.name != ruleSetName)
            {
                if (string.IsNullOrEmpty(ruleSetName))
                {
                    ruleSetName = config.defaultRuleSet;
                }

                RuleSet ruleSet = config.ruleSets.Find(r => r.name == ruleSetName && r.enabled && !r.IsEmpty());

                if (ruleSet != null)
                {
                    currentRuleSet = ruleSet;
                }

                ValidateCurrentDamageHook();
                if (config.schedule.broadcast && !string.IsNullOrEmpty(currentBroadcastMessage))
                {
                    Server.Broadcast(currentBroadcastMessage, GetMessage("Prefix"));
                    Puts(RemoveFormatting(" Schedule Broadcast: " + currentBroadcastMessage));
                }
            }

            if (config.schedule.enabled)
            {
                scheduleUpdateTimer = timer.Once(config.schedule.useRealtime ? 30f : 3f, () => TimerLoop());
            }
        }

        private void ValidateCurrentDamageHook()
        {
            if (!config.options.handleDamage)
            {
                Unsubscribe(nameof(OnEntityTakeDamage));
                tpveEnabled = false;
                return;
            }
            RuleSet ruleSet = currentRuleSet;
            tpveEnabled = ruleSet != null && ruleSet.enabled && !ruleSet.IsEmpty();
            if (tpveEnabled)
            {
                Subscribe(nameof(OnEntityTakeDamage));
            }
            else
            {
                Unsubscribe(nameof(OnEntityTakeDamage));
            }
        }

        #endregion

        #region Subclasses
        // configuration and data storage container

        private class TwigDamageOptions
        {
            [JsonProperty(PropertyName = "Apply To Twig (when TwigDamage flag is not set", NullValueHandling = NullValueHandling.Ignore)]
            public bool? _Twig = null;

            [JsonProperty(PropertyName = "Apply To Twig (no flag required)")]
            public bool Twig;

            [JsonProperty(PropertyName = "Apply To Wood")]
            public bool Wood;

            [JsonProperty(PropertyName = "Apply To Stone")]
            public bool Stone;

            [JsonProperty(PropertyName = "Apply To Metal")]
            public bool Metal;

            [JsonProperty(PropertyName = "Apply To HQM")]
            public bool HQM;

            [JsonProperty(PropertyName = "Require TwigDamage Flag")]
            public bool RequireTwigDamageFlag;

            [JsonProperty(PropertyName = "Block Damage From Ally")]
            public bool BlockAllyDamage;

            [JsonProperty(PropertyName = "Check And Allow When Authed")]
            public bool CheckAndAllowWhenAuthed;

            [JsonProperty(PropertyName = "Require Owner Online", NullValueHandling = NullValueHandling.Ignore)]
            public bool? _Online = null;

            [JsonProperty(PropertyName = "Block Damage When Owner Is Online")]
            public bool BlockWhenOnline = true;

            [JsonProperty(PropertyName = "Log Offenses")]
            public bool Log;

            [JsonProperty(PropertyName = "Notify Offenders")]
            public bool Notify;

            [JsonProperty(PropertyName = "Reflect Damage Multiplier")]
            public float ReflectDamageMultiplier;

            [JsonProperty(PropertyName = "Multiplier Allows Armor Protection")]
            public bool ReflectDamageProtection = true;

            internal bool Any => Log || Notify || ReflectDamageMultiplier > 0f || Twig || Wood || Stone || Metal || HQM;

            internal bool CanHandleGrade(BuildingGrade.Enum grade, RuleFlags _flags)
            {
                if (grade == BuildingGrade.Enum.Twigs) return Twig;

                bool enabled = grade switch
                {
                    BuildingGrade.Enum.Wood => Wood,
                    BuildingGrade.Enum.Stone => Stone,
                    BuildingGrade.Enum.Metal => Metal,
                    BuildingGrade.Enum.TopTier => HQM,
                    _ => false
                };

                if (!enabled)
                {
                    return false;
                }

                return !RequireTwigDamageFlag || (_flags & RuleFlags.TwigDamage) != 0;
            }
        }

        private class ApartmentOptions
        {
            [JsonProperty(PropertyName = "Enabled")]
            public bool Enabled = true;

            [JsonProperty(PropertyName = "Allow PVP Damage")]
            public bool PVP;

            [JsonProperty(PropertyName = "PVP Requires Attacker To Be Alive")]
            public bool Alive = true;

            [JsonProperty(PropertyName = "PVP Requires Attacker In The Same Room")]
            public bool SameRoom = true;

            [JsonProperty(PropertyName = "Allow Master Key")]
            public bool MasterKey;

            [JsonProperty(PropertyName = "Allow Bribe In Basement")]
            public bool Bribe;

            [JsonProperty(PropertyName = "Allow Break In (Rental Shop)")]
            public bool Shop;

            [JsonProperty(PropertyName = "Allow Break In (Apartment Room)")]
            public bool Room;
        }

        private class ConfigurationOptions
        {
            [JsonProperty(PropertyName = "Entities with these skin ID's can hurt anything")]
            public List<ulong> SkinExclusions = new();

            [JsonProperty(PropertyName = "Apartment Complex")]
            public ApartmentOptions Apartments = new();

            [JsonProperty(PropertyName = "Armor damage (PVE)")]
            public ArmorDamagePVE ArmorDamage = new();

            [JsonProperty(PropertyName = "Loot")]
            public LootSupport Loot = new();

            [JsonProperty(PropertyName = "Reflect PVP Damage Multipliers (0 = disabled, 1 = 100%)")]
            public ReflectDamagePVP Reflect = new();

            [JsonProperty(PropertyName = "TwigDamage (FLAG)")]
            public TwigDamageOptions BlockHandler = new();

            [JsonProperty(PropertyName = "handleDamage")] // (true) enable TruePVE damage handling hooks
            public bool handleDamage = true;

            // Keep ConVar.Server.pve = true for Steam/browser "pve" listing, while TruePVE RuleSets
            // remain the real damage authority (vanilla reflect paths are suppressed while handleDamage is on).
            [JsonProperty(PropertyName = "Use Game server.pve For Server Browser Tag")]
            public bool UseGamePveBrowserTag = true;

            [JsonProperty(PropertyName = "useZones")] // (true) use ZoneManager/LiteZones for zone-specific damage behavior (requires modification of ZoneManager.cs)
            public bool useZones = true;

            [JsonProperty(PropertyName = "Trace To Player Console")]
            public bool PlayerConsole;

            [JsonProperty(PropertyName = "Trace To Server Console")]
            public bool ServerConsole = true;

            [JsonProperty(PropertyName = "Log Trace To File")]
            public bool LogToFile = true;

            [JsonProperty(PropertyName = "Maximum Distance From Player To Trace")]
            public float MaxTraceDistance = 50f;

            [JsonProperty(PropertyName = "Prevent Water From Extinguishing BaseOven")]
            public bool disableBaseOvenSplash;

            [JsonProperty(PropertyName = "Prevent Players From Being Marked Hostile")]
            public bool disableHostility;

            [JsonProperty(PropertyName = "Allow PVP Damage In Deep Sea")]
            public bool DeepSeaPVP;

            [JsonProperty(PropertyName = "Allow Raiding In Deep Sea")]
            public bool DeepSeaRaiding;

            [JsonProperty(PropertyName = "Allow PVP Below Height")]
            public float Underworld = -500f;

            [JsonProperty(PropertyName = "Allow PVP Above Height")]
            public float Aboveworld = 5000f;

            [JsonProperty(PropertyName = "Allow Other Damage Below Height")]
            public float UnderworldOther = -500f;

            [JsonProperty(PropertyName = "Allow Other Damage Above Height")]
            public float AboveworldOther = 5000f;

            [JsonProperty(PropertyName = "Allow Cold Metabolism Damage")]
            public bool Cold;

            [JsonProperty(PropertyName = "Allow Heat Metabolism Damage")]
            public bool Heat;

            [JsonProperty(PropertyName = "Allow Thirst And Hunger Damage To Farmable Animals")]
            public bool FarmableMetabolism = true;

            [JsonProperty(PropertyName = "Auto remove mappings from data file that no longer exist on server restart")]
            public bool AutoRemove;

            [JsonProperty(PropertyName = "Vehicles can hurt NPC players (true = ignore this option)")]
            public bool VehiclesCanHurtNpcs = true;

            [JsonProperty(PropertyName = "Use Clans")]
            public bool Clans = true;

            [JsonProperty(PropertyName = "Use Friends")]
            public bool Friends = true;

            [JsonProperty(PropertyName = "Use Teams")]
            public bool Teams = true;
        }

        private class LootSupport
        {
            [JsonProperty(PropertyName = "Auto lock (codelock, keylock, nothing)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, string> AutoLock = new() { ["cupboard.tool.deployed"] = "nothing" };

            [JsonProperty(PropertyName = "Exceptions for locks to various containers option", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> NoLocks = new() { "skulltrophy.deployed", "skull_fire_pit", "bbq.static", "small_refinery_static", "campfire" };

            [JsonProperty(PropertyName = "Enable support to allow adding locks to various containers")]
            public bool Locks;

            [JsonProperty(PropertyName = "Enable codelock anti-raiding (team/clan/friend access only)")]
            public bool Antigrief;

            [JsonProperty(PropertyName = "Protect unlocked TC from being accessed by enemy players")]
            public bool ProtectTC;

            [JsonProperty(PropertyName = "Prevent player shield from dropping on death")]
            public bool NoShieldDrop;

            [JsonProperty(PropertyName = "Prevent player active item from dropping on death")]
            public bool NoActiveItemDrop;

            [JsonProperty(PropertyName = "Prevent player backpack from dropping on death (Rust backpack)")]
            public bool NoRustBackpackDrop;

            [JsonProperty(PropertyName = "Prevent players from using enemy car lifts")]
            public bool Lifts;

            [JsonProperty(PropertyName = "Prevent non-ally from looting sleepers")]
            public bool Sleepers;

            [JsonProperty(PropertyName = "Prevent non-ally from looting corpses")]
            public bool Corpses;

            [JsonProperty(PropertyName = "Prevent non-ally from looting backpacks")]
            public bool Backpacks;

            [JsonProperty(PropertyName = "Prevent non-ally from looting planters")]
            public bool Planters;
        }

        private class ArmorDamagePVE
        {
            [JsonProperty(PropertyName = "Skin IDs which are immune to damage", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ulong> ImmuneSkins = new();

            [JsonProperty(PropertyName = "Enable support for npcs to cause armor damage on hit")]
            public bool Enabled;

            [JsonProperty(PropertyName = "Play headshot sound when a player is headshot by an npc")]
            public bool Headshots;
        }

        private class ReflectDamagePVP
        {
            [JsonProperty(PropertyName = "Multiplier Allows Armor Protection")]
            public bool Protection = true;

            [JsonProperty(PropertyName = "Arrow Damage")]
            public float Arrow;

            [JsonProperty(PropertyName = "Blunt Damage")]
            public float Blunt;

            [JsonProperty(PropertyName = "Bullet Damage")]
            public float Bullet;

            [JsonProperty(PropertyName = "Slash Damage")]
            public float Slash;

            [JsonProperty(PropertyName = "Stab Damage")]
            public float Stab;

            internal bool Any => Arrow != 0 || Blunt != 0 || Bullet != 0 || Slash != 0 || Stab != 0;

            internal float Get(DamageType type) => type switch
            {
                DamageType.Arrow => Arrow,
                DamageType.Blunt => Blunt,
                DamageType.Bullet => Bullet,
                DamageType.Slash => Slash,
                DamageType.Stab => Stab,
                _ => 0
            };
        }

        private class SleeperAuthorizationOptions
        {
            [JsonProperty(PropertyName = "Enabled")]
            public bool Enabled;

            [JsonProperty(PropertyName = "Distinct Loot Required (performance heavy)")]
            public int BaseLoot;

            [JsonProperty(PropertyName = "Foundations Required (performance heavy)")]
            public int FoundationLimit;

            [JsonProperty(PropertyName = "Walls Required (performance heavy)")]
            public int WallLimit;

            [JsonProperty(PropertyName = "Include Twig Structures (performance heavy)")]
            public bool Twig;

            [JsonProperty(PropertyName = "Include Wood Structures (performance heavy)")]
            public bool Wood;

            [JsonProperty(PropertyName = "Skip above checks when the entity count of a base exceeds X (performance heavy)")]
            public int EntityOverrideLimit;

            internal Dictionary<uint, bool> _cache = new();
            internal List<string> ID_FLOORS = new() { "floor", "floor.frame", "floor.grill", "floor.ladder.hatch", "floor.triangle", "floor.triangle.frame", "floor.triangle.grill", "floor.triangle.ladder.hatch" };

            internal bool Checks => BaseLoot > 0 || FoundationLimit > 0 || WallLimit > 0;

            internal bool Any => Checks || EntityOverrideLimit > 0;

            internal List<string> shortnames = new();
            
            internal List<ItemContainer> buffer = new();

            public bool MeetsMinimumRequirements(BuildingPrivlidge priv)
            {
                if (!Any)
                    return true;

                if (_cache.TryGetValue(priv.buildingID, out bool value))
                    return value;
                
                BuildingManager.Building building = priv.GetBuilding();
                if (building == null || !building.HasDecayEntities()) 
                    return false;

                uint ID = building.ID;
                int count = building.decayEntities.Count;

                if (EntityOverrideLimit > 0 && count > EntityOverrideLimit)
                {
                    _cache[ID] = true;
                    return true;
                }

                if (!Checks)
                    return true;

                shortnames.Clear();
                buffer.Clear();

                int walls = 0, foundations = 0, floors = 0, counted = 0;

                foreach (var e in building.decayEntities)
                {
                    if (e == null || e.IsDestroyed)
                    {
                        continue;
                    }
                    if (++counted % 10 == 0 && Performance.report.frameRate < 15)
                    {
                        return false;
                    }
                    if (BaseLoot <= 0 && !(e is BuildingBlock))
                    {
                        continue;
                    }
                    if (FoundationLimit > 0)
                    {
                        if (e.ShortPrefabName == "foundation" || e.ShortPrefabName == "foundation.triangle")
                        {
                            BuildingBlock block = e as BuildingBlock;
                            if (!Twig && block.grade == BuildingGrade.Enum.Twigs)
                            {
                                continue;
                            }
                            if (!Wood && block.grade == BuildingGrade.Enum.Wood)
                            {
                                continue;
                            }
                            foundations++;
                            continue;
                        }
                    }
                    if (WallLimit > 0)
                    {
                        if (e.ShortPrefabName == "wall" || e.ShortPrefabName == "wall.half" || e.ShortPrefabName == "wall.window")
                        {
                            BuildingBlock block = e as BuildingBlock;
                            if (!Twig && block.grade == BuildingGrade.Enum.Twigs)
                            {
                                continue;
                            }
                            if (!Wood && block.grade == BuildingGrade.Enum.Wood)
                            {
                                continue;
                            }
                            walls++;
                            continue;
                        }
                        if (ID_FLOORS.Contains(e.ShortPrefabName))
                        {
                            if (e.children != null)
                            {
                                foreach (var x in e.children)
                                {
                                    if (x is CollectibleEntity col && col != null && col.itemList == null)
                                    {
                                        foundations++;
                                    }
                                }
                            }
                            floors++;
                            continue;
                        }
                    }
                    if (BaseLoot > 0)
                    {
                        AddDistinctBaseLoot(shortnames, buffer, e);
                    }
                }

                if (foundations == 0)
                {
                    foundations = floors;
                }

                bool wallCheck = WallLimit <= 0 || walls >= WallLimit;
                bool foundationCheck = FoundationLimit <= 0 || FoundationLimit >= foundations;
                bool baseLootCheck = BaseLoot <= 0 || shortnames.Count >= BaseLoot;

                _cache[ID] = value = wallCheck && foundationCheck && baseLootCheck;
                if (!value) priv.Invoke(() => _cache.Remove(ID), 15f);
                return value;
            }

            private static void AddDistinctBaseLoot(List<string> shortnames, List<ItemContainer> containers, DecayEntity ent)
            {
                IInventoryProvider provider = ent as IInventoryProvider;
                if (provider == null)
                {
                    return;
                }

                provider.GetAllInventories(containers);
                if (containers.Count == 0)
                {
                    return;
                }

                foreach (ItemContainer container in containers)
                {
                    if (container == null || container.itemList == null)
                    {
                        continue;
                    }
                    foreach (Item item in container.itemList)
                    {
                        if (item != null && item.info != null && !shortnames.Contains(item.info.shortname))
                        {
                            shortnames.Add(item.info.shortname);
                        }
                    }
                }

                containers.Clear();
            }
        }

        // BEGIN: Integrated Loot Protection (LootDefender/PreventLooting) configuration sections

        private class LootDefenderOptions
        {
            [JsonProperty(PropertyName = "Enabled")]
            public bool Enabled = false;

            [JsonProperty(PropertyName = "Lock Bradley Crates")]
            public bool LockBradley = true;

            [JsonProperty(PropertyName = "Lock Patrol Heli Crates")]
            public bool LockHeli = true;

            [JsonProperty(PropertyName = "Lock NPC Corpses")]
            public bool LockNpc = true;

            [JsonProperty(PropertyName = "Lock Radius (meters)")]
            public float LockRadius = 25f;

            [JsonProperty(PropertyName = "Lock Duration (seconds, 0 = forever)")]
            public int LockSeconds = 300;

            [JsonProperty(PropertyName = "Group By Team (sum team damage)")]
            public bool GroupByTeam = true;

            [JsonProperty(PropertyName = "Allow Allies (Clan/Friends) Of Winners")]
            public bool AllowAllies = true;

            [JsonProperty(PropertyName = "Block Looting Only (don't block damage)")]
            public bool BlockLootingOnly = true;

            [JsonProperty(PropertyName = "Remove Fire From Crates")]
            public bool RemoveFireFromCrates = true;

            [JsonProperty(PropertyName = "Owner Toasts - While Fighting (seconds, 0=off)")]
            public int OwnerToastCombatSeconds = 30;

            [JsonProperty(PropertyName = "Owner Toasts - On Loot Denied (true/false)")]
            public bool OwnerToastOnLootDenied = true;

            // Bradley Settings
            [JsonProperty(PropertyName = "Bradley - Damage Lock Threshold")]
            public float BradleyThreshold = 0.2f;

            [JsonProperty(PropertyName = "Bradley - Lock Time (seconds, 0 = forever)")]
            public int BradleyLockTime = 900;

            [JsonProperty(PropertyName = "Bradley - Lock At Harbor")]
            public bool BradleyLockHarbor = false;

            [JsonProperty(PropertyName = "Bradley - Lock From Monument Bradley Plugin")]
            public bool BradleyLockMonument = true;

            [JsonProperty(PropertyName = "Bradley - XP Reward")]
            public double BradleyXP = 0.0;

            [JsonProperty(PropertyName = "Bradley - ShoppyStock Reward Value")]
            public double BradleySS = 0.0;

            [JsonProperty(PropertyName = "Bradley - ShoppyStock Shop Name")]
            public string BradleyShoppyStockShopName = "";

            // Helicopter Settings
            [JsonProperty(PropertyName = "Helicopter - Damage Lock Threshold")]
            public float HeliThreshold = 0.2f;

            [JsonProperty(PropertyName = "Helicopter - Lock Time (seconds, 0 = forever)")]
            public int HeliLockTime = 900;

            [JsonProperty(PropertyName = "Helicopter - Lock At Harbor")]
            public bool? HeliLockHarbor = null; // null = use Bradley setting

            [JsonProperty(PropertyName = "Helicopter - Unlock When X Distance From Owner (meters, 0 = disabled)")]
            public float HeliUnlockDistance = 1500f;

            [JsonProperty(PropertyName = "Helicopter - Broadcast Unlocked Notification To Chat")]
            public bool HeliBroadcastUnlocked = false;

            [JsonProperty(PropertyName = "Helicopter - XP Reward")]
            public double HeliXP = 0.0;

            [JsonProperty(PropertyName = "Helicopter - ShoppyStock Reward Value")]
            public double HeliSS = 0.0;

            [JsonProperty(PropertyName = "Helicopter - ShoppyStock Shop Name")]
            public string HeliShoppyStockShopName = "";

            // NPC Settings
            [JsonProperty(PropertyName = "NPC - Damage Lock Threshold")]
            public float NpcThreshold = 0.2f;

            [JsonProperty(PropertyName = "NPC - Lock Time (seconds, 0 = forever)")]
            public int NpcLockTime = 0;

            [JsonProperty(PropertyName = "NPC - XP Reward")]
            public double NpcXP = 0.0;

            [JsonProperty(PropertyName = "NPC - ShoppyStock Reward Value")]
            public double NpcSS = 0.0;

            [JsonProperty(PropertyName = "NPC - ShoppyStock Shop Name")]
            public string NpcShoppyStockShopName = "";

            [JsonProperty(PropertyName = "NPC - Broadcast Locked Notification To Chat")]
            public bool NpcBroadcastLocked = false;

            [JsonProperty(PropertyName = "NPC - Reward Distance Multiplier")]
            public DistanceMultiplierSettings NpcDistanceMultiplier = new();

            [JsonProperty(PropertyName = "NPC - Reward Weapon Multiplier", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, double> NpcWeaponMultipliers = new();

            // Hackable Crate Settings
            [JsonProperty(PropertyName = "Hackable Crates - Enabled")]
            public bool HackableEnabled = false;

            [JsonProperty(PropertyName = "Hackable Crates - Lock Time (seconds, 0 = forever)")]
            public int HackableLockTime = 900;

            [JsonProperty(PropertyName = "Hackable Crates - Lock At Harbor")]
            public bool HackableLockHarbor = false;

            [JsonProperty(PropertyName = "Hackable Crates - Block Timer Increase On Damage To Laptop")]
            public bool HackableBlockLaptopDamage = true;

            [JsonProperty(PropertyName = "Hackable Crates - Broadcast Locked Notification To Chat")]
            public bool HackableBroadcastLocked = false;

            [JsonProperty(PropertyName = "Hackable Crates - Broadcast Unlocked Notification To Chat")]
            public bool HackableBroadcastUnlocked = false;

            [JsonProperty(PropertyName = "Hackable Crates - Cooldown Between Notifications For Each Player")]
            public float HackableNotifyCooldown = 0f;

            [JsonProperty(PropertyName = "Hackable Crates - Permissions Enabled To Set Required Hack Seconds")]
            public bool HackablePermissionsEnabled = true;

            [JsonProperty(PropertyName = "Hackable Crates - Permissions", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<HackPermission> HackablePermissions = new()
            {
                new() { Permission = "truepve.lootdefender.hackedcrates.regular", Value = 750f },
                new() { Permission = "truepve.lootdefender.hackedcrates.elite", Value = 500f },
                new() { Permission = "truepve.lootdefender.hackedcrates.legend", Value = 300f },
                new() { Permission = "truepve.lootdefender.hackedcrates.vip", Value = 120f },
            };

            // Player Lockouts
            [JsonProperty(PropertyName = "Player Lockouts - Bypass During F15 Server Wipe Event")]
            public bool LockoutBypassF15 = false;

            [JsonProperty(PropertyName = "Player Lockouts - Time Between Bradley In Minutes")]
            public double LockoutBradleyMinutes = 0.0;

            [JsonProperty(PropertyName = "Player Lockouts - Time Between Heli In Minutes")]
            public double LockoutHeliMinutes = 0.0;

            [JsonProperty(PropertyName = "Player Lockouts - Command To See Lockout Times")]
            public string LockoutCommand = "lockouts";

            [JsonProperty(PropertyName = "Player Lockouts - Lockout Entire Team")]
            public bool LockoutTeam = true;

            [JsonProperty(PropertyName = "Player Lockouts - Lockout Entire Clan")]
            public bool LockoutClan = true;

            [JsonProperty(PropertyName = "Player Lockouts - Exclude Members Offline For More Than X Minutes")]
            public float LockoutExcludeOfflineMinutes = 15f;

            [JsonProperty(PropertyName = "Player Lockouts - Lockouts Ignored For Entities With Skin ID", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ulong> LockoutExceptions = new();

            // Lockout UI
            [JsonProperty(PropertyName = "Lockout UI - Command To Toggle UI")]
            public string LockoutUICommand = "lockui";

            [JsonProperty(PropertyName = "Lockout UI - Bradley Enabled")]
            public bool LockoutUIBradleyEnabled = true;

            [JsonProperty(PropertyName = "Lockout UI - Heli Enabled")]
            public bool LockoutUIHeliEnabled = true;

            // UI Settings
            [JsonProperty(PropertyName = "Lockout UI - Bradley Settings")]
            public LDUIBradleySettings UIBradley = new();

            [JsonProperty(PropertyName = "Lockout UI - Heli Settings")]
            public LDUIHeliSettings UIHeli = new();

            // Discord Messages
            [JsonProperty(PropertyName = "Discord Messages - Webhook URL")]
            public string DiscordWebhookUrl = "https://support.discordapp.com/hc/en-us/articles/228383668-Intro-to-Webhooks";

            [JsonProperty(PropertyName = "Discord Messages - Embed Color (DECIMAL)")]
            public int DiscordMessageColor = 3329330;

            [JsonProperty(PropertyName = "Discord Messages - Embed Title")]
            public string DiscordEmbedTitle = "Lockouts";

            [JsonProperty(PropertyName = "Discord Messages - Embed Player Field Name")]
            public string DiscordEmbedPlayer = "Player";

            [JsonProperty(PropertyName = "Discord Messages - Embed Message Field Name")]
            public string DiscordEmbedMessage = "Message";

            [JsonProperty(PropertyName = "Discord Messages - Embed Server Field Name")]
            public string DiscordEmbedServer = "Connect via Steam:";

            [JsonProperty(PropertyName = "Discord Messages - Add BattleMetrics Link")]
            public bool DiscordBattleMetrics = true;

            [JsonProperty(PropertyName = "Discord Messages - Show Notification In Server Console")]
            public bool DiscordNotifyConsole = false;

            // Damage Report Settings
            [JsonProperty(PropertyName = "Damage Report - Hex Color Single Player")]
            public string ReportSinglePlayerColor = "#6d88ff";

            [JsonProperty(PropertyName = "Damage Report - Hex Color Team")]
            public string ReportTeamColor = "#ff804f";

            [JsonProperty(PropertyName = "Damage Report - Hex Color Ok")]
            public string ReportOkColor = "#88ff6d";

            [JsonProperty(PropertyName = "Damage Report - Hex Color Not Ok")]
            public string ReportNotOkColor = "#ff5716";
        }

        private class LDUIBradleySettings
        {
            [JsonProperty(PropertyName = "Enabled")]
            public bool Enabled = true;

            [JsonProperty(PropertyName = "Bradley Anchor Min")]
            public string AnchorMin = "0.896 0.275";

            [JsonProperty(PropertyName = "Bradley Anchor Max")]
            public string AnchorMax = "0.936 0.310";

            [JsonProperty(PropertyName = "Bradley Background Color")]
            public string BackgroundColor = "#FF0000";

            [JsonProperty(PropertyName = "Bradley Text Color")]
            public string TextColor = "#FFFF00";

            [JsonProperty(PropertyName = "Panel Alpha")]
            public float Alpha = 1f;

            [JsonProperty(PropertyName = "Font Size")]
            public int FontSize = 18;
        }

        private class LDUIHeliSettings
        {
            [JsonProperty(PropertyName = "Enabled")]
            public bool Enabled = true;

            [JsonProperty(PropertyName = "Heli Anchor Min")]
            public string AnchorMin = "0.896 0.325";

            [JsonProperty(PropertyName = "Heli Anchor Max")]
            public string AnchorMax = "0.936 0.360";

            [JsonProperty(PropertyName = "Heli Background Color")]
            public string BackgroundColor = "#1F51FF";

            [JsonProperty(PropertyName = "Heli Text Color")]
            public string TextColor = "#FFFF00";

            [JsonProperty(PropertyName = "Panel Alpha")]
            public float Alpha = 1f;

            [JsonProperty(PropertyName = "Font Size")]
            public int FontSize = 18;
        }

        private class HackPermission
        {
            [JsonProperty(PropertyName = "Permission")]
            public string Permission { get; set; }

            [JsonProperty(PropertyName = "Hack Time")]
            public float Value { get; set; }
        }

        private class DistanceMultiplierSettings
        {
            [JsonProperty(PropertyName = "400 meters")]
            public float meters400 = 1f;

            [JsonProperty(PropertyName = "300 meters")]
            public float meters300 = 1f;

            [JsonProperty(PropertyName = "200 meters")]
            public float meters200 = 1f;

            [JsonProperty(PropertyName = "100 meters")]
            public float meters100 = 1f;

            [JsonProperty(PropertyName = "75 meters")]
            public float meters75 = 1f;

            [JsonProperty(PropertyName = "50 meters")]
            public float meters50 = 1f;

            [JsonProperty(PropertyName = "25 meters")]
            public float meters25 = 1f;

            [JsonProperty(PropertyName = "under")]
            public float under = 1f;

            public double GetDistanceMult(float distance) =>
                distance >= 400 ? meters400 :
                distance >= 300 ? meters300 :
                distance >= 200 ? meters200 :
                distance >= 100 ? meters100 :
                distance >= 75 ? meters75 :
                distance >= 50 ? meters50 :
                distance >= 25 ? meters25 :
                under;
        }

        private class SupplyDropOptions
        {
            [JsonProperty(PropertyName = "Allow Locking Signals With These Skins", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ulong> AllowedSignalSkins = new() { 0 };

            [JsonProperty(PropertyName = "Lock Supply Drops To Players")]
            public bool LockSupplyDropsToPlayers = false;

            [JsonProperty(PropertyName = "Lock To Player For X Seconds (0 = Forever)")]
            public int LockSeconds = 360;

            [JsonProperty(PropertyName = "Bypass Spawning Cargo Plane")]
            public bool BypassSpawningCargoPlane = false;

            [JsonProperty(PropertyName = "Maximum Drop Distance From Signal")]
            public float MaximumDropDistanceFromSignal = 20f;

            [JsonProperty(PropertyName = "Destroy Drop After X Seconds")]
            public float DestroyDropAfterSeconds = 0f;

            [JsonProperty(PropertyName = "Cargo Plane Low Altitude Drop")]
            public bool LowDrop = true;

            [JsonProperty(PropertyName = "Lock Supply Drops From Npc Random Raids Plugin")]
            public bool LockFromNpcRandomRaids = false;

            [JsonProperty(PropertyName = "Lock Supply Drops From Helpful Supply Plugin")]
            public bool LockFromHelpfulSupply = false;

            [JsonProperty(PropertyName = "Disable CH47 Gibs")]
            public bool DisableCH47Gibs = false;
        }

        private class PreventLootingOptions
        {
            [JsonProperty(PropertyName = "Enabled")]
            public bool Enabled = false;

            [JsonProperty(PropertyName = "Use Permissions (preventlooting.use)")]
            public bool UsePermissions = false;

            [JsonProperty(PropertyName = "Admins Can Always Loot")]
            public bool AdminCanLoot = true;

            [JsonProperty(PropertyName = "Allow Looting Players")]
            public bool AllowLootPlayers = false;

            [JsonProperty(PropertyName = "Allow Looting Corpses")]
            public bool AllowLootCorpses = true;

            [JsonProperty(PropertyName = "Allow Looting Storage Containers")]
            public bool AllowLootStorage = true;

            [JsonProperty(PropertyName = "Use Teams For Allies")]
            public bool UseTeams = true;

            [JsonProperty(PropertyName = "Use Friends API For Allies")]
            public bool UseFriendsAPI = true;

            [JsonProperty(PropertyName = "Respect Cupboard Authorization")]
            public bool UseCupboardAuth = true;

            [JsonProperty(PropertyName = "Only In Cupboard Range (if true)")]
            public bool OnlyInCupboardRange = false;

            [JsonProperty(PropertyName = "Excluded ShortPrefabNames", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> ExcludedShortPrefabNames = new();

            [JsonProperty(PropertyName = "Debug Logging")]
            public bool Debug;

            // Additional PreventLooting options from original plugin
            [JsonProperty(PropertyName = "Can Authorize Cupboard")]
            public bool CanAuthorizeCupboard = true;

            [JsonProperty(PropertyName = "Can Racked Weapon Mount")]
            public bool AllowRackedWeaponMount = false;

            [JsonProperty(PropertyName = "Can Racked Weapon Swap")]
            public bool AllowRackedWeaponSwap = false;

            [JsonProperty(PropertyName = "Can Racked Weapon Take")]
            public bool AllowRackedWeaponTake = false;

            [JsonProperty(PropertyName = "Protect Planterboxes (Prevent unauthorized harvesting)")]
            public bool ProtectPlanterboxes = true;

            [JsonProperty(PropertyName = "Can Racked Weapon Unload")]
            public bool AllowRackedWeaponUnload = false;

            [JsonProperty(PropertyName = "Can Racked Weapon Load")]
            public bool AllowRackedWeaponLoad = false;

            [JsonProperty(PropertyName = "Can Loot Backpack")]
            public bool AllowLootBackpacks = false;

            [JsonProperty(PropertyName = "Can Loot Backpack Plugin")]
            public bool AllowLootBackpackPlugin = false;

            [JsonProperty(PropertyName = "Can Pickup")]
            public bool AllowPickup = false;

            [JsonProperty(PropertyName = "Can Oven Toggle")]
            public bool AllowOvenToggle = false;

            [JsonProperty(PropertyName = "Use Zone Manager")]
            public bool UseZoneManager = false;

            [JsonProperty(PropertyName = "Zone Manager Include Mode")]
            public bool ZoneManagerIncludeMode = false;

            [JsonProperty(PropertyName = "Zone IDs", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> ZoneIDs = new() { "12345678" };

            [JsonProperty(PropertyName = "Use Dynamic PVP")]
            public bool UseDynamicPVP = false;

            [JsonProperty(PropertyName = "Use Exclude Entities")]
            public bool UseExcludeEntities = true;

            [JsonProperty(PropertyName = "Exclude Entities", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> ExcludeEntities = new() { "mailbox.deployed" };

            [JsonProperty(PropertyName = "Use Cupboard")]
            public bool UseCupboard = false;

            [JsonProperty(PropertyName = "Use Cupboard Include", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> UseCupboardInclude = new() { "storage" };

            [JsonProperty(PropertyName = "Use Only In Cupboard Range Include", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> OnlyInCupboardRangeInclude = new() { "storage" };
        }

        // END: Integrated Loot Protection configuration sections

        private class Configuration
        {
            [JsonProperty(PropertyName = "Config Version")]
            public string configVersion = null;

            [JsonProperty(PropertyName = "Default RuleSet")]
            public string defaultRuleSet = "default";

            [JsonProperty(PropertyName = "Configuration Options")]
            public ConfigurationOptions options = new();

            [JsonProperty(PropertyName = "Mappings")]
            public Dictionary<string, string> mappings = new();

            [JsonProperty(PropertyName = "Schedule")]
            public Schedule schedule = new();

            [JsonProperty(PropertyName = "RuleSets")]
            public List<RuleSet> ruleSets = new();

            [JsonProperty(PropertyName = "Entity Groups")]
            public List<EntityGroup> groups = new();

            [JsonProperty(PropertyName = "Allow Killing Sleepers")]
            public bool AllowKillingSleepers;

            [JsonProperty(PropertyName = "Allow Killing Sleepers (Ally Only)")]
            public bool AllowKillingSleepersAlly;

            [JsonProperty(PropertyName = "Allow Killing Sleepers (Authorization Only)", NullValueHandling = NullValueHandling.Ignore)]
            public bool? _AllowKillingSleepersAuthorization = null;

            [JsonProperty(PropertyName = "Allow Killing Sleepers (TC Auth Only)")]
            public SleeperAuthorizationOptions AllowKillingSleepersAuthorization = new();

            [JsonProperty(PropertyName = "Allow Killing Sleepers (After X Hours Offline)")]
            public float AllowKillingSleepersHoursOffline;

            [JsonProperty(PropertyName = "Allow Killing Sleepers (Allowed steam ids)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ulong> AllowKillingSleepersIds = new() { 0 };

            [JsonProperty(PropertyName = "Ignore Firework Damage")]
            public bool Firework = true;

            [JsonProperty(PropertyName = "Ignore Campfire Damage")]
            public bool Campfires;

            [JsonProperty(PropertyName = "Ignore Ladder Damage")]
            public bool Ladders;

            [JsonProperty(PropertyName = "Ignore Sleeping Bag Damage")]
            public bool SleepingBags;

            [JsonProperty(PropertyName = "Players Can Trigger Traps In Monument Topology")]
            public bool PlayersTriggerTraps = true;

            [JsonProperty(PropertyName = "Players Can Hurt Traps In Monument Topology")]
            public bool PlayersHurtTraps;

            [JsonProperty(PropertyName = "Players Can Trigger Turrets In Monument Topology")]
            public bool PlayersTriggerTurrets = true;

            [JsonProperty(PropertyName = "Players Can Hurt Turrets In Monument Topology")]
            public bool PlayersHurtTurrets;

            [JsonProperty(PropertyName = "Prevent hackable crate timer from resetting when attacked")]
            public bool laptop = true;

            [JsonProperty(PropertyName = "Block Scrap Heli Damage")]
            public bool scrap = true;

            [JsonProperty(PropertyName = "Prevent elevator from crushing players by using a short upward teleport")]
            public bool lift;

            [JsonProperty(PropertyName = "Block Igniter Damage")]
            public bool igniter;

            [JsonProperty(PropertyName = "Block Wallpaper Damage")]
            public bool wallpaper = true;

            [JsonProperty(PropertyName = "Block Radioactive Water Damage")]
            public bool BlockRadioactiveWaterDamage = true;

            [JsonProperty(PropertyName = "Block Decay Damage To Vehicles")]
            public bool BlockDecayDamageToVehicles;

            [JsonProperty(PropertyName = "Block Spray Can In Safe Zones")]
            public bool BlockSprayCanInSafeZones;

            [JsonProperty(PropertyName = "Prevent heli from strafing in the inner radius of safe zones")]
            public bool PreventSafeZoneStrafing;

            [JsonProperty(PropertyName = "Prevent players from throwing water in arctic and tundra biome")]
            public bool PreventThrowingWaterInFreezingBiome;

            [JsonProperty(PropertyName = "Prevent ragdolling when struck by another vehicle")]
            public bool PreventRagdolling = true;

            [JsonProperty(PropertyName = "Experimental ZoneManager support for PVE zones")]
            public bool PVEZones;

            [JsonProperty(PropertyName = "Loot Defender")]
            public LootDefenderOptions LootDefender = new();

            [JsonProperty(PropertyName = "Prevent Looting")]
            public PreventLootingOptions PreventLooting = new();

            [JsonProperty(PropertyName = "Supply Drop Settings")]
            public SupplyDropOptions SupplyDrops = new();

            [JsonProperty(PropertyName = "Kill Notifications")] 
            public KillNotifyOptions Notify = new();

            internal Dictionary<ulong, List<string>> groupCache = new();
            internal TruePVE instance;

            public void Init(TruePVE instance)
            {
                this.instance = instance;
                schedule.Init(instance);
                foreach (RuleSet rs in ruleSets)
                    rs.Build(instance);
                ruleSets.Remove(null);
            }

            public List<string> ResolveEntityGroups(BaseEntity entity)
            {
                ulong id = entity == null || entity.net == null ? 0 : entity.net.ID.Value;

                if (id > 0 && groupCache.TryGetValue(id, out var cachedGroups))
                {
                    return cachedGroups;
                }

                List<string> currentGroups = new(groups.Count);

                string typeName = instance.GetTypeName(entity);

                foreach (EntityGroup group in groups)
                {
                    if (group.Contains(typeName, entity.ShortPrefabName))
                    {
                        currentGroups.Add(group.name);
                    }
                }

                if (id > 0)
                {
                    groupCache[id] = currentGroups;
                }

                return currentGroups;
            }

            public bool HasMapping(string key)
            {
                return mappings.ContainsKey(key) || mappings.ContainsKey(AllZones);
            }

            public RuleSet GetDefaultRuleSet()
            {
                RuleSet foundRuleSet = null;
                int matchCount = 0;

                foreach (var r in ruleSets)
                {
                    if (r.name == defaultRuleSet)
                    {
                        foundRuleSet = r;
                        matchCount++;
                    }
                }

                if (matchCount > 1)
                {
                    Puts($"Warning - duplicate ruleset found for default RuleSet: '{defaultRuleSet}'");
                }

                return foundRuleSet;
            }

            public RuleSet GetDudRuleSet()
            {
                return new("override")
                {
                    _flags = RuleFlags.HumanNPCDamage,
                    defaultAllowDamage = false,
                    enabled = true
                };
            }
        }

        private class KillNotifyOptions
        {
            [JsonProperty(PropertyName = "Enabled")] 
            public bool Enabled = true;

            [JsonProperty(PropertyName = "GameTip Style (0-Blue,1-Red,2-BlueLong,3-BlueShort,4-ServerEvent)")] 
            public int Style = 4;

            [JsonProperty(PropertyName = "Top Contributors Count")] 
            public int TopContributors = 3;
        }

        // Lockout UI Implementation (Credits: Absolut & k1lly0u from LootDefender)
        private class LDUIClass
        {
            private const string BradleyPanelName = "TruePVE_Lockouts_UI_Bradley";
            private const string HeliPanelName = "TruePVE_Lockouts_UI_Heli";

            private static CuiElementContainer CreateElementContainer(string panelName, string color, string aMin, string aMax, bool cursor = false, string parent = "Overlay")
            {
                var NewElement = new CuiElementContainer
                {
                    {
                        new CuiPanel
                        {
                            Image =
                            {
                                Color = color
                            },
                            RectTransform =
                            {
                                AnchorMin = aMin,
                                AnchorMax = aMax
                            },
                            CursorEnabled = cursor
                        },
                        new CuiElement().Parent = parent,
                        panelName
                    }
                };
                return NewElement;
            }

            private static void CreateLabel(ref CuiElementContainer container, string panel, string color, string text, int size, string aMin, string aMax, TextAnchor align = TextAnchor.MiddleCenter)
            {
                container.Add(new CuiLabel
                {
                    Text =
                    {
                        Color = color,
                        FontSize = size,
                        Align = align,
                        FadeIn = 1.0f,
                        Text = text
                    },
                    RectTransform =
                    {
                        AnchorMin = aMin,
                        AnchorMax = aMax
                    }
                },
                panel);
            }

            private static string Color(string hexColor, float a = 1.0f)
            {
                a = Mathf.Clamp(a, 0f, 1f);
                hexColor = hexColor.TrimStart('#');
                int r = int.Parse(hexColor.Substring(0, 2), NumberStyles.AllowHexSpecifier);
                int g = int.Parse(hexColor.Substring(2, 2), NumberStyles.AllowHexSpecifier);
                int b = int.Parse(hexColor.Substring(4, 2), NumberStyles.AllowHexSpecifier);
                return $"{(double)r / 255} {(double)g / 255} {(double)b / 255} {a}";
            }

            public static void DestroyLockoutUI(TruePVE instance, BasePlayer player)
            {
                if (player.IsValid() && player.IsConnected && instance._ldUIPlayers.Contains(player))
                {
                    CuiHelper.DestroyUi(player, BradleyPanelName);
                    CuiHelper.DestroyUi(player, HeliPanelName);
                    instance._ldUIPlayers.Remove(player);
                    DestroyLockoutUpdate(instance, player);
                }
            }

            public static void DestroyAllLockoutUI(TruePVE instance)
            {
                // Create a copy of the list to avoid modification during iteration
                var playersCopy = new List<BasePlayer>(instance._ldUIPlayers);
                for (int i = 0; i < playersCopy.Count; i++)
                {
                    var player = playersCopy[i];
                    if (player != null && player.IsValid() && player.IsConnected)
                    {
                        CuiHelper.DestroyUi(player, BradleyPanelName);
                        CuiHelper.DestroyUi(player, HeliPanelName);
                        DestroyLockoutUpdate(instance, player);
                    }
                }
                instance._ldUIPlayers.Clear();
            }

            private static void Create(TruePVE instance, BasePlayer player, string panelName, string text, int fontSize, string color, string panelColor, string aMin, string aMax)
            {
                var element = CreateElementContainer(panelName, panelColor, aMin, aMax, false, "Hud");
                CreateLabel(ref element, panelName, Color(color), text, fontSize, "0 0", "1 1");
                CuiHelper.AddUi(player, element);

                if (!instance._ldUIPlayers.Contains(player))
                {
                    instance._ldUIPlayers.Add(player);
                }
            }

            public static void ShowLockouts(TruePVE instance, BasePlayer player)
            {
                if (player == null || !player.IsConnected) return;

                if (instance._isF15EventActive || instance.permission.UserHasPermission(player.UserIDString, "truepve.lootdefender.bypass.lockouts"))
                {
                    instance._lockouts.Remove(player.UserIDString);
                    return;
                }

                if (!instance._lockouts.TryGetValue(player.UserIDString, out var lo))
                {
                    instance._lockouts[player.UserIDString] = lo = new LockoutInfo();
                }

                var settings = GetSettings(instance, player.UserIDString);
                if (!settings.Enabled || !settings.Lockouts) return;

                if (instance.config.LootDefender.UIBradley.Enabled && instance.config.LootDefender.LockoutUIBradleyEnabled)
                {
                    double bradleyTime = GetLockoutTime(instance, LDDamageType.Bradley, lo, player.UserIDString);
                    if (bradleyTime > 0f)
                    {
                        string bradley = Math.Floor(TimeSpan.FromSeconds(bradleyTime).TotalMinutes).ToString();
                        string bradleyBackgroundColor = Color(instance.config.LootDefender.UIBradley.BackgroundColor, instance.config.LootDefender.UIBradley.Alpha);
                        Create(instance, player, BradleyPanelName, $"{bradley}m", instance.config.LootDefender.UIBradley.FontSize, instance.config.LootDefender.UIBradley.TextColor, bradleyBackgroundColor, instance.config.LootDefender.UIBradley.AnchorMin, instance.config.LootDefender.UIBradley.AnchorMax);
                        SetLockoutUpdate(instance, player);
                    }
                }

                if (instance.config.LootDefender.UIHeli.Enabled && instance.config.LootDefender.LockoutUIHeliEnabled)
                {
                    double heliTime = GetLockoutTime(instance, LDDamageType.Heli, lo, player.UserIDString);
                    if (heliTime > 0)
                    {
                        string heli = Math.Floor(TimeSpan.FromSeconds(heliTime).TotalMinutes).ToString();
                        string heliBackgroundColor = Color(instance.config.LootDefender.UIHeli.BackgroundColor, instance.config.LootDefender.UIHeli.Alpha);
                        Create(instance, player, HeliPanelName, $"{heli}m", instance.config.LootDefender.UIHeli.FontSize, instance.config.LootDefender.UIHeli.TextColor, heliBackgroundColor, instance.config.LootDefender.UIHeli.AnchorMin, instance.config.LootDefender.UIHeli.AnchorMax);
                        SetLockoutUpdate(instance, player);
                    }
                }
            }

            private static double GetLockoutTime(TruePVE instance, LDDamageType damageType, LockoutInfo lo, string playerId)
            {
                double time = 0;
                double currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                if (damageType == LDDamageType.Bradley)
                {
                    if (lo.Bradley > currentTime)
                    {
                        time = lo.Bradley - currentTime;
                    }
                    else
                    {
                        lo.Bradley = 0;
                    }
                }
                else if (damageType == LDDamageType.Heli)
                {
                    if (lo.Heli > currentTime)
                    {
                        time = lo.Heli - currentTime;
                    }
                    else
                    {
                        lo.Heli = 0;
                    }
                }

                if (!lo.Any())
                {
                    instance._lockouts.Remove(playerId);
                }

                return time < 0 ? 0 : time;
            }

            public static void UpdateLockoutUI(TruePVE instance, BasePlayer player)
            {
                instance._ldUIPlayers.RemoveAll(p => p == null || !p.IsConnected);

                if (player == null || !player.IsConnected)
                {
                    return;
                }

                DestroyLockoutUI(instance, player);

                var settings = GetSettings(instance, player.UserIDString);
                if (!settings.Enabled || !settings.Lockouts)
                {
                    return;
                }

                ShowLockouts(instance, player);
            }

            private static void SetLockoutUpdate(TruePVE instance, BasePlayer player)
            {
                if (!instance._ldUITimers.TryGetValue(player.userID, out var timers))
                {
                    instance._ldUITimers[player.userID] = timers = new LDUITimers();
                }

                if (timers.Lockout == null || timers.Lockout.Destroyed)
                {
                    timers.Lockout = instance.timer.Once(60f, () => UpdateLockoutUI(instance, player));
                }
                else
                {
                    timers.Lockout.Reset();
                }
            }

            public static void DestroyLockoutUpdate(TruePVE instance, BasePlayer player)
            {
                if (!instance._ldUITimers.TryGetValue(player.userID, out var timers))
                {
                    return;
                }

                if (timers.Lockout == null || timers.Lockout.Destroyed)
                {
                    return;
                }

                timers.Lockout.Destroy();
            }

            public static LDUISettings GetSettings(TruePVE instance, string playerId)
            {
                if (!instance._ldUISettings.TryGetValue(playerId, out var uii))
                {
                    instance._ldUISettings[playerId] = uii = new LDUISettings();
                }
                return uii;
            }
        }

        private class RuleSet
        {
            public string name;
            public bool enabled = true;
            public bool defaultAllowDamage = false;
            public string flags = string.Empty;
            internal RuleFlags _flags = RuleFlags.None;
            internal bool Changed;

            public HashSet<string> rules = new();
            internal HashSet<Rule> parsedRules = new();
            internal Dictionary<string, Rule> ruleDictionary = new();

            public RuleSet() { }
            public RuleSet(string name) { this.name = name; }

            // evaluate the passed lists of entity groups against rules
            public DamageResult Evaluate(TruePVE instance, BaseEntity attacker, List<string> eg1, BaseEntity victim, List<string> eg2, bool returnDefaultValue = true)
            {
                bool trace = instance.trace;

                if (trace) instance.Trace("Evaluating Rules...", 3);

                if (ruleDictionary == null || ruleDictionary.Count == 0)
                {
                    if (trace) instance.Trace($"No rules found; returning default value: {defaultAllowDamage}", 4);
                    return defaultAllowDamage ? DamageResult.Allow : DamageResult.Block;
                }

                bool vg1 = eg1 != null && eg1.Count > 0;
                bool vg2 = eg2 != null && eg2.Count > 0;

                // 1. Check all direct links between eg1 and eg2
                if (vg1 && vg2)
                {
                    if (trace) instance.Trace("Checking direct initiator->target rules...", 4);

                    foreach (string s1 in eg1)
                    {
                        foreach (string s2 in eg2)
                        {
                            string ruleText = s1 + "->" + s2; // Using concatenation for performance

                            if (trace) instance.Trace($"Evaluating \"{ruleText}\"...", 5);

                            if (ruleDictionary.TryGetValue(ruleText, out Rule rule))
                            {
                                if (trace) instance.Trace($"Match found; allow damage? {rule.hurt}", 6);
                                return rule.hurt ? DamageResult.Allow : DamageResult.Block;
                            }

                            if (trace) instance.Trace("No match found", 6);
                        }
                    }
                }

                // 2. If no direct match, check group -> Any
                if (vg1)
                {
                    if (trace) instance.Trace("No direct match rules found; continuing with group->Any...", 4);

                    foreach (string s1 in eg1)
                    {
                        string ruleText = s1 + "->" + Any;

                        if (trace) instance.Trace($"Evaluating \"{ruleText}\"...", 5);

                        if (ruleDictionary.TryGetValue(ruleText, out Rule rule))
                        {
                            if (trace) instance.Trace($"Match found; allow damage? {rule.hurt}", 6);
                            return rule.hurt ? DamageResult.Allow : DamageResult.Block;
                        }

                        if (trace) instance.Trace("No match found", 6);
                    }
                }

                // 3. If still no match, check Any -> group
                if (vg2)
                {
                    if (trace) instance.Trace("No matching group->Any rules found; continuing with Any->group...", 4);

                    foreach (string s2 in eg2)
                    {
                        string ruleText = Any + "->" + s2;

                        if (trace) instance.Trace($"Evaluating \"{ruleText}\"...", 5);

                        if (ruleDictionary.TryGetValue(ruleText, out Rule rule))
                        {
                            if (trace) instance.Trace($"Match found; allow damage? {rule.hurt}", 6);
                            return rule.hurt ? DamageResult.Allow : DamageResult.Block;
                        }

                        if (trace) instance.Trace("No match found", 6);
                    }
                }

                // 4. If no rule was found, return the default value if specified
                if (returnDefaultValue)
                {
                    if (trace) instance.Trace($"No matching rules found; returning default value: {defaultAllowDamage}", 4);

                    return defaultAllowDamage ? DamageResult.Allow : DamageResult.Block;
                }

                // 5. If not returning default, default to None
                return DamageResult.None;
            }

            // build rule strings to rules
            public void Build(TruePVE instance)
            {
                foreach (string ruleText in rules)
                {
                    try { parsedRules.Add(new(instance, ruleText)); }
                    catch { Puts("Invalid rule: {0}", ruleText); }
                }
                parsedRules.Remove(null);
                ValidateRules();
                InitializeRuleDictionary();
                if (flags.Length == 0)
                {
                    _flags |= RuleFlags.None;
                    return;
                }
                foreach (string _value in flags.Split(','))
                {
                    string value = _value.Trim();
                    if (!Enum.TryParse(value, out RuleFlags flag))
                    {
                        if (value == "SamSitesIgnorePlayers")
                        {
                            ConvertSamSiteFlag();
                        }
                        else if (value == "TrapsIgnoreScientists")
                        {
                            ConvertTrapsIgnoreScientists();
                        }
                        else if (value == "TurretsIgnoreScientists")
                        {
                            ConvertTurretsIgnoreScientists("TurretsIgnoreScientists", "TurretsIgnoreScientist", RuleFlags.TurretsIgnoreScientist);
                        }
                        else if (value == "StaticTurretsIgnoreScientists")
                        {
                            ConvertTurretsIgnoreScientists("StaticTurretsIgnoreScientists", "StaticTurretsIgnoreScientist", RuleFlags.StaticTurretsIgnoreScientist);
                        }
                        else
                        {
                            Puts("WARNING - invalid flag: '{0}' (does this flag still exist?)", value);
                        }
                    }
                    else if (!HasFlag(flag))
                    {
                        _flags |= flag;
                    }
                }
                if (Changed)
                {
                    instance.SaveConfig();
                    Changed = false;
                }
            }

            public void InitializeRuleDictionary()
            {
                if (parsedRules != null)
                {
                    ruleDictionary = new(StringComparer.OrdinalIgnoreCase);

                    foreach (Rule rule in parsedRules)
                    {
                        if (rule != null && rule.valid && rule.key != null)
                        {
                            ruleDictionary[rule.key] = rule;
                        }
                    }
                }
                else
                {
                    ruleDictionary = null;
                }
            }

            private void ConvertSamSiteFlag()
            {
                flags = flags.Replace("SamSitesIgnorePlayers", "PlayerSamSitesIgnorePlayers, StaticSamSitesIgnorePlayers");
                if (!HasFlag(RuleFlags.PlayerSamSitesIgnorePlayers))
                {
                    _flags |= RuleFlags.PlayerSamSitesIgnorePlayers;
                }
                if (!HasFlag(RuleFlags.StaticSamSitesIgnorePlayers))
                {
                    _flags |= RuleFlags.StaticSamSitesIgnorePlayers;
                }
                Changed = true;
            }

            private void ConvertTrapsIgnoreScientists()
            {
                flags = flags.Replace("TrapsIgnoreScientists", "TrapsIgnoreScientist");
                if (!HasFlag(RuleFlags.TrapsIgnoreScientist))
                {
                    _flags |= RuleFlags.TrapsIgnoreScientist;
                }
                Changed = true;
            }

            private void ConvertTurretsIgnoreScientists(string from, string to, RuleFlags flag)
            {
                flags = flags.Replace(from, to);
                if (!HasFlag(flag))
                {
                    _flags |= flag;
                }
                Changed = true;
            }

            public void ValidateRules()
            {
                foreach (Rule rule in parsedRules)
                    if (!rule.valid)
                        Interface.Oxide.LogWarning($"Warning - invalid rule: {rule.ruleText}");
            }

            // add a rule
            public void AddRule(TruePVE instance, string ruleText)
            {
                rules.Add(ruleText);
                parsedRules.Add(new(instance, ruleText));
            }

            public bool HasAnyFlag(RuleFlags flags) => (_flags | flags) != RuleFlags.None;
            public bool HasFlag(RuleFlags flag) => (_flags & flag) == flag;
            public bool IsEmpty() => rules.IsNullOrEmpty() && _flags == RuleFlags.None;
        }
        
        private class Rule
        {
            public string ruleText;
            internal string key;
            internal bool hurt;
            internal bool valid;

            public Rule() { }
            public Rule(TruePVE instance, string ruleText)
            {
                this.ruleText = ruleText;
                valid = Translate(instance);
            }

            public bool Translate(TruePVE instance)
            {
                if (string.IsNullOrWhiteSpace(ruleText))
                    return false;

                string[] splitStr = instance.regex.Split(ruleText.Trim());
                if (splitStr.Length < 3)
                    return false;

                string rs0 = splitStr[0];
                string rs1 = splitStr[^1]; // Using index from end operator
                string[] mid = splitStr[1..^1]; // Slicing the array

                bool canHurt = !Array.Exists(mid, s => s.Equals("cannot", StringComparison.OrdinalIgnoreCase) || s.Equals("can't", StringComparison.OrdinalIgnoreCase));

                // rs0 and rs1 shouldn't ever be "nothing" simultaneously
                if (rs0.Equals("nothing", StringComparison.OrdinalIgnoreCase) || rs1.Equals("nothing", StringComparison.OrdinalIgnoreCase) || rs0.Equals("none", StringComparison.OrdinalIgnoreCase) || rs1.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    canHurt = !canHurt;
                }

                if (instance.synonyms.Contains(rs0)) rs0 = Any;
                if (instance.synonyms.Contains(rs1)) rs1 = Any;

                key = rs0 + "->" + rs1;
                hurt = canHurt;
                return true;
            }

            public override int GetHashCode() => key.GetHashCode();

            public override bool Equals(object obj)
            {
                if (obj == null) return false;
                if (obj == this) return true;
                if (obj is Rule obj2)
                    return key.Equals(obj2.key);
                return false;
            }
        }

        private readonly Regex regex = new(@"\s+", RegexOptions.Compiled);

        private readonly HashSet<string> synonyms = new(StringComparer.OrdinalIgnoreCase)
        {
            "anything", "nothing", "all", "any", "none", "everything"
        };

        // container for mapping entities
        private class EntityGroup
        {
            public string name;

            internal readonly HashSet<string> _memberSet;
            internal readonly HashSet<string> _exclusionSet;

            private string _cachedMembersString = string.Empty;
            private string _cachedExclusionsString = string.Empty;

            private bool _isMembersDirty = true;
            private bool _isExclusionsDirty = true;

            public EntityGroup()
            {
                _memberSet = new(StringComparer.OrdinalIgnoreCase);
                _exclusionSet = new(StringComparer.OrdinalIgnoreCase);
            }

            public EntityGroup(string name) : this()
            {
                this.name = name;
            }

            public string members
            {
                get
                {
                    if (_isMembersDirty)
                    {
                        _cachedMembersString = string.Join(", ", _memberSet);
                        _isMembersDirty = false;
                    }
                    return _cachedMembersString;
                }
                set
                {
                    _memberSet.Clear();
                    if (!string.IsNullOrEmpty(value))
                    {
                        var members = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var member in members)
                        {
                            var trimmed = member.Trim();
                            if (!string.IsNullOrEmpty(trimmed))
                            {
                                _memberSet.Add(trimmed);
                            }
                        }
                    }

                    _isMembersDirty = true;
                }
            }

            public string exclusions
            {
                get
                {
                    if (_isExclusionsDirty)
                    {
                        _cachedExclusionsString = string.Join(", ", _exclusionSet);
                        _isExclusionsDirty = false;
                    }
                    return _cachedExclusionsString;
                }
                set
                {
                    _exclusionSet.Clear();
                    if (!string.IsNullOrEmpty(value))
                    {
                        var exclusions = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var exclusion in exclusions)
                        {
                            var trimmed = exclusion.Trim();
                            if (!string.IsNullOrEmpty(trimmed))
                            {
                                _exclusionSet.Add(trimmed);
                            }
                        }
                    }

                    _isExclusionsDirty = true;
                }
            }

            public bool IsMember(string value)
            {
                if (string.IsNullOrEmpty(value)) return false;
                return _memberSet.Contains(value);
            }

            public bool IsExclusion(string value)
            {
                if (string.IsNullOrEmpty(value)) return false;
                return _exclusionSet.Contains(value);
            }

            public bool Contains(string typeName, string prefabName)
            {
                return (_memberSet.Contains(typeName) || _memberSet.Contains(prefabName)) && !(_exclusionSet.Contains(typeName) || _exclusionSet.Contains(prefabName));
            }
        }

        // scheduler
        private class Schedule
        {
            public bool enabled;
            public bool useRealtime;
            public bool broadcast;
            public List<string> entries = new();
            internal List<ScheduleEntry> parsedEntries = new();
            internal bool valid;

            public void Init(TruePVE instance)
            {
                // Add entries to parsedEntries
                foreach (string str in entries)
                {
                    parsedEntries.Add(new(instance, str));
                }

                // Check if parsedEntries is null or empty
                if (parsedEntries == null || parsedEntries.Count == 0)
                {
                    enabled = false;
                    return;
                }

                // Count valid entries
                int validEntriesCount = 0;
                foreach (var entry in parsedEntries)
                {
                    if (entry.valid)
                    {
                        validEntriesCount++;
                    }
                }

                // If there are less than 2 valid entries, disable the schedule
                if (validEntriesCount < 2)
                {
                    enabled = false;
                    return;
                }

                // Collect all distinct ruleSets
                using var distinctRuleSets = Pool.Get<PooledHashSet<string>>();
                foreach (var entry in parsedEntries)
                {
                    distinctRuleSets.Add(entry.ruleSet);
                }

                // If there are less than 2 distinct ruleSets, disable the schedule
                if (distinctRuleSets.Count < 2)
                {
                    enabled = false;
                }
                else
                {
                    valid = true;
                }
            }

            // returns delta between current time and next schedule entry
            public void ClockUpdate(out string ruleSetName, out string message)
            {
                // Determine the current TimeSpan based on useRealtime
                TimeSpan currentTime = default;
                if (useRealtime || TOD_Sky.Instance?.Cycle == null)
                {
                    // Create a TimeSpan representing the total number of days since Sunday
                    currentTime = new TimeSpan((int)DateTime.Now.DayOfWeek, 0, 0, 0).Add(DateTime.Now.TimeOfDay);
                }
                else
                {
                    currentTime = TOD_Sky.Instance.Cycle.DateTime.TimeOfDay;
                }

                ScheduleEntry se = null;

                // Step 1: Check for non-daily entries
                bool hasNonDaily = false;
                foreach (var entry in parsedEntries)
                {
                    if (!entry.isDaily)
                    {
                        hasNonDaily = true;
                        break; // Early exit once a non-daily entry is found
                    }
                }

                // Step 2: Find the most recent valid non-daily entry <= currentTime
                if (hasNonDaily)
                {
                    TimeSpan? maxTime = null;
                    foreach (var entry in parsedEntries)
                    {
                        if (!entry.valid || entry.isDaily) continue; // only non-daily here

                        if (entry.time <= currentTime)
                        {
                            if (!maxTime.HasValue || entry.time > maxTime.Value)
                                maxTime = entry.time;
                        }
                    }

                    if (maxTime.HasValue)
                    {
                        foreach (var entry in parsedEntries)
                        {
                            if (entry.valid && !entry.isDaily && entry.time == maxTime.Value)
                            {
                                se = entry;
                                break; // Exit once the first matching entry is found
                            }
                        }
                    }
                    else
                    {
                        // No non-daily entry in the current week segment (e.g., it's early Sunday).
                        // Fall back to the latest non-daily entry overall (previous week's last).
                        TimeSpan latest = TimeSpan.MinValue;
                        ScheduleEntry latestEntry = null;
                        foreach (var entry in parsedEntries)
                        {
                            if (!entry.valid || entry.isDaily) continue;
                            if (entry.time > latest)
                            {
                                latest = entry.time;
                                latestEntry = entry;
                            }
                        }
                        if (latestEntry != null)
                            se = latestEntry;
                    }
                }

                // Step 3: Handle daily entries if useRealtime is true
                if (useRealtime)
                {
                    ScheduleEntry daily = null;
                    TimeSpan maxDailyTime = TimeSpan.Zero;
                    bool hasValidDaily = false;

                    // Find the maximum time among valid daily entries <= current real-time
                    foreach (var entry in parsedEntries)
                    {
                        if (entry.valid && entry.isDaily && entry.time <= DateTime.Now.TimeOfDay)
                        {
                            if (!hasValidDaily || entry.time > maxDailyTime)
                            {
                                maxDailyTime = entry.time;
                                hasValidDaily = true;
                            }
                        }
                    }

                    if (hasValidDaily)
                    {
                        foreach (var entry in parsedEntries)
                        {
                            if (entry.valid && entry.isDaily && entry.time == maxDailyTime)
                            {
                                daily = entry;
                                break;
                            }
                        }
                    }
                    else
                    {
                        // No daily entry earlier today -> use the last daily of the day
                        TimeSpan lastTime = TimeSpan.Zero;
                        ScheduleEntry lastEntry = null;
                        foreach (var entry in parsedEntries)
                        {
                            if (!entry.valid || !entry.isDaily) continue;
                                if (lastEntry == null || entry.time > lastTime)
                                {
                                    lastTime = entry.time;
                                    lastEntry = entry;
                            }
                        }
                        daily = lastEntry;
                    }

                    if (daily != null)
                    {
                        // Compare on the same "week clock" axis
                            // Create a TimeSpan representing the day's offset
                            TimeSpan dayOffset = new((int)DateTime.Now.DayOfWeek, 0, 0, 0);
                            TimeSpan dailyAdjustedTime = daily.time.Add(dayOffset);

                        if (se == null || dailyAdjustedTime > se.time)
                            {
                                se = daily;
                        }
                    }
                }

                // Assign the output parameters
                ruleSetName = se?.ruleSet;
                message = se?.message;
            }
        }

        private class ScheduleEntry
        {
            public string ruleSet;
            public string message;
            public string scheduleText;
            public bool valid;
            public TimeSpan time;
            internal bool isDaily = false;

            public ScheduleEntry() { }

            public ScheduleEntry(TruePVE instance, string scheduleText)
            {
                this.scheduleText = scheduleText;
                valid = Translate(instance);
            }

            private bool Translate(TruePVE instance)
            {
                if (string.IsNullOrWhiteSpace(scheduleText))
                    return false;

                // Split the scheduleText into at most 3 parts: TimeSpan, RuleSet, Message
                string[] split = instance.regex.Split(scheduleText.Trim(), 3);
                if (split.Length < 2)
                {
                    return false; // At least TimeSpan and RuleSet are required
                }

                string ts = split[0];
                string rs = split[1];
                string msg = split.Length > 2 ? split[2] : string.Empty;

                // Check if the TimeSpan starts with "*." indicating a daily schedule
                if (ts.Length > 2 && ts.StartsWith("*.", StringComparison.Ordinal))
                {
                    isDaily = true;
                    ts = ts[2..]; // Remove the "*." prefix
                }

                if (!TimeSpan.TryParse(ts, out TimeSpan span))
                {
                    string c = ts[^1].ToString();
                    if (!c.IsNumeric())
                    {
                        Puts("Invalid last character '{0}' in time format '{1}'", c, ts);
                    }
                    else
                    {
                        Puts("Time format is invalid: {0}", ts);
                    }
                    return false;
                }

                time = span;
                ruleSet = rs;
                message = msg;

                return true;
            }

            public override int GetHashCode() => ruleSet != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(ruleSet) : 0;

            public override bool Equals(object obj)
            {
                if (obj is ScheduleEntry other)
                    return string.Equals(ruleSet, other.ruleSet, StringComparison.OrdinalIgnoreCase);
                return false;
            }
        }

#endregion

        #region Lang
        // load default messages to Lang
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new()
            {
                {"Prefix", "<color=#FFA500>[ TruePVE ]</color>" },
                {"Enable", "TruePVE enable set to {0}" },
                {"Twig", "<color=#ff0000>WARNING:</color> It is against server rules to destroy other players' items. Actions logged for admin review." },

                {"Header_Usage", "---- TruePVE usage ----"},
                {"Cmd_Usage_def", "Loads default configuration and data"},
                {"Cmd_Usage_sched", "Enable or disable the schedule" },
                {"Cmd_Usage_prod", "Show the prefab name and type of the entity being looked at"},
                {"Cmd_Usage_map", "Create/remove a mapping entry" },
                {"Cmd_Usage_mapzone", "Map current zone to a ruleset or exclude" },
                {"Cmd_Usage_unmapzone", "Unmap current zone" },
                {"Cmd_Usage_trace", "Toggle tracing on/off" },

                {"Warning_PveMode", "ConVar server.pve is TRUE while 'Use Game server.pve For Server Browser Tag' is false. Disable server.pve, or enable the browser-tag option so TruePVE can suppress vanilla reflect safely."},
                {"Warning_NoRuleSet", "No RuleSet found for \"{0}\"" },
                {"Warning_DuplicateRuleSet", "Multiple RuleSets found for \"{0}\"" },

                {"Error_InvalidCommand", "Invalid command" },
                {"Error_InvalidParameter", "Invalid parameter: {0}"},
                {"Error_InvalidParamForCmd", "Invalid parameters for command \"{0}\""},
                {"Error_InvalidMapping", "Invalid mapping: {0} => {1}; Target must be a valid RuleSet or \"exclude\"" },
                {"Error_NoMappingToDelete", "Cannot delete mapping: \"{0}\" does not exist" },
                {"Error_NoPermission", "Cannot execute command: No permission"},
                {"Error_NoSuicide", "You are not allowed to commit suicide"},
                {"Error_NoEntityFound", "No entity found"},

                {"Notify_AvailOptions", "Available Options: {0}"},
                {"Notify_DefConfigLoad", "Loaded default configuration"},
                {"Notify_DefDataLoad", "Loaded default mapping data"},
                {"Notify_ProdResult", "Prod results: type={0}, prefab={1}"},
                {"Notify_SchedSetEnabled", "Schedule enabled" },
                {"Notify_SchedSetDisabled", "Schedule disabled" },
                {"Notify_InvalidSchedule", "Schedule is not valid" },
                {"Notify_MappingCreated", "Mapping created for \"{0}\" => \"{1}\"" },
                {"Notify_MappingUpdated", "Mapping for \"{0}\" changed from \"{1}\" to \"{2}\"" },
                {"Notify_MappingDeleted", "Mapping for \"{0}\" => \"{1}\" deleted" },
                {"Notify_TraceToggle", "Trace mode toggled {0}" },

                {"Format_EnableColor", "#00FFFF"}, // cyan
                {"Format_EnableSize", "12"},
                {"Format_NotifyColor", "#00FFFF"}, // cyan
                {"Format_NotifySize", "12"},
                {"Format_HeaderColor", "#FFA500"}, // orange
                {"Format_HeaderSize", "14"},
                {"Format_ErrorColor", "#FF0000"}, // red
                {"Format_ErrorSize", "12"},

                {"Error_TimeLeft", "You must wait another {0} hours to attack this player."},
                {"Error_OfflineTimeLeft", "You must wait another {0} to attack this player."},
                {"Error_MasterKeyDisabled", "Apartment master keys are disabled." },
                {"Error_BribeDisabled", "Bribing the security guard is disabled." },
                {"Error_Harvest", "You are not allowed to plant or harvest this."},
                {"Error_CannotAccessEntity", "You are not allowed to access this" },
                {"Notify_LockedToYou", "This loot is locked to you/your team."},
                {"Notify_LockedToOthers", "This loot is locked to another player/team."},
                {"Notify_HeliDestroyed", "Patrol Helicopter destroyed in {0}. Top damage: {1}"},
                {"Notify_BradleyDestroyed", "Bradley APC destroyed in {0}. Top damage: {1}"},
                {"Notify_HeliOwned", "Patrol Helicopter currently owned by: {0}"},
                {"Notify_BradleyOwned", "Bradley APC currently owned by: {0}"},
                {"Notify_LootOwned", "Loot owned by: {0}"},
                {"Notify_NPCLocked", "{0} has been locked to {1} and their team"},
                {"ShoppyStockReward", "Added {0} {1} to your account."},
                {"NoLockouts", "You have no lockouts."},
                {"HeliKilled", "A heli was killed."},
                {"BradleyKilled", "A bradley was killed."},
                {"HeliUnlocked", "The heli at {0} has been unlocked."},
                {"BradleyUnlocked", "The bradley at {0} has been unlocked."},
                
                // PreventLooting messages
                {"OnTryLootPlayer", "You can not loot players!"},
                {"OnTryLootCorpse", "You can not loot corpses of players!"},
                {"OnTryLootEntity", "You can not use this entity because it is not yours!"},
                {"OnTryLootWeaponRack", "You can not use this weapon rack because it is not yours!"},
                {"OnTryLootBackpack", "You can not open this backpack because it is not yours!"},
                {"OnTryPickup", "You can not pickup this because it is not yours!"},
                {"NoAccess", "This entity is not yours!"},
                {"PlayerNotFound", "Player {0} not found!"},
                {"ShareAll", "All players were given permission to use this entity!"},
                {"SharePlayer", "The player {0} was given permission to use this entity!"},
                {"NoShare", "No permissions have been found for this entity!"},
                {"ListShare", "List of permissions for this entity:"},
                {"EntityNotFound", "You are not standing in front of the entity or away from it!"},
                {"HasShareAllList", "All players are allowed to use this entity!"},
                {"ShareClear", "All permissions for this entity have been deleted!"},
                {"HasShareAll", "All players already have permission to use this entity!"},
                {"HasSharePlayer", "Player {0} already has permission to use this entity!"},
                {"HasUnShareAll", "Permission to use this entity has not been issued to all players!"},
                {"HasUnSharePlayer", "Player {0} has not been granted permission to use this entity!"},
                {"WasUnShareAll", "All players have been removed permission for this entity!"},
                {"WasUnSharePlayer", "The permission to use this entity has been removed from player {0}!"},
                {"MultiplePlayerFind", "Multiple players found:"},
                {"OwnEntity", "This object is yours!"},
                {"NoPermission", "You do not have enough rights to execute this command!"},
                {"EntPrevent", "This entity is protected!"},
                {"EntNoPrevent", "This entity is not protected!"},
                {"OnTryOnOff", "You can not turn on or off this entity because it is not yours!"},
                {"OnTryAuthCB", "You can not authorize in cupboard because it is not yours!"},
                {"InvalidSearch", "Name is too short or invalid SteamID... Please try again."},
                
                // Planterbox protection messages
                {"Planterbox_NoHarvest", "You can't harvest this planterbox. It belongs to another player."},
                {"Planterbox_NoPlant", "You can't plant seeds in this planterbox. It belongs to another player."},
            }, this);
            
            // Russian translations for PreventLooting
            lang.RegisterMessages(new Dictionary<string, string>
            {
                {"OnTryLootPlayer", "Вы не можете обворовывать игроков!"},
                {"OnTryLootCorpse", "Вы не можете обворовывать трупы игроков!"},
                {"OnTryLootEntity", "Вы не можете использовать этот объект, потому что он вам не принадлежит!"},
                {"OnTryLootWeaponRack", "Вы не можете использовать эту оружейную стойку, потому что она вам не принадлежит!"},
                {"OnTryLootBackpack", "Вы не можете открыть чужой рюкзак!"},
                {"OnTryPickup", "Вы не можете взять чужое!"},
                {"NoAccess", "Этот объект не принадлежит вам!"},
                {"PlayerNotFound", "Игрок с именем {0} не найден!"},
                {"ShareAll", "Всем игрокам было выдано разрешение на использование этого объекта!"},
                {"SharePlayer", "Игроку {0} было выдано разрешение на использование этого объекта!"},
                {"NoShare", "Не найдено разрешений для этого объекта!"},
                {"ListShare", "Список разрешений для этого объекта:"},
                {"EntityNotFound", "Вы стоите не перед хранилищем или далеко от него!"},
                {"HasShareAllList", "Всем игрокам разрешено использовать этот объект!"},
                {"ShareClear", "Все разрешения для этого объекта были удалены!"},
                {"HasShareAll", "Все игроки уже имеют разрешение на использование этого объекта!"},
                {"HasSharePlayer", "Игрок {0} уже имеет разрешение на использование этого объекта!"},
                {"HasUnShareAll", "Разрешение на использование этого объекта не было выдано для всех игроков!"},
                {"HasUnSharePlayer", "Игроку {0} не было выдано разрешение на использование этого объекта!"},
                {"WasUnShareAll", "Всем игрокам было удалено разрешение на использование этого объекта!"},
                {"WasUnSharePlayer", "Игроку {0} было удалено разрешение на использование этого объекта!"},
                {"MultiplePlayerFind", "Найдено несколько игроков:"},
                {"OwnEntity", "Этот объект принадлежит вам!"},
                {"NoPermission", "У вас недостаточно прав для выполнения этой команды!"},
                {"EntPrevent", "Этот предмет защищен от воровства!"},
                {"EntNoPrevent", "Этот предмет не защищен от воровства!"},
                {"OnTryOnOff", "Вы не можете включить или выключить этот объект, потому что он вам не принадлежит!"},
                {"OnTryAuthCB", "Вы не можете авторизоваться в чужом шкафу, потому что он вам не принадлежит!"},
            }, this, "ru");
        }

        // get message from Lang
        private string GetMessage(string key, string userId = null) => lang.GetMessage(key, this, userId);
        #endregion
    }
}