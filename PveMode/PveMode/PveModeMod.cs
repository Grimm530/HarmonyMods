using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace PveModeHarmony
{
    /// <summary>
    /// Harmony mod entry point for PveMode (port of Oxide PveMode 1.2.9).
    /// Owns lifecycle (load/unload), config/data/lang paths, console commands and the
    /// "/EventsTime" chat command. Gameplay logic lives in PveModeManager; API surface in
    /// PveModeApi; Harmony patches live under Patches/.
    /// </summary>
    public class PveModeMod : IHarmonyModHooks
    {
        public static PveModeMod Instance { get; private set; }

        private ConsoleSystem.Command _clearTimeCmd;
        private ConsoleSystem.Command _clearOwnerCmd;

        private static string ServerRoot => AppDomain.CurrentDomain.BaseDirectory ?? Environment.CurrentDirectory;
        private static string ConfigPath => Path.Combine(ServerRoot, "HarmonyConfig", "PveMode.json");
        private static string LangPath => Path.Combine(ServerRoot, "HarmonyLanguage", "PveMode.json");
        private static string DataPath => Path.Combine(ServerRoot, "HarmonyData", "PveMode", "players.json");

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;

            PveModeConfig config = PveModeConfig.Load(ConfigPath);
            PveModeLang.Load(LangPath);
            PveModeManager.Init(config, DataPath);
            PveModeApi.Activate();

            RegisterCommands();

            Debug.Log("[PveMode] 0PveMode loaded gen=" + PveModeApi.GetGeneration() + ". Config: HarmonyConfig/PveMode.json. Data: HarmonyData/PveMode/. Console: ClearTimePveMode / ClearOwnerPveMode. Chat: /EventsTime.");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            UnregisterCommands();
            PveModeApi.Deactivate();
            PveModeManager.Shutdown();
            Instance = null;
            Debug.Log("[PveMode] 0PveMode unloaded.");
        }

        // ---- Console commands --------------------------------------------------

        private void RegisterCommands()
        {
            try
            {
                _clearTimeCmd = new ConsoleSystem.Command
                {
                    Name = "ClearTimePveMode",
                    FullName = "global.ClearTimePveMode",
                    Variable = false,
                    ServerAdmin = true,
                    ServerUser = true,
                    AllowRunFromServer = true,
                    Call = ConsoleClearTimePveMode
                };
                _clearOwnerCmd = new ConsoleSystem.Command
                {
                    Name = "ClearOwnerPveMode",
                    FullName = "global.ClearOwnerPveMode",
                    Variable = false,
                    ServerAdmin = true,
                    ServerUser = true,
                    AllowRunFromServer = true,
                    Call = ConsoleClearOwnerPveMode
                };

                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                if (dict != null)
                {
                    dict["global.ClearTimePveMode"] = _clearTimeCmd;
                    dict["global.ClearOwnerPveMode"] = _clearOwnerCmd;
                }
                if (globalDict != null)
                {
                    globalDict["ClearTimePveMode"] = _clearTimeCmd;
                    globalDict["ClearOwnerPveMode"] = _clearOwnerCmd;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PveMode] RegisterCommands failed: " + ex.Message);
            }
        }

        private void UnregisterCommands()
        {
            try
            {
                ConsoleSystem.Index.Server.Dict?.Remove("global.ClearTimePveMode");
                ConsoleSystem.Index.Server.Dict?.Remove("global.ClearOwnerPveMode");
                ConsoleSystem.Index.Server.GlobalDict?.Remove("ClearTimePveMode");
                ConsoleSystem.Index.Server.GlobalDict?.Remove("ClearOwnerPveMode");
            }
            catch { }
        }

        private void ConsoleClearTimePveMode(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Player() != null || arg.Args == null || arg.Args.Length == 0 || arg.Args.Length > 2) return;
            if (!ulong.TryParse(arg.Args[0].ToString(), out ulong steamId)) { arg.ReplyWith("Invalid SteamID64."); return; }
            string nameEvent = arg.Args.Length == 2 ? arg.Args[1].ToString() : string.Empty;
            PveModeManager.ConsoleClearTime(steamId, nameEvent);
        }

        private void ConsoleClearOwnerPveMode(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Player() != null || arg.Args == null || arg.Args.Length == 0 || arg.Args.Length > 2) return;
            if (!ulong.TryParse(arg.Args[0].ToString(), out ulong steamId)) { arg.ReplyWith("Invalid SteamID64."); return; }
            string nameEvent = arg.Args.Length == 2 ? arg.Args[1].ToString() : string.Empty;
            PveModeManager.ConsoleClearOwner(steamId, nameEvent);
        }

        // ---- Chat command (/EventsTime) -----------------------------------------

        /// <summary>Called from Patches/Chat_Say_Patch.cs. Returns true if the message was consumed.</summary>
        public bool OnChatCommand(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrEmpty(message)) return false;
            string trimmed = message.Trim();
            if (!trimmed.StartsWith("/", StringComparison.Ordinal)) return false;

            string command = trimmed.Substring(1).Split(' ')[0];
            if (!string.Equals(command, "EventsTime", StringComparison.OrdinalIgnoreCase)) return false;

            ShowEventsTime(player);
            return true;
        }

        private void ShowEventsTime(BasePlayer player)
        {
            Dictionary<string, double> times = PveModeManager.GetTimesPlayer(player.userID);
            if (times == null || times.Count == 0) return;

            string message = PveModeLang.Get("EventsTime");
            foreach (KeyValuePair<string, double> kv in times)
            {
                ControllerEvent controller = PveModeManager.Events.FirstOrDefault(x => x.ShortName == kv.Key);
                if (controller == null)
                {
                    message += "\n- " + kv.Key + " = " + PveModeManager.GetTimeFormat(kv.Value);
                }
                else
                {
                    double remaining = PveModeManager.GetOwnerCooldownRemaining(player.userID, kv.Key, controller.Config.CooldownOwner);
                    if (remaining > 0) message += "\n- " + kv.Key + "* = " + PveModeManager.GetTimeFormat(remaining);
                }
            }

            PveModeManager.SendChat(player, message);
        }
    }
}
