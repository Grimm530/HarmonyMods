# PrivateMessages (Harmony)

Oxide **PrivateMessages 1.1.12** port. Send private messages with reply and history.

## Load order

1. **0Permissions**
2. **PrivateMessages**

## Deploy

```powershell
.\build.ps1
```

Copies **only** `PrivateMessages.dll` to `HarmonyMods\PrivateMessages.dll`.

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/PrivateMessages.json` |
| Lang | `HarmonyLanguage/PrivateMessages.json` |

## Commands

`/pm`, `/send`, `/msg`, `/tell`, `/r`, `/reply`, `/pmhistory` (plus config `PmCommand`)

## Permissions

| Permission | Effect |
|------------|--------|
| `privatemessages.allow` | Required when `UsePermission` is true |
| `privatemessages.block` | Player cannot receive PMs |

Ignore / UFilter / BetterChatMute / GTFO plugin refs are no-ops. Native `ChatMute` is still honored. BetterChat mute is checked via AppDomain `BetterChatMute_ApiType` when present.
