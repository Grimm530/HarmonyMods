# CustomMapGen vs HarmonyCustomGenerator — Verification Report

This document compares CustomMapGen implementation against the reference repo (HarmonyCustomGenerator) and the MAP_GENERATION_ASSEMBLY_REFERENCE.md to confirm we are "doing it right."

---

## 1. SwapMonument / PostSaveSwap

| Aspect | HarmonyCustomGenerator | CustomMapGen | Verdict |
|--------|------------------------|-------------|---------|
| **When run** | `LoadingScreen.Update("DONE")` **Prefix** → SwapMonument.Initiate(path) then MapImage then **Application.Quit()** | `LoadingScreen.Update("DONE")` **Postfix** → deferred spawn then PostSaveSwap.Run(mapPath); **no quit** | ✅ Correct for server: we must not quit; Postfix is appropriate so game finishes loading. |
| **Map path** | Built from tempData (mapsize, mapseed) + Config.mapSettings.MapName + folder "maps" | Built from `World.MapFolderName` + `World.MapFileName`, with fallback to `Environment.CurrentDirectory` | ✅ We use game's actual save path; more robust. |
| **Load map** | `_mainMap.Load(mapPath)`; direct `.world.prefabs` (they reference game types) | `_mainMap.Load(mapPath)`; reflection for `world` / `prefabs` (no ProtoBuf ref) | ✅ Same behavior; we avoid game assembly types. |
| **Match monuments** | `StringPool.Get(x.id).Contains(monument.prefabShortname)`; shortname = `Path.GetFileNameWithoutExtension(file)` → e.g. "outpost.prefab" for "outpost.prefab.map" | Same idea; we use `prefabShortname` from file; **plus** we match **"compound"** when swapping outpost (center safe zone) | ✅ We are correct; center uses compound.prefab, so matching compound for outpost.prefab.map is right. |
| **CreatePrefabFromMap** | `MapHander.CreatePrefabFromMap(VectorData startPos, VectorData rotation, List<PrefabData> prefabs)`; id `2749405185` → `504351302`, scale `(0,0,0)` for that id; position/rotation math same | `MapHandlerReflection.CreatePrefabFromMap(object startPos, object startRot, IList prefabs)`; same id remap and scale; same Calculate/CalculateRot/RotateVector logic via reflection | ✅ Logic aligned with reference; we use reflection for PrefabData/VectorData. |
| **Save** | `_mainMap.Save(mapPath)` or `.Replace(".map", ".swapped.map")` when SaveBothMaps | We save over same path (configurable); no separate .swapped.map in current flow | ✅ Documented in REF; optional .swapped.map can be added if desired. |

**Conclusion:** PostSaveSwap and MapHandlerReflection are aligned with SwapMonument/MapHander. We additionally handle compound-for-outpost and run in Postfix without quitting.

---

## 2. Rail Ring (GenerateRailRing / GenerateRailLayout)

| Aspect | HarmonyCustomGenerator | CustomMapGen | Verdict |
|--------|------------------------|-------------|---------|
| **Disable rails** | Prefix: `MinWorldSize(__instance) = int.MaxValue` so size check fails | We don't disable rails by MinWorldSize; we only force **enabled** when "Wanted" | ⚠️ We have no "NotWanted" path that sets MinWorldSize to max; we could add it for parity. |
| **Enable rails** | Prefix: `MinWorldSize(__instance) = 0`; **Transpiler**: change constant `5000` → `0` in IL (bypasses a size check) | Prefix: `World.Config.AboveGroundRails = true`; `__instance.MinWorldSize = (int)World.Size` so size check passes | ✅ We don't rely on Transpiler; we set MinWorldSize so the component runs at any size. REF says check is `World.Size >= MinWorldSize`. |
| **GenerateRailLayout** | Not patched separately | Prefix: set `World.Config.AboveGroundRails = true` when "Wanted" | ✅ REF: both GenerateRailRing and GenerateRailLayout check AboveGroundRails; we cover both. |

**Conclusion:** We are doing it right for enabling rails. Optional: add explicit "NotWanted" that sets MinWorldSize to int.MaxValue for GenerateRailRing (and optionally GenerateRailLayout skip) to mirror reference.

---

## 3. Road Ring (GenerateRoadRing)

| Aspect | HarmonyCustomGenerator | CustomMapGen | Verdict |
|--------|------------------------|-------------|---------|
| **Disable ring** | Prefix: `MinWorldSize(__instance) = int.MaxValue` | Prefix: `return false` when `GenerateRingRoad == "NotWanted"` | ✅ Same effect: ring road not generated. |
| **Enable ring** | Prefix: `MinWorldSize(__instance) = 0`; Transpiler: `5000` → `0` | Prefix: set `World.Config.MainRoads = true` when "Wanted" | ⚠️ We don't set `MinWorldSize` for road ring. If game uses MinWorldSize (e.g. 3500/5000), we rely on default; if it's 5000 we might skip on smaller maps. |
| **PlaceMonumentsRoadside** | Prefix: `MinSize(__instance) = 99999` to effectively disable roadside monuments when not wanted | We have PlaceMonumentsRoadsidePatches + WorldAddPrefabPatches to block car wrecks | ✅ We control roadside/car wrecks via config and patches. |
| **PlaceRoadObjects** | Prefix: `return false` when not GenerateSideObjects | We have VehicleCarWrecksPatches etc. | ✅ Covered. |

