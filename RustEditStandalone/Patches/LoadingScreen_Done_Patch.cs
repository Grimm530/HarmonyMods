using HarmonyLib;

namespace RustEditStandalone.Patches;

/// <summary>
/// Run IO wiring when the loading screen reaches "DONE" (after Finalizing World / Cleaning Up),
/// so ProcessIO runs at the correct time regardless of CustomMapGen or spawn order.
/// </summary>
[HarmonyPatch(typeof(UI_LoadingScreen), nameof(UI_LoadingScreen.Update), new[] { typeof(string) })]
public static class LoadingScreen_Done_Patch
{
    private const float IoDelayAfterDone = 2f;

    static void Postfix(string strType)
    {
        if (strType != "DONE") return;
        if (RustEditStandaloneMod.Instance == null) return;

        RustEditIOProcessor.ScheduleProcessIO(IoDelayAfterDone);
    }
}
