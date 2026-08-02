using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CopyPasteHarmony;

public class CopyPasteHarmonyMod : IHarmonyModHooks
{
    public static CopyPasteHarmonyMod Instance { get; private set; }

    private Oxide.Plugins.CopyPaste _plugin;
    private readonly Dictionary<string, MethodInfo> _commandMethods = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ConsoleSystem.Command> _registeredCommands = new();

    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        Instance = this;

        OxideCoreCompat.OxideCompatBootstrap.Initialize("CopyPaste");

        _plugin = new Oxide.Plugins.CopyPaste();

        // Ensure config exists (CopyPaste's LoadDefaultConfig writes the default schema).
        _plugin.EnsureConfigLoaded();

        InvokePrivate(_plugin, "Init");
        InvokePrivate(_plugin, "OnServerInitialized");

        RegisterPluginCommandsFromAttributes();
        RegisterConsoleCommands();

        Debug.Log("[CopyPasteHarmony] Loaded. Config: HarmonyConfig/CopyPaste.json");
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        UnregisterConsoleCommands();
        _commandMethods.Clear();
        _plugin = null;
        Instance = null;
    }

    private void RegisterPluginCommandsFromAttributes()
    {
        if (_plugin == null) return;

        var type = _plugin.GetType();
        var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        foreach (var m in methods)
        {
            var attr = m.GetCustomAttribute<Oxide.Core.Libraries.Covalence.CommandAttribute>();
            if (attr == null) continue;
            if (string.IsNullOrWhiteSpace(attr.Name)) continue;
            _commandMethods[attr.Name.Trim()] = m;
        }
    }

    private void RegisterConsoleCommands()
    {
        var dict = ConsoleSystem.Index.Server.Dict;
        if (dict == null) return;

        // Minimal console command registration so F1/server console can run it.
        // Chat handling is done by the Harmony patch on ConVar.Chat.say.
        foreach (var kvp in _commandMethods)
        {
            string cmdName = kvp.Key;
            if (_registeredCommands.Any(c => c.Name.Equals(cmdName, StringComparison.OrdinalIgnoreCase)))
                continue;

            var method = kvp.Value;
            var command = new ConsoleSystem.Command
            {
                Name = cmdName,
                FullName = "global." + cmdName,
                Variable = true,
                ServerAdmin = false,
                AllowRunFromServer = true,
                Call = arg =>
                {
                    try
                    {
                        var bp = arg?.Player();
                        if (bp == null)
                        {
                            Debug.Log($"[CopyPasteHarmony] Console command '{cmdName}' requires a player.");
                            return;
                        }

                        var iPlayer = new OxideCompatPlayer(bp);
                        var argsArr = arg?.Args ?? Array.Empty<string>();
                        method.Invoke(_plugin, new object[] { iPlayer, cmdName, argsArr });
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[CopyPasteHarmony] Command '{cmdName}' failed: {ex}");
                    }
                }
            };

            dict["global." + cmdName] = command;
            if (ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict[cmdName] = command;

            _registeredCommands.Add(command);
        }

        // Required so the server console resolves the commands.
        if (dict != null)
            ConsoleSystem.Index.All = dict.Values.ToArray();
    }

    private void UnregisterConsoleCommands()
    {
        var dict = ConsoleSystem.Index.Server.Dict;
        if (dict != null)
        {
            foreach (var cmd in _registeredCommands)
            {
                if (cmd == null) continue;
                dict.Remove("global." + cmd.Name);
                ConsoleSystem.Index.Server.GlobalDict?.Remove(cmd.Name);
            }
            ConsoleSystem.Index.All = dict.Values.ToArray();
        }

        _registeredCommands.Clear();
    }

    private static void InvokePrivate(object target, string methodName)
    {
        if (target == null) return;
        var mi = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        mi?.Invoke(target, Array.Empty<object>());
    }

    internal bool TryHandleChatCommand(BasePlayer player, string message)
    {
        if (player == null || string.IsNullOrWhiteSpace(message)) return false;
        if (_plugin == null) return false;

        string msg = message.Trim();
        if (msg.StartsWith("/")) msg = msg.Substring(1).Trim();
        if (string.IsNullOrEmpty(msg)) return false;

        var parts = msg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        var cmd = parts[0];
        if (!_commandMethods.TryGetValue(cmd, out var method)) return false;

        var cmdArgs = parts.Skip(1).ToArray();
        var iPlayer = new OxideCompatPlayer(player);
        try
        {
            method.Invoke(_plugin, new object[] { iPlayer, cmd, cmdArgs });
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CopyPasteHarmony] Chat command '{cmd}' failed: {ex}");
        }

        return true; // skip chat output
    }

    internal string GetServerRootStatic()
        => Path.GetFullPath(Path.Combine(Application.dataPath ?? ".", ".."));

    private class OxideCompatPlayer : Oxide.Core.Libraries.Covalence.IPlayer
    {
        private readonly BasePlayer _bp;

        public OxideCompatPlayer(BasePlayer bp) => _bp = bp;

        public object Object => _bp;
        public string Id => _bp.userID.ToString();
        public string UserIDString => _bp.userID.ToString();
        public bool IsAdmin => _bp.IsAdmin;

        public bool HasPermission(string permName)
            => OxideCoreCompat.PermissionStore.IsAllowed(Id);

        public void Reply(string message)
        {
            if (_bp == null || _bp.net?.connection == null)
            {
                Debug.Log($"[CopyPasteHarmony] Reply (no connection): {message}");
                return;
            }

            ConsoleNetwork.SendClientCommand(_bp.net.connection, "chat.add", 0, 0, message);
        }
    }
}

