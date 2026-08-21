using System;
using System.Collections.Generic;
using System.Globalization;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace TeleportGUI
{
    /// <summary>
    /// Canonical userdata.json schema (Oxide TeleportGUI 2.0.50 TeleportData).
    /// Also deserializes legacy TeleportGUI_Data.json where practical.
    /// Warp points live in warpdata.json via <see cref="WarpPoint"/> / <see cref="TeleportGUIWarpData"/>.
    /// </summary>
    public class TeleportGUIData
    {
        [JsonProperty("Users")]
        public Dictionary<ulong, UserData> Users { get; set; } = new Dictionary<ulong, UserData>();

        [JsonProperty("LastResetTime")]
        public double LastResetTime { get; set; }

        /// <summary>
        /// Legacy TeleportGUI_Data.json field. Populates <see cref="LastResetTime"/> on deserialize when present.
        /// Not written when serializing canonical userdata.
        /// </summary>
        [JsonProperty("LastResetDate", NullValueHandling = NullValueHandling.Ignore)]
        public string LastResetDate
        {
            get => null;
            set
            {
                if (string.IsNullOrEmpty(value))
                    return;
                if (DateTime.TryParse(value, null, DateTimeStyles.RoundtripKind, out DateTime dt))
                    LastResetTime = ToUnixSeconds(dt.ToUniversalTime());
            }
        }

        /// <summary>
        /// Legacy TeleportGUI_Data.json embedded warps. Prefer warpdata.json.
        /// Accepted on deserialize; never written to canonical userdata (ShouldSerialize).
        /// </summary>
        [JsonProperty("WarpPoints")]
        [JsonConverter(typeof(LegacyWarpPointsConverter))]
        public Dictionary<string, WarpPoint> WarpPoints { get; set; }

        public bool ShouldSerializeWarpPoints() => false;

        public bool ShouldSerializeLastResetDate() => false;

        [JsonIgnore]
        public bool ShouldResetUses
        {
            get
            {
                if (LastResetTime <= 0)
                    return true;

                DateTime now = DateTime.UtcNow;
                DateTime lastTime = DateTimeOffset.FromUnixTimeSeconds((long)LastResetTime).UtcDateTime;
                return now.Day != lastTime.Day || now.Month != lastTime.Month || now.Year != lastTime.Year;
            }
        }

        /// <summary>Alias used by older Harmony Mod code.</summary>
        public bool ShouldResetDailyUses() => ShouldResetUses;

        public void MarkResetNow()
        {
            LastResetTime = ToUnixSeconds(DateTime.UtcNow);
        }

        public static double ToUnixSeconds(DateTime utc) =>
            (utc.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

        public class UserData
        {
            [JsonProperty("Locations")]
            [JsonConverter(typeof(StringVector3DictionaryConverter))]
            public Dictionary<string, Vector3> Locations { get; set; } = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);

            [JsonProperty("Homes")]
            [JsonConverter(typeof(HomesDictionaryConverter))]
            public Dictionary<string, HomePoint> Homes { get; set; } = new Dictionary<string, HomePoint>(StringComparer.OrdinalIgnoreCase);

            [JsonProperty("TPUsage")]
            public Usage TPUsage { get; set; } = new Usage();

            [JsonProperty("HomeUsage")]
            public Usage HomeUsage { get; set; } = new Usage();

            [JsonProperty("WarpUsage")]
            public Usage WarpUsage { get; set; } = new Usage();

            [JsonProperty("LastOnlineTime")]
            public double LastOnlineTime { get; set; }

            [JsonProperty("ShowSleepers")]
            public bool ShowSleepers { get; set; }

            [JsonProperty("AutoAccept")]
            public AutoAcceptEnum AutoAccept { get; set; }

            // --- Legacy TeleportGUI_Data.json flat usage fields ---

            [JsonProperty("TPUsesToday", NullValueHandling = NullValueHandling.Ignore)]
            private int? LegacyTPUsesToday
            {
                get => null;
                set
                {
                    if (value.HasValue)
                    {
                        TPUsage ??= new Usage();
                        TPUsage.UsesToday = value.Value;
                    }
                }
            }

            [JsonProperty("HomeUsesToday", NullValueHandling = NullValueHandling.Ignore)]
            private int? LegacyHomeUsesToday
            {
                get => null;
                set
                {
                    if (value.HasValue)
                    {
                        HomeUsage ??= new Usage();
                        HomeUsage.UsesToday = value.Value;
                    }
                }
            }

            [JsonProperty("WarpUsesToday", NullValueHandling = NullValueHandling.Ignore)]
            private int? LegacyWarpUsesToday
            {
                get => null;
                set
                {
                    if (value.HasValue)
                    {
                        WarpUsage ??= new Usage();
                        WarpUsage.UsesToday = value.Value;
                    }
                }
            }

            [JsonProperty("TPCooldownUntil", NullValueHandling = NullValueHandling.Ignore)]
            private double? LegacyTPCooldownUntil
            {
                get => null;
                set
                {
                    if (value.HasValue)
                    {
                        TPUsage ??= new Usage();
                        TPUsage.Cooldown = value.Value;
                    }
                }
            }

            [JsonProperty("HomeCooldownUntil", NullValueHandling = NullValueHandling.Ignore)]
            private double? LegacyHomeCooldownUntil
            {
                get => null;
                set
                {
                    if (value.HasValue)
                    {
                        HomeUsage ??= new Usage();
                        HomeUsage.Cooldown = value.Value;
                    }
                }
            }

            [JsonProperty("WarpCooldownUntil", NullValueHandling = NullValueHandling.Ignore)]
            private double? LegacyWarpCooldownUntil
            {
                get => null;
                set
                {
                    if (value.HasValue)
                    {
                        WarpUsage ??= new Usage();
                        WarpUsage.Cooldown = value.Value;
                    }
                }
            }

            // --- Compatibility accessors for older Harmony Mod ---

            [JsonIgnore]
            public int TPUsesToday
            {
                get => TPUsage?.UsesToday ?? 0;
                set
                {
                    TPUsage ??= new Usage();
                    TPUsage.UsesToday = value;
                }
            }

            [JsonIgnore]
            public int HomeUsesToday
            {
                get => HomeUsage?.UsesToday ?? 0;
                set
                {
                    HomeUsage ??= new Usage();
                    HomeUsage.UsesToday = value;
                }
            }

            [JsonIgnore]
            public int WarpUsesToday
            {
                get => WarpUsage?.UsesToday ?? 0;
                set
                {
                    WarpUsage ??= new Usage();
                    WarpUsage.UsesToday = value;
                }
            }

            [JsonIgnore]
            public double TPCooldownUntil
            {
                get => TPUsage?.Cooldown ?? 0;
                set
                {
                    TPUsage ??= new Usage();
                    TPUsage.Cooldown = value;
                }
            }

            [JsonIgnore]
            public double HomeCooldownUntil
            {
                get => HomeUsage?.Cooldown ?? 0;
                set
                {
                    HomeUsage ??= new Usage();
                    HomeUsage.Cooldown = value;
                }
            }

            [JsonIgnore]
            public double WarpCooldownUntil
            {
                get => WarpUsage?.Cooldown ?? 0;
                set
                {
                    WarpUsage ??= new Usage();
                    WarpUsage.Cooldown = value;
                }
            }

            public bool IsOnTPCooldown(double now) => (TPUsage?.Cooldown ?? 0) > now;
            public bool IsOnHomeCooldown(double now) => (HomeUsage?.Cooldown ?? 0) > now;
            public bool IsOnWarpCooldown(double now) => (WarpUsage?.Cooldown ?? 0) > now;

            public void ResetDailyUses()
            {
                TPUsage ??= new Usage();
                HomeUsage ??= new Usage();
                WarpUsage ??= new Usage();
                TPUsage.UsesToday = 0;
                HomeUsage.UsesToday = 0;
                WarpUsage.UsesToday = 0;
            }

            [Flags]
            public enum AutoAcceptEnum
            {
                None = 0,
                Clans = 1 << 0,
                Teams = 1 << 1,
                Friends = 1 << 2,
                All = 1 << 3
            }

            public class Usage
            {
                [JsonProperty("UsesToday")]
                public int UsesToday { get; set; }

                [JsonProperty("Cooldown")]
                public double Cooldown { get; set; }

                public bool IsOnCooldown(double now) => Cooldown > now;

                public void Reset()
                {
                    UsesToday = 0;
                    Cooldown = 0;
                }
            }

            public class HomePoint
            {
                [JsonProperty("Position")]
                public Vector3 Position { get; set; }

                [JsonProperty("Offset")]
                public Vector3 Offset { get; set; }

                [JsonProperty("EntityID")]
                public ulong EntityID { get; set; }

                public HomePoint() { }

                public HomePoint(Vector3 position)
                {
                    Position = position;
                    Offset = Vector3.zero;
                    EntityID = 0UL;
                }

                public Vector3 ToVector3()
                {
                    return TryGetPosition(out Vector3 position) ? position : Position;
                }

                public static HomePoint FromVector3(Vector3 v) => new HomePoint(v);

                public static implicit operator HomePoint(Vector3Data v) =>
                    v == null ? new HomePoint() : FromVector3(v.ToVector3());

                /// <summary>
                /// Resolve world position. Fixed homes use Position; bag/bed homes use entity + Offset.
                /// Returns false when the linked entity is missing or destroyed.
                /// </summary>
                public bool TryGetPosition(out Vector3 position)
                {
                    if (EntityID == 0UL)
                    {
                        position = Position;
                        return true;
                    }

                    try
                    {
                        var entity = BaseNetworkable.serverEntities?.Find(new NetworkableId(EntityID)) as BaseEntity;
                        if (entity == null || entity.IsDestroyed)
                        {
                            position = default;
                            return false;
                        }

                        position = entity.transform.TransformPoint(Offset);
                        return true;
                    }
                    catch
                    {
                        position = default;
                        return false;
                    }
                }
            }
        }

        /// <summary>warpdata.json entry (Oxide WarpPoint).</summary>
        public class WarpPoint
        {
            [JsonProperty("Position")]
            public Vector3 Position { get; set; }

            [JsonProperty("Permission")]
            public string Permission { get; set; } = string.Empty;

            [JsonProperty("Command")]
            public string Command { get; set; } = string.Empty;

            public Vector3 ToVector3() => Position;

            public static WarpPoint FromVector3(Vector3 v) => new WarpPoint
            {
                Position = v,
                Permission = string.Empty,
                Command = string.Empty
            };

            public static implicit operator WarpPoint(Vector3Data v) =>
                v == null ? new WarpPoint() : FromVector3(v.ToVector3());
        }

        /// <summary>Legacy / helper Vector3 DTO (TeleportGUI_Data.json style).</summary>
        [Serializable]
        public class Vector3Data
        {
            [JsonProperty("x")]
            public float X { get; set; }

            [JsonProperty("y")]
            public float Y { get; set; }

            [JsonProperty("z")]
            public float Z { get; set; }

            public Vector3 ToVector3() => new Vector3(X, Y, Z);

            public static Vector3Data FromVector3(Vector3 v) => new Vector3Data { X = v.x, Y = v.y, Z = v.z };
        }

        #region Converters

        internal static Vector3 ReadVector3(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return Vector3.zero;

            if (token is JObject obj)
            {
                float x = obj.Value<float?>("x") ?? obj.Value<float?>("X") ?? 0f;
                float y = obj.Value<float?>("y") ?? obj.Value<float?>("Y") ?? 0f;
                float z = obj.Value<float?>("z") ?? obj.Value<float?>("Z") ?? 0f;
                return new Vector3(x, y, z);
            }

            return Vector3.zero;
        }

        internal static JObject WriteVector3(Vector3 v) => new JObject
        {
            ["x"] = v.x,
            ["y"] = v.y,
            ["z"] = v.z
        };

        private sealed class StringVector3DictionaryConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType) =>
                objectType == typeof(Dictionary<string, Vector3>);

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                var result = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
                if (reader.TokenType == JsonToken.Null)
                    return result;

                JObject obj = JObject.Load(reader);
                foreach (JProperty prop in obj.Properties())
                    result[prop.Name] = ReadVector3(prop.Value);

                return result;
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var dict = (Dictionary<string, Vector3>)value ?? new Dictionary<string, Vector3>();
                writer.WriteStartObject();
                foreach (KeyValuePair<string, Vector3> kvp in dict)
                {
                    writer.WritePropertyName(kvp.Key);
                    WriteVector3(kvp.Value).WriteTo(writer);
                }
                writer.WriteEndObject();
            }
        }

        private sealed class HomesDictionaryConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType) =>
                objectType == typeof(Dictionary<string, UserData.HomePoint>);

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                var result = new Dictionary<string, UserData.HomePoint>(StringComparer.OrdinalIgnoreCase);
                if (reader.TokenType == JsonToken.Null)
                    return result;

                JObject obj = JObject.Load(reader);
                foreach (JProperty prop in obj.Properties())
                    result[prop.Name] = ReadHomePoint(prop.Value);

                return result;
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var dict = (Dictionary<string, UserData.HomePoint>)value ?? new Dictionary<string, UserData.HomePoint>();
                writer.WriteStartObject();
                foreach (KeyValuePair<string, UserData.HomePoint> kvp in dict)
                {
                    writer.WritePropertyName(kvp.Key);
                    WriteHomePoint(kvp.Value).WriteTo(writer);
                }
                writer.WriteEndObject();
            }

            private static UserData.HomePoint ReadHomePoint(JToken token)
            {
                if (token == null || token.Type == JTokenType.Null)
                    return new UserData.HomePoint();

                if (token is JObject obj)
                {
                    // Canonical: Position / Offset / EntityID
                    if (obj["Position"] != null || obj["EntityID"] != null || obj["Offset"] != null)
                    {
                        return new UserData.HomePoint
                        {
                            Position = ReadVector3(obj["Position"]),
                            Offset = ReadVector3(obj["Offset"]),
                            EntityID = obj.Value<ulong?>("EntityID") ?? 0UL
                        };
                    }

                    // Legacy flat { x, y, z }
                    if (obj["x"] != null || obj["X"] != null)
                        return new UserData.HomePoint(ReadVector3(obj));
                }

                return new UserData.HomePoint();
            }

            private static JObject WriteHomePoint(UserData.HomePoint home)
            {
                home ??= new UserData.HomePoint();
                return new JObject
                {
                    ["Position"] = WriteVector3(home.Position),
                    ["Offset"] = WriteVector3(home.Offset),
                    ["EntityID"] = home.EntityID
                };
            }
        }

        private sealed class LegacyWarpPointsConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType) =>
                objectType == typeof(Dictionary<string, WarpPoint>);

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                var result = new Dictionary<string, WarpPoint>(StringComparer.OrdinalIgnoreCase);
                if (reader.TokenType == JsonToken.Null)
                    return result;

                JObject obj = JObject.Load(reader);
                foreach (JProperty prop in obj.Properties())
                    result[prop.Name] = ReadWarpPoint(prop.Value);

                return result;
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var dict = (Dictionary<string, WarpPoint>)value ?? new Dictionary<string, WarpPoint>();
                writer.WriteStartObject();
                foreach (KeyValuePair<string, WarpPoint> kvp in dict)
                {
                    writer.WritePropertyName(kvp.Key);
                    WriteWarpPoint(kvp.Value).WriteTo(writer);
                }
                writer.WriteEndObject();
            }
        }

        internal static WarpPoint ReadWarpPoint(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return new WarpPoint();

            if (token is JObject obj)
            {
                if (obj["Position"] != null)
                {
                    return new WarpPoint
                    {
                        Position = ReadVector3(obj["Position"]),
                        Permission = obj.Value<string>("Permission") ?? string.Empty,
                        Command = obj.Value<string>("Command") ?? string.Empty
                    };
                }

                // Legacy { x, y, z }
                return new WarpPoint
                {
                    Position = ReadVector3(obj),
                    Permission = obj.Value<string>("Permission") ?? string.Empty,
                    Command = obj.Value<string>("Command") ?? string.Empty
                };
            }

            return new WarpPoint();
        }

        internal static JObject WriteWarpPoint(WarpPoint warp)
        {
            warp ??= new WarpPoint();
            return new JObject
            {
                ["Position"] = WriteVector3(warp.Position),
                ["Permission"] = warp.Permission ?? string.Empty,
                ["Command"] = warp.Command ?? string.Empty
            };
        }

        #endregion
    }

    /// <summary>
    /// Root object for HarmonyData/TeleportGUI/warpdata.json
    /// (dictionary of name -> WarpPoint with Position/Permission/Command).
    /// </summary>
    [JsonConverter(typeof(TeleportGUIWarpDataConverter))]
    public class TeleportGUIWarpData : Dictionary<string, TeleportGUIData.WarpPoint>
    {
        public TeleportGUIWarpData() : base(StringComparer.OrdinalIgnoreCase) { }

        public TeleportGUIWarpData(IDictionary<string, TeleportGUIData.WarpPoint> dictionary)
            : base(dictionary, StringComparer.OrdinalIgnoreCase) { }
    }

    internal sealed class TeleportGUIWarpDataConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) =>
            objectType == typeof(TeleportGUIWarpData) ||
            objectType == typeof(Dictionary<string, TeleportGUIData.WarpPoint>);

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var result = new TeleportGUIWarpData();
            if (reader.TokenType == JsonToken.Null)
                return result;

            JObject obj = JObject.Load(reader);
            foreach (JProperty prop in obj.Properties())
                result[prop.Name] = TeleportGUIData.ReadWarpPoint(prop.Value);

            return result;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var dict = value as IDictionary<string, TeleportGUIData.WarpPoint>
                       ?? new Dictionary<string, TeleportGUIData.WarpPoint>();

            writer.WriteStartObject();
            foreach (KeyValuePair<string, TeleportGUIData.WarpPoint> kvp in dict)
            {
                writer.WritePropertyName(kvp.Key);
                TeleportGUIData.WriteWarpPoint(kvp.Value).WriteTo(writer);
            }
            writer.WriteEndObject();
        }
    }
}
