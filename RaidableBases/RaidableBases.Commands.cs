using Facepunch;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rust;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using static RaidableBases.RaidableBasesExtensionMethods.ExtensionMethods;

namespace RaidableBases
{
    public partial class RaidableBases
    {

        #region Commands

        [ConsoleCommand("ui_buyraid")]
        private void ccmdBuyRaid(ConsoleSystem.Arg arg)
        {
            if (!arg.HasArgs())
            {
                return;
            }

            var player = arg.Player();

            if (player.IsNull() || player.GetIPlayer() == null)
            {
                return;
            }

            if (arg.GetString(0) == "closeui")
            {
                if (config.UI.BuyableCooldowns.BuyOnly)
                {
                    UI.PrivateEvents.Remove(player.userID);
                    UI.DestroyTimer(player, player.userID, UiType.Cooldown);
                    if (!UI.DestroyUi(player, UiType.Cooldown))
                    {
                        CuiHelper.DestroyUi(player, "RB_UI_Cooldown");
                    }
                }
                if (config.UI.Lockout.BuyOnly)
                {
                    UI.PublicEvents.Remove(player.userID);
                    UI.DestroyTimer(player, player.userID, UiType.Lockout);
                    if (!UI.DestroyUi(player, UiType.Lockout))
                    {
                        CuiHelper.DestroyUi(player, "RB_UI_Lockout");
                    }
                }
                UI.DestroyTimer(player, player.userID, UiType.Buyable);
                CuiHelper.DestroyUi(player, "RB_UI_Buyable");
                return;
            }

            if (arg.GetString(0) == "accept_teleport")
            {
                BuyableTeleport(player);
                UI.DestroyUi(player, UiType.Teleport);
                CuiHelper.DestroyUi(player, "RB_UI_Teleport");
                return;
            }

            if (arg.GetString(0) == "decline_teleport")
            {
                UI.DestroyUi(player, UiType.Teleport);
                CuiHelper.DestroyUi(player, "RB_UI_Teleport");
                return;
            }

            CommandBuyRaid(player.GetIPlayer(), config.Settings.BuyCommand, arg.Args.ToStringArray());
        }

        private void OnCuiDraggableDrag(BasePlayer player, string name, Vector3 position, CommunityEntity.DraggablePositionSendType dragType)
        {
            if (player == null)
            {
                return;
            }

            UiType uiType = name switch
            {
                "RB_UI_Buyable" => UiType.Buyable,
                "RB_UI_Cooldown" => UiType.Cooldown,
                "RB_UI_Delay" => UiType.Delay,
                "RB_UI_Lockout" => UiType.Lockout,
                "RB_UI_Status" => UiType.Status,
                "RB_UI_Teleport" => UiType.Teleport,
                _ => UiType.Invalid
            };

            if (uiType == UiType.Invalid || !UI.Offsets.TryGetValue(player.userID, out var ui) || !ui.TryGetValue(uiType, out var offsets))
            {
                return;
            }

            switch (dragType)
            {
                case CommunityEntity.DraggablePositionSendType.Relative:
                    {
                        Vector2 delta = new Vector2(position.x, position.y);
                        offsets.Min += delta;
                        offsets.Max += delta;
                        offsets.NormalizedAnchor = Vector2.zero;
                        break;
                    }
                case CommunityEntity.DraggablePositionSendType.NormalizedParent:
                    {
                        offsets.NormalizedAnchor = new Vector2(position.x, position.y);
                        break;
                    }
                default:
                    return;
            }

            UI.TrySetMoveUi(player, uiType);

            if (SaveOffsetDataTimer is { Destroyed: false }) SaveOffsetDataTimer.Reset();
            else SaveOffsetDataTimer = timer.Once(5f, UI.SaveOffsetData);
        }

        private Timer SaveOffsetDataTimer;

        [ConsoleCommand("rb_ui_move")]
        private void ccmdMovePosition(ConsoleSystem.Arg arg)
        {
            if (!arg.HasArgs() || !arg.Player().Is(out BasePlayer player))
            {
                return;
            }
            if (!Enum.TryParse(arg.GetString(0), true, out UiType type))
            {
                return;
            }
            if (!UI.Offsets.TryGetValue(player.userID, out var ui) || !ui.TryGetValue(type, out var offsets))
            {
                return;
            }
            bool moveUI = UI.IsMovingUi(player, type);
            if (moveUI)
            {
                UI.TrySetMoveUi(player, type, true);
                moveUI = false;
            }
            else moveUI = true;
            switch (type)
            {
                case UiType.Buyable: UI.ShowBuyableUi(player, moveUI); break;
                case UiType.Cooldown: UI.ShowBuyableCooldownsUi(player, moveUI); break;
                case UiType.Delay: UI.ShowDelayUi(player, moveUI); break;
                case UiType.Lockout: UI.ShowLockoutsUi(player, moveUI); break;
                case UiType.Status: UI.ShowStatusUi(player, moveUI); break;
            }
        }

        private void CommandReloadConfig(IPlayer user, string command, string[] args)
        {
            if (user.IsServer || user.Player().IsAdmin)
            {
                if (IsGridLoading() || !IsPasteAvailable())
                {
                    Message(user, IsGridLoading() ? "GridIsLoading" : "PasteOnCooldown");
                    return;
                }
                Message(user, "ReloadInit");
                if (command == "rb.reloadconfig")
                {
                    SetOnSun(false);
                    UI.DestroyAll();
                    Message(user, "ReloadConfig");
                    LoadConfig();
                    Automated.IsMaintainedEnabled = config.Settings.Maintained.Enabled;
                    Automated.StartCoroutine(RaidableType.Maintained, user);
                    Automated.IsScheduledEnabled = config.Settings.Schedule.Enabled;
                    Automated.StartCoroutine(RaidableType.Scheduled, user);
                    buyableEnabled = config.Settings.Buyable.Max > 0;
                    Initialize();
                }
                if (command == "rb.reloadprofiles")
                {
                    ServerMgr.Instance.StartCoroutine(ReloadProfiles(user));
                }
                if (command == "rb.reloadtables")
                {
                    ServerMgr.Instance.StartCoroutine(ReloadTables(user));
                }
            }
        }

        private void Initialize()
        {
            if (config.Settings.Buyable.Cooldowns == null)
            {
                config.Settings.Buyable.Cooldowns = new();
                data.BuyableCooldowns.Clear();
                SaveConfig();
            }
            if (config.Settings.TeleportMarker)
            {
                Subscribe(nameof(OnMapMarkerAdded));
            }
            else Unsubscribe(nameof(OnMapMarkerAdded));
            Subscribe(nameof(OnPlayerSleepEnded));
            GridController.LoadSpawns();
            if (ZoneManager != null)
            {
                SpawnsController.SetupZones(true);
            }
            Skins.Clear();
            CreateDefaultFiles();
            SetOnSun(true);
            GridController.SetupGrid();
        }

        private readonly Dictionary<string, string?> prefabReplacements = new()
        {
            ["assets/prefabs/building/gates.external.high.adobe/gates.external.high.adobe.prefab"] = "assets/prefabs/building/gates.external.high/gates.external.high.stone/gates.external.high.stone.prefab",
            ["assets/prefabs/building/gates.external.high.legacy/gates.external.high.legacy.prefab"] = "assets/prefabs/building/gates.external.high/gates.external.high.stone/gates.external.high.stone.prefab",
            ["assets/prefabs/building/wall.external.high.adobe/wall.external.high.adobe.prefab"] = "assets/prefabs/building/wall.external.high.stone/wall.external.high.stone.prefab",
            ["assets/prefabs/building/wall.external.high.legacy/wall.external.high.legacy.prefab"] = "assets/prefabs/building/wall.external.high.stone/wall.external.high.stone.prefab",
            ["assets/prefabs/building/wall.external.high.frontier/wall.external.high.frontier.prefab"] = "assets/prefabs/building/wall.external.high.stone/wall.external.high.stone.prefab",
            ["assets/prefabs/deployable/chair/ice_throne/chair.icethrone.prefab"] = "assets/prefabs/deployable/chair/chair.deployed.prefab",
            ["assets/prefabs/deployable/floor_half_shelves/halfheight_salvaged_bamboo_shelves.prefab"] = "assets/prefabs/deployable/shelves/shelves.prefab",
            ["assets/prefabs/deployable/hazmatplushy/hazmatplushy_deployed.prefab"] = "assets/prefabs/misc/xmas/pookie/pookie_deployed.prefab",
            ["assets/prefabs/deployable/jack o lantern/jackolantern.angry.prefab"] = "assets/prefabs/deployable/lantern/lantern.deployed.prefab",
            ["assets/prefabs/deployable/jack o lantern/jackolantern.happy.prefab"] = "assets/prefabs/deployable/lantern/lantern.deployed.prefab",
            ["assets/prefabs/deployable/large wood storage/skins/abyss_dlc_large_wood_box/abyss_dlc_storage_horizontal/abyss_barrel_horizontal.prefab"] = "assets/prefabs/deployable/large wood storage/box.wooden.large.prefab",
            ["assets/prefabs/deployable/large wood storage/skins/abyss_dlc_large_wood_box/abyss_dlc_storage_vertical/abyss_barrel_vertical.prefab"] = "assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab",
            ["assets/prefabs/deployable/large wood storage/skins/jungle_dlc_large_wood_box/jungle_dlc_storage_horizontal/wicker_barrel.prefab"] = "assets/prefabs/deployable/large wood storage/box.wooden.large.prefab",
            ["assets/prefabs/deployable/large wood storage/skins/jungle_dlc_large_wood_box/jungle_dlc_storage_vertical/bamboo_barrel.prefab"] = "assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab",
            ["assets/prefabs/deployable/large wood storage/skins/medieval_large_wood_box/medieval.box.wooden.large.prefab"] = "assets/prefabs/deployable/large wood storage/box.wooden.large.prefab",
            ["assets/prefabs/deployable/legacyfurnace/legacy_furnace.prefab"] = "assets/prefabs/deployable/furnace/furnace.prefab",
            ["assets/prefabs/deployable/lunar_new_year_2025_wall_divider/lunar_near_year_2025_wall_divider_a.prefab"] = "",
            ["assets/prefabs/deployable/lunar_new_year_2025_wall_divider/lunar_near_year_2025_wall_divider_b.prefab"] = "",
            ["assets/prefabs/deployable/lunar_new_year_2025_wall_divider/lunar_near_year_2025_wall_divider_c.prefab"] = "",
            ["assets/prefabs/deployable/sculptures/icesculpture/sculpture.ice.deployed.prefab"] = "assets/prefabs/misc/xmas/pookie/pookie_deployed.prefab",
            ["assets/prefabs/deployable/secretlab chair/secretlabchair.deployed.prefab"] = "assets/prefabs/deployable/chair/chair.deployed.prefab",
            ["assets/prefabs/deployable/shelves/skins/salvaged_bamboo_shelves/salvaged_bamboo_shelves.prefab"] = "assets/prefabs/deployable/shelves/shelves.prefab",
            ["assets/prefabs/deployable/signs/sign.pictureframe.portrait.prefab"] = "assets/prefabs/deployable/signs/sign.small.wood.prefab",
            ["assets/prefabs/deployable/signs/sign.pictureframe.xl.prefab"] = "assets/prefabs/deployable/signs/sign.large.wood.prefab",
            ["assets/prefabs/deployable/sofa/sofa.deployed.prefab"] = "assets/prefabs/deployable/chair/chair.deployed.prefab",
            ["assets/prefabs/deployable/sofa/sofa.pattern.deployed.prefab"] = "assets/prefabs/deployable/chair/chair.deployed.prefab",
            ["assets/prefabs/deployable/tool cupboard/retro/cupboard.tool.retro.deployed.prefab"] = "assets/prefabs/deployable/tool cupboard/cupboard.tool.deployed.prefab",
            ["assets/prefabs/deployable/tool cupboard/shockbyte/cupboard.tool.shockbyte.deployed.prefab"] = "assets/prefabs/deployable/tool cupboard/cupboard.tool.deployed.prefab",
            ["assets/prefabs/deployable/wall_single_shallow_shelves/wall_single_shallow_shelf.prefab"] = "assets/prefabs/deployable/shelves/shelves.prefab",
            ["assets/prefabs/deployable/youtooz_figurines/hazmat_youtooz.deployed.prefab"] = "assets/prefabs/misc/xmas/pookie/pookie_deployed.prefab",
            ["assets/prefabs/deployable/youtooz_figurines/heavyscientist_youtooz.deployed.prefab"] = "assets/prefabs/misc/xmas/pookie/pookie_deployed.prefab",
            ["assets/prefabs/instruments/drumkit/drumkit.deployed.prefab"] = "assets/prefabs/deployable/chair/chair.deployed.prefab",
            ["assets/prefabs/instruments/piano/piano.deployed.prefab"] = "assets/prefabs/deployable/chair/chair.deployed.prefab",
            ["assets/prefabs/instruments/xylophone/xylophone.deployed.prefab"] = "assets/prefabs/deployable/chair/chair.deployed.prefab",
            ["assets/prefabs/misc/chinesenewyear/chineselantern/chineselantern.deployed.prefab"] = "assets/prefabs/deployable/lantern/lantern.deployed.prefab",
            ["assets/prefabs/misc/chinesenewyear/chineselantern/chineselantern_white.deployed.prefab"] = "assets/prefabs/deployable/lantern/lantern.deployed.prefab",
            ["assets/prefabs/misc/chippy arcade/chippyarcademachine.prefab"] = "assets/prefabs/deployable/furnace/furnace.prefab",
            ["assets/prefabs/misc/decor_dlc/bardoors/door.double.hinged.bardoors.prefab"] = "assets/prefabs/building/door.double.hinged/door.double.hinged.metal.prefab",
            ["assets/prefabs/misc/decor_dlc/rockingchair/rockingchair.deployed.prefab"] = "assets/prefabs/deployable/chair/chair.deployed.prefab",
            ["assets/prefabs/misc/decor_dlc/rockingchair/skins/rockingchair.rockingchair2.deployed.prefab"] = "assets/prefabs/deployable/chair/chair.deployed.prefab",
            ["assets/prefabs/misc/decor_dlc/rockingchair/skins/rockingchair.rockingchair3.deployed.prefab"] = "assets/prefabs/deployable/chair/chair.deployed.prefab",
            ["assets/prefabs/misc/decor_dlc/storagebarrel/storage_barrel_b.prefab"] = "assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab",
            ["assets/prefabs/misc/decor_dlc/storagebarrel/storage_barrel_c.prefab"] = "assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab",
            ["assets/prefabs/misc/decor_dlc/storagebarrel/unused_storage_barrel_a.prefab"] = "assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab",
            ["assets/prefabs/misc/easter/faberge_egg_a/rustigeegg_a.deployed.prefab"] = "assets/prefabs/misc/xmas/pookie/pookie_deployed.prefab",
            ["assets/prefabs/misc/easter/faberge_egg_b/rustigeegg_b.deployed.prefab"] = "assets/prefabs/misc/xmas/pookie/pookie_deployed.prefab",
            ["assets/prefabs/misc/easter/faberge_egg_c/rustigeegg_c.deployed.prefab"] = "assets/prefabs/misc/xmas/pookie/pookie_deployed.prefab",
            ["assets/prefabs/misc/easter/faberge_egg_d/rustigeegg_d.deployed.prefab"] = "assets/prefabs/misc/xmas/pookie/pookie_deployed.prefab",
            ["assets/prefabs/misc/easter/faberge_egg_e/rustigeegg_e.deployed.prefab"] = "assets/prefabs/misc/xmas/pookie/pookie_deployed.prefab",
            ["assets/prefabs/misc/easter/faberge_egg_f/rustigeegg_f.deployed.prefab"] = "assets/prefabs/misc/xmas/pookie/pookie_deployed.prefab",
            ["assets/prefabs/misc/easter/faberge_egg_g/rustigeegg_g.deployed.prefab"] = "assets/prefabs/misc/xmas/pookie/pookie_deployed.prefab",
            ["assets/prefabs/misc/halloween/candles/largecandleset.prefab"] = "assets/prefabs/deployable/lantern/lantern.deployed.prefab",
            ["assets/prefabs/misc/halloween/candles/smallcandleset.prefab"] = "assets/prefabs/deployable/lantern/lantern.deployed.prefab",
            ["assets/prefabs/misc/halloween/cursed_cauldron/cursedcauldron.deployed.prefab"] = "assets/prefabs/deployable/campfire/campfire.prefab",
            ["assets/prefabs/misc/halloween/skull_fire_pit/skull_fire_pit.prefab"] = "assets/prefabs/deployable/campfire/campfire.prefab",
            ["assets/prefabs/misc/medieval door skin/medieval.door.double.hinged.metal.prefab"] = "assets/prefabs/building/door.double.hinged/door.double.hinged.metal.prefab",
            ["assets/prefabs/misc/medieval door skin/medieval.door.hinged.metal.prefab"] = "assets/prefabs/building/door.hinged/door.hinged.metal.prefab",
            ["assets/prefabs/misc/permstore/factorydoor/door.hinged.industrial.d.prefab"] = "assets/prefabs/building/door.hinged/door.hinged.metal.prefab",
            ["assets/prefabs/misc/summer_dlc/beach_chair/beachchair.deployed.prefab"] = "assets/prefabs/deployable/chair/chair.deployed.prefab",
            ["assets/prefabs/misc/summer_dlc/beach_chair/beachtable.deployed.prefab"] = "assets/prefabs/deployable/table/table.deployed.prefab",
            ["assets/prefabs/misc/summer_dlc/beach_towel/beachtowel.deployed.prefab"] = "assets/prefabs/deployable/sleeping bag/sleepingbag_leather_deployed.prefab",
            ["assets/prefabs/misc/summer_dlc/photoframe/photoframe.landscape.prefab"] = "assets/prefabs/deployable/signs/sign.small.wood.prefab",
            ["assets/prefabs/misc/summer_dlc/photoframe/photoframe.large.prefab"] = "assets/prefabs/deployable/signs/sign.small.wood.prefab",
            ["assets/prefabs/misc/summer_dlc/photoframe/photoframe.portrait.prefab"] = "assets/prefabs/deployable/signs/sign.small.wood.prefab",
            ["assets/prefabs/misc/twitch/hobobarrel/hobobarrel.deployed.prefab"] = "assets/prefabs/deployable/furnace/furnace.prefab",
            ["assets/prefabs/misc/twitch/industrialdoora/door.hinged.industrial.a.prefab"] = "assets/prefabs/building/door.hinged/door.hinged.metal.prefab",
            ["assets/prefabs/misc/xmas/snowman/snowman.deployed.prefab"] = "assets/prefabs/misc/xmas/pookie/pookie_deployed.prefab",
        };

