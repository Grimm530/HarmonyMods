# Rust Map Generation Reference

This document provides an overview of all map generation-related classes and their key members for use in Harmony mods and reflection.

> Troubleshooting note: for operator-facing diagnostics on custom outpost swap spawn coverage (`[TRACK]` log lines for expected-vs-attempted prefab IDs), see `README.md` section **Outpost swap spawn coverage tracking**.

## Core Terrain Classes

### TerrainGenerator
**Location:** `oxide/!Assembly-CSharp-RUST/TerrainGenerator.cs`

Singleton component that creates the terrain GameObject.

**Key Methods:**
- `CreateTerrain()` - Creates terrain with default resolution
- `CreateTerrain(int heightmapResolution, int alphamapResolution)` - Creates terrain with custom resolution
- `GetHeightMapRes()` - Returns height map resolution
- `GetSplatMapRes()` - Returns splat map resolution
- `GetBaseMapRes()` - Returns base map resolution

**Properties:**
- `config` (TerrainConfig) - Terrain configuration

---

### TerrainConfig
**Location:** `oxide/!Assembly-CSharp-RUST/TerrainConfig.cs`

ScriptableObject containing terrain material and splat configurations.

**Key Properties:**
- `CastShadows` (bool) - Whether terrain casts shadows
- `GroundMask` (LayerMask) - Ground layer mask
- `WaterMask` (LayerMask) - Water layer mask
- `Material` (Material) - Terrain material
- `Splats` (SplatType[]) - Array of splat types (8 elements)

**Key Methods:**
- `GetAridColors()` - Returns arid biome colors
- `GetTemperateColors()` - Returns temperate biome colors
- `GetTundraColors()` - Returns tundra biome colors
- `GetArcticColors()` - Returns arctic biome colors
- `GetJungleColors()` - Returns jungle biome colors
- `GetCurrentGroundTypeNoAlloc(bool isGrounded, RaycastHit hit)` - Gets ground type without allocation

**Nested Classes:**
- `SplatType` - Contains color and overlay information for each splat
- `SplatOverlay` - Overlay configuration for splats

---

## Procedural Generation Components

All procedural components inherit from `ProceduralComponent` and implement `Process(uint seed)`.

### GenerateHeight
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateHeight.cs`

Generates terrain height map using native code.

**Key Method:**
- `Process(uint seed)` - Generates height map

**Native Method:**
- `Native_GenerateHeight` - Calls RustNative DLL

**Uses:**
- `World.Config.PercentageTier0/1/2` - Loot tier percentages
- `World.Config.PercentageBiomeArid/Temperate/Tundra/Arctic` - Biome percentages
- `TerrainMeta.LootAxisAngle` - Loot axis angle
- `TerrainMeta.BiomeAxisAngle` - Biome axis angle

---

### GenerateTopology
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateTopology.cs`

Generates terrain topology map (road/river/cliff/etc. placement).

**Key Method:**
- `Process(uint seed)` - Generates topology map

**Native Method:**
- `Native_GenerateTopology` - Calls RustNative DLL

**Uses:**
- Same config values as GenerateHeight

---

### GenerateBiome
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateBiome.cs`

Generates biome map (arid/temperate/tundra/arctic/jungle).

**Key Method:**
- `Process(uint seed)` - Generates biome map

**Native Method:**
- `Native_GenerateBiome` - Calls RustNative DLL

**Uses:**
- `World.Config.PercentageBiomeArid/Temperate/Tundra/Arctic/Jungle` - Biome percentages
- `World.Config.PercentageTier0/1/2` - Loot tier percentages

---

### GenerateSplat
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateSplat.cs`

Generates terrain splat map (texture blending).

**Key Method:**
- `Process(uint seed)` - Generates splat map

**Native Method:**
- `Native_GenerateSplat` - Calls RustNative DLL

---

### GenerateCliffTopology
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateCliffTopology.cs`

Marks cliff areas based on slope and splat maps.

**Key Properties:**
- `KeepExisting` (bool) - Whether to keep existing cliff topology

**Key Method:**
- `Process(uint seed)` - Generates cliff topology
- `Process(int x, int z)` - Static method to process specific coordinates

**Constants:**
- `slopeCutoff = 30f` - Slope angle threshold
- `splatCutoff = 0.4f` - Splat threshold

---

### GenerateCliffSplat
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateCliffSplat.cs`

Applies cliff splat textures to steep slopes.

