using System;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace WipeScheduleHarmony.Patches
{
    /// <summary>
    /// Clients only forward ConsoleGen commands (e.g. cui.endtest). WipeSchedule CUI buttons are rewritten
    /// to "cui.endtest WIPESCHEDULE …" in RustCui; this prefix routes them.
    /// Returns true for non-WIPESCHEDULE payloads so Shop/Kits and other mods still work.
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
            var mod = WipeScheduleHarmonyMod.Instance;
            if (mod?.Plugin == null) return true;

            if (string.Equals(marker, "WIPESCHEDULE", StringComparison.OrdinalIgnoreCase))
            {
                Dispatch(mod, args, a);
                return false;
            }

            return true;
        }

        private static void Dispatch(WipeScheduleHarmonyMod mod, ConsoleSystem.Arg args, Array a)
        {
            var player = args.Connection?.player as BasePlayer ?? args.Player();
            if (player == null || player.IsDestroyed || !player.IsConnected) return;

            var sb = new StringBuilder("command.wipe.schedule");
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
                mod.Plugin.CmdConsoleWipeSchedule(uiArg);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WipeSchedule] cui.endtest: " + ex);
            }
        }
    }
}
