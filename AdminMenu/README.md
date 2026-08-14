# AdminMenu Harmony Mod (2.1.13)

Oxide-free Harmony port of **AdminMenu 2.1.13** (Chaos UI). Uses Permissions for all access checks.

## Load order

1. **0Permissions.dll** (preferred first)
2. **AdminMenu.dll**

```text
harmony.load 0Permissions
harmony.load AdminMenu
```

Startup load order is filesystem-defined. AdminMenu auto-rebinds and re-registers `adminmenu.*` when Permissions loads or is reloaded (`Permissions_Generation` + ready callback). After `harmony.reload 0Permissions`, you do **not** need to reload AdminMenu.

## Access (Permissions only)

- `/admin` requires `adminmenu.use` — **no** game `IsAdmin` bypass.
- On load, AdminMenu grants all built-in `adminmenu.*` section perms **and** any non-empty `RequiredPermission` values from `HarmonyConfig/AdminMenu.json` (Player Info buttons like Backpacks / InventoryViewer / Freeze) to the Permissions group **`admin`**.
- Put staff in that group (they then see Commands, Permissions, Groups, Convars, Plugins, Give, Player Info, etc.):

```text
perm usergroup add <steamid> admin
```

Or grant individually:

```text
perm grant user <steamid> adminmenu.use
perm grant group admin adminmenu.permissions
...
```

Player Info custom command perms used by the default config (also auto-granted to `admin`):

| Feature | Permission |
|---------|------------|
| View Backpack | `backpacks.admin` |
| View Inventory | `inventoryviewer.allowed` |
| Freeze / Unfreeze | `freeze.use` |
## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/AdminMenu.json` |
| Data | `HarmonyData/AdminMenu/` |
| Lang | `HarmonyLanguage/AdminMenu.json` (file wins over embedded defaults) |
| Images | `HarmonyImages/AdminMenu/` (`magnifyingglass.png`, `adminmenulogo.png`) |

## Command

- `/admin` — open Admin Menu

## CUI

Button commands are rewritten in `ChaosUI.Show` to `cui.endtest ADMINMENU adminmenu.callback …`, then routed to `CommandCallbackHandler.HandleCallback`.

## Build

```powershell
.\.cursor\HarmonyMods\AdminMenu\build.ps1
```

Copies `AdminMenu.dll` to `HarmonyMods/`.
