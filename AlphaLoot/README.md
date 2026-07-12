# AlphaLoot Harmony Mod

Harmony-based loot table mod. Patches loot spawn methods directly for low-overhead control over containers, Bradley/Heli crates, NPC corpses, and gift boxes. Config lives in `HarmonyConfig/AlphaLoot.json`; data (loot tables, skins) in `HarmonyData/AlphaLoot`.

---

## 1) Mod Identity

| Attribute | Value |
|-----------|-------|
| **Mod name** | AlphaLoot |
| **Type** | Harmony mod (IHarmonyModHooks) |
| **Purpose** | Low-overhead Harmony patches for loot table control |

**Primary responsibilities:**
- Override loot spawning for containers, Bradley/Heli crates, NPC corpses, and gift boxes (unwrap)
- Apply custom loot profiles from JSON config
- Provide admin commands: `al.additems`, `al.search`, `al.repopulateall`
- Auto-generate vanilla loot profiles when missing; optional auto-updater for new game items

**Key feature flags / modes:**
- `OverrideFancyDrop` – when true, supply drops use AlphaLoot `supply_drop` profile instead of vanilla/FancyDrop
- `AutoUpdate` – when true, merges new items from ItemManager into default containers on load

---

## 2) Runtime Topology (Architecture Overview)

| Component | Stores | Invariants |
|-----------|--------|------------|
| `AlphaLootMod.Instance` | Singleton; `_config`, `_storedData`, `_heliData`, `_bradleyData` | Set on `OnLoaded`, nulled on `OnUnloaded` |
| `AlphaLootContext` | Static: `Config`, `WeightedSkinIds`, `ImportedSkinIds`, `BlockedWorkshopSkinIds` | Must be set before any populate; cleared on unload |
| `_storedData` | Main loot profiles (containers, NPCs, unwraps) | Loaded from `ProfileName.json` |
| `_heliData` | Heli crate profiles | Loaded from `HeliProfileName.json` |
| `_bradleyData` | Bradley crate profiles | Loaded from `BradleyProfileName.json` |

**State flow:**
- Config from `HarmonyConfig/AlphaLoot.json`
- Data from `HarmonyData/AlphaLoot/LootProfiles/*.json`
- Skin data from `item_skin_ids.json`

**Dependencies:** `Rust.Harmony`, `0Harmony`, `Assembly-CSharp`, `Newtonsoft.Json`

---

## 3) Persistent Data Model

### `StoredData`
- **Purpose:** Loot profiles per container/NPC type
- **Key fields:**
  - `loot_advanced` / `loot_simple` – container profiles (ShortPrefabName → profile)
  - `npcs_advanced` / `npcs_simple` – NPC corpse profiles (ShortPrefabName → profile)
  - `custom_advanced` / `custom_simple` – custom/event loot profiles
  - `npc_loadouts` – NPC shortname → loadout name list
- **Keys:** Container/NPC shortname (case-insensitive)
- **Storage:** `HarmonyData/AlphaLoot/LootProfiles/<ProfileName>.json`
- **Lifecycle:** Load on `OnLoaded`; Save via `AlphaLootTools.SaveData()` after AddItems or AutoUpdate

### Profile types
- `AdvancedLootContainerProfile` / `SimpleLootContainerProfile` – containers
- `AdvancedNPCLootProfile` / `SimpleNPCLootProfile` – NPC corpses
- `AdvancedCustomLootProfile` / `SimpleCustomLootProfile` – custom spawns

### `AlphaLootConfig`
- **Storage:** `HarmonyConfig/AlphaLoot.json`
- **Not hot-reloadable** – config read only on load

---

## 4) Configuration Schema (CONDENSED)

| Field | Type | Default | Behavioral impact |
|-------|------|---------|-------------------|
| `ProfileName` | string | `"default_loottable"` | Main loot profile filename |
| `HeliProfileName` | string | `"default_heli_loottable"` | Heli crate profile |
| `BradleyProfileName` | string | `"default_bradley_loottable"` | Bradley crate profile |
| `BradleyCrates` | int | -1 | Crates per Bradley (-1 = vanilla) |
| `HelicopterCrates` | int | -1 | Crates per Heli (-1 = vanilla) |
| `GlobalMultiplier` | float | 1f | Multiply all loot amounts |
| `MultiplyUnstackable` | bool | false | Apply multipliers to unstackable items |
| `ContainerOverrides` | Dict<string,string> | {} | Map container shortname → override profile |
| `IgnoreSkinsFor` | HashSet<string> | {} | Item shortnames that skip random skins |
| `UseApprovedSkins` | bool | false | When true, allow paid DLC skins and log the upstream Aug 7 2025 TOS warning. When false, block paid skins from loot tables. |
| `OverrideFancyDrop` | bool | false | Supply drops use `supply_drop` profile |
| `AutoUpdate` | bool | false | Merge new items into loot tables on load |
| `DebugSupplyDrops` | bool | false | Log supply drop contents to console |
| `DebugLootTable` | bool | false | Log all loot spawns (container, profile, multiplier, items) to console |

