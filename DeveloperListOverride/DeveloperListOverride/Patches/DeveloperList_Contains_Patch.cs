using HarmonyLib;

namespace DeveloperListOverride.Patches
{
    /// <summary>
    /// When DeveloperList.Contains(steamid) is called, also return true if the Steam ID is in our config list.
    /// This grants orange name, auth level 3, and developer tools to configured Steam IDs.
    /// </summary>
    [HarmonyPatch(typeof(DeveloperList), nameof(DeveloperList.Contains), new[] { typeof(string) })]
    public static class DeveloperList_Contains_String_Patch
    {
        static bool Prefix(string steamid, ref bool __result)
        {
            if (DeveloperListOverrideConfig.IsOverrideDeveloper(steamid))
            {
                __result = true;
                return false; // skip original
            }
            return true; // run original
        }
    }
}
