# ChatFilter

Harmony mod that filters inappropriate language in chat. Replaces or blocks bad words, supports whitelist and leet-speak decoding. Optional offense tracking with mute/kick/ban. **No Oxide required.**

## Mod Identity

| Item | Value |
|------|-------|
| **Purpose** | Word filter for global/team chat with replacement, whitelist, offenses |
| **Entry point** | `ChatFilterMod` implements `IHarmonyModHooks` |
| **Config** | `HarmonyConfig/ChatFilter.json` (or `oxide/config/ChatFilter.json`) |
| **Data** | `HarmonyData/ChatFilter_Offenses.json` (offense counts per Steam ID) |

## Project Structure

| File | Responsibility |
|------|----------------|
| `ChatFilterMod.cs` | Lifecycle, filter logic, leet table, whitelist check, offense recording, mute/kick/ban |
| `ChatFilterConfig.cs` | JSON config load/save, default word list and options |
| `Patches/Chat_sayImpl_Patch.cs` | Prefix on `Chat.sayImpl`: read message, exclude checks, filter, replace `arg.Args[0]` or cancel |

## Configuration (Condensed)

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| Word Filter - Enabled | bool | true | Apply word filter |
| Word Filter List | list string | bitch, cunt, nigger, nig, faggot, fuck | Phrases to replace/block |
| Word Filter - Allow Partial Match In Words (legacy aggressive mode) | bool | false | If true, allows substring matches inside larger words (e.g. `basement` matching `semen`) |
| Word To White List | list string | night | Never filter these (e.g. "night" vs "nig") |
| Word Filter - Replacement | string | * | Character(s) to replace each character of bad word |
| Word Filter - Use Custom Replacement | bool | false | If true, use Custom Replacement for whole word |
| Word Filter - Custom Replacement | string | Unicorn | Replacement when Use Custom is true |
| Whole Message Filter - Enabled | bool | false | If true, clear entire message on any match |
| Block Special Characters in Chat | bool | false | Block messages that contain non-allowed characters |
| Exclude Team Chat | bool | false | Skip filter for team channel |
| Exclude admins | bool | true | Don’t filter admin/developer |
| Exclude Steam IDs | list string | [] | Steam IDs that are never filtered |
| Offenses - Count To Mute/Kick/Ban | int | 3, 3, 20 | Thresholds (0 = disabled) |
| Offenses - Time To Mute | int | 300 | Mute duration in seconds |
| Time to Ban (minutes) | int | 30 | 0 = permanent |
| Offenses - Broadcast kick/ban | bool | true | Announce to chat |
| Clear Offense After | int | 0 | 0=no clear, 1=all, 2=kick, 3=mute, 4=ban |

## Chat pipeline (ChatFilter → ChatTranslator → Rustcord)

ChatFilter is designed to run **first** so that anything downstream sees only filtered text:

1. **ChatFilter** (Prefix, `Priority.First`) – Runs first. Reads the message, removes bad words, writes the cleaned message back into the same `Arg`. Returns true so the rest of the pipeline runs.
2. **BetterChat** (Prefix, `Priority.High`) – Titles, group colours, and ColouredChat name/message colours. Sends `chat.add` and skips the original `sayImpl`.
3. **ChatTranslator** – Skipped while BetterChat is loaded (BetterChat already sent the line). Without BetterChat, translates per recipient.
4. **Rustcord** (Postfix) – Reads the message from the Arg (filtered) and sends it to Discord / other servers. So relayed chat is always clean.

**Load order:** Load ChatFilter before ChatTranslator and Rustcord when possible (e.g. `harmony.load ChatFilter` then the others). The patch uses `[HarmonyPriority(Priority.First)]` so it runs first regardless, but correct load order avoids edge cases.

## Harmony Patches

| Patch | Target | Type | Purpose |
|-------|--------|------|---------|
| `Chat_sayImpl_Patch` | `Chat.sayImpl(ChatChannel, Arg)` | Prefix (Priority.First) | Get message from arg; exclude team/admins/SteamIds; block special chars if enabled; filter words; if full block or empty after filter return false and send "Your message was blocked"; if had match mutate Arg so first argument is filtered text, record offense, return true so ChatTranslator/Rustcord/original see filtered message |

## Behavior Summary

- **Filter:** Bad words are matched after leet decoding (e.g. 4→a, 0→o). By default, exact normalized whole-word matches are filtered; optional legacy partial-in-word matching can be enabled in config. Whitelist words (e.g. "night") are never filtered. Matched words are replaced by config (asterisks or custom string) or whole message cleared if Whole Message Filter is on.
- **Offenses:** Each message that contained a filtered word increments that player’s offense count. At Mute/Kick/Ban thresholds the mod applies mute (game `ChatMute` flag + timer), kick, or ban (via `ServerUsers.Set` + `ServerUsers.Save` + `player.Kick`). Temp ban uses `Facepunch.Math.Epoch.Current + (BanTimeMin * 60)` and a timer to `ServerUsers.Remove` + `ServerUsers.Save`.
- **Excludes:** Team chat can be excluded; admins/developers and listed Steam IDs are excluded from filtering. If the **DeveloperListOverride** mod is loaded, players in its config are also treated as developers for exclusion (so they keep orange name in chat and aren't filtered).

## What NOT to Touch

- **Patch target:** `Chat.sayImpl` signature (channel, Arg) may change with game version.
- **Arg.Args:** Mod assumes first argument can be replaced (`arg.Args[0] = filtered`); if Arg implementation changes to return a copy, replace via reflection or another patch point.

## Build & Deploy

```powershell
cd .cursor\HarmonyMods\ChatFilter
.\build.ps1
```

Output: `D:\!RustServer\HarmonyMods\ChatFilter.dll`. Load: `harmony.load ChatFilter`. Config path: `HarmonyConfig/ChatFilter.json`.
