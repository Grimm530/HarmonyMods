# TCUpgrade Harmony Mod

Standalone Harmony mod providing TC (tool cupboard) upgrade, repair, reskin, wallpaper, and TC skin replacement. **No Oxide plugin required.** Loaded by HarmonyLoader from `HarmonyMods/`. Config: `HarmonyConfig/TCUpgrade.json` or `oxide/config/TCUpgrade.json` (fallback).

---

## 1. Mod Identity

| Attribute | Value |
|-----------|-------|
| **Name** | TCUpgrade |
| **Type** | Harmony mod (IL patching) |
| **Purpose** | Extend TC functionality: upgrade blocks, repair, reskin, wallpaper, TC skins |
| **Primary responsibilities** | Building block upgrade/downgrade from TC; repair blocks and deployables; wall reskin; wallpaper apply/remove; TC model skin replacement; TC inventory restrictions |

**Key feature flags** (config): `Reskin Enable`, `Reskin Wall Enable`, `Wallpaper Enable`, `Deployables Repair`, `Downgrade Enable`, `Use NoEscape Plugin`, `Use RaidBlock Plugin`, `Bypass DLC ownership check`.

---

## 2. Runtime Topology (Architecture Overview)

| Component | What it stores | Key invariants |
|----------|----------------|----------------|
| `TCUpgradeMod._buildingCupboard` | `BuildingPrivlidge` → `TCConfig` | One TCConfig per TC; cleared on unload |
| `TCUpgradeMod._playerSelectedSkins` | `ulong` (userID) → `TCSkin` | Persists during session only |
| `TCUpgradeMod._data` | `TCUpgradeData` (CustomWallpapers) | Loaded on start, saved on addwp |
| `TCUpgradeConfig.Config` | Static config singleton | Loaded from HarmonyConfig or oxide/config |

**State flow:** Config + Data → Load; Patches → `OnLootStarted`/`OnLootEnded`; SENDCMD → Coroutines; Unload → stop coroutines, clear caches.

**Dependencies:** Optional Oxide plugins (NoEscape, RaidBlock) via reflection when `Use NoEscape Plugin` / `Use RaidBlock Plugin` are true.

---

## 3. Persistent Data Model

### TCUpgradeData (`HarmonyConfig/TCUpgrade/data.json`)

| Field | Purpose |
|-------|---------|
| `Version` | Schema version (currently "1.6.5") |
| `CustomWallpapers` | `Dictionary<string, HashSet<ulong>>` — category (Wall/Floor/Ceiling) → skin IDs |

**Lifecycle:** Load on `OnLoaded`; Save when `addwp` adds a skin or when data version is migrated to 1.6.5.

### TCConfig (runtime only, in `_buildingCupboard`)

Per-TC state for upgrade/repair/reskin/wallpaper work: `Id`, `Grade`, `SkinId`, `Color`, `Colour`, coroutine refs (`WorkUpgrade`, `WorkRepair`, etc.), `Work`, `Repair`, `Reskin`, `Upwall`, `Effect`, `Downgrade`, `WallpaperId`, `WpInternal`, `WpExternal`, `Player` (current operator).

---

## 4. Configuration Schema (Condensed)

| Field | Type | Default | Behavioral impact |
|-------|------|---------|-------------------|
| Admin Steam IDs (bypass all permission checks) | `List<ulong>` | `[]` | Bypasses all permission checks |
| Grant upgrade/skin item permissions to all players | bool | true | When false, only Admin Steam IDs can use skin-tier items (TCUpgrade.updefault/upskin). When true, everyone can if they own the DLC. |
| Bypass DLC ownership check | bool | false | Allows skins without DLC (⚠ creative/test only per Facepunch) |
| Use NoEscape Plugin | bool | true | When true, calls NoEscape `IsRaidBlocked` via reflection |
| Use RaidBlock Plugin | bool | true | When true, calls RaidBlock `IsRaidBlocked` via reflection |
| Debug | bool | false | Verbose logging |
| Force both sides including external sides | bool | true | No longer patches game `CheckWallpaper`; game may remove invalid wallpaper (e.g. after rotation). Option kept for compatibility; both internal/external wallpaper when valid. |
| Item Category Filter (Resources, ResourcesAndComponents, All) | string | Resources | TC inventory category filter via `onlyAcceptCategory` and blocked items |
| Wallpaper Damage | bool | true | When false, wallpaper blocks get full damage protection |
| Wallpaper placement Cost (Cloth) | int | 5 | Cloth per wallpaper apply |
| Deployables Repair | bool | true | Include deployables in repair |
| Repair Cooldown After Recent Damage (seconds) | float | 30 | Skip repair if `SecondsSinceAttacked < cooldown` |
| Downgrade Enable | bool | true | Allow downgrade (e.g. metal→stone) |
| Downgrade only Owner Entity Build | bool | true | Only owner blocks can be downgraded |
| Upgrade only Owner Entity Build | bool | true | Only owner blocks can be upgraded |
| Upgrade / Downgrade only Owner and Team | bool | false | When true, team members' blocks included |
| Reskin Wall TC Distance | float | 100 | Radius for external wall reskin |
| Only reskin on wall of the same grade | bool | true | Restrict wall reskin to same grade |
| Cooldown Frequency Upgrade/Repair/Reskin/Wallpaper | `Dict<string,float>` | `TCUpgrade.use:2`, `TCUpgrade.vip:1` | Delay between each block (larger = slower) |
| Cost Modifier for repairs | `Dict<string,float>` | `TCUpgrade.use:1.5`, `TCUpgrade.vip:1` | Multiply repair cost |
| Allow Items in TC Inventory | `Dict<string,bool>` | gunpowder/sulfur/explosives/etc: false | Block or allow specific items in TC |
| Show player status in auth list | bool | false | When true, viewers in Admin Steam IDs see "[Online]" or "[Offline]" next to each auth name |
| Items | `List<ItemInfo>` | 12 default items | Upgrade menu items (grade, skin, permission) |

