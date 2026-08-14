# HitMarkers (Harmony)

Combined port of Oxide **HitMarkers 1.2.5** (Mevent/Grimm530) and **HeadshotIcon 1.0.111**.

## Features

- Damage numbers, hit icon (headshot tint), health line, building damage — player-configurable UI (`/hits`, `/marker`, `/hm`, `/bar` from config)
- Headshot / kill PNG overlay from HeadshotIcon (`/hit` toggle)
- Native `FileStorage` for CUI images (no ImageLibrary)
- Oxide Notify / UINotify are ignored; messages go to chat

## Commands

| Command | Description |
|---------|-------------|
| `/hits`, `/marker`, `/hm`, `/bar` | Open HitMarkers settings (config `Commands`) |
| `/hit` | Toggle HeadshotIcon death/hit overlay |

## Permissions

Optional `hitmarkers.use` plus per-font / per-button permissions from config. Requires `0Permissions`.

## Config / data / images

- Config: `HarmonyConfig/HitMarkers.json` (includes nested `HeadshotIcon` section)
- Data: `HarmonyData/HitMarkers.json`
- Images: `HarmonyImages/HitMarkers/` (`hit.png`, `death.png`, `hitinfo.png`) stored via FileStorage

## Harmony patches

| Method | Kind | Notes |
|--------|------|--------|
| `BaseCombatEntity.Hurt(HitInfo)` | prefix (health snapshot) + postfix observer | Does **not** skip original |
| `BaseCombatEntity.Die(HitInfo)` | postfix | HeadshotIcon death skull |
| `Chat.say` | prefix | `/hits` `/marker` `/hit` |
| `cui.endtest` | prefix | `HITMARKERS` marker only |
| `BasePlayer.OnDisconnected` | postfix | UI cleanup |

Load order: **0Permissions → HitMarkers**.
