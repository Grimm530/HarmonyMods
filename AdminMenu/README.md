# AdminMenu Harmony Mod (2.1.13)

Oxide-free Harmony port of **AdminMenu 2.1.13** (Chaos UI). Uses Permissions for all access checks.

## Load order

1. **Permissions.dll** (preferred first)
2. **AdminMenu.dll**

```text
harmony.load Permissions
harmony.load AdminMenu
```

Startup load order is filesystem-defined. AdminMenu auto-rebinds and re-registers `adminmenu.*` when Permissions loads or is reloaded (`Permissions_Generation` + ready callback). After `harmony.reload Permissions`, you do **not** need to reload AdminMenu.

## Access (Permissions only)

- `/admin` requires `adminmenu.use` — **no** game `IsAdmin` bypass.
- On load, AdminMenu grants all `adminmenu.*` section perms to the Permissions group **`admin`**.
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

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/AdminMenu.json` |
| Data | `HarmonyData/AdminMenu/` |
| Lang | `HarmonyLanguage/AdminMenu.json` (optional overrides) |
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
