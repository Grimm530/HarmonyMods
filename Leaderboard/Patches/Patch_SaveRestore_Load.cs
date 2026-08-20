using HarmonyLib;

namespace Leaderboard.Patches;

/// <summary>
/// WipeId is assigned during SaveRestore.Load. Harmony OnLoaded can run before that on boot,
/// so this postfix is the reliable new-save trigger.
/// </summary>
[HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Load))]
public static class Patch_SaveRestore_Load
{
    static void Postfix(bool __result)
    {
        if (!__result) return;
        LeaderboardMod.Instance?.OnWorldLoaded();
    }
}
