using System.Reflection;
using HarmonyLib;

namespace IndustrialRecyclerHarmony.Patches
{
    /// <summary>
    /// WebRCON / some console paths call ConsoleSystem.Run with the full line as strCommand.
    /// Handle giveindustrialrecycler here so Facepunch never looks up the whole string as a name.
    /// </summary>
    internal static class ConsoleSystemRunIndustrialRecyclerLogic
    {
        internal static bool Prefix(ConsoleSystem.Option options, string strCommand, ref string __result)
        {
            var mod = IndustrialRecyclerMod.Instance;
            if (mod == null || string.IsNullOrWhiteSpace(strCommand))
                return true;
            if (!options.IsServer)
                return true;
            if (!mod.TryRunServerConsoleCommand(strCommand))
                return true;
            __result = string.Empty;
            return false;
        }

        internal static MethodBase FindStringRunOverload(int paramCount)
        {
            var opt = typeof(ConsoleSystem.Option);
            foreach (var m in typeof(ConsoleSystem).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name != "Run" || m.ReturnType != typeof(string))
                    continue;
                var p = m.GetParameters();
                if (p.Length != paramCount)
                    continue;
                if (p[0].ParameterType != opt || p[1].ParameterType != typeof(string))
                    continue;
                if (paramCount == 3 && p[2].ParameterType != typeof(object[]))
                    continue;
                return m;
            }
            return null;
        }
    }

    [HarmonyPatch]
    public static class Patch_ConsoleSystem_Run_IndustrialRecycler_2Arg
    {
        private static MethodBase _target;

        static bool Prepare()
        {
            _target = ConsoleSystemRunIndustrialRecyclerLogic.FindStringRunOverload(2);
            return _target != null;
        }

        static MethodBase TargetMethod() => _target;

        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Option __0, string __1, ref string __result)
            => ConsoleSystemRunIndustrialRecyclerLogic.Prefix(__0, __1, ref __result);
    }

    [HarmonyPatch]
    public static class Patch_ConsoleSystem_Run_IndustrialRecycler_3Arg
    {
        private static MethodBase _target;

        static bool Prepare()
        {
            _target = ConsoleSystemRunIndustrialRecyclerLogic.FindStringRunOverload(3);
            return _target != null;
        }

        static MethodBase TargetMethod() => _target;

        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Option __0, string __1, object[] __2, ref string __result)
            => ConsoleSystemRunIndustrialRecyclerLogic.Prefix(__0, __1, ref __result);
    }
}
