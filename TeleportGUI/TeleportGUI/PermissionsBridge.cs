/*
 * Reflection consumer for PermissionsHarmony.PermissionsMod (0Permissions).
 * Generation rebind + ready callbacks per Harmony_Mod_Execution_Framework.md section 10a.
 * No Oxide references. Signatures match AdminMenu / Kits / Backpacks / SkillTree bridges.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace TeleportGUI
{
    /// <summary>
    /// Lazy reflection bridge to 0Permissions. Call <see cref="Initialize"/> from OnLoaded
    /// and <see cref="Shutdown"/> from OnUnloaded. Re-registers supplied permissions when
    /// 0Permissions loads or reloads (Cecil rename / generation bump).
    /// </summary>
    public static class PermissionsBridge
    {
        private const string LogTag = "[TeleportGUI]";

        private static Type _permType;
        private static object _service;
        private static int _boundGen = -1;
        private static bool _resolveAttempted;
        private static bool _loggedLink;

        private static MethodInfo _userHas;
        private static MethodInfo _register;
        private static MethodInfo _svcUserHas;
        private static MethodInfo _svcRegister;

        private static readonly HashSet<string> _permissions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static Action _readyCallback;
        private static bool _initialized;

        public static bool IsAvailable
        {
            get
            {
                EnsureBound();
                return _permType != null && _service != null;
            }
        }

        /// <summary>
        /// Store permission names, subscribe for 0Permissions ready/reload, and register now if available.
        /// Safe to call again with an updated list (replaces prior set).
        /// </summary>
        public static void Initialize(IEnumerable<string> permissions)
        {
            _permissions.Clear();
            if (permissions != null)
            {
                foreach (var p in permissions)
                {
                    if (!string.IsNullOrEmpty(p))
                        _permissions.Add(p);
                }
            }

            if (_readyCallback == null)
                _readyCallback = OnPermissionsReady;

            RegisterReadyCallback(_readyCallback);
            _initialized = true;
            RegisterAllStored();
        }

        /// <summary>
        /// Unsubscribe ready callback and clear bind/cache. Call from mod OnUnloaded.
        /// </summary>
        public static void Shutdown()
        {
            if (_readyCallback != null)
                UnregisterReadyCallback(_readyCallback);
            _readyCallback = null;
            _permissions.Clear();
            _initialized = false;
            ClearBind();
            _boundGen = -1;
            _resolveAttempted = false;
            _loggedLink = false;
        }

        public static void RegisterPermission(string perm)
        {
            if (string.IsNullOrEmpty(perm)) return;
            _permissions.Add(perm);
            EnsureBound();
            InvokeVoid(_register, _svcRegister, perm);
        }

        public static bool UserHasPermission(BasePlayer player, string perm)
        {
            if (player == null) return false;
            if (string.IsNullOrEmpty(perm)) return true;
            string userId = player.UserIDString;
            if (string.IsNullOrEmpty(userId)) return false;
            return InvokeBool(_userHas, _svcUserHas, userId, perm);
        }

        private static void OnPermissionsReady()
        {
            if (!_initialized) return;
            ClearBind();
            EnsureBound();
            RegisterAllStored();
        }

        private static void RegisterAllStored()
        {
            EnsureBound();
            if (_register == null && _svcRegister == null) return;
            foreach (var perm in _permissions)
                InvokeVoid(_register, _svcRegister, perm);
        }

        private static void RegisterReadyCallback(Action callback)
        {
            if (callback == null) return;
            EnsureBound();
            try
            {
                var mi = _permType?.GetMethod("RegisterReadyCallback", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(Action) }, null);
                if (mi != null)
                {
                    mi.Invoke(null, new object[] { callback });
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(LogTag + " RegisterReadyCallback: " + ex.Message);
            }

            // 0Permissions not loaded yet - stash for next OnLoaded InvokeReadyCallbacks.
            try
            {
                var list = AppDomain.CurrentDomain.GetData("Permissions_ReadyCallbacks") as IList;
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
            catch (Exception ex)
            {
                Debug.LogWarning(LogTag + " RegisterReadyCallback fallback: " + ex.Message);
            }
        }

        private static void UnregisterReadyCallback(Action callback)
        {
            if (callback == null) return;
            try
            {
                var type = AppDomain.CurrentDomain.GetData("Permissions_ApiType") as Type ?? _permType;
                var mi = type?.GetMethod("UnregisterReadyCallback", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(Action) }, null);
                mi?.Invoke(null, new object[] { callback });
            }
            catch { }

            try
            {
                var list = AppDomain.CurrentDomain.GetData("Permissions_ReadyCallbacks") as IList;
                if (list != null)
                {
                    lock (list)
                        list.Remove(callback);
                }
            }
            catch { }
        }

        private static int ReadGeneration()
        {
            try
            {
                if (AppDomain.CurrentDomain.GetData("Permissions_Generation") is int g)
                    return g;
            }
            catch { }
            return 0;
        }

        private static object ReadLiveInstance(Type type)
        {
            if (type == null) return null;
            try
            {
                return type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            }
            catch { return null; }
        }

        private static object ReadLiveService(object instance)
        {
            if (instance == null) return null;
            try
            {
                return instance.GetType().GetProperty("Service", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);
            }
            catch { return null; }
        }

        private static Type ResolveLivePermType()
        {
            var fromDomain = AppDomain.CurrentDomain.GetData("Permissions_ApiType") as Type;
            if (fromDomain != null && ReadLiveInstance(fromDomain) != null)
                return fromDomain;

            Type fallback = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType("PermissionsHarmony.PermissionsMod");
                    if (t == null) continue;
                    if (ReadLiveInstance(t) != null)
                        return t;
                    fallback ??= t;
                }
                catch { }
            }
            return fromDomain ?? fallback;
        }

        private static void ClearBind()
        {
            _permType = null;
            _service = null;
            _userHas = _register = null;
            _svcUserHas = _svcRegister = null;
        }

        private static void EnsureBound()
        {
            int gen = ReadGeneration();
            object liveInstance = ReadLiveInstance(_permType);
            object liveService = ReadLiveService(liveInstance);
            bool alive = _permType != null && _service != null && _boundGen == gen
                         && liveInstance != null && ReferenceEquals(_service, liveService);
            if (alive) return;

            try
            {
                ClearBind();
                _permType = ResolveLivePermType();
                liveInstance = ReadLiveInstance(_permType);

                if (_permType == null || liveInstance == null)
                {
                    if (!_resolveAttempted)
                    {
                        _resolveAttempted = true;
                        Debug.LogWarning(LogTag + " Permissions mod not loaded - permission checks will fail until 0Permissions.dll is loaded.");
                    }
                    return;
                }

                _resolveAttempted = false;
                BindStatic();
                BindService();
                _boundGen = gen;

                if (!_loggedLink)
                {
                    _loggedLink = true;
                    Debug.Log(LogTag + " Linked to Permissions Harmony mod.");
                }
                else
                    Debug.Log(LogTag + " Re-linked to Permissions Harmony mod (gen=" + gen + ").");
            }
            catch (Exception ex)
            {
                ClearBind();
                Debug.LogWarning(LogTag + " Permissions bind failed: " + ex.Message);
            }
        }

        private static void BindStatic()
        {
            const BindingFlags S = BindingFlags.Public | BindingFlags.Static;
            _userHas = _permType.GetMethod("UserHasPermission", S, null, new[] { typeof(string), typeof(string) }, null);
            _register = _permType.GetMethod("RegisterPermission", S, null, new[] { typeof(string) }, null);
        }

        private static void BindService()
        {
            try
            {
                var instance = ReadLiveInstance(_permType);
                if (instance == null) return;
                _service = ReadLiveService(instance);
                if (_service == null) return;

                var st = _service.GetType();
                const BindingFlags I = BindingFlags.Public | BindingFlags.Instance;
                _svcUserHas = st.GetMethod("UserHasPermission", I, null, new[] { typeof(string), typeof(string) }, null);
                _svcRegister = st.GetMethod("RegisterPermission", I, null, new[] { typeof(string) }, null);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(LogTag + " Permissions Service bind: " + ex.Message);
            }
        }

        private static bool InvokeBool(MethodInfo staticMi, MethodInfo serviceMi, params object[] args)
        {
            EnsureBound();
            try
            {
                if (staticMi != null && staticMi.Invoke(null, args) is bool sb)
                    return sb;
            }
            catch { }
            try
            {
                if (_service != null && serviceMi != null && serviceMi.Invoke(_service, args) is bool ib)
                    return ib;
            }
            catch { }
            return false;
        }

        private static void InvokeVoid(MethodInfo staticMi, MethodInfo serviceMi, params object[] args)
        {
            EnsureBound();
            try
            {
                if (staticMi != null)
                {
                    staticMi.Invoke(null, args);
                    return;
                }
            }
            catch { }
            try
            {
                if (_service != null && serviceMi != null)
                    serviceMi.Invoke(_service, args);
            }
            catch { }
        }
    }
}
