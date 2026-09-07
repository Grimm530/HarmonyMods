using GrimmCuiHarmony;

namespace TCUpgrade
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("SENDCMD", (_, src) => TCUpgradeMod.Instance?.HandleSendCmdFromCui(src));
        }
    }
}
