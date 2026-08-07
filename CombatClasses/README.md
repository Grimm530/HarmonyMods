# CombatClasses (Harmony OxideCompat port)

Port of Oxide `CombatClasses` v1.0.0131 to a SkillTree-style Harmony mod. Coexists with **SkillTree** by disabling overlapping abilities.

## Paths

| | |
|---|---|
| Config | `HarmonyConfig/CombatClasses.json` |
| Player data | `CustomDataDirectory` → `C:\!DataPersistence\oxide\data\CombatClasses\` (shared JSON) |
| UI images / avatars / themes | `oxide/data/CombatClasses/` (server-local; `images`, `Avatars`, `CCThemeData.json`) |
| Runtime DLL | `HarmonyMods/CombatClasses.dll` (entry DLL only) |
| Source | `.cursor/HarmonyMods/CombatClasses/` |

## KEEP vs REMOVE (vs SkillTree)

### KEEP
- Class system, XP, ranks, gear box
- Weapon-locked class damage
- **Assault**: adrenaline shot, smoke
- **Medic**: revive HP, recover HP, syringe heal bonus
- **Heavy**: bullet resistance, F1 grenade deploy
- **Sniper**: scanner
- **Demolition**: explosive damage + rocket damage only
- **Assassin**: ping / poison / melee
- **Scavenger**: Tap Ready

### REMOVE (SkillTree owns these)
| Ability | Config default | Runtime |
|---|---|---|
| Medic passive healing | `passivehealing = false` | `ResetPassiveHealingTimer` no-op |
| Medic bandage boost | `bandageboost = false` | Bandages skip heal bonus (syringe kept) |
| Heavy explosive DR | `explosivedmgreduction = 0` | Skipped in `ReduceDamage` |
| Demo explosive radius | `explosiveradius = 0` | Skipped in `OnExplosiveThrown` |
| Demo dud chance | `explosivedudreduction = 0` | `OnExplosiveDud` always null |
| Demo rocket speed | `rocketspeed = 0` | Skipped in `OnRocketLaunched` |

## Build

```powershell
cd .cursor\HarmonyMods\CombatClasses
.\build.ps1
```

Copies **only** `CombatClasses.dll` to root `HarmonyMods/`.

## Load

- Auto-loads with other Harmony mods on server start.
- Requires **Permissions** Harmony mod (`0Permissions.dll` / Permissions).
- Do **not** run the Oxide plugin at the same time — unload/disable `oxide/plugins/CombatClasses.cs` (file left in place; not deleted by this port).
- Chat: `/class`, `/gearbox`, `/givexp`, `/movecombat`, `/resetcombat` (aliases from config).
- CUI buttons bridge via `cui.endtest CC …`.

## Notes

- Nested `Oxide.Plugins.CombatClassesEx` helpers are included at the end of `CombatClassesPlugin.cs`.
- Images/avatars resolve under `CustomDataDirectory` (or HarmonyData/CombatClasses).
