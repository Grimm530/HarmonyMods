# SafeZonePVE Harmony Mod

## Mod Identity

| Attribute | Value |
|-----------|--------|
| **Name** | SafeZonePVE |
| **Author** | nivex |
| **Type** | Harmony mod (decompiled source) |
| **Version** | 1.0.0.2 |
| **Purpose** | Fixes safe zone PVE exploits: prevents players from being targeted by AI/turrets/Bradley while in safe zones, reflects damage back to attackers in safe zones, and disables helicopter collision damage inside safe zones |

**Primary responsibilities:**
- Block AI targeting (SimpleAIMemory, HumanNPC, AIBrainSenses, BaseNpc) for players in safe zones or with `limitNetworking`
- Block Bradley APC visibility tests for non-Steam-ID entities
- Reflect PVP damage back to the attacker when both players are in a safe zone
- Disable helicopter collision damage inside safe zones (with 15s grace timer on entry)
- Clear hostile timer for players in safe zones being targeted
- Block NPCAutoTurret hostility checks for safe zone players

---

## Project Structure

```
.cursor/HarmonyMods/SafeZonePVE/
├── SafeZonePVE.csproj
├── build.ps1
├── Readme.md
├── Properties/
│   └── AssemblyInfo.cs
└── HarmonyMods.RustGame.Nivex.SafeZonePVE/
    ├── Manager.cs          # IHarmonyModHooks, all patches, PatchDefinition system
    └── ExtensionMethods.cs # IsSteamId, Cast<T> helpers
```

**Note:** This is decompiled from the original `SafeZonePVE.dll` by nivex. The source has been adapted for compilation in this workspace.

---

## Harmony Patches (All Permanent)

| Target | Patch Type | Purpose |
|--------|-----------|---------|
| `TriggerBase.OnEntityEnter` | Prefix | Track helicopters entering safe zones (15s grace for collision damage) |
| `BradleyAPC.VisibilityTest` | Prefix | Block Bradley targeting non-Steam-ID entities |
| `SimpleAIMemory.SetKnown` | Prefix | Block AI memory of safe zone / limitNetworking players |
| `HumanNPC.GetBestTarget` | Postfix | Nullify target if player is in safe zone |
| `AIBrainSenses.GetNearest` | Postfix | Nullify nearest entity if in safe zone |
| `BaseNpc.WantsToAttack` | Prefix | Return 0 attack desire for safe zone players |
| `BaseHelicopter.CollisionDamageEnabled` | Prefix | Disable collision damage in safe zones (unless grace timer active) |
| `BasePlayer.OnAttacked` | Prefix | Reflect damage / block PVP in safe zones |
| `NPCAutoTurret.IsEntityHostile` | Prefix | Block turret hostility for safe zone players |

---

## Build & Deploy

```powershell
# From the SafeZonePVE directory:
.\build.ps1

# Or manually:
dotnet build SafeZonePVE.csproj -c Release
# Output DLL is copied to <server root>\HarmonyMods\SafeZonePVE.dll
```

**Requirements:**
- .NET SDK (targets net48)
- Game assemblies in `RustDedicated_Data\Managed\` (resolved via `$(ManagedPath)` in csproj)

---

## Runtime

- **Load:** `harmony.load SafeZonePVE` (or automatic at server startup)
- **Unload:** `harmony.unload SafeZonePVE`
- **No config file** — all behavior is hardcoded

---

## Key Implementation Details

- **ClientRPC for hostile timer:** Uses reflection to call `BaseEntity.ClientRPC(RpcTarget, float)` to avoid compiler issues with `ReadOnlySpan<T>` overload resolution against .NET Framework 4.8
- **Damage reflection:** When a non-admin player attacks another player and both are in a safe zone, the damage is applied back to the attacker instead
- **Helicopter grace timer:** When a helicopter enters a safe zone trigger, it gets a 15-second `CanVehicleTakeDamage` entry; collision damage is only disabled after that window closes

---

## What NOT to Touch Without Care

- **Patch target method signatures** — Rust version-dependent; `ValidatePatchDefinitions` uses `AccessTools.Method` with explicit parameter types
- **`CanVehicleTakeDamage` list** — Tracks helicopter safe zone grace timers; clearing it mid-flight will immediately disable collision damage
- **Reflection-based `ClientRPC` call** — Avoids compiler `ReadOnlySpan` issue; do not convert back to a direct call without resolving the `System.ReadOnlySpan` predefined type issue
