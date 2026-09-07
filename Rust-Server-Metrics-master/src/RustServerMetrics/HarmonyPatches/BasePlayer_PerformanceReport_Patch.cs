using HarmonyLib;
using Network;
using Newtonsoft.Json;
using System;

namespace RustServerMetrics.HarmonyPatches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PerformanceReport))]
    public class BasePlayer_PerformanceReport_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseEntity.RPCMessage msg)
        {
            if (msg.read == null)
                return true;

            long originalPosition = msg.read.Position;
            try
            {
                // Keep stream alignment exactly as original code expects.
                _ = msg.read.String();
                string rawJson = msg.read.StringRaw();
                var report = DeserializeClientPerformanceReport(rawJson);
                if (!report.HasValue)
                {
                    return true;
                }

                var logger = SingletonComponent<MetricsLogger>.Instance;
                if (logger != null && logger.OnClientPerformanceReport(report.Value))
                {
                    // Original method exits early when this returns true.
                    return false;
                }
            }
            catch
            {
                // Ignore parse/read failures and let vanilla handler process as usual.
            }
            finally
            {
                msg.read.Position = originalPosition;
            }

            return true;
        }

        private static ClientPerformanceReport? DeserializeClientPerformanceReport(string rawJson)
        {
            if (string.IsNullOrEmpty(rawJson))
                return null;

            try
            {
                return JsonConvert.DeserializeObject<ClientPerformanceReport>(rawJson);
            }
            catch
            {
                return null;
            }
        }
    }
}