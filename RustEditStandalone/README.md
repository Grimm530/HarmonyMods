# RustEditStandalone

A **Harmony mod** that replicates Oxide.Ext.RustEdit functionality for **vending machines** and **IO (electrical) connections** on servers **without legacy plugin host**.

## Purpose

If you use custom maps made with [RustEdit](https://rustedit.io) but run your server **without legacy plugin host**, vending machines (and other custom entities) placed in the editor will spawn but stay **empty**. RustEdit relies on Oxide.Ext.RustEdit to:

- Read RustEdit data from the map
- Populate vending machines with items from vending profiles
- Restore IO (electrical) connections between switches, doors, lights, etc.
- Set up loot, resources, spawn handlers, etc.

This mod implements **vending machine population** and **IO connection restoration** using Harmony patching and the game’s built-in `World.GetMap()` API, with Harmony-only dependencies.

## Requirements

- Rust dedicated server with **Harmony mod support** (e.g. Rust.Harmony)
- Custom map created and saved in **RustEdit** (with vending profiles configured)
- Harmony-only/uMod required

## Installation

1. **Build the mod**
   ```bash
   cd .cursor/HarmonyMods/RustEditStandalone
   dotnet build
   ```

2. **Copy the DLL** into your server’s Harmony mods folder:
   ```
   RustEditStandalone.dll → <server_root>/HarmonyMods/
   ```
   (Same folder where CustomMapGen and other Harmony mods go.)

3. **Restart the server** (or reload: `harmony.unload RustEditStandalone` then `harmony.load RustEditStandalone`).

## Supported Features

- **Vending machines**  
  Populates NPC vending machines using the vending profiles defined in RustEdit when the map was saved.

- **IO (electrical) connections**  
  Restores wiring between IO entities (switches, door controllers, lights, timers, RF receivers/broadcasters, power counters, branches, card readers, etc.) from RustEdit IO data and from a `customgenerator` extras layer when the map has one. Connections are applied after world spawn so prefabs exist before wiring. The mod stays loaded; it does not unload after the wires are applied.

## Not Implemented (vs full Oxide.Ext.RustEdit)

- Custom loot containers + respawn
- Resource respawn handlers
- Junk pile respawn
- NPC spawners
- Vehicle spawn handlers
- Ocean patrol paths
- Custom APC paths
- Desk keycard spawners
- Damage/decay overrides

These would require additional Harmony patches and logic, but the same pattern could be extended.

## Data Format

RustEdit stores data in the map file as custom map layers. This mod:

**Vending**
1. Reads data via `World.GetMap("rustedit_vending")` or scans all map layers.
2. Deserializes XML into vending profiles.
3. Matches each vending machine to its profile by prefab filename.
4. Populates machines with up to 7 random items from the profile.

**IO (electrical)**
1. Reads RustEdit IO from `io`, `rustedit_io`, and other custom map layers. Both XML `SerializedIOData` (what RustEdit saves) and protobuf IO data are accepted. The layer with the most valid entities is used.
2. Also reads the `customgenerator` JSON layer. Its `IO` list (prefab, `float[3]` position, inputs/outputs, and settings) is world-space wiring and is applied together with any RustEdit IO layer. A `customgenerator` entity within 1 meter of the same prefab replaces the RustEdit record so the live-server positions win.
3. After prefabs have spawned, each entity is matched by prefab and position. Candidates are indexed by prefab, and only the closest entity within 1 meter is used. Output and input slots are bounds-checked. A wire that is already on the right slot is left alone. The log names the prefab, slot, and why a connection was skipped.
4. Settings copied from the saved entity: card reader access level, door manipulator effect, timer length, RF frequency, unlimited ammo, peacekeeper, turret weapon, branch amount, counter target, RC identifier, counter passthrough, elevator floors, and phone name.

If vending machines remain empty:

- Confirm the map was saved in RustEdit with vending profiles assigned.
- RustEdit may use obfuscated or different map keys; the mod also tries non-standard map layers.
- Check server logs for errors during world load.

If custom electrics/IO still don’t work:

- RustEdit maps need an IO layer (`io`, `rustedit_io`, or another XML/protobuf layer). Maps that only carry monument wiring in the `customgenerator` layer are wired from that JSON list.
- Matching requires the prefab within 1 meter of the saved position. The server log lists the first missing entities and the first failed slots.
- Wiring runs about 2 seconds after the loading screen reports done, with a 90 second fallback after the first prefab if IO data was found at startup. `rustedit.io.reset` runs it again from the map.

## Compatibility

- Works with **CustomMapGen** and other Harmony mods.
- Does **not** require or conflict with Oxide.
- Safe to run on servers that may have Oxide installed; it simply runs independently.
