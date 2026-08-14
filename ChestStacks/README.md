# ChestStacks (Harmony)

Oxide **ChestStacks 1.4.6** port as a standalone Harmony mod. Right-click a matching chest while holding another to stack it.

## Load order

1. **0Permissions**
2. **ChestStacks**

## Deploy

```powershell
.\build.ps1
```

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/ChestStacks.json` |
| Data | `HarmonyData/ChestStacks/boxes.json` |
| Lang | `HarmonyLanguage/ChestStacks.json` |

## Permissions

| Permission | Effect |
|------------|--------|
| `cheststacks.use` | Allow stacking (also stack limits from config) |
| `cheststacks.vip` | Higher stack limits (config) |

Hold a box and press **FIRE_SECONDARY** (aim at a same-type box) to stack.

ZString/Cysharp.Text was replaced with `string.Format`.
