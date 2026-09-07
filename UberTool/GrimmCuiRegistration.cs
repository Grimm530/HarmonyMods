using System;
using GrimmCuiHarmony;

namespace UberToolHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("UBERTOOL", (_, src) => UberToolHarmony.UberToolMod.Instance?.HandleCuiEndtest(src, src.Args)); GrimmCui.RegisterJsonRewriter(GrimmCuiPatterns.CreateCommandBridgeRewriter("UBERTOOL", () => UberToolHarmony.UberToolMod.Instance?.UiConsoleCommands ?? Array.Empty<string>()));
        }
    }
}

