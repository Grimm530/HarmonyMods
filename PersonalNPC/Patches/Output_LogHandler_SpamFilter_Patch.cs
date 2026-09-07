using HarmonyLib;
using UnityEngine;

namespace PersonalNPCHarmony.Patches
{
    /// <summary>
    /// Filters PersonalNPC's "ownerPlayer is not player" spam without touching
    /// Application.logMessageReceived. Hijacking that event (old clearConsoleOfSpam)
    /// left Facepunch.Output.LogHandler still subscribed while also calling it from
    /// HandleLog — ServerConsole.OnMessage then printed every line twice, while the
    /// Unity logfile stayed single.
    /// </summary>
    [HarmonyPatch(typeof(Facepunch.Output), nameof(Facepunch.Output.LogHandler))]
    internal static class Output_LogHandler_SpamFilter_Patch
    {
        private static bool Prefix(string log)
        {
            if (string.IsNullOrEmpty(log))
                return true;

            var plugin = PersonalNPC.Instance;
            if (plugin == null || !plugin.ClearConsoleOfSpam)
                return true;

            if (log.Contains("ownerPlayer is not player"))
                return false;

            return true;
        }
    }
}
