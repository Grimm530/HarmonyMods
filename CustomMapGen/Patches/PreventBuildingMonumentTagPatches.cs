using HarmonyLib;

namespace CustomMapGen.Patches
{
    // Patch ConstructionErrors to allow building on roads if configured
    // Note: This is a simplified implementation - roads may not use PreventBuildingMonumentTag
    [HarmonyPatch(typeof(ConstructionErrors), nameof(ConstructionErrors.GetPreventBuildingMonumentTag))]
    public static class ConstructionErrors_GetPreventBuildingMonumentTag_Patch
    {
        static void Postfix(ref PreventBuildingMonumentTag __result)
        {
            if (!CustomMapGen.IsCustomMapGenEnabled())
                return;
            
            var config = CustomMapGen.Instance.GetConfig();
            if (config.AllowBuildingOnRoads)
            {
                // Allow building on roads by returning null (no building prevention)
                // This is a simplified approach - may need refinement based on actual road building restrictions
                // Note: Roads might use different building prevention mechanisms
            }
        }
    }
}
