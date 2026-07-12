using System.Collections.Generic;
using Newtonsoft.Json;

namespace Convoy
{
    /// <summary>Full plugin config matching Oxide Convoy Convoy.json (English keys). Used for pathfinding + vehicle spawn.</summary>
    public class ConvoyPluginConfig
    {
        [JsonProperty("Version")]
        public string Version { get; set; }

        [JsonProperty("Prefix of chat messages")]
        public string Prefix { get; set; } = "[Convoy]";

        [JsonProperty("Main Setting")]
        public ConvoyMainConfig MainConfig { get; set; }

        [JsonProperty("Behavior Settings")]
        public ConvoyBehaviorConfig BehaviorConfig { get; set; }

        [JsonProperty("Loot Settings")]
        public ConvoyLootConfig LootConfig { get; set; }

        [JsonProperty("Route Settings")]
        public ConvoyPathConfig PathConfig { get; set; }

        [JsonProperty("Convoy Presets")]
        public HashSet<ConvoyEventConfig> EventConfigs { get; set; }

        [JsonProperty("Travelling Vendor Configurations")]
        public HashSet<ConvoyTravellingVendorConfig> TravelingVendorConfigs { get; set; }

        [JsonProperty("Modular Configurations")]
        public HashSet<ConvoyModularCarConfig> ModularCarConfigs { get; set; }

        [JsonProperty("Bradley Configurations")]
        public HashSet<ConvoyBradleyConfig> BradleyConfigs { get; set; }

        [JsonProperty("Sedan Configurations")]
        public HashSet<ConvoySedanConfig> SedanConfigs { get; set; }

        [JsonProperty("Bike Configurations")]
        public HashSet<ConvoyBikeConfig> BikeConfigs { get; set; }

        [JsonProperty("Karuza Car Configurations")]
        public HashSet<ConvoyKaruzaCarConfig> KaruzaCarConfigs { get; set; }

        [JsonProperty("Heli Configurations")]
        public HashSet<ConvoyHeliConfig> HeliConfigs { get; set; }

        [JsonProperty("Turret Configurations")]
        public HashSet<ConvoyTurretConfig> TurretConfigs { get; set; }

        [JsonProperty("SamSite Configurations")]
        public HashSet<ConvoySamSiteConfig> SamsiteConfigs { get; set; }

        [JsonProperty("Crate presets")]
        public HashSet<ConvoyCrateConfig> CrateConfigs { get; set; }

        [JsonProperty("NPC Configurations")]
        public HashSet<ConvoyNpcConfig> NpcConfigs { get; set; }

        [JsonProperty("Marker Setting")]
        public ConvoyMarkerConfigFull MarkerConfig { get; set; }

        [JsonProperty("Event zone")]
        public ConvoyZoneConfig ZoneConfig { get; set; }

        [JsonProperty("Notification Settings")]
        public ConvoyNotifyConfig NotifyConfig { get; set; }

        [JsonProperty("GUI")]
        public ConvoyGUIConfig GUIConfig { get; set; }

        [JsonProperty("Supported Plugins")]
        public ConvoySupportedPluginsConfig SupportedPluginsConfig { get; set; }

        [JsonProperty("Enable debug logging [true/false]")]
        public bool Debug { get; set; }

        [JsonProperty("Default event position (x,y,z)")]
        public float[] DefaultEventPosition { get; set; } = new float[] { 0f, 100f, 0f };

        [JsonProperty("Event duration when auto-started [sec]")]
        public int EventDurationAutoSec { get; set; } = 3600;

