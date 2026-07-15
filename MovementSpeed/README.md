# MovementSpeed (Harmony port)

Oxide-free port of **MovementSpeed 1.0.9** (imthenewguy). Applies permission- and API-driven run/swim speed via `BasePlayer.ApplyInheritedVelocity`. SkillTree **RoadRunner** (and related swim buffs) call this mod's API.

## Paths

| Resource | Path |
|----------|------|
| Config | `HarmonyConfig/MovementSpeed.json` |
| Logs | `HarmonyData/MovementSpeed/logs/` |

## Load order

Facepunch HarmonyLoader loads `HarmonyMods/*.dll` in filesystem order (typically alphabetical). On this server that is approximately:

`… → MovementSpeed → … → Permissions → … → SkillTree → …`

**No forced order is required.** Both mods use AppDomain ready callbacks:

| Mod | Binds when |
|-----|------------|
| MovementSpeed | Re-registers perms when `Permissions` fires `Permissions_ReadyCallbacks` |
| SkillTree | Rebinds RoadRunner API when `MovementSpeed` fires `MovementSpeed_ReadyCallbacks`; re-registers skill perms on Permissions ready |

Manual `harmony.load` in any order is fine after a full restart or when using those callbacks.

## API (for SkillTree / other mods)

Registered as AppDomain `MovementSpeed_ApiType` (`MovementSpeedHarmony.MovementSpeedMod`):

| Method | Purpose |
|--------|---------|
| `AddRunSpeedBoost(player, plugin, mod, duration, force)` | Temporary/permanent run boost |
| `RemoveRunSpeed(player, plugin)` | Remove that plugin's run boost |
| `AddSwimSpeedBoost(player, plugin, mod, duration, force)` | Swim boost |
| `RemoveSwimSpeed(player, plugin)` | Remove swim boost |
| `PauseSpeedBoost(userId, pause)` | Pause while in events/zones |

SkillTree calls these with plugin keys `"SkillTree_Run"` / `"SkillTree"`.

## Commands

| Command | Type | Description |
|---------|------|-------------|
| `/togglerun` | chat | Toggle personal run boost (config name) |
| `/toggleswim` | chat | Toggle personal swim boost |
| `msdisablerun` / `msenablerun` | console | Force off/on run for a steam id |
| `msdisableswim` / `msenableswim` | console | Force off/on swim |

## Build

```powershell
cd .cursor\HarmonyMods\MovementSpeed
.\build.ps1
```
