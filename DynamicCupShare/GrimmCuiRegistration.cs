using GrimmCuiHarmony;

namespace DynamicCupShareHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("DYNAMICCUPSHARE", (_, src) => DynamicCupShareMod.Instance?.HandleCuiCallback(src, src.Args));
        }
    }
}
