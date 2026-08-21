/*
 * IndustrialTransferSpeed Harmony Mod
 * Patches game to set IndustrialConveyor.MaxStackSizePerMove from config.
 * Conveyor speed only — no planter/composter adaptor farming features.
 * Config: HarmonyConfig/IndustrialTransferSpeed.json
 */

using UnityEngine;

namespace IndustrialTransferSpeed
{
    public class IndustrialTransferSpeedMod : IHarmonyModHooks
    {
        public static IndustrialTransferSpeedMod Instance { get; private set; }

        private const int VanillaDefaultMaxStackSizePerMove = 128;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            IndustrialTransferSpeedConfig.Load();
            ApplyToExistingConveyors();
            Debug.Log($"[IndustrialTransferSpeed] Harmony mod loaded. MaxStackSizePerMove = {IndustrialTransferSpeedConfig.Config.MaxStackSizePerMove}. Config: HarmonyConfig/IndustrialTransferSpeed.json");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            ResetAllConveyors();
            Instance = null;
            Debug.Log("[IndustrialTransferSpeed] Harmony mod unloaded - conveyors reset to vanilla (128).");
        }

        /// <summary>Apply config to conveyors that already exist (e.g. mod loaded via harmony.load after server start).</summary>
        private static void ApplyToExistingConveyors()
        {
            if (BaseNetworkable.serverEntities == null) return;

            int count = 0;
            foreach (var entity in BaseNetworkable.serverEntities)
            {
                if (entity is IndustrialConveyor conveyor && conveyor.IsValid())
                {
                    conveyor.MaxStackSizePerMove = IndustrialTransferSpeedConfig.Config.MaxStackSizePerMove;
                    count++;
                }
            }
            if (count > 0)
                Debug.Log($"[IndustrialTransferSpeed] Applied to {count} existing conveyors.");
        }

        /// <summary>Reset all conveyors to vanilla value when mod unloads.</summary>
        private static void ResetAllConveyors()
        {
            if (BaseNetworkable.serverEntities == null) return;

            foreach (var entity in BaseNetworkable.serverEntities)
            {
                if (entity is IndustrialConveyor conveyor && conveyor.IsValid())
                {
                    conveyor.MaxStackSizePerMove = VanillaDefaultMaxStackSizePerMove;
                }
            }
        }
    }
}
