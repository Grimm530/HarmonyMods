# KillFeed (Harmony)

Oxide **KillFeed 2.1.2** port as a standalone Harmony mod (no Oxide runtime).

KillFeed 2.1.2 Harmony port. Death feed CUI + admin editor.

## Load order

1. **0Permissions** (`0Permissions.dll`)
2. **KillFeed** (`KillFeed.dll`)

VirtualItems also needs **ItemRetriever**. CustomEntities is a framework used by other mods (e.g. WaterBases) — keep this DLL separate.

## Deploy

```powershell
.\build.ps1
```

Copies **only** `KillFeed.dll` to `HarmonyMods\KillFeed.dll`.

Load: `harmony.load KillFeed` (or automatic at startup).

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/KillFeed.json` |
| Data | `HarmonyData/KillFeed/` |
| Lang | `HarmonyLanguage/KillFeed/{lang}.json` |
| Images | `HarmonyImages/KillFeed/` |

## CUI

Button commands are rewritten to `cui.endtest KILLFEED …` and routed by this mod's `cui.endtest` prefix (returns true for other markers).

## Chat

Commands go through `ConVar.Chat.say` + shared `ChatSayBridge`.