---

## 5) Permissions & Authorization

| Access | Who | Where enforced |
|--------|-----|----------------|
| `al.additems` | Admin only | `CanUseCommand()` – checks `arg.Player().IsAdmin` |
| `al.search` | Admin only | Same |
| `al.repopulateall` | Admin only | Same |
| `al.skins` | Admin only | Same (`al.skins clear` clears random/per-item skins in loot data) |

Admin status required for console commands. Player chat commands need optional `AlphaLootCommands.cs` Oxide plugin.

---

## 6) Harmony Patches & Event Handling

| Patch | Target | Behavior | Side effects |
|-------|--------|----------|--------------|
| `LootContainer_SpawnLoot_Patch` | `LootContainer.SpawnLoot` | Prefix: if profile found and enabled, clear inventory and populate from profile; return `false` to skip vanilla | Inventory modify, optional `Invoke(SpawnLoot, delay)` for refresh |
| `BradleyAPC_ServerInit_Patch` | `BradleyAPC.ServerInit` | Postfix: set `maxCratesToSpawn` from config | None |
| `PatrolHelicopter_ServerInit_Patch` | `PatrolHelicopter.ServerInit` | Postfix: set `maxCratesToSpawn` from config | None |
| `HumanNPC_ApplyLoot_Patch` | `HumanNPC.ApplyLoot` | Prefix: populate corpse from NPC profile; return `false` to skip vanilla | Corpse container modify |
| `ScarecrowNPC_ApplyLoot_Patch` | `ScarecrowNPC.ApplyLoot` | Same as HumanNPC | Corpse container modify |
| `ItemModUnwrap_ServerCommand_Patch` | `ItemModUnwrap.ServerCommand` | Prefix: if unwrap profile exists, populate player inventory and consume item; return `false` to skip vanilla | Item consume, inventory grant |
| `State_Dead_StartRagdoll_Patch` | Gen2 `State_Dead.StartRagdoll` | Postfix: populate animal corpse from NPC profile | Corpse container modify |
| `State_Dead_StartRagdoll_ScientistNPC2_Patch` | Gen2 scientist `State_Dead.StartRagdoll` | Same population path for ScientistNPC2 ragdolls | Corpse container modify |
| `BaseEntity_DropCorpse_Patch` | `BaseEntity.DropCorpse` | Postfix: capture corpse for Gen2 `State_Dead` path (stack trace check) | Sets `_lastGen2Corpse` |
| `Interface_CallHook_OnCorpsePopulate_Patch` | `Interface.CallHook("OnCorpsePopulate", entity, corpse)` | Postfix: if no Oxide plugin handled the corpse and an NPC profile exists, populate via AlphaLoot and return corpse | Mirrors Oxide AlphaLoot corpse hook behavior |
| `LootFill_DelayFill_Patch` | `LootFill.DelayFill` | Prefix: if profile matches, clear storage and populate (non–`LootContainer` delayed fills) | Inventory modify |
| `LootableCorpse_TakeFrom_ScientistNPC2_Patch` | `LootableCorpse.TakeFrom` | Postfix: when looting ScientistNPC2 corpse, apply NPC loot profile | Corpse container modify |

**Design:** AlphaLoot runs first. Oxide plugins can overwrite loot after AlphaLoot if they hook `OnLootSpawn` / `OnCorpsePopulate`.

---

## 7) Command Surface

| Command | Purpose | Side effects | Failure modes |
|---------|---------|--------------|---------------|
| `al.additems <shortname> [shortname2]...` | Add item(s) to default containers (crate_normal, loot-barrel-1/2, box.wooden) | Modifies `StoredData`, writes JSON | Invalid shortname; no matching container |
| `al.search <shortname>` | List containers/profiles containing the item | None | Invalid shortname |
| `al.repopulateall` | Queue all LootContainers (except HackableLockedCrate) to respawn loot | `CreateInventory`, `Invoke(SpawnLoot)` | Skips HackableLockedCrate |
| `al.skins clear` | Clears weighted random skins and per-item skins in loot JSON (after Steam definitions load); persists via `SaveData` | Rewrites loot profile JSON, updates skin block state | Requires Steam/workshop definitions ready |

All commands require admin. Console or F1; no chat binding in mod.

---

## 8) Lifecycle & State Machine

