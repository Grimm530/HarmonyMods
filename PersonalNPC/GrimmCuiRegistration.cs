using System;
using GrimmCuiHarmony;

namespace PersonalNPCHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("PNPC", (_, src) =>
            {
                var a = src.Args; if (a == null || a.Length < 2) return;
                var player = src.Connection?.player as BasePlayer; if (player == null) return;
                var rest = new System.Collections.Generic.List<string>();
                for (int i = 2; i < a.Length; i++) rest.Add(a.GetValue(i)?.ToString() ?? string.Empty);
                PersonalNPCHarmonyMod.Instance?.DispatchConsole(a.GetValue(1)?.ToString() ?? string.Empty, rest, src.Connection);
            });
            GrimmCui.RegisterEndtest("PNPCHELPER", (_, src) =>
            {
                var a = src.Args; if (a == null || a.Length < 2) return;
                var player = src.Connection?.player as BasePlayer; if (player == null) return;
                var rest = new System.Collections.Generic.List<string>();
                for (int i = 2; i < a.Length; i++) rest.Add(a.GetValue(i)?.ToString() ?? string.Empty);
                PersonalNPCHarmonyMod.Instance?.DispatchConsole(a.GetValue(1)?.ToString() ?? string.Empty, rest, src.Connection);
            });
            GrimmCui.RegisterJsonRewriter(json =>
            {
                if (string.IsNullOrEmpty(json) || json.IndexOf("\"command\":\"pnpc", StringComparison.Ordinal) < 0) return json;
                json = json.Replace("\"command\":\"pnpchelper.", "\"command\":\"cui.endtest PNPCHELPER pnpchelper.");
                return json.Replace("\"command\":\"pnpc", "\"command\":\"cui.endtest PNPC pnpc");
            });
        }
    }
}

