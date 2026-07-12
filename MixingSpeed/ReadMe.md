# MixingSpeed — Dictionary / Reference

Persistent reference for AI when modifying, extending, or debugging the **MixingSpeed** Harmony mod.

**Mod type:** Harmony mod. Loaded by HarmonyLoader from `HarmonyMods/`. No Oxide lifecycle hooks.

---

## 1) Mod Identity

| Attribute | Value |
|-----------|-------|
| **Name** | MixingSpeed |
| **Author** | Farkas |
| **Type** | Harmony mod |
| **Purpose** | Configurable mixing-table speed: multiplier for mix duration and optional instant mix |

**Primary responsibilities:**
- Load JSON config from `HarmonyConfig/MixingSpeed.json`
- Patch `MixingTable.StartMixing` via transpiler to alter `RemainingMixTime` (divide by multiplier or set to 0 for instant)
**Key flags:** `HarmonyConfig.Config.InstantMix` (skip duration), `HarmonyConfig.Config.MixingSpeedMultiplier` (divide mix time when not instant).

---

## 2) Project Structure & Topology

| Path / component | Purpose |
|------------------|--------|
| `.cursor/HarmonyMods/MixingSpeed/` | Source root |
| `MixingSpeed/HarmonyConfig.cs` | Config load, `ConfigData` (MixingSpeedMultiplier, InstantMix) |
| `MixingSpeed/Bootstrap_StartupShared.cs` | Harmony Prefix on `Bootstrap.StartupShared` — log "Loaded: MixingSpeed by Farkas" |
| `MixingSpeed/MixingTable_StartMixing.cs` | Harmony Transpiler on `MixingTable.StartMixing` — modify RemainingMixTime |
| `build.ps1` | Builds Release, copies `MixingSpeed.dll` to `D:\!RustServer\HarmonyMods\` |

**Config path:** `HarmonyConfig/MixingSpeed.json` (relative to server root). Directory created if missing; default config written when file absent or on parse error.

**State flow:** No explicit OnLoaded/OnUnloaded; config is loaded inside the `MixingTable.StartMixing` transpiler when mixing runs (lazy). Patches are applied when HarmonyLoader loads the assembly.

---

## 3) Persistent Data Model

- **No mod-owned persistent entity data.** Mix duration is computed at mix start by the game; the transpiler only alters the value passed to `set_RemainingMixTime`.
- **Config only:** `HarmonyConfig/MixingSpeed.json` — see Configuration Schema. Lifecycle: load in `HarmonyConfig.LoadConfig()` from within the transpiler (on each mix, or could be cached); create directory and default file if missing; on parse error, fall back to default in memory.

---

## 4) Configuration Schema

| Field | Type | Default | Behavioral impact |
|-------|------|--------|--------------------|
| **MixingSpeedMultiplier** | float | 2 | Divisor for vanilla mix duration. Final time = vanilla / multiplier. Ignored if InstantMix is true. Must be > 0 to apply. |
| **InstantMix** | bool | false | If true, RemainingMixTime is forced to 0 (instant completion). Transpiler removes 6 IL instructions and replaces the value-holding instruction with Ldc_R4 0. |

**Invariant:** When `InstantMix` is true, the multiplier is not used. When `InstantMix` is false and `MixingSpeedMultiplier > 0`, the value on stack before `set_RemainingMixTime` is divided by the multiplier.

---

## 5) Console Commands

None. Mod has no console command surface.

---

## 6) Harmony Patches & Event Flow

| Patch target | Patch type | Purpose |
|--------------|------------|---------|
| **Bootstrap.StartupShared** | Prefix | Log "[Harmony] Loaded: MixingSpeed by Farkas." Returns true (always run original). |
| **MixingTable.StartMixing** | Transpiler | Locate IL call to `set_RemainingMixTime`. If InstantMix: replace value with 0 and remove 6 preceding instructions (IL-dependent). Else if MixingSpeedMultiplier > 0: insert Ldc_R4 multiplier + Div before the call so stack value becomes value/multiplier. Calls `HarmonyConfig.LoadConfig()` at start of transpiler. |

**Transpiler details:** Index is the position of the `set_RemainingMixTime` call. For instant mix, the instruction at `callIndex - 1` is replaced with `Ldc_R4 0` and six instructions before that are removed (requires `callIndex >= 7`). For multiplier, two instructions (load multiplier, Div) are inserted at `callIndex`.

---

## 7) Lifecycle & State Machine

- **Load:** HarmonyLoader applies patches when the DLL is loaded. No `IHarmonyModHooks` entry point; patches are discovered by convention.
- **First mix (runtime):** Transpiler runs → `HarmonyConfig.LoadConfig()` (creates config file if needed) → IL modified for that mix.
- **Unload:** UnpatchAll by loader; no explicit cleanup (no timers or entity state).

---

## 8) What NOT to Touch Without Care

- **Patch targets:** `Bootstrap.StartupShared`, `MixingTable.StartMixing` — method names/signatures and IL layout can change with Rust version.
- **Transpiler IL assumptions:** The instant-mix path assumes a specific IL pattern (e.g. 7+ instructions before the setter call); reordering in the game could break it. The multiplier path only inserts before the call and is more robust.
- **Config path:** `HarmonyConfig/MixingSpeed.json` — other tools or docs may assume this path.
- **Config load in transpiler:** Config is loaded every time the transpiler runs; consider caching if profiling shows cost.

---

## 9) Performance Anti-Patterns

- **Reference:** `.cursor/PluginInstructionalFiles/#System.Linq-Removal.md`, `Rust_Plugin_Performance_Best_Practices.md`.
- This mod does not iterate entity lists; it only runs a transpiler on mix start.
- **Transpiler:** Runs once per mix start; `LoadConfig()` reads and parses JSON each time. For high mix frequency, caching config (e.g. after first load) would reduce I/O.

---

## Workspace Paths (this project)

| Path | Purpose |
|------|---------|
| `.cursor/HarmonyMods/MixingSpeed/` | MixingSpeed source |
| `HarmonyConfig/MixingSpeed.json` | Runtime config (server root) |
| `HarmonyMods/` (e.g. `D:\!RustServer\HarmonyMods\`) | Deployed Harmony DLLs |
| `bin/Release/net48/MixingSpeed.dll` | Build output |
