using System;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace KitsHarmony.Patches
{
    /// <summary>
    /// Clients only forward ConsoleGen commands (e.g. cui.endtest). Kits CUI buttons are rewritten
    /// to "cui.endtest KITS …" in RustCui; this prefix routes them to CmdKitsConsole.
    /// Returns true for non-KITS payloads so InventoryShortcuts, TCUpgrade, etc. still work.
    ///
    /// Note: the header X often "works" via CUI close= alone even when commands are dead — that is
    /// client-side UI destroy, not proof that UI_Kits is running.
    /// </summary>
    [HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
    public static class Cui_Endtest_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg args)
        {
            var a = args?.Args;
            if (a == null || a.Length < 1) return true;
            if (!string.Equals(a[0].ToString(), "KITS", StringComparison.OrdinalIgnoreCase)) return true;

            var mod = KitsHarmonyMod.Instance;
            if (mod?.Plugin == null) return false;

            var player = args.Connection?.player as BasePlayer ?? args.Player();
            if (player == null || player.IsDestroyed || !player.IsConnected) return false;

            // Build full command line for Arg ctor (parses command + args). Do NOT use
            // ConsoleSystem.Run(opt, "UI_Kits givekit 1") — that overload treats the whole
            // string as the command *name*, so the handler never runs.
            var sb = new StringBuilder("UI_Kits");
            for (int i = 1; i < a.Length; i++)
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
                mod.Plugin.CmdKitsConsole(uiArg);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Kits] cui.endtest KITS: " + ex);
            }

            return false;
        }
    }
}
