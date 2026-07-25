# BetterTC Oxide Updates vs TCUpgrade Harmony Mod

Comparison of BetterTC 1.6.1 through 1.6.5 with the TCUpgrade Harmony mod. Oxide-only features (permissions API, web CDN updates, Notify plugin) are skipped.

**TCUpgrade version:** 1.6.5 (parity target: BetterTC 1.6.5)

---

## Implemented in TCUpgrade 1.6.5

| Feature | BetterTC version | TCUpgrade status |
|---------|------------------|------------------|
| Item Category Filter (Resources / ResourcesAndComponents / All) | 1.6.1 | Implemented in `UpdateBlockedItems` |
| Allow any item in TC (e.g. blueprint fragments via config) | 1.6.1 | Supported via `Allow Items in TC Inventory` |
| NO DLC / per-item permission on menu cards | 1.5.9 | `CanUseItemSkin` + NODLC button |
| Player status in auth list (admin Steam IDs) | 1.5.94 | `ShowPlayerStatusInAuthList` config |
| Wallpaper inside/outside (wpInternal / wpExternal) | 1.6.0 | Implemented |
| Boat wallpaper UI + apply from steering wheel | 1.6.2+ | Implemented (`tcupgrade.openboatwallpaper`, BOATCMD) |
| Wallpaper DLC ownership (skin ranges incl. Industrial + Glowing) | 1.6.5 | `IsWallpaperAllowed` in `TCUpgradeHelpers` |
| Wallpaper Damage config | 1.6.1+ | `WallpaperDamage` config + `ApplyWallpaperProtection` |
| Detailed missing-resource gametips | 1.6.5 | `GetMissingResources` + `*Detail` lang keys |
| Floor wallpaper on boat hull blocks | 1.6.5 | hull included in floor filters |
| autolock / autocodelock on TC place | 1.6.0 | Implemented |
| Auto Sort Items by Grade | 1.6.0 | Config present |
| Disable for Barges per item | 1.6.0 | `DisableBarges` + `IsOnBarge` |
| TC parent preservation on Train/Barge reskin | 1.5.93 | `TCSkinReplace` parent logic |
| KeyLock keyCode copy via reflection | 1.6.5 | `TCUpgradeHelpers.CopyLock` |

---

## Skipped (Oxide-only or not applicable)

| Feature | Reason |
|---------|--------|
| Staging API / CHECK UPDATE web import | Uses remote JSON; Harmony mod uses local `HarmonyImages/TCUpgrade` |
| Oxide permission names (`bettertc.*`) | Replaced by config + 0Permissions-style strings (`TCUpgrade.*`) |
| Notify / UINotify plugin integration | Not on Harmony stack |
| ImageLibrary / Carbon image DB | Local FileStorage + HarmonyImages |
| Barges Oxide plugin hook | Replaced by `IsOnBarge` (Train/Barge detection) |

---

## Config keys added for 1.6.5 parity

```json
"Item Category Filter (Resources, ResourcesAndComponents, All)": "Resources",
"Wallpaper Damage": true
```

Existing servers pick up defaults on reload; save config to persist new keys to `HarmonyConfig/TCUpgrade.json`.
