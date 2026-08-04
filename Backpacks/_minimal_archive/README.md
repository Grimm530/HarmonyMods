# Backpacks (Harmony Mod)

Standalone Harmony mod that gives each player an **extra-inventory backpack**: open via F1 `backpack` or by **clicking the on-screen backpack button**, persist to JSON, and on death either **drop** a dropped-backpack entity or **erase** contents (config). No Oxide; follows HARMONY_MODS_GUIDE.

## Mod Identity

| Item | Value |
|------|--------|
| **Purpose** | Per-player virtual backpack (extra inventory), drop or erase on death |
| **Entry point** | `BackpacksMod` implements `IHarmonyModHooks` |
| **Loading** | `harmony.load Backpacks` (from `HarmonyMods/Backpacks.dll`) |

## Project Structure

| File | Responsibility |
|------|----------------|
| `BackpacksMod.cs` | Lifecycle, config, open/save backpack, death handler, `backpack` command, button show/destroy |
| `BackpacksConfig.cs` | JSON config (DropOnDeath, EraseOnDeath, Capacity, button image path/URL, position) |
| `BackpackButtonUI.cs` | CUI: build JSON for on-screen button (image + click), Show/Destroy via CommunityEntity |
| `BackpackData.cs` | `BackpackItemEntry` for JSON (itemid, amount, slot, condition, blueprint, skin, contents) |
| `BackpackStorageMarker.cs` | Component on virtual backpack entity (OwnerId) so we save on loot close |
| `Patches/Patch_BasePlayer_Die.cs` | Postfix → drop or erase backpack on death; destroy button |
| `Patches/Patch_PlayerLoot_Clear.cs` | Prefix → save backpack when player closes loot if it was our container |
| `Patches/Patch_BasePlayer_ServerInit.cs` | Postfix → show backpack button when player connects |
| `Patches/Patch_BasePlayer_EndSleeping.cs` | Postfix → show backpack button when player wakes (respawn) |
| `Patches/Patch_ServerMgr_Initialize.cs` | Prefix → ensure server identity folder exists before FileStorage opens (avoids SqliteException error 14) |

## Persistent Data Model

- **Config:** `HarmonyConfig/Backpacks.json` (or `oxide/config/Backpacks.json`, `Config/Backpacks.json`, server root `Backpacks.json`). Options: Drop on death, Erase on death, Capacity (slots), Minimum despawn time; **Show backpack button**, **Button image path** (e.g. `HarmonyImages/Backpack/backpackgz.png`), **Button image URL** (optional override), **Button position** (anchormin/anchormax).
- **Per-player data:** `HarmonyData/BackpacksData/<steamid>.json` (default; configurable via **Data folder path**) — list of `BackpackItemEntry` (itemid, amount, slot, condition, maxCondition, blueprint, skin, contents for nested items). The mod **only** reads/writes these JSON files; it does **not** write to the server save or to FileStorage (sv.files.*.db).

## Harmony Patches

| Patch | Target | Type | Purpose |
|-------|--------|------|---------|
| `Patch_BasePlayer_Die` | `BasePlayer.Die` | Postfix | Destroy button, then call `BackpacksMod.OnPlayerDie`: drop or erase per config |
| `Patch_PlayerLoot_Clear` | `PlayerLoot.Clear` | Prefix | If loot included our backpack container, save to JSON and destroy entity |
| `Patch_BasePlayer_ServerInit` | `BasePlayer.ServerInit` | Postfix | Show on-screen backpack button to player |
| `Patch_BasePlayer_EndSleeping` | `BasePlayer.EndSleeping` | Postfix | Show on-screen backpack button (e.g. after respawn) |
| `Patch_ServerMgr_Initialize` | `ServerMgr.Initialize` | Prefix | Ensure `server/<identity>/` exists so FileStorage (sv.files.*.db) can open; prevents error 14 when loading/saving vending machines etc. |

## On-screen button (CUI)

- **Visibility:** When **Show backpack button** is true in config, a clickable backpack image is shown on the HUD (default: top-right, anchormin `0.85 0.88`, anchormax `0.98 0.98`). Players can click it to open their backpack instead of typing `backpack` in F1.
- **Image:** On plugin load the mod loads **`HarmonyImages/Backpack/backpackgz.png`** (or **Button image path** in config) into Rust's FileStorage, caches the texture ID, and uses it for the CUI RawImage so the button shows your custom image. If that file is missing or **Button image URL** is set, the fallback saddle-bag icon is used. Image is loaded on NextTick (and retried after 5s if FileStorage/CommunityEntity isn't ready), so when a player logs on the cached image is already set.
- **Lifecycle:** Button is shown on connect (ServerInit) and on wake/respawn (EndSleeping); destroyed on death and on mod unload.

