using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Network;
using Newtonsoft.Json;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LoadingMessages
{
    /// <summary>
    /// Harmony port of Oxide LoadingMessages 1.1.3 (CosaNostra/Def/klauz24).
    /// Shows custom texts on the loading screen. Config: HarmonyConfig/LoadingMessages.json
    /// </summary>
    public class LoadingMessagesMod : IHarmonyModHooks
    {
        public static LoadingMessagesMod Instance { get; private set; }

        private readonly Dictionary<ulong, Connection> _clients = new Dictionary<ulong, Connection>();
        private readonly List<ulong> _disconnectedClients = new List<ulong>();

        #region Variables

        private static MsgConfig _config;
        private TimerHandle _timer;
        private List<Connection> _queueConnections;
        private static MsgCollection _messages, _messagesQueue;
        private bool _hooksActive = true;
        private GameObject _runnerGo;
        private ModRunner _runner;

        private static FieldInfo _nextMessageTimeField;
        private static FieldInfo _queueField;

        #endregion

        #region Classes

        private class MsgCollection
        {
            public List<MsgEntry> MessagesList;
            public MsgEntry CurrentMessage;
            private int _messageIndex;

            public void AdvanceMessage()
            {
                if (_config.EnableCyclicity)
                {
                    if (_config.EnableRandomCyclicity)
                    {
                        CurrentMessage = PickRandom(MessagesList);
                    }
                    else
                    {
                        CurrentMessage = MessagesList[_messageIndex++];
                        if (_messageIndex >= MessagesList.Count)
                            _messageIndex = 0;
                    }
                }
            }

            public void SelectFirst() => CurrentMessage = MessagesList.First();
        }

        /// <summary>Stand-in for Oxide Timer from timer.Every.</summary>
        private sealed class TimerHandle
        {
            private readonly ModRunner _runner;
            private readonly Action _callback;
            private readonly float _interval;
            private bool _destroyed;

            public TimerHandle(ModRunner runner, float interval, Action callback)
            {
                _runner = runner;
                _interval = interval;
                _callback = callback;
                _runner.StartCoroutine(Loop());
            }

            private IEnumerator Loop()
            {
                while (!_destroyed)
                {
                    yield return new WaitForSeconds(_interval);
                    if (_destroyed) yield break;
                    try { _callback?.Invoke(); }
                    catch (Exception ex) { Debug.LogError("[LoadingMessages] timer: " + ex); }
                }
            }

            public void Destroy() => _destroyed = true;
        }

        private sealed class ModRunner : MonoBehaviour
        {
        }

        #endregion

        #region Config

        private class MsgConfig
        {
            [JsonProperty("Cycle Messages Every ~N Seconds")]
            public float CyclicityFreq;
            [JsonProperty("Enable Messages Cyclicity")]
            public bool EnableCyclicity;
            [JsonProperty("Use Random Cyclicity (Instead of sequential)")]
            public bool EnableRandomCyclicity;
            [JsonProperty("Messages")]
            public List<MsgEntry> Msgs;
            [JsonProperty("Enable Queue Messages")]
            public bool EnableQueueMessages;
            [JsonProperty("Queue Messages")]
            public List<MsgEntry> QueueMsgs;
            [JsonProperty("Last Message (When entering game)")]
            public MsgEntry LastMessage;
        }

        private class MsgEntry
        {
            [JsonProperty("Icon name")]
            public string IconName;
            [JsonProperty("Message")]
            public string Message;
        }

        private static string GetServerRoot() =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static string GetConfigPath() =>
            Path.Combine(GetServerRoot(), "HarmonyConfig", "LoadingMessages.json");

        private void LoadConfig()
        {
            string path = GetConfigPath();
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(path))
            {
                try
                {
                    _config = JsonConvert.DeserializeObject<MsgConfig>(File.ReadAllText(path));
                }
                catch (Exception ex)
                {
                    Debug.LogError("[LoadingMessages] Failed to read config: " + ex.Message);
                    _config = null;
                }
            }
            else
            {
                string oxideCfg = Path.Combine(GetServerRoot(), "oxide", "config", "LoadingMessages.json");
                if (File.Exists(oxideCfg))
                {
                    try
                    {
                        _config = JsonConvert.DeserializeObject<MsgConfig>(File.ReadAllText(oxideCfg));
                        if (_config != null)
                        {
                            SaveConfig();
                            Debug.Log("[LoadingMessages] Migrated config from oxide/config/LoadingMessages.json");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[LoadingMessages] Oxide config migrate failed: " + ex.Message);
                    }
                }
            }

            if (_config == null)
            {
                LoadDefaultConfig();
                SaveConfig();
            }

            _messages = new MsgCollection { MessagesList = _config.Msgs };
            _messagesQueue = new MsgCollection { MessagesList = _config.QueueMsgs };
            if (_config.EnableQueueMessages || _config.QueueMsgs != null)
                return;
            _config.QueueMsgs = new List<MsgEntry>
            {
                new MsgEntry
                {
                    IconName = "Bolt",
                    Message = "<color=#add8e6>You're in queue...",
                }
            };
            SaveConfig();
            Debug.LogWarning("[LoadingMessages] Detected probably outdated config. New entries added. Check your config.");
        }

        private void LoadDefaultConfig()
        {
            _config = new MsgConfig
            {
                EnableCyclicity = true,
                EnableRandomCyclicity = false,
                CyclicityFreq = 5.0f,
                Msgs = new List<MsgEntry>
                {
                    new MsgEntry
                    {
                        IconName = "Bolt",
                        Message = "<color=#add8e6>{PLAYERNAME}, welcome to our server!",
                    },
                    new MsgEntry
                    {
                        IconName = "Bolt",
                        Message = "<color=#add8e6>Enjoy your stay.",
                    }
                },
                EnableQueueMessages = false,
                QueueMsgs = new List<MsgEntry>
                {
                    new MsgEntry
                    {
                        IconName = "Bolt",
                        Message = "<color=#add8e6>You're in queue...",
                    }
                },
                LastMessage = new MsgEntry
                {
                    IconName = "Bolt",
                    Message = "<color=#008000>Entering game..."
                }
            };
        }

        private void SaveConfig()
        {
            try
            {
                string path = GetConfigPath();
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonConvert.SerializeObject(_config, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogError("[LoadingMessages] Failed to save config: " + ex.Message);
            }
        }

        #endregion

        #region Lifecycle (IHarmonyModHooks)

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            EnsureReflection();
            LoadConfig();
            EnsureRunner();
            RunLoadedChecks();
            _runner.StartCoroutine(WaitForServerInitialized());
            Debug.Log("[LoadingMessages] Harmony mod loaded. Config: " + GetConfigPath());
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            Unload();
            if (_runnerGo != null)
            {
                UnityEngine.Object.Destroy(_runnerGo);
                _runnerGo = null;
                _runner = null;
            }
            Instance = null;
            Debug.Log("[LoadingMessages] Harmony mod unloaded.");
        }

        private void EnsureRunner()
        {
            if (_runner != null) return;
            _runnerGo = new GameObject("LoadingMessages_Runner");
            UnityEngine.Object.DontDestroyOnLoad(_runnerGo);
            _runnerGo.hideFlags = HideFlags.HideAndDontSave;
            _runner = _runnerGo.AddComponent<ModRunner>();
        }

        private static void EnsureReflection()
        {
            if (_nextMessageTimeField == null)
                _nextMessageTimeField = typeof(ConnectionQueue).GetField("nextMessageTime", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_queueField == null)
                _queueField = typeof(ConnectionQueue).GetField("queue", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private IEnumerator WaitForServerInitialized()
        {
            while (ServerMgr.Instance == null)
                yield return null;
            OnServerInitialized();
        }

        #endregion

        #region Hooks (invoked by Harmony patches)

        private void Unload()
        {
            _messages = null;
            _messagesQueue = null;
            _timer?.Destroy();
            _timer = null;
            _clients.Clear();
            if (_config != null && _config.EnableQueueMessages)
                SetNextMessageTime(0f);
        }

        private void RunLoadedChecks()
        {
            if (_config?.Msgs == null || _config.Msgs.Count == 0)
            {
                _hooksActive = false;
                Debug.LogWarning("[LoadingMessages] No loading messages defined! Check your config.");
                return;
            }
            if (_config.EnableCyclicity && _config.Msgs.Count <= 1)
            {
                _config.EnableCyclicity = false;
                Debug.LogWarning("[LoadingMessages] You have message cyclicity enabled, but only 1 message is defined. Check your config.");
            }

            if (_config.EnableQueueMessages && _config.QueueMsgs == null || _config.QueueMsgs.Count == 0)
            {
                _config.EnableQueueMessages = false;
                Debug.LogWarning("[LoadingMessages] You have queue messages enabled, but no queue messages is defined. Check your config.");
            }

            _messages.SelectFirst();
            if (_config.EnableQueueMessages)
                _messagesQueue.SelectFirst();
        }

        private void OnServerInitialized()
        {
            try
            {
                if (_queueField != null && ServerMgr.Instance?.connectionQueue != null)
                    _queueConnections = _queueField.GetValue(ServerMgr.Instance.connectionQueue) as List<Connection>;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LoadingMessages] OnServerInitialized queue cache: " + ex.Message);
            }
        }

        internal void OnUserApprove(Connection connection)
        {
            if (!_hooksActive || connection == null || Instance == null) return;
            // Postfix still runs on early Reject() returns; only track connections that entered auth.
            if (!connection.active || connection.rejected) return;
            if (!ConnectionAuth.m_AuthConnection.Contains(connection)) return;
            _clients[connection.userid] = connection;
            if (_timer == null)
                _timer = new TimerHandle(_runner, _config.CyclicityFreq, HandleClients);
            SendPacket(connection, GetCurrentMessage());
        }

        internal void OnPlayerConnected(BasePlayer player)
        {
            if (!_hooksActive || player == null || Instance == null) return;
            ulong id = (ulong)player.userID;
            _clients.Remove(id);
            SendPacket(player.Connection, GetLastMessage() ?? GetCurrentMessage());
        }

        #endregion

        #region Logic

        private void HandleClients()
        {
            if (_clients.Count == 0)
            {
                _timer?.Destroy();
                _timer = null;
                return;
            }
            UpdateCurrentMessages();
            if (_config.EnableQueueMessages && ServerMgr.Instance != null && ServerMgr.Instance.connectionQueue.Queued > 0)
                SuppressDefaultQueueMessage();
            foreach (var client in _clients.Values)
            {
                if (!client.active)
                {
                    _disconnectedClients.Add(client.userid);
                    continue;
                }

                if (client.state == Connection.State.InQueue)
                {
                    if (!_config.EnableQueueMessages)
                        continue;
                    SendPacket(client, GetCurrentQueueMessage());
                    continue;
                }
                SendPacket(client, GetCurrentMessage());
            }

            if (_disconnectedClients.Count == 0)
                return;
            _disconnectedClients.ForEach(uid => _clients.Remove(uid));
            _disconnectedClients.Clear();
        }

        private void SendPacket(Connection conn, MsgEntry entry)
        {
            if (entry == null) return;
            var icon = entry.IconName;
            var message = entry.Message;
            if (IsValidStr(icon) && IsValidStr(message))
            {
                if (conn != null && Net.sv != null)
                {
                    var net = Net.sv.StartWrite();
                    net.PacketID(Message.Type.Message);
                    net.String(icon);
                    net.String(message.Replace("{PLAYERNAME}", conn.username).Replace("</color>", ""));
                    net.Send(new SendInfo(conn));
                }
            }
            else
            {
                Debug.LogError($"[LoadingMessages] Invalid MsgEntry!\nIconName: {icon}\nMessage: {message}");
            }
        }

        #endregion

        #region Utils

        private static bool IsValidStr(string str) => str != null && str.Length > 0;
        private static T PickRandom<T>(IReadOnlyList<T> list) => list[Random.Range(0, list.Count - 1)];
        private static MsgEntry GetCurrentMessage() => _messages?.CurrentMessage;
        private static MsgEntry GetLastMessage() => _config?.LastMessage;
        private static MsgEntry GetCurrentQueueMessage() => _messagesQueue?.CurrentMessage;
        private static MsgCollection GetMessagesCollection() => _messages;
        private static MsgCollection GetQueueMessagesCollection() => _messagesQueue;
        private static void UpdateCurrentMessages()
        {
            GetMessagesCollection()?.AdvanceMessage();
            GetQueueMessagesCollection()?.AdvanceMessage();
        }

        // Kept for parity with the Oxide plugin (unused by original logic).
        private int GetQueuePosition(Connection con) => _queueConnections != null ? _queueConnections.IndexOf(con) : -1;

        private static void SuppressDefaultQueueMessage() => SetNextMessageTime(float.MaxValue);

        private static void SetNextMessageTime(float value)
        {
            try
            {
                EnsureReflection();
                var cq = ServerMgr.Instance?.connectionQueue;
                if (cq == null || _nextMessageTimeField == null) return;
                _nextMessageTimeField.SetValue(cq, value);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LoadingMessages] SetNextMessageTime: " + ex.Message);
            }
        }

        #endregion
    }
}