        /// <summary>Returns a full default config so the JSON file can be populated with Route Settings, Convoy Presets, etc.</summary>
        public static ConvoyPluginConfig GetDefault()
        {
            return new ConvoyPluginConfig
            {
                Version = "2.9.1",
                Prefix = "[Convoy]",
                Debug = false,
                DefaultEventPosition = new float[] { 0f, 100f, 0f },
                EventDurationAutoSec = 3600,
                MainConfig = new ConvoyMainConfig
                {
                    IsAutoEvent = false,
                    MinTimeBetweenEvents = 3600,
                    MaxTimeBetweenEvents = 3600,
                    PreStartTime = 0,
                    EnableStartStopLogs = false,
                    DontStopEventIfPlayerInZone = false,
                    IsTurretDropWeapon = false,
                    KillEventAfterLoot = true,
                    EndAfterLootTime = 300
                },
                BehaviorConfig = new ConvoyBehaviorConfig
                {
                    AggressiveTime = 80,
                    IsStopConvoyAggressive = true,
                    StopTime = 80,
                    IsPlayerTurretEnable = true
                },
                LootConfig = new ConvoyLootConfig
                {
                    DropLoot = true,
                    LootLossPercent = 0.5f,
                    BlockLootingByMove = false,
                    BlockLootingByNpcs = false,
                    BlockLootingByBradleys = false,
                    BlockLootingByHeli = false
                },
                PathConfig = new ConvoyPathConfig
                {
                    PathType = 1,
                    MinRoadLength = 200,
                    BlockRoads = new HashSet<int>(),
                    RegularPathConfig = new ConvoyRegularPathConfig { IsRingRoad = true },
                    ComplexPathConfig = new ConvoyComplexPathConfig { ChooseLongestRoute = true, MinRoadCount = 3 },
                    CustomPathConfig = new ConvoyCustomPathConfig { CustomRoutesPresets = new List<string>() }
                },
                EventConfigs = new HashSet<ConvoyEventConfig>
                {
                    new ConvoyEventConfig
                    {
                        PresetName = "easy",
                        DisplayName = "Easy Convoy",
                        IsAutoStart = true,
                        Chance = 40f,
                        MinTimeAfterWipe = 0,
                        MaxTimeAfterWipe = 259200,
                        EventTime = 3600,
                        ZoneRadius = 50f,
                        MaxGroundDamageDistance = 50,
                        MaxHeliDamageDistance = 300,
                        VehiclesOrder = new List<string> { "motorbike_easy", "motorbike_sidecar_easy", "sedan_easy", "motorbike_sidecar_easy", "motorbike_easy" },
                        IsHeli = false,
                        HeliPreset = ""
                    }
                },
                TravelingVendorConfigs = new HashSet<ConvoyTravellingVendorConfig>(),
                ModularCarConfigs = new HashSet<ConvoyModularCarConfig>(),
                BradleyConfigs = new HashSet<ConvoyBradleyConfig>(),
                SedanConfigs = new HashSet<ConvoySedanConfig>(),
                BikeConfigs = new HashSet<ConvoyBikeConfig>(),
                KaruzaCarConfigs = new HashSet<ConvoyKaruzaCarConfig>(),
                HeliConfigs = new HashSet<ConvoyHeliConfig>(),
                TurretConfigs = new HashSet<ConvoyTurretConfig>(),
                SamsiteConfigs = new HashSet<ConvoySamSiteConfig>(),
                CrateConfigs = new HashSet<ConvoyCrateConfig>(),
                NpcConfigs = new HashSet<ConvoyNpcConfig>(),
                MarkerConfig = new ConvoyMarkerConfigFull
                {
                    Enable = true,
                    UseShopMarker = true,
                    UseRingMarker = true,
                    Radius = 0.2f,
                    Alpha = 0.6f,
                    Color1 = new ColorConfig { R = 0.81f, G = 0.25f, B = 0.15f },
                    Color2 = new ColorConfig { R = 0f, G = 0f, B = 0f }
                },
                ZoneConfig = new ConvoyZoneConfig(),
                NotifyConfig = new ConvoyNotifyConfig
                {
                    IsChatEnable = false,
                    TimeNotifications = new HashSet<int> { 300, 60, 30, 5 },
                    GameTipConfig = new ConvoyGameTipConfig { IsEnabled = true, Style = 0 }
                },
                GUIConfig = new ConvoyGUIConfig(),
                SupportedPluginsConfig = new ConvoySupportedPluginsConfig()
            };
        }
    }

    public class ConvoyMainConfig
    {
        [JsonProperty("Enable automatic event holding [true/false]")]
        public bool IsAutoEvent { get; set; }

        [JsonProperty("Minimum time between events [sec]")]
        public int MinTimeBetweenEvents { get; set; }

        [JsonProperty("Maximum time between events [sec]")]
        public int MaxTimeBetweenEvents { get; set; }

        [JsonProperty("The time between receiving a chat notification and the start of the event [sec.]")]
        public int PreStartTime { get; set; }

        [JsonProperty("Enable logging of the start and end of the event? [true/false]")]
        public bool EnableStartStopLogs { get; set; }

