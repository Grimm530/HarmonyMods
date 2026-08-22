using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Leaderboard;

public class LeaderboardConfig
{
    [JsonProperty("StorageType")] public string StorageType { get; set; } = "Json"; // Json | Sqlite
    [JsonProperty("DataFolder")] public string DataFolder { get; set; } = "HarmonyData/LeaderboardData";
    [JsonProperty("Commands")] public string[] Commands { get; set; } = { "leaderboard", "lb", "stats" };
    [JsonProperty("CooldownSeconds")] public float CooldownSeconds { get; set; } = 0.2f;
    [JsonProperty("WipeDataOnNewSave")] public bool WipeDataOnNewSave { get; set; } = false;

    /// <summary>
    /// When true, hits on NPC BasePlayers (scientists, dwellers, custom NPCs) count toward hitrate BodyHits.
    /// Oxide UltimateLeaderboard ties this to "Count NPC kills as player kills" (default false there).
    /// Default true here so PvE/NPC shooting populates the Hitrate tab.
    /// </summary>
    [JsonProperty("CountNpcHitsForHitrate")] public bool CountNpcHitsForHitrate { get; set; } = true;

    /// <summary>
    /// When true, killing an NPC BasePlayer also increments Kill/"kills" (Discord "Killers").
    /// On PvE servers leave false so Killers stays for real PvP and scientists go to "NPC kills" only.
    /// </summary>
    [JsonProperty("CountNpcKillsAsPlayerKills")] public bool CountNpcKillsAsPlayerKills { get; set; } = false;

    [JsonProperty("Relay")]
    public RelayConfig Relay { get; set; } = new();

    [JsonProperty("Discord")]
    public DiscordConfig Discord { get; set; } = new();

    [JsonProperty("TemplatePath")] public string TemplatePath { get; set; } = "LeaderboardData/Templates";

    /// <summary>Base URL for leaderboard stat icons (e.g. https://yourserver.com/images/Leaderboard/). Must end with / or be empty. RawImage needs a full URL.</summary>
    [JsonProperty("ImageBaseUrl")] public string ImageBaseUrl { get; set; } = "";

    /// <summary>
    /// Which optional event mods Leaderboard should hook. Disabled entries are not patched
    /// and are not counted as failures. Enable only mods that actually load on this server.
    /// </summary>
    [JsonProperty("EventIntegration")]
    public EventIntegrationConfig EventIntegration { get; set; } = new();
}

public class EventIntegrationConfig
{
    [JsonProperty("RaidableBases")] public bool RaidableBases { get; set; } = false;
    [JsonProperty("Convoy")] public bool Convoy { get; set; } = true;
    [JsonProperty("ArmoredTrain")] public bool ArmoredTrain { get; set; } = true;
    [JsonProperty("CustomHelicopterTiers")] public bool CustomHelicopterTiers { get; set; } = false;
}

public class RelayConfig
{
    [JsonProperty("Enabled")] public bool Enabled { get; set; } = false;
    /// <summary>POST JSON to LeaderBot /relay. Prefer http://127.0.0.1:8765/relay when the bot runs on the same host.</summary>
    [JsonProperty("Url")] public string Url { get; set; } = "";
    [JsonProperty("BatchIntervalSeconds")] public float BatchIntervalSeconds { get; set; } = 30f;
    /// <summary>On mod load, push every player JSON (Players + all StatsStorage rows) to the relay so Discord/MySQL catch up.</summary>
    [JsonProperty("SyncAllOnLoad")] public bool SyncAllOnLoad { get; set; } = true;

    /// <summary>
    /// Unique id for this Rust instance when several servers share one Discord/MySQL relay.
    /// Combined playtime and stats are SUM of each server's last local totals. Must differ
    /// per machine — do not rely on server.identity when two boxes both use "grimm".
    /// </summary>
    [JsonProperty("ServerId")] public string ServerId { get; set; } = "";
}

public class DiscordConfig
{
    [JsonProperty("WebhookUrl")] public string WebhookUrl { get; set; } = "";
    [JsonProperty("AutoMessageIntervalSeconds")] public float AutoMessageIntervalSeconds { get; set; } = 3600f;
    [JsonProperty("Enabled")] public bool Enabled { get; set; } = false;
}
