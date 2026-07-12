using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace DiscordLinks;

/// <summary>
/// Harmony mod for linking Discord accounts to Steam (game) accounts.
/// Used by MapVoter when "Only Authenticated users can vote through discord" is enabled.
/// - In-game: /link → generates code, player uses /link &lt;code&gt; in Discord
/// - Bot sends RCON: discordlink_claim &lt;code&gt; &lt;discordId&gt;
/// Config: HarmonyConfig/DiscordLinks.json
/// Data: HarmonyData/DiscordLinks/links.json
/// </summary>
public class DiscordLinksMod : IHarmonyModHooks
{
    public static DiscordLinksMod Instance { get; private set; }

    private static readonly string ConfigPath = "HarmonyConfig/DiscordLinks.json";
    private static readonly string DataPath = "HarmonyData/DiscordLinks/links.json";
    private readonly Dictionary<string, PendingCode> _pendingCodes = new();
    private static readonly object _lock = new();
    private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private ConsoleSystem.Command _discordLinkClaimCommand;
    private DiscordLinksConfig _config;

    private class PendingCode
    {
        public ulong SteamId;
        public string SteamName;
        public DateTime Expires;
    }

    private class LinkEntry
    {
        [JsonProperty("discordId")] public string DiscordId { get; set; }
        [JsonProperty("steamId")] public string SteamId { get; set; }
        [JsonProperty("steamName")] public string SteamName { get; set; }
        [JsonProperty("discordName")] public string DiscordName { get; set; }
        [JsonProperty("linkedAt")] public string LinkedAt { get; set; }
    }

    private class LinksData
    {
        [JsonProperty("links")] public List<LinkEntry> Links { get; set; } = new();
    }

