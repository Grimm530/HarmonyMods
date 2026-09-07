using System;
using GrimmCuiHarmony;

namespace StackManagerHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("STACKMANAGER", (_, src) => StackManagerHarmonyMod.Instance?.HandleCuiCallback(src, src.Args));
            GrimmCui.RegisterJsonRewriter(GrimmCuiPatterns.CreateChaosCallbackRewriter("STACKMANAGER", "stackmanager.callback"));
            GrimmCui.RegisterJsonRewriter(GrimmCuiPatterns.CreateChaosCallbackRewriter("STACKMANAGER", "stacksextended.callback"));
        }
    }
}

