using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using Facepunch;

namespace ZombieHorde
{
    public class ConfigData
    {
        public static ConfigData Configuration;

    [JsonProperty(PropertyName = "Horde Options")]
    public HordeOptions Horde { get; set; }

    [JsonProperty(PropertyName = "Horde Member Options")]
    public MemberOptions Member { get; set; }

    [JsonProperty(PropertyName = "Loot Table")]
    public LootTable Loot { get; set; }

    [JsonProperty(PropertyName = "Monument Spawn Options")]
    public MonumentSpawn Monument { get; set; }

    [JsonProperty(PropertyName = "Timed Spawn Options")]
    public TimedSpawnOptions TimedSpawns { get; set; }

    [JsonProperty(PropertyName = "Horde Profiles (profile name, list of applicable loadouts)")]
    public Dictionary<string, List<string>> HordeProfiles { get; set; }

    public class TimedSpawnOptions
    {
        [JsonProperty(PropertyName = "Only allows spawns during the set time period")]
        public bool Enabled { get; set; }

        [JsonProperty(PropertyName = "Despawn hordes outside of the set time period")]
        public bool Despawn { get; set; }

        [JsonProperty(PropertyName = "Start time (0.0 - 24.0)")]
        public float Start { get; set; }

        [JsonProperty(PropertyName = "End time (0.0 - 24.0)")]
        public float End { get; set; }
        
        [JsonProperty(PropertyName = "Broadcast notification when hordes start spawning")]
        public bool BroadcastStart { get; set; }
        
        [JsonProperty(PropertyName = "Broadcast notification when hordes start despawning")]
        public bool BroadcastEnd { get; set; }
    }

    public class HordeOptions
    {
        [JsonProperty(PropertyName = "Amount of zombies to spawn when a new horde is created")]
        public int InitialMemberCount { get; set; }

        [JsonProperty(PropertyName = "Maximum amount of spawned zombies per horde")]
        public int MaximumMemberCount { get; set; }

        [JsonProperty(PropertyName = "Maximum amount of hordes at any given time")]
        public int MaximumHordes { get; set; }

        [JsonProperty(PropertyName = "Amount of time from when a horde is destroyed until a new horde is created (seconds)")]
        public int RespawnTime { get; set; }

        [JsonProperty(PropertyName = "Amount of time before a horde grows in size")]
        public int GrowthRate { get; set; }

        [JsonProperty(PropertyName = "Add a zombie to the horde when a horde member kills a player")]
        public bool CreateOnDeath { get; set; }

        [JsonProperty(PropertyName = "Merge hordes together if they collide")]
        public bool MergeHordes { get; set; }

        [JsonProperty(PropertyName = "Spawn system (SpawnsDatabase, Random)")]
        public string SpawnType { get; set; }

        [JsonProperty(PropertyName = "Spawn file (only required when using SpawnsDatabase)")]
        public string SpawnFile { get; set; }

        [JsonProperty(PropertyName = "Amount of time a player needs to be outside of a zombies vision before it forgets about them")]
        public float ForgetTime { get; set; }

        [JsonProperty(PropertyName = "Default roam speed (Slowest, Slow, Normal, Fast)")]
        public string DefaultRoamSpeed { get; set; }

        [JsonProperty(PropertyName = "Force all hordes to roam locally")]
        public bool LocalRoam { get; set; }

        [JsonProperty(PropertyName = "Local roam distance")]
        public float RoamDistance { get; set; }

        [JsonProperty(PropertyName = "Restrict chase distance for local hordes (1.5x the maximum roam distance for that horde)")]
        public bool RestrictLocalChaseDistance { get; set; }

        [JsonProperty(PropertyName = "Use horde profiles for randomly spawned hordes")]
        public bool UseProfiles { get; set; }

        [JsonProperty(PropertyName = "Specific horde profiles for randomly spawned hordes")]
        public List<string> RandomProfiles { get; set; } = new List<string>();

        [JsonProperty(PropertyName = "Sense nearby gunshots and explosions")]
        public bool UseSenses { get; set; }

        [JsonProperty(PropertyName = "Raid online players who are inside or on bases")]
        public bool RaidOnlinePlayersAtBases { get; set; }

        [JsonProperty(PropertyName = "Online base raid scan range")]
        public float OnlineBaseRaidScanRange { get; set; }

