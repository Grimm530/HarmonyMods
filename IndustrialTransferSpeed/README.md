# IndustrialTransferSpeed Harmony Mod

**Standalone** Harmony mod for configurable industrial conveyor transfer speed. Patches the game directly—no Oxide plugin required.

Planter/composter industrial adaptor farming features were removed (they were broken). This mod only controls conveyor stack size per move.

## Features

- **MaxStackSizePerMove** - Control how many items conveyors transfer per move (vanilla: 128)
- **Config** - `HarmonyConfig/IndustrialTransferSpeed.json` (created on first load)
- **Clean unload** - Resets conveyors to vanilla (128) when mod is unloaded

## Architecture

| Component | Role |
|-----------|------|
| **BaseNetworkable.ServerInit** patch | Sets `MaxStackSizePerMove` when conveyors spawn (new or loaded) |
| **IndustrialConveyor.PostServerLoad** patch | Ensures value applied when conveyors load from save |
| **IndustrialTransferSpeedMod** | OnLoaded: apply to existing conveyors; OnUnloaded: reset to 128 |
| **IndustrialTransferSpeedConfig** | Loads `HarmonyConfig/IndustrialTransferSpeed.json` |

## Config

```json
{
  "MaxStackSizePerMove (van is 128)": 256
}
```

- **MaxStackSizePerMove** - Max stack amount conveyors can transfer per move (1-100000). Vanilla default: 128.

## Build

```powershell
.\build.ps1
```

Output: `HarmonyMods/IndustrialTransferSpeed.dll`

## Migration from Oxide Plugin

Replace the Oxide plugin `IndustrialTransferSpeed.cs` with this Harmony mod:
1. Build and copy `IndustrialTransferSpeed.dll` to `HarmonyMods/`
2. Remove/unload the Oxide plugin
3. Create or edit `HarmonyConfig/IndustrialTransferSpeed.json` with desired `MaxStackSizePerMove`
