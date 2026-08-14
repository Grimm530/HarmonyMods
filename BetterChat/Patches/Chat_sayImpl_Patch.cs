using System;
using ConVar;
using HarmonyLib;

namespace BetterChatHarmony.Patches
{
    /// <summary>
    /// Runs after ChatFilter (Priority.First) and before ChatTranslator (Normal).
    /// Formats titles + colours and sends chat.add, then skips the original sayImpl.
    /// </summary>
    [HarmonyPatch(typeof(Chat), "sayImpl", new Type[] { typeof(Chat.ChatChannel), typeof(ConsoleSystem.Arg) })]
    [HarmonyPriority(HarmonyLib.Priority.High)]
    public static class Chat_sayImpl_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Chat.ChatChannel targetChannel, ConsoleSystem.Arg arg)
        {
            var mod = BetterChatMod.Instance;
            if (mod == null) return true;
            return mod.HandleSayImpl(targetChannel, arg);
        }
    }
}
