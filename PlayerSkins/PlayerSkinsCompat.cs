/*
 * Oxide-free shims for PlayerSkins 3.0.141 Chaos UI under Harmony.
 * No Oxide assemblies are referenced or loaded.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace PlayerSkinsHarmony
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

    public static class PlayerExtensions
    {
        public static bool IsSteamId(this ulong id) => id > 76561197960265728UL;

        public static bool IsSteamId(this string id) =>
            !string.IsNullOrEmpty(id) && ulong.TryParse(id, out var uid) && uid.IsSteamId();

        public static bool IsSteamId(this EncryptedValue<ulong> id)
        {
            try { return id.Get().IsSteamId(); }
            catch
            {
                try { return ((ulong)id).IsSteamId(); }
                catch { return false; }
            }
        }

        public static bool HasPermission(this BasePlayer player, string perm)
        {
            if (player == null || string.IsNullOrEmpty(perm)) return false;
            return PermissionsBridge.UserHasPermission(player.UserIDString, perm);
        }

        public static ulong GetUserId(this BasePlayer player)
        {
            if (player == null) return 0UL;
            try { return player.userID.Get(); }
            catch { return (ulong)player.userID; }
        }

        public static void LocalizedMessage(this BasePlayer player, object plugin, string key, params object[] args)
        {
            if (player == null || string.IsNullOrEmpty(key)) return;
            var lang = PlayerSkinsHost.Instance?.Lang;
            var msg = lang?.GetMessage(key, plugin, player.UserIDString) ?? key;
            if (args != null && args.Length > 0)
            {
                try { msg = string.Format(msg, args); }
                catch { }
            }
            player.ChatMessage(msg);
        }

        public static void LocalizedMessage(this BasePlayer player, object plugin, string key)
        {
            if (player == null || string.IsNullOrEmpty(key)) return;
            var lang = PlayerSkinsHost.Instance?.Lang;
            player.ChatMessage(lang?.GetMessage(key, plugin, player.UserIDString) ?? key);
        }
    }

    public class LangHelper
    {
        private readonly Dictionary<string, Dictionary<string, string>> _byLang =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _file =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public int FileMessageCount => _file.Count;

        public void RegisterMessages(Dictionary<string, string> messages)
        {
            RegisterMessages(messages, null, "en");
        }

        public void RegisterMessages(Dictionary<string, string> messages, object plugin, string language = "en")
        {
            if (messages == null) return;
            language = string.IsNullOrEmpty(language) ? "en" : language;
            if (!_byLang.TryGetValue(language, out var dict))
                _byLang[language] = dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in messages)
                dict[kv.Key] = kv.Value;
        }

        public bool LoadLanguageFile(string path)
        {
            _file = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            try
            {
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                if (loaded == null) return false;
                foreach (var kv in loaded)
                {
                    if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
                        _file[kv.Key] = kv.Value;
                }
                return _file.Count > 0;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlayerSkins] Lang file load failed: " + ex.Message);
                return false;
            }
        }

        public string GetMessage(string key, object plugin, string userId)
        {
            if (string.IsNullOrEmpty(key)) return "";
            if (_file.TryGetValue(key, out var fromFile) && !string.IsNullOrEmpty(fromFile))
                return fromFile;

            var lang = string.IsNullOrEmpty(userId) ? "en" : "en";
            if (_byLang.TryGetValue(lang, out var dict) && dict.TryGetValue(key, out var msg))
                return msg;
            if (_byLang.TryGetValue("en", out var en) && en.TryGetValue(key, out msg))
                return msg;
            foreach (var d in _byLang.Values)
            {
                if (d.TryGetValue(key, out msg))
                    return msg;
            }
            return key;
        }

        public string GetMessage(string key, string userId = null) =>
            GetMessage(key, null, userId);
    }

    public class Timer
    {
        public Action Callback;
        public bool Destroyed { get; private set; }
        public void Destroy() => Destroyed = true;
    }

    public class TimerLib
    {
        private readonly List<Timer> _timers = new List<Timer>();

        public void DestroyAll()
        {
            List<Timer> copy;
            lock (_timers) { copy = new List<Timer>(_timers); _timers.Clear(); }
            foreach (var t in copy) t?.Destroy();
        }

        public Timer Once(float seconds, Action callback)
        {
            if (callback == null) return new Timer();
            var t = new Timer { Callback = callback };
            lock (_timers) _timers.Add(t);
            try
            {
                ServerMgr.Instance?.StartCoroutine(WaitAndRun(seconds, t, () =>
                {
                    if (!t.Destroyed)
                    {
                        try { callback(); }
                        catch (Exception ex) { Debug.LogWarning("[PlayerSkins] Timer: " + ex.Message); }
                    }
                    t.Destroy();
                    lock (_timers) _timers.Remove(t);
                }));
            }
            catch { lock (_timers) _timers.Remove(t); }
            return t;
        }

        public Timer In(float seconds, Action callback) => Once(seconds, callback);

        public Timer Every(float seconds, Action callback) => Repeat(seconds, -1, callback);

        public Timer Repeat(float seconds, int times, Action callback)
        {
            if (callback == null) return new Timer();
            var t = new Timer { Callback = callback };
            lock (_timers) _timers.Add(t);
            try
            {
                ServerMgr.Instance?.StartCoroutine(RepeatRun(seconds, times, t, callback));
            }
            catch { lock (_timers) _timers.Remove(t); }
            return t;
        }

        private static IEnumerator WaitAndRun(float seconds, Timer timer, Action callback)
        {
            if (seconds > 0f)
                yield return new WaitForSeconds(seconds);
            else
                yield return null;
            try { callback?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning("[PlayerSkins] Timer: " + ex.Message); }
        }

        private IEnumerator RepeatRun(float seconds, int times, Timer timer, Action callback)
        {
            int count = 0;
            while (!timer.Destroyed && (times < 0 || count < times))
            {
                if (seconds > 0f)
                    yield return new WaitForSeconds(seconds);
                else
                    yield return null;
                if (timer.Destroyed) break;
                try { callback(); }
                catch (Exception ex) { Debug.LogWarning("[PlayerSkins] Timer: " + ex.Message); }
                count++;
            }
            timer.Destroy();
            lock (_timers) _timers.Remove(timer);
        }
    }

    public enum RequestMethod
    {
        GET,
        POST
    }

    public class WebRequestHelper
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
                    using (var req = new HttpRequestMessage(
                               method == RequestMethod.POST ? HttpMethod.Post : HttpMethod.Get, url))
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
                        using (var resp = await Http.SendAsync(req).ConfigureAwait(false))
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
                try
                {
                    Interface.NextTick(() =>
                    {
                        try { callback?.Invoke(code, response); }
                        catch (Exception ex) { Debug.LogWarning("[PlayerSkins] webrequest callback: " + ex.Message); }
                    });
                }
                catch { }
            });
        }
    }

    /// <summary>Oxide-like permission API; plugin owner args ignored.</summary>
    public class PermissionLib
    {
        private readonly HashSet<string> _registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Action _readyCallback;

        private void EnsureReadyCallback()
        {
            if (_readyCallback != null) return;
            _readyCallback = () =>
            {
                foreach (var perm in _registered)
                {
                    try { PermissionsBridge.RegisterPermission(perm); } catch { }
                }
            };
            PermissionsBridge.RegisterReadyCallback(_readyCallback);
        }

        public void RegisterPermission(string perm, object owner = null)
        {
            if (string.IsNullOrEmpty(perm)) return;
            _registered.Add(perm);
            PermissionsBridge.RegisterPermission(perm);
            EnsureReadyCallback();
        }

        public bool UserHasPermission(string playerId, string perm) =>
            PermissionsBridge.UserHasPermission(playerId, perm);

        public bool PermissionExists(string perm, object owner = null) =>
            PermissionsBridge.PermissionExists(perm);
    }

    public class DynamicConfigFile
    {
        private object _data;
        private readonly string _path;

        public JsonSerializerSettings Settings { get; } = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Populate
        };

        public DynamicConfigFile(string path = null, object data = null)
        {
            _path = path;
            _data = data;
        }

        public T ReadObject<T>() where T : class, new()
        {
            try
            {
                if (string.IsNullOrEmpty(_path) || !File.Exists(_path))
                    return new T();
                return JsonConvert.DeserializeObject<T>(File.ReadAllText(_path), Settings) ?? new T();
            }
            catch
            {
                return new T();
            }
        }

        public void WriteObject(object obj, bool sync = true)
        {
            if (obj == null) return;
            _data = obj;
            if (sync) Save();
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(_path) || _data == null) return;
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonConvert.SerializeObject(_data, Settings));
        }
    }

    public class OxideStub
    {
        public void NextTick(Action action) => Interface.NextTick(action);

        public object CallHook(string name, params object[] args) => Interface.CallHook(name, args);
    }

    public static class Interface
    {
        public static OxideStub Oxide { get; } = new OxideStub();

        public static void NextTick(Action action)
        {
            try { ServerMgr.Instance?.StartCoroutine(NextTickCoroutine(action)); }
            catch (Exception ex) { Debug.LogWarning("[PlayerSkins] NextTick: " + ex.Message); }
        }

        private static IEnumerator NextTickCoroutine(Action action)
        {
            yield return null;
            try { action?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning("[PlayerSkins] NextTick: " + ex.Message); }
        }

        public static object CallHook(string name, params object[] args)
        {
            if (string.IsNullOrEmpty(name)) return null;
            object result = null;
            try
            {
                var key = "PlayerSkins_Hook_" + name;
                if (AppDomain.CurrentDomain.GetData(key) is Func<object[], object> fn)
                    result = fn(args);

                if (AppDomain.CurrentDomain.GetData("PlayerSkins_HookHandlers_" + name) is IList handlers)
                {
                    foreach (var handler in handlers)
                    {
                        try
                        {
                            if (handler is Action<object[]> action)
                                action(args);
                            else if (handler is Func<object[], object> func)
                                func(args);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning("[PlayerSkins] CallHook handler " + name + ": " + ex.Message);
                        }
                    }
                }

                if (string.Equals(name, "OnPlayerSkinsSkinsLoaded", StringComparison.Ordinal))
                    FireSkinsLoadedHook(args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlayerSkins] CallHook(" + name + "): " + ex.Message);
            }
            return result;
        }

        private static void FireSkinsLoadedHook(object[] args)
        {
            try
            {
                if (AppDomain.CurrentDomain.GetData("PlayerSkins_OnSkinsLoaded") is Action<object[]> direct)
                    direct(args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlayerSkins] OnPlayerSkinsSkinsLoaded: " + ex.Message);
            }
        }
    }

    public class PlayerSkinsHost
    {
        public static PlayerSkinsHost Instance { get; private set; }

        public string ServerRoot { get; private set; }
        public string ConfigDirectory { get; private set; }
        public string DataDirectory { get; private set; }
        public string LangDirectory { get; private set; }
        public string ConfigPath { get; private set; }
        public string LangPath { get; private set; }
        public string UserDataPath { get; private set; }
        public string SkinListPath { get; private set; }
        public string ExcludedSkinsPath { get; private set; }

        public LangHelper Lang { get; } = new LangHelper();
        public TimerLib Timer { get; } = new TimerLib();
        public WebRequestHelper Webrequest { get; } = new WebRequestHelper();
        public PermissionLib Permission { get; } = new PermissionLib();
        public DynamicConfigFile Config { get; private set; }

        public static void Init(string serverRoot)
        {
            Instance = new PlayerSkinsHost();
            Instance.ServerRoot = serverRoot ?? ".";
            Instance.ConfigDirectory = Path.Combine(Instance.ServerRoot, "HarmonyConfig");
            Instance.DataDirectory = Path.Combine(Instance.ServerRoot, "HarmonyData", "PlayerSkins");
            Instance.LangDirectory = Path.Combine(Instance.ServerRoot, "HarmonyLanguage");
            Instance.ConfigPath = Path.Combine(Instance.ConfigDirectory, "PlayerSkins.json");
            Instance.LangPath = Path.Combine(Instance.LangDirectory, "PlayerSkins.json");
            Instance.UserDataPath = Path.Combine(Instance.DataDirectory, "userdata.json");
            Instance.SkinListPath = Path.Combine(Instance.DataDirectory, "skinlist.json");
            Instance.ExcludedSkinsPath = Path.Combine(Instance.DataDirectory, "excludedskins.json");

            Directory.CreateDirectory(Instance.ConfigDirectory);
            Directory.CreateDirectory(Instance.DataDirectory);
            Directory.CreateDirectory(Instance.LangDirectory);

            if (File.Exists(Instance.ConfigPath))
            {
                try
                {
                    Instance.Config = new DynamicConfigFile(Instance.ConfigPath,
                        JsonConvert.DeserializeObject(File.ReadAllText(Instance.ConfigPath)));
                }
                catch
                {
                    Instance.Config = new DynamicConfigFile(Instance.ConfigPath);
                }
            }
            else
            {
                Instance.Config = new DynamicConfigFile(Instance.ConfigPath);
            }

            Debug.Log("[PlayerSkins] Config: " + Instance.ConfigPath);
            Debug.Log("[PlayerSkins] Data:   " + Instance.DataDirectory);
            Debug.Log("[PlayerSkins] Lang:   " + Instance.LangPath);
        }

        public void ReloadLanguage()
        {
            if (Lang.LoadLanguageFile(LangPath))
                Debug.Log("[PlayerSkins] OK: Loaded " + Lang.FileMessageCount + " lang entries from HarmonyLanguage/PlayerSkins.json");
            else
                Debug.LogWarning("[PlayerSkins] HarmonyLanguage/PlayerSkins.json missing or empty - using embedded defaults");
        }

        public static void Shutdown()
        {
            Instance?.Timer?.DestroyAll();
            Instance = null;
        }

        public void Puts(string message) => Debug.Log("[PlayerSkins] " + message);
        public void Puts(string format, params object[] args) =>
            Debug.Log("[PlayerSkins] " + (args == null || args.Length == 0 ? format : string.Format(format, args)));
        public void PrintWarning(string message) => Debug.LogWarning("[PlayerSkins] " + message);
        public void PrintError(string message) => Debug.LogError("[PlayerSkins] " + message);
        public void PrintError(string format, params object[] args) =>
            Debug.LogError("[PlayerSkins] " + (args == null || args.Length == 0 ? format : string.Format(format, args)));
    }

    public abstract class PlayerSkinsPluginBase
    {
        public string Title => "PlayerSkins";
        public string Name => "PlayerSkins";
        public VersionNumber Version { get; protected set; } = new VersionNumber(3, 0, 142);
        public bool IsLoaded { get; set; } = true;

        protected PlayerSkinsHost Host => PlayerSkinsHost.Instance;
        protected PermissionLib permission => Host?.Permission;
        protected LangHelper lang => Host?.Lang;
        protected TimerLib timer => Host?.Timer;
        protected WebRequestHelper webrequest => Host?.Webrequest;
        protected DynamicConfigFile Config => Host?.Config;

        protected Dictionary<string, string> m_Messages;

        protected ConfigurationFile m_ConfigurationFile;

        protected BaseConfigData ConfigurationData
        {
            get => m_ConfigurationFile?.Data;
            set
            {
                if (m_ConfigurationFile != null)
                    m_ConfigurationFile.Data = value;
            }
        }

        protected void Puts(string message) => Host?.Puts(message);
        protected void Puts(string format, params object[] args) => Host?.Puts(format, args);
        protected void PrintWarning(string message) => Host?.PrintWarning(message);
        protected void PrintError(string message) => Host?.PrintError(message);
        protected void PrintError(string format, params object[] args) => Host?.PrintError(format, args);

        protected void SendReply(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrEmpty(message)) return;
            player.ChatMessage(message);
        }

        protected void SendReply(ConsoleSystem.Arg arg, string message)
        {
            if (arg == null) return;
            var player = arg.Player();
            if (player != null)
                player.ChatMessage(message);
            else
                Debug.Log("[PlayerSkins] " + message);
        }

        protected string GetString(string key, BasePlayer player) => GetString(key, player?.UserIDString);
        protected string GetString(string key, ulong playerId) => GetString(key, playerId.ToString());
        protected string GetString(string key, string playerId) =>
            lang?.GetMessage(key, this, playerId ?? "0") ?? key ?? "";

        protected string FormatString(string key, BasePlayer player, params object[] args) =>
            FormatString(key, player?.UserIDString, args);

        protected string FormatString(string key, ulong playerId, params object[] args) =>
            FormatString(key, playerId.ToString(), args);

        protected string FormatString(string key, string playerId, params object[] args)
        {
            var msg = GetString(key, playerId);
            if (args == null || args.Length == 0) return msg;
            try { return string.Format(msg, args); }
            catch { return msg; }
        }

        protected static T ParseType<T>(string type)
        {
            try { return (T)Enum.Parse(typeof(T), type, true); }
            catch { return default; }
        }

        protected void RegisterLang()
        {
            if (m_Messages != null && m_Messages.Count > 0)
                lang?.RegisterMessages(m_Messages, this);
        }

        protected void LoadConfig()
        {
            PrepareConfigFile(ref m_ConfigurationFile);
            if (m_ConfigurationFile == null)
            {
                LoadDefaultConfig();
                return;
            }

            m_ConfigurationFile.Load();
            if (m_ConfigurationFile.Data == null)
            {
                LoadDefaultConfig();
                return;
            }

            if (m_ConfigurationFile.Data.Version < Version)
            {
                var oldVersion = m_ConfigurationFile.Data.Version;
                UpdateConfigValues(oldVersion);
            }
        }

        protected void LoadDefaultConfig()
        {
            PrepareConfigFile(ref m_ConfigurationFile);
            m_ConfigurationFile?.Create(this);
            SaveConfiguration();
        }

        protected void UpdateConfigValues(VersionNumber oldVersion)
        {
            PrintWarning("Updating configuration from version " + oldVersion + " to " + Version);
            OnConfigurationUpdated(oldVersion);
            if (m_ConfigurationFile?.Data != null)
                m_ConfigurationFile.Data.Version = Version;
            PrintWarning("Configuration update complete");
            SaveConfiguration();
        }

        protected void SaveConfiguration() => m_ConfigurationFile?.Save();

        protected virtual void OnConfigurationUpdated(VersionNumber oldVersion) { }

        protected virtual ConfigurationFile OnLoadConfig(ref ConfigurationFile configurationFile)
        {
            configurationFile = null;
            return null;
        }

        protected virtual void PrepareConfigFile(ref ConfigurationFile configurationFile) =>
            OnLoadConfig(ref configurationFile);

        protected virtual T GenerateDefaultConfiguration<T>() where T : BaseConfigData, new() => null;

        public abstract void HarmonyInit();
        public abstract void HarmonyServerInitialized();
        public abstract void HarmonyUnload();

        public abstract class BaseConfigData
        {
            public VersionNumber Version { get; set; }
        }

        protected abstract class ConfigurationFile
        {
            protected DynamicConfigFile ConfigFile;

            public abstract BaseConfigData Data { get; set; }

            protected ConfigurationFile(DynamicConfigFile configFile)
            {
                ConfigFile = configFile;
            }

            public abstract void Load();
            public abstract void Save();
            public abstract void Create(PlayerSkinsPluginBase plugin);
        }

        protected class ConfigurationFile<T> : ConfigurationFile where T : BaseConfigData, new()
        {
            public T m_ConfigData;

            public override BaseConfigData Data
            {
                get => m_ConfigData;
                set => m_ConfigData = value as T;
            }

            public ConfigurationFile(DynamicConfigFile configFile) : base(configFile) { }

            public override void Load()
            {
                m_ConfigData = ConfigFile.ReadObject<T>();
                if (m_ConfigData == null)
                    m_ConfigData = Activator.CreateInstance<T>();
            }

            public override void Save()
            {
                if (m_ConfigData != null)
                    ConfigFile.WriteObject(m_ConfigData, true);
            }

            public override void Create(PlayerSkinsPluginBase plugin)
            {
                m_ConfigData = plugin.GenerateDefaultConfiguration<T>();
                if (m_ConfigData == null)
                    m_ConfigData = Activator.CreateInstance<T>();
                if (m_ConfigData.Version.Major == 0 && m_ConfigData.Version.Minor == 0 && m_ConfigData.Version.Patch == 0)
                    m_ConfigData.Version = plugin.Version;
            }
        }
    }
}

namespace Oxide.Ext.Chaos
{
    /// <summary>Oxide Hash alias used by Chaos plugins (default-on-miss indexer).</summary>
    public class Hash<TKey, TValue> : Dictionary<TKey, TValue>
    {
        public new TValue this[TKey key]
        {
            get => TryGetValue(key, out var value) ? value : default;
            set => base[key] = value;
        }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class PermissionAttribute : Attribute
    {
    }

    public static class Economics
    {
        private static Type _apiType;
        private static MethodInfo _balanceObj;
        private static MethodInfo _balanceUlong;
        private static MethodInfo _depositObj;
        private static MethodInfo _depositUlong;
        private static MethodInfo _withdrawObj;
        private static MethodInfo _withdrawUlong;
        private static object _pluginWrapper;
        private static MethodInfo _pluginCall;
        private static bool _loggedLink;

        public static bool IsLoaded
        {
            get
            {
                EnsureBound();
                return _apiType != null || _pluginWrapper != null;
            }
        }

        public static double Balance(object playerId)
        {
            EnsureBound();
            try
            {
                if (_balanceObj != null && _balanceObj.Invoke(null, new[] { playerId }) is double d1)
                    return d1;
                if (_balanceUlong != null && TryToUlong(playerId, out var uid) &&
                    _balanceUlong.Invoke(null, new object[] { uid }) is double d2)
                    return d2;
                var viaCall = PluginCall("Balance", playerId);
                if (viaCall != null) return Convert.ToDouble(viaCall);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlayerSkins] Economics.Balance: " + ex.Message);
            }
            return 0d;
        }

        public static double Balance(ulong playerId) => Balance((object)playerId);

        public static bool Deposit(object playerId, double amount)
        {
            EnsureBound();
            try
            {
                if (_depositObj != null && _depositObj.Invoke(null, new[] { playerId, amount }) is bool b1)
                    return b1;
                if (_depositUlong != null && TryToUlong(playerId, out var uid) &&
                    _depositUlong.Invoke(null, new object[] { uid, amount }) is bool b2)
                    return b2;
                var viaCall = PluginCall("Deposit", playerId, amount);
                if (viaCall is bool b3) return b3;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlayerSkins] Economics.Deposit: " + ex.Message);
            }
            return false;
        }

        public static bool Withdraw(object playerId, double amount)
        {
            EnsureBound();
            try
            {
                if (_withdrawObj != null && _withdrawObj.Invoke(null, new[] { playerId, amount }) is bool b1)
                    return b1;
                if (_withdrawUlong != null && TryToUlong(playerId, out var uid) &&
                    _withdrawUlong.Invoke(null, new object[] { uid, amount }) is bool b2)
                    return b2;
                var viaCall = PluginCall("Withdraw", playerId, amount);
                if (viaCall is bool b3) return b3;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlayerSkins] Economics.Withdraw: " + ex.Message);
            }
            return false;
        }

        private static void ClearBind()
        {
            _apiType = null;
            _balanceObj = _balanceUlong = _depositObj = _depositUlong = _withdrawObj = _withdrawUlong = null;
            _pluginWrapper = null;
            _pluginCall = null;
        }

        private static void EnsureBound()
        {
            object liveWrapper = AppDomain.CurrentDomain.GetData("Economics_Plugin");
            Type liveType = AppDomain.CurrentDomain.GetData("Economics_ApiType") as Type;
            bool alive = (_apiType != null || _pluginWrapper != null)
                         && ReferenceEquals(_pluginWrapper, liveWrapper)
                         && (_apiType == null || _apiType == liveType);
            if (alive && (_apiType != null || _pluginWrapper != null)) return;

            try
            {
                ClearBind();
                _apiType = liveType;
                if (_apiType == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            _apiType = asm.GetType("EconomicsHarmony.EconomicsHarmonyMod")
                                      ?? asm.GetType("EconomicsHarmony.EconomicsMod");
                            if (_apiType != null) break;
                        }
                        catch { }
                    }
                }

                if (_apiType != null)
                {
                    const BindingFlags S = BindingFlags.Public | BindingFlags.Static;
                    _balanceObj = _apiType.GetMethod("Balance", S, null, new[] { typeof(object) }, null)
                               ?? _apiType.GetMethod("Balance", S, null, new[] { typeof(string) }, null);
                    _balanceUlong = _apiType.GetMethod("Balance", S, null, new[] { typeof(ulong) }, null);
                    _depositObj = _apiType.GetMethod("Deposit", S, null, new[] { typeof(object), typeof(double) }, null)
                               ?? _apiType.GetMethod("Deposit", S, null, new[] { typeof(string), typeof(double) }, null);
                    _depositUlong = _apiType.GetMethod("Deposit", S, null, new[] { typeof(ulong), typeof(double) }, null);
                    _withdrawObj = _apiType.GetMethod("Withdraw", S, null, new[] { typeof(object), typeof(double) }, null)
                                ?? _apiType.GetMethod("Withdraw", S, null, new[] { typeof(string), typeof(double) }, null);
                    _withdrawUlong = _apiType.GetMethod("Withdraw", S, null, new[] { typeof(ulong), typeof(double) }, null);
                }

                _pluginWrapper = liveWrapper;
                if (_pluginWrapper != null)
                {
                    _pluginCall = _pluginWrapper.GetType().GetMethod("Call",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { typeof(string), typeof(object[]) }, null);
                }

                if ((_apiType != null || _pluginWrapper != null) && !_loggedLink)
                {
                    _loggedLink = true;
                    Debug.Log("[PlayerSkins] Linked to Economics Harmony mod.");
                }
            }
            catch (Exception ex)
            {
                ClearBind();
                Debug.LogWarning("[PlayerSkins] Economics bind: " + ex.Message);
            }
        }

        private static object PluginCall(string method, params object[] args)
        {
            if (_pluginCall == null || _pluginWrapper == null) return null;
            try { return _pluginCall.Invoke(_pluginWrapper, new object[] { method, args }); }
            catch { return null; }
        }

        private static bool TryToUlong(object value, out ulong result)
        {
            result = 0UL;
            if (value == null) return false;
            if (value is ulong u) { result = u; return true; }
            if (value is long l && l >= 0) { result = (ulong)l; return true; }
            if (value is string s && ulong.TryParse(s, out var parsed)) { result = parsed; return true; }
            if (value is EncryptedValue<ulong> ev)
            {
                try { result = ev.Get(); return true; }
                catch
                {
                    try { result = (ulong)ev; return true; }
                    catch { return false; }
                }
            }
            try
            {
                result = Convert.ToUInt64(value);
                return true;
            }
            catch { return false; }
        }
    }

    public static class ServerRewards
    {
        private static Type _apiType;
        private static MethodInfo _checkPoints;
        private static MethodInfo _takePoints;
        private static MethodInfo _addPoints;
        private static bool _bound;

        public static bool IsLoaded
        {
            get
            {
                EnsureBound();
                return _apiType != null;
            }
        }

        public static object CheckPoints(object playerId)
        {
            EnsureBound();
            try
            {
                if (_checkPoints != null)
                    return _checkPoints.Invoke(null, new[] { playerId });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlayerSkins] ServerRewards.CheckPoints: " + ex.Message);
            }
            return 0;
        }

        public static object TakePoints(object playerId, int amount)
        {
            EnsureBound();
            try
            {
                if (_takePoints != null)
                    return _takePoints.Invoke(null, new[] { playerId, amount });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlayerSkins] ServerRewards.TakePoints: " + ex.Message);
            }
            return null;
        }

        public static object AddPoints(object playerId, int amount)
        {
            EnsureBound();
            try
            {
                if (_addPoints != null)
                    return _addPoints.Invoke(null, new[] { playerId, amount });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlayerSkins] ServerRewards.AddPoints: " + ex.Message);
            }
            return null;
        }

        private static void EnsureBound()
        {
            if (_bound) return;
            _bound = true;
            try
            {
                _apiType = AppDomain.CurrentDomain.GetData("ServerRewards_ApiType") as Type;
                if (_apiType == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            _apiType = asm.GetType("ServerRewardsHarmony.ServerRewardsMod");
                            if (_apiType != null) break;
                        }
                        catch { }
                    }
                }
                if (_apiType == null) return;

                const BindingFlags S = BindingFlags.Public | BindingFlags.Static;
                _checkPoints = _apiType.GetMethod("CheckPoints", S, null, new[] { typeof(object) }, null)
                            ?? FindMethod(_apiType, "CheckPoints", 1);
                _takePoints = _apiType.GetMethod("TakePoints", S, null, new[] { typeof(object), typeof(int) }, null)
                           ?? FindMethod(_apiType, "TakePoints", 2);
                _addPoints = _apiType.GetMethod("AddPoints", S, null, new[] { typeof(object), typeof(int) }, null)
                          ?? FindMethod(_apiType, "AddPoints", 2);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlayerSkins] ServerRewards bind: " + ex.Message);
            }
        }

        private static MethodInfo FindMethod(Type type, string name, int paramCount)
        {
            foreach (var mi in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (mi.Name == name && mi.GetParameters().Length == paramCount)
                    return mi;
            }
            return null;
        }
    }

    public static class ImageLibrary
    {
        private static Type _apiType;
        private static MethodInfo _addImage;
        private static MethodInfo _getImage;
        private static bool _bridgeAttempted;
        private static bool _useFallback;

        public static bool IsLoaded
        {
            get
            {
                EnsureBridge();
                return _apiType != null || _useFallback;
            }
        }

        public static bool AddImage(string url, string imageName, ulong skinId, Action callback = null)
        {
            EnsureBridge();
            if (!_useFallback && _addImage != null)
            {
                try
                {
                    if (_addImage.Invoke(null, new object[] { url, imageName, skinId, callback }) is bool ok)
                        return ok;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[PlayerSkins] ImageLibrary.AddImage bridge: " + ex.Message);
                }
            }

            ImageLibraryFallback.Enqueue(url, imageName, skinId, callback);
            return true;
        }

        public static string GetImage(string imageName, ulong skinId = 0UL)
        {
            EnsureBridge();
            if (!_useFallback && _getImage != null)
            {
                try
                {
                    if (_getImage.Invoke(null, new object[] { imageName, skinId }) is string s)
                        return s ?? string.Empty;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[PlayerSkins] ImageLibrary.GetImage bridge: " + ex.Message);
                }
            }
            return ImageLibraryFallback.Get(imageName, skinId);
        }

        private static void EnsureBridge()
        {
            if (_bridgeAttempted) return;
            _bridgeAttempted = true;
            try
            {
                _apiType = AppDomain.CurrentDomain.GetData("ImageLibrary_ApiType") as Type;
                if (_apiType == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            _apiType = asm.GetType("ImageLibraryHarmony.ImageLibraryMod")
                                      ?? asm.GetType("ImageLibrary.ImageLibraryMod");
                            if (_apiType != null) break;
                        }
                        catch { }
                    }
                }

                if (_apiType != null)
                {
                    const BindingFlags S = BindingFlags.Public | BindingFlags.Static;
                    _addImage = _apiType.GetMethod("AddImage", S);
                    _getImage = _apiType.GetMethod("GetImage", S);
                    if (_addImage == null && _getImage == null)
                        _useFallback = true;
                }
                else
                {
                    _useFallback = true;
                }
            }
            catch
            {
                _useFallback = true;
            }
        }

        private static class ImageLibraryFallback
        {
            private static readonly Dictionary<string, string> Cache =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            private static readonly HashSet<string> Pending =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private static readonly HttpClient Http = new HttpClient();

            private static string Key(string name, ulong skin) => (name ?? "") + ":" + skin;

            public static string Get(string name, ulong skin)
            {
                return Cache.TryGetValue(Key(name, skin), out var crc) ? crc : string.Empty;
            }

            public static void Enqueue(string url, string name, ulong skin, Action callback)
            {
                var key = Key(name, skin);
                if (Cache.ContainsKey(key))
                {
                    PlayerSkinsHarmony.Interface.NextTick(() =>
                    {
                        try { callback?.Invoke(); } catch { }
                    });
                    return;
                }
                lock (Pending)
                {
                    if (!Pending.Add(key)) return;
                }

                Task.Run(async () =>
                {
                    try
                    {
                        var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
                        PlayerSkinsHarmony.Interface.NextTick(() =>
                        {
                            try
                            {
                                StoreBytes(key, bytes);
                                callback?.Invoke();
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning("[PlayerSkins] ImageLibrary fallback callback: " + ex.Message);
                            }
                            finally
                            {
                                lock (Pending) Pending.Remove(key);
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[PlayerSkins] ImageLibrary fallback download: " + ex.Message);
                        lock (Pending) Pending.Remove(key);
                    }
                });
            }

            private static void StoreBytes(string key, byte[] bytes)
            {
                if (bytes == null || bytes.Length == 0) return;
                try
                {
                    var ce = CommunityEntity.ServerInstance;
                    if (ce == null || ce.net == null) return;
                    var crc = FileStorage.server.Store(bytes, FileStorage.Type.png, ce.net.ID);
                    Cache[key] = crc.ToString();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[PlayerSkins] ImageLibrary fallback store: " + ex.Message);
                }
            }
        }
    }

    public static class PlayerDlcApi
    {
        private static Type _apiType;

        public static bool IsLoaded
        {
            get
            {
                EnsureBound();
                return _apiType != null;
            }
        }

        public static bool IsPaidSkin(ulong workshopId) => InvokeBool("IsPaidSkin", workshopId);

        public static bool IsRedirectedSkin(string shortname) => InvokeBool("IsRedirectedSkin", shortname);

        public static bool IsRedirectedSkin(int itemId) => InvokeBool("IsRedirectedSkin", itemId);

        public static bool IsOwnedOrFreeSkin(BasePlayer player, ulong workshopId) =>
            InvokeBool("IsOwnedOrFreeSkin", player, workshopId);

        public static bool FilterOwnedOrFreeSkins(BasePlayer player, List<ulong> workshopIds) =>
            InvokeBool("FilterOwnedOrFreeSkins", player, workshopIds);

        private static bool InvokeBool(string method, params object[] args)
        {
            EnsureBound();
            if (_apiType == null) return false;
            try
            {
                foreach (var mi in _apiType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (!string.Equals(mi.Name, method, StringComparison.Ordinal)) continue;
                    if (mi.GetParameters().Length != (args?.Length ?? 0)) continue;
                    if (mi.Invoke(null, args) is bool b) return b;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlayerSkins] PlayerDlcApi." + method + ": " + ex.Message);
            }
            return false;
        }

        private static void EnsureBound()
        {
            try
            {
                Type published = AppDomain.CurrentDomain.GetData("PlayerDlcApi_ApiType") as Type;
                if (published != null)
                {
                    _apiType = published;
                    return;
                }

                _apiType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        Type candidate = asm.GetType("PlayerDlcApiHarmony.PlayerDlcApiMod")
                                         ?? asm.GetType("PlayerDLCAPI.PlayerDLCAPIMod");
                        if (candidate == null) continue;

                        var instance = candidate.GetProperty(
                            "Instance",
                            BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                        if (instance == null) continue;

                        _apiType = candidate;
                        return;
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlayerSkins] PlayerDlcApi bind: " + ex.Message);
            }
        }
    }
}

namespace Oxide.Ext.Chaos.Data
{
    public class Datafile<T>
    {
        private readonly string _path;
        private readonly JsonConverter[] _converters;
        public T Data;

        public Datafile(string name, params JsonConverter[] converters)
        {
            _converters = converters;
            var root = PlayerSkinsHarmony.PlayerSkinsHost.Instance?.DataDirectory
                       ?? Path.Combine(".", "HarmonyData", "PlayerSkins");
            if (!Directory.Exists(root))
                Directory.CreateDirectory(root);

            name = (name ?? "data").Replace('\\', '/').Trim('/');
            if (name.StartsWith("PlayerSkins/", StringComparison.OrdinalIgnoreCase))
                name = name.Substring("PlayerSkins/".Length);
            if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 5);

            _path = Path.Combine(root, name + ".json");
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            Load();
        }

        public virtual void Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    var settings = new JsonSerializerSettings
                    {
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                        DefaultValueHandling = DefaultValueHandling.Populate
                    };
                    if (_converters != null)
                    {
                        foreach (var c in _converters)
                            settings.Converters.Add(c);
                    }
                    Data = JsonConvert.DeserializeObject<T>(File.ReadAllText(_path), settings);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlayerSkins] Datafile load " + Path.GetFileName(_path) + ": " + ex.Message);
            }
            finally
            {
                if (Data == null && typeof(T).IsClass)
                    Data = Activator.CreateInstance<T>();
            }
        }

        public virtual void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(_path, JsonConvert.SerializeObject(Data, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlayerSkins] Datafile save " + Path.GetFileName(_path) + ": " + ex.Message);
            }
        }
    }
}
