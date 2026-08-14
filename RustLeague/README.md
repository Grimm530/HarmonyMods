# RustLeague (Harmony Mod)

Oxide-free Harmony port of **RustLeague** (car/ball arena). The Oxide plugin needed a hand-placed ZoneManager arena. This mod **spawns the arena 700m in the sky** at a random map location (same idea as AirEvent), opens a join window, teleports players in, then despawns the structure when the event ends.

## Load order

1. **0Permissions.dll**
2. **RustLeague.dll**

```text
harmony.load 0Permissions
harmony.load RustLeague
```

Unload the Oxide plugin if it is still present:

```text
o.unload RustLeague
```

## Sky arena

The structure is placed **terrain height + `ArenaAltitude`** (default **700m**). XZ is a random inland map point — no ground scan, no monument, no Zone Manager. Players never walk there; they teleport in for the match. Event occupants are exempt from fly/speed/noclip antihack while they are in the arena.

On first load the mod reads `maps/prefabs/RustLeagueArena.map` (or `ArenaPrefabPath`) for **size and goal positions only**. The original glass monument cannot replicate to clients, so the live arena is a **metal pitch**: one open floor and four perimeter walls (no interior dividers, no roof). It despawns when the join window fails or the match ends.

| Setting | Default | Meaning |
|---------|---------|---------|
| `ArenaPrefabPath` | `maps/prefabs/RustLeagueArena.map` | WorldSerialization `.map` used for pitch size / goals |
| `ArenaAltitude` | `700` | Meters above terrain |

Set `UseFixedLocation` to `true` and `/rl center` to pin the XZ (still lifted 700m). `/rl here` uses your current XZ, in the sky.

## Cycle

Default (config):

| Setting | Default | Meaning |
|---------|---------|---------|
| `JoinWindowSeconds` | 1200 (20 min) | How long `/rl` join stays open |
| `EventIntervalSeconds` | 7200 (120 min) | Time from one open until the next open |
| `playersOnlineNeeded` | 2 | Skip a cycle if the server is quieter than this |
| `MinPlayersToStart` | 2 | Match starts once this many have joined |
| `MaxPlayersToStart` | 6 | Starts immediately at this count |

If the join window expires without enough players, everyone is refunded (when join cost is on) and the next cycle waits out the 120-minute interval.

## Commands

| Command | Who | What |
|---------|-----|------|
| `/rl` | everyone | Open join/leave UI while an event is advertised |
| `/rl join` `/rl leave` | everyone | Join or leave the waiting list |
| `/rl open` | admin | Force-open at a random sky location (or fixed XZ) |
| `/rl close` | admin | End the current event |
| `/rl here` | admin | Open an event in the sky above your XZ |
| `/rl spawn` | admin | Spawn the arena (if needed) and teleport you onto it |
| `/rl tp` `/rl goto` | admin | Teleport to the live arena (uses the sky Y, not the ground) |
| `/rl scan` | admin | No-op (sky spawn does not scan the ground) |
| `/rl status` | admin | Open/running/join/altitude |
| `/rl location` | admin | Print world position + grid |
| `/rl center` `/rl red` `/rl blue` | admin | Save a fixed arena (also sets `UseFixedLocation`) |
| `rl` / `rl.open` / `rl.close` / `rl.spawn` / `rl.tp` | F1 / server console | Same as chat (use dots, not spaces) |

Permission: `rustleague.admin` (server admins always pass).

## Match rules (unchanged)

- Modular cars on a circle around the ball, red vs blue
- Goals are trigger boxes; first to `WinPoints` or `MaxRounds` wins
- Dismount fires a rocket; seat-swap flips the car during countdown
- Event players/cars/ball take no damage
- Optional join cost and win item from config

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/RustLeague.json` |
| Lang | `HarmonyLanguage/RustLeague.json` (optional overrides) |
| Arena cache | `HarmonyData/RustLeague/Arena.json` |
| Arena source | `maps/prefabs/RustLeagueArena.map` |

On first load, `oxide/config/RustLeague.json` is copied if the Harmony config is missing.

## Build

```powershell
.\.cursor\HarmonyMods\RustLeague\build.ps1
```

Copies **only** `RustLeague.dll` to `HarmonyMods/`.