**Key Method:**
- `Process(uint seed)` - Generates cliff splat
- `Process(int x, int z)` - Static method to process specific coordinates

---

### GenerateOceanTopology
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateOceanTopology.cs`

Marks ocean areas based on height map.

**Key Method:**
- `Process(uint seed)` - Generates ocean topology

---

### GenerateClutterTopology
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateClutterTopology.cs`

Generates clutter (rocks/trees) placement topology.

**Key Method:**
- `Process(uint seed)` - Generates clutter topology

---

### GenerateDecorTopology
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateDecorTopology.cs`

Generates decor (grass/bushes) placement topology.

**Key Properties:**
- `KeepExisting` (bool) - Whether to keep existing decor topology

**Key Method:**
- `Process(uint seed)` - Generates decor topology

---

## Infrastructure Generation

### GenerateRoadRing
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateRoadRing.cs`

Generates the main road ring around the map.

**Key Properties:**
- `MinWorldSize` (int) - Minimum world size to generate ring

**Key Method:**
- `Process(uint seed)` - Generates road ring

**Checks:**
- `World.Config.MainRoads` - Whether to generate main roads

**Constants:**
- `Width = 12f`
- `InnerPadding = 1f`
- `OuterPadding = 1f`
- `InnerFade = 1f`
- `OuterFade = 8f`

---

### GenerateRoadLayout
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateRoadLayout.cs`

Generates road network connecting monuments.

**Key Properties:**
- `RoadType` (InfrastructureType) - Road or Trail

**Key Method:**
- `Process(uint seed)` - Generates road layout

**Checks:**
- `World.Config.SideRoads` - For Road type
- `World.Config.Trails` - For Trail type

**Constants:**
- `RoadWidth = 10f`
- `TrailWidth = 4f`

---

### GenerateRoadMeshes
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateRoadMeshes.cs`

Creates road mesh GameObjects.

**Key Properties:**
- `RoadMesh` (Mesh) - Road mesh
- `RoadMeshes` (Mesh[]) - Array of road meshes
- `RoadMaterial` (Material) - Road material
- `RoadRingMaterial` (Material) - Road ring material
- `RoadPhysicMaterial` (PhysicMaterial) - Road physics material

**Key Method:**
- `Process(uint seed)` - Generates road meshes

**Runs On Cache:** Yes

---

### GenerateRoadTerrain
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateRoadTerrain.cs`

Adjusts terrain height for roads.

**Key Method:**
- `Process(uint seed)` - Adjusts terrain for roads

**Constants:**
- `SmoothenLoops = 2`
- `SmoothenIterations = 8`
- `SmoothenY = 16`
- `SmoothenXZ = 4`

---

### GenerateRoadTopology
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateRoadTopology.cs`

Marks road topology on terrain.

**Key Method:**
- `Process(uint seed)` - Generates road topology

---

### GenerateRailRing
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateRailRing.cs`

Generates the main rail ring around the map.

**Key Properties:**
- `MinWorldSize` (int) - Minimum world size to generate ring

**Key Method:**
- `Process(uint seed)` - Generates rail ring

**Checks:**
- `World.Config.AboveGroundRails` - Whether to generate above-ground rails

**Constants:**
- `Width = 4f`
- `InnerPadding = 1f`
- `OuterPadding = 1f`
- `InnerFade = 1f`
- `OuterFade = 32f`

---

### GenerateRailLayout
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateRailLayout.cs`

Generates rail network connecting monuments.

**Key Method:**
- `Process(uint seed)` - Generates rail layout

**Checks:**
- `World.Config.AboveGroundRails` - Whether to generate above-ground rails

**Constants:**
- `Width = 4f`

---

### GenerateRailBranching
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateRailBranching.cs`

Generates rail branch lines.

**Key Method:**
- `Process(uint seed)` - Generates rail branches

**Constants:**
- `Width = 4f`

---

### GenerateRailSiding
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateRailSiding.cs`

Generates rail siding tracks.

**Key Method:**
- `Process(uint seed)` - Generates rail sidings

**Constants:**
- `Width = 4f`

---

### GenerateRailMeshes
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateRailMeshes.cs`

Creates rail mesh GameObjects.

**Key Properties:**
- `RailMesh` (Mesh) - Rail mesh
- `RailMeshes` (Mesh[]) - Array of rail meshes
- `RailMaterial` (Material) - Rail material
- `RailPhysicMaterial` (PhysicMaterial) - Rail physics material

**Key Method:**
- `Process(uint seed)` - Generates rail meshes

**Runs On Cache:** Yes

---

### GenerateRailTerrain
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateRailTerrain.cs`

