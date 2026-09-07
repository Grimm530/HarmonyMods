using System;
using GrimmCuiHarmony;

namespace InventoryViewer
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterJsonRewriter(json =>
            {
                if (string.IsNullOrEmpty(json) || json.IndexOf("command.wipe.schedule", StringComparison.Ordinal) < 0) return json;
                return json.Replace("\"command\":\"command.wipe.schedule", "\"command\":\"cui.endtest WIPESCHEDULE");
            });
        }
    }
}
