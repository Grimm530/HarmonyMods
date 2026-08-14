# InventoryViewer (Harmony)

Oxide **Inventory Viewer 4.1.3** port. Admins with permission can inspect another player's inventory via a hidden corpse loot panel.

## Load order

1. **0Permissions**
2. **InventoryViewer**

## Deploy

```powershell
.\build.ps1
```

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/InventoryViewer.json` |
| Lang | `HarmonyLanguage/InventoryViewer.json` |

## Permissions

| Permission | Effect |
|------------|--------|
| `inventoryviewer.allowed` | Use `/viewinv` |
| `inventoryviewer.unlock` | Move items while inspecting |

## Commands

`/viewinv`, `/viewinventory`, `/inspect` — look at a player, or pass a name/SteamID.

Public API: `InventoryViewerMod.Instance.ViewInventory(viewer, target)`.