        [JsonProperty("The event will not end if there are players nearby [true/false]")]
        public bool DontStopEventIfPlayerInZone { get; set; }

        [JsonProperty("The turrets of the сonvoy will drop loot after destruction? [true/false]")]
        public bool IsTurretDropWeapon { get; set; }

        [JsonProperty("Destroy the сonvoy after opening all the crates [true/false]")]
        public bool KillEventAfterLoot { get; set; }

        [JsonProperty("Time to destroy the сonvoy after opening all the crates [sec]")]
        public int EndAfterLootTime { get; set; }
    }

    public class ConvoyBehaviorConfig
    {
        [JsonProperty("The time for which the convoy becomes aggressive after it has been attacked (-1 - is always aggressive)")]
        public int AggressiveTime { get; set; }

        [JsonProperty("The convoy will always remain aggressive while stopped [true/false]")]
        public bool IsStopConvoyAggressive { get; set; }

        [JsonProperty("The duration of the stop after the attack")]
        public int StopTime { get; set; }

        [JsonProperty("Player turrets will attack NPCs if the convoy is stopped (false - They won't attack at all) [true/false]")]
        public bool IsPlayerTurretEnable { get; set; }
    }

    public class ConvoyLootConfig
    {
        [JsonProperty("When the car is destroyed, loot falls to the ground [true/false]")]
        public bool DropLoot { get; set; }

        [JsonProperty("Percentage of loot loss when destroying a сar [0.0-1.0]")]
        public float LootLossPercent { get; set; }

        [JsonProperty("Prohibit looting crates if the convoy is moving [true/false]")]
        public bool BlockLootingByMove { get; set; }

        [JsonProperty("Prohibit looting crates if NPCs are alive [true/false]")]
        public bool BlockLootingByNpcs { get; set; }

        [JsonProperty("Prohibit looting crates if Bradleys are alive [true/false]")]
        public bool BlockLootingByBradleys { get; set; }

        [JsonProperty("Prohibit looting crates if Heli is alive [true/false]")]
        public bool BlockLootingByHeli { get; set; }
    }

    public class ConvoyPathConfig
    {
        [JsonProperty("Type of routes (0 - standard (fast generation), 1 - experimental (multiple roads are used), 2 - custom)")]
        public int PathType { get; set; }

        [JsonProperty("Minimum road length")]
        public int MinRoadLength { get; set; }

        [JsonProperty("List of excluded roads (/convoyroadblock)")]
        public HashSet<int> BlockRoads { get; set; }

        [JsonProperty("Setting up the standard route type")]
        public ConvoyRegularPathConfig RegularPathConfig { get; set; }

        [JsonProperty("Setting up a experimental type")]
        public ConvoyComplexPathConfig ComplexPathConfig { get; set; }

        [JsonProperty("Setting up a custom route type")]
        public ConvoyCustomPathConfig CustomPathConfig { get; set; }
    }

    public class ConvoyRegularPathConfig
    {
        [JsonProperty("If there is a ring road on the map, then the convoy will always spawn here")]
        public bool IsRingRoad { get; set; }
    }

    public class ConvoyComplexPathConfig
    {
        [JsonProperty("Always choose the longest route? [true/false]")]
        public bool ChooseLongestRoute { get; set; }

        [JsonProperty("The minimum number of roads in a complex route")]
        public int MinRoadCount { get; set; }
    }

    public class ConvoyCustomPathConfig
    {
        [JsonProperty("List of presets for custom routes")]
        public List<string> CustomRoutesPresets { get; set; }
    }

    public class ConvoyEventConfig
    {
        [JsonProperty("Name")]
        public string PresetName { get; set; }

        [JsonProperty("Name displayed on the map (For custom marker)")]
        public string DisplayName { get; set; }

        [JsonProperty("Automatic startup")]
        public bool IsAutoStart { get; set; }

        [JsonProperty("Probability of a preset [0.0-100.0]")]
        public float Chance { get; set; }

        [JsonProperty("The minimum time after the server's wipe when this preset can be selected automatically [sec]")]
        public int MinTimeAfterWipe { get; set; }

        [JsonProperty("The maximum time after the server's wipe when this preset can be selected automatically [sec] (-1 - do not use this parameter)")]
        public int MaxTimeAfterWipe { get; set; }

