using System;
using GrimmCuiHarmony;

namespace WipeScheduleHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCuiPatterns.RegisterRebuiltCommand("WIPESCHEDULE", "command.wipe.schedule", arg => WipeScheduleHarmonyMod.Instance?.Plugin?.CmdConsoleWipeSchedule(arg));
            GrimmCui.RegisterJsonRewriter(json =>
            {
                if (string.IsNullOrEmpty(json) || json.IndexOf("command.wipe.schedule", StringComparison.Ordinal) < 0) return json;
                return json.Replace("\"command\":\"command.wipe.schedule", "\"command\":\"cui.endtest WIPESCHEDULE");
            });
        }
    }
}

