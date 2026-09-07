using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace TeleportGUI
{
    /// <summary>Config structure aligned with original TeleportGUI (no VIP tiers; use Default + AdminsBypass).</summary>
    public class TeleportGUIConfig
    {
        [JsonProperty("Allowed Steam IDs (empty = everyone can use; otherwise only these + admins)")]
        public List<string> AllowedSteamIds { get; set; } = new List<string>();

        [JsonProperty("Admins bypass allowlist and limits")]
        public bool AdminsBypass { get; set; } = true;

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

        [JsonProperty("Warp points (name -> position). Include Outpost, Bandit, or custom. Set X,Y,Z in config or via admin.)")]
        public Dictionary<string, WarpPointConfig> WarpPoints { get; set; } = new Dictionary<string, WarpPointConfig>
        {
            ["Outpost"] = new WarpPointConfig { X = 0, Y = 0, Z = 0 },
            ["Bandit"] = new WarpPointConfig { X = 0, Y = 0, Z = 0 }
        };

        [JsonProperty("Data folder path (empty = serverRoot/HarmonyData/TeleportGUI)")]
        public string DataFolderPath { get; set; } = "";

        [JsonProperty("TpBack command aliases (e.g. tpback, back)")]
        public List<string> TpBackCommandAliases { get; set; } = new List<string> { "tpback", "back" };

        [JsonProperty("Death command aliases (teleport to last death location)")]
        public List<string> DeathCommandAliases { get; set; } = new List<string> { "death" };

        [JsonProperty("Record death location for /death command")]
        public bool RecordDeathLocation { get; set; } = true;

        public class WarpPointConfig
        {
            [JsonProperty("X")]
            public float X { get; set; }
            [JsonProperty("Y")]
            public float Y { get; set; }
            [JsonProperty("Z")]
            public float Z { get; set; }
        }

        public class ChatOptions
        {
            [JsonProperty("Use chat prefix")]
            public bool UsePrefix { get; set; } = false;

            [JsonProperty("Chat prefix")]
            public string Prefix { get; set; } = "<color=#C4FF00>TP: </color>";

            [JsonProperty("Chat icon (steam ID)")]
            public ulong Icon { get; set; } = 0;
        }

        public class TeleportOptions
        {
            [JsonProperty("Teleport request timeout (seconds)")]
            public int RequestTimeout { get; set; } = 30;

            [JsonProperty("Only shows friends, clan members and team mates in player list")]
            public bool FriendliesOnly { get; set; } = false;

            [JsonProperty("Cancel pending teleport if hurt")]
            public bool CancelOnDamage { get; set; } = false;

            [JsonProperty("Cancel pending teleport if either player dies")]
            public bool CancelOnDeath { get; set; } = false;

            [JsonProperty("Teleport delay options")]
            public DefaultIntOption Delay { get; set; } = new DefaultIntOption { Default = 5 };

            [JsonProperty("Teleport cooldown options")]
            public DefaultCooldownOption Cooldown { get; set; } = new DefaultCooldownOption { Default = 300 };

            [JsonProperty("Teleport daily limit options")]
            public DefaultLimitOption DailyLimit { get; set; } = new DefaultLimitOption { Default = 10 };

            [JsonProperty("Limits")]
            public LimitOptions Limits { get; set; } = new LimitOptions();

            [JsonProperty("Purchase")]
            public PurchaseOptions Purchase { get; set; } = new PurchaseOptions();

            [JsonProperty("Command aliases")]
            public List<string> CommandAliases { get; set; } = new List<string> { "tp", "tpr" };
        }

        public class HomeOptions
        {
            [JsonProperty("Max home options")]
            public HomeLimitOption MaxHomes { get; set; } = new HomeLimitOption { Default = 5 };

            [JsonProperty("Sleeping bag homes")]
            public SleepingBagOptions SleepingBags { get; set; } = new SleepingBagOptions();

            [JsonProperty("Allow creating home in building blocked area")]
            public bool AllowSetHomeInBuildBlocked { get; set; } = false;

            [JsonProperty("Allow creating home on a tugboat")]
            public bool AllowSetHomeOnTugboat { get; set; } = true;

            [JsonProperty("Require building privilege to set home")]
            public bool RequirePrivilegeSetHome { get; set; } = true;

            [JsonProperty("Homes can only be set on building blocks")]
            public bool MustSetHomeOnBuilding { get; set; } = false;

            [JsonProperty("Allow homes to be set on floors")]
            public bool CanSetHomeOnFloor { get; set; } = true;

            [JsonProperty("Don't allow homes to be set within X distance of another home")]
            public float MinimumHomeRadiusDistance { get; set; } = 20f;

            [JsonProperty("Disable home point if it is clipping inside a wall or entity")]
            public bool DisableHomeInEntity { get; set; } = true;

            [JsonProperty("Wipe home data when the server is wiped")]
            public bool WipeHomesOnNewServerSave { get; set; } = true;

            [JsonProperty("Cancel pending teleport if hurt")]
            public bool CancelOnDamage { get; set; } = false;

            [JsonProperty("Cancel pending teleport if either player dies")]
            public bool CancelOnDeath { get; set; } = false;

            [JsonProperty("Teleport delay options")]
            public DefaultIntOption Delay { get; set; } = new DefaultIntOption { Default = 5 };

            [JsonProperty("Teleport cooldown options")]
            public DefaultCooldownOption Cooldown { get; set; } = new DefaultCooldownOption { Default = 60 };

            [JsonProperty("Teleport daily limit options")]
            public DefaultLimitOption DailyLimit { get; set; } = new DefaultLimitOption { Default = 0 };

            [JsonProperty("Limits")]
            public LimitOptions Limits { get; set; } = new LimitOptions();

            [JsonProperty("Purchase")]
            public PurchaseOptions Purchase { get; set; } = new PurchaseOptions();

            [JsonProperty("Command aliases")]
            public List<string> CommandAliases { get; set; } = new List<string> { "home", "sethome", "deletehome" };

            public class HomeLimitOption
            {
                [JsonProperty("Default home limit (0 disables limits entirely)")]
                public int Default { get; set; } = 5;
            }

            public class SleepingBagOptions
            {
                [JsonProperty("Create home on bag placement")]
                public bool CreateHomeOnBagPlacement { get; set; } = false;

                [JsonProperty("Create home on bed placement")]
                public bool CreateHomeOnBedPlacement { get; set; } = false;

                [JsonProperty("Create home on beach towel placement")]
                public bool CreateHomeOnBeachTowelPlacement { get; set; } = false;

                [JsonProperty("Only create a home on placement if it is inside a building")]
                public bool OnlyCreateInBuilding { get; set; } = true;

                [JsonProperty("Disable set home command")]
                public bool DisableSetHomeCommand { get; set; } = false;
            }
        }

        public class WarpOptions
        {
            [JsonProperty("Teleport to random point in X vicinity (0 to disable)")]
            public float VicinityTeleportRadius { get; set; } = 0f;

            [JsonProperty("Radius to check for NPC's when teleporting to a monument warp point")]
            public float MonumentWarpNPCRadius { get; set; } = 25f;

            [JsonProperty("Cancel pending teleport if hurt")]
            public bool CancelOnDamage { get; set; } = false;

            [JsonProperty("Cancel pending teleport if either player dies")]
            public bool CancelOnDeath { get; set; } = false;

            [JsonProperty("Teleport delay options")]
            public DefaultIntOption Delay { get; set; } = new DefaultIntOption { Default = 5 };

            [JsonProperty("Teleport cooldown options")]
            public DefaultCooldownOption Cooldown { get; set; } = new DefaultCooldownOption { Default = 120 };

            [JsonProperty("Teleport daily limit options")]
            public DefaultLimitOption DailyLimit { get; set; } = new DefaultLimitOption { Default = 0 };

            [JsonProperty("Limits")]
            public LimitOptions Limits { get; set; } = new LimitOptions();

            [JsonProperty("Purchase")]
            public PurchaseOptions Purchase { get; set; } = new PurchaseOptions();

            [JsonProperty("Monument warps")]
            public Dictionary<string, MonumentWarp> MonumentWarps { get; set; } = new Dictionary<string, MonumentWarp>();

            [JsonProperty("Command aliases")]
            public List<string> CommandAliases { get; set; } = new List<string> { "warp" };

            public class MonumentWarp
            {
                [JsonProperty("Enabled")]
                public bool Enabled { get; set; }
                [JsonProperty("Safe zone only")]
                public bool SafeZoneOnly { get; set; }
                [JsonProperty("Command")]
                public string Command { get; set; } = string.Empty;
            }
        }

        public class DefaultIntOption
        {
            [JsonProperty("Default time until teleport (seconds)")]
            public int Default { get; set; } = 5;
        }

        public class DefaultCooldownOption
        {
            [JsonProperty("Default cooldown time (seconds)")]
            public int Default { get; set; } = 300;
        }

        public class DefaultLimitOption
        {
            [JsonProperty("Default daily limit (0 disables limits entirely)")]
            public int Default { get; set; } = 10;
        }

        public class AdminOptions
        {
            [JsonProperty("Don't notify user's when a admin teleports to them")]
            public bool Silent { get; set; } = false;

            [JsonProperty("Allow instant teleportation for admins")]
            public bool Instant { get; set; } = true;
        }

        public class UIOptions
        {
            [JsonProperty("Disable UI")]
            public bool DisableUI { get; set; } = false;

            [JsonProperty("Hide admins from player search list")]
            public bool HideAdminsInUI { get; set; } = false;

            [JsonProperty("Hide warp points if the player doesn't have permission")]
            public bool HideWarpsNoPermission { get; set; } = false;

            [JsonProperty("UI Colors")]
            public UIColors Colors { get; set; } = new UIColors();

            [JsonProperty("Request Popup")]
            public RequestPopupOptions RequestPopup { get; set; } = new RequestPopupOptions();

            public class UIColors
            {
                [JsonProperty("Background")]
                public UIColorEntry Background { get; set; } = new UIColorEntry { Hex = "1D1A3F", Alpha = 0.94f };

                [JsonProperty("Panel")]
                public UIColorEntry Panel { get; set; } = new UIColorEntry { Hex = "E6E6FA", Alpha = 0.2f };

                [JsonProperty("Header")]
                public UIColorEntry Header { get; set; } = new UIColorEntry { Hex = "8A2BE2", Alpha = 0.314f };

                [JsonProperty("Button")]
                public UIColorEntry Button { get; set; } = new UIColorEntry { Hex = "322A45", Alpha = 1f };

                [JsonProperty("Close")]
                public UIColorEntry Close { get; set; } = new UIColorEntry { Hex = "FF007F", Alpha = 1f };

                [JsonProperty("Highlight")]
                public UIColorEntry Highlight { get; set; } = new UIColorEntry { Hex = "A020F0", Alpha = 1f };
            }

            public class UIColorEntry
            {
                [JsonProperty("Hex")]
                public string Hex { get; set; } = "808080";

                [JsonProperty("Alpha")]
                public float Alpha { get; set; } = 1f;
            }

            public class RequestPopupOptions
            {
                public enum AnchorEnum
                {
                    TopLeft, TopCenter, TopRight, CenterLeft, Center, CenterRight,
                    BottomLeft, BottomCenter, BottomRight, FullStretch, TopStretch,
                    HorizontalCenterStretch, BottomStretch, LeftStretch, VerticalCenterStretch, RightStretch
                }

                [JsonProperty("Anchor (TopLeft, TopCenter, TopRight, CenterLeft, Center, CenterRight, BottomLeft, BottomCenter, BottomRight, FullStretch, TopStretch, HorizontalCenterStretch, BottomStretch, LeftStretch, VerticalCenterStretch, RightStretch)")]
                public AnchorEnum Anchor { get; set; } = AnchorEnum.CenterRight;

                [JsonProperty("Offset")]
                public UIOffset Offset { get; set; } = new UIOffset(-137.5f, -22.5f, 12.5f, 22.5f);

                [JsonProperty("Horizontal padding")]
                public PopupHorizontalPadding Padding { get; set; } = new PopupHorizontalPadding();

                public class UIOffset
                {
                    public float XMin { get; set; }
                    public float YMin { get; set; }
                    public float XMax { get; set; }
                    public float YMax { get; set; }

                    public UIOffset() { }
                    public UIOffset(float xMin, float yMin, float xMax, float yMax)
                    {
                        XMin = xMin; YMin = yMin; XMax = xMax; YMax = yMax;
                    }
                }

                public class PopupHorizontalPadding
                {
                    public float Left { get; set; }
                    public float Right { get; set; } = 10f;
                }

                [Obsolete("Use UIOffset")]
                public class RequestPopupOffset
                {
                    [JsonProperty("XMin")]
                    public float XMin { get; set; } = -137.5f;
                    [JsonProperty("YMin")]
                    public float YMin { get; set; } = -22.5f;
                    [JsonProperty("XMax")]
                    public float XMax { get; set; } = 12.5f;
                    [JsonProperty("YMax")]
                    public float YMax { get; set; } = 22.5f;
                }
            }
        }

        public enum PurchaseMode
        {
            Scrap,
            Economics,
            ServerRewards
        }

        public class LimitOptions
        {
            [JsonProperty("Default daily limit (0 disables limits entirely)")]
            public int Default { get; set; }

            [JsonProperty("VIP limits (permission -> limit)")]
            public Dictionary<string, int> Vip { get; set; } = new Dictionary<string, int>();

            public int GetHighestOption(Func<string, bool> hasPerm)
            {
                int best = Default;
                if (Vip == null) return best;
                foreach (var kv in Vip)
                {
                    if (hasPerm(kv.Key) && kv.Value > best)
                        best = kv.Value;
                }
                return best;
            }
        }

        public class PurchaseOptions
        {
            [JsonProperty("Default cost")]
            public int Default { get; set; }

            [JsonProperty("Pay always")]
            public bool PayAlways { get; set; }

            [JsonProperty("Pay after using daily limits")]
            public bool PayAfterUsingDailyLimits { get; set; }

            [JsonProperty("Currency mode")]
            public PurchaseMode Mode { get; set; } = PurchaseMode.Scrap;

            [JsonProperty("VIP costs (permission -> cost)")]
            public Dictionary<string, int> Vip { get; set; } = new Dictionary<string, int>();

            public int GetLowestOption(Func<string, bool> hasPerm)
            {
                int best = Default;
                bool matched = false;
                if (Vip == null) return best;
                foreach (var kv in Vip)
                {
                    if (!hasPerm(kv.Key)) continue;
                    if (!matched || kv.Value < best)
                    {
                        best = kv.Value;
                        matched = true;
                    }
                }
                return best;
            }
        }

        public class PlayerTargetCondition
        {
            public bool CanTeleport { get; set; } = true;
            public bool CanTeleportTargetPlayer { get; set; } = true;
        }

        public class PositionCondition : PlayerTargetCondition
        {
            public bool CanTeleportTargetPosition { get; set; } = true;
        }

        public class WhilstBleedingCondition : PlayerTargetCondition { }
        public class WhenCraftingCondition : PlayerTargetCondition { }
        public class MountedCondition : PlayerTargetCondition { }
        public class InWaterCondition : PlayerTargetCondition { }
        public class SafeZoneCondition : PlayerTargetCondition { }
        public class NoTPZoneCondition : PlayerTargetCondition { }
        public class RaidBlockedCondition : PlayerTargetCondition { }
        public class TargetTeleportCondition : PlayerTargetCondition { public new bool CanTeleport { get; set; } = false; }
        public class BuildingBlockedCondition : PositionCondition { }
        public class OilRigCondition : PositionCondition { }
        public class UnderwaterLabsCondition : PositionCondition { }
        public class TrainTunnelsCondition : PositionCondition { }

        public class OnWaterCondition : PlayerTargetCondition
        {
            public float MaxHeight { get; set; } = 3f;
        }

        public class HostileCondition : PlayerTargetCondition
        {
            public bool OnlyWarps { get; set; }
        }

        public class InMonumentCondition : PositionCondition
        {
            public bool IgnoreSafeZones { get; set; }
            public string[] IgnoreMonuments { get; set; } = Array.Empty<string>();
        }

        public class CustomTopologyCondition : PositionCondition
        {
            public string[] Topologies { get; set; } = Array.Empty<string>();
        }

        public class TeleportConditions
        {
            public WhilstBleedingCondition WhilstBleeding { get; set; } = new WhilstBleedingCondition { CanTeleport = false, CanTeleportTargetPlayer = true };
            public WhenCraftingCondition WhenCrafting { get; set; } = new WhenCraftingCondition { CanTeleport = false, CanTeleportTargetPlayer = true };
            public MountedCondition Mounted { get; set; } = new MountedCondition { CanTeleport = false, CanTeleportTargetPlayer = false };
            public BuildingBlockedCondition BuildingBlocked { get; set; } = new BuildingBlockedCondition();
            public RaidBlockedCondition RaidBlocked { get; set; } = new RaidBlockedCondition();
            public TargetTeleportCondition CargoShip { get; set; } = new TargetTeleportCondition();
            public TargetTeleportCondition TugBoat { get; set; } = new TargetTeleportCondition();
            public TargetTeleportCondition HotAirBalloon { get; set; } = new TargetTeleportCondition();
            public OilRigCondition OilRig { get; set; } = new OilRigCondition();
            public UnderwaterLabsCondition UnderwaterLabs { get; set; } = new UnderwaterLabsCondition();
            public TrainTunnelsCondition TrainTunnels { get; set; } = new TrainTunnelsCondition();
            public InWaterCondition InWater { get; set; } = new InWaterCondition { CanTeleport = false, CanTeleportTargetPlayer = false };
            public OnWaterCondition OnWater { get; set; } = new OnWaterCondition { CanTeleport = true, CanTeleportTargetPlayer = true, MaxHeight = 3f };
            public NoTPZoneCondition NoTpZone { get; set; } = new NoTPZoneCondition();
            public SafeZoneCondition SafeZone { get; set; } = new SafeZoneCondition { CanTeleport = true, CanTeleportTargetPlayer = true };
            public HostileCondition Hostile { get; set; } = new HostileCondition();
            public InMonumentCondition InMonument { get; set; } = new InMonumentCondition();
            public CustomTopologyCondition Topology { get; set; } = new CustomTopologyCondition();
        }
    }
}
