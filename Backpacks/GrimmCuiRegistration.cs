using System;
using GrimmCuiHarmony;

namespace BackpacksHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("BP", (_, src) => BackpacksHarmonyMod.Instance?.HandleCuiEndtest(src, src.Args));
            GrimmCui.RegisterJsonRewriter(json =>
            {
                if (string.IsNullOrEmpty(json)) return json;
                if (json.IndexOf("\"command\":\"backpack.", StringComparison.Ordinal) >= 0)
                    json = json.Replace("\"command\":\"backpack.", "\"command\":\"cui.endtest BP backpack.");
                if (json.IndexOf("\"command\":\"UI_Kits", StringComparison.Ordinal) >= 0)
                    json = json.Replace("\"command\":\"UI_Kits", "\"command\":\"cui.endtest KITS");
                return json;
            });
        }
    }
}
