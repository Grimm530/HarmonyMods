using HarmonyLib;

namespace DeveloperListOverride.Patches
{
    /// <summary>
    /// Ensures chat name color is orange for our override list even if the player's IsDeveloper
    /// flag wasn't set at join (e.g. config path difference or load order).
    /// </summary>
    [HarmonyPatch(typeof(ConVar.Chat), "GetNameColor", new[] { typeof(ulong), typeof(BasePlayer) })]
    public static class Chat_GetNameColor_Patch
    {
        static void Postfix(ulong userId, BasePlayer player, ref string __result)
        {
            if (DeveloperListOverrideConfig.IsOverrideDeveloper(userId.ToString()))
                __result = "#fa5"; // orange
        }
    }
}
