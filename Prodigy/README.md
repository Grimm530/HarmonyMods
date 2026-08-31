# Prodigy (Harmony Mod)

Prodigy is a Harmony mod that provides detailed entity information (“prod” info) when you look at an entity and run the `prodigy` command. It shows owner, last online, position, prefab, type, health, code lock, auth list, build date (for blocks/TC), and entity-specific details. No Oxide dependency.

## Mod identity

| Field | Value |
|-------|--------|
| **Name** | Prodigy |
| **Type** | Harmony mod (IL patching + console commands + CUI) |
| **Load** | `harmony.load Prodigy` (not `o.load`) |
| **Unload** | `harmony.unload Prodigy` |
| **Origin** | Ported from Oxide plugin Prodigy by nivex |

## Project structure

| Path | Purpose |
|------|---------|
| `Prodigy.csproj` | .NET 4.8 project; references Rust/Harmony/Unity |
| `ProdigyMod.cs` | Main mod: lifecycle, config, data, `prodigy` / `prodigy_ui_move` commands, Prod logic |
| `ProdigyConfig.cs` | Config: AdminOnly, AllowedSteamIds, AllowedMlrsSteamIds, DataFolder |
| `ProdigyData.cs` | Data model: Blocks, TC, Offsets, LogObject, UiOffsets, Vector3Converter |
| `ProdigyUI.cs` | CUI via CommunityEntity (AddUI/DestroyUI); large and small panel layouts |
| `ProdigyTickBehaviour.cs` | MonoBehaviour Update: save every 300s, UI auto-close timers |
| `Patches/Patch_BaseEntity_OnPlaced.cs` | Records block/TC placement for build-date logging |
| `Patches/Patch_GlobalNetworkHandler_OnEntityKilled.cs` | Removes block/TC from data when entity is killed |
| `Patches/Patch_Chat_Say.cs` | Intercepts `/prod` and `/prodigy` chat so keybind `bind p "chat.say /prod"` works |
| `build.ps1` | Builds and copies `Prodigy.dll` to `HarmonyMods/` |

## Persistent data model

- **File:** `HarmonyData/Prodigy/ProdigyData.json` (path from config `DataFolder`).
- **Contents:** `Blocks` (per-user build logs by position), `TC` (per-user TC logs), `Offsets` (per-user UI position/size), `WipeId`.
- **New wipe:** On load, if `SaveRestore.WipeId` != stored `WipeId`, `Blocks` and `TC` are cleared and `WipeId` is updated.
- **Save:** Every 300 seconds and on unload; only when `Changed` is true.

## Commands

| Command | Who | Effect |
|---------|-----|--------|
| `/prod` or `prodigy` | Admin or allowed (see config) | Raycast from eyes; show prod info and CUI panel for hit entity. |
| `prodigy reset` | Same | Reset current user’s UI offsets. |
| `/prod components` or `prodigy components` | Same | With entity hit: reply with component list (type, name, layer). |
| `prodigy_ui_move close` | Same | Close prodigy UI. |
| `prodigy_ui_move up/down/left/right <arg>` | Same | Move panel (from UI buttons); `arg` is encoded panel state. |

**Keybind (F1):** The command is server-only, so a keybind must go through chat. Use:  
`bind p "chat.say /prod"`  
Then press **P** while looking at an entity to run prodigy. Use `bind p "chat.say /prod reset"` for reset. The chat message is suppressed (not shown to others). `/prodigy` also works.

## Config (HarmonyConfig/Prodigy.json)

| Option | Meaning |
|--------|--------|
| `AdminOnly` | If true, only admins can use prodigy; else `AllowedSteamIds` apply (admins always allowed). |
| `AllowedSteamIds` | Steam IDs allowed when `AdminOnly` is false. |
| `AllowedMlrsSteamIds` | Steam IDs allowed to use MLRS repair (hammer on MLRS). |
| `DataFolder` | Folder under server root for `ProdigyData.json` (e.g. `HarmonyData/Prodigy`). |

## Patches

| Patch | Method | Effect |
|-------|--------|--------|
| `Patch_BaseEntity_OnPlaced` | `BaseEntity.OnPlaced(BasePlayer)` | On place of BuildingBlock/BuildingPrivlidge, append log (user, position, date) to `Blocks` or `TC`. |
| `Patch_GlobalNetworkHandler_OnEntityKilled` | `GlobalNetworkHandler.OnEntityKilled(BaseNetworkable)` | On kill of BuildingBlock/BuildingPrivlidge, remove matching log from data. |

## Lifecycle

- **OnLoaded:** Apply Harmony patches, load config and data, create tick GameObject, register `prodigy` and `prodigy_ui_move`. Clear Blocks/TC if wipe changed.
- **OnUnloaded:** Destroy UI for allowed players, save data, unpatch, unregister commands, clear Instance.

## UI behavior

- **Panel:** Large or small layout; per-user offsets and “small UI” stored in data.
- **Rows:** Position/Type/Size/Col/Last/**Owner** on the left; PrefabId/Health/Building ID/Skin/Code/**Last Online** on the right. Last Online is `Online` when the owner is connected, otherwise the last-seen date/time from PlaytimeTracker (`yyyy-MM-dd HH:mm` local), or `N/A`.
- **Timed:** Panel auto-closes after 10 seconds unless user held Sprint when running prodigy (then “Timed UI disabled”). Duck when running prodigy toggles small UI.
- **Buttons:** Move (up/down/left/right) and close; commands are `prodigy_ui_move …` and executed server-side.

## What is not included (vs Oxide Prodigy)

- No Oxide permissions (`prodigy.allow`, `prodigy.mlrs`); use config `AdminOnly` / `AllowedSteamIds` / `AllowedMlrsSteamIds`.
- No Oxide plugin refs (AbandonedBases, Clans, RaidableBases); no “Abandoned Base”, “Raidable Base”, or clan tag (shows “Clan Tag: N/A”).
- No Oxide CUI; uses game CommunityEntity AddUI/DestroyUI with JSON CUI.

## Performance

- Patches run only on place/kill of building block or TC; no hot-path scans.
- Save is throttled (300s) and only when dirty. UI timers are processed in a single Update.

## Build and deploy

1. Open PowerShell in the mod folder (e.g. `.cursor/HarmonyMods/Prodigy`).
2. Run: `.\build.ps1`
3. Ensure `Prodigy.dll` is in server `HarmonyMods/` (script copies to `D:\!RustServer\HarmonyMods` by default).
4. In server console: `harmony.load Prodigy` (reload: `harmony.unload Prodigy` then `harmony.load Prodigy`).
