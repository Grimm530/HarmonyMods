/*
 * Bridge to the 0Permissions Harmony mod (PermissionsHarmony.PermissionsMod).
 *
 * Late-bound through reflection so PersonalNPC.dll never references 0Permissions.dll.
 * Supports the section 10a generation rebind protocol: when 0Permissions is reloaded it
 * bumps AppDomain key "Permissions_Generation" and publishes a fresh "Permissions_ApiType".
 * We re-resolve on generation change and replay every permission we registered.
 */
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace PersonalNPCHarmony
{
    public class HarmonyPermissionHelper
    {
        private readonly HashSet<string> _registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _granted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Action _readyCallback;

        private static Type _permType;
        private static MethodInfo _userHas;
        private static MethodInfo _register;
        private static MethodInfo _exists;
        private static MethodInfo _grantUser;
        private static MethodInfo _revokeUser;
        private static MethodInfo _registerReady;
        private static int _boundGen = -1;
        private static bool _resolveAttempted;
        private static bool _loggedLink;

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

        private static void EnsureBound()
        {
            int gen = ReadGeneration();
            object live = ReadLiveInstance(_permType);
            if (_permType != null && _boundGen == gen && live != null) return;

            try
            {
                _permType = null;
                _userHas = _register = _exists = _grantUser = _revokeUser = _registerReady = null;
                _permType = ResolveLivePermType();
                live = ReadLiveInstance(_permType);
                if (_permType == null || live == null)
                {
                    if (!_resolveAttempted)
                    {
                        _resolveAttempted = true;
                        Debug.LogWarning("[PersonalNPC] Permissions mod not loaded - personalnpc.* grants are inactive until 0Permissions.dll is loaded.");
                    }
                    return;
                }

                _resolveAttempted = false;
                BindingFlags sf = BindingFlags.Public | BindingFlags.Static;
                _userHas = _permType.GetMethod("UserHasPermission", sf, null, new[] { typeof(string), typeof(string) }, null);
                _register = _permType.GetMethod("RegisterPermission", sf, null, new[] { typeof(string) }, null);
                _exists = _permType.GetMethod("PermissionExists", sf, null, new[] { typeof(string) }, null);
                _grantUser = _permType.GetMethod("GrantUserPermission", sf, null, new[] { typeof(string), typeof(string) }, null);
                _revokeUser = _permType.GetMethod("RevokeUserPermission", sf, null, new[] { typeof(string), typeof(string) }, null);
                _registerReady = _permType.GetMethod("RegisterReadyCallback", sf, null, new[] { typeof(Action) }, null);
                _boundGen = gen;

                if (!_loggedLink)
                {
                    _loggedLink = true;
                    Debug.Log("[PersonalNPC] Linked to Permissions Harmony mod.");
                }
                else
                    Debug.Log("[PersonalNPC] Re-linked to Permissions Harmony mod (gen=" + gen + ").");
            }
            catch (Exception ex)
            {
                _permType = null;
                Debug.LogWarning("[PersonalNPC] Permissions bind failed: " + ex.Message);
            }
        }

        private void EnsureReadyCallback()
        {
            if (_readyCallback != null) return;
            _readyCallback = ReplayRegistered;
            EnsureBound();
            try
            {
                if (_registerReady != null)
                {
                    _registerReady.Invoke(null, new object[] { _readyCallback });
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
                    if (!list.Contains(_readyCallback))
                        list.Add(_readyCallback);
                }
            }
            catch { }
        }

        private void ReplayRegistered()
        {
            EnsureBound();
            foreach (var perm in _registered)
            {
                try { _register?.Invoke(null, new object[] { perm }); } catch { }
            }
        }

        public void RegisterPermission(string perm, object plugin = null)
        {
            if (string.IsNullOrEmpty(perm)) return;
            _registered.Add(perm);
            EnsureBound();
            EnsureReadyCallback();
            try { _register?.Invoke(null, new object[] { perm }); } catch { }
        }

        public bool PermissionExists(string perm)
        {
            if (string.IsNullOrEmpty(perm)) return false;
            if (_registered.Contains(perm)) return true;
            EnsureBound();
            try
            {
                if (_exists != null && _exists.Invoke(null, new object[] { perm }) is bool b)
                    return b;
            }
            catch { }
            return false;
        }

        public bool UserHasPermission(string userId, string perm)
        {
            if (string.IsNullOrEmpty(userId)) return false;
            if (string.IsNullOrEmpty(perm)) return true;

            if (_granted.Contains(userId + ":" + perm)) return true;

            EnsureBound();
            try
            {
                if (_userHas != null && _userHas.Invoke(null, new object[] { userId, perm }) is bool ok)
                    return ok;
            }
            catch { }

            return false;
        }

        public void GrantUserPermission(string userId, string perm) => GrantUserPermission(userId, perm, null);

        /// <summary>Oxide signature: permission.GrantUserPermission(id, perm, plugin).</summary>
        public void GrantUserPermission(string userId, string perm, object plugin)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(perm)) return;
            _granted.Add(userId + ":" + perm);
            EnsureBound();
            try { _grantUser?.Invoke(null, new object[] { userId, perm }); } catch { }
        }

        public void RevokeUserPermission(string userId, string perm) => RevokeUserPermission(userId, perm, null);

        public void RevokeUserPermission(string userId, string perm, object plugin)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(perm)) return;
            _granted.Remove(userId + ":" + perm);
            EnsureBound();
            try { _revokeUser?.Invoke(null, new object[] { userId, perm }); } catch { }
        }
    }
}