Adjusts terrain height for rails.

**Key Method:**
- `Process(uint seed)` - Adjusts terrain for rails

**Constants:**
- `SmoothenLoops = 8`
- `SmoothenIterations = 8`
- `SmoothenY = 64`
- `SmoothenXZ = 32`
- `TransitionSteps = 8`

---

### GenerateRailTopology
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateRailTopology.cs`

Marks rail topology on terrain.

**Key Method:**
- `Process(uint seed)` - Generates rail topology

---

### GeneratePowerlineLayout
**Location:** `oxide/!Assembly-CSharp-RUST/GeneratePowerlineLayout.cs`

Generates powerline network connecting monuments.

**Key Method:**
- `Process(uint seed)` - Generates powerline layout

**Checks:**
- `World.Config.Powerlines` - Whether to generate powerlines

---

### GenerateRiverLayout
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateRiverLayout.cs`

Generates river paths.

**Key Method:**
- `Process(uint seed)` - Generates river layout

**Checks:**
- `World.Config.Rivers` - Whether to generate rivers

**Constants:**
- `Width = 8f`
- `InnerPadding = 1f`
- `OuterPadding = 1f`
- `InnerFade = 16f`
- `OuterFade = 64f`

---

### GenerateRiverMeshes
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateRiverMeshes.cs`

Creates river mesh GameObjects.

**Key Properties:**
- `RiverMesh` (Mesh) - River mesh
- `RiverInteriorMesh` (Mesh) - River interior mesh
- `RiverInteriorFrontCapMesh` (Mesh) - Front cap mesh
- `RiverInteriorBackCapMesh` (Mesh) - Back cap mesh
- `RiverMeshes` (Mesh[]) - Array of river meshes
- `RiverMaterial` (Material) - River material
- `RiverPhysicMaterial` (PhysicMaterial) - River physics material

**Key Method:**
- `Process(uint seed)` - Generates river meshes

**Runs On Cache:** Yes

---

### GenerateRiverTerrain
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateRiverTerrain.cs`

Adjusts terrain height for rivers.

**Key Method:**
- `Process(uint seed)` - Adjusts terrain for rivers

**Constants:**
- `SmoothenLoops = 1`
- `SmoothenIterations = 8`
- `SmoothenY = 8`
- `SmoothenXZ = 4`

---

### GenerateRiverTopology
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateRiverTopology.cs`

Marks river topology on terrain.

**Key Method:**
- `Process(uint seed)` - Generates river topology

---

### GenerateDungeonGrid
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateDungeonGrid.cs`

Generates underground train tunnel network.

**Key Properties:**
- `TunnelFolder` (string) - Folder for tunnel prefabs
- `StationFolder` (string) - Folder for station prefabs
- `UpwardsFolder` (string) - Folder for upward transition prefabs
- `TransitionFolder` (string) - Folder for transition prefabs
- `LinkFolder` (string) - Folder for link prefabs
- `ConnectionType` (InfrastructureType) - Tunnel or Station
- `CellSize` (int) - Grid cell size (216)
- `LinkHeight` (float) - Link height (1.5f)
- `LinkRadius` (float) - Link radius (3f)
- `LinkTransition` (float) - Link transition distance (9f)

**Key Method:**
- `Process(uint seed)` - Generates dungeon grid

**Checks:**
- `World.Config.BelowGroundRails` - Whether to generate underground tunnels

**Runs On Cache:** Yes

---

## Erosion and Terrain Refinement

### GenerateErosion
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateErosion.cs`

Applies hydraulic erosion to terrain.

**Key Method:**
- `Process(uint seed)` - Generates erosion

**Static Property:**
- `splatPaintingData` (SplatPaintingData) - Data for splat painting after erosion

**Nested Struct:**
- `SplatPaintingData` - Contains height map delta and angle map

---

### GenerateErosionSplat
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateErosionSplat.cs`

Paints splat textures based on erosion data.

**Key Method:**
- `Process(uint seed)` - Paints erosion splats

**Uses:**
- `GenerateErosion.splatPaintingData` - Requires erosion data

---

## Texture and Mesh Generation

