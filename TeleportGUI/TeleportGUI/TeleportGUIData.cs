using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace TeleportGUI
{
    /// <summary>Root data file: users and last reset time for daily limits.</summary>
    public class TeleportGUIData
    {
        [JsonProperty("Users")]
        public Dictionary<ulong, UserData> Users { get; set; } = new Dictionary<ulong, UserData>();

        [JsonProperty("LastResetDate")]
        public string LastResetDate { get; set; } = "";

        [JsonProperty("WarpPoints")]
        public Dictionary<string, WarpPoint> WarpPoints { get; set; } = new Dictionary<string, WarpPoint>();

        public bool ShouldResetDailyUses()
        {
            if (string.IsNullOrEmpty(LastResetDate)) return true;
            try
            {
                var last = DateTime.Parse(LastResetDate, null, System.Globalization.DateTimeStyles.RoundtripKind);
                return DateTime.UtcNow.Date != last.Date;
            }
            catch { return true; }
        }

        public class UserData
        {
            [JsonProperty("Homes")]
            public Dictionary<string, HomePoint> Homes { get; set; } = new Dictionary<string, HomePoint>();

            [JsonProperty("TPUsesToday")]
            public int TPUsesToday { get; set; }
            [JsonProperty("HomeUsesToday")]
            public int HomeUsesToday { get; set; }
            [JsonProperty("WarpUsesToday")]
            public int WarpUsesToday { get; set; }

            [JsonProperty("TPCooldownUntil")]
            public double TPCooldownUntil { get; set; }
            [JsonProperty("HomeCooldownUntil")]
            public double HomeCooldownUntil { get; set; }
            [JsonProperty("WarpCooldownUntil")]
            public double WarpCooldownUntil { get; set; }

            [JsonProperty("LastOnlineTime")]
            public double LastOnlineTime { get; set; }

            [JsonProperty("TPUsage")]
            public Usage TPUsage { get; set; } = new Usage();
            [JsonProperty("HomeUsage")]
            public Usage HomeUsage { get; set; } = new Usage();
            [JsonProperty("WarpUsage")]
            public Usage WarpUsage { get; set; } = new Usage();

            [JsonProperty("ShowSleepers")]
            public bool ShowSleepers { get; set; }

            [JsonProperty("AutoAccept")]
            public AutoAcceptEnum AutoAccept { get; set; }

            public bool IsOnTPCooldown(double now) => TPCooldownUntil > now;
            public bool IsOnHomeCooldown(double now) => HomeCooldownUntil > now;
            public bool IsOnWarpCooldown(double now) => WarpCooldownUntil > now;

            public void ResetDailyUses()
            {
                TPUsesToday = 0;
                HomeUsesToday = 0;
                WarpUsesToday = 0;
                TPUsage?.Reset();
                HomeUsage?.Reset();
                WarpUsage?.Reset();
            }

            public class HomePoint
            {
                [JsonProperty("x")]
                public float X { get; set; }
                [JsonProperty("y")]
                public float Y { get; set; }
                [JsonProperty("z")]
                public float Z { get; set; }

                [JsonProperty("Offset")]
                public Vector3 Offset { get; set; }

                [JsonProperty("EntityID")]
                public ulong EntityID { get; set; }

                [JsonIgnore]
                public Vector3 Position
                {
                    get => new Vector3(X, Y, Z);
                    set
                    {
                        X = value.x;
                        Y = value.y;
                        Z = value.z;
                    }
                }

                public bool TryGetPosition(out Vector3 position)
                {
                    if (EntityID != 0UL)
                    {
                        var ent = BaseNetworkable.serverEntities.Find(new NetworkableId(EntityID)) as BaseEntity;
                        if (ent != null && !ent.IsDestroyed)
                        {
                            position = ent.transform.position + Offset;
                            return true;
                        }
                        position = default;
                        return false;
                    }

                    position = Position;
                    return !(X == 0f && Y == 0f && Z == 0f);
                }
            }

            [Flags]
            public enum AutoAcceptEnum
            {
                Clans = 1,
                Friends = 2,
                Teams = 4,
                All = 8
            }

            public class Usage
            {
                [JsonProperty("UsesToday")]
                public int UsesToday { get; set; }

                [JsonProperty("CooldownUntil")]
                public double CooldownUntil { get; set; }

                public bool IsOnCooldown(double now) => CooldownUntil > now;

                public void Reset()
                {
                    UsesToday = 0;
                }
            }
        }

        public class WarpPoint
        {
            [JsonProperty("x")]
            public float X { get; set; }
            [JsonProperty("y")]
            public float Y { get; set; }
            [JsonProperty("z")]
            public float Z { get; set; }

            [JsonProperty("Permission")]
            public string Permission { get; set; } = string.Empty;

            [JsonProperty("Command")]
            public string Command { get; set; } = string.Empty;

            [JsonIgnore]
            public Vector3 Position
            {
                get => new Vector3(X, Y, Z);
                set
                {
                    X = value.x;
                    Y = value.y;
                    Z = value.z;
                }
            }

            public static WarpPoint FromVector3(Vector3 v) => new WarpPoint { X = v.x, Y = v.y, Z = v.z };
        }

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
    }
}
