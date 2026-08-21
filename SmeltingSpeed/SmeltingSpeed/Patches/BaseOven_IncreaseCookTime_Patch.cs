/*
 * BaseOven.IncreaseCookTime Prefix
 * Multiplies cook progress before items receive their cooking cycle.
 */

using HarmonyLib;

namespace SmeltingSpeed.Patches
{
    [HarmonyPatch(typeof(BaseOven), "IncreaseCookTime")]
    public class BaseOven_IncreaseCookTime_Patch
    {
        static void Prefix(ref float amount)
        {
            amount *= SmeltingSpeedMod.SpeedMultiplier;
        }
    }
}
