using System;

namespace LimitEntities
{
    internal static class CompatHelpers
    {
        private const ulong MinSteamId = 76561197960265728UL;

        public static bool IsSteamId(this ulong id) => id > MinSteamId;

        public static bool IsSteamId(this string id) =>
            !string.IsNullOrEmpty(id) && ulong.TryParse(id, out var uid) && uid.IsSteamId();

        public static bool IsSteamId(this EncryptedValue<ulong> id) => ((ulong)id).IsSteamId();
    }
}
