using UnityEngine;

namespace Convoy
{
    /// <summary>Minimal notify/logging for pathfinding and event (replaces legacy NotifyManager).</summary>
    public static class ConvoyNotifyStub
    {
        public static void PrintLogMessage(string key, string arg = null)
        {
            Debug.Log("[Convoy] " + (key ?? "") + (arg != null ? " " + arg : ""));
        }

        public static void PrintWarningMessage(string key, object arg = null)
        {
            Debug.LogWarning("[Convoy] " + (key ?? "") + (arg != null ? " " + arg : ""));
        }

        public static void PrintError(BasePlayer player, string key, params object[] args)
        {
            string msg = "[Convoy] Error: " + key;
            if (args != null && args.Length > 0)
                msg += " " + string.Join(", ", args);
            Debug.LogError(msg);
        }

        public static void SendMessageToAll(string key, string prefix, string displayName, string gridStr)
        {
            Debug.Log($"[Convoy] {key}: prefix={prefix} displayName={displayName} grid={gridStr}");
        }

        public static void SendMessageToAll(string key, string prefix)
        {
            Debug.Log($"[Convoy] {key}: prefix={prefix}");
        }
    }
}
