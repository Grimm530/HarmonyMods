using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Rustcord;

/// <summary>
/// Config for Rustcord Harmony mod. Slim structure (no Oxide).
/// Loads from HarmonyConfig/Rustcord.json (or oxide/config/Rustcord.json as fallback).
/// Generates default config file if none exists.
/// </summary>
public static class RustcordConfig
{
    public static ConfigData Config { get; private set; }

    private static string _configPath;

    public static void LoadConfig()
    {
        var serverRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        // Try HarmonyConfig first, then oxide/config (per README)
        var harmonyPath = Path.Combine(serverRoot, "HarmonyConfig", "Rustcord.json");
        var oxidePath = Path.Combine(serverRoot, "oxide", "config", "Rustcord.json");

        if (File.Exists(harmonyPath))
        {
            _configPath = harmonyPath;
            TryLoadConfig(harmonyPath);
            return;
        }
        if (File.Exists(oxidePath))
        {
            _configPath = oxidePath;
            TryLoadConfig(oxidePath);
            return;
        }

        // No config exists: create default at HarmonyConfig and save
        var harmonyDir = Path.Combine(serverRoot, "HarmonyConfig");
        if (!Directory.Exists(harmonyDir))
            Directory.CreateDirectory(harmonyDir);

        _configPath = harmonyPath;
        Config = CreateDefaultConfig();
        EnsureDefaults();
        SaveConfig();
        RustcordMod.Log($"Created default config at HarmonyConfig/Rustcord.json - ADD API Key (Bot Token) to enable Discord posting!", force: true);
    }

    private static bool TryLoadConfig(string fullPath)
    {
        try
        {
            var json = File.ReadAllText(fullPath);
            Config = JsonConvert.DeserializeObject<ConfigData>(json) ?? new ConfigData();
            EnsureDefaults();
            RustcordMod.Log($"Config loaded from {fullPath}", force: true);
            return true;
        }
        catch (Exception ex)
        {
            RustcordMod.Log($"Failed to load config: {ex.Message}", force: true);
            Config = new ConfigData();
            EnsureDefaults();
            return false;
        }
    }

    /// <summary>Create default config matching Oxide Rustcord structure. Uses Bot token + channel IDs (no webhooks required).</summary>
    private static ConfigData CreateDefaultConfig()
    {
        return new ConfigData
        {
            General = new GeneralSettings { ServerName = "TestServer" },
            PostSettings = new PostSettings { PlayerChat = true, JoinsQuits = false, Deaths = false, CrateDrops = false },
            OutputFormat = new OutputSettings(),
            Channels = new List<ChannelConfig>
            {
                new ChannelConfig { Channelid = 1319693648350281818, perms = new List<string> { "msg_teamchat" }, CustomWordsToLog = new List<string>(), WebhookUrl = "" },
                new ChannelConfig { Channelid = 1320013624982638723, perms = new List<string> { "msg_chat" }, CustomWordsToLog = new List<string>(), WebhookUrl = "" },
                new ChannelConfig { Channelid = 1319693759855726632, perms = new List<string> { "msg_join", "msg_quit" }, CustomWordsToLog = new List<string>(), WebhookUrl = "" },
                new ChannelConfig
                {
                    Channelid = 1319693785361420288,
                    perms = new List<string> { "cmd_kick", "cmd_com", "cmd_mute", "cmd_unmute", "death_pvp", "game_bug", "msg_serverinit", "game_report", "cmd_allow", "cmd_players" },
                    CustomWordsToLog = new List<string>(),
                    WebhookUrl = ""
                }
            },
            Webhooks = new Dictionary<string, string> { { "1320013624982638723", "" } }
        };
    }

    public static void SaveConfig()
    {
        if (string.IsNullOrEmpty(_configPath) || Config == null) return;
        try
        {
            var json = JsonConvert.SerializeObject(Config, Formatting.Indented);
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            RustcordMod.Log($"Failed to save config: {ex.Message}", force: true);
        }
    }

