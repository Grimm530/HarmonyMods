using HarmonyLib;

namespace IndustrialTransferSpeed.Patches
{
    [HarmonyPatch(typeof(StorageContainer), "CanCompletePickup")]
    public static class StorageContainer_CanCompletePickup_Composter_Patch
    {
        static bool Prefix(StorageContainer __instance, ref bool __result)
        {
            if (__instance is not Composter && __instance is not PlanterBox || !HasBlockingChild(__instance))
            {
                return true;
            }

            __result = false;
            return false;
        }

        private static bool HasBlockingChild(StorageContainer container)
        {
            if (container.children == null || container.children.Count == 0)
            {
                return false;
            }

            foreach (BaseEntity child in container.children)
            {
                if (child is IndustrialStorageAdaptor adaptor && ComposterStorageAdaptor.IsManagedAdaptor(adaptor))
                {
                    continue;
                }

                if (container is PlanterBox && child is GrowableEntity)
                {
                    continue;
                }

                return true;
            }

            return false;
        }
    }
}
