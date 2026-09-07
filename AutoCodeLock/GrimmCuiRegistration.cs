using System;
using GrimmCuiHarmony;

namespace AutoCodeLockHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("AUTOCODELOCK", (_, src) => AutoCodeLockMod.Instance?.HandleCuiCallback(src, src.Args));
            GrimmCui.RegisterJsonRewriter(GrimmCuiPatterns.CreateChaosCallbackRewriter("AUTOCODELOCK", "autocodelock.callback"));
        }
    }
}
