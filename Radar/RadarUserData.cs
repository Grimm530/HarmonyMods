using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Radar;

/// <summary>
/// Per-user persisted data (e.g. UI position). Stored under HarmonyData/Radar/{userId}.json.
/// First-time users get defaults; when an admin moves the UI we save their preferred location.
/// </summary>
public static class RadarUserData
{
    [Serializable]
    public class UserData
    {
        [JsonProperty("UiAnchorMin")]
        public string UiAnchorMin { get; set; }

        [JsonProperty("UiAnchorMax")]
        public string UiAnchorMax { get; set; }
    }

    private static string GetServerRoot()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }

    /// <summary>Directory for per-user Radar data: {serverRoot}/HarmonyData/Radar</summary>
    public static string GetDataDirectory()
    {
        return Path.Combine(GetServerRoot(), "HarmonyData", "Radar");
    }

    private static string GetUserFilePath(ulong userId)
    {
        return Path.Combine(GetDataDirectory(), userId + ".json");
    }

    /// <summary>Load saved UI position for a user. Returns null if no file or first-time user.</summary>
    public static UserData Load(ulong userId)
    {
        try
        {
            var path = GetUserFilePath(userId);
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<UserData>(json);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning("[Radar] User data load error for " + userId + ": " + ex.Message);
            return null;
        }
    }

    /// <summary>Save user's preferred UI position. Creates HarmonyData/Radar if needed.</summary>
    public static void Save(ulong userId, string uiAnchorMin, string uiAnchorMax)
    {
        try
        {
            var dir = GetDataDirectory();
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var data = new UserData { UiAnchorMin = uiAnchorMin, UiAnchorMax = uiAnchorMax };
            var path = GetUserFilePath(userId);
            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError("[Radar] User data save error for " + userId + ": " + ex.Message);
        }
    }
}
