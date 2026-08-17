# ChatIcons – Harmony Mod

Standalone Harmony mod that sets a customizable Steam avatar icon for all **non-user** chat messages (server/system messages). Replaces the Oxide CustomIcon plugin.

**No Oxide plugin required.** Loaded by HarmonyLoader from `HarmonyMods/`.

## Config

`HarmonyConfig/ChatIcons.json`:

```json
{
  "Steam Avatar User ID": 0,
  "Replace MOTD icon": true
}
```

| Key | Effect |
|-----|--------|
| `Steam Avatar User ID` | Steam64 ID whose avatar is used for chat messages with userId 0 (server, plugins, system). `0` = leave the default icon. |
| `Replace MOTD icon` | When true (and a Steam ID is set), hide the client-drawn `server.motd` (default Rust gear icon) and re-send the same text as `chat.add` with the Steam avatar. Keep `server.motd` as the message text. |

`server.motd` is a replicated convar. The client paints it locally with the default Rust icon; that path never goes through `chat.add`, so ChatIcons cannot retarget the sprite itself. Replacement is: suppress the replicated string to clients, then send the MOTD as a normal server chat line with the configured avatar.

## Loading

- **Automatic** at server startup from `HarmonyMods/ChatIcons.dll`
- **Manual**: `harmony.load ChatIcons`

## Building

```powershell
cd .cursor\HarmonyMods\ChatIcons
.\build.ps1
```

DLL is copied to this server's `HarmonyMods/ChatIcons.dll`.

## Related mods

Often used with **Rustcord** (Discord chat relay) and **MapVoter** (map vote notifications to Discord). For MapVoter ↔ Discord setup, see [MAPVOTER-BRIDGE.md](../../ticket-support-system-discord/docs/MAPVOTER-BRIDGE.md).
