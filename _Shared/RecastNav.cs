// Shared Recast/Unity navmesh helper for Hotfix 5+.
// UnityEngine.AI.NavMesh.SamplePosition is empty when the new Recast mesh is default.
// Link this file into NPC Harmony mods (same pattern as GrimmCoreBridge.cs).

using ConVar;
using Rust.Ai.Gen2;
using Rust.Ai.Gen2.Nav;
using UnityEngine;
using UnityEngine.AI;

namespace GrimmShared
{
    internal static class RecastNav
    {
        public static bool UsesUnityMesh => AI.useUnityNavmesh;

        public static bool IsMeshReadyAt(Vector3 worldPos)
        {
            if (AI.useUnityNavmesh)
                return true;
            if (RustNavigation.Instance == null)
                return false;
            IndependantNavmesh independent = IndependantNavmesh.FindNavmeshAtPosition(worldPos);
            if (independent != null)
                return independent.IsBuilt();
            return RustNavigation.Instance.IsDefaultNavmeshBuilt();
        }

        public static bool SamplePosition(Vector3 sourcePosition, out NavMeshHit hit, float maxDistance, int areaMask)
        {
            if (AI.useUnityNavmesh)
                return NavMesh.SamplePosition(sourcePosition, out hit, maxDistance, areaMask);
            if (RustNavigation.Instance == null)
            {
                hit = default;
                return false;
            }
            return RustNavMeshHelpers.SamplePosition(sourcePosition, out hit, maxDistance, areaMask, false);
        }

        public static bool SamplePosition(Vector3 sourcePosition, out NavMeshHit hit, float maxDistance, NavMeshQueryFilter filter)
        {
            if (AI.useUnityNavmesh)
                return NavMesh.SamplePosition(sourcePosition, out hit, maxDistance, filter);
            // Recast does not use Unity areaMask/agentType pairing.
            return SamplePosition(sourcePosition, out hit, maxDistance, filter.areaMask);
        }

        public static bool CalculatePath(Vector3 source, Vector3 target, int areaMask, out Vector3[] corners, out NavMeshPathStatus status)
        {
            corners = System.Array.Empty<Vector3>();
            status = NavMeshPathStatus.PathInvalid;
            if (AI.useUnityNavmesh)
            {
                var path = new NavMeshPath();
                if (!NavMesh.CalculatePath(source, target, areaMask, path))
                    return false;
                status = path.status;
                corners = path.corners ?? System.Array.Empty<Vector3>();
                return true;
            }
            if (RustNavigation.Instance == null)
                return false;
            var rustPath = new RustNavMeshPath();
            if (!RustNavMeshHelpers.CalculatePath(source, target, areaMask, rustPath))
                return false;
            status = rustPath.status;
            if (rustPath.corners == null || rustPath.corners.Count == 0)
            {
                corners = System.Array.Empty<Vector3>();
                return true;
            }
            corners = new Vector3[rustPath.corners.Count];
            for (int i = 0; i < rustPath.corners.Count; i++)
                corners[i] = rustPath.corners[i].Value;
            return true;
        }

        public static bool Warp(BaseNavigator navigator, Vector3 worldPos)
        {
            if (navigator?.Agent == null)
                return false;
            return navigator.Agent.Warp(worldPos);
        }
    }
}
