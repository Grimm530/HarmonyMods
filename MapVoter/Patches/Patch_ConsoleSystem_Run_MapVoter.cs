using System;
using System.Reflection;
using HarmonyLib;

namespace MapVoter.Patches;

/// <summary>
/// WebRCON calls <c>ConsoleSystem.Run</c>; overloads vary by game build. Attribute-based patch on
/// <c>Run(Option, string)</c> fails with "Undefined target method" if that overload does not exist.
/// We probe for <c>string Run(Option, string)</c> or <c>string Run(Option, string, object[])</c> and skip
/// patching if neither exists (server console still works via <see cref="Patch_ServerConsole_Update"/>).
/// </summary>
internal static class ConsoleSystemRunMapVoterLogic
{
    /// <remarks>Use <c>__0</c>/<c>__1</c> in patch methods — Harmony matches originals by name; Facepunch uses <c>options</c>/<c>strCommand</c>/<c>args</c>.</remarks>
    internal static bool Prefix(ConsoleSystem.Option options, string strCommand, ref string __result)
    {
        var mod = MapVoterMod.Instance;
        if (mod == null || string.IsNullOrWhiteSpace(strCommand))
            return true;
        if (!options.IsServer)
            return true;

        if (!mod.TryHandleDedicatedMapVoterLine(strCommand, out var reply))
            return true;

        __result = reply ?? string.Empty;
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

/// <summary><c>string Run(Option, string)</c></summary>
[HarmonyPatch]
public static class Patch_ConsoleSystem_Run_MapVoter_2Arg
{
    private static MethodBase _target;

    static bool Prepare()
    {
        _target = ConsoleSystemRunMapVoterLogic.FindStringRunOverload(2);
        return _target != null;
    }

    static MethodBase TargetMethod() => _target;

    [HarmonyPrefix]
    public static bool Prefix(ConsoleSystem.Option __0, string __1, ref string __result)
        => ConsoleSystemRunMapVoterLogic.Prefix(__0, __1, ref __result);
}

/// <summary><c>string Run(Option, string, object[])</c> (e.g. params)</summary>
[HarmonyPatch]
public static class Patch_ConsoleSystem_Run_MapVoter_3Arg
{
    private static MethodBase _target;

    static bool Prepare()
    {
        _target = ConsoleSystemRunMapVoterLogic.FindStringRunOverload(3);
        return _target != null;
    }

    static MethodBase TargetMethod() => _target;

    [HarmonyPrefix]
    public static bool Prefix(ConsoleSystem.Option __0, string __1, object[] __2, ref string __result)
        => ConsoleSystemRunMapVoterLogic.Prefix(__0, __1, ref __result);
}
