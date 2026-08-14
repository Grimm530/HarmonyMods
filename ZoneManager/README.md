# ZoneManager (Harmony)

Oxide **Zone Manager 3.1.11** (k1lly0u / Grimm530) port as a standalone Harmony mod (no Oxide runtime).

TruePVE, SkillTree, and other mods resolve this plugin via AppDomain keys `ZoneManager_ApiType` / `ZoneManager_Plugin` and `Call(...)`.

## Load order

1. **0Permissions** (`0Permissions.dll`)
2. **ZoneManager** (`ZoneManager.dll`)
3. Optional: **Spawns** (`Spawns.dll`) — eject spawnfiles; missing is a soft-fail
4. Optional: **Backpacks** — already a Harmony mod; used when looting your own backpack in NoPlayerLoot zones

**PopupNotifications** is a no-op; enter/leave messages go to chat.

## Deploy

```powershell
.\.cursor\HarmonyMods\ZoneManager\build.ps1
```

Copies **only** `ZoneManager.dll` to `HarmonyMods\ZoneManager.dll`.

Load: `harmony.load ZoneManager` (or automatic at startup). Leave `oxide/plugins/ZoneManager.cs` in place but do not run both.

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/ZoneManager.json` |
| Data | `HarmonyData/ZoneManager/zone_data.json` |
| Lang | Embedded EN + optional `HarmonyLanguage/ZoneManager.json` overrides |

## AppDomain API

```text
ZoneManager_ApiType  → typeof(ZoneManagerHarmony.ZoneManagerMod)
ZoneManager_Plugin   → ZoneManagerMod instance (Call)
```

`Call(string hook, params object[] args)` covers at least:

- `GetPlayerZoneIDs` / `GetEntityZoneIDs` / `GetZoneIDs` / `GetZoneIDsNoAlloc`
- `isPlayerInZone` / `IsPlayerInZone` / `IsEntityInZone`
- `GetZoneName` / `GetZoneLocation` / `GetZoneRadius` / `GetZoneSize`
- `CreateOrUpdateZone` (alias `CreateZone`) / `EraseZone` (alias `erase`)
- `AddFlag` / `RemoveFlag` / `HasFlag` / `PlayerHasFlag` / `EntityHasFlag`
- `CanTeleport` / `CanRedeemKit` / `CanShop` / `CanTrade` / `CanRemove`
- `ZoneFieldList`

Enter/exit events (`OnEnterZone` / `OnExitZone`) are forwarded to SkillTree (`Dispatch_OnEnterZone`) and MovementSpeed when those mods are loaded.

## Chat commands

Requires `zonemanager.zone`: `/zone_add`, `/zone_edit`, `/zone`, `/zone_flags`, `/zone_list`, `/zone_remove`, `/zone_wipe`, `/zone_stats`, `/zone_player`, `/zone_entity`

CUI flag editor buttons route through `cui.endtest ZONEMANAGER …`.

## Permissions

| Permission | Effect |
|------------|--------|
| `zonemanager.zone` | Create / edit / list zones |
| `zonemanager.ignoreflag.<flag>` | Bypass a specific zone flag |