    private static void EnsureDefaults()
    {
        Config ??= new ConfigData();
        Config.General ??= new GeneralSettings();
        Config.DiscordSide ??= new DiscordSideSettings();
        Config.PostSettings ??= new PostSettings();
        Config.Channels ??= new List<ChannelConfig>();
        Config.Filters ??= new FilterSettings();
        Config.OutputFormat ??= new OutputSettings();
        Config.CommandRoles ??= new CommandRoleSettings();
    }

    public class ConfigData
    {
        [JsonProperty("General Settings")]
        public GeneralSettings General { get; set; } = new();

        [JsonProperty("Discord to Game Settings")]
        public DiscordSideSettings DiscordSide { get; set; } = new();

        [JsonProperty("Post to Discord (what the mod sends)")]
        public PostSettings PostSettings { get; set; } = new();

        [JsonProperty("Discord Output Formatting")]
        public OutputSettings OutputFormat { get; set; } = new();

        [JsonProperty("Filter Settings")]
        public FilterSettings Filters { get; set; } = new();

        [JsonProperty("Discord Logging Channels")]
        public List<ChannelConfig> Channels { get; set; } = new();

        /// <summary>Channel ID to Webhook URL. For Harmony mod using webhooks only.</summary>
        [JsonProperty("Webhooks (Channel ID -> URL, for Harmony mod without bot)")]
        public Dictionary<string, string> Webhooks { get; set; } = new();

        [JsonProperty("Discord Command Role Assignment (Empty = All roles can use command.)")]
        public CommandRoleSettings CommandRoles { get; set; } = new();
    }

    public class GeneralSettings
    {
        [JsonProperty("API Key (Bot Token)")]
        public string Apikey { get; set; } = "";

        [JsonProperty("Server Name (for multi-server: shown in Discord and cross-server chat, e.g. SVR1)")]
        public string ServerName { get; set; } = "";

        [JsonProperty("Auto Reload Plugin")]
        public bool AutoReload { get; set; } = false;

        [JsonProperty("Auto Reload Time (Seconds)")]
        public int AutoReloadSeconds { get; set; } = 901;

        [JsonProperty("Enable Bot Status")]
        public bool EnableBotStatus { get; set; } = false;

        [JsonProperty("In-Game Report Command")]
        public string ReportCommand { get; set; } = "report";

        [JsonProperty("Discord Extension Log Level (Verbose/Debug/Info/Warning/Error/Exception/Off)")]
        public string LogLevel { get; set; } = "Info";
    }

    public class DiscordSideSettings
    {
        /// <summary>When false, Discord→Game (chat & commands) is disabled. Harmony mod does not implement Discord→Game; ticket-support bot handles it via RCON. This option exists for config parity with Oxide Rustcord.</summary>
        [JsonProperty("Enable Discord to Game (chat & commands from Discord into game)")]
        public bool EnableDiscordToGame { get; set; } = false;

        [JsonProperty("Discord Command Prefix")]
        public string CommandPrefix { get; set; } = "!";

        [JsonProperty("Discord to Game Chat: Icon (Steam ID)")]
        public ulong DiscordChatIconSteamId { get; set; } = 76561197967147520;

        [JsonProperty("Discord to Game Chat: Tag")]
        public string DiscordChatTag { get; set; } = "[DiscordChat]";

        [JsonProperty("Discord to Game Chat: Tag Color (Hex)")]
        public string DiscordChatTagColor { get; set; } = "#FF4500";

        [JsonProperty("Discord to Game Chat: Player Name Color (Hex)")]
        public string DiscordChatPlayerNameColor { get; set; } = "#55AAFF";

        [JsonProperty("Discord to Game Chat: Message Color (Hex)")]
        public string DiscordChatMessageColor { get; set; } = "#008000";

        [JsonProperty("Debug: Discord to Game forwarding (log to server console)")]
        public bool DebugDiscordToGame { get; set; } = false;
    }