**Config load:** `TCUpgradeConfig.LoadConfig()` on mod load. Prefer `HarmonyConfig/TCUpgrade.json`; fallback `oxide/config/TCUpgrade.json`; migrates from legacy `TCUpgrade.json` if present. Not hot-reloadable.

---

## 5. Permissions & Authorization Matrix

Permission model is **config-based** (no Oxide permission system). `HasPermission(userId, perm)` returns true only if: (1) User is in Admin Steam IDs, or (2) `perm == "TCUpgrade.use"` or `perm == "default"`. Checks for `TCUpgrade.repair`, `TCUpgrade.upgrade`, etc. therefore grant access only to admins (unless perm is TCUpgrade.use/default).

| Permission | Who | What it gates | Where enforced |
|------------|-----|---------------|-----------------|
| (Admin Steam IDs) | Config | Bypass all checks | `HasPermission` |
| TCUpgrade.use | All (default) | Base access | Various |
| TCUpgrade.upgrade | Players | Upgrade menu/action | `HandleSendCmd` UPGRADE |
| TCUpgrade.repair | Players | Repair toggle | `HandleSendCmd` REPAIR |
| TCUpgrade.reskin | Players | Reskin, upwall | RESKIN, UPWALL |
| TCUpgrade.wallpaper | Players | Wallpaper menus/actions | WALLPAPER, WALLPAPERON |
| TCUpgrade.wallpaper.nocost | Players | No cloth cost, cloth refund on pickup | `WallpaperBlock`, `RPC_PickupWallpaperStart_Patch` |
| TCUpgrade.upgrade.nocost | Players | No upgrade cost | `UpgradeBlock` |
| TCUpgrade.repair.nocost | Players | No repair cost | `RepairBlock` |
| TCUpgrade.reskin.nocost | Players | No reskin cost | `ReskinBlock` |
| TCUpgrade.tcskinchange | Players | TC skin menu | TCSKIN |
| TCUpgrade.tcskindeployed | Players | Auto TC skin on deploy | `Analytics_OnEntityBuilt_Patch` |
| TCUpgrade.authlist | Players | Auth list menu | AUTH, REMOVEAUTH |
| TCUpgrade.admin | Admins | wphammer, addwp, DELCUSTOMWP | `CmdWphammer`, `CmdAddwp` |
| TCUpgrade.autolock | Players | Auto key lock on TC deploy | `Analytics_OnEntityBuilt_Patch` |
| TCUpgrade.autocodelock | Players | Auto code lock on TC deploy | `Analytics_OnEntityBuilt_Patch` |
| TCUpgrade.wallpaper.custom | Players | Custom wallpaper skins (addwp) | `GetWallpaperItems` |

---

## 6. Harmony Patches