        private readonly Dictionary<string, string> DlcReplacements = new()
        {
            ["abovegroundpool"] = "planter.large",
            ["krieg_storage_horizontal"] = "box.wooden.large",
            ["krieg_storage_vertical"] = "box.wooden.large",
            ["abyss.barrel.horizontal"] = "box.wooden.large",
            ["abyss.barrel.vertical"] = "locker",
            ["arcade.machine.chippy"] = "electric.battery.rechargable.medium",
            ["attire.egg.suit"] = "wood.armor.pants",
            ["attire.nesthat"] = "wood.armor.helmet",
            ["attire.ninja.suit"] = "hazmatsuit",
            ["attire.snowman.helmet"] = "deer.skull.mask",
            ["bamboo.barrel"] = "box.wooden.large",
            ["barricade.medieval"] = "barricade.metal",
            ["bathtub.planter"] = "planter.triangle",
            ["beachchair"] = "chair",
            ["beachparasol"] = "storageadaptor",
            ["beachtable"] = "table",
            ["beachtowel"] = "sleepingbag",
            ["blunderbuss"] = "shotgun.waterpipe",
            ["boogieboard"] = "kayak",
            ["boombox"] = "electric.audioalarm",
            ["boots.frog"] = "attire.hide.boots",
            ["carvable.pumpkin"] = "lantern",
            ["cassette"] = "telephone",
            ["cassette.medium"] = "electric.battery.rechargable.small",
            ["cassette.short"] = "electric.timer",
            ["chair.icethrone"] = "bed",
            ["chicken.costume"] = "roadsign.kilt",
            ["chineselantern"] = "hat.miner",
            ["chineselanternwhite"] = "nightvisiongoggles",
            ["clatter.helmet"] = "bucket.helmet",
            ["cocoknight.armor.gloves"] = "burlap.gloves",
            ["cocoknight.armor.helmet"] = "burlap.headwrap",
            ["cocoknight.armor.pants"] = "attire.hide.pants",
            ["cocoknight.armor.torso"] = "attire.hide.poncho",
            ["concretehatchet"] = "hatchet",
            ["concretepickaxe"] = "pickaxe",
            ["connected.speaker"] = "electric.solarpanel.large",
            ["cupboard.tool.retro"] = "cupboard.tool",
            ["cupboard.tool.shockbyte"] = "cupboard.tool",
            ["cursedcauldron"] = "electric.furnace",
            ["discoball"] = "fireplace.stone",
            ["discofloor"] = "drone",
            ["discofloor.largetiles"] = "smart.switch",
            ["discord.trophy"] = "bucket.helmet",
            ["diverhatchet"] = "axe.salvaged",
            ["diverpickaxe"] = "pickaxe",
            ["divertorch"] = "Torch",
            ["door.double.hinged.bardoors"] = "door.double.hinged.wood",
            ["door.hinged.industrial.a"] = "wall.frame.garagedoor",
            ["draculacape"] = "hoodie",
            ["draculamask"] = "riot.helmet",
            ["dragondoorknocker"] = "door.hinged.metal",
            ["drumkit"] = "fun.guitar",
            ["easterdoorwreath"] = "sign.wooden.small",
            ["factorydoor"] = "door.hinged.metal",
            ["firework.boomer.blue"] = "tunalight",
            ["firework.boomer.champagne"] = "flare",
            ["firework.boomer.green"] = "fuse",
            ["firework.boomer.orange"] = "weapon.mod.simplesight",
            ["firework.boomer.pattern"] = "largemedkit",
            ["firework.boomer.red"] = "battery.small",
            ["firework.boomer.violet"] = "bucket.helmet",
            ["firework.romancandle.blue"] = "trap.bear",
            ["firework.romancandle.green"] = "tincan.alarm",
            ["firework.romancandle.red"] = "electric.button",
            ["firework.romancandle.violet"] = "chocolate",
            ["firework.volcano"] = "pickaxe",
            ["firework.volcano.red"] = "chair",
            ["firework.volcano.violet"] = "electric.button",
            ["fishtrophy"] = "waterjug",
            ["fogmachine"] = "electric.fuelgenerator.small",
            ["frankensteinmask"] = "riot.helmet",
            ["frontier_hatchet"] = "hatchet",
            ["fun.bass"] = "fun.guitar",
            ["fun.boomboxportable"] = "fun.guitar",
            ["fun.casetterecorder"] = "fun.guitar",
            ["fun.cowbell"] = "fun.guitar",
            ["fun.flute"] = "fun.guitar",
            ["fun.jerrycanguitar"] = "fun.guitar",
            ["fun.tambourine"] = "fun.guitar",
            ["fun.trumpet"] = "fun.guitar",
            ["fun.tuba"] = "fun.guitar",
            ["gates.external.high.adobe"] = "gates.external.high.stone",
            ["gates.external.high.legacy"] = "gates.external.high.stone",
            ["gates.external.high.frontier"] = "gates.external.high.stone",
            ["giantcandycanedecor"] = "electric.audioalarm",
            ["giantlollipops"] = "water.catcher.small",
            ["gun.water"] = "waterjug",
            ["gunrack.horizontal"] = "box.wooden",
            ["gunrack.single.1.horizontal"] = "box.wooden",
            ["gunrack.single.2.horizontal"] = "box.wooden.large",
            ["gunrack.single.3.horizontal"] = "box.wooden.large",
            ["gunrack_stand"] = "locker",
            ["gunrack_tall.horizontal"] = "locker",
            ["gunrack_wide.horizontal"] = "locker",
            ["half.bamboo.shelves"] = "shelves",
            ["halloween.surgeonsuit"] = "hazmatsuit",
            ["hat.bunnyhat"] = "wood.armor.helmet",
            ["hat.dragonmask"] = "coffeecan.helmet",
            ["hat.oxmask"] = "coffeecan.helmet",
            ["hat.rabbitmask"] = "diving.mask",
            ["hat.ratmask"] = "coffeecan.helmet",
            ["hat.snakemask"] = "burlap.headwrap",
            ["hat.tigermask"] = "hat.cap",
            ["hat.wellipets"] = "prisonerhood",
            ["hazmat.plushy"] = "diving.mask",
            ["hazmatsuit.arcticsuit"] = "hazmatsuit_scientist",
            ["hazmatsuit.diver"] = "hazmatsuit",
            ["hazmatsuit.frontier"] = "metal.facemask",
            ["hazmatsuit.lumberjack"] = "hazmatsuit_scientist_peacekeeper",
            ["hazmatsuit.nomadsuit"] = "metal.plate.torso",
            ["hazmatsuit.spacesuit"] = "hazmatsuit",
            ["hazmatyoutooz"] = "jacket.snow",
            ["heavyscientistyoutooz"] = "pookie.bear",
            ["hobobarrel"] = "box.wooden.large",
            ["horse.costume"] = "hoodie",
            ["huntingtrophylarge"] = "ceilinglight",
            ["huntingtrophysmall"] = "flashlight.held",
            ["industrial.wall.light"] = "searchlight",
            ["industrial.wall.light.blue"] = "electric.simplelight",
            ["industrial.wall.light.green"] = "electric.simplelight",
            ["industrial.wall.light.red"] = "electric.simplelight",
            ["innertube"] = "sled",
            ["innertube.horse"] = "sled.xmas",
            ["innertube.unicorn"] = "wrappedgift",
            ["jackolantern.angry"] = "lantern",
            ["jackolantern.happy"] = "tunalight",
            ["jungle.rock"] = "rock",
            ["knife.bone.obsidian"] = "knife.bone",
            ["knife.skinning"] = "knife.combat",
            ["knightsarmour.helmet"] = "coffeecan.helmet",
            ["knightsarmour.skirt"] = "roadsign.kilt",
            ["knighttorso.armour"] = "roadsign.jacket",
            ["largecandles"] = "torch",
            ["laserlight"] = "weapon.mod.lasersight",
            ["legacy bow"] = "bow.hunting",
            ["legacyfurnace"] = "furnace",
            ["lumberjack.hatchet"] = "hatchet",
            ["lumberjack.pickaxe"] = "pickaxe",
            ["mace.baseballbat"] = "mace",
            ["medieval.box.wooden.large"] = "box.wooden.large",
            ["medieval.door.double.hinged.metal"] = "door.double.hinged.metal",
            ["medieval.door.hinged.metal"] = "door.hinged.metal",
            ["megaphone"] = "fun.guitar",
            ["metal.facemask.hockey"] = "metal.facemask",
            ["metal.facemask.icemask"] = "metal.facemask",
            ["metal.plate.torso.icevest"] = "metal.plate.torso",
            ["microphonestand"] = "pumpkin",
            ["minecart.planter"] = "planter.triangle",
            ["mobilephone"] = "telephone",
            ["movembermoustache"] = "attire.hide.helterneck",
            ["movembermoustachecard"] = "attire.hide.pants",
            ["mummymask"] = "attire.hide.vest",
            ["newyeargong"] = "black.raspberries",
            ["paddlingpool"] = "planter.large",
            ["photoframe.landscape"] = "sign.wooden.huge",
            ["photoframe.large"] = "sign.wooden.medium",
            ["photoframe.portrait"] = "sign.wooden.large",
            ["piano"] = "fun.guitar",
            ["pistol.water"] = "pistol.eoka",
            ["rail.road.planter"] = "planter.large",
            ["rifle.ak.diver"] = "rifle.ak",
            ["rifle.ak.ice"] = "rifle.lr300",
            ["rifle.ak.jungle"] = "rifle.m39",
            ["rifle.ak.med"] = "rifle.ak",
            ["rocket.launcher.dragon"] = "rocket.launcher",
            ["rockingchair"] = "attire.hide.boots",
            ["rockingchair.rockingchair2"] = "fish.herring",
            ["rockingchair.rockingchair3"] = "hatchet",
            ["rustige_egg_a"] = "weapon.mod.small.scope",
            ["rustige_egg_b"] = "smg.2",
            ["rustige_egg_c"] = "crossbow",
            ["rustige_egg_d"] = "diving.fins",
            ["rustige_egg_e"] = "door.closer",
            ["rustige_egg_f"] = "electric.hbhfsensor",
            ["rustige_egg_g"] = "carburetor3",
            ["salvaged.bamboo.shelves"] = "fuse",
            ["santabeard"] = "egg",
            ["scarecrow"] = "torch",
            ["sculpture.ice"] = "wallpaper",
            ["secretlabchair"] = "chair",
            ["sign.hanging"] = "lantern",
            ["sign.hanging.banner.large"] = "wallpaper",
            ["sign.hanging.ornate"] = "pie.pumpkin",
            ["sign.neon.125x125"] = "sign.wooden.large",
            ["sign.neon.125x215"] = "sign.wooden.large",
            ["sign.neon.125x215.animated"] = "sign.wooden.large",
            ["sign.neon.xl"] = "sign.woodsign.wooden.hugeen.huge",
            ["sign.neon.xl.animated"] = "sign.wooden.huge",
            ["sign.pictureframe.landscape"] = "sign.wooden.medium",
            ["sign.pictureframe.portrait"] = "sign.wooden.medium",
            ["sign.pictureframe.tall"] = "sign.wooden.medium",
            ["sign.pictureframe.xl"] = "sign.wooden.small",
            ["sign.pictureframe.xxl"] = "sign.wooden.small",
            ["sign.pole.banner.large"] = "sign.wooden.small",
            ["sign.post.double"] = "advancedcraftingtea_quality",
            ["sign.post.single"] = "advanceharvestingtea",
            ["sign.post.town"] = "maxhealthtea.advanced",
            ["sign.post.town.roof"] = "scraptea.advanced",
            ["single.shallow.wall.shelves"] = "shelves",
            ["skull"] = "hat.wolf",
            ["skull.trophy"] = "pumpkin",
            ["skull.trophy.jar"] = "lantern",
            ["skull.trophy.jar2"] = "smgbody",
            ["skull.trophy.table"] = "sofa",
            ["skull_fire_pit"] = "fireplace.stone",
            ["skulldoorknocker"] = "electric.button",
            ["skullspikes"] = "spraycan",
            ["skullspikes.candles"] = "lantern",
            ["skullspikes.pumpkin"] = "pumpkin",
            ["skylantern"] = "lantern",
            ["skylantern.skylantern.green"] = "pistol.prototype17",
            ["skylantern.skylantern.orange"] = "rock",
            ["skylantern.skylantern.purple"] = "chair",
            ["skylantern.skylantern.red"] = "lantern",
            ["smallcandles"] = "lantern",
            ["snowmachine"] = "batteringram",
            ["snowman"] = "apple",
            ["snowmobiletomaha"] = "snowmobilee",
            ["sofa"] = "table",
            ["sofa.pattern"] = "chair",
            ["soundlight"] = "lantern",
            ["spear.cny"] = "spear.wooden",
            ["spookyspeaker"] = "mailbox",
            ["unused_storage_barrel_a"] = "tunalight",
            ["storage_barrel_b"] = "rug",
            ["storage_barrel_c"] = "trap.landmine",
            ["strobelight"] = "lantern",
            ["sunglasses"] = "mask.bandana",
            ["sunglasses02black"] = "mask.bandana",
            ["sunglasses02camo"] = "pants.shorts",
            ["sunglasses02red"] = "wood.armor.pants",
            ["sunglasses03black"] = "pants",
            ["sunglasses03chrome"] = "tshirt.long",
            ["sunglasses03gold"] = "shirt.collared",
            ["sunken.knife"] = "knife.combat",
            ["tool.instant_camera"] = "cctv.camera",
            ["toolgun"] = "hammer",
            ["torch.torch.skull"] = "torch",
            ["torchholder"] = "torch",
            ["triangle.rail.road.planter"] = "planter.large",
            ["trophy"] = "ammo.rocket.mlrs",
            ["trophy2023"] = "ammo.rocket.mlrs",
            ["twitch.headset"] = "hat.cap",
            ["twitchrivals2023desk"] = "table",
            ["twitchsunglasses"] = "hat.miner",
            ["vehicle.car_radio"] = "lantern",
            ["wall.external.high.adobe"] = "wall.external.high",
            ["wall.external.high.legacy"] = "wall.external.high.stone",
            ["wall.frame.lunar2025_a"] = "woodframe.small",
            ["wall.frame.lunar2025_b"] = "woodframe.medium",
            ["wall.frame.lunar2025_c"] = "woodframe.large",
            ["wantedposter"] = "rug.bear",
            ["wantedposter.wantedposter2"] = "rug",
            ["wantedposter.wantedposter3"] = "shirt.collared",
            ["wantedposter.wantedposter4"] = "semibody",
            ["wicker.barrel"] = "tunalight",
            ["xmas.door.garland"] = "door.hinged.wood",
            ["xmas.double.door.garland"] = "door.double.hinged.wood",
            ["xmas.lightstring"] = "wiretool",
            ["xmas.lightstring.advanced"] = "wiretool",
            ["xmas.window.garland"] = "wall.window.glass.reinforced",
            ["xmasdoorwreath"] = "door.hinged.wood",
            ["xylophone"] = "telephone",
        };

