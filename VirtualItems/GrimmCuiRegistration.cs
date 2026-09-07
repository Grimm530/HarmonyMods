using System;
using GrimmCuiHarmony;

namespace VirtualItemsHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("VIRTUALITEMS", (_, src) => VirtualItemsHarmony.VirtualItemsMod.Instance?.HandleCuiEndtest(src, src.Args)); GrimmCui.RegisterJsonRewriter(GrimmCuiPatterns.CreateCommandBridgeRewriter("VIRTUALITEMS", () => VirtualItemsHarmony.VirtualItemsMod.Instance?.UiConsoleCommands ?? Array.Empty<string>()));
        }
    }
}

