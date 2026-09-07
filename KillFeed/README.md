# KillFeed (Harmony)

**KillFeed 2.2.2** port as a standalone Harmony mod (Harmony-only runtime).

Death feed CUI + admin editor, Discord live killfeed, team/clan/friend kill filters, and custom bot icon overrides (NpcSpawn/BetterNpc-style names).

## Load order

1. **0Permissions** (`0Permissions.dll`)
2. **KillFeed** (`KillFeed.dll`)

Optional: **Friends** / **Clans** Oxide plugins for friend-kill hiding and third-party clan detection (Rust native team/clan work without them).

## Deploy

```powershell
.\build.ps1
```

Copies **only** `KillFeed.dll` to `HarmonyMods\KillFeed.dll`.

Load: `harmony.load KillFeed` (or automatic at startup).

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/KillFeed.json` |
| Data | `HarmonyData/KillFeed/` |
| Lang | `HarmonyLanguage/KillFeed/{lang}.json` |
| Images | `HarmonyImages/KillFeed/` (fallback for `HarmonyData/KillFeed/Images/`) |

## What's in 2.2.x

- **2.2.0** Discord webhook killfeed (batched embeds/plain text), nav scroll fix
- **2.2.1** Hide team/clan/friend kills; suicide Discord fix; bot given names in feed
- **2.2.2** Custom Images tab; icons resolve from NPC prefab type (not display name)

## CUI

Button commands are rewritten to `cui.endtest KILLFEED …` and routed by this mod's `cui.endtest` prefix (returns true for other markers).

## Chat

Commands go through `ConVar.Chat.say` + shared `ChatSayBridge`.
