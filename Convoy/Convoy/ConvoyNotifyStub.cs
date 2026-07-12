using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Convoy
{
    /// <summary>
    /// Player/console notify for Convoy. Game tips use BasePlayer.ShowToast → gametip.showtoast_translated
    /// (Harmony_Mod_Execution_Framework §13). Never use obsolete gametip.showtoast.
    /// </summary>
    public static class ConvoyNotifyStub
    {
        private const string TipToken = "convoy.tip";

        private static readonly Dictionary<string, string> English = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["RouteCachingStart_Log"] = "Started caching complex convoy routes.",
            ["RouteCachingStop_Log"] = "Finished caching complex convoy routes. Count:",
            ["RouteNotFound_Exeption"] = "No suitable road route found. Try PathType 0 and lower MinRoadLength.",
            ["EventStart_Log"] = "The event has begun! (Preset name - {0})",
            ["EventStop_Log"] = "The event is over!",
            ["EventActive_Exeption"] = "This event is active now. Finish the current event! (convoystop)",
            ["ConfigurationNotFound_Exeption"] = "No suitable convoy preset found in config.",
            ["SuccessfullyLaunched"] = "Convoy event launched.",
            ["PreStart"] = "{0} In {1} the cargo will be transported along the road!",
            ["EventStart"] = "{0} {1} is spawned at grid {2}",
            ["DamageDistance"] = "{0} Come closer!",
            ["ConvoyAttacked"] = "{0} {1} attacked a convoy",
            ["CantLoot"] = "{0} It is necessary to stop the convoy and kill the guards!",
            ["Looted"] = "{0} {1} has been looted!",
            ["RemainTime"] = "{0} {1} will be destroyed in {2}!",
            ["PreFinish"] = "{0} The event will be over in {1}",
            ["Finish"] = "{0} The event is over!",
            ["Hours"] = "h.",
            ["Minutes"] = "m.",
            ["Seconds"] = "s.",
        };

        private static ConvoyNotifyConfig Notify => ConvoyMod.Instance?.FullConfig?.NotifyConfig;

        private static string Prefix => ConvoyMod.Instance?.FullConfig?.Prefix
            ?? ConvoyMod.Instance?.Config?.Prefix
            ?? "[Convoy]";

        private static string Format(string key, params object[] args)
        {
            string text;
            if (key == null || !English.TryGetValue(key, out text))
                text = key ?? "";
            if (args == null || args.Length == 0)
                return text;
            try { return string.Format(text, args); }
            catch { return text; }
        }

        private static string ClearColorAndSize(string message)
        {
            if (string.IsNullOrEmpty(message)) return message;
            message = message.Replace("</color>", string.Empty);
            message = message.Replace("</size>", string.Empty);
            while (message.Contains("<color="))
            {
                int index = message.IndexOf("<color=", StringComparison.Ordinal);
                int end = message.IndexOf('>', index);
                if (index < 0 || end < 0) break;
                message = message.Remove(index, end - index + 1);
            }
            while (message.Contains("<size="))
            {
                int index = message.IndexOf("<size=", StringComparison.Ordinal);
                int end = message.IndexOf('>', index);
                if (index < 0 || end < 0) break;
                message = message.Remove(index, end - index + 1);
            }
            return message;
        }

        public static string GetTimeMessage(int seconds)
        {
            if (seconds < 0) seconds = 0;
            var span = TimeSpan.FromSeconds(seconds);
            var sb = new StringBuilder();
            if (span.Hours > 0) sb.Append(' ').Append(span.Hours).Append(' ').Append(Format("Hours"));
            if (span.Minutes > 0) sb.Append(' ').Append(span.Minutes).Append(' ').Append(Format("Minutes"));
            if (sb.Length == 0) sb.Append(' ').Append(span.Seconds).Append(' ').Append(Format("Seconds"));
            return sb.ToString().Trim();
        }

        public static void PrintLogMessage(string key, string arg = null)
        {
            Debug.Log("[Convoy] " + ClearColorAndSize(arg != null ? Format(key, arg) : Format(key)));
        }

        public static void PrintWarningMessage(string key, object arg = null)
        {
            Debug.LogWarning("[Convoy] " + ClearColorAndSize(arg != null ? Format(key, arg) : Format(key)));
        }

        public static void PrintError(BasePlayer player, string key, params object[] args)
        {
            string msg = Format(key, args);
            if (player != null && player.IsConnected)
                SendMessageToPlayer(player, key, args);
            else
                Debug.LogError("[Convoy] Error: " + ClearColorAndSize(msg));
        }

        public static void PrintInfoMessage(BasePlayer player, string key, params object[] args)
        {
            if (player != null && player.IsConnected)
                SendMessageToPlayer(player, key, args);
            else
                Debug.LogWarning("[Convoy] " + ClearColorAndSize(Format(key, args)));
        }

        public static void SendMessageToAll(string key, params object[] args)
        {
            var list = BasePlayer.activePlayerList;
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                BasePlayer player = list[i];
                if (player != null && player.IsConnected)
                    SendMessageToPlayer(player, key, args);
            }
            Debug.Log("[Convoy] " + ClearColorAndSize(BuildPlayerMessage(key, args)));
        }

        /// <summary>Overload kept for existing call sites that pass prefix/displayName/grid explicitly.</summary>
        public static void SendMessageToAll(string key, string prefix, string displayName, string gridStr)
        {
            SendMessageToAll(key, (object)prefix, displayName, gridStr);
        }

        public static void SendMessageToAll(string key, string prefix)
        {
            SendMessageToAll(key, (object)prefix);
        }

        public static void SendMessageToPlayer(BasePlayer player, string key, params object[] args)
        {
            if (player == null || !player.IsConnected) return;

            object[] resolved = ResolveTimeArgs(args);
            string playerMessage = BuildPlayerMessage(key, resolved);
            if (string.IsNullOrWhiteSpace(playerMessage)) return;

            var notify = Notify;
            bool chat = notify == null || notify.IsChatEnable;
            bool tip = notify?.GameTipConfig != null && notify.GameTipConfig.IsEnabled;

            // Default: if neither channel configured, still tip (matches your Convoy.json: chat off, tips on).
            if (notify == null)
            {
                chat = false;
                tip = true;
            }

            if (chat)
                player.ChatMessage(playerMessage);

            if (tip)
                ShowGameTip(player, ClearColorAndSize(playerMessage), notify?.GameTipConfig?.Style ?? 0);
        }

        private static string BuildPlayerMessage(string key, object[] args)
        {
            // Keys that already include {0} as prefix in the template.
            if (args != null && args.Length > 0)
                return Format(key, args);
            return Format(key, Prefix);
        }

        private static object[] ResolveTimeArgs(object[] args)
        {
            if (args == null || args.Length == 0) return args;
            object[] clone = new object[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] is int sec)
                    clone[i] = GetTimeMessage(sec);
                else
                    clone[i] = args[i];
            }
            return clone;
        }

        /// <summary>
        /// Framework §13: gametip.showtoast_translated (same as BasePlayer.ShowToast).
        /// Plain showgametip as fallback. Never use obsolete gametip.showtoast.
        /// </summary>
        public static void ShowGameTip(BasePlayer player, string text, int styleInt = 0)
        {
            if (player == null || !player.IsConnected) return;
            if (string.IsNullOrWhiteSpace(text)) return;

            string toast = ClearColorAndSize(text);
            if (styleInt < 0 || styleInt > (int)GameTip.Styles.LAST)
                styleInt = 0;

            try
            {
                // Exact Facepunch path from BasePlayer.ShowToast (no Rust.Localization Phrase dependency).
                player.SendConsoleCommand("gametip.showtoast_translated", styleInt, TipToken, toast, false, Array.Empty<string>());
                return;
            }
            catch { }

            // Plain gametip banner (TCUpgrade pattern).
            try
            {
                player.SendConsoleCommand("gametip.hidegametip");
                player.SendConsoleCommand("gametip.showgametip", toast);
                var mgr = ServerMgr.Instance;
                if (mgr != null)
                    mgr.StartCoroutine(HideGameTipDelayed(player, 8f));
            }
            catch { }
        }

        private static IEnumerator HideGameTipDelayed(BasePlayer player, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            try
            {
                if (player != null && player.IsConnected)
                    player.SendConsoleCommand("gametip.hidegametip");
            }
            catch { }
        }
    }
}
