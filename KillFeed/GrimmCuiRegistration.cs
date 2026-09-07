using System;
using GrimmCuiHarmony;

namespace KillFeedHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("KILLFEED", (_, src) => KillFeedHarmony.KillFeedMod.Instance?.HandleCuiEndtest(src, src.Args)); GrimmCui.RegisterJsonRewriter(GrimmCuiPatterns.CreateCommandBridgeRewriter("KILLFEED", () => KillFeedHarmony.KillFeedMod.Instance?.UiConsoleCommands ?? Array.Empty<string>()));
        }
    }
}

