# CustomMapGen Harmony Mod

A Harmony mod for customizing procedural map generation in Rust. This mod allows you to control various aspects of procedural map generation including cliffs, lakes, islands, powerlines, and above-ground railroads.

## Features

- **Disable Cliffs**: Turn off cliff rock generation completely
- **Control Lake Count**: Specify exact number of lakes (0 to disable, -1 for default)
- **Control Island Count**: Specify exact number of islands (0 to disable, -1 for default)
- **Powerlines** (config): `true` / `false` – enable or disable powerline generation
- **Ziplines** (config): `true` / `false` – enable or disable zipline launch/arrival points during map gen
- **Disable Above-Ground Rails**: Turn off above-ground railroad generation

**Train tracks and tunnel entrances:** With **ConnectRailsToTunnelEntrances** (default `true`), the mod adds rail segments from the above-ground rail network to each train tunnel entrance after the dungeon grid runs, so tracks connect visually instead of running past. Requires above-ground rails and tunnel entrances (`GenerateAboveGroundTrainTracks`: `"Wanted"`, `RemoveUndergroundTunnels`: `false`). Set to `false` to use vanilla behavior.

### QoL & Map Settings (HarmonyCustomGenerator parity)

- **Skip Asset Warmup** (`SkipAssetWarmup`): Skip asset warmup on server start to reduce startup time.
- **Map Settings** (`MapSettings`): Override map size (3500–6000), save folder, save name; force new seed each startup (`ForceNewMapEachTime`).
- **Map Image** (`MapImage`): Renders at `LoadingScreen.Update("DONE")` – after Finalizing World, matching HarmonyCustomGenerator. High-quality splat/height map PNG with monument labels. **Fonts:** Place `dinprobold.otf`, `dinpro.otf`, `PermanentMarker.ttf` in `maps/images/resources` (default path). No remote download.
- **Monument Swapping** (`SwapMonuments`): After map save, replace vanilla monuments with custom `.map` prefabs from `CustomPrefabsFolder` (e.g. `maps/prefabs/harbor_1.prefab.map`). Uses post-save .map edit (same approach as HarmonyCustomGenerator). See **Custom monuments** below.
- **Language** (`Language`): Config key for future RU/EN support (e.g. `"en"`, `"ru"`).

## Installation

1. Build the mod using the build script:
   ```powershell
   .\build.ps1
   ```
   Or on Windows:
   ```cmd
   build.bat
   ```

2. The DLL will be automatically copied to `HarmonyMods/CustomMapGen.dll`

3. Restart your Rust server - the mod will load automatically

## Custom monuments (Monument Swapping)

When `SwapMonuments.Enabled` is true, after procedural map generation and save, the mod loads the saved `.map` file, finds vanilla monument prefabs by shortname, and replaces them with custom `.map` prefabs from `CustomPrefabsFolder`. Position and rotation of the original monument are preserved.

**File naming:** The custom file must be named with `.prefab.map` extension, e.g.:
- `harbor_1.prefab.map`
- `fishing_village_a.prefab.map`
- `outpost.prefab.map` — replaces the **center safe zone**; the game uses prefab name `compound` (not "outpost"), so the mod matches both when you have `outpost.prefab.map`

**Where to put files:** Place `.map` files in the folder set by `SwapMonuments.CustomPrefabsFolder` (default: `maps/prefabs`), relative to the server root.

**Creating a custom monument (e.g. custom outpost):**
1. Open **RustEdit**, load or build your monument.
2. For a fully custom monument (no original prefab inside), add a **SpawnPoint** at **(0, 0, 0)** — this is the center of the original monument; misplacement will misalign the swap.
3. Ensure **SpawnPoint** or the root monument is the **first object** in the hierarchy.
4. Save as `<monument_name>.prefab.map` (e.g. `outpost.prefab.map`) and put it in `maps/prefabs/`.

