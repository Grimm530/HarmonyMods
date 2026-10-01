using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

public static class PermissionsBridge
{
	private const string GenKey = "Permissions_Generation";

	private const string ApiTypeKey = "Permissions_ApiType";

	private const string ReadyListKey = "Permissions_ReadyCallbacks";

	private const string MembershipListKey = "Permissions_MembershipChangedCallbacks";

	private static readonly Type[] Sig1 = new Type[1] { typeof(string) };

	private static readonly Type[] Sig2 = new Type[2]
	{
		typeof(string),
		typeof(string)
	};

	private static readonly Type[] Sig3 = new Type[3]
	{
		typeof(string),
		typeof(string),
		typeof(int)
	};

	private static readonly Type[] SigAction = new Type[1] { typeof(Action) };

	private static readonly Type[] SigActionStr = new Type[1] { typeof(Action<string>) };

	private static int _boundGen = -1;

	private static bool _resolveAttempted;

	private static bool _loggedLink;

	private static string _bindSource = "unbound";

	private static Func<string, string, bool> _userHasFn;

	private static Func<string, string, bool> _groupHasFn;

	private static Func<string, bool> _existsFn;

	private static Action<string> _registerFn;

	private static Func<string, string, bool> _grantUserFn;

	private static Func<string, string, bool> _revokeUserFn;

	private static Func<string, string, bool> _grantGroupFn;

	private static Func<string, string, bool> _revokeGroupFn;

	private static Func<string, string, bool> _addUserGroupFn;

	private static Func<string, string, bool> _removeUserGroupFn;

	private static Func<string, string, int, bool> _createGroupFn;

	private static Func<string, bool> _groupExistsFn;

	private static Func<string, bool> _removeGroupFn;

	private static Func<string, string, bool> _setGroupParentFn;

	private static Func<string, string, bool> _userHasGroupFn;

	private static Func<string, string[]> _getUserGroupsFn;

	private static Func<string[]> _getGroupsFn;

	private static Func<string[]> _getPermissionsFn;

	private static Func<string, string[]> _getUsersInGroupFn;

	private static Func<string, string[]> _getGroupPermsFn;

	private static Func<string, int> _getGroupRankFn;

	private static Func<string, string> _getGroupTitleFn;

	private static Func<string, string> _getGroupParentFn;

	private static Func<string, string[]> _getUserPermsFn;

	private static Func<string, string[]> _getPermissionUsersFn;

	private static Func<string, bool> _userExistsFn;

	private static Func<string, bool> _groupDataExistsFn;

	private static Action<Action> _registerReadyFn;

	private static Action<Action> _unregisterReadyFn;

	private static Action<Action<string>> _registerMembershipFn;

	private static Action<Action<string>> _unregisterMembershipFn;

	public static string BindSource => _bindSource;

	public static bool IsAvailable
	{
		get
		{
			EnsureBound();
			return _userHasFn != null;
		}
	}

	public static bool IsBound => IsAvailable;

