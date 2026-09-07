using System;
using GrimmCuiHarmony;

namespace HudHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("HUD", (_, src) => HudHarmony.HudMod.Instance?.HandleCuiEndtest(src, src.Args)); GrimmCui.RegisterJsonRewriter(GrimmCuiPatterns.CreateCommandBridgeRewriter("HUD", () => HudHarmony.HudMod.Instance?.UiConsoleCommands ?? Array.Empty<string>()));
        }
    }
}

