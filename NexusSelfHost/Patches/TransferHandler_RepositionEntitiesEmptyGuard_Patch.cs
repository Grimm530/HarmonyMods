using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using NexusSelfHost;
using UnityEngine;

namespace NexusSelfHost.Patches
{
    /// <summary>
    /// <c>TransferHandler.RepositionEntitiesFromTransfer</c> does <c>Request.entities[0]</c> with no count check.
    /// Empty lists (e.g. after packet flood / mid-transfer disconnect) throw <see cref="ArgumentOutOfRangeException"/> and spam logs.
    /// This prefix skips the original when there are no entities. Remaining <c>Handle</c> logic still runs (no-op spawn).
    /// Disable: <c>NEXUS_GUARD_EMPTY_TRANSFER=0</c>.
    /// </summary>
    [HarmonyPatch]
    public static class TransferHandler_RepositionEntitiesEmptyGuard_Patch
    {
        private static bool _targetLoggedOnce;

        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("Rust.Nexus.Handlers.TransferHandler");
            if (t == null)
            {
                if (!_targetLoggedOnce)
                {
                    _targetLoggedOnce = true;
                    Debug.Log("[NexusSelfHost] TransferHandler not found, skipping empty-entities reposition guard.");
                }
                return null;
            }

            var m = AccessTools.Method(t, "RepositionEntitiesFromTransfer");
            if (m == null)
            {
                if (!_targetLoggedOnce)
                {
                    _targetLoggedOnce = true;
                    Debug.Log("[NexusSelfHost] TransferHandler.RepositionEntitiesFromTransfer not found, skipping empty-entities guard.");
                }
                return null;
            }

            if (!_targetLoggedOnce)
            {
                _targetLoggedOnce = true;
                Debug.Log("[NexusSelfHost] Patching TransferHandler.RepositionEntitiesFromTransfer -> skip when entities empty (disable: NEXUS_GUARD_EMPTY_TRANSFER=0).");
            }

            return m;
        }

        static bool Prefix(object __instance)
        {
            if (!NexusSelfHostOptions.GuardEmptyTransfer)
                return true;


            var request = Traverse.Create(__instance).Property("Request").GetValue();
            if (request == null)
            {
                Debug.LogWarning("[NexusSelfHost] TransferHandler.RepositionEntitiesFromTransfer: Request is null; skipping reposition.");
                return false;
            }

            var entitiesProp = AccessTools.Property(request.GetType(), "entities");
            var entities = entitiesProp?.GetValue(request);
            var count = 0;
            if (entities is ICollection coll)
                count = coll.Count;

            if (count > 0)
                return true;

            var fromZone = Traverse.Create(__instance).Property("FromZone").GetValue();
            var zoneKey = "?";
            if (fromZone != null)
            {
                var keyProp = AccessTools.Property(fromZone.GetType(), "Key");
                var keyVal = keyProp?.GetValue(fromZone);
                if (keyVal != null)
                    zoneKey = keyVal.ToString();
            }

            Debug.LogWarning("[NexusSelfHost] Skipping RepositionEntitiesFromTransfer: transfer has no entities (from zone " + zoneKey + "). Common after disconnect mid-transfer; avoids Nexus ArgumentOutOfRangeException spam.");
            return false;
        }
    }
}
