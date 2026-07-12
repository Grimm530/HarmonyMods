/*
 * BaseOven.GetSmeltingSpeed Postfix
 * Multiplies returned smelting speed by 2 to halve smelt time for all furnace types.
 */

using HarmonyLib;

namespace SmeltingSpeed.Patches
{
    [HarmonyPatch(typeof(BaseOven), nameof(BaseOven.GetSmeltingSpeed))]
    public class BaseOven_GetSmeltingSpeed_Patch
    {
        static void Postfix(ref float __result)
        {
            __result *= SmeltingSpeedMod.SpeedMultiplier;
        }
    }
}