        [JsonProperty("Event time")]
        public int EventTime { get; set; }

        [JsonProperty("Radius of the event zone")]
        public float ZoneRadius { get; set; }

        [JsonProperty("Maximum range for damage to Bradleys/NPCs/turrets (-1 - do not limit)")]
        public int MaxGroundDamageDistance { get; set; }

        [JsonProperty("Maximum range for damage to Heli when the convoy is stopped (-1 - do not limit)")]
        public int MaxHeliDamageDistance { get; set; }

        [JsonProperty("Order of vehicles")]
        public List<string> VehiclesOrder { get; set; }

        [JsonProperty("Enable the helicopter")]
        public bool IsHeli { get; set; }

        [JsonProperty("Heli preset")]
        public string HeliPreset { get; set; }

        [JsonProperty("NPC damage multipliers depending on the attacker's weapon")]
        public Dictionary<string, float> WeaponToScaleDamageNpc { get; set; }
    }

    public class ConvoyVehicleConfig
    {
        [JsonProperty("NPC preset", Order = 100)]
        public string NpcPresetName { get; set; }

        [JsonProperty("Number of NPCs", Order = 100)]
        public int NumberOfNpc { get; set; }

        [JsonProperty("Locations of additional NPCs", Order = 101)]
        public HashSet<ConvoyNpcPoseConfig> AdditionalNpc { get; set; }

        [JsonProperty("Crates", Order = 102)]
        public HashSet<ConvoyPresetLocationConfig> CrateLocations { get; set; }

        [JsonProperty("Turrets", Order = 103)]
        public HashSet<ConvoyPresetLocationConfig> TurretLocations { get; set; }

        [JsonProperty("SamSites", Order = 104)]
        public HashSet<ConvoyPresetLocationConfig> SamSiteLocations { get; set; }
    }

    public class ConvoyNpcPoseConfig : ConvoyLocationConfig
    {
        [JsonProperty("Enable spawn?")]
        public bool IsEnable { get; set; }

        [JsonProperty("Seat prefab")]
        public string SeatPrefab { get; set; }

        [JsonProperty("Will the NPC dismount when the vehicle stops?")]
        public bool IsDismount { get; set; }

        [JsonProperty("NPC preset (Empty - as in a vehicle)")]
        public string NpcPresetName { get; set; }
    }

    public class ConvoyPresetLocationConfig : ConvoyLocationConfig
    {
        [JsonProperty("Preset name")]
        public string PresetName { get; set; }
    }

    public class ConvoyLocationConfig
    {
        [JsonProperty("Position")]
        public string Position { get; set; }

        [JsonProperty("Rotation")]
        public string Rotation { get; set; }
    }

    public class ConvoyTravellingVendorConfig : ConvoyVehicleConfig
    {
        [JsonProperty("Preset Name")]
        public string PresetName { get; set; }

        [JsonProperty("Delete the vendor's map marker?")]
        public bool DeleteMapMarker { get; set; }

        [JsonProperty("Add a lock on the Loot Door? [true/false]")]
        public bool IsLocked { get; set; }

        [JsonProperty("Loot Door Health")]
        public float DoorHealth { get; set; }

        [JsonProperty("Loot door SkinID")]
        public ulong DoorSkin { get; set; }
    }

    public class ConvoyModularCarConfig : ConvoyVehicleConfig
    {
        [JsonProperty("Name")]
        public string PresetName { get; set; }

        [JsonProperty("Scale damage")]
        public float DamageScale { get; set; }

        [JsonProperty("Modules")]
        public List<string> Modules { get; set; }
    }

    public class ConvoyBradleyConfig : ConvoyVehicleConfig
    {
        [JsonProperty("Name")]
        public string PresetName { get; set; }

        [JsonProperty("HP")]
        public float Hp { get; set; }

        [JsonProperty("Damage multiplier from Bradley to buildings (-1 - do not change)")]
        public float BradleyBuildingDamageScale { get; set; }

        [JsonProperty("The viewing distance")]
        public float ViewDistance { get; set; }

        [JsonProperty("Radius of search")]
        public float SearchDistance { get; set; }

        [JsonProperty("The multiplier of Machine-gun aim cone")]
        public float CoaxAimCone { get; set; }

