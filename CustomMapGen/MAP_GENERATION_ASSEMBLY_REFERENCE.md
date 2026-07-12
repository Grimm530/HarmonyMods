# Map Generation Reference

Single reference for Rust procedural map generation: assembly files, key members, and CustomMapGen patches. Use when modifying or patching map generation (e.g. with CustomMapGen Harmony mod).

**Base path (game assembly):** `D:\!RustServer\oxide\!Assembly-CSharp-RUST`  
**Reference repo (for borrowing):** `D:\!RustServer\.cursor\HarmonyMods\HarmonyCustomGenerator` — see **Part 6** for mapping to our patches and assembly targets.

---

# Part 1 – Assembly file list (what each file does)

## Generate* (Procedural components)

| File | Purpose |
|------|--------|
| **GenerateBiome.cs** | Fills `TerrainMeta.BiomeMap` from native `generate_biome`; biome/loot tier percentages from `World.Config`. |
| **GenerateCliffSplat.cs** | ProceduralComponent: paints cliff splat (texture) on terrain from cliff topology. |
| **GenerateCliffTopology.cs** | ProceduralComponent: marks cliff topology (steep slopes) for cliff placement. |
| **GenerateClutterTopology.cs** | ProceduralComponent: topology used for clutter (small rocks/decor) placement. |
| **GenerateDecorTopology.cs** | ProceduralComponent: topology used for decor (grass, bushes, etc.) placement. |
| **GenerateDungeonBase.cs** | ProceduralComponent: base layout/data for dungeon generation. |
| **GenerateDungeonGrid.cs** | ProceduralComponent: grid structure for dungeons. |
| **GenerateErosion.cs** | ProceduralComponent: terrain erosion (height + angle); can flatten shores; uses GenerateErosionJobs. |
| **GenerateErosionSplat.cs** | ProceduralComponent: paints erosion splat (e.g. shore/bay) after erosion. |
| **GenerateHeight.cs** | ProceduralComponent: base heightmap from native `generate_height`; uses World.Config biome/loot percentages. |
| **GenerateOceanTopology.cs** | ProceduralComponent: ocean/water-edge topology. |
| **GeneratePowerlineLayout.cs** | ProceduralComponent: powerline path/layout (poles along paths). |
| **GeneratePowerlineTopology.cs** | ProceduralComponent: topology flags for powerline placement. |
| **GenerateRailBranching.cs** | ProceduralComponent: rail branch points. |
| **GenerateRailLayout.cs** | ProceduralComponent: rail path layout. |
| **GenerateRailMeshes.cs** | ProceduralComponent: mesh generation for rails. |
| **GenerateRailRing.cs** | ProceduralComponent: ring/main rail loop. |
| **GenerateRailSiding.cs** | ProceduralComponent: rail sidings. |
| **GenerateRailTerrain.cs** | ProceduralComponent: terrain modification along rails. |
| **GenerateRailTexture.cs** | ProceduralComponent: rail texture/splat. |
| **GenerateRailTopology.cs** | ProceduralComponent: topology for rail placement. |
| **GenerateRiverLayout.cs** | ProceduralComponent: river path layout. |
| **GenerateRiverMeshes.cs** | ProceduralComponent: river mesh generation. |
| **GenerateRiverTerrain.cs** | ProceduralComponent: terrain carving/shaping for rivers. |
| **GenerateRiverTexture.cs** | ProceduralComponent: river texture/splat. |
| **GenerateRiverTopology.cs** | ProceduralComponent: topology for rivers/riverside. |
| **GenerateRoadLayout.cs** | ProceduralComponent: road path layout. |
| **GenerateRoadMeshes.cs** | ProceduralComponent: road mesh generation. |
| **GenerateRoadRing.cs** | ProceduralComponent: ring road layout (main road loop). |
| **GenerateRoadTerrain.cs** | ProceduralComponent: terrain modification along roads. |
| **GenerateRoadTexture.cs** | ProceduralComponent: road texture/splat. |
| **GenerateRoadTopology.cs** | ProceduralComponent: road/roadside topology (MarkRoadside etc.). |
| **GenerateSplat.cs** | ProceduralComponent: general terrain splat (texture layer) generation. |
| **GenerateTerrainMesh.cs** | ProceduralComponent: builds final terrain mesh from height/splat data. |
| **GenerateTextures.cs** | ProceduralComponent: final terrain texture generation. |
| **GenerateTopology.cs** | ProceduralComponent: base topology from native `generate_topology` (height + biome). |
| **GenerateWireMeshes.cs** | ProceduralComponent: powerline wire meshes. |

