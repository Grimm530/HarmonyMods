using HarmonyLib;
using UnityEngine;

namespace RestoreItemsHarmony.Patches
{
    internal static class BaseNetworkable_Spawn_Patch
    {
        public static void Postfix(BaseNetworkable __instance)
        {
            var plugin = RestoreItemsHarmonyMod.Plugin;
            if (plugin == null) return;
            try
            {
                switch (__instance)
                {
                    case PlayerCorpse corpse:
                        plugin.DispatchOnEntitySpawned(corpse);
                        break;
                    case DroppedItemContainer dic:
                        plugin.DispatchOnEntitySpawned(dic);
                        break;
                    case DroppedItem di:
                        plugin.DispatchOnEntitySpawned(di);
                        break;
                }
            }
            catch (System.Exception ex) { Debug.LogWarning("[RestoreItems] OnEntitySpawned: " + ex.Message); }
        }
    }

    internal static class BaseNetworkable_Kill_Patch
    {
        public static void Prefix(BaseNetworkable __instance)
        {
            var plugin = RestoreItemsHarmonyMod.Plugin;
            if (plugin == null) return;
            try { plugin.DispatchOnEntityKill(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[RestoreItems] OnEntityKill: " + ex.Message); }
        }
    }
}
