# RaidableBasesUI (Harmony Mod)

Standalone Harmony mod that provides the **CUI (Community UI)** for RaidableBases: buyable events panel, cooldowns, delay, lockout, status, and teleport UIs. Converted from `RaidableBases.UI.cs` to run as a separate Harmony mod. Harmony-only dependency; can be called by RaidableBases (Oxide or Harmony) via reflection.

## Mod Identity

| Item | Value |
|------|--------|
| **Purpose** | CUI for RaidableBases: buyable panel, cooldowns, delay, lockout, status, teleport |
| **Entry point** | `RaidableBasesUIMod` implements `IHarmonyModHooks` |
| **Loading** | `harmony.load RaidableBasesUI` (from `HarmonyMods/RaidableBasesUI.dll`) |

## Project Structure

| File | Responsibility |
|------|----------------|
| `RaidableBasesUIMod.cs` | Lifecycle, config load, API for RaidableBases to call |
| `RaidableBasesUIConfig.cs` | UI config model; load/save `HarmonyConfig/RaidableBasesUI.json` |
| `RaidableBasesUIHandler.cs` | Build CUI as JSON, send via `CommunityEntity.ServerInstance.ClientRPC`, persist offsets |
| `Patches/Cui_Endtest_Patch.cs` | Intercept `cui.endtest RB_UI` for button commands |

## Config & Data (per HARMONY_MODS_GUIDE)

| Use case | Location | Requirement |
|----------|----------|-------------|
| **Mod DLL** | `HarmonyMods/` | Build script copies DLL here. |
| **Config** | `HarmonyConfig/RaidableBasesUI.json` | Created on first load if missing. |
| **Data** | `HarmonyData/RaidableBasesUI/offsets.json` | Per-player UI position offsets. |

## CUI Button Commands

CUI runs on the **client**; only replicated commands reach the server. This mod uses **`cui.endtest RB_UI ...`** (vanilla `cui.endtest` is replicated) so buttons work for all players, including those who join after server start.

| Button command | Handler |
|----------------|---------|
| `cui.endtest RB_UI ui_buyraid closeui` | Destroy buyable panel. |
| `cui.endtest RB_UI ui_buyraid &lt;mode&gt;` | Forward to server command `ui_buyraid &lt;mode&gt;` (RaidableBases handles purchase). |
| `cui.endtest RB_UI rb_ui_move Buyable left/right/up/down` | Adjust stored offset for buyable panel; RaidableBases refreshes UI on next update. |

## API for RaidableBases (reflection)

RaidableBases (Oxide or Harmony) can call this mod via reflection:

| Method | Purpose |
|--------|---------|
| `RaidableBasesUIHandler.ShowBuyableUi(player, buttons, moveUi, titleText, buttonColorHex, textColorHex, useContrast)` | Show buyable events panel. `buttons`: list of `(mode, text, command)` e.g. `("easy", "Easy - 100 RP", "ui_buyraid easy")`. |
| `RaidableBasesUIHandler.DestroyAllUi(player)` | Destroy all RaidableBasesUI panels for the player. |
| `RaidableBasesUIHandler.MoveBuyableUi(player, direction)` | Adjust buyable panel offset (`"left"`, `"right"`, `"up"`, `"down"`). |
| `RaidableBasesUIHandler.CloseBuyableUi(player)` | Close buyable panel. |

**Type to search:** `RaidableBasesUI.RaidableBasesUIHandler` (static methods). Use `Assembly.GetType("RaidableBasesUI.RaidableBasesUIHandler")` then `GetMethod("ShowBuyableUi", ...)`.

## Harmony Patches

| Patch | Target | Purpose |
|-------|--------|---------|
| `Cui_Endtest_Patch` | `cui.endtest` | Prefix: when `args[0] == "RB_UI"`, handle ui_buyraid / rb_ui_move and return false; else run original. |

## Lifecycle

- **OnLoaded:** Set `Instance`, load config from `HarmonyConfig/RaidableBasesUI.json`, load offset data from `HarmonyData/RaidableBasesUI/offsets.json`, ensure data directory exists.
- **OnUnloaded:** Clear `Instance`.

## What This Mod Does Not Do

- Does **not** call Oxide or any Oxide APIs.
- Does **not** implement raid logic, pricing, cooldowns, or event spawning; RaidableBases provides that and passes data into this mod’s API.
- Cooldown/Lockout/Delay/Status/Teleport UIs from the original `RaidableBases.UI.cs` can be added in a later iteration; this version focuses on the **buyable panel** and the **cui.endtest** routing so RaidableBases can plug in.

## Build & Deploy

```powershell
cd .cursor\HarmonyMods\RaidableBasesUI
.\build.ps1
```

Output: `HarmonyMods/RaidableBasesUI.dll`. Load: `harmony.load RaidableBasesUI`.

## References

- **HARMONY_MODS_GUIDE.md** – Harmony mod layout, CUI button commands (`cui.endtest`), config/data paths.
- **Harmony CUI (Community UI) Reference Files.md** – CUI JSON format, `CommunityEntity.ServerInstance.ClientRPC`, parents (`Hud`, `Overlay`).
- **Leaderboard (README.md)** – CUI built with `JArray`/`JObject`, `RpcTarget.Player("AddUI", connection)`.
- **TCUpgrade (Cui_Endtest_Patch, README)** – `cui.endtest SENDCMD` pattern; this mod uses `cui.endtest RB_UI` for RaidableBases UI.
