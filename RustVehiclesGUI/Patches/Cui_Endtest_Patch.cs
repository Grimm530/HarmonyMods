using System;
using HarmonyLib;
using UnityEngine;

namespace RustVehiclesGUIHarmony.Patches
{
    /// <summary>
    /// Clients only forward ConsoleGen commands, so the plugin's vgui.* button commands are rewritten to
    /// "cui.endtest VGUI vgui.&lt;name&gt; ..." - both by this mod's RustCui and by ServerPanel's RustCui when the
    /// container is embedded in a panel page. This prefix routes them back to the console handlers.
    /// Other markers fall through so ServerPanel, Shop and Kits still receive their own payloads.
    /// </summary>
    [HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
    public static class Cui_Endtest_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg args)
        {
            var a = args?.Args;
            if (a == null || a.Length < 2) return true;

            if (!string.Equals(a[0].ToString(), "VGUI", StringComparison.OrdinalIgnoreCase)) return true;

            var mod = RustVehiclesGUIHarmonyMod.Instance;
            if (mod?.Plugin == null) return true;

            var player = args.Connection?.player as BasePlayer ?? args.Player();
            if (player == null || player.IsDestroyed || !player.IsConnected) return true;

            string command = a[1].ToString();
            var rest = new string[Math.Max(0, a.Length - 2)];
            for (int i = 2; i < a.Length; i++)
                rest[i - 2] = a[i].ToString();

            try
            {
                return !mod.InvokeConsoleCommand(args, command, rest);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RustVehiclesGUI] cui.endtest: " + ex.Message);
                return true;
            }
        }
    }
}
