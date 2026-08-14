# Minimap Harmony Mod (1.3.1)

Oxide-free Harmony port of **Minimap 1.3.1** (Chaos UI / Chaos Map). Uses **0Permissions** for `minimap.use`. Map images are stored with vanilla `FileStorage` (no ImageLibrary).

## Load order

1. **0Permissions.dll** (preferred first)
2. **Minimap.dll**

```text
harmony.load 0Permissions
harmony.load Minimap
```

Startup load order is filesystem-defined. Minimap re-registers `minimap.use` when Permissions loads or is reloaded.

## Access

- `/map` requires `minimap.use`
- Grant:

```text
perm grant user <steamid> minimap.use
perm grant group default minimap.use
```

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/Minimap.json` (migrates from `oxide/config/Minimap.json` on first load if present) |
| Data | `HarmonyData/Minimap/` (`minimap.users.json`, rendered image cache) |
| Lang | `HarmonyLanguage/Minimap.json` (file wins over embedded defaults) |
| Images | `HarmonyImages/Minimap/` (`maparrow.*.png` direction icons) |

## Commands

| Command | Who | Purpose |
|---------|-----|---------|
| `/map` | `minimap.use` | Toggle the minimap |
| `/map reset` | `minimap.use` | Reset size and screen position |
| `minimap.toggle` | `minimap.use` | Console toggle |
| `minimap.reset` | `minimap.use` | Console reset position/size |
| `minimap.zoom.in` / `minimap.zoom.out` | `minimap.use` | Zoom |
| `minimap.regenerate` | admin | Rebuild all map layers |
| `minimap.render` | admin | Re-render map images |

## CUI

Button commands are rewritten in `ChaosUI.Show` to `cui.endtest MINIMAP minimap.callback …`, then routed to `CommandCallbackHandler.HandleCallback`.

## Notes

- First load after a wipe or resolution change renders overworld, tunnels, labs, and (when open) deep sea. That can take a short time; `/map` reports when rendering is still in progress.
- Train-tunnel layer is rebuilt from the 216 m dungeon grid (`TerrainMeta.Path.DungeonGridCells`). After updating this mod, restart or `harmony.load Minimap` so the `v2` tunnel/lab images regenerate. Look for `[Minimap] Tunnel layer: N dungeon-grid cells`.
- Fog-of-war overlay follows `server.fogofwar` / `server.deepseafogofwar` and the matching config flags.
- Optional **heat overlay** (fire toggle on the minimap). Both flags default **off**; if both are false the heat system does nothing.

### Heat map (`HarmonyConfig/Minimap.json` → `Heat Map Settings`)

| Flag | Effect |
|------|--------|
| `Enable PVP heat (player vs player deaths)` | Hotspots where players killed other players. Events expire (default 30 min) and can persist across restarts. |
| `Enable PVE heat (NPC locations)` | Live density of NPCs: scientists, animals, sharks, scarecrows, custom NPC players, etc. Refreshed on the update interval. |

Both can be on at once (same overlay). A fire icon on the minimap toggles it.

## Build

```powershell
.\.cursor\HarmonyMods\Minimap\build.ps1
```

Copies `Minimap.dll` to `HarmonyMods/` and arrow PNGs to `HarmonyImages/Minimap/`.
