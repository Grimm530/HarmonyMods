using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyChat;
using UnityEngine;

namespace SubmersiblePump
{
    public class SubmersiblePumpMod : IHarmonyModHooks
    {
        public static SubmersiblePumpMod Instance { get; private set; }
        public const int VersionMajor = 1;
        public const int VersionMinor = 1;
        public const int VersionPatch = 1;

        private SubmersiblePumpPlugin _plugin;
        private Action _permissionsReadyCallback;
        private readonly List<ConsoleSystem.Command> _commands = new List<ConsoleSystem.Command>();
        private readonly List<ConsoleSystem.Command> _chatAliasCommands = new List<ConsoleSystem.Command>();
        private readonly HashSet<string> _chatCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private GameObject _runner;
        private bool _serverReady;

        public SubmersiblePumpPlugin Plugin => _plugin;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            try
            {
                _plugin = new SubmersiblePumpPlugin(root);
                _plugin.Load();
            }
            catch (Exception ex)
            {
                Debug.LogError("[SubmersiblePump] FAIL: construct/config: " + ex);
                return;
            }

            _permissionsReadyCallback = OnPermissionsReady;
            PermissionsBridge.RegisterReadyCallback(_permissionsReadyCallback);
            EnsureRunner();
            _runner.GetComponent<SubmersiblePumpRunner>().Begin(this);
            ChatSayBridge.Register("SubmersiblePump", OnChatCommand);
            RefreshChatCommands();
            RegisterConsoleCommands();
            Debug.Log($"[SubmersiblePump] OK: Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
        }

        private void OnPermissionsReady()
        {
            try
            {
                _plugin?.RegisterPermissions();
                Debug.Log("[SubmersiblePump] OK: Permissions ready.");
            }
            catch (Exception ex) { Debug.LogWarning("[SubmersiblePump] Permissions ready: " + ex.Message); }
        }

        internal void OnServerInitialized()
        {
            if (_serverReady || _plugin == null) return;
            _serverReady = true;
            try
            {
                _plugin.RegisterPermissions();
                _plugin.OnServerInitialized();
                RefreshChatCommands();
                Debug.Log("[SubmersiblePump] OK: Server initialized.");
            }
            catch (Exception ex) { Debug.LogError("[SubmersiblePump] FAIL: OnServerInitialized: " + ex); }
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            try { ChatSayBridge.Unregister("SubmersiblePump"); } catch { }
            try
            {
                if (_permissionsReadyCallback != null)
                    PermissionsBridge.UnregisterReadyCallback(_permissionsReadyCallback);
            }
            catch { }
            UnregisterConsoleCommands();
            try { _plugin?.Unload(); } catch { }
            _plugin = null;
            if (_runner != null)
            {
                UnityEngine.Object.Destroy(_runner);
                _runner = null;
            }
            Instance = null;
            Debug.Log("[SubmersiblePump] OK: Unloaded.");
        }

        public void RefreshChatCommands()
        {
            _chatCommands.Clear();
            string cmd = _plugin?.ConfigData?.command;
            if (string.IsNullOrWhiteSpace(cmd)) return;
            cmd = cmd.Trim();
            _chatCommands.Add(cmd);
            RegisterChatAliasConsole(cmd);
        }

        public bool OnChatCommand(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrWhiteSpace(message)) return false;
            message = message.Trim();
            if (message.StartsWith("/") || message.StartsWith("\\"))
                message = message.Substring(1).Trim();
            if (message.Length == 0) return false;

            string[] parts = message.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            string[] args = parts.Length > 1 ? new string[parts.Length - 1] : Array.Empty<string>();
            for (int i = 1; i < parts.Length; i++)
                args[i - 1] = parts[i];
            return TryHandleChat(player, parts[0], args);
        }

        public bool TryHandleChat(BasePlayer player, string command, string[] args)
        {
            if (player == null || _plugin == null || string.IsNullOrEmpty(command)) return false;
            if (!_chatCommands.Contains(command)) return false;
            _plugin.CraftPumpCommand(player, command, args);
            return true;
        }

        internal void NextTick(Action action)
        {
            var runner = _runner != null ? _runner.GetComponent<SubmersiblePumpRunner>() : null;
            runner?.Delay(action, 0f);
        }

        private void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("SubmersiblePump_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<SubmersiblePumpRunner>();
        }

