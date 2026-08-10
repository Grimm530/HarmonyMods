using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace Leaderboard.Relay;

/// <summary>
/// Sends data in the same format as the Oxide UltimateLeaderboard plugin's MySQL tables,
/// so your endpoint can write to the same DB. Stats we don't track simply won't be sent
/// (website shows 0 for those). All values are totals, not deltas.
/// </summary>
public static class RelaySender
{
    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

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
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(json)) return;
        // Use HttpClient (not UnityWebRequest) so loopback http:// relays work.
        // Do not rewrite http→https — LeaderBot relay is plain HTTP.
        _ = Task.Run(async () =>
        {
            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var resp = await Http.PostAsync(url, content).ConfigureAwait(false);
                var code = (long)resp.StatusCode;
                try { onDone?.Invoke(code); } catch { }
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(body) && body.Length > 200)
                        body = body.Substring(0, 200);
                    UnityEngine.Debug.LogWarning($"[Leaderboard] Relay POST {url} -> {code} {body}");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Leaderboard] Relay POST {url}: {ex.Message}");
                try { onDone?.Invoke(0); } catch { }
            }
        });
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
