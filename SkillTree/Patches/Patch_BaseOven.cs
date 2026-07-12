// OnFuelConsume / OnOvenToggle
// Method names as string literals to be resilient against game API changes.
using HarmonyLib;
using UnityEngine;
using STPlugin = Oxide.Plugins.SkillTree;

namespace SkillTreeHarmony.Patches
{
    /// <summary>OnFuelConsume — postfix on BaseOven.ConsumeFuel.</summary>
    [HarmonyPatch(typeof(BaseOven), "ConsumeFuel")]
    public static class BaseOven_ConsumeFuel_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseOven __instance, Item fuel, ItemModBurnable burnable)
        {
            if (__instance == null) return;
            try { STPlugin.Dispatch_OnFuelConsume(__instance, fuel, burnable); }
            catch (System.Exception ex) { Debug.LogWarning("[SkillTree] OnFuelConsume: " + ex.Message); }
        }
    }

    /// <summary>OnOvenToggle — postfix on BaseOven.StartCooking (replaces Toggle if method name differs).</summary>
    [HarmonyPatch(typeof(BaseOven), "StartCooking")]
    public static class BaseOven_StartCooking_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseOven __instance)
        {
            if (__instance == null) return;
            try { STPlugin.Dispatch_OnOvenToggle(__instance, null); }
            catch (System.Exception ex) { Debug.LogWarning("[SkillTree] OnOvenToggle(start): " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseOven), "StopCooking")]
    public static class BaseOven_StopCooking_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseOven __instance)
        {
            if (__instance == null) return;
            try { STPlugin.Dispatch_OnOvenToggle(__instance, null); }
            catch (System.Exception ex) { Debug.LogWarning("[SkillTree] OnOvenToggle(stop): " + ex.Message); }
        }
    }
}
