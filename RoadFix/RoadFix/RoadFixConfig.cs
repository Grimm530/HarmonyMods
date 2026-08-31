using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace RoadFix;

public class RoadFixConfig
{
    public class ConfigData
    {
        [JsonProperty("Enabled")]
        public bool Enabled = true;

        /// <summary>
        /// When false (default), road meshes follow path-node height like rails.
        /// When true, vanilla behavior (drape onto terrain surface).
        /// </summary>
        [JsonProperty("RoadsSnapToTerrain")]
        public bool RoadsSnapToTerrain = false;

        [JsonProperty("ElevateOverWater")]
        public bool ElevateOverWater = true;

        [JsonProperty("SoftenTerrainUnderRivers")]
        public bool SoftenTerrainUnderRivers = true;

        [JsonProperty("WaterClearance")]
        public float WaterClearance = 2f;

        /// <summary>Place custom bridge .map tiles under road/rail river crossings.</summary>
        [JsonProperty("SpawnCustomBridges")]
        public bool SpawnCustomBridges = true;

        [JsonProperty("RoadBridgeMapPath")]
        public string RoadBridgeMapPath = "maps/prefabs/bridge.map";

        [JsonProperty("RailBridgeMapPath")]
        public string RailBridgeMapPath = "maps/prefabs/bridgerail.map";

        /// <summary>
        /// Local position of the road path center node in bridge.map (RustEdit).
        /// Map origin stays at (0,0,0); placement puts this node on the world path.
        /// </summary>
        [JsonProperty("RoadPathCenterLocal")]
        public Vector3 RoadPathCenterLocal = new Vector3(-6.346f, 5.065f, 0.262f);

        /// <summary>Local position of the rail path center node in bridgerail.map.</summary>
        [JsonProperty("RailPathCenterLocal")]
        public Vector3 RailPathCenterLocal = new Vector3(-6.316f, 5.036f, 0.127f);

        /// <summary>
        /// One "node" length in metres (bridge already covers ~3 nodes at native scale).
        /// NodeCount ≈ SpanLength / this.
        /// </summary>
        [JsonProperty("BridgeTemplateLength")]
        public float BridgeTemplateLength = 12f;

        /// <summary>
        /// Do not stretch when NodeCount is at or below this (bridge is already larger than the road).
        /// </summary>
        [JsonProperty("StretchOnlyAfterNodes")]
        public int StretchOnlyAfterNodes = 3;

        /// <summary>
        /// Extra length scale per node beyond StretchOnlyAfterNodes (0.05 = +5% per extra node).
        /// Example: 3 nodes → 1.0, 4 nodes → 1.05, 5 nodes → 1.10.
        /// </summary>
        [JsonProperty("StretchPerExtraNode")]
        public float StretchPerExtraNode = 0.05f;

        /// <summary>
        /// After roads raise terrain through rivers, carve the bed back at crossings only
        /// (tight OuterFade). Does not move road/rail path nodes.
        /// </summary>
        [JsonProperty("ReapplyRiverHeightAfterRoads")]
        public bool ReapplyRiverHeightAfterRoads = true;

        /// <summary>
        /// If true, re-run AdjustTerrainHeight on entire rivers (OuterFade≈64).
        /// Causes floating poles/trees — leave false; use local crossing carve instead.
        /// </summary>
        [JsonProperty("FullRiverHeightReapply")]
        public bool FullRiverHeightReapply = false;

        /// <summary>Soft bank pad (metres) past channel width — knocks down side ridges.</summary>
        [JsonProperty("LocalRiverOuterFade")]
        public float LocalRiverOuterFade = 18f;

        /// <summary>Reserved (smoothing strength uses fixed passes now).</summary>
        [JsonProperty("LocalRiverInnerFade")]
        public float LocalRiverInnerFade = 8f;

        /// <summary>
        /// How far along the river to sample clean bed before/after the crossing (metres).
        /// Also influences the gap skipped over raised road/rail fill.
        /// </summary>
        [JsonProperty("LocalRiverSegmentPad")]
        public float LocalRiverSegmentPad = 28f;

        /// <summary>
        /// Multiplier on vanilla GetRadius for carve WIDTH (across channel).
        /// &gt;1 reaches past typical bank crest ridges.
        /// </summary>
        [JsonProperty("RiverCarveWidthScale")]
        public float RiverCarveWidthScale = 1.15f;

        /// <summary>
        /// Small extra metres on the before/after bed average (negative = slightly deeper).
        /// Main depth now comes from real bed samples, not this bonus.
        /// </summary>
        [JsonProperty("BedDepthBonus")]
        public float BedDepthBonus = -0.15f;

