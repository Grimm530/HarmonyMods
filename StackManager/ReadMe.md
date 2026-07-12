# StackManager — Dictionary / Reference

Persistent reference for AI when modifying, extending, or debugging the **StackManager** Harmony mod.

**Mod type:** Harmony mod. Loaded by HarmonyLoader from `HarmonyMods/`. No Oxide lifecycle hooks.

---

## 1) Mod Identity

| Attribute | Value |
|-----------|-------|
| **Name** | StackManager |
| **Author** | Grimm530 [discord: Grimm530] |
| **Version** | 02.15.2026 (AssemblyInformationalVersion) |
| **Type** | Oxidation Harmony mod |
| **Purpose** | Modifies item stack sizes globally at server startup by multiplying vanilla `ItemDefinition.stackable` values per category or per item |

**Primary responsibilities:**
- Multiply stack sizes by configurable multipliers (per `ItemCategory` or per item shortname)
- Blacklist certain items from modification (e.g. water, keys)
- Run immediately after `ItemManager.Initialize` via Harmony transpiler
- Roll back all changes on mod unload

**Precedence (highest → lowest):**
1. **Blacklist** — items are never modified; Category and ItemExact ignored
2. **ItemExact** — exact stack size; Category and Item multiplier ignored
3. **Category** — multiplier applied if not blacklisted and not in ItemExact
4. **Item** — per-item multiplier (overrides Category only; not used if in ItemExact)

---

## 2) Project Structure & Topology

