using HarmonyLib;

namespace IndustrialTransferSpeed.Patches
{
    [HarmonyPatch(typeof(PlanterBox), nameof(PlanterBox.ServerInit))]
    public static class PlanterBox_ServerInit_AttachStorageAdaptor_Patch
    {
        static void Postfix(PlanterBox __instance)
        {
            ComposterStorageAdaptor.EnsureAttached(__instance);
        }
    }
}
