using HarmonyLib;
using RustEditStandalone.Core;

namespace RustEditStandalone.Patches;

[HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Spawn))]
public static class BaseNetworkable_Spawn_Patch
{
    static void Postfix(BaseNetworkable __instance)
    {
        RustEditHub.NotifyEntitySpawned(__instance);
    }
}

[HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill))]
public static class BaseNetworkable_Kill_Patch
{
    static void Prefix(BaseNetworkable __instance)
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
