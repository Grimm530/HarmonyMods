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

1. **Permissions.dll**
2. **Economics.dll** (balance Deposit / Withdraw / Balance hooks)
3. **Kits.dll** (optional — kit item grants via `GiveKit`)
4. **Shop.dll**

```text
harmony.load Permissions
harmony.load Economics
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

Console: `UI_Shop`, `shop.item`, `shop.wipe`, `shop.reset`, `shop.manage`, `shop.discordtest`, …

## Build

```powershell
.\.cursor\HarmonyMods\Shop\build.ps1
```

Copies only `Shop.dll` to `HarmonyMods/`.

## Notes

- CUI buttons are rewritten to `cui.endtest SHOP …` (same pattern as Kits).
- Custom vending machines use `PlayerLoot.StartLootingEntity` prefix.
- Optional Oxide plugins (ServerPanel, Notify, NoEscape, Duel, PlayerDLCAPI) are stubs unless a Harmony equivalent is wired later.
