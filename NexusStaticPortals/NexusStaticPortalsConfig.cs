using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace NexusStaticPortals
{
    public sealed class NexusStaticPortalsConfigData
    {
        [JsonProperty("Debug Enabled")]
        public bool DebugEnabled { get; set; }

        [JsonProperty("Portals")]
        public List<PortalDefinition> Portals { get; set; } = new();

        [JsonProperty("Default Exit Door Prefab")]
        public string ExitDoorPrefab { get; set; } = "assets/prefabs/missions/portal/halloweenportalexit.prefab";

        [JsonProperty("Custom portals data directory")]
        public string CustomPortalsDataDirectory { get; set; } = string.Empty;
    }

    public sealed class PortalDefinition
    {
        [JsonProperty("Name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("IsOneWay")]
        public bool IsOneWay { get; set; } = true;

        [JsonProperty("TeleportationTime")]
        public int TeleportationTime { get; set; } = 3;

        [JsonProperty("RequiresItem")]
        public bool RequiresItem { get; set; }

        [JsonProperty("NexusTransferTargetZoneKey")]
        public string NexusTransferTargetZoneKey { get; set; } = string.Empty;

        [JsonProperty("NexusOneTimeScrapCost")]
        public int NexusOneTimeScrapCost { get; set; }

        [JsonProperty("NexusUnlockCurrencyShortName")]
        public string NexusUnlockCurrencyShortName { get; set; } = "scrap";

        [JsonProperty("NexusPrerequisitePortalName")]
        public string NexusPrerequisitePortalName { get; set; }

        [JsonProperty("EntranceAnchors")]
        public List<PortalAnchorDefinition> EntranceAnchors { get; set; } = new();

        [JsonIgnore]
        public List<BaseEntity> SpawnedEntranceDoors { get; } = new();

        [JsonIgnore]
        public bool IsNexusPortal =>
            !string.IsNullOrWhiteSpace(NexusTransferTargetZoneKey) &&
            EntranceAnchors != null &&
            EntranceAnchors.Count > 0;

        [JsonIgnore]
        public int ExpectedFixedWorldEntrances
        {
            get
            {
                var count = 0;
                if (EntranceAnchors == null)
                    return count;

                foreach (var anchor in EntranceAnchors)
                {
                    if (anchor != null && (anchor.UseFixedWorldTransform || anchor.UseMonumentRelativeTransform))
                        count++;
                }

                return count;
            }
        }
    }

    public sealed class PortalAnchorDefinition
    {
        [JsonProperty("UseFixedWorldTransform")]
        public bool UseFixedWorldTransform { get; set; }

        [JsonProperty("UseMonumentRelativeTransform")]
        public bool UseMonumentRelativeTransform { get; set; }

        [JsonProperty("MonumentNameContains")]
        public string MonumentNameContains { get; set; } = "compound";

        [JsonProperty("WorldPosition")]
        public Vec3Data WorldPosition { get; set; } = new();

        [JsonProperty("WorldEulerAngles")]
        public Vec3Data WorldEulerAngles { get; set; } = new();

        [JsonProperty("LocalPosition")]
        public Vec3Data LocalPosition { get; set; } = new();

        [JsonProperty("LocalEulerAngles")]
        public Vec3Data LocalEulerAngles { get; set; } = new();

        [JsonProperty("WorldScale")]
        public Vec3Data WorldScale { get; set; } = new() { X = 1f, Y = 1f, Z = 1f };

        [JsonProperty("WorldPositionOffset")]
        public Vec3Data WorldPositionOffset { get; set; } = new();

        [JsonProperty("FixedWorldLocalOffset")]
        public Vec3Data FixedWorldLocalOffset { get; set; } = new();

        [JsonProperty("SnapPortalBottomToWorldY")]
        public bool SnapPortalBottomToWorldY { get; set; }

        [JsonProperty("PortalBottomTargetWorldY")]
        public float PortalBottomTargetWorldY { get; set; }
    }

    public sealed class Vec3Data
    {
        [JsonProperty("x")]
        public float X { get; set; }

        [JsonProperty("y")]
        public float Y { get; set; }

        [JsonProperty("z")]
        public float Z { get; set; }

        public Vector3 ToVector3() => new Vector3(X, Y, Z);
    }

    public static class NexusStaticPortalsConfig
    {
        private static readonly string RelativePath = Path.Combine("HarmonyConfig", "NexusStaticPortals.json");
        private static readonly string LegacyImportPath = Path.Combine(Environment.CurrentDirectory, "oxide", "config", "Portals.json");

        public static string ConfigPath => Path.Combine(Environment.CurrentDirectory, RelativePath);

        public static NexusStaticPortalsConfigData LoadOrCreate()
        {
            var config = TryLoad(ConfigPath);
            if (config != null)
            {
                Normalize(config);
                return config;
            }

            config = TryLoad(LegacyImportPath);
            if (config != null)
            {
                Normalize(config);
                Save(config);
                Debug.Log("[NexusStaticPortals] Imported config from oxide/config/Portals.json into HarmonyConfig/NexusStaticPortals.json.");
                return config;
            }

            config = new NexusStaticPortalsConfigData();
            Normalize(config);
            Save(config);
            Debug.Log("[NexusStaticPortals] Wrote default HarmonyConfig/NexusStaticPortals.json.");
            return config;
        }

        public static void Save(NexusStaticPortalsConfigData data)
        {
            if (data == null)
                return;

            var path = ConfigPath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented));
        }

        private static NexusStaticPortalsConfigData TryLoad(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            try
            {
                var json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<NexusStaticPortalsConfigData>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NexusStaticPortals] Failed to load config '" + path + "': " + ex.Message);
                return null;
            }
        }

        private static void Normalize(NexusStaticPortalsConfigData data)
        {
            data.Portals ??= new List<PortalDefinition>();
            data.ExitDoorPrefab ??= "assets/prefabs/missions/portal/halloweenportalexit.prefab";
            data.CustomPortalsDataDirectory ??= string.Empty;

            foreach (var portal in data.Portals)
            {
                if (portal == null)
                    continue;

                portal.Name ??= string.Empty;
                portal.NexusTransferTargetZoneKey ??= string.Empty;
                portal.NexusUnlockCurrencyShortName ??= "scrap";
                portal.EntranceAnchors ??= new List<PortalAnchorDefinition>();

                foreach (var anchor in portal.EntranceAnchors)
                {
                    if (anchor == null)
                        continue;

                    anchor.WorldPosition ??= new Vec3Data();
                    anchor.WorldEulerAngles ??= new Vec3Data();
                    anchor.LocalPosition ??= new Vec3Data();
                    anchor.LocalEulerAngles ??= new Vec3Data();
                    anchor.WorldScale ??= new Vec3Data { X = 1f, Y = 1f, Z = 1f };
                    anchor.WorldPositionOffset ??= new Vec3Data();
                    anchor.FixedWorldLocalOffset ??= new Vec3Data();
                    anchor.MonumentNameContains ??= "compound";
                }
            }
        }
    }
}
