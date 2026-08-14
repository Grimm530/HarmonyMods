# BradleyDrops (Harmony port)

Port of Oxide `BradleyDrops` to a Harmony mod. Config copied from `oxide/config/BradleyDrops.json` (not BradleyDropsSVR1.json). Difficulty notes: `.cursor/PluginInstructionalFiles/BradleyDrops_Difficulty_Summary.md`.

## Paths

| | |
|---|---|
| Config | `HarmonyConfig/BradleyDrops.json` |
| Lang | `HarmonyLanguage/BradleyDrops.json` |
| Runtime DLL | `HarmonyMods/BradleyDrops.dll` |
| Source | `.cursor/HarmonyMods/BradleyDrops/` |

## Build

```powershell
cd .cursor\HarmonyMods\BradleyDrops
.\build.ps1
```

Copies **only** `BradleyDrops.dll` to root `HarmonyMods/`.

## Load

- Requires **Permissions**. Unload `oxide/plugins/BradleyDrops.cs` so both copies do not run.
- Chat/console: report, buy, despawn, `bdgive`, `bdclearcd` (from config + hardcoded).
- CUI (if any) bridges via `cui.endtest BRADLEYDROPS …`.
- Internal Harmony patches (Bradley aiming/weapons, CH47 altitude, hackable crate decay) still apply from `OnServerInitialized`.