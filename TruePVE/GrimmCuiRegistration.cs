using System;
using GrimmCuiHarmony;

namespace TruePVEHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterJsonRewriter(json => { if (string.IsNullOrEmpty(json) || json.IndexOf("ui_buyable_", StringComparison.Ordinal) < 0) return json; return json.Replace("\"command\":\"ui_buyable_", "\"command\":\"cui.endtest RBBUI ui_buyable_"); });
        }
    }
}