        [JsonProperty(PropertyName = "Online base raid scan interval (seconds)")]
        public float OnlineBaseRaidScanInterval { get; set; }
    }

    public class MemberOptions
    {
        [JsonProperty(PropertyName = "Can target animals")]
        public bool TargetAnimals { get; set; }

        [JsonProperty(PropertyName = "Can be targeted by turrets")]
        public bool TargetedByTurrets { get; set; }
        
        [JsonProperty(PropertyName = "Can be targeted by NPC turrets")]
        public bool TargetedByNPCTurrets { get; set; }

        [JsonProperty(PropertyName = "Can be targeted by peacekeeper turrets and NPC turrets")]
        public bool TargetedByPeaceKeeperTurrets { get; set; }

        [JsonProperty(PropertyName = "Can be targeted by Bradley APC")]
        public bool TargetedByAPC { get; set; }

        [JsonProperty(PropertyName = "Can be targeted by other NPCs")]
        public bool TargetedByNPCs { get; set; }

        [JsonProperty(PropertyName = "Can be targeted by animals")]
        public bool TargetedByAnimals { get; set; }

        [JsonProperty(PropertyName = "Can target other NPCs")]
        public bool TargetNPCs { get; set; }

        [JsonProperty(PropertyName = "Can target other NPCs that attack zombies")]
        public bool TargetNPCsThatAttack { get; set; }

        [JsonProperty(PropertyName = "Can target NPCs from HumanNPC")]
        public bool TargetHumanNPCs { get; set; }

        [JsonProperty(PropertyName = "Ignore sleeping players")]
        public bool IgnoreSleepers { get; set; }

        [JsonProperty(PropertyName = "Give all zombies glowing eyes")]
        public bool GiveGlowEyes { get; set; }

        [JsonProperty(PropertyName = "Headshots instantly kill zombie")]
        public bool HeadshotKills { get; set; }
        
        [JsonProperty(PropertyName = "Minimum damage required for a headshot kill")]
        public float MinimumHeadshotDamage { get; set; }

        [JsonProperty(PropertyName = "Kill NPCs that are under water")]
        public bool KillUnderWater { get; set; }

        [JsonProperty(PropertyName = "Can zombies swim across water")]
        public bool CanSwim { get; set; }

        [JsonProperty(PropertyName = "Enable NPC dormant system. This will put NPCs to sleep when no players are nearby to improve performance")]
        public bool EnableDormantSystem { get; set; }

        [JsonProperty(PropertyName = "Dormant until sensed or damaged: no wake from nearby players only (requires Sense nearby gunshots and explosions for audio wake)")]
        public bool DormantUntilSensedOnly { get; set; }

        [JsonProperty(PropertyName = "Zombies make zombie sounds")]
        public bool EnableZombieNoises { get; set; }
        
        [JsonProperty(PropertyName = "Continue to target players who hide in buildings")]
        public bool TargetInBuildings { get; set; }
        
        [JsonProperty(PropertyName = "Throwable explosive building damage multiplier")]
        public float ExplosiveBuildingDamageMultiplier { get; set; }
        
        [JsonProperty(PropertyName = "Maximum explosive throw range")]
        public float MaxExplosiveThrowRange { get; set; }
        
        [JsonProperty(PropertyName = "Despawn dud explosives thrown by Zombies")]
        public bool DespawnDudExplosives { get; set; }
        
        [JsonProperty(PropertyName = "Make dud explosives thrown by Zombies explode anyway")]
        public bool ExplodeDudExplosives { get; set; }

        [JsonProperty(PropertyName = "Don't apply the building damage multiplier if the target is not the owner or authed on the TC")]
        public bool IgnoreBuildingMultiplierNotOwner { get; set; }
        
        [JsonProperty(PropertyName = "Don't apply building damage if the target is not the owner or authed on the TC")]
        public bool DisableBuildingMultiplierNotOwner { get; set; }
        
        [JsonProperty(PropertyName = "Melee weapon building damage multiplier")]
        public float MeleeBuildingDamageMultiplier { get; set; }
        
        [JsonProperty(PropertyName = "Zombies can mount vehicles if target player mounts it")]
        public bool CanMountVehicles { get; set; }
        
        [JsonProperty(PropertyName = "Consume throwable items when using")]
        public bool ConsumeThrowables { get; set; }
        
