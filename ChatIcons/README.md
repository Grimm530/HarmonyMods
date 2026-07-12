# ChatIcons – Harmony Mod

Standalone Harmony mod that sets a customizable Steam avatar icon for all **non-user** chat messages (server/system messages). Replaces the Oxide CustomIcon plugin.

**No Oxide plugin required.** Loaded by HarmonyLoader from `HarmonyMods/`.

## Config

`HarmonyConfig/ChatIcons.json`:

```json
{
  "Steam Avatar User ID": 0
}
```

Set `Steam Avatar User ID` to a Steam64 ID. Chat messages with userId 0 (server, plugins, system) will use this avatar instead of the default icon.

## Loading

- **Automatic** at server startup from `HarmonyMods/ChatIcons.dll`
- **Manual**: `harmony.load ChatIcons`

## Building

```powershell
cd .cursor\HarmonyMods\ChatIcons
.\build.ps1
```

DLL is copied to `D:\!RustServer\HarmonyMods\ChatIcons.dll`.

## Related mods

Often used with **Rustcord** (Discord chat relay) and **MapVoter** (map vote notifications to Discord). For MapVoter ↔ Discord setup, see [MAPVOTER-BRIDGE.md](../../ticket-support-system-discord/docs/MAPVOTER-BRIDGE.md).
