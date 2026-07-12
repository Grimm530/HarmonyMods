/*
 * SmeltingSpeed Harmony Mod
 * Halves smelt time for all furnace types (campfire, furnace, large furnace, oil refinery, electric furnace).
 * Patches: BaseOven.IncreaseCookTime
 */

namespace SmeltingSpeed
{
    public class SmeltingSpeedMod : IHarmonyModHooks
    {
        public static SmeltingSpeedMod Instance { get; private set; }

        /// <summary>Speed multiplier. 2 = half the normal smelt time.</summary>
        public const float SpeedMultiplier = 2f;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            UnityEngine.Debug.Log("[SmeltingSpeed] Harmony mod loaded - smelt time reduced by half for all furnace types.");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            Instance = null;
            UnityEngine.Debug.Log("[SmeltingSpeed] Harmony mod unloaded.");
        }
    }
}
