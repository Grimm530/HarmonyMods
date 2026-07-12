# FurnaceSplitter Harmony Mod

**Standalone** furnace item splitting and auto-fuel. Patches the game directly—no Oxide plugin required.

## Features

- **Split cookables** across furnace/oven input slots evenly
- **Auto fuel** – when you transfer items into a furnace, fuel is taken from your inventory automatically
- **Config** – `HarmonyConfig/FurnaceSplitter.json` (created on first load)

## Architecture

| Component | Role |
|-----------|------|
| **Item.MoveToContainer** patch | Intercepts cookable moves to ovens; runs split logic and auto fuel |
| **ItemContainer.Insert** patch | Auto fuel when default game logic adds cookables (e.g. different ore types) |
| **FurnaceSplitterConfig** | Loads `HarmonyConfig/FurnaceSplitter.json` |

## Config

```json
{
  "debug": false,
  "ovens": {
    "*": { "enabled": true, "autoFuelTransfer": true },
    "furnace": { "enabled": true, "autoFuelTransfer": true },
    "campfire": { "enabled": true, "autoFuelTransfer": false }
  }
}
```

- **debug** – when `true`, logs to server console when items move to ovens: which patch runs, split amounts, fuel needed/transferred, and why splits are skipped (e.g. mixed ore types, temp mismatch)
- **\*** – default for all ovens
- **enabled** – turn off split + fuel for specific oven types
- **autoFuelTransfer** – when true, transfer the correct amount of fuel from your inventory into the cooker when you add resources

## Build

```powershell
.\build.ps1
```

Output: `HarmonyMods/FurnaceSplitter.dll`

## Oxide Plugin

Can run alongside the FurnaceSplitter Oxide plugin for UI, permissions, and `/fs`. Both can coexist—the Harmony mod handles split + fuel at the game level; the plugin adds the overlay.
