# CowFix

Stops wild cows and sheep from becoming "kept" in one tick, which makes TruePVE block damage from players and from other animals (bears included).

## Cause

Livestock AI sleeps while nobody is nearby (`NpcSleepingComponent` turns the FSM off). `SenseComponent.Tick` then treats the whole sleep as `deltaTime`. `LivestockAnimal.OnPlayerSensed` banks that entire gap as familiarity when a player is within `livestock.familiarityradius` (20m). `livestock.trusttobond` is 300 seconds, so any animal that slept longer than about five minutes bonds to the first player who walks up.

`IsTame` becomes true. TruePVE's wild-animal allow (`!isKeptLivestock && BaseNPC2.IsAnimal`) no longer applies, so the cow cannot be killed.

Penned animals (a tool cupboard home) and animals on a lead are left alone.

## What this mod does

1. Caps the familiarity time credited by one sense tick at `SenseComponent.maxRefreshIntervalSeconds` (1 second). Standing near an animal still bonds it at the normal rate. Waking from a long sleep does not.
2. Clears an existing bond on livestock that is tame, not on a lead, and has no cupboard home. That releases herds already stuck by the bug. A real bond made by standing nearby still works for the rest of the session; it is kept across a restart only after the animal has a cupboard home.

## Build / load

- Build: `.\build.ps1` from this folder
- DLL: `HarmonyMods\CowFix.dll` (entry DLL only)
- Load: restart, or `harmony.load CowFix`
