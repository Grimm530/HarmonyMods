# GrimmRewards (RustRewards Harmony Mod 3.2.5)

Oxide-free port of RustRewards, branded as **GrimmRewards** in chat/UI/Discord. Rewards players for kills, harvest, pickup, open, activity, and welcome using Scrap, Economics, or ServerRewards.

## Paths

| What | Location |
|------|----------|
| DLL | `HarmonyMods/RustRewards.dll` |
| Config | `HarmonyConfig/RustRewards.json` |
| Data | `HarmonyData/RustRewards/RustRewards.json` |
| Logs | `HarmonyData/RustRewards/logs/` |
| UI images | under `HarmonyData/` (e.g. `RustRewards/pinned.png`) |

## Load order

1. **Permissions**
2. **Economics** (if using Economics currency)
3. **RustRewards**

Optional bridges (when present): RaidableBases, Clans, Friends, GUIAnnouncements, NoEscape, ServerRewards, ZoneManager, PlaytimeTracker.

## Commands

**Chat**

- `/rr` — player prefs / admin UI
- Main alias from config (`Settings.UI.MainCommandAlias`, default in this server: `GrimmRewards`)
- Also: `rustrewards`

**Console (admin / server)**

- `rustrewards.wipesummary`
- `rustrewards.setwipebaseline`
- `rustrewards.sendreport`
- UI: `CloseRR`, `RRUI`, `rrv`, `RRChange*`, `RRZone`, …

## Build

```powershell
.\build.ps1
```

Copies `RustRewards.dll` to the server `HarmonyMods/` folder.

## Source

Converted from `Oxide.Plugins.Cant-Use/RustRewards.cs` via `convert-from-oxide.ps1`.
