using Facepunch;
using HarmonyLib;

namespace DefendableHomes.Patches
{
    /// <summary>
    /// When ConsoleSystem.Index.Server.Find returns null, inject giveflare/defstop/checkfoundations.
    /// Applied manually because Find(string) was replaced with Find(StringView).
    /// </summary>
    public static class Patch_ConsoleSystem_Server_Find
    {
        public static bool TryApply(HarmonyLib.Harmony harmony)
        {
            var target = AccessTools.Method(typeof(ConsoleSystem.Index.Server), "Find", new[] { typeof(StringView) });
            if (target == null)
                return false;

            var postfix = new HarmonyMethod(typeof(Patch_ConsoleSystem_Server_Find), nameof(Postfix));
            harmony.Patch(target, postfix: postfix);
            return true;
        }

        public static void Postfix(StringView strName, ref ConsoleSystem.Command __result)
        {
            if (__result != null) return;
            var mod = DefendableHomesMod.Instance;
            if (mod == null) return;
            var cmd = mod.GetCommand(strName.ToString());
            if (cmd != null)
                __result = cmd;
        }
    }
}
