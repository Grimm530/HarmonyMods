using System.Collections.Generic;
using Newtonsoft.Json;

namespace MapVoter;

/// <summary>
/// Config compatible with Oxide MapVoter schema where applicable.
/// Discord logging uses ticket-support-system mapvoterDiscordBridge (HTTP relay).
/// </summary>
public class MapVoterConfig
{
    [JsonProperty("Config Version")]
    public string ConfigVersion { get; set; } = "2.0.0";

    [JsonProperty("Commands")]
    public CommandsOptions Commands { get; set; } = new();

    [JsonProperty("Options")]
    public OptionsSettings Options { get; set; } = new();

    [JsonProperty("Discord Settings")]
    public DiscordSettings Discord { get; set; } = new();

    /// <summary>Map size (e.g. 4000). Mod randomly picks 8 different seeds for this size from RustMaps.</summary>
    [JsonProperty("Map size")]
    public int MapSize { get; set; } = 4000;

    [JsonProperty("Number of maps to show (random seeds)")]
    public int NumberOfMaps { get; set; } = 8;

    /// <summary>URL template for RustMaps preview. {0}=size, {1}=seed. Fallback when API returns no image.</summary>
    [JsonProperty("RustMaps image URL template (fallback - API is used first)")]
    public string RustMapsImageUrlTemplate { get; set; } = "https://rustmaps.com/map/{0}_{1}";

    /// <summary>RustMaps API key. Required for map preview images. Get from rustmaps.com/user/profile. Free tier: 250/month.</summary>
    [JsonProperty("RustMaps API key (required for map images - get from rustmaps.com/user/profile, free: 250/month)")]
    public string RustMapsApiKey { get; set; } = "";

    /// <summary>Folder where map images are saved. Default: server root/HarmonyImages/MapVoter</summary>
    [JsonProperty("Images path (where map previews are saved - default: HarmonyImages/MapVoter in server root)")]
    public string ImagesPath { get; set; } = "";

    /// <summary>When true, only use local images from Images path. No RustMaps API. Write seeds to seeds_to_generate.txt for external image generation.</summary>
    [JsonProperty("Use local images only (no RustMaps API - run GenerateMapImages.ps1 for seeds)")]
    public bool UseLocalImagesOnly { get; set; } = false;

    /// <summary>Max dimension (pixels) when resizing map images for UI. Large PNGs (~10MB) are resized and JPEG-compressed to ~1MB. Default 768.</summary>
    [JsonProperty("Map image max dimension (resize/compress for UI - default 768)")]
    public int MapImageMaxDimension { get; set; } = 768;

    /// <summary>JPEG quality (0-100) when compressing map images for UI. Lower = smaller file. Default 75. (Currently uses PNG; JPEG reserved for future.)</summary>
    [JsonProperty("Map image JPEG quality (0-100, default 75)")]
    public int MapImageJpegQuality { get; set; } = 75;

    /// <summary>Max dimension for images sent to Discord (smaller = smaller payload). Default 512.</summary>
    [JsonProperty("Discord image max dimension (smaller = smaller payload - default 512)")]
    public int DiscordImageMaxDimension { get; set; } = 512;

    [JsonProperty("Map options (manual list - used when Map size is 0)")]
    public List<MapOption> Maps { get; set; } = new();

    [JsonProperty("Auto Vote")]
    public AutoVoteOptions AutoVote { get; set; } = new();

    [JsonProperty("Auto Wipe")]
    public AutoWipeOptions AutoWipe { get; set; } = new();

    [JsonProperty("Server data wipe")]
    public ServerDataWipeOptions ServerDataWipe { get; set; } = new();

    [JsonProperty("Logs wipe")]
    public LogsWipeOptions LogsWipe { get; set; } = new();

    [JsonProperty("Oxide wipe")]
    public OxideWipeOptions OxideWipe { get; set; } = new();

    [JsonProperty("UI Theme")]
    public UITheme Theme { get; set; } = new();

    public class CommandsOptions
    {
        [JsonProperty("Open MapVoter UI")]
        public string MapVote { get; set; } = "mvote";

        [JsonProperty("Generate Maps")]
        public string Generate { get; set; } = "MapVoter.generate";

        [JsonProperty("vote result")]
        public string VoteResult { get; set; } = "voteresult";
    }

