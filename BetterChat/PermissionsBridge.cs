using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace BetterChatHarmony
{
    /// <summary>
    /// Bind to 0Permissions via AppDomain. Prefers BCL Funcs + CSV snapshots
    /// (Cecil-rename safe) over MethodInfo on PermissionsHarmony.PermissionsMod.
    /// </summary>
    internal static class PermissionsBridge
    {
        private static Type _permType;
        private static int _boundGeneration = -1;
        private static MethodInfo _userHas;
        private static MethodInfo _register;
        private static MethodInfo _groupExists;
        private static MethodInfo _createGroup;
        private static MethodInfo _addUserGroup;
        private static MethodInfo _removeUserGroup;
        private static MethodInfo _userHasGroup;
        private static MethodInfo _getUserGroups;
        private static MethodInfo _getGroupRank;
        private static MethodInfo _registerReady;
        private static MethodInfo _registerMembership;
        private static MethodInfo _unregisterMembership;

        public static string BindSource { get; private set; } = "unbound";

        public static string DescribeBind()
        {
            bool hasFn = false;
            bool hasSnap = false;
            int snapUsers = 0;
            try { hasFn = AppDomain.CurrentDomain.GetData("Permissions_GetUserGroupsFn") is Func<string, string[]>; }
            catch { }
            try
            {
                if (AppDomain.CurrentDomain.GetData("Permissions_UserGroupsCsv") is Dictionary<string, string> csv)
                {
                    hasSnap = true;
                    snapUsers = csv.Count;
                }
            }
            catch { }
            EnsureBound();
            string source = hasFn ? "func" : (hasSnap ? "snapshot" : (_permType != null ? "method" : "unbound"));
            return "fn=" + hasFn + " snapshot=" + hasSnap + "(" + snapUsers + " users) type=" + (_permType != null) + " source=" + source;
        }

        public static bool IsBound
        {
            get
            {
                EnsureBound();
                return BindSource != "unbound";
            }
        }

        public static void RegisterReadyCallback(Action callback)
        {
            if (callback == null) return;
            EnsureBound();
            try
            {
                if (_registerReady != null)
                {
                    _registerReady.Invoke(null, new object[] { callback });
                    return;
                }
            }
            catch { }

            try
            {
                var list = AppDomain.CurrentDomain.GetData("Permissions_ReadyCallbacks") as IList;
                if (list == null)
                {
                    list = new List<Action>();
                    AppDomain.CurrentDomain.SetData("Permissions_ReadyCallbacks", list);
                }
                if (!list.Contains(callback)) list.Add(callback);
            }
            catch { }

            if (AppDomain.CurrentDomain.GetData("Permissions_ApiType") is Type)
            {
                try { callback(); } catch { }
            }
        }

        public static void UnregisterReadyCallback(Action callback)
        {
            if (callback == null) return;
            try
            {
                var t = AppDomain.CurrentDomain.GetData("Permissions_ApiType") as Type;
                var mi = t?.GetMethod("UnregisterReadyCallback", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(Action) }, null);
                mi?.Invoke(null, new object[] { callback });
            }
            catch { }
        }

        public static bool UserHasPermission(string playerId, string permission)
        {
            EnsureBound();
            if (_userHas == null || string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(permission))
                return false;
            try { return _userHas.Invoke(null, new object[] { playerId, permission }) is true; }
            catch { return false; }
        }

        public static void RegisterPermission(string permission)
        {
            EnsureBound();
            if (_register == null || string.IsNullOrEmpty(permission)) return;
            try { _register.Invoke(null, new object[] { permission }); } catch { }
        }

        public static bool GroupExists(string groupName)
        {
            EnsureBound();
            if (_groupExists == null || string.IsNullOrEmpty(groupName)) return false;
            try { return _groupExists.Invoke(null, new object[] { groupName }) is true; }
            catch { return false; }
        }

        public static bool CreateGroup(string groupName, string title, int rank)
        {
            EnsureBound();
            if (_createGroup == null || string.IsNullOrEmpty(groupName)) return false;
            try { return _createGroup.Invoke(null, new object[] { groupName, title ?? "", rank }) is true; }
            catch { return false; }
        }

        public static bool AddUserGroup(string playerId, string groupName)
        {
            EnsureBound();
            if (_addUserGroup == null) return false;
            try { return _addUserGroup.Invoke(null, new object[] { playerId, groupName }) is true; }
            catch { return false; }
        }

        public static bool RemoveUserGroup(string playerId, string groupName)
        {
            EnsureBound();
            if (_removeUserGroup == null) return false;
            try { return _removeUserGroup.Invoke(null, new object[] { playerId, groupName }) is true; }
            catch { return false; }
        }

        public static void RegisterMembershipChangedCallback(Action<string> callback)
        {
            if (callback == null) return;
            EnsureBound();
            try
            {
                if (_registerMembership != null)
                {
                    _registerMembership.Invoke(null, new object[] { callback });
                    return;
                }
            }
            catch { }

            try
            {
                var list = AppDomain.CurrentDomain.GetData("Permissions_MembershipChangedCallbacks") as IList;
                if (list == null)
                {
                    list = new List<Action<string>>();
                    AppDomain.CurrentDomain.SetData("Permissions_MembershipChangedCallbacks", list);
                }
                if (!list.Contains(callback)) list.Add(callback);
            }
            catch { }
        }

        public static void UnregisterMembershipChangedCallback(Action<string> callback)
        {
            if (callback == null) return;
            try
            {
                if (_unregisterMembership != null)
                {
                    _unregisterMembership.Invoke(null, new object[] { callback });
                    return;
                }
            }
            catch { }
            try
            {
                var list = AppDomain.CurrentDomain.GetData("Permissions_MembershipChangedCallbacks") as IList;
                list?.Remove(callback);
            }
            catch { }
        }

        public static bool UserHasGroup(string playerId, string groupName)
        {
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(groupName)) return false;

            try
            {
                if (AppDomain.CurrentDomain.GetData("Permissions_UserHasGroupFn") is Func<string, string, bool> fn)
                    return fn(playerId, groupName);
            }
            catch { }

            var groups = GetUserGroups(playerId);
            for (int i = 0; i < groups.Length; i++)
            {
                if (string.Equals(groups[i], groupName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            EnsureBound();
            if (_userHasGroup == null) return false;
            try { return _userHasGroup.Invoke(null, new object[] { playerId, groupName }) is true; }
            catch { return false; }
        }

        public static string[] GetAllGroupNames()
        {
            try
            {
                if (AppDomain.CurrentDomain.GetData("Permissions_GetAllGroupNamesFn") is Func<string[]> fn)
                    return CoerceStringArray(fn());
            }
            catch { }

            try
            {
                var csv = AppDomain.CurrentDomain.GetData("Permissions_AllGroupNamesCsv") as string;
                if (!string.IsNullOrEmpty(csv))
                    return csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            }
            catch { }

            return Array.Empty<string>();
        }

        public static string[] GetUserGroups(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
                return Array.Empty<string>();

            try
            {
                if (AppDomain.CurrentDomain.GetData("Permissions_GetUserGroupsFn") is Func<string, string[]> fn)
                {
                    var fromFn = CoerceStringArray(fn(playerId));
                    if (fromFn.Length > 0)
                    {
                        BindSource = "func";
                        return fromFn;
                    }
                }
            }
            catch { }

            try
            {
                var fromSnap = ReadSnapshotGroups(playerId);
                if (fromSnap.Length > 0)
                {
                    BindSource = "snapshot";
                    return fromSnap;
                }
            }
            catch { }

            EnsureBound();
            if (_getUserGroups != null)
            {
                try
                {
                    var fromMi = CoerceStringArray(_getUserGroups.Invoke(null, new object[] { playerId }));
                    if (fromMi.Length > 0)
                    {
                        BindSource = "method";
                        return fromMi;
                    }
                }
                catch { }
            }

            return Array.Empty<string>();
        }

        private static string[] ReadSnapshotGroups(string playerId)
        {
            object raw;
            try { raw = AppDomain.CurrentDomain.GetData("Permissions_UserGroupsCsv"); }
            catch { return Array.Empty<string>(); }
            if (raw == null) return Array.Empty<string>();

            if (raw is Dictionary<string, string> csv)
            {
                if (csv.TryGetValue(playerId, out var joined) && !string.IsNullOrEmpty(joined))
                    return joined.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                return Array.Empty<string>();
            }

            if (raw is IDictionary dict)
            {
                foreach (DictionaryEntry entry in dict)
                {
                    if (entry.Key == null) continue;
                    if (!string.Equals(entry.Key.ToString(), playerId, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var joined = entry.Value?.ToString();
                    if (string.IsNullOrEmpty(joined)) return Array.Empty<string>();
                    return joined.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                }
            }

            return Array.Empty<string>();
        }

        private static string[] CoerceStringArray(object result)
        {
            if (result == null) return Array.Empty<string>();
            if (result is string[] arr) return arr;
            if (result is IEnumerable enumerable)
                return enumerable.Cast<object>().Select(o => o?.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
            return Array.Empty<string>();
        }

        public static int GetGroupRank(string groupName)
        {
            EnsureBound();
            if (_getGroupRank == null || string.IsNullOrEmpty(groupName)) return 0;
            try
            {
                var result = _getGroupRank.Invoke(null, new object[] { groupName });
                return result is int i ? i : 0;
            }
            catch { return 0; }
        }

        private static void EnsureBound()
        {
            int gen = 0;
            try
            {
                var data = AppDomain.CurrentDomain.GetData("Permissions_Generation");
                if (data is int i) gen = i;
            }
            catch { }

            if (_permType != null && _boundGeneration == gen) return;

            _permType = null;
            _userHas = _register = _groupExists = _createGroup = null;
            _addUserGroup = _removeUserGroup = _userHasGroup = _getUserGroups = _getGroupRank = _registerReady = null;
            _registerMembership = _unregisterMembership = null;
            _boundGeneration = gen;
            BindSource = "unbound";

            try { _permType = AppDomain.CurrentDomain.GetData("Permissions_ApiType") as Type; }
            catch { }

            if (_permType == null)
            {
                try
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        var t = asm.GetType("PermissionsHarmony.PermissionsMod");
                        if (t == null) continue;
                        _permType = t;
                        break;
                    }
                }
                catch { }
            }

            if (_permType == null) return;

            const BindingFlags sf = BindingFlags.Public | BindingFlags.Static;
            _userHas = _permType.GetMethod("UserHasPermission", sf, null, new[] { typeof(string), typeof(string) }, null);
            _register = _permType.GetMethod("RegisterPermission", sf, null, new[] { typeof(string) }, null);
            _groupExists = _permType.GetMethod("GroupExists", sf, null, new[] { typeof(string) }, null);
            _createGroup = _permType.GetMethod("CreateGroup", sf, null, new[] { typeof(string), typeof(string), typeof(int) }, null);
            _addUserGroup = _permType.GetMethod("AddUserGroup", sf, null, new[] { typeof(string), typeof(string) }, null);
            _removeUserGroup = _permType.GetMethod("RemoveUserGroup", sf, null, new[] { typeof(string), typeof(string) }, null);
            _userHasGroup = _permType.GetMethod("UserHasGroup", sf, null, new[] { typeof(string), typeof(string) }, null);
            _getUserGroups = _permType.GetMethod("GetUserGroups", sf, null, new[] { typeof(string) }, null);
            _getGroupRank = _permType.GetMethod("GetGroupRank", sf, null, new[] { typeof(string) }, null);
            _registerReady = _permType.GetMethod("RegisterReadyCallback", sf, null, new[] { typeof(Action) }, null);
            _registerMembership = _permType.GetMethod("RegisterMembershipChangedCallback", sf, null, new[] { typeof(Action<string>) }, null);
            _unregisterMembership = _permType.GetMethod("UnregisterMembershipChangedCallback", sf, null, new[] { typeof(Action<string>) }, null);
            BindSource = "method";
        }
    }
}
