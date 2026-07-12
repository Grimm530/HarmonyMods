using HarmonyLib;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace RaidableBasesBuyableUI.Patches
{
    /// <summary>
    /// Chat routing for /buyraid (same pattern as RaidableBases CommandRegistry / TeleportGUI).
    /// Harmony patches on CommandBuyRaid via reflection Invoke are unreliable; intercept chat here.
    /// Returns false to swallow the chat message when we handle it.
    /// </summary>
    [HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.say))]
    public static class Chat_Say_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(ConsoleSystem.Arg arg)
        {
            if (arg == null) return true;

            string message = arg.GetString(0, "text")?.Trim();
            if (string.IsNullOrEmpty(message)) return true;

            var player = arg.Player();
            if (player == null || !player.IsConnected) return true;

            if (!IsEmptyBuyraidChat(message))
                return true;

            var plugin = RaidableBasesBuyableUIMod.Plugin;
            if (plugin == null)
                return true;

            try
            {
                CuiHelper.DestroyUi(player, "RB_UI_Buyable");
                plugin.OpenBuyableModes(player);
                return false; // do not forward to RaidableBases (avoids BuySyntax)
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[RaidableBasesBuyableUI] chat buyraid: " + ex.Message);
                return true;
            }
        }

        /// <summary>
        /// True for: buyraid, /buyraid, buyraid with only whitespace.
        /// False when a mode/filename is supplied (buyraid easy) - let RaidableBases purchase.
        /// </summary>
        internal static bool IsEmptyBuyraidChat(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            message = message.Trim();
            if (message.StartsWith("/")) message = message.Substring(1).Trim();

            // Default BuyCommand is buyraid; also accept common alias without reading RB config.
            if (message.Equals("buyraid", System.StringComparison.OrdinalIgnoreCase))
                return true;

            // buyraid with no further tokens after the command word
            var parts = message.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1 && parts[0].Equals("buyraid", System.StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }
    }
}
