using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NexusSelfHost.Patches
{
    /// <summary>
    /// BroadcastPlayerManifest / RefreshZoneStatus catch all exceptions and call Debug.LogException.
    /// Benign HttpClient cancellations (timeout, disconnect) produce huge repeating stacks. Downgrade to
    /// a single informational line after the ctor timeout patch; still log the first occurrence in full.
    /// </summary>
    [HarmonyPatch]
    public static class Debug_LogException_SuppressNexusHttpCanceled_Patch
    {
        private static bool _loggedNexusCanceledOnce;

        private static bool IsNexusHttpCanceled(Exception exception)
        {
            if (exception is not OperationCanceledException)
                return false;
            var stack = exception.StackTrace ?? string.Empty;
            return stack.IndexOf("NexusConnector", StringComparison.Ordinal) >= 0
                   || stack.IndexOf("NexusZoneConnector", StringComparison.Ordinal) >= 0;
        }

        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Debug), nameof(Debug.LogException), new[] { typeof(Exception) });
        }

        static bool Prefix(Exception exception)
        {
            if (!IsNexusHttpCanceled(exception))
                return true;
            if (!_loggedNexusCanceledOnce)
            {
                _loggedNexusCanceledOnce = true;
                Debug.LogWarning("[NexusSelfHost] Nexus HTTP request was canceled (timeout or disconnect). " +
                                 "Ensure Nexus API is reachable from this host (try nexus.endpoint to loopback for server traffic). " +
                                 "Suppressing further identical LogException spam.");
                return true;
            }
            return false;
        }
    }
}
