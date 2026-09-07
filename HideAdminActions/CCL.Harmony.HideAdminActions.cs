
using HarmonyLib;
using ConVar;

namespace CCL.Harmony.HideAdminActions;

[HarmonyPatch(typeof(Chat), nameof(Chat.Broadcast))]
internal class __HideAdminActions_Chat_Broadcast
{
    [HarmonyPrefix]
    private static bool Prefix(string message, string username, string color, ulong userid)
    {
        // Match NoGiveNotices: block all "gave" messages from SERVER (gave themselves, gave X to Y, gave everyone, etc.)
        return "SERVER" != username || !message.Contains("gave");
    }
}

[HarmonyPatch(typeof(Chat), "GetNameColor")]
internal class __HideAdminActions_Chat_GetNameColor
{
    [HarmonyPrefix]
    private static bool Prefix(ulong userId, BasePlayer player, ref string __result)
    {
        __result = "#5af";

        return false;
    }
}
