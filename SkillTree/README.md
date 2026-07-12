# SkillTree Harmony Mod

Port of SkillTree 1.7.121 (imthenewguy / Grimm530) to the Oxide-free Harmony-first stack.

## Identity

| | |
|---|---|
| Assembly | `SkillTree.dll` |
| Namespace | `SkillTreeHarmony` / `Oxide.Plugins.SkillTree` (partial) |
| Entry point | `SkillTreeHarmony.SkillTreeMod : IHarmonyModHooks` |

## Paths

| Resource | Path |
|---|---|
| Config | `HarmonyConfig/SkillTree.json` |
| Shared data (PCDDATA) | `HarmonyData/SkillTree.json` |
| Default player data dir | `HarmonyData/SkillTree/` |
| Custom player data dir | Configured via `CustomSkillTreeDataDirectory` in config (currently `C:\!DataPersistence\oxide\data\SkillTree`) |
| Logs | `HarmonyData/SkillTree/logs/` |
| Language overrides | `HarmonyLanguage/SkillTree.json` (optional) |

The `CustomSkillTreeDataDirectory` is already set in the live config to the DataPersistence path, so player skill data writes there automatically.

## Build

```powershell
cd c:\!2XRUST\.cursor\HarmonyMods\SkillTree
.\build.ps1
```

Output: `c:\!2XRUST\HarmonyMods\SkillTree.dll`

Set `RUST_MANAGED_PATH` or `RUST_SERVER_ROOT` env vars if the auto-detected paths are wrong.

## Load Order

1. `Permissions.dll` (PermissionsHarmony) **must be loaded first** — permission checks will warn and fail gracefully if missing.
2. `SkillTree.dll`

```
harmony.load Permissions
harmony.load SkillTree
```

## Commands

### Chat (players)
| Command | Description |
|---|---|
| `/st` `/skilltree` `/skills` | Open skill tree menu |
| `/score` `/scoreboard` | Open XP scoreboard |
| Various config-driven commands | See config `chat_commands` section |

### Console (admin)
| Command | Description |
|---|---|
| `ST_UI <args>` | CUI callback (internal) |
| Config-driven console commands | Registered from plugin cmd calls |

## Architecture

```
SkillTree.dll
  SkillTreeHarmony.SkillTreeMod        IHarmonyModHooks entry point
  SkillTreeHarmony.ModRunner           MonoBehaviour: NextTick + coroutines
  SkillTreeHarmony.PermissionsBridge   Reflection bridge to PermissionsHarmony
  Oxide.Plugins.SkillTree (partial x2)
    SkillTreePlugin.cs                 Original plugin body (untouched)
    SkillTreeDispatch.cs               Lifecycle + Dispatch_* static methods
  OxideCompat.cs                       Oxide.Core / Oxide.Plugins shims
  RustCui.cs                           Oxide.Game.Rust.Cui shims
  Patches/                             Harmony patches -> Dispatch_* calls
```

## Optional Integrations

ImageLibrary, Economics, ServerRewards, RaidableBases, ZoneManager, and other optional plugins are resolved via AppDomain at runtime. If absent, SkillTree degrades gracefully (no images, no economy respec, etc.).

## Known Compile Risks

- `Patch_MiscGameHooks.cs`: Several game methods (`AntiHack.ReportViolation`, `ResearchTable.ResearchPrice`, `ScientistNPC.CanTargetEntity`, `BaseMelee.ServerUse`) use internal method names that may differ across Rust updates. If any patch class fails to compile, comment it out and add to a `[HarmonyPatch]` manually or remove from `.csproj`.
- `Patch_ItemCrafter.cs`: `ItemCrafter.CraftItem` signature varies across Rust builds. The patch uses parameter filtering; if the parameter count differs, the patch will not apply (non-fatal).
- `Patch_MiscGameHooks.cs` `BaseNetworkable_Spawned_Patch`: May be high-frequency. Monitor performance on busy servers.
- Fish hooks (`OnFishCatch`, `CanCatchFish`, `OnFishingStopped`) are not patched via game methods — SkillTree's own internal `[HarmonyPatch(BaseFishingRod, "Server_RequestCast")]` handles these directly from within the plugin partial class.
