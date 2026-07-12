# NexusStaticPortals

Harmony mod that keeps only the static, fixed-world Nexus portal part of the old `Portals.cs` plugin.

## Mod Identity

| Item | Value |
|------|-------|
| **Purpose** | Spawn fixed-world Halloween portal entrances that transfer players to another Nexus zone |
| **Entry point** | `NexusStaticPortalsMod` implements `IHarmonyModHooks` |
| **Primary use case** | Hub/Nexus server with hand-placed one-way portals to `svr2`, `svr3`, etc. |
| **Config / data / images** | Config: `HarmonyConfig/NexusStaticPortals.json`; unlock data: **`nexus_unlocks.json`** (same as Oxide `Portals.cs`) under **Custom portals data directory**, or `HarmonyData/nexus_unlocks.json` if unset; legacy `NexusStaticPortals_unlocks.json` in that folder is merged once on load |

## Project Structure

| File | Responsibility |
|------|----------------|
| `NexusStaticPortalsMod.cs` | Lifecycle, spawn/despawn, portal usage, Nexus transfer, one-time unlock persistence |
| `NexusStaticPortalsConfig.cs` | Config schema and `HarmonyConfig` load/import logic |
| `Patches/BasePortal_UsePortal_Patch.cs` | Intercepts use of only the portals spawned by this mod |
| `Patches/ServerMgr_Initialize_SpawnPatch.cs` | Schedules spawn after the server finishes initialization |
| `NexusStaticPortals.csproj` | `net48` Harmony mod project |
| `build.ps1` | Builds Release and copies the DLL to server-root `HarmonyMods/` |

## Config

Config file:

`HarmonyConfig/NexusStaticPortals.json`

On first load, if that file does not exist but `oxide/config/Portals.json` does, the mod imports the old plugin config into the Harmony config path.

### Supported config shape

This mod intentionally supports only the Nexus/static subset:

- `Debug Enabled`
- `Default Exit Door Prefab`
- `Custom portals data directory`
- `Portals[]`
  - `Name`
  - `TeleportationTime`
  - `NexusTransferTargetZoneKey`
  - `NexusOneTimeScrapCost`
  - `NexusUnlockCurrencyShortName`
  - `NexusPrerequisitePortalName`
  - `EntranceAnchors[]`
    - `UseFixedWorldTransform`
    - `WorldPosition`
    - `WorldEulerAngles`
    - `WorldScale`
    - `WorldPositionOffset`
    - `FixedWorldLocalOffset`
    - `SnapPortalBottomToWorldY`
    - `PortalBottomTargetWorldY`

## Behavior

- Spawns only **fixed-world entrance portals** with `UseFixedWorldTransform = true`
- Uses the portal prefab from `Default Exit Door Prefab`
- Intercepts `BasePortal.UsePortal` only for the entities this mod spawned
- Runs `NexusServer.TransferEntity(player, zoneKey, "console", false)` so it matches your existing Nexus flow
- Supports optional one-time currency unlock cost and optional prerequisite portal chain

## Lifecycle

- **OnLoaded:** loads or imports config, loads unlock data
- **ServerMgr.Initialize postfix:** schedules first spawn after startup, then one retry pass
- **OnUnloaded:** destroys spawned portal entities and clears runtime state

## What NOT to Touch

- This mod does **not** port local teleports, dungeon spawning, wall-frame anchors, or Oxide hooks
- It is meant to replace only the static Nexus transfer doorway part of `Portals.cs`
- It should be used with your existing Nexus setup (`NexusSelfHost`, `NexusApi`, zone keys, etc.)

## Performance

- Portal lookup is O(1) by spawned entity net ID
- No global entity scan is used during portal interaction
- Spawn work happens only at startup / retry, not on a hot path

## Build And Deploy

1. Set `RUST_MANAGED` if the script cannot find your Rust managed assemblies:
   `\$env:RUST_MANAGED = "C:\Path\To\RustDedicated_Data\Managed"`
2. From this folder run:
   `powershell -ExecutionPolicy Bypass -File build.ps1`
3. The script copies `NexusStaticPortals.dll` to server-root `HarmonyMods\`
4. Load with:
   `harmony.load NexusStaticPortals`
