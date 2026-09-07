# RaidableBases 3.2.634 (Standalone Harmony Mod)

Fresh port from **nivex RaidableBases 3.2.634** — Harmony-only dependency. Previous 3.1.7 adaptation is preserved in `../RaidableBases_3.1.7_legacy/`.

## Mod identity

| Field | Value |
|-------|--------|
| **Upstream** | RaidableBases 3.2.634 (nivex) |
| **Source** | `.cursor/Origionals/RaidableBases3.2.634.cs` |
| **Adapt script** | `adapt-3.2.634.ps1` |
| **Entry** | `RaidableBases.RaidableBasesHarmonyEntry` (`IHarmonyModHooks`) |

## Project structure

| File | Content |
|------|---------|
| `RaidableBasesHarmony.cs` | Bridge: host, data layer, permissions, hooks, soft-start |
| `RaidableBases.Main.cs` | Core types, HarmonyEngine, RaidableBase, NPC Gen2 |
| `RaidableBases.Misc.cs` | TargetInfoController and other non-region classes |
| `RaidableBases.Hooks.cs` | Lifecycle + hook handlers |
| `RaidableBases.Paste.cs` | Built-in **PasteEngine** (no CopyPaste plugin required) |
| `RaidableBases.*.cs` | Other regions (Spawn, Commands, Config, UI, …) |
| `Patches/` | Harmony patches → `HarmonyModInterface.CallHook` for compat-style hooks |
| `adapt-3.2.634.ps1` | Regenerate partials from upstream `.cs` |

## Data paths

| Data | Path |
|------|------|
| Config | `HarmonyConfig/RaidableBases.json` |
| Profiles, loot, spawns | `HarmonyData/RaidableBases/` |
| Base paste JSON files | `HarmonyData/copypaste/` |

3.2.634 uses an internal **PasteEngine** — it reads paste JSON from the copypaste data folder via `HarmonyDataLayer`. The separate CopyPaste Harmony mod is **not required** for pasting.

## Build

Set `RustManaged` to your server's `RustDedicated_Data/Managed` folder (or use `RUST_MANAGED_PATH`).

```powershell
.\.cursor\HarmonyMods\RaidableBases\build.ps1
```

Output: `HarmonyMods/RaidableBases.dll`

## Load order

1. **0Permissions** — `raidablebases.*` permissions
2. **Kits** (optional) — profile Scientist/Murderer kit names
3. **RaidableBases**

CopyPaste is optional in 3.2.634 (built-in paste engine).

## Re-adapt from upstream

When nivex releases a new `.cs`:

```powershell
# Drop new file in .cursor/Origionals/RaidableBasesX.Y.Z.cs
# Update $version and $oxideSrc in adapt-3.2.634.ps1 if needed
.\adapt-3.2.634.ps1
# Re-apply any manual fixes (InitializeSkinsCoroutine, bridge tweaks, GRIMM_PAPER_SKIN)
.\build.ps1
```

**Local Grimm fixes (must re-apply after adapt):**
- Force paper loot/rewards to skin `2961180853` (`GRIMM_PAPER_SKIN`) and convert any vanilla paper already in raid containers. See `../RaidableBases_3.1.7_legacy/RaidableBases.Main.cs` for the reference.
- Raid AutoTurrets: building-aware LOS (twig blocks sight) and no damage passthrough through twig onto players.

## Legacy

The 3.1.7 Harmony port (CopyPaste-dependent, older hook set) lives in **`../RaidableBases_3.1.7_legacy/`** for rollback reference.