---

## Terrain* (Terrain data and extensions)

| File | Purpose |
|------|--------|
| **TerrainMeta.cs** | Central singleton: Position, Size, HeightMap, BiomeMap, TopologyMap, WaterMap, Path.Monuments, etc. Referenced by almost all generation. |
| **TerrainAlphaMap.cs** | TerrainMap&lt;byte&gt;; alpha blending for terrain layers. |
| **TerrainAtlasSet.cs** | ScriptableObject: atlas of terrain textures/splats. |
| **TerrainBiomeMap.cs** | TerrainMap&lt;byte&gt;; biome IDs (arid, temperate, tundra, arctic, jungle). |
| **TerrainBlendMap.cs** | TerrainMap&lt;byte&gt;; blend weights for terrain layers. |
| **TerrainCollision.cs** | TerrainExtension: collision mesh/physics for terrain. |
| **TerrainDistanceMap.cs** | TerrainMap&lt;byte&gt;; distance-from-feature maps (e.g. distance from monuments). |
| **TerrainHeightMap.cs** | TerrainMap&lt;short&gt;; heightmap; GetHeight, GetNormal, GetSlope; used by almost all generation. |
| **TerrainSplatMap.cs** | TerrainMap&lt;byte&gt;; splat/ground texture layers. |
| **TerrainTexturing.cs** | TerrainExtension: applies splat/biome to terrain material. |
| **TerrainTopologyMap.cs** | TerrainMap&lt;int&gt;; topology flags (road, river, cliff, monument, etc.). |
| **TerrainWaterMap.cs** | TerrainMap&lt;short&gt;; water level / underwater terrain. |

---

## Place* (Monument / object placement)

| File | Purpose |
|------|--------|
| **PlaceCliffs.cs** | ProceduralComponent: places cliff rocks/meshes using cliff topology. |
| **PlaceMonuments.cs** | ProceduralComponent: places main monuments (outpost, compound, caves, etc.); calls World.AddPrefab. |
| **PlaceMonumentsOffshore.cs** | ProceduralComponent: offshore/island monuments. |
| **PlaceMonumentsRoadside.cs** | ProceduralComponent: roadside monuments (e.g. car wrecks). |
| **PlaceMonumentsRailside.cs** | ProceduralComponent: monuments along rails. |
| **PlacePowerlineObjects.cs** | ProceduralComponent: places powerline pole entities. |
| **PlaceRiverObjects.cs** | ProceduralComponent: objects along rivers. |
| **PlaceRoadObjects.cs** | ProceduralComponent: objects along roads. |

---

## Monument / Mountain (TerrainPlacement prefab components)

| File | Purpose |
|------|--------|
| **Monument.cs** | TerrainPlacement: applies monument height/splat/topology from prefab data (radius, fade). |
| **Mountain.cs** | TerrainPlacement: applies mountain height/splat/topology from prefab data. |

---

## AddTo* (ProceduralObject – apply prefab to terrain maps)

| File | Purpose |
|------|--------|
| **AddToAlphaMap.cs** | ProceduralObject: writes alpha (e.g. clear) into TerrainMeta.AlphaMap in bounds. |
| **AddToHeightMap.cs** | ProceduralObject: samples collider and writes height into TerrainMeta.HeightMap. |
| **AddToWaterMap.cs** | ProceduralObject: writes water level and optional topology into TerrainMeta.WaterMap. |

---

## World / setup

| File | Purpose |
|------|--------|
| **WorldSetup.cs** | SingletonComponent: runs procedural generation (InitCoroutine), loads world prefabs, TerrainGenerator; entry point for map gen. |

---

## Other map/terrain related

| File | Purpose |
|------|--------|
| **AsyncTerrainNavMeshBake.cs** | CustomYieldInstruction: async NavMesh bake for terrain (used after gen or at runtime). |
| **DecorAlign.cs** | DecorComponent: aligns decor to terrain normal/slope (used when placing decor). |
| **EnvironmentVolumeEx.cs** | Extension methods: CheckEnvironmentVolumes for placement/terrain checks. |

---

## Jobs (used during generation)

| File | Purpose |
|------|--------|
| **AntiHackJobs/GenerateInsideMeshCommandsJob.cs** | IJobFor: generates raycast commands for “inside mesh” checks (anti-cheat); not terrain gen logic but runs in world. |

---

## Related folders (not single-file listed)