	public static string DescribeBind()
	{
		EnsureBound();
		bool flag = false;
		bool flag2 = false;
		int num = 0;
		try
		{
			flag = AppDomain.CurrentDomain.GetData("Permissions_GetUserGroupsFn") is Func<string, string[]>;
		}
		catch
		{
		}
		try
		{
			if (AppDomain.CurrentDomain.GetData("Permissions_UserGroupsCsv") is Dictionary<string, string> dictionary)
			{
				flag2 = true;
				num = dictionary.Count;
			}
		}
		catch
		{
		}
		return "fn=" + flag + " snapshot=" + flag2 + "(" + num + " users) source=" + _bindSource;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int ReadGeneration()
	{
		try
		{
			object data = AppDomain.CurrentDomain.GetData("Permissions_Generation");
			if (data is int)
			{
				return (int)data;
			}
		}
		catch
		{
		}
		return 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool EnsureBound()
	{
		int num = ReadGeneration();
		if (_boundGen == num && _userHasFn != null)
		{
			return true;
		}
		Rebind(num);
		return _userHasFn != null;
	}

	private static void ClearBind()
	{
		_userHasFn = null;
		_groupHasFn = null;
		_existsFn = null;
		_registerFn = null;
		_grantUserFn = null;
		_revokeUserFn = null;
		_grantGroupFn = null;
		_revokeGroupFn = null;
		_addUserGroupFn = null;
		_removeUserGroupFn = null;
		_createGroupFn = null;
		_groupExistsFn = null;
		_removeGroupFn = null;
		_setGroupParentFn = null;
		_userHasGroupFn = null;
		_getUserGroupsFn = null;
		_getGroupsFn = null;
		_getPermissionsFn = null;
		_getUsersInGroupFn = null;
		_getGroupPermsFn = null;
		_getGroupRankFn = null;
		_getGroupTitleFn = null;
		_getGroupParentFn = null;
		_getUserPermsFn = null;
		_getPermissionUsersFn = null;
		_userExistsFn = null;
		_groupDataExistsFn = null;
		_registerReadyFn = null;
		_unregisterReadyFn = null;
		_registerMembershipFn = null;
		_unregisterMembershipFn = null;
		_bindSource = "unbound";
	}

	private static void Rebind(int gen)
	{
		try
		{
			ClearBind();
			_userHasFn = GetData<Func<string, string, bool>>("Permissions_UserHasPermissionFn");
			_groupHasFn = GetData<Func<string, string, bool>>("Permissions_GroupHasPermissionFn");
			_existsFn = GetData<Func<string, bool>>("Permissions_PermissionExistsFn");
			_registerFn = GetData<Action<string>>("Permissions_RegisterPermissionFn");
			_grantUserFn = GetData<Func<string, string, bool>>("Permissions_GrantUserPermissionFn");
			_revokeUserFn = GetData<Func<string, string, bool>>("Permissions_RevokeUserPermissionFn");
			_grantGroupFn = GetData<Func<string, string, bool>>("Permissions_GrantGroupPermissionFn");
			_revokeGroupFn = GetData<Func<string, string, bool>>("Permissions_RevokeGroupPermissionFn");
			_addUserGroupFn = GetData<Func<string, string, bool>>("Permissions_AddUserGroupFn");
			_removeUserGroupFn = GetData<Func<string, string, bool>>("Permissions_RemoveUserGroupFn");
			_createGroupFn = GetData<Func<string, string, int, bool>>("Permissions_CreateGroupFn");
			_groupExistsFn = GetData<Func<string, bool>>("Permissions_GroupExistsFn");
			_removeGroupFn = GetData<Func<string, bool>>("Permissions_RemoveGroupFn");
			_setGroupParentFn = GetData<Func<string, string, bool>>("Permissions_SetGroupParentFn");
			_userHasGroupFn = GetData<Func<string, string, bool>>("Permissions_UserHasGroupFn");
			_getUserGroupsFn = GetData<Func<string, string[]>>("Permissions_GetUserGroupsFn");
			_getGroupsFn = GetData<Func<string[]>>("Permissions_GetAllGroupNamesFn");
			_getPermissionsFn = GetData<Func<string[]>>("Permissions_GetPermissionsFn");
			_getUsersInGroupFn = GetData<Func<string, string[]>>("Permissions_GetUsersInGroupFn");
			_getGroupPermsFn = GetData<Func<string, string[]>>("Permissions_GetGroupPermissionsFn");
			_getGroupRankFn = GetData<Func<string, int>>("Permissions_GetGroupRankFn");
			_getGroupTitleFn = GetData<Func<string, string>>("Permissions_GetGroupTitleFn");
			_getGroupParentFn = GetData<Func<string, string>>("Permissions_GetGroupParentFn");
			_getUserPermsFn = GetData<Func<string, string[]>>("Permissions_GetUserPermsFn");
			_getPermissionUsersFn = GetData<Func<string, string[]>>("Permissions_GetPermissionUsersFn");
			_userExistsFn = GetData<Func<string, bool>>("Permissions_UserExistsFn");
			_groupDataExistsFn = GetData<Func<string, bool>>("Permissions_GroupDataExistsFn");
			_registerReadyFn = GetData<Action<Action>>("Permissions_RegisterReadyCallbackFn");
			_unregisterReadyFn = GetData<Action<Action>>("Permissions_UnregisterReadyCallbackFn");
			_registerMembershipFn = GetData<Action<Action<string>>>("Permissions_RegisterMembershipChangedCallbackFn");
			_unregisterMembershipFn = GetData<Action<Action<string>>>("Permissions_UnregisterMembershipChangedCallbackFn");
			bool flag = _userHasFn != null;
			if (!flag)
			{
				BindCreateDelegateFallback();
			}
			if (_userHasFn == null)
			{
				_boundGen = gen;
				if (!_resolveAttempted)
				{
					_resolveAttempted = true;
					Debug.LogWarning("[PermissionsBridge] 0Permissions not loaded — permission checks will fail until 0Permissions.dll is loaded.");
				}
				return;
			}
			_resolveAttempted = false;
			_boundGen = gen;
			_bindSource = ((flag && _userHasGroupFn != null) ? "func" : "delegate");
			if (!_loggedLink)
			{
				_loggedLink = true;
				Debug.Log("[PermissionsBridge] Linked to 0Permissions (" + _bindSource + ", gen=" + gen + ").");
			}
			else
			{
				Debug.Log("[PermissionsBridge] Re-linked to 0Permissions (" + _bindSource + ", gen=" + gen + ").");
			}
		}
		catch (Exception ex)
		{
			ClearBind();
			_boundGen = gen;
			Debug.LogWarning("[PermissionsBridge] bind failed: " + ex.Message);
		}
	}

	private static void BindCreateDelegateFallback()
	{
		Type type = AppDomain.CurrentDomain.GetData("Permissions_ApiType") as Type;
		if (type == null)
		{
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
			foreach (Assembly assembly in assemblies)
			{
				try
				{
					Type type2 = assembly.GetType("PermissionsHarmony.PermissionsMod");
					if (type2 == null)
					{
						continue;
					}
					type = type2;
					break;
				}
				catch
				{
				}
			}
		}
		if (!(type == null))
		{
			if (_userHasFn == null)
			{
				_userHasFn = BindFn<Func<string, string, bool>>(type, "UserHasPermission", Sig2);
			}
			if (_groupHasFn == null)
			{
				_groupHasFn = BindFn<Func<string, string, bool>>(type, "GroupHasPermission", Sig2);
			}
			if (_existsFn == null)
			{
				_existsFn = BindFn<Func<string, bool>>(type, "PermissionExists", Sig1);
			}
			if (_registerFn == null)
			{
				_registerFn = BindFn<Action<string>>(type, "RegisterPermission", Sig1);
			}
			if (_grantUserFn == null)
			{
				_grantUserFn = BindFn<Func<string, string, bool>>(type, "GrantUserPermission", Sig2);
			}
			if (_revokeUserFn == null)
			{
				_revokeUserFn = BindFn<Func<string, string, bool>>(type, "RevokeUserPermission", Sig2);
			}
			if (_grantGroupFn == null)
			{
				_grantGroupFn = BindFn<Func<string, string, bool>>(type, "GrantGroupPermission", Sig2);
			}
			if (_revokeGroupFn == null)
			{
				_revokeGroupFn = BindFn<Func<string, string, bool>>(type, "RevokeGroupPermission", Sig2);
			}
			if (_addUserGroupFn == null)
			{
				_addUserGroupFn = BindFn<Func<string, string, bool>>(type, "AddUserGroup", Sig2);
			}
			if (_removeUserGroupFn == null)
			{
				_removeUserGroupFn = BindFn<Func<string, string, bool>>(type, "RemoveUserGroup", Sig2);
			}
			if (_createGroupFn == null)
			{
				_createGroupFn = BindFn<Func<string, string, int, bool>>(type, "CreateGroup", Sig3);
			}
			if (_groupExistsFn == null)
			{
				_groupExistsFn = BindFn<Func<string, bool>>(type, "GroupExists", Sig1);
			}
			if (_removeGroupFn == null)
			{
				_removeGroupFn = BindFn<Func<string, bool>>(type, "RemoveGroup", Sig1);
			}
			if (_setGroupParentFn == null)
			{
				_setGroupParentFn = BindFn<Func<string, string, bool>>(type, "SetGroupParent", Sig2);
			}
			if (_userHasGroupFn == null)
			{
				_userHasGroupFn = BindFn<Func<string, string, bool>>(type, "UserHasGroup", Sig2);
			}
			if (_getUserGroupsFn == null)
			{
				_getUserGroupsFn = BindFn<Func<string, string[]>>(type, "GetUserGroups", Sig1);
			}
			if (_getGroupsFn == null)
			{
				_getGroupsFn = BindFn<Func<string[]>>(type, "GetAllGroupNames", Type.EmptyTypes);
			}
			if (_getPermissionsFn == null)
			{
				_getPermissionsFn = BindFn<Func<string[]>>(type, "GetRegisteredPermissions", Type.EmptyTypes);
			}
			if (_getUsersInGroupFn == null)
			{
				_getUsersInGroupFn = BindFn<Func<string, string[]>>(type, "GetUsersInGroupDisplay", Sig1);
			}
			if (_getGroupPermsFn == null)
			{
				_getGroupPermsFn = BindFn<Func<string, string[]>>(type, "GetGroupPermissions", Sig1);
			}
			if (_getGroupRankFn == null)
			{
				_getGroupRankFn = BindFn<Func<string, int>>(type, "GetGroupRank", Sig1);
			}
			if (_getGroupTitleFn == null)
			{
				_getGroupTitleFn = BindFn<Func<string, string>>(type, "GetGroupTitle", Sig1);
			}
			if (_getGroupParentFn == null)
			{
				_getGroupParentFn = BindFn<Func<string, string>>(type, "GetGroupParent", Sig1);
			}
			if (_getUserPermsFn == null)
			{
				_getUserPermsFn = BindFn<Func<string, string[]>>(type, "GetUserPerms", Sig1);
			}
			if (_getPermissionUsersFn == null)
			{
				_getPermissionUsersFn = BindFn<Func<string, string[]>>(type, "GetPermissionUsers", Sig1);
			}
			if (_registerReadyFn == null)
			{
				_registerReadyFn = BindFn<Action<Action>>(type, "RegisterReadyCallback", SigAction);
			}
			if (_unregisterReadyFn == null)
			{
				_unregisterReadyFn = BindFn<Action<Action>>(type, "UnregisterReadyCallback", SigAction);
			}
			if (_registerMembershipFn == null)
			{
				_registerMembershipFn = BindFn<Action<Action<string>>>(type, "RegisterMembershipChangedCallback", SigActionStr);
			}
			if (_unregisterMembershipFn == null)
			{
				_unregisterMembershipFn = BindFn<Action<Action<string>>>(type, "UnregisterMembershipChangedCallback", SigActionStr);
			}
		}
	}

	private static T GetData<T>(string key) where T : class
	{
		try
		{
			return AppDomain.CurrentDomain.GetData(key) as T;
		}
		catch
		{
			return null;
		}
	}

	private static T BindFn<T>(Type type, string name, Type[] args) where T : class
	{
		if (type == null)
		{
			return null;
		}
		try
		{
			MethodInfo method = type.GetMethod(name, BindingFlags.Static | BindingFlags.Public, null, args, null);
			if (method == null)
			{
				return null;
			}
			return Delegate.CreateDelegate(typeof(T), method) as T;
		}
		catch
		{
			return null;
		}
	}

	public static void RegisterReadyCallback(Action callback)
	{
		if (callback == null)
		{
			return;
		}
		EnsureBound();
		try
		{
			if (_registerReadyFn != null)
			{
				_registerReadyFn(callback);
				return;
			}
		}
		catch
		{
		}
		try
		{
			IList list = AppDomain.CurrentDomain.GetData("Permissions_ReadyCallbacks") as IList;
			if (list == null)
			{
				list = new List<Action>();
				AppDomain.CurrentDomain.SetData("Permissions_ReadyCallbacks", list);
			}
			lock (list)
			{
				if (!list.Contains(callback))
				{
					list.Add(callback);
				}
			}
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[PermissionsBridge] RegisterReadyCallback fallback: " + ex.Message);
		}
	}

	public static void UnregisterReadyCallback(Action callback)
	{
		if (callback == null)
		{
			return;
		}
		try
		{
			EnsureBound();
			_unregisterReadyFn?.Invoke(callback);
		}
		catch
		{
		}
	}

	public static void RegisterMembershipChangedCallback(Action<string> callback)
	{
		if (callback == null)
		{
			return;
		}
		EnsureBound();
		try
		{
			if (_registerMembershipFn != null)
			{
				_registerMembershipFn(callback);
				return;
			}
		}
		catch
		{
		}
		try
		{
			IList list = AppDomain.CurrentDomain.GetData("Permissions_MembershipChangedCallbacks") as IList;
			if (list == null)
			{
				list = new List<Action<string>>();
				AppDomain.CurrentDomain.SetData("Permissions_MembershipChangedCallbacks", list);
			}
			lock (list)
			{
				if (!list.Contains(callback))
				{
					list.Add(callback);
				}
			}
		}
		catch
		{
		}
	}

	public static void UnregisterMembershipChangedCallback(Action<string> callback)
	{
		if (callback == null)
		{
			return;
		}
		try
		{
			EnsureBound();
			if (_unregisterMembershipFn != null)
			{
				_unregisterMembershipFn(callback);
				return;
			}
		}
		catch
		{
		}
		try
		{
			(AppDomain.CurrentDomain.GetData("Permissions_MembershipChangedCallbacks") as IList)?.Remove(callback);
		}
		catch
		{
		}
	}

	public static bool UserHasPermission(string playerId, string perm)
	{
		if (string.IsNullOrEmpty(playerId))
		{
			return false;
		}
		if (string.IsNullOrEmpty(perm))
		{
			return true;
		}
		if (!EnsureBound())
		{
			return false;
		}
		try
		{
			return _userHasFn(playerId, perm);
		}
		catch
		{
			return false;
		}
	}

	public static bool UserHasPermission(BasePlayer player, string perm)
	{
		if (player != null)
		{
			return UserHasPermission(player.UserIDString, perm);
		}
		return false;
	}

	public static void Initialize(IEnumerable<string> permissions = null)
	{
		EnsureBound();
		if (permissions == null)
		{
			return;
		}
		foreach (string permission in permissions)
		{
			RegisterPermission(permission);
		}
	}

	public static void Shutdown()
	{
		ClearBind();
		_boundGen = -1;
		_loggedLink = false;
		_resolveAttempted = false;
	}

	public static bool UserHasPermissionOrDefaultAllow(string userId, string perm)
	{
		if (!EnsureBound() || _userHasFn == null)
		{
			return true;
		}
		return UserHasPermission(userId, perm);
	}

	public static void RegisterPermission(string perm)
	{
		if (string.IsNullOrEmpty(perm) || !EnsureBound())
		{
			return;
		}
		try
		{
			_registerFn?.Invoke(perm);
		}
		catch
		{
		}
	}

	public static bool PermissionExists(string perm)
	{
		if (string.IsNullOrEmpty(perm))
		{
			return false;
		}
		if (!EnsureBound())
		{
			return false;
		}
		try
		{
			return _existsFn != null && _existsFn(perm);
		}
		catch
		{
			return false;
		}
	}

	public static bool GroupHasPermission(string group, string perm)
	{
		if (string.IsNullOrEmpty(group) || string.IsNullOrEmpty(perm))
		{
			return false;
		}
		if (!EnsureBound())
		{
			return false;
		}
		try
		{
			return _groupHasFn != null && _groupHasFn(group, perm);
		}
		catch
		{
			return false;
		}
	}

	public static bool GroupsHavePermission(IEnumerable<string> groups, string perm)
	{
		if (groups == null || string.IsNullOrEmpty(perm))
		{
			return false;
		}
		foreach (string group in groups)
		{
			if (GroupHasPermission(group, perm))
			{
				return true;
			}
		}
		return false;
	}

	public static bool GrantUserPermission(string playerId, string perm)
	{
		if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(perm))
		{
			return false;
		}
		if (!EnsureBound())
		{
			return false;
		}
		try
		{
			return _grantUserFn != null && _grantUserFn(playerId, perm);
		}
		catch
		{
			return false;
		}
	}

	public static bool RevokeUserPermission(string playerId, string perm)
	{
		if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(perm))
		{
			return false;
		}
		if (!EnsureBound())
		{
			return false;
		}
		try
		{
			return _revokeUserFn != null && _revokeUserFn(playerId, perm);
		}
		catch
		{
			return false;
		}
	}

