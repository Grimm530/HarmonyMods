using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Leaderboard.Relay;

/// <summary>
/// Sends data in the same format as the Oxide UltimateLeaderboard plugin's MySQL tables,
/// so your endpoint can write to the same DB. Stats we don't track simply won't be sent
/// (website shows 0 for those). All values are totals, not deltas.
/// </summary>
public static class RelaySender
{
    public static void SendBatch(string url, List<StatUpdatePayload> updates, List<PlayerStatsPayload> players = null)
    {
        if (string.IsNullOrEmpty(url)) return;
        if (updates == null && (players == null || players.Count == 0)) return;
        var wrapper = new BatchPayload
        {
            Updates = updates ?? new List<StatUpdatePayload>(),
            Players = players ?? new List<PlayerStatsPayload>()
        };
        if (wrapper.Updates.Count == 0 && wrapper.Players.Count == 0) return;
        PostJson(url, JsonConvert.SerializeObject(wrapper), _ => { });
    }

    private static void PostJson(string url, string json, Action<long> onDone)
    {
        if (string.IsNullOrEmpty(url)) return;
        // Rust disallows non-secure connections; force HTTPS so relay works when endpoint supports it.
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url.Substring(7);
        try
        {
            using var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SendWebRequest().completed += _ =>
            {
                try { onDone?.Invoke(req.responseCode); } catch { }
                req.Dispose();
            };
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[Leaderboard] Relay POST: {ex.Message}");
        }
    }
}

/// <summary>Same as plugin StatsStorage row: UserId, LootType, ShortName, ItemValue (total, not delta).</summary>
public class StatUpdatePayload
{
    [JsonProperty("UserId")] public ulong UserId;
    [JsonProperty("LootType")] public int LootType;
    [JsonProperty("ShortName")] public string ShortName;
    [JsonProperty("ItemValue")] public float ItemValue;
}

/// <summary>Same as plugin PlayerStats row. Endpoint can INSERT/UPDATE so website shows same columns for all players.</summary>
public class PlayerStatsPayload
{
    [JsonProperty("UserId")] public ulong UserId;
    [JsonProperty("LastIP")] public string LastIP;
    [JsonProperty("LastName")] public string LastName;
    [JsonProperty("ConnectTime")] public string ConnectTime;
    [JsonProperty("DisconnectTime")] public string DisconnectTime;
    [JsonProperty("TotalPlayTime")] public string TotalPlayTime;
    [JsonProperty("Points")] public float Points;
    [JsonProperty("HiddenFromLeaderboard")] public int HiddenFromLeaderboard;
}

public class BatchPayload
{
    [JsonProperty("Updates")] public List<StatUpdatePayload> Updates;
    [JsonProperty("Players")] public List<PlayerStatsPayload> Players;
}