        private void RegisterConsoleCommands()
        {
            UnregisterConsoleCommands();
            RegisterConsole("givepump", arg => _plugin?.GivePumpCommand(arg));
            RefreshChatCommands();
        }

        private void RegisterChatAliasConsole(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (name.IndexOf('.') >= 0) return;

            for (int i = 0; i < _chatAliasCommands.Count; i++)
            {
                var existing = _chatAliasCommands[i];
                if (string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(existing.FullName, "global." + name, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            string localName = name;
            if (ConsoleSystem.Index.Server.Dict != null &&
                ConsoleSystem.Index.Server.Dict.ContainsKey("global." + localName))
                return;

            var cmd = new ConsoleSystem.Command
            {
                Name = localName,
                Parent = string.Empty,
                FullName = "global." + localName,
                Variable = false,
                ServerAdmin = false,
                ServerUser = true,
                AllowRunFromServer = true,
                Replicated = false,
                Call = a =>
                {
                    try
                    {
                        var player = a?.Player();
                        if (player == null) return;
                        var sb = new StringBuilder(localName);
                        var raw = a.Args;
                        if (raw != null)
                        {
                            for (int i = 0; i < raw.Length; i++)
                            {
                                sb.Append(' ');
                                sb.Append(raw[i].ToString() ?? string.Empty);
                            }
                        }
                        OnChatCommand(player, sb.ToString());
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[SubmersiblePump] chat alias " + localName + ": " + ex.Message);
                    }
                }
            };

            try
            {
                ConsoleSystem.Index.Server.Dict["global." + localName] = cmd;
                if (ConsoleSystem.Index.Server.GlobalDict != null)
                    ConsoleSystem.Index.Server.GlobalDict[localName] = cmd;
                _chatAliasCommands.Add(cmd);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SubmersiblePump] RegisterChatAliasConsole(" + localName + "): " + ex.Message);
            }
        }

        private void RegisterConsole(string name, Action<ConsoleSystem.Arg> handler)
        {
            try
            {
                var cmd = new ConsoleSystem.Command
                {
                    Name = name,
                    FullName = "global." + name,
                    Variable = false,
                    ServerAdmin = true,
                    ServerUser = true,
                    AllowRunFromServer = true,
                    Call = a => handler(a)
                };
                _commands.Add(cmd);
                ConsoleSystem.Index.Server.Dict[cmd.FullName] = cmd;
                ConsoleSystem.Index.Server.GlobalDict[cmd.Name] = cmd;
            }
            catch (Exception ex) { Debug.LogWarning("[SubmersiblePump] RegisterConsole: " + ex.Message); }
        }

        private void UnregisterConsoleCommands()
        {
            try
            {
                for (int i = 0; i < _commands.Count; i++)
                {
                    var cmd = _commands[i];
                    ConsoleSystem.Index.Server.Dict?.Remove(cmd.FullName);
                    ConsoleSystem.Index.Server.GlobalDict?.Remove(cmd.Name);
                }
                for (int i = 0; i < _chatAliasCommands.Count; i++)
                {
                    var cmd = _chatAliasCommands[i];
                    ConsoleSystem.Index.Server.Dict?.Remove(cmd.FullName);
                    ConsoleSystem.Index.Server.GlobalDict?.Remove(cmd.Name);
                }
            }
            catch { }
            _commands.Clear();
            _chatAliasCommands.Clear();
        }
    }

    internal sealed class SubmersiblePumpRunner : MonoBehaviour
    {
        private SubmersiblePumpMod _mod;
        private bool _started;

        public void Begin(SubmersiblePumpMod mod)
        {
            _mod = mod;
            if (!_started)
            {
                _started = true;
                StartCoroutine(WaitForServer());
            }
        }

        private IEnumerator WaitForServer()
        {
            while (ServerMgr.Instance == null)
                yield return null;
            yield return new WaitForSeconds(1f);
            _mod?.OnServerInitialized();
        }

        public void Delay(Action action, float delay)
        {
            if (action == null) return;
            StartCoroutine(DelayCo(action, delay));
        }

        private IEnumerator DelayCo(Action action, float delay)
        {
            if (delay <= 0f) yield return null;
            else yield return new WaitForSeconds(delay);
            try { action(); } catch (Exception ex) { Debug.LogWarning("[SubmersiblePump] delayed: " + ex.Message); }
        }
    }
}
