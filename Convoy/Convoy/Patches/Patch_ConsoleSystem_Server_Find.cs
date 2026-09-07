using Facepunch;
using HarmonyLib;

namespace Convoy.Patches
{
    /// <summary>
    /// When the game's Server.Find(strName) returns null, inject convoystart/convoystop.
    /// Applied manually from ConvoyMod — current Rust uses Find(StringView), not Find(string).
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
            var mod = ConvoyMod.Instance;
            if (mod == null) return;
            var cmd = mod.GetConvoyCommand(strName.ToString());
            if (cmd != null)
                __result = cmd;
        }
    }
}
