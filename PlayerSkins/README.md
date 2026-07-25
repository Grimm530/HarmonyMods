# PlayerSkins Harmony Mod (3.0.141)

Oxide-free Harmony port of **PlayerSkins 3.0.141** (Chaos UI). Uses **0Permissions** for access checks. Chaos UI framework is vendored (same approach as AutoCodeLock).

## Load order

1. **0Permissions.dll**
2. **PlayerDLCAPI.dll** (recommended for paid skin ownership)
3. **Economics.dll** (optional, if config currency is Economics)
4. **RustRewards / ServerRewards** (optional, if config currency is ServerRewards)
5. **PlayerSkins.dll**

```text
harmony.load 0Permissions
harmony.load PlayerDLCAPI
harmony.load Economics
harmony.load PlayerSkins
```

`HarmonyConfig/PlayerSkins.json` already uses **Economics** as purchase currency. PlayerSkins binds to `EconomicsHarmony.EconomicsHarmonyMod` via `Economics_ApiType` / `Economics_Plugin` (same bridge as Shop/RaidableBases).

After `harmony.reload 0Permissions`, PlayerSkins auto-rebinds permissions (generation + ready callback). You do **not** need to reload PlayerSkins.

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/PlayerSkins.json` |
| Data | `HarmonyData/PlayerSkins/userdata.json`, `skinlist.json`, `excludedskins.json` |
| Lang | `HarmonyLanguage/PlayerSkins.json` (optional file overrides embedded defaults) |

## Permissions

| Permission | Purpose |
|------------|---------|
| `playerskins.shop` | Open skin shop |
| `playerskins.reskin` | Open re-skin menu |
| `playerskins.nocharge` | Free skin purchases |
| `playerskins.admin` | Admin skin shop options |
| `playerskins.addskin` | `/addskin` workshop import |
| `playerskins.vip*` | Custom VIP skin tiers (from config) |

## Commands

| Chat | Default | Purpose |
|------|---------|---------|
| Skin menu | `/skin` | Re-skin held/hotbar item |
| Skin shop | `/skin shop` or `/skinshop` | Open skin shop UI |
| Re-skin | `/reskin` | Direct re-skin menu |
| Add skin | `/addskin` | Import workshop skin IDs (admin perm) |

| Console | Purpose |
|---------|---------|
| `playerskins.skins` | Import/remove workshop skins or collections |
| `playerskins.setprice` | Set category skin prices |
| `playerskins.giveskin` | Grant a purchased skin to a player |

Command names come from `HarmonyConfig/PlayerSkins.json`.

## CUI

Button commands are rewritten in `ChaosUI.Show` to `cui.endtest PLAYERSKINS playerskins.callback ...`, then routed to `CommandCallbackHandler.HandleCallback` (AutoCodeLock pattern).

## Soft dependencies

- **Economics** (optional): purchase currency when config `Purchase Options` type is Economics
- **ServerRewards / RustRewards** (optional): ServerRewards currency
- **PlayerDLCAPI** (optional): paid skin ownership filtering
  (`PlayerDlcApi_ApiType`); the bridge re-resolves the live API after reload
- **ImageLibrary** (optional): search magnify icon; falls back to HTTP + FileStorage when absent

## Build

```powershell
.\.cursor\HarmonyMods\PlayerSkins\build.ps1
```

Copies `PlayerSkins.dll` to `HarmonyMods/`.

Regenerate plugin body from Oxide reference:

```powershell
.\.cursor\HarmonyMods\PlayerSkins\convert-from-oxide.ps1
```

## Port notes

- Source: `.cursor/Oxide.Plugins.Cant-Use/PlayerSkins.cs`
- Uses existing `HarmonyConfig/PlayerSkins.json` and `HarmonyData/PlayerSkins/` data from Oxide migration
- NPC shop/reskin NPCs (`OnUseNPC`): method is ported; wire NPC user IDs in config when a dedicated NPC interaction patch is added (same gap as Shop/Kits Harmony ports)
