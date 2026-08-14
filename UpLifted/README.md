# UpLifted (Harmony port)

Port of Oxide `UpLifted` to a Harmony mod (elevator/lift system). See `.cursor/PluginInstructionalFiles/UpLifted Plugin DocumentationV1.2.7.md`.

## Paths

| | |
|---|---|
| Config | `HarmonyConfig/UpLifted.json` |
| Data | `HarmonyData/UpLifted.json` (also `HarmonyData/UpLifted/`) |
| Lang | `HarmonyLanguage/UpLifted.json` |
| Runtime DLL | `HarmonyMods/UpLifted.dll` |
| Source | `.cursor/HarmonyMods/UpLifted/` |

## Build

```powershell
cd .cursor\HarmonyMods\UpLifted
.\build.ps1
```

Copies **only** `UpLifted.dll` to root `HarmonyMods/`.

## Load

- Requires **Permissions**. Unload `oxide/plugins/UpLifted.cs` so both copies do not run.
- Chat: `/newlift`, `/liftaid`, `/liftadmin`, `/liftowner` (config can rename create/help).
- CUI buttons bridge via `cui.endtest UPLIFTED …` (`_ul.commands`, `_ul.placement`).