    private class DiscordLinksConfig
    {
        [JsonProperty("EnableDiscordLink")] public bool EnableDiscordLink { get; set; } = true;
        [JsonProperty("LogLinks")] public bool LogLinks { get; set; } = true;
        [JsonProperty("CodeExpiryMinutes")] public int CodeExpiryMinutes { get; set; } = 5;
    }

    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        Instance = this;
        LoadConfig();
        EnsureDataFile();
        RegisterCommands();
        UnityEngine.Debug.Log("[DiscordLinks] Loaded. Use /link in-game to get a code, then /link <code> in Discord.");
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        UnregisterCommands();
        Instance = null;
    }

    private static string GetServerRoot()
    {
        var dp = Application.dataPath ?? "";
        return string.IsNullOrEmpty(dp) ? "." : Path.GetFullPath(Path.Combine(dp, ".."));
    }

    private void LoadConfig()
    {
        var root = GetServerRoot();
        var path = Path.Combine(root, ConfigPath);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                _config = JsonConvert.DeserializeObject<DiscordLinksConfig>(json);
            }
            catch { }
        }

        if (_config == null)
        {
            _config = new DiscordLinksConfig();
            try
            {
                File.WriteAllText(path, JsonConvert.SerializeObject(_config, Formatting.Indented));
                UnityEngine.Debug.Log($"[DiscordLinks] Created default config at {ConfigPath}");
            }
            catch (Exception ex) { UnityEngine.Debug.Log("[DiscordLinks] Could not write config: " + ex.Message); }
        }
    }

    private void EnsureDataFile()
    {
        var root = GetServerRoot();
        var path = Path.Combine(root, DataPath);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        if (!File.Exists(path))
        {
            try
            {
                File.WriteAllText(path, JsonConvert.SerializeObject(new LinksData(), Formatting.Indented));
                UnityEngine.Debug.Log($"[DiscordLinks] Created data file at {DataPath}");
            }
            catch (Exception ex) { UnityEngine.Debug.Log("[DiscordLinks] Could not create data file: " + ex.Message); }
        }
    }

    private void RegisterCommands()
    {
        try
        {
            _discordLinkClaimCommand = new ConsoleSystem.Command
            {
                Name = "discordlink_claim",
                FullName = "global.discordlink_claim",
                Variable = true,
                ServerAdmin = false,
                Call = arg => HandleClaimCommand(arg)
            };
            ConsoleSystem.Index.Server.Dict["global.discordlink_claim"] = _discordLinkClaimCommand;
            if (ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict["discordlink_claim"] = _discordLinkClaimCommand;
        }
        catch (Exception ex) { UnityEngine.Debug.Log("[DiscordLinks] Command reg failed: " + ex.Message); }
    }

    private void UnregisterCommands()
    {
        try
        {
            if (_discordLinkClaimCommand != null)
            {
                ConsoleSystem.Index.Server.Dict?.Remove("global.discordlink_claim");
                ConsoleSystem.Index.Server.GlobalDict?.Remove("discordlink_claim");
                _discordLinkClaimCommand = null;
            }
        }
        catch { }
    }

    private void HandleClaimCommand(ConsoleSystem.Arg arg)
    {
        if (arg?.Args == null || arg.Args.Length < 2)
        {
            arg?.ReplyWith("USAGE: discordlink_claim <code> <discordId>");
            return;
        }
        string code = arg.Args.ArgAt(0).Trim().ToUpperInvariant();
        string discordId = arg.Args.ArgAt(1).Trim();
        string discordName = arg.Args.Length > 2 ? arg.Args.ArgAt(2).Trim() : "";

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(discordId))
        {
            arg.ReplyWith("INVALID");
            return;
        }

        int expiry = _config?.CodeExpiryMinutes ?? 5;
        PendingCode pending;
        lock (_lock)
        {
            PruneExpired();
            if (!_pendingCodes.TryGetValue(code, out pending))
            {
                arg.ReplyWith("INVALID_CODE");
                return;
            }
            if (DateTime.UtcNow > pending.Expires)
            {
                _pendingCodes.Remove(code);
                arg.ReplyWith("EXPIRED");
                return;
            }
            _pendingCodes.Remove(code);
        }

        var path = Path.Combine(GetServerRoot(), DataPath);
        LinksData data;
        try
        {
            var json = File.ReadAllText(path);
            data = JsonConvert.DeserializeObject<LinksData>(json) ?? new LinksData();
        }
        catch
        {
            data = new LinksData();
        }

        if (data.Links == null) data.Links = new List<LinkEntry>();

        for (int i = data.Links.Count - 1; i >= 0; i--)
        {
            var l = data.Links[i];
            if (l.DiscordId == discordId || l.SteamId == pending.SteamId.ToString())
                data.Links.RemoveAt(i);
        }
        data.Links.Add(new LinkEntry
        {
            DiscordId = discordId,
            SteamId = pending.SteamId.ToString(),
            SteamName = pending.SteamName ?? "",
            DiscordName = discordName,
            LinkedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        });

        try
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented));
        }
        catch (Exception ex)
        {
            arg.ReplyWith("ERROR: " + ex.Message);
            return;
        }

        UnityEngine.Debug.Log($"[DiscordLinks] CLAIM handshake: code {code} → Discord {discordId} ↔ Steam {pending.SteamId} ({pending.SteamName})");
        if (_config?.LogLinks ?? true)
            UnityEngine.Debug.Log($"[DiscordLinks] Linked Discord {discordId} to Steam {pending.SteamId} ({pending.SteamName})");
        arg.ReplyWith("OK");
    }

    /// <summary>Called from chat patch when player types /link</summary>
    internal bool OnChatLink(BasePlayer player, out string message)
    {
        message = null;
        if (player == null) return false;
        if (_config != null && !_config.EnableDiscordLink) return false;

        int expiry = _config?.CodeExpiryMinutes ?? 5;
        string code = GenerateCode();
        lock (_lock)
        {
            PruneExpired();
            _pendingCodes[code] = new PendingCode
            {
                SteamId = player.userID,
                SteamName = player.displayName ?? "",
                Expires = DateTime.UtcNow.AddMinutes(expiry)
            };
        }

        message = $"Your link code is: {code}. It expires in {expiry} minutes. Go to Discord and type /link {code}";
        return true;
    }

    private static string GenerateCode()
    {
        var r = new System.Random();
        var arr = new char[6];
        for (int i = 0; i < 6; i++)
            arr[i] = CodeChars[r.Next(CodeChars.Length)];
        return new string(arr);
    }

    private void PruneExpired()
    {
        var now = DateTime.UtcNow;
        var toRemove = new List<string>();
        foreach (var kv in _pendingCodes)
        {
            if (now > kv.Value.Expires)
                toRemove.Add(kv.Key);
        }
        foreach (var k in toRemove)
            _pendingCodes.Remove(k);
    }
}
