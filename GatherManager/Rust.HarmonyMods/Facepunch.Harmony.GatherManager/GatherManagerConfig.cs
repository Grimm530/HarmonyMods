using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Facepunch.Harmony.GatherManager
{
    public class GatherManagerConfig
    {
        private static readonly string ConfigPath = Path.Combine("HarmonyConfig", "GatherManager.json");

        public const float DefaultMiningQuarryResourceTickRate = 5f;
        public const float DefaultExcavatorResourceTickRate = 3f;
        public const float DefaultExcavatorTimeForFullResources = 120f;
        public const float DefaultExcavatorBeltSpeedMax = 0.1f;

        public string ChatPrefix { get; set; } = "Gather Manager";
        public string ChatPrefixColor { get; set; } = "#008000ff";

        public Dictionary<string, float> GatherResourceModifiers { get; set; } = new Dictionary<string, float>();
        public Dictionary<string, float> GatherDispenserModifiers { get; set; } = new Dictionary<string, float>();
        public Dictionary<string, float> QuarryResourceModifiers { get; set; } = new Dictionary<string, float>();
        public Dictionary<string, float> ExcavatorResourceModifiers { get; set; } = new Dictionary<string, float>();
        public Dictionary<string, float> PickupResourceModifiers { get; set; } = new Dictionary<string, float>();
        public Dictionary<string, float> SurveyResourceModifiers { get; set; } = new Dictionary<string, float>();

        public bool DebugGather { get; set; } = false;

        public float MiningQuarryResourceTickRate { get; set; } = DefaultMiningQuarryResourceTickRate;
        public float ExcavatorResourceTickRate { get; set; } = DefaultExcavatorResourceTickRate;
        public float ExcavatorTimeForFullResources { get; set; } = DefaultExcavatorTimeForFullResources;
        public float ExcavatorBeltSpeedMax { get; set; } = DefaultExcavatorBeltSpeedMax;

        public void Save()
        {
            try
            {
                if (!Directory.Exists("HarmonyConfig"))
                    Directory.CreateDirectory("HarmonyConfig");

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"ChatPrefix\": \"{Escape(ChatPrefix)}\",");
                sb.AppendLine($"  \"ChatPrefixColor\": \"{Escape(ChatPrefixColor)}\",");
                sb.AppendLine($"  \"MiningQuarryResourceTickRate\": {MiningQuarryResourceTickRate},");
                sb.AppendLine($"  \"ExcavatorResourceTickRate\": {ExcavatorResourceTickRate},");
                sb.AppendLine($"  \"ExcavatorTimeForFullResources\": {ExcavatorTimeForFullResources},");
                sb.AppendLine($"  \"ExcavatorBeltSpeedMax\": {ExcavatorBeltSpeedMax},");
                sb.AppendLine($"  \"Debug\": {DebugGather.ToString().ToLower()},");
                sb.AppendLine($"  \"DebugGather\": {DebugGather.ToString().ToLower()},");
                sb.AppendLine($"  \"GatherResourceModifiers\": {DictToJson(GatherResourceModifiers)},");
                sb.AppendLine($"  \"GatherDispenserModifiers\": {DictToJson(GatherDispenserModifiers)},");
                sb.AppendLine($"  \"QuarryResourceModifiers\": {DictToJson(QuarryResourceModifiers)},");
                sb.AppendLine($"  \"ExcavatorResourceModifiers\": {DictToJson(ExcavatorResourceModifiers)},");
                sb.AppendLine($"  \"PickupResourceModifiers\": {DictToJson(PickupResourceModifiers)},");
                sb.AppendLine($"  \"SurveyResourceModifiers\": {DictToJson(SurveyResourceModifiers)}");
                sb.Append("}");
                File.WriteAllText(ConfigPath, sb.ToString());
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GatherManager] Failed to save config: {ex.Message}");
            }
        }

        private static string DictToJson(Dictionary<string, float> d)
        {
            if (d == null || d.Count == 0) return "{}";
            var sb = new StringBuilder("{");
            var first = true;
            foreach (var kv in d)
            {
                if (!first) sb.Append(",");
                sb.Append($"\"{Escape(kv.Key)}\":{kv.Value}");
                first = false;
            }
            sb.Append("}");
            return sb.ToString();
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private static Dictionary<string, float> ParseDict(string json)
        {
            var dict = new Dictionary<string, float>();
            if (string.IsNullOrWhiteSpace(json) || json == "{}") return dict;
            var trimmed = json.Trim();
            if (!trimmed.StartsWith("{") || !trimmed.EndsWith("}")) return dict;
            var inner = trimmed.Substring(1, trimmed.Length - 2).Trim();
            if (string.IsNullOrEmpty(inner)) return dict;
            var inKey = false;
            var inValue = false;
            var key = new StringBuilder();
            var value = new StringBuilder();
            for (int i = 0; i < inner.Length; i++)
            {
                var c = inner[i];
                if (c == '"' && (i == 0 || inner[i - 1] != '\\'))
                {
                    if (!inKey && !inValue) { inKey = true; key.Clear(); }
                    else if (inKey) { inKey = false; }
                    else if (!inValue) { inValue = true; value.Clear(); }
                    else { inValue = false; }
                }
                else if (inKey) key.Append(c);
                else if (inValue && c != '"') value.Append(c);
                else if (c == ':' && key.Length > 0) { inValue = true; value.Clear(); }
                else if ((c == ',' || c == '}') && key.Length > 0 && value.Length > 0)
                {
                    if (float.TryParse(value.ToString().Trim(), out var v))
                        dict[key.ToString().Trim()] = v;
                    key.Clear();
                    value.Clear();
                }
            }
            if (key.Length > 0 && value.Length > 0 && float.TryParse(value.ToString().Trim(), out var lastV))
                dict[key.ToString().Trim()] = lastV;
            return dict;
        }

        public static GatherManagerConfig Load()
        {
            var config = new GatherManagerConfig();
            try
            {
                if (!Directory.Exists("HarmonyConfig"))
                    Directory.CreateDirectory("HarmonyConfig");

                if (!File.Exists(ConfigPath))
                {
                    config.Save();
                    Debug.Log("[GatherManager] Created default config at HarmonyConfig/GatherManager.json");
                    return config;
                }
                var json = File.ReadAllText(ConfigPath);
                config.GatherResourceModifiers = ParseDict(ExtractJsonValueForKey(json, "GatherResourceModifiers"));
                config.GatherDispenserModifiers = ParseDict(ExtractJsonValueForKey(json, "GatherDispenserModifiers"));
                config.QuarryResourceModifiers = ParseDict(ExtractJsonValueForKey(json, "QuarryResourceModifiers"));
                config.ExcavatorResourceModifiers = ParseDict(ExtractJsonValueForKey(json, "ExcavatorResourceModifiers"));
                config.PickupResourceModifiers = ParseDict(ExtractJsonValueForKey(json, "PickupResourceModifiers"));
                config.SurveyResourceModifiers = ParseDict(ExtractJsonValueForKey(json, "SurveyResourceModifiers"));
                if (float.TryParse(ExtractNumForKey(json, "MiningQuarryResourceTickRate"), out var qTr)) config.MiningQuarryResourceTickRate = qTr;
                if (float.TryParse(ExtractNumForKey(json, "ExcavatorResourceTickRate"), out var eTr)) config.ExcavatorResourceTickRate = eTr;
                if (float.TryParse(ExtractNumForKey(json, "ExcavatorTimeForFullResources"), out var eTf)) config.ExcavatorTimeForFullResources = eTf;
                if (float.TryParse(ExtractNumForKey(json, "ExcavatorBeltSpeedMax"), out var eBs)) config.ExcavatorBeltSpeedMax = eBs;
                if ( HasKeyTrue( json, "Debug" ) || HasKeyTrue( json, "DebugGather" ) )
                    config.DebugGather = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GatherManager] Failed to load config: {ex.Message}");
            }
            return config;
        }

        private static string ExtractJsonValueForKey(string json, string key)
        {
            var keyIdx = json.IndexOf($"\"{key}\"", StringComparison.Ordinal);
            if (keyIdx < 0) return "{}";
            var colon = json.IndexOf(':', keyIdx);
            if (colon < 0) return "{}";
            var rest = json.Substring(colon + 1).Trim();
            var start = rest.IndexOf('{');
            if (start < 0) return "{}";
            var d = 1;
            for (int i = start + 1; i < rest.Length; i++)
            {
                if (rest[i] == '{') d++;
                else if (rest[i] == '}') { d--; if (d == 0) return rest.Substring(start, i - start + 1); }
            }
            return "{}";
        }

        private static bool HasKeyTrue( string json, string key )
        {
            var keyIdx = json.IndexOf( $"\"{key}\"", StringComparison.Ordinal );
            if ( keyIdx < 0 ) return false;
            var after = json.Substring( keyIdx + key.Length + 3 );
            for ( int i = 0; i < Math.Min( 15, after.Length ); i++ )
            {
                if ( after[i] == 't' && i + 4 <= after.Length && after.Substring( i, 4 ) == "true" )
                    return true;
                if ( after[i] == 'f' && i + 5 <= after.Length && after.Substring( i, 5 ) == "false" )
                    return false;
            }
            return false;
        }

        private static string ExtractNumForKey(string json, string key)
        {
            var keyIdx = json.IndexOf($"\"{key}\"", StringComparison.Ordinal);
            if (keyIdx < 0) return "0";
            var colon = json.IndexOf(':', keyIdx);
            if (colon < 0) return "0";
            var rest = json.Substring(colon + 1).Trim().TrimStart(' ');
            var end = 0;
            while (end < rest.Length && (char.IsDigit(rest[end]) || rest[end] == '.' || rest[end] == '-')) end++;
            return end > 0 ? rest.Substring(0, end) : "0";
        }

        private static string ExtractJsonValue(string line)
        {
            var colon = line.IndexOf(':');
            if (colon < 0) return "{}";
            var rest = line.Substring(colon + 1).Trim();
            var start = rest.IndexOf('{');
            if (start < 0) return "{}";
            var d = 1;
            for (int i = start + 1; i < rest.Length; i++)
            {
                if (rest[i] == '{') d++;
                else if (rest[i] == '}') { d--; if (d == 0) return rest.Substring(start, i - start + 1); }
            }
            return "{}";
        }

        private static string ExtractNum(string line)
        {
            var colon = line.IndexOf(':');
            if (colon < 0) return "0";
            var rest = line.Substring(colon + 1).Trim().TrimEnd(',', ' ', '\r');
            return rest;
        }
    }
}
