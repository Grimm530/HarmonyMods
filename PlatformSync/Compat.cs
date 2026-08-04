using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace PlatformSync
{
    public enum RequestMethod
    {
        GET,
        POST,
        PUT,
        DELETE,
        PATCH
    }

    /// <summary>Minimal Oxide-style shims used by the PlatformSync port.</summary>
    public static class Compat
    {
        public static readonly string ServerRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        public static string DataDirectory => Path.Combine(ServerRoot, "HarmonyData");
        public static string LinksDataPath => Path.Combine(DataDirectory, "PlatformSync", "links.json");
        public static string LinksLogLegacyPath => Path.Combine(DataDirectory, "PlatformSync", "links.log");
        public static string OxideLinksDataPath => Path.Combine(ServerRoot, "oxide", "data", "PlatformSync", "links.json");
        public static string OxideLinksLogLegacyPath => Path.Combine(ServerRoot, "oxide", "data", "PlatformSync", "links.log");

        public static readonly TimerHelper Timer = new TimerHelper();
        public static readonly WebRequestHelper Webrequest = new WebRequestHelper();
        public static readonly PermissionHelper Permission = new PermissionHelper();
        public static readonly LangHelper Lang = new LangHelper();
        public static readonly PlayersHelper Players = new PlayersHelper();

        public static void EnsureDataFolders()
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(ServerRoot, "HarmonyConfig"));
                Directory.CreateDirectory(Path.Combine(DataDirectory, "PlatformSync"));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlatformSync] EnsureDataFolders: " + ex.Message);
            }
        }

        public static void NextTick(Action action)
        {
            if (action == null) return;
            BootstrapRunner.Start(NextTickCo(action));
        }

        private static IEnumerator NextTickCo(Action action)
        {
            yield return null;
            try { action(); }
            catch (Exception ex) { Debug.LogWarning("[PlatformSync] NextTick: " + ex.Message); }
        }

        public static void Puts(string message) => Debug.Log("[PlatformSync] " + message);

        /// <summary>Resolve player from console arg (avoids non-public Arg.Player extension).</summary>
        public static BasePlayer GetPlayer(ConsoleSystem.Arg arg)
        {
            if (arg?.Connection == null) return null;
            var p = arg.Connection.player as BasePlayer;
            if (p != null) return p;
            return BasePlayer.FindByID(arg.Connection.userid);
        }

        #region Timer

        public sealed class TimerHelper
        {
            public void Once(float delay, Action action)
            {
                if (action == null) return;
                BootstrapRunner.Start(OnceCo(delay, action));
            }

            private static IEnumerator OnceCo(float delay, Action action)
            {
                if (delay > 0f) yield return new WaitForSeconds(delay);
                try { action(); }
                catch (Exception ex) { Debug.LogWarning("[PlatformSync] timer.Once: " + ex.Message); }
            }
        }

        #endregion

        #region WebRequest

        public sealed class WebRequestHelper
        {
            private static readonly HttpClient Http = new HttpClient();

            public void Enqueue(string url, string body, Action<int, string> callback, object owner,
                RequestMethod method = RequestMethod.GET, Dictionary<string, string> headers = null, float timeout = 30f)
            {
                if (string.IsNullOrEmpty(url)) return;
                Task.Run(async () =>
                {
                    int code = 0;
                    string response = "";
                    try
                    {
                        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout > 0f ? timeout : 30f)))
                        using (var req = new HttpRequestMessage(
                                   method == RequestMethod.POST ? HttpMethod.Post :
                                   method == RequestMethod.PUT ? HttpMethod.Put :
                                   method == RequestMethod.DELETE ? HttpMethod.Delete :
                                   HttpMethod.Get, url))
                        {
                            if (!string.IsNullOrEmpty(body))
                                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                            if (headers != null)
                            {
                                foreach (var kv in headers)
                                {
                                    if (kv.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) && req.Content != null)
                                        req.Content.Headers.ContentType =
                                            new System.Net.Http.Headers.MediaTypeHeaderValue(kv.Value.Split(';')[0].Trim());
                                    else
                                        req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                                }
                            }
                            using (var resp = await Http.SendAsync(req, cts.Token).ConfigureAwait(false))
                            {
                                code = (int)resp.StatusCode;
                                response = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        code = 0;
                        response = ex.Message;
                    }
                    NextTick(() =>
                    {
                        try { callback?.Invoke(code, response); }
                        catch (Exception ex) { Debug.LogWarning("[PlatformSync] webrequest callback: " + ex.Message); }
                    });
                });
            }
        }

        #endregion

        #region Permission (0Permissions bridge + local fallback)

        /// <summary>
        /// Group add/remove for Discord link/nitro. Prefers 0Permissions (Permissions_ApiType)
        /// with generation rebind; falls back to HarmonyData/PlatformSync/groups.json only if
        /// Permissions is not loaded.
        /// </summary>
        public sealed class PermissionHelper
        {
            private Type _permType;
            private MethodInfo _userHasGroup;
            private MethodInfo _addUserGroup;
            private MethodInfo _removeUserGroup;
            private MethodInfo _groupExists;
            private MethodInfo _createGroup;
            private MethodInfo _registerReady;
            private int _boundGen = -1;
            private bool _resolveAttempted;
            private bool _loggedLink;
            private bool _localFallbackLogged;
            private Action _readyCallback;
            private bool _localLoaded;
            private readonly Dictionary<string, HashSet<string>> _localGroups =
                new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            private string LocalGroupsPath => Path.Combine(DataDirectory, "PlatformSync", "groups.json");

            public bool IsPermissionsBound
            {
                get
                {
                    EnsureBound();
                    return _permType != null && _addUserGroup != null;
                }
            }

            /// <summary>Call from plugin Init so we link early and rebind on 0Permissions reload.</summary>
            public void EnsureLinked()
            {
                EnsureBound();
                EnsureReadyCallback();
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

            private void ClearBind()
            {
                _permType = null;
                _userHasGroup = _addUserGroup = _removeUserGroup = _groupExists = _createGroup = _registerReady = null;
            }

            private void EnsureBound()
            {
                int gen = ReadGeneration();
                object live = ReadLiveInstance(_permType);
                if (_permType != null && _boundGen == gen && live != null && _addUserGroup != null)
                    return;

                try
                {
                    ClearBind();
                    _permType = ResolveLivePermType();
                    live = ReadLiveInstance(_permType);
                    if (_permType == null || live == null)
                    {
                        if (!_resolveAttempted)
                        {
                            _resolveAttempted = true;
                            Debug.LogWarning("[PlatformSync] 0Permissions not loaded yet — Discord groups will use local groups.json until Permissions is ready.");
                        }
                        EnsureLocalLoaded();
                        return;
                    }

                    _resolveAttempted = false;
                    const BindingFlags sf = BindingFlags.Public | BindingFlags.Static;
                    _userHasGroup = _permType.GetMethod("UserHasGroup", sf, null, new[] { typeof(string), typeof(string) }, null);
                    _addUserGroup = _permType.GetMethod("AddUserGroup", sf, null, new[] { typeof(string), typeof(string) }, null);
                    _removeUserGroup = _permType.GetMethod("RemoveUserGroup", sf, null, new[] { typeof(string), typeof(string) }, null);
                    _groupExists = _permType.GetMethod("GroupExists", sf, null, new[] { typeof(string) }, null);
                    _createGroup = _permType.GetMethod("CreateGroup", sf, null, new[] { typeof(string), typeof(string), typeof(int) }, null);
                    _registerReady = _permType.GetMethod("RegisterReadyCallback", sf, null, new[] { typeof(Action) }, null);
                    _boundGen = gen;

                    if (!_loggedLink)
                    {
                        _loggedLink = true;
                        Puts("Linked to Permissions Harmony mod for Discord/nitro groups.");
                    }
                    else
                        Puts("Re-linked to Permissions Harmony mod (gen=" + gen + ").");
                }
                catch (Exception ex)
                {
                    ClearBind();
                    Debug.LogWarning("[PlatformSync] Permissions bind failed: " + ex.Message);
                    EnsureLocalLoaded();
                }
            }

            private void EnsureReadyCallback()
            {
                if (_readyCallback != null) return;
                _readyCallback = () =>
                {
                    EnsureBound();
                };
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
                    var list = AppDomain.CurrentDomain.GetData("Permissions_ReadyCallbacks") as IList;
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

            private void EnsureLocalLoaded()
            {
                if (_localLoaded) return;
                _localLoaded = true;
                LoadLocalGroups();
                if (!_localFallbackLogged)
                {
                    _localFallbackLogged = true;
                    Puts("Using HarmonyData/PlatformSync/groups.json fallback until 0Permissions is available.");
                }
            }

            private void LoadLocalGroups()
            {
                try
                {
                    if (!File.Exists(LocalGroupsPath)) return;
                    var data = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(File.ReadAllText(LocalGroupsPath));
                    if (data == null) return;
                    foreach (var kv in data)
                    {
                        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        if (kv.Value != null)
                            foreach (var g in kv.Value)
                                if (!string.IsNullOrWhiteSpace(g)) set.Add(g);
                        _localGroups[kv.Key] = set;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[PlatformSync] LoadLocalGroups: " + ex.Message);
                }
            }

            private void SaveLocalGroups()
            {
                try
                {
                    var dir = Path.GetDirectoryName(LocalGroupsPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    var data = new Dictionary<string, List<string>>();
                    foreach (var kv in _localGroups)
                        data[kv.Key] = new List<string>(kv.Value);
                    File.WriteAllText(LocalGroupsPath, JsonConvert.SerializeObject(data, Formatting.Indented));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[PlatformSync] SaveLocalGroups: " + ex.Message);
                }
            }

            private bool InvokeBool(MethodInfo mi, params object[] args)
            {
                if (mi == null) return false;
                try
                {
                    var result = mi.Invoke(null, args);
                    return result is bool b && b;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[PlatformSync] Permissions invoke " + mi.Name + ": " + ex.Message);
                    return false;
                }
            }

            private void MirrorLocalAdd(string userId, string groupName)
            {
                EnsureLocalLoaded();
                if (!_localGroups.TryGetValue(userId, out var set))
                    _localGroups[userId] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (set.Add(groupName))
                    SaveLocalGroups();
            }

            private void MirrorLocalRemove(string userId, string groupName)
            {
                if (!_localLoaded && !File.Exists(LocalGroupsPath)) return;
                EnsureLocalLoaded();
                if (_localGroups.TryGetValue(userId, out var set) && set.Remove(groupName))
                    SaveLocalGroups();
            }

            public bool GroupExists(string groupName)
            {
                EnsureBound();
                if (string.IsNullOrWhiteSpace(groupName)) return false;
                if (_groupExists != null)
                    return InvokeBool(_groupExists, groupName);
                return true;
            }

            public void CreateGroup(string groupName, string title, int rank)
            {
                EnsureBound();
                if (string.IsNullOrWhiteSpace(groupName)) return;
                if (_createGroup != null)
                {
                    try { _createGroup.Invoke(null, new object[] { groupName, title ?? groupName, rank }); }
                    catch (Exception ex) { Debug.LogWarning("[PlatformSync] CreateGroup: " + ex.Message); }
                }
            }

            public bool UserHasGroup(string userId, string groupName)
            {
                EnsureBound();
                if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(groupName)) return false;
                if (_userHasGroup != null)
                    return InvokeBool(_userHasGroup, userId, groupName);
                EnsureLocalLoaded();
                return _localGroups.TryGetValue(userId, out var set) && set.Contains(groupName);
            }

            public void AddUserGroup(string userId, string groupName)
            {
                EnsureBound();
                EnsureReadyCallback();
                if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(groupName)) return;
                if (_addUserGroup != null)
                {
                    InvokeBool(_addUserGroup, userId, groupName);
                    MirrorLocalAdd(userId, groupName);
                    return;
                }
                EnsureLocalLoaded();
                if (!_localGroups.TryGetValue(userId, out var set))
                    _localGroups[userId] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (set.Add(groupName))
                    SaveLocalGroups();
            }

            public void RemoveUserGroup(string userId, string groupName)
            {
                EnsureBound();
                EnsureReadyCallback();
                if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(groupName)) return;
                if (_removeUserGroup != null)
                {
                    InvokeBool(_removeUserGroup, userId, groupName);
                    MirrorLocalRemove(userId, groupName);
                    return;
                }
                EnsureLocalLoaded();
                if (_localGroups.TryGetValue(userId, out var set) && set.Remove(groupName))
                    SaveLocalGroups();
            }
        }

        #endregion

        #region Lang / Players / Rustcord

        public sealed class LangHelper
        {
            private readonly Dictionary<string, string> _messages =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public void RegisterMessages(Dictionary<string, string> messages, object plugin)
            {
                if (messages == null) return;
                foreach (var kv in messages)
                    _messages[kv.Key] = kv.Value;
            }

            public string GetMessage(string key, object plugin, string id)
            {
                if (!string.IsNullOrEmpty(key) && _messages.TryGetValue(key, out var msg))
                    return msg;
                return key ?? "";
            }
        }

        public sealed class PlayersHelper
        {
            public PlayerWrapper FindPlayerById(string id)
            {
                if (string.IsNullOrEmpty(id) || !ulong.TryParse(id, out var uid)) return null;
                var p = BasePlayer.FindByID(uid) ?? BasePlayer.FindSleeping(uid);
                return p != null ? new PlayerWrapper(p) : null;
            }
        }

        public sealed class PlayerWrapper
        {
            private readonly BasePlayer _player;
            public PlayerWrapper(BasePlayer player) { _player = player; }
            public string Name => _player?.displayName ?? "";
            public string Id => _player?.UserIDString ?? "";
            public bool BelongsToGroup(string group) => Permission.UserHasGroup(Id, group);
            public void AddToGroup(string group) => Permission.AddUserGroup(Id, group);
            public void RemoveFromGroup(string group) => Permission.RemoveUserGroup(Id, group);
        }

        /// <summary>Resolves Oxide Rustcord plugin or Harmony RustcordMod for Discord role checks.</summary>
        public sealed class PluginRef
        {
            private readonly string _name;
            private object _plugin;
            private MethodInfo _call;
            private bool _tried;

            public PluginRef(string name) { _name = name; }
            public static PluginRef Find(string name) => new PluginRef(name);

            public bool IsLoaded
            {
                get
                {
                    Resolve();
                    return _plugin != null;
                }
            }

            public object Call(string method, params object[] args)
            {
                Resolve();
                if (_plugin == null) return null;
                try
                {
                    // Harmony RustcordMod: public static methods DiscordUserHasRole / GetDiscordUserRoleNames if present
                    if (_plugin is Type type)
                    {
                        var m = type.GetMethod(method, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
                        if (m == null) return null;
                        return m.Invoke(m.IsStatic ? null : null, args ?? Array.Empty<object>());
                    }

                    if (_call == null)
                        _call = _plugin.GetType().GetMethod("Call", new[] { typeof(string), typeof(object[]) });
                    if (_call != null)
                        return _call.Invoke(_plugin, new object[] { method, args ?? Array.Empty<object>() });

                    var direct = _plugin.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                    return direct?.Invoke(_plugin, args ?? Array.Empty<object>());
                }
                catch
                {
                    return null;
                }
            }

            private void Resolve()
            {
                if (_tried) return;
                _tried = true;
                try
                {
                    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Type iface = asm.GetType("Oxide.Core.Interface");
                        if (iface == null) continue;
                        object oxide = iface.GetProperty("Oxide", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                        if (oxide == null) continue;
                        object rpm = oxide.GetType().GetProperty("RootPluginManager")?.GetValue(oxide);
                        if (rpm == null) continue;
                        MethodInfo get = rpm.GetType().GetMethod("GetPlugin", new[] { typeof(string) });
                        _plugin = get?.Invoke(rpm, new object[] { _name });
                        if (_plugin != null) return;
                    }
                }
                catch { }

                // Harmony RustcordMod
                try
                {
                    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Type t = asm.GetType("Rustcord.RustcordMod") ?? asm.GetType("RustcordMod");
                        if (t == null) continue;
                        var inst = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                        if (inst != null) { _plugin = inst; return; }
                        _plugin = t; // type for static API
                        return;
                    }
                }
                catch { }
            }
        }

        #endregion

        #region Commands

        private static readonly List<ConsoleSystem.Command> Commands = new List<ConsoleSystem.Command>();

        public static void RegisterConsoleCommand(string name, Action<ConsoleSystem.Arg> handler, bool adminOnly = true)
        {
            try
            {
                var cmd = new ConsoleSystem.Command
                {
                    Name = name,
                    FullName = "global." + name,
                    Variable = false,
                    ServerAdmin = adminOnly,
                    ServerUser = !adminOnly,
                    AllowRunFromServer = true,
                    Call = arg => handler(arg)
                };
                Commands.Add(cmd);
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                if (dict != null) dict["global." + name] = cmd;
                if (globalDict != null) globalDict[name] = cmd;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlatformSync] RegisterConsoleCommand(" + name + "): " + ex.Message);
            }
        }

        public static void UnregisterCommands()
        {
            try
            {
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                foreach (var cmd in Commands)
                {
                    if (dict != null) dict.Remove(cmd.FullName);
                    if (globalDict != null) globalDict.Remove(cmd.Name);
                }
            }
            catch { }
            Commands.Clear();
        }

        #endregion

        /// <summary>Runs a coroutine even when ServerMgr does not exist yet (Harmony BeforeSceneLoad).</summary>
        private sealed class BootstrapRunner : MonoBehaviour
        {
            public static void Start(IEnumerator routine)
            {
                if (routine == null) return;
                if (ServerMgr.Instance != null)
                {
                    ServerMgr.Instance.StartCoroutine(routine);
                    return;
                }
                var go = new GameObject("PlatformSync_Bootstrap");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.AddComponent<BootstrapRunner>()._routine = routine;
            }

            private IEnumerator _routine;

            private void Start()
            {
                if (_routine != null)
                    StartCoroutine(RunAndDestroy(_routine));
            }

            private IEnumerator RunAndDestroy(IEnumerator routine)
            {
                yield return StartCoroutine(routine);
                if (this != null && gameObject != null)
                    UnityEngine.Object.Destroy(gameObject);
            }
        }
    }

}