- **OnLoaded:** Set `Instance`, resolve `_baseDataPath` to `HarmonyData/AlphaLoot` and `_configPath` to `HarmonyConfig/AlphaLoot.json`, load config and data, set `AlphaLootContext`, defer DLC skin block-list creation until `ItemSkinDirectory` and Steam inventory definitions are ready, optionally run `AlphaLootVanillaGenerator` when profiles empty or AutoUpdate enabled, register console commands.
- **OnUnloaded:** Unregister commands, clear `AlphaLootContext`, null `Instance` and data.
- **Save:** Explicit via `AlphaLootTools.SaveData()` (AddItems, AutoUpdate); no periodic auto-save.
- **Invariants:** `AlphaLootContext` must be valid during any populate; patches check `mod != null` before use.

---

## 9) External API Surface

No public API for other mods. Compatible with Oxide plugins (they run after Harmony patches).

---

## 10) UI / CUI / Networking

Not applicable.

---

## 11) Gameplay / World Interaction

- **Containers:** `LootContainer.SpawnLoot` → clear + `PopulateLoot` → optional refresh `Invoke(SpawnLoot, delay)`.
- **Bradley/Heli:** `maxCratesToSpawn` set in `ServerInit`; crates still use `heli_crate` / `bradley_crate` profiles via `LootContainer.SpawnLoot`.
- **NPC corpses:** `HumanNPC.ApplyLoot`, `ScarecrowNPC.ApplyLoot`, Gen2 `State_Dead.StartRagdoll` → `PopulateCorpseLoot` / `profile.PopulateLoot`.
- **Unwrap:** `ItemModUnwrap.ServerCommand` → populate `player.inventory.containerMain`.
- Profile name resolution: `SupplyDrop` → `supply_drop`; `underwater_labs/` prefabs → `underwater_labs/<ShortPrefabName>`; others → `ShortPrefabName`.

---

## 12) Non-Obvious Design Decisions

- **Paths:** Config in `HarmonyConfig/AlphaLoot.json`; data under `HarmonyData/AlphaLoot` (directories created automatically if missing).
- **Supply drop:** Requires `supply_drop` in `loot_advanced` (or `loot_simple`) of main profile; if missing, supply drops fall through to vanilla unless `OverrideFancyDrop` is false (then always vanilla).
- **Gen2 corpses:** Uses `BaseEntity.DropCorpse` postfix + stack trace to detect Gen2 `State_Dead.StartRagdoll`; captures corpse, then `State_Dead_StartRagdoll_Patch` postfix populates it.
- **Skin directory readiness:** `ItemSkinDirectory.Instance` can throw while `assets/skins.asset` is not mounted; startup treats that as "not ready" and waits in `DeferredSkinBlockInitializer` instead of failing `OnLoaded`.
- **Era filtering:** `LootSpawn` filters by `Server.Era`; `RestrictedEras` on slots/entries skip non-matching eras.
- **Profile lookup:** Container overrides applied first; `heli_crate`/`bradley_crate` use `_heliData`/`_bradleyData` with `GetRandomLootProfile`; others use `_storedData`.

---

## 13) What NOT to Touch Without Care

- **`AlphaLootContext`:** Static; must be set before any populate and cleared on unload. Patches assume it is valid.
- **`State_Dead_StartRagdoll_Patch`:** Stack trace logic is fragile; game updates may change call stack.
- **`StoredData` dictionaries:** Use `StringComparer.OrdinalIgnoreCase`; profile names are case-insensitive.
- **`repopulateall`:** Uses `FindObjectsOfType<LootContainer>`; avoid during high entity count.
- **Loot spawn slots:** Modifying `LootSpawnSlots` in memory without saving loses changes on reload.

---

## 14) Performance Anti-Patterns to Avoid

- **Do not iterate `BaseNetworkable.serverEntities`** – Use `Find(NetworkableId)` when ID known.
- **Do not use reflection** for internal logic – Use direct method calls.
- **`repopulateall`** – Iterates all `LootContainer` instances; consider throttling or batching on large servers.

---

## Installation

1. **Console commands** (admin): `al.additems`, `al.search`, `al.repopulateall`, `al.skins` (see `al.skins clear` above).
2. **(Optional)** Load `AlphaLootCommands.cs` Oxide plugin for chat commands and LootCycler: `/aloot`, `al.repopulateall`, hammer a container to preview cycling loot.
3. Build and deploy:
   ```powershell
   .\build.ps1
   ```
4. Restart the server (or use `harmony.load AlphaLoot` if supported).

The mod loads `AlphaLoot.dll` from `HarmonyMods/` automatically on server start.

## Developer source layout (this folder)

