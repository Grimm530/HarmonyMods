# BetterBackpack

Harmony mod that adds **Existing** and **Retrieval** buttons/commands to vanilla Rust backpacks (small and large). Harmony-only; no Oxide plugin.

Retrieval uses **ItemRetriever** the same way the virtual Backpacks mod does (`API_AddSupplier`). Do not also inject backpack items into main inventory.

## Mod Identity

| Item | Value |
|------|-------|
| **Purpose** | Extend vanilla backpack with auto-stack (Existing) and craft/reload from backpack (Retrieval via ItemRetriever) |
| **Entry point** | `BetterBackpackMod` implements `IHarmonyModHooks` |
| **Authorization** | All players |

## Features

| Button / command | Default | Behavior |
|------------------|---------|----------|
| **Existing** `/existing` | ON (config) | After crate/world/harvest loot **lands in main/belt**, if that item already exists in the worn Rust backpack or virtual Backpacks bag, yank it there (stack, or a new slot if the stack is full). No space → leave in inventory. Does **not** yank splits, slot swaps, or items dragged out of a backpack (virtual gather off is irrelevant — this is `/existing`). Crate loot is vanilla: full main means the item stays in the crate. |
| **Retrieval** `/retrieval` | ON (config) | Crafting, construction, workbench, and gun reload use items in the worn backpack through ItemRetriever |

## ItemRetriever

Requires **ItemRetriever.dll** (no load-order requirement; binds via `ItemRetriever_ReadyCallbacks`).

| Piece | Role |
|-------|------|
| `API_AddSupplier("BetterBackpack", spec)` | Find / sum / take / ammo / `SerializeForNetwork` from the worn backpack when Retrieval is ON for that player. Network copies use **fake UIDs** and are omitted while the player is dead so death bags do not show unlootable backpack dupes. |
| Unsearchable flag (bit 25) | Stops ItemRetriever from **also** walking `Flag.Backpack` children on wear (that would double-count and ghost items) |

On unload, the supplier is removed and the unsearchable flag is cleared.

## Project Structure

| File | Responsibility |
|------|----------------|
| `BetterBackpackMod.cs` | Lifecycle, prefs, ItemRetriever bind, chat commands |
| `BetterBackpackConfig.cs` | Config load/save |
| `ItemRetrieverBinder.cs` | AppDomain bind to ItemRetriever (same keys as Backpacks) |
| `ItemRetrieverSupplier.cs` | Worn-backpack supplier + unsearchable flag |
| `Patches/Item_MoveToContainer_Patch.cs` | Existing: mark world/loot pickups that target main/belt (no destination hijack) |
| `Patches/PlayerInventory_OnItemAddedOrRemoved_Patch.cs` | Existing: gather into backpack after a successful inventory add |
| `Patches/ItemContainer_MarkDirty_Patch.cs` | Retrieval: mark main dirty so ItemRetriever re-sends supplier items |
| `Patches/BasePlayer_PlayerInit_Patch.cs` | Force main sync on spawn so craft/reload see backpack items |
| `Patches/ServerMgr_Update_Patch.cs` | Process deferred Existing moves |
| `Patches/Chat_Say_Patch.cs` | `/existing` and `/retrieval` |

## What was removed

These duplicated ItemRetriever and caused ghost/dupe items when taking things out of the worn bag:

- Injecting backpack item UIDs into `UpdateItemContainer` (main inventory)
- Postfix `GetAmount` / `Take` / `FindItem*` / `FindAmmo`
- Crafting `CollectIngredient` / `DoesHaveUsableItem` / `DoesHaveOKConditionItem`

## Load order

No forced order. Typical boot is alphabetical (`Backpacks` → `BetterBackpack` → `ItemRetriever`). BetterBackpack registers an ItemRetriever ready callback and binds when ItemRetriever loads.

```
harmony.load 0Permissions
harmony.load ItemRetriever
harmony.load BetterBackpack
```

Manual load order does not matter as long as both DLLs end up loaded.

## Config

Path: `HarmonyConfig/BetterBackpack.json`

- **Existing** / **Retrieval** master switches and per-player defaults
- Chat notifications / reminder
- **Loot Debug**: empty Steam IDs = all players. Duration minutes auto-off (0 = until you set the flag false and reload). Grep `[BetterBackpack:Loot]`. Successful stack-merges log as `stacked-away` (the source UID is destroyed after its amount is added to an existing stack) — not `INVALID` / `GONE` / `dest=world/none`.
- CUI parent/anchors (legacy; current build uses chat commands)

## Build & Deploy

```powershell
.\build.ps1
```

Copies **only** `BetterBackpack.dll` to root `HarmonyMods/`. Load: `harmony.load BetterBackpack`.
