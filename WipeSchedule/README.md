# WipeSchedule Harmony Mod (2.0.21)

Oxide-free Harmony port of **Wipe Schedule 2.0.21** (Mevent). Exact logic replica; hosting uses HarmonyConfig / HarmonyData / 0Permissions.

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/WipeSchedule.json` |
| Data | `HarmonyData/WipeSchedule/` (`Schedule.json`, `IconSetup.json`, `EventsColors.json`, `SetupUI/`) |

## Dependencies (load order)

1. **0Permissions.dll** (optional — needed for `wipeschedule.admin` edit UI)
2. **WipeSchedule.dll**

```text
harmony.load 0Permissions
harmony.load WipeSchedule
```

ImageLibrary is not required; images load via a built-in FileStorage HTTP loader.

## Permissions

- `wipeschedule.admin` — calendar admin / edit UI

## Commands

Chat (from config `Commands`, defaults):

- `/wipe`, `/wipedata` — open wipe calendar

Console:

- `command.wipe.schedule` — CUI button handler (also via `cui.endtest WIPESCHEDULE`)
- `wipeschedule.time` — print UTC / configured timezone (server admin)

## Build

```powershell
.\.cursor\HarmonyMods\WipeSchedule\build.ps1
```

Copies only `WipeSchedule.dll` to `HarmonyMods/`.

## Notes

- CUI buttons are rewritten to `cui.endtest WIPESCHEDULE …` (same pattern as Shop/Kits).
- Optional Oxide plugins (ServerPanel, Notify, ImageLibrary) remain stubs unless a Harmony equivalent is wired later.
