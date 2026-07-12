using HarmonyLib;
using Thorium.Rust.HarmonyPatches.Utility;
using Thorium.Rust.Models;
using Thorium.Rust.Services;

namespace Thorium.Rust.HarmonyPatches.BasePlayer_Patch;

[HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
internal static class BasePlayer_PlayerInit_Patch
{
    [HarmonyPostfix]
    private static void Postfix(BasePlayer __instance)
    {
        try
        {
            if (!DataHandler.IsConfigured || __instance == null) return;

            DataHandler.TotalJoinPackets++;
            var joinCache = DataHandler.JoinCache;

            var ip = __instance.Connection?.ipaddress ?? string.Empty;

            ProtoBufManager.WriteBool(joinCache, true);
            ProtoBufManager.WriteString(joinCache, __instance.UserIDString ?? string.Empty);
            ProtoBufManager.WriteString(joinCache, __instance.displayName ?? string.Empty);
            ProtoBufManager.WriteString(joinCache, ip);

            var steamId = Helpers.GetSteamIdOrZero(__instance);
            if (steamId == 0) return;

            var pos = __instance.transform.position;
            AntiCheatSnapshotProcessor.Enqueue(steamId, PlayerSnapshot.Create(pos, __instance, SnapshotTypeEnums.Join, new CombatData { Weapon = ip }));
        }
        catch
        {
        }
    }
}