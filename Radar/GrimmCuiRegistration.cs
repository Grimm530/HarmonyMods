using GrimmCuiHarmony;

namespace Radar
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("RADAR", (player, src) =>
            {
                if (player == null) return;
                RadarMod.Instance?.HandleCuiCommand(player, src.Args.AsStringArray());
            });
        }
    }
}
