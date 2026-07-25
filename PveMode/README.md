# 0PveMode (Harmony port of PveMode)

Harmony port of Oxide **PveMode 1.2.9**. Named **`0PveMode`** so HarmonyLoader / filesystem order places it before **ArmoredTrain** / **Convoy** (same idea as **0Permissions**).

Config/data/lang and AppDomain keys stay **`PveMode*`** for compatibility; only the mod/DLL name is `0PveMode`.

## Build

```powershell
./build.ps1
```

Deploys `HarmonyMods/0PveMode.dll` and removes any legacy `PveMode.dll`.

## Load

```
harmony.load 0PveMode
```

Typical order: `0Permissions` → `TruePVE` → `0PveMode` → content mods (`Convoy`, `ArmoredTrain`, …).

Consumers resolve the API **lazily** and **rebind** via `PveMode_Generation` + `PveMode_ReadyCallbacks` if they loaded first — you do not need to reload Convoy after loading 0PveMode.

## Paths

| What | Path |
|------|------|
| Config | `HarmonyConfig/PveMode.json` |
| Cooldown data | `HarmonyData/PveMode/players.json` |
| Language | `HarmonyLanguage/PveMode.json` |

## AppDomain API

| Key | Meaning |
|-----|---------|
| `PveMode_ApiType` | `PveModeHarmony.PveModeApi` |
| `PveMode_Generation` | int, bumps each load/reload |
| `PveMode_ReadyCallbacks` | `List<Action>` — consumers re-register |
| `PveMode_CanEntityTakeDamage` | TruePVE bridge |
| `PveMode_CanEntityBeTargeted` | TruePVE bridge |
| `PveMode_OwnerCallbacks` | `List<Action<string,string,BasePlayer>>` set/clear owner |

`plugins.Exists("PveMode")` / `plugins.Exists("0PveMode")` both resolve (TruePVE OxideCompat alias).

## Console / chat

- `ClearTimePveMode <steamid> [event]`
- `ClearOwnerPveMode <steamid> [event]`
- `/EventsTime`
