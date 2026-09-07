using System;
using GrimmCuiHarmony;

namespace RustLeagueHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("RUSTLEAGUE", (_, src) => RustLeagueHarmony.RustLeagueMod.Instance?.HandleCuiCallback(src));
            GrimmCui.RegisterJsonRewriter(json =>
            {
                if (string.IsNullOrEmpty(json) || json.IndexOf("rustleague.", StringComparison.Ordinal) < 0) return json;
                return json.Replace("\"command\":\"rustleague.", "\"command\":\"cui.endtest RUSTLEAGUE rustleague.");
            });
        }
    }
}
