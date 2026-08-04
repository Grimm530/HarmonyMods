# ServerPanel (Harmony)

Harmony port of Mevent's **ServerPanel 2.0.20** with **ServerPanelPopUps 2.0.20** consolidated into a
single assembly. No Oxide/Carbon required.

## Build

```powershell
.\build.ps1
```

The script builds `ServerPanel.csproj` and copies `ServerPanel.dll` to the server root `HarmonyMods/`.

## Load

```
harmony.load ServerPanel
```

## Files used at runtime

| Purpose | Path |
| --- | --- |
| ServerPanel config | `HarmonyConfig/ServerPanel.json` |
| PopUps config | `HarmonyConfig/ServerPanelPopUps.json` |
| ServerPanel data | `HarmonyData/ServerPanel/` (Categories, Template, HeaderFields, Localization, Players) |
| PopUps data | `HarmonyData/ServerPanelPopUps/` |
| Language overrides (optional) | `HarmonyLanguage/<lang>/ServerPanel.json`, `.../ServerPanelPopUps.json` |

Existing configs and data are used as-is. The Oxide `ServerPanelMigrations` gate is skipped because the
data on this server is already at 2.0.20.

## Commands

Fixed console commands: `UI_ServerPanel`, `UI_ServerPanel_Close`, `UI_ServerPanel_Send_Command`,
`serverpanel_broadcastvideo`, `UI_ServerPanel_PopUps`, `serverpanelpopups_broadcastvideo`.

Category commands are registered from `HarmonyData/ServerPanel/Categories.json` as both chat and console
commands (for the current data set: `help`, `info`, `binds`, `rules`, `commands`, `wipe`, `menu_shop`,
`menu_kits`, `vehicles`, `leaderboard`, `rb`, ...). Pop-up commands come from the PopUps config plus
`popupid <id>`.

CUI buttons carry plugin commands that the client will not forward, so they are rewritten to
`cui.endtest <MARKER>` in `RustCui.cs` and routed back by `Patches/Cui_Endtest_Patch.cs`. Markers:
`SERVERPANEL`, `SPCLOSE`, `SPSEND`, `SPVIDEO`, `SPPOPUPS`, `SPPOPVIDEO`. Buttons belonging to embedded
plugin pages are rewritten to the markers their own mods listen for (`SHOP`, `SHOPINST`, `KITS`,
`WIPESCHEDULE`, and `VGUI` for RustVehiclesGUI's `vgui.*` buttons).

## API for other mods

```csharp
AppDomain.CurrentDomain.GetData("ServerPanel_ApiType");        // typeof(ServerPanelHarmonyMod)
AppDomain.CurrentDomain.GetData("ServerPanel_Plugin");         // IsLoaded + Call(string, object[])
AppDomain.CurrentDomain.GetData("ServerPanelPopUps_Plugin");   // IsLoaded + Call(string, object[])
```

`ServerPanel_Plugin` dispatches the full `API_*` surface (`API_OnServerPanelProcessCategory`,
`API_OnServerPanelOpenCategoryByID`, `API_OnServerPanelCallClose`, `API_OnServerPanelClosed`,
`API_OnServerPanelGetCategoryInfo`, `API_GetBackgroundParentLayer`, ...) and also exposes the common ones
as direct methods.

## Plugin pages

Categories with a `Plugin Name` are resolved through AppDomain keys and opened with `API_OpenPlugin`:

| Category plugin | Resolved from |
| --- | --- |
| Shop | `Shop_Plugin` |
| Kits | `Kits_Plugin` (fallback `Kits_ApiType` -> Instance.Plugin) |
| WipeSchedule | `WipeSchedule_Plugin` |
| RustVehiclesGUI | `RustVehiclesGUI_Plugin` (falls back to `RustVehicles_Plugin`) |
| RaidableBasesBuyableUI / RaidableBasesUI | `RaidableBasesBuyableUI_Plugin` |
| UltimateLeaderboard / Leaderboard | `Leaderboard_Plugin` (+ `UltimateLeaderboard_Plugin` alias) |

Containers returned by another mod live in that mod's assembly, so `ForeignCui` serializes them to JSON
before they are merged into the panel.

## Images

There is no ImageLibrary Harmony mod, so `ServerPanelCompat.cs` contains `ImageLibraryBridge`: it
downloads images and stores them in `FileStorage`, exposing `AddImage` / `GetImage` / `HasImage` /
`ImportImageList`. With `Enable Offline Image Mode: false` (current config) images come from the
Mevent CDN on first load; icons can be blank for a few seconds after `harmony.load` while the queue
drains. Setting `Enable Offline Image Mode: true` makes it read `TheMevent/...` paths from
`HarmonyData/` or `HarmonyImages/` instead.

## Known gaps

- The `Leaderboard` Harmony mod does not implement `API_OpenPlugin` or publish a plugin wrapper, so the
  LEADERBOARD category stays empty until it exposes one. VEHICLES is served by the `RustVehiclesGUI` mod.
- ServerPanelAvatars is not ported; header avatars use the Steam avatar path already built into
  ServerPanel.
- The in-game editor is included but only lightly exercised; edits are written back to
  `HarmonyData/ServerPanel/`.
- `Interface.CallHook` only forwards `OnServerPanelClosed` and `OnServerPanelCategoryPage` to the known
  consumer mods; arbitrary Oxide hook broadcasts are no-ops.
