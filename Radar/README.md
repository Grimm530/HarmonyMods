# Radar

Harmony mod for admin entity ESP (players, sleepers, corpses, bags, TCs, stashes, backpacks). Uses built-in `ddraw.arrow`, `ddraw.box`, `ddraw.text`. Configurable via `HarmonyConfig/Radar.json` (mirrors `AdminRadar.json` markers, drawing distances, and GUI layout).

## Mod Identity

| Item | Value |
|------|-------|
| **Purpose** | Entity visibility (radar/ESP) for admins |
| **Entry point** | `RadarMod` implements `IHarmonyModHooks` |
| **Authorization** | Admin or developer only (`player.IsAdmin \|\| player.IsDeveloper`) |

## Project Structure

| File | Responsibility |
|------|----------------|
| `RadarMod.cs` | Lifecycle, CUI panel, chat handling, state storage, loads `RadarConfig` |
| `RadarState.cs` | Per-player enabled flags per entity type |
| `RadarBehaviour.cs` | MonoBehaviour on player; scans entities, sends `ddraw` commands |
| `RadarConfig.cs` | Harmony config loader (`HarmonyConfig/Radar.json`); colors, drawing distances, GUI, options |
| `Patches/Chat_Say_Patch.cs` | Intercepts `/radar` chat |
| `Patches/Cui_Endtest_Patch.cs` | Intercepts `cui.endtest RADAR` for UI buttons |

## Persistent Data Model

- **Config file (Harmony).**  
  - Location: `HarmonyConfig/Radar.json` under the server root (see Harmony Mods guide).  
  - Created automatically on first load if missing.  
  - Shape mirrors relevant portions of Oxide `AdminRadar.json`:
    - **`Color-Hex Codes`**: Per-marker colors (players, sleepers, corpses, bags, TC, stash, backpacks, and additional AdminRadar markers for future expansion).
    - **`Drawing Distances`**: Per-entity-type maximum draw distances (players, corpses, bags, TC, stash, backpacks, etc.).
    - **`GUI`**: Move arrow text, panel anchor offsets, ON/OFF colors, and per-entity “Show Button - X” flags that control which toggles render in the Harmony radar UI.
    - **`Options`**: Box/trap prefab or item shortnames (`Boxes`, `Additional Traps`), NPC target display, and text/amount options (mirrored from `AdminRadar.json`).
    - **`Track Admin Status`**: Cheat/status prefixes on player labels (Radar, God, Vanish, NOCLIP, Spectating).
    - **`Settings`**: Default view distance, refresh timing hints, and `User Interface Enabled` flag.
- **Runtime state in memory.**
  - `RadarMod.PlayerStates`: `Dictionary<ulong, RadarState>` keyed by `userID`.
  - `RadarState`: `Enabled`, `ViewDistance` (50–800m, step 50; default from config `Settings -> Default Distance`), move mode flag, UI anchors (`GUI -> Offset Min/Max`), `_enabled` (HashSet of `RadarEntityType`).
  - Default entity types ON: Players, Sleepers, Dead, Bags, TC. Stash and Backpack default OFF (config controls button visibility; per-player state controls ON/OFF).

## Command Surface

| Command | Permission | Purpose |
|---------|------------|---------|
| `/radar` (chat or F1) | Admin/Developer | Toggle radar and show/hide UI panel |

**Replicated commands:** Two console commands are registered so they work when used with other mods (e.g. AdminTime) and for players who join after server start:
- **`radar`** — Name the client looks up for `/radar` in chat/F1; without it the client shows "unknown command" and never sends to the server.
- **`RADAR_CMD`** — Used by CUI buttons via `cui.endtest RADAR <action>`; handler also supports direct F1 `RADAR_CMD` with args.

Both are added to `ConsoleSystem.Index.Server.Replicated` via reflection. CUI buttons use `cui.endtest RADAR <action>` (vanilla replicated) so they work for all players. See `HARMONY_MODS_GUIDE.md` (CUI button commands / replicated commands).

## Harmony Patches

| Patch | Target | Type | Purpose |
|-------|--------|------|---------|
| `Chat_Say_Patch` | `ConVar.Chat.say` | Prefix | Intercept `/radar` when message is sent as chat (e.g. F1 `chat.say /radar`); return `!handled` to suppress default chat when handled |
| `RunWithResult_Patch` | `ConsoleSystem.RunWithResult` | Prefix | When the client sends the command `radar` or `global.radar` (e.g. /radar in chat box), handle it so /radar works even if the command wasn’t in the client’s replicated list |
| `Cui_Endtest_Patch` | `cui.endtest` | Prefix | Handle Radar `RADAR` (CLOSE, TOGGLE_RADAR, TOGGLE_{EntityType}, RANGE_UP, RANGE_DOWN); return `!handled` so TCUpgrade etc. can handle their commands |

