using System;
using GrimmCuiHarmony;

namespace RaidableBases
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCuiPatterns.RegisterConsoleRouter("RBUI", 1);
            GrimmCui.RegisterDragPrefix("RB_UI_", (player, name, position, type) =>
                RaidableBasesHost.Instance?.ModInstance?.DispatchOnCuiDraggableDrag(player, name, position, type));
            GrimmCui.RegisterJsonRewriter(json =>
            {
                if (string.IsNullOrEmpty(json)) return json;
                if (json.IndexOf("ui_buyraid", StringComparison.Ordinal) >= 0)
                    json = json.Replace("\"command\":\"ui_buyraid", "\"command\":\"cui.endtest RBUI ui_buyraid");
                if (json.IndexOf("rb_ui_move", StringComparison.Ordinal) >= 0)
                    json = json.Replace("\"command\":\"rb_ui_move", "\"command\":\"cui.endtest RBUI rb_ui_move");
                return json;
            });
        }
    }
}