**Recommendation:** Consider adding a Prefix for GenerateRoadRing that sets `__instance.MinWorldSize = 0` (or `(int)World.Size`) when "Wanted", so ring road runs on all map sizes the game allows, matching reference behavior.

---

## 4. River (GenerateRiverLayout)

| Aspect | HarmonyCustomGenerator | CustomMapGen | Verdict |
|--------|------------------------|-------------|---------|
| **Disable rivers** | `Timing.Start("Processing World")` Prefix: `World.Config.Rivers = false` | `GenerateRiverLayout.Process` Prefix: `return false` | ✅ We skip the component entirely; no rivers. REF says river layout checks World.Config.Rivers; we achieve same by not running Process. |

**Conclusion:** We are doing it right; we don't need to hook Timing.

---

## 5. World / Map Settings (size, seed, folder, file name)

| Aspect | HarmonyCustomGenerator | CustomMapGen | Verdict |
|--------|------------------------|-------------|---------|
| **Bootstrap** | `Bootstrap.StartupShared` Prefix: nav_disable, nav_wait, logging | `Bootstrap.DedicatedServerStartup` Prefix: Server.worldsize, Server.seed from config | ✅ We target **server** bootstrap; they target shared (client/server). We are correct for dedicated server. |
| **Size/seed capture** | `World.InitSize`, `World.InitSeed` Prefix: store in tempData | We don't patch InitSize/InitSeed; we set Server.worldsize/Server.seed in Bootstrap | ✅ Size/seed come from ConVar; World reads them. We set ConVar before World init. |
| **World.get_Size** | Postfix: `__result = tempData.mapsize` when OverrideSizes | We don't override World.Size; we set Server.worldsize so World gets correct size | ✅ Size flows from ConVar; no need to patch get_Size if ConVar is set first. |
| **Map folder** | `World.get_MapFolderName` Postfix: override to "maps" full path | `World.get_MapFolderName` Postfix: override from config (SaveFolderOverride) | ✅ Aligned. |
| **Map file name** | `World.get_MapFileName` Postfix: format from MapName + size + seed | `World.get_MapFileName` Postfix: SaveNameOverride + size + seed + 273 + ".map" | ✅ Aligned; we use 273 for compatibility. |
| **CanLoadFromDisk** | Postfix: `__result = false` when GenerateNewMapEverytime | We set Server.seed randomly when ForceNewMapEachTime; game may still load if name matches | ⚠️ Reference forces CanLoadFromDisk = false so map is never loaded from disk. We could add this patch for "always generate new map" parity. |

**Conclusion:** World/map settings are aligned. Optional: add patch for `World.CanLoadFromDisk` when "force new map each time" is enabled.

---

## 6. WorldSetup / LoadingScreen / Map Image

| Aspect | HarmonyCustomGenerator | CustomMapGen | Verdict |
|--------|------------------------|-------------|---------|
| **LoadingScreen.Update("DONE")** | Prefix: run SwapMonument, MapImage, then Application.Quit() | Postfix: run deferred compound spawn, then PostSaveSwap (when RunPostSaveSwap), no quit | ✅ Correct for server: no quit; Postfix after load done. |
| **Map image** | MapImage.RenderMap(0.75f, 150) using MapImageRender.Render (splat/height, monuments, grid) | WorldSetupMapImagePatches: Postfix on WorldSetup.InitCoroutine or after gen; we render and save when MapImage enabled | ✅ We trigger map image from WorldSetup/InitCoroutine; REF says WorldSetup.InitCoroutine for map image. |
| **TerrainMeta init** | They save TerrainMeta/Texturing/Path in tempData for MapImage | We use TerrainMeta / TerrainHeightMap etc. when we run; no tempData | ✅ We don't need tempData; we run in context where TerrainMeta exists. |

**Conclusion:** We are doing it right; we don't quit and we run swap + image in the right places.

---

## 7. Monuments (PlaceMonuments, filters, roadside)

