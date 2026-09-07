using System;
using GrimmCuiHarmony;

namespace KitsHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCuiPatterns.RegisterRebuiltCommand("KITS", "UI_Kits", arg => KitsHarmonyMod.Instance?.Plugin?.CmdKitsConsole(arg));
            GrimmCui.RegisterJsonRewriter(json =>
            {
                if (string.IsNullOrEmpty(json) || json.IndexOf("UI_Kits", StringComparison.Ordinal) < 0) return json;
                return json.Replace("\"command\":\"UI_Kits", "\"command\":\"cui.endtest KITS");
            });
        }
    }
}
