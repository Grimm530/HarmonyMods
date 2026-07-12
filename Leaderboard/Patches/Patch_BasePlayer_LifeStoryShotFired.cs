using HarmonyLib;

namespace Leaderboard.Patches;

/// <summary>
/// When a player fires a projectile weapon, record the ammo type as ShotFired (Fired category).
/// Called from BaseProjectile after ServerProjectileShoot. MLRS rockets are not tracked here
/// (they use a different code path).
/// </summary>
[HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.LifeStoryShotFired), new[] { typeof(BaseEntity) })]
public static class Patch_BasePlayer_LifeStoryShotFired
{
    static void Postfix(BasePlayer __instance, BaseEntity withWeapon)
    {
        if (__instance == null || !SteamIdHelper.IsSteamId(__instance.userID)) return;
        if (withWeapon is not BaseProjectile projectile) return;
        if (projectile.primaryMagazine?.ammoType == null) return;
        var shortname = projectile.primaryMagazine.ammoType.shortname;
        if (string.IsNullOrEmpty(shortname)) return;

        var mod = LeaderboardMod.Instance;
        if (mod == null) return;

        mod.RecordStat(__instance.userID, LootType.ShotFired, shortname, 1f);
    }
}
