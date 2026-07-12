using System;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace RaidableBases
{
    /// <summary>
    /// Clients only forward ConsoleGen commands (e.g. cui.endtest). Built-in RB UI buttons are
    /// rewritten to "cui.endtest RBUI ui_buyraid|rb_ui_move …" in CuiHelper; this routes them
    /// to the registered console handlers. Non-RBUI payloads return true so Kits / BuyableUI still work.
    /// </summary>
    [HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
    internal static class Cui_Endtest_Patch
    {
        public const string Marker = "RBUI";

        [HarmonyPrefix]
        private static bool Prefix(ConsoleSystem.Arg args)
        {
            var a = args?.Args;
            if (a == null || a.Length < 1) return true;
            if (!string.Equals(a[0].ToString(), Marker, StringComparison.OrdinalIgnoreCase))
                return true;

            var player = args.Connection?.player as BasePlayer ?? args.Player();
            if (player == null || player.IsDestroyed || !player.IsConnected) return false;

            if (a.Length < 2) return false;
            string cmdName = a[1].ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(cmdName)) return false;

            // Full command line for Arg ctor (parses command + args).
            var sb = new StringBuilder(cmdName);
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
                if (!TryInvokeRegistered(cmdName, uiArg))
                    Debug.LogWarning($"[RaidableBases] cui.endtest RBUI: command not registered: {cmdName}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RaidableBases] cui.endtest RBUI: " + ex);
            }

            return false;
        }

        private static bool TryInvokeRegistered(string cmdName, ConsoleSystem.Arg uiArg)
        {
            string key = cmdName.Contains(".") ? cmdName : "global." + cmdName;
            if (ConsoleSystem.Index.Server.Dict != null &&
                ConsoleSystem.Index.Server.Dict.TryGetValue(key, out var cmd) &&
                cmd?.Call != null)
            {
                cmd.Call(uiArg);
                return true;
            }

            if (!cmdName.Contains(".") &&
                ConsoleSystem.Index.Server.GlobalDict != null &&
                ConsoleSystem.Index.Server.GlobalDict.TryGetValue(cmdName, out cmd) &&
                cmd?.Call != null)
            {
                cmd.Call(uiArg);
                return true;
            }

            return false;
        }
    }
}
