using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Leaderboard.Relay;

/// <summary>
/// Sends data in the same format as the Oxide UltimateLeaderboard plugin's MySQL tables,
/// so your endpoint can write to the same DB. Stats we don't track simply won't be sent
/// (website shows 0 for those). All values are totals, not deltas.
/// </summary>
public static class RelaySender
{
    private static readonly object QueueLock = new object();
    private static readonly Queue<(string Url, string Json)> Queue = new Queue<(string, string)>();
    private static bool _workerRunning;

    /// <summary>Max StatsStorage rows per HTTP POST. Remote MySQL cannot finish large batches before the HTTP timeout.</summary>
    private const int MaxUpdatesPerPost = 25;

    /// <summary>Enqueue one or more POSTs. Returns how many HTTP calls were queued.</summary>
    public static int SendBatch(string url, List<StatUpdatePayload> updates, List<PlayerStatsPayload> players = null, string serverId = null)
    {
        if (string.IsNullOrEmpty(url)) return 0;
        updates = updates ?? new List<StatUpdatePayload>();
        players = players ?? new List<PlayerStatsPayload>();
        if (updates.Count == 0 && players.Count == 0) return 0;
        if (string.IsNullOrWhiteSpace(serverId))
            serverId = "unknown";

        if (updates.Count <= MaxUpdatesPerPost)
        {
            Enqueue(url, new BatchPayload { Updates = updates, Players = players, ServerId = serverId });
            return 1;
        }

        var posts = 0;
        if (players.Count > 0)
        {
            Enqueue(url, new BatchPayload { Updates = new List<StatUpdatePayload>(), Players = players, ServerId = serverId });
            posts++;
        }

        var byId = new Dictionary<ulong, PlayerStatsPayload>(players.Count);
        foreach (var p in players)
        {
            if (p != null) byId[p.UserId] = p;
        }

        for (int offset = 0; offset < updates.Count; offset += MaxUpdatesPerPost)
        {
            var take = Math.Min(MaxUpdatesPerPost, updates.Count - offset);
            var chunk = updates.GetRange(offset, take);
            var chunkPlayers = new List<PlayerStatsPayload>();
            var seen = new HashSet<ulong>();
            foreach (var u in chunk)
            {
                if (!seen.Add(u.UserId)) continue;
                if (byId.TryGetValue(u.UserId, out var pp))
                    chunkPlayers.Add(pp);
            }
            Enqueue(url, new BatchPayload { Updates = chunk, Players = chunkPlayers, ServerId = serverId });
            posts++;
        }
        return posts;
    }

    private static void Enqueue(string url, BatchPayload wrapper)
    {
        Enqueue(url, JsonConvert.SerializeObject(wrapper));
    }

    private static void Enqueue(string url, string json)
    {
        lock (QueueLock)
        {
            Queue.Enqueue((url, json));
            if (_workerRunning) return;
            _workerRunning = true;
        }
        _ = Task.Run(ProcessQueue);
    }

    private static void ProcessQueue()
    {
        while (true)
        {
            string url;
            string json;
            lock (QueueLock)
            {
                if (Queue.Count == 0)
                {
                    _workerRunning = false;
                    return;
                }
                var item = Queue.Dequeue();
                url = item.Url;
                json = item.Json;
            }
            PostJson(url, json);
        }
    }

    private static void PostJson(string url, string json)
    {
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(json)) return;
        // HttpWebRequest + Proxy=null: Unity HttpClient uses the WinHTTP/IE proxy and
        // fails loopback HTTP with "An error occurred while sending the request".
        // Do not rewrite http→https — LeaderBot relay is plain HTTP.
        // One POST at a time so SyncAll chunks do not lock the same MySQL rows.
        try
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json; charset=utf-8";
            request.Proxy = null;
            request.KeepAlive = false;
            request.Timeout = 20000;
            request.ReadWriteTimeout = 20000;
            request.AutomaticDecompression = DecompressionMethods.None;
            request.ServicePoint.Expect100Continue = false;

            var bytes = Encoding.UTF8.GetBytes(json);
            request.ContentLength = bytes.Length;
            using (var stream = request.GetRequestStream())
                stream.Write(bytes, 0, bytes.Length);

            using var resp = (HttpWebResponse)request.GetResponse();
            var code = (int)resp.StatusCode;
            if (code < 200 || code >= 300)
            {
                string body;
                using (var reader = new StreamReader(resp.GetResponseStream() ?? Stream.Null))
                    body = reader.ReadToEnd();
                if (!string.IsNullOrEmpty(body) && body.Length > 200)
                    body = body.Substring(0, 200);
                UnityEngine.Debug.LogWarning($"[Leaderboard] Relay POST {url} -> {code} {body}");
            }
        }
        catch (WebException wex)
        {
            var extra = "";
            if (wex.Response is HttpWebResponse httpResp)
            {
                try
                {
                    using var reader = new StreamReader(httpResp.GetResponseStream() ?? Stream.Null);
                    extra = reader.ReadToEnd();
                    if (!string.IsNullOrEmpty(extra) && extra.Length > 200)
                        extra = extra.Substring(0, 200);
                }
                catch { /* ignore body read failures */ }
            }
            UnityEngine.Debug.LogWarning($"[Leaderboard] Relay POST {url}: {FormatEx(wex)}{(string.IsNullOrEmpty(extra) ? "" : " " + extra)}");
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[Leaderboard] Relay POST {url}: {FormatEx(ex)}");
        }
    }

    private static string FormatEx(Exception ex)
    {
        var msg = ex.Message ?? ex.GetType().Name;
        for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
            msg += " -> " + (inner.Message ?? inner.GetType().Name);
        return msg;
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
    /// <summary>Which Rust instance sent this batch. Bot stores last local totals per ServerId and SUMs them.</summary>
    [JsonProperty("ServerId")] public string ServerId;
}
