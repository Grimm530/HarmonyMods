using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Oxide.Game.Rust.Cui;
using Rust;
using Rust.Modular;
using UnityEngine;
using Color = UnityEngine.Color;

namespace RustLeagueHarmony
{
    public partial class RustLeaguePlugin
    {
        public static RustLeaguePlugin Instance { get; private set; }

        internal ConfigData configData;
        internal readonly TimerLib timer = new TimerLib();
        internal RustLeagueGrid Grid;

        internal bool testing;
        internal bool soloTest;
        internal golePostRed zoneRed;
        internal golePostBlue zoneBlue;
        internal rustLeague ballMono;
        internal ArenaBounds arenaBounds;
        private BaseEntity ball;
        private MapMarkerGenericRadius _mapMarker;
        private VendingMachineMapMarker _shopMarker;

        private bool eventOpen;
        private bool eventRunning;
        private int carCount;
        public bool eventRoundOver;
        public int inEventRound;
        public string finalScore = "";
        private DateTime _cycleOpenedAt = DateTime.MinValue;
        internal Vector3 arenaOrigin;
        internal float arenaYaw;

        public Dictionary<ulong, bool> eventPlayer = new Dictionary<ulong, bool>();
        public Dictionary<ulong, string> RuningEventPlayer = new Dictionary<ulong, string>();
        public Dictionary<ulong, Vector3> EventPlayerLastPos = new Dictionary<ulong, Vector3>();
        public Dictionary<ulong, bool> paiedPlayers = new Dictionary<ulong, bool>();
        public List<ulong> eventEntitys = new List<ulong>();
        public List<ulong> eventPlayerList = new List<ulong>();
        public Dictionary<ulong, carLoc> RedEventCars = new Dictionary<ulong, carLoc>();
        public Dictionary<ulong, carLoc> BlueEventCars = new Dictionary<ulong, carLoc>();
        internal readonly List<rustLeagueCar> LiveCars = new List<rustLeagueCar>();
        public List<ulong> currentPlayers = new List<ulong>();
        readonly Dictionary<int, Timer> EventTimers = new Dictionary<int, Timer>();

        public class carLoc
        {
            public Vector3 position;
            public Quaternion rotation;
            public ulong playerID;
            public string name = "";
        }

        public BaseNetworkable FindEntity(ulong netID)
        {
            return BaseNetworkable.serverEntities.Find(new NetworkableId(netID));
        }

        internal string Lang(string key)
        {
            return RustLeagueHost.Instance?.Lang.GetMessage(key) ?? key;
        }

        internal void Reply(BasePlayer player, string key, params object[] args)
        {
            if (player == null) return;
            string msg = Lang(key);
            if (args != null && args.Length > 0)
                msg = string.Format(msg, args);
            player.ChatMessage(msg);
        }

