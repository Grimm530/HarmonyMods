using System;
using GrimmCuiHarmony;

namespace UpLiftedHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("UPLIFTED", (_, src) => UpLiftedHarmony.UpLiftedMod.Instance?.HandleCuiEndtest(src, src.Args)); GrimmCui.RegisterJsonRewriter(GrimmCuiPatterns.CreateCommandBridgeRewriter("UPLIFTED", () => UpLiftedHarmony.UpLiftedMod.Instance?.UiConsoleCommands ?? Array.Empty<string>()));
        }
    }
}

