using GrimmCuiHarmony;

namespace SortButton
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("SORTBUTTON", (_, src) =>
            {
                var player = src.Connection?.player as BasePlayer;
                if (player == null || src.Args == null || src.Args.Length < 2) return;
                SortButtonMod.Instance?.HandleCui(player, src.GetString(1) ?? string.Empty);
            });
        }
    }
}
