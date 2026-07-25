using System;
using HarmonyLib;
using UnityEngine;

namespace TeleportGUI.Patches
{
    /// <summary>
    /// Clients only forward ConsoleGen commands (e.g. cui.endtest). TeleportGUI CUI buttons are emitted
    /// as "cui.endtest TELEPORTGUI …" by <see cref="TeleportGUIUI"/>; this marker routes only the
    /// TELEPORTGUI payload to the mod. All other markers (Shop/Kits/SkillTree/etc.) pass through untouched.
    /// This is the safe replacement for relying on a directly-registered custom console command.
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
            if (!string.Equals(marker, TeleportGUIMod.CuiMarker, StringComparison.OrdinalIgnoreCase))
                return true;

            var mod = TeleportGUIMod.Instance;
            if (mod == null) return true;

            try
            {
                mod.HandleCuiEndtest(args, a);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TeleportGUI] cui.endtest TELEPORTGUI: " + ex);
            }

            return false;
        }
    }
}
