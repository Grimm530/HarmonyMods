using System.Reflection;
using HarmonyLib;
using ConVar;

namespace ChatFilter.Patches
{
    /// <summary>
    /// When ChatFilter replaced the message, arg.GetString(0, ...) returns that filtered text so
    /// ChatTranslator and Rustcord (and the game) see the cleaned message, not the original.
    /// Uses __0 so the patch works regardless of the target method's parameter names.
    /// </summary>
    [HarmonyPatch]
    public static class Arg_GetString_Patch
    {
        static MethodBase TargetMethod()
        {
            var t = typeof(ConsoleSystem.Arg);
            return t.GetMethod("GetString", new[] { typeof(int), typeof(string) });
        }

        [HarmonyPrefix]
        [HarmonyPriority(HarmonyLib.Priority.First)]
        public static bool Prefix(int __0, ref string __result)
        {
            if (__0 != 0) return true;
            var over = ChatFilter.ChatFilterMod.GetFilterOverride();
            if (over == null) return true;
            __result = over;
            return false;
        }
    }
}
