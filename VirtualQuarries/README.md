# VirtualQuarries (Harmony port)

Port of Oxide `VirtualQuarries` **2.6.0** to a Harmony mod. No Oxide.Core reference; images use **FileStorage** instead of ImageLibrary.

## Paths

| | |
|---|---|
| Config | `HarmonyConfig/VirtualQuarries.json` |
| Data | `HarmonyData/VirtualQuarries/` |
| Lang | `HarmonyLanguage/VirtualQuarries.json` |
| Runtime DLL | `HarmonyMods/VirtualQuarries.dll` |
| Source | `.cursor/HarmonyMods/VirtualQuarries/` |

## Build

```powershell
cd .cursor\HarmonyMods\VirtualQuarries
.\build.ps1
```

Copies **only** `VirtualQuarries.dll` to root `HarmonyMods/`.

## Load

- Auto-loads with other Harmony mods. Requires **Permissions** (`0Permissions.dll`).
- Do **not** run the Oxide plugin at the same time — unload `oxide/plugins/VirtualQuarries.cs` (file is left in place).
- Chat commands come from config (`commandList`) plus `/vqtip`.
- CUI buttons bridge via `cui.endtest VIRTUALQUARRIES …`.

## Notes

- Economy plugins (Economics, ServerRewards, IQEconomic, BankSystem, ShoppyStock), PopUpAPI, and RedeemStorageAPI are optional Harmony/AppDomain bridges; missing refs log warnings like the Oxide plugin.
- Static quarry / excavator hooks patch `EngineSwitch`, `ExcavatorArm.RPC_SetResourceTarget`, and `ExcavatorSignalComputer.RequestSupplies`.
