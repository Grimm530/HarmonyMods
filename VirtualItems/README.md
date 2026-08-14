# VirtualItems (Harmony)

Oxide **VirtualItems 0.5.1** port as a standalone Harmony mod (no Oxide runtime).

VirtualItems 0.5.1 Harmony port. Free craft/build ingredients via ItemRetriever.

## Load order

1. **0Permissions** (`0Permissions.dll`)
2. **VirtualItems** (`VirtualItems.dll`)

VirtualItems also needs **ItemRetriever**. CustomEntities is a framework used by other mods (e.g. WaterBases) — keep this DLL separate.

## Deploy

```powershell
.\build.ps1
```

Copies **only** `VirtualItems.dll` to `HarmonyMods\VirtualItems.dll`.

Load: `harmony.load VirtualItems` (or automatic at startup).

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/VirtualItems.json` |
| Data | `HarmonyData/VirtualItems/` |
| Lang | `HarmonyLanguage/VirtualItems/{lang}.json` |
| Images | `HarmonyImages/VirtualItems/` |

## CUI

Button commands are rewritten to `cui.endtest VIRTUALITEMS …` and routed by this mod's `cui.endtest` prefix (returns true for other markers).

## Chat

Commands go through `ConVar.Chat.say` + shared `ChatSayBridge`.