        [JsonProperty("The multiplier of Machine-gun fire rate")]
        public float CoaxFireRate { get; set; }

        [JsonProperty("Amount of Machine-gun burst shots")]
        public int CoaxBurstLength { get; set; }

        [JsonProperty("The time between shots of the main gun [sec.]")]
        public float NextFireTime { get; set; }

        [JsonProperty("The time between shots of the main gun in a fire rate [sec.]")]
        public float TopTurretFireRate { get; set; }

        [JsonProperty("Numbers of crates")]
        public int CountCrates { get; set; }

        [JsonProperty("Open the crates immediately after spawn")]
        public bool InstCrateOpen { get; set; }

        [JsonProperty("LootManager Preset")]
        public string LootManagerPreset { get; set; }

        [JsonProperty(PropertyName = "Own loot table", NullValueHandling = NullValueHandling.Ignore)]
        public ConvoyBaseLootTableConfig BaseLootTableConfig { get; set; }
    }

    public class ConvoySedanConfig : ConvoyVehicleConfig
    {
        [JsonProperty("Name")]
        public string PresetName { get; set; }

        [JsonProperty("HP")]
        public float Hp { get; set; }
    }

    public class ConvoyKaruzaCarConfig : ConvoyVehicleConfig
    {
        [JsonProperty("Preset Name")]
        public string PresetName { get; set; }

        [JsonProperty("Prefab Name")]
        public string PrefabName { get; set; }
    }

    public class ConvoyBikeConfig : ConvoyVehicleConfig
    {
        [JsonProperty("Preset Name")]
        public string PresetName { get; set; }

        [JsonProperty("Prefab Name")]
        public string PrefabName { get; set; }

        [JsonProperty("HP")]
        public float Hp { get; set; }
    }

    public class ConvoyTurretConfig
    {
        [JsonProperty("Preset Name")]
        public string PresetName { get; set; }

        [JsonProperty("Health")]
        public float Hp { get; set; }

        [JsonProperty("Weapon ShortName")]
        public string ShortNameWeapon { get; set; }

        [JsonProperty("Ammo ShortName")]
        public string ShortNameAmmo { get; set; }

        [JsonProperty("Number of ammo")]
        public int CountAmmo { get; set; }

        [JsonProperty("Target detection range (0 - do not change)")]
        public float TargetDetectionRange { get; set; }

        [JsonProperty("Target loss range (0 - do not change)")]
        public float TargetLossRange { get; set; }
    }

    public class ConvoySamSiteConfig
    {
        [JsonProperty("Preset Name")]
        public string PresetName { get; set; }

        [JsonProperty("Health")]
        public float Hp { get; set; }

        [JsonProperty("Number of ammo")]
        public int CountAmmo { get; set; }
    }

    public class ConvoyCrateConfig
    {
        [JsonProperty("Preset Name")]
        public string PresetName { get; set; }

        [JsonProperty("Prefab")]
        public string PrefabName { get; set; }

        [JsonProperty("SkinID (0 - default)")]
        public ulong Skin { get; set; }

        [JsonProperty("Time to unlock the crates (LockedCrate) [sec.]")]
        public float HackTime { get; set; }

        [JsonProperty("LootManager Preset")]
        public string LootManagerPreset { get; set; }

        [JsonProperty(PropertyName = "Own loot table", NullValueHandling = NullValueHandling.Ignore)]
        public ConvoyLootTableConfig LootTableConfig { get; set; }
    }

    public class ConvoyHeliConfig
    {
        [JsonProperty("Name")]
        public string PresetName { get; set; }

        [JsonProperty("HP")]
        public float Hp { get; set; }

        [JsonProperty("HP of the main rotor")]
        public float MainRotorHealth { get; set; }

        [JsonProperty("HP of tail rotor")]
        public float RearRotorHealth { get; set; }

        [JsonProperty("Numbers of crates")]
        public int CratesAmount { get; set; }

        [JsonProperty("Flying height")]
        public float Height { get; set; }

        [JsonProperty("Bullet speed")]
        public float BulletSpeed { get; set; }

        [JsonProperty("Bullet Damage")]
        public float BulletDamage { get; set; }

        [JsonProperty("The distance to which the helicopter can move away from the convoy")]
        public float Distance { get; set; }

        [JsonProperty("The time for which the helicopter can leave the convoy to attack the target [sec.]")]
        public float OutsideTime { get; set; }

