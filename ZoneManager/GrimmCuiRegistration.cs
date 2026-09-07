using System;
using GrimmCuiHarmony;

namespace ZoneManagerHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("ZONEMANAGER", (_, src) => ZoneManagerHarmony.ZoneManagerMod.Instance?.HandleCuiEndtest(src, src.Args));
            GrimmCui.RegisterJsonRewriter(json =>
            {
                if (string.IsNullOrEmpty(json) || json.IndexOf("zmui.editflag", StringComparison.Ordinal) < 0) return json;
                return json.Replace("\"command\":\"zmui.editflag", "\"command\":\"cui.endtest ZONEMANAGER zmui.editflag");
            });
        }
    }
}

