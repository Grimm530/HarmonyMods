# IndustrialTransferSpeed Harmony Mod

**Standalone** Harmony mod for configurable industrial conveyor transfer speed plus composter/planter industrial storage adaptor support. Patches the game directly—no Oxide plugin required. More performant than the Oxide equivalent (no constant entity iteration, no reflection hooks).

## Features

- **MaxStackSizePerMove** – Control how many items conveyors transfer per move (vanilla: 128)
- **Composter storage adaptor support** – Automatically attaches one managed storage adaptor to composters; that adaptor provides both Industrial In and Industrial Out
- **Planter storage adaptor support** – Automatically attaches one managed storage adaptor to planter boxes for industrial fertilizer input and harvest output
- **Planter output popup** – Players looting a planter can choose Harvest, Seeds, or Clones output; choices are saved per planter
- **Config** – `HarmonyConfig/IndustrialTransferSpeed.json` (created on first load)
- **Clean unload** – Resets conveyors to vanilla (128) when mod is unloaded

## Architecture

| Component | Role |
|-----------|------|
| **BaseNetworkable.ServerInit** patch | Sets `MaxStackSizePerMove` when conveyors spawn (new or loaded) |
| **IndustrialConveyor.PostServerLoad** patch | Ensures value applied when conveyors load from save |
| **DecayEntity.SupportsChildDeployables** patch | Treats composters as valid child-deployable targets for storage adaptors |
| **Composter.ServerInit** patch | Creates a storage adaptor child on newly spawned/loaded composters |
| **PlanterBox.ServerInit** patch | Creates a storage adaptor child on newly spawned/loaded planters |
| **PlayerLoot.StartLootingEntity** patch | Shows the planter production popup while a player loots a planter |
| **cui.endtest** patch | Routes planter popup button clicks back to the Harmony mod |
| **StorageContainer.CanCompletePickup** patch | Blocks picking up composters only when non-managed child deployables are attached |
| **BaseCombatEntity.CanCompletePickup** patch | Blocks direct pickup of managed composter/planter storage adaptors |
| **IndustrialTransferSpeedMod** | OnLoaded: apply to existing conveyors; OnUnloaded: reset to 128 |
| **IndustrialTransferSpeedConfig** | Loads `HarmonyConfig/IndustrialTransferSpeed.json` |

## Config

```json
{
  "MaxStackSizePerMove (van is 128)": 600,
  "ComposterAdaptorLocalPosition (x y z)": [0.0, 0.7, 0.62],
  "ComposterAdaptorLocalRotation (x y z)": [90.0, 0.0, 0.0],
  "ComposterAdaptorLayoutVersion": 10,
  "ComposterAdaptorLocalPositions (x y z)": [
    [0.0, 0.7, 0.62]
  ],
  "ComposterAdaptorLocalRotations (x y z)": [
    [90.0, 0.0, 0.0]
  ],
  "PlanterAdaptorLocalPosition (x y z)": [0.0, 0.2, 0.32],
  "PlanterAdaptorLocalRotation (x y z)": [0.0, 0.0, 0.0],
  "PlanterAdaptorLayoutVersion": 3,
  "PlanterAutoHarvestEnabled": true,
  "PlanterAutoHarvestIntervalSeconds": 10.0,
  "PlanterAutoHarvestMode (Harvest, Seed, or Clone)": "Harvest",
  "PlanterAutoHarvestStage (Fruiting or Ripe)": "Ripe",
  "PlanterAutoHarvestStageThresholdPercent": 0,
  "PlanterAutoCloneStage (Sapling, Mature, Fruiting, or Ripe)": "Sapling",
  "PlanterAutoCloneStageThresholdPercent": 0
}
```

- **MaxStackSizePerMove** – Max stack amount conveyors can transfer per move (1–100000). Vanilla default: 128.
- **ComposterAdaptorLocalPositions / Rotations** – Local offsets and Euler rotations for the auto-spawned composter storage adaptor. Defaults mirror UltimateIndustrialFarm's composter slot.
- **PlanterAdaptorLocalPosition / Rotation** – Fallback local offset and Euler rotation for unknown planter prefabs. Known planter prefabs use UltimateIndustrialFarm's per-prefab slot positions.
- **PlanterAutoHarvestEnabled / IntervalSeconds / Mode** – When enabled and the planter output is connected, managed planters periodically collect mature output into the planter inventory so conveyors can move it out. `Harvest` outputs normal produce; `Seed` outputs seeds; `Clone` outputs genetic cuttings with growable genes encoded into item instance data without deleting the source plant.
- **Player production popup** – When a player loots a planter, the popup can set that planter to `Harvest`, `Seed`, or `Clone`. Per-planter selections are saved in `HarmonyConfig/IndustrialTransferSpeed.Planters.json`.
- **PlanterAutoHarvestStage / CloneStage / ThresholdPercent** – Fruit harvest waits for the configured harvest stage (`Ripe` by default, so plants are no longer harvested during `Sapling`). Clone mode uses its own stage gate (`Sapling` by default). Thresholds require progress within the configured stage before collection starts.
- **Direct fertilizer pull** – Managed planters can pull fertilizer from a connected composter/storage adaptor input without a conveyor between the two adaptors.
- **ComposterAdaptorLayoutVersion** – Forces older broken or experimental composter adaptor layouts to reset to the current defaults.
- **Slot colors** – Managed composter/planter adaptor inputs are forced blue and outputs orange.

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