        private Dictionary<string, int> replaced = new();
        private Dictionary<string, int> removed = new();
        private Coroutine _editCo;
        private int _editCode;

        private void CommandEdit(IPlayer user, string command, string[] args)
        {
            if (user.IsServer || (user.Object as BasePlayer).Connection.authLevel >= 2)
            {
                if (_editCo != null) return;

                if (_editCode == 0) _editCode = UnityEngine.Random.Range(1000, 9999);

                if (!Array.Exists(args, arg => arg == _editCode.ToString()))
                {
                    user.Message($"This action will modify your copypaste files and loot tables to comply with Facepunch's Terms of Service regarding paid content. You should backup your copypaste folder, and loot tables, before proceeding. To confirm, type: {config.Settings.EditCommand} {_editCode}");
                    user.Message($"Default behavior will delete any content rather than replace it. If you prefer to have it replaced, then specify which to replace: {config.Settings.EditCommand} {_editCode} replace_prefabs replace_loot");
                    return;
                }

                _editCode = 0;
                _editCo = ServerMgr.Instance.StartCoroutine(CheckFilesForPaidContent(user, args.Contains("test"), args.Contains("replace_prefabs"), args.Contains("replace_loot")));
            }
        }

        private IEnumerator CheckFilesForPaidContent(IPlayer user, bool test, bool prefabs, bool loot)
        {
            using var sb = DisposableBuilder.Get();
            if (prefabs)
            {
                HashSet<string> files = new();

                foreach (string file in GetCopyPasteFiles())
                {
                    files.Add(Path.Combine("copypaste", System.IO.Path.GetFileNameWithoutExtension(file)).Replace(".json", ""));
                }

                Puts("Confirmed. Updating content within {0} copypaste files...", files.Count);

                foreach (string file in files)
                {
                    yield return CheckFileForPaidContent(file, test, prefabs);
                }
            }

            foreach (var pair in replaced.OrderByAscending(x => x.Value)) sb.AppendLine($"{pair.Key} ({pair.Value})");
            Puts("{0} replacements:\n{1}", replaced.Sum(x => x.Value), sb.ToString());
            sb.Clear();

            foreach (var pair in removed.OrderByAscending(x => x.Value)) sb.AppendLine($"{pair.Key} ({pair.Value})");
            Puts("{0} removals:\n{1}", removed.Sum(x => x.Value), sb.ToString());
            sb.Clear();

            Puts("Updating content within loot table files...");
            yield return ReloadTables(user, sb, true, loot);
            Puts("{0} loot table removals:\n{1}", sb.ToString().Count(c => c == ','), sb.ToString());
            sb.Clear();

            Puts($"{user.Name} ({user.Id}) has removed paid content from all copypaste and loot table files.");
            if (!user.IsServer) user.Message("Edit completed, see server console for additional information.");
        }

        private IEnumerator CheckFileForPaidContent(string filename, bool test, bool prefabs)
        {
            HarmonyDataFile data;
            try
            {
                data = HarmonyDataLayer.GetDatafile(filename);
            }
            catch (Exception ex)
            {
                Puts("Error loading {0}: {1}", filename, ex);
                yield break;
            }

            if (data["entities"] == null)
            {
                Puts($"{filename} is missing entity data");
                yield break;
            }

            var entities = data["entities"] as List<object>;
            bool changed = false;
            int checks = 0;

            for (int i = entities.Count - 1; i >= 0; i--)
            {
                var obj = entities[i];
                if (++checks >= 1000)
                {
                    checks = 0;
                    yield return CoroutineEx.waitForSeconds(0.075f);
                }
                if (!(obj is Dictionary<string, object> entity))
                {
                    continue;
                }
                if (!entity.TryGetValue("prefabname", out var val))
                {
                    continue;
                }
                var prefab = val.ToString();
                try
                {
                    if (!PaidDeployableItems.ContainsKey(prefab))
                    {
                        continue;
                    }

                    string replacement = prefabReplacements.GetValueOrDefault(prefab);

                    changed = true;

                    if (!prefabs || string.IsNullOrEmpty(replacement))
                    {
                        if (!test) entities.RemoveAt(i);
                        removed.TryAdd(prefab, 0);
                        removed[prefab]++;
                    }
                    else
                    {
                        if (!test) entity["prefabname"] = replacement;
                        replaced.TryAdd(prefab, 0);
                        replaced[prefab]++;
                    }
                }
                catch (Exception ex)
                {
                    Puts("Error with prefab: {0}", ex);
                }
            }

            if (changed)
            {
                Puts("Updated {0}", filename);
                if (!test) data.Save();
            }

            _editCo = null;
        }

