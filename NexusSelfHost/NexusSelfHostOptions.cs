using System;

namespace NexusSelfHost
{
    /// <summary>
    /// Effective options: <b>environment variable wins</b> when set to a non-whitespace value; otherwise JSON from
    /// <see cref="NexusSelfHostConfig"/>.
    /// </summary>
    public static class NexusSelfHostOptions
    {
        private static NexusSelfHostConfigData _cfg = new();
        private static volatile bool _initialized;
        private static readonly object InitLock = new object();

        public static void Initialize(NexusSelfHostConfigData cfg)
        {
            lock (InitLock)
            {
                _cfg = cfg ?? new NexusSelfHostConfigData();
                _initialized = true;
            }
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
                return;
            lock (InitLock)
            {
                if (_initialized)
                    return;
                _cfg = NexusSelfHostConfig.LoadOrCreate();
                _initialized = true;
            }
        }

        public static NexusSelfHostConfigData Raw
        {
            get
            {
                EnsureInitialized();
                return _cfg;
            }
        }

        /// <summary>Verbose Nexus HTTP / SendRequestImpl logging (env <c>NEXUS_DEBUG</c>).</summary>
        public static bool VerboseHttp
        {
            get
            {
                EnsureInitialized();
                if (TryGetEnv("NEXUS_DEBUG", out var e))
                {
                    if (IsExplicitlyFalse(e))
                        return false;
                    if (IsExplicitlyTrue(e))
                        return true;
                    return true;
                }

                return _cfg.Debug.VerboseHttp;
            }
        }

        public static bool LogBlueprintOnConnect
        {
            get
            {
                EnsureInitialized();
                if (TryGetEnv("NEXUS_LOG_BLUEPRINT_CONNECT", out var e))
                    return !string.Equals(e, "0", StringComparison.OrdinalIgnoreCase);
                return _cfg.Debug.LogBlueprintOnConnect;
            }
        }

        /// <summary>Extra log line after PlayerInit resync (vanilla only when <c>NEXUS_DEBUG=1</c>).</summary>
        public static bool LogPlayerInitResync
        {
            get
            {
                EnsureInitialized();
                if (TryGetEnv("NEXUS_DEBUG", out var e))
                    return string.Equals(e.Trim(), "1", StringComparison.OrdinalIgnoreCase);
                return _cfg.Debug.LogPlayerInitResync;
            }
        }

        public static bool LogTransferFix
        {
            get
            {
                EnsureInitialized();
                if (TryGetEnv("NEXUS_LOG_TRANSFER_FIX", out var e))
                    return !string.Equals(e, "0", StringComparison.OrdinalIgnoreCase);
                return _cfg.Debug.LogTransferFix;
            }
        }

        public static bool LogTransferEntry
        {
            get
            {
                EnsureInitialized();
                if (TryGetEnv("NEXUS_LOG_TRANSFER_ENTRY", out var e))
                    return !string.Equals(e, "0", StringComparison.OrdinalIgnoreCase);
                return _cfg.Debug.LogTransferEntry;
            }
        }

        public static bool VerboseTransferFix
        {
            get
            {
                EnsureInitialized();
                if (TryGetEnv("NEXUS_VERBOSE_TRANSFER_FIX", out var e))
                {
                    if (IsExplicitlyFalse(e))
                        return false;
                    if (IsExplicitlyTrue(e))
                        return true;
                }
                return _cfg.Debug.VerboseTransferFix;
            }
        }

        public static bool OutpostTransferEnabled
        {
            get
            {
                EnsureInitialized();
                if (TryGetEnv("NEXUS_TRANSFER_OUTPOST_ENABLED", out var e))
                    return !string.Equals(e, "0", StringComparison.OrdinalIgnoreCase);
                return _cfg.OutpostTransfer.Enabled;
            }
        }

        public static bool PortalTransferEnabled
        {
            get
            {
                EnsureInitialized();
                if (TryGetEnv("NEXUS_TRANSFER_PORTAL_ENABLED", out var e))
                    return !string.Equals(e, "0", StringComparison.OrdinalIgnoreCase);
                return _cfg.PortalTransfer.Enabled;
            }
        }

        public static bool PortalTransferForceConsoleTransfers
        {
            get
            {
                EnsureInitialized();
                if (TryGetEnv("NEXUS_TRANSFER_PORTAL_FORCE_CONSOLE", out var e))
                    return !string.Equals(e, "0", StringComparison.OrdinalIgnoreCase);
                return _cfg.PortalTransfer.ForceConsoleTransfers;
            }
        }

        public static bool PortalTransferUsePortalRotation
        {
            get
            {
                EnsureInitialized();
                if (TryGetEnv("NEXUS_TRANSFER_PORTAL_USE_ROTATION", out var e))
                    return !string.Equals(e, "0", StringComparison.OrdinalIgnoreCase);
                return _cfg.PortalTransfer.UsePortalRotation;
            }
        }

        public static float PortalTransferForwardOffsetMeters
        {
            get
            {
                EnsureInitialized();
                if (TryGetEnv("NEXUS_TRANSFER_PORTAL_FORWARD_OFFSET", out var e) &&
                    float.TryParse(e, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var v))
                    return v;
                return _cfg.PortalTransfer.ForwardOffsetMeters;
            }
        }

        public static float PortalTransferUpOffsetMeters
        {
            get
            {
                EnsureInitialized();
                if (TryGetEnv("NEXUS_TRANSFER_PORTAL_UP_OFFSET", out var e) &&
                    float.TryParse(e, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var v))
                    return v;
                return _cfg.PortalTransfer.UpOffsetMeters;
            }
        }

