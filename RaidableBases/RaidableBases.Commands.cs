using Facepunch;
using HarmonyLib;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rust;
using Rust.Ai.Gen2;
using Rust.Ai.Gen2.Nav;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Newtonsoft.Json.Serialization;
using Color = UnityEngine.Color;
using static RaidableBases.RaidableBasesExtensionMethods.ExtensionMethods;

namespace RaidableBases
{
    public partial class RaidableBases
    {

        #region Commands


        private static bool HasArgument(string[] args, string value)
        {
            return args != null && Array.Exists(args, arg => arg.Equals(value, StringComparison.OrdinalIgnoreCase));
        }

        private static UiType GetMovableUiType(string[] args)
        {
            if (HasArgument(args, "buyable") || HasArgument(args, "buy")) return UiType.Buyable;
            if (HasArgument(args, "cooldown") || HasArgument(args, "cooldowns")) return UiType.Cooldown;
            if (HasArgument(args, "delay") || HasArgument(args, "pvpdelay")) return UiType.Delay;
            if (HasArgument(args, "lockout") || HasArgument(args, "lockouts")) return UiType.Lockout;
            if (HasArgument(args, "pasteprogress") || HasArgument(args, "paste") || HasArgument(args, "progress")) return UiType.PasteProgress;
            if (HasArgument(args, "status")) return UiType.Status;
            if (HasArgument(args, "teleport")) return UiType.Teleport;
            return UiType.Invalid;
        }

        private int GetRequestedPurchaseType(BasePlayer player, string[] args)
        {
            bool pve = HasArgument(args, "pve");
            bool pvp = HasArgument(args, "pvp");

            if (pve && pvp)
            {
                return -1;
            }

            if (pve)
            {
                return 1;
            }

            if (pvp)
            {
                return 2;
            }

            bool pveOnly = player != null && player.HasPermission("raidablebases.buyraid.pveonly");
            bool pvpOnly = player != null && player.HasPermission("raidablebases.buyraid.pvponly");

            if (pveOnly && pvpOnly)
            {
                return -1;
            }

            if (pveOnly)
            {
                return 1;
            }

            if (pvpOnly)
            {
                return 2;
            }

            return 0;
        }

        private bool CanBuyRaidType(BasePlayer player, int purchaseType)
        {
            if (player == null || purchaseType < 0)
            {
                return false;
            }

            return purchaseType switch
            {
                1 => !player.HasPermission("raidablebases.buyraid.pvponly"),
                2 => config.Settings.Buyable.AllowBuyPVP && !player.HasPermission("raidablebases.buyraid.pveonly"),
                _ => true
            };
        }

        private bool IsFreePurchase(IPlayer user, BasePlayer buyer, string[] args)
        {
            return HasArgument(args, "free") && user?.IsAdmin == true ||
                user != null && !user.IsServer && user.HasPermission("raidablebases.buyraid.free") ||
                buyer != null && buyer.HasPermission("raidablebases.buyraid.free");
        }

        internal void CloseBuyableUi(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

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

            UI.DestroyBuyableUi(player);
        }

        [ConsoleCommand("rb.info")]
        private void ccmdTargetInfo(ConsoleSystem.Arg arg)
        {
            if (!arg.Player().Is(out BasePlayer player) || !player.IsAdmin && !player.HasPermission("raidablebases.infoui"))
            {
                return;
            }

            if (string.Equals(arg.GetString(0), "clear", StringComparison.OrdinalIgnoreCase))
            {
                _targetInfo?.Hide(player);
                return;
            }

            _targetInfo?.Show(player, arg);
        }

        [ConsoleCommand("ui_buyraid")]
        private void ccmdBuyRaid(ConsoleSystem.Arg arg)
        {
            if (!arg.HasArgs())
            {
                return;
            }

            var player = arg.Player();

            if (player == null)
            {
                return;
            }

            string action = arg.GetString(0);

            if (action == "closeui")
            {
                CloseBuyableUi(player);
                return;
            }

            if (action == "back_to_buyable")
            {
                UI.DestroyBuyableUi(player);
                UI.ShowBuyableUi(player, false);
                return;
            }

            if (action == "confirm_type" && arg.HasArgs(3))
            {
                string mode = arg.GetString(1).Replace("__", " ");
                string type = arg.GetString(2).ToLowerInvariant();
                int purchaseType = type == "pve" ? 1 : type == "pvp" ? 2 : -1;

                if (!RaidableModes.Contains(mode) || !CanBuyRaidType(player, purchaseType))
                {
                    SendNotification(player, "BuyableTypeUnavailable");
                    return;
                }

                UI.ShowBuyableConfirmationUi(player, mode, type);
                return;
            }

            if (action == "confirm_purchase" && arg.HasArgs(3))
            {
                string mode = arg.GetString(1);
                string type = arg.GetString(2).ToLowerInvariant();
                int purchaseType = type == "pve" ? 1 : type == "pvp" ? 2 : -1;

                if (!CanBuyRaidType(player, purchaseType))
                {
                    SendNotification(player, "BuyableTypeUnavailable");
                    return;
                }

                if (player.GetIPlayer() != null)
                {
                    CommandBuyRaid(player.GetIPlayer(), config.Settings.BuyCommand, new[] { mode, type });
                }

                return;
            }

            if (action == "accept_teleport")
            {
                BuyableTeleport(player);
                UI.DestroyUi(player, UiType.Teleport);
                return;
            }

            if (action == "decline_teleport")
            {
                UI.DestroyUi(player, UiType.Teleport);
                return;
            }

            if (player.GetIPlayer() != null)
            {
                CommandBuyRaid(player.GetIPlayer(), config.Settings.BuyCommand, arg.Args.ToStringArray());
            }
        }

        internal void DispatchOnCuiDraggableDrag(BasePlayer player, string name, Vector3 position, CommunityEntity.DraggablePositionSendType dragType)
        {
            OnCuiDraggableDrag(player, name, position, dragType);
        }

        private void OnCuiDraggableDrag(BasePlayer player, string name, Vector3 position, CommunityEntity.DraggablePositionSendType dragType)
        {
            if (player == null)
            {
                return;
            }

            UiType type = name switch
            {
                "RB_UI_Buyable" => UiType.Buyable,
                "RB_UI_Cooldown" => UiType.Cooldown,
                "RB_UI_Delay" => UiType.Delay,
                "RB_UI_Lockout" => UiType.Lockout,
                "RB_UI_PasteProgress" => UiType.PasteProgress,
                "RB_UI_Status" => UiType.Status,
                "RB_UI_Teleport" => UiType.Teleport,
                _ => UiType.Invalid
            };

            if (type == UiType.Invalid)
            {
                return;
            }

            UiOffsets offsets = UI.GetOffsets(player.userID, type);

            switch (dragType)
            {
                case CommunityEntity.DraggablePositionSendType.Relative:
                case CommunityEntity.DraggablePositionSendType.RelativeAnchor:
                    Vector2 delta = new(position.x, position.y);
                    offsets.Min += delta;
                    offsets.Max += delta;
                    offsets.NormalizedAnchor = Vector2.zero;
                    break;
                case CommunityEntity.DraggablePositionSendType.NormalizedParent:
                case CommunityEntity.DraggablePositionSendType.NormalizedScreen:
                    Vector2 normalizedPosition = new(Mathf.Clamp01(position.x), Mathf.Clamp01(position.y));
                    if (!UI.IsMeaningfulDrag(offsets, normalizedPosition))
                    {
                        return;
                    }
                    offsets.NormalizedAnchor = normalizedPosition;
                    break;
                default:
                    return;
            }

            UI.RecordUiPosition(player, type);

            // Reimplemented interfaces can be dragged while another interface already provides a cursor.
            // Explicit move mode only supplies a temporary cursor, so dragging then extends its timer.
            if (UI.IsMovingUi(player, type))
            {
                UI.TrySetMoveUi(player, type);
            }

            if (SaveOffsetDataTimer is { Destroyed: false }) SaveOffsetDataTimer.Reset();
            else SaveOffsetDataTimer = timer.Once(5f, UI.SaveOffsetData);
        }

        private Timer SaveOffsetDataTimer;

        [ConsoleCommand("rb_ui_move")]
        private void ccmdMovePosition(ConsoleSystem.Arg arg)
        {
            if (!arg.HasArgs() || !arg.Player().Is(out BasePlayer player) || !Enum.TryParse(arg.GetString(0), true, out UiType type) || !UiHandler.IsMovableUi(type))
            {
                return;
            }

            UI.GetOffsets(player.userID, type);

            if (UI.IsMovingUi(player, type))
            {
                UI.TrySetMoveUi(player, type, true);
            }
            else
            {
                UI.TrySetMoveUi(player, type);
            }

            UI.UpdateUi(player, type);
        }

        protected bool CanReloadConfiguration(IPlayer user)
        {
            if (IsGridLoading() || !IsPasteAvailable())
            {
                Reply(user, IsGridLoading() ? "GridIsLoading" : "PasteOnCooldown");
                return false;
            }

            return true;
        }

        protected void ReloadConfiguration(IPlayer user)
        {
            Reply(user, "ReloadInit");
            SetOnSun(false);
            UI.SaveOffsetData();
            UI.DestroyAll();
            Reply(user, "ReloadConfig");
            LoadConfig();
            UI.LoadOffsetData();
            Automated.IsMaintainedEnabled = config.Settings.Maintained.Enabled;
            Automated.StartCoroutine(RaidableType.Maintained, user);
            Automated.IsScheduledEnabled = config.Settings.Schedule.Enabled;
            Automated.StartCoroutine(RaidableType.Scheduled, user);
            buyableEnabled = config.Settings.Buyable.Max > 0;
            Initialize();
        }

