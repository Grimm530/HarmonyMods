using System;
using GrimmCuiHarmony;

namespace ServerPanelHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            void Route(string marker, string command, bool popUps) =>
                GrimmCui.RegisterEndtest(marker, (_, src) => ServerPanelHarmonyMod.Instance?.HandleCuiMarker(src, src.Args, command, popUps));
            Route("SERVERPANEL", "UI_ServerPanel", false);
            Route("SPCLOSE", "UI_ServerPanel_Close", false);
            Route("SPSEND", "UI_ServerPanel_Send_Command", false);
            Route("SPVIDEO", "serverpanel_broadcastvideo", false);
            Route("SPPOPUPS", "UI_ServerPanel_PopUps", true);
            Route("SPPOPVIDEO", "serverpanelpopups_broadcastvideo", true);
            GrimmCui.RegisterJsonRewriter(json =>
            {
                if (string.IsNullOrEmpty(json)) return json;
                (string command, string marker)[] markers =
                {
                    ("UI_ServerPanel_PopUps", "SPPOPUPS"),
                    ("UI_ServerPanel_Send_Command", "SPSEND"),
                    ("UI_ServerPanel_Close", "SPCLOSE"),
                    ("UI_ServerPanel", "SERVERPANEL"),
                    ("serverpanelpopups_broadcastvideo", "SPPOPVIDEO"),
                    ("serverpanel_broadcastvideo", "SPVIDEO"),
                    ("UI_Shop_Installer", "SHOPINST"),
                    ("UI_Shop", "SHOP"),
                    ("UI_Kits", "KITS"),
                    ("command.wipe.schedule", "WIPESCHEDULE"),
                    ("vgui.", "VGUI vgui.")
                };
                for (int i = 0; i < markers.Length; i++)
                {
                    var (command, marker) = markers[i];
                    if (json.IndexOf(command, StringComparison.Ordinal) < 0) continue;
                    json = json.Replace("\"command\":\"" + command, "\"command\":\"cui.endtest " + marker);
                }
                return json;
            });
        }
    }
}