**Allow vanilla to spawn:** When using custom monuments, the **vanilla** monument (e.g. outpost or bandit_town) must be allowed to spawn so it is written to the map. After save, the mod replaces it with your custom `.map` by matching shortname. So do not block the monument type you are replacing—use `TrySpawningOutpostInCenter` to move it to center if desired; post-save swap then replaces it with `outpost.prefab.map` (or `bandit_town.prefab.map` if you use a custom bandit).

**Config:** In `CustomMapGen.json`, set `"SwapMonuments": { "Enabled": true, "RunPostSaveSwap": true, "CustomPrefabsFolder": "maps/prefabs", "SaveBothVersions": false, "PlacementHeightOffset": 0, "TrySpawningOutpostInCenter": true, "AllowBanditCamp": false, "FillBanditSlotWithMonument": true, "UseBlockedOutpostSlotForRelocation": true }`. If `SaveBothVersions` is true, the swapped map is saved as `*.swapped.map` and the original is kept.

**Placement (sunk or off alignment):** The mod places your custom monument so that the **first prefab** in your `.map` file ends up exactly at the vanilla monument’s position and rotation. If your monument appears **sunk into the ground**, either (1) in RustEdit ensure the **origin** (first object / SpawnPoint) is at **ground level** of the building (Y = 0 for the floor), or (2) add a **height offset** in config: `"PlacementHeightOffset": 1.5` (or 2) to raise the whole monument. If it’s **shifted or rotated wrong**, the first prefab in the export is not at the center or forward direction you expect — re-export with the root/SpawnPoint at the center and ground of the monument.

**When does monument swap run?**  
PostSaveSwap runs **immediately after** the procedural map file is saved (`WorldSerialization.Save` postfix). This does not depend on LoadingScreen.Update("DONE"), so swap runs on headless/dedicated servers even when "DONE" is never called. No delay and no blocking at the end of InitCoroutine.

**Shore flatten:** Only flattens (smooths) height near water (shores and bays). It does not paint or modify topology; the game paints what needs to be painted.

**Troubleshooting:** If you need to disable swap/injection, set `"RunPostSaveSwap": false`.

## Invalid prefab IDs (StringPool / "Could not find path for prefab ID")

If your map was created with a different Rust version or a custom monument that references prefabs not in the server manifest, the map file can contain **prefab IDs** that the server doesn't recognize. When the mod is **loaded**, it filters those out at spawn time and (if `SaveMapAfterFilteringInvalidPrefabs` is true) **saves the map** so the file no longer contains them — the fix is permanent and errors won't come back even if you unload the mod. When the mod is **unloaded** (e.g. existing map detected and mod unloads), the filter doesn't run, so the game tries to spawn those prefabs and you see `StringPool.GetString - no string for ID...` and `Could not find path for prefab ID`.

- **Why they aren't removed permanently until you save:** The filter only runs when the mod is loaded; it removes entries from the in-memory list and optionally saves the map. Once the map file is saved without those entries, the next load (with or without the mod) won't have them.
- **Why they were added on creation:** They were written into the map when it was first generated (procgen or a past run) or when a custom monument was placed — e.g. a different game version, DLC content, or a custom .map that referenced prefabs by ID that this server's manifest doesn't have.
- **Identifying the prefabs:** The log will show the unknown prefab IDs (e.g. `2630900005, 924385556, ...`). The server manifest doesn't have these hashes, so we can't resolve them to paths. You can compare with a client manifest or prefab list (Rust wiki / Facepunch docs) or open your custom monument in RustEdit and remove any prefabs that might be from a different build. After removing them from your custom .map and regenerating (or after one run with the mod so it saves the cleaned map), the errors go away.

**Config:** `SaveMapAfterFilteringInvalidPrefabs` (default true) — when the mod filters invalid prefabs, it saves the map so the file is permanently cleaned.

## Outpost swap spawn coverage tracking

When troubleshooting custom `outpost.map` content that appears in the serialized world list but does not show in-game, enable debug logging and use the built-in tracking report. This report compares:

