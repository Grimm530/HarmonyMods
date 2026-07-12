using HarmonyLib;
using System;
using UnityEngine;

namespace BackpacksHarmony.Patches
{
    /// <summary>Optional NPC conversation hooks for GUI button hide/show.</summary>
    [HarmonyPatch(typeof(NPCTalking), nameof(NPCTalking.Server_OnConversationStarted))]
    public static class NPCTalking_BeginConversation_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(NPCTalking __instance, BasePlayer speakingTo)
        {
            try
            {
                var plugin = BackpacksHarmonyMod.Instance?.Plugin;
                if (plugin == null) return;
                if (!plugin.IsSubscribed(nameof(plugin.OnNpcConversationStart))) return;
                // ConversationData not available on this entry point; pass null.
                plugin.OnNpcConversationStart(__instance, speakingTo, null);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Backpacks] OnNpcConversationStart: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(NPCTalking), nameof(NPCTalking.Server_OnConversationEnded))]
    public static class NPCTalking_EndConversation_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(NPCTalking __instance, BasePlayer player)
        {
            try
            {
                var plugin = BackpacksHarmonyMod.Instance?.Plugin;
                if (plugin == null) return;
                if (!plugin.IsSubscribed(nameof(plugin.OnNpcConversationEnded))) return;
                plugin.OnNpcConversationEnded(__instance, player);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Backpacks] OnNpcConversationEnded: " + ex.Message);
            }
        }
    }
}
