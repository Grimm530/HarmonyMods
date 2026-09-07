using GrimmCuiHarmony;

namespace TeleportGUI
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            // Chaos UI path (TeleportGUI.UI.cs) — same pattern as AdminMenu / Minimap.
            GrimmCui.RegisterEndtest("TELEPORTGUI", (_, src) =>
                TeleportGUIMod.Instance?.HandleCuiCallback(src, src.Args));

            GrimmCui.RegisterJsonRewriter(
                GrimmCuiPatterns.CreateChaosCallbackRewriter("TELEPORTGUI", "teleportgui.callback"));

            // Hand-rolled TeleportGUIUI.cs path (teleportgui.cui …) — bridge until Chaos is sole UI.
            GrimmCui.RegisterJsonRewriter(
                GrimmCuiPatterns.CreateCommandBridgeRewriter("TELEPORTGUI", () => new[] { "teleportgui.cui" }));
        }
    }
}
