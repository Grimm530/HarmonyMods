using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Facepunch.Sqlite;

namespace EconomicsHarmony
{
    [Info("Economics", "Grimm530", "3.10.4")]
    [Description("Basic economics system and economy API")]
    public class Economics : EconomicsPluginBase
    {
        #region Configuration

        private Configuration config;

        private class Configuration
        {
            [JsonProperty("Allow negative balance for accounts")]
            public bool AllowNegativeBalance = false;

            [JsonProperty("Balance limit for accounts (0 to disable)")]
            public int BalanceLimit = 0;

            [JsonProperty("Maximum balance for accounts (0 to disable)")] // TODO: From version 3.8.6; remove eventually
            private int BalanceLimitOld { set { BalanceLimit = value; } }

            [JsonProperty("Negative balance limit for accounts (0 to disable)")]
            public int NegativeBalanceLimit = 0;

            [JsonProperty("Remove unused accounts")]
            public bool RemoveUnused = true;

            [JsonProperty("Log transactions to file")]
            public bool LogTransactions = false;

            [JsonProperty("Starting account balance (0 or higher)")]
            public int StartingBalance = 1000;

            [JsonProperty("Starting money amount (0 or higher)")] // TODO: From version 3.8.6; remove eventually
            private int StartingBalanceOld { set { StartingBalance = value; } }

            [JsonProperty("Wipe balances on new save file")]
            public bool WipeOnNewSave = false;

            [JsonProperty("Purge accounts older than days (0 to disable)")]
            public int PurgeAfterDays = 0;

            [JsonProperty("Enable RP accumulation monitoring")]
            public bool EnableRPMonitoring = true;

            [JsonProperty("Walk stack on API deposits to label Shop/RustRewards (expensive during mass deposits — set false if server stalls)")]
            public bool DepositStackWalkForSource = true;

            [JsonProperty("RP threshold for Discord notification (0 to disable)")]
            public double RPThreshold = 5000.0;

            [JsonProperty("Monitoring window in hours")]
            public int MonitoringWindowHours = 12;

            [JsonProperty("Discord webhook URL for RP notifications")]
            public string DiscordWebhookUrl = "";

            [JsonProperty("Enable debug logging")]
            public bool EnableDebugLogging = false;

            [JsonProperty("Use 24-hour daily cycles instead of rolling 12-hour windows")]
            public bool UseDailyCycles = true;

            [JsonProperty("Daily reset time (24-hour format, UTC)")]
            public string DailyResetTime = "00:00";

            [JsonProperty("Notification throttling settings")]
            public NotificationThrottlingSettings NotificationThrottling = new NotificationThrottlingSettings();

            [JsonProperty("Use PlaytimeTracker for last seen data if available")]
            public bool UsePlaytimeTracker = true;

            [JsonProperty("Enable periodic RP reports every 6 hours")]
            public bool EnablePeriodicReports = true;

            [JsonProperty("Periodic report interval (hours)")]
            public int PeriodicReportIntervalHours = 6;

            /// <summary>
            /// File = oxide/data/Economics/Economics.json via Oxide data directory (junction data root for shared paths across zones),
            /// or the same two filenames under "Custom economics data directory" when that setting is set.
            /// Sqlite = Facepunch.Sqlite (RustDedicated_Data/Managed/Facepunch.Sqlite.dll + game sqlite3).
            /// With Sqlite: each connect reloads from the DB; each disconnect upserts so the next zone sees the latest balance.
            /// RP tracking: Economics_RPTracking.json next to Economics.json.
            /// </summary>
            [JsonProperty("Balance storage mode (File | Sqlite)")]
            public string BalanceStorageMode = "File";

            /// <summary>Absolute path recommended, same path on all server instances (e.g. C:/rust/shared/economics_balances.db).</summary>
            [JsonProperty("SQLite database file path")]
            public string BalanceSqlitePath = @"C:\!DataPersistence\economics_balances.db";

            [JsonProperty("Enable Data Persistence Debug")]
            public bool EnableDataPersistenceDebug = false;

            /// <summary>
            /// When set, balance and RP tracking JSON are read/written under this folder as Economics.json and
            /// Economics_RPTracking.json (same layout as oxide/data/Economics/). Empty keeps default Oxide data files.
            /// </summary>
            [JsonProperty("Custom economics data directory (absolute path, empty = default oxide/data/Economics)")]
            public string CustomEconomicsDataDirectory = "";

            public class NotificationThrottlingSettings
            {
                [JsonProperty("Minimum time between notifications for same player (minutes)")]
                public int MinIntervalMinutes = 30;

                [JsonProperty("Maximum notifications per player per day")]
                public int MaxNotificationsPerDay = 5;

                [JsonProperty("Enable notification throttling")]
                public bool Enabled = true;
            }

            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonConvert.DeserializeObject<Dictionary<string, object>>(ToJson());
        }

        protected override void LoadDefaultConfig() => config = new Configuration();

        protected override void LoadConfig()
        {
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null)
                {
                    throw new JsonException();
                }

