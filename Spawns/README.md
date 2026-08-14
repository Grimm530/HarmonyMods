# Spawns Harmony Mod (2.0.36)

Spawn-point database used by ZoneManager and other mods.

## Load

- DLL: `HarmonyMods/Spawns.dll`
- AppDomain API: `Spawns_ApiType` → `SpawnsHarmony.SpawnsMod`

## Paths

| Kind | Path |
|------|------|
| Data | `HarmonyData/Spawns/` (migrated from `oxide/data/SpawnsDatabase/` if dest was missing) |
| Lang | `HarmonyLanguage/Spawns.json` |

## Chat (auth level ≥ 1)

`/spawns new|open|add|remove|save|close|show`

## API (Oxide Call parity)

Static methods on `SpawnsMod`:

- `GetSpawns(string filename)` → `List<Vector3>` or error string
- `GetSpawnsCount(string filename)` → `int` or error string
- `GetRandomSpawn(string filename)` → `Vector3` or error string
- `GetRandomSpawnRange(string filename, int min, int max)`
- `GetSpawn(string filename, int number)`
- `GetSpawnfileNames()` → `string[]`
- `Call(string method, params object[] args)`

ZoneManager later: `AppDomain.CurrentDomain.GetData("Spawns_ApiType")` then invoke the matching method.

## Build

```powershell
.\build.ps1
```
