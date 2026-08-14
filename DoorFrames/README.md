# DoorFrames (Harmony)

Oxide **DoorFrames 2.2.0** port. Place double doors, garage doors, shop fronts, fences, and cells in floor frames.

## Load order

1. **0Permissions**
2. **DoorFrames**

## Deploy

```powershell
.\build.ps1
```

Copies **only** `DoorFrames.dll` to `HarmonyMods\DoorFrames.dll`.

## Commands

| Command | Action |
|---------|--------|
| `/df.rotate` | Help text (post-placement rotate is not supported) |

## Permissions

`doorframes.all`, `doorframes.fence`, `doorframes.wood`, `doorframes.metal`, `doorframes.armored`, `doorframes.garage`, `doorframes.shopfront`, `doorframes.bardoors`, `doorframes.prison`, `doorframes.rotate`, `doorframes.rotate.all`

Granted to Permissions group **`admin`**.