        private readonly List<string> _buyers = new();
        private void CommandBuyRaid(IPlayer user, string command, string[] args)
        {
            if (user == null)
            {
                return;
            }

            var player = user.Player();

            if (user.IsServer && args.Length >= 1 && args[0].IsSteamId())
            {
                player = BasePlayer.FindByID(ulong.Parse(args[0]));
                args = Array.Empty<string>();
            }
            else if (args.Length > 1 && args[1].IsSteamId())
            {
                player = BasePlayer.FindByID(ulong.Parse(args[1]));
            }

            if (!player.IsNetworked())
            {
                Message(user, args.Length > 1 ? m("TargetNotFoundId", user.Id, args[1]) : "TargetNotFoundNoId");
                return;
            }

            var buyer = user.Player() ?? player;

            if (SaveRestore.IsSaving)
            {
                if (user.IsServer) timer.Once(1f, () => CommandBuyRaid(user, command, args));
                else Message(buyer, "BuyableServerSaving");
                return;
            }

            if (IsGridLoading())
            {
                if (user.IsServer) timer.Once(1f, () => CommandBuyRaid(user, command, args));
                else Message(buyer, "GridIsLoading");
                return;
            }

            string userid = buyer.UserIDString;
            if (_buyers.Contains(userid)) return;
            _buyers.Add(userid);
            InvokeHandler.Instance.Invoke(() => _buyers.Remove(userid), 0.5f);

            if (!bypassRestarting && ServerMgr.Instance.Restarting && ServerMgr.Instance.restartCoroutine.Current != null)
            {
                Message(buyer, buyer.IsAdmin ? "BuyableServerRestartingAdmin" : "BuyableServerRestarting");
                return;
            }

            if (config.Settings.Buyable.UsePermission && !user.HasPermission("raidablebases.buyraid"))
            {
                Message(user, "No Permission");
                return;
            }

            if (player.HasPermission("raidablebases.banned") || player.HasPermission("raidablebases.buyraid.banned"))
            {
                Message(player, player.IsAdmin ? "BannedAdmin" : "Banned", player.UserIDString);
                return;
            }

            if (!IsCopyPasteLoaded(out var error))
            {
                Message(buyer, error);
                return;
            }

            if (args.Length == 0)
            {
                if (Interface.CallHook("OnPurchaseBase", buyer, player) != null)
                {
                    return;
                }

                if (config.UI.Buyable.Enabled)
                {
                    UI.ShowBuyableUi(player, false);
                }
                else
                {
                    Message(buyer, "BuySyntax", config.Settings.BuyCommand, user.IsServer ? "ID" : user.Id);
                }
                return;
            }

            if (args[0].Equals("reset", StringComparison.CurrentCultureIgnoreCase) && config.Settings.Buyable.Cooldowns.Costs.Any)
            {
                CommandBuyRaidTakePayments(user, buyer, player, Array.Empty<string>());
                return;
            }

            if (!buyableEnabled && !buyer.HasPermission("raidablebases.canbypass"))
            {
                Message(buyer, "BuyRaidsDisabled");
                return;
            }

            string value = args[0].Replace("__", " ");
            string mode = GetRaidableMode(value, user, buyer);

            if (HasBuyableCooldown(buyer, mode))
            {
                return;
            }

            if (!CanSpawnDifficultyToday(RaidableType.Purchased, mode))
            {
                if (!CanFileMode(user, buyer)) Message(buyer, "No Permission To Buy File", value);
                else if (!FileExists(value)) Message(buyer, "FileDoesNotExist2", value);
                else Message(buyer, "BuyDifficultyNotAvailableToday", mode);
                return;
            }

            if (!config.Settings.Include.Any && (!args.Contains("free") || user == null || !user.IsAdmin))
            {
                Message(player, "NoBuyableEventsCostsEnabled");
                return;
            }

            if (mode == RaidableMode.Random || !IsDifficultyAvailable(mode, RaidableType.Purchased, false))
            {
                Message(buyer, "BuyAnotherDifficulty", value);
                return;
            }

            if (!IsDifficultyAvailable(mode, RaidableType.Purchased, true))
            {
                Message(buyer, "BuyRaidNotConfiguredProperly");
                return;
            }

            if (Get(RaidableType.Purchased) >= config.Settings.Buyable.Max)
            {
                if (config.Settings.Buyable.AutoCloseUi)
                {
                    UI.DestroyTimer(player, player.userID, UiType.Buyable);
                    CuiHelper.DestroyUi(player, "RB_UI_Buyable");
                }
                Message(buyer, "Max Events", command, config.Settings.Buyable.Max);
                return;
            }

            int max = config.Settings.Buyable.Limits.Get(mode);
            if (max < 0 || max > 0 && Get(mode, true) >= max)
            {
                Message(buyer, "Max Events", mode, max);
                return;
            }

            if (IsEventOwner(player, true))
            {
                CuiHelper.DestroyUi(player, "RB_UI_Buyable");
                Message(buyer, "BuyableAlreadyOwner");
                return;
            }

            if (IsQueued(player, GetMembers(buyer.userID)))
            {
                CuiHelper.DestroyUi(player, "RB_UI_Buyable");
                return;
            }

            if (!Buildings.Profiles.Values.Exists(profile =>
                    profile?.Options != null
                    && string.Equals(profile.Options.Mode, mode, StringComparison.OrdinalIgnoreCase)
                    && profile.Options.Permission.Has(player, RaidableType.Purchased)))
            {
                Message(player, "No Permission To Buy");
                return;
            }

            if (!isDifficultyEnabledAfterWipeOverridden && !IsDifficultyEnabledAfterWipe(mode, RaidableType.Purchased, player.UserIDString, out double remainingHours))
            {
                double remainingSeconds = remainingHours * 3600;
                Message(player, "BuyAnotherDifficultyWipeTimed", mode, FormatTime(remainingSeconds, player.UserIDString));
                return;
            }

            CuiHelper.DestroyUi(player, "RB_UI_Buyable");

            if (Interface.CallHook("OnPurchaseTakePayments", buyer, player, value, mode) is object obj && obj != null)
            {
                Message(player, obj is string str ? str : "No Permission");
                return;
            }

            CommandBuyRaidTakePayments(user, buyer, player, args, false, mode, value);
        }

        private bool RemovePlayer(BasePlayer player, bool justEntered = true, float tolerance = 1f)
        {
            if (player.IsKilled()) return false;
            var v = player.transform.position;
            foreach (var raid in Raids)
            {
                if (tolerance > raid.ProtectionRadius * 4f) continue;
                if (!raid.InRangeTolerance(v, tolerance)) continue;
                if (raid.RemovePlayer(player, raid.Location, raid.ProtectionRadius, raid.Type, justEntered))
                {
                    Message(player, "Another plugin has forcefully removed you from this event!");
                    return true;
                }
            }
            return false;
        }

        private bool HasBuyableCooldown(BasePlayer buyer, string mode, bool message = false)
        {
            if (buyer != null && !buyer.HasPermission("raidablebases.buyable.bypass.cooldown"))
            {
                if (string.IsNullOrEmpty(mode) || !RaidableModes.Contains(mode)) return false;
                if (BuyableInfo.GetTimeRemaining(this, buyer, mode, message) > 0) return true;
                if (Raids.Exists(raid => raid.HasBuyableCooldown(buyer, mode))) return true;
            }
            return false;
        }

        private bool HasBuyableCooldown(BasePlayer buyer, int level)
        {
            return GetModeFromLevel(level, out string mode) && HasBuyableCooldown(buyer, mode, true);
        }

        public void CommandBuyRaidTakePayments(IPlayer user, BasePlayer buyer, BasePlayer player, string[] args, bool reset = true, string mode = RaidableMode.Disabled, string value = null)
        {
            var payments = new Payments(buyer);
            var money = reset ? config.Settings.Buyable.Cooldowns.Costs.Money : config.Settings.Include.Economics ? config.Settings.Economics.Get(mode) : 0.0;
            var points = reset ? config.Settings.Buyable.Cooldowns.Costs.Points : config.Settings.Include.ServerRewards ? config.Settings.ServerRewards.Get(mode) : 0;
            var options = reset ? new() { config.Settings.Buyable.Cooldowns.Costs.Custom } : config.Settings.Include.Custom && config.Settings.Custom.TryGetValue(mode, out var val) ? val : new();
            var free = (args.Contains("free") && user != null && user.IsAdmin) || (user != null && !user.IsServer && user.HasPermission("raidablebases.buyraid.free")) || (buyer != null && buyer.HasPermission("raidablebases.buyraid.free"));
            if (free)
            {
                InitializeFreePayments(buyer, player, payments);
            }
            if (InvalidCustomPayment(buyer, player, payments, options, free))
            {
                return;
            }
            if (InvalidEconomicsPayment(buyer, player, payments, money, free))
            {
                return;
            }
            if (InvalidServerRewardsPayment(buyer, player, payments, points, free))
            {
                return;
            }
            if (payments.valid)
            {
                ProcessValidPayments(user, buyer, player, payments, mode, reset, free, value, args);
            }
            else
            {
                ProcessInvalidPayments(buyer, options, money, points);
            }
        }

        private void InitializeFreePayments(BasePlayer buyer, BasePlayer player, Payments payments)
        {
            payments.Custom = new(this, buyer, player, new());
            payments.Economics = new(this, buyer, player);
            payments.ServerRewards = new(this, buyer, player);
        }

        private bool InvalidCustomPayment(BasePlayer buyer, BasePlayer player, Payments payments, List<CustomCostOptions> options, bool free)
        {
            return !free && options.Count > 0 && options.Exists(o => o.isItem || o.isPlugin) && (payments.Custom = TryBuyRaidCustom(options, buyer, player)) == null;
        }

        private bool InvalidEconomicsPayment(BasePlayer buyer, BasePlayer player, Payments payments, double money, bool free)
        {
            return !free && money > 0 && (Economics.CanCall() || BankSystem.CanCall() || IQEconomic.CanCall()) && (payments.Economics = TryBuyRaidEconomics(money, buyer, player)) == null;
        }

        private bool InvalidServerRewardsPayment(BasePlayer buyer, BasePlayer player, Payments payments, int points, bool free)
        {
            return !free && points > 0 && ServerRewards.CanCall() && (payments.ServerRewards = TryBuyRaidServerRewards(points, buyer, player)) == null;
        }

        private void ProcessValidPayments(IPlayer user, BasePlayer buyer, BasePlayer player, Payments payments, string mode, bool reset, bool free, string value, string[] args)
        {
            if (!reset)
            {
                if (value != null && GetFileMode(user, buyer, value) == RaidableMode.Random) value = null;
                if (value != null && Buildings.Profiles.ContainsKey(value) && !FileExists(value)) value = null;
                if (config.Settings.Buyable.Refunds.Repeat && despawnCooldowns.TryGetValue(player.userID, out var t) && t.Item2 == mode && FileExists(t.Item1)) value = t.Item1;

                payments.type = args.Contains("pve") || player.HasPermission("raidablebases.buyraid.pveonly") ? 1 : args.Contains("pvp") || player.HasPermission("raidablebases.buyraid.pvponly") ? 2 : 0;

                payments.Take(false);

                BuyRaid(mode, payments, player, value, free);
            }
            else if (data.BuyableCooldowns.Remove(player.userID))
            {
                payments.Take(true);
                UI.UpdateUi(player, UiType.Cooldown);
                Message(buyer, "RemovedCooldownFor", player.displayName, player.UserIDString);
            }
        }

        private void ProcessInvalidPayments(BasePlayer buyer, List<CustomCostOptions> options, double money, int points)
        {
            if (options.Count > 0 && (!config.Settings.Include.Custom && options.Exists(o => o.Enabled) || !options.Exists(o => o.Enabled)))
            {
                Message(buyer, "CustomWithdrawDisabled");
            }
            else if (money > 0 && (!Economics.CanCall() && !IQEconomic.CanCall() && !BankSystem.CanCall()))
            {
                Message(buyer, "EconomicsWithdrawDisabled");
            }
            else if (points > 0 && !ServerRewards.CanCall())
            {
                Message(buyer, "ServerRewardPointsDisabled");
            }
            else if (money == 0 && config.Settings.Include.Economics && (Economics.CanCall() || IQEconomic.CanCall() || BankSystem.CanCall()))
            {
                Message(buyer, "NoBuyableEventsCostConfigured");
            }
            else if (points == 0 && config.Settings.Include.ServerRewards && ServerRewards.CanCall())
            {
                Message(buyer, "NoBuyableEventsCostConfigured");
            }
            else Message(buyer, "NoBuyableEventsCostConfigured");
        }

        public bool IsQueued(BasePlayer player, HashSet<ulong> members)
        {
            foreach (ulong member in members)
            {
                foreach (var sp in Queues.queue)
                {
                    if (sp.type == RaidableType.Purchased && sp.userid == member)
                    {
                        Message(player, player.userID == sp.userid ? "BuyableAlreadyQueued" : "BuyableAlreadyQueuedAllied");

                        return true;
                    }
                }
            }
            return false;
        }

        private void CommandBlockRaids(BasePlayer player, string command, string[] args)
        {
            float radius = 5f;
            if (args.Length != 0 && float.TryParse(args[0], out float value) && value > 5f)
            {
                radius = value;
            }
            if (config.Settings.Management.BlockedPositions.RemoveAll(x => InRange(player.transform.position, x.position, radius)) == 0)
            {
                config.Settings.Management.BlockedPositions.Add(new(player.transform.position, radius));
                Player.Message(player, $"Block added; raids will no longer spawn within {radius}m of this position");
                SaveConfig();
            }
            else Player.Message(player, "Block removed; raids are now allowed to spawn at this position");
        }

