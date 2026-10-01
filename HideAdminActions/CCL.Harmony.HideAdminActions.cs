
using HarmonyLib;
using ConVar;

namespace CCL.Harmony.HideAdminActions;

[HarmonyPatch(typeof(Chat), nameof(Chat.BroadcastPlayerAction), typeof(BasePlayer), typeof(string))]
internal class __HideAdminActions_Chat_BroadcastPlayerAction_Single
{
    [HarmonyPrefix]
    private static bool Prefix(string action)
    {
        return !action.Contains("gave");
    }
}

[HarmonyPatch(typeof(Chat), nameof(Chat.BroadcastPlayerAction), typeof(BasePlayer), typeof(string), typeof(BasePlayer), typeof(string))]
internal class __HideAdminActions_Chat_BroadcastPlayerAction_Triple
{
    [HarmonyPrefix]
    private static bool Prefix(string middle, string suffix)
    {
        return !middle.Contains("gave") && !suffix.Contains("gave");
    }
}

[HarmonyPatch(typeof(Chat), nameof(Chat.Broadcast))]
internal class __HideAdminActions_Chat_Broadcast
{
    [HarmonyPrefix]
    private static bool Prefix(string message, string username)
    {
        return "SERVER" != username || !message.Contains("gave");
    }
}

[HarmonyPatch(typeof(Chat), "GetNameColor")]
internal class __HideAdminActions_Chat_GetNameColor
{
    [HarmonyPrefix]
    private static bool Prefix(ref string __result)
    {
        __result = "#5af";

        return false;
    }
}
