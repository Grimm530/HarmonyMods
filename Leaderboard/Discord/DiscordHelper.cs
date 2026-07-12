using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Leaderboard.Discord;

public static class DiscordHelper
{
    public static void SendWebhook(string webhookUrl, string title, List<(string name, string value)> fields, int color = 0x55AAFF)
    {
        if (string.IsNullOrEmpty(webhookUrl)) return;
        var fieldsList = new List<object>();
        foreach (var f in fields ?? new List<(string, string)>())
            fieldsList.Add(new { name = f.name, value = f.value ?? "", inline = true });
        if (fieldsList.Count == 0) return;
        var payload = new { embeds = new[] { new { title, color, fields = fieldsList } } };
        var json = JsonConvert.SerializeObject(payload);
        PostJson(webhookUrl, json, _ => { });
    }

    public static void SendSimpleMessage(string webhookUrl, string content)
    {
        if (string.IsNullOrEmpty(webhookUrl)) return;
        var payload = new { content };
        PostJson(webhookUrl, JsonConvert.SerializeObject(payload), _ => { });
    }

    private static void PostJson(string url, string json, Action<long> onDone)
    {
        try
        {
            using var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
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
            UnityEngine.Debug.LogWarning($"[Leaderboard] Discord POST: {ex.Message}");
        }
    }
}
