# AutoCodeLock Harmony Mod (3.0.12)

Oxide-free Harmony port of **AutoCodeLock 3.0.12** (Chaos UI). Uses **0Permissions** for access checks. Chaos UI framework is vendored (same approach as AdminMenu).

## Load order

1. **0Permissions.dll** (preferred first)
2. **AutoCodeLock.dll**

```text
harmony.load 0Permissions
harmony.load AutoCodeLock
```

After `harmony.reload 0Permissions`, AutoCodeLock auto-rebinds and re-registers `autocodelock.*` (generation + ready callback). You do **not** need to reload AutoCodeLock.

## Permissions

| Permission | Purpose |
|------------|---------|
| `autocodelock.deploydoor` | Auto-deploy codelock on doors |
| `autocodelock.deploybox` | Auto-deploy on boxes |
| `autocodelock.deploylocker` | Auto-deploy on lockers |
| `autocodelock.deploycup` | Auto-deploy on cupboards |
| `autocodelock.autolock` | Auto-set pin / guest code |
| `autocodelock.nolockneed` | Spawn locks without consuming inventory item |
| `autocodelock.doorcloser` | Door closer deploy / `/closer` |

Example:

```text
perm grant user <steamid> autocodelock.deploydoor
perm grant user <steamid> autocodelock.autolock
perm grant group default autocodelock.doorcloser
```

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/AutoCodeLock.json` |
| Data | `HarmonyData/AutoCodeLock/user_data.json` |
| Lang | `HarmonyLanguage/AutoCodeLock.json` (file wins over embedded defaults) |

## Commands

| Chat | Default | Purpose |
|------|---------|---------|
| Codelock menu | `/codelock` | Open settings UI |
| Skin | `/codelock.skin` | Preferred lock skin (needs PlayerDLCAPI + `nolockneed`) |
| Closer | `/closer` | Toggle door closer on looked-at door |

Command names are configurable in config.

## CUI

Button commands are rewritten in `ChaosUI.Show` to `cui.endtest AUTOCODELOCK autocodelock.callback …`, then routed to `CommandCallbackHandler.HandleCallback` (same pattern as AdminMenu).

## Soft dependencies

- **NoEscape** (optional): raid/combat block checks when those config options are enabled. Looks for AppDomain `NoEscape_ApiType` or known Harmony types.
- **PlayerDLCAPI** (optional): codelock skin ownership. Looks for AppDomain `PlayerDlcApi_ApiType`.
- **CanAutoLock** hook: other mods can register `AppDomain.CurrentDomain.SetData("AutoCodeLock_Hook_CanAutoLock", (Func<object[], object>)handler)`.

## Build

```powershell
.\.cursor\HarmonyMods\AutoCodeLock\build.ps1
```

Copies `AutoCodeLock.dll` to `HarmonyMods/`.

## Port notes

- Source: `.cursor/Oxide.Plugins.Cant-Use/AutoCodeLock.cs`
- ProtoBuf storage dropped — JSON only under `HarmonyData`
- Fixed Oxide bugs while porting: skin command now sets the selected skin; guest-code apply updates guest codes (not pin)
- Team/clan lock **access** is owned by DynamicCupShare. After auto-PIN or guest-code apply this mod calls `DynamicCupShareMod.NotifyCodeLockChanged` and does not wipe existing guest users.
