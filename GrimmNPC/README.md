# GrimmNPC

Harmony mod for Rust dedicated servers. It **extends** stock **Gen1** humanoid NPCs (`ScientistNPC` / `HumanNPC` + `BaseAIBrain` + `BaseNavigator`); it does **not** replace the game’s brain, navigator, or FSM. 

Custom NPCs are identified by a fixed **`skinID`** (`GrimmNPC.CUSTOM_NPC_SKIN_ID`). Plugins register per-NPC data before or after spawn; the mod applies tuning at `ScientistNPC.ServerInit` and uses patches only on those entities where possible.

---

## Purpose (what it modifies, not replaces)

- **Spawn / config:** Postfix on `ScientistNPC.ServerInit` to apply `CustomNpcData` (health, name, sense range, nav agent flags, optional bomber explosive, etc.) without destroying components.
- **Brain think (postfix):** After `BaseAIBrain.Think`, throttled logic for dormancy (optional), roam enforcement, combat assists, dynamic navmesh tweaks, idle/raid-adjacent behavior, and special weapons handling. **Stock `Think` still runs;** this adds orchestration on top.
- **Roam vs idle:** Custom NPCs with **`RoamRange > 5m`** (typical bosses) are **not** soft-halted for “no memory target but moving” — that movement is used by **`ScientistBrain`** `RoamState`, **`DismountedState`**, and related stock states. Only **`RoamRange ≤ 5m`** (stationary guards) get the in-range idle halt. **Not** controlled by `HarmonyConfig/GrimmNPC.json` or `oxide/config/BossMonster.json`; patrol still needs **`AIMovePoint`**s from the map’s **`AIInformationZone`** (monuments usually have them). **`oxide/plugins/BossMonster.cs`** sets **`Navigator.MaxRoamDistanceFromHome`** from each boss’s **Roam Range** JSON so stock roam scoring matches the plugin tether.
- **Raiding:** Postfix on `BaseAIBrain.Think` calls `Raid.TickRaid` for registered raiding NPCs (structure raycasts, explosives/rockets via `SpecialWeaponsHandler`). Does not replace vanilla combat when LOS to players exists.
- **Targeting:** Prefix on `HumanNPC.GetBestTarget` for custom NPCs only, **skipping** the original method and supplying a custom pick (chase range from home, guard priority, raid-goal bias, config flags). This is a deliberate tradeoff: narrower than duplicating `AIBrainSenses.UpdateSenses`, but it **does** own target choice for those NPCs.
- **Roam clamp:** Prefix on `BaseNavigator.SetDestination` (4-parameter overload) to clamp destinations to horizontal roam range from `HomePosition`, with raid-goal bypass and optional “freeze unless players nearby” (see below).
- **Swimming (optional):** If `CustomNpcData.CanSwim`, patches `BaseNavigator` `IsSwimming`, `GetTargetSpeed`, `UpdatePositionAndRotation`, and `CanEnableNavMeshNavigation` to approximate water movement. **High sensitivity:** touches navigator internals; only enable when needed.
- **Damage / turrets:** Postfix on `BaseCombatEntity.OnAttacked` for turret damage scaling; prefix on `AutoTurret.ShouldTarget` for opt-out targeting.
- **Bomber:** Prefix on `NPCPlayer.CreateCorpse` when `IsBomber` to run effect + optional Oxide hook.
- **Save-load safety (global):** Prefix on `BaseEntity.HasChild` replaces the method with a cycle-safe walk (fixes circular parent saves). Affects **all** entities, not only custom NPCs.

---

## Patch discipline

| Area | Patches | Notes |
|------|---------|--------|
| `BaseAIBrain.Think` | Postfix (two classes: main + raiding) | ~brain think rate; work is throttled per NPC. |
| `HumanNPC.GetBestTarget` | Prefix, return `false` | Custom target selection for skinID NPCs only. |
| `BaseNavigator.SetDestination` | Prefix | Clamps roam; blocking prefix when freezing movement. |
| `BaseNavigator` swimming | Prefix (several) | Only when `CanSwim`; replaces parts of nav stepping when swimming. |
| `ScientistNPC.ServerInit` | Postfix | Spawn-time only. |
| `BaseAIBrain.InitializeAI` | Postfix | Custom NPCs: unpause navigator if needed (reflection on private field). |
| `BaseEntity.HasChild` | Prefix, always skips original | **Global** cycle fix. |
| `BaseCombatEntity.OnAttacked` | Postfix | |
| `AutoTurret.ShouldTarget` | Prefix | |
| `NPCPlayer.CreateCorpse` | Prefix | Bomber only. |

**Rules this mod follows:**

- No patches on `Navigator.Think`, `UpdateNavigation`, `TickMovement`, or full `AIBrainSenses.UpdateSenses` (per NPC framework).
- Prefixes that **block** the original are used only where required (turret deny, targeting replacement, swimming overrides, optional movement freeze).
- Transpilers: none.
- Verify patch targets against your game assembly after updates (`Assembly-CSharp`).

---

## Framework relationship

