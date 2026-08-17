/*
--------------------------------------------------------------------------------
MIT License (Original Work by Arainrr)
--------------------------------------------------------------------------------

Copyright (c) 2020 Arainrr

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Original Software"),
to deal in the Original Software without restriction, including without 
limitation the rights to use, copy, modify, merge, publish, distribute, 
sublicense, and/or sell copies of the Original Software, and to permit persons 
to whom the Original Software is furnished to do so, subject to the following 
conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Original Software.

THE ORIGINAL SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR 
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE ORIGINAL SOFTWARE OR 
THE USE OR OTHER DEALINGS IN THE ORIGINAL SOFTWARE.

--------------------------------------------------------------------------------
Proprietary Additions & Enhancements by Grimm530
--------------------------------------------------------------------------------

The modifications, enhancements, additions, rewritten systems, features, and all
new code introduced by Grimm530 (“Modified Software”) are proprietary works and
are NOT licensed under the MIT License.

By installing, loading, or using this Modified Software, you agree to the 
following terms:

1. You may not merge, publish, redistribute, sublicense, share, leak, or sell 
   the Modified Software or any derivative works without the explicit written 
   consent of the Developer.

2. You may copy or modify the Modified Software **only for personal, private use
   on servers you own or operate**. Distribution of modified or unmodified 
   versions to any third party is strictly prohibited.

3. The Modified Software is provided "AS IS" without warranty of any kind. The 
   Developer assumes no liability for damages of any kind arising from its use.

Developer: Grimm530 (r3ap3rsg@gmail.com)
Copyright © 2025 Grimm530. All rights reserved.

--------------------------------------------------------------------------------
Patch notes:
2/22/2026 - Fixed issue with vehicles not returning loot from containers when destroyed.
5/11/2026 - Karuza Custom Vehicles: discover vehicles from KaruzaEntitiesCommon.json API when RustCar/RustHelicopter/RustPlane JSON folders are empty or incomplete.
8/15/2026 - Use Clans now includes Facepunch native clans (clanId / ClanManager) without dropping Oxide Clans / Rust:IO Clans. Clear vanilla owner-lock so clan mates can mount.

*/
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Facepunch;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Rust;
using Rust.Modular;
using UnityEngine;
using static RustVehiclesHarmony.RustVehicles;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

namespace RustVehiclesHarmony
{
    /// <summary>
    /// RustVehicles 2.0.5 ported for Harmony (no Oxide). Logic matches the Oxide plugin; hosting differs.
    /// </summary>
    [Info("Rust Vehicles", "Grimm530", "2.0.5")]
    [Description("Allows players to buy vehicles and then spawn or store it")]
    public class RustVehicles : RustVehiclesPluginBase
    {
        #region Types
        internal Plugin 
        Economics, 
        ServerRewards, 
        Friends, 
        Clans, 
        NoEscape, 
        LandOnCargoShip, 
        RustTranslationAPI, 
        ZoneManager, 
        CustomEntities,
        RustCar,
        RustPlane,
        RustHelicopter,
        KaruzaVehicleChatCommand;

        public RustVehicles()
        {
            Version = new VersionNumber(2, 0, 5);
        }

        private readonly string PERMISSION_USE = "RustVehicles.use";
        private readonly string PERMISSION_ALL = "RustVehicles.all";
        private readonly string PERMISSION_ADMIN = "RustVehicles.admin";
        private readonly string PERMISSION_BYPASS_COST = "RustVehicles.bypasscost";
        private readonly string PERMISSION_NO_DAMAGE = "RustVehicles.nodamage";
        private readonly string PERMISSION_NO_COLLISION_DAMAGE = "RustVehicles.nocollisiondamage";
        private readonly string PERMISSION_PICKUP = "RustVehicles.pickup";
        private const int ITEMID_FUEL = -946369541;
        private const int ITEMID_HOTAIRBALLOON_ARMOR = -1989600732;
        private const string PREFAB_ITEM_DROP = "assets/prefabs/misc/item drop/item_drop.prefab";

        private readonly Dictionary<string, CustomVehicleInfo> discoveredCustomVehicles = new Dictionary<string, CustomVehicleInfo>();
        
        public class CustomVehicleInfo
        {
            public string PrefabPath { get; set; }
            public ulong SkinID { get; set; }
        }

        public class CustomVehicleSettings : Dictionary<string, BaseVehicleSettings>
        {

        }

        public static RustVehicles Instance { get; private set; }
        public readonly Dictionary<BaseEntity, OwnedVehicle> vehiclesCache = new Dictionary<BaseEntity, OwnedVehicle>();
        public readonly Dictionary<string, BaseVehicleSettings> allVehicleSettings = new Dictionary<string, BaseVehicleSettings>();
        public readonly Dictionary<string, string> commandToVehicleType = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, CustomVehicleConfig> _catalogByName;
        private static readonly string KaruzaCatalogPath = Path.Combine(Interface.Oxide.ConfigDirectory, "KaruzaCatalog", "KaruzaVehicleItemManager.cs");
        private static readonly string KaruzaEntitiesCommonConfigPath = Path.Combine(Interface.Oxide.ConfigDirectory, "KaruzaEntitiesCommon.json");
        private const int KaruzaApiEntityTypePlane = 1;
        private const int KaruzaApiEntityTypeHelicopter = 2;
        private const int KaruzaApiEntityTypeCar = 3;
        private static readonly Regex KaruzaApiCustomPrefabSearch = new Regex(@"assets/custom/[^""]+\.prefab", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private readonly object _false = false;
        private bool finishedLoading = false;
        private const int LAYER_GROUND = Layers.Solid | Layers.Mask.Water | Layers.Construction;

        private readonly float HELICOPTER_LIFT = 0.25f;
        private readonly float TUGBOAT_ENGINETHRUST = 200000f;
        private readonly Vector3 MINICOPTER_TORQUE = new Vector3(400.0f, 400.0f, 200.0f);
        private readonly Vector3 ATTACK_HELICOPTER_TORQUE = new Vector3(8000.0f, 8000.0f, 5200.0f);
        private readonly Vector3 SCRAP_HELICOPTER_TORQUE = new Vector3(8000.0f, 8000.0f, 4000.0f);
        //Air
        private const string PREFAB_MINICOPTER = "assets/content/vehicles/minicopter/minicopter.entity.prefab";
        private const string PREFAB_ATTACKHELICOPTER = "assets/content/vehicles/attackhelicopter/attackhelicopter.entity.prefab";
        private const string PREFAB_TRANSPORTCOPTER = "assets/content/vehicles/scrap heli carrier/scraptransporthelicopter.prefab";
        private const string PREFAB_CHINOOK = "assets/prefabs/npc/ch47/ch47.entity.prefab";
        private const string PREFAB_HOTAIRBALLOON = "assets/prefabs/deployable/hot air balloon/hotairballoon.prefab";
        //Boat
        private const string PREFAB_TUGBOAT = "assets/content/vehicles/boats/tugboat/tugboat.prefab";
        private const string PREFAB_ROWBOAT = "assets/content/vehicles/boats/rowboat/rowboat.prefab";
        private const string PREFAB_RHIB = "assets/content/vehicles/boats/rhib/rhib.prefab";
        private const string PREFAB_KAYAK = "assets/content/vehicles/boats/kayak/kayak.prefab";
        private const string PREFAB_SUBMARINE_SOLO = "assets/content/vehicles/submarine/submarinesolo.entity.prefab";
        private const string PREFAB_SUBMARINE_DUO = "assets/content/vehicles/submarine/submarineduo.entity.prefab";
        private const string PREFAB_DPV = "assets/content/vehicles/dpv/dpv.deployed.prefab";
        //Car
        private const string PREFAB_SEDAN = "assets/content/vehicles/sedan_a/sedantest.entity.prefab";
        private const string PREFAB_SEDANRAIL = "assets/content/vehicles/sedan_a/sedanrail.entity.prefab";
        private const string PREFAB_CHASSIS_SMALL = "assets/content/vehicles/modularcar/car_chassis_2module.entity.prefab";
        private const string PREFAB_CHASSIS_MEDIUM = "assets/content/vehicles/modularcar/car_chassis_3module.entity.prefab";
        private const string PREFAB_CHASSIS_LARGE = "assets/content/vehicles/modularcar/car_chassis_4module.entity.prefab";
        //Train
        private const string PREFAB_TRAINENGINE = "assets/content/vehicles/trains/workcart/workcart_aboveground.entity.prefab";
        private const string PREFAB_TRAINENGINE_COVERED = "assets/content/vehicles/trains/workcart/workcart_aboveground2.entity.prefab";
        private const string PREFAB_TRAINENGINE_LOCOMOTIVE = "assets/content/vehicles/trains/locomotive/locomotive.entity.prefab";
        private const string PREFAB_WORKCART = "assets/content/vehicles/trains/workcart/workcart.entity.prefab";
        private const string PREFAB_TRAINWAGON_A = "assets/content/vehicles/trains/wagons/trainwagona.entity.prefab";
        private const string PREFAB_TRAINWAGON_B = "assets/content/vehicles/trains/wagons/trainwagonb.entity.prefab";
        private const string PREFAB_TRAINWAGON_C = "assets/content/vehicles/trains/wagons/trainwagonc.entity.prefab";
        private const string PREFAB_TRAINWAGON_UNLOADABLE = "assets/content/vehicles/trains/wagons/trainwagonunloadable.entity.prefab";
        private const string PREFAB_TRAINWAGON_UNLOADABLE_FUEL = "assets/content/vehicles/trains/wagons/trainwagonunloadablefuel.entity.prefab";
        private const string PREFAB_TRAINWAGON_UNLOADABLE_LOOT = "assets/content/vehicles/trains/wagons/trainwagonunloadableloot.entity.prefab";
        private const string PREFAB_CABOOSE = "assets/content/vehicles/trains/caboose/traincaboose.entity.prefab";
        //Horse
        private const string PREFAB_RIDABLEHORSE = "assets/content/vehicles/horse/ridablehorse.prefab";
        //Snowmobile
        private const string PREFAB_SNOWMOBILE = "assets/content/vehicles/snowmobiles/snowmobile.prefab";
        //Crane
        private const string PREFAB_MAGNET_CRANE = "assets/content/vehicles/crane_magnet/magnetcrane.entity.prefab";
        //Pedal Bike
        private const string PREFAB_PEDALBIKE = "assets/content/vehicles/bikes/pedalbike.prefab";
        private const string PREFAB_PEDALTRIKE = "assets/content/vehicles/bikes/pedaltrike.prefab";
        //Motorbike
        private const string PREFAB_MOTORBIKE = "assets/content/vehicles/bikes/motorbike.prefab";
        private const string PREFAB_MOTORBIKE_SIDECAR = "assets/content/vehicles/bikes/motorbike_sidecar.prefab";
        //Seige
        private const string PREFAB_SIEGETOWER = "assets/content/vehicles/siegeweapons/siegetower/siegetower.entity.prefab";
        private const string PREFAB_CATAPULT = "assets/content/vehicles/siegeweapons/catapult/catapult.entity.prefab";
        private const string PREFAB_BATTERINGRAM = "assets/content/vehicles/siegeweapons/batteringram/batteringram.entity.prefab";
        private const string PREFAB_BALLISTA = "assets/content/vehicles/siegeweapons/ballista/ballista.entity.prefab";


        #endregion Fields

        #region Configuration

        public PluginConfiguration configData { get; private set; }

        public class PluginConfiguration
        {
            [JsonProperty(PropertyName = "Settings")]
            public GlobalSettings global = new GlobalSettings();

            [JsonProperty(PropertyName = "Chat Settings")]
            public ChatSettings chat = new ChatSettings();

            [JsonProperty("Allow vehicles to be spawned/recalled in zones listed in prevent spawning zones")]
            public bool CanSpawnInZones = false;

            [JsonProperty(PropertyName = "Zones to prevent users from spawning/recalled vehicles within.", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> AntiSpawnZones = new List<string> { "KeepVehiclesOut" };

            [JsonProperty(PropertyName = "Normal Vehicle Settings")]

            public NormalVehicleSettings normalVehicles = new NormalVehicleSettings();
            [JsonProperty(PropertyName = "Modular Vehicle Settings", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, ModularVehicleSettings> modularVehicles = new Dictionary<string, ModularVehicleSettings>
            {
                ["SmallCar"] = new ModularVehicleSettings
                {
                    Purchasable = true,
                    NoDamage = false,
                    NoCollisionDamage = false,
                    DisplayName = "Small Modular Car",
                    Distance = 5,
                    MinDistanceForPlayers = 3,
                    UsePermission = true,
                    Permission = "RustVehicles.smallmodularcar",
                    BypassCostPermission = "RustVehicles.smallmodularcarfree",
                    Commands = new List<string> { "small", "smallcar" },
                    PurchasePrices = new Dictionary<string, PriceInfo>
                    {
                        ["scrap"] = new PriceInfo { amount = 1600, displayName = "Scrap" }
                    },
                    SpawnPrices = new Dictionary<string, PriceInfo>
                    {
                        ["metal.refined"] = new PriceInfo { amount = 10, displayName = "High Quality Metal" }
                    },
                    RecallPrices = new Dictionary<string, PriceInfo>
                    {
                        ["scrap"] = new PriceInfo { amount = 5, displayName = "Scrap" }
                    },
                    SpawnCooldown = 7200,
                    RecallCooldown = 30,
                    CooldownPermissions = new Dictionary<string, CooldownPermission>
                    {
                        ["RustVehicles.vip"] = new CooldownPermission
                        {
                            spawnCooldown = 3600,
                            recallCooldown = 10
                        }
                    },
                    ChassisType = "Small",
                    ModuleItems = new List<ModuleItem>
                    {
                        new ModuleItem
                        {
                            shortName = "vehicle.1mod.cockpit.with.engine", healthPercentage = 50f
                        },
                        new ModuleItem
                        {
                            shortName = "vehicle.1mod.storage", healthPercentage = 50f
                        }
                    },
                    EngineItems = new List<EngineItem>
                    {
                        new EngineItem
                        {
                            shortName = "carburetor1", conditionPercentage = 20f
                        },
                        new EngineItem
                        {
                            shortName = "crankshaft1", conditionPercentage = 20f
                        },
                        new EngineItem
                        {
                            shortName = "piston1", conditionPercentage = 20f
                        },
                        new EngineItem
                        {
                            shortName = "sparkplug1", conditionPercentage = 20f
                        },
                        new EngineItem
                        {
                            shortName = "valve1", conditionPercentage = 20f
                        }
                    }
                },
                ["MediumCar"] = new ModularVehicleSettings
                {
                    Purchasable = true,
                    NoDamage = false,
                    NoCollisionDamage = false,
                    DisplayName = "Medium Modular Car",
                    Distance = 5,
                    MinDistanceForPlayers = 3,
                    UsePermission = true,
                    Permission = "RustVehicles.mediumodularcar",
                    BypassCostPermission = "RustVehicles.mediumodularcarfree",
                    Commands = new List<string> { "medium", "mediumcar" },
                    PurchasePrices = new Dictionary<string, PriceInfo>
                    {
                        ["scrap"] = new PriceInfo { amount = 2400, displayName = "Scrap" }
                    },
                    SpawnPrices = new Dictionary<string, PriceInfo>
                    {
                        ["metal.refined"] = new PriceInfo { amount = 50, displayName = "High Quality Metal" }
                    },
                    RecallPrices = new Dictionary<string, PriceInfo>
                    {
                        ["scrap"] = new PriceInfo { amount = 8, displayName = "Scrap" }
                    },
                    SpawnCooldown = 9000,
                    RecallCooldown = 30,
                    CooldownPermissions = new Dictionary<string, CooldownPermission>
                    {
                        ["RustVehicles.vip"] = new CooldownPermission
                        {
                            spawnCooldown = 4500,
                            recallCooldown = 10
                        }
                    },
                    ChassisType = "Medium",
                    ModuleItems = new List<ModuleItem>
                    {
                        new ModuleItem
                        {
                            shortName = "vehicle.1mod.cockpit.with.engine", healthPercentage = 50f
                        },
                        new ModuleItem
                        {
                            shortName = "vehicle.1mod.rear.seats", healthPercentage = 50f
                        },
                        new ModuleItem
                        {
                            shortName = "vehicle.1mod.flatbed", healthPercentage = 50f
                        }
                    },
                    EngineItems = new List<EngineItem>
                    {
                        new EngineItem
                        {
                            shortName = "carburetor2", conditionPercentage = 20f
                        },
                        new EngineItem
                        {
                            shortName = "crankshaft2", conditionPercentage = 20f
                        },
                        new EngineItem
                        {
                            shortName = "piston2", conditionPercentage = 20f
                        },
                        new EngineItem
                        {
                            shortName = "sparkplug2", conditionPercentage = 20f
                        },
                        new EngineItem
                        {
                            shortName = "valve2", conditionPercentage = 20f
                        }
                    }
                },
                ["LargeCar"] = new ModularVehicleSettings
                {
                    Purchasable = true,
                    NoDamage = false,
                    NoCollisionDamage = false,
                    DisplayName = "Large Modular Car",
                    Distance = 6,
                    MinDistanceForPlayers = 3,
                    UsePermission = true,
                    Permission = "RustVehicles.largemodularcar",
                    BypassCostPermission = "RustVehicles.largemodularcarfree",
                    Commands = new List<string> { "large", "largecar" },
                    PurchasePrices = new Dictionary<string, PriceInfo>
                    {
                        ["scrap"] = new PriceInfo { amount = 3000, displayName = "Scrap" }
                    },
                    SpawnPrices = new Dictionary<string, PriceInfo>
                    {
                        ["metal.refined"] = new PriceInfo { amount = 100, displayName = "High Quality Metal", }
                    },
                    RecallPrices = new Dictionary<string, PriceInfo>
                    {
                        ["scrap"] = new PriceInfo { amount = 10, displayName = "Scrap" }
                    },
                    SpawnCooldown = 10800,
                    RecallCooldown = 30,
                    CooldownPermissions = new Dictionary<string, CooldownPermission>
                    {
                        ["RustVehicles.vip"] = new CooldownPermission
                        {
                            spawnCooldown = 5400,
                            recallCooldown = 10
                        }
                    },
                    ChassisType = "Large",
                    ModuleItems = new List<ModuleItem>
                    {
                        new ModuleItem
                        {
                            shortName = "vehicle.1mod.engine", healthPercentage = 50f
                        },
                        new ModuleItem
                        {
                            shortName = "vehicle.1mod.cockpit.armored", healthPercentage = 50f
                        },
                        new ModuleItem
                        {
                            shortName = "vehicle.1mod.passengers.armored", healthPercentage = 50f
                        },
                        new ModuleItem
                        {
                            shortName = "vehicle.1mod.storage", healthPercentage = 50f
                        }
                    },
                    EngineItems = new List<EngineItem>
                    {
                        new EngineItem
                        {
                            shortName = "carburetor3", conditionPercentage = 10f
                        },
                        new EngineItem
                        {
                            shortName = "crankshaft3", conditionPercentage = 10f
                        },
                        new EngineItem
                        {
                            shortName = "piston3", conditionPercentage = 10f
                        },
                        new EngineItem
                        {
                            shortName = "piston3", conditionPercentage = 10f
                        },
                        new EngineItem
                        {
                            shortName = "sparkplug3", conditionPercentage = 10f
                        },
                        new EngineItem
                        {
                            shortName = "sparkplug3", conditionPercentage = 10f
                        },
                        new EngineItem
                        {
                            shortName = "valve3", conditionPercentage = 10f
                        },
                        new EngineItem
                        {
                            shortName = "valve3", conditionPercentage = 10f
                        }
                    }
                }
            };

            [JsonProperty(PropertyName = "Train Vehicle Settings", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, TrainVehicleSettings> trainVehicles = new Dictionary<string, TrainVehicleSettings>
            {
                ["WorkCartAboveGround"] = new TrainVehicleSettings
                {
                    Purchasable = true,
                    NoDamage = false,
                    NoCollisionDamage = false,
                    DisplayName = "Work Cart Above Ground",
                    Distance = 12,
                    MinDistanceForPlayers = 6,
                    UsePermission = true,
                    Permission = "RustVehicles.workcartaboveground",
                    BypassCostPermission = "RustVehicles.workcartabovegroundfree",
                    Commands = new List<string>
                    {
                        "cartground", "workcartground"
                    },
                    PurchasePrices = new Dictionary<string, PriceInfo>
                    {
                        ["scrap"] = new PriceInfo { amount = 2000, displayName = "Scrap" }
                    },
                    SpawnCooldown = 1800,
                    RecallCooldown = 30,
                    CooldownPermissions = new Dictionary<string, CooldownPermission>
                    {
                        ["RustVehicles.vip"] = new CooldownPermission
                        {
                            spawnCooldown = 900,
                            recallCooldown = 10
                        }
                    },
                    TrainComponents = new List<TrainComponent>
                    {
                        new TrainComponent { type = "Engine" }
                    }
                },
                ["WorkCartCovered"] = new TrainVehicleSettings
                {
                    Purchasable = true,
                    NoDamage = false,
                    NoCollisionDamage = false,
                    DisplayName = "Covered Work Cart",
                    Distance = 12,
                    MinDistanceForPlayers = 6,
                    UsePermission = true,
                    Permission = "RustVehicles.coveredworkcart",
                    BypassCostPermission = "RustVehicles.coveredworkcartfree",
                    Commands = new List<string>
                    {
                        "cartcovered", "coveredworkcart"
                    },
                    PurchasePrices = new Dictionary<string, PriceInfo>
                    {
                        ["scrap"] = new PriceInfo { amount = 2000, displayName = "Scrap" }
                    },
                    SpawnCooldown = 1800,
                    RecallCooldown = 30,
                    CooldownPermissions = new Dictionary<string, CooldownPermission>
                    {
                        ["RustVehicles.vip"] = new CooldownPermission
                        {
                            spawnCooldown = 900,
                            recallCooldown = 10
                        }
                    },
                    TrainComponents = new List<TrainComponent>
                    {
                        new TrainComponent { type = "CoveredEngine" }
                    }
                },
                ["CompleteTrain"] = new TrainVehicleSettings
                {
                    Purchasable = true,
                    NoDamage = false,
                    NoCollisionDamage = false,
                    DisplayName = "Complete Train",
                    Distance = 12,
                    MinDistanceForPlayers = 6,
                    UsePermission = true,
                    Permission = "RustVehicles.completetrain",
                    BypassCostPermission = "RustVehicles.completetrainfree",
                    Commands = new List<string>
                    {
                        "ctrain", "completetrain"
                    },
                    PurchasePrices = new Dictionary<string, PriceInfo>
                    {
                        ["scrap"] = new PriceInfo { amount = 6000, displayName = "Scrap" }
                    },
                    SpawnCooldown = 3600,
                    RecallCooldown = 60,
                    CooldownPermissions = new Dictionary<string, CooldownPermission>
                    {
                        ["RustVehicles.vip"] = new CooldownPermission
                        {
                            spawnCooldown = 900,
                            recallCooldown = 10
                        }
                    },
                    TrainComponents = new List<TrainComponent>
                    {
                        new TrainComponent
                        {
                            type = "Engine"
                        },
                        new TrainComponent
                        {
                            type = "WagonA"
                        },
                        new TrainComponent
                        {
                            type = "WagonB"
                        },
                        new TrainComponent
                        {
                            type = "WagonC"
                        },
                        new TrainComponent
                        {
                            type = "Unloadable"
                        },
                        new TrainComponent
                        {
                            type = "UnloadableLoot"
                        }
                    }
                },
                ["Locomotive"] = new TrainVehicleSettings
                {
                    Purchasable = true,
                    NoDamage = false,
                    NoCollisionDamage = false,
                    DisplayName = "Locomotive",
                    Distance = 12,
                    MinDistanceForPlayers = 6,
                    UsePermission = true,
                    Permission = "RustVehicles.locomotive",
                    BypassCostPermission = "RustVehicles.locomotivefree",
                    Commands = new List<string>
                    {
                        "loco", "locomotive"
                    },
                    PurchasePrices = new Dictionary<string, PriceInfo>
                    {
                        ["scrap"] = new PriceInfo { amount = 2000, displayName = "Scrap" }
                    },
                    SpawnCooldown = 1800,
                    RecallCooldown = 30,
                    CooldownPermissions = new Dictionary<string, CooldownPermission>
                    {
                        ["RustVehicles.vip"] = new CooldownPermission
                        {
                            spawnCooldown = 900,
                            recallCooldown = 10
                        }
                    },
                    TrainComponents = new List<TrainComponent>
                    {
                        new TrainComponent { type = "Locomotive" }
                    }
                }
            };

            [System.ComponentModel.DefaultValue(null)]
            [JsonProperty(PropertyName = "Custom Vehicle Settings", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public CustomVehicleSettings customVehicles = null;

            [JsonProperty(PropertyName = "Debug Settings")]
            public DebugSettings debug = new DebugSettings();

            [JsonProperty(PropertyName = "Version")]
            public VersionNumber version;
        }

        public class DebugSettings
        {
            [JsonProperty(PropertyName = "Debug Init")]
            public bool DebugInit { get; set; } = false;

            [JsonProperty(PropertyName = "Debug Karuza Vehicles")]
            public bool DebugKaruzaVehicles { get; set; } = false;

            [JsonProperty(PropertyName = "Debug Reconnect")]
            public bool DebugReconnect { get; set; } = false;

            [JsonProperty(PropertyName = "Debug Pickup")]
            public bool DebugPickup { get; set; } = false;

            [JsonProperty(PropertyName = "Debug Recall")]
            public bool DebugRecall { get; set; } = false;
        }

        public class ChatSettings
        {
            [JsonProperty(PropertyName = "Use Universal Chat Command")]
            public bool useUniversalCommand = true;

            [JsonProperty(PropertyName = "Help Chat Command")]
            public string helpCommand = "license";

            [JsonProperty(PropertyName = "Buy Chat Command")]
            public string buyCommand = "buy";

            [JsonProperty(PropertyName = "Spawn Chat Command")]
            public string spawnCommand = "spawn";

            [JsonProperty(PropertyName = "Recall Chat Command")]
            public string recallCommand = "recall";

            [JsonProperty(PropertyName = "Kill Chat Command")]
            public string killCommand = "kill";

            [JsonProperty(PropertyName = "Custom Kill Chat Command Prefix")]
            public string customKillCommandPrefix = "no";

            [JsonProperty(PropertyName = "Bypass Cooldown Command")]
            public string bypassCooldownCommand = "pay";

            [JsonProperty(PropertyName = "Chat Prefix")]
            public string prefix = "<color=#00FFFF>[VehicleLicense]</color>: ";

            [JsonProperty(PropertyName = "Chat SteamID Icon")]
            public ulong steamIDIcon = 76561198924840872;
        }

        public class GlobalSettings
        {
            [JsonProperty(PropertyName = "Store Vehicle On Plugin Unloaded / Server Restart")]
            public bool storeVehicle = true;

            [JsonProperty(PropertyName = "Clear Vehicle Data On Map Wipe")]
            public bool clearVehicleOnWipe;

            [JsonProperty(PropertyName = "Interval to check vehicle for wipe (Seconds)")]
            public float checkVehiclesInterval = 300;

            [JsonProperty(PropertyName = "Spawn vehicle in the direction you are looking at")]
            public bool spawnLookingAt = true;

            [JsonProperty(PropertyName = "Automatically claim vehicles purchased from vehicle vendors")]
            public bool autoClaimFromVendor;

            [JsonProperty(PropertyName = "Vehicle vendor purchases will unlock the license for the player")]
            public bool autoUnlockFromVendor;

            [JsonProperty(PropertyName = "Limit the number of vehicles at a time")]
            public int limitVehicles;

            [JsonProperty(PropertyName = "Kill a random vehicle when the number of vehicles is limited")]
            public bool killVehicleLimited;

            [JsonProperty(PropertyName = "Prevent vehicles from damaging players")]
            public bool preventDamagePlayer = true;

            [JsonProperty(PropertyName = "Prevent vehicles from damaging NPCs")]
            public bool preventDamageNPCs = false;

            [JsonProperty(PropertyName = "Safe dismount players who jump off train")]
            public bool safeTrainDismount = true;

            [JsonProperty(PropertyName = "Prevent vehicles from shattering")]
            public bool preventShattering = true;

            [JsonProperty(PropertyName = "Prevent vehicles from spawning or recalling in safe zone")]
            public bool preventSafeZone = true;

            [JsonProperty(PropertyName = "Prevent vehicles from spawning or recalling when the player are building blocked")]
            public bool preventBuildingBlocked = true;

            [JsonProperty(PropertyName = "Prevent vehicles from spawning or recalling when the player is mounted or parented")]
            public bool preventMountedOrParented = true;

            [JsonProperty(PropertyName = "Check if any player mounted when recalling a vehicle")]
            public bool anyMountedRecall = true;

            [JsonProperty(PropertyName = "Check if any player mounted when killing a vehicle")]
            public bool anyMountedKill;

            [JsonProperty(PropertyName = "Dismount all players when a vehicle is recalled")]
            public bool dismountAllPlayersRecall = true;

            [JsonProperty(PropertyName = "Prevent other players from mounting vehicle")]
            public bool preventMounting = true;

            [JsonProperty(PropertyName = "Prevent mounting on driver's seat only")]
            public bool preventDriverSeat = true;

            [JsonProperty(PropertyName = "Prevent other players from looting fuel container and inventory")]
            public bool preventLooting = true;

            [JsonProperty(PropertyName = "Prevent other players from pushing vehicles they do not own")]
            public bool preventPushing = false;

            [JsonProperty(PropertyName = "Use Teams")]
            public bool useTeams;

            [JsonProperty(PropertyName = "Use Clans")]
            public bool useClans = true;

            [JsonProperty(PropertyName = "Use Friends")]
            public bool useFriends = true;

            [JsonProperty(PropertyName = "Vehicle No Decay")]
            public bool noDecay;

            [JsonProperty(PropertyName = "Vehicle No Fire Ball")]
            public bool noFireBall = true;

            [JsonProperty(PropertyName = "Vehicle No Server Gibs")]
            public bool noServerGibs = true;

            [JsonProperty(PropertyName = "Chinook No Map Marker")]
            public bool noMapMarker = true;

            [JsonProperty(PropertyName = "Use Raid Blocker (Need NoEscape Plugin)")]
            public bool useRaidBlocker;

            [JsonProperty(PropertyName = "Use Combat Blocker (Need NoEscape Plugin)")]
            public bool useCombatBlocker;

            [JsonProperty(PropertyName = "Populate the config with Custom Vehicles (CANNOT BE UNDONE! Will make config much larger)")]
            public bool useCustomVehicles;

            [JsonProperty(PropertyName = "Kill Players Owned Vehicles On Disconnect?")] public bool killOnDisconnect { get; set; } = false;
        }

        public class NormalVehicleSettings
        {
            [JsonProperty(PropertyName = "Tugboat", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public TugboatSettings tugboat = new TugboatSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                DisplayName = "Tugboat",
                speedMultiplier = 1,
                autoAuth = true,
                Distance = 10,
                MinDistanceForPlayers = 3,
                UsePermission = true,
                Permission = "RustVehicles.tug",
                BypassCostPermission = "RustVehicles.tugfree",
                Commands = new List<string> { "tugboat", "tug" },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo
                    {
                        amount = 10000,
                        displayName = "Scrap"
                    }
                },
                SpawnCooldown = 450,
                RecallCooldown = 30,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 60,
                        recallCooldown = 10
                    }
                }
            };
            [JsonProperty(PropertyName = "Sedan", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public SedanSettings sedan = new SedanSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                DisplayName = "Sedan",
                Distance = 5,
                MinDistanceForPlayers = 3,
                UsePermission = true,
                Permission = "RustVehicles.sedan",
                BypassCostPermission = "RustVehicles.sedanfree",
                Commands = new List<string> { "car", "sedan" },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo { amount = 300, displayName = "Scrap" }
                },
                SpawnCooldown = 300,
                RecallCooldown = 30,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 150,
                        recallCooldown = 10
                    }
                }
            };

            [JsonProperty(PropertyName = "Chinook", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public ChinookSettings chinook = new ChinookSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                DisplayName = "Chinook",
                Distance = 15,
                MinDistanceForPlayers = 6,
                UsePermission = true,
                Permission = "RustVehicles.chinook",
                BypassCostPermission = "RustVehicles.chinookfree",
                Commands = new List<string> { "ch47", "chinook" },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo { amount = 3000, displayName = "Scrap" }
                },
                SpawnCooldown = 3000,
                RecallCooldown = 30,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 1500,
                        recallCooldown = 10
                    }
                }
            };

            [JsonProperty(PropertyName = "Rowboat", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public RowboatSettings rowboat = new RowboatSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                DisplayName = "Row Boat",
                Distance = 5,
                MinDistanceForPlayers = 2,
                UsePermission = true,
                Permission = "RustVehicles.rowboat",
                BypassCostPermission = "RustVehicles.rowboatfree",
                Commands = new List<string> { "row", "rowboat" },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo { amount = 500, displayName = "Scrap" }
                },
                SpawnCooldown = 300,
                RecallCooldown = 30,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 150,
                        recallCooldown = 10
                    }
                }
            };

            [JsonProperty(PropertyName = "RHIB", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public RhibSettings rhib = new RhibSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                DisplayName = "Rigid Hulled Inflatable Boat",
                Distance = 10,
                MinDistanceForPlayers = 3,
                UsePermission = true,
                Permission = "RustVehicles.rhib",
                BypassCostPermission = "RustVehicles.rhibfree",
                Commands = new List<string> { "rhib" },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo { amount = 1000, displayName = "Scrap" }
                },
                SpawnCooldown = 450,
                RecallCooldown = 30,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 225,
                        recallCooldown = 10
                    }
                }
            };

