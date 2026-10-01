using HarmonyLib;
using RustEditStandalone.Core;

namespace RustEditStandalone.Patches;

[HarmonyPatch(typeof(UI_LoadingScreen), nameof(UI_LoadingScreen.Update), new[] { typeof(string) })]
public static class LoadingScreen_Done_Patch
{
    static void Postfix(string strType)
    {
        if (strType != "DONE") return;
        RustEditHub.NotifyServerReady();
    }
}