## Entity Types & Sources

| Type | Source | Display |
|------|--------|---------|
| Players | `BasePlayer.activePlayerList` | Name + optional `(R|G|V|F|S)` status prefix from `Track Admin Status` |
| NPC (Gen1 humanoids: scientists, GrimmBoss/GrimmNPC `ScientistNPC`, etc.) | `BasePlayer.bots` (+ `activePlayerList` if `IsNpc`), plus `Vis.Entities` for `TravellingVendor` / humanoid `BaseNPC2` | Prefab short name, or **Grimm** `Name` from `GrimmNPC.GetNpcData(netId)` when GrimmNPC is loaded (vanilla `ScientistNPC.displayName` is always the translated &quot;Scientist&quot;, so Radar reads registered data for the Grimm skin) |
| Sleepers | `BasePlayer.sleepingPlayerList` | Player name (same marker as players: dot + name, light blue-green) |
| Dead | `Vis.Entities` → `PlayerCorpse` | `playerSteamID` |
| Bags | `Vis.Entities` → `SleepingBag` | Owner name/ID via `deployerUserID` |
| TC | `Vis.Entities` → `BuildingPrivlidge` | "TC" |
| Stash | `Vis.Entities` → `StashContainer` | "Stash" (no owner—`StashContainer` has no owner API) |
| Backpack | `Vis.Entities` → `DroppedItemContainer` | `playerSteamID` |

## Coexistence with AdminTime

Both mods patch `ConVar.Chat.say` and register replicated console commands. They do not conflict: AdminTime handles only `mytime`, `myweather`, `storm`, `myweather.clear`; Radar handles only `radar`. Load order does not matter. Ensure both mods add their commands to the Replicated list (Radar registers `radar` and `RADAR_CMD`; AdminTime registers `mytime`, `myweather`, `storm`, `myweather.clear`) so `/mytime` and `/radar` both work for all players.

## Lifecycle

- **OnLoaded:** Set `Instance`, load config, register `radar` and `RADAR_CMD` in Dict/GlobalDict and add both to the **Replicated** list, log message.
- **OnUnloaded:** Remove both commands from Replicated list and Dict/GlobalDict, destroy `RadarBehaviour` on all players with state, clear `PlayerStates`, set `Instance = null`.
  - Config is not written on unload except via explicit save; initial load always ensures a `HarmonyConfig/Radar.json` exists.

## UI (CUI)

- Panel: `Radar_ESP_Panel`, parent `Hud` (avoids TCUpgrade overlap on Overlay/OverlayNonScaled).
- Range control: `-` / `+` buttons to decrease/increase view distance (50–800m, step 50).  
  - **Default distance** is taken from `HarmonyConfig/Radar.json` → `Settings -> Default Distance` (mirrors `AdminRadar.json (Settings -> "Default Distance")`).
- Toggle colors: configurable to match AdminRadar:
  - `HarmonyConfig/Radar.json -> GUI -> "Color On"` used for ON-state buttons.
  - `HarmonyConfig/Radar.json -> GUI -> "Color Off"` used for OFF-state buttons.
- Close button: uses both `close` property (client-side destroy) and SENDCMD CLOSE (server cleanup).
- Movement:
  - Move button text: from `GUI -> "Move Arrow Text"` (e.g. `"↕"`) combined with `" Move"` to match AdminRadar-style “↕ Move” label.
  - Panel anchor defaults: from `GUI -> "Offset Min"` / `"Offset Max"`; stored per-player in `RadarState.UiAnchorMin/Max`. Values are normalized anchors (0–1) in Harmony Radar, but are shaped like the AdminRadar GUI block so config is compatible.
  - When move mode is active, `Radar_ESP_Move` overlay shows ← ↑ ↓ → controls; `GUI -> "Move Arrow Text"` does not change the move overlay arrows, only the main button caption.
