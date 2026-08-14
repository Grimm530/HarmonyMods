# DynamicCupShare Harmony Mod (3.1.23)

Oxide-free Harmony port of **DynamicCupShare 3.1.23** (Chaos UI). Uses **0Permissions** for access checks. Chaos UI framework is vendored (same approach as AdminMenu / AutoCodeLock).

## Load order

1. **0Permissions.dll** (preferred first)
2. **DynamicCupShare.dll**

```text
harmony.load 0Permissions
harmony.load DynamicCupShare
```

Unload or disable the Oxide plugins `DynamicCupShare.cs`, `BlueprintShare.cs`, and `BuildingWorkbench.cs` before loading this mod.

After `harmony.reload 0Permissions`, DynamicCupShare auto-rebinds and re-registers `dynamiccupshare.*` (generation + ready callback). You do **not** need to reload DynamicCupShare.

On load, player-facing perms are granted to group **`default`** (share, blueprints, building workbench). Admin-only perms are not.

## Permissions

| Permission | Who | Purpose |
|------------|-----|---------|
| `dynamiccupshare.canclanshare` | default | Required only if clan-share permission is enabled in config |
| `dynamiccupshare.canfriendshare` | default | Required only if friend-share permission is enabled in config |
| `dynamiccupshare.canteamshare` | default | Required only if team-share permission is enabled in config |
| `dynamiccupshare.adminmode` | admin | Toggle admin mode (`/dcsadmin`) |
| `blueprintshare.use` | default | Auto-share studied / tech-tree blueprints |
| `blueprintshare.toggle` | default | `/bs toggle` |
| `blueprintshare.share` | default | `/bs share <player>` |
| `blueprintshare.show` | default | `/bs show team\|clan\|friend [name]` |
| `blueprintshare.bypass` | admin | Share via `/bs share` with players outside team/clan/friends |
| `buildingworkbench.use` | default | Use the highest workbench anywhere in an authorized building |
| `buildingworkbench.cancelcraft` | default | Cancel queued crafts that need a higher bench when you leave |

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/DynamicCupShare.json` |
| Data | `HarmonyData/DynamicCupShare/user_data.json` |
| Temp shares | `HarmonyData/DynamicCupShare/temporary_shares.json` |
| Lang | `HarmonyLanguage/DynamicCupShare.json` (file wins over embedded defaults) |

On first load, missing Harmony files are copied from `oxide/config/DynamicCupShare.json` and `oxide/data/DynamicCupShare/` when those exist. Oxide `oxide/data/BlueprintShare.json` is imported once into `user_data.json`.

## Commands

| Chat | Default | Purpose |
|------|---------|---------|
| Share menu | `/share` | Sharing toggles + **Commands** tab (all chat commands and options). Takes priority over TruePVE PreventLooting’s old `/share`. |
| Share player | `/shareplayer <steamid>` | Admin-only: edit another player's shares |
| Admin mode | `/dcsadmin` | Toggle cupboard admin auth mode |
| Blueprints | `/bs` | Opens the Commands tab (`toggle` / `share` / `show` still work in chat) |

## CUI

Button commands are rewritten in `ChaosUI.Show` to `cui.endtest DYNAMICCUPSHARE dynamiccupshare.callback …`, then routed to `CommandCallbackHandler.HandleCallback` (same pattern as AdminMenu).

## Soft dependencies

- **Clans** (optional Harmony or Oxide plugin): `GetClan` / `IsClanMember` / `IsMemberOrAlly`. Falls back to vanilla `ClanManager` when no plugin is loaded.
- **Friends** (optional Harmony or Oxide plugin): `HasFriend` / `GetFriends`.
- Membership-change rebuilds: Oxide Clans/Friends hooks are observed when Oxide is present. Harmony Clans/Friends can call `DynamicCupShareMod.NotifyFriendChanged` / `NotifyClanMembersChanged`.

## Build

```powershell
.\.cursor\HarmonyMods\DynamicCupShare\build.ps1
```

Copies `DynamicCupShare.dll` to `HarmonyMods/`.

## Port notes

- Source: `oxide/plugins/DynamicCupShare.cs` + `oxide/plugins/BlueprintShare.cs` + `.cursor/Origionals/BuildingWorkbench.cs`
- ProtoBuf storage dropped — JSON only under `HarmonyData`
- Cupboard temp-share restore now uses `temporaryCupboardShares` (Oxide copy used turret shares by mistake)
- SAM friend/team share flags aligned with turret share logic
- Blueprint sharing is a `/share` type. Auto-share runs when both players have Blueprints enabled for that relationship and the sharer has `blueprintshare.use`
- Building Workbench extends workbench range to the whole authorized building (and boats). Toggle it on the Commands tab. Clan/friend/team cupboard share already auths those players, so they get the same range once they have `buildingworkbench.use`.