### GenerateTextures
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateTextures.cs`

Saves terrain maps to world cache.

**Key Method:**
- `Process(uint seed)` - Saves textures

**Runs On Cache:** Yes

**Saves Maps:**
- `height` - Height map
- `splat` - Splat map
- `biome` - Biome map
- `topology` - Topology map
- `alpha` - Alpha map
- `water` - Water map

---

### GenerateTerrainMesh
**Location:** `oxide/!Assembly-CSharp-RUST/GenerateTerrainMesh.cs`

Applies height map to terrain mesh.

**Key Method:**
- `Process(uint seed)` - Applies terrain mesh

**Runs On Cache:** Yes

---

## Map UI and Rendering

### MapHelper
**Location:** `oxide/!Assembly-CSharp-RUST/MapHelper.cs`

Utility class for map grid coordinates and positions.

**Key Methods:**
- `StringToGrid(string text)` - Converts string (e.g., "A1") to grid coordinates
- `GridToPosition(Vector2i grid)` - Converts grid to world position
- `StringToPosition(string text)` - Converts string to world position
- `PositionToString(Vector3 position)` - Converts position to string
- `PositionToGrid(Vector3 position)` - Converts position to grid
- `GridToString(Vector2i grid)` - Converts grid to string

---

### MapImageRenderer
**Location:** `oxide/!Assembly-CSharp-RUST/MapImageRenderer.cs`

Renders map images for display.

**Key Method:**
- `Render(out int imageWidth, out int imageHeight, out Color background, float scale = 0.5f, bool lossy = true, bool transparent = false, int oceanMargin = 500)` - Renders map image

**Returns:** `byte[]` - PNG or JPG image data

---

### MapInterface
**Location:** `oxide/!Assembly-CSharp-RUST/MapInterface.cs`

Singleton component for map UI interface.

**Key Properties:**
- `IsOpen` (static bool) - Whether map is currently open
- `HasPreviouslyOpenedInThisSession` (static bool) - Whether map was opened this session
- `View` (MapView) - Map view component
- `NexusMap` (UINexusMap) - Nexus map component

---

### MapView
**Location:** `oxide/!Assembly-CSharp-RUST/MapView.cs`

Main map view component.

**Key Properties:**
- `mapImage` (RawImage) - Map image display
- `cameraPositon` (Image) - Camera position indicator
- `ShowGrid` (bool) - Whether to show grid
- `ShowPointOfInterestMarkers` (bool) - Whether to show POI markers
- `ShowSleepingBags` (bool) - Whether to show sleeping bags
- `ShowLocalPlayer` (bool) - Whether to show local player
- `ShowTeamMembers` (bool) - Whether to show team members
- `ShowTrainLayer` (bool) - Whether to show train layer
- `ShowUndergroundLayers` (bool) - Whether to show underground layers
- `FogOfWarGrid` (PaintableImageGrid) - Fog of war grid

**Key Methods:**
- `OnPointerDown(PointerEventData eventData)` - Handles map clicks

---

### MapLayerRenderer
**Location:** `oxide/!Assembly-CSharp-RUST/MapLayerRenderer.cs`

Renders different map layers (train tunnels, underwater labs, dungeons).

**Key Methods:**
- `Render(MapLayer layer)` - Renders specified layer
- `GetOrCreate()` - Gets or creates singleton instance
- `GetUnderwaterLabFloorCount()` - Gets number of underwater lab floors

**Renders:**
- `MapLayer.TrainTunnels` - Train tunnel layer
- `MapLayer.Underwater1-8` - Underwater lab floors
- `MapLayer.Dungeons` - Dungeon layer

---

### MapLayer
**Location:** `oxide/!Assembly-CSharp-RUST/MapLayer.cs`

Enum for map layers.

**Values:**
- `Overworld = -1`
- `TrainTunnels = 0`
- `Underwater1-8 = 1-8`
- `Dungeons = 10`

---

## Map Markers

### MapMarker
**Location:** `oxide/!Assembly-CSharp-RUST/MapMarker.cs`

Base class for map markers.

**Key Properties:**
- `appType` (AppMarkerType) - Marker type for app
- `markerObj` (GameObjectRef) - Marker GameObject reference
- `serverMapMarkers` (static List<MapMarker>) - All server map markers

**Key Methods:**
- `GetAppMarkerData()` - Gets marker data for app

---

### MapMarker Variants
**Location:** `oxide/!Assembly-CSharp-RUST/MapMarker*.cs`

Various marker types:
- `MapMarkerCH47` - CH47 helicopter marker
- `MapMarkerDeliveryDrone` - Delivery drone marker
- `MapMarkerExplosion` - Explosion marker
- `MapMarkerGenericRadius` - Generic radius marker
- `MapMarkerHelicopterFlee` - Helicopter flee marker
- `MapMarkerMissionProvider` - Mission provider marker
- `MapMarkerMLRSRocket` - MLRS rocket marker
- `MapMarkerPet` - Pet marker

---

### MapEntity
**Location:** `oxide/!Assembly-CSharp-RUST/MapEntity.cs`

Entity for map items (paper maps, etc.).

**Key Properties:**
- `fogImages` (uint[]) - Fog of war images (1 element)
- `paintImages` (uint[]) - Paint images (144 elements)

**Key Methods:**
- `ImageUpdate(RPCMessage msg)` - Updates map image (RPC)

---

## Map Upload

### MapUploader
**Location:** `oxide/!Assembly-CSharp-RUST/MapUploader.cs`

Handles map upload to external services.

**Key Properties:**
- `IsUploaded` (static bool) - Whether map is uploaded
- `OriginalName` (static string) - Original map name
- `OriginalMapFileName` (static string) - Original map file name
- `OriginalSaveFileName` (static string) - Original save file name
- `IsImageUploaded` (static bool) - Whether image is uploaded
- `ImageUrl` (static string) - Image URL

**Key Methods:**
- `UploadMap()` - Uploads map
- `UploadMapImage(byte[] image)` - Uploads map image

---

## Environment and Placement

### EnvironmentVolumeEx
**Location:** `oxide/!Assembly-CSharp-RUST/EnvironmentVolumeEx.cs`

Extension methods for environment volume checking.

**Key Methods:**
- `CheckEnvironmentVolumes(Transform transform, Vector3 pos, Quaternion rot, Vector3 scale, EnvironmentType type)` - Checks environment volumes
- `CheckEnvironmentVolumesInsideTerrain(...)` - Checks if volumes are inside terrain
- `CheckEnvironmentVolumesOutsideTerrain(...)` - Checks if volumes are outside terrain
- `CheckEnvironmentVolumesAboveAltitude(...)` - Checks if volumes are above altitude
- `CheckEnvironmentVolumesBelowAltitude(...)` - Checks if volumes are below altitude

---

## World Configuration

### WorldConfig
**Location:** `oxide/!Assembly-CSharp-RUST/WorldConfig.cs` (referenced but not shown)

Contains world generation settings.

**Key Properties (referenced in code):**
- `Powerlines` (bool) - Generate powerlines
- `AboveGroundRails` (bool) - Generate above-ground rails
- `BelowGroundRails` (bool) - Generate below-ground rails
- `Rivers` (bool) - Generate rivers
- `MainRoads` (bool) - Generate main roads
- `SideRoads` (bool) - Generate side roads
- `Trails` (bool) - Generate trails
- `PercentageTier0/1/2` (float) - Loot tier percentages
- `PercentageBiomeArid/Temperate/Tundra/Arctic/Jungle` (float) - Biome percentages

**Key Methods:**
- `LoadFromWorldConfig()` - Loads config from JSON

---

## TerrainMeta

### TerrainMeta
Referenced throughout but not shown in provided files.

**Key Static Properties (referenced):**
- `TerrainMeta.Path` - TerrainPath component
- `TerrainMeta.HeightMap` - TerrainHeightMap component
- `TerrainMeta.SplatMap` - TerrainSplatMap component
- `TerrainMeta.BiomeMap` - TerrainBiomeMap component
- `TerrainMeta.TopologyMap` - TerrainTopologyMap component
- `TerrainMeta.AlphaMap` - TerrainAlphaMap component
- `TerrainMeta.WaterMap` - TerrainWaterMap component
- `TerrainMeta.PlacementMap` - TerrainPlacementMap component
- `TerrainMeta.Position` (Vector3) - Terrain position
- `TerrainMeta.Size` (Vector3) - Terrain size
- `TerrainMeta.Center` (Vector3) - Terrain center
- `TerrainMeta.Max` (Vector3) - Terrain max bounds
- `TerrainMeta.LootAxisAngle` (float) - Loot axis angle
- `TerrainMeta.BiomeAxisAngle` (float) - Biome axis angle

---

## Notes for Harmony Patching

1. **ProceduralComponent.Process()** - All generation components implement this method. Patch with `[HarmonyPatch(typeof(ComponentName), nameof(ComponentName.Process))]`

2. **World.Config** - Check boolean flags before generating infrastructure. Patch `WorldConfig.LoadFromWorldConfig()` to modify settings.

3. **TerrainMeta.Path** - Contains lists of paths:
   - `Roads` (List<PathList>)
   - `Rails` (List<PathList>)
   - `Rivers` (List<PathList>)
   - `Powerlines` (List<PathList>)
   - `LakeObjs` (List<LakeInfo>)
   - `Monuments` (List<MonumentInfo>)

4. **PathList** - Represents a path (road/rail/river/powerline) with:
   - `Name` (string)
   - `Path` (PathInterpolator) - Path points and tangents
   - `Width` (float)
   - `Topology` (int) - Topology flags
   - `Splat` (int) - Splat flags
   - `Hierarchy` (int) - Path hierarchy level

5. **Native Methods** - Height, topology, biome, and splat generation use native DLL calls. These cannot be easily patched but their inputs can be modified.

6. **Seed** - All `Process()` methods take a `uint seed` parameter. Modifying this will change generation but may break determinism.

---

## Common Patching Patterns

### Disable Component
```csharp
[HarmonyPatch(typeof(GeneratePowerlineLayout), nameof(GeneratePowerlineLayout.Process))]
public static class GeneratePowerlineLayout_Process_Patch
{
    static bool Prefix(GeneratePowerlineLayout __instance, ref uint seed)
    {
        if (CustomMapGen.IsCustomMapGenEnabled())
        {
            var config = CustomMapGen.Instance.GetConfig();
            if (config.RemoveSmallPowerLines && config.RemoveLargePowerLines)
            {
                return false; // Skip original method
            }
        }
        return true; // Continue with original method
    }
}
```

### Modify Config Before Processing
```csharp
[HarmonyPatch(typeof(WorldConfig), nameof(WorldConfig.LoadFromWorldConfig))]
public static class WorldConfig_LoadFromWorldConfig_Patch
{
    static void Postfix(WorldConfig __instance)
    {
        if (CustomMapGen.IsCustomMapGenEnabled())
        {
            var config = CustomMapGen.Instance.GetConfig();
            __instance.Powerlines = !config.RemoveSmallPowerLines && !config.RemoveLargePowerLines;
            __instance.AboveGroundRails = config.GenerateAboveGroundTrainTracks == "Wanted";
            __instance.Rivers = !config.RemoveRivers;
        }
    }
}
```

### Modify Path List After Generation
```csharp
[HarmonyPatch(typeof(ProcessProceduralObjects), nameof(ProcessProceduralObjects.Process))]
public static class ProcessProceduralObjects_Process_Patch
{
    static void Postfix()
    {
        if (CustomMapGen.IsCustomMapGenEnabled())
        {
            var config = CustomMapGen.Instance.GetConfig();
            // Modify TerrainMeta.Path.LakeObjs, etc.
        }
    }
}
```

---

## Extracting Entity Data from RustEdit Prefabs

To create custom monuments with procedurally placed entities, you need to extract entity data from RustEdit prefab files.

### Quick Start

1. **Install the extraction plugin**: Copy `PrefabEntityExtractor.cs` to `oxide/plugins/`
2. **Extract entities**: Run `prefab.extract "D:\RustEdit\CustomPrefabs\YourPrefab.prefab" output_name`
3. **Use generated code**: Copy the generated C# class to your CustomMapGen patches
4. **Call from monument patch**: Use `YourPrefabEntityData.AddEntitiesToMonument(monument)` in your monument placement patch

### Example: Adding Entities to a Monument

```csharp
[HarmonyPatch(typeof(PlaceMonuments), nameof(PlaceMonuments.Process))]
public static class PlaceMonumentsCustom_Patch
{
    static void Postfix(PlaceMonuments __instance, uint seed)
    {
        if (!CustomMapGen.IsCustomMapGenEnabled() || TerrainMeta.Path == null)
            return;
        
        foreach (var monument in TerrainMeta.Path.Monuments)
        {
            if (monument.name.Contains("YourMonumentName"))
            {
                // Add entities extracted from RustEdit prefab
                YourPrefabEntityData.AddEntitiesToMonument(monument);
            }
        }
    }
}
```

### Entity Data Structure

Extracted entities contain:
- **PrefabName**: Full Rust prefab path
- **LocalPosition**: Position relative to prefab root (Vector3)
- **LocalRotation**: Rotation relative to prefab root (Quaternion)
- **Scale**: Entity scale (Vector3)

Entities are stored in local coordinates so they can be placed at any monument location while maintaining relative positions.

**See `PREFAB_EXTRACTION_GUIDE.md` for detailed instructions.**