using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Facepunch;
using Network;
using Newtonsoft.Json;
using ProtoBuf;
using UnityEngine;

namespace RadioHarmony
{
    public interface IRadio
    {
        bool CanTransmitRadio();
        bool CanReceiveRadioCommunication(BasePlayer player, IRadio transmittingRadio);
        void ReceiveRadioCommunication(byte[] data);
        int GetRadioFrequency();
    }

    public class RadioConfig
    {
        public int GlobalRadioPhoneNumber { get; set; } = 99994444;
        public string GlobalRadioPhoneName { get; set; } = "Global Radio";
        public VehicleRadioConfig VehicleRadio { get; set; } = new VehicleRadioConfig();
    }

    public class VehicleRadioConfig
    {
        [JsonProperty("Permission Required")]
        public string Permission = "vehicleradio.use";

        [JsonProperty("Auto Install On Spawn")]
        public bool AutoInstallOnSpawn = true;

        [JsonProperty("Vehicle Settings")]
        public VehicleSettings Vehicles = new VehicleSettings();

        [JsonProperty("Commands")]
        public CommandConfig Commands = new CommandConfig();

        [JsonProperty("Messages")]
        public MessageConfig Messages = new MessageConfig();
    }

    public class VehicleSettings
    {
        [JsonProperty("Allow Minicopters")] public bool AllowMinicopters = true;
        [JsonProperty("Allow Attack Helicopters")] public bool AllowAttackHelicopters = true;
        [JsonProperty("Allow Tugboats")] public bool AllowTugboats = true;
        [JsonProperty("Minicopter Radio Position")]
        public RadioPosition MinicopterPosition = new RadioPosition { X = 0f, Y = 0.6f, Z = 1.3f, RotationY = 180f };
        [JsonProperty("Attack Helicopter Radio Position")]
        public RadioPosition AttackHelicopterPosition = new RadioPosition { X = 0f, Y = 1.0f, Z = 2.0f, RotationY = 180f };
        [JsonProperty("Tugboat Radio Position")]
        public RadioPosition TugboatPosition = new RadioPosition { X = 0f, Y = 0.5f, Z = 2.0f, RotationY = 180f };
        [JsonProperty("Use Absolute Position for Tugboat")] public bool UseTugboatAbsolutePosition = false;
        [JsonProperty("Tugboat Absolute Position")]
        public AbsolutePosition TugboatAbsolutePosition = new AbsolutePosition();
    }

    public class RadioPosition
    {
        [JsonProperty("X Position")] public float X;
        [JsonProperty("Y Position")] public float Y = 0.6f;
        [JsonProperty("Z Position")] public float Z = 1.3f;
        [JsonProperty("Rotation Y")] public float RotationY = 180f;
    }

    public class AbsolutePosition
    {
        [JsonProperty("Offset Forward")] public float OffsetForward = 2f;
        [JsonProperty("Offset Up")] public float OffsetUp = 2f;
        [JsonProperty("Offset Right")] public float OffsetRight;
    }

    public class CommandConfig
    {
        [JsonProperty("Install Radio Command")] public string InstallCommand = "radio";
        [JsonProperty("Remove Radio Command")] public string RemoveCommand = "rradio";
    }

    public class MessageConfig
    {
        [JsonProperty("No Permission")] public string NoPermission = "You don't have permission to use this command!";
        [JsonProperty("Not In Vehicle")] public string NotInVehicle = "You need to be mounted in a vehicle to use this command!";
        [JsonProperty("Unsupported Vehicle")] public string UnsupportedVehicle = "Radios can only be installed in helicopters and tugboats!";
        [JsonProperty("Already Has Radio")] public string AlreadyHasRadio = "This vehicle already has a radio installed!";
        [JsonProperty("Radio Installed")] public string RadioInstalled = "Radio installed successfully!";
        [JsonProperty("No Radio")] public string NoRadio = "This vehicle doesn't have a radio installed!";
        [JsonProperty("Radio Removed")] public string RadioRemoved = "Radio removed successfully!";
    }

    public class RadioMod : IHarmonyModHooks
    {
        public const string GlobalPhonePermission = "Radio.GiveGlobalPhone";
        public const string AppDomainApiKey = "Radio_ApiType";
        const string MobilePhoneItem = "mobilephone";

        public static RadioMod Instance { get; private set; }
        public RadioConfig Config { get; private set; }
        public VehicleRadioService Vehicles { get; private set; }