        private void CommandRaidHunter(IPlayer user, string command, string[] args)
        {
            if (RaidableModes.Count == 0 && IsGridLoading())
            {
                Message(user, "GridIsLoading");
                return;
            }

            var player = user.Player();
            bool isAdmin = user.IsServer || player.IsAdmin;
            string arg = args.Length >= 1 ? args[0].ToLower() : string.Empty;

            switch (arg)
            {
                case "pvp":
                    {
                        var nearest = GetNearestBase(player.transform.position);
                        if (nearest == null || nearest.AllowPVP || !nearest.IsParticipant(player) || !CanBypassLock(nearest, player))
                        {
                            Message(player, "CommandNotAllowed");
                            return;
                        }
                        if (nearest.Type == RaidableType.Purchased && player.HasPermission("raidablebases.buyraid.pveonly"))
                        {
                            Message(player, "CommandNotAllowed");
                            return;
                        }
                        nearest._currentSphereColor = SphereColor.None;
                        nearest.AllowPVP = true;
                        nearest.UpdateMarker();
                        nearest.CreateSpheres();
                        return;
                    }
                case "blockraids":
                    {
                        if (isAdmin)
                        {
                            CommandBlockRaids(player, command, args);
                        }
                        return;
                    }
                case "version":
                    {
                        Message(user, $"RaidableBases {Version} by nivex");
                        return;
                    }
                case "unban":
                    {
                        if (!isAdmin) return;
                        if (args.Length > 1)
                        {
                            foreach (var v in args.Skip(1))
                            {
                                if (RustCore.FindPlayerByName(v) is BasePlayer target)
                                {
                                    Revoke(target.UserIDString);
                                }
                                else if (v.IsSteamId())
                                {
                                    Revoke(v);
                                }
                            }
                        }
                        else
                        {
                            if (user.IsServer) { user.Message("You must specify a user! rb unban <steamid>"); return; }
                            Revoke(user.Id);
                        }
                        void Revoke(string userid)
                        {
                            foreach (var group in permission.GetUserGroups(userid))
                            {
                                if (permission.GroupHasPermission(group, "raidablebases.banned"))
                                {
                                    permission.RevokeGroupPermission(group, "raidablebases.banned");
                                    user.Message($"Banned permission has been removed from group: {group}");
                                }
                            }
                            if (permission.UserHasPermission(userid, "raidablebases.banned"))
                            {
                                permission.RevokeUserPermission(userid, "raidablebases.banned");
                                user.Message($"Banned permission has been revoked.");
                            }
                        }
                        return;
                    }
                case "invite":
                    {
                        CommandInvite(user, player, args);
                        return;
                    }
                case "resettime":
                    {
                        if (isAdmin)
                        {
                            data.RaidTime = DateTime.MinValue;
                        }

                        return;
                    }
                case "wipe":
                    {
                        if (isAdmin)
                        {
                            wiped = true;
                            bool ret = CheckForWipe(config.Settings.Wipe.RemoveFromList);
                            Message(user, ret ? "Wipe successful." : "There's nothing to wipe.");
                        }

                        return;
                    }
                case "revokepg":
                    {
                        if (isAdmin)
                        {
                            RevokePermissionsAndGroups(config.Settings.Wipe.Remove);
                        }

                        return;
                    }
                case "ignore_wipetime":
                    {
                        if (isAdmin)
                        {
                            isDifficultyEnabledAfterWipeOverridden = !isDifficultyEnabledAfterWipeOverridden;
                            Message(user, $"Bypassing wipe time check: {isDifficultyEnabledAfterWipeOverridden}");
                        }

                        return;
                    }
                case "ignore_restart":
                    {
                        if (isAdmin)
                        {
                            bypassRestarting = !bypassRestarting;
                            Message(user, $"Bypassing restart check: {bypassRestarting}");
                        }

                        return;
                    }
                case "savefix":
                    {
                        if (user.IsAdmin || user.HasPermission("raidablebases.allow"))
                        {
                            int removed = BaseEntity.saveList.RemoveWhere(IsKilled);

                            Message(user, $"Removed {removed} invalid entities from the save list.");

                            if (SaveRestore.IsSaving)
                            {
                                SaveRestore.IsSaving = false;
                                Message(user, "Server save has been canceled. You must type server.save again, and then restart your server.");
                            }
                            else Message(user, "Server save is operating normally.");
                        }

                        return;
                    }
                case "tp":
                    {
                        if (player.IsNetworked() && (isAdmin || user.HasPermission("raidablebases.allow")))
                        {
                            RaidableBase raid = null;
                            float num = 9999f;

                            foreach (var other in Raids)
                            {
                                float num2 = player.Distance(other.Location);

                                if (num2 > other.ProtectionRadius * 2f && num2 < num)
                                {
                                    num = num2;
                                    raid = other;
                                }
                            }

                            if (raid != null)
                            {
                                raid.Teleport(player);
                            }
                        }
                        else CommandRaidHunter(user, command, new string[1] { "teleport" });

                        return;
                    }
                case "isblocked":
                    {
                        if (isAdmin && player)
                        {
                            Vector3 v = player.transform.position;
                            if (player.IsFlying && Physics.Raycast(player.eyes.HeadRay(), out var hit, 500f, targetMask2, QueryTriggerInteraction.Ignore))
                            {
                                v = hit.point;
                                DrawText(player, 5f, Color.red, v, "!");
                            }
                            var blocked = SpawnsController.IsLocationBlocked(v);
                            Message(user, "IsLocationBlocked: " + blocked);
                            var baseName = Buildings.Profiles.FirstOrDefault().Key;
                            Queues.Test(user, baseName, v, out _, 50f);
                        }
                        return;
                    }
                case "test":
                    {
                        if (isAdmin && player != null)
                        {
                            data.Lockouts[player.UserIDString] = new();
                            data.BuyableCooldowns[player.userID] = new();
                            foreach (var mode in RaidableModes)
                            {
                                var date = DateTime.Now.AddMinutes(5 + (RaidableModes.IndexOf(mode) * 5));
                                data.Lockouts[player.UserIDString].Levels[mode] = date;
                                data.BuyableCooldowns[player.userID].Modes[mode] = date;
                            }
                            UI.UpdateUi(player, UiType.Lockout);
                            UI.UpdateUi(player, UiType.Cooldown);
                        }
                        return;
                    }
                case "rca":
                    {
                        if (player != null && isAdmin)
                        {
                            SpawnsController.GetSpawnHeight(player.transform.position, player: player);
                            if (SpawnsController.IsSafeZone(player.transform.position)) Message(user, "Safe zone position");
                            if (SpawnsController.IsMonumentPosition(player.transform.position, 0f)) Message(user, "Monument position");
                        }
                        return;
                    }
                case "grid":
                    {
                        if (player.IsNetworked() && (isAdmin || user.HasPermission("raidablebases.ddraw")))
                        {
                            ShowGrid(player, args.Length == 2 && args[1] == "all", args.Length == 2 ? args[1] : string.Empty);
                        }
                        return;
                    }
                case "ladder":
                case "lifetime":
                    {
                        ShowLadder(user, args);
                        return;
                    }
                case "queue_clear":
                    {
                        if (isAdmin)
                        {
                            int num = Queues.queue.Count;
                            Queues.RestartCoroutine();
                            Message(user, $"Cleared and refunded {num} in the queue.");
                        }
                        return;
                    }
                case "resetui":
                    {
                        UiHandler.DestroyUi(player);
                        if (UI.Offsets.TryGetValue(player.userID, out var ui))
                        {
                            if (args.Length == 1) UI.Offsets.Remove(player.userID);
                            if (args.Contains("buyable")) ui.Remove(UiType.Buyable);
                            if (args.Contains("cooldown")) ui.Remove(UiType.Cooldown);
                            if (args.Contains("delay")) ui.Remove(UiType.Delay);
                            if (args.Contains("lockout")) ui.Remove(UiType.Lockout);
                            if (args.Contains("status")) ui.Remove(UiType.Status);
                            Message(player, "ResetUI");
                        }
                        UI.UpdateUi(player, UiType.Cooldown);
                        UI.UpdateUi(player, UiType.Delay);
                        UI.UpdateUi(player, UiType.Lockout);
                        UI.UpdateUi(player, UiType.Status);
                        Message(player, "Your UI settings have been reset to defaults.");
                        return;
                    }
                case "setui":
                    {
                        HandleUiCommand(player, args);
                        return;
                    }
                case "hint":
                    {
                        HandleHintsCommand(player);
                        return;
                    }
            }

            if (config.RankedLadder.Enabled)
            {
                ShowLadder(user);
            }

            if (Automated.IsScheduledEnabled && (Raids.Count == 0 || !Automated.IsMaintainedEnabled) && GridController.GetRaidTime() > 0)
            {
                ShowNextScheduledEvent(user);
            }

            if (player.IsNetworked())
            {
                DrawRaidLocations(player, isAdmin || player.HasPermission("raidablebases.ddraw"));
            }
        }

        private readonly Dictionary<string, UiType> uiMappings = new()
        {
            { "buyable", UiType.Buyable },
            { "cooldown", UiType.Cooldown },
            { "delay", UiType.Delay },
            { "lockout", UiType.Lockout },
            { "status", UiType.Status }
        };

        private void SaveUiOffset(BasePlayer player, UiType uiType, UiOffsets os)
        {
            switch (uiType)
            {
                case UiType.Buyable:
                    (config.UI.Buyable.OffsetMin, config.UI.Buyable.OffsetMax) = (os.Min, os.Max);
                    break;
                case UiType.Cooldown:
                    (config.UI.BuyableCooldowns.OffsetMin, config.UI.BuyableCooldowns.OffsetMax) = (os.Min, os.Max);
                    break;
                case UiType.Delay:
                    (config.UI.Delay.OffsetMin, config.UI.Delay.OffsetMax) = (os.Min, os.Max);
                    break;
                case UiType.Lockout:
                    (config.UI.Lockout.OffsetMin, config.UI.Lockout.OffsetMax) = (os.Min, os.Max);
                    break;
                case UiType.Status:
                    (config.UI.Status.OffsetMin, config.UI.Status.OffsetMax) = (os.Min, os.Max);
                    break;
            }

            Message(player, $"You have saved the default offsets for the {uiType} UI.");

            SaveConfig();

            foreach (var data in UI.Offsets.ToList())
            {
                if (data.Value.ContainsKey(uiType))
                {
                    data.Value[uiType] = new UiOffsets(os.Min, os.Max);
                }
            }

            UI.SaveOffsetData();

            foreach (var target in BasePlayer.activePlayerList)
            {
                UI.UpdateUi(target, uiType);
            }
        }

        private bool CanBypassLock(RaidableBase raid, BasePlayer player)
        {
            return raid.ownerId == 0uL || raid.BypassUseOwners() || raid.ownerId.IsSteamId() || raid.IsAlly(player);
        }

        public void HandleHintsCommand(BasePlayer player)
        {
            var nearest = GetNearestBase(player.transform.position);
            if (nearest == null)
            {
                Message(player, "TargetTooFar");
                return;
            }

            var opt = nearest.Options.DrawLoot;
            bool canBypass = opt.CanBypass && nearest.CanBypass(player);

            if (!canBypass)
            {
                if (nearest.HintCooldowns.Count > 0)
                {
                    Message(player, "CommandNotAllowed");
                    return;
                }

                if (!nearest.Options.Permission.Has(player, nearest.Type) || !string.IsNullOrWhiteSpace(opt.Permission) && !player.HasPermission(opt.Permission))
                {
                    Message(player, "No Permission");
                    return;
                }

                if (!opt.Enabled || opt.DrawTime <= 0f)
                {
                    Message(player, "CommandNotAllowed");
                    return;
                }

                if (!nearest.IsParticipant(player) || !CanBypassLock(nearest, player))
                {
                    Message(player, "OwnerLocked");
                    return;
                }

                if (!nearest.RequiredLootPercentageMet(opt.RequiredLootPercentage, out double percentageMet))
                {
                    Message(player, "Hints Loot Requirement", Math.Round(percentageMet, 2), opt.RequiredLootPercentage);
                    return;
                }

                if (opt.Cooldown > 0)
                {
                    nearest.AddHintCooldown(player, opt.Cooldown);
                }
            }

            float drawTime = Mathf.Max(1f, opt.DrawTime);
            int amount = canBypass ? 0 : opt.MaxContainersToDraw;
            using var objects = DisposableList<object[]>();

            foreach (var container in nearest._containers)
            {
                if (IsContainerKilled(container) || container.inventory.IsEmpty() || opt.CupboardOnly && !(container is BuildingPrivlidge))
                {
                    continue;
                }

                string text = opt.ShowCupboardQuantity ? $"<size={opt.FontSize}>{container.inventory.itemList.Count}</size>" : $"<size={opt.FontSize}>X</size>";
                Color color = !opt.YellowCupboard || container is not BuildingPrivlidge ? Color.green : Color.yellow;

                objects.Add(new object[] { drawTime, color, container.CenterPoint(), text });

                if (amount > 0 && objects.Count >= amount)
                {
                    break;
                }
            }

            if (objects.Count > 0)
            {
                AdminCommand(player, () =>
                {
                    foreach (var obj in objects)
                    {
                        player.SendConsoleCommand("ddraw.text", obj[0], obj[1], obj[2], obj[3]);
                    }
                });
            }

            Message(player, objects.Count > 0 ? "Hints Drawn On Screen" : "Hints None Available");

            Interface.CallHook("OnRaidableBaseHint", player, nearest.Location, nearest.ProtectionRadius, nearest.Options.Level, nearest.GetLootAmountRemaining(), nearest.GetOwner(), nearest.GetRaiders());
        }

        public void HandleUiCommand(BasePlayer player, string[] args)
        {
            if (!isInitialized || args.Length == 1)
            {
                Message(player, "Invalid argument!");
                return;
            }

            UiHandler.DestroyUi(player);

            if (UI.Offsets.TryGetValue(player.userID, out var ui))
            {
                foreach (var (arg, uiType) in uiMappings)
                {
                    if (args.Contains(arg) && ui.TryGetValue(uiType, out var os))
                    {
                        SaveUiOffset(player, uiType, os);
                        return;
                    }
                }

                Message(player, "No matching UI type found for the provided arguments.");
            }
            else
            {
                Message(player, "No UI offsets found for your user ID.");
            }
        }

        private void CommandInvite(IPlayer user, BasePlayer player, string[] args)
        {
            if (args.Length < 2) { Message(user, "Invite Usage", config.Settings.HunterCommand); return; }
            if (!(RustCore.FindPlayer(args[1]) is BasePlayer target)) { Message(user, "TargetNotFoundId", args[1]); return; }
            var isAllowed = user.IsServer || player.IsAdmin || player.HasPermission("fauxadmin.allowed");
            var raid = isAllowed ? GetNearestBase(target.transform.position) : Raids.FirstOrDefault(x => x.ownerId.IsSteamId() && (x.ownerId == player.userID || x.IsAlly(player.userID, x.ownerId)));
            if (raid == null) { Message(user, isAllowed ? "TargetTooFar" : "Invite Ownership Error"); return; }
            if (!isAllowed && !player.HasPermission("raidablebases.invitecommand") && !raid.IsAlly(player.userID, target.userID)) { Message(user, "Invite Not Ally"); return; }
            if (!isAllowed && !raid.IsPayLocked && raid.HasLockout(target)) { Message(user, "Invite Lockout Error"); Message(target, "Invite Failed"); return; }
            if (!raid.raiders.TryGetValue(target.userID, out var raider)) raid.raiders[target.userID] = raider = new(target);
            if (InRange(raid.Location, target.transform.position, raid.ProtectionRadius * 1.5f)) raider.lastActiveTime = Time.time;
            if (user.IsServer || player.IsAdmin || user.HasPermission("raidablebases.allow")) Message(user, $"You can use this command to set them as the owner of this raid: {config.Settings.EventCommand} setowner {target.userID}");
            raider.IsAlly = true;
            raider.IsAllowed = true;
            raider.IsParticipant = true;
            Message(target, "Invite Allowed", user.Name);
            Message(user, "Invite Success", target.displayName);
        }

