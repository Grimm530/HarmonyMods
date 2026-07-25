using System;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace CHT.Patches
{
    /// <summary>
    /// Routes <c>cui.endtest CHT …</c> to cht.shopcontroller.
    /// Leaves SHOP/KITS/etc. alone (return true). After load, <see cref="CuiEndtestRebind"/>
    /// restores Shop's prefix if CHT's PatchAll displaced it.
    /// </summary>
    [HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
    public static class CuiEndtestPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ConsoleSystem.Arg args)
        {
            try
            {
                var values = args?.Args;
                if (values == null || values.Length < 1)
                    return true;

                string marker = values[0].ToString();
                if (string.IsNullOrEmpty(marker) ||
                    !string.Equals(marker, "CHT", StringComparison.OrdinalIgnoreCase))
                    return true;

                if (values.Length < 2 || CHTMod.Plugin == null)
                    return false;

                var player = args.Connection?.player as BasePlayer ?? args.Player();
                if (player == null)
                    return false;

                // values: CHT cht.shopcontroller buy Easy → pass "cht.shopcontroller buy Easy"
                var command = new StringBuilder(values[1].ToString());
                for (var i = 2; i < values.Length; i++)
                    command.Append(' ').Append(values[i].ToString());

                var option = ConsoleSystem.Option.Server.Quiet();
                if (args.Connection != null)
                    option = option.FromConnection(args.Connection);
                CHTMod.Plugin.cmdShopController(new ConsoleSystem.Arg(option, command.ToString()));
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CHT] cui.endtest: " + ex.Message);
                return true;
            }
        }
    }
}