| Patch | Target | When | Why |
|-------|--------|------|-----|
| `PlayerLoot_StartLootingEntity_Patch` | `PlayerLoot.StartLootingEntity` | Postfix | On TC looting started → `OnLootStarted` → Show upgrade/repair buttons |
| `StorageContainer_PlayerStoppedLooting_Patch` | `StorageContainer.PlayerStoppedLooting` | Postfix | On TC looting stopped → `OnLootEnded` → Destroy UI |
| `BuildingPrivlidge_Spawn_Patch` | `StorageContainer.ServerInit` | Postfix | TC spawn (skinID=0) → `UpdateBlockedItems` |
| `RPC_PickupWallpaperStart_Patch` | `BuildingBlock.RPC_PickupWallpaperStart` | Prefix | Refund cloth for `TCUpgrade.wallpaper.nocost`; then `RemoveWallpaper`; return false (skip vanilla) |
| `Hammer_DoAttackShared_Patch` | `Hammer.DoAttackShared` | Postfix | Wallpaper hammer (skin 3494416562) on floor/foundation → rotate wallpaper |
| `Analytics_OnEntityBuilt_Patch` | `Analytics.Azure.OnEntityBuilt` | Postfix | TC built → TCSkinReplace or add auto lock (autolock/autocodelock) |

---

## 7. Command Surface

### SENDCMD (ConsoleSystem, player-initiated)

| Command | Args | Permission | Side effects |
|---------|------|------------|--------------|
| MENU | — | — | Show upgrade menu |
| PAGE | page | — | Paginate menu |
| CLOSE | — | — | Close menu, refresh buttons |
| UPGRADE | id grade skinId page color | TCUpgrade.upgrade | Start/stop upgrade coroutine |
| REPAIR | — | TCUpgrade.repair | Toggle repair coroutine |
| STOP | page | — | Stop upgrade/wallpaper/upwall |
| COSTUPGRADE | id grade skinId page | — | Show cost tip |
| EFFECT | page | — | Toggle Effect (UI only) |
| DOWNGRADE | page | — | Toggle Downgrade mode |
| RESKIN | id grade skinId page [color] | TCUpgrade.reskin | Start/stop reskin |
| UPWALL | id grade skinId page | TCUpgrade.upwall | Start/stop wall reskin |
| WALLPAPER | id grade skinId page [cat] | TCUpgrade.wallpaper | Show wallpaper menu |
| WALLPAPERSELECT | … skinId category | — | Select wallpaper skin |
| WALLPAPERON | id grade skinId page wallpall category | TCUpgrade.wallpaper | Start/stop wallpaper |
| WALLPAPERSIDES | … wpExt wpInt category | — | Toggle internal/external |
| DELCUSTOMWP | skinId category page | TCUpgrade.admin | Remove custom wallpaper |
| AUTH | page | TCUpgrade.authlist | Show auth list |
| REMOVEAUTH | page … uid | TCUpgrade.authlist | Remove player from TC auth |
| TCSKIN | page | TCUpgrade.tcskinchange | Show TC skin menu |
| TCSKINSELECT | shortName page | — | Apply TC skin, destroy/recreate TC |
| COLOR | id grade skinId col page | — | Show color picker |
| COLORSELECT | id grade skinId col page | — | Set color, refresh |
| CLOSE2 | page | — | Close color/skin menu, back to main |

### Console commands

| Command | Permission | Purpose |
|---------|------------|---------|
| `wphammer` [player] | TCUpgrade.admin | Give wallpaper hammer (skin 3494416562) to self or target |
| `addwp <skinId> <Wall\|Floor\|Ceiling>` | TCUpgrade.admin | Add custom wallpaper skin to category; persists to data.json |

---

## 8. Lifecycle & State Machine

1. **OnLoaded:** Load config, load data, register SENDCMD (and add to Replicated list), wphammer, addwp; iterate TCs → UpdateBlockedItems for skinID=0.
2. **OnUnloaded:** Destroy UI for all players; stop all coroutines in `_buildingCupboard`; clear `_buildingCupboard`; remove SENDCMD from Replicated list and Dict/GlobalDict; remove wphammer/addwp; set `Instance = null`.
3. **Loot started:** Create TCConfig, show buttons.
4. **Loot ended:** Destroy UI panels (TCUpgrade.buttons, TCUpgrade.upgrade, TCUpgrade.color, TCUpgrade.tcskin, TCUpgrade.authlist).
5. **Save points:** Config saved on first load from oxide or when Items empty; Data saved on addwp.

**Invariants:** TCConfig exists only while TC is in `_buildingCupboard`; coroutines must be stopped before TC removal or unload.

---

## 9. External API Surface

- **Public methods:** `HasPermission`, `GetOrCreateConfig`, `OnLootStarted`, `OnLootEnded`, `StopCoroutinesForPlayer`, `UpdateBlockedItems`, `GetPlayerSelectedSkin`, `SetPlayerSelectedSkin`, `TCSkinReplace`.
- **Calls other plugins:** NoEscape `IsRaidBlocked`, RaidBlock `IsRaidBlocked` (via reflection when config enabled).
- **No `API_*` methods.** Not intended as an extension point.

