# Economics Harmony Mod (Extended 3.10.4)

Oxide-free Harmony port of **Economics Extended 3.10.4** (SQLite balances, RP tracking, Discord webhooks).

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/Economics.json` |
| Default data | `HarmonyData/Economics/` |
| Custom data (this server) | `C:\!DataPersistence\oxide\data\Economics` (set in config) |
| SQLite DB (this server) | `C:\!DataPersistence\economics_balances.db` |
| Logs | `HarmonyData/Economics/logs/` |

## Dependencies

1. **0Permissions.dll** — load before Economics (`harmony.load 0Permissions` then `harmony.load Economics`).
2. **Facepunch.Sqlite** — used when `"Balance storage mode"` is `Sqlite`. Already shipped with the server as `RustDedicated_Data/Managed/Facepunch.Sqlite.dll` (+ native `sqlite3`). No `System.Data.SQLite` required.

Existing `economics_balances.db` schema is reused. If Facepunch.Sqlite fails at runtime, Economics falls back to JSON file storage instead of failing `OnLoaded`.

## Permissions

- `economics.balance`
- `economics.deposit` / `economics.depositall`
- `economics.setbalance` / `economics.setbalanceall`
- `economics.transfer` / `economics.transferall`
- `economics.withdraw` / `economics.withdrawall`
- `economics.wipe`

## Commands (chat `/` and console)

- `balance`, `deposit`, `SetBalance`, `transfer`, `withdraw`
- `ecowipe`, `ecopurge`, `ecostats`
- `testdiscord`, `testdiscorddirect`, `testwebhook`
- `ecodailysummary`, `ecoperiodicreport`

## API for other Harmony mods

```csharp
// AppDomain type
var t = AppDomain.CurrentDomain.GetData("Economics_ApiType") as Type;
// Static: Balance / Deposit / SetBalance / Transfer / Withdraw

// Plugin wrapper (RaidableBases-style Call)
var plugin = AppDomain.CurrentDomain.GetData("Economics_Plugin");
plugin.GetType().GetMethod("Call")?.Invoke(plugin, new object[] { "Deposit", playerId, amount });
```

## Build

```powershell
.\.cursor\HarmonyMods\Economics\build.ps1
```

Copies only `Economics.dll` to `HarmonyMods/`.
