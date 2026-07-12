using System;
using System.Reflection;
using HarmonyLib;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace RaidableBasesBuyableUI.Patches
{
    /// <summary>
    /// Intercept RaidableBases CommandRegistry.TryHandleChat for empty buyraid
    /// BEFORE it reflects into CommandBuyRaid (which was bypassing our method patches).
    /// </summary>
    public static class RaidableBases_TryHandleChat_Patch
    {
        public static bool Prefix(BasePlayer player, string message, ref bool __result)
        {
            if (player == null || string.IsNullOrWhiteSpace(message))
                return true;

            if (!Chat_Say_Patch.IsEmptyBuyraidChat(message))
                return true;

            var plugin = RaidableBasesBuyableUIMod.Plugin;
            if (plugin == null)
                return true;

            try
            {
                CuiHelper.DestroyUi(player, "RB_UI_Buyable");
                plugin.OpenBuyableModes(player);
                __result = true; // chat was handled
                return false;    // skip RB TryHandleChat -> no CommandBuyRaid / BuySyntax
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RaidableBasesBuyableUI] TryHandleChat buyraid: " + ex.Message);
                return true;
            }
        }

        public static MethodInfo FindTargetMethod()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = asm.GetType("RaidableBases.CommandRegistry"); } catch { }
                if (t == null) continue;
                var m = t.GetMethod("TryHandleChat", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null, new[] { typeof(BasePlayer), typeof(string) }, null);
                if (m != null) return m;
            }
            return null;
        }
    }
}
