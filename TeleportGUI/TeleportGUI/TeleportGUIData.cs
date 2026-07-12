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
        public Dictionary<string, Vector3Data> WarpPoints { get; set; } = new Dictionary<string, Vector3Data>();

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
            public Dictionary<string, Vector3Data> Homes { get; set; } = new Dictionary<string, Vector3Data>();

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

            public bool IsOnTPCooldown(double now) => TPCooldownUntil > now;
            public bool IsOnHomeCooldown(double now) => HomeCooldownUntil > now;
            public bool IsOnWarpCooldown(double now) => WarpCooldownUntil > now;

            public void ResetDailyUses()
            {
                TPUsesToday = 0;
                HomeUsesToday = 0;
                WarpUsesToday = 0;
            }
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