        /// <summary>
        /// Flatten rail path nodes across bridge spans onto a straight grade
        /// (matches bridgerail gravel deck / kills rail waves on the span).
        /// </summary>
        [JsonProperty("SnapRailNodesToDeck")]
        public bool SnapRailNodesToDeck = true;

        /// <summary>Added to snapped rail node Y (gravel top relative to path center).</summary>
        [JsonProperty("RailDeckGravelOffset")]
        public float RailDeckGravelOffset = 0f;

        /// <summary>
        /// Extra yaw (degrees) applied after LookRotation(pathTangent).
        /// +90 aligns template +X with the path.
        /// </summary>
        [JsonProperty("BridgeYawOffset")]
        public float BridgeYawOffset = 90f;

        /// <summary>
        /// World Y nudge for the whole bridge map. -5 ≈ Bridgeonly -53 → -58.
        /// </summary>
        /// <summary>World Y nudge for the whole bridge (more negative = lower).</summary>
        /// <summary>
        /// World Y nudge after aligning path-center local to bank-average path height.
        /// Negative lowers the deck onto the road (screenshot lip was ~1.5m with -0.5).
        /// </summary>
        [JsonProperty("BridgeHeightOffset")]
        public float BridgeHeightOffset = -2f;

        /// <summary>
        /// When two rail spans' centers are within this radius (metres), keep only the longer one.
        /// Stops double bridges at junctions that merge before the river.
        /// </summary>
        [JsonProperty("RailBridgeMergeRadius")]
        public float RailBridgeMergeRadius = 55f;

        /// <summary>
        /// Base length multiplier (1 = native). Applied before per-extra-node stretch.
        /// &lt;1 shortens the bridge (e.g. 0.85 = 15% shorter).
        /// </summary>
        [JsonProperty("BridgeLengthScale")]
        public float BridgeLengthScale = 0.85f;

        /// <summary>Local axis to stretch for span length: "X" or "Z".</summary>
        [JsonProperty("BridgeLengthAxis")]
        public string BridgeLengthAxis = "X";

        /// <summary>
        /// Max bank-to-bank pitch (degrees). Keeps slope follow without extreme tilts.
        /// </summary>
        [JsonProperty("MaxBridgePitchDegrees")]
        public float MaxBridgePitchDegrees = 20f;

        /// <summary>
        /// Flip all bridge pitch if decks tip the wrong way (+1 normal, -1 invert).
        /// </summary>
        [JsonProperty("BridgePitchSign")]
        public float BridgePitchSign = -1f;

        /// <summary>Max distance from road/rail centerline to a river path to count as a crossing.</summary>
        [JsonProperty("CrossingDetectRadius")]
        public float CrossingDetectRadius = 12f;

        /// <summary>Minimum span length (metres) before placing a bridge.</summary>
        [JsonProperty("MinBridgeSpanLength")]
        public float MinBridgeSpanLength = 8f;

        /// <summary>Sample step along path when scanning for crossings.</summary>
        [JsonProperty("CrossingSampleStep")]
        public float CrossingSampleStep = 2f;

        /// <summary>
        /// Max deck-corridor width (metres) for local carve under a span.
        /// Cap only — actual river width still comes from GetRadius.
        /// </summary>
        [JsonProperty("RecarveWidth")]
        public float RecarveWidth = 14f;

        [JsonProperty("DebugLogging")]
        public bool DebugLogging = true;
    }

    public static ConfigData Config;

    private static readonly string Location = Path.Combine("HarmonyConfig", "RoadFix.json");

    public static void LoadConfig()
    {
        if (!Directory.Exists("HarmonyConfig"))
            Directory.CreateDirectory("HarmonyConfig");
        if (!File.Exists(Location))
        {
            LoadDefaultConfig();
            return;
        }
        try
        {
            Config = JsonConvert.DeserializeObject<ConfigData>(File.ReadAllText(Location));
            if (Config == null)
                LoadDefaultConfig();
        }
        catch
        {
            LoadDefaultConfig();
        }
    }

    public static bool IsEnabled()
    {
        LoadConfig();
        return Config?.Enabled == true;
    }

    private static void LoadDefaultConfig()
    {
        Config = new ConfigData();
        SaveConfig();
    }

    public static void SaveConfig()
    {
        try
        {
            File.WriteAllText(Location,
                JToken.Parse(JsonConvert.SerializeObject(Config)).ToString(Formatting.Indented));
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RoadFix] Failed to save config: {ex}");
        }
    }
}