        [JsonProperty(PropertyName = "Make zombies gingerbread men")]
        public bool GingerBreadZombies { get; set; }
        
        [JsonProperty(PropertyName = "Corpse despawn time (0 is default behavior)")]
        public float CorpseDespawnTime { get; set; }
        
        public List<Loadout> Loadouts { get; set; }

        [JsonIgnore]
        private EntityType _senseTypes = 0;

        public EntityType GetSenseTypes()
        {
            if (_senseTypes == 0)
            {
                _senseTypes |= EntityType.Player;

                if (TargetNPCs)
                    _senseTypes |= EntityType.BasePlayerNPC;

                if (TargetAnimals)
                    _senseTypes |= EntityType.NPC;
            }
            return _senseTypes;
        }

        public class Loadout
        {
            public string LoadoutID { get; set; }

            [JsonProperty(PropertyName = "Potential names for zombies using this loadout (chosen at random)")]
            public string[] Names { get; set; }

            [JsonProperty(PropertyName = "Damage multiplier")]
            public float DamageMultiplier { get; set; }

            [JsonProperty(PropertyName = "Aim cone scale (for projectile weapons)")]
            public float AimConeScale { get; set; }

            public NPCSettings.VitalStats Vitals { get; set; }

            public ZombieMovementStats Movement { get; set; }

            public NPCSettings.SensoryStats Sensory { get; set; }

            public List<LootTable.InventoryItem> BeltItems { get; set; }

            public List<LootTable.InventoryItem> MainItems { get; set; }

            public List<LootTable.InventoryItem> WearItems { get; set; }

            [JsonProperty(PropertyName = "Random loot override (applies to this profile only)")]
            public LootTable.RandomLoot LootOverride { get; set; } = new LootTable.RandomLoot();

            [JsonProperty(PropertyName = "AlphaLoot profiles as loot override (applies to this profile only)")]
            public string[] DropAlphaLootOverride { get; set; } = Array.Empty<string>();

            public class ZombieMovementStats : NPCSettings.MovementStats
            {
                public override void ApplySettingsToNavigator(BaseNavigator baseNavigator)
                {
                    if (baseNavigator == null) return;

                    base.ApplySettingsToNavigator(baseNavigator);

                    baseNavigator.topologyPreference = (TerrainTopology.Enum)1673010749;

                    if (ConfigData.Configuration?.Member != null && ConfigData.Configuration.Member.CanSwim)
                        baseNavigator.SwimmingSpeedMultiplier = 0.4f;
                }
            }

            [JsonIgnore]
            private NPCSettings _npcSettings;

            [JsonIgnore]
            public NPCSettings NPCSettings
            {
                get
                {
                    if (_npcSettings == null)
                    {
                        _npcSettings = new NPCSettings
                        {
                            Types = new NPCType[] { ConfigData.Configuration.Member.GingerBreadZombies ? NPCType.GingerBreadMan : NPCType.Scarecrow },
                            AimConeScale = AimConeScale,
                            DisplayNames = Names,
                            Vitals = Vitals,
                            Movement = Movement,
                            Sensory = Sensory,
                            KillUnderWater = !ConfigData.Configuration.Member.CanSwim,
                            StripCorpseLoot = ConfigData.Configuration.Loot.DropInventory,
                            DropInventoryOnDeath = ConfigData.Configuration.Loot.DropInventory,
                            EnableNavMesh = false,
                            TargetedByNPCTurrets = ConfigData.Configuration.Member.TargetedByNPCTurrets,
                            DropAlphaLootProfiles = DropAlphaLootOverride?.Length > 0 ? DropAlphaLootOverride : ConfigData.Configuration.Loot.DropAlphaLootProfiles
                        };

                        _npcSettings.Movement.CanSwim = ConfigData.Configuration.Member.CanSwim;
                    }

                    return _npcSettings;
                }
            }

            [JsonIgnore]
            private static Dictionary<string, float> _effectiveRangeDefaults = new Dictionary<string, float>();

            [JsonIgnore]
            private static ItemDefinition _glowEyes;

            [JsonIgnore]
            public static ItemDefinition GlowEyes
            {
                get
                {
                    if (_glowEyes == null)
                        _glowEyes = ItemManager.FindItemDefinition("gloweyes");
                    return _glowEyes;
                }
            }