    public class OptionsSettings
    {
        [JsonProperty("Enable file Debug mode (true/false)")]
        public bool FileDebug { get; set; } = false;

        [JsonProperty("Enable Console Debug mode (true/false)")]
        public bool ConsoleDebug { get; set; } = false;

        [JsonProperty("Disable UI")]
        public bool UIisDisabled { get; set; } = false;
    }

    public class DiscordSettings
    {
        [JsonProperty("Log to Discord (true/false)")]
        public bool LogToDiscord { get; set; } = false;

        [JsonProperty("Discord Logs Channel Id")]
        public string LogsChannelId { get; set; } = "";

        [JsonProperty("Vote Channel id")]
        public string VoteChannelId { get; set; } = "";

        [JsonProperty("Winning Map Channel id")]
        public string WinningMapChannelId { get; set; } = "";

        /// <summary>URL of mapvoterDiscordBridge (e.g. http://localhost:3921). Uses ticket-support-system relay.</summary>
        [JsonProperty("Discord bridge URL (ticket-support-system mapvoterDiscordBridge)")]
        public string BridgeUrl { get; set; } = "http://localhost:3921";

        [JsonProperty("Mention role on vote start and end")]
        public string MentionRole { get; set; } = "@everyone";
    }

    public class AutoVoteOptions
    {
        [JsonProperty("Voting Settings")]
        public VotingSettingsOptions VotingSettings { get; set; } = new();

        [JsonProperty("Enable Auto Vote (true/false)")]
        public bool EnableAutoVote { get; set; } = false;

        [JsonProperty("Start voting X days before wipe")]
        public int StartVotingDaysBeforeWipe { get; set; } = 4;

        [JsonProperty("Vote start (HH:mm) 24-hour clock")]
        public string VoteStartTime { get; set; } = "17:00";

        [JsonProperty("Number of maps generated")]
        public int NumberOfMapsGenerated { get; set; } = 4;

        public class VotingSettingsOptions
        {
            [JsonProperty("Stop voting after (minutes)")]
            public int StopVotingAfterMinutes { get; set; } = 60;

            [JsonProperty("Only players with permission can vote (true/false)")]
            public bool OnlyPlayersWithPermissionCanVote { get; set; } = false;
        }
    }

    public class AutoWipeOptions
    {
        [JsonProperty("Custom Map")]
        public CustomMapOptions CustomMap { get; set; } = new();

        [JsonProperty("Map Wipe schedule")]
        public List<int> MapWipeSchedule { get; set; } = new() { 15 };

        [JsonProperty("BP Wipe schedule")]
        public List<int> BPWipeSchedule { get; set; } = new();

        [JsonProperty("Server identity")]
        public string ServerIdentity { get; set; } = "grimm";

        [JsonProperty("Generate Custom Map")]
        public GenerateCustomMapOptions GenerateCustomMap { get; set; } = new();

        [JsonProperty("Enable Auto Wipe (true/false)")]
        public bool EnableAutoWipe { get; set; } = true;

        [JsonProperty("Wipe BPs at forced wipe day")]
        public bool WipeBPsAtForcedWipeDay { get; set; } = false;

        [JsonProperty("Forced Wipe time (HH:mm) 24-hour clock")]
        public string ForcedWipeTime { get; set; } = "19:00";

        [JsonProperty("Wipe time (HH:mm) 24-hour clock")]
        public string WipeTime { get; set; } = "19:00";

        /// <summary>Schedule restart when wipe is within this many minutes. Default 120 (2 hours). Uses WipeTimer for wipe timing.</summary>
        [JsonProperty("Schedule restart when within (minutes) of wipe")]
        public int ScheduleRestartWithinMinutes { get; set; } = 120;

        /// <summary>Do not run periodic checks if wipe is more than this many hours away. Re-checks on next server restart. Default 32.</summary>
        [JsonProperty("Start checking when within (hours) of wipe")]
        public double StartCheckingWithinHours { get; set; } = 32;

        /// <summary>Check interval (minutes) when &gt;2h to wipe. Default 60.</summary>
        [JsonProperty("Check every (minutes) when >2h to wipe")]
        public int CheckIntervalMinutesWhenOver2h { get; set; } = 60;

        /// <summary>Check interval (minutes) when 30min-2h to wipe. Default 15.</summary>
        [JsonProperty("Check every (minutes) when 30min-2h to wipe")]
        public int CheckIntervalMinutesWhen30mTo2h { get; set; } = 15;

