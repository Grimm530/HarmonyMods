using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BetterChatHarmony
{
    public static class BetterChatConfig
    {
        public static Root Config { get; private set; }

        private static string _path;

        public class Root
        {
            [JsonProperty("Maximal Titles")]
            public int MaxTitles { get; set; } = 3;

            [JsonProperty("Maximal Characters Per Message")]
            public int MaxMessageLength { get; set; } = 128;

            [JsonProperty("Reverse Title Order")]
            public bool ReverseTitleOrder { get; set; } = false;

            [JsonProperty("Coloured Chat")]
            public ColouredSettings Coloured { get; set; } = new ColouredSettings();

            [JsonProperty("Groups")]
            public List<ChatGroup> Groups { get; set; } = new List<ChatGroup>();
        }

        public class ColouredSettings
        {
            [JsonProperty("Player Inactivity Data Removal (days)")]
            public int InactivityRemovalTime { get; set; } = 7;

            [JsonProperty("Rainbow Colours")]
            public string[] RainbowColours { get; set; } =
                { "#ff0000", "#ffa500", "#ffff94", "#008000", "#0000ff", "#4b0082", "#ee82ee" };

            [JsonProperty("Blocked Characters")]
            public string[] BlockedValues { get; set; } = { "{", "}", "size" };

            [JsonProperty("Name colour commands")]
            public string[] NameColourCommands { get; set; } = { "colour", "color" };

            [JsonProperty("Name colour commands (Help)")]
            public string[] NameColoursCommands { get; set; } = { "colours", "colors" };

            [JsonProperty("Name show colour permission")]
            public string NamePermShow { get; set; } = "colouredchat.name.show";

            [JsonProperty("Name use permission")]
            public string NamePermUse { get; set; } = "colouredchat.name.use";

            [JsonProperty("Name use gradient permission")]
            public string NamePermGradient { get; set; } = "colouredchat.name.gradient";

            [JsonProperty("Name default rainbow name permission")]
            public string NamePermRainbow { get; set; } = "colouredchat.name.rainbow";

            [JsonProperty("Name bypass restrictions permission")]
            public string NamePermBypass { get; set; } = "colouredchat.name.bypass";

            [JsonProperty("Name set others colour permission")]
            public string NamePermSetOthers { get; set; } = "colouredchat.name.setothers";

            [JsonProperty("Name get random colour permission")]
            public string NamePermRandomColour { get; set; } = "colouredchat.name.random";

            [JsonProperty("Name use blacklist")]
            public bool NameUseBlacklist { get; set; } = true;

            [JsonProperty("Name blocked colour hex")]
            public List<string> NameBlockColoursHex { get; set; } = new List<string> { "#000000" };

            [JsonProperty("Name blocked colours range hex")]
            public List<ColourRange> NameBlacklistedRangeColoursHex { get; set; } =
                new List<ColourRange> { new ColourRange("#000000", "#000000") };

            [JsonProperty("Name use whitelist")]
            public bool NameUseWhitelist { get; set; } = false;

            [JsonProperty("Name whitelisted colours hex")]
            public List<string> NameWhitelistedColoursHex { get; set; } = new List<string> { "#000000" };

            [JsonProperty("Name whitelisted colour range hex")]
            public List<ColourRange> NameWhitelistedRangeColoursHex { get; set; } =
                new List<ColourRange> { new ColourRange("#000000", "#FFFFFF") };

            [JsonProperty("Message colour commands")]
            public string[] MessageColourCommands { get; set; } = { "mcolour", "mcolor" };

            [JsonProperty("Message colour commands (Help)")]
            public string[] MessageColoursCommands { get; set; } = { "mcolours", "mcolors" };

            [JsonProperty("Message show colour permission")]
            public string MessagePermShow { get; set; } = "colouredchat.message.show";

            [JsonProperty("Message use permission")]
            public string MessagePermUse { get; set; } = "colouredchat.message.use";

            [JsonProperty("Message use gradient permission")]
            public string MessagePermGradient { get; set; } = "colouredchat.message.gradient";

            [JsonProperty("Message default rainbow name permission")]
            public string MessagePermRainbow { get; set; } = "colouredchat.message.rainbow";

            [JsonProperty("Message bypass restrictions permission")]
            public string MessagePermBypass { get; set; } = "colouredchat.message.bypass";

            [JsonProperty("Message set others colour permission")]
            public string MessagePermSetOthers { get; set; } = "colouredchat.message.setothers";

            [JsonProperty("Message get random colour permission")]
            public string MessagePermRandomColour { get; set; } = "colouredchat.message.random";

            [JsonProperty("Message use blacklist")]
            public bool MessageUseBlacklist { get; set; } = true;

            [JsonProperty("Message blocked colours hex")]
            public List<string> MessageBlockColoursHex { get; set; } = new List<string> { "#000000" };

            [JsonProperty("Message blocked colour range hex")]
            public List<ColourRange> MessageBlacklistedRangeColoursHex { get; set; } =
                new List<ColourRange> { new ColourRange("#000000", "#000000") };

            [JsonProperty("Message use whitelist")]
            public bool MessageUseWhitelist { get; set; } = false;

            [JsonProperty("Message whitelisted colours hex")]
            public List<string> MessageWhitelistedColoursHex { get; set; } = new List<string> { "#000000" };

            [JsonProperty("Message whitelisted colour range hex")]
            public List<ColourRange> MessageWhitelistedRangeColoursHex { get; set; } =
                new List<ColourRange> { new ColourRange("#000000", "#FFFFFF") };
        }

        public class ColourRange
        {
            [JsonProperty("From")]
            public string From { get; set; }

            [JsonProperty("To")]
            public string To { get; set; }

            public ColourRange() { }

            public ColourRange(string from, string to)
            {
                From = from;
                To = to;
            }
        }

        public static void Load(string serverRoot)
        {
            _path = Path.Combine(serverRoot, "HarmonyConfig", "BetterChat.json");
            Directory.CreateDirectory(Path.GetDirectoryName(_path));

            Root root = null;
            if (File.Exists(_path))
            {
                try
                {
                    var raw = File.ReadAllText(_path);
                    var token = JToken.Parse(raw);
                    if (token is JArray arr)
                    {
                        root = new Root { Groups = arr.ToObject<List<ChatGroup>>() ?? new List<ChatGroup>() };
                        MergeOxideSettings(serverRoot, root);
                    }
                    else
                    {
                        root = token.ToObject<Root>();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[BetterChat] Failed to read HarmonyConfig/BetterChat.json: " + ex.Message);
                }
            }

            if (root == null)
            {
                root = new Root();
                MergeOxideSettings(serverRoot, root);
            }

            if (root.Coloured == null)
                root.Coloured = new ColouredSettings();
            if (root.Groups == null)
                root.Groups = new List<ChatGroup>();
            if (root.Groups.Count == 0)
                root.Groups.Add(new ChatGroup("default"));

            foreach (var g in root.Groups)
            {
                if (g == null) continue;
                if (g.Title == null) g.Title = new ChatGroup.TitleSettings(g.GroupName);
                if (g.Username == null) g.Username = new ChatGroup.UsernameSettings();
                if (g.Message == null) g.Message = new ChatGroup.MessageSettings();
                if (g.Format == null) g.Format = new ChatGroup.FormatSettings();
            }

            EnsureBuiltinGroups(root);
            Config = root;
            Save();
        }

        private static ChatGroup FindGroup(Root root, string name)
        {
            if (root?.Groups == null || string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < root.Groups.Count; i++)
            {
                var g = root.Groups[i];
                if (g != null && string.Equals(g.GroupName, name, StringComparison.OrdinalIgnoreCase))
                    return g;
            }
            return null;
        }

        /// <summary>Keep verified (and similar) even if a reload saved an older in-memory config over the file.</summary>
        private static void EnsureBuiltinGroups(Root root)
        {
            var verified = FindGroup(root, "verified");
            if (verified == null)
            {
                verified = new ChatGroup("verified")
                {
                    Priority = 50,
                    Title =
                    {
                        Text = "√",
                        Color = "#00FF66",
                        Size = 18,
                        Hidden = false,
                        HiddenIfNotPrimary = false,
                        AttachToUsername = true,
                        Bold = true
                    }
                };
                root.Groups.Add(verified);
                Debug.Log("[BetterChat] Restored missing group 'verified' (bold green √ before name).");
            }
            else if (verified.Title != null)
            {
                if (verified.Title.Text == "✅" || verified.Title.Text == "✓" ||
                    string.IsNullOrWhiteSpace(verified.Title.Text) ||
                    string.Equals(verified.Title.Text, "[verified]", StringComparison.OrdinalIgnoreCase))
                    verified.Title.Text = "√";
                verified.Title.AttachToUsername = true;
                verified.Title.Bold = true;
                if (string.IsNullOrEmpty(verified.Title.Color) || verified.Title.Color == "#55aaff" ||
                    verified.Title.Color == "#00C853")
                    verified.Title.Color = "#00FF66";
                verified.Title.Hidden = false;
                verified.Title.HiddenIfNotPrimary = false;
            }
        }

        public static void Save()
        {
            if (Config == null || string.IsNullOrEmpty(_path)) return;
            try
            {
                File.WriteAllText(_path, JsonConvert.SerializeObject(Config, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BetterChat] Failed to save config: " + ex.Message);
            }
        }

        private static void MergeOxideSettings(string serverRoot, Root root)
        {
            TryMergeFile(Path.Combine(serverRoot, "oxide", "config", "BetterChat.json"), jo =>
            {
                if (jo["Maximal Titles"] != null) root.MaxTitles = jo["Maximal Titles"].Value<int>();
                if (jo["Maximal Characters Per Message"] != null)
                    root.MaxMessageLength = jo["Maximal Characters Per Message"].Value<int>();
                if (jo["Reverse Title Order"] != null)
                    root.ReverseTitleOrder = jo["Reverse Title Order"].Value<bool>();
            });

            TryMergeFile(Path.Combine(serverRoot, "oxide", "config", "ColouredChat.json"), jo =>
            {
                try
                {
                    var coloured = jo.ToObject<ColouredSettings>();
                    if (coloured != null) root.Coloured = coloured;
                }
                catch { }
            });
        }

        private static void TryMergeFile(string path, Action<JObject> apply)
        {
            if (!File.Exists(path)) return;
            try
            {
                var jo = JObject.Parse(File.ReadAllText(path));
                apply(jo);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BetterChat] Oxide config merge " + path + ": " + ex.Message);
            }
        }
    }
}
