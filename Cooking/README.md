# Cooking (Harmony OxideCompat port)

Port of Oxide `Cooking` v2.0.35 to a Harmony mod. Player/recipe data is shared across SVR1/SVR2/SVR3 via the existing hardlinked `Cooking.json`.

## Paths

| | |
|---|---|
| Config | `HarmonyConfig/Cooking.json` |
| Language | `HarmonyLanguage/Cooking.json` |
| Player data | `Custom cooking data directory` → `C:\!DataPersistence\oxide\data\Cooking\Cooking.json` (shared; overwrite in place) |
| Fallback data | `HarmonyData/Cooking/Cooking.json` when the custom directory is empty and the shared file is absent |
| UI images | ItemId/SkinId in CUI (no ImageLibrary). Optional files: `HarmonyImages/Cooking/` |
| Runtime DLL | `HarmonyMods/Cooking.dll` (entry DLL only) |
| Source | `.cursor/HarmonyMods/Cooking/` |

## Chat / UI

- Recipe menu: `/recipemenu`, `/cook` (from config)
- Ingredient bag and farmers market commands from config
- CUI buttons bridge via `cui.endtest COOKING …`

## SkillTree API

`AppDomain.SetData("Cooking_ApiType", typeof(CookingMod))` so SkillTree `PluginManager.Find("Cooking")` works.

Static methods used by SkillTree:

- `IsCookingMeal(Item)`
- `IsCustomIngredient(Item)`
- `IsHorseBuffed(RidableHorse)`
- `GetCookingMealsAndIngredients()`
- `API_GetBagItemCount` / `API_TakeBagItems`
- `Call(method, args)`

## Build

```powershell
cd .cursor\HarmonyMods\Cooking
.\build.ps1
```

Copies **only** `Cooking.dll` to root `HarmonyMods/`.

## Load

- Auto-loads with other Harmony mods on server start.
- Requires **Permissions** Harmony mod (`0Permissions.dll`).
- Do **not** run the Oxide plugin at the same time — unload/disable `oxide/plugins/Cooking.cs` (file left in place; not deleted by this port).

## Notes

- Data saves use in-place overwrite (`File.WriteAllText`) so the shared hardlink is not broken.
- Gather/consume/split patches target `ResourceDispenser.GiveResourceFromItem`, `CollectibleEntity.DoPickup`, `Item.ServerCommand`, `Item.SplitItem`, and `ItemModConsume.DoAction` (verified in `.cursor/!Assembly-CSharp-RUST/`).
