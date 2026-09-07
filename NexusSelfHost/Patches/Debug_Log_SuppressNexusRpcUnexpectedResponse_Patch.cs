using System;
using HarmonyLib;
using UnityEngine;

namespace NexusSelfHost.Patches
{
    /// <summary>
    /// Vanilla logs "Received an unexpected nexus RPC response (likely timed out), ignoring" during heavy load.
    /// Usually harmless. Shared state for one-time Console notice.
    /// </summary>
    internal static class NexusRpcUnexpectedResponseSuppressor
    {
        internal static bool ExplainedOnce;

        internal static bool ShouldSuppressAndNote(object message)
        {
            if (message == null)
                return false;
            var s = message as string ?? message.ToString();
            if (string.IsNullOrEmpty(s))
                return false;
            if (s.IndexOf("unexpected nexus RPC response", StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            if (s.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            if (!ExplainedOnce)
            {
                ExplainedOnce = true;
                Console.WriteLine("[NexusSelfHost] Suppressing log spam: \"unexpected nexus RPC response (likely timed out)\". Usually benign under load; Nexus HTTP timeout is extended via NexusConnector_Ctor_HttpTimeout_Patch.");
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Debug), nameof(Debug.Log), new[] { typeof(object) })]
    public static class Debug_Log_SuppressNexusRpcUnexpectedResponse_Object_Patch
    {
        static bool Prefix(object message)
        {
            return !NexusRpcUnexpectedResponseSuppressor.ShouldSuppressAndNote(message);
        }
    }

    [HarmonyPatch(typeof(Debug), nameof(Debug.Log), new[] { typeof(object), typeof(UnityEngine.Object) })]
    public static class Debug_Log_SuppressNexusRpcUnexpectedResponse_ObjectContext_Patch
    {
        static bool Prefix(object message, UnityEngine.Object context)
        {
            return !NexusRpcUnexpectedResponseSuppressor.ShouldSuppressAndNote(message);
        }
    }

    [HarmonyPatch(typeof(Debug), nameof(Debug.LogWarning), new[] { typeof(object) })]
    public static class Debug_LogWarning_SuppressNexusRpcUnexpectedResponse_Object_Patch
    {
        static bool Prefix(object message)
        {
            return !NexusRpcUnexpectedResponseSuppressor.ShouldSuppressAndNote(message);
        }
    }

    [HarmonyPatch(typeof(Debug), nameof(Debug.LogWarning), new[] { typeof(object), typeof(UnityEngine.Object) })]
    public static class Debug_LogWarning_SuppressNexusRpcUnexpectedResponse_ObjectContext_Patch
    {
        static bool Prefix(object message, UnityEngine.Object context)
        {
            return !NexusRpcUnexpectedResponseSuppressor.ShouldSuppressAndNote(message);
        }
    }
}
