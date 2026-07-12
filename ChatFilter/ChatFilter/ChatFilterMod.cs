using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using ConVar;
using Network;

namespace ChatFilter
{
    public class ChatFilterMod : IHarmonyModHooks
    {
        public static ChatFilterMod Instance { get; private set; }

        private static readonly Regex SpecialCharRegex = new Regex(
            @"^[a-zA-Z0-9.,-_ \[\\]\\'~`?<>;:/!()*&%$#@=+|{}\""-]*$",
            RegexOptions.Compiled);

        private static readonly Dictionary<string, string> LeetTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"}{", "h"}, {"|-|", "h"}, {"]-[", "h"}, {"/-/", "h"},
            {"|{", "k"}, {"/\\/\\", "m"}, {"|\\|", "n"}, {"/\\/", "n"},
            {"()", "o"}, {"[]", "o"}, {"vv", "w"}, {"\\/\\/", "w"},
            {"><", "x"}, {"2", "z"}, {"4", "a"}, {"@", "a"}, {"8", "b"},
            {"ß", "b"}, {"(", "c"}, {"<", "c"}, {"{", "c"}, {"3", "e"},
            {"€", "e"}, {"6", "g"}, {"9", "g"}, {"&", "g"}, {"#", "h"},
            {"$", "s"}, {"7", "t"}, {"|", "l"}, {"1", "i"}, {"!", "i"}, {"0", "o"}
        };

        private Dictionary<string, OffenseData> _playerOffenses = new Dictionary<string, OffenseData>();
        private const string DataFileName = "ChatFilter_Offenses";

        /// <summary>When set, Arg.GetString(0, ...) returns this until cleared (so ChatTranslator/sayImpl see filtered message). Cleared in sayImpl Postfix.</summary>
        [ThreadStatic] private static string _filterOverrideForGetString;
        public static void SetFilterOverride(string filteredMessage) => _filterOverrideForGetString = filteredMessage;
        public static string GetFilterOverride() => _filterOverrideForGetString;
        public static void ClearFilterOverride() => _filterOverrideForGetString = null;

        public class OffenseData
        {
            public int Offenses { get; set; } = 1;
            public DateTime LastOffense { get; set; } = DateTime.UtcNow;
        }

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            ChatFilterConfig.LoadConfig();
            LoadData();
            UnityEngine.Debug.Log("[ChatFilter] Harmony mod loaded. Config: HarmonyConfig/ChatFilter.json");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            SaveData();
            Instance = null;
        }

        public static bool ShouldExclude(BasePlayer player)
        {
            var cfg = ChatFilterConfig.Config;
            if (cfg == null) return false;
            if (cfg.ExcludeAdmins && (player?.IsAdmin == true || player?.IsDeveloper == true))
                return true;
            // Treat DeveloperListOverride list as developers (orange-name mod) so they're excluded and chat color stays correct
            if (cfg.ExcludeAdmins && player != null && IsOverrideDeveloper(player.UserIDString))
                return true;
            if (cfg.ExcludeSteamIds != null && cfg.ExcludeSteamIds.Count > 0 &&
                cfg.ExcludeSteamIds.Contains(player?.UserIDString ?? ""))
                return true;
            return false;
        }

        /// <summary>Returns true if player is in DeveloperListOverride config (no reference required).</summary>
        private static bool IsOverrideDeveloper(string userIDString)
        {
            if (string.IsNullOrEmpty(userIDString)) return false;
            try
            {
                var type = Type.GetType("DeveloperListOverride.DeveloperListOverrideConfig, DeveloperListOverride");
                if (type == null) return false;
                var method = type.GetMethod("IsOverrideDeveloper", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new[] { typeof(string) }, null);
                if (method == null) return false;
                return (bool)method.Invoke(null, new object[] { userIDString });
            }
            catch { return false; }
        }

        public static bool HasSpecialChars(string input)
        {
            if (string.IsNullOrEmpty(input)) return false;
            return !SpecialCharRegex.IsMatch(input);
        }

        public string FilterMessage(BasePlayer player, string message, out bool hadMatch, out bool filterAll)
        {
            hadMatch = false;
            filterAll = false;
            var cfg = ChatFilterConfig.Config;
            if (cfg == null || !cfg.WordFilterEnabled) return message;

            var filtered = message;
            var words = message.Split(new[] { ' ' }, StringSplitOptions.None);

            foreach (var word in words)
            {
                if (IsWhitelisted(word)) continue;

                var decoded = TranslateLeet(word);
                var normalized = Regex.Replace(decoded, @"[^\w]", "").ToLowerInvariant();
                bool wordMatched = false;

                // Prefix list: word starts with phrase (catches Nigger123, Niggerrrrr, Nigherxyz, etc.)
                foreach (var prefix in cfg.WordFilterPrefixPhrases ?? new List<string>())
                {
                    if (string.IsNullOrEmpty(prefix)) continue;
                    if (!normalized.StartsWith(prefix.ToLowerInvariant())) continue;

                    hadMatch = true;
                    wordMatched = true;
                    filtered = filtered.Replace(word, "");
                    if (cfg.FilterAll)
                    {
                        filterAll = true;
                        return "";
                    }
                    break;
                }
                if (wordMatched) continue;

                // Exact list: whole-word match by default.
                // Optional legacy behavior can still match partial substrings when explicitly enabled.
                foreach (var banned in cfg.WordFilterPhrases ?? new List<string>())
                {
                    if (string.IsNullOrEmpty(banned)) continue;
                    var banLower = Regex.Replace(banned, @"[^\w]", "").ToLowerInvariant();
                    if (string.IsNullOrEmpty(banLower)) continue;

                    bool isExactMatch = normalized == banLower;
                    bool isPartialMatch = cfg.WordFilterAllowPartialMatchInWords &&
                                          banLower.Length >= 4 &&
                                          normalized.Contains(banLower);
                    bool match = isExactMatch || isPartialMatch;
                    if (!match) continue;

                    hadMatch = true;
                    filtered = filtered.Replace(word, "");
                    if (cfg.FilterAll)
                    {
                        filterAll = true;
                        return "";
                    }
                    break;
                }
            }

            // Collapse multiple spaces and trim so "yo  im  i promise" → "yo im i promise"
            if (hadMatch)
            {
                filtered = Regex.Replace(filtered, @"\s+", " ");
                filtered = filtered.Trim();
            }
            return filtered;
        }

        private bool IsWhitelisted(string word)
        {
            var list = ChatFilterConfig.Config?.WordWhiteList;
            if (list == null || list.Count == 0) return false;
            if (string.IsNullOrWhiteSpace(word)) return false;

            var lower = word.ToLowerInvariant();
            var trimmed = lower.TrimEnd('.', ',', '!', '?', ';', ':');
            if (list.Contains(lower) || list.Contains(trimmed)) return true;
            foreach (var w in list)
            {
                if (string.IsNullOrEmpty(w)) continue;
                if (lower.Contains(w.ToLowerInvariant())) return true;
            }
            return false;
        }

        private string ReplaceWord(string original)
        {
            var cfg = ChatFilterConfig.Config;
            if (cfg == null) return new string('*', Math.Max(1, original.Length));
            if (cfg.WordFilterUseCustomReplacement)
                return cfg.WordFilterCustomReplacement ?? "*";
            var rep = cfg.WordFilterReplacement ?? "*";
            if (string.IsNullOrEmpty(rep)) return cfg.WordFilterCustomReplacement ?? "*";
            var result = "";
            for (int i = 0; i < original.Length; i++)
                result += rep;
            return result;
        }

        private static string TranslateLeet(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            var s = input;
            foreach (var kv in LeetTable)
                s = s.Replace(kv.Key, kv.Value);
            return s;
        }

        private const int MuteDuration24HoursSeconds = 24 * 3600;

        private static void SendRedMessage(BasePlayer player, string text)
        {
            if (player?.net?.connection == null) return;
            var colored = "<color=red>" + text + "</color>";
            ConsoleNetwork.SendClientCommand(player.net.connection, "chat.add", 2, 0, colored);
        }

        public void RecordOffense(BasePlayer player)
        {
            if (player == null) return;
            var id = player.UserIDString;
            if (_playerOffenses.TryGetValue(id, out var data))
            {
                data.Offenses++;
                data.LastOffense = DateTime.UtcNow;
            }
            else
            {
                _playerOffenses[id] = new OffenseData();
            }
            SaveData();
            ApplyOffensePunishment(player);
        }

        private void ApplyOffensePunishment(BasePlayer player)
        {
            if (!_playerOffenses.TryGetValue(player.UserIDString, out var data))
                return;

            int count = data.Offenses;

            // First offense: warning only (red message)
            if (count == 1)
            {
                SendRedMessage(player, "You dirty mouthed basement dweller clean it up or get muted!");
                return;
            }

            // Second offense: mute 24 hours
            if (count == 2)
            {
                SendRedMessage(player, "You have been muted for 24 hours the rest of us will enjoy your silence.");
                player.SetPlayerFlag(BasePlayer.PlayerFlags.ChatMute, true);
                ServerMgr.Instance?.Invoke(() =>
                {
                    if (player != null && !player.IsDestroyed)
                        player.SetPlayerFlag(BasePlayer.PlayerFlags.ChatMute, false);
                }, MuteDuration24HoursSeconds);
                return;
            }

            // Third offense and beyond: permanent mute
            if (count >= 3)
            {
                SendRedMessage(player, "You have been permanently muted open a ticket to grovel for forgiveness.");
                player.SetPlayerFlag(BasePlayer.PlayerFlags.ChatMute, true);
            }
        }

        public void ClearOffense(BasePlayer player)
        {
            if (player == null) return;
            _playerOffenses.Remove(player.UserIDString);
            SaveData();
        }

        public int GetOffenseCount(BasePlayer player)
        {
            if (player == null) return 0;
            return _playerOffenses.TryGetValue(player.UserIDString, out var d) ? d.Offenses : 0;
        }

        private void LoadData()
        {
            try
            {
                var serverRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var path = Path.Combine(serverRoot, "HarmonyMods_Data", DataFileName + ".json");
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    return;
                if (!File.Exists(path)) return;
                var json = File.ReadAllText(path);
                _playerOffenses = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, OffenseData>>(json)
                    ?? new Dictionary<string, OffenseData>();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[ChatFilter] Load data: " + ex.Message);
            }
        }

        private void SaveData()
        {
            try
            {
                var serverRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var path = Path.Combine(serverRoot, "HarmonyMods_Data", DataFileName + ".json");
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(_playerOffenses, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[ChatFilter] Save data: " + ex.Message);
            }
        }
    }
}
