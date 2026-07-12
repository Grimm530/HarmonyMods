using System;
using System.Collections.Generic;
using UnityEngine;

namespace GrimmNPC.NpcSpawnExtensionMethods
{
    /// <summary>
    /// Facepunch/Oxide helpers that NpcSpawn expects (List.GetRandom, IsSteamId on EncryptedValue/ulong).
    /// </summary>
    public static class GameCompatExtensions
    {
        public static T GetRandom<T>(this List<T> list)
        {
            if (list == null || list.Count == 0) return default;
            return list[UnityEngine.Random.Range(0, list.Count)];
        }

        public static T GetRandom<T>(this HashSet<T> set)
        {
            if (set == null || set.Count == 0) return default;
            int idx = UnityEngine.Random.Range(0, set.Count);
            int i = 0;
            foreach (T item in set)
            {
                if (i++ == idx) return item;
            }
            return default;
        }

        public static bool IsSteamId(this ulong id) => id > 76561197960265728UL;

        public static bool IsSteamId(this EncryptedValue<ulong> id)
        {
            try { return ((ulong)id) > 76561197960265728UL; }
            catch { return false; }
        }
    }
}
