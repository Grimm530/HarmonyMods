# LoadingMessages (Harmony port)

Harmony mod port of **LoadingMessages 1.1.3** (CosaNostra/Def/klauz24). Shows custom texts on the loading screen. Behavior matches the Harmony mod as closely as possible.

## Identity

| Field | Value |
|-------|--------|
| **Name** | LoadingMessages |
| **Source** | `.cursor/Harmony.Plugins.Cant-Use/LoadingMessages.cs` |
| **Type** | Harmony mod (`IHarmonyModHooks`) |
| **Config** | `HarmonyConfig/LoadingMessages.json` (migrates from `legacy/config/LoadingMessages.json` if present) |

## What changed vs original LoadingMessages (only Harmony necessities)

- `RustPlugin` → `LoadingMessagesMod : IHarmonyModHooks` (`OnLoaded` / `OnUnloaded`)
- Config under `HarmonyConfig/LoadingMessages.json` instead of `legacy/config`
- Harmony hooks → Harmony patches:
  - `OnUserApprove` → `ConnectionAuth.OnNewConnection` Postfix (compat injects `IOnUserApprove` here, not `Approve`)
  - `OnPlayerConnected` → `BasePlayer.PlayerInit` Postfix
- `timer.Every` → coroutine timer on a DontDestroyOnLoad runner
- `Puts` / `PrintWarning` / `PrintError` → `UnityEngine.Debug` logs
- `connectionQueue.nextMessageTime` / `queue` accessed via reflection (private fields)

**Unchanged:** Message cycling, queue messages, last message, `{PLAYERNAME}` replacement, `Message.Type.Message` packets, config schema.

## Project structure

| File | Content |
|------|--------|
| `LoadingMessages/LoadingMessagesMod.cs` | Entry + config + original plugin logic |
| `LoadingMessages/Patches/ConnectionAuth_OnNewConnection_Patch.cs` | OnUserApprove (`OnNewConnection`) |
| `LoadingMessages/Patches/BasePlayer_PlayerInit_Patch.cs` | OnPlayerConnected |

## Build / deploy

```powershell
.\build.ps1
```

Copies `LoadingMessages.dll` only into server `HarmonyMods/`.

Load: `harmony.load LoadingMessages` (or restart the server).

## Config

Use the existing file at **`HarmonyConfig/LoadingMessages.json`**. Keys match the Harmony mod (`Cycle Messages Every ~N Seconds`, `Messages`, `Last Message (When entering game)`, etc.).