	public static bool GrantGroupPermission(string group, string perm, object ownerPlugin = null)
	{
		if (string.IsNullOrEmpty(group) || string.IsNullOrEmpty(perm))
		{
			return false;
		}
		if (!EnsureBound())
		{
			return false;
		}
		try
		{
			if (_grantGroupFn != null && _grantGroupFn(group, perm))
			{
				return true;
			}
			return _groupHasFn != null && _groupHasFn(group, perm);
		}
		catch
		{
			return false;
		}
	}

	public static bool RevokeGroupPermission(string group, string perm, object ownerPlugin = null)
	{
		if (string.IsNullOrEmpty(group) || string.IsNullOrEmpty(perm))
		{
			return false;
		}
		if (!EnsureBound())
		{
			return false;
		}
		try
		{
			return _revokeGroupFn != null && _revokeGroupFn(group, perm);
		}
		catch
		{
			return false;
		}
	}

	public static bool AddUserGroup(string playerId, string group)
	{
		if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(group))
		{
			return false;
		}
		if (!EnsureBound())
		{
			return false;
		}
		try
		{
			return _addUserGroupFn != null && _addUserGroupFn(playerId, group);
		}
		catch
		{
			return false;
		}
	}

	public static bool RemoveUserGroup(string playerId, string group)
	{
		if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(group))
		{
			return false;
		}
		if (!EnsureBound())
		{
			return false;
		}
		try
		{
			return _removeUserGroupFn != null && _removeUserGroupFn(playerId, group);
		}
		catch
		{
			return false;
		}
	}

	public static bool UserHasGroup(string playerId, string group)
	{
		if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(group))
		{
			return false;
		}
		if (!EnsureBound())
		{
			return false;
		}
		try
		{
			if (_userHasGroupFn != null)
			{
				return _userHasGroupFn(playerId, group);
			}
		}
		catch
		{
		}
		string[] userGroups = GetUserGroups(playerId);
		for (int i = 0; i < userGroups.Length; i++)
		{
			if (string.Equals(userGroups[i], group, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	public static bool CreateGroup(string name, string title, int rank)
	{
		if (string.IsNullOrEmpty(name))
		{
			return false;
		}
		if (!EnsureBound())
		{
			return false;
		}
		try
		{
			return _createGroupFn != null && _createGroupFn(name, title ?? "", rank);
		}
		catch
		{
			return false;
		}
	}

	public static bool GroupExists(string group)
	{
		if (string.IsNullOrEmpty(group))
		{
			return false;
		}
		if (!EnsureBound())
		{
			return false;
		}
		try
		{
			return _groupExistsFn != null && _groupExistsFn(group);
		}
		catch
		{
			return false;
		}
	}

	public static bool RemoveGroup(string group)
	{
		if (string.IsNullOrEmpty(group))
		{
			return false;
		}
		if (!EnsureBound())
		{
			return false;
		}
		try
		{
			return _removeGroupFn != null && _removeGroupFn(group);
		}
		catch
		{
			return false;
		}
	}

	public static bool SetGroupParent(string group, string parent)
	{
		if (string.IsNullOrEmpty(group))
		{
			return false;
		}
		if (!EnsureBound())
		{
			return false;
		}
		try
		{
			return _setGroupParentFn != null && _setGroupParentFn(group, parent ?? "");
		}
		catch
		{
			return false;
		}
	}

	public static string GetGroupParent(string group)
	{
		if (string.IsNullOrEmpty(group))
		{
			return "";
		}
		if (!EnsureBound())
		{
			return "";
		}
		try
		{
			if (_getGroupParentFn != null)
			{
				return _getGroupParentFn(group) ?? "";
			}
		}
		catch
		{
		}
		return GetGroupData(group)?.ParentGroup ?? "";
	}

	public static int GetGroupRank(string groupName)
	{
		if (string.IsNullOrEmpty(groupName))
		{
			return 0;
		}
		if (!EnsureBound())
		{
			return 0;
		}
		try
		{
			return (_getGroupRankFn != null) ? _getGroupRankFn(groupName) : 0;
		}
		catch
		{
			return 0;
		}
	}

	public static string[] GetPermissions()
	{
		if (!EnsureBound())
		{
			return Array.Empty<string>();
		}
		try
		{
			return CoerceStringArray(_getPermissionsFn?.Invoke());
		}
		catch
		{
			return Array.Empty<string>();
		}
	}

	public static string[] GetGroups()
	{
		return GetAllGroupNames();
	}

	public static string[] GetAllGroupNames()
	{
		if (!EnsureBound())
		{
			try
			{
				string text = AppDomain.CurrentDomain.GetData("Permissions_AllGroupNamesCsv") as string;
				if (!string.IsNullOrEmpty(text))
				{
					return text.Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries);
				}
			}
			catch
			{
			}
			return Array.Empty<string>();
		}
		try
		{
			if (_getGroupsFn != null)
			{
				return CoerceStringArray(_getGroupsFn());
			}
		}
		catch
		{
		}
		return Array.Empty<string>();
	}

	public static string[] GetUsersInGroup(string group)
	{
		if (string.IsNullOrEmpty(group))
		{
			return Array.Empty<string>();
		}
		if (!EnsureBound())
		{
			return Array.Empty<string>();
		}
		try
		{
			return CoerceStringArray(_getUsersInGroupFn?.Invoke(group));
		}
		catch
		{
			return Array.Empty<string>();
		}
	}

	public static string[] GetUserGroups(string playerId)
	{
		if (string.IsNullOrEmpty(playerId))
		{
			return Array.Empty<string>();
		}
		if (!EnsureBound())
		{
			return Array.Empty<string>();
		}
		try
		{
			if (_getUserGroupsFn != null)
			{
				return CoerceStringArray(_getUserGroupsFn(playerId));
			}
		}
		catch
		{
		}
		UserData userData = GetUserData(playerId);
		if (userData?.Groups == null || userData.Groups.Count == 0)
		{
			return Array.Empty<string>();
		}
		string[] array = new string[userData.Groups.Count];
		userData.Groups.CopyTo(array);
		return array;
	}

	public static string[] GetGroupPermissions(string group)
	{
		if (string.IsNullOrEmpty(group))
		{
			return Array.Empty<string>();
		}
		if (!EnsureBound())
		{
			return Array.Empty<string>();
		}
		try
		{
			if (_getGroupPermsFn != null)
			{
				return CoerceStringArray(_getGroupPermsFn(group));
			}
		}
		catch
		{
		}
		GroupData groupData = GetGroupData(group);
		if (groupData?.Perms == null || groupData.Perms.Count == 0)
		{
			return Array.Empty<string>();
		}
		string[] array = new string[groupData.Perms.Count];
		groupData.Perms.CopyTo(array);
		return array;
	}

	public static string[] GetPermissionUsers(string perm)
	{
		if (string.IsNullOrEmpty(perm))
		{
			return Array.Empty<string>();
		}
		if (!EnsureBound())
		{
			return Array.Empty<string>();
		}
		try
		{
			return CoerceStringArray(_getPermissionUsersFn?.Invoke(perm));
		}
		catch
		{
			return Array.Empty<string>();
		}
	}

	public static UserData GetUserData(string playerId)
	{
		if (string.IsNullOrEmpty(playerId))
		{
			return null;
		}
		if (!EnsureBound())
		{
			return null;
		}
		try
		{
			if (_userExistsFn != null && !_userExistsFn(playerId))
			{
				return null;
			}
			string[] array = _getUserPermsFn?.Invoke(playerId);
			if (array == null && _userExistsFn == null)
			{
				return null;
			}
			string[] array2 = _getUserGroupsFn?.Invoke(playerId);
			UserData userData = new UserData();
			if (array != null)
			{
				for (int i = 0; i < array.Length; i++)
				{
					if (!string.IsNullOrEmpty(array[i]))
					{
						userData.Perms.Add(array[i]);
					}
				}
			}
			if (array2 != null)
			{
				for (int j = 0; j < array2.Length; j++)
				{
					if (!string.IsNullOrEmpty(array2[j]))
					{
						userData.Groups.Add(array2[j]);
					}
				}
			}
			return userData;
		}
		catch
		{
			return null;
		}
	}

	public static GroupData GetGroupData(string group)
	{
		if (string.IsNullOrEmpty(group))
		{
			return null;
		}
		if (!EnsureBound())
		{
			return null;
		}
		try
		{
			if (_groupDataExistsFn != null && !_groupDataExistsFn(group))
			{
				return null;
			}
			if (_groupExistsFn != null && !_groupExistsFn(group) && _groupDataExistsFn == null)
			{
				return null;
			}
			string[] arr = _getGroupPermsFn?.Invoke(group);
			return new GroupData
			{
				Perms = ToHashSet(arr),
				ParentGroup = ((_getGroupParentFn != null) ? (_getGroupParentFn(group) ?? "") : ""),
				Title = ((_getGroupTitleFn != null) ? (_getGroupTitleFn(group) ?? "") : ""),
				Rank = ((_getGroupRankFn != null) ? _getGroupRankFn(group) : 0)
			};
		}
		catch
		{
			return null;
		}
	}

	private static HashSet<string> ToHashSet(string[] arr)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (arr == null)
		{
			return hashSet;
		}
		for (int i = 0; i < arr.Length; i++)
		{
			if (!string.IsNullOrEmpty(arr[i]))
			{
				hashSet.Add(arr[i]);
			}
		}
		return hashSet;
	}

	private static string[] CoerceStringArray(object result)
	{
		if (result == null)
		{
			return Array.Empty<string>();
		}
		if (result is string[] result2)
		{
			return result2;
		}
		if (result is IEnumerable enumerable)
		{
			List<string> list = new List<string>();
			foreach (object item in enumerable)
			{
				string text = item?.ToString();
				if (!string.IsNullOrEmpty(text))
				{
					list.Add(text);
				}
			}
			if (list.Count != 0)
			{
				return list.ToArray();
			}
			return Array.Empty<string>();
		}
		return Array.Empty<string>();
	}
}

public class UserData
{
    public System.Collections.Generic.HashSet<string> Perms { get; set; } = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
    public System.Collections.Generic.HashSet<string> Groups { get; set; } = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
}

public class GroupData
{
    public System.Collections.Generic.HashSet<string> Perms { get; set; } = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
    public string ParentGroup { get; set; } = "";
    public string Title { get; set; } = "";
    public int Rank { get; set; }
}
