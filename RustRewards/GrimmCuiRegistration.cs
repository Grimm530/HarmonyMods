using System;
using GrimmCuiHarmony;

namespace RustRewardsHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("RR", (_, src) => RustRewardsHarmonyMod.Instance?.HandleCuiEndtest(src, src.Args));
            GrimmCui.RegisterJsonRewriter(json =>
            {
                if (string.IsNullOrEmpty(json)) return json;
                string[] cmds = { "RRChangeAllZoneMult", "RRChangeZoneMult" };
                for (int i = 0; i < cmds.Length; i++)
                    json = json.Replace("\"command\":\"" + cmds[i], "\"command\":\"cui.endtest RR " + cmds[i]);
                return json;
            });
        }
    }
}

