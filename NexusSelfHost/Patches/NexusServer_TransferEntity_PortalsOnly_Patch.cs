using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using NexusSelfHost;
using UnityEngine;

namespace NexusSelfHost.Patches
{
    /// <summary>
    /// Blocks vanilla Nexus travel via <c>ocean</c> (Nexus island map triggers) and <c>ferry</c> so players
    /// typically only change zones through Oxide <c>Portals.cs</c> (and admin <c>nexus.transfer</c>), which use <c>console</c>.
    /// Opt out: <c>NEXUS_PORTALS_ONLY_TRANSFER=0</c>.
    /// </summary>
    [HarmonyPatch]
    public static class NexusServer_TransferEntity_PortalsOnly_Patch
    {
        private static bool _loggedOnce;
        private static bool _loggedBlockedOnce;

        static MethodBase TargetMethod()
        {
            var ns = AccessTools.TypeByName("NexusServer");
            var be = AccessTools.TypeByName("BaseEntity");
            if (ns == null || be == null)
            {
                if (!_loggedOnce)
                {
                    _loggedOnce = true;
                    Debug.Log("[NexusSelfHost] NexusServer or BaseEntity not found, skipping TransferEntity portals-only patch.");
                }
                return null;
            }

            var m = AccessTools.Method(ns, "TransferEntity", new[] { be, typeof(string), typeof(string), typeof(bool) });
            if (m == null)
            {
                if (!_loggedOnce)
                {
                    _loggedOnce = true;
                    Debug.Log("[NexusSelfHost] NexusServer.TransferEntity not found, skipping portals-only patch.");
                }
                return null;
            }

            if (!_loggedOnce)
            {
                _loggedOnce = true;
                Debug.Log("[NexusSelfHost] Patching NexusServer.TransferEntity: block ocean/ferry unless NEXUS_PORTALS_ONLY_TRANSFER=0 (console transfers unchanged).");
            }

            return m;
        }

        static bool Prefix(object entity, ref string toZoneKey, ref string method, ref bool includeFerry, ref Task __result)
        {
            if (!NexusSelfHostOptions.BlockOceanFerryTransfers)
                return true;

            var m = method ?? string.Empty;
            if (string.Equals(m, "ferry", StringComparison.OrdinalIgnoreCase)
                || string.Equals(m, "ocean", StringComparison.OrdinalIgnoreCase))
            {
                if (!_loggedBlockedOnce)
                {
                    _loggedBlockedOnce = true;
                    var who = entity == null ? "null" : entity.GetType().Name;
                    Debug.Log("[NexusSelfHost] Suppressing vanilla Nexus ocean/ferry transfers while portals-only mode is enabled. First blocked transfer: method=" + m + " entity=" + who + ".");
                }

                __result = Task.CompletedTask;
                return false;
            }

            return true;
        }
    }
}
