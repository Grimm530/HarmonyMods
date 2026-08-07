// Routes chat.say through the shared ChatSayBridge so Shop (/s) and SkillTree (/st)
// both work regardless of which mod's Chat.say prefix runs first.
using HarmonyChat;
using HarmonyLib;
using UnityEngine;

namespace SkillTreeHarmony.Patches
{
    [HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.say))]
    public static class Chat_Say_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Normal)]
        public static bool Prefix(ConsoleSystem.Arg arg)
        {
            if (arg == null) return true;
            string message = arg.GetString(0, "text")?.Trim();
            if (string.IsNullOrEmpty(message) || !message.StartsWith("/")) return true;

            var player = arg.Player();
            if (player == null || !player.IsConnected) return true;

            try
            {
                if (ChatSayBridge.Dispatch(player, message))
                    return false; // consumed — do not forward to default chat
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[SkillTree] Chat_Say_Patch: " + ex.Message);
            }
            return true;
        }
    }
}
