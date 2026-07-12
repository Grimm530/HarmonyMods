/*
 * Invokes RaidableBases OnEntitySpawned hook. Mod has overloads for TimedExplosive, FireBall, DroppedItemContainer, BaseLock, PlayerCorpse; others are no-op when not subscribed.
 */
using HarmonyLib;
using UnityEngine;

namespace RaidableBases
{
    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Spawn))]
    internal static class BaseNetworkable_Spawn_Patch
    {
        [HarmonyPostfix]
        static void Postfix(BaseNetworkable __instance)
        {
            if (__instance == null)
                return;
            Interface.CallHook("OnEntitySpawned", __instance);
        }
    }
}
