using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using OxidePlugin = Oxide.Plugins.MovementSpeed;

namespace MovementSpeedHarmony
{
    /// <summary>
    /// Harmony entry for MovementSpeed 1.0.9. Exposes Add/Remove run+swim boosts for SkillTree RoadRunner
    /// via AppDomain MovementSpeed_ApiType (static methods) that SkillTree PluginManager.Find resolves.
    /// Config: HarmonyConfig/MovementSpeed.json
    /// </summary>
    public class MovementSpeedMod : IHarmonyModHooks
    {
        public static MovementSpeedMod Instance { get; private set; }
        public static OxidePlugin Plugin { get; private set; }

        public const string AppDomainApiKey = "MovementSpeed_ApiType";
        public const string AppDomainReadyCallbacksKey = "MovementSpeed_ReadyCallbacks";

        private Coroutine _initCoroutine;
        private Action _permissionsReadyCallback;
        private readonly List<ConsoleSystem.Command> _commands = new List<ConsoleSystem.Command>();
        private readonly HashSet<string> _chatCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            ModRunner.Ensure();

            try
            {
                Plugin = new OxidePlugin();
                Plugin.HarmonyLoadConfig();
            }
            catch (Exception ex)
            {
                Debug.LogError("[MovementSpeed] Failed to construct/config: " + ex);
                return;
            }

