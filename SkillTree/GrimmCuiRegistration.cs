using System;
using GrimmCuiHarmony;

namespace SkillTreeHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("ST", (_, src) => SkillTreeHarmony.SkillTreeMod.Instance?.HandleCuiEndtest(src, src.Args)); GrimmCui.RegisterJsonRewriter(GrimmCuiPatterns.CreateCommandBridgeRewriter("ST", () => SkillTreeHarmony.SkillTreeMod.Instance?.UiConsoleCommands ?? Array.Empty<string>()));
        }
    }
}

