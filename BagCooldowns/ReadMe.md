# BagCooldowns — Dictionary / Reference

Persistent reference for AI when modifying, extending, or debugging the **BagCooldowns** Harmony mod.

**Mod type:** Harmony mod. Loaded by HarmonyLoader from `HarmonyMods/`. No Oxide lifecycle hooks.

---

## 1) Mod Identity

| Attribute | Value |
|-----------|-------|
| **Name** | BagCooldowns |
| **Author** | Farkas |
| **Type** | Harmony mod |
| **Purpose** | Configurable respawn cooldowns for sleeping bags, beds, beach towels, and campers |

**Primary responsibilities:**
- Load JSON config from `HarmonyConfig/BagCooldowns.json`
- Apply per-type `UnlockSeconds` and `SecondsBetweenReuses` to `SleepingBag` instances (by `RespawnType`)
- Set unlock time on newly spawned bags (skip when loading save to preserve cooldowns across restarts)
- On unload: reset bag timers to vanilla defaults


**Key behavior:** `StaticRespawnArea` is never modified (uses its own unlock logic).

---

## 2) Project Structure & Topology

| Path / component | Purpose |
|------------------|--------|
| `.cursor/HarmonyMods/BagCooldowns/` | Source root |
| `BagCooldowns/HarmonyHooks.cs` | `IHarmonyModHooks` entry — OnLoaded / OnUnloaded |
| `BagCooldowns/HarmonyConfig.cs` | Config load/save, `ConfigData`, `RespawnOption` |
| `BagCooldowns/HarmonyMethods.cs` | `SetBagTimers`, `SetSecondsBetweenReUses`, `SetUnlockTime`, `ResetBagTimers`, `CheckUnlockTime` |
| `BagCooldowns/BaseNetworkable_Spawn.cs` | Harmony Postfix on `BaseNetworkable.Spawn` — apply config to bags |
| `BagCooldowns/Bootstrap_StartupShared.cs` | Harmony Prefix — log "Loaded: BagCooldowns by Farkas" |
| `build.ps1` | Builds Release, copies `BagCooldowns.dll` to `D:\!RustServer\HarmonyMods\` |

**Config path:** `HarmonyConfig/BagCooldowns.json` (relative to server root). Created under `HarmonyConfig/` if missing; default config written on first run.

**State flow:** Config load in OnLoaded → `SetBagTimers()` applies to existing bags; each new bag spawn → Postfix applies config and (if not loading save) sets unlock time.

---

## 3) Persistent Data Model

- **No mod-owned persistent entity data.** Cooldown state lives on the game’s `SleepingBag` (e.g. `unlockSeconds`, `secondsBetweenReuses`).
- **Config only:** `HarmonyConfig/BagCooldowns.json` — see Configuration Schema. Lifecycle: load in `HarmonyConfig.LoadConfig()` at OnLoaded; create directory and default file if missing; on parse error, fall back to default in memory and do not overwrite file.

---

## 4) Configuration Schema

| Field | Type | Default | Behavioral impact |
|-------|------|--------|--------------------|
| **SleepingBag.UnlockSeconds** | float | 150 | Time until bag becomes usable after placement (realtime). |
| **SleepingBag.SecondsBetweenReuses** | float | 150 | Cooldown between respawns at this bag. |
| **Bed.UnlockSeconds** | float | 60 | Same for beds. |
| **Bed.SecondsBetweenReuses** | float | 60 | Same for beds. |
| **BeachTowel.UnlockSeconds** | float | 150 | Same for beach towels. |
| **BeachTowel.SecondsBetweenReuses** | float | 150 | Same for beach towels. |
| **Camper.UnlockSeconds** | float | 150 | Same for campers. |
| **Camper.SecondsBetweenReuses** | float | 150 | Same for campers. |

**Invariant:** `CheckUnlockTime` ensures `unlockSeconds` is not greater than `secondsBetweenReuses`; if so, it clamps unlock time to `realtimeSinceStartup + secondsBetweenReuses`.

---

## 5) Console Commands

None. Mod has no console command surface.

---

## 6) Harmony Patches & Event Flow

| Patch target | Patch type | Purpose |
|--------------|------------|---------|
| **Bootstrap.StartupShared** | Prefix | Log "[Harmony] Loaded: BagCooldowns by Farkas." Returns true (always run original). |
| **BaseNetworkable.Spawn** | Postfix | If instance is a `SleepingBag` and not `StaticRespawnArea`: call `SetSecondsBetweenReUses`; if `!Rust.Application.isLoadingSave`, call `SetUnlockTime` (so cooldowns persist across restarts when loading save). |
**RespawnType mapping (game enum):** 0 = Static, 1 = SleepingBag, 2 = Bed, 3 = BeachTowel, 4 = Camper. Mod uses `(int)RespawnType - 1` as index into config (0–3); Static (0) is skipped.

---

## 7) Lifecycle & State Machine

- **OnLoaded:** `HarmonyConfig.LoadConfig()` → `HarmonyMethods.SetBagTimers()` (apply config to all existing bags).
- **OnUnloaded:** `HarmonyMethods.ResetBagTimers()` — sets vanilla defaults (SleepingBag/BeachTowel/Camper 300, Bed 120) for all non-Static bags.
- **Spawn (runtime):** Postfix on `BaseNetworkable.Spawn` applies config and optionally unlock time; no ordering constraints beyond not running unlock when `isLoadingSave`.

---

## 8) What NOT to Touch Without Care

- **Patch targets:** `BaseNetworkable.Spawn`, `Bootstrap.StartupShared` — method names/signatures can change with Rust version.
- **RespawnType ordinals:** Logic assumes game enum 0=Static, 1=SleepingBag, 2=Bed, 3=BeachTowel, 4=Camper. Any new respawn type must be added in `HarmonyMethods` and `HarmonyConfig.ConfigData`.
- **`Rust.Application.isLoadingSave`:** Used to avoid overwriting unlock time when loading a save; changing this can break cooldown persistence across restarts.
- **StaticRespawnArea:** Must remain excluded from all cooldown logic.
- **Config path:** `HarmonyConfig/BagCooldowns.json` — other tools or docs may assume this path.
- **Vanilla reset values in `ResetBagTimers`:** Must match game defaults if unload is to restore vanilla behavior.

---

## 9) Performance Anti-Patterns

- **Reference:** `.cursor/PluginInstructionalFiles/#System.Linq-Removal.md`, `Rust_Plugin_Performance_Best_Practices.md`.
- This mod does not iterate `BaseNetworkable.serverEntities`; it uses the static list `SleepingBag.sleepingBags` (via reflection in current code). Prefer documented, stable access patterns when refactoring.
- No LINQ in hot paths in current implementation.

---

## Workspace Paths (this project)

| Path | Purpose |
|------|---------|
| `.cursor/HarmonyMods/BagCooldowns/` | BagCooldowns source |
| `HarmonyConfig/BagCooldowns.json` | Runtime config (server root) |
| `HarmonyMods/` (e.g. `D:\!RustServer\HarmonyMods\`) | Deployed Harmony DLLs |
| `bin/Release/net48/BagCooldowns.dll` | Build output |
