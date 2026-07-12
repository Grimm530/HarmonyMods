# Rustcord Harmony Mod

Harmony mod version of Rustcord – game server monitoring through Discord. **No Oxide required.**

## Features

- **Game->Discord** via Discord bot (API Key + channel IDs) or webhooks
- **Relay-compatible format**: `:speech_left: SVR1 PlayerName: message` for ticket-support-system-discord
- **Auto-generates config** if missing: creates `HarmonyConfig/Rustcord.json` on first load
- Loads from `HarmonyConfig/Rustcord.json` (then `oxide/config/Rustcord.json` as fallback)
- Uses same config structure as Oxide Rustcord (channel IDs, no webhooks required)

## Setup

1. Place `Rustcord.dll` in `HarmonyMods/` and load with `harmony.load Rustcord` (or automatic at startup).
2. On first run, a default config is created at `HarmonyConfig/Rustcord.json` (if none exists).
3. **Add your Discord bot token** to `"API Key (Bot Token)"` in General Settings.
4. **Channel IDs** in `Discord Logging Channels` – the bot posts to these channels. No webhooks needed.
5. Optional: use **Webhook URL** or **Webhooks** dict if you prefer webhooks instead of the bot.

## Config

Loads from `HarmonyConfig/Rustcord.json` or `oxide/config/Rustcord.json`. Use your existing Oxide Rustcord config as a base and add webhooks:

```json
{
  "General Settings": {
    "Server Name (for multi-server: shown in Discord and cross-server chat, e.g. SVR1)": "TestServer"
  },
  "Discord to Game Settings": {
    "Enable Discord to Game (chat & commands from Discord into game)": false
  },
  "Rust Logging Settings": {
    "Enable Logging: Player Chat": true,
    "Enable Logging: Joins & Quits": true,
    "Enable Logging: Deaths": false,
    "Enable Logging: Crate Drops (Hackable/Supply)": false
  },
  "Webhooks (Channel ID -> URL, for Harmony mod without bot)": {
    "1319693785361420288": "https://discord.com/api/webhooks/.../...",
    "1320013624982638723": "https://discord.com/api/webhooks/.../..."
  },
  "Discord Logging Channels": [
    {
      "Discord Channel ID #": 1319693785361420288,
      "Channel Flags": ["msg_chat", "msg_join", "msg_quit"],
      "Webhook URL": "https://discord.com/api/webhooks/.../..."
    }
  ]
}
```

- **Webhooks** dict: Maps channel ID (string) → webhook URL. Used when posting.
- **ChannelConfig.WebhookUrl**: Alternative – webhook per channel in the Channels array.
- **Channel Flags** (perms): `msg_chat`, `msg_teamchat`, `msg_join`, `msg_quit`, `death_pvp`, `log_cratedrop`, `log_supplydrop`, etc.

## Relay (ticket-support-system-discord)

Messages are sent in the format `:speech_left: SVR1 PlayerName: message`. The relay bot picks them up and forwards to other servers. Set **Server Name** in config for multi-server relay.

**Chat.Broadcast patch:** When the bot sends relay messages via RCON `say`, the game normally shows "SERVER" as the prefix. This mod includes a patch that hides "SERVER" for relay messages (SVR1, SVR2, [Discord], etc.) so they appear as just the blue tag + content. Install Rustcord on all servers that receive relay for the clean format.

The ticket-support-system has multiple integration points:
- **Relay** (Rustcord): Game chat → Discord channels, cross-server relay
- **MapVoter bridge**: Map vote notifications (vote_started, vote_ended) – see [MAPVOTER-BRIDGE.md](../../ticket-support-system-discord/docs/MAPVOTER-BRIDGE.md)

## Discord to Game

- **Enable Discord to Game**: Config option exists for parity with Oxide Rustcord. Set to `false` (default) – the Harmony mod does **not** implement Discord→Game; the ticket-support-system bot handles Discord chat via RCON with the correct format. Copy config from Oxide and this will be respected.

## Limitations (vs Oxide Rustcord)

- **Webhook-only**: No Discord bot gateway; no Discord->Game (commands, presence). Discord user messages are handled by the relay bot via RCON.
- No optional plugin hooks (Clans, AdminHammer, etc.).
- No translation – add TranslationAPI integration later if needed.
