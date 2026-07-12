# ShorterNights

Harmony mod that makes nights ~1/3 length by speeding up time passage during night. Uses `TOD_Time.OnSunrise`/`OnSunset` event subscription (like the TimeOfDay oxide plugin). Optional config to disable the time-of-day display on screen.

## Mod Identity

| Item | Value |
|------|-------|
| **Purpose** | Speed up night so nights pass in ~1/3 real time |
| **Entry point** | `ShorterNightsMod` implements `IHarmonyModHooks` |
| **Night length** | 10 real minutes (vs 30 default) = ~3× faster |

## Config

Config file: `HarmonyConfig/ShorterNights.json`

| Option | Default | Description |
|--------|---------|-------------|
| Show time of day display on screen | `true` | When `true`, displays game time (e.g. `TOD: 14:30`) under the hotbar. Set to `false` to hide. |

## Project Structure

| File | Responsibility |
|------|----------------|
| `ShorterNightsMod.cs` | Lifecycle, subscribes to OnSunrise/OnSunset, sets DayLengthInMinutes per phase |
| `ShorterNightsConfig.cs` | Loads/saves config from HarmonyConfig/ShorterNights.json |

## Mechanism

- Subscribes to `TOD_Sky.Instance.Components.Time.OnSunrise` and `OnSunset`
- On sunrise: `DayLengthInMinutes = 30 * (24 / daySpan)` (normal day)
- On sunset: `DayLengthInMinutes = 10 * (24 / (24 - daySpan))` (3× faster night)

References `Assembly-CSharp-firstpass` (where `TOD_Time`/`TOD_Sky` live).

## Build & Deploy

```powershell
cd .cursor\HarmonyMods\ShorterNights
.\build.ps1
```

Output: `D:\!RustServer\HarmonyMods\ShorterNights.dll`

Load: `harmony.load ShorterNights`
