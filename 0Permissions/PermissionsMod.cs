using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace PermissionsHarmony
{
    /// <summary>
    /// Oxide-style groups/permissions for Harmony-only servers.
    /// Commands: perm.grant / perm.revoke / perm.usergroup / perm.group / perm.show
    /// (aliases: grant, revoke, usergroup)
    /// </summary>
    public class PermissionsMod : IHarmonyModHooks
    {
        public static PermissionsMod Instance { get; private set; }
        public const string AppDomainApiKey = "Permissions_ApiType";
        public const string AppDomainGenerationKey = "Permissions_Generation";
        public const string AppDomainReadyCallbacksKey = "Permissions_ReadyCallbacks";
        public const string AppDomainGetUserGroupsFn = "Permissions_GetUserGroupsFn";
        public const string AppDomainUserHasGroupFn = "Permissions_UserHasGroupFn";
        public const string AppDomainGetAllGroupNamesFn = "Permissions_GetAllGroupNamesFn";
        public const string AppDomainUserGroupsCsvKey = "Permissions_UserGroupsCsv";
        public const string AppDomainAllGroupNamesCsvKey = "Permissions_AllGroupNamesCsv";
        public const string AppDomainMembershipCallbacksKey = "Permissions_MembershipChangedCallbacks";

        private PermissionService _service;
        private readonly List<ConsoleSystem.Command> _registered = new List<ConsoleSystem.Command>();

        public PermissionService Service => _service;

        static PermissionsMod()
        {
            // CreateInstance runs before PatchAll / other mods. Set identity before anyone
            // touches FileStorage.server (Minimap's EnterGame patch does this during PatchAll).
            ServerIdentityGuard.EnsureReady();
        }

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            ServerIdentityGuard.EnsureReady();
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            _service = new PermissionService(root);
            BumpGenerationAndPublishApi();
            PublishMembershipApi();
            RegisterCommands();
            Debug.Log($"[Permissions] Loaded gen={GetGeneration()}. Groups={_service.GetGroups().Count()} RegisteredPerms={_service.GetPermissions().Count()} Data=HarmonyData/Permissions/");
            InvokeReadyCallbacks();
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            UnregisterCommands();
            _service?.Shutdown();
            _service = null;
            // Keep Permissions_Generation, snapshots, and ready callbacks so consumers rebind on next load.
            // Drop Func delegates so they cannot call into this unloaded assembly.
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, null); } catch { }
            try { AppDomain.CurrentDomain.SetData(AppDomainGetUserGroupsFn, null); } catch { }
            try { AppDomain.CurrentDomain.SetData(AppDomainUserHasGroupFn, null); } catch { }
            try { AppDomain.CurrentDomain.SetData(AppDomainGetAllGroupNamesFn, null); } catch { }
            Instance = null;
        }

        // ---- Generation / ready callbacks (consumer rebind failsafe) ----

        public static int GetGeneration()
        {
            try
            {
                var data = AppDomain.CurrentDomain.GetData(AppDomainGenerationKey);
                if (data is int i) return i;
            }
            catch { }
            return 0;
        }

        public static void RegisterReadyCallback(Action callback)
        {
            if (callback == null) return;
            var list = GetOrCreateReadyCallbacks();
            lock (list)
            {
                if (!list.Contains(callback))
                    list.Add(callback);
            }
            // If Permissions is already up, run immediately so late subscribers still register.
            if (Instance?._service != null)
            {
                try { callback(); }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Permissions] Ready callback (immediate): " + ex.Message);
                }
            }
        }

        public static void UnregisterReadyCallback(Action callback)
        {
            if (callback == null) return;
            try
            {
                if (AppDomain.CurrentDomain.GetData(AppDomainReadyCallbacksKey) is List<Action> list)
                {
                    lock (list)
                        list.Remove(callback);
                }
            }
            catch { }
        }

        private static void BumpGenerationAndPublishApi()
        {
            int gen = GetGeneration() + 1;
            try { AppDomain.CurrentDomain.SetData(AppDomainGenerationKey, gen); } catch { }
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(PermissionsMod)); } catch { }
        }

        /// <summary>
        /// BCL Funcs + string snapshots so BetterChat can read groups without MethodInfo
        /// on a Cecil-renamed 0Permissions assembly.
        /// </summary>
        private static void PublishMembershipApi()
        {
            try { AppDomain.CurrentDomain.SetData(AppDomainGetUserGroupsFn, (Func<string, string[]>)GetUserGroups); } catch { }
            try { AppDomain.CurrentDomain.SetData(AppDomainUserHasGroupFn, (Func<string, string, bool>)UserHasGroup); } catch { }
            try { AppDomain.CurrentDomain.SetData(AppDomainGetAllGroupNamesFn, (Func<string[]>)GetAllGroupNames); } catch { }
            RefreshMembershipSnapshot(log: true);
        }

        public static string[] GetAllGroupNames()
        {
            var svc = Instance?._service;
            if (svc == null) return Array.Empty<string>();
            return svc.GetGroups().ToArray();
        }

        public static void RegisterMembershipChangedCallback(Action<string> callback)
        {
            if (callback == null) return;
            var list = GetOrCreateMembershipCallbacks();
            lock (list)
            {
                if (!list.Contains(callback))
                    list.Add(callback);
            }
        }

        public static void UnregisterMembershipChangedCallback(Action<string> callback)
        {
            if (callback == null) return;
            try
            {
                if (AppDomain.CurrentDomain.GetData(AppDomainMembershipCallbacksKey) is List<Action<string>> list)
                {
                    lock (list)
                        list.Remove(callback);
                }
            }
            catch { }
        }

        private static List<Action<string>> GetOrCreateMembershipCallbacks()
        {
            try
            {
                if (AppDomain.CurrentDomain.GetData(AppDomainMembershipCallbacksKey) is List<Action<string>> existing)
                    return existing;
            }
            catch { }

            var created = new List<Action<string>>();
            try { AppDomain.CurrentDomain.SetData(AppDomainMembershipCallbacksKey, created); } catch { }
            return created;
        }

        /// <summary>Rebuild the cross-mod snapshot. playerId empty = all users (group create/delete).</summary>
        public static void NotifyMembershipChanged(string playerId)
        {
            RefreshMembershipSnapshot();

            List<Action<string>> snapshot;
            try
            {
                if (!(AppDomain.CurrentDomain.GetData(AppDomainMembershipCallbacksKey) is List<Action<string>> list) || list.Count == 0)
                    return;
                lock (list)
                    snapshot = new List<Action<string>>(list);
            }
            catch
            {
                return;
            }

            string id = playerId ?? "";
            foreach (var cb in snapshot)
            {
                try { cb(id); }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Permissions] Membership callback: " + ex.Message);
                }
            }
        }

        internal static void RefreshMembershipSnapshot(bool log = false)
        {
            var svc = Instance?._service;
            var csv = svc != null
                ? svc.BuildUserGroupsCsv()
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string allNames = svc != null ? string.Join(",", svc.GetGroups()) : "";
            try { AppDomain.CurrentDomain.SetData(AppDomainUserGroupsCsvKey, csv); } catch { }
            try { AppDomain.CurrentDomain.SetData(AppDomainAllGroupNamesCsvKey, allNames); } catch { }
            if (log)
                Debug.Log($"[Permissions] Membership snapshot: {csv.Count} users, groups=[{allNames}]");
        }

        private static List<Action> GetOrCreateReadyCallbacks()
        {
            try
            {
                if (AppDomain.CurrentDomain.GetData(AppDomainReadyCallbacksKey) is List<Action> existing)
                    return existing;
            }
            catch { }

            var created = new List<Action>();
            try { AppDomain.CurrentDomain.SetData(AppDomainReadyCallbacksKey, created); } catch { }
            return created;
        }

        private static void InvokeReadyCallbacks()
        {
            List<Action> snapshot;
            try
            {
                if (!(AppDomain.CurrentDomain.GetData(AppDomainReadyCallbacksKey) is List<Action> list) || list.Count == 0)
                    return;
                lock (list)
                    snapshot = new List<Action>(list);
            }
            catch
            {
                return;
            }

            Debug.Log($"[Permissions] Invoking {snapshot.Count} ready callback(s) for consumer re-register.");
            foreach (var cb in snapshot)
            {
                try { cb(); }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Permissions] Ready callback failed: " + ex.Message);
                }
            }
        }

        // ---- Static API for other Harmony mods (Kits, etc.) ----

        public static bool UserHasPermission(string playerId, string permission) =>
            Instance?._service?.UserHasPermission(playerId, permission) == true;

        public static bool GroupHasPermission(string groupName, string permission) =>
            Instance?._service?.GroupHasPermission(groupName, permission) == true;

        public static void RegisterPermission(string permission) =>
            Instance?._service?.RegisterPermission(permission);

        public static bool PermissionExists(string permission) =>
            Instance?._service?.PermissionExists(permission) == true;

        public static bool GrantUserPermission(string playerId, string permission) =>
            Instance?._service?.GrantUserPermission(playerId, permission) == true;

        public static bool GrantGroupPermission(string groupName, string permission) =>
            Instance?._service?.GrantGroupPermission(groupName, permission) == true;

        public static bool RevokeUserPermission(string playerId, string permission) =>
            Instance?._service?.RevokeUserPermission(playerId, permission) == true;

        public static bool RevokeGroupPermission(string groupName, string permission) =>
            Instance?._service?.RevokeGroupPermission(groupName, permission) == true;

        public static bool AddUserGroup(string playerId, string groupName) =>
            Instance?._service?.AddUserGroup(playerId, groupName) == true;

        public static bool RemoveUserGroup(string playerId, string groupName) =>
            Instance?._service?.RemoveUserGroup(playerId, groupName) == true;

        public static bool UserHasGroup(string playerId, string groupName) =>
            Instance?._service?.UserHasGroup(playerId, groupName) == true;

        /// <summary>Steam IDs currently in the group (no nickname suffix).</summary>
        public static string[] GetUserIdsInGroup(string groupName)
        {
            var svc = Instance?._service;
            if (svc == null || string.IsNullOrWhiteSpace(groupName))
                return Array.Empty<string>();
            var list = new List<string>();
            foreach (var entry in svc.GetUsersInGroup(groupName))
            {
                if (string.IsNullOrEmpty(entry)) continue;
                int space = entry.IndexOf(' ');
                list.Add(space > 0 ? entry.Substring(0, space) : entry);
            }
            return list.ToArray();
        }

        public static bool CreateGroup(string groupName, string title, int rank) =>
            Instance?._service?.CreateGroup(groupName, title, rank) == true;

        public static bool GroupExists(string groupName) =>
            Instance?._service?.GroupExists(groupName) == true;

        public static string[] GetUserGroups(string playerId)
        {
            var user = Instance?._service?.GetUserData(playerId);
            if (user?.Groups == null || user.Groups.Count == 0)
                return Array.Empty<string>();
            var arr = new string[user.Groups.Count];
            user.Groups.CopyTo(arr);
            return arr;
        }

        public static int GetGroupRank(string groupName) =>
            Instance?._service?.GetGroupData(groupName)?.Rank ?? 0;

        #region Commands

        private void RegisterCommands()
        {
            // Root dispatcher: perm usergroup add <id> <group>
            // Tebex/RCON uses this space form; ServerAdmin=true is required for FromRcon.
            Register("perm", HandlePermRoot, parent: null);
            // Dotted style (like al.additems): perm.usergroup / perm.grant / ...
            Register("usergroup", HandleUserGroup, parent: "perm");
            Register("grant", HandleGrant, parent: "perm");
            Register("revoke", HandleRevoke, parent: "perm");
            Register("group", HandleGroup, parent: "perm");
            Register("show", HandleShow, parent: "perm");
            // Oxide-compatible aliases (common Tebex package templates)
            Register("usergroup", HandleUserGroup, parent: "oxide");
            Register("grant", HandleGrant, parent: "oxide");
            Register("revoke", HandleRevoke, parent: "oxide");
            Register("group", HandleGroup, parent: "oxide");
            Register("show", HandleShow, parent: "oxide");
            // Global short aliases (no parent)
            Register("grant", HandleGrant, parent: null);
            Register("revoke", HandleRevoke, parent: null);
            Register("usergroup", HandleUserGroup, parent: null);
            Register("p.show", HandleShow, parent: null);
            Debug.Log("[Permissions] Commands ready (RCON/Tebex): perm usergroup | perm.usergroup | oxide.usergroup | grant | revoke");
        }

        private void Register(string name, Action<ConsoleSystem.Arg> handler, string parent = null)
        {
            string fullName;
            string dictKey;
            string cmdName = name;
            string cmdParent = parent ?? "";

            if (name.Contains("."))
            {
                // e.g. p.show
                var parts = name.Split(new[] { '.' }, 2);
                cmdParent = parts[0];
                cmdName = parts[1];
                fullName = name;
                dictKey = name;
            }
            else if (!string.IsNullOrEmpty(parent))
            {
                fullName = parent + "." + name;
                dictKey = fullName;
            }
            else
            {
                fullName = "global." + name;
                dictKey = fullName;
            }

            var cmd = new ConsoleSystem.Command
            {
                Name = cmdName,
                Parent = cmdParent,
                FullName = fullName,
                // ServerAdmin required for Tebex/RCON (Facepunch Arg.HasPermission).
                ServerAdmin = true,
                ServerUser = false,
                Variable = false,
                AllowRunFromServer = true,
                Call = arg =>
                {
                    if (arg == null) return;
                    if (arg.Connection != null && !arg.IsAdmin)
                    {
                        arg.ReplyWith("Permission denied (admin only).");
                        return;
                    }
                    try { handler(arg); }
                    catch (Exception ex) { arg.ReplyWith("Error: " + ex.Message); }
                }
            };

            ConsoleSystem.Index.Server.Dict[dictKey] = cmd;
            if (string.IsNullOrEmpty(parent) && !name.Contains(".") && ConsoleSystem.Index.Server.GlobalDict != null)
                ConsoleSystem.Index.Server.GlobalDict[cmdName] = cmd;
            // Also index dotted form for RCON clients that look up FullName
            if (!string.IsNullOrEmpty(parent))
                ConsoleSystem.Index.Server.Dict[fullName] = cmd;

            _registered.Add(cmd);
        }

        /// <summary>perm usergroup add ... | perm grant user ... | perm show groups</summary>
        private void HandlePermRoot(ConsoleSystem.Arg arg)
        {
            var a = Args(arg);
            if (a.Length == 0)
            {
                arg.ReplyWith(
                    "Usage:\n" +
                    "  perm usergroup add|remove <steamid|name> <group>\n" +
                    "  perm grant user|group <target> <permission>\n" +
                    "  perm revoke user|group <target> <permission>\n" +
                    "  perm group add|remove|set|parent ...\n" +
                    "  perm show user|group|perm|groups|perms ...\n" +
                    "Also: perm.usergroup / perm.grant / usergroup / grant");
                return;
            }
            string sub = a[0].ToLowerInvariant();
            // Rebuild arg list without the subcommand for handlers
            var rest = new string[a.Length - 1];
            Array.Copy(a, 1, rest, 0, rest.Length);
            switch (sub)
            {
                case "usergroup":
                case "u":
                    HandleUserGroupArgs(arg, rest);
                    break;
                case "grant":
                case "g":
                    HandleGrantArgs(arg, rest);
                    break;
                case "revoke":
                case "r":
                    HandleRevokeArgs(arg, rest);
                    break;
                case "group":
                    HandleGroupArgs(arg, rest);
                    break;
                case "show":
                case "s":
                    HandleShowArgs(arg, rest);
                    break;
                default:
                    arg.ReplyWith($"Unknown subcommand '{sub}'. Try: usergroup, grant, revoke, group, show");
                    break;
            }
        }

        private void UnregisterCommands()
        {
            string[] keys =
            {
                "global.perm", "perm.usergroup", "perm.grant", "perm.revoke", "perm.group", "perm.show",
                "oxide.usergroup", "oxide.grant", "oxide.revoke", "oxide.group", "oxide.show",
                "global.grant", "global.revoke", "global.usergroup", "p.show"
            };
            foreach (var k in keys)
                ConsoleSystem.Index.Server.Dict?.Remove(k);
            ConsoleSystem.Index.Server.GlobalDict?.Remove("perm");
            ConsoleSystem.Index.Server.GlobalDict?.Remove("grant");
            ConsoleSystem.Index.Server.GlobalDict?.Remove("revoke");
            ConsoleSystem.Index.Server.GlobalDict?.Remove("usergroup");
            _registered.Clear();
        }

        private string[] Args(ConsoleSystem.Arg arg)
        {
            if (arg?.Args == null || arg.Args.Length == 0) return Array.Empty<string>();
            var a = new string[arg.Args.Length];
            for (int i = 0; i < arg.Args.Length; i++)
                a[i] = arg.GetString(i, "") ?? "";
            return a;
        }

        private void HandleGrant(ConsoleSystem.Arg arg) => HandleGrantArgs(arg, Args(arg));
        private void HandleRevoke(ConsoleSystem.Arg arg) => HandleRevokeArgs(arg, Args(arg));
        private void HandleUserGroup(ConsoleSystem.Arg arg) => HandleUserGroupArgs(arg, Args(arg));
        private void HandleGroup(ConsoleSystem.Arg arg) => HandleGroupArgs(arg, Args(arg));
        private void HandleShow(ConsoleSystem.Arg arg) => HandleShowArgs(arg, Args(arg));

        private void HandleGrantArgs(ConsoleSystem.Arg arg, string[] a)
        {
            // grant user <id> <perm>  |  grant group <group> <perm>
            if (a.Length < 3)
            {
                arg.ReplyWith("Usage: perm grant user <name|steamid> <permission>\n       perm grant group <group> <permission>");
                return;
            }
            string targetType = a[0].ToLowerInvariant();
            string target = a[1];
            string perm = a[2];
            if (targetType == "user" || targetType == "u")
            {
                var id = _service.ResolvePlayerId(target);
                if (id == null) { arg.ReplyWith($"Player '{target}' not found."); return; }
                if (ulong.TryParse(id, out var uid))
                {
                    var player = BasePlayer.FindByID(uid) ?? BasePlayer.FindSleeping(uid);
                    if (player != null) _service.TouchUser(id, player.displayName);
                }
                bool ok = _service.GrantUserPermission(id, perm);
                arg.ReplyWith(ok
                    ? $"Granted '{perm}' to user {id}"
                    : $"No change (user {id} already has '{perm}' or invalid).");
            }
            else if (targetType == "group" || targetType == "g")
            {
                if (!_service.GroupExists(target)) { arg.ReplyWith($"Group '{target}' not found."); return; }
                bool ok = _service.GrantGroupPermission(target, perm);
                arg.ReplyWith(ok
                    ? $"Granted '{perm}' to group {target}"
                    : $"No change (group already has '{perm}').");
            }
            else arg.ReplyWith("First arg must be 'user' or 'group'.");
        }

        private void HandleRevokeArgs(ConsoleSystem.Arg arg, string[] a)
        {
            if (a.Length < 3)
            {
                arg.ReplyWith("Usage: perm revoke user <name|steamid> <permission>\n       perm revoke group <group> <permission>");
                return;
            }
            string targetType = a[0].ToLowerInvariant();
            string target = a[1];
            string perm = a[2];
            if (targetType == "user" || targetType == "u")
            {
                var id = _service.ResolvePlayerId(target);
                if (id == null) { arg.ReplyWith($"Player '{target}' not found."); return; }
                bool ok = _service.RevokeUserPermission(id, perm);
                arg.ReplyWith(ok ? $"Revoked '{perm}' from user {id}" : "No change.");
            }
            else if (targetType == "group" || targetType == "g")
            {
                if (!_service.GroupExists(target)) { arg.ReplyWith($"Group '{target}' not found."); return; }
                bool ok = _service.RevokeGroupPermission(target, perm);
                arg.ReplyWith(ok ? $"Revoked '{perm}' from group {target}" : "No change.");
            }
            else arg.ReplyWith("First arg must be 'user' or 'group'.");
        }

        private void HandleUserGroupArgs(ConsoleSystem.Arg arg, string[] a)
        {
            // usergroup add|remove <user> <group>
            if (a.Length < 3)
            {
                arg.ReplyWith("Usage: perm usergroup add <name|steamid> <group>\n       perm usergroup remove <name|steamid> <group>");
                return;
            }
            string action = a[0].ToLowerInvariant();
            var id = _service.ResolvePlayerId(a[1]);
            if (id == null)
            {
                // Allow granting by raw steamid even if never seen (create user entry)
                if (ulong.TryParse(a[1], out _))
                    id = a[1];
                else
                {
                    arg.ReplyWith($"Player '{a[1]}' not found. Use full SteamID64.");
                    return;
                }
            }
            string group = a[2];
            if (ulong.TryParse(id, out var uid))
            {
                var player = BasePlayer.FindByID(uid) ?? BasePlayer.FindSleeping(uid);
                if (player != null) _service.TouchUser(id, player.displayName);
                else _service.TouchUser(id, id);
            }

            if (action == "add")
            {
                if (!_service.GroupExists(group)) { arg.ReplyWith($"Group '{group}' not found. Create with: perm group add {group}"); return; }
                bool ok = _service.AddUserGroup(id, group);
                arg.ReplyWith(ok ? $"Added {id} to group '{group}'" : "No change (already in group).");
            }
            else if (action == "remove")
            {
                bool ok = _service.RemoveUserGroup(id, group);
                arg.ReplyWith(ok ? $"Removed {id} from group '{group}'" : "No change.");
            }
            else arg.ReplyWith("Action must be 'add' or 'remove'.");
        }

        private void HandleGroupArgs(ConsoleSystem.Arg arg, string[] a)
        {
            if (a.Length < 1)
            {
                arg.ReplyWith("Usage:\n  perm group add <group> [title] [rank]\n  perm group remove <group>\n  perm group set <group> <title> [rank]\n  perm group parent <group> <parent|none>");
                return;
            }
            string action = a[0].ToLowerInvariant();
            switch (action)
            {
                case "add":
                {
                    if (a.Length < 2) { arg.ReplyWith("Usage: perm group add <group> [title] [rank]"); return; }
                    string title = a.Length >= 3 ? a[2] : a[1];
                    int rank = 0;
                    if (a.Length >= 4) int.TryParse(a[3], out rank);
                    bool ok = _service.CreateGroup(a[1], title, rank);
                    arg.ReplyWith(ok ? $"Created group '{a[1]}'" : $"Group '{a[1]}' already exists.");
                    break;
                }
                case "remove":
                {
                    if (a.Length < 2) { arg.ReplyWith("Usage: perm group remove <group>"); return; }
                    bool ok = _service.RemoveGroup(a[1]);
                    arg.ReplyWith(ok ? $"Removed group '{a[1]}'" : "Cannot remove (missing or is 'default').");
                    break;
                }
                case "set":
                {
                    if (a.Length < 3) { arg.ReplyWith("Usage: perm group set <group> <title> [rank]"); return; }
                    _service.SetGroupTitle(a[1], a[2]);
                    if (a.Length >= 4 && int.TryParse(a[3], out var rank))
                        _service.SetGroupRank(a[1], rank);
                    arg.ReplyWith($"Updated group '{a[1]}'");
                    break;
                }
                case "parent":
                {
                    if (a.Length < 3) { arg.ReplyWith("Usage: perm group parent <group> <parent|none>"); return; }
                    string parent = a[2].Equals("none", StringComparison.OrdinalIgnoreCase) ? "" : a[2];
                    bool ok = _service.SetGroupParent(a[1], parent);
                    arg.ReplyWith(ok ? $"Set parent of '{a[1]}' → '{(string.IsNullOrEmpty(parent) ? "none" : parent)}'" : "Failed (missing group or circular parent).");
                    break;
                }
                default:
                    arg.ReplyWith("Unknown action. Use add/remove/set/parent.");
                    break;
            }
        }

        private void HandleShowArgs(ConsoleSystem.Arg arg, string[] a)
        {
            if (a.Length < 1)
            {
                arg.ReplyWith("Usage: perm show user <name|id> | group <group> | perm <permission> | groups | perms");
                return;
            }
            string what = a[0].ToLowerInvariant();
            var sb = new StringBuilder();

            if (what == "groups")
            {
                foreach (var g in _service.GetGroups())
                {
                    var data = _service.GetGroupData(g);
                    sb.AppendLine($"{g}  title=\"{data?.Title}\" rank={data?.Rank} perms={data?.Perms?.Count ?? 0} parent={data?.ParentGroup}");
                }
                arg.ReplyWith(sb.Length == 0 ? "(no groups)" : sb.ToString());
                return;
            }
            if (what == "perms" || what == "permissions")
            {
                foreach (var p in _service.GetPermissions())
                    sb.AppendLine(p);
                arg.ReplyWith(sb.Length == 0 ? "(no registered permissions)" : sb.ToString());
                return;
            }
            if (a.Length < 2)
            {
                arg.ReplyWith("Usage: perm show user|group|perm <name>");
                return;
            }

            if (what == "user" || what == "u")
            {
                var id = _service.ResolvePlayerId(a[1]) ?? (ulong.TryParse(a[1], out _) ? a[1] : null);
                if (id == null) { arg.ReplyWith($"Player '{a[1]}' not found."); return; }
                _service.TouchUser(id, id);
                var user = _service.GetUserData(id) ?? new UserData();
                sb.AppendLine($"User {id} ({user.LastSeenNickname})");
                sb.AppendLine("Groups: " + (user.Groups.Count == 0 ? "(none)" : string.Join(", ", user.Groups.OrderBy(x => x))));
                sb.AppendLine("Direct perms: " + (user.Perms.Count == 0 ? "(none)" : string.Join(", ", user.Perms.OrderBy(x => x))));
                arg.ReplyWith(sb.ToString());
                return;
            }
            if (what == "group" || what == "g")
            {
                var data = _service.GetGroupData(a[1]);
                if (data == null) { arg.ReplyWith($"Group '{a[1]}' not found."); return; }
                sb.AppendLine($"Group {a[1]} title=\"{data.Title}\" rank={data.Rank} parent={data.ParentGroup}");
                sb.AppendLine("Perms: " + (data.Perms.Count == 0 ? "(none)" : string.Join(", ", data.Perms.OrderBy(x => x))));
                var members = _service.GetUsersInGroup(a[1]).ToList();
                sb.AppendLine($"Members ({members.Count}): " + (members.Count == 0 ? "(none)" : string.Join(", ", members)));
                arg.ReplyWith(sb.ToString());
                return;
            }
            if (what == "perm" || what == "permission")
            {
                string perm = a[1];
                sb.AppendLine($"Permission '{perm}'");
                sb.AppendLine("Users: " + string.Join(", ", _service.GetUsersWithPermission(perm).Take(50)));
                sb.AppendLine("Groups: " + string.Join(", ", _service.GetGroupsWithPermission(perm)));
                arg.ReplyWith(sb.ToString());
                return;
            }
            arg.ReplyWith("Unknown. Use user|group|perm|groups|perms");
        }

        #endregion
    }

    /// <summary>Auto-add connecting players to default group + refresh nickname.</summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    internal static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            var svc = PermissionsMod.Instance?.Service;
            if (svc == null || __instance == null) return;
            try
            {
                svc.TouchUser(__instance.UserIDString, __instance.displayName);
                svc.AddUserGroup(__instance.UserIDString, "default");
            }
            catch { }
        }
    }
}
