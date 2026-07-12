# Harmony Mod Migration Map: TranslationAPI, ChatTranslator, Rustcord

Migration plan for converting three Oxide plugins to standalone Harmony mods (no Oxide). Reference for connecting them and working with the external Discord bot.

---

## 1. External Discord Bot (ticket-support-system-discord)

| Path | Purpose |
|------|---------|
| `D:\!RustServer\.cursor\ticket-support-system-discord\` | Full Discord bot (tickets, linking, etc.) |
| `utils/rustcordRelay.js` | **Relay layer**: Bot messages from Rustcord → RCON to other Rust servers; Discord user messages → RCON to ALL servers as `[Discord] Name: message` |
| `config/config.json` → `rustcord_relay` | `{ enabled, channelId, servers: [{ name, host, port, password }] }` |

**Integration**: The Discord bot listens for messages in the relay channel. When **Rustcord** (game server) posts to Discord (e.g. `SVR1 Grimm530: hello`), the bot relays that to OTHER Rust servers via WebSocket RCON `say "..."`. Discord USER messages are sent to all servers by the bot as `[Discord] DisplayName: message` — Rustcord Harmony mod does NOT need to handle Discord→Game for relay; the bot does it. Rustcord Harmony mod only needs to send Game→Discord.

- [ ] **No changes needed** to ticket-support-system-discord for Harmony migration (relay stays the same)
- [ ] Rustcord Harmony mod must POST to Discord in the same format (server-tagged messages) so relay works

---

## 2. TranslationAPI Harmony Mod

| Task | Status |
|------|--------|
| Core translation logic (Google/Microsoft/Yandex) | [x] Done |
| Config: `HarmonyConfig/TranslationAPI.json` (created if missing) | [x] Done |
| Static API: `TranslationAPIMod.Translate(text, to, from, callback)` | [x] Done |
| No Oxide dependency | [x] Done |
| Remove Oxide bridge plugin (server has no Oxide) | [x] Done – deleted `oxide/plugins/TranslationAPI.cs` |

**Usage by other mods**:
```csharp
TranslationAPIMod.Translate(message, "en", "auto", translated => { /* use translated */ });
```

---

## 3. ChatTranslator Harmony Mod

### Current Oxide Behavior
- Hooks `OnPlayerChat` (or `OnBetterChat` if BetterChat loaded)
- For each receiver: gets `lang.GetLanguage(targetId)` and `lang.GetLanguage(senderId)`
- Calls `TranslationAPI.Call("Translate", message, langTo, langFrom, callback)`
- Sends translated message via `target.Command("chat.add", ...)` or team/CompanionServer

### Harmony Migration Tasks

| Task | Status |
|------|--------|
| **Config** – ForceServerDefault, SkipSameLanguage, etc. | [x] Done |
| **Language storage** – Replace `lang.GetLanguage(id)` with custom storage (JSON/SQLite or in-memory) | [x] Done – `HarmonyConfig/ChatTranslator_languages.json` |
| **Chat interception** – Harmony patch on `ConVar.Chat` or `ConsoleNetwork` chat path | [x] Done – Prefix on `Chat.sayImpl` |
| **Translation call** – Use `TranslationAPIMod.Translate()` directly | [x] Done |
| **Message delivery** – `ConsoleNetwork.SendClientCommand` or `Player.Message` / game equivalents | [x] Done – `chat.add2`, `SendClientCommand` |
| **Optional**: BetterChat/BetterChatMute/ChatFilter integration (skip if not used) | [ ] N/A (standalone) |

### Game Patch Targets
- `ConVar.Chat` – main chat handling; find method that processes player chat before broadcast
- `ConsoleNetwork.BroadcastToAllClients("chat.add2", ...)` / `chat.add` – where messages are sent
- `RelationshipManager.PlayerTeam.BroadcastTeamChat` – team chat
- `CompanionServer.Server.TeamChat.Record` – Rust+ team chat

---

## 4. Rustcord Harmony Mod

### Current Oxide Dependencies
- `Oxide.Ext.Discord` – Discord bot (Connect, GetChannel, CreateMessage, UpdateStatus)
- `Oxide.Core.Plugins` – PluginReference for TranslationAPI, ChatTranslator, Clans, etc.
- ~60 Oxide hooks (OnPlayerChat, OnPlayerConnected, OnCrateDropped, etc.)

### Discord Replacement
Game has **no** server-side Discord API. Options:
- **Discord.Net** or **DSharpPlus** – .NET Discord library (add as NuGet/assembly reference)
- **Discord webhooks only** – HTTP POST to `discord.com/api/webhooks/{id}/{token}` for one-way logging (no bot presence, no Discord→Game commands)

For full Rustcord feature parity (bot presence, Discord→Game commands, multi-channel): use Discord.Net or DSharpPlus.

### Harmony Migration Tasks

#### 4.1 Core & Discord

| Task | Status |
|------|--------|
| **Discord webhooks** – HTTP POST for one-way Game→Discord | [x] Done |
| **Config** – General, Post to Discord, Channels, Webhooks (slim structure) | [x] Done |
| **Bot connection** – (deferred) Webhook mode only for now | [ ] N/A |
| **GetChannel / CreateMessage** – Replaced with PostToWebhook | [x] Done |
| **UpdateStatus (presence)** – Not in webhook mode | [ ] N/A |

#### 4.2 Game→Discord (Hooks → Harmony Patches)

| Oxide Hook | Game Patch Target | Status |
|------------|-------------------|--------|
| `OnPlayerChat` | `Chat.sayImpl` Postfix | [x] Done |
| `OnPlayerConnected` | `BasePlayer.PlayerInit` Postfix | [x] Done |
| `OnPlayerDisconnected` | `ServerMgr.OnDisconnected` Postfix | [x] Done |
| `OnCrateDropped` | `HackableLockedCrate.SetWasDropped` Postfix | [x] Done |
| `OnSupplyDropLanded` | `SupplyDrop.OnCollisionEnter` Postfix (land check) | [x] Done |
| `OnPlayerDeath` | `BaseCombatEntity.Die` Postfix | [x] Done |
| `OnPlayerReported` | Report flow (if game exposes it) | [ ] |
| `OnTeamCreated` / `OnTeamLeave` / etc. | `RelationshipManager` team methods | [ ] |
| `OnEntitySpawned` | `BaseNetworkable.ServerInit` or spawn hooks | [ ] |
| `OnServerMessage` | Console/server broadcast path | [ ] |
| `OnNewSave` | SaveRestore save path | [ ] |
| Event hooks (Air, Harbor, Convoy, etc.) | Plugin-specific – optional, patch if needed | [ ] |

#### 4.3 Discord→Game

| Task | Status |
|------|--------|
| **OnDiscordGuildMessageCreated** – Discord user messages → in-game chat | [ ] |
| **Bot/webhook messages** – Forward relay messages (server-tagged) to in-game | [ ] |
| **Discord commands** (!kick, !broadcast, etc.) → `DiscordToGameCmd` | [ ] |
| **Skip own bot** – Avoid echo when our bot posts | [ ] |

#### 4.4 TranslationAPI Integration

| Task | Status |
|------|--------|
| **Chat→Discord**: Before sending to Discord, call `TranslationAPIMod.Translate(msg, "en", "auto", cb)` | [ ] |
| **Only when ChatTranslator not active** – Rustcord uses TranslationAPI when ChatTranslator not loaded | [ ] |

#### 4.5 Optional Plugin Hooks (Lower Priority)
- Clans, AdminChat, AdminHammer, AdminRadar, Kits, Vanish, RaidableBases, DangerousTreasures, NoGiveNotices, Give, AirEvent, HarborEvent, JunkyardEvent, PowerPlantEvent
- Each requires finding game equivalent or implementing stub

#### 4.6 Config Structure (Slim – No Oxide Dependencies)

| Oxide Section | Kept? | Notes |
|---------------|-------|-------|
| General Settings (1–10) | ✓ | API Key, Server Name, Auto Reload, Bot Status, Report Command, Log Level |
| Discord to Game (11–19) | ✓ | Prefix, Discord Chat icon/tag/colors (for when bot relays user messages) |
| Rust/Plugin/Premium Logging (20–64) | ✗ | Removed – logged elsewhere; mod uses minimal "Post to Discord" |
| Post to Discord | ✓ | Slim: Player Chat, Joins & Quits, Deaths, Crate Drops (what mod can send) |
| Discord Output Formatting (65–77) | ✓ | Simple/Embed for Bans, Deaths, Join/Quit, Kicks, Teams, etc. |
| Output Type (Plugin/Premium) (78–99) | ✗ | Removed – no Oxide plugin hooks |
| Logging Exclusions (100–109) | ✗ | Removed |
| Filter Settings | ✓ | Chat filter words, replacement |
| Discord Logging Channels | ✓ | Without msg_private (not used in Harmony) |
| Discord Command Role Assignment | ✓ | For future bot-mode commands |
| Webhooks | ✓ | Channel ID → URL (required for Harmony webhook mode) |

**Config file**: `HarmonyConfig/Rustcord.json`. Add webhook URLs per channel for Game→Discord posting.

---

## 5. Connection Diagram (Target State)

```
┌─────────────────────────────────────────────────────────────────────┐
│  Rust Dedicated Server (no Oxide)                                    │
│                                                                      │
│  ┌──────────────┐     ┌──────────────────┐     ┌────────────────┐ │
│  │ ChatTranslator│────▶│  TranslationAPI   │     │    Rustcord     │ │
│  │ (Harmony)    │     │  (Harmony)       │◀────│  (Harmony)      │ │
│  └──────────────┘     └──────────────────┘     └────────┬────────┘ │
│         │                        ▲                      │          │
│         │                        │                      │          │
│         ▼                        │                      ▼          │
│  Chat patch ─────────────────────┘              Discord API        │
│  (ConVar.Chat)                                            │        │
└────────────────────────────────────────────────────────────┴────────┘
                                                             │
                                                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│  Discord (Webhook/Bot)                                              │
