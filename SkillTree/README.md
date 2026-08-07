# SkillTree Harmony Mod

Port of SkillTree 1.7.122 (imthenewguy / Grimm530) to the Oxide-free Harmony-first stack.

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

Facepunch loads `HarmonyMods/*.dll` alphabetically (filesystem order). Typical order here:

`MovementSpeed` → `Permissions` → `SkillTree`

**Do not rely on a manual load sequence.** SkillTree binds via ready callbacks:

- `Permissions_ReadyCallbacks` → re-register skilltree.* permissions
- `MovementSpeed_ReadyCallbacks` → rebind RoadRunner / swim `PluginReference`

RoadRunner needs `MovementSpeed.dll` present; if it is missing, those buffs soft-fail (null checks).

## Commands

### Chat (players)
| Command | Description |
|---|---|
| `/st` `/skilltree` `/skills` | Open skill tree menu |
| `/score` `/scoreboard` | Open XP scoreboard |
| Various config-driven commands | See config `chat_commands` section |

Undotted chat aliases (`st`, `skilltree`, `skills`, `score`, `scoreboard`, plus config-driven names) are registered as **unreplicated** server console commands. Player chat (`/st`, `/setgenes`, …) is handled by `ChatSayBridge` on `chat.say`. They must **not** be added to `Index.Server.Replicated` — clients have no ConsoleGen entries and spam `Replicated convar not found on client: global.setgenes` (etc.) on join. UI/console handlers stay unreplicated.

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
