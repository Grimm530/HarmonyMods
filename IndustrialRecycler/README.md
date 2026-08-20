# IndustrialRecycler (Harmony)

Oxide **IndustrialRecycler 1.9.1** port as a standalone Harmony mod (no Oxide runtime).

IndustrialRecycler 1.9.1 Harmony port. Storage adapters + virtual recycler.

## Load order

1. **0Permissions** (`0Permissions.dll`)
2. **IndustrialRecycler** (`IndustrialRecycler.dll`)

VirtualItems also needs **ItemRetriever**. CustomEntities is a framework used by other mods (e.g. WaterBases) — keep this DLL separate.

## Deploy

```powershell
.\build.ps1
```

Copies **only** `IndustrialRecycler.dll` to `HarmonyMods\IndustrialRecycler.dll`.

Load: `harmony.load IndustrialRecycler` (or automatic at startup).

Shop Command product (gives the industrial recycler item, no player permission required):

```text
giveindustrialrecycler %steamid%
```

Standard variant: `givestandardrecycler %steamid%`. Chat `/giveindustrialrecycler` still requires `industrialrecycler.give`.

Hit a placed recycler (or its attached boxes/adapters) with a **hammer** to pick it back up as the skinned box item. Owner, team, and friends (per config) can pick up; monument recyclers cannot.

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/IndustrialRecycler.json` |
| Data | `HarmonyData/IndustrialRecycler/` |
| Lang | `HarmonyLanguage/IndustrialRecycler/{lang}.json` |
| Images | `HarmonyImages/IndustrialRecycler/` |

## CUI

Button commands are rewritten to `cui.endtest INDUSTRIALRECYCLER …` and routed by this mod's `cui.endtest` prefix (returns true for other markers).

## Chat

Commands go through `ConVar.Chat.say` + shared `ChatSayBridge`.
