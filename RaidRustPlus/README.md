# RaidRustPlus

Harmony mod that sends Rust+ raid alerts to building-authorized players when base entities are destroyed.  
This is the Rust+ only slice of `AAlertRaidEN` (no Discord, Telegram, VK, or in-game UI).

## Mod Identity

| Item | Value |
|------|-------|
| **Purpose** | Send Rust+ push raid alerts for destroyed base entities |
| **Entry point** | `RaidRustPlusMod` implements `IHarmonyModHooks` |
| **Patch target** | `BaseCombatEntity.Die(HitInfo)` postfix |
| **Push API** | `CompanionServer.NotificationList.SendNotificationTo` with `NotificationChannel.SmartAlarm` |

## Project Structure

| File | Responsibility |
|------|----------------|
| `RaidRustPlus/RaidRustPlusMod.cs` | Core logic: filters destroyed entities, resolves building auth, builds templates, sends Rust+ notifications, cooldown per owner |
| `RaidRustPlus/BaseCombatEntity_Die_Patch.cs` | Harmony patch that forwards death events to `RaidRustPlusMod` |
| `RaidRustPlus/RaidRustPlusConfig.cs` | Config model and load/create logic (`HarmonyConfig/RaidRustPlus.json`) |
| `RaidRustPlus/RaidRustPlus.csproj` | Build settings and Rust assembly references |
| `build.ps1` | Build and copy DLL into server `HarmonyMods/` |

## Persistent Data Model

- **Config file**  
  - Location: `HarmonyConfig/RaidRustPlus.json` (server root).  
  - Auto-created on first load.
- **Runtime state**  
  - Cooldown dictionary in memory: next send time per authed SteamID.
  - No data file required.

## Behavior

- **Trigger:** entity death via `BaseCombatEntity.Die(HitInfo)`.
- **Attacker filter:** only when `info.InitiatorPlayer` is present.
- **Entity filter:**
  - Building blocks at or above configured minimum grade.
  - Optional extra deployables (`IOEntity`, `DecayEntity`, `AutoTurret`, `SamSite`, custom shortname list).
- **Recipients:** all players authorized on the entity/building privilege.
- **Rust+ checks:** only sends if companion is active (`App.serverid`, `App.port`, `App.notifications`).
- **Cooldown:** per recipient, configurable seconds.

## Config Keys

| Key | Purpose |
|-----|---------|
| `Enabled` | Master switch |
| `Server Name` | `{servername}` template value |
| `Notification Cooldown Seconds` | Per-recipient cooldown |
| `Minimum Building Grade` | 1=wood, 2=stone, 3=metal, 4=toptier |
| `Include Extra Deployables` | Enable non-building alerts |
| `Extra Deployable Prefab Shortnames` | Additional shortnames to include |
| `Rust+ Title Template` | Notification title template |
| `Rust+ Body Template` | Notification body template |

Template tokens: `{name}`, `{steamid}`, `{destroy}`, `{quad}`, `{ip}`, `{servername}`.

## What Not To Touch Without Care

- `NotificationChannel.SmartAlarm` and `Util.TryGetServerPairingData()` are intentional to match Rust+ companion behavior.
- Patch target signature must stay `BaseCombatEntity.Die(HitInfo)`.
- Avoid heavy scans in patch path; logic runs on every combat entity death.

## Build & Deploy

```powershell
.\build.ps1
```

- Output DLL: `D:\!RustServer\HarmonyMods\RaidRustPlus.dll`
- Load: `harmony.load RaidRustPlus`
- Unload: `harmony.unload RaidRustPlus`
