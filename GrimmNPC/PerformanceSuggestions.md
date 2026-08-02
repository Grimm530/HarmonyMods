# GrimmNPC (Harmony) — performance notes

**Deployment status:** This Harmony mod is **not loaded** on the current server profile. Keep this file so that if GrimmNPC is re-enabled later, you know exactly which hot paths it touches and how it interacts with other AI/nav code.

## Patched hot paths (high tick frequency)

| Target | Patch class / file | Role |
|--------|-------------------|------|
| `BaseAIBrain.Think` | `ThinkPatches.cs` — `BaseAIBrain_Think_Patch` | Postfix: dormancy, roam, combat assist, navmesh fixes, logging (mostly throttled). **Runs on every think for every brain that hits this path.** |
| `BaseAIBrain.Think` | `RaidingPatches.cs` — `BaseAIBrain_Think_Raiding_Patch` | Second postfix on the **same** method: raiding tick for custom scientists when enabled. **Doubles Harmony entry cost** for `Think` vs a single patch. |
| `HumanNPC.GetBestTarget` | `TargetingPatches.cs` — `HumanNPC_GetBestTarget_Patch` | Prefix: custom targeting / scoring. **Runs when the game resolves best target.** |
| `BaseAIBrain.InitializeAI` | `SpawnPatches.cs` — `BaseAIBrain_InitializeAI_Patch` | Postfix: navigator / area setup after brain init. Lower frequency than `Think`. |
| `BaseNavigator` | `SwimmingPatches.cs` | Multiple patches: `IsSwimming`, `GetTargetSpeed`, `UpdateNavigation`, `SetDestination` (float overload), `Stop`, `Pause`, `Resume`. **Nav mesh + swim path.** |
| `BaseNavigator.SetDestination` | `NavigationPatches.cs` | Additional overload (`NavigationSpeed` variant). |

## If you turn GrimmNPC back on

1. **Profile again** (`profile.perfsnapshot` + `Summarize-RustTraceJson.ps1` or Perfetto) and compare **before/after** on `BaseAIBrain`, `HumanNPC.GetBestTarget`, and `BaseNavigator` slices.
2. **Avoid stacking** with other mods that patch the same methods (see `oxide/plugins/NpcSpawn.cs` swim Harmony — it intentionally no-ops when `GrimmNPC.dll` is present).
3. Consider **merging** the two `BaseAIBrain.Think` postfixes into one class with a single postfix that calls shared helpers, to cut one Harmony indirection layer (same behavior, less wrapper overhead).
4. Keep throttles and caches as documented in `ThinkPatches.cs`; avoid new per-tick allocations in `Think` / `GetBestTarget`.

## Related docs in repo

- `GrimmNPC` instructional / navigation docs (if present) for AI flow context.
- `oxide/plugins/NpcSpawn.cs` — `NpcSpawnSwimHarmonyGuard` and swim patches (coordination with GrimmNPC).
