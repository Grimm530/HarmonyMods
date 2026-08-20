using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Durable wipe token written by WipeBetweenRestarts.ps1 / MapVoter when a real map/forced wipe runs.
/// Each mod stores the last token it processed; daily TimedExecute restarts do not change the token.
/// </summary>
internal static class WipeSignal
{
    public static string SignalPath =>
        Path.Combine(Environment.CurrentDirectory, "HarmonyConfig", "wipe_signal.json");

    public static string ReadToken()
    {
        try
        {
            if (!File.Exists(SignalPath)) return "";
            var jo = JObject.Parse(File.ReadAllText(SignalPath));
            return ((string)jo["token"] ?? "").Trim();
        }
        catch
        {
            return "";
        }
    }

    public static bool ShouldWipe(string stateFilePath)
    {
        var token = ReadToken();
        if (string.IsNullOrEmpty(token)) return false;
        try
        {
            if (File.Exists(stateFilePath)
                && string.Equals(File.ReadAllText(stateFilePath).Trim(), token, StringComparison.Ordinal))
                return false;
        }
        catch { }
        return true;
    }

    public static void MarkWiped(string stateFilePath)
    {
        var token = ReadToken();
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(stateFilePath)) return;
        try
        {
            var dir = Path.GetDirectoryName(stateFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(stateFilePath, token);
        }
        catch { }
    }

    public static void Write(DateTime wipeAt, int mapSeed, bool wasForcedWipe)
    {
        try
        {
            var at = wipeAt.Year > 2000 ? wipeAt : DateTime.Now;
            var token = at.ToString("yyyy-MM-ddTHH:mm:ss") + "|" + mapSeed + "|" + (wasForcedWipe ? "forced" : "map");
            var payload = new
            {
                token,
                wipeAt = at.ToString("yyyy-MM-ddTHH:mm:ss"),
                mapSeed,
                wasForcedWipe
            };
            var dir = Path.Combine(Environment.CurrentDirectory, "HarmonyConfig");
            Directory.CreateDirectory(dir);
            File.WriteAllText(SignalPath, JsonConvert.SerializeObject(payload, Formatting.Indented));
        }
        catch { }
    }
}
