using System;
using GrimmCuiHarmony;

namespace CombatClassesHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("CC", (_, src) => CombatClassesHarmony.CombatClassesMod.Instance?.HandleCuiEndtest(src));
            GrimmCui.RegisterJsonRewriter(GrimmCuiPatterns.CreateCommandBridgeRewriter("CC", () => CombatClassesHarmony.CombatClassesMod.Instance?.UiConsoleCommands ?? Array.Empty<string>()));
            GrimmCui.RegisterDragPrefix("CC_UI_", (player, name, position, type) => Harmony.Plugins.CombatClasses.Dispatch_OnCuiDraggableDrag(player, name, position, type));
        }
    }
}