        public static bool OutpostTransferForceConsoleTransfers
        {
            get
            {
                EnsureInitialized();
                if (TryGetEnv("NEXUS_TRANSFER_OUTPOST_FORCE_CONSOLE", out var e))
                    return !string.Equals(e, "0", StringComparison.OrdinalIgnoreCase);
                return _cfg.OutpostTransfer.ForceConsoleTransfers;
            }
        }

        public static bool OutpostTransferUseMonumentRotation
        {
            get
            {
                EnsureInitialized();
                if (TryGetEnv("NEXUS_TRANSFER_OUTPOST_USE_ROTATION", out var e))
                    return !string.Equals(e, "0", StringComparison.OrdinalIgnoreCase);
                return _cfg.OutpostTransfer.UseMonumentRotation;
            }
        }

        public static float OutpostTransferDefaultHeightAboveRoot
        {
            get
            {
                EnsureInitialized();
                if (TryGetEnv("NEXUS_TRANSFER_OUTPOST_DEFAULT_HEIGHT", out var e) &&
                    float.TryParse(e, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var v))
                    return v;
                return _cfg.OutpostTransfer.DefaultHeightAboveRoot;
            }
        }

        public static Vector3Config OutpostTransferLocalOffset
        {
            get
            {
                EnsureInitialized();
                return _cfg.OutpostTransfer.LocalOffset ?? new Vector3Config();
            }
        }

        public static bool OutpostTransferReferencePointEnabled
        {
            get
            {
                EnsureInitialized();
                return _cfg.OutpostTransfer.ReferencePointEnabled;
            }
        }

        public static string OutpostTransferReferenceZoneKey
        {
            get
            {
                EnsureInitialized();
                return _cfg.OutpostTransfer.ReferenceZoneKey ?? string.Empty;
            }
        }

        public static Vector3Config OutpostTransferReferenceWorldPosition
        {
            get
            {
                EnsureInitialized();
                return _cfg.OutpostTransfer.ReferenceWorldPosition ?? new Vector3Config();
            }
        }

        public static bool NotifyPlayerDisconnect
        {
            get
            {
                EnsureInitialized();
                if (TryGetEnv("NEXUS_NOTIFY_PLAYER_DISCONNECT", out var e))
                    return !string.Equals(e, "0", StringComparison.OrdinalIgnoreCase);
                return _cfg.Nexus.NotifyPlayerDisconnect;
            }
        }

        public static bool GuardEmptyTransfer
        {
            get
            {
                EnsureInitialized();
                if (TryGetEnv("NEXUS_GUARD_EMPTY_TRANSFER", out var e))
                    return !string.Equals(e, "0", StringComparison.OrdinalIgnoreCase);
                return _cfg.Patches.GuardEmptyTransfer;
            }
        }

        /// <summary>When true, ocean/ferry transfers are blocked.</summary>
        public static bool BlockOceanFerryTransfers
        {
            get
            {
                EnsureInitialized();
                if (TryGetEnv("NEXUS_PORTALS_ONLY_TRANSFER", out var e))
                    return !string.Equals(e, "0", StringComparison.OrdinalIgnoreCase);
                return _cfg.Patches.BlockOceanFerryTransfers;
            }
        }

        public static bool SkipPersistCacheInvalidate
        {
            get
            {
                EnsureInitialized();
                if (TryGetEnv("NEXUS_SKIP_PERSIST_CACHE_INVALIDATE", out var e))
                    return string.Equals(e, "1", StringComparison.OrdinalIgnoreCase);
                return _cfg.Patches.SkipPersistCacheInvalidate;
            }
        }

        public static bool SkipBlueprintSnapshotResync
        {
            get
            {
                EnsureInitialized();
                if (TryGetEnv("NEXUS_SKIP_BLUEPRINT_SNAPSHOT_RESYNC", out var e))
                    return string.Equals(e, "1", StringComparison.OrdinalIgnoreCase);
                return _cfg.Patches.SkipBlueprintSnapshotResync;
            }
        }

        public static bool TransferSkipSafeSpawn
        {
            get
            {
                EnsureInitialized();
                if (TryGetEnv("NEXUS_TRANSFER_SKIP_SAFE_SPAWN", out var e))
                    return string.Equals(e, "1", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(e, "true", StringComparison.OrdinalIgnoreCase);
                return _cfg.Transfer.SkipSafeSpawn;
            }
        }

        public static float TransferConsoleMinSepMeters
        {
            get
            {
                EnsureInitialized();
                if (TryGetEnv("NEXUS_TRANSFER_CONSOLE_MIN_SEP", out var e) &&
                    float.TryParse(e, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var v) &&
                    v > 0.5f)
                    return v;
                return _cfg.Transfer.ConsoleMinSepMeters;
            }
        }

        public static int TransferConsoleRespawnTries
        {
            get
            {
                EnsureInitialized();
                if (TryGetEnv("NEXUS_TRANSFER_CONSOLE_RESPAWN_TRIES", out var e) &&
                    int.TryParse(e, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var v) &&
                    v > 0)
                    return v;
                return _cfg.Transfer.ConsoleRespawnTries;
            }
        }

        public static float TransferGroundMarginMeters
        {
            get
            {
                EnsureInitialized();
                if (TryGetEnv("NEXUS_TRANSFER_GROUND_MARGIN", out var e) &&
                    float.TryParse(e, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var v) &&
                    v > 0.25f)
                    return v;
                return _cfg.Transfer.GroundMarginMeters;
            }
        }

        private static bool TryGetEnv(string name, out string value)
        {
            value = Environment.GetEnvironmentVariable(name);
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool IsExplicitlyTrue(string e)
        {
            return string.Equals(e, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(e, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(e, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExplicitlyFalse(string e)
        {
            return string.Equals(e, "0", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(e, "false", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(e, "no", StringComparison.OrdinalIgnoreCase);
        }
    }
}
