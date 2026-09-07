using System;
using GrimmCuiHarmony;

namespace QuestHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("QUEST", (_, src) => QuestHarmony.QuestMod.Instance?.HandleCuiEndtest(src, src.Args)); GrimmCui.RegisterJsonRewriter(GrimmCuiPatterns.CreateCommandBridgeRewriter("QUEST", () => QuestHarmony.QuestMod.Instance?.UiConsoleCommands ?? Array.Empty<string>()));
        }
    }
}