            public Loadout()
            {
                Names = new string[] { "Zombie" };

                DamageMultiplier = 1f;

                AimConeScale = 2f;

                Vitals = new NPCSettings.VitalStats();

                Movement = new ZombieMovementStats();

                Sensory = new NPCSettings.SensoryStats();

                BeltItems = new List<LootTable.InventoryItem>();
                MainItems = new List<LootTable.InventoryItem>();
                WearItems = new List<LootTable.InventoryItem>();
            }

            public Loadout(string loadoutID) : this()
            {
                LoadoutID = loadoutID;
            }

            internal void GiveToPlayer(ZombieNPC zombieNpc, bool applyInventory = true)
            {
                if (zombieNpc == null || zombieNpc.Npc == null)
                    return;

                var inventory = zombieNpc.Npc.inventory;
                if (inventory == null)
                    return;

                if (applyInventory)
                {
                    inventory.Strip();

                    foreach (LootTable.InventoryItem inventoryItem in BeltItems)
                    {
                        Item item = inventoryItem.Give(inventory.containerBelt);
                        AdjustHeldRanges(item);
                    }

                    foreach (LootTable.InventoryItem inventoryItem in MainItems)
                        inventoryItem.Give(inventory.containerMain);

                    if (ConfigData.Configuration.Member.GingerBreadZombies)
                    {
                        Item item = ItemManager.CreateByName("gingerbreadsuit");
                        item?.MoveToContainer(inventory.containerWear);
                    }
                    else
                    {
                        foreach (LootTable.InventoryItem inventoryItem in WearItems)
                            inventoryItem.Give(inventory.containerWear);

                        if (ConfigData.Configuration.Member.GiveGlowEyes)
                        {
                            Item item = ItemManager.Create(GlowEyes);
                            if (!item.MoveToContainer(inventory.containerWear))
                                item.Remove(0f);
                        }
                    }
                }
                else
                {
                    // GrimmNPC already equipped wear/belt — only tweak effective ranges on existing belt items
                    if (inventory.containerBelt?.itemList != null)
                    {
                        foreach (Item item in inventory.containerBelt.itemList)
                            AdjustHeldRanges(item);
                    }
                }
            }

            private void AdjustHeldRanges(Item item)
            {
                if (item == null) return;
                HeldEntity heldEntity = item.GetHeldEntity() as HeldEntity;
                if (heldEntity == null) return;

                if (heldEntity is BaseProjectile projectile)
                {
                    if (!_effectiveRangeDefaults.ContainsKey(item.info.shortname))
                        _effectiveRangeDefaults[item.info.shortname] = projectile.effectiveRange;

                    if (ProjectileEffectiveRange.TryGetValue(item.info.shortname, out float effectiveRange))
                        projectile.effectiveRange = effectiveRange;
                    else projectile.effectiveRange *= 1.25f;
                }

                if (heldEntity is BaseMelee melee)
                {
                    if (!_effectiveRangeDefaults.ContainsKey(item.info.shortname))
                        _effectiveRangeDefaults[item.info.shortname] = melee.effectiveRange;

                    melee.effectiveRange *= 1.5f;
                }
            }

            private static readonly Dictionary<string, float> ProjectileEffectiveRange = new Dictionary<string, float>
            {
                ["bow.compound"] = 20,
                ["bow.hunting"] = 20,
                ["crossbow"] = 20,
                ["flamethrower"] = 8,
                ["gun.water"] = 10,
                ["lmg.m249"] = 150,
                ["multiplegrenadelauncher"] = 20,
                ["pistol.eoka"] = 5,
                ["pistol.m92"] = 15,
                ["pistol.nailgun"] = 10,
                ["pistol.python"] = 15,
                ["pistol.revolver"] = 15,
                ["pistol.semiauto"] = 15,
                ["pistol.water"] = 10,
                ["rifle.ak"] = 30,
                ["rifle.bolt"] = 80,
                ["rifle.l96"] = 100,
                ["rifle.lr300"] = 40,
                ["rifle.m39"] = 30,
                ["rifle.semiauto"] = 20,
                ["rocket.launcher"] = 20,
                ["shotgun.double"] = 15,
                ["shotgun.pump"] = 15,
                ["shotgun.spas12"] = 15,
                ["shotgun.waterpipe"] = 10,
                ["smg.2"] = 20,
                ["smg.mp5"] = 20,
                ["smg.thompson"] = 20,
                ["snowballgun"] = 10,
                ["speargun"] = 10,
            };

