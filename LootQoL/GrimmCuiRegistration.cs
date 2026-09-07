using System;
using GrimmCuiHarmony;

namespace LootQoLHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            // FastLoot / SortButton embed "cui.endtest LOOTQOL …" directly in UI JSON.
            GrimmCui.RegisterEndtest("LOOTQOL", (_, src) => LootQoLHarmony.LootQoLMod.Instance?.HandleCuiCallback(src));
            // Legacy rewriter if any UI still emits lootqol.* commands.
            GrimmCui.RegisterJsonRewriter(json =>
            {
                if (string.IsNullOrEmpty(json) || json.IndexOf("lootqol.", StringComparison.Ordinal) < 0) return json;
                return json.Replace("\"command\":\"lootqol.", "\"command\":\"cui.endtest LOOTQOL ");
            });
        }
    }
}
