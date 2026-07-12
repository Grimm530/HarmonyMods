# LoadingMessages (Harmony port)

Harmony mod port of **LoadingMessages 1.1.3** (CosaNostra/Def/klauz24). Shows custom texts on the loading screen. Behavior matches the Oxide plugin as closely as possible.

## Identity

| Field | Value |
|-------|--------|
| **Name** | LoadingMessages |
| **Source** | `.cursor/Oxide.Plugins.Cant-Use/LoadingMessages.cs` |
| **Type** | Harmony mod (`IHarmonyModHooks`) |
| **Config** | `HarmonyConfig/LoadingMessages.json` (migrates from `oxide/config/LoadingMessages.json` if present) |

## What changed vs Oxide LoadingMessages (only Harmony necessities)

- `RustPlugin` → `LoadingMessagesMod : IHarmonyModHooks` (`OnLoaded` / `OnUnloaded`)
- Config under `HarmonyConfig/LoadingMessages.json` instead of `oxide/config`
- Oxide hooks → Harmony patches:
  - `OnUserApprove` → `ConnectionAuth.Approve` Postfix
  - `OnPlayerConnected` → `BasePlayer.PlayerInit` Postfix
- `timer.Every` → coroutine timer on a DontDestroyOnLoad runner
- `Puts` / `PrintWarning` / `PrintError` → `UnityEngine.Debug` logs
- `connectionQueue.nextMessageTime` / `queue` accessed via reflection (private fields)

**Unchanged:** Message cycling, queue messages, last message, `{PLAYERNAME}` replacement, `Message.Type.Message` packets, config schema.

## Project structure

| File | Content |
|------|--------|
| `LoadingMessages/LoadingMessagesMod.cs` | Entry + config + original plugin logic |
| `LoadingMessages/Patches/ConnectionAuth_Approve_Patch.cs` | OnUserApprove |
| `LoadingMessages/Patches/BasePlayer_PlayerInit_Patch.cs` | OnPlayerConnected |

## Build / deploy

```powershell
.\build.ps1
```

Copies `LoadingMessages.dll` only into server `HarmonyMods/`.

Load: `harmony.load LoadingMessages` (or restart the server).

## Config

Use the existing file at **`HarmonyConfig/LoadingMessages.json`**. Keys match the Oxide plugin (`Cycle Messages Every ~N Seconds`, `Messages`, `Last Message (When entering game)`, etc.).
