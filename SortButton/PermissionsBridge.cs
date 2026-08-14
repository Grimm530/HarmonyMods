/*
 * Reflection bridge to PermissionsHarmony.PermissionsMod + Instance.Service.
 * Generation rebind + ready callbacks (Harmony framework §10a).
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SortButton
{
    public static class PermissionsBridge
    {
        private static Type _permType;
        private static object _service;
        private static int _boundGen = -1;
        private static bool _resolveAttempted;
        private static bool _loggedLink;

        private static MethodInfo _userHas;
        private static MethodInfo _register;
        private static MethodInfo _exists;
        private static MethodInfo _grantGroup;
        private static MethodInfo _groupExists;
        private static MethodInfo _createGroup;
        private static MethodInfo _svcUserHas;
        private static MethodInfo _svcRegister;
        private static MethodInfo _svcExists;
        private static MethodInfo _svcGrantGroup;
        private static MethodInfo _svcGroupExists;
        private static MethodInfo _svcCreateGroup;

        public static bool IsAvailable
        {
            get
            {
                EnsureBound();
                return _permType != null && _service != null;
            }
        }

        public static void RegisterReadyCallback(Action callback)
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
                Debug.LogWarning("[SortButton] RegisterReadyCallback: " + ex.Message);
            }

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
                Debug.LogWarning("[SortButton] RegisterReadyCallback fallback: " + ex.Message);
            }
        }

        public static void UnregisterReadyCallback(Action callback)
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
            _userHas = _register = _exists = _grantGroup = _groupExists = _createGroup = null;
            _svcUserHas = _svcRegister = _svcExists = _svcGrantGroup = _svcGroupExists = _svcCreateGroup = null;
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
                        Debug.LogWarning("[SortButton] Permissions mod not loaded — permission checks will fail until 0Permissions.dll is loaded.");
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
                    Debug.Log("[SortButton] Linked to Permissions Harmony mod.");
                }
                else
                    Debug.Log($"[SortButton] Re-linked to Permissions Harmony mod (gen={gen}).");
            }
            catch (Exception ex)
            {
                ClearBind();
                Debug.LogWarning("[SortButton] Permissions bind failed: " + ex.Message);
            }
        }

        private static void BindStatic()
        {
            const BindingFlags S = BindingFlags.Public | BindingFlags.Static;
            _userHas = _permType.GetMethod("UserHasPermission", S, null, new[] { typeof(string), typeof(string) }, null);
            _register = _permType.GetMethod("RegisterPermission", S, null, new[] { typeof(string) }, null);
            _exists = _permType.GetMethod("PermissionExists", S, null, new[] { typeof(string) }, null);
            _grantGroup = _permType.GetMethod("GrantGroupPermission", S, null, new[] { typeof(string), typeof(string) }, null);
            _groupExists = _permType.GetMethod("GroupExists", S, null, new[] { typeof(string) }, null);
            _createGroup = _permType.GetMethod("CreateGroup", S, null, new[] { typeof(string), typeof(string), typeof(int) }, null);
        }

        private static void BindService()
        {
            try
            {
                var instanceProp = _permType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                var instance = instanceProp?.GetValue(null);
                if (instance == null) return;
                var serviceProp = instance.GetType().GetProperty("Service", BindingFlags.Public | BindingFlags.Instance);
                _service = serviceProp?.GetValue(instance);
                if (_service == null) return;

                var st = _service.GetType();
                const BindingFlags I = BindingFlags.Public | BindingFlags.Instance;
                _svcUserHas = st.GetMethod("UserHasPermission", I, null, new[] { typeof(string), typeof(string) }, null);
                _svcRegister = st.GetMethod("RegisterPermission", I, null, new[] { typeof(string) }, null);
                _svcExists = st.GetMethod("PermissionExists", I, null, new[] { typeof(string) }, null);
                _svcGrantGroup = st.GetMethod("GrantGroupPermission", I, null, new[] { typeof(string), typeof(string) }, null);
                _svcGroupExists = st.GetMethod("GroupExists", I, null, new[] { typeof(string) }, null);
                _svcCreateGroup = st.GetMethod("CreateGroup", I, null, new[] { typeof(string), typeof(string), typeof(int) }, null);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SortButton] Permissions Service bind: " + ex.Message);
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

        public static bool UserHasPermission(string playerId, string perm)
        {
            if (string.IsNullOrEmpty(playerId)) return false;
            if (string.IsNullOrEmpty(perm)) return true;
            return InvokeBool(_userHas, _svcUserHas, playerId, perm);
        }

        public static void RegisterPermission(string perm)
        {
            if (string.IsNullOrEmpty(perm)) return;
            InvokeVoid(_register, _svcRegister, perm);
        }

        public static bool PermissionExists(string perm)
        {
            if (string.IsNullOrEmpty(perm)) return false;
            return InvokeBool(_exists, _svcExists, perm);
        }

        public static bool GrantGroupPermission(string group, string perm)
        {
            if (string.IsNullOrEmpty(group) || string.IsNullOrEmpty(perm)) return false;
            return InvokeBool(_grantGroup, _svcGrantGroup, group, perm);
        }

        public static bool GroupExists(string group)
        {
            if (string.IsNullOrEmpty(group)) return false;
            return InvokeBool(_groupExists, _svcGroupExists, group);
        }

        public static bool CreateGroup(string group, string title, int rank)
        {
            if (string.IsNullOrEmpty(group)) return false;
            return InvokeBool(_createGroup, _svcCreateGroup, group, title ?? group, rank);
        }
    }
}

