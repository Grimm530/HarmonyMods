using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace ChatFilter
{
    public static class ChatFilterConfig
    {
        [Serializable]
        public class ConfigData
        {
            [JsonProperty("Word Filter - Enabled")]
            public bool WordFilterEnabled { get; set; } = true;

            [JsonProperty("Word Filter List")]
            public List<string> WordFilterPhrases { get; set; } = new List<string>
            {
                "Africoon", "akata", "Anal", "ASS", "assfucka", "assfucker", "asshole", "b00b", "b00bs", "ballsack",
                "bastard", "Beaner", "beastial", "beastiality", "bestial", "bestiality", "bitch", "blow job", "blowjob", "blowjobs",
                "boner", "Boob", "boobs", "booobs", "boooobs", "booooobs", "buttplug", "c0ck", "c0cksucker", "Camel jockey",
                "cheat", "cheating", "Chink", "chink", "cl1t", "clit", "clitoris", "clits", "Cock", "cock-sucker",
                "cockface", "cockhead", "cockmunch", "cockmuncher", "cocks", "cocksuck", "cocksucked", "cocksucker", "cocksucking", "cocksucks",
                "cocksuka", "cocksukka", "cok", "cokmuncher", "coksucka", "Coon", "Coonass", "Cum", "cummer", "Cums",
                "cumshot", "cunilingus", "cunillingus", "cunnilingus", "cunt", "cuntlick", "cuntlicker", "cuntlicking", "cunts", "cyalis",
                "cyberfuc", "cyberfuck", "cyberfucked", "cyberfucker", "cyberfuckers", "cyberfucking", "d1ck", "dick", "Dick", "dickhead",
                "DIE", "dildo", "dildos", "dog-fucker", "doggin", "dogging", "donkeyribber", "doosh", "Douche", "duche",
                "Dune Coon", "ejaculate", "ejaculated", "ejaculates", "ejaculating", "ejaculatings", "ejaculation", "ejakulate", "F4nny", "fag",
                "fagging", "faggitt", "faggot", "faggs", "fagot", "fagots", "Fags", "fatass", "felching", "fellate",
                "fellatio", "fingerfuck", "fingerfucked", "fingerfucker", "fingerfuckers", "fingerfucking", "fingerfucks", "fistfuck", "fistfucked", "fistfucker",
                "fistfuckers", "fistfucking", "fistfuckings", "fistfucks", "fuck", "fucker", "fuckhead", "fuckheads", "fucking", "fuckingshitmotherfucker",
                "fuckme", "fuckwhit", "fuckwit", "fukwhit", "fukwit", "gangbang", "gangbanged", "gangbangs", "gaysex", "goatse",
                "Gook", "hardcoresex", "hitler", "horniest", "horny", "hotsex", "humming", "jack-off", "jackoff", "jap",
                "jerk-off", "JEWS", "jism", "Jiz", "jizm", "jizz", "Jungle Bunny", "kawk", "KILL", "kill myself",
                "kill yourself", "kkk", "Kock", "kondum", "kondums", "kummer", "kunilingus", "kys", "l3i+ch", "l3itch",
                "labia", "m0f0", "m0fo", "m45terbate", "ma5terb8", "ma5terbate", "masochist", "master-bate", "masterb8", "masterbat*",
                "masterbat3", "masterbate", "masterbation", "masterbations", "masturbate", "MURDER", "n1g", "n1gga", "n1gger", "nazi",
                "nig", "niga", "nigg3r", "nigg4h", "Nigga", "niggah", "niggar", "niggas", "niggaz", "nigger",
                "niggers", "Niglet", "Nignog", "nutsack", "orgasim", "orgasims", "orgasm", "orgasms", "orgy", "p0rn",
                "Pecker", "pedo", "pedophile", "penis", "penisfucker", "phonesex", "phuck", "phuk", "phuked", "phuking",
                "phukked", "phukking", "phuks", "pigfucker", "pissoff", "Porch Monkey", "Porn", "porno", "pornography", "pornos",
                "pron", "pube", "pubes", "pusse", "pussi", "pussies", "PUSSY", "pussys", "rape", "Raped",
                "raping", "Rapist", "rectum", "retard", "retarded", "retards", "rimjaw", "rimming", "sadist", "scat",
                "schlong", "scrotum", "Semen", "Sex", "shaggin", "shagging", "shit", "shitdick", "shited", "shitfuck",
                "shithead", "shitty", "slut", "sluts", "smegma", "Smut", "Spook", "spunk", "suicid", "suicide",
                "t1tt1e5", "t1tties", "Teets", "testical", "testicle", "Tit", "titfuck", "Tits", "tittie5", "tittiefucker",
                "titties", "tittyfuck", "tittywank", "titwank", "Towel Head", "TRUMP", "Turk", "tw4t", "twat", "twathead",
                "twatty", "twunt", "twunter", "v14gra", "v1gra", "vagina", "viagra", "vulva", "w00se", "Wang",
                "wank", "wanker", "wanky", "Wetback", "whore", "Wigger", "Willies", "Willy", "xrated", "xxx"
            };

            [JsonProperty("Word Filter - Prefix List (remove whole word if it STARTS with these; catches Nigger123, Niggerrrrr, Nigherxyz, etc.)")]
            public List<string> WordFilterPrefixPhrases { get; set; } = new List<string>
            {
                "nigger", "nigher"
            };

            [JsonProperty("Word Filter - Allow Partial Match In Words (legacy aggressive mode)")]
            public bool WordFilterAllowPartialMatchInWords { get; set; } = false;

            [JsonProperty("Word To White List")]
            public List<string> WordWhiteList { get; set; } = new List<string> { "night" };

            [JsonProperty("Word Filter - Replacement (e.g. * or leave empty to use custom)")]
            public string WordFilterReplacement { get; set; } = "*";

            [JsonProperty("Word Filter - Use Custom Replacement (if true, use Custom Replacement text)")]
            public bool WordFilterUseCustomReplacement { get; set; } = false;

            [JsonProperty("Word Filter - Custom Replacement")]
            public string WordFilterCustomReplacement { get; set; } = "Unicorn";

            [JsonProperty("Whole Message Filter - Enabled (clear entire message if any bad word)")]
            public bool FilterAll { get; set; } = false;

            [JsonProperty("Block Special Characters in Chat")]
            public bool BlockSpecialCharacters { get; set; } = false;

            [JsonProperty("Exclude Team Chat from filter")]
            public bool ExcludeTeamChat { get; set; } = false;

            [JsonProperty("Exclude admins from filter")]
            public bool ExcludeAdmins { get; set; } = true;

            [JsonProperty("Exclude Steam IDs (these players are not filtered)")]
            public List<string> ExcludeSteamIds { get; set; } = new List<string>();

            [JsonProperty("Offenses - Count To Mute (0 = disabled)")]
            public int MuteCount { get; set; } = 3;

            [JsonProperty("Offenses - Count To Kick (0 = disabled)")]
            public int KickCount { get; set; } = 3;

            [JsonProperty("Offenses - Count To Ban (0 = disabled)")]
            public int BanCount { get; set; } = 20;

            [JsonProperty("Offenses - Time To Mute (seconds)")]
            public int TimeToMute { get; set; } = 300;

            [JsonProperty("Time to Ban (minutes, 0 = permanent)")]
            public int BanTimeMin { get; set; } = 30;

            [JsonProperty("Offenses - Broadcast kick")]
            public bool BroadcastKick { get; set; } = true;

            [JsonProperty("Offenses - Broadcast Ban")]
            public bool BroadcastBan { get; set; } = true;

            [JsonProperty("Clear Offense After (0=Disabled, 1=All, 2=Kick, 3=Mute, 4=Ban)")]
            public int ClearOffenseAfter { get; set; } = 0;
        }

        public static ConfigData Config;
        private static string _configPath;

        public static void LoadConfig()
        {
            try
            {
                var serverRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var paths = new[]
                {
                    Path.Combine(serverRoot, "HarmonyConfig", "ChatFilter.json"),
                    Path.Combine(serverRoot, "oxide", "config", "ChatFilter.json"),
                    Path.Combine(serverRoot, "Config", "ChatFilter.json"),
                    Path.Combine(serverRoot, "ChatFilter.json"),
                };
                foreach (var p in paths)
                {
                    if (File.Exists(p))
                    {
                        _configPath = p;
                        var json = File.ReadAllText(p);
                        Config = JsonConvert.DeserializeObject<ConfigData>(json);
                        if (Config != null)
                        {
                            UnityEngine.Debug.Log("[ChatFilter] Config loaded from " + p);
                            return;
                        }
                    }
                }
                Config = new ConfigData();
                _configPath = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "HarmonyConfig", "ChatFilter.json");
                var dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                SaveConfig();
                UnityEngine.Debug.Log("[ChatFilter] Config created at " + _configPath);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[ChatFilter] Config load error: " + ex.Message);
                Config ??= new ConfigData();
            }
        }

        public static void SaveConfig()
        {
            try
            {
                if (_configPath == null || Config == null) return;
                var dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonConvert.SerializeObject(Config, Formatting.Indented);
                File.WriteAllText(_configPath, json);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[ChatFilter] Config save error: " + ex.Message);
            }
        }
    }
}
