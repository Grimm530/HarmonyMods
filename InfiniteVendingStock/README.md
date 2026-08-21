# InfiniteVendingStock (Harmony)

Oxide **Infinite Vending Stock 1.0.2** port (no Oxide runtime). NPC vending machines restock to 10,000,000 so remaining stock does not cap the buy amount.

## Deploy

```powershell
.\build.ps1
```

Copies **only** `InfiniteVendingStock.dll` to `HarmonyMods\InfiniteVendingStock.dll`.

Unload the Oxide plugin (`InfiniteVendingStock.cs`) before loading this mod so they do not double-apply.

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/InfiniteVendingStock.json` |

## Features

- Restocks every `NPCVendingMachine` inventory stack after orders are installed, after server init, and on the next tick after a sale.
- Large stock unblocks the in-stock buy slider (vanilla `DoTransaction` already clamps quantity to 1–1,000,000).

## Config

| Field | Default | Effect |
|-------|---------|--------|
| `Enabled` | `true` | Master toggle |
| `NPC vending stock amount` | `10000000` | Amount written onto each vendor inventory item |

## Harmony patches

| Target | Kind | Purpose |
|--------|------|---------|
| `NPCVendingMachine.InstallFromVendingOrders` | Postfix | Restock after vanilla order install (covers spawn + 1s delayed refresh) |
| `VendingMachine.DoTransaction` | Postfix | Restock NPC vendors after a sale (shop UI and drone markets) |

Both patches are postfix observers. They do not skip the original method.

## Overlap

**ServerQoL** already includes this restock when `NPC Vending` → `Restock NPC vending machines` is true. Do not run both unless that ServerQoL option is disabled.

## Permissions

None. Affects all NPC vending machines.
