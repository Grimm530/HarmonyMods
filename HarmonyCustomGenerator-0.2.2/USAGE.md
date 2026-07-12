# Custom Rust Map Generator Usage Guide

## Table of Contents
1. [Installation](#installation)
2. [Map Generation](#map-generation)
3. [Monument Swapping](#monument-swapping)

## Installation

1. Make sure your server have Harmony 2.3 installed (default installed)
2. Copy the generator dll file to the `HarmonyMods/CustomGenerator.dll`
3. Configure settings in `HarmonyConfig/CustomGeneratorCFG.json`

Logs will be available in `HarmonyConfig/logs`  
Generated map images in `mapimages/`


## Map Generation

1. Run the server with the installed generator at least once
2. Configure desired parameters in the configuration file `HarmonyConfig/CustomGeneratorCFG.json`
3. Run the server again
4. The generated map will be saved in the `maps/` folder or default folder with the chosen name


## Monument Swapping

Monument Swapping allows you to replace vanilla monuments with custom ones while maintaining the original map layout and connections. This feature enables:

- Direct replacement of vanilla monuments with custom versions
- Preservation of original monument positions and road/rail connections
- Multiple monument replacements in a single generation
- Automatic generation of two map versions (with and without custom monuments)


### Custom Monument Preparation
1. Enable "Swap Monuments" => "Enabled": true
2. Place your custom monument prefabs in the `maps/prefabs` folder
3. Prefab requirements:
   - File format: `.map` (see examples in the `CustomPrefabs` folder, thanks to FlySelf)
   - Name format: `monument_original_path.prefab` (example: `fishing_village_c.prefab`)
   - Monument size must match the original
   - Proper terrain alignment in the prefab

**Outpost (center safe zone):** In procedural maps the center safe zone is the *compound* prefab, not outpost. To replace it with a custom Outpost, name your file `outpost.prefab.map` or `outpost.map` and place it in `maps/prefabs`. The swap will replace the compound with your custom monument.

### No outpost/compound on the map at all

If there is no center safe zone (Outpost) or any main monuments on the map, check **Monuments → Main Monuments → TargetCount**. If it is **0**, the game places **zero** monuments from the main monuments folder (which includes the compound/outpost in `monument/medium`). Set **TargetCount** to at least **1** (e.g. 8) so main monuments (including the center compound) are placed. Then use **Force Outpost to map center** in Main Generator to put the compound at map center, and optionally use monument swap to replace it with a custom `outpost.prefab.map`.

### Swap ran but monument is missing in-game

If the log shows `[SWAP MN] outpost.prefab: replaced with N prefab(s)` but the monument does not appear when you load the map, the **map file does contain the replacement prefabs**; the game is not spawning them when it loads. This usually happens when:

- **Prefab IDs in the custom .map do not match the server’s game build.** The game looks up each prefab by ID (StringPool). If the custom .map was made with a different Rust version, or exported from RustEdit/another tool with different ID mapping, the server will skip those prefabs when loading the map.
- **Fix:** Use a custom monument .map that was saved/exported from the **same Rust (server) build** so prefab IDs match. Try the **template** from `CustomPrefabs` (e.g. `rustmaps_outpost_template.map` copied to `maps/prefabs/outpost.prefab.map`) to confirm the pipeline works, then replace with your own .map built the same way.

> Note: Make sure your custom monuments and whole map are properly tested before using them on server!

For support, join our [discord server](https://discord.gg/xUdpkm8RUS).