# PlayerDLCAPI Harmony Mod (1.7.0)

Oxide-free port of Player DLC API 1.6.2. It exposes paid workshop skin,
Steam inventory content, DLC app, item, and redirected-item ownership checks
to other Harmony mods.

## Design

The mod does not patch game ownership behavior. Rust already owns the
authoritative checks:

- `SteamInventoryItem.HasUnlocked`
- `SteamDLCItem.HasLicense`
- `SteamInventory.HasItem`
- `SkinHelpers.TryGetRedirectSkinId`

PlayerDLCAPI builds the missing workshop-ID/content-ID and item redirect
indexes after Rust's Steam inventory definitions are ready, then delegates
ownership decisions to those game APIs.

`Rust.Workshop.dll` is not used. Its `WorkshopSkin` and rendering types load
and apply community skin assets; they do not provide server-side player
ownership data.

## Harmony API

Provider type: `PlayerDlcApiHarmony.PlayerDlcApiMod`

AppDomain keys:

- `PlayerDlcApi_ApiType`
- `PlayerDlcApi_Generation`
- `PlayerDlcApi_ReadyCallbacks`

The public static API preserves the Oxide plugin method names and overloads:

- `Initialized`
- `IsPaidSkin`, `FilterPaidSkins`
- `IsOwnedOrFreeSkin`, `FilterOwnedOrFreeSkins`
- `IsDLCItem`
- `IsOwnedOrFreeItem`, `FilterOwnedOrFreeItems`
- `CheckContentOwnership`, `FilterContentOwnership`
- `IsRedirectedSkin`
- `GetRedirectedShortname`, `GetRedirectedItemId`
- `GetRedirectedShortnameIfNotOwned`, `GetRedirectedItemIdIfNotOwned`

Consumers should resolve `PlayerDlcApi_ApiType` each time their cached
generation differs from `PlayerDlcApi_Generation`. They may register an
`Action` through `RegisterReadyCallback` to be notified after definitions are
indexed.

## Consumers on this server

Only these mods can grant paid DLC or workshop skins to players:

| Mod | Uses PlayerDLCAPI? | Config |
|-----|-------------------|--------|
| **Shop** | Yes — `IsOwnedOrFreeItem` before buy | `HarmonyConfig/Shop.json` + per-item `BypassOwnershipCheck` in shop data |
| **PlayerSkins** | Yes — when `ApprovedIfOwned` is enabled | `HarmonyConfig/PlayerSkins.json` → Workshop Options |
| **AlphaLoot** | No — blocks paid skins globally via `UseApprovedSkins` | `HarmonyConfig/AlphaLoot.json` |

AlphaLoot does not call this API. With `UseApprovedSkins: false` (default), it
builds a server-wide block list so random loot never rolls paid workshop skins.
That is TOS-safe without per-player ownership checks.

Other Harmony mods (AutoCodeLock, Kits, etc.) are out of scope for this DLL on
this server.

## Load order

Load PlayerDLCAPI before Shop and PlayerSkins:

```text
harmony.load PlayerDLCAPI
harmony.load AlphaLoot
harmony.load Shop
harmony.load PlayerSkins
```

The API is published as soon as the DLL loads. `Initialized()` becomes true
after Rust item and Steam inventory definitions are available.

## Recommended config (TOS-safe defaults)

**AlphaLoot** (`HarmonyConfig/AlphaLoot.json`):

```json
"Use skins from the approved skin list (WARNING! Allowing users to use paid DLC they don't own is against Rusts TOS)": false
```

**PlayerSkins** (`HarmonyConfig/PlayerSkins.json` → Workshop Options):

- `"Include approved skins ..."`: `true` — show Facepunch-approved skins
- `"Only show approved skins that the player owns"`: `true` — requires PlayerDLCAPI
- `"Enable workshop skins in the skin shop"`: your choice (community skins)

**Shop** (`HarmonyConfig/Shop.json`):

- No global DLC toggle. Enforcement is automatic when PlayerDLCAPI is loaded.
- Per shop item: set `BypassOwnershipCheck: true` only for intentional admin overrides.
- Permission `shop.bypass.dlc` is registered but not checked in Shop 2.4.201 logic.

## Build

```powershell
.\.cursor\HarmonyMods\PlayerDLCAPI\build.ps1
```

The script copies only `PlayerDLCAPI.dll` to the root `HarmonyMods/` runtime
directory.
