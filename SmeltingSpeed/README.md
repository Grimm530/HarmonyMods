# SmeltingSpeed

Harmony mod that halves smelt time for all oven/furnace types in Rust. No config. Single patch on `BaseOven.IncreaseCookTime`.

## Mod Identity

| Item | Value |
|------|-------|
| **Purpose** | Reduce smelt time by 50% for all furnace types |
| **Entry point** | `SmeltingSpeedMod` implements `IHarmonyModHooks` |
| **Speed multiplier** | 2× (effectively halves time) |

## Affected Furnace Types

| Type | Base Class | Temperature |
|------|------------|-------------|
| Campfire | BaseOven | Warming (50°C) |
| Furnace | BaseOven | Smelting (1000°C) |
| Large Furnace | BaseOven | Smelting (1000°C) |
| Oil Refinery | BaseOven | Fractioning (1500°C) |
| Electric Furnace | ElectricOven → BaseOven | Smelting (1000°C) |

All extend `BaseOven` and apply cook progress through `IncreaseCookTime(deltaTime * GetSmeltingSpeed())`, so patching the progress amount covers all.

## Project Structure

| File | Responsibility |
|------|----------------|
| `SmeltingSpeedMod.cs` | Lifecycle, `SpeedMultiplier` constant |
| `Patches/BaseOven_IncreaseCookTime_Patch.cs` | Prefix on `BaseOven.IncreaseCookTime`; multiplies progress amount by 2 |

## Persistent Data Model

- **No config.** All behavior hardcoded.
- `SpeedMultiplier = 2f` in `SmeltingSpeedMod`.

## Harmony Patches

| Patch | Target | Type | Purpose |
|-------|--------|------|---------|
| `BaseOven_IncreaseCookTime_Patch` | `BaseOven.IncreaseCookTime` | Prefix | Multiply cook progress by 2 to halve smelt time |

## Lifecycle

- **OnLoaded:** Set `Instance`, log load message.
- **OnUnloaded:** Set `Instance = null`, log unload message.

## What NOT to Touch Without Care

- **Patch target:** `BaseOven.IncreaseCookTime(float amount)` signature may change by Rust version.
- **Fuel consumption:** Fuel burn rate is tied to `cookingTemperature` in `Cook()`, not `GetSmeltingSpeed`. Fuel still burns at vanilla rate; only cook progress is doubled. This matches vanilla behavior (higher temp = faster fuel burn + faster cook).

## Performance

- Single Prefix per `IncreaseCookTime` call (runs when ovens are cooking).
- Minimal: one multiplication per oven tick (~0.5s interval per oven).

## Build & Deploy

```powershell
.\build.ps1
```

Output: `<server root>\HarmonyMods\SmeltingSpeed.dll`. Load: `harmony.load SmeltingSpeed`.
