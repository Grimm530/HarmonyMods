# CommunityTab

Harmony mod that forces the server to appear in the **Community** tab of the Rust server browser instead of the **Modded** tab by stripping the `modded` tag before server info is sent to Steam.

## Why use this?

- **Oxide:** Setting `"Options": { "Modded": false }` in `oxide/oxide.config.json` is supposed to do the same, but if your server still shows under Modded (e.g. another mod adds `modded` to tags, or config path differs), this mod enforces Community listing.
- **No Oxide:** On a vanilla or non-Oxide server, if `server.tags` or another process adds `modded`, this patch removes it so the server appears in Community.

## Mod Identity

| Item | Value |
|------|-------|
| **Purpose** | Force server browser listing under Community tab |
| **Mechanism** | Strip `modded` from `ConVar.Server.tags` (Prefix) and from `SteamServer.GameTags` (Postfix, priority Last) so the server is listed as Community, not Modded. This plugin is the exception that *does* set GameTags; other mods leave tags to the server. |

## Project Structure

| File | Responsibility |
|------|----------------|
| `ServerMgr_UpdateServerInformation.cs` | Prefix: strip `modded` from `ConVar.Server._tags`. Postfix (Priority.Last): strip `modded` from `SteamServer.GameTags` so the server appears in Community tab. |
| `CommunityTab.csproj` | net48, refs: 0Harmony, Assembly-CSharp, UnityEngine.CoreModule |
| `build.ps1` | Build and copy DLL to `D:\!RustServer\HarmonyMods\CommunityTab.dll` |

## Harmony Patch

| Patch | Target | Type | Purpose |
|-------|--------|------|---------|
| `ServerMgr_UpdateServerInformation` | `ServerMgr.UpdateServerInformation` | Prefix | Remove `modded` from `ConVar.Server.tags` (via backing field `_tags`) so the built tags don’t include it. |
| `ServerMgr_UpdateServerInformation` | `ServerMgr.UpdateServerInformation` | Postfix (Last) | Remove `modded` from `SteamServer.GameTags` after the method runs so the server is listed in Community, not Modded. This plugin is intended to change GameTags; others leave tags to the server. |

## How the game chooses Community vs Modded

- `ServerMgr.UpdateServerInformation()` runs periodically (~every 30s). It reads `ConVar.Server.tags`, builds a compressed tag string, and sets `SteamServer.GameTags`.
- If that string includes the `modded` tag, Steam lists the server in the **Modded** tab; if not, it lists in the **Community** tab.
- Other Harmony mods (e.g. BagCooldowns, CraftingSpeed, MixingSpeed) add `modded` so their servers show as modded. This mod does the opposite: it strips `modded` so the server shows as Community.

## Lifecycle

- No `IHarmonyModHooks`; patch applies on load. No config, no commands.

## Build and deploy

1. From the mod folder: `.\build.ps1`
2. Restart the server or run `harmony.load CommunityTab` (if your loader supports it).
3. DLL must be in the server’s `HarmonyMods/` directory (e.g. `D:\!RustServer\HarmonyMods\CommunityTab.dll`).

## What NOT to touch

- **Patch order:** Prefix runs first so the original method sees tags already stripped. Postfix runs last (Priority.Last) so we strip `modded` from `SteamServer.GameTags` after any other mods that may have added it—this is the intended way this plugin changes GameTags to "not modded".
- **ConVar.Server._tags:** Setting the backing field avoids `AutoCorrectTags` in the setter, which could alter other tags. If the game renames or removes `_tags`, the reflection lookup will fail and the mod will log a warning and no-op.
