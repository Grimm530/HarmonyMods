using System;
using GrimmCuiHarmony;

namespace BradleyDropsHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("BRADLEYDROPS", (_, src) => BradleyDropsHarmony.BradleyDropsMod.Instance?.HandleCuiEndtest(src, src.Args)); GrimmCui.RegisterJsonRewriter(GrimmCuiPatterns.CreateCommandBridgeRewriter("BRADLEYDROPS", () => BradleyDropsHarmony.BradleyDropsMod.Instance?.UiConsoleCommands ?? Array.Empty<string>()));
        }
    }
}

