using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Leaderboard.Storage;

public class JsonLeaderboardStorage : ILeaderboardStorage
{
    private readonly string _basePath;
    private readonly object _lock = new();

    public JsonLeaderboardStorage(string dataFolder)
    {
        var folder = (dataFolder ?? "LeaderboardData").Trim();
        // InitStorage may already pass an absolute path under CurrentDirectory.
        _basePath = Path.IsPathRooted(folder)
            ? Path.Combine(folder, "Players")
            : Path.Combine(Environment.CurrentDirectory, folder, "Players");
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
                    if (stats.StatsStorage == null)
                        stats.StatsStorage = new Dictionary<LootType, Dictionary<string, float>>();
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

    public List<PlayerStats> LoadAllPlayers()
    {
        var list = new List<PlayerStats>();
        try
        {
            if (!Directory.Exists(_basePath)) return list;
            string[] files;
            lock (_lock)
                files = Directory.GetFiles(_basePath, "*.json");
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    string json;
                    lock (_lock)
                        json = File.ReadAllText(files[i]);
                    var stats = JsonConvert.DeserializeObject<PlayerStats>(json);
                    if (stats == null) continue;
                    if (stats.UserId == 0)
                    {
                        var name = Path.GetFileNameWithoutExtension(files[i]);
                        if (ulong.TryParse(name, out var uid))
                            stats.UserId = uid;
                    }
                    if (stats.UserId == 0) continue;
                    if (stats.StatsStorage == null)
                        stats.StatsStorage = new Dictionary<LootType, Dictionary<string, float>>();
                    list.Add(stats);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[Leaderboard] LoadAll {Path.GetFileName(files[i])}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[Leaderboard] LoadAllPlayers: {ex.Message}");
        }
        return list;
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
