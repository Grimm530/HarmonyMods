# InventoryCleaner (Harmony)

Oxide **Inventory Cleaner 2.1.1** (Joao Pster) port as a standalone Harmony mod (no Oxide runtime).

## Load order

1. **0Permissions** (`0Permissions.dll`)
2. **InventoryCleaner** (`InventoryCleaner.dll`)

Permissions uses ready-callbacks (§10a), so InventoryCleaner re-registers perms if Permissions loads later or is reloaded.

## Deploy

```powershell
.\build.ps1
```

Copies **only** `InventoryCleaner.dll` to `HarmonyMods\InventoryCleaner.dll`.

Load: `harmony.load InventoryCleaner` (or automatic at startup).

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/InventoryCleaner.json` |
| Lang | Embedded EN + optional `HarmonyLanguage/InventoryCleaner.json` overrides |

## Permissions

| Permission | Effect |
|------------|--------|
| `inventorycleaner.allowed` | Use `/clearinv` (and aliases) |
| `inventorycleaner.cleaneveryone` | `/clearinv [opt] everyone` |
| `inventorycleaner.cleanondeath` | Strip inventory on death (before loot drop) |
| `inventorycleaner.cleanonexit` | Strip inventory on disconnect (if not already dead) |

On load (and when Permissions becomes ready), all four are granted to the Permissions group **`admin`**. Put staff in that group:

```
perm usergroup add <steamid> admin
```

## Commands (chat)

Aliases: `/clearinv`, `/cleaninv`, `/clear.inv`, `/clean.inv`, `/inv.clear`, `/invclear`, `/inv.clean`, `/invclean`

| Args | Action |
|------|--------|
| *(none)* / `main` | Strip all containers |
| `inv` | Clear main inventory |
| `belt` | Clear belt |
| `wear` | Clear clothing |
| `help` | Permission summary |
| `cmds` | Option help |
| `[opt] everyone` | Same clear for all players (needs `cleaneveryone`) |

## Features

- `ConVar.Chat.say` prefix → chat command handling
- `BasePlayer.Die` prefix → strip on death (Oxide `OnPlayerDeath` timing)
- `BasePlayer.OnDisconnected` postfix → strip on logout
