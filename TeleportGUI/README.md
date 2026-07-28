# TeleportGUI (Harmony)

Exact-replica Harmony port of k1lly0u TeleportGUI **2.0.481**. No Oxide dependency. Uses `0Permissions`, `HarmonyLanguage` patterns, and `cui.endtest TELEPORTGUI` for UI actions.

## Paths

| Role | Path |
|------|------|
| Source | `.cursor/HarmonyMods/TeleportGUI/` |
| Runtime DLL | `HarmonyMods/TeleportGUI.dll` |
| Config | `HarmonyConfig/TeleportGUI.json` |
| User / warp data | `HarmonyData/TeleportGUI/userdata.json`, `warpdata.json` |

## Project Structure

| File | Responsibility |
|------|----------------|
| `TeleportGUIMod.cs` | Lifecycle, commands, TPR/TPA flow, teleport sequence, CUI carrier handler, public API |
| `TeleportGUIConfig.cs` | Full Oxide-compatible config schema (VIP maps, purchase, monuments, conditions, UI) |
| `TeleportGUIData.cs` | userdata / warpdata models + legacy `TeleportGUI_Data.json` migration |
| `TeleportGUI.UI.cs` | Main `/tp` UI — ChaosUI port matching Oxide (tabs, grid, settings) |
| `TeleportGUIUI.cs` | Legacy/manual CUI helpers (request popups); actions via `cui.endtest TELEPORTGUI ...` |
| `PermissionsBridge.cs` | Soft-bind to `0Permissions` with generation rebind |
| `Patches/Cui_Endtest_Patch.cs` | Routes `TELEPORTGUI` marker into `HandleCuiEndtest` |
| `Patches/Chat_Say_Patch.cs` | Chat command intercept |
| `Patches/BasePlayer_Die_Patch.cs` | Death location |
| `Patches/BasePlayer_OnAttacked_Patch.cs` | Cancel delayed TP on attack when configured |

## Authorization

Primary: `0Permissions` permission strings matching Oxide (`teleportgui.tp.use`, homes/warps/admin, VIP keys). Optional Harmony allowlist extras in config still load safely.

## Build & Deploy

```powershell
.\build.ps1
```

Copies `TeleportGUI.dll` to workspace `HarmonyMods/`. Load: `harmony.load TeleportGUI`.

## Remaining gaps vs Oxide 2.0.481

- Monument discovery / generated warps (`showmonumentbounds`, `showgeneratedwarps`)
- Sleeping-bag / bed home hooks
- Server save / new-save lifecycle patches
- Scrap / Economics / ServerRewards payment gating
- Full home-placement + condition checks (`MeetsConditions`, foundation/entity validation) — `UnityEngine.PhysicsModule` is now referenced so raycast work can continue
- Lang message catalog completeness may still be partial
