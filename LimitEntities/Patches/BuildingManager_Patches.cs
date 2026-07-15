using HarmonyLib;
using Building = BuildingManager.Building;

namespace LimitEntities.Patches
{
    /// <summary>
    /// ServerBuildingManager.Merge / Split are private; Harmony patches by name.
    /// Mirrors Oxide OnBuildingMerge / OnBuildingSplit.
    /// </summary>
    [HarmonyPatch(typeof(ServerBuildingManager), "Merge")]
    internal static class ServerBuildingManager_Merge_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Building building1, Building building2)
        {
            var service = LimitEntitiesMod.Service;
            if (service == null || !service.IsReady || building1 == null || building2 == null) return;
            // Oxide: OnBuildingMerge(manager, to, from) => to=building1, from=building2
            service.OnBuildingMerge(building2.ID, building1.ID);
        }
    }

    [HarmonyPatch(typeof(ServerBuildingManager), "Split")]
    internal static class ServerBuildingManager_Split_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Building oldBuilding, out uint __state)
        {
            __state = oldBuilding != null ? oldBuilding.ID : 0u;
        }

        [HarmonyPostfix]
        private static void Postfix(uint __state)
        {
            if (__state == 0u) return;
            var service = LimitEntitiesMod.Service;
            if (service == null || !service.IsReady) return;
            service.OnBuildingSplit(__state);
        }
    }
}
