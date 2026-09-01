using HarmonyLib;

namespace IndustrialTransferSpeed.Patches
{
    [HarmonyPatch(typeof(Composter), nameof(Composter.ServerInit))]
    public static class Composter_ServerInit_AttachStorageAdaptor_Patch
    {
        static void Postfix(Composter __instance)
        {
            ComposterStorageAdaptor.EnsureAttached(__instance);
        }
    }
}
