using System;
using GrimmCuiHarmony;

namespace JetPackHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("JETPACK", (_, src) => JetPackHarmony.JetPackMod.Instance?.HandleCuiEndtest(src, src.Args)); GrimmCui.RegisterJsonRewriter(GrimmCuiPatterns.CreateCommandBridgeRewriter("JETPACK", () => JetPackHarmony.JetPackMod.Instance?.UiConsoleCommands ?? Array.Empty<string>()));
        }
    }
}

