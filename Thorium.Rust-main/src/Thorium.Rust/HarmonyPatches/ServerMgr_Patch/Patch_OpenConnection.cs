using HarmonyLib;

namespace Thorium.Rust.HarmonyPatches.ServerMgr_Patch;

[HarmonyPatch(typeof(ServerMgr), nameof(ServerMgr.OpenConnection))]
internal class Patch_OpenConnection
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        ThoriumLoader.OnServerStarted();
    }
}