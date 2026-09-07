using GrimmCuiHarmony;

namespace MapVoter
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtestPrefix("MapVoter", (_, src) =>
            {
                var player = src.Connection?.player as BasePlayer;
                if (player == null || src.Args == null) return;
                var args = new string[src.Args.Length];
                for (int i = 0; i < src.Args.Length; i++) args[i] = src.GetString(i) ?? string.Empty;
                MapVoterMod.Instance?.HandleCuiCommand(player, args);
            });
        }
    }
}