- prefab rows expected from the swapped `outpost.map`
- prefab IDs actually attempted through `World.SpawnPrefab(...)`
- IDs that were attempted but resolved to null prefab objects

### Log signatures

Look for these lines after `World.Spawn`:

- `[CustomMapGen] [TRACK] Outpost swap spawn coverage (...)`
- `[CustomMapGen] [TRACK] Missing attempted IDs: ...`
- `[CustomMapGen] [TRACK] missing id=... expectedCount=... path="..."`
- `[CustomMapGen] [TRACK] Null-prefab IDs during spawn: ...`
- `[CustomMapGen] [TRACK] null id=... attempts=... nullCount=... path="..."`

### How to interpret

- `missing id=...` means the ID was present in the swapped map rows but never attempted by world spawn for this boot path/category ordering.
- `null id=...` means spawn was attempted, but `Prefab.Load(id)` did not produce a usable prefab object at runtime (often scene/bundle/runtime constraints).
- If there are **no missing/null IDs**, then map rows are surviving the spawn pipeline and missing visuals are likely caused by non-entity scene content or systems that RustEdit-style post-processing usually restores (IO wiring, loot/resource handlers, deployable post init, etc.).

### Operational note

You do **not** need a fresh map generation for this report. A normal restart on an existing map is enough, as long as `CustomMapGen` remains loaded in diagnostic mode on existing-map boots.

## Configuration

The mod creates a configuration file at `HarmonyConfig/CustomMapGen.json` on first run. The config structure matches `standard.json` format for compatibility. Edit this file to customize map generation:

```json
{
  "Enabled": true,
  "GenerateRingRoad": "Wanted",
  "GenerateAboveGroundTrainTracks": "Wanted",
  "RemoveSmallPowerLines": true,
  "RemoveLargePowerLines": true,
  "RemoveRivers": false,
  "RemoveCarWrecks": true,
  "EnableCliffs": true,
  "LakeMinAmount": 2,
  "LakeMaxAmount": 2,
  "LakesBlocked": false,
  "LakesGenerate": "Wanted",
  "IslandsEnabled": true,
  "IslandIntensity": 7,
  "OasesMinAmount": 2,
  "OasesMaxAmount": 2,
  "OasesBlocked": false,
  "OasesGenerate": "Wanted",
  "CanyonsMinAmount": 2,
  "CanyonsMaxAmount": 2,
  "CanyonsBlocked": false,
  "CanyonsGenerate": "NotWanted",
  "TerrainConfiguration": {
    "IslandConfig": {
      "Enabled": true,
      "Intensity": 7
    },
    "MountainConfig": {
      "ReduceMountains": true
    },
    "TierConfig": {
      "Enabled": true,
      "Tier0Percentage": 0.33,
      "Tier1Percentage": 0.33,
      "Tier2Percentage": 0.34
    },
    "BiomeConfig": {
      "Enabled": true,
      "AridPercentage": 0.25,
      "TemperatePercentage": 0.25,
      "TundraPercentage": 0.25,
      "ArcticPercentage": 0.25,
      "JunglePercentage": 0.5
    },
    "FlattenShoreAndBay": true,
    "BiomeAxisAngle": "TopDesertBottomSnow",
    "LootAxisAngle": "LeftTier0RightTier2"
  }
}
```

### Configuration Options