            // Publish API immediately so SkillTree (loads later alphabetically) can Find it.
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(MovementSpeedMod)); }
            catch { }

            // Permissions loads AFTER MovementSpeed alphabetically — re-register perms when it is ready.
            _permissionsReadyCallback = OnPermissionsReady;
            RegisterPermissionsReadyCallback(_permissionsReadyCallback);

            _initCoroutine = ModRunner.Instance.StartCoroutine(WaitForServerThenInit());
            Debug.Log("[MovementSpeed] Harmony mod loaded. Config: HarmonyConfig/MovementSpeed.json. No load-order requirement (ready callbacks).");
        }

        private void OnPermissionsReady()
        {
            try
            {
                Plugin?.EnsurePermissionsRegistered();
                Debug.Log("[MovementSpeed] Permissions ready — speed perms re-registered.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MovementSpeed] Permissions ready: " + ex.Message);
            }
        }

        private IEnumerator WaitForServerThenInit()
        {
            while (ServerMgr.Instance == null) yield return null;
            // Wait past alphabetical peers (Permissions, SkillTree) finishing OnLoaded.
            yield return new WaitForSeconds(2f);

            try { Plugin?.CallInit(); }
            catch (Exception ex) { Debug.LogWarning("[MovementSpeed] Init: " + ex.Message); }

            try { Plugin?.EnsurePermissionsRegistered(); }
            catch (Exception ex) { Debug.LogWarning("[MovementSpeed] EnsurePermissions: " + ex.Message); }

            try { Plugin?.CallOnServerInitialized(); }
            catch (Exception ex) { Debug.LogError("[MovementSpeed] OnServerInitialized: " + ex); }

            RegisterCommands();
            NotifyReady();
            _initCoroutine = null;
            Debug.Log("[MovementSpeed] Server initialized.");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            if (_permissionsReadyCallback != null)
            {
                // leave callback in list (harmless); clear local ref
                _permissionsReadyCallback = null;
            }

            if (_initCoroutine != null && ModRunner.Instance != null)
            {
                ModRunner.Instance.StopCoroutine(_initCoroutine);
                _initCoroutine = null;
            }

            try { Plugin?.CallUnload(); }
            catch (Exception ex) { Debug.LogWarning("[MovementSpeed] Unload: " + ex.Message); }

            UnregisterCommands();
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, null); } catch { }

            ModRunner.Destroy();
            Plugin = null;
            Instance = null;
            Debug.Log("[MovementSpeed] Harmony mod unloaded.");
        }

        #region Ready callbacks (SkillTree / other consumers — order-independent)

        /// <summary>
        /// Register to be notified when MovementSpeed API is ready (or immediately if already up).
        /// SkillTree uses this so RoadRunner binds even if SkillTree was harmony.load'd before MovementSpeed.
        /// </summary>
        public static void RegisterReadyCallback(Action callback)
        {
            if (callback == null) return;
            try
            {
                var list = AppDomain.CurrentDomain.GetData(AppDomainReadyCallbacksKey) as System.Collections.IList;
                if (list == null)
                {
                    list = new List<Action>();
                    AppDomain.CurrentDomain.SetData(AppDomainReadyCallbacksKey, list);
                }
                lock (list)
                {
                    if (!list.Contains(callback))
                        list.Add(callback);
                }
            }
            catch { }

            if (Instance != null && Plugin != null)
            {
                try { callback(); }
                catch (Exception ex) { Debug.LogWarning("[MovementSpeed] Ready callback (immediate): " + ex.Message); }
            }
        }

        private static void NotifyReady()
        {
            try
            {
                var list = AppDomain.CurrentDomain.GetData(AppDomainReadyCallbacksKey) as System.Collections.IList;
                if (list == null) return;
                Action[] copy;
                lock (list)
                {
                    copy = new Action[list.Count];
                    for (int i = 0; i < list.Count; i++)
                        copy[i] = list[i] as Action;
                }
                foreach (var a in copy)
                {
                    if (a == null) continue;
                    try { a(); }
                    catch (Exception ex) { Debug.LogWarning("[MovementSpeed] Ready callback: " + ex.Message); }
                }
            }
            catch (Exception ex) { Debug.LogWarning("[MovementSpeed] NotifyReady: " + ex.Message); }
        }

        private static void RegisterPermissionsReadyCallback(Action callback)
        {
            if (callback == null) return;
            try
            {
                var permType = AppDomain.CurrentDomain.GetData("Permissions_ApiType") as Type;
                var mi = permType?.GetMethod("RegisterReadyCallback", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(Action) }, null);
                if (mi != null)
                {
                    mi.Invoke(null, new object[] { callback });
                    return;
                }
            }
            catch { }

            try
            {
                var list = AppDomain.CurrentDomain.GetData("Permissions_ReadyCallbacks") as System.Collections.IList;
                if (list == null)
                {
                    list = new List<Action>();
                    AppDomain.CurrentDomain.SetData("Permissions_ReadyCallbacks", list);
                }
                lock (list)
                {
                    if (!list.Contains(callback))
                        list.Add(callback);
                }
            }
            catch { }

            // Permissions already up (manual load / late subscribe)
            if (AppDomain.CurrentDomain.GetData("Permissions_ApiType") is Type)
            {
                try { callback(); } catch { }
            }
        }

        #endregion

        #region Static API for SkillTree PluginBridgeApi

        public static void AddRunSpeedBoost(BasePlayer player, string plugin, float mod, float duration, bool force)
            => Plugin?.AddRunSpeedBoost(player, plugin, mod, duration, force);

        public static void RemoveRunSpeed(BasePlayer player, string plugin)
            => Plugin?.RemoveRunSpeed(player, plugin);

        public static void AddSwimSpeedBoost(BasePlayer player, string plugin, float mod, float duration, bool force)
            => Plugin?.AddSwimSpeedBoost(player, plugin, mod, duration, force);

        public static void RemoveSwimSpeed(BasePlayer player, string plugin)
            => Plugin?.RemoveSwimSpeed(player, plugin);

        public static void PauseSpeedBoost(ulong id, bool pause)
            => Plugin?.PauseSpeedBoost(id, pause);

        #endregion

        #region Commands

        private void RegisterCommands()
        {
            UnregisterCommands();
            _chatCommands.Clear();

            if (Plugin?.cmd != null)
            {
                foreach (var reg in Plugin.cmd.RegisteredChatCommands)
                    _chatCommands.Add(reg.name);
            }

            // Attribute console commands
            RegisterConsole("msdisablerun", arg => Plugin?.InvokeConsole("DisableRunCMD", arg));
            RegisterConsole("msdisableswim", arg => Plugin?.InvokeConsole("DisableSwimCMD", arg));
            RegisterConsole("msenablerun", arg => Plugin?.InvokeConsole("EnableRunCMD", arg));
            RegisterConsole("msenableswim", arg => Plugin?.InvokeConsole("EnableSwimCMD", arg));
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
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                if (dict != null) dict["global." + name] = cmd;
                if (globalDict != null) globalDict[name] = cmd;
            }
            catch (Exception ex) { Debug.LogWarning("[MovementSpeed] RegisterConsole(" + name + "): " + ex.Message); }
        }

        private void UnregisterCommands()
        {
            try
            {
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                foreach (var cmd in _commands)
                {
                    dict?.Remove(cmd.FullName);
                    globalDict?.Remove(cmd.Name);
                }
            }
            catch { }
            _commands.Clear();
        }

        public bool OnChatCommand(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrWhiteSpace(message) || Plugin == null) return false;
            message = message.Trim();
            if (message.StartsWith("/")) message = message.Substring(1).Trim();
            string[] parts = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            string name = parts[0];
            if (!_chatCommands.Contains(name)) return false;

            foreach (var reg in Plugin.cmd.RegisteredChatCommands)
            {
                if (!string.Equals(reg.name, name, StringComparison.OrdinalIgnoreCase)) continue;
                Plugin.InvokeChat(reg.method, player, name, parts.Skip(1).ToArray());
                return true;
            }
            return false;
        }

        #endregion
    }
}

