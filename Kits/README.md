# Kits (Harmony Mod)

**No Oxide dependency.** Harmony port of Oxide **Kits 2.3.8** (Mevent) — same kit UI/logic, adapted only for Harmony hosting.

## Mod identity

| Field | Value |
|-------|--------|
| **Name** | Kits |
| **Type** | Harmony mod (`IHarmonyModHooks`) |
| **Oxide** | None — for Oxide-free servers only |
| **Source** | `.cursor/Oxide.Plugins.Cant-Use/Kits.cs` (v2.3.8) |
| **Config** | `HarmonyConfig/Kits.json` (migrates from `oxide/config/Kits.json` if present) |
| **Data** | `HarmonyData/Kits/` (`Kits.json`, `DisabledAutoKits.json`, `Players/{userid}.json`, `logs/`) |

## Project structure

| File | Content |
|------|--------|
| `Kits.cs` | Full 2.3.8 plugin logic (ported from Oxide; near-verbatim) |
| `KitsCompat.cs` | Oxide shims: `IPlayer`, config/data, timers, lang, permissions, webrequest |
| `RustCui.cs` | CUI helpers (`Oxide.Game.Rust.Cui`) without Oxide pooling |
| `KitsHarmonyMod.cs` | Harmony entry, AppDomain API, command registration |
| `Patches/Chat_Say_Patch.cs` | Prefix on `ConVar.Chat.say` for `/kit`, `/kits`, `/editkit` |
| `Patches/Cui_Endtest_Patch.cs` | Routes `cui.endtest KITS …` CUI clicks to `UI_Kits` |
| `Patches/PlayerLifecycle_Patches.cs` | Respawn / disconnect / death / wipe hooks |
| `convert-from-oxide.ps1` | Regenerates `Kits.cs` from Oxide source |
| `Kits.csproj` | Game refs + Krafs.Publicizer |
| `build.ps1` | Build and copy DLL to `HarmonyMods/` |

## What changed vs Oxide (only Harmony necessities)

| Oxide | Harmony |
|-------|---------|
| `oxide/config/Kits.json` | `HarmonyConfig/Kits.json` |
| `oxide/data/Kits/` | `HarmonyData/Kits/` |
| `RustPlugin` + attributes | `KitsPluginBase` + manual ConsoleSystem / chat patch |
| `Interface.CallHook` / PluginReferences | No-op / always null (use Offline Image Mode) |
| ImageLibrary / ServerPanel / Notify / NoEscape | Unloaded stubs — offline images work; Notify falls back to chat |
| Permissions | Admins always pass; grant others via `HarmonyPermissionHelper` |

**Unchanged:** kit data model, UI layouts, give/redeem/cooldown/limit logic, auto-kits, Discord logging, CopyPaste building paste params.

## Chat / console commands

| Command | Description |
|---------|-------------|
| **/kit**, **/kits** | Open kits UI / redeem by name (from config `Commands`) |
| **/editkit** | Admin kit editor |
| `UI_Kits` | UI callbacks (CUI buttons use `cui.endtest KITS …` → this command) |
| `kits.reset`, `kits.give`, `kits.givekit`, `kits.template`, `kits.convert` | Admin/server console (prefer space form below) |
| `kits template …` | Space-form F1/console (replicated). Example: `kits template fullscreen rust categories` |

## API for other mods

AppDomain key: **`Kits_ApiType`** → `KitsHarmony.KitsHarmonyMod`

| Method | Description |
|--------|-------------|
| `GiveKit(BasePlayer, string)` | Give kit (returns object like Oxide) |
| `GiveKit(BasePlayer, string, bool)` | Give kit with UI flag |

## Build and deploy

```powershell
.\.cursor\HarmonyMods\Kits\build.ps1
```

DLL → **`HarmonyMods/Kits.dll`**. Load: `harmony.load Kits`.

Refresh from Oxide source:

```powershell
.\.cursor\HarmonyMods\Kits\convert-from-oxide.ps1
.\.cursor\HarmonyMods\Kits\build.ps1
```

## Path mapping (Oxide → Harmony)

| Oxide | Harmony |
|-------|---------|
| `oxide/config/Kits.json` | `HarmonyConfig/Kits.json` |
| `oxide/data/Kits/Kits.json` | `HarmonyData/Kits/Kits.json` |
| `oxide/data/Kits/DisabledAutoKits.json` | `HarmonyData/Kits/DisabledAutoKits.json` |
| `oxide/data/Kits/Players/{steamid}.json` | `HarmonyData/Kits/Players/{steamid}.json` |
| `oxide/data/TheMevent/...` (offline images) | `HarmonyData/TheMevent/...` |

Relative data keys are unchanged (`Kits/Kits`, `Kits/Players/{id}`, etc.) — the data root is `HarmonyData/`.

## Notes

- Enable **Offline Image Mode** is optional; built-in HTTP image loader works without ImageLibrary.
- **Permissions:** Load `Permissions.dll` for Oxide-style groups. Kit `Permission` fields (e.g. `kits.defensep3`) are enforced via that mod. Server admins do **not** auto-pass kit perms — grant via `admin` (or other) groups. See `HarmonyConfig/Permissions.json`.
- **AutoWipe** on new save: wipe detection uses `SaveRestore.WipeId` change; first boot after load stores the id without wiping. Use `kits.reset` manually if needed.
- Plugin hooks (`OnKitRedeemed`, `canRedeemKit`, Notify) are no-ops without Oxide plugins.
