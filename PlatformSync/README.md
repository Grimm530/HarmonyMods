# PlatformSync (Harmony mod)

Harmony port of **Platform Sync server plugin** 1.1.01 (`PlatformSync | Grimm530`). Behavior is kept as close as possible to the Oxide plugin; only loader/config/hooks differ.

## Identity

| Field | Value |
|-------|--------|
| **Name** | PlatformSync |
| **Source** | `.cursor/Oxide.Plugins.Cant-Use/PlatformSync.cs` |
| **Type** | Harmony mod (`IHarmonyModHooks`) |
| **Entry** | `PlatformSync.PlatformSyncHarmonyEntry` |
| **Config** | `HarmonyConfig/PlatformSync.json` (migrates from `oxide/config/PlatformSync.json` if present) |
| **Data** | `HarmonyData/PlatformSync/` (`links.json`, optional `groups.json` mirror/fallback) |
| **Permissions** | **0Permissions** — Discord link / nitro groups (`verified`, `nitro`, …) via `Permissions_ApiType` + generation rebind |

## What changed vs Oxide PlatformSync (Harmony necessities only)

- `RustPlugin` → `PlatformSyncHarmonyEntry : IHarmonyModHooks` + `PlatformSyncPlugin`
- Config/data under `HarmonyConfig/` / `HarmonyData/` instead of `oxide/config` + `oxide/data`
- `OnPlayerConnected` → Harmony postfix on `BasePlayer.PlayerInit`
- Chat commands `/link`, `/testlink`, `/testurl` → Harmony prefix on `ConVar.Chat.say`
- Console commands registered via `ConsoleSystem` (`ps.testlink`, `ps.testurl`, `localverify`, `localverifycheck`, `localverifyroles`)
- `timer` / `webrequest` / `lang` / `permission` / `PluginReference` → `Compat` shims
- Permission groups: **0Permissions** (`PermissionsHarmony.PermissionsMod`) with §10a generation rebind; mirrors membership to `HarmonyData/PlatformSync/groups.json`; falls back to that file only if Permissions is not loaded
- On Discord unlink / nitro loss: removes the API/config group from 0Permissions (e.g. `verified` / `nitro`)
- Rustcord: resolves Oxide Rustcord plugin, or Harmony `RustcordMod` if it exposes `DiscordUserHasRole` / `GetDiscordUserRoleNames`

**Unchanged:** validate API URLs, link/nitro group logic, local verify flow, link log format, lang strings, debug commands.

## Project structure

| File | Content |
|------|--------|
| `PlatformSyncHarmonyEntry.cs` | `IHarmonyModHooks` entry |
| `PlatformSyncPlugin.cs` | Ported plugin logic (near-identical to Oxide source) |
| `PlatformSyncConfig.cs` | Config load/save for `HarmonyConfig/PlatformSync.json` |
| `Compat.cs` | Timer, webrequest, permission, lang, Rustcord bridge, console commands |
| `Patches/BasePlayer_PlayerInit_Patch.cs` | Connect validate |
| `Patches/Chat_Say_Patch.cs` | Chat commands |

## Config

Uses existing `HarmonyConfig/PlatformSync.json`:

```json
{
  "APIToken": "...",
  "EnableDiscordLink": true,
  "EnableNitro": true,
  "GuildID": "...",
  "LocalVerifyDiscordRole": "...",
  "LocalVerifyOxideGroup": "verified"
}
```

Optional: `LogLinks` (default `true`).

`LocalVerifyOxideGroup` and the Platform Sync API `discord_oxide_group` / `nitro_oxide_group` must match group names in `HarmonyData/Permissions/groups.json` (this server uses `verified` for Discord link).

## Build / deploy

```powershell
.\.cursor\HarmonyMods\PlatformSync\build.ps1
```

Copies **only** `PlatformSync.dll` into server `HarmonyMods/`.

Load with `harmony.load PlatformSync` (or automatic at startup).

## Commands

| Command | Who | Purpose |
|---------|-----|---------|
| `/link` | players | Check Discord link / apply groups |
| `/testlink [steamid]` | admin | Debug PlatformSync API |
| `/testurl` | admin | Debug HTTP/HTTPS reachability |
| `ps.testlink` / `ps.testurl` | console admin | Same as above |
| `ps.recheck [all\|verified\|nitro\|status\|cancel]` | console / RCON admin | Re-check all members of `verified` and/or `nitro` via Platform Sync API; **only removes/adds group membership** (never deletes user data) |
| `localverify <steamid> <discordid>` | console admin | Manual verify via Rustcord role |
| `localverifycheck` | console admin | Re-check local links |
| `localverifyroles <discordid>` | console admin | List cached Discord roles |

### `ps.recheck`

Queued API validate for everyone currently in the selected permission group(s). Safe for offline players.

```text
ps.recheck              # verified + nitro
ps.recheck nitro        # nitro boosters only
ps.recheck verified     # Discord-linked / verified only
ps.recheck status
ps.recheck cancel
```

- **Removes** `verified` / `nitro` when the API says unlinked / not boosting (local-verify exceptions still kept).
- **Adds** the group when the API says they should have it.
- **Does not** delete `HarmonyData/Permissions/users.json` entries — only group membership changes.
- Rate-limited (~0.75s between requests). Prefer RCON/console over Discord slash; a Discord bot can still fire this via RCON if you want a Discord button.

## Note on Rustcord local verify

`localverify*` needs Rustcord APIs `DiscordUserHasRole` / `GetDiscordUserRoleNames`. The stock Harmony Rustcord mod is webhook-oriented and does not implement those; use an Oxide Rustcord that exposes them, or extend Harmony Rustcord later. Core `/link` + connect validation do **not** require Rustcord.

## Reference

- Harmony framework: `.cursor/PluginInstructionalFiles/Harmony_Mod_Execution_Framework.md`
- Original plugin: `.cursor/Oxide.Plugins.Cant-Use/PlatformSync.cs`
