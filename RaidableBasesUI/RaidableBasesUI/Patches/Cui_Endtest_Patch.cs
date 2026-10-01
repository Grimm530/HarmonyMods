using System;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace RaidableBasesBuyableUI.Patches
{
    /// <summary>
    /// Clients only forward ConsoleGen commands. BuyableUI buttons are rewritten to
    /// "cui.endtest RBBUI ui_buyable_…" in RustCui; this routes them to the plugin handlers.
    /// </summary>
    [HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
    public static class Cui_Endtest_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg args)
        {
            var a = args?.Args;
            if (a == null || a.Length < 1) return true;
            if (!string.Equals(a[0].ToString(), RaidableBasesBuyableUIMod.CuiMarker, StringComparison.OrdinalIgnoreCase))
                return true;

            var plugin = RaidableBasesBuyableUIMod.Plugin;
            if (plugin == null) return false;

            var player = args.Connection?.player as BasePlayer ?? args.Player();
            if (player == null || player.IsDestroyed || !player.IsConnected) return false;

            if (a.Length < 2) return false;
            var cmd = a[1].ToString() ?? string.Empty;

            var sb = new StringBuilder(cmd);
            for (int i = 2; i < a.Length; i++)
            {
                sb.Append(' ');
                string s = a[i].ToString() ?? string.Empty;
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
                plugin.DispatchUiCommand(cmd, uiArg);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RaidableBasesBuyableUI] cui.endtest RBBUI: " + ex);
            }

            return false;
        }
    }
}
