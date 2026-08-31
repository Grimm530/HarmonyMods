using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace Rustcord;

/// <summary>
/// Harmony mod: Rustcord - game server monitoring through Discord. No Oxide.
/// Uses Discord webhooks for Game->Discord (one-way). Compatible with ticket-support-system-discord relay.
/// Config: HarmonyConfig/Rustcord.json or oxide/config/Rustcord.json
/// </summary>
public class RustcordMod : IHarmonyModHooks
{
    public static RustcordMod Instance { get; private set; }

    private static string _lastLogMessage;
    private static readonly object _sendLock = new();
    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "RustcordHarmony (https://grimmzone.com, 1.0)");
        return client;
    }

    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        Instance = this;
        RustcordConfig.LoadConfig();
        var apikey = RustcordConfig.Config?.General?.Apikey?.Trim();
        var hasKey = !string.IsNullOrEmpty(apikey);
        var mode = hasKey ? "Bot" : "Webhook";
        Log($"Rustcord Harmony mod loaded. ({mode} mode - Game->Discord)", force: true);
        if (!hasKey)
            Log("API Key (Bot Token) is empty - add token to HarmonyConfig/Rustcord.json 'General Settings' -> 'API Key (Bot Token)'", force: true);
        else
            Log($"API Key present. Channels with msg_chat: {CountChannelsForPerm("msg_chat")}", force: true);
    }

    private static int CountChannelsForPerm(string perm)
    {
        var cfg = RustcordConfig.Config;
        if (cfg?.Channels == null) return 0;
        var n = 0;
        foreach (var ch in cfg.Channels)
            if (ch.perms != null && ch.perms.Contains(perm)) n++;
        return n;
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        Instance = null;
    }

    internal static void Log(string message, bool force = false)
    {
        if (force || message != _lastLogMessage)
        {
            _lastLogMessage = message;
            Debug.LogWarning($"[Rustcord] {message}");
        }
    }

    /// <summary>Get webhook URL for a channel. Checks Webhooks dict and ChannelConfig.WebhookUrl.</summary>
    public static string GetWebhookForChannel(ulong channelId)
    {
        var cfg = RustcordConfig.Config;
        if (cfg?.Webhooks != null && cfg.Webhooks.TryGetValue(channelId.ToString(), out var url) && !string.IsNullOrWhiteSpace(url))
            return url.Trim();

        foreach (var ch in cfg?.Channels ?? new List<RustcordConfig.ChannelConfig>())
        {
            if (ch.Channelid == channelId && !string.IsNullOrWhiteSpace(ch.WebhookUrl))
                return ch.WebhookUrl.Trim();
        }
        return null;
    }

    /// <summary>Post a simple text message to Discord. Uses bot token + channel ID (Discord API), or webhook if configured. Format for relay: ":speech_left: SVR1 PlayerName: message"</summary>
    public static void PostToDiscord(string content, string permRequired = "msg_chat")
    {
        if (string.IsNullOrEmpty(content)) return;
        var cfg = RustcordConfig.Config;
        if (cfg == null || cfg.Channels == null) return;

        var botToken = cfg.General?.Apikey?.Trim();
        var useBot = !string.IsNullOrEmpty(botToken);

        foreach (var ch in cfg.Channels)
        {
            if (ch.perms == null || !ch.perms.Contains(permRequired)) continue;

            var webhook = GetWebhookForChannel(ch.Channelid);
            if (!string.IsNullOrEmpty(webhook))
            {
                PostToWebhook(webhook, content);
            }
            else if (useBot)
            {
                PostToChannel(ch.Channelid, content, botToken);
            }
        }
    }

    /// <summary>Post to a Discord channel via Bot API (channel ID). Runs async to avoid blocking game thread.</summary>
    public static void PostToChannel(ulong channelId, string content, string botToken)
    {
        if (channelId == 0 || string.IsNullOrEmpty(content) || string.IsNullOrEmpty(botToken)) return;

        var runner = ServerMgr.Instance;
        if (runner != null)
            runner.StartCoroutine(PostToChannelCoroutine(channelId, content, botToken));
    }

    private static IEnumerator PostToChannelCoroutine(ulong channelId, string content, string botToken)
    {
        var url = $"https://discord.com/api/v10/channels/{channelId}/messages";
        var payload = JsonConvert.SerializeObject(new { content });

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Authorization", "Bot " + botToken);

        Task<HttpResponseMessage> task;
        lock (_sendLock)
        {
            task = Http.SendAsync(request);
        }
        yield return new WaitUntil(() => task.IsCompleted);

        if (task.IsFaulted || task.IsCanceled)
        {
            Log($"Discord API post failed: {task.Exception?.GetBaseException()?.Message}");
            yield break;
        }
        var response = task.Result;
        if (!response.IsSuccessStatusCode)
        {
            var body = response.Content?.ReadAsStringAsync()?.Result ?? "";
            Log($"Discord API returned {(int)response.StatusCode}: {body?.Substring(0, Math.Min(150, body?.Length ?? 0))}");
        }
    }

    /// <summary>Post to a specific webhook URL. Runs async to avoid blocking game thread.</summary>
    public static void PostToWebhook(string webhookUrl, string content)
    {
        if (string.IsNullOrEmpty(webhookUrl) || string.IsNullOrEmpty(content)) return;

        var runner = ServerMgr.Instance;
        if (runner != null)
            runner.StartCoroutine(PostToWebhookCoroutine(webhookUrl, content));
    }

    private static IEnumerator PostToWebhookCoroutine(string webhookUrl, string content)
    {
        var payload = JsonConvert.SerializeObject(new { content });

        var request = new HttpRequestMessage(HttpMethod.Post, webhookUrl)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        Task<HttpResponseMessage> task;
        lock (_sendLock)
        {
            task = Http.SendAsync(request);
        }
        yield return new WaitUntil(() => task.IsCompleted);

        if (task.IsFaulted || task.IsCanceled)
        {
            Log($"Webhook post failed: {task.Exception?.GetBaseException()?.Message}");
            yield break;
        }
        var response = task.Result;
        if (!response.IsSuccessStatusCode)
        {
            var body = response.Content?.ReadAsStringAsync()?.Result ?? "";
            Log($"Webhook returned {(int)response.StatusCode}: {body?.Substring(0, Math.Min(100, body?.Length ?? 0))}");
        }
    }

    #region Format helpers (relay-compatible)

    /// <summary>Format chat message for relay: ":speech_left: SVR1 PlayerName: message"</summary>
    public static string FormatChat(string serverName, string playerName, string message)
    {
        var prefix = string.IsNullOrEmpty(serverName) ? "" : serverName.Trim() + " ";
        return $":speech_left: {prefix}{playerName}: {message}";
    }

    public static string FormatJoin(string serverName, string playerName)
    {
        var prefix = string.IsNullOrEmpty(serverName) ? "" : serverName.Trim() + " ";
        return $":white_check_mark: {prefix}{playerName} has connected!";
    }

    public static string FormatQuit(string serverName, string playerName, string reason)
    {
        var prefix = string.IsNullOrEmpty(serverName) ? "" : serverName.Trim() + " ";
        return $":x: {prefix}{playerName} has disconnected! ({reason})";
    }

    public static string FormatDeath(string serverName, string killer, string victim)
    {
        var prefix = string.IsNullOrEmpty(serverName) ? "" : serverName.Trim() + " ";
        return $":skull_crossbones: {prefix}{killer} killed {victim}.";
    }

    public static string FormatCrateDropped(string serverName)
    {
        var prefix = string.IsNullOrEmpty(serverName) ? "" : serverName.Trim() + " ";
        return $":helicopter: {prefix}A Chinook has delivered a crate.";
    }

    public static string FormatSupplyDrop(string serverName)
    {
        var prefix = string.IsNullOrEmpty(serverName) ? "" : serverName.Trim() + " ";
        return $":airplane: {prefix}A supply drop has landed.";
    }

    public static string ApplyFilter(string message)
    {
        var cfg = RustcordConfig.Config?.Filters;
        if (cfg?.FilterWords == null || cfg.FilterWords.Count == 0) return message;
        var result = message;
        var replacement = cfg.FilteredWord ?? "<censored>";
        foreach (var word in cfg.FilterWords)
        {
            if (string.IsNullOrEmpty(word)) continue;
            result = result.Replace(" " + word + " ", " " + replacement + " ")
                .Replace(word, replacement);
        }
        return result;
    }

    #endregion
}
