# AirbourneSpawn Harmony Mod (1.0.191)

Oxide-free Harmony port of **AirbourneSpawn 1.0.191**. Players can respawn on a looping cargo plane / CH47 / F15, jump with a parachute, and optionally take a spawn kit. Uses **0Permissions** for access checks and **Kits** (Harmony) for autokit.

## Load order

1. **0Permissions.dll**
2. **Kits.dll** (optional — required only if `Give kit on respawn` is set)
3. **AirbourneSpawn.dll**

```text
harmony.load 0Permissions
harmony.load Kits
harmony.load AirbourneSpawn
```

Unload the Oxide plugin if it is still present:

```text
o.unload AirbourneSpawn
```

After `harmony.reload 0Permissions`, AirbourneSpawn re-registers `airbournespawn.*` via the permissions ready callback.

## Permissions

| Permission | Purpose |
|------------|---------|
| `airbournespawn.use` | Show plane spawn option / beach button / allow beach spawn |
| `airbournespawn.ignorecooldown` | Skip plane spawn cooldown |

Example:

```text
perm grant group default airbournespawn.use
```

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/AirbourneSpawn.json` |
| Lang | `HarmonyLanguage/AirbourneSpawn.json` (file wins over embedded defaults) |

On first load, if `HarmonyConfig/AirbourneSpawn.json` is missing, the mod copies `oxide/config/AirbourneSpawn.json` when that file exists.

## Behaviour

- Flight entity loops across the map (CargoPlane, CH47, or F15 from config).
- Death screen can list the plane as a spawn option, or `Force random respawns` mounts everyone on the plane.
- Purple **SPAWN ON BEACH** CUI button appears after death (uses `cui.endtest AIRBOURNESPAWN beach`).
- Jump (when over the island) gives a parachute and deploys it; custom descent settings match the Oxide plugin.
- Chat commands are blocked while mounted on the plane.
- Flyhack/speedhack violations are suppressed during jump/parachute.

## Build

```powershell
.\.cursor\HarmonyMods\AirbourneSpawn\build.ps1
```

Copies `AirbourneSpawn.dll` to `HarmonyMods/`.

## Port notes

- Source: `oxide/plugins/AirbourneSpawn.cs`
- Chaos/Oxide CUI replaced with CommunityEntity JSON + `cui.endtest`
- Oxide timers replaced with `ServerMgr` coroutines
- Kits autokit is given by this mod after plane mount; Harmony Kits `OnPlayerRespawned` is skipped for those players when a spawn kit is configured
- MagicPanel hook omitted (no Harmony MagicPanel in this workspace)