                if (!DictionaryKeysEqual(config.ToDictionary(), Config.ToDictionary()))
                {
                    LogWarning("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch
            {
                LogWarning($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            LogWarning($"Configuration changes saved to {Name}.json");
            Config.WriteObject(config, true);
        }

        #endregion Configuration

        #region Stored Data

        private DynamicConfigFile data;
        private StoredData storedData;
        private DynamicConfigFile rpTrackingData;
        private RPTrackingData storedRPTrackingData;
        private bool changed;
        private readonly HashSet<string> _dirtyPlayerIds = new HashSet<string>();

        private string _balanceJsonPath;
        private string _rpTrackingJsonPath;

        private bool UsesCustomEconomicsDataDirectory =>
            !string.IsNullOrWhiteSpace(config?.CustomEconomicsDataDirectory);

        private string CustomEconomicsDataDirectoryFull =>
            UsesCustomEconomicsDataDirectory
                ? Path.GetFullPath(config.CustomEconomicsDataDirectory.Trim())
                : null;

        /// <summary>Oxide keys under oxide/data/Economics/ (e.g. shared junction to C:\!DataPersistence\oxide\data).</summary>
        private string BalanceDataKey => $"{Name}/{Name}";

        private string RpTrackingDataKey => $"{Name}/{Name}_RPTracking";

        /// <summary>Null when balance storage mode is File (JSON data file).</summary>
        private ISharedBalanceStore _sharedBalances;

        private readonly Queue<string> _discordEmbedQueue = new Queue<string>();
        private readonly object _discordEmbedLock = new object();
        private bool _discordEmbedInFlight;
        private int _discordEmbed429Retries;
        private long _lastDiscord429LogUnix;
        private const float DiscordEmbedSuccessSpacingSeconds = 1.15f;
        private const int DiscordEmbedMax429Retries = 12;

        private bool _rpTrackingDirty;
        private bool _rpTrackingForceFullSave;
        private bool _rpTrackingFullyLoaded;
        private readonly HashSet<string> _dirtyRpTrackingPlayerIds = new HashSet<string>();
        private Timer _rpTrackingDeferredSaveTimer;
        private const float RpTrackingDeferredSaveSeconds = 4f;
        private const int SharedJsonIoMaxAttempts = 5;
        private const int SharedJsonIoRetryDelayMs = 40;

        private interface ISharedBalanceStore
        {
            void EnsureSchemaAndLoad(StoredData into);
            bool TryGetPlayer(string playerId, out PlayerData data);
            void Upsert(string playerId, PlayerData pd);
            void Delete(string playerId);
            void Wipe();
        }

        /// <summary>
        /// Shared balance store via Facepunch.Sqlite (game Managed DLL + native sqlite3).
        /// Compatible with existing economics_balances.db schema created by the old System.Data.SQLite path.
        /// </summary>
        private sealed class SqliteSharedBalanceStore : ISharedBalanceStore, IDisposable
        {
            private readonly string _path;
            private readonly object _sync = new object();
            private FacepunchBalanceDb _db;
            private const string Table = "economics_balances";

            public SqliteSharedBalanceStore(Economics p)
            {
                _path = p.config.BalanceSqlitePath ?? "";
            }

            private FacepunchBalanceDb OpenDb()
            {
                if (_db != null)
                    return _db;

                if (string.IsNullOrWhiteSpace(_path))
                    throw new InvalidOperationException("Balance SQLite path is empty.");

                string dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var db = new FacepunchBalanceDb();
                db.Open(_path, fastMode: true);
                // Facepunch defaults to EXCLUSIVE locking; NORMAL allows multi-instance shared DB use.
                db.Execute("PRAGMA locking_mode = NORMAL");
                db.Execute("PRAGMA busy_timeout = 5000");
                _db = db;
                return _db;
            }

            public void EnsureSchemaAndLoad(StoredData into)
            {
                lock (_sync)
                {
                    FacepunchBalanceDb db = OpenDb();
                    db.Execute($@"
CREATE TABLE IF NOT EXISTS {Table} (
  steam_id TEXT NOT NULL PRIMARY KEY,
  balance REAL NOT NULL,
  last_seen REAL NOT NULL DEFAULT 0,
  last_seen_formatted TEXT NOT NULL DEFAULT ''
);");
                    db.LoadAllBalances(into);
                }
            }

            public bool TryGetPlayer(string playerId, out PlayerData data)
            {
                data = null;
                if (string.IsNullOrEmpty(playerId))
                    return false;

                lock (_sync)
                {
                    FacepunchBalanceDb db = OpenDb();
                    if (!db.TryReadPlayer(playerId, out float balance, out float lastSeen, out string lastSeenFormatted))
                        return false;

                    data = new PlayerData
                    {
                        Balance = balance,
                        LastSeen = lastSeen,
                        LastSeenFormatted = lastSeenFormatted ?? ""
                    };
                    return true;
                }
            }

            public void Upsert(string playerId, PlayerData pd)
            {
                if (pd == null || string.IsNullOrEmpty(playerId))
                    return;

                lock (_sync)
                {
                    FacepunchBalanceDb db = OpenDb();
                    db.Execute(
                        $"REPLACE INTO {Table} (steam_id, balance, last_seen, last_seen_formatted) VALUES (?, ?, ?, ?);",
                        playerId,
                        (float)pd.Balance,
                        (float)pd.LastSeen,
                        pd.LastSeenFormatted ?? "");
                }
            }

            public void Delete(string playerId)
            {
                if (string.IsNullOrEmpty(playerId))
                    return;

                lock (_sync)
                {
                    FacepunchBalanceDb db = OpenDb();
                    db.Execute($"DELETE FROM {Table} WHERE steam_id = ?;", playerId);
                }
            }

            public void Wipe()
            {
                lock (_sync)
                {
                    FacepunchBalanceDb db = OpenDb();
                    db.Execute($"DELETE FROM {Table};");
                }
            }

            public void Dispose()
            {
                lock (_sync)
                {
                    if (_db == null)
                        return;
                    try { _db.Close(); }
                    catch { /* ignore close errors on unload */ }
                    _db = null;
                }
            }
        }

        /// <summary>Subclass so we can read multi-column rows (public Facepunch API is single-column).</summary>
        private sealed class FacepunchBalanceDb : Database
        {
            private const string Table = "economics_balances";

            public void LoadAllBalances(StoredData into)
            {
                if (into?.Players == null)
                    return;

                IntPtr stm = Prepare($"SELECT steam_id, balance, last_seen, last_seen_formatted FROM {Table}");
                var rows = new List<PlayerDataRow>(256);
                ExecuteAndReadQueryResults(stm, rows, ReadPlayerRow);
                for (int i = 0; i < rows.Count; i++)
                {
                    PlayerDataRow row = rows[i];
                    if (string.IsNullOrEmpty(row.SteamId))
                        continue;
                    into.Players[row.SteamId] = new PlayerData
                    {
                        Balance = row.Balance,
                        LastSeen = row.LastSeen,
                        LastSeenFormatted = row.LastSeenFormatted ?? ""
                    };
                }
            }

            public bool TryReadPlayer(string playerId, out float balance, out float lastSeen, out string lastSeenFormatted)
            {
                balance = 0f;
                lastSeen = 0f;
                lastSeenFormatted = "";

                IntPtr stm = Prepare($"SELECT steam_id, balance, last_seen, last_seen_formatted FROM {Table} WHERE steam_id = ? LIMIT 1");
                Bind(stm, 1, playerId);
                PlayerDataRow? row = ExecuteAndReadQueryResult(stm, ReadPlayerRow);
                if (!row.HasValue || string.IsNullOrEmpty(row.Value.SteamId))
                    return false;

                balance = row.Value.Balance;
                lastSeen = row.Value.LastSeen;
                lastSeenFormatted = row.Value.LastSeenFormatted ?? "";
                return true;
            }

            private static PlayerDataRow ReadPlayerRow(IntPtr stmHandle)
            {
                // REAL columns map to float in Facepunch.Sqlite (double is not supported by GetColumnValue).
                string id = GetColumnValue<string>(stmHandle, 0);
                float bal = GetColumnValue<float>(stmHandle, 1);
                float ls = GetColumnValue<float>(stmHandle, 2);
                string lsf = GetColumnValue<string>(stmHandle, 3) ?? "";
                return new PlayerDataRow(id, bal, ls, lsf);
            }

            private readonly struct PlayerDataRow
            {
                public readonly string SteamId;
                public readonly float Balance;
                public readonly float LastSeen;
                public readonly string LastSeenFormatted;

                public PlayerDataRow(string steamId, float balance, float lastSeen, string lastSeenFormatted)
                {
                    SteamId = steamId;
                    Balance = balance;
                    LastSeen = lastSeen;
                    LastSeenFormatted = lastSeenFormatted;
                }
            }
        }

        private ISharedBalanceStore CreateSharedBalanceStoreOrNull()
        {
            string mode = config.BalanceStorageMode ?? "File";
            if (!string.Equals(mode, "Sqlite", StringComparison.OrdinalIgnoreCase))
                return null;

            if (!IsFacepunchSqliteAvailable())
            {
                PrintError("Economics: Balance storage mode is Sqlite but Facepunch.Sqlite failed to load. Falling back to File (JSON) storage.");
                return null;
            }

            return new SqliteSharedBalanceStore(this);
        }

        private static bool IsFacepunchSqliteAvailable()
        {
            try
            {
                return typeof(Database) != null;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (TypeLoadException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Disable shared store and persist via JSON when SQLite fails at runtime.</summary>
        private void DisableSharedBalancesAndFallBackToFile(string reason)
        {
            if (_sharedBalances == null)
                return;

            PrintError($"Economics: disabling Sqlite shared storage ({reason}). Saving balances to JSON instead.");
            if (_sharedBalances is IDisposable disposable)
            {
                try { disposable.Dispose(); }
                catch { /* ignore */ }
            }
            _sharedBalances = null;
            _dirtyPlayerIds.Clear();
            changed = true;
        }

        /// <summary>One-time copy from legacy oxide/data/Economics.json to oxide/data/Economics/Economics.json or custom directory.</summary>
        private void MigrateLegacyEconomicsDataFilesIfNeeded()
        {
            if (UsesCustomEconomicsDataDirectory)
            {
                var root = CustomEconomicsDataDirectoryFull;
                if (!string.IsNullOrEmpty(root))
                    Directory.CreateDirectory(root);
            }

            var nestedBalance = Interface.Oxide.DataFileSystem.GetFile(BalanceDataKey);
            var targetBalancePath = UsesCustomEconomicsDataDirectory
                ? Path.Combine(CustomEconomicsDataDirectoryFull, $"{Name}.json")
                : nestedBalance.Filename;

            var oldBalance = Interface.Oxide.DataFileSystem.GetFile(Name);
            if (oldBalance.Exists() && !File.Exists(targetBalancePath))
            {
                string dir = Path.GetDirectoryName(targetBalancePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                File.Copy(oldBalance.Filename, targetBalancePath);
                Puts(UsesCustomEconomicsDataDirectory
                    ? "Economics: migrated balances from legacy flat file into custom economics directory (Economics.json)."
                    : "Economics: migrated balances to oxide/data/Economics/Economics.json (legacy flat file left in place; you may delete it after verifying).");
            }
            else if (UsesCustomEconomicsDataDirectory && nestedBalance.Exists() && !File.Exists(targetBalancePath))
            {
                string dir = Path.GetDirectoryName(targetBalancePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                File.Copy(nestedBalance.Filename, targetBalancePath);
                Puts("Economics: copied balances from default oxide/data/Economics/Economics.json to custom economics directory.");
            }

            var nestedRp = Interface.Oxide.DataFileSystem.GetFile(RpTrackingDataKey);
            var targetRpPath = UsesCustomEconomicsDataDirectory
                ? Path.Combine(CustomEconomicsDataDirectoryFull, $"{Name}_RPTracking.json")
                : nestedRp.Filename;

            var oldRp = Interface.Oxide.DataFileSystem.GetFile($"{Name}_RPTracking");
            if (oldRp.Exists() && !File.Exists(targetRpPath))
            {
                string dir = Path.GetDirectoryName(targetRpPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                File.Copy(oldRp.Filename, targetRpPath);
                Puts(UsesCustomEconomicsDataDirectory
                    ? "Economics: migrated RP tracking into custom economics directory (Economics_RPTracking.json)."
                    : "Economics: migrated RP tracking to oxide/data/Economics/Economics_RPTracking.json.");
            }
            else if (UsesCustomEconomicsDataDirectory && nestedRp.Exists() && !File.Exists(targetRpPath))
            {
                string dir = Path.GetDirectoryName(targetRpPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                File.Copy(nestedRp.Filename, targetRpPath);
                Puts("Economics: copied RP tracking from default oxide path to custom economics directory.");
            }
        }

        private void InitializeEconomicsDataPaths()
        {
            if (UsesCustomEconomicsDataDirectory)
            {
                var root = CustomEconomicsDataDirectoryFull;
                if (!string.IsNullOrEmpty(root))
                    Directory.CreateDirectory(root);

                _balanceJsonPath = Path.GetFullPath(Path.Combine(root, $"{Name}.json"));
                _rpTrackingJsonPath = Path.GetFullPath(Path.Combine(root, $"{Name}_RPTracking.json"));
                data = null;
                rpTrackingData = null;
            }
            else
            {
                data = Interface.Oxide.DataFileSystem.GetFile(BalanceDataKey);
                rpTrackingData = Interface.Oxide.DataFileSystem.GetFile(RpTrackingDataKey);
                _balanceJsonPath = Path.GetFullPath(data.Filename);
                _rpTrackingJsonPath = Path.GetFullPath(rpTrackingData.Filename);
            }

            if (config.EnableDataPersistenceDebug)
            {
                DataPersistenceDebug($"Uses custom economics directory: {UsesCustomEconomicsDataDirectory}");
                DataPersistenceDebug($"Balance JSON: {_balanceJsonPath} (exists: {File.Exists(_balanceJsonPath)})");
                DataPersistenceDebug($"RP tracking JSON: {_rpTrackingJsonPath} (exists: {File.Exists(_rpTrackingJsonPath)})");
            }
        }

        /// <summary>
        /// Loads Economics.json. Supports:
        /// - Extended format: { "Players": { steamId: { Balance, LastSeen, ... } } }
        /// - Oxide Economics 3.9 format: { "Balances": { steamId: amount } } (+ optional LastSeen map)
        /// - Very old: { steamIdulong: amount }
        /// </summary>
        private void LoadOrMigrateBalanceDataFromJson()
        {
            storedData = new StoredData();

            if (string.IsNullOrEmpty(_balanceJsonPath) || !File.Exists(_balanceJsonPath))
                return;

            string jsonContent;
            try
            {
                jsonContent = File.ReadAllText(_balanceJsonPath);
            }
            catch (Exception ex)
            {
                Puts($"Economics: failed to read {_balanceJsonPath}: {ex.Message}");
                return;
            }

            if (string.IsNullOrWhiteSpace(jsonContent))
                return;

            // Prefer Oxide Balances / hybrid detection before Players deserialize
            // (a Balances-only file deserializes to StoredData with empty Players).
            if (TryLoadBalancesFormat(jsonContent, out int balancesCount) && balancesCount > 0)
            {
                MarkAllPlayersDirty();
                Puts($"Economics: loaded {balancesCount} account(s) from Oxide Balances JSON ({_balanceJsonPath}).");
                return;
            }

            try
            {
                var playersData = JsonConvert.DeserializeObject<StoredData>(jsonContent);
                if (playersData?.Players != null && playersData.Players.Count > 0)
                {
                    storedData = playersData;
                    Puts($"Economics: loaded {storedData.Players.Count} account(s) from Players JSON ({_balanceJsonPath}).");
                    return;
                }
            }
            catch
            {
                // Fall through to other legacy shapes.
            }

            try
            {
                Dictionary<ulong, double> temp =
                    JsonConvert.DeserializeObject<Dictionary<ulong, double>>(jsonContent);
                if (temp != null && temp.Count > 0)
                {
                    foreach (KeyValuePair<ulong, double> old in temp)
                    {
                        string playerId = old.Key.ToString();
                        storedData.Players[playerId] = new PlayerData
                        {
                            Balance = old.Value,
                            LastSeenFormatted = "Never"
                        };
                    }
                    MarkAllPlayersDirty();
                    Puts($"Economics: migrated {temp.Count} account(s) from very old ulong-key JSON.");
                }
            }
            catch (Exception ex)
            {
                Puts($"Economics: balance JSON load failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Oxide Economics.cs StoredData shape: { "Balances": { "steamId": double }, "LastSeen"?: { ... } }.
        /// Returns true when the root has a Balances object (even if empty).
        /// </summary>
        private bool TryLoadBalancesFormat(string jsonContent, out int migratedCount)
        {
            migratedCount = 0;
            try
            {
                var root = JObject.Parse(jsonContent);
                var balances = root["Balances"] as JObject;
                if (balances == null)
                    return false;

                var lastSeen = root["LastSeen"] as JObject ?? new JObject();

                foreach (var kvp in balances)
                {
                    string playerId = kvp.Key;
                    if (string.IsNullOrEmpty(playerId))
                        continue;

                    double balance = kvp.Value.Type == JTokenType.Null ? 0.0 : kvp.Value.Value<double>();
                    double lastSeenTime = 0.0;
                    if (lastSeen[playerId] != null && lastSeen[playerId].Type != JTokenType.Null)
                        lastSeenTime = lastSeen[playerId].Value<double>();

                    storedData.Players[playerId] = new PlayerData
                    {
                        Balance = balance,
                        LastSeen = lastSeenTime,
                        LastSeenFormatted = lastSeenTime > 0 ? FormatTimestamp(lastSeenTime) : "Never"
                    };
                    migratedCount++;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Try read one player's balance from either Players or Oxide Balances JSON on disk.
        /// </summary>
        private bool TryReadPlayerBalanceFromJsonText(string jsonContent, string playerId, out PlayerData pd)
        {
            pd = null;
            if (string.IsNullOrEmpty(jsonContent) || string.IsNullOrEmpty(playerId))
                return false;

            try
            {
                var root = JObject.Parse(jsonContent);

                var players = root["Players"] as JObject;
                if (players != null && players[playerId] != null)
                {
                    var token = players[playerId];
                    pd = new PlayerData
                    {
                        Balance = token["Balance"]?.Value<double>() ?? 0.0,
                        LastSeen = token["LastSeen"]?.Value<double>() ?? 0.0,
                        LastSeenFormatted = token["LastSeenFormatted"]?.Value<string>() ?? ""
                    };
                    return true;
                }

                var balances = root["Balances"] as JObject;
                if (balances != null && balances[playerId] != null)
                {
                    double lastSeenTime = 0.0;
                    var lastSeen = root["LastSeen"] as JObject;
                    if (lastSeen?[playerId] != null && lastSeen[playerId].Type != JTokenType.Null)
                        lastSeenTime = lastSeen[playerId].Value<double>();

                    pd = new PlayerData
                    {
                        Balance = balances[playerId].Value<double>(),
                        LastSeen = lastSeenTime,
                        LastSeenFormatted = lastSeenTime > 0 ? FormatTimestamp(lastSeenTime) : "Never"
                    };
                    return true;
                }
            }
            catch
            {
                // ignored — caller logs
            }

            return false;
        }

        private class StoredData
        {
            public readonly Dictionary<string, PlayerData> Players = new Dictionary<string, PlayerData>();
        }

        private class PlayerData
        {
            public double Balance { get; set; } = 0.0;
            public double LastSeen { get; set; } = 0.0;
            public string LastSeenFormatted { get; set; } = "";
        }

        private class RPTrackingData
        {
            public readonly Dictionary<string, List<RPAcquisition>> PlayerRPAcquisitions = new Dictionary<string, List<RPAcquisition>>();
            public readonly Dictionary<string, DailyRPData> PlayerDailyRP = new Dictionary<string, DailyRPData>();
        }

        private class RPAcquisition
        {
            public double Amount { get; set; }
            public double Timestamp { get; set; }
            public string TimestampFormatted { get; set; } = "";
            public string Source { get; set; } = "Unknown"; // "Shop", "Transfer", "Direct"
            public string FromPlayer { get; set; } = ""; // For transfers
        }

        private class DailyRPData
        {
            public string Date { get; set; } = ""; // yyyy-MM-dd format
            public double TotalRP { get; set; } = 0.0;
            public double LastNotificationTime { get; set; } = 0.0;
            public int NotificationCount { get; set; } = 0;
            public Dictionary<string, double> SourceBreakdown { get; set; } = new Dictionary<string, double>();
        }

        private void SaveData()
        {
            if (!changed) return;

            foreach (var player in storedData.Players.Values)
            {
                if (player.LastSeen > 0)
                {
                    player.LastSeenFormatted = FormatTimestamp(player.LastSeen);
                }
                else
                {
                    player.LastSeenFormatted = "Never";
                }
            }

            if (_sharedBalances != null)
            {
                try
                {
                    if (_dirtyPlayerIds.Count > 0)
                    {
                        Puts($"Saving {_dirtyPlayerIds.Count} balance record(s) to shared database...");
                        foreach (var playerId in _dirtyPlayerIds)
                        {
                            if (storedData.Players.TryGetValue(playerId, out var playerData))
                            {
                                _sharedBalances.Upsert(playerId, playerData);
                            }
                        }
                    }

                    changed = false;
                    _dirtyPlayerIds.Clear();
                    return;
                }
                catch (Exception ex)
                {
                    DisableSharedBalancesAndFallBackToFile(ex.Message);
                    // Fall through to JSON save below.
                }
            }

            var sortedData = new StoredData();
            var sortedPlayers = new List<KeyValuePair<string, PlayerData>>(storedData.Players);
            sortedPlayers.Sort((a, b) => b.Value.LastSeen.CompareTo(a.Value.LastSeen));

            foreach (var kvp in sortedPlayers)
            {
                sortedData.Players[kvp.Key] = kvp.Value;
            }

            Puts("Saving balances for players...");
            string dir = Path.GetDirectoryName(_balanceJsonPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_balanceJsonPath, JsonConvert.SerializeObject(sortedData, Formatting.Indented));

            changed = false;
            _dirtyPlayerIds.Clear();
        }

        private void MarkDataChanged(string playerId = null)
        {
            changed = true;

            if (_sharedBalances != null && !string.IsNullOrEmpty(playerId))
            {
                _dirtyPlayerIds.Add(playerId);
            }
        }

        private void MarkAllPlayersDirty()
        {
            changed = true;

            if (_sharedBalances == null)
                return;

            _dirtyPlayerIds.Clear();
            foreach (var playerId in storedData.Players.Keys)
            {
                _dirtyPlayerIds.Add(playerId);
            }
        }

        private void DataPersistenceDebug(string message)
        {
            if (config.EnableDataPersistenceDebug)
            {
                Puts($"[Economics persistence] {message}");
            }
        }

        /// <summary>
        /// File mode: merge this player's balance from shared Economics.json (e.g. after transfer from another server).
        /// Supports Extended Players format and Oxide Balances format.
        /// </summary>
        private void RefreshPlayerBalanceFromJsonFile(string playerId)
        {
            if (_sharedBalances != null || string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(_balanceJsonPath) ||
                !File.Exists(_balanceJsonPath))
                return;

            try
            {
                if (!TryReadPlayerBalanceFromJsonText(File.ReadAllText(_balanceJsonPath), playerId, out var pd) || pd == null)
                    return;

                if (config.BalanceLimit > 0 && pd.Balance > config.BalanceLimit)
                    pd.Balance = config.BalanceLimit;

                storedData.Players[playerId] = pd;
                DataPersistenceDebug($"OnConnect user={playerId}: balance refreshed from {_balanceJsonPath}");
            }
            catch (Exception ex)
            {
                PrintWarning($"Economics: failed to refresh balance from file for {playerId}: {ex.Message}");
            }
        }

        /// <summary>
        /// File mode: merge this player's RP tracking rows from shared Economics_RPTracking.json.
        /// </summary>
        private void RefreshPlayerRpTrackingFromJsonFile(string playerId)
        {
            if (string.IsNullOrEmpty(playerId) || storedRPTrackingData == null ||
                string.IsNullOrEmpty(_rpTrackingJsonPath) || !File.Exists(_rpTrackingJsonPath))
                return;

            try
            {
                var fromDisk = JsonConvert.DeserializeObject<RPTrackingData>(ReadAllTextShared(_rpTrackingJsonPath));
                if (fromDisk == null)
                    return;

                if (fromDisk.PlayerRPAcquisitions != null &&
                    fromDisk.PlayerRPAcquisitions.TryGetValue(playerId, out var acq) && acq != null)
                {
                    var cloned = new List<RPAcquisition>(acq.Count);
                    for (int i = 0; i < acq.Count; i++)
                    {
                        var a = acq[i];
                        if (a == null) continue;
                        cloned.Add(new RPAcquisition
                        {
                            Amount = a.Amount,
                            Timestamp = a.Timestamp,
                            TimestampFormatted = string.IsNullOrWhiteSpace(a.TimestampFormatted)
                                ? FormatTimestamp(a.Timestamp)
                                : a.TimestampFormatted,
                            Source = a.Source ?? "Unknown",
                            FromPlayer = a.FromPlayer ?? ""
                        });
                    }
                    storedRPTrackingData.PlayerRPAcquisitions[playerId] = cloned;
                }

                if (fromDisk.PlayerDailyRP != null &&
                    fromDisk.PlayerDailyRP.TryGetValue(playerId, out var daily) && daily != null)
                {
                    storedRPTrackingData.PlayerDailyRP[playerId] = new DailyRPData
                    {
                        Date = daily.Date ?? "",
                        TotalRP = daily.TotalRP,
                        LastNotificationTime = daily.LastNotificationTime,
                        NotificationCount = daily.NotificationCount,
                        SourceBreakdown = daily.SourceBreakdown != null
                            ? new Dictionary<string, double>(daily.SourceBreakdown)
                            : new Dictionary<string, double>()
                    };
                }

                DataPersistenceDebug($"OnConnect user={playerId}: RP tracking refreshed from {_rpTrackingJsonPath}");
            }
            catch (Exception ex)
            {
                PrintWarning($"Economics: failed to refresh RP tracking from file for {playerId}: {ex.Message}");
            }
        }

        private void SharedUpsertPlayer(string playerId)
        {
            if (_sharedBalances == null || string.IsNullOrEmpty(playerId)) return;
            if (!storedData.Players.TryGetValue(playerId, out PlayerData pd)) return;
            if (pd.LastSeen > 0)
            {
                pd.LastSeenFormatted = FormatTimestamp(pd.LastSeen);
            }
            else
            {
                pd.LastSeenFormatted = "Never";
            }

            _sharedBalances.Upsert(playerId, pd);
        }

        /// <summary>
        /// Sqlite shared store: reload this player from the DB so each server sees balances written by other zones (e.g. after Nexus transfer).
        /// </summary>
        private void RefreshPlayerBalanceFromSharedStore(string playerId)
        {
            if (_sharedBalances == null || string.IsNullOrEmpty(playerId)) return;

            if (_sharedBalances.TryGetPlayer(playerId, out PlayerData fromDb))
            {
                if (config.BalanceLimit > 0 && fromDb.Balance > config.BalanceLimit)
                {
                    fromDb.Balance = config.BalanceLimit;
                }

                storedData.Players[playerId] = fromDb;
            }
            else
            {
                storedData.Players[playerId] = new PlayerData { Balance = config.StartingBalance };
            }
        }

        private void EnsureRpTrackingFullyLoaded()
        {
            if (_rpTrackingFullyLoaded || storedRPTrackingData == null)
                return;

            if (!string.IsNullOrEmpty(_rpTrackingJsonPath) && File.Exists(_rpTrackingJsonPath))
            {
                var fromDisk = TryLoadRpTrackingFromDisk();
                if (fromDisk != null)
                {
                    foreach (var kvp in fromDisk.PlayerRPAcquisitions)
                        storedRPTrackingData.PlayerRPAcquisitions[kvp.Key] = kvp.Value;

                    foreach (var kvp in fromDisk.PlayerDailyRP)
                        storedRPTrackingData.PlayerDailyRP[kvp.Key] = kvp.Value;
                }
            }

            _rpTrackingFullyLoaded = true;
        }

        private void EnsurePlayerRpTrackingLoaded(string playerId)
        {
            if (string.IsNullOrEmpty(playerId) || storedRPTrackingData == null)
                return;

            if (storedRPTrackingData.PlayerRPAcquisitions.ContainsKey(playerId) ||
                storedRPTrackingData.PlayerDailyRP.ContainsKey(playerId))
                return;

            RefreshPlayerRpTrackingFromJsonFile(playerId);
        }

        private void EvictPlayerRpTrackingFromMemory(string playerId)
        {
            if (string.IsNullOrEmpty(playerId) || storedRPTrackingData == null ||
                _dirtyRpTrackingPlayerIds.Contains(playerId))
                return;

            storedRPTrackingData.PlayerRPAcquisitions.Remove(playerId);
            storedRPTrackingData.PlayerDailyRP.Remove(playerId);
        }

        private void EvictOfflinePlayerRpTracking()
        {
            if (storedRPTrackingData == null || _rpTrackingFullyLoaded)
                return;

            var connected = new HashSet<string>();
            foreach (var player in covalence.Players.Connected)
            {
                if (player != null && !string.IsNullOrEmpty(player.Id))
                    connected.Add(player.Id);
            }

            var acqKeys = new List<string>(storedRPTrackingData.PlayerRPAcquisitions.Keys);
            for (int i = 0; i < acqKeys.Count; i++)
            {
                var playerId = acqKeys[i];
                if (!connected.Contains(playerId))
                    storedRPTrackingData.PlayerRPAcquisitions.Remove(playerId);
            }

            var dailyKeys = new List<string>(storedRPTrackingData.PlayerDailyRP.Keys);
            for (int i = 0; i < dailyKeys.Count; i++)
            {
                var playerId = dailyKeys[i];
                if (!connected.Contains(playerId))
                    storedRPTrackingData.PlayerDailyRP.Remove(playerId);
            }
        }

        private void NormalizeRpTrackingTimestampFormatted()
        {
            if (storedRPTrackingData?.PlayerRPAcquisitions == null)
                return;

            IEnumerable<string> playerIds = _dirtyRpTrackingPlayerIds.Count > 0
                ? _dirtyRpTrackingPlayerIds
                : storedRPTrackingData.PlayerRPAcquisitions.Keys;

            foreach (var playerId in playerIds)
            {
                if (!storedRPTrackingData.PlayerRPAcquisitions.TryGetValue(playerId, out var list) || list == null)
                    continue;

                foreach (var a in list)
                {
                    if (a == null)
                        continue;
                    if (string.IsNullOrWhiteSpace(a.TimestampFormatted) && a.Timestamp > 0)
                        a.TimestampFormatted = FormatTimestamp(a.Timestamp);
                }
            }
        }

        private static List<RPAcquisition> CloneRPAcquisitions(List<RPAcquisition> source)
        {
            if (source == null)
                return new List<RPAcquisition>();

            var list = new List<RPAcquisition>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                var a = source[i];
                if (a == null) continue;
                list.Add(new RPAcquisition
                {
                    Amount = a.Amount,
                    Timestamp = a.Timestamp,
                    TimestampFormatted = a.TimestampFormatted ?? "",
                    Source = a.Source ?? "Unknown",
                    FromPlayer = a.FromPlayer ?? ""
                });
            }
            return list;
        }

        private static DailyRPData CloneDailyRP(DailyRPData source)
        {
            if (source == null)
                return new DailyRPData();

            return new DailyRPData
            {
                Date = source.Date ?? "",
                TotalRP = source.TotalRP,
                LastNotificationTime = source.LastNotificationTime,
                NotificationCount = source.NotificationCount,
                SourceBreakdown = source.SourceBreakdown != null
                    ? new Dictionary<string, double>(source.SourceBreakdown)
                    : new Dictionary<string, double>()
            };
        }

        private RPTrackingData TryLoadRpTrackingFromDisk()
        {
            if (string.IsNullOrEmpty(_rpTrackingJsonPath) || !File.Exists(_rpTrackingJsonPath))
                return new RPTrackingData();

            for (int attempt = 0; attempt < SharedJsonIoMaxAttempts; attempt++)
            {
                try
                {
                    string json = ReadAllTextShared(_rpTrackingJsonPath);
                    var fromDisk = JsonConvert.DeserializeObject<RPTrackingData>(json);
                    return fromDisk ?? new RPTrackingData();
                }
                catch (IOException) when (attempt < SharedJsonIoMaxAttempts - 1)
                {
                    Thread.Sleep(SharedJsonIoRetryDelayMs * (attempt + 1));
                }
                catch (Exception ex)
                {
                    PrintWarning($"Economics: failed to read RP tracking from {_rpTrackingJsonPath}: {ex.Message}");
                    return new RPTrackingData();
                }
            }

            return new RPTrackingData();
        }

        private static string ReadAllTextShared(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        private void MergeDirtyRpTrackingInto(RPTrackingData target)
        {
            if (target == null || storedRPTrackingData == null)
                return;

            foreach (var playerId in _dirtyRpTrackingPlayerIds)
            {
                if (storedRPTrackingData.PlayerRPAcquisitions.TryGetValue(playerId, out var acquisitions))
                    target.PlayerRPAcquisitions[playerId] = CloneRPAcquisitions(acquisitions);
                else
                    target.PlayerRPAcquisitions.Remove(playerId);

                if (storedRPTrackingData.PlayerDailyRP.TryGetValue(playerId, out var daily))
                    target.PlayerDailyRP[playerId] = CloneDailyRP(daily);
                else
                    target.PlayerDailyRP.Remove(playerId);
            }
        }

        private bool TryWriteRpTrackingJson(string path, string contents)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string tempPath = path + ".tmp";

            for (int attempt = 0; attempt < SharedJsonIoMaxAttempts; attempt++)
            {
                try
                {
                    using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var writer = new StreamWriter(stream))
                    {
                        writer.Write(contents);
                    }

                    if (File.Exists(path))
                        File.Replace(tempPath, path, null);
                    else
                        File.Move(tempPath, path);

                    return true;
                }
                catch (IOException) when (attempt < SharedJsonIoMaxAttempts - 1)
                {
                    TryDeleteFile(tempPath);
                    Thread.Sleep(SharedJsonIoRetryDelayMs * (attempt + 1));
                }
                catch (Exception ex)
                {
                    TryDeleteFile(tempPath);
                    PrintWarning($"Economics: failed to write RP tracking to {path}: {ex.Message}");
                    return false;
                }
            }

            TryDeleteFile(tempPath);
            return false;
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // ignored
            }
        }

        private void SaveRPTrackingData()
        {
            if (storedRPTrackingData == null || string.IsNullOrEmpty(_rpTrackingJsonPath))
                return;

            if (!_rpTrackingForceFullSave && !_rpTrackingDirty && _dirtyRpTrackingPlayerIds.Count == 0)
                return;

            NormalizeRpTrackingTimestampFormatted();

            RPTrackingData toWrite;
            if (_rpTrackingForceFullSave)
            {
                toWrite = storedRPTrackingData;
            }
            else
            {
                toWrite = TryLoadRpTrackingFromDisk();
                MergeDirtyRpTrackingInto(toWrite);
            }

            string json = JsonConvert.SerializeObject(toWrite, Formatting.Indented);
            if (!TryWriteRpTrackingJson(_rpTrackingJsonPath, json))
            {
                PrintWarning($"Economics: failed to save RP tracking after {SharedJsonIoMaxAttempts} attempts ({_rpTrackingJsonPath}).");
                return;
            }

            _rpTrackingDirty = false;
            _rpTrackingForceFullSave = false;
            _dirtyRpTrackingPlayerIds.Clear();

            if (toWrite == storedRPTrackingData)
                _rpTrackingFullyLoaded = true;
        }

        /// <summary>
        /// RP tracking JSON can be huge; saving synchronously on every deposit (e.g. deposit *) freezes the server.
        /// Coalesce writes — OnServerSave and Unload still flush reliably.
        /// </summary>
        private void MarkRpTrackingDirtyDeferredSave(string playerId = null)
        {
            _rpTrackingDirty = true;
            if (!string.IsNullOrEmpty(playerId))
                _dirtyRpTrackingPlayerIds.Add(playerId);

            _rpTrackingDeferredSaveTimer?.Destroy();
            _rpTrackingDeferredSaveTimer = timer.Once(RpTrackingDeferredSaveSeconds, FlushRpTrackingDeferredSave);
        }

        private void FlushRpTrackingDeferredSave()
        {
            _rpTrackingDeferredSaveTimer = null;
            if (!_rpTrackingDirty)
            {
                return;
            }

            SaveRPTrackingData();
        }

        private void MigrateRPAcquisitionsData()
        {
            bool migrated = false;
            
            // Check if any players have RPAcquisitions data in the main file
            foreach (var player in storedData.Players.Values)
            {
                // This will be null since we removed RPAcquisitions from PlayerData
                // But we need to check if the old data structure exists
            }
            
            // Check if old data format exists by trying to read it
            try
            {
                if (string.IsNullOrEmpty(_balanceJsonPath) || !File.Exists(_balanceJsonPath))
                    throw new FileNotFoundException();

                var oldData =
                    JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(_balanceJsonPath));
                if (oldData != null && oldData.ContainsKey("Players"))
                {
                    var playersData = oldData["Players"] as Dictionary<string, object>;
                    if (playersData != null)
                    {
                        foreach (var kvp in playersData)
                        {
                            var playerData = kvp.Value as Dictionary<string, object>;
                            if (playerData != null && playerData.ContainsKey("RPAcquisitions"))
                            {
                                var rpAcquisitions = playerData["RPAcquisitions"] as List<object>;
                                if (rpAcquisitions != null && rpAcquisitions.Count > 0)
                                {
                                    // Migrate RPAcquisitions to separate file
                                    var migratedList = new List<RPAcquisition>();
                                    foreach (var rp in rpAcquisitions)
                                    {
                                        var rpDict = rp as Dictionary<string, object>;
                                        if (rpDict != null)
                                        {
                                            double migratedTs = Convert.ToDouble(rpDict.GetValueOrDefault("Timestamp", 0.0));
                                            migratedList.Add(new RPAcquisition
                                            {
                                                Amount = Convert.ToDouble(rpDict.GetValueOrDefault("Amount", 0.0)),
                                                Timestamp = migratedTs,
                                                TimestampFormatted = migratedTs > 0 ? FormatTimestamp(migratedTs) : "",
                                                Source = rpDict.GetValueOrDefault("Source", "Unknown").ToString(),
                                                FromPlayer = rpDict.GetValueOrDefault("FromPlayer", "").ToString()
                                            });
                                        }
                                    }
                                    
                                    if (migratedList.Count > 0)
                                    {
                                        storedRPTrackingData.PlayerRPAcquisitions[kvp.Key] = migratedList;
                                        _dirtyRpTrackingPlayerIds.Add(kvp.Key);
                                        migrated = true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Old format doesn't exist or is incompatible, that's fine
            }
            
            if (migrated)
            {
                Puts("Migrated RPAcquisitions data to separate file");
                _rpTrackingDirty = true;
                SaveRPTrackingData();
                MarkDataChanged(); // Mark main data as changed to clean up old RPAcquisitions
            }
        }

        private string FormatTimestamp(double timestamp)
        {
            if (timestamp <= 0) return "Never";
            
            try
            {
                DateTime dateTime = DateTimeOffset.FromUnixTimeSeconds((long)timestamp).LocalDateTime;
                return dateTime.ToString("MM/dd/yyyy HH:mm:ss");
            }
            catch
            {
                return "Invalid Date";
            }
        }

        private void TrackRPAcquisition(string playerId, double amount, string source = "Direct", string fromPlayer = "")
        {
            EnsurePlayerRpTrackingLoaded(playerId);

            double currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");

            // Initialize player data if needed
            if (!storedRPTrackingData.PlayerRPAcquisitions.ContainsKey(playerId))
            {
                storedRPTrackingData.PlayerRPAcquisitions[playerId] = new List<RPAcquisition>();
            }
            if (!storedRPTrackingData.PlayerDailyRP.ContainsKey(playerId))
            {
                storedRPTrackingData.PlayerDailyRP[playerId] = new DailyRPData();
            }

            var playerRPAcquisitions = storedRPTrackingData.PlayerRPAcquisitions[playerId];
            var playerDailyRP = storedRPTrackingData.PlayerDailyRP[playerId];

            // Check if we need to reset daily data
            if (!string.Equals(playerDailyRP.Date, today, StringComparison.Ordinal))
            {
                playerDailyRP.Date = today;
                playerDailyRP.TotalRP = 0.0;
                playerDailyRP.LastNotificationTime = 0.0;
                playerDailyRP.NotificationCount = 0;
                playerDailyRP.SourceBreakdown.Clear();
            }

            // Add new acquisition
            playerRPAcquisitions.Add(new RPAcquisition
            {
                Amount = amount,
                Timestamp = currentTime,
                TimestampFormatted = FormatTimestamp(currentTime),
                Source = source,
                FromPlayer = fromPlayer
            });

            // Update daily totals
            playerDailyRP.TotalRP += amount;
            if (!playerDailyRP.SourceBreakdown.ContainsKey(source))
            {
                playerDailyRP.SourceBreakdown[source] = 0.0;
            }
            playerDailyRP.SourceBreakdown[source] += amount;

            // Clean up old acquisitions (keep last 7 days)
            double sevenDaysAgo = currentTime - (7 * 24 * 3600);
            playerRPAcquisitions.RemoveAll(rp => rp.Timestamp < sevenDaysAgo);

            // Check if threshold is exceeded and notification should be sent
            if (playerDailyRP.TotalRP >= config.RPThreshold)
            {
                bool shouldNotify = true;

                // Apply throttling if enabled
                if (config.NotificationThrottling.Enabled)
                {
                    double minIntervalSeconds = config.NotificationThrottling.MinIntervalMinutes * 60;
                    double timeSinceLastNotification = currentTime - playerDailyRP.LastNotificationTime;

                    // Check minimum interval
                    if (timeSinceLastNotification < minIntervalSeconds)
                    {
                        shouldNotify = false;
                        if (config.EnableDebugLogging)
                        {
                            Puts($"[Economics Debug] Notification throttled for {playerId} - too soon since last notification");
                        }
                    }

                    // Check maximum notifications per day
                    if (shouldNotify && playerDailyRP.NotificationCount >= config.NotificationThrottling.MaxNotificationsPerDay)
                    {
                        shouldNotify = false;
                        if (config.EnableDebugLogging)
                        {
                            Puts($"[Economics Debug] Notification throttled for {playerId} - max daily notifications reached");
                        }
                    }
                }

                if (shouldNotify)
                {
                    playerDailyRP.LastNotificationTime = currentTime;
                    playerDailyRP.NotificationCount++;
                    SendDiscordNotification(playerId, playerDailyRP.TotalRP, amount, source, fromPlayer, playerDailyRP.SourceBreakdown);
                }
            }

            MarkRpTrackingDirtyDeferredSave(playerId);
        }

        private void SendDiscordNotification(string playerId, double totalRP, double latestAmount, string source = "Direct", string fromPlayer = "", Dictionary<string, double> sourceBreakdown = null)
        {
            try
            {
                if (string.IsNullOrEmpty(config.DiscordWebhookUrl))
                {
                    return;
                }

                var player = players.FindPlayerById(playerId);
                string playerName = player?.Name ?? "Unknown Player";
                
                string sourceInfo = "";
                string emoji = "🚨";
                
                switch (source)
                {
                    case "Shop":
                        emoji = "🛒";
                        sourceInfo = $"**Latest Source:** Shop Sale\n";
                        break;
                    case "Transfer":
                        emoji = "💸";
                        var fromPlayerObj = players.FindPlayerById(fromPlayer);
                        string fromPlayerName = fromPlayerObj?.Name ?? "Unknown Player";
                        sourceInfo = $"**Latest Source:** Direct Transfer from {fromPlayerName}\n";
                        break;
                    default:
                        emoji = "💰";
                        sourceInfo = $"**Latest Source:** Direct Deposit\n";
                        break;
                }

                // Prefer embed formatting
                var embedJson = BuildRPAlertEmbedJson(emoji, playerName, playerId, totalRP, latestAmount, sourceInfo, sourceBreakdown);
                if (!string.IsNullOrEmpty(embedJson))
                {
                    SendDiscordEmbed(embedJson);
                    if (config.EnableDebugLogging)
                    {
                        Puts("[Economics Debug] RP alert sent as embed");
                    }
                    return;
                }

                // Fallback text sending
                string timeFrame = config.UseDailyCycles ? "24h (Daily)" : $"{config.MonitoringWindowHours}h";
                string message = $"{emoji} **RP Accumulation Alert** {emoji}\n" +
                               $"**Player:** {playerName} ({playerId})\n" +
                               sourceInfo +
                               $"**Total RP in {timeFrame}:** {totalRP:F2}\n" +
                               $"**Latest Deposit:** {latestAmount:F2}\n" +
                               $"**Threshold:** {config.RPThreshold:F2}\n" +
                               $"**Time:** {DateTime.UtcNow:MM-dd-yyyy hh:mm tt} UTC";
                var discordPlugin = plugins.Find("DiscordMessages");
                if (discordPlugin != null && !string.IsNullOrEmpty(config.DiscordWebhookUrl))
                {
                    discordPlugin.Call("API_SendTextMessage", config.DiscordWebhookUrl, message, false, this);
                }
                else if (!string.IsNullOrEmpty(config.DiscordWebhookUrl))
                {
                    SendWebhookMessage(message);
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to send Discord notification: {ex.Message}");
            }
        }

        private void SendWebhookMessage(string message)
        {
            try
            {
                var payload = new
                {
                    content = message,
                    username = "Economics Monitor",
                    avatar_url = "https://www.dropbox.com/scl/fi/cfqwdj0sqdtn7ydog3g14/gr.png?rlkey=0ataku53xk5ouytcskmvt5vxx&st=dlljaqox&dl=1"
                };

                webrequest.Enqueue(config.DiscordWebhookUrl, JsonConvert.SerializeObject(payload), (code, response) =>
                {
                    if (code != 200 && code != 204)
                    {
                        LogError($"Discord webhook failed with code {code}: {response}");
                    }
                }, this, RequestMethod.POST, new Dictionary<string, string>
                {
                    ["Content-Type"] = "application/json"
                });
            }
            catch (Exception ex)
            {
                LogError($"Failed to send webhook message: {ex.Message}");
            }
        }

        // --- Discord Embed helpers (modeled after XDQuest FancyMessage) ---
        private class FancyMessage
        {
            [JsonProperty("content")] public string Content;
            [JsonProperty("username")] public string Username;
            [JsonProperty("avatar_url")] public string AvatarUrl;
            [JsonProperty("embeds")] public List<Embed> Embeds;

            public FancyMessage(string content, List<Embed> embeds)
            {
                Content = content;
                Embeds = embeds;
                Username = "Economics Monitor";
                AvatarUrl = "https://www.dropbox.com/scl/fi/cfqwdj0sqdtn7ydog3g14/gr.png?rlkey=0ataku53xk5ouytcskmvt5vxx&st=dlljaqox&dl=1";
            }

            public string ToJson() => JsonConvert.SerializeObject(this);

            public class Embed
            {
                [JsonProperty("title")] public string Title { get; }
                [JsonProperty("description")] public string Description { get; }
                [JsonProperty("color")] public int Color { get; }
                [JsonProperty("timestamp")] public string Timestamp { get; }

                public Embed(string title, string description = "", int color = 0x7289DA)
                {
                    Title = title;
                    Description = description;
                    Color = color;
                    Timestamp = DateTime.UtcNow.ToString("o");
                }
            }
        }

        private readonly Dictionary<string, string> _headersDiscord = new Dictionary<string, string>
        {
            {"Content-Type", "application/json"}
        };

        private void SendDiscordEmbed(string json)
        {
            if (string.IsNullOrEmpty(config.DiscordWebhookUrl))
            {
                return;
            }

            lock (_discordEmbedLock)
            {
                _discordEmbedQueue.Enqueue(json);
            }

            TryStartDiscordEmbedSend();
        }

        private void TryStartDiscordEmbedSend()
        {
            lock (_discordEmbedLock)
            {
                if (_discordEmbedInFlight || _discordEmbedQueue.Count == 0)
                {
                    return;
                }

                _discordEmbedInFlight = true;
            }

            SendDiscordEmbedHttp();
        }

        private void SendDiscordEmbedHttp()
        {
            string json;
            lock (_discordEmbedLock)
            {
                if (_discordEmbedQueue.Count == 0)
                {
                    _discordEmbedInFlight = false;
                    return;
                }

                json = _discordEmbedQueue.Peek();
            }

            string url = config.DiscordWebhookUrl.Contains("?") ? config.DiscordWebhookUrl : (config.DiscordWebhookUrl + "?wait=true");
            webrequest.Enqueue(url, json, DiscordEmbedHttpCallback, this, RequestMethod.POST, _headersDiscord, 10f);
        }

        private void DiscordEmbedHttpCallback(int code, string response)
        {
            if (code == 200 || code == 204)
            {
                lock (_discordEmbedLock)
                {
                    if (_discordEmbedQueue.Count > 0)
                    {
                        _discordEmbedQueue.Dequeue();
                    }

                    _discordEmbedInFlight = false;
                    _discordEmbed429Retries = 0;
                }

                timer.Once(DiscordEmbedSuccessSpacingSeconds, TryStartDiscordEmbedSend);
                return;
            }

            if (code == 429)
            {
                _discordEmbed429Retries++;
                MaybeLogDiscord429Once();

                if (_discordEmbed429Retries > DiscordEmbedMax429Retries)
                {
                    lock (_discordEmbedLock)
                    {
                        if (_discordEmbedQueue.Count > 0)
                        {
                            _discordEmbedQueue.Dequeue();
                        }

                        _discordEmbedInFlight = false;
                        _discordEmbed429Retries = 0;
                    }

                    LogWarning("[Economics] Discord webhook: dropped one queued embed after repeated rate limits (429).");
                    timer.Once(DiscordEmbedSuccessSpacingSeconds, TryStartDiscordEmbedSend);
                    return;
                }

                float waitSeconds = ParseDiscordRetryAfterSeconds(response);
                timer.Once(waitSeconds, SendDiscordEmbedHttp);
                return;
            }

            lock (_discordEmbedLock)
            {
                if (_discordEmbedQueue.Count > 0)
                {
                    _discordEmbedQueue.Dequeue();
                }

                _discordEmbedInFlight = false;
                _discordEmbed429Retries = 0;
            }

            LogError($"Discord embed post failed with code {code}: {response}");
            timer.Once(0.25f, TryStartDiscordEmbedSend);
        }

        private static float ParseDiscordRetryAfterSeconds(string response)
        {
            if (string.IsNullOrEmpty(response))
            {
                return 2f;
            }

            try
            {
                JToken t = JObject.Parse(response)["retry_after"];
                if (t != null && t.Type != JTokenType.Null)
                {
                    double sec = t.Value<double>();
                    if (sec < 0.35)
                    {
                        sec = 0.35;
                    }

                    if (sec > 120.0)
                    {
                        sec = 120.0;
                    }

                    return (float)sec;
                }
            }
            catch
            {
                // ignored
            }

            return 2f;
        }

        private void MaybeLogDiscord429Once()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (now - _lastDiscord429LogUnix < 60)
            {
                return;
            }

            _lastDiscord429LogUnix = now;
            LogWarning("[Economics] Discord webhook rate limited (429); serializing posts and honoring retry_after. Suppressing duplicate 429 logs for 60s.");
        }

        private string BuildPeriodicReportEmbedJson()
        {
            EnsureRpTrackingFullyLoaded();

            if (storedRPTrackingData?.PlayerDailyRP == null || storedRPTrackingData.PlayerDailyRP.Count == 0)
            {
                var emptyEmbed = new FancyMessage.Embed("🗂️ PERIODIC RP REPORT", "No RP activity found in the selected period.");
                var msgEmpty = new FancyMessage(null, new List<FancyMessage.Embed> { emptyEmbed });
                return msgEmpty.ToJson();
            }

            // Window
            int intervalHours = Math.Max(1, config.PeriodicReportIntervalHours);
            DateTime start = DateTime.UtcNow.AddHours(-intervalHours);
            DateTime end = DateTime.UtcNow;
            TimeSpan duration = end - start;

            // Totals
            double totalRPEarned = 0;
            int activePlayers = 0;
            var playerTotals = new Dictionary<string, (double totalRP, Dictionary<string, double> sourceBreakdown, string playerName)>();

            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            foreach (var kvp in storedRPTrackingData.PlayerDailyRP)
            {
                string playerId = kvp.Key;
                var daily = kvp.Value;
                if (daily.Date != today || daily.TotalRP <= 0) continue;

                totalRPEarned += daily.TotalRP;
                activePlayers++;
                var player = players.FindPlayerById(playerId);
                string playerName = player?.Name ?? "Unknown Player";
                playerTotals[playerId] = (daily.TotalRP, daily.SourceBreakdown, playerName);
            }

            // Build description
            string startDateStr = start.ToString("MM-dd-yyyy");
            string endDateTimeStr = end.ToString("MM-dd-yyyy hh:mm tt");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"**Report Period:** {startDateStr} to {endDateTimeStr} UTC");
            sb.AppendLine($"**Duration:** {duration.Days}d {duration.Hours}h {duration.Minutes}m");
            sb.AppendLine();
            if (playerTotals.Count == 0)
            {
                sb.AppendLine("No RP activity found in the selected period.");
            }
            else
            {
                sb.AppendLine($"**Total RP Earned:** {totalRPEarned:F2}");
                sb.AppendLine($"**Active Players:** {activePlayers}");
                sb.AppendLine();
                sb.AppendLine("🏆 **TOP RP ACCUMULATORS**");
                var topPlayers = SortByTotalRpDesc(playerTotals, 10);
                for (int i = 0; i < topPlayers.Count; i++)
                {
                    var kv = topPlayers[i];
                    var (rp, breakdown, name) = kv.Value;
                    sb.AppendLine($"**{name}** ({kv.Key}) - {rp:F0}");
                    if (breakdown != null && breakdown.Count > 0)
                    {
                        var sortedSources = SortDictByValueDesc(breakdown);
                        for (int s = 0; s < sortedSources.Count; s++)
                        {
                            var src = sortedSources[s];
                            string sourceName = src.Key == "Shop" ? "Shop" : src.Key == "Transfer" ? "Transfer" : src.Key == "Direct" ? "Direct Deposit" : src.Key;
                            sb.AppendLine($" - {sourceName} - {src.Value:F0}");
                        }
                    }
                    sb.AppendLine();
                }
            }

            var embed = new FancyMessage.Embed("🗂️ PERIODIC RP REPORT", sb.ToString());
            var msg = new FancyMessage(null, new List<FancyMessage.Embed> { embed });
            return msg.ToJson();
        }

        private void SendWipeSummaryReport()
        {
            try
            {
                var report = FormatWipeSummaryReport();
                if (!string.IsNullOrEmpty(report))
                {
                    // Prefer chunked text to avoid large embed size limits
                    if (!string.IsNullOrEmpty(config.DiscordWebhookUrl))
                        SendDiscordTextChunks(config.DiscordWebhookUrl, report, "Economics Monitor");
                    Puts("[Economics] Wipe summary report sent");
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to send wipe summary report: {ex.Message}");
            }
        }

        [ConsoleCommand("economics.wipesummary")]
        public void CmdEconomicsWipeSummary(ConsoleSystem.Arg arg)
        {
            try
            {
                string report = FormatWipeSummaryReport();
                if (!string.IsNullOrEmpty(report))
                {
                    // Print to server console
                    Puts(report);
                    arg?.ReplyWith(report);
                    // Also send to Discord if configured (chunked text for safety)
                    if (!string.IsNullOrEmpty(config.DiscordWebhookUrl))
                        SendDiscordTextChunks(config.DiscordWebhookUrl, report, "Economics Monitor");
                }
                else
                {
                    const string msgEmpty = "[Economics] No data available for wipe summary.";
                    Puts(msgEmpty);
                    arg?.ReplyWith(msgEmpty);
                }
            }
            catch (Exception ex)
            {
                string err = $"[Economics] Error generating wipe summary: {ex.Message}";
                LogError(err);
                arg?.ReplyWith(err);
            }
        }

        // Chunked plain content sender to respect Discord 2000-char limit with buffer
        private void SendDiscordTextChunks(string webhook, string message, string username)
        {
            if (string.IsNullOrEmpty(webhook) || string.IsNullOrEmpty(message)) return;
            const int maxLen = 1800;
            int index = 0;
            while (index < message.Length)
            {
                int len = Math.Min(maxLen, message.Length - index);
                string chunk = message.Substring(index, len);
                var payload = new Dictionary<string, object>
                {
                    {"content", chunk},
                    {"username", username},
                    {"avatar_url", "https://www.dropbox.com/scl/fi/cfqwdj0sqdtn7ydog3g14/gr.png?rlkey=0ataku53xk5ouytcskmvt5vxx&st=dlljaqox&dl=1"}
                };
                webrequest.Enqueue(webhook, JsonConvert.SerializeObject(payload), (code, response) => { }, this, RequestMethod.POST, new Dictionary<string, string>{{"Content-Type","application/json"}}, 30f);
                index += len;
            }
        }

        private string FormatWipeSummaryReport()
        {
            EnsureRpTrackingFullyLoaded();

            if (storedRPTrackingData?.PlayerRPAcquisitions == null || storedRPTrackingData.PlayerRPAcquisitions.Count == 0)
            {
                return "🏦 **ECONOMICS WIPE SUMMARY**\n*No RP tracking data found for wipe summary.*";
            }

            var report = new System.Text.StringBuilder();
            report.AppendLine("🏦 **ECONOMICS WIPE SUMMARY REPORT**");
            report.AppendLine($"*Wipe completed: {DateTime.Now:yyyy-MM-dd HH:mm:ss}*");
            report.AppendLine();

            // Calculate totals for the entire wipe
            double totalRPEarned = 0;
            int totalTransactions = 0;
            var playerTotals = new Dictionary<string, (double totalRP, int transactionCount, string playerName)>();

            foreach (var playerData in storedRPTrackingData.PlayerRPAcquisitions)
            {
                string playerId = playerData.Key;
                var acquisitions = playerData.Value;
                
                if (acquisitions.Count == 0) continue;

                double playerTotalRP = 0;
                for (int i = 0; i < acquisitions.Count; i++)
                    playerTotalRP += acquisitions[i].Amount;
                int playerTransactionCount = acquisitions.Count;
                
                totalRPEarned += playerTotalRP;
                totalTransactions += playerTransactionCount;

                // Get player name
                var player = players.FindPlayerById(playerId);
                string playerName = player?.Name ?? "Unknown Player";
                
                playerTotals[playerId] = (playerTotalRP, playerTransactionCount, playerName);
            }

            if (playerTotals.Count == 0)
            {
                return "🏦 **ECONOMICS WIPE SUMMARY**\n*No RP transactions found for wipe summary.*";
            }

            var sortedPlayers = SortByTotalRpAndTxDesc(playerTotals, 10);

            report.AppendLine("**💰 TOP RP EARNERS**");
            for (int i = 0; i < sortedPlayers.Count; i++)
            {
                var kvp = sortedPlayers[i];
                var (totalRP, transactionCount, playerName) = kvp.Value;
                report.AppendLine($"**{playerName}** ({kvp.Key})");
                report.AppendLine($"- Total RP Earned: {totalRP:F2}");
                report.AppendLine($"- Transactions: {transactionCount}");
                report.AppendLine();
            }

            // Add wipe totals
            report.AppendLine("**📊 WIPE TOTALS**");
            report.AppendLine($"- Total RP Earned: {totalRPEarned:F2}");
            report.AppendLine($"- Total Transactions: {totalTransactions}");
            report.AppendLine($"- Active Players: {playerTotals.Count}");
            report.AppendLine($"- Average RP per Player: {(playerTotals.Count > 0 ? totalRPEarned / playerTotals.Count : 0):F2}");

            return report.ToString();
        }

        public void OnServerSave()
        {
            PurgeInactiveAccountsIfEnabled();
            SaveData();
            // RP tracking is always persisted to Economics_RPTracking.json (independent of balance storage mode).
            // Flush on server save so SQLite/shared-balance setups still sync RP JSON to disk.
            _rpTrackingDeferredSaveTimer?.Destroy();
            _rpTrackingDeferredSaveTimer = null;
            SaveRPTrackingData();
            EvictOfflinePlayerRpTracking();
        }

        public void OnServerInitialized()
        {
            storedRPTrackingData = new RPTrackingData();
            _rpTrackingFullyLoaded = false;

            // Migrate existing RPAcquisitions data to separate file
            MigrateRPAcquisitionsData();
            
            // Save migrated data immediately to prevent loss
            if (changed)
            {
                SaveData();
                Puts("Migrated data saved successfully.");
            }
            
            // Ensure purge runs after all plugins (e.g., PlaytimeTracker) are loaded
            PurgeInactiveAccountsIfEnabled();

            if (_sharedBalances == null)
            {
                foreach (var p in covalence.Players.Connected)
                {
                    if (p == null || string.IsNullOrEmpty(p.Id))
                        continue;
                    RefreshPlayerBalanceFromJsonFile(p.Id);
                    RefreshPlayerRpTrackingFromJsonFile(p.Id);
                }
            }

            // Setup daily summary timer
            SetupDailySummaryTimer();
            
            // Setup periodic reports timer
            SetupPeriodicReportsTimer();
        }

        public override void HarmonyServerInitialized()
        {
            OnServerInitialized();
            // Fallback autosave every 5 minutes if SaveRestore.Save patch misses a cycle
            timer.Repeat(300f, -1, () =>
            {
                try { OnServerSave(); }
                catch (Exception ex) { PrintWarning("Autosave: " + ex.Message); }
            });
        }

        public void OnUserConnected(IPlayer user)
        {
            if (user == null || string.IsNullOrEmpty(user.Id)) return;

            if (_sharedBalances != null)
            {
                RefreshPlayerBalanceFromSharedStore(user.Id);
            }
            else
            {
                RefreshPlayerBalanceFromJsonFile(user.Id);
                RefreshPlayerRpTrackingFromJsonFile(user.Id);
            }

            UpdateLastSeen(user.Id);

            if (_sharedBalances != null)
            {
                SharedUpsertPlayer(user.Id);
                DataPersistenceDebug($"OnConnect user={user.Id}: refreshed from shared store + upsert (last_seen)");
            }
        }

        public void OnUserDisconnected(IPlayer user)
        {
            if (user == null || string.IsNullOrEmpty(user.Id)) return;

            UpdateLastSeen(user.Id);

            if (_sharedBalances != null)
            {
                SharedUpsertPlayer(user.Id);
                DataPersistenceDebug($"OnDisconnect user={user.Id}: upserted to shared store");
            }
            else
            {
                SaveData();
                DataPersistenceDebug($"OnDisconnect user={user.Id}: File mode — saved balances to disk");
            }

            if (_rpTrackingDirty || _dirtyRpTrackingPlayerIds.Contains(user.Id))
            {
                _rpTrackingDeferredSaveTimer?.Destroy();
                _rpTrackingDeferredSaveTimer = null;
                SaveRPTrackingData();
            }

            EvictPlayerRpTrackingFromMemory(user.Id);
        }

        private void UpdateLastSeen(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
                return;

            // Ensure player data exists
            if (!storedData.Players.ContainsKey(playerId))
            {
                storedData.Players[playerId] = new PlayerData { Balance = config.StartingBalance };
            }

            double currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            
            // Try to get last seen from PlaytimeTracker if available and enabled
            if (config.UsePlaytimeTracker)
            {
                try
                {
                    var playtimeTracker = plugins.Find("PlaytimeTracker");
                    if (playtimeTracker != null)
                    {
                        object lastSeenResult = playtimeTracker.Call("GetLastSeen", playerId);
                        if (lastSeenResult != null && lastSeenResult is double lastSeen)
                        {
                            // PlaytimeTracker can lag or use different semantics than "this moment"; never store a
                            // LastSeen older than now when Economics is actively handling this player.
                            storedData.Players[playerId].LastSeen = Math.Max(lastSeen, currentTime);
                            MarkDataChanged(playerId);
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (config.EnableDebugLogging)
                    {
                        Puts($"[Economics Debug] Failed to get last seen from PlaytimeTracker: {ex.Message}");
                    }
                }
            }
            
            // Fallback to our own tracking
            storedData.Players[playerId].LastSeen = currentTime;
            MarkDataChanged(playerId);
        }


        public override void HarmonyUnload()
        {
            lock (_discordEmbedLock)
            {
                _discordEmbedQueue.Clear();
                _discordEmbedInFlight = false;
                _discordEmbed429Retries = 0;
            }

            _rpTrackingDeferredSaveTimer?.Destroy();
            _rpTrackingDeferredSaveTimer = null;

            SaveData();
            SaveRPTrackingData();

            if (_sharedBalances is IDisposable disposable)
            {
                try { disposable.Dispose(); }
                catch { /* ignore close errors on unload */ }
                _sharedBalances = null;
            }
        }

        #endregion Stored Data

        #region Localization

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["CommandBalance"] = "balance",
                ["CommandDeposit"] = "deposit",
                ["CommandSetBalance"] = "SetBalance",
                ["CommandTransfer"] = "transfer",
                ["CommandWithdraw"] = "withdraw",
                ["CommandWipe"] = "ecowipe",
                ["CommandTestDiscord"] = "testdiscord",
                ["CommandTestDiscordDirect"] = "testdiscorddirect",
                ["CommandTestWebhook"] = "testwebhook",
                ["DataSaved"] = "Economics data saved!",
                ["DataWiped"] = "Economics data wiped!",
                ["DepositedToAll"] = "Deposited {0} total ({1} each) to {2} player(s)",
                ["LogDeposit"] = "{0} deposited to {1}",
                ["LogSetBalance"] = "{0} set as balance for {1}",
                ["LogTransfer"] = "{0} transferred to {1} from {2}",
                ["LogWithdrawl"] = "{0} withdrawn from {1}",
                ["NegativeBalance"] = "Balance can not be negative!",
                ["NotAllowed"] = "You are not allowed to use the '{0}' command",
                ["NoPlayersFound"] = "No players found with name or ID '{0}'",
                ["PlayerBalance"] = "Balance for {0}: {1}",
                ["PlayerLacksMoney"] = "'{0}' does not have enough money!",
                ["PlayersFound"] = "Multiple players were found, please specify: {0}",
                ["ReceivedFrom"] = "You have received {0} from {1}",
                ["SetBalanceForAll"] = "Balance set to {0} for {1} player(s)",
                ["TransactionFailed"] = "Transaction failed! Make sure amount is above 0",
                ["TransferredTo"] = "{0} transferred to {1}",
                ["TransferredToAll"] = "Transferred {0} total ({1} each) to {2} player(s)",
                ["TransferToSelf"] = "You can not transfer money yourself!",
                ["UsageBalance"] = "{0} - check your balance",
                ["UsageBalanceOthers"] = "{0} <player name or id> - check balance of a player",
                ["UsageDeposit"] = "{0} <player name or id> <amount> - deposit amount to player",
                ["UsageSetBalance"] = "Usage: {0} <player name or id> <amount> - set balance for player",
                ["UsageTransfer"] = "Usage: {0} <player name or id> <amount> - transfer money to player",
                ["UsageWithdraw"] = "Usage: {0} <player name or id> <amount> - withdraw money from player",
                ["UsageWipe"] = "Usage: {0} - wipe all economics data",
                ["UsagePurge"] = "Usage: {0} - manually purge inactive accounts",
                ["UsageStats"] = "Usage: {0} - show economics statistics",
                ["YouLackMoney"] = "You do not have enough money!",
                ["YouLostMoney"] = "You lost: {0}",
                ["YouReceivedMoney"] = "You received: {0}",
                ["YourBalance"] = "Your balance is: {0}",
                ["WithdrawnForAll"] = "Withdrew {0} total ({1} each) from {2} player(s)",
                ["ZeroAmount"] = "Amount cannot be zero"
            }, this);
        }

        #endregion Localization

        #region Initialization

        private const string permissionBalance = "economics.balance";
        private const string permissionDeposit = "economics.deposit";
        private const string permissionDepositAll = "economics.depositall";
        private const string permissionSetBalance = "economics.setbalance";
        private const string permissionSetBalanceAll = "economics.setbalanceall";
        private const string permissionTransfer = "economics.transfer";
        private const string permissionTransferAll = "economics.transferall";
        private const string permissionWithdraw = "economics.withdraw";
        private const string permissionWithdrawAll = "economics.withdrawall";
        private const string permissionWipe = "economics.wipe";

        public override void HarmonyInit()
        {
            LoadDefaultMessages();
            LoadConfig();
            // Register universal chat/console commands
            AddLocalizedCommand(nameof(CommandBalance));
            AddLocalizedCommand(nameof(CommandDeposit));
            AddLocalizedCommand(nameof(CommandSetBalance));
            AddLocalizedCommand(nameof(CommandTransfer));
            AddLocalizedCommand(nameof(CommandWithdraw));
            AddLocalizedCommand(nameof(CommandWipe));
            AddLocalizedCommand(nameof(CommandTestDiscord));
            AddLocalizedCommand(nameof(CommandTestDiscordDirect));
            AddLocalizedCommand(nameof(CommandTestWebhook));
            
            // Register new commands
            AddCovalenceCommand("ecopurge", nameof(CommandPurge));
            AddCovalenceCommand("ecostats", nameof(CommandStats));

            // Register permissions for commands
            permission.RegisterPermission(permissionBalance, this);
            permission.RegisterPermission(permissionDeposit, this);
            permission.RegisterPermission(permissionDepositAll, this);
            permission.RegisterPermission(permissionSetBalance, this);
            permission.RegisterPermission(permissionSetBalanceAll, this);
            permission.RegisterPermission(permissionTransfer, this);
            permission.RegisterPermission(permissionTransferAll, this);
            permission.RegisterPermission(permissionWithdraw, this);
            permission.RegisterPermission(permissionWithdrawAll, this);
            permission.RegisterPermission(permissionWipe, this);

            // Load existing data and migrate old data format
            MigrateLegacyEconomicsDataFilesIfNeeded();
            InitializeEconomicsDataPaths();

            storedData = new StoredData();
            _sharedBalances = CreateSharedBalanceStoreOrNull();

            if (_sharedBalances != null)
            {
                try
                {
                    _sharedBalances.EnsureSchemaAndLoad(storedData);
                    Puts($"Economics: shared balance storage ({config.BalanceStorageMode}) loaded {storedData.Players.Count} account(s).");
                }
                catch (Exception ex)
                {
                    PrintError($"Economics: shared balance storage failed ({ex.Message}). No balances loaded.");
                    DisableSharedBalancesAndFallBackToFile(ex.Message);
                }

                if (_sharedBalances != null && storedData.Players.Count == 0)
                {
                    LoadOrMigrateBalanceDataFromJson();
                    if (storedData.Players.Count > 0)
                    {
                        Puts("Economics: imported balances from Economics.json into shared storage.");
                        MarkAllPlayersDirty();
                    }
                }
            }

            if (_sharedBalances == null)
            {
                if (storedData.Players.Count == 0)
                    LoadOrMigrateBalanceDataFromJson();
            }

            List<string> playerData = new List<string>(storedData.Players.Keys);

            // Check for and set any balances over maximum allowed
            if (config.BalanceLimit > 0)
            {
                foreach (string p in playerData)
                {
                    if (storedData.Players[p].Balance > config.BalanceLimit)
                    {
                        storedData.Players[p].Balance = config.BalanceLimit;
                        MarkDataChanged();
                    }
                }
            }

            // Check for and remove any inactive player balance data
            if (config.RemoveUnused)
            {
                foreach (string p in playerData)
                {
                    if (storedData.Players[p].Balance.Equals(config.StartingBalance))
                    {
                        storedData.Players.Remove(p);
                        _sharedBalances?.Delete(p);
                        MarkAllPlayersDirty();
                    }
                }
            }

            // Purge inactive accounts if configured
            PurgeInactiveAccountsIfEnabled();

            // File mode: rewrite Oxide Balances JSON into Players shape on first load.
            // Sqlite mode: flush imported/dirty rows.
            if (changed)
            {
                SaveData();
            }
        }

        public void OnNewSave()
        {
            // Send Discord wipe summary report before any data clearing
            if (config.EnableRPMonitoring && !string.IsNullOrEmpty(config.DiscordWebhookUrl))
            {
                SendWipeSummaryReport();
            }

            // Clear RP tracking data on wipe (but preserve player balances)
            if (storedRPTrackingData != null)
            {
                storedRPTrackingData.PlayerRPAcquisitions.Clear();
                storedRPTrackingData.PlayerDailyRP.Clear();
                _rpTrackingDirty = true;
                _rpTrackingForceFullSave = true;
                _dirtyRpTrackingPlayerIds.Clear();
                SaveRPTrackingData();
                PrintWarning("Economics RP tracking data cleared for new wipe");
            }

            // Only wipe player balances if explicitly configured
            if (config.WipeOnNewSave)
            {
                _sharedBalances?.Wipe();
                storedData.Players.Clear();
                MarkDataChanged();
                Interface.Call("OnEconomicsDataWiped");
                PrintWarning("Economics player balances wiped for new save");
            }
            else
            {
                PrintWarning("Economics wipe detected - RP tracking cleared, player balances preserved");
            }
        }

        // Daily summary timer
        private Timer dailySummaryTimer;
        
        // Periodic report timer (every 6 hours)
        private Timer periodicReportTimer;

        private void SetupDailySummaryTimer()
        {
            if (!config.EnableRPMonitoring || string.IsNullOrEmpty(config.DiscordWebhookUrl))
                return;

            try
            {
                // Parse daily reset time
                var timeParts = config.DailyResetTime.Split(':');
                int hour = int.Parse(timeParts[0]);
                int minute = timeParts.Length > 1 ? int.Parse(timeParts[1]) : 0;

                var now = DateTime.UtcNow;
                var nextReset = new DateTime(now.Year, now.Month, now.Day, hour, minute, 0, DateTimeKind.Utc);
                
                // If the time has already passed today, schedule for tomorrow
                if (nextReset <= now)
                {
                    nextReset = nextReset.AddDays(1);
                }

                var timeUntilReset = nextReset - now;
                dailySummaryTimer = timer.Once((float)timeUntilReset.TotalSeconds, () =>
                {
                    SendDailySummaryReport();
                    // Schedule next day's report
                    timer.Once(24 * 60 * 60, SendDailySummaryReport);
                });

                Puts($"[Economics] Daily summary scheduled for {nextReset:yyyy-MM-dd HH:mm:ss} UTC");
            }
            catch (Exception ex)
            {
                LogError($"Failed to setup daily summary timer: {ex.Message}");
            }
        }

        private void SendDailySummaryReport()
        {
            try
            {
                var embedJson = BuildDailySummaryEmbedJson();
                if (!string.IsNullOrEmpty(embedJson))
                {
                    SendDiscordEmbed(embedJson);
                    Puts("[Economics] Daily summary report sent as embed");
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to send daily summary report: {ex.Message}");
            }
        }

        private string FormatDailySummaryReport()
        {
            if (storedRPTrackingData?.PlayerDailyRP == null || storedRPTrackingData.PlayerDailyRP.Count == 0)
            {
                return "📊 **ECONOMICS DAILY SUMMARY**\n*No RP activity found for today.*";
            }

            var report = new System.Text.StringBuilder();
            report.AppendLine("📊 **ECONOMICS DAILY SUMMARY REPORT**");
            report.AppendLine($"*{DateTime.UtcNow:MM-dd-yyyy} - Daily RP Activity*");
            report.AppendLine();

            // Calculate totals for today
            double totalRPEarned = 0;
            int activePlayers = 0;
            var playerTotals = new Dictionary<string, (double totalRP, Dictionary<string, double> sourceBreakdown, string playerName)>();

            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");

            foreach (var playerData in storedRPTrackingData.PlayerDailyRP)
            {
                string playerId = playerData.Key;
                var dailyData = playerData.Value;
                
                if (dailyData.Date != today || dailyData.TotalRP <= 0) continue;

                totalRPEarned += dailyData.TotalRP;
                activePlayers++;

                // Get player name
                var player = players.FindPlayerById(playerId);
                string playerName = player?.Name ?? "Unknown Player";
                
                playerTotals[playerId] = (dailyData.TotalRP, dailyData.SourceBreakdown, playerName);
            }

            if (playerTotals.Count == 0)
            {
                return "📊 **ECONOMICS DAILY SUMMARY**\n*No RP activity found for today.*";
            }

            var sortedPlayers = SortByTotalRpDesc(playerTotals, 10);

            report.AppendLine("**💰 TOP RP EARNERS TODAY**");
            for (int i = 0; i < sortedPlayers.Count; i++)
            {
                var kvp = sortedPlayers[i];
                var (totalRP, sourceBreakdown, playerName) = kvp.Value;
                report.AppendLine($"**{playerName}** ({kvp.Key})");
                report.AppendLine($"- Total RP: {totalRP:F2}");
                
                // Show source breakdown if available
                if (sourceBreakdown.Count > 1)
                {
                    report.AppendLine("- Sources:");
                    var sortedSources = SortDictByValueDesc(sourceBreakdown);
                    for (int s = 0; s < sortedSources.Count; s++)
                    {
                        var source = sortedSources[s];
                        string sourceName = source.Key switch
                        {
                            "Shop" => "🛒 Shop",
                            "Transfer" => "💸 Transfer",
                            "Direct" => "💰 Direct",
                            _ => $"📊 {source.Key}"
                        };
                        report.AppendLine($"  {sourceName}: {source.Value:F2}");
                    }
                }
                report.AppendLine();
            }

            // Add daily totals
            report.AppendLine("**📈 DAILY TOTALS**");
            report.AppendLine($"- Total RP Earned: {totalRPEarned:F2}");
            report.AppendLine($"- Active Players: {activePlayers}");
            report.AppendLine($"- Average RP per Player: {(activePlayers > 0 ? totalRPEarned / activePlayers : 0):F2}");

            return report.ToString();
        }

        private string BuildDailySummaryEmbedJson()
        {
            if (storedRPTrackingData?.PlayerDailyRP == null || storedRPTrackingData.PlayerDailyRP.Count == 0)
            {
                var embedEmpty = new FancyMessage.Embed("📊 ECONOMICS DAILY SUMMARY", "No RP activity found for today.");
                return new FancyMessage(null, new List<FancyMessage.Embed> { embedEmpty }).ToJson();
            }

            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");

            double totalRPEarned = 0;
            int activePlayers = 0;
            var playerTotals = new Dictionary<string, (double totalRP, Dictionary<string, double> sourceBreakdown, string playerName)>();

            foreach (var kv in storedRPTrackingData.PlayerDailyRP)
            {
                var daily = kv.Value;
                if (daily.Date != today || daily.TotalRP <= 0) continue;
                totalRPEarned += daily.TotalRP;
                activePlayers++;
                var player = players.FindPlayerById(kv.Key);
                string playerName = player?.Name ?? "Unknown Player";
                playerTotals[kv.Key] = (daily.TotalRP, daily.SourceBreakdown, playerName);
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"*{DateTime.UtcNow:MM-dd-yyyy} - Daily RP Activity*");
            sb.AppendLine();
            if (playerTotals.Count == 0)
            {
                sb.AppendLine("No RP activity found for today.");
            }
            else
            {
                sb.AppendLine("**💰 TOP RP EARNERS TODAY**");
                var topPlayers = SortByTotalRpDesc(playerTotals, 10);
                for (int i = 0; i < topPlayers.Count; i++)
                {
                    var kv = topPlayers[i];
                    var (rp, breakdown, name) = kv.Value;
                    sb.AppendLine($"**{name}** ({kv.Key})");
                    sb.AppendLine($"- Total RP: {rp:F2}");
                    if (breakdown != null && breakdown.Count > 1)
                    {
                        sb.AppendLine("- Sources:");
                        var sortedSources = SortDictByValueDesc(breakdown);
                        for (int si = 0; si < sortedSources.Count; si++)
                        {
                            var s = sortedSources[si];
                            string srcName = s.Key == "Shop" ? "🛒 Shop" : s.Key == "Transfer" ? "💸 Transfer" : s.Key == "Direct" ? "💰 Direct" : $"📊 {s.Key}";
                            sb.AppendLine($"  {srcName}: {s.Value:F2}");
                        }
                    }
                    sb.AppendLine();
                }

                sb.AppendLine("**📈 DAILY TOTALS**");
                sb.AppendLine($"- Total RP Earned: {totalRPEarned:F2}");
                sb.AppendLine($"- Active Players: {activePlayers}");
                sb.AppendLine($"- Average RP per Player: {(activePlayers > 0 ? totalRPEarned / activePlayers : 0):F2}");
            }

            var embed = new FancyMessage.Embed("📊 ECONOMICS DAILY SUMMARY REPORT", sb.ToString());
            return new FancyMessage(null, new List<FancyMessage.Embed> { embed }).ToJson();
        }

        private string BuildRPAlertEmbedJson(string emoji, string playerName, string playerId, double totalRP, double latestAmount, string sourceInfo, Dictionary<string, double> sourceBreakdown)
        {
            var sb = new System.Text.StringBuilder();
            string timeFrame = config.UseDailyCycles ? "24h (Daily)" : $"{config.MonitoringWindowHours}h";
            sb.AppendLine($"**Player:** {playerName} ({playerId})");
            sb.Append(sourceInfo);
            sb.AppendLine($"**Total RP in {timeFrame}:** {totalRP:F2}");
            sb.AppendLine($"**Latest Deposit:** {latestAmount:F2}");
            sb.AppendLine($"**Threshold:** {config.RPThreshold:F2}");
            if (sourceBreakdown != null && sourceBreakdown.Count > 1)
            {
                sb.AppendLine();
                sb.AppendLine("**Daily Source Breakdown:**");
                var sortedSources = SortDictByValueDesc(sourceBreakdown);
                for (int i = 0; i < sortedSources.Count; i++)
                {
                    var kv = sortedSources[i];
                    string sourceName = kv.Key == "Shop" ? "🛒 Shop Sales" : kv.Key == "Transfer" ? "💸 Transfers" : kv.Key == "Direct" ? "💰 Direct Deposits" : $"📊 {kv.Key}";
                    sb.AppendLine($"{sourceName}: {kv.Value:F2} RP");
                }
            }
            sb.AppendLine($"**Time:** {DateTime.UtcNow:MM-dd-yyyy hh:mm tt} UTC");

            var embed = new FancyMessage.Embed($"{emoji} RP Accumulation Alert {emoji}", sb.ToString());
            var msg = new FancyMessage(null, new List<FancyMessage.Embed> { embed });
            return msg.ToJson();
        }

        private void SetupPeriodicReportsTimer()
        {
            if (!config.EnablePeriodicReports || !config.EnableRPMonitoring || string.IsNullOrEmpty(config.DiscordWebhookUrl))
                return;

            try
            {
                // Calculate time until next 6-hour interval
                var now = DateTime.UtcNow;
                var nextReport = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
                
                // Find the next 6-hour interval (00:00, 06:00, 12:00, 18:00)
                while (nextReport <= now)
                {
                    nextReport = nextReport.AddHours(config.PeriodicReportIntervalHours);
                }

                var timeUntilNext = nextReport - now;
                periodicReportTimer = timer.Once((float)timeUntilNext.TotalSeconds, () =>
                {
                    SendPeriodicReport();
                    // Schedule next report
                    timer.Repeat(config.PeriodicReportIntervalHours * 60 * 60, 0, SendPeriodicReport);
                });

                Puts($"[Economics] Periodic reports scheduled every {config.PeriodicReportIntervalHours} hours, next at {nextReport:yyyy-MM-dd HH:mm:ss} UTC");
            }
            catch (Exception ex)
            {
                LogError($"Failed to setup periodic reports timer: {ex.Message}");
            }
        }

        private void SendPeriodicReport()
        {
            try
            {
                // Build embed payload modeled after XDQuest FancyMessage
                var embedPayloadJson = BuildPeriodicReportEmbedJson();
                if (!string.IsNullOrEmpty(embedPayloadJson))
                {
                    // Prefer direct webhook with embed JSON
                    SendDiscordEmbed(embedPayloadJson);
                    Puts("[Economics] Periodic report sent as embed");
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to send periodic report: {ex.Message}");
            }
        }

        private string FormatPeriodicReport()
        {
            EnsureRpTrackingFullyLoaded();

            if (storedRPTrackingData?.PlayerDailyRP == null || storedRPTrackingData.PlayerDailyRP.Count == 0)
            {
                return "🗂️ **PERIODIC RP REPORT**\n\n*No RP activity found in the last 6 hours.*";
            }

            var report = new System.Text.StringBuilder();
            report.AppendLine("🗂️ **PERIODIC RP REPORT**");
            report.AppendLine();
            
            // Calculate time since last report based on configured interval
            var intervalHours = Math.Max(1, config.PeriodicReportIntervalHours);
            var sixHoursAgo = DateTime.UtcNow.AddHours(-intervalHours);
            var now = DateTime.UtcNow;
            var duration = now - sixHoursAgo;
            
            // Display period with requested orientation and formatting
            string startDateStr = sixHoursAgo.ToString("MM-dd-yyyy");
            string endDateTimeStr = now.ToString("MM-dd-yyyy hh:mm tt");
            report.AppendLine($"**Report Period:** {startDateStr} to {endDateTimeStr} UTC");
            report.AppendLine($"**Duration:** {duration.Days}d {duration.Hours}h {duration.Minutes}m");
            report.AppendLine();

            // Calculate totals for the period
            double totalRPEarned = 0;
            int activePlayers = 0;
            var playerTotals = new Dictionary<string, (double totalRP, Dictionary<string, double> sourceBreakdown, string playerName)>();

            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");

            foreach (var playerData in storedRPTrackingData.PlayerDailyRP)
            {
                string playerId = playerData.Key;
                var dailyData = playerData.Value;
                
                if (dailyData.Date != today || dailyData.TotalRP <= 0) continue;

                totalRPEarned += dailyData.TotalRP;
                activePlayers++;

                // Get player name
                var player = players.FindPlayerById(playerId);
                string playerName = player?.Name ?? "Unknown Player";
                
                playerTotals[playerId] = (dailyData.TotalRP, dailyData.SourceBreakdown, playerName);
            }

            if (playerTotals.Count == 0)
            {
                return "🗂️ **PERIODIC RP REPORT**\n\n*No RP activity found in the last 6 hours.*";
            }

            report.AppendLine($"**Total RP Earned:** {totalRPEarned:F2}");
            report.AppendLine($"**Active Players:** {activePlayers}");
            report.AppendLine();

            var sortedPlayers = SortByTotalRpDesc(playerTotals, 10);

            report.AppendLine("🏆 **TOP RP ACCUMULATORS**");
            for (int i = 0; i < sortedPlayers.Count; i++)
            {
                var kvp = sortedPlayers[i];
                var (totalRP, sourceBreakdown, playerName) = kvp.Value;
                report.AppendLine($"**{playerName}** ({kvp.Key}) - {totalRP:F0}");
                
                // Show source breakdown for each player
                if (sourceBreakdown != null && sourceBreakdown.Count > 0)
                {
                    var sortedSources = SortDictByValueDesc(sourceBreakdown);
                    for (int s = 0; s < sortedSources.Count; s++)
                    {
                        var source = sortedSources[s];
                        string sourceName = source.Key switch
                        {
                            "Shop" => "Shop",
                            "Transfer" => "Transfer",
                            "Direct" => "Direct Deposit",
                            _ => source.Key
                        };
                        report.AppendLine($" - {sourceName} - {source.Value:F0}");
                    }
                }
                report.AppendLine();
            }

            return report.ToString();
        }

        #endregion Initialization

        #region API Methods

        public double Balance(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                LogWarning("Balance method called without a valid player ID");
                return 0.0;
            }

            bool created = !storedData.Players.ContainsKey(playerId);
            if (!storedData.Players.ContainsKey(playerId))
            {
                storedData.Players[playerId] = new PlayerData { Balance = config.StartingBalance };
            }

            if (_sharedBalances != null && created)
            {
                SharedUpsertPlayer(playerId);
            }

            return storedData.Players[playerId].Balance;
        }
        
        public double Balance(object playerId) => Balance(GetUserId(playerId));
        
        private string GetUserId(object playerId)
        {
            if (playerId == null)
                throw new ArgumentException("Invalid player ID, playerId must be a valid SteamID of type ulong or string");

            if (playerId is string sid)
            {
                if (string.IsNullOrEmpty(sid) || !sid.IsSteamId())
                    throw new ArgumentException("Invalid player ID, playerId must be a valid SteamID of type ulong or string");
                return sid;
            }

            if (playerId is ulong uid)
                return uid.ToString();

            if (playerId is EncryptedValue<ulong> enc)
                return ((ulong)enc).ToString();

            if (playerId is BasePlayer bp)
                return bp.UserIDString;

            if (playerId is IPlayer ip && !string.IsNullOrEmpty(ip.Id))
                return ip.Id;

            string userId = playerId.ToString();
            if (string.IsNullOrEmpty(userId) || !userId.IsSteamId())
                throw new ArgumentException("Invalid player ID, playerId must be a valid SteamID of type ulong or string");

            return userId;
        }
        
        public bool Deposit(string playerId, double amount)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                LogWarning("Deposit method called without a valid player ID");
                return false;
            }

            if (amount > 0 && amount < 1 && amount != Math.Floor(amount))
                amount = 0;

            if (amount > 0 && SetBalance(playerId, amount + Balance(playerId)))
            {
                UpdateLastSeen(playerId); // Track activity on deposit
                
               // Track RP acquisition for monitoring
               if (config.EnableRPMonitoring && config.RPThreshold > 0)
               {
                   string source = "Direct";
                   if (config.DepositStackWalkForSource)
                   {
                       try
                       {
                           var stackTrace = new System.Diagnostics.StackTrace();
                           for (int i = 0; i < stackTrace.FrameCount; i++)
                           {
                               var frame = stackTrace.GetFrame(i);
                               var method = frame.GetMethod();
                               if (method != null && method.DeclaringType != null)
                               {
                                   string className = method.DeclaringType.Name;
                                   string methodName = method.Name;

                                   if (config.EnableDebugLogging && i < 3)
                                   {
                                       Puts($"[Economics Debug] Stack frame {i}: {className}.{methodName}");
                                   }

                                   if (className.Contains("RustRewards") ||
                                       className.Contains("RustReward"))
                                   {
                                       source = "RustRewards";
                                       if (config.EnableDebugLogging)
                                       {
                                           Puts($"[Economics Debug] Detected RustRewards source: {className}.{methodName}");
                                       }

                                       break;
                                   }

                                   if (className.Contains("Shop") ||
                                       className.Contains("ShopItem") ||
                                       methodName.Contains("Shop") ||
                                       methodName.Contains("Sell") ||
                                       methodName.Contains("Economy") ||
                                       (className.Contains("EconomyEntry") && methodName == "Add"))
                                   {
                                       source = "Shop";
                                       if (config.EnableDebugLogging)
                                       {
                                           Puts($"[Economics Debug] Detected Shop source: {className}.{methodName}");
                                       }

                                       break;
                                   }
                               }
                           }
                       }
                       catch (Exception ex)
                       {
                           if (config.EnableDebugLogging)
                           {
                               Puts($"[Economics Debug] Exception in call stack analysis: {ex.Message}");
                           }
                       }
                   }

                   if (config.EnableDebugLogging)
                   {
                       Puts($"[Economics Debug] Final source for {playerId}: {source} (amount: {amount})");
                   }

                   TrackRPAcquisition(playerId, amount, source);
               }
                
                Interface.Call("OnEconomicsDeposit", playerId, amount);

                if (config.LogTransactions)
                {
                    LogToFile("transactions", $"[{DateTime.Now}] {GetLang("LogDeposit", null, FormatMoney(amount), playerId)}", this);
                }

                return true;
            }

            return false;
        }

        public bool Deposit(object playerId, double amount) => Deposit(GetUserId(playerId), amount);

        public bool SetBalance(string playerId, double amount)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                LogWarning("SetBalance method called without a valid player ID");
                return false;
            }

            if (amount >= 0 || config.AllowNegativeBalance)
            {
                amount = Math.Round(amount, 2);
                if (config.BalanceLimit > 0 && amount > config.BalanceLimit)
                {
                    amount = config.BalanceLimit;
                }
                else if (config.AllowNegativeBalance && config.NegativeBalanceLimit < 0 && amount < config.NegativeBalanceLimit)
                {
                    amount = config.NegativeBalanceLimit;
                }

                // Ensure player data exists
                if (!storedData.Players.ContainsKey(playerId))
                {
                    storedData.Players[playerId] = new PlayerData();
                }

                storedData.Players[playerId].Balance = amount;
                UpdateLastSeen(playerId); // Track activity when balance changes
                MarkDataChanged(playerId);
                SharedUpsertPlayer(playerId);

                Interface.Call("OnEconomicsBalanceUpdated", playerId, amount);
                Interface.CallDeprecatedHook("OnBalanceChanged", "OnEconomicsBalanceUpdated", new System.DateTime(2022, 7, 1), playerId, amount);

                if (config.LogTransactions)
                {
                    LogToFile("transactions", $"[{DateTime.Now}] {GetLang("LogSetBalance", null, FormatMoney(amount), playerId)}", this);
                }

                return true;
            }

            return false;
        }

        public bool SetBalance(object playerId, double amount) => SetBalance(GetUserId(playerId), amount);

        public bool Transfer(string playerId, string targetId, double amount)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                LogWarning("Transfer method called without a valid player ID");
                return false;
            }

            if (Withdraw(playerId, amount) && Deposit(targetId, amount))
            {
                // Track transfer for monitoring (only the recipient gets the RP)
                if (config.EnableRPMonitoring && config.RPThreshold > 0)
                {
                    TrackRPAcquisition(targetId, amount, "Transfer", playerId);
                }
                
                Interface.Call("OnEconomicsTransfer", playerId, targetId, amount);

                if (config.LogTransactions)
                {
                    LogToFile("transactions", $"[{DateTime.Now}] {GetLang("LogTransfer", null, FormatMoney(amount), targetId, playerId)}", this);
                }

                return true;
            }

            return false;
        }

        public bool Transfer(object playerId, ulong targetId, double amount)
        {
            return Transfer(GetUserId(playerId), targetId.ToString(), amount);
        }

        public bool Withdraw(string playerId, double amount)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                LogWarning("Withdraw method called without a valid player ID");
                return false;
            }

            if (amount >= 0 || config.AllowNegativeBalance)
            {
                double balance = Balance(playerId);
                if ((balance >= amount || (config.AllowNegativeBalance && balance + amount > config.NegativeBalanceLimit)) && SetBalance(playerId, balance - amount))
                {
                    UpdateLastSeen(playerId); // Track activity on withdrawal
                    Interface.Call("OnEconomicsWithdrawl", playerId, amount);

                    if (config.LogTransactions)
                    {
                        LogToFile("transactions", $"[{DateTime.Now}] {GetLang("LogWithdrawl", null, FormatMoney(amount), playerId)}", this);
                    }

                    return true;
                }
            }

            return false;
        }

        public bool Withdraw(object playerId, double amount) => Withdraw(GetUserId(playerId), amount);

        #endregion API Methods

        private void PurgeInactiveAccountsIfEnabled()
        {
            if (config.PurgeAfterDays <= 0)
            {
                return;
            }

            double cutoff = DateTimeOffset.UtcNow.AddDays(-config.PurgeAfterDays).ToUnixTimeSeconds();
            List<string> accountsToRemove = new List<string>();

            foreach (var kvp in storedData.Players)
            {
                string playerId = kvp.Key;
                PlayerData playerData = kvp.Value;

                // Check if player has been inactive
                if (playerData.LastSeen > 0 && playerData.LastSeen < cutoff)
                {
                    accountsToRemove.Add(playerId);
                }
                else if (playerData.LastSeen == 0)
                {
                    // No lastSeen data - try PlaytimeTracker as fallback
                    if (config.UsePlaytimeTracker)
                    {
                        try
                        {
                            var playtimeTracker = plugins.Find("PlaytimeTracker");
                            if (playtimeTracker != null)
                            {
                                object result = playtimeTracker.Call("GetLastSeen", playerId);
                                if (result != null)
                                {
                                    double lastSeen = Convert.ToDouble(result);
                                    if (lastSeen > 0 && lastSeen < cutoff)
                                    {
                                        accountsToRemove.Add(playerId);
                                    }
                                }
                                else
                                {
                                    // No tracking data available - remove the account
                                    accountsToRemove.Add(playerId);
                                }
                            }
                            else
                            {
                                // PlaytimeTracker not available - remove the account
                                accountsToRemove.Add(playerId);
                            }
                        }
                        catch (Exception ex)
                        {
                            if (config.EnableDebugLogging)
                            {
                                Puts($"[Economics Debug] Failed to get last seen from PlaytimeTracker for purge: {ex.Message}");
                            }
                            // If we can't get lastSeen from anywhere, remove the account
                            accountsToRemove.Add(playerId);
                        }
                    }
                    else
                    {
                        // No tracking data available - remove the account
                        accountsToRemove.Add(playerId);
                    }
                }
            }

            if (accountsToRemove.Count > 0)
            {
                foreach (string id in accountsToRemove)
                {
                    storedData.Players.Remove(id);
                    _sharedBalances?.Delete(id);
                }
                MarkDataChanged();
                Puts($"Purged {accountsToRemove.Count} inactive account(s) older than {config.PurgeAfterDays} days.");
            }
        }

        #region Commands

        #region Balance Command

        public void CommandBalance(IPlayer player, string command, string[] args)
        {
            if (args != null && args.Length > 0)
            {
                if (!player.HasPermission(permissionBalance))
                {
                    Message(player, "NotAllowed", command);
                    return;
                }

                IPlayer target = FindPlayer(args[0], player);
                if (target == null)
                {
                    Message(player, "UsageBalance", command);
                    return;
                }

                Message(player, "PlayerBalance", target.Name, FormatMoney(Balance(target.Id)));
                return;
            }

            if (player.IsServer)
            {
                Message(player, "UsageBalanceOthers", command);
            }
            else
            {
                Message(player, "YourBalance", FormatMoney(Balance(player.Id)));
            }
        }

        [ChatCommand("testdiscord")]
        public void CommandTestDiscord(IPlayer player, string command, string[] args)
        {
            if (player == null) return;

            string playerId;
            if (player.IsServer)
            {
                // Use a test player ID for console
                playerId = "76561197967147516"; // Your Steam ID
            }
            else
            {
                // For in-game players, use their ID directly
                playerId = player.Id;
            }
            
            // Test DiscordMessages plugin directly
            var discordPlugin = plugins.Find("DiscordMessages");
            if (discordPlugin != null)
            {
                Puts($"[Economics] DiscordMessages plugin found: {discordPlugin.Name} v{discordPlugin.Version}");
                
                // Test with a simple message first
                string simpleMessage = "🧪 **Economics Test** - Direct API Call";
                discordPlugin.Call("API_SendTextMessage", "https://discord.com/api/webhooks/847964844619726870/eqcEr0aKlcQ8QrFaQK8x7wanIx21DV3OrBaIqvtNP2y1Ug0TbcSyTH1aB3tgXL7iOEje", simpleMessage, false, this);
                Puts($"[Economics] Simple test message sent");
                
                // Wait a moment then send the full test
                timer.Once(2f, () => {
                    SendDiscordNotification(playerId, 10000.0, 10000.0, "Test", "");
                    SendSimpleDiscordTest();
                });
            }
            else
            {
                Puts($"[Economics] DiscordMessages plugin NOT FOUND!");
            }
            
            Message(player, "Test Discord notification sent!");
        }

        [ChatCommand("testdiscorddirect")]
        public void CommandTestDiscordDirect(IPlayer player, string command, string[] args)
        {
            if (player == null) return;

            // Test DiscordMessages plugin using its own message command
            var discordPlugin = plugins.Find("DiscordMessages");
            if (discordPlugin != null)
            {
                Puts($"[Economics] Testing DiscordMessages with its own message command...");
                
                // Try to call the message command directly
                string testMessage = "🧪 **Economics Direct Test** - Using DiscordMessages message command";
                discordPlugin.Call("MessageCommand", player, "message", new string[] { testMessage });
                
                Puts($"[Economics] Called DiscordMessages message command");
            }
            else
            {
                Puts($"[Economics] DiscordMessages plugin NOT FOUND!");
            }
            
            Message(player, "Direct Discord test sent!");
        }

        [ChatCommand("testwebhook")]
        public void CommandTestWebhook(IPlayer player, string command, string[] args)
        {
            if (player == null) return;

            // Test the webhook URL directly
            string webhookUrl = "https://discord.com/api/webhooks/847964844619726870/eqcEr0aKlcQ8QrFaQK8x7wanIx21DV3OrBaIqvtNP2y1Ug0TbcSyTH1aB3tgXL7iOEje";
            
            Puts($"[Economics] Testing webhook URL: {webhookUrl}");
            
            // Test with a simple message
            string testMessage = "🧪 **Webhook Test** - Direct webhook call from Economics";
            
            try
            {
                webrequest.Enqueue(webhookUrl, $"{{\"content\":\"{testMessage}\"}}", (code, response) => {
                    Puts($"[Economics] Webhook response code: {code}");
                    if (code != 200 && code != 204)
                    {
                        Puts($"[Economics] Webhook error response: {response}");
                    }
                    else
                    {
                        Puts($"[Economics] Webhook test successful!");
                    }
                }, this, RequestMethod.POST, new Dictionary<string, string> { ["Content-Type"] = "application/json" });
            }
            catch (Exception ex)
            {
                Puts($"[Economics] Webhook test failed: {ex.Message}");
            }
            
            Message(player, "Webhook test initiated!");
        }
        
        private void SendSimpleDiscordTest()
        {
            try
            {
                string message = "🔔 **Signal Received** - Economics Plugin Test\n" +
                               $"**Time:** {DateTime.Now:yyyy-MM-dd HH:mm:ss} UTC\n" +
                               "**Status:** Discord integration working!";

                // Use DiscordMessages plugin API with Economics webhook
                var discordPlugin = plugins.Find("DiscordMessages");
                if (discordPlugin != null && !string.IsNullOrEmpty(config.DiscordWebhookUrl))
                {
                    discordPlugin.Call("API_SendTextMessage", config.DiscordWebhookUrl, message, false, this);
                    Puts("[Economics] Test message sent via DiscordMessages API");
                }
                else if (!string.IsNullOrEmpty(config.DiscordWebhookUrl))
                {
                    // Fallback to direct webhook call
                    SendWebhookMessage(message);
                    Puts("[Economics] Test message sent via direct webhook");
                }
                else
                {
                    Puts("[Economics] No Discord webhook configured!");
                }
            }
            catch (Exception ex)
            {
                Puts($"[Economics] Failed to send test message: {ex.Message}");
            }
        }

        #endregion Balance Command

        #region Deposit Command

        public void CommandDeposit(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(permissionDeposit))
            {
                Message(player, "NotAllowed", command);
                return;
            }

            if (args == null || args.Length <= 1)
            {
                Message(player, "UsageDeposit", command);
                return;
            }

            double amount;
            double.TryParse(args[1], out amount);
            if (amount <= 0)
            {
                Message(player, "ZeroAmount");
                return;
            }

            if (args[0] == "*")
            {
                if (!player.HasPermission(permissionDepositAll))
                {
                    Message(player, "NotAllowed", command);
                    return;
                }

                int receivers = 0;
                var depositTargets = new List<string>(storedData.Players.Keys);
                for (int i = 0; i < depositTargets.Count; i++)
                {
                    if (Deposit(depositTargets[i], amount))
                    {
                        receivers++;
                    }
                }
                Message(player, "DepositedToAll", FormatMoney(amount * receivers), FormatMoney(amount), receivers);
            }
            else
            {
                IPlayer target = FindPlayer(args[0], player);
                if (target == null)
                {
                    return;
                }

                if (Deposit(target.Id, amount))
                {
                    Message(player, "PlayerBalance", target.Name, FormatMoney(Balance(target.Id)));
                }
                else
                {
                    Message(player, "TransactionFailed", target.Name);
                }
            }
        }

        #endregion Deposit Command

        #region Set Balance Command

        public void CommandSetBalance(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(permissionSetBalance))
            {
                Message(player, "NotAllowed", command);
                return;
            }

            if (args == null || args.Length <= 1)
            {
                Message(player, "UsageSetBalance", command);
                return;
            }

            double amount;
            double.TryParse(args[1], out amount);

            if (amount < 0)
            {
                Message(player, "NegativeBalance");
                return;
            }

            if (args[0] == "*")
            {
                if (!player.HasPermission(permissionSetBalanceAll))
                {
                    Message(player, "NotAllowed", command);
                    return;
                }

                int receivers = 0;
                var setTargets = new List<string>(storedData.Players.Keys);
                for (int i = 0; i < setTargets.Count; i++)
                {
                    if (SetBalance(setTargets[i], amount))
                    {
                        receivers++;
                    }
                }
                Message(player, "SetBalanceForAll", FormatMoney(amount), receivers);
            }
            else
            {
                IPlayer target = FindPlayer(args[0], player);
                if (target == null)
                {
                    return;
                }

                if (SetBalance(target.Id, amount))
                {
                    Message(player, "PlayerBalance", target.Name, FormatMoney(Balance(target.Id)));
                }
                else
                {
                    Message(player, "TransactionFailed", target.Name);
                }
            }
        }

        #endregion Set Balance Command

        #region Transfer Command

        public void CommandTransfer(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(permissionTransfer))
            {
                Message(player, "NotAllowed", command);
                return;
            }

            if (args == null || args.Length <= 1)
            {
                Message(player, "UsageTransfer", command);
                return;
            }

            double amount;
            double.TryParse(args[1], out amount);

            if (amount <= 0)
            {
                Message(player, "ZeroAmount");
                return;
            }

            if (args[0] == "*")
            {
                if (!player.HasPermission(permissionTransferAll))
                {
                    Message(player, "NotAllowed", command);
                    return;
                }

                if (!Withdraw(player.Id, amount))
                {
                    Message(player, "YouLackMoney");
                    return;
                }

                int receivers = 0;
                foreach (IPlayer _ in players.Connected)
                    receivers++;
                double splitAmount = amount /= receivers;

                foreach (IPlayer target in players.Connected)
                {
                    if (Deposit(target.Id, splitAmount))
                    {
                        if (target.IsConnected)
                        {
                            Message(target, "ReceivedFrom", FormatMoney(splitAmount), player.Name);
                        }
                    }
                }
                Message(player, "TransferredToAll", FormatMoney(amount), FormatMoney(splitAmount), receivers);
            }
            else
            {
                IPlayer target = FindPlayer(args[0], player);
                if (target == null)
                {
                    return;
                }

                if (target.Equals(player))
                {
                    Message(player, "TransferToSelf");
                    return;
                }

                if (!Withdraw(player.Id, amount))
                {
                    Message(player, "YouLackMoney");
                    return;
                }

                if (Deposit(target.Id, amount))
                {
                    Message(player, "TransferredTo", FormatMoney(amount), target.Name);
                    Message(target, "ReceivedFrom", FormatMoney(amount), player.Name);
                }
                else
                {
                    Message(player, "TransactionFailed", target.Name);
                }
            }
        }

        #endregion Transfer Command

        #region Withdraw Command

        public void CommandWithdraw(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(permissionWithdraw))
            {
                Message(player, "NotAllowed", command);
                return;
            }

            if (args == null || args.Length <= 1)
            {
                Message(player, "UsageWithdraw", command);
                return;
            }

            double amount;
            double.TryParse(args[1], out amount);

            if (amount <= 0)
            {
                Message(player, "ZeroAmount");
                return;
            }

            if (args[0] == "*")
            {
                if (!player.HasPermission(permissionWithdrawAll))
                {
                    Message(player, "NotAllowed", command);
                    return;
                }

                int receivers = 0;
                var withdrawTargets = new List<string>(storedData.Players.Keys);
                for (int i = 0; i < withdrawTargets.Count; i++)
                {
                    if (Withdraw(withdrawTargets[i], amount))
                    {
                        receivers++;
                    }
                }
                Message(player, "WithdrawnForAll", FormatMoney(amount * receivers), FormatMoney(amount), receivers);
            }
            else
            {
                IPlayer target = FindPlayer(args[0], player);
                if (target == null)
                {
                    return;
                }

                if (Withdraw(target.Id, amount))
                {
                    Message(player, "PlayerBalance", target.Name, FormatMoney(Balance(target.Id)));
                }
                else
                {
                    Message(player, "YouLackMoney", target.Name);
                }
            }
        }

        #endregion Withdraw Command

        #region Wipe Command

        public void CommandWipe(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(permissionWipe))
            {
                Message(player, "NotAllowed", command);
                return;
            }

            _sharedBalances?.Wipe();
            storedData = new StoredData();
            MarkDataChanged();
            SaveData();

            Message(player, "DataWiped");
            Interface.Call("OnEconomicsDataWiped", player);
        }

        [Command("ecopurge")]
        public void CommandPurge(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(permissionWipe))
            {
                Message(player, "NotAllowed", command);
                return;
            }

            int originalCount = storedData.Players.Count;
            PurgeInactiveAccountsIfEnabled();
            int newCount = storedData.Players.Count;
            
            player.Reply($"Purge complete. Removed {originalCount - newCount} inactive accounts. {newCount} accounts remaining.");
        }

        [Command("ecostats")]
        public void CommandStats(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(permissionBalance))
            {
                Message(player, "NotAllowed", command);
                return;
            }

            int totalAccounts = storedData.Players.Count;
            int trackedAccounts = 0;
            foreach (var p in storedData.Players)
            {
                if (p.Value.LastSeen > 0)
                    trackedAccounts++;
            }
            int untrackedAccounts = totalAccounts - trackedAccounts;
            
            double currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            int inactiveAccounts = 0;
            
            if (config.PurgeAfterDays > 0)
            {
                double cutoff = currentTime - (config.PurgeAfterDays * 24 * 60 * 60);
                foreach (var kvp in storedData.Players)
                {
                    if (kvp.Value.LastSeen > 0 && kvp.Value.LastSeen < cutoff)
                    {
                        inactiveAccounts++;
                    }
                }
            }
            
            player.Reply($"Economics Stats:\n" +
                        $"Total accounts: {totalAccounts}\n" +
                        $"Tracked accounts: {trackedAccounts}\n" +
                        $"Untracked accounts: {untrackedAccounts}\n" +
                        $"Inactive accounts (>100 days): {inactiveAccounts}\n" +
                        $"Purge setting: {config.PurgeAfterDays} days");
        }

        [Command("ecodailysummary")]
        public void CommandDailySummary(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(permissionWipe))
            {
                Message(player, "NotAllowed", command);
                return;
            }

            player.Reply("Sending daily summary report...");
            SendDailySummaryReport();
        }

        [Command("ecoperiodicreport")]
        public void CommandPeriodicReport(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(permissionWipe))
            {
                Message(player, "NotAllowed", command);
                return;
            }

            player.Reply("Sending periodic report...");
            SendPeriodicReport();
        }

        #endregion Wipe Command

        #endregion Commands

        #region Helpers

        private IPlayer FindPlayer(string playerNameOrId, IPlayer player)
        {
            var foundPlayers = new List<IPlayer>();
            foreach (var p in players.FindPlayers(playerNameOrId))
                foundPlayers.Add(p);

            if (foundPlayers.Count > 1)
            {
                var names = new List<string>();
                for (int i = 0; i < foundPlayers.Count && i < 10; i++)
                    names.Add(foundPlayers[i].Name);
                Message(player, "PlayersFound", string.Join(", ", names).Truncate(60));
                return null;
            }

            IPlayer target = foundPlayers.Count == 1 ? foundPlayers[0] : null;
            if (target == null)
            {
                Message(player, "NoPlayersFound", playerNameOrId);
                return null;
            }

            return target;
        }

        private static bool DictionaryKeysEqual(Dictionary<string, object> a, Dictionary<string, object> b)
        {
            if (a == null || b == null) return a == b;
            if (a.Count != b.Count) return false;
            foreach (var key in a.Keys)
            {
                if (!b.ContainsKey(key))
                    return false;
            }
            return true;
        }

        private static List<KeyValuePair<string, double>> SortDictByValueDesc(Dictionary<string, double> dict)
        {
            var list = new List<KeyValuePair<string, double>>(dict);
            list.Sort((x, y) => y.Value.CompareTo(x.Value));
            return list;
        }

        private static List<KeyValuePair<string, (double totalRP, Dictionary<string, double> sourceBreakdown, string playerName)>> SortByTotalRpDesc(
            Dictionary<string, (double totalRP, Dictionary<string, double> sourceBreakdown, string playerName)> dict, int take)
        {
            var list = new List<KeyValuePair<string, (double totalRP, Dictionary<string, double> sourceBreakdown, string playerName)>>(dict);
            list.Sort((x, y) => y.Value.totalRP.CompareTo(x.Value.totalRP));
            if (take >= 0 && list.Count > take)
                list.RemoveRange(take, list.Count - take);
            return list;
        }

        private static List<KeyValuePair<string, (double totalRP, int transactionCount, string playerName)>> SortByTotalRpAndTxDesc(
            Dictionary<string, (double totalRP, int transactionCount, string playerName)> dict, int take)
        {
            var list = new List<KeyValuePair<string, (double totalRP, int transactionCount, string playerName)>>(dict);
            list.Sort((x, y) => y.Value.totalRP.CompareTo(x.Value.totalRP));
            if (take >= 0 && list.Count > take)
                list.RemoveRange(take, list.Count - take);
            return list;
        }

        private void AddLocalizedCommand(string command)
        {
            foreach (string language in lang.GetLanguages(this))
            {
                Dictionary<string, string> messages = lang.GetMessages(language, this);
                foreach (KeyValuePair<string, string> message in messages)
                {
                    if (message.Key.Equals(command))
                    {
                        if (!string.IsNullOrEmpty(message.Value))
                        {
                            AddCovalenceCommand(message.Value, command);
                        }
                    }
                }
            }
        }

        /// <summary>Invariant numeric formatting avoids culture-specific currency symbols that garble in the Windows server console.</summary>
        private static string FormatMoney(double amount)
        {
            return amount.ToString("N2", CultureInfo.InvariantCulture);
        }

        private string GetLang(string langKey, string playerId = null, params object[] args)
        {
            return string.Format(lang.GetMessage(langKey, this, playerId), args);
        }

        private void Message(IPlayer player, string textOrLang, params object[] args)
        {
            if (player == null)
            {
                return;
            }

            string message = GetLang(textOrLang, player.Id, args);
            string text = message != textOrLang ? message : textOrLang;

            if (player.IsConnected)
            {
                player.Reply(text);
                return;
            }

            // Dedicated window / RCON: often !IsConnected but IsServer is false depending on Oxide build
            if (ShouldEchoCommandFeedbackToConsole(player))
            {
                Puts(text);
            }
        }

        /// <summary>
        /// True for server console / RCON invokers. Offline real players keep a Steam64 id — we do not echo those to the log.
        /// </summary>
        private static bool ShouldEchoCommandFeedbackToConsole(IPlayer player)
        {
            if (player.IsConnected)
            {
                return false;
            }

            if (player.IsServer)
            {
                return true;
            }

            string id = player.Id ?? string.Empty;
            if (string.Equals(id, "server", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string name = player.Name ?? string.Empty;
            if (string.Equals(name, "Server", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Anything that is not a normal Steam account id is treated as console/plugin (show feedback).
            return !LooksLikeRustSteam64Id(id);
        }

        private static bool LooksLikeRustSteam64Id(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length < 17)
            {
                return false;
            }

            return ulong.TryParse(id, out ulong v) && v >= 76561197960265728UL;
        }

        #endregion Helpers
    }
}

#region Extension Methods

namespace EconomicsHarmony.EconomicsExtensionMethods
{
    public static class ExtensionMethods
    {
        public static T Clamp<T>(this T val, T min, T max) where T : IComparable<T>
        {
            if (val.CompareTo(min) < 0)
            {
                return min;
            }
            else if (val.CompareTo(max) > 0)
            {
                return max;
            }
            else
            {
                return val;
            }
        }
    }
}

#endregion Extension Methods