#### Infrastructure Settings
- **GenerateRingRoad** (string): `"Wanted"`, `"NotWanted"`, or `"NoPreference"` - Control ring road generation
- **GenerateAboveGroundTrainTracks** (string): `"Wanted"`, `"NotWanted"`, or `"NoPreference"` - Control above-ground rails
- **Powerlines** (bool): `true` = allow powerlines, `false` = remove all powerlines (simple toggle; overrides RemoveSmallPowerLines/RemoveLargePowerLines when set in JSON).
- **Ziplines** (bool): `true` = allow ziplines (launch/arrival points), `false` = block zipline prefabs during map generation.
- **RemoveSmallPowerLines** / **RemoveLargePowerLines** (bool): Legacy; when **Powerlines** is not in the config, powerlines are derived from these (both true = no powerlines).
- **RemoveRivers** (bool): Remove rivers from map
- **ConnectRailsToTunnelEntrances** (bool): When `true` (default), add rail segments from the above-ground rail network to each train tunnel entrance so tracks connect visually. Set `false` for vanilla behavior (tracks may run past entrances). If your config was created before this option existed, add `"ConnectRailsToTunnelEntrances": true` to enable.
- **RemoveCarWrecks** (bool): Remove roadside car wreck monuments
- **SwapMonuments.TrySpawningOutpostInCenter** (bool): Move the outpost (and bandit camp) to map center. The game still places one outpost; the mod only redirects its position to center (no second outpost is created). Required when using custom outpost.prefab.map at center.
- **SwapMonuments.AllowBanditCamp** (bool): When **false** (default), the bandit town monument (`bandit_town.prefab`) is not spawned. Use this when the compound in the center acts as a combined outpost/bandit camp. When **true**, bandit town spawns as usual.
- **SwapMonuments.FillBanditSlotWithMonument** (bool): When **true** (default) and AllowBanditCamp is false, the bandit slot (where bandit town would have spawned) is filled with another monument (e.g. Gas Station, Supermarket) if no monument was relocated there from center. Prevents losing a monument slot.
- **SwapMonuments.UseBlockedOutpostSlotForRelocation** (bool): When **true** (default) and the procedural compound/outpost is blocked because it spawned away from center, that position is saved. It is then used to relocate (1) small monuments too close to large monuments, and (2) **large monuments that spawn at map center** (e.g. Water Treatment Plant) — so they move to the original outpost position instead of overlapping the center outpost.

#### Terrain Features
- **EnableCliffs** (bool): Enable/disable cliff rock generation
- **LakeMinAmount** / **LakeMaxAmount** (int): Range of lakes to generate (e.g., 2-2 for exactly 2 lakes)
- **LakesBlocked** (bool): Block all lakes
- **LakesGenerate** (string): `"Wanted"`, `"NotWanted"`, or `"NoPreference"`
- **IslandsEnabled** (bool): Enable/disable islands
- **IslandIntensity** (int): 0-10 scale for island generation intensity (7 = default)
- **OasesMinAmount** / **OasesMaxAmount** (int): Range of oases to generate
- **OasesBlocked** (bool): Block all oases
- **OasesGenerate** (string): `"Wanted"`, `"NotWanted"`, or `"NoPreference"`
- **CanyonsMinAmount** / **CanyonsMaxAmount** (int): Range of canyons to generate
- **CanyonsBlocked** (bool): Block all canyons
- **CanyonsGenerate** (string): `"Wanted"`, `"NotWanted"`, or `"NoPreference"`

#### Map Image Settings
- **MapImage.Enabled** (bool): When true, render and save map PNG after generation.
- **MapImage.OutputFolder** (string): Folder for map images, e.g. `"maps/images"` or `"mapimages"`. Empty = server root.
- **MapImage.MapVoterFormat** (bool): When true, save as `{size}_{seed}.png` (MapVoter); otherwise `map_{size}_{seed}.png`.
- **MapImage.IncludeMonumentNames** (bool): Draw monument labels on the map (default true).
- **MapImage.IncludeGrid** (bool): Draw grid overlay (default false).
- **MapImage.Scale** (float): Resolution multiplier (0.1–4, default 0.75 to match HCG and stay under 4096 texture limit).
- **MapImage.OceanMargin** (int): Ocean border in pixels (100–500, default 150).
- **MapImage.FontResourcesPath** (string): Font folder. Default `maps/images/resources`. Place dinprobold.otf, dinpro.otf, PermanentMarker.ttf there.