        /// <summary>Check interval (minutes) when &lt;30min to wipe. Default 2.</summary>
        [JsonProperty("Check every (minutes) when <30min to wipe")]
        public int CheckIntervalMinutesWhenUnder30m { get; set; } = 2;

        public class CustomMapOptions
        {
            [JsonProperty("Enable custom map (true/false)")]
            public bool EnableCustomMap { get; set; } = false;

            [JsonProperty("Custom map URL")]
            public string CustomMapUrl { get; set; } = "";
        }

        public class GenerateCustomMapOptions
        {
            [JsonProperty("Generate custom map instead of procedural on vote start (true/false)")]
            public bool GenerateCustomMapInsteadOfProcedural { get; set; } = false;

            [JsonProperty("Saved custom config name (RustMaps)")]
            public string SavedCustomConfigName { get; set; } = "CustomMapConfigName";
        }
    }

    /// <summary>Deletes old map/save files from server/{identity}/ on wipe. Uses substring patterns (e.g. "proceduralmap" matches proceduralmap.4000.239.281.map).</summary>
    public class ServerDataWipeOptions
    {
        [JsonProperty("Enable server data wipe on forced wipe day")]
        public bool EnableOnForcedWipeDay { get; set; } = false;

        [JsonProperty("Enable server data wipe on map wipe day")]
        public bool EnableOnMapWipeDay { get; set; } = false;

        [JsonProperty("File names to be deleted on forced wipe day (patterns - in server folder)")]
        public List<string> FileNamesToDeleteOnForcedWipeDay { get; set; } = new();

        [JsonProperty("File names to be deleted on map wipe day (patterns - in server folder)")]
        public List<string> FileNamesToDeleteOnMapWipeDay { get; set; } = new();
    }

    /// <summary>Deletes log files (e.g. logfile-20260218-151044.txt) from logs/ on wipe.</summary>
    public class LogsWipeOptions
    {
        [JsonProperty("Enable logs wipe on forced wipe day")]
        public bool EnableOnForcedWipeDay { get; set; } = false;

        [JsonProperty("Enable logs wipe on map wipe day")]
        public bool EnableOnMapWipeDay { get; set; } = false;

        [JsonProperty("File patterns to delete from logs folder (e.g. logfile)")]
        public List<string> FilePatterns { get; set; } = new() { "logfile" };
    }

    /// <summary>Deletes Oxide logs folder and data files on wipe. Only runs if oxide folder exists.</summary>
    public class OxideWipeOptions
    {
        [JsonProperty("Enable Oxide wipe on forced wipe day")]
        public bool EnableOnForcedWipeDay { get; set; } = false;

        [JsonProperty("Enable Oxide wipe on map wipe day")]
        public bool EnableOnMapWipeDay { get; set; } = false;

        [JsonProperty("Delete oxide/logs folder")]
        public bool DeleteOxideLogsFolder { get; set; } = true;

        [JsonProperty("Oxide data files to delete (relative to oxide/data/)")]
        public List<string> OxideDataFilesToDelete { get; set; } = new() { "oxide.covalence.data", "oxide.lang.data" };
    }

    public class UITheme
    {
        [JsonProperty("Color Scheme")]
        public ColorScheme Colors { get; set; } = new();

        [JsonProperty("Spacing")]
        public SpacingOptions Spacing { get; set; } = new();

        [JsonProperty("Typography")]
        public TypographySettings Typography { get; set; } = new();

        [JsonProperty("Effects")]
        public EffectsOptions Effects { get; set; } = new();

        public class ColorScheme
        {
            [JsonProperty("Deep Background (Almost black)")]
            public string DeepBackground { get; set; } = "#0a0a0f";

            [JsonProperty("Background Color (Panel background)")]
            public string Background { get; set; } = "#1a1d29";

            [JsonProperty("Surface Color (Card surfaces)")]
            public string Surface { get; set; } = "#252938";

            [JsonProperty("Elevated Surface (Hover state)")]
            public string ElevatedSurface { get; set; } = "#2d3346";

            [JsonProperty("Primary Color (Bright cyan)")]
            public string Primary { get; set; } = "#00d9ff";

            [JsonProperty("Secondary Color (Vibrant orange)")]
            public string Secondary { get; set; } = "#ff6b35";

