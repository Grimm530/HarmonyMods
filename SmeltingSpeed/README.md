# SmeltingSpeed

Harmony mod that halves smelt time for all oven/furnace types in Rust. No config. Single patch on `BaseOven.GetSmeltingSpeed`.

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

All extend `BaseOven` and use `GetSmeltingSpeed()` in `Cook()` → `IncreaseCookTime(deltaTime * GetSmeltingSpeed())`, so a single patch covers all.

## Project Structure

| File | Responsibility |
|------|----------------|
| `SmeltingSpeedMod.cs` | Lifecycle, `SpeedMultiplier` constant |
| `Patches/BaseOven_GetSmeltingSpeed_Patch.cs` | Postfix on `BaseOven.GetSmeltingSpeed`; multiplies result by 2 |

## Persistent Data Model

- **No config.** All behavior hardcoded.
- `SpeedMultiplier = 2f` in `SmeltingSpeedMod`.

## Harmony Patches

| Patch | Target | Type | Purpose |
|-------|--------|------|---------|
| `BaseOven_GetSmeltingSpeed_Patch` | `BaseOven.GetSmeltingSpeed` | Postfix | Multiply return value by 2 to halve smelt time |

## Lifecycle

- **OnLoaded:** Set `Instance`, log load message.
- **OnUnloaded:** Set `Instance = null`, log unload message.

## What NOT to Touch Without Care

- **Patch target:** `BaseOven.GetSmeltingSpeed` signature may change by Rust version.
- **Fuel consumption:** Fuel burn rate is tied to `cookingTemperature` in `Cook()`, not `GetSmeltingSpeed`. Fuel still burns at vanilla rate; only cook progress is doubled. This matches vanilla behavior (higher temp = faster fuel burn + faster cook).

## Performance

- Single Postfix per `GetSmeltingSpeed` call (runs when ovens are cooking).
- Minimal: one multiplication per oven tick (~0.5s interval per oven).

## Build & Deploy

```powershell
.\build.ps1
```

Output: `D:\!RustServer\HarmonyMods\SmeltingSpeed.dll`. Load: `harmony.load SmeltingSpeed`.
