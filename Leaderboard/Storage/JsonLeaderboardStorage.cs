using System;
using System.IO;
using Newtonsoft.Json;

namespace Leaderboard.Storage;

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
                var stats = JsonConvert.DeserializeObject<PlayerStats>(json);
                if (stats != null)
                {
                    stats.UserId = userId; // ensure id is set
                    callback?.Invoke(stats);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[Leaderboard] Load {userId}: {ex.Message}");
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
                var json = JsonConvert.SerializeObject(stats, Formatting.Indented);
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
            if (isUnload && kv.Value.IsOnline)
            {
                var session = (DateTime.UtcNow - kv.Value.ConnectTime).TotalSeconds;
                if (session > 0)
                    kv.Value.TotalPlayTime += session;
                kv.Value.DisconnectTime = DateTime.UtcNow;
                kv.Value.ConnectTime = DateTime.UtcNow;
                kv.Value.IsOnline = false;
            }
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
}