#### Terrain Configuration
- **TerrainConfiguration**: Advanced terrain settings matching `standard.json` format
  - **IslandConfig**: Island generation settings
  - **MountainConfig**: Mountain reduction settings
  - **TierConfig**: Loot tier distribution percentages
  - **BiomeConfig**: Biome distribution percentages
  - **FlattenShoreAndBay**: Flatten shore and bay areas
  - **BiomeAxisAngle**: Biome axis angle configuration
  - **LootAxisAngle**: Loot tier axis angle configuration

#### DebugLogging
- **DebugLogging** (bool): When **true** (default), extra log lines for troubleshooting: outpost/bandit AddPrefab hits and redirect, shore-flatten patch entry and cell count, compound entity spawn/skip with prefab paths and reasons (e.g. AssetScene-props). Set to **false** once issues are resolved to reduce log noise.

#### BlockedPrefabs
- **BlockedPrefabs** (list of strings): Prefab path segments to blacklist during map generation. The game uses **lowercase** paths and **underscores** (e.g. `coastal_rocks`, `rock_formation_small`). Entries are matched with `name.Contains(entry)`, so use lowercase and underscores, not display names like "Coastal Rocks" or "Rock Formation Small".

#### Legacy Support
For backward compatibility, the mod also supports these simplified properties:
- **LakeCount** (int): `-1` = default, `0` = disable, `>0` = exact count (maps to LakeMinAmount/LakeMaxAmount)
- **IslandCount** (int): `-1` = default, `0` = disable, `>0` = intensity (maps to IslandIntensity)
- **Powerlines** (bool): Preferred; `true`/`false` for powerlines. **EnablePowerlines** (bool): Legacy; maps to Powerlines.
- **EnableAboveGroundRails** (bool): Maps to GenerateAboveGroundTrainTracks

## Example Use Cases

### Standard Configuration (Matching standard.json)
```json
{
  "Enabled": true,
  "GenerateRingRoad": "Wanted",
  "GenerateAboveGroundTrainTracks": "Wanted",
  "RemoveSmallPowerLines": true,
  "RemoveLargePowerLines": true,
  "RemoveRivers": false,
  "RemoveCarWrecks": true,
  "EnableCliffs": true,
  "LakeMinAmount": 2,
  "LakeMaxAmount": 2,
  "LakesBlocked": false,
  "LakesGenerate": "Wanted",
  "IslandsEnabled": true,
  "IslandIntensity": 7,
  "OasesMinAmount": 2,
  "OasesMaxAmount": 2,
  "OasesBlocked": false,
  "OasesGenerate": "Wanted",
  "CanyonsMinAmount": 2,
  "CanyonsMaxAmount": 2,
  "CanyonsBlocked": false,
  "CanyonsGenerate": "NotWanted"
}
```

### No Cliffs, 2 Lakes, No Powerlines, Above-Ground Rails Enabled (Legacy Format)
```json
{
  "Enabled": true,
  "EnableCliffs": false,
  "LakeCount": 2,
  "IslandCount": 7,
  "EnablePowerlines": false,
  "EnableAboveGroundRails": true
}
```

## How It Works

This mod uses Harmony IL patching to intercept procedural generation components:

1. **WorldConfigPatches**: Modifies `WorldConfig` to apply powerline and rail settings
2. **PlaceCliffsPatches**: Prevents cliff generation when disabled
3. **PlaceMonumentsOffshorePatches**: Controls island count by modifying `TargetCount`
4. **LakeInfoPatches**: Prevents lakes from being registered when count limit is reached

## Notes

- Changes only apply to **new procedural maps** - existing maps are not affected
- You must generate a new map for changes to take effect
- Lake and island counts are approximate - the game's placement algorithm may place slightly fewer if valid locations cannot be found
- The mod works alongside Rust's native `WorldConfig` system
- **Outpost**: With `TrySpawningOutpostInCenter`, the outpost is **moved** to map center (the game’s single outpost placement is redirected), not created a second time.
- **Compound monument entities**: Custom entities that require `AssetScene-props` (e.g. lights, walls under `assets/content/`) are skipped during procedural generation because that bundle is not loaded then. They could be added later (e.g. after the map is generated or as a final touch) if a deferred spawn hook is added in a future update.

