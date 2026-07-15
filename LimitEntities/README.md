# LimitEntities (Harmony)

Oxide **LimitEntities 2.3.10** port as a standalone Harmony mod (no Oxide runtime).

## Load order

1. **0Permissions** (`0Permissions.dll`)
2. **LimitEntities** (`LimitEntities.dll`)

Permissions uses ready-callbacks (§10a), so LimitEntities re-registers perms if Permissions loads later or is reloaded.

## Deploy

```powershell
.\build.ps1
```

Copies **only** `LimitEntities.dll` to `HarmonyMods\LimitEntities.dll`.

Load: `harmony.load LimitEntities` (or automatic at startup).

Unload Oxide `LimitEntities` plugin first if present.

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/LimitEntities.json` (existing schema — do not invent keys) |
| Data | `HarmonyData/LimitEntities.json` (`BuildingsOwners`) |
| Lang | Embedded EN messages from the Oxide plugin |

## Permissions

- `limitentities.admin` — view other players' limits via chat command
- `limitentities.immunity` — skip all limits
- Config tiers (defaults): `limitentities.default`, `limitentities.vip`, `limitentities.elite`

## Commands

| Type | Command | Notes |
|------|---------|--------|
| Chat | `/limits`, `/limit` | From config `Commands list` |
| Console | `limitentities.list` | Admin — dumps tracked prefabs to server log |

## Features

- `Planner.DoBuild` prefix → `HandleCanBuild` (block when limit hit)
- Entity spawn/kill tracking + player/building counters
- Building owners persistence
- Entity groups + permission priority tiers
- Building merge/split owner + counter transfer
- Warn-at-% GameTips (config `Warn when more than %`)

## Skipped (config flags already false)

- ZoneManager
- ChestStacks
- PlaceryExtended
- Powered lights hooks

## Wipe

`SaveRestore.Load` with a changed wipe id clears `HarmonyData/LimitEntities.json` (`BuildingsOwners`).

## Horses

Do **not** place horses under LimitEntities — use Shop `Horse Limits` / `shop.horse` instead.
