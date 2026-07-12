using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Leaderboard;

public class LeaderboardConfig
{
    [JsonProperty("StorageType")] public string StorageType { get; set; } = "Json"; // Json | Sqlite
    [JsonProperty("DataFolder")] public string DataFolder { get; set; } = "HarmonyMods_Data/LeaderboardData";
    [JsonProperty("Commands")] public string[] Commands { get; set; } = { "leaderboard", "lb", "stats" };
    [JsonProperty("CooldownSeconds")] public float CooldownSeconds { get; set; } = 0.2f;
    [JsonProperty("WipeDataOnNewSave")] public bool WipeDataOnNewSave { get; set; } = false;

    [JsonProperty("Relay")]
    public RelayConfig Relay { get; set; } = new();

    [JsonProperty("Discord")]
    public DiscordConfig Discord { get; set; } = new();

    [JsonProperty("TemplatePath")] public string TemplatePath { get; set; } = "LeaderboardData/Templates";

    /// <summary>Base URL for leaderboard stat icons (e.g. https://yourserver.com/images/Leaderboard/). Must end with / or be empty. RawImage needs a full URL.</summary>
    [JsonProperty("ImageBaseUrl")] public string ImageBaseUrl { get; set; } = "";
}

public class RelayConfig
{
    [JsonProperty("Enabled")] public bool Enabled { get; set; } = false;
    [JsonProperty("Url")] public string Url { get; set; } = ""; // POST JSON to bot
    [JsonProperty("BatchIntervalSeconds")] public float BatchIntervalSeconds { get; set; } = 30f;
}

public class DiscordConfig
{
    [JsonProperty("WebhookUrl")] public string WebhookUrl { get; set; } = "";
    [JsonProperty("AutoMessageIntervalSeconds")] public float AutoMessageIntervalSeconds { get; set; } = 3600f;
    [JsonProperty("Enabled")] public bool Enabled { get; set; } = false;
}