## Config file structure

CustomMapGen creates `HarmonyConfig/CustomMapGen.json` on first run. The file is **minimal by default** – it has core settings ( lakes, islands, cliffs, powerlines, etc.) and empty arrays for monument-related sections (LargeMonuments, SmallMonuments, Safezones, etc.). Monument placement is controlled by the game and `standard.json` when using HarmonyCustomGenerator; CustomMapGen’s config focuses on terrain, infrastructure, MapImage, and SwapMonuments. If you want a fuller config (e.g. with monument lists), you can copy from `HarmonyCustomGenerator/standard.json` or manually add sections.

**MapImage defaults:** `Enabled: false`, `IncludeGrid: false`, `IncludeMonumentNames: true`. Set `MapImage.Enabled: true` and `MapImage.OutputFolder: "maps/images"` (or `"mapimages"`) to save map images after generation.

## Troubleshooting

- **Mod not loading**: Check that `CustomMapGen.dll` is in the `HarmonyMods` directory next to the game executable (e.g. `RustDedicated_Data/Managed` or your server’s HarmonyMods folder).
- **Settings not applying**: Ensure `Enabled: true` in `HarmonyConfig/CustomMapGen.json` and restart the server.
- **Map looks unchanged (rocks, powerlines, wrecks, beaches still there)**:
  1. **Use a new procedural map.** Patches only run during **map generation**. If the server loads a **cached/saved map** (same seed + size as before), generation is skipped and the mod has no effect. Change the seed or world size, or delete the cached map file so the game generates a fresh map.
  2. **Config path:** The mod loads `HarmonyConfig/CustomMapGen.json` relative to the server (next to `RustDedicated_Data`). Ensure that file exists and has your desired options.
  3. **Rocks / clutter:** There is no “remove all rocks” option. To reduce or remove specific rocks, add path segments to **BlockedPrefabs** using **lowercase** and **underscores** (e.g. `coastal_rocks`, `rock_formation_small`, `rock_formation_medium`). To block small clutter rocks too, add `v3_rocks_small`, `v3_arid_rocks_small`, `v3_arctic_rocks_small`.
- **Lakes/Islands not matching count**: The game’s placement algorithm may not find enough valid locations; this is normal.
- **Train tracks not connecting to tunnel entrances**: Ensure **ConnectRailsToTunnelEntrances** is `true` in `CustomMapGen.json` (default). The mod adds rail segments to each tunnel entrance after the dungeon grid runs. Also ensure `GenerateAboveGroundTrainTracks` is `"Wanted"` and `RemoveUndergroundTunnels` is `false`. Generate a **new** procedural map for the fix to apply.
- **Map image: no monument labels**: Place `dinprobold.otf`, `dinpro.otf`, or `PermanentMarker.ttf` in `maps/images/resources` or `mapimages/resources` (relative to server root). Copy from HarmonyCustomGenerator's `mapimages/resources` if you use both mods. No remote download – fonts must exist locally.

## Related / Unrelated Tools

- **rustmaps-cli** ([github.com/maintc/rustmaps-cli](https://github.com/maintc/rustmaps-cli)) is an **unrelated** tool: it uses the **rustmaps.com** web service to generate map *files* (seed/size/custom config), then you download and load those maps on your server. It does **not** patch the game’s procedural generation. This Harmony mod changes how the **game itself** generates maps when you run a procedural map (new seed/size). The two can be used for different workflows (external pre-made maps vs in-game procedural), but rustmaps-cli does not help “fix” or replace this mod.

## Development

This mod follows the same structure as other Harmony mods in this workspace. See `GrimmNPC` for reference implementation patterns.
