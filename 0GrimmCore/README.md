# 0GrimmCore

Core Harmony infrastructure for Grimmzone servers:

- **Unified `BaseCombatEntity.Hurt` dispatcher** — one Harmony prefix; mods register ordered handlers via `GrimmCoreBridge`
- **Vanilla scientist/scarecrow removal** — if a **map** `HumanNPC` / `ScarecrowNPC` is *not* tagged as a custom Grimm/RaidableBases NPC, we `Kill()` it during the AI `ServerThink` prefix so it does not remain on the map brain-dead.
  - Tagged custom NPCs (skin `3793751435`, `HumanoidNPC`, `CustomScientistNpc`, etc.) still think.
  - **Animals are not patched** and must keep vanilla movement.
- **Shared skinID** — `3793751435` (blank workshop skin) via `GrimmCoreBridge.TagCustomEntity()`

## Load order

Deploy as `HarmonyMods/0GrimmCore.dll`. Loads before `0Permissions` and most other mods.

Other mods must register Hurt handlers in `OnLoaded` and call `GrimmCoreBridge.UnregisterHurtMod()` in `OnUnloaded`.

AppDomain registration accepts foreign bridge delegates (`GrimmCoreHurtPrefixHandler` linked into each mod) by rebinding to GrimmCore’s own `HurtPrefixHandler` — a strict `is` check previously dropped them silently.

Link shared bridge in your `.csproj`:

```xml
<Compile Include="..\_Shared\GrimmCoreBridge.cs" Link="GrimmCoreBridge.cs" />
```

## Build

```powershell
.\.cursor\HarmonyMods\0GrimmCore\build.ps1
```

Copies only `0GrimmCore.dll` to root `HarmonyMods/`.
