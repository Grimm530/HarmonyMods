using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using static RaidableBases.RaidableBasesExtensionMethods.ExtensionMethods;

namespace RaidableBases
{
    /// <summary>
    /// Chat command routing (Oxide AddCovalenceCommand → /rb /buyraid /rbe etc.).
    /// </summary>
    [HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.say))]
    internal static class Chat_Say_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(ConsoleSystem.Arg arg)
        {
            if (arg == null) return true;
            string message = arg.GetString(0, "text")?.Trim();
            if (string.IsNullOrEmpty(message)) return true;
            var player = arg.Player();
            if (player == null) return true;
            return !CommandRegistry.TryHandleChat(player, message);
        }
    }

    /// <summary>
    /// Registers Oxide-style covalence + [ConsoleCommand] handlers on ConsoleSystem,
    /// and routes chat prefixes to the same methods.
    /// </summary>
    internal static class CommandRegistry
    {
        private static readonly List<ConsoleSystem.Command> Registered = new List<ConsoleSystem.Command>();
        private static readonly Dictionary<string, (string method, string permission)> ChatCommands =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

        public static void RegisterCovalence(string cmd, string methodName, string permission, object plugin)
        {
            if (string.IsNullOrWhiteSpace(cmd) || string.IsNullOrWhiteSpace(methodName) || plugin == null)
                return;

            string name = cmd.Trim().TrimStart('/');
            ChatCommands[name] = (methodName, permission);

            // Also expose as console command (server + client console).
            RegisterConsole(name, arg =>
            {
                var player = arg?.Player();
                IPlayer user = player != null ? new BasePlayerWrapper(player) : new ServerPlayerWrapper();
                if (!string.IsNullOrEmpty(permission) && player != null && !player.UserIDString.HasPermission(permission) && !player.IsAdmin)
                {
                    arg?.ReplyWith("No permission.");
                    return;
                }
                InvokeCommand(plugin, methodName, user, name, ArgToArgs(arg));
            }, allowServer: true);
        }

        public static void RegisterAttributedConsoleCommands(object plugin)
        {
            if (plugin == null) return;
            var type = plugin.GetType();
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var attr = method.GetCustomAttribute<ConsoleCommandAttribute>();
                if (attr == null || string.IsNullOrEmpty(attr.Command)) continue;
                var mi = method;
                RegisterConsole(attr.Command, arg =>
                {
                    try { mi.Invoke(plugin, new object[] { arg }); }
                    catch (Exception ex) { Debug.LogWarning($"[RaidableBases] {attr.Command}: {ex.InnerException?.Message ?? ex.Message}"); }
                }, allowServer: true);
            }
        }

        public static bool TryHandleChat(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrWhiteSpace(message)) return false;
            message = message.Trim();
            if (message.StartsWith("/")) message = message.Substring(1).Trim();
            string[] parts = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;

            if (!ChatCommands.TryGetValue(parts[0], out var entry))
                return false;

            if (!string.IsNullOrEmpty(entry.permission) && !player.UserIDString.HasPermission(entry.permission) && !player.IsAdmin)
            {
                player.ChatMessage("No permission.");
                return true;
            }

            var plugin = RaidableBasesHost.Instance?.ModInstance;
            if (plugin == null) return false;

            var args = parts.Skip(1).ToArray();
            InvokeCommand(plugin, entry.method, new BasePlayerWrapper(player), parts[0], args);
            return true;
        }

        public static void UnregisterAll()
        {
            foreach (var cmd in Registered)
            {
                try
                {
                    ConsoleSystem.Index.Server.Dict?.Remove(cmd.FullName);
                    if (cmd.FullName != null && cmd.FullName.StartsWith("global."))
                        ConsoleSystem.Index.Server.GlobalDict?.Remove(cmd.Name);
                }
                catch { }
            }
            Registered.Clear();
            ChatCommands.Clear();
        }

        private static void RegisterConsole(string name, Action<ConsoleSystem.Arg> handler, bool allowServer)
        {
            bool hasDot = name.Contains(".");
            string fullName = hasDot ? name : "global." + name;
            string dictKey = fullName;
            string globalKey = hasDot ? null : name;

            var cmd = new ConsoleSystem.Command
            {
                Name = hasDot ? name.Split('.')[1] : name,
                Parent = hasDot ? name.Split('.')[0] : "global",
                FullName = fullName,
                Variable = false,
                ServerAdmin = false,
                ServerUser = true,
                AllowRunFromServer = allowServer,
                Call = arg =>
                {
                    try { handler(arg); }
                    catch (Exception ex) { Debug.LogWarning($"[RaidableBases] command {name}: {ex.Message}"); }
                }
            };
            ConsoleSystem.Index.Server.Dict[dictKey] = cmd;
            if (globalKey != null && ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict[globalKey] = cmd;
            Registered.Add(cmd);
        }

        private static void InvokeCommand(object plugin, string methodName, IPlayer user, string command, string[] args)
        {
            try
            {
                var mi = plugin.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi == null)
                {
                    Debug.LogWarning($"[RaidableBases] Command method missing: {methodName}");
                    return;
                }
                mi.Invoke(plugin, new object[] { user, command, args ?? Array.Empty<string>() });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RaidableBases] {methodName}: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        private static string[] ArgToArgs(ConsoleSystem.Arg arg)
        {
            if (arg?.Args == null || arg.Args.Length == 0) return Array.Empty<string>();
            var a = new string[arg.Args.Length];
            for (int i = 0; i < arg.Args.Length; i++)
                a[i] = arg.Args[i].ToString();
            return a;
        }
    }

    /// <summary>Console/RCON stand-in when no player is attached.</summary>
    internal sealed class ServerPlayerWrapper : IPlayer
    {
        public string Id => "0";
        public object Object => null;
        public bool IsServer => true;
        public string Name => "Server";
        public bool IsAdmin => true;
        public bool IsConnected => false;
        public bool IsBanned => false;
        public void Reply(string message) { if (!string.IsNullOrEmpty(message)) Debug.Log("[RaidableBases] " + message); }
        public void Message(string msg) => Reply(msg);
        public void Teleport(float x, float y, float z) { }
    }
}
