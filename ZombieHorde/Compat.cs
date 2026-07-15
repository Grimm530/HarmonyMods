using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace ZombieHorde
{
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
            int c = Major.CompareTo(other.Major);
            if (c != 0) return c;
            c = Minor.CompareTo(other.Minor);
            if (c != 0) return c;
            return Patch.CompareTo(other.Patch);
        }

        public static bool operator <(VersionNumber a, VersionNumber b) => a.CompareTo(b) < 0;
        public static bool operator >(VersionNumber a, VersionNumber b) => a.CompareTo(b) > 0;
        public static bool operator <=(VersionNumber a, VersionNumber b) => a.CompareTo(b) <= 0;
        public static bool operator >=(VersionNumber a, VersionNumber b) => a.CompareTo(b) >= 0;

        public override string ToString() => Major + "." + Minor + "." + Patch;
    }

    public static class Compat
    {
        public static readonly string ServerRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        public static string ConfigDirectory => Path.Combine(ServerRoot, "HarmonyConfig");
        public static string DataDirectory => Path.Combine(ServerRoot, "HarmonyData");
        public static string ConfigPath => Path.Combine(ConfigDirectory, "ZombieHorde.json");
        public static string OxideConfigPath => Path.Combine(ServerRoot, "oxide", "config", "ZombieHorde.json");

        public static readonly TimerHelper Timer = new TimerHelper();
        public static readonly PermissionHelper Permission = new PermissionHelper();
        public static readonly LangHelper Lang = new LangHelper();

        public static void EnsureFolders()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectory);
                Directory.CreateDirectory(Path.Combine(DataDirectory, "ZombieHorde"));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ZombieHorde] EnsureFolders: " + ex.Message);
            }
        }

        public static void NextTick(Action action)
        {
            if (action == null) return;
            BootstrapRunner.Start(NextTickCo(action), null);
        }

        private static IEnumerator NextTickCo(Action action)
        {
            yield return null;
            try { action(); }
            catch (Exception ex) { Debug.LogWarning("[ZombieHorde] NextTick: " + ex.Message); }
        }

        public static void Puts(string message) => Debug.Log("[ZombieHorde] " + message);
        public static void PrintWarning(string message) => Debug.LogWarning("[ZombieHorde] " + message);
        public static void PrintError(string message) => Debug.LogError("[ZombieHorde] " + message);

        public static BasePlayer GetPlayer(ConsoleSystem.Arg arg)
        {
            if (arg?.Connection == null) return null;
            var p = arg.Connection.player as BasePlayer;
            if (p != null) return p;
            return BasePlayer.FindByID(arg.Connection.userid);
        }

        public sealed class TimerHelper
        {
            private readonly List<Coroutine> _serverCoroutines = new List<Coroutine>();
            private readonly List<GameObject> _bootstrapObjects = new List<GameObject>();
            private int _generation;

            public void In(float delay, Action action) => Once(delay, action);

            public void Once(float delay, Action action)
            {
                if (action == null) return;
                int gen = _generation;
                BootstrapRunner.Start(OnceCo(delay, action, gen), Track);
            }

            public void Every(float interval, Action action)
            {
                if (action == null) return;
                int gen = _generation;
                BootstrapRunner.Start(EveryCo(interval, action, gen), Track);
            }

            /// <summary>
            /// Cancel pending Once/Every callbacks. Required on harmony.reload — ServerMgr coroutines
            /// otherwise fire against a null Instance/Configuration and NRE in SpawnOrder.Create.
            /// </summary>
            public void CancelAll()
            {
                _generation++;
                if (ServerMgr.Instance != null)
                {
                    for (int i = 0; i < _serverCoroutines.Count; i++)
                    {
                        Coroutine c = _serverCoroutines[i];
                        if (c != null)
                        {
                            try { ServerMgr.Instance.StopCoroutine(c); } catch { }
                        }
                    }
                }
                _serverCoroutines.Clear();

                for (int i = 0; i < _bootstrapObjects.Count; i++)
                {
                    GameObject go = _bootstrapObjects[i];
                    if (go != null)
                    {
                        try { UnityEngine.Object.Destroy(go); } catch { }
                    }
                }
                _bootstrapObjects.Clear();
            }

            private void Track(Coroutine coroutine, GameObject bootstrapGo)
            {
                if (coroutine != null)
                    _serverCoroutines.Add(coroutine);
                if (bootstrapGo != null)
                    _bootstrapObjects.Add(bootstrapGo);
            }

            private IEnumerator OnceCo(float delay, Action action, int gen)
            {
                if (delay > 0f) yield return new WaitForSeconds(delay);
                if (gen != _generation) yield break;
                try { action(); }
                catch (Exception ex) { Debug.LogWarning("[ZombieHorde] timer.Once: " + ex.Message); }
            }

            private IEnumerator EveryCo(float interval, Action action, int gen)
            {
                while (gen == _generation)
                {
                    if (interval > 0f) yield return new WaitForSeconds(interval);
                    else yield return null;
                    if (gen != _generation) yield break;
                    try { action(); }
                    catch (Exception ex) { Debug.LogWarning("[ZombieHorde] timer.Every: " + ex.Message); }
                }
            }
        }

        public sealed class PermissionHelper
        {
            private readonly HashSet<string> _registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, HashSet<string>> _userPerms =
                new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            private string PermsPath => Path.Combine(DataDirectory, "ZombieHorde", "permissions.json");
            private object _oxidePerm;
            private MethodInfo _userHasPermission;
            private MethodInfo _grantUserPermission;
            private MethodInfo _revokeUserPermission;
            private MethodInfo _registerPermission;
            private bool _resolved;

            private void Resolve()
            {
                if (_resolved) return;
                _resolved = true;
                try
                {
                    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Type iface = asm.GetType("Oxide.Core.Interface");
                        if (iface == null) continue;
                        object oxide = iface.GetProperty("Oxide", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                        if (oxide == null) continue;
                        MethodInfo getLib = oxide.GetType().GetMethod("GetLibrary", new[] { typeof(string) });
                        if (getLib != null)
                            _oxidePerm = getLib.Invoke(oxide, new object[] { "Permission" });
                        if (_oxidePerm == null) continue;
                        Type t = _oxidePerm.GetType();
                        _userHasPermission = t.GetMethod("UserHasPermission", new[] { typeof(string), typeof(string) });
                        _grantUserPermission = t.GetMethod("GrantUserPermission", new[] { typeof(string), typeof(string), typeof(object) });
                        _revokeUserPermission = t.GetMethod("RevokeUserPermission", new[] { typeof(string), typeof(string) });
                        _registerPermission = t.GetMethod("RegisterPermission", new[] { typeof(string), typeof(object) });
                        LoadLocal();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[ZombieHorde] Oxide permission resolve: " + ex.Message);
                }
                LoadLocal();
            }

            private void LoadLocal()
            {
                try
                {
                    if (!File.Exists(PermsPath)) return;
                    var data = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(File.ReadAllText(PermsPath));
                    if (data == null) return;
                    foreach (var kv in data)
                    {
                        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        if (kv.Value != null)
                            foreach (var p in kv.Value)
                                if (!string.IsNullOrWhiteSpace(p)) set.Add(p);
                        _userPerms[kv.Key] = set;
                    }
                }
                catch { }
            }

            private void SaveLocal()
            {
                try
                {
                    var dir = Path.GetDirectoryName(PermsPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    var data = new Dictionary<string, List<string>>();
                    foreach (var kv in _userPerms)
                        data[kv.Key] = new List<string>(kv.Value);
                    File.WriteAllText(PermsPath, JsonConvert.SerializeObject(data, Formatting.Indented));
                }
                catch { }
            }

            public void RegisterPermission(string name, object owner)
            {
                Resolve();
                _registered.Add(name);
                if (_oxidePerm != null && _registerPermission != null)
                {
                    try { _registerPermission.Invoke(_oxidePerm, new object[] { name, owner }); }
                    catch { }
                }
            }

            public bool UserHasPermission(string userId, string perm)
            {
                Resolve();
                if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(perm)) return false;

                // Admins always pass admin permission
                if (perm.Equals("zombiehorde.admin", StringComparison.OrdinalIgnoreCase))
                {
                    if (ulong.TryParse(userId, out ulong uid))
                    {
                        var player = BasePlayer.FindByID(uid) ?? BasePlayer.FindSleeping(uid);
                        if (player != null && (player.IsAdmin || player.IsDeveloper))
                            return true;
                    }
                }

                if (_oxidePerm != null && _userHasPermission != null)
                {
                    try { return (bool)_userHasPermission.Invoke(_oxidePerm, new object[] { userId, perm }); }
                    catch { }
                }
                return _userPerms.TryGetValue(userId, out var set) && set.Contains(perm);
            }

            public void GrantUserPermission(string userId, string perm, object owner = null)
            {
                Resolve();
                if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(perm)) return;
                if (_oxidePerm != null && _grantUserPermission != null)
                {
                    try { _grantUserPermission.Invoke(_oxidePerm, new object[] { userId, perm, owner }); return; }
                    catch { }
                }
                if (!_userPerms.TryGetValue(userId, out var set))
                    _userPerms[userId] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (set.Add(perm)) SaveLocal();
            }

            public void RevokeUserPermission(string userId, string perm)
            {
                Resolve();
                if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(perm)) return;
                if (_oxidePerm != null && _revokeUserPermission != null)
                {
                    try { _revokeUserPermission.Invoke(_oxidePerm, new object[] { userId, perm }); return; }
                    catch { }
                }
                if (_userPerms.TryGetValue(userId, out var set) && set.Remove(perm))
                    SaveLocal();
            }
        }

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

        public sealed class PluginRef
        {
            private readonly string _name;
            private object _plugin;
            private MethodInfo _call;
            private bool _tried;

            public PluginRef(string name) { _name = name; }

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
                    if (_call == null)
                        _call = _plugin.GetType().GetMethod("Call", new[] { typeof(string), typeof(object[]) });
                    if (_call != null)
                        return _call.Invoke(_plugin, new object[] { method, args ?? Array.Empty<object>() });
                    var direct = _plugin.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                    return direct?.Invoke(_plugin, args ?? Array.Empty<object>());
                }
                catch { return null; }
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
            }

            public void Reset() { _tried = false; _plugin = null; _call = null; }
        }

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
                Debug.LogWarning("[ZombieHorde] RegisterConsoleCommand(" + name + "): " + ex.Message);
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

        private sealed class BootstrapRunner : MonoBehaviour
        {
            public static void Start(IEnumerator routine, Action<Coroutine, GameObject> track)
            {
                if (routine == null) return;
                if (ServerMgr.Instance != null)
                {
                    Coroutine c = ServerMgr.Instance.StartCoroutine(routine);
                    track?.Invoke(c, null);
                    return;
                }
                var go = new GameObject("ZombieHorde_Bootstrap");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.AddComponent<BootstrapRunner>()._routine = routine;
                track?.Invoke(null, go);
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
