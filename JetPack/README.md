# JetPack (Harmony)

Oxide **JetPack 1.3.7** port as a standalone Harmony mod (no Oxide runtime).

JetPack 1.3.7 Harmony port. Wearable jetpack.

## Load order

1. **0Permissions** (`0Permissions.dll`)
2. **JetPack** (`JetPack.dll`)

VirtualItems also needs **ItemRetriever**. CustomEntities is a framework used by other mods (e.g. WaterBases) — keep this DLL separate.

## Deploy

```powershell
.\build.ps1
```

Copies **only** `JetPack.dll` to `HarmonyMods\JetPack.dll`.

Load: `harmony.load JetPack` (or automatic at startup).

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/JetPack.json` |
| Data | `HarmonyData/JetPack/` |
| Lang | `HarmonyLanguage/JetPack/{lang}.json` |
| Images | `HarmonyImages/JetPack/` |

## Flight

The flying body is the large backpack (`item_drop_backpack`) instead of a loot bag, synced like a vehicle every physics tick. Rocket-launcher pipes stay parented to the seat. Exhaust uses the original helicopter-rocket visual, but as inert parented decor that is shown/hidden instead of spawned/killed.

## CUI

Button commands are rewritten to `cui.endtest JETPACK …` and routed by this mod's `cui.endtest` prefix (returns true for other markers).

## Chat

Commands go through `ConVar.Chat.say` + shared `ChatSayBridge`.
