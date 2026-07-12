# GatherManager

Harmony mod for Rust dedicated server: full plugin parity with per-resource gather modifiers, craft speed, blueprint unlock, Mining Quarry and Excavator tuning, and `/gather` command. Gather info is appended to the server description (no Steam tags).

## Installation

1. Ensure there is a folder named `HarmonyMods` beside `RustDedicated.exe`.
2. **Option A**: Run the build script and copy the DLL:
   - From this folder run: `.\build.ps1`
   - This builds `GatherManager.dll` and copies it to `D:\!RustServer\HarmonyMods\`.
   - Edit `build.ps1` if your server root or paths differ.
   - **Build requirement**: `Rust.Harmony.dll` and game assemblies must be in `RustDedicated_Data\Managed`. If missing, copy from your Rust server install, or the script will try to build `Rust.Harmony` from `.cursor\Harmony-Assembly` and copy it to `Managed`.
3. **Option B**: Download a pre-built release and place `GatherManager.dll` in the `HarmonyMods` folder.

Restart the server (or use `harmony.load GatherManager` if your loader supports it) to load the mod.

## Commands

### All players

- **`/gather`** (in-game) – Shows detailed gather information (per-source modifiers, quarry speed).

### Admin only

- **`gather.scale [amount]`** – Get or set global gather multiplier (1–1000). Applies to **all** sources (nodes, pickups, quarry, excavator, survey, loot) when no per-resource rate is set. Triggers full loot respawn when changed.
- **`craft.scale [amount]`** – Get or set craft speed multiplier (0.01–1; lower = faster). Example: `craft.scale 0.5`
- **`blueprints.grantall [true|false]`** – Grant all blueprints to new players on connect.
- **`gather.rate <type> <resource> <multiplier>`** – Set per-resource gather rate. Types: `dispenser`, `pickup`, `quarry`, `excavator`, `survey`. Resource: display name or `*` for all. Use `remove` to reset.
- **`gather.resources`** – List valid resource names.
- **`gather.dispensers`** – List valid dispenser types (Tree, Ore, Flesh).
- **`dispenser.scale <dispenser> <multiplier>`** – Set dispenser-type scale. Dispensers: `tree`, `ore`, `corpse`.
- **`quarry.tickrate <seconds>`** – Set Mining Quarry resource tick interval (minimum 1 second).
- **`excavator.tickrate <seconds>`** – Set Excavator resource tick interval (minimum 1 second).

## Gather types covered

All of these use the global scale (`gather.scale`) when no per-resource modifier is set, and per-resource rates when you configure them with `gather.rate <type> <resource> <multiplier>`:

| Type | Source | Patched methods / behaviour |
|------|--------|-----------------------------|
| **dispenser** | Trees, ore nodes, flesh (corpses) | `ResourceDispenser.GiveResourceFromItem`, `ResourceDispenser.AssignFinishBonus` |
| **pickup** | Collectibles (hemp, mushrooms, etc.) and random drops (e.g. seeds) | `CollectibleEntity.DoPickup`, `RandomItemDispenser.TryAward` |
| **growable** | Plant harvest (pumpkins, corn, etc.) | `GrowableEntity.GiveFruit` |
| **quarry** | Mining Quarry output | `MiningQuarry.ProcessResources` |
| **excavator** | Excavator monument output | `ExcavatorArm.ProduceResources` |
| **survey** | Survey charge drops | `SurveyCharge.Explode` |
| **loot** | Barrels, crates, NPC corpses | `LootContainer.PopulateLoot`, `HumanNPC.CreateCorpse` |

Quarry and excavator tick rates are controlled by `quarry.tickrate` and `excavator.tickrate`.

## Configuration

Config file: `HarmonyConfig/GatherManager.json` (created on first load, or when saving via commands).

Settings persist across restarts. Modify via the console commands above, or edit the JSON directly.

## Compatibility

- **Oxide chat commands:** The mod never intercepts `chat.say`, `chat.teamsay`, or `chat.localsay`. Oxide plugin chat commands (e.g. `/kit`, `/spawn`) work for all players including the default group.
- **Console command scope:** GatherManager only handles its own commands; all other commands pass through to the game/Oxide unchanged.

## Related / Reference

### Game assembly (decompiled source)

These Rust game assemblies were used to design the patches. Key for understanding gather flow and why we only scale `Item` output:

| File | Relevance |
|------|-----------|
| `ResourceDispenser.cs` | `GiveResourceFromItem`, `AssignFinishBonus`, `UpdateFraction`, `containedItems`, `finishBonus` – core gather logic |
| `ResourceEntity.cs` | `health`, `OnAttacked` – entity health tied to `fractionRemaining` depletion |
| `TreeEntity.cs` | Trees; extends `ResourceEntity`, uses `ResourceDispenser` |
| `OreResourceEntity.cs` | Ore nodes; extends `StagedResourceEntity`, hotspot bonus |
| `CollectibleEntity.cs` | Pickups (hemp, mushrooms); `DoPickup`, `itemList` |
| `GrowableEntity.cs` | Plants; `GiveFruit` for harvest |
| `RandomItemDispenser.cs` | Random drops from collectibles (e.g. seeds from hemp); `TryAward`, `DistributeItems` |
| `MiningQuarry.cs` | Quarry; `ProcessResources` |
| `ExcavatorArm.cs` | Excavator; `ProduceResources` |
| `AntiHackJobs/GatherHitIndicesJob.cs`, `GatherNoClipBatchesJob.cs`, `GatherPlayersWithTicksJob.cs` | Gather-related anti-cheat |
| `BasePlayerJobs/GatherPosToValidateJob.cs` | Gather position validation |
| `ScaleParticleSystem.cs`, `ScaleRenderer.cs`, `ScaleTrailRenderer.cs`, `ScaleTransform.cs` | Scale components (e.g. on resource entities) |

Path: `.cursor/!Assembly-CSharp-RUST/` (and `AntiHackJobs/`, `BasePlayerJobs/` subdirs).

## Bugs / Features

Open an issue on the project repo.
