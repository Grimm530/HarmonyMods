# RustEditStandalone

A **Harmony mod** that provides full behavioral parity with **Oxide.Ext.RustEdit** on servers **without Oxide**.

Custom maps from [RustEdit](https://www.rustedit.io) store extra layers (IO, vending, loot, NPC, APC, ocean path, vehicles). This mod reads those layers and applies the same runtime behavior as the Oxide extension.

**AutoUpdater is omitted** (it targeted Oxide’s Managed folder). Update this mod by rebuilding/redeploying `RustEditStandalone.dll`.

## Requirements

- Rust dedicated server with Harmony mod support
- Custom map saved in RustEdit
- **No Oxide/uMod required**

## Install

1. Build:
   ```powershell
   cd .cursor\HarmonyMods\RustEditStandalone
   powershell -File build.ps1
   ```
2. Deployed automatically to `<server_root>/HarmonyMods/RustEditStandalone.dll`
3. Restart the server, or `harmony.load RustEditStandalone`

## Config

`HarmonyConfig/RustEdit.json` (created on first load):

```json
{
  "Automatic Updates": { "Enabled": false },
  "Spawn Handlers": {
    "Enable loot container spawn handlers": true,
    "Enable resource spawn handlers": true,
    "Enable NPC spawn handlers": true,
    "Enable APC spawn handlers": true
  },
  "Respawn Times": {
    "Default loot containers": { "Minimum (minutes)": 30, "Maximum (minutes)": 60 },
    "Desk keycard": { "Minimum (minutes)": 15, "Maximum (minutes)": 20 },
    "Diesel Collectable": { "Minimum (minutes)": 30, "Maximum (minutes)": 45 },
    "Junk piles": { "Minimum (minutes)": 20, "Maximum (minutes)": 45 },
    "Resources": { "Minimum (minutes)": 20, "Maximum (minutes)": 45 },
    "Traps/Barricades (Respawn/Re-Arm)": { "Minimum (minutes)": 25, "Maximum (minutes)": 40 },
    "Vehicles": { "Minimum (minutes)": 45, "Maximum (minutes)": 60 }
  }
}
```

## Features

- IO connections + CardReaderMonitor / AutoTurretManager / WheelSwitch bridge; map IO protection
- Vending profile populate + restock
- Loot / Resource / JunkPile spawn handlers + respawn
- Desk keycard + excavator diesel collectable respawn
- Custom spawn points (`spawn_point.prefab`) override player respawn
- Custom ocean patrol path
- Custom APC paths + spawners
- Vehicle spawn/respawn handlers
- NPC spawners (config gated)
- Excavator arm rotation fix for rotated monuments
- Custom topology layers (`custom_topology_*`)
- NPCShopKeeper ↔ InvisibleVendingMachine linking
- Map deployable immortality (no GroundWatch / decay / stability / damage)

## Commands (admin / RCON)

| Command | Description |
|---------|-------------|
| `rustedit` | Help |
| `rustedit.apc.status` / `killall` / `respawn` | Custom APCs |
| `rustedit.io.reset` | Rewire from map IO layer |
| `rustedit.vending.restock` / `restockall` | Restock vending |
| `rustedit.resource.respawnall` / `info` | Resources |
| `rustedit.loot.respawnall` / `info` | Loot |
| `rustedit.junkpile.respawnall` / `info` | Junk piles |
| `rustedit.desk.populate` | Force desk keycards |
| `rustedit.spawns.show [time]` | Draw spawn points |
| `rustedit.ocean.show [time]` | Draw ocean path |
| `rustedit.checkupdate` / `downloadupdate` | Stub (unsupported) |

Chat aliases like `/rustedit.spawns.show` work through the same console command names when used as chat commands with admin.

## API

`AppDomain.CurrentDomain.GetData("RustEdit_ApiType")` → `typeof(RustEditApi)`

```csharp
RustEditApi.GetAllMapEntities(ref list);
RustEditApi.GetMapEntitiesOfType<T>(ref list);
RustEditApi.GetActiveNPCs(ref list);
RustEditApi.GetActiveAPCs(ref list);
RustEditApi.GetSpawnpoints(ref list);
RustEditApi.GetTopologyMapNames();
RustEditApi.TryGetTopologyMap(name, out map);

// Events
RustEditApi.NPCSpawned;
RustEditApi.APCSpawned;
RustEditApi.MapDataProcessed;
```

## Notes

- Assembly name is **`RustEditStandalone`** → DLL `RustEditStandalone.dll`
- Map keys are resolved via XOR(prefabCount) + optional AES + layer scan
- IO protobuf manual deserializer preserved from the prior standalone port