        readonly Dictionary<ulong, IRadio> _playerRadios = new Dictionary<ulong, IRadio>();
        readonly Dictionary<ulong, PhoneController> _playerPhones = new Dictionary<ulong, PhoneController>();
        readonly HashSet<string> _chatCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly List<ConsoleSystem.Command> _commands = new List<ConsoleSystem.Command>();
        Action _permissionsReady;
        GameObject _runner;
        Telephone _globalPhone;
        string _root;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            _root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            LoadConfig();
            Vehicles = new VehicleRadioService(this, _root);
            Vehicles.LoadData();

            _permissionsReady = RegisterPermissions;
            PermissionsBridge.RegisterReadyCallback(_permissionsReady);

            RefreshChatCommands();
            RegisterConsoleAliases();

            if (_runner == null)
            {
                _runner = new GameObject("Radio_Runner");
                UnityEngine.Object.DontDestroyOnLoad(_runner);
                _runner.AddComponent<RadioRunner>().Begin(this);
            }

            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(RadioMod)); }
            catch { }

            Debug.Log("[Radio] Harmony mod loaded. Chat: /GiveGlobalPhone /radio /rradio. Config: HarmonyConfig/Radio.json");
        }

        public void OnServerInitialized()
        {
            RegisterPermissions();
            RegisterRadioPhone();
            Vehicles.ValidateAndRestore();
            Debug.Log("[Radio] Server initialized.");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            if (_permissionsReady != null)
                PermissionsBridge.UnregisterReadyCallback(_permissionsReady);
            DestroyRadioPhone();
            Vehicles?.SaveData();
            UnregisterConsole();
            try { AppDomain.CurrentDomain.SetData(AppDomainApiKey, null); }
            catch { }
            if (_runner != null)
            {
                UnityEngine.Object.Destroy(_runner);
                _runner = null;
            }
            Instance = null;
            Debug.Log("[Radio] Unloaded.");
        }

        void RegisterPermissions()
        {
            PermissionsBridge.RegisterPermission(GlobalPhonePermission);
            if (!string.IsNullOrEmpty(Config?.VehicleRadio?.Permission))
                PermissionsBridge.RegisterPermission(Config.VehicleRadio.Permission);
        }

        void RefreshChatCommands()
        {
            _chatCommands.Clear();
            _chatCommands.Add("giveglobalphone");
            var vr = Config?.VehicleRadio?.Commands;
            if (vr != null)
            {
                if (!string.IsNullOrWhiteSpace(vr.InstallCommand)) _chatCommands.Add(vr.InstallCommand);
                if (!string.IsNullOrWhiteSpace(vr.RemoveCommand)) _chatCommands.Add(vr.RemoveCommand);
            }
        }

        public bool TryHandleChat(BasePlayer player, string command, string[] args)
        {
            if (player == null || string.IsNullOrEmpty(command) || !_chatCommands.Contains(command))
                return false;
            if (command.Equals("giveglobalphone", StringComparison.OrdinalIgnoreCase))
            {
                GiveGlobalPhone(player);
                return true;
            }
            var vr = Config?.VehicleRadio?.Commands;
            if (vr != null && command.Equals(vr.InstallCommand, StringComparison.OrdinalIgnoreCase))
            {
                Vehicles.CmdInstall(player);
                return true;
            }
            if (vr != null && command.Equals(vr.RemoveCommand, StringComparison.OrdinalIgnoreCase))
            {
                Vehicles.CmdRemove(player);
                return true;
            }
            return false;
        }

        public void OnPlayerVoice(BasePlayer player, byte[] data)
        {
            if (player == null || data == null || player.net == null) return;

            IRadio playerRadio = null;
            ulong id = player.net.ID.Value;
            if (!_playerPhones.ContainsKey(id))
            {
                if (!_playerRadios.TryGetValue(id, out playerRadio))
                    return;
                if (!playerRadio.CanTransmitRadio())
                    return;
            }

            foreach (var radio in _playerRadios.Values)
            {
                if (!radio.CanReceiveRadioCommunication(player, playerRadio))
                    continue;
                radio.ReceiveRadioCommunication(data);
            }

            foreach (var phone in _playerPhones.Values)
            {
                if (phone?.currentPlayer == null || phone.currentPlayer.net == null) continue;
                if (phone.currentPlayer.net.ID.Value == id) continue;
                var target = RpcTarget.SendInfo("OnReceivedVoice", new SendInfo(phone.currentPlayer.Connection)
                {
                    priority = Priority.Immediate
                });
                phone.ParentEntity.ClientRPC(target, data.Length, data);
            }
        }

        public static void RegisterRadio(BasePlayer player, IRadio radio)
        {
            if (player == null || player.IsNpc || Instance == null) return;
            Instance._playerRadios[player.net.ID.Value] = radio;
        }

        /// <summary>
        /// Accepts RadioHarmony.IRadio or a foreign IRadio (KaruzaVehicles) via adapter.
        /// </summary>
        public static void RegisterRadio(BasePlayer player, object radio)
        {
            if (radio is IRadio typed)
            {
                RegisterRadio(player, typed);
                return;
            }
            if (radio == null) return;
            RegisterRadio(player, new ForeignRadioAdapter(radio));
        }

        sealed class ForeignRadioAdapter : IRadio
        {
            readonly object _inner;
            readonly MethodInfo _canTx;
            readonly MethodInfo _canRx;
            readonly MethodInfo _recv;
            readonly MethodInfo _freq;

            public ForeignRadioAdapter(object inner)
            {
                _inner = inner;
                Type t = inner.GetType();
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public;
                _canTx = t.GetMethod("CanTransmitRadio", bf, null, Type.EmptyTypes, null);
                _canRx = t.GetMethod("CanReceiveRadioCommunication", bf);
                _recv = t.GetMethod("ReceiveRadioCommunication", bf, null, new[] { typeof(byte[]) }, null);
                _freq = t.GetMethod("GetRadioFrequency", bf, null, Type.EmptyTypes, null);
            }

            public bool CanTransmitRadio()
            {
                try { return _canTx?.Invoke(_inner, null) is bool b && b; }
                catch { return false; }
            }

            public bool CanReceiveRadioCommunication(BasePlayer player, IRadio transmittingRadio)
            {
                try
                {
                    if (_canRx == null) return true;
                    object r = _canRx.Invoke(_inner, new object[] { player, transmittingRadio });
                    return r is bool b && b;
                }
                catch { return true; }
            }

            public void ReceiveRadioCommunication(byte[] data)
            {
                try { _recv?.Invoke(_inner, new object[] { data }); }
                catch { }
            }

            public int GetRadioFrequency()
            {
                try { return _freq?.Invoke(_inner, null) is int n ? n : 0; }
                catch { return 0; }
            }
        }

        public static void RemoveRadio(BasePlayer player)
        {
            if (Instance == null || player == null || player.IsNpc || player.net == null) return;
            Instance._playerRadios.Remove(player.net.ID.Value);
        }

        public bool TryHandleGlobalRadioDial(PhoneController callerPhone, PhoneController receiverPhone, BasePlayer player)
        {
            if (callerPhone == null || receiverPhone == null || Config == null) return false;
            if (receiverPhone.PhoneNumber != Config.GlobalRadioPhoneNumber) return false;

            callerPhone.ServerHangUp();
            callerPhone.SetPhoneStateWithPlayer(Telephone.CallState.InProcess);
            player?.SetActiveTelephone(callerPhone);
            if (player?.net != null)
                _playerPhones[player.net.ID.Value] = callerPhone;
            return true;
        }

        public void OnPhoneDialFailed(PhoneController phone, Telephone.DialFailReason reason, BasePlayer player)
        {
            if (player == null) player = phone?.currentPlayer;
            if (player?.net == null) return;
            _playerPhones.Remove(player.net.ID.Value);
        }

        void RegisterRadioPhone()
        {
            if (Config == null || Config.GlobalRadioPhoneNumber <= 0) return;
            try
            {
                var telephone = GameManager.server.CreateEntity("assets/bundled/prefabs/autospawn/phonebooth/phonebooth.static.prefab") as Telephone;
                if (telephone == null) return;
                telephone.Spawn();
                TelephoneManager.DeregisterTelephone(telephone.Controller);
                telephone.Controller.PhoneNumber = Config.GlobalRadioPhoneNumber;
                telephone.Controller.PhoneName = Config.GlobalRadioPhoneName;
                TelephoneManager.RegisterTelephone(telephone.Controller);
                _globalPhone = telephone;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Radio] RegisterRadioPhone: " + ex.Message);
            }
        }

        void DestroyRadioPhone()
        {
            try
            {
                var phone = TelephoneManager.GetTelephone(Config.GlobalRadioPhoneNumber);
                if (phone == null) return;
                TelephoneManager.DeregisterTelephone(phone);
                if (phone.ParentEntity != null && !phone.ParentEntity.IsDestroyed)
                    phone.ParentEntity.Kill();
            }
            catch { }
            _globalPhone = null;
        }

        void GiveGlobalPhone(BasePlayer player)
        {
            if (player == null) return;
            if (!player.IsAdmin && !PermissionsBridge.UserHasPermission(player.UserIDString, GlobalPhonePermission))
                return;

            var def = ItemManager.FindItemDefinition(MobilePhoneItem);
            if (def == null) return;
            Item item = ItemManager.Create(def);
            if (item == null) return;
            item.amount = 1;
            item.name = "Global Radio Phone";
            if (!player.inventory.GiveItem(item, player.inventory.containerBelt))
            {
                item.Remove();
                return;
            }

            var phone = item.GetHeldEntity() as MobilePhone;
            var controller = phone?.Controller;
            if (controller == null) return;
            if (controller.savedNumbers == null)
                controller.savedNumbers = Pool.Get<PhoneDirectory>();
            if (controller.savedNumbers.entries == null)
                controller.savedNumbers.entries = Pool.Get<List<PhoneDirectory.DirectoryEntry>>();
            if (controller.IsSavedContactValid(Config.GlobalRadioPhoneName, Config.GlobalRadioPhoneNumber))
            {
                var directoryEntry = Pool.Get<PhoneDirectory.DirectoryEntry>();
                directoryEntry.phoneName = Config.GlobalRadioPhoneName;
                directoryEntry.phoneNumber = Config.GlobalRadioPhoneNumber;
                directoryEntry.ShouldPool = false;
                controller.savedNumbers.entries.Add(directoryEntry);
            }
            controller.savedNumbers.ShouldPool = false;
            controller.baseEntity.SendNetworkUpdate();
        }

        void LoadConfig()
        {
            string path = Path.Combine(_root, "HarmonyConfig", "Radio.json");
            try
            {
                if (File.Exists(path))
                    Config = JsonConvert.DeserializeObject<RadioConfig>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Radio] Config load failed: " + ex.Message);
            }
            if (Config == null) Config = new RadioConfig();
            if (Config.VehicleRadio == null) Config.VehicleRadio = new VehicleRadioConfig();
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonConvert.SerializeObject(Config, Formatting.Indented));
            }
            catch { }
        }

        void RegisterConsoleAliases()
        {
            foreach (var name in _chatCommands)
            {
                string local = name;
                try
                {
                    var cmd = new ConsoleSystem.Command
                    {
                        Name = local,
                        FullName = "global." + local,
                        Variable = false,
                        ServerAdmin = false,
                        ServerUser = true,
                        AllowRunFromServer = true,
                        Call = a =>
                        {
                            var player = a?.Connection?.player as BasePlayer;
                            if (player == null) return;
                            string[] args = Array.Empty<string>();
                            if (a.Args != null && a.Args.Length > 0)
                            {
                                args = new string[a.Args.Length];
                                for (int i = 0; i < a.Args.Length; i++)
                                    args[i] = a.Args[i].ToString();
                            }
                            TryHandleChat(player, local, args);
                        }
                    };
                    _commands.Add(cmd);
                    ConsoleSystem.Index.Server.Dict[cmd.FullName] = cmd;
                    ConsoleSystem.Index.Server.GlobalDict[cmd.Name] = cmd;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Radio] console alias " + local + ": " + ex.Message);
                }
            }
        }

        void UnregisterConsole()
        {
            try
            {
                var dict = ConsoleSystem.Index.Server.Dict;
                var globalDict = ConsoleSystem.Index.Server.GlobalDict;
                foreach (var cmd in _commands)
                {
                    dict?.Remove(cmd.FullName);
                    globalDict?.Remove(cmd.Name);
                }
            }
            catch { }
            _commands.Clear();
        }
    }

    internal sealed class RadioRunner : MonoBehaviour
    {
        RadioMod _mod;
        bool _started;
        public void Begin(RadioMod mod)
        {
            _mod = mod;
            if (_started) return;
            _started = true;
            StartCoroutine(Wait());
        }
        IEnumerator Wait()
        {
            while (ServerMgr.Instance == null) yield return null;
            yield return new WaitForSeconds(1f);
            _mod?.OnServerInitialized();
        }
    }
}