        internal void Broadcast(string key, params object[] args)
        {
            string msg = Lang(key);
            if (args != null && args.Length > 0)
                msg = string.Format(msg, args);
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player != null && player.IsConnected)
                    player.ChatMessage(msg);
            }
        }

        public void HarmonyInit()
        {
            Instance = this;
            LoadDefaultMessages();
            RustLeagueHost.Instance?.ReloadLanguage();
            LoadOrCreateConfig();
            testing = configData.settings.testing;
            Grid = new RustLeagueGrid(this);
            if (ServerMgr.Instance != null)
                ArenaCatalog.Load(this);
        }

        public void RegisterPermissions()
        {
            PermissionsBridge.RegisterPermission(configData.settings.PermissionAdmin);
        }

        public void HarmonyServerInitialized()
        {
            if (!ArenaCatalog.Ready)
                ArenaCatalog.Load(this);
            DestroyLeftovers();
            PurgeDestroyedFromSaveList();
            if (configData.settings.autoEvents)
                ScheduleNextCycle(2f);
        }

        public void HarmonyUnload()
        {
            Grid?.StopScan();
            closeEvent();
            timer.DestroyAll();
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null) continue;
                CuiHelper.DestroyUi(player, "theUIleagueMenu");
                CuiHelper.DestroyUi(player, "waitingPlay");
                CuiHelper.DestroyUi(player, "TeamHudBlocknameTimer");
            }
            Instance = null;
        }

        private void DestroyLeftovers()
        {
            foreach (var c in UnityEngine.Object.FindObjectsOfType<golePostRed>())
                UnityEngine.Object.Destroy(c);
            foreach (var c in UnityEngine.Object.FindObjectsOfType<golePostBlue>())
                UnityEngine.Object.Destroy(c);
            foreach (var c in UnityEngine.Object.FindObjectsOfType<rustLeague>())
                UnityEngine.Object.Destroy(c);
            foreach (var c in UnityEngine.Object.FindObjectsOfType<ArenaBounds>())
                UnityEngine.Object.Destroy(c);
        }

        #region Config

        public class ConfigData
        {
            [JsonProperty("Settings")]
            public Settings settings { get; set; }

            [JsonProperty("Ball Settings")]
            public ballSettings BallSettings { get; set; }

            [JsonProperty("Item settings")]
            public itemSettings ItemSettings { get; set; }

            [JsonProperty("Car information")]
            public carSettings CarSettings { get; set; }

            [JsonProperty("Event Location information")]
            public EventSettings eventSettings { get; set; }

            [JsonProperty("Grid Scan")]
            public GridSettings Grid { get; set; }

            public VersionNumber Version { get; set; }
        }

        public class VersionNumber
        {
            public int Major { get; set; }
            public int Minor { get; set; }
            public int Patch { get; set; }
        }

        public class carSettings
        {
            public int totalRockets { get; set; }
            public string carFrame { get; set; }
            public int carSlot0 { get; set; }
            public int carSlot1 { get; set; }
            public int carSlot2 { get; set; }
            public int carSlot3 { get; set; }
            public int tierFixUp { get; set; }
        }

        public class Settings
        {
            public string PermissionAdmin { get; set; }
            public float RoundSeconds { get; set; }
            public int MaxRounds { get; set; }
            public int WinPoints { get; set; }
            public bool autoEvents { get; set; }
            public int JoinWindowSeconds { get; set; }
            public int EventIntervalSeconds { get; set; }
            public int playersOnlineNeeded { get; set; }
            public int MinPlayersToStart { get; set; }
            public int MaxPlayersToStart { get; set; }
            public bool UseFixedLocation { get; set; }
            public bool ShowMapMarker { get; set; }
            public bool testing { get; set; }
            public string ArenaPrefabPath { get; set; }
            public float ArenaSpawnDelay { get; set; }
            public float ArenaAltitude { get; set; }

            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public string ArenaZoneID { get; set; }

            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public int nextEventSeconds { get; set; }
        }

        public class ballSettings
        {
            public Vector3 BallMaxvelocity { get; set; }
        }

        public class itemSettings
        {
            public bool winItemEnable { get; set; }
            public int winItem { get; set; }
            public int winItemAmount { get; set; }
            public bool joinItemEnable { get; set; }
            public int joinItem { get; set; }
            public int joinItemAmount { get; set; }
        }

        public class EventSettings
        {
            public Vector3 eventCenter { get; set; }
            public Vector3 RedZone { get; set; }
            public Vector3 RedZoneSize { get; set; }
            public float RedZoneRotation { get; set; }
            public Vector3 BlueZone { get; set; }
            public Vector3 BlueZoneSize { get; set; }
            public float BlueZoneRotation { get; set; }
            public float FieldGoalDistance { get; set; }
            public float CarSpawnRadius { get; set; }
            public Vector3 ArenaBoundsSize { get; set; }
            public Vector3 ArenaOrigin { get; set; }
        }

        public class GridSettings
        {
            public float CellSize { get; set; }
            public float ArenaRadius { get; set; }
            public float MaxSlope { get; set; }
            public float MonumentBuffer { get; set; }
            public float BuildingBuffer { get; set; }
            public float WaterBuffer { get; set; }
            public int EdgePadding { get; set; }
        }

        private ConfigData GetBaseConfig()
        {
            return new ConfigData
            {
                CarSettings = new carSettings
                {
                    totalRockets = 5,
                    carFrame = "assets/content/vehicles/modularcar/car_chassis_2module.entity.prefab",
                    carSlot0 = 1559779253,
                    carSlot1 = -1501451746,
                    carSlot2 = 0,
                    carSlot3 = 0,
                    tierFixUp = 2
                },
                settings = new Settings
                {
                    PermissionAdmin = "rustleague.admin",
                    RoundSeconds = 120,
                    MaxRounds = 4,
                    WinPoints = 5,
                    autoEvents = true,
                    JoinWindowSeconds = 1200,
                    EventIntervalSeconds = 7200,
                    playersOnlineNeeded = 2,
                    MinPlayersToStart = 2,
                    MaxPlayersToStart = 6,
                    UseFixedLocation = false,
                    ShowMapMarker = true,
                    testing = false,
                    ArenaPrefabPath = "maps/prefabs/RustLeagueArena.map",
                    ArenaSpawnDelay = 0.01f,
                    ArenaAltitude = 700f
                },
                BallSettings = new ballSettings
                {
                    BallMaxvelocity = new Vector3(100f, 108f, 100f)
                },
                ItemSettings = new itemSettings
                {
                    winItemEnable = false,
                    winItem = -932201673,
                    winItemAmount = 100,
                    joinItemEnable = false,
                    joinItem = -932201673,
                    joinItemAmount = 50
                },
                eventSettings = new EventSettings
                {
                    eventCenter = Vector3.zero,
                    RedZone = Vector3.zero,
                    RedZoneSize = new Vector3(20f, 18f, 3f),
                    RedZoneRotation = 0f,
                    BlueZone = Vector3.zero,
                    BlueZoneSize = new Vector3(20f, 18f, 3f),
                    BlueZoneRotation = 180f,
                    FieldGoalDistance = 40f,
                    CarSpawnRadius = 20f,
                    ArenaBoundsSize = new Vector3(55f, 40f, 95f),
                    ArenaOrigin = Vector3.zero
                },
                Grid = new GridSettings
                {
                    CellSize = 50f,
                    ArenaRadius = 55f,
                    MaxSlope = 4f,
                    MonumentBuffer = 80f,
                    BuildingBuffer = 40f,
                    WaterBuffer = 1.5f,
                    EdgePadding = 150
                },
                Version = new VersionNumber { Major = 1, Minor = 3, Patch = 0 }
            };
        }

        private void LoadOrCreateConfig()
        {
            string path = RustLeagueHost.Instance.ConfigPath;
            if (File.Exists(path))
            {
                try
                {
                    configData = JsonConvert.DeserializeObject<ConfigData>(File.ReadAllText(path));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[RustLeague] Config parse failed, using defaults: " + ex.Message);
                    configData = GetBaseConfig();
                }
            }
            else
            {
                configData = GetBaseConfig();
            }

            if (configData == null) configData = GetBaseConfig();
            if (configData.settings == null) configData.settings = GetBaseConfig().settings;
            if (configData.BallSettings == null) configData.BallSettings = GetBaseConfig().BallSettings;
            if (configData.ItemSettings == null) configData.ItemSettings = GetBaseConfig().ItemSettings;
            if (configData.CarSettings == null) configData.CarSettings = GetBaseConfig().CarSettings;
            if (configData.eventSettings == null) configData.eventSettings = GetBaseConfig().eventSettings;
            if (configData.Grid == null) configData.Grid = GetBaseConfig().Grid;

            var defaults = GetBaseConfig();
            if (configData.settings.JoinWindowSeconds <= 0) configData.settings.JoinWindowSeconds = defaults.settings.JoinWindowSeconds;
            if (configData.settings.EventIntervalSeconds <= 0)
            {
                if (configData.settings.nextEventSeconds > 600)
                    configData.settings.EventIntervalSeconds = configData.settings.nextEventSeconds;
                else
                    configData.settings.EventIntervalSeconds = defaults.settings.EventIntervalSeconds;
            }
            if (configData.settings.MinPlayersToStart <= 0) configData.settings.MinPlayersToStart = 2;
            if (configData.settings.MaxPlayersToStart <= 0) configData.settings.MaxPlayersToStart = 6;
            if (string.IsNullOrEmpty(configData.settings.PermissionAdmin))
                configData.settings.PermissionAdmin = "rustleague.admin";
            if (configData.eventSettings.FieldGoalDistance <= 0f) configData.eventSettings.FieldGoalDistance = 40f;
            if (configData.eventSettings.CarSpawnRadius <= 0f) configData.eventSettings.CarSpawnRadius = 20f;
            if (configData.eventSettings.ArenaBoundsSize == Vector3.zero)
                configData.eventSettings.ArenaBoundsSize = new Vector3(55f, 40f, 95f);
            if (configData.eventSettings.RedZoneSize == Vector3.zero)
                configData.eventSettings.RedZoneSize = new Vector3(20f, 18f, 3f);
            if (configData.eventSettings.BlueZoneSize == Vector3.zero)
                configData.eventSettings.BlueZoneSize = new Vector3(20f, 18f, 3f);
            if (string.IsNullOrEmpty(configData.settings.ArenaPrefabPath))
                configData.settings.ArenaPrefabPath = defaults.settings.ArenaPrefabPath;
            if (configData.settings.ArenaSpawnDelay <= 0f)
                configData.settings.ArenaSpawnDelay = 0.01f;
            if (configData.settings.ArenaAltitude <= 0f)
                configData.settings.ArenaAltitude = 700f;
            if (configData.CarSettings.totalRockets < 5)
                configData.CarSettings.totalRockets = 5;

            configData.Version = new VersionNumber { Major = 1, Minor = 3, Patch = 5 };
            SaveConfig();
        }

        internal void SaveConfig()
        {
            string path = RustLeagueHost.Instance?.ConfigPath;
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, JsonConvert.SerializeObject(configData, Formatting.Indented));
        }

        private void LoadDefaultMessages()
        {
            RustLeagueHost.Instance?.Lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Blocked"] = "<color=#ce422b>You can not use this command.</color>",
                ["startEvent"] = "RustLeague is open at <color=#ce422b>{0}</color> for 20 minutes. Type /rl to join.",
                ["alreadystarted"] = "Event already started.",
                ["joined"] = "You just joined the waiting list for the event.",
                ["alreadyjoined"] = "you already are in the event waiting list.",
                ["left"] = "You are not longer wating on the event",
                ["notinevent"] = "You are not in the event waiting list.",
                ["notopen"] = "There is no active event.",
                ["stillLooking"] = "RustLeague still needs more players at {0}. Type /rl  ({1} left)",
                ["StartOff"] = "RustLeague has now started.",
                ["NoneOpen"] = "There is no RustLeague event going on.",
                ["EventBegin"] = "The event will start shortly.",
                ["startEngines"] = "Start your engine and GO GO GO.",
                ["RoundOver"] = "This round is now over.",
                ["RedScore"] = "Red team just scored ball reset.",
                ["BlueScore"] = "Blue team just scored ball reset.",
                ["YourTeamRed"] = "YOU ARE RED",
                ["YourTeamBlue"] = "YOU ARE BLUE",
                ["YourTeamRedChat"] = "<color=#ce422b>You are on RED. Put the ball in the RED goal.</color>",
                ["YourTeamBlueChat"] = "<color=#4da6ff>You are on BLUE. Put the ball in the BLUE goal.</color>",
                ["endEvent"] = "RustLeague event is now over.",
                ["tie"] = "RustLeague event is now over and there is a tie.",
                ["redGUI"] = "The winner is red team.",
                ["blueGUI"] = "The winner is blue team.",
                ["tieGUI"] = "There was a tie.",
                ["eventTries"] = "Rust League event is over not enough players joined.",
                ["joinInfoUI"] = "Welcome to Rust League we recommend you strip off everything before you join the event waiting list, And remember to have fun.",
                ["leaveInfoUI"] = "Welcome to Rust League you are currently in the waiting list.",
                ["gaveJoinRefund"] = "<color=#ce422b>There was no one to match you with here is your refund</color>",
                ["dropedJoinRefund"] = "<color=#ce422b>There was no one to match you with here is your refund IT WAS DROPED ON THE GROUND</color>",
                ["charged"] = "<color=#ce422b>You were charged {0} {1} to join the event.</color>",
                ["Notcharged"] = "<color=#ce422b>You need {0} {1} to join the Rust League event.</color>",
                ["gaveWinItem"] = "<color=#ce422b>You just won {0} {1}</color>",
                ["dropedWinItem"] = "<color=#ce422b>You just won {0} {1} but it was droped on the ground.</color>",
                ["AnnounceWin"] = "RustLeague: {0} team dominated {1} {2}",
                ["AnnounceTie"] = "RustLeague: {0} team tied {1} {2}",
                ["OpenEventConsoleAlready"] = "Event is already running.",
                ["OpenEventConsole"] = "Event is now open.",
                ["ServerOnlyCanRun"] = "Server only can run this command.",
                ["UiJoin"] = "Join",
                ["UiStay"] = "Stay",
                ["UiLeave"] = "Leave",
                ["noGrid"] = "Could not pick a sky location. Try /rl here.",
                ["gridScanning"] = "Sky spawn is ready.",
                ["centerSet"] = "Event center saved (arena will spawn 700m above this XZ).",
                ["RedZoneSet"] = "Red goal saved.",
                ["BlueZoneSet"] = "Blue goal saved.",
                ["NotSetWhenStarted"] = "Cannot change location while an event is open.",
                ["scanStarted"] = "Sky events pick a random map location each open. No scan needed.",
                ["statusLine"] = "RustLeague: open={0} running={1} joined={2} altitude={3} next={4} world={5} ents={6}",
                ["arenaSpawning"] = "RustLeague arena is spawning at {0}…",
                ["arenaReady"] = "RustLeague arena is up at {0}. Type /rl to join, /rl tp to teleport there.",
                ["arenaMissing"] = "RustLeague arena prefab was not found. Place maps/prefabs/RustLeagueArena.map (or set ArenaPrefabPath).",
                ["arenaNotSpawned"] = "No arena is spawned. Use /rl spawn (or /rl here) first.",
                ["arenaTp"] = "Teleported to RustLeague {0}  ({1:F0}, {2:F0}, {3:F0})  world pieces={4} entities={5}",
                ["soloTestStarting"] = "Solo test: spawning arena at {0} then starting with you only.",
                ["soloTestStarted"] = "Solo test started. You are in the event with one car."
            });
        }

        #endregion

        private bool IsAdmin(BasePlayer player)
        {
            if (player == null) return false;
            return player.HasPermission(configData.settings.PermissionAdmin);
        }

        internal string GridRef(Vector3 pos) => MapHelper.PositionToString(pos);

        internal float ArenaAltitude => Mathf.Max(50f, configData.settings.ArenaAltitude);

        internal Vector3 LiftToSky(Vector3 xz)
        {
            float terrain = TerrainMeta.HeightMap != null ? TerrainMeta.HeightMap.GetHeight(xz) : 0f;
            return new Vector3(xz.x, terrain + ArenaAltitude, xz.z);
        }

        internal Vector3 PickRandomSkyOrigin()
        {
            float half = World.Size > 100f ? World.Size * 0.45f : 1800f;
            Vector2 r = UnityEngine.Random.insideUnitCircle * half;
            return LiftToSky(new Vector3(r.x, 0f, r.y));
        }

        internal bool IsEventOccupant(BasePlayer player)
        {
            if (player == null) return false;
            ulong id = player.GetUserId();
            return EventPlayerLastPos.ContainsKey(id) || RuningEventPlayer.ContainsKey(id);
        }

        internal bool TryBlockViolation(BasePlayer player, AntiHackType type)
        {
            if (!IsEventOccupant(player)) return false;
            if (type != AntiHackType.FlyHack && type != AntiHackType.SpeedHack && type != AntiHackType.NoClip)
                return false;
            ResetAntiHack(player);
            return true;
        }

        internal static void ResetAntiHack(BasePlayer player)
        {
            if (player == null || player.ActivePlayerInd < 0) return;
            try
            {
                int ind = player.ActivePlayerInd;
                if (AntiHack.PlayerStates.IsCreated)
                    AntiHack.PlayerStates[ind] = default;
                if (AntiHack.PlayerNoclipStates.IsCreated)
                    AntiHack.PlayerNoclipStates[ind] = default;
                if (AntiHack.PlayerSpeedhackStates.IsCreated)
                    AntiHack.PlayerSpeedhackStates[ind] = default;
                if (AntiHack.PlayerFlyhackStates.IsCreated)
                    AntiHack.PlayerFlyhackStates[ind] = default;
                player.rpcHistory?.Clear();
            }
            catch { }
        }

        internal string JoinTimeLeft()
        {
            int window = Mathf.Max(60, configData.settings.JoinWindowSeconds);
            var end = _cycleOpenedAt.AddSeconds(window);
            var left = end - DateTime.UtcNow;
            if (left.TotalSeconds < 0) left = TimeSpan.Zero;
            return string.Format("{0}M {1}S", (int)left.TotalMinutes, left.Seconds);
        }
    }
}