| Aspect | HarmonyCustomGenerator | CustomMapGen | Verdict |
|--------|------------------------|-------------|---------|
| **PlaceMonuments.Process** | Prefix: per-folder config (TargetCount, MinWorldSize, DistanceSame/Different, Filter) from Config.Monuments.monuments; list populated from WorldSetup_InitCoroutine Prefix (GetComponentsInChildren<PlaceMonuments>) | We have PlaceMonumentsFilterPatches, PlaceMonumentsRoadsidePatches, PlaceMonumentsOutpostPatches, PlaceMonumentsCompoundPatches, etc.; we don't build a monument list from scene | ✅ Different design: we use static config (BlockedPrefabs, filters, etc.) and don't need to scan PlaceMonuments at startup. REF lists our patches; we cover filter, roadside, outpost, compound. |
| **Tunnel entrances / Oasis / Canyon / Lake** | PlaceMonuments_Process Prefix: set MinWorldSize 0 or 999999 by folder (tunnel-entrance, unique_environment/oasis, etc.) | PlaceMonumentsPatches (oasis/canyon), PlaceCliffsPatches, LakeInfoPatches, etc. | ✅ We have equivalent controls via config and patches. |
| **Car wrecks / roadside** | PlaceDecorUniform "Roadside Wrecks" return false; PlaceMonumentsRoadside MinSize 99999; PlaceRoadObjects return false | PlaceMonumentsRoadsidePatches, VehicleCarWrecksPatches, WorldAddPrefabPatches (block car wrecks) | ✅ Covered. |

**Conclusion:** Monument and roadside behavior are covered; we use config-driven patches instead of scanning scene monuments.

---

## 8. Road Topology (Allow building on roads)

| Aspect | HarmonyCustomGenerator | CustomMapGen | Verdict |
|--------|------------------------|-------------|---------|
| **Target** | `GenerateRoadTopology.Process` **Postfix** | `GenerateRoadTopology.MarkRoadside` **Postfix** | ⚠️ Different target. REF says our patch is MarkRoadside. |
| **Logic** | ImageProcessing.Dilate2D: where (map & 49) != 0, replace ROAD with BUILDING | Clear topology flag 4096 (roadside) from all cells so building allowed | Both achieve "allow building on roads." Reference is more surgical (dilate + condition); we strip roadside flag globally. |

**Conclusion:** Functionally correct; we use MarkRoadside as in MAP_GENERATION_ASSEMBLY_REFERENCE. If the game ever relies on ROAD vs BUILDING in a way we break, we could consider a Process Postfix + Dilate2D approach like the reference.

---

## 9. Config / Utility

| Aspect | HarmonyCustomGenerator | CustomMapGen | Verdict |
|--------|------------------------|-------------|---------|
| **Config file** | HarmonyConfig/CustomGenerator.json | HarmonyConfig/CustomMapGen.json | ✅ Our naming. |
| **Config structure** | MapSettings, Generator (Road, Rail, UniqueEnviroment, RemoveRivers, etc.), Swap, Monuments | MapSettings, TerrainConfiguration, SwapMonuments, BlockedPrefabs, etc. | ✅ Different but REF and our docs align. |
| **Logger** | CustomGenerator.Utility.Logging (file + Debug.Log) | UnityEngine.Debug.Log + optional DebugLogging in config | ✅ We don't need file logger for server. |
| **EnumParser** | Parse filter enums (BiomeType, SplatType, TopologyAll, etc.) for monument SpawnFilter | We don't expose per-monument filter enums in the same way; we use BlockedPrefabs and simpler options | ✅ Acceptable; we have different config surface. |
| **MapImage** | Full render with fonts, monument names, grid, PNG/JPG | WorldSetupMapImagePatches call game's MapImageRenderer or equivalent; we save when enabled | ✅ REF: we use MapImageRenderer / WorldSetup.InitCoroutine. |

**Conclusion:** Config and utilities are appropriate for our design.

---

## 10. Summary: Are We Doing It Right?

- **SwapMonument / PostSaveSwap:** ✅ Aligned; we add compound matching and server-safe (no quit) behavior.
- **MapHandler / CreatePrefabFromMap:** ✅ Same id remap and position/rotation math; we use reflection.
- **Rails:** ✅ Enabling rails is correct; optional: add "NotWanted" path (MinWorldSize = int.MaxValue).
- **Road ring:** ✅ Disable is correct; optional: set MinWorldSize when "Wanted" so ring runs at all sizes.
- **Rivers:** ✅ Correct (skip Process).
- **World / Bootstrap / Map settings:** ✅ Correct for server; optional: CanLoadFromDisk when force new map.
- **LoadingScreen / Map image:** ✅ Correct (Postfix, no quit).
- **Monuments / roadside:** ✅ Covered with our patch set.
- **Road topology (Allow building on roads):** ✅ We use MarkRoadside as per REF; behavior correct.

**Optional improvements (for parity with reference):**

**Implemented parity improvements:**

1. **GenerateRoadRing:** When `GenerateRingRoad == "Wanted"`, set `__instance.MinWorldSize = 0` so ring road is not skipped on smaller maps (e.g. 3500).
2. **GenerateRailRing:** When rails are "NotWanted", set `MinWorldSize = int.MaxValue` so the component skips.

**Design choice (no forced maps):** CustomMapGen does **not** force new map generation. Map generation runs only when the map file is missing (game default). We do not patch `World.CanLoadFromDisk` nor set a new seed each startup; if the map file exists, the game loads it.

No critical gaps found; CustomMapGen is doing it right relative to the reference and MAP_GENERATION_ASSEMBLY_REFERENCE.md.
