using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;

namespace Leaderboard.Storage;

internal static class LeaderboardJson
{
    public static readonly JsonSerializerSettings Settings = new()
    {
        Culture = CultureInfo.InvariantCulture,
        DateFormatHandling = DateFormatHandling.IsoDateFormat,
        DateTimeZoneHandling = DateTimeZoneHandling.Utc,
        Formatting = Formatting.Indented
    };
}

public class JsonLeaderboardStorage : ILeaderboardStorage
{
    private readonly string _basePath;
    private readonly object _lock = new();

    public JsonLeaderboardStorage(string dataFolder)
    {
        _basePath = Path.Combine(Environment.CurrentDirectory, dataFolder.Trim(), "Players");
        try
        {
            if (!Directory.Exists(_basePath))
                Directory.CreateDirectory(_basePath);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[Leaderboard] Json storage folder: {ex.Message}");
        }
    }

    private string FilePath(ulong userId) => Path.Combine(_basePath, userId + ".json");

    public void LoadPlayer(ulong userId, Action<PlayerStats> callback)
    {
        var path = FilePath(userId);
        try
        {
            if (!File.Exists(path))
            {
                callback?.Invoke(new PlayerStats(userId));
                return;
            }
            lock (_lock)
            {
                var json = File.ReadAllText(path);
                var stats = JsonConvert.DeserializeObject<PlayerStats>(json, LeaderboardJson.Settings);
                if (stats != null)
                {
                    stats.UserId = userId;
                    stats.IsOnline = false;
                    callback?.Invoke(stats);
                    return;
                }
            }
            PreserveCorrupt(path, userId, "deserialized empty");
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[Leaderboard] Load {userId}: {ex.Message}");
            PreserveCorrupt(path, userId, ex.Message);
        }
        callback?.Invoke(new PlayerStats(userId));
    }

    public void SavePlayer(PlayerStats stats)
    {
        if (stats == null) return;
        var path = FilePath(stats.UserId);
        try
        {
            lock (_lock)
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonConvert.SerializeObject(stats, LeaderboardJson.Settings);
                File.WriteAllText(path, json);
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[Leaderboard] Save {stats.UserId}: {ex.Message}");
        }
    }

    public void SaveAll(bool isUnload = false)
    {
        var mod = LeaderboardMod.Instance;
        if (mod == null) return;
        foreach (var kv in mod.GetAllStatsSnapshot())
        {
            if (isUnload)
                kv.Value.CommitOnlineSession();
            SavePlayer(kv.Value);
        }
    }

    public void Wipe()
    {
        try
        {
            if (!Directory.Exists(_basePath)) return;
            foreach (var f in Directory.GetFiles(_basePath, "*.json"))
            {
                try { File.Delete(f); } catch { }
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[Leaderboard] Wipe: {ex.Message}");
        }
    }

    /// <summary>Move a bad player file aside so one corrupt file is not overwritten and does not stop the rest.</summary>
    private static void PreserveCorrupt(string path, ulong userId, string reason)
    {
        try
        {
            if (!File.Exists(path)) return;
            var bad = path + ".corrupt";
            if (File.Exists(bad)) File.Delete(bad);
            File.Move(path, bad);
            UnityEngine.Debug.LogWarning($"[Leaderboard] Kept corrupt data for {userId} at {bad} ({reason})");
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[Leaderboard] Could not preserve corrupt {userId}: {ex.Message}");
        }
    }
}
