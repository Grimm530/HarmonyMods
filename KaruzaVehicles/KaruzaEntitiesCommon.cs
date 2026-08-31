// Requires: CustomEntities

using Facepunch;
using Facepunch.Extend;
using Facepunch.Rust;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using ProtoBuf;
using Rust;
using Rust.Ai.Gen2;
using Rust.Instruments;
using Rust.Modular;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Assertions;
using VLB;
using static AudioVisualisationEntity;
using static BaseEntity;
using static BaseNetworkable;
using static BaseOven;
using static BasePlayer;
using static BaseVehicle;
using static Hopper;
using static InstrumentKeyController;
using static Item;
using static KaruzaVehicles.CustomEntities;
using static RandomItemDispenser;
using static RidableHorse;
using static Rust.Modular.EngineStorage;
using static SeekerTarget;

namespace KaruzaVehicles
{
    [Info("KaruzaEntitiesCommon", "Karuza", "1.10.00")]
    public class KaruzaEntitiesCommon : RustPlugin
    {
        internal Plugin BulletProjectile;

        internal Plugin SimpleStatus;

        public static KaruzaEntitiesCommon Instance;

        public KaruzaEntitiesCommon()
        {
            Instance = this;
        }

        public Dictionary<string, ItemDefinition> WeaponsToAmmoTypeItemIdMap = new Dictionary<string, ItemDefinition>();
        public Dictionary<string, Mesh> CachedMeshes = new Dictionary<string, Mesh>();
        public Dictionary<int, CargoShip> CargoColliders = new Dictionary<int, CargoShip>();
        public List<Recipe> CachedMixingTableRecipes = new List<Recipe>();
        public List<Recipe> CachedCookingTableRecipes = new List<Recipe>();
        public List<TechTreeData> CachedTechTreeData = new List<TechTreeData>();
        public List<string> DeadTreePrefabs = new List<string>();
        public HashSet<string> InstrumentPrefabs = new HashSet<string>();
        public HashSet<string> AllowedFluids = new HashSet<string>();
        public Dictionary<Type, FieldInfo[]> CachedFields = new Dictionary<Type, FieldInfo[]>();
        public List<SamSite.ISamSiteTarget> CustomSAMTargets = new List<SamSite.ISamSiteTarget>();
        public RadarHandler Radar;

        public uint CachedMixingTableFilenameStringId;
        public uint CachedCookingTableFilenameStringId;

        private bool isUnloading = false;
        private Dictionary<string, string> messageDict = new Dictionary<string, string>();

        #region Constants

        public const string PREFAB_SPECIALGIB_SHORTNAME = "SpecialGib";
        public const string PREFAB_SPECIALWORLDITEM_SHORTNAME = "SpecialWorldItem";
        public const string PREFAB_SPECIALTOWWORLDITEM_SHORTNAME = "SpecialTowWorldItem";
        public const string PREFAB_SPECIALTRAVELLINGVENDOR_SHORTNAME = "SpecialTravellingVendor";

        public const string PREFAB_SPECIALGIB_FULLPATH = "assets/custom/specialgib.prefab";
        public const string PREFAB_SPECIALWORLDITEM_FULLPATH = "assets/custom/specialworlditem.prefab";
        public const string PREFAB_SPECIALTOWWORLDITEM_FULLPATH = "assets/custom/specialtowworlditem.prefab";
        public const string PREFAB_SPECIALTRAVELLINGVENDOR_FULLPATH = "assets/custom/specialtravellingvendor.prefab";
        public const string PREFAB_PRISON_GATE = "assets/prefabs/building/wall.frame.cell/wall.frame.cell.gate.prefab";
        public const string PREFAB_PRESS_BUTTON = "assets/prefabs/deployable/playerioents/button/button.prefab";

        public static int GENERAL_COLLIDER = LayerMask.GetMask("Deployable", "Default", "Deployed", "Deployable", "Vehicle Large", "Vehicle World", "Vehicle Detailed", "Resource", "Terrain", "Water", "World", "Tree", "Construction", "Transparent", "Ragdoll", "Clutter");


        public static int IGNORE_COL_MASK = LayerMask.GetMask(LayerMask.LayerToName((int)Layer.Physics_Debris), LayerMask.LayerToName((int)Layer.Ragdoll));
        public static int BB_IGNORE_COL_MASK = LayerMask.GetMask(LayerMask.LayerToName((int)Layer.Physics_Debris), LayerMask.LayerToName((int)Layer.Ragdoll), LayerMask.LayerToName((int)Layer.Player_Movement), LayerMask.LayerToName((int)Layer.Player_Server));
        public static int TOW_TRIGGER_IGNORE_COL_MASK = LayerMask.GetMask(LayerMask.LayerToName((int)Layer.Physics_Debris), LayerMask.LayerToName((int)Layer.Ragdoll), LayerMask.LayerToName((int)Layer.Player_Movement), LayerMask.LayerToName((int)Layer.Player_Server));
        public static int TOW_TRIGGER_AUTO_COL_MASK = LayerMask.GetMask(LayerMask.LayerToName((int)Layer.Vehicle_World), LayerMask.LayerToName((int)Layer.Default), LayerMask.LayerToName((int)Layer.Vehicle_Large));

        public static int PROP_COL_MASK = LayerMask.GetMask(LayerMask.LayerToName((int)Layer.World), LayerMask.LayerToName((int)Layer.Construction), LayerMask.LayerToName((int)Layer.Default), LayerMask.LayerToName((int)Layer.Terrain), LayerMask.LayerToName((int)Layer.Physics_Projectile), LayerMask.LayerToName((int)Layer.Tree));

        public static int GROUND_LAYER = LayerMask.GetMask("Terrain", "Construction");
        public static int HOVER_COLLIDER = LayerMask.GetMask(LayerMask.LayerToName((int)Layer.Deployed), LayerMask.LayerToName((int)Layer.Default), LayerMask.LayerToName((int)Layer.Terrain), LayerMask.LayerToName((int)Layer.Water), LayerMask.LayerToName((int)Layer.World), LayerMask.LayerToName((int)Layer.Tree));

        public const int SHEETL_METAL_ITEMID = -1994909036;
        public const int PROPANE_ITEMID = -1673693549;
        public const int SYRINGE_ITEMID = 1079279582;
        public const int SPRING_ITEMID = -1021495308;
        public const int CASSETTE_LONG_ITEMID = 476066818;
        public const int CASSETTE_MEDIUM_ITEMID = -912398867;
        public const int CASSETTE_SHORT_ITEMID = 1523403414;
        public const int GEAR_ITEMID = 479143914;
        public const int MLRS_ITEMID = -1843426638;
        public const int ROCKET_HV_ITEMID = -1841918730;
        public const int BLADE_ITEMID = 1882709339;
        public const int WEAPON_FLASHLIGHT_ITEMID = 952603248;
        public const int NAIL_ITEMID = -2097376851;

        public static readonly Vector3 DISMOUNT_START_MODIFIER = new Vector3(0.0f, 0.5f, 0.0f);
        public static readonly Vector3 DISMOUNT_END_MODIFIER = new Vector3(0.0f, 1.3f, 0.0f);

        public const string SWITCH_PREFAB = "assets/prefabs/deployable/playerioents/simpleswitch/switch.prefab";
        public const string FUEL_STORAGE = "assets/prefabs/deployable/oil jack/fuelstorage.prefab";
        public const string TELEPHONE_PREFAB = "assets/prefabs/voiceaudio/telephone/telephone.deployed.prefab";
        public const string RF_BROADCASTER_PREFAB = "assets/prefabs/deployable/playerioents/gates/rfbroadcaster/rfbroadcaster.prefab";
        public const string CODELOCK_PREFAB = "assets/prefabs/locks/keypad/lock.code.prefab";
        public const string NPC_VENDING_PREFAB = "assets/prefabs/deployable/vendingmachine/npcvendingmachine.prefab";
        public const string SNOWMOBILE_PREFAB = "assets/content/vehicles/snowmobiles/tomahasnowmobile.prefab";
        public const string SNOWMOBILE2_PREFAB = "assets/content/vehicles/snowmobiles/snowmobile.prefab";
        public const string SEDAN_PREFAB = "assets/content/vehicles/sedan_a/sedantest.entity.prefab";
        public const string SEDAN_RAIL_PREFAB = "assets/content/vehicles/sedan_a/sedanrail.entity.prefab";
        public const string HORSE_PREFAB = "assets/rust.ai/nextai/testridablehorse.prefab";
        public const string MINICOPTER_PREFAB = "assets/content/vehicles/minicopter/minicopter.entity.prefab";
        public const string PREFAB_WORLD = "assets/prefabs/misc/burlap sack/generic_world.prefab";
        public const string PEDAL_BIKE_PREFAB = "assets/content/vehicles/bikes/pedalbike.prefab";
        public const string MOTOR_BIKE_PREFAB = "assets/content/vehicles/bikes/motorbike.prefab";
        public const string DRONE_PREFAB = "assets/prefabs/deployable/drone/drone.deployed.prefab";
        public const string CAMERA_PREFAB = "assets/prefabs/deployable/cctvcamera/cctv_deployed.prefab";
        public const string TURRET_PREFAB = "assets/prefabs/npc/autoturret/autoturret_deployed.prefab";
        public const string PREFAB_COUNTER = "assets/prefabs/deployable/playerioents/counter/counter.prefab";
        public const string BRADLEY_PREFAB = "assets/prefabs/npc/m2bradley/bradleyapc.prefab";
        public const string COMPUTER_STATION_PREFAB = "assets/prefabs/deployable/computerstation/computerstation.static.prefab";
        public const string TWITCH_RIVALS_DESK_PREFAB = "assets/prefabs/misc/twitch/twitch_rivals_2023_desk/twitchrivals2023_desk.prefab";
        public const string SEARCH_LIGHT_PREFAB = "assets/prefabs/deployable/search light/searchlight.deployed.prefab";
        public const string DEBRIS_EFFECT_PREFAB = "assets/content/vehicles/minicopter/debris_effect.prefab";
        public const string CARGO_PREFAB = "assets/content/vehicles/boats/cargoship/cargoshiptest.prefab";
        public const string BUTTON_PREFAB = "assets/prefabs/deployable/playerioents/button/button.prefab";
        public const string BUTTON_COMPACT_PREFAB = "assets/prefabs/io/electric/switches/pressbutton/pressbutton_compact.prefab";
        public const string BUTTON_TRAIN_STAIRS_PREFAB = "assets/prefabs/io/electric/switches/pressbutton/pressbutton_trainstairwell.prefab";
        public const string BUTTON_INVIS_PREFAB = "assets/prefabs/io/electric/switches/pressbutton/pressbutton_invisible.prefab";
        public const string TROPHY_PREFAB = "assets/prefabs/misc/decor_dlc/huntingtrophy_large/huntingtrophylarge.deployed.prefab";
        public const string SPHERE_PREFAB = "assets/prefabs/visualization/sphere.prefab";
        public const string MINI_GUN_PREFAB = "assets/prefabs/weapons/minigun/minigun.entity.prefab";
        public const string M249_GUN_PREFAB = "assets/prefabs/weapons/m249/m249.entity.prefab";
        public const string CHASSIS_PREFAB = "assets/content/vehicles/modularcar/car_chassis_4module.entity.prefab";
        public const string FLARE_FX = "assets/content/vehicles/attackhelicopter/effects/pfx_flares_attackhelicopter.prefab";
        public const string HOT_AIR_BALOON_PREFAB = "assets/prefabs/deployable/hot air balloon/hotairballoon.prefab";
        public const string ATTACK_HELI_PREFAB = "assets/content/vehicles/attackhelicopter/attackhelicopter.entity.prefab";
        public const string VEHICLE_FUEL_MODULE = "assets/content/vehicles/modularcar/module_entities/2module_fuel_tank.prefab";
        public const string VEHICLE_STORAGE_MODULE = "assets/content/vehicles/modularcar/module_entities/1module_storage.prefab";
        public const string VEHICLE_ENGINE_MODULE = "assets/content/vehicles/modularcar/module_entities/1module_engine.prefab";
        public const string VEHICLE_COCKPIT_MODULE = "assets/content/vehicles/modularcar/module_entities/1module_cockpit.prefab";
        public const string VEHICLE_ARMORED_PASSENGER_MODULE = "assets/content/vehicles/modularcar/module_entities/1module_passengers_armored.prefab";
        public const string VEHICLE_ARMORED_COCKPIT_MODULE = "assets/content/vehicles/modularcar/module_entities/1module_cockpit_armored.prefab";
        public const string STORAGE_ADAPTOR_PREFAB = "assets/prefabs/deployable/playerioents/industrialadaptors/storageadaptor.deployed.prefab";
        public const string WHEEL_SWITCH_PREFAB = "assets/prefabs/io/kinetic/wheelswitch_wheel_only.prefab";
        public const string BOBBER_PREFAB = "assets/prefabs/tools/fishing rod/bobber/bobber.entity.prefab";
        public const string SCRAP_HELI_PREFAB = "assets/content/vehicles/scrap heli carrier/scraptransporthelicopter.prefab";
        public const string PREFAB_TRUMPET = "assets/prefabs/instruments/trumpet/trumpet.weapon.prefab";
        public const string PREFAB_MIXING_TABLE = "assets/prefabs/deployable/mixingtable/mixingtable.deployed.prefab";
        public const string PREFAB_COOKING_BENCH = "assets/prefabs/deployable/cookingworkbench/cookingworkbench.deployed.prefab";
        public const string PREFAB_ELECTRIC_FURNACE = "assets/prefabs/deployable/playerioents/electricfurnace/electricfurnace.deployed.prefab";
        public const string PREFAB_WORKBENCH_1 = "assets/prefabs/deployable/tier 1 workbench/workbench1.deployed.prefab";
        public const string PREFAB_WORKBENCH_2 = "assets/prefabs/deployable/tier 2 workbench/workbench2.deployed.prefab";
        public const string PREFAB_WORKBENCH_3 = "assets/prefabs/deployable/tier 3 workbench/workbench3.deployed.prefab";
        public const string CCTV_PREFAB = "assets/prefabs/deployable/cctvcamera/cctv_deployed.prefab";
        public const string ZIP_PREFAB = "assets/prefabs/deployable/locker/sound/equip_zipper.prefab";
        public const string RECYCLER_PREFAB = "assets/bundled/prefabs/static/recycler_static.prefab";
        public const string RECYCLER_START_EFFECT_PREFAB = "assets/prefabs/deployable/recycler/effects/start.prefab";
        public const string RECYCLER_STOP_EFFECT_PREFAB = "assets/prefabs/deployable/recycler/effects/stop.prefab";
        public const string SHOP_FRONT_TRANSACTION_EFFECT_PREFAB = "assets/prefabs/building/wall.frame.shopfront/effects/metal_transaction_complete.prefab";
        public const string BATTERING_RAM_PREFAB = "assets/content/vehicles/siegeweapons/batteringram/batteringram.entity.prefab";
        public const string SPRINKLER_PREFAB = "assets/prefabs/deployable/playerioents/sprinkler/electric.sprinkler.deployed.prefab";
        public const string PREFAB_WATER_BARREL = "assets/prefabs/deployable/liquidbarrel/waterbarrel.prefab";
        public const string RHIB_PREFAB = "assets/content/vehicles/boats/rhib/rhib.prefab";
        public const string MOTOR_BOAT_PREFAB = "assets/content/vehicles/boats/rowboat/rowboat.prefab";
        public const string PT_BOAT_PREFAB = "assets/content/vehicles/boats/ptboat/ptboat.prefab";
        public const string BOOMBOX_STATIC_PREFAB = "assets/prefabs/voiceaudio/boombox/boombox.static.prefab";
        public const string BOOMBOX_DEPLOYED_PREFAB = "assets/prefabs/voiceaudio/boombox/boombox.deployed.prefab";
        public const string BOOMBOX_DEPLOYED_STATIC_PREFAB = "assets/prefabs/voiceaudio/boombox/boombox.deployed.static.prefab";
        public const string SAIL_PREFAB = "assets/prefabs/deployable/boatbuilding/sail/sail.deployed.prefab";
        public const string ANCHOR_PREFAB = "assets/prefabs/deployable/boatbuilding/anchor/anchor.deployed.prefab";
        public const string CANNON_PREFAB = "assets/prefabs/deployable/boatbuilding/cannon/cannon.deployed.prefab";
        public const string STATIC_CANNON_PREFAB = "assets/prefabs/deployable/boatbuilding/cannon/cannon.land.static.prefab";
        public const string SINGLE_FIFTYCAL_PREFAB = "";
        public const string DOUBLE_FIFTYCAL_PREFAB = "";
        public const string STEERING_WHEEL_PREFAB = "assets/prefabs/deployable/boatbuilding/steeringwheel/steeringwheel.deployed.prefab";
        public const string BOAT_WALL_PREFAB = "assets/prefabs/building boat/wall/wall.prefab";
        public const string BOAT_FLOOR_PREFAB = "assets/prefabs/building boat/floor/floor.prefab";
        public const string BOAT_TRIANGLE_PREFAB = "assets/prefabs/building boat/floor.triangle/floor.triangle.prefab";
        public const string BOAT_WALL_COLLIDER_PREFAB = "assets/prefabs/building boat/wall/wall.wood.full.prefab";
        public const string BOAT_STAIRS_PREFAB = "assets/prefabs/building boat/stair/stair.prefab";
        public const string BOAT_STAIRS_COLLIDER_PREFAB = "assets/prefabs/building boat/stair/stair.wood.prefab";
        public const string BOAT_WALL_LOW_BARRIER_PREFAB = "assets/prefabs/building boat/wall.low.barrier/wall.low.barrier.prefab";
        public const string BOAT_WALL_LOW_BARRIER_COLLIDER_PREFAB = "assets/prefabs/building boat/wall.low.barrier/wall.low.barrier.wood.prefab";


        public const string RADAR_LOCK = "RadarLock";
        public const string RADAR_WARNING = "RadarWarning";

        public const string SHOOT_PROJECTILE_HOOK = "ShootProjectile";

        public const string LOWGRADE_SHORTNAME = "lowgradefuel";

        const string ATTACK_HELI_ROCKET_PANEL = "attackhelirockets";
        const string ENGINE_PANEL = "engine";

        const string CAR_ENGINE_PREFAB = "assets/content/vehicles/modularcar/module_entities/1module_engine.prefab";
        const string HMLMG_GUN_PREFAB = "assets/prefabs/weapons/hmlmg/hmlmg.entity.prefab";
        const string TRAVELLING_VENDOR_PREFAB = "assets/prefabs/npc/travelling vendor/travellingvendor.prefab";
        const string GARAGE_DOOR_PREFAB = "assets/prefabs/building/wall.frame.garagedoor/wall.frame.garagedoor.prefab";
        const string SINGLE_SHEET_DOOR_PREFAB = "assets/prefabs/building/door.hinged/door.hinged.metal.prefab";
        const string HAB_DOOR_PREFAB = "assets/prefabs/deployable/hot air balloon/door.hinged.hab_t1.prefab";

        const string BATHTUB_PREFAB = "assets/prefabs/misc/decor_dlc/bath tub planter/bathtub.planter.deployed.prefab";
        const string PRESSURE_PAD_PREFAB = "assets/prefabs/deployable/playerioents/detectors/pressurepad/pressurepad.deployed.prefab";
        const string XMAS_LIGHTS_PREFAB = "assets/prefabs/misc/xmas/christmas_lights/xmas.lightstring.deployed.prefab";
        const string FRDIGE_PREFAB = "assets/prefabs/deployable/fridge/fridge.deployed.prefab";
        const string COFFIN_PREFAB = "assets/prefabs/misc/halloween/coffin/coffinstorage.prefab";
        const string SALVAGE_SWORD_PREFAB = "assets/prefabs/weapons/sword/salvaged_sword.entity.prefab";
        const string SANTA_SLEIGH_PREFAB = "assets/prefabs/misc/xmas/sleigh/santasleigh.prefab";
        const string LONGSWORD_PREFAB = "assets/prefabs/weapons/sword big/longsword.entity.prefab";
        const string SALVAGE_CLEAVER_PREFAB = "assets/prefabs/weapons/cleaver big/salvaged_cleaver.entity.prefab";
        const string PTZ_PREFAB = "assets/prefabs/deployable/ptz security camera/ptz_cctv_deployed.prefab";
        const string FLARE_PREFAB = "assets/content/vehicles/_sharedsubents/helipilotflare.prefab";
        const string CAR_RADIO_PREFAB = "assets/content/vehicles/modularcar/subents/modular_car_radio.prefab";
        const string JET_PREFAB = "assets/scripts/entity/misc/f15/f15e.prefab";
        const string HOT_AIR_BALOON_ARMOR_PREFAB = "assets/prefabs/deployable/hot air balloon/hotairballoon_armor_t1.prefab";
        const string LANTERN_PREFAB = "assets/prefabs/deployable/lantern/lantern.deployed.prefab";

        const string HAB_HINGE_COLLIDER = "hinge";
        const string HAB_DOOR_WORLD_COLLODER = "DoorWorldCollider";

        const string CENTRAL_LOCKING_UNLOCKED_TOAST = "CentrlockLockingUnlockToast";
        const string CENTRAL_LOCKING_LOCKED_TOAST = "CentrlockLockingLockedToast";
        const string ON_ENGINE_ON_TOAST = "OnEngineOnToast";
        const string VTOL_ON_ENGINE_ON_TOAST = "VtolOnEngineOnToast";
        const string NO_AMMO_TOAST = "NoAmmoToast";
        const string AMMO_TOAST_DISPLAYNAME_PARAM = "{AmmoDisplayName}";
        const string POWER_SWITCH_ON_TOAST = "PowerSwitchOnToast";
        const string POWER_SWITCH_OFF_TOAST = "PowerSwitchOffToast";
        const string LANDING_GEAR_ON_TOAST = "LandingGearOnToast";
        const string LANDING_GEAR_OFF_TOAST = "LandingGearOffToast";
        const string LANDING_GEAR_WARNING_TOAST = "LandingGearWarningToast";
        const string LANDING_GEAR_VTOL_WARNING_TOAST = "LandingGearVTOLWarningToast";

        const string TOO_FAR_TO_TOW_TOAST = "TowAnchorTooFar";
        const string TOO_FAR_TO_TOW_TOAST_DISTANCE_KEY = "{Distance}";
        const string CANT_SEE_TO_TOW_TOAST = "TowAnchorNotVisible";
        const string NOT_ATTACHED_TO_TOW_TOAST = "TowAnchorNotValid";
        const string NO_VEHICLE_TO_TOW_TOAST = "TowAnchorNotVehicle";
        const string NO_SAME_VEHICLE_TOW_TOAST = "TowAnchorConnectToSelf";
        const string NO_ACCESS_TO_TOW_TOAST = "TowAnchorNoAccess";
        const string LINKED_COMPUTER_STATION_SEAT_PROMPT = "LinkedComputerStationSeatPrompt";
        const string LINKED_COMPUTER_STATION_SEAT_REAR_VIEW_PROMPT = "LinkedComputerStationSeatRearViewPrompt";
        const string TOW_AUTO_CONNECT_ON_TOAST = "TowAutoConnectSwitchOn";
        const string TOW_AUTO_CONNECT_OFF_TOAST = "TowAutoConnectSwitchOff";

        const string TOWTRIGGER_ONTRIGGERENTER = "TowTrigger.OnTriggerEnter";
        const string SENDNETWORKUPDATE_POSITION = "SendNetworkUpdate_Position";
        const string BASECOMBATENTITY_ONATTACKED = "BaseCombatEntity.OnAttacked";
        const string SNOWMOBILE_ONRPCMESSAGE = "Snowmobile.OnRpcMessage";
        const string STATICINSTRUMENT_ONRPCMESSAGE = "StaticInstrument.OnRpcMessage";
        const string DOOR_ONRPCMESSAGE = "Door.OnRpcMessage";
        const string RFBROADCASTER_ONRPCMESSAGE = "RFBroadcaster.OnRpcMessage";
        const string PRESSBUTTON_ONRPCMESSAGE = "PressButton.OnRpcMessage";
        const string ELECTRICSWITCH_ONRPCMESSAGE = "ElectricSwitch.OnRpcMessage";
        const string MODULARCAR_ONRPCMESSAGE = "ModularCar.OnRpcMessage";
        const string PLAYERHELICOPTER_ONRPCMESSAGE = "PlayerHelicopter.OnRpcMessage";
        const string COMPUTER_STATION_ONRPCMESSAGE = "ComputerStation.OnRpcMessage";

        public const string UPDATE_NETWORKGROUP = "Custom_UpdateNetworkGroup";
        public const string UPDATE_GROUPS = "Custom_UpdateGroups";

        const string RPC_SERVER_REQUEST_OPEN_PANEL = "SERVER_RequestOpenPanel";
        const string RPC_USING_CLIENTSIDE_PLAYER = "SV_RPC Message is using a clientside player!";
        const string RPC_OPENFUEL = "RPC_OpenFuel";
        const string RPC_OPENITEMSTORAGE = "RPC_OpenItemStorage";
        const string RPC_WANTSPUSH = "RPC_WantsPush";
        const string RPC_CLOSEDOOR = "RPC_CloseDoor";
        const string RPC_OPENDOOR = "RPC_OpenDoor";
        const string RPC_TOGGLEHATCH = "RPC_ToggleHatch";
        const string RPC_PRESS = "RPC_Press";
        const string RPC_SWITCH = "RPC_Switch";
        const string RPC_SVSWITCH = "SVSwitch";
        const string RPC_TOGGLE_SWITCH = "ToggleSwitch";
        const string RPC_TIMEWARNING_CALL = "Call";
        const string RPC_TIMEWARNING_CONDITIONS = "Conditions";
        const string RPC_TIMEWARNING_SERVER_PLAYNOTE = "Server_PlayNote";
        const string RPC_TIMEWARNING_SERVER_STOPNOTE = "Server_StopNote";
        const string RPC_TIMEWARNING_SERVER_SETFREQUENCY = "ServerSetFrequency";
        const string RPC_TIMEWARNING_CHANGE_CODE = "RPC_ChangeCode";
        const string RPC_TIMEWARNING_TRY_LOCK = "TryLock";
        const string RPC_TIMEWARNING_TRY_UNLOCK = "TryLock";
        const string RPC_TIMEWARNING_BASEOVEN_RPCMESSAGE = "BaseOven.OnRpcMessage";
        const string RPC_CLIENT_PLAYNOTE = "Client_PlayNote";
        const string RPC_CLIENT_STOPNOTE = "Client_StopNote";
        const string RPC_CLIENT_BIKEUPDATE = "BikeUpdate";
        const string RPC_CLIENT_SNOWMOBILEUPDATE = "SnowmobileUpdate";
        const string RPC_TIMEWARNING_BEGIN_ROTATE = "BeginRotate";
        const string RPC_TIMEWARNING_CANCEL_ROTATE = "CancelRotate";
        const string RPC_RECEIVE_BOOKMARKS = "ReceiveBookmarks";
        const string RPC_TIMEWARNING_BEGIN_CONTROLLING_BOOKMARK = "BeginControllingBookmark";
        const string RPC_TIMEWARNING_SERVER_UPDATE_SETTINGS = "ServerUpdateSettings";
        const string RPC_TIMEWARNING_AUDIO_VISUALISATION_ENTITY = "AudioVisualisationEntity.OnRpcMessage";
        const string RPC_LOWER_SAIL = "LowerSail";
        const string RPC_RAISE_SAIL = "RaiseSail";
        const string RPC_SAIL_ONRPCMESSAGE = "Sail.OnRpcMessage";
        const string RPC_STEERING_WHEEL = "SpecialSteeringWheel.OnRpcMessage";
        const string RPC_RECEIVE_CLIENT_ROTATION = "ReceiveClientRotation";
        const string RPC_BOOMBOX = "SpecialDeployableBoomBox.OnRpcMessage";
        const string RPC_BOOMBOX_TOGGLEPLAY = "ServerTogglePlay";
        const string RPC_BOOMBOX_UPDATE_RADIO = "Server_UpdateRadioIP";

        const string CAN_USE_FUEL_HOOK = "CanUseFuel";
        const string ON_FUEL_CHECK_HOOK = "OnFuelCheck";
        const string CAN_USE_LOCKED_ENTITY_HOOK = "CanUseLockedEntity";
        const string ON_BUTTON_PRESS_HOOK = "OnButtonPress";

        public const float WATER_GUN_REPEAT_DELAY = 1.0f;
        public const float SPECIAL_GUN_REPEAT_DELAY = 0.25f;

        const string FLARE_SHORTNAME = "flare";
        const float COUNTER_UPDATE_DELAY = 1.0f;
        const float WEAPON_COUNTER_UPDATE_DELAY = 0.35f;
        const string SEEKER_PREFAB = "assets/prefabs/ammo/rocket/rocket_heatseeker.prefab";
        const string VEHICLE_TRIGGER = "TriggerVehicle";

        #endregion

        #region Harmony

        [AutoPatch]
        [HarmonyLib.HarmonyPatch(typeof(SamSite), nameof(SamSite.AddTargetSet))]
        public class SamSite_AddTargetSet
        {
            public static void Prefix(List<SamSite.ISamSiteTarget> allTargets, float scanRadius, SamSite __instance)
            {
                var inst = Instance;
                if (inst?.CustomSAMTargets == null || allTargets == null || __instance == null)
                    return;

                var eye = __instance.eyePoint;
                if (eye == null)
                    return;

                Vector3 eyePos = eye.transform.position;
                var targets = inst.CustomSAMTargets;
                for (int i = 0; i < targets.Count; i++)
                {
                    var target = targets[i];
                    if (target == null || target.SAMTargetType == null)
                        continue;
                    if (Vector3.Distance(target.CenterPoint(), eyePos) < target.SAMTargetType.scanRadius)
                        allTargets.Add(target);
                }
            }
        }

        [AutoPatch]
        [HarmonyLib.HarmonyPatch(typeof(WireTool), nameof(WireTool.AllowDifferentParentConnections))]
        public class WireTool_AllowDifferentParentConnections
        {
            public static bool Prefix(BaseEntity ent, ref bool __result)
            {
                if (ent is SpecialContainerIOEntity)
                {
                    __result = true;
                    return false;
                }

                return true;
            }
        }

        [AutoPatch]
        [HarmonyLib.HarmonyPatch(typeof(WireTool), nameof(WireTool.RPC_MakeConnection))]
        public class WireTool_RPC_MakeConnection
        {
            public static bool Prefix(BaseEntity.RPCMessage rpc)
            {
                var prevPos = rpc.read.Position;

                BasePlayer player = rpc.player;
                WireConnectionMessage wireConnectionMessage = rpc.read.Proto<WireConnectionMessage>();

                if (wireConnectionMessage.inputID != wireConnectionMessage.outputID)
                {
                    rpc.read.Position = prevPos;
                    return true;
                }

                var entRef = new EntityRef<TowingAnchor>(wireConnectionMessage.inputID);
                if (!entRef.IsValid(true))
                {
                    rpc.read.Position = prevPos;
                    return true;
                }

                if (wireConnectionMessage.linePoints.Count <= 2)
                {
                    return false;
                }

                var towingConnector = entRef.Get(true);
                if (!WireTool.CanPlayerUseWires(player))
                {
                    return false;
                }

                var lp = wireConnectionMessage.linePoints[1];
                var position = towingConnector.transform.TransformPoint(lp);
                towingConnector.TryConnectAtPosition(position, player);

                return false;
            }
        }

        #endregion

        #region Language

        protected override void LoadDefaultMessages()
        {
            LoadDefaultEnglish();

            if (configuration.DefaultForeignLanguages)
            {
                LoadDefaultSpanish();
                LoadDefaultGerman();
                LoadDefaultRussian();
                LoadDefaultLatvian();
                LoadDefaultLithuanian();
            }
        }

        void LoadDefaultEnglish()
        {
            var existingMessages = lang.GetMessages("en", this) ?? new Dictionary<string, string>();

            TryRegisterDefaultMessage(CENTRAL_LOCKING_UNLOCKED_TOAST, "Passenger seats unlocked", existingMessages);
            TryRegisterDefaultMessage(CENTRAL_LOCKING_LOCKED_TOAST, "Passenger seats locked", existingMessages);
            TryRegisterDefaultMessage(ON_ENGINE_ON_TOAST, "Engine On", existingMessages);
            TryRegisterDefaultMessage(POWER_SWITCH_ON_TOAST, "Power On", existingMessages);
            TryRegisterDefaultMessage(POWER_SWITCH_OFF_TOAST, "Power Off", existingMessages);
            TryRegisterDefaultMessage(NO_AMMO_TOAST, "You are out of ammo!", existingMessages);
            TryRegisterDefaultMessage(LANDING_GEAR_ON_TOAST, "Landing Gear Engaged", existingMessages);
            TryRegisterDefaultMessage(LANDING_GEAR_OFF_TOAST, "Landing Gear Disengaged", existingMessages);
            TryRegisterDefaultMessage(LANDING_GEAR_WARNING_TOAST, "Toggle Landing Gear Switch to disable VTOL", existingMessages);
            TryRegisterDefaultMessage(LANDING_GEAR_VTOL_WARNING_TOAST, "Disengage Landing Gear for better performance", existingMessages);
            TryRegisterDefaultMessage(VTOL_ON_ENGINE_ON_TOAST, "Hold Crouch for VTOL mode", existingMessages);
            TryRegisterDefaultMessage(TOO_FAR_TO_TOW_TOAST, "You are too far! The vehicle must be {Distance}m closer", existingMessages);
            TryRegisterDefaultMessage(NOT_ATTACHED_TO_TOW_TOAST, "No vehicle found at the anchor point", existingMessages);
            TryRegisterDefaultMessage(CANT_SEE_TO_TOW_TOAST, "Anchor is not in the line of sight", existingMessages);
            TryRegisterDefaultMessage(NO_VEHICLE_TO_TOW_TOAST, "Not attached to a vehicle", existingMessages);
            TryRegisterDefaultMessage(NO_SAME_VEHICLE_TOW_TOAST, "Cannot attach to the same vehicle", existingMessages);
            TryRegisterDefaultMessage(LINKED_COMPUTER_STATION_SEAT_PROMPT, "Press [Duck] and [Use] keys to switch to the computer", existingMessages);
            TryRegisterDefaultMessage(LINKED_COMPUTER_STATION_SEAT_REAR_VIEW_PROMPT, "Press [Duck] and [Use] keys to access the rear camera", existingMessages);
            TryRegisterDefaultMessage(TOW_AUTO_CONNECT_ON_TOAST, "Auto Connect Engaged", existingMessages);
            TryRegisterDefaultMessage(TOW_AUTO_CONNECT_OFF_TOAST, "Auto Connect Disengaged", existingMessages);

            lang.RegisterMessages(existingMessages, this, "en");

            messageDict = existingMessages;
        }

        void LoadDefaultSpanish()
        {
            var existingMessages = lang.GetMessages("es", this) ?? new Dictionary<string, string>();
            TryRegisterDefaultMessage(CENTRAL_LOCKING_UNLOCKED_TOAST, "Asiento del acompañante desbloqueado", existingMessages);
            TryRegisterDefaultMessage(CENTRAL_LOCKING_LOCKED_TOAST, "Asiento del acompañante bloqueado", existingMessages);
            TryRegisterDefaultMessage(ON_ENGINE_ON_TOAST, "Motor encendido", existingMessages);
            TryRegisterDefaultMessage(POWER_SWITCH_ON_TOAST, "activado", existingMessages);
            TryRegisterDefaultMessage(POWER_SWITCH_OFF_TOAST, "desactivado", existingMessages);
            TryRegisterDefaultMessage(NO_AMMO_TOAST, "¡No tienes municiones!", existingMessages);

            lang.RegisterMessages(messageDict, this, "es");
        }

        void LoadDefaultGerman()
        {
            var existingMessages = lang.GetMessages("de", this) ?? new Dictionary<string, string>();

            TryRegisterDefaultMessage(CENTRAL_LOCKING_UNLOCKED_TOAST, "Beifahrersitz geöffnet", existingMessages);
            TryRegisterDefaultMessage(CENTRAL_LOCKING_LOCKED_TOAST, "Beifahrersitz geschlossen", existingMessages);
            TryRegisterDefaultMessage(ON_ENGINE_ON_TOAST, "Motor ist an", existingMessages);
            TryRegisterDefaultMessage(POWER_SWITCH_ON_TOAST, "Zündung ist An", existingMessages);
            TryRegisterDefaultMessage(POWER_SWITCH_OFF_TOAST, "Zündung ist Aus", existingMessages);
            TryRegisterDefaultMessage(NO_AMMO_TOAST, "Du hast keine Munition mehr!", existingMessages);

            lang.RegisterMessages(messageDict, this, "de");
        }

        void LoadDefaultRussian()
        {
            var existingMessages = lang.GetMessages("ru", this) ?? new Dictionary<string, string>();

            TryRegisterDefaultMessage(CENTRAL_LOCKING_UNLOCKED_TOAST, "Пассажирские сиденья разблокированы", existingMessages);
            TryRegisterDefaultMessage(CENTRAL_LOCKING_LOCKED_TOAST, "Пассажирские сиденья заблокированы", existingMessages);
            TryRegisterDefaultMessage(ON_ENGINE_ON_TOAST, "Двигатель включен", existingMessages);
            TryRegisterDefaultMessage(POWER_SWITCH_ON_TOAST, "Включено", existingMessages);
            TryRegisterDefaultMessage(POWER_SWITCH_OFF_TOAST, "Выключено", existingMessages);
            TryRegisterDefaultMessage(NO_AMMO_TOAST, "Боеприпасы закончились!", existingMessages);

            lang.RegisterMessages(messageDict, this, "ru");
        }

        void LoadDefaultLatvian()
        {
            var existingMessages = lang.GetMessages("lv", this) ?? new Dictionary<string, string>();

            TryRegisterDefaultMessage(CENTRAL_LOCKING_UNLOCKED_TOAST, "Pasažieru sēdekļi atbloķēti", existingMessages);
            TryRegisterDefaultMessage(CENTRAL_LOCKING_LOCKED_TOAST, "Pasažieru sēdekļi bloķēti", existingMessages);
            TryRegisterDefaultMessage(ON_ENGINE_ON_TOAST, "Dzinējs ieslēgts", existingMessages);
            TryRegisterDefaultMessage(POWER_SWITCH_ON_TOAST, "Ieslēgts", existingMessages);
            TryRegisterDefaultMessage(POWER_SWITCH_OFF_TOAST, "Izslēgts", existingMessages);
            TryRegisterDefaultMessage(NO_AMMO_TOAST, "Jums beidzās munīcija!", existingMessages);

            lang.RegisterMessages(messageDict, this, "lv");
        }

        void LoadDefaultLithuanian()
        {
            var existingMessages = lang.GetMessages("lt", this) ?? new Dictionary<string, string>();

            TryRegisterDefaultMessage(CENTRAL_LOCKING_UNLOCKED_TOAST, "Keleivių sėdynės atrakintos", existingMessages);
            TryRegisterDefaultMessage(CENTRAL_LOCKING_LOCKED_TOAST, "Keleivių sėdynės užrakintos", existingMessages);
            TryRegisterDefaultMessage(ON_ENGINE_ON_TOAST, "Variklis įjungtas", existingMessages);
            TryRegisterDefaultMessage(POWER_SWITCH_ON_TOAST, "Įjungta", existingMessages);
            TryRegisterDefaultMessage(POWER_SWITCH_OFF_TOAST, "Išjungta", existingMessages);
            TryRegisterDefaultMessage(NO_AMMO_TOAST, "Jums baigėsi šaudmenys!", existingMessages);

            lang.RegisterMessages(messageDict, this, "lt");
        }

        public void TryRegisterDefaultMessage(string key, string message, Dictionary<string, string> existing)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (existing.ContainsKey(key))
            {
                return;
            }

            existing[key] = message;
        }

        public void TryRegisterMessage(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (messageDict.ContainsKey(key))
            {
                return;
            }

            messageDict[key] = string.Empty;
            lang.RegisterMessages(messageDict, this, "en");
        }

        public void ShowToast(BasePlayer player, string key, GameTip.Styles style = GameTip.Styles.Blue_Normal, string argKey = "", string argValue = "")
        {
            if (player.IsNpc || player.IsBot)
            {
                return;
            }

            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            var msg = Instance.lang.GetMessage(key, this, player.UserIDString);
            if (string.IsNullOrEmpty(msg))
            {
                return;
            }

            if (!string.IsNullOrEmpty(argKey))
            {
                msg = msg.Replace(argKey, argValue);
            }

            player.ShowToast(style, msg);
        }

        #endregion

        #region Hooks

        internal void OnEntitySpawned(CargoShip entity)
        {
            CacheCargoTriggers(entity);
        }

        void CacheCargoTriggers(CargoShip entity)
        {
            var cols = entity.GetComponentsInChildren<Collider>();
            for (int i = 0; i < cols.Length; i++)
            {
                var col = cols[i];
                if (!col.isTrigger)
                {
                    continue;
                }

                if (CargoColliders.ContainsKey(col.GetInstanceID()))
                {
                    continue;
                }

                if (col.name != VEHICLE_TRIGGER)
                {
                    continue;
                }

                CargoColliders.Add(col.GetInstanceID(), entity);
            }
        }

        internal object OnEntityKill(CargoShip entity)
        {
            var toRemove = 0;
            foreach (var cargo in CargoColliders)
            {
                if (cargo.Value.net.ID.Value == entity.net.ID.Value)
                {
                    toRemove = cargo.Key;
                    break;
                }
            }

            if (toRemove != 0)
            {
                CargoColliders.Remove(toRemove);
            }

            return null;
        }

        #endregion

        #region Init

        internal void Init()
        {
            Instance = this;

            Unsubscribe(nameof(OnEntitySpawned));
            Unsubscribe(nameof(OnEntityKill));
            PopulateCollections();
        }

        internal void OnServerInitialized()
        {
            LoadConfig();

            Puts($"API is configured: {APIHelper.IsConfigured()}");

            InitializeMeshes();
            InitializeMixingTableRecipes();
            InitializeTechTreeData();
            InitializeAllowedFluids();
            RegisterVehicleBundles();
            StatusUtilities.InitializeStatusBars();
            InitializeCargoTriggers();
            CrosshairUtilities.GenerateGUI();
            GForceGUIUtilities.GenerateGUI();
            var radarGo = new GameObject();
            Radar = radarGo.AddComponent<RadarHandler>();

            if (configuration.SubscribeToCargoSpawn)
            {
                Subscribe(nameof(OnEntitySpawned));
                Subscribe(nameof(OnEntityKill));
            }
        }

        internal void Unload()
        {
            isUnloading = true;

            SaveAndUnregisterEntityBundles();

            CachedMeshes.Clear();
            CachedMeshes = null;
            CachedMixingTableRecipes.Clear();
            CachedMixingTableRecipes = null;
            CachedCookingTableRecipes.Clear();
            CachedCookingTableRecipes = null;
            CachedTechTreeData.Clear();
            CachedTechTreeData = null;
            AllowedFluids.Clear();
            AllowedFluids = null;
            TowTrigger.CachedTriggers.Clear();
            TowTrigger.CachedTriggers = null;
            WeaponsToAmmoTypeItemIdMap.Clear();
            WeaponsToAmmoTypeItemIdMap = null;
            CargoColliders.Clear();
            CargoColliders = null;
            DeadTreePrefabs.Clear();
            DeadTreePrefabs = null;
            InstrumentPrefabs.Clear();
            InstrumentPrefabs = null;
            CachedFields.Clear();
            CachedFields = null;

            CustomAntiHack.HitBuffer = null;
            CustomAntiHack.HitBufferB = null;
            CustomAntiHack.ColBuffer = null;

            if (StatusUtilities.StatusArgs != null)
            {
                StatusUtilities.StatusArgs.Clear();
                StatusUtilities.StatusArgs = null;
            }

            CrosshairUtilities.Unload();
            GForceGUIUtilities.Unload();

            GameObject.Destroy(Radar);

            Instance = null;
        }

        void InitializeCargoTriggers()
        {
            var cargoShips = BaseNetworkable.serverEntities.OfType<CargoShip>();
            foreach (var cargo in cargoShips)
            {
                CacheCargoTriggers(cargo);
            }
        }

        public void InitializeMixingTableRecipes()
        {
            var mixingTable = GameManager.server.CreateEntity(PREFAB_MIXING_TABLE, Vector3.zero, Quaternion.Euler(Vector3.zero)) as MixingTable;
            Instance.CachedMixingTableRecipes = mixingTable.Recipes.Recipes.ToList();
            Instance.CachedMixingTableFilenameStringId = mixingTable.Recipes.FilenameStringId;
            mixingTable.Kill(BaseNetworkable.DestroyMode.None);

            var cookingBench = GameManager.server.CreateEntity(PREFAB_COOKING_BENCH, Vector3.zero, Quaternion.Euler(Vector3.zero)) as CookingWorkbench;
            Instance.CachedCookingTableRecipes = cookingBench.Recipes.Recipes.ToList();
            Instance.CachedCookingTableFilenameStringId = cookingBench.Recipes.FilenameStringId;
            cookingBench.Kill(BaseNetworkable.DestroyMode.None);
        }

        public void InitializeTechTreeData()
        {
            var ent = GameManager.server.CreateEntity(PREFAB_WORKBENCH_3, Vector3.zero, Quaternion.Euler(Vector3.zero)) as Workbench;
            Instance.CachedTechTreeData = ent.techTrees.ToList();
            ent.Kill(BaseNetworkable.DestroyMode.None);
        }

        public void InitializeAllowedFluids()
        {
            AllowedFluids = new HashSet<string>()
            {
                "water",
                "water.radioactive"
            };
        }

        public void InitializeMeshes()
        {
            List<string> toInit = new List<string>()
            {
                SCRAP_HELI_PREFAB,
                BRADLEY_PREFAB,
                "assets/prefabs/npc/ch47/ch47.entity.prefab",
                "assets/prefabs/npc/patrol helicopter/patrolhelicopter.prefab"
            };

            foreach (var toI in toInit)
            {
                var ent = GameManager.server.CreateEntity(toI, Vector3.zero, Quaternion.Euler(Vector3.zero));
                if (configuration.SpawnMeshColliderVehicles)
                {
                    ent.Spawn();
                }

                GameObject gibSource = null;
                GameObjectRef serverGibs = null;
                if (ent is BaseHelicopter)
                {
                    serverGibs = (ent as BaseHelicopter).serverGibs;
                    (ent as BaseHelicopter).serverGibs = new GameObjectRef();
                }
                else if (ent is PatrolHelicopter ph)
                {
                    serverGibs = ph.servergibs;
                    (ent as PatrolHelicopter).servergibs = new GameObjectRef();
                }
                else if (ent is BradleyAPC)
                {
                    serverGibs = (ent as BradleyAPC).servergibs;
                    var bradley = (ent as BradleyAPC);
                    bradley.servergibs = new GameObjectRef();

                    foreach (var col in bradley.GetComponentsInChildren<Collider>(includeInactive: true))
                    {
                        if (col is MeshCollider mc)
                        {
                            Mesh physicsMesh = mc.sharedMesh;
                            if (!Instance.CachedMeshes.ContainsKey(col.name))
                            {
                                Instance.CachedMeshes.Add(col.name, physicsMesh);
                            }
                        }
                    }
                }

                gibSource = serverGibs.Get().GetComponent<ServerGib>()._gibSource;
                MeshRenderer[] componentsInChildren = gibSource.GetComponentsInChildren<MeshRenderer>(includeInactive: true);

                foreach (MeshRenderer meshRenderer in componentsInChildren)
                {
                    MeshFilter component = meshRenderer.GetComponent<MeshFilter>();
                    MeshCollider component3 = meshRenderer.GetComponent<MeshCollider>();
                    Mesh physicsMesh = ((component3 != null) ? component3.sharedMesh : component.sharedMesh);

                    if (!Instance.CachedMeshes.ContainsKey(meshRenderer.name))
                    {
                        Instance.CachedMeshes.Add(meshRenderer.name, physicsMesh);
                    }
                }

                ent.Kill(BaseNetworkable.DestroyMode.None);
            }
        }

        public static void InitializeAmmoTypes(List<BaseVehicleConfig> vehicleConfigs)
        {
            foreach (var config in vehicleConfigs)
            {
                InitializeAmmoTypes(config);
            }
        }

        public static void InitializeAmmoTypes(BaseVehicleConfig config)
        {
            InitializeAmmoTypes(config.WeaponConfigurations);
            InitializeGimbalAmmoTypes(config.GimbalConfigs);
            InitializeComputerStationAmmoTypes(config.ComputerStationConfigs);
        }

        private static void InitializeGimbalAmmoTypes(List<GimbalConfig> gimbalConfigs)
        {
            for (int n = 0; n < gimbalConfigs.Count; n++)
            {
                var gc = gimbalConfigs[n];
                if (gc != null && gc.WeaponControllerConfig.WeaponConfigurations != null)
                {
                    InitializeAmmoTypes(gc.WeaponControllerConfig.WeaponConfigurations);
                }
            }
        }

        private static void InitializeComputerStationAmmoTypes(List<ComputerStationConfig> stationConfigs)
        {
            if (stationConfigs == null)
            {
                return;
            }

            for (int n = 0; n < stationConfigs.Count; n++)
            {
                var sc = stationConfigs[n];
                if (sc != null && sc.WeaponControllerConfig.WeaponConfigurations != null)
                {
                    InitializeAmmoTypes(sc.WeaponControllerConfig.WeaponConfigurations);
                }
            }
        }

        private static void InitializeAmmoTypes(List<WeaponConfiguration> weaponConfigurations)
        {
            for (int i = 0; i < weaponConfigurations.Count; i++)
            {
                var wc = weaponConfigurations[i];
                InitializeAmmoType(wc);
            }
        }

        public static void InitializeAmmoType(WeaponConfiguration weaponConfiguration)
        {
            if (!string.IsNullOrEmpty(weaponConfiguration.AmmoShortName))
            {
                AddAmmoTypeToAmmoTypeMap(weaponConfiguration.AmmoShortName);
            }
        }

        private static void AddAmmoTypeToAmmoTypeMap(string ammoTypeShortName)
        {
            if (string.IsNullOrEmpty(ammoTypeShortName))
                return;

            if (!Instance.WeaponsToAmmoTypeItemIdMap.ContainsKey(ammoTypeShortName))
                Instance.WeaponsToAmmoTypeItemIdMap[ammoTypeShortName] = ItemManager.itemList.Find(x => x.shortname == ammoTypeShortName) ?? new ItemDefinition();
        }

        #endregion

        #region Interfaces

        public interface IRadarVehicle
        {
            BaseVehicle BaseVehicle { get; }
            BaseVehicleConfig VehicleConfig { get; }
            BasePlayer CustomGetDriver();
        }

        public interface ICustomContainer
        {
            BaseEntity Container { get; }
            ItemContainer inventory { get; }
            bool OnlyOneUser { get; }
            string PanelName { get; }
            bool PlayerOpenLoot(BasePlayer player, string panelToOpen = "", bool doPositionChecks = true);
            void CustomDropItems(BaseEntity initiator = null);
            bool CanOpenLootPanel(BasePlayer player, string panelName);
        }

        public interface IStorageContainer : ICustomContainer
        {
            ContainerConfiguration Config { get; set; }
        }

        public interface ISpecialAmmoContainer : ICustomContainer
        {
            AmmoContainerConfiguration Config { get; set; }
        }

        public interface IKaruzaPrefab
        {
            public IKaruzaCustomPrefab KaruzaEntity { get; }
        }

        public interface IJoint
        {
            Action OnJointUpdate { get; set; }
        }

        public interface ICustomSwitch
        {
            BaseSwitchConfig Config { get; set; }
            List<ILightToggle> Lights { get; set; }
            List<SpecialDoor> Doors { get; set; }
            List<VehiclePropeller> Propellers { get; set; }
            List<SpecialSiren> Sirens { get; set; }
            List<SpecialSail> Sails { get; set; }
            List<SpecialAnchor> Anchors { get; set; }
            bool ToggleLights { get; set; }
            bool ToggleJoint { get; set; }
            bool ToggleDoors { get; set; }
            bool TogglePropellers { get; set; }
            bool ToggleSirens { get; set; }
            bool ToggleAnchors { get; set; }
            bool ToggleSails { get; set; }
            bool IgnoreCodelock { get; set; }
            Action<bool> OnJointToggle { get; set; }
            void SetState(bool newState);
        }

        public interface ILandingGearVehicle
        {
            bool LandingGearOn { get; }
            bool IsOnGround { get; }
            bool IsPowered { get; }
            BasePlayer CustomGetDriver();
            void ToggleLandingGear(bool wantsOn);
            Action<bool> OnLandingGearToggle { get; set; }
            LandingGearSettings LandingGearSettings { get; }
            BaseVehicleConfig VehicleConfig { get; }
            BaseVehicle BaseVehicle { get; }
        }

        public interface ILightToggle
        {
            bool CanVehicleToggle { get; set; }
            void ToggleLights(bool lightsOn);
        }

        public interface IKaruzaCustomPrefab
        {
            BodyType BodyType { get; }
            BaseEntity BaseEntity { get; }
            Action OnPhysicsUpdate { get; set; }
            void Hurt(HitInfo info);
            void OnAttacked(HitInfo info);
            void DoRepair(BasePlayer player);
            Action<bool> OnToggled { get; set; }
        }

        public interface IKaruzaCustomEntity : IKaruzaCustomPrefab
        {
            Action<bool> OnPowerToggle { get; set; }
            bool IsPowered { get; }
            bool TogglePower(BasePlayer player);
            void TogglePower(bool on, bool force = false);
            void OnJointUpdate();
            bool HasLock { get; }
            bool CanAccess(BasePlayer player);
            List<IStorageContainer> StorageContainers { get; }
            Action<float, float> OnHealthChange { get; set; }
        }

        public interface IVehicle : IEngineControllerUser, IKaruzaCustomEntity
        {
            BaseVehicleConfig VehicleConfig { get; }
            BaseVehicle BaseVehicle { get; }
            float SteerAngle { get; }
            float DriveWheelVelocity { get; }
            bool IsStartingUp { get; }
            List<MountPointInfo> MountPoints { get; }
            List<Weakspot> Weakspots { get; set; }
            IFuelSystem FuelSystem { get; }
            // Casing to mimic BaseCombatEntity
            float healthFraction { get; }
            Action OnMountedChange { get; set; }
            bool IsOnGround { get; }
            Dictionary<HurtTriggerType, Dictionary<HurtTriggerConfig, TriggerHurtNotChild>> HurtTriggers { get; }
            Action<bool> OnBoostToggle { get; set; }
            bool IsBoosting { get; set; }
            float LastBoostTime { get; set; }
            bool BoostOnCooldown { get; set; }
            float BoostTime { get; set; }
            Action OnBoostTimeUpdate { get; set; }
            void UpdateBoost(bool isBoosting);
            List<WeaponSystem> WeaponSystems { get; }
            KaruzaVehicleFuelSystem BoostFuelSystem { get; set; }
            VehicleBoostContainer BoostContainer { get; }
            ISpecialAmmoContainer AmmoContainer { get; }
            VehicleFuelContainer FuelContainer { get; }
            bool IsEjectEligible { get; }
            float GetBrakeInput();
            float GetFuelFraction(bool force = false);
            void AttemptMount(BasePlayer player, bool doMountChecks = true);
            void SwapSeats(BasePlayer player, int targetSeat = 0, bool forcingRestrainedPlayer = false);
            void PlayerServerInput(InputState inputState, BasePlayer player);
            BasePlayer CustomGetDriver();
            bool CustomHasDriver();
            bool CanSwapToSeat(BasePlayer player, BaseMountable mount);
            void ToggleCentralLocking(bool isOn);
            bool LootFuel(BasePlayer player);
            bool LootStorage(BasePlayer player);
            float GetThrottleInput();
            void AddTowingVehicle();
            void RemoveTowingVehicle();
            void AddTowedVehicle();
            void RemoveTowedVehicle();
            void CustomSetParent(BaseEntity entity);
            void CustomLightToggle(bool? forcedState = null);
            void UpdateLoseControl(bool loseControl);
        }

        public interface IWeaponController
        {
            // The source entity. May be same as WeaponControllerEntity, however WeaponControllerEntity can be a child
            BaseEntity BaseEntity { get; }
            BaseEntity WeaponControllerEntity { get; }
            Transform Transform { get; }
            List<SpecialGun> SpecialGuns { get; }
            void InvokeStopSpecialGuns();
            float NextDryFireEffect { get; set; }
            Vector3 Forward { get; }
            bool HasAmmoContainer { get; }
            ISpecialAmmoContainer AmmoContainer { get; }
            float LastAmmoUpdate { get; set; }
            bool UnlimitedAmmo { get; }
            string NoAmmoToast { get; }
        }

        public interface ITVGuidedRocketController
        {
            void OnTVGuidedProjectileFired(TVGuidedRocketCamera rocketCamera);
        }

        public interface IBaseVehicleConfigPrefab
        {
            public string PrefabPath { get; set; }
        }

        public interface IBaseVehicleConfigEnabled
        {
            bool Enabled { get; }
        }

        public interface IBaseVehicleConfigLocation
        {
            ConfigVector Location { get; }
        }

        public interface IBaseVehicleConfigRotation
        {
            ConfigVector Rotation { get; }
        }

        public interface IBaseVehicleConfigScale
        {
            ConfigVector Scale { get; }
        }

        public interface ILandingGearConfig
        {
            public LandingGearSettings LandingGearSettings { get; set; }
        }

        public interface IBaseVehicleSubPropList
        {
            public List<PropConfig> SubPropConfigs { get; set; }
        }

        public interface IBaseVehicleConfigFlags
        {
            public Flags Flags { get; set; }
        }


        #endregion

        #region Custom Triggers

        public class HopperTrigger : TriggerEnterTimer
        {
            public SpecialStorageContainer StorageContainer;

            public override void OnEntityEnter(BaseEntity ent)
            {
                base.OnEntityEnter(ent);

                if (ent is IHopperTarget hopperTarget && hopperTarget.ToEntity.isServer)
                {
                    if (ent is DroppedItem droppedItem && !droppedItem.HasFlag(Flags.Reserved3) && StorageContainer.inventory.QuickIndustrialPreCheck(droppedItem.item, new Vector2i(0, StorageContainer.inventory.capacity - 1), droppedItem.item.amount, out var _))
                    {
                        Vector3 value = ent.transform.position;
                        if (droppedItem.childCollider != null)
                        {
                            value = droppedItem.childCollider.bounds.center;
                        }
                    }

                    hopperTarget.PrepareForHopper();
                    if (hopperTarget.Rigidbody != null)
                    {
                        hopperTarget.Rigidbody.useGravity = false;
                        hopperTarget.Rigidbody.linearVelocity = Vector3.zero;
                        hopperTarget.Rigidbody.angularVelocity = Vector3.zero;
                    }

                    hopperTarget.TransferAllItemsToContainer(StorageContainer.inventory, StorageContainer.transform.position);
                }
            }
        }

        public class FoilageInteractionTrigger : BaseMonoBehaviour
        {
            BoxCollider boxCollider;

            bool karuzaEntityIsVehicle;
            IVehicle vehicle;

            IKaruzaCustomEntity _karuzaEntity;
            public IKaruzaCustomEntity KaruzaEntity
            {
                get { return _karuzaEntity; }
                set
                {
                    _karuzaEntity = value;
                    if (_karuzaEntity is IVehicle veh)
                    {
                        karuzaEntityIsVehicle = true;
                        vehicle = veh;
                    }
                }
            }

            public int InterestLayers;
            public int Modifier;


            private float nextTriggerUpdate = Time.realtimeSinceStartup;
            List<BaseEntity> harvestEntities = new List<BaseEntity>();

            public void OnTriggerEnter(Collider collider)
            {
                if (!KaruzaEntity.BaseEntity.IsOn())
                {
                    return;
                }

                if (nextTriggerUpdate > Time.realtimeSinceStartup)
                {
                    return;
                }

                nextTriggerUpdate = Time.realtimeSinceStartup + 0.1f;

                using (TimeWarning.New("FoilageInteractionTrigger.OnTriggerEnter"))
                {
                    int num = 1 << collider.gameObject.layer;
                    if ((InterestLayers & num) != num)
                    {
                        return;
                    }

                    if (collider.gameObject.layer == (int)Layer.Bush)
                    {
                        var entRef = Rust.Registry.Entity.Get(collider.gameObject.transform);
                        if (entRef.IsDestroyed)
                        {
                            return;
                        }

                        if (entRef is BushEntity bushEnt)
                        {
                            bushEnt.Kill();
                        }

                        return;
                    }

                    if (collider.gameObject.layer == (int)Layer.Harvestable)
                    {
                        harvestEntities.Clear();
                        Vis.Entities(collider.transform.position, 0.1f, harvestEntities, -1);
                        for (int i = 0; i < harvestEntities.Count; i++)
                        {
                            var item = harvestEntities[i];
                            if (Harvest(item))
                            {
                                break;
                            }
                        }

                        return;
                    }
                }
            }

            public virtual bool Harvest(BaseEntity ent)
            {
                if (ent is CollectibleEntity collectibleEntity && collectibleEntity.IsFood())
                {
                    PickupHarvestable(collectibleEntity);
                    return true;
                }

                if (ent is GrowableEntity growableEntity && growableEntity.CanPick(null))
                {
                    PickFruit(growableEntity);
                    return true;
                }

                return false;
            }

            public void PickFruit(GrowableEntity growableEntity)
            {
                if (!growableEntity.CanPick(null))
                {
                    return;
                }

                growableEntity.harvests++;

                IStorageContainer storageContainer = null;
                for (int i = 0; i < KaruzaEntity.StorageContainers.Count; i++)
                {
                    var tempContainer = KaruzaEntity.StorageContainers[i];
                    tempContainer = KaruzaEntity.StorageContainers[0];

                    if (tempContainer.inventory.IsFull())
                    {
                        continue;
                    }

                    storageContainer = tempContainer;
                    break;
                }

                GiveFruit(growableEntity, storageContainer, growableEntity.CurrentPickAmount);

                RandomItemDispenser randomItemDispenser = PrefabAttribute.server.Find<RandomItemDispenser>(growableEntity.prefabID);
                if (randomItemDispenser != null)
                {
                    DistributeItems(randomItemDispenser, storageContainer, base.transform.position);
                }

                growableEntity.ResetSeason();
                if (growableEntity.Properties.pickEffect.isValid)
                {
                    Effect.server.Run(growableEntity.Properties.pickEffect.resourcePath, base.transform.position, Vector3.up);
                }

                if (growableEntity.harvests >= growableEntity.Properties.maxHarvests)
                {
                    if (growableEntity.Properties.disappearAfterHarvest)
                    {
                        growableEntity.Die();
                    }
                    else
                    {
                        growableEntity.ChangeState(PlantProperties.State.Dying, resetAge: true);
                    }
                }
                else
                {
                    growableEntity.ChangeState(PlantProperties.State.Mature, resetAge: true);
                }
            }


            public void GiveFruit(GrowableEntity growableEntity, IStorageContainer container, int amount)
            {
                if (amount <= 0)
                {
                    return;
                }

                bool flag = growableEntity.Properties.pickupItem.condition.enabled;
                if (flag)
                {
                    for (int i = 0; i < amount; i++)
                    {
                        GiveFruit(growableEntity, container, 1, flag);
                    }
                }
                else
                {
                    GiveFruit(growableEntity, container, amount, flag);
                }
            }

            public void GiveFruit(GrowableEntity growableEntity, IStorageContainer container, int amount, bool applyCondition)
            {
                Item item = ItemManager.Create(growableEntity.Properties.pickupItem, amount, 0uL);
                if (applyCondition)
                {
                    item.conditionNormalized = growableEntity.Properties.fruitVisualScaleCurve.Evaluate(growableEntity.StageProgressFraction);
                }

                item.amount = item.amount * Modifier;

                if (!object.ReferenceEquals(container, null))
                {
                    BasePlayer player = null;
                    if (karuzaEntityIsVehicle && vehicle.CustomHasDriver())
                    {
                        player = vehicle.CustomGetDriver();
                    }

                    Interface.CallHook("OnGrowableGathered", growableEntity, item, player);
                    container.inventory.GiveItem(item);
                }
                else
                {
                    item.Drop(base.transform.position + Vector3.up * 0.5f, Vector3.up * 1f);
                }
            }

            public void PickupHarvestable(CollectibleEntity growableEntity)
            {
                if (growableEntity.itemList == null || Interface.CallHook("OnCollectiblePickup", growableEntity, null) != null)
                {
                    return;
                }

                IStorageContainer storageContainer = null;
                for (int i = 0; i < KaruzaEntity.StorageContainers.Count; i++)
                {
                    var tempContainer = KaruzaEntity.StorageContainers[i];
                    tempContainer = KaruzaEntity.StorageContainers[0];

                    if (tempContainer.inventory.IsFull())
                    {
                        continue;
                    }

                    storageContainer = tempContainer;
                    break;
                }

                var array = growableEntity.itemList;
                foreach (ItemAmount itemAmount in array)
                {
                    Item item = ItemManager.Create(itemAmount.itemDef, (int)itemAmount.amount, 0uL);
                    if (item == null)
                    {
                        continue;
                    }

                    item.amount = item.amount * Modifier;
                    if (!object.ReferenceEquals(storageContainer, null))
                    {
                        storageContainer.inventory.GiveItem(item);
                    }
                    else
                    {
                        item.Drop(base.transform.position + Vector3.up * 0.5f, Vector3.up);
                    }
                }

                growableEntity.itemList = null;
                if (growableEntity.pickupEffect.isValid)
                {
                    Effect.server.Run(growableEntity.pickupEffect.resourcePath, base.transform.position, base.transform.up);
                }

                RandomItemDispenser randomItemDispenser = PrefabAttribute.server.Find<RandomItemDispenser>(growableEntity.prefabID);
                if (randomItemDispenser != null)
                {
                    DistributeItems(randomItemDispenser, storageContainer, base.transform.position);
                }

                growableEntity.Kill();
            }

            public void DistributeItems(RandomItemDispenser dispenser, IStorageContainer inventory, Vector3 distributorPosition)
            {
                var chances = dispenser.Chances;
                for (int i = 0; i < chances.Length; i++)
                {
                    var itemChance = chances[i];
                    bool flag = TryAward(dispenser, itemChance, inventory, distributorPosition);
                    if (dispenser.OnlyAwardOne && flag)
                    {
                        break;
                    }
                }
            }

            private bool TryAward(RandomItemDispenser dispenser, RandomItemChance itemChance, IStorageContainer container, Vector3 distributorPosition)
            {
                float num = UnityEngine.Random.Range(0f, 1f);
                if (itemChance.Chance >= num)
                {
                    Item item = ItemManager.Create(itemChance.Item, itemChance.Amount, 0uL);
                    if (item != null)
                    {
                        if (!object.ReferenceEquals(container, null))
                        {
                            container.inventory.GiveItem(item);
                        }
                        else
                        {
                            item.Drop(distributorPosition + Vector3.up * 0.5f, Vector3.up);
                        }
                    }

                    return true;
                }

                return false;
            }
        }

        public class TowTrigger : BaseMonoBehaviour
        {
            public static Dictionary<int, TowTrigger> CachedTriggers = new Dictionary<int, TowTrigger>();

            public TowTriggerType TowTriggerType;
            public int InterestLayers;
            public bool IsConnected;
            public TowTrigger ConnectedTrigger;
            public IVehicle ConnectedVehicle;
            public IVehicle Vehicle;

            bool IsHitch { get { return TowTriggerType == TowTriggerType.Hitch || TowTriggerType == TowTriggerType.SemiHitch; } }

            TowTriggerConfig config;
            BoxCollider bc;
            ConfigurableJoint joint;
            SpecialButton btn;
            int instanceID = 0;
            float nextConnectionAllowed;
            bool hasWheels;
            WheelCollider[] wheels;

            void Awake()
            {
                bc = gameObject.AddComponent<BoxCollider>();
            }

            void OnDestroy()
            {
                if (CachedTriggers == null)
                {
                    return;
                }

                CachedTriggers.Remove(instanceID);
                DestroyConnection();
            }

            public void Initialize(IVehicle vehicle, TowTriggerConfig config)
            {
                this.config = config;
                this.Vehicle = vehicle;
                TowTriggerType = config.TowTriggerType;

                bc.size = config.Size;
                bc.center = config.Location;
                bc.enabled = true;
                bc.excludeLayers = TOW_TRIGGER_IGNORE_COL_MASK;

                instanceID = bc.GetInstanceID();

                CachedTriggers.Add(instanceID, this);

                if (IsHitch)
                {
                    bc.gameObject.layer = (int)Layer.Trigger;
                    bc.includeLayers = LayerMask.GetMask(LayerMask.LayerToName((int)Layer.Vehicle_Large));

                    InterestLayers = bc.includeLayers;
                    bc.isTrigger = true;

                    btn = PropUtilities.CreateCustomEntity<SpecialButton>(Vector3.zero, Vector3.zero, Vector3.one, PREFAB_PRESS_BUTTON, vehicle.BaseVehicle);
                    btn.limitNetworking = true;
                    btn.PressAction += DisconnectConnected;
                }
                else
                {
                    bc.gameObject.layer = (int)Layer.Vehicle_Large;
                    bc.includeLayers = LayerMask.GetMask(LayerMask.LayerToName((int)Layer.Trigger));
                    bc.excludeLayers = LayerMask.GetMask(LayerMask.LayerToName((int)Layer.Default));
                    InterestLayers = bc.includeLayers;
                }

                gameObject.transform.localPosition = Vector3.zero;
                gameObject.transform.SetParent(this.Vehicle.BaseVehicle.transform, false);

                wheels = vehicle.BaseVehicle.GetComponentsInChildren<WheelCollider>();
                hasWheels = wheels.Length > 0;
            }

            public void OnTriggerEnter(Collider collider)
            {
                if (IsConnected)
                {
                    return;
                }

                using (TimeWarning.New(TOWTRIGGER_ONTRIGGERENTER))
                {
                    int num = 1 << collider.gameObject.layer;
                    if ((InterestLayers & num) != num)
                    {
                        return;
                    }

                    if (!CachedTriggers.ContainsKey(collider.GetInstanceID()))
                    {
                        return;
                    }

                    var collidingTrigger = CachedTriggers[collider.GetInstanceID()];
                    if (!CanConnect(collidingTrigger))
                    {
                        return;
                    }

                    Connect(collidingTrigger);
                }
            }

            public void DisconnectConnected(BasePlayer basePlayer)
            {
                if (!IsConnected)
                {
                    return;
                }

                DestroyConnection();
            }

            void DestroyConnection()
            {
                Destroy(joint);

                var isReceiver = false;
                if (!IsHitch)
                {
                    isReceiver = true;
                    var thisVeh = Vehicle;
                    if (thisVeh != null)
                    {
                        if (config.ListenForLightsFromHitch)
                        {
                            if (thisVeh.BaseVehicle != null)
                            {
                                var thisVehLightsOn = thisVeh.BaseVehicle.HasFlag(Flags.Reserved5);
                                if (thisVehLightsOn)
                                {
                                    thisVeh.BaseVehicle.LightToggle(null);
                                }
                            }

                            if (thisVeh.IsPowered)
                            {
                                thisVeh.TogglePower(false, true);
                            }
                        }

                        thisVeh.RemoveTowingVehicle();
                    }
                }

                if (ConnectedVehicle != null && !isReceiver)
                {
                    Vehicle.RemoveTowedVehicle();
                    ConnectedVehicle.RemoveTowingVehicle();
                }

                if (ConnectedTrigger != null)
                {
                    ConnectedTrigger.IsConnected = false;
                    ConnectedTrigger.ConnectedTrigger = null;
                    ConnectedTrigger.DestroyConnection();

                }

                ConnectedVehicle = null;

                bc.enabled = true;
                IsConnected = false;

                if (btn != null)
                {
                    btn.limitNetworking = true;
                }

                ConnectedTrigger = null;
                enabled = false;
                nextConnectionAllowed = Time.realtimeSinceStartup + 2f;
                Vehicle.BaseVehicle.rigidBody.interpolation = RigidbodyInterpolation.None;

            }

            public void Connect(TowTrigger requestingTrigger)
            {
                switch (requestingTrigger.TowTriggerType)
                {
                    case TowTriggerType.SemiReceiver:
                    case TowTriggerType.Receiver:
                        CreateJoint();

                        requestingTrigger.bc.enabled = false;
                        bc.enabled = false;

                        requestingTrigger.Vehicle.BaseVehicle.rigidBody.WakeUp();

                        // Temporarily align towed vehicle for anchors to properly calculate
                        var eulerAngles = requestingTrigger.Vehicle.BaseVehicle.transform.eulerAngles;
                        var tempEularAngles = Vehicle.BaseVehicle.transform.eulerAngles;
                        requestingTrigger.Vehicle.BaseVehicle.transform.eulerAngles = tempEularAngles;

                        joint.connectedBody = requestingTrigger.Vehicle.BaseVehicle.rigidBody;
                        joint.anchor = config.TowAnchorPosition;
                        joint.connectedAnchor = requestingTrigger.config.TowAnchorPosition;

                        requestingTrigger.Vehicle.BaseVehicle.transform.eulerAngles = eulerAngles;

                        requestingTrigger.Vehicle.AddTowingVehicle();
                        Vehicle.AddTowedVehicle();

                        Instance.NextTick(() =>
                        {
                            // Hack to fix jittery behavior
                            requestingTrigger.Vehicle.BaseVehicle.rigidBody.interpolation = RigidbodyInterpolation.Interpolate;
                            requestingTrigger.Vehicle.BaseVehicle.rigidBody.interpolation = RigidbodyInterpolation.None;

                            Vehicle.BaseVehicle.rigidBody.interpolation = RigidbodyInterpolation.Interpolate;
                            Vehicle.BaseVehicle.rigidBody.interpolation = RigidbodyInterpolation.None;
                        });

                        IsConnected = true;
                        ConnectedTrigger = requestingTrigger;
                        ConnectedTrigger.IsConnected = true;
                        ConnectedTrigger.ConnectedTrigger = this;
                        ConnectedTrigger.enabled = true;
                        ConnectedVehicle = ConnectedTrigger.Vehicle;
                        enabled = true;

                        var btnPos = config.TowAnchorPosition;
                        var btnRot = Vector3.zero;
                        if (config.ReleaseButtonSettings != null && config.ReleaseButtonSettings.Enabled)
                        {
                            btnPos = config.ReleaseButtonSettings.Location;
                            btnRot = config.ReleaseButtonSettings.Rotation;
                        }

                        btn.transform.localPosition = btnPos;
                        btn.transform.localEulerAngles = btnRot;
                        btn.limitNetworking = false;
                        btn.SendNetworkUpdate();
                        break;

                    case TowTriggerType.Hitch:
                    case TowTriggerType.SemiHitch:
                    default:
                        return;
                }
            }

            void CreateJoint()
            {
                joint = Vehicle.BaseVehicle.gameObject.AddComponent<ConfigurableJoint>();

                joint.breakForce = float.PositiveInfinity;
                joint.breakTorque = float.PositiveInfinity;

                //joint.breakForce = 40000f;
                //joint.breakTorque = 350;
                Invoke(UpdateBreakForce, 1f);

                joint.axis = Vector3.up;
                joint.secondaryAxis = Vector3.forward;
                joint.xMotion = ConfigurableJointMotion.Locked;
                joint.zMotion = ConfigurableJointMotion.Locked;
                joint.yMotion = ConfigurableJointMotion.Locked;
                joint.angularXMotion = ConfigurableJointMotion.Free;
                joint.angularZMotion = ConfigurableJointMotion.Free;
                joint.angularYMotion = ConfigurableJointMotion.Free;
                joint.linearLimitSpring = new SoftJointLimitSpring() { damper = config.LinearLimitSpringDamper, spring = config.LinearLimitSpringStiffness };

                joint.linearLimit = new SoftJointLimit() { limit = 5f, contactDistance = 0, bounciness = 0 };
                joint.angularXLimitSpring = new SoftJointLimitSpring();
                joint.lowAngularXLimit = new SoftJointLimit() { limit = -5 };
                joint.highAngularXLimit = new SoftJointLimit() { limit = 5 };
                joint.angularYZLimitSpring = new SoftJointLimitSpring();
                joint.angularYLimit = new SoftJointLimit() { limit = 120 };
                joint.angularZLimit = new SoftJointLimit() { limit = 5 };
                joint.xDrive = new JointDrive() { maximumForce = float.PositiveInfinity };
                joint.yDrive = new JointDrive() { maximumForce = float.PositiveInfinity };
                joint.zDrive = new JointDrive() { maximumForce = float.PositiveInfinity };
                joint.rotationDriveMode = RotationDriveMode.XYAndZ;
                joint.angularXDrive = new JointDrive() { maximumForce = float.PositiveInfinity, useAcceleration = true };
                joint.angularYZDrive = new JointDrive() { maximumForce = float.PositiveInfinity, useAcceleration = true };
                joint.slerpDrive = new JointDrive() { maximumForce = float.PositiveInfinity };
                joint.projectionMode = JointProjectionMode.None;
                joint.projectionDistance = 0.1f;
                joint.swapBodies = true;
                joint.enableCollision = true;
                joint.enablePreprocessing = true;
                joint.autoConfigureConnectedAnchor = false;
                joint.massScale = config.MassScale;
                joint.connectedMassScale = config.ConnectedMassScale;
            }

            public bool CanConnect(TowTrigger requestingTrigger)
            {
                if (IsConnected)
                {
                    return false;
                }

                if (nextConnectionAllowed > Time.realtimeSinceStartup)
                {
                    return false;
                }

                if (this.Vehicle.BaseVehicle.rigidBody.linearVelocity.magnitude > 2)
                {
                    return false;
                }

                switch (requestingTrigger.TowTriggerType)
                {
                    case TowTriggerType.Hitch:
                        return TowTriggerType == TowTriggerType.Receiver;
                    case TowTriggerType.Receiver:
                        return TowTriggerType == TowTriggerType.Hitch;
                    case TowTriggerType.SemiHitch:
                        return TowTriggerType == TowTriggerType.SemiReceiver;
                    case TowTriggerType.SemiReceiver:
                        return TowTriggerType == TowTriggerType.SemiHitch;
                    default:
                        return true;
                }
            }

            void UpdateBreakForce()
            {
                joint.breakForce = config.BreakForce;
                joint.breakTorque = config.BreakTorque;
            }

            void OnJointBreak(float breakForce)
            {
                DestroyConnection();
            }

            // Receiver triggers will listen for inputs from the hitch
            void LateUpdate()
            {
                if (!IsConnected)
                {
                    return;
                }

                if ((IsHitch && joint == null) || ConnectedTrigger.Vehicle.IsDead())
                {
                    DestroyConnection();
                    return;
                }

                if (config.ListenForLightsFromHitch)
                {
                    var towingVeh = ConnectedTrigger.Vehicle;
                    var towVehLightsOn = towingVeh.BaseVehicle.HasFlag(Flags.Reserved5);
                    var thisVeh = Vehicle;
                    var thisVehLightsOn = thisVeh.BaseVehicle.HasFlag(Flags.Reserved5);

                    if (thisVeh.IsPowered != towingVeh.IsPowered)
                    {
                        thisVeh.TogglePower(towingVeh.IsPowered, true);
                    }

                    if (thisVehLightsOn != towVehLightsOn)
                    {
                        thisVeh.CustomLightToggle(towVehLightsOn);
                    }
                }
            }
        }

        #endregion

        #region Custom Classes

        public class WeaponSystem
        {
            public WeaponConfiguration Config;
            public float LastFiredTime;
            public int CurrentBarrel;
            public int MagazineCapacity;
            public Action<BasePlayer> OnWeaponFired;
            public Rigidbody RigidBody;
            public bool HasRigidBody;

            List<DamageTypeEntry> damageTypes;
            public List<DamageTypeEntry> DamageTypes
            {
                get
                {
                    if (damageTypes == null)
                    {
                        damageTypes = new List<DamageTypeEntry>();
                        if (Config.DamageTypes != null)
                        {
                            for (int i = 0; i < Config.DamageTypes.Count; i++)
                            {
                                var dt = Config.DamageTypes[i];
                                damageTypes.Add(new DamageTypeEntry() { type = dt.Type, amount = dt.Amount });
                            }
                        }
                    }

                    return damageTypes;
                }
            }
        }

        public class Weakspot
        {
            public BaseCombatEntity KaruzaEntity;
            public float MaxHealth;
            public float Health;
            public float HealthFractionOnDestroyed = 0.5f;

            public GameObjectRef DestroyedParticles;
            public GameObjectRef DamagedParticles;
            public Vector3 EffectLocalPosition;

            public bool HideWeakspotOnDeath;
            public bool LoseControlOnDeath;
            public bool IsDestroyed;

            public List<BaseEntity> Entities = new List<BaseEntity>();

            List<Tuple<float, LowHealthEffectConfig>> lowHealthThresholds = new List<Tuple<float, LowHealthEffectConfig>>();
            SpecialBaseCombatEntity lowHealthPrefab;
            bool lowHealthEffectsValid;
            bool invokingLowHealthFx;
            LowHealthEffectConfig spawnedLowHealthEffect;

            public float HealthFraction()
            {
                return Health / MaxHealth;
            }

            public void Hurt(float amount, HitInfo info)
            {
                if (IsDestroyed)
                {
                    return;
                }

                Health -= amount;
                Effect.server.Run(DamagedParticles.resourcePath, KaruzaEntity, 0, EffectLocalPosition, Vector3.up, null);
                if (Health <= 0f)
                {
                    Health = 0f;
                    WeakspotDestroyed();
                }

                InvokeLowHealthEffect(0.1f);
            }

            public void Heal(float amount)
            {
                Health += amount;
                if (Health > 0 && IsDestroyed)
                {
                    IsDestroyed = false;
                    if (HideWeakspotOnDeath)
                    {
                        foreach (var weakspotEntity in Entities)
                        {
                            weakspotEntity.limitNetworking = false;
                            weakspotEntity.SetActive(true);
                        }
                    }
                }

                InvokeLowHealthEffect(0.1f);
            }

            public void WeakspotDestroyed()
            {
                IsDestroyed = true;
                Effect.server.Run(DestroyedParticles.resourcePath, KaruzaEntity, 0, EffectLocalPosition, Vector3.up, null);
                KaruzaEntity.Hurt(KaruzaEntity.MaxHealth() * HealthFractionOnDestroyed, DamageType.Generic, null, useProtection: false);

                if (HideWeakspotOnDeath)
                {
                    foreach (var weakspotEntity in Entities)
                    {
                        weakspotEntity.limitNetworking = true;
                        weakspotEntity.SetActive(false);
                    }
                }

                if (LoseControlOnDeath && KaruzaEntity is IVehicle veh)
                {
                    veh.UpdateLoseControl(true);
                }
            }

            public void InitializeLowHealthEffects(LowHealthEffectSettings lowHealthEffectSetting)
            {
                if (lowHealthEffectSetting == null || !lowHealthEffectSetting.Enabled)
                {
                    return;
                }

                var effects = lowHealthEffectSetting.Effects.OrderBy(e => e.HealthPercent).ToList();
                foreach (var effect in effects)
                {
                    if (!effect.Enabled)
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(effect.PrefabPath) || effect.HealthPercent <= 0)
                    {
                        continue;
                    }

                    var lowHealthThreshold = MaxHealth * effect.HealthPercent;
                    if (lowHealthThreshold > 0)
                    {
                        lowHealthThresholds.Add(new Tuple<float, LowHealthEffectConfig>(lowHealthThreshold, effect));
                    }
                }

                lowHealthEffectsValid = lowHealthThresholds.Count > 0;
            }

            private void InvokeLowHealthEffect(float time)
            {
                if (!lowHealthEffectsValid)
                {
                    return;
                }

                invokingLowHealthFx = true;
                KaruzaEntity.Invoke(UpdateLowHealthEffect, time);
            }

            private void UpdateLowHealthEffect()
            {
                invokingLowHealthFx = false;
                if (!lowHealthEffectsValid)
                {
                    return;
                }

                var spawnEffect = false;
                LowHealthEffectConfig spawnEffectConfig = null;

                if (!IsDestroyed)
                {
                    foreach (var kv in lowHealthThresholds)
                    {
                        if (kv.Item1 >= Health)
                        {
                            spawnEffect = true;
                            spawnEffectConfig = kv.Item2;
                            break;
                        }
                    }
                }

                if (spawnEffect)
                {
                    if (spawnedLowHealthEffect != null)
                    {
                        if (spawnedLowHealthEffect == spawnEffectConfig)
                        {
                            return;
                        }

                        lowHealthPrefab.Kill(DestroyMode.None);
                    }

                    lowHealthPrefab = PropUtilities.CreateCustomEntity(spawnEffectConfig.Location, spawnEffectConfig.Rotation, spawnEffectConfig.Scale, spawnEffectConfig.PrefabPath, KaruzaEntity, spawnEffectConfig.SkinId);
                    lowHealthPrefab.DestroyParentOnDestroy = false;
                    spawnedLowHealthEffect = spawnEffectConfig;
                }
                else if (spawnedLowHealthEffect != null)
                {
                    lowHealthPrefab.Kill(DestroyMode.None);
                    spawnedLowHealthEffect = null;
                }
            }
        }

        public static class BezierCurve
        {
            //Update the positions of the rope section
            public static void GetBezierCurve(Vector3 A, Vector3 B, Vector3 C, Vector3 D, List<Vector3> allRopeSections)
            {
                //The resolution of the line
                //Make sure the resolution is adding up to 1, so 0.3 will give a gap at the end, but 0.2 will work
                float resolution = 0.1f;

                float t = 0;

                while (t <= 1f)
                {
                    //Find the coordinates between the control points with a Bezier curve
                    Vector3 newPos = DeCasteljausAlgorithm(A, B, C, D, t);

                    allRopeSections.Add(newPos);

                    //Which t position are we at?
                    t += resolution;
                }

                allRopeSections.Add(D);
            }

            //The De Casteljau's Algorithm
            static Vector3 DeCasteljausAlgorithm(Vector3 A, Vector3 B, Vector3 C, Vector3 D, float t)
            {
                //Linear interpolation = lerp = (1 - t) * A + t * B
                //Could use Vector3.Lerp(A, B, t)

                //To make it faster
                float oneMinusT = 1f - t;

                //Layer 1
                Vector3 Q = oneMinusT * A + t * B;
                Vector3 R = oneMinusT * B + t * C;
                Vector3 S = oneMinusT * C + t * D;

                //Layer 2
                Vector3 P = oneMinusT * Q + t * R;
                Vector3 T = oneMinusT * R + t * S;

                //Final interpolated position
                Vector3 U = oneMinusT * P + t * T;

                ////Layer 1
                //Vector3 Q = oneMinusT * Vector3.Lerp(A, B, t);
                //Vector3 R = oneMinusT * Vector3.Lerp(B, C, t);
                //Vector3 S = oneMinusT * Vector3.Lerp(C, D, t);

                ////Layer 2
                //Vector3 P = oneMinusT * Vector3.Lerp(Q, R, t);
                //Vector3 T = oneMinusT * Vector3.Lerp(R, S, t);

                ////Final interpolated position
                //Vector3 U = oneMinusT * Vector3.Lerp(P, T, t);


                return U;
            }
        }

        #endregion

        #region Custom Behaviors

        public class PlayerEjector : MonoBehaviour
        {
            BasePlayer player;
            BaseMountable chair;
            DroppedItem worldItem;
            TimedExplosive rocket;
            EjectSettings ejectSettings;
            List<Collider> cols = new List<Collider>();

            float currentTime = 0;
            float lastUpdate = 0;
            float launchTime = 0;
            float updateRate = 0.01F;

            bool airborne = false;
            bool rocketDestroyed = false;
            bool collidersEnabled = false;

            void Awake()
            {
                worldItem = this.gameObject.GetComponent<DroppedItem>();
            }

            public void Initialize(EjectSettings ejectSettings)
            {
                this.ejectSettings = ejectSettings;

                var worldItemCols = worldItem.GetComponentsInChildren<Collider>();
                for (int i = 0; i < worldItemCols.Length; i++)
                {
                    var worldItemCol = worldItemCols[i];
                    if (worldItemCol.isTrigger)
                    {
                        continue;
                    }

                    worldItemCol.isTrigger = true;
                    cols.Add(worldItemCol);
                }

                chair = GameManager.server.CreateEntity(ejectSettings.SeatPrefab, new Vector3(0, -0.6f, 0), Quaternion.Euler(Vector3.zero.normalized)) as BaseMountable;
                chair.enableSaving = false;
                chair.legacyDismount = true;
                chair.isMobile = true;
                chair.globalBroadcast = true;

                var chairCols = chair.GetComponentsInChildren<Collider>();
                for (int i = 0; i < chairCols.Length; i++)
                {
                    var chairCol = chairCols[i];
                    if (chairCol.isTrigger)
                    {
                        continue;
                    }

                    chairCol.isTrigger = true;
                }

                chair.SetParent(this.worldItem);
                chair.Spawn();

                AddRocket();
                launchTime = Time.time;
            }

            private void AddRocket()
            {
                this.rocket = GameManager.server.CreateEntity("assets/prefabs/ammo/rocket/rocket_smoke.prefab", Vector3.zero, Quaternion.Euler(new Vector3(90, 0, 0))) as TimedExplosive;
                this.rocket.SetParent(this.worldItem);
                this.rocket.enabled = false;
                this.rocket.timerAmountMin = 999999999f;
                var coll = this.rocket.gameObject.GetComponent<ServerProjectile>();
                coll.impacted = true;
                this.rocket.Spawn();
            }

            public void MountPlayer(BasePlayer toMount)
            {
                chair.MountPlayer(toMount);
                toMount.SendNetworkUpdateImmediate();
                this.player = toMount;
                airborne = true;
            }

            void LateUpdate()
            {
                if (!airborne)
                {
                    return;
                }

                currentTime = UnityEngine.Time.time;
                if (!rocketDestroyed && currentTime > launchTime + ejectSettings.RocketLifeSeconds)
                {
                    if (this.rocket != null)
                    {
                        this.rocket.Kill();
                    }

                    rocketDestroyed = true;
                }

                if (!collidersEnabled && currentTime > launchTime + ejectSettings.CollisionDelay)
                {
                    for (int i = 0; i < cols.Count; i++)
                    {
                        var col = cols[i];
                        col.isTrigger = false;
                    }

                    collidersEnabled = true;
                }

                if (object.ReferenceEquals(player, null))
                {
                    Destroy(this);
                    return;
                }

                if (currentTime < lastUpdate + updateRate)
                {
                    return;
                }

                lastUpdate = currentTime;
                if (player.WaterFactor() > 0F)
                {
                    DismountPlayer();
                }
            }

            public void DismountPlayer()
            {
                var maybeMounted = chair.GetMounted();
                if (maybeMounted != null)
                {
                    chair.DismountPlayer(maybeMounted);
                }

                Destroy(this);
            }

            void OnDestroy()
            {
                if (this.worldItem != null)
                {
                    var worldItem = this.worldItem.GetComponent<DroppedItem>();
                    if (worldItem != null)
                    {
                        worldItem.DestroyItem();
                    }

                    var currentBaseNet = this.worldItem.GetComponent<BaseNetworkable>();
                    if (currentBaseNet != null && !currentBaseNet.IsDestroyed)
                    {
                        currentBaseNet.Kill(BaseNetworkable.DestroyMode.None);
                    }

                    Destroy(this.worldItem.gameObject);
                }

                if (this.chair != null)
                {
                    var worldItem = this.chair.GetComponent<DroppedItem>();
                    if (worldItem != null)
                    {
                        worldItem.DestroyItem();
                    }

                    var currentBaseNet = this.chair.GetComponent<BaseNetworkable>();
                    if (currentBaseNet != null && !currentBaseNet.IsDestroyed)
                    {
                        currentBaseNet.Kill(BaseNetworkable.DestroyMode.None);
                    }

                    Destroy(this.chair.gameObject);
                }

                if (this.rocket != null)
                {
                    var currentBaseNet = this.rocket.GetComponent<BaseNetworkable>();
                    if (currentBaseNet != null && !currentBaseNet.IsDestroyed)
                    {
                        currentBaseNet.Kill(BaseNetworkable.DestroyMode.None);
                    }

                    Destroy(this.rocket.gameObject);
                }
            }

            void OnCollisionEnter(Collision collision)
            {
                if (player == null)
                {
                    DismountPlayer();
                    return;
                }

                if (!player.IsConnected)
                {
                    DismountPlayer();
                    return;
                }

                if (!player.IsAlive())
                {
                    DismountPlayer();
                    return;
                }

                if (airborne)
                {
                    chair.transform.localPosition = new Vector3(0, 0, -0.6F);
                    chair.transform.hasChanged = true;
                    airborne = false;
                }

                if (chair.IsMounted())
                {
                    DismountPlayer();
                    player.ApplyFallDamageFromVelocity(collision.relativeVelocity.magnitude * -1);
                }
                else
                {
                    Destroy(this);
                }
            }
        }

        public class ColliderEngineWatcher : MonoBehaviour
        {
            IVehicle vehicle;
            Collider collider;

            public BaseColliderConfiguration Config;

            void Awake()
            {
                collider = GetComponent<Collider>();
                vehicle = GetComponentInParent<IVehicle>();
                enabled = false;
            }

            public void Initialize()
            {
                if (Config.RequireVehicleMounted)
                {
                    vehicle.OnMountedChange += OnMountedChange;
                    collider.isTrigger = true;
                }
                else if (Config.RequireEngineOff)
                {
                    vehicle.OnToggled += RequireEngineOff_OnEngineToggle;
                }
            }

            void OnMountedChange()
            {
                var vehicleMounted = vehicle.BaseVehicle.IsMounted();
                if (collider.isTrigger != !vehicleMounted)
                {
                    collider.isTrigger = !vehicleMounted;
                }
            }

            void RequireEngineOff_OnEngineToggle(bool engineOn)
            {
                if (collider.isTrigger != engineOn)
                {
                    collider.isTrigger = engineOn;
                }
            }
        }

        public class BoostWatcher : MonoBehaviour
        {
            IVehicle vehicle;
            BaseEntity entity;

            void Awake()
            {
                vehicle = GetComponentInParent<IVehicle>();
                entity = GetComponent<BaseEntity>();
                entity.limitNetworking = true;
                enabled = false;

                vehicle.OnBoostToggle += OnBoostToggle;
            }

            void OnBoostToggle(bool boostOn)
            {
                entity.limitNetworking = !boostOn;
            }
        }

        public class HoverEngine : MonoBehaviour
        {
            IVehicle vehicle;

            public HoverEngineConfig Config;
            public bool IsGrounded;

            int layerMask;
            float lastHitDist;
            float forceAmount;
            RaycastHit hit;
            Vector3 position;


            void Awake()
            {
                this.vehicle = this.GetComponentInParent<IVehicle>();
                this.vehicle.OnToggled += OnEngineToggle;
                enabled = false;
            }

            public void Initialize(HoverEngineConfig config)
            {
                this.Config = config;
                layerMask = (int)Config.LayerMask;
            }

            public void FixedUpdate()
            {
                UpdateHover();
            }

            void OnEngineToggle(bool engineOn)
            {
                enabled = engineOn;
            }

            private float HooksLawDampen(float distance, int localStrength, int dampening)
            {
                forceAmount = localStrength * (Config.HoverDistance - distance) + (dampening * (lastHitDist - distance));
                forceAmount = Mathf.Max(0f, forceAmount);
                lastHitDist = distance;

                return forceAmount;
            }

            private void UpdateHover()
            {
                if (Physics.Raycast(transform.position, transform.TransformDirection(-Vector3.up), out hit, Config.HoverDistance, layerMask))
                {
                    float forceAmount = HooksLawDampen(hit.distance, Config.HoverStrength, Config.Dampening);
                    this.vehicle.BaseVehicle.rigidBody.AddForceAtPosition(transform.up * forceAmount, transform.position);
                    IsGrounded = true;
                }
                else
                {
                    lastHitDist = Config.HoverDistance * 1.1f;
                    IsGrounded = false;
                }
            }
        }

        private class GuidedRocket : MonoBehaviour
        {
            public float AngleModifier { get; set; } = 45f;
            private ServerProjectile projectile;
            private BaseEntity target;
            BaseEntity entity;
            BasePlayer player;
            bool isTracking = true;

            private void Awake()
            {
                projectile = GetComponent<ServerProjectile>();
                entity = projectile.GetComponent<BaseEntity>();
                player = entity.creatorEntity as BasePlayer;
            }

            private void FixedUpdate()
            {
                UpdateRocketPosition();
            }

            private void UpdateRocketPosition()
            {
                if (!isTracking || entity.IsDestroyed)
                {
                    return;
                }

                if (player.IsDead() || !player.isMounted)
                {
                    isTracking = false;
                    return;
                }

                Vector3 aimAngle = player.serverInput.current.aimAngles;
                Vector3 targetPos = new Ray(player.transform.position, Quaternion.Euler(aimAngle) * Vector3.forward).GetPoint(500);

                Vector3 targetDirection = (targetPos - projectile.transform.position).normalized;
                Vector3 currentDirection = (projectile.transform.localRotation * Vector3.forward);

                float maxAngleChange = (AngleModifier * UnityEngine.Time.fixedDeltaTime);
                float angleChange = Vector3.Angle(targetDirection, currentDirection);
                float maxChange = maxAngleChange < angleChange ? (maxAngleChange / angleChange) : 1;
                Vector3 change = Vector3.Slerp(currentDirection, targetDirection, maxChange);
                Vector3 modifiedDirection = change.normalized;



                //Vector3 aimAngle = player.serverInput.current.aimAngles;
                //Vector3 targetPos = new Ray(player.transform.position, Quaternion.Euler(aimAngle) * Vector3.forward).GetPoint(500);

                //float distance = Vector3.Distance(projectile.transform.position, targetPos);
                //Vector3 targetDirection = (targetPos - projectile.transform.position).normalized;


                projectile.InitializeVelocity(targetDirection * projectile.speed);
            }

            private void OnDestroy()
            {
                BaseEntity entity = projectile.GetComponent<BaseEntity>();

                if (entity != null && !entity.IsDestroyed)
                {
                    entity.Kill();
                }
            }
        }

        public class JointController : MonoBehaviour, IJoint
        {
            IKaruzaCustomEntity karuzaCustomEntity;

            Quaternion targetRot;
            Vector3 targetPos;

            bool? currentState;
            bool updateSyncPosition = true;
            Quaternion offRotation;
            Quaternion onRotation;
            Vector3 offPosition;
            Vector3 onPosition;
            float distance;
            float angle;
            float cachedDegrees;
            float cachedDistance;
            bool hasRotationChange;
            bool hasPositionChange;

            public Action OnJointUpdate { get; set; }
            public JointConfig Config { get; private set; }
            public bool ListenForPlayerInputs;
            public BUTTON OnButton;
            public BUTTON OffButton;
            public SpecialBaseCombatEntity SpecialBaseCombatEntity;

            void Awake()
            {
                SpecialBaseCombatEntity = GetComponent<SpecialBaseCombatEntity>();
                if (!SpecialBaseCombatEntity.syncPosition)
                {
                    updateSyncPosition = false;
                }

                karuzaCustomEntity = SpecialBaseCombatEntity.transform.parent.GetComponent<IKaruzaCustomEntity>();

                enabled = false;
            }

            public void Configure(JointConfig config, bool defaultPositionIsOff = true)
            {
                this.Config = config;
                enabled = true;

                hasRotationChange = !(config.RotationWhenOff == Vector3.zero && config.RotationWhenOn == Vector3.zero);
                if (hasRotationChange)
                {
                    offRotation = Quaternion.Euler(config.RotationWhenOff);
                    onRotation = Quaternion.Euler(config.RotationWhenOn);
                    angle = Quaternion.Angle(offRotation, onRotation);

                    if (defaultPositionIsOff)
                    {
                        targetRot = offRotation;
                    }
                    else
                    {
                        targetRot = onRotation;
                    }

                    SpecialBaseCombatEntity.CachedTransform.localRotation = targetRot;
                }

                hasPositionChange = !(config.PositionWhenOff == Vector3.zero && config.PositionWhenOn == Vector3.zero);
                if (hasPositionChange)
                {
                    offPosition = config.PositionWhenOff;
                    onPosition = config.PositionWhenOn;
                    distance = Vector3.Distance(offPosition, onPosition);

                    if (defaultPositionIsOff)
                    {
                        targetPos = offPosition;
                    }
                    else
                    {
                        targetPos = onPosition;
                    }

                    SpecialBaseCombatEntity.CachedTransform.localPosition = targetPos;
                }

                if ((hasRotationChange || hasPositionChange) && updateSyncPosition)
                {
                    SpecialBaseCombatEntity.syncPosition = true;
                    SpecialBaseCombatEntity.CustomSendNetworkUpdate_Position();
                    SpecialBaseCombatEntity.syncPosition = false;
                }
            }

            public void PlayerServerInput(InputState inputState)
            {
                var rotUpdated = UpdateRotationInput(inputState);
                var posUpdated = UpdatePositionInput(inputState);
            }

            bool UpdateRotationInput(InputState inputState)
            {
                if (!hasRotationChange)
                {
                    return false;
                }

                var rotateDegree = 0;
                if (inputState.IsDown(OnButton))
                {
                    rotateDegree += 1;
                }

                if (inputState.IsDown(OffButton))
                {
                    rotateDegree -= 1;
                }

                if (rotateDegree == 0)
                {
                    return false;
                }

                var newDegree = cachedDegrees + rotateDegree;
                if (newDegree <= 0)
                {
                    newDegree = 0;
                }
                else if (newDegree > angle)
                {
                    newDegree = angle;
                }

                return UpdateRotation(newDegree);
            }

            bool UpdatePositionInput(InputState inputState)
            {
                if (!hasPositionChange)
                {
                    return false;
                }

                var rotateDegree = 0f;
                if (inputState.IsDown(OnButton))
                {
                    rotateDegree += 0.025f;
                }

                if (inputState.IsDown(OffButton))
                {
                    rotateDegree -= 0.025f;
                }

                if (rotateDegree == 0)
                {
                    return false;
                }

                var newDegree = cachedDistance + rotateDegree;
                if (newDegree <= 0)
                {
                    newDegree = 0;
                }
                else if (newDegree > distance)
                {
                    newDegree = distance;
                }

                return UpdatePosition(newDegree);
            }

            public bool UpdateRotation(float rotateDegree)
            {
                if (rotateDegree == cachedDegrees)
                {
                    return false;
                }

                cachedDegrees = rotateDegree;
                var interpolationAmount = cachedDegrees / angle;

                targetRot = Quaternion.Lerp(offRotation, onRotation, interpolationAmount);

                if (updateSyncPosition)
                {
                    SpecialBaseCombatEntity.syncPosition = true;
                }

                enabled = true;

                SpecialBaseCombatEntity.CachedTransform.localRotation = Quaternion.Lerp(SpecialBaseCombatEntity.CachedTransform.localRotation, targetRot, Time.deltaTime * Config.LerpSpeed);

                return true;
            }

            public bool UpdatePosition(float rotateDegree)
            {
                if (rotateDegree == cachedDistance)
                {
                    return false;
                }

                cachedDistance = rotateDegree;
                var interpolationAmount = cachedDistance / distance;

                targetPos = Vector3.Lerp(offPosition, onPosition, interpolationAmount);

                if (updateSyncPosition)
                {
                    SpecialBaseCombatEntity.syncPosition = true;
                }

                enabled = true;

                SpecialBaseCombatEntity.CachedTransform.localPosition = Vector3.Lerp(SpecialBaseCombatEntity.CachedTransform.localPosition, targetPos, Time.deltaTime * Config.LerpSpeed);

                return true;
            }

            public void OnJointToggle(bool wantsOn)
            {
                if (currentState == wantsOn)
                {
                    return;
                }

                currentState = wantsOn;

                if (wantsOn)
                {
                    if (hasRotationChange)
                    {
                        targetRot = onRotation;
                    }

                    if (hasPositionChange)
                    {
                        targetPos = onPosition;
                    }
                }
                else
                {
                    if (hasRotationChange)
                    {
                        targetRot = offRotation;
                    }

                    if (hasPositionChange)
                    {
                        targetPos = offPosition;
                    }
                }

                if ((hasRotationChange || hasPositionChange) && updateSyncPosition)
                {
                    SpecialBaseCombatEntity.syncPosition = true;
                }

                enabled = true;
            }

            public void OnJointWinch(float degrees)
            {
                if (degrees == cachedDegrees)
                {
                    return;
                }

                var interpolationAmount = degrees / angle;
                cachedDegrees = degrees;

                if (hasRotationChange)
                {
                    targetRot = Quaternion.Lerp(offRotation, onRotation, interpolationAmount);
                }

                if (hasPositionChange)
                {
                    targetPos = Vector3.Lerp(offPosition, onPosition, interpolationAmount);
                }

                if ((hasRotationChange || hasPositionChange) && updateSyncPosition)
                {
                    SpecialBaseCombatEntity.syncPosition = true;
                }

                currentState = true;
                enabled = true;
            }

            void FixedUpdate()
            {
                if ((!hasRotationChange || Quaternion.Angle(SpecialBaseCombatEntity.CachedTransform.localRotation, targetRot) <= 0.01f) && (!hasPositionChange || targetPos == SpecialBaseCombatEntity.CachedTransform.localPosition))
                {
                    currentState = null;

                    if ((hasRotationChange || hasPositionChange) && updateSyncPosition)
                    {
                        SpecialBaseCombatEntity.CustomSendNetworkUpdate_Position();
                        SpecialBaseCombatEntity.syncPosition = false;
                    }

                    enabled = false;
                    return;
                }


                if (hasRotationChange)
                {
                    SpecialBaseCombatEntity.CachedTransform.localRotation = Quaternion.Lerp(SpecialBaseCombatEntity.CachedTransform.localRotation, targetRot, Time.deltaTime * Config.LerpSpeed);
                }

                if (hasPositionChange)
                {
                    SpecialBaseCombatEntity.CachedTransform.localPosition = Vector3.Lerp(SpecialBaseCombatEntity.CachedTransform.localPosition, targetPos, Time.deltaTime * Config.LerpSpeed);
                }
            }

            void Update()
            {
                karuzaCustomEntity.OnJointUpdate();
                OnJointUpdate?.Invoke();
            }
        }

        public class VehicleBomb : MonoBehaviour
        {
            private BaseEntity entity;
            private BasePlayer owner;
            private List<DamageTypeEntry> damageTypes;
            private bool isActive = false;
            private float radius = 0;
            private string explosionEffect;
            private float timerAmountMin;
            private float timerAmountMax;
            private float fuseLength;

            public bool InWater
            {
                get
                {
                    return TerrainMeta.WaterMap.GetHeight(this.transform.position) > this.transform.position.y;
                }
            }

            void Awake()
            {
                this.entity = this.GetComponent<BaseEntity>();
                enabled = false;
            }

            void Update()
            {
                if (!isActive)
                    return;

                if (this.InWater)
                {
                    isActive = false;
                    Explode();
                }
            }

            public void Initialize(BasePlayer player, List<DamageTypeEntry> damageTypes, float radius, string explosionEffect, float timerAmountMin, float timerAmountMax)
            {
                owner = player;
                this.damageTypes = damageTypes;
                this.radius = radius;
                this.explosionEffect = explosionEffect;
                this.timerAmountMin = timerAmountMin;
                this.timerAmountMax = timerAmountMax;

                SetFuse();

                enabled = true;
                this.isActive = true;
            }

            private void OnCollisionEnter(Collision collision)
            {
                if (!isActive)
                    return;

                if (fuseLength > 0)
                {
                    Invoke(nameof(Explode), fuseLength);
                }
                else
                {
                    Explode();
                }
            }

            void OnDestroy()
            {
                var explosionFx = explosionEffect;
                if (string.IsNullOrEmpty(explosionFx))
                {
                    explosionFx = "assets/content/vehicles/mlrs/effects/pfx_mlrs_rocket_explosion_air.prefab";
                }

                Effect.server.Run(explosionFx, this.transform.position, Vector3.zero, null, true);
                DamageUtil.RadiusDamage(owner, this.entity, this.transform.position, 2, radius, damageTypes, 1075980544, true);

                if (this.entity != null)
                {
                    var currentBaseNet = this.entity.GetComponent<BaseNetworkable>();
                    if (!currentBaseNet.IsDestroyed)
                        currentBaseNet?.Kill(BaseNetworkable.DestroyMode.None);
                    Destroy(this.entity.gameObject);
                }
            }

            void Explode()
            {
                Destroy(this);
            }

            public void SetFuse()
            {
                if (timerAmountMax <= 0)
                {
                    return;
                }

                if (timerAmountMin < 0)
                {
                    timerAmountMin = 0;
                }

                this.fuseLength = UnityEngine.Random.Range(timerAmountMin, timerAmountMax);
            }
        }

        public class VehiclePropeller : MonoBehaviour
        {
            const float LERP_MODIFIER = 0.5f;
            const float MAX_ROTATION = 55;

            private IVehicle vehicle;

            float currentRotation;

            public bool RequireEngine = true;

            bool engineOn;
            bool powerOn;
            bool hasVehicle;
            Transform cachedTransform;

            void Awake()
            {
                cachedTransform = transform;

                var entity = this.GetComponent<BaseEntity>();

                this.vehicle = this.GetComponentInParent<IVehicle>();
                hasVehicle = vehicle != null;
                if (hasVehicle)
                {
                    this.vehicle.OnToggled += OnEngineToggle;
                    this.vehicle.OnPowerToggle += OnPowerToggle;
                }
            }

            void OnEngineToggle(bool newState)
            {
                engineOn = newState;
            }

            void OnPowerToggle(bool newState)
            {
                powerOn = newState;
            }

            public void ForceOn(bool newState)
            {
                engineOn = newState;
                powerOn = newState;
            }

            void FixedUpdate()
            {
                UpdatePropellers();
            }

            void UpdatePropellers()
            {
                var modifier = -0.2f;
                if (hasVehicle && this.vehicle.IsStartingUp)
                {
                    modifier = 1;
                }
                else if (engineOn || (!RequireEngine && powerOn))
                {
                    modifier = 1.5f;
                }

                if (this.currentRotation <= 0.025 && modifier <= 0)
                {
                    this.currentRotation = 0;
                    return;
                }

                this.currentRotation = Mathf.Lerp(this.currentRotation, modifier, LERP_MODIFIER * Time.fixedDeltaTime);
                cachedTransform.Rotate(new Vector3(0, this.currentRotation * MAX_ROTATION * -1, 0));
            }

            public void DeregisterActions()
            {
                if (hasVehicle)
                {
                    this.vehicle.OnToggled -= OnEngineToggle;
                    this.vehicle.OnPowerToggle -= OnPowerToggle;
                }
            }
        }

        public class Gimbal : MonoBehaviour, IWeaponController, IJoint
        {
            GimbalInputState currentInputState = new GimbalInputState();
            const float Y_LERP_MODIFIER = 15.5f;
            const float X_LERP_MODIFIER = 15.5f;

            SpecialBaseMountable mount;
            BaseEntity baseEntity;
            Transform cachedTransform;
            Transform weaponTransform;

            public WeaponControllerConfig Config;
            public Action OnJointUpdate { get; set; }
            public IKaruzaCustomPrefab KaruzaCustomEntity { get { return Vehicle; } }
            public BaseEntity WeaponControllerEntity { get { return this.baseEntity; } }


            List<WeaponSystem> WeaponSystems = new List<WeaponSystem>();

            bool invokeStopSpecialGunsStarted;
            bool mountedSet;

            float y;
            float x;
            public IVehicle Vehicle { get; private set; }
            public Vector3 Forward { get { return weaponTransform.forward; } }
            public List<SpecialGun> SpecialGuns { get; private set; } = new List<SpecialGun>();

            public Transform Transform { get { return weaponTransform; } }

            public float NextDryFireEffect { get; set; }
            public bool HasAmmoContainer { get; private set; }
            public bool UnlimitedAmmo { get; private set; }
            public ISpecialAmmoContainer AmmoContainer { get; private set; }
            public float LastAmmoUpdate { get; set; }
            public string NoAmmoToast { get; private set; }
            public BaseEntity BaseEntity { get; private set; }

            void Awake()
            {
                cachedTransform = this.transform;
                baseEntity = this.GetComponent<BaseEntity>();
                Vehicle = cachedTransform.parent.GetComponent<IVehicle>();
                var newGoT = new GameObject();
                weaponTransform = newGoT.transform;
                weaponTransform.SetParent(this.transform, false);
            }

            public void ConfigureWeaponController(IWeaponController wc)
            {
                UnlimitedAmmo = wc.UnlimitedAmmo;
                AmmoContainer = wc.AmmoContainer;
                HasAmmoContainer = wc.HasAmmoContainer;
                NoAmmoToast = wc.NoAmmoToast;
                BaseEntity = wc.BaseEntity;

            }

            public void InitializeGimbal(WeaponControllerConfig config)
            {
                Config = config;
                for (int i = 0; i < config.WeaponConfigurations.Count; i++)
                {
                    var wc = config.WeaponConfigurations[i];
                    if (!wc.Enabled)
                    {
                        continue;
                    }

                    var ws = new WeaponSystem()
                    {
                        Config = wc,
                        HasRigidBody = true,
                        RigidBody = Vehicle.BaseVehicle.rigidBody
                    };

                    for (int n = 0; n < wc.CounterConfigurations.Count; n++)
                    {
                        var cc = wc.CounterConfigurations[n];
                        if (!cc.Enabled)
                        {
                            continue;
                        }

                        var counter = PropUtilities.CreateCustomEntity<VehicleAmmoCounter>(cc.Location, cc.Rotation, cc.Scale, PREFAB_COUNTER, Vehicle.BaseVehicle);
                        counter.ConfigureCounter(ws);
                    }

                    WeaponSystems.Add(ws);
                }

                Vehicle.OnToggled += OnEngineToggle;
            }

            void OnEngineToggle(bool newState)
            {
                if (!Config.RequiresEngine)
                {
                    return;
                }

                enabled = newState;
            }

            public void SetChildren()
            {
                mount = FindMount(cachedTransform);
                mount.giveCrosshair = true;
                mountedSet = true;
                // Gimbal for Y
                SpecialGuns.AddRange(cachedTransform.GetComponentsInChildren<SpecialGun>());
            }

            public virtual void FixedUpdate()
            {
                UpdateGimbal();
            }

            void UpdateGimbal()
            {
                if (!mountedSet)
                {
                    return;
                }

                if (!this.mount.IsMounted())
                {
                    return;
                }

                var player = this.mount.GetMounted();
                MountedInput(player.serverInput);
                UpdateWeapons(player);

                this.x = Mathf.Lerp(x, currentInputState.pitch * 5.5f, X_LERP_MODIFIER * Time.fixedDeltaTime);
                this.y = Mathf.Lerp(y, currentInputState.yaw * 7.5f, Y_LERP_MODIFIER * Time.fixedDeltaTime);

                var mountEulerAngles = mount.transform.parent.transform.localRotation.eulerAngles;
                var thisEulerAngles = cachedTransform.localRotation.eulerAngles;
                for (int i = 0; i < 3; i++)
                {
                    mountEulerAngles[i] -= ((mountEulerAngles[i] > 180f) ? 360f : 0f);
                }

                cachedTransform.localRotation = Quaternion.Euler(0, Mathf.Clamp(thisEulerAngles.y + this.y, Config.MinY, Config.MaxY), 0f);
                mount.transform.parent.transform.localRotation = Quaternion.Euler(Mathf.Clamp(mountEulerAngles.x + this.x, Config.MinX, Config.MaxX), 0, 0f);

                weaponTransform.rotation = mount.transform.parent.transform.rotation;

                OnJointUpdate?.Invoke();
                Vehicle.OnJointUpdate();
            }

            static SpecialBaseMountable FindMount(Transform parent)
            {
                SpecialBaseMountable mount = null;
                foreach (Transform child in parent)
                {
                    var foundMount = child.GetComponentInChildren<SpecialBaseMountable>();
                    if (foundMount != null)
                    {
                        mount = foundMount;
                        break;
                    }
                    else if (child.childCount > 0)
                    {
                        FindMount(child);
                    }
                }

                return mount;
            }

            static List<T> FindComponents<T>(Transform parent) where T : BaseEntity, new()
            {
                List<T> foundComponents = new List<T>();
                foreach (Transform child in parent)
                {
                    var foundChildren = child.GetComponentsInChildren(typeof(T));
                    if (foundChildren.Length >= 0)
                    {
                        foreach (T item in foundChildren)
                        {
                            foundComponents.Add(item);
                        }

                        break;
                    }
                    else if (child.childCount > 0)
                    {
                        foreach (T item in FindComponents<T>(child))
                        {
                            foundComponents.Add(item);
                        }
                    }
                }

                return foundComponents;
            }

            public virtual void MountedInput(InputState inputState)
            {
                this.currentInputState.Reset();

                //this.currentInputState.yaw = inputState.IsDown(BUTTON.RIGHT) ? 1f : 0.0f;
                //this.currentInputState.yaw -= inputState.IsDown(BUTTON.LEFT) ? 1f : 0.0f;
                this.currentInputState.yaw = HelperUtilities.MouseToBinary(inputState.current.mouseDelta.x, -1f, 1f);
                this.currentInputState.pitch = HelperUtilities.MouseToBinary(-inputState.current.mouseDelta.y, -0.95f, 0.70f);
            }

            #region Weapons

            private void UpdateWeapons(BasePlayer driver)
            {
                if (Vehicle.BaseEntity.InSafeZone())
                {
                    return;
                }

                for (int i = 0; i < WeaponSystems.Count; i++)
                {
                    var ws = WeaponSystems[i];

                    WeaponUtilities.UpdateWeapon(this, driver, ws);
                }
            }

            void StopSpecialGuns()
            {
                foreach (var specialGun in SpecialGuns)
                {
                    specialGun.ToggleOff();
                }

                invokeStopSpecialGunsStarted = false;
            }

            public void InvokeStopSpecialGuns()
            {
                StopSpecialGuns();
            }

            void OnDestroy()
            {
                Destroy(weaponTransform);
            }

            #endregion

            public class GimbalInputState
            {
                public float yaw;

                public float pitch;

                public void Reset()
                {
                    yaw = 0f;
                    pitch = 0f;
                }
            }

        }

        #endregion

        #region Special Entity Classes

        #region Storage

        public class SpecialStorageContainer : StorageContainer, IStorageContainer
        {
            public IKaruzaCustomEntity KaruzaCustomEntity { get; protected set; }
            public BaseEntity Container { get { return this; } }
            public string PanelName { get { return panelName; } }
            public bool OnlyOneUser { get { return onlyOneUser; } }
            public ContainerConfiguration Config { get; set; }
            public bool DestroyParentOnDestroy = true;
            public bool HasKaruzaEntity;

            public override bool ShouldInheritNetworkGroup()
            {
                return false;
            }

            void Awake()
            {
                allowedItems = new ItemDefinition[0];
            }

            public override OBB WorldSpaceBounds()
            {
                return KaruzaCustomEntity.BaseEntity.WorldSpaceBounds();
            }

            public override void ServerInit()
            {
                this.KaruzaCustomEntity = this.GetComponentInParent<IKaruzaCustomEntity>();

                networkEntityScale = true;
                canBeDemolished = false;

                if (_inventory == null)
                {
                    Debug.Assert(_inventory == null, "Double init of inventory!");
                    _inventory = Facepunch.Pool.Get<ItemContainer>();

                    _inventory.entityOwner = this;
                    _inventory.allowedContents = ((allowedContents == (ItemContainer.ContentsType)0) ? ItemContainer.ContentsType.Generic : allowedContents);
                    _inventory.SetOnlyAllowedItems(allowedItems, allowedItem, allowedItem2);
                    _inventory.SetBlacklist(blockedItems);
                    _inventory.maxStackSize = maxStackSize;
                    _inventory.ServerInitialize(null, inventorySlots);
                    _inventory.GiveUID();
                    _inventory.onDirty += OnInventoryDirty;
                    _inventory.onItemAddedRemoved = OnItemAddedOrRemoved;
                    _inventory.onItemAddedToStack = OnItemAddedToStack;
                    _inventory.onItemRemovedFromStack = OnItemRemovedFromStack;
                    _inventory.onItemPositionChanged = OnItemPositionChanged;
                    _inventory.canAcceptItem = ItemFilter;

                    OnInventoryFirstCreated(_inventory);
                }

                base.ServerInit();
                BuildingManager.server.Remove(this);

                HasKaruzaEntity = this.KaruzaCustomEntity != null;

                if (Config != null && Config.AllowedItems?.Count > 0)
                {
                    this.inventory.SetOnlyAllowedItems(ItemManager.itemList.FindAll(it => Config.AllowedItems.Contains(it.shortname)).ToArray());
                }
            }

            public override bool CanCompletePickup(BasePlayer player)
            {
                return false;
            }

            public override bool ShouldDisplayPickupOption(BasePlayer player)
            {
                return false;
            }

            public override void Hurt(HitInfo info)
            {
                this.KaruzaCustomEntity.Hurt(info);
                return;
            }

            public override void OnAttacked(HitInfo info)
            {
                using (TimeWarning.New(BASECOMBATENTITY_ONATTACKED))
                {
                    this.KaruzaCustomEntity.OnAttacked(info);
                    return;
                }
            }

            public override void DoRepair(BasePlayer player)
            {
                this.KaruzaCustomEntity.DoRepair(player);
            }

            public override bool CanBeLooted(BasePlayer player)
            {
                if (Config.IgnoreCodelock)
                {
                    return true;
                }

                return KaruzaCustomEntity.CanAccess(player);
            }

            public void CustomDropItems(BaseEntity initiator = null)
            {
                ItemContainer itemContainer = this.inventory;
                if (itemContainer == null || itemContainer.itemList == null || itemContainer.itemList.Count == 0)
                {
                    return;
                }

                if (this.ShouldDropItemsIndividually() || (itemContainer.itemList.Count == 1 && !this.DropFloats))
                {
                    if (initiator != null)
                    {
                        this.DropBonusItems(initiator, itemContainer);
                    }

                    DropUtil.DropItems(itemContainer, this.GetDropPosition());
                }
                else
                {
                    string prefab = (this.DropFloats ? "assets/prefabs/misc/item drop/item_drop_buoyant.prefab" : "assets/prefabs/misc/item drop/item_drop.prefab");
                    _ = itemContainer.Drop(prefab, this.GetDropPosition(), this.Transform.rotation, this.DestroyLootPercent) != null;
                }
            }

            public override void DestroyShared()
            {
                base.DestroyShared();

                if (DestroyParentOnDestroy && KaruzaCustomEntity != null && !KaruzaCustomEntity.BaseEntity.IsDestroyed)
                {
                    KaruzaCustomEntity.BaseEntity.Kill();
                }
            }

            public override void UpdateNetworkGroup()
            {
                isCallingUpdateNetworkGroup = false;
                if (net == null || Network.Net.sv == null || Network.Net.sv.visibility == null)
                {
                    return;
                }

                using (TimeWarning.New(UPDATE_NETWORKGROUP))
                {
                    var group = BaseNetworkable.GlobalNetworkGroup;
                    if (HasKaruzaEntity)
                    {
                        if (KaruzaCustomEntity.BaseEntity.IsDestroyed || KaruzaCustomEntity.BaseEntity.net == null)
                        {
                            return;
                        }

                        group = KaruzaCustomEntity.BaseEntity.net.group;
                    }

                    if (net.SwitchGroup(group))
                    {
                        if (isSpawned)
                        {
                            if (net.group == null)
                            {
                                Debug.LogWarning(ToString() + " changed its network group to null");
                                return;
                            }

                            NetWrite netWrite = Network.Net.sv.StartWrite();
                            netWrite.PacketID(Message.Type.GroupChange);
                            netWrite.EntityID(net.ID);
                            netWrite.GroupID(net.group.ID);
                            netWrite.Send(new SendInfo(net.group.subscribers));
                        }
                    }
                }
            }

            public override bool ShouldNetworkTo(BasePlayer player)
            {
                if (IsTransferProtected())
                {
                    return false;
                }

                if (limitNetworking)
                {
                    return false;
                }

                if (HasKaruzaEntity)
                {
                    return KaruzaCustomEntity.BaseEntity.ShouldNetworkTo(player);
                }

                if (net.group == null)
                {
                    return true;
                }

                return player.net.subscriber.IsSubscribed(net.group);
            }
        }

        public class SpecialStorageContainerProxy : SpecialStorageContainer
        {
            public ICustomContainer MainContainer;

            public override bool PlayerOpenLoot(BasePlayer player, string panelToOpen = "", bool doPositionChecks = true)
            {
                if (MainContainer.Container.IsLocked() || MainContainer.Container.IsTransferring())
                {
                    player.ShowToast(GameTip.Styles.Red_Normal, LockedMessage, false);
                    return false;
                }

                if (MainContainer.OnlyOneUser && MainContainer.Container.IsOpen())
                {
                    player.ShowToast(GameTip.Styles.Red_Normal, InUseMessage, false);
                    return false;
                }

                panelToOpen = MainContainer.PanelName;
                if (!MainContainer.CanOpenLootPanel(player, panelToOpen))
                {
                    return false;
                }

                if (player.inventory.loot.StartLootingEntity(MainContainer.Container, doPositionChecks))
                {
                    using (FlagsUpdateScope flagsUpdateScope = MainContainer.Container.StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(Flags.Open, true);
                    }

                    player.inventory.loot.AddContainer(MainContainer.inventory);
                    player.inventory.loot.SendImmediate();
                    player.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", player), panelToOpen);
                    MainContainer.Container.SendNetworkUpdate();

                    return true;
                }

                return false;
            }

            public override void PlayerStoppedLooting(BasePlayer player)
            {
                using (FlagsUpdateScope flagsUpdateScope = MainContainer.Container.StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(BaseEntity.Flags.Open, false);
                }

                MainContainer.Container.SendNetworkUpdate();
            }
        }

        public class SpecialContainerIOEntity : ContainerIOEntity, IIndustrialStorage
        {
            public IKaruzaCustomEntity KaruzaCustomEntity { get; protected set; }
            public ICustomContainer CustomContainer { get; set; }
            public bool DestroyParentOnDestroy = true;
            public bool HasKaruzaEntity;

            public BaseEntity IndustrialEntity => this;

            public ItemContainer Container { get { return CustomContainer.inventory; } }

            public override bool DisregardGravityRestrictionsOnLiquid { get; } = true;

            public override bool ShouldInheritNetworkGroup()
            {
                return false;
            }

            public override void ServerInit()
            {
                networkEntityScale = true;
                canBeDemolished = false;
                dropsLoot = false;
                spawnDeployableCorpseOnDeath = false;

                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(BaseEntity.Flags.Busy, true);
                }

                _inventory = CustomContainer.inventory;

                base.ServerInit();
                BuildingManager.server.Remove(this);

                this.KaruzaCustomEntity = this.GetComponentInParent<IKaruzaCustomEntity>();

                HasKaruzaEntity = this.KaruzaCustomEntity != null;
            }

            public override void DoServerDestroy()
            {
                // Get item container so we can free without argument exception
                _inventory = Pool.Get<ItemContainer>();
                base.DoServerDestroy();
            }

            public override bool PlayerOpenLoot(BasePlayer player, string panelToOpen = "", bool doPositionChecks = true)
            {
                if (CustomContainer.Container.IsLocked() || CustomContainer.Container.IsTransferring())
                {
                    player.ShowToast(GameTip.Styles.Red_Normal, StorageContainer.LockedMessage, false);
                    return false;
                }

                if (CustomContainer.OnlyOneUser && CustomContainer.Container.IsOpen())
                {
                    player.ShowToast(GameTip.Styles.Red_Normal, StorageContainer.InUseMessage, false);
                    return false;
                }

                panelToOpen = CustomContainer.PanelName;
                if (!CustomContainer.CanOpenLootPanel(player, panelToOpen))
                {
                    return false;
                }

                if (player.inventory.loot.StartLootingEntity(CustomContainer.Container, doPositionChecks))
                {
                    using (FlagsUpdateScope flagsUpdateScope = CustomContainer.Container.StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(Flags.Open, true);
                    }

                    player.inventory.loot.AddContainer(CustomContainer.inventory);
                    player.inventory.loot.SendImmediate();
                    player.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", player), panelToOpen);
                    CustomContainer.Container.SendNetworkUpdate();

                    return true;
                }

                return false;
            }

            public override int GetPassthroughAmount(int outputSlot = 0)
            {
                return currentEnergy;
            }

            public override int GetCurrentEnergy()
            {
                return currentEnergy;
            }

            public override void UpdateFromInput(int inputAmount, int inputSlot)
            {
                if (inputs[inputSlot].type != ioType || inputs[inputSlot].type == IOType.Industrial)
                {
                    IOStateChanged(inputAmount, inputSlot);
                    return;
                }

                //UpdateHasPower(inputAmount, inputSlot);
                lastEnergy = currentEnergy;
                currentEnergy = inputAmount;

                _processQueues[GetQueueType()].Enqueue(this);
            }

            public override bool AllowWireConnections()
            {
                return true;
            }

            public Vector2i InputSlotRange(int slotIndex)
            {
                if (Container != null)
                {
                    return new Vector2i(0, Container.capacity - 1);
                }

                return new Vector2i(0, 0);
            }

            public Vector2i OutputSlotRange(int slotIndex)
            {
                if (Container != null)
                {
                    return new Vector2i(0, Container.capacity - 1);
                }

                return new Vector2i(0, 0);
            }

            public void OnStorageItemTransferBegin()
            {
            }

            public void OnStorageItemTransferEnd()
            {
            }

            public override bool CanCompletePickup(BasePlayer player)
            {
                return false;
            }

            public override bool ShouldDisplayPickupOption(BasePlayer player)
            {
                return false;
            }

            public override void Hurt(HitInfo info)
            {
                this.KaruzaCustomEntity.Hurt(info);
                return;
            }

            public override void OnAttacked(HitInfo info)
            {
                using (TimeWarning.New(BASECOMBATENTITY_ONATTACKED))
                {
                    this.KaruzaCustomEntity.OnAttacked(info);
                    return;
                }
            }

            public override void DoRepair(BasePlayer player)
            {
                this.KaruzaCustomEntity.DoRepair(player);
            }

            public override bool CanBeLooted(BasePlayer player)
            {
                return KaruzaCustomEntity.CanAccess(player);
            }

            public override void DestroyShared()
            {
                base.DestroyShared();

                if (DestroyParentOnDestroy && KaruzaCustomEntity != null && !KaruzaCustomEntity.BaseEntity.IsDestroyed)
                {
                    KaruzaCustomEntity.BaseEntity.Kill();
                }
            }

            public override void UpdateNetworkGroup()
            {
                isCallingUpdateNetworkGroup = false;
                if (net == null || Network.Net.sv == null || Network.Net.sv.visibility == null)
                {
                    return;
                }

                using (TimeWarning.New(UPDATE_NETWORKGROUP))
                {
                    var group = BaseNetworkable.GlobalNetworkGroup;
                    if (HasKaruzaEntity)
                    {
                        if (KaruzaCustomEntity.BaseEntity.IsDestroyed || KaruzaCustomEntity.BaseEntity.net == null)
                        {
                            return;
                        }

                        group = KaruzaCustomEntity.BaseEntity.net.group;
                    }

                    if (net.SwitchGroup(group))
                    {
                        if (isSpawned)
                        {
                            if (net.group == null)
                            {
                                Debug.LogWarning(ToString() + " changed its network group to null");
                                return;
                            }

                            NetWrite netWrite = Network.Net.sv.StartWrite();
                            netWrite.PacketID(Message.Type.GroupChange);
                            netWrite.EntityID(net.ID);
                            netWrite.GroupID(net.group.ID);
                            netWrite.Send(new SendInfo(net.group.subscribers));
                        }
                    }
                }
            }

            public override bool ShouldNetworkTo(BasePlayer player)
            {
                if (IsTransferProtected())
                {
                    return false;
                }

                if (limitNetworking)
                {
                    return false;
                }

                if (HasKaruzaEntity)
                {
                    return KaruzaCustomEntity.BaseEntity.ShouldNetworkTo(player);
                }

                if (net.group == null)
                {
                    return true;
                }

                return player.net.subscriber.IsSubscribed(net.group);
            }
        }

        public class SpecialShopFront : ShopFront, IStorageContainer
        {
            public IKaruzaCustomEntity KaruzaCustomEntity { get; protected set; }
            public BaseEntity Container { get { return this; } }
            public string PanelName { get { return panelName; } }
            public bool OnlyOneUser { get { return onlyOneUser; } }
            public ContainerConfiguration Config { get; set; }
            public bool DestroyParentOnDestroy = true;
            public bool HasKaruzaEntity;

            public override bool ShouldInheritNetworkGroup()
            {
                return false;
            }

            public override OBB WorldSpaceBounds()
            {
                return KaruzaCustomEntity.BaseEntity.WorldSpaceBounds();
            }

            void Awake()
            {
                allowedItems = new ItemDefinition[0];
                transactionCompleteEffect = new GameObjectRef()
                {
                    guid = GameManifest.pathToGuid[SHOP_FRONT_TRANSACTION_EFFECT_PREFAB]
                };
            }

            public override void ServerInit()
            {
                networkEntityScale = true;

                canBeDemolished = false;
                base.ServerInit();
                BuildingManager.server.Remove(this);

                maxUseAngle = 180;
                this.KaruzaCustomEntity = this.GetComponentInParent<IKaruzaCustomEntity>();

                HasKaruzaEntity = this.KaruzaCustomEntity != null;

                if (Config != null && Config.AllowedItems?.Count > 0)
                {
                    this.inventory.SetOnlyAllowedItems(ItemManager.itemList.FindAll(it => Config.AllowedItems.Contains(it.shortname)).ToArray());
                }
            }

            public override bool CanCompletePickup(BasePlayer player)
            {
                return false;
            }

            public override bool ShouldDisplayPickupOption(BasePlayer player)
            {
                return false;
            }

            public override void Hurt(HitInfo info)
            {
                this.KaruzaCustomEntity.Hurt(info);
                return;
            }

            public override void OnAttacked(HitInfo info)
            {
                using (TimeWarning.New(BASECOMBATENTITY_ONATTACKED))
                {
                    this.KaruzaCustomEntity.OnAttacked(info);
                    return;
                }
            }

            public override void DoRepair(BasePlayer player)
            {
                this.KaruzaCustomEntity.DoRepair(player);
            }

            public override bool CanBeLooted(BasePlayer player)
            {
                if (Config.IgnoreCodelock)
                {
                    return true;
                }

                return KaruzaCustomEntity.CanAccess(player);
            }

            public void CustomDropItems(BaseEntity initiator = null)
            {
                ItemContainer itemContainer = this.inventory;
                if (itemContainer == null || itemContainer.itemList == null || itemContainer.itemList.Count == 0)
                {
                    return;
                }

                if (this.ShouldDropItemsIndividually() || (itemContainer.itemList.Count == 1 && !this.DropFloats))
                {
                    if (initiator != null)
                    {
                        this.DropBonusItems(initiator, itemContainer);
                    }

                    DropUtil.DropItems(itemContainer, this.GetDropPosition());
                }
                else
                {
                    string prefab = (this.DropFloats ? "assets/prefabs/misc/item drop/item_drop_buoyant.prefab" : "assets/prefabs/misc/item drop/item_drop.prefab");
                    itemContainer.Drop(prefab, this.GetDropPosition(), this.Transform.rotation, 0);
                }
            }

            public override void DestroyShared()
            {
                base.DestroyShared();

                if (DestroyParentOnDestroy && KaruzaCustomEntity != null && !KaruzaCustomEntity.BaseEntity.IsDestroyed)
                {
                    KaruzaCustomEntity.BaseEntity.Kill();
                }
            }

            public override void UpdateNetworkGroup()
            {
                isCallingUpdateNetworkGroup = false;
                if (net == null || Network.Net.sv == null || Network.Net.sv.visibility == null)
                {
                    return;
                }

                using (TimeWarning.New(UPDATE_NETWORKGROUP))
                {
                    var group = BaseNetworkable.GlobalNetworkGroup;
                    if (HasKaruzaEntity)
                    {
                        if (KaruzaCustomEntity.BaseEntity.IsDestroyed || KaruzaCustomEntity.BaseEntity.net == null)
                        {
                            return;
                        }

                        group = KaruzaCustomEntity.BaseEntity.net.group;
                    }

                    if (net.SwitchGroup(group))
                    {
                        if (isSpawned)
                        {
                            if (net.group == null)
                            {
                                Debug.LogWarning(ToString() + " changed its network group to null");
                                return;
                            }

                            NetWrite netWrite = Network.Net.sv.StartWrite();
                            netWrite.PacketID(Message.Type.GroupChange);
                            netWrite.EntityID(net.ID);
                            netWrite.GroupID(net.group.ID);
                            netWrite.Send(new SendInfo(net.group.subscribers));
                        }
                    }
                }
            }

            public override bool ShouldNetworkTo(BasePlayer player)
            {
                if (IsTransferProtected())
                {
                    return false;
                }

                if (limitNetworking)
                {
                    return false;
                }

                if (HasKaruzaEntity)
                {
                    return KaruzaCustomEntity.BaseEntity.ShouldNetworkTo(player);
                }

                if (net.group == null)
                {
                    return true;
                }

                return player.net.subscriber.IsSubscribed(net.group);
            }
        }

        public class SpecialLocker : Locker, IStorageContainer
        {
            public IKaruzaCustomEntity KaruzaCustomEntity { get; protected set; }
            public BaseEntity Container { get { return this; } }
            public string PanelName { get { return panelName; } }
            public bool OnlyOneUser { get { return onlyOneUser; } }
            public ContainerConfiguration Config { get; set; }
            public bool DestroyParentOnDestroy = true;
            public bool HasKaruzaEntity;

            public override bool ShouldInheritNetworkGroup()
            {
                return false;
            }

            void Awake()
            {
                allowedItems = new ItemDefinition[0];
                equipSound = new GameObjectRef()
                {
                    guid = GameManifest.pathToGuid[ZIP_PREFAB]
                };
            }

            public override void ServerInit()
            {
                networkEntityScale = true;

                canBeDemolished = false;
                base.ServerInit();
                BuildingManager.server.Remove(this);

                this.KaruzaCustomEntity = this.GetComponentInParent<IKaruzaCustomEntity>();

                HasKaruzaEntity = this.KaruzaCustomEntity != null;

                if (Config != null && Config.AllowedItems?.Count > 0)
                {
                    this.inventory.SetOnlyAllowedItems(ItemManager.itemList.FindAll(it => Config.AllowedItems.Contains(it.shortname)).ToArray());
                }
            }

            public override bool CanCompletePickup(BasePlayer player)
            {
                return false;
            }

            public override bool ShouldDisplayPickupOption(BasePlayer player)
            {
                return false;
            }

            public override void Hurt(HitInfo info)
            {
                this.KaruzaCustomEntity.Hurt(info);
                return;
            }

            public override void OnAttacked(HitInfo info)
            {
                using (TimeWarning.New(BASECOMBATENTITY_ONATTACKED))
                {
                    this.KaruzaCustomEntity.OnAttacked(info);
                    return;
                }
            }

            public override void DoRepair(BasePlayer player)
            {
                this.KaruzaCustomEntity.DoRepair(player);
            }

            public override bool CanBeLooted(BasePlayer player)
            {
                if (Config.IgnoreCodelock)
                {
                    return true;
                }

                return KaruzaCustomEntity.CanAccess(player);
            }

            public void CustomDropItems(BaseEntity initiator = null)
            {
                ItemContainer itemContainer = this.inventory;
                if (itemContainer == null || itemContainer.itemList == null || itemContainer.itemList.Count == 0)
                {
                    return;
                }

                if (this.ShouldDropItemsIndividually() || (itemContainer.itemList.Count == 1 && !this.DropFloats))
                {
                    if (initiator != null)
                    {
                        this.DropBonusItems(initiator, itemContainer);
                    }

                    DropUtil.DropItems(itemContainer, this.GetDropPosition());
                }
                else
                {
                    string prefab = (this.DropFloats ? "assets/prefabs/misc/item drop/item_drop_buoyant.prefab" : "assets/prefabs/misc/item drop/item_drop.prefab");
                    itemContainer.Drop(prefab, this.GetDropPosition(), this.Transform.rotation, 0);
                }
            }

            public override void DestroyShared()
            {
                base.DestroyShared();

                if (DestroyParentOnDestroy && KaruzaCustomEntity != null && !KaruzaCustomEntity.BaseEntity.IsDestroyed)
                {
                    KaruzaCustomEntity.BaseEntity.Kill();
                }
            }

            public override void UpdateNetworkGroup()
            {
                isCallingUpdateNetworkGroup = false;
                if (net == null || Network.Net.sv == null || Network.Net.sv.visibility == null)
                {
                    return;
                }

                using (TimeWarning.New(UPDATE_NETWORKGROUP))
                {
                    var group = BaseNetworkable.GlobalNetworkGroup;
                    if (HasKaruzaEntity)
                    {
                        if (KaruzaCustomEntity.BaseEntity.IsDestroyed || KaruzaCustomEntity.BaseEntity.net == null)
                        {
                            return;
                        }

                        group = KaruzaCustomEntity.BaseEntity.net.group;
                    }

                    if (net.SwitchGroup(group))
                    {
                        if (isSpawned)
                        {
                            if (net.group == null)
                            {
                                Debug.LogWarning(ToString() + " changed its network group to null");
                                return;
                            }

                            NetWrite netWrite = Network.Net.sv.StartWrite();
                            netWrite.PacketID(Message.Type.GroupChange);
                            netWrite.EntityID(net.ID);
                            netWrite.GroupID(net.group.ID);
                            netWrite.Send(new SendInfo(net.group.subscribers));
                        }
                    }
                }
            }

            public override bool ShouldNetworkTo(BasePlayer player)
            {
                if (IsTransferProtected())
                {
                    return false;
                }

                if (limitNetworking)
                {
                    return false;
                }

                if (HasKaruzaEntity)
                {
                    return KaruzaCustomEntity.BaseEntity.ShouldNetworkTo(player);
                }

                if (net.group == null)
                {
                    return true;
                }

                return player.net.subscriber.IsSubscribed(net.group);
            }
        }

        public class SpecialBeehive : Beehive, IStorageContainer
        {
            public IKaruzaCustomEntity KaruzaCustomEntity { get; protected set; }
            public BaseEntity Container { get { return this; } }
            public string PanelName { get { return panelName; } }
            public bool OnlyOneUser { get { return onlyOneUser; } }
            public ContainerConfiguration Config { get; set; }
            public bool DestroyParentOnDestroy = true;
            public bool HasKaruzaEntity;

            public override bool ShouldInheritNetworkGroup()
            {
                return false;
            }

            void Awake()
            {
                allowedItems = new ItemDefinition[0];
            }

            public override void ServerInit()
            {
                networkEntityScale = true;
                canBeDemolished = false;

                base.ServerInit();

                outsideCheck = new TimeCachedValue<bool>
                {
                    refreshCooldown = updateHiveStatsInterval,
                    refreshRandomRange = 5f,
                    updateValue = CustomIsOutsideAccurate
                };

                BuildingManager.server.Remove(this);

                this.KaruzaCustomEntity = this.GetComponentInParent<IKaruzaCustomEntity>();

                HasKaruzaEntity = this.KaruzaCustomEntity != null;
            }

            bool CustomIsOutsideAccurate()
            {
                return true;
            }

            public override bool CanCompletePickup(BasePlayer player)
            {
                return false;
            }

            public override bool ShouldDisplayPickupOption(BasePlayer player)
            {
                return false;
            }

            public override void Hurt(HitInfo info)
            {
                this.KaruzaCustomEntity.Hurt(info);
                return;
            }

            public override void OnAttacked(HitInfo info)
            {
                using (TimeWarning.New(BASECOMBATENTITY_ONATTACKED))
                {
                    this.KaruzaCustomEntity.OnAttacked(info);
                    return;
                }
            }

            public override void DoRepair(BasePlayer player)
            {
                this.KaruzaCustomEntity.DoRepair(player);
            }

            public override bool CanBeLooted(BasePlayer player)
            {
                if (Config.IgnoreCodelock)
                {
                    return true;
                }

                return KaruzaCustomEntity.CanAccess(player);
            }

            public void CustomDropItems(BaseEntity initiator = null)
            {
                ItemContainer itemContainer = this.inventory;
                if (itemContainer == null || itemContainer.itemList == null || itemContainer.itemList.Count == 0)
                {
                    return;
                }

                if (this.ShouldDropItemsIndividually() || (itemContainer.itemList.Count == 1 && !this.DropFloats))
                {
                    if (initiator != null)
                    {
                        this.DropBonusItems(initiator, itemContainer);
                    }

                    DropUtil.DropItems(itemContainer, this.GetDropPosition());
                }
                else
                {
                    string prefab = (this.DropFloats ? "assets/prefabs/misc/item drop/item_drop_buoyant.prefab" : "assets/prefabs/misc/item drop/item_drop.prefab");
                    itemContainer.Drop(prefab, this.GetDropPosition(), this.Transform.rotation, 0);
                }
            }

            public override void DestroyShared()
            {
                base.DestroyShared();

                if (DestroyParentOnDestroy && KaruzaCustomEntity != null && !KaruzaCustomEntity.BaseEntity.IsDestroyed)
                {
                    KaruzaCustomEntity.BaseEntity.Kill();
                }
            }

            public override void UpdateNetworkGroup()
            {
                isCallingUpdateNetworkGroup = false;
                if (net == null || Network.Net.sv == null || Network.Net.sv.visibility == null)
                {
                    return;
                }

                using (TimeWarning.New(UPDATE_NETWORKGROUP))
                {
                    var group = BaseNetworkable.GlobalNetworkGroup;
                    if (HasKaruzaEntity)
                    {
                        if (KaruzaCustomEntity.BaseEntity.IsDestroyed || KaruzaCustomEntity.BaseEntity.net == null)
                        {
                            return;
                        }

                        group = KaruzaCustomEntity.BaseEntity.net.group;
                    }

                    if (net.SwitchGroup(group))
                    {
                        if (isSpawned)
                        {
                            if (net.group == null)
                            {
                                Debug.LogWarning(ToString() + " changed its network group to null");
                                return;
                            }

                            NetWrite netWrite = Network.Net.sv.StartWrite();
                            netWrite.PacketID(Message.Type.GroupChange);
                            netWrite.EntityID(net.ID);
                            netWrite.GroupID(net.group.ID);
                            netWrite.Send(new SendInfo(net.group.subscribers));
                        }
                    }
                }
            }

            public override bool ShouldNetworkTo(BasePlayer player)
            {
                if (IsTransferProtected())
                {
                    return false;
                }

                if (limitNetworking)
                {
                    return false;
                }

                if (HasKaruzaEntity)
                {
                    return KaruzaCustomEntity.BaseEntity.ShouldNetworkTo(player);
                }

                if (net.group == null)
                {
                    return true;
                }

                return player.net.subscriber.IsSubscribed(net.group);
            }
        }

        public class SpecialLiquidContainer : LiquidContainer, ICustomContainer
        {
            public IKaruzaCustomEntity KaruzaCustomEntity { get; protected set; }
            public BaseEntity Container { get { return this; } }
            public string PanelName { get { return lootPanelName; } }
            public bool OnlyOneUser { get { return onlyOneUser; } }
            public bool DestroyParentOnDestroy = true;
            public bool HasKaruzaEntity;
            public bool IgnoreCodelock;

            public List<string> AllowedItems;

            public override bool ShouldInheritNetworkGroup()
            {
                return false;
            }

            public override void ServerInit()
            {
                networkEntityScale = true;
                this.maxStackSize = 200000;
                this.allowedContents = ItemContainer.ContentsType.Liquid;

                base.ServerInit();
                this.KaruzaCustomEntity = this.GetComponentInParent<IKaruzaCustomEntity>();

                HasKaruzaEntity = this.KaruzaCustomEntity != null;

                if (AllowedItems?.Count > 0)
                {
                    this.inventory.SetOnlyAllowedItems(ItemManager.itemList.FindAll(it => AllowedItems.Contains(it.shortname)).ToArray());
                }
            }

            public override bool CanCompletePickup(BasePlayer player)
            {
                return false;
            }

            public override bool ShouldDisplayPickupOption(BasePlayer player)
            {
                return false;
            }

            public override void Hurt(HitInfo info)
            {
                this.KaruzaCustomEntity.Hurt(info);
                return;
            }

            public override void OnAttacked(HitInfo info)
            {
                using (TimeWarning.New(BASECOMBATENTITY_ONATTACKED))
                {
                    this.KaruzaCustomEntity.OnAttacked(info);
                    return;
                }
            }

            public override void DoRepair(BasePlayer player)
            {
                this.KaruzaCustomEntity.DoRepair(player);
            }

            public override bool CanBeLooted(BasePlayer player)
            {
                if (IgnoreCodelock)
                {
                    return true;
                }

                return KaruzaCustomEntity.CanAccess(player);
            }

            public void CustomDropItems(BaseEntity initiator = null)
            {
                ItemContainer itemContainer = this.inventory;
                if (itemContainer == null || itemContainer.itemList == null || itemContainer.itemList.Count == 0)
                {
                    return;
                }

                if (this.ShouldDropItemsIndividually() || (itemContainer.itemList.Count == 1 && !this.DropFloats))
                {
                    if (initiator != null)
                    {
                        this.DropBonusItems(initiator, itemContainer);
                    }

                    DropUtil.DropItems(itemContainer, this.GetDropPosition());
                }
                else
                {
                    string prefab = (this.DropFloats ? "assets/prefabs/misc/item drop/item_drop_buoyant.prefab" : "assets/prefabs/misc/item drop/item_drop.prefab");
                    itemContainer.Drop(prefab, this.GetDropPosition(), this.Transform.rotation, 0);
                }
            }

            public override void DestroyShared()
            {
                base.DestroyShared();

                if (DestroyParentOnDestroy && KaruzaCustomEntity != null && !KaruzaCustomEntity.BaseEntity.IsDestroyed)
                {
                    KaruzaCustomEntity.BaseEntity.Kill();
                }
            }

            public override void UpdateNetworkGroup()
            {
                isCallingUpdateNetworkGroup = false;
                if (net == null || Network.Net.sv == null || Network.Net.sv.visibility == null)
                {
                    return;
                }

                using (TimeWarning.New(UPDATE_NETWORKGROUP))
                {
                    var group = BaseNetworkable.GlobalNetworkGroup;
                    if (HasKaruzaEntity)
                    {
                        if (KaruzaCustomEntity.BaseEntity.IsDestroyed || KaruzaCustomEntity.BaseEntity.net == null)
                        {
                            return;
                        }

                        group = KaruzaCustomEntity.BaseEntity.net.group;
                    }

                    if (net.SwitchGroup(group))
                    {
                        if (isSpawned)
                        {
                            if (net.group == null)
                            {
                                Debug.LogWarning(ToString() + " changed its network group to null");
                                return;
                            }

                            NetWrite netWrite = Network.Net.sv.StartWrite();
                            netWrite.PacketID(Message.Type.GroupChange);
                            netWrite.EntityID(net.ID);
                            netWrite.GroupID(net.group.ID);
                            netWrite.Send(new SendInfo(net.group.subscribers));
                        }
                    }
                }
            }
        }

        public class VehicleLiquidStorageContainer : SpecialLiquidContainer, IStorageContainer
        {
            public ContainerConfiguration Config { get; set; }
        }

        public class VehicleAmmoLiquidContainer : SpecialLiquidContainer, ISpecialAmmoContainer
        {
            IVehicle vehicle;

            public AmmoContainerConfiguration Config { get; set; }

            public override void ServerInit()
            {
                base.ServerInit();
                vehicle = KaruzaCustomEntity as IVehicle;
                Initialize(vehicle.VehicleConfig);
            }

            public void Initialize(BaseEntityConfig config)
            {
                var ammoShortnames = new List<string>();

                for (int i = 0; i < vehicle.WeaponSystems.Count; i++)
                {
                    var wc = vehicle.WeaponSystems[i];
                    if (wc.Config.ProjectileType != ProjectileType.Water)
                    {
                        continue;
                    }

                    if (!Instance.AllowedFluids.Contains(wc.Config.AmmoShortName))
                    {
                        continue;
                    }

                    if (!ammoShortnames.Contains(wc.Config.AmmoShortName))
                    {
                        ammoShortnames.Add(wc.Config.AmmoShortName);
                    }
                }

                SetGimbalConfigWeapons(config.GimbalConfigs, ammoShortnames);
                SetComputerStationWeapons(config.ComputerStationConfigs, ammoShortnames);
                this.inventory.onlyAllowedItems = ItemManager.itemList.FindAll(it => ammoShortnames.Contains(it.shortname) || it.shortname == FLARE_SHORTNAME).ToArray();
            }
            void SetGimbalConfigWeapons(List<GimbalConfig> gimbalConfigs, List<string> ammoShortnames)
            {
                for (int i = 0; i < gimbalConfigs.Count; i++)
                {
                    var gc = gimbalConfigs[i];
                    if (gc.WeaponControllerConfig == null)
                    {
                        continue;
                    }

                    for (int n = 0; n < gc.WeaponControllerConfig.WeaponConfigurations.Count; n++)
                    {
                        var wc = gc.WeaponControllerConfig.WeaponConfigurations[n];
                        if (!wc.Enabled)
                        {
                            continue;
                        }

                        if (wc.ProjectileType != ProjectileType.Water)
                        {
                            continue;
                        }

                        if (!Instance.AllowedFluids.Contains(wc.AmmoShortName))
                        {
                            continue;
                        }

                        if (!ammoShortnames.Contains(wc.AmmoShortName))
                        {
                            ammoShortnames.Add(wc.AmmoShortName);
                        }
                    }
                }
            }

            void SetComputerStationWeapons(List<ComputerStationConfig> stationConfigs, List<string> ammoShortnames)
            {
                for (int i = 0; i < stationConfigs.Count; i++)
                {
                    var sc = stationConfigs[i];
                    if (sc.WeaponControllerConfig == null)
                    {
                        continue;
                    }

                    for (int n = 0; n < sc.WeaponControllerConfig.WeaponConfigurations.Count; n++)
                    {
                        var wc = sc.WeaponControllerConfig.WeaponConfigurations[n];
                        if (!wc.Enabled)
                        {
                            continue;
                        }

                        if (wc.ProjectileType != ProjectileType.Water)
                        {
                            continue;
                        }

                        if (!Instance.AllowedFluids.Contains(wc.AmmoShortName))
                        {
                            continue;
                        }

                        if (!ammoShortnames.Contains(wc.AmmoShortName))
                        {
                            ammoShortnames.Add(wc.AmmoShortName);
                        }
                    }
                }
            }

        }

        public class VehicleAmmoContainer : SpecialStorageContainer, ISpecialAmmoContainer
        {
            public AmmoContainerConfiguration Config { get; set; }

            List<string> ammoShortnames = new List<string>();

            IVehicle vehicle;

            public override void ServerInit()
            {
                base.ServerInit();
                vehicle = KaruzaCustomEntity as IVehicle;
                Initialize(vehicle.VehicleConfig);
            }

            public void Initialize(BaseEntityConfig config)
            {
                for (int i = 0; i < vehicle.WeaponSystems.Count; i++)
                {
                    var wc = vehicle.WeaponSystems[i];
                    if (wc.Config.ProjectileType == ProjectileType.Flare)
                    {
                        continue;
                    }

                    if (!ammoShortnames.Contains(wc.Config.AmmoShortName))
                    {
                        ammoShortnames.Add(wc.Config.AmmoShortName);
                    }
                }

                SetGimbalConfigWeapons(config.GimbalConfigs);
                SetComputerStationWeapons(config.ComputerStationConfigs);
                this.inventory.onlyAllowedItems = ItemManager.itemList.FindAll(it => ammoShortnames.Contains(it.shortname) || it.shortname == FLARE_SHORTNAME).ToArray();
            }

            void SetGimbalConfigWeapons(List<GimbalConfig> gimbalConfigs)
            {
                for (int i = 0; i < gimbalConfigs.Count; i++)
                {
                    var gc = gimbalConfigs[i];
                    if (gc.WeaponControllerConfig == null)
                    {
                        continue;
                    }

                    for (int n = 0; n < gc.WeaponControllerConfig.WeaponConfigurations.Count; n++)
                    {
                        var wc = gc.WeaponControllerConfig.WeaponConfigurations[n];
                        if (!wc.Enabled)
                        {
                            continue;
                        }

                        if (wc.ProjectileType == ProjectileType.Flare)
                        {
                            continue;
                        }

                        if (!ammoShortnames.Contains(wc.AmmoShortName))
                        {
                            ammoShortnames.Add(wc.AmmoShortName);
                        }
                    }
                }
            }

            void SetComputerStationWeapons(List<ComputerStationConfig> stationConfigs)
            {
                for (int i = 0; i < stationConfigs.Count; i++)
                {
                    var sc = stationConfigs[i];
                    if (sc.WeaponControllerConfig == null)
                    {
                        continue;
                    }

                    for (int n = 0; n < sc.WeaponControllerConfig.WeaponConfigurations.Count; n++)
                    {
                        var wc = sc.WeaponControllerConfig.WeaponConfigurations[n];
                        if (!wc.Enabled)
                        {
                            continue;
                        }

                        if (wc.ProjectileType == ProjectileType.Flare)
                        {
                            continue;
                        }

                        if (!ammoShortnames.Contains(wc.AmmoShortName))
                        {
                            ammoShortnames.Add(wc.AmmoShortName);
                        }
                    }
                }
            }

            public override bool ItemFilter(BasePlayer player, Item item, int targetSlot)
            {
                if (!base.ItemFilter(player, item, targetSlot))
                {
                    return false;
                }

                if (panelName != ATTACK_HELI_ROCKET_PANEL)
                {
                    return IsValidFlare(item.info.shortname) || IsValidRocket(item.info.shortname);
                }

                if (targetSlot == -1)
                {
                    if (IsValidFlare(item.info.shortname))
                    {
                        for (int i = 12; i < base.inventory.capacity; i++)
                        {
                            if (!base.inventory.SlotTaken(item, i))
                            {
                                targetSlot = i;
                                break;
                            }
                        }
                    }
                    else
                    {
                        if (!IsValidRocket(item.info.shortname))
                        {
                            return false;
                        }

                        for (int j = 0; j < 12; j++)
                        {
                            if (!base.inventory.SlotTaken(item, j))
                            {
                                targetSlot = j;
                                break;
                            }
                        }
                    }
                }

                if (targetSlot < 12)
                {
                    return IsValidRocket(item.info.shortname);
                }

                return IsValidFlare(item.info.shortname);
            }

            bool IsValidFlare(string shortName)
            {
                return shortName == FLARE_SHORTNAME;
            }

            bool IsValidRocket(string shortName)
            {
                return ammoShortnames.Contains(shortName);
            }
        }

        public class VehicleFuelContainer : SpecialStorageContainer
        {
            public bool AllowLootingWithDriver;
            IVehicle vehicle;

            public override void ServerInit()
            {
                base.ServerInit();

                if (Config.AllowedItems?.Count <= 0)
                {
                    this.inventory.SetOnlyAllowedItems(new ItemDefinition[] { ItemManager.itemList.Find(it => it.shortname == LOWGRADE_SHORTNAME) });
                }

                vehicle = KaruzaCustomEntity as IVehicle;
            }

            public override bool CanBeLooted(BasePlayer player)
            {
                if (!AllowLootingWithDriver)
                {
                    if (!KaruzaCustomEntity.BaseEntity.IsOn())
                    {
                        return base.CanBeLooted(player);
                    }

                    var driver = vehicle.CustomGetDriver();
                    if (object.ReferenceEquals(driver, null))
                    {
                        return base.CanBeLooted(player);
                    }

                    return base.CanBeLooted(player) && driver.userID == player.userID;
                }
                else
                {
                    return base.CanBeLooted(player);
                }
            }
        }

        public class VehicleBoostContainer : SpecialStorageContainer
        {
        }

        public class EngineStorageContainer : SpecialStorageContainer
        {
            static readonly EngineItemTypes[] largeSlotTypes = new EngineItemTypes[]
            {
                    EngineItemTypes.Crankshaft,
                    EngineItemTypes.Carburetor,
                    EngineItemTypes.SparkPlug,
                    EngineItemTypes.Valve,
                    EngineItemTypes.Piston,
                    EngineItemTypes.SparkPlug,
                    EngineItemTypes.Valve,
                    EngineItemTypes.Piston
            };

            static readonly EngineItemTypes[] smallSlotTypes = new EngineItemTypes[]
            {
                    EngineItemTypes.Crankshaft,
                    EngineItemTypes.Carburetor,
                    EngineItemTypes.SparkPlug,
                    EngineItemTypes.Valve,
                    EngineItemTypes.Piston,
            };

            public EngineItemTypes[] slotTypes = new EngineItemTypes[0];

            IVehicle vehicle;
            public VehicleModuleEngineItems allEngineItems;

            float accelerationBoostSlots;
            float topSpeedBoostSlots;
            float fuelEconomyBoostSlots;

            public float PerformanceFractionAcceleration;
            public float PerformanceFractionTopSpeed;
            public float PerformanceFractionFuelEconomy;
            public float OverallPerformanceFraction;

            public float internalDamageMultiplier = 0.5f;

            public bool IsUsable;

            bool ready;

            float accelerationBoostPercent;
            float topSpeedBoostPercent;
            float fuelEconomyBoostPercent;

            public override void ServerInit()
            {
                maxStackSize = 1;
                // Small
                if (inventorySlots == 5)
                {
                    accelerationBoostSlots = 2;
                    fuelEconomyBoostSlots = 2;
                    topSpeedBoostSlots = 3;
                    slotTypes = smallSlotTypes;
                }
                // Large
                else
                {
                    accelerationBoostSlots = 4;
                    fuelEconomyBoostSlots = 3;
                    topSpeedBoostSlots = 4;
                    slotTypes = largeSlotTypes;
                }

                base.ServerInit();

                if (HasKaruzaEntity)
                {
                    vehicle = KaruzaCustomEntity as IVehicle;
                }

                ready = true;
            }

            public void RefreshPerformanceStats()
            {
                PerformanceFractionAcceleration = GetPerformanceFraction(accelerationBoostPercent);
                PerformanceFractionTopSpeed = GetPerformanceFraction(topSpeedBoostPercent);
                PerformanceFractionFuelEconomy = GetPerformanceFraction(fuelEconomyBoostPercent);

                OverallPerformanceFraction = (PerformanceFractionAcceleration + PerformanceFractionTopSpeed + PerformanceFractionFuelEconomy) / 3f;
            }

            float GetPerformanceFraction(float statBoostPercent)
            {
                if (!IsUsable || !ready)
                {
                    return 0f;
                }

                var hf = vehicle.BaseVehicle.healthFraction;
                float num = Mathf.Lerp(0f, 0.25f, hf);
                float num2 = ((hf != 0f) ? (statBoostPercent * 0.75f) : 0f);
                return num + num2;
            }

            public override void Load(LoadInfo info)
            {
                base.Load(info);
                if (info.msg.engineStorage != null)
                {
                    IsUsable = info.msg.engineStorage.isUsable;
                    accelerationBoostPercent = info.msg.engineStorage.accelerationBoost;
                    topSpeedBoostPercent = info.msg.engineStorage.topSpeedBoost;
                    fuelEconomyBoostPercent = info.msg.engineStorage.fuelEconomyBoost;
                }

                RefreshPerformanceStats();
            }

            public override int GetIdealSlot(BasePlayer player, ItemContainer container, Item item)
            {
                return GetValidSlot(item);
            }

            public int GetValidSlot(Item item)
            {
                ItemModEngineItem component = item.info.GetComponent<ItemModEngineItem>();
                if (component == null)
                {
                    return -1;
                }

                EngineItemTypes engineItemType = component.engineItemType;
                for (int i = 0; i < inventorySlots; i++)
                {
                    if (engineItemType == slotTypes[i] && !inventory.SlotTaken(item, i))
                    {
                        return i;
                    }
                }

                return -1;
            }

            public override void OnInventoryFirstCreated(ItemContainer container)
            {
                RefreshLoadoutData();
            }

            public override void OnItemAddedOrRemoved(Item item, bool added)
            {
                RefreshLoadoutData();
            }

            public override bool ItemFilter(BasePlayer player, Item item, int targetSlot)
            {
                if (!base.ItemFilter(player, item, targetSlot))
                {
                    return false;
                }

                if (targetSlot < 0 || targetSlot >= slotTypes.Length)
                {
                    return false;
                }

                ItemModEngineItem component = item.info.GetComponent<ItemModEngineItem>();
                if (component != null && component.engineItemType == slotTypes[targetSlot])
                {
                    return true;
                }

                return false;
            }

            public void RefreshLoadoutData()
            {
                IsUsable = false;
                if (inventory.IsFull())
                {
                    var hasBrokenItem = false;
                    for (int i = 0; i < inventory.itemList.Count; i++)
                    {
                        var item = inventory.itemList[i];
                        if (item.isBroken)
                        {
                            hasBrokenItem = true;
                            break;
                        }
                    }

                    if (!hasBrokenItem)
                    {
                        IsUsable = true;
                    }
                }

                accelerationBoostPercent = GetContainerItemsValueFor(EngineItemTypeEx.BoostsAcceleration) / accelerationBoostSlots;
                topSpeedBoostPercent = GetContainerItemsValueFor(EngineItemTypeEx.BoostsTopSpeed) / topSpeedBoostSlots;
                fuelEconomyBoostPercent = GetContainerItemsValueFor(EngineItemTypeEx.BoostsFuelEconomy) / fuelEconomyBoostSlots;
                SendNetworkUpdate();
                RefreshPerformanceStats();
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);
                info.msg.engineStorage = Pool.Get<ProtoBuf.EngineStorage>();
                info.msg.engineStorage.isUsable = IsUsable;
                info.msg.engineStorage.accelerationBoost = accelerationBoostPercent;
                info.msg.engineStorage.topSpeedBoost = topSpeedBoostPercent;
                info.msg.engineStorage.fuelEconomyBoost = fuelEconomyBoostPercent;
            }

            public float GetContainerItemsValueFor(Func<EngineItemTypes, bool> boostConditional)
            {
                float num = 0f;
                foreach (Item item in inventory.itemList)
                {
                    ItemModEngineItem component = item.info.GetComponent<ItemModEngineItem>();
                    if (component != null && boostConditional(component.engineItemType) && !item.isBroken)
                    {
                        num += item.amount * GetTierValue(component.tier);
                    }
                }

                return num;
            }

            public float GetTierValue(int tier)
            {
                switch (tier)
                {
                    case 1:
                        return 0.6f;
                    case 2:
                        return 0.8f;
                    case 3:
                        return 1f;
                    default:
                        Debug.LogError(GetType().Name + ": Unrecognised item tier: " + tier);
                        return 0f;
                }
            }
        }

        public class SpecialFurnaceOven : SpecialOven
        {
            public override void ServerInit()
            {
                inputSlots = 2;
                _inputSlotIndex = 0;
                outputSlots = 3;
                _outputSlotIndex = 2;
                fuelSlots = 0;

                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.Reserved5, true);
                }

                base.ServerInit();
            }

            public override bool CanRunWithNoFuel
            {
                get
                {
                    return true;
                }
            }

            public override void OnItemAddedOrRemoved(Item item, bool bAdded)
            {
                base.OnItemAddedOrRemoved(item, bAdded);

                if (bAdded)
                {
                    var isCookable = item.info.ItemModCookable != null;
                    if (isCookable && !HasFlag(Flags.On))
                    {
                        StartCooking();
                    }
                }
                else if (!HasCookable())
                {
                    StopCooking();
                }
            }

            bool HasCookable()
            {
                if (inventory == null)
                {
                    return false;
                }

                foreach (Item item in inventory.itemList)
                {
                    if (IsBurnableItem(item) || IsMaterialInput(item))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public class SpecialOven : BaseOven, IStorageContainer
        {
            public IKaruzaCustomEntity KaruzaCustomEntity { get; protected set; }
            public BaseEntity Container { get { return this; } }
            public string PanelName { get { return panelName; } }
            public bool OnlyOneUser { get { return onlyOneUser; } }
            public ContainerConfiguration Config { get; set; }
            public bool DestroyParentOnDestroy = true;
            public bool HasKaruzaEntity;

            public override bool ShouldInheritNetworkGroup()
            {
                return false;
            }

            void Awake()
            {
                allowedItems = new ItemDefinition[0];
            }

            public override void ServerInit()
            {
                networkEntityScale = true;

                canBeDemolished = false;
                base.ServerInit();
                BuildingManager.server.Remove(this);

                this.KaruzaCustomEntity = this.GetComponentInParent<IKaruzaCustomEntity>();

                HasKaruzaEntity = this.KaruzaCustomEntity != null;

                if (Config != null && Config.AllowedItems?.Count > 0)
                {
                    this.inventory.SetOnlyAllowedItems(ItemManager.itemList.FindAll(it => Config.AllowedItems.Contains(it.shortname)).ToArray());
                }
            }

            public override void OvenFull()
            {
                Invoke(PauseCooking, 0f);
            }

            public void PauseCooking()
            {
                UpdateAttachmentTemperature();
                if (base.inventory != null)
                {
                    base.inventory.temperature = 15f;
                    foreach (Item item in base.inventory.itemList)
                    {
                        if (item.HasFlag(Flag.OnFire))
                        {
                            item.SetFlag(Flag.OnFire, b: false);
                            item.MarkDirty();
                        }

                        if (item.HasFlag(Flag.Cooking))
                        {
                            item.SetFlag(Flag.Cooking, b: false);
                            item.MarkDirty();
                        }
                    }
                }

                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.Reserved8, true);
                }
            }

            public override bool CanPickupOven()
            {
                return false;
            }

            public override bool CanCompletePickup(BasePlayer player)
            {
                return false;
            }

            public override bool ShouldDisplayPickupOption(BasePlayer player)
            {
                return false;
            }

            public override void Hurt(HitInfo info)
            {
                this.KaruzaCustomEntity.Hurt(info);
                return;
            }

            public override void OnAttacked(HitInfo info)
            {
                using (TimeWarning.New(BASECOMBATENTITY_ONATTACKED))
                {
                    this.KaruzaCustomEntity.OnAttacked(info);
                    return;
                }
            }

            public override void DoRepair(BasePlayer player)
            {
                this.KaruzaCustomEntity.DoRepair(player);
            }

            public override bool CanBeLooted(BasePlayer player)
            {
                if (Config.IgnoreCodelock)
                {
                    return true;
                }

                return KaruzaCustomEntity.CanAccess(player);
            }

            public void CustomDropItems(BaseEntity initiator = null)
            {
                ItemContainer itemContainer = this.inventory;
                if (itemContainer == null || itemContainer.itemList == null || itemContainer.itemList.Count == 0)
                {
                    return;
                }

                if (this.ShouldDropItemsIndividually() || (itemContainer.itemList.Count == 1 && !this.DropFloats))
                {
                    if (initiator != null)
                    {
                        this.DropBonusItems(initiator, itemContainer);
                    }

                    DropUtil.DropItems(itemContainer, this.GetDropPosition());
                }
                else
                {
                    string prefab = (this.DropFloats ? "assets/prefabs/misc/item drop/item_drop_buoyant.prefab" : "assets/prefabs/misc/item drop/item_drop.prefab");
                    itemContainer.Drop(prefab, this.GetDropPosition(), this.Transform.rotation, 0);
                }
            }

            public override void DestroyShared()
            {
                base.DestroyShared();

                if (DestroyParentOnDestroy && KaruzaCustomEntity != null && !KaruzaCustomEntity.BaseEntity.IsDestroyed)
                {
                    KaruzaCustomEntity.BaseEntity.Kill();
                }
            }

            public override void UpdateNetworkGroup()
            {
                isCallingUpdateNetworkGroup = false;
                if (net == null || Network.Net.sv == null || Network.Net.sv.visibility == null)
                {
                    return;
                }

                using (TimeWarning.New(UPDATE_NETWORKGROUP))
                {
                    var group = BaseNetworkable.GlobalNetworkGroup;
                    if (HasKaruzaEntity)
                    {
                        if (KaruzaCustomEntity.BaseEntity.IsDestroyed || KaruzaCustomEntity.BaseEntity.net == null)
                        {
                            return;
                        }

                        group = KaruzaCustomEntity.BaseEntity.net.group;
                    }

                    if (net.SwitchGroup(group))
                    {
                        if (isSpawned)
                        {
                            if (net.group == null)
                            {
                                Debug.LogWarning(ToString() + " changed its network group to null");
                                return;
                            }

                            NetWrite netWrite = Network.Net.sv.StartWrite();
                            netWrite.PacketID(Message.Type.GroupChange);
                            netWrite.EntityID(net.ID);
                            netWrite.GroupID(net.group.ID);
                            netWrite.Send(new SendInfo(net.group.subscribers));
                        }
                    }
                }
            }

            public override bool ShouldNetworkTo(BasePlayer player)
            {
                if (IsTransferProtected())
                {
                    return false;
                }

                if (limitNetworking)
                {
                    return false;
                }

                if (HasKaruzaEntity)
                {
                    return KaruzaCustomEntity.BaseEntity.ShouldNetworkTo(player);
                }

                if (net.group == null)
                {
                    return true;
                }

                return player.net.subscriber.IsSubscribed(net.group);
            }
        }

        public class SpecialFridge : SpecialStorageContainer, IFoodSpoilModifier
        {
            public ItemCategory OnlyAcceptCategory = ItemCategory.All;

            public float PoweredFoodSpoilageRateMultiplier = 0.1f;

            public float GetSpoilMultiplier(Item arg)
            {
                return PoweredFoodSpoilageRateMultiplier;
            }

            public override void ServerInit()
            {
                base.ServerInit();
                base.inventory.canAcceptItem = CanAcceptItem;
                OnlyAcceptCategory = ItemCategory.Food;

                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.Reserved8, true);
                }
            }

            public bool CanAcceptItem(BasePlayer player, Item item, int targetSlot)
            {
                if (OnlyAcceptCategory == ItemCategory.All)
                {
                    return true;
                }

                if (item.info.category != OnlyAcceptCategory)
                {
                    return false;
                }

                return true;
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);
            }
        }

        public class SpecialRecycler : Recycler, IStorageContainer
        {
            public IKaruzaCustomEntity KaruzaCustomEntity { get; protected set; }

            public BaseEntity Container { get { return this; } }
            public string PanelName { get { return panelName; } }
            public bool OnlyOneUser { get { return onlyOneUser; } }
            public ContainerConfiguration Config { get; set; }
            public bool DestroyParentOnDestroy = true;
            public bool HasKaruzaEntity;

            bool ignoreLock;
            bool autoToggle;

            public override bool ShouldInheritNetworkGroup()
            {
                return false;
            }

            void Awake()
            {
                allowedItems = new ItemDefinition[0];
            }

            public override void ServerInit()
            {
                networkEntityScale = true;

                canBeDemolished = false;
                base.ServerInit();
                BuildingManager.server.Remove(this);

                this.KaruzaCustomEntity = this.GetComponentInParent<IKaruzaCustomEntity>();

                HasKaruzaEntity = this.KaruzaCustomEntity != null;
                startSound = new GameObjectRef();
                stopSound = new GameObjectRef();

                inventorySlots = 12;
            }

            public void Configure(RecyclerConfiguration config)
            {
                onlyOneUser = config.OnlyOneUser;
                ignoreLock = config.IgnoreCodelock;
                autoToggle = config.AutoToggle;

                if (!string.IsNullOrEmpty(config.StartEffect))
                {
                    startSound.guid = GameManifest.pathToGuid[config.StartEffect];
                }

                if (!string.IsNullOrEmpty(config.StopEffect))
                {
                    stopSound.guid = GameManifest.pathToGuid[config.StopEffect];
                }

                inventorySlots = 12;
            }


            public override void OnItemAddedOrRemoved(Item item, bool bAdded)
            {
                base.OnItemAddedOrRemoved(item, bAdded);

                if (autoToggle && bAdded && HasRecyclable())
                {
                    StartRecycling();
                }
            }


            public override void Hurt(HitInfo info)
            {
                this.KaruzaCustomEntity.Hurt(info);
                return;
            }

            public override void OnAttacked(HitInfo info)
            {
                using (TimeWarning.New(BASECOMBATENTITY_ONATTACKED))
                {
                    this.KaruzaCustomEntity.OnAttacked(info);
                    return;
                }
            }

            public override void DoRepair(BasePlayer player)
            {
                this.KaruzaCustomEntity.DoRepair(player);
            }

            public override bool CanBeLooted(BasePlayer player)
            {
                if (ignoreLock)
                {
                    return true;
                }

                return KaruzaCustomEntity.CanAccess(player);
            }

            public void CustomDropItems(BaseEntity initiator = null)
            {
                ItemContainer itemContainer = this.inventory;
                if (itemContainer == null || itemContainer.itemList == null || itemContainer.itemList.Count == 0)
                {
                    return;
                }

                if (this.ShouldDropItemsIndividually() || (itemContainer.itemList.Count == 1 && !this.DropFloats))
                {
                    if (initiator != null)
                    {
                        this.DropBonusItems(initiator, itemContainer);
                    }

                    DropUtil.DropItems(itemContainer, this.GetDropPosition());
                }
                else
                {
                    string prefab = (this.DropFloats ? "assets/prefabs/misc/item drop/item_drop_buoyant.prefab" : "assets/prefabs/misc/item drop/item_drop.prefab");
                    itemContainer.Drop(prefab, this.GetDropPosition(), this.Transform.rotation, 0);
                }
            }

            public override void DestroyShared()
            {
                base.DestroyShared();

                if (DestroyParentOnDestroy && KaruzaCustomEntity != null && !KaruzaCustomEntity.BaseEntity.IsDestroyed)
                {
                    KaruzaCustomEntity.BaseEntity.Kill();
                }
            }

            public override void UpdateNetworkGroup()
            {
                isCallingUpdateNetworkGroup = false;
                if (net == null || Network.Net.sv == null || Network.Net.sv.visibility == null)
                {
                    return;
                }

                using (TimeWarning.New(UPDATE_NETWORKGROUP))
                {
                    var group = BaseNetworkable.GlobalNetworkGroup;
                    if (HasKaruzaEntity)
                    {
                        if (KaruzaCustomEntity.BaseEntity.IsDestroyed || KaruzaCustomEntity.BaseEntity.net == null)
                        {
                            return;
                        }

                        group = KaruzaCustomEntity.BaseEntity.net.group;
                    }

                    if (net.SwitchGroup(group))
                    {
                        if (isSpawned)
                        {
                            if (net.group == null)
                            {
                                Debug.LogWarning(ToString() + " changed its network group to null");
                                return;
                            }

                            NetWrite netWrite = Network.Net.sv.StartWrite();
                            netWrite.PacketID(Message.Type.GroupChange);
                            netWrite.EntityID(net.ID);
                            netWrite.GroupID(net.group.ID);
                            netWrite.Send(new SendInfo(net.group.subscribers));
                        }
                    }
                }
            }

            public override bool ShouldNetworkTo(BasePlayer player)
            {
                if (IsTransferProtected())
                {
                    return false;
                }

                if (limitNetworking)
                {
                    return false;
                }

                if (HasKaruzaEntity)
                {
                    return KaruzaCustomEntity.BaseEntity.ShouldNetworkTo(player);
                }

                if (net.group == null)
                {
                    return true;
                }

                return player.net.subscriber.IsSubscribed(net.group);
            }

        }

        public class SpecialWorkbench : Workbench, IStorageContainer
        {
            public IKaruzaCustomEntity KaruzaCustomEntity { get; protected set; }

            public BaseEntity Container { get { return this; } }
            public string PanelName { get { return panelName; } }
            public bool OnlyOneUser { get { return onlyOneUser; } }
            public ContainerConfiguration Config { get; set; }
            public bool DestroyParentOnDestroy = true;
            public bool HasKaruzaEntity;

            public override bool ShouldInheritNetworkGroup()
            {
                return false;
            }

            void Awake()
            {
                allowedItems = new ItemDefinition[0];
            }

            public override void ServerInit()
            {
                networkEntityScale = true;

                //techTrees = ScriptableObject.CreateInstance<TechTreeData>();
                Workbenchlevel = 1;
                if (name == PREFAB_WORKBENCH_2)
                {
                    Workbenchlevel = 2;
                }
                else if (name == PREFAB_WORKBENCH_3)
                {
                    Workbenchlevel = 3;
                }

                techTrees = Instance.CachedTechTreeData.ToArray();


                canBeDemolished = false;
                base.ServerInit();
                BuildingManager.server.Remove(this);

                this.KaruzaCustomEntity = this.GetComponentInParent<IKaruzaCustomEntity>();

                HasKaruzaEntity = this.KaruzaCustomEntity != null;

                if (Config != null && Config.AllowedItems?.Count > 0)
                {
                    this.inventory.SetOnlyAllowedItems(ItemManager.itemList.FindAll(it => Config.AllowedItems.Contains(it.shortname)).ToArray());
                }

                InitializeTrigger();
            }

            void InitializeTrigger()
            {
                var boxGo = new GameObject();
                boxGo.transform.SetParent(KaruzaCustomEntity.BaseEntity.transform, false);
                boxGo.transform.localPosition = KaruzaCustomEntity.BaseEntity.bounds.center;
                boxGo.layer = (int)Layer.Trigger;

                var boxCol = boxGo.AddComponent<BoxCollider>();
                boxCol.isTrigger = true;
                boxCol.size = KaruzaCustomEntity.BaseEntity.bounds.size;

                bounds = new Bounds()
                {
                    center = KaruzaCustomEntity.BaseEntity.bounds.center,
                    size = KaruzaCustomEntity.BaseEntity.bounds.size
                };

                var wbTrigger = boxGo.AddComponent<TriggerWorkbench>();
                wbTrigger.parentBench = this;
                wbTrigger.interestLayers = LayerMask.GetMask(LayerMask.LayerToName((int)Layer.Player_Server));
            }

            public override bool CanCompletePickup(BasePlayer player)
            {
                return false;
            }

            public override bool ShouldDisplayPickupOption(BasePlayer player)
            {
                return false;
            }

            public override void Hurt(HitInfo info)
            {
                this.KaruzaCustomEntity.Hurt(info);
                return;
            }

            public override void OnAttacked(HitInfo info)
            {
                using (TimeWarning.New(BASECOMBATENTITY_ONATTACKED))
                {
                    this.KaruzaCustomEntity.OnAttacked(info);
                    return;
                }
            }

            public override void DoRepair(BasePlayer player)
            {
                this.KaruzaCustomEntity.DoRepair(player);
            }

            public override bool CanBeLooted(BasePlayer player)
            {
                return KaruzaCustomEntity.CanAccess(player);
            }

            public void CustomDropItems(BaseEntity initiator = null)
            {
                ItemContainer itemContainer = this.inventory;
                if (itemContainer == null || itemContainer.itemList == null || itemContainer.itemList.Count == 0)
                {
                    return;
                }

                if (this.ShouldDropItemsIndividually() || (itemContainer.itemList.Count == 1 && !this.DropFloats))
                {
                    if (initiator != null)
                    {
                        this.DropBonusItems(initiator, itemContainer);
                    }

                    DropUtil.DropItems(itemContainer, this.GetDropPosition());
                }
                else
                {
                    string prefab = (this.DropFloats ? "assets/prefabs/misc/item drop/item_drop_buoyant.prefab" : "assets/prefabs/misc/item drop/item_drop.prefab");
                    itemContainer.Drop(prefab, this.GetDropPosition(), this.Transform.rotation, 0);
                }
            }

            public override void DestroyShared()
            {
                base.DestroyShared();

                if (DestroyParentOnDestroy && KaruzaCustomEntity != null && !KaruzaCustomEntity.BaseEntity.IsDestroyed)
                {
                    KaruzaCustomEntity.BaseEntity.Kill();
                }
            }

            public override void UpdateNetworkGroup()
            {
                isCallingUpdateNetworkGroup = false;
                if (net == null || Network.Net.sv == null || Network.Net.sv.visibility == null)
                {
                    return;
                }

                using (TimeWarning.New(UPDATE_NETWORKGROUP))
                {
                    var group = BaseNetworkable.GlobalNetworkGroup;
                    if (HasKaruzaEntity)
                    {
                        if (KaruzaCustomEntity.BaseEntity.IsDestroyed || KaruzaCustomEntity.BaseEntity.net == null)
                        {
                            return;
                        }

                        group = KaruzaCustomEntity.BaseEntity.net.group;
                    }

                    if (net.SwitchGroup(group))
                    {
                        if (isSpawned)
                        {
                            if (net.group == null)
                            {
                                Debug.LogWarning(ToString() + " changed its network group to null");
                                return;
                            }

                            NetWrite netWrite = Network.Net.sv.StartWrite();
                            netWrite.PacketID(Message.Type.GroupChange);
                            netWrite.EntityID(net.ID);
                            netWrite.GroupID(net.group.ID);
                            netWrite.Send(new SendInfo(net.group.subscribers));
                        }
                    }
                }
            }

            public override bool ShouldNetworkTo(BasePlayer player)
            {
                if (IsTransferProtected())
                {
                    return false;
                }

                if (limitNetworking)
                {
                    return false;
                }

                if (HasKaruzaEntity)
                {
                    return KaruzaCustomEntity.BaseEntity.ShouldNetworkTo(player);
                }

                if (net.group == null)
                {
                    return true;
                }

                return player.net.subscriber.IsSubscribed(net.group);
            }
        }

        public class SpecialMixingTable : MixingTable, IStorageContainer
        {
            public IKaruzaCustomEntity KaruzaCustomEntity { get; protected set; }
            public BaseEntity Container { get { return this; } }
            public string PanelName { get { return panelName; } }
            public bool OnlyOneUser { get { return onlyOneUser; } }
            public ContainerConfiguration Config { get; set; }
            public bool DestroyParentOnDestroy = true;
            public bool HasKaruzaEntity;

            public override bool ShouldInheritNetworkGroup()
            {
                return false;
            }

            void Awake()
            {
                allowedItems = new ItemDefinition[0];
                if (Recipes == null)
                {
                    Recipes = ScriptableObject.CreateInstance<RecipeList>();
                }
            }

            public override void ServerInit()
            {
                networkEntityScale = true;

                canBeDemolished = false;
                base.ServerInit();
                BuildingManager.server.Remove(this);

                this.KaruzaCustomEntity = this.GetComponentInParent<IKaruzaCustomEntity>();

                HasKaruzaEntity = this.KaruzaCustomEntity != null;

                if (Config != null && Config.AllowedItems?.Count > 0)
                {
                    this.inventory.SetOnlyAllowedItems(ItemManager.itemList.FindAll(it => Config.AllowedItems.Contains(it.shortname)).ToArray());
                }
            }

            public override bool CanCompletePickup(BasePlayer player)
            {
                return false;
            }

            public override bool ShouldDisplayPickupOption(BasePlayer player)
            {
                return false;
            }

            public override void Hurt(HitInfo info)
            {
                this.KaruzaCustomEntity.Hurt(info);
                return;
            }

            public override void OnAttacked(HitInfo info)
            {
                using (TimeWarning.New(BASECOMBATENTITY_ONATTACKED))
                {
                    this.KaruzaCustomEntity.OnAttacked(info);
                    return;
                }
            }

            public override void DoRepair(BasePlayer player)
            {
                this.KaruzaCustomEntity.DoRepair(player);
            }

            public override bool CanBeLooted(BasePlayer player)
            {
                if (Config.IgnoreCodelock)
                {
                    return true;
                }

                return KaruzaCustomEntity.CanAccess(player);
            }

            public void CustomDropItems(BaseEntity initiator = null)
            {
                ItemContainer itemContainer = this.inventory;
                if (itemContainer == null || itemContainer.itemList == null || itemContainer.itemList.Count == 0)
                {
                    return;
                }

                if (this.ShouldDropItemsIndividually() || (itemContainer.itemList.Count == 1 && !this.DropFloats))
                {
                    if (initiator != null)
                    {
                        this.DropBonusItems(initiator, itemContainer);
                    }

                    DropUtil.DropItems(itemContainer, this.GetDropPosition());
                }
                else
                {
                    string prefab = (this.DropFloats ? "assets/prefabs/misc/item drop/item_drop_buoyant.prefab" : "assets/prefabs/misc/item drop/item_drop.prefab");
                    itemContainer.Drop(prefab, this.GetDropPosition(), this.Transform.rotation, 0);
                }
            }

            public override void DestroyShared()
            {
                base.DestroyShared();

                if (DestroyParentOnDestroy && KaruzaCustomEntity != null && !KaruzaCustomEntity.BaseEntity.IsDestroyed)
                {
                    KaruzaCustomEntity.BaseEntity.Kill();
                }
            }

            public override void UpdateNetworkGroup()
            {
                isCallingUpdateNetworkGroup = false;
                if (net == null || Network.Net.sv == null || Network.Net.sv.visibility == null)
                {
                    return;
                }

                using (TimeWarning.New(UPDATE_NETWORKGROUP))
                {
                    var group = BaseNetworkable.GlobalNetworkGroup;
                    if (HasKaruzaEntity)
                    {
                        if (KaruzaCustomEntity.BaseEntity.IsDestroyed || KaruzaCustomEntity.BaseEntity.net == null)
                        {
                            return;
                        }

                        group = KaruzaCustomEntity.BaseEntity.net.group;
                    }

                    if (net.SwitchGroup(group))
                    {
                        if (isSpawned)
                        {
                            if (net.group == null)
                            {
                                Debug.LogWarning(ToString() + " changed its network group to null");
                                return;
                            }

                            NetWrite netWrite = Network.Net.sv.StartWrite();
                            netWrite.PacketID(Message.Type.GroupChange);
                            netWrite.EntityID(net.ID);
                            netWrite.GroupID(net.group.ID);
                            netWrite.Send(new SendInfo(net.group.subscribers));
                        }
                    }
                }
            }

            public override bool ShouldNetworkTo(BasePlayer player)
            {
                if (IsTransferProtected())
                {
                    return false;
                }

                if (limitNetworking)
                {
                    return false;
                }

                if (HasKaruzaEntity)
                {
                    return KaruzaCustomEntity.BaseEntity.ShouldNetworkTo(player);
                }

                if (net.group == null)
                {
                    return true;
                }

                return player.net.subscriber.IsSubscribed(net.group);
            }
        }

        #endregion

        #region Counters

        public class SpecialCounter : SpecialIOEntity
        {
            public int ShownCounter = 0;
            public int ShownPassthru = 0;

            public bool DisplayPassthrough { get { return this.HasFlag(Flags.Reserved2); } }
            public bool DisplayCounter { get { return !this.DisplayPassthrough; } }

            public override void Awake()
            {
                base.Awake();
                enabled = true;
            }

            public override void ServerInit()
            {
                base.ServerInit();

                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(BaseEntity.Flags.Busy, true);
                }
            }

            public virtual void ShowCounter()
            {
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(BaseEntity.Flags.Reserved2, false);
                }
            }

            public virtual void ShowPassthru()
            {
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(BaseEntity.Flags.Reserved2, true);
                }
            }

            public override bool CanCompletePickup(BasePlayer player)
            {
                return false;
            }

            public override bool ShouldDisplayPickupOption(BasePlayer player)
            {
                return false;
            }

            public void SetPower(bool wants, bool networkUpdate = true)
            {
                if (HasFlag(Flags.On) == wants)
                {
                    return;
                }

                if (networkUpdate)
                {
                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(BaseEntity.Flags.Reserved8, wants);
                        flagsUpdateScope.Set(BaseEntity.Flags.On, wants);
                    }
                }
                else
                {
                    SetFlagLocal(Flags.Reserved8, wants);
                    SetFlagLocal(Flags.On, wants);
                }
            }

            public virtual bool SetCounter(int amount)
            {
                amount = Mathf.Clamp(amount, 0, 9999);
                if (ShownCounter == amount)
                {
                    return false;
                }

                ShownCounter = amount;
                return true;
            }

            public virtual bool SetPassthru(int amount)
            {
                amount = Mathf.Clamp(amount, 0, 9999);
                if (ShownPassthru == amount)
                {
                    return false;
                }

                ShownPassthru = amount;
                return true;
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);

                if (info.forDisk)
                {
                    return;
                }

                info.msg.ioEntity.genericInt1 = ShownCounter;
                info.msg.ioEntity.genericInt2 = ShownPassthru;
                info.msg.ioEntity.genericInt3 = 1;
            }
        }

        public class VehicleCounter : SpecialCounter
        {
            protected float UpdateDelay = COUNTER_UPDATE_DELAY;
            protected float NextUpdateTime;
            protected float CurrentTime;

            protected IVehicle Vehicle;

            bool powerOn;

            public override void ServerInit()
            {
                base.ServerInit();
                Vehicle = KaruzaEntity as IVehicle;
                Vehicle.OnPowerToggle += OnPowerToggle;
                enabled = false;
            }

            protected virtual void OnPowerToggle(bool newState)
            {
                enabled = newState;
                if (!newState)
                {
                    ShowCounter();
                }
            }

            void LateUpdate()
            {
                CurrentTime = Time.realtimeSinceStartup;
                if (NextUpdateTime >= CurrentTime)
                {
                    return;
                }

                UpdateCounter();
                NextUpdateTime = CurrentTime + UpdateDelay;
            }

            protected virtual void UpdateCounter()
            {

            }

            public override bool SetCounter(int amount)
            {
                if (!base.SetCounter(amount))
                {
                    return false;
                }

                InvalidateNetworkCache();
                for (int i = 0; i < Vehicle.MountPoints.Count; i++)
                {
                    var mp = Vehicle.MountPoints[i];
                    var mounted = mp.mountable.GetMounted();
                    if (!object.ReferenceEquals(mounted, null))
                    {
                        mounted.QueueUpdate(NetworkQueue.Update, this);
                    }
                }

                return true;
            }

            public override bool SetPassthru(int amount)
            {
                if (!base.SetPassthru(amount))
                {
                    return false;
                }

                InvalidateNetworkCache();
                for (int i = 0; i < Vehicle.MountPoints.Count; i++)
                {
                    var mp = Vehicle.MountPoints[i];
                    var mounted = mp.mountable.GetMounted();
                    if (!object.ReferenceEquals(mounted, null))
                    {
                        mounted.QueueUpdate(NetworkQueue.Update, this);
                    }
                }

                return true;
            }
        }

        public class VehicleAmmoCounter : VehicleCounter
        {
            WeaponSystem weaponSystem;

            float lastUpdateTime;
            bool reloading;
            bool invokingUpdate;
            IWeaponController vehicle;
            BasePlayer cachedPlayer;

            public override void ServerInit()
            {
                base.ServerInit();
                UpdateDelay = WEAPON_COUNTER_UPDATE_DELAY;
                vehicle = KaruzaEntity as IWeaponController;
                enabled = false;
            }

            public void ConfigureCounter(WeaponSystem weaponSystem)
            {
                this.weaponSystem = weaponSystem;
                if (weaponSystem == null || weaponSystem.Config == null)
                {
                    return;
                }

                weaponSystem.OnWeaponFired += OnWeaponFired;
            }

            protected override void OnPowerToggle(bool newState)
            {
                base.OnPowerToggle(newState);

                if (newState)
                {
                    cachedPlayer = Vehicle.CustomGetDriver();
                    UpdateCounter();
                }
            }

            void OnWeaponFired(BasePlayer player)
            {
                if (invokingUpdate)
                {
                    return;
                }

                invokingUpdate = true;
                cachedPlayer = player;
                Invoke(UpdateCounter, WEAPON_COUNTER_UPDATE_DELAY);
            }

            protected override void UpdateCounter()
            {
                invokingUpdate = false;

                if (!HasFlag(Flags.Reserved8))
                {
                    cachedPlayer = null;
                    return;
                }

                // NextUpdateTime will be the last update time
                var lastAmmoUpdate = vehicle.LastAmmoUpdate;
                if (lastAmmoUpdate < lastUpdateTime && !DisplayPassthrough)
                {
                    return;
                }

                lastUpdateTime = CurrentTime;

                var noDriver = false;
                var driver = cachedPlayer;
                if (!object.ReferenceEquals(cachedPlayer, null) && !cachedPlayer.isMounted)
                {
                    driver = null;
                    noDriver = true;
                }

                var ammoCount = 0;
                if (noDriver && weaponSystem.Config.AmmoSource == AmmoSource.Inventory && !Vehicle.VehicleConfig.AmmoContainer.Enabled)
                {
                    ammoCount = 0;
                }
                else
                {
                    ammoCount = WeaponUtilities.GetAmmoCount(vehicle, driver, weaponSystem.Config);
                }

                var reloading = weaponSystem.Config.EnableReload && weaponSystem.MagazineCapacity <= 0;

                if (reloading)
                {
                    enabled = true;
                    SetCounter(0);
                    ShowPassthru();
                    SetPassthru(ammoCount);
                }
                else
                {
                    enabled = false;
                    cachedPlayer = null;

                    if (SetPassthru(0))
                    {
                        this.SendNetworkUpdateImmediate();
                    }

                    ShowCounter();
                    SetCounter(ammoCount);
                }
            }
        }

        public class VehicleHealthCounter : VehicleCounter
        {
            float cachedHealth;
            bool invokingUpdate;

            public override void ServerInit()
            {
                base.ServerInit();
                enabled = false;
            }

            protected override void UpdateCounter()
            {
                var health = KaruzaEntity.BaseEntity.Health();
                cachedHealth = health;
                SetCounter((int)health);
            }

            protected override void OnPowerToggle(bool newState)
            {
                base.OnPowerToggle(newState);

                if (newState)
                {
                    UpdateCounter();
                }
            }

            public void OnVehicleHealthChanged(float oldValue, float newValue)
            {
                if (newValue > 0 && !invokingUpdate && newValue != cachedHealth)
                {
                    invokingUpdate = true;
                    cachedHealth = newValue;
                    Invoke(OnHealthChangedUpdate, 0.25f);
                }
            }

            void OnHealthChangedUpdate()
            {
                SetCounter((int)cachedHealth);
                invokingUpdate = false;
            }
        }

        public class VehicleSpeedCounter : VehicleCounter
        {
            public bool ShowKmh;

            int cachedSpeed;

            protected override void UpdateCounter()
            {
                var speed = (int)Vehicle.BaseVehicle.rigidBody.linearVelocity.magnitude;
                if (speed == cachedSpeed)
                {
                    return;
                }

                cachedSpeed = speed;
                if (ShowKmh)
                {
                    SetCounter((int)(Vehicle.BaseVehicle.rigidBody.linearVelocity.magnitude * 3.6f));
                }
                else
                {
                    SetCounter(speed);
                }
            }
        }

        public class VehicleAltitudeCounter : VehicleCounter
        {
            float cachedAltitudeFloat;

            protected override void UpdateCounter()
            {
                var altitudeFloat = Vehicle.BaseVehicle.transform.position.y - TerrainMeta.HeightMap.GetHeight(CachedTransform.position);
                altitudeFloat = altitudeFloat < 0 ? 0 : altitudeFloat;
                if (altitudeFloat == cachedAltitudeFloat)
                {
                    return;
                }

                cachedAltitudeFloat = altitudeFloat;
                var altitude = (int)altitudeFloat;
                SetCounter(altitude);
            }
        }

        public class VehicleFuelCounter : VehicleCounter
        {
            bool invokingFuel;
            KaruzaVehicleFuelSystem fuelSystem;

            public override void ServerInit()
            {
                base.ServerInit();
                enabled = false;
            }

            public void SetFuelSystem(KaruzaVehicleFuelSystem fuelSystem)
            {
                if (!object.ReferenceEquals(this.fuelSystem, null))
                {
                    return;
                }

                this.fuelSystem = fuelSystem;
                this.fuelSystem.OnFuelUsed += OnFuelUsed;
            }

            protected override void OnPowerToggle(bool newState)
            {
                base.OnPowerToggle(newState);

                if (newState)
                {
                    UpdateCounter();
                }
            }

            protected override void UpdateCounter()
            {
                invokingFuel = false;

                var fuel = fuelSystem.GetFuelAmount();
                if (fuel == ShownCounter)
                {
                    return;
                }

                SetCounter(fuel);
            }

            private void OnFuelUsed(int amount, Item item)
            {
                if (amount > 0 && !invokingFuel)
                {
                    Invoke(UpdateCounter, 1f);
                }
            }
        }

        public class VehicleBoostTimeCounter : VehicleCounter
        {
            int cachedBoostTime;

            public override void ServerInit()
            {
                base.ServerInit();
                enabled = false;

                Vehicle.OnBoostTimeUpdate += OnBoostTimerUpdate;
            }

            protected override void OnPowerToggle(bool newState)
            {
                base.OnPowerToggle(newState);

                if (newState)
                {
                    UpdateCounter();
                }
            }

            protected override void UpdateCounter()
            {
                int boostTime = 0;
                if (Vehicle.BoostTime > 0 && Vehicle.BoostTime < 1)
                {
                    boostTime = 1;
                }
                else
                {
                    boostTime = Mathf.FloorToInt(Vehicle.BoostTime);
                }

                if (boostTime == cachedBoostTime)
                {
                    return;
                }

                SetCounter(boostTime);
            }

            private void OnBoostTimerUpdate()
            {
                UpdateCounter();
            }
        }

        public class DynamicAnchorCounter : VehicleCounter
        {
            bool invokingUpdate;

            protected override void OnPowerToggle(bool newState)
            {
                enabled = newState;
                if (!newState)
                {
                    ShowPassthru();
                }
            }

            public override bool SetPassthru(int amount)
            {
                amount = Mathf.Clamp(amount, 0, 9999);
                if (ShownPassthru == amount)
                {
                    return false;
                }

                ShownPassthru = amount;
                InvalidateNetworkCache();
                if (!invokingUpdate)
                {
                    Invoke(SendUpdate, UpdateDelay);
                    invokingUpdate = true;
                }

                return true;
            }

            void SendUpdate()
            {
                invokingUpdate = false;
                for (int i = 0; i < Vehicle.MountPoints.Count; i++)
                {
                    var mp = Vehicle.MountPoints[i];
                    var mounted = mp.mountable.GetMounted();
                    if (!object.ReferenceEquals(mounted, null))
                    {
                        mounted.QueueUpdate(NetworkQueue.Update, this);
                    }
                }
            }
        }

        #endregion

        #region Buttons

        public class SpecialButton : SpecialIOEntity
        {
            public virtual float PressDuration => 1.5f;
            public Action<BasePlayer> PressAction;

            public override bool OnRpcMessage(BasePlayer player, uint rpc, Message msg)
            {
                using (TimeWarning.New(PRESSBUTTON_ONRPCMESSAGE))
                {
                    if (rpc == 4188121069u && player != null)
                    {
                        Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                        using (TimeWarning.New(RPC_PRESS))
                        {
                            using (TimeWarning.New(RPC_TIMEWARNING_CONDITIONS))
                            {
                                if (!RPC_Server.IsVisible.Test(4188121069u, RPC_PRESS, this, player, 3f))
                                {
                                    return true;
                                }
                            }

                            try
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                {
                                    RPCMessage rPCMessage = default(RPCMessage);
                                    rPCMessage.connection = msg.connection;
                                    rPCMessage.player = player;
                                    rPCMessage.read = msg.read;
                                    RPCMessage msg2 = rPCMessage;
                                    Press(msg2);
                                }
                            }
                            catch (Exception exception)
                            {
                                Debug.LogException(exception);
                                player.Kick("RPC Error in Custom RPC_Press");
                            }
                        }

                        return true;
                    }
                }

                return base.OnRpcMessage(player, rpc, msg);
            }

            public void Press(RPCMessage msg)
            {
                if (!CanPress(msg.player))
                {
                    return;
                }

                if (!IsOn() && Interface.CallHook(ON_BUTTON_PRESS_HOOK, this, msg.player) == null)
                {
                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(BaseEntity.Flags.On, true);
                    }

                    SendNetworkUpdateImmediate();
                    CustomAction(msg.player);
                    Invoke(Unpress, PressDuration);
                }
            }

            public virtual bool CanPress(BasePlayer player)
            {
                return true;
            }

            protected virtual void CustomAction(BasePlayer player)
            {
                PressAction?.Invoke(player);
            }

            void Unpress()
            {
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(BaseEntity.Flags.On, false);
                }
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);

                if (info.forDisk)
                {
                    return;
                }

                info.msg.ioEntity.genericFloat1 = PressDuration;
            }
        }

        public class CustomButton : SpecialButton, ICustomSwitch
        {
            public List<ILightToggle> Lights { get; set; } = new List<ILightToggle>();
            public List<SpecialDoor> Doors { get; set; } = new List<SpecialDoor>();
            public List<VehiclePropeller> Propellers { get; set; } = new List<VehiclePropeller>();
            public List<SpecialSiren> Sirens { get; set; } = new List<SpecialSiren>();
            public List<SpecialSail> Sails { get; set; } = new List<SpecialSail>();
            public List<SpecialAnchor> Anchors { get; set; } = new List<SpecialAnchor>();
            public bool ToggleLights { get; set; }
            public bool ToggleJoint { get; set; }
            public bool ToggleDoors { get; set; }
            public bool TogglePropellers { get; set; }
            public bool ToggleSirens { get; set; }
            public bool ToggleSails { get; set; }
            public bool ToggleAnchors { get; set; }
            public bool IgnoreCodelock { get; set; }
            public BaseSwitchConfig Config { get; set; }

            public Action<bool> OnJointToggle { get; set; }

            bool isOn;

            public override void ServerInit()
            {
                base.ServerInit();
                enabled = true;
            }

            public override bool CanPress(BasePlayer player)
            {
                if (IgnoreCodelock)
                {
                    return true;
                }

                return KaruzaCustomEntity.CanAccess(player);
            }

            protected override void CustomAction(BasePlayer player)
            {
                SetState(!isOn);
            }

            public void SetState(bool newState)
            {
                isOn = newState;
                if (ToggleLights)
                {
                    for (int i = 0; i < Lights.Count; i++)
                    {
                        var vl = Lights[i];
                        vl.ToggleLights(isOn);
                    }
                }

                if (ToggleJoint)
                {
                    OnJointToggle?.Invoke(isOn);
                }

                if (ToggleDoors)
                {
                    for (int i = 0; i < Doors.Count; i++)
                    {
                        var door = Doors[i];
                        door.CustomSetOpen(door.HasFlag(Flags.Open));
                    }
                }

                if (TogglePropellers)
                {
                    for (int i = 0; i < Propellers.Count; i++)
                    {
                        var propeller = Propellers[i];
                        propeller.ForceOn(isOn);
                    }
                }

                if (ToggleSirens)
                {
                    for (int i = 0; i < Sirens.Count; i++)
                    {
                        var siren = Sirens[i];
                        siren.ToggleSiren(isOn);
                    }
                }

                if (ToggleSails)
                {
                    for (int i = 0; i < Sails.Count; i++)
                    {
                        var sail = Sails[i];
                        if (isOn)
                        {
                            sail.Lower();
                        }
                        else
                        {
                            sail.Raise();
                        }
                    }
                }

                if (ToggleAnchors)
                {
                    for (int i = 0; i < Anchors.Count; i++)
                    {
                        var anchor = Anchors[i];
                        //anchor.ToggleSiren(isOn);
                    }
                }
            }

            void LateUpdate()
            {
                if (!isOn)
                {
                    return;
                }

                if (!TogglePropellers)
                {
                    return;
                }

                if (ToggleDoors || ToggleLights || ToggleJoint)
                {
                    return;
                }

                if (!KaruzaCustomEntity.IsPowered)
                {
                    CustomAction(null);
                }
            }
        }

        public class VehiclePowerButton : SpecialButton
        {
            public override float PressDuration => 0.1f;

            IVehicle vehicle;

            public override void ServerInit()
            {
                base.ServerInit();
                vehicle = KaruzaEntity as IVehicle;
            }

            protected override void CustomAction(BasePlayer player)
            {
                base.CustomAction(player);

                var curVal = vehicle.IsPowered;
                vehicle.TogglePower(player);

                if (!vehicle.VehicleConfig.GeneralToastSettings.Enabled || !vehicle.VehicleConfig.GeneralToastSettings.PowerSwitchEnabled)
                {
                    return;
                }

                if (curVal != vehicle.IsPowered)
                {
                    if (vehicle.IsPowered)
                    {
                        Instance.ShowToast(player, vehicle.VehicleConfig.GeneralToastSettings.PowerSwitchOnToast);
                    }
                    else
                    {
                        Instance.ShowToast(player, vehicle.VehicleConfig.GeneralToastSettings.PowerSwitchOffToast, GameTip.Styles.Red_Normal);
                    }
                }
            }
        }

        #endregion

        #region Switches

        public class SpecialSwitch : SpecialIOEntity
        {

            public Action<BasePlayer, bool> SwitchToggled;

            public override bool OnRpcMessage(BasePlayer player, uint rpc, Message msg)
            {
                using (TimeWarning.New(ELECTRICSWITCH_ONRPCMESSAGE))
                {
                    if (rpc == 3043863856u && player != null)
                    {
                        Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                        using (TimeWarning.New(RPC_SWITCH))
                        {
                            using (TimeWarning.New(RPC_TIMEWARNING_CONDITIONS))
                            {
                                if (!RPC_Server.IsVisible.Test(3043863856u, RPC_SWITCH, this, player, 3f))
                                {
                                    return true;
                                }
                            }

                            try
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                {
                                    RPCMessage rPCMessage = default(RPCMessage);
                                    rPCMessage.connection = msg.connection;
                                    rPCMessage.player = player;
                                    rPCMessage.read = msg.read;
                                    RPCMessage msg2 = rPCMessage;
                                    RPC_Switch(msg2);
                                }
                            }
                            catch (Exception exception)
                            {
                                Debug.LogException(exception);
                                player.Kick("RPC Error in Custom RPC_Switch");
                            }
                        }

                        return true;
                    }
                    else if (rpc == 2810053005u && player != null)
                    {
                        Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                        using (TimeWarning.New(RPC_TOGGLE_SWITCH))
                        {
                            using (TimeWarning.New(RPC_TIMEWARNING_CONDITIONS))
                            {
                                if (!RPC_Server.CallsPerSecond.Test(2810053005u, RPC_TOGGLE_SWITCH, this, player, 3uL))
                                {
                                    return true;
                                }

                                if (!RPC_Server.IsVisible.Test(2810053005u, RPC_TOGGLE_SWITCH, this, player, 3f))
                                {
                                    return true;
                                }
                            }

                            try
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                {
                                    RPCMessage rPCMessage = default(RPCMessage);
                                    rPCMessage.connection = msg.connection;
                                    rPCMessage.player = player;
                                    rPCMessage.read = msg.read;
                                    RPCMessage msg2 = rPCMessage;
                                    SetSwitch(msg2.player, !IsOn());
                                }
                            }
                            catch (Exception exception)
                            {
                                Debug.LogException(exception);
                                player.Kick("RPC Error in Custom ToggleSwitch");
                            }
                        }
                        return true;
                    }
                }

                return base.OnRpcMessage(player, rpc, msg);
            }

            public override void ServerInit()
            {
                base.ServerInit();
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(BaseEntity.Flags.Busy, false);
                }
            }

            public virtual void SetSwitch(BasePlayer player, bool state)
            {
                if (state != IsOn())
                {
                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(BaseEntity.Flags.On, state);
                        flagsUpdateScope.Set(BaseEntity.Flags.Busy, true);
                    }

                    Invoke(UnBusy, 0.5f);
                    SendNetworkUpdateImmediate();
                    SwitchToggled?.Invoke(player, state);
                }
            }

            public void RPC_Switch(RPCMessage msg)
            {
                bool switchOn = msg.read.Bool();

                if (!CanSwitch(msg.player))
                {
                    return;
                }

                SetSwitch(msg.player, switchOn);
            }

            protected void UnBusy()
            {
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(BaseEntity.Flags.Busy, false);
                }
            }

            public virtual bool CanSwitch(BasePlayer player)
            {
                if (KaruzaCustomEntity.BaseEntity.HasFlag(Flags.Broken))
                {
                    return false;
                }

                return true;
            }
        }

        public class CentralLockingSwitch : SpecialSwitch
        {
            IVehicle vehicle;

            public override void ServerInit()
            {
                base.ServerInit();
                vehicle = KaruzaEntity as IVehicle;
            }

            public override void SetSwitch(BasePlayer player, bool state)
            {
                if (vehicle.CustomHasDriver())
                {
                    var driver = vehicle.CustomGetDriver();
                    var isDriver = driver != null ? player.net.ID.Value == driver.net.ID.Value : false;
                    if (!isDriver)
                    {
                        return;
                    }
                }
                else
                {
                    if (!vehicle.CanAccess(player))
                    {
                        return;
                    }
                }

                if (state == IsOn())
                {
                    return;
                }

                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.On, state);
                    flagsUpdateScope.Set(Flags.Busy, true);
                }

                Invoke(UnBusy, 0.5f);
                vehicle.ToggleCentralLocking(state);
                SendNetworkUpdateImmediate();

                if (!vehicle.VehicleConfig.GeneralToastSettings.Enabled || !vehicle.VehicleConfig.GeneralToastSettings.CentralLockToastEnabled)
                {
                    return;
                }

                if (state)
                {
                    Instance.ShowToast(player, vehicle.VehicleConfig.GeneralToastSettings.CentralLockUnlockToast);
                }
                else
                {
                    Instance.ShowToast(player, vehicle.VehicleConfig.GeneralToastSettings.CentralLockLockedToast);
                }
            }
        }

        public class CustomSwitch : SpecialSwitch, ICustomSwitch
        {
            public List<ILightToggle> Lights { get; set; } = new List<ILightToggle>();
            public List<SpecialDoor> Doors { get; set; } = new List<SpecialDoor>();
            public List<VehiclePropeller> Propellers { get; set; } = new List<VehiclePropeller>();
            public List<SpecialSiren> Sirens { get; set; } = new List<SpecialSiren>();
            public List<SpecialSail> Sails { get; set; } = new List<SpecialSail>();
            public List<SpecialAnchor> Anchors { get; set; } = new List<SpecialAnchor>();
            public bool ToggleLights { get; set; }
            public bool ToggleJoint { get; set; }
            public bool ToggleDoors { get; set; }
            public bool TogglePropellers { get; set; }
            public bool ToggleSirens { get; set; }
            public bool ToggleSails { get; set; }
            public bool ToggleAnchors { get; set; }
            public bool IgnoreCodelock { get; set; }
            public Action<bool> OnJointToggle { get; set; }
            public BaseSwitchConfig Config { get; set; }

            public override void SetSwitch(BasePlayer player, bool state)
            {
                SetState(state);
            }

            public void SetState(bool state)
            {
                if (state == IsOn())
                {
                    return;
                }

                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.On, state);
                    flagsUpdateScope.Set(Flags.Busy, true);
                }

                Invoke(UnBusy, 0.5f);
                SendNetworkUpdateImmediate();

                if (ToggleLights)
                {
                    for (int i = 0; i < Lights.Count; i++)
                    {
                        var vl = Lights[i];
                        vl.ToggleLights(state);
                    }
                }

                if (ToggleJoint)
                {
                    OnJointToggle?.Invoke(state);
                }

                if (ToggleDoors)
                {
                    for (int i = 0; i < Doors.Count; i++)
                    {
                        var door = Doors[i];
                        door.CustomSetOpen(state);
                    }
                }

                if (TogglePropellers)
                {
                    for (int i = 0; i < Propellers.Count; i++)
                    {
                        var propeller = Propellers[i];
                        propeller.ForceOn(state);
                    }
                }

                if (ToggleSirens)
                {
                    for (int i = 0; i < Sirens.Count; i++)
                    {
                        var siren = Sirens[i];
                        siren.ToggleSiren(state);
                    }
                }

                if (ToggleSails)
                {
                    for (int i = 0; i < Sails.Count; i++)
                    {
                        var sail = Sails[i];
                        if (state)
                        {
                            sail.Lower();
                        }
                        else
                        {
                            sail.Raise();
                        }
                    }
                }

                if (ToggleAnchors)
                {
                    for (int i = 0; i < Anchors.Count; i++)
                    {
                        var anchor = Anchors[i];
                        //anchor.ToggleSiren(isOn);
                    }
                }
            }

            public override bool CanSwitch(BasePlayer player)
            {
                if (KaruzaCustomEntity.BaseEntity.HasFlag(Flags.Broken))
                {
                    return false;
                }

                if (IgnoreCodelock)
                {
                    return true;
                }

                return KaruzaCustomEntity.CanAccess(player);
            }
        }

        public class LandingGearSwitch : SpecialSwitch
        {
            ILandingGearVehicle vehicle;
            BaseVehicleConfig vehicleConfig;

            public LandingGearSettings Config;

            public override void ServerInit()
            {
                base.ServerInit();
                vehicle = KaruzaEntity.BaseEntity.GetComponent<ILandingGearVehicle>();
                vehicle.OnLandingGearToggle += OnLandingGearToggle;
                Config = vehicle.LandingGearSettings;
                vehicleConfig = vehicle.VehicleConfig;
            }

            void OnLandingGearToggle(bool wantsOn)
            {
                if (wantsOn == IsOn())
                {
                    return;
                }

                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(BaseEntity.Flags.On, wantsOn);
                    flagsUpdateScope.Set(BaseEntity.Flags.Busy, true);
                }

                Invoke(UnBusy, 0.5f);
                SendNetworkUpdateImmediate();
            }

            public override void SetSwitch(BasePlayer player, bool state)
            {
                if (!vehicle.IsPowered)
                {
                    return;
                }

                if (state == IsOn())
                {
                    return;
                }

                if (!state && !Config.CanToggleOffWhenGrounded && vehicle.IsOnGround)
                {
                    return;
                }

                if (Config.CanToggleByPassenger)
                {
                    if (!player.isMounted || player.GetMountedVehicle().net.ID.Value != KaruzaEntity.BaseEntity.net.ID.Value)
                    {
                        return;
                    }
                }
                else
                {
                    var driver = vehicle.CustomGetDriver();
                    var isDriver = driver != null ? player.net.ID.Value == driver.net.ID.Value : false;
                    if (!isDriver)
                    {
                        return;
                    }
                }

                var flags = this.flags;

                SetFlagLocal(Flags.On, state);
                SetFlagLocal(Flags.Busy, b: true);

                Invoke(UnBusy, 0.5f);
                SendNetworkUpdateImmediate();

                if (flags != this.flags)
                {
                    GlobalNetworkHandler.server?.TrySendNetworkUpdate(this);
                }

                vehicle.ToggleLandingGear(state);

                if (vehicleConfig.GeneralToastSettings.Enabled && vehicleConfig.GeneralToastSettings.LandingGearSwitchEnabled)
                {
                    if (state)
                    {
                        KaruzaEntitiesCommon.Instance.ShowToast(player, vehicleConfig.GeneralToastSettings.LandingGearSwitchOnToast);
                    }
                    else
                    {
                        KaruzaEntitiesCommon.Instance.ShowToast(player, vehicleConfig.GeneralToastSettings.LandingGearSwitchOffToast, GameTip.Styles.Red_Normal);
                    }
                }
            }
        }

        public class VehiclePowerSwitch : SpecialSwitch
        {
            IVehicle vehicle;

            public override void ServerInit()
            {
                base.ServerInit();

                vehicle = KaruzaEntity as IVehicle;
                vehicle.OnPowerToggle += OnPowerToggle;
            }

            void OnPowerToggle(bool newState)
            {
                if (!newState && HasFlag(Flags.On) && !HasFlag(Flags.Busy))
                {
                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(Flags.On, false);
                    }
                }
            }

            public override void SetSwitch(BasePlayer player, bool state)
            {
                if (state != IsOn() && vehicle.TogglePower(player))
                {
                    var curVal = !state;

                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(Flags.On, state);
                        flagsUpdateScope.Set(Flags.Busy, true);
                    }

                    Invoke(UnBusy, 0.5f);
                    SendNetworkUpdateImmediate();

                    if (!vehicle.VehicleConfig.GeneralToastSettings.Enabled)
                    {
                        return;
                    }

                    if (curVal != state)
                    {
                        if (state)
                        {
                            Instance.ShowToast(player, vehicle.VehicleConfig.GeneralToastSettings.PowerSwitchOnToast);
                        }
                        else
                        {
                            Instance.ShowToast(player, vehicle.VehicleConfig.GeneralToastSettings.PowerSwitchOffToast, GameTip.Styles.Red_Normal);
                        }
                    }
                }
            }
        }

        public class LanternPowerSwitch : VehiclePowerSwitch
        {
            public override bool OnRpcMessage(BasePlayer player, uint rpc, Message msg)
            {
                using (TimeWarning.New(RPC_TIMEWARNING_BASEOVEN_RPCMESSAGE))
                {
                    if (rpc == 4167839872u && player != null)
                    {
                        Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                        using (TimeWarning.New(RPC_SWITCH))
                        {
                            using (TimeWarning.New(RPC_TIMEWARNING_CONDITIONS))
                            {
                                if (!RPC_Server.MaxDistance.Test(4167839872u, RPC_SVSWITCH, this, player, 3f))
                                {
                                    return true;
                                }
                            }

                            try
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                {
                                    RPCMessage rPCMessage = default(RPCMessage);
                                    rPCMessage.connection = msg.connection;
                                    rPCMessage.player = player;
                                    rPCMessage.read = msg.read;
                                    RPCMessage msg2 = rPCMessage;
                                    SVSwitch(msg2);
                                }
                            }
                            catch (Exception exception)
                            {
                                Debug.LogException(exception);
                                player.Kick("RPC Error in SVSwitch");
                            }
                        }

                        return true;
                    }
                }

                return base.OnRpcMessage(player, rpc, msg);
            }

            public void SVSwitch(RPCMessage msg)
            {
                bool switchOn = msg.read.Bit();
                SetSwitch(msg.player, switchOn);
            }
        }

        public class LanternSwitch : CustomSwitch
        {
            public override bool OnRpcMessage(BasePlayer player, uint rpc, Message msg)
            {
                using (TimeWarning.New(RPC_TIMEWARNING_BASEOVEN_RPCMESSAGE))
                {
                    if (rpc == 4167839872u && player != null)
                    {
                        Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                        using (TimeWarning.New(RPC_SWITCH))
                        {
                            using (TimeWarning.New(RPC_TIMEWARNING_CONDITIONS))
                            {
                                if (!RPC_Server.MaxDistance.Test(4167839872u, RPC_SVSWITCH, this, player, 3f))
                                {
                                    return true;
                                }
                            }

                            try
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                {
                                    RPCMessage rPCMessage = default(RPCMessage);
                                    rPCMessage.connection = msg.connection;
                                    rPCMessage.player = player;
                                    rPCMessage.read = msg.read;
                                    RPCMessage msg2 = rPCMessage;
                                    SVSwitch(msg2);
                                }
                            }
                            catch (Exception exception)
                            {
                                Debug.LogException(exception);
                                player.Kick("RPC Error in SVSwitch");
                            }
                        }

                        return true;
                    }
                }

                return base.OnRpcMessage(player, rpc, msg);
            }

            public void SVSwitch(RPCMessage msg)
            {
                bool switchOn = msg.read.Bit();
                SetSwitch(msg.player, switchOn);
            }
        }


        public class SpecialWheelSwitch : SpecialBaseCombatEntity
        {
            private float rotateProgress;
            private BasePlayer rotatorPlayer;
            private float progressTickRate = 0.1f;
            private Flags BeingRotated = Flags.Reserved1;
            private bool rotatingRight = true;

            public float MinRotation;
            public float MaxRotation;
            public Func<float, bool> OnRotation;
            public float RotateAmount = 0.1f;
            public bool RoundValue = true;

            public override void ServerInit()
            {
                base.ServerInit();

                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(BaseEntity.Flags.Busy, false);
                }
            }

            public override bool OnRpcMessage(BasePlayer player, uint rpc, Message msg)
            {
                using (TimeWarning.New("WheelSwitch.OnRpcMessage"))
                {
                    if (rpc == 2223603322u && player != null)
                    {
                        Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                        using (TimeWarning.New(RPC_TIMEWARNING_BEGIN_ROTATE))
                        {
                            using (TimeWarning.New(RPC_TIMEWARNING_CONDITIONS))
                            {
                                if (!RPC_Server.IsVisible.Test(2223603322u, RPC_TIMEWARNING_BEGIN_ROTATE, this, player, 3f))
                                {
                                    return true;
                                }
                            }

                            try
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                {
                                    RPCMessage rPCMessage = default(RPCMessage);
                                    rPCMessage.connection = msg.connection;
                                    rPCMessage.player = player;
                                    rPCMessage.read = msg.read;
                                    RPCMessage msg2 = rPCMessage;
                                    BeginRotate(msg2);
                                }
                            }
                            catch (Exception exception)
                            {
                                Debug.LogException(exception);
                                player.Kick("RPC Error in BeginRotate");
                            }
                        }

                        return true;
                    }

                    if (rpc == 434251040 && player != null)
                    {
                        Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                        using (TimeWarning.New(RPC_TIMEWARNING_CANCEL_ROTATE))
                        {
                            using (TimeWarning.New(RPC_TIMEWARNING_CONDITIONS))
                            {
                                if (!RPC_Server.IsVisible.Test(434251040u, RPC_TIMEWARNING_CANCEL_ROTATE, this, player, 3f))
                                {
                                    return true;
                                }
                            }

                            try
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                {
                                    RPCMessage rPCMessage = default(RPCMessage);
                                    rPCMessage.connection = msg.connection;
                                    rPCMessage.player = player;
                                    rPCMessage.read = msg.read;
                                    RPCMessage msg3 = rPCMessage;
                                    CancelRotate(msg3);
                                }
                            }
                            catch (Exception exception2)
                            {
                                Debug.LogException(exception2);
                                player.Kick("RPC Error in CancelRotate");
                            }
                        }

                        return true;
                    }
                }

                return base.OnRpcMessage(player, rpc, msg);
            }

            [RPC_Server]
            [RPC_Server.IsVisible(3f)]
            public void BeginRotate(RPCMessage msg)
            {
                if (!IsBeingRotated())
                {
                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(BeingRotated, true);
                    }

                    rotatorPlayer = msg.player;
                    InvokeRepeating(RotateProgress, 0f, progressTickRate);
                }
            }

            public void CancelPlayerRotation()
            {
                CancelInvoke(RotateProgress);

                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(BeingRotated, false);
                }

                rotatorPlayer = null;
            }

            public void RotateProgress()
            {
                if (!rotatorPlayer || rotatorPlayer.IsDead() || rotatorPlayer.IsSleeping() || Vector3Ex.Distance2D(rotatorPlayer.transform.position, CachedTransform.position) > 2f || !rotatorPlayer.serverInput.IsDown(BUTTON.USE))
                {
                    CancelPlayerRotation();
                    return;
                }

                rotatingRight = !rotatorPlayer.serverInput.IsDown(BUTTON.FIRE_PRIMARY);
                if (rotatingRight)
                {
                    SetRotateProgress(rotateProgress - RotateAmount);
                }
                else
                {
                    SetRotateProgress(rotateProgress + RotateAmount);
                }
            }

            public void SetForceRotateProgress(float newValue)
            {
                if (rotateProgress != newValue)
                {
                    rotateProgress = newValue;
                    SendNetworkUpdate();
                }
            }

            public void SetRotateProgress(float newValue)
            {
                if (newValue < MinRotation)
                {
                    newValue = MinRotation;
                }
                else if (newValue > MaxRotation)
                {
                    newValue = MaxRotation;
                }
                else if (RoundValue)
                {
                    newValue = Mathf.Round(newValue * 10.0f) * 0.1f;
                }

                if (rotateProgress != newValue)
                {
                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(Flags.Reserved4, true);
                    }

                    rotateProgress = newValue;
                    SendNetworkUpdate();
                    OnRotation?.Invoke(rotateProgress);

                    Invoke(StoppedRotatingCheck, 0.25f);
                }
            }

            public void StoppedRotatingCheck()
            {
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.Reserved4, false);
                }
            }

            [RPC_Server]
            [RPC_Server.IsVisible(3f)]
            public void CancelRotate(RPCMessage msg)
            {
                CancelPlayerRotation();
            }

            public bool IsBeingRotated()
            {
                return HasFlag(BeingRotated);
            }

            public override void Load(LoadInfo info)
            {
                base.Load(info);
                if (info.msg.sphereEntity != null)
                {
                    rotateProgress = info.msg.sphereEntity.radius;
                }
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);
                info.msg.sphereEntity = Facepunch.Pool.Get<ProtoBuf.SphereEntity>();
                info.msg.sphereEntity.radius = rotateProgress;
            }
        }


        #endregion

        #region Lights
        public class SpecialLight : SpecialIOEntity, ILightToggle
        {
            public bool CanVehicleToggle { get; set; } = true;
            public bool LimitNetworkinWhenOff;

            public override void ServerInit()
            {
                base.ServerInit();
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.Busy, true);
                }
            }

            public virtual void ToggleLights(bool lightsOn)
            {
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.Reserved8, lightsOn);
                    flagsUpdateScope.Set(Flags.On, lightsOn);
                }

                if (!LimitNetworkinWhenOff)
                {
                    return;
                }

                limitNetworking = !lightsOn;
            }
        }

        public class SpecialLightVehicle : SpecialLight
        {
            public override void ServerInit()
            {
                base.ServerInit();

                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.Reserved5, false);
                }
            }

            public override void ToggleLights(bool lightsOn)
            {
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.Reserved5, lightsOn);
                }

                if (!LimitNetworkinWhenOff)
                {
                    return;
                }

                limitNetworking = !lightsOn;
            }
        }

        public class SpecialLightSedan : SpecialLight
        {
            Flags lightFlag;

            public override void ServerInit()
            {
                var prefabName = StringPool.Get(prefabID);
                var isRailSedan = false;
                if (prefabName == SEDAN_RAIL_PREFAB)
                {
                    isRailSedan = true;
                }

                lightFlag = Flags.Reserved2;
                if (isRailSedan)
                {
                    lightFlag = Flags.Reserved5;
                }

                base.ServerInit();

                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(lightFlag, false);
                }
            }

            public override void ToggleLights(bool lightsOn)
            {
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(lightFlag, lightsOn);
                }

                if (!LimitNetworkinWhenOff)
                {
                    return;
                }

                limitNetworking = !lightsOn;
            }
        }

        #endregion

        #region Special Entities

        public class SpecialBaseCombatEntity : BaseCombatEntity, ICustomEntity
        {
            protected float ChangeTolerance = 0.0001f;

            public Transform CachedTransform;

            public CustomHandler Handler { get; set; }
            public List<BaseEntity> SaveListInDataFile { get; set; } = null;

            public virtual bool ShouldDoNormalVanillaBaseCombatEntityHurt => true;

            public override bool ShouldInheritNetworkGroup()
            {
                return false;
            }

            public virtual bool EnableSavingToDiskByDefault => false;

            public virtual bool HasDefaultInventory => false;
            public virtual bool DefaultInventoryHandledByBaseType => false;
            public virtual bool DefaultInventoryItemFilter(BasePlayer player, Item item, int targetSlot) => true;

            public virtual void OnDefaultInventoryDirty()
            {

            }

            public virtual void OnItemAddedOrRemoved(Item item, bool added)
            {

            }

            public ItemContainer DefaultInventory { get; set; } = null;

            public virtual int DefaultInventoryCapacity => 48;

            public virtual string DefaultClientsideFullPrefabName() => PREFAB_SPHERE; //change this to sth base combat related for testing yo.

            public IKaruzaCustomPrefab KaruzaEntity { get; set; }

            bool propagateDamageToParent = true;

            public bool DestroyParentOnDestroy = true;
            public bool HasKaruzaEntity;
            public bool UseGlobalNetworkPosition;

            bool firstBroadcastOccured;

            public bool ForceNetworkGroup;

            float nextIdleUpdateTime;
            Vector3 oldPos;
            Quaternion oldRot;

            public void SetConditional(bool isConditional)
            {
                KaruzaEntity.OnToggled += ToggleConditional;
                limitNetworking = true;
            }

            void ToggleConditional(bool state)
            {
                limitNetworking = !state;
            }

            public override void ResetState()
            {
                _prefab = this;
                base.ResetState();
                _prefab = null;
            }

            protected bool CustomHasChanged
            {
                get
                {
                    var changed = false;
                    var sqrMag = (oldPos - CachedTransform.position).sqrMagnitude;
                    if (sqrMag > ChangeTolerance)
                    {
                        changed = true;
                    }
                    else if (Quaternion.Angle(oldRot, CachedTransform.rotation) > 0.01f)
                    {
                        changed = true;
                    }

                    if (changed)
                    {
                        oldPos = CachedTransform.position;
                        oldRot = CachedTransform.rotation;
                    }

                    return changed;
                }
            }

            public override float AntiHackVelocity()
            {
                return 300f;
            }

            public virtual void CustomNetworkPositionTick()
            {
                var forceUpdate = false;
                if (!CachedTransform.hasChanged)
                {
                    // Force world items to send an update otherwise they will sometimes not render for the client
                    if (ticksSinceStopped > 6)
                    {
                        if (Time.time < nextIdleUpdateTime)
                        {
                            return;
                        }

                        forceUpdate = true;
                    }

                    ticksSinceStopped++;
                    nextIdleUpdateTime += 10;
                }
                else
                {
                    ticksSinceStopped = 0;
                }

                CustomTransformChanged(forceUpdate);
                CachedTransform.hasChanged = false;
            }

            public void CustomTransformChanged()
            {
                CustomTransformChanged(true);
            }

            public void CustomTransformChanged(bool forceUpdate = false)
            {
                if (!forceUpdate && !globalBroadcast && firstBroadcastOccured)
                {
                    return;
                }

                UpdateTransformForNetwork(ForceNetworkGroup);
                firstBroadcastOccured = true;
            }

            public void UpdateTransformForNetwork()
            {
                UpdateTransformForNetwork(ForceNetworkGroup);
            }

            public virtual void UpdateTransformForNetwork(bool forceNetworkGroup, float networkGroupUpdateTime = 5f)
            {
                var position = CachedTransform.position;
                Query.Server.Grid.Move(this, position.x, position.y);

                if (net == null)
                {
                    return;
                }

                InvalidateNetworkCache();

                if ((forceNetworkGroup || !firstBroadcastOccured) && !isCallingUpdateNetworkGroup)
                {
                    Invoke(UpdateNetworkGroup, networkGroupUpdateTime);
                    isCallingUpdateNetworkGroup = true;
                }

                if (ShouldUpdateNetworkPosition())
                {
                    CustomSendNetworkUpdate_Position(false, forceNetworkGroup);
                    OnPositionalNetworkUpdate();
                }
            }

            public override void UpdateNetworkGroup()
            {
                isCallingUpdateNetworkGroup = false;
                if (net == null || Network.Net.sv == null || Network.Net.sv.visibility == null)
                {
                    return;
                }

                using (TimeWarning.New(UPDATE_NETWORKGROUP))
                {
                    var group = BaseNetworkable.GlobalNetworkGroup;
                    if (HasKaruzaEntity)
                    {
                        if (KaruzaEntity.BaseEntity.net == null)
                        {
                            return;
                        }

                        group = KaruzaEntity.BaseEntity.net.group;
                    }

                    if (net.SwitchGroup(group))
                    {
                        if (isSpawned)
                        {
                            if (net.group == null)
                            {
                                Debug.LogWarning(ToString() + " changed its network group to null");
                                return;
                            }

                            NetWrite netWrite = Network.Net.sv.StartWrite();
                            netWrite.PacketID(Message.Type.GroupChange);
                            netWrite.EntityID(net.ID);
                            netWrite.GroupID(net.group.ID);
                            netWrite.Send(new SendInfo(net.group.subscribers));
                        }
                    }
                }
            }

            public override bool CanCompletePickup(BasePlayer player)
            {
                return false;
            }

            public override bool ShouldDisplayPickupOption(BasePlayer player)
            {
                return false;
            }

            public override bool CanBeLooted(BasePlayer player)
            {
                return false;
            }

            public override Vector3 GetNetworkPosition()
            {
                if (UseGlobalNetworkPosition)
                {
                    return CachedTransform.position;
                }

                return CachedTransform.localPosition;
            }

            public override Quaternion GetNetworkRotation()
            {
                return CachedTransform.localRotation;
            }

            public override bool ShouldNetworkTo(BasePlayer player)
            {
                if (IsTransferProtected())
                {
                    return false;
                }

                if (limitNetworking)
                {
                    return false;
                }

                if (HasKaruzaEntity)
                {
                    return KaruzaEntity.BaseEntity.ShouldNetworkTo(player);
                }

                if (net.group == null)
                {
                    return true;
                }

                return player.net.subscriber.IsSubscribed(net.group);
            }

            public void CustomSendNetworkUpdate_Position(bool immediate = false, bool global = false)
            {
                if (Rust.Application.isLoading || Rust.Application.isLoadingSave || IsDestroyed || net == null || !isSpawned)
                {
                    return;
                }

                using (TimeWarning.New(SENDNETWORKUPDATE_POSITION))
                {
                    List<Connection> subscribers = null;

                    if (global)
                    {
                        subscribers = BaseNetworkable.GetGlobalNetworkGroup(this.globalNetworkBehavior).subscribers;
                    }
                    else if (HasKaruzaEntity)
                    {
                        subscribers = KaruzaEntity.BaseEntity.GetSubscribers();
                    }
                    else
                    {
                        subscribers = GetSubscribers();
                    }

                    if (subscribers == null || subscribers.Count <= 0)
                    {
                        return;
                    }

                    NetWrite netWrite = Network.Net.sv.StartWrite();
                    netWrite.PacketID(Message.Type.EntityPosition);
                    netWrite.EntityID(net.ID);
                    Vector3 obj2 = GetNetworkPosition();
                    netWrite.Vector3(in obj2);
                    obj2 = GetNetworkRotation().eulerAngles;
                    netWrite.Vector3(in obj2);
                    netWrite.Float(GetNetworkTime());
                    NetworkableId uid = parentEntity.uid;
                    if (uid.IsValid)
                    {
                        netWrite.EntityID(uid);
                    }

                    SendInfo sendInfo = new SendInfo(subscribers);
                    sendInfo.method = SendMethod.ReliableUnordered;
                    sendInfo.priority = Priority.Immediate;
                    SendInfo info = sendInfo;
                    netWrite.Send(info);
                }
            }

            public override void ServerInit()
            {
                base.ServerInit();
                // Use custom network update
                ConfigurationNetworkPositionUpdates();

                if (!HasKaruzaEntity)
                {
                    this.KaruzaEntity = this.GetComponentInParent<IKaruzaCustomPrefab>();
                    if (object.ReferenceEquals(KaruzaEntity, null) && !object.ReferenceEquals(CachedTransform.parent, null))
                    {
                        this.KaruzaEntity = CachedTransform.parent.GetComponentInParent<IKaruzaCustomPrefab>();
                    }

                    HasKaruzaEntity = this.KaruzaEntity != null;
                }

                Handler?.ServerInit();

                serverEntities.RegisterID(this);
                if (net != null)
                {
                    net.handler = this;
                }

                // Busy on garage doors causes them to make noise
                // Workaround by locking instead of busy
                var prefabName = StringPool.Get(prefabID);
                if (prefabName == GARAGE_DOOR_PREFAB)
                {
                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(Flags.Locked, true);
                    }
                }
                else
                {
                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(Flags.Busy, true);
                    }
                }
            }

            protected void ConfigurationNetworkPositionUpdates()
            {
                // Use custom network update
                if (syncPosition && PositionTickRate >= 0f)
                {
                    CancelInvoke(NetworkPositionTick);

                    if (PositionTickFixedTime)
                    {
                        InvokeRepeatingFixedTime(CustomNetworkPositionTick);
                    }
                    else
                    {
                        InvokeRandomized(CustomNetworkPositionTick, PositionTickRate, PositionTickRate - PositionTickRate * 0.05f, PositionTickRate * 0.05f);
                    }
                }
                else
                {
                    //InvokeRepeating(CustomNetworkPositionTick, 30f, 30f);
                }

                UpdateTransformForNetwork(true, 0.0f);
            }

            public override void Hurt(HitInfo info)
            {
                if (propagateDamageToParent)
                {
                    this.KaruzaEntity.Hurt(info);
                    return;
                }

                base.Hurt(info);
            }

            public override void OnAttacked(HitInfo info)
            {
                using (TimeWarning.New(BASECOMBATENTITY_ONATTACKED))
                {
                    if (propagateDamageToParent)
                    {
                        this.KaruzaEntity.OnAttacked(info);
                        return;
                    }

                    base.OnAttacked(info);
                }
            }

            public override void DoRepair(BasePlayer player)
            {
                this.KaruzaEntity.DoRepair(player);
            }

            public static T CreateSpecialEntity<T>(Vector3 position, Vector3 rotation, Vector3 scale, string prefabName, bool detectCollisions = false, bool syncPosition = false) where T : SpecialBaseCombatEntity, new()
            {
                var newGO = GameManager.server.CreatePrefab(prefabName, position, Quaternion.Euler(rotation));
                var baseEntity = newGO.GetComponent<BaseEntity>();

                var maxHealth = 500f;
                var baseCombatEntity = newGO.GetComponent<BaseCombatEntity>();
                if (baseCombatEntity != null)
                {
                    maxHealth = baseCombatEntity.StartHealth();
                }

                DestroyImmediate(baseEntity);

                var triggers = newGO.GetComponentsInChildren<TriggerParent>();
                for (int i = 0; i < triggers.Length; i++)
                {
                    var trigger = triggers[i];
                    UnityEngine.Object.DestroyImmediate(trigger);
                }

                var iterateOver = newGO.GetComponentsInChildren<Component>(true);
                for (var i = 0; i < iterateOver.Length; i++)
                {
                    var compo = iterateOver[i];

                    if (compo is Rigidbody)
                    {
                        continue;
                    }

                    if (compo is Transform)
                    {
                        continue;
                    }

                    if (compo is TriggerLadder)
                    {
                        continue;
                    }

                    if (compo is CanvasRenderer)
                    {
                        continue;
                    }

                    if (detectCollisions && compo is Collider col && !col.isTrigger)
                    {
                        compo.gameObject.layer = (int)Layer.Default;
                        col.includeLayers = PROP_COL_MASK;
                        col.excludeLayers = IGNORE_COL_MASK;

                        if (col is MeshCollider mc)
                        {
                            mc.convex = true;
                        }

                        if (prefabName == HOT_AIR_BALOON_ARMOR_PREFAB && (col.name == HAB_HINGE_COLLIDER || col.name == HAB_DOOR_WORLD_COLLODER))
                        {
                            DestroyImmediate(compo);
                        }

                        continue;
                    }

                    DestroyImmediate(compo);
                }

                var rigidbody = newGO.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    DestroyImmediate(rigidbody);
                }

                var replacementEntity = newGO.AddComponent<T>();

                replacementEntity.prefabID = StringPool.Get(prefabName);
                replacementEntity.pickup.enabled = false;
                replacementEntity.baseProtection = ScriptableObject.CreateInstance<ProtectionProperties>();
                replacementEntity.bounds = baseEntity.bounds;
                replacementEntity.impactEffect = baseEntity.impactEffect;
                replacementEntity.gameObject.layer = (int)Layer.Default;
                replacementEntity.syncPosition = syncPosition;
                replacementEntity.canTriggerParent = false;

                replacementEntity._maxHealth = maxHealth;
                replacementEntity.startHealth = maxHealth;
                replacementEntity.health = maxHealth;
                replacementEntity.transform.localScale = scale;

                replacementEntity.sendsHitNotification = true;
                replacementEntity.EnableSaving(false);

                //if (replacementEntity is BuildingBlock buildingBlock)
                //{
                //    buildingBlock.SetGrade(BuildingGrade.Enum.Metal);
                //    buildingBlock.SetHealthToMax();
                //    var target = RpcTarget.SendInfo("RefreshSkin", new SendInfo()
                //    {
                //        priority = Priority.Immediate
                //    });

                //    buildingBlock.ClientRPC(target);
                //    buildingBlock.UpdateSkin();

                //    var stability = replacementEntity.GetComponent<StabilityEntity>();
                //    if (stability)
                //    {
                //        stability.grounded = true;
                //    }
                //}
                //else

                return replacementEntity;
            }

            public override void Save(SaveInfo info)
            {
                info.msg.baseNetworkable = Facepunch.Pool.Get<ProtoBuf.BaseNetworkable>();
                info.msg.baseNetworkable.uid = net.ID;
                info.msg.baseNetworkable.prefabID = prefabID;
                if (net.group != null)
                {
                    info.msg.baseNetworkable.group = net.group.ID;
                }

                if (!info.forDisk)
                {
                    info.msg.createdThisFrame = creationFrame == UnityEngine.Time.frameCount;
                }

                info.msg.baseEntity = Facepunch.Pool.Get<ProtoBuf.BaseEntity>();
                if (info.forDisk)
                {
                    info.msg.baseEntity.pos = CachedTransform.localPosition;
                    info.msg.baseEntity.rot = CachedTransform.localRotation.eulerAngles;
                }
                else
                {
                    info.msg.baseEntity.pos = GetNetworkPosition();
                    info.msg.baseEntity.rot = GetNetworkRotation().eulerAngles;
                    info.msg.baseEntity.time = GetNetworkTime();

                    if (CachedTransform.localScale != Vector3.one)
                    {
                        info.msg.baseEntity.scale = CachedTransform.localScale;
                    }
                }

                info.msg.baseEntity.flags = (int)flags;
                info.msg.baseEntity.skinid = skinID;

                info.msg.parent = Facepunch.Pool.Get<ParentInfo>();
                info.msg.parent.uid = parentEntity.uid;
                info.msg.parent.bone = parentBone;

                info.msg.baseCombat = Facepunch.Pool.Get<BaseCombat>();
                info.msg.baseCombat.state = (int)lifestate;

                info.msg.baseCombat.health = 100;
                info.msg.baseCombat.maxHealthOverride = 100;
                info.msg.baseCombat.maxHealth = 100;

                OnEntitySaveForNetwork(info);
            }

            public override void DestroyShared()
            {
                base.DestroyShared();

                if (DestroyParentOnDestroy && HasKaruzaEntity && !KaruzaEntity.BaseEntity.IsDestroyed)
                {
                    KaruzaEntity.BaseEntity.Kill();
                }
            }

            public void OnDefaultInventoryPreAnnihilation()
            {
            }

            public void OnDefaultInventoryFirstCreated()
            {
            }

            public void OnCustomPrefabPrototypeEntityRegistered()
            {
            }

            public void OnCustomPrefabPrototypeEntityUnregistered()
            {
            }

            public bool IsBaseCombat()
            {
                return true;
            }

            public void SaveExtra(Stream stream, BinaryWriter writer)
            {
            }

            public void LoadExtra(Stream stream, BinaryReader reader)
            {
            }

            public void PostLoadExtra(Stream stream, BinaryReader reader)
            {
            }

            public void OnEntitySaveForNetwork(SaveInfo info)
            {
            }

            public override void OnParentChanging(BaseEntity oldParent, BaseEntity newParent)
            {
                base.OnParentChanging(oldParent, newParent);

                Handler?.OnParentChanging(oldParent, newParent);
            }

            public override void PreServerLoad()
            {
                base.PreServerLoad();
                Handler?.PreServerLoad();
            }

            public virtual void Awake()
            {
                CachedTransform = this.transform;

                this.syncPosition = false;
                this.gameObject.layer = (int)Layer.Default;
                this.enabled = false;

                this.sendsHitNotification = true;
                this.sendsMeleeHitNotification = true;
                this.EnableSaving(false);

                CustomHandler.AttachNewHandlerToCustomEntityIfNotPrototype(this);
            }
        }

        public class SpecialDeployableBoomBox : DeployableBoomBox
        {
            public IKaruzaCustomEntity KaruzaCustomEntity { get; protected set; }
            public bool DestroyParentOnDestroy = true;
            public bool HasKaruzaEntity;

            public override void ServerInit()
            {
                networkEntityScale = true;

                canBeDemolished = false;
                base.ServerInit();
                BuildingManager.server.Remove(this);
                this.KaruzaCustomEntity = this.GetComponentInParent<IKaruzaCustomEntity>();
                HasKaruzaEntity = KaruzaCustomEntity != null;
            }

            public override bool OnRpcMessage(BasePlayer player, uint rpc, Message msg)
            {
                using (TimeWarning.New(RPC_BOOMBOX))
                {
                    if (rpc == 1918716764 && player != null)
                    {
                        Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                        using (TimeWarning.New(RPC_BOOMBOX_UPDATE_RADIO))
                        {
                            using (TimeWarning.New(RPC_TIMEWARNING_CONDITIONS))
                            {
                                if (!RPC_Server.CallsPerSecond.Test(1918716764u, RPC_BOOMBOX_UPDATE_RADIO, this, player, 2uL))
                                {
                                    return true;
                                }
                            }

                            try
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                {
                                    RPCMessage rPCMessage = default(RPCMessage);
                                    rPCMessage.connection = msg.connection;
                                    rPCMessage.player = player;
                                    rPCMessage.read = msg.read;
                                    RPCMessage msg2 = rPCMessage;
                                    Server_UpdateRadioIP(msg2);
                                }
                            }
                            catch (Exception exception)
                            {
                                Debug.LogException(exception);
                                player.Kick("RPC Error in Server_UpdateRadioIP");
                            }
                        }

                        return true;
                    }

                    if (rpc == 1785864031 && player != null)
                    {
                        Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                        using (TimeWarning.New(RPC_BOOMBOX_TOGGLEPLAY))
                        {
                            using (TimeWarning.New(RPC_TIMEWARNING_CONDITIONS))
                            {
                                if (!RPC_Server.CallsPerSecond.Test(1785864031u, RPC_BOOMBOX_TOGGLEPLAY, this, player, 2uL))
                                {
                                    return true;
                                }
                            }

                            try
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                {
                                    RPCMessage rPCMessage = default(RPCMessage);
                                    rPCMessage.connection = msg.connection;
                                    rPCMessage.player = player;
                                    rPCMessage.read = msg.read;
                                    RPCMessage msg3 = rPCMessage;
                                    ServerTogglePlay(msg3);
                                }
                            }
                            catch (Exception exception2)
                            {
                                Debug.LogException(exception2);
                                player.Kick("RPC Error in ServerTogglePlay");
                            }
                        }

                        return true;
                    }
                }

                return base.OnRpcMessage(player, rpc, msg);
            }

            public override bool CanCompletePickup(BasePlayer player)
            {
                return false;
            }

            public override bool ShouldDisplayPickupOption(BasePlayer player)
            {
                return false;
            }

            public override void Hurt(HitInfo info)
            {
                this.KaruzaCustomEntity.Hurt(info);
                return;
            }

            public override void OnAttacked(HitInfo info)
            {
                using (TimeWarning.New(BASECOMBATENTITY_ONATTACKED))
                {
                    this.KaruzaCustomEntity.OnAttacked(info);
                    return;
                }
            }

            public override void DoRepair(BasePlayer player)
            {
                this.KaruzaCustomEntity.DoRepair(player);
            }

            public override bool CanBeLooted(BasePlayer player)
            {
                return KaruzaCustomEntity.CanAccess(player);
            }

            public override void DestroyShared()
            {
                base.DestroyShared();

                if (DestroyParentOnDestroy && KaruzaCustomEntity != null && !KaruzaCustomEntity.BaseEntity.IsDestroyed)
                {
                    KaruzaCustomEntity.BaseEntity.Kill();
                }
            }

            public override void UpdateNetworkGroup()
            {
                isCallingUpdateNetworkGroup = false;
                if (net == null || Network.Net.sv == null || Network.Net.sv.visibility == null)
                {
                    return;
                }

                using (TimeWarning.New(UPDATE_NETWORKGROUP))
                {
                    var group = BaseNetworkable.GlobalNetworkGroup;
                    if (HasKaruzaEntity)
                    {
                        if (KaruzaCustomEntity.BaseEntity.IsDestroyed || KaruzaCustomEntity.BaseEntity.net == null)
                        {
                            return;
                        }

                        group = KaruzaCustomEntity.BaseEntity.net.group;
                    }

                    if (net.SwitchGroup(group))
                    {
                        if (isSpawned)
                        {
                            if (net.group == null)
                            {
                                Debug.LogWarning(ToString() + " changed its network group to null");
                                return;
                            }

                            NetWrite netWrite = Network.Net.sv.StartWrite();
                            netWrite.PacketID(Message.Type.GroupChange);
                            netWrite.EntityID(net.ID);
                            netWrite.GroupID(net.group.ID);
                            netWrite.Send(new SendInfo(net.group.subscribers));
                        }
                    }
                }
            }

            public override bool ShouldNetworkTo(BasePlayer player)
            {
                if (IsTransferProtected())
                {
                    return false;
                }

                if (limitNetworking)
                {
                    return false;
                }

                if (HasKaruzaEntity)
                {
                    return KaruzaCustomEntity.BaseEntity.ShouldNetworkTo(player);
                }

                if (net.group == null)
                {
                    return true;
                }

                return player.net.subscriber.IsSubscribed(net.group);
            }
        }

        public class SpecialBuildingBlock : SpecialBaseCombatEntity
        {
            public float wallpaperHealth = -1f;
            public float wallpaperRotation = 0;
            public float wallpaperHealth2 = -1f;
            public float wallpaperRotation2 = 0;

            public ulong skinID2 { get; set; }
            public ulong modelState { get; set; } = 0;

            public string ForceColPrefab;
            public bool IgnorePlayerCollisons;
            public bool DetectCollisions;

            public override void ServerInit()
            {
                base.ServerInit();

                if (DetectCollisions)
                {
                    if (string.IsNullOrEmpty(ForceColPrefab))
                    {
                        var blockDefinition = PrefabAttribute.server.Find<Construction>(prefabID);
                        ConstructionGrade defaultGrade = blockDefinition.defaultGrade;
                        ForceColPrefab = defaultGrade.skinObject.resourcePath;
                    }

                    GameObject colGO = GameManager.server.CreatePrefab(ForceColPrefab, this.transform);
                    var currentSkin = colGO.GetComponent<ConstructionSkin>();
                    Model component = currentSkin.GetComponent<Model>();

                    var triggers = colGO.GetComponentsInChildren<TriggerParent>(true);
                    for (int i = 0; i < triggers.Length; i++)
                    {
                        var trigger = triggers[i];
                        UnityEngine.Object.DestroyImmediate(trigger);
                    }

                    var iterateOver = colGO.GetComponentsInChildren<Collider>(true);

                    for (var i = 0; i < iterateOver.Length; i++)
                    {
                        var col = iterateOver[i];
                        if (!col.isTrigger)
                        {
                            col.gameObject.layer = (int)Layer.Default;
                            col.includeLayers = PROP_COL_MASK;

                            if (IgnorePlayerCollisons)
                            {
                                col.excludeLayers = BB_IGNORE_COL_MASK;
                            }
                            else
                            {
                                col.excludeLayers = IGNORE_COL_MASK;
                            }

                            continue;
                        }

                        UnityEngine.Object.DestroyImmediate(col);
                    }

                    SetModel(component);
                }
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);

                info.msg.buildingBlock = Facepunch.Pool.Get<ProtoBuf.BuildingBlock>();
                info.msg.buildingBlock.grade = 1;
                info.msg.buildingBlock.model = modelState;
                info.msg.buildingBlock.wallpaperID = skinID;
                info.msg.buildingBlock.wallpaperID2 = skinID2;
                info.msg.buildingBlock.wallpaperHealth = wallpaperHealth;
                info.msg.buildingBlock.wallpaperHealth2 = wallpaperHealth2;
                info.msg.buildingBlock.wallpaperRotation = wallpaperRotation;
                info.msg.buildingBlock.wallpaperRotation2 = wallpaperRotation2;
            }
        }

        public class SpecialSail : Sail
        {
            public IKaruzaCustomEntity KaruzaCustomEntity { get; protected set; }
            public bool DestroyParentOnDestroy = true;
            public bool HasKaruzaEntity;
            public bool IsOnSwitch;

            TimeCachedValue<bool> isOutside;

            public void Raise()
            {
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(BaseEntity.Flags.Busy, true);
                    flagsUpdateScope.Set(BaseEntity.Flags.Reserved13, true);
                }

                CustomWaitForRaise();
            }

            public void CustomWaitForRaise()
            {
                CancelInvoke(CustomOnFullyRaised);
                timeUntilLoweredRaised = RaiseDuration;
                Invoke(CustomOnFullyRaised, RaiseDuration);
            }

            public void CustomOnFullyRaised()
            {
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(BaseEntity.Flags.Busy, false);
                    flagsUpdateScope.Set(BaseEntity.Flags.Reserved13, false);
                    flagsUpdateScope.Set(BaseEntity.Flags.Reserved12, false);
                    flagsUpdateScope.Set(BaseEntity.Flags.Reserved3, false);
                }

                ToggleColliders();
            }

            public void Lower()
            {
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(BaseEntity.Flags.Busy, true);
                    flagsUpdateScope.Set(BaseEntity.Flags.Reserved12, true);
                }

                CustomWaitForLower();
            }

            public void CustomWaitForLower()
            {
                CancelInvoke(CustomOnFullyLowered);
                timeUntilLoweredRaised = LowerDuration;
                Invoke(CustomOnFullyLowered, LowerDuration);
            }

            public void CustomOnFullyLowered()
            {
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(BaseEntity.Flags.Busy, false);
                    flagsUpdateScope.Set(BaseEntity.Flags.Reserved12, false);
                    flagsUpdateScope.Set(BaseEntity.Flags.Reserved13, false);
                    flagsUpdateScope.Set(BaseEntity.Flags.Reserved3, true);
                }

                ToggleColliders();
            }
            public override void ServerInit()
            {
                networkEntityScale = true;

                canBeDemolished = false;
                base.ServerInit();
                BuildingManager.server.Remove(this);
                this.KaruzaCustomEntity = this.GetComponentInParent<IKaruzaCustomEntity>();
                HasKaruzaEntity = KaruzaCustomEntity != null;
            }

            public override void InitShared()
            {
                base.InitShared();
                CancelInvoke(CacheIsWindBlocked);
                InvokeRandomized(CustomCacheIsWindBlocked, 0f, 5f, 2f);

                isOutside = new TimeCachedValue<bool>
                {
                    refreshCooldown = 5,
                    refreshRandomRange = 5f,
                    updateValue = IsOutside
                };

                isOutside.Get(false);
            }

            public void CustomCacheIsWindBlocked()
            {
                if (Lowered)
                {
                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(BaseEntity.Flags.Reserved14, isOutside.Get(false));
                    }
                }
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);

                info.msg.baseCombat.health = 100;
                info.msg.baseCombat.maxHealthOverride = 100;
                info.msg.baseCombat.maxHealth = 100;
            }

            #region KaruzaEntity

            public override bool ShouldInheritNetworkGroup()
            {
                return false;
            }

            public override bool CanCompletePickup(BasePlayer player)
            {
                return false;
            }

            public override bool ShouldDisplayPickupOption(BasePlayer player)
            {
                return false;
            }

            public override void Hurt(HitInfo info)
            {
                this.KaruzaCustomEntity.Hurt(info);
                return;
            }

            public override void OnAttacked(HitInfo info)
            {
                using (TimeWarning.New(BASECOMBATENTITY_ONATTACKED))
                {
                    this.KaruzaCustomEntity.OnAttacked(info);
                    return;
                }
            }

            public override void DoRepair(BasePlayer player)
            {
                this.KaruzaCustomEntity.DoRepair(player);
            }

            public override bool CanBeLooted(BasePlayer player)
            {
                return true;
            }

            public override void DestroyShared()
            {
                base.DestroyShared();

                if (DestroyParentOnDestroy && KaruzaCustomEntity != null && !KaruzaCustomEntity.BaseEntity.IsDestroyed)
                {
                    KaruzaCustomEntity.BaseEntity.Kill();
                }
            }

            public override void UpdateNetworkGroup()
            {
                isCallingUpdateNetworkGroup = false;
                if (net == null || Network.Net.sv == null || Network.Net.sv.visibility == null)
                {
                    return;
                }

                using (TimeWarning.New(UPDATE_NETWORKGROUP))
                {
                    var group = BaseNetworkable.GlobalNetworkGroup;
                    if (HasKaruzaEntity)
                    {
                        if (KaruzaCustomEntity.BaseEntity.IsDestroyed || KaruzaCustomEntity.BaseEntity.net == null)
                        {
                            return;
                        }

                        group = KaruzaCustomEntity.BaseEntity.net.group;
                    }

                    if (net.SwitchGroup(group))
                    {
                        if (isSpawned)
                        {
                            if (net.group == null)
                            {
                                Debug.LogWarning(ToString() + " changed its network group to null");
                                return;
                            }

                            NetWrite netWrite = Network.Net.sv.StartWrite();
                            netWrite.PacketID(Message.Type.GroupChange);
                            netWrite.EntityID(net.ID);
                            netWrite.GroupID(net.group.ID);
                            netWrite.Send(new SendInfo(net.group.subscribers));
                        }
                    }
                }
            }

            public override bool ShouldNetworkTo(BasePlayer player)
            {
                if (IsTransferProtected())
                {
                    return false;
                }

                if (limitNetworking)
                {
                    return false;
                }

                if (HasKaruzaEntity)
                {
                    return KaruzaCustomEntity.BaseEntity.ShouldNetworkTo(player);
                }

                if (net.group == null)
                {
                    return true;
                }

                return player.net.subscriber.IsSubscribed(net.group);
            }

            #endregion
        }

        public class SpecialAnchor : Anchor
        {
            public IKaruzaCustomEntity KaruzaCustomEntity { get; protected set; }
            public bool DestroyParentOnDestroy = true;
            public bool HasKaruzaEntity;

            public void Lower()
            {
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.Busy, true);
                    flagsUpdateScope.Set(Flags.Reserved12, true);
                }

                WaitForLower(false);
            }

            public void Raise()
            {
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.Busy, true);
                    flagsUpdateScope.Set(Flags.Reserved13, true);
                }

                WaitForRaise();
            }

            public override void ServerInit()
            {
                networkEntityScale = true;

                canBeDemolished = false;
                base.ServerInit();
                BuildingManager.server.Remove(this);
                this.KaruzaCustomEntity = this.GetComponentInParent<IKaruzaCustomEntity>();
                HasKaruzaEntity = KaruzaCustomEntity != null;
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);

                info.msg.baseCombat.health = 100;
                info.msg.baseCombat.maxHealthOverride = 100;
                info.msg.baseCombat.maxHealth = 100;
            }

            #region KaruzaEntity

            public override bool ShouldInheritNetworkGroup()
            {
                return false;
            }

            public override bool CanCompletePickup(BasePlayer player)
            {
                return false;
            }

            public override bool ShouldDisplayPickupOption(BasePlayer player)
            {
                return false;
            }

            public override void Hurt(HitInfo info)
            {
                this.KaruzaCustomEntity.Hurt(info);
                return;
            }

            public override void OnAttacked(HitInfo info)
            {
                using (TimeWarning.New(BASECOMBATENTITY_ONATTACKED))
                {
                    this.KaruzaCustomEntity.OnAttacked(info);
                    return;
                }
            }

            public override void DoRepair(BasePlayer player)
            {
                this.KaruzaCustomEntity.DoRepair(player);
            }

            public override bool CanBeLooted(BasePlayer player)
            {
                return true;
            }

            public override void DestroyShared()
            {
                base.DestroyShared();

                if (DestroyParentOnDestroy && KaruzaCustomEntity != null && !KaruzaCustomEntity.BaseEntity.IsDestroyed)
                {
                    KaruzaCustomEntity.BaseEntity.Kill();
                }
            }

            public override void UpdateNetworkGroup()
            {
                isCallingUpdateNetworkGroup = false;
                if (net == null || Network.Net.sv == null || Network.Net.sv.visibility == null)
                {
                    return;
                }

                using (TimeWarning.New(UPDATE_NETWORKGROUP))
                {
                    var group = BaseNetworkable.GlobalNetworkGroup;
                    if (HasKaruzaEntity)
                    {
                        if (KaruzaCustomEntity.BaseEntity.IsDestroyed || KaruzaCustomEntity.BaseEntity.net == null)
                        {
                            return;
                        }

                        group = KaruzaCustomEntity.BaseEntity.net.group;
                    }

                    if (net.SwitchGroup(group))
                    {
                        if (isSpawned)
                        {
                            if (net.group == null)
                            {
                                Debug.LogWarning(ToString() + " changed its network group to null");
                                return;
                            }

                            NetWrite netWrite = Network.Net.sv.StartWrite();
                            netWrite.PacketID(Message.Type.GroupChange);
                            netWrite.EntityID(net.ID);
                            netWrite.GroupID(net.group.ID);
                            netWrite.Send(new SendInfo(net.group.subscribers));
                        }
                    }
                }
            }

            public override bool ShouldNetworkTo(BasePlayer player)
            {
                if (IsTransferProtected())
                {
                    return false;
                }

                if (limitNetworking)
                {
                    return false;
                }

                if (HasKaruzaEntity)
                {
                    return KaruzaCustomEntity.BaseEntity.ShouldNetworkTo(player);
                }

                if (net.group == null)
                {
                    return true;
                }

                return player.net.subscriber.IsSubscribed(net.group);
            }

            #endregion
        }

        public class SpecialMountedWeaponSeat : MountedWeaponSeat
        {
            public IKaruzaCustomEntity KaruzaCustomEntity { get; protected set; }

            public bool DestroyParentOnDestroy = true;
            public bool HasKaruzaEntity;
            protected bool HasVehicle;

            public bool ForceDriver = true;
            public bool CanSwapTo = true;

            BaseVehicle vehicle;

            public override bool DirectlyMountable()
            {
                return true;
            }

            public override float AntiHackVelocity()
            {
                return 50f;
            }

            void Awake()
            {
                this.isMobile = true;
            }

            public override void ServerInit()
            {
                networkEntityScale = true;
                this.isMobile = true;
                ignoreVehicleParent = true;

                this.mountSyncType = MountSyncType.RepositionPerFrame;

                base.ServerInit();

                this.KaruzaCustomEntity = this.GetComponentInParent<IKaruzaCustomEntity>();
                HasKaruzaEntity = KaruzaCustomEntity != null;
                if (this.KaruzaCustomEntity.BaseEntity is BaseVehicle vehicle)
                {
                    this.vehicle = vehicle;
                    HasVehicle = true;
                }

                if (HasVehicle)
                {
                    List<Transform> newDismountPoints = new List<Transform>();
                    for (int i = 0; i < this.vehicle.dismountPositions.Length; i++)
                    {
                        newDismountPoints.Add(this.vehicle.dismountPositions[i]);
                    }

                    newDismountPoints.AddRange(dismountPositions
                        .Where(gdmp => !newDismountPoints.Exists(dmp => dmp.transform.position == gdmp.transform.position)));

                    var newDm = newDismountPoints.ToArray();
                    this.vehicle.dismountPositions = newDm;
                }
            }

            public override BaseVehicle VehicleParent()
            {
                if (!HasVehicle || ignoreVehicleParent)
                {
                    return null;
                }

                return this.vehicle;
            }

            public override bool CanSwapToThis(BasePlayer player)
            {
                if (!CanSwapTo)
                {
                    return false;
                }

                return true;
            }

            public override void OnPlayerDismounted(BasePlayer player)
            {
                player.PauseFlyHackDetection(1.5f);
                base.OnPlayerDismounted(player);

                if (giveCrosshair)
                {
                    CrosshairUtilities.HideGUI(player);
                }
            }

            public override void AttemptMount(BasePlayer player, bool doMountChecks = true)
            {
                if (!player.IsNpc && !player.IsBot && ForceDriver)
                {
                    this.vehicle.AttemptMount(player, false);
                }
                else
                {
                    var vehicle = this.vehicle;
                    if (vehicle._mounted != null || !vehicle.MountEligable(player))
                    {
                        return;
                    }

                    if (_mounted != null || IsDead() || !player.CanMountMountablesNow() || IsTransferring() || IsSeatClipping(this) || ClothingBlocksMounting(player))
                    {
                        return;
                    }

                    if (doMountChecks)
                    {
                        if (checkPlayerLosOnMount && UnityEngine.Physics.Linecast(player.eyes.position, mountAnchor.position + base.transform.up * mountLOSVertOffset, out var hitInfo, 1218652417))
                        {
                            bool flag = false;
                            BaseEntity entity = hitInfo.GetEntity();
                            if (entity != null && (entity == this || entity == VehicleParent()))
                            {
                                flag = true;
                            }

                            if (!flag)
                            {
                                Debug.Log("not self, " + entity.name + " vs " + VehicleParent()?.name);
                                Debug.Log(entity.isClient + " vs " + VehicleParent()?.isClient);
                                return;
                            }
                        }

                        if (!HasValidDismountPosition(player))
                        {
                            Debug.Log("no valid dismount");
                            return;
                        }
                    }

                    MountPlayer(player);
                    if (player.GetMountedVehicle() == vehicle)
                    {
                        vehicle.PlayerMounted(player, this);
                    }
                }
            }

            public override void OnPlayerMounted()
            {
                base.OnPlayerMounted();
                if (_mounted.GetMountedVehicle() == this.vehicle)
                {
                    this.vehicle.PlayerMounted(_mounted, this);
                }

                if (giveCrosshair)
                {
                    CrosshairUtilities.ShowGUI(_mounted);
                }
            }

            public override bool HasValidDismountPosition(BasePlayer player)
            {
                return this.vehicle.HasValidDismountPosition(player);
            }

            #region KaruzaEntity

            public override bool ShouldInheritNetworkGroup()
            {
                return false;
            }

            public override bool CanCompletePickup(BasePlayer player)
            {
                return false;
            }

            public override bool ShouldDisplayPickupOption(BasePlayer player)
            {
                return false;
            }

            public override void Hurt(HitInfo info)
            {
                this.KaruzaCustomEntity.Hurt(info);
                return;
            }

            public override void OnAttacked(HitInfo info)
            {
                using (TimeWarning.New(BASECOMBATENTITY_ONATTACKED))
                {
                    this.KaruzaCustomEntity.OnAttacked(info);
                    return;
                }
            }

            public override void DoRepair(BasePlayer player)
            {
                this.KaruzaCustomEntity.DoRepair(player);
            }

            public override bool CanBeLooted(BasePlayer player)
            {
                return true;
            }

            public override void DestroyShared()
            {
                base.DestroyShared();

                if (DestroyParentOnDestroy && KaruzaCustomEntity != null && !KaruzaCustomEntity.BaseEntity.IsDestroyed)
                {
                    KaruzaCustomEntity.BaseEntity.Kill();
                }
            }

            public override void UpdateNetworkGroup()
            {
                isCallingUpdateNetworkGroup = false;
                if (net == null || Network.Net.sv == null || Network.Net.sv.visibility == null)
                {
                    return;
                }

                using (TimeWarning.New(UPDATE_NETWORKGROUP))
                {
                    var group = BaseNetworkable.GlobalNetworkGroup;
                    if (HasKaruzaEntity)
                    {
                        if (KaruzaCustomEntity.BaseEntity.IsDestroyed || KaruzaCustomEntity.BaseEntity.net == null)
                        {
                            return;
                        }

                        group = KaruzaCustomEntity.BaseEntity.net.group;
                    }

                    if (net.SwitchGroup(group))
                    {
                        if (isSpawned)
                        {
                            if (net.group == null)
                            {
                                Debug.LogWarning(ToString() + " changed its network group to null");
                                return;
                            }

                            NetWrite netWrite = Network.Net.sv.StartWrite();
                            netWrite.PacketID(Message.Type.GroupChange);
                            netWrite.EntityID(net.ID);
                            netWrite.GroupID(net.group.ID);
                            netWrite.Send(new SendInfo(net.group.subscribers));
                        }
                    }
                }
            }

            public override bool ShouldNetworkTo(BasePlayer player)
            {
                if (IsTransferProtected())
                {
                    return false;
                }

                if (limitNetworking)
                {
                    return false;
                }

                if (HasKaruzaEntity)
                {
                    return KaruzaCustomEntity.BaseEntity.ShouldNetworkTo(player);
                }

                if (net.group == null)
                {
                    return true;
                }

                return player.net.subscriber.IsSubscribed(net.group);
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);

                info.msg.baseCombat.health = 100;
                info.msg.baseCombat.maxHealthOverride = 100;
                info.msg.baseCombat.maxHealth = 100;
            }

            #endregion
        }

        public class SpecialCannon : Cannon
        {
            public IKaruzaCustomEntity KaruzaCustomEntity { get; protected set; }

            public bool DestroyParentOnDestroy = true;
            public bool HasKaruzaEntity;
            protected bool HasVehicle;

            public bool ForceDriver = true;
            public bool CanSwapTo = true;

            BaseVehicle vehicle;

            public override bool DirectlyMountable()
            {
                return true;
            }

            public override float AntiHackVelocity()
            {
                return 50f;
            }

            void Awake()
            {
                this.isMobile = true;
            }

            public override void ServerInit()
            {
                networkEntityScale = true;
                this.isMobile = true;
                ignoreVehicleParent = true;

                this.mountSyncType = MountSyncType.RepositionPerFrame;

                base.ServerInit();

                this.KaruzaCustomEntity = this.GetComponentInParent<IKaruzaCustomEntity>();
                HasKaruzaEntity = KaruzaCustomEntity != null;
                if (this.KaruzaCustomEntity.BaseEntity is BaseVehicle vehicle)
                {
                    this.vehicle = vehicle;
                    HasVehicle = true;
                }

                if (HasVehicle)
                {
                    List<Transform> newDismountPoints = new List<Transform>();
                    for (int i = 0; i < this.vehicle.dismountPositions.Length; i++)
                    {
                        newDismountPoints.Add(this.vehicle.dismountPositions[i]);
                    }

                    newDismountPoints.AddRange(dismountPositions
                        .Where(gdmp => !newDismountPoints.Exists(dmp => dmp.transform.position == gdmp.transform.position)));

                    var newDm = newDismountPoints.ToArray();
                    this.vehicle.dismountPositions = newDm;
                }
            }

            public override BaseVehicle VehicleParent()
            {
                if (!HasVehicle || ignoreVehicleParent)
                {
                    return null;
                }

                return this.vehicle;
            }

            public override bool CanSwapToThis(BasePlayer player)
            {
                if (!CanSwapTo)
                {
                    return false;
                }

                return true;
            }

            public override void OnPlayerDismounted(BasePlayer player)
            {
                player.PauseFlyHackDetection(1.5f);
                base.OnPlayerDismounted(player);

                if (giveCrosshair)
                {
                    CrosshairUtilities.HideGUI(player);
                }
            }

            public override void AttemptMount(BasePlayer player, bool doMountChecks = true)
            {
                if (!player.IsNpc && !player.IsBot && ForceDriver)
                {
                    this.vehicle.AttemptMount(player, false);
                }
                else
                {
                    var vehicle = this.vehicle;
                    if (vehicle._mounted != null || !vehicle.MountEligable(player))
                    {
                        return;
                    }

                    if (_mounted != null || IsDead() || !player.CanMountMountablesNow() || IsTransferring() || IsSeatClipping(this) || ClothingBlocksMounting(player))
                    {
                        return;
                    }

                    if (doMountChecks)
                    {
                        if (checkPlayerLosOnMount && UnityEngine.Physics.Linecast(player.eyes.position, mountAnchor.position + base.transform.up * mountLOSVertOffset, out var hitInfo, 1218652417))
                        {
                            bool flag = false;
                            BaseEntity entity = hitInfo.GetEntity();
                            if (entity != null && (entity == this || entity == VehicleParent()))
                            {
                                flag = true;
                            }

                            if (!flag)
                            {
                                Debug.Log("not self, " + entity.name + " vs " + VehicleParent()?.name);
                                Debug.Log(entity.isClient + " vs " + VehicleParent()?.isClient);
                                return;
                            }
                        }

                        if (!HasValidDismountPosition(player))
                        {
                            Debug.Log("no valid dismount");
                            return;
                        }
                    }

                    MountPlayer(player);
                    if (player.GetMountedVehicle() == vehicle)
                    {
                        vehicle.PlayerMounted(player, this);
                    }
                }
            }

            public override void OnPlayerMounted()
            {
                base.OnPlayerMounted();
                if (_mounted.GetMountedVehicle() == this.vehicle)
                {
                    this.vehicle.PlayerMounted(_mounted, this);
                }

                if (giveCrosshair)
                {
                    CrosshairUtilities.ShowGUI(_mounted);
                }
            }

            public override bool HasValidDismountPosition(BasePlayer player)
            {
                return this.vehicle.HasValidDismountPosition(player);
            }

            #region KaruzaEntity

            public override bool ShouldInheritNetworkGroup()
            {
                return false;
            }

            public override bool CanCompletePickup(BasePlayer player)
            {
                return false;
            }

            public override bool ShouldDisplayPickupOption(BasePlayer player)
            {
                return false;
            }

            public override void Hurt(HitInfo info)
            {
                this.KaruzaCustomEntity.Hurt(info);
                return;
            }

            public override void OnAttacked(HitInfo info)
            {
                using (TimeWarning.New(BASECOMBATENTITY_ONATTACKED))
                {
                    this.KaruzaCustomEntity.OnAttacked(info);
                    return;
                }
            }

            public override void DoRepair(BasePlayer player)
            {
                this.KaruzaCustomEntity.DoRepair(player);
            }

            public override bool CanBeLooted(BasePlayer player)
            {
                return true;
            }

            public override void DestroyShared()
            {
                base.DestroyShared();

                if (DestroyParentOnDestroy && KaruzaCustomEntity != null && !KaruzaCustomEntity.BaseEntity.IsDestroyed)
                {
                    KaruzaCustomEntity.BaseEntity.Kill();
                }
            }

            public override void UpdateNetworkGroup()
            {
                isCallingUpdateNetworkGroup = false;
                if (net == null || Network.Net.sv == null || Network.Net.sv.visibility == null)
                {
                    return;
                }

                using (TimeWarning.New(UPDATE_NETWORKGROUP))
                {
                    var group = BaseNetworkable.GlobalNetworkGroup;
                    if (HasKaruzaEntity)
                    {
                        if (KaruzaCustomEntity.BaseEntity.IsDestroyed || KaruzaCustomEntity.BaseEntity.net == null)
                        {
                            return;
                        }

                        group = KaruzaCustomEntity.BaseEntity.net.group;
                    }

                    if (net.SwitchGroup(group))
                    {
                        if (isSpawned)
                        {
                            if (net.group == null)
                            {
                                Debug.LogWarning(ToString() + " changed its network group to null");
                                return;
                            }

                            NetWrite netWrite = Network.Net.sv.StartWrite();
                            netWrite.PacketID(Message.Type.GroupChange);
                            netWrite.EntityID(net.ID);
                            netWrite.GroupID(net.group.ID);
                            netWrite.Send(new SendInfo(net.group.subscribers));
                        }
                    }
                }
            }

            public override bool ShouldNetworkTo(BasePlayer player)
            {
                if (IsTransferProtected())
                {
                    return false;
                }

                if (limitNetworking)
                {
                    return false;
                }

                if (HasKaruzaEntity)
                {
                    return KaruzaCustomEntity.BaseEntity.ShouldNetworkTo(player);
                }

                if (net.group == null)
                {
                    return true;
                }

                return player.net.subscriber.IsSubscribed(net.group);
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);

                info.msg.baseCombat.health = 100;
                info.msg.baseCombat.maxHealthOverride = 100;
                info.msg.baseCombat.maxHealth = 100;
            }

            #endregion

        }

        public class SpecialLadder : BaseLadder
        {
            public IKaruzaCustomEntity KaruzaCustomEntity { get; protected set; }

            public bool DestroyParentOnDestroy = true;
            public bool HasKaruzaEntity;

            public override void ServerInit()
            {
                networkEntityScale = true;

                base.ServerInit();

                this.KaruzaCustomEntity = this.GetComponentInParent<IKaruzaCustomEntity>();
                HasKaruzaEntity = KaruzaCustomEntity != null;
            }

            #region KaruzaEntity

            public override bool ShouldInheritNetworkGroup()
            {
                return false;
            }

            public override bool CanCompletePickup(BasePlayer player)
            {
                return false;
            }

            public override bool ShouldDisplayPickupOption(BasePlayer player)
            {
                return false;
            }

            public override void Hurt(HitInfo info)
            {
                this.KaruzaCustomEntity.Hurt(info);
                return;
            }

            public override void OnAttacked(HitInfo info)
            {
                using (TimeWarning.New(BASECOMBATENTITY_ONATTACKED))
                {
                    this.KaruzaCustomEntity.OnAttacked(info);
                    return;
                }
            }

            public override void DoRepair(BasePlayer player)
            {
                this.KaruzaCustomEntity.DoRepair(player);
            }

            public override bool CanBeLooted(BasePlayer player)
            {
                return true;
            }

            public override void DestroyShared()
            {
                base.DestroyShared();

                if (DestroyParentOnDestroy && KaruzaCustomEntity != null && !KaruzaCustomEntity.BaseEntity.IsDestroyed)
                {
                    KaruzaCustomEntity.BaseEntity.Kill();
                }
            }

            public override void UpdateNetworkGroup()
            {
                isCallingUpdateNetworkGroup = false;
                if (net == null || Network.Net.sv == null || Network.Net.sv.visibility == null)
                {
                    return;
                }

                using (TimeWarning.New(UPDATE_NETWORKGROUP))
                {
                    var group = BaseNetworkable.GlobalNetworkGroup;
                    if (HasKaruzaEntity)
                    {
                        if (KaruzaCustomEntity.BaseEntity.IsDestroyed || KaruzaCustomEntity.BaseEntity.net == null)
                        {
                            return;
                        }

                        group = KaruzaCustomEntity.BaseEntity.net.group;
                    }

                    if (net.SwitchGroup(group))
                    {
                        if (isSpawned)
                        {
                            if (net.group == null)
                            {
                                Debug.LogWarning(ToString() + " changed its network group to null");
                                return;
                            }

                            NetWrite netWrite = Network.Net.sv.StartWrite();
                            netWrite.PacketID(Message.Type.GroupChange);
                            netWrite.EntityID(net.ID);
                            netWrite.GroupID(net.group.ID);
                            netWrite.Send(new SendInfo(net.group.subscribers));
                        }
                    }
                }
            }

            public override bool ShouldNetworkTo(BasePlayer player)
            {
                if (IsTransferProtected())
                {
                    return false;
                }

                if (limitNetworking)
                {
                    return false;
                }

                if (HasKaruzaEntity)
                {
                    return KaruzaCustomEntity.BaseEntity.ShouldNetworkTo(player);
                }

                if (net.group == null)
                {
                    return true;
                }

                return player.net.subscriber.IsSubscribed(net.group);
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);

                info.msg.baseCombat.health = 100;
                info.msg.baseCombat.maxHealthOverride = 100;
                info.msg.baseCombat.maxHealth = 100;
            }

            #endregion
        }

        public class VehicleMount : SpecialBaseMountable
        {
            public override bool DirectlyMountable()
            {
                return false;
            }

            protected virtual void Awake()
            {
                legacyDismount = true;
            }

            public override bool CanSwapToThis(BasePlayer player)
            {
                return false;
            }

            public override void AttemptMount(BasePlayer player, bool doMountChecks = true)
            {
                if (ForceDriver)
                {
                    this.Vehicle.AttemptMount(player, false);
                }
                else
                {
                    var vehicle = this.Vehicle.BaseVehicle;
                    if (!this.Vehicle.CanAccess(player))
                    {
                        return;
                    }

                    if (vehicle._mounted != null || !vehicle.MountEligable(player))
                    {
                        return;
                    }

                    var idealMountPointFor = vehicle.GetIdealMountPointFor(player);
                    if (idealMountPointFor == null)
                    {
                        return;
                    }

                    idealMountPointFor.MountPlayer(player);
                    if (player.GetMountedVehicle() == vehicle)
                    {
                        vehicle.PlayerMounted(player, idealMountPointFor);
                    }
                }
            }
        }

        public class WaterServerProjectile : ServerProjectile
        {
            public string ExplosionEffect;
            public BasePlayer Attacker;
            public BaseEntity AttackingPrefab;
            public List<DamageTypeEntry> DamageTypes;
            public float SplashRadius;
            public string AmmoPrefab;
            public int AmmoAmount;
            public Rigidbody RigidBody;

            public override void OnHit(RaycastHit rayHit, BaseEntity hitEntity)
            {
                base.OnHit(rayHit, hitEntity);

                Vector3 posWorld = transform.position;
                Effect.server.Run(ExplosionEffect, posWorld, GetExplosionNormal(), null, broadcast: true);
                WaterBall.DoSplash(posWorld, SplashRadius, Instance.WeaponsToAmmoTypeItemIdMap[AmmoPrefab], AmmoAmount);
                var minRadius = SplashRadius * 0.5f;
                DamageUtil.RadiusDamage(Attacker, AttackingPrefab, posWorld, minRadius, SplashRadius, DamageTypes, 131072, useLineOfSight: true);

                if (PushPlayers(posWorld, minRadius, SplashRadius, 131072))
                {
                    Attacker.MarkHostileFor(300f);
                }

                Destroy(this);
            }

            public Vector3 GetExplosionNormal()
            {
                Vector3 result;
                Quaternion rotation = base.transform.rotation;
                Vector3 forward = Vector3.forward;
                result = rotation * forward;
                return result;
            }

            bool PushPlayers(Vector3 pos, float minRadius, float radius, int layers)
            {
                Vector3 force = RigidBody.transform.forward * 5f;
                force.y = 0f;
                force += Vector3.up * 1.5f;

                var hitPlayer = false;
                List<BasePlayer> players = Pool.Get<List<BasePlayer>>();
                Vis.Entities(pos, radius, players, layers);
                for (int i = 0; i < players.Count; i++)
                {
                    BasePlayer baseEntity = players[i];
                    Vector3 vector = baseEntity.ClosestPoint(pos);
                    float num = Mathf.Clamp01((Vector3.Distance(vector, pos) - minRadius) / (radius - minRadius));
                    if (num > 1f)
                    {
                        continue;
                    }

                    baseEntity.DoPush(force);
                    hitPlayer = true;
                }

                Pool.FreeUnmanaged(ref players);

                return hitPlayer;
            }

            new void FixedUpdate()
            {
                DoMovement();
            }

            public override bool IsAValidHit(BaseEntity hitEnt)
            {
                return true;
            }

            public override bool DoMovement()
            {
                if (impacted)
                {
                    return false;
                }

                CurrentVelocity += GetVelocityStep();
                Vector3 vector = AddSwim(CurrentVelocity);
                float num = vector.magnitude * Time.fixedDeltaTime;
                if (DoHitDetection(vector, num))
                {
                    return false;
                }

                if (shouldMoveProjectile)
                {
                    base.transform.position += base.transform.forward * num;
                }

                if (AutomaticallyRotate() && vector != Vector3.zero)
                {
                    base.transform.rotation = Quaternion.LookRotation(vector.normalized);
                }

                PostDoMove();
                return true;
            }
        }

        public class NukeTimedExplosive : TimedExplosive
        {
            public int ExplosionSteps;
            public int ReplaceTreeWithDeadVariantAtStep = -1;

            public override void Explode(Vector3 explosionFxPos)
            {
                Facepunch.Rust.Analytics.Azure.OnExplosion(this);
                Collider component = GetComponent<Collider>();
                if ((bool)component)
                {
                    component.enabled = false;
                }

                WaterLevel.WaterInfo waterInfo = WaterLevel.GetWaterInfo(explosionFxPos - new Vector3(0f, 0.25f, 0f), waves: true, volumes: true);
                if (underwaterExplosionEffect.isValid && waterInfo.isValid && waterInfo.currentDepth >= underwaterExplosionDepth)
                {
                    Effect.server.Run(underwaterExplosionEffect.resourcePath, explosionFxPos, GetExplosionNormal(), null, broadcast: true);
                }
                else if (explosionEffect.isValid)
                {
                    Vector3 posWorld = explosionFxPos;
                    if (explosionOffsetMode == ExplosionEffectOffsetMode.Local)
                    {
                        Vector3 vector = base.transform.TransformPoint(explosionEffectOffset) - base.transform.position;
                        posWorld += vector;
                    }

                    if (explosionOffsetMode == ExplosionEffectOffsetMode.World)
                    {
                        posWorld += explosionEffectOffset;
                    }

                    Effect.server.Run(explosionEffect.resourcePath, posWorld, GetExplosionNormal(), null, broadcast: true);
                }

                if (watersurfaceExplosionEffect.isValid && waterInfo.isValid && waterInfo.overallDepth >= watersurfaceExplosionDepth.x && waterInfo.currentDepth <= watersurfaceExplosionDepth.y)
                {
                    Effect.server.Run(watersurfaceExplosionEffect.resourcePath, explosionFxPos.WithY(waterInfo.surfaceLevel), GetExplosionNormal(), null, broadcast: true);
                }


                Vector3 vector2 = ExplosionCenter();

                ServerMgr.Instance.StartCoroutine(WeaponUtilities.DoNukeExplosion(creatorEntity, LookupPrefab(), vector2, minExplosionRadius, explosionRadius, ExplosionSteps, 1210222849, replaceTreeWithDeadVariantAtStep: ReplaceTreeWithDeadVariantAtStep));

                SeismicSensor.Notify(vector2, vibrationLevel);
                SingletonComponent<NpcNoiseManager>.Instance.OnExplosion(creatorEntity, this);

                if (!base.IsDestroyed && !HasFlag(Flags.Broken) && !ConVar.Server.explosive_testing_mode)
                {
                    Kill(DestroyMode.Gib);
                }
            }
        }

        public class CustomSeekingServerProjectile : ServerProjectile
        {
            const string RADAR_LOCK = "RadarLock";

            public SeekerStrength MinStrength = SeekerStrength.MEDIUM;
            public SeekerStrength MaxStrength = SeekerStrength.HIGHEST;

            public float courseAdjustRate = 1f;

            public float maxTrackDistance = 500f;

            public float minLockDot;

            public float flareLockDot = 0.6f;

            public bool autoSeek;

            public float swimAfter = 6f;

            public float launchingDuration = 0.15f;

            public float armingDuration = 0.75f;

            public float velocityRampUpTime = 6f;

            public Vector3 armingFinalDir;

            public float armingVelocity;

            public float orphanedVectorChangeRate = 30f;

            public SeekerTarget lockedTarget;

            public AnimationCurve velocityCurve;

            public float nextTargetUpdateTime = float.NegativeInfinity;

            public Vector3 seekingDestination;

            public float launchTime;

            public Vector3 initialDir = Vector3.forward;

            public bool orphanedProjectile;

            public Vector3 orphanedTargetVector;

            public Vector3 orphanedRotationAxis;

            public float totalArmingPhaseDuration => launchingDuration + armingDuration;

            Transform cachedTransform;
            ISeekerTargetOwner sourceSeekerOwner;
            bool hasSourceSeekerOwner;
            HashSet<ISeekerTargetOwner> ignored = new HashSet<ISeekerTargetOwner>();


            void Awake()
            {
                cachedTransform = this.transform;
            }

            public override void InitShared()
            {
                base.InitShared();
                sourceSeekerOwner = ignoreEntity as ISeekerTargetOwner;
                hasSourceSeekerOwner = sourceSeekerOwner != null;
            }

            public Vector3 GetSeekingDestination()
            {
                return seekingDestination;
            }

            public float TimeSinceArmed()
            {
                return TimeSinceLaunch() - totalArmingPhaseDuration;
            }

            public float TimeSinceLaunch()
            {
                return Mathf.Max(Time.time - launchTime, 0f);
            }

            public void EnableBoosters()
            {
                using (FlagsUpdateScope flagsUpdateScope = baseEntity.StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(BaseEntity.Flags.On, b: true);
                }

                Invoke(DisableBoosters, 1f);
            }

            public void DisableBoosters()
            {
                using (FlagsUpdateScope flagsUpdateScope = baseEntity.StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(BaseEntity.Flags.On, b: false);
                }
            }

            public override void InitializeVelocity(Vector3 overrideVel)
            {
                Vector3 normalized = overrideVel.normalized;
                launchTime = Time.time;
                initialDir = normalized;
                Invoke(EnableBoosters, 0.5f);
                base.InitializeVelocity(overrideVel);
            }

            public void PickNewRotationAxis()
            {
                orphanedRotationAxis = Vector3.Cross(orphanedTargetVector, UnityEngine.Random.onUnitSphere).normalized;
            }

            public void UpdateTarget()
            {
                if (orphanedProjectile)
                {
                    lockedTarget = null;
                    return;
                }

                if (Time.realtimeSinceStartup >= nextTargetUpdateTime)
                {
                    if (autoSeek)
                    {
                        lockedTarget = GetBestForPoint(cachedTransform.position, cachedTransform.forward, minLockDot, maxTrackDistance, MinStrength, MaxStrength);
                    }
                    else
                    {
                        SeekerTarget bestForPoint = GetBestForPoint(cachedTransform.position, cachedTransform.forward, flareLockDot, maxTrackDistance, MinStrength, MaxStrength);
                        if (bestForPoint != null)
                        {
                            if (lockedTarget != null)
                            {
                                ignored.Add(lockedTarget.owner);
                            }

                            lockedTarget = bestForPoint;
                        }
                    }

                    nextTargetUpdateTime = Time.realtimeSinceStartup + 0.1f;
                }

                if (lockedTarget != null && lockedTarget.TryGetPosition(out var result))
                {
                    seekingDestination = result;
                }
                else
                {
                    seekingDestination = cachedTransform.position + cachedTransform.forward * 1000f;
                }

                if (lockedTarget != null)
                {
                    autoSeek = false;
                    lockedTarget.SendOwnerMessage(base.baseEntity, RADAR_LOCK);
                }
            }

            public override bool DoMovement()
            {
                float num = TimeSinceLaunch();
                if (!(num < launchingDuration))
                {
                    if (num < totalArmingPhaseDuration)
                    {
                        float num2 = num - launchingDuration;
                        Vector3 vector = Vector3.Lerp(initialDir, armingFinalDir, Mathf.Clamp01(num2 / armingDuration));
                        base.CurrentVelocity = vector * armingVelocity;
                    }
                    else
                    {
                        UpdateTarget();
                        Vector3 normalized = base.CurrentVelocity.normalized;
                        Vector3 normalized2;
                        if (orphanedProjectile)
                        {
                            normalized2 = orphanedTargetVector;
                            orphanedTargetVector = Quaternion.AngleAxis(orphanedVectorChangeRate * Time.deltaTime, orphanedRotationAxis) * orphanedTargetVector;
                            if (UnityEngine.Random.value < 0.02f)
                            {
                                PickNewRotationAxis();
                            }
                        }
                        else
                        {
                            normalized2 = (GetSeekingDestination() - cachedTransform.position).normalized;
                        }

                        Vector3 vector2 = Vector3.MoveTowards(normalized, normalized2, Time.fixedDeltaTime * courseAdjustRate);
                        vector2.Normalize();
                        float num3 = armingVelocity + velocityCurve.Evaluate(TimeSinceArmed() / velocityRampUpTime) * speed;
                        base.CurrentVelocity = vector2 * num3;
                    }
                }

                return base.DoMovement();
            }

            SeekerTarget GetBestForPoint(Vector3 from, Vector3 forward, float maxCone, float maxDist, SeekerStrength minStrength = SeekerStrength.LOW, SeekerStrength maxStrength = SeekerStrength.HIGHEST)
            {
                SeekerTarget result = null;
                float num = 0f;
                foreach (KeyValuePair<ISeekerTargetOwner, SeekerTarget> seekerTarget in SeekerTarget.seekerTargets)
                {
                    ISeekerTargetOwner key = seekerTarget.Key;
                    if (hasSourceSeekerOwner && key == sourceSeekerOwner)
                    {
                        continue;
                    }

                    if (ignored.Contains(key))
                    {
                        continue;
                    }

                    SeekerTarget value = seekerTarget.Value;
                    if (value.strength > maxStrength || value.strength < minStrength || !value.IsValidTarget() || !value.TryGetPosition(out var result2))
                    {
                        continue;
                    }

                    Vector3 rhs = Vector3Ex.Direction(result2, from);
                    float num2 = Vector3.Dot(forward, rhs);
                    float num3 = Vector3.Distance(result2, from);
                    if (num3 < maxDist && num2 > maxCone)
                    {
                        float num4 = 1f - num3 / maxDist * 0.3f;
                        float num5 = num2 / maxCone * 1f;
                        float num6 = (float)value.strength / 1000f * 0.5f;
                        float num7 = num4 + num5 + num6;
                        if (num7 > num && key.IsVisible(from, maxDist))
                        {
                            result = value;
                            num = num7;
                        }
                    }
                }

                return result;
            }
        }

        public class SpecialDroneCamera : SpecialCCTV_RC
        {
            public override bool CanAcceptInput => true;

            public override void ServerInit()
            {
                turnSpeed = 1.5f;
                yawClamp = new Vector2(-360, 360);
                pitchClamp = new Vector2(-180, 180);
            }

            public override void UserInput(InputState inputState, CameraViewerId viewerID)
            {
                if (UpdateManualAim(inputState))
                {
                    CustomSendNetworkUpdate_Position(true);
                }
            }

            public override bool UpdateManualAim(InputState inputState)
            {
                if (!HasWorldItem)
                {
                    return false;
                }

                float num = 0f - inputState.current.mouseDelta.y;
                float x = inputState.current.mouseDelta.x;
                pitchAmount = Mathf.Clamp(pitchAmount + num * turnSpeed, pitchClamp.x, pitchClamp.y);
                yawAmount = Mathf.Clamp(yawAmount + x * turnSpeed, yawClamp.x, yawClamp.y) % 360f;

                var lerpedX = Mathf.Lerp(this.transform.localEulerAngles.x, pitchAmount, serverLerpSpeed);
                var lerpedY = Mathf.Lerp(this.transform.localEulerAngles.y, yawAmount, serverLerpSpeed);

                var eulerAngles = this.transform.localEulerAngles;

                var sendUpdate = false;
                if (eulerAngles.x != lerpedX)
                {
                    sendUpdate = true;
                }
                else if (eulerAngles.y != lerpedY)
                {
                    sendUpdate = true;
                }

                eulerAngles.y = lerpedY;
                eulerAngles.x = lerpedX;

                this.transform.localEulerAngles = eulerAngles;
                return sendUpdate;
            }
        }

        public class SpecialTurretCamera : SpecialPoweredRemoteControlEntity
        {
        }

        public class TVGuidedRocketCamera : SpecialPoweredRemoteControlEntity
        {
            TimedExplosive projectile;
            ServerProjectile serverProjectile;
            BasePlayer player;

            bool isTracking = true;
            float cachedY;
            float cachedX;

            public override void ServerInit()
            {
                base.ServerInit();
                projectile = GetParentEntity() as TimedExplosive;
                serverProjectile = projectile.GetComponent<ServerProjectile>();
            }

            public void BeginTracking(BasePlayer player)
            {
                this.player = player;
                isTracking = true;
            }

            public override void UserInput(InputState inputState, CameraViewerId viewerID)
            {
                if (!isTracking || projectile.IsDestroyed)
                {
                    return;
                }

                if (player.IsDead() || player.IsWounded())
                {
                    isTracking = false;
                    return;
                }

                if (!inputState.IsAnyDown() && inputState.current.mouseDelta.y == cachedY && inputState.current.mouseDelta.x == cachedX)
                {
                    return;
                }

                cachedY = inputState.current.mouseDelta.y;
                cachedX = inputState.current.mouseDelta.x;

                var yaw = inputState.IsDown(BUTTON.RIGHT) ? 1f : 0.0f;
                yaw -= inputState.IsDown(BUTTON.LEFT) ? 1f : 0.0f;

                var pitch = inputState.IsDown(BUTTON.BACKWARD) ? 1f : 0.0f;
                pitch -= inputState.IsDown(BUTTON.FORWARD) ? 1f : 0.0f;

                var yawCoEfficient = yaw * 2;
                var pitchCoEfficient = pitch * 2;

                var yawMod = Quaternion.AngleAxis(yawCoEfficient, projectile.transform.up);
                var pitchMod = Quaternion.AngleAxis(pitchCoEfficient, projectile.transform.right);
                var modRot = pitchMod * yawMod;
                var rot = modRot * projectile.transform.rotation;
                projectile.transform.rotation = rot;
                serverProjectile.CurrentVelocity = projectile.transform.forward * serverProjectile.speed;
            }

            public override bool CanControl(ulong playerID)
            {
                if (projectile.IsDestroyed || serverProjectile.impacted)
                {
                    return false;
                }

                if (IsDead() || IsDestroyed)
                {
                    return false;
                }

                CustomSendNetworkUpdate_Position(true);
                return true;
            }
        }

        public class SpecialVehicleModule : SpecialBaseCombatEntity
        {
            public override void Save(SaveInfo info)
            {
                base.Save(info);
                info.msg.simpleUID = Facepunch.Pool.Get<SimpleUID>();
            }
        }

        public class SpecialCCTV_RC : SpecialPoweredRemoteControlEntity
        {
            public SpecialWorldItem WorldItem;

            protected bool HasWorldItem;

            public Transform yaw;

            public Transform pitch;

            public Vector2 pitchClamp = new Vector2(-50f, 50f);

            public Vector2 yawClamp = new Vector2(-50f, 50f);

            public float[] fovScales = new float[0];

            public float pitchAmount;

            public float yawAmount;
            public float turnSpeed = 0.5f;

            public float serverLerpSpeed = 0.5f;

            public float clientLerpSpeed = 0.5f;

            public float zoomLerpSpeed = 10f;

            public int fovScaleIndex;

            public float fovScaleLerped = 1f;

            public bool hasPTZ = true;

            public const Flags Flag_HasViewer = Flags.Reserved5;

            public RealTimeSinceEx timeSinceLastServerTick;

            public override bool RequiresMouse => hasPTZ;

            protected override bool EntityCanPing => true;

            public override bool CanAcceptInput => hasPTZ;

            public override void ServerInit()
            {
                base.ServerInit();
                timeSinceLastServerTick = 0.0;
                InvokeRandomized(ServerTick, UnityEngine.Random.Range(0f, 1f), 0.015f, 0.01f);
            }

            public override void PostServerLoad()
            {
                base.PostServerLoad();
                UpdateRotation(10000f);
            }

            public override void UserInput(InputState inputState, CameraViewerId viewerID)
            {
                if (UpdateManualAim(inputState))
                {
                    SendNetworkUpdate();
                }
            }

            public override bool InitializeControl(CameraViewerId viewerID)
            {
                bool result = base.InitializeControl(viewerID);
                UpdateViewers();
                return result;
            }

            public override void StopControl(CameraViewerId viewerID)
            {
                base.StopControl(viewerID);
                UpdateViewers();
            }

            public void UpdateViewers()
            {
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.Reserved5, base.ViewerCount > 0);
                }
            }

            public void ServerTick()
            {
                if (!base.IsDestroyed)
                {
                    float delta = (float)(double)timeSinceLastServerTick;
                    timeSinceLastServerTick = 0.0;
                    UpdateRotation(delta);
                }
            }

            public virtual bool UpdateManualAim(InputState inputState)
            {
                if (!hasPTZ)
                {
                    return false;
                }

                float num = 0f - inputState.current.mouseDelta.y;
                float x = inputState.current.mouseDelta.x;
                pitchAmount = Mathf.Clamp(pitchAmount + num * turnSpeed, pitchClamp.x, pitchClamp.y);
                yawAmount = Mathf.Clamp(yawAmount + x * turnSpeed, yawClamp.x, yawClamp.y) % 360f;

                bool flag = inputState.WasJustPressed(BUTTON.FIRE_PRIMARY);
                if (flag)
                {
                    fovScaleIndex = (fovScaleIndex + 1) % fovScales.Length;
                }

                return num != 0f || x != 0f || flag;
            }

            public virtual void UpdateRotation(float delta)
            {
                if (!hasPTZ)
                {
                    return;
                }

                Quaternion to = Quaternion.Euler(pitchAmount, 0f, 0f);
                Quaternion to2 = Quaternion.Euler(0f, yawAmount, 0f);
                float speed = serverLerpSpeed;
                pitch.transform.localRotation = Mathx.Lerp(pitch.transform.localRotation, to, speed, delta);
                yaw.transform.localRotation = Mathx.Lerp(yaw.transform.localRotation, to2, speed, delta);
                if (fovScales != null && fovScales.Length != 0)
                {
                    if (fovScales.Length > 1)
                    {
                        fovScaleLerped = Mathx.Lerp(fovScaleLerped, fovScales[fovScaleIndex], zoomLerpSpeed, delta);
                    }
                    else
                    {
                        fovScaleLerped = fovScales[0];
                    }
                }
                else
                {
                    fovScaleLerped = 1f;
                }

                if (HasWorldItem)
                {
                    var eulerAngles = WorldItem.transform.localRotation.eulerAngles;
                    eulerAngles.y = yaw.localRotation.eulerAngles.y;
                    eulerAngles.x = pitch.localRotation.eulerAngles.x;
                    WorldItem.transform.localRotation = Quaternion.Euler(eulerAngles);
                }
            }

            public override void Load(LoadInfo info)
            {
                base.Load(info);
                if (info.msg.rcEntity != null)
                {
                    int num = Mathf.Clamp((int)info.msg.rcEntity.zoom, 0, fovScales.Length - 1);
                    pitchAmount = info.msg.rcEntity.aim.x;
                    yawAmount = info.msg.rcEntity.aim.y;
                    fovScaleIndex = num;
                }
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);
                if (info.msg.rcEntity == null)
                {
                    info.msg.rcEntity = Facepunch.Pool.Get<RCEntity>();
                }

                DoSave(info);
            }

            protected virtual void DoSave(SaveInfo info)
            {
                info.msg.rcEntity.aim.x = pitchAmount;
                info.msg.rcEntity.aim.y = yawAmount;
                info.msg.rcEntity.aim.z = 0f;
                info.msg.rcEntity.zoom = fovScaleIndex;
            }

            public virtual void SetChildren()
            {
                WorldItem = this.transform.GetComponentInChildren<SpecialWorldItem>();
                HasWorldItem = !object.ReferenceEquals(WorldItem, null);
            }
        }

        public class SpecialAutoTurret_RC : SpecialCCTV_RC
        {
            Vector3 aimDir = new Vector3(0, 0, 0);
            Quaternion aimDirRot;
            float rcTurnSensitivity = 4f;

            public override void ServerInit()
            {
                base.ServerInit();
            }

            public override void SetChildren()
            {
                base.SetChildren();
                aimDir = KaruzaEntity.BaseEntity.transform.forward;
            }

            public override void UserInput(InputState inputState, CameraViewerId viewerID)
            {
                if (UpdateManualAim(inputState))
                {
                    ClientRPC(RpcTarget.NetworkGroup("CLIENT_ReceiveAimDir"), aimDir);
                }
            }

            public override bool InitializeControl(CameraViewerId viewerID)
            {
                var init = base.InitializeControl(viewerID);

                if (init)
                {
                    ClientRPC(RpcTarget.NetworkGroup("CLIENT_ReceiveAimDir"), aimDir);
                }

                return init;
            }

            public override bool UpdateManualAim(InputState inputState)
            {
                float x = (0f - inputState.current.mouseDelta.y) * rcTurnSensitivity;
                float y = inputState.current.mouseDelta.x * rcTurnSensitivity;
                Vector3 euler = Quaternion.LookRotation(aimDir, base.transform.up).eulerAngles + new Vector3(x, y, 0f);
                if (euler.x >= 0f && euler.x <= 135f)
                {
                    euler.x = Mathf.Clamp(euler.x, 0f, 45f);
                }

                if (euler.x >= 225f && euler.x <= 360f)
                {
                    euler.x = Mathf.Clamp(euler.x, 285f, 360f);
                }

                Vector3 vector = Quaternion.Euler(euler) * Vector3.forward;
                bool result = !Mathf.Approximately(aimDir.x, vector.x) || !Mathf.Approximately(aimDir.y, vector.y) || !Mathf.Approximately(aimDir.z, vector.z);
                aimDir = vector;

                return y != 0f || x != 0f;
            }

            public override void UpdateRotation(float delta)
            {
                //SendAimDir();
                if (HasWorldItem)
                {
                    if (aimDir != Vector3.zero)
                    {
                        aimDirRot = Quaternion.LookRotation(aimDir);
                        WorldItem.transform.rotation = aimDirRot;
                    }
                    else
                    {
                        WorldItem.transform.localEulerAngles = Vector3.zero;
                    }
                }
            }

            protected override void DoSave(SaveInfo info)
            {

            }
        }

        public class SpecialPoweredRemoteControlEntity : SpecialIOEntity, IRemoteControllable
        {
            public bool isStatic;

            public Transform viewEyes;

            protected virtual bool EntityCanPing => false;

            public virtual bool CanAcceptInput => false;

            public int ViewerCount { get; set; }

            public CameraViewerId? ControllingViewerId { get; set; }

            public virtual bool RequiresMouse => false;

            public virtual float MaxRange => 10000f;

            public RemoteControllableControls rcControls;

            public RemoteControllableControls RequiredControls => rcControls;

            public bool CanPing => EntityCanPing;

            public bool IsBeingControlled
            {
                get
                {
                    if (ViewerCount > 0)
                    {
                        return ControllingViewerId.HasValue;
                    }

                    return false;
                }
            }

            public bool IsStatic()
            {
                return isStatic;
            }

            public override void Spawn()
            {
                base.Spawn();
            }

            public virtual Matrix4x4 GetEyesMatrix()
            {
                return viewEyes.localToWorldMatrix;
            }

            public virtual bool InitializeControl(CameraViewerId viewerID)
            {
                ++ViewerCount;
                if (CanAcceptInput && !ControllingViewerId.HasValue)
                {
                    ControllingViewerId = viewerID;
                    return true;
                }

                return !CanAcceptInput;
            }

            public virtual void StopControl(CameraViewerId viewerID)
            {
                --ViewerCount;
                if (ControllingViewerId == viewerID)
                {
                    ControllingViewerId = null;
                }
            }

            public virtual void UserInput(InputState inputState, CameraViewerId viewerID)
            {
            }

            public virtual bool CanControl(ulong playerID)
            {
                return true;
            }

            public override bool CanUseNetworkCache(Connection connection)
            {
                if (IsStatic())
                {
                    return base.CanUseNetworkCache(connection);
                }

                return false;
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);
                if (info.forDisk || IsStatic())
                {
                    info.msg.rcEntity = Facepunch.Pool.Get<RCEntity>();
                    info.msg.rcEntity.identifier = GetIdentifier();
                }
            }

            public override void Load(LoadInfo info)
            {
                base.Load(info);
            }

            public Transform GetEyes()
            {
                return viewEyes;
            }

            public virtual float GetFovScale()
            {
                return 1f;
            }

            public BaseEntity GetEnt()
            {
                return this;
            }

            public void UpdateIdentifier(string newID, bool clientSend = false)
            {
            }

            public string GetIdentifier()
            {
                return string.Empty;
            }

            public void RCSetup()
            {
            }

            public void RCShutdown()
            {
            }
        }

        public class SpecialSteeringWheel : SpecialBaseMountable
        {
            BaseBoat boat;

            [NonSerialized]
            public float __sync_ServerSteeringRotation;

            [Sync(Pack = false, Autosave = true)]
            public float ServerSteeringRotation
            {
                [CompilerGenerated]
                get
                {
                    return __sync_ServerSteeringRotation;
                }
                [CompilerGenerated]
                set
                {
                    if (!IsSyncVarEqual(__sync_ServerSteeringRotation, value))
                    {
                        __sync_ServerSteeringRotation = value;
                        byte nameID = __GetWeaverID("ServerSteeringRotation");
                        SV_SyncVarSend(nameID);
                    }
                }
            }

            [RPC_Server]
            [RPC_Server.IsVisible(3f)]
            [RPC_Server.CallsPerSecond(15uL)]
            [RPC_Server.InputValidation(new Type[] { typeof(float) })]
            public void ReceiveClientRotation(RPCMessage msg)
            {
                if (!(msg.player == null) && !(GetMounted() != msg.player))
                {
                    float val = (ServerSteeringRotation = msg.read.Float());
                    boat.steering = Mathx.RemapValClamped(val, -170f, 170f, 1f, -1f);
                }
            }

            public override bool OnRpcMessage(BasePlayer player, uint rpc, Message msg)
            {
                using (TimeWarning.New(RPC_STEERING_WHEEL))
                {
                    if (rpc == 3277541392u && player != null)
                    {
                        Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);

                        using (TimeWarning.New(RPC_RECEIVE_CLIENT_ROTATION))
                        {
                            using (TimeWarning.New(RPC_TIMEWARNING_CONDITIONS))
                            {
                                if (!RPC_Server.CallsPerSecond.Test(3277541392u, RPC_RECEIVE_CLIENT_ROTATION, this, player, 15uL))
                                {
                                    return true;
                                }

                                long position = msg.read.Position;
                                msg.read.Read<float>();
                                msg.read.Position = position;
                                if (!RPC_Server.IsVisible.Test(3277541392u, RPC_RECEIVE_CLIENT_ROTATION, this, player, 3f))
                                {
                                    return true;
                                }
                            }

                            try
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                {
                                    RPCMessage rPCMessage = default(RPCMessage);
                                    rPCMessage.connection = msg.connection;
                                    rPCMessage.player = player;
                                    rPCMessage.read = msg.read;
                                    RPCMessage msg2 = rPCMessage;
                                    ReceiveClientRotation(msg2);
                                }
                            }
                            catch (Exception exception)
                            {
                                Debug.LogException(exception);
                                player.Kick("RPC Error in ReceiveClientRotation");
                            }
                        }

                        return true;
                    }
                }

                return base.OnRpcMessage(player, rpc, msg);
            }

            public override void ServerInit()
            {
                base.ServerInit();

                boat = KaruzaEntity.BaseEntity as BaseBoat;
            }

            public void ResetSteering()
            {
                ServerSteeringRotation = 0f;
                boat.steering = 0;
            }

            public override void PlayerServerInput(InputState inputState, BasePlayer player)
            {
                base.PlayerServerInput(inputState, player);
            }

            public override bool WriteSyncVar(byte id, NetWrite writer)
            {
                if (id == 0)
                {
                    if (ConVar.Global.developer > 2)
                    {
                        NetworkableId iD = net.ID;
                        Debug.Log("SyncVar Writing: ServerSteeringRotation for " + iD.ToString());
                    }

                    SyncVarNetWrite(writer, __sync_ServerSteeringRotation);
                    return true;
                }

                return base.WriteSyncVar(id, writer);
            }

            public override bool OnSyncVar(byte id, NetRead reader, bool fromAutoSave = false)
            {
                if (id == 0)
                {
                    try
                    {
                        _ = __sync_ServerSteeringRotation;
                        float _sync_ServerSteeringRotation = reader.Float();
                        __sync_ServerSteeringRotation = _sync_ServerSteeringRotation;
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }

                    return true;
                }

                return base.OnSyncVar(id, reader, fromAutoSave);
            }

            public byte __GetWeaverID(string propertyName)
            {
                if (propertyName == "ServerSteeringRotation")
                {
                    return 0;
                }

                return byte.MaxValue;
            }

            public override void WriteAutoSaveSyncVars(NetWrite writer)
            {
                base.WriteAutoSaveSyncVars(writer);
                WriteSyncVar(0, writer);
            }

            public override void ReadAutoSaveSyncVars(NetRead reader)
            {
                base.ReadAutoSaveSyncVars(reader);
                OnSyncVar(0, reader, fromAutoSave: true);
            }

            public override bool AutoSaveSyncVars(SaveInfo save)
            {
                base.AutoSaveSyncVars(save);
                NetWrite obj = Network.Net.sv.StartWrite();
                WriteAutoSaveSyncVars(obj);
                var (src, num) = obj.GetBuffer();
                if (_autosaveBuffer == null)
                {
                    _autosaveBuffer = BaseEntity._autosaveBufferPool.Rent(num);
                }

                if (_autosaveBuffer.Length < num)
                {
                    BaseEntity._autosaveBufferPool.Return(_autosaveBuffer);
                    _autosaveBuffer = BaseEntity._autosaveBufferPool.Rent(num);
                }

                Buffer.BlockCopy(src, 0, _autosaveBuffer, 0, num);
                save.msg.baseEntity.syncVars = _autosaveBuffer;
                Facepunch.Pool.Free(ref obj);
                return true;
            }

            public override bool AutoLoadSyncVars(LoadInfo load)
            {
                base.AutoLoadSyncVars(load);
                if (load.msg.baseEntity != null && load.msg.baseEntity.syncVars != null)
                {
                    NetRead obj = Facepunch.Pool.Get<NetRead>();
                    obj.Init(load.msg.baseEntity.syncVars.AsSpan());
                    ReadAutoSaveSyncVars(obj);
                    Facepunch.Pool.Free(ref obj);
                }

                return true;
            }

            public override void ResetSyncVars()
            {
                base.ResetSyncVars();
                __sync_ServerSteeringRotation = 0f;
            }

            public override bool ShouldInvalidateCache(byte id)
            {
                if (id == 0)
                {
                    return true;
                }

                return base.ShouldInvalidateCache(id);
            }
        }


        public class SpecialComputerStationMount : SpecialBaseMountable
        {
            public SpecialComputerStation SpecialComputerStation;

            public bool HasStation { get { return !object.ReferenceEquals(SpecialComputerStation, null); } }

            public override void OnPlayerMounted()
            {
                base.OnPlayerMounted();

                if (Vehicle.VehicleConfig.GeneralToastSettings.Enabled && !string.IsNullOrEmpty(Vehicle.VehicleConfig.GeneralToastSettings.LinkedComputerStationSeatPrompt))
                {
                    Instance.ShowToast(_mounted, Vehicle.VehicleConfig.GeneralToastSettings.LinkedComputerStationSeatPrompt);
                }
            }

            public override bool CanSwapToThis(BasePlayer player)
            {
                if (HasStation && SpecialComputerStation.IsBusy())
                {
                    return false;
                }

                return base.CanSwapToThis(player);
            }

            public override bool DirectlyMountable()
            {
                if (HasStation && SpecialComputerStation.IsBusy())
                {
                    return false;
                }

                return base.DirectlyMountable();
            }

            public override bool AnyMounted()
            {
                if (HasStation && SpecialComputerStation.IsBusy())
                {
                    return true;
                }

                return base.AnyMounted();
            }

            public override void PlayerServerInput(InputState inputState, BasePlayer player)
            {
                if (inputState.IsDown(BUTTON.DUCK) && inputState.IsDown(BUTTON.USE))
                {
                    this.DismountPlayer(player, lite: true);
                    SpecialComputerStation.MountPlayer(player);
                    player.MarkSwapSeat();
                    UpdateMountFlags();
                }

                base.PlayerServerInput(inputState, player);
            }
        }

        public class SpecialComputerStation : SpecialBaseMountable, IWeaponController, ITVGuidedRocketController
        {
            public ComputerStationConfig Config;

            bool invokeStopSpecialGunsStarted = false;
            bool hasCamera;
            bool hasCameraWorldItem;

            public bool HasLinkedMount;
            public SpecialComputerStationMount LinkedMount;
            public SpecialCCTV_RC ControllingCamera;

            public ulong currentPlayerID;

            List<WeaponSystem> WeaponSystems = new List<WeaponSystem>();
            SpecialWorldItem worldItem;
            public IKaruzaCustomPrefab KaruzaCustomEntity { get { return Vehicle; } }
            public BaseEntity WeaponControllerEntity { get { return hasCamera ? hasCameraWorldItem ? this.ControllingCamera.WorldItem : this.ControllingCamera : this.worldItem; } }
            public Vector3 Forward { get { return hasCamera ? hasCameraWorldItem ? this.ControllingCamera.WorldItem.transform.forward : this.ControllingCamera.pitch.transform.forward : this.worldItem.transform.forward; } }
            public List<SpecialGun> SpecialGuns { get; private set; } = new List<SpecialGun>();
            public Transform Transform { get { return hasCamera ? hasCameraWorldItem ? this.ControllingCamera.WorldItem.transform : this.ControllingCamera.pitch.transform : this.worldItem.transform; } }
            public float NextDryFireEffect { get; set; }
            public bool HasAmmoContainer { get; private set; }
            public bool UnlimitedAmmo { get; private set; }
            public ISpecialAmmoContainer AmmoContainer { get; private set; }
            public float LastAmmoUpdate { get; set; }
            public string NoAmmoToast { get; private set; }
            public BaseEntity BaseEntity { get; private set; }

            TVGuidedRocketCamera controlledRocket;
            bool hasControlledRocket;

            public void OnTVGuidedProjectileFired(TVGuidedRocketCamera rocketCamera)
            {
                if (!hasCamera)
                {
                    return;
                }

                if (currentPlayerID <= 0)
                {
                    return;
                }

                controlledRocket = rocketCamera;
                hasControlledRocket = true;
                ControllingCamera.StopControl(new CameraViewerId(currentPlayerID, 0L));
                rocketCamera.InitializeControl(new CameraViewerId(currentPlayerID, 0L));
                SendNetworkUpdateImmediate();
                rocketCamera.BeginTracking(_mounted);
            }

            public void ConfigureWeaponController(IWeaponController wc)
            {
                UnlimitedAmmo = wc.UnlimitedAmmo;
                AmmoContainer = wc.AmmoContainer;
                HasAmmoContainer = wc.HasAmmoContainer;
                NoAmmoToast = wc.NoAmmoToast;
                BaseEntity = wc.BaseEntity;
            }

            public override bool OnRpcMessage(BasePlayer player, uint rpc, Message msg)
            {
                using (TimeWarning.New(COMPUTER_STATION_ONRPCMESSAGE))
                {
                    if (rpc == 552248427 && player != null)
                    {
                        Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                        if (ConVar.Global.developer > 2)
                        {
                            Debug.Log("SV_RPCMessage: " + player?.ToString() + " - BeginControllingBookmark ");
                        }

                        using (TimeWarning.New(RPC_TIMEWARNING_BEGIN_CONTROLLING_BOOKMARK))
                        {
                            try
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                {
                                    RPCMessage rPCMessage = default(RPCMessage);
                                    rPCMessage.connection = msg.connection;
                                    rPCMessage.player = player;
                                    rPCMessage.read = msg.read;
                                    RPCMessage msg6 = rPCMessage;
                                    BeginControllingBookmark(msg6);
                                }
                            }
                            catch (Exception exception5)
                            {
                                Debug.LogException(exception5);
                                player.Kick("RPC Error in BeginControllingBookmark");
                            }
                        }

                        return true;
                    }
                }

                return base.OnRpcMessage(player, rpc, msg);
            }

            #region Computer Station

            List<SpecialCCTV_RC> controllingCameras = new List<SpecialCCTV_RC>();
            Dictionary<string, SpecialCCTV_RC> controlBookmarks = new Dictionary<string, SpecialCCTV_RC>();
            string controlBookmarksStr;

            [RPC_Server]
            public void BeginControllingBookmark(RPCMessage msg)
            {
                BasePlayer player = msg.player;
                string text = msg.read.String();
                if (!controlBookmarks.TryGetValue(text, out SpecialCCTV_RC camera))
                {
                    return;
                }

                if (hasControlledRocket)
                {
                    hasControlledRocket = false;
                    controlledRocket.StopControl(new CameraViewerId(currentPlayerID, 0L));
                }

                var baseEntity = ControllingCamera;
                if (baseEntity)
                {
                    baseEntity.StopControl(new CameraViewerId(currentPlayerID, 0L));
                }

                SetControllingCamera(camera);
            }

            public void SendControlBookmarks(BasePlayer player)
            {
                if (controllingCameras.Count <= 1)
                {
                    return;
                }

                string arg = controlBookmarksStr;
                ClientRPC(RpcTarget.Player(RPC_RECEIVE_BOOKMARKS, player), arg);
            }

            public void SetChildren(List<SpecialCCTV_RC> cameras, SpecialWorldItem worldItem, SpecialGun[] specialGuns)
            {
                SpecialGuns.AddRange(specialGuns);
                if (cameras != null && cameras.Count > 0)
                {
                    for (int i = 0; i < cameras.Count; i++)
                    {
                        var camera = cameras[i];
                        camera.isStatic = true;
                        camera.syncPosition = true;
                        hasCamera = true;

                        if (Config.WeaponControllerConfig.MinY != -360 && Config.WeaponControllerConfig.MaxY != 360)
                        {
                            camera.yawClamp = new Vector2(Config.WeaponControllerConfig.MinY, Config.WeaponControllerConfig.MaxY);
                        }

                        if (Config.WeaponControllerConfig.MinX != -360 && Config.WeaponControllerConfig.MaxX != 360)
                        {
                            camera.pitchClamp = new Vector2(Config.WeaponControllerConfig.MinX, Config.WeaponControllerConfig.MaxX);
                        }

                        camera.SetChildren();

                        controllingCameras.Add(camera);

                        var cameraName = $"Camera {i}";
                        controlBookmarks.Add(cameraName, camera);
                        controlBookmarksStr += $"{cameraName};";
                    }
                }
                else if (worldItem != null)
                {
                    this.worldItem = worldItem;
                }
            }

            public void SetControllingCamera(SpecialCCTV_RC camera)
            {
                ControllingCamera = camera;
                hasCameraWorldItem = !object.ReferenceEquals(ControllingCamera.WorldItem, null);
                _mounted.net.SwitchSecondaryGroup(BaseNetworkable.GetGlobalNetworkGroup(this.globalNetworkBehavior));

                bool b = ControllingCamera.InitializeControl(new CameraViewerId(currentPlayerID, 0L));
                SetFlagLocal(Flags.Reserved2, b);
                SendNetworkUpdateImmediate();
                SendControlBookmarks(_mounted);
                InvokeRepeating(ControlCheck, 0f, 0f);
            }

            public void InitializeStation(ComputerStationConfig config, List<SpecialCCTV_RC> cameras, SpecialWorldItem worldItem, SpecialGun[] specialGuns)
            {
                this.Config = config;

                if (config.WeaponControllerConfig != null)
                {
                    var vehicle = Vehicle;
                    var hasVehicle = vehicle != null;

                    for (int i = 0; i < config.WeaponControllerConfig.WeaponConfigurations.Count; i++)
                    {
                        var wc = config.WeaponControllerConfig.WeaponConfigurations[i];
                        if (!wc.Enabled)
                        {
                            continue;
                        }

                        var ws = new WeaponSystem()
                        {
                            Config = wc
                        };

                        if (hasVehicle)
                        {
                            ws.RigidBody = vehicle.BaseVehicle.rigidBody;
                            ws.HasRigidBody = true;
                        }

                        for (int n = 0; n < wc.CounterConfigurations.Count; n++)
                        {
                            var cc = wc.CounterConfigurations[n];
                            if (!cc.Enabled)
                            {
                                continue;
                            }

                            var counter = PropUtilities.CreateCustomEntity<VehicleAmmoCounter>(cc.Location, cc.Rotation, cc.Scale, PREFAB_COUNTER, Vehicle.BaseVehicle);
                            counter.ConfigureCounter(ws);
                        }

                        WeaponSystems.Add(ws);
                    }
                }

                SetChildren(cameras, worldItem, specialGuns);
            }

            public override void DestroyShared()
            {
                if (base.isServer && (bool)GetMounted())
                {
                    StopControl(GetMounted());
                }

                base.DestroyShared();
            }

            public void StopControl(BasePlayer player)
            {
                if (hasCamera && !object.ReferenceEquals(ControllingCamera, null))
                {
                    ControllingCamera.StopControl(new CameraViewerId(currentPlayerID, 0L));
                    ControllingCamera = null;
                    hasCameraWorldItem = false;
                    if (player != null)
                    {
                        player.net.SwitchSecondaryGroup(null);
                    }
                }

                currentPlayerID = 0uL;
                SetFlagLocal(Flags.Reserved2, false);
                SendNetworkUpdate();
                CancelInvoke(ControlCheck);
            }

            public void ControlCheck()
            {
                bool flag = false;
                if (hasCamera && IsMounted())
                {
                    if (_mounted.IsNpc || _mounted.IsBot)
                    {
                        return;
                    }

                    if (hasControlledRocket)
                    {
                        if (!controlledRocket.CanControl(currentPlayerID))
                        {
                            hasControlledRocket = false;
                            controlledRocket.StopControl(new CameraViewerId(currentPlayerID, 0L));
                            ControllingCamera.InitializeControl(new CameraViewerId(currentPlayerID, 0L));
                            SendNetworkUpdateImmediate();
                        }
                        else
                        {
                            _mounted.net.SwitchSecondaryGroup(BaseNetworkable.GetGlobalNetworkGroup(this.globalNetworkBehavior));
                        }

                        return;
                    }

                    if (ControllingCamera.CanControl(currentPlayerID))
                    {
                        flag = true;
                        _mounted.net.SwitchSecondaryGroup(BaseNetworkable.GetGlobalNetworkGroup(this.globalNetworkBehavior));
                    }
                }

                if (!flag)
                {
                    StopControl(_mounted);
                }
            }

            public override void OnPlayerMounted()
            {
                base.OnPlayerMounted();

                var mounted = _mounted;
                currentPlayerID = mounted.userID.Get();

                if (hasCamera)
                {
                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(BaseEntity.Flags.On, b: true);
                    }

                    ControllingCamera?.StopControl(new CameraViewerId(currentPlayerID, 0L));
                    SetControllingCamera(controllingCameras[0]);
                }
                else
                {
                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(BaseEntity.Flags.Reserved2, b: true);
                    }
                }
            }

            public override bool AttemptDismount(BasePlayer player)
            {
                if (hasControlledRocket)
                {
                    hasControlledRocket = false;
                    controlledRocket.StopControl(new CameraViewerId(currentPlayerID, 0L));
                    ControllingCamera.InitializeControl(new CameraViewerId(currentPlayerID, 0L));
                    SendNetworkUpdateImmediate();
                    return false;
                }
                else if (hasCamera)
                {
                    if (HasLinkedMount)
                    {
                        StopControl(player);
                        using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                        {
                            flagsUpdateScope.Set(BaseEntity.Flags.On, b: false);
                        }

                        this.DismountPlayer(player, lite: true);
                        LinkedMount.MountPlayer(player);
                        player.MarkSwapSeat();
                        UpdateMountFlags();
                        return false;
                    }
                    else if (!player.serverInput.WasDown(BUTTON.JUMP) && !player.serverInput.IsDown(BUTTON.JUMP))
                    {
                        int currentSeatIndex = Vehicle.BaseVehicle.GetPlayerSeat(player);
                        Vehicle.SwapSeats(player, -1);
                        int newSeatIndex = Vehicle.BaseVehicle.GetPlayerSeat(player);

                        if (currentSeatIndex != newSeatIndex)
                        {
                            StopControl(player);
                            using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                            {
                                flagsUpdateScope.Set(BaseEntity.Flags.On, b: false);
                            }

                            UpdateMountFlags();
                        }

                        return false;
                    }
                }

                return base.AttemptDismount(player);
            }

            public override void OnPlayerDismounted(BasePlayer player)
            {
                base.OnPlayerDismounted(player);
                StopControl(player);
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(BaseEntity.Flags.On, b: false);
                }
            }

            public override void PlayerServerInput(InputState inputState, BasePlayer player)
            {
                if (Config.WeaponControllerConfig.RequiresEngine && !Vehicle.BaseEntity.IsOn())
                {
                    return;
                }

                if (Config.IsDriver)
                {
                    Vehicle.PlayerServerInput(inputState, player);
                }

                if (hasControlledRocket)
                {
                    controlledRocket.UserInput(inputState, new CameraViewerId(player.userID.Get(), 0L));
                }
                else if (hasCamera)
                {
                    if (!HasFlag(Flags.Reserved2))
                    {
                        return;
                    }

                    ControllingCamera.UserInput(inputState, new CameraViewerId(player.userID.Get(), 0L));
                    UpdatePing(player);
                }
                else
                {
                    var rotation = (Quaternion.Inverse(Vehicle.BaseEntity.transform.rotation) * player.eyes.rotation);
                    var lookEuler = rotation.eulerAngles;

                    for (int i = 0; i < 3; i++)
                    {
                        lookEuler[i] -= ((lookEuler[i] > 180f) ? 360f : 0f);
                    }

                    lookEuler.y = Mathf.Clamp(lookEuler.y, Config.WeaponControllerConfig.MinY, Config.WeaponControllerConfig.MaxY);
                    lookEuler.x = Mathf.Clamp(lookEuler.x, Config.WeaponControllerConfig.MinX, Config.WeaponControllerConfig.MaxX);

                    worldItem.transform.rotation = Vehicle.BaseEntity.transform.rotation * Quaternion.Euler(lookEuler);
                }

                Vehicle.OnJointUpdate();
                UpdateWeapons(player);
            }

            void UpdatePing(BasePlayer player)
            {
                if (!player.serverInput.WasJustReleased(BUTTON.FIRE_THIRD))
                {
                    return;
                }

                if (!Physics.Raycast(ControllingCamera.transform.position, ControllingCamera.pitch.forward, out RaycastHit hit, 450, GENERAL_COLLIDER))
                {
                    return;
                }

                player.AddPingAtLocation(PingType.Hostile, hit.point, 15, player.net.ID);
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);
                if (!info.forDisk)
                {
                    info.msg.ioEntity = Facepunch.Pool.Get<ProtoBuf.IOEntity>();
                    if (hasControlledRocket && !object.ReferenceEquals(controlledRocket.net, null))
                    {
                        info.msg.ioEntity.genericEntRef1 = controlledRocket.net.ID;
                    }
                    else
                    {
                        info.msg.ioEntity.genericEntRef1 = ControllingCamera ? ControllingCamera.net.ID : new NetworkableId();
                    }
                }
                else
                {
                    info.msg.computerStation = Facepunch.Pool.Get<ProtoBuf.ComputerStation>();
                    info.msg.computerStation.bookmarks = string.Empty;
                }
            }

            public bool AllowPings()
            {
                return true;
            }

            //public override bool AnyMounted()
            //{
            //    if (Config.LinkedMount != null && Config.LinkedMount.Enabled)
            //    {
            //        return true;
            //    }

            //    return base.AnyMounted();
            //}

            public override bool DirectlyMountable()
            {
                if (Config.LinkedMount != null && Config.LinkedMount.Enabled)
                {
                    return false;
                }

                return base.DirectlyMountable();
            }

            #endregion

            #region Weapons

            private void UpdateWeapons(BasePlayer driver)
            {
                if (Vehicle.BaseEntity.InSafeZone())
                {
                    return;
                }

                for (int i = 0; i < WeaponSystems.Count; i++)
                {
                    var ws = WeaponSystems[i];

                    WeaponUtilities.UpdateWeapon(this, driver, ws);
                }
            }

            void StopSpecialGuns()
            {
                foreach (var specialGun in SpecialGuns)
                {
                    specialGun.ToggleOff();
                }

                invokeStopSpecialGunsStarted = false;
            }

            public void InvokeStopSpecialGuns()
            {
                if (!invokeStopSpecialGunsStarted)
                {
                    invokeStopSpecialGunsStarted = true;
                    Invoke(StopSpecialGuns, SPECIAL_GUN_REPEAT_DELAY);
                }
            }

            #endregion
        }

        public class SpecialSnowmobile : VehicleMount
        {
            bool updateFlags;

            protected override void Awake()
            {
                base.Awake();
                enabled = true;
            }

            public void LateUpdate()
            {
                if (Vehicle.BaseVehicle.HasFlag(Flags.Reserved5) != HasFlag(Flags.Reserved5))
                {
                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(Flags.Reserved5, Vehicle.BaseVehicle.HasFlag(Flags.Reserved5));
                    }
                }

                if (Vehicle.BaseEntity.IsOn())
                {
                    SendNetworkUpdate();
                }
            }

            public void RPC_OpenFuel(RPCMessage msg)
            {
                BasePlayer player = msg.player;
                Vehicle.LootFuel(player);
            }

            public void RPC_OpenItemStorage(RPCMessage msg)
            {
                BasePlayer player = msg.player;
                Vehicle.LootStorage(player);
            }

            public void RPC_WantsPush(RPCMessage msg)
            {
                BasePlayer player = msg.player;
                if (!player.isMounted && Vehicle.BaseVehicle.CanPushNow(player) && (!Vehicle.BaseVehicle.OnlyOwnerAccessible() || player == creatorEntity) && Interface.CallHook("OnVehiclePush", Vehicle.BaseVehicle, msg.player) == null)
                {
                    player.metabolism.calories.Subtract(3f);
                    player.metabolism.SendChanges();
                    if (Vehicle.BaseVehicle.rigidBody.IsSleeping())
                    {
                        Vehicle.BaseVehicle.rigidBody.WakeUp();
                    }

                    Vehicle.BaseVehicle.DoPushAction(player);
                    Vehicle.BaseVehicle.timeSinceLastPush = 0f;
                }
            }

            public override bool OnRpcMessage(BasePlayer player, uint rpc, Message msg)
            {
                using (TimeWarning.New(SNOWMOBILE_ONRPCMESSAGE))
                {
                    if (rpc == 1851540757 && player != null)
                    {
                        Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                        if (ConVar.Global.developer > 2)
                        {
                            Debug.Log("SV_RPCMessage: " + player?.ToString() + " - RPC_OpenFuel ");
                        }

                        using (TimeWarning.New(RPC_OPENFUEL))
                        {
                            try
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                {
                                    RPCMessage rPCMessage = default(RPCMessage);
                                    rPCMessage.connection = msg.connection;
                                    rPCMessage.player = player;
                                    rPCMessage.read = msg.read;
                                    RPCMessage msg2 = rPCMessage;
                                    RPC_OpenFuel(msg2);
                                }
                            }
                            catch (Exception exception)
                            {
                                Debug.LogException(exception);
                                player.Kick("RPC Error in Custom RPC_OpenFuel");
                            }
                        }

                        return true;
                    }

                    if (rpc == 924237371 && player != null)
                    {
                        Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                        if (ConVar.Global.developer > 2)
                        {
                            Debug.Log("SV_RPCMessage: " + player?.ToString() + " - RPC_OpenItemStorage ");
                        }

                        using (TimeWarning.New(RPC_OPENITEMSTORAGE))
                        {
                            using (TimeWarning.New(RPC_TIMEWARNING_CONDITIONS))
                            {
                                if (!RPC_Server.MaxDistance.Test(924237371u, RPC_OPENITEMSTORAGE, this, player, 3f))
                                {
                                    return true;
                                }
                            }

                            try
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                {
                                    RPCMessage rPCMessage = default(RPCMessage);
                                    rPCMessage.connection = msg.connection;
                                    rPCMessage.player = player;
                                    rPCMessage.read = msg.read;
                                    RPCMessage msg3 = rPCMessage;
                                    RPC_OpenItemStorage(msg3);
                                }
                            }
                            catch (Exception exception2)
                            {
                                Debug.LogException(exception2);
                                player.Kick("RPC Error in Custom RPC_OpenItemStorage");
                            }
                        }

                        return true;
                    }

                    if (rpc == 2115395408 && player != null)
                    {
                        Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                        if (ConVar.Global.developer > 2)
                        {
                            Debug.Log("SV_RPCMessage: " + player?.ToString() + " - RPC_WantsPush ");
                        }

                        using (TimeWarning.New(RPC_WANTSPUSH))
                        {
                            using (TimeWarning.New(RPC_TIMEWARNING_CONDITIONS))
                            {
                                if (!RPC_Server.MaxDistance.Test(2115395408u, RPC_WANTSPUSH, this, player, 5f))
                                {
                                    return true;
                                }
                            }

                            try
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                {
                                    RPCMessage rPCMessage = default(RPCMessage);
                                    rPCMessage.connection = msg.connection;
                                    rPCMessage.player = player;
                                    rPCMessage.read = msg.read;
                                    RPCMessage msg2 = rPCMessage;
                                    RPC_WantsPush(msg2);
                                }
                            }
                            catch (Exception exception)
                            {
                                Debug.LogException(exception);
                                player.Kick("RPC Error in Custom RPC_WantsPush");
                            }
                        }

                        return true;
                    }
                }

                return base.OnRpcMessage(player, rpc, msg);
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);

                info.msg.snowmobile = Facepunch.Pool.Get<ProtoBuf.Snowmobile>();
                //info.msg.snowmobile.steerInput = Vehicle.SteerAngle;
                info.msg.snowmobile.driveWheelVel = Vehicle.DriveWheelVelocity;
                info.msg.snowmobile.throttleInput = Vehicle.GetThrottleInput();
                info.msg.snowmobile.brakeInput = Vehicle.GetBrakeInput();
                //info.msg.snowmobile.fuelFraction = Vehicle.GetFuelFraction(true);
            }
        }

        public class SpecialStaticInstrument : SpecialBaseMountable
        {
            public InstrumentKeyController KeyController;

            public bool ShouldSuppressHandsAnimationLayer;

            public override bool OnRpcMessage(BasePlayer player, uint rpc, Message msg)
            {
                using (TimeWarning.New(STATICINSTRUMENT_ONRPCMESSAGE))
                {
                    if (rpc == 1625188589 && player != null)
                    {
                        Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                        using (TimeWarning.New(RPC_TIMEWARNING_SERVER_PLAYNOTE))
                        {
                            try
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                {
                                    RPCMessage rPCMessage = default(RPCMessage);
                                    rPCMessage.connection = msg.connection;
                                    rPCMessage.player = player;
                                    rPCMessage.read = msg.read;
                                    RPCMessage msg2 = rPCMessage;
                                    Server_PlayNote(msg2);
                                }
                            }
                            catch (Exception exception)
                            {
                                Debug.LogException(exception);
                                player.Kick("RPC Error in Server_PlayNote");
                            }
                        }

                        return true;
                    }

                    if (rpc == 705843933 && player != null)
                    {
                        Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                        using (TimeWarning.New(RPC_TIMEWARNING_SERVER_STOPNOTE))
                        {
                            try
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                {
                                    RPCMessage rPCMessage = default(RPCMessage);
                                    rPCMessage.connection = msg.connection;
                                    rPCMessage.player = player;
                                    rPCMessage.read = msg.read;
                                    RPCMessage msg3 = rPCMessage;
                                    Server_StopNote(msg3);
                                }
                            }
                            catch (Exception exception2)
                            {
                                Debug.LogException(exception2);
                                player.Kick("RPC Error in Server_StopNote");
                            }
                        }

                        return true;
                    }
                }

                return base.OnRpcMessage(player, rpc, msg);
            }

            [RPC_Server]
            private void Server_PlayNote(RPCMessage msg)
            {
                int arg = msg.read.Int32();
                int arg2 = msg.read.Int32();
                int arg3 = msg.read.Int32();
                float arg4 = msg.read.Float();
                KeyController.ProcessServerPlayedNote(GetMounted());
                ClientRPC(RpcTarget.NetworkGroup(RPC_CLIENT_PLAYNOTE), arg, arg2, arg3, arg4);
            }

            [RPC_Server]
            private void Server_StopNote(RPCMessage msg)
            {
                int arg = msg.read.Int32();
                int arg2 = msg.read.Int32();
                int arg3 = msg.read.Int32();
                ClientRPC(RpcTarget.NetworkGroup(RPC_CLIENT_STOPNOTE), arg, arg2, arg3);
            }

            public override bool IsInstrument()
            {
                return true;
            }
        }

        public class SpecialBaseMountable : BaseVehicleSeat
        {
            public IKaruzaCustomPrefab KaruzaEntity { get { return Vehicle; } }
            public IVehicle Vehicle { get; protected set; }
            public bool DestroyParentOnDestroy = true;
            public bool ForceDriver = true;
            public bool CanSwapTo = true;

            protected bool HasVehicle;

            public override bool DirectlyMountable()
            {
                return true;
            }

            public override OBB WorldSpaceBounds()
            {
                if (!HasVehicle)
                {
                    return new OBB(base.transform.position, base.transform.lossyScale, base.transform.rotation, bounds);
                }

                return new OBB(Vehicle.BaseVehicle.transform.position, Vehicle.BaseVehicle.transform.lossyScale, Vehicle.BaseVehicle.transform.rotation, Vehicle.BaseVehicle.bounds);
            }

            public override bool ShouldInheritNetworkGroup()
            {
                return false;
            }

            public override bool CanCompletePickup(BasePlayer player)
            {
                return false;
            }

            public override bool ShouldDisplayPickupOption(BasePlayer player)
            {
                return false;
            }

            public override bool BlocksWaterFor(BasePlayer player)
            {
                return KaruzaEntity.BaseEntity.BlocksWaterFor(player);
            }

            public override float AirFactor()
            {
                return KaruzaEntity.BaseEntity.AirFactor();
            }

            void Awake()
            {
                this.isMobile = true;
            }

            public override float AntiHackVelocity()
            {
                return 50f;
            }

            public override void ServerInit()
            {
                networkEntityScale = true;
                this.isMobile = true;
                ignoreVehicleParent = true;

                this.mountSyncType = MountSyncType.RepositionPerFrame;

                base.ServerInit();

                this.Vehicle = this.GetComponentInParent<IVehicle>();
                HasVehicle = this.Vehicle != null;

                if (HasVehicle)
                {
                    List<Transform> newDismountPoints = new List<Transform>();
                    var vehicle = Vehicle.BaseVehicle;

                    for (int i = 0; i < vehicle.dismountPositions.Length; i++)
                    {
                        newDismountPoints.Add(vehicle.dismountPositions[i]);
                    }

                    newDismountPoints.AddRange(dismountPositions
                        .Where(gdmp => !newDismountPoints.Exists(dmp => dmp.transform.position == gdmp.transform.position)));

                    var newDm = newDismountPoints.ToArray();
                    vehicle.dismountPositions = newDm;
                }
            }

            public override BaseVehicle VehicleParent()
            {
                if (ignoreVehicleParent)
                {
                    return null;
                }

                return Vehicle.BaseVehicle;
            }

            public override void PlayerServerInput(InputState inputState, BasePlayer player)
            {
                Vehicle.PlayerServerInput(inputState, player);
            }

            public override void DestroyShared()
            {
                base.DestroyShared();

                if (DestroyParentOnDestroy && Vehicle != null && !Vehicle.IsDestroyed && !Vehicle.IsDead())
                {
                    Vehicle.BaseEntity.Kill();
                }
            }

            public override void Hurt(HitInfo info)
            {
                this.Vehicle.Hurt(info);
                return;
            }

            public override void OnAttacked(HitInfo info)
            {
                using (TimeWarning.New(BASECOMBATENTITY_ONATTACKED))
                {
                    this.Vehicle.OnAttacked(info);
                    return;
                }
            }

            public override void DoRepair(BasePlayer player)
            {
                this.Vehicle.DoRepair(player);
            }

            public override bool CanSwapToThis(BasePlayer player)
            {
                if (!CanSwapTo)
                {
                    return false;
                }

                return Vehicle.CanSwapToSeat(player, this);
            }

            public override void OnPlayerDismounted(BasePlayer player)
            {
                player.PauseFlyHackDetection(1.5f);
                base.OnPlayerDismounted(player);

                if (giveCrosshair)
                {
                    CrosshairUtilities.HideGUI(player);
                }

                if (Vehicle.VehicleConfig.GForceSettings.Enabled)
                {
                    GForceGUIUtilities.HideBlackoutGUI(player);
                    GForceGUIUtilities.HideRedoutGUI(player);
                }
            }

            public override void AttemptMount(BasePlayer player, bool doMountChecks = true)
            {
                if (!player.IsNpc && !player.IsBot && ForceDriver)
                {
                    this.Vehicle.AttemptMount(player, false);
                }
                else
                {
                    var vehicle = this.Vehicle.BaseVehicle;
                    if (vehicle._mounted != null || !vehicle.MountEligable(player))
                    {
                        return;
                    }

                    if (_mounted != null || IsDead() || !player.CanMountMountablesNow() || IsTransferring() || IsSeatClipping(this) || ClothingBlocksMounting(player))
                    {
                        return;
                    }

                    if (doMountChecks)
                    {
                        if (checkPlayerLosOnMount && UnityEngine.Physics.Linecast(player.eyes.position, mountAnchor.position + base.transform.up * mountLOSVertOffset, out var hitInfo, 1218652417))
                        {
                            bool flag = false;
                            BaseEntity entity = hitInfo.GetEntity();
                            if (entity != null && (entity == this || entity == VehicleParent()))
                            {
                                flag = true;
                            }

                            if (!flag)
                            {
                                Debug.Log("not self, " + entity.name + " vs " + VehicleParent()?.name);
                                Debug.Log(entity.isClient + " vs " + VehicleParent()?.isClient);
                                return;
                            }
                        }

                        if (!HasValidDismountPosition(player))
                        {
                            Debug.Log("no valid dismount");
                            return;
                        }
                    }

                    MountPlayer(player);
                    if (player.GetMountedVehicle() == vehicle)
                    {
                        vehicle.PlayerMounted(player, this);
                    }
                }
            }

            public override void OnPlayerMounted()
            {
                base.OnPlayerMounted();
                if (_mounted.GetMountedVehicle() == Vehicle.BaseVehicle)
                {
                    Vehicle.BaseVehicle.PlayerMounted(_mounted, this);
                }

                if (giveCrosshair)
                {
                    CrosshairUtilities.ShowGUI(_mounted);
                }
            }

            public override bool HasValidDismountPosition(BasePlayer player)
            {
                return Vehicle.BaseVehicle.HasValidDismountPosition(player);
            }

            public override void UpdateNetworkGroup()
            {
                isCallingUpdateNetworkGroup = false;
                if (net == null || Network.Net.sv == null || Network.Net.sv.visibility == null)
                {
                    return;
                }

                using (TimeWarning.New(UPDATE_NETWORKGROUP))
                {
                    var group = BaseNetworkable.GlobalNetworkGroup;
                    if (HasVehicle)
                    {
                        if (Vehicle.BaseVehicle.IsDead() || Vehicle.BaseVehicle.net == null)
                        {
                            return;
                        }

                        group = Vehicle.BaseVehicle.net.group;
                    }

                    if (net.SwitchGroup(group))
                    {
                        if (isSpawned)
                        {
                            if (net.group == null)
                            {
                                Debug.LogWarning(ToString() + " changed its network group to null");
                                return;
                            }

                            NetWrite netWrite = Network.Net.sv.StartWrite();
                            netWrite.PacketID(Message.Type.GroupChange);
                            netWrite.EntityID(net.ID);
                            netWrite.GroupID(net.group.ID);
                            netWrite.Send(new SendInfo(net.group.subscribers));
                        }
                    }
                }
            }

            public override bool ShouldNetworkTo(BasePlayer player)
            {
                if (IsTransferProtected())
                {
                    return false;
                }

                if (limitNetworking)
                {
                    return false;
                }

                if (HasVehicle)
                {
                    return Vehicle.BaseVehicle.ShouldNetworkTo(player);
                }

                if (net.group == null)
                {
                    return true;
                }

                return player.net.subscriber.IsSubscribed(net.group);
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);

                info.msg.baseCombat.health = 100;
                info.msg.baseCombat.maxHealthOverride = 100;
                info.msg.baseCombat.maxHealth = 100;
            }
        }

        public class SpecialSleepingBag : SleepingBag
        {
            public IKaruzaCustomPrefab KaruzaEntity { get { return Vehicle; } }
            // todo decouple
            protected IVehicle Vehicle;
            public bool DestroyParentOnDestroy = true;

            public BaseVehicleSeat AssociatedSeat;
            public bool HasAssociatedSeat;

            public RespawnSettings Config;

            float lowHealthThreshold;

            public override void ServerInit()
            {
                base.ServerInit();
                networkEntityScale = true;
                this.Vehicle = this.GetComponentInParent<IVehicle>();

                if (Config.EnableLowHealthUnlock && Config.LowHealthUnlockThreshold > 0)
                {
                    lowHealthThreshold = Vehicle.VehicleConfig.MaxHealth * Config.LowHealthUnlockThreshold;
                    Vehicle.OnHealthChange += OnVehicleHealthChanged;
                }

                Invoke(UpdateBag, 0.5f);
            }

            public override bool IsMobile()
            {
                return true;
            }

            void UpdateBag()
            {
                var isUnlocked = deployerUserID <= 0 || !Vehicle.HasLock;
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.Reserved3, isUnlocked);
                }
            }

            public override void PostPlayerSpawn(BasePlayer p)
            {
                base.PostPlayerSpawn(p);
                if (HasAssociatedSeat)
                {
                    if (p.IsConnected)
                    {
                        p.EndSleeping();
                    }

                    AssociatedSeat.MountPlayer(p);
                }
            }

            public override RespawnInformation.SpawnOptions.RespawnState GetRespawnState(ulong userID)
            {
                RespawnInformation.SpawnOptions.RespawnState respawnState = base.GetRespawnState(userID);
                if (respawnState != RespawnInformation.SpawnOptions.RespawnState.OK)
                {
                    return respawnState;
                }

                if (HasAssociatedSeat)
                {
                    BasePlayer mounted = AssociatedSeat.GetMounted();
                    if (mounted != null && (ulong)mounted.userID != userID)
                    {
                        return RespawnInformation.SpawnOptions.RespawnState.Occupied;
                    }
                }

                return RespawnInformation.SpawnOptions.RespawnState.OK;
            }


            public override void RPC_MakePublic(RPCMessage msg)
            {
                bool flag = msg.read.Bit();
                if (flag == IsPublic())
                {
                    return;
                }

                if (!Vehicle.HasLock && !flag)
                {
                    return;
                }

                if (!canBePublic || !msg.player.CanInteract() || (deployerUserID != (ulong)msg.player.userID && !msg.player.CanBuild()))
                {
                    return;
                }

                SetPublic(flag);
                if (!IsPublic())
                {
                    if (ConVar.Server.max_sleeping_bags > 0)
                    {
                        CanAssignBedResult? canAssignBedResult = CanAssignBed(msg.player, this, msg.player.userID, 1, 0, this);
                        if (canAssignBedResult.HasValue)
                        {
                            if (canAssignBedResult.Value.Result == BagResultType.Ok)
                            {
                                msg.player.ShowToast(GameTip.Styles.Blue_Long, bagLimitPhrase, false, canAssignBedResult.Value.Count.ToString(), canAssignBedResult.Value.Max.ToString());
                            }
                            else
                            {
                                msg.player.ShowToast(GameTip.Styles.Blue_Long, cannotMakeBedPhrase, false, canAssignBedResult.Value.Count.ToString(), canAssignBedResult.Value.Max.ToString());
                            }

                            if (canAssignBedResult.Value.Result != 0)
                            {
                                return;
                            }
                        }
                    }

                    ulong num = deployerUserID;
                    deployerUserID = msg.player.userID;
                    NotifyPlayer(num);
                    NotifyPlayer(deployerUserID);
                    OnBagChangedOwnership(this, num);
                }

                SendNetworkUpdate();
            }

            public void OnVehicleHealthChanged(float oldValue, float newValue)
            {
                if (newValue <= 0)
                {
                    return;
                }

                var shouldUnlock = lowHealthThreshold >= newValue;
                if (!shouldUnlock)
                {
                    return;
                }

                if (HasFlag(Flags.Reserved3))
                {
                    return;
                }

                RemoveBagForPlayer(this, deployerUserID);
                deployerUserID = 0;

                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.Reserved3, true);
                }
            }

            public override void OnAttacked(HitInfo info)
            {
                using (TimeWarning.New(BASECOMBATENTITY_ONATTACKED))
                {
                    this.Vehicle.BaseEntity.OnAttacked(info);
                    return;
                }
            }

            public override void DestroyShared()
            {
                base.DestroyShared();

                if (DestroyParentOnDestroy && Vehicle != null && !Vehicle.IsDestroyed && !Vehicle.IsDead())
                {
                    Vehicle.BaseVehicle.Kill();
                }
            }

            public override void DoRepair(BasePlayer player)
            {
                this.Vehicle.DoRepair(player);
            }

            public override bool CanBeLooted(BasePlayer player)
            {
                return Vehicle.CanAccess(player);
            }
        }

        public class SpecialDoor : Door
        {
            public IKaruzaCustomPrefab KaruzaEntity { get; protected set; }
            public IKaruzaCustomEntity KaruzaCustomEntity { get; protected set; }

            public bool DestroyParentOnDestroy = true;

            float doorBusyTime = 1.0f;

            public override void ServerInit()
            {
                base.ServerInit();
                this.KaruzaCustomEntity = this.GetComponentInParent<IKaruzaCustomEntity>();
                this.KaruzaEntity = KaruzaCustomEntity;

                canNpcOpen = false;
                isSecurityDoor = true;

                if (this.PrefabName == GARAGE_DOOR_PREFAB)
                {
                    doorBusyTime = 3.5f;
                }
            }

            public override bool OnRpcMessage(BasePlayer player, uint rpc, Message msg)
            {
                using (TimeWarning.New(DOOR_ONRPCMESSAGE))
                {
                    if (rpc == 3999508679u && player != null)
                    {
                        Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                        using (TimeWarning.New(RPC_CLOSEDOOR))
                        {
                            using (TimeWarning.New(RPC_TIMEWARNING_CONDITIONS))
                            {
                                if (!RPC_Server.MaxDistance.Test(3999508679u, RPC_CLOSEDOOR, this, player, 3f))
                                {
                                    return true;
                                }
                            }

                            try
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                {
                                    RPCMessage rPCMessage = default(RPCMessage);
                                    rPCMessage.connection = msg.connection;
                                    rPCMessage.player = player;
                                    rPCMessage.read = msg.read;
                                    RPCMessage rpc2 = rPCMessage;
                                    Custom_RPC_CloseDoor(rpc2);
                                }
                            }
                            catch (Exception exception)
                            {
                                Debug.LogException(exception);
                                player.Kick("RPC Error in Custom RPC_CloseDoor");
                            }
                        }

                        return true;
                    }

                    if (rpc == 3314360565u && player != null)
                    {
                        Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                        using (TimeWarning.New(RPC_OPENDOOR))
                        {
                            using (TimeWarning.New(RPC_TIMEWARNING_CONDITIONS))
                            {
                                if (!RPC_Server.MaxDistance.Test(3314360565u, RPC_OPENDOOR, this, player, 3f))
                                {
                                    return true;
                                }
                            }

                            try
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                {
                                    RPCMessage rPCMessage = default(RPCMessage);
                                    rPCMessage.connection = msg.connection;
                                    rPCMessage.player = player;
                                    rPCMessage.read = msg.read;
                                    RPCMessage rpc4 = rPCMessage;
                                    Custom_RPC_OpenDoor(rpc4);
                                }
                            }
                            catch (Exception exception3)
                            {
                                Debug.LogException(exception3);
                                player.Kick("RPC Error in Custom RPC_OpenDoor");
                            }
                        }

                        return true;
                    }

                    if (rpc == 3000490601u && player != null)
                    {
                        Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                        using (TimeWarning.New(RPC_TOGGLEHATCH))
                        {
                            using (TimeWarning.New(RPC_TIMEWARNING_CONDITIONS))
                            {
                                if (!RPC_Server.MaxDistance.Test(3000490601u, RPC_TOGGLEHATCH, this, player, 3f))
                                {
                                    return true;
                                }
                            }

                            try
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                {
                                    RPCMessage rPCMessage = default(RPCMessage);
                                    rPCMessage.connection = msg.connection;
                                    rPCMessage.player = player;
                                    rPCMessage.read = msg.read;
                                    RPCMessage rpc5 = rPCMessage;
                                    Custom_RPC_ToggleHatch(rpc5);
                                }
                            }
                            catch (Exception exception4)
                            {
                                Debug.LogException(exception4);
                                player.Kick("RPC Error in Custom RPC_ToggleHatch");
                            }
                        }

                        return true;
                    }
                }

                return base.OnRpcMessage(player, rpc, msg);
            }

            private void DoorOpeningNotBusy()
            {
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(BaseEntity.Flags.Busy, false);
                }

                SendNetworkUpdateImmediate();
            }

            [RPC_Server]
            [RPC_Server.MaxDistance(3f)]
            protected void Custom_RPC_OpenDoor(RPCMessage rpc)
            {
                if (!rpc.player.CanInteract(usableWhileCrawling: true) || rpc.player.IsNpc || rpc.player.IsBot || !canHandOpen || IsOpen() || IsBusy() || IsLocked() || IsInvoking(DoorOpeningNotBusy))
                {
                    return;
                }

                if (KaruzaEntity != null && !KaruzaCustomEntity.CanAccess(rpc.player))
                {
                    return;
                }

                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    if (canReverseOpen)
                    {
                        flagsUpdateScope.Set(Flags.Reserved1, base.transform.InverseTransformPoint(rpc.player.transform.position).x > 0f);
                    }

                    flagsUpdateScope.Set(Flags.Open, true);
                    flagsUpdateScope.Set(Flags.Busy, true);
                }

                Invoke(DoorOpeningNotBusy, doorBusyTime);
                SendNetworkUpdateImmediate();

                //if (isSecurityDoor && (UnityEngine.Object)(object)NavMeshLink != null)
                //{
                //    SetNavMeshLinkEnabled(wantsOn: true);
                //}

                //if (checkPhysBoxesOnOpen)
                //{
                //    StartCheckingForBlockages(isOpening: true);
                //}

                OnPlayerOpenedDoor(rpc.player);
            }

            [RPC_Server.MaxDistance(3f)]
            [RPC_Server]
            private void Custom_RPC_CloseDoor(RPCMessage rpc)
            {
                if (!rpc.player.CanInteract(usableWhileCrawling: true) || !canHandOpen || !IsOpen() || IsBusy() || IsLocked())
                {
                    return;
                }

                if (KaruzaCustomEntity != null && !KaruzaCustomEntity.CanAccess(rpc.player))
                {
                    return;
                }

                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.Open, false);
                    flagsUpdateScope.Set(Flags.Busy, true);
                }

                Invoke(DoorOpeningNotBusy, doorBusyTime);
                SendNetworkUpdateImmediate();
            }

            [RPC_Server]
            [RPC_Server.MaxDistance(3f)]
            private void Custom_RPC_ToggleHatch(RPCMessage rpc)
            {
                if (!rpc.player.CanInteract(usableWhileCrawling: true) || !hasHatch)
                {
                    return;
                }

                if (KaruzaCustomEntity != null && !KaruzaCustomEntity.CanAccess(rpc.player))
                {
                    return;
                }

                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.Reserved3, !HasFlag(Flags.Reserved3));
                }
            }

            public void CustomSetOpen(bool open)
            {
                if (open)
                {
                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(Flags.Open, true);
                        flagsUpdateScope.Set(Flags.Busy, true);
                    }

                    Invoke(DoorOpeningNotBusy, doorBusyTime);
                    SendNetworkUpdateImmediate();
                }
                else
                {
                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(Flags.Open, false);
                        flagsUpdateScope.Set(Flags.Busy, true);
                    }

                    Invoke(DoorOpeningNotBusy, doorBusyTime);
                    SendNetworkUpdateImmediate();
                }
            }

            public override void Hurt(HitInfo info)
            {
                this.KaruzaEntity.Hurt(info);
                return;
            }

            public override void OnAttacked(HitInfo info)
            {
                using (TimeWarning.New(BASECOMBATENTITY_ONATTACKED))
                {
                    this.KaruzaEntity.BaseEntity.OnAttacked(info);
                    return;
                }
            }

            public override void DestroyShared()
            {
                base.DestroyShared();

                if (DestroyParentOnDestroy && KaruzaEntity != null && !KaruzaEntity.BaseEntity.IsDestroyed)
                {
                    KaruzaEntity.BaseEntity.Kill();
                }
            }

            public override void DoRepair(BasePlayer player)
            {
                this.KaruzaEntity.DoRepair(player);
            }

            public override bool CanBeLooted(BasePlayer player)
            {
                return KaruzaCustomEntity.CanAccess(player);
            }
        }

        public class PipeProp : SpecialBaseCombatEntity
        {
            public List<PipePropSetting> PipePropSettings = new List<PipePropSetting>();

            public override void Save(SaveInfo info)
            {
                base.Save(info);

                info.msg.ioEntity = Pool.Get<ProtoBuf.IOEntity>();
                info.msg.ioEntity.inputs = Pool.Get<List<ProtoBuf.IOEntity.IOConnection>>();
                info.msg.ioEntity.outputs = Pool.Get<List<ProtoBuf.IOEntity.IOConnection>>();

                ProtoBuf.IOEntity.IOConnection iOConnection = Pool.Get<ProtoBuf.IOEntity.IOConnection>();

                iOConnection.connectedID = this.net.ID;
                iOConnection.connectedToSlot = 1;
                iOConnection.niceName = string.Empty;
                iOConnection.inUse = true;
                info.msg.ioEntity.inputs.Add(iOConnection);

                for (int i = 0; i < PipePropSettings.Count; i++)
                {
                    var pps = PipePropSettings[i];
                    if (pps.LinePoints.Count <= 0)
                    {
                        continue;
                    }

                    var pipeConnection = Pool.Get<ProtoBuf.IOEntity.IOConnection>();
                    //pipeConnection.connectedID = this.net.ID;
                    pipeConnection.niceName = string.Empty;
                    pipeConnection.connectedToSlot = 0;
                    pipeConnection.type = (int)pps.PipeType;
                    pipeConnection.inUse = true;
                    pipeConnection.colour = (int)pps.PipeColor;
                    pipeConnection.worldSpaceRotation = Vector3.up;
                    pipeConnection.lineThickness = pps.PipeThickness;
                    pipeConnection.originPosition = pps.LinePoints[0].Location;
                    pipeConnection.originRotation = pps.OriginRotation;
                    pipeConnection.linePointList = Pool.Get<List<ProtoBuf.IOEntity.IOConnection.LineVec>>();

                    for (int n = 0; n < pps.LinePoints.Count; n++)
                    {
                        var linePoint = pps.LinePoints[n];
                        var lineVec = Pool.Get<ProtoBuf.IOEntity.IOConnection.LineVec>();
                        lineVec.vec = linePoint.Location;
                        pipeConnection.linePointList.Add(lineVec);
                    }

                    info.msg.ioEntity.outputs.Add(pipeConnection);
                }

                if (info.forDisk)
                {
                    return;
                }

                info.msg.decayEntity = Pool.Get<ProtoBuf.DecayEntity>();
                info.msg.decayEntity.buildingID = 0;
            }
        }

        public class TowingAnchor : SpecialBaseCombatEntity
        {
            private IVehicle _vehicle;
            public IVehicle Vehicle
            {
                get { return _vehicle; }
                set
                {
                    KaruzaEntity = _vehicle = value;
                    HasKaruzaEntity = KaruzaEntity != null;
                }
            }

            public BaseEntity Controller;

            private SpecialTowWorldItem parentEnt;

            private BaseCombatEntity connectedEntity;
            private Rigidbody connectedRigidBody;

            private SpringJoint springJoint;
            //private FixedJoint fixedJoint;
            private List<Vector3> linePoints = new List<Vector3>();
            private Rigidbody rigidBody;
            private Vector3 anchorPos;
            private SpecialButton releaseButton;
            private SphereCollider autoConnectTrigger;

            private float ropeLength = 1f;
            private float loadMass = 100f;
            private bool hasTowVehicle;
            private bool retracting;
            private float cachedRopeLength = -1;
            private bool autoConnect;

            public float MinRopeLength = 0f;
            public float MinRopeLengthWhenConnected = 0;
            public float MinRopeLengthComputed { get { return hasTowVehicle ? MinRopeLengthWhenConnected : MinRopeLength; } }
            public float MaxRopeLength = 20f;
            public bool DisplayRope;
            public bool DisplayRopeWhenDisconnected;
            public bool DisplayReleaseButton;
            public float MassScale = 1f;
            public float ConnectedMassScale = 1f;
            public int RopeType = (int)IOEntity.IOType.Industrial;
            public int RopeColor = (int)WireTool.WireColour.Gray;
            public float RopeThickness;
            public bool RequireDriverForAutoConnect = true;

            public Action<float> WinchUpdated;
            public Action<bool> AutoConnectUpdate;
            public Action ConnectChange;

            public override float PositionTickRate => 0.05f;

            public override void Awake()
            {
                base.Awake();
                enabled = true;
            }

            public override void ServerInit()
            {
                base.ServerInit();
                parentEnt = transform.GetComponentInParent<SpecialTowWorldItem>();

                rigidBody = parentEnt.GetComponent<Rigidbody>();

                anchorPos = Controller.transform.InverseTransformPoint(transform.position);

                autoConnectTrigger = gameObject.AddComponent<SphereCollider>();
                autoConnectTrigger.radius = 0.5f;
                autoConnectTrigger.includeLayers = TOW_TRIGGER_AUTO_COL_MASK;
                autoConnectTrigger.isTrigger = true;
                autoConnectTrigger.enabled = false;

                Vehicle.OnPowerToggle += (bool isOn) =>
                {
                    if (!isOn)
                    {
                        ToggleAutoConnect(false);
                    }
                };

                if (DisplayReleaseButton)
                {
                    releaseButton = PropUtilities.CreateCustomEntity<SpecialButton>(new Vector3(0, 0.2f, 0), new Vector3(270, 0, 0), Vector3.one, PREFAB_PRESS_BUTTON, this);
                    releaseButton.limitNetworking = true;
                    releaseButton.PressAction += LocalReleaseButtonPushed;
                    releaseButton.KaruzaEntity = KaruzaEntity;
                    releaseButton.HasKaruzaEntity = true;
                }

                InitializeJoint();
            }

            void InitializeJoint()
            {
                springJoint = Vehicle.BaseEntity.gameObject.AddComponent<SpringJoint>();
                springJoint.autoConfigureConnectedAnchor = false;
                springJoint.connectedBody = rigidBody;
                springJoint.connectedAnchor = new Vector3(0, 0.1f, 0);
                springJoint.anchor = anchorPos;
                springJoint.minDistance = 0;
                springJoint.enableCollision = true;

                springJoint.connectedMassScale = ConnectedMassScale;
                springJoint.massScale = MassScale;

                parentEnt.HasSpringJoint = true;
                parentEnt.parentEntity.Set(Controller);
                parentEnt.SendNetworkUpdateImmediate();

                loadMass = 1;
                ropeLength = MinRopeLengthComputed;
                WinchUpdated?.Invoke(ropeLength);

                UpdateSpring();
                Invoke(UpdateVisualRope, 1.0f);
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);

                info.msg.ioEntity = Pool.Get<ProtoBuf.IOEntity>();
                info.msg.ioEntity.inputs = Pool.Get<List<ProtoBuf.IOEntity.IOConnection>>();
                info.msg.ioEntity.outputs = Pool.Get<List<ProtoBuf.IOEntity.IOConnection>>();

                ProtoBuf.IOEntity.IOConnection iOConnection = Pool.Get<ProtoBuf.IOEntity.IOConnection>();
                if (hasTowVehicle)
                {
                    iOConnection.connectedID = this.net.ID;
                }

                iOConnection.connectedToSlot = 1;
                iOConnection.niceName = hasTowVehicle ? "Tow Vehicle" : "Connect to Vehicle";
                iOConnection.type = RopeType;
                iOConnection.inUse = true;
                iOConnection.colour = RopeColor;
                iOConnection.worldSpaceRotation = Vector3.up;
                iOConnection.lineThickness = RopeThickness;
                iOConnection.originPosition = springJoint.transform.TransformPoint(springJoint.anchor);
                iOConnection.originRotation = Vector3.up;
                info.msg.ioEntity.inputs.Add(iOConnection);

                ProtoBuf.IOEntity.IOConnection iOConnection2 = Pool.Get<ProtoBuf.IOEntity.IOConnection>();
                //iOConnection2.connectedID = this.net.ID;
                iOConnection2.niceName = hasTowVehicle ? "Tow Vehicle" : "Connect to Vehicle";
                iOConnection2.connectedToSlot = 0;
                iOConnection2.type = RopeType;
                iOConnection2.inUse = true;
                iOConnection2.colour = RopeColor;
                iOConnection2.worldSpaceRotation = Vector3.up;
                iOConnection2.lineThickness = RopeThickness;
                iOConnection2.originPosition = springJoint.transform.TransformPoint(springJoint.anchor);
                iOConnection2.originRotation = Vector3.up;
                iOConnection2.linePointList = Pool.Get<List<ProtoBuf.IOEntity.IOConnection.LineVec>>();

                for (int i = 0; i < linePoints.Count; i++)
                {
                    var linePoint = linePoints[i];
                    ProtoBuf.IOEntity.IOConnection.LineVec lineVec = Pool.Get<ProtoBuf.IOEntity.IOConnection.LineVec>();
                    lineVec.vec = CachedTransform.InverseTransformPoint(linePoint);
                    iOConnection2.linePointList.Add(lineVec);
                }

                info.msg.ioEntity.outputs.Add(iOConnection2);

                if (info.forDisk)
                {
                    return;
                }

                info.msg.decayEntity = Pool.Get<ProtoBuf.DecayEntity>();
                info.msg.decayEntity.buildingID = 0;
            }

            public void TryConnectAtPosition(Vector3 worldPos, BasePlayer initiator)
            {
                if (!parentEnt.IsVisibleAndCanSee(worldPos))
                {
                    Instance.ShowToast(initiator, CANT_SEE_TO_TOW_TOAST, GameTip.Styles.Red_Normal);
                    return;
                }

                var colliderList = Pool.Get<List<Collider>>();
                GamePhysics.OverlapSphere(worldPos, 0.2f, colliderList, TOW_TRIGGER_AUTO_COL_MASK, QueryTriggerInteraction.Ignore);
                if (colliderList.Count <= 0)
                {
                    Pool.FreeUnmanaged(ref colliderList);
                    Instance.ShowToast(initiator, NOT_ATTACHED_TO_TOW_TOAST, GameTip.Styles.Red_Normal);

                    var distance = Vector3.Distance(worldPos, springJoint.transform.TransformPoint(springJoint.anchor));
                    ropeLength = Mathf.Round(distance * 10.0f) * 0.1f;
                    WinchUpdated?.Invoke(ropeLength);
                    UpdateSpring();
                    parentEnt.transform.position = worldPos;
                    parentEnt.CustomTransformChanged();
                    retracting = true;
                    Invoke(AutoRetractWinch, 15);
                    return;
                }

                var collider = colliderList[0];
                Pool.FreeUnmanaged(ref colliderList);
                TryConnect(collider, worldPos, initiator);
            }

            private void TryConnect(Collider collider, Vector3 worldPos, BasePlayer initiator, bool manualConnection = true)
            {
                BaseCombatEntity toTow = null;
                Rigidbody toTowRigidbody = null;
                var vehicle = collider.GetComponentInParent<BaseMountable>();
                if (!object.ReferenceEquals(vehicle, null))
                {
                    toTow = vehicle;
                    toTowRigidbody = vehicle.rigidBody;

                    if (Vehicle.BaseEntity.net.ID.Value == vehicle.net.ID.Value)
                    {
                        if (manualConnection)
                        {
                            Instance.ShowToast(initiator, NO_SAME_VEHICLE_TOW_TOAST, GameTip.Styles.Red_Normal);
                        }

                        return;
                    }

                    if (autoConnect && vehicle.AnyMounted())
                    {
                        return;
                    }

                    if (vehicle is BaseVehicleModule bvm)
                    {
                        vehicle = bvm.Vehicle;
                        if (!manualConnection)
                        {
                            var modCar = bvm.Vehicle as ModularCar;
                            if (modCar.CarLock.HasALock && !modCar.PlayerCanUseThis(initiator, ModularCarCodeLock.LockType.General))
                            {
                                return;
                            }
                        }
                    }
                    else if (vehicle is WaterInflatable wi)
                    {
                        wi.additiveDownhillVelocity = 0;
                        transform.up = wi.transform.up;
                        worldPos = wi.transform.position + (wi.transform.forward * 0.1f) + (wi.transform.up * -0.01f);
                    }
                    else if (!(vehicle is BaseVehicle))
                    {
                        Debug.Log("Not Vehicle");

                        Instance.ShowToast(initiator, NO_VEHICLE_TO_TOW_TOAST, GameTip.Styles.Red_Normal);
                        return;
                    }

                    toTow = vehicle;
                    toTowRigidbody = vehicle.rigidBody;
                }
                else
                {
                    toTowRigidbody = collider.GetComponentInParent<Rigidbody>();
                    if (toTowRigidbody != null)
                    {
                        toTow = toTowRigidbody.GetComponent<BaseCombatEntity>();
                    }

                    if (object.ReferenceEquals(toTow, null))
                    {
                        Instance.ShowToast(initiator, NO_VEHICLE_TO_TOW_TOAST, GameTip.Styles.Red_Normal);
                        return;
                    }
                }

                var distance = Vector3.Distance(worldPos, springJoint.transform.TransformPoint(springJoint.anchor));
                if (distance > MaxRopeLength)
                {
                    if (manualConnection)
                    {
                        Instance.ShowToast(initiator, TOO_FAR_TO_TOW_TOAST, GameTip.Styles.Red_Normal, TOO_FAR_TO_TOW_TOAST_DISTANCE_KEY, $"{distance:0.##}");
                    }

                    return;
                }

                connectedEntity = toTow;
                connectedRigidBody = toTowRigidbody;

                if (connectedEntity is IVehicle iVehicle)
                {
                    if (!manualConnection && iVehicle.HasLock && !iVehicle.CanAccess(initiator))
                    {
                        return;
                    }

                    iVehicle.AddTowingVehicle();
                }

                springJoint.connectedBody = connectedRigidBody;
                springJoint.connectedAnchor = connectedEntity.transform.InverseTransformPoint(worldPos);

                if (DisplayReleaseButton)
                {
                    releaseButton.limitNetworking = false;
                }

                CancelInvoke(AutoRetractWinch);

                loadMass = toTowRigidbody.mass;

                ropeLength = (Mathf.Round(distance * 10.0f) * 0.1f) + 0.1f;
                hasTowVehicle = true;
                if (ropeLength < MinRopeLengthComputed)
                {
                    ropeLength = MinRopeLengthComputed;
                }

                WinchUpdated?.Invoke(ropeLength);
                UpdateSpring();

                connectedRigidBody.WakeUp();
                connectedEntity.SendNetworkUpdate();
                parentEnt.SetTowVehicle(connectedEntity, springJoint.connectedAnchor, Quaternion.Inverse(connectedEntity.transform.rotation) * parentEnt.transform.rotation);
                ToggleAutoConnect(false);
                ConnectChange?.Invoke();
            }

            void FixedUpdate()
            {
                if (ropeLength == 0 && cachedRopeLength == 0)
                {
                    return;
                }

                if (!CustomHasChanged)
                {
                    return;
                }

                UpdateVisualRope();
            }

            void LateUpdate()
            {
                if (!hasTowVehicle)
                {
                    return;
                }

                if (connectedEntity.IsDestroyed || connectedEntity.IsDead())
                {
                    DestroyConnection();
                }
            }

            public void DestroyConnection(BasePlayer basePlayer)
            {
                DestroyConnection();
            }

            public void DestroyConnection()
            {
                DestroyConnection(1, 15);
            }

            public void DestroyConnection(float winchDelay, float colliderDelay)
            {
                hasTowVehicle = false;

                if (connectedEntity is IVehicle iVehicle)
                {
                    iVehicle.RemoveTowingVehicle();
                }

                connectedEntity = null;

                springJoint.connectedBody = rigidBody;
                springJoint.connectedAnchor = new Vector3(0, 0.1f, 0);
                rigidBody.WakeUp();

                if (DisplayReleaseButton)
                {
                    releaseButton.limitNetworking = true;
                }

                parentEnt.ResetTowVehicle(colliderDelay);
                loadMass = 1;
                UpdateVisualRope();
                retracting = true;
                Invoke(AutoRetractWinch, winchDelay);
                ConnectChange?.Invoke();
            }

            public void LocalReleaseButtonPushed(BasePlayer basePlayer)
            {
                DestroyConnection(10, 0);
            }

            private void UpdateSpring()
            {
                float density = 7750f;
                float radius = 0.02f;
                float volume = Mathf.PI * radius * radius * ropeLength;
                float ropeMass = volume * density;

                ropeMass += loadMass;

                float ropeForce = ropeMass * 9.81f;
                float kRope = ropeForce / 0.01f;

                springJoint.spring = kRope * 1.0f;
                springJoint.damper = kRope * 0.8f;

                springJoint.maxDistance = ropeLength;
            }

            private void UpdateVisualRope()
            {
                cachedRopeLength = ropeLength;
                if (!DisplayRope)
                {
                    return;
                }

                if (!DisplayRopeWhenDisconnected && !hasTowVehicle && !retracting && ropeLength == 0)
                {
                    if (linePoints.Count > 0)
                    {
                        linePoints.Clear();
                        SendNetworkUpdateImmediate();
                    }

                    return;
                }

                Vector3 A = Controller.transform.TransformPoint(springJoint.anchor);
                //Vector3 A = springJoint.transform.localPosition;
                Vector3 D = springJoint.connectedBody.transform.TransformPoint(springJoint.connectedAnchor);

                var B = A;
                Vector3 C = (D + springJoint.connectedBody.transform.up * (-(A - D).magnitude * 0.05f));
                linePoints.Clear();
                linePoints.Add(A);
                BezierCurve.GetBezierCurve(A, B, C, D, linePoints);

                if (hasTowVehicle)
                {
                    SendNetworkUpdate();
                }
                else
                {
                    SendNetworkUpdateImmediate();
                }
            }

            public bool UpdateWinch(float newRopeLength)
            {
                if (ropeLength == newRopeLength)
                {
                    return false;
                }

                newRopeLength = Mathf.Clamp(newRopeLength, MinRopeLengthComputed, MaxRopeLength);
                if (ropeLength == newRopeLength)
                {
                    return false;
                }

                ropeLength = newRopeLength;
                UpdateSpring();
                WinchUpdated?.Invoke(ropeLength);
                // Do here will be skipped in fixed update
                if (ropeLength == 0)
                {
                    UpdateVisualRope();
                }

                if (rigidBody.IsSleeping())
                {
                    rigidBody.WakeUp();
                }

                if (!hasTowVehicle)
                {
                    if (!autoConnect)
                    {
                        Invoke(AutoRetractWinch, 10f);
                    }
                }
                else if (connectedRigidBody.IsSleeping())
                {
                    connectedRigidBody.WakeUp();
                }

                return true;
            }

            void AutoRetractWinch()
            {
                var newRopeLength = Mathf.Clamp(ropeLength - 1, MinRopeLengthComputed, MaxRopeLength);
                if (ropeLength == newRopeLength)
                {
                    if (retracting)
                    {
                        rigidBody.WakeUp();
                        retracting = false;
                        UpdateVisualRope();
                    }

                    return;
                }

                rigidBody.WakeUp();
                ropeLength = newRopeLength;
                UpdateSpring();
                WinchUpdated?.Invoke(ropeLength);
                Invoke(UpdateVisualRope, 1f);
                Invoke(AutoRetractWinch, 1f);
            }

            void OnJointBreak(float breakForce)
            {
                if (!hasTowVehicle)
                {
                    return;
                }

                if (springJoint == null)
                {
                    CancelInvoke(UpdateVisualRope);
                    CancelInvoke(AutoRetractWinch);
                    retracting = false;

                    hasTowVehicle = false;

                    if (connectedEntity is IVehicle iVehicle)
                    {
                        iVehicle.RemoveTowingVehicle();
                    }

                    connectedEntity = null;
                    connectedRigidBody = null;

                    if (DisplayReleaseButton)
                    {
                        releaseButton.limitNetworking = false;
                    }

                    InitializeJoint();
                    parentEnt.ResetTowVehicle(0);
                    UpdateVisualRope();
                }
            }

            public void ToggleAutoConnect(BasePlayer player, bool isOn)
            {
                var currAutoConnect = autoConnect;
                ToggleAutoConnect(isOn);

                if (HasKaruzaEntity && Vehicle.VehicleConfig.GeneralToastSettings.Enabled && Vehicle.VehicleConfig.GeneralToastSettings.TowAutoConnectSwitchToastsEnabled && currAutoConnect != autoConnect)
                {
                    if (autoConnect && !string.IsNullOrEmpty(Vehicle.VehicleConfig.GeneralToastSettings.TowAutoConnectSwitchOnToast))
                    {
                        Instance.ShowToast(player, Vehicle.VehicleConfig.GeneralToastSettings.TowAutoConnectSwitchOnToast);
                    }
                    else if (!autoConnect && !string.IsNullOrEmpty(Vehicle.VehicleConfig.GeneralToastSettings.TowAutoConnectSwitchOffToast))
                    {
                        Instance.ShowToast(player, Vehicle.VehicleConfig.GeneralToastSettings.TowAutoConnectSwitchOffToast, GameTip.Styles.Red_Normal);
                    }
                }
            }

            public void ToggleAutoConnect(bool isOn)
            {
                if (hasTowVehicle && isOn)
                {
                    isOn = false;
                }

                if (autoConnect == isOn)
                {
                    return;
                }

                autoConnect = isOn;
                autoConnectTrigger.enabled = isOn;
                AutoConnectUpdate?.Invoke(autoConnect);

                if (isOn)
                {
                    CancelInvoke(AutoRetractWinch);
                    retracting = false;
                }
                else if (!hasTowVehicle)
                {
                    Invoke(AutoRetractWinch, 10f);
                }
            }

            void OnTriggerEnter(Collider collider)
            {
                if (!autoConnect)
                {
                    return;
                }

                if (RequireDriverForAutoConnect && !Vehicle.CustomHasDriver())
                {
                    return;
                }

                int num = 1 << collider.gameObject.layer;
                if ((autoConnectTrigger.includeLayers.value & num) != num)
                {
                    Debug.Log("Not interested");
                    return;
                }

                TryConnect(collider, collider.ClosestPoint(CachedTransform.position), Vehicle.CustomGetDriver(), false);
            }

            public override void DestroyShared()
            {
                if (hasTowVehicle)
                {
                    if (connectedEntity is IVehicle iVehicle)
                    {
                        iVehicle.RemoveTowingVehicle();
                    }
                }

                base.DestroyShared();
            }
        }

        public class SpecialBroadcaster : SpecialIOEntity
        {
            private float nextChangeTime;
            public int frequency;

            public override bool OnRpcMessage(BasePlayer player, uint rpc, Message msg)
            {
                if (rpc == 1052196345 && player != null)
                {
                    Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                    if (ConVar.Global.developer > 2)
                    {
                        Debug.Log("SV_RPCMessage: " + player?.ToString() + " - SERVER_RequestOpenPanel ");
                    }

                    using (TimeWarning.New(RPC_SERVER_REQUEST_OPEN_PANEL))
                    {
                        using (TimeWarning.New(RPC_TIMEWARNING_CONDITIONS))
                        {
                            if (!RPC_Server.CallsPerSecond.Test(1052196345u, RPC_SERVER_REQUEST_OPEN_PANEL, this, player, 3uL))
                            {
                                return true;
                            }

                            if (!RPC_Server.IsVisible.Test(1052196345u, RPC_SERVER_REQUEST_OPEN_PANEL, this, player, 3f))
                            {
                                return true;
                            }

                            if (!RPC_Server.MaxDistance.Test(1052196345u, RPC_SERVER_REQUEST_OPEN_PANEL, this, player, 3f))
                            {
                                return true;
                            }
                        }

                        try
                        {
                            using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                            {
                                RPCMessage rPCMessage = default(RPCMessage);
                                rPCMessage.connection = msg.connection;
                                rPCMessage.player = player;
                                rPCMessage.read = msg.read;
                                RPCMessage msg2 = rPCMessage;
                                SERVER_RequestOpenPanel(msg2);
                            }
                        }
                        catch (Exception exception)
                        {
                            Debug.LogException(exception);
                            player.Kick("RPC Error in SERVER_RequestOpenPanel");
                        }
                    }

                    return true;
                }

                using (TimeWarning.New(RFBROADCASTER_ONRPCMESSAGE))
                {
                    if (rpc == 2778616053u && player != null)
                    {
                        Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                        if (ConVar.Global.developer > 2)
                        {
                            Debug.Log("SV_RPCMessage: " + player?.ToString() + " - ServerSetFrequency ");
                        }

                        using (TimeWarning.New(RPC_TIMEWARNING_SERVER_SETFREQUENCY))
                        {
                            using (TimeWarning.New(RPC_TIMEWARNING_CONDITIONS))
                            {
                                if (!RPC_Server.IsVisible.Test(2778616053u, RPC_TIMEWARNING_SERVER_SETFREQUENCY, this, player, 3f))
                                {
                                    return true;
                                }
                            }

                            try
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                {
                                    RPCMessage rPCMessage = default(RPCMessage);
                                    rPCMessage.connection = msg.connection;
                                    rPCMessage.player = player;
                                    rPCMessage.read = msg.read;
                                    RPCMessage msg2 = rPCMessage;
                                    ServerSetFrequency(msg2);
                                }
                            }
                            catch (Exception exception)
                            {
                                Debug.LogException(exception);
                                player.Kick("RPC Error in Custom ServerSetFrequency");
                            }
                        }

                        return true;
                    }
                }

                return base.OnRpcMessage(player, rpc, msg);
            }

            public void SERVER_RequestOpenPanel(RPCMessage msg)
            {
                BasePlayer player = msg.player;
                if (player != null && CanChangeFrequency(player))
                {
                    ClientRPC(RpcTarget.Player("CLIENT_OpenPanel", player), frequency);
                }
            }

            public void ServerSetFrequency(RPCMessage msg)
            {
                if (!CanChangeFrequency(msg.player) || Time.time < nextChangeTime)
                {
                    return;
                }

                nextChangeTime = Time.time + 2f;
                int num = msg.read.Int32();

                frequency = num;
                SendNetworkUpdate();
            }

            private bool CanChangeFrequency(BasePlayer player)
            {
                if (player != null)
                {
                    return player.CanBuild();
                }

                return false;
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);
                if (info.forDisk || CanChangeFrequency(info.forConnection?.player as BasePlayer))
                {
                    info.msg.ioEntity.genericInt1 = frequency;
                }
            }
        }

        public class SpecialIOEntity : SpecialBaseCombatEntity
        {
            public IKaruzaCustomEntity KaruzaCustomEntity;

            public override void Awake()
            {
                base.Awake();
                enabled = false;
            }

            public override void ServerInit()
            {
                base.ServerInit();

                if (HasKaruzaEntity)
                {
                    KaruzaCustomEntity = KaruzaEntity as IKaruzaCustomEntity;
                }

                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.Busy, false);
                }
            }

            public virtual bool IsPowered()
            {
                return KaruzaCustomEntity.IsPowered;
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);
                info.msg.ioEntity = Pool.Get<ProtoBuf.IOEntity>();

                if (info.forDisk)
                {
                    return;
                }

                info.msg.decayEntity = Pool.Get<ProtoBuf.DecayEntity>();
                info.msg.decayEntity.buildingID = 0;
            }
        }

        public class SpecialAudioVisualisationEntity : SpecialIOEntity, ILightToggle
        {
            public EntityRef<BaseEntity> connectedTo;
            public LightColour currentColour { get; set; }
            public VolumeSensitivity currentVolumeSensitivity { get; set; } = VolumeSensitivity.Medium;
            public Speed currentSpeed { get; set; } = Speed.Medium;
            public int currentGradient { get; set; }
            public bool CanVehicleToggle { get; set; } = true;

            public override bool OnRpcMessage(BasePlayer player, uint rpc, Message msg)
            {
                using (TimeWarning.New(RPC_TIMEWARNING_AUDIO_VISUALISATION_ENTITY))
                {
                    if (rpc == 4002266471u && player != null)
                    {
                        Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);

                        using (TimeWarning.New(RPC_TIMEWARNING_SERVER_UPDATE_SETTINGS))
                        {
                            using (TimeWarning.New(RPC_TIMEWARNING_CONDITIONS))
                            {
                                if (!RPC_Server.CallsPerSecond.Test(4002266471u, RPC_TIMEWARNING_SERVER_UPDATE_SETTINGS, this, player, 5uL))
                                {
                                    return true;
                                }

                                if (!RPC_Server.IsVisible.Test(4002266471u, RPC_TIMEWARNING_SERVER_UPDATE_SETTINGS, this, player, 3f))
                                {
                                    return true;
                                }
                            }

                            try
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                {
                                    RPCMessage rPCMessage = default(RPCMessage);
                                    rPCMessage.connection = msg.connection;
                                    rPCMessage.player = player;
                                    rPCMessage.read = msg.read;
                                    RPCMessage msg2 = rPCMessage;
                                    ServerUpdateSettings(msg2);
                                }
                            }
                            catch (Exception exception)
                            {
                                Debug.LogException(exception);
                                player.Kick("RPC Error in Custom_ServerUpdateSettings");
                            }
                        }

                        return true;
                    }
                }

                return base.OnRpcMessage(player, rpc, msg);
            }

            public override void ServerInit()
            {
                base.ServerInit();
                SetAudioSource();
            }

            public void SetAudioSource()
            {
                var audioSource = this.KaruzaEntity.BaseEntity.GetComponentInChildren<DeployableBoomBox>();
                connectedTo.Set(audioSource);
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);
                if (info.msg.connectedSpeaker == null)
                {
                    info.msg.connectedSpeaker = Facepunch.Pool.Get<ProtoBuf.ConnectedSpeaker>();
                }

                info.msg.connectedSpeaker.connectedTo = connectedTo.uid;
                if (info.msg.audioEntity == null)
                {
                    info.msg.audioEntity = Facepunch.Pool.Get<AudioEntity>();
                }

                info.msg.audioEntity.colourMode = (int)currentColour;
                info.msg.audioEntity.volumeRange = (int)currentVolumeSensitivity;
                info.msg.audioEntity.speed = (int)currentSpeed;
                info.msg.audioEntity.gradient = currentGradient;
            }

            [RPC_Server]
            [RPC_Server.CallsPerSecond(5uL)]
            [RPC_Server.IsVisible(3f)]
            public void ServerUpdateSettings(RPCMessage msg)
            {
                int num = msg.read.Int32();
                int num2 = msg.read.Int32();
                int num3 = msg.read.Int32();
                int num4 = msg.read.Int32();
                if (currentColour != (LightColour)num || currentVolumeSensitivity != (VolumeSensitivity)num2 || currentSpeed != (Speed)num3 || currentGradient != num4)
                {
                    currentColour = (LightColour)num;
                    currentVolumeSensitivity = (VolumeSensitivity)num2;
                    currentSpeed = (Speed)num3;
                    currentGradient = num4;
                    SendNetworkUpdate();
                }
            }

            public override void Load(LoadInfo info)
            {
                base.Load(info);
                if (info.msg.audioEntity != null)
                {
                    currentColour = (LightColour)info.msg.audioEntity.colourMode;
                    currentVolumeSensitivity = (VolumeSensitivity)info.msg.audioEntity.volumeRange;
                    currentSpeed = (Speed)info.msg.audioEntity.speed;
                    currentGradient = info.msg.audioEntity.gradient;
                }

                if (info.msg.connectedSpeaker != null)
                {
                    connectedTo.uid = info.msg.connectedSpeaker.connectedTo;
                }
            }

            public void ToggleLights(bool lightsOn)
            {
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.Reserved8, lightsOn);
                }
            }
        }

        public class SpecialGun : SpecialBaseCombatEntity
        {
            protected Flags CachedFlags;

            public virtual Vector3 Forward { get { return CachedTransform.forward; } }

            public override void ServerInit()
            {
                base.ServerInit();
                CachedFlags = this.flags;
            }

            public virtual void ToggleOn()
            {
                SetFlagLocal(Flags.Reserved11, true);
                SetFlagLocal(Flags.Reserved12, true);
                CustomSendNetworkUpdate_Flags();
            }

            public virtual void ToggleOff()
            {
                SetFlagLocal(Flags.Reserved12, false);
                SetFlagLocal(Flags.Reserved11, false);
                if (CachedFlags != this.flags)
                {
                    Invoke(CustomSendNetworkUpdate_Flags, SPECIAL_GUN_REPEAT_DELAY);
                }
            }

            protected void CustomSendNetworkUpdate_Flags()
            {
                if (CachedFlags != this.flags)
                {
                    CachedFlags = this.flags;
                    SendNetworkUpdate_Flags();
                }
            }
        }

        public class SprinklerSpecialGun : SpecialGun
        {
            public override Vector3 Forward { get { return CachedTransform.up; } }

            public override void ToggleOn()
            {
                SetFlagLocal(Flags.On, true);
                CustomSendNetworkUpdate_Flags();
            }

            public override void ToggleOff()
            {
                SetFlagLocal(Flags.On, false);
                if (CachedFlags != this.flags)
                {
                    Invoke(CustomSendNetworkUpdate_Flags, WATER_GUN_REPEAT_DELAY);
                }
            }
        }

        public class SpecialTravellingVendor : SpecialBaseCombatEntity
        {
            bool updateFlags;
            float cachedAngle;
            WheelIsGroundedFlags wheelFlags;
            float nextUpdateTime;
            IVehicle vehicle;

            public override void ServerInit()
            {
                base.ServerInit();
                UpdateWheelFlags();
                InitializeColliders();

                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.On, true);
                }

                vehicle = KaruzaEntity as IVehicle;
                enabled = HasKaruzaEntity;
            }

            void InitializeColliders()
            {
                var mainCollider = gameObject.AddComponent<BoxCollider>();
                mainCollider.center = new Vector3(0, 1.118f, -0.361f);
                mainCollider.size = new Vector3(2, 2.5f, 3.6f);
                mainCollider.isTrigger = false;

                var frontCollider = gameObject.AddComponent<BoxCollider>();
                frontCollider.center = new Vector3(0, 0.246f, 2.079f);
                frontCollider.size = new Vector3(2, 0.8f, 1.0f);
                frontCollider.isTrigger = false;
            }

            void LateUpdate()
            {
                if (!HasKaruzaEntity || KaruzaEntity.BaseEntity.IsOn())
                {
                    return;
                }

                if (Time.time < nextUpdateTime)
                {
                    return;
                }

                nextUpdateTime = Time.time + 15;
                CustomTransformChanged(true);
            }

            void Update()
            {
                if (vehicle.GetBrakeInput() > 0 != HasFlag(TravellingVendor.TravellingVendorFlags.Braking))
                {
                    updateFlags = true;

                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(TravellingVendor.TravellingVendorFlags.Braking, vehicle.GetBrakeInput() > 0);
                    }

                }

                if (KaruzaEntity.BaseEntity.HasFlag(Flags.Reserved5) != HasFlag(TravellingVendor.TravellingVendorFlags.Lights))
                {
                    updateFlags = true;

                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(TravellingVendor.TravellingVendorFlags.Lights, KaruzaEntity.BaseEntity.HasFlag(Flags.Reserved5));
                    }
                }
            }

            void FixedUpdate()
            {
                if (updateFlags || vehicle.SteerAngle != cachedAngle || vehicle.BaseVehicle.rigidBody.linearVelocity.magnitude > 0)
                {
                    cachedAngle = vehicle.SteerAngle;
                    SendNetworkUpdate();
                }
            }

            [Flags]
            private enum WheelIsGroundedFlags
            {
                RearLeft = 1,
                RearRight = 2,
                FrontLeft = 4,
                FrontRight = 8
            }

            private void UpdateWheelFlags()
            {
                wheelFlags |= WheelIsGroundedFlags.FrontLeft;
                wheelFlags |= WheelIsGroundedFlags.FrontRight;
                wheelFlags |= WheelIsGroundedFlags.RearLeft;
                wheelFlags |= WheelIsGroundedFlags.RearRight;
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);

                if (!HasKaruzaEntity)
                {
                    return;
                }

                info.msg.travellingVendor = Pool.Get<ProtoBuf.TravellingVendor>();
                info.msg.travellingVendor.steeringAngle = vehicle.SteerAngle;
                info.msg.travellingVendor.velocity = vehicle.BaseVehicle.rigidBody.linearVelocity;
                info.msg.travellingVendor.wheelFlags = (int)wheelFlags;
            }
        }

        public class SpecialHotAirBaloon : SpecialBaseCombatEntity
        {
            public override void Save(SaveInfo info)
            {
                base.Save(info);

                info.msg.hotAirBalloon = Facepunch.Pool.Get<ProtoBuf.HotAirBalloon>();
                info.msg.hotAirBalloon.inflationAmount = 1;
            }
        }

        public class SpecialSedan : SpecialBaseCombatEntity
        {
            bool updateFlags;
            bool isRailSedan = false;
            IVehicle vehicle;

            public override void ServerInit()
            {
                base.ServerInit();

                var prefabName = StringPool.Get(prefabID);
                if (prefabName == SEDAN_RAIL_PREFAB)
                {
                    isRailSedan = true;
                }

                vehicle = KaruzaEntity as IVehicle;

                enabled = true;
            }

            public void LateUpdate()
            {
                var lightFlag = Flags.Reserved2;
                if (isRailSedan)
                {
                    lightFlag = Flags.Reserved5;
                }

                if (KaruzaEntity.BaseEntity.HasFlag(Flags.Reserved5) != HasFlag(lightFlag))
                {
                    updateFlags = true;

                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(lightFlag, KaruzaEntity.BaseEntity.HasFlag(Flags.Reserved5));
                    }
                }

                var isOn = KaruzaEntity.BaseEntity.HasFlag(Flags.On);
                var brakeInput = vehicle.GetBrakeInput();
                var showBrakes = brakeInput > 0 && isOn;

                if (showBrakes != HasFlag(Flags.Reserved3))
                {
                    updateFlags = true;
                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(Flags.Reserved3, showBrakes);
                    }
                }

                if (!isRailSedan)
                {
                    updateFlags = true;
                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(Flags.Reserved4, vehicle.SteerAngle < -2f);
                        flagsUpdateScope.Set(Flags.Reserved5, vehicle.SteerAngle > 2f);
                    }
                }

                if (isOn != HasFlag(Flags.Reserved1))
                {
                    updateFlags = true;
                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(Flags.Reserved1, isOn);
                    }
                }

                if (isOn)
                {
                    SendNetworkUpdate();
                }
                else if (updateFlags)
                {
                    updateFlags = false;
                    SendNetworkUpdate_Flags();
                }
            }

            //public override void Save(SaveInfo info)
            //{
            //    base.Save(info);

            //    if (!hasVehicle)
            //    {
            //        return;
            //    }

            //    info.msg.car = Facepunch.Pool.Get<ProtoBuf.TravellingVendor>();
            //    info.msg.travellingVendor.steeringAngle = Vehicle.SteerAngle;
            //    info.msg.travellingVendor.linearVelocity = Vehicle.Rigidbody.linearVelocity;
            //    info.msg.travellingVendor.wheelFlags = (int)wheelFlags;
            //}
        }

        public class SpecialGib : SpecialBaseCombatEntity
        {
            public string GibName;
            public bool DetectCollisions;
            public override float PositionTickRate => 0.1f;
            public override bool PositionTickFixedTime => true;

            bool forceNetworkPositionTick;

            public override bool ShouldUpdateNetworkPosition()
            {
                return syncPosition || forceNetworkPositionTick;
            }

            public override void ServerInit()
            {
                KaruzaEntity = this.GetComponentInParent<IKaruzaCustomPrefab>();
                if (object.ReferenceEquals(KaruzaEntity, null) && !object.ReferenceEquals(CachedTransform.parent, null))
                {
                    KaruzaEntity = CachedTransform.parent.GetComponentInParent<IKaruzaCustomPrefab>();
                }

                HasKaruzaEntity = this.KaruzaEntity != null;

                if (HasKaruzaEntity)
                {
                    // If body is world item subscribe to transform changes
                    if (KaruzaEntity.BodyType == BodyType.WorldItem)
                    {
                        syncPosition = true;
                        KaruzaEntity.OnPhysicsUpdate += CustomTransformChanged;
                    }
                    // Is gib propeller
                    else if (!object.ReferenceEquals(this.GetComponent<VehiclePropeller>(), null))
                    {
                        syncPosition = true;
                    }
                    // Is gib parent, or parent's parent, a propeller wheel or landing gear controller
                    else
                    {
                        var jc = CachedTransform.parent.GetComponent<JointController>();
                        if (!object.ReferenceEquals(jc, null))
                        {
                            syncPosition = false;
                            KaruzaEntity.OnPhysicsUpdate += CustomTransformChangedGibBody;
                            jc.OnJointUpdate += SendNetworkUpdate_Position;

                            InvokeRandomized(CustomNetworkPositionTickGibBody, 10f, 10f - 10f * 0.05f, 10f * 0.05f);
                        }
                        else if (
                            !object.ReferenceEquals(CachedTransform.parent.GetComponent<SpecialWorldItem>(), null)
                            || !object.ReferenceEquals(CachedTransform.parent.GetComponent<VehiclePropeller>(), null)
                            || !object.ReferenceEquals(CachedTransform.parent.GetComponent<JointController>(), null)
                            || (!object.ReferenceEquals(CachedTransform.parent.parent, null)
                                && !object.ReferenceEquals(CachedTransform.parent.parent.GetComponent<VehiclePropeller>(), null)))
                        {
                            syncPosition = false;
                            KaruzaEntity.OnPhysicsUpdate += CustomTransformChangedGibBody;
                            InvokeRandomized(CustomNetworkPositionTickGibBody, 10f, 10f - 10f * 0.05f, 10f * 0.05f);
                        }
                        else
                        {
                            // why do we do this
                            syncPosition = false;
                            KaruzaEntity.OnPhysicsUpdate += CustomTransformChangedGibBody;
                            InvokeRandomized(CustomNetworkPositionTickGibBody, 30f, 30f - 30f * 0.05f, 30f * 0.05f);
                        }
                    }
                }
                else
                {
                    syncPosition = true;
                }

                base.ServerInit();
                PhysicsInit();
            }

            public virtual void CustomNetworkPositionTickGibBody()
            {
                if (!forceNetworkPositionTick)
                {
                    return;
                }

                CustomTransformChanged(!globalBroadcast);
                forceNetworkPositionTick = false;
            }

            public void CustomTransformChangedGibBody()
            {
                forceNetworkPositionTick = true;
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);

                if (info.forDisk)
                {
                    return;
                }

                info.msg.servergib = Facepunch.Pool.Get<ProtoBuf.ServerGib>();
                info.msg.servergib.gibName = GibName;
            }

            public void PhysicsInit()
            {
                if (DetectCollisions)
                {
                    var mesh = Instance.CachedMeshes[GibName];

                    var meshCollider = gameObject.AddComponent<MeshCollider>();
                    meshCollider.sharedMesh = mesh;
                    meshCollider.convex = true;
                    meshCollider.excludeLayers = IGNORE_COL_MASK;
                }
            }
        }

        public class SpecialWorldItem : SpecialBaseCombatEntity
        {
            public override string DefaultClientsideFullPrefabName() => PREFAB_WORLD;
            public int ItemId { get; set; }
            public override float PositionTickRate { get { return syncPosition ? 0.1f : 5; } }

            float nextUpdateTime;
            // for editor
            bool hasVehicle;
            bool engineOn;
            bool hasMounted;
            public IVehicle Vehicle;


            public override void Save(SaveInfo info)
            {
                base.Save(info);

                if (info.forDisk)
                {
                    return;
                }

                info.msg.worldItem = Facepunch.Pool.Get<ProtoBuf.WorldItem>();
                info.msg.worldItem.item = Pool.Get<ProtoBuf.Item>();
                info.msg.worldItem.item.itemid = this.ItemId;
                info.msg.worldItem.item.name = " ";
            }

            public override void ServerInit()
            {
                ForceNetworkGroup = true;

                var setNetworkUpdates = false;
                if (syncPosition)
                {
                    setNetworkUpdates = true;
                    syncPosition = false;
                }

                base.ServerInit();
                Vehicle = KaruzaEntity as IVehicle;
                hasVehicle = Vehicle != null;

                syncPosition = setNetworkUpdates;
                if (PositionTickFixedTime)
                {
                    InvokeRepeatingFixedTime(WorldItemCustomTransformChanged);
                }
                else
                {
                    InvokeRandomized(WorldItemCustomTransformChanged, PositionTickRate, PositionTickRate - PositionTickRate * 0.05f, PositionTickRate * 0.05f);
                }

                if (hasVehicle)
                {
                    Vehicle.OnToggled += OnEngineToggle;
                    Vehicle.OnMountedChange += OnMountedChange;
                }
            }

            public void OnEngineToggle(bool newState)
            {
                engineOn = newState;
            }

            public void OnMountedChange()
            {
                hasMounted = Vehicle.BaseVehicle.AnyMounted();
            }

            public virtual void WorldItemCustomTransformChanged()
            {
                if (!syncPosition || (hasVehicle && !engineOn && !hasMounted && !CustomHasChanged))
                {
                    var time = Time.time;
                    if (time < nextUpdateTime)
                    {
                        return;
                    }

                    nextUpdateTime = time + 15;
                }

                CustomTransformChanged(true);
            }
        }

        public class SpecialTowWorldItem : SpecialWorldItem
        {
            public bool HasTowVehicle;
            public Vector3 TowVehicleLocalPosition;
            public Quaternion TowVehicleLocalRotation;
            public BaseCombatEntity TowVehicle;
            public bool HasSpringJoint;
            public BaseEntity CachedParent;

            Rigidbody rigidBody;
            bool networkUpdateRequired;
            SphereCollider sphereCollider;

            public override void Awake()
            {
                base.Awake();
                enabled = true;

                rigidBody = this.gameObject.GetOrAddComponent<Rigidbody>();
                rigidBody.mass = 1;
                rigidBody.linearDamping = 1f;
                rigidBody.angularDamping = 5f;
                rigidBody.sleepThreshold = 0.25f;
            }

            public override void ServerInit()
            {
                base.ServerInit();

                ForceNetworkGroup = false;

                sphereCollider = gameObject.AddComponent<SphereCollider>();
                sphereCollider.radius = 0.1f;
                sphereCollider.gameObject.layer = (int)Layer.Default;
                sphereCollider.excludeLayers = TOW_TRIGGER_IGNORE_COL_MASK;
            }

            public override Vector3 GetNetworkPosition()
            {
                if (HasTowVehicle)
                {
                    return TowVehicleLocalPosition;
                }

                if (HasSpringJoint)
                {
                    return CachedParent.transform.InverseTransformPoint(CachedTransform.position);
                }

                return base.GetNetworkPosition();
            }

            public override Quaternion GetNetworkRotation()
            {
                if (HasTowVehicle)
                {
                    return TowVehicleLocalRotation;
                }

                if (HasSpringJoint)
                {
                    return Quaternion.Inverse(CachedParent.transform.rotation) * CachedTransform.rotation;
                }

                return base.GetNetworkRotation();
            }

            void FixedUpdate()
            {
                if (!HasTowVehicle)
                {
                    return;
                }

                CachedTransform.position = TowVehicle.transform.TransformPoint(TowVehicleLocalPosition);
                CachedTransform.rotation = TowVehicle.transform.rotation * TowVehicleLocalRotation;
            }

            public void ResetTowVehicle(float colliderDelay)
            {
                TowVehicle = null;
                HasTowVehicle = false;
                parentEntity.Set(CachedParent);
                rigidBody.isKinematic = false;
                SendNetworkUpdateImmediate();
                Invoke(CustomTransformChanged, 0.05f);
                CancelInvoke(UpdateTowVehicle);

                if (colliderDelay > 0)
                {
                    Invoke(ResetCollider, colliderDelay);
                }
                else
                {
                    ResetCollider();
                }
            }

            void ResetCollider()
            {
                sphereCollider.enabled = true;
            }

            public void SetTowVehicle(BaseCombatEntity towVehicle, Vector3 localPosition, Quaternion localRotation)
            {
                CancelInvoke(ResetCollider);
                TowVehicle = towVehicle;
                TowVehicleLocalPosition = localPosition;
                TowVehicleLocalRotation = localRotation;
                CachedTransform.position = towVehicle.transform.TransformPoint(localPosition);
                rigidBody.isKinematic = true;
                sphereCollider.enabled = false;
                CustomTransformChanged();
                Invoke(UpdateTowVehicle, 0.05f);
            }

            void UpdateTowVehicle()
            {
                HasTowVehicle = true;
                parentEntity.Set(TowVehicle);
                networkUpdateRequired = true;
                SendNetworkUpdateImmediate();
                Invoke(CustomTransformChanged, 0.05f);
            }

            public override void WorldItemCustomTransformChanged()
            {
                if (HasTowVehicle)
                {
                    return;
                }

                CustomTransformChanged(true);
            }
        }

        public class SpecialModularCar : SpecialBaseCombatEntity
        {
            float cachedAngle;
            float cachedBrakeInput;
            float cachedThrottleInput;
            float nextUpdateTime;
            static NetworkableId storageId = default(NetworkableId);

            bool hasVehicle;
            bool engineOn;

            IVehicle vehicle;

            static readonly Dictionary<int, Vector3> Sockets = new Dictionary<int, Vector3>()
                {
                    { 0, new Vector3(0.08f, 0.70f, 2.25f) },
                    { 1, new Vector3(0.02f, 0.70f, 2.25f) },
                    { 2, new Vector3(-0.03f, 0.70f, 2.25f) },
                    { 3, new Vector3(-0.09f, 0.70f, 2.25f) },
                };

            public override void ServerInit()
            {
                if (syncPosition)
                {
                    syncPosition = false;
                }

                base.ServerInit();
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.Busy, false);
                }

                vehicle = KaruzaEntity as IVehicle;
                hasVehicle = vehicle != null;
                enabled = false;
                if (hasVehicle)
                {
                    vehicle.OnToggled += OnEngineToggle;
                    enabled = true;
                }

                InvokeRandomized(ModularCarCustomTransformChanged, PositionTickRate, PositionTickRate - PositionTickRate * 0.05f, PositionTickRate * 0.05f);
            }

            public override bool OnRpcMessage(BasePlayer player, uint rpc, Message msg)
            {
                using (TimeWarning.New(MODULARCAR_ONRPCMESSAGE))
                {
                    if (rpc == 1851540757 && player != null)
                    {
                        Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                        if (ConVar.Global.developer > 2)
                        {
                            Debug.Log("SV_RPCMessage: " + player?.ToString() + " - RPC_OpenFuel ");
                        }

                        using (TimeWarning.New(RPC_OPENFUEL))
                        {
                            try
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                {
                                    RPCMessage rPCMessage = default(RPCMessage);
                                    rPCMessage.connection = msg.connection;
                                    rPCMessage.player = player;
                                    rPCMessage.read = msg.read;
                                    RPCMessage msg2 = rPCMessage;
                                    RPC_OpenFuel(msg2);
                                }
                            }
                            catch (Exception exception)
                            {
                                Debug.LogException(exception);
                                player.Kick("RPC Error in Custom RPC_OpenFuel");
                            }
                        }

                        return true;
                    }
                }

                return base.OnRpcMessage(player, rpc, msg);
            }

            public void RPC_OpenFuel(RPCMessage msg)
            {
                BasePlayer player = msg.player;
                vehicle.LootFuel(player);
            }

            private void FixedUpdate()
            {
                var throttleInput = vehicle.GetThrottleInput();
                var brakeInput = vehicle.GetBrakeInput();
                var steerAngle = vehicle.SteerAngle;
                if (steerAngle != cachedAngle || throttleInput != cachedThrottleInput || brakeInput != cachedBrakeInput)
                {
                    cachedThrottleInput = throttleInput;
                    cachedBrakeInput = brakeInput;
                    cachedAngle = steerAngle;
                    SendNetworkUpdate();
                }
            }

            void OnEngineToggle(bool newState)
            {
                engineOn = newState;
                enabled = engineOn;

                if (!enabled)
                {
                    var throttleInput = 0;
                    var brakeInput = 0;
                    if (throttleInput != cachedThrottleInput || brakeInput != cachedBrakeInput)
                    {
                        cachedThrottleInput = throttleInput;
                        cachedBrakeInput = brakeInput;
                        SendNetworkUpdate();
                    }
                }
            }

            private void ModularCarCustomTransformChanged()
            {
                if (!hasVehicle || engineOn)
                {
                    return;
                }

                if (Time.time < nextUpdateTime)
                {
                    return;
                }

                nextUpdateTime = Time.time + 15;
                CustomTransformChanged(true);
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);
                info.msg.modularCar = Facepunch.Pool.Get<ProtoBuf.ModularCar>();
                info.msg.modularCar.driveWheelVel = 0;
                info.msg.modularCar.fuelStorageID = storageId;

                if (hasVehicle)
                {
                    info.msg.modularCar.steerAngle = cachedAngle;
                    info.msg.modularCar.throttleInput = cachedThrottleInput;
                    info.msg.modularCar.brakeInput = cachedBrakeInput;
                }
            }

            public void TryAddModule(Item moduleItem, int socket)
            {
                ItemModVehicleModule component = moduleItem.info.GetComponent<ItemModVehicleModule>();
                if (component == null)
                {
                    return;
                }

                var itemModVehicle = moduleItem.info.GetComponent<ItemModVehicleModule>();

                var position = Sockets[socket];

                var newGO = GameManager.server.CreatePrefab(itemModVehicle.entityPrefab.resourcePath, position, Quaternion.Euler(Vector3.zero));
                var iterateOver = newGO.GetComponentsInChildren<Collider>().ToArray();
                for (var i = 0; i < iterateOver.Length; i++)
                {
                    var compo = iterateOver[i];
                    DestroyImmediate(compo);
                }

                DestroyImmediate(newGO.GetComponent<Rigidbody>());

                BaseEntity baseEntity = newGO.GetComponent<BaseEntity>();
                baseEntity.SetParent(this);
                baseEntity.Spawn();

                var baseVehicleModule = baseEntity.GetComponent<VehicleModuleEngine>();
                baseVehicleModule.AssociatedItemInstance = moduleItem;

                var es = (baseVehicleModule.GetContainer() as Rust.Modular.EngineStorage);
                es.dropsLoot = false;

                baseVehicleModule.AdminFixUp(3);
            }
        }

        public class SpecialHelicopter : VehicleMount
        {
            public bool IsShrunk;

            public override void ServerInit()
            {
                base.ServerInit();
                InvokeRandomized(CustomUpdateNetwork, 0f, 0.2f, 0.05f);
                enabled = false;
            }

            public override bool OnRpcMessage(BasePlayer player, uint rpc, Message msg)
            {
                if (!IsShrunk)
                {
                    using (TimeWarning.New(PLAYERHELICOPTER_ONRPCMESSAGE))
                    {
                        if (rpc == 2115395408 && player != null)
                        {
                            Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                            if (ConVar.Global.developer > 2)
                            {
                                Debug.Log("SV_RPCMessage: " + player?.ToString() + " - RPC_WantsPush ");
                            }

                            using (TimeWarning.New(RPC_WANTSPUSH))
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CONDITIONS))
                                {
                                    if (!RPC_Server.MaxDistance.Test(2115395408u, RPC_WANTSPUSH, this, player, 5f))
                                    {
                                        return true;
                                    }
                                }

                                try
                                {
                                    using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                    {
                                        RPCMessage rPCMessage = default(RPCMessage);
                                        rPCMessage.connection = msg.connection;
                                        rPCMessage.player = player;
                                        rPCMessage.read = msg.read;
                                        RPCMessage msg2 = rPCMessage;
                                        RPC_WantsPush(msg2);
                                    }
                                }
                                catch (Exception exception)
                                {
                                    Debug.LogException(exception);
                                    player.Kick("RPC Error in Custom RPC_WantsPush");
                                }
                            }

                            return true;
                        }

                        if (rpc == 1851540757 && player != null)
                        {
                            Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                            if (ConVar.Global.developer > 2)
                            {
                                Debug.Log("SV_RPCMessage: " + player?.ToString() + " - RPC_OpenFuel ");
                            }

                            using (TimeWarning.New(RPC_OPENFUEL))
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CONDITIONS))
                                {
                                    if (!RPC_Server.IsVisible.Test(1851540757u, RPC_OPENFUEL, this, player, 6f))
                                    {
                                        return true;
                                    }
                                }

                                try
                                {
                                    using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                    {
                                        RPCMessage rPCMessage = default(RPCMessage);
                                        rPCMessage.connection = msg.connection;
                                        rPCMessage.player = player;
                                        rPCMessage.read = msg.read;
                                        RPCMessage msg2 = rPCMessage;
                                        RPC_OpenFuel(msg2);
                                    }
                                }
                                catch (Exception exception)
                                {
                                    Debug.LogException(exception);
                                    player.Kick("RPC Error in Custom RPC_OpenFuel");
                                }
                            }

                            return true;
                        }
                    }
                }

                return base.OnRpcMessage(player, rpc, msg);
            }

            public void RPC_WantsPush(RPCMessage msg)
            {
                BasePlayer player = msg.player;
                if (!player.isMounted && Vehicle.BaseVehicle.CanPushNow(player) && (!Vehicle.BaseVehicle.OnlyOwnerAccessible() || player == creatorEntity))
                {
                    player.metabolism.calories.Subtract(3f);
                    player.metabolism.SendChanges();
                    if (Vehicle.BaseVehicle.rigidBody.IsSleeping())
                    {
                        Vehicle.BaseVehicle.rigidBody.WakeUp();
                    }

                    Vehicle.BaseVehicle.DoPushAction(player);
                    Vehicle.BaseVehicle.timeSinceLastPush = 0f;
                }
            }

            public void RPC_OpenFuel(RPCMessage msg)
            {
                BasePlayer player = msg.player;
                Vehicle.LootFuel(player);
            }

            private void CustomUpdateNetwork()
            {
                if (!IsShrunk)
                {
                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(Flags.Reserved5, Vehicle.BaseVehicle.HasFlag(Flags.Reserved5));
                        flagsUpdateScope.Set(Flags.On, Vehicle.IsStartingUp || Vehicle.BaseEntity.IsOn());
                    }
                }

                if (Vehicle.BaseEntity.IsOn())
                {
                    SendNetworkUpdate();
                }
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);
                info.msg.miniCopter = Facepunch.Pool.Get<ProtoBuf.Minicopter>();
                info.msg.miniCopter.fuelFraction = Vehicle.GetFuelFraction(force: true);
            }
        }

        public class SpecialHuntingTrophy : SpecialBaseCombatEntity
        {
            uint trophyId;

            public override void ServerInit()
            {
                trophyId = (uint)skinID;

                base.ServerInit();
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);
                if (skinID > 0)
                {
                    info.msg.headData = Facepunch.Pool.Get<HeadData>();
                    info.msg.headData.entitySource = trophyId;
                    info.msg.headData.count = 1;
                }
            }
        }

        public class SpecialCargoShip : SpecialBaseCombatEntity
        {
            public uint layoutId;

            public override void ServerInit()
            {
                layoutId = (uint)skinID;

                base.ServerInit();
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);
                info.msg.cargoShip = Pool.Get<ProtoBuf.CargoShip>();
                info.msg.cargoShip.layout = layoutId;
            }
        }

        public class SpecialBradley : SpecialBaseCombatEntity
        {
            bool updateQueued = false;
            bool hasVehicle;
            IVehicle vehicle;
            static Vector3 PROTO_ROT = new Vector3(0.0001f, 0.0001f, 0.0001f);

            public override float PositionTickRate => 10f;

            public override void ServerInit()
            {
                base.ServerInit();

                if (syncPosition)
                {
                    ForceNetworkGroup = true;
                    KaruzaEntity.OnPhysicsUpdate += QueueNetworkUpdate;
                }

                vehicle = KaruzaEntity as IVehicle;
                hasVehicle = vehicle != null;
            }

            void QueueNetworkUpdate()
            {
                updateQueued = true;
            }

            public override void CustomNetworkPositionTick()
            {
                if (!updateQueued)
                {
                    return;
                }

                CustomTransformChanged(true);
                updateQueued = false;
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);

                info.msg.bradley = Pool.Get<ProtoBuf.BradleyAPC>();

                if (hasVehicle)
                {
                    info.msg.bradley.engineThrottle = vehicle.GetThrottleInput();
                    info.msg.bradley.throttleLeft = info.msg.bradley.engineThrottle;
                    info.msg.bradley.throttleRight = info.msg.bradley.engineThrottle;
                }

                info.msg.bradley.mainGunVec = Vector3.zero;
                info.msg.bradley.topTurretVec = Vector3.zero;
            }
        }

        public class SpecialJet : SpecialBaseCombatEntity
        {
            bool updateQueued = false;

            public override float PositionTickRate => 10f;

            public override void ServerInit()
            {
                base.ServerInit();

                if (syncPosition)
                {
                    ForceNetworkGroup = true;
                    KaruzaEntity.OnPhysicsUpdate += QueueNetworkUpdate;
                }
            }

            void QueueNetworkUpdate()
            {
                updateQueued = true;
            }

            public override void CustomNetworkPositionTick()
            {
                if (!updateQueued)
                {
                    return;
                }

                CustomTransformChanged(true);
                updateQueued = false;
            }
        }

        public class SpecialSiren : SpecialBaseCombatEntity
        {
            int toneIndex;
            bool sirenOn;
            public List<SirenTone> Tones = new List<SirenTone>();

            public void ToggleSiren(bool isOn)
            {
                sirenOn = isOn;
                PlaySiren();
            }

            void PlaySiren()
            {
                if (!sirenOn)
                {
                    return;
                }

                if (toneIndex >= Tones.Count)
                {
                    toneIndex = 0;
                }

                var sirenTone = Tones[toneIndex];

                this.ClientRPC(
                    RpcTarget.NetworkGroup(RPC_CLIENT_PLAYNOTE),
                    (int)sirenTone.Note,
                    (int)sirenTone.NoteType,
                    sirenTone.Octave,
                    1f
                );

                Invoke(() => this.ClientRPC(
                    RpcTarget.NetworkGroup(RPC_CLIENT_STOPNOTE),
                    (int)sirenTone.Note,
                    (int)sirenTone.NoteType,
                    sirenTone.Octave),
                    sirenTone.Duration - 0.1f
                );

                toneIndex++;
                if (toneIndex >= Tones.Count)
                {
                    toneIndex = 0;
                }

                Invoke(PlaySiren, sirenTone.Duration);
            }
        }

        #endregion

        #region Engines

        public class SpecialFakeEngine : SpecialBaseCombatEntity
        {
            protected IVehicle Vehicle;

            public bool LimitNetworkingWhenOff;

            public override void ServerInit()
            {
                base.ServerInit();
                Vehicle = KaruzaEntity as IVehicle;
            }

            public virtual void UpdateNetwork()
            {
                if (enabled)
                {
                    return;
                }

                Flags flags = base.flags;
                if (HasFlag(Flags.On))
                {
                    SendNetworkUpdate();
                }
                else if (flags != base.flags)
                {
                    SendNetworkUpdate_Flags();
                }
            }

            public override void OnFlagsChanged(Flags old, Flags next)
            {
                if (!LimitNetworkingWhenOff)
                {
                    return;
                }

                if (!HasFlag(Flags.On))
                {
                    Invoke(DolimitNetworking, 3.5f);
                }
                else
                {
                    limitNetworking = false;
                    CancelInvoke(DolimitNetworking);
                }
            }

            protected void DolimitNetworking()
            {
                limitNetworking = true;
            }
        }

        public class SpecialFakeIOEngine : SpecialFakeEngine
        {
            public override void OnFlagsChanged(Flags old, Flags next)
            {
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.Reserved8, HasFlag(Flags.On));
                }
            }
        }

        public class SpecialFakeDroneEngine : SpecialFakeEngine
        {
            bool updateQueued = false;

            public override void ServerInit()
            {
                base.ServerInit();

                if (syncPosition)
                {
                    ForceNetworkGroup = true;
                    KaruzaEntity.OnPhysicsUpdate += QueueNetworkUpdate;
                }
            }

            void QueueNetworkUpdate()
            {
                updateQueued = true;
            }

            public override void CustomNetworkPositionTick()
            {
                if (!updateQueued)
                {
                    return;
                }

                CustomTransformChanged(true);
            }

            public override void OnFlagsChanged(Flags old, Flags next)
            {
                SetFlagLocal(Flags.Reserved1, HasFlag(Flags.On));
            }
        }

        public class SpecialFakePropEngine : SpecialFakeEngine
        {
            public override void ServerInit()
            {
                base.ServerInit();
                this.LimitNetworkingWhenOff = true;
                this.limitNetworking = true;
            }
        }

        public class SpecialSnowmobileEngine : SpecialFakeEngine
        {
            public override void ServerInit()
            {
                base.ServerInit();
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.InUse, true);
                }

                InvokeRandomized(UpdateClients, 0f, 0.2f, 0.05f);
                enabled = false;
            }

            public virtual void SendClientRPC(byte throttleAndBrake)
            {
                ClientRPC(RpcTarget.NetworkGroup(RPC_CLIENT_SNOWMOBILEUPDATE), Vehicle.SteerAngle, throttleAndBrake, Vehicle.DriveWheelVelocity, 1f);
            }

            private void UpdateClients()
            {
                if (!Vehicle.BaseEntity.IsOn())
                {
                    return;
                }

                byte num = (byte)((Vehicle.GetThrottleInput() + 1f) * 7f);
                byte b = 0;
                byte throttleAndBrake = (byte)(num + (b << 4));
                SendClientRPC(throttleAndBrake);
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);

                info.msg.snowmobile = Facepunch.Pool.Get<ProtoBuf.Snowmobile>();
                //info.msg.snowmobile.steerInput = Vehicle.SteerAngle;
                info.msg.snowmobile.driveWheelVel = Vehicle.DriveWheelVelocity;
                info.msg.snowmobile.throttleInput = Vehicle.GetThrottleInput();
                info.msg.snowmobile.brakeInput = Vehicle.BaseEntity.IsOn() ? 0 : 1;
                //info.msg.snowmobile.fuelFraction = Vehicle.GetFuelFraction(true);
            }
        }

        public class SpecialBoatEngine : SpecialFakeEngine
        {
            float cachedThrottleInput;

            public override void ServerInit()
            {
                base.ServerInit();
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.InUse, true);
                }

                InvokeRandomized(UpdateClients, 0f, 0.2f, 0.05f);
                enabled = false;
            }

            public virtual void SendClientUpdate(float gasPedal)
            {
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.Reserved2, gasPedal != 0f);
                    flagsUpdateScope.Set(Flags.Reserved12, gasPedal < 0f);
                }
            }

            private void UpdateClients()
            {
                if (!Vehicle.BaseEntity.IsOn())
                {
                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(Flags.Reserved2, false);
                        flagsUpdateScope.Set(Flags.Reserved12, false);
                    }

                    return;
                }

                var throttleInput = Vehicle.GetThrottleInput();
                if (cachedThrottleInput == throttleInput)
                {
                    return;
                }

                cachedThrottleInput = throttleInput;
                SendClientUpdate(cachedThrottleInput);
            }
        }

        public class SpecialBatteringRamEngine : SpecialFakeEngine
        {

            float cachedBrakeInput;
            float cachedThrottleInput;
            float cachedVelocity;
            bool hasVehicle;

            public override void ServerInit()
            {
                base.ServerInit();
                InvokeRandomized(CustomUpdateNetwork, 0f, 0.2f, 0.05f);
                hasVehicle = Vehicle != null;
                enabled = false;
            }

            void CustomUpdateNetwork()
            {
                var velocity = 10f;
                var throttleInput = 1f;
                var brakeInput = 0f;
                if (hasVehicle)
                {
                    velocity = Vehicle.DriveWheelVelocity;
                    throttleInput = Vehicle.GetThrottleInput();
                    brakeInput = Vehicle.GetBrakeInput();
                }

                if (HasFlag(Flags.On) && (throttleInput != cachedThrottleInput || brakeInput != cachedBrakeInput || velocity != cachedVelocity))
                {
                    cachedThrottleInput = throttleInput;
                    cachedBrakeInput = brakeInput;
                    cachedVelocity = velocity;
                    SendNetworkUpdate();
                }
                else if (flags != base.flags)
                {
                    SendNetworkUpdate_Flags();
                }
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);
                info.msg.batteringRam = Facepunch.Pool.Get<ProtoBuf.BatteringRam>();
                info.msg.batteringRam.driveWheelVel = cachedVelocity;
                info.msg.batteringRam.throttleInput = cachedThrottleInput;
                info.msg.batteringRam.brakeInput = cachedBrakeInput;
            }
        }

        public class SpecialBikeEngine : SpecialFakeEngine
        {
            public bool CanToggleLights;
            public bool IsMotorBike;

            public override void ServerInit()
            {
                base.ServerInit();
                using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.InUse, true);
                }

                InvokeRandomized(CustomUpdateNetwork, 0f, 0.2f, 0.05f);
                enabled = false;
            }

            private void CustomUpdateNetwork()
            {
                if (CanToggleLights)
                {
                    if (KaruzaEntity.BaseEntity.HasFlag(Flags.Reserved5) != HasFlag(Flags.Reserved5))
                    {
                        SetFlagLocal(Flags.Reserved5, KaruzaEntity.BaseEntity.HasFlag(Flags.Reserved5));
                    }
                }

                UpdateNetwork();
                UpdateClients();
            }

            public virtual void SendClientRPC(byte throttleAndBrake)
            {
                ClientRPC(RpcTarget.NetworkGroup(RPC_CLIENT_BIKEUPDATE), GetNetworkTime(), Vehicle.SteerAngle, throttleAndBrake, Vehicle.DriveWheelVelocity, 1);
            }

            private void UpdateClients()
            {
                if (!IsMotorBike || !CanToggleLights)
                {
                    return;
                }

                if (!Vehicle.BaseEntity.IsOn())
                {
                    return;
                }

                byte num = (byte)((Vehicle.GetThrottleInput() + 1f) * 7f);
                byte b = (byte)0;
                byte throttleAndBrake = (byte)(num + (b << 4));
                SendClientRPC(throttleAndBrake);
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);
                info.msg.bike = Facepunch.Pool.Get<ProtoBuf.Bike>();
                info.msg.bike.steerInput = Vehicle.SteerAngle;
                info.msg.bike.driveWheelVel = Vehicle.DriveWheelVelocity;
                info.msg.bike.throttleInput = Vehicle.GetThrottleInput();
                info.msg.bike.brakeInput = Vehicle.BaseEntity.IsOn() ? 0 : 1;

                if (!IsMotorBike && !info.forDisk)
                {
                    info.msg.baseEntity.pos = CachedTransform.position;
                    info.msg.baseEntity.rot = Vehicle.BaseVehicle.transform.localEulerAngles;
                }
            }
        }

        public class SpecialModularCarEngine : SpecialFakeEngine
        {
            bool hasVehicle;
            float cachedBrakeInput;
            float cachedThrottleInput;
            float cachedVelocity;

            static readonly Dictionary<int, Vector3> Sockets = new Dictionary<int, Vector3>()
                {
                    { 0, new Vector3(0.08f, 0.70f, 2.25f) },
                    { 1, new Vector3(0.02f, 0.70f, 2.25f) },
                    { 2, new Vector3(-0.03f, 0.70f, 2.25f) },
                    { 3, new Vector3(-0.09f, 0.70f, 2.25f) },
                };

            public override void ServerInit()
            {
                base.ServerInit();
                InvokeRandomized(UpdateNetwork, 0f, 0.2f, 0.05f);
                hasVehicle = Vehicle != null;
                enabled = false;
            }

            public override void Save(SaveInfo info)
            {
                base.Save(info);
                info.msg.modularCar = Facepunch.Pool.Get<ProtoBuf.ModularCar>();
                info.msg.modularCar.fuelStorageID = default(NetworkableId);

                if (hasVehicle)
                {
                    info.msg.modularCar.steerAngle = 0;
                    info.msg.modularCar.driveWheelVel = cachedVelocity;
                    info.msg.modularCar.throttleInput = cachedThrottleInput;
                    info.msg.modularCar.brakeInput = cachedBrakeInput;
                }
                else
                {
                    info.msg.modularCar.throttleInput = 1;
                    info.msg.modularCar.driveWheelVel = 10;
                }
            }

            public void TryAddModule(Item moduleItem, int socket)
            {
                ItemModVehicleModule component = moduleItem.info.GetComponent<ItemModVehicleModule>();
                if (component == null)
                {
                    return;
                }

                var itemModVehicle = moduleItem.info.GetComponent<ItemModVehicleModule>();

                var position = Sockets[socket];

                var newGO = GameManager.server.CreateEntity(itemModVehicle.entityPrefab.resourcePath, position, Quaternion.Euler(Vector3.zero)) as VehicleModuleEngine;
                var iterateOver = newGO.GetComponentsInChildren<Collider>().ToArray();
                for (var i = 0; i < iterateOver.Length; i++)
                {
                    var compo = iterateOver[i];
                    DestroyImmediate(compo);
                }

                DestroyImmediate(newGO.GetComponent<Rigidbody>());

                newGO.transform.localScale = new Vector3(0.0001f, 0.0001f, 0.0001f);
                newGO.networkEntityScale = true;
                newGO.AssociatedItemInstance = moduleItem;
                newGO.SetParent(this);
                newGO.Spawn();


                var es = (newGO.GetContainer() as Rust.Modular.EngineStorage);
                es.dropsLoot = false;

                newGO.AdminFixUp(3);
            }

            public override void UpdateNetwork()
            {
                base.UpdateNetwork();

                if (enabled)
                {
                    return;
                }

                Flags flags = base.flags;

                var throttleInput = 1f;
                var brakeInput = 0f;
                var velocity = 1f;

                if (hasVehicle)
                {
                    throttleInput = Vehicle.GetThrottleInput();
                    brakeInput = Vehicle.GetBrakeInput();
                    velocity = Vehicle.DriveWheelVelocity;
                }

                if (HasFlag(Flags.On) && (throttleInput != cachedThrottleInput || brakeInput != cachedBrakeInput || velocity != cachedVelocity))
                {
                    cachedThrottleInput = throttleInput;
                    cachedBrakeInput = brakeInput;
                    cachedVelocity = velocity;
                    SendNetworkUpdate();
                }
                else if (flags != base.flags)
                {
                    SendNetworkUpdate_Flags();
                }
            }
        }

        #endregion

        #endregion

        #region CustomFuelSystem

        public class KaruzaVehicleFuelSystem : IFuelSystem
        {
            private readonly ulong fuelNetId;
            private FuelSource fuelSource;

            public EntityRef<StorageContainer> fuelStorageInstance;

            public float nextFuelCheckTime;

            public bool cachedHasFuel;

            public float pendingFuel;

            public Action<int, Item> OnFuelUsed;

            public KaruzaVehicleFuelSystem(FuelSource fuelSource, StorageContainer fuelContainer)
            {
                this.fuelSource = fuelSource;
                if (this.fuelSource == FuelSource.Container)
                {
                    fuelNetId = fuelContainer.net.ID.Value;
                    fuelStorageInstance.Set(fuelContainer);
                }
            }

            public bool HasValidInstance(bool isServer)
            {
                if (fuelSource != FuelSource.Container)
                {
                    return true;
                }

                return fuelStorageInstance.IsValid(isServer);
            }

            public NetworkableId GetInstanceID()
            {
                return fuelStorageInstance.uid;
            }

            public void SetInstanceID(NetworkableId uid)
            {
                fuelStorageInstance.uid = uid;
            }

            bool IsInFuelInteractionRange(BasePlayer player)
            {
                if (fuelSource != FuelSource.Container)
                {
                    return false;
                }

                StorageContainer fuelContainer = GetFuelContainer();
                object obj = Interface.CallHook("CanCheckFuel", this, fuelContainer, player);
                if (obj is bool b)
                {
                    return b;
                }

                if (fuelContainer != null)
                {
                    float maxDist = 3f;
                    return fuelContainer.Distance(player.eyes.position) <= maxDist;
                }

                return false;
            }

            StorageContainer GetFuelContainer()
            {
                StorageContainer storageContainer = fuelStorageInstance.Get(true);
                if (storageContainer.IsValid())
                {
                    return storageContainer;
                }

                return null;
            }

            public bool CheckNewChild(BaseEntity child)
            {
                return false;
            }

            Item GetFuelItem()
            {
                StorageContainer fuelContainer = GetFuelContainer();
                object obj = Interface.CallHook("OnFuelItemCheck", this, fuelContainer);
                if (obj is Item item)
                {
                    return item;
                }

                if (fuelContainer == null)
                {
                    return null;
                }

                return fuelContainer.inventory.GetSlot(0);
            }

            public int GetFuelAmount()
            {
                if (fuelSource != FuelSource.Container)
                {
                    return 1;
                }

                return GetTotalFuel();
            }

            int GetTotalFuel()
            {
                StorageContainer fuelContainer = GetFuelContainer();
                var totalFuel = 0;
                for (int i = 0; i < fuelContainer.inventory.itemList.Count; i++)
                {
                    var fuelItem = fuelContainer.inventory.itemList[i];
                    object obj = Interface.CallHook("OnFuelAmountCheck", this, fuelItem);
                    if (obj is int intConverted)
                    {
                        totalFuel += intConverted;
                    }
                    else if (fuelItem != null && fuelItem.amount > 0)
                    {
                        totalFuel += fuelItem.amount;
                    }
                }

                return totalFuel;
            }

            public float GetFuelFraction()
            {
                if (fuelSource != FuelSource.Container)
                {
                    return 1;
                }

                Item fuelItem = GetFuelItem();
                if (fuelItem == null || fuelItem.amount < 1)
                {
                    return 0f;
                }

                return Mathf.Clamp01((float)fuelItem.amount / (float)fuelItem.MaxStackable());
            }

            public bool HasFuel(bool forceCheck = false)
            {
                if (fuelSource != FuelSource.Container)
                {
                    return true;
                }

                if (Time.time <= nextFuelCheckTime && !forceCheck)
                {
                    return cachedHasFuel;
                }

                if (fuelSource == FuelSource.Container)
                {
                    object obj = Interface.CallHook(ON_FUEL_CHECK_HOOK, this);
                    if (obj is bool b)
                    {
                        return b;
                    }

                    var fuelItem = GetFuelItem();
                    cachedHasFuel = fuelItem != null && fuelItem.amount > 0;
                }

                nextFuelCheckTime = Time.time + UnityEngine.Random.Range(1f, 2f);
                return cachedHasFuel;
            }

            public int TryUseFuel(float seconds, float fuelUsedPerSecond)
            {
                if (fuelSource != FuelSource.Container)
                {
                    return 0;
                }

                var fuelContainer = GetFuelContainer();
                object obj = Interface.CallHook(CAN_USE_FUEL_HOOK, this, fuelContainer, seconds, fuelUsedPerSecond);
                if (obj is int i)
                {
                    return i;
                }

                if (object.ReferenceEquals(fuelContainer, null))
                {
                    return 0;
                }

                var slot = fuelContainer.inventory.GetSlot(0);
                if (slot == null || slot.amount < 1)
                {
                    return 0;
                }

                pendingFuel += seconds * fuelUsedPerSecond;
                if (pendingFuel >= 1f)
                {
                    int num = Mathf.FloorToInt(pendingFuel);
                    slot.UseItem(num);
                    pendingFuel -= num;
                    OnFuelUsed?.Invoke(num, slot);
                    return num;
                }

                return 0;
            }

            public void LootFuel(BasePlayer player)
            {
                if (fuelSource != FuelSource.Container)
                {
                    return;
                }

                if (IsInFuelInteractionRange(player))
                {
                    GetFuelContainer().PlayerOpenLoot(player);
                }
            }

            public void AddFuel(int amount)
            {
                if (fuelSource != FuelSource.Container)
                {
                    return;
                }

                StorageContainer fuelContainer = GetFuelContainer();
                if (fuelContainer != null)
                {
                    fuelContainer.inventory.AddItem(fuelContainer.inventory.onlyAllowedItems[0], Mathf.FloorToInt(amount), 0uL);
                }

                return;
            }

            public void FillFuel()
            {
                if (fuelSource != FuelSource.Container)
                {
                    return;
                }

                StorageContainer fuelContainer = GetFuelContainer();
                if (fuelContainer != null)
                {
                    fuelContainer.inventory.AddItem(fuelContainer.inventory.onlyAllowedItems[0], fuelContainer.inventory.onlyAllowedItems[0].stackable, 0uL);
                }

                return;
            }

            public int GetFuelCapacity()
            {
                if (fuelSource != FuelSource.Container)
                {
                    return 1;
                }

                var container = GetFuelContainer();
                if (container.inventory.itemList.Count > 0)
                {
                    return container.inventory.itemList[0].info.stackable;
                }

                return container.inventory.onlyAllowedItems[0].stackable;
            }

            public void RemoveFuel(int amount)
            {
                if (fuelSource != FuelSource.Container)
                {
                    return;
                }

                var fuelContainer = GetFuelContainer();
                Item slot = fuelContainer.inventory.GetSlot(0);
                slot.UseItem(amount);
            }
        }

        public class SpecialCodeLock : CodeLock
        {
            IVehicle vehicle;
            float lowHealthThreshold;
            bool hasVehicle;
            public bool DestroyParentOnDestroy = true;

            public CodeLockConfig Config;

            public override bool ShouldInheritNetworkGroup()
            {
                return false;
            }

            public override void ServerInit()
            {
                this.vehicle = this.GetComponentInParent<IVehicle>();
                hasVehicle = vehicle != null;

                base.ServerInit();

                enabled = false;
                if (Config.EnableLowHealthUnlock && Config.LowHealthUnlockThreshold > 0)
                {
                    lowHealthThreshold = vehicle.VehicleConfig.MaxHealth * Config.LowHealthUnlockThreshold;
                    vehicle.OnHealthChange += OnHealthChanged;
                }
            }

            void OnHealthChanged(float oldValue, float newValue)
            {
                if (newValue <= 0)
                {
                    return;
                }

                var shouldLimitNetwork = lowHealthThreshold >= newValue;
                if (shouldLimitNetwork == limitNetworking)
                {
                    return;
                }

                limitNetworking = shouldLimitNetwork;
                if (shouldLimitNetwork)
                {
                    whitelistPlayers.Clear();
                    guestPlayers.Clear();
                    guestCode = string.Empty;
                    code = string.Empty;
                    hasCode = false;
                    hasGuestCode = false;
                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(Flags.Locked, false);
                    }
                }
            }

            public override bool OnRpcMessage(BasePlayer player, uint rpc, Message msg)
            {
                using (TimeWarning.New("CodeLock.OnRpcMessage"))
                {
                    if (rpc == 4013784361u && player != null)
                    {
                        Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                        if (ConVar.Global.developer > 2)
                        {
                            Debug.Log("SV_RPCMessage: " + player?.ToString() + " - RPC_ChangeCode ");
                        }

                        using (TimeWarning.New(RPC_TIMEWARNING_CHANGE_CODE))
                        {
                            using (TimeWarning.New(RPC_TIMEWARNING_CONDITIONS))
                            {
                                if (!RPC_Server.MaxDistance.Test(4013784361u, RPC_TIMEWARNING_CHANGE_CODE, this, player, 3f, checkParent: true))
                                {
                                    return true;
                                }
                            }

                            try
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                {
                                    RPCMessage rPCMessage = default(RPCMessage);
                                    rPCMessage.connection = msg.connection;
                                    rPCMessage.player = player;
                                    rPCMessage.read = msg.read;
                                    RPCMessage rpc2 = rPCMessage;
                                    CustomRPC_ChangeCode(rpc2);
                                }
                            }
                            catch (Exception exception)
                            {
                                Debug.LogException(exception);
                                player.Kick("RPC Error in Custom RPC_ChangeCode");
                            }
                        }

                        return true;
                    }

                    if (rpc == 2626067433u && player != null)
                    {
                        Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                        if (ConVar.Global.developer > 2)
                        {
                            Debug.Log("SV_RPCMessage: " + player?.ToString() + " - TryLock ");
                        }

                        using (TimeWarning.New(RPC_TIMEWARNING_TRY_LOCK))
                        {
                            using (TimeWarning.New(RPC_TIMEWARNING_CONDITIONS))
                            {
                                if (!RPC_Server.MaxDistance.Test(2626067433u, RPC_TIMEWARNING_TRY_LOCK, this, player, 3f, checkParent: true))
                                {
                                    return true;
                                }
                            }

                            try
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                {
                                    RPCMessage rPCMessage = default(RPCMessage);
                                    rPCMessage.connection = msg.connection;
                                    rPCMessage.player = player;
                                    rPCMessage.read = msg.read;
                                    RPCMessage rpc3 = rPCMessage;
                                    CustomTryLock(rpc3);
                                }
                            }
                            catch (Exception exception2)
                            {
                                Debug.LogException(exception2);
                                player.Kick("RPC Error in Custom TryLock");
                            }
                        }

                        return true;
                    }

                    if (rpc == 1718262 && player != null)
                    {
                        Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                        if (ConVar.Global.developer > 2)
                        {
                            Debug.Log("SV_RPCMessage: " + player?.ToString() + " - TryUnlock ");
                        }

                        using (TimeWarning.New(RPC_TIMEWARNING_TRY_UNLOCK))
                        {
                            using (TimeWarning.New(RPC_TIMEWARNING_CONDITIONS))
                            {
                                if (!RPC_Server.MaxDistance.Test(1718262u, RPC_TIMEWARNING_TRY_UNLOCK, this, player, 3f, checkParent: true))
                                {
                                    return true;
                                }
                            }

                            try
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                {
                                    RPCMessage rPCMessage = default(RPCMessage);
                                    rPCMessage.connection = msg.connection;
                                    rPCMessage.player = player;
                                    rPCMessage.read = msg.read;
                                    RPCMessage rpc4 = rPCMessage;
                                    CustomTryUnlock(rpc4);
                                }
                            }
                            catch (Exception exception3)
                            {
                                Debug.LogException(exception3);
                                player.Kick("RPC Error in Custom TryUnlock");
                            }
                        }

                        return true;
                    }

                    if (rpc == 418605506 && player != null)
                    {
                        Assert.IsTrue(player.isServer, RPC_USING_CLIENTSIDE_PLAYER);
                        if (ConVar.Global.developer > 2)
                        {
                            Debug.Log("SV_RPCMessage: " + player?.ToString() + " - UnlockWithCode ");
                        }

                        using (TimeWarning.New("UnlockWithCode"))
                        {
                            using (TimeWarning.New(RPC_TIMEWARNING_CONDITIONS))
                            {
                                if (!RPC_Server.MaxDistance.Test(418605506u, "UnlockWithCode", this, player, 3f, checkParent: true))
                                {
                                    return true;
                                }
                            }

                            try
                            {
                                using (TimeWarning.New(RPC_TIMEWARNING_CALL))
                                {
                                    RPCMessage rPCMessage = default(RPCMessage);
                                    rPCMessage.connection = msg.connection;
                                    rPCMessage.player = player;
                                    rPCMessage.read = msg.read;
                                    RPCMessage rpc5 = rPCMessage;
                                    CustomUnlockWithCode(rpc5);
                                }
                            }
                            catch (Exception exception4)
                            {
                                Debug.LogException(exception4);
                                player.Kick("RPC Error in Custom UnlockWithCode");
                            }
                        }

                        return true;
                    }
                }

                return false;
            }

            public override bool OnTryToOpen(BasePlayer player)
            {
                if (vehicle.BaseVehicle.IsLocked() && vehicle.BaseVehicle.creatorEntity.net.ID.Value != player.net.ID.Value)
                {
                    DoEffect(effectDenied.resourcePath);
                    return false;
                }

                object obj = Interface.CallHook(CAN_USE_LOCKED_ENTITY_HOOK, player, this);
                if (obj is bool b)
                {
                    return b;
                }

                if (!IsLocked())
                {
                    return true;
                }

                if (whitelistPlayers.Contains(player.userID) || guestPlayers.Contains(player.userID))
                {
                    DoEffect(effectUnlocked.resourcePath);
                    return true;
                }

                DoEffect(effectDenied.resourcePath);
                return false;
            }

            [RPC_Server]
            [RPC_Server.MaxDistance(3f, CheckParent = true)]
            private void CustomRPC_ChangeCode(RPCMessage rpc)
            {
                if (!rpc.player.CanInteract())
                {
                    return;
                }

                if (vehicle.BaseVehicle.OnlyOwnerAccessible() && vehicle.BaseVehicle.creatorEntity != null && vehicle.BaseVehicle.creatorEntity.net.ID.Value != rpc.player.net.ID.Value)
                {
                    DoEffect(effectDenied.resourcePath);
                    return;
                }

                string text = rpc.read.String();
                bool flag = rpc.read.Bit();
                if (!IsLocked() && text.Length == 4 && text.IsNumeric() && !(!hasCode && flag) && Interface.CallHook("CanChangeCode", rpc.player, this, text, flag) == null)
                {
                    if (!hasCode && !flag)
                    {
                        using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                        {
                            flagsUpdateScope.Set(Flags.Locked, true);
                        }
                    }

                    Facepunch.Rust.Analytics.Azure.OnCodelockChanged(rpc.player, this, flag ? guestCode : code, text, flag);
                    if (!flag)
                    {
                        code = text;
                        hasCode = code.Length > 0;
                        whitelistPlayers.Clear();
                        whitelistPlayers.Add(rpc.player.userID);
                    }
                    else
                    {
                        guestCode = text;
                        hasGuestCode = guestCode.Length > 0;
                        guestPlayers.Clear();
                        guestPlayers.Add(rpc.player.userID);
                    }

                    Interface.CallHook("OnCodeChanged", rpc.player, this, text, flag);
                    DoEffect(effectCodeChanged.resourcePath);
                    SendNetworkUpdate();
                }
            }

            [RPC_Server]
            [RPC_Server.MaxDistance(3f, CheckParent = true)]
            private void CustomTryUnlock(RPCMessage rpc)
            {
                if (!rpc.player.CanInteract())
                {
                    return;
                }

                if (vehicle.BaseVehicle.IsLocked() && vehicle.BaseVehicle.creatorEntity.net.ID.Value != rpc.player.net.ID.Value)
                {
                    DoEffect(effectDenied.resourcePath);
                    return;
                }

                if (IsLocked() && Interface.CallHook("CanUnlock", rpc.player, this) == null && !IsCodeEntryBlocked() && whitelistPlayers.Contains(rpc.player.userID))
                {
                    DoEffect(effectUnlocked.resourcePath);
                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(Flags.Locked, false);
                    }

                    SendNetworkUpdate();
                }
            }

            [RPC_Server.MaxDistance(3f, CheckParent = true)]
            [RPC_Server]
            private void CustomTryLock(RPCMessage rpc)
            {
                if (!rpc.player.CanInteract())
                {
                    return;
                }

                if (vehicle.BaseVehicle.IsLocked() && vehicle.BaseVehicle.creatorEntity.net.ID.Value != rpc.player.net.ID.Value)
                {
                    DoEffect(effectDenied.resourcePath);
                    return;
                }

                if (!IsLocked() && code.Length == 4 && Interface.CallHook("CanLock", rpc.player, this) == null && whitelistPlayers.Contains(rpc.player.userID))
                {
                    DoEffect(effectLocked.resourcePath);
                    using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        flagsUpdateScope.Set(Flags.Locked, true);
                    }

                    SendNetworkUpdate();
                }
            }

            [RPC_Server.MaxDistance(3f, CheckParent = true)]
            [RPC_Server]
            private void CustomUnlockWithCode(RPCMessage rpc)
            {
                if (!rpc.player.CanInteract() || !IsLocked() || IsCodeEntryBlocked())
                {
                    return;
                }

                string text = rpc.read.String();
                if (Interface.CallHook("OnCodeEntered", this, rpc.player, text) != null)
                {
                    return;
                }

                bool flag = text == guestCode;
                bool flag2 = text == code;
                if (!(text == code) && (!hasGuestCode || !(text == guestCode)))
                {
                    if (UnityEngine.Time.realtimeSinceStartup > lastWrongTime + 60f)
                    {
                        wrongCodes = 0;
                    }

                    DoEffect(effectDenied.resourcePath);
                    DoEffect(effectShock.resourcePath);
                    rpc.player.Hurt((wrongCodes + 1) * 5f, DamageType.ElectricShock, this, useProtection: false);
                    ++wrongCodes;
                    if (wrongCodes > 5)
                    {
                        rpc.player.ShowToast(GameTip.Styles.Red_Normal, blockwarning, false);
                    }

                    if (wrongCodes >= maxFailedAttempts)
                    {
                        using (FlagsUpdateScope flagsUpdateScope = StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                        {
                            flagsUpdateScope.Set(Flags.Reserved11, true);
                        }

                        Invoke(ClearCodeEntryBlocked, lockoutCooldown);
                    }

                    lastWrongTime = UnityEngine.Time.realtimeSinceStartup;
                    return;
                }

                SendNetworkUpdate();
                if (flag2)
                {
                    if (!whitelistPlayers.Contains(rpc.player.userID))
                    {
                        DoEffect(effectCodeChanged.resourcePath);
                        whitelistPlayers.Add(rpc.player.userID);
                        wrongCodes = 0;
                    }

                    Facepunch.Rust.Analytics.Azure.OnCodeLockEntered(rpc.player, this, isGuest: false);
                }
                else if (flag && !guestPlayers.Contains(rpc.player.userID))
                {
                    DoEffect(effectCodeChanged.resourcePath);
                    guestPlayers.Add(rpc.player.userID);
                    Facepunch.Rust.Analytics.Azure.OnCodeLockEntered(rpc.player, this, isGuest: true);
                }
            }


            public override void UpdateNetworkGroup()
            {
                isCallingUpdateNetworkGroup = false;
                if (net == null || Network.Net.sv == null || Network.Net.sv.visibility == null)
                {
                    return;
                }

                using (TimeWarning.New(UPDATE_NETWORKGROUP))
                {
                    var group = BaseNetworkable.GlobalNetworkGroup;
                    if (hasVehicle)
                    {
                        if (vehicle.BaseEntity.IsDestroyed || vehicle.BaseEntity.net == null)
                        {
                            return;
                        }

                        group = vehicle.BaseEntity.net.group;
                    }

                    if (net.SwitchGroup(group))
                    {
                        if (isSpawned)
                        {
                            if (net.group == null)
                            {
                                Debug.LogWarning(ToString() + " changed its network group to null");
                                return;
                            }

                            NetWrite netWrite = Network.Net.sv.StartWrite();
                            netWrite.PacketID(Message.Type.GroupChange);
                            netWrite.EntityID(net.ID);
                            netWrite.GroupID(net.group.ID);
                            netWrite.Send(new SendInfo(net.group.subscribers));
                        }
                    }
                }
            }

            public override bool ShouldNetworkTo(BasePlayer player)
            {
                if (IsTransferProtected())
                {
                    return false;
                }

                if (limitNetworking)
                {
                    return false;
                }

                if (hasVehicle)
                {
                    return vehicle.BaseEntity.ShouldNetworkTo(player);
                }

                if (net.group == null)
                {
                    return true;
                }

                return player.net.subscriber.IsSubscribed(net.group);
            }

            public override void DestroyShared()
            {
                base.DestroyShared();

                if (DestroyParentOnDestroy && hasVehicle && !vehicle.BaseEntity.IsDestroyed)
                {
                    vehicle.BaseEntity.Kill();
                }
            }

            public override void OnAttacked(HitInfo info)
            {
                using (TimeWarning.New(BASECOMBATENTITY_ONATTACKED))
                {
                    vehicle.BaseEntity.OnAttacked(info);
                    return;
                }
            }
        }

        #endregion

        #region Common Config

        public struct ConfigVector : IEquatable<ConfigVector>
        {
            public float x;

            public float y;

            public float z;

            private static readonly ConfigVector zeroVector = new ConfigVector(0f, 0f, 0f);

            private static readonly ConfigVector oneVector = new ConfigVector(1f, 1f, 1f);

            private static readonly ConfigVector upVector = new ConfigVector(0f, 1f, 0f);

            private static readonly ConfigVector downVector = new ConfigVector(0f, -1f, 0f);

            private static readonly ConfigVector leftVector = new ConfigVector(-1f, 0f, 0f);

            private static readonly ConfigVector rightVector = new ConfigVector(1f, 0f, 0f);

            private static readonly ConfigVector forwardVector = new ConfigVector(0f, 0f, 1f);

            private static readonly ConfigVector backVector = new ConfigVector(0f, 0f, -1f);

            private static readonly ConfigVector positiveInfinityVector = new ConfigVector(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);

            private static readonly ConfigVector negativeInfinityVector = new ConfigVector(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            public static ConfigVector zero
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    return zeroVector;
                }
            }

            public void CopyTo(ConfigVector instance)
            {
                instance.x = x;
                instance.y = y;
                instance.z = z;
            }

            public ConfigVector Copy()
            {
                ConfigVector vectorData = default(ConfigVector);
                CopyTo(vectorData);
                return vectorData;
            }

            public ConfigVector(float x, float y, float z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }

            public static implicit operator ConfigVector(Vector3 v)
            {
                return new ConfigVector(v.x, v.y, v.z);
            }

            public static implicit operator ConfigVector(Quaternion q)
            {
                return q.eulerAngles;
            }

            public static implicit operator Vector3(ConfigVector v)
            {
                return new Vector3(v.x, v.y, v.z);
            }

            public static implicit operator Vector4(ConfigVector v)
            {
                return new Vector4(v.x, v.y, v.z, 0f);
            }

            public static implicit operator Quaternion(ConfigVector v)
            {
                return Quaternion.Euler(v);
            }

            public bool Equals(ConfigVector other)
            {
                if (x == other.x && y == other.y)
                {
                    return z == other.z;
                }

                return false;
            }

            public override bool Equals(object obj)
            {
                if (obj is ConfigVector)
                {
                    return Equals((ConfigVector)obj);
                }

                return false;
            }

            public static bool operator ==(ConfigVector a, ConfigVector b)
            {
                return a.Equals(b);
            }

            public static bool operator !=(ConfigVector a, ConfigVector b)
            {
                return !a.Equals(b);
            }

            public static bool operator ==(ConfigVector a, Vector3 b)
            {
                return a.Equals(b);
            }

            public static bool operator !=(ConfigVector a, Vector3 b)
            {
                return !a.Equals(b);
            }

            public static bool operator ==(Vector3 a, ConfigVector b)
            {
                return a.Equals(b);
            }

            public static bool operator !=(Vector3 a, ConfigVector b)
            {
                return !a.Equals(b);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(x, y, z);
            }
        }

        public class CustomVehicleConfig : BaseVehicleConfig
        {
        }

        public class BaseVehicleConfig : BaseEntityConfig
        {
            public virtual SeekerStrength SeekerStrength { get; set; } = SeekerStrength.OFF;
            public ConfigVector TorqueScale { get; set; }
            public float SleepThreshold { get; set; } = 0.025f;
            public float OutsideDecayMinutes { get; set; } = 480f;
            public float InsideDecayMinutes { get; set; } = 2880f;
            public bool CheckingBuildingPrivForInsideDecay { get; set; } = false;
            public float TimeAfterEngineOffToStartDecay { get; set; } = 600f;
            public float Mass { get; set; }
            public float Drag { get; set; }
            public float AngularDrag { get; set; }
            public float MaxDepenetrationVelocity { get; set; } = 10f;
            public float MaxAngularVelocity { get; set; }
            public ConfigVector CenterOfMass { get; set; }
            public bool AutomaticCenterOfMass { get; set; }
            public ConfigVector InertiaTensor { get; set; }
            public bool AutomaticInertiaTensor { get; set; } = true;
            public ConfigVector WaterSampleModifier { get; set; }
            public float EngineStartupTime { get; set; } = 5f;
            public bool DisableCounterUpdates { get; set; }
            public bool AllowTerrainTrigger { get; set; } = false;
            public float PushActionForce { get; set; } = 5f;
            public float PushCooldown { get; set; } = 1f;
            public float SubmergedPushActionForce { get; set; } = 12.5f;
            public virtual float DriverDamageScaling { get; set; } = 1f;
            public virtual float PassengerDamageScaling { get; set; } = 1f;
            public float Bounciness { get; set; } = -1f;
            public PhysicsMaterialCombine BounceCombine { get; set; } = PhysicsMaterialCombine.Minimum;
            public float DynamicFriction { get; set; } = -1f;
            public PhysicsMaterialCombine FrictionCombine { get; set; } = PhysicsMaterialCombine.Minimum;
            public MagnetSettings MagnetSettings { get; set; } = new MagnetSettings();
            public RadioSettings RadioSettings { get; set; } = new RadioSettings();
            public PhysicalEngineSettings PhysicalEngineSettings { get; set; } = new PhysicalEngineSettings();
            public FuelSettings FuelSettings { get; set; } = new FuelSettings();
            public BoostSettings BoostSettings { get; set; } = new BoostSettings();
            public EjectSettings EjectSettings { get; set; } = new EjectSettings();
            public List<WeaponConfiguration> WeaponConfigurations { get; set; } = new List<WeaponConfiguration>();
            public List<WheelColliderConfig> WheelColliders { get; set; } = new List<WheelColliderConfig>();
            public List<HoverEngineConfig> HoverEngines { get; set; } = new List<HoverEngineConfig>();
            public List<DismountPointConfig> CustomDismountPositions { get; set; } = new List<DismountPointConfig>();
            public FoilageInteractionSettings FoilageInteraction { get; set; } = new FoilageInteractionSettings();
            public TowSettings TowSettings { get; set; } = new TowSettings();
            public GForceSettings GForceSettings { get; set; } = new GForceSettings();
            public List<CustomCounterConfig> HealthCounters { get; set; } = new List<CustomCounterConfig>();
            public List<CustomSpeedCounterConfig> SpeedCounters { get; set; } = new List<CustomSpeedCounterConfig>();
            public List<CustomCounterConfig> AltitudeCounters { get; set; } = new List<CustomCounterConfig>();
            public List<CustomCounterConfig> FuelCounters { get; set; } = new List<CustomCounterConfig>();
            public List<VehicleWeakspotConfig> Weakspots { get; set; } = new List<VehicleWeakspotConfig>();
            public AmmoContainerConfiguration AmmoContainer { get; set; } = new AmmoContainerConfiguration();
            public CodeLockConfig CodeLockConfig { get; set; } = new CodeLockConfig();
            public RadarSettings RadarSettings { get; set; } = new RadarSettings();
        }

        public class SuperBaseEntityConfig
        {
            public EntityNetworkRange NetworkRange { get; set; } = EntityNetworkRange.Medium;
            public bool EnableSaving { get; set; } = false;
            public BodyType BodyType { get; set; } = BodyType.WorldItem;
            public int BodyItemId { get; set; } = SHEETL_METAL_ITEMID;
            public ulong BodySkinId { get; set; }
            public string BodyGibName { get; set; }
            public string BodyGibPrefab { get; set; }
            public BoundsSettings Bounds { get; set; } = new BoundsSettings();
            public List<PropConfig> PropConfigs { get; set; } = new List<PropConfig>();
            public PipePropSettings PipeProps { get; set; } = new PipePropSettings();

        }

        public class BaseEntityConfig : SuperBaseEntityConfig
        {
            // TODO Rename to BodyPrefab
            public float MaxHealth { get; set; }
            public bool EnableServerOcclusion { get; set; } = false;
            public virtual bool DoHitEffect { get; set; }
            public bool UnlimitedAmmo { get; set; } = false;
            public bool NukeOnExplosion { get; set; }
            public int NukeExplosionSteps { get; set; } = 10;
            public int NukeReplaceTreeWithDeadVariantAtStep { get; set; } = 8;
            public float ExplosionDamage { get; set; }
            public float MinExplosionRadius { get; set; } = 2f;
            public float ExplosionRadius { get; set; } = 5f;
            public List<string> ExplosionFxs { get; set; } = new List<string>();
            public LowHealthEffectSettings LowHealthEffectSettings { get; set; } = new LowHealthEffectSettings();

            public Dictionary<string, int> BuildCosts { get; set; } = new Dictionary<string, int>();
            public virtual Dictionary<DamageType, float> ProtectionProperties { get; set; } = new Dictionary<DamageType, float>()
                {
                    { DamageType.Generic, 0 },
                    { DamageType.Hunger, 1 },
                    { DamageType.Thirst, 1 },
                    { DamageType.Cold, 1 },
                    { DamageType.Drowned, 1 },
                    { DamageType.Heat, -2 },
                    { DamageType.Bleeding, 1 },
                    { DamageType.Poison, 1 },
                    { DamageType.Suicide, 1 },
                    { DamageType.Bullet, 0.95f },
                    { DamageType.Slash, 1 },
                    { DamageType.Blunt, 1 },
                    { DamageType.Fall, 1 },
                    { DamageType.Radiation, 1 },
                    { DamageType.Bite, 1 },
                    { DamageType.Stab, 1 },
                    { DamageType.Explosion, -2f },
                    { DamageType.RadiationExposure, 0 },
                    { DamageType.ColdExposure, 0 },
                    { DamageType.Decay, 0},
                    { DamageType.ElectricShock, 0 },
                    { DamageType.Arrow, 1 },
                    { DamageType.AntiVehicle, 0f },
                    { DamageType.Collision, 1 },
                    { DamageType.Fun_Water, 0 },
                    { DamageType.LAST, 0 }
                };

            public BoomboxSettings BoomboxSettings { get; set; } = new BoomboxSettings();
            public HurtTriggerSettings HurtTrigger { get; set; } = new HurtTriggerSettings();
            public PowerSettings PowerSettings { get; set; } = new PowerSettings();
            public ToastSettings GeneralToastSettings { get; set; } = new ToastSettings();
            public List<StorageContainerConfiguration> StorageContainers { get; set; } = new List<StorageContainerConfiguration>();
            public List<RecyclerConfiguration> Recyclers { get; set; } = new List<RecyclerConfiguration>();
            public List<GimbalConfig> GimbalConfigs { get; set; } = new List<GimbalConfig>();
            public List<ComputerStationConfig> ComputerStationConfigs { get; set; } = new List<ComputerStationConfig>();
            public BouyancySettings BuoyancySettings { get; set; } = new BouyancySettings();
            public List<BoxColliderConfig> CustomBoxColliders { get; set; } = new List<BoxColliderConfig>();
            public List<SphereColliderConfiguration> CustomSphereColliders { get; set; } = new List<SphereColliderConfiguration>();
            public List<PhysicsTrigger> PhysicsTriggers { get; set; } = new List<PhysicsTrigger>();
            public List<SafeZoneTrigger> SafeZoneTriggers { get; set; } = new List<SafeZoneTrigger>();
            public RespawnSettings RespawnSettings { get; set; } = new RespawnSettings();
            public List<CustomSwitchConfig> CustomSwitches { get; set; } = new List<CustomSwitchConfig>();
            public List<CustomWheelConfig> CustomWheels { get; set; } = new List<CustomWheelConfig>();
            public List<CustomInputJointConfig> InputJoints { get; set; } = new List<CustomInputJointConfig>();
        }

        public class DismountPointConfig
        {
            public ConfigVector Location { get; set; }
        }

        public class BouyancySettings
        {
            public bool Enabled { get; set; }
            public float UnderwaterDrag { get; set; } = 2.5f;
            public float WavesEffect { get; set; } = 1;
            public float FlowMovementScale { get; set; } = 1f;
            public bool DoEffects { get; set; } = true;
            public float RequiredSubmergedFraction { get; set; } = 0f;
            public bool ScaleForceWithMass { get; set; }
            public List<BouyancyPointConfig> BuoyancyPoints { get; set; } = new List<BouyancyPointConfig>();
        }

        public class BouyancyPointConfig
        {
            public float Size { get; set; } = 0.5f;
            public ConfigVector Location { get; set; }
            public float Force { get; set; } = 250f;
        }

        public class PhysicsTrigger
        {
            public ConfigVector Size { get; set; } = new Vector3(0.1f, 0.1f, 0.1f);
            public ConfigVector Location { get; set; }
            public bool CheckForObjUnderFeet { get; set; }
            public bool ParentSleepers { get; set; } = true;
            public bool ParentNPCPlayers { get; set; } = true;
            public PhysicsTriggerType PhysicsTriggerType { get; set; } = PhysicsTriggerType.Player;
        }

        public class SafeZoneTrigger
        {
            public bool Enabled { get; set; } = true;
            public ConfigVector Size { get; set; } = new ConfigVector(0.1f, 0.1f, 0.1f);
            public ConfigVector Location { get; set; }
        }

        public class WeaponControllerConfig
        {
            public float MinX { get; set; } = -360;
            public float MaxX { get; set; } = 360;
            public float MinY { get; set; } = -360;
            public float MaxY { get; set; } = 360;
            public bool RequiresEngine { get; set; } = false;
            public List<WeaponConfiguration> WeaponConfigurations { get; set; } = new List<WeaponConfiguration>();
        }

        public class EjectSettings
        {
            public bool Enabled { get; set; }
            public int ItemId { get; set; }
            public float Velocity { get; set; }
            public string SeatPrefab { get; set; }
            public float PreventEjectDistance { get; set; } = 30f;
            public float RocketLifeSeconds { get; set; } = 1f;
            public float CollisionDelay { get; set; } = 0.5f;
            public bool AutoEjectIfLowHealth { get; set; } = false;
            public float LowHealthThreshold { get; set; } = 0.2f;
            public bool DestroyVehicleOnEject { get; set; } = false;
            public float DestroyVehicleOnEjectDelay { get; set; } = 1f;
            public bool RequirePowerToEject { get; set; } = true;
            public ConfigVector EjectDirectionModifiers { get; set; } = Vector3.one;
            public List<ConfigSettings> ButtonSettings { get; set; } = new List<ConfigSettings>();
        }

        public class WheelColliderConfig
        {
            public ConfigVector Location { get; set; }
            public float Radius { get; set; } = 0.20f;
            public float Mass { get; set; } = 20;
            public float SpringStiffness { get; set; } = 10000;
            public float Damper { get; set; } = 600f;
            public float TargetPosition { get; set; } = 1f;
            public float WheelDampingRate { get; set; } = 0.5f;
            public float ContactOffset { get; set; }
            public float ForceAppPointDistance { get; set; }
            public WheelFrictionCurveConfig ForwardFriction { get; set; } = new WheelFrictionCurveConfig();
            public WheelFrictionCurveConfig SidewaysFriction { get; set; } = new WheelFrictionCurveConfig()
            {
                ExtremumSlip = 0.2f,
                ExtremumValue = 1f,
                AsymptoteValue = 0.75f,
                AsymptoteSlip = 0.5f,
                Stiffness = 1f,
            };

            public bool Steering { get; set; }
            public bool Power { get; set; }
            public bool Brake { get; set; }
            public float TyreFriction { get; set; } = 1.5f;
            public float SteeringModifier { get; set; } = 45f;
            public float SuspensionDistance { get; set; } = 0.3f;
            public PropConfig PropConfig { get; set; }
        }

        public class WeaponConfiguration
        {
            public bool Enabled { get; set; } = true;
            public string AmmoPrefab { get; set; }
            public string AmmoShortName { get; set; }
            public ulong AmmoSkinId { get; set; }
            public float AmmoSpeed { get; set; }
            public int AmmoPerShot { get; set; } = 1;
            public AmmoSource AmmoSource { get; set; } = AmmoSource.Inventory;
            public ConfigVector AmmoScale { get; set; } = Vector3.one;
            public virtual float FireRate { get; set; }
            public string MuzzleEffect { get; set; }
            public ConfigVector MuzzleNormLocal { get; set; } = Vector3.forward;
            public int MuzzleEffectCount { get; set; } = 1;
            public float MuzzleEffectRepeatDelay { get; set; }
            public ProjectileType ProjectileType { get; set; }
            public float AimCone { get; set; }
            public float MinExplosionRadius { get; set; }
            public float ExplosionRadius { get; set; }
            public string ExplosionEffect { get; set; }
            public float DetonationTimerMin { get; set; }
            public float DetonationTimerMax { get; set; }
            public virtual int MagazineCapacity { get; set; }
            public virtual float ReloadTime { get; set; }
            public bool EnableReload { get; set; }
            public string DryFireEffect { get; set; } = "assets/prefabs/weapons/m249/effects/dryfire.prefab";
            public float DryFireEffectDelay { get; set; } = 1f;
            public float RecoilModifier { get; set; }
            public bool SendToNetworkGroupOnly { get; set; }
            public string ImpactEffect { get; set; }
            public int ExplosionSteps { get; set; } = 1;
            public int ReplaceTreeWithDeadVariantAtStep { get; set; } = -1;
            public BarrelConfiguration BarrelConfig { get; set; }
            public BUTTON Button { get; set; }
            public List<DamageTypeConfig> DamageTypes { get; set; } = new List<DamageTypeConfig>();
            public List<CustomBarrelConfiguration> CustomBarrelConfigs { get; set; } = new List<CustomBarrelConfiguration>();
            public List<CustomCounterConfig> CounterConfigurations { get; set; } = new List<CustomCounterConfig>();
        }

        public class RadarSettings
        {
            public bool Enabled { get; set; }
            public bool CanRadarOtherVehicles { get; set; } = true;
        }

        public class DamageTypeConfig
        {
            public DamageType Type { get; set; }

            public float Amount { get; set; }
        }

        public class CustomCounterConfig : ConfigSettings, IBaseVehicleConfigEnabled, IBaseVehicleConfigLocation, IBaseVehicleConfigRotation
        {
            public override bool Enabled { get; set; } = true;
            public bool DetectCollisions { get; set; }
        }

        public class CustomSpeedCounterConfig : CustomCounterConfig
        {
            public bool ShowKMH { get; set; }
        }

        public class PropConfig : IBaseVehicleConfigFlags, IBaseVehicleConfigScale
        {
            public PartType PartType { get; set; }
            public PropType PropType { get; set; }
            public string PrefabPath { get; set; }
            public string NetworkPrefabOverride { get; set; }
            public ulong SkinId { get; set; }
            public ulong SkinId2 { get; set; }
            public int ItemId { get; set; }
            public ConfigVector Location { get; set; }
            public ConfigVector Rotation { get; set; }
            public ConfigVector Scale { get; set; } = Vector3.one;
            public string GibName { get; set; }
            public bool DetectCollisions { get; set; }
            public ulong? ModelState { get; set; } = null;
            public Flags Flags { get; set; }
            public List<PropConfig> SubPropConfigs { get; set; }
        }

        public class AmmoContainerConfiguration : ContainerConfiguration, IBaseVehicleConfigEnabled
        {
            public virtual bool Enabled { get; set; } = true;
        }

        public class ProxyContainerSetting : PropConfigSettings
        {
            public bool DetectCollisions { get; set; }
            public ulong SkinId { get; set; }
        }

        public class ContainerAdaptorSetting : PropConfigSettings
        {
            public ContainerAdaptorType ContainerAdaptorType { get; set; } = ContainerAdaptorType.Input;
            public bool DetectCollisions { get; set; }
            public string InputNameOverride { get; set; }
            public string OutputNameOverride { get; set; }

        }

        public class StorageContainerConfiguration : ContainerConfiguration, IBaseVehicleConfigEnabled
        {
            public virtual bool Enabled { get; set; } = true;
            public StorageContainerType StorageContainerType { get; set; } = StorageContainerType.Storage;
        }

        public class RecyclerConfiguration : ContainerConfiguration, IBaseVehicleConfigEnabled
        {
            public virtual bool Enabled { get; set; } = true;
            public override string PrefabPath { get; set; } = RECYCLER_PREFAB;
            public override int ItemCount { get; set; } = 12;
            public bool OnlyOneUser { get; set; }
            public bool AutoToggle { get; set; }
            public string StartEffect { get; set; } = RECYCLER_START_EFFECT_PREFAB;
            public string StopEffect { get; set; } = RECYCLER_STOP_EFFECT_PREFAB;
        }

        public class ContainerConfiguration : BaseConfigSettings, IBaseVehicleConfigPrefab
        {
            public virtual string PrefabPath { get; set; }
            public string NetworkPrefabOverride { get; set; }
            public virtual int ItemCount { get; set; }
            public virtual string PanelName { get; set; }
            public ulong SkinId { get; set; }
            public bool DetectCollisions { get; set; }
            public bool IsLocked { get; set; }
            public bool DropsLoot { get; set; } = true;
            public bool IgnoreCodelock { get; set; }
            public virtual List<ItemConfiguration> DefaultItems { get; set; } = new List<ItemConfiguration>();
            public virtual List<string> AllowedItems { get; set; } = new List<string>();
            public List<ProxyContainerSetting> ProxyContainers { get; set; } = new List<ProxyContainerSetting>();
            public List<ContainerAdaptorSetting> Adaptors { get; set; } = new List<ContainerAdaptorSetting>();
        }

        public class ItemConfiguration
        {
            public string ShortName { get; set; }
            public int Amount { get; set; } = 1;
            public ulong SkinId { get; set; }
        }

        public class ToastSettings
        {
            public bool Enabled { get; set; } = false;
            public bool OnEngineOnToastEnabled { get; set; } = true;
            public string OnEngineOnToast { get; set; } = ON_ENGINE_ON_TOAST;
            public bool CentralLockToastEnabled { get; set; } = true;
            public string CentralLockUnlockToast { get; set; } = CENTRAL_LOCKING_UNLOCKED_TOAST;
            public string CentralLockLockedToast { get; set; } = CENTRAL_LOCKING_LOCKED_TOAST;
            public bool PowerSwitchEnabled { get; set; } = true;
            public string PowerSwitchOnToast { get; set; } = POWER_SWITCH_ON_TOAST;
            public string PowerSwitchOffToast { get; set; } = POWER_SWITCH_OFF_TOAST;
            public bool LandingGearSwitchEnabled { get; set; } = true;
            public string LandingGearSwitchOnToast { get; set; } = LANDING_GEAR_ON_TOAST;
            public string LandingGearSwitchOffToast { get; set; } = LANDING_GEAR_OFF_TOAST;
            public bool LandingGearOnWarningEnabled { get; set; } = true;
            public float LandingGearOnWarningDelay { get; set; } = 5f;
            public string LandingGearOnWarning { get; set; } = LANDING_GEAR_WARNING_TOAST;
            public bool NoAmmoToastEnabled { get; set; } = true;
            public string NoAmmoToast { get; set; } = NO_AMMO_TOAST;
            public bool LinkedComputerStationToastsEnabled { get; set; } = true;
            public string LinkedComputerStationSeatPrompt { get; set; } = LINKED_COMPUTER_STATION_SEAT_PROMPT;
            public bool TowAutoConnectSwitchToastsEnabled { get; set; }
            public string TowAutoConnectSwitchOnToast { get; set; } = TOW_AUTO_CONNECT_ON_TOAST;
            public string TowAutoConnectSwitchOffToast { get; set; } = TOW_AUTO_CONNECT_OFF_TOAST;
        }

        public class CodeLockConfig : PropConfigSettings
        {
            public bool EnableLowHealthUnlock { get; set; }
            public float LowHealthUnlockThreshold { get; set; } = 0.15f;
            public bool SpawnWithRandomCode { get; set; } = false;
            public override string PrefabPath { get; set; } = CODELOCK_PREFAB;
            public ConfigSettings CentralLockingSwitchSettings { get; set; } = new ConfigSettings();
        }

        public class BoxColliderConfig : BaseColliderConfiguration
        {
            public ConfigVector Size { get; set; }
        }

        public class SphereColliderConfiguration : BaseColliderConfiguration
        {
            public float Radius { get; set; }
        }

        public class BaseColliderConfiguration
        {
            public ConfigVector Center { get; set; }
            public bool RequireVehicleMounted { get; set; }
            public bool RequireEngineOff { get; set; }
            public LayerFlag ExcludeLayers { get; set; } = LayerFlag.Ragdoll | LayerFlag.Physics_Debris | LayerFlag.Player_Movement;
        }

        public class HurtTriggerSettings
        {
            public bool Enabled { get; set; } = false;
            public List<HurtTriggerConfig> Triggers { get; set; } = new List<HurtTriggerConfig>();
        }

        public class FoilageInteractionSettings
        {
            public bool Enabled { get; set; } = false;
            public List<FoilageInteractionConfig> Triggers { get; set; } = new List<FoilageInteractionConfig>();
        }

        public class TowSettings
        {
            public bool Enabled { get; set; } = false;
            public List<TowTriggerConfig> Triggers { get; set; } = new List<TowTriggerConfig>();
            public FacepunchTowingSettings VanillaTowingSettings { get; set; } = new FacepunchTowingSettings();
            public DynamicTowingSettings DynamicTowingSettings { get; set; } = new DynamicTowingSettings();
        }

        public class FacepunchTowingSettings
        {
            public bool Enabled { get; set; }
            public bool PreventInputWhileTowed { get; set; } = true;
            public ConfigVector TowAnchorPoint { get; set; }
            public ConfigVector TowAnchorTriggerSize { get; set; } = Vector3.one;
            public GaitType MaxTowingGait { get; set; } = GaitType.Trot;
        }

        public class DynamicTowingSettings
        {
            public bool Enabled { get; set; }
            public List<AnchorGroup> Groups { get; set; } = new List<AnchorGroup>();
            public List<DynamicTowAnchor> Anchors { get; set; } = new List<DynamicTowAnchor>();
        }

        public class DynamicTowAnchor : IBaseVehicleConfigEnabled, IBaseVehicleConfigLocation
        {
            public bool Enabled { get; set; }
            public string GroupKey { get; set; }
            public ConfigVector Location { get; set; }
            public int ItemId { get; set; } = NAIL_ITEMID;
            public ulong SkinId { get; set; }
            public float MinRopeLength { get; set; } = 0f;
            public float MinRopeLengthWhenConnected { get; set; } = 0f;
            public float MaxRopeLength { get; set; } = 20f;
            public float MassScale { get; set; } = 1f;
            public float ConnectedMassScale { get; set; } = 1f;
            public ConfigVector AdaptorScale { get; set; } = Vector3.one;
            public bool DisplayRope { get; set; } = true;
            public bool DisplayRopeWhenDisconnected { get; set; } = true;
            public bool AddReleaseButtonToAnchor { get; set; } = true;
            public IOType RopeType { get; set; } = IOType.Industrial;
            public WireColour RopeColor { get; set; } = WireColour.Gray;
            public float RopeThickness { get; set; } = 1f;
            public bool RequireDriverForAutoConnect { get; set; } = true;
            public PropConfigSettings AutoAttachSwitchSettings { get; set; } = new PropConfigSettings();
            public ConfigSettings ReleaseButtonSettings { get; set; } = new ConfigSettings();
            public ConfigSettings AnchorWheel { get; set; } = new ConfigSettings();
            public ConfigSettings AnchorDisplay { get; set; } = new ConfigSettings();
        }

        public class AnchorGroup : IBaseVehicleConfigEnabled
        {
            public bool Enabled { get; set; }
            public string GroupKey { get; set; }
            public ConfigSettings ReleaseButtonSettings { get; set; } = new ConfigSettings();
        }

        public class BoundsSettings
        {
            public ConfigVector Center { get; set; }
            public ConfigVector Size { get; set; }
        }

        public class RespawnSettings
        {
            public bool Enabled { get; set; }
            public bool EnableLowHealthUnlock { get; set; }
            public float LowHealthUnlockThreshold { get; set; } = 0.15f;
            public List<RespawnPointConfig> RespawnPoints { get; set; } = new List<RespawnPointConfig>();
        }

        public class RespawnPointConfig : PropConfigSettings
        {
            public ulong SkinId { get; set; }
            public string NetworkPrefabOverride { get; set; }
            public bool DetectCollisions { get; set; }
            public float UnlockTime { get; set; } = 300f;
            public string DisplayName { get; set; }
            public RespawnPointSeat LinkedMount { get; set; } = new RespawnPointSeat();
        }

        public class RespawnPointSeat : PropConfigSettings
        {
        }

        public class FuelSettings
        {
            public float FuelPerSec { get; set; } = 0.25f;
            public FuelSource FuelSource { get; set; } = FuelSource.Container;
            public bool AllowLootingWithDriver { get; set; } = false;
            public FuelContainerConfiguration FuelContainer { get; set; } = new FuelContainerConfiguration()
            {
                PanelName = "fuelsmall",
                ItemCount = 1,
                DropsLoot = false,
            };
        }

        public class FuelContainerConfiguration : ContainerConfiguration
        {
            public List<FuelModifierSetting> FuelModifiers { get; set; } = new List<FuelModifierSetting>();
        }

        public class FuelModifierSetting
        {
            public float SpeedMultiplier { get; set; } = 1f;
            public float FuelPerSec { get; set; } = -1f;
            public ulong SkinId { get; set; }
        }

        public class BoostSettings
        {
            public bool Enabled { get; set; } = false;
            public BUTTON Button { get; set; } = BUTTON.SPRINT;
            public float EnginePowerModifier { get; set; } = 1.0f;
            public float Cooldown { get; set; } = 0f;
            public FuelSource BoostFuelSource { get; set; } = FuelSource.None;
            public float MaxBoostDuration { get; set; }
            public int BoostFuelPerSecond { get; set; } = 1;
            public BoostType BoostType { get; set; } = BoostType.Hold;
            public ContainerConfiguration BoostFuelContainer { get; set; } = new ContainerConfiguration()
            {
                PanelName = "fuelsmall",
                ItemCount = 1,
                DropsLoot = false,
            };

            public List<PropConfigSettings> BoostPropConfigs { get; set; } = new List<PropConfigSettings>();
            public List<CustomCounterConfig> BoostCounters { get; set; } = new List<CustomCounterConfig>();
            public List<CustomCounterConfig> BoostTimeCounters { get; set; } = new List<CustomCounterConfig>();
        }

        public class PhysicalEngineSettings : BaseConfigSettings, IBaseVehicleConfigPrefab, IBaseVehicleConfigEnabled
        {
            public bool Enabled { get; set; } = false;
            public string PrefabPath { get; set; }
            public string NetworkPrefabOverride { get; set; }
            public ulong SkinId { get; set; }
            public bool DetectCollisions { get; set; }
            public bool IsLocked { get; set; }
            public bool DropsLoot { get; set; } = true;
            public bool IgnoreCodelock { get; set; }
            public EngineType EngineType { get; set; } = EngineType.Small;
            public List<ItemConfiguration> DefaultItems { get; set; } = new List<ItemConfiguration>();
        }

        public class RadioSettings
        {
            public bool Enabled { get; set; } = false;
            public BaseConfigSettings RadioConfig { get; set; } = new BaseConfigSettings();
            public ConfigSettings BroadcasterConfig { get; set; } = new ConfigSettings();
        }

        public class BoomboxSettings : PropConfigSettings
        {
            public override string PrefabPath { get; set; } = BOOMBOX_STATIC_PREFAB;
            public bool RequireVehiclePower { get; set; }
        }

        public class GimbalConfig : IBaseVehicleConfigEnabled, IBaseVehicleConfigLocation, IBaseVehicleConfigRotation, IBaseVehicleSubPropList
        {
            public bool Enabled { get; set; } = false;
            public ConfigVector Location { get; set; }
            public ConfigVector Rotation { get; set; }
            public int ItemId { get; set; }
            public ulong SkinId { get; set; }
            public WeaponControllerConfig WeaponControllerConfig { get; set; } = new WeaponControllerConfig();
            public List<PropConfig> SubPropConfigs { get; set; } = new List<PropConfig>();
        }

        public class ComputerStationConfig : PropConfigSettings, IBaseVehicleSubPropList
        {
            public ulong SkinId { get; set; }
            public string NetworkPrefabOverride { get; set; }
            public bool CanSwapTo { get; set; } = true;
            public bool IsLocalSeat { get; set; } = false;
            public bool ParentToVehicle { get; set; } = false;
            public bool IsDriver { get; set; } = false;
            public PropConfigSettings LinkedMount { get; set; } = new PropConfigSettings();
            public WeaponControllerConfig WeaponControllerConfig { get; set; } = new WeaponControllerConfig();
            public List<PropConfig> SubPropConfigs { get; set; } = new List<PropConfig>();
        }

        public class PowerSettings
        {
            public bool RequireDriver { get; set; } = true;
            public bool TogglesLight { get; set; } = false;
            public List<PropConfigSettings> SwitchConfigs { get; set; } = new List<PropConfigSettings>();
        }

        public class LowHealthEffectSettings
        {
            public bool Enabled { get; set; } = false;
            public List<LowHealthEffectConfig> Effects { get; set; } = new List<LowHealthEffectConfig>();
        }

        public class LowHealthEffectConfig
        {
            public bool Enabled { get; set; } = false;
            public float HealthPercent { get; set; }
            public ConfigVector Location { get; set; }
            public ConfigVector Scale { get; set; } = Vector3.one;
            public ConfigVector Rotation { get; set; }
            public string PrefabPath { get; set; }
            public ulong SkinId { get; set; }
        }

        public class PropConfigSettings : ConfigSettings, IBaseVehicleConfigPrefab, IBaseVehicleConfigFlags
        {
            public virtual string PrefabPath { get; set; }
            public Flags Flags { get; set; }
        }

        public class ConfigSettings : BaseConfigSettings, IBaseVehicleConfigEnabled
        {
            public virtual bool Enabled { get; set; }
        }

        public class BaseConfigSettings : IBaseVehicleConfigLocation, IBaseVehicleConfigRotation, IBaseVehicleConfigScale
        {
            public ConfigVector Location { get; set; }
            public ConfigVector Rotation { get; set; }
            public ConfigVector Scale { get; set; } = Vector3.one;
        }

        public class BaseLocationSetting : IBaseVehicleConfigLocation
        {
            public ConfigVector Location { get; set; }
        }

        public class HoverEngineConfig
        {
            public bool Enabled { get; set; }
            public ConfigVector Location { get; set; }
            public int HoverStrength { get; set; }
            public float HoverDistance { get; set; }
            public int Dampening { get; set; }
            public LayerFlag LayerMask { get; set; } = LayerFlag.Deployed | LayerFlag.Default | LayerFlag.Terrain | LayerFlag.Water | LayerFlag.World | LayerFlag.Tree;
        }

        public class HurtTriggerConfig
        {
            public ConfigVector Location { get; set; }
            public ConfigVector Size { get; set; } = new Vector3(0.1f, 0.1f, 0.1f);
            public float DamagePerSecond { get; set; } = 4f;
            public float DamageTickRate { get; set; } = 1f;
            public float DamageDelay { get; set; }
            public DamageType DamageType { get; set; } = DamageType.Collision;
            public bool IgnoreNPC { get; set; } = false;
            public float NpcMultiplier { get; set; } = 1f;
            public float ResourceMultiplier { get; set; } = 5f;
            public bool TriggerHitImpacts { get; set; } = true;
            public bool RequireUpAxis { get; set; }
            public bool UseSourceEntityDamageMultiplier { get; set; } = true;
            public bool IgnoreAllVehicleMounted { get; set; } = true;
            public float ActivationDelay { get; set; }
            public HurtTriggerType HurtTriggerType { get; set; }
            public float HurtTriggerMinSpeed { get; set; } = 1;
            public bool RequireEngineOn { get; set; }
            public LayerFlag LayerMask { get; set; } = LayerFlag.AI | LayerFlag.Tree | LayerFlag.Player_Server;
        }

        public class FoilageInteractionConfig
        {
            public ConfigVector Location { get; set; }
            public ConfigVector Size { get; set; } = new Vector3(0.1f, 0.1f, 0.1f);
            public bool CanRemoveFoilage { get; set; }
            public bool CanHarvestFood { get; set; }
            public int Modifier { get; set; } = 1;
        }

        //public class VehiclePhysicsTriggerConfig
        //{
        //    public bool Enabled { get; set; } = false;
        //    public Vector3 Location { get; set; }
        //    public Vector3 Size { get; set; } = new Vector3(0.1f, 0.1f, 0.1f);
        //}

        public class TowTriggerConfig
        {
            public bool Enabled { get; set; } = true;
            public ConfigSettings ReleaseButtonSettings { get; set; } = new ConfigSettings();
            public ConfigVector Location { get; set; }
            public ConfigVector Size { get; set; } = new Vector3(0.1f, 0.1f, 0.1f);
            public TowTriggerType TowTriggerType { get; set; } = TowTriggerType.Hitch;
            public ConfigVector TowAnchorPosition { get; set; }
            public bool ListenForLightsFromHitch { get; set; }
            public float BreakForce { get; set; } = 40000f;
            public float BreakTorque { get; set; } = 350f;
            public float MassScale { get; set; } = 1f;
            public float ConnectedMassScale { get; set; } = 1f;
            public float LinearLimitSpringDamper { get; set; } = 0;
            public float LinearLimitSpringStiffness { get; set; } = 0;
        }

        public class CustomBarrelConfiguration : IBaseVehicleConfigEnabled
        {
            public bool Enabled { get; set; } = true;
            public string MuzzleFx { get; set; }
            public ConfigVector MuzzlePos { get; set; }
            public ConfigVector MuzzleFxPos { get; set; }
            public ConfigVector MuzzleNormLocal { get; set; } = Vector3.zero;
        }

        public class LandingGearSettings
        {
            public bool Enabled { get; set; } = false;
            public bool UpdateWheelColliders { get; set; } = true;
            public float DragWhileOn { get; set; } = 0f;
            public float StallVelocityWhileOn { get; set; } = -1f;
            public float StallForceWhileOn { get; set; } = 0f;
            public float GroundedDistanceCheckWhileOn { get; set; } = -1f;
            public bool CanToggleOffWhenGrounded { get; set; } = false;
            public bool CanToggleByPassenger { get; set; } = false;
            public bool DisableWeaponsWhenOn { get; set; } = false;
            public bool DisableVTOLWhenOff { get; set; } = false;
            public bool ForceVTOLWhenOn { get; set; } = false;
            public bool AutoRetract { get; set; } = false;
            public float AutoRetractAfterSeconds { get; set; } = 5f;
            public List<PropConfigSettings> SwitchConfigs { get; set; } = new List<PropConfigSettings>();
            public List<JointConfig> LandingPropConfigs { get; set; } = new List<JointConfig>();
        }

        public class GForceSettings
        {
            public bool Enabled { get; set; } = false;
            public float BlackoutThreshold { get; set; } = 7;
            public float RedoutThreshold { get; set; } = -7;
            public bool DamagePlayer { get; set; } = true;
        }

        public class JointConfig : IBaseVehicleSubPropList, IBaseVehicleConfigScale
        {
            public ConfigVector Location { get; set; }
            public ConfigVector Rotation { get; set; }
            public ConfigVector Scale { get; set; } = Vector3.one;
            public int ItemId { get; set; }
            public ulong SkinId { get; set; }
            public ConfigVector RotationWhenOn { get; set; }
            public ConfigVector RotationWhenOff { get; set; }
            public ConfigVector PositionWhenOn { get; set; }
            public ConfigVector PositionWhenOff { get; set; }
            public bool DefaultPositionIsOff { get; set; } = true;
            public float LerpSpeed { get; set; } = 0.5f;
            public List<PropConfig> SubPropConfigs { get; set; } = new List<PropConfig>();
            public HurtTriggerSettings HurtTriggerSettings { get; set; } = new HurtTriggerSettings();
            public List<PhysicsTrigger> PhysicsTriggers { get; set; } = new List<PhysicsTrigger>();
            public List<SafeZoneTrigger> SafeZoneTriggers { get; set; } = new List<SafeZoneTrigger>();
            public FoilageInteractionSettings FoilageInteraction { get; set; } = new FoilageInteractionSettings();
        }

        public class CustomSwitchConfig : BaseSwitchConfig, IBaseVehicleSubPropList
        {
            public bool ToggleJoint { get; set; }
            public List<JointConfig> JointConfigs { get; set; } = new List<JointConfig>();
            public List<PropConfig> SubPropConfigs { get; set; } = new List<PropConfig>();
            public List<SirenConfig> SirenConfigs { get; set; } = new List<SirenConfig>();
        }

        public class BaseSwitchConfig : ConfigSettings, IBaseVehicleConfigPrefab
        {
            public string PrefabPath { get; set; }
            public bool ToggleLights { get; set; }
            public bool ToggleDoors { get; set; }
            public bool TogglePropellers { get; set; }
            public bool ToggleSirens { get; set; }
            public bool ToggleSails { get; set; }
            public bool ToggleAnchors { get; set; }
            public bool IgnoreCodelock { get; set; } = true;
        }

        public class SirenConfig : ConfigSettings
        {
            public List<SirenTone> Tones { get; set; } = new List<SirenTone>();
        }

        public class SirenTone
        {
            public Notes Note { get; set; }
            public NoteType NoteType { get; set; }
            public int Octave { get; set; }
            public float Duration { get; set; }
        }

        public class CustomWheelConfig : IBaseVehicleConfigEnabled
        {
            public virtual bool Enabled { get; set; }
            public BaseSwitchConfig SwitchConfig { get; set; } = new BaseSwitchConfig();
            public ConfigSettings RotationWheel { get; set; } = new ConfigSettings();
            public ConfigSettings PositionWheel { get; set; } = new ConfigSettings();
            public JointConfig JointConfig { get; set; } = new JointConfig();
        }

        public class CustomInputJointConfig : IBaseVehicleConfigEnabled
        {
            public virtual bool Enabled { get; set; }
            public BUTTON OnButton { get; set; }
            public BUTTON OffButton { get; set; }
            public BaseSwitchConfig SwitchConfig { get; set; } = new BaseSwitchConfig();
            public JointConfig JointConfig { get; set; } = new JointConfig();
            public DynamicTowingSettings DynamicTowingSettings { get; set; } = new DynamicTowingSettings();
        }

        public class DriftWheelFrictionCurveConfig : WheelFrictionCurveConfig
        {
            public bool Enabled { get; set; } = false;
        }

        public class WheelFrictionCurveConfig
        {
            public float ExtremumSlip { get; set; }

            public float ExtremumValue { get; set; }

            public float AsymptoteSlip { get; set; }

            public float AsymptoteValue { get; set; }

            public float Stiffness { get; set; }
        }


        public class VehicleWeakspotConfig : WeakspotConfig
        {
            public bool LoseControlOnDeath { get; set; } = true;
        }

        public class WeakspotData
        {
            public Weakspot Weakspot;
            public BaseEntity Entity;
        }

        public class WeakspotConfig : IBaseVehicleSubPropList, IBaseVehicleConfigEnabled
        {
            public bool Enabled { get; set; }
            public float MaxHealth { get; set; } = 500f;
            public float HealthFractionOnDestroyed { get; set; } = 0.5f;
            public bool HideWeakspotOnDeath { get; set; } = false;
            public string DestroyedParticlesFx { get; set; } = "assets/prefabs/npc/patrol helicopter/effects/component_destroyed.prefab";
            public string DamagedParticlesFx { get; set; } = "assets/prefabs/npc/patrol helicopter/effects/component_damged.prefab";
            public ConfigVector EffectLocalPosition { get; set; }
            public List<PropConfig> SubPropConfigs { get; set; } = new List<PropConfig>();
            public LowHealthEffectSettings LowHealthEffectSettings { get; set; } = new LowHealthEffectSettings();
        }

        public class MagnetSettings : IBaseVehicleConfigEnabled
        {
            public bool Enabled { get; set; }
            public bool ScaleScrapResourcesByHealth { get; set; } = true;
            public ConfigVector ShredDirection { get; set; } = Vector3.forward;
            public bool RequireObjectOff { get; set; }
            public Dictionary<string, int> ShredResources { get; set; } = new Dictionary<string, int>();
        }

        public class PipePropSettings : BaseConfigSettings, IBaseVehicleConfigPrefab
        {
            public string PrefabPath { get; set; } = STORAGE_ADAPTOR_PREFAB;
            public List<PipePropSetting> PipeSettings { get; set; } = new List<PipePropSetting>();
        }

        public class PipePropSetting
        {
            public bool Enabled { get; set; }
            public IOType PipeType { get; set; } = IOType.Industrial;
            public WireColour PipeColor { get; set; } = WireColour.Gray;
            public float PipeThickness { get; set; } = 1f;
            public ConfigVector OriginRotation { get; set; } = Vector3.up;
            public List<LinePointSetting> LinePoints { get; set; } = new List<LinePointSetting>();
        }

        public class LinePointSetting : IBaseVehicleConfigLocation
        {
            public ConfigVector Location { get; set; }
        }

        public class SamTargetType
        {
            public float ScanRadius { get; set; }
            public float SpeedMultiplier { get; set; }
            public float TimeBetweenBursts { get; set; }

            public SamTargetType()
            {

            }

            public SamTargetType(float scanRadius, float speedMultiplier, float timeBetweenBursts)
            {
                this.ScanRadius = scanRadius;
                this.SpeedMultiplier = speedMultiplier;
                this.TimeBetweenBursts = timeBetweenBursts;
            }

            public static implicit operator SamSite.SamTargetType(SamTargetType v)
            {
                return new SamSite.SamTargetType(v.ScanRadius, v.SpeedMultiplier, v.TimeBetweenBursts);
            }

            public static implicit operator SamTargetType(SamSite.SamTargetType v)
            {
                return new SamTargetType()
                {
                    ScanRadius = v.scanRadius,
                    SpeedMultiplier = v.speedMultiplier,
                    TimeBetweenBursts = v.timeBetweenBursts
                };
            }
        }

        public enum PropType
        {
            World = 0,
            Entity = 1,
            Gib = 2,
            CustomCombatEntity = 3,
            None = 4,
            Unused1 = 5,
            CustomBaseMountable = 6,
            Unused2 = 7,
            CustomDoor = 8
        }

        public enum PartType
        {
            Propeller,
            CarEngine,
            GenericShrunkEngine,
            Prop,
            MiniGun,
            Mount,
            Seat,
            IOEngine,
            DriverSeat,
            LocalMount,
            LocalSeat,
            ConditionalLight,
            Wheel,
            GimbalItem,
            IOShrunkEngine,
            PropEngine,
            PropShrunkEngine,
            M249,
            IOEntity,
            GenericEngine,
            CustomCamera,
            Light,
            ShrunkLight,
            SpecialGun,
            ModularCarChassis,
            IOPropeller,
            AudioVisualisationEntity,
            ConditionalProp,
            BuildingBlock,
            Boat,
            TurretMount,
            Ladder
        }

        public enum CounterType
        {
            Health,
            Fuel,
            Altitude,
            Speed
        }

        public enum AmmoSource
        {
            Inventory,
            Calories
        }

        public enum BarrelConfiguration
        {
            DualFront,
            DualBottom,
            Bottom,
            DualSide,
            Unused1,
            Unused2,
            Custom,
            SpecialGuns,
            SpecialGunsM249,
            Sprinkler
        }

        public enum ProjectileType
        {
            Bullet = 0,
            ServerProjectile = 1,
            BombProjectile = 2,
            SpecialGuns = 3,
            Flare = 4,
            SeekingServerProjectile = 5,
            DroppedItem = 6,
            Entity = 7,
            SpecialGunsSync = 8,
            Effect = 9,
            GuidedServerProjectile = 10,
            AntiVehicleSeekerProjectile = 11,
            BombEntityProjectile = 12,
            TVGuidedServerProjectile = 13,
            Water = 14,
            TorpedoProjectile = 15,
            NukeServerProjectile = 16
        }

        public enum HurtTriggerType
        {
            Front,
            Back
        }

        public enum TowTriggerType
        {
            Hitch,
            Receiver,
            SemiHitch,
            SemiReceiver
        }

        public enum FuelSource
        {
            Container,
            None,
            Calories
        }

        public enum EngineType
        {
            Small,
            Large
        }

        public enum BoostType
        {
            Hold,
            PressOnce,
            Continuous
        }

        public enum BodyType
        {
            WorldItem,
            Gib,
            Prefab
        }

        public enum PhysicsTriggerType
        {
            Player
        }

        [Flags]
        public enum LayerFlag
        {
            Default = 1 << 0,
            TransparentFX = 1 << 1,
            Ignore_Raycast = 1 << 2,
            Reserved1 = 1 << 3,
            Water = 1 << 4,
            UI = 1 << 5,
            Reserved2 = 1 << 6,
            Reserved3 = 1 << 7,
            Deployed = 1 << 8,
            Ragdoll = 1 << 9,
            Invisible = 1 << 10,
            AI = 1 << 11,
            Player_Movement = 1 << 12,
            Vehicle_Detailed = 1 << 13,
            Game_Trace = 1 << 14,
            Vehicle_World = 1 << 15,
            World = 1 << 16,
            Player_Server = 1 << 17,
            Trigger = 1 << 18,
            Harvestable = 1 << 19,
            Physics_Projectile = 1 << 20,
            Construction = 1 << 21,
            Construction_Socket = 1 << 22,
            Terrain = 1 << 23,
            Transparent = 1 << 24,
            Clutter = 1 << 25,
            Bush = 1 << 26,
            Vehicle_Large = 1 << 27,
            Prevent_Movement = 1 << 28,
            Prevent_Building = 1 << 29,
            Tree = 1 << 30,
            Physics_Debris = 1 << 31,
        }

        public enum IOType
        {
            Electric = 0,
            Fluidic = 1,
            //Kinetic = 2,
            //Generic = 3,
            Industrial = 4
        }

        public enum WireColour
        {
            Gray = 0,
            Red = 1,
            Green = 2,
            Blue = 3,
            Yellow = 4,
            Pink = 5,
            Purple = 6,
            Orange = 7,
            //White,
            //LightBlue = 8,
            //Invisible,
            //Count
        }

        public enum StorageContainerType
        {
            Storage = 0,
            Furnace = 1,
            Workbench = 2,
            Fridge = 3,
            MixingTable = 4,
            BBQ = 5,
            FuellessBBQ = 6,
            Locker = 7,
            CookingBench = 8,
            ShopFront = 9,
            Beehive = 10
        }

        public enum ContainerAdaptorType
        {
            Input = 0,
            Output = 1
        }

        #endregion

        #region Utilities

        //gui stuff
        public static class CrosshairUtilities
        {
            const string CROSSHAIR_CUI_NAME = "kvc.crosshair";
            const int CROSSHAIR_SIZE = 14;
            const string CROSSHAIR_CHARACTER = "•";

            public static CuiElementContainer ContainerMain;
            public static string ContainerMainJson;

            public static void GenerateGUI()
            {
                ContainerMain = new CuiElementContainer();

                ContainerMain.Add(new CuiElement
                {
                    Name = CROSSHAIR_CUI_NAME,
                    Parent = "Overlay",
                    DestroyUi = CROSSHAIR_CUI_NAME,
                    Components =
                        {
                            new CuiTextComponent
                            {
                                FontSize = CROSSHAIR_SIZE,
                                Align = TextAnchor.MiddleCenter,
                                Color = $"1 1 1 0.8",
                                Text = CROSSHAIR_CHARACTER
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0.48 0.48",
                                AnchorMax = "0.52 0.52"
                            }
                        }
                });


                ContainerMainJson = ContainerMain.ToJson();
            }

            public static void Unload()
            {
                foreach (var player in BasePlayer.activePlayerList)
                {
                    HideGUI(player);
                }

                ContainerMain.Clear();
                ContainerMain = null;
                ContainerMainJson = null;
            }

            public static void ShowGUI(BasePlayer player)
            {
                CuiHelper.AddUi(player, ContainerMainJson);
            }

            public static void HideGUI(BasePlayer player)
            {
                CuiHelper.DestroyUi(player, CROSSHAIR_CUI_NAME);
            }
        }

        public class ColorCode
        {
            public string hexValue;
            public UnityEngine.Color rustValue;
            public string rustString;
            public ColorCode(string hex)
            {
                hex = hex.ToUpper();

                hexValue = "#" + hex;

                //extract the R, G, B
                var r = (float)short.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber) / 255;
                var g = (float)short.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber) / 255;
                var b = (float)short.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber) / 255;

                rustValue = new UnityEngine.Color(r, g, b);
                rustString = $"{r} {g} {b}";
            }
        }

        public static class GForceGUIUtilities
        {
            static CuiElementContainer BlackoutContainer;
            static CuiElementContainer RedoutContainer;

            static string BlackoutJson;
            static string RedoutJson;

            const string BLACKOUT_CUI_NAME = "kvc.blackout";
            const string BLACKOUT_CUI_NAME2 = "kvc.blackout2";
            const string BLACKOUT_CUI_NAME3 = "kvc.blackout3";


            const string REDOUT_CUI_NAME = "kvc.blackout";

            const string RED = "1 0 0 1";
            const string BLACK = "0 0 0 1";

            public static void GenerateGUI()
            {
                GenerateBlackoutGUI();
                GeneratedRedoutGUI();
            }

            static void GenerateBlackoutGUI()
            {
                BlackoutContainer = new CuiElementContainer();

                BlackoutContainer.Add(new CuiPanel
                {
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    Image = new CuiImageComponent()
                    {
                        Sprite = "assets/content/ui/ui.background.transparent.radial.psd",
                        Color = BLACK,
                        FadeIn = 5f,
                    },
                    FadeOut = 5f
                }, "Overlay", BLACKOUT_CUI_NAME, destroyUi: BLACKOUT_CUI_NAME);

                BlackoutContainer.Add(new CuiPanel
                {
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    Image = new CuiImageComponent()
                    {
                        Sprite = "assets/content/ui/ui.background.transparent.radial.psd",
                        Color = BLACK,
                        FadeIn = 5f,
                    },
                    FadeOut = 5f
                }, "Overlay", BLACKOUT_CUI_NAME2, destroyUi: BLACKOUT_CUI_NAME2);

                BlackoutContainer.Add(new CuiPanel
                {
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    Image = new CuiImageComponent()
                    {
                        Sprite = "assets/content/ui/ui.background.transparent.radial.psd",
                        Color = BLACK,
                        FadeIn = 5f,
                    },
                    FadeOut = 5f
                }, "Overlay", BLACKOUT_CUI_NAME3, destroyUi: BLACKOUT_CUI_NAME3);

                BlackoutJson = BlackoutContainer.ToJson();
            }

            static void GeneratedRedoutGUI()
            {
                RedoutContainer = new CuiElementContainer();

                RedoutContainer.Add(new CuiPanel
                {
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    Image = new CuiImageComponent()
                    {
                        Sprite = "assets/content/ui/ui.background.transparent.radial.psd",
                        Color = RED,
                        FadeIn = 5f,
                    },
                    FadeOut = 5f
                }, "Overlay", REDOUT_CUI_NAME, destroyUi: REDOUT_CUI_NAME);

                RedoutJson = RedoutContainer.ToJson();
            }

            public static void Unload()
            {
                foreach (var player in BasePlayer.activePlayerList)
                {
                    HideBlackoutGUI(player);
                    HideRedoutGUI(player);
                }

                BlackoutContainer.Clear();
                BlackoutContainer = null;
                BlackoutJson = null;

                RedoutContainer.Clear();
                RedoutContainer = null;
                RedoutJson = null;
            }

            public static void ShowBlackoutGUI(BasePlayer player)
            {
                CuiHelper.AddUi(player, BlackoutJson);
            }

            public static void HideBlackoutGUI(BasePlayer player)
            {
                CuiHelper.DestroyUi(player, BLACKOUT_CUI_NAME);
                CuiHelper.DestroyUi(player, BLACKOUT_CUI_NAME2);
                CuiHelper.DestroyUi(player, BLACKOUT_CUI_NAME3);
            }

            public static void ShowRedoutGUI(BasePlayer player)
            {
                CuiHelper.AddUi(player, RedoutJson);
            }

            public static void HideRedoutGUI(BasePlayer player)
            {
                CuiHelper.DestroyUi(player, REDOUT_CUI_NAME);
            }
        }

        public static class ItemUtilities
        {
            public static Item SpawnItem(string shortname, int amount = 1, ulong skinId = 0)
            {
                var itemDef = ItemManager.itemList
                        .Find(it => it.shortname
                            .Equals(shortname, StringComparison.InvariantCultureIgnoreCase)
                        );

                if (itemDef == null)
                {
                    Debug.LogError($"Item {shortname} could not be found. Skipping spawning");
                    return null;
                }

                var spawnedItem = ItemManager.CreateByItemID(itemDef.itemid, amount, skinId);
                return spawnedItem;
            }
        }

        public class StatusUtilities
        {
            private static bool statusBarsEnabled = false;
            const string StatusName = "VehicleHealth";
            const string SET_STATUS_HOOK = "SetStatus";
            const string SET_STATUS_PROPERTY_HOOK = "SetStatusProperty";
            const string PROGRESS_KEY = "progress";
            const string TITLE_KEY = "title";

            public static Dictionary<string, object> StatusArgs = new Dictionary<string, object>
            {
                [PROGRESS_KEY] = 0.0f,
                [TITLE_KEY] = 0.0f
            };

            public static void InitializeStatusBars()
            {
                if (Instance.SimpleStatus == null)
                {
                    return;
                }

                statusBarsEnabled = true;
                Instance.SimpleStatus.CallHook("CreateStatus", Instance, StatusName, new Dictionary<string, object>
                {
                    ["color"] = "1.0 1.0 1.0 0.028",
                    ["titleColor"] = "1 1 1 0.8",
                    ["icon"] = "assets/icons/gear.png",
                    ["iconColor"] = "1 1 1 0.5",
                    ["progress"] = 100f, // It is important to set the starting value if you plan for this status to be a progress bar!
                    ["progressColor"] = "0.0 0.0 0.0 0.65",
                    ["rank"] = -1 // TIP: The rank property can determine where in the status list this status will appear among the other custom statuses. Lower numbers come first.
                });
            }

            public static void UpdateHealth(IVehicle vehicle)
            {
                if (!statusBarsEnabled || Instance.isUnloading)
                {
                    return;
                }

                StatusArgs[PROGRESS_KEY] = vehicle.healthFraction;
                StatusArgs[TITLE_KEY] = $"{(int)vehicle.BaseEntity.Health()}";

                for (int i = 0; i < vehicle.MountPoints.Count; i++)
                {
                    var mp = vehicle.MountPoints[i];
                    if (!mp.mountable.AnyMounted())
                    {
                        continue;
                    }

                    var mounted = mp.mountable.GetMounted();
                    if (object.ReferenceEquals(mounted, null))
                    {
                        continue;
                    }

                    Instance.SimpleStatus.CallHook(SET_STATUS_HOOK, mounted.UserIDString, StatusName, int.MaxValue);
                    Instance.SimpleStatus.CallHook(SET_STATUS_PROPERTY_HOOK, mounted.UserIDString, StatusName, StatusArgs);
                }
            }

            public static void HideHealth(BasePlayer player)
            {
                if (!statusBarsEnabled)
                {
                    return;
                }

                Instance.SimpleStatus.CallHook(SET_STATUS_HOOK, player.UserIDString, StatusName, 0);
            }
        }

        public class HelperUtilities
        {
            public static float MouseToBinary(float amount, float min, float max)
            {
                return Mathf.Clamp(amount, min, max);
            }
        }

        public class BoostUtilities
        {
            public static void TryDisableBoost(IVehicle vehicle)
            {
                if (!vehicle.VehicleConfig.BoostSettings.Enabled)
                {
                    return;
                }

                if (!vehicle.IsBoosting)
                {
                    return;
                }

                if (vehicle.CustomHasDriver())
                {
                    return;
                }

                var currentTime = Time.realtimeSinceStartup;
                vehicle.LastBoostTime = currentTime;
                vehicle.UpdateBoost(false);
            }

            public static void UpdateBoost(IVehicle vehicle, BasePlayer driver)
            {
                if (!vehicle.VehicleConfig.BoostSettings.Enabled)
                {
                    return;
                }

                var currentTime = Time.realtimeSinceStartup;
                if (!vehicle.BaseEntity.IsOn())
                {
                    DisableBoost(vehicle);
                    return;
                }

                var boostSettings = vehicle.VehicleConfig.BoostSettings;
                if (!driver.serverInput.IsDown(boostSettings.Button))
                {
                    if (boostSettings.BoostType == BoostType.Continuous)
                    {
                        if (vehicle.BoostTime > 0)
                        {
                            vehicle.BoostTime -= Time.fixedDeltaTime;
                            if (vehicle.BoostTime < 0)
                            {
                                vehicle.BoostTime = 0;
                                vehicle.OnBoostTimeUpdate?.Invoke();
                                return;
                            }

                            vehicle.OnBoostTimeUpdate?.Invoke();
                        }

                        if (!vehicle.IsBoosting)
                        {
                            return;
                        }

                        DisableBoost(vehicle);
                        return;
                    }
                    else if (boostSettings.BoostType == BoostType.Hold)
                    {
                        DisableBoost(vehicle);
                        return;
                    }

                    if (!vehicle.IsBoosting)
                    {
                        return;
                    }
                }

                if (!vehicle.IsBoosting)
                {
                    if ((boostSettings.BoostType != BoostType.Continuous || vehicle.BoostOnCooldown) && boostSettings.Cooldown > 0 && currentTime <= vehicle.LastBoostTime + boostSettings.Cooldown)
                    {
                        return;
                    }

                    vehicle.BoostOnCooldown = false;
                }
                else
                {
                    if (boostSettings.BoostType == BoostType.Continuous)
                    {
                        vehicle.BoostTime += Time.fixedDeltaTime;
                        vehicle.OnBoostTimeUpdate?.Invoke();

                        if (boostSettings.Cooldown > 0 && vehicle.BoostOnCooldown && currentTime >= vehicle.LastBoostTime + boostSettings.Cooldown)
                        {
                            DisableBoost(vehicle);
                            return;
                        }

                        if (boostSettings.MaxBoostDuration > 0 && vehicle.BoostTime >= boostSettings.MaxBoostDuration)
                        {
                            vehicle.BoostTime = 0;
                            vehicle.OnBoostTimeUpdate?.Invoke();
                            DisableBoost(vehicle, true);
                            return;
                        }
                    }
                    else if (boostSettings.MaxBoostDuration > 0 && currentTime >= vehicle.LastBoostTime + boostSettings.MaxBoostDuration)
                    {
                        DisableBoost(vehicle, true);
                        return;
                    }
                }

                if (!vehicle.BoostFuelSystem.HasFuel())
                {
                    DisableBoost(vehicle);
                    return;
                }

                vehicle.BoostFuelSystem.TryUseFuel(Time.fixedDeltaTime, boostSettings.BoostFuelPerSecond);
                var wasBoosting = vehicle.IsBoosting;
                vehicle.UpdateBoost(true);

                if (!wasBoosting && vehicle.IsBoosting)
                {
                    vehicle.LastBoostTime = currentTime;
                }
            }

            static void DisableBoost(IVehicle vehicle, bool? cooldown = null)
            {
                if (!vehicle.IsBoosting)
                {
                    return;
                }

                var currentTime = Time.realtimeSinceStartup;
                vehicle.UpdateBoost(false);
                vehicle.LastBoostTime = currentTime;
                if (cooldown.HasValue)
                {
                    vehicle.BoostOnCooldown = cooldown.Value;
                }
            }
        }

        public class EjectUtilities
        {
            public static bool CanEject(IVehicle vehicle)
            {
                if (!vehicle.VehicleConfig.EjectSettings.Enabled)
                {
                    return false;
                }

                if (vehicle.VehicleConfig.EjectSettings.RequirePowerToEject && !vehicle.BaseVehicle.IsOn())
                {
                    return false;
                }

                if (vehicle.BaseVehicle.IsDead())
                {
                    return false;
                }

                if (!vehicle.IsEjectEligible)
                {
                    return false;
                }

                return true;
            }

            public static bool ShouldEject(IVehicle vehicle)
            {
                if (!vehicle.VehicleConfig.EjectSettings.Enabled)
                {
                    return false;
                }

                if (vehicle.VehicleConfig.EjectSettings.AutoEjectIfLowHealth)
                {
                    var lowHealthThreshold = vehicle.BaseVehicle._maxHealth * vehicle.VehicleConfig.EjectSettings.LowHealthThreshold;
                    if (vehicle.BaseVehicle.health <= lowHealthThreshold)
                    {
                        return true;
                    }
                }

                return false;
            }

            public static void TryEject(IVehicle vehicle, BasePlayer player)
            {
                var canEject = CanEject(vehicle);
                if (!canEject)
                {
                    return;
                }

                Eject(vehicle, player);
            }

            public static void Eject(IVehicle vehicle, BasePlayer player)
            {
                if (player.IsDead())
                {
                    return;
                }

                if (Physics.Raycast(player.eyes.position, vehicle.BaseVehicle.transform.up, out RaycastHit hit, vehicle.VehicleConfig.EjectSettings.PreventEjectDistance, GENERAL_COLLIDER))
                {
                    return;
                }

                var ejectMods = vehicle.VehicleConfig.EjectSettings.EjectDirectionModifiers;
                var ejectDir = vehicle.BaseVehicle.transform.up;
                ejectDir.x *= ejectMods.x;
                ejectDir.y *= ejectMods.y;
                ejectDir.z *= ejectMods.z;

                var ejectVelocity = ejectDir * vehicle.VehicleConfig.EjectSettings.Velocity;
                DroppedItem worldModel = ItemManager.CreateByItemID(vehicle.VehicleConfig.EjectSettings.ItemId)
                        .Drop(player.transform.position + vehicle.BaseVehicle.transform.up * 2, ejectVelocity, vehicle.BaseVehicle.transform.rotation)
                        .GetComponent<DroppedItem>();

                worldModel.allowPickup = false;
                worldModel.enableSaving = false;
                worldModel.syncPosition = true;
                worldModel.Invoke("IdleDestroy", float.MaxValue);
                worldModel.EnableGlobalBroadcast(true);
                worldModel.CancelInvoke(worldModel.IdleDestroy);

                if (player.isMounted)
                {
                    vehicle.BaseVehicle.DismountPlayer(player);
                }

                var ejector = worldModel.gameObject.AddComponent<PlayerEjector>();
                ejector.Initialize(vehicle.VehicleConfig.EjectSettings);
                ejector.MountPlayer(player);

                if (vehicle.VehicleConfig.EjectSettings.DestroyVehicleOnEject && !vehicle.BaseVehicle.AnyMounted())
                {
                    if (vehicle.VehicleConfig.EjectSettings.DestroyVehicleOnEjectDelay > 0)
                    {
                        vehicle.BaseVehicle.Invoke(() =>
                        {
                            if (vehicle.IsDead() || vehicle.BaseVehicle.AnyMounted())
                            {
                                return;
                            }

                            vehicle.BaseVehicle.Hurt(vehicle.BaseVehicle._maxHealth);
                        }, vehicle.VehicleConfig.EjectSettings.DestroyVehicleOnEjectDelay);
                    }
                    else
                    {
                        vehicle.BaseVehicle.Hurt(vehicle.BaseVehicle._maxHealth);
                    }
                }
            }
        }

        public class WeaponUtilities
        {
            const float FLARE_LAUNCH_VAL = 10f;

            public static bool UpdateWeapon(IWeaponController controller, BasePlayer driver, WeaponSystem weaponSystem)
            {
                if (weaponSystem == null || weaponSystem.Config == null)
                {
                    return false;
                }

                var currentTime = Time.realtimeSinceStartup;
                var weaponConfiguration = weaponSystem.Config;

                if (weaponConfiguration.EnableReload && weaponSystem.MagazineCapacity <= 0)
                {
                    if (weaponSystem.LastFiredTime + weaponConfiguration.ReloadTime > currentTime)
                    {
                        return false;
                    }

                    weaponSystem.MagazineCapacity = weaponConfiguration.MagazineCapacity;
                }

                if (!object.ReferenceEquals(driver, null) && !driver.serverInput.IsDown(weaponConfiguration.Button))
                {
                    return false;
                }

                if (string.IsNullOrEmpty(weaponConfiguration.AmmoPrefab) && weaponConfiguration.ProjectileType != ProjectileType.Flare && weaponConfiguration.ProjectileType != ProjectileType.Effect)
                {
                    return false;
                }

                if (currentTime <= weaponSystem.LastFiredTime + weaponConfiguration.FireRate)
                {
                    return false;
                }

                weaponSystem.LastFiredTime = currentTime;
                bool canFireWeapon = CanFireWeapon(controller, driver, weaponSystem);
                if (!canFireWeapon)
                {
                    return false;
                }

                if (weaponConfiguration.EnableReload)
                {
                    --weaponSystem.MagazineCapacity;
                }

                Vector3 muzzlePos = GetMuzzlePosition(controller, weaponConfiguration.BarrelConfig, weaponSystem.CurrentBarrel, weaponConfiguration.CustomBarrelConfigs);
                if (muzzlePos == Vector3.zero)
                {
                    return false;
                }

                var aimMod = controller.Forward;
                if (weaponConfiguration.BarrelConfig == BarrelConfiguration.SpecialGuns)
                {
                    aimMod = controller.SpecialGuns[weaponSystem.CurrentBarrel].Forward;
                }
                else if (weaponConfiguration.BarrelConfig == BarrelConfiguration.DualBottom)
                {
                    aimMod = Vector3.up * -1.5f;
                }

                Vector3 modifiedAimDir = AimConeUtil.GetAimConeQuat(weaponConfiguration.AimCone) * aimMod;
                var barrelCount = weaponConfiguration.BarrelConfig == BarrelConfiguration.Custom ? weaponConfiguration.CustomBarrelConfigs.Count :
                    (weaponConfiguration.BarrelConfig == BarrelConfiguration.SpecialGuns || weaponConfiguration.BarrelConfig == BarrelConfiguration.SpecialGunsM249 || weaponConfiguration.BarrelConfig == BarrelConfiguration.Sprinkler) ? controller.SpecialGuns.Count :
                    2;

                var ammoPerShot = weaponConfiguration.AmmoPerShot;
                switch (weaponConfiguration.ProjectileType)
                {
                    case ProjectileType.SpecialGuns:
                    case ProjectileType.Bullet:
                        if (!IsAmmoAvailable(controller, driver, weaponConfiguration, ammoPerShot, muzzlePos))
                        {
                            return false;
                        }

                        ShootBullet(driver, controller.BaseEntity, muzzlePos, modifiedAimDir * weaponConfiguration.AmmoSpeed, weaponSystem.DamageTypes, weaponConfiguration.AmmoPrefab, weaponConfiguration.SendToNetworkGroupOnly, weaponConfiguration.ImpactEffect);
                        TriggerMuzzleEffect(controller, weaponSystem);
                        UpdateRecoil(controller, weaponSystem, muzzlePos, controller.Transform.rotation);
                        UpdateBarrel(ref weaponSystem.CurrentBarrel, barrelCount);
                        break;

                    case ProjectileType.ServerProjectile:
                    case ProjectileType.TorpedoProjectile:
                        if (!IsAmmoAvailable(controller, driver, weaponConfiguration, ammoPerShot, muzzlePos))
                        {
                            return false;
                        }

                        FireWithServerProjectile(driver, controller.BaseEntity, muzzlePos, modifiedAimDir, weaponConfiguration, weaponSystem.DamageTypes, weaponConfiguration.SendToNetworkGroupOnly);
                        TriggerMuzzleEffect(controller, weaponSystem);
                        UpdateRecoil(controller, weaponSystem, muzzlePos, controller.Transform.rotation);
                        UpdateBarrel(ref weaponSystem.CurrentBarrel, barrelCount);
                        break;

                    case ProjectileType.AntiVehicleSeekerProjectile:
                        if (!IsAmmoAvailable(controller, driver, weaponConfiguration, 1, muzzlePos))
                        {
                            return false;
                        }

                        var avsp = FireWithServerProjectile(driver, controller.BaseEntity, muzzlePos, modifiedAimDir, weaponConfiguration, weaponSystem.DamageTypes, weaponConfiguration.SendToNetworkGroupOnly, false);
                        UpdateSeeker(avsp, controller.BaseEntity, weaponSystem, true, modifiedAimDir, SeekerStrength.LOW, SeekerStrength.LOW);
                        TriggerMuzzleEffect(controller, weaponSystem);
                        UpdateRecoil(controller, weaponSystem, muzzlePos, controller.Transform.rotation);
                        UpdateBarrel(ref weaponSystem.CurrentBarrel, barrelCount);

                        break;

                    case ProjectileType.SeekingServerProjectile:
                        if (!IsAmmoAvailable(controller, driver, weaponConfiguration, 1, muzzlePos))
                        {
                            return false;
                        }

                        var sp = FireWithServerProjectile(driver, controller.BaseEntity, muzzlePos, modifiedAimDir, weaponConfiguration, weaponSystem.DamageTypes, weaponConfiguration.SendToNetworkGroupOnly, false);
                        UpdateSeeker(sp, controller.BaseEntity, weaponSystem, false, modifiedAimDir, SeekerStrength.MEDIUM, SeekerStrength.HIGHEST);
                        TriggerMuzzleEffect(controller, weaponSystem);
                        UpdateRecoil(controller, weaponSystem, muzzlePos, controller.Transform.rotation);
                        UpdateBarrel(ref weaponSystem.CurrentBarrel, barrelCount);

                        break;

                    case ProjectileType.GuidedServerProjectile:
                        if (!IsAmmoAvailable(controller, driver, weaponConfiguration, 1, muzzlePos))
                        {
                            return false;
                        }

                        FireGuidedRocket(driver, controller.BaseEntity, muzzlePos, modifiedAimDir, weaponSystem, weaponConfiguration.SendToNetworkGroupOnly);
                        TriggerMuzzleEffect(controller, weaponSystem);
                        UpdateRecoil(controller, weaponSystem, muzzlePos, controller.Transform.rotation);
                        UpdateBarrel(ref weaponSystem.CurrentBarrel, barrelCount);
                        break;

                    case ProjectileType.TVGuidedServerProjectile:
                        if (!IsAmmoAvailable(controller, driver, weaponConfiguration, 1, muzzlePos))
                        {
                            return false;
                        }

                        var tvsp = FireWithServerProjectile(driver, controller.BaseEntity, muzzlePos, modifiedAimDir, weaponConfiguration, weaponSystem.DamageTypes, weaponConfiguration.SendToNetworkGroupOnly, true);
                        TriggerMuzzleEffect(controller, weaponSystem);
                        UpdateRecoil(controller, weaponSystem, muzzlePos, controller.Transform.rotation);
                        UpdateBarrel(ref weaponSystem.CurrentBarrel, barrelCount);
                        if (controller is ITVGuidedRocketController tVGuidedRocketController)
                        {
                            var spdc = PropUtilities.CreateCustomEntity<TVGuidedRocketCamera>(new Vector3(0, 0.5f, 0), Vector3.zero, new Vector3(0.0001f, 0.0001f, 0.0001f), CAMERA_PREFAB, tvsp, 0, null, false);
                            tVGuidedRocketController.OnTVGuidedProjectileFired(spdc);
                        }

                        break;

                    case ProjectileType.BombProjectile:
                        if (!IsAmmoAvailable(controller, driver, weaponConfiguration, ammoPerShot, muzzlePos))
                        {
                            return false;
                        }

                        FireDroppedItemProjectile(driver, muzzlePos, modifiedAimDir, weaponSystem, controller.Transform.forward, weaponConfiguration.AmmoSkinId, true, weaponConfiguration.SendToNetworkGroupOnly);
                        TriggerMuzzleEffect(controller, weaponSystem);
                        UpdateBarrel(ref weaponSystem.CurrentBarrel, barrelCount);
                        break;

                    case ProjectileType.BombEntityProjectile:
                        if (!IsAmmoAvailable(controller, driver, weaponConfiguration, ammoPerShot, muzzlePos))
                        {
                            return false;
                        }

                        FireEntityBomb(driver, muzzlePos, weaponSystem, controller.Transform.forward, weaponConfiguration.SendToNetworkGroupOnly);
                        TriggerMuzzleEffect(controller, weaponSystem);
                        UpdateBarrel(ref weaponSystem.CurrentBarrel, barrelCount);
                        break;

                    case ProjectileType.DroppedItem:
                        if (!IsAmmoAvailable(controller, driver, weaponConfiguration, ammoPerShot, muzzlePos))
                        {
                            return false;
                        }

                        FireDroppedItemProjectile(driver, muzzlePos, modifiedAimDir, weaponSystem, controller.Transform.forward, weaponConfiguration.AmmoSkinId, false, weaponConfiguration.SendToNetworkGroupOnly);
                        TriggerMuzzleEffect(controller, weaponSystem);
                        UpdateBarrel(ref weaponSystem.CurrentBarrel, barrelCount);
                        break;

                    case ProjectileType.Flare:
                        if (!IsAmmoAvailable(controller, driver, weaponConfiguration, ammoPerShot, muzzlePos))
                        {
                            return false;
                        }

                        TriggerMuzzleEffect(controller, weaponSystem);
                        LaunchFlares(controller.Transform);
                        UpdateBarrel(ref weaponSystem.CurrentBarrel, barrelCount);

                        break;

                    case ProjectileType.Entity:
                        if (!IsAmmoAvailable(controller, driver, weaponConfiguration, ammoPerShot, muzzlePos))
                        {
                            return false;
                        }

                        FireEntity(driver, muzzlePos, modifiedAimDir, weaponSystem, weaponConfiguration.SendToNetworkGroupOnly);
                        TriggerMuzzleEffect(controller, weaponSystem);
                        UpdateBarrel(ref weaponSystem.CurrentBarrel, barrelCount);
                        break;

                    case ProjectileType.SpecialGunsSync:
                        ammoPerShot = barrelCount * ammoPerShot;
                        if (!IsAmmoAvailable(controller, driver, weaponConfiguration, ammoPerShot, driver.transform.position))
                        {
                            return false;
                        }

                        for (int i = 0; i < controller.SpecialGuns.Count; i++)
                        {
                            TriggerMuzzleEffect(controller, weaponSystem);
                            ShootBullet(driver, controller.BaseEntity, muzzlePos, AimConeUtil.GetAimConeQuat(weaponConfiguration.AimCone) * aimMod * weaponConfiguration.AmmoSpeed, weaponSystem.DamageTypes, weaponConfiguration.AmmoPrefab, weaponConfiguration.SendToNetworkGroupOnly, weaponConfiguration.ImpactEffect);
                            UpdateRecoil(controller, weaponSystem, muzzlePos, controller.Transform.rotation);
                            UpdateBarrel(ref weaponSystem.CurrentBarrel, barrelCount);
                            muzzlePos = GetMuzzlePosition(controller, weaponConfiguration.BarrelConfig, weaponSystem.CurrentBarrel);
                        }
                        break;

                    case ProjectileType.Effect:
                        TriggerMuzzleEffect(controller, weaponSystem);
                        UpdateBarrel(ref weaponSystem.CurrentBarrel, barrelCount);
                        return true;

                    case ProjectileType.Water:
                        if (!IsAmmoAvailable(controller, driver, weaponConfiguration, ammoPerShot, muzzlePos))
                        {
                            return false;
                        }

                        ShootWater(driver, controller.BaseEntity, muzzlePos, modifiedAimDir, weaponConfiguration, weaponSystem.DamageTypes);
                        TriggerMuzzleEffect(controller, weaponSystem);
                        UpdateRecoil(controller, weaponSystem, muzzlePos, controller.Transform.rotation);
                        UpdateBarrel(ref weaponSystem.CurrentBarrel, barrelCount);
                        break;

                    case ProjectileType.NukeServerProjectile:
                        if (!IsAmmoAvailable(controller, driver, weaponConfiguration, ammoPerShot, muzzlePos))
                        {
                            return false;
                        }

                        FireNukeExplosive(driver, controller.BaseEntity, muzzlePos, modifiedAimDir, weaponConfiguration, weaponSystem.DamageTypes, weaponConfiguration.SendToNetworkGroupOnly);
                        TriggerMuzzleEffect(controller, weaponSystem);
                        UpdateRecoil(controller, weaponSystem, muzzlePos, controller.Transform.rotation);
                        UpdateBarrel(ref weaponSystem.CurrentBarrel, barrelCount);
                        break;

                    default:
                        break;
                }

                UseAmmo(controller, driver, weaponConfiguration, ammoPerShot);
                weaponSystem.OnWeaponFired?.Invoke(driver);
                return true;
            }

            static bool CanFireWeapon(IWeaponController controller, BasePlayer driver, WeaponSystem weaponSystem)
            {
                switch (weaponSystem.Config.ProjectileType)
                {
                    case ProjectileType.TorpedoProjectile:
                        WaterLevel.WaterInfo waterInfo = WaterLevel.GetWaterInfo(controller.Transform.position, waves: true, volumes: true, controller.BaseEntity);
                        if (!waterInfo.isValid)
                        {
                            return false;
                        }

                        return waterInfo.surfaceLevel - controller.Transform.position.y > 0;

                    default:
                        return true;
                }
            }

            public static void UpdateRecoil(IWeaponController controller, WeaponSystem weaponSystem, Vector3 muzzlePos, Quaternion muzzleRot)
            {
                if (weaponSystem.Config.RecoilModifier <= 0 || !weaponSystem.HasRigidBody)
                {
                    return;
                }

                Vector3 muzzlePitch = (muzzleRot * Vector3.back) + (controller.Transform.up) + (controller.Transform.forward);
                weaponSystem.RigidBody.AddForceAtPosition(muzzlePitch.normalized * weaponSystem.Config.RecoilModifier, muzzlePos, ForceMode.Impulse);
            }

            public static void ShootBullet(BasePlayer driver, BaseEntity entity, Vector3 muzzlePos, Vector3 velocity, List<DamageTypeEntry> damageTypes, string ammoPrefab, bool sendToNetworkGroupOnly, string impactEffect)
            {
                Instance.BulletProjectile.CallHook(SHOOT_PROJECTILE_HOOK, driver, entity, muzzlePos, velocity, damageTypes, sendToNetworkGroupOnly, ammoPrefab, impactEffect);
            }

            public static void ShootWater(BasePlayer driver, BaseEntity entity, Vector3 muzzlePos, Vector3 modifiedAimDir, WeaponConfiguration weaponConfiguration, List<DamageTypeEntry> damageTypes)
            {
                var newGo = new GameObject();
                newGo.layer = (int)Layer.Physics_Projectile;
                newGo.transform.position = muzzlePos;
                var rigidbody = newGo.AddComponent<Rigidbody>();
                rigidbody.isKinematic = false;
                rigidbody.useGravity = true;
                rigidbody.detectCollisions = true;
                rigidbody.interpolation = RigidbodyInterpolation.None;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rigidbody.constraints = RigidbodyConstraints.None;

                var serverProjectile = newGo.AddComponent<WaterServerProjectile>();

                serverProjectile.ExplosionEffect = weaponConfiguration.ExplosionEffect;
                serverProjectile.Attacker = driver;
                serverProjectile.AttackingPrefab = entity;
                serverProjectile.DamageTypes = damageTypes;
                serverProjectile.SplashRadius = weaponConfiguration.ExplosionRadius;
                serverProjectile.AmmoAmount = weaponConfiguration.AmmoPerShot;
                serverProjectile.AmmoPrefab = weaponConfiguration.AmmoShortName;
                serverProjectile.RigidBody = rigidbody;

                serverProjectile.gravityModifier = 2.5f;
                serverProjectile.speed = weaponConfiguration.AmmoSpeed;
                serverProjectile.InitializeVelocity(modifiedAimDir * weaponConfiguration.AmmoSpeed);
            }

            public static void UpdateSeeker(BaseEntity justLaunched, BaseEntity sourceEntity, WeaponSystem weaponSystem, bool goStraightWitNoTarget, Vector3 aimDir, SeekerStrength minStrength = SeekerStrength.LOW, SeekerStrength maxStrength = SeekerStrength.HIGH)
            {
                var serverProjectile = justLaunched.GetComponent<ServerProjectile>();
                var csp = justLaunched.gameObject.AddComponent<CustomSeekingServerProjectile>();

                if (!object.ReferenceEquals(serverProjectile, null))
                {
                    UnityEngine.Object.DestroyImmediate(serverProjectile);
                }

                csp.courseAdjustRate = 0.7f;
                csp.maxTrackDistance = 500;
                csp.minLockDot = 0.3f;
                csp.flareLockDot = 0.6f;
                csp.autoSeek = false;
                csp.swimAfter = 6;
                csp.launchingDuration = 0.15f;
                csp.armingDuration = 0.5f;
                csp.velocityRampUpTime = 6f;
                csp.armingFinalDir = new Vector3(0.00f, 1.00f, 0.00f);
                csp.armingVelocity = 10;
                csp.orphanedVectorChangeRate = 30;
                csp.velocityCurve = new AnimationCurve();
                csp.velocityCurve.postWrapMode = WrapMode.ClampForever;
                csp.velocityCurve.preWrapMode = WrapMode.ClampForever;
                csp.shouldMoveProjectile = true;
                csp.ignoreEntity = sourceEntity;

                csp.velocityCurve.AddKey(new Keyframe()
                {
                    outTangent = 0,
                    value = 0,
                    weightedMode = WeightedMode.None,
                    inTangent = 0,
                    inWeight = 0,
                    outWeight = 0,
                });

                csp.velocityCurve.AddKey(new Keyframe()
                {
                    outTangent = 0,
                    value = 1,
                    weightedMode = WeightedMode.None,
                    inTangent = 0,
                    inWeight = 0,
                    outWeight = 0,
                    time = 0.5031738f
                });

                csp.orphanedProjectile = false;

                csp.gravityModifier = 0.5f;
                csp.speed = weaponSystem.Config.AmmoSpeed;
                var armingVel = weaponSystem.Config.AmmoSpeed * 3;
                if (weaponSystem.HasRigidBody & weaponSystem.RigidBody.linearVelocity.magnitude > armingVel)
                {
                    armingVel = weaponSystem.RigidBody.linearVelocity.magnitude + weaponSystem.Config.AmmoSpeed;
                }

                csp.InitializeVelocity(aimDir * armingVel);

                csp.armingVelocity = armingVel;
                csp.armingFinalDir = aimDir;
                csp.autoSeek = true;
                csp.MinStrength = minStrength;
                csp.MaxStrength = maxStrength;

                if (goStraightWitNoTarget)
                {
                    csp.swimScale = Vector3.zero;
                }

                justLaunched.prefabID = StringPool.Get(weaponSystem.Config.AmmoPrefab);

                if (!weaponSystem.Config.SendToNetworkGroupOnly)
                {
                    justLaunched.EnableGlobalBroadcast(true);
                }

                justLaunched.Spawn();
            }

            private static void FireDroppedItemProjectile(BasePlayer driver, Vector3 muzzlePos, Vector3 modifiedAimDir, WeaponSystem weaponSystem, Vector3 forward, ulong skinId, bool isBomb, bool sendToNetworkGroupOnly)
            {
                var rotation = Quaternion.LookRotation(forward);
                var weaponConfiguration = weaponSystem.Config;

                DroppedItem worldModel = ItemManager.CreateByName(weaponConfiguration.AmmoPrefab, skin: skinId)
                    .Drop(muzzlePos, modifiedAimDir * weaponConfiguration.AmmoSpeed, rotation)
                    .GetComponent<DroppedItem>();

                worldModel.EnableGlobalBroadcast(true);

                if (isBomb)
                {
                    worldModel.EnableSaving(false);
                    worldModel.allowPickup = false;

                    worldModel.CancelInvoke(worldModel.IdleDestroy);
                    worldModel.CancelInvoke(worldModel.SleepCheck);

                    var bomb = worldModel.gameObject.AddComponent<VehicleBomb>();
                    bomb.Initialize(driver, weaponSystem.DamageTypes, weaponConfiguration.ExplosionRadius, weaponConfiguration.ExplosionEffect, weaponConfiguration.DetonationTimerMax, weaponConfiguration.DetonationTimerMin);
                }
            }

            private static void FireEntityBomb(BasePlayer driver, Vector3 muzzlePos, WeaponSystem weaponSystem, Vector3 forward, bool sendToNetworkGroupOnly)
            {
                var rotation = Quaternion.LookRotation(forward);
                var weaponConfiguration = weaponSystem.Config;

                var bombEnt = GameManager.server.CreatePrefab(weaponConfiguration.AmmoPrefab, muzzlePos, rotation);
                var baseEnt = bombEnt.GetComponent<BaseEntity>();


                if (sendToNetworkGroupOnly)
                {
                    baseEnt.net = Network.Net.sv.CreateNetworkable();
                    baseEnt.net.SwitchGroup(driver.net.group);
                }
                else
                {
                    baseEnt.EnableGlobalBroadcast(true);
                }

                baseEnt.Spawn();

                var bomb = bombEnt.gameObject.AddComponent<VehicleBomb>();
                bomb.Initialize(driver, weaponSystem.DamageTypes, weaponConfiguration.ExplosionRadius, weaponConfiguration.ExplosionEffect, weaponConfiguration.DetonationTimerMax, weaponConfiguration.DetonationTimerMin);
            }

            public static BaseEntity FireNukeExplosive(BasePlayer driver, BaseEntity sourceEntity, Vector3 muzzlePos, Vector3 aimDir, WeaponConfiguration weaponConfiguration, List<DamageTypeEntry> damageTypes, bool sendToNetworkGroupOnly, bool spawn = true)
            {
                var bulletEnt = GameManager.server.CreatePrefab(weaponConfiguration.AmmoPrefab, muzzlePos, new Quaternion());
                var timedExplosive = bulletEnt.GetComponent<TimedExplosive>();
                var replacementEntity = bulletEnt.AddComponent<NukeTimedExplosive>();

                GameObject.DestroyImmediate(timedExplosive);
                replacementEntity.ExplosionSteps = weaponConfiguration.ExplosionSteps;
                replacementEntity.ReplaceTreeWithDeadVariantAtStep = weaponConfiguration.ReplaceTreeWithDeadVariantAtStep;

                PropUtilities.CopySerializableFields<TimedExplosive, NukeTimedExplosive>(timedExplosive, replacementEntity);

                replacementEntity.explosionMatchesNormal = false;

                return FireWithServerProjectile(driver, sourceEntity, bulletEnt, muzzlePos, aimDir, weaponConfiguration, damageTypes, sendToNetworkGroupOnly, spawn);
            }

            public static BaseEntity FireWithServerProjectile(BasePlayer driver, BaseEntity sourceEntity, Vector3 muzzlePos, Vector3 aimDir, WeaponConfiguration weaponConfiguration, List<DamageTypeEntry> damageTypes, bool sendToNetworkGroupOnly, bool spawn = true)
            {
                var bulletEnt = GameManager.server.CreatePrefab(weaponConfiguration.AmmoPrefab, muzzlePos, new Quaternion());
                return FireWithServerProjectile(driver, sourceEntity, bulletEnt, muzzlePos, aimDir, weaponConfiguration, damageTypes, sendToNetworkGroupOnly, spawn);
            }

            public static BaseEntity FireWithServerProjectile(BasePlayer driver, BaseEntity sourceEntity, GameObject bulletEnt, Vector3 muzzlePos, Vector3 aimDir, WeaponConfiguration weaponConfiguration, List<DamageTypeEntry> damageTypes, bool sendToNetworkGroupOnly, bool spawn = true)
            {
                ServerProjectile serverProjectile = bulletEnt.GetComponent<ServerProjectile>();
                TimedExplosive timedExplosive = bulletEnt.GetComponent<TimedExplosive>();
                var baseEnt = bulletEnt.GetComponent<BaseEntity>();
                baseEnt.creatorEntity = driver;

                var hasDriver = !object.ReferenceEquals(driver, null);

                if (!object.ReferenceEquals(timedExplosive, null))
                {
                    if (hasDriver)
                    {
                        timedExplosive.OwnerID = driver.userID.Get();
                    }

                    if (weaponConfiguration.DamageTypes != null)
                    {
                        if (weaponConfiguration.DamageTypes.Count > 0)
                        {
                            timedExplosive.damageTypes = damageTypes;
                        }
                    }

                    if (weaponConfiguration.MinExplosionRadius > 0)
                    {
                        timedExplosive.minExplosionRadius = weaponConfiguration.MinExplosionRadius;
                    }

                    if (weaponConfiguration.ExplosionRadius > 0)
                    {
                        timedExplosive.explosionRadius = weaponConfiguration.ExplosionRadius;
                    }

                    if (weaponConfiguration.DetonationTimerMin > 0)
                    {
                        timedExplosive.timerAmountMin = weaponConfiguration.DetonationTimerMin;
                    }

                    if (weaponConfiguration.DetonationTimerMax > 0)
                    {
                        timedExplosive.timerAmountMax = weaponConfiguration.DetonationTimerMax;
                    }

                    if (!string.IsNullOrEmpty(weaponConfiguration.ExplosionEffect))
                    {
                        timedExplosive.explosionEffect.guid = GameManifest.pathToGuid[weaponConfiguration.ExplosionEffect];
                    }
                }

                baseEnt.networkEntityScale = true;
                baseEnt.transform.localScale = weaponConfiguration.AmmoScale;

                if (spawn)
                {
                    if (!object.ReferenceEquals(serverProjectile, null))
                    {
                        if (weaponConfiguration.ProjectileType != ProjectileType.TorpedoProjectile)
                        {
                            serverProjectile.gravityModifier = 0.5f;
                        }

                        serverProjectile.speed = weaponConfiguration.AmmoSpeed;
                        serverProjectile.ignoreEntity = sourceEntity;
                        serverProjectile.InitializeVelocity(aimDir * weaponConfiguration.AmmoSpeed);
                    }

                    if (sendToNetworkGroupOnly && hasDriver)
                    {
                        baseEnt.net = Network.Net.sv.CreateNetworkable();
                        baseEnt.net.SwitchGroup(driver.net.group);
                    }

                    baseEnt.Spawn();
                }

                return baseEnt;
            }

            public static void FireGuidedRocket(BasePlayer player, BaseEntity sourceEntity, Vector3 muzzlePos, Vector3 aimDir, WeaponSystem weaponSystem, bool sendToNetworkGroupOnly)
            {
                var sp = FireWithServerProjectile(player, sourceEntity, muzzlePos, aimDir, weaponSystem.Config, weaponSystem.DamageTypes, sendToNetworkGroupOnly);
                var gr = sp.gameObject.AddComponent<GuidedRocket>();
            }

            private static void FireEntity(BasePlayer driver, Vector3 muzzlePos, Vector3 aimDir, WeaponSystem weaponSystem, bool sendToNetworkGroupOnly)
            {
                var weaponConfiguration = weaponSystem.Config;

                GameObject createdEnt = GameManager.server.CreatePrefab(weaponConfiguration.AmmoPrefab, muzzlePos, new Quaternion(), false);

                var baseEnt = createdEnt.GetComponent<BaseEntity>();
                baseEnt.creatorEntity = driver;

                ServerProjectile serverProjectile = createdEnt.GetComponent<ServerProjectile>();
                TimedExplosive timedExplosive = createdEnt.GetComponent<TimedExplosive>();

                if (!object.ReferenceEquals(timedExplosive, null))
                {
                    timedExplosive.OwnerID = driver.userID.Get();

                    if (weaponSystem.DamageTypes != null)
                    {
                        if (weaponSystem.DamageTypes.Count > 0)
                        {
                            timedExplosive.damageTypes = weaponSystem.DamageTypes;
                        }

                        if (weaponConfiguration.ExplosionRadius > 0)
                        {
                            timedExplosive.explosionRadius = weaponConfiguration.ExplosionRadius;
                        }

                        if (!string.IsNullOrEmpty(weaponConfiguration.ExplosionEffect))
                        {
                            timedExplosive.explosionEffect.guid = GameManifest.pathToGuid[weaponConfiguration.ExplosionEffect];
                        }
                    }
                }

                if (!object.ReferenceEquals(serverProjectile, null))
                {
                    serverProjectile.gravityModifier = 0.5f;
                    if (weaponConfiguration.AmmoSpeed > 0)
                    {
                        serverProjectile.speed = weaponConfiguration.AmmoSpeed;
                    }

                    serverProjectile.InitializeVelocity(aimDir * serverProjectile.speed);
                }
                else
                {
                    baseEnt.transform.localEulerAngles = aimDir;
                }

                if (sendToNetworkGroupOnly)
                {
                    baseEnt.net = Network.Net.sv.CreateNetworkable();
                    baseEnt.net.SwitchGroup(driver.net.group);
                }

                baseEnt.Spawn();
                createdEnt.SetActive(true);
            }

            private static void UpdateBarrel(ref int nextBarrel, int barrelCount = 2)
            {
                if (++nextBarrel > barrelCount - 1)
                    nextBarrel = 0;
            }

            private static void TriggerMuzzleEffect(IWeaponController controller, WeaponSystem weaponSystem)
            {
                if (weaponSystem.Config.BarrelConfig != BarrelConfiguration.Sprinkler && string.IsNullOrEmpty(weaponSystem.Config.MuzzleEffect))
                {
                    return;
                }

                Vector3 posLocal;
                switch (weaponSystem.Config.BarrelConfig)
                {
                    case BarrelConfiguration.Sprinkler:
                    case BarrelConfiguration.SpecialGuns:
                    case BarrelConfiguration.SpecialGunsM249:
                        var specialGun = controller.SpecialGuns[weaponSystem.CurrentBarrel];

                        specialGun.ToggleOn();
                        controller.InvokeStopSpecialGuns();

                        EffectRun(weaponSystem.Config.MuzzleEffect, specialGun, 0, specialGun.Forward, specialGun.CachedTransform.up, weaponSystem.Config.MuzzleNormLocal, null, weaponSystem.Config.SendToNetworkGroupOnly);

                        return;

                    case BarrelConfiguration.DualFront:
                        posLocal = (weaponSystem.CurrentBarrel % 2 == 0 ? Vector3.right : Vector3.left) * 0.18f + (Vector3.forward * 2.3f) + (Vector3.up * 0.9f);

                        break;

                    case BarrelConfiguration.DualSide:
                        posLocal = (weaponSystem.CurrentBarrel % 2 == 0 ? Vector3.right : Vector3.left) * 1.88f + (Vector3.forward * 1.3f) + (Vector3.up * 0.5f);
                        break;

                    case BarrelConfiguration.Custom:
                        var customBarrelConfig = weaponSystem.Config.CustomBarrelConfigs.ElementAtOrDefault(weaponSystem.CurrentBarrel);
                        if (!customBarrelConfig.Enabled)
                        {
                            if (weaponSystem.CurrentBarrel == 0)
                            {
                                throw new ArgumentNullException("Invalid Custom Barrel Config");
                            }
                            else
                            {
                                weaponSystem.CurrentBarrel = 0;
                                customBarrelConfig = weaponSystem.Config.CustomBarrelConfigs[0];
                            }
                        }

                        posLocal = (Vector3.right * customBarrelConfig.MuzzleFxPos.x) + (Vector3.forward * customBarrelConfig.MuzzleFxPos.z) + (Vector3.up * customBarrelConfig.MuzzleFxPos.y);
                        if (!string.IsNullOrEmpty(customBarrelConfig.MuzzleFx))
                        {
                            EffectRun(customBarrelConfig.MuzzleFx, controller.WeaponControllerEntity, 0, posLocal, customBarrelConfig.MuzzleNormLocal, Vector3.zero, null, weaponSystem.Config.SendToNetworkGroupOnly);
                        }

                        break;

                    case BarrelConfiguration.DualBottom:
                        posLocal = Vector3.zero + (Vector3.down * 3) + (controller.Transform.right * 0.75f * (weaponSystem.CurrentBarrel % 2 == 0 ? 1 : -1));
                        break;

                    case BarrelConfiguration.Bottom:
                        posLocal = Vector3.zero + (Vector3.down * 3);
                        break;

                    default:
                        return;
                }

                if (weaponSystem.Config.MuzzleEffectCount > 0)
                {
                    EffectRun(weaponSystem.Config.MuzzleEffect, controller.WeaponControllerEntity, 0, posLocal, weaponSystem.Config.MuzzleNormLocal, Vector3.zero, null, weaponSystem.Config.SendToNetworkGroupOnly);

                    if (weaponSystem.Config.MuzzleEffectCount > 1)
                    {
                        Instance.timer.Repeat(weaponSystem.Config.MuzzleEffectRepeatDelay, weaponSystem.Config.MuzzleEffectCount - 1, () =>
                        {
                            EffectRun(weaponSystem.Config.MuzzleEffect, controller.WeaponControllerEntity, 0, posLocal, weaponSystem.Config.MuzzleNormLocal, Vector3.zero, null, weaponSystem.Config.SendToNetworkGroupOnly);
                        });
                    }
                }
            }

            public static void EffectRun(string strName, BaseEntity ent, uint boneID, Vector3 posLocal, Vector3 normLocal, Vector3 up, Connection sourceConnection = null, bool sendToNetworkGroupOnly = false)
            {
                if (!string.IsNullOrEmpty(strName))
                {
                    Effect.reusableInstance.Init(Effect.Type.Generic, ent, boneID, posLocal, normLocal, sourceConnection);
                    Effect.reusableInstance.upDir = up;
                    Effect.reusableInstance.pooledString = strName;

                    if (sendToNetworkGroupOnly)
                    {
                        Effect.reusableInstance.targets = ent.net.group.subscribers;
                    }

                    EffectNetwork.Send(Effect.reusableInstance);
                }
            }

            private static Vector3 GetMuzzlePosition(IWeaponController controller, BarrelConfiguration barrelConfig, int currentBarrel, List<CustomBarrelConfiguration> customConfig = null)
            {
                Vector3 muzzlePos = Vector3.zero;
                var vehicleTransform = controller.Transform;
                switch (barrelConfig)
                {
                    case BarrelConfiguration.SpecialGuns:
                        var specialGun = controller.SpecialGuns[currentBarrel];
                        muzzlePos = specialGun.CachedTransform.position + specialGun.CachedTransform.forward * 1;
                        break;

                    case BarrelConfiguration.SpecialGunsM249:
                        var specialGunM249 = controller.SpecialGuns[currentBarrel];
                        muzzlePos = specialGunM249.CachedTransform.position + vehicleTransform.forward * 1;
                        break;

                    case BarrelConfiguration.Bottom:
                        muzzlePos = vehicleTransform.position + (Vector3.down * 3);
                        break;

                    case BarrelConfiguration.DualBottom:
                        muzzlePos = vehicleTransform.position + (Vector3.down * 3) + (vehicleTransform.right * 0.75f * (currentBarrel % 2 == 0 ? 1 : -1));
                        break;

                    case BarrelConfiguration.DualFront:
                        muzzlePos = vehicleTransform.position + ((Vector3.up * 0.8f) + (vehicleTransform.forward * 2f) + (vehicleTransform.right * 0.25f * (currentBarrel % 2 == 0 ? 1 : -1)));
                        break;

                    case BarrelConfiguration.DualSide:
                        muzzlePos = vehicleTransform.position + ((Vector3.up * 0.4f) + (vehicleTransform.forward * 1.8f) + (vehicleTransform.right * 1.15f * (currentBarrel % 2 == 0 ? 1 : -1)));
                        break;

                    case BarrelConfiguration.Sprinkler:
                        muzzlePos = GetCustomMuzzlePosition(vehicleTransform, 0, customConfig);
                        break;

                    case BarrelConfiguration.Custom:
                        muzzlePos = GetCustomMuzzlePosition(vehicleTransform, currentBarrel, customConfig);
                        break;

                    default:
                        break;
                }

                return muzzlePos;
            }

            public static Vector3 GetCustomMuzzlePosition(Transform transform, int currentBarrel, List<CustomBarrelConfiguration> customConfig)
            {
                var customBarrelConfig = customConfig.ElementAtOrDefault(currentBarrel);
                if (!customBarrelConfig.Enabled)
                {
                    if (currentBarrel == 0)
                    {
                        throw new ArgumentNullException("Invalid Custom Barrel Config");
                    }
                    else
                    {
                        currentBarrel = 0;
                        customBarrelConfig = customConfig[0];
                    }
                }

                return transform.TransformPoint(customBarrelConfig.MuzzlePos);
            }

            public static void LaunchFlares(Transform transform)
            {
                var leftFlareObj = GameManager.server.CreatePrefab(FLARE_PREFAB, transform.position + (transform.right * -3), transform.rotation);
                var leftFlare = leftFlareObj.GetComponent<HeliPilotFlare>();
                leftFlare.Init(-transform.right * FLARE_LAUNCH_VAL);

                var rightFlareObj = GameManager.server.CreatePrefab(FLARE_PREFAB, transform.position + (transform.right * 3), transform.rotation);
                var rightFlare = rightFlareObj.GetComponent<HeliPilotFlare>();
                rightFlare.Init(transform.right * FLARE_LAUNCH_VAL);
            }

            static int GetAmount(BasePlayer player, WeaponConfiguration weaponConfiguration)
            {
                var itemId = Instance.WeaponsToAmmoTypeItemIdMap[weaponConfiguration.AmmoShortName].itemid;
                if (itemId == 0)
                {
                    return 0;
                }

                int amount = 0;
                if (player.inventory.containerMain != null)
                {
                    amount += GetAmount(player.inventory.containerMain, itemId, weaponConfiguration.AmmoSkinId);
                }

                if (player.inventory.containerBelt != null)
                {
                    amount += GetAmount(player.inventory.containerBelt, itemId, weaponConfiguration.AmmoSkinId);
                }

                if (player.inventory.containerWear != null)
                {
                    amount += GetAmount(player.inventory.containerWear, itemId, weaponConfiguration.AmmoSkinId);
                }

                return amount;
            }

            public static Item FindItemByItemID(BasePlayer player, string shortname, ulong skinId)
            {
                var itemId = Instance.WeaponsToAmmoTypeItemIdMap[shortname].itemid;
                if (itemId == 0)
                {
                    return null;
                }

                if (player.inventory.containerMain != null)
                {
                    Item item = FindItemByItemID(player.inventory.containerMain, itemId, skinId);
                    if (item != null && item.IsValid())
                    {
                        return item;
                    }
                }

                if (player.inventory.containerBelt != null)
                {
                    Item item = FindItemByItemID(player.inventory.containerBelt, itemId, skinId);
                    if (item != null && item.IsValid())
                    {
                        return item;
                    }
                }

                if (player.inventory.containerWear != null)
                {
                    Item item = FindItemByItemID(player.inventory.containerWear, itemId, skinId);
                    if (item != null && item.IsValid())
                    {
                        return item;
                    }
                }

                return null;
            }
            public static Item FindItemByItemID(ItemContainer itemContainer, string shortname, ulong skinId)
            {
                var itemId = Instance.WeaponsToAmmoTypeItemIdMap[shortname].itemid;
                if (itemId == 0)
                {
                    return null;
                }

                return FindItemByItemID(itemContainer, itemId, skinId);
            }

            public static Item FindItemByItemID(ItemContainer itemContainer, int itemid, ulong skinId)
            {
                for (int i = 0; i < itemContainer.itemList.Count; i++)
                {
                    var item = itemContainer.itemList[i];
                    if (item.info.itemid == itemid && item.skin == skinId)
                    {
                        return item;
                    }
                }

                return null;
            }

            public static int GetAmount(ItemContainer itemContainer, WeaponConfiguration weaponConfiguration)
            {
                var itemId = Instance.WeaponsToAmmoTypeItemIdMap[weaponConfiguration.AmmoShortName].itemid;
                if (itemId == 0)
                {
                    return 0;
                }

                return GetAmount(itemContainer, itemId, weaponConfiguration.AmmoSkinId);
            }

            public static int GetAmount(ItemContainer itemContainer, int itemid, ulong skinId)
            {
                int amount = 0;
                for (int i = 0; i < itemContainer.itemList.Count; i++)
                {
                    var item = itemContainer.itemList[i];
                    if (item.info.itemid == itemid && item.skin == skinId)
                    {
                        amount += item.amount;
                    }
                }

                return amount;
            }

            public static void InitializeWeaponSystems(IVehicle vehicle)
            {
                for (int n = 0; n < vehicle.VehicleConfig.WeaponConfigurations.Count; n++)
                {
                    var wc = vehicle.VehicleConfig.WeaponConfigurations[n];
                    if (!wc.Enabled)
                    {
                        continue;
                    }

                    var ws = new WeaponSystem()
                    {
                        Config = wc,
                        MagazineCapacity = wc.MagazineCapacity,
                        HasRigidBody = true,
                        RigidBody = vehicle.BaseVehicle.rigidBody
                    };

                    vehicle.WeaponSystems.Add(ws);

                    for (int i = 0; i < wc.CounterConfigurations.Count; i++)
                    {
                        var cc = wc.CounterConfigurations[i];
                        if (!cc.Enabled)
                        {
                            continue;
                        }

                        var counter = PropUtilities.CreateCustomEntity<VehicleAmmoCounter>(cc.Location, cc.Rotation, cc.Scale, PREFAB_COUNTER, vehicle.BaseVehicle);
                        counter.ConfigureCounter(ws);
                    }
                }
            }

            public static void UseAmmo(IWeaponController weaponController, BasePlayer player, WeaponConfiguration weaponConfiguration, int ammoCount)
            {
                weaponController.LastAmmoUpdate = Time.realtimeSinceStartup;

                if (weaponController.UnlimitedAmmo || ammoCount <= 0)
                {
                    return;
                }

                Item ammo = null;
                switch (weaponConfiguration.AmmoSource)
                {
                    case AmmoSource.Inventory:
                        if (weaponController.HasAmmoContainer)
                        {
                            ammo = FindItemByItemID(weaponController.AmmoContainer.inventory, weaponConfiguration.AmmoShortName, weaponConfiguration.AmmoSkinId);
                        }
                        else
                        {
                            ammo = FindItemByItemID(player, weaponConfiguration.AmmoShortName, weaponConfiguration.AmmoSkinId);
                        }

                        ammo?.UseItem(ammoCount);
                        return;

                    case AmmoSource.Calories:
                        player.metabolism.calories.Add(ammoCount * -1);
                        return;

                    default:
                        throw new NotImplementedException($"UseAmmo: AmmoSource: {weaponConfiguration.AmmoSource} not supported");
                }
            }

            public static bool IsAmmoAvailable(IWeaponController controller, BasePlayer player, WeaponConfiguration weaponConfiguration, int ammoAmount, Vector3 muzzlePos)
            {
                if (GetAmmoCount(controller, player, weaponConfiguration) >= ammoAmount)
                {
                    return true;
                }

                if (controller.NextDryFireEffect < Time.realtimeSinceStartup)
                {
                    if (!string.IsNullOrEmpty(controller.NoAmmoToast))
                    {
                        var ammoName = Instance.WeaponsToAmmoTypeItemIdMap[weaponConfiguration.AmmoShortName].displayName;
                        Instance.ShowToast(player, controller.NoAmmoToast, GameTip.Styles.Red_Normal, AMMO_TOAST_DISPLAYNAME_PARAM, ammoName.translated);
                    }

                    if (!string.IsNullOrEmpty(weaponConfiguration.DryFireEffect))
                    {
                        Effect.server.Run(weaponConfiguration.DryFireEffect, muzzlePos, Vector3.up);
                    }

                    controller.NextDryFireEffect = Time.realtimeSinceStartup + weaponConfiguration.DryFireEffectDelay;
                }

                return false;
            }

            public static int GetAmmoCount(IWeaponController weaponController, BasePlayer player, WeaponConfiguration weaponConfiguration)
            {
                if (weaponController.UnlimitedAmmo)
                {
                    return 999;
                }

                switch (weaponConfiguration.AmmoSource)
                {
                    case AmmoSource.Inventory:
                        if (weaponController.HasAmmoContainer)
                        {
                            return GetAmount(weaponController.AmmoContainer.inventory, weaponConfiguration);
                        }

                        return GetAmount(player, weaponConfiguration);

                    case AmmoSource.Calories:
                        return (int)player.metabolism.calories.value;

                    default:
                        throw new NotImplementedException($"GetAmmoCount: AmmoSource: {weaponConfiguration.AmmoSource} not supported");
                }
            }

            public static IEnumerator DoNukeExplosion(BaseEntity attackingPlayer, BaseEntity weaponPrefab, Vector3 pos, float startRadius, float maxRadius, int radiusSteps, int layers, int replaceTreeWithDeadVariantAtStep = -1)
            {
                float radiusIncrement = (maxRadius - startRadius) / radiusSteps;
                var currRadius = startRadius;
                var replaceTreeWithDeadVariant = replaceTreeWithDeadVariantAtStep >= 0;

                using (TimeWarning.New("CoroutineTimedExplosive.RadiusDamage"))
                {
                    var spawnedTrees = Pool.Get<HashSet<ulong>>();

                    for (int k = 0; k < radiusSteps; k++)
                    {
                        var replaceTreeWithDeadVariantForStep = replaceTreeWithDeadVariant && replaceTreeWithDeadVariantAtStep <= k;
                        List<BaseEntity> visEntities = Pool.Get<List<BaseEntity>>();
                        Vis.Entities(pos, currRadius, visEntities, layers, QueryTriggerInteraction.Ignore);
                        for (int i = 0; i < visEntities.Count; i++)
                        {
                            TryNukeEntity(attackingPlayer, weaponPrefab, visEntities[i], replaceTreeWithDeadVariantForStep, spawnedTrees);
                            if (replaceTreeWithDeadVariantForStep)
                            {
                                if (i % 5 == 0)
                                {
                                    yield return CoroutineEx.waitForFixedUpdate;
                                }
                            }
                            else if (i % 5 == 0)
                            {
                                yield return CoroutineEx.waitForFixedUpdate;
                            }
                        }

                        Pool.FreeUnmanaged(ref visEntities);
                        currRadius += radiusIncrement;
                    }

                    Pool.FreeUnmanaged(ref spawnedTrees);
                }
            }

            public static void TryNukeEntity(BaseEntity attackingPlayer, BaseEntity weaponPrefab, BaseEntity hitEntity, bool replaceTreeWithDeadVariantForStep, HashSet<ulong> spawnedTrees)
            {
                if (hitEntity.IsDestroyed || !hitEntity.isServer || (replaceTreeWithDeadVariantForStep && spawnedTrees.Contains(hitEntity.net.ID.Value)))
                {
                    return;
                }

                var isCombatEntity = false;
                if (hitEntity is BaseCombatEntity bce)
                {
                    isCombatEntity = true;
                    if (bce.IsDead())
                    {
                        return;
                    }
                }

                var hitPos = hitEntity.transform.position;
                var rot = hitEntity.transform.rotation;
                if (hitEntity is BasePlayer bp)
                {
                    HitInfo hitInfo = new HitInfo();
                    hitInfo.Initiator = attackingPlayer;
                    hitInfo.WeaponPrefab = weaponPrefab;
                    hitInfo.damageTypes.Add(DamageType.Explosion, 1000);
                    bp.OnAttacked(hitInfo);
                }
                else
                {
                    hitEntity.Kill(DestroyMode.Gib);
                }

                // Trees aren't combat entities so can skip
                if (replaceTreeWithDeadVariantForStep && !isCombatEntity && hitEntity is TreeEntity && !hitEntity.ShortPrefabName.Contains("dead"))
                {
                    //var newTree;
                    //spawnedTrees.Add();
                    var deadTreePrefab = Instance.DeadTreePrefabs[UnityEngine.Random.Range(0, Instance.DeadTreePrefabs.Count - 1)];
                    var deadTree = GameManager.server.CreateEntity(deadTreePrefab, hitPos, rot);
                    deadTree.Spawn();

                    spawnedTrees.Add(deadTree.net.ID.Value);
                }
            }
        }

        public static class PropUtilities
        {
            public static readonly Flags[] FlagValues = Enum.GetValues(typeof(Flags)).Cast<Flags>().ToArray();

            public static void InitializeVehicle(IVehicle vehicle)
            {
                List<ItemAmount> buildCosts = new List<ItemAmount>();
                foreach (var buildItems in vehicle.VehicleConfig.BuildCosts)
                {
                    ItemDefinition repairCostDef = ItemManager.FindItemDefinition(buildItems.Key);
                    buildCosts.Add(new ItemAmount(repairCostDef, buildItems.Value));
                }

                var itemDef = vehicle.BaseVehicle.GetOrAddComponent<ItemDefinition>();
                var itemBp = vehicle.BaseVehicle.GetOrAddComponent<ItemBlueprint>();

                itemBp.ingredients = buildCosts;
                vehicle.BaseVehicle.repair.itemTarget = itemDef;
                vehicle.BaseVehicle.rigidBody.SetActive(true);
                if (vehicle.BaseEntity.triggers == null)
                {
                    vehicle.BaseEntity.triggers = new List<TriggerBase>();
                }

                vehicle.BaseEntity.networkRange = vehicle.VehicleConfig.NetworkRange;

                bool autoSyncTransforms = Physics.autoSyncTransforms;

                try
                {
                    Physics.autoSyncTransforms = false;

                    RegisterVehicleToasts(vehicle);
                    InitializeBoombox(vehicle.BaseVehicle, vehicle.VehicleConfig.BoomboxSettings);
                    InitializeDismountPoints(vehicle);
                    InitializeGimbals(vehicle.BaseVehicle, vehicle.VehicleConfig.GimbalConfigs);
                    InitializeComputerStations(vehicle);
                    InitializeProps(vehicle.BaseVehicle, vehicle.VehicleConfig.PropConfigs);
                    InitializeRespawns(vehicle.BaseVehicle, vehicle.VehicleConfig.RespawnSettings);
                    InitializeWeakspots(vehicle);
                    WeaponUtilities.InitializeWeaponSystems(vehicle);
                    InitializeAmmoContainer(vehicle.BaseVehicle, vehicle.VehicleConfig.AmmoContainer);
                    InitializeStorageContainers(vehicle.BaseVehicle, vehicle.VehicleConfig.StorageContainers);
                    InitializeRecyclers(vehicle.BaseVehicle, vehicle.VehicleConfig.Recyclers);
                    InitializeFuelContainer(vehicle);
                    InitializeCodelock(vehicle.BaseVehicle, vehicle.VehicleConfig.CodeLockConfig);
                    InitializeRadio(vehicle);
                    InitializeColliders(vehicle.BaseVehicle, vehicle.VehicleConfig.CustomBoxColliders, vehicle.VehicleConfig.CustomSphereColliders);
                    //InitializeVendingMachines(vehicle);
                    InitializePowerSwitches(vehicle);
                    InitializeCounters(vehicle);
                    InitializeHoverEngines(vehicle);
                    InitializeBoost(vehicle);
                    InitializePhysicalEngine(vehicle);
                    InitializeFoilageInteractionTriggers(vehicle.VehicleConfig.FoilageInteraction, vehicle, vehicle.BaseVehicle.gameObject.transform);
                    InitializeTowTriggers(vehicle);
                    InitializeWorldCollider(vehicle);

                    InitializePhysicsTriggers(vehicle.VehicleConfig.PhysicsTriggers, vehicle.BaseVehicle, vehicle.BaseVehicle.transform);
                    InitializeSafeZoneTriggers(vehicle.VehicleConfig.SafeZoneTriggers, vehicle.BaseVehicle, vehicle.BaseVehicle.transform);
                    InitializeBuoynancyPoints(vehicle.BaseVehicle, vehicle.VehicleConfig.BuoyancySettings);
                    InitializeHurtTriggers(vehicle, vehicle.VehicleConfig.HurtTrigger, vehicle.BaseVehicle.transform);

                    if (vehicle is ILandingGearVehicle lgv)
                    {
                        InitializeLandingGear(lgv);
                    }

                    InitializeCustomSwitches(vehicle.BaseVehicle, vehicle.VehicleConfig.CustomSwitches);
                    InitializeCustomWheels(vehicle.BaseVehicle, vehicle.VehicleConfig.CustomWheels);
                    InitializeInputJoints(vehicle, vehicle.VehicleConfig.InputJoints);
                    InitializeMagnetLiftable(vehicle);
                    InitializePipes(vehicle.BaseVehicle, vehicle.VehicleConfig.PipeProps);
                    InitializeEject(vehicle, vehicle.VehicleConfig.EjectSettings);
                    InitializeBaseCollider(vehicle.BaseVehicle);
                    InitializeNavMeshObstacles(vehicle.BaseVehicle);
                }
                finally
                {
                    if (autoSyncTransforms)
                    {
                        Physics.SyncTransforms();
                    }

                    Physics.autoSyncTransforms = autoSyncTransforms;
                }
            }

            public static void InitializeBaseCollider(BaseEntity baseEntity)
            {
                var col = baseEntity.gameObject.AddComponent<SphereCollider>();
                col.radius = 0.01f;
            }

            public static void InitializeNavMeshObstacles(BaseEntity baseEntity)
            {
                var newNavNeshObstacle = new GameObject();
                newNavNeshObstacle.transform.transform.SetParent(baseEntity.transform, false);
                var nmo = newNavNeshObstacle.AddComponent<NavMeshObstacle>();
                nmo.center = baseEntity.bounds.center;
                nmo.size = baseEntity.bounds.size;
            }

            public static void InitializePipes(BaseEntity baseEntity, PipePropSettings pipeProps)
            {
                if (pipeProps == null || pipeProps.PipeSettings.Count <= 0)
                {
                    return;
                }

                var pipePropSettings = new List<PipePropSetting>();
                for (int i = 0; i < pipeProps.PipeSettings.Count; i++)
                {
                    var pp = pipeProps.PipeSettings[i];
                    if (!pp.Enabled || pp.LinePoints.Count <= 0)
                    {
                        continue;
                    }

                    pipePropSettings.Add(pp);
                }

                if (pipePropSettings.Count <= 0)
                {
                    return;
                }

                var pipeProp = PropUtilities.CreateCustomEntity<PipeProp>(pipeProps.Location, pipeProps.Rotation, pipeProps.Scale, pipeProps.PrefabPath, baseEntity);
                pipeProp.PipePropSettings = pipePropSettings;
                pipeProp.SendNetworkUpdate();
            }

            private static void InitializeEject(IVehicle vehicle, EjectSettings ejectSettings)
            {
                if (!ejectSettings.Enabled)
                {
                    return;
                }

                if (ejectSettings.ButtonSettings == null)
                {
                    return;
                }

                for (int i = 0; i < ejectSettings.ButtonSettings.Count; i++)
                {
                    var bs = ejectSettings.ButtonSettings[i];
                    var ejectButton = PropUtilities.CreateCustomEntity<SpecialButton>(bs.Location, bs.Rotation, bs.Scale, PREFAB_PRESS_BUTTON, vehicle.BaseEntity);
                    ejectButton.PressAction += (BasePlayer bp) =>
                    {
                        if (!bp.isMounted)
                        {
                            return;
                        }

                        var mountedVehicle = bp.GetMountedVehicle();
                        if (!mountedVehicle.PlayerIsMounted(bp))
                        {
                            return;
                        }

                        EjectUtilities.TryEject(vehicle, bp);
                    };
                }
            }

            private static void InitializeMagnetLiftable(IVehicle vehicle)
            {
                if (vehicle.VehicleConfig.MagnetSettings == null || !vehicle.VehicleConfig.MagnetSettings.Enabled)
                {
                    return;
                }

                var ml = vehicle.BaseEntity.gameObject.AddComponent<MagnetLiftable>();

                List<ItemAmount> shredResources = new List<ItemAmount>();
                foreach (var shredResource in vehicle.VehicleConfig.MagnetSettings.ShredResources)
                {
                    ItemDefinition itemDef = ItemManager.FindItemDefinition(shredResource.Key);
                    shredResources.Add(new ItemAmount(itemDef, shredResource.Value));
                }

                ml.shredResources = shredResources.ToArray();
                ml.shredDirection = vehicle.VehicleConfig.MagnetSettings.ShredDirection;
                ml.requireObjectOff = vehicle.VehicleConfig.MagnetSettings.RequireObjectOff;
                ml.scaleScrapResourcesByHealth = vehicle.VehicleConfig.MagnetSettings.ScaleScrapResourcesByHealth;
            }

            public static void InitializeWeakspots(IVehicle vehicle)
            {
                if (vehicle.VehicleConfig.Weakspots == null)
                {
                    return;
                }

                for (int i = 0; i < vehicle.VehicleConfig.Weakspots.Count; i++)
                {
                    var wksptSettings = vehicle.VehicleConfig.Weakspots[i];

                    var weakspot = new Weakspot()
                    {
                        MaxHealth = wksptSettings.MaxHealth,
                        Health = wksptSettings.MaxHealth,
                        HealthFractionOnDestroyed = wksptSettings.HealthFractionOnDestroyed,
                        EffectLocalPosition = wksptSettings.EffectLocalPosition,
                        HideWeakspotOnDeath = wksptSettings.HideWeakspotOnDeath,
                        LoseControlOnDeath = wksptSettings.LoseControlOnDeath,
                        KaruzaEntity = vehicle.BaseEntity as BaseCombatEntity,
                    };

                    if (wksptSettings.Enabled)
                    {
                        vehicle.Weakspots.Add(weakspot);
                        weakspot.DestroyedParticles = new GameObjectRef();
                        if (!string.IsNullOrEmpty(wksptSettings.DestroyedParticlesFx))
                        {
                            var fxId = GameManifest.pathToGuid[wksptSettings.DestroyedParticlesFx];
                            weakspot.DestroyedParticles.guid = fxId;
                        }
                        else
                        {
                            var fxId = GameManifest.pathToGuid["assets/prefabs/npc/patrol helicopter/effects/component_destroyed.prefab"];
                            weakspot.DestroyedParticles.guid = fxId;
                        }

                        weakspot.DamagedParticles = new GameObjectRef();
                        if (!string.IsNullOrEmpty(wksptSettings.DamagedParticlesFx))
                        {
                            var fxId = GameManifest.pathToGuid[wksptSettings.DamagedParticlesFx];
                            weakspot.DamagedParticles.guid = fxId;
                        }
                        else
                        {
                            var fxId = GameManifest.pathToGuid["assets/prefabs/npc/patrol helicopter/effects/component_damged.prefab"];
                            weakspot.DamagedParticles.guid = fxId;
                        }

                        weakspot.InitializeLowHealthEffects(wksptSettings.LowHealthEffectSettings);
                    }

                    for (int n = 0; n < wksptSettings.SubPropConfigs.Count; n++)
                    {
                        var spc = wksptSettings.SubPropConfigs[n];
                        InitializeWeakspotProps(spc, vehicle.BaseEntity, vehicle, weakspot);
                    }
                }
            }

            private static void InitializeWeakspotProps(PropConfig propConfig, BaseEntity parent, IVehicle vehicle, Weakspot weakspot)
            {
                var tempC = propConfig.SubPropConfigs;
                propConfig.SubPropConfigs = null;
                var prop = PropUtilities.InitializeProp(propConfig, parent, vehicle.BaseEntity);
                weakspot.Entities.Add(prop);

                propConfig.SubPropConfigs = tempC;

                if (propConfig.SubPropConfigs != null && propConfig.SubPropConfigs.Count > 0)
                {
                    for (int k = 0; k < propConfig.SubPropConfigs.Count; k++)
                    {
                        var subConfig = propConfig.SubPropConfigs[k];
                        InitializeWeakspotProps(subConfig, prop, vehicle, weakspot);
                    }
                }
            }

            private static void InitializeWorldCollider(IVehicle vehicle)
            {
                if (!vehicle.VehicleConfig.AllowTerrainTrigger)
                {
                    return;
                }

                var wheelCols = vehicle.BaseVehicle.transform.GetComponentsInChildren<WheelCollider>();
                var cols = vehicle.BaseVehicle.transform.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < cols.Length; i++)
                {
                    var c = cols[i];
                    if (c.isTrigger)
                    {
                        continue;
                    }

                    c.gameObject.layer = (int)Layer.Vehicle_World;
                    var tcp = c.gameObject.AddComponent<TerrainCollisionProxy>();
                    tcp.colliders = wheelCols;
                }
            }

            private static void RegisterVehicleToasts(IVehicle vehicle)
            {
                var ts = vehicle.VehicleConfig.GeneralToastSettings;
                if (ts == null || !ts.Enabled)
                {
                    return;
                }

                Instance.TryRegisterMessage(ts.CentralLockLockedToast);
                Instance.TryRegisterMessage(ts.CentralLockUnlockToast);
                Instance.TryRegisterMessage(ts.PowerSwitchOnToast);
                Instance.TryRegisterMessage(ts.PowerSwitchOffToast);
                Instance.TryRegisterMessage(ts.OnEngineOnToast);
            }

            public static void InitializeHurtTriggers(IVehicle vehicle, HurtTriggerSettings hurtTriggerSettings, Transform parent)
            {
                if (vehicle == null || hurtTriggerSettings == null || !hurtTriggerSettings.Enabled || hurtTriggerSettings.Triggers?.Count <= 0)
                {
                    return;
                }

                foreach (HurtTriggerType item in Enum.GetValues(typeof(HurtTriggerType)))
                {
                    vehicle.HurtTriggers[item] = new Dictionary<HurtTriggerConfig, TriggerHurtNotChild>();
                }

                for (int i = 0; i < hurtTriggerSettings.Triggers.Count; i++)
                {
                    var ht = hurtTriggerSettings.Triggers[i];
                    InitializeHurtTrigger(parent, vehicle, ht);
                }
            }

            private static void InitializeHurtTrigger(Transform parent, IVehicle vehicle, HurtTriggerConfig hurtTrigger)
            {
                var triggerHurtGameObject = new GameObject($"HurtTrigger{hurtTrigger.Location}");
                triggerHurtGameObject.transform.localPosition = hurtTrigger.Location;
                triggerHurtGameObject.transform.SetParent(parent, false);
                triggerHurtGameObject.layer = (int)Layer.Trigger;

                var bc = triggerHurtGameObject.AddComponent<BoxCollider>();
                bc.size = hurtTrigger.Size;
                bc.isTrigger = true;

                var triggerHurtComponent = triggerHurtGameObject.AddComponent<TriggerHurtNotChild>();

                triggerHurtComponent.interestLayers = (int)hurtTrigger.LayerMask;
                triggerHurtComponent.DamagePerSecond = hurtTrigger.DamagePerSecond;
                triggerHurtComponent.DamageTickRate = hurtTrigger.DamageTickRate;
                triggerHurtComponent.DamageDelay = hurtTrigger.DamageDelay;
                triggerHurtComponent.damageType = hurtTrigger.DamageType;
                triggerHurtComponent.ignoreNPC = hurtTrigger.IgnoreNPC;
                triggerHurtComponent.npcMultiplier = hurtTrigger.NpcMultiplier;
                triggerHurtComponent.resourceMultiplier = hurtTrigger.ResourceMultiplier;
                triggerHurtComponent.RequireUpAxis = hurtTrigger.RequireUpAxis;
                triggerHurtComponent.UseSourceEntityDamageMultiplier = hurtTrigger.UseSourceEntityDamageMultiplier;
                triggerHurtComponent.ignoreAllVehicleMounted = hurtTrigger.IgnoreAllVehicleMounted;
                triggerHurtComponent.activationDelay = hurtTrigger.ActivationDelay;
                triggerHurtComponent.triggerHitImpacts = hurtTrigger.TriggerHitImpacts;

                vehicle.HurtTriggers[hurtTrigger.HurtTriggerType].Add(hurtTrigger, triggerHurtComponent);

                triggerHurtGameObject.SetActive(false);
            }

            private static void InitializeHoverEngines(IVehicle vehicle)
            {
                for (int i = 0; i < vehicle.VehicleConfig.HoverEngines.Count; i++)
                {
                    var he = vehicle.VehicleConfig.HoverEngines[i];
                    InitializeHoverEngine(he, vehicle);
                }
            }

            private static void InitializeHoverEngine(HoverEngineConfig config, IVehicle vehicle)
            {
                if (!config.Enabled)
                {
                    return;
                }

                var hoverEngineGameObject = new GameObject($"HoverEngine{config.Location}");
                hoverEngineGameObject.transform.localPosition = config.Location;
                hoverEngineGameObject.transform.SetParent(vehicle.BaseVehicle.gameObject.transform, false);

                var he = hoverEngineGameObject.AddComponent<HoverEngine>();
                he.Initialize(config);
            }

            private static void InitializeDismountPoints(IVehicle vehicle)
            {
                if (vehicle.VehicleConfig.CustomDismountPositions == null || vehicle.VehicleConfig.CustomDismountPositions.Count <= 0)
                {
                    return;
                }

                var customDismountPoints = new List<Transform>();
                for (int i = 0; i < vehicle.VehicleConfig.CustomDismountPositions.Count; i++)
                {
                    var dmp = vehicle.VehicleConfig.CustomDismountPositions[i];
                    GameObject customDismount = new GameObject();
                    customDismount.transform.localPosition = dmp.Location;
                    customDismount.transform.transform.SetParent(vehicle.BaseVehicle.transform, false);
                    customDismountPoints.Add(customDismount.transform);
                }

                var cdmPArray = customDismountPoints.ToArray();
                vehicle.BaseVehicle.dismountPositions = cdmPArray;
            }

            public static void InitializePhysicsTriggers(List<PhysicsTrigger> physicsTriggers, BaseEntity baseEntity, Transform parent)
            {
                if (physicsTriggers == null)
                {
                    return;
                }

                for (int i = 0; i < physicsTriggers.Count; i++)
                {
                    var pt = physicsTriggers[i];
                    InitializePhysicsTrigger(parent, baseEntity, pt);
                }
            }

            private static void InitializePhysicsTrigger(Transform parent, BaseEntity baseEntity, PhysicsTrigger physicsTrigger)
            {
                var physicsTriggerGameObject = new GameObject($"PhysicsTrigger{physicsTrigger.Location}");
                physicsTriggerGameObject.transform.localPosition = physicsTrigger.Location;
                physicsTriggerGameObject.transform.SetParent(parent, false);
                physicsTriggerGameObject.layer = (int)Layer.Trigger;

                var bc = physicsTriggerGameObject.AddComponent<BoxCollider>();
                bc.size = physicsTrigger.Size;
                bc.isTrigger = true;

                var physicsTriggerComponent = physicsTriggerGameObject.AddComponent<TriggerParentDelayedExit>();
                physicsTriggerComponent.interestLayers = LayerMask.GetMask(LayerMask.LayerToName((int)Layer.Player_Server));
                //physicsTriggerComponent.intersectionMode = TriggerParentEnclosed.TriggerMode.TriggerPoint;
                physicsTriggerComponent.parentMountedPlayers = true;
                physicsTriggerComponent.parentSleepers = physicsTrigger.ParentSleepers;
                physicsTriggerComponent.ParentNPCPlayers = physicsTrigger.ParentNPCPlayers;
                physicsTriggerComponent.checkForObjUnderFeet = physicsTrigger.CheckForObjUnderFeet;
                //physicsTriggerComponent.CheckBoundsOnUnparent = false;
                physicsTriggerComponent.doClippingCheck = false;
                physicsTriggerComponent.associatedMountable = (baseEntity as BaseVehicle);
                physicsTriggerComponent.overrideOtherTriggers = true;

                baseEntity.triggers.Add(physicsTriggerComponent);
            }

            public static void InitializeSafeZoneTriggers(List<SafeZoneTrigger> safeZoneTriggers, BaseEntity baseEntity, Transform parent)
            {
                if (safeZoneTriggers == null)
                {
                    return;
                }

                for (int i = 0; i < safeZoneTriggers.Count; i++)
                {
                    var szt = safeZoneTriggers[i];
                    if (!szt.Enabled)
                    {
                        continue;
                    }

                    InitializeSafeZoneTrigger(parent, baseEntity, szt);
                }
            }

            private static void InitializeSafeZoneTrigger(Transform parent, BaseEntity baseEntity, SafeZoneTrigger safeZoneTrigger)
            {
                var safeZoneTriggerGameObject = new GameObject($"SafeZoneTrigger{safeZoneTrigger.Location}");
                safeZoneTriggerGameObject.transform.localPosition = safeZoneTrigger.Location;
                safeZoneTriggerGameObject.transform.SetParent(parent, false);
                safeZoneTriggerGameObject.layer = (int)Layer.Trigger;

                var bc = safeZoneTriggerGameObject.AddComponent<BoxCollider>();
                bc.size = safeZoneTrigger.Size;
                bc.isTrigger = true;

                var safeZoneTriggerComponent = safeZoneTriggerGameObject.AddComponent<TriggerSafeZone>();
                safeZoneTriggerComponent.interestLayers = LayerMask.GetMask(LayerMask.LayerToName((int)Layer.Player_Server));
                safeZoneTriggerComponent.maxDepth = -1;

                baseEntity.triggers.Add(safeZoneTriggerComponent);
            }

            public static void InitializeBuoynancyPoints(BaseEntity baseEntity, BouyancySettings buoyancySettings)
            {
                if (!buoyancySettings.Enabled)
                {
                    return;
                }

                var buoyancy = baseEntity.gameObject.GetOrAddComponent<Buoyancy>();
                var rigidBody = baseEntity.gameObject.GetOrAddComponent<Rigidbody>();

                buoyancy.forEntity = baseEntity;
                buoyancy.forVehicle = baseEntity as BaseVehicle;
                buoyancy.rigidBody = rigidBody;

                buoyancy.useUnderwaterDrag = buoyancySettings.UnderwaterDrag > 0;
                buoyancy.underwaterDrag = buoyancySettings.UnderwaterDrag;
                buoyancy.wavesEffect = buoyancySettings.WavesEffect;
                buoyancy.flowMovementScale = buoyancySettings.FlowMovementScale;
                buoyancy.doEffects = buoyancySettings.DoEffects;
                buoyancy.requiredSubmergedFraction = buoyancySettings.RequiredSubmergedFraction;
                buoyancy.scaleForceWithMass = buoyancySettings.ScaleForceWithMass;

                if (buoyancySettings.BuoyancyPoints.Count <= 0)
                {
                    CalculateDynamicBuoyancy(baseEntity, buoyancy);
                    return;
                }

                var curPoints = buoyancy.points?.ToList() ?? new List<BuoyancyPoint>();
                for (int i = 0; i < buoyancySettings.BuoyancyPoints.Count; i++)
                {
                    var bpc = buoyancySettings.BuoyancyPoints[i];
                    var bp = InitializeBuoynancy(baseEntity, bpc);

                    curPoints.Add(bp);
                }

                buoyancy.points = curPoints.ToArray();
                buoyancy.SavePointData(true);
            }

            public static void CalculateDynamicBuoyancy(BaseEntity baseEntity, Buoyancy buoyancy)
            {
                Vector3 forward = baseEntity.transform.forward;
                Vector3 right = baseEntity.transform.right;
                Vector3 position = baseEntity.transform.position;
                float num = baseEntity.bounds.size.x / 2f;
                float num2 = baseEntity.bounds.size.z / 2f;

                var curPoints = buoyancy.points?.ToList() ?? new List<BuoyancyPoint>();
                curPoints.Add(InitializeBuoynancy(baseEntity, new BouyancyPointConfig()
                {
                    Location = baseEntity.transform.InverseTransformPoint(position + forward * num2),
                    Force = 1f,
                    Size = 0.1f
                }));

                curPoints.Add(InitializeBuoynancy(baseEntity, new BouyancyPointConfig()
                {
                    Location = baseEntity.transform.InverseTransformPoint(position + forward * num2 + right * num),
                    Force = 1f,
                    Size = 0.1f
                }));

                curPoints.Add(InitializeBuoynancy(baseEntity, new BouyancyPointConfig()
                {
                    Location = baseEntity.transform.InverseTransformPoint(position + forward * num2 + -right * num),
                    Force = 1f,
                    Size = 0.1f
                }));

                curPoints.Add(InitializeBuoynancy(baseEntity, new BouyancyPointConfig()
                {
                    Location = baseEntity.transform.InverseTransformPoint(position),
                    Force = 1f
                }));

                curPoints.Add(InitializeBuoynancy(baseEntity, new BouyancyPointConfig()
                {
                    Location = baseEntity.transform.InverseTransformPoint(position + right * num),
                    Force = 1f,
                    Size = 0.1f
                }));

                curPoints.Add(InitializeBuoynancy(baseEntity, new BouyancyPointConfig()
                {
                    Location = baseEntity.transform.InverseTransformPoint(position + -right * num),
                    Force = 1f
                }));

                curPoints.Add(InitializeBuoynancy(baseEntity, new BouyancyPointConfig()
                {
                    Location = baseEntity.transform.InverseTransformPoint(position + -forward * num2),
                    Force = 1f
                }));

                curPoints.Add(InitializeBuoynancy(baseEntity, new BouyancyPointConfig()
                {
                    Location = baseEntity.transform.InverseTransformPoint(position + -forward * num2 + right * num),
                    Force = 1f,
                    Size = 0.1f
                }));

                curPoints.Add(InitializeBuoynancy(baseEntity, new BouyancyPointConfig()
                {
                    Location = baseEntity.transform.InverseTransformPoint(position + -forward * num2 + -right * num),
                    Force = 1f,
                    Size = 0.1f
                }));

                buoyancy.points = curPoints.ToArray();
                buoyancy.SavePointData(forced: true);
            }

            private static void InitializeFoilageInteractionTriggers(FoilageInteractionSettings foilageInteractionSettings, IKaruzaCustomEntity karuzaCustomEntity, Transform parent)
            {
                if (foilageInteractionSettings == null || !foilageInteractionSettings.Enabled)
                {
                    return;
                }

                for (int i = 0; i < foilageInteractionSettings.Triggers.Count; i++)
                {
                    var bkt = foilageInteractionSettings.Triggers[i];
                    InitializeFoilageInteractionTrigger(parent, karuzaCustomEntity, bkt);
                }
            }

            static void InitializeFoilageInteractionTrigger(Transform parent, IKaruzaCustomEntity karuzaCustomEntity, FoilageInteractionConfig config)
            {
                if (!config.CanHarvestFood && !config.CanRemoveFoilage)
                {
                    return;
                }

                var gameObject = new GameObject();
                gameObject.transform.localPosition = config.Location;
                gameObject.transform.SetParent(parent, false);
                gameObject.layer = (int)Layer.Reserved1;

                var bc = gameObject.AddComponent<BoxCollider>();
                bc.size = config.Size;
                bc.isTrigger = true;

                var trigger = gameObject.AddComponent<FoilageInteractionTrigger>();
                trigger.KaruzaEntity = karuzaCustomEntity;
                trigger.Modifier = config.Modifier;

                var layers = new List<string>();
                if (config.CanHarvestFood)
                {
                    layers.Add($"{Layer.Harvestable}");
                }

                if (config.CanRemoveFoilage)
                {
                    layers.Add($"{Layer.Bush}");
                }

                trigger.InterestLayers = LayerMask.GetMask(layers.ToArray());
            }

            public static void InitializePowerSwitches(IVehicle vehicle)
            {
                if (vehicle.VehicleConfig.PowerSettings == null || vehicle.VehicleConfig.PowerSettings.SwitchConfigs == null)
                {
                    return;
                }

                for (int i = 0; i < vehicle.VehicleConfig.PowerSettings.SwitchConfigs.Count; i++)
                {
                    var sc = vehicle.VehicleConfig.PowerSettings.SwitchConfigs[i];
                    InitializePowerSwitch(vehicle, sc);
                }
            }

            public static void InitializePowerSwitch(IKaruzaCustomEntity karuaEntity, PropConfigSettings pc)
            {
                if (!pc.Enabled || string.IsNullOrEmpty(pc.PrefabPath))
                {
                    return;
                }

                BaseEntity prop = null;
                if (pc.PrefabPath == BUTTON_PREFAB || pc.PrefabPath == BUTTON_COMPACT_PREFAB || pc.PrefabPath == BUTTON_TRAIN_STAIRS_PREFAB || pc.PrefabPath == BUTTON_INVIS_PREFAB)
                {
                    prop = PropUtilities.CreateCustomEntity<VehiclePowerButton>(pc.Location, pc.Rotation, pc.Scale, pc.PrefabPath, karuaEntity.BaseEntity);
                }
                else if (pc.PrefabPath == LANTERN_PREFAB)
                {
                    prop = PropUtilities.CreateCustomEntity<LanternPowerSwitch>(pc.Location, pc.Rotation, pc.Scale, pc.PrefabPath, karuaEntity.BaseEntity);
                }
                else
                {
                    prop = PropUtilities.CreateCustomEntity<VehiclePowerSwitch>(pc.Location, pc.Rotation, pc.Scale, pc.PrefabPath, karuaEntity.BaseEntity);
                }
            }

            public static void InitializeCounters(IVehicle vehicle)
            {
                if (vehicle.VehicleConfig.AltitudeCounters != null)
                {
                    for (int i = 0; i < vehicle.VehicleConfig.AltitudeCounters.Count; i++)
                    {
                        var c = vehicle.VehicleConfig.AltitudeCounters[i];
                        if (!c.Enabled)
                        {
                            continue;
                        }

                        PropUtilities.CreateCustomEntity<VehicleAltitudeCounter>(c.Location, c.Rotation, c.Scale, PREFAB_COUNTER, vehicle.BaseVehicle, detectCollisions: c.DetectCollisions);
                    }
                }

                if (vehicle.VehicleConfig.FuelCounters != null)
                {
                    for (int i = 0; i < vehicle.VehicleConfig.FuelCounters.Count; i++)
                    {
                        var c = vehicle.VehicleConfig.FuelCounters[i];
                        if (!c.Enabled)
                        {
                            continue;
                        }

                        PropUtilities.CreateCustomEntity<VehicleFuelCounter>(c.Location, c.Rotation, c.Scale, PREFAB_COUNTER, vehicle.BaseVehicle, detectCollisions: c.DetectCollisions);
                    }
                }

                if (vehicle.VehicleConfig.HealthCounters != null)
                {
                    for (int i = 0; i < vehicle.VehicleConfig.HealthCounters.Count; i++)
                    {
                        var c = vehicle.VehicleConfig.HealthCounters[i];
                        if (!c.Enabled)
                        {
                            continue;
                        }

                        var hc = PropUtilities.CreateCustomEntity<VehicleHealthCounter>(c.Location, c.Rotation, c.Scale, PREFAB_COUNTER, vehicle.BaseVehicle, detectCollisions: c.DetectCollisions);
                        vehicle.OnHealthChange += hc.OnVehicleHealthChanged;
                    }
                }

                if (vehicle.VehicleConfig.SpeedCounters != null)
                {
                    for (int i = 0; i < vehicle.VehicleConfig.SpeedCounters.Count; i++)
                    {
                        var c = vehicle.VehicleConfig.SpeedCounters[i];
                        if (!c.Enabled)
                        {
                            continue;
                        }

                        var spc = PropUtilities.CreateCustomEntity<VehicleSpeedCounter>(c.Location, c.Rotation, c.Scale, PREFAB_COUNTER, vehicle.BaseVehicle, detectCollisions: c.DetectCollisions);
                        spc.ShowKmh = c.ShowKMH;
                    }
                }
            }

            public static void InitializeBoombox(BaseEntity baseEntity, BoomboxSettings boomBoxSettings)
            {
                if (boomBoxSettings == null || !boomBoxSettings.Enabled || string.IsNullOrEmpty(boomBoxSettings.PrefabPath))
                {
                    return;
                }

                if (boomBoxSettings.PrefabPath == BOOMBOX_DEPLOYED_PREFAB)
                {
                    boomBoxSettings.PrefabPath = BOOMBOX_DEPLOYED_STATIC_PREFAB;
                }

                PropUtilities.CreateSpecialBoombox(boomBoxSettings, baseEntity);
            }

            public static void InitializeRespawns(BaseEntity baseEntity, RespawnSettings respawnSettings)
            {
                if (respawnSettings == null || !respawnSettings.Enabled)
                {
                    return;
                }

                for (int i = 0; i < respawnSettings.RespawnPoints.Count; i++)
                {
                    var rp = respawnSettings.RespawnPoints[i];
                    var spc = PropUtilities.CreateCustomBag<SpecialSleepingBag>(rp, respawnSettings, baseEntity, i);

                    var hasLinkedSeat = rp.LinkedMount != null && rp.LinkedMount.Enabled;
                    if (hasLinkedSeat)
                    {
                        var linkedMount = PropUtilities.CreateCustomBaseMountable<SpecialBaseMountable>(rp.LinkedMount.Location, rp.LinkedMount.Rotation, rp.LinkedMount.Scale, rp.LinkedMount.PrefabPath, baseEntity, 0, null, false, true, spawn: false);

                        spc.AssociatedSeat = linkedMount;
                        spc.HasAssociatedSeat = true;

                        linkedMount.Spawn();

                        if (baseEntity is IVehicle vehicle)
                        {
                            PropUtilities.InitializeSeat(linkedMount, false, false, vehicle);
                        }
                    }
                }
            }

            public static void InitializeGimbals(BaseEntity baseEntity, List<GimbalConfig> gimbalConfigs)
            {
                if (gimbalConfigs == null)
                {
                    return;
                }

                for (int i = 0; i < gimbalConfigs.Count; i++)
                {
                    var gc = gimbalConfigs[i];
                    if (!gc.Enabled)
                    {
                        continue;
                    }

                    var prop = PropUtilities.CreateWorldItem(gc.Location, gc.Rotation, PREFAB_WORLD, gc.ItemId, baseEntity, gc.SkinId, false, true);
                    var gimbal = prop.gameObject.AddComponent<Gimbal>();

                    if (gc.SubPropConfigs != null && gc.SubPropConfigs.Count > 0)
                    {
                        for (int j = 0; j < gc.SubPropConfigs.Count; j++)
                        {
                            var subConfig = gc.SubPropConfigs[j];
                            PropUtilities.InitializeProp(subConfig, prop, baseEntity);
                        }
                    }

                    gimbal.InitializeGimbal(gc.WeaponControllerConfig);
                    gimbal.SetChildren();
                }
            }

            public static void InitializeComputerStations(IVehicle vehicle)
            {
                if (vehicle.VehicleConfig.ComputerStationConfigs == null)
                {
                    return;
                }

                for (int i = 0; i < vehicle.VehicleConfig.ComputerStationConfigs.Count; i++)
                {
                    var cs = vehicle.VehicleConfig.ComputerStationConfigs[i];
                    if (!cs.Enabled)
                    {
                        continue;
                    }

                    var prop = PropUtilities.CreateCustomComputerStation(cs.Location, cs.Rotation, cs.Scale, cs.PrefabPath, vehicle.BaseVehicle, cs.SkinId, cs.NetworkPrefabOverride);
                    prop.CanSwapTo = cs.CanSwapTo;
                    prop.ForceDriver = !cs.IsLocalSeat;

                    PropUtilities.InitializeSeat(prop, cs.IsDriver, true, vehicle);

                    SpecialWorldItem worldItem = null;
                    List<SpecialGun> specialGuns = new List<SpecialGun>();
                    List<SpecialCCTV_RC> cameras = new List<SpecialCCTV_RC>();

                    BaseEntity parent = prop;
                    if (cs.ParentToVehicle)
                    {
                        parent = vehicle.BaseVehicle;
                    }

                    if (cs.SubPropConfigs != null && cs.SubPropConfigs.Count > 0)
                    {
                        for (int j = 0; j < cs.SubPropConfigs.Count; j++)
                        {
                            var subConfig = cs.SubPropConfigs[j];
                            var subProp = PropUtilities.InitializeProp(subConfig, parent, vehicle.BaseVehicle);
                            if (subConfig.PartType == PartType.CustomCamera)
                            {
                                var camera = subProp as SpecialCCTV_RC;
                                cameras.Add(camera);
                                var sgs = subProp.GetComponentsInChildren<SpecialGun>();
                                specialGuns.AddRange(sgs);
                            }
                            else if (subConfig.PartType == PartType.GimbalItem)
                            {
                                worldItem = subProp as SpecialWorldItem;
                                var sgs = subProp.GetComponentsInChildren<SpecialGun>();
                                specialGuns.AddRange(sgs);
                            }
                            else if (subConfig.PartType == PartType.SpecialGun || subConfig.PartType == PartType.M249 || subConfig.PartType == PartType.MiniGun)
                            {
                                specialGuns.Add(subProp as SpecialGun);
                            }
                        }
                    }

                    prop.InitializeStation(cs, cameras, worldItem, specialGuns.ToArray());
                    var hasLinkedSeat = cs.LinkedMount != null && cs.LinkedMount.Enabled;
                    if (hasLinkedSeat)
                    {
                        prop.CanSwapTo = false;

                        var linkedMount = PropUtilities.CreateCustomBaseMountable<SpecialComputerStationMount>(cs.LinkedMount.Location, cs.LinkedMount.Rotation, cs.LinkedMount.Scale, cs.LinkedMount.PrefabPath, vehicle.BaseVehicle, 0, null, false, true, spawn: false);

                        prop.LinkedMount = linkedMount;
                        prop.HasLinkedMount = true;

                        linkedMount.SpecialComputerStation = prop;
                        linkedMount.Spawn();

                        PropUtilities.InitializeSeat(linkedMount, cs.IsDriver, false, vehicle);
                    }
                }
            }

            private static void InitializeTowTriggers(IVehicle vehicle)
            {
                var towSettings = vehicle.VehicleConfig.TowSettings;
                if (vehicle.VehicleConfig.TowSettings == null || !towSettings.Enabled)
                {
                    return;
                }

                for (int i = 0; i < towSettings.Triggers.Count; i++)
                {
                    var tt = towSettings.Triggers[i];
                    InitializeTowTrigger(vehicle, tt);
                }

                InitializeDynamicTowing(towSettings.DynamicTowingSettings, vehicle, vehicle.BaseEntity);
            }

            static void InitializeDynamicTowing(DynamicTowingSettings dynamicTowingSettings, IVehicle vehicle, BaseEntity parent)
            {
                if (!dynamicTowingSettings.Enabled)
                {
                    return;
                }

                var anchorGroups = new Dictionary<string, List<TowingAnchor>>();
                for (int i = 0; i < dynamicTowingSettings.Anchors.Count; i++)
                {
                    var anchor = dynamicTowingSettings.Anchors[i];
                    var anchorItem = PropUtilities.CreateTowWorldItem(parent.transform.TransformPoint(anchor.Location), Vector3.zero, PREFAB_WORLD, anchor.ItemId, null, anchor.SkinId, false);

                    anchorItem.KaruzaEntity = vehicle;
                    anchorItem.Vehicle = vehicle;
                    anchorItem.HasKaruzaEntity = true;
                    anchorItem.CachedParent = parent;
                    //anchorItem.UseGlobalNetworkPosition = true;

                    anchorItem.Spawn();
                    vehicle.OnToggled -= anchorItem.OnEngineToggle;
                    vehicle.OnMountedChange -= anchorItem.OnMountedChange;


                    var towAdaptor = PropUtilities.CreateCustomEntity<TowingAnchor>(new Vector3(0, -0.1f, 0), Vector3.zero, anchor.AdaptorScale, STORAGE_ADAPTOR_PREFAB, anchorItem, spawn: false);
                    towAdaptor.Vehicle = vehicle;
                    towAdaptor.Controller = parent;
                    towAdaptor.RequireDriverForAutoConnect = anchor.RequireDriverForAutoConnect;

                    parent.AddChild(anchorItem);

                    towAdaptor.MinRopeLength = anchor.MinRopeLength;
                    towAdaptor.MinRopeLengthWhenConnected = anchor.MinRopeLengthWhenConnected;
                    towAdaptor.MaxRopeLength = anchor.MaxRopeLength;
                    towAdaptor.DisplayRopeWhenDisconnected = anchor.DisplayRopeWhenDisconnected;
                    towAdaptor.DisplayRope = anchor.DisplayRope;
                    towAdaptor.MassScale = anchor.MassScale;
                    towAdaptor.ConnectedMassScale = anchor.ConnectedMassScale;
                    towAdaptor.DisplayReleaseButton = anchor.AddReleaseButtonToAnchor;
                    towAdaptor.RopeColor = (int)anchor.RopeColor;
                    towAdaptor.RopeType = (int)anchor.RopeType;
                    towAdaptor.RopeThickness = anchor.RopeThickness;

                    if (!string.IsNullOrEmpty(anchor.GroupKey))
                    {
                        if (!anchorGroups.ContainsKey(anchor.GroupKey))
                        {
                            anchorGroups[anchor.GroupKey] = new List<TowingAnchor>();
                        }

                        anchorGroups[anchor.GroupKey].Add(towAdaptor);
                    }

                    if (anchor.AnchorWheel.Enabled)
                    {
                        var wheel = PropUtilities.CreateCustomEntity<SpecialWheelSwitch>(anchor.AnchorWheel.Location, anchor.AnchorWheel.Rotation, anchor.AnchorWheel.Scale, WHEEL_SWITCH_PREFAB, parent);
                        wheel.MinRotation = anchor.MinRopeLength;
                        wheel.MaxRotation = anchor.MaxRopeLength;
                        wheel.OnRotation += towAdaptor.UpdateWinch;
                        towAdaptor.ConnectChange += () => wheel.MinRotation = towAdaptor.MinRopeLengthComputed;
                        towAdaptor.WinchUpdated += (float winchLength) => wheel.SetForceRotateProgress(winchLength);
                    }

                    if (anchor.AnchorDisplay.Enabled)
                    {
                        var counter = PropUtilities.CreateCustomEntity<DynamicAnchorCounter>(anchor.AnchorDisplay.Location, anchor.AnchorDisplay.Rotation, anchor.AnchorDisplay.Scale, PREFAB_COUNTER, parent);
                        counter.SetCounter(0);
                        counter.ShowPassthru();
                        towAdaptor.WinchUpdated += (float winchLength) => counter.SetPassthru((int)(winchLength * 100));
                    }

                    if (anchor.ReleaseButtonSettings.Enabled)
                    {
                        var releaseButton = PropUtilities.CreateCustomEntity<SpecialButton>(anchor.ReleaseButtonSettings.Location, anchor.ReleaseButtonSettings.Rotation, anchor.ReleaseButtonSettings.Scale, PREFAB_PRESS_BUTTON, parent);
                        releaseButton.PressAction += towAdaptor.DestroyConnection;
                    }

                    if (anchor.AutoAttachSwitchSettings.Enabled)
                    {
                        var autoAttachSwitch = PropUtilities.CreateCustomEntity<SpecialSwitch>(anchor.AutoAttachSwitchSettings.Location, anchor.AutoAttachSwitchSettings.Rotation, anchor.AutoAttachSwitchSettings.Scale, anchor.AutoAttachSwitchSettings.PrefabPath, parent);
                        autoAttachSwitch.SwitchToggled += towAdaptor.ToggleAutoConnect;
                        towAdaptor.AutoConnectUpdate += (bool isOn) => autoAttachSwitch.SetSwitch(null, isOn);
                    }

                    towAdaptor.Spawn();
                }

                for (int i = 0; i < dynamicTowingSettings.Groups.Count; i++)
                {
                    var group = dynamicTowingSettings.Groups[i];
                    if (!group.Enabled)
                    {
                        continue;
                    }

                    if (!anchorGroups.ContainsKey(group.GroupKey))
                    {
                        continue;
                    }

                    var anchors = anchorGroups[group.GroupKey];
                    if (group.ReleaseButtonSettings.Enabled)
                    {
                        var releaseButton = PropUtilities.CreateCustomEntity<SpecialButton>(group.ReleaseButtonSettings.Location, group.ReleaseButtonSettings.Rotation, group.ReleaseButtonSettings.Scale, PREFAB_PRESS_BUTTON, vehicle.BaseEntity);

                        for (int n = 0; n < anchors.Count; n++)
                        {
                            var anchor = anchors[n];
                            releaseButton.PressAction += anchor.DestroyConnection;
                        }
                    }
                }
            }

            static void InitializeTowTrigger(IVehicle vehicle, TowTriggerConfig config)
            {
                if (!config.Enabled)
                {
                    return;
                }

                var gameObject = new GameObject();
                var trigger = gameObject.AddComponent<TowTrigger>();
                trigger.Initialize(vehicle, config);
            }

            public static void InitializeCustomSwitches(BaseEntity baseEntity, List<CustomSwitchConfig> customSwitches)
            {
                if (customSwitches == null)
                {
                    return;
                }

                for (int i = 0; i < customSwitches.Count; i++)
                {
                    var csw = customSwitches[i];
                    if (!csw.Enabled || string.IsNullOrEmpty(csw.PrefabPath))
                    {
                        continue;
                    }

                    ICustomSwitch customSwitch = null;
                    if (csw.PrefabPath == BUTTON_PREFAB || csw.PrefabPath == BUTTON_COMPACT_PREFAB || csw.PrefabPath == BUTTON_TRAIN_STAIRS_PREFAB || csw.PrefabPath == BUTTON_INVIS_PREFAB)
                    {
                        customSwitch = PropUtilities.CreateCustomEntity<CustomButton>(csw.Location, csw.Rotation, csw.Scale, csw.PrefabPath, baseEntity);
                    }
                    else if (csw.PrefabPath == LANTERN_PREFAB)
                    {
                        customSwitch = PropUtilities.CreateCustomEntity<LanternSwitch>(csw.Location, csw.Rotation, csw.Scale, csw.PrefabPath, baseEntity);
                    }
                    else
                    {
                        customSwitch = PropUtilities.CreateCustomEntity<CustomSwitch>(csw.Location, csw.Rotation, csw.Scale, csw.PrefabPath, baseEntity);
                    }

                    customSwitch.ToggleDoors = csw.ToggleDoors;
                    customSwitch.ToggleJoint = csw.ToggleJoint;
                    customSwitch.ToggleLights = csw.ToggleLights;
                    customSwitch.TogglePropellers = csw.TogglePropellers;
                    customSwitch.ToggleSirens = csw.ToggleSirens;
                    customSwitch.ToggleSails = csw.ToggleSails;
                    customSwitch.ToggleAnchors = csw.ToggleAnchors;
                    customSwitch.IgnoreCodelock = csw.IgnoreCodelock;
                    customSwitch.Config = csw;

                    if (csw.JointConfigs != null)
                    {
                        for (int n = 0; n < csw.JointConfigs.Count; n++)
                        {
                            var jc = csw.JointConfigs[n];
                            InitializeJoint(jc, baseEntity, customSwitch);
                        }
                    }

                    for (int n = 0; n < csw.SubPropConfigs.Count; n++)
                    {
                        var spc = csw.SubPropConfigs[n];
                        var sp = PropUtilities.InitializeProp(spc, baseEntity, baseEntity);

                        if (csw.ToggleLights && sp is ILightToggle light)
                        {
                            light.CanVehicleToggle = false;
                            customSwitch.Lights.Add(light);
                            continue;
                        }

                        if (csw.ToggleDoors && sp is SpecialDoor door)
                        {
                            customSwitch.Doors.Add(door);
                            continue;
                        }

                        if (csw.TogglePropellers && spc.PartType == PartType.Propeller)
                        {
                            var propeller = sp.GetComponent<VehiclePropeller>();
                            if (propeller != null)
                            {
                                propeller.DeregisterActions();
                                customSwitch.Propellers.Add(propeller);
                            }

                            continue;
                        }

                        if (csw.ToggleSails && spc.PartType == PartType.Boat && sp is SpecialSail ss)
                        {
                            customSwitch.Sails.Add(ss);
                            ss.IsOnSwitch = true;
                            continue;
                        }

                        if (csw.ToggleAnchors && spc.PartType == PartType.Boat && sp is SpecialAnchor sa)
                        {
                            customSwitch.Anchors.Add(sa);
                            continue;
                        }
                    }

                    for (int n = 0; n < csw.SirenConfigs.Count; n++)
                    {
                        var sc = csw.SirenConfigs[n];
                        var siren = PropUtilities.CreateCustomEntity<SpecialSiren>(sc.Location, sc.Rotation, sc.Scale, PREFAB_TRUMPET, baseEntity);
                        siren.Tones = sc.Tones;

                        if (csw.ToggleSirens)
                        {
                            customSwitch.Sirens.Add(siren);
                        }
                    }
                }
            }

            static JointController InitializeJoint(JointConfig jointConfig, BaseEntity baseEntity, ICustomSwitch customSwitch)
            {
                var hasSwitch = customSwitch != null;
                var jc = jointConfig;
                var joint = PropUtilities.CreateWorldItem(jc.Location, jc.Rotation, PREFAB_WORLD, jc.ItemId, baseEntity, jc.SkinId, false, true);
                var lgc = joint.gameObject.AddComponent<JointController>();
                lgc.Configure(jc, jc.DefaultPositionIsOff);

                if (hasSwitch && customSwitch.ToggleJoint)
                {
                    customSwitch.OnJointToggle += lgc.OnJointToggle;
                }

                InitializeFoilageInteractionTriggers(jc.FoilageInteraction, baseEntity as IKaruzaCustomEntity, joint.transform);
                InitializePhysicsTriggers(jc.PhysicsTriggers, baseEntity, joint.transform);
                InitializeSafeZoneTriggers(jc.SafeZoneTriggers, baseEntity, joint.transform);
                InitializeHurtTriggers(baseEntity as IVehicle, jc.HurtTriggerSettings, joint.transform);

                for (int k = 0; k < jc.SubPropConfigs.Count; k++)
                {
                    var spc = jc.SubPropConfigs[k];
                    var sp = PropUtilities.InitializeProp(spc, joint, baseEntity);

                    if (hasSwitch && customSwitch.ToggleLights && sp is SpecialLight light)
                    {
                        light.CanVehicleToggle = false;
                        customSwitch.Lights.Add(light);
                        continue;
                    }

                    if (hasSwitch && customSwitch.ToggleDoors && sp is SpecialDoor door)
                    {
                        customSwitch.Doors.Add(door);
                        continue;
                    }

                    if (hasSwitch && customSwitch.TogglePropellers && spc.PartType == PartType.Propeller)
                    {
                        var propeller = sp.GetComponent<VehiclePropeller>();
                        if (propeller != null)
                        {
                            propeller.DeregisterActions();
                            customSwitch.Propellers.Add(propeller);
                        }
                    }
                }

                return lgc;
            }

            public static void InitializeCustomWheels(BaseEntity baseEntity, List<CustomWheelConfig> customWheels)
            {
                if (customWheels == null)
                {
                    return;
                }

                for (int i = 0; i < customWheels.Count; i++)
                {
                    var csw = customWheels[i];
                    if (!csw.Enabled)
                    {
                        continue;
                    }

                    var hasRotationWheel = csw.RotationWheel != null && csw.RotationWheel.Enabled;
                    var hasPositionWheel = csw.PositionWheel != null && csw.PositionWheel.Enabled;
                    if (!hasRotationWheel && !hasPositionWheel)
                    {
                        continue;
                    }

                    ICustomSwitch customSwitch = null;
                    var hasSwitch = csw.SwitchConfig != null && csw.SwitchConfig.Enabled && !string.IsNullOrEmpty(csw.SwitchConfig.PrefabPath);
                    if (hasSwitch)
                    {
                        if (csw.SwitchConfig.PrefabPath == BUTTON_PREFAB || csw.SwitchConfig.PrefabPath == BUTTON_COMPACT_PREFAB || csw.SwitchConfig.PrefabPath == BUTTON_TRAIN_STAIRS_PREFAB || csw.SwitchConfig.PrefabPath == BUTTON_INVIS_PREFAB)
                        {
                            customSwitch = PropUtilities.CreateCustomEntity<CustomButton>(csw.SwitchConfig.Location, csw.SwitchConfig.Rotation, csw.SwitchConfig.Scale, csw.SwitchConfig.PrefabPath, baseEntity);
                        }
                        else
                        {
                            customSwitch = PropUtilities.CreateCustomEntity<CustomSwitch>(csw.SwitchConfig.Location, csw.SwitchConfig.Rotation, csw.SwitchConfig.Scale, csw.SwitchConfig.PrefabPath, baseEntity);
                        }

                        customSwitch.ToggleDoors = csw.SwitchConfig.ToggleDoors;
                        customSwitch.ToggleLights = csw.SwitchConfig.ToggleLights;
                        customSwitch.TogglePropellers = csw.SwitchConfig.TogglePropellers;
                        customSwitch.Config = csw.SwitchConfig;
                    }

                    var jc = csw.JointConfig;
                    var joint = InitializeJoint(jc, baseEntity, customSwitch);

                    if (hasRotationWheel)
                    {
                        var offRotation = Quaternion.Euler(jc.RotationWhenOff);
                        var onRotation = Quaternion.Euler(jc.RotationWhenOn);
                        var angle = Quaternion.Angle(offRotation, onRotation);
                        var rotationWheel = PropUtilities.CreateCustomEntity<SpecialWheelSwitch>(csw.RotationWheel.Location, csw.RotationWheel.Rotation, csw.RotationWheel.Scale, WHEEL_SWITCH_PREFAB, baseEntity);
                        rotationWheel.MinRotation = 0;
                        rotationWheel.MaxRotation = angle;
                        rotationWheel.OnRotation += joint.UpdateRotation;
                        rotationWheel.RotateAmount = 1;
                        rotationWheel.RoundValue = false;
                    }

                    if (hasPositionWheel)
                    {
                        var angle = Vector3.Distance(jc.PositionWhenOff, jc.PositionWhenOn);
                        var positionWheel = PropUtilities.CreateCustomEntity<SpecialWheelSwitch>(csw.PositionWheel.Location, csw.PositionWheel.Rotation, csw.PositionWheel.Scale, WHEEL_SWITCH_PREFAB, baseEntity);
                        positionWheel.MinRotation = 0;
                        positionWheel.MaxRotation = angle;
                        positionWheel.OnRotation += joint.UpdatePosition;
                        positionWheel.RotateAmount = 1;
                        positionWheel.RoundValue = false;
                    }
                }
            }

            public static void InitializeInputJoints(IVehicle vehicle, List<CustomInputJointConfig> inputJoints)
            {
                if (inputJoints == null)
                {
                    return;
                }

                for (int i = 0; i < inputJoints.Count; i++)
                {
                    var ij = inputJoints[i];
                    if (!ij.Enabled)
                    {
                        continue;
                    }

                    ICustomSwitch customSwitch = null;
                    var hasSwitch = ij.SwitchConfig != null && ij.SwitchConfig.Enabled && !string.IsNullOrEmpty(ij.SwitchConfig.PrefabPath);
                    if (hasSwitch)
                    {
                        if (ij.SwitchConfig.PrefabPath == BUTTON_PREFAB || ij.SwitchConfig.PrefabPath == BUTTON_COMPACT_PREFAB || ij.SwitchConfig.PrefabPath == BUTTON_TRAIN_STAIRS_PREFAB || ij.SwitchConfig.PrefabPath == BUTTON_INVIS_PREFAB)
                        {
                            customSwitch = PropUtilities.CreateCustomEntity<CustomButton>(ij.SwitchConfig.Location, ij.SwitchConfig.Rotation, ij.SwitchConfig.Scale, ij.SwitchConfig.PrefabPath, vehicle.BaseEntity);
                        }
                        else
                        {
                            customSwitch = PropUtilities.CreateCustomEntity<CustomSwitch>(ij.SwitchConfig.Location, ij.SwitchConfig.Rotation, ij.SwitchConfig.Scale, ij.SwitchConfig.PrefabPath, vehicle.BaseEntity);
                        }

                        customSwitch.ToggleDoors = ij.SwitchConfig.ToggleDoors;
                        customSwitch.ToggleLights = ij.SwitchConfig.ToggleLights;
                        customSwitch.TogglePropellers = ij.SwitchConfig.TogglePropellers;
                        customSwitch.Config = ij.SwitchConfig;
                    }

                    var jc = ij.JointConfig;
                    var joint = InitializeJoint(jc, vehicle.BaseEntity, customSwitch);
                    joint.ListenForPlayerInputs = true;
                    joint.OnButton = ij.OnButton;
                    joint.OffButton = ij.OffButton;

                    InitializeDynamicTowing(ij.DynamicTowingSettings, vehicle, joint.SpecialBaseCombatEntity);
                }
            }

            private static void InitializeLandingGear(ILandingGearVehicle vehicle)
            {
                if (vehicle.LandingGearSettings == null)
                {
                    return;
                }

                if (vehicle.LandingGearSettings.Enabled)
                {
                    for (int i = 0; i < vehicle.LandingGearSettings.SwitchConfigs.Count; i++)
                    {
                        var sc = vehicle.LandingGearSettings.SwitchConfigs[i];
                        InitializeLandingGearSwitch(vehicle.BaseVehicle, sc);
                    }
                }

                for (int i = 0; i < vehicle.LandingGearSettings.LandingPropConfigs.Count; i++)
                {
                    var lpc = vehicle.LandingGearSettings.LandingPropConfigs[i];
                    InitializeLandingGearController(vehicle, lpc);
                }
            }

            public static void InitializeLandingGearController(ILandingGearVehicle vehicle, JointConfig lpc)
            {
                var prop = PropUtilities.CreateWorldItem(lpc.Location, lpc.Rotation, PREFAB_WORLD, lpc.ItemId, vehicle.BaseVehicle, lpc.SkinId, false, true);
                var lgc = prop.gameObject.AddComponent<JointController>();
                lgc.Configure(lpc, false);
                vehicle.OnLandingGearToggle += lgc.OnJointToggle;
                if (lpc.SubPropConfigs == null)
                {
                    lpc.SubPropConfigs = new List<PropConfig>();
                }

                for (int i = 0; i < lpc.SubPropConfigs.Count; i++)
                {
                    var spc = lpc.SubPropConfigs[i];
                    PropUtilities.InitializeProp(spc, prop, vehicle.BaseVehicle);
                }
            }

            private static void InitializeLandingGearSwitch(BaseEntity baseEntity, PropConfigSettings pc)
            {
                if (!pc.Enabled || string.IsNullOrEmpty(pc.PrefabPath))
                {
                    return;
                }

                PropUtilities.CreateCustomEntity<LandingGearSwitch>(pc.Location, pc.Rotation, pc.Scale, pc.PrefabPath, baseEntity);
            }

            public static void InitializeRadio(IVehicle vehicle)
            {
                if (vehicle.VehicleConfig.RadioSettings == null || !vehicle.VehicleConfig.RadioSettings.Enabled || vehicle.VehicleConfig.RadioSettings.RadioConfig == null)
                {
                    return;
                }

                if (!Instance.configuration.EnableRadio)
                {
                    if (vehicle.VehicleConfig.RadioSettings.BroadcasterConfig.Enabled)
                    {
                        PropUtilities.CreateCustomEntity(vehicle.VehicleConfig.RadioSettings.BroadcasterConfig.Location, vehicle.VehicleConfig.RadioSettings.BroadcasterConfig.Rotation, vehicle.VehicleConfig.RadioSettings.BroadcasterConfig.Scale, RF_BROADCASTER_PREFAB, vehicle.BaseVehicle, 0, string.Empty);

                        return;
                    }

                    return;
                }

                var cockpitRadio = PropUtilities.CreateEntityProp(vehicle.VehicleConfig.RadioSettings.RadioConfig.Location, vehicle.VehicleConfig.RadioSettings.RadioConfig.Rotation, new Vector3(0.0001f, 0.0001f, 0.0001f), TELEPHONE_PREFAB, vehicle.BaseVehicle, 0, string.Empty) as Telephone;
                cockpitRadio.SetMaxHealth(999999);
                cockpitRadio.SetHealth(999999);
                cockpitRadio.Controller.activeCallTo = cockpitRadio.Controller;

                using (FlagsUpdateScope flagsUpdateScope = cockpitRadio.StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.Reserved8, true);
                    flagsUpdateScope.Set(Flags.Locked, true);
                }

                if (!vehicle.VehicleConfig.RadioSettings.BroadcasterConfig.Enabled)
                {
                    return;
                }

                var radioBroadcaster = PropUtilities.CreateCustomEntity<SpecialBroadcaster>(vehicle.VehicleConfig.RadioSettings.BroadcasterConfig.Location, vehicle.VehicleConfig.RadioSettings.BroadcasterConfig.Rotation, vehicle.VehicleConfig.RadioSettings.BroadcasterConfig.Scale, RF_BROADCASTER_PREFAB, vehicle.BaseVehicle, 0, string.Empty);
                radioBroadcaster.frequency = 1;

                using (FlagsUpdateScope flagsUpdateScope = radioBroadcaster.StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.Reserved8, true);
                }
            }

            public static void InitializeProps(BaseEntity baseEntity, List<PropConfig> propConfigs)
            {
                for (int i = 0; i < propConfigs.Count; i++)
                {
                    var prop = propConfigs[i];
                    PropUtilities.InitializeProp(prop, baseEntity, baseEntity);
                }
            }

            public static BaseEntity InitializeProp(PropConfig propConfig, BaseEntity parent, BaseEntity karuzaEntity)
            {
                BaseEntity prop = null;
                var syncPosition = false;

                try
                {
                    if (propConfig.PartType == PartType.Mount || propConfig.PartType == PartType.LocalMount || propConfig.PartType == PartType.CustomCamera)
                    {
                        propConfig.PropType = PropType.None;
                    }
                    else if (propConfig.PartType == PartType.Seat || propConfig.PartType == PartType.DriverSeat || propConfig.PartType == PartType.LocalSeat)
                    {
                        propConfig.PropType = PropType.CustomBaseMountable;
                    }

                    switch (propConfig.PropType)
                    {
                        case PropType.None:
                            break;

                        case PropType.World:
                            syncPosition = propConfig.PartType == PartType.Propeller || propConfig.PartType == PartType.IOPropeller || propConfig.PartType == PartType.Wheel || propConfig.PartType == PartType.GimbalItem || (!object.ReferenceEquals(parent, null) && (parent.GetComponent<Gimbal>() != null || parent.GetComponent<SpecialCCTV_RC>() != null));
                            prop = PropUtilities.CreateWorldItem(propConfig.Location, propConfig.Rotation, PREFAB_WORLD, propConfig.ItemId, parent, propConfig.SkinId, propConfig.DetectCollisions, syncPosition);
                            break;

                        case PropType.Entity:
                            prop = PropUtilities.CreateEntityProp(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, true);
                            if (prop.PrefabName == PREFAB_PRISON_GATE)
                            {
                                using (FlagsUpdateScope flagsUpdateScope = prop.StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                                {
                                    flagsUpdateScope.Set(Flags.Locked, false);
                                }
                            }

                            break;

                        case PropType.Gib:
                            prop = PropUtilities.CreateGib(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, propConfig.GibName, parent, propConfig.DetectCollisions, propConfig.PartType != PartType.Propeller);
                            break;

                        case PropType.CustomCombatEntity:
                            if (propConfig.PrefabPath == PREFAB_SPECIALTRAVELLINGVENDOR_FULLPATH)
                            {
                                prop = PropUtilities.CreateSpecialTravellingVendor(propConfig.Location, propConfig.Rotation, parent);
                            }
                            else if (propConfig.PrefabPath == HOT_AIR_BALOON_PREFAB)
                            {
                                prop = PropUtilities.CreateCustomEntity<SpecialHotAirBaloon>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions, false, true);
                            }
                            else if (propConfig.PrefabPath == SEDAN_PREFAB || propConfig.PrefabPath == SEDAN_RAIL_PREFAB)
                            {
                                prop = PropUtilities.CreateCustomEntity<SpecialSedan>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions, true);
                            }
                            else if (propConfig.PrefabPath == JET_PREFAB && propConfig.PartType == PartType.Prop)
                            {
                                prop = PropUtilities.CreateCustomEntity<SpecialJet>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions, true);
                            }
                            else if (propConfig.PrefabPath == CARGO_PREFAB && propConfig.PartType == PartType.Prop)
                            {
                                prop = PropUtilities.CreateCustomEntity<SpecialCargoShip>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions, true);
                            }
                            else if (propConfig.PrefabPath == TROPHY_PREFAB && propConfig.PartType == PartType.Prop)
                            {
                                prop = PropUtilities.CreateCustomEntity<SpecialHuntingTrophy>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions, false);
                            }
                            else if (propConfig.PrefabPath == VEHICLE_FUEL_MODULE || propConfig.PrefabPath == VEHICLE_STORAGE_MODULE || propConfig.PrefabPath == VEHICLE_ENGINE_MODULE
                            || propConfig.PrefabPath == VEHICLE_COCKPIT_MODULE || propConfig.PrefabPath == VEHICLE_ARMORED_PASSENGER_MODULE || propConfig.PrefabPath == VEHICLE_ARMORED_PASSENGER_MODULE
                            || propConfig.PrefabPath == VEHICLE_ARMORED_PASSENGER_MODULE)
                            {
                                prop = PropUtilities.CreateCustomEntity<SpecialVehicleModule>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions);
                            }
                            else if (propConfig.PrefabPath == BRADLEY_PREFAB)
                            {
                                prop = PropUtilities.CreateCustomEntity<SpecialBradley>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions, true);
                            }
                            else
                            {
                                prop = PropUtilities.CreateCustomEntity(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions);
                            }
                            break;

                        case PropType.CustomBaseMountable:
                            //prop = CreateEntityProp(propConfig.Location, propConfig.Rotation, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, true);
                            syncPosition = propConfig.PartType == PartType.Seat && parent.GetComponent<Gimbal>() != null;
                            if (Instance.InstrumentPrefabs.Contains(propConfig.PrefabPath))
                            {
                                prop = PropUtilities.CreateCustomBaseMountable<SpecialStaticInstrument>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions, syncPosition);
                            }
                            else if (propConfig.PrefabPath == STEERING_WHEEL_PREFAB)
                            {
                                prop = PropUtilities.CreateCustomBaseMountable<SpecialSteeringWheel>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions, syncPosition);
                            }
                            else
                            {
                                var sbm = PropUtilities.CreateCustomBaseMountable<SpecialBaseMountable>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions, syncPosition);
                                sbm.ForceDriver = propConfig.PartType != PartType.LocalSeat;
                                prop = sbm;
                            }

                            break;

                        case PropType.CustomDoor:
                            prop = PropUtilities.CreateCustomDoor<SpecialDoor>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions);
                            break;

                        default:
                            return null;
                    }

                    switch (propConfig.PartType)
                    {
                        case PartType.MiniGun:
                            prop = PropUtilities.CreateCustomEntity<SpecialGun>(propConfig.Location, propConfig.Rotation, propConfig.Scale, MINI_GUN_PREFAB, parent);
                            break;

                        case PartType.M249:
                            prop = PropUtilities.CreateCustomEntity<SpecialGun>(propConfig.Location, propConfig.Rotation, propConfig.Scale, M249_GUN_PREFAB, parent);
                            break;

                        case PartType.SpecialGun:
                            if (propConfig.PrefabPath == SPRINKLER_PREFAB)
                            {
                                prop = PropUtilities.CreateCustomEntity<SprinklerSpecialGun>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent);
                            }
                            else
                            {
                                prop = PropUtilities.CreateCustomEntity<SpecialGun>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent);
                            }
                            break;

                        case PartType.Propeller:
                            var propeller = prop.gameObject.AddComponent<VehiclePropeller>();
                            if (propConfig.PropType == PropType.Gib)
                            {
                                prop.Spawn();
                            }
                            break;

                        case PartType.IOPropeller:
                            var ioPropeller = prop.gameObject.AddComponent<VehiclePropeller>();
                            ioPropeller.RequireEngine = false;
                            break;

                        case PartType.CarEngine:
                            var chassis = PropUtilities.CreateCustomEntity<SpecialModularCarEngine>(propConfig.Location, propConfig.Rotation, new Vector3(0.001f, 0.001f, 0.001f), CHASSIS_PREFAB, parent);
                            var engineMod = ItemManager.CreateByItemID(1559779253, 1);
                            chassis.TryAddModule(engineMod, 0);
                            chassis.LimitNetworkingWhenOff = true;
                            chassis.limitNetworking = true;
                            prop = chassis;
                            break;

                        case PartType.GenericShrunkEngine:
                            SpecialFakeEngine fakeShrunkEngine;
                            if (propConfig.PrefabPath == MOTOR_BIKE_PREFAB)
                            {
                                var bikeEngine = PropUtilities.CreateCustomEntity<SpecialBikeEngine>(propConfig.Location, propConfig.Rotation, new Vector3(0.001f, 0.001f, 0.001f), propConfig.PrefabPath, parent, 0, string.Empty, syncPosition: syncPosition);
                                bikeEngine.IsMotorBike = true;
                                fakeShrunkEngine = bikeEngine;
                            }
                            else if (propConfig.PrefabPath == PEDAL_BIKE_PREFAB)
                            {
                                fakeShrunkEngine = PropUtilities.CreateCustomEntity<SpecialBikeEngine>(propConfig.Location, propConfig.Rotation, new Vector3(0.001f, 0.001f, 0.001f), propConfig.PrefabPath, parent, 0, string.Empty, syncPosition: syncPosition);
                            }
                            else if (propConfig.PrefabPath == DRONE_PREFAB)
                            {
                                var drone = PropUtilities.CreateCustomEntity<SpecialFakeDroneEngine>(propConfig.Location, propConfig.Rotation, new Vector3(0.001f, 0.001f, 0.001f), propConfig.PrefabPath, parent, 0, string.Empty);
                                fakeShrunkEngine = drone;
                            }
                            else if (propConfig.PrefabPath == BATTERING_RAM_PREFAB)
                            {
                                var batteringRam = PropUtilities.CreateCustomEntity<SpecialBatteringRamEngine>(propConfig.Location, propConfig.Rotation, new Vector3(0.001f, 0.001f, 0.001f), propConfig.PrefabPath, parent, 0, string.Empty);
                                fakeShrunkEngine = batteringRam;
                            }
                            else if (propConfig.PrefabPath == SEDAN_PREFAB || propConfig.PrefabPath == SEDAN_RAIL_PREFAB)
                            {
                                prop = PropUtilities.CreateCustomEntity<SpecialSedan>(propConfig.Location, propConfig.Rotation, new Vector3(0.001f, 0.001f, 0.001f), propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions, true);
                                break;
                            }
                            else if (propConfig.PrefabPath == SNOWMOBILE_PREFAB || propConfig.PrefabPath == SNOWMOBILE2_PREFAB)
                            {
                                fakeShrunkEngine = PropUtilities.CreateCustomEntity<SpecialSnowmobileEngine>(propConfig.Location, propConfig.Rotation, new Vector3(0.001f, 0.001f, 0.001f), propConfig.PrefabPath, parent, 0, string.Empty, propConfig.DetectCollisions, true);
                            }
                            else if (propConfig.PrefabPath == RHIB_PREFAB || propConfig.PrefabPath == MOTOR_BOAT_PREFAB || propConfig.PrefabPath == PT_BOAT_PREFAB)
                            {
                                fakeShrunkEngine = PropUtilities.CreateCustomEntity<SpecialBoatEngine>(propConfig.Location, propConfig.Rotation, new Vector3(0.001f, 0.001f, 0.001f), propConfig.PrefabPath, parent, 0, string.Empty, propConfig.DetectCollisions, true);
                            }
                            else
                            {
                                fakeShrunkEngine = PropUtilities.CreateCustomEntity<SpecialFakeEngine>(propConfig.Location, propConfig.Rotation, new Vector3(0.001f, 0.001f, 0.001f), propConfig.PrefabPath, parent, 0, string.Empty);
                            }

                            fakeShrunkEngine.LimitNetworkingWhenOff = true;
                            fakeShrunkEngine.limitNetworking = true;
                            prop = fakeShrunkEngine;
                            break;

                        case PartType.IOShrunkEngine:
                            SpecialFakeEngine fakeShrunkIOEngine;
                            fakeShrunkIOEngine = PropUtilities.CreateCustomEntity<SpecialFakeIOEngine>(propConfig.Location, propConfig.Rotation, new Vector3(0.001f, 0.001f, 0.001f), propConfig.PrefabPath, parent, 0, string.Empty, propConfig.DetectCollisions);

                            fakeShrunkIOEngine.LimitNetworkingWhenOff = true;
                            fakeShrunkIOEngine.limitNetworking = true;
                            prop = fakeShrunkIOEngine;
                            break;

                        case PartType.IOEngine:
                            SpecialFakeEngine fakeIOEngine;
                            fakeIOEngine = PropUtilities.CreateCustomEntity<SpecialFakeIOEngine>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, 0, string.Empty, propConfig.DetectCollisions);

                            prop = fakeIOEngine;
                            break;

                        case PartType.PropShrunkEngine:
                            SpecialFakeEngine fakeShrunkPropEngine;
                            fakeShrunkPropEngine = PropUtilities.CreateCustomEntity<SpecialFakePropEngine>(propConfig.Location, propConfig.Rotation, new Vector3(0.001f, 0.001f, 0.001f), propConfig.PrefabPath, parent, propConfig.SkinId, string.Empty, propConfig.DetectCollisions);

                            prop = fakeShrunkPropEngine;
                            break;

                        case PartType.PropEngine:
                            SpecialFakeEngine fakePropEngine;
                            fakePropEngine = PropUtilities.CreateCustomEntity<SpecialFakePropEngine>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, string.Empty, propConfig.DetectCollisions);

                            prop = fakePropEngine;
                            break;

                        case PartType.GenericEngine:
                            SpecialFakeEngine fakeEngine;

                            if (propConfig.PrefabPath == MOTOR_BIKE_PREFAB)
                            {
                                var bikeEngine = PropUtilities.CreateCustomEntity<SpecialBikeEngine>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, 0, string.Empty, spawn: false);
                                bikeEngine.syncPosition = true;
                                bikeEngine.CanToggleLights = true;
                                bikeEngine.IsMotorBike = true;

                                bikeEngine.Spawn();
                                fakeEngine = bikeEngine;
                            }
                            else if (propConfig.PrefabPath == PEDAL_BIKE_PREFAB)
                            {
                                var bikeEngine = PropUtilities.CreateCustomEntity<SpecialBikeEngine>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, 0, string.Empty, spawn: false);
                                bikeEngine.syncPosition = true;
                                bikeEngine.CanToggleLights = true;

                                bikeEngine.Spawn();
                                fakeEngine = bikeEngine;
                            }
                            else if (propConfig.PrefabPath == DRONE_PREFAB)
                            {
                                var drone = PropUtilities.CreateCustomEntity<SpecialFakeDroneEngine>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, 0, string.Empty, propConfig.DetectCollisions, true);
                                fakeEngine = drone;
                            }
                            else if (propConfig.PrefabPath == BATTERING_RAM_PREFAB)
                            {
                                var batteringRam = PropUtilities.CreateCustomEntity<SpecialBatteringRamEngine>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, 0, string.Empty);
                                fakeEngine = batteringRam;
                            }
                            else if (propConfig.PrefabPath == SNOWMOBILE_PREFAB || propConfig.PrefabPath == SNOWMOBILE2_PREFAB)
                            {
                                var snowMobile = PropUtilities.CreateCustomEntity<SpecialSnowmobileEngine>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, 0, string.Empty, propConfig.DetectCollisions, true);
                                fakeEngine = snowMobile;
                            }
                            else if (propConfig.PrefabPath == RHIB_PREFAB || propConfig.PrefabPath == MOTOR_BOAT_PREFAB || propConfig.PrefabPath == PT_BOAT_PREFAB)
                            {
                                fakeEngine = PropUtilities.CreateCustomEntity<SpecialBoatEngine>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, 0, string.Empty, propConfig.DetectCollisions, true);
                            }
                            else
                            {
                                fakeEngine = PropUtilities.CreateCustomEntity<SpecialFakeEngine>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, 0, string.Empty, propConfig.DetectCollisions);
                            }

                            prop = fakeEngine;
                            break;

                        case PartType.Mount:
                        case PartType.LocalMount:
                            if (propConfig.PrefabPath == SNOWMOBILE_PREFAB || propConfig.PrefabPath == SNOWMOBILE2_PREFAB)
                            {
                                prop = PropUtilities.CreateCustomBaseMountable<SpecialSnowmobile>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions, true);
                            }
                            else if (propConfig.PrefabPath == MINICOPTER_PREFAB)
                            {
                                prop = PropUtilities.CreateCustomBaseMountable<SpecialHelicopter>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions, true);
                            }
                            else
                            {
                                var sbm = PropUtilities.CreateCustomBaseMountable<VehicleMount>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions, syncPosition);
                                prop = sbm;
                                sbm.ForceDriver = propConfig.PartType != PartType.LocalMount;
                            }

                            break;

                        case PartType.Seat:
                            if (prop.GetComponentInParent<Gimbal>() != null)
                            {
                                PropUtilities.InitializeSeat(prop, false, true, karuzaEntity as IVehicle);
                            }
                            else
                            {
                                PropUtilities.InitializeSeat(prop, false, false, karuzaEntity as IVehicle);
                            }

                            break;

                        case PartType.LocalSeat:
                            PropUtilities.InitializeSeat(prop, false, false, karuzaEntity as IVehicle);
                            break;

                        case PartType.DriverSeat:
                            PropUtilities.InitializeSeat(prop, true, false, karuzaEntity as IVehicle);
                            break;

                        case PartType.IOEntity:
                            if (prop == null)
                            {
                                prop = PropUtilities.CreateCustomEntity<SpecialIOEntity>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions);
                            }
                            break;

                        case PartType.CustomCamera:
                            if (propConfig.PrefabPath == TURRET_PREFAB)
                            {
                                var cc = PropUtilities.CreateCustomCamera<SpecialAutoTurret_RC>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions);
                                prop = cc;
                            }
                            else if (propConfig.PrefabPath == DRONE_PREFAB)
                            {
                                var cc = PropUtilities.CreateCustomCamera<SpecialDroneCamera>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions);
                                prop = cc;
                            }
                            else
                            {
                                var cc = PropUtilities.CreateCustomCamera<SpecialCCTV_RC>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions);
                                prop = cc;
                            }
                            break;

                        case PartType.ConditionalLight:
                            if (propConfig.PrefabPath == ATTACK_HELI_PREFAB || propConfig.PrefabPath == SCRAP_HELI_PREFAB)
                            {
                                var clv = PropUtilities.CreateCustomEntity<SpecialLightVehicle>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions);

                                clv.LimitNetworkinWhenOff = true;
                                clv.limitNetworking = true;
                                prop = clv;
                            }
                            else if (propConfig.PrefabPath == SEDAN_PREFAB || propConfig.PrefabPath == SEDAN_RAIL_PREFAB)
                            {
                                var sls = PropUtilities.CreateCustomEntity<SpecialLightSedan>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions);

                                sls.LimitNetworkinWhenOff = true;
                                sls.limitNetworking = true;
                                prop = sls;
                            }
                            else
                            {
                                var cl = PropUtilities.CreateCustomEntity<SpecialLight>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions);

                                cl.LimitNetworkinWhenOff = true;
                                cl.limitNetworking = true;
                                prop = cl;
                            }
                            break;

                        case PartType.Light:
                            if (propConfig.PrefabPath == ATTACK_HELI_PREFAB || propConfig.PrefabPath == SCRAP_HELI_PREFAB)
                            {
                                prop = PropUtilities.CreateCustomEntity<SpecialLightVehicle>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions);
                            }
                            else
                            {
                                prop = PropUtilities.CreateCustomEntity<SpecialLight>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions);
                            }
                            break;

                        case PartType.ShrunkLight:
                            if (propConfig.PrefabPath == ATTACK_HELI_PREFAB || propConfig.PrefabPath == SCRAP_HELI_PREFAB)
                            {
                                var slv = PropUtilities.CreateCustomEntity<SpecialLightVehicle>(propConfig.Location, propConfig.Rotation, new Vector3(0.0001f, 0.0001f, 0.0001f), propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions);

                                slv.LimitNetworkinWhenOff = true;
                                slv.limitNetworking = true;
                                prop = slv;
                            }
                            else if (propConfig.PrefabPath == SEDAN_PREFAB || propConfig.PrefabPath == SEDAN_RAIL_PREFAB)
                            {
                                var sls = PropUtilities.CreateCustomEntity<SpecialLightSedan>(propConfig.Location, propConfig.Rotation, new Vector3(0.0001f, 0.0001f, 0.0001f), propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions);

                                sls.LimitNetworkinWhenOff = true;
                                sls.limitNetworking = true;
                                prop = sls;
                            }
                            else
                            {
                                var sl = PropUtilities.CreateCustomEntity<SpecialLight>(propConfig.Location, propConfig.Rotation, new Vector3(0.0001f, 0.0001f, 0.0001f), propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions);

                                sl.LimitNetworkinWhenOff = true;
                                sl.limitNetworking = true;
                                prop = sl;
                            }
                            break;

                        case PartType.ConditionalProp:
                            var cp = PropUtilities.CreateCustomEntity(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions);

                            cp.SetConditional(true);
                            prop = cp;
                            break;

                        case PartType.BuildingBlock:
                            var bb = PropUtilities.CreateSpecialBuildingBlock(propConfig, parent);
                            prop = bb;
                            break;

                        case PartType.ModularCarChassis:
                            prop = PropUtilities.CreateCustomEntity<SpecialModularCar>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions);
                            prop.enabled = true;
                            break;

                        case PartType.AudioVisualisationEntity:
                            prop = PropUtilities.CreateCustomEntity<SpecialAudioVisualisationEntity>(propConfig.Location, propConfig.Rotation, propConfig.Scale, propConfig.PrefabPath, parent, propConfig.SkinId, propConfig.NetworkPrefabOverride, propConfig.DetectCollisions);
                            break;

                        case PartType.Boat:
                            if (propConfig.PrefabPath == SAIL_PREFAB)
                            {
                                prop = PropUtilities.CreateSpecialSail(propConfig, parent);
                            }
                            else if (propConfig.PrefabPath == ANCHOR_PREFAB)
                            {
                                prop = PropUtilities.CreateSpecialAnchor(propConfig, parent);
                            }

                            break;

                        case PartType.TurretMount:
                            if (propConfig.PrefabPath == CANNON_PREFAB || propConfig.PrefabPath == STATIC_CANNON_PREFAB)
                            {
                                var cannon = PropUtilities.CreateSpecialTurret<Cannon, SpecialCannon>(propConfig, parent);
                                cannon.ForceDriver = false;

                                prop = cannon;
                            }
                            else if (propConfig.PrefabPath == SINGLE_FIFTYCAL_PREFAB || propConfig.PrefabPath == DOUBLE_FIFTYCAL_PREFAB)
                            {
                                var turret = PropUtilities.CreateSpecialTurret<MountedWeaponSeat, SpecialMountedWeaponSeat>(propConfig, parent);
                                turret.ForceDriver = false;

                                prop = turret;
                            }

                            PropUtilities.InitializeSeat(prop, false, true, karuzaEntity as IVehicle);
                            break;

                        case PartType.Ladder:
                            prop = PropUtilities.CreateSpecialLadder(propConfig, parent);
                            break;

                        default:
                            break;
                    }

                    if (propConfig.SubPropConfigs != null && propConfig.SubPropConfigs.Count > 0)
                    {
                        for (int i = 0; i < propConfig.SubPropConfigs.Count; i++)
                        {
                            var subConfig = propConfig.SubPropConfigs[i];
                            PropUtilities.InitializeProp(subConfig, prop, karuzaEntity);
                        }
                    }

                    using (FlagsUpdateScope flagsUpdateScope = prop.StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        for (int i = 0; i < FlagValues.Length; i++)
                        {
                            var flag = FlagValues[i];
                            if (propConfig.Flags.HasFlag(flag))
                            {
                                flagsUpdateScope.Set(flag, true);
                            }
                        }
                    }

                    return prop;

                }
                catch (Exception ex)
                {
                    Instance.PrintError($"{propConfig.PartType} - {propConfig.PropType} - Path: {propConfig.PrefabPath} - ItemId: {propConfig.ItemId} Failed");
                    throw;
                }
            }

            public static void InitializeSeat(BaseEntity entity, bool isDriver, bool isGunner, IKaruzaCustomEntity karuzaEntity)
            {
                if (karuzaEntity == null)
                {
                    return;
                }

                var seat = entity as BaseMountable;
                seat.legacyDismount = true;
                seat.ignoreVehicleParent = false;
                seat.isMobile = true;

                List<Transform> seatDismountPoints = new List<Transform>();
                for (int i = 0; i < seat.dismountPositions.Length; i++)
                {
                    seatDismountPoints.Add(seat.dismountPositions[i]);
                }

                GameObject topDismount = new GameObject();
                topDismount.transform.position = karuzaEntity.BaseEntity.transform.position + (karuzaEntity.BaseEntity.transform.up * 3);
                topDismount.transform.transform.SetParent(karuzaEntity.BaseEntity.transform, true);
                seatDismountPoints.Add(topDismount.transform);

                GameObject leftDismount = new GameObject();
                leftDismount.transform.position = karuzaEntity.BaseEntity.transform.position + (karuzaEntity.BaseEntity.transform.right * -3) + (karuzaEntity.BaseEntity.transform.up * 1);
                leftDismount.transform.transform.SetParent(karuzaEntity.BaseEntity.transform, true);
                seatDismountPoints.Add(leftDismount.transform);

                GameObject rightDismount = new GameObject();
                rightDismount.transform.position = karuzaEntity.BaseEntity.transform.position + (karuzaEntity.BaseEntity.transform.right * 3) + (karuzaEntity.BaseEntity.transform.up * 1);
                rightDismount.transform.transform.SetParent(karuzaEntity.BaseEntity.transform, true);
                seatDismountPoints.Add(rightDismount.transform);

                GameObject forwardDismount = new GameObject();
                forwardDismount.transform.position = karuzaEntity.BaseEntity.transform.position + (karuzaEntity.BaseEntity.transform.forward * 4) + (karuzaEntity.BaseEntity.transform.up * 1);
                forwardDismount.transform.transform.SetParent(karuzaEntity.BaseEntity.transform, true);
                seatDismountPoints.Add(forwardDismount.transform);

                GameObject backDismount = new GameObject();
                backDismount.transform.position = karuzaEntity.BaseEntity.transform.position + (karuzaEntity.BaseEntity.transform.forward * -4) + (karuzaEntity.BaseEntity.transform.up * 1);
                backDismount.transform.transform.SetParent(karuzaEntity.BaseEntity.transform, true);
                seatDismountPoints.Add(backDismount.transform);

                seat.dismountPositions = seatDismountPoints.ToArray();
                seat.SendNetworkUpdateImmediate();

                var mpi = new BaseVehicle.MountPointInfo()
                {
                    mountable = seat,
                    pos = seat.transform.position,
                    rot = seat.transform.rotation.eulerAngles,
                    isDriver = isDriver
                };

                if (karuzaEntity is IVehicle vehicle)
                {
                    if (isDriver && vehicle.VehicleConfig.WeaponConfigurations.Count > 0 && seat is SpecialBaseMountable sbm)
                    {
                        sbm.giveCrosshair = true;
                    }

                    if (isDriver || isGunner)
                    {
                        for (int i = 0; i < vehicle.BaseVehicle.mountPoints.Count; i++)
                        {
                            var mp = vehicle.BaseVehicle.mountPoints[i];
                            if (!mp.isDriver)
                            {
                                vehicle.BaseVehicle.mountPoints.Insert(i, mpi);
                                return;
                            }
                        }
                    }

                    vehicle.BaseVehicle.mountPoints.Add(mpi);
                }
            }

            public static EngineStorageContainer InitializePhysicalEngine(IVehicle vehicle)
            {
                if (vehicle.VehicleConfig.PhysicalEngineSettings == null || !vehicle.VehicleConfig.PhysicalEngineSettings.Enabled)
                {
                    return null;
                }

                if (string.IsNullOrEmpty(vehicle.VehicleConfig.PhysicalEngineSettings.PrefabPath))
                {
                    Instance.Puts($"{vehicle.BaseVehicle.ShortPrefabName} - Has physical engine container enabled but no prefab path");
                    return null;
                }

                var itemCount = 5;
                if (vehicle.VehicleConfig.PhysicalEngineSettings.EngineType == EngineType.Large)
                {
                    itemCount = 8;
                }

                var containerSettings = new ContainerConfiguration()
                {
                    PrefabPath = vehicle.VehicleConfig.PhysicalEngineSettings.PrefabPath,
                    NetworkPrefabOverride = vehicle.VehicleConfig.PhysicalEngineSettings.NetworkPrefabOverride,
                    SkinId = vehicle.VehicleConfig.PhysicalEngineSettings.SkinId,
                    DetectCollisions = vehicle.VehicleConfig.PhysicalEngineSettings.DetectCollisions,
                    PanelName = ENGINE_PANEL,
                    IsLocked = vehicle.VehicleConfig.PhysicalEngineSettings.IsLocked,
                    DropsLoot = vehicle.VehicleConfig.PhysicalEngineSettings.DropsLoot,
                    Location = vehicle.VehicleConfig.PhysicalEngineSettings.Location,
                    Rotation = vehicle.VehicleConfig.PhysicalEngineSettings.Rotation,
                    Scale = vehicle.VehicleConfig.PhysicalEngineSettings.Scale,
                    DefaultItems = vehicle.VehicleConfig.PhysicalEngineSettings.DefaultItems,
                    IgnoreCodelock = vehicle.VehicleConfig.PhysicalEngineSettings.IgnoreCodelock,
                    ItemCount = itemCount
                };

                return PropUtilities.CreateContainer<EngineStorageContainer>(vehicle.BaseVehicle, containerSettings);
            }

            public static ISpecialAmmoContainer InitializeAmmoContainer(BaseEntity baseEntity, AmmoContainerConfiguration ammoContainer)
            {
                if (ammoContainer == null || !ammoContainer.Enabled)
                {
                    return null;
                }

                if (string.IsNullOrEmpty(ammoContainer.PrefabPath))
                {
                    Instance.Puts($"{baseEntity.ShortPrefabName} - Has ammo container enabled but no prefab path");
                    return null;
                }

                ISpecialAmmoContainer returnContainer = null;
                if (ammoContainer.PrefabPath == PREFAB_WATER_BARREL)
                {
                    returnContainer = PropUtilities.CreateLiquidContainer<VehicleAmmoLiquidContainer>(baseEntity, ammoContainer);
                }
                else
                {
                    returnContainer = PropUtilities.CreateContainer<VehicleAmmoContainer>(baseEntity, ammoContainer);
                }

                returnContainer.Config = ammoContainer;

                for (int i = 0; i < ammoContainer.ProxyContainers.Count; i++)
                {
                    var pcc = ammoContainer.ProxyContainers[i];
                    if (!pcc.Enabled)
                    {
                        continue;
                    }

                    var pc = PropUtilities.CreateContainerProxy<SpecialStorageContainerProxy>(baseEntity, pcc, ammoContainer.PanelName);
                    pc.MainContainer = returnContainer;
                }

                for (int i = 0; i < ammoContainer.Adaptors.Count; i++)
                {
                    var ad = ammoContainer.Adaptors[i];
                    if (!ad.Enabled)
                    {
                        continue;
                    }

                    var adc = PropUtilities.CreateContainerIOEntity(baseEntity, ad, returnContainer);
                }

                return returnContainer;
            }

            public static void InitializeStorageContainers(BaseEntity baseEntity, List<StorageContainerConfiguration> storageContainers)
            {
                if (storageContainers == null)
                {
                    return;
                }

                for (int i = 0; i < storageContainers.Count; i++)
                {
                    var storageContainer = storageContainers[i];
                    if (!storageContainer.Enabled)
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(storageContainer.PrefabPath))
                    {
                        Instance.Puts($"{baseEntity.ShortPrefabName} - Has storage enabled but no prefab path");
                        continue;
                    }

                    IStorageContainer container = null;
                    switch (storageContainer.StorageContainerType)
                    {
                        case StorageContainerType.Furnace:
                            var furnace = PropUtilities.CreateContainer<SpecialFurnaceOven>(baseEntity, storageContainer, false);
                            furnace.temperature = TemperatureType.Smelting;
                            furnace.smeltSpeed = 5;
                            furnace.Spawn();

                            container = furnace;
                            break;

                        case StorageContainerType.BBQ:
                            var oven = PropUtilities.CreateContainer<SpecialOven>(baseEntity, storageContainer, false);

                            oven.temperature = TemperatureType.Cooking;
                            oven.inputSlots = 3;
                            oven._inputSlotIndex = 1;
                            oven.outputSlots = 4;
                            oven._outputSlotIndex = 4;
                            oven.fuelSlots = 1;
                            oven.smeltSpeed = 5;

                            oven.Spawn();
                            container = oven;
                            break;

                        case StorageContainerType.FuellessBBQ:
                            var fuellessBBQ = PropUtilities.CreateContainer<SpecialFurnaceOven>(baseEntity, storageContainer, false);
                            fuellessBBQ.temperature = TemperatureType.Cooking;
                            fuellessBBQ.smeltSpeed = 5;
                            fuellessBBQ.Spawn();

                            container = fuellessBBQ;
                            break;

                        case StorageContainerType.Workbench:
                            container = PropUtilities.CreateContainer<SpecialWorkbench>(baseEntity, storageContainer);
                            break;

                        case StorageContainerType.Fridge:
                            container = PropUtilities.CreateContainer<SpecialFridge>(baseEntity, storageContainer);
                            break;

                        case StorageContainerType.CookingBench:
                            var cookingBench = PropUtilities.CreateContainer<SpecialMixingTable>(baseEntity, storageContainer, false);
                            cookingBench.Recipes.Recipes = Instance.CachedCookingTableRecipes.ToArray();
                            cookingBench.Recipes.FilenameStringId = Instance.CachedCookingTableFilenameStringId;
                            cookingBench.Spawn();
                            container = cookingBench;
                            break;

                        case StorageContainerType.MixingTable:
                            var mixingTable = PropUtilities.CreateContainer<SpecialMixingTable>(baseEntity, storageContainer, false);
                            mixingTable.Recipes.Recipes = Instance.CachedMixingTableRecipes.ToArray();
                            mixingTable.Recipes.FilenameStringId = Instance.CachedMixingTableFilenameStringId;
                            mixingTable.Spawn();
                            container = mixingTable;
                            break;

                        case StorageContainerType.Locker:
                            container = PropUtilities.CreateContainer<SpecialLocker>(baseEntity, storageContainer);
                            break;

                        case StorageContainerType.ShopFront:
                            container = PropUtilities.CreateContainer<SpecialShopFront>(baseEntity, storageContainer);
                            break;

                        case StorageContainerType.Beehive:
                            container = PropUtilities.CreateContainer<SpecialBeehive>(baseEntity, storageContainer);
                            break;

                        case StorageContainerType.Storage:
                        default:
                            if (storageContainer.PrefabPath == PREFAB_WATER_BARREL)
                            {
                                container = PropUtilities.CreateLiquidContainer<VehicleLiquidStorageContainer>(baseEntity, storageContainer);
                                container.Config = storageContainer;
                            }
                            else
                            {
                                container = PropUtilities.CreateContainer<SpecialStorageContainer>(baseEntity, storageContainer);
                            }
                            break;
                    }

                    for (int n = 0; n < storageContainer.ProxyContainers.Count; n++)
                    {
                        var pcc = storageContainer.ProxyContainers[n];
                        if (!pcc.Enabled)
                        {
                            continue;
                        }

                        var pc = PropUtilities.CreateContainerProxy<SpecialStorageContainerProxy>(baseEntity, pcc, storageContainer.PanelName);
                        pc.MainContainer = container;
                    }

                    for (int n = 0; n < storageContainer.Adaptors.Count; n++)
                    {
                        var ad = storageContainer.Adaptors[n];
                        if (!ad.Enabled)
                        {
                            continue;
                        }

                        var adc = PropUtilities.CreateContainerIOEntity(baseEntity, ad, container);
                    }
                }
            }

            public static void InitializeRecyclers(BaseEntity baseEntity, List<RecyclerConfiguration> recyclers)
            {
                if (recyclers == null)
                {
                    return;
                }

                for (int i = 0; i < recyclers.Count; i++)
                {
                    var recycler = recyclers[i];
                    if (!recycler.Enabled)
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(recycler.PrefabPath))
                    {
                        Instance.Puts($"{baseEntity.ShortPrefabName} - Has recycler enabled but no prefab path");
                        continue;
                    }

                    var container = PropUtilities.CreateContainer<SpecialRecycler>(baseEntity, recycler);
                    container.Configure(recycler);

                    for (int n = 0; n < recycler.ProxyContainers.Count; n++)
                    {
                        var pcc = recycler.ProxyContainers[n];
                        if (!pcc.Enabled)
                        {
                            continue;
                        }

                        var pc = PropUtilities.CreateContainerProxy<SpecialStorageContainerProxy>(baseEntity, pcc, recycler.PanelName);
                        pc.MainContainer = container;
                    }

                    for (int n = 0; n < recycler.Adaptors.Count; n++)
                    {
                        var ad = recycler.Adaptors[n];
                        if (!ad.Enabled)
                        {
                            continue;
                        }

                        var adc = PropUtilities.CreateContainerIOEntity(baseEntity, ad, container);
                    }
                }
            }

            public static VehicleFuelContainer InitializeFuelContainer(IVehicle vehicle)
            {
                if (vehicle.VehicleConfig.FuelSettings.FuelContainer == null || vehicle.VehicleConfig.FuelSettings.FuelSource != FuelSource.Container)
                {
                    return null;
                }

                if (string.IsNullOrEmpty(vehicle.VehicleConfig.FuelSettings.FuelContainer.PrefabPath))
                {
                    Instance.Puts($"{vehicle.BaseVehicle.ShortPrefabName} - Has Fuel enabled but no prefab path");
                    return null;
                }

                var prop = PropUtilities.CreateContainer<VehicleFuelContainer>(vehicle.BaseVehicle, vehicle.VehicleConfig.FuelSettings.FuelContainer);
                prop.AllowLootingWithDriver = vehicle.VehicleConfig.FuelSettings.AllowLootingWithDriver;

                for (int n = 0; n < vehicle.VehicleConfig.FuelSettings.FuelContainer.ProxyContainers.Count; n++)
                {
                    var pcc = vehicle.VehicleConfig.FuelSettings.FuelContainer.ProxyContainers[n];
                    if (!pcc.Enabled)
                    {
                        continue;
                    }

                    var pc = PropUtilities.CreateContainerProxy<SpecialStorageContainerProxy>(vehicle.BaseVehicle, pcc, prop.PanelName);
                    pc.MainContainer = prop;
                }

                for (int n = 0; n < vehicle.VehicleConfig.FuelSettings.FuelContainer.Adaptors.Count; n++)
                {
                    var ad = vehicle.VehicleConfig.FuelSettings.FuelContainer.Adaptors[n];
                    if (!ad.Enabled)
                    {
                        continue;
                    }

                    var adc = PropUtilities.CreateContainerIOEntity(vehicle.BaseVehicle, ad, prop);
                }

                return prop;
            }

            public static void InitializeBoost(IVehicle vehicle)
            {
                var boostSettings = vehicle.VehicleConfig.BoostSettings;
                if (boostSettings == null || !boostSettings.Enabled)
                {
                    return;
                }

                StorageContainer boostFuelContainer = null;
                if (boostSettings.BoostFuelContainer != null && boostSettings.BoostFuelSource == FuelSource.Container)
                {
                    if (string.IsNullOrEmpty(boostSettings.BoostFuelContainer.PrefabPath))
                    {
                        Instance.Puts($"{vehicle.BaseVehicle.ShortPrefabName} - Has Boost Container enabled but no prefab path");
                        return;
                    }

                    boostFuelContainer = PropUtilities.CreateContainer<VehicleBoostContainer>(vehicle.BaseVehicle, boostSettings.BoostFuelContainer);
                }

                for (int i = 0; i < boostSettings.BoostPropConfigs.Count; i++)
                {
                    var bpc = boostSettings.BoostPropConfigs[i];

                    if (string.IsNullOrEmpty(bpc.PrefabPath))
                    {
                        Instance.Puts($"{vehicle.BaseVehicle.ShortPrefabName} - Has Boost Prop without a prefab path, Skipping");
                        continue;
                    }

                    var prop = PropUtilities.CreateCustomEntity(bpc.Location, bpc.Rotation, bpc.Scale, bpc.PrefabPath, vehicle.BaseVehicle);

                    using (FlagsUpdateScope flagsUpdateScope = prop.StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                    {
                        for (int n = 0; n < PropUtilities.FlagValues.Length; n++)
                        {
                            var flag = PropUtilities.FlagValues[n];
                            if (bpc.Flags.HasFlag(flag))
                            {
                                flagsUpdateScope.Set(flag, true);
                            }
                        }
                    }

                    prop.gameObject.AddComponent<BoostWatcher>();
                }

                vehicle.BoostFuelSystem = new KaruzaVehicleFuelSystem(boostSettings.BoostFuelSource, boostFuelContainer);
                if (boostSettings.BoostCounters != null)
                {
                    for (int i = 0; i < boostSettings.BoostCounters.Count; i++)
                    {
                        var c = boostSettings.BoostCounters[i];
                        if (!c.Enabled)
                        {
                            continue;
                        }

                        var vfc = PropUtilities.CreateCustomEntity<VehicleFuelCounter>(c.Location, c.Rotation, c.Scale, PREFAB_COUNTER, vehicle.BaseVehicle, detectCollisions: c.DetectCollisions);
                        vfc.SetFuelSystem(vehicle.BoostFuelSystem);
                    }
                }

                if (boostSettings.BoostTimeCounters != null)
                {
                    for (int i = 0; i < boostSettings.BoostTimeCounters.Count; i++)
                    {
                        var c = boostSettings.BoostTimeCounters[i];
                        if (!c.Enabled)
                        {
                            continue;
                        }

                        PropUtilities.CreateCustomEntity<VehicleBoostTimeCounter>(c.Location, c.Rotation, c.Scale, PREFAB_COUNTER, vehicle.BaseVehicle, detectCollisions: c.DetectCollisions);
                    }
                }
            }

            public static BuoyancyPoint InitializeBuoynancy(BaseEntity vehicle, BouyancyPointConfig buoyancyPointConfig)
            {
                var buoyancyPointGameObject = new GameObject($"BuoyancyPoint_{buoyancyPointConfig.Location}");
                buoyancyPointGameObject.transform.localPosition = buoyancyPointConfig.Location;
                buoyancyPointGameObject.transform.SetParent(vehicle.gameObject.transform, false);

                var buoyancyPoint = buoyancyPointGameObject.AddComponent<BuoyancyPoint>();
                buoyancyPoint.buoyancyForce = buoyancyPointConfig.Force;
                buoyancyPoint.size = buoyancyPointConfig.Size;

                return buoyancyPoint;
            }

            public static WheelCollider InitializeWheelCollider(IVehicle vehicle, WheelColliderConfig wheelColliderConfig, out BaseEntity visEntity)
            {
                GameObject wheelColliderGameObject = new GameObject($"WheelCollider{wheelColliderConfig.Location}");
                wheelColliderGameObject.transform.localPosition = wheelColliderConfig.Location;
                wheelColliderGameObject.transform.SetParent(vehicle.BaseVehicle.gameObject.transform, false);

                visEntity = null;
                if (wheelColliderConfig.PropConfig != null)
                {
                    visEntity = InitializeProp(wheelColliderConfig.PropConfig, vehicle.BaseVehicle, vehicle.BaseEntity);
                    visEntity.syncPosition = true;
                    visEntity.transform.localPosition = wheelColliderConfig.PropConfig.Location;
                }

                var wc = wheelColliderGameObject.AddComponent<WheelCollider>();
                wc.center = Vector3.zero;
                wc.mass = wheelColliderConfig.Mass;
                wc.radius = wheelColliderConfig.Radius;
                wc.gameObject.layer = (int)Layer.Vehicle_World;
                //wc.includeLayers = LayerMask.GetMask(LayerMask.LayerToName((int)Layer.World));
                wc.isTrigger = false;
                wc.includeLayers = 0;
                wc.excludeLayers = (LayerMask)(-2147483136);
                wc.contactOffset = wheelColliderConfig.ContactOffset;
                wc.forceAppPointDistance = wheelColliderConfig.ForceAppPointDistance;

                //wc.excludeLayers = PHYSICS_DEBRIS_MASK;
                wc.suspensionSpring = new JointSpring()
                {
                    spring = wheelColliderConfig.SpringStiffness,
                    damper = wheelColliderConfig.Damper,
                    targetPosition = wheelColliderConfig.TargetPosition,
                };

                wc.suspensionDistance = wheelColliderConfig.SuspensionDistance;

                wc.wheelDampingRate = wheelColliderConfig.WheelDampingRate;
                wc.forwardFriction = new WheelFrictionCurve()
                {
                    extremumSlip = wheelColliderConfig.ForwardFriction.ExtremumSlip,
                    extremumValue = wheelColliderConfig.ForwardFriction.ExtremumValue,
                    asymptoteValue = wheelColliderConfig.ForwardFriction.AsymptoteValue,
                    asymptoteSlip = wheelColliderConfig.ForwardFriction.AsymptoteSlip,
                    stiffness = wheelColliderConfig.ForwardFriction.Stiffness
                };

                wc.sidewaysFriction = new WheelFrictionCurve()
                {
                    extremumSlip = wheelColliderConfig.SidewaysFriction.ExtremumSlip,
                    extremumValue = wheelColliderConfig.SidewaysFriction.ExtremumValue,
                    asymptoteValue = wheelColliderConfig.SidewaysFriction.AsymptoteValue,
                    asymptoteSlip = wheelColliderConfig.SidewaysFriction.AsymptoteSlip,
                    stiffness = wheelColliderConfig.SidewaysFriction.Stiffness
                };

                wc.ResetSprungMasses();
                wc.ConfigureVehicleSubsteps(5, 12, 15);

                return wc;
            }

            public static CodeLock InitializeCodelock(BaseEntity baseEntity, CodeLockConfig codeLockConfig)
            {
                var codelockConfig = codeLockConfig;
                if (codelockConfig == null || !codelockConfig.Enabled || codelockConfig.Location == Vector3.zero)
                {
                    return null;
                }

                var codeLock = PropUtilities.CreateCustomCodelock(codelockConfig, baseEntity);
                if (codelockConfig.CentralLockingSwitchSettings != null && codelockConfig.CentralLockingSwitchSettings.Enabled)
                {
                    var centralLockingSwitch = PropUtilities.CreateCustomEntity<CentralLockingSwitch>(codelockConfig.CentralLockingSwitchSettings.Location, codelockConfig.CentralLockingSwitchSettings.Rotation, codelockConfig.CentralLockingSwitchSettings.Scale, SWITCH_PREFAB, baseEntity);
                }

                return codeLock;
            }

            public static SpecialCCTV_RC CreateCustomCamera<T>(Vector3 location, Vector3 rotation, Vector3 scale, string prefab, BaseEntity parent, ulong skinId = 0, string networkPrefabOverride = "", bool detectCollisions = false, int layer = (int)Layer.Default) where T : SpecialCCTV_RC
            {
                var newGO = GameManager.server.CreatePrefab(prefab, location, Quaternion.Euler(rotation));
                newGO.transform.localScale = scale;

                var baseEntity = newGO.GetComponent<BaseEntity>();

                var maxHealth = 500;

                var oldIO = newGO.GetComponent<CCTV_RC>();
                UnityEngine.Object.DestroyImmediate(baseEntity);

                var iterateOver = newGO.GetComponentsInChildren<Component>().ToArray();
                for (var i = 0; i < iterateOver.Length; i++)
                {
                    var compo = iterateOver[i];
                    if (compo is Rigidbody)
                    {
                        continue;
                    }

                    if (compo is Transform)
                    {
                        continue;
                    }

                    UnityEngine.Object.DestroyImmediate(compo);
                }

                var rigidbody = newGO.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    UnityEngine.Object.DestroyImmediate(rigidbody);
                }

                var replacementEntity = newGO.AddComponent<T>();

                replacementEntity.prefabID = StringPool.Get(prefab);
                replacementEntity.syncPosition = true;
                replacementEntity.pickup.enabled = false;
                replacementEntity.baseProtection = ScriptableObject.CreateInstance<ProtectionProperties>();
                replacementEntity.bounds = baseEntity.bounds;
                replacementEntity.impactEffect = baseEntity.impactEffect;
                replacementEntity.gameObject.layer = layer;

                replacementEntity._maxHealth = maxHealth;
                replacementEntity.startHealth = maxHealth;
                replacementEntity.health = maxHealth;

                replacementEntity.sendsHitNotification = true;
                replacementEntity.EnableSaving(false);

                replacementEntity.SetParent(parent);
                replacementEntity.canTriggerParent = false;

                replacementEntity.flags = baseEntity.flags;
                replacementEntity.globalBuildingBlock = baseEntity.globalBuildingBlock;
                replacementEntity.model = baseEntity.model;
                replacementEntity.links = baseEntity.links;

                if (!object.ReferenceEquals(oldIO, null))
                {
                    replacementEntity.lifestate = oldIO.lifestate;
                    replacementEntity.markAttackerHostile = oldIO.markAttackerHostile;
                    replacementEntity.propDirection = oldIO.propDirection;
                    replacementEntity.fovScaleIndex = oldIO.fovScaleIndex;
                    replacementEntity.serverLerpSpeed = oldIO.serverLerpSpeed;
                    replacementEntity.clientLerpSpeed = oldIO.clientLerpSpeed;
                    replacementEntity.zoomLerpSpeed = oldIO.zoomLerpSpeed;
                    replacementEntity.turnSpeed = oldIO.turnSpeed;
                    replacementEntity.fovScaleLerped = oldIO.fovScaleLerped;
                    replacementEntity.fovScales = oldIO.fovScales;
                    replacementEntity.pitchClamp = oldIO.pitchClamp;
                    replacementEntity.pitch = oldIO.pitch;
                    replacementEntity.yawClamp = oldIO.yawClamp;
                    replacementEntity.yaw = oldIO.yaw;
                    replacementEntity.hasPTZ = prefab != CCTV_PREFAB;
                }
                else
                {
                    var yaw = new GameObject();
                    yaw.transform.SetParent(replacementEntity.transform, false);
                    replacementEntity.yaw = yaw.transform;

                    var pitch = new GameObject();
                    pitch.transform.SetParent(replacementEntity.transform, false);
                    replacementEntity.pitch = pitch.transform;

                    replacementEntity.yawClamp = new Vector2(-50f, 50f);
                    replacementEntity.pitchClamp = new Vector2(-50f, 50f);
                    replacementEntity.serverLerpSpeed = 100f;
                    replacementEntity.turnSpeed = 100f;

                    replacementEntity.hasPTZ = prefab == TURRET_PREFAB;
                }

                if (skinId > 0)
                {
                    replacementEntity.skinID = skinId;
                }

                if (!string.IsNullOrEmpty(networkPrefabOverride))
                {
                    replacementEntity.prefabID = StringPool.Get(networkPrefabOverride);
                }

                if (detectCollisions)
                {
                    replacementEntity.gameObject.layer = (int)Layer.Default;
                    BoxCollider bc = null;
                    if (prefab == PTZ_PREFAB)
                    {
                        bc = replacementEntity.gameObject.AddComponent<BoxCollider>();
                        bc.center = new Vector3(0, -0.191f, 0.0f);
                        bc.size = new Vector3(0.3f, 0.4f, 0.3f);
                    }

                    if (bc != null)
                    {
                        bc.enabled = true;
                        bc.isTrigger = false;
                        bc.excludeLayers = IGNORE_COL_MASK;
                        bc.contactOffset += 0.3f;
                    }
                    else
                    {
                        //Instance.PrintWarning($"Camera: {prefab} has collisions enabled but no existing collider");
                    }
                }

                replacementEntity.Spawn();
                return replacementEntity;
            }

            public static T CreateCustomBag<T>(RespawnPointConfig respawnPointConfig, RespawnSettings respawnSettings, BaseEntity parentEntity, int bagIdx) where T : SpecialSleepingBag, new()
            {
                Vector3 location = respawnPointConfig.Location;
                Vector3 rotation = respawnPointConfig.Rotation;
                string prefab = respawnPointConfig.PrefabPath;
                BaseEntity parent = parentEntity;
                ulong skinId = respawnPointConfig.SkinId;
                string networkPrefabOverride = respawnPointConfig.NetworkPrefabOverride;
                bool detectCollisions = respawnPointConfig.DetectCollisions;

                bool syncPosition = false;

                var newGO = GameManager.server.CreatePrefab(prefab, location, Quaternion.Euler(rotation));
                var baseEntity = newGO.GetComponent<BaseEntity>();

                var oldBag = newGO.GetComponent<SleepingBag>();
                UnityEngine.Object.DestroyImmediate(baseEntity);

                var maxHealth = 500;
                var iterateOver = newGO.GetComponentsInChildren<Component>().ToArray();
                var closedColliders = new List<GameObject>();
                for (var i = 0; i < iterateOver.Length; i++)
                {
                    var compo = iterateOver[i];

                    if (compo is Rigidbody)
                    {
                        continue;
                    }

                    if (compo is Transform)
                    {
                        continue;
                    }

                    if (detectCollisions && compo is Collider col && !col.isTrigger)
                    {
                        col.excludeLayers = IGNORE_COL_MASK;
                        compo.gameObject.layer = (int)Layer.Default;

                        continue;
                    }

                    if (compo is Model)
                    {
                        continue;
                    }

                    UnityEngine.Object.DestroyImmediate(compo);
                }

                var rigidbody = newGO.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    UnityEngine.Object.DestroyImmediate(rigidbody);
                }

                var replacementEntity = newGO.AddComponent<T>();

                replacementEntity.syncPosition = syncPosition;
                replacementEntity.prefabID = StringPool.Get(prefab);
                replacementEntity.pickup.enabled = false;
                replacementEntity.baseProtection = ScriptableObject.CreateInstance<ProtectionProperties>();
                replacementEntity.bounds = baseEntity.bounds;
                replacementEntity.impactEffect = baseEntity.impactEffect;
                replacementEntity.gameObject.layer = (int)Layer.Default;

                replacementEntity._maxHealth = maxHealth;
                replacementEntity.startHealth = maxHealth;
                replacementEntity.health = maxHealth;

                replacementEntity.sendsHitNotification = true;
                replacementEntity.EnableSaving(false);

                replacementEntity.SetParent(parent);
                replacementEntity.secondsBetweenReuses = respawnPointConfig.UnlockTime;
                replacementEntity.spawnOffset = oldBag.spawnOffset;
                replacementEntity.RespawnType = RespawnInformation.SpawnOptions.RespawnType.Camper;
                replacementEntity.isStatic = oldBag.isStatic;
                replacementEntity.canBePublic = oldBag.canBePublic;
                replacementEntity.notifyPlayerOnServerInit = oldBag.notifyPlayerOnServerInit;
                replacementEntity.canTriggerParent = false;
                replacementEntity.debrisPrefab = oldBag.debrisPrefab;
                replacementEntity.flags = oldBag.flags;
                replacementEntity.globalBuildingBlock = oldBag.globalBuildingBlock;
                replacementEntity.lifestate = oldBag.lifestate;
                replacementEntity.links = oldBag.links;
                replacementEntity.markAttackerHostile = oldBag.markAttackerHostile;
                replacementEntity.model = oldBag.model;
                replacementEntity.propDirection = oldBag.propDirection;
                replacementEntity.transform.localScale = respawnPointConfig.Scale;

                if (!string.IsNullOrEmpty(respawnPointConfig.DisplayName))
                {
                    replacementEntity.niceName = respawnPointConfig.DisplayName;
                }
                else
                {
                    replacementEntity.niceName = $"{parentEntity.ShortPrefabName} - {bagIdx}";
                }

                if (skinId > 0)
                {
                    replacementEntity.skinID = skinId;
                }

                if (!string.IsNullOrEmpty(networkPrefabOverride))
                {
                    replacementEntity.prefabID = StringPool.Get(networkPrefabOverride);
                }

                replacementEntity.Config = respawnSettings;

                replacementEntity.Spawn();

                return replacementEntity;
            }

            public static T CreateCustomDoor<T>(Vector3 location, Vector3 rotation, Vector3 scale, string prefab, BaseEntity parent, ulong skinId = 0, string networkPrefabOverride = "", bool detectCollisions = false, bool syncPosition = false) where T : SpecialDoor, new()
            {
                var newGO = GameManager.server.CreatePrefab(prefab, location, Quaternion.Euler(rotation));
                newGO.transform.localScale = scale;

                var baseEntity = newGO.GetComponent<BaseEntity>();

                var oldDoor = newGO.GetComponent<Door>();
                UnityEngine.Object.DestroyImmediate(baseEntity);

                var maxHealth = 500f;
                var iterateOver = newGO.GetComponentsInChildren<Component>().ToArray();
                var closedColliders = new List<GameObject>();
                for (var i = 0; i < iterateOver.Length; i++)
                {
                    var compo = iterateOver[i];

                    if (compo is Rigidbody)
                    {
                        continue;
                    }

                    if (compo is Transform)
                    {
                        continue;
                    }

                    if (compo is Model)
                    {
                        continue;
                    }

                    if (detectCollisions && compo is Collider col && !col.isTrigger)
                    {
                        compo.gameObject.layer = (int)Layer.Default;
                        col.excludeLayers = IGNORE_COL_MASK;

                        if (prefab == HAB_DOOR_PREFAB && col.name == HAB_HINGE_COLLIDER)
                        {
                            closedColliders.Add(col.gameObject);
                        }
                    }

                    UnityEngine.Object.DestroyImmediate(compo);
                }

                var replacementEntity = newGO.AddComponent<T>();

                replacementEntity.syncPosition = syncPosition;
                replacementEntity.prefabID = StringPool.Get(prefab);
                replacementEntity.pickup.enabled = false;
                replacementEntity.baseProtection = ScriptableObject.CreateInstance<ProtectionProperties>();
                replacementEntity.bounds = baseEntity.bounds;
                replacementEntity.impactEffect = baseEntity.impactEffect;
                replacementEntity.gameObject.layer = (int)Layer.Default;

                replacementEntity._maxHealth = maxHealth;
                replacementEntity.maxHealthOverride = maxHealth;
                replacementEntity.startHealth = maxHealth;
                replacementEntity.health = maxHealth;

                replacementEntity.sendsHitNotification = true;
                replacementEntity.EnableSaving(false);

                replacementEntity.SetParent(parent);
                replacementEntity.canHandOpen = oldDoor.canHandOpen;
                replacementEntity.canBeDemolished = oldDoor.canBeDemolished;
                replacementEntity.canNpcOpen = oldDoor.canNpcOpen;
                replacementEntity.canReverseOpen = oldDoor.canReverseOpen;
                replacementEntity.canTakeCloser = false;
                replacementEntity.canTakeKnocker = false;
                replacementEntity.canTakeLock = false;
                //replacementEntity.dismountSoundDef = oldBm.dismountSoundDef;
                //replacementEntity.mountSoundDef = oldBm.mountSoundDef;
                //replacementEntity.swapSoundDef = oldBm.swapSoundDef;
                replacementEntity.canTriggerParent = false;
                replacementEntity.checkPhysBoxesOnOpen = false;
                replacementEntity.debrisPrefab = oldDoor.debrisPrefab;
                replacementEntity.isSecurityDoor = oldDoor.isSecurityDoor;
                replacementEntity.flags = oldDoor.flags;
                replacementEntity.globalBuildingBlock = oldDoor.globalBuildingBlock;
                //replacementEntity.isMobile = oldBm.isMobile;
                replacementEntity.lifestate = oldDoor.lifestate;
                replacementEntity.links = oldDoor.links;
                replacementEntity.markAttackerHostile = oldDoor.markAttackerHostile;
                replacementEntity.model = oldDoor.model;
                replacementEntity.knockEffect = oldDoor.knockEffect;
                replacementEntity.NpcTriggerBox = oldDoor.NpcTriggerBox;
                replacementEntity.propDirection = oldDoor.propDirection;
                replacementEntity.ClosedColliderRoots = oldDoor.ClosedColliderRoots;
                replacementEntity.BusyColliderRoots = oldDoor.BusyColliderRoots;
                replacementEntity.networkEntityScale = true;

                if (replacementEntity.ClosedColliderRoots.Length <= 0 && closedColliders.Count > 0)
                {
                    replacementEntity.ClosedColliderRoots = closedColliders.ToArray();
                }

                if (skinId > 0)
                {
                    replacementEntity.skinID = skinId;
                }

                if (!string.IsNullOrEmpty(networkPrefabOverride))
                {
                    replacementEntity.prefabID = StringPool.Get(networkPrefabOverride);
                }

                replacementEntity.Spawn();

                return replacementEntity;
            }

            public static SpecialCodeLock CreateCustomCodelock(CodeLockConfig codeLockConfig, BaseEntity parent)
            {
                Vector3 location = codeLockConfig.Location;
                Vector3 rotation = codeLockConfig.Rotation;

                var newGO = GameManager.server.CreatePrefab(codeLockConfig.PrefabPath, location, Quaternion.Euler(rotation));
                var baseEntity = newGO.GetComponent<BaseEntity>();
                baseEntity.transform.localScale = codeLockConfig.Scale;

                var oldCode = newGO.GetComponent<CodeLock>();
                UnityEngine.Object.DestroyImmediate(baseEntity);

                var iterateOver = newGO.GetComponentsInChildren<Component>().ToArray();
                for (var i = 0; i < iterateOver.Length; i++)
                {
                    var compo = iterateOver[i];

                    if (compo is Rigidbody)
                    {
                        continue;
                    }

                    if (compo is Transform)
                    {
                        continue;
                    }

                    if (compo is Model)
                    {
                        continue;
                    }

                    UnityEngine.Object.DestroyImmediate(compo);
                }

                var rigidbody = newGO.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    UnityEngine.Object.DestroyImmediate(rigidbody);
                }

                var replacementEntity = newGO.AddComponent<SpecialCodeLock>();

                replacementEntity.prefabID = StringPool.Get(codeLockConfig.PrefabPath);
                replacementEntity.bounds = baseEntity.bounds;
                replacementEntity.impactEffect = baseEntity.impactEffect;
                replacementEntity.gameObject.layer = (int)Layer.Default;
                replacementEntity.parentBone = oldCode.parentBone;

                replacementEntity.EnableSaving(false);

                replacementEntity.SetParent(parent);
                replacementEntity.flags = oldCode.flags;
                //replacementEntity.isMobile = oldBm.isMobile;
                replacementEntity.links = oldCode.links;
                replacementEntity.model = oldCode.model;
                replacementEntity.CanRemove = false;
                replacementEntity.effectCodeChanged = oldCode.effectCodeChanged;
                replacementEntity.effectDenied = oldCode.effectDenied;
                replacementEntity.effectLocked = oldCode.effectLocked;
                replacementEntity.effectShock = oldCode.effectShock;
                replacementEntity.effectUnlocked = oldCode.effectUnlocked;
                replacementEntity.keyEnterDialog = oldCode.keyEnterDialog;
                replacementEntity.syncPosition = oldCode.syncPosition;
                replacementEntity.canTriggerParent = false;
                replacementEntity.code = oldCode.code;
                replacementEntity.creatorEntity = oldCode.creatorEntity;
                //replacementEntity.enabled = oldCode.enabled;
                replacementEntity.globalBuildingBlock = oldCode.globalBuildingBlock;
                replacementEntity.globalBroadcast = oldCode.globalBroadcast;
                replacementEntity.guestCode = oldCode.guestCode;
                replacementEntity.guestPlayers = oldCode.guestPlayers;
                replacementEntity.HasBrain = oldCode.HasBrain;
                replacementEntity.hasCode = oldCode.hasCode;
                replacementEntity.hasGuestCode = oldCode.hasGuestCode;
                replacementEntity.impactEffect = oldCode.impactEffect;
                replacementEntity.itemType = oldCode.itemType;
                replacementEntity.lastWrongTime = oldCode.lastWrongTime;
                replacementEntity.postNetworkUpdateComponents = oldCode.postNetworkUpdateComponents;
                replacementEntity.ticksSinceStopped = oldCode.ticksSinceStopped;
                replacementEntity.whitelistPlayers = oldCode.whitelistPlayers;
                replacementEntity._limitedNetworking = oldCode._limitedNetworking;
                replacementEntity.networkEntityScale = true;


                replacementEntity.Config = codeLockConfig;

                if (replacementEntity.Config.SpawnWithRandomCode)
                {
                    replacementEntity.hasCode = true;
                    replacementEntity.code = UnityEngine.Random.Range(1000, 9999).ToString();

                    replacementEntity.SetFlagLocal(Flags.Locked, true);
                }
                else
                {
                    replacementEntity.SetFlagLocal(Flags.Locked, false);
                }

                replacementEntity.Spawn();

                return replacementEntity;
            }

            public static SpecialBuildingBlock CreateSpecialBuildingBlock(PropConfig propConfig, BaseEntity parent)
            {
                var newGO = GameManager.server.CreatePrefab(propConfig.PrefabPath, propConfig.Location, Quaternion.Euler(propConfig.Rotation));
                var baseEntity = newGO.GetComponent<BaseEntity>();
                newGO.transform.localScale = propConfig.Scale;

                var oldBb = newGO.GetComponent<BuildingBlock>();
                UnityEngine.Object.DestroyImmediate(baseEntity);

                var iterateOver = newGO.GetComponentsInChildren<Component>().ToArray();
                for (var i = 0; i < iterateOver.Length; i++)
                {
                    var compo = iterateOver[i];

                    if (compo is Rigidbody)
                    {
                        continue;
                    }

                    if (compo is ConstructionSkin)
                    {
                        continue;
                    }

                    if (compo is Transform)
                    {
                        continue;
                    }

                    if (propConfig.DetectCollisions && compo is Collider col && !col.isTrigger)
                    {
                        compo.gameObject.layer = (int)Layer.Default;
                        col.excludeLayers = IGNORE_COL_MASK;
                        continue;
                    }

                    UnityEngine.Object.DestroyImmediate(compo);
                }

                var rigidbody = newGO.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    UnityEngine.Object.DestroyImmediate(rigidbody);
                }

                var replacementEntity = newGO.AddComponent<SpecialBuildingBlock>();

                replacementEntity.prefabID = StringPool.Get(propConfig.PrefabPath);
                replacementEntity.bounds = baseEntity.bounds;
                replacementEntity.impactEffect = baseEntity.impactEffect;
                replacementEntity.gameObject.layer = (int)Layer.Default;
                replacementEntity.parentBone = oldBb.parentBone;

                replacementEntity.EnableSaving(false);

                replacementEntity.SetParent(parent);
                replacementEntity.flags = oldBb.flags;
                //replacementEntity.isMobile = oldBm.isMobile;
                replacementEntity.links = oldBb.links;
                replacementEntity.model = oldBb.model;
                replacementEntity.syncPosition = false;
                replacementEntity.canTriggerParent = false;
                replacementEntity.creatorEntity = oldBb.creatorEntity;
                //replacementEntity.enabled = oldCode.enabled;
                replacementEntity.globalBuildingBlock = oldBb.globalBuildingBlock;
                replacementEntity.globalBroadcast = oldBb.globalBroadcast;
                replacementEntity.impactEffect = oldBb.impactEffect;
                replacementEntity.postNetworkUpdateComponents = oldBb.postNetworkUpdateComponents;
                replacementEntity.ticksSinceStopped = oldBb.ticksSinceStopped;
                replacementEntity.modelState = 0;

                replacementEntity.skinID = propConfig.SkinId;
                // Backward compatibility
                replacementEntity.skinID2 = propConfig.SkinId;
                replacementEntity.DetectCollisions = propConfig.DetectCollisions;

                if (propConfig.ModelState.HasValue)
                {
                    replacementEntity.modelState = propConfig.ModelState.Value;

                    if (propConfig.SkinId2 > 0)
                    {
                        replacementEntity.skinID2 = propConfig.SkinId2;
                    }
                }
                else if (propConfig.SkinId > 0)
                {
                    replacementEntity.modelState = 1;
                }
                else if (propConfig.PrefabPath == BOAT_WALL_PREFAB)
                {
                    replacementEntity.modelState = 4;
                }

                if (propConfig.PrefabPath == BOAT_WALL_PREFAB)
                {
                    replacementEntity.ForceColPrefab = BOAT_WALL_COLLIDER_PREFAB;
                    if (propConfig.SkinId > 0)
                    {
                        replacementEntity.IgnorePlayerCollisons = true;
                    }
                }
                else if (propConfig.PrefabPath == BOAT_WALL_LOW_BARRIER_PREFAB)
                {
                    replacementEntity.ForceColPrefab = BOAT_WALL_LOW_BARRIER_COLLIDER_PREFAB;
                }
                else if (propConfig.PrefabPath == BOAT_STAIRS_PREFAB)
                {
                    replacementEntity.ForceColPrefab = BOAT_STAIRS_COLLIDER_PREFAB;
                }


                replacementEntity.Spawn();

                return replacementEntity;
            }

            public static SpecialSail CreateSpecialSail(PropConfig propConfig, BaseEntity parent)
            {
                var newGO = GameManager.server.CreatePrefab(propConfig.PrefabPath, propConfig.Location, Quaternion.Euler(propConfig.Rotation));
                var baseEntity = newGO.GetComponent<BaseEntity>();
                newGO.transform.localScale = propConfig.Scale;

                var oldBb = newGO.GetComponent<Sail>();
                UnityEngine.Object.DestroyImmediate(baseEntity);

                var iterateOver = newGO.GetComponentsInChildren<Component>().ToArray();
                for (var i = 0; i < iterateOver.Length; i++)
                {
                    var compo = iterateOver[i];

                    if (compo is Rigidbody)
                    {
                        continue;
                    }

                    if (compo is ConstructionSkin)
                    {
                        continue;
                    }

                    if (compo is Transform)
                    {
                        continue;
                    }

                    if (propConfig.DetectCollisions && compo is Collider col && !col.isTrigger)
                    {
                        compo.gameObject.layer = (int)Layer.Default;
                        col.excludeLayers = IGNORE_COL_MASK;
                        continue;
                    }

                    UnityEngine.Object.DestroyImmediate(compo);
                }

                var rigidbody = newGO.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    UnityEngine.Object.DestroyImmediate(rigidbody);
                }

                var replacementEntity = newGO.AddComponent<SpecialSail>();

                replacementEntity.prefabID = StringPool.Get(propConfig.PrefabPath);
                replacementEntity.bounds = baseEntity.bounds;
                replacementEntity.impactEffect = baseEntity.impactEffect;
                replacementEntity.gameObject.layer = (int)Layer.Default;
                replacementEntity.parentBone = oldBb.parentBone;

                replacementEntity.EnableSaving(false);

                replacementEntity.SetParent(parent);
                replacementEntity.flags = oldBb.flags;
                //replacementEntity.isMobile = oldBm.isMobile;
                replacementEntity.links = oldBb.links;
                replacementEntity.model = oldBb.model;
                replacementEntity.syncPosition = false;
                replacementEntity.canTriggerParent = false;
                replacementEntity.creatorEntity = oldBb.creatorEntity;
                //replacementEntity.enabled = oldCode.enabled;
                replacementEntity.globalBuildingBlock = oldBb.globalBuildingBlock;
                replacementEntity.globalBroadcast = oldBb.globalBroadcast;
                replacementEntity.impactEffect = oldBb.impactEffect;
                replacementEntity.postNetworkUpdateComponents = oldBb.postNetworkUpdateComponents;
                replacementEntity.ticksSinceStopped = oldBb.ticksSinceStopped;
                replacementEntity.RaisedCollider = oldBb.RaisedCollider;
                replacementEntity.LoweredCollider = oldBb.LoweredCollider;
                replacementEntity.debrisPrefab = new GameObjectRef();

                replacementEntity.Spawn();

                return replacementEntity;
            }

            public static SpecialAnchor CreateSpecialAnchor(PropConfig propConfig, BaseEntity parent)
            {
                var newGO = GameManager.server.CreatePrefab(propConfig.PrefabPath, propConfig.Location, Quaternion.Euler(propConfig.Rotation));
                var baseEntity = newGO.GetComponent<BaseEntity>();
                newGO.transform.localScale = propConfig.Scale;

                var oldBb = newGO.GetComponent<Anchor>();
                UnityEngine.Object.DestroyImmediate(baseEntity);

                var iterateOver = newGO.GetComponentsInChildren<Component>().ToArray();
                for (var i = 0; i < iterateOver.Length; i++)
                {
                    var compo = iterateOver[i];

                    if (compo is Rigidbody)
                    {
                        continue;
                    }

                    if (compo is ConstructionSkin)
                    {
                        continue;
                    }

                    if (compo is Transform)
                    {
                        continue;
                    }

                    if (propConfig.DetectCollisions && compo is Collider col && !col.isTrigger)
                    {
                        compo.gameObject.layer = (int)Layer.Default;
                        col.excludeLayers = IGNORE_COL_MASK;
                        continue;
                    }

                    UnityEngine.Object.DestroyImmediate(compo);
                }

                var rigidbody = newGO.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    UnityEngine.Object.DestroyImmediate(rigidbody);
                }

                var replacementEntity = newGO.AddComponent<SpecialAnchor>();

                replacementEntity.prefabID = StringPool.Get(propConfig.PrefabPath);
                replacementEntity.bounds = baseEntity.bounds;
                replacementEntity.impactEffect = baseEntity.impactEffect;
                replacementEntity.gameObject.layer = (int)Layer.Default;
                replacementEntity.parentBone = oldBb.parentBone;

                replacementEntity.EnableSaving(false);

                replacementEntity.SetParent(parent);
                replacementEntity.flags = oldBb.flags;
                replacementEntity.links = oldBb.links;
                replacementEntity.model = oldBb.model;
                replacementEntity.syncPosition = false;
                replacementEntity.canTriggerParent = false;
                replacementEntity.creatorEntity = oldBb.creatorEntity;
                replacementEntity.globalBuildingBlock = oldBb.globalBuildingBlock;
                replacementEntity.globalBroadcast = oldBb.globalBroadcast;
                replacementEntity.impactEffect = oldBb.impactEffect;
                replacementEntity.postNetworkUpdateComponents = oldBb.postNetworkUpdateComponents;
                replacementEntity.ticksSinceStopped = oldBb.ticksSinceStopped;

                replacementEntity.Spawn();

                return replacementEntity;
            }

            public static Y CreateSpecialTurret<T, Y>(PropConfig propConfig, BaseEntity parent) where T : BaseVehicleSeat, new() where Y : BaseVehicleSeat, new()
            {
                var newGO = GameManager.server.CreatePrefab(propConfig.PrefabPath, propConfig.Location, Quaternion.Euler(propConfig.Rotation));
                var baseEntity = newGO.GetComponent<BaseEntity>();
                newGO.transform.localScale = propConfig.Scale;

                var oldBb = newGO.GetComponent<T>();
                UnityEngine.Object.DestroyImmediate(baseEntity);

                var playerServerColliderId = 0f;
                if (oldBb is Cannon c)
                {
                    playerServerColliderId = c.playerServerCollider.GetInstanceID();
                }

                var iterateOver = newGO.GetComponentsInChildren<Component>().ToArray();
                for (var i = 0; i < iterateOver.Length; i++)
                {
                    var compo = iterateOver[i];

                    if (compo is Rigidbody)
                    {
                        continue;
                    }

                    if (compo is ConstructionSkin)
                    {
                        continue;
                    }

                    if (compo is Transform)
                    {
                        continue;
                    }

                    var cid = compo.GetInstanceID();
                    if (cid == playerServerColliderId)
                    {
                        continue;
                    }

                    if (propConfig.DetectCollisions && compo is Collider col && !col.isTrigger)
                    {
                        compo.gameObject.layer = (int)Layer.Default;
                        col.excludeLayers = IGNORE_COL_MASK;
                        continue;
                    }

                    UnityEngine.Object.DestroyImmediate(compo);
                }

                var replacementEntity = newGO.AddComponent<Y>();

                CopySerializableFields<T, Y>(oldBb, replacementEntity);

                replacementEntity.syncPosition = false;
                //replacementEntity.syncsMountedPlayers = oldBb.syncsMountedPlayers;
                replacementEntity.prefabID = StringPool.Get(propConfig.PrefabPath);
                replacementEntity.pickup.enabled = false;
                replacementEntity.baseProtection = ScriptableObject.CreateInstance<ProtectionProperties>();
                //replacementEntity.bounds = baseEntity.bounds;
                replacementEntity.impactEffect = baseEntity.impactEffect;
                replacementEntity.gameObject.layer = (int)Layer.Default;

                replacementEntity._maxHealth = 100;
                replacementEntity.startHealth = 100;
                replacementEntity.health = 100;

                replacementEntity.sendsHitNotification = oldBb.sendsHitNotification;
                replacementEntity.EnableSaving(false);

                replacementEntity.SetParent(parent);
                replacementEntity.dismountPositions = oldBb.dismountPositions;
                replacementEntity.mountAnchor = oldBb.mountAnchor;
                replacementEntity.maxMountDistance = oldBb.maxMountDistance;
                replacementEntity.canTriggerParent = false;
                replacementEntity.canWieldItems = oldBb.canWieldItems;
                replacementEntity.canDrinkWhileMounted = oldBb.canDrinkWhileMounted;
                //replacementEntity.dismountSoundDef = oldBm.dismountSoundDef;
                //replacementEntity.mountSoundDef = oldBm.mountSoundDef;
                //replacementEntity.swapSoundDef = oldBm.swapSoundDef;
                replacementEntity.modifiesPlayerCollider = oldBb.modifiesPlayerCollider;
                replacementEntity.clippingAndVisChecks = oldBb.clippingAndVisChecks;
                replacementEntity.clippingChecksLocation = oldBb.clippingChecksLocation;
                replacementEntity.clippingCheckRadius = oldBb.clippingCheckRadius;
                replacementEntity.customPlayerCollider = oldBb.customPlayerCollider;
                replacementEntity.flags = oldBb.flags;
                replacementEntity.globalBuildingBlock = oldBb.globalBuildingBlock;
                //replacementEntity.isMobile = oldBm.isMobile;
                replacementEntity.lifestate = oldBb.lifestate;
                replacementEntity.links = oldBb.links;
                replacementEntity.markAttackerHostile = oldBb.markAttackerHostile;
                replacementEntity.model = oldBb.model;
                replacementEntity.MountedCameraMode = oldBb.MountedCameraMode;
                replacementEntity.pitchClamp = oldBb.pitchClamp;
                replacementEntity.propDirection = oldBb.propDirection;
                replacementEntity.relativeViewAngles = oldBb.relativeViewAngles;
                replacementEntity.yawClamp = oldBb.yawClamp;
                replacementEntity.eyeCenterOverride = oldBb.eyeCenterOverride;
                replacementEntity.eyePositionOverride = oldBb.eyePositionOverride;
                replacementEntity.allowedGestures = oldBb.allowedGestures;
                replacementEntity.checkPlayerLosOnMount = oldBb.checkPlayerLosOnMount;
                replacementEntity.disableMeshCullingForPlayers = oldBb.disableMeshCullingForPlayers;
                replacementEntity.sendsMeleeHitNotification = oldBb.sendsMeleeHitNotification;
                replacementEntity.networkEntityScale = true;

                // cannon
                //replacementEntity.useVehicleParentYaw = oldBb.useVehicleParentYaw;
                //replacementEntity.ammoPrefabs = oldBb.ammoPrefabs;
                //replacementEntity.isMountedOnVehicle = true;

                if (replacementEntity is SpecialCannon sc && oldBb is Cannon can)
                {
                    sc.playerServerCollider = can.playerServerCollider;
                    sc.pitchTransform = can.pitchTransform;
                    sc.yawTransform = can.yawTransform;
                    sc.mountTransform = can.mountTransform;
                    sc.runInLateUpdate = can.runInLateUpdate;
                    sc.useVehicleParentYaw = can.useVehicleParentYaw;
                    sc.aimDir = can.aimDir;
                    sc.ammoPrefabs = can.ammoPrefabs;
                    sc.runSideChecks = can.runSideChecks;
                    sc.leftGroundCheckTransform = can.leftGroundCheckTransform;
                    sc.rightGroundCheckTransform = can.rightGroundCheckTransform;
                    sc.leftSideCheckPositions = can.leftSideCheckPositions;
                    sc.rightSideCheckPositions = can.rightSideCheckPositions;
                    sc.originalLocalMountPos = can.originalLocalMountPos;
                    sc.runBoundsChecks = can.runBoundsChecks;
                    sc.areaChecks = can.areaChecks;
                    sc.ballistaOwner = can.ballistaOwner;
                    sc.reloadProgress = can.reloadProgress;
                    sc.verticalRatio = can.verticalRatio;
                    sc.lastReloadStartTime = can.lastReloadStartTime;
                    sc.magazine = can.magazine;
                    sc.turnSensivity = can.turnSensivity;
                    sc.muzzle = can.muzzle;
                    sc.reloadTime = can.reloadTime;


                    sc.FirePoint = can.FirePoint;
                    sc.reloadAimDirHeight = can.reloadAimDirHeight;
                    sc.fuseLightTime = can.fuseLightTime;
                    sc.middleGroundCheck = can.middleGroundCheck;
                    sc.mountedProtection = can.mountedProtection;

                    sc.alignRotationToParent = can.alignRotationToParent;
                    sc.reloadPreventsAiming = can.reloadPreventsAiming;
                    sc.mountLOSVertOffset = can.mountLOSVertOffset;
                    sc.AmmoPrefab = can.AmmoPrefab;


                    sc.syncAimDirOnFire = can.syncAimDirOnFire;
                    sc.syncAimDirOnReload = can.syncAimDirOnReload;
                }

                if (oldBb is BaseVehicleSeat vehicleSeat)
                {
                    replacementEntity.mountedAnimationSpeed = vehicleSeat.mountedAnimationSpeed;
                    replacementEntity.forcePlayerModelUpdate = vehicleSeat.forcePlayerModelUpdate;
                    replacementEntity.giveCrosshair = vehicleSeat.giveCrosshair;
                }

                if (!string.IsNullOrEmpty(propConfig.NetworkPrefabOverride))
                {
                    replacementEntity.prefabID = StringPool.Get(propConfig.NetworkPrefabOverride);
                }


                replacementEntity.Spawn();

                return replacementEntity;
            }

            public static SpecialDeployableBoomBox CreateSpecialBoombox(BoomboxSettings boomboxSettings, BaseEntity parent)
            {
                var newGO = GameManager.server.CreatePrefab(boomboxSettings.PrefabPath, boomboxSettings.Location, Quaternion.Euler(boomboxSettings.Rotation));
                var baseEntity = newGO.GetComponent<BaseEntity>();
                newGO.transform.localScale = boomboxSettings.Scale;

                var oldBb = newGO.GetComponent<DeployableBoomBox>();
                UnityEngine.Object.DestroyImmediate(baseEntity);

                var iterateOver = newGO.GetComponentsInChildren<Collider>().ToArray();
                for (var i = 0; i < iterateOver.Length; i++)
                {
                    var compo = iterateOver[i];
                    UnityEngine.Object.DestroyImmediate(compo);
                }

                var replacementEntity = newGO.AddComponent<SpecialDeployableBoomBox>();

                CopySerializableFields<DeployableBoomBox, SpecialDeployableBoomBox>(oldBb, replacementEntity);

                replacementEntity.syncPosition = false;
                replacementEntity.prefabID = StringPool.Get(boomboxSettings.PrefabPath);
                replacementEntity.pickup.enabled = false;
                replacementEntity.baseProtection = ScriptableObject.CreateInstance<ProtectionProperties>();
                replacementEntity.impactEffect = baseEntity.impactEffect;
                replacementEntity.gameObject.layer = (int)Layer.Default;
                replacementEntity.EnableSaving(false);

                replacementEntity.SetParent(parent);
                replacementEntity.networkEntityScale = true;

                replacementEntity.Spawn();

                return replacementEntity;
            }

            public static SpecialLadder CreateSpecialLadder(PropConfig propConfig, BaseEntity parent)
            {
                var newGO = GameManager.server.CreatePrefab(propConfig.PrefabPath, propConfig.Location, Quaternion.Euler(propConfig.Rotation));
                var baseEntity = newGO.GetComponent<BaseEntity>();
                newGO.transform.localScale = propConfig.Scale;

                var oldBb = newGO.GetComponent<BaseLadder>();
                UnityEngine.Object.DestroyImmediate(baseEntity);

                var iterateOver = newGO.GetComponentsInChildren<Component>().ToArray();
                for (var i = 0; i < iterateOver.Length; i++)
                {
                    var compo = iterateOver[i];

                    if (compo is Rigidbody)
                    {
                        continue;
                    }

                    if (compo is ConstructionSkin)
                    {
                        continue;
                    }

                    if (compo is Transform)
                    {
                        continue;
                    }

                    if (compo is TriggerBase)
                    {
                        continue;
                    }

                    if (compo is Collider col)
                    {
                        if (col.isTrigger)
                        {
                            continue;
                        }

                        if (propConfig.DetectCollisions)
                        {
                            compo.gameObject.layer = (int)Layer.Default;
                            col.excludeLayers = IGNORE_COL_MASK;
                            continue;
                        }
                        ;
                    }


                    UnityEngine.Object.DestroyImmediate(compo);
                }

                var replacementEntity = newGO.AddComponent<SpecialLadder>();

                CopySerializableFields<BaseLadder, SpecialLadder>(oldBb, replacementEntity);

                replacementEntity.syncPosition = false;
                replacementEntity.prefabID = StringPool.Get(propConfig.PrefabPath);
                replacementEntity.pickup.enabled = false;
                replacementEntity.baseProtection = ScriptableObject.CreateInstance<ProtectionProperties>();
                replacementEntity.impactEffect = baseEntity.impactEffect;
                replacementEntity.gameObject.layer = (int)Layer.Default;

                replacementEntity._maxHealth = 100;
                replacementEntity.startHealth = 100;
                replacementEntity.health = 100;

                replacementEntity.sendsHitNotification = oldBb.sendsHitNotification;
                replacementEntity.EnableSaving(false);

                replacementEntity.SetParent(parent);
                replacementEntity.canTriggerParent = false;
                replacementEntity.networkEntityScale = true;

                if (!string.IsNullOrEmpty(propConfig.NetworkPrefabOverride))
                {
                    replacementEntity.prefabID = StringPool.Get(propConfig.NetworkPrefabOverride);
                }

                replacementEntity.Spawn();

                return replacementEntity;
            }

            public static void CopySerializableFields<T, Y>(T src, Y dst)
            {
                var type = typeof(T);
                if (!Instance.CachedFields.TryGetValue(type, out FieldInfo[] srcFields))
                {
#if CARBON
                    srcFields = Instance.CachedFields[type] = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
#else
                    srcFields = Instance.CachedFields[type] = GetFieldInfosIncludingBaseClasses(type, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
#endif
                }

                foreach (var field in srcFields)
                {
                    var value = field.GetValue(src);
                    field.SetValue(dst, value);
                }
            }

            public static FieldInfo[] GetFieldInfosIncludingBaseClasses(Type type, BindingFlags bindingFlags)
            {
                FieldInfo[] fieldInfos = type.GetFields(bindingFlags);

                if (type.BaseType == typeof(BaseNetworkable))
                {
                    return fieldInfos;
                }

                var currentType = type;
                var fieldComparer = new FieldInfoComparer();
                var fieldInfoList = new HashSet<FieldInfo>(fieldInfos, fieldComparer);

                while (currentType != typeof(BaseNetworkable))
                {
                    fieldInfos = currentType.GetFields(bindingFlags);
                    fieldInfoList.UnionWith(fieldInfos);
                    currentType = currentType.BaseType;
                }

                return fieldInfoList.ToArray();
            }

            private class FieldInfoComparer : IEqualityComparer<FieldInfo>
            {
                public bool Equals(FieldInfo x, FieldInfo y)
                {
                    return x.DeclaringType == y.DeclaringType && x.Name == y.Name;
                }

                public int GetHashCode(FieldInfo obj)
                {
                    return obj.Name.GetHashCode() ^ obj.DeclaringType.GetHashCode();
                }
            }

            public static SpecialComputerStation CreateCustomComputerStation(Vector3 location, Vector3 rotation, Vector3 scale, string prefab, BaseEntity parent, ulong skinId = 0, string networkPrefabOverride = "")
            {
                return CreateCustomBaseMountable<SpecialComputerStation>(location, rotation, scale, prefab, parent, skinId, networkPrefabOverride);
            }

            public static T CreateCustomBaseMountable<T>(Vector3 location, Vector3 rotation, Vector3 scale, string prefab, BaseEntity parent, ulong skinId = 0, string networkPrefabOverride = "", bool detectCollisions = false, bool syncPosition = false, bool spawn = true) where T : BaseVehicleSeat, new()
            {
                var newGO = GameManager.server.CreatePrefab(prefab, location, Quaternion.Euler(rotation));
                newGO.transform.localScale = scale;

                var baseEntity = newGO.GetComponent<BaseEntity>();

                var maxHealth = 500;
                var oldBm = newGO.GetComponent<BaseMountable>();
                UnityEngine.Object.DestroyImmediate(baseEntity);

                var iterateOver = newGO.GetComponentsInChildren<Component>().ToArray();
                for (var i = 0; i < iterateOver.Length; i++)
                {
                    var compo = iterateOver[i];

                    if (compo is Rigidbody)
                    {
                        continue;
                    }

                    if (compo is Transform)
                    {
                        continue;
                    }

                    if (detectCollisions && compo is Collider col && !col.isTrigger)
                    {
                        if (col is MeshCollider mc)
                        {
                            mc.convex = true;
                        }

                        compo.gameObject.layer = (int)Layer.Default;
                        col.excludeLayers = IGNORE_COL_MASK;
                        continue;
                    }

                    if (compo is Model)
                    {
                        continue;
                    }

                    UnityEngine.Object.DestroyImmediate(compo);
                }

                var rigidbody = newGO.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    UnityEngine.Object.DestroyImmediate(rigidbody);
                }

                var replacementEntity = newGO.AddComponent<T>();

                replacementEntity.syncPosition = syncPosition;
                replacementEntity.prefabID = StringPool.Get(prefab);
                replacementEntity.pickup.enabled = false;
                replacementEntity.baseProtection = ScriptableObject.CreateInstance<ProtectionProperties>();
                replacementEntity.bounds = baseEntity.bounds;
                replacementEntity.impactEffect = baseEntity.impactEffect;
                replacementEntity.gameObject.layer = (int)Layer.Default;

                replacementEntity._maxHealth = maxHealth;
                replacementEntity.startHealth = maxHealth;
                replacementEntity.health = maxHealth;

                replacementEntity.sendsHitNotification = oldBm.sendsHitNotification;
                replacementEntity.EnableSaving(false);

                replacementEntity.SetParent(parent);
                replacementEntity.dismountPositions = oldBm.dismountPositions;
                replacementEntity.mountAnchor = oldBm.mountAnchor;
                replacementEntity.maxMountDistance = oldBm.maxMountDistance;
                replacementEntity.canTriggerParent = false;
                replacementEntity.canWieldItems = oldBm.canWieldItems;
                replacementEntity.canDrinkWhileMounted = oldBm.canDrinkWhileMounted;
                //replacementEntity.dismountSoundDef = oldBm.dismountSoundDef;
                //replacementEntity.mountSoundDef = oldBm.mountSoundDef;
                //replacementEntity.swapSoundDef = oldBm.swapSoundDef;
                replacementEntity.modifiesPlayerCollider = oldBm.modifiesPlayerCollider;
                replacementEntity.clippingAndVisChecks = oldBm.clippingAndVisChecks;
                replacementEntity.clippingChecksLocation = oldBm.clippingChecksLocation;
                replacementEntity.clippingCheckRadius = oldBm.clippingCheckRadius;
                replacementEntity.customPlayerCollider = oldBm.customPlayerCollider;
                replacementEntity.flags = oldBm.flags;
                replacementEntity.globalBuildingBlock = oldBm.globalBuildingBlock;
                //replacementEntity.isMobile = oldBm.isMobile;
                replacementEntity.lifestate = oldBm.lifestate;
                replacementEntity.links = oldBm.links;
                replacementEntity.markAttackerHostile = oldBm.markAttackerHostile;
                replacementEntity.model = oldBm.model;
                replacementEntity.MountedCameraMode = oldBm.MountedCameraMode;
                replacementEntity.pitchClamp = oldBm.pitchClamp;
                replacementEntity.propDirection = oldBm.propDirection;
                replacementEntity.relativeViewAngles = oldBm.relativeViewAngles;
                replacementEntity.yawClamp = oldBm.yawClamp;
                replacementEntity.eyeCenterOverride = oldBm.eyeCenterOverride;
                replacementEntity.eyePositionOverride = oldBm.eyePositionOverride;
                replacementEntity.allowedGestures = oldBm.allowedGestures;
                replacementEntity.checkPlayerLosOnMount = oldBm.checkPlayerLosOnMount;
                replacementEntity.disableMeshCullingForPlayers = oldBm.disableMeshCullingForPlayers;
                replacementEntity.sendsMeleeHitNotification = oldBm.sendsMeleeHitNotification;
                replacementEntity.networkEntityScale = true;

                if (oldBm is BaseVehicleSeat vehicleSeat)
                {
                    replacementEntity.mountedAnimationSpeed = vehicleSeat.mountedAnimationSpeed;
                    replacementEntity.forcePlayerModelUpdate = vehicleSeat.forcePlayerModelUpdate;
                    replacementEntity.giveCrosshair = vehicleSeat.giveCrosshair;
                }

                if (skinId > 0)
                {
                    replacementEntity.skinID = skinId;
                }

                if (!string.IsNullOrEmpty(networkPrefabOverride))
                {
                    replacementEntity.prefabID = StringPool.Get(networkPrefabOverride);
                }

                if (replacementEntity is SpecialStaticInstrument ins)
                {
                    ins.KeyController = (oldBm as StaticInstrument).KeyController;
                }

                if (spawn)
                {
                    replacementEntity.Spawn();
                }

                return replacementEntity;
            }

            public static BaseEntity CreateEntityProp(Vector3 location, Vector3 rotation, Vector3 scale, string prefab, BaseEntity parent, ulong skinId, string networkPrefabOverride, bool spawn = true, bool syncPosition = false)
            {
                var ent = GameManager.server.CreateEntity(prefab, location, Quaternion.Euler(rotation));
                var rigidbody = ent.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    UnityEngine.Object.Destroy(rigidbody);
                }

                ent.EnableSaving(false);
                ent.syncPosition = syncPosition;
                ent.transform.localScale = scale;
                if (scale != Vector3.one)
                {
                    ent.networkEntityScale = true;
                }

                if (!string.IsNullOrEmpty(networkPrefabOverride))
                {
                    ent.prefabID = StringPool.Get(networkPrefabOverride);
                }

                ent.SetParent(parent);

                if (skinId > 0)
                {
                    ent.skinID = skinId;
                }

                if (spawn)
                {
                    ent.Spawn();
                }

                using (FlagsUpdateScope flagsUpdateScope = ent.StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.Locked, true, true);

                    if (ent is IOEntity io)
                    {
                        io.enabled = false;
                        flagsUpdateScope.Set(Flags.Reserved8, true, true);
                    }
                    else if (ent is BaseCardGameEntity cge)
                    {
                        ent.enabled = true;
                        flagsUpdateScope.Set(Flags.Locked, false, true);
                        cge.legacyDismount = true;
                    }
                    else if (ent is SlotMachine sm)
                    {
                        ent.enabled = true;
                        flagsUpdateScope.Set(Flags.Locked, false, true);
                        sm.legacyDismount = true;
                        sm.clippingAndVisChecks = true;

                        List<Transform> newDismountPoints = new List<Transform>();
                        var vehicle = parent.GetComponentInParent<BaseVehicle>();

                        for (int i = 0; i < vehicle.dismountPositions.Length; i++)
                        {
                            newDismountPoints.Add(vehicle.dismountPositions[i]);
                        }

                        newDismountPoints.AddRange(sm.dismountPositions
                            .Where(gdmp => !newDismountPoints.Exists(dmp => dmp.transform.position == gdmp.transform.position)));

                        var newDm = newDismountPoints.ToArray();
                        vehicle.dismountPositions = newDm;
                    }
                    else if (ent is ModularCarRadio carRadio)
                    {
                        flagsUpdateScope.Set(Flags.Busy, false);
                    }
                }

                return ent;
            }

            public static SpecialBaseCombatEntity CreateCustomEntity(Vector3 location, Vector3 rotation, Vector3 scale, string prefab, BaseEntity parent, ulong skinId = 0, string networkPrefabOverride = "", bool detectCollisions = false, bool syncPosition = false)
            {
                return CreateCustomEntity<SpecialBaseCombatEntity>(location, rotation, scale, prefab, parent, skinId, networkPrefabOverride, detectCollisions, syncPosition);
            }

            public static T CreateCustomEntity<T>(Vector3 location, Vector3 rotation, Vector3 scale, string prefab, BaseEntity parent, ulong skinId = 0, string networkPrefabOverride = "", bool detectCollisions = false, bool syncPosition = false, bool spawn = true) where T : SpecialBaseCombatEntity, new()
            {
                var ent = SpecialBaseCombatEntity.CreateSpecialEntity<T>(location, rotation, scale, prefab, detectCollisions, syncPosition);
                ent.SetParent(parent);

                if (skinId > 0)
                {
                    ent.skinID = skinId;
                }

                if (!string.IsNullOrEmpty(networkPrefabOverride))
                {
                    ent.prefabID = StringPool.Get(networkPrefabOverride);
                }

                if (spawn)
                {
                    ent.Spawn();
                }

                if (detectCollisions)
                {
                    ent.gameObject.layer = (int)Layer.Default;
                    BoxCollider bc = null;
                    if (ent is Door door)
                    {
                        //bc = ent.gameObject.AddComponent<BoxCollider>();
                        //bc.center = new Vector3(0, 1.166f, 0);
                        //bc.size = new Vector3(0.2f, 2.11f, 1.21f);
                        foreach (GameObject gameObject in door.ClosedColliderRoots)
                        {
                            gameObject.gameObject.SetActive(true);
                        }
                    }
                    else if (prefab == HOT_AIR_BALOON_ARMOR_PREFAB)
                    {
                        var iterateOver = ent.GetComponentsInChildren<MeshCollider>();
                        for (var i = 0; i < iterateOver.Length; i++)
                        {
                            var compo = iterateOver[i];
                            if (compo == null)
                            {
                                continue;
                            }

                            if (compo.name == HAB_HINGE_COLLIDER)
                            {
                                GameObject.DestroyImmediate(compo.gameObject);
                                break;
                            }
                        }
                    }
                    // Required
                    else if (prefab.Contains("loot_barrel") || prefab.Contains("loot-barrel") || prefab.Contains("oil_barrel"))
                    {
                        bc = ent.gameObject.AddComponent<BoxCollider>();
                        bc.center = new Vector3(0, 0.573f, 0);
                        bc.size = new Vector3(0.75f, 1.150f, 0.750f);
                    }
                    else if (prefab == BUTTON_PREFAB || prefab == BUTTON_COMPACT_PREFAB || prefab == BUTTON_TRAIN_STAIRS_PREFAB || prefab == BUTTON_INVIS_PREFAB)
                    {
                        bc = ent.gameObject.AddComponent<BoxCollider>();
                        bc.center = Vector3.zero;
                        bc.size = new Vector3(0.5f, 0.5f, 0.5f);
                    }
                    else if (prefab.Contains("4module"))
                    {
                        var iterateOver = ent.gameObject.GetComponentsInChildren<Collider>().ToArray();
                        for (var i = 0; i < iterateOver.Length; i++)
                        {
                            var compo = iterateOver[i];
                            UnityEngine.Object.DestroyImmediate(compo);
                        }

                        bc = ent.gameObject.AddComponent<BoxCollider>();
                        bc.center = Vector3.zero;
                        bc.size = new Vector3(1f, 1.5f, 6.1f);
                    }
                    else if (prefab.Contains("3module"))
                    {
                        var iterateOver = ent.gameObject.GetComponentsInChildren<Collider>().ToArray();
                        for (var i = 0; i < iterateOver.Length; i++)
                        {
                            var compo = iterateOver[i];
                            UnityEngine.Object.DestroyImmediate(compo);
                        }

                        bc = ent.gameObject.AddComponent<BoxCollider>();
                        bc.center = Vector3.zero;
                        bc.size = new Vector3(1f, 1.5f, 4.6f);
                    }
                    else if (prefab.Contains("2module"))
                    {
                        var iterateOver = ent.gameObject.GetComponentsInChildren<Collider>().ToArray();
                        for (var i = 0; i < iterateOver.Length; i++)
                        {
                            var compo = iterateOver[i];
                            UnityEngine.Object.DestroyImmediate(compo);
                        }

                        bc = ent.gameObject.AddComponent<BoxCollider>();
                        bc.center = Vector3.zero;
                        bc.size = new Vector3(1, 1.5f, 3.1f);
                    }
                    else if (prefab.Contains("module_entities"))
                    {
                        bc = ent.gameObject.AddComponent<BoxCollider>();
                        bc.center = new Vector3(0, 0.549f, 0);
                        bc.size = new Vector3(2.00f, 1.8f, 2.00f);
                    }
                    else if (prefab == MINI_GUN_PREFAB || prefab == M249_GUN_PREFAB || prefab == HMLMG_GUN_PREFAB)
                    {
                        bc = ent.gameObject.AddComponent<BoxCollider>();
                        bc.center = new Vector3(0, 0f, 0.170f);
                        bc.size = new Vector3(0.50f, 0.200f, 1.00f);
                    }
                    else if (prefab == PREFAB_COUNTER)
                    {
                        bc = ent.gameObject.AddComponent<BoxCollider>();
                        bc.center = new Vector3(0, 0.092f, 0.04f);
                        bc.size = new Vector3(0.190f, 0.190f, 0.090f);
                    }
                    else if (prefab == PTZ_PREFAB)
                    {
                        bc = ent.gameObject.AddComponent<BoxCollider>();
                        bc.center = new Vector3(0, -0.191f, 0.0f);
                        bc.size = new Vector3(0.3f, 0.4f, 0.3f);
                    }
                    else if (prefab.Contains("wall.window"))
                    {
                        bc = ent.gameObject.AddComponent<BoxCollider>();
                        bc.center = new Vector3(0.006f, 0.652f, 0.0f);
                        bc.size = new Vector3(0.1f, 1.230f, 2.250f);
                    }
                    else if (prefab == LONGSWORD_PREFAB)
                    {
                        bc = ent.gameObject.AddComponent<BoxCollider>();
                        bc.center = new Vector3(-0.012f, 0.0f, -0.326f);
                        bc.size = new Vector3(0.2f, 0.10f, 0.9f);
                    }
                    else if (prefab.Contains("weaponracklight"))
                    {
                        bc = ent.gameObject.AddComponent<BoxCollider>();
                        bc.center = new Vector3(0, 0.042f, 0.126f);
                        bc.size = new Vector3(0.6f, 0.1f, 0.1f);
                    }
                    else if (prefab == SALVAGE_CLEAVER_PREFAB)
                    {
                        bc = ent.gameObject.AddComponent<BoxCollider>();
                        bc.center = new Vector3(-0.065f, 0.021f, -0.287f);
                        bc.size = new Vector3(0.250f, 0.050f, 0.800f);
                    }
                    else if (prefab.Contains("rocket_launcher"))
                    {
                        bc = ent.gameObject.AddComponent<BoxCollider>();
                        bc.center = new Vector3(0.100f, 0.138f, -0.167f);
                        bc.size = new Vector3(0.400f, 0.8f, 0.4f);
                    }
                    else if (prefab.Contains("kayak"))
                    {
                        bc = ent.gameObject.AddComponent<BoxCollider>();
                        bc.center = new Vector3(0.0f, 0.129f, 0.0f);
                        bc.size = new Vector3(1f, 0.3f, 3.90f);
                    }
                    else if (prefab.Contains("volcano"))
                    {
                        bc = ent.gameObject.AddComponent<BoxCollider>();
                        bc.center = new Vector3(0.0f, 0.174f, 0.0f);
                        bc.size = new Vector3(0.3f, 0.4f, 0.3f);
                    }
                    else if (prefab == MOTOR_BIKE_PREFAB)
                    {
                        bc = ent.gameObject.AddComponent<BoxCollider>();
                        bc.center = new Vector3(0.0f, 0.701f, -0.046f);
                        bc.size = new Vector3(0.4f, 1.0f, 2.1f);
                    }
                    else if (prefab == XMAS_LIGHTS_PREFAB)
                    {
                        bc = ent.gameObject.AddComponent<BoxCollider>();
                        bc.center = new Vector3(0.0f, 0.051f, 0.0f);
                        bc.size = new Vector3(3.000f, 0.100f, 0.100f);
                    }
                    else if (prefab == FRDIGE_PREFAB)
                    {
                        bc = ent.gameObject.AddComponent<BoxCollider>();
                        bc.center = new Vector3(0.0f, 0.982f, 0.0f);
                        bc.size = new Vector3(1.000f, 2.00f, 1.000f);
                    }
                    else if (prefab.Contains("spear"))
                    {
                        bc = ent.gameObject.AddComponent<BoxCollider>();
                        bc.center = new Vector3(0.0f, 0.119f, 0.516f);
                        bc.size = new Vector3(0.100f, 0.30f, 1.800f);
                    }
                    else if (prefab.Contains("baseballbat"))
                    {
                        bc = ent.gameObject.AddComponent<BoxCollider>();
                        bc.center = new Vector3(0.0f, 0.028f, 0.0f);
                        bc.size = new Vector3(0.1f, 0.70f, 0.1f);
                    }
                    else if (prefab == COFFIN_PREFAB)
                    {
                        bc = ent.gameObject.AddComponent<BoxCollider>();
                        bc.center = new Vector3(0.0f, 0.273f, 0.0f);
                        bc.size = new Vector3(2.4f, 0.7f, 0.920f);
                    }
                    else if (prefab == WHEEL_SWITCH_PREFAB)
                    {
                        bc = ent.gameObject.AddComponent<BoxCollider>();
                        bc.center = new Vector3(0.0f, 0.905f, 0.561f);
                        bc.size = new Vector3(0.4f, 0.4f, 0.2f);
                    }
                    else if (prefab == SALVAGE_SWORD_PREFAB)
                    {
                        bc = ent.gameObject.AddComponent<BoxCollider>();
                        bc.center = new Vector3(0.1f, 0.0f, -0.184f);
                        bc.size = new Vector3(0.2f, 0.1f, 0.7f);
                    }

                    if (bc == null)
                    {
                        if (ent.GetComponents<Collider>().Length <= 0)
                        {
                            //Instance.PrintWarning($"Prefab: {prefab} has collisions enabled but no existing collider");
                        }
                    }
                    else
                    {
                        bc.enabled = true;
                        bc.isTrigger = false;
                        bc.excludeLayers = IGNORE_COL_MASK;
                    }
                }

                return ent;
            }

            public static BaseEntity CreateSpecialTravellingVendor(Vector3 location, Vector3 rotation, BaseEntity parent)
            {
                var ent = GameManager.server.CreateEntity(PREFAB_SPECIALTRAVELLINGVENDOR_FULLPATH, location, Quaternion.Euler(rotation)) as SpecialTravellingVendor;
                ent.prefabID = StringPool.Get(TRAVELLING_VENDOR_PREFAB);
                ent.enabled = true;
                ent.gameObject.layer = (int)Layer.Default;
                ent.SetParent(parent);
                ent.Spawn();
                return ent;
            }

            public static BaseEntity CreateGib(Vector3 location, Vector3 rotation, Vector3 scale, string prefab, string gibName, BaseEntity parent, bool detectCollisions = false, bool spawn = true)
            {
                var ent = GameManager.server.CreateEntity(PREFAB_SPECIALGIB_FULLPATH, location, Quaternion.Euler(rotation)) as SpecialGib;
                ent.GibName = gibName;
                ent.DetectCollisions = detectCollisions;
                ent.prefabID = StringPool.Get(prefab);
                ent.gameObject.layer = (int)Layer.Default;
                ent.SetParent(parent);
                ent._maxHealth = 500;
                ent.startHealth = 500;
                ent.health = 500;
                ent.transform.localScale = scale;

                if (spawn)
                {
                    ent.Spawn();
                }

                return ent;
            }

            public static SpecialWorldItem CreateWorldItem(Vector3 location, Vector3 rotation, string prefab, int itemId, BaseEntity parent, ulong skinId, bool detectCollisions, bool syncPosition, bool spawn = true)
            {
                var ent = GameManager.server.CreateEntity(PREFAB_SPECIALWORLDITEM_FULLPATH, location, Quaternion.Euler(rotation)) as SpecialWorldItem;
                ent.ItemId = itemId;
                ent.enabled = false;
                ent.syncPosition = syncPosition;
                ent.prefabID = StringPool.Get(prefab);
                ent.gameObject.layer = (int)Layer.Default;
                ent.SetParent(parent);
                ent.pickup.enabled = false;

                if (skinId > 0)
                {
                    ent.skinID = skinId;
                }

                if (spawn)
                {
                    ent.Spawn();
                }

                if (detectCollisions)
                {
                    Collider c = null;
                    if (itemId == SHEETL_METAL_ITEMID)
                    {
                        c = ent.gameObject.AddComponent<BoxCollider>();
                        var bc = c as BoxCollider;
                        bc.center = Vector3.zero;
                        bc.size = new Vector3(0.710f, 0.100f, 1.100f);
                    }
                    else if (itemId == PROPANE_ITEMID)
                    {
                        c = ent.gameObject.AddComponent<SphereCollider>();
                        var sc = c as SphereCollider;
                        sc.radius = 0.125f;
                    }
                    else if (itemId == SYRINGE_ITEMID)
                    {
                        c = ent.gameObject.AddComponent<BoxCollider>();
                        var bc = c as BoxCollider;
                        bc.center = Vector3.zero;
                        bc.size = new Vector3(0.4f, 0.200f, 0.4f);
                    }
                    else if (itemId == SPRING_ITEMID)
                    {
                        c = ent.gameObject.AddComponent<BoxCollider>();
                        var bc = c as BoxCollider;
                        bc.center = new Vector3(0, 0.093f, 0);
                        bc.size = new Vector3(0.15f, 0.18f, 0.15f);
                    }
                    else if (itemId == GEAR_ITEMID)
                    {
                        c = ent.gameObject.AddComponent<BoxCollider>();
                        var bc = c as BoxCollider;
                        bc.center = new Vector3(0, 0.093f, 0);
                        bc.size = new Vector3(0.25f, 0.2f, 0.25f);
                    }
                    else if (itemId == CASSETTE_LONG_ITEMID || itemId == CASSETTE_MEDIUM_ITEMID || itemId == CASSETTE_SHORT_ITEMID)
                    {
                        c = ent.gameObject.AddComponent<BoxCollider>();
                        var bc = c as BoxCollider;
                        bc.center = new Vector3(0, 0.0f, 0);
                        bc.size = new Vector3(0.140f, 0.020f, 0.100f);
                    }
                    else if (itemId == MLRS_ITEMID)
                    {
                        c = ent.gameObject.AddComponent<BoxCollider>();
                        var bc = c as BoxCollider;
                        bc.center = new Vector3(0, 0.0f, 0);
                        bc.size = new Vector3(0.2f, 0.2f, 1.9f);
                    }
                    else if (itemId == MLRS_ITEMID)
                    {
                        c = ent.gameObject.AddComponent<BoxCollider>();
                        var bc = c as BoxCollider;
                        bc.center = new Vector3(0, 0.0f, 0);
                        bc.size = new Vector3(0.2f, 0.2f, 1.9f);
                    }
                    else if (itemId == ROCKET_HV_ITEMID)
                    {
                        c = ent.gameObject.AddComponent<BoxCollider>();
                        var bc = c as BoxCollider;
                        bc.center = new Vector3(0, 0.053f, 0.065f);
                        bc.size = new Vector3(0.100f, 0.100f, 0.650f);
                    }
                    else if (itemId == BLADE_ITEMID)
                    {
                        c = ent.gameObject.AddComponent<BoxCollider>();
                        var bc = c as BoxCollider;
                        bc.center = new Vector3(0, 0.007f, 0.0f);
                        bc.size = new Vector3(0.6f, 0.020f, 0.230f);
                    }
                    else if (itemId == WEAPON_FLASHLIGHT_ITEMID)
                    {
                        c = ent.gameObject.AddComponent<BoxCollider>();
                        var bc = c as BoxCollider;
                        bc.center = new Vector3(0, 0.0f, 0.0f);
                        bc.size = new Vector3(0.1f, 0.1f, 0.2f);
                    }

                    if (c != null)
                    {
                        c.enabled = true;
                        c.isTrigger = false;
                        c.excludeLayers = IGNORE_COL_MASK;
                        c.contactOffset += 0.3f;
                    }
                    else
                    {
                        if (ent.GetComponents<Collider>().Length <= 0)
                        {
                            //Instance.PrintWarning($"World Item: {itemId} has collisions enabled but no existing collider");
                        }
                    }
                }

                return ent;
            }

            public static SpecialTowWorldItem CreateTowWorldItem(Vector3 location, Vector3 rotation, string prefab, int itemId, BaseEntity parent, ulong skinId, bool spawn = true)
            {
                var ent = GameManager.server.CreateEntity(PREFAB_SPECIALTOWWORLDITEM_FULLPATH, location, Quaternion.Euler(rotation)) as SpecialTowWorldItem;
                ent.ItemId = itemId;
                ent.syncPosition = true;
                ent.prefabID = StringPool.Get(prefab);
                ent.gameObject.layer = (int)Layer.Default;
                ent.SetParent(parent);
                ent.pickup.enabled = false;

                if (skinId > 0)
                {
                    ent.skinID = skinId;
                }

                if (spawn)
                {
                    ent.Spawn();
                }

                return ent;
            }

            public static T CreateLiquidContainer<T>(BaseEntity parentEntity, ContainerConfiguration storageContainerConfiguration, bool spawn = true) where T : SpecialLiquidContainer
            {

                var newGO = GameManager.server.CreatePrefab(storageContainerConfiguration.PrefabPath, storageContainerConfiguration.Location, Quaternion.Euler(storageContainerConfiguration.Rotation), false);
                newGO.transform.localScale = storageContainerConfiguration.Scale;

                var baseEntity = newGO.GetComponent<BaseEntity>();
                var oldCompo = newGO.GetComponent<LiquidContainer>();

                UnityEngine.Object.DestroyImmediate(baseEntity);

                var iterateOver = newGO.GetComponentsInChildren<Component>().ToArray();
                for (var i = 0; i < iterateOver.Length; i++)
                {
                    var compo = iterateOver[i];

                    if (compo is Rigidbody)
                    {
                        continue;
                    }

                    if (compo is Transform)
                    {
                        continue;
                    }

                    if (storageContainerConfiguration.DetectCollisions && compo is Collider col && !col.isTrigger)
                    {
                        compo.gameObject.layer = (int)Layer.Default;
                        continue;
                    }

                    UnityEngine.Object.DestroyImmediate(compo);
                }

                var rigidbody = newGO.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    UnityEngine.Object.DestroyImmediate(rigidbody);
                }

                var replacementEntity = newGO.AddComponent<T>();

                if (!string.IsNullOrEmpty(storageContainerConfiguration.NetworkPrefabOverride))
                {
                    replacementEntity.prefabID = StringPool.Get(storageContainerConfiguration.NetworkPrefabOverride);
                }
                else
                {
                    replacementEntity.prefabID = StringPool.Get(storageContainerConfiguration.PrefabPath);
                }

                if (storageContainerConfiguration.DetectCollisions)
                {
                    BoxCollider bc = null;
                    var prefabName = storageContainerConfiguration.PrefabPath;
                    if (!string.IsNullOrEmpty(storageContainerConfiguration.NetworkPrefabOverride))
                    {
                        prefabName = storageContainerConfiguration.NetworkPrefabOverride;
                    }

                    if (prefabName == FRDIGE_PREFAB)
                    {
                        bc = replacementEntity.gameObject.AddComponent<BoxCollider>();
                        bc.center = new Vector3(0.0f, 0.982f, 0.0f);
                        bc.size = new Vector3(1.000f, 2.00f, 1.000f);
                    }

                    if (bc != null)
                    {
                        bc.enabled = true;
                        bc.isTrigger = false;
                        bc.excludeLayers = IGNORE_COL_MASK;
                    }
                    else
                    {
                        if (replacementEntity.GetComponents<Collider>().Length <= 0)
                        {
                            //Instance.PrintWarning($"Camera: {storageContainerConfiguration.PrefabPath} has collisions enabled but no existing collider");
                        }
                    }
                }

                newGO.layer = (int)Layer.Default;
                replacementEntity.pickup.enabled = false;
                replacementEntity.baseProtection = ScriptableObject.CreateInstance<ProtectionProperties>();
                replacementEntity.bounds = baseEntity.bounds;
                replacementEntity.impactEffect = baseEntity.impactEffect;
                replacementEntity.skinID = storageContainerConfiguration.SkinId;
                replacementEntity.dropsLoot = false;
                replacementEntity.sendsHitNotification = true;
                replacementEntity.EnableSaving(false);
                replacementEntity.numSlots = storageContainerConfiguration.ItemCount;
                replacementEntity.outputs = oldCompo.outputs;
                replacementEntity.inputs = oldCompo.inputs;

                if (!string.IsNullOrEmpty(storageContainerConfiguration.PanelName))
                {
                    replacementEntity.lootPanelName = storageContainerConfiguration.PanelName;
                }
                else
                {
                    replacementEntity.lootPanelName = oldCompo.lootPanelName;
                }

                replacementEntity.AllowedItems = storageContainerConfiguration.AllowedItems;
                replacementEntity.SetParent(parentEntity, false, true);

                replacementEntity.health = 100;
                replacementEntity.startHealth = 100;
                replacementEntity._maxHealth = 100;
                replacementEntity.maxHealthOverride = 100;
                replacementEntity.gameObject.AwakeFromInstantiate();
                replacementEntity.IgnoreCodelock = storageContainerConfiguration.IgnoreCodelock;

                if (spawn)
                {
                    replacementEntity.Spawn();
                }

                using (FlagsUpdateScope flagsUpdateScope = replacementEntity.StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.Locked, storageContainerConfiguration.IsLocked, true);
                }

                return replacementEntity;
            }

            public static T CreateContainer<T>(BaseEntity parentEntity, ContainerConfiguration storageContainerConfiguration, bool spawn = true) where T : StorageContainer
            {
                var newGO = GameManager.server.CreatePrefab(storageContainerConfiguration.PrefabPath, storageContainerConfiguration.Location, Quaternion.Euler(storageContainerConfiguration.Rotation), false);
                newGO.transform.localScale = storageContainerConfiguration.Scale;

                var baseEntity = newGO.GetComponent<BaseEntity>();
                var oldCompo = newGO.GetComponent<StorageContainer>();

                UnityEngine.Object.DestroyImmediate(baseEntity);

                var iterateOver = newGO.GetComponentsInChildren<Component>().ToArray();
                for (var i = 0; i < iterateOver.Length; i++)
                {
                    var compo = iterateOver[i];

                    if (compo is Rigidbody)
                    {
                        continue;
                    }

                    if (compo is Transform)
                    {
                        continue;
                    }

                    if (storageContainerConfiguration.DetectCollisions && compo is Collider col && !col.isTrigger)
                    {
                        compo.gameObject.layer = (int)Layer.Default;
                        continue;
                    }

                    UnityEngine.Object.DestroyImmediate(compo);
                }

                var rigidbody = newGO.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    UnityEngine.Object.DestroyImmediate(rigidbody);
                }

                var replacementEntity = newGO.AddComponent<T>();

                if (!string.IsNullOrEmpty(storageContainerConfiguration.NetworkPrefabOverride))
                {
                    replacementEntity.prefabID = StringPool.Get(storageContainerConfiguration.NetworkPrefabOverride);
                }
                else
                {
                    replacementEntity.prefabID = StringPool.Get(storageContainerConfiguration.PrefabPath);
                }

                if (storageContainerConfiguration.DetectCollisions)
                {
                    BoxCollider bc = null;
                    var prefabName = storageContainerConfiguration.PrefabPath;
                    if (!string.IsNullOrEmpty(storageContainerConfiguration.NetworkPrefabOverride))
                    {
                        prefabName = storageContainerConfiguration.NetworkPrefabOverride;
                    }

                    if (prefabName == FRDIGE_PREFAB)
                    {
                        bc = replacementEntity.gameObject.AddComponent<BoxCollider>();
                        bc.center = new Vector3(0.0f, 0.982f, 0.0f);
                        bc.size = new Vector3(1.000f, 2.00f, 1.000f);
                    }

                    if (bc != null)
                    {
                        bc.enabled = true;
                        bc.isTrigger = false;
                        bc.excludeLayers = IGNORE_COL_MASK;
                    }
                    else
                    {
                        if (replacementEntity.GetComponents<Collider>().Length <= 0)
                        {
                            //Instance.PrintWarning($"Camera: {storageContainerConfiguration.PrefabPath} has collisions enabled but no existing collider");
                        }
                    }
                }

                newGO.layer = (int)Layer.Default;
                replacementEntity.pickup.enabled = false;
                replacementEntity.baseProtection = ScriptableObject.CreateInstance<ProtectionProperties>();
                replacementEntity.bounds = baseEntity.bounds;
                replacementEntity.impactEffect = baseEntity.impactEffect;
                replacementEntity.skinID = storageContainerConfiguration.SkinId;
                replacementEntity.dropsLoot = false;
                replacementEntity.sendsHitNotification = true;
                replacementEntity.EnableSaving(false);
                replacementEntity.inventorySlots = storageContainerConfiguration.ItemCount;

                if (!string.IsNullOrEmpty(storageContainerConfiguration.PanelName))
                {
                    replacementEntity.panelName = storageContainerConfiguration.PanelName;
                }
                else
                {
                    replacementEntity.panelName = oldCompo.panelName;
                }

                if (replacementEntity is SpecialBeehive sbh && oldCompo is Beehive bh)
                {
                    sbh.HoneyCombDefinition = bh.HoneyCombDefinition;
                    sbh.BeeNucleusDefinition = bh.BeeNucleusDefinition;
                    sbh.allowedItem = bh.allowedItem;
                    sbh.allowedItem2 = bh.allowedItem2;
                    sbh.masterSwarm = bh.masterSwarm;
                    sbh.hurtTrigger = bh.hurtTrigger;
                }

                (replacementEntity as IStorageContainer).Config = storageContainerConfiguration;
                replacementEntity.SetParent(parentEntity, false, true);
                replacementEntity.health = 100;
                replacementEntity.startHealth = 100;
                replacementEntity._maxHealth = 100;
                replacementEntity.maxHealthOverride = 100;
                replacementEntity.gameObject.AwakeFromInstantiate();

                UnityEngine.Object.DestroyImmediate(oldCompo);
                if (spawn)
                {
                    replacementEntity.Spawn();
                }

                using (FlagsUpdateScope flagsUpdateScope = replacementEntity.StartSetFlags(FlagsUpdateMode.SendNetworkUpdate))
                {
                    flagsUpdateScope.Set(Flags.Locked, storageContainerConfiguration.IsLocked, true);
                }

                return replacementEntity;
            }

            public static SpecialContainerIOEntity CreateContainerIOEntity(BaseEntity parentEntity, ContainerAdaptorSetting adaptorConfig, ICustomContainer container)
            {
                var newGO = GameManager.server.CreatePrefab(adaptorConfig.PrefabPath, adaptorConfig.Location, Quaternion.Euler(adaptorConfig.Rotation), false);
                newGO.transform.localScale = adaptorConfig.Scale;

                var baseEntity = newGO.GetComponent<BaseEntity>();
                var oldCompo = newGO.GetComponent<IOEntity>();

                UnityEngine.Object.DestroyImmediate(baseEntity);

                var iterateOver = newGO.GetComponentsInChildren<Component>().ToArray();
                var hasIOEntityChecker = false;
                for (var i = 0; i < iterateOver.Length; i++)
                {
                    var compo = iterateOver[i];

                    if (compo is Rigidbody)
                    {
                        continue;
                    }

                    if (compo is Transform)
                    {
                        continue;
                    }

                    if (compo is IOEntityMovementChecker)
                    {
                        hasIOEntityChecker = true;
                        continue;
                    }

                    if (adaptorConfig.DetectCollisions && compo is Collider col && !col.isTrigger)
                    {
                        compo.gameObject.layer = (int)Layer.Default;
                        continue;
                    }

                    UnityEngine.Object.DestroyImmediate(compo);
                }

                var rigidbody = newGO.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    UnityEngine.Object.DestroyImmediate(rigidbody);
                }

                var replacementEntity = newGO.AddComponent<SpecialContainerIOEntity>();

                replacementEntity.prefabID = StringPool.Get(adaptorConfig.PrefabPath);

                newGO.layer = (int)Layer.Default;
                replacementEntity.pickup.enabled = false;
                replacementEntity.baseProtection = ScriptableObject.CreateInstance<ProtectionProperties>();
                replacementEntity.bounds = baseEntity.bounds;
                replacementEntity.impactEffect = baseEntity.impactEffect;
                replacementEntity.sendsHitNotification = true;
                replacementEntity.EnableSaving(false);

                replacementEntity.ioType = oldCompo.ioType;

                if (adaptorConfig.ContainerAdaptorType == ContainerAdaptorType.Input)
                {
                    replacementEntity.inputs = oldCompo.inputs;
                    if (!string.IsNullOrEmpty(adaptorConfig.InputNameOverride))
                    {
                        for (int i = 0; i < replacementEntity.inputs.Length; i++)
                        {
                            var input = replacementEntity.inputs[i];
                            input.niceName = adaptorConfig.InputNameOverride;
                        }
                    }

                    replacementEntity.outputs = new IOEntity.IOSlot[0];
                }

                if (adaptorConfig.ContainerAdaptorType == ContainerAdaptorType.Output)
                {
                    if (!string.IsNullOrEmpty(adaptorConfig.OutputNameOverride))
                    {
                        replacementEntity.outputs = oldCompo.outputs;
                        for (int i = 0; i < replacementEntity.outputs.Length; i++)
                        {
                            var output = replacementEntity.outputs[i];
                            output.niceName = adaptorConfig.OutputNameOverride;
                        }
                    }

                    replacementEntity.inputs = new IOEntity.IOSlot[0];
                }

                replacementEntity.SetParent(parentEntity, false, true);

                if (!hasIOEntityChecker)
                {
                    replacementEntity.gameObject.AddComponent<IOEntityMovementChecker>();
                }

                replacementEntity.startHealth = 100;
                replacementEntity.health = 100;
                replacementEntity._maxHealth = 100;
                replacementEntity.maxHealthOverride = 100;
                replacementEntity.CustomContainer = container;
                replacementEntity.gameObject.AwakeFromInstantiate();
                replacementEntity.Spawn();

                return replacementEntity;
            }

            public static T CreateContainerProxy<T>(BaseEntity parentEntity, ProxyContainerSetting storageContainerConfiguration, string panelName) where T : SpecialStorageContainer
            {
                var newGO = GameManager.server.CreatePrefab(storageContainerConfiguration.PrefabPath, storageContainerConfiguration.Location, Quaternion.Euler(storageContainerConfiguration.Rotation), false);
                newGO.transform.localScale = storageContainerConfiguration.Scale;

                var baseEntity = newGO.GetComponent<BaseEntity>();
                var oldCompo = newGO.GetComponent<StorageContainer>();

                UnityEngine.Object.DestroyImmediate(baseEntity);

                var iterateOver = newGO.GetComponentsInChildren<Component>().ToArray();
                for (var i = 0; i < iterateOver.Length; i++)
                {
                    var compo = iterateOver[i];

                    if (compo is Rigidbody)
                    {
                        continue;
                    }

                    if (compo is Transform)
                    {
                        continue;
                    }

                    if (storageContainerConfiguration.DetectCollisions && compo is Collider col && !col.isTrigger)
                    {
                        compo.gameObject.layer = (int)Layer.Default;
                        continue;
                    }

                    UnityEngine.Object.DestroyImmediate(compo);
                }

                var rigidbody = newGO.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    UnityEngine.Object.DestroyImmediate(rigidbody);
                }

                var replacementEntity = newGO.AddComponent<T>();

                replacementEntity.prefabID = StringPool.Get(storageContainerConfiguration.PrefabPath);

                newGO.layer = (int)Layer.Default;
                replacementEntity.pickup.enabled = false;
                replacementEntity.baseProtection = ScriptableObject.CreateInstance<ProtectionProperties>();
                replacementEntity.bounds = baseEntity.bounds;
                replacementEntity.impactEffect = baseEntity.impactEffect;
                replacementEntity.skinID = storageContainerConfiguration.SkinId;
                replacementEntity.dropsLoot = false;
                replacementEntity.sendsHitNotification = true;
                replacementEntity.EnableSaving(false);
                replacementEntity.panelName = panelName;

                replacementEntity.SetParent(parentEntity, false, true);

                replacementEntity.startHealth = 100;
                replacementEntity.health = 100;
                replacementEntity._maxHealth = 100;
                replacementEntity.maxHealthOverride = 100;
                replacementEntity.gameObject.AwakeFromInstantiate();
                replacementEntity.Spawn();

                return replacementEntity;
            }

            public static void InitializeColliders(BaseEntity baseEntity, List<BoxColliderConfig> boxColliders, List<SphereColliderConfiguration> sphereColliders)
            {
                if (boxColliders != null)
                {
                    for (int i = 0; i < boxColliders.Count; i++)
                    {
                        var bcc = boxColliders[i];
                        InitializeBoxCollider(baseEntity, bcc);
                    }
                }

                if (sphereColliders != null)
                {
                    for (int i = 0; i < sphereColliders.Count; i++)
                    {
                        var csc = sphereColliders[i];
                        InitializeSphereCollider(baseEntity, csc);
                    }
                }
            }

            static void InitializeBoxCollider(BaseEntity baseEntity, BoxColliderConfig boxColliderConfiguration)
            {
                var bcgo = new GameObject();
                var bc = bcgo.AddComponent<BoxCollider>();
                bc.center = boxColliderConfiguration.Center;
                bc.size = boxColliderConfiguration.Size;
                bc.enabled = true;
                bc.excludeLayers = (int)boxColliderConfiguration.ExcludeLayers;
                bc.transform.SetParent(baseEntity.transform, false);
                bc.gameObject.layer = (int)Layer.Default;

                if (boxColliderConfiguration.RequireVehicleMounted || boxColliderConfiguration.RequireEngineOff)
                {
                    var cew = bcgo.AddComponent<ColliderEngineWatcher>();
                    cew.Config = boxColliderConfiguration;
                    cew.Initialize();
                }
            }

            static void InitializeSphereCollider(BaseEntity baseEntity, SphereColliderConfiguration sphereColliderConfiguration)
            {
                var scgo = new GameObject();
                var sc = scgo.AddComponent<SphereCollider>();
                sc.center = sphereColliderConfiguration.Center;
                sc.radius = sphereColliderConfiguration.Radius;
                sc.enabled = true;
                sc.transform.SetParent(baseEntity.transform, false);
                sc.excludeLayers = (int)sphereColliderConfiguration.ExcludeLayers;
                sc.gameObject.layer = (int)Layer.Default;

                if (sphereColliderConfiguration.RequireVehicleMounted || sphereColliderConfiguration.RequireEngineOff)
                {
                    var cew = scgo.AddComponent<ColliderEngineWatcher>();
                    cew.Config = sphereColliderConfiguration;
                    cew.Initialize();
                }
            }
        }

        public class RadarHandler : MonoBehaviour
        {
            Dictionary<IRadarVehicle, string[]> radarVehicles = new Dictionary<IRadarVehicle, string[]>();
            Dictionary<IRadarVehicle, string[]> queuedCommands = new Dictionary<IRadarVehicle, string[]>();

            public void RegisterRadarVehicle(IRadarVehicle radarVehicle)
            {
                if (!radarVehicle.VehicleConfig.RadarSettings.Enabled)
                {
                    return;
                }

                if (radarVehicles.ContainsKey(radarVehicle))
                {
                    return;
                }

                var radarCommands = CreateRadarCommands(radarVehicle);
                radarVehicles[radarVehicle] = radarCommands;
                queuedCommands[radarVehicle] = radarCommands;
                InvokeHandler.Invoke(this, ExecuteRadarCommands, 1f);
            }

            public void DrawRadarVehicles(IRadarVehicle radarVehicle)
            {
                if (!radarVehicle.VehicleConfig.RadarSettings.CanRadarOtherVehicles)
                {
                    return;
                }

                var radarUser = radarVehicle.CustomGetDriver();
                foreach (var radarVehicleCmds in radarVehicles)
                {
                    for (int i = 0; i < radarVehicleCmds.Value.Length; i++)
                    {
                        var cmd = radarVehicleCmds.Value[i];
                        radarUser.SendConsoleCommand(cmd);
                    }
                }
            }

            public void ClearRadarVehicles(IRadarVehicle radarVehicle, BasePlayer playerToClear)
            {
                if (!radarVehicle.VehicleConfig.RadarSettings.CanRadarOtherVehicles)
                {
                    return;
                }


                var radarUser = playerToClear;
                foreach (var radarVehicleKv in radarVehicles)
                {
                    var cmd = CreateRemoveCommand(radarVehicleKv.Key);
                    radarUser.SendConsoleCommand(cmd);
                }
            }

            string[] CreateRadarCommands(IRadarVehicle radarVehicle)
            {
                var netId = radarVehicle.BaseVehicle.net.ID.Value;
                return new string[1] {
                    $"ddraw.box -1 \"\" 0,0,0 5 0,0,0 10 0 {netId} {netId}", 
                    //$"ddraw.text -1 \"\" 0,1.5,0 \"{radarVehicle.BaseVehicle.ShortPrefabName}\" 1 1 1 {netId} {netId}" 
                };
            }

            string CreateRemoveCommand(IRadarVehicle radarVehicle)
            {
                return $"ddraw.clearid {radarVehicle.BaseVehicle.net.ID.Value}";
            }


            // If draw = false, then vehicle has died and this will be removed automatically on the client
            public void RemoveRadarVehicle(IRadarVehicle radarVehicle, bool draw = true)
            {
                if (!radarVehicles.ContainsKey(radarVehicle))
                {
                    return;
                }

                radarVehicles.Remove(radarVehicle);

                if (!draw)
                {
                    return;
                }

                queuedCommands[radarVehicle] = new string[] { CreateRemoveCommand(radarVehicle) };

                InvokeHandler.Invoke(this, ExecuteRadarCommands, 1f);
                // Remove vehicle for existing
                //
                // or
                //
                // Remove all and redraw
            }

            void ExecuteRadarCommands()
            {
                foreach (var kv in radarVehicles)
                {
                    var radarVehicle = kv.Key;
                    if (!radarVehicle.VehicleConfig.RadarSettings.CanRadarOtherVehicles)
                    {
                        continue;
                    }

                    var radarUser = radarVehicle.CustomGetDriver();
                    foreach (var qcKv in queuedCommands)
                    {
                        if (qcKv.Key == radarVehicle)
                        {
                            continue;
                        }

                        for (int i = 0; i < qcKv.Value.Length; i++)
                        {
                            var cmd = qcKv.Value[i];
                            radarUser.SendConsoleCommand(cmd);
                        }
                    }
                }

                queuedCommands.Clear();
            }
        }

        #endregion

        #region CustomAntiHack

        public static class CustomAntiHack
        {
            public const int BufferLength = 8192;

            public static RaycastHit[] HitBuffer;

            public static RaycastHit[] HitBufferB;

            public static Collider[] ColBuffer;

            public static bool TestNoClipping(Vector3 oldPos, Vector3 newPos, float radius, float backtracking, bool vehicleLayer = false, BaseEntity ignoreEntity = null)
            {
                int num = 1503731969;
                if (!vehicleLayer)
                {
                    num &= -8193;
                }

                Vector3 normalized = (newPos - oldPos).normalized;
                Vector3 vector = oldPos - normalized * backtracking;
                float magnitude = (newPos - vector).magnitude;
                Ray ray = new Ray(vector, normalized);
                if (GamePhysics.CheckCapsule(oldPos, newPos, radius, num, QueryTriggerInteraction.Ignore))
                {
                    List<Collider> obj = Facepunch.Pool.Get<List<Collider>>();
                    GamePhysics.OverlapCapsule(oldPos, newPos, radius, obj, num);
                    bool flag = false;
                    bool flag2 = false;
                    for (int i = 0; i < obj.Count; i++)
                    {
                        Collider collider = obj[i];
                        if (collider is TerrainCollider)
                        {
                            flag2 = true;
                        }
                        else if (((int)collider.excludeLayers & 0x1000) != 4096)
                        {
                            BaseEntity baseEntity = GameObjectEx.ToBaseEntity(collider);
                            if (ShouldUseCastNoClipChecks(baseEntity))
                            {
                                flag = true;
                            }
                            else if (!GamePhysics.CompareEntity(baseEntity, ignoreEntity))
                            {
                                Facepunch.Pool.FreeUnmanaged(ref obj);
                                return true;
                            }
                        }
                    }

                    Facepunch.Pool.FreeUnmanaged(ref obj);
                    if (flag || flag2)
                    {
                        RaycastHit hitInfo;
                        if (!flag2 && ignoreEntity == null)
                        {
                            if (!Physics.Raycast(ray, out hitInfo, magnitude + radius, num, QueryTriggerInteraction.Ignore))
                            {
                                return Physics.SphereCast(ray, radius, out hitInfo, magnitude, num, QueryTriggerInteraction.Ignore);
                            }

                            return true;
                        }

                        if (!Trace(ray, 0f, out hitInfo, magnitude + radius, num, QueryTriggerInteraction.Ignore, ignoreEntity))
                        {
                            return Trace(ray, radius, out hitInfo, magnitude, num, QueryTriggerInteraction.Ignore, ignoreEntity);
                        }

                        return true;
                    }
                }

                return false;
            }

            private static bool ShouldUseCastNoClipChecks(BaseEntity entity)
            {
                if (entity is AnimatedBuildingBlock || entity is SlidingProgressDoor || entity is SleepingBag)
                {
                    return true;
                }

                return false;
            }

            public static bool TestNoClipping(Vector3 oldPos, Vector3 newPos, float radius, float backtracking, bool sphereCast, out Collider collider, bool vehicleLayer = false, BaseEntity ignoreEntity = null)
            {
                int num = 1503731969;
                if (!vehicleLayer)
                {
                    num &= -8193;
                }

                Vector3 normalized = (newPos - oldPos).normalized;
                Vector3 vector = oldPos - normalized * backtracking;
                float magnitude = (newPos - vector).magnitude;
                Ray ray = new Ray(vector, normalized);
                RaycastHit hitInfo;
                bool flag = Trace(ray, 0f, out hitInfo, magnitude + radius, num, QueryTriggerInteraction.Ignore, ignoreEntity);
                if (!flag && sphereCast)
                {
                    flag = Trace(ray, radius, out hitInfo, magnitude, num, QueryTriggerInteraction.Ignore, ignoreEntity);
                }

                collider = hitInfo.collider;
                if (flag)
                {
                    return Verify(hitInfo, vector);
                }

                return false;
            }

            public static bool Trace(Ray ray, float radius, out RaycastHit hitInfo, float maxDistance = float.PositiveInfinity, int layerMask = -5, QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.UseGlobal, BaseEntity ignoreEntity = null)
            {
                List<RaycastHit> obj = Facepunch.Pool.Get<List<RaycastHit>>();
                TraceAllUnordered(ray, radius, obj, maxDistance, layerMask, triggerInteraction, ignoreEntity);
                if (obj.Count == 0)
                {
                    hitInfo = default(RaycastHit);
                    Facepunch.Pool.FreeUnmanaged(ref obj);
                    return false;
                }

                GamePhysics.Sort(obj);
                hitInfo = obj[0];
                Facepunch.Pool.FreeUnmanaged(ref obj);
                return true;
            }

            public static void TraceAllUnordered(Ray ray, float radius, List<RaycastHit> hits, float maxDistance = float.PositiveInfinity, int layerMask = -5, QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.UseGlobal, BaseEntity ignoreEntity = null)
            {
                int num = ((radius != 0f) ? Physics.SphereCastNonAlloc(ray, radius, HitBuffer, maxDistance, layerMask, triggerInteraction) : Physics.RaycastNonAlloc(ray, HitBuffer, maxDistance, layerMask, triggerInteraction));
                if (num < HitBuffer.Length && ((uint)layerMask & 0x10u) != 0 && WaterSystem.Trace(ray, out var position, out var normal, maxDistance))
                {
                    RaycastHit raycastHit = default(RaycastHit);
                    raycastHit.point = position;
                    raycastHit.normal = normal;
                    raycastHit.distance = (position - ray.origin).magnitude;
                    RaycastHit raycastHit2 = raycastHit;
                    HitBuffer[num++] = raycastHit2;
                }

                if (num == 0)
                {
                    return;
                }

                if (num >= HitBuffer.Length)
                {
                    Debug.LogWarning("Physics query is exceeding hit buffer length. Contact Karuza");
                }

                for (int i = 0; i < num; i++)
                {
                    RaycastHit raycastHit3 = HitBuffer[i];
                    if (Verify(raycastHit3, ray.origin, ignoreEntity))
                    {
                        hits.Add(raycastHit3);
                    }
                }
            }

            public static bool Verify(RaycastHit hitInfo, Vector3 rayOrigin, BaseEntity ignoreEntity = null)
            {
                Vector3 vector = hitInfo.point;
                if (hitInfo.collider is TerrainCollider && vector == Vector3.zero && hitInfo.distance == 0f)
                {
                    vector = rayOrigin;
                }

                return Verify(hitInfo.collider, vector, ignoreEntity);
            }

            public static bool Verify(Collider collider, Vector3 point, BaseEntity ignoreEntity = null)
            {
                if (collider == null)
                {
                    if ((bool)WaterSystem.Collision && WaterSystem.Collision.GetIgnore(point))
                    {
                        return false;
                    }

                    return true;
                }

                if (collider is TerrainCollider)
                {
                    if ((bool)TerrainMeta.Collision && TerrainMeta.Collision.GetIgnore(point))
                    {
                        return false;
                    }

                    return true;
                }

                var baseEnt = GameObjectEx.ToBaseEntity(collider);
                if (CompareEntity(baseEnt, ignoreEntity) || (!baseEnt.IsRealNull() && baseEnt.HasParent() && CompareEntity(baseEnt.parentEntity.Get(true), ignoreEntity)))
                {
                    return false;
                }

                return collider.enabled;
            }

            public static bool CompareEntity(BaseEntity a, BaseEntity b)
            {
                if (a.IsRealNull() || b.IsRealNull())
                {
                    return false;
                }

                if (a == b)
                {
                    return true;
                }

                return false;
            }
        }

        #endregion

        #region Entity Bundles

        CustomEntities.CustomPrefabBundle bundle;

        public static void RegisterVehicleBundles()
        {
            var specialGibRecipe = new CustomEntities.CustomPrefabRecipe(PREFAB_SPECIALGIB_SHORTNAME, typeof(SpecialGib), 0, null, false);
            var specialWorldItemRecipe = new CustomEntities.CustomPrefabRecipe(PREFAB_SPECIALWORLDITEM_SHORTNAME, typeof(SpecialWorldItem), 0, null, false);
            var specialTowWorldItemRecipe = new CustomEntities.CustomPrefabRecipe(PREFAB_SPECIALTOWWORLDITEM_SHORTNAME, typeof(SpecialTowWorldItem), 0, null, false);
            var specialTravellingVendorRecipe = new CustomEntities.CustomPrefabRecipe(PREFAB_SPECIALTRAVELLINGVENDOR_SHORTNAME, typeof(SpecialTravellingVendor), 0, null, false);

            Instance.bundle = new CustomEntities.CustomPrefabBundle(Instance, new CustomEntities.GenericPrefabRecipe[] { specialGibRecipe, specialWorldItemRecipe, specialTowWorldItemRecipe, specialTravellingVendorRecipe });
            if (!CustomEntities.CustomPrefabs.RegisterAndLoadBundle(Instance.bundle))
            {
                Instance.PrintError("Bundles failed to load");
            }
        }

        public static void SaveAndUnregisterEntityBundles()
        {
            if (Instance.bundle == null)
            {
                return;
            }

            if (!CustomEntities.CustomPrefabs.SaveAndUnregisterBundle(Instance.bundle))
            {
                Instance.PrintError("Bundles failed to unload");
            }

            Instance.bundle.Recipes = null;
            Instance.bundle = null;
        }

        #endregion

        #region Config Migration

        public static void TryUpdateConfig(string entityName, CustomVehicleConfig config)
        {
        }

        void PopulateCollections()
        {
            TowTrigger.CachedTriggers = new Dictionary<int, TowTrigger>();

            InstrumentPrefabs = new HashSet<string>()
            {
                "assets/bundled/prefabs/static/piano.deployed.static.prefab",
                "assets/bundled/prefabs/static/drumkit.deployed.static.prefab",
                "assets/bundled/prefabs/static/xylophone.deployed.static.prefab",
                "assets/prefabs/instruments/piano/piano.deployed.prefab",
                "assets/prefabs/instruments/drumkit/drumkit.deployed.prefab",
                "assets/prefabs/instruments/xylophone/xylophone.deployed.prefab"
            };

            DeadTreePrefabs = new List<string>()
            {
                "assets/bundled/prefabs/autospawn/resource/v3_tundra_forest_dead/pine_dead_a.prefab",
                "assets/bundled/prefabs/autospawn/resource/v3_tundra_forest_dead/pine_dead_b.prefab",
                "assets/bundled/prefabs/autospawn/resource/v3_tundra_forest_dead/pine_dead_c.prefab",
                "assets/bundled/prefabs/autospawn/resource/v3_tundra_forest_dead/pine_dead_d.prefab",
                "assets/bundled/prefabs/autospawn/resource/v3_tundra_forest_dead/pine_dead_e.prefab",
                "assets/bundled/prefabs/autospawn/resource/v3_tundra_forest_dead/pine_dead_f.prefab"
            };

            CustomAntiHack.HitBuffer = new RaycastHit[8192];
            CustomAntiHack.HitBufferB = new RaycastHit[8192];
            CustomAntiHack.ColBuffer = new Collider[8192];
        }

        #endregion

        #region Config

        private Configuration configuration;

        public class Configuration
        {
            public bool SubscribeToCargoSpawn { get; set; } = true;
            public bool DefaultForeignLanguages { get; set; } = true;
            public bool SpawnMeshColliderVehicles { get; set; } = true;
            public bool EnableRadio { get; set; } = false;
            public bool SaveApiConfigsToDisk { get; set; } = true;
            public string APIPath { get; set; }
            public string APIId { get; set; }
            public string APISecret { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                configuration = Config.ReadObject<Configuration>();
                if (configuration == null)
                {
                    throw new Exception();
                }

                SaveConfig();
            }
            catch (Exception ex)
            {
                Config.WriteObject(configuration, false, $"{Interface.Oxide.ConfigDirectory}/{Name}.jsonError");
                PrintError($"The configuration file contains an error. {ex}");
            }
        }

        protected override void LoadDefaultConfig()
        {
            configuration = new Configuration();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(configuration);
        }

        public class ConfigVectorConverter : JsonConverter
        {
            private static readonly Type V3 = typeof(ConfigVector);

            public bool EnableVector3 { get; set; }

            public ConfigVectorConverter()
            {
                EnableVector3 = true;
            }

            public ConfigVectorConverter(bool enableVector2, bool enableVector3, bool enableVector4)
                : this()
            {
                EnableVector3 = enableVector3;
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                if (value == null)
                {
                    writer.WriteNull();
                    return;
                }

                Type type = value.GetType();
                if ((object)type == V3)
                {
                    ConfigVector vector2 = (ConfigVector)value;
                    WriteVector(writer, vector2.x, vector2.y, vector2.z, null);
                }
                else
                {
                    throw new NotImplementedException();
                }
            }

            private static void WriteVector(JsonWriter writer, float x, float y, float? z, float? w)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("x");
                writer.WriteValue(x);
                writer.WritePropertyName("y");
                writer.WriteValue(y);
                if (z.HasValue)
                {
                    writer.WritePropertyName("z");
                    writer.WriteValue(z.Value);
                    if (w.HasValue)
                    {
                        writer.WritePropertyName("w");
                        writer.WriteValue(w.Value);
                    }
                }

                writer.WriteEndObject();
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                if ((object)objectType == V3)
                {
                    return PopulateVector3(reader);
                }

                throw new NotImplementedException();
            }

            public override bool CanConvert(Type objectType)
            {
                if (!EnableVector3 || (object)objectType != V3)
                {
                    return false;
                }

                return true;
            }

            private static ConfigVector PopulateVector3(JsonReader reader)
            {
                var result = default(ConfigVector);
                if (reader.TokenType != JsonToken.Null)
                {
                    JObject jObject = JObject.Load(reader);
                    result.x = jObject["x"].Value<float>();
                    result.y = jObject["y"].Value<float>();
                    result.z = jObject["z"].Value<float>();
                }

                return result;
            }
        }

        #endregion

        #region API

        public interface IKaruzaEntityPlugin
        {
            void RegisterEntityBundles(Dictionary<string, SuperBaseEntityConfig> configs);
            string Name { get; }
            string DirectoryPath { get; }
        }

        public static void RequestEntityConfigs<T>(IKaruzaEntityPlugin karuzaEntityPlugin, int entityTypeId) where T : SuperBaseEntityConfig
        {
            if (!APIHelper.IsConfigured())
            {
                return;
            }

            var successCallback = (int code, string response) =>
            {
                Instance.Puts($"Entity Configs for {karuzaEntityPlugin.Name} Received!");
                Instance.Puts("Parsing Configs");

                var serializerSettings = new JsonSerializerSettings();
                serializerSettings.Converters.Add(new ConfigVectorConverter());

                var entityConfigs = JsonConvert.DeserializeObject<List<EntityConfigModel>>(response, serializerSettings);

                var serializer = new JsonSerializer();
                serializer.Converters.Add(new ConfigVectorConverter());


                var entityConfigsDict = new Dictionary<string, SuperBaseEntityConfig>();
                for (int i = 0; i < entityConfigs.Count; i++)
                {
                    var vehicleConfig = entityConfigs[i];

                    try
                    {
                        var config = vehicleConfig.Config.ToObject<T>(serializer);
                        entityConfigsDict.Add(vehicleConfig.Name, config);
                    }
                    catch (Exception ex)
                    {
                        Instance.PrintError($"Entity failed to load: {vehicleConfig.Name} - Exception: {ex.ToString()}");
                    }
                }

                Instance.Puts($"Parsing Complete for {entityConfigs.Count} {karuzaEntityPlugin.Name} entities");

                if (Instance.configuration.SaveApiConfigsToDisk)
                {
                    SaveEntityConfigsToDisk(karuzaEntityPlugin.DirectoryPath, entityConfigs);
                    Instance.Puts($"Saved {entityConfigs.Count} {karuzaEntityPlugin.Name} configs to {karuzaEntityPlugin.DirectoryPath}");
                }

                Instance.Puts($"Attempting to Register the Bundles for {karuzaEntityPlugin.Name}");
                karuzaEntityPlugin.RegisterEntityBundles(entityConfigsDict);
                Instance.Puts($"Bundles Registered Successfully for {karuzaEntityPlugin.Name}");
            };

            var errorCallback = (int code, string response) =>
            {
                Instance.Puts($"Bundles Failed to Register - Please retry");
            };

            var timerCallback = () =>
            {
                if (Instance == null)
                {
                    return;
                }

                Instance.Puts($"Requesting Entity Configs for {karuzaEntityPlugin.Name} ");
                APIHelper.GetEntityConfigs(entityTypeId, successCallback, errorCallback);
            };

            Instance.timer.In(1f, timerCallback);
        }

        static void SaveEntityConfigsToDisk(string directoryPath, List<EntityConfigModel> entityConfigs)
        {
            if (string.IsNullOrEmpty(directoryPath) || entityConfigs == null)
            {
                return;
            }

            Directory.CreateDirectory(directoryPath);

            foreach (var vehicleConfig in entityConfigs)
            {
                if (string.IsNullOrEmpty(vehicleConfig.Name) || vehicleConfig.Name.StartsWith("_") || vehicleConfig.Config == null)
                {
                    continue;
                }

                var safeName = string.Join("_", vehicleConfig.Name.Split(Path.GetInvalidFileNameChars()));
                var filePath = Path.Combine(directoryPath, $"{safeName}.json");
                File.WriteAllText(filePath, vehicleConfig.Config.ToString(Formatting.Indented));
            }
        }

        public static class APIHelper
        {
            public static bool IsConfigured()
            {
                return !string.IsNullOrEmpty(Instance.configuration.APISecret)
                    && !string.IsNullOrEmpty(Instance.configuration.APIId)
                    && !string.IsNullOrEmpty(Instance.configuration.APIPath);
            }

            public static void GetEntityConfigs(int entityTypeId, Action<int, string> successCallback = null, Action<int, string> errorCallback = null)
            {
                var path = $"{Instance.configuration.APIPath}{entityTypeId}";

                var headers = APIHelper.GetHeaders(Oxide.Core.Libraries.RequestMethod.GET, path, string.Empty, Instance.configuration.APIId.ToLower(), Instance.configuration.APISecret);
                Instance.webrequest.Enqueue(path, string.Empty, (code, response) => CallbackHandler(code, response, successCallback, errorCallback), Instance, Oxide.Core.Libraries.RequestMethod.GET, headers, 180, decompressionMethod: DecompressionMethods.GZip);
            }

            static void CallbackHandler(int code, string response, Action<int, string> successCallback, Action<int, string> errorCallback)
            {
                Instance.Puts($"CallbackHandler - Code: {code}");

                if (code != 200)
                {
                    errorCallback?.Invoke(code, response);
                    Instance.Puts($"CallbackHandler Error - {response}");
                    return;
                }

                if (response == null)
                {
                    errorCallback?.Invoke(code, response);
                    return;
                }

                successCallback?.Invoke(code, response);
            }

            static Dictionary<string, string> GetHeaders(Oxide.Core.Libraries.RequestMethod requestMethod, string url, string body, string appId, string apiKey)
            {
                var toReturn = new Dictionary<string, string>()
                {
                    { "Content-Type", "application/json" },
                    { "Accept-Type", "application/json" },
                    { "Accept-Encoding", "gzip" },
                };

                string requestContentBase64String = string.Empty;
                // Karuza's API signs with HttpUtility.UrlEncode (lowercase %xx).
                // Uri.EscapeDataString uses uppercase hex and will 401.
                string requestUri = Uri.EscapeDataString(url.ToLowerInvariant()).ToLowerInvariant();
                string requestHttpMethod = $"{requestMethod}";

                DateTime epochStart = new DateTime(1970, 01, 01, 0, 0, 0, 0, DateTimeKind.Utc);
                TimeSpan timeSpan = DateTime.UtcNow - epochStart;
                string requestTimeStamp = Convert.ToUInt64(timeSpan.TotalSeconds).ToString();

                string nonce = Guid.NewGuid().ToString("N");

                byte[] content = Encoding.ASCII.GetBytes(body);
                SHA256 hash = SHA256.Create();
                byte[] requestContentHash = hash.ComputeHash(content);
                requestContentBase64String = Convert.ToBase64String(requestContentHash);

                string signatureRawData = $"{appId}{requestHttpMethod}{requestUri}{requestTimeStamp}{nonce}{requestContentBase64String}";
                var secretKeyByteArray = Convert.FromBase64String(apiKey);
                byte[] signature = Encoding.UTF8.GetBytes(signatureRawData);

                using (HMACSHA256 hmac = new HMACSHA256(secretKeyByteArray))
                {
                    byte[] signatureBytes = hmac.ComputeHash(signature);
                    string requestSignatureBase64String = Convert.ToBase64String(signatureBytes);
                    toReturn.Add("Authorization", string.Format("gameserver {0}:{1}:{2}:{3}", appId, requestSignatureBase64String, nonce, requestTimeStamp));
                }

                return toReturn;
            }
        }

        public class EntityConfigModel
        {
            public string Name { get; set; }
            public JObject Config { get; set; }
        }

        #endregion
    }

    #region Extensions
    // https://stackoverflow.com/questions/4108828/generic-extension-method-to-see-if-an-enum-contains-a-flag
    public static class EnumExtensions
    {
        #region Public Static Methods 
        /// <summary>
        /// Determines whether the specified value has flags. Note this method is up to 60 times faster
        /// than the one that comes with .NET 4 as it avoids any explict boxing or unboxing. 
        /// </summary>
        /// <typeparam name="TEnum">The type of the enum.</typeparam>
        /// <param name="value">The value.</param>
        /// <param name="flag">The flag.</param>
        /// <returns>
        ///  <c>true</c> if the specified value has flags; otherwise, <c>false</c>.
        /// </returns>
        /// <exception cref="ArgumentException">If TEnum is not an enum.</exception>
        public static bool HasFlags<TEnum>(this TEnum value, TEnum flag) where TEnum : struct, IComparable, IConvertible, IFormattable
        {
            return EnumExtensionsInternal<TEnum>.HasFlagsDelegate(value, flag);
        }
        #endregion Public Static Methods 

        #region Nested Classes 

        static class EnumExtensionsInternal<TEnum> where TEnum : struct, IComparable, IConvertible, IFormattable
        {
            #region Public Static Variables 
            /// <summary>
            /// The delegate which determines if a flag is set.
            /// </summary>
            public static readonly Func<TEnum, TEnum, bool> HasFlagsDelegate = CreateHasFlagDelegate();
            #endregion Public Static Variables 

            #region Private Static Methods 
            /// <summary>
            /// Creates the has flag delegate.
            /// </summary>
            /// <returns></returns>
            private static Func<TEnum, TEnum, bool> CreateHasFlagDelegate()
            {
                if (!typeof(TEnum).IsEnum)
                {
                    throw new ArgumentException(string.Format("{0} is not an Enum", typeof(TEnum)), typeof(EnumExtensionsInternal<>).GetGenericArguments()[0].Name);
                }

                ParameterExpression valueExpression = Expression.Parameter(typeof(TEnum));
                ParameterExpression flagExpression = Expression.Parameter(typeof(TEnum));
                ParameterExpression flagValueVariable = Expression.Variable(Type.GetTypeCode(typeof(TEnum)) == TypeCode.UInt64 ? typeof(ulong) : typeof(long));
                Expression<Func<TEnum, TEnum, bool>> lambdaExpression = Expression.Lambda<Func<TEnum, TEnum, bool>>(
                  Expression.Block(
                    new[] { flagValueVariable },
                    Expression.Assign(
                      flagValueVariable,
                      Expression.Convert(
                        flagExpression,
                        flagValueVariable.Type
                      )
                    ),
                    Expression.Equal(
                      Expression.And(
                        Expression.Convert(
                          valueExpression,
                          flagValueVariable.Type
                        ),
                        flagValueVariable
                      ),
                      flagValueVariable
                    )
                  ),
                  valueExpression,
                  flagExpression
                );
                return lambdaExpression.Compile();
            }
            #endregion Private Static Methods 
        }

        #endregion Nested Classes 
    }

    #endregion
}
