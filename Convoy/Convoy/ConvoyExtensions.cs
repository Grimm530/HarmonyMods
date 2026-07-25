using System;
using System.Collections.Generic;
using UnityEngine;
using Facepunch;

namespace Convoy
{
    /// <summary>Extensions used by the Convoy port (replaces Oxide.Plugins.ConvoyExtensionMethods).</summary>
    public static class ConvoyExtensions
    {
        public static bool IsExists(this BaseNetworkable entity) => entity != null && !entity.IsDestroyed;

        /// <summary>Steam player check (EncryptedValue-safe). Oxide Convoy parity: userID.IsSteamId().</summary>
        public static bool IsRealPlayer(this BasePlayer player)
        {
            if (player == null) return false;
            // Cast EncryptedValue<ulong> before threshold check (Harmony_Mod_Execution_Framework §14).
            return ((ulong)player.userID) >= 76561197960265728UL;
        }

        /// <summary>Parse string like "(x, y, z)" or "x, y, z" to Vector3.</summary>
        public static Vector3 ToVector3(this string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return Vector3.zero;
            string t = s.Trim().TrimStart('(').TrimEnd(')');
            string[] parts = t.Split(',');
            if (parts.Length != 3) return Vector3.zero;
            float x = float.TryParse(parts[0].Trim(), out float vx) ? vx : 0f;
            float y = float.TryParse(parts[1].Trim(), out float vy) ? vy : 0f;
            float z = float.TryParse(parts[2].Trim(), out float vz) ? vz : 0f;
            return new Vector3(x, y, z);
        }

        private static readonly System.Random _rnd = new System.Random();

        public static T GetRandom<T>(this IList<T> list)
        {
            if (list == null || list.Count == 0) return default;
            return list[_rnd.Next(list.Count)];
        }

        public static T GetRandom<T>(this HashSet<T> set)
        {
            if (set == null || set.Count == 0) return default;
            int i = _rnd.Next(set.Count);
            int n = 0;
            foreach (T x in set)
            {
                if (n == i) return x;
                n++;
            }
            return default;
        }
    }
}