            [JsonProperty(PropertyName = "Hot Air Balloon", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public HotAirBalloonSettings hotAirBalloon = new HotAirBalloonSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                DisplayName = "Hot Air Balloon",
                Distance = 20,
                MinDistanceForPlayers = 5,
                UsePermission = true,
                Permission = "RustVehicles.hotairballoon",
                BypassCostPermission = "RustVehicles.hotairballoonfree",
                Commands = new List<string> { "hab", "hotairballoon" },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo { amount = 500, displayName = "Scrap" }
                },
                SpawnCooldown = 900,
                RecallCooldown = 30,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 450,
                        recallCooldown = 10
                    }
                }
            };

            [JsonProperty(PropertyName = "Armored Hot Air Balloon", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public ArmoredHotAirBalloonSettings armoredHotAirBalloon = new ArmoredHotAirBalloonSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                DisplayName = "Armored Hot Air Balloon",
                Distance = 10,
                MinDistanceForPlayers = 5,
                UsePermission = true,
                Permission = "RustVehicles.armoredhotairballoon",
                BypassCostPermission = "RustVehicles.armoredhotairballoonfree",
                Commands = new List<string> { "ahab", "armoredhotairballoon", "armoredballoon", "aballoon" },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo { amount = 500, displayName = "Scrap" }
                },
                SpawnCooldown = 1000,
                RecallCooldown = 40,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 550,
                        recallCooldown = 20
                    }
                }
            };
            [JsonProperty(PropertyName = "Ridable Horse", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public RidableHorseSettings ridableHorse = new RidableHorseSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                IsDoubleSaddle = false,
                ArmorType = "",
                DisplayName = "Ridable Horse",
                Distance = 6,
                MinDistanceForPlayers = 1,
                UsePermission = true,
                Permission = "RustVehicles.ridablehorse",
                BypassCostPermission = "RustVehicles.ridablehorsefree",
                Commands = new List<string> { "horse", "ridablehorse" },
                Breeds = new List<string>
                {
                    "Appalosa", "Bay", "Buckskin", "Chestnut", "Dapple Grey", "Piebald", "Pinto", "Red Roan", "White Thoroughbred", "Black Thoroughbred"
                },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo { amount = 700, displayName = "Scrap" }
                },
                SpawnCooldown = 3000,
                RecallCooldown = 30,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 1500,
                        recallCooldown = 10
                    }
                }
            };

            [JsonProperty(PropertyName = "Mini Copter", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public MiniCopterSettings miniCopter = new MiniCopterSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                DisplayName = "Mini Copter",
                Distance = 8,
                MinDistanceForPlayers = 2,
                rotationScale = 1.0f,
                flyHackPause = 0,
                liftFraction = 0.25f,
                instantTakeoff = false,
                fuelPerSec = -1f,
                storageContainers = 0,
                storageLargeContainers = 0,
                largeStorageLockable = true,
                largeStorageSize = 48,
                dropStorage = true,
                autoturret = false,
                autoTurretTargetsPlayers = true,
                autoTurretTargetsNPCs = true,
                autoTurretTargetsAnimals = true,
                turretRange = 30f,
                addSearchLight = true,
                lightTail = false,
                tailLightPosX = 0f,
                tailLightPosY = 1.2f,
                tailLightPosZ = -2.0f,
                tailLightRotX = 33f,
                tailLightRotY = 180f,
                tailLightRotZ = 0f,
                UsePermission = true,
                Permission = "RustVehicles.minicopter",
                BypassCostPermission = "RustVehicles.minicopterfree",
                Commands = new List<string> { "mini", "minicopter" },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo { amount = 4000, displayName = "Scrap" }
                },
                SpawnCooldown = 1800,
                RecallCooldown = 30,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 900,
                        recallCooldown = 10
                    }
                }
            };

            [JsonProperty(PropertyName = "Attack Helicopter", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public AttackHelicopterSettings attackHelicopter = new AttackHelicopterSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                DisplayName = "Attack Helicopter",
                Distance = 8,
                MinDistanceForPlayers = 2,
                rotationScale = 1.0f,
                flyHackPause = 0,
                liftFraction = 0.33f,
                HVSpawnAmmoAmount = 0,
                IncendiarySpawnAmmoAmount = 0,
                FlareSpawnAmmoAmount = 0,
                instantTakeoff = false,
                UsePermission = true,
                Permission = "RustVehicles.attackhelicopter",
                BypassCostPermission = "RustVehicles.attackhelicopterfree",
                Commands = new List<string> { "attack", "aheli", "attackheli", "attackhelicopter" },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo { amount = 4000, displayName = "Scrap" }
                },
                SpawnCooldown = 1800,
                RecallCooldown = 30,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 900,
                        recallCooldown = 10
                    }
                }
            };

            [JsonProperty(PropertyName = "Transport Helicopter", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public TransportHelicopterSettings transportHelicopter = new TransportHelicopterSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                DisplayName = "Transport Copter",
                Distance = 7,
                MinDistanceForPlayers = 4,
                flyHackPause = 0,
                rotationScale = 1.0f,
                liftFraction = .25f,
                instantTakeoff = false,
                UsePermission = true,
                Permission = "RustVehicles.transportcopter",
                BypassCostPermission = "RustVehicles.transportcopterfree",
                Commands = new List<string>
                {
                    "tcop", "transportcopter"
                },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo { amount = 5000, displayName = "Scrap" }
                },
                SpawnCooldown = 2400,
                RecallCooldown = 30,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 1200,
                        recallCooldown = 10
                    }
                }
            };

            [JsonProperty(PropertyName = "Work Cart", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public WorkCartSettings workCart = new WorkCartSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                DisplayName = "Work Cart",
                Distance = 12,
                MinDistanceForPlayers = 6,
                UsePermission = true,
                Permission = "RustVehicles.workcart",
                BypassCostPermission = "RustVehicles.workcartfree",
                Commands = new List<string>
                {
                    "cart", "workcart"
                },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo { amount = 2000, displayName = "Scrap" }
                },
                SpawnCooldown = 1800,
                RecallCooldown = 30,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 900,
                        recallCooldown = 10
                    }
                }
            };
            [JsonProperty(PropertyName = "Sedan Rail", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public WorkCartSettings sedanRail = new WorkCartSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                DisplayName = "Sedan Rail",
                Distance = 6,
                MinDistanceForPlayers = 3,
                UsePermission = true,
                Permission = "RustVehicles.sedanrail",
                BypassCostPermission = "RustVehicles.sedanrailfree",
                Commands = new List<string>
                {
                    "carrail", "sedanrail"
                },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo { amount = 500, displayName = "Scrap" }
                },
                SpawnCooldown = 600,
                RecallCooldown = 30,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 300,
                        recallCooldown = 10
                    }
                }
            };

            [JsonProperty(PropertyName = "Magnet Crane", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public MagnetCraneSettings magnetCrane = new MagnetCraneSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                DisplayName = "Magnet Crane",
                Distance = 16,
                MinDistanceForPlayers = 8,
                UsePermission = true,
                Permission = "RustVehicles.magnetcrane",
                BypassCostPermission = "RustVehicles.magnetcranefree",
                Commands = new List<string>
                {
                    "crane", "magnetcrane"
                },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo { amount = 2000, displayName = "Scrap" }
                },
                SpawnCooldown = 600,
                RecallCooldown = 30,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 300,
                        recallCooldown = 10
                    }
                }
            };

            [JsonProperty(PropertyName = "Submarine Solo", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public SubmarineSoloSettings submarineSolo = new SubmarineSoloSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                DisplayName = "Submarine Solo",
                Distance = 5,
                MinDistanceForPlayers = 2,
                UsePermission = true,
                Permission = "RustVehicles.submarinesolo",
                BypassCostPermission = "RustVehicles.submarinesolofree",
                Commands = new List<string> { "subsolo", "solo" },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo { amount = 600, displayName = "Scrap" }
                },
                SpawnCooldown = 300,
                RecallCooldown = 30,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 150,
                        recallCooldown = 10
                    }
                }
            };

            [JsonProperty(PropertyName = "Submarine Duo", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public SubmarineDuoSettings submarineDuo = new SubmarineDuoSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                DisplayName = "Submarine Duo",
                Distance = 5,
                MinDistanceForPlayers = 2,
                UsePermission = true,
                Permission = "RustVehicles.submarineduo",
                BypassCostPermission = "RustVehicles.submarineduofree",
                Commands = new List<string> { "subduo", "duo" },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo { amount = 1000, displayName = "Scrap" }
                },
                SpawnCooldown = 300,
                RecallCooldown = 30,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 150,
                        recallCooldown = 10
                    }
                }
            };

            [JsonProperty(PropertyName = "Snowmobile", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public SnowmobileSettings snowmobile = new SnowmobileSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                DisplayName = "Snowmobile",
                Distance = 5,
                MinDistanceForPlayers = 2,
                UsePermission = true,
                Permission = "RustVehicles.snowmobile",
                BypassCostPermission = "RustVehicles.snowmobilefree",
                Commands = new List<string> { "snow", "snowmobile" },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo { amount = 1000, displayName = "Scrap" }
                },
                SpawnCooldown = 300,
                RecallCooldown = 30,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 150,
                        recallCooldown = 10
                    }
                }
            };

            [JsonProperty(PropertyName = "Pedal Bike", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public PedalBikeSettings pedalBike = new PedalBikeSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                DisplayName = "Pedal Bike",
                Distance = 5,
                MinDistanceForPlayers = 2,
                UsePermission = true,
                Permission = "RustVehicles.pedalbike",
                BypassCostPermission = "RustVehicles.pedalbikefree",
                Commands = new List<string> { "bike", "pbike" },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo { amount = 100, displayName = "Scrap" }
                },
                SpawnCooldown = 300,
                RecallCooldown = 30,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 150,
                        recallCooldown = 10
                    }
                }
            };
            [JsonProperty(PropertyName = "Pedal Trike", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public PedalTrikeSettings pedalTrike = new PedalTrikeSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                DisplayName = "Pedal Trike",
                Distance = 5,
                MinDistanceForPlayers = 2,
                UsePermission = true,
                Permission = "RustVehicles.pedaltrike",
                BypassCostPermission = "RustVehicles.pedaltrikefree",
                Commands = new List<string> { "trike", "ptrike" },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo { amount = 200, displayName = "Scrap" }
                },
                SpawnCooldown = 300,
                RecallCooldown = 30,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 150,
                        recallCooldown = 10
                    }
                }
            };

            [JsonProperty(PropertyName = "Motorbike", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public MotorBikeSettings motorBike = new MotorBikeSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                DisplayName = "Motorbike",
                Distance = 5,
                MinDistanceForPlayers = 2,
                UsePermission = true,
                Permission = "RustVehicles.motorbike",
                BypassCostPermission = "RustVehicles.motorbikefree",
                Commands = new List<string> { "mbike", "motorbike" },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo { amount = 750, displayName = "Scrap" }
                },
                SpawnCooldown = 300,
                RecallCooldown = 30,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 150,
                        recallCooldown = 10
                    }
                }
            };

            [JsonProperty(PropertyName = "Motorbike Sidecar", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public MotorBikeSidecarSettings motorBikeSidecar = new MotorBikeSidecarSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                DisplayName = "Motorbike Sidecar",
                Distance = 5,
                MinDistanceForPlayers = 2,
                UsePermission = true,
                Permission = "RustVehicles.motorbikesidecar",
                BypassCostPermission = "RustVehicles.motorbikesidecarfree",
                Commands = new List<string> { "mbikescar", "motorbikesidecar" },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo { amount = 1000, displayName = "Scrap" }
                },
                SpawnCooldown = 300,
                RecallCooldown = 30,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 150,
                        recallCooldown = 10
                    }
                }
            };

            [JsonProperty(PropertyName = "Kayak", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public KayakSettings Kayak = new KayakSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                DisplayName = "Kayak",
                Distance = 5,
                MinDistanceForPlayers = 2,
                UsePermission = true,
                Permission = "RustVehicles.kayak",
                BypassCostPermission = "RustVehicles.kayakfree",
                Commands = new List<string> { "kayak" },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo { amount = 300, displayName = "Scrap" }
                },
                SpawnCooldown = 300,
                RecallCooldown = 30,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 30,
                        recallCooldown = 10
                    }
                }
            };

            [JsonProperty(PropertyName = "DPV", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public DpvSettings dpv = new DpvSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                DisplayName = "DPV",
                Distance = 5,
                MinDistanceForPlayers = 2,
                UsePermission = true,
                Permission = "RustVehicles.dpv",
                BypassCostPermission = "RustVehicles.dpvfree",
                Commands = new List<string> { "dpv" },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo { amount = 500, displayName = "Scrap" }
                },
                SpawnCooldown = 300,
                RecallCooldown = 30,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 30,
                        recallCooldown = 10
                    }
                }
            };

            [JsonProperty(PropertyName = "Siege Tower", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public SiegeTowerSettings siegeTower = new SiegeTowerSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                DisplayName = "Siege Tower",
                Distance = 15,
                MinDistanceForPlayers = 2,
                UsePermission = true,
                Permission = "RustVehicles.siegetower",
                BypassCostPermission = "RustVehicles.siegetowerfree",
                Commands = new List<string> { "siegetower", "tower" },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo { amount = 1500, displayName = "Scrap" }
                },
                SpawnCooldown = 300,
                RecallCooldown = 30,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 30,
                        recallCooldown = 10
                    }
                }
            };
            [JsonProperty(PropertyName = "Catapult", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public CatapultSettings catapult = new CatapultSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                DisplayName = "Catapult",
                Distance = 15,
                MinDistanceForPlayers = 2,
                UsePermission = true,
                Permission = "RustVehicles.catapult",
                BypassCostPermission = "RustVehicles.catapultfree",
                Commands = new List<string> { "catapult" },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo { amount = 1500, displayName = "Scrap" }
                },
                SpawnCooldown = 300,
                RecallCooldown = 30,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 30,
                        recallCooldown = 10
                    }
                }
            };

            [JsonProperty(PropertyName = "Batteringram", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public BatteringramSettings batteringram = new BatteringramSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                DisplayName = "Batteringram",
                Distance = 15,
                MinDistanceForPlayers = 2,
                UsePermission = true,
                Permission = "RustVehicles.batteringram",
                BypassCostPermission = "RustVehicles.batteringramfree",
                Commands = new List<string> { "batteringram" },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo { amount = 2500, displayName = "Scrap" }
                },
                SpawnCooldown = 300,
                RecallCooldown = 30,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 30,
                        recallCooldown = 10
                    }
                }
            };

            [JsonProperty(PropertyName = "Ballista", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public BallistaSettings ballista = new BallistaSettings
            {
                Purchasable = true,
                NoDamage = false,
                NoCollisionDamage = false,
                DisplayName = "Ballista",
                Distance = 15,
                MinDistanceForPlayers = 2,
                UsePermission = true,
                Permission = "RustVehicles.ballista",
                BypassCostPermission = "RustVehicles.ballistafree",
                Commands = new List<string> { "ballista" },
                PurchasePrices = new Dictionary<string, PriceInfo>
                {
                    ["scrap"] = new PriceInfo { amount = 1000, displayName = "Scrap" }
                },
                SpawnCooldown = 300,
                RecallCooldown = 30,
                CooldownPermissions = new Dictionary<string, CooldownPermission>
                {
                    ["RustVehicles.vip"] = new CooldownPermission
                    {
                        spawnCooldown = 30,
                        recallCooldown = 10
                    }
                }
            };
        }



        #region BaseSettings

        [JsonObject(MemberSerialization.OptIn)]
        public class BaseVehicleSettings
        {
            #region Properties

            [JsonProperty(PropertyName = "Purchasable")]
            public bool Purchasable { get; set; }

            [JsonProperty(PropertyName = "No Damage")]
            public bool NoDamage { get; set; }

            [JsonProperty(PropertyName = "No Collision Damage")]
            public bool NoCollisionDamage { get; set; }

            [JsonProperty(PropertyName = "Display Name")]
            public string DisplayName { get; set; }

            [JsonProperty(PropertyName = "Prefab Path", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public string PrefabPath { get; set; }

            [JsonProperty(PropertyName = "Skin ID", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public ulong SkinID { get; set; }

            [JsonProperty(PropertyName = "Use Permission")]
            public bool UsePermission { get; set; }

            [JsonProperty(PropertyName = "Permission")]
            public string Permission { get; set; }

            [JsonProperty(PropertyName = "Bypass Cost Permission")]
            public string BypassCostPermission { get; set; }

            [JsonProperty(PropertyName = "Distance To Spawn")]
            public float Distance { get; set; }

            [JsonProperty(PropertyName = "Time Before Vehicle Wipe (Seconds)")]
            public double WipeTime { get; set; }

            [JsonProperty(PropertyName = "Exclude cupboard zones when wiping")]
            public bool ExcludeCupboard { get; set; }

            [JsonProperty(PropertyName = "Maximum Health")]
            public float MaxHealth { get; set; }

            [JsonProperty(PropertyName = "Maximum Speed")]
            public float MaxSpeed { get; set; }

            [JsonProperty(PropertyName = "Can Recall Maximum Distance")]
            public float RecallMaxDistance { get; set; }

            [JsonProperty(PropertyName = "Can Kill Maximum Distance")]
            public float KillMaxDistance { get; set; }

            [JsonProperty(PropertyName = "Minimum distance from player to recall or spawn")]
            public float MinDistanceForPlayers { get; set; } = 3f;

            [JsonProperty(PropertyName = "Remove License Once Crashed")]
            public bool RemoveLicenseOnceCrash { get; set; }

            [JsonProperty(PropertyName = "Commands")]
            public List<string> Commands { get; set; } = new List<string>();

            [JsonProperty(PropertyName = "Purchase Prices")]
            public Dictionary<string, PriceInfo> PurchasePrices { get; set; } = new Dictionary<string, PriceInfo>();

            [JsonProperty(PropertyName = "Spawn Prices")]
            public Dictionary<string, PriceInfo> SpawnPrices { get; set; } = new Dictionary<string, PriceInfo>();

            [JsonProperty(PropertyName = "Recall Prices")]
            public Dictionary<string, PriceInfo> RecallPrices { get; set; } = new Dictionary<string, PriceInfo>();

            [JsonProperty(PropertyName = "Recall Cooldown Bypass Prices")]
            public Dictionary<string, PriceInfo> BypassRecallCooldownPrices { get; set; } = new Dictionary<string, PriceInfo>();

            [JsonProperty(PropertyName = "Spawn Cooldown Bypass Prices")]
            public Dictionary<string, PriceInfo> BypassSpawnCooldownPrices { get; set; } = new Dictionary<string, PriceInfo>();

            [JsonProperty(PropertyName = "Spawn Cooldown (Seconds)")]
            public double SpawnCooldown { get; set; }

            [JsonProperty(PropertyName = "Recall Cooldown (Seconds)")]
            public double RecallCooldown { get; set; }

            [JsonProperty(PropertyName = "Cooldown Permissions")]
            public Dictionary<string, CooldownPermission> CooldownPermissions { get; set; } = new Dictionary<string, CooldownPermission>();


            #endregion Properties

            protected PluginConfiguration configData => Instance.configData;

            public virtual bool IsWaterVehicle => false;
            public virtual bool IsTrainVehicle => false;
            public virtual bool IsNormalVehicle => true;
            public virtual bool IsFightVehicle => false;
            public virtual bool IsModularVehicle => false;
            public virtual bool IsConnectableVehicle => false;
            public virtual bool CustomVehicle => false;

            protected virtual IFuelSystem GetFuelSystem(BaseEntity entity)
            {
                return null;
            }
            protected virtual IEnumerable<ItemContainer> GetInventories(BaseEntity entity)
            {
                yield break;
            }

            #region Spawn

            protected virtual string GetVehiclePrefab(string vehicleType)
            {
                switch (vehicleType)
                {
                    case "Tugboat":
                        return PREFAB_TUGBOAT;
                    case "Rowboat":
                        return PREFAB_ROWBOAT;
                    case "RHIB":
                        return PREFAB_RHIB;
                    case "Sedan":
                        return PREFAB_SEDAN;
                    case "HotAirBalloon":
                    case "ArmoredHotAirBalloon":
                        return PREFAB_HOTAIRBALLOON;
                    case "MiniCopter":
                        return PREFAB_MINICOPTER;
                    case "AttackHelicopter":
                        return PREFAB_ATTACKHELICOPTER;
                    case "TransportHelicopter":
                        return PREFAB_TRANSPORTCOPTER;
                    case "Chinook":
                        return PREFAB_CHINOOK;
                    case "RidableHorse":
                        return PREFAB_RIDABLEHORSE;
                    case "WorkCart":
                        return PREFAB_WORKCART;
                    case "SedanRail":
                        return PREFAB_SEDANRAIL;
                    case "MagnetCrane":
                        return PREFAB_MAGNET_CRANE;
                    case "SubmarineSolo":
                        return PREFAB_SUBMARINE_SOLO;
                    case "SubmarineDuo":
                        return PREFAB_SUBMARINE_DUO;
                    case "Snowmobile":
                        return PREFAB_SNOWMOBILE;
                    case "PedalBike":
                        return PREFAB_PEDALBIKE;
                    case "PedalTrike":
                        return PREFAB_PEDALTRIKE;
                    case "MotorBike":
                        return PREFAB_MOTORBIKE;
                    case "MotorBike_SideCar":
                        return PREFAB_MOTORBIKE_SIDECAR;
                    case "Kayak":
                        return PREFAB_KAYAK;
                    case "Dpv":
                        return PREFAB_DPV;
                    case "SiegeTower":
                        return PREFAB_SIEGETOWER;
                    case "Catapult":
                        return PREFAB_CATAPULT;
                    case "Batteringram":
                        return PREFAB_BATTERINGRAM;
                    case "Ballista":
                        return PREFAB_BALLISTA;
                    default:
                        return null;
                }
            }

            internal virtual string GetVehicleCustomPrefab(string vehicleType)
            {
                if (string.IsNullOrEmpty(vehicleType))
                    return string.Empty;

                if (Instance?.allVehicleSettings.TryGetValue(vehicleType, out var settings) == true && 
                    !string.IsNullOrEmpty(settings.PrefabPath))
                {
                    return settings.PrefabPath;
                }

                if (Instance?.discoveredCustomVehicles.TryGetValue(vehicleType, out var customInfo) == true &&
                    !string.IsNullOrEmpty(customInfo.PrefabPath))
                {
                    return customInfo.PrefabPath;
                }

                return string.Empty;
            }

            public virtual BaseEntity SpawnVehicle(BasePlayer player, OwnedVehicle vehicle, Vector3 position, Quaternion rotation)
            {
                var prefab = GetVehiclePrefab(vehicle.VehicleType);
                if (string.IsNullOrEmpty(prefab))
                {
                    prefab = GetVehicleCustomPrefab(vehicle.VehicleType);
                    if (string.IsNullOrEmpty(prefab)) throw new ArgumentException($"Prefab not found for {vehicle.VehicleType}");
                }
                var entity = GameManager.server.CreateEntity(prefab, position, rotation);
                if (entity == null)
                {
                    return null;
                }
                PreSetupVehicle(entity, vehicle, player);
                entity.Spawn();
                SetupVehicle(entity, vehicle, player);

                var ridableHorse = entity as RidableHorse;
                if (ridableHorse != null)
                {
                    var horsePosition = ridableHorse.transform.position;
                    horsePosition.y -= 2.5f;
                    ridableHorse.transform.position = horsePosition;
                }

                if (!entity.IsDestroyed)
                {
                    Instance.CacheVehicleEntity(entity, vehicle, player);
                    return ModifyVehicle(entity, vehicle, player);
                }
                Instance.Print(player, Instance.Lang("NotSpawnedOrRecalled", player.UserIDString, DisplayName));
                return null;
            }

            #region Setup

            public virtual void PreSetupVehicle(BaseEntity entity, OwnedVehicle vehicle, BasePlayer player)
            {
                bool isCustomVehicle = entity.PrefabName != null && entity.PrefabName.Contains("assets/custom/");
                entity.enableSaving = !isCustomVehicle && configData.global.storeVehicle;
                entity.OwnerID = player.userID;
            }

            public virtual void SetupVehicle(BaseEntity entity, OwnedVehicle vehicle, BasePlayer player, bool justCreated = true)
            {
                if (MaxHealth > 0 && Math.Abs(MaxHealth - entity.MaxHealth()) > 0f)
                {
                    (entity as BaseCombatEntity)?.InitializeHealth(MaxHealth, MaxHealth);
                }

                var helicopterVehicle = entity as BaseHelicopter;
                if (helicopterVehicle != null)
                {
                    if (configData.global.noServerGibs)
                    {
                        helicopterVehicle.serverGibs.guid = string.Empty;
                    }
                    if (configData.global.noFireBall)
                    {
                        helicopterVehicle.fireBall.guid = string.Empty;
                    }
                    if (configData.global.noMapMarker)
                    {
                        var ch47Helicopter = entity as CH47Helicopter;
                        if (ch47Helicopter != null)
                        {
                            if (ch47Helicopter.mapMarkerInstance)
                            {
                                ch47Helicopter.mapMarkerInstance.Kill();
                            }
                            ch47Helicopter.mapMarkerEntityPrefab.guid = string.Empty;
                        }
                    }
                }
                if (!configData.global.preventShattering) return;
                var magnetLiftable = entity.GetComponent<MagnetLiftable>();
                if (magnetLiftable != null)
                {
                    UnityEngine.Object.Destroy(magnetLiftable);
                }
            }
            private BaseEntity ModifyVehicle(BaseEntity entity, OwnedVehicle vehicle, BasePlayer player)
            {
                if (entity is RidableHorse)
                {
                    var ridableHorse = entity as RidableHorse;

                    string randBreed = configData.normalVehicles.ridableHorse.Breeds[
                        Random.Range(0, configData.normalVehicles.ridableHorse.Breeds.Count)
                    ];
                    if (configData.normalVehicles.ridableHorse.BreedsRef.TryGetValue(randBreed, out int breedIndex))
                    {
                        ridableHorse.ApplyBreed(breedIndex);
                    }

                    if (configData.normalVehicles.ridableHorse.IsDoubleSaddle)
                    {
                        ridableHorse.SetFlag(BaseEntity.Flags.Reserved9, false, networkupdate: false);
                        ridableHorse.SetFlag(BaseEntity.Flags.Reserved10, true, networkupdate: false);
                        ridableHorse.UpdateMountFlags();
                    }

                    string armorType = configData.normalVehicles.ridableHorse.ArmorType?.ToLower();
                    if (!string.IsNullOrEmpty(armorType))
                    {
                        int armorItemId = GetHorseArmorItemId(armorType);
                        if (armorItemId != 0 && ridableHorse.equipmentInventory != null)
                        {
                            Item armorItem = ItemManager.CreateByItemID(armorItemId);
                            if (armorItem != null)
                            {
                                armorItem.MoveToContainer(ridableHorse.equipmentInventory);
                                ridableHorse.EquipmentUpdate();
                            }
                        }
                    }

                    return entity;
                }
                if (entity is Tugboat)
                {
                    Tugboat tug = entity as Tugboat;
                    tug.engineThrust *= configData.normalVehicles.tugboat.speedMultiplier;
                    if (!configData.normalVehicles.tugboat.autoAuth) return entity;
                    Instance.AuthAlliesOnTugboat(tug, player);
                    return entity;
                }

                if (entity is AttackHelicopter)
                {
                    AttackHelicopter attackHelicopter = entity as AttackHelicopter;
                    attackHelicopter.torqueScale *= configData.normalVehicles.attackHelicopter.rotationScale;
                    attackHelicopter.liftFraction = configData.normalVehicles.attackHelicopter.liftFraction;
                    return entity;
                }
                if (entity is HotAirBalloon && vehicle.VehicleType.Equals("ArmoredHotAirBalloon"))
                {
                    HotAirBalloon HAB = entity as HotAirBalloon;
                    Item armor = ItemManager.CreateByItemID(ITEMID_HOTAIRBALLOON_ARMOR);
                    if (armor == null)
                    {
                        Debug.Log("[RustVehicles] Please report this to the developer/maintainer. PREFAB_HOTAIRBALLOON_ARMOR's item is NULL");
                        return entity;
                    }
                    ItemModHABEquipment component = armor.info.GetComponent<ItemModHABEquipment>();
                    if (component == null) return entity;
                    HotAirBalloonEquipment equipment = GameManager.server.CreateEntity(component.Prefab.resourcePath, HAB.transform.position, HAB.transform.rotation) as HotAirBalloonEquipment;
                    equipment.SetParent(HAB, true);
                    equipment.Spawn();
                    float delayNextUpgradeOnRemoveDuration = equipment.DelayNextUpgradeOnRemoveDuration;
                    equipment.DelayNextUpgradeOnRemoveDuration = delayNextUpgradeOnRemoveDuration;
                    armor.UseItem();
                    HAB.SendNetworkUpdateImmediate();
                    return entity;
                }

                if (entity is ScrapTransportHelicopter)
                {
                    ScrapTransportHelicopter scrap = entity as ScrapTransportHelicopter;
                    scrap.torqueScale *= configData.normalVehicles.transportHelicopter.rotationScale;
                    scrap.liftFraction = configData.normalVehicles.transportHelicopter.liftFraction;
                    return entity;
                }

                if (entity is Minicopter)
                {
                    Minicopter mini = entity as Minicopter;
                    mini.torqueScale *= configData.normalVehicles.miniCopter.rotationScale;
                    mini.liftFraction = configData.normalVehicles.miniCopter.liftFraction;
                    return entity;
                }
                return entity;
            }

            int GetHorseArmorItemId(string armorType)
            {
                switch (armorType)
                {
                    case "wood":
                        return 1659447559;
                    case "roadsign":
                        return 60528587;
                    default:
                        return 0;
                }
            }

            #endregion Setup

            #endregion Spawn

            #region Recall

            public virtual void PreRecallVehicle(BasePlayer player, OwnedVehicle vehicle, Vector3 position, Quaternion rotation)
            {
                if (configData.global.dismountAllPlayersRecall)
                {
                    DismountAllPlayers(vehicle.Entity);
                }

                if (CanDropInventory())
                {
                    TryDropVehicleInventory(player, vehicle);
                }

                if (vehicle.Entity.HasParent())
                {
                    vehicle.Entity.SetParent(null, true, true);
                }
            }

            public virtual void PostRecallVehicle(BasePlayer player, OwnedVehicle vehicle, Vector3 position, Quaternion rotation)
            {
            }

            #region DropInventory

            protected virtual bool CanDropInventory()
            {
                return false;
            }

            private void TryDropVehicleInventory(BasePlayer player, OwnedVehicle vehicle)
            {
                var droppedItemContainer = DropVehicleInventory(player, vehicle);
                if (droppedItemContainer != null)
                {
                    Instance.Print(player, Instance.Lang("VehicleInventoryDropped", player.UserIDString, DisplayName));
                }
            }

            protected virtual DroppedItemContainer DropVehicleInventory(BasePlayer player, OwnedVehicle vehicle)
            {
                var inventories = GetInventories(vehicle.Entity);
                foreach (var inventory in inventories)
                {
                    if (inventory != null)
                    {
                        return inventory.Drop(PREFAB_ITEM_DROP, vehicle.Entity.GetDropPosition(), vehicle.Entity.transform.rotation, 0);
                    }
                }
                return null;
            }

            #endregion DropInventory

            #region Train Car

            protected bool TryGetTrainCarPositionAndRotation(BasePlayer player, OwnedVehicle vehicle, ref string reason, ref Vector3 original, ref Quaternion rotation)
            {
                float distResult;
                TrainTrackSpline splineResult;
                if (!TrainTrackSpline.TryFindTrackNear(original, Distance, out splineResult, out distResult))
                {
                    reason = Instance.Lang("TooFarTrainTrack", player.UserIDString);
                    return false;
                }

                var position = splineResult.GetPosition(distResult);
                if (!SpaceIsClearForTrainTrack(vehicle, position, rotation))
                {
                    reason = Instance.Lang("TooCloseTrainBarricadeOrWorkCart", player.UserIDString);
                    return false;
                }

                original = position;
                reason = null;
                return true;
            }

            protected bool TryMoveToTrainTrackNear(TrainCar trainCar)
            {
                float distResult;
                TrainTrackSpline splineResult;
                if (TrainTrackSpline.TryFindTrackNear(trainCar.GetFrontWheelPos(), 2f, out splineResult, out distResult))
                {
                    trainCar.FrontWheelSplineDist = distResult;
                    Vector3 tangent;
                    var positionAndTangent = splineResult.GetPositionAndTangent(trainCar.FrontWheelSplineDist, trainCar.transform.forward, out tangent);
                    trainCar.SetTheRestFromFrontWheelData(ref splineResult, positionAndTangent, tangent, trainCar.localTrackSelection, null, true);
                    trainCar.FrontTrackSection = splineResult;
                    if (trainCar.SpaceIsClear())
                    {
                        return true;
                    }
                }
                return false;
            }

            protected bool SpaceIsClearForTrainTrack(OwnedVehicle vehicle, Vector3 position, Quaternion rotation)
            {
                var colliders = Pool.Get<List<Collider>>();
                if (vehicle.Entity == null)
                {
                    var prefab = GetVehiclePrefab(vehicle.VehicleType);

                    if (string.IsNullOrEmpty(prefab)) prefab = GetVehicleCustomPrefab(vehicle.VehicleType);

                    if (!string.IsNullOrEmpty(prefab))
                    {
                        var trainEngine = GameManager.server.FindPrefab(prefab)?.GetComponent<TrainEngine>();
                        if (trainEngine != null)
                        {
                            GamePhysics.OverlapOBB(new OBB(position, trainEngine.transform.lossyScale, rotation, trainEngine.bounds), colliders, Layers.Mask.Vehicle_World);
                        }
                    }
                }
                else
                {
                    GamePhysics.OverlapOBB(new OBB(position, vehicle.Entity.transform.lossyScale, rotation, vehicle.Entity.bounds), colliders, Layers.Mask.Vehicle_World);
                }
                var free = true;
                foreach (var item in colliders)
                {
                    var baseEntity = item.ToBaseEntity();
                    if (baseEntity == vehicle.Entity)
                    {
                        continue;
                    }
                    free = false;
                    break;
                }
                Pool.FreeUnmanaged(ref colliders);
                return free;
            }

            #endregion

            #endregion Recall

            #region Refund

            protected virtual bool CanRefundFuel(bool isCrash, bool isUnload)
            {
                return false;
            }

            protected virtual bool CanRefundInventory(bool isCrash, bool isUnload)
            {
                return false;
            }

            protected virtual void CollectVehicleItems(List<Item> items, OwnedVehicle vehicle, bool isCrash, bool isUnload)
            {
                if (CanRefundFuel(isCrash, isUnload))
                {
                    var fuelSystem = GetFuelSystem(vehicle.Entity);
                    if (fuelSystem is EntityFuelSystem entityFuelSystem)
                    {
                        var fuelContainer = entityFuelSystem.GetFuelContainer();
                        if (fuelContainer != null && fuelContainer.inventory != null && fuelContainer.inventory.itemList != null)
                        {
                            items.AddRange(fuelContainer.inventory.itemList);
                        }
                    }
                }
                if (CanRefundInventory(isCrash, isUnload))
                {
                    var inventories = GetInventories(vehicle.Entity);
                    foreach (var inventory in inventories)
                    {
                        if (inventory == null || inventory.itemList == null)
                        {
                            continue;
                        }
                        items.AddRange(inventory.itemList);
                    }
                }
            }

            public void RefundVehicleItems(OwnedVehicle vehicle, bool isCrash, bool isUnload)
            {
                var collect = Pool.Get<List<Item>>();

                CollectVehicleItems(collect, vehicle, isCrash, isUnload);

                if (collect.Count > 0)
                {
                    var player = RustCore.FindPlayerById(vehicle.PlayerId);
                    if (player == null)
                    {
                        DropItemContainer(vehicle.Entity, vehicle.PlayerId, collect);
                    }
                    else
                    {
                        for (var i = 0; i < collect.Count; i++)
                        {
                            var item = collect[i];
                            player.GiveItem(item);
                        }

                        if (player.IsConnected)
                        {
                            Instance.Print(player, Instance.Lang("RefundedVehicleItems", player.UserIDString, DisplayName));
                        }
                    }
                }
                Pool.FreeUnmanaged(ref collect);
            }

            #endregion Refund

            #region GiveFuel

            protected void TryGiveFuel(BaseEntity entity, IFuelVehicle iFuelVehicle)
            {
                if (iFuelVehicle == null || iFuelVehicle.SpawnFuelAmount <= 0)
                {
                    return;
                }
                var fuelSystem = GetFuelSystem(entity);
                if (fuelSystem is EntityFuelSystem entityFuelSystem)
                {
                    var fuelContainer = entityFuelSystem.GetFuelContainer();
                    if (fuelContainer != null && fuelContainer.inventory != null)
                    {
                        var fuelItem = ItemManager.CreateByItemID(ITEMID_FUEL, iFuelVehicle.SpawnFuelAmount);
                        if (!fuelItem.MoveToContainer(fuelContainer.inventory))
                        {
                            fuelItem.Remove();
                        }
                    }
                }
            }

            #endregion GiveFuel

            #region Permission

            public double GetCooldown(BasePlayer player, bool isSpawn)
            {
                var cooldown = isSpawn ? SpawnCooldown : RecallCooldown;
                foreach (var entry in CooldownPermissions)
                {
                    var currentCooldown = isSpawn ? entry.Value.spawnCooldown : entry.Value.recallCooldown;
                    if (cooldown > currentCooldown && Instance.permission.UserHasPermission(player.UserIDString, entry.Key))
                    {
                        cooldown = currentCooldown;
                    }
                }
                return cooldown;
            }

            #endregion Permission

            #region TryGetVehicleParams

            public virtual bool TryGetVehicleParams(BasePlayer player, OwnedVehicle vehicle, out string reason, ref Vector3 spawnPos, ref Quaternion spawnRot)
            {
                Vector3 original;
                Quaternion rotation;
                if (!TryGetPositionAndRotation(player, vehicle, out reason, out original, out rotation))
                {
                    return false;
                }

                CorrectPositionAndRotation(player, vehicle, original, rotation, out spawnPos, out spawnRot);
                return true;
            }

            protected virtual float GetSpawnRotationAngle()
            {
                return 90f;
            }

            protected virtual Vector3 GetOriginalPosition(BasePlayer player)
            {
                if (configData.global.spawnLookingAt || IsWaterVehicle || IsTrainVehicle)
                {
                    return GetGroundPositionLookingAt(player, Distance, IsWaterVehicle, !IsTrainVehicle);
                }

                return player.transform.position;
            }

            protected virtual bool TryGetPositionAndRotation(BasePlayer player, OwnedVehicle vehicle, out string reason, out Vector3 original, out Quaternion rotation)
            {
                original = GetOriginalPosition(player);
                rotation = Quaternion.identity;
                if (MinDistanceForPlayers > 0)
                {
                    var nearbyPlayers = Pool.Get<List<BasePlayer>>();
                    Vis.Entities(original, MinDistanceForPlayers, nearbyPlayers, Layers.Mask.Player_Server);
                    bool flag = false;
                    foreach (var nearbyPlayer in nearbyPlayers)
                    {
                        if (nearbyPlayer.userID.IsSteamId() && nearbyPlayer != player)
                        {
                            flag = true;
                            break;
                        }
                    }
                    Pool.FreeUnmanaged(ref nearbyPlayers);
                    if (flag)
                    {
                        reason = Instance.Lang("PlayersOnNearby", player.UserIDString, DisplayName);
                        return false;
                    }
                }
                if (IsWaterVehicle && !IsInWater(original))
                {
                    reason = Instance.Lang("NotLookingAtWater", player.UserIDString, DisplayName);
                    return false;
                }
                RaycastHit hit;
                if (IsWaterVehicle && Physics.Raycast(original, player.eyes.MovementForward(), out hit, 100))
                {
                    if (hit.GetEntity() is PaddlingPool)
                    {
                        reason = Instance.Lang("NotLookingAtWater", player.UserIDString, DisplayName);
                        return false;
                    }
                    List<BaseEntity> pools = Pool.Get<List<BaseEntity>>();
                    Vis.Entities(original, 0.5f, pools, Layers.Mask.Deployed);
                    bool hasPaddlingPool = false;
                    foreach (var pool in pools)
                    {
                        if (pool is PaddlingPool)
                        {
                            hasPaddlingPool = true;
                            break;
                        }
                    }
                    if (hasPaddlingPool)
                    {
                        reason = Instance.Lang("NotLookingAtWater", player.UserIDString, DisplayName);
                        Pool.FreeUnmanaged(ref pools);
                        return false;
                    }
                    Pool.FreeUnmanaged(ref pools);
                }
                reason = null;
                return true;
            }

            protected virtual void CorrectPositionAndRotation(BasePlayer player, OwnedVehicle vehicle, Vector3 original, Quaternion rotation, out Vector3 spawnPos, out Quaternion spawnRot)
            {
                spawnPos = original;

                if (IsTrainVehicle)
                {
                    var forward = player.eyes.HeadForward().WithY(0);
                    spawnRot = forward != Vector3.zero ? Quaternion.LookRotation(forward) : Quaternion.identity;
                    return;
                }

                if (configData.global.spawnLookingAt)
                {
                    var needGetGround = true;

                    if (IsWaterVehicle)
                    {
                        RaycastHit hit;
                        if (Physics.Raycast(spawnPos, Vector3.up, out hit, 100, LAYER_GROUND) && hit.GetEntity() is StabilityEntity)
                        {
                            needGetGround = false;
                        }

                        float waterHeight = WaterLevel.GetWaterSurface(spawnPos, true, true, null);

                        spawnPos.y = waterHeight;

                        if ((int)player.transform.position.y >= -1)
                        {
                            if (vehicle.VehicleType == "Tugboat" &&
                                Vector3.Distance(spawnPos, player.transform.position) < configData.normalVehicles.tugboat.Distance &&
                                spawnPos.y - player.transform.position.y < configData.normalVehicles.tugboat.Distance)
                            {
                                spawnPos += player.eyes.MovementForward() * configData.normalVehicles.tugboat.Distance;
                            }
                        }
                        else
                        {
                            if (vehicle.VehicleType == "Tugboat" &&
                                Vector3.Distance(spawnPos, player.transform.position) < configData.normalVehicles.tugboat.Distance &&
                                spawnPos.y - player.transform.position.y < configData.normalVehicles.tugboat.Distance)
                            {
                                spawnPos += player.eyes.MovementForward() * configData.normalVehicles.tugboat.Distance;
                            }
                        }
                    }
                    else
                    {
                        if (TryGetCenterOfFloorNearby(ref spawnPos))
                        {
                            needGetGround = false;

                            if (vehicle.VehicleType == "TransportHelicopter" &&
                                Vector3.Distance(spawnPos, player.transform.position) < configData.normalVehicles.transportHelicopter.Distance)
                            {
                                spawnPos += player.eyes.MovementForward() * configData.normalVehicles.transportHelicopter.Distance;
                            }
                        }
                    }

                    if (needGetGround)
                    {
                        spawnPos = GetGroundPosition(spawnPos, IsWaterVehicle);

                        if (IsWaterVehicle)
                        {
                            float waterHeight = WaterLevel.GetWaterSurface(spawnPos, true, true, null);
                            float verticalOffset = 1f;

                            if (vehicle.VehicleType == "SkyBoat") verticalOffset = 2f;
                            else if (vehicle.VehicleType == "Tugboat") verticalOffset = 1.5f;

                            spawnPos.y = waterHeight + verticalOffset;

                            if ((int)player.transform.position.y >= -1 && spawnPos.y <= -1)
                            {
                                if (vehicle.VehicleType == "Tugboat" &&
                                    Vector3.Distance(spawnPos, player.transform.position) < configData.normalVehicles.tugboat.Distance &&
                                    spawnPos.y - player.transform.position.y < configData.normalVehicles.tugboat.Distance)
                                {
                                    spawnPos += player.eyes.MovementForward() * configData.normalVehicles.tugboat.Distance;
                                }
                            }
                            else if ((int)player.transform.position.y < -1)
                            {
                                if (vehicle.VehicleType == "Tugboat" &&
                                    Vector3.Distance(spawnPos, player.transform.position) < configData.normalVehicles.tugboat.Distance &&
                                    spawnPos.y - player.transform.position.y < configData.normalVehicles.tugboat.Distance)
                                {
                                    spawnPos += player.eyes.MovementForward() * configData.normalVehicles.tugboat.Distance;
                                }
                            }
                        }

                        if (vehicle.VehicleType == "TransportHelicopter" &&
                            Vector3.Distance(spawnPos, player.transform.position) < configData.normalVehicles.transportHelicopter.Distance)
                        {
                            spawnPos += player.eyes.MovementForward() * configData.normalVehicles.transportHelicopter.Distance;
                        }
                    }
                }
                else
                {
                    GetPositionWithNoPlayersNearby(player, ref spawnPos);
                }

                var normalized = (spawnPos - player.transform.position).normalized;
                var angle = normalized != Vector3.zero ? Quaternion.LookRotation(normalized).eulerAngles.y : Random.Range(0f, 360f);
                var rotationAngle = GetSpawnRotationAngle();
                spawnRot = Quaternion.Euler(Vector3.up * (angle + rotationAngle));
            }

            private void GetPositionWithNoPlayersNearby(BasePlayer player, ref Vector3 spawnPos)
            {
                var minDistance = Mathf.Min(MinDistanceForPlayers, 2.5f);
                var maxDistance = Mathf.Max(Distance, minDistance);

                var players = new BasePlayer[1];
                var sourcePos = spawnPos;
                for (var i = 0; i < 10; i++)
                {
                    spawnPos.x = sourcePos.x + Random.Range(minDistance, maxDistance) * (Random.value >= 0.5f ? 1 : -1);
                    spawnPos.z = sourcePos.z + Random.Range(minDistance, maxDistance) * (Random.value >= 0.5f ? 1 : -1);

                    if (BaseEntity.Query.Server.GetPlayersInSphere(spawnPos, minDistance, players, p => p.userID.IsSteamId() && p != player) == 0)
                    {
                        break;
                    }
                }
                spawnPos = GetGroundPosition(spawnPos, IsWaterVehicle);
            }

            private bool TryGetCenterOfFloorNearby(ref Vector3 spawnPos)
            {
                var buildingBlocks = Pool.Get<List<BuildingBlock>>();
                Vis.Entities(spawnPos, 2f, buildingBlocks, Layers.Mask.Construction);
                if (buildingBlocks.Count > 0)
                {
                    var position = spawnPos;
                    BuildingBlock closestBuildingBlock = null;
                    float closestDistance = float.MaxValue;
                    foreach (var block in buildingBlocks)
                    {
                        if (!block.ShortPrefabName.Contains("wall"))
                        {
                            float distance = (block.transform.position - position).magnitude;
                            if (distance < closestDistance)
                            {
                                closestDistance = distance;
                                closestBuildingBlock = block;
                            }
                        }
                    }
                    if (closestBuildingBlock != null)
                    {
                        var worldSpaceBounds = closestBuildingBlock.WorldSpaceBounds();
                        spawnPos = worldSpaceBounds.position;
                        spawnPos.y += worldSpaceBounds.extents.y;
                        Pool.FreeUnmanaged(ref buildingBlocks);
                        return true;
                    }
                }
                Pool.FreeUnmanaged(ref buildingBlocks);
                return false;
            }

            #endregion TryGetVehicleParams
        }
        public abstract class FuelVehicleSettings : BaseVehicleSettings, IFuelVehicle
        {
            public int SpawnFuelAmount { get; set; }
            public bool RefundFuelOnKill { get; set; } = true;
            public bool RefundFuelOnCrash { get; set; } = true;

            public override void SetupVehicle(BaseEntity entity, OwnedVehicle vehicle, BasePlayer player, bool justCreated = true)
            {
                if (justCreated)
                {
                    TryGiveFuel(entity, this);
                }
                base.SetupVehicle(entity, vehicle, player, justCreated);
            }

            protected override bool CanRefundFuel(bool isCrash, bool isUnload)
            {
                return isUnload || (isCrash ? RefundFuelOnCrash : RefundFuelOnKill);
            }
        }

        public abstract class InventoryVehicleSettings : BaseVehicleSettings, IInventoryVehicle
        {
            public bool RefundInventoryOnKill { get; set; } = true;
            public bool RefundInventoryOnCrash { get; set; } = true;
            public bool DropInventoryOnRecall { get; set; }

            protected override bool CanDropInventory()
            {
                return DropInventoryOnRecall;
            }

            protected override bool CanRefundInventory(bool isCrash, bool isUnload)
            {
                return isUnload || (isCrash ? RefundInventoryOnCrash : RefundInventoryOnKill);
            }
        }

        public abstract class InvFuelVehicleSettings : BaseVehicleSettings, IFuelVehicle, IInventoryVehicle
        {
            public int SpawnFuelAmount { get; set; }
            public bool RefundFuelOnKill { get; set; } = true;
            public bool RefundFuelOnCrash { get; set; } = true;
            public bool RefundInventoryOnKill { get; set; } = true;
            public bool RefundInventoryOnCrash { get; set; } = true;
            public bool DropInventoryOnRecall { get; set; }

            public override void SetupVehicle(BaseEntity entity, OwnedVehicle vehicle, BasePlayer player, bool justCreated = true)
            {
                if (justCreated)
                {
                    TryGiveFuel(entity, this);
                }
                base.SetupVehicle(entity, vehicle, player, justCreated);
            }

            protected override bool CanDropInventory()
            {
                return DropInventoryOnRecall;
            }

            protected override bool CanRefundInventory(bool isCrash, bool isUnload)
            {
                return isUnload || (isCrash ? RefundInventoryOnCrash : RefundInventoryOnKill);
            }

            protected override bool CanRefundFuel(bool isCrash, bool isUnload)
            {
                return isUnload || (isCrash ? RefundFuelOnCrash : RefundFuelOnKill);
            }
        }

        #endregion BaseSettings

        #region Interface

        public interface IFuelVehicle
        {
            [JsonProperty(PropertyName = "Amount Of Fuel To Spawn", Order = 20)]
            int SpawnFuelAmount { get; set; }

            [JsonProperty(PropertyName = "Refund Fuel On Kill", Order = 21)]
            bool RefundFuelOnKill { get; set; }

            [JsonProperty(PropertyName = "Refund Fuel On Crash", Order = 22)]
            bool RefundFuelOnCrash { get; set; }
        }

        public interface IInventoryVehicle
        {
            [JsonProperty(PropertyName = "Refund Inventory On Kill", Order = 30)]
            bool RefundInventoryOnKill { get; set; }

            [JsonProperty(PropertyName = "Refund Inventory On Crash", Order = 31)]
            bool RefundInventoryOnCrash { get; set; }

            [JsonProperty(PropertyName = "Drop Inventory Items When Vehicle Recall", Order = 49)]
            bool DropInventoryOnRecall { get; set; }
        }

        public interface IModularVehicle
        {
            [JsonProperty(PropertyName = "Refund Engine Items On Kill", Order = 40)]
            bool RefundEngineOnKill { get; set; }

            [JsonProperty(PropertyName = "Refund Engine Items On Crash", Order = 41)]
            bool RefundEngineOnCrash { get; set; }

            [JsonProperty(PropertyName = "Refund Module Items On Kill", Order = 42)]
            bool RefundModuleOnKill { get; set; }

            [JsonProperty(PropertyName = "Refund Module Items On Crash", Order = 43)]
            bool RefundModuleOnCrash { get; set; }
        }

        public interface IAmmoVehicle
        {
            [JsonProperty(PropertyName = "Amount Of Ammo To Spawn", Order = 20)]
            int SpawnAmmoAmount { get; set; }
        }

        public interface ITrainVehicle
        {
        }

        #endregion Interface

        #region Struct

        public struct CooldownPermission
        {
            public double spawnCooldown;
            public double recallCooldown;
        }

        public struct ModuleItem
        {
            public string shortName;
            public float healthPercentage;
        }

        public struct EngineItem
        {
            public string shortName;
            public float conditionPercentage;
        }

        public struct PriceInfo
        {
            public int amount;
            public string displayName;
        }

        public struct TrainComponent
        {
            public string type;
        }

        #endregion Struct

        #region VehicleSettings

        public class PedalBikeSettings : BaseVehicleSettings
        {
        }

        public class PedalTrikeSettings : BaseVehicleSettings
        {
        }

        public class MotorBikeSettings : FuelVehicleSettings
        {
            protected override IFuelSystem GetFuelSystem(BaseEntity entity)
            {
                return (entity as Bike)?.GetFuelSystem();
            }
        }

        public class MotorBikeSidecarSettings : FuelVehicleSettings
        {
            protected override IFuelSystem GetFuelSystem(BaseEntity entity)
            {
                return (entity as Bike)?.GetFuelSystem();
            }
        }

        public class AtvSettings : FuelVehicleSettings
        {
            protected override IFuelSystem GetFuelSystem(BaseEntity entity)
            {
                return (entity as Bike)?.GetFuelSystem();
            }

            public override bool CustomVehicle => true;
        }
        public class RaceSofaSettings : FuelVehicleSettings
        {
            protected override IFuelSystem GetFuelSystem(BaseEntity entity)
            {
                return (entity as Bike)?.GetFuelSystem();
            }

            public override bool CustomVehicle => true;
        }

        public class KayakSettings : BaseVehicleSettings
        {
            public override bool IsWaterVehicle => true;
        }

        public class SedanSettings : BaseVehicleSettings
        {
        }

        public class ChinookSettings : BaseVehicleSettings
        {
        }

        public class DpvSettings : FuelVehicleSettings
        {
            public override bool IsWaterVehicle => true;

            protected override IFuelSystem GetFuelSystem(BaseEntity entity)
            {
                return (entity as DiverPropulsionVehicle)?.GetFuelSystem();
            }
        }

        public class RowboatSettings : InvFuelVehicleSettings
        {
            public override bool IsWaterVehicle => true;

            protected override IFuelSystem GetFuelSystem(BaseEntity entity)
            {
                return (entity as MotorRowboat)?.GetFuelSystem();
            }

            protected override IEnumerable<ItemContainer> GetInventories(BaseEntity entity)
            {
                yield return (entity as MotorRowboat)?.storageUnitInstance.Get(true)?.inventory;
            }
        }

        public class RhibSettings : RowboatSettings
        {
        }

        public class SiegeTowerSettings : BaseVehicleSettings
        {
        }

        public class CatapultSettings : BaseVehicleSettings
        {
        }

        public class BatteringramSettings : FuelVehicleSettings
        {
            protected override IFuelSystem GetFuelSystem(BaseEntity entity)
            {
                return (entity as BatteringRam)?.GetFuelSystem();
            }
        }

        public class BallistaSettings : BaseVehicleSettings
        {
        }

        public class TugboatSettings : FuelVehicleSettings
        {
            public override bool IsWaterVehicle => true;

            [JsonProperty(PropertyName = "Speed Multiplier")]
            public float speedMultiplier { get; set; } = 1;

            [JsonProperty(PropertyName = "Auto Auth Teammates on spawn/recall")]
            public bool autoAuth { get; set; } = true;

            protected override IFuelSystem GetFuelSystem(BaseEntity entity)
            {
                return (entity as MotorRowboat)?.GetFuelSystem();
            }
        }


        public class HotAirBalloonSettings : InvFuelVehicleSettings
        {
            protected override float GetSpawnRotationAngle()
            {
                return 180f;
            }

            protected override IFuelSystem GetFuelSystem(BaseEntity entity)
            {
                return (entity as HotAirBalloon)?.fuelSystem;
            }

            protected override IEnumerable<ItemContainer> GetInventories(BaseEntity entity)
            {
                yield return (entity as HotAirBalloon)?.storageUnitInstance.Get(true)?.inventory;
            }
        }

        public class ArmoredHotAirBalloonSettings : HotAirBalloonSettings
        {
        }

        #region CopterOptions: Prefabs, State, and Helpers

        private readonly string MCO_MINICOPTER_PREFAB = "assets/content/vehicles/minicopter/minicopter.entity.prefab";
        private readonly string MCO_STORAGE_SMALL_PREFAB = "assets/prefabs/deployable/hot air balloon/subents/hab_storage.prefab";
        private readonly string MCO_STORAGE_LARGE_PREFAB = "assets/content/vehicles/boats/rhib/subents/rhib_storage.prefab";
        private readonly string MCO_AUTOTURRET_PREFAB = "assets/prefabs/npc/autoturret/autoturret_deployed.prefab";
        private readonly string MCO_SWITCH_PREFAB = "assets/prefabs/deployable/playerioents/simpleswitch/switch.prefab";
        private readonly string MCO_SEARCHLIGHT_PREFAB = "assets/prefabs/deployable/search light/searchlight.deployed.prefab";
        private readonly string MCO_FLASHER_BLUE_PREFAB = "assets/prefabs/deployable/playerioents/lights/flasherlight/electric.flasherlight.deployed.prefab";
        private readonly string MCO_SPHERE_PREFAB = "assets/prefabs/visualization/sphere.prefab";

        private static readonly Vector3 MCO_TURRET_SWITCH_LOCAL_POS = new Vector3(0f, 0.36f, 0.32f);

        private TOD_Sky mcoTime;
        private float mcoSunrise;
        private float mcoSunset;
        private float mcoLastHourChecked;
        private bool mcoLastNightState;

        private static void MCO_SetupInvincible(BaseCombatEntity entity)
        {
            if (entity == null) return;
            entity._maxHealth = 99999999f;
            entity._health = 99999999f;
            entity.SendNetworkUpdate();
        }

        private static void MCO_DestroyGroundComps(BaseEntity entity)
        {
            if (entity == null) return;
            UnityEngine.Object.DestroyImmediate(entity.GetComponent<DestroyOnGroundMissing>());
            UnityEngine.Object.DestroyImmediate(entity.GetComponent<GroundWatch>());
        }

        private static void MCO_DestroyAllMeshColliders(BaseEntity entity)
        {
            if (entity == null) return;
            foreach (var mc in entity.GetComponentsInChildren<MeshCollider>())
            {
                UnityEngine.Object.DestroyImmediate(mc);
            }
        }

        private static StorageContainer[] MCO_GetCopterStorage(Minicopter mini, string smallPrefab, string largePrefab)
        {
            var containers = mini.GetComponentsInChildren<StorageContainer>();
            var result = new List<StorageContainer>();
            foreach (var container in containers)
            {
                if (container != null && (container.name == smallPrefab || container.name == largePrefab))
                {
                    result.Add(container);
                }
            }
            var array = new StorageContainer[result.Count];
            for (int i = 0; i < result.Count; i++)
            {
                array[i] = result[i];
            }
            return array;
        }

        private static void MCO_SetupLargeStorage(StorageContainer box, bool lockable, int capacity)
        {
            if (box == null) return;
            box.isLockable = lockable;
            if (box.inventory != null)
            {
                box.inventory.capacity = Mathf.Clamp(capacity, 6, 48);
            }
            box.panelName = "generic_resizable";
        }

        private void MCO_AddStorageBox(Minicopter mini, string prefab, Vector3 localPos, Quaternion localRot)
        {
            var box = GameManager.server.CreateEntity(prefab, mini.transform.position, mini.transform.rotation) as StorageContainer;
            if (box == null) return;
            box.Spawn();
            box.SetParent(mini, false, false);
            box.transform.localPosition = localPos;
            box.transform.localRotation = localRot;
            box.SendNetworkUpdateImmediate();
            if (prefab == MCO_STORAGE_LARGE_PREFAB)
            {
                var baseLock = box.GetComponentInChildren<BaseLock>();
                if (baseLock != null)
                {
                    baseLock.transform.localPosition = new Vector3(0.0f, 0.3f, 0.298f);
                    baseLock.transform.localRotation = Quaternion.Euler(new Vector3(0, 90, 0));
                    baseLock.SendNetworkUpdateImmediate();
                }
            }
        }

        private void MCO_AddSearchLight(Minicopter mini)
        {
            var sphere = GameManager.server.CreateEntity(MCO_SPHERE_PREFAB, mini.transform.position) as SphereEntity;
            if (sphere == null) return;
            sphere.EnableSaving(true);
            sphere.EnableGlobalBroadcast(false);
            sphere.Spawn();
            sphere.SetParent(mini, false, false);
            sphere.transform.localPosition = new Vector3(0f, 0.24f, 1.8f);
            sphere.currentRadius = 0.1f;
            sphere.lerpRadius = 0.1f;
            sphere.UpdateScale();
            sphere.SendNetworkUpdateImmediate();

            var light = GameManager.server.CreateEntity(MCO_SEARCHLIGHT_PREFAB, mini.transform.position) as SearchLight;
            if (light == null) return;
            light.pickup.enabled = false;
            MCO_DestroyAllMeshColliders(light);
            MCO_DestroyGroundComps(light);
            light.Spawn();
            MCO_SetupInvincible(light);
            light.SetFlag(BaseEntity.Flags.Reserved5, true);
            light.SetFlag(BaseEntity.Flags.Busy, true);
            light.SetParent(sphere, false, false);
            light.transform.localPosition = Vector3.zero;
            light.transform.localRotation = Quaternion.Euler(-20f, 180f, 180f);
            light.SendNetworkUpdateImmediate();
        }

        private void MCO_AddTailFlasher(Minicopter mini)
        {
            var flasher = GameManager.server.CreateEntity(MCO_FLASHER_BLUE_PREFAB, mini.transform.position) as FlasherLight;
            if (flasher == null) return;
            flasher.pickup.enabled = false;
            MCO_DestroyGroundComps(flasher);
            flasher.SetParent(mini, false, false);
            var s = configData.normalVehicles.miniCopter;
            flasher.transform.localPosition = new Vector3(s.tailLightPosX, s.tailLightPosY, s.tailLightPosZ);
            flasher.transform.localRotation = Quaternion.Euler(s.tailLightRotX, s.tailLightRotY, s.tailLightRotZ);
            flasher.Spawn();
            var sl = mini.GetComponentInChildren<SearchLight>();
            var initial = sl != null && sl.IsPowered();
            flasher.SetFlag(IOEntity.Flag_HasPower, initial);
            flasher.SendNetworkUpdateImmediate();
        }

        private void MCO_AddTurretWithSwitch(Minicopter mini, ulong ownerId)
        {
            var turret = GameManager.server.CreateEntity(MCO_AUTOTURRET_PREFAB, mini.transform.position) as AutoTurret;
            if (turret == null) return;
            turret.pickup.enabled = false;
            MCO_DestroyAllMeshColliders(turret);
            MCO_DestroyGroundComps(turret);
            turret.SetParent(mini, false, false);
            turret.transform.localPosition = new Vector3(0f, 0f, 2.47f);
            turret.transform.localRotation = Quaternion.identity;
            turret.Spawn();

            var player = BasePlayer.FindByID(ownerId);
            if (player != null)
            {
                turret.authorizedPlayers.Add(player.userID);
                turret.SendNetworkUpdate();
            }

            var sw = GameManager.server.CreateEntity(MCO_SWITCH_PREFAB, mini.transform.position, mini.transform.rotation) as ElectricSwitch;
            if (sw != null)
            {
                sw.pickup.enabled = false;
                MCO_DestroyAllMeshColliders(sw);
                MCO_DestroyGroundComps(sw);
                sw.SetParent(turret, false, false);
                sw.transform.localPosition = MCO_TURRET_SWITCH_LOCAL_POS;
                sw.transform.localRotation = Quaternion.identity;
                sw.Spawn();
                MCO_SetupInvincible(sw);
            }
        }

        private void MCO_ApplyMiniCopterOptions(Minicopter mini, OwnedVehicle vehicle, BasePlayer player, bool justCreated)
        {
            if (mini == null) return;
            var settings = configData.normalVehicles.miniCopter;

            if (settings.fuelPerSec >= 0f)
            {
                mini.fuelPerSec = settings.fuelPerSec;
            }

            var existing = MCO_GetCopterStorage(mini, MCO_STORAGE_SMALL_PREFAB, MCO_STORAGE_LARGE_PREFAB);
            bool hasExtraSeats = settings.extraSeats;
            if (!hasExtraSeats)
            {
                var entities = mini.GetComponentsInChildren<BaseEntity>();
                foreach (var c in entities)
                {
                    if (c != null && (c.PrefabName == "assets/prefabs/vehicle/seats/passengerchair.prefab" || c.PrefabName == "assets/prefabs/deployable/chair/chair.deployed.prefab"))
                    {
                        hasExtraSeats = true;
                        break;
                    }
                }
            }
            if (existing != null && existing.Length > 0)
            {
                foreach (var box in existing)
                {
                    if (box.PrefabName == MCO_STORAGE_LARGE_PREFAB)
                    {
                        MCO_SetupLargeStorage(box, settings.largeStorageLockable, settings.largeStorageSize);
                    }
                }
            }
            else
            {
                if (settings.storageLargeContainers == 1)
                {
                    MCO_AddStorageBox(mini, MCO_STORAGE_LARGE_PREFAB, new Vector3(0.0f, 0.07f, -1.05f), Quaternion.Euler(0f, 180f, 0f));
                }
                else if (settings.storageLargeContainers >= 2)
                {
                    MCO_AddStorageBox(mini, MCO_STORAGE_LARGE_PREFAB, new Vector3(-0.48f, 0.07f, -1.05f), Quaternion.Euler(0f, 180f, 0f));
                    MCO_AddStorageBox(mini, MCO_STORAGE_LARGE_PREFAB, new Vector3(0.48f, 0.07f, -1.05f), Quaternion.Euler(0f, 180f, 0f));
                }

                switch (Mathf.Clamp(settings.storageContainers, 0, 3))
                {
                    case 1:
                        MCO_AddStorageBox(mini, MCO_STORAGE_SMALL_PREFAB, new Vector3(0f, 0.75f, -1f), Quaternion.identity);
                        break;
                    case 2:
                        if (!hasExtraSeats)
                        {
                            MCO_AddStorageBox(mini, MCO_STORAGE_SMALL_PREFAB, new Vector3(0.6f, 0.24f, -0.35f), Quaternion.identity);
                            MCO_AddStorageBox(mini, MCO_STORAGE_SMALL_PREFAB, new Vector3(-0.6f, 0.24f, -0.35f), Quaternion.identity);
                        }
                        break;
                    case 3:
                        MCO_AddStorageBox(mini, MCO_STORAGE_SMALL_PREFAB, new Vector3(0f, 0.75f, -1f), Quaternion.identity);
                        if (!hasExtraSeats)
                        {
                            MCO_AddStorageBox(mini, MCO_STORAGE_SMALL_PREFAB, new Vector3(0.6f, 0.24f, -0.35f), Quaternion.identity);
                            MCO_AddStorageBox(mini, MCO_STORAGE_SMALL_PREFAB, new Vector3(-0.6f, 0.24f, -0.35f), Quaternion.identity);
                        }
                        break;
                }
            }

            foreach (var box in mini.GetComponentsInChildren<StorageContainer>())
            {
                if (box != null && box.PrefabName == MCO_STORAGE_LARGE_PREFAB)
                {
                    MCO_SetupLargeStorage(box, settings.largeStorageLockable, settings.largeStorageSize);
                }
            }

            if (settings.addSearchLight)
            {
                var light = mini.GetComponentInChildren<SearchLight>();
                if (light == null)
                {
                    MCO_AddSearchLight(mini);
                }
                else
                {
                    light.pickup.enabled = false;
                    MCO_DestroyAllMeshColliders(light);
                    MCO_DestroyGroundComps(light);
                    MCO_SetupInvincible(light);
                }
            }

            if (settings.lightTail)
            {
                var flasher = mini.GetComponentInChildren<FlasherLight>();
                if (flasher == null)
                {
                    MCO_AddTailFlasher(mini);
                }
                else
                {
                    flasher.pickup.enabled = false;
                    MCO_DestroyGroundComps(flasher);
                    MCO_SetupInvincible(flasher);
                    var slNow = mini.GetComponentInChildren<SearchLight>();
                    var pow = slNow != null && slNow.IsPowered();
                    flasher.SetFlag(IOEntity.Flag_HasPower, pow);
                }
            }

            if (settings.autoturret)
            {
                var turret = mini.GetComponentInChildren<AutoTurret>();
                if (turret == null)
                {
                    MCO_AddTurretWithSwitch(mini, vehicle?.PlayerId ?? player?.userID ?? 0);
                    turret = mini.GetComponentInChildren<AutoTurret>();
                }
                if (turret != null)
                {
                    turret.pickup.enabled = false;
                    turret.sightRange = settings.turretRange;
                    MCO_DestroyAllMeshColliders(turret);
                    MCO_DestroyGroundComps(turret);
                }
            }
        }

        internal object OnTurretTarget(AutoTurret turret, BaseCombatEntity target)
        {
            if (turret == null || target == null) return null;
            var mini = turret.GetParentEntity() as Minicopter;
            if (mini == null) return null;
            var s = configData.normalVehicles.miniCopter;
            if (!s.autoturret) return null;

            if (!s.autoTurretTargetsAnimals && target is BaseAnimalNPC) return _false;

            var bp = target as BasePlayer;
            if (bp != null)
            {
                if (!s.autoTurretTargetsNPCs && bp.IsNpc) return _false;
                if (!s.autoTurretTargetsPlayers && bp.userID.IsSteamId()) return _false;
                if (bp.InSafeZone() && (bp.IsNpc || !bp.IsHostile())) return _false;
            }
            return null;
        }

        internal void OnSwitchToggled(ElectricSwitch electricSwitch, BasePlayer player)
        {
            var turret = electricSwitch?.GetParentEntity() as AutoTurret;
            if (turret == null) return;
            var mini = turret.GetParentEntity() as Minicopter;
            if (mini == null) return;
            if (!configData.normalVehicles.miniCopter.autoturret) return;

            if (electricSwitch.IsOn())
            {
                turret.SetFlag(IOEntity.Flag_HasPower, true);
                turret.InitiateStartup();
            }
            else
            {
                turret.SetFlag(IOEntity.Flag_HasPower, false);
                turret.InitiateShutdown();
            }
        }

        internal object OnServerCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Connection == null || arg.cmd.FullName != "inventory.lighttoggle") return null;
            var player = arg.Player();
            if (player == null) return null;
            var mini = player.GetMountedVehicle() as Minicopter;
            if (mini == null) return null;
            if (!mini.IsDriver(player)) return null;
            if (!configData.normalVehicles.miniCopter.addSearchLight)
            {
                var flasherOnlyMini = player.GetMountedVehicle() as Minicopter;
                if (flasherOnlyMini != null)
                {
                    var flasherOnly = flasherOnlyMini.GetComponentInChildren<FlasherLight>();
                    if (flasherOnly != null)
                    {
                        var desired = !flasherOnly.IsPowered();
                        flasherOnly.SetFlag(IOEntity.Flag_HasPower, desired);
                        flasherOnly.SendNetworkUpdateImmediate();
                        return _false;
                    }
                }
                return null;
            }

            foreach (var child in mini.children)
            {
                var sphere = child as SphereEntity;
                if (sphere == null) continue;
                foreach (var grandChild in sphere.children)
                {
                    var light = grandChild as SearchLight;
                    if (light == null) continue;
                    var willPower = !light.IsPowered();
                    light.SetFlag(IOEntity.Flag_HasPower, willPower);
                    light.SendNetworkUpdateImmediate();
                    var flasher = mini.GetComponentInChildren<FlasherLight>();
                    if (flasher != null)
                    {
                        flasher.SetFlag(IOEntity.Flag_HasPower, willPower);
                        flasher.SendNetworkUpdateImmediate();
                    }
                    return _false;
                }
            }

            return null;
        }

        #endregion CopterOptions: Prefabs, State, and Helpers
        public class MiniCopterSettings : FuelVehicleSettings
        {
            public override bool IsFightVehicle => true;

            [JsonProperty("Rotation Scale")]
            public float rotationScale = 1.0f;

            [JsonProperty("Lift Fraction")]
            public float liftFraction = 0.25f;

            [JsonProperty("Seconds to pause flyhack when dismount from Mini Copter.")]
            public int flyHackPause;

            [JsonProperty("Instant Engine Start-up (instant take-off)")]
            public bool instantTakeoff;

            [JsonProperty("Extra Seats")]
            public bool extraSeats = false;

            [JsonProperty("Fuel per Second")]
            public float fuelPerSec = -1f;

            [JsonProperty("Storage Containers")]
            public int storageContainers = 0;

            [JsonProperty("Large Storage Containers")]
            public int storageLargeContainers = 0;

            [JsonProperty("Large Storage Lockable")]
            public bool largeStorageLockable = true;

            [JsonProperty("Large Storage Size (Max 48)")]
            public int largeStorageSize = 48;

            [JsonProperty("Drop Storage Loot On Death")]
            public bool dropStorage = true;

            [JsonProperty("Add auto turret to heli")]
            public bool autoturret = false;

            [JsonProperty("Auto turret targets players")]
            public bool autoTurretTargetsPlayers = true;

            [JsonProperty("Auto turret targets NPCs")]
            public bool autoTurretTargetsNPCs = true;

            [JsonProperty("Auto turret targets animals")]
            public bool autoTurretTargetsAnimals = true;

            [JsonProperty("Mini Turret Range (Default 30)")]
            public float turretRange = 30f;

            [JsonProperty("Light: Add Searchlight to heli")]
            public bool addSearchLight = true;

            [JsonProperty("Light: Add Nightitme Tail Light")]
            public bool lightTail = false;

            [JsonProperty("Tail Light Pos X")]
            public float tailLightPosX = 0f;

            [JsonProperty("Tail Light Pos Y")]
            public float tailLightPosY = 0.825f;

            [JsonProperty("Tail Light Pos Z")]
            public float tailLightPosZ = -2.65f;

            [JsonProperty("Tail Light Rot X (Euler)")]
            public float tailLightRotX = 270f;

            [JsonProperty("Tail Light Rot Y (Euler)")]
            public float tailLightRotY = 0f;

            [JsonProperty("Tail Light Rot Z (Euler)")]
            public float tailLightRotZ = 0f;

            protected override IFuelSystem GetFuelSystem(BaseEntity entity)
            {
                return (entity as Minicopter)?.GetFuelSystem();
            }

            protected override bool CanRefundInventory(bool isCrash, bool isUnload)
            {
                return isUnload;
            }

            protected override IEnumerable<ItemContainer> GetInventories(BaseEntity entity)
            {
                var mini = entity as Minicopter;
                if (mini == null) yield break;
                foreach (var container in mini.GetComponentsInChildren<StorageContainer>())
                {
                    if (container != null && container.inventory != null)
                        yield return container.inventory;
                }
                foreach (var turret in mini.GetComponentsInChildren<AutoTurret>())
                {
                    if (turret != null && turret.inventory != null)
                        yield return turret.inventory;
                }
            }

            public override void SetupVehicle(BaseEntity entity, OwnedVehicle vehicle, BasePlayer player, bool justCreated = true)
            {
                var mini = entity as Minicopter;
                if (mini != null)
                {
                    mini.torqueScale *= rotationScale;
                    mini.liftFraction = liftFraction;

                    Instance.MCO_ApplyMiniCopterOptions(mini, vehicle, player, justCreated);
                }

                base.SetupVehicle(entity, vehicle, player, justCreated);
                if (!extraSeats) return;

                bool hasPassengerChair = false;
                var entities = entity.GetComponentsInChildren<BaseEntity>();
                foreach (var c in entities)
                {
                    if (c != null && c.PrefabName == "assets/prefabs/vehicle/seats/passengerchair.prefab")
                    {
                        hasPassengerChair = true;
                        break;
                    }
                }
                if (hasPassengerChair)
                {
                    return;
                }

                var baseVehicle = entity as BaseVehicle;
                mini = entity as Minicopter;
                if (baseVehicle == null || mini == null) return;

                if (baseVehicle.mountPoints == null || baseVehicle.mountPoints.Count == 0) return;

                try
                {
                    var empty = Vector3.zero;
                    var pFront = baseVehicle.mountPoints.Count > 1 ? baseVehicle.mountPoints[1] : baseVehicle.mountPoints[0];

                    Vector3 leftVector = new Vector3(0.6f, 0.2f, -0.2f);
                    Vector3 rightVector = new Vector3(-0.6f, 0.2f, -0.2f);
                    Vector3 backVector = new Vector3(0.0f, 0.4f, -1.2f);
                    Vector3 backVector2 = new Vector3(0.0f, 0.4f, -1.45f);
                    Vector3 playerOffsetVector = new Vector3(0f, 0f, -0.25f);
                    Quaternion backQuaternion = Quaternion.Euler(0f, 180f, 0f);

                    BaseEntity seatLeft = GameManager.server.CreateEntity("assets/prefabs/vehicle/seats/passengerchair.prefab", baseVehicle.transform.position, baseVehicle.transform.rotation) as BaseEntity;
                    BaseEntity seatRight = GameManager.server.CreateEntity("assets/prefabs/vehicle/seats/passengerchair.prefab", baseVehicle.transform.position, baseVehicle.transform.rotation) as BaseEntity;
                    BaseEntity seatBack = GameManager.server.CreateEntity("assets/prefabs/vehicle/seats/passengerchair.prefab", baseVehicle.transform.position, baseVehicle.transform.rotation) as BaseEntity;
                    if (seatLeft == null || seatRight == null || seatBack == null)
                    {
                        seatLeft?.Kill(); seatRight?.Kill(); seatBack?.Kill();
                        return;
                    }
                    seatLeft.SetParent(baseVehicle, false, false);
                    seatLeft.transform.localPosition = leftVector + playerOffsetVector;
                    seatLeft.transform.localRotation = new Quaternion();
                    seatLeft.Spawn();

                    seatRight.SetParent(baseVehicle, false, false);
                    seatRight.transform.localPosition = rightVector + playerOffsetVector;
                    seatRight.transform.localRotation = new Quaternion();
                    seatRight.Spawn();

                    seatBack.SetParent(baseVehicle, false, false);
                    seatBack.transform.localPosition = backVector;
                    seatBack.transform.localRotation = backQuaternion;
                    seatBack.Spawn();

                    BaseVehicle.MountPointInfo CreateMount(Vector3 vec, BaseVehicle.MountPointInfo exampleSeat, Vector3 rotation, BaseMountable mountable)
                    {
                        return new BaseVehicle.MountPointInfo
                        {
                            pos = vec,
                            rot = rotation,
                            bone = exampleSeat.bone,
                            prefab = exampleSeat.prefab,
                            mountable = mountable
                        };
                    }

                    var pLeftSide = CreateMount(leftVector, pFront, empty, seatLeft.GetComponent<BaseMountable>());
                    var pRightSide = CreateMount(rightVector, pFront, empty, seatRight.GetComponent<BaseMountable>());
                    baseVehicle.mountPoints.Add(pLeftSide);
                    baseVehicle.mountPoints.Add(pRightSide);

                    var pBackReverse = CreateMount(backVector2, pFront, new Vector3(0f, 180f, 0f), seatBack.GetComponent<BaseMountable>());
                    baseVehicle.mountPoints.Add(pBackReverse);

                    baseVehicle.SendNetworkUpdateImmediate();
                }
                catch { }
            }
        }

        public class AttackHelicopterSettings : InvFuelVehicleSettings
        {
            private const int HV_AMMO_ITEM_ID = -1841918730;
            private const int INCENDIARY_AMMO_ITEM_ID = 1638322904;
            private const int FLARE_ITEM_ID = 304481038;

            [JsonProperty("HV Rocket Spawn Amount")]
            public int HVSpawnAmmoAmount { get; set; }

            [JsonProperty("Incendiary Rocket Spawn Amount")]
            public int IncendiarySpawnAmmoAmount { get; set; }

            [JsonProperty("Flare Spawn Amount")]
            public int FlareSpawnAmmoAmount { get; set; }

            public override bool IsFightVehicle => true;

            [JsonProperty("Rotation Scale")]
            public float rotationScale = 1.0f;

            [JsonProperty("Lift Fraction")]
            public float liftFraction = 0.33f;

            [JsonProperty("Seconds to pause flyhack when dismount from Attack Helicopter.")]
            public int flyHackPause;

            [JsonProperty("Instant Engine Start-up (instant take-off)")]
            public bool instantTakeoff;

            [JsonProperty("Extra Seats")]
            public bool extraSeats = false;

            protected override IFuelSystem GetFuelSystem(BaseEntity entity)
            {
                return (entity as AttackHelicopter)?.GetFuelSystem();
            }

            protected override IEnumerable<ItemContainer> GetInventories(BaseEntity entity)
            {
                yield return (entity as AttackHelicopter)?.GetRockets().inventory;
                yield return (entity as AttackHelicopter)?.GetTurret().inventory;
            }
            public override void SetupVehicle(BaseEntity entity, OwnedVehicle vehicle, BasePlayer player, bool justCreated = true)
            {
                if (justCreated)
                {
                    TryGiveAmmo(entity);
                }
                base.SetupVehicle(entity, vehicle, player, justCreated);

                if (!extraSeats) return;

                var baseVehicle = entity as BaseVehicle;
                var heli = entity as AttackHelicopter;
                if (baseVehicle == null || heli == null) return;

                if (baseVehicle.mountPoints == null || baseVehicle.mountPoints.Count == 0) return;

                try
                {
                    var empty = Vector3.zero;
                    var pFront = baseVehicle.mountPoints.Count > 1 ? baseVehicle.mountPoints[1] : baseVehicle.mountPoints[0];
                    Vector3 leftVector = new Vector3(1.1f, 0.7f, 0.3f);
                    Vector3 rightVector = new Vector3(-1.1f, 0.7f, 0.3f);
                    Vector3 playerOffsetVector = new Vector3(0f, 0f, -0.25f);

                    BaseVehicle.MountPointInfo CreateMount(Vector3 vec, BaseVehicle.MountPointInfo exampleSeat, Vector3 rotation, BaseMountable mountable)
                    {
                        return new BaseVehicle.MountPointInfo
                        {
                            pos = vec,
                            rot = rotation,
                            bone = exampleSeat.bone,
                            prefab = exampleSeat.prefab,
                            mountable = mountable
                        };
                    }

                    var leftSeat = GameManager.server.CreateEntity("assets/prefabs/vehicle/seats/passengerchair.prefab", baseVehicle.transform.position, Quaternion.identity) as BaseEntity;
                    if (leftSeat != null)
                    {
                        leftSeat.SetParent(baseVehicle, false, false);
                        leftSeat.transform.localPosition = leftVector + playerOffsetVector;
                        leftSeat.transform.localRotation = Quaternion.identity;
                        leftSeat.Spawn();
                        var leftMountable = leftSeat.GetComponent<BaseMountable>();
                        var pLeftSide = CreateMount(leftVector, pFront, empty, leftMountable);
                        baseVehicle.mountPoints.Add(pLeftSide);
                    }

                    var rightSeat = GameManager.server.CreateEntity("assets/prefabs/vehicle/seats/passengerchair.prefab", baseVehicle.transform.position, Quaternion.identity) as BaseEntity;
                    if (rightSeat != null)
                    {
                        rightSeat.SetParent(baseVehicle, false, false);
                        rightSeat.transform.localPosition = rightVector + playerOffsetVector;
                        rightSeat.transform.localRotation = Quaternion.identity;
                        rightSeat.Spawn();
                        var rightMountable = rightSeat.GetComponent<BaseMountable>();
                        var pRightSide = CreateMount(rightVector, pFront, empty, rightMountable);
                        baseVehicle.mountPoints.Add(pRightSide);
                    }

                    baseVehicle.SendNetworkUpdateImmediate();
                }
                catch { }
            }

            private void TryGiveAmmo(BaseEntity entity)
            {
                if (entity == null || (HVSpawnAmmoAmount <= 0 && IncendiarySpawnAmmoAmount <= 0 && FlareSpawnAmmoAmount <= 0))
                {
                    return;
                }

                AttackHelicopterRockets ammoContainer = (entity as AttackHelicopter)?.GetRockets();

                if (ammoContainer == null || ammoContainer.inventory == null) return;

                Item ammoItem = ItemManager.CreateByItemID(HV_AMMO_ITEM_ID, HVSpawnAmmoAmount);
                if (!ammoItem.MoveToContainer(ammoContainer.inventory))
                {
                    ammoItem.Remove();
                }

                ammoItem = ItemManager.CreateByItemID(INCENDIARY_AMMO_ITEM_ID, IncendiarySpawnAmmoAmount);
                if (!ammoItem.MoveToContainer(ammoContainer.inventory))
                {
                    ammoItem.Remove();
                }

                ammoItem = ItemManager.CreateByItemID(FLARE_ITEM_ID, FlareSpawnAmmoAmount);
                if (!ammoItem.MoveToContainer(ammoContainer.inventory))
                {
                    ammoItem.Remove();
                }
            }
        }



        public class TransportHelicopterSettings : FuelVehicleSettings
        {
            public override bool IsFightVehicle => true;

            [JsonProperty("Lift Fraction")]
            public float liftFraction = 0.25f;

            [JsonProperty("Rotation Scale")]
            public float rotationScale = 1.0f;

            [JsonProperty("Seconds to pause flyhack when dismount from Transport Scrap Helicopter.")]
            public int flyHackPause;

            [JsonProperty("Instant Engine Start-up (instant take-off)")]
            public bool instantTakeoff;

            protected override IFuelSystem GetFuelSystem(BaseEntity entity)
            {
                return (entity as ScrapTransportHelicopter)?.GetFuelSystem();
            }
        }

        public class RidableHorseSettings : InventoryVehicleSettings
        {
            [JsonProperty("Spawn with Double Saddle")]
            public bool IsDoubleSaddle { get; set; }

            [JsonProperty("Armor Type (wood, roadsign)")]
            public string ArmorType { get; set; } = "";

            [JsonProperty("Breeds")]
            public List<string> Breeds { get; set; }

            [JsonIgnore]
            public Dictionary<string, int> BreedsRef = new Dictionary<string, int>()
            {
                ["Appalosa"] = 0,
                ["Bay"] = 1,
                ["Buckskin"] = 2,
                ["Chestnut"] = 3,
                ["Dapple Grey"] = 4,
                ["Piebald"] = 5,
                ["Pinto"] = 6,
                ["Red Roan"] = 7,
                ["White Thoroughbred"] = 8,
                ["Black Thoroughbred"] = 9
            };

            protected override IEnumerable<ItemContainer> GetInventories(BaseEntity entity)
            {
                yield return (entity as RidableHorse)?.storageInventory;
            }

            public override void PostRecallVehicle(BasePlayer player, OwnedVehicle vehicle, Vector3 position, Quaternion rotation)
            {
                base.PostRecallVehicle(player, vehicle, position, rotation);

                var ridableHorse = vehicle.Entity as RidableHorse;
                if (ridableHorse != null)
                {
                    ridableHorse.TryLeaveHitch();
                }
            }

            protected override void CorrectPositionAndRotation(BasePlayer player, OwnedVehicle vehicle, Vector3 original, Quaternion rotation, out Vector3 spawnPos, out Quaternion spawnRot)
            {
                base.CorrectPositionAndRotation(player, vehicle, original, rotation, out spawnPos, out spawnRot);
                spawnPos += Vector3.up * 0.3f;
            }
        }

        public class WorkCartSettings : FuelVehicleSettings
        {
            public override bool IsTrainVehicle => true;

            public bool IsConnectableEngine(TrainEngine trainEngine)
            {
                return trainEngine.frontCoupling != null && trainEngine.rearCoupling != null;
            }

            protected override IFuelSystem GetFuelSystem(BaseEntity entity)
            {
                return (entity as TrainEngine)?.GetFuelSystem();
            }

            public override void PostRecallVehicle(BasePlayer player, OwnedVehicle vehicle, Vector3 position, Quaternion rotation)
            {
                base.PostRecallVehicle(player, vehicle, position, rotation);
                var trainEngine = vehicle.Entity as TrainEngine;
                if (trainEngine != null)
                {
                    TryMoveToTrainTrackNear(trainEngine);
                }
            }

            protected override bool TryGetPositionAndRotation(BasePlayer player, OwnedVehicle vehicle, out string reason, out Vector3 original, out Quaternion rotation)
            {
                return !base.TryGetPositionAndRotation(player, vehicle, out reason, out original, out rotation)
                       || TryGetTrainCarPositionAndRotation(player, vehicle, ref reason, ref original, ref rotation);
            }
        }

        public class MagnetCraneSettings : FuelVehicleSettings
        {
            protected override IFuelSystem GetFuelSystem(BaseEntity entity)
            {
                return (entity as MagnetCrane)?.GetFuelSystem();
            }
        }
        public class SubmarineSoloSettings : InvFuelVehicleSettings, IAmmoVehicle
        {
            private const int AMMO_ITEM_ID = -1671551935;

            public int SpawnAmmoAmount { get; set; }
            public override bool IsWaterVehicle => true;

            protected override IFuelSystem GetFuelSystem(BaseEntity entity)
            {
                return (entity as BaseSubmarine)?.GetFuelSystem();
            }

            protected override IEnumerable<ItemContainer> GetInventories(BaseEntity entity)
            {
                yield return (entity as BaseSubmarine)?.GetItemContainer()?.inventory;
                yield return (entity as BaseSubmarine)?.GetTorpedoContainer()?.inventory;
            }

            public override void SetupVehicle(BaseEntity entity, OwnedVehicle vehicle, BasePlayer player, bool justCreated = true)
            {
                if (justCreated)
                {
                    TryGiveAmmo(entity);
                }
                base.SetupVehicle(entity, vehicle, player, justCreated);
            }

            private void TryGiveAmmo(BaseEntity entity)
            {
                if (entity == null || SpawnAmmoAmount <= 0)
                {
                    return;
                }
                var ammoContainer = (entity as BaseSubmarine)?.GetTorpedoContainer();

                if (ammoContainer == null || ammoContainer.inventory == null) return;

                var ammoItem = ItemManager.CreateByItemID(AMMO_ITEM_ID, SpawnAmmoAmount);
                if (!ammoItem.MoveToContainer(ammoContainer.inventory))
                {
                    ammoItem.Remove();
                }
            }
        }

        public class SubmarineDuoSettings : SubmarineSoloSettings
        {
        }

        public class SnowmobileSettings : InvFuelVehicleSettings
        {
            protected override IFuelSystem GetFuelSystem(BaseEntity entity)
            {
                return (entity as Snowmobile)?.GetFuelSystem();
            }

            protected override IEnumerable<ItemContainer> GetInventories(BaseEntity entity)
            {
                yield return (entity as Snowmobile)?.GetItemContainer()?.inventory;
            }
        }

        public class ModularVehicleSettings : InvFuelVehicleSettings, IModularVehicle
        {
            #region Properties

            public bool RefundEngineOnKill { get; set; } = false;
            public bool RefundEngineOnCrash { get; set; } = false;
            public bool RefundModuleOnKill { get; set; } = false;
            public bool RefundModuleOnCrash { get; set; } = false;

            [JsonProperty(PropertyName = "Chassis Type (Small, Medium, Large)", Order = 50)]
            public string ChassisType { get; set; } = "Small";

            [JsonProperty(PropertyName = "Vehicle Module Items", Order = 51)]
            public List<ModuleItem> ModuleItems { get; set; } = new List<ModuleItem>();

            [JsonProperty(PropertyName = "Vehicle Engine Items", Order = 52)]
            public List<EngineItem> EngineItems { get; set; } = new List<EngineItem>();

            #endregion Properties

            #region ModuleItems

            private List<ModuleItem> _validModuleItems;

            public IEnumerable<ModuleItem> ValidModuleItems
            {
                get
                {
                    if (_validModuleItems == null)
                    {
                        _validModuleItems = new List<ModuleItem>();
                        foreach (var modularItem in ModuleItems)
                        {
                            var itemDefinition = ItemManager.FindItemDefinition(modularItem.shortName);
                            if (itemDefinition != null)
                            {
                                var itemModVehicleModule = itemDefinition.GetComponent<ItemModVehicleModule>();
                                if (itemModVehicleModule == null || !itemModVehicleModule.entityPrefab.isValid)
                                {
                                    Instance.PrintError($"'{modularItem}' is not a valid vehicle module");
                                    continue;
                                }
                                _validModuleItems.Add(modularItem);
                            }
                        }
                    }
                    return _validModuleItems;
                }
            }

            public IEnumerable<Item> CreateModuleItems()
            {
                foreach (var moduleItem in ValidModuleItems)
                {
                    var item = ItemManager.CreateByName(moduleItem.shortName);
                    if (item != null)
                    {
                        item.condition = item.maxCondition * (moduleItem.healthPercentage / 100f);
                        item.MarkDirty();
                        yield return item;
                    }
                }
            }

            #endregion ModuleItems

            #region EngineItems

            private List<EngineItem> _validEngineItems;

            public IEnumerable<EngineItem> ValidEngineItems
            {
                get
                {
                    if (_validEngineItems == null)
                    {
                        _validEngineItems = new List<EngineItem>();
                        foreach (var modularItem in EngineItems)
                        {
                            var itemDefinition = ItemManager.FindItemDefinition(modularItem.shortName);
                            if (itemDefinition != null)
                            {
                                var itemModEngineItem = itemDefinition.GetComponent<ItemModEngineItem>();
                                if (itemModEngineItem == null)
                                {
                                    Instance.PrintError($"'{modularItem}' is not a valid engine item");
                                    continue;
                                }
                                _validEngineItems.Add(modularItem);
                            }
                        }
                    }
                    return _validEngineItems;
                }
            }

            public IEnumerable<Item> CreateEngineItems()
            {
                foreach (var engineItem in ValidEngineItems)
                {
                    var item = ItemManager.CreateByName(engineItem.shortName);
                    if (item != null)
                    {
                        item.condition = item.maxCondition * (engineItem.conditionPercentage / 100f);
                        item.MarkDirty();
                        yield return item;
                    }
                }
            }

            #endregion EngineItems

            public override bool IsNormalVehicle => false;
            public override bool IsModularVehicle => true;

            protected override IFuelSystem GetFuelSystem(BaseEntity entity)
            {
                return (entity as ModularCar)?.GetFuelSystem();
            }

            #region Spawn

            protected override string GetVehiclePrefab(string vehicleType)
            {
                switch (ChassisType)
                {
                    case "Small":
                        return PREFAB_CHASSIS_SMALL;
                    case "Medium":
                        return PREFAB_CHASSIS_MEDIUM;
                    case "Large":
                        return PREFAB_CHASSIS_LARGE;
                    default:
                        return null;
                }
            }
            #region Setup

            public override void SetupVehicle(BaseEntity entity, OwnedVehicle vehicle, BasePlayer player, bool justCreated = true)
            {
                var modularCar = entity as ModularCar;
                if (modularCar != null)
                {
                    bool hasModuleItems = false;
                    foreach (var item in ValidModuleItems)
                    {
                        hasModuleItems = true;
                        break;
                    }
                    if (hasModuleItems)
                    {
                        AttacheVehicleModules(modularCar, vehicle);
                    }
                    bool hasEngineItems = false;
                    foreach (var item in ValidEngineItems)
                    {
                        hasEngineItems = true;
                        break;
                    }
                    if (hasEngineItems)
                    {
                        Instance.NextTick(() =>
                        {
                            AddItemsToVehicleEngine(modularCar, vehicle);
                        });
                    }
                }
                base.SetupVehicle(entity, vehicle, player, justCreated);
            }

            #endregion Setup

            #endregion Spawn

            #region Recall

            public override void PreRecallVehicle(BasePlayer player, OwnedVehicle vehicle, Vector3 position, Quaternion rotation)
            {
                base.PreRecallVehicle(player, vehicle, position, rotation);

                if (vehicle.Entity is ModularCar)
                {
                    var modularCarGarages = Pool.Get<List<ModularCarGarage>>();
                    Vis.Entities(vehicle.Entity.transform.position, 3f, modularCarGarages, Layers.Mask.Deployed | Layers.Mask.Default);
                    ModularCarGarage modularCarGarage = null;
                    foreach (var garage in modularCarGarages)
                    {
                        if (garage.carOccupant == vehicle.Entity)
                        {
                            modularCarGarage = garage;
                            break;
                        }
                    }
                    Pool.FreeUnmanaged(ref modularCarGarages);
                    if (modularCarGarage != null)
                    {
                        modularCarGarage.enabled = false;
                        modularCarGarage.ReleaseOccupant();
                        modularCarGarage.Invoke(() => modularCarGarage.enabled = true, 0.25f);
                    }
                }
            }

            #region DropInventory

            protected override DroppedItemContainer DropVehicleInventory(BasePlayer player, OwnedVehicle vehicle)
            {
                var modularCar = vehicle.Entity as ModularCar;
                if (modularCar != null)
                {
                    foreach (var moduleEntity in modularCar.AttachedModuleEntities)
                    {
                        if (moduleEntity is VehicleModuleEngine)
                        {
                            continue;
                        }
                        var moduleStorage = moduleEntity as VehicleModuleStorage;
                        if (moduleStorage != null)
                        {
                            return moduleStorage.GetContainer()?.inventory?.Drop(PREFAB_ITEM_DROP, vehicle.Entity.GetDropPosition(), vehicle.Entity.transform.rotation, 0);
                        }
                    }
                }
                return null;
            }

            #endregion DropInventory

            #endregion Recall

            #region Refund

            private void GetRefundStatus(bool isCrash, bool isUnload, out bool refundFuel, out bool refundInventory, out bool refundEngine, out bool refundModule)
            {
                if (isUnload)
                {
                    refundFuel = refundInventory = refundEngine = refundModule = true;
                    return;
                }
                refundFuel = isCrash ? RefundFuelOnCrash : RefundFuelOnKill;
                refundInventory = isCrash ? RefundInventoryOnCrash : RefundInventoryOnKill;
                refundEngine = isCrash ? RefundEngineOnCrash : RefundEngineOnKill;
                refundModule = isCrash ? RefundModuleOnCrash : RefundModuleOnKill;
            }

            protected override void CollectVehicleItems(List<Item> items, OwnedVehicle vehicle, bool isCrash, bool isUnload)
            {
                var modularCar = vehicle.Entity as ModularCar;
                if (modularCar != null)
                {
                    bool refundFuel, refundInventory, refundEngine, refundModule;
                    GetRefundStatus(isCrash, isUnload, out refundFuel, out refundInventory, out refundEngine, out refundModule);

                    foreach (var moduleEntity in modularCar.AttachedModuleEntities)
                    {
                        if (refundEngine)
                        {
                            var moduleEngine = moduleEntity as VehicleModuleEngine;
                            if (moduleEngine != null)
                            {
                                var engineContainer = moduleEngine.GetContainer()?.inventory;
                                if (engineContainer != null)
                                {
                                    items.AddRange(engineContainer.itemList);
                                }
                                continue;
                            }
                        }
                        if (refundInventory)
                        {
                            var moduleStorage = moduleEntity as VehicleModuleStorage;
                            if (moduleStorage != null && !(moduleEntity is VehicleModuleEngine))
                            {
                                var storageContainer = moduleStorage.GetContainer()?.inventory;
                                if (storageContainer != null)
                                {
                                    items.AddRange(storageContainer.itemList);
                                }
                            }
                        }
                    }
                    if (refundFuel)
                    {
                        var fuelSystem = GetFuelSystem(modularCar);
                        if (fuelSystem is EntityFuelSystem entityFuelSystem)
                        {
                            var fuelContainer = entityFuelSystem.GetFuelContainer()?.inventory;
                            if (fuelContainer != null)
                            {
                                items.AddRange(fuelContainer.itemList);
                            }
                        }
                    }
                    if (refundModule)
                    {
                        var moduleContainer = modularCar.Inventory?.ModuleContainer;
                        if (moduleContainer != null)
                        {
                            items.AddRange(moduleContainer.itemList);
                        }
                    }
                }
            }

            #endregion Refund

            #region VehicleModules

            private void AttacheVehicleModules(ModularCar modularCar, OwnedVehicle vehicle)
            {
                foreach (var moduleItem in CreateModuleItems())
                {
                    if (!modularCar.TryAddModule(moduleItem))
                    {
                        Instance?.PrintError($"Module item '{moduleItem.info.shortname}' in '{vehicle.VehicleType}' cannot be attached to the vehicle");
                        moduleItem.Remove();
                    }
                }
            }
            private void AddItemsToVehicleEngine(ModularCar modularCar, OwnedVehicle vehicle)
            {
                if (modularCar == null || modularCar.IsDestroyed)
                {
                    return;
                }
                foreach (var moduleEntity in modularCar.AttachedModuleEntities)
                {
                    var vehicleModuleEngine = moduleEntity as VehicleModuleEngine;
                    if (vehicleModuleEngine != null)
                    {
                        var engineInventory = vehicleModuleEngine.GetContainer()?.inventory;
                        if (engineInventory != null)
                        {
                            foreach (var engineItem in CreateEngineItems())
                            {
                                var moved = false;
                                for (var i = 0; i < engineInventory.capacity; i++)
                                {
                                    if (engineItem.MoveToContainer(engineInventory, i, false))
                                    {
                                        moved = true;
                                        break;
                                    }
                                }
                                if (!moved)
                                {
                                    Instance?.PrintError($"Engine item '{engineItem.info.shortname}' in '{vehicle.VehicleType}' cannot be move to the vehicle engine inventory");
                                    engineItem.Remove();
                                    engineItem.DoRemove();
                                }
                            }
                        }
                    }
                }
            }

            #endregion VehicleModules
        }

        public class TrainVehicleSettings : FuelVehicleSettings, ITrainVehicle
        {
            #region Properties

            [JsonProperty(PropertyName = "Train Components", Order = 50)]
            public List<TrainComponent> TrainComponents { get; set; } = new List<TrainComponent>();

            #endregion Properties

            public override bool IsNormalVehicle => false;
            public override bool IsTrainVehicle => true;
            public override bool IsConnectableVehicle => true;

            protected override IFuelSystem GetFuelSystem(BaseEntity entity)
            {
                return (entity as TrainCar)?.GetFuelSystem();
            }

            protected override string GetVehiclePrefab(string vehicleType)
            {
                return TrainComponents.Count > 0 ? GetTrainVehiclePrefab(TrainComponents[0].type) : base.GetVehiclePrefab(vehicleType);
            }

            internal override string GetVehicleCustomPrefab(string vehicleType)
            {
                if (!configData.global.useCustomVehicles) return string.Empty;
                return TrainComponents.Count > 0 ? GetTrainVehiclePrefab(TrainComponents[0].type) : base.GetVehicleCustomPrefab(vehicleType);
            }

            #region Spawn

            private static string GetTrainVehiclePrefab(string componentType)
            {
                switch (componentType)
                {
                    case "Engine":
                        return PREFAB_TRAINENGINE;
                    case "CoveredEngine":
                        return PREFAB_TRAINENGINE_COVERED;
                    case "Locomotive":
                        return PREFAB_TRAINENGINE_LOCOMOTIVE;
                    case "WagonA":
                        return PREFAB_TRAINWAGON_A;
                    case "WagonB":
                        return PREFAB_TRAINWAGON_B;
                    case "WagonC":
                        return PREFAB_TRAINWAGON_C;
                    case "Unloadable":
                        return PREFAB_TRAINWAGON_UNLOADABLE;
                    case "UnloadableLoot":
                        return PREFAB_TRAINWAGON_UNLOADABLE_LOOT;
                    case "UnloadableFuel":
                        return PREFAB_TRAINWAGON_UNLOADABLE_FUEL;
                    case "Caboose":
                        return PREFAB_CABOOSE;
                    default:
                        return null;
                }
            }

            public override BaseEntity SpawnVehicle(BasePlayer player, OwnedVehicle vehicle, Vector3 position, Quaternion rotation)
            {
                TrainCar prevTrainCar = null, primaryTrainCar = null;
                foreach (var component in TrainComponents)
                {
                    var prefab = GetTrainVehiclePrefab(component.type);
                    if (string.IsNullOrEmpty(prefab))
                    {
                        throw new ArgumentException($"Prefab not found for {vehicle.VehicleType}({component.type})");
                    }
                    float distResult;
                    TrainTrackSpline splineResult;
                    if (prevTrainCar == null)
                    {
                        if (TrainTrackSpline.TryFindTrackNear(position, 20f, out splineResult, out distResult))
                        {
                            position = splineResult.GetPosition(distResult);
                            prevTrainCar = GameManager.server.CreateEntity(prefab, position, rotation) as TrainCar;
                            if (prevTrainCar == null)
                            {
                                continue;
                            }
                            PreSetupVehicle(prevTrainCar, vehicle, player);
                            prevTrainCar.Spawn();
                            prevTrainCar.CancelInvoke(prevTrainCar.KillMessage);
                            SetupVehicle(prevTrainCar, vehicle, player);
                        }
                    }
                    else
                    {
                        var newTrainCar = GameManager.server.CreateEntity(prefab, prevTrainCar.transform.position, prevTrainCar.transform.rotation) as TrainCar;
                        if (newTrainCar == null)
                        {
                            continue;
                        }

                        position += prevTrainCar.transform.rotation * (newTrainCar.bounds.center - Vector3.forward * (newTrainCar.bounds.extents.z + prevTrainCar.bounds.extents.z));
                        if (TrainTrackSpline.TryFindTrackNear(position, 20f, out splineResult, out distResult))
                        {
                            position = splineResult.GetPosition(distResult);
                            newTrainCar.transform.position = position;

                            PreSetupVehicle(newTrainCar, vehicle, player);
                            newTrainCar.Spawn();
                            newTrainCar.CancelInvoke(newTrainCar.KillMessage);
                            SetupVehicle(newTrainCar, vehicle, player);

                            float minSplineDist;
                            var distance = prevTrainCar.RearTrackSection.GetDistance(position, 1f, out minSplineDist);
                            var preferredAltTrack = prevTrainCar.RearTrackSection != prevTrainCar.FrontTrackSection ? prevTrainCar.RearTrackSection : null;
                            newTrainCar.MoveFrontWheelsAlongTrackSpline(prevTrainCar.RearTrackSection, minSplineDist, distance, preferredAltTrack, TrainTrackSpline.TrackSelection.Default);

                            newTrainCar.coupling.frontCoupling.TryCouple(prevTrainCar.coupling.rearCoupling, true);
                            prevTrainCar = newTrainCar;
                        }
                    }
                    if (primaryTrainCar == null)
                    {
                        primaryTrainCar = prevTrainCar;
                    }
                }
                if (primaryTrainCar == null || primaryTrainCar.IsDestroyed)
                {
                    Instance.Print(player, Instance.Lang("NotSpawnedOrRecalled", player.UserIDString, DisplayName));
                    return null;
                }
                Instance.CacheVehicleEntity(primaryTrainCar, vehicle, player);
                return primaryTrainCar;
            }

            #endregion Spawn

            #region Recall

            public override void PreRecallVehicle(BasePlayer player, OwnedVehicle vehicle, Vector3 position, Quaternion rotation)
            {
                base.PreRecallVehicle(player, vehicle, position, rotation);
                var trainCar = vehicle.Entity as TrainCar;
                if (trainCar != null)
                {
                    trainCar.coupling.Uncouple(true);
                    trainCar.coupling.Uncouple(false);
                }
            }

            public override void PostRecallVehicle(BasePlayer player, OwnedVehicle vehicle, Vector3 position, Quaternion rotation)
            {
                base.PostRecallVehicle(player, vehicle, position, rotation);
                var trainCar = vehicle.Entity as TrainCar;
                if (trainCar != null)
                {
                    TryMoveToTrainTrackNear(trainCar);
                }
            }

            #endregion Recall

            #region Refund
            protected override void CollectVehicleItems(List<Item> items, OwnedVehicle vehicle, bool isCrash, bool isUnload)
            {
                if (!CanRefundFuel(isCrash, isUnload)) return;

                var trainCar = vehicle.Entity as TrainCar;

                if (trainCar == null) return;
                var fuelSystem = GetFuelSystem(trainCar);

                if (fuelSystem is EntityFuelSystem entityFuelSystem)
                {
                    var fuelContainer = entityFuelSystem.GetFuelContainer()?.inventory;

                    if (fuelContainer != null)
                    {
                        items.AddRange(fuelContainer.itemList);
                    }
                }
            }

            #endregion Refund

            #region TryGetVehicleParams

            protected override bool TryGetPositionAndRotation(BasePlayer player, OwnedVehicle vehicle, out string reason, out Vector3 original, out Quaternion rotation)
            {
                if (!base.TryGetPositionAndRotation(player, vehicle, out reason, out original, out rotation)) return true;

                return TryGetTrainCarPositionAndRotation(player, vehicle, ref reason, ref original, ref rotation);
            }


            #endregion TryGetVehicleParams

            #endregion VehicleSettings
        }

        protected override void LoadConfig()
        {
                base.LoadConfig();
                try
                {
                    configData = Config.ReadObject<PluginConfiguration>();
                    if (configData == null)
                    {
                        LoadDefaultConfig();
                    }
                    else
                    {
                        if (configData.version == default(VersionNumber))
                        {
                            configData.version = Version;
                        }
                    }
                }
                catch (Exception ex)
                {
                    PrintError($"The configuration file is corrupted. \n{ex}");
                    LoadDefaultConfig();
                }
                SaveConfig();
            }

            protected override void LoadDefaultConfig()
            {
                PrintWarning("Creating a new configuration file");
                configData = new PluginConfiguration();
                configData.version = Version;
            }

            protected override void SaveConfig()
            {
                Config.WriteObject(configData);
            }

            #endregion ConfigurationFile

        #region Localization

            private void Print(BasePlayer player, string message)
            {
                Player.Message(player, message, configData.chat.prefix, configData.chat.steamIDIcon);
            }

            private void Print(ConsoleSystem.Arg arg, string message)
            {
                var player = arg.Player();
                if (player == null)
                {
                    Puts(message);
                }
                else
                {
                    PrintToConsole(player, message);
                }
            }

            private string Lang(string key, string id = null, params object[] args)
            {
                try
                {
                    return string.Format(lang.GetMessage(key, this, id), args);
                }
                catch (Exception)
                {
                    PrintError($"Error in the language formatting of '{key}'. (userid: {id}. lang: {lang.GetLanguage(id)}. args: {string.Join(" ,", args)})");
                    throw;
                }
            }

            protected override void LoadDefaultMessages()
            {
                lang.RegisterMessages(new Dictionary<string, string>
                {
                    ["Help"] = "These are the available commands:",
                    ["Help1"] = "<color=#4DFF4D>/{0}</color> -- To buy a vehicle",
                    ["Help2"] = "<color=#4DFF4D>/{0}</color> -- To spawn a vehicle",
                    ["Help3"] = "<color=#4DFF4D>/{0}</color> -- To recall a vehicle",
                    ["Help4"] = "<color=#4DFF4D>/{0}</color> -- To kill a vehicle",
                    ["Help5"] = "<color=#4DFF4D>/{0}</color> -- To buy, spawn or recall a <color=#009EFF>{1}</color>",

                    ["PriceFormat"] = "<color=#FF1919>{0}</color> x{1}",
                    ["HelpBuy"] = "<color=#4DFF4D>/{0} {1}</color> -- To buy a <color=#009EFF>{2}</color>",
                    ["HelpBuyPrice"] = "<color=#4DFF4D>/{0} {1}</color> -- To buy a <color=#009EFF>{2}</color>. Price: {3}",
                    ["HelpSpawn"] = "<color=#4DFF4D>/{0} {1}</color> -- To spawn a <color=#009EFF>{2}</color>",
                    ["HelpSpawnPrice"] = "<color=#4DFF4D>/{0} {1}</color> -- To spawn a <color=#009EFF>{2}</color>. Price: {3}",
                    ["HelpRecall"] = "<color=#4DFF4D>/{0} {1}</color> -- To recall a <color=#009EFF>{2}</color>",
                    ["HelpRecallPrice"] = "<color=#4DFF4D>/{0} {1}</color> -- To recall a <color=#009EFF>{2}</color>. Price: {3}",
                    ["HelpKill"] = "<color=#4DFF4D>/{0} {1}</color> -- To kill a <color=#009EFF>{2}</color>",
                    ["HelpKillCustom"] = "<color=#4DFF4D>/{0} {1}</color> or <color=#4DFF4D>/{2}</color>  -- To kill a <color=#009EFF>{3}</color>",

                    ["NotAllowed"] = "You do not have permission to use this command.",
                    ["PleaseWait"] = "Please wait a little bit before using this command.",
                    ["RaidBlocked"] = "<color=#FF1919>You may not do that while raid blocked</color>.",
                    ["CombatBlocked"] = "<color=#FF1919>You may not do that while combat blocked</color>.",
                    ["OptionNotFound"] = "This <color=#009EFF>{0}</color> option doesn't exist.",
                    ["VehiclePurchased"] = "You have purchased a <color=#009EFF>{0}</color>, type <color=#4DFF4D>/{1}</color> for more information.",
                    ["VehicleAlreadyPurchased"] = "You have already purchased <color=#009EFF>{0}</color>.",
                    ["VehicleCannotBeBought"] = "<color=#009EFF>{0}</color> is unpurchasable",
                    ["VehicleNotOut"] = "<color=#009EFF>{0}</color> is not out, type <color=#4DFF4D>/{1}</color> for more information.",
                    ["AlreadyVehicleOut"] = "You already have a <color=#009EFF>{0}</color> outside, type <color=#4DFF4D>/{1}</color> for more information.",
                    ["VehicleNotYetPurchased"] = "You have not yet purchased a <color=#009EFF>{0}</color>, type <color=#4DFF4D>/{1}</color> for more information.",
                    ["VehicleSpawned"] = "You spawned your <color=#009EFF>{0}</color>.",
                    ["VehicleRecalled"] = "You recalled your <color=#009EFF>{0}</color>.",
                    ["VehicleKilled"] = "You killed your <color=#009EFF>{0}</color>.",
                    ["VehicleOnSpawnCooldown"] = "You must wait <color=#FF1919>{0}</color> seconds before you can spawn your <color=#009EFF>{1}</color>.",
                    ["VehicleOnRecallCooldown"] = "You must wait <color=#FF1919>{0}</color> seconds before you can recall your <color=#009EFF>{1}</color>.",
                    ["VehicleOnSpawnCooldownPay"] = "You must wait <color=#FF1919>{0}</color> seconds before you can spawn your <color=#009EFF>{1}</color>. You can bypass this cooldown by using the <color=#FF1919>/{2}</color> command to pay <color=#009EFF>{3}</color>",
                    ["VehicleOnRecallCooldownPay"] = "You must wait <color=#FF1919>{0}</color> seconds before you can recall your <color=#009EFF>{1}</color>. You can bypass this cooldown by using the <color=#FF1919>/{2}</color> command to pay <color=#009EFF>{3}</color>",
                    ["NotLookingAtWater"] = "You must be looking at water to spawn or recall a <color=#009EFF>{0}</color>.",
                    ["BuildingBlocked"] = "You can't spawn a <color=#009EFF>{0}</color> if you don't have the building privileges.",
                    ["RefundedVehicleItems"] = "Your <color=#009EFF>{0}</color> vehicle items was refunded to your inventory.",
                    ["PlayerMountedOnVehicle"] = "It cannot be recalled or killed when players mounted on your <color=#009EFF>{0}</color>.",
                    ["PlayerInSafeZone"] = "You cannot spawn or recall your <color=#009EFF>{0}</color> in the safe zone.",
                    ["VehicleInventoryDropped"] = "Your <color=#009EFF>{0}</color> vehicle inventory cannot be recalled, it have dropped to the ground.",
                    ["NoResourcesToPurchaseVehicle"] = "You don't have enough resources to buy a <color=#009EFF>{0}</color>. You are missing: \n{1}",
                    ["NoResourcesToSpawnVehicle"] = "You don't have enough resources to spawn a <color=#009EFF>{0}</color>. You are missing: \n{1}",
                    ["NoResourcesToSpawnVehicleBypass"] = "You don't have enough resources to bypass the cooldown to spawn a <color=#009EFF>{0}</color>. You are missing: \n{1}",
                    ["NoResourcesToRecallVehicle"] = "You don't have enough resources to recall a <color=#009EFF>{0}</color>. You are missing: \n{1}",
                    ["NoResourcesToRecallVehicleBypass"] = "You don't have enough resources to bypass the cooldown to recall a <color=#009EFF>{0}</color>. You are missing: \n{1}",
                    ["MountedOrParented"] = "You cannot spawn or recall a <color=#009EFF>{0}</color> when mounted or parented.",
                    ["RecallTooFar"] = "You must be within <color=#FF1919>{0}</color> meters of <color=#009EFF>{1}</color> to recall.",
                    ["KillTooFar"] = "You must be within <color=#FF1919>{0}</color> meters of <color=#009EFF>{1}</color> to kill.",
                    ["PlayersOnNearby"] = "You cannot spawn or recall a <color=#009EFF>{0}</color> when there are players near the position you are looking at.",
                    ["RecallWasBlocked"] = "An external plugin blocked you from recalling a <color=#009EFF>{0}</color>.",
                    ["NoRecallInZone"] = "No recalling a <color=#009EFF>{0}</color> in the zone.",
                    ["NoSpawnInZone"] = "No spawning a <color=#009EFF>{0}</color> in the zone.",
                    ["NoSpawnInAir"] = "No spawning a <color=#009EFF>{0}</color> in the air.",
                    ["SpawnWasBlocked"] = "An external plugin blocked you from spawning a <color=#009EFF>{0}</color>.",
                    ["VehiclesLimit"] = "You can have up to <color=#009EFF>{0}</color> vehicles at a time.",
                    ["TooFarTrainTrack"] = "You are too far from the train track.",
                    ["TooCloseTrainBarricadeOrWorkCart"] = "You are too close to the train barricade or work cart.",
                    ["NotSpawnedOrRecalled"] = "For some reason, your <color=#009EFF>{0}</color> vehicle was not spawned/recalled",

                    ["CantUse"] = "Sorry! This {0} belongs to {1}. You cannot use it.",
                    ["CantPush"] = "Sorry! This {0} belongs to {1}. You cannot push it.",
                }, this);
                lang.RegisterMessages(new Dictionary<string, string>
                {
                    ["Help"] = "可用命令列表:",
                    ["Help1"] = "<color=#4DFF4D>/{0}</color> -- 购买一辆载具",
                    ["Help2"] = "<color=#4DFF4D>/{0}</color> -- 生成一辆载具",
                    ["Help3"] = "<color=#4DFF4D>/{0}</color> -- 召回一辆载具",
                    ["Help4"] = "<color=#4DFF4D>/{0}</color> -- 摧毁一辆载具",
                    ["Help5"] = "<color=#4DFF4D>/{0}</color> -- 购买，生成，召回一辆 <color=#009EFF>{1}</color>",

                    ["PriceFormat"] = "<color=#FF1919>{0}</color> x{1}",
                    ["HelpBuy"] = "<color=#4DFF4D>/{0} {1}</color> -- 购买一辆 <color=#009EFF>{2}</color>",
                    ["HelpBuyPrice"] = "<color=#4DFF4D>/{0} {1}</color> -- 购买一辆 <color=#009EFF>{2}</color>，价格: {3}",
                    ["HelpSpawn"] = "<color=#4DFF4D>/{0} {1}</color> -- 生成一辆 <color=#009EFF>{2}</color>",
                    ["HelpSpawnPrice"] = "<color=#4DFF4D>/{0} {1}</color> -- 生成一辆 <color=#009EFF>{2}</color>，价格: {3}",
                    ["HelpRecall"] = "<color=#4DFF4D>/{0} {1}</color> -- 召回一辆 <color=#009EFF>{2}</color>",
                    ["HelpRecallPrice"] = "<color=#4DFF4D>/{0} {1}</color> -- 召回一辆 <color=#009EFF>{2}</color>，价格: {3}",
                    ["HelpKill"] = "<color=#4DFF4D>/{0} {1}</color> -- 摧毁一辆 <color=#009EFF>{2}</color>",
                    ["HelpKillCustom"] = "<color=#4DFF4D>/{0} {1}</color> 或者 <color=#4DFF4D>/{2}</color>  -- 摧毁一辆 <color=#009EFF>{3}</color>",

                    ["NotAllowed"] = "您没有权限使用该命令",
                    ["PleaseWait"] = "使用此命令之前请稍等一下",
                    ["RaidBlocked"] = "<color=#FF1919>您被突袭阻止了，不能使用该命令</color>",
                    ["CombatBlocked"] = "<color=#FF1919>您被战斗阻止了，不能使用该命令</color>",
                    ["OptionNotFound"] = "选项 <color=#009EFF>{0}</color> 不存在",
                    ["VehiclePurchased"] = "您购买了 <color=#009EFF>{0}</color>, 输入 <color=#4DFF4D>/{1}</color> 了解更多信息",
                    ["VehicleAlreadyPurchased"] = "您已经购买了 <color=#009EFF>{0}</color>",
                    ["VehicleCannotBeBought"] = "<color=#009EFF>{0}</color> 是不可购买的",
                    ["VehicleNotOut"] = "您还没有生成您的 <color=#009EFF>{0}</color>, 输入 <color=#4DFF4D>/{1}</color> 了解更多信息",
                    ["AlreadyVehicleOut"] = "您已经生成了您的 <color=#009EFF>{0}</color>, 输入 <color=#4DFF4D>/{1}</color> 了解更多信息",
                    ["VehicleNotYetPurchased"] = "您还没有购买 <color=#009EFF>{0}</color>, 输入 <color=#4DFF4D>/{1}</color> 了解更多信息",
                    ["VehicleSpawned"] = "您生成了您的 <color=#009EFF>{0}</color>",
                    ["VehicleRecalled"] = "您召回了您的 <color=#009EFF>{0}</color>",
                    ["VehicleKilled"] = "您摧毁了您的 <color=#009EFF>{0}</color>",
                    ["VehicleOnSpawnCooldown"] = "您必须等待 <color=#FF1919>{0}</color> 秒，才能生成您的 <color=#009EFF>{1}</color>",
                    ["VehicleOnRecallCooldown"] = "您必须等待 <color=#FF1919>{0}</color> 秒，才能召回您的 <color=#009EFF>{1}</color>",
                    ["VehicleOnSpawnCooldownPay"] = "您必须等待 <color=#FF1919>{0}</color> 秒，才能生成您的 <color=#009EFF>{1}</color>。你可以使用 <color=#FF1919>/{2}</color> 命令支付 <color=#009EFF>{3}</color> 来绕过这个冷却时间",
                    ["VehicleOnRecallCooldownPay"] = "您必须等待 <color=#FF1919>{0}</color> 秒，才能召回您的 <color=#009EFF>{1}</color>。你可以使用 <color=#FF1919>/{2}</color> 命令支付 <color=#009EFF>{3}</color> 来绕过这个冷却时间",
                    ["NotLookingAtWater"] = "您必须看着水面才能生成您的 <color=#009EFF>{0}</color>",
                    ["BuildingBlocked"] = "您没有领地柜权限，无法生成您的 <color=#009EFF>{0}</color>",
                    ["RefundedVehicleItems"] = "您的 <color=#009EFF>{0}</color> 载具物品已经归还回您的库存",
                    ["PlayerMountedOnVehicle"] = "您的 <color=#009EFF>{0}</color> 上坐着玩家，无法被召回或摧毁",
                    ["PlayerInSafeZone"] = "您不能在安全区域内生成或召回您的 <color=#009EFF>{0}</color>",
                    ["VehicleInventoryDropped"] = "您的 <color=#009EFF>{0}</color> 载具物品不能召回，它已经掉落在地上了",
                    ["NoResourcesToPurchaseVehicle"] = "您没有足够的资源购买 <color=#009EFF>{0}</color>，还需要: \n{1}",
                    ["NoResourcesToSpawnVehicle"] = "您没有足够的资源生成 <color=#009EFF>{0}</color>，还需要: \n{1}",
                    ["NoResourcesToSpawnVehicleBypass"] = "您没有足够的资源绕过冷却时间来生成 <color=#009EFF>{0}</color>，还需要: \n{1}",
                    ["NoResourcesToRecallVehicle"] = "您没有足够的资源召回 <color=#009EFF>{0}</color>，还需要: \n{1}",
                    ["NoResourcesToRecallVehicleBypass"] = "您没有足够的资源绕过冷却时间来召回 <color=#009EFF>{0}</color>，还需要: \n{1}",
                    ["MountedOrParented"] = "当您坐着或者在附着在实体上时无法生成或召回 <color=#009EFF>{0}</color>",
                    ["RecallTooFar"] = "您必须在 <color=#FF1919>{0}</color> 米内才能召回您的 <color=#009EFF>{1}</color>",
                    ["KillTooFar"] = "您必须在 <color=#FF1919>{0}</color> 米内才能摧毁您的 <color=#009EFF>{1}</color>",
                    ["PlayersOnNearby"] = "您正在看着的位置附近有玩家时无法生成或召回 <color=#009EFF>{0}</color>",
                    ["RecallWasBlocked"] = "有其他插件阻止您召回 <color=#009EFF>{0}</color>.",
                    ["NoRecallInZone"] = "不召回该区域中的<color=#009EFF>{0}</color>.",
                    ["NoSpawnInZone"] = "不会在该区域生成 <color=#009EFF>{0}</color>.",
                    ["NoSpawnInAir"] = "在空中时不会生成 <color=#009EFF>{0}</color>.",
                    ["SpawnWasBlocked"] = "有其他插件阻止您生成 <color=#009EFF>{0}</color>.",
                    ["VehiclesLimit"] = "您在同一时间内最多可以拥有 <color=#009EFF>{0}</color> 辆载具",
                    ["TooFarTrainTrack"] = "您距离铁路轨道太远了",
                    ["TooCloseTrainBarricadeOrWorkCart"] = "您距离铁轨障碍物或其它火车太近了",
                    ["NotSpawnedOrRecalled"] = "由于某些原因，您的 <color=#009EFF>{0}</color> 载具无法生成或召回",

                    ["CantUse"] = "您不能使用它，这个 {0} 属于 {1}",
                    ["CantPush"] = "您无法推送此内容，它 {0} 属于 {1}.",
                }, this, "zh-CN");
                lang.RegisterMessages(new Dictionary<string, string>
                {
                    ["Help"] = "Список доступных команд:",
                    ["Help1"] = "<color=#4DFF4D>/{0}</color> -- Купить транспорт",
                    ["Help2"] = "<color=#4DFF4D>/{0}</color> -- Создать транспорт",
                    ["Help3"] = "<color=#4DFF4D>/{0}</color> -- Вызвать транспорт",
                    ["Help4"] = "<color=#4DFF4D>/{0}</color> -- Уничтожить транспорт",
                    ["Help5"] = "<color=#4DFF4D>/{0}</color> -- Купить, создать, или вызвать <color=#009EFF>{1}</color>",

                    ["PriceFormat"] = "<color=#FF1919>{0}</color> x{1}",
                    ["HelpBuy"] = "<color=#4DFF4D>/{0} {1}</color> -- Купить <color=#009EFF>{2}</color>.",
                    ["HelpBuyPrice"] = "<color=#4DFF4D>/{0} {1}</color> -- Купить <color=#009EFF>{2}</color>. Цена: {3}",
                    ["HelpSpawn"] = "<color=#4DFF4D>/{0} {1}</color> -- Создать <color=#009EFF>{2}</color>",
                    ["HelpSpawnPrice"] = "<color=#4DFF4D>/{0} {1}</color> -- Вызывать <color=#009EFF>{2}</color>. Цена: {3}",
                    ["HelpRecall"] = "<color=#4DFF4D>/{0} {1}</color> -- Вызвать <color=#009EFF>{2}</color>",
                    ["HelpRecallPrice"] = "<color=#4DFF4D>/{0} {1}</color> -- Вызвать <color=#009EFF>{2}</color>. Цена: {3}",
                    ["HelpKill"] = "<color=#4DFF4D>/{0} {1}</color> -- Уничтожить <color=#009EFF>{2}</color>",
                    ["HelpKillCustom"] = "<color=#4DFF4D>/{0} {1}</color> или же <color=#4DFF4D>/{2}</color>  -- Уничтожить <color=#009EFF>{3}</color>",

                    ["NotAllowed"] = "У вас нет разрешения для использования данной команды.",
                    ["PleaseWait"] = "Пожалуйста, подождите немного, прежде чем использовать эту команду.",
                    ["RaidBlocked"] = "<color=#FF1919>Вы не можете это сделать из-за блокировки (рейд)</color>.",
                    ["CombatBlocked"] = "<color=#FF1919>Вы не можете это сделать из-за блокировки (бой)</color>.",
                    ["OptionNotFound"] = "Опция <color=#009EFF>{0}</color> не существует.",
                    ["VehiclePurchased"] = "Вы приобрели <color=#009EFF>{0}</color>, напишите <color=#4DFF4D>/{1}</color> для получения дополнительной информации.",
                    ["VehicleAlreadyPurchased"] = "Вы уже приобрели <color=#009EFF>{0}</color>.",
                    ["VehicleCannotBeBought"] = "<color=#009EFF>{0}</color> приобрести невозможно",
                    ["VehicleNotOut"] = "<color=#009EFF>{0}</color> отсутствует. Напишите <color=#4DFF4D>/{1}</color> для получения дополнительной информации.",
                    ["AlreadyVehicleOut"] = "У вас уже есть <color=#009EFF>{0}</color>, напишите <color=#4DFF4D>/{1}</color>  для получения дополнительной информации.",
                    ["VehicleNotYetPurchased"] = "Вы ещё не приобрели <color=#009EFF>{0}</color>. Напишите <color=#4DFF4D>/{1}</color> для получения дополнительной информации.",
                    ["VehicleSpawned"] = "Вы создали ваш <color=#009EFF>{0}</color>.",
                    ["VehicleRecalled"] = "Вы вызвали ваш <color=#009EFF>{0}</color>.",
                    ["VehicleKilled"] = "Вы уничтожили ваш <color=#009EFF>{0}</color>.",
                    ["VehicleOnSpawnCooldown"] = "Вам необходимо подождать <color=#FF1919>{0}</color> секунд прежде, чем создать свой <color=#009EFF>{1}</color>.",
                    ["VehicleOnRecallCooldown"] = "Вам необходимо подождать <color=#FF1919>{0}</color> секунд прежде, чем вызвать свой <color=#009EFF>{1}</color>.",
                    ["VehicleOnSpawnCooldownPay"] = "Вам необходимо подождать <color=#FF1919>{0}</color> секунд прежде, чем создать свой <color=#009EFF>{1}</color>. Вы можете обойти это время восстановления, используя команду <color=#FF1919>/{2}</color>, чтобы заплатить <color=#009EFF>{3}</color>",
                    ["VehicleOnRecallCooldownPay"] = "Вам необходимо подождать <color=#FF1919>{0}</color> секунд прежде, чем вызвать свой <color=#009EFF>{1}</color>. Вы можете обойти это время восстановления, используя команду <color=#FF1919>/{2}</color>, чтобы заплатить <color=#009EFF>{3}</color>",
                    ["NotLookingAtWater"] = "Вы должны смотреть на воду, чтобы создать или вызвать <color=#009EFF>{0}</color>.",
                    ["BuildingBlocked"] = "Вы не можете создать <color=#009EFF>{0}</color> если отсутствует право строительства.",
                    ["RefundedVehicleItems"] = "Запчасти от вашего <color=#009EFF>{0}</color> были возвращены в ваш инвентарь.",
                    ["PlayerMountedOnVehicle"] = "Нельзя вызвать, когда игрок находится в вашем <color=#009EFF>{0}</color>.",
                    ["PlayerInSafeZone"] = "Вы не можете создать, или вызвать ваш <color=#009EFF>{0}</color> в безопасной зоне.",
                    ["VehicleInventoryDropped"] = "Инвентарь из вашего <color=#009EFF>{0}</color> не может быть вызван, он выброшен на землю.",
                    ["NoResourcesToPurchaseVehicle"] = "У вас недостаточно ресурсов для покупки <color=#009EFF>{0}</color>. Вам не хватает: \n{1}",
                    ["NoResourcesToSpawnVehicle"] = "У вас недостаточно ресурсов для покупки <color=#009EFF>{0}</color>. Вам не хватает: \n{1}",
                    ["NoResourcesToSpawnVehicleBypass"] = "У вас недостаточно ресурсов для покупки <color=#009EFF>{0}</color>. Вам не хватает: \n{1}",
                    ["NoResourcesToRecallVehicle"] = "У вас недостаточно ресурсов для покупки <color=#009EFF>{0}</color>. Вам не хватает: \n{1}",
                    ["NoResourcesToRecallVehicleBypass"] = "У вас недостаточно ресурсов для покупки <color=#009EFF>{0}</color>. Вам не хватает: \n{1}",
                    ["MountedOrParented"] = "Вы не можете создать <color=#009EFF>{0}</color> когда сидите или привязаны к объекту.",
                    ["RecallTooFar"] = "Вы должны быть в пределах <color=#FF1919>{0}</color> метров от <color=#009EFF>{1}</color>, чтобы вызывать.",
                    ["KillTooFar"] = "Вы должны быть в пределах <color=#FF1919>{0}</color> метров от <color=#009EFF>{1}</color>, уничтожить.",
                    ["PlayersOnNearby"] = "Вы не можете создать <color=#009EFF>{0}</color> когда рядом с той позицией, на которую вы смотрите, есть игроки.",
                    ["RecallWasBlocked"] = "Внешний плагин заблокировал вам вызвать <color=#009EFF>{0}</color>.",
                    ["NoRecallInZone"] = "Нет отзыва <color=#009EFF>{0}</color> в зоне.",
                    ["NoSpawnInZone"] = "В зоне не создается <color=#009EFF>{0}</color>.",
                    ["NoSpawnInAir"] = "Не создавать <color=#009EFF>{0}</color> в воздухе.",
                    ["SpawnWasBlocked"] = "Внешний плагин заблокировал вам создать <color=#009EFF>{0}</color>.",
                    ["VehiclesLimit"] = "У вас может быть до <color=#009EFF>{0}</color> автомобилей одновременно",
                    ["TooFarTrainTrack"] = "Вы слишком далеко от железнодорожных путей",
                    ["TooCloseTrainBarricadeOrWorkCart"] = "Вы слишком близко к железнодорожной баррикаде или рабочей тележке",
                    ["NotSpawnedOrRecalled"] = "По какой-то причине ваш <color=#009EFF>{0}</color>  автомобилей не был вызван / отозван",

                    ["CantUse"] = "Простите! Этот {0} принадлежит {1}. Вы не можете его использовать.",
                    ["CantPush"] = "Простите! Этот {0} принадлежит {1}. Вы не можете его подтолкнуть.",
                }, this, "ru");
            }

            #endregion LanguageFile

        #region Stored Data

            public VehicleDatabase vehicleDatabase { get; private set; }

            public class VehicleDatabase
            {
                public readonly Dictionary<ulong, Dictionary<string, OwnedVehicle>> playerData = new Dictionary<ulong, Dictionary<string, OwnedVehicle>>();

                public IEnumerable<BaseEntity> ActiveVehicles(ulong playerId)
                {
                    Dictionary<string, OwnedVehicle> vehicles;
                    if (!playerData.TryGetValue(playerId, out vehicles))
                    {
                        yield break;
                    }

                    foreach (var vehicle in vehicles.Values)
                    {
                        if (vehicle.Entity != null && !vehicle.Entity.IsDestroyed)
                        {
                            yield return vehicle.Entity;
                        }
                    }
                }

                public Dictionary<string, OwnedVehicle> GetPlayerVehicles(ulong playerId, bool readOnly = true)
                {
                    Dictionary<string, OwnedVehicle> vehicles;
                    if (!playerData.TryGetValue(playerId, out vehicles))
                    {
                        if (!readOnly)
                        {
                            vehicles = new Dictionary<string, OwnedVehicle>();
                            playerData.Add(playerId, vehicles);
                            return vehicles;
                        }
                        return null;
                    }
                    return vehicles;
                }

                public bool IsVehiclePurchased(ulong playerId, string vehicleType, out OwnedVehicle vehicle)
                {
                    vehicle = GetVehicleLicense(playerId, vehicleType);
                    if (vehicle == null)
                    {
                        return false;
                    }
                    return true;
                }

                public OwnedVehicle GetVehicleLicense(ulong playerId, string vehicleType)
                {
                    Dictionary<string, OwnedVehicle> vehicles;
                    if (!playerData.TryGetValue(playerId, out vehicles))
                    {
                        return null;
                    }
                    OwnedVehicle vehicle;
                    if (!vehicles.TryGetValue(vehicleType, out vehicle))
                    {
                        return null;
                    }
                    return vehicle;
                }

                public bool HasVehicleLicense(ulong playerId, string vehicleType)
                {
                    Dictionary<string, OwnedVehicle> vehicles;
                    if (!playerData.TryGetValue(playerId, out vehicles))
                    {
                        return false;
                    }
                    return vehicles.ContainsKey(vehicleType);
                }

                public bool AddVehicleLicense(ulong playerId, string vehicleType)
                {
                    Dictionary<string, OwnedVehicle> vehicles;
                    if (!playerData.TryGetValue(playerId, out vehicles))
                    {
                        vehicles = new Dictionary<string, OwnedVehicle>();
                        playerData.Add(playerId, vehicles);
                    }
                    if (vehicles.ContainsKey(vehicleType))
                    {
                        return false;
                    }
                    vehicles.Add(vehicleType, OwnedVehicle.New(playerId, vehicleType));
                    Instance.SaveData();
                    return true;
                }
                public bool RemoveVehicleLicense(ulong playerId, string vehicleType)
                {
                    Dictionary<string, OwnedVehicle> vehicles;
                    if (!playerData.TryGetValue(playerId, out vehicles))
                    {
                        return false;
                    }

                    if (!vehicles.Remove(vehicleType))
                    {
                        return false;
                    }
                    Instance.SaveData();
                    Interface.CallHook("OnLicensedVehicleRemoved", playerId, vehicleType);
                    return true;
                }

                [HookMethod(nameof(GetVehicleLicenseNames))]
                public List<string> GetVehicleLicenseNames(ulong playerId)
                {
                    if (playerData?.TryGetValue(playerId, out var vehicles) == true)
                    {
                        Instance.Puts($"Found {vehicles.Count} vehicle licenses for player {playerId}");
                        var keysList = new List<string>(vehicles.Keys);
                        return keysList;
                    }
                    Instance.Puts($"No vehicle licenses found for player {playerId}");
                    return new List<string>();
                }

                public void PurchaseAllVehicles(ulong playerId)
                {
                    var changed = false;
                    Dictionary<string, OwnedVehicle> vehicles;
                    if (!playerData.TryGetValue(playerId, out vehicles))
                    {
                        vehicles = new Dictionary<string, OwnedVehicle>();
                        playerData.Add(playerId, vehicles);
                    }
                    foreach (var vehicleType in Instance.allVehicleSettings.Keys)
                    {
                        if (!vehicles.ContainsKey(vehicleType))
                        {
                            vehicles.Add(vehicleType, OwnedVehicle.New(playerId, vehicleType));
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        Instance.SaveData();
                    }
                }

                public void AddLicenseForAllPlayers(string vehicleType)
                {
                    foreach (var entry in playerData)
                    {
                        if (!entry.Value.ContainsKey(vehicleType))
                        {
                            entry.Value.Add(vehicleType, OwnedVehicle.New(entry.Key, vehicleType));
                        }
                    }
                }

                public void RemoveLicenseForAllPlayers(string vehicleType)
                {
                    foreach (var entry in playerData)
                    {
                        entry.Value.Remove(vehicleType);
                    }
                }

                public void ResetPlayerData()
                {
                    foreach (var vehicleEntries in playerData)
                    {
                        foreach (var vehicleEntry in vehicleEntries.Value)
                        {
                            vehicleEntry.Value.ClearData();
                        }
                    }
                }
            }

        [JsonObject(MemberSerialization.OptIn)]
        public class OwnedVehicle
        {
            [JsonProperty("entityID")]
            public ulong EntityId { get; set; }

            [JsonProperty("lastDeath")]
            public double LastDeath { get; set; }

            public ulong PlayerId { get; set; }
            public BaseEntity Entity { get; set; }
            public string VehicleType { get; set; }

            
            public double LastRecall { get; set; }
            public double LastDismount { get; set; }

            public void RecordDismount()
            {
                LastDismount = TimeEx.currentTimestamp;
            }

            public void RecordRecall()
            {
                LastRecall = TimeEx.currentTimestamp;
            }

            public void RecordDeath()
            {
                Entity = null;
                EntityId = 0;
                LastDeath = TimeEx.currentTimestamp;
            }

            public void ClearData()
            {
                EntityId = 0;
                LastDeath = 0;
            }

            public static OwnedVehicle New(ulong playerId, string vehicleType)
            {
                var vehicle = new OwnedVehicle();
                vehicle.VehicleType = vehicleType;
                vehicle.PlayerId = playerId;
                return vehicle;
            }
        }

        private void LoadData()
        {
            try
            {
                vehicleDatabase = Interface.Oxide.DataFileSystem.ReadObject<VehicleDatabase>("RustVehicles/RustVehicles");
            }
            catch
            {
                vehicleDatabase = null;
            }
            if (vehicleDatabase == null)
            {
                InitializeDatabase();
            }
        }

            private void InitializeDatabase()
            {
                vehicleDatabase = new VehicleDatabase();
                SaveData();
            }

            private void SaveData()
            {
                Interface.Oxide.DataFileSystem.WriteObject("RustVehicles/RustVehicles", vehicleDatabase);
            }

            internal void OnNewSave()
            {
                if (configData.global.clearVehicleOnWipe)
                {
                    InitializeDatabase();
                }
                else
                {
                    vehicleDatabase.ResetPlayerData();
                    SaveData();
                }
            }

            void ManualWipe()
            {
                InitializeDatabase();
                Puts("Data wiped successfully");
            }
            #endregion DataFile

        #region Oxide Hooks & Integration
        internal void Init()
        {
            LoadData();
            Instance = this;
            Economics = PluginBridges.Economics;
            ServerRewards = PluginBridges.ServerRewards;
            Friends = PluginBridges.Friends;
            Clans = PluginBridges.Clans;
            NoEscape = PluginBridges.NoEscape;
            LandOnCargoShip = PluginBridges.LandOnCargoShip;
            RustTranslationAPI = PluginBridges.RustTranslationAPI;
            ZoneManager = PluginBridges.ZoneManager;
            CustomEntities = PluginBridges.CustomEntities;
            RustCar = PluginBridges.RustCar;
            RustPlane = PluginBridges.RustPlane;
            RustHelicopter = PluginBridges.RustHelicopter;
            KaruzaVehicleChatCommand = PluginBridges.KaruzaVehicleChatCommand;
            permission.RegisterPermission(PERMISSION_USE, this);
            permission.RegisterPermission(PERMISSION_ALL, this);
            permission.RegisterPermission(PERMISSION_ADMIN, this);
            permission.RegisterPermission(PERMISSION_BYPASS_COST, this);
            permission.RegisterPermission(PERMISSION_NO_DAMAGE, this);
            permission.RegisterPermission(PERMISSION_NO_COLLISION_DAMAGE, this);
            permission.RegisterPermission(PERMISSION_PICKUP, this);


            string[] normalVehicleTypes = { "Tugboat", "Rowboat", "RHIB", "Sedan", "HotAirBalloon", "ArmoredHotAirBalloon", 
                "MiniCopter", "AttackHelicopter", "TransportHelicopter", "Chinook", "RidableHorse", "WorkCart", 
                "SedanRail", "MagnetCrane", "SubmarineSolo", "SubmarineDuo", "Snowmobile", "Kayak", "PedalBike", 
                "PedalTrike", "MotorBike", "MotorBike_SideCar", "Dpv", "SiegeTower", "Catapult", "Batteringram", "Ballista" };
            foreach (string vehicleType in normalVehicleTypes)
            {
                allVehicleSettings.Add(vehicleType, GetBaseVehicleSettings(vehicleType));
            }

            foreach (var entry in configData.modularVehicles)
            {
                allVehicleSettings.Add(entry.Key, entry.Value);
            }
            foreach (var entry in configData.trainVehicles)
            {
                allVehicleSettings.Add(entry.Key, entry.Value);
            }
            

            if (configData.customVehicles != null)
            {
                foreach (var entry in configData.customVehicles)
                {
                    allVehicleSettings.Add(entry.Key, entry.Value);
                }
            }

            foreach (var entry in allVehicleSettings)
            {
                BaseVehicleSettings settings = entry.Value;

                if (settings.UsePermission && !string.IsNullOrEmpty(settings.Permission))
                {
                    if (!permission.PermissionExists(settings.Permission, this))
                    {
                        permission.RegisterPermission(settings.Permission, this);
                    }
                }

                if (settings.UsePermission && !string.IsNullOrEmpty(settings.BypassCostPermission))
                {
                    if (!permission.PermissionExists(settings.BypassCostPermission, this))
                    {
                        permission.RegisterPermission(settings.BypassCostPermission, this);
                    }
                }

                foreach (var perm in settings.CooldownPermissions.Keys)
                {
                    if (!permission.PermissionExists(perm, this))
                    {
                        permission.RegisterPermission(perm, this);
                    }
                }

                foreach (var command in settings.Commands)
                {
                    if (string.IsNullOrEmpty(command))
                    {
                        continue;
                    }
                    var commandLower = command.ToLower();
                    if (!commandToVehicleType.ContainsKey(commandLower))
                    {
                        commandToVehicleType.Add(commandLower, entry.Key);
                    }
                    else
                    {
                        var existingVehicle = commandToVehicleType[commandLower];
                        PrintError($"You have the same two commands({command}). Command '{commandLower}' already maps to '{existingVehicle}', trying to map to '{entry.Key}'");
                    }
                    if (configData.chat.useUniversalCommand)
                    {
                        cmd.AddChatCommand(command, this, nameof(CmdUniversal));
                    }
                    if (!string.IsNullOrEmpty(configData.chat.customKillCommandPrefix))
                    {
                        cmd.AddChatCommand(configData.chat.customKillCommandPrefix + command, this, nameof(CmdCustomKill));
                    }
                }
            }

            cmd.AddChatCommand(configData.chat.helpCommand, this, nameof(CmdLicenseHelp));
            cmd.AddChatCommand(configData.chat.buyCommand, this, nameof(CmdBuyVehicle));
            cmd.AddChatCommand(configData.chat.spawnCommand, this, nameof(CmdSpawnVehicle));
            cmd.AddChatCommand(configData.chat.recallCommand, this, nameof(CmdRecallVehicle));
            cmd.AddChatCommand(configData.chat.killCommand, this, nameof(CmdKillVehicle));
            cmd.AddChatCommand("vldiscover", this, nameof(CmdDiscoverCustomVehicles));

            Unsubscribe(nameof(CanMountEntity));
            Unsubscribe(nameof(OnEntityTakeDamage));
            Unsubscribe(nameof(OnEntityDismounted));
            Unsubscribe(nameof(OnEntityEnter));
            Unsubscribe(nameof(CanLootEntity));
            Unsubscribe(nameof(OnEntitySpawned));
            Unsubscribe(nameof(OnRidableAnimalClaimed));
            Unsubscribe(nameof(OnEngineStarted));
            Unsubscribe(nameof(OnVehiclePush));
        }

        internal void OnServerInitialized(bool initial)
        {
            if (initial) ServerMgr.Instance.StartCoroutine(DelayedUpdatePlayerData(TimeEx.currentTimestamp));
            else ServerMgr.Instance.StartCoroutine(UpdatePlayerData(TimeEx.currentTimestamp));
            
            ServerMgr.Instance.StartCoroutine(DelayedInitialization());

            if (ShouldEnforceMountRestrictions())
            {
                Subscribe(nameof(CanMountEntity));
            }
            if (configData.global.noDecay)
            {
                Subscribe(nameof(OnEntityTakeDamage));
            }
            if (configData.global.preventDamagePlayer || configData.global.safeTrainDismount || configData.global.preventDamageNPCs)
            {
                Subscribe(nameof(OnEntityEnter));
            }
            if (configData.global.preventLooting)
            {
                Subscribe(nameof(CanLootEntity));
            }
            if (configData.global.autoClaimFromVendor)
            {
                Subscribe(nameof(OnEntitySpawned));
                Subscribe(nameof(OnRidableAnimalClaimed));
            }

            if (KaruzaVehicleChatCommand == true)
            {
                var pluginName = "KaruzaVehicleChatCommand";
                PrintError($"{pluginName} Detected!");
                NextTick(() => Interface.Oxide.UnloadPlugin(pluginName));
                PrintError($"Unloaded {pluginName} to prevent plugin conflict...");

                NextTick(() => Interface.Oxide.ReloadPlugin(Name));
                return;
            }
        }

        private IEnumerator DelayedInitialization()
        {
            yield return CoroutineEx.waitForSeconds(30f);

            if (Instance != this || configData?.global == null)
            {
                yield break;
            }

            if (configData.global.useCustomVehicles)
            {
                RunDiscoveryAndPromote(() =>
                {
                    configData.global.useCustomVehicles = false;
                    SaveConfig();
                });
            }

            bool hasWipeTime = false;
            foreach (var kvp in allVehicleSettings)
            {
                if (kvp.Value.WipeTime > 0)
                {
                    hasWipeTime = true;
                    break;
                }
            }
            if (configData.global.checkVehiclesInterval > 0 && hasWipeTime)
            {
                Subscribe(nameof(OnEntityDismounted));
                timer.Every(configData.global.checkVehiclesInterval, CheckVehicles);
            }
            else if (configData.normalVehicles?.miniCopter?.flyHackPause > 0 || configData.normalVehicles?.transportHelicopter?.flyHackPause > 0 || configData.normalVehicles?.attackHelicopter?.flyHackPause > 0)
            {
                Subscribe(nameof(OnEntityDismounted));
            }
            if (configData.normalVehicles?.miniCopter?.instantTakeoff == true || configData.normalVehicles?.attackHelicopter?.instantTakeoff == true
                 || configData.normalVehicles?.transportHelicopter?.instantTakeoff == true)
            {
                Subscribe(nameof(OnEngineStarted));
            }
            if (configData.global.preventPushing)
            {
                Subscribe(nameof(OnVehiclePush));
            }
        }

        internal void Unload()
        {
            if (!configData.global.storeVehicle)
            {
                var snapshot = new List<KeyValuePair<BaseEntity, OwnedVehicle>>(vehiclesCache);
                foreach (var entry in snapshot)
                {
                    if (entry.Key != null && !entry.Key.IsDestroyed)
                    {
                        RefundVehicleItems(entry.Value, isUnload: true);
                        entry.Key.Kill(BaseNetworkable.DestroyMode.Gib);
                    }
                    entry.Value.EntityId = 0;
                }
            }
            SaveData();
            Instance = null;
        }
        // ---- Harmony lifecycle (replaces Oxide Init / OnServerInitialized / Unload) ----
        public override void HarmonyInit()
        {
            LoadConfig();
            LoadDefaultMessages();
            Init();
        }

        public override void HarmonyServerInitialized()
        {
            OnServerInitialized(RustVehiclesHarmonyMod.IsFirstServerInit);
        }

        public override void HarmonyUnload()
        {
            Unload();
        }

        internal void OnServerSave()
        {
            timer.Once(Random.Range(0f, 60f), SaveData);
        }

        #region Pickup Support
        private const float PICKUP_RADIUS = 5f;

        private void DebugPickup(string message)
        {
            if (configData.debug.DebugPickup)
            {
                Puts($"[DebugPickup] {message}");
            }
        }

        private void DebugRecall(string message)
        {
            if (configData.debug.DebugRecall)
            {
                Puts($"[DebugRecall] {message}");
            }
        }

        [ChatCommand("pickup")]
        internal void CmdPickup(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_PICKUP))
            {
                Print(player, Lang("NotAllowed", player.UserIDString));
                return;
            }

            DebugPickup($"Player {player.displayName} ({player.userID}) attempting pickup");
            
            RaycastHit hit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out hit, PICKUP_RADIUS, -1))
            {
                DebugPickup("Raycast failed - no hit");
                Print(player, "Vehicle not found");
                return;
            }

            var target = hit.GetEntity() as BaseEntity;
            if (target == null)
            {
                DebugPickup($"Raycast hit but GetEntity() returned null. Collider: {hit.collider?.name}, Layer: {hit.collider?.gameObject.layer}");
                Print(player, "Vehicle not found");
                return;
            }

            DebugPickup("Found entity via raycast:");
            DebugPickup($"  - Entity Type: {target.GetType().Name}");
            DebugPickup($"  - ShortPrefabName: {target.ShortPrefabName}");
            DebugPickup($"  - EntityID (net.ID.Value): {target.net.ID.Value}");
            DebugPickup($"  - PrefabID: {target.prefabID}");
            DebugPickup($"  - OwnerID: {target.OwnerID}");
            DebugPickup($"  - IsDestroyed: {target.IsDestroyed}");
            DebugPickup($"  - vehiclesCache count: {vehiclesCache.Count}");

            OwnedVehicle vehicle;
            if (!vehiclesCache.TryGetValue(target as BaseEntity, out vehicle))
            {
                DebugPickup("Target entity not in vehiclesCache, checking parent...");
                var parent = target.parentEntity.IsValid(true) ? target.parentEntity.Get(true) : null;
                if (parent != null)
                {
                    DebugPickup("Parent entity found:");
                    DebugPickup($"  - Parent Type: {parent.GetType().Name}");
                    DebugPickup($"  - Parent EntityID: {parent.net.ID.Value}");
                    DebugPickup($"  - Parent PrefabID: {parent.prefabID}");
                    DebugPickup($"  - Parent in vehiclesCache: {vehiclesCache.ContainsKey(parent)}");
                }
                else
                {
                    DebugPickup("No parent entity found");
                }
                
                if (parent == null || !vehiclesCache.TryGetValue(parent, out vehicle))
                {
                    DebugPickup("FAILED: Vehicle not found in cache");
                    DebugPickup("Available vehicles in cache:");
                    foreach (var kvp in vehiclesCache)
                    {
                        if (kvp.Key != null && !kvp.Key.IsDestroyed)
                        {
                            DebugPickup($"  - {kvp.Value.VehicleType}: EntityID={kvp.Key.net.ID.Value}, PrefabID={kvp.Key.prefabID}, PlayerID={kvp.Value.PlayerId}");
                        }
                    }
                    Print(player, "Vehicle not found or cannot be picked up");
                    return;
                }
                DebugPickup("Found vehicle via parent entity");
            }
            else
            {
                DebugPickup("Found vehicle directly in cache");
            }

            DebugPickup("Vehicle data:");
            DebugPickup($"  - VehicleType: {vehicle.VehicleType}");
            DebugPickup($"  - PlayerID: {vehicle.PlayerId}");
            DebugPickup($"  - Stored EntityID: {vehicle.EntityId}");
            DebugPickup($"  - Entity reference: {(vehicle.Entity != null ? "Set" : "Null")}");
            if (vehicle.Entity != null)
            {
                DebugPickup($"  - Entity EntityID: {vehicle.Entity.net.ID.Value}");
                DebugPickup($"  - Entity PrefabID: {vehicle.Entity.prefabID}");
            }

            if (vehicle.PlayerId != player.userID)
            {
                DebugPickup($"FAILED: Vehicle owner mismatch. Vehicle PlayerID: {vehicle.PlayerId}, Player userID: {player.userID}");
                Print(player, "This vehicle isn't yours");
                return;
            }

            var effectiveType = GetVehicleTypeFromEntity(vehicle.Entity) ?? vehicle.VehicleType;
            var settings = GetBaseVehicleSettings(effectiveType);
            if (settings == null)
                return;


            var prices = settings.RecallPrices;
            if (prices != null && prices.Count > 0)
            {
                string resources;
                if (!ProcessPayment(player, settings, prices, out resources))
                {
                    Print(player, Lang("NoResourcesToRecallVehicle", player.UserIDString, settings.DisplayName, resources));
                    return;
                }
            }

            DismountAllPlayers(vehicle.Entity);
            if (vehicle.Entity.HasParent())
                vehicle.Entity.SetParent(null, true, true);

            settings.RefundVehicleItems(vehicle, isCrash: false, isUnload: true);

            if (vehicle.Entity != null && !vehicle.Entity.IsDestroyed)
            {
                var entityToKill = vehicle.Entity;
                // Clear entity references (but don't set LastDeath - pickup is not a death)
                vehicle.Entity = null;
                vehicle.EntityId = 0;
                vehiclesCache.Remove(entityToKill);
                entityToKill.Kill(BaseNetworkable.DestroyMode.Gib);
            }
            
            Print(player, $"Vehicle {settings.DisplayName} was picked up and stored. Use /{configData.chat.spawnCommand} {vehicle.VehicleType} to spawn.");
        }

        #endregion Pickup Support

        internal void OnEntityDismounted(BaseMountable entity, BasePlayer player)
        {
            if (entity == null)
            {
                return;
            }
            if (player != null)
            {
                BaseEntity vehicleEntity = entity.GetParentEntity();
                if (configData.normalVehicles.miniCopter.flyHackPause > 0 && vehicleEntity is Minicopter)
                {
                    player.PauseFlyHackDetection(configData.normalVehicles.miniCopter.flyHackPause);
                }
                else if (configData.normalVehicles.transportHelicopter.flyHackPause > 0 && vehicleEntity is ScrapTransportHelicopter)
                {
                    player.PauseFlyHackDetection(configData.normalVehicles.transportHelicopter.flyHackPause);
                }
                else if (configData.normalVehicles.attackHelicopter.flyHackPause > 0 && vehicleEntity is AttackHelicopter)
                {
                    player.PauseFlyHackDetection(configData.normalVehicles.attackHelicopter.flyHackPause);
                }
            }
            var vehicleParent = entity.VehicleParent();
            if (vehicleParent == null || vehicleParent.IsDestroyed)
            {
                return;
            }
            OwnedVehicle vehicle;
            if (!vehiclesCache.TryGetValue(vehicleParent, out vehicle))
            {
                return;
            }
            vehicle.RecordDismount();
        }

        internal void OnEngineStarted(BaseMountable entity, BasePlayer player)
        {
            if (player == null || entity == null) return;

            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_USE)) return;
            BaseVehicle mounted = player.GetMountedVehicle();

            if (mounted == null || !vehiclesCache.ContainsKey(mounted)) return;

            PlayerHelicopter heli = mounted as PlayerHelicopter;

            NextTick(() =>
            {
                if (heli == null) return;

                if (configData.normalVehicles.miniCopter.instantTakeoff && heli is Minicopter)
                {
                    heli.engineController.FinishStartingEngine();
                    return;
                }

                if (configData.normalVehicles.attackHelicopter.instantTakeoff && heli is AttackHelicopter)
                {
                    heli.engineController.FinishStartingEngine();
                }

                if (configData.normalVehicles.transportHelicopter.instantTakeoff && heli is ScrapTransportHelicopter)
                {
                    heli.engineController.FinishStartingEngine();
                }
            });
        }

        internal object OnVehiclePush(BaseVehicle vehicle, BasePlayer player)
        {
            if (vehicle == null || player == null) return null;
            if (!vehiclesCache.TryGetValue(vehicle, out OwnedVehicle foundVehicle)) return null;
            ulong userID = player.userID.Get();

            if (foundVehicle.PlayerId == userID || AreFriends(foundVehicle.PlayerId, player.userID)) return null;
            if (HasAdminPermission(player)) return null;

            SendCantPushMessage(player, foundVehicle);
            return true;
        }

        #region Vehicle Mounting
        internal object CanMountEntity(BasePlayer friend, BaseMountable entity)
        {
            if (friend == null || entity == null)
            {
                return null;
            }

            if (string.Equals(entity.ShortPrefabName, "passengerchair", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entity.ShortPrefabName, "chair.deployed", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            var vehicleParent = entity.VehicleParent();
            if (vehicleParent == null || vehicleParent.IsDestroyed)
            {
                return null;
            }
            OwnedVehicle vehicle;
            if (!vehiclesCache.TryGetValue(vehicleParent, out vehicle))
            {
                return null;
            }
            if (AreFriends(vehicle.PlayerId, friend.userID))
            {
                return null;
            }
            if (configData.global.preventDriverSeat && vehicleParent.HasMountPoints())
            {
                var matchedMountable = false;
                foreach (var mountPointInfo in vehicleParent.allMountPoints)
                {
                    if (mountPointInfo == null || mountPointInfo.mountable != entity) continue;
                    matchedMountable = true;
                    if (!mountPointInfo.isDriver)
                    {
                        return null;
                    }
                    break;
                }
                if (!matchedMountable)
                {
                    return null;
                }
            }
            else if (!configData.global.preventMounting)
            {
                return null;
            }
            if (HasAdminPermission(friend))
            {
                return null;
            }
            SendCantUseMessage(friend, vehicle);
            return _false;
        }

        #endregion Mount

        #region Loot

        internal object CanLootEntity(BasePlayer friend, RidableHorse horse)
        {
            if (friend == null || horse == null)
            {
                return null;
            }
            return CanLootEntityInternal(friend, horse);
        }

        internal object CanLootEntity(BasePlayer friend, StorageContainer container)
        {
            if (friend == null || container == null) return null;

            var parentEntity = container.GetParentEntity();

            if (parentEntity == null) return null;

            return CanLootEntityInternal(friend, parentEntity);
        }

        private object CanLootEntityInternal(BasePlayer friend, BaseEntity parentEntity)
        {
            OwnedVehicle vehicle;
            if (!TryGetVehicle(parentEntity, out vehicle))
            {
                return null;
            }

            if (AreFriends(vehicle.PlayerId, friend.userID)) return null;

            if (HasAdminPermission(friend)) return null;

            SendCantUseMessage(friend, vehicle);
            return _false;
        }

        #endregion Loot

        #region Vehicle Decay Management

        internal void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo hitInfo)
        {
            if (entity == null || hitInfo?.damageTypes == null)
            {
                return;
            }
            OwnedVehicle vehicle;
            if (!TryGetVehicle(entity, out vehicle))
            {
                return;
            }
            if (permission.UserHasPermission(vehicle.PlayerId.ToString(), PERMISSION_NO_DAMAGE) && GetBaseVehicleDamage(vehicle.VehicleType))
            {
                hitInfo.damageTypes.ScaleAll(0);
                return;
            }
            if (hitInfo.damageTypes.Has(DamageType.Collision) && permission.UserHasPermission(vehicle.PlayerId.ToString(), PERMISSION_NO_COLLISION_DAMAGE) && GetBaseVehicleCollisionDamage(vehicle.VehicleType))
            {
                hitInfo.damageTypes.Scale(DamageType.Collision, 0);
                return;
            }
            if (!hitInfo.damageTypes.Has(DamageType.Decay)) return;
            hitInfo.damageTypes.Scale(DamageType.Decay, 0);
        }

        #endregion Decay

        #region Claim

        internal void OnEntitySpawned(Tugboat tugboat)
        {
            TryClaimVehicle(tugboat);
        }

        internal void OnEntitySpawned(BaseSubmarine baseSubmarine)
        {
            TryClaimVehicle(baseSubmarine);
        }

        internal void OnEntitySpawned(MotorRowboat motorRowboat)
        {
            TryClaimVehicle(motorRowboat);
        }

        internal void OnEntitySpawned(Minicopter miniCopter)
        {
            TryClaimVehicle(miniCopter);
        }

        internal void OnEntitySpawned(AttackHelicopter attackHelicopter)
        {
            TryClaimVehicle(attackHelicopter);
        }

        internal void OnRidableAnimalClaimed(BaseVehicle ridableAnimal, BasePlayer player)
        {
            TryClaimVehicle(ridableAnimal, player);
        }

        #endregion Claim

        #region Damage Protection System


        internal object OnEntityEnter(TriggerHurtNotChild triggerHurtNotChild, BasePlayer player)
        {
            if (triggerHurtNotChild == null || player == null || triggerHurtNotChild.SourceEntity == null)
            {
                return null;
            }
            var sourceEntity = triggerHurtNotChild.SourceEntity;

            if (!vehiclesCache.ContainsKey(sourceEntity) || (!configData.global.preventDamageNPCs && !player.userID.IsSteamId())) return null;

            var baseVehicle = sourceEntity as BaseVehicle;

            if ((baseVehicle == null || player.userID.IsSteamId()) && configData.global.preventDamagePlayer) return _false;

            if (configData.global.preventDamageNPCs && !player.userID.IsSteamId()) return _false;

            if (baseVehicle is TrainEngine)
            {
                if (!configData.global.safeTrainDismount && configData.global.preventDamagePlayer && player.userID.IsSteamId()) return _false;

                if (!configData.global.safeTrainDismount) return null;

                var transform = triggerHurtNotChild.transform;
                MoveToPosition(player, transform.position + (Random.value >= 0.5f ? -transform.right : transform.right) * 2.5f);

                return configData.global.preventDamagePlayer ? _false : null;
            }

            if (!configData.global.preventDamagePlayer) return null;

            Vector3 pos;
            if (GetDismountPosition(baseVehicle, player, out pos))
            {
                MoveToPosition(player, pos);
            }

            return _false;
        }


        internal object OnEntityEnter(TriggerHurt triggerHurt, BasePlayer player)
        {
            if (triggerHurt == null || player == null)
            {
                return null;
            }
            var sourceEntity = triggerHurt.gameObject.ToBaseEntity();
            if (sourceEntity == null || !vehiclesCache.ContainsKey(sourceEntity)) return null;

            if (configData.global.preventDamagePlayer && player.userID.IsSteamId()
                || (configData.global.preventDamageNPCs && !player.userID.IsSteamId()))
            {
                MoveToPosition(player, sourceEntity.CenterPoint() + Vector3.down);

                return _false;
            }
            return null;
        }

        #endregion Damage

        #region Destroy

        internal void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            OnEntityDeathOrKill(entity, true);
        }

        internal void OnEntityKill(BaseCombatEntity entity)
        {
            OnEntityDeathOrKill(entity);
        }

        #endregion Destroy

        #region Vehicle Customization

        internal object OnEntityReskin(BaseEntity entity, ItemSkinDirectory.Skin skin, BasePlayer player)
        {
            if (entity == null || player == null)
            {
                return null;
            }
            OwnedVehicle vehicle;
            if (TryGetVehicle(entity, out vehicle))
            {
                return _false;
            }
            return null;
        }

        #endregion Reskin

        #region Player Management
        internal void OnPlayerDisconnected(BasePlayer player)
        {
            if (!configData.global.killOnDisconnect)
                return;

            var playerVehicles = vehicleDatabase.GetPlayerVehicles(player.userID);
            if (playerVehicles == null || playerVehicles.Count == 0)
            {
                return;
            }

            foreach (var kvp in playerVehicles)
            {
                var vehicleType = kvp.Key;
                KillLicensedVehicle(player, vehicleType);
            }
        }
        #endregion Player

        #endregion Oxide Hooks

        #region Intergration: RustTranslationAPI

        [HookMethod(nameof(SpawnLicensedVehicle))]
        public bool SpawnLicensedVehicle(BasePlayer player, string vehicleType, string command, bool bypassCooldown = false)
        {
            SpawnVehicle(player, vehicleType, bypassCooldown, command);

            if (!GetLicensedVehicle(player.userID.Get(), vehicleType)) return false;

            return true;
        }

        [HookMethod(nameof(RecallLicensedVehicle))]
        public bool RecallLicensedVehicle(BasePlayer player, string vehicleType, string command, bool bypassCooldown = false)
        {
            RecallVehicle(player, vehicleType, bypassCooldown, command);
            return true;
        }

        [HookMethod(nameof(KillLicensedVehicle))]
        public bool KillLicensedVehicle(BasePlayer player, string vehicleType, bool response = true)
        {
            KillVehicle(player, vehicleType, response);

            if (GetLicensedVehicle(player.userID.Get(), vehicleType)) return false;

            return true;
        }

        [HookMethod(nameof(BuyVehicleLicense))]
        public bool BuyVehicleLicense(BasePlayer player, string vehicleType, bool response = true)
        {
            if (!BuyVehicle(player, vehicleType, response)) return false;

            return true;
        }

        [HookMethod(nameof(IsLicensedVehicle))]
        public bool IsLicensedVehicle(BaseEntity entity)
        {
            return vehiclesCache.ContainsKey(entity);
        }

        [HookMethod(nameof(GetLicensedVehicle))]
        public BaseEntity GetLicensedVehicle(ulong playerId, string license)
        {
            return vehicleDatabase.GetVehicleLicense(playerId, license)?.Entity;
        }
        [HookMethod(nameof(HasVehicleLicense))]
        public bool HasVehicleLicense(ulong playerId, string license)
        {
            return vehicleDatabase.HasVehicleLicense(playerId, license);
        }

        [HookMethod(nameof(RemoveVehicleLicense))]
        public bool RemoveVehicleLicense(ulong playerId, string license)
        {
            return vehicleDatabase.RemoveVehicleLicense(playerId, license);
        }

        [HookMethod(nameof(AddVehicleLicense))]
        public bool AddVehicleLicense(ulong playerId, string license)
        {
            return vehicleDatabase.AddVehicleLicense(playerId, license);
        }

        [HookMethod(nameof(GetVehicleLicenses))]
        public List<string> GetVehicleLicenses(ulong playerId)
        {
            return vehicleDatabase.GetVehicleLicenseNames(playerId);
        }

        [HookMethod(nameof(PurchaseAllVehicles))]
        public void PurchaseAllVehicles(ulong playerId)
        {
            vehicleDatabase.PurchaseAllVehicles(playerId);
        }

        [HookMethod("ResetVehicleDeathState")]  
        public void ResetVehicleDeathState()
        {
            foreach (var playerData in vehicleDatabase.playerData)
            {
                foreach (var vehicle in playerData.Value)
                {
                    vehicle.Value.LastDeath = 0;

                    Puts($"Reset the lastDeath for vehicle {vehicle.Key} of player {playerData.Key}");
                }
            }
            SaveData();
        }

        private OwnedVehicle GetLicensedVehicle(string vehicleType)
        {
            foreach (var playerData in vehicleDatabase.playerData)
            {
                if (playerData.Value.ContainsKey(vehicleType))
                {
                    return playerData.Value[vehicleType];
                }
            }
            return null;
        }

        public bool IsLicensedVehicleType(string vehicleType)
        {
            string vehicleTypeLower = vehicleType.ToLower();

            foreach (var playerData in vehicleDatabase.playerData)
            {
                foreach (var vehicle in playerData.Value)
                {
                    if (vehicle.Key.ToLower() == vehicleTypeLower)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        #endregion API

        #region External: TranslationAPI

        private string GetItemTranslationByShortName(string language, string itemShortName)
        {
            return (string)RustTranslationAPI.Call("GetItemTranslationByShortName", language, itemShortName);
        }

        private string GetItemDisplayName(string language, string itemShortName, string displayName)
        {
            if (RustTranslationAPI != null)
            {
                var displayName1 = GetItemTranslationByShortName(language, itemShortName);
                if (!string.IsNullOrEmpty(displayName1))
                {
                    return displayName1;
                }
            }
            return displayName;
        }

        #endregion RustTranslationAPI

        #region Core

        #region User Communication

        private void SendCantUseMessage(BasePlayer friend, OwnedVehicle vehicle)
        {
            var settings = GetBaseVehicleSettings(vehicle.VehicleType);
            if (settings != null)
            {
                var player = RustCore.FindPlayerById(vehicle.PlayerId);
                var playerName = player?.displayName ?? ServerMgr.Instance.persistance.GetPlayerName(vehicle.PlayerId) ?? "Unknown";
                Print(friend, Lang("CantUse", friend.UserIDString, settings.DisplayName, $"<color=#{(player != null && player.IsConnected ? "69D214" : "FF6347")}>{playerName}</color>"));
            }
        }

        private void SendCantPushMessage(BasePlayer friend, OwnedVehicle vehicle)
        {
            var settings = GetBaseVehicleSettings(vehicle.VehicleType);
            if (settings == null) return;

            var player = RustCore.FindPlayerById(vehicle.PlayerId);
            var playerName = player?.displayName ?? ServerMgr.Instance.persistance.GetPlayerName(vehicle.PlayerId) ?? "Unknown";
            Print(friend, Lang("CantPush", friend.UserIDString, settings.DisplayName, $"<color=#{(player != null && player.IsConnected ? "69D214" : "FF6347")}>{playerName}</color>"));
        }

        #endregion User Communication

        #region CheckEntity

        private void OnEntityDeathOrKill(BaseCombatEntity entity, bool isCrash = false)
        {
            if (entity == null)
            {
                return;
            }
            OwnedVehicle vehicle;
            if (!vehiclesCache.TryGetValue(entity, out vehicle))
            {
                return;
            }

            RefundVehicleItems(vehicle, isCrash);


            if (entity is Minicopter && configData.normalVehicles.miniCopter.dropStorage)
            {
                var containers = entity.GetComponentsInChildren<StorageContainer>();
                foreach (var c in containers)
                {
                    c.DropItems();
                }
                var turrets = entity.GetComponentsInChildren<AutoTurret>();
                foreach (var t in turrets)
                {
                    t.DropItems();
                }
            }

            var settings = GetBaseVehicleSettings(vehicle.VehicleType);
            if (isCrash && settings.RemoveLicenseOnceCrash)
            {
                RemoveVehicleLicense(vehicle.PlayerId, vehicle.VehicleType);
            }

            vehicle.RecordDeath();
            vehiclesCache.Remove(entity);
            Interface.CallHook("OnLicensedVehicleDeath", vehicle.PlayerId, vehicle.VehicleType);
        }

        #endregion CheckEntity

        #region Vehicle Status Monitoring

        private void CheckVehicles()
        {
            var currentTimestamp = TimeEx.currentTimestamp;
            var snapshot = new List<KeyValuePair<BaseEntity, OwnedVehicle>>(vehiclesCache.Count);
            foreach (var entry in vehiclesCache)
            {
                snapshot.Add(entry);
            }
            foreach (var entry in snapshot)
            {
                if (entry.Key == null || entry.Key.IsDestroyed)
                {
                    continue;
                }
                if (VehicleIsActive(entry.Key, entry.Value, currentTimestamp))
                {
                    continue;
                }

                if (VehicleAnyMounted(entry.Key))
                {
                    continue;
                }
                entry.Key.Kill(BaseNetworkable.DestroyMode.Gib);
            }
        }

        private bool VehicleIsActive(BaseEntity entity, OwnedVehicle vehicle, double currentTimestamp)
        {
            var settings = GetBaseVehicleSettings(vehicle.VehicleType);
            if (settings.WipeTime <= 0)
            {
                return true;
            }
            if (settings.ExcludeCupboard && entity.GetBuildingPrivilege() != null)
            {
                return true;
            }
            return currentTimestamp - vehicle.LastDismount < settings.WipeTime;
        }

        #endregion Vehicle Status Monitoring

        #region Refund

        private void RefundVehicleItems(OwnedVehicle vehicle, bool isCrash = false, bool isUnload = false)
        {
            var entity = vehicle.Entity;
            if (entity == null || entity.IsDestroyed)
            {
                return;
            }

            var effectiveType = GetVehicleTypeFromEntity(entity) ?? vehicle.VehicleType;
            var settings = GetBaseVehicleSettings(effectiveType);
            if (settings != null)
            {
                settings.RefundVehicleItems(vehicle, isCrash, isUnload);
            }
        }

        private static void DropItemContainer(BaseEntity entity, ulong playerId, List<Item> collect)
        {
            var droppedItemContainer = GameManager.server.CreateEntity(PREFAB_ITEM_DROP, entity.GetDropPosition(), entity.transform.rotation) as DroppedItemContainer;
            if (droppedItemContainer != null)
            {
                droppedItemContainer.inventory = new ItemContainer();
                droppedItemContainer.inventory.ServerInitialize(null, Mathf.Min(collect.Count, droppedItemContainer.maxItemCount));
                droppedItemContainer.inventory.GiveUID();
                droppedItemContainer.inventory.entityOwner = droppedItemContainer;
                droppedItemContainer.inventory.SetFlag(ItemContainer.Flag.NoItemInput, true);
                for (var i = collect.Count - 1; i >= 0; i--)
                {
                    var item = collect[i];
                    if (!item.MoveToContainer(droppedItemContainer.inventory))
                    {
                        item.DropAndTossUpwards(droppedItemContainer.transform.position);
                    }
                }

                droppedItemContainer.OwnerID = playerId;
                droppedItemContainer.Spawn();
            }
        }

        #endregion Refund

        #region Payment Processing

        private bool ProcessPayment(BasePlayer player, BaseVehicleSettings settings, Dictionary<string, PriceInfo> prices, out string resources)
        {
            if (permission.UserHasPermission(player.UserIDString, PERMISSION_BYPASS_COST) || permission.UserHasPermission(player.UserIDString, settings.BypassCostPermission))
            {
                resources = null;
                return true;
            }

            if (!ValidatePayment(player, prices, out resources))
            {
                return false;
            }

            var collect = Pool.Get<List<Item>>();
            foreach (var entry in prices)
            {
                if (entry.Value.amount <= 0)
                {
                    continue;
                }
                var itemDefinition = ItemManager.FindItemDefinition(entry.Key);
                if (itemDefinition != null)
                {
                    player.inventory.Take(collect, itemDefinition.itemid, entry.Value.amount);
                    player.Command("note.inv", itemDefinition.itemid, -entry.Value.amount);
                    continue;
                }
                switch (entry.Key.ToLower())
                {
                    case "economics":
                        Economics?.Call("Withdraw", player.userID.Get(), (double)entry.Value.amount);
                        continue;

                    case "serverrewards":
                        ServerRewards?.Call("TakePoints", player.userID.Get(), entry.Value.amount);
                        continue;
                }
            }

            foreach (var item in collect)
            {
                item.Remove();
            }
            Pool.FreeUnmanaged(ref collect);
            resources = null;
            return true;
        }

        private bool ValidatePayment(BasePlayer player, Dictionary<string, PriceInfo> prices, out string resources)
        {
            var entries = new Hash<string, int>();
            var language = RustTranslationAPI != null ? lang.GetLanguage(player.UserIDString) : null;
            foreach (var entry in prices)
            {
                if (entry.Value.amount <= 0)
                {
                    continue;
                }
                int missingAmount;
                var itemDefinition = ItemManager.FindItemDefinition(entry.Key);
                if (itemDefinition != null)
                {
                    missingAmount = entry.Value.amount - player.inventory.GetAmount(itemDefinition.itemid);
                }
                else
                {
                    missingAmount = GetCurrencyBalance(entry.Key, entry.Value.amount, player.userID.Get());
                }

                if (missingAmount <= 0)
                {
                    continue;
                }
                var displayName = GetItemDisplayName(language, entry.Key, entry.Value.displayName);
                entries[displayName] += missingAmount;
            }
            if (entries.Count > 0)
            {
                var stringBuilder = new StringBuilder();
                foreach (var entry in entries)
                {
                    stringBuilder.AppendLine($"* {Lang("PriceFormat", player.UserIDString, entry.Key, entry.Value)}");
                }
                resources = stringBuilder.ToString();
                return false;
            }
            resources = null;
            return true;
        }

        private int GetCurrencyBalance(string key, int price, ulong playerId)
        {
            switch (key.ToLower())
            {
                case "economics":
                    var balance = Economics?.Call("Balance", playerId);
                    if (balance is double)
                    {
                        var n = price - (double)balance;
                        return n <= 0 ? 0 : (int)Math.Ceiling(n);
                    }
                    return price;

                case "serverrewards":
                    var points = ServerRewards?.Call("CheckPoints", playerId);
                    if (points is int)
                    {
                        var n = price - (int)points;
                        return n <= 0 ? 0 : n;
                    }
                    return price;

                default:
                    PrintError($"Unknown Currency Type '{key}'");
                    return price;
            }
        }

        #endregion Payment Processing

        #region AreFriends

        private bool AreFriends(ulong playerId, ulong friendId)
        {
            if (playerId == friendId)
            {
                return true;
            }
            if (configData.global.useTeams && SameTeam(playerId, friendId))
            {
                return true;
            }

            if (configData.global.useFriends && HasFriend(playerId, friendId))
            {
                return true;
            }
            if (configData.global.useClans && SameClan(playerId, friendId))
            {
                return true;
            }
            return false;
        }

        private static bool SameTeam(ulong playerId, ulong friendId)
        {
            if (!RelationshipManager.TeamsEnabled())
            {
                return false;
            }
            var playerTeam = RelationshipManager.ServerInstance.FindPlayersTeam(playerId);
            if (playerTeam == null)
            {
                return false;
            }
            var friendTeam = RelationshipManager.ServerInstance.FindPlayersTeam(friendId);
            if (friendTeam == null)
            {
                return false;
            }
            return playerTeam == friendTeam;
        }

        private bool HasFriend(ulong playerId, ulong friendId)
        {
            if (Friends == null)
            {
                return false;
            }
            return (bool)Friends.Call("HasFriend", playerId, friendId);
        }

        private static bool IsSteamIdInClan(IClan clan, ulong steamId)
        {
            if (clan == null || steamId == 0UL)
            {
                return false;
            }

            if (clan.Creator == steamId)
            {
                return true;
            }

            if (clan.Members == null)
            {
                return false;
            }

            for (int i = 0; i < clan.Members.Count; i++)
            {
                if (clan.Members[i].SteamId == steamId)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsNativeClanMate(ulong playerId, ulong friendId)
        {
            if (playerId == friendId)
            {
                return true;
            }

            var playerA = BasePlayer.FindByID(playerId) ?? BasePlayer.FindSleeping(playerId);
            var playerB = BasePlayer.FindByID(friendId) ?? BasePlayer.FindSleeping(friendId);
            if (playerA != null && playerB != null && playerA.clanId != 0L && playerA.clanId == playerB.clanId)
            {
                return true;
            }

            var backend = ClanManager.ServerInstance?.Backend;
            if (backend != null)
            {
                long clanId = 0L;
                if (playerA != null && playerA.clanId != 0L)
                {
                    clanId = playerA.clanId;
                }
                else if (playerB != null && playerB.clanId != 0L)
                {
                    clanId = playerB.clanId;
                }

                if (clanId != 0L && backend.TryGet(clanId, out IClan clan)
                    && IsSteamIdInClan(clan, playerId) && IsSteamIdInClan(clan, friendId))
                {
                    return true;
                }
            }

            if (playerA?.serverClan != null
                && (IsSteamIdInClan(playerA.serverClan, friendId) || playerA.serverClan.Creator == friendId))
            {
                return true;
            }

            if (playerB?.serverClan != null
                && (IsSteamIdInClan(playerB.serverClan, playerId) || playerB.serverClan.Creator == playerId))
            {
                return true;
            }

            return false;
        }

        private bool SameClan(ulong playerId, ulong friendId)
        {
            return IsNativeClanMate(playerId, friendId);
        }

        private bool ShouldEnforceMountRestrictions()
        {
            return configData.global.preventMounting || configData.global.preventDriverSeat;
        }

        private void ReleaseVanillaOwnerLock(BaseEntity entity)
        {
            if (!ShouldEnforceMountRestrictions() || entity == null)
            {
                return;
            }

            var baseVehicle = entity as BaseVehicle;
            if (baseVehicle != null && baseVehicle.OnlyOwnerAccessible())
            {
                baseVehicle.ClearOwnerEntry();
                return;
            }

            var balloon = entity as HotAirBalloon;
            if (balloon != null && balloon.OnlyOwnerAccessible())
            {
                balloon.ClearOwnerEntry();
            }
        }

        #endregion AreFriends

        #region Player Restriction Checks

        private bool IsPlayerBlocked(BasePlayer player)
        {
            if (NoEscape == null)
            {
                return false;
            }
            if (configData.global.useRaidBlocker && IsRaidBlocked(player.UserIDString))
            {
                Print(player, Lang("RaidBlocked", player.UserIDString));
                return true;
            }
            if (configData.global.useCombatBlocker && IsCombatBlocked(player.UserIDString))
            {
                Print(player, Lang("CombatBlocked", player.UserIDString));
                return true;
            }
            return false;
        }

        private bool IsRaidBlocked(string playerId)
        {
            return (bool)NoEscape.Call("IsRaidBlocked", playerId);
        }

        private bool IsCombatBlocked(string playerId)
        {
            return (bool)NoEscape.Call("IsCombatBlocked", playerId);
        }

        private bool InZone(BasePlayer player)
        {
            if (ZoneManager == null || !ZoneManager.IsLoaded) return false;
            foreach (var zone in configData.AntiSpawnZones)
            {
                if ((bool)ZoneManager?.Call("PlayerHasFlag", player, zone.ToLower()))
                {
                    return true;
                }
            }
            return false;
        }

        #endregion IsPlayerBlocked

        #region Karuza API (Custom Entities)

        private sealed class KaruzaEntitiesCommonFileConfig
        {
            [JsonProperty("APIPath")]
            public string APIPath { get; set; }
            [JsonProperty("APIId")]
            public string APIId { get; set; }
            [JsonProperty("APISecret")]
            public string APISecret { get; set; }
        }

        private sealed class KaruzaApiEntityRow
        {
            public string Name { get; set; }
            public JObject Config { get; set; }
        }

        private bool TryGetKaruzaEntitiesApiCredentials(out string apiPath, out string apiId, out string apiSecret)
        {
            apiPath = null;
            apiId = null;
            apiSecret = null;
            try
            {
                if (!File.Exists(KaruzaEntitiesCommonConfigPath))
                    return false;

                var cfg = JsonConvert.DeserializeObject<KaruzaEntitiesCommonFileConfig>(File.ReadAllText(KaruzaEntitiesCommonConfigPath));
                if (cfg == null)
                    return false;

                if (string.IsNullOrWhiteSpace(cfg.APIPath) || string.IsNullOrWhiteSpace(cfg.APIId) || string.IsNullOrWhiteSpace(cfg.APISecret))
                    return false;

                apiPath = cfg.APIPath.Trim();
                if (!apiPath.EndsWith("/", StringComparison.Ordinal))
                    apiPath += "/";
                apiId = cfg.APIId.Trim();
                apiSecret = cfg.APISecret.Trim();
                return true;
            }
            catch (Exception ex)
            {
                PrintWarning($"[CustomVehicles] Could not read KaruzaEntitiesCommon.json for API: {ex.Message}");
                return false;
            }
        }

        private static Dictionary<string, string> BuildKaruzaSignedRequestHeaders(RequestMethod requestMethod, string url, string body, string appIdLower, string apiSecretBase64)
        {
            var headers = new Dictionary<string, string>
            {
                { "Content-Type", "application/json" },
                { "Accept-Type", "application/json" },
                { "Accept-Encoding", "gzip" },
            };

            string requestContentBase64String = string.Empty;
            string requestUri = Uri.EscapeDataString(url.ToLower());
            string requestHttpMethod = $"{requestMethod}";

            DateTime epochStart = new DateTime(1970, 01, 01, 0, 0, 0, 0, DateTimeKind.Utc);
            TimeSpan timeSpan = DateTime.UtcNow - epochStart;
            string requestTimeStamp = Convert.ToUInt64(timeSpan.TotalSeconds).ToString();

            string nonce = Guid.NewGuid().ToString("N");

            byte[] content = Encoding.ASCII.GetBytes(body ?? string.Empty);
            using (SHA256 hash = SHA256.Create())
            {
                byte[] requestContentHash = hash.ComputeHash(content);
                requestContentBase64String = Convert.ToBase64String(requestContentHash);
            }

            string signatureRawData = $"{appIdLower}{requestHttpMethod}{requestUri}{requestTimeStamp}{nonce}{requestContentBase64String}";
            byte[] secretKeyByteArray = Convert.FromBase64String(apiSecretBase64);
            byte[] signature = Encoding.UTF8.GetBytes(signatureRawData);

            using (HMACSHA256 hmac = new HMACSHA256(secretKeyByteArray))
            {
                byte[] signatureBytes = hmac.ComputeHash(signature);
                string requestSignatureBase64String = Convert.ToBase64String(signatureBytes);
                headers.Add("Authorization", string.Format("gameserver {0}:{1}:{2}:{3}", appIdLower, requestSignatureBase64String, nonce, requestTimeStamp));
            }

            return headers;
        }

        private void RequestKaruzaEntityList(string apiPath, string apiId, string apiSecret, int entityTypeId, Action<int, string> callback)
        {
            var fullUrl = $"{apiPath}{entityTypeId}";
            Dictionary<string, string> headers;
            try
            {
                headers = BuildKaruzaSignedRequestHeaders(RequestMethod.GET, fullUrl, string.Empty, apiId.ToLower(), apiSecret);
            }
            catch (Exception ex)
            {
                PrintWarning($"[CustomVehicles] Karuza API signing failed for entity type {entityTypeId}: {ex.Message}");
                callback?.Invoke(0, null);
                return;
            }

            webrequest.Enqueue(fullUrl, string.Empty, (code, response) => callback?.Invoke(code, response), this, RequestMethod.GET, headers, 180f, decompressionMethod: DecompressionMethods.GZip);
        }

        private static string TryExtractCustomPrefabFromConfig(JObject config)
        {
            if (config == null)
                return null;
            var m = KaruzaApiCustomPrefabSearch.Match(config.ToString(Formatting.None));
            return m.Success ? m.Value : null;
        }

        private void MergeKaruzaApiEntityList(string response)
        {
            var rows = JsonConvert.DeserializeObject<List<KaruzaApiEntityRow>>(response);
            if (rows == null || rows.Count == 0)
                return;

            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrEmpty(row.Name))
                    continue;
                if (discoveredCustomVehicles.ContainsKey(row.Name))
                    continue;

                string prefabPath = null;
                ulong skinId = 0;
                if (_catalogByName != null && _catalogByName.TryGetValue(row.Name, out var cat) && cat != null)
                {
                    prefabPath = cat.PrefabPath;
                    skinId = cat.SkinID;
                }

                if (string.IsNullOrEmpty(prefabPath))
                    prefabPath = TryExtractCustomPrefabFromConfig(row.Config);

                var bodySkinTok = row.Config?["BodySkinId"];
                if (bodySkinTok != null && bodySkinTok.Type != JTokenType.Null && ulong.TryParse(bodySkinTok.ToString(), out var bodySkin) && bodySkin > 0)
                    skinId = bodySkin;

                if (string.IsNullOrEmpty(prefabPath))
                {
                    PrintWarning($"[CustomVehicles] Karuza API vehicle '{row.Name}' skipped (no assets/custom prefab; ensure KaruzaVehicleItemManager catalog lists this vehicle or API config includes prefab data).");
                    continue;
                }

                discoveredCustomVehicles[row.Name] = new CustomVehicleInfo
                {
                    PrefabPath = prefabPath,
                    SkinID = skinId
                };
            }
        }

        #endregion Karuza API (Custom Entities)

        #region Karuza Catalog Loader

        private void LoadKaruzaCatalog()
        {
            if (configData.debug.DebugKaruzaVehicles)
            {
                Puts($"[DebugKaruzaVehicles] LoadKaruzaCatalog started - Path: {KaruzaCatalogPath}");
            }
            
            _catalogByName = new Dictionary<string, CustomVehicleConfig>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(KaruzaCatalogPath))
                {
                    if (configData.debug.DebugKaruzaVehicles)
                    {
                        PrintWarning($"[DebugKaruzaVehicles] Catalog not found: {KaruzaCatalogPath}");
                    }
                    else
                    {
                        PrintWarning($"[CustomVehicles] Catalog not found: {KaruzaCatalogPath}");
                    }
                    return;
                }
                var src = File.ReadAllText(KaruzaCatalogPath);

                if (configData.debug.DebugKaruzaVehicles)
                {
                    Puts($"[DebugKaruzaVehicles] Catalog file read - Size: {src.Length} characters");
                }

                var prefabRx = new System.Text.RegularExpressions.Regex(@"""assets/custom/(?<name>[^""]+)\.prefab""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var skinRx = new System.Text.RegularExpressions.Regex(@"Skin(Id|ID)\s*=\s*(?<skin>\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                int processedCount = 0;
                foreach (System.Text.RegularExpressions.Match m in prefabRx.Matches(src))
                {
                    var name = m.Groups["name"].Value;
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    ulong skin = 0;
                    var tail = src.Substring(m.Index, Math.Min(500, src.Length - m.Index));
                    var sm = skinRx.Match(tail);
                    if (sm.Success && ulong.TryParse(sm.Groups["skin"].Value, out var parsed)) skin = parsed;

                    _catalogByName[name] = new CustomVehicleConfig { Name = name, PrefabPath = $"assets/custom/{name}.prefab", SkinID = skin };
                    processedCount++;
                    
                    if (configData.debug.DebugKaruzaVehicles)
                    {
                        Puts($"[DebugKaruzaVehicles] Added catalog entry - Name: {name}, PrefabPath: assets/custom/{name}.prefab, SkinID: {skin}");
                    }
                }
                
                if (configData.debug.DebugKaruzaVehicles)
                {
                    Puts($"[DebugKaruzaVehicles] LoadKaruzaCatalog completed - Processed: {processedCount}, Total entries: {_catalogByName.Count}");
                }
                else
                {
                    Puts($"[CustomVehicles] Catalog entries: {_catalogByName.Count}");
                }
            }
            catch (Exception ex)
            {
                if (configData.debug.DebugKaruzaVehicles)
                {
                    PrintWarning($"[DebugKaruzaVehicles] Catalog read failed: {ex}");
                }
                else
                {
                    PrintWarning($"[CustomVehicles] Catalog read failed: {ex}");
                }
            }
        }

        #endregion Karuza Catalog Loader

        #region Dynamic Vehicle Detection

        private void RunDiscoveryAndPromote(Action onComplete = null)
        {
            LoadKaruzaCatalog();
            discoveredCustomVehicles.Clear();
            DiscoverCustomVehiclesFromFolders();

            void finish()
            {
                AddDiscoveredCustomVehiclesToSettings();
                MapCustomCommandsFromConfig();
                PrintWarning($"[CustomVehicles] Discovery complete: {discoveredCustomVehicles.Count} entries processed.");
                try
                {
                    onComplete?.Invoke();
                }
                catch (Exception ex)
                {
                    PrintWarning($"[CustomVehicles] Discovery callback error: {ex.Message}");
                }
            }

            if (!TryGetKaruzaEntitiesApiCredentials(out var apiPath, out var apiId, out var apiSecret))
            {
                finish();
                return;
            }

            Puts("[CustomVehicles] Karuza API configured — loading entity lists (same IDs as RustPlane=1, RustHelicopter=2, RustCar=3).");

            // Sequential requests: same outcome as parallel, simpler than counting callbacks.
            int[] entityTypeIds = { KaruzaApiEntityTypePlane, KaruzaApiEntityTypeHelicopter, KaruzaApiEntityTypeCar };
            string[] labels = { "plane", "helicopter", "car" };

            void fetchKaruzaEntityIndex(int i)
            {
                if (i >= entityTypeIds.Length)
                {
                    finish();
                    return;
                }

                RequestKaruzaEntityList(apiPath, apiId, apiSecret, entityTypeIds[i], (code, response) =>
                {
                    if (code != 200 || string.IsNullOrEmpty(response))
                        PrintWarning($"[CustomVehicles] Karuza API {labels[i]} request failed (HTTP {code}).");
                    else
                    {
                        try
                        {
                            MergeKaruzaApiEntityList(response);
                        }
                        catch (Exception ex)
                        {
                            PrintWarning($"[CustomVehicles] Karuza API {labels[i]} parse error: {ex.Message}");
                        }
                    }

                    fetchKaruzaEntityIndex(i + 1);
                });
            }

            fetchKaruzaEntityIndex(0);
        }

        private void DiscoverCustomVehiclesFromFolders()
        {
            var folderPaths = new[]
            {
                Path.Combine(Interface.Oxide.ConfigDirectory, "RustCar"),
                Path.Combine(Interface.Oxide.ConfigDirectory, "RustHelicopter"),
                Path.Combine(Interface.Oxide.ConfigDirectory, "RustPlane")
            };

            foreach (var folderPath in folderPaths)
            {
                foreach (var config in EnumerateConfigs(folderPath))
                {
                    if (!string.IsNullOrEmpty(config.PrefabPath))
                    {
                        discoveredCustomVehicles[config.Name] = new CustomVehicleInfo
                        {
                            PrefabPath = config.PrefabPath,
                            SkinID = config.SkinID
                        };
                    }
                }
            }
        }

        private void AddDiscoveredCustomVehiclesToSettings()
        {
            if (configData.customVehicles == null)
            {
                configData.customVehicles = new CustomVehicleSettings();
            }

            foreach (var entry in discoveredCustomVehicles)
            {
                var vehicleName = entry.Key;
                var vehicleInfo = entry.Value;
                var keyBase = vehicleName.ToLower();

                var settings = CreateBlankTemplateForCustom(vehicleName, vehicleInfo.PrefabPath, vehicleInfo.SkinID, keyBase);
                
                configData.customVehicles[vehicleName] = settings;
                
                allVehicleSettings[vehicleName] = settings;
                commandToVehicleType[keyBase] = vehicleName;
                
                RegisterCustomVehiclePermissions(vehicleName, settings);
            }
        }

        private BaseVehicleSettings CreateBlankTemplateForCustom(string displayName, string prefabPath, ulong skinId, string keyBase)
        {
            var settings = prefabPath.Contains("helicopter") || prefabPath.Contains("plane") || prefabPath.Contains("copter")
                ? new MiniCopterSettings()
                : new SedanSettings() as BaseVehicleSettings;

            settings.Purchasable = true;
            settings.DisplayName = displayName;
            settings.PrefabPath = prefabPath;
            settings.SkinID = skinId;
            settings.Distance = 10;
            settings.MinDistanceForPlayers = 3;
            settings.UsePermission = true;
            settings.Permission = $"RustVehicles.{keyBase}";
            settings.BypassCostPermission = $"RustVehicles.{keyBase}free";
            settings.Commands = new List<string> { keyBase };
            settings.PurchasePrices = new Dictionary<string, PriceInfo>
            {
                ["scrap"] = new PriceInfo { amount = 10000, displayName = "Scrap" }
            };
            settings.SpawnCooldown = 450;
            settings.RecallCooldown = 30;
            settings.CooldownPermissions = new Dictionary<string, CooldownPermission>();
            settings.NoDamage = false;
            settings.NoCollisionDamage = false;
            settings.RemoveLicenseOnceCrash = false;
            settings.WipeTime = 0;
            settings.MaxSpeed = 0;

            return settings;
        }
        private void MapCustomCommandsFromConfig()
        {
            if (configData.customVehicles != null)
            {
                foreach (var entry in configData.customVehicles)
                {
                    var vehicleName = entry.Key;
                    var settings = entry.Value;
                    
                    foreach (var command in settings.Commands)
                    {
                        if (!string.IsNullOrEmpty(command))
                        {
                            var commandLower = command.ToLower();
                            if (!commandToVehicleType.ContainsKey(commandLower))
                            {
                                commandToVehicleType[commandLower] = vehicleName;
                            }
                            else
                            {
                                var existingVehicle = commandToVehicleType[commandLower];
                                PrintError($"Duplicate command '{commandLower}' - already maps to '{existingVehicle}', trying to map to '{vehicleName}'");
                            }
                        }
                    }
                }
            }
        }

        [ChatCommand("vldiscover")]
        internal void CmdDiscoverCustomVehicles(BasePlayer player, string command, string[] args)
        {
            if (player == null || !HasAdminPermission(player))
            {
                Print(player, "No permission.");
                return;
            }

            Print(player, "Starting custom vehicle discovery (folders + Karuza API if configured)…");
            RunDiscoveryAndPromote(() =>
            {
                Print(player, $"Custom vehicle discovery complete: {discoveredCustomVehicles.Count} entries processed.");
            });
        }
        
        #endregion Dynamic Vehicle Detection 

        #region Custom Vehicle Config Loading

        private sealed class CustomVehicleConfig
        {
            public string Name { get; set; }
            public string PrefabPath { get; set; }
            public ulong SkinID { get; set; } = 0;
        }



        private IEnumerable<CustomVehicleConfig> EnumerateConfigs(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                yield break;

            foreach (var path in Directory.GetFiles(folderPath, "*.json"))
            {
                CustomVehicleConfig config = null;

                try
                {
                    var json = File.ReadAllText(path);
                    config = JsonConvert.DeserializeObject<CustomVehicleConfig>(json);
                }
                catch (Exception ex)
                {
                    PrintWarning($"[RustVehicles] Bad JSON in {Path.GetFileName(path)}: {ex.Message}");
                    continue;
                }

                if (string.IsNullOrEmpty(config?.Name))
                {
                    var fileName = Path.GetFileNameWithoutExtension(path);
                    if (config == null)
                        config = new CustomVehicleConfig();
                    config.Name = fileName;
                }

                if (string.IsNullOrEmpty(config.PrefabPath))
                {
                    if (_catalogByName != null
                        && _catalogByName.TryGetValue(config.Name, out var cat)
                        && !string.IsNullOrEmpty(cat.PrefabPath))
                    {
                        config.PrefabPath = cat.PrefabPath;
                        config.SkinID = cat.SkinID; 
                    }
                    else
                    {
                        PrintWarning($"[RustVehicles] Skipping {config.Name} — PrefabPath missing and not found in catalog.");
                        continue;
                    }
                }

                yield return config;
            }
        }



        private void RegisterCustomVehiclePermissions(string vehicleName, BaseVehicleSettings settings)
        {
            if (!permission.PermissionExists(settings.Permission, this))
            {
                permission.RegisterPermission(settings.Permission, this);
            }

            if (!permission.PermissionExists(settings.BypassCostPermission, this))
            {
                permission.RegisterPermission(settings.BypassCostPermission, this);
            }

            if (settings.CooldownPermissions != null)
            {
                foreach (var cooldownPerm in settings.CooldownPermissions)
                {
                    if (!permission.PermissionExists(cooldownPerm.Key, this))
                    {
                        permission.RegisterPermission(cooldownPerm.Key, this);
                    }
                }
            }
        }

        #endregion

        #region Configuration Retrieval

        private BaseVehicleSettings GetBaseVehicleSettings(string vehicleType)
        {
            BaseVehicleSettings settings;
            if (allVehicleSettings.TryGetValue(vehicleType, out settings))
            {
                return settings;
            }
            
            // Direct config access for normal vehicles during initialization
            switch (vehicleType)
            {
                case "Tugboat":
                    return configData?.normalVehicles?.tugboat;
                case "Rowboat":
                    return configData?.normalVehicles?.rowboat;
                case "RHIB":
                    return configData?.normalVehicles?.rhib;
                case "Sedan":
                    return configData?.normalVehicles?.sedan;
                case "HotAirBalloon":
                    return configData?.normalVehicles?.hotAirBalloon;
                case "ArmoredHotAirBalloon":
                    return configData?.normalVehicles?.armoredHotAirBalloon;
                case "MiniCopter":
                    return configData?.normalVehicles?.miniCopter;
                case "AttackHelicopter":
                    return configData?.normalVehicles?.attackHelicopter;
                case "TransportHelicopter":
                    return configData?.normalVehicles?.transportHelicopter;
                case "Chinook":
                    return configData?.normalVehicles?.chinook;
                case "RidableHorse":
                    return configData?.normalVehicles?.ridableHorse;
                case "WorkCart":
                    return configData?.normalVehicles?.workCart;
                case "SedanRail":
                    return configData?.normalVehicles?.sedanRail;
                case "MagnetCrane":
                    return configData?.normalVehicles?.magnetCrane;
                case "SubmarineSolo":
                    return configData?.normalVehicles?.submarineSolo;
                case "SubmarineDuo":
                    return configData?.normalVehicles?.submarineDuo;
                case "Snowmobile":
                    return configData?.normalVehicles?.snowmobile;
                case "PedalBike":
                    return configData?.normalVehicles?.pedalBike;
                case "PedalTrike":
                    return configData?.normalVehicles?.pedalTrike;
                case "MotorBike":
                    return configData?.normalVehicles?.motorBike;
                case "MotorBike_SideCar":
                    return configData?.normalVehicles?.motorBikeSidecar;
                case "Kayak":
                    return configData?.normalVehicles?.Kayak;
                case "Dpv":
                    return configData?.normalVehicles?.dpv;
                case "SiegeTower":
                    return configData?.normalVehicles?.siegeTower;
                case "Catapult":
                    return configData?.normalVehicles?.catapult;
                case "Batteringram":
                    return configData?.normalVehicles?.batteringram;
                case "Ballista":
                    return configData?.normalVehicles?.ballista;
                default:
                    return null;
            }
        }
        private bool GetBaseVehicleCollisionDamage(string vehicleType)
        {
            BaseVehicleSettings settings;
            return allVehicleSettings.TryGetValue(vehicleType, out settings) && settings.NoCollisionDamage;
        }




        private bool GetBaseVehicleDamage(string vehicleType)
        {
            BaseVehicleSettings settings;
            return allVehicleSettings.TryGetValue(vehicleType, out settings) && settings.NoDamage;
        }




        #endregion GetSettings

        #region Permission

        private bool HasAdminPermission(BasePlayer player)
        {
            return permission.UserHasPermission(player.UserIDString, PERMISSION_ADMIN);
        }

        private bool CanViewVehicleInfo(BasePlayer player, string vehicleType, BaseVehicleSettings settings)
        {
            if (settings.Purchasable && settings.Commands.Count > 0)
            {
                return HasVehiclePermission(player, vehicleType);
            }
            return false;
        }

        private bool HasVehiclePermission(BasePlayer player, string vehicleType)
        {
            if (string.IsNullOrEmpty(vehicleType))
                return false;

            string resolvedType;
            if (!commandToVehicleType.TryGetValue(vehicleType, out resolvedType))
            {
                resolvedType = vehicleType;
            }

            var settings = GetBaseVehicleSettings(resolvedType);
            if (settings == null)
            {
                return false;
            }
            if (!settings.UsePermission)
            {
                return true;
            }
            if (string.IsNullOrEmpty(settings.Permission))
            {
                return false;
            }
            var hasAll = permission != null && permission.UserHasPermission(player.UserIDString, PERMISSION_ALL);
            var hasSpecific = permission != null && permission.UserHasPermission(player.UserIDString, settings.Permission);
            return hasAll || hasSpecific;
        }
        [HookMethod("API_CanUseLicensedVehicle")]
        public bool API_CanUseLicensedVehicle(BasePlayer player, string vehicleType)
        {
            if (player == null || string.IsNullOrEmpty(vehicleType))
                return false;

            return HasVehiclePermission(player, vehicleType);
        }

        [HookMethod("API_CanSpawnLicensedVehicle")]
        public object API_CanSpawnLicensedVehicle(BasePlayer player, string vehicleType)
        {
            if (player == null || string.IsNullOrEmpty(vehicleType))
                return "Invalid player or vehicle";

            var settings = GetBaseVehicleSettings(vehicleType);
            OwnedVehicle vehicle;
            if (!vehicleDatabase.IsVehiclePurchased(player.userID, vehicleType, out vehicle))
            {
                if (!permission.UserHasPermission(player.UserIDString, PERMISSION_BYPASS_COST))
                    return Lang("VehicleNotYetPurchased", player.UserIDString, settings?.DisplayName ?? vehicleType, configData.chat.buyCommand);
                vehicle = OwnedVehicle.New(player.userID, vehicleType);
            }

            string reason;
            var position = Vector3.zero;
            var rotation = Quaternion.identity;
            if (!CanSpawn(player, vehicle, false, "api", out reason, ref position, ref rotation))
                return reason ?? "Spawn blocked";

            return null;
        }

        [HookMethod("API_CanRecallLicensedVehicle")]
        public object API_CanRecallLicensedVehicle(BasePlayer player, string vehicleType)
        {
            if (player == null || string.IsNullOrEmpty(vehicleType))
                return "Invalid player or vehicle";

            var settings = GetBaseVehicleSettings(vehicleType);
            OwnedVehicle vehicle;
            if (!vehicleDatabase.IsVehiclePurchased(player.userID, vehicleType, out vehicle))
                return Lang("VehicleNotYetPurchased", player.UserIDString, settings?.DisplayName ?? vehicleType, configData.chat.buyCommand);

            string reason;
            var position = Vector3.zero;
            var rotation = Quaternion.identity;
            if (!CanRecall(player, vehicle, false, "api", out reason, ref position, ref rotation))
                return reason ?? "Recall blocked";

            return null;
        }

        #endregion Permission

        #region Claim

        private void TryClaimVehicle(BaseVehicle baseVehicle)
        {
            NextTick(() =>
            {
                if (baseVehicle == null)
                {
                    return;
                }
                var player = baseVehicle.creatorEntity as BasePlayer;
                var allowClaim = player != null && player.userID.IsSteamId() && baseVehicle.OnlyOwnerAccessible();

                if (!allowClaim && configData?.global != null && configData.global.autoClaimFromVendor)
                {
                    var ownerId = baseVehicle.OwnerID;
                    if (ownerId.IsSteamId())
                    {
                        var ownerPlayer = BasePlayer.FindByID(ownerId);
                        if (ownerPlayer != null)
                        {
                            player = ownerPlayer;
                            allowClaim = true;
                        }
                    }
                }

                if (!allowClaim)
                {
                    return;
                }
                var vehicleType = GetClaimableVehicleType(baseVehicle);
                if (!string.IsNullOrEmpty(vehicleType))
                {
                    TryClaimVehicle(player, baseVehicle, vehicleType);
                }
            });
        }

        private void TryClaimVehicle(BaseVehicle baseVehicle, BasePlayer player)
        {
            if (player == null || !player.userID.IsSteamId())
            {
                return;
            }
            var vehicleType = GetClaimableVehicleType(baseVehicle);
            if (!string.IsNullOrEmpty(vehicleType))
            {
                TryClaimVehicle(player, baseVehicle, vehicleType);
            }
        }

        private bool TryClaimVehicle(BasePlayer player, BaseEntity entity, string vehicleType)
        {
            OwnedVehicle vehicle;
            if (!vehicleDatabase.IsVehiclePurchased(player.userID, vehicleType, out vehicle))
            {
                if (!configData.global.autoUnlockFromVendor)
                {
                    return false;
                }

                vehicleDatabase.AddVehicleLicense(player.userID, vehicleType);
                vehicle = vehicleDatabase.GetVehicleLicense(player.userID, vehicleType);
            }
            if (vehicle.Entity == null || vehicle.Entity.IsDestroyed)
            {
                var settings = GetBaseVehicleSettings(vehicle.VehicleType);
                if (settings != null)
                {
                    settings.PreSetupVehicle(entity, vehicle, player);
                    settings.SetupVehicle(entity, vehicle, player, false);
                }
                CacheVehicleEntity(entity, vehicle, player);
                return true;
            }
            return false;
        }

        #endregion Claim

        private bool TryGetVehicle(BaseEntity entity, out OwnedVehicle vehicle)
        {
            if (!vehiclesCache.TryGetValue(entity, out vehicle))
            {
                var vehicleModule = entity as BaseVehicleModule;
                if (vehicleModule == null)
                {
                    return false;
                }
                var parent = vehicleModule.Vehicle;
                if (parent == null || !vehiclesCache.TryGetValue(parent, out vehicle))
                {
                    return false;
                }
            }
            return true;
        }

        private IEnumerator DelayedUpdatePlayerData(double currentTimestamp)
        {
            yield return CoroutineEx.waitForSeconds(30f);
            yield return UpdatePlayerData(currentTimestamp);
        }

        private IEnumerator UpdatePlayerData(double currentTimestamp)
        {
            foreach (var playerData in vehicleDatabase.playerData)
            {
                foreach (var entry in playerData.Value)
                {
                    entry.Value.PlayerId = playerData.Key;
                    entry.Value.VehicleType = entry.Key;
                    if (configData.global.storeVehicle)
                    {
                        entry.Value.LastRecall = entry.Value.LastDismount = currentTimestamp;
                        if (entry.Value.EntityId == 0)
                        {
                            continue;
                        }
                        NetworkableId id = new NetworkableId(entry.Value.EntityId);
                        entry.Value.Entity = BaseNetworkable.serverEntities.Find(id) as BaseEntity;
                        if (entry.Value.Entity == null || entry.Value.Entity.IsDestroyed)
                        {
                            entry.Value.EntityId = 0;
                        }
                        else
                        {
                            vehiclesCache.TryAdd(entry.Value.Entity, entry.Value);
                            ReleaseVanillaOwnerLock(entry.Value.Entity);
                            if (entry.Value.Entity is Tugboat)
                            {
                                Tugboat vehicle = entry.Value.Entity as Tugboat;
                                vehicle.engineThrust = TUGBOAT_ENGINETHRUST * configData.normalVehicles.tugboat.speedMultiplier;
                            }
                            else if (entry.Value.Entity is ScrapTransportHelicopter)
                            {
                                ScrapTransportHelicopter vehicle = entry.Value.Entity as ScrapTransportHelicopter;
                                vehicle.liftFraction = configData.normalVehicles.transportHelicopter.liftFraction;
                                vehicle.torqueScale = SCRAP_HELICOPTER_TORQUE * configData.normalVehicles.transportHelicopter.rotationScale;
                            }
                            else if (entry.Value.Entity is Minicopter)
                            {
                                Minicopter vehicle = entry.Value.Entity as Minicopter;
                                vehicle.liftFraction = configData.normalVehicles.miniCopter.liftFraction;
                                vehicle.torqueScale = MINICOPTER_TORQUE * configData.normalVehicles.miniCopter.rotationScale;
                            }
                            else if (entry.Value.Entity is AttackHelicopter)
                            {
                                AttackHelicopter vehicle = entry.Value.Entity as AttackHelicopter;
                                vehicle.liftFraction = configData.normalVehicles.attackHelicopter.liftFraction;
                                vehicle.torqueScale = ATTACK_HELICOPTER_TORQUE * configData.normalVehicles.attackHelicopter.rotationScale;
                            }
                        }
                    }
                    yield return CoroutineEx.waitForSeconds(0.3f);
                }
            }
            finishedLoading = true;

            if (CustomEntities != null && CustomEntities.IsLoaded)
            {
                ServerMgr.Instance.StartCoroutine(ValidateCustomVehiclesFromStorage());
            }
        }
        
        private IEnumerator ValidateCustomVehiclesFromStorage()
        {
            if (CustomEntities == null || !CustomEntities.IsLoaded)
            {
                if (configData.debug.DebugInit)
                {
                    Puts("[DebugInit] ValidateCustomVehiclesFromStorage: CustomEntities not loaded");
                }
                yield break;
            }
            
            if (configData.debug.DebugInit)
            {
                Puts("[DebugInit] ValidateCustomVehiclesFromStorage: Starting validation");
            }
            

            var vehiclesToValidate = new List<(ulong PlayerID, string VehicleType, OwnedVehicle Vehicle, string PrefabPath)>();
            
            foreach (var playerData in vehicleDatabase.playerData)
            {
                foreach (var vehicleEntry in playerData.Value)
                {
                    var vehicle = vehicleEntry.Value;
                    

                    if (vehicle.EntityId != 0) continue;
                    

                    bool isCustomVehicle = allVehicleSettings.TryGetValue(vehicleEntry.Key, out var settings) && 
                        (settings.CustomVehicle || (!string.IsNullOrEmpty(settings.PrefabPath) && settings.PrefabPath.Contains("assets/custom/")));
                    
                    if (!isCustomVehicle) continue;
                    
                    vehiclesToValidate.Add((vehicle.PlayerId, vehicleEntry.Key, vehicle, settings?.PrefabPath ?? ""));
                }
            }
            
            if (vehiclesToValidate.Count == 0)
            {
                if (configData.debug.DebugInit)
                {
                    Puts("[DebugInit] ValidateCustomVehiclesFromStorage: No vehicles with entityID: 0 to validate");
                }
                yield break;
            }
            
            if (configData.debug.DebugInit)
            {
                Puts($"[DebugInit] ValidateCustomVehiclesFromStorage: Found {vehiclesToValidate.Count} custom vehicles with entityID: 0 to validate");
            }
            

            var customEntitiesType = CustomEntities.GetType();
            var binaryDataType = customEntitiesType.GetNestedType("BinaryData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            
            if (binaryDataType == null)
            {
                if (configData.debug.DebugInit)
                {
                    Puts("[DebugInit] ValidateCustomVehiclesFromStorage: Failed to get BinaryData type");
                }
                yield break;
            }
            
            var cacheField = binaryDataType.GetField("_cacheByOwner", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (cacheField == null)
            {
                if (configData.debug.DebugInit)
                {
                    Puts("[DebugInit] ValidateCustomVehiclesFromStorage: Failed to get _cacheByOwner field");
                }
                yield break;
            }
            
            var cacheByOwner = cacheField.GetValue(null) as System.Collections.IDictionary;
            if (cacheByOwner == null)
            {
                if (configData.debug.DebugInit)
                {
                    Puts("[DebugInit] ValidateCustomVehiclesFromStorage: cacheByOwner is null");
                }
                yield break;
            }
            
            var saveListField = binaryDataType.GetField("CustomEntitySaveList", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (saveListField == null)
            {
                if (configData.debug.DebugInit)
                {
                    Puts("[DebugInit] ValidateCustomVehiclesFromStorage: Failed to get CustomEntitySaveList field");
                }
                yield break;
            }
            

            var tempCache = new Dictionary<(ulong OwnerID, string VehicleType), BaseEntity>();
            var playersToCheck = new HashSet<ulong>();
            foreach (var vehicleInfo in vehiclesToValidate)
            {
                playersToCheck.Add(vehicleInfo.PlayerID);
            }
            
             foreach (System.Collections.DictionaryEntry entry in cacheByOwner)
            {
                var plugin = entry.Key as Plugin;
                var binaryData = entry.Value;
                if (binaryData == null) continue;
                
                var saveList = saveListField.GetValue(binaryData) as System.Collections.IList;
                if (saveList == null) continue;
                
                foreach (BaseEntity entity in saveList)
                {
                    if (entity == null || entity.IsDestroyed) continue;
                    if (entity.PrefabName == null || !entity.PrefabName.Contains("assets/custom/")) continue;
                    if (entity.OwnerID == 0) continue;
                    

                    ulong ownerId = entity.OwnerID;
                    if (!playersToCheck.Contains(ownerId)) continue;
                    
                    string entityPrefab = entity.PrefabName;
                    
                    foreach (var vehicleInfo in vehiclesToValidate)
                    {
                        if (vehicleInfo.PlayerID != ownerId) continue;
                        if (string.IsNullOrEmpty(vehicleInfo.PrefabPath)) continue;
                        
                        string normalizedEntityPrefab = entityPrefab.Replace("assets/custom/", "").Replace(".prefab", "").ToLower();
                        string normalizedVehiclePrefab = vehicleInfo.PrefabPath.Replace("assets/custom/", "").Replace(".prefab", "").ToLower();
                        
                        if (normalizedEntityPrefab.Contains(normalizedVehiclePrefab) || normalizedVehiclePrefab.Contains(normalizedEntityPrefab))
                        {
                            var key = (ownerId, vehicleInfo.VehicleType);
                            if (!tempCache.ContainsKey(key))
                            {
                                tempCache[key] = entity;
                                
                                if (configData.debug.DebugInit)
                                {
                                    Puts($"[DebugInit] ValidateCustomVehiclesFromStorage: Cached entity - OwnerID: {ownerId}, VehicleType: {vehicleInfo.VehicleType}, EntityID: {entity.net.ID.Value}");
                                }
                            }
                            break; 
                        }
                    }
                }
            }
            

            int validatedCount = 0;
            int vehiclesChecked = 0;
            
            foreach (var vehicleInfo in vehiclesToValidate)
            {
                vehiclesChecked++;
                
                if (configData.debug.DebugInit)
                {
                    Puts($"[DebugInit] ValidateCustomVehiclesFromStorage: Checking vehicle - PlayerID: {vehicleInfo.PlayerID}, VehicleType: {vehicleInfo.VehicleType}, PrefabPath: {vehicleInfo.PrefabPath}");
                }
                

                var key = (vehicleInfo.PlayerID, vehicleInfo.VehicleType);
                if (tempCache.TryGetValue(key, out var foundEntity))
                {

                    vehicleInfo.Vehicle.EntityId = foundEntity.net.ID.Value;
                    vehicleInfo.Vehicle.Entity = foundEntity;
                    vehicleInfo.Vehicle.LastDeath = 0;

                    vehiclesCache[foundEntity] = vehicleInfo.Vehicle;
                    validatedCount++;
                    
                    Puts($"[DebugInit] Validated custom vehicle - PlayerID: {vehicleInfo.PlayerID}, VehicleType: {vehicleInfo.VehicleType}, Restored EntityId: {vehicleInfo.Vehicle.EntityId}");
                }
                else if (configData.debug.DebugInit)
                {
                    Puts($"[DebugInit] ValidateCustomVehiclesFromStorage: Vehicle NOT found in CustomEntities - PlayerID: {vehicleInfo.PlayerID}, VehicleType: {vehicleInfo.VehicleType}");
                }
                
                yield return null;
            }
            

            tempCache.Clear();
            
            if (configData.debug.DebugInit)
            {
                Puts($"[DebugInit] ValidateCustomVehiclesFromStorage: Validation complete - Checked {vehiclesChecked} vehicles, Validated {validatedCount}");
            }
            

            if (validatedCount > 0)
            {
                SaveData(); 
                Puts($"Validated {validatedCount} custom vehicles from CustomEntities storage");
            }
        }



        #region Utility Functions

        private static string GetClaimableVehicleType(BaseVehicle baseVehicle)
        {
            if (baseVehicle is Tugboat) return "Tugboat";
            if (baseVehicle is ScrapTransportHelicopter) return "TransportHelicopter";
            if (baseVehicle is Minicopter) return "MiniCopter";
            if (baseVehicle is AttackHelicopter) return "AttackHelicopter";
            if (baseVehicle is RHIB) return "RHIB";
            if (baseVehicle is MotorRowboat) return "Rowboat";
            if (baseVehicle is SubmarineDuo) return "SubmarineDuo";
            if (baseVehicle is BaseSubmarine) return "SubmarineSolo";
            if (baseVehicle is Kayak) return "Kayak";
            if (baseVehicle is BaseVehicle) return "RidableHorse";
            return null;
        }

        private static string GetVehicleTypeFromEntity(BaseEntity entity)
        {
            if (entity == null) return null;
            if (entity is Minicopter) return "MiniCopter";
            if (entity is AttackHelicopter) return "AttackHelicopter";
            if (entity is ScrapTransportHelicopter) return "TransportHelicopter";
            if (entity is RidableHorse) return "RidableHorse";
            if (entity is RHIB) return "RHIB";
            if (entity is MotorRowboat) return "Rowboat";
            if (entity is Tugboat) return "Tugboat";
            if (entity is SubmarineDuo) return "SubmarineDuo";
            if (entity is BaseSubmarine) return "SubmarineSolo";
            if (entity is Kayak) return "Kayak";
            return null;
        }

        private static bool GetDismountPosition(BaseVehicle baseVehicle, BasePlayer player, out Vector3 result)
        {
            var parentVehicle = baseVehicle.VehicleParent();
            if (parentVehicle != null)
            {
                return GetDismountPosition(parentVehicle, player, out result);
            }
            var list = Pool.Get<List<Vector3>>();
            foreach (var transform in baseVehicle.dismountPositions)
            {
                if (baseVehicle.ValidDismountPosition(player, transform.position))
                {
                    list.Add(transform.position);
                    if (baseVehicle.dismountStyle == BaseVehicle.DismountStyle.Ordered)
                    {
                        break;
                    }
                }
            }
            if (list.Count == 0)
            {
                result = Vector3.zero;
                Pool.FreeUnmanaged(ref list);
                return false;
            }
            var pos = player.transform.position;
            list.Sort((a, b) => Vector3.Distance(a, pos).CompareTo(Vector3.Distance(b, pos)));
            result = list[0];
            Pool.FreeUnmanaged(ref list);
            return true;
        }

        private static bool VehicleAnyMounted(BaseEntity entity)
        {
            var baseVehicle = entity as BaseVehicle;
            if (baseVehicle != null && baseVehicle.AnyMounted())
            {
                return true;
            }
            return entity.GetComponentsInChildren<BasePlayer>()?.Length > 0;
        }

        private static void DismountAllPlayers(BaseEntity entity)
        {
            var baseVehicle = entity as BaseVehicle;
            if (baseVehicle != null)
            {
                foreach (var mountPointInfo in baseVehicle.allMountPoints)
                {
                    if (mountPointInfo != null && mountPointInfo.mountable != null)
                    {
                        var mounted = mountPointInfo.mountable.GetMounted();
                        if (mounted != null)
                        {
                            mountPointInfo.mountable.DismountPlayer(mounted);
                        }
                    }
                }
            }
            var players = entity.GetComponentsInChildren<BasePlayer>();
            foreach (var player in players)
            {
                player.SetParent(null, true, true);
            }
        }

        private static Vector3 GetGroundPositionLookingAt(BasePlayer player, float distance, bool isWaterVehicle, bool needUp = true)
        {
            RaycastHit hitInfo;
            var headRay = player.eyes.HeadRay();

            if (Physics.Raycast(headRay, out hitInfo, distance, LAYER_GROUND))
            {
                float heightOffset = 0f;

                if (hitInfo.collider.gameObject.layer == LayerMask.NameToLayer("Construction"))
                {
                    heightOffset = 3f;
                }
                else if (!isWaterVehicle)
                {
                    heightOffset = 2f;
                }

                return hitInfo.point + Vector3.up * heightOffset;
            }

            Vector3 groundPosition = GetGroundPosition(headRay.origin + headRay.direction * distance, isWaterVehicle, needUp);

            if (!isWaterVehicle)
            {
                groundPosition.y += 2f;
            }

            return groundPosition;
        }

        private static Vector3 GetGroundPosition(Vector3 position, bool isWaterVehicle, bool needUp = true)
        {
            RaycastHit hitInfo;
            position.y = Physics.Raycast(needUp ? position + Vector3.up * 250 : position, Vector3.down, out hitInfo, needUp ? 400f : 50f, LAYER_GROUND)
                ? hitInfo.point.y
                : TerrainMeta.HeightMap.GetHeight(position);

            if (!isWaterVehicle)
            {
                position.y += 2f;
            }
            return position;
        }

        private static bool IsInWater(Vector3 position)
        {
            var colliders = Pool.Get<List<Collider>>();
            Vis.Colliders(position, 0.5f, colliders);
            bool flag = false;
            foreach (var collider in colliders)
            {
                if (collider.gameObject.layer == (int)Layer.Water)
                {
                    flag = true;
                    break;
                }
            }
            Pool.FreeUnmanaged(ref colliders);
            return flag || WaterLevel.Test(position, false, false);
        }

        private static void MoveToPosition(BasePlayer player, Vector3 position)
        {
            player.Teleport(position);
            player.ForceUpdateTriggers();
            player.SendNetworkUpdateImmediate();
        }
        private void AuthAlliesOnTugboat(Tugboat tug, BasePlayer player)
        {
            VehiclePrivilege vehiclePrivilege = null;
            foreach (BaseEntity child in tug.children)
            {
                vehiclePrivilege = child as VehiclePrivilege;
                if (vehiclePrivilege != null)
                {
                    break;
                }
            }

            if (vehiclePrivilege == null)
            {
                return;
            }

            AuthPlayerOnVehiclePrivilege(vehiclePrivilege, player);

            if (configData.global.useTeams)
            {
                RelationshipManager.PlayerTeam team = RelationshipManager.ServerInstance.FindPlayersTeam(player.userID);
                if (team != null)
                {
                    foreach (ulong id in team.members)
                    {
                        AuthPlayerOnVehiclePrivilege(vehiclePrivilege, BasePlayer.FindByID(id));
                    }
                }
            }

            if (configData.global.useClans)
            {
                AuthClanOnVehiclePrivilege(vehiclePrivilege, player);
            }
        }

        private static void AuthPlayerOnVehiclePrivilege(VehiclePrivilege vehiclePrivilege, BasePlayer player)
        {
            if (player != null)
            {
                vehiclePrivilege.AddPlayer(player);
            }
        }

        private void AuthClanOnVehiclePrivilege(VehiclePrivilege vehiclePrivilege, BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            IClan clan = player.serverClan;
            var backend = ClanManager.ServerInstance?.Backend;
            if (clan == null && player.clanId != 0L && backend != null)
            {
                backend.TryGet(player.clanId, out clan);
            }

            if (clan?.Members != null)
            {
                for (int i = 0; i < clan.Members.Count; i++)
                {
                    AuthPlayerOnVehiclePrivilege(vehiclePrivilege, BasePlayer.FindByID(clan.Members[i].SteamId));
                }

                if (clan.Creator != 0UL)
                {
                    AuthPlayerOnVehiclePrivilege(vehiclePrivilege, BasePlayer.FindByID(clan.Creator));
                }
            }
        }
        #region Train Car

        #endregion

        #endregion Karuza Catalog Loader

        private BaseVehicleSettings CreateCustomVehicleSettings(CustomVehicleConfig config, string category)
        {
            if (config == null)
                return null;

            BaseVehicleSettings baseSettings;

            switch (category)
            {
                case "Car":
                    baseSettings = new SedanSettings();
                    break;

                case "Helicopter":
                    baseSettings = new MiniCopterSettings();
                    break;

                case "Plane":
                    baseSettings = new MiniCopterSettings();
                    break;

                default:
                    baseSettings = new MiniCopterSettings();
                    break;
            }

            if (baseSettings == null)
                return null;

            var slug = config.Name?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(slug))
                return null;

            baseSettings.Purchasable = true;
            baseSettings.DisplayName = config.Name;
            baseSettings.Distance = 10;
            baseSettings.MinDistanceForPlayers = 3;

            baseSettings.UsePermission = true;
            baseSettings.Permission = $"RustVehicles.{slug}";
            baseSettings.BypassCostPermission = $"RustVehicles.{slug}.free";
            baseSettings.Commands = new List<string> { slug };

            baseSettings.PurchasePrices = new Dictionary<string, PriceInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["scrap"] = new PriceInfo { amount = 1000, displayName = "Scrap" }
            };

            baseSettings.SpawnCooldown = 300;
            baseSettings.RecallCooldown = 30;
            baseSettings.CooldownPermissions = new Dictionary<string, CooldownPermission>(StringComparer.OrdinalIgnoreCase)
            {
                ["RustVehicles.vip"] = new CooldownPermission
                {
                    spawnCooldown = 150,
                    recallCooldown = 10
                }
            };

            if (!string.IsNullOrEmpty(config.PrefabPath))
            {
                if (baseSettings is MiniCopterSettings heli)
                {
                    heli.PrefabPath = config.PrefabPath;
                    if (config.SkinID > 0) heli.SkinID = config.SkinID;
                }
                else if (baseSettings is SedanSettings car)
                {
                    car.PrefabPath = config.PrefabPath;
                    if (config.SkinID > 0) car.SkinID = config.SkinID;
                }
            }

            return baseSettings;
        }

        private BaseVehicleSettings CreateCustomVehicleSettings(CustomVehicleConfig config)
        {
            return CreateCustomVehicleSettings(config, "Helicopter");
        }


        #endregion GetSettings

        #region Commands

        #region Unified Command Handler

        internal void CmdUniversal(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_USE))
            {
                Print(player, Lang("NotAllowed", player.UserIDString));
                return;
            }

            string vehicleType;
            if (IsValidOption(player, command, out vehicleType))
            {
                var bypassCooldown = args.Length > 0 && IsValidBypassCooldownOption(args[0]);
                HandleUniversalCmd(player, vehicleType, bypassCooldown, command);
            }
        }

        private void HandleUniversalCmd(BasePlayer player, string vehicleType, bool bypassCooldown, string command)
        {
            if (!finishedLoading)
            {
                Print(player, Lang("PleaseWait", player.UserIDString));
                return;
            }


            OwnedVehicle vehicle;

            string reason;
            var position = Vector3.zero;
            var rotation = Quaternion.identity;
            if (vehicleDatabase.IsVehiclePurchased(player.userID, vehicleType, out vehicle))
            {
                if (vehicle.Entity != null && !vehicle.Entity.IsDestroyed)
                {
                    if (CanRecall(player, vehicle, bypassCooldown, command, out reason, ref position, ref rotation))
                    {
                        RecallVehicle(player, vehicle, position, rotation);
                        return;
                    }
                }
                else
                {
                    if (CanSpawn(player, vehicle, bypassCooldown, command, out reason, ref position, ref rotation))
                    {
                        SpawnVehicle(player, vehicle, position, rotation);
                        return;
                    }
                }
                Print(player, reason);
                return;
            }
            if (!BuyVehicle(player, vehicleType)) return;
            vehicleDatabase.IsVehiclePurchased(player.userID, vehicleType, out vehicle);
            if (vehicle.Entity != null && !vehicle.Entity.IsDestroyed)
            {
                if (CanRecall(player, vehicle, bypassCooldown, command, out reason, ref position, ref rotation))
                {
                    RecallVehicle(player, vehicle, position, rotation);
                    return;
                }
            }
            else
            {
                if (CanSpawn(player, vehicle, bypassCooldown, command, out reason, ref position, ref rotation))
                {
                    SpawnVehicle(player, vehicle, position, rotation);
                    return;
                }
            }
            Print(player, reason);
        }

        #endregion Universal Command

        #region Custom Kill Command

        internal void CmdCustomKill(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_USE))
            {
                Print(player, Lang("NotAllowed", player.UserIDString));
                return;
            }
            command = command.Remove(0, configData.chat.customKillCommandPrefix.Length);
            HandleKillCmd(player, command);
        }

        #endregion Custom Kill Command

        #region Command Documentation

        internal void CmdLicenseHelp(BasePlayer player, string command, string[] args)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine(Lang("Help", player.UserIDString));
            stringBuilder.AppendLine(Lang("Help1", player.UserIDString, configData.chat.buyCommand));
            stringBuilder.AppendLine(Lang("Help2", player.UserIDString, configData.chat.spawnCommand));
            stringBuilder.AppendLine(Lang("Help3", player.UserIDString, configData.chat.recallCommand));
            stringBuilder.AppendLine(Lang("Help4", player.UserIDString, configData.chat.killCommand));

            foreach (var entry in allVehicleSettings)
            {
                if (CanViewVehicleInfo(player, entry.Key, entry.Value))
                {
                    if (configData.chat.useUniversalCommand)
                    {
                        var firstCmd = entry.Value.Commands[0];
                        stringBuilder.AppendLine(Lang("Help5", player.UserIDString, firstCmd, entry.Value.DisplayName));
                    }
                }
            }
            Print(player, stringBuilder.ToString());
        }

        #endregion Help Command

        #region Remove Command

        [ConsoleCommand("vl.remove")]
        internal void CCmdRemoveVehicle(ConsoleSystem.Arg arg)
        {
            if (arg.IsAdmin && arg.Args != null && arg.Args.Length == 2)
            {
                var option = arg.Args[0].ToString();
                string vehicleType;
                if (!IsValidVehicleType(option, out vehicleType))
                {
                    Print(arg, $"{option} is not a valid vehicle type");
                    return;
                }
                switch (arg.Args[1].ToString().ToLower())
                {
                    case "*":
                    case "all":
                        {
                            vehicleDatabase.RemoveLicenseForAllPlayers(vehicleType);
                            var vehicleName = GetBaseVehicleSettings(vehicleType).DisplayName;
                            Print(arg, $"You successfully removed the vehicle({vehicleName}) of all players");
                        }
                        return;

                    default:
                        {
                            var target = RustCore.FindPlayer(arg.Args[1].ToString());
                            if (target == null)
                            {
                                Print(arg, $"Player '{arg.Args[1]}' not found");
                                return;
                            }

                            var vehicleName = GetBaseVehicleSettings(vehicleType).DisplayName;
                            if (RemoveVehicleLicense(target.userID, vehicleType))
                            {
                                Print(arg, $"You successfully removed the vehicle({vehicleName}) of {target.displayName}");
                                return;
                            }

                            Print(arg, $"{target.displayName} has not purchased vehicle({vehicleName}) and cannot be removed");
                        }
                        return;
                }
            }
        }

        [ConsoleCommand("vl.dumpcommands")]
        internal void CCmdDumpCommands(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !permission.UserHasPermission(arg.Connection.userid.ToString(), "RustVehicles.admin")) 
            { 
                Print(arg, "No permission.");
                return; 
            }

            var output = new System.Text.StringBuilder();
            output.AppendLine("=== Command to Vehicle Type Mapping ===");
            var sortedCommands = new List<KeyValuePair<string, string>>(commandToVehicleType);
            sortedCommands.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.Ordinal));
            foreach (var kvp in sortedCommands)
            {
                output.AppendLine($"Command: '{kvp.Key}' -> VehicleType: '{kvp.Value}'");
            }
            output.AppendLine("\n=== All Vehicle Settings ===");
            var sortedSettings = new List<KeyValuePair<string, BaseVehicleSettings>>(allVehicleSettings);
            sortedSettings.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.Ordinal));
            foreach (var kvp in sortedSettings)
            {
                var commands = string.Join(", ", kvp.Value.Commands);
                output.AppendLine($"VehicleType: '{kvp.Key}' -> Commands: [{commands}]");
            }
            Print(arg, output.ToString());
        }

        [ConsoleCommand("vl.cleardata")]
        internal void CCmdClearVehicle(ConsoleSystem.Arg arg)
        {
            if (arg.IsAdmin)
            {
                var keysCopy = new List<BaseEntity>(vehiclesCache.Keys);
                foreach (var vehicle in keysCopy)
                {
                    vehicle.Kill(BaseNetworkable.DestroyMode.Gib);
                }
                vehiclesCache.Clear();
                InitializeDatabase();
                Print(arg, "You successfully cleaned up all vehicle data");
            }
        }

        [ConsoleCommand("vl.reloadcustom")]
        internal void CCmdReloadCustom(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !permission.UserHasPermission(arg.Connection.userid.ToString(), "RustVehicles.admin")) { return; }

            RunDiscoveryAndPromote(() =>
            {
                LoadCustomVehiclesFromConfig();

                commandToVehicleType.Clear();
                foreach (var kv in allVehicleSettings)
                {
                    foreach (var cmdStr in kv.Value.Commands)
                    {
                        if (!string.IsNullOrEmpty(cmdStr))
                        {
                            var cmdLower = cmdStr.ToLower();
                            if (!commandToVehicleType.ContainsKey(cmdLower))
                            {
                                commandToVehicleType[cmdLower] = kv.Key;
                            }
                            else
                            {
                                var existingVehicle = commandToVehicleType[cmdLower];
                                PrintError($"Duplicate command '{cmdLower}' - already maps to '{existingVehicle}', trying to map to '{kv.Key}'");
                            }
                        }
                    }
                }

                Puts("[CustomVehicles] Reloaded catalog, folder/API discovery, and command map.");
            });
        }

        private void LoadCustomVehiclesFromConfig()
        {
            if (configData.customVehicles != null)
            {
                foreach (var entry in configData.customVehicles)
                {
                    if (!allVehicleSettings.ContainsKey(entry.Key))
                    {
                        allVehicleSettings[entry.Key] = entry.Value;
                        
                        RegisterCustomVehiclePermissions(entry.Key, entry.Value);
                    }
                }
            }
        }

        #endregion Remove Command

        #region Purchase Command Handler

        [ConsoleCommand("vl.buy")]
        internal void CCmdBuyVehicle(ConsoleSystem.Arg arg)
        {
            if (arg.IsAdmin && arg.Args != null && arg.Args.Length == 2)
            {
                var option = arg.Args[0].ToString();
                string vehicleType;
                if (!IsValidVehicleType(option, out vehicleType))
                {
                    Print(arg, $"{option} is not a valid vehicle type");
                    return;
                }
                switch (arg.Args[1].ToString().ToLower())
                {
                    case "*":
                    case "all":
                        {
                            vehicleDatabase.AddLicenseForAllPlayers(vehicleType);
                            var vehicleName = GetBaseVehicleSettings(vehicleType).DisplayName;
                            Print(arg, $"You successfully purchased the vehicle({vehicleName}) for all players");
                        }
                        return;

                    default:
                        {
                            var target = RustCore.FindPlayer(arg.Args[1].ToString());
                            if (target == null)
                            {
                                Print(arg, $"Player '{arg.Args[1]}' not found");
                                return;
                            }

                            var vehicleName = GetBaseVehicleSettings(vehicleType).DisplayName;
                            if (AddVehicleLicense(target.userID, vehicleType))
                            {
                                Print(arg, $"You successfully purchased the vehicle({vehicleName}) for {target.displayName}");
                                Interface.CallHook("OnLicensedVehiclePurchased", target, vehicleType);
                                return;
                            }

                            Print(arg, $"{target.displayName} has purchased vehicle({vehicleName})");
                            Interface.CallHook("OnLicensedVehiclePurchased", target, vehicleType);
                        }
                        return;
                }
            }
            var player = arg.Player();

            if (player == null)
            {
                Print(arg, $"The server console cannot use the '{arg.cmd.FullName}' command");
            }
            else
            {
                CmdBuyVehicle(player, arg.cmd.FullName, arg.Args == null ? Array.Empty<string>() : Array.ConvertAll(arg.Args, value => value.ToString()));
            }
        }
        internal void CmdBuyVehicle(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_USE))
            {
                Print(player, Lang("NotAllowed", player.UserIDString));
                return;
            }
            if (args == null || args.Length < 1)
            {
                var stringBuilder = new StringBuilder();
                stringBuilder.AppendLine(Lang("Help", player.UserIDString));
                foreach (var entry in allVehicleSettings)
                {
                    if (CanViewVehicleInfo(player, entry.Key, entry.Value))
                    {
                        var firstCmd = entry.Value.Commands[0];
                        if (entry.Value.PurchasePrices.Count > 0)
                        {
                            var prices = FormatPriceInfo(player, entry.Value.PurchasePrices);
                            stringBuilder.AppendLine(Lang("HelpBuyPrice", player.UserIDString, configData.chat.buyCommand, firstCmd, entry.Value.DisplayName, prices));
                        }
                        else
                        {
                            stringBuilder.AppendLine(Lang("HelpBuy", player.UserIDString, configData.chat.buyCommand, firstCmd, entry.Value.DisplayName));
                        }
                    }
                }
                Print(player, stringBuilder.ToString());
                return;
            }
            string vehicleType;

            if (IsValidOption(player, args[0], out vehicleType))
            {
                BuyVehicle(player, vehicleType);
            }
        }

        private bool BuyVehicle(BasePlayer player, string vehicleType, bool response = true)
        {
            var settings = GetBaseVehicleSettings(vehicleType);
            if (!settings.Purchasable)
            {
                Print(player, Lang("VehicleCannotBeBought", player.UserIDString, settings.DisplayName));
                return false;
            }
            var vehicles = vehicleDatabase.GetPlayerVehicles(player.userID, false);
            if (vehicles.ContainsKey(vehicleType))
            {
                Print(player, Lang("VehicleAlreadyPurchased", player.UserIDString, settings.DisplayName));
                return false;
            }
            string resources;
            if (settings.PurchasePrices.Count > 0 && !ProcessPayment(player, settings, settings.PurchasePrices, out resources))
            {
                Print(player, Lang("NoResourcesToPurchaseVehicle", player.UserIDString, settings.DisplayName, resources));
                return false;
            }
                    vehicles.Add(vehicleType, OwnedVehicle.New(player.userID, vehicleType));
            SaveData();
            Interface.CallHook("OnLicensedVehiclePurchased", player, vehicleType, response);
            if (response) Print(player, Lang("VehiclePurchased", player.UserIDString, settings.DisplayName, configData.chat.spawnCommand));
            return true;
        }

        #endregion Buy Command

        #region Spawn Command

        [ConsoleCommand("vl.spawn")]
        internal void CCmdSpawnVehicle(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
            {
                Print(arg, $"The server console cannot use the '{arg.cmd.FullName}' command");
            }
            else
            {
                CmdSpawnVehicle(player, arg.cmd.FullName, arg.Args == null ? Array.Empty<string>() : Array.ConvertAll(arg.Args, value => value.ToString()));
            }
        }

        internal void CmdSpawnVehicle(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_USE))
            {
                Print(player, Lang("NotAllowed", player.UserIDString));
                return;
            }
            if (args == null || args.Length < 1)
            {
                var stringBuilder = new StringBuilder();
                stringBuilder.AppendLine(Lang("Help", player.UserIDString));
                foreach (var entry in allVehicleSettings)
                {
                    if (CanViewVehicleInfo(player, entry.Key, entry.Value))
                    {
                        var firstCmd = entry.Value.Commands[0];
                        if (entry.Value.SpawnPrices.Count > 0)
                        {
                            var prices = FormatPriceInfo(player, entry.Value.SpawnPrices);
                            stringBuilder.AppendLine(Lang("HelpSpawnPrice", player.UserIDString, configData.chat.spawnCommand, firstCmd, entry.Value.DisplayName, prices));
                        }
                        else
                        {
                            stringBuilder.AppendLine(Lang("HelpSpawn", player.UserIDString, configData.chat.spawnCommand, firstCmd, entry.Value.DisplayName));
                        }
                    }
                }
                Print(player, stringBuilder.ToString());
                return;
            }
            string vehicleType;
            if (IsValidOption(player, args[0], out vehicleType))
            {
                var bypassCooldown = args.Length > 1 && IsValidBypassCooldownOption(args[1]);
                SpawnVehicle(player, vehicleType, bypassCooldown, command + " " + args[0]);
            }
        }

        private bool SpawnVehicle(BasePlayer player, string vehicleType, bool bypassCooldown, string command)
        {
            var settings = GetBaseVehicleSettings(vehicleType);
            OwnedVehicle vehicle;
            if (!vehicleDatabase.IsVehiclePurchased(player.userID, vehicleType, out vehicle))
            {
                if (!permission.UserHasPermission(player.UserIDString, PERMISSION_BYPASS_COST))
                {
                    Print(player, Lang("VehicleNotYetPurchased", player.UserIDString, settings.DisplayName, configData.chat.buyCommand));
                    return false;
                }
                BuyVehicle(player, vehicleType);
                vehicle = vehicleDatabase.GetVehicleLicense(player.userID, vehicleType);
            }
            if (vehicle.Entity != null && !vehicle.Entity.IsDestroyed)
            {
                Print(player, Lang("AlreadyVehicleOut", player.UserIDString, settings.DisplayName, configData.chat.recallCommand));
                return false;
            }
            string reason;
            var position = Vector3.zero;
            var rotation = Quaternion.identity;
            if (CanSpawn(player, vehicle, bypassCooldown, command, out reason, ref position, ref rotation))
            {
                SpawnVehicle(player, vehicle, position, rotation);
                return false;
            }
            Print(player, reason);
            return true;
        }

        private bool CanSpawn(BasePlayer player, OwnedVehicle vehicle, bool bypassCooldown, string command, out string reason, ref Vector3 position, ref Quaternion rotation)
        {

            var settings = GetBaseVehicleSettings(vehicle.VehicleType);
            BaseEntity randomVehicle = null;
            if (configData.global.limitVehicles > 0)
            {
                var activeVehicles = vehicleDatabase.ActiveVehicles(player.userID);
                int count = 0;
                foreach (var activeVehicle in activeVehicles)
                {
                    count++;
                }
                if (count >= configData.global.limitVehicles)
                {
                    if (configData.global.killVehicleLimited)
                    {
                        int index = 0;
                        int targetIndex = Random.Range(0, count);
                        foreach (var activeVehicle in activeVehicles)
                        {
                            if (index == targetIndex)
                            {
                                randomVehicle = activeVehicle;
                                break;
                            }
                            index++;
                        }
                    }
                    else
                    {
                        reason = Lang("VehiclesLimit", player.UserIDString, configData.global.limitVehicles);
                        return false;
                    }
                }
            }
            if (!CanPlayerAction(player, vehicle, settings, out reason, ref position, ref rotation))
            {
                return false;
            }
            var obj = Interface.CallHook("CanLicensedVehicleSpawn", player, vehicle.VehicleType, position, rotation);
            if (obj != null)
            {
                var s = obj as string;
                reason = s ?? Lang("SpawnWasBlocked", player.UserIDString, settings.DisplayName);
                return false;
            }

#if DEBUG
            if (player.IsAdmin)
            {
                reason = null;
                return true;
            }
#endif
            if (!CheckCooldown(player, vehicle, settings, bypassCooldown, true, command, out reason))
            {
                return false;
            }

            string resources;
            if (settings.SpawnPrices.Count > 0 && !ProcessPayment(player, settings, settings.SpawnPrices, out resources))
            {
                reason = Lang("NoResourcesToSpawnVehicle", player.UserIDString, settings.DisplayName, resources);
                return false;
            }

            if (!configData.CanSpawnInZones && InZone(player))
            {
                reason = Lang("NoSpawnInZone", player.UserIDString, settings.DisplayName);
                return false;
            }

            if (randomVehicle != null)
            {
                randomVehicle.Kill(BaseNetworkable.DestroyMode.Gib);
            }
            reason = null;
            return true;
        }

        private void SpawnVehicle(BasePlayer player, OwnedVehicle vehicle, Vector3 position, Quaternion rotation, bool response = true)
        {
            var settings = GetBaseVehicleSettings(vehicle.VehicleType);
            var entity = settings.SpawnVehicle(player, vehicle, position, rotation);
            if (entity == null)
            {
                return;
            }

            Interface.CallHook("OnLicensedVehicleSpawned", entity, player, vehicle.VehicleType);
            if (!response) return;
            Print(player, Lang("VehicleSpawned", player.UserIDString, settings.DisplayName));
        }

        private void CacheVehicleEntity(BaseEntity entity, OwnedVehicle vehicle, BasePlayer player)
        {
            vehicle.PlayerId = player.userID;
            vehicle.VehicleType = vehicle.VehicleType;
            vehicle.Entity = entity;
            vehicle.EntityId = entity.net.ID.Value;
            vehicle.LastDismount = vehicle.LastRecall = TimeEx.currentTimestamp;
            vehicle.LastDeath = 0; 
            if (!vehiclesCache.ContainsKey(entity))
            {
                vehiclesCache.Add(entity, vehicle);
            }
            else
            {
                vehiclesCache[entity] = vehicle;
            }

            ReleaseVanillaOwnerLock(entity);
        }

        #endregion Spawn Command

        #region Recall Command

        [ConsoleCommand("vl.recall")]
        internal void CCmdRecallVehicle(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
            {
                Print(arg, $"The server console cannot use the '{arg.cmd.FullName}' command");
            }
            else
            {
                CmdRecallVehicle(player, arg.cmd.FullName, arg.Args == null ? Array.Empty<string>() : Array.ConvertAll(arg.Args, value => value.ToString()));
            }
        }

        internal void CmdRecallVehicle(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_USE))
            {
                Print(player, Lang("NotAllowed", player.UserIDString));
                return;
            }
            if (args == null || args.Length < 1)
            {
                var stringBuilder = new StringBuilder();
                stringBuilder.AppendLine(Lang("Help", player.UserIDString));
                foreach (var entry in allVehicleSettings)
                {
                    if (CanViewVehicleInfo(player, entry.Key, entry.Value))
                    {
                        var firstCmd = entry.Value.Commands[0];
                        if (entry.Value.RecallPrices.Count > 0)
                        {
                            var prices = FormatPriceInfo(player, entry.Value.RecallPrices);
                            stringBuilder.AppendLine(Lang("HelpRecallPrice", player.UserIDString, configData.chat.recallCommand, firstCmd, entry.Value.DisplayName, prices));
                        }
                        else
                        {
                            stringBuilder.AppendLine(Lang("HelpRecall", player.UserIDString, configData.chat.recallCommand, firstCmd, entry.Value.DisplayName));
                        }
                    }
                }
                Print(player, stringBuilder.ToString());
                return;
            }
            string vehicleType;
            if (IsValidOption(player, args[0], out vehicleType))
            {
                var bypassCooldown = args.Length > 1 && IsValidBypassCooldownOption(args[1]);
                RecallVehicle(player, vehicleType, bypassCooldown, command + " " + args[0]);
            }
        }

        private bool RecallVehicle(BasePlayer player, string vehicleType, bool bypassCooldown, string command)
        {
            DebugRecall($"Player {player.displayName} ({player.userID}) attempting recall for vehicle type: {vehicleType}");
            
            var settings = GetBaseVehicleSettings(vehicleType);
            OwnedVehicle vehicle;
            if (!vehicleDatabase.IsVehiclePurchased(player.userID, vehicleType, out vehicle))
            {
                DebugRecall("FAILED: Vehicle not purchased");
                Print(player, Lang("VehicleNotYetPurchased", player.UserIDString, settings.DisplayName, configData.chat.buyCommand));
                return false;
            }
            
            DebugRecall("Vehicle data from database:");
            DebugRecall($"  - VehicleType: {vehicle.VehicleType}");
            DebugRecall($"  - PlayerID: {vehicle.PlayerId}");
            DebugRecall($"  - Stored EntityID: {vehicle.EntityId}");
            DebugRecall($"  - Entity reference: {(vehicle.Entity != null ? "Set" : "Null")}");
            
            if (vehicle.Entity != null)
            {
                DebugRecall("Entity details:");
                DebugRecall($"  - Entity Type: {vehicle.Entity.GetType().Name}");
                DebugRecall($"  - Entity EntityID: {vehicle.Entity.net.ID.Value}");
                DebugRecall($"  - Entity PrefabID: {vehicle.Entity.prefabID}");
                DebugRecall($"  - IsDestroyed: {vehicle.Entity.IsDestroyed}");
                DebugRecall($"  - In vehiclesCache: {vehiclesCache.ContainsKey(vehicle.Entity)}");
            }
            else if (vehicle.EntityId != 0)
            {
                DebugRecall($"Entity reference is null but EntityID is set ({vehicle.EntityId}), attempting lookup...");
                NetworkableId id = new NetworkableId(vehicle.EntityId);
                var foundEntity = BaseNetworkable.serverEntities.Find(id) as BaseEntity;
                if (foundEntity != null)
                {
                    DebugRecall("Found entity by EntityID:");
                    DebugRecall($"  - Entity Type: {foundEntity.GetType().Name}");
                    DebugRecall($"  - Entity EntityID: {foundEntity.net.ID.Value}");
                    DebugRecall($"  - Entity PrefabID: {foundEntity.prefabID}");
                    DebugRecall($"  - IsDestroyed: {foundEntity.IsDestroyed}");
                }
                else
                {
                    DebugRecall("Entity not found by EntityID lookup");
                }
            }
            else
            {
                DebugRecall("EntityID is 0, vehicle is not spawned");
            }
            
            if (vehicle.Entity != null && !vehicle.Entity.IsDestroyed)
            {
                string reason;
                var position = Vector3.zero;
                var rotation = Quaternion.identity;
                DebugRecall("Vehicle entity exists and is not destroyed, checking if can recall...");
                if (CanRecall(player, vehicle, bypassCooldown, command, out reason, ref position, ref rotation))
                {
                    DebugRecall("Can recall - proceeding with recall");
                    RecallVehicle(player, vehicle, position, rotation);
                    return true;
                }
                DebugRecall($"Cannot recall - reason: {reason}");
                Print(player, reason);
                return false;
            }
            DebugRecall("FAILED: Vehicle entity is null or destroyed");
            Print(player, Lang("VehicleNotOut", player.UserIDString, settings.DisplayName, configData.chat.spawnCommand));
            return false;
        }

        private bool CanRecall(BasePlayer player, OwnedVehicle vehicle, bool bypassCooldown, string command, out string reason, ref Vector3 position, ref Quaternion rotation)
        {
            var settings = GetBaseVehicleSettings(vehicle.VehicleType);
            if (settings.RecallMaxDistance > 0 && Vector3.Distance(player.transform.position, vehicle.Entity.transform.position) > settings.RecallMaxDistance)
            {
                reason = Lang("RecallTooFar", player.UserIDString, settings.RecallMaxDistance, settings.DisplayName);
                return false;
            }
            if (configData.global.anyMountedRecall && VehicleAnyMounted(vehicle.Entity))
            {
                reason = Lang("PlayerMountedOnVehicle", player.UserIDString, settings.DisplayName);
                return false;
            }
            if (!CanPlayerAction(player, vehicle, settings, out reason, ref position, ref rotation))
            {
                return false;
            }

            var obj = Interface.CallHook("CanLicensedVehicleRecall", vehicle.Entity, player, vehicle.VehicleType, position, rotation);
            if (obj != null)
            {
                var s = obj as string;
                reason = s ?? Lang("RecallWasBlocked", player.UserIDString, settings.DisplayName);
                return false;
            }
#if DEBUG
            if (player.IsAdmin)
            {
                reason = null;
                return true;
            }
#endif
            if (!CheckCooldown(player, vehicle, settings, bypassCooldown, false, command, out reason))
            {
                return false;
            }
            string resources;
            if (settings.RecallPrices.Count > 0 && !ProcessPayment(player, settings, settings.RecallPrices, out resources))
            {
                reason = Lang("NoResourcesToRecallVehicle", player.UserIDString, settings.DisplayName, resources);
                return false;
            }

            if (!configData.CanSpawnInZones && InZone(player))
            {
                reason = Lang("NoRecallInZone", player.UserIDString, settings.DisplayName);
                return false;
            }
            reason = null;
            return true;
        }

        private void RecallVehicle(BasePlayer player, OwnedVehicle vehicle, Vector3 position, Quaternion rotation)
        {
            var settings = GetBaseVehicleSettings(vehicle.VehicleType);
            settings.PreRecallVehicle(player, vehicle, position, rotation);
            BaseEntity vehicleEntity = vehicle.Entity;

            if (vehicleEntity.IsOn()) vehicleEntity.SetFlag(BaseEntity.Flags.On, false);
            if (vehicleEntity is TrainEngine)
            {
                TrainEngine train = vehicleEntity as TrainEngine;
                train.completeTrain.trackSpeed = 0;
            }
            else
            {
                vehicleEntity.SetVelocity(Vector3.zero);
                vehicleEntity.SetAngularVelocity(Vector3.zero);
            }

            if (!(vehicleEntity is RidableHorse))
            {
                position.y += 2f;
            }

            vehicleEntity.transform.SetPositionAndRotation(position, rotation);
            vehicleEntity.transform.hasChanged = true;
            vehicleEntity.UpdateNetworkGroup();
            vehicleEntity.SendNetworkUpdateImmediate();


            settings.PostRecallVehicle(player, vehicle, position, rotation);
            vehicle.RecordRecall();
            vehicle.LastDeath = 0; 

            if (vehicleEntity == null || vehicleEntity.IsDestroyed)
            {
                Print(player, Lang("NotSpawnedOrRecalled", player.UserIDString, settings.DisplayName));
                return;
            }

            Interface.CallHook("OnLicensedVehicleRecalled", vehicleEntity, player, vehicle.VehicleType);
            Print(player, Lang("VehicleRecalled", player.UserIDString, settings.DisplayName));
        }

        #endregion Recall Command

        #region Kill Command

        [ConsoleCommand("vl.kill")]
        internal void CCmdKillVehicle(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
            {
                Print(arg, $"The server console cannot use the '{arg.cmd.FullName}' command");
            }
            else
            {
                CmdKillVehicle(player, arg.cmd.FullName, arg.Args == null ? Array.Empty<string>() : Array.ConvertAll(arg.Args, value => value.ToString()));
            }
        }

        internal void CmdKillVehicle(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_USE))
            {
                Print(player, Lang("NotAllowed", player.UserIDString));
                return;
            }
            if (args == null || args.Length < 1)
            {
                var stringBuilder = new StringBuilder();
                stringBuilder.AppendLine(Lang("Help", player.UserIDString));
                foreach (var entry in allVehicleSettings)
                {
                    if (CanViewVehicleInfo(player, entry.Key, entry.Value))
                    {
                        var firstCmd = entry.Value.Commands[0];
                        if (!string.IsNullOrEmpty(configData.chat.customKillCommandPrefix))
                        {
                            stringBuilder.AppendLine(Lang("HelpKillCustom", player.UserIDString, configData.chat.killCommand, firstCmd, configData.chat.customKillCommandPrefix + firstCmd, entry.Value.DisplayName));
                        }
                        else
                        {
                            stringBuilder.AppendLine(Lang("HelpKill", player.UserIDString, configData.chat.killCommand, firstCmd, entry.Value.DisplayName));
                        }
                    }
                }
                Print(player, stringBuilder.ToString());
                return;
            }

            HandleKillCmd(player, args[0]);
        }

        private void HandleKillCmd(BasePlayer player, string option)
        {
            string vehicleType;
            if (IsValidOption(player, option, out vehicleType))
            {
                KillVehicle(player, vehicleType);
            }
        }

        private bool KillVehicle(BasePlayer player, string vehicleType, bool response = true)
        {
            var settings = GetBaseVehicleSettings(vehicleType);
            OwnedVehicle vehicle;
            if (!vehicleDatabase.IsVehiclePurchased(player.userID, vehicleType, out vehicle))
            {
                if (player.IsConnected)
                    Print(player, Lang("VehicleNotYetPurchased", player.UserIDString, settings.DisplayName, configData.chat.buyCommand));
                return false;
            }
            if (vehicle.Entity != null && !vehicle.Entity.IsDestroyed)
            {
                if (!CanKill(player, vehicle, settings))
                {
                    return false;
                }
                var entityToKill = vehicle.Entity;
                entityToKill.Kill(BaseNetworkable.DestroyMode.Gib);
                if (!response) return true;
                Interface.CallHook("OnLicensedVehicleKilled", player, vehicle.VehicleType, response);
                if (player.IsConnected)
                    Print(player, Lang("VehicleKilled", player.UserIDString, settings.DisplayName));
                return true;
            }
            if (player.IsConnected)
                Print(player, Lang("VehicleNotOut", player.UserIDString, settings.DisplayName, configData.chat.spawnCommand));
            return false;
        }

        private bool CanKill(BasePlayer player, OwnedVehicle vehicle, BaseVehicleSettings settings)
        {
            if (configData.global.anyMountedKill && VehicleAnyMounted(vehicle.Entity))
            {
                Print(player, Lang("PlayerMountedOnVehicle", player.UserIDString, settings.DisplayName));
                return false;
            }
            if (settings.KillMaxDistance > 0 && Vector3.Distance(player.transform.position, vehicle.Entity.transform.position) > settings.KillMaxDistance)
            {
                Print(player, Lang("KillTooFar", player.UserIDString, settings.KillMaxDistance, settings.DisplayName));
                return false;
            }

            return true;
        }

        #endregion Kill Command

        #region Manual Wipe Command
        
        [ConsoleCommand("vl_wipe")]
        internal void ManualWipeCMD(ConsoleSystem.Arg arg)
        {
            if (arg.Args != null)
            {
                ManualWipe();
            }
        }
        
        #endregion Manual Wipe Command

        #region Command Helpers

        private bool IsValidBypassCooldownOption(string option)
        {
            return !string.IsNullOrEmpty(configData.chat.bypassCooldownCommand) &&
                    string.Equals(option, configData.chat.bypassCooldownCommand, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsValidOption(BasePlayer player, string option, out string vehicleType)
        {
            vehicleType = null;
            var optionLower = option?.ToLower();
            if (string.IsNullOrEmpty(optionLower) || !commandToVehicleType.TryGetValue(optionLower, out vehicleType))
            {
                Print(player, Lang("OptionNotFound", player.UserIDString, option));
                vehicleType = null;
                return false;
            }
            if (!HasVehiclePermission(player, vehicleType))
            {
                Print(player, Lang("NotAllowed", player.UserIDString));
                vehicleType = null;
                return false;
            }
            if (IsPlayerBlocked(player))
            {
                vehicleType = null;
                return false;
            }
            return true;
        }
        private bool IsValidVehicleType(string option, out string vehicleType)
        {
            foreach (var entry in allVehicleSettings)
            {
                if (string.Equals(entry.Key, option, StringComparison.OrdinalIgnoreCase))
                {
                    vehicleType = entry.Key;
                    return true;
                }
            }

            vehicleType = null;
            return false;
        }

        private string FormatPriceInfo(BasePlayer player, Dictionary<string, PriceInfo> prices)
        {
            var language = RustTranslationAPI != null ? lang.GetLanguage(player.UserIDString) : null;
            var priceStrings = new List<string>();
            foreach (var p in prices)
            {
                priceStrings.Add(Lang("PriceFormat", player.UserIDString, GetItemDisplayName(language, p.Key, p.Value.displayName), p.Value.amount));
            }
            return string.Join(", ", priceStrings);
        }

        private bool CanPlayerAction(BasePlayer player, OwnedVehicle vehicle, BaseVehicleSettings settings, out string reason, ref Vector3 position, ref Quaternion rotation)
        {
            if (configData.global.preventBuildingBlocked && player.IsBuildingBlocked())
            {
                reason = Lang("BuildingBlocked", player.UserIDString, settings.DisplayName);
                return false;
            }
            if (configData.global.preventSafeZone && player.InSafeZone())
            {
                reason = Lang("PlayerInSafeZone", player.UserIDString, settings.DisplayName);
                return false;
            }
            if (configData.global.preventMountedOrParented && HasMountedOrParented(player, settings))
            {
                reason = Lang("MountedOrParented", player.UserIDString, settings.DisplayName);
                return false;
            }
            if (!settings.TryGetVehicleParams(player, vehicle, out reason, ref position, ref rotation))
            {
                return false;
            }
            reason = null;
            return true;
        }

        private bool HasMountedOrParented(BasePlayer player, BaseVehicleSettings settings)
        {
            if (player.GetMountedVehicle() != null)
            {
                return true;
            }
            var parentEntity = player.GetParentEntity();
            if (parentEntity != null)
            {
                if (configData.global.spawnLookingAt)
                {
                    if (LandOnCargoShip != null && parentEntity is CargoShip && settings.IsFightVehicle)
                    {
                        return false;
                    }
                }
                return true;
            }
            return false;
        }

        private bool CheckCooldown(BasePlayer player, OwnedVehicle vehicle, BaseVehicleSettings settings, bool bypassCooldown, bool isSpawnCooldown, string command, out string reason)
        {
            var cooldown = settings.GetCooldown(player, isSpawnCooldown);
            if (cooldown > 0)
            {
                var timeLeft = Math.Ceiling(cooldown - (TimeEx.currentTimestamp - (isSpawnCooldown ? vehicle.LastDeath : vehicle.LastRecall)));
                if (timeLeft > 0)
                {
                    var bypassPrices = isSpawnCooldown ? settings.BypassSpawnCooldownPrices : settings.BypassRecallCooldownPrices;
                    if (bypassCooldown && bypassPrices.Count > 0)
                    {
                        string resources;
                        if (!ProcessPayment(player, settings, bypassPrices, out resources))
                        {
                            reason = Lang(isSpawnCooldown ? "NoResourcesToSpawnVehicleBypass" : "NoResourcesToRecallVehicleBypass", player.UserIDString, settings.DisplayName, resources);
                            return false;
                        }

                        if (isSpawnCooldown)
                        {
                            vehicle.LastDeath = 0;
                        }
                        else
                        {
                            vehicle.LastRecall = 0;
                        }
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(configData.chat.bypassCooldownCommand) || bypassPrices.Count <= 0)
                        {
                            reason = Lang(isSpawnCooldown ? "VehicleOnSpawnCooldown" : "VehicleOnRecallCooldown", player.UserIDString, timeLeft, settings.DisplayName);
                        }
                        else
                        {
                            reason = Lang(isSpawnCooldown ? "VehicleOnSpawnCooldownPay" : "VehicleOnRecallCooldownPay", player.UserIDString, timeLeft, settings.DisplayName,
                                          command + " " + configData.chat.bypassCooldownCommand,
                                          FormatPriceInfo(player, isSpawnCooldown ? settings.BypassSpawnCooldownPrices : settings.BypassRecallCooldownPrices));
                        }
                        return false;
                    }
                }
            }
            reason = null;
            return true;
        }

        #endregion Command Helpers

        #endregion Commands
    }
}