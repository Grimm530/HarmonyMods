# BetterChat Harmony Mod

Oxide **BetterChat** (groups, titles, formats) and **ColouredChat** (per-player name/message colours) ported into one Harmony mod. **No Oxide required.**

They share one chat pipeline: ColouredChat already mutated BetterChat’s `OnBetterChat` dictionary. Combining them avoids two `Chat.sayImpl` blockers and keeps titles + colours on the same formatted line.

## Identity

| Item | Value |
|------|-------|
| **DLL** | `BetterChat.dll` |
| **Entry** | `BetterChatHarmony.BetterChatMod` (`IHarmonyModHooks`) |
| **Config** | `HarmonyConfig/BetterChat.json` |
| **Colour data** | `HarmonyData/BetterChat/Colours.json` |
| **API** | AppDomain key `BetterChat_ApiType` |

## Config

`HarmonyConfig/BetterChat.json` used to be a **raw group array** (0Permissions still accepts that). On first load this mod wraps it:

```json
{
  "Maximal Titles": 2,
  "Maximal Characters Per Message": 128,
  "Reverse Title Order": false,
  "Coloured Chat": { },
  "Groups": [ { "GroupName": "default", "Priority": 4, "Title": { }, "Username": { }, "Message": { }, "Format": { } } ]
}
```

Oxide `oxide/config/BetterChat.json` and `oxide/config/ColouredChat.json` are merged once if present. Unload the Oxide plugins after this mod is loaded so they do not double-send chat.

## Commands

| Command | Permission | Purpose |
|---------|------------|---------|
| `/chat group add\|remove\|set\|list` | `betterchat.admin` (or game admin) | Manage title groups |
| `/chat user add\|remove` | `betterchat.admin` | Add/remove a player from a group |
| `/colour` `/color` `<#hex\|random\|rainbow\|gradient\|clear>` | `colouredchat.name.use` (+ extras) | Name colour |
| `/colours` `/colors` | `colouredchat.name.use` | Name colour help |
| `/mcolour` `/mcolor` | `colouredchat.message.use` | Message colour |
| `/mcolours` `/mcolors` | `colouredchat.message.use` | Message colour help |
| `betterchat` (RCON / F1) | server admin | Same as `/chat` |

Colour extras: `.gradient`, `.rainbow`, `.random`, `.setothers`, `.bypass`, `.show` (must be granted to *see* the colour in chat). Admins pass these checks.

Group membership still lives in **0Permissions** (`perm usergroup add …`). BetterChat only stores title/colour *style* per group name. 0Permissions publishes a membership snapshot (`Permissions_UserGroupsCsv`) and Funcs that BetterChat reads on every chat line. AdminMenu is not in this path — it already writes through 0Permissions.

Debug: `betterchat who 76561197967147516` prints the link status, 0Permissions groups, and titles that would show.

## Chat pipeline

| Order | Mod | `sayImpl` | Role |
|-------|-----|-----------|------|
| 1 | ChatFilter | Prefix `Priority.First` | Filter words, return true |
| 2 | **BetterChat** | Prefix `Priority.High` | Titles + colours, `chat.add`, skip original |
| 3 | ChatTranslator | Prefix Normal | **Skipped** while BetterChat is loaded (avoids double chat) |
| 4 | Rustcord | Postfix | Discord relay still runs |

`Chat.say` commands go through shared `HarmonyChat.ChatSayBridge` (same as Shop / SkillTree).

## SkillTree titles

SkillTree registers an `OnBetterChat` modifier and appends `[Lv.N]` / prestige titles. Grant `skilltree.notitles` to hide them per player.

## Harmony patches

| Patch | Target | Type |
|-------|--------|------|
| `Chat_Say_Patch` | `Chat.say` | Prefix — `ChatSayBridge.Dispatch` |
| `Chat_sayImpl_Patch` | `Chat.sayImpl(ChatChannel, Arg)` | Prefix High — format and send |

## API (other Harmony mods)

```csharp
AppDomain.CurrentDomain.GetData("BetterChat_ApiType")  // Type = BetterChatHarmony.BetterChatMod
BetterChatMod.RegisterOnBetterChat(Func<Dictionary<string, object>, object>)
BetterChatMod.RegisterThirdPartyTitle(string pluginName, Func<BasePlayer, string> getter)
BetterChatMod.API_GetFormattedMessage(BasePlayer, string, bool console)
```

Dictionary keys match Oxide BetterChat (`Player` is `BasePlayer`, not `IPlayer`).

## Build

```powershell
cd .cursor\HarmonyMods\BetterChat
.\build.ps1
```

Load: `harmony.load BetterChat`. Prefer **0Permissions** loaded first (this mod rebinds via ready callback).
