using System;
using HarmonyLib;

namespace ServerPanelHarmony.Patches
{
    /// <summary>
    /// Clients only forward ConsoleGen commands (e.g. cui.endtest). ServerPanel / PopUps CUI buttons are
    /// rewritten to "cui.endtest &lt;MARKER&gt; ..." in RustCui; this prefix routes them back to the plugin
    /// console handlers. Unknown markers fall through so other mods still receive their own payloads.
    /// </summary>
    [HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
    public static class Cui_Endtest_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg args)
        {
            var a = args?.Args;
            if (a == null || a.Length < 1) return true;

            var mod = ServerPanelHarmonyMod.Instance;
            if (mod == null) return true;

            string command = a[0].ToString() switch
            {
                "SERVERPANEL" => "UI_ServerPanel",
                "SPCLOSE" => "UI_ServerPanel_Close",
                "SPSEND" => "UI_ServerPanel_Send_Command",
                "SPVIDEO" => "serverpanel_broadcastvideo",
                "SPPOPUPS" => "UI_ServerPanel_PopUps",
                "SPPOPVIDEO" => "serverpanelpopups_broadcastvideo",
                _ => null
            };

            if (command == null) return true;

            bool popUps = command.StartsWith("UI_ServerPanel_PopUps", StringComparison.Ordinal) ||
                          command.StartsWith("serverpanelpopups", StringComparison.Ordinal);
            mod.HandleCuiMarker(args, a, command, popUps);
            return false;
        }
    }
}