        protected void DrawRaidLocations(BasePlayer player, bool hasPerm)
        {
            if (!player.HasPermission("raidablebases.block.filenames") && !player.IsAdmin && !player.IsDeveloper)
            {
                foreach (var raid in Raids)
                {
                    if (InRange2D(raid.Location, player.transform.position, 100f))
                    {
                        Player.Message(player, $"{raid.BaseName} @ {raid.Location} ({MapHelper.PositionToString(raid.Location)})");
                    }
                }
            }

            if (hasPerm)
            {
                AdminCommand(player, () =>
                {
                    foreach (var raid in Raids)
                    {
                        int num = BasePlayer.activePlayerList.Count(x => x.IsNetworked() && x.Distance(raid.Location) <= raid.ProtectionRadius * 3f);
                        int distance = Mathf.CeilToInt(player.transform.position.Distance(raid.Location));
                        string message = mx("RaidMessage", player.UserIDString, distance, num);
                        string flag = mx(raid.GetAllowKey(), player.UserIDString);

                        DrawText(player, 15f, Color.yellow, raid.Location, string.Format("<size=24>{0}{1} {2} [{3} {4}] {5}</size>", raid.BaseName, flag, raid.Type + ":" + raid.Mode(player.UserIDString, true), message, FormatGridReference(player, raid.Location), raid.Location));

                        foreach (var ri in raid.raiders.Values.Where(x => x.IsAlly && x.player.IsNetworked()))
                        {
                            DrawText(player, 15f, Color.yellow, ri.player.transform.position, $"<size=24>{mx("Ally", player.UserIDString).Replace(":", string.Empty)}</size>");
                        }

                        if (raid.ownerId.IsSteamId() && raid.GetOwner() is BasePlayer owner)
                        {
                            DrawText(player, 15f, Color.yellow, owner.transform.position, $"<size=24>{mx("Owner", player.UserIDString).Replace(":", string.Empty)}</size>");
                        }
                    }
                });
            }
        }

        protected void ShowNextScheduledEvent(IPlayer user)
        {
            string message;
            double time = GridController.GetRaidTime();
            int count = config.Settings.Schedule.GetPlayerCount();

            if (count < config.Settings.Schedule.PlayerLimitMin)
            {
                message = mx("Not Enough Online", user.Id, config.Settings.Schedule.PlayerLimitMin);
            }
            else if (count > config.Settings.Schedule.PlayerLimitMax)
            {
                message = mx("Too Many Online", user.Id, config.Settings.Schedule.PlayerLimitMax);
            }
            else message = FormatTime(time, user.Id);

            QueueNotification(user, "Next", message);
        }

        protected void ShowLadder(IPlayer user)
        {
            if (!config.RankedLadder.Enabled || config.RankedLadder.Top < 1)
            {
                return;
            }

            using var modes = DisposableList<(int level, string template)>();
            using var sb = DisposableBuilder.Get();
            var info = data.GetPlayerInfo(user.Id);
            var points = mx("Points", user.Id);
            var total = mx("Total", user.Id);

            foreach (var pair in info.Modes)
            {
                modes.Add((GetLevelFromMode(pair.Key.Replace("Points", "").Replace("Total", "")), $"{pair.Key} (<color=#FFFF00>{pair.Value}</color>)"));
            }

            modes.Sort((a, b) => a.level.CompareTo(b.level));

            for (int i = 0; i < modes.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(modes[i].template.Replace("Points", " " + points).Replace("Total", total + " "));
            }

            QueueNotification(user, "RankedPoints", info.Raids, info.Points, info.TotalRaids, info.TotalPoints, user.IsServer ? rf(sb.ToString()) : sb.ToString());
            QueueNotification(user, "RankedWins2", config.Settings.HunterCommand);
        }

        protected void ShowLadder(IPlayer user, string[] args)
        {
            if (!config.RankedLadder.Enabled || config.RankedLadder.Top < 1)
            {
                return;
            }

            if (args.Contains("resetme"))
            {
                if (data.Players.ContainsKey(user.Id))
                {
                    data.Players[user.Id] = new();
                }
                QueueNotification(user, "Your ranked stats have been reset.");
                return;
            }

            using var sb = DisposableBuilder.Get();
            using var ladder = DisposableList<(PlayerInfo info, string userid, int raids, int points)>();
            bool isByWipe = args[0].Equals("ladder", StringComparison.OrdinalIgnoreCase);
            string mode = args.Length == 2 ? GetRaidableMode(args[1]) : RaidableMode.Points;

            foreach (var (userid, info) in data.Players)
            {
                (int raids, int points) = mode switch
                {
                    RaidableMode.Points =>
                    (
                        isByWipe ? info.Raids : info.TotalRaids,
                        isByWipe ? info.Points : info.TotalPoints
                    ),
                    _ =>
                    (
                        isByWipe ? info.Modes.GetValueOrDefault(mode) : info.Modes.GetValueOrDefault("Total" + mode),
                        isByWipe ? info.Modes.GetValueOrDefault(mode + "Points") : info.Modes.GetValueOrDefault("Total" + mode + "Points")
                    )
                };

                if (points > 0)
                {
                    ladder.Add((info, userid, raids, points));
                }
            }

            if (ladder.Count < 30 && ConVar.Server.hostname.EndsWith("ed Test"))
            {
                for (int i = 0; i < 30 - ladder.Count; i++)
                {
                    var userid = UnityEngine.Random.Range(1000, 9999999);
                    PlayerInfo info = new() { Name = RandomUsernames.Get(userid).ToFriendlyJson() };
                    int raids = UnityEngine.Random.Range(1, 5);
                    int points = UnityEngine.Random.Range(15, 60);
                    ladder.Add(new(info, userid.ToString(), raids, points));
                }

                PlayerInfo info2 = new() { Name = user.Name.ToFriendlyJson() };
                ladder.Insert(15, new(info2, user.Id, 5, 15));
            }

            if (ladder.Count == 0)
            {
                QueueNotification(user, "Ladder Insufficient Players");
                return;
            }

            string header = mx(isByWipe ? "RankedLadder" : "RankedTotal", user.Id, config.RankedLadder.Top, mode);

            if (!string.IsNullOrWhiteSpace(header))
            {
                sb.AppendLine(header);
            }

            ladder.Sort((a, b) => b.points.CompareTo(a.points));

            int me = ladder.FindIndex(e => e.userid == user.Id);
            int top = Math.Min(config.RankedLadder.Top, ladder.Count);
            for (int i = 0; i < ladder.Count; ++i)
            {
                if (i >= top && i != me)
                    continue;

                int rank = i + 1;
                var (info, userid, raids, points) = ladder[i];
                string name = string.IsNullOrWhiteSpace(info.Name) ? covalence.Players.FindPlayerById(userid)?.Name ?? userid : info.Name.FromFriendlyJson();

                if (string.IsNullOrWhiteSpace(info.Name))
                {
                    info.Name = name.ToFriendlyJson();
                }

                sb.AppendLine(mx("NotifyPlayerFormat", user.Id))
                  .Replace("{rank}", $"{rank}")
                  .Replace("{name}", $"{name}")
                  .Replace("{value}", $"{raids}")
                  .Replace("{points}", $"{points}");
            }

            QueueNotification(user, sb.ToString());
        }

        private int GetLevelFromMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
            {
                return -1;
            }
            foreach (var profile in Buildings.Profiles.Values)
            {
                if (string.IsNullOrWhiteSpace(profile.Options.Mode))
                {
                    continue;
                }
                if (!IsModeValid(profile.Options.Mode))
                {
                    continue;
                }
                if (profile.Options.Mode.Equals(mode, StringComparison.OrdinalIgnoreCase))
                {
                    return profile.Options.Level;
                }
            }
            return -1;
        }

        private bool GetModeFromLevel(int val, out string mode)
        {
            return GetModeFromLevel(val.ToString(), out mode);
        }

        private bool GetModeFromLevel(string value, out string mode)
        {
            mode = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
            foreach (var profile in Buildings.Profiles.Values)
            {
                string m = profile?.Options?.Mode;
                if (string.IsNullOrWhiteSpace(m) || !IsModeValid(m))
                {
                    continue;
                }
                if (profile.Options.Level.ToString() == value)
                {
                    mode = m;
                    return true;
                }
                if (profile.Options.Mode.Equals(value, StringComparison.OrdinalIgnoreCase))
                {
                    mode = m;
                    return true;
                }
            }
            return false;
        }

