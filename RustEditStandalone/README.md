# RustEditStandalone

A **Harmony mod** that replicates Oxide.Ext.RustEdit functionality for **vending machines** and **IO (electrical) connections** on servers **without Oxide**.

## Purpose

If you use custom maps made with [RustEdit](https://rustedit.io) but run your server **without Oxide**, vending machines (and other custom entities) placed in the editor will spawn but stay **empty**. RustEdit relies on Oxide.Ext.RustEdit to:

- Read RustEdit data from the map
- Populate vending machines with items from vending profiles
- Restore IO (electrical) connections between switches, doors, lights, etc.
- Set up loot, resources, spawn handlers, etc.

This mod implements **vending machine population** and **IO connection restoration** using Harmony patching and the game’s built-in `World.GetMap()` API, with no Oxide dependencies.

## Requirements

- Rust dedicated server with **Harmony mod support** (e.g. Rust.Harmony)
- Custom map created and saved in **RustEdit** (with vending profiles configured)
- No Oxide/uMod required

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
  Restores wiring between IO entities (switches, door controllers, lights, timers, RF receivers/broadcasters, power counters, branches, card readers, etc.) from the RustEdit IO map layer. Connections are applied a few seconds after world spawn so all prefabs exist before wiring.

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
1. Reads the IO layer via `World.GetMap("rustedit_io")` or by scanning map layers for ProtoBuf `SerializedIOData`.
2. After prefabs have spawned, matches each serialized entity by prefab path + position and restores input/output connections and entity settings (timer length, frequency, access level, etc.).

If vending machines remain empty:

- Confirm the map was saved in RustEdit with vending profiles assigned.
- RustEdit may use obfuscated or different map keys; the mod also tries non-standard map layers.
- Check server logs for errors during world load.

If custom electrics/IO still don’t work:

- Ensure the map was saved in RustEdit with IO connections defined (wires between entities).
- The IO layer may use a different key; the mod tries `rustedit_io`, `io`, and scans other map layers for valid ProtoBuf IO data.
- Processing runs once, 5 seconds after the first prefab is tracked; if the world is still loading, increase the delay in `RustEditIOProcessor.cs` if needed.

## Compatibility

- Works with **CustomMapGen** and other Harmony mods.
- Does **not** require or conflict with Oxide.
- Safe to run on servers that may have Oxide installed; it simply runs independently.
