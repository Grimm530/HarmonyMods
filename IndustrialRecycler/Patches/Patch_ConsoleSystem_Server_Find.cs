using Facepunch;
using HarmonyLib;

namespace IndustrialRecyclerHarmony.Patches
{
    /// <summary>
    /// Dict/GlobalDict registration is not enough for ConsoleSystem.Run (Shop / RCON).
    /// Current Rust uses Find(StringView); inject giveindustrialrecycler when Find returns null.
    /// Applied manually — same pattern as CHT / Convoy / ArmoredTrain.
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
            var mod = IndustrialRecyclerMod.Instance;
            if (mod == null) return;
            var cmd = mod.GetCommand(strName.ToString());
            if (cmd != null)
                __result = cmd;
        }
    }
}
