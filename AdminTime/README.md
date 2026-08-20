# AdminTime

Harmony mod: per-player admin time and weather. Commands: `/mytime`, `/myweather`, `/storm`, `/myweather.clear`. No Oxide; permissions via config allowlist. Port of the Admin Time Oxide plugin (nivex + Grimm530).

## Mod Identity

| Item | Value |
|------|-------|
| **Purpose** | Let allowed players set personal time (0–24h), clouds/fog/rain/wind/brightness, and (admin) global storm |
| **Entry point** | `AdminTimeMod` implements `IHarmonyModHooks` |
| **Authorization** | Config: `AllowedSteamIds` + `AdminsCanUseMytime`; `/storm` optional admin-only via `StormAdminOnly` |

## Project Structure

| File | Responsibility |
|------|----------------|
| `AdminTimeMod.cs` | Lifecycle, command registration, permission check, time/weather logic, SendVar, public API |
| `AdminTimeConfig.cs` | JSON config load, allowlist, block positions (x,z,radius) |
| `Patches/Patch_BasePlayer_ServerInit.cs` | On connect: re-apply stored overrides (Toggle(player, true)) |

## Persistent Data Model

- **In-memory only:** `_players`: `Dictionary<ulong, Dictionary<string, float>>` — per-player keys: `time`, `clouds`, `fog`, `rain`, `wind`, `brightness`. Not saved to disk. Cleared for a player when they disconnect so on reconnection they see server time/weather and must use /mytime and /myweather again.
- **Config:** `AdminTime.json` — `AllowedSteamIds`, `AdminsCanUseMytime`, `StormAdminOnly`, `BlockInEventTerritory`, `BlockPositions` (list of `"x,z,radius"`).

## Command Surface

| Command | Permission | Purpose |
|---------|------------|---------|
| `/mytime [0-24\|-1]` | Allowlist or admin | Set personal time (hours); -1 or omit to clear |
| `/myweather <clouds\|fog\|rain\|wind\|brightness\|clear> [value]` | Allowlist or admin | Set per-player weather; `clear` = reset all |
| `/myweather.clear` | Allowlist or admin | Alias to clear all weather overrides |
| `/storm <0-1\|off\|on\|default>` | Admin (if StormAdminOnly) | Global thunder/lightning intensity |

**Coexistence with Radar:** AdminTime and Radar both patch `ConVar.Chat.say` and register replicated console commands. They do not conflict: AdminTime handles only the four commands above; Radar handles only `/radar`. Load order does not matter. Both mods add their commands to the Replicated list so `/mytime` and `/radar` work for all players when both mods are loaded.

## Config Options

| Option | Type | Description |
|--------|------|-------------|
| AllowedSteamIds | string[] | Steam IDs that can use mytime/myweather |
| AdminsCanUseMytime | bool | If true, server admins can use without being in list |
| StormAdminOnly | bool | If true, only admins can use /storm |
| BlockInEventTerritory | bool | If true, block overrides inside BlockPositions |
| BlockPositions | string[] | `"x,z,radius"` entries (e.g. `"100,200,50"`) |

## Harmony Patches

| Patch | Target | Type | Purpose |
|-------|--------|------|---------|
| `Patch_BasePlayer_ServerInit` | `BasePlayer.ServerInit` | Postfix | Call `AdminTimeMod.OnPlayerConnected` (no-op: new connections see server time/weather) |
| `Patch_BasePlayer_OnDisconnected` | `BasePlayer.OnDisconnected` | Postfix | Clear player's cached overrides so they must use /mytime and /myweather again on next connect |
| `Patch_EnvSync_ServerInit` | `EnvSync.ServerInit` | Postfix | Bind the live EnvSync instance so /mytime can push per-player sky snapshots |
| `Patch_BasePlayer_QueueUpdate` | `BasePlayer.QueueUpdate` | Prefix | Do not queue the 5s stock EnvSync daytime snapshot to players with a time override |
| `Patch_BaseEntity_CanUseNetworkCache` | `BaseEntity.CanUseNetworkCache` | Postfix | Disable EnvSync snapshot cache for players with a time override |
| `Patch_EnvSync_Save` | `EnvSync.Save` | Postfix | Rewrite `environment.dateTime` for that connection so the client sky shows /mytime |

Client `EnvSync.Update` applies the networked sky time every frame. After Facepunch time/demo changes, that can ignore the `admintime` convar, which is why `/mytime` could reply success with no visible change. The EnvSync patches are the visible-time path; `admintime` is still sent as a client command (with delayed IsAdmin restore) and as `global.admintime` via ConsoleReplicatedVars.

## Lifecycle

- **OnLoaded:** Load config, register console/chat commands (`mytime`, `myweather`, `storm`, `myweather.clear`) in Dict/GlobalDict and add them to the **Replicated** list so F1 and chat work for players who join after server start. No overrides applied to existing players; everyone sees server time/weather until they use the commands.
- **OnPlayerDisconnected:** Remove player from `_players` so their overrides are cleared for next connect.
- **OnUnloaded:** Remove commands from Replicated list and Dict/GlobalDict, Toggle(each stored player, false), clear `_players`.

## Differences from Oxide Plugin

- **No Oxide:** No `permission` library; use config `AllowedSteamIds` and `AdminsCanUseMytime`.
- **No RaidableBases hook:** Cannot call `EventTerritory` via Oxide. Use config `BlockPositions` (x,z,radius) to block overrides in specific areas.
- **No PvP delay hooks:** Oxide `OnPlayerPvpDelayEntry`/`Expired` not available; overrides are not auto-disabled during PvP delay.
- **API:** Same semantics: `SetPlayerTime`, `GetPlayerTime`, `HasTimeOverride`, `ResetPlayerTime` (static on `AdminTimeMod`) for Oxide plugins to call via reflection.

## Replicated commands (no reload for new players)

Commands are registered in `Dict` and `GlobalDict` with `Replicated = true`, and **added to `ConsoleSystem.Index.Server.Replicated`** via reflection. That list is what the server sends to clients on connect; without it, players who join after server start do not receive the commands and get "unknown command" in chat or F1 until the mod is reloaded. On unload, commands are removed from the replicated list and from Dict/GlobalDict. See `HARMONY_MODS_GUIDE.md` (CUI button commands / replicated commands).

## What NOT to Touch

- **ConsoleSystem registration:** Commands must be in both `Dict` and `GlobalDict`, and in the **Replicated** list (see above), so chat and F1 work for all players.
- **SendVar:** Uses `Message.Type.ConsoleReplicatedVars` and `Network.Net.sv`; client-only replicated var for brightness.
- **Admin spoof:** Non-admins get `IsAdmin` flag set only long enough for the client to accept `admintime`/`admin*`. The flag is cleared after a short delay so the entity update and console command are processed first. Visible time also goes through `EnvSync` snapshots and does not depend on that spoof.

## Build & Deploy

```powershell
.\build.ps1
```

Output: `D:\!RustServer\HarmonyMods\AdminTime.dll`. Load: `harmony.load AdminTime`. Config: `HarmonyConfig/AdminTime.json` or `oxide/config/AdminTime.json`.
