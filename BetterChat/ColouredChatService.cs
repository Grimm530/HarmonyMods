using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEngine;
using Pool = Facepunch.Pool;

namespace BetterChatHarmony
{
    public class ColouredChatService
    {
        private const string ColourRegex = @"^#(?:[0-9a-fA-F]{3}){1,2}$";

        private readonly string _dataPath;
        private readonly string _oxideDataPath;
        private readonly StringBuilder _sb = new StringBuilder();
        private readonly System.Random _random = new System.Random();
        private readonly Dictionary<string, CachePlayerData> _cache = new Dictionary<string, CachePlayerData>();
        private StoredData _stored = new StoredData();

        public Dictionary<string, PlayerData> AllColourData => _stored.AllColourData;

        public ColouredChatService(string serverRoot)
        {
            var dir = Path.Combine(serverRoot, "HarmonyData", "BetterChat");
            Directory.CreateDirectory(dir);
            _dataPath = Path.Combine(dir, "Colours.json");
            _oxideDataPath = Path.Combine(serverRoot, "oxide", "data", "ColouredChat.json");
            Load();
        }

        public class StoredData
        {
            public Dictionary<string, PlayerData> AllColourData { get; set; } =
                new Dictionary<string, PlayerData>(StringComparer.OrdinalIgnoreCase);
        }

        public class PlayerData
        {
            [JsonProperty("Name Colour")]
            public string NameColour = string.Empty;

            [JsonProperty("Name Gradient Args")]
            public string[] NameGradientArgs;

            [JsonProperty("Message Colour")]
            public string MessageColour = string.Empty;

            [JsonProperty("Message Gradient Args")]
            public string[] MessageGradientArgs;

            [JsonProperty("Last active")]
            public long LastActive = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private class CachePlayerData
        {
            public string NameColourGradient;
            public string PrimaryGroup;
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_dataPath))
                {
                    _stored = JsonConvert.DeserializeObject<StoredData>(File.ReadAllText(_dataPath)) ?? new StoredData();
                }
                else if (File.Exists(_oxideDataPath))
                {
                    _stored = JsonConvert.DeserializeObject<StoredData>(File.ReadAllText(_oxideDataPath)) ?? new StoredData();
                    Save();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BetterChat] ColouredChat data load: " + ex.Message);
                _stored = new StoredData();
            }

