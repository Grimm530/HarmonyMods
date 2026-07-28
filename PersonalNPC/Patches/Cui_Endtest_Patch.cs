using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PersonalNPCHarmony.Patches
{
    /// <summary>
    /// Clients only forward ConsoleGen commands, so Oxide-style pnpc / pnpchelper.* CUI buttons
    /// never reach the server. RustCui rewrites them to "cui.endtest PNPC ..." /
    /// "cui.endtest PNPCHELPER ..."; this prefix unwraps the marker and runs the real handler.
    /// Any other payload falls through so InventoryShortcuts, Kits, TCUpgrade etc. keep working.
    /// </summary>
    [HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
    public static class Cui_Endtest_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg args)
        {
            var a = args?.Args;
            if (a == null || a.Length < 2) return true;

            string marker = a[0].ToString();
            bool isPnpc = string.Equals(marker, "PNPC", StringComparison.OrdinalIgnoreCase);
            bool isHelper = string.Equals(marker, "PNPCHELPER", StringComparison.OrdinalIgnoreCase);
            if (!isPnpc && !isHelper) return true;

            var mod = PersonalNPCHarmonyMod.Instance;
            if (mod == null) return false;

            var player = args.Connection?.player as BasePlayer;
            if (player == null || player.IsDestroyed || !player.IsConnected) return false;

            string command = a[1].ToString();
            var rest = new List<string>(Math.Max(0, a.Length - 2));
            for (int i = 2; i < a.Length; i++)
                rest.Add(a[i].ToString());

            try { mod.DispatchConsole(command, rest, args.Connection); }
            catch (Exception ex) { Debug.LogWarning("[PersonalNPC] cui.endtest " + marker + ": " + ex); }

            return false;
        }
    }
}
