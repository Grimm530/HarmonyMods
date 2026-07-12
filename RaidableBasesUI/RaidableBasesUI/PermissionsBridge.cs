using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RaidableBasesBuyableUI
{
    /// <summary>Bridge to PermissionsHarmony.PermissionsMod (AppDomain API).</summary>
    internal static class PermissionsBridge
    {
        private static Type _permType;
        private static MethodInfo _userHas;
        private static MethodInfo _register;
        private static MethodInfo _grantGroup;
        private static MethodInfo _groupHas;
        private static MethodInfo _registerReady;
        private static MethodInfo _unregisterReady;
        private static int _boundGen = -1;
        private static bool _resolvedOk;

        /// <summary>True when Permissions mod API methods were found.</summary>
        public static bool IsAvailable
        {
            get
            {
                Resolve(force: false);
                return _resolvedOk;
            }
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

        private static void Resolve(bool force)
        {
            int gen = ReadGeneration();
            object live = ReadLiveInstance(_permType);
            if (!force && _resolvedOk && _boundGen == gen && live != null) return;

            try
            {
                _permType = null;
                _userHas = _register = _grantGroup = _groupHas = _registerReady = _unregisterReady = null;
                _resolvedOk = false;

                _permType = ResolveLivePermType();
                live = ReadLiveInstance(_permType);
                if (_permType == null || live == null)
                    return;

                BindingFlags sf = BindingFlags.Public | BindingFlags.Static;
                _userHas = _permType.GetMethod("UserHasPermission", sf, null, new[] { typeof(string), typeof(string) }, null);
                _register = _permType.GetMethod("RegisterPermission", sf, null, new[] { typeof(string) }, null);
                _grantGroup = _permType.GetMethod("GrantGroupPermission", sf, null, new[] { typeof(string), typeof(string) }, null);
                _groupHas = _permType.GetMethod("GroupHasPermission", sf, null, new[] { typeof(string), typeof(string) }, null);
                _registerReady = _permType.GetMethod("RegisterReadyCallback", sf, null, new[] { typeof(Action) }, null);
                _unregisterReady = _permType.GetMethod("UnregisterReadyCallback", sf, null, new[] { typeof(Action) }, null);
                _resolvedOk = _register != null && _userHas != null;
                _boundGen = gen;
            }
            catch (Exception ex)
            {
                _resolvedOk = false;
                Debug.LogWarning("[RaidableBasesBuyableUI] Permissions resolve: " + ex.Message);
            }
        }

        public static void RegisterReadyCallback(Action callback)
        {
            if (callback == null) return;
            Resolve(force: true);
            try
            {
                if (_registerReady != null)
                {
                    _registerReady.Invoke(null, new object[] { callback });
                    return;
                }
            }
            catch (Exception ex) { Debug.LogWarning("[RaidableBasesBuyableUI] RegisterReadyCallback: " + ex.Message); }

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
            catch (Exception ex)
            {
                Debug.LogWarning("[RaidableBasesBuyableUI] RegisterReadyCallback fallback: " + ex.Message);
            }
        }

        public static void UnregisterReadyCallback(Action callback)
        {
            if (callback == null) return;
            try
            {
                Resolve(force: false);
                _unregisterReady?.Invoke(null, new object[] { callback });
            }
            catch { }
        }

        public static void RegisterPermission(string perm)
        {
            Resolve(force: true);
            try { _register?.Invoke(null, new object[] { perm }); }
            catch (Exception ex) { Debug.LogWarning("[RaidableBasesBuyableUI] RegisterPermission: " + ex.Message); }
        }

        public static bool GrantGroupPermission(string group, string perm)
        {
            Resolve(force: true);
            try
            {
                if (_grantGroup != null && _grantGroup.Invoke(null, new object[] { group, perm }) is true)
                    return true;
                // Already granted counts as success
                if (_groupHas != null && _groupHas.Invoke(null, new object[] { group, perm }) is true)
                    return true;
            }
            catch (Exception ex) { Debug.LogWarning("[RaidableBasesBuyableUI] GrantGroupPermission: " + ex.Message); }
            return false;
        }

        public static bool GroupHasPermission(string group, string perm)
        {
            if (string.IsNullOrEmpty(group) || string.IsNullOrEmpty(perm)) return false;
            Resolve(force: false);
            try
            {
                if (_groupHas != null)
                    return _groupHas.Invoke(null, new object[] { group, perm }) is true;
            }
            catch { }
            return false;
        }

        public static bool UserHasPermission(string userId, string perm)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(perm)) return false;
            Resolve(force: false);
            try
            {
                if (_userHas != null)
                    return _userHas.Invoke(null, new object[] { userId, perm }) is true;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// For gallery access: if Permissions is unavailable, allow everyone.
        /// When available, require the permission (normally granted to group default).
        /// </summary>
        public static bool UserHasPermissionOrDefaultAllow(string userId, string perm)
        {
            Resolve(force: false);
            if (!_resolvedOk || _userHas == null)
                return true;
            return UserHasPermission(userId, perm);
        }
    }
}
