# RecyclerSpeed

Harmony mod that reduces recycler recycle time. Default: 2x speed (half the vanilla time). No permissions—affects all players and all recycler types.

## Mod Identity

| Item | Value |
|------|-------|
| **Purpose** | Speed up recycler processing (radtown + safezone) |
| **Entry point** | `HarmonyHooks` implements `IHarmonyModHooks` |
| **Authorization** | None—default group. All players get faster recycling. |

## Project Structure

| File | Responsibility |
|------|----------------|
| `HarmonyConfig.cs` | JSON config load; multiplier, overlay toggle, anchors |
| `HarmonyHooks.cs` | Lifecycle; loads config on `OnLoaded` |
| `RecyclerSpeedUI.cs` | CUI overlay (container + text) to cover static efficiency display |
| `Recycler_GetRecyclerStats_Patch.cs` | Postfix on `Recycler.GetRecyclerStats` |
| `StorageContainer_PlayerOpenLoot_Patch.cs` | Postfix; when Recycler opened, send overlay |
| `PlayerLoot_Clear_Patch.cs` | Prefix; when loot closed, destroy overlay |

## Configuration

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| `RecyclerSpeedMultiplier` | float | 2 | Divide recycle interval by this. 2 = half time (2x speed). |
| `ShowOverlay` | bool | true | Show CUI overlay over static "60% EFFICIENCY, 5 SEC" with actual modded values. |
| `OverlayParent` | string | "OverlayNonScaled" | CUI parent (Overlay, Hud, OverlayNonScaled). Match TCUpgrade. |
| `OverlayAnchormin` | string | "0.56 0.515" | Overlay position (normalized). Adjust if text doesn't line up. |
| `OverlayAnchormax` | string | "0.70 0.555" | Overlay size. Adjust if text doesn't line up. |

Config file: `HarmonyConfig/RecyclerSpeed.json`

Example:
```json
{
  "RecyclerSpeedMultiplier": 2,
  "ShowOverlay": true,
  "OverlayAnchormin": "0.56 0.515",
  "OverlayAnchormax": "0.70 0.555"
}
```

## Vanilla vs Modded

Duration/efficiency now come from `Recycler.GetRecyclerStats` (zone config + powergrid buffs). Multiplier divides the resolved duration.

| Example | Vanilla Interval | Default (2x) |
|---------|------------------|--------------|
| typical radtown config | ~5s | ~2.5s |
| typical safezone config | ~8s | ~4s |

## Harmony Patches

| Patch | Target | Type | Purpose |
|-------|--------|------|---------|
| `Recycler_GetRecyclerStats_Patch` | `Recycler.GetRecyclerStats` | Postfix | Divide `duration` by `RecyclerSpeedMultiplier` |

## Lifecycle

- **OnLoaded:** Load config from `HarmonyConfig/RecyclerSpeed.json`.
- **OnUnloaded:** No cleanup required.

## What NOT to Touch

- **Patch target:** `Recycler.GetRecyclerStats` signature/behavior may change by Rust version.
- **Config path:** `HarmonyConfig` directory is standard; changing breaks auto-create.

## Build & Deploy

```powershell
.\build.ps1
```

Output: `D:\!RustServer\HarmonyMods\RecyclerSpeed.dll`. Load: `harmony.load RecyclerSpeed`.
