using System;
using HarmonyLib;

namespace AlwaysBonus.Patches;

/// <summary>
/// Prefix patch for TreeEntity.DidHitMarker.
/// When TreeX is enabled: always returns true (always hit the X marker). Otherwise: runs original.
/// </summary>
[HarmonyPatch(typeof(TreeEntity), nameof(TreeEntity.DidHitMarker), new Type[] { typeof(HitInfo) })]
public class TreeEntity_DidHitMarker_Patch
{
    [HarmonyPrefix]
    public static bool Prefix(ref bool __result)
    {
        AlwaysBonusConfig.LoadConfig();
        if (AlwaysBonusConfig.Config?.TreeX != true)
            return true;

        __result = true;
        return false;
    }
}