- **NPC / AI (`AI_NPC_Plugin_Execution_Framework.md`):** Treats `BaseAIBrain`, `ScientistBrain`, `BaseNavigator`, and senses as **owned by the game**. GrimmNPC **configures** them at spawn and **adds** post-think behavior, roam clamping at `SetDestination`, and optional swimming overrides. It does **not** reimplement the full think or nav tick; the heaviest custom work is throttled or tied to raid/combat flows.
- **Harmony (`Harmony_Mod_Execution_Framework.md`):** Uses `IHarmonyModHooks`, `PatchAll`, postfix-first where practical, and documents shared `Think` postfixes. Mod-to-mod: static API on `GrimmNPC` / `Raid`; optional `CallOxideHook` uses reflection and is **not** required for core behavior.

---

## Error handling rules

- **File I/O** (config load/save, data persistence): `try/catch` with logging; falls back to defaults.
- **Reflection** (Oxide `CallHook`, spawn-time `NavMeshAgent` / `MonumentNavMesh` probes, `InitializeAI` paused field): failures are handled or logged; avoid silent swallow except where enumeration of assemblies can throw.
- **Game logic in patches:** No broad `try/catch` around entire postfix bodies; failures should surface in logs during development.
- **`CallOxideHook`:** Failures log **only** when `EnableDebugLogging` is true (avoids spam on servers without Oxide).

---

## Performance rules

- Dictionaries for per-NPC state are pre-sized where cheap.
- Config is cached in targeting / think paths (~5 s refresh).
- Raid structure scan near a raid goal caps at **`MaxRaidGoalStructureScan`** entities per search (5000) to bound worst-case work.
- Foundation cleanup reuses a static buffer list instead of allocating each tick.
- Avoid LINQ in patch hot paths; prefer indexed loops.
- **Swimming** and **full-server entity scans** (raid goal) remain expensive when enabled; keep `CanSwim` and raiding NPC counts within what your hardware tolerates.

---

## Public API (direct calls, no reflection)

- `GrimmNPC.IsCustomNpc(BaseEntity entity)`
- `GrimmNPC.GetConfig()`
- `GrimmNPC.RegisterPending(BaseEntity entity, CustomNpcData npcData)` — before `Spawn()` if net ID is not yet valid
- `GrimmNPC.RegisterNpc(ulong netId, CustomNpcData npcData)`
- `GrimmNPC.UnregisterNpc(ulong netId)`
- `GrimmNPC.GetNpcData(ulong netId)`
- `GrimmNPC.SetKnown(ScientistNPC npc, BaseEntity entity)` — memory assist (Gen1)
- `GrimmNPC.CallOxideHook(string hookName, params object[] args)` — optional; reflection
- `GrimmNPC.Raid.*` — e.g. `AddTargetRaid`, `AddTargetGuard`, `TickRaid` is invoked from patches

**Movement freeze (replaces old BossMonster `ControllerBoss` component scan):** set on `CustomNpcData`:

- `FreezeMovementUnlessPlayersNearby = true`
- `FreezeMovementPlayerCheckRadius` (default `150`)

### BossMonster (Oxide) cooperation

The **BossMonster** plugin (`oxide/plugins/BossMonster.cs`) is Gen1-only (`ScientistNPC` / `HumanNPC`): it **requires** GrimmNPC for registration and tuning. It does **not** replace stock `Think` / movement ticks.

- **Registration:** `RegisterPending` before `Spawn()`, `UnregisterNpc` on teardown; `CustomNpcData` is built in one shared path (`FillGrimmCustomNpcData`) for bosses and helper NPCs (roam/chase/sense, monument vs terrain `AreaMask` / `AgentTypeID`, `CanSwim` from boss JSON for bosses, helpers default `CanSwim = true`).
- **Memory assist:** helper aggro uses **`GrimmNPC.SetKnown`** when the static API is resolved at init; falls back to `ScientistNPC.SetKnown` / `Senses.Memory` only if that call fails or the API is missing.
- **Boss-only behavior:** `ControllerBoss` still handles plugin-owned abilities (radius AOE, teleports, strafe coroutines, return-to-spawn), **navigator Pause/Resume** during helper waves, and post-spawn brain field tweaks — not GrimmNPC’s roam clamp or post-think orchestration.
- **Init safety:** if `TerrainMeta.Path` / monuments never become ready, deferred init stops after a **bounded** number of retries (~30 × 2s) instead of rescheduling forever.

---

## Configuration and data paths (production server)

| Item | Path |
|------|------|
| Config | `{server}/HarmonyConfig/GrimmNPC.json` |
| Persisted data (used NPC user IDs) | `{server}/HarmonyConfig/GrimmNPC/data.json` |

If you previously used `.cursor/HarmonyMods/GrimmNPC/data.json` in a dev tree, copy that file into `HarmonyConfig/GrimmNPC/data.json` on the dedicated server.

**Load:** deploy `GrimmNPC.dll` under `HarmonyMods`, then `harmony.load GrimmNPC` (or rely on autoload). Do not load two different DLLs that patch the same methods for the same behavior.

---

## Build

```text
dotnet build GrimmNPC.csproj -c Release
```

Output: `bin/Release/net48/GrimmNPC.dll` (copy to `HarmonyMods`).

---

## References

- Game / API truth: decompiled `Assembly-CSharp` (e.g. `.cursor/!Assembly-CSharp-RUST`)
- Loader / HarmonyLib: `.cursor/!Harmony-Assembly`
- NPC framework: `.cursor/PluginInstructionalFiles/AI_NPC_Plugin_Execution_Framework.md`
- Harmony framework: `.cursor/PluginInstructionalFiles/Harmony_Mod_Execution_Framework.md`
