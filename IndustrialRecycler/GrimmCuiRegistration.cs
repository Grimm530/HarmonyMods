using System;
using GrimmCuiHarmony;

namespace IndustrialRecyclerHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("INDUSTRIALRECYCLER", (_, src) => IndustrialRecyclerHarmony.IndustrialRecyclerMod.Instance?.HandleCuiEndtest(src, src.Args)); GrimmCui.RegisterJsonRewriter(GrimmCuiPatterns.CreateCommandBridgeRewriter("INDUSTRIALRECYCLER", () => IndustrialRecyclerHarmony.IndustrialRecyclerMod.Instance?.UiConsoleCommands ?? Array.Empty<string>()));
        }
    }
}

