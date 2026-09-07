namespace Leaderboard.Patches;

internal static class GrimmCoreHurtRegistration
{
    private const string ModId = "Leaderboard";

    internal static void Register() => GrimmCoreBridge.RegisterHurtPostfix(ModId, 143, Postfix);
    internal static void Unregister() => GrimmCoreBridge.UnregisterHurtMod(ModId);

    private static void Postfix(BaseCombatEntity entity, HitInfo info)
    {
        if (info?.HitEntity != entity) return;
        if (entity is not BasePlayer victim) return;

        var attacker = info.InitiatorPlayer;
        if (attacker == null || attacker.IsNpc || attacker == victim) return;
        if (!SteamIdHelper.IsSteamId(attacker.userID) || !SteamIdHelper.IsSteamId(victim.userID)) return;

        var mod = LeaderboardMod.Instance;
        if (mod == null) return;
        if (!info.hasDamage) return;

        string key = HitAreaToKey(info.boneArea);
        if (string.IsNullOrEmpty(key)) return;

        mod.RecordStat(attacker.userID, LootType.BodyHits, key, 1f);
    }

    private static string HitAreaToKey(HitArea area)
    {
        if ((area & HitArea.Head) != 0) return "head";
        if ((area & HitArea.Chest) != 0) return "chest";
        if ((area & HitArea.Stomach) != 0) return "stomach";
        if ((area & HitArea.Arm) != 0 || (area & HitArea.Hand) != 0) return "arm";
        if ((area & HitArea.Leg) != 0 || (area & HitArea.Foot) != 0) return "leg";
        return null;
    }
}
