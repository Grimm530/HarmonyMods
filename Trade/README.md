# Trade (Harmony)

Oxide **Trade 1.2.15** port. Player-to-player shop-front trading.

## Load order

1. **0Permissions**
2. **Trade**

## Deploy

```powershell
.\build.ps1
```

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/Trade.json` |
| Data / logs | `HarmonyData/Trade/Log.txt` |
| Lang | `HarmonyLanguage/Trade.json` |

## Commands

- `/trade <name>` — request
- `/trade yes` / `accept` / `+`
- `/trade no` / `cancel` / `-`

## API

```
AppDomain.CurrentDomain.GetData("Trade_ApiType") // typeof(TradeMod)
TradeMod.IsTradeBox(entity)
```
