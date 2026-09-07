using GrimmCuiHarmony;

namespace PlayerSkinsHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("PLAYERSKINS", (_, src) => PlayerSkinsMod.Instance?.HandleCuiCallback(src, src.Args));
            GrimmCui.RegisterJsonRewriter(GrimmCuiPatterns.CreateChaosCallbackRewriter("PLAYERSKINS", "playerskins.callback"));
        }
    }
}
