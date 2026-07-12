# Map Image: HarmonyCustomGenerator vs CustomMapGen

## Timing (Critical)

| | HarmonyCustomGenerator | CustomMapGen (current) |
|---|---|---|
| **Hook** | `LoadingScreen.Update("DONE")` Prefix | `WorldSerialization.Save` Postfix |
| **When** | After `[1.1s] Finalizing World` | During/after `[225s] Processing World`, before Saving World |
| **Order** | Finalizing World → CGen SIZE/SEED → DEPS → Render → Quit | Processing World → **[our render]** → Saving World → Finalizing World |

**Fix:** Move CustomMapGen render to `LoadingScreen.Update("DONE")` Prefix to match HCG.

## Font Paths

| | HarmonyCustomGenerator | CustomMapGen |
|---|---|---|
| **Primary** | `maps/images/resources` (CustomGenerator MapImage.cs) | `maps/images/resources` then `mapimages/resources` |
| **Utility** | `mapimages/resources` (MapImageRender.cs hardcoded) | Same |
| **User request** | - | Use `D:\!RustServer\maps\images\resources` = `maps/images/resources` when cwd is D:\!RustServer |

**Fix:** Default `FontResourcesPath = "maps/images/resources"` in config. Ensure this is checked first.

## Render Parameters

| | HarmonyCustomGenerator | CustomMapGen |
|---|---|---|
| **Scale** | 0.75 (hardcoded) | 1.0 (config) |
| **Ocean margin** | 150 | 350 |
| **Output size** | 4000×0.75 + 150×2 = 3300px | 4000 + 350×2 = 4700px |
| **Texture limit** | 3300 < 4096 ✓ | 4700 > 4096 ✗ (fallback to dummy) |

**Fix:** Default scale 0.75, ocean margin 150 to match HCG and stay under 4096.

## Monument Rendering

| | HarmonyCustomGenerator | CustomMapGen |
|---|---|---|
| **Font** | PermanentMarker.ttf (primary), dinprobold.otf | dinprobold, dinpro, PermanentMarker (order) |
| **Monument filter** | `shouldDisplayOnMap && mapIcon == null` OR `name.Contains("train")` | Same logic |
| **Font size** | 20 (Regular), 11 (Smaller) | 22 |

**Fix:** Same logic. Font not found → no monuments drawn. Fixing font path fixes monuments.

## Output

| | HarmonyCustomGenerator | CustomMapGen |
|---|---|---|
| **Path** | `maps/images/{mapName}.png` | config.OutputFolder |
| **Format** | Map name from config | `{size}_{seed}.png` or `map_{size}_{seed}.png` |

## Color / Quality

Both use same terrain pipeline: TerrainHeightMap, TerrainSplatMap, TerrainTopologyMap.
Same formulas: SunPower 0.65, Contrast 0.94, Brightness 1.05.

Dimmer output likely from:
1. **Texture fallback** – 4700px exceeds 4096, dummy texture used.
2. **Different timing** – rendering at Save vs DONE may affect available data.

**Fix:** Reduce resolution (scale 0.75, margin 150) to avoid texture fallback.
