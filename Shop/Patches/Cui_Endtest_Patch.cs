using System;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ShopHarmony.Patches
{
    /// <summary>
    /// Clients only forward ConsoleGen commands (e.g. cui.endtest). Shop CUI buttons are rewritten
    /// to "cui.endtest SHOP …" / "cui.endtest SHOPINST …" in RustCui; this prefix routes them.
    /// Returns true for non-SHOP payloads so Kits and other mods still work.
    /// </summary>
    [HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
    public static class Cui_Endtest_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg args)
        {
            var a = args?.Args;
            if (a == null || a.Length < 1) return true;

            string marker = a[0].ToString();
            var mod = ShopHarmonyMod.Instance;
            if (mod?.Plugin == null) return true;

            if (string.Equals(marker, "SHOPINST", StringComparison.OrdinalIgnoreCase))
            {
                Dispatch(mod, args, a, installer: true);
                return false;
            }

            if (string.Equals(marker, "SHOP", StringComparison.OrdinalIgnoreCase))
            {
                Dispatch(mod, args, a, installer: false);
                return false;
            }

            return true;
        }

        private static void Dispatch(ShopHarmonyMod mod, ConsoleSystem.Arg args, Array a, bool installer)
        {
            var player = args.Connection?.player as BasePlayer ?? args.Player();
            if (player == null || player.IsDestroyed || !player.IsConnected) return;

            var sb = new StringBuilder(installer ? "UI_Shop_Installer" : "UI_Shop");
            for (int i = 1; i < a.Length; i++)
            {
                sb.Append(' ');
                string s = a.GetValue(i)?.ToString() ?? string.Empty;
                if (s.IndexOfAny(new[] { ' ', '"' }) >= 0)
                    sb.Append('"').Append(s.Replace("\"", "\\\"")).Append('"');
                else
                    sb.Append(s);
            }

            try
            {
                var opt = ConsoleSystem.Option.Server.Quiet();
                if (args.Connection != null)
                    opt = opt.FromConnection(args.Connection);
                var uiArg = new ConsoleSystem.Arg(opt, sb.ToString());
                if (installer)
                    mod.Plugin.CmdConsoleShopInstaller(uiArg);
                else
                    mod.Plugin.CmdConsoleShop(uiArg);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Shop] cui.endtest: " + ex);
            }
        }
    }
}