│  Rustcord posts: "SVR1 PlayerName: message"                         │
└─────────────────────────────────────────────────────────────────────┘
                                                             │
                                                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│  ticket-support-system-discord (external bot)                        │
│  rustcordRelay.js: Bot messages → RCON to OTHER servers              │
│                    Discord user messages → RCON to ALL servers       │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 6. File Locations

| Component | Source | Output |
|-----------|--------|--------|
| TranslationAPI | `.cursor/HarmonyMods/TranslationAPI/` | `HarmonyMods/TranslationAPI.dll` |
| ChatTranslator | `.cursor/HarmonyMods/ChatTranslator/` | `HarmonyMods/ChatTranslator.dll` |
| Rustcord | `.cursor/HarmonyMods/Rustcord/` | `HarmonyMods/Rustcord.dll` |
| Configs | `HarmonyConfig/*.json` | |

---

## 7. Implementation Order

1. [x] TranslationAPI – done
2. [x] ChatTranslator – language storage, chat patch, TranslationAPI.Translate, /lang command
3. [x] Rustcord – Webhook mode, config, Game→Discord patches (Chat, Join/Quit, Death, Crate/Supply)
4. [ ] Rustcord – Discord→Game (relay bot handles via RCON; optional full bot later)
5. [ ] Integration testing with ticket-support-system-discord relay

---

*Last updated: Migration planning. Check off items as completed.*
