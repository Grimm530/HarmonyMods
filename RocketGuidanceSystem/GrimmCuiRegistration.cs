using System;
using GrimmCuiHarmony;

namespace RocketGuidanceSystemHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("RGS", (_, src) => RocketGuidanceSystemHarmony.RocketGuidanceSystemMod.Instance?.HandleCuiEndtest(src, src.Args)); GrimmCui.RegisterJsonRewriter(GrimmCuiPatterns.CreateCommandBridgeRewriter("RGS", () => RocketGuidanceSystemHarmony.RocketGuidanceSystemMod.Instance?.UiConsoleCommands ?? Array.Empty<string>()));
        }
    }
}

