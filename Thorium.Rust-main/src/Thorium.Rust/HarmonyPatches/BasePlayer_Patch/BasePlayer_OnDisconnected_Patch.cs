using HarmonyLib;
using Thorium.Rust.HarmonyPatches.Utility;
using Thorium.Rust.Models;
using Thorium.Rust.Services;

namespace Thorium.Rust.HarmonyPatches.BasePlayer_Patch;

[HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
internal static class BasePlayer_OnDisconnected_Patch
{
    [HarmonyPrefix]
    private static void Prefix(BasePlayer __instance)
    {
        try
        {
            if (!DataHandler.IsConfigured || __instance == null) return;

            DataHandler.TotalJoinPackets++;
            var joinCache = DataHandler.JoinCache;
            ProtoBufManager.WriteBool(joinCache, false);
            ProtoBufManager.WriteString(joinCache, __instance.UserIDString ?? string.Empty);

            var steamId = Helpers.GetSteamIdOrZero(__instance);
            if (steamId == 0) return;

            var pos = __instance.transform.position;
            AntiCheatSnapshotProcessor.Enqueue(steamId,
                PlayerSnapshot.Create(pos, __instance, SnapshotTypeEnums.Leave, 
                    new CombatData { Weapon = string.Empty }));

            AntiCheatSnapshotProcessor.CleanupPlayer(steamId);
        }
        catch
        {
        }
    }
}