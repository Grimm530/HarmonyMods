# Scale (Harmony)

Oxide **Scale 1.0.0** port. Admin tool to resize entities by look or by entity ID.

## Load order

1. **0Permissions**
2. **Scale**

## Deploy

```powershell
.\build.ps1
```

Copies **only** `Scale.dll` to `HarmonyMods\Scale.dll`.

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/Scale.json` |

## Commands

| Command | Action |
|---------|--------|
| `/scale` | Print look-target scale |
| `/scale 2` | Uniform scale |
| `/scale 2 1.5 0.8` | Vector scale |
| `/scale default` | Reset to 1,1,1 |
| `/scaleid <id> [size\|x y z\|default]` | Same by network ID |

## Permissions

| Permission | Effect |
|------------|--------|
| `scale.use` | Use `/scale` and `/scaleid` (admins always allowed) |

Granted to Permissions group **`admin`**.
