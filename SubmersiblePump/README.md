# SubmersiblePump (Harmony)

Oxide **SubmersiblePump 1.1.0** port. Craft and place a skinned fuel generator that becomes a water pump (optional freshwater topology).

## Load order

1. **0Permissions**
2. **SubmersiblePump**

## Deploy

```powershell
.\build.ps1
```

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/SubmersiblePump.json` |
| Data | `HarmonyData/SubmersiblePump/SubmersiblePump.json` |
| Lang | `HarmonyLanguage/SubmersiblePump.json` |

## Commands

- Chat: `/pump craft` (command from config)
- Console: `givepump [steamid]` (needs `submersiblepump.give`)

Hold **Sprint** while placing to keep original water type (salt), per config.
