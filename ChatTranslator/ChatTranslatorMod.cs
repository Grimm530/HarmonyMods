using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace ChatTranslator;

/// <summary>
/// Harmony mod: ChatTranslator - translates chat messages to each player's language preference.
/// No Oxide. Requires TranslationAPI Harmony mod. Config: HarmonyConfig/ChatTranslator.json (created on first load if missing)
/// </summary>
public class ChatTranslatorMod : IHarmonyModHooks
{
    public static ChatTranslatorMod Instance { get; private set; }

    private static string _lastLogMessage;
    private static readonly object LangLock = new();
    private static Dictionary<string, string> _playerLanguages = new();

    private const string LangFile = "HarmonyConfig/ChatTranslator_languages.json";

    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        Instance = this;
        ChatTranslatorConfig.LoadConfig();
        LoadLanguages();
        Log("ChatTranslator Harmony mod loaded.", force: true);
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        SaveLanguages();
        Instance = null;
    }

    internal static void Log(string message, bool force = false)
    {
        if (force || message != _lastLogMessage)
        {
            _lastLogMessage = message;
            Debug.LogWarning($"[ChatTranslator] {message}");
        }
    }

    #region Language Storage

    public static string GetLanguage(string playerId)
    {
        if (string.IsNullOrEmpty(playerId)) return null;
        string configured = null;
        lock (LangLock)
        {
            _playerLanguages.TryGetValue(playerId, out configured);
        }
        if (!string.IsNullOrEmpty(configured))
            return configured;

        // Oxide parity: when no explicit /lang override exists, fall back to the player's client language.
        return GetClientLanguage(playerId);
    }

    public static void SetLanguage(string playerId, string langCode)
    {
        if (string.IsNullOrEmpty(playerId)) return;
        langCode = string.IsNullOrWhiteSpace(langCode) ? "en" : langCode.Split('-')[0].ToLower();
        try
        {
            _ = CultureInfo.GetCultureInfo(langCode);
        }
        catch
        {
            langCode = "en";
        }

        lock (LangLock)
        {
            _playerLanguages[playerId] = langCode;
        }
        SaveLanguages();
    }

    private static void LoadLanguages()
    {
        var serverRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var path = Path.Combine(serverRoot, LangFile);
        if (!File.Exists(path)) return;
        try
        {
            var json = File.ReadAllText(path);
            var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            if (dict != null)
            {
                lock (LangLock)
                {
                    _playerLanguages = dict;
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to load languages: {ex.Message}", force: true);
        }
    }

    private static void SaveLanguages()
    {
        var serverRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var dir = Path.GetDirectoryName(Path.Combine(serverRoot, LangFile));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        var path = Path.Combine(serverRoot, LangFile);
        try
        {
            Dictionary<string, string> copy;
            lock (LangLock)
            {
                copy = new Dictionary<string, string>(_playerLanguages);
            }
            File.WriteAllText(path, JsonConvert.SerializeObject(copy, Formatting.Indented));
        }
        catch (Exception ex)
        {
            Log($"Failed to save languages: {ex.Message}", force: true);
        }
    }

    #endregion

    #region Translation

    private static string GetClientLanguage(string playerId)
    {
        if (!ulong.TryParse(playerId, out var userId))
            return null;

        var player = BasePlayer.FindByID(userId) ?? BasePlayer.FindSleeping(userId);
        var raw = player?.net?.connection?.info?.GetString("global.language", null);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // Normalize "ru-RU" -> "ru" for translation provider compatibility.
        var code = raw.Split('-')[0].Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(code))
            return null;
        try
        {
            _ = CultureInfo.GetCultureInfo(code);
            return code;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Check if TranslationAPI mod is available.</summary>
    public static bool IsTranslationAPIAvailable()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var t = asm.GetType("TranslationAPI.TranslationAPIMod");
                if (t != null)
                {
                    var inst = t.GetProperty("Instance")?.GetValue(null);
                    return inst != null;
                }
            }
            catch { }
        }
        return false;
    }

    /// <summary>Translate text via TranslationAPI. Fallback to original if unavailable.</summary>
    public static void Translate(string message, string targetId, string senderId, Action<string> callback)
    {
        var apiType = Type.GetType("TranslationAPI.TranslationAPIMod");
        if (apiType == null)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                apiType = asm.GetType("TranslationAPI.TranslationAPIMod");
                if (apiType != null) break;
            }
        }

        if (apiType == null)
        {
            callback?.Invoke(message ?? string.Empty);
            return;
        }

        var config = ChatTranslatorConfig.Config;
        var langTo = config?.ForceServerDefault == true
            ? ChatTranslatorConfig.GetServerLanguage()
            : (GetLanguage(targetId) ?? "en");
        var langFrom = GetLanguage(senderId) ?? "auto";

        if (config?.SkipSameLanguage == true && langFrom != "auto" &&
            string.Equals(langTo, langFrom, StringComparison.OrdinalIgnoreCase))
        {
            callback?.Invoke(message ?? string.Empty);
            return;
        }

        var translateMethod = apiType.GetMethod("Translate",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (translateMethod == null)
        {
            callback?.Invoke(message ?? string.Empty);
            return;
        }

        try
        {
            translateMethod.Invoke(null, new object[] { message ?? string.Empty, langTo, langFrom, callback });
        }
        catch (Exception ex)
        {
            Log($"Translation error: {ex.Message}", force: true);
            callback?.Invoke(message ?? string.Empty);
        }
    }

    #endregion
}
