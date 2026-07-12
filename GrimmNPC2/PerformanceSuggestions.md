# GrimmNPC2 (Harmony) — performance notes

**Deployment status:** This Harmony mod is **not loaded** on the current server profile. This file records cost-relevant patches if you re-enable it later.

## Patched paths

| Target | File | Notes |
|--------|------|--------|
| `SenseComponent.CanTarget` | `Patches/SenseComponentCanTargetPatch.cs` | **Target acquisition / sensing** — can run often when AI evaluates whether an entity is a valid target. Keep logic minimal; avoid LINQ/allocations in the hot path. |
| `BaseEntity.Spawn` | `SpawnPatches2.cs` | Spawn-time setup (lower rate than senses). |
| `BaseEntity.DoServerDestroy` | `SpawnPatches2.cs` | Teardown. |
| `BaseCombatEntity.OnAttacked` | `SpawnPatches2.cs` | Combat events. |
| `AutoTurret.ShouldTarget` | `SpawnPatches2.cs` | Turret targeting. |
| `NpcShootingComponent.ServerInitPostNetworkGroupAssign` | `NpcShootingWeaponPatch.cs` | Weapon init. |

## If you turn GrimmNPC2 back on

1. Re-capture a native trace and confirm time under `SenseComponent.CanTarget` and any Harmony wrapper names.
2. Ensure you are not double-patching the same AI pipeline elsewhere (e.g. legacy GrimmNPC + GrimmNPC2 overlapping goals).
3. Prefer **early returns** and **cached config** in `CanTarget`; do not scan the whole entity list per call unless throttled.

## Coordination

- Custom NPC behavior for live population may be driven by **Oxide.Ext.ChaosNPC** and **NpcSpawn** / **ZombieHorde** — see `.cursor/Extensions/Oxide.Ext.ChaosNPC/PerformanceSuggestions.md` for how profiler names map to the extension.
