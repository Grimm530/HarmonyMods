using System;
using GrimmCuiHarmony;

namespace HitMarkersHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("HITMARKERS", (_, src) => HitMarkersHarmony.HitMarkersMod.Instance?.HandleCuiEndtest(src, src.Args)); GrimmCui.RegisterJsonRewriter(GrimmCuiPatterns.CreateCommandBridgeRewriter("HITMARKERS", () => HitMarkersHarmony.HitMarkersMod.Instance?.UiConsoleCommands ?? Array.Empty<string>()));
        }
    }
}

