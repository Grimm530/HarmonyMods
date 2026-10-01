using HarmonyLib;
using System.Reflection;
using UnityEngine;
using CustomGenerator.Utility;

using static CustomGenerator.ExtConfig;
namespace CustomGenerator {
    [HarmonyPatch(typeof(Bootstrap), "StartupShared")]
    internal static class Bootstrap_StartupShared {
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

            Rust.Ai.AiManager.nav_disable = true;
            Rust.Ai.AiManager.nav_wait = false;

            Logging.ClearOldLogs();
        }
    }
}
