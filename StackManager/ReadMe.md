# StackManager Harmony Mod (StacksExtended 2.0.24 UI)

Oxide-free Harmony port of **StacksExtended 2.0.24** Chaos UI. Uses the vendored Chaos UIFramework (same pattern as AdminMenu) — no `Oxide.Ext.Chaos` extension.

## Load order

```text
harmony.load 0Permissions
harmony.load StackManager
```

## Access

- Chat: `/stacks`
- Permission: `stackmanager.admin` (granted to Permissions group `admin` on load)

```text
perm usergroup add <steamid> admin
```

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/StackManager.json` |
| Data | `HarmonyData/StackManager/` (`stack_limits.json`, `player_overrides.json`, `storage_limits.json`, `vip_limits.json`) |
| Lang | `HarmonyLanguage/StackManager.json` |
| Images | `HarmonyImages/StackManager/` (optional `magnifyingglass.png`) |

## CUI

Button commands rewrite to `cui.endtest STACKMANAGER stackmanager.callback …`, then route to `CommandCallbackHandler`.

## Build

```powershell
.\.cursor\HarmonyMods\StackManager\build.ps1
```

Copies `StackManager.dll` to `HarmonyMods/`.
