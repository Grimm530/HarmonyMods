using Facepunch;
using HarmonyLib;

namespace CHT.Patches
{
    /// <summary>
    /// Current Rust uses Find(StringView). Dict registration alone is not enough for ConsoleSystem.Run
    /// (Shop's cht.openshop). Inject CHT commands when Find returns null.
    /// </summary>
    public static class Patch_ConsoleSystem_Server_Find
    {
        public static bool TryApply(HarmonyLib.Harmony harmony)
        {
            var target = AccessTools.Method(typeof(ConsoleSystem.Index.Server), "Find", new[] { typeof(StringView) });
            if (target == null)
                return false;

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(Patch_ConsoleSystem_Server_Find), nameof(Postfix)));
            return true;
        }

        public static void Postfix(StringView strName, ref ConsoleSystem.Command __result)
        {
            if (__result != null) return;
            var mod = CHTMod.Instance;
            if (mod == null) return;
            var cmd = mod.GetCommand(strName.ToString());
            if (cmd != null)
                __result = cmd;
        }
    }
}