        private void CommandReloadConfig(IPlayer user, string command, string[] args)
        {
            if (!(user.IsServer || user.Player().IsAdmin) || !CanReloadConfiguration(user))
            {
                return;
            }

            if (command == "rb.reloadconfig")
            {
                ReloadConfiguration(user);
                return;
            }

            Reply(user, "ReloadInit");

            if (command == "rb.reloadprofiles")
            {
                ServerMgr.Instance.StartCoroutine(ReloadProfiles(user));
            }

            if (command == "rb.reloadtables")
            {
                ServerMgr.Instance.StartCoroutine(ReloadTables(user));
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
            GridController.SpawnCache.Clear();
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

        private readonly Dictionary<string, string> prefabReplacements = new()
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
            ["assets/prefabs/deployable/large wood storage/skins/abyss_dlc_large_wood_box/abyss_dlc_storage_vertical/abyss_barrel_vertical.prefab"] = "assets/prefabs/deployable/large wood storage/box.wooden.large.prefab",
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
            ["krieg_storage_vertical"] = "box.wooden",
            ["abyss.barrel.horizontal"] = "box.wooden.large",
            ["abyss.barrel.vertical"] = "box.wooden",
            ["arcade.machine.chippy"] = "electric.battery.rechargable.medium",
            ["attire.egg.suit"] = "wood.armor.pants",
            ["attire.nesthat"] = "wood.armor.helmet",
            ["attire.ninja.suit"] = "hazmatsuit",
            ["attire.snowman.helmet"] = "deer.skull.mask",
            ["bamboo.barrel"] = "box.wooden",
            ["barricade.medieval"] = "barricade.metal",
            ["bathtub.planter"] = "planter.triangle",
            ["beachchair"] = "chair",
            ["beachparasol"] = "storageadaptor",
            ["beachtable"] = "box.wooden",
            ["beachtowel"] = "sleepingbag",
            ["blunderbuss"] = "shotgun.waterpipe",
            ["boogieboard"] = "kayak",
            ["boombox"] = "electric.audioalarm",
            ["boots.frog"] = "attire.hide.boots",
            ["carvable.pumpkin"] = "lantern",
            ["cassette"] = "telephone",
            ["cassette.medium"] = "electric.battery.rechargable.small",
            ["cassette.short"] = "electric.timer",
            ["chair.icethrone"] = "chair",
            ["chicken.costume"] = "roadsign.kilt",
            ["chineselantern"] = "ceilinglight",
            ["chineselanternwhite"] = "ceilinglight",
            ["clatter.helmet"] = "riot.helmet",
            ["cocoknight.armor.gloves"] = "woodarmor.gloves",
            ["cocoknight.armor.helmet"] = "wood.armor.helmet",
            ["cocoknight.armor.pants"] = "wood.armor.pants",
            ["cocoknight.armor.torso"] = "wood.armor.jacket",
            ["concretehatchet"] = "hatchet",
            ["concretepickaxe"] = "pickaxe",
            ["connected.speaker"] = "electric.audioalarm",
            ["cupboard.tool.retro"] = "cupboard.tool",
            ["cupboard.tool.shockbyte"] = "cupboard.tool",
            ["cursedcauldron"] = "campfire",
            ["discoball"] = "ceilinglight",
            ["discofloor"] = "rug",
            ["discofloor.largetiles"] = "rug",
            ["discord.trophy"] = "pookie.bear",
            ["diverhatchet"] = "axe.salvaged",
            ["diverpickaxe"] = "pickaxe",
            ["divertorch"] = "Torch",
            ["door.double.hinged.bardoors"] = "door.double.hinged.wood",
            ["door.hinged.industrial.a"] = "door.hinged.metal",
            ["draculacape"] = "hoodie",
            ["draculamask"] = "riot.helmet",
            ["dragondoorknocker"] = "sign.wooden.small",
            ["drumkit"] = "chair",
            ["easterdoorwreath"] = "sign.wooden.small",
            ["factorydoor"] = "door.hinged.metal",
            ["firework.boomer.blue"] = "pookie.bear",
            ["firework.boomer.champagne"] = "pookie.bear",
            ["firework.boomer.green"] = "pookie.bear",
            ["firework.boomer.orange"] = "pookie.bear",
            ["firework.boomer.pattern"] = "pookie.bear",
            ["firework.boomer.red"] = "pookie.bear",
            ["firework.boomer.violet"] = "pookie.bear",
            ["firework.romancandle.blue"] = "pookie.bear",
            ["firework.romancandle.green"] = "pookie.bear",
            ["firework.romancandle.red"] = "pookie.bear",
            ["firework.romancandle.violet"] = "pookie.bear",
            ["firework.volcano"] = "pookie.bear",
            ["firework.volcano.red"] = "pookie.bear",
            ["firework.volcano.violet"] = "pookie.bear",
            ["fishtrophy"] = "sign.wooden.small",
            ["fogmachine"] = "electric.hbhfsensor",
            ["frankensteinmask"] = "riot.helmet",
            ["frontier_hatchet"] = "hatchet",
            ["fun.bass"] = "sign.wooden.small",
            ["fun.boomboxportable"] = "pookie.bear",
            ["fun.casetterecorder"] = "electric.digitalclock",
            ["fun.cowbell"] = "fun.guitar",
            ["fun.flute"] = "fun.guitar",
            ["fun.jerrycanguitar"] = "fun.guitar",
            ["fun.tambourine"] = "fun.guitar",
            ["fun.trumpet"] = "fun.guitar",
            ["fun.tuba"] = "fun.guitar",
            ["gates.external.high.adobe"] = "gates.external.high.stone",
            ["gates.external.high.legacy"] = "gates.external.high.stone",
            ["gates.external.high.frontier"] = "gates.external.high.stone",
            ["giantcandycanedecor"] = "electric.hbhfsensor",
            ["giantlollipops"] = "electric.hbhfsensor",
            ["gun.water"] = "smg.thompson",
            ["gunrack.horizontal"] = "sign.wooden.small",
            ["gunrack.single.1.horizontal"] = "sign.wooden.small",
            ["gunrack.single.2.horizontal"] = "sign.wooden.small",
            ["gunrack.single.3.horizontal"] = "sign.wooden.small",
            ["gunrack_stand"] = "box.wooden",
            ["gunrack_tall.horizontal"] = "sign.wooden.small",
            ["gunrack_wide.horizontal"] = "sign.wooden.small",
            ["half.bamboo.shelves"] = "shelves",
            ["halloween.surgeonsuit"] = "hazmatsuit",
            ["hat.bunnyhat"] = "wood.armor.helmet",
            ["hat.dragonmask"] = "riot.helmet",
            ["hat.oxmask"] = "riot.helmet",
            ["hat.rabbitmask"] = "riot.helmet",
            ["hat.ratmask"] = "riot.helmet",
            ["hat.snakemask"] = "riot.helmet",
            ["hat.tigermask"] = "riot.helmet",
            ["hat.wellipets"] = "coffeecan.helmet",
            ["hazmat.plushy"] = "pookie.bear",
            ["hazmatsuit.arcticsuit"] = "hazmatsuit",
            ["hazmatsuit.diver"] = "hazmatsuit",
            ["hazmatsuit.frontier"] = "hazmatsuit",
            ["hazmatsuit.lumberjack"] = "hazmatsuit",
            ["hazmatsuit.nomadsuit"] = "hazmatsuit",
            ["hazmatsuit.spacesuit"] = "hazmatsuit",
            ["hazmatyoutooz"] = "pookie.bear",
            ["heavyscientistyoutooz"] = "pookie.bear",
            ["hobobarrel"] = "campfire",
            ["horse.costume"] = "hoodie",
            ["huntingtrophylarge"] = "sign.wooden.small",
            ["huntingtrophysmall"] = "sign.wooden.small",
            ["industrial.wall.light"] = "electrical.branch",
            ["industrial.wall.light.blue"] = "electrical.branch",
            ["industrial.wall.light.green"] = "electrical.branch",
            ["industrial.wall.light.red"] = "electrical.branch",
            ["innertube"] = "sled",
            ["innertube.horse"] = "sled",
            ["innertube.unicorn"] = "sled",
            ["jackolantern.angry"] = "lantern",
            ["jackolantern.happy"] = "lantern",
            ["jungle.rock"] = "rock",
            ["knife.bone.obsidian"] = "knife.bone",
            ["knife.skinning"] = "knife.combat",
            ["knightsarmour.helmet"] = "coffeecan.helmet",
            ["knightsarmour.skirt"] = "roadsign.kilt",
            ["knighttorso.armour"] = "roadsign.jacket",
            ["largecandles"] = "pookie.bear",
            ["laserlight"] = "electrical.branch",
            ["legacy bow"] = "bow.hunting",
            ["legacyfurnace"] = "furnace",
            ["lumberjack.hatchet"] = "hatchet",
            ["lumberjack.pickaxe"] = "pickaxe",
            ["mace.baseballbat"] = "mace",
            ["medieval.box.wooden.large"] = "box.wooden.large",
            ["medieval.door.double.hinged.metal"] = "door.double.hinged.metal",
            ["medieval.door.hinged.metal"] = "door.hinged.metal",
            ["megaphone"] = "tincan.alarm",
            ["metal.facemask.hockey"] = "metal.facemask",
            ["metal.facemask.icemask"] = "metal.facemask",
            ["metal.plate.torso.icevest"] = "metal.plate.torso",
            ["microphonestand"] = "tincan.alarm",
            ["minecart.planter"] = "box.wooden",
            ["mobilephone"] = "telephone",
            ["movembermoustache"] = "mask.bandana",
            ["movembermoustachecard"] = "mask.bandana",
            ["mummymask"] = "riot.helmet",
            ["newyeargong"] = "target.reactive",
            ["paddlingpool"] = "planter.large",
            ["photoframe.landscape"] = "sign.wooden.small",
            ["photoframe.large"] = "sign.wooden.small",
            ["photoframe.portrait"] = "sign.wooden.small",
            ["piano"] = "chair",
            ["pistol.water"] = "pistol.eoka",
            ["rail.road.planter"] = "planter.large",
            ["rifle.ak.diver"] = "rifle.ak",
            ["rifle.ak.ice"] = "rifle.ak",
            ["rifle.ak.jungle"] = "rifle.ak",
            ["rifle.ak.med"] = "rifle.ak",
            ["rocket.launcher.dragon"] = "rocket.launcher",
            ["rockingchair"] = "chair",
            ["rockingchair.rockingchair2"] = "chair",
            ["rockingchair.rockingchair3"] = "chair",
            ["rustige_egg_a"] = "pookie.bear",
            ["rustige_egg_b"] = "pookie.bear",
            ["rustige_egg_c"] = "pookie.bear",
            ["rustige_egg_d"] = "pookie.bear",
            ["rustige_egg_e"] = "pookie.bear",
            ["rustige_egg_f"] = "pookie.bear",
            ["rustige_egg_g"] = "pookie.bear",
            ["salvaged.bamboo.shelves"] = "sign.wooden.small",
            ["santabeard"] = "mask.bandana",
            ["scarecrow"] = "box.wooden",
            ["sculpture.ice"] = "box.wooden",
            ["secretlabchair"] = "chair",
            ["sign.hanging"] = "sign.wooden.small",
            ["sign.hanging.banner.large"] = "sign.wooden.small",
            ["sign.hanging.ornate"] = "pie.pumpkin",
            ["sign.neon.125x125"] = "sign.wooden.small",
            ["sign.neon.125x215"] = "sign.wooden.small",
            ["sign.neon.125x215.animated"] = "sign.wooden.large",
            ["sign.neon.xl"] = "sign.wooden.medium",
            ["sign.neon.xl.animated"] = "sign.wooden.huge",
            ["sign.pictureframe.landscape"] = "sign.wooden.medium",
            ["sign.pictureframe.portrait"] = "sign.wooden.medium",
            ["sign.pictureframe.tall"] = "sign.wooden.medium",
            ["sign.pictureframe.xl"] = "sign.wooden.small",
            ["sign.pictureframe.xxl"] = "sign.wooden.small",
            ["sign.pole.banner.large"] = "sign.wooden.small",
            ["sign.post.double"] = "tincan.alarm",
            ["sign.post.single"] = "tincan.alarm",
            ["sign.post.town"] = "tincan.alarm",
            ["sign.post.town.roof"] = "tincan.alarm",
            ["single.shallow.wall.shelves"] = "sign.wooden.medium",
            ["skull"] = "fat.animal",
            ["skull.trophy"] = "pookie.bear",
            ["skull.trophy.jar"] = "lantern",
            ["skull.trophy.jar2"] = "pookie.bear",
            ["skull.trophy.table"] = "pookie.bear",
            ["skull_fire_pit"] = "campfire",
            ["skulldoorknocker"] = "sign.wooden.small",
            ["skullspikes"] = "tincan.alarm",
            ["skullspikes.candles"] = "tincan.alarm",
            ["skullspikes.pumpkin"] = "tincan.alarm",
            ["skylantern"] = "rock",
            ["skylantern.skylantern.green"] = "rock",
            ["skylantern.skylantern.orange"] = "rock",
            ["skylantern.skylantern.purple"] = "rock",
            ["skylantern.skylantern.red"] = "rock",
            ["smallcandles"] = "lantern",
            ["snowmachine"] = "pookie.bear",
            ["snowman"] = "box.wooden",
            ["snowmobiletomaha"] = "snowmobile",
            ["sofa"] = "table",
            ["sofa.pattern"] = "chair",
            ["soundlight"] = "electrical.branch",
            ["spear.cny"] = "spear.wooden",
            ["spookyspeaker"] = "electric.audioalarm",
            ["unused_storage_barrel_a"] = "box.wooden",
            ["storage_barrel_b"] = "box.wooden",
            ["storage_barrel_c"] = "box.wooden",
            ["strobelight"] = "electrical.branch",
            ["sunglasses"] = "mask.bandana",
            ["sunglasses02black"] = "mask.bandana",
            ["sunglasses02camo"] = "mask.bandana",
            ["sunglasses02red"] = "mask.bandana",
            ["sunglasses03black"] = "mask.bandana",
            ["sunglasses03chrome"] = "mask.bandana",
            ["sunglasses03gold"] = "mask.bandana",
            ["sunken.knife"] = "knife.combat",
            ["tool.instant_camera"] = "cctv.camera",
            ["toolgun"] = "hammer",
            ["torch.torch.skull"] = "torch",
            ["torchholder"] = "electrical.branch",
            ["triangle.rail.road.planter"] = "planter.triangle",
            ["trophy"] = "ammo.rocket.mlrs",
            ["trophy2023"] = "pookie.bear",
            ["twitch.headset"] = "hat.cap",
            ["twitchrivals2023desk"] = "table",
            ["twitchsunglasses"] = "mask.bandana",
            ["vehicle.car_radio"] = "lantern",
            ["wall.external.high.adobe"] = "wall.external.high",
            ["wall.external.high.legacy"] = "wall.external.high.stone",
            ["wall.frame.lunar2025_a"] = "door.double.hinged.wood",
            ["wall.frame.lunar2025_b"] = "door.double.hinged.wood",
            ["wall.frame.lunar2025_c"] = "door.double.hinged.wood",
            ["wantedposter"] = "electrical.branch",
            ["wantedposter.wantedposter2"] = "electrical.branch",
            ["wantedposter.wantedposter3"] = "electrical.branch",
            ["wantedposter.wantedposter4"] = "electrical.branch",
            ["wicker.barrel"] = "box.wooden",
            ["xmas.door.garland"] = "electric.simplelight",
            ["xmas.double.door.garland"] = "shutter.wood.a",
            ["xmas.lightstring"] = "electric.simplelight",
            ["xmas.lightstring.advanced"] = "electric.simplelight",
            ["xmas.window.garland"] = "electric.simplelight",
            ["xmasdoorwreath"] = "sign.wooden.small",
            ["xylophone"] = "chair",
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
                    ReplyOrLog(user, $"This action will modify your copypaste files and loot tables to comply with Facepunch's Terms of Service regarding paid content. You should backup your copypaste folder, and loot tables, before proceeding. To confirm, type: {config.Settings.EditCommand} {_editCode}");
                    ReplyOrLog(user, $"Default behavior will delete any content rather than replace it. If you prefer to have it replaced, then specify which to replace: {config.Settings.EditCommand} {_editCode} replace_prefabs replace_loot");
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
                using var files = DisposableHashSet<string>();

                foreach (string file in GetCopyPasteFiles())
                {
                    files.Add(Path.Combine("copypaste", GetFileNameWithoutExtension(file)).Replace(".json", ""));
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
            DynamicConfigFile data;
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

        private ulong BusyNoticeId;
        private Dictionary<ulong, double> _buyers = new();
        private void CommandBuyRaid(IPlayer user, string command, string[] args)
        {
            user ??= _consolePlayer;
            var player = user.Player();

            if (DebugMode)
            {
                var where = player != null ? player.transform.position.ToString() : "server";
                var why = args.Length > 0 ? string.Join(" ", args) : "<none>";
                Puts($"user={user.Name} ({user.Id}), command=/{command}, pos={where}, args=[{why}]");
            }

            string[] retryArgs = args;
            int index = Array.FindIndex(args, arg => arg.IsSteamId());
            if (index >= 0)
            {
                string targetId = args[index];

                if (!BasePlayer.Find(targetId).Is(out BasePlayer target))
                {
                    Reply(user, "TargetNotFoundId", targetId);
                    return;
                }

                player = target;

                string[] filtered = new string[args.Length - 1];

                if (index > 0)
                {
                    Array.Copy(args, 0, filtered, 0, index);
                }

                if (index < args.Length - 1)
                {
                    Array.Copy(args, index + 1, filtered, index, args.Length - index - 1);
                }

                args = filtered;
            }
            else if (player == null)
            {
                Reply(user, "TargetNotFoundNoId");
                return;
            }
            else if (!player.IsConnected)
            {
                Reply(user, "TargetNotFoundId");
                return;
            }

            var buyer = user.Player() ?? player;
            ulong userid = buyer.userID;

            if (!buyableEnabled)
            {
                SendNotification(buyer, "BuyRaidsDisabled");
                return;
            }

            string busy = SaveRestore.IsSaving ? "BuyableServerSaving" : IsGridLoading() ? "GridIsLoading" : null;
            bool purchaseAttempt = config.Settings.Buyable.RandomOnly || args.Length != 0;
            if (busy != null && purchaseAttempt)
            {
                if (BusyNoticeId == 0)
                {
                    BusyNoticeId = buyer.userID;
                    NotifyOnce(buyer, busy);
                }
                else if (BusyNoticeId != buyer.userID)
                {
                    NotifyOnce(buyer, busy);
                    return;
                }

                ulong id = buyer.userID;
                timer.Once(1f, () =>
                {
                    if (buyer != null && buyer.IsConnected) CommandBuyRaid(user, command, retryArgs);
                    else if (BusyNoticeId == id) BusyNoticeId = 0;
                });

                return;
            }

            BusyNoticeId = 0;

            double now = Time.timeAsDouble;
            if (_buyers.TryGetValue(userid, out var t) && t > now)
            {
                SendNotification(buyer, "BuyCooldown", Math.Round(t - now, 2));
                return;
            }
            if (_buyers.Count > 10) _buyers.Clear();
            _buyers[userid] = now + 0.5;

            if (!bypassRestarting && ServerMgr.Instance.Restarting && ServerMgr.Instance.restartCoroutine.Current != null)
            {
                SendNotification(buyer, buyer.IsAdmin ? "BuyableServerRestartingAdmin" : "BuyableServerRestarting");
                return;
            }

            if (config.Settings.Buyable.UsePermission && !user.HasPermission("raidablebases.buyraid"))
            {
                SendNotification(buyer, "No Permission");
                return;
            }

            if (player.HasPermission("raidablebases.banned") || player.HasPermission("raidablebases.buyraid.banned"))
            {
                SendNotification(buyer, buyer.IsAdmin ? "BannedAdmin" : "Banned", buyer.UserIDString);
                return;
            }

            if (!IsPasteEngineReady(out var error))
            {
                SendNotification(buyer, error);
                return;
            }

            if (args.Length == 0) // exposes hook call
            {
                if (HarmonyModInterface.CallHook("OnPurchaseBase", buyer, player) != null)
                {
                    return;
                }

                if (!config.Settings.Buyable.RandomOnly)
                {
                    if (config.UI.Buyable.Enabled)
                    {
                        UI.ShowBuyableUi(buyer, false);
                    }
                    else
                    {
                        SendNotification(buyer, "BuySyntax", config.Settings.BuyCommand, user.IsServer ? "ID" : user.Id);
                    }

                    return;
                }
            }

            if (args.Contains("reset") && config.Settings.Buyable.Cooldowns.Costs.Any && data.BuyableCooldowns.ContainsKey(player.userID))
            {
                CommandBuyRaidTakePayments(user, buyer, player, -1, Array.Empty<string>());
                return;
            }

            string value = config.Settings.Buyable.RandomOnly ? string.Empty : args[0].Replace("__", " ");
            string mode = null;
            if (config.Settings.Buyable.RandomOnly)
            {
                using var rng = DisposableList<string>();

                foreach (var m in RaidableModes)
                {
                    if (HasBuyableCooldown(player, m, false) || !CanSpawnDifficultyToday(RaidableType.Purchased, m) || !IsDifficultyAvailable(m, RaidableType.Purchased, false) || !IsDifficultyAvailable(m, RaidableType.Purchased, true))
                    {
                        continue;
                    }

                    int limit = config.Settings.Buyable.Limits.Get(m);
                    if (limit < 0 || limit > 0 && Get(m, true) >= limit)
                    {
                        continue;
                    }

                    if (!Buildings.Profiles.Values.Exists(profile => profile.Options.Mode == m && profile.Options.Permission.Has(player, RaidableType.Purchased)))
                    {
                        continue;
                    }

                    if (!isDifficultyEnabledAfterWipeOverridden && !IsDifficultyEnabledAfterWipe(m, RaidableType.Purchased, player.UserIDString, out _))
                    {
                        continue;
                    }

                    rng.Add(m);
                }

                if (rng.Count == 0)
                {
                    SendNotification(buyer, "BuyAnotherDifficulty", RaidableMode.Random);
                    return;
                }

                mode = rng.GetSecureRandom();
                value = mode;
            }
            else
            {
                mode = GetRaidableMode(value, user, buyer);
            }

            int purchaseType = GetRequestedPurchaseType(player, args);
            if (!CanBuyRaidType(player, purchaseType))
            {
                SendNotification(buyer, "BuyableTypeUnavailable");
                return;
            }

            if (HasBuyableCooldown(player, mode, true))
            {
                return;
            }

            if (!CanSpawnDifficultyToday(RaidableType.Purchased, mode))
            {
                if (!CanFileMode(user, buyer)) SendNotification(buyer, "No Permission To Buy File", value);
                else if (!FileExists(value)) SendNotification(buyer, "FileDoesNotExist2", value);
                else SendNotification(buyer, "BuyDifficultyNotAvailableToday", mode);
                return;
            }

            if (!config.Settings.Include.Any && !IsFreePurchase(user, buyer, args))
            {
                SendNotification(buyer, "NoBuyableEventsCostsEnabled");
                return;
            }

            if (mode == RaidableMode.Random || !IsDifficultyAvailable(mode, RaidableType.Purchased, false))
            {
                SendNotification(buyer, "BuyAnotherDifficulty", value);
                return;
            }

            if (!IsDifficultyAvailable(mode, RaidableType.Purchased, true))
            {
                SendNotification(buyer, "BuyRaidNotConfiguredProperly");
                return;
            }

            if (Get(RaidableType.Purchased) >= config.Settings.Buyable.Max)
            {
                if (config.Settings.Buyable.AutoCloseUi)
                {
                    UI.DestroyBuyableUi(buyer);
                }
                SendNotification(buyer, "Max Events", command, config.Settings.Buyable.Max);
                return;
            }

            int max = config.Settings.Buyable.Limits.Get(mode);
            if (max < 0 || max > 0 && Get(mode, true) >= max)
            {
                SendNotification(buyer, "Max Events", mode, max);
                return;
            }

            if (IsEventOwner(player, true))
            {
                SendNotification(buyer, "BuyableAlreadyOwner");
                UI.DestroyBuyableUi(buyer);
                return;
            }

            using var members = GetMembers(buyer, out bool delay);

            if (IsQueued(player, members))
            {
                UI.DestroyBuyableUi(buyer);
                if (delay) _buyers[userid] = now + 15;
                return;
            }

            if (!Buildings.Profiles.Values.Exists(profile => profile.Options.Mode == mode && profile.Options.Permission.Has(player, RaidableType.Purchased)))
            {
                SendNotification(buyer, "No Permission To Buy");
                return;
            }

            if (!isDifficultyEnabledAfterWipeOverridden && !IsDifficultyEnabledAfterWipe(mode, RaidableType.Purchased, player.UserIDString, out double remainingHours))
            {
                double remainingSeconds = remainingHours * 3600;
                SendNotification(buyer, "BuyAnotherDifficultyWipeTimed", mode, FormatTime(remainingSeconds, buyer.UserIDString));
                return;
            }

            UI.DestroyBuyableUi(buyer);

            if (HarmonyModInterface.CallHook("OnPurchaseTakePayments", buyer, player, value, mode) is object obj && obj != null)
            {
                SendNotification(buyer, obj is string str ? str : "No Permission");
                return;
            }

            if (DebugMode) Puts($"{user.Name} ({user.Id}): attempt to take payment");

            int level = GetLevelFromMode(mode);

            CommandBuyRaidTakePayments(user, buyer, player, level, args, false, mode, value);
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
                    SendNotification(player, "Another plugin has forcefully removed you from this event!");
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

        private bool HasBuyableCooldown(BasePlayer buyer, int level, bool message = true)
        {
            return GetModeFromLevel(level, out string mode) && HasBuyableCooldown(buyer, mode, message);
        }

        public void CommandBuyRaidTakePayments(IPlayer user, BasePlayer buyer, BasePlayer player, int level, string[] args, bool reset = true, string mode = RaidableMode.Disabled, string value = null)
        {
            var payments = new Payments(buyer);
            var money = reset ? config.Settings.Buyable.Cooldowns.Costs.Money : config.Settings.Include.Economics ? config.Settings.Economics.Get(mode) : 0.0;
            var points = reset ? config.Settings.Buyable.Cooldowns.Costs.Points : config.Settings.Include.ServerRewards ? config.Settings.ServerRewards.Get(mode) : 0;
            var options = reset ? new() { config.Settings.Buyable.Cooldowns.Costs.Custom } : config.Settings.Include.Custom && config.Settings.Custom.TryGetValue(mode, out var val) ? val : new();
            var free = IsFreePurchase(user, buyer, args);
            if (free)
            {
                InitializeFreePayments(buyer, player, payments);
            }
            if (InvalidCustomPayment(buyer, player, payments, options, free))
            {
                if (DebugMode) Puts($"{user.Name} ({user.Id}): attempt to take custom payment failed");
                return;
            }
            if (InvalidEconomicsPayment(buyer, player, payments, money, free))
            {
                if (DebugMode) Puts($"{user.Name} ({user.Id}): attempt to take payment failed");
                return;
            }
            if (InvalidServerRewardsPayment(buyer, player, payments, points, free))
            {
                if (DebugMode) Puts($"{user.Name} ({user.Id}): attempt to take rp failed");
                return;
            }
            if (payments.valid)
            {
                ProcessValidPayments(user, buyer, player, payments, mode, reset, free, value, args);
            }
            else ProcessInvalidPayments(buyer, options, level, money, points, reset);
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

        private bool ProcessValidPayments(IPlayer user, BasePlayer buyer, BasePlayer player, Payments payments, string mode, bool reset, bool free, string value, string[] args)
        {
            if (!reset)
            {
                if (value != null && GetFileMode(user, buyer, value) == RaidableMode.Random) value = null;
                if (value != null && Buildings.Profiles.ContainsKey(value) && !FileExists(value)) value = null;
                if (config.Settings.Buyable.Refunds.Repeat && despawnCooldowns.TryGetValue(player.userID, out var t) && t.Item2 == mode && FileExists(t.Item1)) value = t.Item1;

                payments.type = GetRequestedPurchaseType(player, args);

                if (BuyRaid(mode, payments, player, value, free))
                {
                    if (DebugMode) Puts($"{user.Name} ({user.Id}): successful payment");
                    payments.Take(false);
                    return true;
                }

                return false;
            }
            else if (data.BuyableCooldowns.Remove(player.userID))
            {
                payments.Take(true);
                UI.UpdateUi(player, UiType.Cooldown);
                SendNotification(buyer, "RemovedCooldownFor", player.displayName, player.UserIDString);
                return true;
            }
            SendNotification(buyer, "NoCooldownFor", player.displayName, player.UserIDString);
            return false;
        }

        private void ProcessInvalidPayments(BasePlayer buyer, List<CustomCostOptions> options, int level, double money, int points, bool reset)
        {
            bool hasCustomCost = options.Exists(o => o.isItem || o.isPlugin);

            if (money > 0 && (!Economics.CanCall() && !IQEconomic.CanCall() && !BankSystem.CanCall()))
            {
                if (DebugMode) Puts($"{buyer.displayName} ({buyer.UserIDString}): invalid payment, no economy plugin is loaded");
                SendNotification(buyer, "EconomicsWithdrawDisabled");
            }
            else if (points > 0 && !ServerRewards.CanCall())
            {
                if (DebugMode) Puts($"{buyer.displayName} ({buyer.UserIDString}): invalid payment, server rewards is not loaded");
                SendNotification(buyer, "ServerRewardPointsDisabled");
            }
            else if (!reset && hasCustomCost && !config.Settings.Include.Custom)
            {
                if (DebugMode) Puts($"{buyer.displayName} ({buyer.UserIDString}): invalid custom payment configuration, Require Custom Costs is not true!");
                SendNotification(buyer, "CustomWithdrawDisabled");
            }
            else if (!reset && !hasCustomCost && config.Settings.Include.Custom)
            {
                if (DebugMode) Puts($"{buyer.displayName} ({buyer.UserIDString}): invalid custom payment configuration, no payment is enabled!");
                SendNotification(buyer, "CustomWithdrawDisabled");
            }
            else
            {
                if (DebugMode) Puts($"{buyer.displayName} ({buyer.UserIDString}): invalid payment");
                SendNotification(buyer, "NoBuyableEventsCostConfigured");
            }
        }

        public bool IsQueued(BasePlayer player, HashSet<ulong> members)
        {
            foreach (ulong member in members)
            {
                foreach (var sp in Queues.queue)
                {
                    if (sp.type == RaidableType.Purchased && sp.userid == member)
                    {
                        SendNotification(player, player.userID == sp.userid ? "BuyableAlreadyQueued" : "BuyableAlreadyQueuedAllied");

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
                Reply(user, "GridIsLoading");
                return;
            }

            var player = user.Player();
            bool isAdmin = user.IsServer || player.IsAdmin;
            bool isConnected = player != null && player.IsConnected;
            string arg = args.Length >= 1 ? args[0].ToLower() : string.Empty;

            switch (arg)
            {
                case "pvp":
                    {
                        var nearest = GetNearestBase(player.transform.position);
                        if (nearest == null || nearest.AllowPVP || !nearest.IsParticipant(player) || !CanBypassLock(nearest, player))
                        {
                            SendNotification(player, "CommandNotAllowed");
                            return;
                        }
                        if (nearest.Type == RaidableType.Purchased && player.HasPermission("raidablebases.buyraid.pveonly"))
                        {
                            SendNotification(player, "CommandNotAllowed");
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
                        Reply(user, $"RaidableBases {Version} by nivex");
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
                            if (user.IsServer) { Puts("You must specify a user! rb unban <steamid>"); return; }
                            Revoke(user.Id);
                        }
                        void Revoke(string userid)
                        {
                            foreach (var group in permission.GetUserGroups(userid))
                            {
                                if (permission.GroupHasPermission(group, "raidablebases.banned"))
                                {
                                    permission.RevokeGroupPermission(group, "raidablebases.banned");
                                    ReplyOrLog(user, $"Banned permission has been removed from group: {group}");
                                }
                            }
                            if (permission.UserHasPermission(userid, "raidablebases.banned"))
                            {
                                permission.RevokeUserPermission(userid, "raidablebases.banned");
                                ReplyOrLog(user, $"Banned permission has been revoked.");
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
                            Reply(user, ret ? "Wipe successful." : "There's nothing to wipe.");
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
                            Reply(user, $"Bypassing wipe time check: {isDifficultyEnabledAfterWipeOverridden}");
                        }

                        return;
                    }
                case "ignore_restart":
                    {
                        if (isAdmin)
                        {
                            bypassRestarting = !bypassRestarting;
                            Reply(user, $"Bypassing restart check: {bypassRestarting}");
                        }

                        return;
                    }
                case "savefix":
                    {
                        if (user.IsAdmin || user.HasPermission("raidablebases.allow"))
                        {
                            int removed = BaseEntity.saveList.RemoveWhere(IsKilled);

                            Reply(user, $"Removed {removed} invalid entities from the save list.");

                            if (SaveRestore.IsSaving)
                            {
                                SaveRestore.IsSaving = false;
                                Reply(user, "Server save has been canceled. You must type server.save again, and then restart your server.");
                            }
                            else Reply(user, "Server save is operating normally.");
                        }

                        return;
                    }
                case "tp":
                    {
                        if (isConnected && (isAdmin || user.HasPermission("raidablebases.allow")))
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
                            if (SpawnsController.IsSafeZone(player.transform.position)) Reply(user, "Safe zone position");
                            if (SpawnsController.IsMonumentPosition(player.transform.position, 0f)) Reply(user, "Monument position");
                        }
                        return;
                    }
                case "grid":
                    {
                        if (isConnected && (isAdmin || user.HasPermission("raidablebases.ddraw")))
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
                            Reply(user, $"Cleared and refunded {num} in the queue.");
                        }
                        return;
                    }
                case "resetui":
                    {
                        if (player == null)
                        {
                            return;
                        }

                        UiType uiType = args.Length == 1 ? UiType.Invalid : GetMovableUiType(args);

                        if (args.Length > 1 && uiType == UiType.Invalid)
                        {
                            SendNotification(player, "Invalid argument!");
                            return;
                        }

                        _targetInfo?.Hide(player);
                        UI.DestroyAllUi(player);

                        if (args.Length == 1)
                        {
                            UI.Offsets.Remove(player.userID);
                        }
                        else if (UI.Offsets.TryGetValue(player.userID, out Dictionary<UiType, UiOffsets> offsets))
                        {
                            offsets.Remove(uiType);
                        }

                        UI.SaveOffsetData();
                        UI.UpdateUi(player, UiType.Buyable);
                        UI.UpdateUi(player, UiType.Cooldown);
                        UI.UpdateUi(player, UiType.Delay);
                        UI.UpdateUi(player, UiType.Lockout);
                        UI.UpdateUi(player, UiType.PasteProgress);
                        UI.UpdateUi(player, UiType.Status);
                        UI.UpdateUi(player, UiType.Teleport);
                        SendNotification(player, "Your UI settings have been reset to defaults.");
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

            if (isConnected)
            {
                DrawRaidLocations(player, isAdmin || player.HasPermission("raidablebases.ddraw"));
            }
        }

        private void SetDefaultsForOffsetType(BasePlayer player, UiType uiType, UiOffsets offsets)
        {
            if (player == null || offsets == null)
            {
                return;
            }

            switch (uiType)
            {
                case UiType.Buyable:
                    (config.UI.Buyable.OffsetMin, config.UI.Buyable.OffsetMax, config.UI.Buyable.NormalizedAnchor) = (offsets.Min, offsets.Max, offsets.NormalizedAnchor);
                    UI.DefaultBuyableOffsets = offsets.Clone();
                    break;
                case UiType.Cooldown:
                    (config.UI.BuyableCooldowns.OffsetMin, config.UI.BuyableCooldowns.OffsetMax, config.UI.BuyableCooldowns.NormalizedAnchor) = (offsets.Min, offsets.Max, offsets.NormalizedAnchor);
                    UI.DefaulCooldownOffsets = offsets.Clone();
                    break;
                case UiType.Delay:
                    (config.UI.Delay.OffsetMin, config.UI.Delay.OffsetMax, config.UI.Delay.NormalizedAnchor) = (offsets.Min, offsets.Max, offsets.NormalizedAnchor);
                    UI.DefaultDelayOffsets = offsets.Clone();
                    break;
                case UiType.Lockout:
                    (config.UI.Lockout.OffsetMin, config.UI.Lockout.OffsetMax, config.UI.Lockout.NormalizedAnchor) = (offsets.Min, offsets.Max, offsets.NormalizedAnchor);
                    UI.DefaultLockoutOffsets = offsets.Clone();
                    break;
                case UiType.PasteProgress:
                    (config.UI.PasteProgress.OffsetMin, config.UI.PasteProgress.OffsetMax, config.UI.PasteProgress.NormalizedAnchor) = (offsets.Min, offsets.Max, offsets.NormalizedAnchor);
                    UI.DefaultPasteProgressOffsets = offsets.Clone();
                    break;
                case UiType.Status:
                    (config.UI.Status.OffsetMin, config.UI.Status.OffsetMax, config.UI.Status.NormalizedAnchor) = (offsets.Min, offsets.Max, offsets.NormalizedAnchor);
                    UI.DefaultStatusOffsets = offsets.Clone();
                    break;
                case UiType.Teleport:
                    (config.UI.Teleport.OffsetMin, config.UI.Teleport.OffsetMax, config.UI.Teleport.NormalizedAnchor) = (offsets.Min, offsets.Max, offsets.NormalizedAnchor);
                    UI.DefaultTeleportOffsets = offsets.Clone();
                    break;
                default:
                    return;
            }

            SendNotification(player, $"You have saved the default offsets for the {uiType} UI.");

            SaveConfig();

            using var offsetCollections = UI.Offsets.Values.ToPooledList();

            foreach (var col in offsetCollections)
            {
                if (col.ContainsKey(uiType))
                {
                    col[uiType] = offsets.Clone();
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
            return raid.ownerId == 0uL || raid.BypassUseOwners() || raid.IsAlly(player);
        }

        public void HandleHintsCommand(BasePlayer player)
        {
            var nearest = GetNearestBase(player.transform.position);
            if (nearest == null)
            {
                SendNotification(player, "TargetTooFar");
                return;
            }

            var opt = nearest.Options.DrawLoot;
            bool canBypass = opt.CanBypass && nearest.CanBypass(player);

            if (!canBypass)
            {
                if (nearest.HintCooldowns.Count > 0)
                {
                    SendNotification(player, "CommandNotAllowed");
                    return;
                }

                if (!nearest.Options.Permission.Has(player, nearest.Type) || !string.IsNullOrWhiteSpace(opt.Permission) && !player.HasPermission(opt.Permission))
                {
                    SendNotification(player, "No Permission");
                    return;
                }

                if (!opt.Enabled || opt.DrawTime <= 0f)
                {
                    SendNotification(player, "CommandNotAllowed");
                    return;
                }

                if (!nearest.IsParticipant(player) || !CanBypassLock(nearest, player))
                {
                    SendNotification(player, "OwnerLocked");
                    return;
                }

                if (!nearest.RequiredLootPercentageMet(opt.RequiredLootPercentage, out double percentageMet))
                {
                    SendNotification(player, "Hints Loot Requirement", Math.Round(percentageMet, 2), opt.RequiredLootPercentage);
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

            SendNotification(player, objects.Count > 0 ? "Hints Drawn On Screen" : "Hints None Available");

            HarmonyModInterface.CallHook("OnRaidableBaseHint", player, nearest.Location, nearest.ProtectionRadius, nearest.Options.Level, nearest.GetLootAmountCounted(), nearest.GetOwner(), nearest.GetRaiders());
        }

        public void HandleUiCommand(BasePlayer player, string[] args)
        {
            UiType uiType = GetMovableUiType(args);

            if (!isInitialized || player == null || args.Length == 1 || uiType == UiType.Invalid)
            {
                SendNotification(player, "Invalid argument!");
                return;
            }

            UiOffsets offsets = UI.GetOffsets(player.userID, uiType).Clone();
            UI.DestroyUi(player, uiType);
            SetDefaultsForOffsetType(player, uiType, offsets);
        }

        private void CommandInvite(IPlayer user, BasePlayer player, string[] args)
        {
            if (args.Length < 2) { Reply(user, "Invite Usage", config.Settings.HunterCommand); return; }
            if (!(RustCore.FindPlayer(args[1]) is BasePlayer target)) { Reply(user, "TargetNotFoundId", args[1]); return; }
            var isAllowed = user.IsServer || player.IsAdmin || player.HasPermission("fauxadmin.allowed");
            var raid = isAllowed ? GetNearestBase(target.transform.position) : Raids.FirstOrDefault(x => x.ownerId.IsSteamId() && (x.ownerId == player.userID || x.IsAlly(player, x.ownerId)));
            if (raid == null) { Reply(user, isAllowed ? "TargetTooFar" : "Invite Ownership Error"); return; }
            if (!isAllowed && !player.HasPermission("raidablebases.invitecommand") && !raid.IsAlly(player, target)) { Reply(user, "Invite Not Ally"); return; }
            if (!isAllowed && !raid.IsPayLocked && raid.HasLockout(target)) { Reply(user, "Invite Lockout Error"); SendNotification(target, "Invite Failed"); return; }
            if (!raid.raiders.TryGetValue(target.userID, out var raider)) raid.raiders[target.userID] = raider = new(target);
            if (InRange(raid.Location, target.transform.position, raid.ProtectionRadius * 1.5f)) raider.lastActiveTime = Time.timeAsDouble;
            if (user.IsServer || player.IsAdmin || user.HasPermission("raidablebases.allow")) Reply(user, $"You can use this command to set them as the owner of this raid: {config.Settings.EventCommand} setowner {target.userID}");
            raider.IsAlly = true;
            raider.IsAllowed = true;
            raider.IsParticipant = true;
            SendNotification(target, "Invite Allowed", user.Name);
            Reply(user, "Invite Success", target.displayName);
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
                        int num = BasePlayer.activePlayerList.Count(x => x != null && x.Distance(raid.Location) <= raid.ProtectionRadius * 3f);
                        int distance = Mathf.CeilToInt(player.transform.position.Distance(raid.Location));
                        string message = mx("RaidMessage", player.UserIDString, distance, num);
                        string flag = mx(raid.GetAllowKey(), player.UserIDString);

                        DrawText(player, 15f, Color.yellow, raid.Location, string.Format("<size=24>{0}{1} {2} [{3} {4}] {5}</size>", raid.BaseName, flag, raid.Type + ":" + raid.Mode(player.UserIDString, true), message, FormatGridReference(player, raid.Location), raid.Location));

                        foreach (var ri in raid.raiders.Values)
                        {
                            BasePlayer ally = ri.player;

                            if (!ri.IsAlly || ally == null || !ally.IsConnected)
                            {
                                continue;
                            }

                            DrawText(player, 15f, Color.yellow, ally.transform.position, $"<size=24>{mx("Ally", player.UserIDString).Replace(":", string.Empty)}</size>");
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

            SendNotification(user, "Next", message);
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

            SendNotification(user, "RankedPoints", info.Raids, info.Points, info.TotalRaids, info.TotalPoints, user.IsServer ? rf(sb.ToString()) : sb.ToString());
            SendNotification(user, "RankedWins2", config.Settings.HunterCommand);
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
                SendNotification(user, "Your ranked stats have been reset.");
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

            if (ladder.Count < 30 && ConVar.Server.hostname.EndsWith("rs Test Server"))
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
                SendNotification(user, "Ladder Insufficient Players");
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

            SendNotification(user, sb.ToString());
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

        private bool GetModeFromLevel(int level, out string mode)
        {
            mode = null;

            foreach (var profile in Buildings.Profiles.Values)
            {
                string m = profile?.Options?.Mode;
                if (string.IsNullOrWhiteSpace(m) || !IsModeValid(m))
                {
                    continue;
                }

                if (profile.Options.Level == level)
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

            if (int.TryParse(value, out int level) && GetModeFromLevel(level, out string modeFromLevel))
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
                if (profile.Options.Mode == key || key.Equals(baseName, StringComparison.OrdinalIgnoreCase) || profile.Options.AdditionalBases.Exists(extra => extra.Key.Equals(baseName, StringComparison.OrdinalIgnoreCase)))
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

            if (HandledCommandArguments(player, user, isAllowed, args))
            {
                return;
            }

            string mode = RaidableMode.Random;
            string baseName = null;

            if (command == config.Settings.EventCommand && !TryResolveManualEventArguments(user, args, out mode, out baseName))
            {
                return;
            }

            if (!CanCommandContinue(player, user, isAllowed))
            {
                return;
            }
            if (RaidableModes.Count == 0)
            {
                Reply(user, "GridIsLoading");
                return;
            }
            if (command == config.Settings.EventCommand) // rbe
            {
                ProcessEventCommand(user, player, isAllowed, mode, baseName);
            }
            else if (command == config.Settings.ConsoleCommand) // rbevent
            {
                ProcessConsoleCommand(user, player, isAllowed, args);
            }
        }

        protected void ProcessEventCommand(IPlayer user, BasePlayer player, bool isAllowed, string mode, string baseName) // rbe
        {
            if (!isAllowed || player == null || !player.IsConnected)
            {
                return;
            }

            if (baseName != null && !FileExists(baseName))
            {
                SendNotification(user, "FileDoesNotExist2", baseName);
                return;
            }

            var (key, profile) = GetBuilding(RaidableType.Manual, mode, baseName, null);

            if (!IsProfileValid(key, profile, true, RaidableType.Manual))
            {
                SendNotification(user, profile == null ? "BuildingNotConfigured" : GetDebugMessage(mode, RaidableType.Manual, false, true, user.Id, key, profile.Options));
                return;
            }

            if (!Physics.Raycast(player.eyes.HeadRay(), out var hit, isAllowed ? Mathf.Infinity : 100f, targetMask2 | Layers.Mask.Default, QueryTriggerInteraction.Ignore))
            {
                SendNotification(user, "LookElsewhere");
                return;
            }

            if (!player.IsAdmin && SpawnsController.IsAreaSafetyQueryAvailable)
            {
                SendNotification(user, "You must wait to use this command until the current request is completed.");
                return;
            }

            var safeRadius = Mathf.Max(M_RADIUS * 2f, profile.Options.ArenaWalls.Radius);
            var safe = player.IsAdmin || SpawnsController.IsAreaSafe(hit.point, 0f, safeRadius, safeRadius, safeRadius, manualMask, false, out _, RaidableType.Manual, profile.Options.CustomSpawns);

            if (!safe && !player.IsFlying && InRange(player.transform.position, hit.point, 50f))
            {
                SendNotification(user, "PasteIsBlockedStandAway");
                return;
            }

            bool pasted = false;

            if (safe && (isAllowed || !SpawnsController.IsMonumentPosition(hit.point, profile.Options.ProtectionRadius(RaidableType.Manual))))
            {
                var spawns = GridController.Spawns.Values.FirstOrDefault(s => s.GetLocations(CacheType.Generic).Exists(t => InRange2D(t.Location, hit.point, M_RADIUS)) || s.GetLocations(CacheType.Seabed).Exists(t => InRange2D(t.Location, hit.point, M_RADIUS)));
                RandomBase rb = new();
                rb.Position = hit.point;
                rb.user = user;
                rb.Instance = this;
                rb.BaseName = key;
                rb.Profile = profile;
                rb.type = RaidableType.Manual;
                rb.spawns = spawns ??= new(this);
                rb.payments = new();
                rb.payments.admin = player.IsAdmin;
                rb.pasteData = GetPasteData(key);
                rb.admin = player.IsAdmin && player.IsFlying && player.isInvisible && player.IsHoldingEntity<Hammer>() ? player : null;
                ParseListedOptions(rb);
                pasted = true;
                loadCoroutines[rb] = ServerMgr.Instance.StartCoroutine(PasteManualEvent(rb, hit.point, player, user));
            }
            else SendNotification(user, "PasteIsBlocked");

            if (!pasted && Queues.Messages.Any())
            {
                SendNotification(user, IsGridLoading() ? "GridIsLoading" : Queues.Messages.GetLast(user.Id));
            }
        }

        protected void ProcessConsoleCommand(IPlayer user, BasePlayer player, bool isAllowed, string[] args) // rbevent
        {
            if (IsGridLoading())
            {
                int count = GridController.Spawns.TryGetValue(RaidableType.Grid, out var value) ? value.Spawns.Count : 0;
                SendNotification(user, "GridIsLoadingFormatted", (Time.realtimeSinceStartupAsDouble - GridController.gridTime).ToString("N02"), count);
                return;
            }
            if (isAllowed)
            {
                BasePlayer owner = null;
                if (args.Length == 2 && ulong.TryParse(args[1], out var id) && id.IsSteamId()) owner = BasePlayer.FindByID(id);
                int events = 1;
                if (args.Length == 2 && !args[1].IsSteamId() && int.TryParse(args[1], out var evts) && MeetsManualEventMaximum(owner, user, evts)) events = evts;
                for (int i = 0; i < events; i++) { SpawnRandomBase(RaidableType.Manual, GetRaidableMode(Array.Find(args, IsRaidableMode)), Array.Find(args, FileExists), isAllowed, null, owner, isAllowed && user.IsConnected ? user : null); }
                SendNotification(player, "BaseQueued", Queues.queue.Count);
            }
        }

        private bool CanCommandContinue(BasePlayer player, IPlayer user, bool isAllowed)
        {
            if (!IsPasteEngineReady(out var error))
            {
                Reply(user, error);
                return false;
            }

            return MeetsManualEventMaximum(player, user, 1);
        }

        private bool TryResolveManualEventArguments(IPlayer user, string[] args, out string mode, out string baseName)
        {
            bool hasDifficulty = false;
            bool hasBaseName = false;
            mode = RaidableMode.Random;
            baseName = null;

            foreach (string arg in args)
            {
                bool isDifficulty = TryGetManualEventMode(arg, out string resolvedMode);
                bool isBase = TryGetManualEventBaseName(arg, out string resolvedBaseName);

                if (!isDifficulty && !isBase)
                {
                    ReplyUnknownManualEventArgument(user, arg);
                    return false;
                }

                if (isDifficulty && !hasDifficulty)
                {
                    mode = resolvedMode;
                    hasDifficulty = true;
                }

                if (isBase && !hasBaseName)
                {
                    baseName = resolvedBaseName;
                    hasBaseName = true;
                }
            }

            return true;
        }

        private bool TryGetManualEventMode(string value, out string mode)
        {
            if (value.Equals(RaidableMode.Random, StringComparison.OrdinalIgnoreCase))
            {
                mode = RaidableMode.Random;
                return true;
            }

            if (int.TryParse(value, out int level) && GetModeFromLevel(level, out mode))
            {
                return true;
            }

            foreach (string configuredMode in GetRaidableModes())
            {
                if (configuredMode.Equals(value, StringComparison.OrdinalIgnoreCase))
                {
                    mode = configuredMode;
                    return true;
                }
            }

            mode = RaidableMode.Random;
            return false;
        }

        private bool TryGetManualEventBaseName(string value, out string baseName)
        {
            foreach (var (key, profile) in Buildings.Profiles)
            {
                if (key.Equals(value, StringComparison.OrdinalIgnoreCase))
                {
                    baseName = key;
                    return true;
                }

                foreach (string additionalBase in profile.Options.AdditionalBases.Keys)
                {
                    if (additionalBase.Equals(value, StringComparison.OrdinalIgnoreCase))
                    {
                        baseName = additionalBase;
                        return true;
                    }
                }
            }

            baseName = null;
            return false;
        }

        private void ReplyUnknownManualEventArgument(IPlayer user, string value)
        {
            using var candidates = DisposableList<string>();
            candidates.AddRange(ManualEventHelperCommands);

            foreach (string mode in GetRaidableModes())
            {
                AddManualEventCandidate(candidates, mode);
            }

            AddManualEventCandidate(candidates, RaidableMode.Random);

            foreach (var (key, profile) in Buildings.Profiles)
            {
                AddManualEventCandidate(candidates, key);

                foreach (string additionalBase in profile.Options.AdditionalBases.Keys)
                {
                    AddManualEventCandidate(candidates, additionalBase);
                }
            }

            int maximumDistance = Math.Max(2, value.Length / 3);
            using var matches = DisposableList<(string Value, int Distance)>();
            using var previous = DisposableList<int>();
            using var current = DisposableList<int>();

            foreach (string candidate in candidates)
            {
                int distance = GetEditDistance(value, candidate, previous, current);

                if (distance <= maximumDistance)
                {
                    matches.Add((candidate, distance));
                }
            }

            matches.Sort((a, b) =>
            {
                int result = a.Distance.CompareTo(b.Distance);
                return result != 0 ? result : string.Compare(a.Value, b.Value, StringComparison.OrdinalIgnoreCase);
            });

            using var closestMatches = DisposableList<string>();

            for (int i = 0; i < matches.Count && i < 3; i++)
            {
                closestMatches.Add(matches[i].Value);
            }

            string suggestions = closestMatches.Count == 0
                ? string.Empty
                : en
                    ? $" Did you mean: {string.Join(", ", closestMatches)}?"
                    : $" Возможно, вы имели в виду: {string.Join(", ", closestMatches)}?";

            ReplyOrLog(user, en
                ? $"Unknown /{config.Settings.EventCommand} argument '{value}'. Nothing was spawned.{suggestions}"
                : $"Неизвестный аргумент /{config.Settings.EventCommand}: '{value}'. База не была создана.{suggestions}");
        }

        private static void AddManualEventCandidate(List<string> candidates, string value)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].Equals(value, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            candidates.Add(value);
        }

        private static int GetEditDistance(string value, string candidate, List<int> previous, List<int> current)
        {
            if (string.IsNullOrEmpty(value))
            {
                return candidate?.Length ?? 0;
            }

            if (string.IsNullOrEmpty(candidate))
            {
                return value.Length;
            }

            previous.Clear();
            current.Clear();

            for (int i = 0; i <= candidate.Length; i++)
            {
                previous.Add(i);
                current.Add(0);
            }

            for (int valueIndex = 1; valueIndex <= value.Length; valueIndex++)
            {
                current[0] = valueIndex;
                char valueCharacter = char.ToLowerInvariant(value[valueIndex - 1]);

                for (int candidateIndex = 1; candidateIndex <= candidate.Length; candidateIndex++)
                {
                    int substitutionCost = valueCharacter == char.ToLowerInvariant(candidate[candidateIndex - 1]) ? 0 : 1;
                    current[candidateIndex] = Math.Min(
                        Math.Min(current[candidateIndex - 1] + 1, previous[candidateIndex] + 1),
                        previous[candidateIndex - 1] + substitutionCost);
                }

                (previous, current) = (current, previous);
            }

            return previous[candidate.Length];
        }

        private bool MeetsManualEventMaximum(BasePlayer player, IPlayer user, int num)
        {
            if (!(user.IsServer || player.IsAdmin || user.HasPermission("raidablebases.bypassmaxmanualeventlimit")) && Get(RaidableType.Manual) + num > config.Settings.Manual.Max)
            {
                SendNotification(user, "Max Events", RaidableType.Manual, config.Settings.Manual.Max);
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

            bool isConnected = player != null && player.IsConnected;
            switch (args[0].ToLower())
            {
                case "despawn":
                    if (isConnected && (isAllowed || player.HasPermission("raidablebases.despawn.buyraid")))
                    {
                        DespawnBase(player, isAllowed);
                    }
                    return true;
                case "draw":
                    if (isConnected)
                    {
                        DrawSpheres(player, isAllowed);
                    }
                    return true;
                case "debug":
                    {
                        if (!isAllowed) return true;
                        DebugMode = !DebugMode;
                        Queues.Messages.User = DebugMode ? user : null;
                        Reply(user, $"Debug mode (v{Version}): {DebugMode}");
                        if (DebugMode)
                        {
                            if (!_ownershipReady) Reply(user, "Steam Inventory definitions are not yet available.");
                            if (IsGridBroken())
                            {
                                Reply(user, "Another plugin has prevented the grid from loading? It is not functioning, it has been canceled by another process.");
                            }
                            if (GridController.step != 0)
                            {
                                if (GridController.step == int.MaxValue) Reply(user, "Grid has not initialized.");
                                else if (GridController.step > 0) Reply(user, $"Grid last completed step: {GridController.step - 1} with {GridController.progress}/{GridController.progressTotal} read");
                            }
                            TimeSpan uptime = TimeSpan.FromSeconds(Time.realtimeSinceStartupAsDouble);
                            Reply(user, $"Server Uptime: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s");
                            Reply(user, $"Scheduled Events Running: {Automated._scheduledCoroutine != null}");
                            Reply(user, $"Maintained Events Running: {Automated._maintainedCoroutine != null}");
                            Reply(user, $"Queues Pending: {Queues.queue.Count}");
                            if (!AnyCopyPasteFileExists)
                            {
                                Reply(user, "No copypaste file in any profile exists!");
                            }
                            if (Queues.Messages.Any())
                            {
                                Reply(user, $"DEBUG: Last messages:");
                                Queues.Messages.PrintAll(user);
                            }
                            else Reply(user, "No debug messages.");
                            if (exConf is JsonException)
                            {
                                Reply(user, $"{exConf.Message}\n\n\nYour config contains a json error!");
                            }
                            foreach (var error in profileErrors)
                            {
                                Reply(user, $"Json error found in {error}");
                            }
                            int points = 0;
                            foreach (var (type, spawns) in GridController.Spawns)
                            {
                                if (spawns.Spawns.Count > 0)
                                {
                                    Reply(user, $"Potential points on {type}: {spawns.Spawns.Count} available/{spawns.Cached.Select(x => x.Value).Count()} with temporary holds.");
                                    points += spawns.Spawns.Count;
                                }
                            }
                            if (IsGridBroken())
                            {
                                if (points > 1000) { GridController.gridCoroutine = null; Puts("Grid activated with {0} points, you need to find whatever plugin you have that's breaking this plugin. There's no reason the grid should partially load then stop without finishing.", points); }
                                else Reply(user, "You must reload RaidableBases or type rb.reloadconfig to load the grid.");
                            }

                        }
                        return true;
                    }
                case "kill_cleanup":
                    {
                        if (!isAllowed || player == null) return true;
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
                        if (num == 0) Reply(user, "You must use the command near the base that you want to despawn. It cannot be owned by a player.");
                        else Reply(user, $"Kill sent for {num} entities.");
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
                            if (mode == RaidableMode.Random) mode = GetRaidableModes().GetSecureRandom();
                            RaidableBase.GenerateLoot(this, user, mode, args);
                        }
                        return true;
                    }
                case "active":
                    {
                        if (!isAllowed) return true;

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
                                    if (FileExists(extra) && data.Cycle.CanSpawn(RaidableType.Maintained, profile.Options.Mode, extra, player, false))
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

                        Reply(user, sb.ToString());

                        return true;
                    }
                case "expire":
                case "resetcooldown":
                    {
                        if (!isAllowed) return true;
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
                                    Reply(user, "RemovedCooldownFor", target.displayName, target.UserIDString);
                                }
                                if (args.Length == 2 || args[2] == "lockout")
                                {
                                    data.Lockouts.Remove(target.UserIDString);
                                    UI.UpdateUi(target, UiType.Lockout);
                                    SendNotification(user, "RemovedLockFor", target.displayName, target.UserIDString);
                                }
                            }
                            return true;
                        }
                        Reply(user, "Target not found");
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
                                    SendNotification(user, "TargetTooFar");
                                }
                                else if (raid.TrySetPayLock(new(target) { Economics = new(this, target) }, !args.Contains("lockout")))
                                {
                                    SendNotification(user, "RaidLockedTo", target.displayName);
                                }
                                else SendNotification(user, "You must use clearowner first.");
                            }
                            else SendNotification(user, "TargetNotFoundId", args[1]);
                        }

                        return true;
                    }
                case "clearowner":
                    {
                        if (isConnected && (isAllowed || user.HasPermission("raidablebases.clearowner")))
                        {
                            var target = player;
                            if (isAllowed && args.Length >= 2 && RustCore.FindPlayer(args[1]) is BasePlayer other)
                            {
                                target = other;
                            }
                            if (!(GetNearestBase(target.transform.position) is RaidableBase raid))
                            {
                                SendNotification(user, "TooFar");
                            }
                            else if (isAllowed || raid.ownerId == player.userID)
                            {
                                raid.ResetEventLock(false);
                                raid.raiders.Clear();
                                SendNotification(user, "RaidOwnerCleared");
                            }
                            else SendNotification(user, "OwnerLocked");
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
                Reply(user, $"Toggled maintained events {(Automated.IsMaintainedEnabled ? "on" : "off")}");
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
                Reply(user, $"Toggled scheduled events {(Automated.IsScheduledEnabled ? "on" : "off")}");
                if (args.Contains("scheduled"))
                {
                    config.Settings.Schedule.Enabled = Automated.IsScheduledEnabled;
                    SaveConfig();
                    return;
                }
            }

            if (config.Settings.Buyable.Max > 0)
            {
                Reply(user, $"Toggled buyable events {((buyableEnabled = !buyableEnabled) ? "on" : "off")}");
            }

            Queues.Paused = !buyableEnabled && !Automated.IsScheduledEnabled && !Automated.IsMaintainedEnabled;
            IsScheduledReload = args.Contains("scheduled_reload") && Queues.Paused;
            if (args.Contains("scheduled_reload"))
            {
                Reply(user, $"Scheduled reload after all events despawn has been {(IsScheduledReload ? "enabled" : "disabled")}");
            }
            Reply(user, $"Toggled queue/spawn manager {(Queues.Paused ? "off" : "on")}");
        }

        private void CommandPopulate(IPlayer user, string command, string[] args)
        {
            if (args.Length == 0)
            {
                Reply(user, "Valid arguments: 0 1 2 3 4 all");
                return;
            }

            using var lootList = DisposableList<LootItem>();

            foreach (ItemDefinition def in ItemManager.GetItemDefinitions())
            {
                if (!BlacklistedItems.Contains(def.shortname))
                {
                    lootList.Add(new(def.shortname));
                }
            }

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

                        Reply(user, $"Created Editable_Lists/{mode}.json");
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
                SendNotification(user, val.profile.Options.Enabled ? "ToggleProfileEnabled" : "ToggleProfileDisabled", val.key);
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
                ReplyOrLog(user, $"\n{sb}\nChanged {search} for {changes} bases to {value}");
            }
            else ReplyOrLog(user, "No changes required.");
        }


        private bool TryGetConfigProfile(IPlayer user, string[] args, out string profileName, out BaseProfile profile)
        {
            profileName = args.Length > 1 ? string.Join(" ", args, 1, args.Length - 1).Trim().Trim('"') : string.Empty;

            if (string.IsNullOrWhiteSpace(profileName))
            {
                ReplyOrLog(user, "You must specify a profile name.");
                profile = null;
                return false;
            }

            foreach (var entry in Buildings.Profiles)
            {
                if (entry.Key.Equals(profileName, StringComparison.OrdinalIgnoreCase))
                {
                    profileName = entry.Key;
                    profile = entry.Value;
                    return true;
                }
            }

            ReplyOrLog(user, $"Profile '{profileName}' was not found.");
            profile = null;
            return false;
        }

        [Flags]
        private enum ItemDefinitionFlags
        {
            None = 0,
            ItemModDeployable = 1 << 0,
            ItemModEntity = 1 << 1,
            ItemModProjectile = 1 << 2,
            ItemModCatapultBoulder = 1 << 3,
            ItemModCookable = 1 << 4,
            AttackEntity = 1 << 5,
            BaseSiegeWeapon = 1 << 6,
            ThrownWeapon = 1 << 7
        }

        private readonly Dictionary<ItemDefinition, ItemDefinitionFlags> DefinitionToItemModType = new();

        private void AddDefinitionFlag(ItemDefinition def, ItemDefinitionFlags flag)
        {
            DefinitionToItemModType.TryGetValue(def, out var flags);
            DefinitionToItemModType[def] = flags | flag;
        }

        private bool HasDefinitionFlag(ItemDefinition def, ItemDefinitionFlags flags)
        {
            return def != null && DefinitionToItemModType.TryGetValue(def, out var value) && (value & flags) != 0;
        }

        private bool IsWeaponItemDefinition(ItemDefinition def)
        {
            return def != null && (def.category == ItemCategory.Weapon || HasDefinitionFlag(def, ItemDefinitionFlags.AttackEntity | ItemDefinitionFlags.BaseSiegeWeapon));
        }

        private bool IsAmmoItemDefinition(ItemDefinition def)
        {
            return def != null && (def.category == ItemCategory.Ammunition || HasDefinitionFlag(def, ItemDefinitionFlags.ItemModProjectile | ItemDefinitionFlags.ItemModCatapultBoulder));
        }

        private bool IsCookableItemDefinition(ItemDefinition def) => HasDefinitionFlag(def, ItemDefinitionFlags.ItemModCookable);

        private bool IsThrownWeaponItemDefinition(ItemDefinition def) => HasDefinitionFlag(def, ItemDefinitionFlags.ThrownWeapon);

        private void CommandAcceptedItems(IPlayer user, string[] args)
        {
            BasePlayer player = user.Player();
            if (player == null)
            {
                ReplyOrLog(user, "This command must be used by a player.");
                return;
            }

            if (!TryGetConfigProfile(user, args, out string profileName, out var profile))
            {
                return;
            }

            List<string> configured = profile.Options.AllowedWeaponAndAmmoShortnames;
            HashSet<string> accepted = new(configured, StringComparer.OrdinalIgnoreCase);
            int added = 0;

            void Add(ItemDefinition def)
            {
                if (def != null && accepted.Add(def.shortname))
                {
                    configured.Add(def.shortname);
                    added++;
                }
            }

            void ProcessItem(Item item)
            {
                if (item?.info == null)
                {
                    return;
                }

                bool isWeapon = IsWeaponItemDefinition(item.info);

                if (isWeapon || IsAmmoItemDefinition(item.info))
                {
                    Add(item.info);
                }

                if (isWeapon && item.GetHeldEntity() is BaseProjectile projectile)
                {
                    Add(projectile.primaryMagazine?.ammoType);
                }
            }

            using var items = player.GetAllItems();
            items.ForEach(ProcessItem);

            Item backpack = player.inventory.GetBackpackWithInventory();
            backpack?.contents?.itemList?.ForEach(ProcessItem);

            configured.Sort(StringComparer.OrdinalIgnoreCase);
            SaveProfile(profileName, profile.Options);
            ReplyOrLog(user, added > 0
                ? $"Added {added} weapon and ammo shortname{(added == 1 ? string.Empty : "s")} to '{profileName}'."
                : $"No new weapon or ammo shortnames were found for '{profileName}'.");
        }

        private void CommandRetrieveItems(IPlayer user, string[] args)
        {
            BasePlayer player = user.Player();
            if (player == null)
            {
                ReplyOrLog(user, "This command must be used by a player.");
                return;
            }

            if (!TryGetConfigProfile(user, args, out string profileName, out var profile))
            {
                return;
            }

            List<string> configured = profile.Options.AllowedWeaponAndAmmoShortnames;
            if (configured.Count == 0)
            {
                ReplyOrLog(user, $"'{profileName}' has no accepted weapon or ammo shortnames configured.");
                return;
            }

            HashSet<string> unique = new(StringComparer.OrdinalIgnoreCase);
            using var definitions = DisposableList<ItemDefinition>();
            using var invalid = DisposableList<string>();
            using var unsupported = DisposableList<string>();

            foreach (string configuredShortname in configured)
            {
                string shortname = configuredShortname.Trim();
                if (!unique.Add(shortname))
                {
                    continue;
                }

                ItemDefinition def = ItemManager.FindItemDefinition(shortname);
                if (def == null)
                {
                    invalid.Add(shortname);
                    continue;
                }

                if (!IsWeaponItemDefinition(def) && !IsAmmoItemDefinition(def))
                {
                    unsupported.Add(shortname);
                    continue;
                }

                definitions.Add(def);
            }

            if (definitions.Count == 0)
            {
                ReplyOrLog(user, $"'{profileName}' has no valid weapon or ammo items to retrieve." +
                    (invalid.Count > 0 ? $" Invalid shortnames: {string.Join(", ", invalid)}." : string.Empty) +
                    (unsupported.Count > 0 ? $" Non-weapon or ammo shortnames: {string.Join(", ", unsupported)}." : string.Empty));
                return;
            }

            Item backpack = player.inventory.GetBackpackWithInventory();
            ItemContainer backpackContainer = backpack?.contents;

            int GetFreeSlots(ItemContainer container)
            {
                return container == null ? 0 : Math.Max(0, container.capacity - container.itemList.Count);
            }

            bool HasSpaceForItems(int amount)
            {
                int spacesFree = GetFreeSlots(player.inventory.containerMain) +
                    GetFreeSlots(player.inventory.containerBelt) +
                    GetFreeSlots(backpackContainer);

                return amount <= spacesFree;
            }

            if (!HasSpaceForItems(definitions.Count))
            {
                ReplyOrLog(user, $"You do not have enough free inventory slots to retrieve all {definitions.Count} items from '{profileName}'.");
                return;
            }

            int retrieved = 0;
            using var failed = DisposableList<string>();
            GiveItemOptions giveOptions = backpackContainer == null ? GiveItemOptions.None : GiveItemOptions.BackpackOverflow;

            foreach (ItemDefinition def in definitions)
            {
                Item item = ItemManager.Create(def, 1, 0uL);
                if (item == null)
                {
                    failed.Add(def.shortname);
                    continue;
                }

                if (!player.inventory.GiveItem(item, null, giveOptions))
                {
                    failed.Add(def.shortname);
                    item.Remove();
                    continue;
                }

                retrieved++;
            }

            ReplyOrLog(user, $"Retrieved {retrieved} item{(retrieved == 1 ? string.Empty : "s")} from '{profileName}'." +
                (invalid.Count > 0 ? $" Invalid shortnames skipped: {string.Join(", ", invalid)}." : string.Empty) +
                (unsupported.Count > 0 ? $" Non-weapon or ammo shortnames skipped: {string.Join(", ", unsupported)}." : string.Empty) +
                (failed.Count > 0 ? $" Items that could not be given: {string.Join(", ", failed)}." : string.Empty));
        }

        private class ConfigPresetController : IDisposable
        {
            private const string FULL_PREFIX = "RBC2.Full.";
            private const string CONFIG_PREFIX = "RBC2.Config.";
            private const string PROFILES_PREFIX = "RBC2.Profiles.";
            private const string LOOT_TABLES_PREFIX = "RBC2.LootTables.";
            private const string REPLACE = "$";
            private const string IMPORT_UI = "RB_UI_ConfigImport";
            private const int CHUNK_LENGTH = 3000;
            private const int CUI_INPUT_LIMIT = 16000;
            private const int COMMAND_PACKET_OVERHEAD = 64;
            private const int MAX_ENCODED_LENGTH = 4_000_000;
            private const int MAX_JSON_BYTES = 32 * 1024 * 1024;
            private const float IMPORT_TIMEOUT = 900f;
            private RaidableBases Instance;
            private HashSet<ulong> _importUiUsers = new();
            private PresetImport _activeImport;
            private Timer _importTimer;

            private enum PayloadScope
            {
                Full,
                Config,
                Profiles,
                LootTables
            }

            public ConfigPresetController(RaidableBases instance)
            {
                Instance = instance;
            }

            private class Package
            {
                [JsonProperty("v")]
                public int FormatVersion = 4;

                [JsonProperty("r")]
                public string PluginVersion;

                [JsonProperty("e")]
                public bool English;

                [JsonProperty("c")]
                public JObject Config;

                [JsonProperty("b")]
                public JObject ProfileBaseline;

                [JsonProperty("p")]
                public Dictionary<string, JObject> Profiles = new(StringComparer.OrdinalIgnoreCase);

                [JsonProperty("l")]
                public Dictionary<string, List<LootItem>> LootTables = new(StringComparer.OrdinalIgnoreCase);
            }

            private class PayloadContractResolver : DefaultContractResolver
            {
                protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
                {
                    JsonProperty property = base.CreateProperty(member, memberSerialization);

                    if (member.DeclaringType != typeof(Package))
                    {
                        property.PropertyName = member.Name;
                    }

                    return property;
                }

                protected override JsonObjectContract CreateObjectContract(Type objectType)
                {
                    JsonObjectContract contract = base.CreateObjectContract(objectType);
                    var extensionDataGetter = contract.ExtensionDataGetter;
                    var extensionDataSetter = contract.ExtensionDataSetter;

                    if (extensionDataGetter != null)
                    {
                        contract.ExtensionDataGetter = target => extensionDataGetter(target).Select(entry => new KeyValuePair<object, object>($"@{entry.Key}", entry.Value));
                    }

                    if (extensionDataSetter != null)
                    {
                        contract.ExtensionDataSetter = (target, name, value) => extensionDataSetter(target, name.Length > 0 && name[0] == '@' ? name.Substring(1) : name, value);
                    }

                    return contract;
                }
            }

            private static JsonSerializer CreatePayloadSerializer()
            {
                return JsonSerializer.Create(new JsonSerializerSettings
                {
                    ContractResolver = new PayloadContractResolver()
                });
            }

            private static JObject ToPayloadObject(object value)
            {
                return JObject.FromObject(value, CreatePayloadSerializer());
            }

            private static T FromPayloadObject<T>(JToken value)
            {
                return value.ToObject<T>(CreatePayloadSerializer());
            }

            private class CommandFile
            {
                [JsonProperty("Commands")]
                public List<string> Commands = new();
            }

            private class PayloadFile
            {
                [JsonProperty("Full")]
                public string Full;

                [JsonProperty("Config")]
                public string Config;

                [JsonProperty("Profiles")]
                public string Profiles;

                [JsonProperty("Loot Tables")]
                public string LootTables;

                [JsonProperty("Commands")]
                public List<string> Commands;

                [JsonProperty("Config Commands", NullValueHandling = NullValueHandling.Ignore)]
                public List<string> ConfigCommands;

                [JsonProperty("Profiles Commands", NullValueHandling = NullValueHandling.Ignore)]
                public List<string> ProfilesCommands;

                [JsonProperty("Loot Tables Commands", NullValueHandling = NullValueHandling.Ignore)]
                public List<string> LootTablesCommands;
            }

            private class PresetImport
            {
                public string Id;
                public uint Checksum;
                public int TotalLength;
                public string[] Chunks;
                public int Received;
            }

            private static bool HaveSameJsonProperties(JObject left, JObject right)
            {
                if (left.Count != right.Count)
                {
                    return false;
                }

                foreach (var property in left.Properties())
                {
                    if (right.Property(property.Name) == null)
                    {
                        return false;
                    }
                }

                return true;
            }

            private static JToken CreatePatch(JToken baseline, JToken value)
            {
                if (JToken.DeepEquals(baseline, value))
                {
                    return null;
                }

                if (baseline is JObject baselineObject && value is JObject valueObject && HaveSameJsonProperties(baselineObject, valueObject))
                {
                    JObject patch = new();

                    foreach (var property in valueObject.Properties())
                    {
                        JToken childPatch = CreatePatch(baselineObject[property.Name], property.Value);

                        if (childPatch != null)
                        {
                            patch[property.Name] = childPatch;
                        }
                    }

                    return patch.HasValues ? patch : null;
                }

                return new JObject
                {
                    [REPLACE] = value?.DeepClone() ?? JValue.CreateNull()
                };
            }

            private static JToken ApplyPatch(JToken baseline, JToken patch)
            {
                if (patch is not JObject patchObject)
                {
                    return patch?.DeepClone() ?? JValue.CreateNull();
                }

                if (patchObject.Count == 1)
                {
                    JProperty replacement = patchObject.Property(REPLACE);

                    if (replacement != null)
                    {
                        return replacement.Value.DeepClone();
                    }
                }

                JObject result = baseline is JObject baselineObject ? (JObject)baselineObject.DeepClone() : new JObject();

                foreach (var property in patchObject.Properties())
                {
                    result[property.Name] = ApplyPatch(result[property.Name], property.Value);
                }

                return result;
            }

            private void GetLootPaths(List<string> paths)
            {
                if (HarmonyDataLayer.ExistsDatafile(Path.Combine(Name, "Default_Loot")))
                {
                    paths.Add("Default_Loot");
                }

                string[] folders = { "Difficulty_Loot", "Weekday_Loot", "Base_Loot" };

                foreach (string folder in folders)
                {
                    foreach (string file in HarmonyDataLayer.GetFiles(Path.Combine(Name, folder), "*.json"))
                    {
                        string path = $"{folder}/{GetFileNameWithoutExtension(file)}";

                        if (!ContainsIgnoreCase(paths, path))
                        {
                            paths.Add(path);
                        }
                    }
                }
            }

            private static bool ContainsIgnoreCase(List<string> values, string value)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    if (values[i].Equals(value, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool IsValidLootPath(string path)
            {
                if (string.IsNullOrWhiteSpace(path) || path.Contains(".."))
                {
                    return false;
                }

                string normalized = path.Replace('\\', '/');

                if (normalized.Equals("Default_Loot", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                int separator = normalized.IndexOf('/');

                if (separator <= 0 || separator != normalized.LastIndexOf('/'))
                {
                    return false;
                }

                string folder = normalized.Substring(0, separator);
                string fileName = normalized.Substring(separator + 1);

                if (!folder.Equals("Difficulty_Loot", StringComparison.OrdinalIgnoreCase)
                    && !folder.Equals("Weekday_Loot", StringComparison.OrdinalIgnoreCase)
                    && !folder.Equals("Base_Loot", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return IsValidProfileName(fileName);
            }

            private List<LootItem> ReadLootTable(string path)
            {
                try
                {
                    string dataPath = Path.Combine(Name, path.Replace('/', Path.DirectorySeparatorChar));
                    return HarmonyDataLayer.ReadObject<List<LootItem>>(dataPath) ?? new();
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException($"Loot table '{path}' could not be read: {ex.Message}", ex);
                }
            }

            private bool IsLootItemUsable(LootItem item, bool allProbabilitiesZero)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.shortname) || item.amount <= 0 || !allProbabilitiesZero && item.probability <= 0f)
                {
                    return false;
                }

                string shortname = item.shortname;

                if (shortname.Equals("chocholate", StringComparison.OrdinalIgnoreCase))
                {
                    shortname = "chocolate";
                }

                if (shortname.EndsWith(".bp", StringComparison.OrdinalIgnoreCase))
                {
                    shortname = shortname.Substring(0, shortname.Length - 3);
                }

                if (Instance.BlacklistedItems.Exists(value => value.Equals(shortname, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }

                ItemDefinition def = ItemManager.FindItemDefinition(shortname);

                return def != null && (!Instance.config.BlockPaidContent || !Instance.RequiresOwnership(def, 0));
            }

            private Package CreatePackage(out int lootTableCount, out int lootItemCount, out int skippedLootItemCount)
            {
                Package package = new()
                {
                    PluginVersion = Version.ToString(),
                    English = en,
                    Config = ToPayloadObject(Instance.config)
                };

                using var profileNames = DisposableList<string>();
                profileNames.AddRange(Instance.Buildings.Profiles.Keys);
                profileNames.Sort(StringComparer.OrdinalIgnoreCase);

                if (profileNames.Count > 0)
                {
                    package.ProfileBaseline = ToPayloadObject(Instance.Buildings.Profiles[profileNames[0]].Options);

                    foreach (string profileName in profileNames)
                    {
                        JObject profile = ToPayloadObject(Instance.Buildings.Profiles[profileName].Options);
                        JToken patch = CreatePatch(package.ProfileBaseline, profile);
                        package.Profiles[profileName] = patch as JObject ?? new JObject();
                    }
                }

                lootTableCount = 0;
                lootItemCount = 0;
                skippedLootItemCount = 0;

                using var lootPaths = DisposableList<string>();
                GetLootPaths(lootPaths);
                lootPaths.Sort(StringComparer.OrdinalIgnoreCase);

                foreach (string path in lootPaths)
                {
                    List<LootItem> source = ReadLootTable(path);
                    List<LootItem> exported = source;

                    List<LootItem> configured = source.FindAll(item => item != null && !string.IsNullOrWhiteSpace(item.shortname));
                    bool allProbabilitiesZero = configured.Count > 0 && configured.All(item => item.probability == 0f);
                    exported = source.FindAll(item => IsLootItemUsable(item, allProbabilitiesZero));
                    skippedLootItemCount += source.Count - exported.Count;

                    package.LootTables[path] = exported;
                    lootTableCount++;
                    lootItemCount += exported.Count;
                }

                return package;
            }

            private static string ToBase64(byte[] bytes)
            {
                return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            }

            private static byte[] FromBase64(string value)
            {
                string base64 = value.Replace('-', '+').Replace('_', '/');

                switch (base64.Length % 4)
                {
                    case 0: break;
                    case 2: base64 += "=="; break;
                    case 3: base64 += "="; break;
                    default: throw new FormatException("The preset payload has invalid Base64 padding.");
                }

                return Convert.FromBase64String(base64);
            }

            private static string GetPrefix(PayloadScope scope)
            {
                switch (scope)
                {
                    case PayloadScope.Full: return FULL_PREFIX;
                    case PayloadScope.Config: return CONFIG_PREFIX;
                    case PayloadScope.Profiles: return PROFILES_PREFIX;
                    case PayloadScope.LootTables: return LOOT_TABLES_PREFIX;
                    default: throw new ArgumentOutOfRangeException(nameof(scope));
                }
            }

            private static bool IncludesConfig(PayloadScope scope)
            {
                return scope == PayloadScope.Full || scope == PayloadScope.Config;
            }

            private static bool IncludesProfiles(PayloadScope scope)
            {
                return scope == PayloadScope.Full || scope == PayloadScope.Profiles;
            }

            private static bool IncludesLootTables(PayloadScope scope)
            {
                return scope == PayloadScope.Full || scope == PayloadScope.LootTables;
            }

            private static bool TryGetPayloadScope(string encoded, out PayloadScope scope, out int prefixLength)
            {
                scope = PayloadScope.Full;
                prefixLength = 0;

                if (string.IsNullOrEmpty(encoded))
                {
                    return false;
                }

                if (encoded.StartsWith(FULL_PREFIX, StringComparison.Ordinal))
                {
                    prefixLength = FULL_PREFIX.Length;
                    return true;
                }

                if (encoded.StartsWith(CONFIG_PREFIX, StringComparison.Ordinal))
                {
                    scope = PayloadScope.Config;
                    prefixLength = CONFIG_PREFIX.Length;
                    return true;
                }

                if (encoded.StartsWith(PROFILES_PREFIX, StringComparison.Ordinal))
                {
                    scope = PayloadScope.Profiles;
                    prefixLength = PROFILES_PREFIX.Length;
                    return true;
                }

                if (encoded.StartsWith(LOOT_TABLES_PREFIX, StringComparison.Ordinal))
                {
                    scope = PayloadScope.LootTables;
                    prefixLength = LOOT_TABLES_PREFIX.Length;
                    return true;
                }

                return false;
            }

            private static Package GetScopedPackage(Package package, PayloadScope scope)
            {
                return new Package
                {
                    FormatVersion = package.FormatVersion,
                    PluginVersion = package.PluginVersion,
                    English = package.English,
                    Config = IncludesConfig(scope) ? package.Config : null,
                    ProfileBaseline = IncludesProfiles(scope) ? package.ProfileBaseline : null,
                    Profiles = IncludesProfiles(scope) ? package.Profiles : null,
                    LootTables = IncludesLootTables(scope) ? package.LootTables : null
                };
            }

            private static string Encode(Package package, PayloadScope scope)
            {
                string json;

                using (StringWriter writer = new(CultureInfo.InvariantCulture))
                using (JsonTextWriter jsonWriter = new(writer))
                {
                    jsonWriter.Formatting = Formatting.None;
                    CreatePayloadSerializer().Serialize(jsonWriter, GetScopedPackage(package, scope));
                    json = writer.ToString();
                }

                byte[] compressed = Facepunch.Utility.Compression.Compress(Encoding.UTF8.GetBytes(json));
                return GetPrefix(scope) + ToBase64(compressed);
            }

            private PayloadFile CreatePayloads(out int lootTableCount, out int lootItemCount, out int skippedLootItemCount)
            {
                Package package = CreatePackage(out lootTableCount, out lootItemCount, out skippedLootItemCount);

                return new PayloadFile
                {
                    Full = Encode(package, PayloadScope.Full),
                    Config = Encode(package, PayloadScope.Config),
                    Profiles = Encode(package, PayloadScope.Profiles),
                    LootTables = Encode(package, PayloadScope.LootTables)
                };
            }

            private string CreateFullPayload()
            {
                Package package = CreatePackage(out _, out _, out _);
                return Encode(package, PayloadScope.Full);
            }

            private static List<string> CreateCommands(string encoded, out int payloadLength)
            {
                payloadLength = encoded.Length;
                List<string> commands = new();

                if (encoded.Length <= CHUNK_LENGTH)
                {
                    commands.Add($"rb.config {encoded}");
                    return commands;
                }

                string id = GetChecksum(encoded).ToString("X8", CultureInfo.InvariantCulture);
                int chunkCount = (encoded.Length + CHUNK_LENGTH - 1) / CHUNK_LENGTH;

                commands.Add($"rb.config begin {id} {chunkCount} {encoded.Length}");

                for (int index = 0; index < chunkCount; index++)
                {
                    int offset = index * CHUNK_LENGTH;
                    int length = Math.Min(CHUNK_LENGTH, encoded.Length - offset);
                    commands.Add($"rb.config chunk {id} {index} {encoded.Substring(offset, length)}");
                }

                commands.Add($"rb.config apply {id}");
                return commands;
            }

            private static uint GetChecksum(string value)
            {
                unchecked
                {
                    uint hash = 2166136261u;

                    for (int i = 0; i < value.Length; i++)
                    {
                        hash ^= value[i];
                        hash *= 16777619u;
                    }

                    return hash;
                }
            }

            private static uint GetCrc32(byte[] bytes)
            {
                uint crc = uint.MaxValue;

                for (int i = 0; i < bytes.Length; i++)
                {
                    crc ^= bytes[i];

                    for (int bit = 0; bit < 8; bit++)
                    {
                        crc = (crc & 1u) != 0u ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
                    }
                }

                return ~crc;
            }

            private static uint ReadUInt32(byte[] bytes, int index)
            {
                return (uint)bytes[index] | (uint)bytes[index + 1] << 8 | (uint)bytes[index + 2] << 16 | (uint)bytes[index + 3] << 24;
            }

            private static Package Decode(string encoded, out PayloadScope scope)
            {
                if (encoded.Length > MAX_ENCODED_LENGTH)
                {
                    throw new InvalidDataException($"The preset payload exceeds the {MAX_ENCODED_LENGTH:N0} character limit.");
                }

                if (!TryGetPayloadScope(encoded, out scope, out int prefixLength))
                {
                    throw new FormatException("The preset payload has an invalid header.");
                }

                string payload = encoded.Substring(prefixLength);
                byte[] compressed = FromBase64(payload);

                if (compressed.Length < 18)
                {
                    throw new InvalidDataException("The preset payload was truncated or corrupted by the console.");
                }

                uint expectedCrc = ReadUInt32(compressed, compressed.Length - 8);
                uint expectedSize = ReadUInt32(compressed, compressed.Length - 4);

                if (expectedSize > (uint)MAX_JSON_BYTES)
                {
                    throw new InvalidDataException($"The uncompressed preset exceeds the {MAX_JSON_BYTES:N0} byte limit.");
                }

                byte[] jsonBytes;

                try
                {
                    jsonBytes = Facepunch.Utility.Compression.Uncompress(compressed);
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException("The preset payload was truncated or corrupted by the console.", ex);
                }

                if (jsonBytes == null || (uint)jsonBytes.Length != expectedSize || GetCrc32(jsonBytes) != expectedCrc)
                {
                    throw new InvalidDataException("The preset payload was truncated or corrupted by the console.");
                }

                string json = Encoding.UTF8.GetString(jsonBytes);
                JObject packageObject = JObject.Parse(json);
                int formatVersion = packageObject["v"]?.Value<int>() ?? 0;

                if (formatVersion != 4)
                {
                    throw new InvalidDataException($"Unsupported preset format version: {formatVersion}.");
                }

                Package package = packageObject.ToObject<Package>(CreatePayloadSerializer());

                return package ?? throw new JsonException("The preset package is empty.");
            }

            private static bool IsValidProfileName(string profileName)
            {
                return !string.IsNullOrWhiteSpace(profileName) && !profileName.Contains("..") && profileName.IndexOf('/') == -1 && profileName.IndexOf('\\') == -1 && profileName.IndexOfAny(Path.GetInvalidFileNameChars()) == -1;
            }

            private static string WriteCommands(string fileName, List<string> commands)
            {
                string dataPath = Path.Combine(Name, "Presets", GetFileNameWithoutExtension(fileName));
                HarmonyDataLayer.WriteObject(dataPath, new CommandFile { Commands = commands });
                return Path.Combine(HarmonyDataLayer.DataDirectory, $"{dataPath}.json");
            }

            private static string WritePayload(string fileName, PayloadFile payloads)
            {
                string dataPath = Path.Combine(Name, "Presets", GetFileNameWithoutExtension(fileName));
                HarmonyDataLayer.WriteObject(dataPath, payloads);
                return Path.Combine(HarmonyDataLayer.DataDirectory, $"{dataPath}.json");
            }

            private static int GetUiPayloadLimit()
            {
                int commandLimit = Math.Max(1, ConVar.Server.maxpacketsize_command - COMMAND_PACKET_OVERHEAD);
                return Math.Min(CUI_INPUT_LIMIT, Math.Min(MAX_ENCODED_LENGTH, commandLimit));
            }

            private static bool TryPrepare(Package package, PayloadScope scope, out Configuration importedConfig, out Dictionary<string, BuildingOptions> importedProfiles, out Dictionary<string, List<LootItem>> importedLootTables, out string error)
            {
                importedConfig = null;
                importedProfiles = new(StringComparer.OrdinalIgnoreCase);
                importedLootTables = new(StringComparer.OrdinalIgnoreCase);
                error = null;

                bool languageMismatch = package.English != en;

                if (languageMismatch && scope != PayloadScope.Full)
                {
                    error = "Config, Profiles, and Loot Tables presets can only be imported by the same language version of Raidable Bases. Use the Full preset to import between English and Russian.";
                    return false;
                }

                if (IncludesConfig(scope))
                {
                    if (package.Config == null)
                    {
                        error = "The preset does not contain the plugin configuration.";
                        return false;
                    }

                    try
                    {
                        importedConfig = FromPayloadObject<Configuration>(package.Config);
                    }
                    catch (Exception ex)
                    {
                        error = $"The plugin configuration could not be read: {ex.Message}";
                        return false;
                    }

                    if (importedConfig == null)
                    {
                        error = "The preset contains an empty plugin configuration.";
                        return false;
                    }
                }

                if (IncludesProfiles(scope))
                {
                    package.Profiles ??= new(StringComparer.OrdinalIgnoreCase);

                    if (package.Profiles.Count > 0 && package.ProfileBaseline == null)
                    {
                        error = "The preset contains profiles without a profile baseline.";
                        return false;
                    }

                    HashSet<string> profileNames = new(StringComparer.OrdinalIgnoreCase);

                    foreach (var (profileName, patch) in package.Profiles)
                    {
                        if (!IsValidProfileName(profileName))
                        {
                            error = $"The preset contains an invalid profile name: {profileName}.";
                            return false;
                        }

                        if (!profileNames.Add(profileName))
                        {
                            error = $"The preset contains duplicate profile names: {profileName}.";
                            return false;
                        }

                        try
                        {
                            JToken profileToken = ApplyPatch(package.ProfileBaseline, patch ?? new JObject());
                            BuildingOptions options = FromPayloadObject<BuildingOptions>(profileToken);

                            if (options == null)
                            {
                                error = $"The preset profile '{profileName}' is empty.";
                                return false;
                            }

                            importedProfiles[profileName] = options;
                        }
                        catch (Exception ex)
                        {
                            error = $"The preset profile '{profileName}' could not be read: {ex.Message}";
                            return false;
                        }
                    }
                }

                if (IncludesLootTables(scope))
                {
                    package.LootTables ??= new(StringComparer.OrdinalIgnoreCase);

                    foreach (var (path, lootItems) in package.LootTables)
                    {
                        string normalized = path?.Replace('\\', '/');

                        if (!IsValidLootPath(normalized))
                        {
                            error = $"The preset contains an invalid loot table path: {path}.";
                            return false;
                        }

                        importedLootTables[normalized] = lootItems ?? new();
                    }
                }

                return true;
            }

            private bool WriteProfileIfChanged(string dataPath, BuildingOptions options)
            {
                if (Instance.DataFileExists(dataPath))
                {
                    try
                    {
                        JObject existing = HarmonyDataLayer.ReadObject<JObject>(dataPath);
                        JToken imported = JToken.FromObject(options);

                        if (JToken.DeepEquals(existing, imported))
                        {
                            return false;
                        }
                    }
                    catch
                    {
                        // An invalid or unreadable profile should be replaced.
                    }
                }

                HarmonyDataLayer.WriteObject(dataPath, options);
                return true;
            }

            private bool WriteLootTableIfChanged(string dataPath, List<LootItem> lootItems)
            {
                if (Instance.DataFileExists(dataPath))
                {
                    try
                    {
                        var existing = HarmonyDataLayer.ReadObject<List<LootItem>>(dataPath);

                        if (JToken.DeepEquals(
                            JToken.FromObject(existing ?? new List<LootItem>()),
                            JToken.FromObject(lootItems ?? new List<LootItem>())))
                        {
                            return false;
                        }
                    }
                    catch
                    {
                        // An invalid or unreadable existing loot table should be replaced.
                    }
                }

                HarmonyDataLayer.WriteObject(dataPath, lootItems);
                return true;
            }

            private bool Import(IPlayer user, string encoded, bool directConsolePaste = false)
            {
                if (Instance.IsGridLoading() || !Instance.IsPasteAvailable())
                {
                    Instance.Reply(user, Instance.IsGridLoading() ? "GridIsLoading" : "PasteOnCooldown");
                    return false;
                }

                try
                {
                    Package package = Decode(encoded, out PayloadScope scope);

                    if (!TryPrepare(package, scope, out var importedConfig, out var importedProfiles, out var importedLootTables, out string error))
                    {
                        Instance.ReplyOrLog(user, error);
                        return false;
                    }

                    List<string> backupCommands = CreateCommands(CreateFullPayload(), out _);
                    string backupPath = WriteCommands($"rb.config.backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}", backupCommands);

                    if (IncludesConfig(scope))
                    {
                        Instance.config = importedConfig;
                        Instance.SaveConfig();
                    }

                    int changedProfileCount = 0;

                    foreach (var (profileName, options) in importedProfiles)
                    {
                        string dataPath = Path.Combine(Name, "Profiles", profileName);

                        if (WriteProfileIfChanged(dataPath, options))
                        {
                            changedProfileCount++;
                        }
                    }

                    int importedLootItemCount = 0;
                    int changedLootTableCount = 0;

                    foreach (var (lootPath, lootItems) in importedLootTables)
                    {
                        string dataPath = Path.Combine(Name, lootPath.Replace('/', Path.DirectorySeparatorChar));

                        if (WriteLootTableIfChanged(dataPath, lootItems))
                        {
                            changedLootTableCount++;
                            importedLootItemCount += lootItems.Count;
                        }
                    }

                    switch (scope)
                    {
                        case PayloadScope.Full:
                            Instance.ReplyOrLog(user, $"Applied the full preset: config, {changedProfileCount} changed profile{(changedProfileCount == 1 ? string.Empty : "s")}, and {changedLootTableCount} changed loot table{(changedLootTableCount == 1 ? string.Empty : "s")} with {importedLootItemCount} item entr{(importedLootItemCount == 1 ? "y" : "ies")}.");
                            break;
                        case PayloadScope.Config:
                            Instance.ReplyOrLog(user, "Applied the config preset.");
                            break;
                        case PayloadScope.Profiles:
                            Instance.ReplyOrLog(user, $"Applied the profiles preset: {changedProfileCount} of {importedProfiles.Count} profile{(importedProfiles.Count == 1 ? string.Empty : "s")} changed.");
                            break;
                        case PayloadScope.LootTables:
                            Instance.ReplyOrLog(user, $"Applied the loot tables preset: {changedLootTableCount} of {importedLootTables.Count} loot table{(importedLootTables.Count == 1 ? string.Empty : "s")} changed with {importedLootItemCount} item entr{(importedLootItemCount == 1 ? "y" : "ies")}.");
                            break;
                    }

                    Instance.ReplyOrLog(user, $"Backup: {backupPath}");

                    if (!string.Equals(package.PluginVersion, Version.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        Instance.ReplyOrLog(user, $"The preset was created with Raidable Bases {package.PluginVersion ?? "unknown"} and was imported by {Version}.");
                    }

                    Instance.ReloadConfiguration(user);
                    return true;
                }
                catch (Exception ex)
                {
                    string guidance = directConsolePaste
                        ? $" The console may have truncated the payload. Config, Profiles, and Loot Tables values up to {GetUiPayloadLimit():N0} characters can be pasted into 'rb.config import'; use Commands from rb.json for Full imports or longer values."
                        : string.Empty;
                    Instance.ReplyOrLog(user, $"Failed to apply the configuration preset: {ex.Message}{guidance}");
                    return false;
                }
            }

            public void ForgetImportUi(ulong userid)
            {
                _importUiUsers.Remove(userid);
            }

            private void DestroyImportUi(IPlayer user)
            {
                if (user == null)
                {
                    return;
                }

                if (user.Object is BasePlayer player)
                {
                    CuiHelper.DestroyUi(player, IMPORT_UI);
                    ForgetImportUi(player.userID);
                    return;
                }

                if (ulong.TryParse(user.Id, out ulong userid))
                {
                    ForgetImportUi(userid);
                }
            }

            private void ShowImportUi(IPlayer user)
            {
                if (user?.Object is not BasePlayer player)
                {
                    Instance.ReplyOrLog(user, "The full-string import window can only be opened in-game.");
                    return;
                }

                DestroyImportUi(user);

                UiHandler.UiPalette palette = Instance.UI.GetPalette();
                CuiElementContainer container = new();
                int payloadLimit = GetUiPayloadLimit();
                const float width = 720f;
                const float height = 194f;
                const string body = IMPORT_UI + "_Body";
                const string inputPanel = IMPORT_UI + "_InputPanel";

                UiHandler.AddCuiPanel(container, palette.Background, "0.5 0.5", "0.5 0.5",
                    FormattableString.Invariant($"{-width * 0.5f:0.#} {-height * 0.5f:0.#}"),
                    FormattableString.Invariant($"{width * 0.5f:0.#} {height * 0.5f:0.#}"),
                    "Overlay", IMPORT_UI, cursor: true, keyboard: true);
                UiHandler.AddCuiPanel(container, palette.Accent, "0 1", "1 1", "0 -2", "0 0", IMPORT_UI, IMPORT_UI + "_Accent");
                UiHandler.AddCuiPanel(container, palette.Accent, "0 1", "0 1", "18 -31", "24 -25", IMPORT_UI, IMPORT_UI + "_Dot");
                UiHandler.AddCuiElement(container, "Configuration Import", 21, TextAnchor.MiddleLeft, palette.Accent,
                    "0 1", "1 1", "32 -47", "-62 -7", IMPORT_UI, IMPORT_UI + "_Title");
                UiHandler.AddCuiButton(container, palette.Panel, "rb.config import.close", "×", palette.Accent, 20, TextAnchor.MiddleCenter,
                    "1 1", "1 1", "-52 -48", "-18 -14", IMPORT_UI, IMPORT_UI + "_Close");
                UiHandler.AddCuiElement(container, "Paste one RBC2 value or exported rb.config command, then press Enter.", 13, TextAnchor.MiddleLeft, palette.Muted,
                    "0 1", "1 1", "18 -75", "-18 -47", IMPORT_UI, IMPORT_UI + "_Instructions", false);

                UiHandler.AddCuiPanel(container, palette.Cell, "0 0", "1 1", "18 18", "-18 -79", IMPORT_UI, body);
                UiHandler.AddCuiElement(container, $"Maximum input: {payloadLimit:N0} characters • Commands must be entered in order", 12, TextAnchor.MiddleLeft, palette.Muted,
                    "0 1", "1 1", "12 -31", "-12 -5", body, IMPORT_UI + "_Limit", false);
                UiHandler.AddCuiPanel(container, palette.Panel, "0 0", "1 1", "12 12", "-12 -35", body, inputPanel);
                UiHandler.AddCuiInput(container, "rb.config import", payloadLimit, palette.Text, 12, TextAnchor.MiddleLeft,
                    "0 0", "1 1", "10 4", "-10 -4", inputPanel, IMPORT_UI + "_Input");

                if (CuiHelper.AddUi(player, container))
                {
                    _importUiUsers.Add(player.userID);
                }
                else
                {
                    Instance.ReplyOrLog(user, "The configuration import interface could not be opened. Paste imports through the server or F1 console instead.");
                }
            }

            private void ClearImport()
            {
                _importTimer?.Destroy();
                _importTimer = null;
                _activeImport = null;
            }

            private void RefreshImportTimeout()
            {
                _importTimer?.Destroy();
                _importTimer = Instance.timer.Once(IMPORT_TIMEOUT, () =>
                {
                    _activeImport = null;
                    _importTimer = null;
                });
            }

            private bool TryGetImport(IPlayer user, string id, out PresetImport import)
            {
                import = _activeImport;

                if (import == null || !import.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                {
                    Instance.ReplyOrLog(user, "No matching configuration preset import is active. Paste its begin command first.");
                    return false;
                }

                return true;
            }

            private void BeginImport(IPlayer user, IReadOnlyList<string> args)
            {
                if (args.Count != 4 || !uint.TryParse(args[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint checksum) || !int.TryParse(args[2], out int chunkCount) || !int.TryParse(args[3], out int totalLength))
                {
                    Instance.ReplyOrLog(user, "Invalid configuration preset begin command.");
                    return;
                }

                int expectedChunks = totalLength > 0 ? (totalLength + CHUNK_LENGTH - 1) / CHUNK_LENGTH : 0;

                if (totalLength <= 0 || totalLength > MAX_ENCODED_LENGTH || chunkCount != expectedChunks)
                {
                    Instance.ReplyOrLog(user, "The configuration preset begin command contains invalid length information.");
                    return;
                }

                ClearImport();
                _activeImport = new()
                {
                    Id = checksum.ToString("X8", CultureInfo.InvariantCulture),
                    Checksum = checksum,
                    TotalLength = totalLength,
                    Chunks = new string[chunkCount]
                };
                RefreshImportTimeout();
                Instance.ReplyOrLog(user, $"Started configuration preset import {_activeImport.Id}. Waiting for {chunkCount} chunks.");
            }

            private static bool IsChunkTextValid(string value)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return false;
                }

                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];

                    if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '-' && c != '_' && c != '.')
                    {
                        return false;
                    }
                }

                return true;
            }

            private void AddChunk(IPlayer user, IReadOnlyList<string> args)
            {
                if (args.Count != 4)
                {
                    Instance.ReplyOrLog(user, "Invalid configuration preset chunk command.");
                    return;
                }

                if (!TryGetImport(user, args[1], out var import))
                {
                    return;
                }

                if (!int.TryParse(args[2], out int index) || index < 0 || index >= import.Chunks.Length)
                {
                    Instance.ReplyOrLog(user, "The configuration preset chunk index is invalid.");
                    return;
                }

                string value = args[3];
                int expectedLength = index == import.Chunks.Length - 1 ? import.TotalLength - index * CHUNK_LENGTH : CHUNK_LENGTH;

                if (value.Length > expectedLength && TryGetPayloadScope(value, out _, out _))
                {
                    Instance.ReplyOrLog(user, "This appears to be a complete preset payload, not one chunk of it. Run 'rb.config import' in-game and paste it into the import window.");
                    return;
                }

                if (value.Length != expectedLength || !IsChunkTextValid(value))
                {
                    Instance.ReplyOrLog(user, $"Configuration preset chunk {index} was truncated or corrupted by the console. Expected {expectedLength:N0} characters but received {value.Length:N0}.");
                    return;
                }

                bool replaced = import.Chunks[index] != null;
                if (!replaced)
                {
                    import.Received++;
                }

                import.Chunks[index] = value;
                RefreshImportTimeout();
                Instance.ReplyOrLog(user, $"Configuration preset chunk {index + 1}/{import.Chunks.Length} {(replaced ? "replaced" : "received")} ({import.Received}/{import.Chunks.Length}).");
            }

            private void ApplyImport(IPlayer user, IReadOnlyList<string> args)
            {
                if (args.Count != 2)
                {
                    Instance.ReplyOrLog(user, "Invalid configuration preset apply command.");
                    return;
                }

                if (!TryGetImport(user, args[1], out var import))
                {
                    return;
                }

                if (import.Received != import.Chunks.Length)
                {
                    using var missing = DisposableList<int>();
                    for (int i = 0; i < import.Chunks.Length; i++)
                    {
                        if (import.Chunks[i] == null)
                        {
                            missing.Add(i);
                        }
                    }

                    Instance.ReplyOrLog(user, $"The configuration preset is missing chunk{(missing.Count == 1 ? string.Empty : "s")}: {string.Join(", ", missing)}.");
                    return;
                }

                using var sb = DisposableBuilder.Get();
                sb.EnsureCapacity(import.TotalLength);

                for (int i = 0; i < import.Chunks.Length; i++)
                {
                    sb.Append(import.Chunks[i]);
                }

                string encoded = sb.ToString();

                if (encoded.Length != import.TotalLength || GetChecksum(encoded) != import.Checksum)
                {
                    Instance.ReplyOrLog(user, "The assembled configuration preset was truncated or corrupted. Paste the begin and chunk commands again.");
                    return;
                }

                if (Import(user, encoded))
                {
                    ClearImport();
                    DestroyImportUi(user);
                }
            }

            public bool TryHandleCommand(IPlayer user, string[] args)
            {
                if (args.Length > 0)
                {
                    switch (args[0].ToLowerInvariant())
                    {
                        case "begin": BeginImport(user, args); return true;
                        case "chunk": AddChunk(user, args); return true;
                        case "apply": ApplyImport(user, args); return true;
                        case "import.close": DestroyImportUi(user); return true;
                        case "import":
                            {
                                if (args.Length == 1)
                                {
                                    ShowImportUi(user);
                                    return true;
                                }

                                HandleImportUiSubmission(user, args);
                                return true;
                            }
                    }

                    string encoded = string.Concat(args).Trim().Trim('"');

                    if (TryGetPayloadScope(encoded, out _, out _))
                    {
                        Import(user, encoded, true);
                        return true;
                    }
                }

                if (args.Length == 0 || !args[0].Equals("export", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (Instance.IsGridLoading())
                {
                    Instance.Reply(user, "GridIsLoading");
                    return true;
                }

                try
                {
                    PayloadFile payloads = CreatePayloads(out int lootTableCount, out int lootItemCount, out int skippedLootItemCount);
                    List<string> commands = CreateCommands(payloads.Full, out int payloadLength);
                    int uiPayloadLimit = GetUiPayloadLimit();
                    payloads.Commands = commands;
                    payloads.ConfigCommands = payloads.Config.Length >= uiPayloadLimit ? CreateCommands(payloads.Config, out _) : null;
                    payloads.ProfilesCommands = payloads.Profiles.Length >= uiPayloadLimit ? CreateCommands(payloads.Profiles, out _) : null;
                    payloads.LootTablesCommands = payloads.LootTables.Length >= uiPayloadLimit ? CreateCommands(payloads.LootTables, out _) : null;
                    string presetPath = WritePayload("rb", payloads);
                    int chunkCount = commands.Count == 1 ? 1 : commands.Count - 2;
                    int profileCount = Instance.Buildings.Profiles.Count;

                    Instance.ReplyOrLog(user, $"Created a {commands.Count}-command configuration preset containing the config, {profileCount} profile{(profileCount == 1 ? string.Empty : "s")}, and {lootTableCount} loot table{(lootTableCount == 1 ? string.Empty : "s")} with {lootItemCount} item entr{(lootItemCount == 1 ? "y" : "ies")}.");
                    Instance.ReplyOrLog(user, $"Skipped {skippedLootItemCount} loot entr{(skippedLootItemCount == 1 ? "y" : "ies")} with no usable amount, no usable probability, an invalid or blocked shortname, or paid content blocked by the current config.");
                    Instance.ReplyOrLog(user, $"Payload length: {payloadLength:N0} characters in {chunkCount} chunk{(chunkCount == 1 ? string.Empty : "s")}. Preset file: {presetPath}");

                    Instance.ReplyOrLog(user, $"The preset contains Commands for Full imports and separately shareable Config, Profiles, and Loot Tables values. Values longer than {uiPayloadLimit:N0} characters have their own matching Commands value.");
                }
                catch (Exception ex)
                {
                    if (user.IsServer) Puts($"Failed to create the configuration preset: {ex}");
                    else user.Message($"Failed to create the configuration preset: {ex.Message}");
                }

                return true;
            }

            private void HandleImportUiSubmission(IPlayer user, string[] args)
            {
                string input = string.Join(" ", args, 1, args.Length - 1).Trim();

                if (input.EndsWith(",", StringComparison.Ordinal))
                {
                    input = input.Substring(0, input.Length - 1).TrimEnd();
                }

                input = input.Trim('"');

                if (string.IsNullOrWhiteSpace(input))
                {
                    Instance.ReplyOrLog(user, "Nothing was pasted.");
                    return;
                }

                const string commandPrefix = "rb.config ";

                if (input.StartsWith(commandPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    input = input.Substring(commandPrefix.Length).TrimStart();
                }

                if (StartsWithImportAction(input, "begin") || StartsWithImportAction(input, "chunk") || StartsWithImportAction(input, "apply"))
                {
                    string[] commandArgs = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    if (commandArgs[0].Equals("begin", StringComparison.OrdinalIgnoreCase))
                    {
                        BeginImport(user, commandArgs);
                        return;
                    }

                    if (commandArgs[0].Equals("chunk", StringComparison.OrdinalIgnoreCase))
                    {
                        AddChunk(user, commandArgs);
                        return;
                    }

                    ApplyImport(user, commandArgs);
                    return;
                }

                string payload = input;

                if (!TryGetPayloadScope(payload, out PayloadScope scope, out _))
                {
                    Instance.ReplyOrLog(user, "Paste a recognized RBC2 payload or one rb.config command from rb.json.");
                    return;
                }

                if (scope == PayloadScope.Full)
                {
                    Instance.ReplyOrLog(user, "Full presets exceed the CUI limit. Paste each entry from Commands here one at a time, in order.");
                    return;
                }

                if (payload.Length >= GetUiPayloadLimit())
                {
                    Instance.ReplyOrLog(user, $"The pasted value reached the {GetUiPayloadLimit():N0}-character CUI limit and may be incomplete. Paste its matching Commands entries here one at a time, in order.");
                    return;
                }

                if (Import(user, payload))
                {
                    DestroyImportUi(user);
                }
            }

            private static bool StartsWithImportAction(string input, string action)
            {
                int length = 0;

                while (length < input.Length && !char.IsWhiteSpace(input[length]))
                {
                    length++;
                }

                return length == action.Length && string.Compare(input, 0, action, 0, length, StringComparison.OrdinalIgnoreCase) == 0;
            }

            public void Dispose()
            {
                ClearImport();

                foreach (ulong userid in _importUiUsers)
                {
                    BasePlayer player = RustCore.FindPlayerById(userid);

                    if (player != null)
                    {
                        CuiHelper.DestroyUi(player, IMPORT_UI);
                    }
                }

                _importUiUsers.Clear();
            }
        }

        private void ReplyOrLog(IPlayer user, string message)
        {
            if (user.IsServer) Puts(message);
            else user.Reply(message);
        }

        private void CommandConfig(IPlayer user, string command, string[] args)
        {
            if (!user.HasPermission("raidablebases.config"))
            {
                Reply(user, "No Permission");
                return;
            }

            if (_configPresetController.TryHandleCommand(user, args))
            {
                return;
            }

            if (args.Length == 0 || !arguments.Exists(str => args[0].Equals(str, StringComparison.OrdinalIgnoreCase)))
            {
                Reply(user, "ConfigUseFormat", string.Join("|", arguments));
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
                case "accepted-items": CommandAcceptedItems(user, args); return;
                case "retrieve-items": CommandRetrieveItems(user, args); return;
                case "maintained":
                    {
                        Automated.IsMaintainedEnabled = !Automated.IsMaintainedEnabled;
                        Automated.StartCoroutine(RaidableType.Maintained);
                        Reply(user, $"Toggled maintained events {(Automated.IsMaintainedEnabled ? "on" : "off")}");
                        config.Settings.Maintained.Enabled = Automated.IsMaintainedEnabled;
                        SaveConfig();
                        return;
                    }
                case "scheduled":
                    {
                        Automated.IsScheduledEnabled = !Automated.IsScheduledEnabled;
                        Automated.StartCoroutine(RaidableType.Scheduled);
                        Reply(user, $"Toggled scheduled events {(Automated.IsScheduledEnabled ? "on" : "off")}");
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
                ReplyOrLog(user, "Enabled map markers and dome.");
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
                ReplyOrLog(user, "Removed all explosive costs from the profiles.");
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