---

## 10. UI / CUI Behavior

**TC button bar (Upgrade, Repair, Auth):** Each button shows an item icon (wood, hammer, wooden box) and turns red only when *that* action is active—Upgrade red when upgrading, Repair red when repairing. Opening the wallpaper menu closes the upgrade menu.

| Panel ID | When created | When destroyed | Purpose |
|----------|--------------|----------------|---------|
| TCUpgrade.buttons | OnLootStarted (TC) | OnLootEnded, CLOSE | Upgrade + Repair + Auth buttons (with wood/hammer/box icons) |
| TCUpgrade.upgrade | MENU, PAGE | CLOSE, STOP, other menus | Main upgrade/reskin/wallpaper menu |
| TCUpgrade.color | WALLPAPER, COLOR | CLOSE2, WALLPAPERSELECT | Wallpaper picker / color picker |
| TCUpgrade.tcskin | TCSKIN | TCSKINSELECT, CLOSE2 | TC skin selection |
| TCUpgrade.authlist | AUTH | CLOSE, REMOVEAUTH | Authorized players list |

**Button commands:** All CUI buttons use `cui.endtest TCUPGRADE action [args]` (AdminMenu/TeleportGUI pattern). Clients only forward ConsoleGen commands; `Cui_Endtest_Patch` intercepts the `TCUPGRADE` marker and runs the handler. Bare `SENDCMD` is not forwarded by the client. `CUIHelper.NormalizeButtonCommand` rewrites legacy `cui.endtest SENDCMD` / bare `SENDCMD` forms to the bridge. The server also registers `SENDCMD` for direct F1 use.

**Throttling:** None. UI is driven by CUI commands and loot events. **Suppression:** RaidBlock/NoEscape blocks upgrade/repair/reskin/wallpaper actions; permissions gate menu visibility.

---

## 11. Gameplay / World Interaction

- **Upgrade:** `UpgradeBlock` — set grade, skin, color; take resources from TC; `SetGrade`, `UpdateSkin`, `SetHealthToMax`, `SendNetworkUpdateImmediate`.
- **Repair:** `RepairBlock` — take resources from TC; set `entity.health`; `OnRepairFinished` / `OnRepair`.
- **Reskin:** `ReskinBlock` — change skin/color; optional cost; `UpdateSkin`, `SetCustomColour`.
- **Reskin wall:** `ReskinWall` — `GameManager.server.CreateEntity` new prefab; copy health/lock; kill old; spawn new.
- **Wallpaper:** `WallpaperBlock` — `SetWallpaper` / `RemoveWallpaper`; optional cloth cost.
- **TC skin replace:** `TCSkinReplace` — create new TC entity; `NextTick` to copy auth, inventory, lock, kill old TC.
- **Blocked items:** `UpdateBlockedItems` — modifies `cupboard.inventory.blockedItems` and `onlyAllowedItems` from config.
- **DLC check:** `TCUpgradeHelpers.IsSkinOwnedOrBypass` — blocks skins without DLC unless `Bypass DLC ownership check`.

---

## 12. Non-Obvious Design Decisions

- **ForceBothSides:** CheckWallpaper is no longer patched; the game's `CheckWallpaper()` runs so wallpaper is auto-removed when invalid (e.g. after rotation), avoiding client/server desync. The `forcebothsides` config option remains for compatibility but no longer disables the game check.
- **TC skin replace uses `NextTick`:** TC replacement runs one frame later so parent/attachment/building state is stable before copying auth/inventory and killing old TC.
- **Permission check is config-only:** No Oxide permission system; Admin Steam IDs + string match for `TCUpgrade.use`/`default`. Other perms (e.g. TCUpgrade.repair) are checked but `HasPermission` only returns true for admin or TCUpgrade.use/default.
- **BuildingPrivlidge_Spawn patches StorageContainer.ServerInit:** `BuildingPrivlidge.Spawn` may not resolve in some Harmony/game versions; `StorageContainer.ServerInit` is the reliable hook for TC init.
- **Wallpaper hammer:** Special hammer skin (3494416562) rotates wallpaper on floor/foundation; no permission beyond hammer ownership.
- **CUI and replicated commands:** CUI buttons use `cui.endtest SENDCMD action [args]` so the client always has the vanilla replicated command; `Cui_Endtest_Patch` handles SENDCMD and buttons work for all players (including after mod reload). The mod also registers `SENDCMD` and adds it to the Replicated list for direct F1 use.

---

## 13. What NOT to Touch Without Care

