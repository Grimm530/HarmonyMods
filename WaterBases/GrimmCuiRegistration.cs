using System;
using GrimmCuiHarmony;

namespace WaterBasesHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("WB", (_, src) => WaterBasesHarmony.WaterBasesMod.Instance?.HandleCuiEndtest(src));
            GrimmCui.RegisterJsonRewriter(GrimmCuiPatterns.CreateCommandBridgeRewriter("WB", () => WaterBasesHarmony.WaterBasesMod.Instance?.UiConsoleCommands ?? Array.Empty<string>()));
        }
    }
}
