using GrimmCuiHarmony;

namespace RaidableBasesUI
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterJsonRewriter(json =>
            {
                if (string.IsNullOrEmpty(json)) return json;
                return json.Replace("\"command\":\"ui_buyable_", "\"command\":\"cui.endtest RBBUI ui_buyable_");
            });
        }
    }
}