namespace Oxide.Plugins
{
    public partial class MovementSpeed
    {
        public void CallInit() => Init();
        public void CallOnServerInitialized() => OnServerInitialized(true);
        public void CallUnload() => Unload();

        /// <summary>
        /// Safe to call multiple times. Permissions often loads after MovementSpeed alphabetically.
        /// </summary>
        public void EnsurePermissionsRegistered()
        {
            if (config == null) return;
            try
            {
                if (!permission.PermissionExists(perm_admin, this))
                    permission.RegisterPermission(perm_admin, this);

                if (config.run_permissions != null)
                {
                    foreach (var perm in config.run_permissions.Keys)
                        if (!permission.PermissionExists(perm)) permission.RegisterPermission(perm, this);
                }
                if (config.swim_permissions != null)
                {
                    foreach (var perm in config.swim_permissions.Keys)
                        if (!permission.PermissionExists(perm)) permission.RegisterPermission(perm, this);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MovementSpeed] EnsurePermissionsRegistered: " + ex.Message);
            }
        }

        public void InvokeChat(string method, BasePlayer player, string command, string[] args)
        {
            var mi = GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi == null) return;
            var ps = mi.GetParameters();
            try
            {
                if (ps.Length == 3) mi.Invoke(this, new object[] { player, command, args });
                else if (ps.Length == 1 && ps[0].ParameterType == typeof(BasePlayer)) mi.Invoke(this, new object[] { player });
                else mi.Invoke(this, null);
            }
            catch (Exception ex) { Debug.LogWarning("[MovementSpeed] InvokeChat " + method + ": " + ex.Message); }
        }

        public void InvokeConsole(string method, ConsoleSystem.Arg arg)
        {
            var mi = GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi == null) return;
            try { mi.Invoke(this, new object[] { arg }); }
            catch (Exception ex) { Debug.LogWarning("[MovementSpeed] InvokeConsole " + method + ": " + ex.Message); }
        }

        // Called from Harmony patches
        public void DispatchConnected(BasePlayer player) => OnPlayerConnected(player);
        public void DispatchDisconnected(BasePlayer player, string reason) => OnPlayerDisconnected(player, reason);
    }
}