| Path | Contents |
|------|----------|
| `AlphaLoot.csproj` | Single net48 project; references `RustDedicated_Data\\Managed\\*.dll` |
| `GlobalUsings.cs` | Project-wide aliases (`Object` → `UnityEngine.Object`, `Random` → `UnityEngine.Random`) so ILSpy-style casts compile cleanly |
| `AlphaLoot.Harmony/` | Core mod: config, `StoredData`, profiles, `AlphaLootMod`, tools, vanilla generator, deferred init |
| `AlphaLoot.Harmony.Patches/` | Harmony patches (`namespace AlphaLoot.Harmony.Patches`) |
| `Properties/AssemblyInfo.cs` | Assembly version metadata |
| `build.ps1` | `dotnet build` then copies `bin\\Release\\net48\\AlphaLoot.dll` → `D:\\!RustServer\\HarmonyMods\\AlphaLoot.dll` |

The same sources also live under `d:\\!RustServer\\AlphaLoot\\` for day-to-day editing; build output and deployment target `HarmonyMods\\AlphaLoot.dll`.

## Config & Data Paths

| Resource | Path |
|----------|------|
| Config | `HarmonyConfig/AlphaLoot.json` |
| Loot profiles | `HarmonyData/AlphaLoot/LootProfiles/*.json` |
| Skin data | `HarmonyData/AlphaLoot/item_skin_ids.json` |
| Auto-updater state | `HarmonyData/AlphaLoot/AutoUpdater/do_not_edit_this_file.json` |

Config is in the config folder; data is under `HarmonyData/AlphaLoot`. Directories are created on first load if missing.

## Supported Features

| Feature | Status |
|--------|--------|
| Loot container profiles (advanced/simple) | ✅ |
| Heli crate profiles | ✅ |
| Bradley crate profiles | ✅ |
| Item unwrap (gift boxes) | ✅ |
| Bradley/Heli crate count | ✅ |
| Container overrides | ✅ |
| Global multiplier, skins | ✅ |
| NPC corpse loot (HumanNPC, ScarecrowNPC) | ✅ |
| Gen2 AI corpses (animals, State_Dead) | ✅ |
| FancyDrop override (OverrideFancyDrop config) | ✅ |
| CustomLootSpawns/EventLoot (plugins can overwrite after) | ⚠️ |
| CanPopulateLoot hook | ❌ (plugins can block via their hooks) |
| Admin commands + LootCycler | ✅ (optional Oxide plugin for chat) |
| Auto-updater, al.additems, al.search, al.skins clear | ✅ (Harmony mod, admin console) |
| `LootFill` storage entities, ScientistNPC2 corpse paths | ✅ |

## Vanilla profile generation

When loot profile files are missing or empty (e.g. after deleting `HarmonyData/AlphaLoot/LootProfiles/*.json`), the mod **automatically generates fresh vanilla loot tables** from the game's prefab definitions, matching vanilla loot layout. Generated files are saved to `HarmonyData/AlphaLoot/LootProfiles/`. Config (multiplier, profile names, etc.) is read from `HarmonyConfig/AlphaLoot.json`.

## Troubleshooting: Duplicate Rare Items from Heli/Bradley

**Symptom:** Player receives multiple copies of a rare item (e.g. 6× M249) from helicopter/Bradley crates, even though the loot table has only 1 such item with `DontMultiply: true` and `NumberToSpawn: 1`.

**Root cause:** Each crate is populated **independently**. The helicopter drops N crates (see `HelicopterCrates` in config; vanilla = 4); each crate runs the loot table separately. `DontMultiply` only limits duplicates *within a single crate*—it does not coordinate across crates. So if 6 crates drop and each rolls the M249 (weight 1 vs ~100+ total in that slot), the player can get 6× M249 total.

**Solutions:**
1. **Reduce crate count** – Lower `HelicopterCrates` or `BradleyCrates` in `HarmonyConfig/AlphaLoot.json` to limit total chances (e.g. 2 crates instead of 4).
2. **Further reduce weight** – Lower the SubSpawn `Weight` for the rare item to make it even rarer per crate.
3. **Remove from heli/bradley** – Remove the item from `default_heli_loottable.json` or `default_bradley_loottable.json` if you want zero chance per crate.

## Troubleshooting: Startup Skin Directory Wait

**Symptom:** Startup logs `Waiting for item skin and Steam inventory definitions before building DLC skin block list...`.

**Expected behavior:** The mod remains loaded and waits for Rust's `ItemSkinDirectory` / Steam inventory definitions before building `BlockedWorkshopSkinIds`. This avoids the `ItemSkinDirectory.Instance` `NullReferenceException` seen when `assets/skins.asset` is not available during early Harmony `OnLoaded`.

**Action:** No action is needed unless the wait repeats indefinitely. If it does, confirm the server content and Steam inventory definitions are loading correctly before running `al.skins clear`.

## Requirements

- Rust dedicated server with HarmonyLoader
- HarmonyData/AlphaLoot folder (mod creates vanilla profiles on first load if empty)

## Build

```powershell
.\build.ps1
```

**Output:** `D:\!RustServer\HarmonyMods\AlphaLoot.dll`
