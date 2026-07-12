using System;
using System.Reflection;
using Facepunch;
using HarmonyLib;

namespace MapVoter.Patches;

/// <summary>
/// When Server.Find returns null, inject MapVoter commands.
/// Applied manually — Find(string) was replaced with Find(StringView) in current Rust builds.
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
        var mod = MapVoterMod.Instance;
        if (mod == null) return;
        var cmd = mod.GetMapVoterCommand(strName.ToString());
        if (cmd != null)
            __result = cmd;
    }
}
