using HarmonyLib;

namespace Leaderboard.Patches;

/// <summary>
/// When a timed explosive explodes (C4, satchel, beancan, F1, molotov, flashbang, survey charge, MLRS, etc.),
/// record ExplosiveUsed for the creator so raid stats update. Matches UltimateLeaderboard OnTimedExplosiveExplode.
/// </summary>
[HarmonyPatch(typeof(TimedExplosive), nameof(TimedExplosive.Explode), new[] { typeof(UnityEngine.Vector3) })]
public static class Patch_TimedExplosive_Explode
{
    static void Postfix(TimedExplosive __instance)
    {
        if (__instance == null) return;
        if (__instance.creatorEntity is not BasePlayer player || player.IsNpc || !SteamIdHelper.IsSteamId(player.userID)) return;

        var prefab = __instance.LookupPrefab();
        if (prefab == null) return;

        var shortname = ExplosivePrefabToShortname(prefab.ShortPrefabName);
        if (string.IsNullOrEmpty(shortname)) return;

        var mod = LeaderboardMod.Instance;
        if (mod == null) return;

        mod.RecordStat(player.userID, LootType.ExplosiveUsed, shortname, 1f);
    }

    /// <summary>Map game prefab names to leaderboard shortnames (matches UltimateLeaderboard GetShortnameFromPrefab).</summary>
    private static string ExplosivePrefabToShortname(string prefabName)
    {
        if (string.IsNullOrEmpty(prefabName)) return prefabName;
        return prefabName switch
        {
            "40mm_grenade_he" => "ammo.grenadelauncher.he",
            "grenade.beancan.deployed" => "grenade.beancan",
            "grenade.f1.deployed" => "grenade.f1",
            "grenade.molotov.deployed" => "grenade.molotov",
            "grenade.flashbang.deployed" => "grenade.flashbang",
            "explosive.satchel.deployed" => "explosive.satchel",
            "explosive.timed.deployed" => "explosive.timed",
            "rocket_basic" => "ammo.rocket.basic",
            "rocket_hv" => "ammo.rocket.hv",
            "rocket_fire" => "ammo.rocket.fire",
            "survey_charge.deployed" => "surveycharge",
            _ => prefabName
        };
    }
}
