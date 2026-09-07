using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;
using CustomGenerator.Utility;
using Rust.Ai;

using static CustomGenerator.ExtConfig;
namespace CustomGenerator {
    [HarmonyPatch(typeof(Bootstrap), "StartupShared")]
    internal static class Bootstrap_StartupShared {
        /// <summary>
        /// Live dedicated vs CGen bake-and-quit. Facepunch consumes +server.* as ConVars, so they often
        /// never appear in GetCommandLineArgs — identity/port are the reliable signal.
        /// </summary>
        internal static bool IsLiveDedicatedServer()
        {
            try
            {
                if (!string.IsNullOrEmpty(ConVar.Server.identity))
                    return true;
            }
            catch { }
            try
            {
                if (ConVar.Server.port > 0)
                    return true;
            }
            catch { }
            try
            {
                foreach (string arg in Environment.GetCommandLineArgs())
                {
                    if (string.IsNullOrEmpty(arg)) continue;
                    if (arg.StartsWith("+server.", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }
            return false;
        }

        [HarmonyPrefix]
        private static void Prefix() {

            Logging.StartingMessage();
            
            if (Config.SkipAssetWarmup) {
                try {
                    var field = typeof(ConVar.Global).GetField("skipAssetWarmup_crashes", BindingFlags.Public | BindingFlags.Static);
                    if (field != null && field.FieldType == typeof(bool)) {
                        field.SetValue(null, true);
                        Logging.Info("Skipping asset warmup...");
                    }
                } catch (System.Exception) {
                    // ConVar was removed/renamed in this game build; skip warmup option has no effect
                }
            }

            if (IsLiveDedicatedServer())
            {
                // Game defaults: bake navmesh and wait so animals/NPCs spawn on a real mesh.
                AiManager.nav_disable = false;
                AiManager.nav_wait = true;
                Logging.Info("Live dedicated: navmesh on (nav_disable=false, nav_wait=true).");
            }
            else
            {
                AiManager.nav_disable = true;
                AiManager.nav_wait = false;
            }

            Logging.ClearOldLogs();
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (!IsLiveDedicatedServer())
                return;
            if (AiManager.nav_disable || !AiManager.nav_wait)
            {
                AiManager.nav_disable = false;
                AiManager.nav_wait = true;
                Logging.Info("Live dedicated: forced navmesh on after StartupShared.");
            }
        }
    }
}
