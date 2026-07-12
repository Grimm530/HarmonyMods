using System;
using System.Collections;
using System.Net.Http;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace TranslationAPI;

/// <summary>
/// Harmony mod: Translation API - web translation via Google/Microsoft/Yandex.
/// Used by ChatTranslator, Rustcord via Oxide bridge plugin (TranslationAPI.cs).
/// Config: HarmonyConfig/TranslationAPI.json (created on first load if missing)
/// </summary>
public class TranslationAPIMod : IHarmonyModHooks
{
    public static TranslationAPIMod Instance { get; private set; }

    private static readonly Regex GoogleRegex = new(@"\[\[\[""((?:\s|.)+?)"",""(?:\s|.)+?""");
    private static readonly Regex MicrosoftRegex = new("\"(.*)\"");

    private static string _lastLogMessage;

    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        Instance = this;
        TranslationAPIConfig.LoadConfig();
        Log("TranslationAPI Harmony mod loaded.", force: true);
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        Instance = null;
    }

    /// <summary>
    /// Translates text from one language to another. Call from Oxide bridge or via reflection.
    /// </summary>
    public static void Translate(string text, string to, string from, Action<string> callback)
    {
        if (Instance == null || callback == null)
        {
            callback?.Invoke(text ?? string.Empty);
            return;
        }

        var config = TranslationAPIConfig.Config;
        if (config == null)
        {
            callback(text);
            return;
        }

        var apiKey = config.ApiKey ?? string.Empty;
        var service = (config.Service ?? "google").ToLower();
        to = to.Contains("-") ? to.Split('-')[0].ToLower() : to.ToLower();
        from = from.Contains("-") ? from.Split('-')[0].ToLower() : from.ToLower();

        if (string.IsNullOrEmpty(apiKey) && service != "google")
        {
            Log("Invalid API key, please check that it is set and valid");
            callback(text);
            return;
        }

        var runner = ServerMgr.Instance;
        if (runner != null)
            runner.StartCoroutine(DoTranslateCoroutine(text, to, from, apiKey, service, callback));
        else
            callback(text);
    }

    private static IEnumerator DoTranslateCoroutine(string text, string to, string from, string apiKey, string service, Action<string> callback)
    {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(15);

        if (service == "google")
        {
            var url = string.IsNullOrEmpty(apiKey)
                ? $"https://translate.googleapis.com/translate_a/single?client=gtx&tl={to}&sl={from}&dt=t&q={Uri.EscapeDataString(text)}"
                : $"https://www.googleapis.com/language/translate/v2?key={apiKey}&target={to}&source={from}&q={Uri.EscapeDataString(text)}";

            var task = client.GetStringAsync(url);
            yield return new WaitUntil(() => task.IsCompleted);

            if (!task.IsCompleted || task.IsFaulted || task.IsCanceled || task.Result == null)
            {
                Log($"No valid response from Google: {task.Exception?.Message}");
                callback(text);
                yield break;
            }

            var response = task.Result;
            if (response == "[null,null,\"\"]")
            {
                Log("No valid response received from Google, try again later");
                callback(text);
                yield break;
            }

            ParseAndInvoke(response, 200, text, service, apiKey, callback);
            yield break;
        }

        if (service == "bing" || service == "microsoft")
        {
            var detectUrl = $"http://api.microsofttranslator.com/V2/Ajax.svc/Detect?appId={apiKey}&text={Uri.EscapeDataString(text)}";
            var detectTask = client.GetStringAsync(detectUrl);
            yield return new WaitUntil(() => detectTask.IsCompleted);

            if (!detectTask.IsCompleted || detectTask.IsFaulted || string.IsNullOrEmpty(detectTask.Result) || detectTask.Result.Contains("<html>"))
            {
                Log("No valid response from Microsoft Detect");
                callback(text);
                yield break;
            }

            var r = detectTask.Result;
            if (r.Contains("ArgumentException: Invalid appId"))
            {
                Log("Invalid API key for Microsoft");
                callback(text);
                yield break;
            }

            var detectedFrom = r.Trim('"');
            var translateUrl = $"http://api.microsofttranslator.com/V2/Ajax.svc/Translate?appId={apiKey}&to={to}&from={detectedFrom}&text={Uri.EscapeDataString(text)}";
            var transTask = client.GetStringAsync(translateUrl);
            yield return new WaitUntil(() => transTask.IsCompleted);

            if (!transTask.IsCompleted || transTask.IsFaulted || string.IsNullOrEmpty(transTask.Result) || transTask.Result.Contains("<html>"))
            {
                Log("No valid response from Microsoft Translate");
                callback(text);
                yield break;
            }

            ParseAndInvoke(transTask.Result, 200, text, service, apiKey, callback);
            yield break;
        }

        if (service == "yandex")
        {
            var detectUrl = $"https://translate.yandex.net/api/v1.5/tr.json/detect?key={apiKey}&hint={from}&text={Uri.EscapeDataString(text)}";
            var detectTask = client.GetStringAsync(detectUrl);
            yield return new WaitUntil(() => detectTask.IsCompleted);

            if (!detectTask.IsCompleted || detectTask.IsFaulted || string.IsNullOrEmpty(detectTask.Result))
            {
                Log("No valid response from Yandex Detect");
                callback(text);
                yield break;
            }

            try
            {
                var det = JObject.Parse(detectTask.Result);
                from = (string)det["lang"] ?? from;
            }
            catch
            {
                Log("Yandex detect parse error");
                callback(text);
                yield break;
            }

            var transUrl = $"https://translate.yandex.net/api/v1.5/tr.json/translate?key={apiKey}&lang={from}-{to}&text={Uri.EscapeDataString(text)}";
            var transTask = client.GetStringAsync(transUrl);
            yield return new WaitUntil(() => transTask.IsCompleted);

            if (!transTask.IsCompleted || transTask.IsFaulted || string.IsNullOrEmpty(transTask.Result))
            {
                Log("No valid response from Yandex Translate");
                callback(text);
                yield break;
            }

            if (transTask.Result.Contains("The specified translation direction is not supported"))
            {
                Log($"Invalid language code (to: {to}, from: {from})");
                callback(text);
                yield break;
            }

            ParseAndInvoke(transTask.Result, 200, text, service, apiKey, callback);
            yield break;
        }

        Log($"Translation service '{service}' is not valid");
        callback(text);
    }

    private static void ParseAndInvoke(string response, int code, string originalText, string service, string apiKey, Action<string> callback)
    {
        if (code != 200 || string.IsNullOrEmpty(response))
        {
            callback(originalText);
            return;
        }

        string translated = null;

        try
        {
            if (service == "google" && string.IsNullOrEmpty(apiKey))
                translated = GoogleRegex.Match(response).Groups[1].Value;
            else if (service == "google" && !string.IsNullOrEmpty(apiKey))
            {
                var data = JObject.Parse(response)?["data"]?["translations"];
                if (data is JArray arr && arr.Count > 0)
                    translated = (string)arr[0]?["translatedText"];
            }
            else if (service == "microsoft" || service == "bing")
                translated = MicrosoftRegex.Match(response).Groups[1].Value;
            else if (service == "yandex")
            {
                var textToken = JObject.Parse(response)?["text"];
                translated = textToken is JArray textArr && textArr.Count > 0 ? (string)textArr[0] : (string)textToken;
            }
        }
        catch (Exception ex)
        {
            Log($"Translation parse error: {ex.Message}");
        }

        callback(string.IsNullOrEmpty(translated) ? originalText : Regex.Unescape(translated));
    }

    internal static void Log(string message, bool force = false)
    {
        if (force || message != _lastLogMessage)
        {
            _lastLogMessage = message;
            UnityEngine.Debug.LogWarning($"[TranslationAPI] {message}");
        }
    }
}
