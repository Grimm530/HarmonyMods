# HarmonyCustomGenerator (publicrust) vs CustomMapGen – Comparison

Comparison of [publicrust/HarmonyCustomGenerator](https://github.com/publicrust/HarmonyCustomGenerator) with this project’s CustomMapGen, using `MAP_GENERATION_ASSEMBLY_REFERENCE.md` and current implementation.

---

## Summary verdict

- **Generator behavior (roads, rails, rivers, monuments, terrain):** CustomMapGen is **at least as capable** and in several areas **more detailed** (monument categories, terrain, config re-application).
- **Documentation:** CustomMapGen is **stronger** – you have a full assembly reference, patch→target table, and troubleshooting; the public repo does not.
- **Missing in CustomMapGen:** Map **save/layout** options, **Skip Asset Warmup**, **Map Image Generator**, and **Monument Swapping** (custom .map prefabs + two map versions). Those are the main gaps to consider.

---

## 1. What the public repo has (from README / USAGE / SWAP_MONUMENTS)

| Area | Feature | Public repo | Our CustomMapGen |
|------|---------|-------------|-------------------|
| **QoL** | Skip Asset Warmup on start | ✅ | ❌ Not implemented |
| **Map settings** | Generate map over default limits (e.g. &gt;3500, ≤6000) | ✅ | ❌ Not in our mod (server/world config) |
| **Map settings** | Generate new map every time | ✅ | ❌ Server/world config |
| **Map settings** | Save map in specific folder | ✅ | ❌ Server/world config |
| **Map settings** | Save map with specific name | ✅ | ❌ Server/world config |
| **Generator** | Road Ring on any map | ✅ | ✅ `GenerateRingRoad` + `GenerateRoadRingPatches` |
| **Generator** | Roadside monuments on/off | ✅ | ✅ Via monument filters + roadside |
| **Generator** | Roadside objects (e.g. car wrecks) | ✅ | ✅ `RemoveCarWrecks` + `VehicleCarWrecksPatches` / `PlaceMonumentsRoadsidePatches` |
| **Generator** | Rail Ring on any map | ✅ | ✅ `AboveGroundRails` + `GenerateRailPatches` |
| **Generator** | Railside monuments/objects | ✅ | ✅ Config + filters (we don’t name “railside” explicitly but rails follow game logic) |
| **Generator** | Remove rivers | ✅ | ✅ `RemoveRivers` + `GenerateRiverLayoutPatches` |
| **Generator** | Remove tunnel entrances | ✅ | ✅ `RemoveUndergroundTunnels` (BelowGroundRails) |
| **Generator** | Tier percentages (Tier0/1/2) | ✅ | ✅ `TerrainConfiguration.TierConfig` |
| **Generator** | Biome percentages (Arid, Temperate, Tundra, Arctic) | ✅ | ✅ `TerrainConfiguration.BiomeConfig` (+ Jungle) |
| **Generator** | Unique environment (oasis, canyons, lakes) | ✅ | ✅ Oases, Canyons, Lakes config + patches |
| **Monuments** | Full monument placement configuration | ⬜ Unchecked in their README | ✅ We have filters/counts per category |
| **Monuments** | Distances between monuments | ✅ | ✅ Implicit via game + our filters |
| **Monuments** | Specific monument counts | ✅ | ✅ Min/Max and Blocked/Desired per category |
| **Monuments** | Filters (biome, splat, topology) | ✅ | ✅ BiomePreferences, etc. in our monument configs |
| **Map image** | Generate splat/height map | ✅ | ❌ Not implemented |
| **Map image** | Generate monument names | ✅ | ❌ Not implemented |
| **Map image** | Generate map grid | ✅ | ❌ Not implemented |
| **Extra** | **Monument Swapping** (vanilla → custom .map prefabs) | ✅ | ✅ Post-save swap (PostSaveSwapPatches + LoadingScreen DONE) |
| **Extra** | Save both map versions (with/without swaps) | ✅ | ✅ `SwapMonuments.SaveBothVersions` → save as `*.swapped.map` |
| **Extra** | Russian/English config support | ✅ | ❌ We use single JSON (could add lang keys) |

---

## 2. What we have that they don’t (or that’s better documented)

| Area | Feature | Notes |
|------|---------|--------|
| **Docs** | **MAP_GENERATION_ASSEMBLY_REFERENCE.md** | Full assembly file list, patch→target table, TerrainMeta/WorldConfig members, procgen order, troubleshooting. Public repo has no equivalent. |
| **Config reliability** | **ProcgenConfigApplyPatches** | Re-applies our config at procgen start so BlockedPrefabs, AboveGroundRails, etc. survive server world config. Critical for “config not sticking” issues. |
| **Config hooks** | Multiple WorldConfig entry points | We patch LoadFromWorldConfig, LoadScriptableConfigs, MergeScriptableConfig, LoadFromJsonString, LoadFromJsonFile so config is applied whenever world config is loaded. |
| **Compound/content** | **PlaceMonumentsCompoundPatches** | Skips spawning from `assets/content/` during procgen to avoid “prefab not found” spam; they may rely on game behavior only. |
| **Outpost** | **TrySpawningOutpostInCenter** | Redirects outpost/bandit position to map center in `World.AddPrefab`; same idea as “move monument,” we just do it for safezone. |
| **Cargo** | **EmbedCargoShipPath** | CargoNotifierPatches; not mentioned in their feature list. |
| **Building** | **AllowBuildingOnRoads** | GenerateRoadTopologyPatches + PreventBuildingMonumentTagPatches; they don’t list this. |
| **Terrain** | **FlattenShoreAndBay** | GenerateHeightShoreFlattenPatches; explicit shore/bay flattening. |
| **Terrain** | **MountainConfig.ReduceMountains** | MountainPatches. |
| **Terrain** | **BiomeAxisAngle / LootAxisAngle** | TerrainMetaPatches (reflection); control biome/loot axis. |
| **Monuments** | **Rich per-category config** | OilRigConfigurations, Safezones, LargeMonuments, SmallMonuments, Harbors, WaterWells, Caves, Mountains, Quarries, Icebergs, IceLakes, Ruins with Blocked/Desired/BiomePreferences/CustomPrefab. |
| **Prefabs** | **BlockedPrefabs** | Applied to PrefabBlacklist; blocks rocks/decor by path substring; documented in our reference. |
| **Implementation status** | **IMPLEMENTATION_STATUS.md** | Clear list of what’s wired vs not; they don’t publish this. |

---

## 3. What we added (implemented)

- **Skip Asset Warmup** – `FileSystemWarmupPatches.cs` skips `FileSystem_Warmup.Run` when `SkipAssetWarmup` is true.
- **Map settings** – `BootstrapPatches.cs` applies `MapSizeOverride` (3500–6000) and `ForceNewMapEachTime` (random seed) before `World.InitSize`/`InitSeed`. `WorldMapSettingsPatches.cs` overrides `World.Name`, `MapFileName`, `MapFolderName`, `SaveFileName`, `SaveFolderName` when `MapSettings.SaveFolderOverride` / `SaveNameOverride` are set.
- **Map Image Generator** – `WorldSetupMapImagePatches.cs` runs after procgen, calls `MapImageRenderer.Render`, and saves PNG to `MapImage.OutputFolder` (or current directory). `IncludeMonumentNames` / `IncludeGrid` are reserved for future overlay.
- **Monument Swapping** – **Post-save swap:** When `LoadingScreen.Update("DONE")` runs, `PostSaveSwap.Run(mapPath)` loads the saved .map file, finds prefabs whose shortname matches custom `.map` files in `CustomPrefabsFolder`, removes the vanilla prefab, and adds the custom .map’s prefabs with transformed position/rotation (same logic as HarmonyCustomGenerator). Uses reflection for game types (PrefabData/VectorData). `SaveBothVersions` saves the swapped map as `*.swapped.map` and keeps the original.
- **Language** – Config key `"Language": "en"` (or `"ru"`) for future RU/EN support.

---

## 3b. What we were missing (and what would need to change)

### 3b.1 ~~Skip Asset Warmup~~ — Implemented (FileSystemWarmupPatches)

### 3b.2 ~~Map save/layout~~ — Implemented (BootstrapPatches, WorldMapSettingsPatches)

### 3b.3 ~~Map Image Generator~~ — Implemented (WorldSetupMapImagePatches; monument names/grid reserved for future)

### 3b.4 Monument Swapping — Implemented (PostSaveSwapPatches + LoadingScreen DONE); SaveBothVersions implemented

- **Post-save swap:** After map save, `PostSaveSwap.Run(mapPath)` loads the .map, swaps matching monuments with custom `.map` prefabs from `CustomPrefabsFolder`, then saves (overwrite or `*.swapped.map`).
- **Save both versions:** When `SwapMonuments.SaveBothVersions` is true, the swapped map is saved as `*.swapped.map` and the original file is kept.

---

## 4. What might need fixing on our side

- **Reference vs code:** In `MAP_GENERATION_ASSEMBLY_REFERENCE.md` some patch names are listed (e.g. `GenerateRiverLayoutPatches`, `LakeInfoPatches`, `VehicleCarWrecksPatches`, `WorldAddPrefabPatches`). Confirm that every listed patch file exists under `Patches/` and that the target methods (e.g. `GenerateRiverLayout.Process`, `World.AddPrefab`) match the game build you use. Your reference is already very close to the repo layout.
- **GenerateRoadRingPatches / GenerateRoadTopologyPatches:** Reference says “Controls ring road” and “Controls roadside / AllowBuildingOnRoads”. Verify that when `GenerateRingRoad` is `"NotWanted"` we actually skip or disable the ring road, and that `AllowBuildingOnRoads` is applied in both topology and building-check (PreventBuildingMonumentTagPatches).
- **UnderwaterLabsMinAmount / UnderwaterLabsMaxAmount:** IMPLEMENTATION_STATUS says “Stored but NOT enforced”. If you want parity with “specific monument counts,” consider adding a patch that enforces min/max for underwater labs similar to lakes/oases.

Nothing in the public repo suggests their core generator logic is “better”; our design (config re-apply, multiple WorldConfig hooks, compound skip, detailed monument config) is solid and better documented.

---

## 5. Conclusion

- **Generator and terrain:** CustomMapGen is on par or ahead (more categories, terrain options, and config application). Your **MAP_GENERATION_ASSEMBLY_REFERENCE.md** is a real advantage.
- **Gaps to consider adding:**  
  - **Monument Swapping** (custom .map prefabs + optional two map versions).  
  - **Map Image Generator** (splat/height, monument names, grid) if you want it.  
  - **Skip Asset Warmup** as QoL.  
  - Map **save folder/name/new map/size** either in docs or via patches if you want to mirror the public repo.
- **Fixes:** Align reference with actual patch files/targets, enforce UnderwaterLabs min/max if desired, and double-check ring road and AllowBuildingOnRoads behavior.

Overall: **your implementation and documentation are stronger for map generation and config.** The public repo adds convenience (warmup, save options, map images) and a dedicated **Monument Swapping** workflow; we don’t have those yet.
