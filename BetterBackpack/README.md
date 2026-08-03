# BetterBackpack

Harmony mod that adds **Existing** and **Retrieval** buttons to vanilla Rust backpacks (small and large). Harmony-only; no Oxide plugin.

## Mod Identity

| Item | Value |
|------|-------|
| **Purpose** | Extend vanilla backpack with auto-stack (Existing) and craft/reload from backpack (Retrieval) |
| **Entry point** | `BetterBackpackMod` implements `IHarmonyModHooks` |
| **Authorization** | All players |

## Features

| Button | Default | Behavior |
|--------|---------|----------|
| **Existing** | OFF | When looting, if you already have that item in your backpack with room to stack, automatically moves it there |
| **Retrieval** | OFF | Allows crafting, construction, workbench, and **gun reload** to use items stored in the backpack. |

## Project Structure

| File | Responsibility |
|------|----------------|
| `BetterBackpackMod.cs` | Lifecycle, CUI buttons, player preferences |
| `BetterBackpackConfig.cs` | Config load/save, Debug toggle |
| `Patches/Item_ServerCommand_Patch.cs` | Debug: log commands sent to backpack items |
| `Patches/PlayerLoot_StartLootingItem_Patch.cs` | Show buttons when worn backpack opened |
| `Patches/PlayerLoot_AddContainer_Patch.cs` | Show buttons when dropped backpack opened |
| `Patches/PlayerLoot_Clear_Patch.cs` | Destroy buttons when loot closed |
| `Patches/Cui_Endtest_Patch.cs` | Handle button clicks |
| `Patches/PlayerInventory_OnItemAddedOrRemoved_Patch.cs` | Existing: stack pickup leftovers to backpack (external loot/world only) |
| `Patches/Item_MoveToContainer_Patch.cs` | Existing: redirect external→main loot to backpack; mark pickups so inventory transfers are ignored |
| `Patches/ItemCrafter_CollectIngredient_Patch.cs` | Retrieval: crafting from backpack |
| `Patches/ItemCrafter_DoesHaveUsableItem_Patch.cs` | Retrieval: CanCraft check |
| `Patches/ItemCrafter_DoesHaveOKConditionItem_Patch.cs` | Retrieval: condition check for crafting |
| `Patches/PlayerInventory_FindAmmo_Patch.cs` | Retrieval: ammo reload from backpack (FindAmmo, HasAmmo) |
| `Patches/PlayerInventory_FindItemsByItemID_Patch.cs` | Retrieval: ammo item lookup for reload |
| `Patches/PlayerInventory_FindItemByItemID_Patch.cs` | Retrieval: ammo type switch |
| `Patches/PlayerInventory_GetAmount_Patch.cs` | Retrieval: GetAmount includes backpack (crafting UI, ingredient checks) |
| `Patches/PlayerInventory_Take_Patch.cs` | Retrieval: construction/workbench from backpack |
| `Patches/PlayerInventory_GetIdealPickupContainer_Patch.cs` | **Existing: route loot directly to backpack** – when looting crates, items stack into backpack immediately instead of main first |
| `Patches/ItemContainer_MarkDirty_Patch.cs` | **Retrieval: mark main dirty when backpack changes** – ensures crafting UI receives updated backpack data without opening the bag |
| `Patches/BaseEntity_ClientRPC_UpdateItemContainer_Patch.cs` | **Retrieval: inject backpack into main inventory sent to client** – crafting UI and reload see backpack items without client patches |

## Persistent Data Model

- **Config:** `HarmonyConfig/BetterBackpack.json` – Debug toggle, plus **default values** for new players.
- `BetterBackpackMod.PlayerPrefsByUserId`: `Dictionary<ulong, PlayerPrefs>` keyed by `userID`.
- `PlayerPrefs`: `ExistingEnabled`, `RetrievalEnabled`. **New players inherit from config** (default true if config says true). Players can still toggle per-session via the UI buttons.

## Harmony Patches

