# Radio (Harmony)

Combined port of Oxide **Radio 1.2.0** (Karuza voice/phone) and **VehicleRadio 1.0.5** (boombox on minicopter / attack heli / tugboat).

## Features

- Global radio telephone number (config). Dialing it joins a voice party-line.
- `/GiveGlobalPhone` gives a mobile phone with the global number saved (perm `Radio.GiveGlobalPhone`)
- Other Harmony mods can register `IRadio` via `RadioHarmony.RadioMod.RegisterRadio` / `RemoveRadio`
- Boomboxes parented to minicopters, attack helicopters, and tugboats
- Auto-install on vehicle spawn (`VehicleRadio.Auto Install On Spawn`, default true)
- Manual `/radio` install and `/rradio` remove (perm `vehicleradio.use`)

## Config / data

- Config: `HarmonyConfig/Radio.json` (Karuza fields + nested `VehicleRadio`)
- Data: `HarmonyData/Radio/VehicleRadio.json`

## Harmony patches

| Method | Kind | Notes |
|--------|------|--------|
| `ServerMgr.OnPlayerVoice` | prefix observer | Copies voice bytes, does not skip original |
| `PhoneController.CallPhone` | prefix blocker | Only for the global radio number |
| `PhoneController.OnDialFailed` | postfix | Cleanup |
| `BaseNetworkable.Spawn` | postfix | MiniCopter / AttackHelicopter / Tugboat boombox |
| `BaseNetworkable.Kill` | prefix | Remove tracked boombox |
| `Chat.say` | prefix | `/GiveGlobalPhone` `/radio` `/rradio` |

Load order: **0Permissions → Radio**.
