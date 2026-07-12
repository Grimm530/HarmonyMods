using System;
using System.Reflection;
using UnityEngine;

namespace AdminAlias
{
    /// <summary>
    /// Harmony mod: change in-game display name for configured players (e.g. play as admin under a different name).
    /// Config: HarmonyConfig/AdminAlias.json — "Overrides": { "76561198000000001": "YourAlias" }.
    /// Console command "adminalias" is registered and added to the replicated list so it works for all players (no reload needed).
    /// </summary>
    public class AdminAliasMod : IHarmonyModHooks
    {
        public static AdminAliasMod Instance { get; private set; }

        private ConsoleSystem.Command _adminaliasCommand;
        private static object _replicatedList;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            AdminAliasConfig.LoadConfig();

            try
            {
                _adminaliasCommand = new ConsoleSystem.Command
                {
                    Name = "adminalias",
                    FullName = "global.adminalias",
                    Variable = true,
                    ServerAdmin = false,
                    Replicated = true,
                    Call = HandleAdminalias
                };
                ConsoleSystem.Index.Server.Dict["global.adminalias"] = _adminaliasCommand;
                if (ConsoleSystem.Index.Server.GlobalDict != null)
                    ConsoleSystem.Index.Server.GlobalDict["adminalias"] = _adminaliasCommand;

                // Add to replicated list so clients who join after server start receive the command (fixes "unknown command" until reload).
                var serverType = typeof(ConsoleSystem.Index.Server);
                var prop = serverType.GetProperty("Replicated", BindingFlags.Public | BindingFlags.Static);
                if (prop != null)
                {
                    var list = prop.GetValue(null) as System.Collections.IList;
                    if (list != null && !list.Contains(_adminaliasCommand))
                    {
                        list.Add(_adminaliasCommand);
                        _replicatedList = list;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AdminAlias] Command registration failed: " + ex.Message);
            }

            Debug.Log("[AdminAlias] Loaded. Add your Steam64 ID and desired name to HarmonyConfig/AdminAlias.json (Overrides). Use 'adminalias' in F1 to see your alias.");
        }

        private static void HandleAdminalias(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            if (player == null) return;
            var name = AdminAliasConfig.GetOverride(player.userID);
            if (!string.IsNullOrEmpty(name))
                player.ConsoleMessage("Your alias: " + name);
            else
                player.ConsoleMessage("No alias set. Add your Steam64 ID to HarmonyConfig/AdminAlias.json (Overrides).");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            try
            {
                if (_replicatedList is System.Collections.IList list && _adminaliasCommand != null)
                    list.Remove(_adminaliasCommand);
                if (_adminaliasCommand != null)
                {
                    ConsoleSystem.Index.Server.Dict?.Remove("global.adminalias");
                    ConsoleSystem.Index.Server.GlobalDict?.Remove("adminalias");
                }
            }
            catch { }
            _adminaliasCommand = null;
            _replicatedList = null;
            Instance = null;
        }
    }
}
