# Shop Harmony Mod (2.4.201)

Oxide-free Harmony port of **Shop 2.4.201** (Grimm530 / Mevent). Exact logic replica; hosting uses HarmonyConfig / HarmonyData / Permissions / Economics.

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/Shop.json` |
| Data | `HarmonyData/Shop/` (Shops, Players, UI, Limits, Cooldown, …) |
| Logs | `HarmonyData/Shop/logs/` |
| Lang | `HarmonyLanguage/` (optional overrides) |

## Dependencies (load order)

1. **0Permissions.dll**
2. **Economics.dll** (balance Deposit / Withdraw / Balance hooks)
3. **PlayerDLCAPI.dll** (strongly recommended for paid item/skin ownership)
4. **Kits.dll** (optional — kit item grants via `GiveKit`)
5. **Shop.dll**

```text
harmony.load 0Permissions
harmony.load Economics
harmony.load PlayerDLCAPI
harmony.load Kits
harmony.load Shop
```

## Permissions

- `shop.admin`, `shop.free`, `shop.setvm`, `shop.setnpc`, `shop.bypass.dlc`
- Plus any permission from `HarmonyConfig/Shop.json` (`Permission to use plugin`) and per-category / discount keys

## Commands

Chat (from config `Commands`, this server uses `s` / `shops`):

- `/s`, `/shops` — open shop
- `/shop.setvm`, `/shop.setnpc` — admin NPC/VM bind
- `/shop.install` — installer UI

Undotted chat aliases (`s`, `shops`, `shop`) are registered as **unreplicated** server console commands. Player chat (`/s`) is handled by `ChatSayBridge` on `chat.say`. They must **not** be added to `Index.Server.Replicated` — that causes client `Replicated convar not found` spam on join. Dotted admin commands stay unreplicated.

Console: `UI_Shop`, `shop.item`, `shop.wipe`, `shop.reset`, `shop.manage`, `shop.discordtest`, `shop.horse`, …

## Horse spawn / ownership limit

Config section `Horse Limits` in `HarmonyConfig/Shop.json` (default max **4** owned horses by `OwnerID`).

**Shop product (Command type):**

```text
shop.horse "Horse" %steamid%
```

Optional third arg overrides refund amount when over limit (defaults to `Horse Refund Amount`):

```text
shop.horse "Horse" %steamid% 75
```

Behavior:

- Under limit: spawns `ridablehorse` near the player on NavMesh, sets `OwnerID`
- At limit: blocks spawn and refunds via active economy (`Deposit`)
- Hitching-post claim (`SERVER_Claim`) also blocked at the same ownership limit

**Shop data:** Horse category Command products must use `shop.horse` (not `animalspawn.horse`). `animalspawn.horse` is registered as a compatibility alias.

## Build

```powershell
.\.cursor\HarmonyMods\Shop\build.ps1
```

Copies only `Shop.dll` to `HarmonyMods/`.

## Notes

- CUI buttons are rewritten to `cui.endtest SHOP …` (same pattern as Kits).
- Custom vending machines use `PlayerLoot.StartLootingEntity` prefix.
- PlayerDLCAPI binds through `PlayerDlcApi_ApiType` and is refreshed on each
  purchase ownership check, including after `harmony.reload PlayerDLCAPI`.
- Command product `bdgive Easy %steamid% 1` is dispatched to BradleyDrops (`BradleyDrops_ApiType`) so shop purchases grant the signal.
- Command product `cht.openshop %steamid%` closes Shop UI and calls CHT via `CHT_ApiType` (heli menu lives on OverlayNonScaled).
- Other optional Oxide plugins (ServerPanel, Notify, NoEscape, Duel) remain
  stubs unless a Harmony equivalent is wired later.
