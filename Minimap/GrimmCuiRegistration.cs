using System;
using GrimmCuiHarmony;

namespace MinimapHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("MINIMAP", (_, src) => MinimapHarmonyMod.Instance?.HandleCuiCallback(src, src.Args));
            GrimmCui.RegisterJsonRewriter(GrimmCuiPatterns.CreateChaosCallbackRewriter("MINIMAP", "minimap.callback"));
        }
    }
}

