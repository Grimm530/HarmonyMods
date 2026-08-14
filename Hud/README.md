# Hud (Harmony OxideCompat port)

Port of Oxide `Hud` 3.4.0. Server HUD: players, time, grid, events, economy, extra menu.

## Paths

| | |
|---|---|
| Config | `HarmonyConfig/Hud.json` |
| Data | `HarmonyData/Hud/` (`data.json`) |
| Images | `HarmonyImages/Hud/` (local PNG via FileStorage + remote UnityWebRequest; no ImageLibrary) |
| Runtime DLL | `HarmonyMods/Hud.dll` |
| Source | `.cursor/HarmonyMods/Hud/` |

## Build

```powershell
cd .cursor\HarmonyMods\Hud
.\build.ps1
```

Copies **only** `Hud.dll` to root `HarmonyMods/`.

## Load

- Auto-loads with other Harmony mods. Requires **Permissions** for `hud.streamer` and extra-menu perms.
- Do **not** run the Oxide plugin at the same time — leave `oxide/plugins/Hud.cs` in place but unloaded.
- Chat: `/h` (`open`, `events`, `hide`, `close`, `setup`).
- CUI: `cui.endtest HUD UI_H …`.
- AppDomain: `Hud_ApiType` → `HudHarmony.HudMod` (`Call`, including `API_PlayerHudState`).

## Economy

Resolves Harmony mods via AppDomain (no Oxide PluginReference):

- **Economics** → `Economics_ApiType` / `Economics_Plugin` (`Balance`)
- **ServerRewards** config → `RustRewards_ApiType` / `RustRewards_Plugin` (`CheckPoints`)
- **ShoppyStock** config → `Shop_ApiType`

## Patch notes (this build)

Postfix/prefix observers on player connect/sleep/queue, `BaseNetworkable.Spawn`/`Kill` (Bradley, heli, CH47, cargo, airdrop), `CargoShip.OnArrivedAtHarbor` / `LeaveHarbor`, `HackableLockedCrate.StartHacking` / `HackProgress`, `DeepSeaManager.OpenDeepSea` / `CloseDeepSea`, computer-station mount. Custom event hooks are config-driven (`OnEventTouch`); the Harmony port does not rewrite `oxide/plugins/Hud.cs`.
