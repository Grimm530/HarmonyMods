/*
 * Reflection bridge to PermissionsHarmony.PermissionsMod + Instance.Service.
 * Adapted from CombatClasses/PermissionsBridge.cs — namespace changed to RocketGuidanceSystemHarmony,
 * log tag changed to [RocketGuidanceSystem].  Same reflection API to PermissionsHarmony.PermissionsMod.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace RocketGuidanceSystemHarmony
{
    /// <summary>Oxide-compatible user permission data (local mirror).</summary>
    public class UserData
    {
        public HashSet<string> Perms  { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Groups { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Oxide-compatible group permission data (local mirror).</summary>
    public class GroupData
    {
        public HashSet<string> Perms  { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public string ParentGroup { get; set; } = "";
        public string Title       { get; set; } = "";
        public int    Rank        { get; set; }
    }

    /// <summary>Oxide permission.* parity via Permissions Harmony mod (lazy reflection binding).</summary>
    public static class PermissionsBridge
    {
        private static Type   _permType;
        private static object _service;
        private static int    _boundGen = -1;
        private static bool   _resolveAttempted;
        private static bool   _loggedLink;

        private static MethodInfo _userHas, _register, _exists, _groupHas;
        private static MethodInfo _grantUser, _revokeUser, _grantGroup, _revokeGroup;
        private static MethodInfo _addUserGroup, _removeUserGroup, _userHasGroup;
        private static MethodInfo _createGroup, _groupExists, _getUserGroups;

        private static MethodInfo _svcGetPermissions, _svcGetGroups, _svcGetUsersInGroup;
        private static MethodInfo _svcGetUserData, _svcGetGroupData;
        private static MethodInfo _svcUserHas, _svcRegister, _svcExists, _svcGroupHas;
        private static MethodInfo _svcGrantUser, _svcRevokeUser, _svcGrantGroup, _svcRevokeGroup;
        private static MethodInfo _svcAddUserGroup, _svcRemoveUserGroup, _svcUserHasGroup;
        private static MethodInfo _svcCreateGroup, _svcGroupExists;

        public static bool IsAvailable { get { EnsureBound(); return _permType != null && _service != null; } }

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
                Debug.LogWarning("[RocketGuidanceSystem] RegisterReadyCallback: " + ex.Message);
            }

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
                Debug.LogWarning("[RocketGuidanceSystem] RegisterReadyCallback fallback: " + ex.Message);
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
            _userHas = _register = _exists = _groupHas = null;
            _grantUser = _revokeUser = _grantGroup = _revokeGroup = null;
            _addUserGroup = _removeUserGroup = _userHasGroup = null;
            _createGroup = _groupExists = _getUserGroups = null;
            _svcGetPermissions = _svcGetGroups = _svcGetUsersInGroup = null;
            _svcGetUserData = _svcGetGroupData = null;
            _svcUserHas = _svcRegister = _svcExists = _svcGroupHas = null;
            _svcGrantUser = _svcRevokeUser = _svcGrantGroup = _svcRevokeGroup = null;
            _svcAddUserGroup = _svcRemoveUserGroup = _svcUserHasGroup = null;
            _svcCreateGroup = _svcGroupExists = null;
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
                        Debug.LogWarning("[RocketGuidanceSystem] Permissions mod not loaded - permission checks will fail until 0Permissions.dll is loaded.");
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
                    Debug.Log("[RocketGuidanceSystem] Linked to Permissions Harmony mod.");
                }
                else
                    Debug.Log($"[RocketGuidanceSystem] Re-linked to Permissions Harmony mod (gen={gen}).");
            }
            catch (Exception ex)
            {
                ClearBind();
                Debug.LogWarning("[RocketGuidanceSystem] PermissionsBridge bind failed: " + ex.Message);
            }
        }

        private static void BindStatic()
        {
            const BindingFlags S = BindingFlags.Public | BindingFlags.Static;
            _userHas      = _permType.GetMethod("UserHasPermission",  S, null, new[] { typeof(string), typeof(string) }, null);
            _register     = _permType.GetMethod("RegisterPermission",  S, null, new[] { typeof(string) }, null);
            _exists       = _permType.GetMethod("PermissionExists",    S, null, new[] { typeof(string) }, null);
            _groupHas     = _permType.GetMethod("GroupHasPermission",  S, null, new[] { typeof(string), typeof(string) }, null);
            _grantUser    = _permType.GetMethod("GrantUserPermission",  S, null, new[] { typeof(string), typeof(string) }, null);
            _revokeUser   = _permType.GetMethod("RevokeUserPermission", S, null, new[] { typeof(string), typeof(string) }, null);
            _grantGroup   = _permType.GetMethod("GrantGroupPermission", S, null, new[] { typeof(string), typeof(string) }, null);
            _revokeGroup  = _permType.GetMethod("RevokeGroupPermission",S, null, new[] { typeof(string), typeof(string) }, null);
            _addUserGroup    = _permType.GetMethod("AddUserGroup",    S, null, new[] { typeof(string), typeof(string) }, null);
            _removeUserGroup = _permType.GetMethod("RemoveUserGroup", S, null, new[] { typeof(string), typeof(string) }, null);
            _userHasGroup    = _permType.GetMethod("UserHasGroup",    S, null, new[] { typeof(string), typeof(string) }, null);
            _createGroup     = _permType.GetMethod("CreateGroup",     S, null, new[] { typeof(string), typeof(string), typeof(int) }, null);
            _groupExists     = _permType.GetMethod("GroupExists",     S, null, new[] { typeof(string) }, null);
            _getUserGroups   = _permType.GetMethod("GetUserGroups",   S, null, new[] { typeof(string) }, null);
        }

        private static void BindService()
        {
            try
            {
                var inst = _permType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (inst == null) return;
                var svcProp = inst.GetType().GetProperty("Service", BindingFlags.Public | BindingFlags.Instance);
                _service = svcProp?.GetValue(inst);
                if (_service == null) return;
                var st = _service.GetType();
                const BindingFlags I = BindingFlags.Public | BindingFlags.Instance;
                _svcGetPermissions  = st.GetMethod("GetPermissions",    I);
                _svcGetGroups       = st.GetMethod("GetGroups",         I);
                _svcGetUsersInGroup = st.GetMethod("GetUsersInGroup",   I, null, new[] { typeof(string) }, null);
                _svcGetUserData     = st.GetMethod("GetUserData",       I, null, new[] { typeof(string) }, null);
                _svcGetGroupData    = st.GetMethod("GetGroupData",      I, null, new[] { typeof(string) }, null);
                _svcUserHas     = st.GetMethod("UserHasPermission",  I, null, new[] { typeof(string), typeof(string) }, null);
                _svcRegister    = st.GetMethod("RegisterPermission",  I, null, new[] { typeof(string) }, null);
                _svcExists      = st.GetMethod("PermissionExists",    I, null, new[] { typeof(string) }, null);
                _svcGroupHas    = st.GetMethod("GroupHasPermission",  I, null, new[] { typeof(string), typeof(string) }, null);
                _svcGrantUser   = st.GetMethod("GrantUserPermission",  I, null, new[] { typeof(string), typeof(string) }, null);
                _svcRevokeUser  = st.GetMethod("RevokeUserPermission", I, null, new[] { typeof(string), typeof(string) }, null);
                _svcGrantGroup  = st.GetMethod("GrantGroupPermission", I, null, new[] { typeof(string), typeof(string) }, null);
                _svcRevokeGroup = st.GetMethod("RevokeGroupPermission",I, null, new[] { typeof(string), typeof(string) }, null);
                _svcAddUserGroup    = st.GetMethod("AddUserGroup",    I, null, new[] { typeof(string), typeof(string) }, null);
                _svcRemoveUserGroup = st.GetMethod("RemoveUserGroup", I, null, new[] { typeof(string), typeof(string) }, null);
                _svcUserHasGroup    = st.GetMethod("UserHasGroup",    I, null, new[] { typeof(string), typeof(string) }, null);
                _svcCreateGroup     = st.GetMethod("CreateGroup",     I, null, new[] { typeof(string), typeof(string), typeof(int) }, null);
                _svcGroupExists     = st.GetMethod("GroupExists",     I, null, new[] { typeof(string) }, null);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RocketGuidanceSystem] Permissions Service bind: " + ex.Message);
            }
        }

        private static bool InvokeBool(MethodInfo staticMi, MethodInfo svcMi, params object[] args)
        {
            EnsureBound();
            try { if (staticMi != null && staticMi.Invoke(null, args) is bool sb) return sb; } catch { }
            try { if (_service != null && svcMi != null && svcMi.Invoke(_service, args) is bool ib) return ib; } catch { }
            return false;
        }

        private static void InvokeVoid(MethodInfo staticMi, MethodInfo svcMi, params object[] args)
        {
            EnsureBound();
            try { if (staticMi != null) { staticMi.Invoke(null, args); return; } } catch { }
            try { if (_service != null && svcMi != null) svcMi.Invoke(_service, args); } catch { }
        }

        private static string[] ToStringArray(object result)
        {
            if (result == null) return Array.Empty<string>();
            if (result is string[] arr) return arr;
            if (result is IEnumerable e)
                return e.Cast<object>().Select(o => o?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToArray();
            return Array.Empty<string>();
        }

        private static HashSet<string> ReadStringHashSet(object obj, string propName)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (obj == null) return set;
            try
            {
                var prop = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                var val = prop?.GetValue(obj);
                if (val is IEnumerable enumerable)
                    foreach (var item in enumerable) { var s = item?.ToString(); if (!string.IsNullOrEmpty(s)) set.Add(s); }
            }
            catch { }
            return set;
        }

        private static string ReadStringProp(object obj, string name)
        {
            if (obj == null) return "";
            try { return obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(obj)?.ToString() ?? ""; }
            catch { return ""; }
        }

        private static int ReadIntProp(object obj, string name)
        {
            if (obj == null) return 0;
            try { var v = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(obj); if (v is int i) return i; if (v != null && int.TryParse(v.ToString(), out var p)) return p; } catch { }
            return 0;
        }

        // ---- Public API ----

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

        public static bool GroupHasPermission(string group, string perm)
        {
            if (string.IsNullOrEmpty(group) || string.IsNullOrEmpty(perm)) return false;
            return InvokeBool(_groupHas, _svcGroupHas, group, perm);
        }

        public static void GrantUserPermission(string playerId, string perm)
        {
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(perm)) return;
            InvokeVoid(_grantUser, _svcGrantUser, playerId, perm);
        }

        public static void RevokeUserPermission(string playerId, string perm)
        {
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(perm)) return;
            InvokeVoid(_revokeUser, _svcRevokeUser, playerId, perm);
        }

        public static bool GrantGroupPermission(string group, string perm)
            => InvokeBool(_grantGroup, _svcGrantGroup, group, perm);

        public static bool RevokeGroupPermission(string group, string perm)
            => InvokeBool(_revokeGroup, _svcRevokeGroup, group, perm);

        public static void AddUserGroup(string playerId, string group)
            => InvokeVoid(_addUserGroup, _svcAddUserGroup, playerId, group);

        public static void RemoveUserGroup(string playerId, string group)
            => InvokeVoid(_removeUserGroup, _svcRemoveUserGroup, playerId, group);

        public static bool UserHasGroup(string playerId, string group)
            => InvokeBool(_userHasGroup, _svcUserHasGroup, playerId, group);

        public static bool CreateGroup(string name, string title, int rank)
            => InvokeBool(_createGroup, _svcCreateGroup, name, title ?? "", rank);

        public static bool GroupExists(string group)
            => InvokeBool(_groupExists, _svcGroupExists, group);

        public static string[] GetPermissions()
        {
            EnsureBound();
            try { if (_service != null && _svcGetPermissions != null) return ToStringArray(_svcGetPermissions.Invoke(_service, null)); } catch { }
            return Array.Empty<string>();
        }

        public static string[] GetGroups()
        {
            EnsureBound();
            try { if (_service != null && _svcGetGroups != null) return ToStringArray(_svcGetGroups.Invoke(_service, null)); } catch { }
            return Array.Empty<string>();
        }

        public static string[] GetUsersInGroup(string group)
        {
            if (string.IsNullOrEmpty(group)) return Array.Empty<string>();
            EnsureBound();
            try { if (_service != null && _svcGetUsersInGroup != null) return ToStringArray(_svcGetUsersInGroup.Invoke(_service, new object[] { group })); } catch { }
            return Array.Empty<string>();
        }

        public static string[] GetUserGroups(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return Array.Empty<string>();
            EnsureBound();
            try { if (_getUserGroups != null) return ToStringArray(_getUserGroups.Invoke(null, new object[] { playerId })); } catch { }
            var ud = GetUserData(playerId);
            if (ud?.Groups == null || ud.Groups.Count == 0) return Array.Empty<string>();
            var arr = new string[ud.Groups.Count]; ud.Groups.CopyTo(arr); return arr;
        }

        public static string[] GetGroupPermissions(string group)
        {
            var gd = GetGroupData(group);
            if (gd?.Perms == null || gd.Perms.Count == 0) return Array.Empty<string>();
            var arr = new string[gd.Perms.Count]; gd.Perms.CopyTo(arr); return arr;
        }

        public static UserData GetUserData(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return null;
            EnsureBound();
            try
            {
                if (_service != null && _svcGetUserData != null)
                {
                    var raw = _svcGetUserData.Invoke(_service, new object[] { playerId });
                    if (raw == null) return null;
                    return new UserData { Perms = ReadStringHashSet(raw, "Perms"), Groups = ReadStringHashSet(raw, "Groups") };
                }
            }
            catch { }
            return null;
        }

        public static GroupData GetGroupData(string group)
        {
            if (string.IsNullOrEmpty(group)) return null;
            EnsureBound();
            try
            {
                if (_service != null && _svcGetGroupData != null)
                {
                    var raw = _svcGetGroupData.Invoke(_service, new object[] { group });
                    if (raw == null) return null;
                    return new GroupData
                    {
                        Perms       = ReadStringHashSet(raw, "Perms"),
                        ParentGroup = ReadStringProp(raw, "ParentGroup"),
                        Title       = ReadStringProp(raw, "Title"),
                        Rank        = ReadIntProp(raw, "Rank")
                    };
                }
            }
            catch { }
            return null;
        }
    }
}