            [JsonProperty("Success Color (Neon green)")]
            public string Success { get; set; } = "#00ff88";

            [JsonProperty("Warning Color (Gold)")]
            public string Warning { get; set; } = "#ffb800";

            [JsonProperty("Error Color (Bright red)")]
            public string Error { get; set; } = "#ff4757";

            [JsonProperty("Info Color (Blue)")]
            public string Info { get; set; } = "#00d9ff";

            [JsonProperty("Text Primary Color (Pure white)")]
            public string TextPrimary { get; set; } = "#ffffff";

            [JsonProperty("Text Secondary Color (Soft blue-gray)")]
            public string TextSecondary { get; set; } = "#a8b2d1";

            [JsonProperty("Text Muted Color (Dim gray)")]
            public string TextMuted { get; set; } = "#6b7694";

            [JsonProperty("Border Color (Borders)")]
            public string Border { get; set; } = "#2d3346";

            [JsonProperty("Overlay Color (Overlay backdrop)")]
            public string Overlay { get; set; } = "0 0 0 0.8";

            [JsonProperty("Shadow Color (Shadow effects)")]
            public string Shadow { get; set; } = "0 0 0 0.4";

            [JsonProperty("Primary Glow (Cyan glow)")]
            public string PrimaryGlow { get; set; } = "#00d9ff";

            [JsonProperty("Secondary Glow (Orange glow)")]
            public string SecondaryGlow { get; set; } = "#ff6b35";

            [JsonProperty("Accent Glow (Purple glow)")]
            public string AccentGlow { get; set; } = "#8b5cf6";
        }

        public class SpacingOptions
        {
            [JsonProperty("Tiny Spacing")]
            public float Tiny { get; set; } = 0.005f;

            [JsonProperty("Small Spacing")]
            public float Small { get; set; } = 0.01f;

            [JsonProperty("Medium Spacing")]
            public float Medium { get; set; } = 0.02f;

            [JsonProperty("Large Spacing")]
            public float Large { get; set; } = 0.04f;

            [JsonProperty("XLarge Spacing")]
            public float XLarge { get; set; } = 0.08f;
        }

        public class TypographySettings
        {
            [JsonProperty("Title Size")]
            public int TitleSize { get; set; } = 20;

            [JsonProperty("Header Size")]
            public int HeaderSize { get; set; } = 16;

            [JsonProperty("Body Size")]
            public int BodySize { get; set; } = 14;

            [JsonProperty("Small Size")]
            public int SmallSize { get; set; } = 12;

            [JsonProperty("Tiny Size")]
            public int TinySize { get; set; } = 10;
        }

        public class EffectsOptions
        {
            [JsonProperty("Enable Card Shadows")]
            public bool EnableCardShadows { get; set; } = true;

            [JsonProperty("Enable Glow Effects (Hover/Selection)")]
            public bool EnableGlowEffects { get; set; } = true;

            [JsonProperty("Enable Blur Background")]
            public bool EnableBlurBackground { get; set; } = true;

            [JsonProperty("Enable Smooth Transitions")]
            public bool EnableSmoothTransitions { get; set; } = true;

            [JsonProperty("Fade In Duration (seconds)")]
            public float FadeInDurationSeconds { get; set; } = 0.3f;

            [JsonProperty("Hover Scale Effect (1.0 = no scale)")]
            public float HoverScaleEffect { get; set; } = 1.05f;

            [JsonProperty("Shadow Blur (pixels)")]
            public float ShadowBlurPixels { get; set; } = 8f;

            [JsonProperty("Glow Blur (pixels)")]
            public float GlowBlurPixels { get; set; } = 20f;
        }
    }

    public class MapOption
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("imageUrl")]
        public string ImageUrl { get; set; } = "";

        /// <summary>Direct link to view map on RustMaps (from API). Fallback for size_seed URL when map is API-generated.</summary>
        [JsonIgnore]
        public string ViewUrl { get; set; } = "";

        /// <summary>FileStorage texture ID for local image (set when loaded from disk).</summary>
        [JsonIgnore]
        public string PngTextureId { get; set; } = "";

        /// <summary>Base64 PNG/JPEG bytes for Discord bridge (set when loaded from disk so bridge can attach image).</summary>
        [JsonIgnore]
        public string ImageDataBase64 { get; set; } = "";
    }
}
