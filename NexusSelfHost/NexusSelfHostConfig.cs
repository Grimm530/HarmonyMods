using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace NexusSelfHost
{
    /// <summary>
    /// JSON at <c>HarmonyConfig/NexusSelfHost.json</c> (server working directory). Created on first load with defaults.
    /// Environment variables override these values when set (non-whitespace); see README.
    /// </summary>
    public sealed class NexusSelfHostConfigData
    {
        public DebugSettings Debug { get; set; } = new();

        public NexusSettings Nexus { get; set; } = new();

        public TransferSettings Transfer { get; set; } = new();

        public PortalTransferSettings PortalTransfer { get; set; } = new();

        public OutpostTransferSettings OutpostTransfer { get; set; } = new();

        public PatchToggles Patches { get; set; } = new();
    }

    public sealed class DebugSettings
    {
        /// <summary>Matches verbose <c>NEXUS_DEBUG</c> logs (SendRequestImpl, endpoint init).</summary>
        public bool VerboseHttp { get; set; }

        /// <summary>Log blueprint snapshot after <c>EnterGame</c> (same as unset <c>NEXUS_LOG_BLUEPRINT_CONNECT</c>).</summary>
        public bool LogBlueprintOnConnect { get; set; } = true;

        /// <summary>Extra line after PlayerInit resync when verbose (normally tied to NEXUS_DEBUG).</summary>
        public bool LogPlayerInitResync { get; set; }

        /// <summary>Log the player-transfer safety decision for each console transfer.</summary>
        public bool LogTransferFix { get; set; } = true;

        /// <summary>Verbose transfer probe details (surface source, skip reasons, chosen respawn).</summary>
        public bool VerboseTransferFix { get; set; }

        /// <summary>Log transfer entry method/from/to/entity-count before reposition handling.</summary>
        public bool LogTransferEntry { get; set; } = true;
    }

    public sealed class NexusSettings
    {
        /// <summary>POST /zone/player/disconnect on disconnect (disable with env <c>NEXUS_NOTIFY_PLAYER_DISCONNECT=0</c>).</summary>
        public bool NotifyPlayerDisconnect { get; set; } = true;
    }

    public sealed class TransferSettings
    {
        /// <summary>Disable console transfer safe-spawn patch when true (env <c>NEXUS_TRANSFER_SKIP_SAFE_SPAWN=1</c>).</summary>
        public bool SkipSafeSpawn { get; set; }

        public float ConsoleMinSepMeters { get; set; } = 12f;

        public int ConsoleRespawnTries { get; set; } = 48;

        public float GroundMarginMeters { get; set; } = 2f;
    }

    public sealed class OutpostTransferSettings
    {
        /// <summary>When true, console transfers are routed to Outpost instead of using incoming world coordinates.</summary>
        public bool Enabled { get; set; }

        /// <summary>When true, all console transfers use Outpost routing; otherwise only the old safe-spawn/terrain fix applies.</summary>
        public bool ForceConsoleTransfers { get; set; } = true;

        /// <summary>Apply the Outpost monument rotation to LocalOffset so the same logical point works on rotated monuments.</summary>
        public bool UseMonumentRotation { get; set; } = true;

        /// <summary>Fallback height above located Outpost root when no custom offset is configured.</summary>
        public float DefaultHeightAboveRoot { get; set; } = 2f;

        /// <summary>User-tunable relative landing point inside Outpost, in local monument space.</summary>
        public Vector3Config LocalOffset { get; set; } = new();

        /// <summary>
        /// Optional reference point supplied by the user on a known server/map. When ReferenceZoneKey matches the current
        /// zone, the mod logs the derived local offset so it can be copied into LocalOffset.
        /// </summary>
        public string ReferenceZoneKey { get; set; } = string.Empty;

        public bool ReferencePointEnabled { get; set; }

        public Vector3Config ReferenceWorldPosition { get; set; } = new();
    }

    public sealed class PortalTransferSettings
    {
        /// <summary>When true, console transfers try to land at a NexusStaticPortals door that points back to the source zone.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>When true, console transfers prefer portal doors before Outpost / procedural fallback.</summary>
        public bool ForceConsoleTransfers { get; set; } = true;

        /// <summary>Apply the resolved portal rotation to the arrival offset so players land in front of the door.</summary>
        public bool UsePortalRotation { get; set; } = true;

        /// <summary>Forward offset from the portal door transform used for the arrival point.</summary>
        public float ForwardOffsetMeters { get; set; } = 2.25f;

        /// <summary>Extra vertical offset applied before the terrain snap safety net.</summary>
        public float UpOffsetMeters { get; set; } = 0.1f;
    }

    public sealed class Vector3Config
    {
        public float X { get; set; }

        public float Y { get; set; }

        public float Z { get; set; }
    }

    public sealed class PatchToggles
    {
        public bool GuardEmptyTransfer { get; set; } = true;

        public bool BlockOceanFerryTransfers { get; set; } = true;

        public bool SkipPersistCacheInvalidate { get; set; }

        public bool SkipBlueprintSnapshotResync { get; set; }
    }

    public static class NexusSelfHostConfig
    {
        private static readonly string RelativePath = Path.Combine("HarmonyConfig", "NexusSelfHost.json");

        public static string ConfigPath => Path.Combine(Environment.CurrentDirectory, RelativePath);

        /// <summary>Load from disk or write defaults. Never returns null.</summary>
        public static NexusSelfHostConfigData LoadOrCreate()
        {
            var path = ConfigPath;
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var loaded = JsonConvert.DeserializeObject<NexusSelfHostConfigData>(json);
                    if (loaded != null)
                    {
                        Debug.Log("[NexusSelfHost] Loaded HarmonyConfig/NexusSelfHost.json");
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NexusSelfHost] Config load failed (" + ex.Message + "); using defaults and rewriting file.");
            }

            var created = new NexusSelfHostConfigData();
            Save(created);
            Debug.Log("[NexusSelfHost] Wrote default HarmonyConfig/NexusSelfHost.json (edit Debug / Transfer / Patches; env vars override when set).");
            return created;
        }

        public static void Save(NexusSelfHostConfigData data)
        {
            if (data == null)
                return;
            var path = ConfigPath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented));
        }
    }
}
