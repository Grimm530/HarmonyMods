/*
 * IndustrialTransferSpeed Harmony Mod
 * Patches game to set IndustrialConveyor.MaxStackSizePerMove from config.
 * More performant than Oxide plugin - no constant updates, direct IL patching.
 * Config: HarmonyConfig/IndustrialTransferSpeed.json
 */

using System;
using System.Collections.Generic;
using UnityEngine;

namespace IndustrialTransferSpeed
{
    public class IndustrialTransferSpeedMod : IHarmonyModHooks
    {
        public static IndustrialTransferSpeedMod Instance { get; private set; }

        private const int VanillaDefaultMaxStackSizePerMove = 128;
        private readonly Dictionary<ulong, PlanterBox> _playerLootingPlanters = new Dictionary<ulong, PlanterBox>();

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            IndustrialTransferSpeedConfig.Load();
            PlanterProductionSettings.Load();
            ApplyToExistingConveyors();
            ApplyToExistingComposters();
            ApplyToExistingPlanters();
            Debug.Log($"[IndustrialTransferSpeed] Harmony mod loaded. MaxStackSizePerMove = {IndustrialTransferSpeedConfig.Config.MaxStackSizePerMove}. Config: HarmonyConfig/IndustrialTransferSpeed.json");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            DestroyPlanterUiForAllPlayers();
            _playerLootingPlanters.Clear();
            ResetAllConveyors();
            Instance = null;
            Debug.Log("[IndustrialTransferSpeed] Harmony mod unloaded - conveyors reset to vanilla (128).");
        }

        public void OnPlanterLootStarted(BasePlayer player, PlanterBox planter)
        {
            if (player == null || planter == null || planter.IsDestroyed)
            {
                return;
            }

            _playerLootingPlanters[player.userID] = planter;
            PlanterProductionUi.Show(player, planter);
        }

        public void OnPlanterLootEnded(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            _playerLootingPlanters.Remove(player.userID);
            PlanterProductionUi.Destroy(player);
        }

        public void HandlePlanterCuiCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg?.Player();
            if (player == null)
            {
                return;
            }

            string[] args = NormalizeCuiArgs(arg.Args);
            if (args == null || args.Length == 0)
            {
                return;
            }

            if (string.Equals(args[0], "ITS_PLANTER_CLOSE", StringComparison.OrdinalIgnoreCase))
            {
                PlanterProductionUi.Destroy(player);
                return;
            }

            if (!string.Equals(args[0], "ITS_PLANTER_MODE", StringComparison.OrdinalIgnoreCase) || args.Length < 2)
            {
                return;
            }

            if (!_playerLootingPlanters.TryGetValue(player.userID, out PlanterBox planter) || planter == null || planter.IsDestroyed)
            {
                player.ChatMessage("[IndustrialTransferSpeed] Open a planter box first.");
                return;
            }

            if (planter.OwnerID != 0UL && planter.OwnerID != player.userID && !player.CanBuild())
            {
                player.ChatMessage("[IndustrialTransferSpeed] You cannot change this planter.");
                return;
            }

            string mode = PlanterProductionSettings.NormalizeMode(args[1]);
            PlanterProductionSettings.SetMode(planter, mode);
            PlanterProductionUi.Show(player, planter);
            player.ChatMessage($"[IndustrialTransferSpeed] Planter output set to {PlanterProductionSettings.GetDisplayName(mode)}.");
        }

        private static string[] NormalizeCuiArgs(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return null;
            }

            if (args.Length == 1 && args[0].StartsWith("ITS_PLANTER_", StringComparison.OrdinalIgnoreCase))
            {
                return args[0].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            }

            return args;
        }

        private static void DestroyPlanterUiForAllPlayers()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                PlanterProductionUi.Destroy(player);
            }
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

        /// <summary>Attach industrial storage adaptors to composters that already exist.</summary>
        private static void ApplyToExistingComposters()
        {
            if (BaseNetworkable.serverEntities == null) return;

            int count = 0;
            foreach (var entity in BaseNetworkable.serverEntities)
            {
                if (entity is Composter composter && composter.IsValid())
                {
                    ComposterStorageAdaptor.EnsureAttached(composter);
                    count++;
                }
            }
            if (count > 0)
                Debug.Log($"[IndustrialTransferSpeed] Checked {count} existing composters for storage adaptors.");
        }

        /// <summary>Attach industrial storage adaptors to planters that already exist.</summary>
        private static void ApplyToExistingPlanters()
        {
            if (BaseNetworkable.serverEntities == null) return;

            int count = 0;
            foreach (var entity in BaseNetworkable.serverEntities)
            {
                if (entity is PlanterBox planter && planter.IsValid())
                {
                    ComposterStorageAdaptor.EnsureAttached(planter);
                    count++;
                }
            }
            if (count > 0)
                Debug.Log($"[IndustrialTransferSpeed] Checked {count} existing planters for storage adaptors.");
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
