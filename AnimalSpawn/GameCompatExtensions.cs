using System.Collections.Generic;
using UnityEngine;

namespace AnimalSpawn.AnimalSpawnExtensionMethods
{
    public static class GameCompatExtensions
    {
        public static TSource First<TSource>(this IList<TSource> source) => source[0];

        public static TSource Last<TSource>(this IList<TSource> source) => source[source.Count - 1];

        public static bool IsPlayer(this BasePlayer player) => player != null && ((ulong)player.userID).IsSteamId();

        public static bool IsExists(this BaseNetworkable entity) => entity != null && !entity.IsDestroyed;

        public static bool IsEqualVector3(this Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 0.1f;

        public static bool IsSteamId(this ulong id) => id >= 76561197960265728UL;

        public static bool IsSteamId(this EncryptedValue<ulong> id)
        {
            try { return ((ulong)id).IsSteamId(); }
            catch
            {
                try { return id.Get().IsSteamId(); }
                catch { return false; }
            }
        }
    }
}