            if (_stored.AllColourData == null)
                _stored.AllColourData = new Dictionary<string, PlayerData>(StringComparer.OrdinalIgnoreCase);
            else
                _stored.AllColourData = new Dictionary<string, PlayerData>(_stored.AllColourData, StringComparer.OrdinalIgnoreCase);
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(_dataPath, JsonConvert.SerializeObject(_stored, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BetterChat] ColouredChat data save: " + ex.Message);
            }
        }

        public void TouchActive(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return;
            if (!_stored.AllColourData.TryGetValue(playerId, out var data)) return;
            data.LastActive = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        public void ClearUpData()
        {
            var cfg = BetterChatConfig.Config?.Coloured;
            if (cfg == null || cfg.InactivityRemovalTime == 0) return;
            long cutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - cfg.InactivityRemovalTime * 86400L;
            var remove = new List<string>();
            foreach (var kv in _stored.AllColourData)
            {
                if (kv.Value.LastActive == 0) continue;
                if (kv.Value.LastActive < cutoff) remove.Add(kv.Key);
            }
            for (int i = 0; i < remove.Count; i++)
                _stored.AllColourData.Remove(remove[i]);
        }

        public void ClearCache() => _cache.Clear();

        public void ClearCache(string id)
        {
            if (!string.IsNullOrEmpty(id)) _cache.Remove(id);
        }

        public PlayerData GetOrCreate(string key, bool isGroup = false)
        {
            if (!_stored.AllColourData.TryGetValue(key, out var data) || data == null)
            {
                data = new PlayerData();
                if (isGroup) data.LastActive = 0;
                _stored.AllColourData[key] = data;
            }
            return data;
        }

        public void ChangeNameColour(string key, string colour, string[] colourArgs)
        {
            var data = GetOrCreate(key);
            data.NameColour = colour ?? string.Empty;
            data.NameGradientArgs = colourArgs;
        }

        public void ChangeMessageColour(string key, string colour, string[] colourArgs)
        {
            var data = GetOrCreate(key);
            data.MessageColour = colour ?? string.Empty;
            data.MessageGradientArgs = colourArgs;
        }

        public bool TryRemoveEmpty(string key)
        {
            if (!_stored.AllColourData.TryGetValue(key, out var data) || data == null) return false;
            if (string.IsNullOrEmpty(data.NameColour) && data.NameGradientArgs == null &&
                string.IsNullOrEmpty(data.MessageColour) && data.MessageGradientArgs == null)
            {
                _stored.AllColourData.Remove(key);
                return true;
            }
            return false;
        }

        public void ApplyToMessage(BetterChatMessage chatMessage)
        {
            if (chatMessage?.Player == null) return;
            var player = chatMessage.Player;
            if (!_stored.AllColourData.TryGetValue(player.UserIDString, out var playerData) || playerData == null)
                playerData = new PlayerData();

            ApplyName(chatMessage, player, playerData);
            chatMessage.Message = GetColouredMessage(player, playerData, chatMessage.Message);
        }

        private void ApplyName(BetterChatMessage chatMessage, BasePlayer player, PlayerData playerData)
        {
            var coloured = GetColouredName(player, playerData);
            if (!string.IsNullOrEmpty(coloured.Name))
                chatMessage.Username = coloured.Name;
            if (!string.IsNullOrEmpty(coloured.Colour) && chatMessage.UsernameSettings != null)
                chatMessage.UsernameSettings.Color = coloured.Colour;
        }

        public struct ColouredName
        {
            public string Name;
            public string Colour;
        }

        public ColouredName GetColouredName(BasePlayer player, PlayerData playerData)
        {
            var playerUserName = player.displayName ?? "";
            var playerColour = player.IsAdmin ? "#af5" : "#5af";
            var playerColourNonModified = playerColour;
            var cfg = BetterChatConfig.Config?.Coloured;

            if (!_cache.TryGetValue(player.UserIDString, out var cached) || cached == null)
            {
                var gradientName = string.Empty;
                if (playerData?.NameGradientArgs != null)
                    gradientName = ProcessGradient(playerUserName, playerData.NameGradientArgs);
                cached = new CachePlayerData
                {
                    NameColourGradient = gradientName,
                    PrimaryGroup = GetPrimaryColourGroup(player.UserIDString)
                };
                _cache[player.UserIDString] = cached;
            }

            if (HasNameShowPerm(player, cfg))
            {
                if (playerData?.NameGradientArgs != null)
                    playerUserName = cached.NameColourGradient;
                else if (!string.IsNullOrEmpty(playerData?.NameColour))
                    playerColour = playerData.NameColour;
                else if (playerUserName == (player.displayName ?? "") && !string.IsNullOrEmpty(cached.NameColourGradient))
                    playerUserName = cached.NameColourGradient;
            }

            if (!string.IsNullOrEmpty(cached.PrimaryGroup) &&
                _stored.AllColourData.TryGetValue(cached.PrimaryGroup, out var groupData))
            {
                if (playerUserName == (player.displayName ?? "") && playerColour == playerColourNonModified)
                {
                    if (groupData?.NameGradientArgs != null)
                    {
                        if (string.IsNullOrEmpty(cached.NameColourGradient))
                            cached.NameColourGradient = ProcessGradient(player.displayName ?? "", groupData.NameGradientArgs);
                        playerUserName = cached.NameColourGradient;
                    }
                    else if (!string.IsNullOrEmpty(groupData?.NameColour))
                        playerColour = groupData.NameColour;
                }
            }

            return new ColouredName
            {
                Name = playerUserName,
                Colour = playerColour == playerColourNonModified ? string.Empty : playerColour
            };
        }

        public string GetColouredMessage(BasePlayer player, PlayerData playerData, string message)
        {
            var playerMessage = message ?? "";
            var cfg = BetterChatConfig.Config?.Coloured;

            if (HasMessageShowPerm(player, cfg))
            {
                if (playerData?.MessageGradientArgs != null)
                    playerMessage = ProcessGradient(message, playerData.MessageGradientArgs);
                else if (!string.IsNullOrEmpty(playerData?.MessageColour))
                    playerMessage = "<color=" + playerData.MessageColour + ">" + message + "</color>";
            }

            if (!_cache.TryGetValue(player.UserIDString, out var cached) || cached == null)
            {
                cached = new CachePlayerData { PrimaryGroup = GetPrimaryColourGroup(player.UserIDString) };
                _cache[player.UserIDString] = cached;
            }

            if (!string.IsNullOrEmpty(cached.PrimaryGroup) &&
                _stored.AllColourData.TryGetValue(cached.PrimaryGroup, out var groupData) &&
                playerMessage == message)
            {
                if (groupData?.MessageGradientArgs != null)
                    playerMessage = ProcessGradient(message, groupData.MessageGradientArgs);
                else if (!string.IsNullOrEmpty(groupData?.MessageColour))
                    playerMessage = "<color=" + groupData.MessageColour + ">" + message + "</color>";
            }

            return playerMessage;
        }

        public string GetPrimaryColourGroup(string playerId)
        {
            var groups = PermissionsBridge.GetUserGroups(playerId);
            var primaryGroup = string.Empty;
            var groupRank = -1;
            for (int i = 0; i < groups.Length; i++)
            {
                var group = groups[i];
                if (!_stored.AllColourData.ContainsKey(group)) continue;
                int current = PermissionsBridge.GetGroupRank(group);
                if (current > groupRank)
                {
                    groupRank = current;
                    primaryGroup = group;
                }
            }
            return primaryGroup;
        }

        public bool IsValidColour(string input) =>
            !string.IsNullOrEmpty(input) && Regex.IsMatch(input, ColourRegex);

        public string IsInvalidCharacter(string input)
        {
            var blocked = BetterChatConfig.Config?.Coloured?.BlockedValues;
            if (blocked == null || string.IsNullOrEmpty(input)) return null;
            for (int i = 0; i < blocked.Length; i++)
            {
                if (!string.IsNullOrEmpty(blocked[i]) && input.IndexOf(blocked[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return blocked[i];
            }
            return null;
        }

        public bool IsValidNameColour(string input, BasePlayer player)
        {
            var cfg = BetterChatConfig.Config?.Coloured;
            if (cfg == null) return true;
            if (player != null && CanNameBypass(player, cfg)) return true;
            if (cfg.NameUseBlacklist)
                return !ContainsHex(cfg.NameBlockColoursHex, input) && !InAnyRange(cfg.NameBlacklistedRangeColoursHex, input);
            if (cfg.NameUseWhitelist)
                return ContainsHex(cfg.NameWhitelistedColoursHex, input) || InAnyRange(cfg.NameWhitelistedRangeColoursHex, input);
            return true;
        }

        public bool IsValidMessageColour(string input, BasePlayer player)
        {
            var cfg = BetterChatConfig.Config?.Coloured;
            if (cfg == null) return true;
            if (player != null && CanMessageBypass(player, cfg)) return true;
            if (cfg.MessageUseBlacklist)
                return !ContainsHex(cfg.MessageBlockColoursHex, input) && !InAnyRange(cfg.MessageBlacklistedRangeColoursHex, input);
            if (cfg.MessageUseWhitelist)
                return ContainsHex(cfg.MessageWhitelistedColoursHex, input) || InAnyRange(cfg.MessageWhitelistedRangeColoursHex, input);
            return true;
        }

        public string GetRndColour() => $"#{_random.Next(0x1000000):X6}";

        public string ProcessGradient(string name, string[] colourArgs)
        {
            if (string.IsNullOrEmpty(name) || colourArgs == null || colourArgs.Length == 0)
                return name ?? "";

            _sb.Clear();
            var colours = Pool.Get<List<Color>>();
            colours.Clear();

            int nameLength = name.Length;
            int gradientsSteps = colourArgs.Length > 1 ? nameLength / (colourArgs.Length - 1) : nameLength;
            if (gradientsSteps <= 1)
            {
                for (int i = 0; i < nameLength; i++)
                {
                    Color startColour;
                    int idx = i > colourArgs.Length - 1 ? colourArgs.Length - 1 : i;
                    ColorUtility.TryParseHtmlString(colourArgs[idx], out startColour);
                    colours.Add(startColour);
                }
            }
            else
            {
                int gradientIterations = nameLength / gradientsSteps;
                for (int i = 0; i < gradientIterations; i++)
                {
                    if (colours.Count >= nameLength) continue;
                    Color startColour;
                    int idx = i > colourArgs.Length - 1 ? colourArgs.Length - 1 : i;
                    ColorUtility.TryParseHtmlString(colourArgs[idx], out startColour);
                    Color endColour = startColour;
                    if (i < colourArgs.Length - 1)
                        ColorUtility.TryParseHtmlString(colourArgs[i + 1], out endColour);
                    GetAndAddGradients(startColour, endColour, gradientsSteps, colours);
                }
                if (colours.Count < nameLength)
                {
                    Color endColour;
                    ColorUtility.TryParseHtmlString(colourArgs[colourArgs.Length - 1], out endColour);
                    while (colours.Count < name.Length)
                        colours.Add(endColour);
                }
            }

            int count = colours.Count < name.Length ? colours.Count : name.Length;
            for (int i = 0; i < count; i++)
            {
                _sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(colours[i])).Append('>')
                    .Append(name[i]).Append("</color>");
            }
            Pool.FreeUnmanaged(ref colours);
            return _sb.ToString();
        }

        private static void GetAndAddGradients(Color start, Color end, int steps, List<Color> results)
        {
            if (steps <= 1)
            {
                results.Add(start);
                return;
            }
            float stepR = (end.r - start.r) / (steps - 1);
            float stepG = (end.g - start.g) / (steps - 1);
            float stepB = (end.b - start.b) / (steps - 1);
            for (int i = 0; i < steps; i++)
                results.Add(new Color(start.r + stepR * i, start.g + stepG * i, start.b + stepB * i));
        }

        private static bool ContainsHex(List<string> list, string input)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], input, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool InAnyRange(List<BetterChatConfig.ColourRange> ranges, string input)
        {
            if (ranges == null) return false;
            for (int i = 0; i < ranges.Count; i++)
            {
                var r = ranges[i];
                if (r == null) continue;
                if (IsInHexRange(input, r.From, r.To)) return true;
            }
            return false;
        }

        private static bool IsInHexRange(string hexCode, string rangeHexCode1, string rangeHexCode2)
        {
            Color mainColour, start, end;
            ColorUtility.TryParseHtmlString(hexCode, out mainColour);
            ColorUtility.TryParseHtmlString(rangeHexCode1, out start);
            ColorUtility.TryParseHtmlString(rangeHexCode2, out end);
            return mainColour.r >= start.r && mainColour.r <= end.r &&
                   mainColour.g >= start.g && mainColour.g <= end.g &&
                   mainColour.b >= start.b && mainColour.b <= end.b;
        }

        public static bool HasNameShowPerm(BasePlayer player, BetterChatConfig.ColouredSettings cfg) =>
            player != null && (player.IsAdmin || PermissionsBridge.UserHasPermission(player.UserIDString, cfg?.NamePermShow));

        public static bool HasNamePerm(BasePlayer player, BetterChatConfig.ColouredSettings cfg) =>
            player != null && (player.IsAdmin || PermissionsBridge.UserHasPermission(player.UserIDString, cfg?.NamePermUse));

        public static bool HasNameRainbow(BasePlayer player, BetterChatConfig.ColouredSettings cfg) =>
            player != null && (player.IsAdmin || PermissionsBridge.UserHasPermission(player.UserIDString, cfg?.NamePermRainbow));

        public static bool CanNameGradient(BasePlayer player, BetterChatConfig.ColouredSettings cfg) =>
            player != null && (player.IsAdmin || PermissionsBridge.UserHasPermission(player.UserIDString, cfg?.NamePermGradient));

        public static bool CanNameBypass(BasePlayer player, BetterChatConfig.ColouredSettings cfg) =>
            player != null && (player.IsAdmin || PermissionsBridge.UserHasPermission(player.UserIDString, cfg?.NamePermBypass));

        public static bool CanNameSetOthers(BasePlayer player, BetterChatConfig.ColouredSettings cfg) =>
            player != null && (player.IsAdmin || PermissionsBridge.UserHasPermission(player.UserIDString, cfg?.NamePermSetOthers));

        public static bool CanNameRandomColour(BasePlayer player, BetterChatConfig.ColouredSettings cfg) =>
            player != null && (player.IsAdmin || PermissionsBridge.UserHasPermission(player.UserIDString, cfg?.NamePermRandomColour));

        public static bool HasMessageShowPerm(BasePlayer player, BetterChatConfig.ColouredSettings cfg) =>
            player != null && (player.IsAdmin || PermissionsBridge.UserHasPermission(player.UserIDString, cfg?.MessagePermShow));

        public static bool HasMessagePerm(BasePlayer player, BetterChatConfig.ColouredSettings cfg) =>
            player != null && (player.IsAdmin || PermissionsBridge.UserHasPermission(player.UserIDString, cfg?.MessagePermUse));

        public static bool HasMessageRainbow(BasePlayer player, BetterChatConfig.ColouredSettings cfg) =>
            player != null && (player.IsAdmin || PermissionsBridge.UserHasPermission(player.UserIDString, cfg?.MessagePermRainbow));

        public static bool CanMessageGradient(BasePlayer player, BetterChatConfig.ColouredSettings cfg) =>
            player != null && (player.IsAdmin || PermissionsBridge.UserHasPermission(player.UserIDString, cfg?.MessagePermGradient));

        public static bool CanMessageBypass(BasePlayer player, BetterChatConfig.ColouredSettings cfg) =>
            player != null && (player.IsAdmin || PermissionsBridge.UserHasPermission(player.UserIDString, cfg?.MessagePermBypass));

        public static bool CanMessageSetOthers(BasePlayer player, BetterChatConfig.ColouredSettings cfg) =>
            player != null && (player.IsAdmin || PermissionsBridge.UserHasPermission(player.UserIDString, cfg?.MessagePermSetOthers));

        public static bool CanMessageRandomColour(BasePlayer player, BetterChatConfig.ColouredSettings cfg) =>
            player != null && (player.IsAdmin || PermissionsBridge.UserHasPermission(player.UserIDString, cfg?.MessagePermRandomColour));
    }
}
