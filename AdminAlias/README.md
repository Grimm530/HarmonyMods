# AdminAlias

Harmony mod that lets you show a different in-game name so you can play as an admin under an alias. The display name is overridden everywhere (player list, chat, kill feed, name above head, etc.) for Steam IDs listed in config.

## Mod Identity

| Item | Value |
|------|-------|
| **Purpose** | Override in-game display name for configured players (e.g. admins playing under a different name) |
| **Entry point** | `AdminAliasMod` implements `IHarmonyModHooks` |
| **Config** | `HarmonyConfig/AdminAlias.json` (Overrides: Steam64 ID → display name) |

## Project Structure

| File | Responsibility |
|------|----------------|
| `AdminAliasMod.cs` | Lifecycle, config load, registers `adminalias` console command and adds it to replicated list so it works for all players (no reload needed) |
| `AdminAliasConfig.cs` | Load/save config from `HarmonyConfig/AdminAlias.json`; `GetOverride(steamId)` |
| `Patches/BasePlayer_get_displayName_Patch.cs` | Postfix on `BasePlayer.get_displayName` to return alias when configured |

## Config and Data

- **Config:** `HarmonyConfig/AdminAlias.json` under server root.
  - Created on first load if missing.
  - **`Overrides`:** `Dictionary<string, string>` — key = Steam64 ID (string), value = display name to show in-game.

Example:

```json
{
  "Overrides": {
    "76561198000000001": "RegularPlayer",
    "76561198000000002": "AnotherAlias"
  }
}
```

- No persistent data file; only config. Reload config by unloading and reloading the mod, or restart the server.

## Console Command

| Command | Purpose |
|---------|---------|
| `adminalias` | (F1) Shows your current alias if you have one in config, or "No alias set". |

The command is registered with `Replicated = true` and **added to `ConsoleSystem.Index.Server.Replicated`** via reflection so that players who join after server start receive it. Without adding to the replicated list, only Dict/GlobalDict are updated and the client never gets the command → "unknown command" until the mod is reloaded. See `HARMONY_MODS_GUIDE.md` (CUI button commands / replicated commands).

## Harmony Patches

| Patch | Target | Type | Purpose |
|-------|--------|------|---------|
| `BasePlayer_get_displayName_Patch` | `BasePlayer.get_displayName` | Postfix | If the player's Steam ID has an entry in `Overrides`, set `__result` to that name so all reads of `displayName` see the alias. |

## Lifecycle

- **OnLoaded:** Set `Instance`, load config from `HarmonyConfig/AdminAlias.json` (create default if missing). Register `adminalias` in Dict/GlobalDict and add to Replicated list.
- **OnUnloaded:** Remove command from Replicated list and Dict/GlobalDict, set `Instance = null`.

## What NOT to Touch

- Do not call Oxide or permission APIs; this mod is standalone and config-based.
- Do not patch methods that might be patched by Oxide if you need universal (with/without Oxide) behavior; `BasePlayer.get_displayName` is vanilla and safe.

## Build and Deploy

1. From the mod folder: `.\build.ps1`
2. DLL is copied to server-root `HarmonyMods/AdminAlias.dll`.
3. Ensure `HarmonyConfig/AdminAlias.json` exists (created on first load) and add your Steam64 ID and desired name under `Overrides`.
4. Load: `harmony.load AdminAlias` (or restart server; Harmony mods load automatically from `HarmonyMods/`).
5. In-game: type `adminalias` in F1 to confirm your alias (works for all players who join; no mod reload needed).

To find your Steam64 ID: profile URL or in-game F1 console after connecting. Example: `76561198000000001`.
