using HarmonyLib;

namespace Leaderboard.Patches;

[HarmonyPatch(typeof(BaseEntity), nameof(BaseEntity.OnPlaced), new[] { typeof(BasePlayer) })]
public static class Patch_BaseEntity_OnPlaced
{
    static void Postfix(BaseEntity __instance, BasePlayer player)
    {
        if (__instance == null || player == null || !SteamIdHelper.IsSteamId(player.userID)) return;
        var mod = LeaderboardMod.Instance;
        if (mod == null) return;

        var prefab = __instance.ShortPrefabName;
        if (string.IsNullOrEmpty(prefab)) return;
        mod.RecordStat(player.userID, LootType.Construction, prefab, 1f);
    }
}
