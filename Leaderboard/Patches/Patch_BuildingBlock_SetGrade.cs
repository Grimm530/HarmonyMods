using HarmonyLib;

namespace Leaderboard.Patches;

[HarmonyPatch(typeof(BuildingBlock), nameof(BuildingBlock.SetGrade), new[] { typeof(BuildingGrade.Enum) })]
public static class Patch_BuildingBlock_SetGrade
{
    static void Postfix(BuildingBlock __instance, BuildingGrade.Enum iGrade)
    {
        if (__instance == null || iGrade == BuildingGrade.Enum.None) return;
        var ownerId = __instance.OwnerID;
        if (ownerId == 0 || !SteamIdHelper.IsSteamId(ownerId)) return;
        var mod = LeaderboardMod.Instance;
        if (mod == null) return;
        var key = $"{__instance.ShortPrefabName ?? "block"} {iGrade.ToString().ToLowerInvariant()}";
        mod.RecordStat(ownerId, LootType.Upgrade, key, 1f);
    }
}