        private string GetRaidableMode(string value, IPlayer caller = null, BasePlayer buyer = null)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return RaidableMode.Random;
            }

            if (GetModeFromLevel(value, out var modeFromLevel))
            {
                return modeFromLevel;
            }

            foreach (var mode in GetRaidableModes())
            {
                if (value.Equals(mode, StringComparison.OrdinalIgnoreCase))
                {
                    return mode;
                }
            }
            
            return GetFileMode(caller, buyer, value);
        }

        private bool IsRaidableMode(string value) => GetRaidableMode(value) != RaidableMode.Random;

        private string GetFileMode(IPlayer caller, BasePlayer buyer, string value) => CanFileMode(caller, buyer) && Get(value, out (string key, BaseProfile profile) val) ? val.profile.Options.Mode : RaidableMode.Random;

        private bool CanFileMode(IPlayer caller, BasePlayer buyer) => config.Settings.Buyable.FileMode || caller != null && caller.IsServer || buyer.HasPermission("raidablebasesbuyableui.spawn.filenames") || buyer.HasPermission("raidablebases.buyable.spawn.filenames");

        [HookMethod("GetRaidableModes")]
        public List<string> GetRaidableModes()
        {
            if (RaidableModes.Count == 0)
            {
                var mapping = new Dictionary<string, int>();

                foreach (var profile in Buildings.Profiles.Values)
                {
                    if (string.IsNullOrWhiteSpace(profile.Options.Mode))
                        continue;

                    if (!IsModeValid(profile.Options.Mode))
                        continue;

                    int level = profile.Options.Level;
                    if (level == -1)
                        level = mapping.Count;

                    mapping.TryAdd(profile.Options.Mode, level);
                }

                foreach (var key in mapping.Keys)
                {
                    if (!arguments.Contains(key))
                    {
                        arguments.Add(key);
                    }
                }

                RaidableModes.AddRange(mapping.Keys);
                RaidableModes.Sort((a, b) => mapping[a].CompareTo(mapping[b]));
            }

            return RaidableModes;
        }

        private bool Get(string baseName, out (string, BaseProfile) val)
        {
            foreach (var (key, profile) in Buildings.Profiles)
            {
                if (key.Equals(baseName, StringComparison.OrdinalIgnoreCase) || profile.Options.AdditionalBases.Exists(extra => extra.Key.Equals(baseName, StringComparison.OrdinalIgnoreCase)))
                {
                    val = (key, profile);
                    return true;
                }
            }
            val = default;
            return false;
        }

        protected void ShowGrid(BasePlayer player, bool showAll, string profile)
        {
            AdminCommand(player, () =>
            {
                foreach (var (type, spawns) in GridController.Spawns)
                {
                    ShowSpawns(player, spawns, showAll, type == RaidableType.Grid ? 500f : 0f, profile);
                }

                foreach (var raid in Raids)
                {
                    if (raid != null && raid.spawns != null && raid.spawns.IsCustomSpawn)
                    {
                        ShowSpawns(player, raid.spawns, showAll, 0f, profile);
                    }
                }

                foreach (var cmi in SpawnsController.Monuments)
                {
                    DrawSphere(player, 30f, Color.blue, cmi.position, cmi.radius);
                    DrawText(player, 30f, Color.cyan, cmi.position, $"<size=16>{cmi.text} ({cmi.radius})</size>");
                }
            });
        }

        private static void ShowSpawns(BasePlayer player, RaidableSpawns spawns, bool showAll, float distance, string profile)
        {
            bool b = !string.IsNullOrEmpty(profile);
            foreach (var rsl in spawns.Spawns.Union(spawns.Seabed))
            {
                if (showAll || distance <= 0f || InRange2D(rsl.Location, player.transform.position, distance))
                {
                    if (!b) DrawText(player, 30f, Color.green, rsl.Location, "X");
                    if (!showAll && !b) continue;
                    var p = GetProfile(player, spawns, rsl);
                    if (p == null || b && profile != p.ProfileName) continue;
                    DrawSphere(player, 30f, Color.green, rsl.Location, p.Options.ProtectionRadius(RaidableType.None));
                    DrawText(player, 30f, Color.green, rsl.Location, "X");
                }
            }

            foreach (CacheType cacheType in Enum.GetValues(typeof(CacheType)))
            {
                (Color color, string text) = cacheType switch
                {
                    CacheType.Generic => (Color.red, "X"),
                    CacheType.Temporary => (Color.cyan, "C"),
                    CacheType.Privilege => (Color.yellow, "TC"),
                    CacheType.Seabed or CacheType.Submerged => (Color.blue, "W"),
                    _ => (Color.red, "X")
                };

                foreach (var rsl in spawns.Inactive(cacheType))
                {
                    if (showAll || distance <= 0f || InRange2D(rsl.Location, player.transform.position, distance))
                    {
                        if (!b) DrawText(player, 30f, color, rsl.Location, text);
                        if (!showAll && !b) continue;
                        var p = GetProfile(player, spawns, rsl);
                        if (p == null || b && profile != p.ProfileName) continue;
                        DrawSphere(player, 30f, Color.green, rsl.Location, p.Options.ProtectionRadius(RaidableType.None));
                        DrawText(player, 30f, Color.green, rsl.Location, "X");
                    }
                }
            }
        }

        private static BaseProfile GetProfile(BasePlayer player, RaidableSpawns spawns, RaidableSpawnLocation rsl)
        {
            foreach (var profile in spawns.Instance.Buildings.Profiles.Values)
            {
                foreach (var col in profile.Spawns.Values)
                {
                    foreach (var spawn in col.Spawns)
                    {
                        if (spawn == rsl)
                        {
                            return profile;
                        }
                    }
                }
            }
            return null;
        }

        private void CommandRaidBase(IPlayer user, string command, string[] args)
        {
            var player = user.Player();
            bool isAllowed = user.IsServer || player.IsAdmin || user.HasPermission("raidablebases.allow");
            if (!CanCommandContinue(player, user, isAllowed, args))
            {
                return;
            }
            if (RaidableModes.Count == 0)
            {
                Message(user, "GridIsLoading");
                return;
            }
            if (command == config.Settings.EventCommand) // rbe
            {
                ProcessEventCommand(user, player, isAllowed, args);
            }
            else if (command == config.Settings.ConsoleCommand) // rbevent
            {
                ProcessConsoleCommand(user, player, isAllowed, args);
            }
        }

        protected void ProcessEventCommand(IPlayer user, BasePlayer player, bool isAllowed, string[] args) // rbe
        {
            if (!isAllowed || !player.IsNetworked())
            {
                return;
            }

            var baseName = Array.Find(args, FileExists);
            var mode = GetRaidableMode(Array.Find(args, IsRaidableMode));
            var (key, profile) = GetBuilding(RaidableType.Manual, mode, baseName, null);

            if (!IsProfileValid(key, profile, true, RaidableType.Manual))
            {
                QueueNotification(user, profile == null ? "BuildingNotConfigured" : GetDebugMessage(mode, RaidableType.Manual, false, true, user.Id, key, profile.Options));
                return;
            }

            if (!Physics.Raycast(player.eyes.HeadRay(), out var hit, isAllowed ? Mathf.Infinity : 100f, targetMask2, QueryTriggerInteraction.Ignore))
            {
                QueueNotification(user, "LookElsewhere");
                return;
            }

            var safeRadius = Mathf.Max(M_RADIUS * 2f, profile.Options.ArenaWalls.Radius);
            var safe = player.IsAdmin || SpawnsController.IsAreaSafe(hit.point, 0f, safeRadius, safeRadius, safeRadius, manualMask, false, out _, RaidableType.Manual, profile.Options.CustomSpawns);

            if (!safe && !player.IsFlying && InRange(player.transform.position, hit.point, 50f))
            {
                QueueNotification(user, "PasteIsBlockedStandAway");
                return;
            }

            bool pasted = false;

            if (safe && (isAllowed || !SpawnsController.IsMonumentPosition(hit.point, profile.Options.ProtectionRadius(RaidableType.Manual))))
            {
                var spawns = GridController.Spawns.Values.FirstOrDefault(s => s.GetLocations(CacheType.Generic).Exists(t => InRange2D(t.Location, hit.point, M_RADIUS)) || s.GetLocations(CacheType.Seabed).Exists(t => InRange2D(t.Location, hit.point, M_RADIUS)));
                var point = hit.point + new Vector3(0f, profile.Options.Setup.PasteHeightAdjustment);
                RandomBase rb = new();
                rb.Instance = this;
                rb.BaseName = key;
                rb.Profile = profile;
                rb.Position = point;
                rb.type = RaidableType.Manual;
                rb.spawns = spawns ??= new(this);
                rb.payments = new();
                rb.payments.admin = player.IsAdmin;
                rb.pasteData = GetPasteData(key);
                ParseListedOptions(rb);
                if (profile.Options.Setup.ForcedHeight != -1)
                {
                    point.y = profile.Options.Setup.ForcedHeight;
                }
                point.y += rb.baseHeight;
                if (PasteBuilding(rb))
                {
                    DrawText(player, 10f, Color.red, point, rb.BaseName);
                    if (ConVar.Server.hostname.Contains("Test Server"))
                    {
                        DrawSphere(player, 30f, Color.blue, point, rb.pasteData.radius);
                    }
                    pasted = true;
                }
            }
            else QueueNotification(user, "PasteIsBlocked");

            if (!pasted && Queues.Messages.Any())
            {
                QueueNotification(user, IsGridLoading() ? "GridIsLoading" : Queues.Messages.GetLast(user.Id));
            }
        }

        protected void ProcessConsoleCommand(IPlayer user, BasePlayer player, bool isAllowed, string[] args) // rbevent
        {
            if (IsGridLoading())
            {
                int count = GridController.Spawns.TryGetValue(RaidableType.Grid, out var value) ? value.Spawns.Count : 0;
                QueueNotification(user, "GridIsLoadingFormatted", (Time.realtimeSinceStartup - GridController.gridTime).ToString("N02"), count);
                return;
            }
            if (isAllowed)
            {
                int events = 1;
                if (args.Length == 2) { if (!int.TryParse(args[1], out events)) events = 1; }
                for (int i = 0; i < events; i++) { SpawnRandomBase(RaidableType.Manual, GetRaidableMode(Array.Find(args, IsRaidableMode)), Array.Find(args, FileExists), isAllowed, null, null, isAllowed && user.IsConnected ? user : null); }
                Message(player, "BaseQueued", Queues.queue.Count);
            }
        }

        private bool CanCommandContinue(BasePlayer player, IPlayer user, bool isAllowed, string[] args)
        {
            if (HandledCommandArguments(player, user, isAllowed, args))
            {
                return false;
            }

            if (!IsCopyPasteLoaded(out var error))
            {
                Message(user, error);
                return false;
            }

            if (!(user.IsServer || player.IsAdmin || user.HasPermission("raidablebases.bypassmaxmanualeventlimit")) && Get(RaidableType.Manual) >= config.Settings.Manual.Max)
            {
                QueueNotification(user, "Max Events", RaidableType.Manual, config.Settings.Manual.Max);
                return false;
            }

            return true;
        }

        private bool HandledCommandArguments(BasePlayer player, IPlayer user, bool isAllowed, string[] args)
        {
            if (args.Length == 0)
            {
                return false;
            }

            switch (args[0].ToLower())
            {
                case "despawn":
                    if (player.IsNetworked() && (isAllowed || player.HasPermission("raidablebases.despawn.buyraid")))
                    {
                        DespawnBase(player, isAllowed);
                    }
                    return true;
                case "draw":
                    if (player.IsNetworked())
                    {
                        DrawSpheres(player, isAllowed);
                    }
                    return true;
                case "checkflat":
                    {
                        if (!isAllowed) return false;
                        if (args.Length != 2 || !float.TryParse(args[1], out var radius) || radius <= 0f) radius = 20f;
                        Message(user, SpawnsController.IsObstructed(player.transform.position, radius, 2.5f, -1f, player.IsHeadUnderwater(), player) ? "Obstruction test failed" : "Obstruction test passed");
                        var landLevel = SpawnsController.GetLandLevel(player.transform.position, radius, 5f, player.IsHeadUnderwater(), player, player.UserIDString);
                        DrawText(player, 30f, Color.red, player.transform.position, $"{landLevel.y - landLevel.x:N01}");
                        Message(user, SpawnsController.IsFlatTerrain(landLevel, 2.5f) ? "Terrain is flat" : "Terrain is not flat");
                        return true;
                    }
                case "debug":
                    {
                        if (!isAllowed) return false;
                        DebugMode = !DebugMode;
                        Queues.Messages._user = DebugMode ? user : null;
                        Message(user, $"Debug mode (v{Version}): {DebugMode}");
                        ConfigCheckFrames(user);
                        if (DebugMode)
                        {
                            if (!_ownershipReady) Message(user, "Steam Inventory definitions are not yet available.");
                            if (IsGridBroken())
                            {
                                Message(user, "Another plugin has prevented the grid from loading? It is not functioning, it has been canceled by another process.");
                            }
                            if (GridController.step != 0)
                            {
                                if (GridController.step == int.MaxValue) Message(user, "Grid has not initialied.");
                                else if (GridController.step > 0) Message(user, $"Grid last completed step: {GridController.step - 1} with {GridController.progress}/{GridController.progressTotal} read");
                            }
                            TimeSpan uptime = TimeSpan.FromSeconds(Time.realtimeSinceStartup);
                            Message(user, $"Server Uptime: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s");
                            Message(user, $"Scheduled Events Running: {Automated._scheduledCoroutine != null}");
                            Message(user, $"Maintained Events Running: {Automated._maintainedCoroutine != null}");
                            Message(user, $"Queues Pending: {Queues.queue.Count}");
                            if (!AnyCopyPasteFileExists && !GridController.BadFrameRate)
                            {
                                Message(user, "No copypaste file in any profile exists!");
                            }
                            if (Queues.Messages.Any())
                            {
                                Message(user, $"DEBUG: Last messages:");
                                Queues.Messages.PrintAll(user);
                            }
                            else Message(user, "No debug messages.");
                            if (exConf is JsonException)
                            {
                                Message(user, $"{exConf.Message}\n\n\nYour config contains a json error!");
                            }
                            foreach (var error in profileErrors)
                            {
                                Message(user, $"Json error found in {error}");
                            }
                            int points = 0;
                            foreach (var (type, spawns) in GridController.Spawns)
                            {
                                if (spawns.Spawns.Count > 0)
                                {
                                    Message(user, $"Potential points on {type}: {spawns.Spawns.Count} available/{spawns.Cached.Select(x => x.Value).Count()} with temporary holds.");
                                    points += spawns.Spawns.Count;
                                }
                            }
                            if (IsGridBroken())
                            {
                                if (points > 1000) { GridController.gridCoroutine = null; Puts("Grid activated with {0} points, you need to find whatever plugin you have that's breaking this plugin. There's no reason the grid should partially load then stop without finishing.", points); }
                                else Message(user, "You must reload RaidableBases or type rb.reloadconfig to load the grid.");
                            }

                        }
                        return true;
                    }
                case "kill_cleanup":
                    {
                        if (!isAllowed || player == null) return false;
                        var num = 0;
                        using var tmp = FindEntitiesOfType<BaseEntity>(player.transform.position, 100f);
                        foreach (var entity in tmp)
                        {
                            if (entity.OwnerID == 0 && IsKillableEntity(entity))
                            {
                                entity.SafelyKill();
                                num++;
                            }
                        }
                        ;
                        if (num == 0) Message(user, "You must use the command near the base that you want to despawn. It cannot be owned by a player.");
                        else Message(user, $"Kill sent for {num} entities.");
                        return true;
                    }
                case "despawnall":
                case "despawn_inactive":
                    {
                        if (isAllowed && Raids.Count > 0)
                        {
                            DespawnAll(args[0].ToLower() == "despawn_inactive");
                            Puts(mx("DespawnedAll", null, user.Name));
                        }

                        return true;
                    }
                case "generateloot":
                    {
                        if (isAllowed)
                        {
                            string mode = args.Length > 1 ? GetRaidableMode(args[1]) : RaidableMode.Random;
                            if (mode == RaidableMode.Random) mode = GetRaidableModes().GetRandom();
                            RaidableBase.GenerateLoot(this, user, mode, args);
                        }
                        return true;
                    }
                case "active":
                    {
                        if (!isAllowed) return false;

                        var sb = new StringBuilder();

                        sb.AppendLine($"Queue: {Queues.queue.Count}, Raids: {Raids.Count}");

                        foreach (var spq in Queues.queue)
                        {
                            if (spq.isBuyableEvent) sb.AppendLine($"{spq.type} ({spq.options.Mode}) with {spq.attempts} attempts ({spq.username}/{spq.userid})");
                            else sb.AppendLine($"{spq.type} ({spq.options.Mode}) with {spq.attempts} attempts");
                        }

                        foreach (var raid in Raids)
                        {
                            sb.AppendLine($"{raid.Type}: {raid.Options.Mode} ({(raid.AllowPVP ? "PVP" : "PVE")}) is {raid.GetPercentComplete()}% done with {raid.BaseName} at {raid.Location} in {PositionToGrid(raid.Location, false)} ({raid.GetPercentCompleteMessage()}) {raid.DespawnString}");
                        }

                        foreach (var (type, spawns) in GridController.Spawns)
                        {
                            sb.AppendLine($"{type} with {spawns.Spawns.Count} spawns and {spawns.Cached.Sum(x => x.Value.Count)} cached");
                        }

                        if (config.Settings.Management.RequireAllSpawned)
                        {
                            if (data.Cycle._buildings.Count > 0)
                            {
                                sb.AppendLine("Bases that cannot respawn yet:");
                                foreach (var (mode, buildings) in data.Cycle._buildings)
                                {
                                    sb.AppendLine($"{mode}: {string.Join(", ", buildings)}");
                                }
                            }

                            sb.AppendLine().Append("Bases that can spawn in the current rotation:");

                            var current = RaidableMode.Random;

                            foreach (var (key, profile) in Buildings.Profiles)
                            {
                                foreach (var extra in profile.Options.AdditionalBases.Keys)
                                {
                                    if (FileExists(extra) && data.Cycle.CanSpawn(RaidableType.Maintained, profile.Options.Mode, extra, player))
                                    {
                                        if (current != profile.Options.Mode)
                                        {
                                            current = profile.Options.Mode;
                                            sb.AppendLine();
                                        }
                                        sb.Append(extra).Append(' ');
                                    }
                                }
                            }
                        }

                        Message(user, sb.ToString());

                        return true;
                    }
                case "expire":
                case "resetcooldown":
                    {
                        if (!isAllowed) return false;
                        if (args.Length >= 2)
                        {
                            var target = RustCore.FindPlayer(args[1]);

                            if (!target.IsNull())
                            {
                                if (args.Length == 2 || args[2] == "buyable")
                                {
                                    foreach (var raid in Raids)
                                    {
                                        raid.cooldowns.Remove(target.userID);
                                    }
                                    data.BuyableCooldowns.Remove(target.userID);
                                    UI.UpdateUi(target, UiType.Cooldown);
                                    Message(user, "RemovedCooldownFor", target.displayName, target.UserIDString);
                                }
                                if (args.Length == 2 || args[2] == "lockout")
                                {
                                    data.Lockouts.Remove(target.UserIDString);
                                    UI.UpdateUi(target, UiType.Lockout);
                                    QueueNotification(user, "RemovedLockFor", target.displayName, target.UserIDString);
                                }
                            }
                            return true;
                        }
                        Message(user, "Target not found");
                        return true;
                    }
                case "expireall":
                case "resetall":
                    {
                        if (isAllowed)
                        {
                            data.BuyableCooldowns.Clear();
                            data.Lockouts.Clear();
                            foreach (var target in BasePlayer.activePlayerList)
                            {
                                UI.UpdateUi(target, UiType.Cooldown);
                                UI.UpdateUi(target, UiType.Lockout);
                            }
                            Puts($"All cooldowns and lockouts have been reset by {user.Name} ({user.Id})");
                        }
                        return true;
                    }
                case "setowner":
                case "lockraid":
                    {
                        if (args.Length >= 2 && (isAllowed || user.HasPermission("raidablebases.setowner")))
                        {
                            if (RustCore.FindPlayer(args[1]) is BasePlayer target && !target.IsKilled())
                            {
                                if (!(GetNearestBase(target.transform.position) is RaidableBase raid))
                                {
                                    QueueNotification(user, "TargetTooFar");
                                }
                                else if (raid.TrySetPayLock(new(target) { Economics = new(this, target) }, !args.Contains("lockout")))
                                {
                                    QueueNotification(user, "RaidLockedTo", target.displayName);
                                }
                                else QueueNotification(user, "You must use clearowner first.");
                            }
                            else QueueNotification(user, "TargetNotFoundId", args[1]);
                        }

                        return true;
                    }
                case "clearowner":
                    {
                        if (player.IsNetworked() && (isAllowed || user.HasPermission("raidablebases.clearowner")))
                        {
                            var target = player;
                            if (isAllowed && args.Length >= 2 && RustCore.FindPlayer(args[1]) is BasePlayer other)
                            {
                                target = other;
                            }
                            if (!(GetNearestBase(target.transform.position) is RaidableBase raid))
                            {
                                QueueNotification(user, "TooFar");
                            }
                            else if (isAllowed || raid.ownerId == player.userID)
                            {
                                raid.ResetEventLock();
                                raid.raiders.Clear();
                                QueueNotification(user, "RaidOwnerCleared");
                            }
                            else QueueNotification(user, "OwnerLocked");
                        }

                        return true;
                    }
            }

            return false;
        }

        private void DrawSpheres(BasePlayer player, bool isAllowed)
        {
            if (isAllowed || player.HasPermission("raidablebases.ddraw"))
            {
                AdminCommand(player, () =>
                {
                    foreach (var raid in Raids)
                    {
                        DrawSphere(player, 30f, Color.blue, raid.Location, raid.ProtectionRadius);
                    }
                });
            }
        }

        private bool IsScheduledReload;

        private void CommandToggle(IPlayer user, string command, string[] args)
        {
            if (!user.HasPermission("raidablebases.config"))
            {
                return;
            }

            if (config.Settings.Maintained.Enabled || args.Contains("maintained"))
            {
                Automated.IsMaintainedEnabled = !Automated.IsMaintainedEnabled;
                Automated.StartCoroutine(RaidableType.Maintained);
                Message(user, $"Toggled maintained events {(Automated.IsMaintainedEnabled ? "on" : "off")}");
                if (args.Contains("maintained"))
                {
                    config.Settings.Maintained.Enabled = Automated.IsMaintainedEnabled;
                    SaveConfig();
                    return;
                }
            }

            if (config.Settings.Schedule.Enabled || args.Contains("scheduled"))
            {
                Automated.IsScheduledEnabled = !Automated.IsScheduledEnabled;
                Automated.StartCoroutine(RaidableType.Scheduled);
                Message(user, $"Toggled scheduled events {(Automated.IsScheduledEnabled ? "on" : "off")}");
                if (args.Contains("scheduled"))
                {
                    config.Settings.Schedule.Enabled = Automated.IsScheduledEnabled;
                    SaveConfig();
                    return;
                }
            }

            if (config.Settings.Buyable.Max > 0)
            {
                Message(user, $"Toggled buyable events {((buyableEnabled = !buyableEnabled) ? "on" : "off")}");
            }

            Queues.Paused = !buyableEnabled && !Automated.IsScheduledEnabled && !Automated.IsMaintainedEnabled;
            IsScheduledReload = args.Contains("scheduled_reload") && Queues.Paused;
            if (args.Contains("scheduled_reload"))
            {
                Message(user, $"Scheduled reload after all events despawn has been {(IsScheduledReload ? "enabled" : "disabled")}");
            }
            Message(user, $"Toggled queue/spawn manager {(Queues.Paused ? "off" : "on")}");
        }

        private void CommandPopulate(IPlayer user, string command, string[] args)
        {
            if (args.Length == 0)
            {
                Message(user, "Valid arguments: 0 1 2 3 4 all");
                return;
            }

            List<LootItem> lootList = new(ItemManager.GetItemDefinitions().Where(def => !BlacklistedItems.Contains(def.shortname)).Select(def => new LootItem(def.shortname)));

            foreach (var arg in args)
            {
                foreach (var mode in GetRaidableModes())
                {
                    bool isModeMatch = mode.Equals(arg, StringComparison.OrdinalIgnoreCase);

                    if (isModeMatch || arg.Equals("all", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!Buildings.DifficultyLootLists.TryGetValue(mode, out var currentLootList))
                        {
                            Buildings.DifficultyLootLists[mode] = currentLootList = new();
                            Buildings.LootID[mode] = DateTime.Now;
                        }

                        foreach (var lootItem in lootList)
                        {
                            if (!currentLootList.Exists(x => x.shortname.Equals(lootItem.shortname, StringComparison.OrdinalIgnoreCase)))
                            {
                                currentLootList.Add(lootItem);
                            }
                        }

                        currentLootList.ForEach(ti => ti.InitializeArmorSlots());
                        currentLootList.Sort((x, y) => x.shortname.CompareTo(y.shortname));
                        HarmonyDataLayer.WriteObject(Path.Combine(Name, "Editable_Lists", mode), currentLootList);

                        Message(user, $"Created Editable_Lists/{mode}.json");
                    }
                }
            }

            SaveConfig();
        }

        private void CommandToggleProfile(IPlayer user, string command, string[] args)
        {
            if (args.Length == 2 && Get(args[1], out (string key, BaseProfile profile) val))
            {
                val.profile.Options.Enabled = !val.profile.Options.Enabled;
                SaveProfile(val.key, val.profile.Options);
                QueueNotification(user, val.profile.Options.Enabled ? "ToggleProfileEnabled" : "ToggleProfileDisabled", val.key);
            }
        }

        private void CommandPasteOption(IPlayer user, string command, string[] args)
        {
            if (args.Length < 2 || args[1] != "true" && args[1] != "false")
            {
                return;
            }
            var changes = 0;
            var search = args[0];
            var value = args[1];
            using var sb = DisposableBuilder.Get();
            var name = args.Length == 3 ? args[2] : null;
            foreach (var (key, profile) in Buildings.Profiles)
            {
                if (!string.IsNullOrWhiteSpace(name) && key != name)
                {
                    continue;
                }
                var pop = profile.Options.PasteOptions.Find(o => o.Key == search);
                if (pop != null && pop.Value != value)
                {
                    changes++;
                    pop.Value = value;
                    sb.Append(key).Append(", ");
                }
                foreach (var (extra, abo) in profile.Options.AdditionalBases)
                {
                    var option = abo.Options.Find(o => o.Key == search);
                    if (option == null)
                    {
                        changes++;
                        abo.Options.Add(new() { Key = search, Value = value });
                        sb.Append(extra).Append(", ");
                    }
                    else if (option.Value != value)
                    {
                        changes++;
                        option.Value = value;
                        sb.Append(extra).Append(", ");
                    }
                }
            }
            if (changes > 0)
            {
                foreach (var (key, profile) in Buildings.Profiles)
                {
                    SaveProfile(key, profile.Options);
                }
                sb.Length -= 2;
                user.Message($"\n{sb}\nChanged {search} for {changes} bases to {value}");
            }
            else user.Message("No changes required.");
        }

        private void CommandConfig(IPlayer user, string command, string[] args)
        {
            if (!user.HasPermission("raidablebases.config"))
            {
                Message(user, "No Permission");
                return;
            }

            if (args.Length == 0 || !arguments.Exists(str => args[0].Equals(str, StringComparison.OrdinalIgnoreCase)))
            {
                Message(user, "ConfigUseFormat", string.Join("|", arguments));
                return;
            }

            string arg = args[0].ToLower();

            switch (arg)
            {
                case "add": ConfigAddBase(user, args); return;
                case "remove": case "clean": ConfigRemoveBase(user, args); return;
                case "list": ConfigListBases(user); return;
                case "toggle": CommandToggleProfile(user, command, args); return;
                case "stability": case "inventories": CommandPasteOption(user, command, args); return;
                case "maintained":
                    {
                        Automated.IsMaintainedEnabled = !Automated.IsMaintainedEnabled;
                        Automated.StartCoroutine(RaidableType.Maintained);
                        Message(user, $"Toggled maintained events {(Automated.IsMaintainedEnabled ? "on" : "off")}");
                        config.Settings.Maintained.Enabled = Automated.IsMaintainedEnabled;
                        SaveConfig();
                        return;
                    }
                case "scheduled":
                    {
                        Automated.IsScheduledEnabled = !Automated.IsScheduledEnabled;
                        Automated.StartCoroutine(RaidableType.Scheduled);
                        Message(user, $"Toggled scheduled events {(Automated.IsScheduledEnabled ? "on" : "off")}");
                        config.Settings.Schedule.Enabled = Automated.IsScheduledEnabled;
                        SaveConfig();
                        return;
                    }
            }

            if (arg.Equals("enable_dome_marker"))
            {
                if (config.Settings.Markers.Radius < 0.25f) config.Settings.Markers.Radius = 0.25f;
                if (config.Settings.Markers.SubRadius < 0.5f) config.Settings.Markers.SubRadius = 0.5f;
                config.Settings.Markers.Manual = true;
                config.Settings.Markers.Buyables = true;
                config.Settings.Markers.Scheduled = true;
                config.Settings.Markers.Maintained = true;
                config.Settings.Markers.UseVendingMarker = true;
                config.Settings.Markers.UseExplosionMarker = false;
                SaveConfig();
                foreach (var (key, profile) in Buildings.Profiles)
                {
                    bool update = false;
                    if (profile.Options.SphereAmount < 5)
                    {
                        update = true;
                        profile.Options.SphereAmount = 5;
                    }
                    if (profile.Options.Silent)
                    {
                        update = true;
                        profile.Options.Silent = false;
                    }
                    if (update)
                    {
                        SaveProfile(key, profile.Options);
                    }
                }
                foreach (var raid in Raids)
                {
                    if (raid.Options.SphereAmount < 5)
                    {
                        raid.Options.SphereAmount = 5;
                    }
                    if (raid.Options.Silent)
                    {
                        raid.Options.Silent = false;
                    }
                    raid.ForceUpdateMarker();
                }
                user.Message("Enabled map markers and dome.");
                return;
            }

            if (arg.Equals("noexplosivecosts"))
            {
                foreach (var (key, profile) in Buildings.Profiles)
                {
                    foreach (var abo in profile.Options.AdditionalBases.Values)
                    {
                        abo.Costs.Clear();
                    }
                    SaveProfile(key, profile.Options);
                }
                user.Message("Removed all explosive costs from the profiles.");
                return;
            }

            string mode = GetRaidableMode(arg);
            if (IsModeValid(mode))
            {
                if (args.Length >= 2 && int.TryParse(args[1], out var amount))
                {
                    ConfigSetDifficultyLimit(user, mode, amount, args.Length >= 3 ? args[2].ToLower() : "automated");
                }
                else if (args.Length == 3 && Enum.TryParse(args[1].ToLower().SentenceCase(), out DayOfWeek dayOfWeek))
                {
                    ConfigSetEnabledWeekday(user, mode, dayOfWeek, args[2].ToLower());
                }
            }
        }

        #endregion Commands

    }
}
