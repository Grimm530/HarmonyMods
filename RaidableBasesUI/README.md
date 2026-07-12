# RaidableBasesBuyableUI (Harmony Mod)

Faithful Harmony port of Oxide **RaidableBasesBuyableUI** 1.0.61 (gallery UI with categories, per-base images, color/transparency prefs). Replaces the older thin `RaidableBasesUI` companion panel.

## Identity

| Item | Value |
|------|--------|
| **DLL** | `HarmonyMods/RaidableBasesBuyableUI.dll` |
| **Source** | `.cursor/HarmonyMods/RaidableBasesUI/` |
| **Config** | `HarmonyConfig/RaidableBasesBuyableUI.json` |
| **Data** | `HarmonyData/RaidableBasesBuyableUI/` |
| **Load** | `harmony.load RaidableBasesBuyableUI` |

## Permissions (Permissions mod)

| Permission | Purpose |
|------------|---------|
| `raidablebasesbuyableui.allow` | Show gallery on empty `/buyraid` - auto-granted to `default` |
| `raidablebasesbuyableui.spawn.filenames` | Per-base image grid (Oxide UX) - **also auto-granted to `default`**; category click always opens the base grid in this mod |
| `raidablebasesbuyableui.spawn.bypass` | Do not track purchased base names |

All players get the gallery by default via `default` group. **RaidableBases built-in Buyable Events UI must stay disabled** (`HarmonyConfig/RaidableBases.json` -> `Buyable Events UI` -> `Enabled: false`) so this mod owns `/buyraid`. This mod also redirects `ShowBuyableUi` if RB still tries to open its panel.

## Data layout

| Path | Contents |
|------|----------|
| `HarmonyData/RaidableBasesBuyableUI/Images/` | `gradient_*.png`, `boy.png`, `backdrop.png` |
| `HarmonyData/RaidableBasesBuyableUI/raids/` | Per-base PNG thumbnails (filename ≈ paste name) |
| `HarmonyData/RaidableBasesBuyableUI/PlayerPreferences.json` | Per-player color / transparency |
| `HarmonyData/RaidableBases/Profiles/` | Read-only profiles (shared with RaidableBases) |
| `HarmonyData/copypaste/` | Paste files checked when indexing profiles |

## Integration with RaidableBases

1. Empty `/buyraid` → RB calls `Interface.CallHook("OnPurchaseBase", …)` → this mod’s Harmony prefix shows the gallery when `allow` is granted.
2. Category / base clicks → `cui.endtest RBBUI ui_buyable_*` → purchase via `ui_buyraid <name>`.
3. `OnRaidableBasePurchased` → tracks owned bases (grey out until category exhausted).
4. `OnPurchaseTakePayments` → blocks repurchase of an already-tracked base.

Load order: **Permissions** → **RaidableBases** → **RaidableBasesBuyableUI** (UI will retry CallHook patch for ~90s if RB loads later).

## Commands

| Command | Who | Effect |
|---------|-----|--------|
| `uit` | Admin | Open gallery (test) |
| `rbbui.reloadimages` | Admin | Reload raids PNGs |
| CUI buttons | Players | `ui_buyable_show` / `purchase` / `changepage` / `color` / `transparency` |

## PNPC API (reflection)

Public methods on `RaidableBasesBuyableUI.RaidableBasesBuyableUIPlugin` (also published as AppDomain `RaidableBasesBuyableUI_ApiType`):

- `OpenForPNPCBuilder(BasePlayer)`
- `CloseForPNPCBuilder(BasePlayer)`
- `IsUiOpen(BasePlayer)` / `IsPNPCBuilderMode(BasePlayer)`

External hooks (optional AppDomain `RaidableBasesBuyableUI_ExternalHooks` dictionary of delegates): `OnPNPCBuilderBaseSelected`, `OnPNPCBuilderBaseUiClosed`.

## Build

```powershell
cd .cursor\HarmonyMods\RaidableBasesUI
.\build.ps1
```

## Oxide parity notes

- UI layout/commands match the Oxide plugin as closely as practical.
- Oxide `oxide/data` → `HarmonyData`; `oxide/config` → `HarmonyConfig/RaidableBasesBuyableUI.json`.
- Button commands are rewritten to `cui.endtest RBBUI …` so clients can reach the server (same pattern as Kits).