            public static bool GetDefaultEffectiveRange(string shortname, out float value) => _effectiveRangeDefaults.TryGetValue(shortname, out value);
        }
    }

    public class LootTable
    {
        [JsonProperty(PropertyName = "Drop inventory on death instead of random loot")]
        public bool DropInventory { get; set; }
        
        [JsonProperty(PropertyName = "Drop default murderer loot on death instead of random loot")]
        public bool DropDefault { get; set; }
        
        [JsonProperty(PropertyName = "Drop one of the specified AlphaLoot profiles as loot")]
        public string[] DropAlphaLootProfiles { get; set; }

        [JsonProperty(PropertyName = "Random loot table")]
        public RandomLoot Random { get; set; }

        [JsonProperty(PropertyName = "Dropped inventory item blacklist (shortnames)")]
        public string[] DroppedBlacklist { get; set; }

        public class InventoryItem
        {
            public string Shortname { get; set; }
            public ulong SkinID { get; set; }
            public int Amount { get; set; }

            [JsonProperty(PropertyName = "Attachments", NullValueHandling = NullValueHandling.Ignore)]
            public InventoryItem[] SubSpawn { get; set; }

            public Item Give(ItemContainer itemContainer)
            {
                Item item = ItemManager.CreateByName(Shortname, Amount, SkinID);
                if (item == null)
                    return null;

                if (!item.MoveToContainer(itemContainer))
                {
                    item.Remove(0f);
                    return null;
                }

                if (item.contents != null && SubSpawn?.Length > 0)
                {
                    for (int i = 0; i < SubSpawn.Length; i++)
                        SubSpawn[i].Give(item.contents);
                }

                return item;
            }
        }

        public class RandomLoot
        {
            [JsonProperty(PropertyName = "Minimum amount of items to spawn")]
            public int Minimum { get; set; } = 0;

            [JsonProperty(PropertyName = "Maximum amount of items to spawn")]
            public int Maximum { get; set; } = 0;

            public List<LootDefinition> List { get; set; } = new List<LootDefinition>();

            public class LootDefinition
            {
                public string Shortname { get; set; }

                public string ItemName { get; set; } = string.Empty;
                
                public int Minimum { get; set; }

                public int Maximum { get; set; }

                public ulong SkinID { get; set; }

                [JsonProperty(PropertyName = "Spawn as blueprint")]
                public bool IsBlueprint { get; set; }

                [JsonProperty(PropertyName = "Probability (0.0 - 1.0)")]
                public float Probability { get; set; }

                [JsonProperty(PropertyName = "Minimum condition (0.0 - 1.0)")]
                public float MinCondition { get; set; } = 1f;

                [JsonProperty(PropertyName = "Maximum condition (0.0 - 1.0)")]
                public float MaxCondition { get; set; } = 1f;

                [JsonProperty(PropertyName = "Spawn with")]
                public LootDefinition Required { get; set; }

                [JsonIgnore]
                private ItemDefinition _blueprintDefinition;

                [JsonIgnore]
                private ItemDefinition BlueprintDefinition
                {
                    get
                    {
                        if (_blueprintDefinition == null)
                            _blueprintDefinition = ItemManager.FindItemDefinition("blueprintbase");
                        return _blueprintDefinition;
                    }
                }

                private int GetAmount()
                {
                    if (Maximum <= 0f || Maximum <= Minimum)
                        return Minimum;

                    return UnityEngine.Random.Range(Minimum, Maximum);
                }

                public void Create(ItemContainer container)
                {
                    Item item;

                    if (!IsBlueprint)
                        item = ItemManager.CreateByName(Shortname, GetAmount(), SkinID);
                    else
                    {
                        item = ItemManager.Create(BlueprintDefinition);
                        item.blueprintTarget = ItemManager.FindItemDefinition(Shortname).itemid;
                    }

                    if (item != null)
                    {
                        if (!string.IsNullOrEmpty(ItemName))
                            item.name = ItemName;
                        
                        if (!IsBlueprint)
                            item.conditionNormalized = UnityEngine.Random.Range(Mathf.Clamp01(MinCondition), Mathf.Clamp01(MaxCondition));

                        item.OnVirginSpawn();
                        if (!item.MoveToContainer(container, -1, true))
                            item.Remove(0f);
                    }

                    Required?.Create(container);
                }
            }
        }
    }

