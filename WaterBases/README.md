# WaterBases Harmony Mod (1.0.26)

Nikedemos Water Bases ported from Oxide, combined with **WaterBasesJunkpileFix** (`JunkPileWater.Spawn` Physics.CheckSphere layermask transpiler).

## Load

- DLL: `HarmonyMods/WaterBases.dll`
- After `0Permissions.dll`
- AppDomain API: `WaterBases_ApiType` → `WaterBasesHarmony.WaterBasesMod`

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/WaterBases.json` |
| Lang | `HarmonyLanguage/WaterBases.json` |

## Chat / console

- `/wb_cfg`, `/give_square`, `/give_triangle`, `/draw_cargo`, `/grid_pos`, `/shore_distance`, `/copypaste_prepare`
- Craft GUI: planner held → `wb_craft.square` / `wb_craft.triangle` (bridged via `cui.endtest WB`)
- Covalence: `wb.give` square/triangle [player]

## API

```
CheckIfInsideWaterBase(DecayEntity)
CheckWaterFoundation(BuildingBlock)
Call(string method, params object[] args)
```

## Junkpile fix

`JunkPileWater.Spawn` CheckSphere layermask is replaced with Vehicle_Large | Construction | Deployed | Default unless the prefab name contains `"ghost"`.

## Build

```powershell
.\build.ps1
```

Copies **only** `WaterBases.dll` into `HarmonyMods/`.