| Path / component | Purpose |
|------------------|--------|
| `.cursor/HarmonyMods/StackManager/` | Source root |
| `StackManager.Utility/Harmony.cs` | `IHarmonyModHooks` entry — OnLoaded / OnUnloaded |
| `StackManager.Utility/Settings.cs` | Config load/save, path, migration from old location |
| `StackManager.Helpers/Stacker.cs` | Apply/rollback stack multipliers to `ItemManager.itemList` |
| `StackManager.Patches/Bootstrap_StartupShared.cs` | Harmony Transpiler — inject `Stacker.Initialize` after `ItemManager.Initialize` |
| `StackManager.Config/DefaultConfig.cs` | Config model: Blacklist, Category, Item, ItemExact |
| `StackManager.Utility/Properties.cs` | Assembly attributes (Product, Version, Copyright) for logging |
| `StackManager.Utility/Log.cs` | Log helpers |
| `build.ps1` | Builds Release, copies `StackManager.dll` to `D:\!RustServer\HarmonyMods\` |

**Config path:** `HarmonyConfig/StackManager.json` (relative to server root). If file is missing and `HarmonyMods/StackManager.json` exists, it is copied to the new location (legacy migration).

**State flow:** Config → `Settings.LoadConfig()` → `DefaultConfig` in memory. `Stacker.Initialize()` reads config and mutates `ItemManager.itemList` in place. `Stacker.Kill()` reverses mutations (divide by same multiplier; ItemExact restored from stored originals).

---

## 3) Persistent Data Model

- **No mod-owned persistent player or entity data.** Stack sizes are changed in `ItemDefinition` at startup; all subsequent item creation uses the modified values automatically.
- **Config only:** `HarmonyConfig/StackManager.json` — see Configuration Schema. Lifecycle: load in `Settings.LoadConfig()` at `Stacker.Initialize`; create directory and default file if missing or on parse error; migrate from `HarmonyMods/StackManager.json` if present. No hot-reload.

---

## 4) Configuration Schema

**File:** `HarmonyConfig/StackManager.json`

| Field | Type | Default | Behavioral impact |
|-------|------|--------|-------------------|
| **Blacklist** | `HashSet<string>` | (see Recommended Blacklist below) | Items skipped in category loop; never modified by Category or ItemExact |
| **Category** | `Dictionary<ItemCategory, float>` | All categories 1f | Multiplier applied to `item.stackable` for items in that category; result `Mathf.CeilToInt(stackable * multiplier)` |
| **Item** | `Dictionary<string, float>` | `{"explosive.timed": 1f}` | Per-item multiplier; checked after category; exact shortname match |
| **ItemExact** | `Dictionary<string, int>` | e.g. syringe.medical 25, blood 100000 | Sets exact stack size; overrides Category and Item. Originals stored for rollback. |

**Processing order (Stacker):**
1. **ItemExact:** For each item in `ItemExact` (skip if blacklisted): store original, set exact value.
2. **Category:** If `item.category == category` AND not in Blacklist AND not in Item AND not in ItemExact → apply multiplier.
3. **Item:** If in `Item` dict AND not in ItemExact → apply multiplier.

**Constraints:** Multipliers are floats; result is `Mathf.CeilToInt`. ItemExact values are used as-is (positive integers). Rollback uses division by the same multiplier; rounding can cause minor drift if multiplier ≠ 1.

**Recommended Blacklist (items that break or cannot stack):** `water`, `water.salt`, `blueprintbase`, `flare`, `generator.wind.scrap`, `battery.small`, `building.planner`, `door.key`, `map`, `note`, `hat.candle`, `hat.miner`, `skull.trophy`, `skull.trophy.table`, `skull.trophy.jar2`, `skull.trophy.jar`, `head.bag`.

---

## 5) Console Commands

None. Mod has no console command surface. (A separate Oxide plugin `StackManagerCommands.cs` may provide `stackmanager.listweapons`; not part of this Harmony mod.)

---

## 6) Harmony Patches & Event Flow

| Patch target | Patch type | Purpose |
|--------------|------------|---------|
| **Bootstrap.StartupShared** | Transpiler | Inject call to `Stacker.Initialize` immediately after `ItemManager.Initialize` so stack changes apply as soon as item definitions exist |

**Transpiler detail:** Targets `ItemManager.Initialize`; emits the original call then a call to `Stacker.Initialize`. Ensures ordering without relying on load order.

---

## 7) Lifecycle & State Machine

- **OnLoaded:** Log product/version/copyright → `Stacker.Initialize()` (load config if not loaded, apply multipliers to `ItemManager.itemList`). Double-init guarded by `Stacker.Initialized`.
- **Game bootstrap:** `Bootstrap.StartupShared` runs; transpiler injects `Stacker.Initialize` after `ItemManager.Initialize`. If mod loads after bootstrap, `OnLoaded` already ran; `Stacker.Initialize` is no-op if `Initialized` is true or `ItemManager.itemList == null`.
- **OnUnloaded:** `Stacker.Kill()` — restore ItemExact from `OriginalStackables`, divide Category/Item stacks by multipliers, clear cache; log unload.

**Invariants:** Rollback must use the same config that was used for apply. Config is loaded once per `Stacker.Initialize`; `Settings.ClearCache()` on unload clears config so the next load reads fresh from file.

---

## 8) What NOT to Touch Without Care

- **`ItemManager.itemList` iteration order:** Logic assumes items iterated twice (ItemExact, then Category, then Item). Changing loop order or structure could break override precedence.
- **`Stacker.Kill` division / ItemExact restore:** Must use exact same multipliers and stored originals as apply. If config changes between init and kill, rollback will be wrong.
- **Transpiler target:** Patching `Bootstrap.StartupShared` and targeting `ItemManager.Initialize` is fragile if Facepunch changes bootstrap or initialization order.
- **Config path:** `HarmonyConfig` and `StackManager.json` are hardcoded in `Settings`; changing breaks migration and load.

---

## 9) Performance Anti-Patterns

- **Reference:** `.cursor/PluginInstructionalFiles/#System.Linq-Removal.md`, `Rust_Plugin_Performance_Best_Practices.md`.
- **Entity lookup (if adding entity code):** Do not iterate `BaseNetworkable.serverEntities` for lookup; use `Find(NetworkableId)`. Do not retry with expensive search if `Find()` fails. Do not use reflection for methods in the same mod.
- **StackManager-specific:** `ItemManager.itemList` iteration is O(n) per category + O(n) for item overrides; acceptable at startup (single run). Do not add per-tick or per-player iteration.

---

## Build & Deployment

| Item | Value |
|------|-------|
| Build | `dotnet build -c Release` or `build.ps1` |
| Output | `bin/Release/net48/StackManager.dll` |
| Deploy | Copy to `HarmonyMods/StackManager.dll` (e.g. `D:\!RustServer\HarmonyMods\`) |
| Load | `harmony.load StackManager` or automatic on server start |

---

## Workspace Paths (this project)

| Path | Purpose |
|------|---------|
| `.cursor/HarmonyMods/StackManager/` | StackManager source |
| `HarmonyConfig/StackManager.json` | Runtime config (server root) |
| `HarmonyMods/` (e.g. `D:\!RustServer\HarmonyMods\`) | Deployed Harmony DLLs |
| `bin/Release/net48/StackManager.dll` | Build output |
