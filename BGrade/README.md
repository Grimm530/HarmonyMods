# BGrade (Harmony)

Oxide **BGrade 1.1.6** port as a standalone Harmony mod (no Oxide runtime). Auto-upgrades building blocks when placed.

## Load order

1. **0Permissions** (`0Permissions.dll`)
2. **BGrade** (`BGrade.dll`)

## Deploy

```powershell
.\build.ps1
```

Copies **only** `BGrade.dll` to `HarmonyMods\BGrade.dll`.

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/BGrade.json` |
| Lang | `HarmonyLanguage/BGrade.json` |

## Permissions

| Permission | Effect |
|------------|--------|
| `bgrade.1` | Wood auto-grade |
| `bgrade.2` | Stone auto-grade |
| `bgrade.3` | Metal auto-grade |
| `bgrade.4` | Armoured auto-grade |
| `bgrade.all` | All grades |
| `bgrade.nores` | Skip resource cost (including twig placement) |

## Commands

Chat: `/bgrade`, `/grade` (from config)

- `/bgrade 0` — disable
- `/bgrade 1-4` — set grade
- `/bgrade t <seconds>` — auto-disable timer
- `/bgrade help`

Console: `bgrade.up` — cycle to next allowed grade

## Patches

| Method | Kind |
|--------|------|
| `Planner.DoBuild(Construction.Target, Construction)` | postfix (OnEntityBuilt) |
| `Planner.PayForPlacement` | prefix skip (nores) |
| `BaseCombatEntity.Die` | postfix (explosion cooldown dict) |
| `BasePlayer.OnDisconnected` | postfix |
| `SaveRestore.DoAutomatedSave` | postfix |
| `ConVar.Chat.say` | prefix (chat commands) |