## Commands

| Command | Purpose |
|---------|--------|
| `backpack` (F1) | Open your backpack (virtual container); same as clicking the on-screen button |

## Lifecycle

- **OnLoaded:** Set `Instance`, load config, build button JSON (URL or fallback icon), register `global.backpack` command, show button to all active players; NextTick load `HarmonyImages/Backpack/backpackgz.png` into FileStorage and cache texture ID, then rebuild JSON and refresh button for all players (retry after 5s if not ready).
- **OnUnloaded:** Unregister command, destroy button and page buttons for all players, save and kill any open backpack entities, clear `_openBackpackState`, set `Instance = null`.

## FileStorage / server save

- **Backpack data:** Stored only in external JSON files under `HarmonyData/BackpacksData/` (or your configured **Data folder path**). The mod does **not** write backpack data to the server save.
- **Button image:** The mod loads `HarmonyImages/Backpack/backpackgz.png` (or config path) into FileStorage on load, caches the texture ID, and uses it for the on-screen button (CUI RawImage → client receives via CL_ReceiveFilePng). Load is deferred to NextTick + 5s retry so FileStorage is ready. Use **Button image URL** to skip FileStorage and use a hosted URL instead.
- **Patch_ServerMgr_Initialize:** Optionally creates `server/<identity>/` before the game opens FileStorage, so other game code (e.g. NPC vending machines) can open the database. Keep Backpacks loaded if you rely on that safeguard.

*(Obsolete: custom image from file no longer uses FileStorage; use Button image URL.)* If the server log shows `SqliteException: Could not open database file: server/my_server_identity/sv.files.281.db (error 14)` when loading or saving (e.g. NPC vending machines), the game’s FileStorage is opening before the server identity folder exists. This mod’s **Patch_ServerMgr_Initialize** runs before server init and creates `server/<identity>/` (e.g. `server/my_server_identity/`) if missing, so the database can open. Keep Backpacks loaded so this fix is applied even if you don’t use the backpack feature.

## What NOT to Touch Without Care

- **Drop on death:** Uses game prefab `item_drop_backpack` and `DroppedItemContainer`; do not change prefab or capacity after spawn.
- **Virtual open:** Uses hidden `coffinstorage` at (0,-500,0) with `BackpackStorageMarker`; save runs when `PlayerLoot.Clear` is called (loot closed).
- **Serialization:** Flat list of items + optional nested `Contents`; complex modded items may not round-trip perfectly.

## Interaction with BetterBackpack

**BetterBackpack** (`.cursor/HarmonyMods/BetterBackpack/`) extends the **vanilla** wearable backpack (small/large) with Existing and Retrieval; it uses **/existing** and **/retrieval** (chat), not a `backpack` command.

- **Backpacks (this mod)** registers the **`backpack`** **console** command (F1). So: F1 `backpack` opens this mod’s virtual extra-inventory; the **vanilla** worn backpack is still opened by clicking the backpack icon in the inventory (Tab), where BetterBackpack’s buttons appear.
- No command overlap: BetterBackpack uses /existing and /retrieval; this mod uses the console command `backpack`. No patch overlap either (different patch targets). Both can run together without interference.

## Relation to Oxide Backpacks Plugin

The WhiteThunder **Backpacks** Oxide plugin (`.cursor/Oxide.Plugins.Cant-Use/Backpacks.cs`) is a full-featured plugin with permissions, GUI, multi-page backpacks, gather/retrieve modes, and Oxide hooks. This Harmony mod is a **minimal standalone** port: one container per player, JSON persistence, drop/erase on death, open via command. It does **not** call Oxide; it patches the game directly per HARMONY_MODS_GUIDE.

## Build & Deploy

```powershell
.\build.ps1
```

Output: `D:\!RustServer\HarmonyMods\Backpacks.dll`. Load with `harmony.load Backpacks`. Config path: `HarmonyConfig/Backpacks.json` (or oxide/config, etc.).