| Patch | Target | Type | Purpose |
|-------|--------|------|---------|
| `PlayerLoot_StartLootingEntity_Patch` | `PlayerLoot.StartLootingEntity` | Postfix | Show buttons when looting backpack entity (WorldItem, DroppedItemContainer) |
| `PlayerLoot_StartLootingItem_Patch` | `PlayerLoot.StartLootingItem` | Postfix | Show buttons when opening worn backpack (if called) |
| `PlayerLoot_AddContainer_Patch` | `PlayerLoot.AddContainer` | Postfix | Show buttons when backpack container added |
| `PlayerLoot_Clear_Patch` | `PlayerLoot.Clear` | Prefix | Destroy buttons when closing loot |
| `Cui_Endtest_Patch` | `cui.endtest` | Prefix | Handle BETTER_BACKPACK TOGGLE_* commands |
| `PlayerInventory_OnItemAddedOrRemoved_Patch` | `PlayerInventory.OnItemAddedOrRemoved` | Postfix | Existing: auto-stack pickup leftovers (marked external only) |
| `Item_MoveToContainer_Patch` | `Item.MoveToContainer` | Prefix/Postfix | Existing: redirect external loot to backpack; mark pickups |
| `ItemCrafter_CollectIngredient_Patch` | `ItemCrafter.CollectIngredient` | Prefix/Postfix | Add backpack to craft sources when Retrieval on |
| `ItemCrafter_DoesHaveUsableItem_Patch` | `ItemCrafter.DoesHaveUsableItem` | Postfix | Include backpack in ingredient count |
| `ItemCrafter_DoesHaveOKConditionItem_Patch` | `ItemCrafter.DoesHaveOKConditionItem` | Postfix | Include backpack in condition check |
| `PlayerInventory_FindAmmo_Patch` | `PlayerInventory.FindAmmo`, `FindAmmo(List, AmmoTypes)`, `HasAmmo` | Postfix | Include backpack for gun reload |
| `PlayerInventory_FindItemsByItemID_Patch` | `PlayerInventory.FindItemsByItemID` | Postfix | Include backpack for ammo lookup |
| `PlayerInventory_FindItemByItemID_Patch` | `PlayerInventory.FindItemByItemID` | Postfix | Include backpack for item lookup |
| `PlayerInventory_GetAmount_Patch` | `PlayerInventory.GetAmount` | Postfix | Include backpack in item count (crafting UI, etc.) |
| `PlayerInventory_Take_Patch` | `PlayerInventory.Take` | Postfix | Include backpack when taking items |
| `PlayerInventory_GetIdealPickupContainer_Patch` | `PlayerInventory.GetIdealPickupContainer` | Postfix | When Existing on + backpack has matching stack, route loot (crates, etc.) directly to backpack |
| `ItemContainer_MarkDirty_Patch` | `ItemContainer.MarkDirty` | Postfix | When backpack contents change + Retrieval on, mark containerMain dirty so client gets crafting UI sync |
| `BaseEntity_ClientRPC_UpdateItemContainer_Patch` | `BaseEntity.ClientRPC(RpcTarget, UpdateItemContainer)` | Prefix | Inject backpack contents into main inventory sync – client receives backpack items in main, so crafting UI and reload work without client mod |

## UI (CUI)

- **Parent:** Configurable `CUIParent`: `Inventory` (default – auto show/hide with Tab), `Hud`, `Overlay`, or `OverlayNonScaled`. Use `Hud` or `Overlay` if `Inventory` fails (client may log "Unknown Parent").
- **Position:** Anchor-based (0–1). Buttons are placed **above the backpack inventory slots** (where the green placeholder boxes are). Default: anchormin `0.05 0.78`, anchormax `0.28 0.88`. Configurable via `Buttons AnchorMin` / `Buttons AnchorMax` for different resolutions.
- **Panel:** Invisible container (no background). Two buttons side-by-side: **Existing** (left), **Retrieval** (right).
- **Visibility:** With `Inventory` parent, buttons auto show when Tab is open and hide when closed. Also shown when looting backpack entities; destroyed when loot panel closes.
- Commands: `cui.endtest BETTER_BACKPACK TOGGLE_EXISTING`, `cui.endtest BETTER_BACKPACK TOGGLE_RETRIEVAL`.

## Lifecycle

- **OnLoaded:** Set `Instance`, register `BETTER_BACKPACK_CMD`.
- **OnUnloaded:** Destroy UI on all players, clear `PlayerPrefsByUserId`, unregister command, set `Instance = null`.