- Per-entity toggle buttons:
  - Entity list is fixed in code (`RadarEntityType`), but **visibility of each toggle** is controlled by `HarmonyConfig/Radar.json -> GUI -> "Show Button - X"` flags, mirroring `AdminRadar.json (152–185)`.
  - Example mappings:
    - `RadarEntityType.Sleepers` → `"Show Button - Sleepers"`.
    - `RadarEntityType.Dead` → `"Show Button - Dead"`.
    - `RadarEntityType.Bags` → `"Show Button - Bags"`.
    - `RadarEntityType.TC` → `"Show Button - TC"`.
    - `RadarEntityType.Stash` → `"Show Button - Stash"`.
    - `RadarEntityType.Backpack` uses the Bags flag by design (no separate AdminRadar flag).
- UI enable/disable:
  - `HarmonyConfig/Radar.json -> Settings -> "User Interface Enabled"` can disable the CUI entirely while keeping radar ddraw markers active.
- CUI via `CommunityEntity.ServerInstance.ClientRPC("AddUI", ...)` and `DestroyUI`.
- Buttons use `cui.endtest RADAR <ACTION>` (avoids TCUpgrade SENDCMD conflict); `Cui_Endtest_Patch` routes to `RadarMod.HandleCuiCommand`. `RADAR_CMD` is also registered in ConsoleSystem and added to the **Replicated** list so F1/direct use works for all players (no mod reload needed).

### Markers and Drawing Distances (matching AdminRadar.json)

- **Markers / colors:**
  - `HarmonyConfig/Radar.json -> "Color-Hex Codes"` mirrors `AdminRadar.json (48–90)`; Harmony Radar currently reads:
    - `Online Player` for live players.
    - `Sleeping Player` / `Sleeping Dead Player` for sleepers.
    - `Dead Player` for corpses.
    - `Sleeping Bags` for sleeping bags.
    - `Tool Cupboards` for TC.
    - `Stash` for stash containers.
    - `Backpacks` for dropped item containers.
  - Additional AdminRadar marker colors (Helicopters, Bradley, Boats, Cars, CCTV, MLRS, NPC, Traps, etc.) are present in the config so the JSON matches AdminRadar; they are available for future Harmony Radar entity types.
- **Drawing distances:**
  - `HarmonyConfig/Radar.json -> "Drawing Distances"` mirrors `AdminRadar.json (102–135)` and is applied per type:
    - Players/Sleepers → `"Players"`.
    - Corpses → `"Player Corpses"`.
    - Bags → `"Sleeping Bags"`.
    - TC → `"Tool Cupboards"`.
    - Stash → `"Stashes"`.
    - Backpacks → `"Backpacks"` (extension key for Harmony Radar).
  - Effective distance = `min(player ViewDistance slider, per-type drawing distance)`.  
    This preserves the AdminRadar idea of per-marker caps while still letting admins shrink the global slider.

### Options → Boxes (AdminRadar 5.4.312)

- **`HarmonyConfig/Radar.json -> Options -> Boxes`** replaces the old `Additional Boxes` key.
- Values may be **item shortnames** (e.g. `krieg_storage`, `box.wooden.large`) or **entity prefab fragments**; deployable item definitions are resolved at runtime like AdminRadar.
- An **empty `Boxes` array disables box ESP** entirely (optional tracking).
- `krieg_storage` variants are boxes, not loot containers.

### Track Admin Status

- Mirrors `AdminRadar.json -> Track Admin Status`, including **Spectating** / **Spectating Text** (`S` marker when `spectatingTarget` is set).
- Prefixes render before player names for online players and sleepers when the corresponding flag is enabled.

## What NOT to Touch Without Care

- **Patch targets:** `ConVar.Chat.say` and `cui.endtest` signatures may change by Rust version.
- **UI panel name:** `Radar_ESP_Panel` used for destroy; changing breaks cleanup.
- **Cui_Endtest_Patch:** Must return `true` (run original) when Radar does not handle the command—otherwise TCUpgrade/other mods using SENDCMD will break. Uses RADAR prefix to avoid conflict.
- **Vis.Entities:** Uses physics sphere; scan radius capped at 200m (`MaxVisScanRadius`) to avoid "Vis query is exceeding collider buffer length" (vanilla buffer 32768). Players/Sleepers use lists + full ViewDistance; Dead/Bags/TC/Stash/Backpack use Vis, so max ~200m for those.

## Performance

- Scan interval: 0.5s (`RadarBehaviour.Update`).
- `Vis.Entities<BaseEntity>` with shared static buffer; entity checks use type casts and `IsDestroyed`.
- `ddraw.clear` each scan; drawings expire after `DrawDuration` (0.6s).

## Build & Deploy

```powershell
.\build.ps1
```

Output: `D:\!RustServer\HarmonyMods\Radar.dll`. Load: `harmony.load Radar`.
