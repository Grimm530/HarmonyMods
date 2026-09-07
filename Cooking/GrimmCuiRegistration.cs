using System;
using GrimmCuiHarmony;

namespace CookingHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("COOKING", (_, src) => CookingHarmony.CookingMod.Instance?.HandleCuiEndtest(src, src.Args)); GrimmCui.RegisterJsonRewriter(GrimmCuiPatterns.CreateCommandBridgeRewriter("COOKING", () => CookingHarmony.CookingMod.Instance?.UiConsoleCommands ?? Array.Empty<string>()));
        }
    }
}

