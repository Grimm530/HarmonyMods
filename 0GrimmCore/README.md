# 0GrimmCore

**Agent contract (new mods + Oxide updates):** `.cursor/PluginInstructionalFiles/GrimmCore_Harmony_Infrastructure.md` — also covers `0Permissions`, `0GrimmCUI`, `0GrimmNPC`, and `_Shared`.

Core Harmony infrastructure for Grimmzone servers:

- **Unified `BaseCombatEntity.Hurt` dispatcher** — one Harmony prefix; mods register ordered handlers via `GrimmCoreBridge`
- **Unified `BaseNetworkable.Spawn` dispatcher** — one Harmony postfix (Oxide-style `OnEntitySpawned`); mods register with `GrimmCoreBridge.RegisterSpawnPostfix`
- **Unified Kill + shotgun game-hook dispatcher** — one `[HarmonyPatch]` per Oxide injection (`Kill`, `Item.Insert`, `OnReceiveTick`, Hammer, Save, …) in `Patches/Patch_GameHooks_Unified.cs`. Mods register typed delegates, or `GrimmCorePluginHooks.Bind` which `CreateDelegate`s existing plugin methods **once**. Empty snapshots return immediately.
- **Hook bus** — named inter-mod hooks with 0–3 arg overloads (no `params object[]` packing on the public API). First non-null result wins.
- **Vanilla scientist/scarecrow removal** — if a **map** `HumanNPC` / `ScarecrowNPC` is *not* tagged as a custom Grimm/RaidableBases NPC, we `Kill()` it during the AI `ServerThink` prefix so it does not remain on the map brain-dead.
  - Tagged custom NPCs (skin `3793751435`, `HumanoidNPC`, `CustomScientistNpc`, etc.) still think.
  - **Animals are not patched** and must keep vanilla movement.
- **Shared skinID** — `3793751435` (blank workshop skin) via `GrimmCoreBridge.IsMarkedCustomEntity()`

## Do not restack

Do **not** add `[HarmonyPatch]` on `BaseNetworkable.Spawn`, `BaseCombatEntity.Hurt`, or `BaseNetworkable.Kill`. Do **not** ship a per-mod `GameHooks_Patches.cs` that HarmonyPatches ~30 methods and then `GetMethods`+`Invoke`. Bind or register typed handlers instead.

Kill: observers always run; first non-null `Func<BaseNetworkable, object>` cancels Kill (Oxide ReturnBehavior 1). Hammer is patched separately (`Hammer.DoAttackShared` does not call base).

## Load order

Deploy as `HarmonyMods/0GrimmCore.dll`. Loads before `0Permissions` and most other mods.

Register in `OnLoaded`; unregister in `OnUnloaded`:

- Hurt: `UnregisterHurtMod`
- Spawn: `UnregisterSpawnMod`
- Kill / game hooks: `UnregisterGameHookMod` (or `GrimmCorePluginHooks.Unbind`)

`harmony.reload 0GrimmCore` clears handler lists. Dispatcher / PatchAll changes need a **full server restart**.

AppDomain registration accepts foreign bridge delegates (`GrimmCoreHurtPrefixHandler` linked into each mod) by rebinding to GrimmCore’s own `HurtPrefixHandler` — a strict `is` check previously dropped them silently.

Link shared bridges in your `.csproj` (no `0GrimmCore.dll` reference):

```xml
<Compile Include="..\_Shared\GrimmCoreBridge.cs" Link="GrimmCoreBridge.cs" />
<Compile Include="..\_Shared\GrimmCorePluginHooks.cs" Link="GrimmCorePluginHooks.cs" />
```

## Build

```powershell
.\.cursor\HarmonyMods\0GrimmCore\build.ps1
```

Copies only `0GrimmCore.dll` to root `HarmonyMods/`.
