# StackManager Dictionary

**Persistent reference dictionary for AI models** when modifying, extending, or debugging the StackManager Harmony mod.

---

## 1) Mod Identity

| Attribute | Value |
|-----------|-------|
| Product | StackManager |
| Assembly | StackManager.dll |
| Author | Grimm530 [discord: Grimm530] |
| Version | 02.15.2026 (AssemblyInformationalVersion) |
| Type | Oxidation Harmony Mod |

**Purpose:** Modifies item stack sizes globally at server startup by multiplying vanilla `ItemDefinition.stackable` values per category or per item.

**Primary responsibilities:**
- Multiply stack sizes by configurable multipliers (per `ItemCategory` or per item shortname)
- Blacklist certain items from modification (e.g., water)
- Run immediately after `ItemManager.Initialize` via Harmony transpiler
- Roll back all changes on mod unload
**Precedence (highest → lowest):**
1. **Blacklist** — items are never modified; Category and ItemExact ignored
2. **ItemExact** — exact stack size; Category and Item multiplier ignored
3. **Category** — multiplier applied if not blacklisted and not in ItemExact
4. **Item** — per-item multiplier (overrides Category only; not used if in ItemExact)

---

## 2) Runtime Topology (Architecture Overview)

| Component | Role |
|-----------|------|
| `Harmony` (IHarmonyModHooks) | Entry point; calls `Stacker.Initialize` on load, `Stacker.Kill` on unload |
| `Stacker` | Applies/rolls back stack multipliers to `ItemManager.itemList` |
| `Settings` | Loads/saves `DefaultConfig` from `HarmonyConfig/StackManager.json` |
| `Bootstrap_StartupShared` | Harmony transpiler: injects `Stacker.Initialize` immediately after `ItemManager.Initialize` |
| `Properties` | Reads assembly attributes (Product, Version, Copyright) for logging |

**State flow:**
- Config → `Settings.LoadConfig()` → `DefaultConfig` (in memory)
- `Stacker.Initialize()` reads config and mutates `ItemManager.itemList` in place
- `Stacker.Kill()` reverses mutations using same config (divides by multiplier)

**Dependencies:**
- `Rust.Harmony` (IHarmonyModHooks)
- `0Harmony` (HarmonyLib)
- `Assembly-CSharp` (Rust game types)
- `Newtonsoft.Json` (config serialization)

---

## 3) Persistent Data Model

**None.** The mod does not persist player data or any runtime state. Only the config file is persisted.

---

## 4) Configuration Schema

**File:** `HarmonyConfig/StackManager.json`  
**Legacy migration:** If file missing and `HarmonyMods/StackManager.json` exists, it is copied to the new location.

| Field | Type | Default | Behavioral impact |
|-------|------|---------|-------------------|
| `Blacklist` | `HashSet<string>` | (see Recommended Blacklist) | Items skipped in category loop; if an item is in both Blacklist and `Item`, it will be modified by the Item override (edge case) |
| `Category` | `Dictionary<ItemCategory, float>` | All categories 1f | Multiplier applied to `item.stackable` for items in that category; `Mathf.CeilToInt(stackable * multiplier)` |
| `Item` | `Dictionary<string, float>` | `{"explosive.timed": 1f}` | Per-item multiplier; checked **after** category filter; item must match shortname exactly |
| `ItemExact` | `Dictionary<string, int>` | (see example) | **Second precedence** (after Blacklist). Sets exact stack size; blacklisted items are skipped. Overrides Category and Item. Originals stored for rollback. |

**Processing order (Stacker):**
1. **ItemExact:** For each item in `ItemExact` (skip if blacklisted): store original, set exact value
2. **Category:** If `item.category == category` AND not in Blacklist AND not in Item AND not in ItemExact → apply multiplier
3. **Item:** If in `Item` dict AND not in ItemExact (and implicitly not blacklisted) → apply multiplier

**Constraints:**
- Multipliers are floats; result is `Mathf.CeilToInt` so stacks are always integers
- Zero or negative multipliers would produce nonsensical stacks
- ItemExact values are used as-is; use positive integers (e.g. `100000` for 100k stacks)
- Rollback uses division by the same multiplier; rounding during apply can cause minor drift on rollback if multiplier ≠ 1

**Load/write:** `JsonConvert.DeserializeObject` / `File.WriteAllText` with `Formatting.Indented`. Config path: `Path.Combine(Application.dataPath, "..", "HarmonyConfig", "StackManager.json")` (server root). Config is loaded once at `Stacker.Initialize`; no hot-reload.

**Recommended Blacklist (items that break or cannot stack):** Certain items should not have their stack size modified; stacking them causes bugs or game-breaking behavior. Use the following list when extending or validating the `Blacklist`:

| Shortname | Notes |
|-----------|-------|
| `water` | Liquid container; stacking breaks mechanics |
| `water.salt` | Liquid container; stacking breaks mechanics |
| `blueprintbase` | Blueprint items |
| `flare` | |
| `generator.wind.scrap` | |
| `battery.small` | |
| `building.planner` | |
| `door.key` | Key items |
| `map` | |
| `note` | |
| `hat.candle` | Attire with special behavior |
| `hat.miner` | Attire with special behavior |
| `skull.trophy` | Trophy items |
| `skull.trophy.table` | Trophy items |
| `skull.trophy.jar2` | Trophy items |
| `skull.trophy.jar` | Trophy items |
| `head.bag` | |

---

## 5) Permissions & Authorization Matrix

