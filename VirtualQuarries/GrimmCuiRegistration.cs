using System;
using GrimmCuiHarmony;

namespace VirtualQuarriesHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("VIRTUALQUARRIES", (_, src) => VirtualQuarriesHarmony.VirtualQuarriesMod.Instance?.HandleCuiEndtest(src, src.Args)); GrimmCui.RegisterJsonRewriter(GrimmCuiPatterns.CreateCommandBridgeRewriter("VIRTUALQUARRIES", () => VirtualQuarriesHarmony.VirtualQuarriesMod.Instance?.UiConsoleCommands ?? Array.Empty<string>()));
        }
    }
}

