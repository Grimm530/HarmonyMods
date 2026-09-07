using System;
using GrimmCuiHarmony;

namespace Prodigy
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("PRODIGY", (_, src) =>
            {
                var a = src.Args; if (a == null || a.Length < 2) return;
                var mod = ProdigyMod.Instance; if (mod == null) return;
                var player = src.Connection?.player as BasePlayer; if (player == null) return;
                string direction = a.GetValue(1)?.ToString();
                string encodedArg = a.Length >= 3 ? a.GetValue(2)?.ToString() : null;
                mod.RunProdigyUiMove(player, direction, encodedArg);
            });
        }
    }
}

