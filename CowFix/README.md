# CowFix

Makes livestock that should be wild killable again under TruePVE, without deleting animals that live at a player tool cupboard.

## Why this is still needed

Hotfix 3 stopped the breeding chain and added `livestock.maxPerSpecies`. It did not clear trust already saved on animals. TruePVE 2.4.6 treats an animal as kept when `IsLeadable` is true (any player has `livestock.trusttolead` seconds, 5 minutes), or it is on a lead, or it has a home cupboard. Those saved animals are not wild, so TruePVE correctly blocks the kill. Killing them and letting them respawn also works, because a fresh spawn has no stored trust. This mod clears the trust instead of deleting the animal.

`SenseComponent.Tick` still turns the whole gap since the last tick into `deltaTime`. `OnPlayerSensed` still banks that entire gap as familiarity. One wake-up can still cross `trusttolead` and `maxtrust` (15 minutes) in a single frame, which makes the animal kept again.

Penned animals (a tool cupboard home) and animals on a lead are left alone. A purchase sets familiarity and starts the lead after spawn, so a barn animal is not cleared at the moment it is bought.

## What this mod does

1. Caps the familiarity time credited by one sense tick at `SenseComponent.maxRefreshIntervalSeconds` (1 second). Standing near an animal still builds trust at the normal rate.
2. On load, and when an animal spawns from a save, zeroes trust on livestock that are already leadable, not on a lead, and have no cupboard home.

## Population

An animal is owned only when its home is a tool cupboard placed or authorized by a player. A lead, stored trust, or standing near a base does not count.

Server console (admin):

| Command | Effect |
|---------|--------|
| `cowfix.pop` | Counts each livestock prefab, penned versus no player cupboard |
| `cowfix.purge` | Kills every livestock animal that has no player tool cupboard |

Penned animals are left in place. LimitEntities then caps each penned prefab at 10 per cupboard owner. `livestock.cullexcess` is the game's own cull down to 300 per species and is separate from this.

## Build / load

- Build: `.\build.ps1` from this folder
- DLL: `HarmonyMods\CowFix.dll` (entry DLL only)
- Load: restart, or `harmony.load CowFix`
