# EntityOwner (Harmony)

Oxide **Entity Owner 3.4.3** (Calytic) port as a standalone Harmony mod (no Oxide runtime).

## Load order

1. **0Permissions** (`0Permissions.dll`)
2. **EntityOwner** (`EntityOwner.dll`)

Permissions uses ready-callbacks (§10a), so EntityOwner re-registers perms if Permissions loads later or is reloaded.

## Deploy

```powershell
.\build.ps1
```

Copies **only** `EntityOwner.dll` to `HarmonyMods\EntityOwner.dll`.

Load: `harmony.load EntityOwner` (or automatic at startup).

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/EntityOwner.json` |
| Lang | Embedded EN + optional `HarmonyLanguage/EntityOwner.json` overrides |

## Permissions

| Permission | Effect |
|------------|--------|
| `entityowner.cancheckowners` | `/prod`, `/prod2`, `/auth` (check) |
| `entityowner.cancheckcodes` | Show code lock codes in `/prod` |
| `entityowner.cancheckassignee` | Show sleeping bag assignee in `/prod` |
| `entityowner.seedetails` | Prefab/skin/outside details in `/prod` |
| `entityowner.canchangeowners` | `/own`, `/unown`, `/setowner`, `/auth`, `/deauth` |

Auth level &gt; 0 bypasses permission checks (same as Oxide). On load (and when Permissions becomes ready), all five are granted to the Permissions group **`admin`**.

```
perm usergroup add <steamid> admin
```

## Commands (chat)

| Command | Description |
|---------|-------------|
| `/prod` | Owner of entity looked at |
| `/prod2 [all/block/storage/...]` | Ownership breakdown of structure / deployables |
| `/prod2 highlight ...` | Same + ddraw spheres |
| `/setowner <player\|steamid>` | Change owner of looked-at entity |
| `/own [type] [player]` | Take/give ownership (`all`/`block`/`storage`/...) |
| `/unown [type]` | Remove ownership |
| `/auth all [player]` | Mass-authorize on nearby cupboards **and** turrets (self if no player) |
| `/auth [cupboard\|turret] [player]` | Check (no player) or mass-authorize that type |
| `/deauth all [player]` | Mass-deauthorize cupboards and turrets (self if no player) |
| `/deauth [cupboard\|turret] player` | Mass-deauthorize |

## Features

- `ConVar.Chat.say` prefix → chat command handling
- Config: `Debug`, `EntityLimit`, `DistanceThreshold`, `CupboardDistanceThreshold`
- Flood-fill ownership / cupboard / turret auth within distance thresholds
- Modern Rust: `(ulong)player.userID` for `OwnerID` / `authorizedPlayers` (`EncryptedValue`)
