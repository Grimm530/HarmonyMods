using HarmonyLib;

namespace DeveloperListOverride.Patches
{
    /// <summary>
    /// DeveloperList.Contains(ulong) calls Contains(string). Patch both so auth,
    /// queue skip, protocol bypass, and PlayerInit all see our Steam IDs.
    /// </summary>
    [HarmonyPatch(typeof(DeveloperList), nameof(DeveloperList.Contains), new[] { typeof(string) })]
    public static class DeveloperList_Contains_String_Patch
    {
        static bool Prefix(string steamid, ref bool __result)
        {
            if (!DeveloperListOverrideConfig.IsOverrideDeveloper(steamid))
                return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(DeveloperList), nameof(DeveloperList.Contains), new[] { typeof(ulong) })]
    public static class DeveloperList_Contains_Ulong_Patch
    {
        static bool Prefix(ulong steamid, ref bool __result)
        {
            if (!DeveloperListOverrideConfig.IsOverrideDeveloper(steamid.ToString()))
                return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(DeveloperList), nameof(DeveloperList.IsDeveloper))]
    public static class DeveloperList_IsDeveloper_Patch
    {
        static bool Prefix(BasePlayer ply, ref bool __result)
        {
            if (ply == null || !DeveloperListOverrideConfig.IsOverrideDeveloper(ply.UserIDString))
                return true;
            __result = true;
            return false;
        }
    }
}
