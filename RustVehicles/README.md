# RustVehicles (Harmony port of Oxide RustVehicles 2.0.5)

Harmony-first, Oxide-free port of the Oxide plugin **RustVehicles** (Arainrr / Grimm530).
Players buy vehicle licenses, then spawn / recall / kill / pickup licensed vehicles.

## Load order

```
0Permissions  ->  (optional) Economics  ->  RustVehicles
```

- `0Permissions.dll` provides the permission backend. RustVehicles links lazily and re-registers when 0Permissions loads/reloads.
- `Economics` (and optionally RustRewards via ServerRewards bridge) are optional currency backends.

## Paths

- Config: `HarmonyConfig/RustVehicles.json` (do not overwrite existing server config)
- Data:   `HarmonyData/RustVehicles/RustVehicles.json` (do not overwrite existing data)

## Commands

Chat (defaults; many aliases come from config vehicle `Commands`):

- `/license` (help) — `CmdLicenseHelp`
- `/buy` — buy a vehicle license
- `/spawn` — spawn a licensed vehicle
- `/recall` — recall / store a spawned vehicle
- `/kill` — kill a spawned vehicle
- `/pickup` — pickup support
- `/vldiscover` — discover custom vehicles

Console:

- `vl.remove`, `vl.dumpcommands`, `vl.cleardata`, `vl.reloadcustom`
- `vl.buy`, `vl.spawn`, `vl.recall`, `vl.kill`
- `vl_wipe` — manual wipe

## Load

```
harmony.load RustVehicles
```

## Build

```powershell
.\convert-from-oxide.ps1   # regenerate RustVehicles.cs from Oxide source
.\build.ps1
```

Builds Release and copies only `RustVehicles.dll` to `<server root>\HarmonyMods\RustVehicles.dll`.
