# BetterTC Oxide Updates vs TCUpgrade Harmony Mod

Comparison of BetterTC changelog (1.5.9–1.6.1) with your TCUpgrade Harmony mod. Features that depend on Oxide (permissions, plugins, CDN) are marked; the rest can be adopted in TCUpgrade if you want parity.

**Implemented (no TC inventory changes):** DLC/permission checks (1.5.9) — menu shows NO DLC when player lacks item permission or skin ownership; StartUpgrade/HandleReskin/HandleUpwall enforce item permission. Player status in auth list (1.5.94) — config "Show player status in auth list" + Admin Steam IDs see [Online]/[Offline]. TC inventory (what's allowed in TC) is left to the game; no Item Category Filter or Allow-any-item changes.

---

## Summary: What to Consider Adding

| Update   | Feature | Oxide-only? | TCUpgrade status | Recommendation |
|----------|--------|-------------|-------------------|----------------|
| **1.6.1** | Item Category Filter (Resources / ResourcesAndComponents / All) | No | Not implemented | **Add** – config + `UpdateBlockedItems` |
| **1.6.1** | Allow any item in TC (e.g. `basicblueprintfragment`: true) | No | Supported | Already in `Allow Items in TC Inventory`; document that blueprint fragments etc. can be allowed here |
| **1.6.1** | Wallpaper damage internal logic | No | You have `WallpaperDamage` config | Optional: align logic if BetterTC changed behavior |
| **1.6.1** | GameTips fixed | No | You use `gametip.hidegametip` / `showgametip` | Verify your flow matches current game API; fix if needed |
| **1.6.0** | Staging API endpoint (skin JSON) | Yes | N/A | Skip – you use local images / HarmonyImages |
| **1.6.0** | Wallpaper inside/outside (wpInternal / wpExternal) | No | **Already in TCUpgrade** | None |
| **1.6.0** | autolock / autocodelock on TC place | No | **Already in TCUpgrade** | None |
| **1.6.0** | Space Station skin | No | **Already in default Items** (Space Station, spacetest) | None |
| **1.6.0** | Auto Sort Items by Grade | No | **Already in config** (`Auto Sort Items by Grade`) | None |
| **1.6.0** | Disable upgrade/skin in Barges per item | No | **Already in config** (`Disable for Barges` per item) | None |
| **1.5.94** | Auth list: names via different method | No | You don’t use Oxide’s name resolution | Optional: improve name resolution if you show auth list |
| **1.5.94** | bettertc.playerstatus (online/offline in auth list) | Permission name is Oxide; idea is generic | Not implemented | **Consider** – add “player status” in auth list (e.g. config or simple “staff” check) |
| **1.5.93** | TC doesn’t move when changing skins on Train/Barge | No | **Already in TCUpgrade** – `TCSkinReplace` uses `tc.HasParent()` and `SetParent(parent, true)` | None |
| **1.5.92** | Compilation / TC disappear fix for special buildings | No | Same parent logic as 1.5.93 | Already covered |
| **1.5.9**  | No updefault/upskin → show NO DLC (can’t reskin skins player doesn’t own) | No | Partially | **Add** – in menu show NO DLC when player lacks item permission or doesn’t own skin; enforce permission in handlers |

---

## Details

### 1.6.1 – Item Category Filter & Allow Any Item

- **Item Category Filter**  
  BetterTC: `"Item Category Filter (Resources, ResourcesAndComponents, All)": "Resources"`  
  - Sets `cupboard.onlyAcceptCategory` to `ItemCategory.Resources`, `ItemCategory.All`, or for `ResourcesAndComponents` sets `All` and then builds blocked set = everything except Resources, Components, and a small tool whitelist.  
  - TCUpgrade does **not** set `onlyAcceptCategory`; it only manipulates `blockedItems` and (when present) `onlyAllowedItems`.  
  - **Recommendation:** Add a config option (e.g. `"Item Category Filter"`: `"Resources"` | `"ResourcesAndComponents"` | `"All"`) and in `UpdateBlockedItems`: set `cupboard.inventory.onlyAcceptCategory` (on the TC’s `StorageContainer`) for `Resources` / `All`; for `ResourcesAndComponents` replicate BetterTC’s logic (Resources + Components + tools allowed, rest blocked).

- **Allow any item (e.g. blueprint fragments)**  
  BetterTC: `allowedItemsConfig` with entries like `"basicblueprintfragment": true`.  
  TCUpgrade: **Already supported** via `Allow Items in TC Inventory` (`AllowedItemsConfig`). You can document that users can add e.g. `"basicblueprintfragment": true` (and same for advanced, etc.) to allow those items in the TC.

### 1.6.1 – Wallpaper Damage / GameTips

- **Wallpaper damage**  
  BetterTC revised internal logic; you already have a `WallpaperDamage`-style option. Only worth changing if you need to match BetterTC’s exact behavior.

- **GameTips**  
  If the game changed how gametips work, ensure your `CreateGameTip` path (e.g. `gametip.hidegametip` / `gametip.showgametip` + delayed hide) still works; fix if the game API changed.

### 1.6.0 – Already in TCUpgrade

- **Wallpaper inside/outside:** `WpInternal`, `WpExternal` in `TCConfig` and WALLPAPERSIDES; you already apply internal/external in `WallpaperProgress` / `WallpaperBlock`.
- **autolock / autocodelock:** Implemented in `Analytics_OnEntityBuilt_Patch` and `AddAutoLock`.
- **Space Station skin:** In default `ItemsList` (e.g. Space Station, spacetest, armored).
- **Auto Sort by Grade:** `AutoSortItems` in config; used when building the upgrade list.
- **Disable for Barges per item:** `DisableBarges` on `ItemInfo` and `IsOnBarge` checks in handlers.

### 1.5.94 – Auth list names & player status

- **Names:** BetterTC switched to a different way of resolving names on the authorized list. You don’t use Oxide’s APIs for that; if your auth list shows names, you can optionally improve resolution the same way (e.g. from game/steam data).
- **Player status (online/offline):** BetterTC added a permission so staff can see [Online] / [Offline] next to names. You can add a similar **optional** feature: e.g. a config like “Show player online/offline in auth list” and, when enabled, show status for each auth entry (no Oxide permission needed; you can gate by your own “staff” or admin list).

### 1.5.93 / 1.5.92 – TC not moving on Train/Barge

- BetterTC: When replacing TC skin, if `tc.HasParent()`, it calls `tcskin.SetParent(parent, true)` in `NextTick` so the new TC stays attached (Train/Barge).  
- TCUpgrade: You already do the same in `TCSkinReplace` (e.g. `if (tc.HasParent()) { var parent = tc.GetParentEntity(); ... newTc.SetParent(parent, true); }`). No change needed.

### 1.5.9 – NO DLC when no permission / don’t own skin

- **Behavior:** If the player doesn’t have the **item’s** permission (e.g. `bettertc.updefault` / `bettertc.upskin`) or doesn’t own the skin, the upgrade/reskin option is not allowed; the UI shows “NO DLC” instead.
- **TCUpgrade today:** You block the **action** when `!IsSkinOwnedOrBypass` (NoDLCPurchased). You do **not**:
  - Use the per-item `Permission` (e.g. `TCUpgrade.updefault`, `TCUpgrade.upskin`) when drawing the menu, or
  - Show a “NO DLC” (or locked) state on the card when the player lacks that permission or doesn’t own the skin.
- **Recommendation:**
  1. **Menu:** When building each upgrade card, compute “can use this skin” = `HasPermission(player, item.Permission)` and `IsSkinOwnedOrBypass(player, item.SkinId)`. If either is false, show a “NO DLC” (or locked) button/state instead of Upgrade/Reskin/Wallpaper for that item.
  2. **Handlers:** In `StartUpgrade`, `HandleReskin`, `HandleUpwall`, and any other handler that applies a skin, also check `HasPermission(player, itemInfo.Permission)`. If false, show the same NoDLCPurchased message and return (so players without that permission can’t use that skin even if they trigger the command).

---

## Oxide-only (skip for Harmony)

- **Staging API:** BetterTC loads skin data from a different JSON URL on staging; you don’t use that.
- **Barges plugin integration:** “Disable in Barges” in BetterTC can tie into a specific Barges plugin; your per-item “Disable for Barges” + `IsOnBarge` (Train/Barge detection) is the Harmony equivalent and doesn’t require Oxide.
- **Permission names** (`bettertc.*`): You use config-based “permissions” (e.g. `TCUpgrade.use`, `TCUpgrade.upskin`); no need to implement Oxide permission strings.

---

## Suggested code changes (short list)

1. **Item Category Filter**  
   - Add config: `"Item Category Filter (Resources, ResourcesAndComponents, All)": "Resources"`.  
   - In `UpdateBlockedItems`, set `cupboard.inventory.onlyAcceptCategory` (StorageContainer) and, for `ResourcesAndComponents`, build blocked/allowed sets like BetterTC (Resources + Components + tool whitelist).

2. **NO DLC / permission per item (1.5.9)**  
   - In `ShowMenu`, for each item: if `!HasPermission(player, item.Permission) || !IsSkinOwnedOrBypass(player, item.SkinId)`, show a “NO DLC” (or locked) state instead of Upgrade/Reskin/Wallpaper buttons.  
   - In `StartUpgrade`, `HandleReskin`, `HandleUpwall`: get `ItemInfo`, check `HasPermission(player, itemInfo.Permission)`; if false, show NoDLCPurchased and return.

3. **Optional**  
   - **Player status in auth list:** Config + when building auth list UI, show [Online]/[Offline] next to names if enabled.  
   - **GameTips:** Verify `CreateGameTip` and gametip console commands against current game build.  
   - **Docs:** In README or config comments, note that “Allow Items in TC Inventory” can include e.g. `"basicblueprintfragment": true`, `"advancedblueprintfragment": true`.

Once you decide which of these you want (e.g. 1 + 2 only), the edits are localized to config, `UpdateBlockedItems`, `ShowMenu`, and the upgrade/reskin/upwall handlers.
