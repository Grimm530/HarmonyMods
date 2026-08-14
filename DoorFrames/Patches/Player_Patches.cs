using HarmonyLib;
using UnityEngine;

namespace DoorFramesHarmony.Patches
{
    [HarmonyPatch(typeof(BasePlayer), "OnReceiveTick")]
    internal static class BasePlayer_OnReceiveTick_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            var plugin = DoorFramesMod.Plugin;
            if (plugin == null || __instance == null) return;
            ulong uid = (ulong)__instance.userID;
            if (!plugin.IsHoldingDoorItem(uid)) return;

            try
            {
                plugin.TickPlacementIndicator(__instance);
                var input = __instance.serverInput;
                if (input == null || !input.IsDown(BUTTON.FIRE_PRIMARY)) return;
                plugin.OnPlayerInput(__instance, input);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[DoorFrames] OnPlayerInput: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.UpdateActiveItem))]
    internal static class BasePlayer_UpdateActiveItem_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            var plugin = DoorFramesMod.Plugin;
            if (plugin == null || __instance == null) return;
            try
            {
                Item current = __instance.GetActiveItem();
                plugin.OnActiveItemChanged(__instance, null, current);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[DoorFrames] OnActiveItemChanged: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    internal static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            var plugin = DoorFramesMod.Plugin;
            if (plugin == null || __instance == null) return;
            try { plugin.OnPlayerDisconnected(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[DoorFrames] OnDisconnected: " + ex.Message); }
        }
    }
}
