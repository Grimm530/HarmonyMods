# RocketGuidanceSystem (Harmony)

Oxide **RocketGuidanceSystem 1.0.704** port as a standalone Harmony mod (no Oxide runtime).

Rocket Guidance System 1.0.704 Harmony port. Homing rocket lock-on.

## Load order

1. **0Permissions** (`0Permissions.dll`)
2. **RocketGuidanceSystem** (`RocketGuidanceSystem.dll`)

VirtualItems also needs **ItemRetriever**. CustomEntities is a framework used by other mods (e.g. WaterBases) — keep this DLL separate.

## Deploy

```powershell
.\build.ps1
```

Copies **only** `RocketGuidanceSystem.dll` to `HarmonyMods\RocketGuidanceSystem.dll`.

Load: `harmony.load RocketGuidanceSystem` (or automatic at startup).

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/RocketGuidanceSystem.json` |
| Data | `HarmonyData/RocketGuidanceSystem/` |
| Lang | `HarmonyLanguage/RocketGuidanceSystem/{lang}.json` |
| Images | `HarmonyImages/RocketGuidanceSystem/` |

## CUI

Button commands are rewritten to `cui.endtest RGS …` and routed by this mod's `cui.endtest` prefix (returns true for other markers).

## Chat

Commands go through `ConVar.Chat.say` + shared `ChatSayBridge`.
