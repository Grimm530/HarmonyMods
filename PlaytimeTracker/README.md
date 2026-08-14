# PlaytimeTracker Harmony Mod (0.2.21)

Tracks playtime / AFK / referrals. Migrates Oxide `PlaytimeTracker` and legacy `PTTracker` data.

## Load

- DLL: `HarmonyMods/PlaytimeTracker.dll`
- After `0Permissions.dll`
- AppDomain API: `PlaytimeTracker_ApiType` → `PlaytimeTrackerHarmony.PlaytimeTrackerMod`

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/PlaytimeTracker.json` |
| Data | `HarmonyData/PlaytimeTracker/user_data.json` |
| Lang | `HarmonyLanguage/PlaytimeTracker.json` |

## Migration (on first load if Harmony data is empty)

1. `HarmonyData/PlaytimeTracker/user_data.json` — use as-is
2. `oxide/data/PlaytimeTracker/user_data.json` — copy/migrate
3. `oxide/data/PTTracker/` or `HarmonyData/PTTracker/` — convert old format (`playTime`/`lastReward` → `playtime`/`lastRewardTime`)
4. Else create empty store

## Chat / console

- `/playtime`, `/playtime top`, `/playtime wipe|grant|lastseen` (admin)
- `/refer <player>`
- `ptt.restorenames`, `ptt.cleanup`

## API

`GetPlayTime`, `GetAFKTime`, `GetReferrals`, `GetLastSeen`, `GetDisplayName`, `Call`

Rewards call `Economics_ApiType.Deposit` or `ServerRewards_ApiType.AddPoints` when those mods are loaded.

## Build

```powershell
.\build.ps1
```
