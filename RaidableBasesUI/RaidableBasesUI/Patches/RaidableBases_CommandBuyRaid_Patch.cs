using System;
using System.Reflection;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace RaidableBasesBuyableUI.Patches
{
    /// <summary>
    /// Prefix for CommandBuyRaid using __args so Harmony does not need RaidableBases.IPlayer at compile time.
    /// (Typed IPlayer parameters often fail to inject -> Prefix no-ops -> BuySyntax.)
    /// </summary>
    public static class RaidableBases_CommandBuyRaid_Patch
    {
        public static bool Prefix(object __instance, object[] __args)
        {
            if (__args == null || __args.Length < 3)
                return true;

            var args = __args[2] as string[];
            if (args != null && args.Length > 0)
                return true;

            var plugin = RaidableBasesBuyableUIMod.Plugin;
            if (plugin == null)
                return true;

            try
            {
                var player = ResolvePlayer(__args[0]);
                if (player == null || !player.IsConnected)
                    return true;

                CuiHelper.DestroyUi(player, "RB_UI_Buyable");
                plugin.OpenBuyableModes(player);
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RaidableBasesBuyableUI] CommandBuyRaid intercept: " + ex.Message);
                return true;
            }
        }

        private static BasePlayer ResolvePlayer(object user)
        {
            if (user is BasePlayer bp) return bp;
            if (user == null) return null;
            try
            {
                var prop = user.GetType().GetProperty("Object", BindingFlags.Public | BindingFlags.Instance);
                if (prop != null)
                    return prop.GetValue(user) as BasePlayer;
            }
            catch { }
            return null;
        }

        public static MethodInfo FindTargetMethod()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = asm.GetType("RaidableBases.RaidableBases"); } catch { }
                if (t == null) continue;

                foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.Name != "CommandBuyRaid") continue;
                    var ps = m.GetParameters();
                    if (ps.Length != 3) continue;
                    if (ps[1].ParameterType != typeof(string)) continue;
                    if (ps[2].ParameterType != typeof(string[])) continue;
                    return m;
                }
            }
            return null;
        }
    }
}
