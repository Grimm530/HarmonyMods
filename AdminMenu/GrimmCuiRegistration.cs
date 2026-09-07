using System;
using GrimmCuiHarmony;

namespace AdminMenuHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("ADMINMENU", (_, src) => AdminMenuHarmonyMod.Instance?.HandleCuiCallback(src, src.Args));
            GrimmCui.RegisterJsonRewriter(GrimmCuiPatterns.CreateChaosCallbackRewriter("ADMINMENU", "adminmenu.callback"));
        }
    }
}
