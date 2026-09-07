using System;
using GrimmCuiHarmony;

namespace RustVehiclesGUIHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("VGUI", (_, src) =>
            {
                var a = src.Args;
                if (a == null || a.Length < 2) return;
                var mod = RustVehiclesGUIHarmonyMod.Instance;
                if (mod?.Plugin == null) return;
                var player = src.Connection?.player as BasePlayer ?? src.Connection?.player as BasePlayer;
                if (player == null) return;
                string command = a.GetValue(1)?.ToString() ?? string.Empty;
                var rest = new string[Math.Max(0, a.Length - 2)];
                for (int i = 2; i < a.Length; i++) rest[i - 2] = a.GetValue(i)?.ToString() ?? string.Empty;
                mod.InvokeConsoleCommand(src, command, rest);
            });
            GrimmCui.RegisterJsonRewriter(json =>
            {
                if (string.IsNullOrEmpty(json) || json.IndexOf("vgui.", StringComparison.Ordinal) < 0) return json;
                return json.Replace("\"command\":\"vgui.", "\"command\":\"cui.endtest VGUI vgui.");
            });
        }
    }
}

