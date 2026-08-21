using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace TeleportGUI
{
    /// <summary>
    /// Config schema aligned with Oxide TeleportGUI 2.0.50 (HarmonyConfig/TeleportGUI.json).
    /// No Oxide types. Optional Harmony-only fields are ignored when absent from JSON.
    /// </summary>
    public class TeleportGUIConfig
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public enum PurchaseMode
        {
            Economics,
            ServerRewards,
            Scrap
        }

        // --- Optional Harmony-only fields (not in Oxide config; safe extras for current Mod) ---

        /// <summary>Optional Harmony-only; absent from Oxide JSON → empty (everyone allowed).</summary>
        [JsonProperty("Allowed Steam IDs (empty = everyone can use; otherwise only these + admins)", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<string> AllowedSteamIds { get; set; } = new List<string>();

        /// <summary>Optional Harmony-only; absent from Oxide JSON → true.</summary>
        [JsonProperty("Admins bypass allowlist and limits", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool AdminsBypass { get; set; } = true;

        /// <summary>Optional Harmony-only seed warps; Oxide keeps warps in warpdata.json.</summary>
        [JsonProperty("Warp points (name -> position). Include Outpost, Bandit, or custom. Set X,Y,Z in config or via admin.)", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Dictionary<string, WarpPointConfig> WarpPoints { get; set; }

        [JsonProperty("Data folder path (empty = serverRoot/HarmonyData/TeleportGUI)", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string DataFolderPath { get; set; } = "";

        [JsonProperty("TpBack command aliases (e.g. tpback, back)", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<string> TpBackCommandAliases { get; set; }

        [JsonProperty("Death command aliases (teleport to last death location)", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<string> DeathCommandAliases { get; set; }

        [JsonProperty("Record death location for /death command", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool RecordDeathLocation { get; set; } = true;

        // --- Oxide 2.0.50 schema ---

        [JsonProperty("Chat options")]
        public ChatOptions Chat { get; set; } = new ChatOptions();

        [JsonProperty("Teleport options")]
        public TeleportOptions Teleport { get; set; } = new TeleportOptions();

        [JsonProperty("Home options")]
        public HomeOptions Home { get; set; } = new HomeOptions();

        [JsonProperty("Warp options")]
        public WarpOptions Warp { get; set; } = new WarpOptions();

        [JsonProperty("Teleport conditions")]
        public TeleportConditions Conditions { get; set; } = new TeleportConditions();

        [JsonProperty("Purge user data after x amount of days of no activity")]
        public int PurgeDays { get; set; } = 7;

        [JsonProperty("Admin options")]
        public AdminOptions Admin { get; set; } = new AdminOptions();

        [JsonProperty("UI options")]
        public UIOptions UI { get; set; } = new UIOptions();

        [JsonProperty("Version")]
        public VersionInfo Version { get; set; } = new VersionInfo { Major = 2, Minor = 0, Patch = 50 };

        public class VersionInfo
        {
            [JsonProperty("Major")]
            public int Major { get; set; }

            [JsonProperty("Minor")]
            public int Minor { get; set; }

            [JsonProperty("Patch")]
            public int Patch { get; set; }
        }

        public class WarpPointConfig
        {
            [JsonProperty("X")]
            public float X { get; set; }

            [JsonProperty("Y")]
            public float Y { get; set; }

            [JsonProperty("Z")]
            public float Z { get; set; }
        }

        public class BaseOptions
        {
            [JsonProperty("Cancel pending teleport if hurt")]
            public bool CancelOnDamage { get; set; }

            [JsonProperty("Cancel pending teleport if either player dies")]
            public bool CancelOnDeath { get; set; }

            [JsonProperty("Teleport delay options")]
            public TeleportDelayOptions Delay { get; set; } = new TeleportDelayOptions();

            [JsonProperty("Teleport cooldown options")]
            public CooldownOptions Cooldown { get; set; } = new CooldownOptions();

            [JsonProperty("Teleport daily limit options")]
            public LimitOptions Limits { get; set; } = new LimitOptions();

            [JsonProperty("Purchase options")]
            public PurchaseOptions Purchase { get; set; } = new PurchaseOptions();

            [JsonProperty("Command aliases")]
            public List<string> CommandAliases { get; set; } = new List<string>();

            /// <summary>Alias for older Harmony Mod code that used DailyLimit.</summary>
            [JsonIgnore]
            public LimitOptions DailyLimit
            {
                get => Limits;
                set => Limits = value ?? new LimitOptions();
            }
        }

        public class TeleportOptions : BaseOptions
        {
            [JsonProperty("Teleport request timeout (seconds)")]
            public int RequestTimeout { get; set; } = 30;

            [JsonProperty("Only shows friends, clan members and team mates in player list")]
            public bool FriendliesOnly { get; set; }

            [JsonProperty("[Friends Plugin] Only shows friends that are mutual friends in player list (slow)")]
            public bool MutualFriendsOnly { get; set; }
        }

        public class HomeOptions : BaseOptions
        {
            [JsonProperty("Max home options")]
            public HomeLimits MaxHomes { get; set; } = new HomeLimits();

            [JsonProperty("Sleeping bag homes")]
            public SleepingBagOptions SleepingBags { get; set; } = new SleepingBagOptions();

            [JsonProperty("Allow creating home in building blocked area")]
            public bool AllowSetHomeInBuildBlocked { get; set; }

            [JsonProperty("Allow creating home on a tugboat")]
            public bool AllowSetHomeOnTugboat { get; set; }

            [JsonProperty("Require building privilege to set home")]
            public bool RequirePrivilegeSetHome { get; set; }

            [JsonProperty("Homes can only be set on building blocks")]
            public bool MustSetHomeOnBuilding { get; set; }

            [JsonProperty("Allow homes to be set on floors")]
            public bool CanSetHomeOnFloor { get; set; }

            [JsonProperty("Don't allow homes to be set within X distance of another home")]
            public float MinimumHomeRadiusDistance { get; set; } = 20f;

            [JsonProperty("Disable home point if it is clipping inside a wall or entity")]
            public bool DisableHomeInEntity { get; set; } = true;

            [JsonProperty("Wipe home data when the server is wiped")]
            public bool WipeHomesOnNewServerSave { get; set; } = true;

            public class SleepingBagOptions
            {
                [JsonProperty("Create home on bag placement")]
                public bool CreateHomeOnBagPlacement { get; set; }

                [JsonProperty("Create home on bed placement")]
                public bool CreateHomeOnBedPlacement { get; set; }

                [JsonProperty("Create home on beach towel placement")]
                public bool CreateHomeOnBeachTowelPlacement { get; set; }

                [JsonProperty("Only create a home on placement if it is inside a building")]
                public bool OnlyCreateInBuilding { get; set; } = true;

                [JsonProperty("Disable set home command")]
                public bool DisableSetHomeCommand { get; set; }
            }

            public class HomeLimits : VipOption
            {
                [JsonProperty("Default home limit (0 disables limits entirely)")]
                public override int Default { get; set; }

                [JsonProperty("VIP home limit (permission | limit)")]
                public override Dictionary<string, int> VIP { get; set; } = new Dictionary<string, int>();
            }
        }

        public class WarpOptions : BaseOptions
        {
            [JsonProperty("Teleport to random point in X vicinity (0 to disable)")]
            public float VicinityTeleportRadius { get; set; }

            [JsonProperty("Radius to check for NPC's when teleporting to a monument warp point")]
            public float MonumentWarpNPCRadius { get; set; } = 25f;

            [JsonProperty("Monument warp points")]
            public Dictionary<string, MonumentWarp> MonumentWarps { get; set; } = new Dictionary<string, MonumentWarp>();

            public class MonumentWarp
            {
                [JsonProperty("Generate warp for this monument")]
                public bool Enabled { get; set; }

                [JsonProperty("Only generate warp points in the monuments safe zone (if applicable)")]
                public bool SafeZoneOnly { get; set; }

                [JsonProperty("Custom chat command")]
                public string Command { get; set; } = string.Empty;

                [JsonProperty("Required permission (prefix with teleportgui.)")]
                public string Permission { get; set; } = string.Empty;

                [JsonProperty("Maximum radius for generated warp points")]
                public float MaxRadius { get; set; }
            }
        }

        public class AdminOptions
        {
            [JsonProperty("Don't notify user's when a admin teleports to them")]
            public bool Silent { get; set; }

            [JsonProperty("Allow instant teleportation for admins")]
            public bool Instant { get; set; }
        }

        public class UIOptions
        {
            [JsonProperty("Disable UI")]
            public bool DisableUI { get; set; }

            [JsonProperty("Hide admins from player search list")]
            public bool HideAdminsInUI { get; set; }

            [JsonProperty("Hide warp points if the player doesn't have permission")]
            public bool HideWarpsNoPermission { get; set; }

            [JsonProperty("UI Colors")]
            public UIColors Colors { get; set; } = new UIColors();

            [JsonProperty("Request Popup")]
            public RequestPopupOptions RequestPopup { get; set; } = new RequestPopupOptions();

            /// <summary>Oxide naming alias used by some call sites.</summary>
            [JsonIgnore]
            public RequestPopupOptions Request
            {
                get => RequestPopup;
                set => RequestPopup = value ?? new RequestPopupOptions();
            }

            public class RequestPopupOptions
            {
                [JsonProperty("Anchor (TopLeft, TopCenter, TopRight, CenterLeft, Center, CenterRight, BottomLeft, BottomCenter, BottomRight, FullStretch, TopStretch, HorizontalCenterStretch, BottomStretch, LeftStretch, VerticalCenterStretch, RightStretch)")]
                [JsonConverter(typeof(StringEnumConverter))]
                public AnchorEnum Anchor { get; set; } = AnchorEnum.CenterRight;

                [JsonProperty("Offset")]
                public UIOffset Offset { get; set; } = new UIOffset(-137.5f, -22.5f, 12.5f, 22.5f);

                [JsonProperty("Horizontal Padding")]
                public HorizontalPadding Padding { get; set; } = new HorizontalPadding { Left = 0f, Right = 10f };

                public class UIOffset
                {
                    [JsonProperty("XMin")]
                    public float XMin { get; set; }

                    [JsonProperty("XMax")]
                    public float XMax { get; set; }

                    [JsonProperty("YMin")]
                    public float YMin { get; set; }

                    [JsonProperty("YMax")]
                    public float YMax { get; set; }

                    public UIOffset() { }

                    public UIOffset(float xMin, float yMin, float xMax, float yMax)
                    {
                        XMin = xMin;
                        YMin = yMin;
                        XMax = xMax;
                        YMax = yMax;
                    }
                }

                public class HorizontalPadding
                {
                    [JsonProperty("Left")]
                    public float Left { get; set; }

                    [JsonProperty("Right")]
                    public float Right { get; set; }
                }

                public enum AnchorEnum
                {
                    TopLeft,
                    TopCenter,
                    TopRight,
                    CenterLeft,
                    Center,
                    CenterRight,
                    BottomLeft,
                    BottomCenter,
                    BottomRight,
                    FullStretch,
                    TopStretch,
                    HorizontalCenterStretch,
                    BottomStretch,
                    LeftStretch,
                    VerticalCenterStretch,
                    RightStretch
                }
            }

            public class UIColors
            {
                [JsonProperty("Background")]
                public UIColorEntry Background { get; set; } = new UIColorEntry { Hex = "151515", Alpha = 0.94f };

                [JsonProperty("Panel")]
                public UIColorEntry Panel { get; set; } = new UIColorEntry { Hex = "FFFFFF", Alpha = 0.165f };

                [JsonProperty("Header")]
                public UIColorEntry Header { get; set; } = new UIColorEntry { Hex = "C4FF00", Alpha = 0.314f };

                [JsonProperty("Button")]
                public UIColorEntry Button { get; set; } = new UIColorEntry { Hex = "2A2E32", Alpha = 1f };

                [JsonProperty("Close")]
                public UIColorEntry Close { get; set; } = new UIColorEntry { Hex = "CE422B", Alpha = 1f };

                [JsonProperty("Highlight")]
                public UIColorEntry Highlight { get; set; } = new UIColorEntry { Hex = "C4FF00", Alpha = 1f };
            }

            /// <summary>Same as Oxide UIColors.Color; named UIColorEntry for TeleportGUIUI.</summary>
            public class UIColorEntry
            {
                [JsonProperty("Hex")]
                public string Hex { get; set; } = "808080";

                [JsonProperty("Alpha")]
                public float Alpha { get; set; } = 1f;
            }
        }

        public class ChatOptions
        {
            [JsonProperty("Use chat prefix")]
            public bool UsePrefix { get; set; }

            [JsonProperty("Chat prefix")]
            public string Prefix { get; set; } = "<color=#C4FF00>TP: </color>";

            [JsonProperty("Chat icon (steam ID)")]
            public ulong Icon { get; set; }
        }

        public class TeleportDelayOptions : VipOption
        {
            [JsonProperty("Default time until teleport (seconds)")]
            public override int Default { get; set; }

            [JsonProperty("VIP time until teleport (permission | seconds)")]
            public override Dictionary<string, int> VIP { get; set; } = new Dictionary<string, int>();
        }

        public class CooldownOptions : VipOption
        {
            [JsonProperty("Default cooldown time (seconds)")]
            public override int Default { get; set; }

            [JsonProperty("VIP cooldown times (permission | seconds)")]
            public override Dictionary<string, int> VIP { get; set; } = new Dictionary<string, int>();
        }

        public class LimitOptions : VipOption
        {
            [JsonProperty("Default daily limit (0 disables limits entirely)")]
            public override int Default { get; set; }

            [JsonProperty("VIP daily limit (permission | limit)")]
            public override Dictionary<string, int> VIP { get; set; } = new Dictionary<string, int>();
        }

        public class PurchaseOptions : VipOption
        {
            [JsonProperty("Require payment to teleport after daily limit has been reached")]
            public bool PayAfterUsingDailyLimits { get; set; }

            [JsonProperty("Always require payment to teleport, no freebies")]
            public bool PayAlways { get; set; }

            [JsonProperty("Payment mode (ServerRewards, Economics, Scrap)")]
            [JsonConverter(typeof(StringEnumConverter))]
            public PurchaseMode Mode { get; set; } = PurchaseMode.Scrap;

            [JsonProperty("Default payment cost")]
            public override int Default { get; set; }

            [JsonProperty("VIP payment cost (permission | cost)")]
            public override Dictionary<string, int> VIP { get; set; } = new Dictionary<string, int>();
        }

        public abstract class VipOption
        {
            public abstract int Default { get; set; }

            public abstract Dictionary<string, int> VIP { get; set; }

            /// <summary>Lowest VIP value among matching permissions; falls back to Default.</summary>
            public int GetLowestOption(Func<string, bool> hasPermission)
            {
                int t = int.MaxValue;
                if (VIP != null && hasPermission != null)
                {
                    foreach (KeyValuePair<string, int> kvp in VIP)
                    {
                        if (hasPermission(kvp.Key) && kvp.Value < t)
                            t = kvp.Value;
                    }
                }

                return t == int.MaxValue ? Default : t;
            }

            /// <summary>Highest VIP value among matching permissions (0 = unlimited); falls back to Default.</summary>
            public int GetHighestOption(Func<string, bool> hasPermission)
            {
                int t = 0;
                if (VIP != null && hasPermission != null)
                {
                    foreach (KeyValuePair<string, int> kvp in VIP)
                    {
                        if (!hasPermission(kvp.Key))
                            continue;

                        if (kvp.Value == 0)
                            return 0;

                        if (kvp.Value > t)
                            t = kvp.Value;
                    }
                }

                return t == 0 ? Default : t;
            }
        }

        #region Teleport Conditions

        public class TeleportConditions
        {
            [JsonProperty("Can teleport whilst bleeding")]
            public WhilstBleedingCondition WhilstBleeding { get; set; } = new WhilstBleedingCondition();

            [JsonProperty("Can teleport whilst crafting")]
            public WhenCraftingCondition WhenCrafting { get; set; } = new WhenCraftingCondition();

            [JsonProperty("Can teleport if mounted")]
            public MountedCondition Mounted { get; set; } = new MountedCondition();

            [JsonProperty("Can teleport if building blocked")]
            public BuildingBlockedCondition BuildingBlocked { get; set; } = new BuildingBlockedCondition();

            [JsonProperty("Can teleport if raid blocked")]
            public RaidBlockedCondition RaidBlocked { get; set; } = new RaidBlockedCondition();

            [JsonProperty("Can teleport if on cargo ship")]
            public CargoShipCondition CargoShip { get; set; } = new CargoShipCondition();

            [JsonProperty("Can teleport if on tug boat")]
            public TugBoatCondition TugBoat { get; set; } = new TugBoatCondition();

            [JsonProperty("Can teleport if on hot air balloon")]
            public HotAirBalloonCondition HotAirBalloon { get; set; } = new HotAirBalloonCondition();

            [JsonProperty("Can teleport if near oil rig")]
            public OilRigCondition OilRig { get; set; } = new OilRigCondition();

            [JsonProperty("Can teleport if in underwater labs")]
            public UnderwaterLabsCondition UnderwaterLabs { get; set; } = new UnderwaterLabsCondition();

            [JsonProperty("Can teleport if in train tunnels")]
            public TrainTunnelsCondition TrainTunnels { get; set; } = new TrainTunnelsCondition();

            [JsonProperty("Can teleport if in water")]
            public InWaterCondition InWater { get; set; } = new InWaterCondition();

            [JsonProperty("Can teleport if on water")]
            public OnWaterCondition OnWater { get; set; } = new OnWaterCondition();

            [JsonProperty("Can teleport if in notp zone")]
            public NoTPZoneCondition NoTpZone { get; set; } = new NoTPZoneCondition();

            [JsonProperty("Can teleport if in safe zone")]
            public SafeZoneCondition SafeZone { get; set; } = new SafeZoneCondition();

            [JsonProperty("Can teleport if hostile")]
            public HostileCondition Hostile { get; set; } = new HostileCondition();

            [JsonProperty("Can teleport if in monument")]
            public InMonumentCondition InMonument { get; set; } = new InMonumentCondition();

            [JsonProperty("Can teleport if in any topologies (advanced)")]
            public CustomTopologyCondition Topology { get; set; } = new CustomTopologyCondition();
        }

        public class WhilstBleedingCondition : TargetTeleportCondition { }

        public class WhenCraftingCondition : TargetTeleportCondition { }

        public class MountedCondition : TargetTeleportCondition { }

        public class RaidBlockedCondition : TargetTeleportCondition { }

        public class CargoShipCondition : TargetTeleportCondition { }

        public class TugBoatCondition : TargetTeleportCondition { }

        public class HotAirBalloonCondition : TargetTeleportCondition { }

        public class InWaterCondition : TargetTeleportCondition { }

        public class NoTPZoneCondition : TargetTeleportCondition { }

        public class SafeZoneCondition : TargetTeleportCondition { }

        public class BuildingBlockedCondition : AllTeleportCondition { }

        public class OilRigCondition : AllTeleportCondition { }

        public class UnderwaterLabsCondition : AllTeleportCondition { }

        public class TrainTunnelsCondition : AllTeleportCondition { }

        public class OnWaterCondition : TargetTeleportCondition
        {
            [JsonProperty("Max height above water level to be considered on water")]
            public float MaxHeight { get; set; } = 3f;
        }

        public class HostileCondition : TargetTeleportCondition
        {
            [JsonProperty("Only check when warping")]
            public bool OnlyWarps { get; set; }
        }

        public class InMonumentCondition : AllTeleportCondition
        {
            [JsonProperty("Ignore safe zones when checking condition")]
            public bool IgnoreSafeZones { get; set; }

            [JsonProperty("Ignore these monuments from condition check (Monument shortnames can be found on the plugin overview)")]
            public string[] IgnoreMonuments { get; set; } = Array.Empty<string>();
        }

        public class CustomTopologyCondition : AllTeleportCondition
        {
            [JsonProperty("Topology names (ex. ['Road', 'Roadside', 'Cliff'], replacing single quotation marks with double quotation marks)")]
            public string[] Topologies { get; set; } = Array.Empty<string>();
        }

        public abstract class AllTeleportCondition : TargetTeleportCondition
        {
            [JsonProperty("Can teleport if target position has condition")]
            public bool CanTeleportTargetPosition { get; set; }
        }

        public abstract class TargetTeleportCondition : BasicTeleportCondition
        {
            [JsonProperty("Can teleport if target player has condition")]
            public bool CanTeleportTargetPlayer { get; set; }
        }

        public abstract class BasicTeleportCondition
        {
            [JsonProperty("Can teleport if player has condition")]
            public bool CanTeleport { get; set; }
        }

        #endregion
    }
}
