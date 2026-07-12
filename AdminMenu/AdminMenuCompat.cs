/*
 * Oxide-free shims for AdminMenu 2.1.13 Chaos UI under Harmony.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Oxide.Ext.Chaos.UIFramework;
using UnityEngine;

namespace AdminMenuHarmony
{
    /// <summary>Oxide Hash&lt;TKey,TValue&gt; — Dictionary subclass with default-on-miss get.</summary>
    public class Hash<TKey, TValue> : Dictionary<TKey, TValue>
    {
        public new TValue this[TKey key]
        {
            get => TryGetValue(key, out var value) ? value : default;
            set => base[key] = value;
        }
    }

    public struct VersionNumber : IComparable<VersionNumber>
    {
        public int Major;
        public int Minor;
        public int Patch;

        public VersionNumber(int major, int minor, int patch)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
        }

        public int CompareTo(VersionNumber other)
        {
            if (Major != other.Major) return Major.CompareTo(other.Major);
            if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
            return Patch.CompareTo(other.Patch);
        }

        public static bool operator >=(VersionNumber a, VersionNumber b) => a.CompareTo(b) >= 0;
        public static bool operator <=(VersionNumber a, VersionNumber b) => a.CompareTo(b) <= 0;
        public static bool operator >(VersionNumber a, VersionNumber b) => a.CompareTo(b) > 0;
        public static bool operator <(VersionNumber a, VersionNumber b) => a.CompareTo(b) < 0;
        public static bool operator ==(VersionNumber a, VersionNumber b) => a.CompareTo(b) == 0;
        public static bool operator !=(VersionNumber a, VersionNumber b) => a.CompareTo(b) != 0;

        public override bool Equals(object obj) => obj is VersionNumber other && this == other;
        public override int GetHashCode() => Major * 397 ^ Minor * 31 ^ Patch;
        public override string ToString() => $"{Major}.{Minor}.{Patch}";
    }

    #region Attributes

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class InfoAttribute : Attribute
    {
        public InfoAttribute(string title, string author, string version) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class PermissionAttribute : Attribute
    {
    }

    #endregion

    #region IPlayer

    public interface IPlayer
    {
        string Id { get; }
        string Name { get; }
        object Object { get; }
        bool IsConnected { get; }
        bool IsAdmin { get; }
        bool IsServer { get; }
        void Message(string msg);
        void Reply(string message);
        bool HasPermission(string perm);
        void Ban(string reason, TimeSpan duration = default);
    }

    public class BasePlayerWrapper : IPlayer
    {
        private readonly BasePlayer _player;
        private readonly string _offlineId;
        private readonly string _offlineName;

        public BasePlayerWrapper(BasePlayer player) => _player = player;

        public BasePlayerWrapper(string id, string name)
        {
            _player = null;
            _offlineId = id ?? "0";
            _offlineName = name ?? id ?? "";
        }

        public string Id => _player?.UserIDString ?? _offlineId ?? "0";
        public string Name => _player?.displayName ?? _offlineName ?? "";
        public object Object => _player;
        public bool IsConnected => _player != null && _player.IsConnected;
        public bool IsAdmin => _player != null && _player.IsAdmin;
        public bool IsServer => false;

        public void Reply(string message)
        {
            if (_player == null || !_player.IsConnected || _player.net?.connection == null) return;
            ConsoleNetwork.SendClientCommand(_player.net.connection, "chat.add", 0, 0, message ?? "");
        }

        public void Message(string msg) => Reply(msg);

        public bool HasPermission(string perm)
        {
            if (string.IsNullOrEmpty(Id)) return false;
            return PermissionsBridge.UserHasPermission(Id, perm);
        }

        public void Ban(string reason, TimeSpan duration = default)
        {
            reason = string.IsNullOrEmpty(reason) ? "Banned" : reason;
            if (!ulong.TryParse(Id, out var uid) || uid == 0UL) return;

            try
            {
                var safeName = (Name ?? Id).Replace("\"", "'");
                var safeReason = reason.Replace("\"", "'");
                ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(),
                    $"banid {uid} \"{safeName}\" \"{safeReason}\"");
            }
            catch
            {
                try
                {
                    ServerUsers.Set(uid, ServerUsers.UserGroup.Banned, Name ?? Id, reason, -1L);
                    ServerUsers.Save();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[AdminMenu] Ban: " + ex.Message);
                }
            }

            if (_player != null && _player.IsConnected && _player.net?.connection != null)
            {
                try { Network.Net.sv.Kick(_player.net.connection, reason); }
                catch { }
            }
        }
    }

    public static class PlayerExtensions
    {
        public static IPlayer ToIPlayer(this BasePlayer player) =>
            player == null ? null : new BasePlayerWrapper(player);

        public static bool IsSteamId(this ulong id) => id > 76561197960265728UL;

        public static bool IsSteamId(this string id) =>
            !string.IsNullOrEmpty(id) && ulong.TryParse(id, out var uid) && uid.IsSteamId();

        public static bool HasPermission(this BasePlayer player, string perm)
        {
            if (player == null || string.IsNullOrEmpty(perm)) return false;
            return PermissionsBridge.UserHasPermission(player.UserIDString, perm);
        }

        public static void AddToGroup(this string userId, string group)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(group)) return;
            PermissionsBridge.AddUserGroup(userId, group);
        }

        public static void RemoveFromGroup(this string userId, string group)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(group)) return;
            PermissionsBridge.RemoveUserGroup(userId, group);
        }

        public static string StripTags(this string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? "";
            var sb = new StringBuilder(value.Length);
            bool inTag = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '<') { inTag = true; continue; }
                if (c == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(c);
            }
            return sb.ToString();
        }
    }

    #endregion

    #region Plugin / Timer

    public class Plugin
    {
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public string Author { get; set; } = "";
        public string Description { get; set; } = "";
        public string Filename { get; set; }
        public VersionNumber Version { get; set; } = new VersionNumber(0, 0, 0);
        public bool IsLoaded { get; set; }
        public bool IsCorePlugin { get; set; }
        public double TotalHookTime { get; set; }
    }

    /// <summary>Oxide Chaos PluginInterface-style helper for cross-mod Calls.</summary>
    public class PluginHelper
    {
        private readonly string _name;
        private static readonly Dictionary<string, PluginHelper> Cache =
            new Dictionary<string, PluginHelper>(StringComparer.OrdinalIgnoreCase);

        private PluginHelper(string name) => _name = name ?? "";

        public static PluginHelper For(string name)
        {
            if (string.IsNullOrEmpty(name)) return new PluginHelper("");
            if (Cache.TryGetValue(name, out var existing)) return existing;
            var helper = new PluginHelper(name);
            Cache[name] = helper;
            return helper;
        }

        public bool IsLoaded
        {
            get
            {
                if (string.IsNullOrEmpty(_name)) return false;
                try
                {
                    var api = AppDomain.CurrentDomain.GetData(_name + "_ApiType");
                    if (api != null) return true;
                    var plugin = AppDomain.CurrentDomain.GetData(_name + "_Plugin");
                    if (plugin != null) return true;
                }
                catch { }

                try
                {
                    var host = AdminMenuHost.Instance?.Plugins;
                    if (host != null && host.Exists(_name)) return true;
                }
                catch { }

                return false;
            }
        }

        public object Call(string method, params object[] args)
        {
            if (string.IsNullOrEmpty(_name) || string.IsNullOrEmpty(method)) return null;
            try
            {
                // Prefer AppDomain plugin instance
                var pluginObj = AppDomain.CurrentDomain.GetData(_name + "_Plugin");
                if (pluginObj != null)
                {
                    var result = InvokeNamed(pluginObj, method, args);
                    if (result.found) return result.value;
                }

                var apiType = AppDomain.CurrentDomain.GetData(_name + "_ApiType") as Type;
                if (apiType != null)
                {
                    var result = InvokeStatic(apiType, method, args);
                    if (result.found) return result.value;

                    // Instance property on HarmonyMod
                    var instanceProp = apiType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    var instance = instanceProp?.GetValue(null);
                    if (instance != null)
                    {
                        result = InvokeNamed(instance, method, args);
                        if (result.found) return result.value;

                        var pluginProp = instance.GetType().GetProperty("Plugin", BindingFlags.Public | BindingFlags.Instance)
                                          ?? instance.GetType().GetProperty("_plugin", BindingFlags.NonPublic | BindingFlags.Instance);
                        // also try field
                        if (pluginProp == null)
                        {
                            var field = instance.GetType().GetField("_plugin", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                            if (field != null)
                            {
                                var p = field.GetValue(instance);
                                if (p != null)
                                {
                                    result = InvokeNamed(p, method, args);
                                    if (result.found) return result.value;
                                }
                            }
                        }
                        else
                        {
                            var p = pluginProp.GetValue(instance);
                            if (p != null)
                            {
                                result = InvokeNamed(p, method, args);
                                if (result.found) return result.value;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AdminMenu] PluginHelper.Call {_name}.{method}: {ex.Message}");
            }
            return null;
        }

        public T Call<T>(string method, params object[] args)
        {
            var result = Call(method, args);
            if (result == null) return default;
            try
            {
                if (result is T t) return t;
                return (T)Convert.ChangeType(result, typeof(T));
            }
            catch
            {
                return default;
            }
        }

        // Typed helpers matching Chaos PluginInterface usage in AdminMenu
        public string GetClanOf(string playerId) => Call<string>("GetClanOf", playerId);
        public object GetPlayTime(string playerId) => Call("GetPlayTime", playerId);
        public object GetAFKTime(string playerId) => Call("GetAFKTime", playerId);
        public object CheckPoints(string playerId) => Call("CheckPoints", playerId);
        public double Balance(ulong playerId) => Call<double>("Balance", playerId.ToString());
        public double Balance(string playerId) => Call<double>("Balance", playerId);

        private static (bool found, object value) InvokeNamed(object target, string method, object[] args)
        {
            if (target == null) return (false, null);
            var type = target.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            var callMethod = type.GetMethod("Call", flags, null, new[] { typeof(string), typeof(object[]) }, null);
            if (callMethod != null && !string.Equals(method, "Call", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var v = callMethod.IsStatic
                        ? callMethod.Invoke(null, new object[] { method, args ?? Array.Empty<object>() })
                        : callMethod.Invoke(target, new object[] { method, args ?? Array.Empty<object>() });
                    return (true, v);
                }
                catch { }
            }

            foreach (var m in type.GetMethods(flags))
            {
                if (!string.Equals(m.Name, method, StringComparison.OrdinalIgnoreCase)) continue;
                var ps = m.GetParameters();
                if ((args == null || args.Length == 0) && ps.Length == 0)
                    return (true, m.Invoke(m.IsStatic ? null : target, null));
                if (args != null && ps.Length == args.Length)
                {
                    try { return (true, m.Invoke(m.IsStatic ? null : target, args)); }
                    catch { }
                }
            }
            return (false, null);
        }

        private static (bool found, object value) InvokeStatic(Type type, string method, object[] args)
        {
            if (type == null) return (false, null);
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            var callMethod = type.GetMethod("Call", flags, null, new[] { typeof(string), typeof(object[]) }, null);
            if (callMethod != null)
            {
                try
                {
                    return (true, callMethod.Invoke(null, new object[] { method, args ?? Array.Empty<object>() }));
                }
                catch { }
            }

            foreach (var m in type.GetMethods(flags))
            {
                if (!string.Equals(m.Name, method, StringComparison.OrdinalIgnoreCase)) continue;
                var ps = m.GetParameters();
                if ((args == null || args.Length == 0) && ps.Length == 0)
                    return (true, m.Invoke(null, null));
                if (args != null && ps.Length == args.Length)
                {
                    try { return (true, m.Invoke(null, args)); }
                    catch { }
                }
            }
            return (false, null);
        }
    }

    public class Timer
    {
        public bool Destroyed { get; private set; }
        public void Destroy() => Destroyed = true;
    }

    public class TimerLib
    {
        public Timer Once(float seconds, Action callback)
        {
            var t = new Timer();
            if (callback == null) return t;
            try
            {
                ServerMgr.Instance?.Invoke(() =>
                {
                    if (t.Destroyed) return;
                    try { callback(); }
                    catch (Exception ex) { Debug.LogWarning("[AdminMenu] Timer: " + ex.Message); }
                }, Mathf.Max(0f, seconds));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AdminMenu] Timer.Once: " + ex.Message);
            }
            return t;
        }
    }

    #endregion

    #region Permission / Plugins / Covalence

    /// <summary>Oxide-like permission API; Plugin owner args ignored.</summary>
    public class PermissionLib
    {
        public bool UserHasPermission(string playerId, string perm) =>
            PermissionsBridge.UserHasPermission(playerId, perm);

        public void RegisterPermission(string perm, object owner = null) =>
            PermissionsBridge.RegisterPermission(perm);

        public bool PermissionExists(string perm, object owner = null) =>
            PermissionsBridge.PermissionExists(perm);

        public string[] GetPermissions(Plugin owner = null) => PermissionsBridge.GetPermissions();

        public string[] GetGroups() => PermissionsBridge.GetGroups();

        public string[] GetUsersInGroup(string group) => PermissionsBridge.GetUsersInGroup(group);

        public string[] GetUserGroups(string playerId) => PermissionsBridge.GetUserGroups(playerId);

        public bool UserHasGroup(string playerId, string group) =>
            PermissionsBridge.UserHasGroup(playerId, group);

        public bool AddUserGroup(string playerId, string group) =>
            PermissionsBridge.AddUserGroup(playerId, group);

        public bool RemoveUserGroup(string playerId, string group) =>
            PermissionsBridge.RemoveUserGroup(playerId, group);

        public bool CreateGroup(string name, string title, int rank) =>
            PermissionsBridge.CreateGroup(name, title, rank);

        public bool GroupExists(string group) => PermissionsBridge.GroupExists(group);

        public bool RemoveGroup(string group) => PermissionsBridge.RemoveGroup(group);

        public bool SetGroupParent(string group, string parent) =>
            PermissionsBridge.SetGroupParent(group, parent);

        public string GetGroupParent(string group) => PermissionsBridge.GetGroupParent(group);

        public string[] GetGroupPermissions(string group) => PermissionsBridge.GetGroupPermissions(group);

        public bool GrantUserPermission(string playerId, string perm, object owner = null) =>
            PermissionsBridge.GrantUserPermission(playerId, perm);

        public bool RevokeUserPermission(string playerId, string perm, object owner = null) =>
            PermissionsBridge.RevokeUserPermission(playerId, perm);

        public bool GrantGroupPermission(string group, string perm, object owner = null) =>
            PermissionsBridge.GrantGroupPermission(group, perm, owner);

        public bool RevokeGroupPermission(string group, string perm, object owner = null) =>
            PermissionsBridge.RevokeGroupPermission(group, perm, owner);

        public UserData GetUserData(string playerId) => PermissionsBridge.GetUserData(playerId);

        public GroupData GetGroupData(string group) => PermissionsBridge.GetGroupData(group);

        public bool GroupsHavePermission(IEnumerable<string> groups, string perm) =>
            PermissionsBridge.GroupsHavePermission(groups, perm);

        public bool GroupHasPermission(string group, string perm) =>
            PermissionsBridge.GroupHasPermission(group, perm);
    }

    public class PluginsLib
    {
        public bool Exists(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return GetPlugins().Any(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Title, name, StringComparison.OrdinalIgnoreCase));
        }

        public List<Plugin> GetPlugins()
        {
            var list = new List<Plugin>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (var key in EnumerateAppDomainKeys())
                {
                    if (key.EndsWith("_ApiType", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = key.Substring(0, key.Length - "_ApiType".Length);
                        if (!seen.Add(name)) continue;
                        var t = AppDomain.CurrentDomain.GetData(key) as Type;
                        list.Add(new Plugin
                        {
                            Name = name,
                            Title = name,
                            Author = "Harmony",
                            Description = "",
                            Filename = name,
                            IsLoaded = t != null,
                            Version = TryReadVersion(t)
                        });
                    }
                }
            }
            catch { }

            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch { continue; }

                    foreach (var t in types)
                    {
                        if (t == null || !t.Name.EndsWith("HarmonyMod", StringComparison.Ordinal))
                            continue;
                        var name = t.Name;
                        if (name.EndsWith("HarmonyMod", StringComparison.Ordinal))
                            name = name.Substring(0, name.Length - "HarmonyMod".Length);
                        if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;
                        list.Add(new Plugin
                        {
                            Name = name,
                            Title = name,
                            Author = "Harmony",
                            Description = "",
                            Filename = name,
                            IsLoaded = true,
                            Version = TryReadVersion(t)
                        });
                    }
                }
            }
            catch { }

            return list;
        }

        private static IEnumerable<string> EnumerateAppDomainKeys()
        {
            // AppDomain has no public key enumeration; probe known suffixes via GetData on discovered names.
            var known = new List<string>();
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var n = asm.GetName().Name ?? "";
                    if (n.EndsWith("Harmony", StringComparison.OrdinalIgnoreCase) ||
                        n.Equals("Permissions", StringComparison.OrdinalIgnoreCase) ||
                        n.Equals("Economics", StringComparison.OrdinalIgnoreCase) ||
                        n.Equals("Kits", StringComparison.OrdinalIgnoreCase) ||
                        n.Equals("Shop", StringComparison.OrdinalIgnoreCase) ||
                        n.Equals("AdminMenu", StringComparison.OrdinalIgnoreCase))
                    {
                        known.Add(n + "_ApiType");
                        known.Add(n.Replace("Harmony", "") + "_ApiType");
                    }
                }
            }
            catch { }

            known.Add("Permissions_ApiType");
            known.Add("Economics_ApiType");
            known.Add("Kits_ApiType");
            known.Add("Shop_ApiType");
            known.Add("AdminMenu_ApiType");
            return known.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static VersionNumber TryReadVersion(Type t)
        {
            if (t == null) return new VersionNumber(0, 0, 0);
            try
            {
                var maj = t.GetField("VersionMajor", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var min = t.GetField("VersionMinor", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var pat = t.GetField("VersionPatch", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (maj is int a && min is int b && pat is int c)
                    return new VersionNumber(a, b, c);
            }
            catch { }
            return new VersionNumber(0, 0, 0);
        }
    }

    public class PlayersHelper
    {
        public IPlayer FindPlayerById(string id)
        {
            if (string.IsNullOrEmpty(id) || !ulong.TryParse(id, out var uid)) return null;
            var p = BasePlayer.FindByID(uid) ?? BasePlayer.FindSleeping(uid);
            return p != null ? new BasePlayerWrapper(p) : null;
        }

        public IEnumerable<IPlayer> Find(string nameOrId)
        {
            if (string.IsNullOrEmpty(nameOrId)) yield break;

            if (ulong.TryParse(nameOrId, out var uid))
            {
                var byId = FindPlayerById(nameOrId);
                if (byId != null) yield return byId;
                yield break;
            }

            foreach (var p in BasePlayer.activePlayerList)
            {
                if (p == null) continue;
                if (p.displayName != null &&
                    p.displayName.IndexOf(nameOrId, StringComparison.OrdinalIgnoreCase) >= 0)
                    yield return new BasePlayerWrapper(p);
            }
        }

        public IEnumerable<IPlayer> All =>
            BasePlayer.activePlayerList.Where(p => p != null).Select(p => (IPlayer)new BasePlayerWrapper(p));

        public IEnumerable<IPlayer> Connected => All;
    }

    public class CovalenceLib
    {
        public PlayersHelper Players { get; } = new PlayersHelper();
    }

    #endregion

    #region Lang / Data / Images

    public class LangHelper
    {
        private readonly Dictionary<string, string> _embedded =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _override;

        public void RegisterMessages(Dictionary<string, string> messages)
        {
            if (messages == null) return;
            foreach (var kv in messages)
                _embedded[kv.Key] = kv.Value;
        }

        public void LoadOverrideFile(string path)
        {
            _override = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                _override = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path))
                            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AdminMenu] Lang override load failed: " + ex.Message);
            }
        }

        public string GetMessage(string key, string userId = null)
        {
            if (string.IsNullOrEmpty(key)) return "";
            if (_override != null && _override.TryGetValue(key, out var ov) && !string.IsNullOrEmpty(ov))
                return ov;
            if (_embedded.TryGetValue(key, out var msg))
                return msg;
            return key;
        }
    }

    public class DataFile<T> where T : class, new()
    {
        private readonly string _path;
        public T Data { get; set; }

        public DataFile(string relativeName)
        {
            var root = AdminMenuHost.Instance?.DataDirectory
                       ?? Path.Combine(AdminMenuHost.Instance?.ServerRoot ?? ".", "HarmonyData", "AdminMenu");
            if (!Directory.Exists(root))
                Directory.CreateDirectory(root);

            relativeName = (relativeName ?? "data").Replace('\\', '/').Trim('/');
            if (!relativeName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                relativeName += ".json";

            // Support nested paths like AdminMenu/recent_players → strip leading AdminMenu/
            if (relativeName.StartsWith("AdminMenu/", StringComparison.OrdinalIgnoreCase))
                relativeName = relativeName.Substring("AdminMenu/".Length);

            _path = Path.Combine(root, relativeName.Replace('/', Path.DirectorySeparatorChar));
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            Load();
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    Data = JsonConvert.DeserializeObject<T>(File.ReadAllText(_path)) ?? new T();
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AdminMenu] DataFile load: " + ex.Message);
            }
            Data = new T();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(_path, JsonConvert.SerializeObject(Data ?? new T(), Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AdminMenu] DataFile save: " + ex.Message);
            }
        }
    }

    public static class AdminMenuImages
    {
        public static string MagnifyCrc;
        public static string LogoCrc;

        public static void TryLoad() => TryLoad(AdminMenuHost.Instance?.ImagesDirectory);

        public static void TryLoad(string imagesRoot)
        {
            MagnifyCrc = null;
            LogoCrc = null;
            if (string.IsNullOrEmpty(imagesRoot) || !Directory.Exists(imagesRoot))
                return;

            // FileStorage's static ctor opens server/<identity>/sv.files.*.db.
            // Harmony OnLoaded runs before Bootstrap sets server.identity (default
            // "my_server_identity"). Touching FileStorage that early permanently
            // poisons the type (TypeInitializationException) and save load fails.
            try
            {
                var identity = ConVar.Server.identity;
                if (string.IsNullOrEmpty(identity) ||
                    string.Equals(identity, "my_server_identity", StringComparison.OrdinalIgnoreCase))
                    return;

                var ce = CommunityEntity.ServerInstance;
                if (ce == null || ce.net == null)
                    return;

                var server = FileStorage.server;
                if (server == null)
                    return;

                MagnifyCrc = StorePng(Path.Combine(imagesRoot, "magnifyingglass.png"), ce)
                             ?? StorePng(Path.Combine(imagesRoot, "adminmenu.search.png"), ce);
                LogoCrc = StorePng(Path.Combine(imagesRoot, "adminmenulogo.png"), ce)
                          ?? StorePng(Path.Combine(imagesRoot, "adminmenu.logo.png"), ce);
            }
            catch (TypeInitializationException ex)
            {
                Debug.LogWarning("[AdminMenu] Image load deferred (FileStorage not ready): " + (ex.InnerException?.Message ?? ex.Message));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AdminMenu] Image load: " + ex.Message);
            }
        }

        private static string StorePng(string path, CommunityEntity ce)
        {
            if (!File.Exists(path)) return null;
            try
            {
                var bytes = File.ReadAllBytes(path);
                if (bytes == null || bytes.Length == 0) return null;
                var crc = FileStorage.server.Store(bytes, FileStorage.Type.png, ce.net.ID);
                return crc.ToString();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AdminMenu] StorePng " + path + ": " + ex.Message);
                return null;
            }
        }
    }

    #endregion

    #region Host / Plugin base

    public class AdminMenuHost
    {
        public static AdminMenuHost Instance { get; private set; }

        public string ServerRoot { get; private set; }
        public string ConfigDirectory { get; private set; }
        public string DataDirectory { get; private set; }
        public string LangDirectory { get; private set; }
        public string ImagesDirectory { get; private set; }
        public string ConfigPath { get; private set; }

        public PermissionLib Permission { get; } = new PermissionLib();
        public TimerLib Timer { get; } = new TimerLib();
        public LangHelper Lang { get; } = new LangHelper();
        public PluginsLib Plugins { get; } = new PluginsLib();
        public CovalenceLib Covalence { get; } = new CovalenceLib();
        public Plugin PluginRef { get; set; }

        public static void Init(string serverRoot)
        {
            Instance = new AdminMenuHost();
            Instance.ServerRoot = serverRoot ?? ".";
            Instance.ConfigDirectory = Path.Combine(Instance.ServerRoot, "HarmonyConfig");
            Instance.DataDirectory = Path.Combine(Instance.ServerRoot, "HarmonyData", "AdminMenu");
            Instance.LangDirectory = Path.Combine(Instance.ServerRoot, "HarmonyLanguage");
            Instance.ImagesDirectory = Path.Combine(Instance.ServerRoot, "HarmonyImages", "AdminMenu");
            Instance.ConfigPath = Path.Combine(Instance.ConfigDirectory, "AdminMenu.json");

            Directory.CreateDirectory(Instance.ConfigDirectory);
            Directory.CreateDirectory(Instance.DataDirectory);
            Directory.CreateDirectory(Instance.LangDirectory);
            Directory.CreateDirectory(Instance.ImagesDirectory);

            Instance.PluginRef = new Plugin
            {
                Name = "AdminMenu",
                Title = "AdminMenu",
                Version = new VersionNumber(2, 1, 13),
                IsLoaded = true
            };

            var langFile = Path.Combine(Instance.LangDirectory, "AdminMenu.json");
            Instance.Lang.LoadOverrideFile(langFile);

            // Do NOT call AdminMenuImages.TryLoad here — FileStorage must not be
            // touched until after Bootstrap sets server.identity. Images load from
            // ScheduleServerInitialized / HarmonyServerInitialized instead.

            Debug.Log($"[AdminMenu] Config: {Instance.ConfigPath}");
            Debug.Log($"[AdminMenu] Data:   {Instance.DataDirectory}");
            Debug.Log($"[AdminMenu] Lang:   {langFile}");
            Debug.Log($"[AdminMenu] Images: {Instance.ImagesDirectory}");
        }

        public static void Shutdown()
        {
            Instance = null;
        }

        public void Puts(string message) => Debug.Log("[AdminMenu] " + message);
        public void PrintWarning(string message) => Debug.LogWarning("[AdminMenu] " + message);
        public void PrintError(string message) => Debug.LogError("[AdminMenu] " + message);
    }

    public abstract class AdminMenuPluginBase
    {
        public virtual string Title => "AdminMenu";
        public virtual string Name => "AdminMenu";
        public virtual VersionNumber Version { get; protected set; } = new VersionNumber(2, 1, 13);
        public bool IsLoaded { get; set; } = true;

        protected AdminMenuHost Host => AdminMenuHost.Instance;
        protected PermissionLib permission => Host?.Permission;
        protected TimerLib timer => Host?.Timer;
        protected LangHelper lang => Host?.Lang;
        protected PluginsLib plugins => Host?.Plugins;
        protected CovalenceLib covalence => Host?.Covalence;
        protected RustLib rust => RustLib.Instance;

        protected PluginHelper Clans => PluginHelper.For("Clans");
        protected PluginHelper PlaytimeTracker => PluginHelper.For("PlaytimeTracker");
        protected PluginHelper ServerRewards => PluginHelper.For("ServerRewards");
        protected PluginHelper Economics => PluginHelper.For("Economics");

        protected string GetString(string key, BasePlayer player) =>
            lang?.GetMessage(key, player?.UserIDString) ?? key ?? "";

        protected string GetString(string key, string playerId) =>
            lang?.GetMessage(key, playerId) ?? key ?? "";

        protected string FormatString(string key, BasePlayer player, params object[] args)
        {
            var msg = GetString(key, player);
            if (args == null || args.Length == 0) return msg;
            try { return string.Format(msg, args); }
            catch { return msg; }
        }

        protected static string FormatTime(double seconds)
        {
            var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
            if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
            return $"{ts.Seconds}s";
        }

        protected void LocalizedMessage(BasePlayer player, string key, params object[] args)
        {
            if (player == null) return;
            var msg = GetString(key, player);
            if (args != null && args.Length > 0)
            {
                try { msg = string.Format(msg, args); }
                catch { }
            }
            player.ChatMessage(msg);
        }

        protected T LoadConfigObject<T>() where T : class, new()
        {
            var path = Host?.ConfigPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return new T();
            try
            {
                return JsonConvert.DeserializeObject<T>(File.ReadAllText(path)) ?? new T();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AdminMenu] LoadConfig: " + ex.Message);
                return new T();
            }
        }

        protected void SaveConfigObject(object config)
        {
            var path = Host?.ConfigPath;
            if (string.IsNullOrEmpty(path) || config == null) return;
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonConvert.SerializeObject(config, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AdminMenu] SaveConfig: " + ex.Message);
            }
        }

        protected virtual void LoadConfig() { }
        protected virtual void SaveConfig() { }

        protected virtual void RegisterMessages() { }

        public abstract void HarmonyInit();
        public abstract void HarmonyServerInitialized();
        public abstract void HarmonyUnload();
    }

    public class RustLib
    {
        public static RustLib Instance { get; } = new RustLib();

        public void RunServerCommand(string command, params object[] args)
        {
            if (string.IsNullOrEmpty(command)) return;
            try
            {
                if (args != null && args.Length > 0)
                    ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), command, args);
                else
                    ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), command);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AdminMenu] RunServerCommand: " + ex.Message);
            }
        }

        public void RunClientCommand(BasePlayer player, string command, params object[] args)
        {
            if (player == null || player.net?.connection == null || string.IsNullOrEmpty(command)) return;
            try
            {
                if (args != null && args.Length > 0)
                    ConsoleNetwork.SendClientCommand(player.net.connection, command, args);
                else
                    ConsoleNetwork.SendClientCommand(player.net.connection, command);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AdminMenu] RunClientCommand: " + ex.Message);
            }
        }
    }

    /// <summary>Oxide Interface stubs — plugin load/unload is a no-op under Harmony.</summary>
    public static class Interface
    {
        public static OxideCompat Oxide { get; } = new OxideCompat();
    }

    public class OxideCompat
    {
        public PluginManager RootPluginManager { get; } = new PluginManager();
        public string PluginDirectory =>
            AdminMenuHost.Instance != null
                ? Path.Combine(AdminMenuHost.Instance.ServerRoot, "HarmonyMods")
                : "HarmonyMods";

        public void LoadPlugin(string name) =>
            Debug.LogWarning($"[AdminMenu] LoadPlugin('{name}') is not supported under Harmony (use harmony.load).");

        public void UnloadPlugin(string name) =>
            Debug.LogWarning($"[AdminMenu] UnloadPlugin('{name}') is not supported under Harmony (use harmony.unload).");

        public void ReloadPlugin(string name) =>
            Debug.LogWarning($"[AdminMenu] ReloadPlugin('{name}') is not supported under Harmony (use harmony.reload).");

        public IEnumerable<PluginLoader> GetPluginLoaders() => Enumerable.Empty<PluginLoader>();
    }

    public class PluginManager
    {
        public IEnumerable<Plugin> GetPlugins()
        {
            var host = AdminMenuHost.Instance?.Plugins;
            return host?.GetPlugins() ?? Enumerable.Empty<Plugin>();
        }
    }

    public class PluginLoader
    {
        public Dictionary<string, string> PluginErrors { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<string> ScanDirectory(string directory) => Enumerable.Empty<string>();
    }

    #endregion
}

namespace Oxide.Ext.Chaos
{
    /// <summary>Stub for [Chaos.Permission] field markers used by AdminMenu.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class PermissionAttribute : Attribute
    {
    }
}

namespace Oxide.Ext.Chaos.Discord
{
    public class DiscordColor
    {
        public int Value { get; }
        public DiscordColor(int value) => Value = value;
        public static DiscordColor Blurple { get; } = new DiscordColor(5793266);
    }

    public class DiscordEmbed : IDisposable
    {
        public int? Color { get; private set; }
        public string AuthorName { get; private set; }
        public string AuthorUrl { get; private set; }
        public string Description { get; private set; }

        public static DiscordEmbed Create() => new DiscordEmbed();

        public DiscordEmbed WithColor(DiscordColor color)
        {
            Color = color?.Value;
            return this;
        }

        public DiscordEmbed WithAuthor(string name, string url = null)
        {
            AuthorName = name;
            AuthorUrl = url;
            return this;
        }

        public DiscordEmbed WithDescription(string description)
        {
            Description = description;
            return this;
        }

        public void Dispose() { }
    }

    public class DiscordMessage : IDisposable
    {
        public string Username { get; private set; }
        public string AvatarUrl { get; private set; }
        public DiscordEmbed Embed { get; private set; }

        public static DiscordMessage Create() => new DiscordMessage();

        public DiscordMessage WithUsername(string username)
        {
            Username = username;
            return this;
        }

        public DiscordMessage WithAvatarUrl(string avatarUrl)
        {
            AvatarUrl = avatarUrl;
            return this;
        }

        public DiscordMessage WithEmbed(DiscordEmbed embed)
        {
            Embed = embed;
            return this;
        }

        public void Dispose()
        {
            Embed?.Dispose();
        }
    }

    public class DiscordWebhook
    {
        private static readonly HttpClient Http = new HttpClient();
        private readonly string _url;

        public DiscordWebhook(object plugin, string url)
        {
            _url = url;
        }

        public void SendAsync(DiscordMessage message)
        {
            if (string.IsNullOrEmpty(_url) || message == null) return;
            var payload = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(message.Username))
                payload["username"] = message.Username;
            if (!string.IsNullOrEmpty(message.AvatarUrl))
                payload["avatar_url"] = message.AvatarUrl;
            if (message.Embed != null)
            {
                var embed = new Dictionary<string, object>();
                if (message.Embed.Color.HasValue)
                    embed["color"] = message.Embed.Color.Value;
                if (!string.IsNullOrEmpty(message.Embed.Description))
                    embed["description"] = message.Embed.Description;
                if (!string.IsNullOrEmpty(message.Embed.AuthorName))
                {
                    var author = new Dictionary<string, object> { ["name"] = message.Embed.AuthorName };
                    if (!string.IsNullOrEmpty(message.Embed.AuthorUrl))
                        author["url"] = message.Embed.AuthorUrl;
                    embed["author"] = author;
                }
                payload["embeds"] = new[] { embed };
            }

            var json = JsonConvert.SerializeObject(payload);
            Task.Run(async () =>
            {
                try
                {
                    using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                    using (var resp = await Http.PostAsync(_url, content).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode)
                        {
                            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                            Debug.LogWarning($"[AdminMenu] Discord webhook {(int)resp.StatusCode}: {body}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[AdminMenu] Discord webhook: " + ex.Message);
                }
            });
        }
    }
}
