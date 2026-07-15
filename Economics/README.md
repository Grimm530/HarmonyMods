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
2. **System.Data.SQLite.dll** — required at runtime when `"Balance storage mode"` is `Sqlite`. Place in `RustDedicated_Data/Managed/`. Copy from:
   - `.cursor/HarmonyMods/Rust-Server-Metrics-master/deps/windows/System.Data.SQLite.dll`, or
   - `.cursor/Oxide.Plugins.Cant-Use/Oxide/Oxide.SQLite-master/src/Dependencies/System.Data.SQLite.dll`

The build references SQLite for compile only (`Private=false`); it is **not** copied into `HarmonyMods/`.

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
