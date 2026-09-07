using GrimmCuiHarmony;

namespace RecyclerSpeed
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("RECYCLER_SPEED", (player, src) =>
            {
                if (player == null) return;
                RecyclerSpeedMod.Instance?.HandleCuiCommand(player, src.Args.AsStringArray());
            });
        }
    }
}
