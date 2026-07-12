# TeleportGUI (Harmony)

Harmony mod providing teleport, homes, and warps via chat/F1 commands and a CUI panel. Converted from k1lly0u's Oxide TeleportGUI plugin. No Oxide dependency.

## Mod Identity

| Item | Value |
|------|-------|
| **Purpose** | Player teleport to players, saved homes, warp points; tpback and death location |
| **Entry point** | `TeleportGUIMod` implements `IHarmonyModHooks` |
| **Authorization** | Config: allowlist (Steam IDs) or everyone; admins can bypass limits |

## Project Structure

| File | Responsibility |
|------|----------------|
| `TeleportGUIMod.cs` | Lifecycle, config/data load/save, command handlers, teleport logic, UI open/CUI handler |
| `TeleportGUIConfig.cs` | JSON config: allowlist, cooldowns, limits, command aliases, warp points |
| `TeleportGUIData.cs` | User data (homes, usage, cooldowns), warp points, Vector3 serialization |
| `TeleportGUIUI.cs` | CUI builder: panel, tabs (Players/Homes/Warps), title, close, prev/next, content list; full-row and action buttons (material/sprite for interactivity) |
| `Patches/Chat_Say_Patch.cs` | Intercepts `/tp`, `/home`, `/warp`, `/tpback`, `/death` (and aliases) |
| `Patches/BasePlayer_Die_Patch.cs` | Records death position for `/death` command |

## Persistent Data Model

- **Config:** `HarmonyConfig/TeleportGUI.json` or `oxide/config/TeleportGUI.json`. Options: AllowedSteamIds, AdminsBypass, command aliases, cooldowns, daily limits, MaxHomes, TeleportDelaySeconds, RecordDeathLocation, WarpPoints (name → X,Y,Z; default includes Outpost and Bandit—set coordinates in config), DataFolderPath.
- **Data:** `HarmonyData/TeleportGUI/TeleportGUI_Data.json` (or path in config). Root: Users (ulong → UserData), LastResetDate, WarpPoints. UserData: Homes (name → position), TP/Home/Warp uses today and cooldown timestamps, LastOnlineTime. Daily uses reset at midnight UTC.

## Command Surface

| Command | Purpose |
|---------|---------|
| `/tp` | Open Teleport GUI (tabs: Players, Homes, Warps) |
| `/tp <player>` | Teleport to player (cooldown + daily limit) |
| `/home` | Open GUI on Homes tab |
| `/home <name>` | Teleport to home |
| `/sethome <name>` | Set home at current position |
| `/deletehome <name>` | Remove a home |
| `/warp` | Open GUI on Warps tab |
| `/warp <name>` | Teleport to warp |
| `/tpback` | Teleport to previous position (before last tp/home/warp) |
| `/death` | Teleport to last death location (if RecordDeathLocation enabled) |

All commands available in chat (with `/`) and in F1 console. Aliases configurable in config. GUI: center panel with button tabs (Players, Homes, Warps) at top; Players lists online players (click row or "TP To" to teleport); Homes and Warps list entries (click row or "Go"). Warps can include Outpost, Bandit, or any admin-defined point (set X,Y,Z in config). All buttons use material/sprite for reliable interactivity. Clicks handled via `teleportgui.cui` console command.

## Harmony Patches

| Patch | Target | Type | Purpose |
|-------|--------|------|---------|
| `Chat_Say_Patch` | `ConVar.Chat.say` | Prefix | Handle `/command [args]`; return false to suppress chat when handled |
| `BasePlayer_Die_Patch` | `BasePlayer.Die` | Postfix | Record death position for `/death` |

## Lifecycle

- **OnLoaded:** Load config and data, merge config warps into data, register console commands (all aliases), log.
- **OnUnloaded:** Unregister commands, save data, clear Instance.

## What NOT to Touch

- **No Oxide:** Do not call Oxide APIs or hooks; mod runs standalone.
- **Chat patch:** Must return `true` when the message is not one of our commands so other mods/plugins can handle it.
- **Teleport:** Uses `player.MovePosition` and `ClientRPCPlayer("ForcePositionTo")`; delay/cancel on move is implemented in the mod.

## Differences from Oxide TeleportGUI

- No Chaos UI; custom CUI panel (tabs, lists, buttons). Commands + GUI.
- No player-to-player request/accept flow; `/tp <player>` teleports the caller to the target after delay.
- No monument discovery; warps are from config or data only. Default config includes Outpost and Bandit (0,0,0 until admin sets X,Y,Z).
- No ZoneManager/NoTP checks.
- No sleeping-bag auto-home; set home only via `/sethome`.
- Permissions: allowlist (Steam IDs) + admin bypass instead of Oxide permissions.

## Build & Deploy

```powershell
.\build.ps1
```

Output: `D:\!RustServer\HarmonyMods\TeleportGUI.dll`. Load: `harmony.load TeleportGUI`. Config: `HarmonyConfig/TeleportGUI.json` or `oxide/config/TeleportGUI.json`.
