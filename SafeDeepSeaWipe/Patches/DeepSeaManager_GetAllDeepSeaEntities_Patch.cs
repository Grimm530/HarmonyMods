using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SafeDeepSeaWipe.Patches;

/// <summary>
/// After the game builds the set of entities to kill on Deep Sea close, remove any entity
/// that is not actually inside DeepSeaBounds. The game adds all entities in DeepSeaGroup
/// and all layer-4 visibility groups without a position check, so mainland entities (e.g.
/// with stale parent refs) can be in that set and would be wrongly killed.
/// </summary>
[HarmonyPatch(typeof(DeepSeaManager), nameof(DeepSeaManager.GetAllDeepSeaEntities), new[] { typeof(HashSet<BaseEntity>), typeof(bool) })]
public static class DeepSeaManager_GetAllDeepSeaEntities_Patch
{
    [HarmonyPostfix]
    public static void Postfix(HashSet<BaseEntity> entities)
    {
        if (entities == null || entities.Count == 0)
            return;

        List<BaseEntity> toRemove = null;
        foreach (BaseEntity ent in entities)
        {
            if (ent == null || ent.IsDestroyed)
                continue;
            if (!DeepSeaManager.IsInsideDeepSea(ent))
            {
                toRemove ??= new List<BaseEntity>();
                toRemove.Add(ent);
            }
        }

        if (toRemove != null)
        {
            foreach (BaseEntity ent in toRemove)
                entities.Remove(ent);
            Debug.Log($"[SafeDeepSeaWipe] Removed {toRemove.Count} entity/entities from Deep Sea kill list (not inside Deep Sea bounds).");
        }
    }
}