**Not applicable.** No permissions, player commands, or permission checks.

---

## 6) Hooks & Event Handling

| Hook | When | Purpose | Subsystem |
|------|------|---------|-----------|
| `IHarmonyModHooks.OnLoaded` | Mod load | Calls `Stacker.Initialize`, logs product/version | Entry |
| `IHarmonyModHooks.OnUnloaded` | Mod unload | Calls `Stacker.Kill`, logs unload | Cleanup |
| `Bootstrap.StartupShared` (transpiler) | Game startup, before items usable | Inject call to `Stacker.Initialize` immediately after `ItemManager.Initialize` | Stack patching |
**No timers, no NextTick.**

**Side effects:**
- `Stacker.Initialize`: Mutates `ItemDefinition.stackable` for every item in `ItemManager.itemList` (in-place)
- `Stacker.Kill`: Reverses those mutations (divides by same multiplier)

---

## 7) Command Surface (Chat + Console)

**StackManager Harmony mod:** No commands (console registration does not work reliably from Harmony mods in this setup).

**Oxide plugin:** `oxide/plugins/StackManagerCommands.cs` provides:
- `stackmanager.listweapons` — Lists all item shortnames in the Weapon category to console. Works from server console and F1.

---

## 8) Lifecycle & State Machine

| Phase | Actions |
|-------|---------|
| **Harmony mod load** | `Harmony.OnLoaded` → `Stacker.Initialize` (loads config if not loaded, applies multipliers to `ItemManager.itemList`) |
| **Game bootstrap** | `Bootstrap.StartupShared` runs; transpiler injects `Stacker.Initialize` after `ItemManager.Initialize`. If mod loads after bootstrap, `OnLoaded` already ran and `Stacker.Initialize` is a no-op if `Initialized` is true |
| **Mod unload** | `Harmony.OnUnloaded` → `Stacker.Kill` → divides all stack values back by multipliers |

**Invariants:**
- `Stacker.Initialized` guards double-initialization; `Stacker.Initialize` returns early if already run or if `ItemManager.itemList == null`
- `Stacker.Kill` returns early if `!Initialized`
- Config is loaded exactly once per `Stacker.Initialize`; `Settings.Loaded` prevents reload
- Rollback must use the same config that was used for apply (division by original multiplier)

---

## 9) External API Surface

**None.** No `API_*` methods or public API for other plugins. No plugin references.

---

## 10) UI / CUI / Networking Behavior

**Not applicable.**

---

## 11) Gameplay / World Interaction

- **No entity spawn/kill**
- **No direct inventory modification at runtime** — stack sizes are changed in `ItemDefinition` at startup; all subsequent item creation uses the modified values automatically
---

## 12) Non-Obvious Design Decisions

- **Transpiler vs. postfix:** `Stacker.Initialize` must run immediately after `ItemManager.Initialize` because item definitions are only valid then. A postfix on `ItemManager.Initialize` would also work, but the transpiler guarantees ordering.
- **Double init guard:** Both `OnLoaded` and the transpiler could call `Stacker.Initialize`; the `Initialized` flag prevents applying multipliers twice (which would compound them).
- **Blacklist > ItemExact > Category > Item:** Blacklist is checked first; blacklisted items are never modified. ItemExact skips blacklisted items. Category and Item loops skip both blacklisted and ItemExact items.
- **Blacklist checked in both loops:** Items in Blacklist are skipped in category loop; Item loop only processes items in `Item`, so blacklisted items there would still get modified — but typically Blacklist and Item don't overlap.
- **Rollback divides:** Original `stackable` is not stored; rollback divides current value by the multiplier. Rounding from `CeilToInt` can cause off-by-one in edge cases.
- **Config migrate:** Old path `HarmonyMods/StackManager.json` is migrated to `HarmonyConfig/StackManager.json` to align with newer Oxidation conventions.

---

## 13) What NOT to Touch Without Care

- **`ItemManager.itemList` iteration order:** Logic assumes items can be iterated twice (category, then item overrides). Changing loop order or structure could break override precedence.
- **`Stacker.Kill` division:** Must use exact same multipliers as apply. If config is changed between init and kill (e.g., reload), rollback will produce wrong values.
- **Transpiler target:** Patching `Bootstrap.StartupShared` and targeting `ItemManager.Initialize` is fragile if Facepunch changes bootstrap or initialization order.
- **Config path:** `HarmonyConfig` and `StackManager.json` are hardcoded in `Settings`; changing breaks migration and load.

---

## 14) Performance Anti-Patterns to Avoid (CRITICAL)

**Entity Lookup Anti-Patterns:** Not directly applicable (no entity lookups), but for any future entity-related code:

1. **NEVER iterate through `BaseNetworkable.serverEntities`** — use `Find(NetworkableId)` when you have an ID.
2. **NEVER do redundant entity lookups** — if `Find()` fails, don't retry expensive searches.
3. **NEVER use reflection for methods in the same plugin** — call directly.
4. **NEVER invalidate valid cached data** — can cause infinite lookup loops.

**StackManager-specific:**
- `ItemManager.itemList` iteration is O(n) per category + O(n) for item overrides; acceptable at startup (single run). Do not add per-tick or per-player iteration.

---

## Build & Deployment

| Item | Value |
|------|-------|
| Build | `dotnet build -c Release` (or `build.ps1`) |
| Output | `bin/Release/net48/StackManager.dll` |
| Deploy | Copy to `HarmonyMods/StackManager.dll` |
| Load | `harmony.load StackManager` (or automatic on server start) |