    public class MonumentSpawn
    {
        public MonumentSettings ArcticResearch { get; set; }
        public MonumentSettings Airfield { get; set; }
        public MonumentSettings Dome { get; set; }
        public MonumentSettings Junkyard { get; set; }
        public MonumentSettings Ferry { get; set; }
        public MonumentSettings LargeHarbor { get; set; }
        public MonumentSettings GasStation { get; set; }
        public MonumentSettings Powerplant { get; set; }
        public MonumentSettings StoneQuarry { get; set; }
        public MonumentSettings SulfurQuarry { get; set; }
        public MonumentSettings HQMQuarry { get; set; }
        public MonumentSettings Radtown { get; set; }
        public MonumentSettings LegacyRadtown { get; set; }
        public MonumentSettings LaunchSite { get; set; }
        public MonumentSettings Satellite { get; set; }
        public MonumentSettings SmallHarbor { get; set; }
        public MonumentSettings Supermarket { get; set; }
        public MonumentSettings Trainyard { get; set; }
        public MonumentSettings Tunnels { get; set; }
        public MonumentSettings Warehouse { get; set; }
        public MonumentSettings WaterTreatment { get; set; }

        public List<CustomSpawnPoints> Custom { get; set; }

        public class MonumentSettings : SpawnSettings
        {
            [JsonProperty(PropertyName = "Enable spawns at this monument")]
            public bool Enabled { get; set; }
        }

        public class CustomSpawnPoints : MonumentSettings
        {
            public SerializedVector Location { get; set; }

            public class SerializedVector
            {
                public float X { get; set; }
                public float Y { get; set; }
                public float Z { get; set; }

                public bool IsValid => !Mathf.Approximately(X, 0f) || !Mathf.Approximately(Y, 0f) || !Mathf.Approximately(Z, 0f);
            
                public SerializedVector() { }

                public SerializedVector(float x, float y, float z)
                {
                    this.X = x;
                    this.Y = y;
                    this.Z = z;
                }

                public static implicit operator Vector3(SerializedVector v)
                {
                    return new Vector3(v.X, v.Y, v.Z);
                }

                public static implicit operator SerializedVector(Vector3 v)
                {
                    return new SerializedVector(v.x, v.y, v.z);
                }
            }
        }
    }

    public class SpawnSettings
    {
        [JsonProperty(PropertyName = "Distance that this horde can roam from their initial spawn point")]
        public float RoamDistance { get; set; }

        [JsonProperty(PropertyName = "Maximum amount of members in this horde")]
        public int HordeSize { get; set; }

        [JsonProperty(PropertyName = "Horde profile")]
        public string Profile { get; set; }
    }

    [JsonProperty(PropertyName = "Raiding Zombies Options")]
    public RaidingZombiesOptions Raiding { get; set; }

    public class RaidingZombiesOptions
    {
        [JsonProperty(PropertyName = "Chance of making group raiders")]
        public int Chance { get; set; } = 50;

        [JsonProperty(PropertyName = "Total raiders in the group")]
        public int TotalPerHorde { get; set; } = 3;

        [JsonProperty(PropertyName = "Total Explosives each raider has")]
        public int TotalExplosivesToUse { get; set; } = 5;

        [JsonProperty(PropertyName = "Target only bases of players he has seen")]
        public bool TargetPlayerOnly { get; set; } = true;

        [JsonProperty(PropertyName = "How long to forget a target he has seen")]
        public float ForgetTargetTime { get; set; } = 640f;

        [JsonProperty(PropertyName = "How far the Leader will scan for a base to raid")]
        public float BaseScanDistance { get; set; } = 40f;

        [JsonProperty(PropertyName = "How much scale damage explosives will do")]
        public float DamageScale { get; set; } = 10f;

        [JsonProperty(PropertyName = "Item shortname of Throwable item he can use")]
        public List<string> ThrowExplosiveItemTypes { get; set; } = new List<string> { "explosive.timed", "explosive.satchel" };

        [JsonProperty(PropertyName = "Rocket Prefab shortnames of rockets he can use")]
        public List<string> RocketPrefabTypes { get; set; } = new List<string> { "rocket_basic" };
    }

    public VersionNumber Version { get; set; }
    }
}