- **GenerateErosionJobs/** – Jobs used by GenerateErosion (height delta, angle, etc.).
- **TerrainHeightMapJobs/** – Jobs for TerrainHeightMap.
- **TerrainTopologyMapJobs/** – Jobs for TerrainTopologyMap.
- **TerrainWaterMapJobs/** – Jobs for TerrainWaterMap.
- **TerrainTexturingJobs/** – Jobs for TerrainTexturing.
- **TerrainMeta.cs** – Listed in Terrain* table above; central terrain state for all generation.

---

## Quick lookup – “I want to change…”

| Goal | Primary file(s) |
|------|------------------|
| Height / base terrain shape | GenerateHeight.cs, GenerateErosion.cs |
| Biomes / loot tiers | GenerateBiome.cs, TerrainBiomeMap.cs, World.Config |
| Cliffs | GenerateCliffTopology.cs, GenerateCliffSplat.cs, PlaceCliffs.cs |
| Roads | GenerateRoadRing.cs, GenerateRoadLayout.cs, GenerateRoadTopology.cs, GenerateRoadTerrain.cs, GenerateRoadMeshes.cs |
| Rivers | GenerateRiverLayout.cs, GenerateRiverTerrain.cs, GenerateRiverTopology.cs, GenerateRiverMeshes.cs |
| Rails | GenerateRailLayout.cs, GenerateRailRing.cs, GenerateRailBranching.cs, GenerateRailSiding.cs, GenerateRailTerrain.cs, GenerateRailMeshes.cs |
| Powerlines | GeneratePowerlineLayout.cs, GeneratePowerlineTopology.cs, GenerateWireMeshes.cs, PlacePowerlineObjects.cs |
| Monuments | PlaceMonuments.cs, Monument.cs, World.AddPrefab |
| Shore flattening | GenerateErosion.cs (shore/bay in erosion step) |
| Topology flags | GenerateTopology.cs, GenerateRoadTopology.cs, GenerateRiverTopology.cs, TerrainTopologyMap.cs |
| Water | TerrainWaterMap.cs, AddToWaterMap.cs, GenerateOceanTopology.cs |
| Order of execution | WorldSetup.cs (InitCoroutine) |

---

## CustomMapGen patches → assembly targets

| Patch file | Target(s) | What it does |
|------------|-----------|----------------|
| WorldConfigPatches.cs | WorldConfig.LoadFromWorldConfig, WorldConfig.LoadScriptableConfigs | Applies CustomMapGen config to World.Config (powerlines, rails, rivers, tiers, biomes, blocked prefabs). |
| WorldConfigMergePatches.cs | WorldConfig.MergeScriptableConfig (private) | Re-applies config after each ScriptableWorldConfig merge. |
| GenerateRailPatches.cs | GenerateRailRing.Process, GenerateRailLayout.Process | Prefix: forces World.Config.AboveGroundRails = true when config says "Wanted" so rails always run when requested. |
| GenerateHeightShoreFlattenPatches.cs | ProcessProceduralObjects.Process | Postfix: flattens shores/bays if FlattenShoreAndBay (runs after water map is populated). |
| GenerateHeightShoreFlattenPatches.cs | ProcessProceduralObjects.Process | Postfix: on 2nd invocation, shore flatten (height only) then **RemoveOceanTopologyFromMainland** — removes ocean (0x80) from mainland cells (height > 0.5) so shore-flattened terrain and island-ring circles do not keep ocean topology; prevents random round lakes. |
| GeneratePowerlineLayoutPatches.cs | GeneratePowerlineLayout.Process | Prefix: skips Process (no powerlines) when config disables powerlines. |
| GenerateRiverLayoutPatches.cs | GenerateRiverLayout.Process | Prefix: skips Process when config removes rivers. |
| GenerateRoadRingPatches.cs | GenerateRoadRing.Process | Controls ring road. |
| GenerateRoadTopologyPatches.cs | GenerateRoadTopology.MarkRoadside | Removes roadside topology flag (4096) only when AllowBuildingOnRoads; does not add or affect ocean (0x80). Index: dst[z*res+x]. |
| LakeInfoPatches.cs | LakeInfo | Lake count limits. |
| MountainPatches.cs | Mountain / terrain | Mountain reduction. |
| PlaceCliffsPatches.cs | PlaceCliffs.Process | Skips when cliffs disabled. |
| PlaceMonumentsCompoundPatches.cs | PlaceMonuments.Process | Spawns compound entities (skips assets/content/ during procgen). |
| PlaceMonumentsFilterPatches.cs | PlaceMonuments.Process | Filters monuments by config. |
| PlaceMonumentsOffshorePatches.cs | PlaceMonumentsOffshore.Process | Island count / intensity. |
| PlaceMonumentsIslandRingsPatches.cs | PlaceMonuments.Process | Postfix: optional fixed island rings (circular terrain at map edge). **Disabled by default** (IslandRings.Enabled = false, Count = 0) to avoid round lakes on mainland. When enabled, clears ocean topology (0x80) in circles so they do not render as lakes. |
| PlaceMonumentsOutpostPatches.cs | PlaceMonuments.Process | No-op; outpost move handled in WorldAddPrefabPatches. |
| PlaceMonumentsPatches.cs | PlaceMonuments.Process | Oases/canyons limits. |
| PlaceMonumentsRoadsidePatches.cs | PlaceMonumentsRoadside.Process | Car wreck filtering (fallback; primary block in WorldAddPrefabPatches). |
| WorldAddPrefabPatches.cs | World.AddPrefab | Blocks car wrecks when configured; redirects outpost/bandit to map center; relocates small monuments too close to large ones. **Monument flow:** (1) Block bandit → fill bandit slot with another monument (e.g. from roadside). (2) Place compound at center. (3) Block procedural compound/outpost not at center and **save that position**; when relocating a small monument (too close to large), **use the blocked outpost slot first** if valid (dry, min distance from others), then fall back to FindValidPositionForSmallMonument. With DebugLogging: lists every monument placed (prefab short name), position, and whether generated or relocated. |
| FileSystemWarmupPatches.cs | FileSystem_Warmup.Run | Skip asset warmup on start when SkipAssetWarmup enabled. |
| BootstrapPatches.cs | Bootstrap.DedicatedServerStartup | Apply map size override (3500–6000) and ForceNewMapEachTime seed before World init. |
| WorldMapSettingsPatches.cs | World.get_Name, get_MapFileName, get_MapFolderName, get_SaveFileName, get_SaveFolderName | Override map/save name and folder when MapSettings overrides set. |
| PostSaveSwapPatches.cs (WorldSerialization_Save_SwapPrefix_Patch) | WorldSerialization.Save(string) | **Prefix:** Before the map file is written, apply monument swap (remove compound/outpost, add prefabs from `outpost.map` in CustomPrefabsFolder). Prefabs list from world via **GetPrefabsListFromWorld** (tries "prefabs" and "Prefabs"). Written file contains your custom outpost; no dependency on DONE. |
| WorldSetupMapImagePatches.cs | WorldSetup.InitCoroutine | Post-generation: MapImage only (if enabled). Swap runs in Save Prefix, not here. |
| TerrainMetaPatches.cs | TerrainMeta | Biome/loot axis. |
| WorldConfigMergePatches.cs | WorldConfig.MergeScriptableConfig | See WorldConfigPatches; re-apply after merge. |
| WorldConfig_LoadFromJsonString_Patch | WorldConfig.LoadFromJsonString | Re-apply CustomMapGen config after server world config string load. |
| WorldConfig_LoadFromJsonFile_Patch | WorldConfig.LoadFromJsonFile | Re-apply CustomMapGen config after server world config file load. |
| ProcgenConfigApplyPatches.cs | WorldSetup.InitCoroutine, GenerateHeight.Process | Reset “applied” flag at gen start; apply config at first procgen component (safety net for BlockedPrefabs, AboveGroundRails). |
| CargoNotifierPatches.cs | CargoNotifier | EmbedCargoShipPath. |
| PreventBuildingMonumentTagPatches.cs | ConstructionErrors.GetPreventBuildingMonumentTag | AllowBuildingOnRoads. |
| VehicleCarWrecksPatches.cs | VehicleCarWrecks | RemoveCarWrecks (roadside). |

---

## Troubleshooting

- **Nothing from CustomMapGen seems to work (rocks, rails, outpost, etc.)**  
  World.Config can be overwritten by the server’s world config (e.g. `world.configfile` / `world.configstring`) after our initial apply. CustomMapGen now re-applies in: (1) LoadFromWorldConfig Postfix, (2) LoadScriptableConfigs Postfix, (3) LoadFromJsonString Postfix, (4) LoadFromJsonFile Postfix, (5) MergeScriptableConfig Postfix, (6) **ProcgenConfigApplyPatches**: at the start of procgen (first component, e.g. Height Map) we apply again so BlockedPrefabs, AboveGroundRails, etc. are in place. Ensure `Enabled` is `true` in HarmonyConfig/CustomMapGen.json and use **PascalCase** keys (e.g. `"BlockedPrefabs"`, `"AboveGroundRails"`). Generate a **new** procedural map (not a cached one).

- **BlockedPrefabs (rocks) not being removed**  
  BlockedPrefabs are applied to `World.Config.PrefabBlacklist`. The game filters prefabs in `Prefab.FindPrefabNames` via `World.Config.IsPrefabAllowed(path)` when `useWorldConfig` is true (default for `Prefab.Load`). Use **lowercase path substrings** in the list (e.g. `coastal_rocks`, `rock_formation_small`, `rock_formation_medium`). The JSON key in CustomMapGen.json must be **"BlockedPrefabs"** (PascalCase). Rocks/cliffs are loaded from autospawn folders (e.g. decor, cliff); they are not in `TerrainMeta.Path.Monuments`, so PlaceMonumentsFilterPatches does not remove them — only PrefabBlacklist at load time does.

- **Above-ground train tracks not appearing**  
  Rails run only when `World.Config.AboveGroundRails` is true when `GenerateRailRing.Process` and `GenerateRailLayout.Process` run. Config is applied in: (1) LoadFromWorldConfig Postfix, (2) LoadScriptableConfigs Postfix, (3) MergeScriptableConfig Postfix, (4) **GenerateRailPatches** Prefix (safety net: forces true when config says "Wanted" right before the check), (5) **ProcgenConfigApplyPatches** at procgen start. Ensure `"AboveGroundRails"` is `"Wanted"` in CustomMapGen.json. Generate a **new** procedural map (not cached).

- **Sphere tank / dome in middle instead of outpost / Outpost not in middle**  
  The **center safe zone is compound.prefab**, not outpost. (1) When the game adds **outpost.prefab**, we redirect its position to map center. (2) When the game adds **bandit_town** and `AllowBanditCamp` is false, we block bandit and spawn the center prefab at map center by loading `monument/medium` and finding a prefab whose name contains **"outpost"** first, then **"compound"** (so we place compound at center). **Monument swap:** A **Save Prefix** on WorldSerialization.Save replaces compound/outpost with your custom map before the file is written: put `outpost.map` or `outpost.prefab.map` in CustomPrefabsFolder (default maps/prefabs). With `DebugLogging` you’ll see "monument/medium prefabs: …" and "Spawned center safe zone … (compound)". Deferred compound entities (ladder_trigger, assets/content/…) are spawned after all prefabs are spawned; see log for “Spawned N deferred compound entities”.

- **Ocean topology on mainland / random round lakes**  
  Ocean/water-edge topology is painted by **GenerateOceanTopology.Process** (0x80 where height ≤ 0.5) and by **AddToWaterMap** (ProceduralObject on prefabs: when monuments stamp terrain they can write water level and topology). With `DebugLogging` you’ll see "[DEBUG] GenerateOceanTopology.Process completed". If circles of ocean topology appear on dry land, possible causes: (1) GenerateOceanTopology painting too far inland, (2) AddToWaterMap on a monument prefab stamping water/topology at that monument’s position (e.g. offshore monument prefabs stamping when placed). CustomMapGen **RemoveOceanTopologyFromMainland** (GenerateHeightShoreFlattenPatches) removes ocean (0x80) from mainland cells (height > 0.5). **Island rings** are **disabled by default** (IslandRings.Enabled = false, Count = 0); when enabled, ocean topology is cleared in circle cells so they do not render as lakes.

- **Lots of errors during map generation**  
  - Shader/GPU errors (e.g. "shader is not supported on this GPU") are from the dedicated server having no GPU; they are normal and do not break procgen.  
  - "Prefab '...' requires asset scene 'AssetScene-props' to be loaded first" / "Couldn't find prefab" come from the game when spawning compound/prefab content that lives in `assets/content/`; that bundle is not loaded during procedural generation. CustomMapGen skips spawning those prefabs in PlaceMonumentsCompoundPatches (we only spawn from our list and skip any path under `assets/content/`). Remaining spam may be from the game’s own monument spawning; we don’t log "Failed to create entity" for skipped content prefabs.

---

# Part 2 – Core terrain & config (key members)

### TerrainGenerator
**Location:** `TerrainGenerator.cs`  
Singleton; creates terrain GameObject.
- **Methods:** `CreateTerrain()`, `CreateTerrain(int heightmapRes, int alphamapRes)`, `GetHeightMapRes()`, `GetSplatMapRes()`, `GetBaseMapRes()`
- **Properties:** `config` (TerrainConfig)

### TerrainConfig
**Location:** `TerrainConfig.cs`  
ScriptableObject: terrain material and splats.
- **Properties:** `CastShadows`, `GroundMask`, `WaterMask`, `Material`, `Splats` (SplatType[])
- **Methods:** `GetAridColors()`, `GetTemperateColors()`, etc., `GetCurrentGroundTypeNoAlloc(...)`

### WorldConfig
**Location:** `WorldConfig.cs`  
World generation flags and percentages.
- **Booleans:** `Powerlines`, `AboveGroundRails`, `BelowGroundRails`, `Rivers`, `MainRoads`, `SideRoads`, `Trails`, `UnderwaterLabs`
- **Percentages:** `PercentageTier0/1/2`, `PercentageBiomeArid/Temperate/Tundra/Arctic/Jungle`
- **Lists:** `PrefabBlacklist`, `PrefabWhitelist`
- **Methods:** `LoadFromWorldConfig(WorldConfig data)`, `LoadFromJsonString(string)`, `LoadScriptableConfigs()`, `IsPrefabAllowed(string)`

### TerrainMeta
**Location:** `TerrainMeta.cs`  
Central terrain state (referenced by almost all generation).
- **Maps:** `HeightMap`, `SplatMap`, `BiomeMap`, `TopologyMap`, `AlphaMap`, `WaterMap`, `PlacementMap`
- **Bounds:** `Position`, `Size`, `Center`, `Max`
- **Angles:** `LootAxisAngle`, `BiomeAxisAngle`
- **Path:** `TerrainMeta.Path` (TerrainPath) – `Roads`, `Rails`, `Rivers`, `Powerlines`, `LakeObjs`, `Monuments` (List&lt;MonumentInfo&gt;)

### PathList (paths in TerrainMeta.Path)
- **Properties:** `Name`, `Path` (PathInterpolator), `Width`, `Topology`, `Splat`, `Hierarchy`

---

# Part 2.5 – Map size, seed, and monument placement

### Size and seed

- **World.Size** – Map edge length (e.g. 3500). Determines:
  - Map bounds (`TerrainMeta.Position` / `TerrainMeta.Size`) used for random placement.
  - How many monuments can fit: `PlaceMonuments` uses `TargetCount`, `TargetCountWorldSizeMultiplier.Evaluate(World.Size)`, and per-prefab `MonumentInfo.MinWorldSize` (skips if `World.Size < component.MinWorldSize`).
- **World.Seed** – Drives determinism. Each procedural component gets `seed = (uint)(World.Seed + componentIndex)`. In **PlaceMonuments.Process**:
  - `SeedRandom.Range(ref seed, x, max)` picks candidate (x,z) in world bounds.
  - Same seed + size → same monument positions (and same terrain).

So: **size** = bounds + capacity (how many large/small monuments fit); **seed** = which positions are chosen.

### Generation order (terrain before monuments)

`WorldSetup.InitCoroutine` runs **all** procedural components in hierarchy order (`GetComponentsInChildren<ProceduralComponent>()`). The game does **not** place monuments before terrain:

1. **Terrain first** – Height, topology, biome, splat, erosion, roads, rivers, rails, etc. run (order depends on scene hierarchy). `TerrainMeta.HeightMap` and `TerrainMeta.TopologyMap` exist before any `PlaceMonuments` run.
2. **Monuments after** – `PlaceMonuments.Process(seed)` uses `heightMap.GetHeight(normX, normZ)` and `MonumentInfo.CheckPlacement` (which uses `TerrainMeta.TopologyMap` for Tier topology). So monuments are placed **after** base terrain (and roads/rivers/topology) exist.
3. **Terrain stamping after placement** – When a monument is actually spawned, `World.SpawnPrefab` calls `prefab.ApplyTerrainPlacements(position, rotation, scale)` then `prefab.Spawn(position, rotation, scale)`. The monument’s `Monument` (TerrainPlacement) component then **stamps** height/splat/topology at that position. So the final terrain under a monument is base terrain + monument stamp.

### What determines monument position

In **PlaceMonuments.cs** (`Process`):

- **Random candidate (x,z)** – `SeedRandom.Range(ref seed, x, max)` over map bounds; `Filter.GetFactor(normX, normZ)` (SpawnFilter) can reject by probability.
- **Height** – `heightMap.GetHeight(normX, normZ)` for the candidate point (terrain must already exist).
- **Topology** – `MonumentInfo.CheckPlacement` requires 3 of 4 monument-corner points to have the right **Tier** topology (Tier0/Tier1/Tier2 masks from `GenerateTopology`). So topology drives *where* a monument is allowed.
- **Distance rules** – Min distance same type, different type, dungeon entrance; boat path checks for water monuments.
- **Best of 8** – For each prefab, up to 10000 attempts; keeps best of 8 candidates by priority/distance, then adds one `SpawnInfo` and continues. Final list is the best of 8 “group” runs.
- **World.AddPrefab** – Each chosen `SpawnInfo` is passed to `World.AddPrefab("Monument", prefab, position, rotation, scale)`.

So position is **determined by seed + terrain (height/topology) + filters + distance rules** inside `PlaceMonuments.Process`. You cannot change it by reordering so that “monuments run before terrain”—they don’t, and they rely on existing terrain.

### Can monuments be moved?

**Yes.** Moving does **not** need to happen before terrain generation.

- **Where to move** – Patch **World.AddPrefab** with a **Prefix** and use `ref Vector3 position`. CustomMapGen already does this in **WorldAddPrefabPatches.cs**: for outpost/bandit it sets `position` to map center (and samples `TerrainMeta.HeightMap.GetHeight` for Y). The same pattern works for any monument: change `position` (and optionally rotation) in the Prefix; the rest of the pipeline uses the new value.
- **What happens after** – `World.AddPrefab` writes to serialization with the (possibly new) position, then calls `SpawnPrefab`. `SpawnPrefab` calls `prefab.ApplyTerrainPlacements(position, rotation, scale)` and `prefab.Spawn(position, rotation, scale)`. So:
  - Terrain is **stamped at the new position** (Monument.ApplyHeight/ApplySplat/ApplyTopology use the position passed in).
  - The prefab instance is spawned at the new position.
- **Conclusion** – Monuments can be moved by patching `World.AddPrefab` and modifying `position` (and height from HeightMap if needed). All monuments are still “placed” by PlaceMonuments first; the move is a redirect at add/spawn time, and terrain is generated (base) then stamped by each monument at its **final** position. No need to place monuments before terrain.

---

# Part 3 – Procedural components (key checks & constants)

All procedural components inherit `ProceduralComponent` and implement `Process(uint seed)`.

| Component | Key check (skips if false) | Notable constants |
|-----------|----------------------------|--------------------|
| **GenerateRailRing** | `World.Config.AboveGroundRails`, `World.Size >= MinWorldSize` | Width=4f, InnerFade=1f, OuterFade=32f |
| **GenerateRailLayout** | `World.Config.AboveGroundRails` | Width=4f |
| **GenerateRoadRing** | `World.Config.MainRoads`, `World.Size >= MinWorldSize` | Width=12f, OuterFade=8f |
| **GenerateRoadLayout** | `World.Config.SideRoads` (Road) or `World.Config.Trails` (Trail) | RoadWidth=10f, TrailWidth=4f |
| **GeneratePowerlineLayout** | `World.Config.Powerlines` | — |
| **GenerateRiverLayout** | `World.Config.Rivers` | Width=8f, OuterFade=64f |
| **GenerateDungeonGrid** | `World.Config.BelowGroundRails` | CellSize=216, LinkRadius=3f |
| **GenerateCliffTopology** | — | slopeCutoff=30f, splatCutoff=0.4f |

**Native (DLL) calls:** GenerateHeight, GenerateTopology, GenerateBiome, GenerateSplat use RustNative; patch inputs (e.g. World.Config) rather than the native methods.

---

# Part 4 – Harmony patching notes

1. **ProceduralComponent.Process()** – Patch with `[HarmonyPatch(typeof(ComponentName), nameof(ComponentName.Process))]`. Use **Prefix** to skip (return false) or **Postfix** to run after.
2. **World.Config** – Patch `WorldConfig.LoadFromWorldConfig` and/or `WorldConfig.LoadScriptableConfigs` (Postfix) to apply CustomMapGen settings. Also patch private `MergeScriptableConfig` with string name: `[HarmonyPatch(typeof(WorldConfig), "MergeScriptableConfig")]`.
3. **TerrainMeta.Path** – Contains `Roads`, `Rails`, `Rivers`, `Powerlines`, `LakeObjs`, `Monuments`. Modify in Postfix after the component that fills them.
4. **Seed** – All `Process(uint seed)` take the same seed; changing it changes generation and can break determinism.

### Example: disable a component
```csharp
[HarmonyPatch(typeof(GeneratePowerlineLayout), nameof(GeneratePowerlineLayout.Process))]
public static class GeneratePowerlineLayout_Process_Patch
{
    static bool Prefix() {
        if (CustomMapGen.IsCustomMapGenEnabled()) {
            var config = CustomMapGen.Instance.GetConfig();
            if (config.RemoveSmallPowerLines && config.RemoveLargePowerLines)
                return false; // skip original
        }
        return true;
    }
}
```

### Example: apply config to WorldConfig
```csharp
[HarmonyPatch(typeof(WorldConfig), nameof(WorldConfig.LoadFromWorldConfig))]
public static class WorldConfig_LoadFromWorldConfig_Patch
{
    static void Postfix(WorldConfig __instance) {
        if (!CustomMapGen.IsCustomMapGenEnabled()) return;
        var config = CustomMapGen.Instance.GetConfig();
        __instance.AboveGroundRails = config.GenerateAboveGroundTrainTracks == "Wanted";
        __instance.Powerlines = !config.RemoveSmallPowerLines && !config.RemoveLargePowerLines;
        // ...
    }
}
```

---

# Part 5 – Prefab / entity extraction

To add custom entities to monuments, extract entity data from RustEdit prefabs (prefab path, local position, rotation, scale). Use that data in a `PlaceMonuments.Process` Postfix to spawn entities at each matching monument.  
**See `PREFAB_EXTRACTION_GUIDE.md`** for step-by-step extraction and code generation.

---

# Part 6 – Reference: HarmonyCustomGenerator repo (for future borrowing)

A clone of the public [HarmonyCustomGenerator](https://github.com/publicrust/HarmonyCustomGenerator) repo is kept locally for comparison and borrowing when extending CustomMapGen.

**Local path:** `D:\!RustServer\.cursor\HarmonyMods\HarmonyCustomGenerator`

| Their path | Contents | Our equivalent / assembly target |
|------------|----------|-----------------------------------|
| **CustomGenerator/Patches/** | | |
| `WorldSetup.cs` | Warmup skip, map size/seed, save path/name, map image trigger | `BootstrapPatches.cs`, `FileSystemWarmupPatches.cs`, `WorldMapSettingsPatches.cs`, `WorldSetupMapImagePatches.cs`; assembly: `Bootstrap`, `WorldSetup.InitCoroutine`, `World` getters |
| `World.cs` | World name/folder/file overrides | `WorldMapSettingsPatches.cs`; assembly: `World.get_Name`, `get_MapFileName`, `get_MapFolderName`, `get_SaveFileName`, `get_SaveFolderName` |
| `RoadRing.cs` | Ring road control | `GenerateRoadRingPatches.cs`; assembly: `GenerateRoadRing.Process` |
| `RailRing.cs` | Rail ring control | `GenerateRailPatches.cs`; assembly: `GenerateRailRing.Process`, `GenerateRailLayout.Process` |
| `River.cs` | River removal | `GenerateRiverLayoutPatches.cs`; assembly: `GenerateRiverLayout.Process` |
| `Monuments.cs` | Monument filters, distances, counts | `PlaceMonumentsFilterPatches.cs`, `PlaceMonumentsRoadsidePatches.cs`, etc.; assembly: `PlaceMonuments.Process`, `World.AddPrefab` |
| **CustomGenerator/Custom/** | | |
| `SwapMonument.cs` | Replace vanilla monuments with custom .map prefabs (post-save .map edit) | `PostSaveSwapPatches.cs` (run on LoadingScreen.Update("DONE")); assembly: `WorldSerialization` (Rust.World), `PrefabData`/`VectorData` (ProtoBuf) |
| **CustomGenerator/Utility/** | | |
| `MapImage.cs` | Splat/height map render, monument names, grid | `WorldSetupMapImagePatches.cs`; assembly: `MapImageRenderer.Render`, `WorldSetup.InitCoroutine` |
| `Logger.cs`, `EnumParser.cs` | Logging and config parsing | CustomMapGen uses `UnityEngine.Debug.Log` and `MapGenConfig` / JSON |

**Other useful paths in the repo:**
- `CustomPrefabs/` – example `.map` templates (outpost, fishing villages, bandit camp, stables).
- `Examples/prefabs/` – example custom monuments (gas_station_1, supermarket_1, fishing_village_c, etc.).
- `USAGE.md`, `SWAP_MONUMENTS.md` – usage and monument-swap setup.

When borrowing: align with our assembly base path (`oxide/!Assembly-CSharp-RUST`) and our config (`HarmonyConfig/CustomMapGen.json`); their config is `CustomGeneratorCFG.json`.

---

*Single reference for CustomMapGen and map generation. Paths relative to `oxide/!Assembly-CSharp-RUST`.*
