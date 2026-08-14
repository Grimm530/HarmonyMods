# UberTool (Harmony)

Oxide **UberTool 1.4.50** port as a standalone Harmony mod (no Oxide runtime).

UberTool 1.4.50 Harmony port. Admin build/remove/hammer tool. Overlay scale is built-in (not the Scale plugin).

## Load order

1. **0Permissions** (`0Permissions.dll`)
2. **UberTool** (`UberTool.dll`)

VirtualItems also needs **ItemRetriever**. CustomEntities is a framework used by other mods (e.g. WaterBases) — keep this DLL separate.

## Deploy

```powershell
.\build.ps1
```

Copies **only** `UberTool.dll` to `HarmonyMods\UberTool.dll`.

Load: `harmony.load UberTool` (or automatic at startup).

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/UberTool.json` |
| Data | `HarmonyData/UberTool/` |
| Lang | `HarmonyLanguage/UberTool/{lang}.json` |
| Images | `HarmonyImages/UberTool/` |

## CUI

Button commands are rewritten to `cui.endtest UBERTOOL …` and routed by this mod's `cui.endtest` prefix (returns true for other markers).

## Chat

Commands go through `ConVar.Chat.say` + shared `ChatSayBridge`.
