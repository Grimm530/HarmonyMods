# RustVehiclesGUI (Harmony port of Oxide RustVehiclesGUI 1.0.5)

Harmony-first, Oxide-free port of **RustVehiclesGUI** (Grimm530). This mod is the *interface only*:
vehicle licences, purchases and spawns are owned by the **RustVehicles** Harmony mod, which this mod
reaches through the `RustVehicles_Plugin` AppDomain bridge.

## Load order

```
0Permissions  ->  RustVehicles  ->  ServerPanel  ->  RustVehiclesGUI
```

- `0Permissions.dll` backs `RustVehicles.use` / `VehicleLicence.use` and the per-vehicle permissions.
- `RustVehicles.dll` is the core plugin. Without it the GUI shows "Vehicle system plugin is not loaded!".
- `ServerPanel.dll` is optional; it is only needed for the in-panel VEHICLES tab.
- `Economics` / `RustRewards` are optional balance sources for the price display.

## Load

```
harmony.load RustVehiclesGUI
```

## Paths

| Purpose | Path |
| --- | --- |
| Config | `HarmonyConfig/RustVehiclesGUI.json` |
| Vehicle card images (PNG) | `HarmonyData/RustVehiclesGUI/images/` |
| Per-player UI settings | `HarmonyData/RustVehiclesGUI/players/playerSettings.json` |
| Core config it reads | `HarmonyConfig/RustVehicles.json` (falls back to `VehicleLicence.json`) |
| Core data it reads | `HarmonyData/RustVehicles/RustVehicles.json` |

## ServerPanel integration

`HarmonyData/ServerPanel/Categories.json` already points the VEHICLES tab at
`Plugin Name: RustVehiclesGUI` / `Plugin Hook: API_OpenPlugin`.

- `API_OpenPlugin(BasePlayer)` returns a `CuiElementContainer` whose elements parent to
  `UI.Server.Panel.Content` (ServerPanel serializes it across the assembly boundary via `ForeignCui`).
- `OnServerPanelClosed(BasePlayer)` clears the image queue, page state and per-player caches.
- `OnServerPanelCategoryPage(BasePlayer, object, int)` drops the pending image queue when the panel
  switches category or page. It returns void on purpose: ServerPanel cancels the switch on any
  non-null hook result.
- `RefreshServerPanelContent` redraws the panel by invoking the registered `UI_ServerPanel` console
  command server-side. The Oxide version used `SendConsoleCommand`, which the client will not forward
  under Harmony.

## API for other mods

```csharp
AppDomain.CurrentDomain.GetData("RustVehiclesGUI_ApiType");  // typeof(RustVehiclesGUIHarmonyMod)
AppDomain.CurrentDomain.GetData("RustVehiclesGUI_Plugin");   // IsLoaded + Name + Version + Call(string, object[])
```

`ServerPanelCompat` resolves the `rustvehiclesgui` category name to `RustVehiclesGUI_Plugin` first and
only falls back to `RustVehicles_Plugin`, so the GUI mod wins whenever both are loaded.

## Commands

Chat aliases come from the `Chat Commands` list in `HarmonyConfig/RustVehiclesGUI.json`
(currently `license`, `l`, `vb`, `vgui`, `vlgui`, `vehiclegui`) and all open the standalone GUI.

> `license` and `l` are also RustVehicles chat commands. Both mods prefix-patch `chat.say`, so whichever
> loads last wins that alias. Remove the overlapping entries from one of the two configs if the standalone
> GUI and the `/license` help text fight over it.

Console commands (registered by the mod, driven by the CUI buttons): `vgui.main`, `vgui.shop`,
`vgui.manage`, `vgui.buy`, `vgui.spawn`, `vgui.recall`, `vgui.pickup`, `vgui.kill`, `vgui.close`,
`vgui.setcolor`, `vgui.transparency`, `vgui.nextpage`, `vgui.prevpage`, `vgui.manage.nextpage`,
`vgui.manage.prevpage`, `vgui.clearcache`, `vgui.imgdump`, plus the `vgui.serverpanel.*` family.

CUI buttons carry `vgui.*` commands that the client will not forward, so `RustCui.cs` rewrites them to
`cui.endtest VGUI vgui.<name> ...` and `Patches/Cui_Endtest_Patch.cs` routes them back. ServerPanel's
own `RustCui.cs` carries the same `vgui.` marker so embedded panel pages work the same way.

Buy / spawn / recall / pickup / kill still go out as `chat.say /<command>` to the player, which the
RustVehicles chat patch picks up server-side - the same flow as the Oxide plugin.

## Build

```powershell
.\convert-from-oxide.ps1   # regenerate RustVehiclesGUI.cs from the Oxide source
.\build.ps1
```

`build.ps1` builds Release and copies only `RustVehiclesGUI.dll` to `<server root>\HarmonyMods\`.

`convert-from-oxide.ps1` rewrites the Oxide source: strips the Oxide usings and namespace, rebases the
class on `RustVehiclesGUIPluginBase`, turns the `[PluginReference]` fields into live `PluginBridges`
properties, repoints the core config/data paths at `HarmonyConfig` / `HarmonyData`, unwraps
`player.userID` at bridge call sites, and appends the Harmony lifecycle plus the ServerPanel hooks.