- **TCConfig / coroutine lifecycle:** Stopping coroutines must happen before clearing `_buildingCupboard`; `Player` field ties work to operator.
- **TCSkinReplace:** Order of operations (spawn → NextTick → copy auth/inventory/lock → kill) is critical. Changing it can orphan inventory or break building attachment.
- **RPC_PickupWallpaperStart_Patch:** Returns `false` to skip vanilla; any logic change must still handle pickup/refund correctly.
- **IsRaidBlocked reflection:** NoEscape/RaidBlock calls use reflection; plugin names and method signatures must match.

---

## 14. Performance Notes

- **Entity iteration:** `BaseNetworkable.serverEntities.OfType<BuildingPrivlidge>()` used on load for UpdateBlockedItems; runs once at startup.
- **Vis.Entities** in `ReskinProgressWall` for walls in radius; consider radius limit from config.
- **No `serverEntities` iteration** in per-tick or per-action paths; `_buildingCupboard` is keyed by TC reference.

---

## Build & Migration

```powershell
cd .cursor/HarmonyMods/TCUpgrade
./build.ps1
```

**Output:** `HarmonyMods/TCUpgrade.dll`

**Migration from Oxide Plugin:**

1. Unload Oxide TCUpgrade: `o.unload TCUpgrade`
2. Build and ensure `TCUpgrade.dll` is in `HarmonyMods/`
3. Config loads from `oxide/config/TCUpgrade.json` or legacy `TCUpgrade.json` if present, then saves to `HarmonyConfig/TCUpgrade.json`

**Do not run both** the Oxide plugin and Harmony mod—they conflict.

---

## 15. Local Images (HarmonyImages/TCUpgrade)

TCUpgrade loads PNG images from `{server root}/HarmonyImages/TCUpgrade/` and stores them in Rust's **FileStorage** under CommunityEntity. This works without Oxide ImageLibrary or Carbon ImageDatabase.

**Setup:**
1. Run the download script to fetch images from the CDN:
   ```powershell
   .\Bundles\BetterTCImages\download-bettertc-images.ps1
   ```
   Default output: `D:\!RustServer\HarmonyImages\TCUpgrade\`. Override with `-ServerRoot "D:\!RustServer"` for a different server.

2. Optional config override: In `HarmonyConfig/TCUpgrade.json`, set `"Images Path Override"` to a custom path (e.g. `"D:\\!RustServer\\HarmonyImages\\TCUpgrade"`) if your images are in a different location than the server root.

3. **If images still don't show:** By default the mod uses **FileStorage** (local PNGs under `HarmonyImages/TCUpgrade/`). Ensure `"Use URL for menu images"` is **false** so the client receives textures via `CL_ReceiveFilePng` (client requests from CommunityEntity, server sends from FileStorage). If images cause AddUI NullRef/kick, set `"Image URL Base"` to a base URL (e.g. `"https://yourserver.com/tcupgrade/"`) and `"Use URL for menu images"` to true; place files: wood.png, stone.png, metal.png, legacywood.png, gingerbread.png, adobe.png, brick.png, brutalist.png, container.png, jungle.png, spacetest.png, armored.png.

4. Expected structure:
   - `HarmonyImages/TCUpgrade/` — lock5.png, upgrade.png, no.png, legacywood.png, gingerbread.png, adobe.png, brick.png, brutalist.png, container.png, jungle.png, spacetest.png
   - `HarmonyImages/TCUpgrade/colours/` — 0.png through 16.png

5. On load, the mod reads these files, calls `FileStorage.server.Store(bytes, FileStorage.Type.png, CommunityEntity.net.ID)`, and maps image keys to FileStorage CRC IDs. Item icons in the upgrade menu use these when config `Img Icon` matches a local file. Default Items are configured with the correct `Img Icon` (e.g. legacywood, gingerbread, adobe, brick, brutalist, container, jungle, spacetest) so the Building Auto Upgrade UI shows the right image per skin. Existing configs get missing `Img Icon` filled automatically on load (migration).

**CUI integration:** The `png` property in `UnityEngine.UI.RawImage` expects a **uint texture ID** from FileStorage. Images must be stored with `CommunityEntity.net.ID` so the client's `SV_RequestFile` (to CommunityEntity) resolves them; the server responds via `CL_ReceiveFilePng`. See `## CUI (Community UI) Reference Files.md` — "Native Rust Image Storage System" and "Url vs Png for RawImage".

---

## References

- `HARMONY_MODS_GUIDE.md` — Harmony architecture, loading, Oxide access
- `.cursor/rules/## CUI (Community UI) Reference Files.md` — CUI JSON format, FileStorage, png/url usage
