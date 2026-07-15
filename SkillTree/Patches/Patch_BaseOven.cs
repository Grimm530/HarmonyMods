// OnFuelConsume / OnOvenToggle
// Oxide OnOvenToggle runs when a player SVSwitch'es the oven, BEFORE StartCooking/StopCooking,
// while oven.IsOn() still reflects the previous state (SkillTree Smelt_Speed depends on that).
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

    /// <summary>
    /// OnOvenToggle — prefix on BaseOven.SVSwitch (player available; IsOn() still pre-toggle).
    /// Do not patch StartCooking/StopCooking: those often have no player and run after IsOn flips.
    /// </summary>
    [HarmonyPatch(typeof(BaseOven), "SVSwitch", new[] { typeof(BaseEntity.RPCMessage) })]
    public static class BaseOven_SVSwitch_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BaseOven __instance, BaseEntity.RPCMessage msg)
        {
            if (__instance == null) return;
            BasePlayer player = msg.player;
            if (player == null) return;
            try { STPlugin.Dispatch_OnOvenToggle(__instance, player); }
            catch (System.Exception ex) { Debug.LogWarning("[SkillTree] OnOvenToggle: " + ex.Message); }
        }
    }
}
