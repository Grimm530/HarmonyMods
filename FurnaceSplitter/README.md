# FurnaceSplitter Harmony Mod

**Standalone** furnace/composter item splitting and auto-fuel. Patches the game directly.

## Features

- **Split cookables** across furnace/oven input slots evenly
- **Split compostables** across composter input slots (post-QoL composters are `BaseOven`)
- **Auto fuel** – when you transfer items into a furnace, fuel is taken from your inventory automatically (skipped for composters)
- **Config** – `HarmonyConfig/FurnaceSplitter.json` (created on first load)

## Architecture

| Component | Role |
|-----------|------|
| **Item.MoveToContainer** patch | Intercepts cookable/compostable moves to ovens; runs split logic and auto fuel |
| **ItemContainer.Insert** patch | Auto fuel when default game logic adds cookables (e.g. different ore types) |
| **FurnaceSplitterConfig** | Loads `HarmonyConfig/FurnaceSplitter.json` |

## Config

```json
{
  "debug": false,
  "ovens": {
    "*": { "enabled": true, "autoFuelTransfer": true },
    "composter": { "enabled": true, "autoFuelTransfer": false },
    "furnace": { "enabled": true, "autoFuelTransfer": true },
    "campfire": { "enabled": true, "autoFuelTransfer": false }
  }
}
```

- **debug** – when `true`, logs to server console when items move to ovens: which patch runs, split amounts, fuel needed/transferred, and why splits are skipped
- **\*** – default for all ovens
- **composter** – enabled for split; auto-fuel off (composters run with no fuel)
- **enabled** – turn off split + fuel for specific oven types
- **autoFuelTransfer** – when true, transfer the correct amount of fuel from your inventory into the cooker when you add resources

## Build

```powershell
.\build.ps1
```

Output: `HarmonyMods/FurnaceSplitter.dll`
