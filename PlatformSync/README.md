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
| **Data** | `HarmonyData/PlatformSync/` (`links.json`, optional `groups.json` fallback) |

## What changed vs Oxide PlatformSync (Harmony necessities only)

- `RustPlugin` → `PlatformSyncHarmonyEntry : IHarmonyModHooks` + `PlatformSyncPlugin`
- Config/data under `HarmonyConfig/` / `HarmonyData/` instead of `oxide/config` + `oxide/data`
- `OnPlayerConnected` → Harmony postfix on `BasePlayer.PlayerInit`
- Chat commands `/link`, `/testlink`, `/testurl` → Harmony prefix on `ConVar.Chat.say`
- Console commands registered via `ConsoleSystem` (`ps.testlink`, `ps.testurl`, `localverify`, `localverifycheck`, `localverifyroles`)
- `timer` / `webrequest` / `lang` / `permission` / `PluginReference` → `Compat` shims
- Permission groups: Oxide `Permission` library via reflection when present; otherwise `HarmonyData/PlatformSync/groups.json` + best-effort `o.usergroup` console commands
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
  "LocalVerifyOxideGroup": "discord"
}
```

Optional: `LogLinks` (default `true`).

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
| `localverify <steamid> <discordid>` | console admin | Manual verify via Rustcord role |
| `localverifycheck` | console admin | Re-check local links |
| `localverifyroles <discordid>` | console admin | List cached Discord roles |

## Note on Rustcord local verify

`localverify*` needs Rustcord APIs `DiscordUserHasRole` / `GetDiscordUserRoleNames`. The stock Harmony Rustcord mod is webhook-oriented and does not implement those; use an Oxide Rustcord that exposes them, or extend Harmony Rustcord later. Core `/link` + connect validation do **not** require Rustcord.

## Reference

- Harmony framework: `.cursor/PluginInstructionalFiles/Harmony_Mod_Execution_Framework.md`
- Original plugin: `.cursor/Oxide.Plugins.Cant-Use/PlatformSync.cs`
