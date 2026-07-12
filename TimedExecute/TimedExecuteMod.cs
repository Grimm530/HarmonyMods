using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace TimedExecute
{
    /// <summary>
    /// Harmony port of Oxide TimedExecute 0.7.4 (PaiN &amp; misticos).
    /// Execute commands every (x) seconds / at real or in-game times.
    /// Config: HarmonyConfig/TimedExecute.json
    /// </summary>
    public class TimedExecuteMod : IHarmonyModHooks
    {
        public static TimedExecuteMod Plugin;

        public enum Types
        {
            RealTime,
            InGameTime,
            Repeater,
            TimerOnce
        }

        private static readonly string ConfigRelativePath = Path.Combine("HarmonyConfig", "TimedExecute.json");

        private ConfigData _config;
        private GameObject _runnerGo;
        private ModRunner _runner;
        private ConsoleSystem.Command _resetCmd;
        private ConsoleSystem.Command _resetCmdAlias;

        #region Classes

        public class ConfigData
        {
            [JsonProperty("EnableInGameTime-Timer")]
            public bool EnableInGameTimeTimer = true;

            [JsonProperty("EnableRealTime-Timer")]
            public bool EnableRealTimeTimer = true;

            [JsonProperty("EnableTimerOnce")]
            public bool EnableTimerOnce = true;

            [JsonProperty("EnableTimerRepeat")]
            public bool EnableTimerRepeat = true;

            /// <summary>Key = HH:mm (in-game), Value = command.</summary>
            [JsonProperty("InGameTime-Timer")]
            public Dictionary<string, string> InGameTimeTimer = new Dictionary<string, string>();

            /// <summary>Key = HH:mm:ss (real time), Value = command.</summary>
            [JsonProperty("RealTime-Timer")]
            public Dictionary<string, string> RealTimeTimer = new Dictionary<string, string>();

            /// <summary>Key = command, Value = delay seconds.</summary>
            [JsonProperty("TimerOnce")]
            public Dictionary<string, float> TimerOnce = new Dictionary<string, float>();

            /// <summary>Key = command, Value = interval seconds.</summary>
            [JsonProperty("TimerRepeat")]
            public Dictionary<string, float> TimerRepeat = new Dictionary<string, float>();
        }

        /// <summary>Stand-in for Oxide Timer.</summary>
        public sealed class Timer
        {
            internal bool Destroyed;
            internal Action Callback;

            public void Destroy() => Destroyed = true;
        }

        public sealed class ModRunner : MonoBehaviour
        {
        }

        class Timers
        {
            public static List<Timer> AllTimers = new List<Timer>();
            public static List<Timer> RepeatTimers = new List<Timer>();
            public static List<Timer> OnceTimers = new List<Timer>();
            public static Timer InGame;
            public static Timer Real;
            public static Timer Repeat;
            public static Timer Once;

            public static void ResetTimer(Types type)
            {
                switch (type)
                {
                    case Types.InGameTime:
                        RunTimer(Types.InGameTime);
                        break;

                    case Types.RealTime:
                        RunTimer(Types.RealTime);
                        break;

                    case Types.Repeater:
                        RunTimer(Types.Repeater);
                        break;

                    case Types.TimerOnce:
                        RunTimer(Types.TimerOnce);
                        break;
                }
            }

            static void DestroyList(List<Timer> list)
            {
                foreach (var t in list)
                    if (t != null)
                        t.Destroy();
                list.Clear();
            }

            public static void RunTimer(Types type)
            {
                float timeinterval = 4.5f;

                switch (type)
                {
                    case Types.InGameTime:
                        if (InGame != null) InGame.Destroy();
                        Plugin.Puts("The InGame timer has started");
                        AllTimers.Add(InGame = Plugin.timer.Repeat(timeinterval, 0, () =>
                        {
                            if (Plugin._config?.InGameTimeTimer == null) return;
                            string now = GetInGameShortTime();
                            foreach (var cmd in Plugin._config.InGameTimeTimer)
                                if (now == cmd.Key)
                                {
                                    Plugin.RunServerCommand(cmd.Value);
                                    Plugin.Puts(string.Format("ran CMD: {0}", cmd.Value));
                                }
                        }));
                        break;

                    case Types.RealTime:
                        if (Real != null) Real.Destroy();
                        Plugin.Puts("The RealTime timer has started");
                        AllTimers.Add(Real = Plugin.timer.Repeat(1, 0, () =>
                        {
                            if (Plugin._config?.RealTimeTimer == null) return;
                            string now = DateTime.Now.ToString("HH:mm:ss");
                            foreach (var cmd in Plugin._config.RealTimeTimer)
                                if (now == cmd.Key)
                                {
                                    Plugin.RunServerCommand(cmd.Value);
                                    Plugin.Puts(string.Format("ran CMD: {0}", cmd.Value));
                                }
                        }));
                        break;

                    case Types.Repeater:
                        DestroyList(RepeatTimers);
                        Plugin.Puts("The Repeat timer has started");
                        if (Plugin._config?.TimerRepeat == null) break;
                        foreach (var cmd in Plugin._config.TimerRepeat)
                        {
                            var command = cmd.Key;
                            Repeat = Plugin.timer.Repeat(Convert.ToSingle(cmd.Value), 0, () =>
                            {
                                Plugin.RunServerCommand(command);
                                Plugin.Puts(string.Format("ran CMD: {0}", command));
                            });
                            RepeatTimers.Add(Repeat);
                            AllTimers.Add(Repeat);
                        }
                        break;

                    case Types.TimerOnce:
                        DestroyList(OnceTimers);
                        Plugin.Puts("The Timer-Once timer has started");
                        if (Plugin._config?.TimerOnce == null) break;
                        foreach (var cmd in Plugin._config.TimerOnce)
                        {
                            var command = cmd.Key;
                            Once = Plugin.timer.Once(Convert.ToSingle(cmd.Value), () =>
                            {
                                Plugin.RunServerCommand(command);
                                Plugin.Puts(string.Format("ran CMD: {0}", command));
                            });
                            OnceTimers.Add(Once);
                            AllTimers.Add(Once);
                        }
                        break;
                }
            }

            public static void DestroyAll()
            {
                foreach (Timer tim in AllTimers)
                    if (tim != null)
                        tim.Destroy();
                AllTimers.Clear();
                RepeatTimers.Clear();
                OnceTimers.Clear();
                InGame = null;
                Real = null;
                Repeat = null;
                Once = null;
            }

            public static void RunAll()
            {
                if (Plugin._config == null) return;

                if (Plugin._config.EnableInGameTimeTimer)
                    RunTimer(Types.InGameTime);

                if (Plugin._config.EnableRealTimeTimer)
                    RunTimer(Types.RealTime);

                if (Plugin._config.EnableTimerRepeat)
                    RunTimer(Types.Repeater);

                if (Plugin._config.EnableTimerOnce)
                    RunTimer(Types.TimerOnce);
            }

            /// <summary>
            /// Matches Oxide covalence Server.Time short-time keys used in config defaults ("01:00", "12:00").
            /// </summary>
            static string GetInGameShortTime()
            {
                if (TOD_Sky.Instance == null)
                    return string.Empty;
                return TOD_Sky.Instance.Cycle.DateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Oxide-style timer.Once / timer.Repeat via MonoBehaviour coroutines.</summary>
        public class HarmonyTimerRunner
        {
            private readonly ModRunner _runner;
            private readonly List<Timer> _owned = new List<Timer>();

            public HarmonyTimerRunner(ModRunner runner)
            {
                _runner = runner;
            }

            public Timer Once(float seconds, Action callback)
            {
                if (callback == null) return new Timer();
                var t = new Timer { Callback = callback };
                _owned.Add(t);
                if (_runner != null)
                    _runner.StartCoroutine(WaitAndRun(seconds, t, callback));
                return t;
            }

            public Timer Repeat(float interval, int repeatCount, Action callback)
            {
                if (callback == null) return new Timer();
                var t = new Timer { Callback = callback };
                _owned.Add(t);
                if (_runner != null)
                    _runner.StartCoroutine(RepeatCoroutine(interval, repeatCount, t, callback));
                return t;
            }

            private IEnumerator WaitAndRun(float seconds, Timer timer, Action callback)
            {
                yield return new WaitForSeconds(seconds);
                if (timer.Destroyed) yield break;
                try { callback?.Invoke(); }
                catch (Exception ex) { Debug.LogWarning("[TimedExecute] Timer: " + ex.Message); }
            }

            private IEnumerator RepeatCoroutine(float interval, int repeatCount, Timer timer, Action callback)
            {
                int count = 0;
                while (!timer.Destroyed && (repeatCount <= 0 || count < repeatCount))
                {
                    yield return new WaitForSeconds(interval);
                    if (timer.Destroyed) break;
                    try { callback?.Invoke(); }
                    catch (Exception ex) { Debug.LogWarning("[TimedExecute] Timer: " + ex.Message); }
                    count++;
                }
            }
        }

        #endregion

        private HarmonyTimerRunner timer;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Plugin = this;
            LoadConfig();
            EnsureRunner();
            timer = new HarmonyTimerRunner(_runner);
            RegisterCommands();
            _runner.StartCoroutine(WaitForServerInitialized());
            Puts("Harmony mod loaded (Timed Execute 0.7.4). Config: HarmonyConfig/TimedExecute.json");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            Timers.DestroyAll();
            UnregisterCommands();
            if (_runnerGo != null)
            {
                UnityEngine.Object.Destroy(_runnerGo);
                _runnerGo = null;
                _runner = null;
            }
            timer = null;
            Plugin = null;
            Puts("Harmony mod unloaded.");
        }

        private IEnumerator WaitForServerInitialized()
        {
            while (ServerMgr.Instance == null)
                yield return null;
            OnServerInitialized();
        }

        void OnServerInitialized()
        {
            Timers.RunAll();
        }

        private void EnsureRunner()
        {
            if (_runner != null) return;
            _runnerGo = new GameObject("TimedExecute_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runnerGo);
            _runnerGo.hideFlags = HideFlags.HideAndDontSave;
            _runner = _runnerGo.AddComponent<ModRunner>();
        }

        #region Config

        private static string GetServerRoot()
        {
            var dp = Application.dataPath ?? "";
            return string.IsNullOrEmpty(dp) ? "." : Path.GetFullPath(Path.Combine(dp, ".."));
        }

        private string ConfigPath => Path.Combine(GetServerRoot(), ConfigRelativePath);

        private void LoadConfig()
        {
            var path = ConfigPath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(path))
            {
                try
                {
                    _config = JsonConvert.DeserializeObject<ConfigData>(File.ReadAllText(path));
                }
                catch (Exception ex)
                {
                    Puts("Failed to load config: " + ex.Message);
                }
            }

            if (_config == null)
            {
                Puts("Creating a new configuration file!");
                LoadDefaultConfig();
                SaveConfig();
            }
            else
            {
                if (_config.InGameTimeTimer == null) _config.InGameTimeTimer = new Dictionary<string, string>();
                if (_config.RealTimeTimer == null) _config.RealTimeTimer = new Dictionary<string, string>();
                if (_config.TimerOnce == null) _config.TimerOnce = new Dictionary<string, float>();
                if (_config.TimerRepeat == null) _config.TimerRepeat = new Dictionary<string, float>();
            }
        }

        private void LoadDefaultConfig()
        {
            _config = new ConfigData
            {
                EnableTimerRepeat = true,
                EnableTimerOnce = true,
                EnableRealTimeTimer = true,
                EnableInGameTimeTimer = true,
                TimerRepeat = new Dictionary<string, float>
                {
                    { "command1 arg", 300f },
                    { "command2 'msg'", 300f }
                },
                TimerOnce = new Dictionary<string, float>
                {
                    { "command1 'msg'", 60f },
                    { "command2 'msg'", 120f },
                    { "command3 arg", 180f },
                    { "reset.timeronce", 181f }
                },
                RealTimeTimer = new Dictionary<string, string>
                {
                    { "16:00:00", "command1 arg" },
                    { "16:30:00", "command2 arg" },
                    { "17:00:00", "command3 arg" },
                    { "18:00:00", "command4 arg" }
                },
                InGameTimeTimer = new Dictionary<string, string>
                {
                    { "01:00", "weather rain" },
                    { "12:00", "command 1" },
                    { "15:00", "command 2" }
                }
            };
        }

        private void SaveConfig()
        {
            try
            {
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(_config, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Puts("Could not write config: " + ex.Message);
            }
        }

        #endregion

        #region Commands / helpers

        private void RegisterCommands()
        {
            try
            {
                _resetCmd = new ConsoleSystem.Command
                {
                    Name = "reset.timeronce",
                    FullName = "global.reset.timeronce",
                    Variable = false,
                    ServerAdmin = true,
                    Call = arg => CmdReset(arg)
                };
                _resetCmdAlias = new ConsoleSystem.Command
                {
                    Name = "resettimeronce",
                    FullName = "global.resettimeronce",
                    Variable = false,
                    ServerAdmin = true,
                    Call = arg => CmdReset(arg)
                };

                ConsoleSystem.Index.Server.Dict["global.reset.timeronce"] = _resetCmd;
                ConsoleSystem.Index.Server.Dict["global.resettimeronce"] = _resetCmdAlias;
                if (ConsoleSystem.Index.Server.GlobalDict != null)
                {
                    ConsoleSystem.Index.Server.GlobalDict["reset.timeronce"] = _resetCmd;
                    ConsoleSystem.Index.Server.GlobalDict["resettimeronce"] = _resetCmdAlias;
                }
            }
            catch (Exception ex)
            {
                Puts("Command registration failed: " + ex.Message);
            }
        }

        private void UnregisterCommands()
        {
            try
            {
                ConsoleSystem.Index.Server.Dict?.Remove("global.reset.timeronce");
                ConsoleSystem.Index.Server.Dict?.Remove("global.resettimeronce");
                ConsoleSystem.Index.Server.GlobalDict?.Remove("reset.timeronce");
                ConsoleSystem.Index.Server.GlobalDict?.Remove("resettimeronce");
            }
            catch { }
            _resetCmd = null;
            _resetCmdAlias = null;
        }

        void CmdReset(ConsoleSystem.Arg arg)
        {
            if (arg != null && arg.IsAdmin)
                Timers.ResetTimer(Types.TimerOnce);
        }

        void RunServerCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return;
            try
            {
                ConsoleSystem.Run(ConsoleSystem.Option.Server, command);
            }
            catch (Exception ex)
            {
                Puts("Command failed (" + command + "): " + ex.Message);
            }
        }

        void Puts(string message)
        {
            Debug.Log("[TimedExecute] " + message);
        }

        #endregion
    }
}
