# ItemRetriever (Harmony Mod)

**No Oxide dependency.** Harmony port of WhiteThunder **ItemRetriever 0.7.7** — library that lets players craft, reload, and build using items from external suppliers (e.g. Backpacks retrieve mode).

## Mod identity

| Field | Value |
|-------|--------|
| **Name** | ItemRetriever |
| **Type** | Harmony mod (`IHarmonyModHooks`) |
| **Oxide** | None — for Oxide-free servers only |
| **Source** | `.cursor/Oxide.Plugins.Cant-Use/ItemRetriever.cs` (v0.7.7) |
| **Config** | None (library plugin) |
| **Data** | None |

## Project structure

| File | Content |
|------|--------|
| `ItemRetriever.cs` | Full 0.7.7 plugin logic (ported from Oxide) |
| `ItemRetrieverCompat.cs` | Slim Oxide shims: `Plugin`, `Interface`, host, `ItemContainerHooks` |
| `ItemRetrieverHarmonyMod.cs` | Harmony entry, AppDomain API, Plugin bridge, ready callbacks |
| `Patches/` | Harmony patches for Oxide inventory/craft hooks |
| `convert-from-oxide.ps1` | Regenerates `ItemRetriever.cs` from Oxide source |
| `ItemRetriever.csproj` | Game refs + Krafs.Publicizer |
| `build.ps1` | Build and copy DLL to `HarmonyMods/` |

## Harmony patches (Oxide hooks)

| Oxide hook | Game method |
|------------|-------------|
| `OnEntitySaved` | `BasePlayer.Save` (Postfix) |
| `OnInventoryNetworkUpdate` | `PlayerInventory.SendUpdatedInventoryInternal` (Prefix reimpl) |
| `OnInventoryItemsCount` | `PlayerInventory.GetAmount(int, bool)` |
| `OnInventoryItemsTake` | `PlayerInventory.Take(List, int, int)` |
| `OnInventoryItemsFind` | `PlayerInventory.FindItemsByItemID` |
| `OnInventoryItemFind` | `PlayerInventory.FindItemByItemID(int)` |
| `OnInventoryAmmoFind` | `PlayerInventory.FindAmmo(List, AmmoTypes)` |
| `OnInventoryAmmoItemFind` | `PlayerInventory.FindAmmo(AmmoTypes)` + Chainsaw/FlameThrower fuel |
| `OnIngredientsCollect` | `ItemCrafter.CollectIngredients` |
| `CanCraft` | `ItemCrafter.CanCraft(ItemBlueprint, int, bool)` |

## API for other mods

AppDomain key: **`ItemRetriever_ApiType`** → `ItemRetrieverHarmony.ItemRetrieverHarmonyMod`

Also: **`ItemRetriever_Plugin`** → Plugin bridge (`Call` routes to APIs).

Ready callbacks: **`ItemRetriever_ReadyCallbacks`** / `RegisterReadyCallback(Action)`.

| Method | Description |
|--------|-------------|
| `API_AddSupplier(pluginOrName, spec)` | Register item supplier (Backpacks retrieve) |
| `API_RemoveSupplier(pluginOrName)` | Unregister supplier |
| `API_GetApi()` | Dictionary of API delegates |
| `API_AddContainer` / `API_RemoveContainer` / ... | Container registration |
| `API_FindPlayerItems` / `Sum` / `Take` / `FindPlayerAmmo` | Query helpers |
| `CallApi(method, args)` | Generic Oxide-style Call |

## Load order

**No forced order required.** On auto-start, DLLs load in filesystem/alphabetical order (typically `Backpacks` before `ItemRetriever` before `Permissions`).

Backpacks registers an `ItemRetriever_ReadyCallbacks` listener before IR exists; when ItemRetriever loads it fires ready callbacks and Backpacks calls `API_AddSupplier`. Same pattern for Permissions (`Permissions_ReadyCallbacks`).

Manual `harmony.load` order does not matter either as long as both DLLs end up loaded.

## Build and deploy

```powershell
.\.cursor\HarmonyMods\ItemRetriever\convert-from-oxide.ps1
.\.cursor\HarmonyMods\ItemRetriever\build.ps1
```

DLL → **`HarmonyMods/ItemRetriever.dll`**. Load: `harmony.load ItemRetriever`.

## PluginReferences

`InstantCraft` / `SuperCrafter` stay null unless those Harmony mods exist (no-op; InstantCraft block callback stays null).
