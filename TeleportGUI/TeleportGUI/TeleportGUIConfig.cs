using System.Collections.Generic;
using Newtonsoft.Json;

namespace TeleportGUI
{
    /// <summary>Config structure aligned with Oxide TeleportGUI (no VIP tiers; use Default + AdminsBypass).</summary>
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

            [JsonProperty("Command aliases")]
            public List<string> CommandAliases { get; set; } = new List<string> { "warp" };
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
                [JsonProperty("Anchor (TopLeft, TopCenter, TopRight, CenterLeft, Center, CenterRight, BottomLeft, BottomCenter, BottomRight, FullStretch, TopStretch, HorizontalCenterStretch, BottomStretch, LeftStretch, VerticalCenterStretch, RightStretch)")]
                public string Anchor { get; set; } = "CenterRight";

                [JsonProperty("Offset")]
                public RequestPopupOffset Offset { get; set; } = new RequestPopupOffset();
            }

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

        public class TeleportConditions
        {
            [JsonProperty("Can teleport whilst bleeding")]
            public ConditionBools WhilstBleeding { get; set; } = new ConditionBools { Target = true, Player = false };

            [JsonProperty("Can teleport whilst crafting")]
            public ConditionBools WhenCrafting { get; set; } = new ConditionBools { Target = true, Player = false };

            [JsonProperty("Can teleport if mounted")]
            public ConditionBools Mounted { get; set; } = new ConditionBools { Target = false, Player = false };

            [JsonProperty("Can teleport if building blocked")]
            public ConditionBoolsTriple BuildingBlocked { get; set; } = new ConditionBoolsTriple { TargetPosition = false, TargetPlayer = false, Player = false };

            [JsonProperty("Can teleport if in water")]
            public ConditionBools InWater { get; set; } = new ConditionBools { Target = false, Player = false };

            [JsonProperty("Can teleport if on water")]
            public ConditionOnWater OnWater { get; set; } = new ConditionOnWater { MaxHeightAboveWater = 3f, Target = true, Player = true };

            [JsonProperty("Can teleport if in safe zone")]
            public ConditionBools InSafeZone { get; set; } = new ConditionBools { Target = true, Player = true };
        }

        public class ConditionBools
        {
            [JsonProperty("Can teleport if target player has condition")]
            public bool Target { get; set; }
            [JsonProperty("Can teleport if player has condition")]
            public bool Player { get; set; }
        }

        public class ConditionBoolsTriple
        {
            [JsonProperty("Can teleport if target position has condition")]
            public bool TargetPosition { get; set; }
            [JsonProperty("Can teleport if target player has condition")]
            public bool TargetPlayer { get; set; }
            [JsonProperty("Can teleport if player has condition")]
            public bool Player { get; set; }
        }

        public class ConditionOnWater
        {
            [JsonProperty("Max height above water level to be considered on water")]
            public float MaxHeightAboveWater { get; set; } = 3f;
            [JsonProperty("Can teleport if target player has condition")]
            public bool Target { get; set; } = true;
            [JsonProperty("Can teleport if player has condition")]
            public bool Player { get; set; } = true;
        }
    }
}