    /// <summary>Minimal: what the Harmony mod posts. No Oxide plugin hooks.</summary>
    public class PostSettings
    {
        [JsonProperty("Player Chat")]
        public bool PlayerChat { get; set; } = true;

        [JsonProperty("Joins & Quits")]
        public bool JoinsQuits { get; set; } = false;

        [JsonProperty("Deaths")]
        public bool Deaths { get; set; } = false;

        [JsonProperty("Crate Drops (Hackable/Supply)")]
        public bool CrateDrops { get; set; } = false;
    }

    public class OutputSettings
    {
        [JsonProperty("Output Type: Bans (Simple/Embed)")]
        public string OutputTypeBans { get; set; } = "Simple";

        [JsonProperty("Output Type: Bug Report (Simple/Embed)")]
        public string OutputTypeBugReport { get; set; } = "Simple";

        [JsonProperty("Output Type: Deaths (Simple/Embed/DeathNotes)")]
        public string OutputTypeDeaths { get; set; } = "Simple";

        [JsonProperty("Output Type: F7 Reports (Simple/Embed)")]
        public string OutputTypeF7Reports { get; set; } = "Simple";

        [JsonProperty("Output Type: Join/Quit (Simple/Embed)")]
        public string OutputTypeJoinQuit { get; set; } = "Simple";

        [JsonProperty("Output Type: Join Player Info (Admin Channel) (Simple/Embed)")]
        public string OutputTypeJoinPlayerInfo { get; set; } = "Simple";

        [JsonProperty("Output Type: Kicks (Simple/Embed)")]
        public string OutputTypeKicks { get; set; } = "Simple";

        [JsonProperty("Output Type: Note Logging (Simple/Embed)")]
        public string OutputTypeNoteLogging { get; set; } = "Simple";

        [JsonProperty("Output Type: Player Name Change (Simple/Embed)")]
        public string OutputTypePlayerNameChange { get; set; } = "Simple";

        [JsonProperty("Output Type: /Report (Simple/Embed)")]
        public string OutputTypeReport { get; set; } = "Simple";

        [JsonProperty("Output Type: Server Wipe (Simple/Embed)")]
        public string OutputTypeServerWipe { get; set; } = "Simple";

        [JsonProperty("Output Type: Teams (Simple/Embed)")]
        public string OutputTypeTeams { get; set; } = "Simple";
    }

    public class FilterSettings
    {
        [JsonProperty("Chat Filter: Replacement Word")]
        public string FilteredWord { get; set; } = "<censored>";

        [JsonProperty("Chat Filter: Words to Filter")]
        public List<string> FilterWords { get; set; } = new();
    }

    public class ChannelConfig
    {
        [JsonProperty("Discord Channel ID #")]
        public ulong Channelid { get; set; }

        [JsonProperty("Channel Flags")]
        public List<string> perms { get; set; } = new();

        [JsonProperty("Custom: Words/Phrases to Log")]
        public List<string> CustomWordsToLog { get; set; } = new();

        /// <summary>Webhook URL for this channel (Harmony mod webhook mode).</summary>
        [JsonProperty("Webhook URL")]
        public string WebhookUrl { get; set; } = "";
    }

    public class CommandRoleSettings
    {
        [JsonProperty("ban")]
        public List<string> Ban { get; set; } = new();

        [JsonProperty("com")]
        public List<string> Com { get; set; } = new();

        [JsonProperty("kick")]
        public List<string> Kick { get; set; } = new();

        [JsonProperty("mute")]
        public List<string> Mute { get; set; } = new();

        [JsonProperty("players")]
        public List<string> Players { get; set; } = new();

        [JsonProperty("timeban")]
        public List<string> Timeban { get; set; } = new();

        [JsonProperty("unban")]
        public List<string> Unban { get; set; } = new();

        [JsonProperty("unmute")]
        public List<string> Unmute { get; set; } = new();
    }
}