        [JsonProperty("The helicopter will not aim for the nearest monument at death [true/false]")]
        public bool ImmediatelyKill { get; set; }

        [JsonProperty("Open the crates immediately after spawn")]
        public bool InstCrateOpen { get; set; }

        [JsonProperty("LootManager Preset")]
        public string LootManagerPreset { get; set; }

        [JsonProperty(PropertyName = "Own loot table", NullValueHandling = NullValueHandling.Ignore)]
        public ConvoyBaseLootTableConfig BaseLootTableConfig { get; set; }
    }

    public class ConvoyNpcConfig
    {
        [JsonProperty("Preset Name")]
        public string PresetName { get; set; }

        [JsonProperty("Name")]
        public string DisplayName { get; set; }

        [JsonProperty("Health")]
        public float Health { get; set; }

        [JsonProperty("Kit")]
        public string Kit { get; set; }

        [JsonProperty("Wear items")]
        public List<ConvoyNpcWear> WearItems { get; set; }

        [JsonProperty("Belt items")]
        public List<ConvoyNpcBelt> BeltItems { get; set; }

        [JsonProperty("Speed")]
        public float Speed { get; set; }

        [JsonProperty("Roam Range")]
        public float RoamRange { get; set; }

        [JsonProperty("Chase Range")]
        public float ChaseRange { get; set; }

        [JsonProperty("Attack Range Multiplier")]
        public float AttackRangeMultiplier { get; set; }

        [JsonProperty("Sense Range")]
        public float SenseRange { get; set; }

        [JsonProperty("Memory duration [sec.]")]
        public float MemoryDuration { get; set; }

        [JsonProperty("Scale damage")]
        public float DamageScale { get; set; }

        [JsonProperty("Aim Cone Scale")]
        public float AimConeScale { get; set; }

        [JsonProperty("Detect the target only in the NPC's viewing vision cone?")]
        public bool CheckVisionCone { get; set; }

        [JsonProperty("Vision Cone")]
        public float VisionCone { get; set; }

        [JsonProperty("Turret damage scale")]
        public float TurretDamageScale { get; set; }

        [JsonProperty("Disable radio effects? [true/false]")]
        public bool DisableRadio { get; set; }

        [JsonProperty("Should remove the corpse?")]
        public bool DeleteCorpse { get; set; }

        [JsonProperty("LootManager Preset")]
        public string LootManagerPreset { get; set; }

        [JsonProperty(PropertyName = "Own loot table", NullValueHandling = NullValueHandling.Ignore)]
        public ConvoyLootTableConfig LootTableConfig { get; set; }
    }

    public class ConvoyNpcWear
    {
        [JsonProperty("ShortName")]
        public string ShortName { get; set; }

        [JsonProperty("skinID (0 - default)")]
        public ulong SkinID { get; set; }
    }

    public class ConvoyNpcBelt
    {
        [JsonProperty("ShortName")]
        public string ShortName { get; set; }

        [JsonProperty("Amount")]
        public int Amount { get; set; }

        [JsonProperty("skinID (0 - default)")]
        public ulong SkinID { get; set; }

        [JsonProperty("Mods")]
        public List<string> Mods { get; set; }

        [JsonProperty("Ammo")]
        public string Ammo { get; set; }
    }

    public class ConvoyBaseLootTableConfig
    {
        [JsonProperty("Clear the standard content of the crate")]
        public bool ClearDefaultItemList { get; set; }

        [JsonProperty("Setting up loot from the loot table")]
        public object PrefabConfigs { get; set; }

        [JsonProperty("Enable spawn of items from the list")]
        public bool IsRandomItemsEnable { get; set; }

        [JsonProperty("Minimum numbers of items")]
        public int MinItemsAmount { get; set; }

        [JsonProperty("Maximum numbers of items")]
        public int MaxItemsAmount { get; set; }

        [JsonProperty("List of items")]
        public List<ConvoyLootItemConfig> Items { get; set; }
    }

    public class ConvoyLootTableConfig : ConvoyBaseLootTableConfig
    {
        [JsonProperty("Allow the AlphaLoot plugin to spawn items in this crate")]
        public bool IsAlphaLoot { get; set; }

        [JsonProperty("The name of the loot preset for AlphaLoot")]
        public string AlphaLootPresetName { get; set; }