## What NOT to Touch Without Care

- **Patch targets:** `PlayerLoot`, `ItemCrafter`, `PlayerInventory` method signatures may change by Rust version.
- **SerializeForNetwork (ClientRPC patch):** Backpack contents are injected into the main container data sent to the client. The client receives items in invisible slots (24+). This is required because crafting UI and reload run **on the client** (vanilla, no mod) – server patches alone cannot change client behavior. Without this, the client would never "see" backpack items for crafting/reload.
- **Existing logic:** Only acts on external pickups (world/loot). `Item_MoveToContainer` marks those moves; backpack↔main and other inventory transfers are ignored so items are not pulled back.
- **CollectIngredient:** Backpack is inserted at index 0 and must be removed in Postfix; exception in Prefix could leave it in.

## Backpack command conflict

**BetterBackpack does NOT register a `backpack` command;** it uses **/existing** and **/retrieval** (chat) only. If another mod or plugin registers the console command `backpack` (e.g. the **Backpacks** Harmony mod in `.cursor/HarmonyMods/Backpacks/`), then F1 `backpack` opens that mod’s UI. To use BetterBackpack with the vanilla worn backpack, players open it by **clicking the backpack icon** in the inventory (Tab); no conflict with /existing or /retrieval.

## Config & Debug

- **Config path:** `HarmonyConfig/BetterBackpack.json`.
- **Chat Notifications:** Set `"Chat Notifications (false = no reminder or /existing / /retrieval feedback in chat)": false` to disable all chat output: no periodic reminder and no feedback when players use `/existing` or `/retrieval`. Toggles still work; they just run silently. Default is `true`.
- **On load:** You should see `[BetterBackpack] Mod loaded. Debug=true/false` in the server console.
- **Debug:** Default is `true`. Set `"Debug": true` in `HarmonyConfig/BetterBackpack.json`, then reload (`harmony.reload BetterBackpack`). When enabled, server console logs:
  - `Item.ServerCommand BACKPACK` – when client sends a command to a worn backpack (e.g. clicking the backpack icon)
  - `StartLootingItem` – when `PlayerLoot.StartLootingItem` is called
  - `AddContainer` – when a container is added to loot
  - `PlayerLoot.Clear` – when loot is closed
  - `OnBackpackOpened` / `SendButtons` – when buttons are shown

Use this to trace whether the backpack open flow triggers our patches when you click the backpack icon in the main inventory.

**Buttons not showing?**
- **Trigger required:** Buttons only appear when you **move an item** (drag in Tab inventory) or **click your equipped backpack** or **open a dropped backpack**. Just opening Tab is not enough.
- **CUI parent:** If buttons never appear, try `"CUIParent": "Overlay"` or `"Hud"` in config. `"Inventory"` may not exist in your Rust version. Old config keys are supported for backward compatibility.
- Check server console for `[BetterBackpack.TRACE] SendButtons` when you move an item – if you see it, the mod is sending UI.

**Crafting or reload from backpack not working?**
- Ensure **Retrieval is ON** for your player: type `/retrieval` in chat and confirm it says ON. New players inherit the config default (`Retrieval (craft/build from backpack)` in config).
- If reload still fails, check server console on mod load for `[BetterBackpack] AmmoTypes not found` – if present, server-side ammo lookup from backpack is disabled (client may still see counts via inventory sync).
- After turning Retrieval ON, the mod forces a main-inventory sync so the client sees backpack items; if it was OFF and you had items in the bag, toggling ON once can fix stale client state.

**Sometimes can't hover/drag items into the backpack?**
- Open the backpack by **clicking the backpack icon** in your inventory (Tab), then drag from main/belt into the backpack panel. If the backpack panel didn’t open (vanilla `PlayerLoot.StartLootingItem`), hover/drag can be flaky.
- Try toggling `/retrieval` to force a full inventory sync; avoid having other loot (e.g. a crate) open at the same time when moving items into the bag.

## Build & Deploy

```powershell
.\build.ps1
```

Output: `D:\!RustServer\HarmonyMods\BetterBackpack.dll`. Load: `harmony.load BetterBackpack`.
