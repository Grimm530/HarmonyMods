using System;
using GrimmCuiHarmony;

namespace ShopHarmony
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("SHOP", (_, src) =>
            {
                var a = src.Args; if (a == null) return;
                var arr = new string[a.Length]; for (int i = 0; i < a.Length; i++) arr[i] = a.GetValue(i)?.ToString() ?? string.Empty;
                ShopHarmonyMod.Instance?.HandleCuiShop(src, arr);
            });
            GrimmCui.RegisterEndtest("SHOPINST", (_, src) =>
            {
                var a = src.Args; if (a == null) return;
                var arr = new string[a.Length]; for (int i = 0; i < a.Length; i++) arr[i] = a.GetValue(i)?.ToString() ?? string.Empty;
                ShopHarmonyMod.Instance?.HandleCuiShopInstaller(src, arr);
            });
            GrimmCui.RegisterJsonRewriter(json =>
            {
                if (string.IsNullOrEmpty(json) || json.IndexOf("UI_Shop", StringComparison.Ordinal) < 0) return json;
                json = json.Replace("\"command\":\"UI_Shop_Installer", "\"command\":\"cui.endtest SHOPINST");
                return json.Replace("\"command\":\"UI_Shop", "\"command\":\"cui.endtest SHOP");
            });
        }
    }
}

