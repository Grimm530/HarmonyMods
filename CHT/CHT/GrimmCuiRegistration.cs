using System;
using GrimmCuiHarmony;

namespace CHT
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCuiPatterns.RegisterRebuiltCommand("CHT", "cht.shopcontroller", arg => CHTMod.Plugin?.cmdShopController(arg));
            GrimmCui.RegisterJsonRewriter(json =>
            {
                if (string.IsNullOrEmpty(json) || json.IndexOf("cht.shopcontroller", StringComparison.Ordinal) < 0) return json;
                return json.Replace("\"command\":\"cht.shopcontroller", "\"command\":\"cui.endtest CHT cht.shopcontroller");
            });
        }
    }
}

