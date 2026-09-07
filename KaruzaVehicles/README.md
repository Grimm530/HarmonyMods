# KaruzaVehicles (Harmony Mod)

**No Oxide dependency.** Harmony port of Karuza custom-entity vehicles (not vanilla VehicleLicence / Harmony **RustVehicles**).

## Mod identity

| Field | Value |
|-------|--------|
| **Name** | KaruzaVehicles |
| **Type** | Harmony mod (`IHarmonyModHooks`) |
| **Oxide** | None — unload the Oxide Karuza plugins listed below |
| **API** | `AppDomain.SetData("KaruzaVehicles_ApiType", typeof(KaruzaVehicles.KaruzaVehiclesMod))` |
| **Upstream** | CustomEntities 1.0.20, Common 1.11.00, RustCar 1.25.0, RustHelicopter 1.31.0, RustPlane 1.37.0 |

## Load order

1. **0Permissions**
2. **KaruzaVehicles**

Cockpit radio voice still needs the separate Radio Harmony mod (another agent). This assembly only stubs `Radio.RegisterRadio` / `RemoveRadio`.

## Unload these Oxide plugins

They must **not** run alongside this mod (double spawn / double save):

- `KaruzaEntitiesCommon`
- `CustomEntities` (bundled here — required custom prefab runtime)
- `RustCar`
- `RustHelicopter`
- `RustPlane`
- `KaruzaVehiclePush`
- `KaruzaVehicleHorseTowing`
- `BulletProjectile`

Do **not** unload `Radio` / `VehicleRadio` until their Harmony mod is loaded.

## What is in this assembly

| Source | Role |
|--------|------|
| `CustomEntities.cs` | Custom prefab register / save / load (required by Karuza) |
| `KaruzaEntitiesCommon.cs` | Shared vehicle entity runtime |
| `RustCar.cs` / `RustHelicopter.cs` / `RustPlane.cs` | Vehicle controllers |
| `KaruzaVehiclePush.cs` | Hold E to push — `PushController` on `PlayerInit` (not a global Update patch) |
| `KaruzaVehicleHorseTowing.cs` | Horse towing of custom vehicles |
| `BulletProjectile.cs` | Karuza projectile helper |
| `Radio.cs` | `IRadio` + no-op/forward Register/Remove |

## Config / data / lang

Copied from Oxide with original filenames:

| Oxide | Harmony |
|-------|---------|
| `oxide/config/KaruzaEntitiesCommon.json` | `HarmonyConfig/KaruzaEntitiesCommon.json` |
| `oxide/config/RustCar.json` + `RustCar/` | `HarmonyConfig/RustCar.json` + `HarmonyConfig/RustCar/` |
| `oxide/config/RustHelicopter.json` + dir | `HarmonyConfig/RustHelicopter.json` + `HarmonyConfig/RustHelicopter/` |
| `oxide/config/RustPlane.json` + dir | `HarmonyConfig/RustPlane.json` + `HarmonyConfig/RustPlane/` |
| `oxide/data/CustomEntities/` | `HarmonyData/CustomEntities/` |
| lang | `HarmonyLanguage/KaruzaVehicles.json` (en), `KaruzaVehicles.<locale>.json` |

## Karuza API (auto-update vehicle JSON)

On every server start and every `harmony.load KaruzaVehicles`, if `HarmonyConfig/KaruzaEntitiesCommon.json` has `APIPath`, `APIId`, and `APISecret`, the mod fetches owned entity configs and (when `SaveApiConfigsToDisk` is true) writes them to:

- planes → `HarmonyConfig/RustPlane/{Name}.json` (type `1`)
- helicopters → `HarmonyConfig/RustHelicopter/{Name}.json` (type `2`)
- cars → `HarmonyConfig/RustCar/{Name}.json` (type `3`)

New purchases only appear after they are added on the KPC Dashboard (**+ Add All Missing**). Credentials live in `HarmonyConfig/KaruzaEntitiesCommon.json` only — do not put the server secret in git commits or docs.

Look for `API is configured: True` then `Entity Configs for RustCar Received!` / `Saved N RustCar configs`.

## Commands

| Command | Permission |
|---------|------------|
| `spawn_at`, `purge_prefab`, `purge_plugin`, `count_plugin` | `customentities.admin` |
| `rustcar.reload <name>` | server console |
| `rusthelicopter.reload <name>` | server console |
| `rustplane.reload <name>` | server console |

## Build

```powershell
.\.cursor\HarmonyMods\KaruzaVehicles\build.ps1
```

Copies **only** `KaruzaVehicles.dll` to `HarmonyMods/`. Load: `harmony.load KaruzaVehicles`.

## Patches

| Method | Kind | Why |
|--------|------|-----|
| `BaseNetworkable.Spawn` | postfix | CargoShip cache, RidableHorse towing wrapper |
| `BaseNetworkable.Kill` | prefix | CustomEntities save-list forget, cargo cleanup |
| `BasePlayer.PlayerInit` | postfix | Attach `PushController` |
| `BasePlayer.OnDisconnected` | postfix | Destroy `PushController` |
| `ItemContainer.Insert` / `Remove` | postfix | CustomEntities inventory save lists |
| `SaveRestore.DoAutomatedSave` | prefix | Persist custom entities |
| `ConVar.Chat.say` | prefix | CustomEntities chat commands |

Not the same as Harmony **RustVehicles** (vanilla vehicle spawn/recall).
