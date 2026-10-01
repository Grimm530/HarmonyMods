using HarmonyLib;
using RustEditStandalone.Core;

namespace RustEditStandalone.Patches;

public static class BaseNetworkable_Kill_Patch
{
    public static void Prefix(BaseNetworkable __instance)
    {
        RustEditHub.NotifyEntityKilled(__instance);
    }
}

[HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Load))]
public static class SaveRestore_Load_Patch
{
    static void Postfix()
    {
        RustEditHub.NotifySaveLoaded();
    }
}
