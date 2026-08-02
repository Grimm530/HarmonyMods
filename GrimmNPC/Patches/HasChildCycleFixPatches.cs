using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace GrimmNPC.Patches
{
    /// <summary>
    /// Prevents StackOverflowException when loading saves with circular parent references.
    /// BaseEntity.HasChild(c) walks up c's parent chain; if the save has a cycle (e.g. A parent=B, B parent=A),
    /// the recursion never terminates. This patch replaces HasChild with a cycle-safe implementation.
    /// See: .cursor/docs/SaveLoad-StackOverflow-Analysis.md
    /// </summary>
    [HarmonyPatch(typeof(BaseEntity), nameof(BaseEntity.HasChild))]
    public static class BaseEntity_HasChild_CycleFix_Patch
    {
        private const int MaxDepth = 10000; // Reasonable limit for entity hierarchy depth

        [System.ThreadStatic]
        private static HashSet<BaseEntity> _visited;

        public static bool Prefix(BaseEntity __instance, BaseEntity c, ref bool __result)
        {
            if (c == null)
            {
                __result = false;
                return false;
            }
            if (c == __instance)
            {
                __result = true;
                return false;
            }

            if (_visited == null)
                _visited = new HashSet<BaseEntity>();
            _visited.Clear();
            int depth = 0;
            BaseEntity current = c;
            while (current != null && depth < MaxDepth)
            {
                if (current == __instance)
                {
                    __result = true;
                    return false;
                }
                if (!_visited.Add(current))
                {
                    Debug.LogWarning($"[HasChildCycleFix] Circular parent reference detected (entity " + (current.net != null ? current.net.ID.ToString() : "?") + "). Treating as not a child.");
                    __result = false;
                    return false;
                }
                current = current.GetParentEntity();
                depth++;
            }
            if (depth >= MaxDepth)
            {
                Debug.LogWarning("[HasChildCycleFix] Parent chain depth exceeded " + MaxDepth + ". Treating as not a child.");
                __result = false;
                return false;
            }
            __result = false;
            return false;
        }
    }
}