        [JsonProperty("Allow the CustomLoot plugin to spawn items in this crate")]
        public bool IsCustomLoot { get; set; }

        [JsonProperty("Allow the Loot Table Stacksize GUI plugin to spawn items in this crate")]
        public bool IsLootTablePlugin { get; set; }
    }

    public class ConvoyLootItemConfig
    {
        [JsonProperty("ShortName")]
        public string Shortname { get; set; }

        [JsonProperty("Minimum")]
        public int MinAmount { get; set; }

        [JsonProperty("Maximum")]
        public int MaxAmount { get; set; }

        [JsonProperty("Chance [0.0-100.0]")]
        public float Chance { get; set; }

        [JsonProperty("Is this a blueprint? [true/false]")]
        public bool IsBlueprint { get; set; }

        [JsonProperty("SkinID (0 - default)")]
        public ulong Skin { get; set; }

        [JsonProperty("Name (empty - default)")]
        public string Name { get; set; }

        [JsonProperty("List of genomes")]
        public List<string> Genomes { get; set; }
    }

    public class ConvoyMarkerConfigFull
    {
        [JsonProperty("Do you use the Marker? [true/false]")]
        public bool Enable { get; set; }

        [JsonProperty("Use a shop marker? [true/false]")]
        public bool UseShopMarker { get; set; }

        [JsonProperty("Use a circular marker? [true/false]")]
        public bool UseRingMarker { get; set; }

        [JsonProperty("Radius")]
        public float Radius { get; set; } = 0.2f;

        [JsonProperty("Alpha")]
        public float Alpha { get; set; } = 0.6f;

        [JsonProperty("Marker color")]
        public ColorConfig Color1 { get; set; }

        [JsonProperty("Outline color")]
        public ColorConfig Color2 { get; set; }
    }

    public class ConvoyZoneConfig
    {
        [JsonProperty("Create a PVP zone in the convoy stop zone? (only for those who use the TruePVE plugin)[true/false]")]
        public bool IsPvpZone { get; set; }

        [JsonProperty("Use the dome? [true/false]")]
        public bool IsDome { get; set; }

        [JsonProperty("Darkening the dome")]
        public int Darkening { get; set; }

        [JsonProperty("Use a colored border? [true/false]")]
        public bool IsColoredBorder { get; set; }

        [JsonProperty("Border color (0 - blue, 1 - green, 2 - purple, 3 - red)")]
        public int BorderColor { get; set; }

        [JsonProperty("Brightness of the color border")]
        public int Brightness { get; set; }
    }

    public class ConvoyNotifyConfig
    {
        [JsonProperty("Use a chat? [true/false]")]
        public bool IsChatEnable { get; set; }

        [JsonProperty("The time until the end of the event, when a message is displayed about the time until the end of the event [sec]")]
        public HashSet<int> TimeNotifications { get; set; }

        [JsonProperty("Facepunch Game Tips setting")]
        public ConvoyGameTipConfig GameTipConfig { get; set; }
    }

    public class ConvoyGameTipConfig
    {
        [JsonProperty("Use Facepunch Game Tips (notification bar above hotbar)? [true/false]")]
        public bool IsEnabled { get; set; } = true;

        [JsonProperty("Style (0 - Blue Normal, 1 - Red Normal, 2 - Blue Long, 3 - Blue Short, 4 - Server Event)")]
        public int Style { get; set; }
    }

    public class ConvoyGUIConfig
    {
        [JsonProperty("Use the Countdown GUI? [true/false]")]
        public bool IsEnable { get; set; }

        [JsonProperty("Vertical offset")]
        public int OffsetMinY { get; set; }
    }

    public class ConvoySupportedPluginsConfig
    {
        [JsonProperty("PVE Mode Setting")]
        public object PveMode { get; set; }

        [JsonProperty("Economy Setting")]
        public object EconomicsConfig { get; set; }

        [JsonProperty("BetterNpc Setting")]
        public object BetterNpcConfig { get; set; }

        [JsonProperty("GUI Announcements setting")]
        public object GUIAnnouncementsConfig { get; set; }

        [JsonProperty("Notify setting")]
        public object NotifyPluginConfig { get; set; }

        [JsonProperty("DiscordMessages setting")]
        public object DiscordMessagesConfig { get; set; }
    }
}
