using System;
using System.Reflection;
using HarmonyLib;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace RaidableBasesBuyableUI.Patches
{
    /// <summary>
    /// When RaidableBases tries to show its built-in Buyable Events panel, open our gallery instead.
    /// Applied manually once RaidableBases is loaded (same as CallHook patch).
    /// </summary>
    public static class RaidableBases_ShowBuyableUi_Patch
    {
        public static bool Prefix(BasePlayer player, bool moveUI)
        {
            var plugin = RaidableBasesBuyableUIMod.Plugin;
            if (plugin == null || player == null)
                return true;

            try
            {
                if (!PermissionsBridge.UserHasPermissionOrDefaultAllow(player.UserIDString, "raidablebasesbuyableui.allow"))
                    return true;

                CuiHelper.DestroyUi(player, "RB_UI_Buyable");
                plugin.OpenBuyableModes(player);
                return false; // skip RaidableBases built-in panel
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RaidableBasesBuyableUI] ShowBuyableUi redirect: " + ex.Message);
                return true;
            }
        }

        public static MethodInfo FindTargetMethod()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try
                {
                    // Nested: RaidableBases.RaidableBases+UiHandler
                    t = asm.GetType("RaidableBases.RaidableBases+UiHandler")
                        ?? asm.GetType("RaidableBases.UiHandler");
                }
                catch { }
                if (t == null) continue;

                var m = t.GetMethod("ShowBuyableUi", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new[] { typeof(BasePlayer), typeof(bool) }, null);
                if (m != null) return m;
            }
            return null;
        }
    }
}
